namespace WinDock.Core;

/// <summary>Positions a group panel in physical desktop pixels, within its monitor work area.</summary>
public static class GroupGeometry
{
    /// <summary>Places a panel toward the inside of the dock's screen edge and clamps it to the work area.</summary>
    /// <param name="work">Monitor work area in physical pixels.</param>
    /// <param name="anchor">Group tile bounds in physical pixels.</param>
    /// <param name="edge">Dock edge.</param>
    /// <param name="width">Requested physical panel width.</param>
    /// <param name="height">Requested physical panel height.</param>
    /// <param name="scale">Monitor DPI scale.</param>
    /// <returns>Panel bounds within the work area.</returns>
    public static PixelRect Place(PixelRect work, PixelRect anchor, DockEdge edge, double width, double height, double scale)
    {
        width = Math.Min(width, work.Width); height = Math.Min(height, work.Height);
        var gap = 8 * scale;
        var x = edge switch { DockEdge.Left => anchor.Right + gap, DockEdge.Right => anchor.X - gap - width, _ => anchor.X + (anchor.Width - width) / 2 };
        var y = edge switch { DockEdge.Top => anchor.Bottom + gap, DockEdge.Bottom => anchor.Y - gap - height, _ => anchor.Y + (anchor.Height - height) / 2 };
        return new PixelRect(Math.Clamp(x, work.X, work.Right - width), Math.Clamp(y, work.Y, work.Bottom - height), width, height);
    }
}
