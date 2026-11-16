using System.Windows;
using System.Windows.Media.Animation;

namespace WinDock;

internal static class DockMotion
{
    // Set the final value independently of the animation, including when interrupted or disabled.
    internal static void SetOpacity(UIElement element, double opacity)
    {
        var current = element.Opacity;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = opacity;
        if (SystemParameters.ClientAreaAnimation && current != opacity)
            element.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(current, opacity, TimeSpan.FromMilliseconds(150)) { FillBehavior = FillBehavior.Stop });
    }
}
