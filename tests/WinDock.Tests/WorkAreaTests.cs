using System.IO;
using System.Windows.Interop;
using WinDock;
using WinDock.Core;

internal static partial class Program
{
    private static void RunWorkAreaWindowTests(string temporary)
    {
        Run("Taskbar moves refresh the indicator and both activation modes and restart pending corner dwell", () =>
        {
            var bounds = new PixelRect(-1920, -400, 1920, 1080);
            var workAreas = new PixelRect[]
            {
                new(-1920, -400, 1920, 1020), // Bottom taskbar.
                new(-1920, -400, 1860, 1080), // Right taskbar.
                new(-1920, -340, 1920, 1020), // Top taskbar.
                new(-1860, -400, 1860, 1080), // Left taskbar.
                bounds // Taskbar hidden or removed.
            };
            var refresh = typeof(DockWindow).GetMethod("RefreshPlacement", PrivateInstance)!;
            var indicatorField = typeof(DockWindow).GetField("indicatorArea", PrivateInstance)!;
            var placementField = typeof(DockWindow).GetField("placement", PrivateInstance)!;
            foreach (var layout in CornerLayouts())
            foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
            foreach (var exact in new[] { false, true })
            {
                var current = new Display(1, "simulated-monitor", bounds, bounds, true, scale);
                var settings = new DockSettings { PlacementMode = layout.Mode, Corner = layout.Corner,
                    Orientation = layout.Orientation, RequireExactCorner = exact, MonitorDevice = current.Device };
                var window = new DockWindow(settings, new SettingsStore(Path.Combine(temporary, "taskbar-move.json")),
                    new FakeRecycleBin(), getDisplays: () => [current]);
                try
                {
                    refresh.Invoke(window, null);
                    foreach (var work in workAreas)
                    {
                        var oldIndicator = (PixelRect)indicatorField.GetValue(window)!;
                        var oldPlacement = (DockPlacement)placementField.GetValue(window)!;
                        var oldX = layout.Right ? oldIndicator.Right - 1 : oldIndicator.X;
                        var oldY = layout.Bottom ? oldIndicator.Bottom - 1 : oldIndicator.Y;
                        window.UpdateVisibility(true, false, false, 100);
                        window.UpdateVisibility(false, DockGeometry.ContainsActivation(oldPlacement, oldIndicator, layout, exact, oldX, oldY), true, 1000);
                        window.UpdateVisibility(false, true, true, 1299);
                        Check(!window.IsHitTestVisible);

                        // Exercise the same refresh used by WM_SETTINGCHANGE and the one-second poll.
                        current = current with { Work = work };
                        refresh.Invoke(window, null);
                        var indicator = (PixelRect)indicatorField.GetValue(window)!;
                        var placement = (DockPlacement)placementField.GetValue(window)!;
                        Check(placement.Trigger == indicator);
                        Check(layout.Right ? indicator.Right == work.Right : indicator.X == work.X);
                        Check(layout.Bottom ? indicator.Bottom == work.Bottom : indicator.Y == work.Y);
                        Check(indicator.X >= work.X && indicator.Y >= work.Y && indicator.Right <= work.Right && indicator.Bottom <= work.Bottom);
                        Check(placement.Window.X >= work.X && placement.Window.Y >= work.Y && placement.Window.Right <= work.Right && placement.Window.Bottom <= work.Bottom);
                        if (!indicator.Contains(oldX, oldY))
                            Check(!DockGeometry.ContainsActivation(placement, indicator, layout, exact, oldX, oldY));

                        var x = layout.Right ? work.Right - 7 * scale : work.X + 7 * scale;
                        var y = layout.Bottom ? work.Bottom - 7 * scale : work.Y + 7 * scale;
                        Check(DockGeometry.ContainsActivation(placement, indicator, layout, exact, x, y));
                        void Hover(long now) => window.UpdateVisibility(false,
                            DockGeometry.ContainsActivation(placement, indicator, layout, exact, x, y),
                            indicator.Contains(x, y) || DockGeometry.ContainsInteraction(placement, layout, scale, x, y), now);
                        Hover(1300);
                        Check(!window.IsHitTestVisible); // The old dwell must not open the moved dock.
                        Hover(1599);
                        Check(!window.IsHitTestVisible);
                        Hover(1600);
                        Check(window.IsHitTestVisible);
                    }
                    Check(new WindowInteropHelper(window).Handle == 0);
                }
                finally { window.Close(); }
            }
        });
    }
}
