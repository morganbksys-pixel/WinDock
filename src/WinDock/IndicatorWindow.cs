using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
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
    private readonly Path cornerMark = new()
    {
        Stroke = Brushes.White, StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        Width = 36, Height = 36, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 5, ShadowDepth = 0, Opacity = 0.85 },
        Visibility = Visibility.Collapsed
    };

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
        Content = new Grid { Children = { capsule, cornerMark } };
        SourceInitialized += (_, _) => Native.MakeClickThrough(new WindowInteropHelper(this).Handle);
    }

    public void Place(PixelRect area, DockEdge edge, double scale)
    {
        capsule.Visibility = Visibility.Visible;
        cornerMark.Visibility = Visibility.Collapsed;
        var horizontal = edge is DockEdge.Top or DockEdge.Bottom;
        capsule.Width = horizontal ? 96 : 5;
        capsule.Height = horizontal ? 5 : 96;
        Width = area.Width / scale;
        Height = area.Height / scale;
        Native.Position(new WindowInteropHelper(this).EnsureHandle(), area);
    }

    internal void Place(PixelRect area, DockLayout layout, double scale)
    {
        if (layout.Mode == DockPlacementMode.Edge) { Place(area, layout.Edge, scale); return; }
        capsule.Visibility = Visibility.Collapsed;
        cornerMark.Visibility = Visibility.Visible;
        var geometry = new StreamGeometry();
        using (var drawing = geometry.Open())
        {
            drawing.BeginFigure(new Point(6, 30), false, false);
            drawing.LineTo(new Point(6, 12), true, false);
            drawing.QuadraticBezierTo(new Point(6, 6), new Point(12, 6), true, false);
            drawing.LineTo(new Point(30, 6), true, false);
        }
        geometry.Transform = new MatrixTransform(layout.Right ? -1 : 1, 0, 0, layout.Bottom ? -1 : 1,
            layout.Right ? 36 : 0, layout.Bottom ? 36 : 0);
        geometry.Freeze();
        cornerMark.Data = geometry;
        Width = area.Width / scale;
        Height = area.Height / scale;
        Native.Position(new WindowInteropHelper(this).EnsureHandle(), area);
    }

    public void SetAppearance(bool visible, bool near, bool over)
    {
        var opacity = !visible ? 0 : over ? 0.95 : near ? 0.7 : 0.38;
        if (targetOpacity == opacity) return;
        targetOpacity = opacity;
        DockMotion.SetOpacity(this, opacity);
    }
}
