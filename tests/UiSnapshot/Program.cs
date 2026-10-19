using BattleplanADC;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length is < 1 or > 2) throw new ArgumentException("Output directory required; optional campaign path may follow.");
        Directory.CreateDirectory(args[0]);
        // Keep test previews and number reservations out of the player's real profile.
        Environment.SetEnvironmentVariable("BATTLEPLAN_ADC_CATALOG_DIR", Path.GetFullPath(Path.Combine(args[0], "catalogs")));
        _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow { Width = 1360, Height = 880 };
        // Use a known copied snapshot when the player's live campaign has moved on.
        if (args.Length == 2)
            typeof(MainWindow).GetMethod("TryOpen", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [args[1]]);
        var content = (FrameworkElement)window.Content;
        var tabs = Find<TabControl>(content) ?? throw new Exception("TabControl missing.");
        var tree = Find<TreeView>(content) ?? throw new Exception("OOB tree missing.");
        var root = (TreeViewItem)tree.Items[0];
        typeof(MainWindow).GetField("_selected", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, root.Tag);
        // Placeholder labels are visual prompts, never valid saved command classifications.
        Invoke("OobSetTier_Click");
        var initialCatalog = (NamingCatalog)typeof(MainWindow).GetField("_catalog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        if (initialCatalog.Choice((Unit)root.Tag).Tier.Length != 0)
            throw new Exception("Command-level placeholder was saved as a real tier.");
        Invoke("SelectBranch_Click");
        var selected = (System.Collections.Generic.HashSet<string>)typeof(MainWindow).GetField("_oobSelectedIds", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        if (selected.Count != 79) throw new Exception($"Branch selection found {selected.Count} units, expected 79.");
        Invoke("ClearOobSelection_Click");
        if (selected.Count != 0) throw new Exception("Clear selection did not clear OOB units.");
        Invoke("SelectVisible_Click");
        if (selected.Count < 2) throw new Exception("Select visible did not select displayed OOB units.");
        var batchRows = (System.Collections.ICollection)typeof(MainWindow).GetMethod("SelectedCatalogRows", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
        if (batchRows.Count != selected.Count) throw new Exception("OOB selection was not passed to batch actions.");
        Invoke("ClearOobSelection_Click");
        var regiment = All((Unit)root.Tag).First(u => u.UserTypedName == "1IR");
        typeof(MainWindow).GetField("_selected", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, regiment);
        Invoke("BattalionLoad_Click");
        var battalionRows = (System.Collections.Generic.List<BattalionRenameRow>)typeof(MainWindow).GetField("_battalionRows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        if (!battalionRows.Any(r => r.Proposed == "5-1 TD") || !battalionRows.Any(r => r.Proposed == "6-1 ARMENG"))
            throw new Exception("Battalion preview did not generate expected names.");
        Render(0, "battalion-names.png");
        var catalog = (NamingCatalog)typeof(MainWindow).GetField("_catalog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        foreach (var u in All((Unit)root.Tag).Where(u => u.Type == 11 && System.Text.RegularExpressions.Regex.IsMatch(u.UserTypedName, @"^\d+IR$")))
        { var c = catalog.Choice(u); c.Tier = "Regiment"; c.Branch = "Infantry"; }
        foreach (var u in All((Unit)root.Tag).Where(u => u.Type == 11 && u.Children.Any(c => c.UserTypedName == "1IR" || c.UserTypedName == "21IR")))
        { var c = catalog.Choice(u); c.Tier = "Division"; c.Branch = "Infantry"; }
        Invoke("MassPreview_Click");
        var mass = (MassOobPlan)typeof(MainWindow).GetField("_massPlan", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        if (mass.Rows.Count < 10) throw new Exception("Mass OOB preview did not include cloned formation descendants.");
        Render(1, "mass-oob-naming.png");
        ((CheckBox)typeof(MainWindow).GetField("ShowMoreToolsBox", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(window)!).IsChecked = true;
        Render(2, "advanced-naming.png");
        Render(3, "unit-editor.png");
        Render(5, "data-map.png");
        var renamedRegiment = mass.Rows.First(r => r.Tier == "Regiment" && r.Proposed != r.Current);
        Invoke("MassApply_Click");
        if (renamedRegiment.Unit.UserTypedName != renamedRegiment.Proposed)
            throw new Exception("Mass OOB preview did not apply selected regiment names in memory.");
        Invoke("BattalionApply_Click");
        if (!battalionRows.Any(r => r.Unit.UserTypedName == "5-1 TD") || !battalionRows.Any(r => r.Unit.UserTypedName == "6-1 ARMENG"))
            throw new Exception("Battalion names were not applied in memory.");
        typeof(MainWindow).GetField("_dirty", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false);
        window.Close();
        Application.Current.Shutdown();

        void Invoke(string method) => typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [window, new RoutedEventArgs()]);

        static IEnumerable<Unit> All(Unit unit)
        {
            yield return unit;
            foreach (var child in unit.Children)
                foreach (var nested in All(child)) yield return nested;
        }

        void Render(int index, string name)
        {
            tabs.SelectedIndex = index;
            content.Measure(new Size(1360, 880));
            content.Arrange(new Rect(0, 0, 1360, 880));
            content.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1360, 880, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(args[0], name));
            encoder.Save(stream);
        }
    }

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (Find<T>(VisualTreeHelper.GetChild(root, i)) is { } result) return result;
        return null;
    }
}
