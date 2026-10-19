using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace BattleplanADC;

public partial class MainWindow
{
    // The catalog lives beside this project's build folders, not beside or inside a game
    // save. Tests run from their own output folder and receive a separate catalog path.
    private NamingCatalog? _catalog;
    private List<NamingProposal> _catalogPreview = [];
    private List<string> _catalogProblems = [];
    private readonly string _catalogDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "catalogs"));

    private void OpenCatalog(Campaign campaign)
    {
        _catalog = NamingCatalog.Load(campaign.FilePath, _catalogDirectory);
        CatalogTypeFilter.Items.Clear();
        CatalogTypeFilter.Items.Add("All game types");
        foreach (var type in campaign.EditableUnits.Select(u => u.Type).Distinct().Order())
            CatalogTypeFilter.Items.Add($"{type}: {BattleplanTypeCatalog.TypeName(type)}");
        CatalogTypeFilter.SelectedIndex = 0;
        _catalogPreview.Clear();
        CatalogPreviewGrid.ItemsSource = null;
        RefreshCatalogGrid();
        CatalogStatus.Text = $"{_catalog.Units.Count} unit choices and {_catalog.Rules.Count} naming schemes saved for this campaign.";
    }
    private void RefreshCatalogGrid()
    {
        // Filters affect display only. Duplicate detection always examines all editable
        // units so a filtered view cannot hide an existing naming collision.
        if (_campaign is null || _catalog is null || CatalogGrid is null) return;
        var duplicateIds = _campaign.EditableUnits.GroupBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Key.Length > 0 && g.Count() > 1).SelectMany(g => g.Select(u => u.Id)).ToHashSet();
        var rows = _campaign.EditableUnits.Select(u => new CatalogRow { Unit = u, Choice = _catalog.Choice(u) });
        rows = CatalogFilter?.SelectedIndex switch
        {
            1 => rows.Where(r => r.Unit.Type == 11),
            2 => rows.Where(r => duplicateIds.Contains(r.Unit.Id)),
            3 => rows.Where(r => r.Tier.Length == 0 || r.Branch.Length == 0),
            _ => rows
        };
        if (CatalogTypeFilter?.SelectedIndex > 0 && int.TryParse(CatalogTypeFilter.SelectedItem?.ToString()?.Split(':')[0], out var type))
            rows = rows.Where(r => r.Unit.Type == type);
        CatalogGrid.ItemsSource = rows.ToList();
    }
    private void CatalogFilter_Changed(object sender, SelectionChangedEventArgs e) => RefreshCatalogGrid();
    private List<CatalogRow> SelectedCatalogRows() => _oobSelectedIds.Count > 0 && _campaign is not null && _catalog is not null
        ? _campaign.Units.Where(u => _oobSelectedIds.Contains(u.Id)).Select(u => new CatalogRow { Unit = u, Choice = _catalog.Choice(u) }).ToList()
        : CatalogGrid.SelectedItems.OfType<CatalogRow>().ToList();
    private void CatalogChanged(string message)
    {
        // Classification/policy edits invalidate previews, but do not mark campaign JSON
        // dirty because these choices are saved in the sidecar immediately.
        _catalog!.Save(_catalogDirectory);
        InvalidateMassPreview();
        _catalogPreview.Clear(); CatalogPreviewGrid.ItemsSource = null;
        RefreshCatalogGrid();
        CatalogStatus.Text = message;
    }
    private static string Selection(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
    private void CatalogAssign_Click(object sender, RoutedEventArgs e)
    {
        if (_catalog is null) return;
        var rows = SelectedCatalogRows();
        var tier = Selection(BatchTier); var branch = Selection(BatchBranch);
        if (rows.Count == 0 || tier.Length == 0 || branch.Length == 0) { CatalogStatus.Text = "Select units, a tier, and a branch first."; return; }
        foreach (var row in rows) { row.Choice.Tier = tier; row.Choice.Branch = branch; }
        CatalogChanged($"Assigned {tier} / {branch} to {rows.Count} units. Choices saved in the editor catalog.");
    }
    private void SetPolicy(string policy)
    {
        if (_catalog is null) return;
        var rows = SelectedCatalogRows();
        if (rows.Count == 0) { CatalogStatus.Text = "Select one or more units first."; return; }
        foreach (var row in rows) row.Choice.Policy = policy;
        CatalogChanged($"Set {rows.Count} units to {policy}.");
    }
    private void CatalogInclude_Click(object sender, RoutedEventArgs e) => SetPolicy("Include");
    private void CatalogExclude_Click(object sender, RoutedEventArgs e) => SetPolicy("Exclude");
    private void CatalogDefault_Click(object sender, RoutedEventArgs e) => SetPolicy("Default");
    private void CatalogNumber_Click(object sender, RoutedEventArgs e)
    {
        if (_catalog is null) return;
        var rows = SelectedCatalogRows();
        if (rows.Count != 1 || !int.TryParse(CatalogNumber.Text, out var number) || number < 0)
        { CatalogStatus.Text = "Select one unit and enter its nonnegative command number."; return; }
        rows[0].Choice.Number = number; rows[0].Choice.NumberOverride = true;
        CatalogChanged($"Assigned number {number} to {rows[0].Current}.");
    }
    private void SchemeUseSelected_Click(object sender, RoutedEventArgs e)
    {
        var names = SelectedCatalogRows().Select(r => r.Unit.UserTypedName).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        SchemeMessage.Text = names.Count == 0 ? "Selected units have no user-entered names. Enter examples manually." : $"Loaded {names.Count} user-entered names. Put them in consecutive numbering order if needed.";
        if (names.Count > 0) SchemeExample.Text = string.Join(Environment.NewLine, names);
    }
    private void SchemeInfer_Click(object sender, RoutedEventArgs e)
    {
        // Return practical hints when an example's syntax is clear but its target HQ
        // classification or numbered parent is still missing.
        var (pattern, explanation) = NamingPatterns.InferExamples(SchemeExample.Text);
        if (pattern is not null) SchemePattern.Text = pattern;
        var tier = Selection(BatchTier); var branch = Selection(BatchBranch);
        if (tier.Length == 0 || branch.Length == 0)
            explanation += " Choose a tier and branch above, then batch assign matching units so the scheme can be applied to them.";
        else if (_catalog is not null && _campaign is not null)
        {
            var matches = _campaign.Units.Count(u => _catalog.Units.TryGetValue(u.Id, out var c) && c.Tier == tier && c.Branch == branch && c.Policy != "Exclude");
            if (matches == 0) explanation += $" No units are classified as {tier} / {branch} yet. Batch select them and assign that tier and branch.";
        }
        if (pattern?.Contains("{parentNumber}") == true)
            explanation += " For {parentNumber}, classify the parent HQ tier, save a numbering scheme for it, and set a command number where the sequence needs a specific value.";
        SchemeMessage.Text = explanation;
    }
    private void SchemeSave_Click(object sender, RoutedEventArgs e)
    {
        if (_catalog is null) return;
        var tier = Selection(BatchTier); var branch = Selection(BatchBranch);
        if (tier.Length == 0 || branch.Length == 0 || SchemePattern.Text.Trim().Length == 0 || !int.TryParse(SchemeStart.Text, out var start) || start < 0)
        { SchemeMessage.Text = "Choose tier and branch, enter a pattern, and set a nonnegative starting number."; return; }
        try { NamingPatterns.Render(SchemePattern.Text, start, 1, 1); }
        catch (InvalidOperationException ex) { SchemeMessage.Text = ex.Message; return; }
        _catalog.SetRule(new NamingRule { Tier = tier, Branch = branch, Pattern = SchemePattern.Text.Trim(), Start = start, Sequence = Selection(SchemeSequence) });
        CatalogChanged($"Saved scheme for {tier} / {branch}.");
        SchemeMessage.Text = $"{tier} / {branch}: {SchemePattern.Text.Trim()}";
    }
    private void CatalogPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_campaign is null || _catalog is null) return;
        (_catalogPreview, _catalogProblems) = CatalogNamingService.Preview(_campaign, _catalog);
        CatalogPreviewGrid.ItemsSource = _catalogPreview;
        var eligible = _campaign.Units.Where(u => !_catalog.Units.TryGetValue(u.Id, out var c) || c.Policy != "Exclude").ToList();
        var unclassified = eligible.Count(u => !_catalog.Units.TryGetValue(u.Id, out var c) || c.Tier.Length == 0 || c.Branch.Length == 0);
        var hq = eligible.Count(u => u.Type == 11 && (!_catalog.Units.TryGetValue(u.Id, out var c) || c.Tier.Length == 0));
        var missingRules = eligible.Where(u => _catalog.Units.TryGetValue(u.Id, out var c) && c.Tier.Length > 0 && c.Branch.Length > 0 && _catalog.Rule(c.Tier, c.Branch) is null)
            .Select(u => { var c = _catalog.Units[u.Id]; return $"{c.Tier} / {c.Branch}"; }).Distinct().Take(4).ToList();
        var hints = new List<string>();
        if (hq > 0) hints.Add($"{hq} HQs have no assigned tier. Use the Headquarters filter and batch assign their intended tiers.");
        if (unclassified > 0) hints.Add($"{unclassified} eligible units lack tier or branch; use Unclassified or a game-type filter to batch assign them.");
        if (missingRules.Count > 0) hints.Add("No naming scheme for " + string.Join(", ", missingRules) + ". Choose each tier/branch and infer from user names or enter a pattern.");
        CatalogStatus.Text = $"{_catalogPreview.Count} proposed changes. " + string.Join(" ", hints.Concat(_catalogProblems.Take(5)));
    }
    private void CatalogApply_Click(object sender, RoutedEventArgs e)
    {
        // Advanced rules preview and apply to the full campaign. Recompute eligibility
        // and final names before writing staged overrides.
        if (_campaign is null || _catalog is null || _catalogPreview.Count == 0) { CatalogStatus.Text = "Preview names first."; return; }
        CatalogPreviewGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        CatalogPreviewGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var fresh = CatalogNamingService.Preview(_campaign, _catalog);
        if (fresh.Proposals.Select(p => p.Unit.Id).Except(_catalogPreview.Select(p => p.Unit.Id)).Any() ||
            _catalogPreview.Any(p => p.Unit.Name != p.Before)) { CatalogStatus.Text = "Campaign or catalog changed. Preview again."; return; }
        var final = _campaign.Units.Select(u => _catalogPreview.FirstOrDefault(p => p.Unit == u)?.After?.Trim() ?? u.Name).ToList();
        var conflicts = final.Where(string.IsNullOrWhiteSpace).Any() ? "Blank names remain. " : "";
        conflicts += string.Join("; ", final.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Key.Length > 0 && g.Count() > 1).Take(5).Select(g => $"Duplicate: {g.Key}"));
        if (_catalogProblems.Count > 0 || conflicts.Length > 0) { CatalogStatus.Text = string.Join(" ", _catalogProblems.Take(5)) + " " + conflicts; return; }
        foreach (var proposal in _catalogPreview) proposal.Unit.UserTypedName = proposal.After.Trim();
        InvalidateMassPreview();
        var count = _catalogPreview.Count;
        _dirty = true;
        _catalogPreview = []; CatalogPreviewGrid.ItemsSource = null;
        RefreshTree(); RefreshCatalogGrid();
        CatalogStatus.Text = $"Applied {count} names in memory. Save with backup to write the campaign.";
    }
}
