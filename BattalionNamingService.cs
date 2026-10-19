using System.Text.RegularExpressions;

namespace BattleplanADC;

public sealed class BattalionRenameRow
{
    public required Unit Unit { get; init; }
    public required string Before { get; init; }
    public bool Use { get; set; }
    public int Slot { get; set; }
    public string Suffix { get; set; } = "";
    public string Proposed { get; set; } = "";
    public string GameType => Unit.TypeName;
}

public static class BattalionNamingService
{
    // The one-regiment tool handles immediate children. The whole-army planner separately
    // walks nested attached HQs when cascading a changed regiment number.
    public const string DefaultPattern = "{slot}-{regiment} {suffix}";
    public static int? UserNumber(Unit command)
    {
        var match = Regex.Match(command.UserTypedName, @"^\s*(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }
    public static List<BattalionRenameRow> Children(Unit command, NamingCatalog? catalog)
    {
        // Slots 1-3 are infantry, 4 artillery, 5 AT/TD, and 6 engineers. Further units
        // receive 7 onward in ChildGroups order. Every proposed slot remains editable.
        var infantry = 0;
        var extra = 7;
        var occupied = new HashSet<int>();
        var rows = new List<BattalionRenameRow>();
        foreach (var unit in command.Children)
        {
            var slot = unit.Type switch
            {
                0 or 12 or 14 when infantry < 3 => ++infantry,
                6 or 18 when !occupied.Contains(4) => 4,
                5 or 20 when !occupied.Contains(5) => 5,
                4 or 19 when !occupied.Contains(6) => 6,
                _ => extra++
            };
            occupied.Add(slot);
            var excluded = catalog?.Units.TryGetValue(unit.Id, out var choice) == true && choice.Policy == "Exclude";
            rows.Add(new BattalionRenameRow { Unit = unit, Before = unit.Name, Use = unit.Type != 11 && !excluded, Slot = slot, Suffix = Suffix(unit.Type) });
        }
        return rows;
    }
    public static string Suffix(int type) => type switch
    {
        0 or 12 or 14 => "INF", 6 => "ART", 18 => "SPART", 5 => "AT", 20 => "TD", 4 => "ENG", 19 => "ARMENG", 3 or 15 or 17 => "ARM", 1 => "CAV", _ => "DET"
    };
    public static string Branch(int type) => type switch
    {
        0 or 12 or 14 => "Infantry", 6 or 18 => "Artillery", 5 or 20 => "Anti-Tank", 4 or 19 => "Engineers", 3 or 15 or 17 => "Armor", 1 => "Cavalry", _ => "Other"
    };
    public static string Render(string pattern, int slot, int regimentNumber, string suffix)
    {
        var value = pattern.Replace("{slot}", slot.ToString()).Replace("{regiment}", regimentNumber.ToString()).Replace("{suffix}", suffix.Trim()).Trim();
        if (Regex.IsMatch(value, @"\{[^{}]+\}")) throw new InvalidOperationException("Unknown token. Use {slot}, {regiment}, or {suffix}.");
        return value;
    }
}
