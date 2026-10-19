using System.Windows;

namespace BattleplanADC;

public partial class MainWindow
{
    // Keep the three common rename screens visible. Advanced tools remain available
    // without letting their terminology dominate the initial workflow.
    private void ShowMoreTools_Changed(object sender, RoutedEventArgs e)
    {
        if (AdvancedTab is null || DataTab is null || FindTab is null || MainTabs is null) return;
        var show = ShowMoreToolsBox.IsChecked == true;
        if (!show && (MainTabs.SelectedItem == AdvancedTab || MainTabs.SelectedItem == DataTab || MainTabs.SelectedItem == FindTab))
            MainTabs.SelectedIndex = 1;
        var visibility = show ? Visibility.Visible : Visibility.Collapsed;
        AdvancedTab.Visibility = visibility;
        DataTab.Visibility = visibility;
        FindTab.Visibility = visibility;
    }
}
