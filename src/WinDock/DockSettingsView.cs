using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinDock;

internal sealed class DockSettingsView : Grid
{
    internal StackPanel PositionPanel { get; } = new();
    internal StackPanel AppearancePanel { get; } = new();
    internal StackPanel GeneralPanel { get; } = new();
    internal TabControl Tabs { get; } = new();
    internal Button CloseButton { get; } = new() { Content = "Terminer", Padding = new Thickness(20, 10, 20, 10), Background = new SolidColorBrush(Color.FromRgb(62, 77, 111)), IsCancel = true };
    internal TextBlock VersionLabel { get; } = new() { Foreground = Brushes.LightGray, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };

    internal DockSettingsView()
    {
        Margin = new Thickness(24, 18, 24, 18);
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        header.Children.Add(new TextBlock { Text = "Réglages", Foreground = Brushes.White, FontSize = 24 });
        header.Children.Add(new TextBlock { Text = "Les changements sont appliqués immédiatement.", Foreground = Brushes.LightGray, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        Children.Add(header);
        Tabs.SetResourceReference(StyleProperty, "SettingsTabs");
        foreach (var (label, panel) in new[] { ("Emplacement", PositionPanel), ("Apparence", AppearancePanel), ("Général", GeneralPanel) })
        {
            var tab = new TabItem { Header = label, Content = new ScrollViewer
            {
                Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 16, 10, 0)
            } };
            tab.SetResourceReference(StyleProperty, "SettingsTab");
            AutomationProperties.SetName(tab, label); Tabs.Items.Add(tab);
        }
        Grid.SetRow(Tabs, 1); Children.Add(Tabs);
        var footer = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        footer.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        footer.Children.Add(VersionLabel); Grid.SetColumn(CloseButton, 1); footer.Children.Add(CloseButton);
        Grid.SetRow(footer, 2); Children.Add(footer);
    }

    internal IEnumerable<UIElement> SettingsControls => PositionPanel.Children.Cast<UIElement>()
        .Concat(AppearancePanel.Children.Cast<UIElement>()).Concat(GeneralPanel.Children.Cast<UIElement>());

    internal void SelectPage(string name) => Tabs.SelectedItem = Tabs.Items.OfType<TabItem>().Single(t => Equals(t.Header, name));
}
