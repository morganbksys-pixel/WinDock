namespace WinDock.Core;

// All coordinates in this layer are physical desktop pixels, including negative monitor origins.
public readonly record struct PixelRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;
}
public readonly record struct DockPlacement(PixelRect Window, PixelRect Trigger);

public static class DockGeometry
{
    public static PixelRect IndicatorArea(PixelRect work, DockEdge edge, double scale)
    {
        var horizontal = edge is DockEdge.Top or DockEdge.Bottom;
        var width = Math.Min(work.Width, (horizontal ? 128 : 24) * scale);
        var height = Math.Min(work.Height, (horizontal ? 24 : 128) * scale);
        var x = work.X + (work.Width - width) / 2;
        var y = work.Y + (work.Height - height) / 2;
        return edge switch
        {
            DockEdge.Top => new(x, work.Y, width, height),
            DockEdge.Bottom => new(x, work.Bottom - height, width, height),
            DockEdge.Left => new(work.X, y, width, height),
            _ => new(work.Right - width, y, width, height)
        };
    }

    public static DockPlacement Calculate(PixelRect work, DockEdge edge, double desiredWidth, double desiredHeight, double scale)
    {
        var margin = 8 * scale;
        var width = Math.Min(desiredWidth * scale, Math.Max(1, work.Width - margin * 2));
        var height = Math.Min(desiredHeight * scale, Math.Max(1, work.Height - margin * 2));
        var x = work.X + (work.Width - width) / 2;
        var y = work.Y + (work.Height - height) / 2;
        switch (edge)
        {
            case DockEdge.Top: y = work.Y + margin; break;
            case DockEdge.Bottom: y = work.Bottom - margin - height; break;
            case DockEdge.Left: x = work.X + margin; break;
            case DockEdge.Right: x = work.Right - margin - width; break;
        }
        var trigger = edge switch
        {
            DockEdge.Top => new PixelRect(x, work.Y, width, 3),
            DockEdge.Bottom => new PixelRect(x, work.Bottom - 3, width, 3),
            DockEdge.Left => new PixelRect(work.X, y, 3, height),
            _ => new PixelRect(work.Right - 3, y, 3, height)
        };
        return new(new(x, y, width, height), trigger);
    }

    // Include the small gap between the activation strip and the visible dock.
    public static PixelRect InteractionArea(DockPlacement p) => new(
        Math.Min(p.Window.X, p.Trigger.X), Math.Min(p.Window.Y, p.Trigger.Y),
        Math.Max(p.Window.Right, p.Trigger.Right) - Math.Min(p.Window.X, p.Trigger.X),
        Math.Max(p.Window.Bottom, p.Trigger.Bottom) - Math.Min(p.Window.Y, p.Trigger.Y));
}
