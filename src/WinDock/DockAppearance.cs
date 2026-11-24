using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinDock;

internal static class DockAppearance
{
    internal static Border Surface(Brush background, UIElement content, Thickness padding) => new()
    {
        Background = background, BorderBrush = ThemePalette.Border,
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = padding, Child = content
    };
}
