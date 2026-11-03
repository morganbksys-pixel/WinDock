using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using WinDock.Core;

namespace WinDock;

// Separate from the dock so its opacity and hit testing stay independent.
internal sealed class IndicatorWindow : Window
{
    private readonly Border capsule = new()
    {
        Background = Brushes.White,
        CornerRadius = new CornerRadius(3),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 5, ShadowDepth = 0, Opacity = 0.85 }
    };
    private double targetOpacity;

    public IndicatorWindow()
    {
        Title = "WinDock — indicateur";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;
        Opacity = 0;
        Content = new Grid { Children = { capsule } };
        SourceInitialized += (_, _) => Native.MakeClickThrough(new WindowInteropHelper(this).Handle);
    }

    public void Place(PixelRect area, DockEdge edge, double scale)
    {
        var horizontal = edge is DockEdge.Top or DockEdge.Bottom;
        capsule.Width = horizontal ? 96 : 5;
        capsule.Height = horizontal ? 5 : 96;
        Width = area.Width / scale;
        Height = area.Height / scale;
        Native.Position(new WindowInteropHelper(this).EnsureHandle(), area);
    }

    public void SetAppearance(bool visible, bool near, bool over)
    {
        var opacity = !visible ? 0 : over ? 0.95 : near ? 0.7 : 0.38;
        if (targetOpacity == opacity) return;
        targetOpacity = opacity;
        BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(150)));
    }
}
