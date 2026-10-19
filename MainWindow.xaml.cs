using Microsoft.Win32;
using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace BattleplanADC;

public sealed record RenameChange(Unit Unit, string Before, string After);
public sealed record CampaignFile(string Name, string Path);
public sealed record TypeSummary(int Id, string GameType, int Count, string EditorDefault, string Example);

public partial class MainWindow : Window
{
    // All UI partials share these session objects. The campaign owns game JSON; the catalog
    // owns editor-only choices; preview lists are disposable plans, not persisted changes.
    private Campaign? _campaign;
    private Unit? _selected;
    private List<RenameChange> _preview = [];
    private List<RegimentEntry> _regiments = [];
    private List<NameProposal> _oobPreview = [];
    private List<string> _oobBuildProblems = [];
    private string _oobRules = "";
    private bool _dirty;
    private string _lastRules = "";
    private readonly string _campaignFolder = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "..", "LocalLow", "Foolish Mortals", "Battleplan"));
    public MainWindow()
    {
        InitializeComponent();
        RefreshCampaignFiles();
        if (CampaignFilesBox.Items.Count == 1)
        {
            TryOpen(((CampaignFile)CampaignFilesBox.Items[0]).Path);
        }
    }
    private void RefreshCampaignFiles()
    {
        var current = (CampaignFilesBox.SelectedItem as CampaignFile)?.Path ?? _campaign?.FilePath;
        CampaignFilesBox.Items.Clear();
        if (Directory.Exists(_campaignFolder))
            foreach (var path in Directory.EnumerateFiles(_campaignFolder, "*.camp").OrderBy(Path.GetFileName))
                CampaignFilesBox.Items.Add(new CampaignFile(Path.GetFileName(path), path));
        CampaignFilesBox.SelectedItem = CampaignFilesBox.Items.OfType<CampaignFile>().FirstOrDefault(x => x.Path == current) ?? CampaignFilesBox.Items.OfType<CampaignFile>().FirstOrDefault();
        SavePathLabel.Text = _campaignFolder;
        if (CampaignFilesBox.Items.Count == 0) Status.Text = "No campaign files found. Browse to a .camp file.";
    }
    private void RefreshFiles_Click(object sender, RoutedEventArgs e) => RefreshCampaignFiles();
    private void OpenSelected_Click(object sender, RoutedEventArgs e)
    {
        if (CampaignFilesBox.SelectedItem is CampaignFile file) TryOpen(file.Path);
    }
    private void TryOpen(string path)
    {
        // Opening a different save clears every pending preview and selection so a proposal
        // can never be applied against the wrong campaign or catalog.
        try
        {
            if (_dirty && MessageBox.Show("Discard unsaved changes?", "Battleplan ADC", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            _campaign = Campaign.Load(path); _dirty = false; _selected = null; _oobSelectedIds.Clear(); _oobAnchorId = null; _preview.Clear(); PreviewGrid.ItemsSource = null;
            _regiments = [];
            RegimentGrid.ItemsSource = _regiments;
            TypeSummaryGrid.ItemsSource = _campaign.EditableUnits.GroupBy(u => u.Type).OrderBy(g => g.Key)
                .Select(g => new TypeSummary(g.Key, BattleplanTypeCatalog.TypeName(g.Key), g.Count(), BattleplanTypeCatalog.DefaultNamingSuffix(g.Key), g.First().Name)).ToList();
            ClearOobPreview();
            OpenCatalog(_campaign);
            _battalionCommand = null; _battalionRows = []; _battalionPreviewNumber = null; BattalionGrid.ItemsSource = null; BattalionRegimentNumber.Text = ""; BattalionMessage.Text = "Select a regiment command in the tree to begin.";
            _massPlan = null; MassGrid.ItemsSource = null; MassMessage.Text = "Assign tiers on the OOB tree, then preview names.";
            CampaignLabel.Text = $"{_campaign.SaveName}  •  {_campaign.Version}  •  {_campaign.EditableUnits.Count()} visible units";
            SavePathLabel.Text = path;
            RefreshTree(); Status.Text = $"Opened {path}";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Cannot open campaign"); }
    }
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Battleplan campaigns (*.camp)|*.camp|All files (*.*)|*.*", InitialDirectory = Directory.Exists(_campaignFolder) ? _campaignFolder : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
        if (dialog.ShowDialog() == true) TryOpen(dialog.FileName);
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // The game may rewrite an open campaign. Campaign.Save adds a second guard by
        // comparing the loaded file hash immediately before replacing it.
        if (_campaign is null) return;
        if (!_dirty) { Status.Text = "No changes to save."; return; }
        if (Process.GetProcessesByName("Battleplan").Length > 0)
        {
            MessageBox.Show("Close Battleplan before saving the campaign, then try again.", "Battleplan is running");
            return;
        }
        try { var backup = _campaign.Save(); _dirty = false; Status.Text = $"Saved. Backup: {backup}"; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Save failed"); }
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (UnitTree is not null) RefreshTree(); }
    private void ExpandAll_Click(object sender, RoutedEventArgs e) => SetTreeExpanded(true);
    private void CollapseAll_Click(object sender, RoutedEventArgs e) => SetTreeExpanded(false);
    private void SetTreeExpanded(bool expanded)
    {
        static void SetItem(TreeViewItem item, bool value)
        {
            item.IsExpanded = value;
            foreach (TreeViewItem child in item.Items) SetItem(child, value);
        }
        foreach (TreeViewItem root in UnitTree.Items) SetItem(root, expanded);
    }
    private void RefreshTree()
    {
        // Build TreeViewItem nodes from the current Unit wrappers on each refresh. Matching
        // descendants keep their ancestors visible, and selection is restored by game ID.
        var selectedUnit = _selected;
        var selectedId = selectedUnit?.Id;
        UnitTree.Items.Clear(); if (_campaign is null) return;
        var query = SearchBox.Text.Trim();
        bool Matches(Unit u) => query.Length == 0 || u.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || u.Id.Contains(query, StringComparison.OrdinalIgnoreCase) || u.Officers.Contains(query, StringComparison.OrdinalIgnoreCase);
        bool AncestorOfSelection(Unit u) { for (var p = selectedUnit; p is not null; p = p.Parent) if (p == u) return true; return false; }
        TreeViewItem? Add(Unit u)
        {
            var children = u.Children.Select(Add).Where(x => x is not null).ToList();
            if (!Matches(u) && children.Count == 0) return null;
            var label = u.Organizational ? $"Organizational • {u.TypeName}" : u.TypeName;
            var item = new TreeViewItem { Header = new TextBlock { Text = $"{u.Name}  [{label}]", Padding = new Thickness(2) }, Tag = u, IsExpanded = query.Length > 0 || u.Parent is null || u.Type == 11 || AncestorOfSelection(u) };
            PaintOobSelection(item);
            foreach (var child in children) item.Items.Add(child);
            return item;
        }
        var roots = _campaign.VisibleRoots.Select(Add).Where(x => x is not null).Cast<TreeViewItem>().ToList();
        foreach (var visibleRoot in roots) UnitTree.Items.Add(visibleRoot);
        TreeViewItem? Find(TreeViewItem item)
        {
            if ((item.Tag as Unit)?.Id == selectedId) return item;
            foreach (TreeViewItem child in item.Items) { var found = Find(child); if (found is not null) return found; }
            return null;
        }
        if (selectedId is not null)
            foreach (var visibleRoot in roots) { var item = Find(visibleRoot); if (item is not null) { item.IsSelected = true; break; } }
        UpdateOobSelectionCount();
    }
    private void UnitTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _selected = (UnitTree.SelectedItem as TreeViewItem)?.Tag as Unit;
        DisplaySelectedUnit();
    }
    private void DisplaySelectedUnit()
    {
        BattalionSelectionChanged();
        OobSelectedUnitChanged();
        SelectedPath.Text = _selected?.Path ?? "Select a unit.";
        UnitNameBox.Text = _selected?.UserTypedName ?? "";
        UnitDetails.Text = _selected is null ? "" : $"Original name (game): {_selected.OriginalName}\nUser name (save): {_selected.UserTypedName}\nGame type: {_selected.TypeName} ({_selected.Type})  •  Subtype ID: {_selected.SubType}\nCountry: {_selected.Country} ({_selected.CountryId})  •  Organizational: {_selected.Organizational}\nElements: {_selected.NumUnits}  •  Strength: {_selected.StrengthPercentage:P0}\nGame unit ID: {_selected.Id}\nOfficers: {(_selected.Officers.Length == 0 ? "none" : _selected.Officers)}";
    }
    private void ApplyUnit_Click(object sender, RoutedEventArgs e)
    {
        // This and every rename path stage an override only; none writes the .camp here.
        if (_selected is null) return;
        _selected.UserTypedName = UnitNameBox.Text; _dirty = true; _preview.Clear(); PreviewGrid.ItemsSource = null;
        InvalidateMassPreview();
        ClearOobPreview();
        RefreshTree(); RegimentGrid.Items.Refresh(); Status.Text = "Unit name updated in memory. Save to write the campaign.";
    }
    private void ClearUnit_Click(object sender, RoutedEventArgs e) { UnitNameBox.Text = ""; ApplyUnit_Click(sender, e); }
    private string Rules() => string.Join("\u001f", ScopeBox.SelectedIndex, _selected?.Id, FilterBox.Text, TypeBox.Text, CountryBox.Text, OnlyUnnamedBox.IsChecked, OnlyLeavesBox.IsChecked, FindBox.Text, ReplaceBox.Text, StartBox.Text, StepBox.Text, RegexModeBox.IsChecked, CaseSensitiveBox.IsChecked);
    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        // Find/replace accepts optional game-field filters and a bounded regex. Capture a
        // signature of all settings so Apply can reject an out-of-date preview.
        if (_campaign is null) return;
        if (!int.TryParse(StartBox.Text, out var number) || number < 0) { MessageBox.Show("Enter a nonnegative starting number."); return; }
        if (!int.TryParse(StepBox.Text, out var step) || step <= 0) { MessageBox.Show("Enter a positive numbering step."); return; }
        Regex? regex = null;
        if (RegexModeBox.IsChecked == true && FindBox.Text.Length > 0)
        {
            try { regex = new Regex(FindBox.Text, CaseSensitiveBox.IsChecked == true ? RegexOptions.None : RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)); }
            catch (ArgumentException ex) { MessageBox.Show(ex.Message, "Invalid regex"); return; }
        }
        int? type = null;
        if (!string.IsNullOrWhiteSpace(TypeBox.Text))
        {
            if (!int.TryParse(TypeBox.Text, out var parsed)) { MessageBox.Show("Type ID must be a number."); return; }
            type = parsed;
        }
        int? country = null;
        if (!string.IsNullOrWhiteSpace(CountryBox.Text))
        {
            if (!int.TryParse(CountryBox.Text, out var parsed)) { MessageBox.Show("Country ID must be a number."); return; }
            country = parsed;
        }
        if (ScopeBox.SelectedIndex > 0 && _selected is null) { MessageBox.Show("Select a unit for this scope."); return; }
        bool Descendant(Unit u, Unit parent) { for (var p = u; p is not null; p = p.Parent) if (p == parent) return true; return false; }
        var units = _campaign.Units.Where(u => ScopeBox.SelectedIndex == 0 || (ScopeBox.SelectedIndex == 1 ? Descendant(u, _selected!) : u == _selected))
            .Where(u => u.Name.Contains(FilterBox.Text, StringComparison.OrdinalIgnoreCase) && (type is null || u.Type == type)
                && (country is null || u.CountryId == country) && (OnlyUnnamedBox.IsChecked != true || string.IsNullOrWhiteSpace(u.UserTypedName))
                && (OnlyLeavesBox.IsChecked != true || u.Children.Count == 0)).ToList();
        _preview = [];
        foreach (var u in units)
        {
            var source = u.Name;
            if (FindBox.Text.Length > 0 && !(regex?.IsMatch(source) ?? source.Contains(FindBox.Text, CaseSensitiveBox.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))) continue;
            var replacement = ReplaceBox.Text.Replace("{n}", number.ToString()).Replace("{name}", source).Replace("{original}", u.OriginalName).Replace("{type}", u.Type.ToString()).Replace("{parent}", u.Parent?.Name ?? "");
            string next;
            try { next = FindBox.Text.Length == 0 ? replacement : regex is not null ? regex.Replace(source, replacement) : source.Replace(FindBox.Text, replacement, CaseSensitiveBox.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase); }
            catch (RegexMatchTimeoutException) { MessageBox.Show("Regex match timed out. Narrow the pattern."); return; }
            if (next != source) _preview.Add(new RenameChange(u, source, next));
            number += step;
        }
        _lastRules = Rules(); PreviewGrid.ItemsSource = _preview;
        var issues = OobNamingService.ValidateFinalNames(_campaign, _preview.Select(p => new NameProposal { Unit = p.Unit, Before = p.Before, After = p.After, Role = "Find / replace" }));
        PreviewLabel.Text = $"{_preview.Count} name changes previewed from {units.Count} matching units." +
            (issues.Count == 0 ? "" : " Resolve duplicate or blank names before applying: " + string.Join("; ", issues.Take(3)));
    }
    private void ApplyBatch_Click(object sender, RoutedEventArgs e)
    {
        // Recheck source names and whole-campaign final-name collisions before changing
        // any UserTypedName, including names outside the selected scope.
        if (_preview.Count == 0 || _lastRules != Rules()) { MessageBox.Show("Preview the current rules first."); return; }
        if (_preview.Any(x => x.Unit.Name != x.Before)) { MessageBox.Show("Unit names changed since preview. Preview again."); return; }
        if (_campaign is not null)
        {
            var issues = OobNamingService.ValidateFinalNames(_campaign, _preview.Select(p => new NameProposal { Unit = p.Unit, Before = p.Before, After = p.After, Role = "Find / replace" }));
            if (issues.Count > 0) { MessageBox.Show(string.Join("\n", issues.Take(8)), "Name conflicts"); return; }
        }
        foreach (var change in _preview) change.Unit.UserTypedName = change.After;
        InvalidateMassPreview();
        ClearOobPreview();
        _dirty = true; Status.Text = $"Applied {_preview.Count} names in memory. Save to write the campaign.";
        _preview.Clear(); PreviewGrid.ItemsSource = null; PreviewLabel.Text = "Preview applied."; RefreshTree();
        RegimentGrid.Items.Refresh();
    }
    private string OobRules() => string.Join("\u001f", OobScopeBox.SelectedIndex, OobScopeBox.SelectedIndex == 1 ? _selected?.Id : "", RenameRegimentsBox.IsChecked, IncludeOtherBox.IsChecked, OtherSuffixBox.Text,
        OobInfantryBox.IsChecked, OobArtilleryBox.IsChecked, OobAntiTankBox.IsChecked, OobEngineerBox.IsChecked,
        string.Join("|", _regiments.Select(r => $"{r.Unit.Id}:{r.Include}:{r.Number}:{r.Suffix}")));
    private void AddSelectedRegiment_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _selected.Type != 11 || _selected.Children.Count == 0)
        {
            MessageBox.Show("Select a Battleplan headquarters with child units first.");
            return;
        }
        if (_regiments.Any(r => r.Unit == _selected))
        {
            Status.Text = "That command is already in the regiment candidate list.";
            return;
        }
        var match = System.Text.RegularExpressions.Regex.Match(_selected.Name, @"^\s*(\d+)");
        _regiments.Add(new RegimentEntry { Unit = _selected, Number = match.Success ? match.Groups[1].Value : "", Suffix = "IR" });
        RegimentGrid.Items.Refresh();
        ClearOobPreview();
        Status.Text = "Added command for manual regiment classification. Check its number and suffix before previewing.";
    }
    private void ClearOobPreview()
    {
        _oobPreview = [];
        _oobBuildProblems = [];
        OobPreviewGrid.ItemsSource = null;
        OobPreviewLabel.Text = "";
        OobProblems.Text = "";
    }
    private void OobPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_campaign is null) return;
        RegimentGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        RegimentGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (OobScopeBox.SelectedIndex == 1 && _selected is null)
        {
            OobProblems.Text = "Select a command in the unit tree for branch scope.";
            return;
        }
        var types = new HashSet<int>();
        if (OobInfantryBox.IsChecked == true) types.Add(0);
        if (OobArtilleryBox.IsChecked == true) { types.Add(6); types.Add(18); }
        if (OobAntiTankBox.IsChecked == true) { types.Add(5); types.Add(20); }
        if (OobEngineerBox.IsChecked == true) { types.Add(4); types.Add(19); }
        if (IncludeOtherBox.IsChecked == true)
            foreach (var type in _regiments.SelectMany(r => r.Unit.Children).Select(u => u.Type))
                if (type is not (0 or 6 or 18 or 5 or 20 or 4 or 19)) types.Add(type);
        var result = OobNamingService.Build(_campaign, _regiments, OobScopeBox.SelectedIndex == 1 ? _selected : null,
            RenameRegimentsBox.IsChecked == true, IncludeOtherBox.IsChecked == true, OtherSuffixBox.Text, types);
        _oobPreview = result.Proposals;
        _oobBuildProblems = result.Problems;
        _oobRules = OobRules();
        OobPreviewGrid.ItemsSource = _oobPreview;
        OobSetupExpander.IsExpanded = false;
        OobPreviewLabel.Text = $"{_oobPreview.Count} proposed changes across {_regiments.Count(r => r.Include)} included regiments. Edit any proposed name before applying.";
        var conflicts = OobNamingService.ValidateFinalNames(_campaign, _oobPreview);
        OobProblems.Text = string.Join("\n", _oobBuildProblems.Concat(conflicts).Take(8));
    }
    private void OobApply_Click(object sender, RoutedEventArgs e)
    {
        if (_campaign is null || _oobPreview.Count == 0 || _oobRules != OobRules())
        {
            MessageBox.Show("Preview the current OOB rules first."); return;
        }
        OobPreviewGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        OobPreviewGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var errors = _oobBuildProblems.Concat(OobNamingService.ValidateFinalNames(_campaign, _oobPreview)).Distinct().ToList();
        if (errors.Count > 0)
        {
            OobProblems.Text = string.Join("\n", errors.Take(8));
            MessageBox.Show("Resolve the preview issues before applying names.");
            return;
        }
        foreach (var proposal in _oobPreview) proposal.Unit.UserTypedName = proposal.After.Trim();
        var count = _oobPreview.Count;
        _dirty = true;
        _preview.Clear(); PreviewGrid.ItemsSource = null;
        ClearOobPreview();
        RefreshTree();
        RegimentGrid.Items.Refresh();
        Status.Text = $"Applied {count} OOB names in memory. Save with backup to write the campaign.";
    }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_dirty && MessageBox.Show("Exit with unsaved changes?", "Battleplan ADC", MessageBoxButton.YesNo) != MessageBoxResult.Yes) e.Cancel = true;
        base.OnClosing(e);
    }
}
