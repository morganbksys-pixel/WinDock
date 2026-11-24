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
    /// <summary>Tests the activation area independently of the indicator's appearance and retention passage.</summary>
    /// <param name="placement">Physical dock and edge trigger bounds.</param>
    /// <param name="indicator">Physical indicator bounds.</param>
    /// <param name="layout">Resolved anchor and orientation.</param>
    /// <param name="requireExactCorner">Whether to restrict activation to the visible dot and work-area corner.</param>
    /// <param name="x">Physical cursor X coordinate.</param>
    /// <param name="y">Physical cursor Y coordinate.</param>
    /// <returns>Whether the cursor should begin or continue the reveal delay.</returns>
    public static bool ContainsActivation(DockPlacement placement, PixelRect indicator, DockLayout layout, bool requireExactCorner, double x, double y)
    {
        if (layout.Mode == DockPlacementMode.Edge)
            return indicator.Contains(x, y) || placement.Trigger.Contains(x, y);
        if (!requireExactCorner) return indicator.Contains(x, y);
        // The 36-DIP indicator draws the dot 7 DIPs from the corner, with a 2.5-DIP radius.
        // Include the whole dot and the corner in a compact 10-DIP target at the same DPI.
        var width = indicator.Width * 10 / 36;
        var height = indicator.Height * 10 / 36;
        var exactCorner = new PixelRect(layout.Right ? indicator.Right - width : indicator.X,
            layout.Bottom ? indicator.Bottom - height : indicator.Y, width, height);
        return exactCorner.Contains(x, y);
    }

    /// <summary>Places the indicator on an edge or in a corner of the available work area.</summary>
    /// <param name="bounds">Physical monitor bounds.</param>
    /// <param name="work">Available monitor work area.</param>
    /// <param name="layout">Resolved anchor and orientation.</param>
    /// <param name="scale">Monitor DPI scale.</param>
    /// <returns>Physical indicator and corner activation bounds.</returns>
    public static PixelRect IndicatorArea(PixelRect bounds, PixelRect work, DockLayout layout, double scale)
    {
        if (layout.Mode == DockPlacementMode.Edge) return IndicatorArea(work, layout.Edge, scale);
        var width = Math.Min(work.Width, 36 * scale);
        var height = Math.Min(work.Height, 36 * scale);
        return new(layout.Right ? work.Right - width : work.X, layout.Bottom ? work.Bottom - height : work.Y, width, height);
    }

    /// <summary>Anchors the dock and its activation area inside the available work area.</summary>
    /// <param name="bounds">Physical monitor bounds.</param>
    /// <param name="work">Available monitor work area.</param>
    /// <param name="layout">Resolved anchor and orientation.</param>
    /// <param name="desiredWidth">Desired width in device-independent units.</param>
    /// <param name="desiredHeight">Desired height in device-independent units.</param>
    /// <param name="scale">Monitor DPI scale.</param>
    /// <returns>Physical dock and activation bounds.</returns>
    public static DockPlacement Calculate(PixelRect bounds, PixelRect work, DockLayout layout, double desiredWidth, double desiredHeight, double scale)
    {
        if (layout.Mode == DockPlacementMode.Edge) return Calculate(work, layout.Edge, desiredWidth, desiredHeight, scale);
        var marginX = Math.Min(8 * scale, Math.Max(0, (work.Width - 1) / 2));
        var marginY = Math.Min(8 * scale, Math.Max(0, (work.Height - 1) / 2));
        var width = Math.Min(desiredWidth * scale, work.Width - marginX * 2);
        var height = Math.Min(desiredHeight * scale, work.Height - marginY * 2);
        var x = layout.Right ? work.Right - marginX - width : work.X + marginX;
        var y = layout.Bottom ? work.Bottom - marginY - height : work.Y + marginY;
        return new(new(x, y, width, height), IndicatorArea(bounds, work, layout, scale));
    }

    /// <summary>Tests the dock and a compact passage from the corner, without retaining the whole adjacent edge.</summary>
    /// <param name="placement">Dock and activation bounds.</param>
    /// <param name="layout">Resolved anchor and orientation.</param>
    /// <param name="scale">Monitor DPI scale.</param>
    /// <param name="x">Physical cursor X coordinate.</param>
    /// <param name="y">Physical cursor Y coordinate.</param>
    /// <returns>Whether the cursor should keep the dock open.</returns>
    public static bool ContainsInteraction(DockPlacement placement, DockLayout layout, double scale, double x, double y)
    {
        if (layout.Mode == DockPlacementMode.Edge) return InteractionArea(placement).Contains(x, y);
        if (placement.Window.Contains(x, y)) return true;
        var window = placement.Window;
        var width = layout.Horizontal ? Math.Min(window.Width, 68 * scale) : window.Width;
        var height = layout.Horizontal ? window.Height : Math.Min(window.Height, 68 * scale);
        var firstTile = new PixelRect(layout.Right ? window.Right - width : window.X,
            layout.Bottom ? window.Bottom - height : window.Y, width, height);
        return InteractionArea(new(firstTile, placement.Trigger)).Contains(x, y);
    }

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
