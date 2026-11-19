using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinDock;

internal sealed class DockSettingsView : Grid
{
    internal static Brush Surface { get; } = ColorBrush(245, 247, 251);
    internal static Brush Ink { get; } = ColorBrush(31, 43, 61);
    internal static Brush Muted { get; } = ColorBrush(78, 95, 116);

    internal StackPanel PositionPanel { get; } = new();
    internal StackPanel AppearancePanel { get; } = new();
    internal StackPanel GeneralPanel { get; } = new();
    internal StackPanel MenusPanel { get; } = new();
    internal StackPanel KeyboardPanel { get; } = new();
    internal TabControl Tabs { get; } = new();
    internal Button CloseButton { get; } = new() { Content = "Terminer", Padding = new Thickness(18, 8, 18, 8), IsCancel = true };
    internal TextBlock VersionLabel { get; } = new() { Foreground = Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };

    internal DockSettingsView()
    {
        Margin = new Thickness(20, 14, 20, 14);
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        Children.Add(new TextBlock { Text = "Réglages", Foreground = Ink, FontSize = 26, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12) });
        CloseButton.SetResourceReference(StyleProperty, "SettingsPrimaryButton");
        Tabs.SetResourceReference(StyleProperty, "SettingsTabs");
        foreach (var (label, panel) in new[]
            { ("Emplacement", PositionPanel), ("Apparence", AppearancePanel), ("Général", GeneralPanel), ("Clavier", KeyboardPanel), ("Menus", MenusPanel) })
        {
            var tab = new TabItem { Header = label, Content = new ScrollViewer
            {
                Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(16, 14, 16, 14),
                PanningMode = PanningMode.VerticalOnly
            } };
            tab.SetResourceReference(StyleProperty, "SettingsTab");
            AutomationProperties.SetName(tab, label); Tabs.Items.Add(tab);
        }
        Grid.SetRow(Tabs, 1); Children.Add(Tabs);
        var footer = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        footer.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        footer.Children.Add(VersionLabel); Grid.SetColumn(CloseButton, 1); footer.Children.Add(CloseButton);
        Grid.SetRow(footer, 2); Children.Add(footer);
    }

    internal IEnumerable<UIElement> SettingsControls => PositionPanel.Children.Cast<UIElement>()
        .Concat(AppearancePanel.Children.Cast<UIElement>()).Concat(GeneralPanel.Children.Cast<UIElement>())
        .Concat(KeyboardPanel.Children.Cast<UIElement>()).Concat(MenusPanel.Children.Cast<UIElement>());

    internal void SelectPage(string name) => Tabs.SelectedItem = Tabs.Items.OfType<TabItem>().Single(t => Equals(t.Header, name));

    private static SolidColorBrush ColorBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
