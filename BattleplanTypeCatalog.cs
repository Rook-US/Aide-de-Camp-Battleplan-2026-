namespace BattleplanADC;

// Values read from this installation's IL2CPP Unit_Type and Country enums.
// The save stores numeric Type/Country IDs. No per-unit equipment model was found in the sampled .camp file.
public static class BattleplanTypeCatalog
{
    private static readonly IReadOnlyDictionary<int, string> Types = new Dictionary<int, string>
    {
        [0] = "Infantry", [1] = "Scouts", [2] = "Snipers", [3] = "Tanks",
        [4] = "Engineers", [5] = "Anti-tank guns", [6] = "Artillery",
        [7] = "Any (game filter)", [8] = "Any except tanks (game filter)",
        [9] = "Naval artillery", [10] = "Air support", [11] = "Headquarters",
        [12] = "Mechanized infantry", [13] = "Anti-air", [14] = "Paratroopers",
        [15] = "Light tanks", [17] = "Heavy tanks", [18] = "Self-propelled artillery",
        [19] = "Mechanized engineers", [20] = "Self-propelled anti-tank guns", [21] = "None"
    };
    private static readonly IReadOnlyDictionary<int, string> Countries = new Dictionary<int, string>
    {
        [0] = "British", [1] = "Canadian", [2] = "US", [3] = "German", [4] = "Italian"
    };
    public static string TypeName(int id) => Types.TryGetValue(id, out var name) ? name : $"Unknown type {id}";
    public static string CountryName(int id) => Countries.TryGetValue(id, out var name) ? name : $"Unknown country {id}";
    public static string DefaultNamingSuffix(int id) => id switch
    {
        0 => "INF", 4 => "ENG", 5 => "AT", 6 => "ART", 18 => "SPART", 19 => "ARMENG", 20 => "TD", _ => "manual"
    };
}
