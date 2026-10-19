using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace BattleplanADC;

public partial class MainWindow
{
    // WPF TreeView is single-select. Maintain the actual batch set by game unit ID and
    // paint selected headers ourselves; _selected remains the current focus unit.
    private readonly HashSet<string> _oobSelectedIds = new(StringComparer.OrdinalIgnoreCase);
    private string? _oobAnchorId;
    private static readonly Brush OobSelectedBrush = new SolidColorBrush(Color.FromRgb(37, 74, 99));
    private static readonly Brush OobSelectedText = Brushes.White;

    private static T? Ancestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match) return match;
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
    }
    private IEnumerable<TreeViewItem> VisibleOobItems()
    {
        // Shift ranges and Select visible use what the player can see after search and
        // expansion, not every unit in the campaign.
        foreach (TreeViewItem root in UnitTree.Items)
            foreach (var item in Visible(root)) yield return item;
        static IEnumerable<TreeViewItem> Visible(TreeViewItem item)
        {
            yield return item;
            if (item.IsExpanded)
                foreach (TreeViewItem child in item.Items)
                    foreach (var nested in Visible(child)) yield return nested;
        }
    }
    private void UnitTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Ignore expander clicks and the inline TextBox. Handle selection before WPF's
        // default TreeView handler so Ctrl/Shift retains our multi-selection.
        if (e.OriginalSource is not DependencyObject source || Ancestor<ToggleButton>(source) is not null || Ancestor<TextBox>(source) is not null) return;
        var item = Ancestor<TreeViewItem>(source);
        if (item?.Tag is not Unit unit) return;
        var modifiers = Keyboard.Modifiers;
        if ((modifiers & ModifierKeys.Shift) != 0 && _oobAnchorId is not null)
        {
            var visible = VisibleOobItems().Select(x => x.Tag).OfType<Unit>().ToList();
            var first = visible.FindIndex(u => u.Id == _oobAnchorId);
            var last = visible.FindIndex(u => u.Id == unit.Id);
            if (first >= 0 && last >= 0)
            {
                if ((modifiers & ModifierKeys.Control) == 0) _oobSelectedIds.Clear();
                for (var i = Math.Min(first, last); i <= Math.Max(first, last); i++) _oobSelectedIds.Add(visible[i].Id);
            }
        }
        else if ((modifiers & ModifierKeys.Control) != 0)
        {
            if (!_oobSelectedIds.Add(unit.Id)) _oobSelectedIds.Remove(unit.Id);
            _oobAnchorId = unit.Id;
        }
        else
        {
            _oobSelectedIds.Clear();
            _oobSelectedIds.Add(unit.Id);
            _oobAnchorId = unit.Id;
        }
        _selected = unit;
        DisplaySelectedUnit();
        PaintAllOobSelections();
        CatalogGrid.SelectedItems.Clear();
        if (e.ClickCount == 2) BeginOobInlineEdit();
        e.Handled = true;
    }
    private void PaintOobSelection(TreeViewItem item)
    {
        if (item.Header is TextBlock text)
        {
            var selected = item.Tag is Unit unit && _oobSelectedIds.Contains(unit.Id);
            text.Background = selected ? OobSelectedBrush : Brushes.Transparent;
            text.Foreground = selected ? OobSelectedText : new SolidColorBrush(Color.FromRgb(23, 43, 56));
        }
    }
    private void PaintAllOobSelections()
    {
        foreach (TreeViewItem root in UnitTree.Items) Paint(root);
        void Paint(TreeViewItem item)
        {
            PaintOobSelection(item);
            foreach (TreeViewItem child in item.Items) Paint(child);
        }
        UpdateOobSelectionCount();
    }
    private void UpdateOobSelectionCount()
    {
        if (_campaign is null) return;
        CountLabel.Text = $"{_campaign.EditableUnits.Count()} units • {_oobSelectedIds.Count} selected • Ctrl/Shift to multi-select";
    }
    private void SelectVisible_Click(object sender, RoutedEventArgs e)
    {
        _oobSelectedIds.Clear();
        foreach (var unit in VisibleOobItems().Select(x => x.Tag).OfType<Unit>()) _oobSelectedIds.Add(unit.Id);
        CatalogGrid.SelectedItems.Clear();
        PaintAllOobSelections();
    }
    private void SelectBranch_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) { Status.Text = "Select a command in the OOB tree first."; return; }
        _oobSelectedIds.Clear();
        void Add(Unit unit) { _oobSelectedIds.Add(unit.Id); foreach (var child in unit.Children) Add(child); }
        Add(_selected);
        CatalogGrid.SelectedItems.Clear();
        PaintAllOobSelections();
    }
    private void ClearOobSelection_Click(object sender, RoutedEventArgs e)
    {
        _oobSelectedIds.Clear(); _oobAnchorId = null;
        CatalogGrid.SelectedItems.Clear();
        PaintAllOobSelections();
    }
    private void CatalogGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // The advanced grid and army tree are alternative batch selection sources.
        if (e.AddedItems.Count == 0 || _oobSelectedIds.Count == 0) return;
        _oobSelectedIds.Clear(); _oobAnchorId = null;
        PaintAllOobSelections();
    }
}
