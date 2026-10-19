using System.Windows;
using System.Windows.Controls;

namespace BattleplanADC;

public partial class MainWindow
{
    private Unit? _battalionCommand;
    private List<BattalionRenameRow> _battalionRows = [];
    private int? _battalionPreviewNumber;
    private string _battalionPreviewPattern = "";
    private Dictionary<string, (int Slot, string Suffix)> _battalionPreviewInputs = [];

    private void BattalionSelectionChanged()
    {
        if (BattalionCommandLabel is null) return;
        var candidate = _selected?.Children.Count > 0 ? _selected : _selected?.Parent;
        BattalionCommandLabel.Text = candidate is null ? "Selected command: none" : $"Selected command: {candidate.Name} ({candidate.Children.Count} immediate child units)";
        if (_battalionCommand is not null && candidate != _battalionCommand)
            BattalionMessage.Text = "Selection changed. Load the selected command's children to work on this regiment.";
    }
    private void BattalionLoad_Click(object sender, RoutedEventArgs e)
    {
        // A clicked battalion resolves to its parent command; a clicked command uses itself.
        if (_campaign is null || _selected is null) { BattalionMessage.Text = "Click a regiment or one of its battalions in the tree first."; return; }
        var command = _selected.Children.Count > 0 ? _selected : _selected.Parent;
        if (command is null || command.Children.Count == 0) { BattalionMessage.Text = "This unit has no immediate child units. Select its regiment command."; return; }
        _battalionCommand = command;
        _battalionRows = BattalionNamingService.Children(command, _catalog);
        BattalionGrid.ItemsSource = _battalionRows;
        _battalionPreviewNumber = null;
        var number = _catalog?.Units.TryGetValue(command.Id, out var choice) == true ? choice.Number : null;
        number ??= BattalionNamingService.UserNumber(command);
        BattalionRegimentNumber.Text = number?.ToString() ?? "";
        BattalionCommandLabel.Text = $"Selected command: {command.Name} ({command.Children.Count} immediate child units)";
        var skipped = _battalionRows.Count(r => !r.Use);
        BattalionMessage.Text = $"{_battalionRows.Count} units shown. {skipped} unchecked; review them before applying." +
            (number is null ? " Enter the regiment number, then choose Update names." : "");
        if (number is not null) BattalionPreview_Click(sender, e);
    }
    private void BattalionPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_battalionCommand is null || _battalionRows.Count == 0) { BattalionMessage.Text = "Load a regiment's children first."; return; }
        BattalionGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        BattalionGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (!int.TryParse(BattalionRegimentNumber.Text, out var number) || number < 0)
        { BattalionMessage.Text = "Enter the regiment number, such as 25 for 25IR."; return; }
        var pattern = BattalionPattern.Text.Trim();
        if (pattern.Length == 0) { BattalionMessage.Text = "Enter a name format. The starting format is {slot}-{regiment} {suffix}."; return; }
        try
        {
            foreach (var row in _battalionRows.Where(r => r.Use))
            {
                if (row.Slot < 0) throw new InvalidOperationException("Unit numbers must be zero or greater.");
                row.Proposed = BattalionNamingService.Render(pattern, row.Slot, number, row.Suffix);
            }
        }
        catch (InvalidOperationException ex) { BattalionMessage.Text = ex.Message; return; }
        _battalionPreviewNumber = number;
        _battalionPreviewPattern = pattern;
        _battalionPreviewInputs = _battalionRows.ToDictionary(r => r.Unit.Id, r => (r.Slot, r.Suffix));
        BattalionGrid.Items.Refresh();
        BattalionMessage.Text = $"Previewed {_battalionRows.Count(r => r.Use)} checked child units for regiment {number}. Edit any proposed name before applying.";
    }
    private void BattalionApply_Click(object sender, RoutedEventArgs e)
    {
        // Require a fresh preview after changing slots, suffixes, the regiment number,
        // or the format. This protects the player from applying stale suggestions.
        if (_campaign is null || _battalionCommand is null || _battalionPreviewNumber is null) { BattalionMessage.Text = "Preview names first."; return; }
        BattalionGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        BattalionGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (!int.TryParse(BattalionRegimentNumber.Text, out var number) || number != _battalionPreviewNumber || BattalionPattern.Text.Trim() != _battalionPreviewPattern ||
            _battalionRows.Any(r => !_battalionPreviewInputs.TryGetValue(r.Unit.Id, out var old) || old.Slot != r.Slot || old.Suffix != r.Suffix))
        { BattalionMessage.Text = "Number, suffix, or format changed. Preview again before applying."; return; }
        var chosen = _battalionRows.Where(r => r.Use && r.Proposed.Trim() != r.Before).ToList();
        if (chosen.Count == 0) { BattalionMessage.Text = "No checked names need changing."; return; }
        if (_battalionRows.Any(r => r.Unit.Name != r.Before)) { BattalionMessage.Text = "Names changed since loading this regiment. Load it again."; return; }
        var blank = chosen.FirstOrDefault(r => string.IsNullOrWhiteSpace(r.Proposed));
        if (blank is not null) { BattalionMessage.Text = $"Proposed name is blank for {blank.Before}."; return; }
        var collision = chosen.GroupBy(r => r.Proposed.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (collision is not null) { BattalionMessage.Text = $"Proposed name {collision.Key} repeats within this regiment. Edit the preview."; return; }
        var unchanged = _campaign.Units.Except(chosen.Select(r => r.Unit)).Select(u => u.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var conflict = chosen.FirstOrDefault(r => unchanged.Contains(r.Proposed.Trim()));
        if (conflict is not null) { BattalionMessage.Text = $"{conflict.Proposed.Trim()} is already used by another unit. Edit the proposed name or include that unit in another rename."; return; }
        foreach (var row in chosen) row.Unit.UserTypedName = row.Proposed.Trim();
        InvalidateMassPreview();
        if (_catalog is not null)
        {
            var parentChoice = _catalog.Choice(_battalionCommand);
            parentChoice.Tier = "Regiment"; parentChoice.Number = number; parentChoice.NumberOverride = true;
            if (parentChoice.Branch.Length == 0) parentChoice.Branch = "Headquarters";
            foreach (var row in chosen)
            {
                var choice = _catalog.Choice(row.Unit);
                choice.Tier = "Battalion"; choice.Branch = BattalionNamingService.Branch(row.Unit.Type);
            }
            _catalog.Save(_catalogDirectory);
        }
        _dirty = true;
        _battalionPreviewNumber = null;
        RefreshTree(); RefreshCatalogGrid();
        BattalionMessage.Text = $"Applied {chosen.Count} battalion names in memory. Save with backup to write the campaign.";
        Status.Text = BattalionMessage.Text;
    }
}
