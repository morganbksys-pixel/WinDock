namespace WinDock.Core;

/// <summary>Available dock anchor families.</summary>
public enum DockPlacementMode { Edge, Corner }
/// <summary>Physical corners of the selected monitor.</summary>
public enum DockCorner { TopLeft, TopRight, BottomLeft, BottomRight }
/// <summary>Main axis of a corner dock.</summary>
public enum DockOrientation { Horizontal, Vertical }

/// <summary>Resolves placement, item direction and the inward side for dock popups.</summary>
/// <param name="Mode">Anchor family.</param>
/// <param name="Edge">Retained edge-mode position.</param>
/// <param name="Corner">Corner-mode position.</param>
/// <param name="Orientation">Corner-mode main axis.</param>
/// <param name="ReverseIconOrder">Whether to reverse the root icon row in corner mode.</param>
public readonly record struct DockLayout(DockPlacementMode Mode, DockEdge Edge, DockCorner Corner, DockOrientation Orientation, bool ReverseIconOrder = false)
{
    /// <summary>Whether the main axis runs horizontally.</summary>
    public bool Horizontal => Mode == DockPlacementMode.Corner
        ? Orientation == DockOrientation.Horizontal : Edge is DockEdge.Top or DockEdge.Bottom;
    /// <summary>Whether the corner is on the right of the monitor.</summary>
    public bool Right => Corner is DockCorner.TopRight or DockCorner.BottomRight;
    /// <summary>Whether the corner is at the bottom of the monitor.</summary>
    public bool Bottom => Corner is DockCorner.BottomLeft or DockCorner.BottomRight;
    /// <summary>Whether physical item order is the reverse of stored order.</summary>
    public bool Reversed => Mode == DockPlacementMode.Corner && ((Horizontal ? Right : Bottom) ^ ReverseIconOrder);
    /// <summary>Edge whose inward normal determines popup placement.</summary>
    public DockEdge EffectiveEdge => Mode == DockPlacementMode.Edge ? Edge
        : Horizontal ? Bottom ? DockEdge.Bottom : DockEdge.Top
        : Right ? DockEdge.Right : DockEdge.Left;
}
