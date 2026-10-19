using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

namespace BattleplanADC;

public sealed class Unit
{
    // Keep the original JsonObject attached so saving changes only explicit fields and
    // preserves game fields that this editor does not understand.
    public required JsonObject Data { get; init; }
    public required string Path { get; init; }
    public required string Id { get; init; }
    public Unit? Parent { get; init; }
    public List<Unit> Children { get; } = [];
    public string OriginalName => Data["OriginalName"]?.GetValue<string>() ?? "";
    public string UserTypedName
    {
        get => Data["UserTypedName"]?.GetValue<string>() ?? "";
        set => Data["UserTypedName"] = value;
    }
    // The game shows the player override when present; never infer an HQ tier from OriginalName.
    public string Name => string.IsNullOrWhiteSpace(UserTypedName) ? OriginalName : UserTypedName;
    public int Type => Data["Type"]?.GetValue<int>() ?? -1;
    public string TypeName => BattleplanTypeCatalog.TypeName(Type);
    public int SubType => Data["SubType"]?.GetValue<int>() ?? -1;
    public int CountryId => Data["Country"]?.GetValue<int>() ?? -1;
    public string Country => BattleplanTypeCatalog.CountryName(CountryId);
    public bool Organizational => Data["Organizational"]?.GetValue<bool>() ?? false;
    public int NumUnits => Data["NumUnits"]?.GetValue<int>() ?? 0;
    public double StrengthPercentage => Data["StrengthPercentage"]?.GetValue<double>() ?? 0;
    public int Depth => Parent is null ? 0 : Parent.Depth + 1;
    public string Officers => Data["AttachedOfficers"] is JsonArray a ? string.Join(", ", a.OfType<JsonObject>().Select(x => x["OfficerName"]?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x))) : "";
    public override string ToString() => Name;
}

public sealed class Campaign
{
    private readonly JsonObject _root;
    private byte[] _sourceHash;
    public string FilePath { get; }
    public Unit RootUnit { get; }
    public List<Unit> Units { get; } = [];
    // Some saves wrap the actual army in a zero-element organizational node (for example
    // the sampled "1 Bn"). Hide that node in the UI but retain it unchanged in the save.
    public bool HasStructuralRoot => RootUnit.Parent is null && RootUnit.Organizational && RootUnit.NumUnits == 0 && RootUnit.Children.Count > 0;
    public IEnumerable<Unit> VisibleRoots => HasStructuralRoot ? RootUnit.Children : [RootUnit];
    public IEnumerable<Unit> EditableUnits => HasStructuralRoot ? Units.Where(u => u != RootUnit) : Units;
    public string SaveName => _root["SaveName"]?.ToString() ?? Path.GetFileNameWithoutExtension(FilePath);
    public string Version => _root["VersionSaved"]?.ToString() ?? "unknown";
    private Campaign(string path, JsonObject root, byte[] sourceHash)
    {
        FilePath = path;
        _root = root;
        _sourceHash = sourceHash;
        RootUnit = Parse(root["Units"] as JsonObject ?? throw new InvalidDataException("Missing Units object."), null, "Units");
        if (Units.GroupBy(u => u.Id).Any(g => string.IsNullOrWhiteSpace(g.Key) || g.Count() > 1))
            throw new InvalidDataException("Unit IDs are missing or duplicated; refusing to edit this save.");
    }
    public static Campaign Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var root = JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException("Campaign is not a JSON object.");
        return new Campaign(path, root, SHA256.HashData(bytes));
    }
    // ChildGroups order is the only reliable hierarchy order observed in the save.
    private Unit Parse(JsonObject data, Unit? parent, string path)
    {
        var unit = new Unit { Data = data, Parent = parent, Path = path, Id = data["ID"]?.ToString() ?? "" };
        Units.Add(unit);
        if (data["ChildGroups"] is JsonArray children)
            for (int i = 0; i < children.Count; i++)
                if (children[i] is JsonObject child) unit.Children.Add(Parse(child, unit, $"{path}/ChildGroups/{i}"));
        return unit;
    }
    public string Save()
    {
        // Refuse to overwrite a campaign changed by Battleplan or another editor since load.
        var current = File.ReadAllBytes(FilePath);
        if (!SHA256.HashData(current).AsSpan().SequenceEqual(_sourceHash))
            throw new IOException("Campaign changed on disk since opening it. Reopen the file before saving.");
        var backup = FilePath + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".bak";
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".adc.tmp";
        var options = new JsonSerializerOptions { WriteIndented = true };
        try
        {
            // Validate a temporary complete campaign before replacing the original. File.Replace
            // creates a timestamped backup and avoids leaving a partially written save.
            File.WriteAllText(temp, _root.ToJsonString(options), new UTF8Encoding(false));
            _ = Campaign.Load(temp);
            File.Replace(temp, FilePath, backup);
            _sourceHash = SHA256.HashData(File.ReadAllBytes(FilePath));
            return backup;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
