using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BattleplanADC;

public partial class MainWindow
{
    // A selection in the tree identifies game units; classification lives in the editor catalog.
    // Battleplan's generic Headquarters type cannot tell us whether this is a regiment or army.
    private void OobSelectedUnitChanged()
    {
        if (OobChoiceLabel is null || _catalog is null) return;
        if (_selected is null) { OobChoiceLabel.Text = ""; return; }
        var choice = _catalog.Choice(_selected);
        OobChoiceLabel.Text = $"{(choice.Tier.Length == 0 ? "Unclassified" : choice.Tier)}{(choice.Number is null ? "" : $" #{choice.Number}")}";
        OobNumberBox.Text = choice.Number?.ToString() ?? "";
    }
    private void OobSetTier_Click(object sender, RoutedEventArgs e)
    {
        if (_catalog is null || _campaign is null) return;
        var tier = Selection(OobTierBox); var branch = Selection(OobBranchBox);
        if (OobTierBox.SelectedIndex <= 0 || OobBranchBox.SelectedIndex <= 0)
        { Status.Text = "Choose a command level and unit type for the selected units."; return; }
        var ids = _oobSelectedIds.Count > 0 ? _oobSelectedIds : _selected is null ? [] : new HashSet<string> { _selected.Id };
        if (ids.Count == 0) { Status.Text = "Select OOB units first."; return; }
        foreach (var unit in _campaign.EditableUnits.Where(u => ids.Contains(u.Id)))
        {
            var choice = _catalog.Choice(unit);
            choice.Tier = tier; choice.Branch = branch;
        }
        _catalog.Save(_catalogDirectory);
        InvalidateMassPreview();
        RefreshCatalogGrid(); OobSelectedUnitChanged();
        Status.Text = $"Assigned {tier} / {branch} to {ids.Count} selected OOB units.";
    }
    private void OobSetNumber_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _catalog is null || _oobSelectedIds.Count > 1 || !int.TryParse(OobNumberBox.Text, out var number) || number <= 0)
        { Status.Text = "Select one OOB unit and enter a positive number."; return; }
        var choice = _catalog.Choice(_selected);
        // Manual numbers join the same permanent ledger used by the random allocator.
        // Old reservations stay occupied even after a unit is renamed or renumbered.
        if (choice.Tier is "Division" or "Regiment")
        {
            var owner = _catalog.ReservedNumbers.FirstOrDefault(r => r.Tier == choice.Tier && r.Number == number && r.UnitId != _selected.Id);
            if (owner is not null) { Status.Text = $"{choice.Tier} number {number} is already reserved for another unit."; return; }
            if (!_catalog.ReservedNumbers.Any(r => r.Tier == choice.Tier && r.Number == number && r.UnitId == _selected.Id))
                _catalog.ReservedNumbers.Add(new NumberReservation { Tier = choice.Tier, Number = number, UnitId = _selected.Id });
        }
        choice.Number = number; choice.NumberOverride = true;
        _catalog.Save(_catalogDirectory);
        InvalidateMassPreview();
        RefreshCatalogGrid(); OobSelectedUnitChanged();
        Status.Text = $"Assigned number {number} to {_selected.Name}. Earlier reservations remain unavailable.";
    }
    private void OobEditName_Click(object sender, RoutedEventArgs e) => BeginOobInlineEdit();
    private void UnitTree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2) { BeginOobInlineEdit(); e.Handled = true; }
    }
    private void BeginOobInlineEdit()
    {
        if (_selected is null) { Status.Text = "Select an OOB unit to edit its name."; return; }
        TreeViewItem? Find(TreeViewItem item)
        {
            if ((item.Tag as Unit)?.Id == _selected.Id) return item;
            foreach (TreeViewItem child in item.Items) { var found = Find(child); if (found is not null) return found; }
            return null;
        }
        var item = UnitTree.Items.OfType<TreeViewItem>().Select(Find).FirstOrDefault(x => x is not null);
        if (item is null) { Status.Text = "Expand or find the selected unit in the tree first."; return; }
        var unit = _selected;
        var edit = new TextBox { Text = unit.Name, MinWidth = 155, MaxWidth = 350 };
        item.Header = edit;
        var finished = false;
        // Inline editing writes only UserTypedName in memory. Campaign.Save owns disk writes.
        void Finish(bool commit)
        {
            if (finished) return;
            finished = true;
            if (commit && edit.Text.Trim() != unit.Name)
            {
                unit.UserTypedName = edit.Text.Trim();
                _dirty = true;
                InvalidateMassPreview();
                RefreshCatalogGrid();
                Status.Text = $"Renamed {unit.Name} in memory. Save with backup to write the campaign.";
            }
            RefreshTree();
            DisplaySelectedUnit();
        }
        edit.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter) { Finish(true); args.Handled = true; }
            else if (args.Key == Key.Escape) { Finish(false); args.Handled = true; }
        };
        edit.LostKeyboardFocus += (_, _) => Finish(true);
        edit.Focus(); edit.SelectAll();
    }
}
