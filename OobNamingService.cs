using System.Text.RegularExpressions;

namespace BattleplanADC;

public sealed class RegimentEntry
{
    public required Unit Unit { get; init; }
    public bool Include { get; set; } = true;
    public string Number { get; set; } = "";
    public string Suffix { get; set; } = "IR";
    public string Current => Unit.Name;
    public string Command => Unit.Parent?.Name ?? "";
    public int Children => Unit.Children.Count;
}

public sealed class NameProposal
{
    public required Unit Unit { get; init; }
    public required string Before { get; init; }
    public required string Role { get; init; }
    public string After { get; set; } = "";
    public string Path => Unit.Path;
}

public static class OobNamingService
{
    // Historical planner kept for the hidden OOB tab and shared final-name validation.
    // Its discovery heuristic can consult OriginalName; do not use it for Whole army's
    // player-controlled HQ classification or naming inference.
    private static readonly Regex Abbreviation = new(@"^\s*(\d+)\s*(IR|AR|CR|REG|R)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OriginalNumber = new(@"^\s*(\d+)(?:st|nd|rd|th)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<RegimentEntry> Discover(Campaign campaign) => campaign.Units
        .Where(u => u.Type == 11 && !u.Organizational && u.Children.Any(c => c.Type != 11) &&
                    (Abbreviation.IsMatch(u.UserTypedName) ||
                     (string.IsNullOrWhiteSpace(u.UserTypedName) &&
                      u.OriginalName.Contains("Regiment", StringComparison.OrdinalIgnoreCase))))
        .Select(u =>
        {
            var abbreviated = Abbreviation.Match(u.UserTypedName);
            var original = OriginalNumber.Match(u.OriginalName);
            return new RegimentEntry
            {
                Unit = u,
                Number = abbreviated.Success ? abbreviated.Groups[1].Value : original.Success ? original.Groups[1].Value : "",
                Suffix = abbreviated.Success ? abbreviated.Groups[2].Value.ToUpperInvariant() : "IR"
            };
        }).ToList();

    public static (List<NameProposal> Proposals, List<string> Problems) Build(
        Campaign campaign, IEnumerable<RegimentEntry> entries, Unit? scopeRoot, bool renameRegiments,
        bool includeOther, string otherSuffix, IReadOnlySet<int>? includedTypes = null)
    {
        var proposals = new List<NameProposal>();
        var problems = new List<string>();
        var regimentIds = entries.Select(e => e.Unit.Id).ToHashSet();
        foreach (var entry in entries.Where(e => e.Include && (scopeRoot is null || IsDescendant(e.Unit, scopeRoot))))
        {
            if (!int.TryParse(entry.Number, out var regimentNumber) || regimentNumber <= 0)
            {
                problems.Add($"{entry.Current}: regiment number must be a positive integer.");
                continue;
            }
            var suffix = entry.Suffix.Trim().ToUpperInvariant();
            if (suffix.Length == 0 || suffix.Any(c => !char.IsLetterOrDigit(c)))
            {
                problems.Add($"{entry.Current}: regiment suffix must use letters or digits.");
                continue;
            }
            if (renameRegiments) Add(entry.Unit, $"{regimentNumber}{suffix}", "Regiment");

            int infantry = 0, artillery = 0, antiTank = 0, engineers = 0, extra = 7;
            foreach (var child in CombatDescendants(entry.Unit))
            {
                int slot;
                string role;
                string branch;
                switch (child.Type)
                {
                    case 0:
                        infantry++;
                        slot = infantry <= 3 ? infantry : extra++;
                        role = "Infantry"; branch = "INF"; break;
                    case 6:
                    case 18:
                        artillery++;
                        slot = artillery == 1 ? 4 : extra++;
                        role = BattleplanTypeCatalog.TypeName(child.Type); branch = child.Type == 18 ? "SPART" : "ART"; break;
                    case 5:
                    case 20:
                        antiTank++;
                        slot = antiTank == 1 ? 5 : extra++;
                        role = BattleplanTypeCatalog.TypeName(child.Type);
                        branch = child.Type == 5 ? "AT" : "TD";
                        break;
                    case 4:
                    case 19:
                        engineers++;
                        slot = engineers == 1 ? 6 : extra++;
                        role = BattleplanTypeCatalog.TypeName(child.Type);
                        branch = child.Type == 19 ? "ARMENG" : "ENG";
                        break;
                    default:
                        if (!includeOther) continue;
                        slot = extra++; role = $"Other type {child.Type}"; branch = otherSuffix.Trim().ToUpperInvariant();
                        if (branch.Length == 0) { problems.Add($"{child.Name}: other-detachment suffix is empty."); continue; }
                        break;
                }
                if (includedTypes is null || includedTypes.Contains(child.Type))
                    Add(child, $"{slot}-{regimentNumber} {branch}", role);
            }
        }
        return (proposals, problems);

        void Add(Unit unit, string name, string role)
        {
            if (unit.Name != name) proposals.Add(new NameProposal { Unit = unit, Before = unit.Name, After = name, Role = role });
        }
        IEnumerable<Unit> CombatDescendants(Unit command)
        {
            foreach (var child in command.Children)
            {
                if (child.Type == 11 && regimentIds.Contains(child.Id)) continue;
                if ((child.Type == 11 || child.Organizational) && child.Children.Count > 0)
                {
                    foreach (var nested in CombatDescendants(child)) yield return nested;
                }
                else yield return child;
            }
        }
    }

    public static List<string> ValidateFinalNames(Campaign campaign, IEnumerable<NameProposal> proposals)
    {
        var changes = proposals.ToList();
        var errors = new List<string>();
        if (changes.Any(p => string.IsNullOrWhiteSpace(p.After))) errors.Add("Proposed names cannot be blank.");
        if (changes.Any(p => p.Unit.Name != p.Before)) errors.Add("A unit name changed since preview. Preview again.");
        var proposedById = changes.ToDictionary(x => x.Unit.Id, x => x.After.Trim());
        var finalNames = campaign.Units.Select(u => (Unit: u, Name: proposedById.TryGetValue(u.Id, out var n) ? n : u.Name)).ToList();
        foreach (var changed in changes)
        {
            var matching = finalNames.Where(x => x.Name.Equals(proposedById[changed.Unit.Id], StringComparison.OrdinalIgnoreCase)).ToList();
            if (matching.Count > 1)
                errors.Add($"Duplicate name '{proposedById[changed.Unit.Id]}' ({matching.Count} units). Change regiment numbers or edit proposed names.");
        }
        return errors.Distinct().ToList();
    }

    private static bool IsDescendant(Unit unit, Unit root)
    {
        for (var p = unit; p is not null; p = p.Parent) if (p == root) return true;
        return false;
    }
}
