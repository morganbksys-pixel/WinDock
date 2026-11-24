using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinDock;

public sealed partial class DockWindow
{
    private void ApplyIconSize()
    {
        var scale = settings.IconSize / 48.0;
        var transform = new ScaleTransform(scale, scale);
        transform.Freeze();
        items.LayoutTransform = transform;
        if (hwnd != 0) RefreshPlacement();
        RefreshGroupPanel();
    }

    internal Grid CreateIconSizePicker()
    {
        var panel = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        panel.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var options = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(options);
        options.Children.Add(new TextBlock
        {
            Text = "Taille des icônes", Foreground = DockSettingsView.Ink, FontSize = 18,
            Margin = new Thickness(0, 0, 0, 8)
        });
        var choices = new WrapPanel();
        options.Children.Add(choices);
        var icon = new Image { Source = Icon, Width = settings.IconSize, Height = settings.IconSize };
        var preview = DockAppearance.Surface(ThemePalette.Surface, icon, new Thickness(4));
        preview.Width = preview.Height = 76;
        preview.Margin = new Thickness(10, 0, 0, 0);
        AutomationProperties.SetName(preview, "Aperçu de la taille des icônes");
        Grid.SetColumn(preview, 1);
        panel.Children.Add(preview);
        foreach (var (name, size) in new[] { ("Petite", 36), ("Standard", 48), ("Grande", 60) })
        {
            var choice = new RadioButton
            {
                Content = name, IsChecked = size == settings.IconSize, Margin = new Thickness(0, 2, 4, 2)
            };
            choice.SetResourceReference(StyleProperty, "PositionChoice");
            AutomationProperties.SetName(choice, $"Taille des icônes : {name}");
            choice.Checked += (_, _) =>
            {
                settings.IconSize = size;
                icon.Width = icon.Height = size;
                ApplyIconSize();
                Persist();
            };
            choices.Children.Add(choice);
        }
        return panel;
    }
}
