using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;
using WinDock.Core;

namespace WinDock;

public sealed partial class DockWindow
{
    private bool darkTheme;

    private void RefreshTheme()
    {
        darkTheme = settings.Theme == DockTheme.Dark ||
            settings.Theme == DockTheme.Automatic && readWindowsDarkTheme();
        ThemePalette.Apply(darkTheme);
        dockBackground.Color = ThemePalette.Surface.Color;
        if (activeMenu is not null) DockMenuTheme.Apply(activeMenu, darkTheme, SystemParameters.HighContrast);
    }

    internal StackPanel CreateThemePicker()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        panel.Children.Add(new TextBlock
        {
            Text = "Thème", Foreground = ThemePalette.Text, FontSize = 18, Margin = new Thickness(0, 0, 0, 8)
        });
        var choices = new WrapPanel();
        panel.Children.Add(choices);
        foreach (var (label, mode) in new[] { ("Clair", DockTheme.Light), ("Sombre", DockTheme.Dark), ("Automatique", DockTheme.Automatic) })
        {
            var choice = new RadioButton
            {
                Content = label, IsChecked = settings.Theme == mode, Margin = new Thickness(0, 2, 4, 2)
            };
            AutomationProperties.SetName(choice, $"Thème {label}");
            choice.SetResourceReference(StyleProperty, "PositionChoice");
            choice.Checked += (_, _) =>
            {
                settings.Theme = mode;
                RefreshTheme();
                Persist();
            };
            choices.Children.Add(choice);
        }
        panel.Children.Add(new TextBlock
        {
            Text = "Automatique suit le thème des applications Windows.", Foreground = ThemePalette.Muted,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0)
        });
        return panel;
    }
}

// WinDock has one UI dispatcher. Live brushes update controls built in code,
// including detached tooltips. Bind template brushes so WPF cannot freeze their colors.
internal static class ThemePalette
{
    private sealed class Tone(string light, string dark, Func<Color> contrast)
    {
        internal Color Light { get; } = Parse(light);
        internal Color Dark { get; } = Parse(dark);
        internal Func<Color> Contrast { get; } = contrast;
        internal SolidColorBrush Brush { get; } = new(Parse(light));
        private SolidColorBrush? templateBrush;
        internal SolidColorBrush TemplateBrush
        {
            get
            {
                if (templateBrush is not null) return templateBrush;
                templateBrush = new SolidColorBrush();
                BindingOperations.SetBinding(templateBrush, SolidColorBrush.ColorProperty,
                    new Binding(nameof(SolidColorBrush.Color)) { Source = Brush, Mode = BindingMode.OneWay });
                return templateBrush;
            }
        }

        private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value);
    }

    private static readonly Dictionary<string, Tone> colors = new()
    {
        ["Window"] = new("#F5F7FB", "#161920", () => SystemColors.WindowColor),
        ["Surface"] = new("#FAFBFD", "#1B1E27", () => SystemColors.WindowColor),
        ["Text"] = new("#1F2B3D", "#F3F4F8", () => SystemColors.WindowTextColor),
        ["Muted"] = new("#4E5F74", "#BEC8D8", () => SystemColors.WindowTextColor),
        ["Border"] = new("#BCCBDD", "#505A6C", () => SystemColors.WindowTextColor),
        ["Divider"] = new("#8292A5", "#858D9B", () => SystemColors.WindowTextColor),
        ["Raised"] = new("#E9EFF7", "#333B49", () => SystemColors.ControlColor),
        ["Hover"] = new("#DBE7F4", "#424E5F", () => SystemColors.ControlColor),
        ["Selected"] = new("#D9EAFE", "#304966", () => SystemColors.HighlightColor),
        ["AccentText"] = new("#164777", "#DBEBFF", () => SystemColors.HighlightTextColor),
        ["Focus"] = new("#346BA5", "#91BDFA", () => SystemColors.HighlightColor)
    };

    internal static SolidColorBrush Window => colors["Window"].Brush;
    internal static SolidColorBrush Surface => colors["Surface"].Brush;
    internal static SolidColorBrush Text => colors["Text"].Brush;
    internal static SolidColorBrush Muted => colors["Muted"].Brush;
    internal static SolidColorBrush Border => colors["Border"].Brush;
    internal static SolidColorBrush Divider => colors["Divider"].Brush;
    internal static SolidColorBrush Raised => colors["Raised"].Brush;

    internal static void Apply(bool dark)
    {
        var resources = Application.Current?.Resources;
        foreach (var (name, tone) in colors)
        {
            var color = SystemParameters.HighContrast ? tone.Contrast() : dark ? tone.Dark : tone.Light;
            if (tone.Brush.Color != color) tone.Brush.Color = color;
            var key = "Theme" + name;
            if (resources is not null && resources[key] != tone.TemplateBrush) resources[key] = tone.TemplateBrush;
        }
    }
}
