using System.Windows;
using System.Windows.Controls;

namespace BattleplanADC;

public partial class MainWindow
{
    private MassOobPlan? _massPlan;
    private string _massPreviewSignature = "";
    private void InvalidateMassPreview()
    {
        _massPlan = null;
        if (MassGrid is not null) MassGrid.ItemsSource = null;
    }
    private string MassSignature() => $"{MassScopeBox.SelectedIndex}|{(MassScopeBox.SelectedIndex == 1 ? _selected?.Id : "")}|{MassChildPattern.Text.Trim()}";
    private void MassPreview_Click(object sender, RoutedEventArgs e)
    {
        // Preview commits number reservations immediately to keep future random picks stable.
        // Unit names remain untouched until Apply, and the .camp file until Save with backup.
        if (_campaign is null || _catalog is null) return;
        if (MassScopeBox.SelectedIndex == 1 && _selected is null)
        { MassMessage.Text = "Choose a group in the army list first."; return; }
        var scope = MassScopeBox.SelectedIndex == 0 ? _campaign.EditableUnits : _campaign.EditableUnits.Where(u => Descendant(u, _selected!));
        try
        {
            _massPlan = MassOobNamingService.Build(_campaign, _catalog, scope, MassChildPattern.Text.Trim());
            _catalog.Save(_catalogDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { MassMessage.Text = ex.Message; return; }
        _massPreviewSignature = MassSignature();
        MassGrid.ItemsSource = _massPlan.Rows;
        var review = _massPlan.Rows.Count(r => !r.Use);
        MassMessage.Text = $"{_massPlan.Rows.Count} names to review. {review} need your input. New numbers: {_massPlan.NewDivisionNumbers} divisions, {_massPlan.NewRegimentNumbers} regiments. " +
            string.Join(" ", _massPlan.Problems.Take(4));
    }
    private void MassApply_Click(object sender, RoutedEventArgs e)
    {
        // Validate the exact preview inputs and final names before mutating any unit.
        // Users can edit each proposed name or uncheck a row in the grid.
        if (_campaign is null || _massPlan is null || _massPreviewSignature != MassSignature())
        { MassMessage.Text = "Preview names first."; return; }
        MassGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        MassGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (_massPlan.Problems.Count > 0) { MassMessage.Text = string.Join(" ", _massPlan.Problems.Take(4)); return; }
        var chosen = _massPlan.Rows.Where(r => r.Use).ToList();
        if (chosen.Count == 0) { MassMessage.Text = "No rows are checked. Review the table or set command levels in the army list."; return; }
        if (_massPlan.Rows.Any(r => r.Unit.Name != r.Before)) { MassMessage.Text = "A name changed since preview. Preview again."; return; }
        if (chosen.Any(r => string.IsNullOrWhiteSpace(r.Proposed))) { MassMessage.Text = "A checked row has no proposed name. Enter one or uncheck it."; return; }
        foreach (var row in chosen.Where(r => r.Tier is "Division" or "Regiment" && r.Number is not null))
        {
            var leading = System.Text.RegularExpressions.Regex.Match(row.Proposed.Trim(), @"^\s*(\d+)");
            if (!leading.Success || !int.TryParse(leading.Groups[1].Value, out var proposedNumber) || proposedNumber != row.Number)
            { MassMessage.Text = $"{row.Unit.Name}: the proposed number differs from reserved number {row.Number}. Set the number on the OOB tree and preview again."; return; }
        }
        var changed = chosen.Where(r => !string.Equals(r.Proposed.Trim(), r.Before, StringComparison.Ordinal)).ToList();
        var unchangedNames = _campaign.EditableUnits.Where(u => changed.All(r => r.Unit != u)).Select(u => u.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var conflict = changed.FirstOrDefault(r => unchangedNames.Contains(r.Proposed.Trim()));
        if (conflict is not null) { MassMessage.Text = $"{conflict.Proposed.Trim()} is already used by another unit. Edit the preview or include that duplicate."; return; }
        var duplicate = changed.GroupBy(r => r.Proposed.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) { MassMessage.Text = $"{duplicate.Key} repeats in the preview. Edit the names."; return; }
        foreach (var row in changed) row.Unit.UserTypedName = row.Proposed.Trim();
        _dirty = changed.Count > 0 || _dirty;
        _massPlan = null;
        MassGrid.ItemsSource = null;
        RefreshTree(); RefreshCatalogGrid();
        MassMessage.Text = $"Applied {changed.Count} names in memory. Save with backup to write the campaign.";
        Status.Text = MassMessage.Text;
    }
    private static bool Descendant(Unit unit, Unit ancestor)
    {
        for (var p = unit; p is not null; p = p.Parent) if (p == ancestor) return true;
        return false;
    }
}
