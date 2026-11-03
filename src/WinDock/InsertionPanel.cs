using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinDock;

/// <summary>Paints a drop marker without changing layout or moving hit-test targets.</summary>
internal sealed class InsertionPanel : StackPanel
{
    private double? insertionOffset;

    internal void ShowInsertion(double? offset)
    {
        if (insertionOffset == offset) return;
        insertionOffset = offset;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (insertionOffset is not { } offset) return;
        var horizontal = Orientation == Orientation.Horizontal;
        var across = horizontal ? ActualHeight : ActualWidth;
        var brush = Brushes.DeepSkyBlue;
        var start = horizontal ? new Point(offset, 8) : new Point(8, offset);
        var end = horizontal ? new Point(offset, across - 8) : new Point(across - 8, offset);
        drawingContext.DrawLine(new Pen(brush, 3), start, end);
        drawingContext.DrawEllipse(brush, null, start, 3, 3);
        drawingContext.DrawEllipse(brush, null, end, 3, 3);
    }
}
