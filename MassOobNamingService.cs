using System.Text.RegularExpressions;

namespace BattleplanADC;

public sealed class MassOobRow
{
    public required Unit Unit { get; init; }
    public required string Before { get; init; }
    public string Tier { get; init; } = "";
    public string GameType => Unit.TypeName;
    public string Current => Before;
    public string Proposed { get; set; } = "";
    public bool Use { get; set; } = true;
    public string Reason { get; init; } = "";
    public int? Number { get; init; }
}

public sealed class MassOobPlan
{
    public List<MassOobRow> Rows { get; } = [];
    public List<string> Problems { get; } = [];
    public int NewDivisionNumbers { get; set; }
    public int NewRegimentNumbers { get; set; }
    public int PreservedNumbers { get; set; }
}

public static class MassOobNamingService
{
    // Build mutates catalog assignments/reservations but never campaign names. The UI saves
    // the catalog on preview and writes UserTypedName only after a checked apply.
    public static MassOobPlan Build(Campaign campaign, NamingCatalog catalog, IEnumerable<Unit> scope, string childPattern, Func<int, int>? randomIndex = null)
    {
        var plan = new MassOobPlan();
        var scoped = scope.ToList();
        var scopedIds = scoped.Select(u => u.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assignments = new List<NumberAssignment>();
        foreach (var tier in new[] { "Division", "Regiment" })
        {
            try { assignments.AddRange(ScatteredNumberService.Assign(campaign, catalog, scoped, tier, randomIndex)); }
            catch (InvalidOperationException ex) { plan.Problems.Add(ex.Message); }
            var added = assignments.Count(a => a.Tier == tier && a.NewlyAllocated);
            if (tier == "Division") plan.NewDivisionNumbers = added;
            else plan.NewRegimentNumbers = added;
        }
        var numbered = assignments.ToDictionary(a => a.Unit.Id, a => a.Number, StringComparer.OrdinalIgnoreCase);
        foreach (var unit in campaign.EditableUnits)
            if (!numbered.ContainsKey(unit.Id) && catalog.Units.TryGetValue(unit.Id, out var choice) && choice.Number is > 0)
                numbered[unit.Id] = choice.Number.Value;
        var rowsById = new Dictionary<string, MassOobRow>(StringComparer.OrdinalIgnoreCase);
        // Preserve an existing user name when its number is still valid. Generate a new
        // formation name only for an unnumbered, repeated, or conflicting assignment.
        foreach (var assignment in assignments)
        {
            var unit = assignment.Unit;
            var typedNumber = ScatteredNumberService.NumberInUserName(unit);
            var preserve = !string.IsNullOrWhiteSpace(unit.UserTypedName) &&
                (typedNumber is null || typedNumber == assignment.Number);
            if (preserve) { plan.PreservedNumbers++; continue; }
            var choice = catalog.Choice(unit);
            var rule = catalog.Rule(assignment.Tier, choice.Branch);
            var observedPattern = rule is null ? ExistingPattern(campaign, catalog, assignment.Tier, choice.Branch) : null;
            var pattern = rule?.Pattern ?? observedPattern ?? (assignment.Tier == "Division"
                ? choice.Branch is "Infantry" or "Headquarters" ? "{n:ordinal} Infantry Division" : $"{{n:ordinal}} {choice.Branch} Division"
                : choice.Branch is "Infantry" or "Headquarters" ? "{n}IR" : "{n} Regiment");
            int? parentNumber = null;
            for (var p = unit.Parent; p is not null; p = p.Parent)
                if (numbered.TryGetValue(p.Id, out var n)) { parentNumber = n; break; }
            try
            {
                var proposed = NamingPatterns.Render(pattern, assignment.Number, 1, parentNumber);
                rowsById[unit.Id] = new MassOobRow { Unit = unit, Before = unit.Name, Tier = assignment.Tier, Proposed = proposed, Number = assignment.Number,
                    Reason = (typedNumber is not null && typedNumber != assignment.Number ? "Repeated or conflicting number" : "Unused number reserved") +
                        (observedPattern is not null ? "; format from user names" : "") };
            }
            catch (InvalidOperationException ex) { plan.Problems.Add($"{unit.Name}: {ex.Message}"); }
        }
        // Renumber all combat descendants of a changed regiment, including units placed
        // under attached HQs. A nested classified regiment/division is a boundary.
        foreach (var assignment in assignments.Where(a => a.Tier == "Regiment" && rowsById.ContainsKey(a.Unit.Id)))
        {
            var infantry = 0; var extra = 7; var occupied = new HashSet<int>();
            foreach (var child in CombatDescendants(assignment.Unit, catalog))
            {
                if (!scopedIds.Contains(child.Id)) continue;
                if (catalog.Units.TryGetValue(child.Id, out var choice) && choice.Policy == "Exclude") continue;
                var slot = child.Type switch
                {
                    0 or 12 or 14 when infantry < 3 => ++infantry,
                    6 or 18 when !occupied.Contains(4) => 4,
                    5 or 20 when !occupied.Contains(5) => 5,
                    4 or 19 when !occupied.Contains(6) => 6,
                    _ => extra++
                };
                occupied.Add(slot);
                try
                {
                    var proposed = BattalionNamingService.Render(childPattern, slot, assignment.Number, BattalionNamingService.Suffix(child.Type));
                    if (proposed != child.Name)
                        rowsById[child.Id] = new MassOobRow { Unit = child, Before = child.Name, Tier = catalog.Units.TryGetValue(child.Id, out var c) && c.Tier.Length > 0 ? c.Tier : "Child unit",
                            Proposed = proposed, Number = slot, Reason = "Parent regiment number changed" };
                }
                catch (InvalidOperationException ex) { plan.Problems.Add($"{child.Name}: {ex.Message}"); }
            }
        }
        var currentDuplicateIds = campaign.EditableUnits.GroupBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Key.Length > 0 && g.Count() > 1).SelectMany(g => g.Select(u => u.Id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Support formations often carry the parent division number in a free-form user
        // name. Replace only that leading number, and show the result for review.
        foreach (var assignment in assignments.Where(a => a.Tier == "Division"))
        {
            var divisionChanged = rowsById.ContainsKey(assignment.Unit.Id);
            foreach (var child in SupportDescendants(assignment.Unit, catalog))
            {
                if (!scopedIds.Contains(child.Id) || rowsById.ContainsKey(child.Id)) continue;
                if (catalog.Units.TryGetValue(child.Id, out var choice) && choice.Policy == "Exclude") continue;
                if (!divisionChanged && !currentDuplicateIds.Contains(child.Id)) continue;
                var typed = child.UserTypedName;
                var prefix = Regex.Match(typed, @"^\s*(\d+)(st|nd|rd|th)?", RegexOptions.IgnoreCase);
                if (!prefix.Success || !int.TryParse(prefix.Groups[1].Value, out var oldNumber) || oldNumber == assignment.Number) continue;
                var replacement = prefix.Groups[2].Success
                    ? NamingPatterns.Render("{n:ordinal}", assignment.Number, 1, null)
                    : assignment.Number.ToString();
                var proposed = replacement + typed[prefix.Length..];
                if (proposed != child.Name)
                    rowsById[child.Id] = new MassOobRow { Unit = child, Before = child.Name,
                        Tier = catalog.Units.TryGetValue(child.Id, out var c) && c.Tier.Length > 0 ? c.Tier : "Attached unit",
                        Proposed = proposed, Reason = "Division number cascade; review", Number = assignment.Number };
            }
        }
        var ruleCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var unit in scoped)
        {
            if (!catalog.Units.TryGetValue(unit.Id, out var choice) || choice.Policy == "Exclude" || choice.Tier is "Division" or "Regiment" || choice.Tier.Length == 0) continue;
            var rule = catalog.Rule(choice.Tier, choice.Branch);
            if (rule is null || rule.Pattern.Length == 0) continue;
            var key = $"{choice.Tier}|{choice.Branch}|{(rule.Sequence == "Per parent" ? unit.Parent?.Id : "all")}";
            var slot = ruleCounters.TryGetValue(key, out var used) ? used + 1 : rule.Start;
            ruleCounters[key] = slot;
            int? parentNumber = null;
            for (var p = unit.Parent; p is not null; p = p.Parent)
                if (numbered.TryGetValue(p.Id, out var n)) { parentNumber = n; break; }
            var ancestorChanged = false;
            for (var p = unit.Parent; p is not null; p = p.Parent)
                if (rowsById.ContainsKey(p.Id)) { ancestorChanged = true; break; }
            if (!ancestorChanged && !rowsById.ContainsKey(unit.Id)) continue;
            try
            {
                var proposed = NamingPatterns.Render(rule.Pattern, choice.Number ?? slot, slot, parentNumber);
                if (proposed != unit.Name)
                    rowsById[unit.Id] = new MassOobRow { Unit = unit, Before = unit.Name, Tier = choice.Tier, Proposed = proposed,
                        Number = choice.Number, Reason = "Saved tier/branch naming scheme" };
            }
            catch (InvalidOperationException ex) { plan.Problems.Add($"{unit.Name}: {ex.Message}"); }
        }
        // Surface duplicates that the rules could not resolve. They start unchecked so
        // the editor never silently invents a name or command level.
        var plannedNames = campaign.EditableUnits.Select(u => (Unit: u, Name: rowsById.TryGetValue(u.Id, out var row) && row.Use ? row.Proposed : u.Name)).ToList();
        var remainingDuplicates = plannedNames.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Key.Length > 0 && g.Count() > 1).SelectMany(g => g.Select(x => x.Unit.Id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var unit in scoped.Where(u => remainingDuplicates.Contains(u.Id) && !rowsById.ContainsKey(u.Id)))
        {
            if (catalog.Units.TryGetValue(unit.Id, out var choice) && choice.Policy == "Exclude") continue;
            // Show remaining duplicates for direct review, but do not invent a name or tier.
            rowsById[unit.Id] = new MassOobRow { Unit = unit, Before = unit.Name, Tier = catalog.Units.TryGetValue(unit.Id, out var c) ? c.Tier : "", Proposed = "", Use = false,
                Reason = "Existing duplicate: enter a unique name or assign tier" };
        }
        foreach (var unit in scoped)
            if (rowsById.TryGetValue(unit.Id, out var row)) plan.Rows.Add(row);
        return plan;
    }
    private static IEnumerable<Unit> CombatDescendants(Unit command, NamingCatalog catalog)
    {
        foreach (var child in command.Children)
        {
            if (catalog.Units.TryGetValue(child.Id, out var choice) && choice.Tier is "Regiment" or "Division") continue;
            if (child.Type == 11 && child.Children.Count > 0)
            {
                foreach (var nested in CombatDescendants(child, catalog)) yield return nested;
            }
            else if (child.Type != 11) yield return child;
        }
    }
    private static IEnumerable<Unit> SupportDescendants(Unit command, NamingCatalog catalog)
    {
        foreach (var child in command.Children)
        {
            if (catalog.Units.TryGetValue(child.Id, out var choice) && choice.Tier is "Regiment" or "Division") continue;
            yield return child;
            foreach (var nested in SupportDescendants(child, catalog)) yield return nested;
        }
    }
    private static string? ExistingPattern(Campaign campaign, NamingCatalog catalog, string tier, string branch)
    {
        return campaign.EditableUnits
            .Where(u => catalog.Units.TryGetValue(u.Id, out var c) && c.Tier == tier && c.Branch == branch && !string.IsNullOrWhiteSpace(u.UserTypedName))
            .Select(u => NamingPatterns.Infer(u.UserTypedName).Pattern)
            .Where(p => p is not null && !p.Contains("{parentNumber}"))
            .GroupBy(p => p!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Key).FirstOrDefault();
    }
}
