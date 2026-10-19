using BattleplanADC;
using System.Text.Json.Nodes;

if (args.Length != 1) throw new ArgumentException("Pass a campaign path.");
var copy = Path.Combine(Path.GetTempPath(), "battleplan-adc-check-" + Guid.NewGuid() + ".camp");
File.Copy(args[0], copy);
try
{
    var before = JsonNode.Parse(File.ReadAllText(copy))!;
    var campaign = Campaign.Load(copy);
    var firstNamedRegiment = campaign.Units.First(u => u.Type == 11 && u.UserTypedName == "1IR");
    if (BattalionNamingService.UserNumber(firstNamedRegiment) != 1) throw new Exception("User-entered regiment number was not read.");
    var battalionRows = BattalionNamingService.Children(firstNamedRegiment, null);
    if (!battalionRows.Any(r => r.Slot == 5 && r.Suffix == "TD") || !battalionRows.Any(r => r.Slot == 6 && r.Suffix == "ARMENG") ||
        BattalionNamingService.Render(BattalionNamingService.DefaultPattern, 5, 25, "TD") != "5-25 TD")
        throw new Exception("Guided battalion suggestions were incorrect.");
    if (!campaign.HasStructuralRoot || campaign.VisibleRoots.Single().UserTypedName != "8th Army" || campaign.EditableUnits.Count() != campaign.Units.Count - 1)
        throw new Exception($"Nonplayable save wrapper was not identified: root={campaign.HasStructuralRoot}, first={campaign.VisibleRoots.First().UserTypedName}, count={campaign.EditableUnits.Count()}.");
    var massCatalog = new NamingCatalog { CampaignPath = copy };
    foreach (var u in campaign.EditableUnits.Where(u => u.Type == 11 && System.Text.RegularExpressions.Regex.IsMatch(u.UserTypedName, @"^\d+IR$")))
    { var c = massCatalog.Choice(u); c.Tier = "Regiment"; c.Branch = "Infantry"; }
    foreach (var u in campaign.EditableUnits.Where(u => u.Type == 11 && u.Children.Any(c => c.UserTypedName == "1IR" || c.UserTypedName == "21IR")))
    { var c = massCatalog.Choice(u); c.Tier = "Division"; c.Branch = "Infantry"; }
    var massPlan = MassOobNamingService.Build(campaign, massCatalog, campaign.EditableUnits, BattalionNamingService.DefaultPattern, count => count - 1);
    if (massPlan.NewDivisionNumbers != 1 || massPlan.NewRegimentNumbers != 3)
        throw new Exception($"Scattered assignment counts wrong: {massPlan.NewDivisionNumbers} divisions, {massPlan.NewRegimentNumbers} regiments.");
    if (!massPlan.Rows.Any(r => r.Tier == "Division" && r.Proposed == "90th Infantry Division") ||
        !massPlan.Rows.Any(r => r.Tier == "Regiment" && r.Proposed == "300IR"))
        throw new Exception("Mass names did not follow player-entered division/regiment patterns.");
    if (massPlan.Rows.Any(r => r.Unit == firstNamedRegiment && r.Tier == "Regiment"))
        throw new Exception("A uniquely numbered existing regiment was marked for renaming.");
    if (massCatalog.ReservedNumbers.Where(r => r.Tier == "Regiment").GroupBy(r => r.Number).Any(g => g.Count() > 1))
        throw new Exception("A regiment number was reserved twice.");
    if (!massPlan.Rows.Any(r => r.Tier == "Regiment" && r.Current == "21IR" && r.Proposed != "21IR") ||
        !massPlan.Rows.Any(r => r.Tier == "Child unit" && r.Proposed.Contains("-300 INF")))
        throw new Exception("Cloned regiment and child names did not cascade.");
    if (!massPlan.Rows.Any(r => r.Proposed == "90DIVSUP") || !massPlan.Rows.Any(r => r.Proposed == "4 DEB"))
        throw new Exception("Division support names did not receive reviewable cascade proposals.");
    var oldReservation = massCatalog.ReservedNumbers.First(r => r.Tier == "Regiment" && r.Number == 1);
    firstNamedRegiment.UserTypedName = "Custom Command";
    _ = ScatteredNumberService.Assign(campaign, massCatalog, campaign.EditableUnits, "Regiment", count => count - 1);
    if (!massCatalog.ReservedNumbers.Any(r => r.Tier == "Regiment" && r.Number == 1 && r.UnitId == oldReservation.UnitId))
        throw new Exception("Changing a name released a previously used number.");
    firstNamedRegiment.UserTypedName = "1IR";
    var overrideChoice = massCatalog.Choice(firstNamedRegiment);
    overrideChoice.Number = 77; overrideChoice.NumberOverride = true;
    var overridden = ScatteredNumberService.Assign(campaign, massCatalog, [firstNamedRegiment], "Regiment", count => count - 1).Single();
    if (overridden.Number != 77 || !massCatalog.ReservedNumbers.Any(r => r.Tier == "Regiment" && r.Number == 1))
        throw new Exception("Manual number override did not retain retired number.");
    var ledgerDirectory = Path.Combine(Path.GetTempPath(), "battleplan-adc-ledger-check-" + Guid.NewGuid());
    try
    {
        massCatalog.Save(ledgerDirectory);
        var reloaded = NamingCatalog.Load(copy, ledgerDirectory);
        if (reloaded.ReservedNumbers.Count != massCatalog.ReservedNumbers.Count ||
            !reloaded.ReservedNumbers.Any(r => r.Tier == "Regiment" && r.Number == 1 && r.UnitId == firstNamedRegiment.Id))
            throw new Exception("Reserved numbers did not persist.");
    }
    finally { if (Directory.Exists(ledgerDirectory)) Directory.Delete(ledgerDirectory, true); }
    var catalogDirectory = Path.Combine(Path.GetTempPath(), "battleplan-adc-catalog-check-" + Guid.NewGuid());
    try
    {
        var catalog = NamingCatalog.Load(copy, catalogDirectory);
        var duplicate = campaign.Units.GroupBy(u => u.Name, StringComparer.OrdinalIgnoreCase).First(g => g.Count() > 1).ToList();
        foreach (var u in duplicate) { var c = catalog.Choice(u); c.Tier = "Regiment"; c.Branch = "Infantry"; c.Policy = "Include"; }
        catalog.SetRule(new NamingRule { Tier = "Regiment", Branch = "Infantry", Pattern = "{n}IR", Start = 101 });
        catalog.Save(catalogDirectory);
        var loadedCatalog = NamingCatalog.Load(copy, catalogDirectory);
        if (loadedCatalog.Units.Count != duplicate.Count || loadedCatalog.Rule("Regiment", "Infantry")?.Pattern != "{n}IR") throw new Exception("Catalog did not persist.");
        var renamed = CatalogNamingService.Preview(campaign, loadedCatalog).Proposals;
        if (duplicate.Any(u => !renamed.Any(p => p.Unit.Id == u.Id))) throw new Exception("Duplicate names were skipped.");
        if (renamed.Select(p => p.After).Distinct(StringComparer.OrdinalIgnoreCase).Count() != renamed.Count) throw new Exception("Generated names were not unique.");
        loadedCatalog.Choice(duplicate[0]).Policy = "Exclude";
        if (CatalogNamingService.Preview(campaign, loadedCatalog).Proposals.Any(p => p.Unit.Id == duplicate[0].Id)) throw new Exception("Exclude policy ignored.");
        if (NamingPatterns.Infer("1st Infantry Division").Pattern != "{n:ordinal} Infantry Division" || NamingPatterns.Render("{n:ordinal} Infantry Division", 2, 1, null) != "2nd Infantry Division") throw new Exception("Ordinal pattern failed.");
        if (NamingPatterns.Infer("1-5 INF").Pattern != "{slot}-{parentNumber} INF" || NamingPatterns.Render("{slot}-{parentNumber} INF", 4, 4, 25) != "4-25 INF") throw new Exception("Nested pattern failed.");
        if (NamingPatterns.InferExamples("1st Infantry Division\n2nd Infantry Division").Pattern != "{n:ordinal} Infantry Division" || NamingPatterns.InferExamples("1-5 INF\n3-5 INF").Pattern is not null) throw new Exception("Multiple example validation failed.");
        if (!NamingPatterns.InferExamples("").Explanation.Contains("Assign their tier and branch") ||
            !NamingPatterns.InferExamples("1-5 INF\n3-5 INF").Explanation.Contains("Batch assign tiers and branches"))
            throw new Exception("Inference recovery guidance missing.");
        try { NamingPatterns.Render("{slot}-{parentNumber} INF", 1, 1, null); throw new Exception("Missing numbered parent was accepted."); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Assign the parent HQ's tier")) { }
    }
    finally { if (Directory.Exists(catalogDirectory)) Directory.Delete(catalogDirectory, true); }
    if (campaign.Units.Count < 2) throw new Exception("Expected a hierarchy.");
    if (BattleplanTypeCatalog.TypeName(19) != "Mechanized engineers" || BattleplanTypeCatalog.TypeName(20) != "Self-propelled anti-tank guns")
        throw new Exception("Game type mapping regressed.");
    var regiments = OobNamingService.Discover(campaign);
    if (regiments.Count != 9) throw new Exception($"Expected nine regiment commands, found {regiments.Count}.");
    var firstRegiment = regiments[0];
    var oob = OobNamingService.Build(campaign, regiments.Where(r => r == firstRegiment), firstRegiment.Unit, true, false, "DET");
    if (oob.Problems.Count > 0) throw new Exception(string.Join("; ", oob.Problems));
    if (!oob.Proposals.Any(p => p.Unit.Type == 20 && p.After == "5-1 TD") ||
        !oob.Proposals.Any(p => p.Unit.Type == 19 && p.After == "6-1 ARMENG"))
        throw new Exception("OOB fixed role slots were not assigned from game types.");
    var syntheticPath = Path.Combine(Path.GetTempPath(), "battleplan-adc-synthetic-" + Guid.NewGuid() + ".camp");
    try
    {
        static JsonObject MakeUnit(int type, string name, string user, params JsonObject[] children) => new()
        {
            ["ID"] = Guid.NewGuid().ToString(), ["Type"] = type, ["Country"] = 2, ["SubType"] = 0,
            ["OriginalName"] = name, ["UserTypedName"] = user, ["Organizational"] = false,
            ["ChildGroups"] = new JsonArray(children.Select(c => (JsonNode?)c).ToArray())
        };
        var regiment = MakeUnit(11, "25th Regiment", "25IR",
            MakeUnit(11, "Attached command", "", MakeUnit(0, "1st Bn", ""), MakeUnit(0, "2nd Bn", "")),
            MakeUnit(0, "3rd Bn", ""),
            MakeUnit(6, "4th Bn", ""), MakeUnit(5, "5th Bn", ""), MakeUnit(19, "6th Bn", ""),
            MakeUnit(1, "7th Coy", ""));
        File.WriteAllText(syntheticPath, new JsonObject { ["Units"] = MakeUnit(11, "Root", "", regiment) }.ToJsonString());
        var synthetic = Campaign.Load(syntheticPath);
        var syntheticEntry = OobNamingService.Discover(synthetic).Single();
        var mapped = OobNamingService.Build(synthetic, [syntheticEntry], null, true, true, "DET").Proposals;
        foreach (var expectedName in new[] { "1-25 INF", "2-25 INF", "3-25 INF", "4-25 ART", "5-25 AT", "6-25 ARMENG", "7-25 DET" })
            if (!mapped.Any(p => p.After == expectedName)) throw new Exception($"Missing OOB proposal {expectedName}.");
        if (OobNamingService.ValidateFinalNames(synthetic, mapped).Count > 0) throw new Exception("Synthetic OOB names conflicted.");
    }
    finally { if (File.Exists(syntheticPath)) File.Delete(syntheticPath); }
    var unit = campaign.Units[1];
    var old = unit.UserTypedName;
    unit.UserTypedName = "ADC CHECK";
    var backup = campaign.Save();
    var after = Campaign.Load(copy);
    if (after.Units[1].UserTypedName != "ADC CHECK") throw new Exception("Rename did not persist.");
    if (Campaign.Load(backup).Units[1].UserTypedName != old) throw new Exception("Backup changed.");
    var afterJson = JsonNode.Parse(File.ReadAllText(copy))!;
    var expected = JsonNode.Parse(before.ToJsonString())!;
    var expectedUnit = ((JsonObject)((JsonObject)expected["Units"]!)["ChildGroups"]![0]!);
    expectedUnit["UserTypedName"] = "ADC CHECK";
    if (!JsonNode.DeepEquals(expected, afterJson)) throw new Exception("Unrelated campaign data changed.");
    File.AppendAllText(copy, " ");
    try { campaign.Save(); throw new Exception("External change was not detected."); }
    catch (IOException ex) when (ex.Message.Contains("changed on disk")) { }
    Console.WriteLine($"PASS: {campaign.Units.Count} units, {regiments.Count} regiments, game type mapping, synthetic OOB slots, rename round trip, backup, whole-save preservation, external-change guard.");
    File.Delete(backup);
}
finally { if (File.Exists(copy)) File.Delete(copy); }
