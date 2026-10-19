using System.Security.Cryptography;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BattleplanADC;

public sealed class UnitChoice
{
    public string Tier { get; set; } = "";
    public string Branch { get; set; } = "";
    public string Policy { get; set; } = "Default";
    public int? Number { get; set; }
    public bool NumberOverride { get; set; }
}

public sealed class NamingRule
{
    public string Tier { get; set; } = "";
    public string Branch { get; set; } = "";
    public string Pattern { get; set; } = "";
    public int Start { get; set; } = 1;
    public string Sequence { get; set; } = "Global";
}

public sealed class NumberReservation
{
    public string Tier { get; set; } = "";
    public int Number { get; set; }
    public string UnitId { get; set; } = "";
}

public sealed class NamingCatalog
{
    // Sidecar state is editor-only. Unit IDs make choices stable across renames and tree moves;
    // the normalized campaign path keeps two saves with the same filename separate.
    public int Version { get; set; } = 1;
    public string CampaignPath { get; set; } = "";
    public Dictionary<string, UnitChoice> Units { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<NamingRule> Rules { get; set; } = [];
    public List<NumberReservation> ReservedNumbers { get; set; } = [];
    public static string FileFor(string campaignPath, string directory)
    {
        var path = Path.GetFullPath(campaignPath).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..16];
        return Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(path)}-{hash}.json");
    }
    public static NamingCatalog Load(string campaignPath, string directory)
    {
        var file = FileFor(campaignPath, directory);
        if (!File.Exists(file)) return new NamingCatalog { CampaignPath = Path.GetFullPath(campaignPath) };
        var catalog = JsonSerializer.Deserialize<NamingCatalog>(File.ReadAllText(file)) ?? throw new InvalidDataException("Empty naming catalog.");
        if (catalog.Version != 1 || !string.Equals(catalog.CampaignPath, Path.GetFullPath(campaignPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Naming catalog belongs to a different campaign or version.");
        return catalog;
    }
    public void Save(string directory)
    {
        // Write the sidecar through a temporary file so a failed write does not truncate it.
        Directory.CreateDirectory(directory);
        var file = FileFor(CampaignPath, directory);
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        File.Move(temp, file, true);
    }
    public UnitChoice Choice(Unit unit)
    {
        if (!Units.TryGetValue(unit.Id, out var choice)) Units[unit.Id] = choice = new UnitChoice();
        return choice;
    }
    public NamingRule? Rule(string tier, string branch) => Rules.FirstOrDefault(r => r.Tier.Equals(tier, StringComparison.OrdinalIgnoreCase) && r.Branch.Equals(branch, StringComparison.OrdinalIgnoreCase));
    public void SetRule(NamingRule rule)
    {
        Rules.RemoveAll(r => r.Tier.Equals(rule.Tier, StringComparison.OrdinalIgnoreCase) && r.Branch.Equals(rule.Branch, StringComparison.OrdinalIgnoreCase));
        Rules.Add(rule);
    }
}

public sealed class CatalogRow
{
    public required Unit Unit { get; init; }
    public required UnitChoice Choice { get; init; }
    public string Current => Unit.Name;
    public string GameType => Unit.TypeName;
    public string Parent => Unit.Parent?.Name ?? "";
    public string Tier => Choice.Tier;
    public string Branch => Choice.Branch;
    public string Policy => Choice.Policy;
    public string Number => Choice.Number?.ToString() ?? "";
    public string Id => Unit.Id;
}

public static class NamingPatterns
{
    // Inference deliberately looks only at user-provided examples. OriginalName is generated
    // by the game and does not reveal the player's intended command hierarchy or syntax.
    private static readonly Regex Ordinal = new(@"^(\d+)(st|nd|rd|th)\b", RegexOptions.IgnoreCase);
    private static readonly Regex ParentSlot = new(@"^(\d+)-(\d+)(\b|(?=\s))");
    public static (string? Pattern, string Explanation) Infer(string example)
    {
        example = example.Trim();
        var pair = ParentSlot.Match(example);
        if (pair.Success) return ("{slot}-{parentNumber}" + example[pair.Length..], "Two numbers may mean child slot and parent number. Confirm the pattern and assign a numbered parent tier.");
        var ordinal = Ordinal.Match(example);
        if (ordinal.Success) return ("{n:ordinal}" + example[ordinal.Length..], "Ordinal sequence inferred. Preview to confirm the wording.");
        var leading = Regex.Match(example, @"^\d+");
        if (leading.Success) return ("{n}" + example[leading.Length..], "Leading number inferred. Preview to confirm the wording.");
        return (null, "I could not identify a changing number in this name. Check that the examples are user-entered names for the same tier and branch. Add two consecutively numbered examples, or enter an advanced pattern using {n}, {n:ordinal}, {slot}, or {parentNumber}.");
    }
    public static (string? Pattern, string Explanation) InferExamples(string examples)
    {
        // Multiple examples must form a consecutive sequence. One plausible match alone is
        // offered as a suggestion, not proof of the user's naming convention.
        var names = examples.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (names.Length == 0) return (null, "Enter a user-entered name example, or select named units and use 'Use selected user names'. Assign their tier and branch first so the pattern has a clear target.");
        var result = Infer(names[0]);
        if (result.Pattern is null || names.Length == 1) return result;
        var firstPair = ParentSlot.Match(names[0]);
        var parent = firstPair.Success ? int.Parse(firstPair.Groups[2].Value) : (int?)null;
        var firstNumber = firstPair.Success ? int.Parse(firstPair.Groups[1].Value) : int.Parse(Regex.Match(names[0], @"^\d+").Value);
        for (var i = 1; i < names.Length; i++)
        {
            var expected = Render(result.Pattern, firstNumber + i, firstNumber + i, parent);
            if (!string.Equals(names[i], expected, StringComparison.OrdinalIgnoreCase))
                return (null, $"Example {i + 1} does not follow the proposed sequence ({expected}). Check that all examples belong to the same tier and branch and are ordered by OOB position. Batch assign tiers and branches, then select consecutive examples; otherwise edit the pattern manually.");
        }
        return (result.Pattern, $"Pattern agrees with {names.Length} consecutive examples. Confirm the assigned tier and branch before saving.");
    }
    public static string Render(string pattern, int number, int slot, int? parentNumber)
    {
        if (pattern.Contains("{parentNumber}") && parentNumber is null) throw new InvalidOperationException("No numbered ancestor is available for {parentNumber}. Assign the parent HQ's tier and branch, save its numbering scheme, and preview again.");
        var result = pattern.Replace("{n:ordinal}", OrdinalNumber(number)).Replace("{n}", number.ToString()).Replace("{slot}", slot.ToString()).Replace("{parentNumber}", parentNumber?.ToString() ?? "");
        if (Regex.IsMatch(result, @"\{[^{}]+\}")) throw new InvalidOperationException("Unknown pattern token.");
        return result.Trim();
    }
    private static string OrdinalNumber(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
}

public sealed class NamingProposal
{
    public required Unit Unit { get; init; }
    public required string Before { get; init; }
    public string After { get; set; } = "";
    public string Tier { get; init; } = "";
    public string Branch { get; init; } = "";
}

public static class CatalogNamingService
{
    // Advanced rule preview is separate from Whole army's scattered-number planner.
    // It uses classified tier/branch pairs and saved patterns across the campaign tree.
    public static (List<NamingProposal> Proposals, List<string> Problems) Preview(Campaign campaign, NamingCatalog catalog)
    {
        var proposals = new List<NamingProposal>();
        var problems = new List<string>();
        var counters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var assignedNumbers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var siblings = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var unit in campaign.Units)
        {
            if (!catalog.Units.TryGetValue(unit.Id, out var choice) || choice.Policy == "Exclude" || choice.Tier.Length == 0 || choice.Branch.Length == 0) continue;
            var rule = catalog.Rule(choice.Tier, choice.Branch);
            if (rule is null || rule.Pattern.Length == 0) continue;
            var parentKey = unit.Parent?.Id ?? "root";
            var key = $"{choice.Tier}|{choice.Branch}|{(rule.Sequence == "Per parent" ? parentKey : "all")}";
            var next = counters.TryGetValue(key, out var used) ? used + 1 : rule.Start;
            counters[key] = next;
            var number = choice.Number ?? next;
            assignedNumbers[unit.Id] = number;
            siblings[parentKey] = siblings.TryGetValue(parentKey, out var slot) ? slot + 1 : 1;
            int? parentNumber = null;
            for (var p = unit.Parent; p is not null; p = p.Parent)
                if (assignedNumbers.TryGetValue(p.Id, out var n)) { parentNumber = n; break; }
            try
            {
                var after = NamingPatterns.Render(rule.Pattern, number, siblings[parentKey], parentNumber);
                if (after.Length == 0) problems.Add($"Blank name for {unit.Id}.");
                else if (after != unit.Name) proposals.Add(new NamingProposal { Unit = unit, Before = unit.Name, After = after, Tier = choice.Tier, Branch = choice.Branch });
            }
            catch (InvalidOperationException ex) { problems.Add($"{unit.Name}: {ex.Message}"); }
        }
        var finalNames = campaign.Units.Select(u => (u.Id, Name: proposals.FirstOrDefault(p => p.Unit == u)?.After ?? u.Name)).ToList();
        foreach (var duplicate in finalNames.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Key.Length > 0 && g.Count() > 1))
            problems.Add($"Duplicate final name: {duplicate.Key} ({duplicate.Count()} units).");
        return (proposals, problems);
    }
}
