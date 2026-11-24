using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WinDock;
using WinDock.Core;

internal static partial class Program
{
    private static void RunExactCornerTests(string temporary)
    {
        Run("Exact corner activation uses the work-area corner and excludes taskbars at every corner and DPI", () =>
        {
            foreach (var layout in CornerLayouts())
            foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
            {
                var bounds = new PixelRect(-2000, -700, 1920, 1080);
                var work = new PixelRect(-1910, -620, 1730, 930);
                var placement = DockGeometry.Calculate(bounds, work, layout, 300, 78, scale);
                var indicator = DockGeometry.IndicatorArea(bounds, work, layout, scale);
                var x = layout.Right ? work.Right - 1 : work.X;
                var y = layout.Bottom ? work.Bottom - 1 : work.Y;
                for (var dx = 0; dx < 2; dx++)
                for (var dy = 0; dy < 2; dy++)
                    Check(DockGeometry.ContainsActivation(placement, indicator, layout, true,
                        x + (layout.Right ? -dx : dx), y + (layout.Bottom ? -dy : dy)));
                for (var distance = (int)Math.Ceiling(10 * scale); distance < 36 * scale; distance++)
                {
                    var nearX = x + (layout.Right ? -distance : distance);
                    var nearY = y + (layout.Bottom ? -distance : distance);
                    Check(!DockGeometry.ContainsActivation(placement, indicator, layout, true, nearX, y));
                    Check(!DockGeometry.ContainsActivation(placement, indicator, layout, true, x, nearY));
                    Check(DockGeometry.ContainsActivation(placement, indicator, layout, false, nearX, nearY));
                }
                Check(!DockGeometry.ContainsActivation(placement, indicator, layout, true,
                    layout.Right ? work.Right : work.X - 1, y));
                Check(!DockGeometry.ContainsActivation(placement, indicator, layout, true,
                    x, layout.Bottom ? work.Bottom : work.Y - 1));
                foreach (var exact in new[] { false, true })
                    Check(!DockGeometry.ContainsActivation(placement, indicator, layout, exact,
                        layout.Right ? bounds.Right - 1 : bounds.X, layout.Bottom ? bounds.Bottom - 1 : bounds.Y));
                var edgeLayout = layout with { Mode = DockPlacementMode.Edge, Edge = DockEdge.Top };
                var edgePlacement = DockGeometry.Calculate(bounds, work, edgeLayout, 300, 78, scale);
                var edgeIndicator = DockGeometry.IndicatorArea(bounds, work, edgeLayout, scale);
                Check(DockGeometry.ContainsActivation(edgePlacement, edgeIndicator, edgeLayout, true,
                    edgePlacement.Trigger.X + 1, edgePlacement.Trigger.Y + 1));
            }
        });
        Run("Exact corner preference round trips and old configurations keep their activation mode", () =>
        {
            var path = Path.Combine(temporary, "exact-corner.json");
            var store = new SettingsStore(path);
            File.WriteAllText(path, "{\"PlacementMode\":\"Corner\",\"Corner\":\"TopRight\",\"Entries\":[]}");
            var settings = store.Load(out var warning);
            Check(warning is null && !settings.RequireExactCorner);
            foreach (var exact in new[] { true, false })
            {
                settings.RequireExactCorner = exact; store.Save(settings);
                var restored = store.Load(out warning);
                Check(warning is null && restored.RequireExactCorner == exact && restored.Corner == DockCorner.TopRight);
            }
        });
    }

    private static void RunExactCornerWindowTests(string temporary)
    {
        Run("Exact corner activation includes the visible blue dot above the taskbar at every corner and DPI", () =>
        {
            var bounds = new PixelRect(-1920, -400, 1920, 1080);
            var work = new PixelRect(-1860, -340, 1800, 960);
            var indicator = new IndicatorWindow();
            try
            {
                foreach (var layout in CornerLayouts())
                foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
                {
                    var area = DockGeometry.IndicatorArea(bounds, work, layout, scale);
                    indicator.Place(area, layout, scale);
                    var mark = ((System.Windows.Controls.Grid)indicator.Content).Children.OfType<System.Windows.Shapes.Path>().Single();
                    var dot = (System.Windows.Media.EllipseGeometry)mark.Data;
                    var settings = new DockSettings { PlacementMode = layout.Mode, Corner = layout.Corner,
                        Orientation = layout.Orientation, RequireExactCorner = true };
                    var window = new DockWindow(settings, new SettingsStore(Path.Combine(temporary, "exact-dot.json")), new FakeRecycleBin());
                    try
                    {
                        var placement = DockGeometry.Calculate(bounds, work, layout, 300, 78, scale);
                        var x = area.X + dot.Center.X * scale;
                        var y = area.Y + dot.Center.Y * scale;
                        var overDot = DockGeometry.ContainsActivation(placement, area, layout, true, x, y);
                        if (!overDot) throw new InvalidOperationException($"Le point visible ne déclenche pas le mode exact : {layout.Corner}/{layout.Orientation}, DPI {scale}.");
                        foreach (var offset in new[] { -2.5, 0, 2.5 })
                        {
                            Check(DockGeometry.ContainsActivation(placement, area, layout, true, x + offset * scale, y));
                            Check(DockGeometry.ContainsActivation(placement, area, layout, true, x, y + offset * scale));
                        }
                        window.UpdateVisibility(true, false, false, 100);
                        window.UpdateVisibility(false, overDot, true, 1000);
                        window.UpdateVisibility(false, overDot, true, 1299); Check(!window.IsHitTestVisible);
                        window.UpdateVisibility(false, overDot, true, 1300); Check(window.IsHitTestVisible);
                    }
                    finally { window.Close(); }
                }
            }
            finally { indicator.Close(); }
        });
        Run("Exact-corner dwell ignores nearby indicator pixels and cancels on departure", () =>
        {
            var settings = new DockSettings { PlacementMode = DockPlacementMode.Corner, Corner = DockCorner.TopRight, RequireExactCorner = true };
            var window = new DockWindow(settings, new SettingsStore(Path.Combine(temporary, "exact-dwell.json")), new FakeRecycleBin());
            try
            {
                var bounds = new PixelRect(-1920, -400, 1920, 1080);
                var placement = DockGeometry.Calculate(bounds, bounds, settings.Layout, 300, 78, 1.5);
                var indicator = DockGeometry.IndicatorArea(bounds, bounds, settings.Layout, 1.5);
                void Pointer(double x, double y, long now) => window.UpdateVisibility(false,
                    DockGeometry.ContainsActivation(placement, indicator, settings.Layout, settings.RequireExactCorner, x, y),
                    DockGeometry.ContainsInteraction(placement, settings.Layout, 1.5, x, y), now);
                window.UpdateVisibility(true, false, false, 100);
                Pointer(-20, -380, 1000); Pointer(-20, -380, 2000); Check(!window.IsHitTestVisible);
                Pointer(-1, -400, 3000); Pointer(-1, -400, 3299); Check(!window.IsHitTestVisible);
                Pointer(-16, -400, 3300); // Still over the 36-DIP indicator, outside the compact exact target.
                Pointer(-1, -400, 3400); Pointer(-1, -400, 3699); Check(!window.IsHitTestVisible);
                Pointer(-1, -400, 3700); Check(window.IsHitTestVisible);
                Pointer(-20, -380, 4000); Check(window.IsHitTestVisible); // The passage still retains an open dock.
                Check(new WindowInteropHelper(window).Handle == 0);
            }
            finally { window.Close(); }
        });
    }

    private static void RunMenuBackgroundTests()
    {
        Run("An isolated worker refuses to create windows on the current desktop", () =>
        {
            try { IsolatedDesktop.VerifyWorker("WinDock-tests-invalid"); throw new InvalidOperationException("Desktop isolation was not checked."); }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("Isolation absente", StringComparison.Ordinal)) { }
        });
        Run("Periodic positioning preserves menu stacking and keyboard focus on a simulated desktop", () =>
        {
            nint dock = 10, menu = 20, application = 30;
            var order = new List<nint> { menu, dock, application };
            var foreground = application;
            var positions = new Dictionary<nint, PixelRect>();
            bool Position(nint hwnd, nint after, int x, int y, int width, int height, uint flags)
            {
                if ((flags & 0x0004) == 0) { order.Remove(hwnd); order.Insert(0, hwnd); }
                if ((flags & 0x0010) == 0) foreground = hwnd;
                positions[hwnd] = new(x, y, width, height);
                return true;
            }
            foreach (var layout in AllLayouts())
            foreach (var scale in new[] { 1.0, 1.5, 2.0 })
            {
                var p = DockGeometry.Calculate(new(-1920, -400, 1920, 1080), new(-1920, -400, 1920, 1020), layout, 301, 79, scale);
                for (var cycle = 0; cycle < 3; cycle++) Native.Position(dock, p.Window, Position);
                Check(order.IndexOf(menu) < order.IndexOf(dock) && foreground == application);
                Check(positions[dock] == new PixelRect(Math.Round(p.Window.X), Math.Round(p.Window.Y), Math.Round(p.Window.Width), Math.Round(p.Window.Height)));
            }
        });
        Run("Menu dismissal distinguishes legitimate desktop input from a stacking failure without hooks", () =>
        {
            nint dock = 10, menu = 20, application = 30, other = 40;
            var open = true;
            var closes = 0;
            using var dismissal = new ContextMenuDismissal(() => open,
                p => new Rect(10, 10, 100, 100).Contains(p), handle => handle == menu,
                () => { open = false; closes++; }, dock, initialForeground: application, observeDesktop: false);
            dismissal.Poll(application, false); Check(open);
            dismissal.Poll(dock, false); Check(open);
            dismissal.Poll(menu, false); Check(open);
            dismissal.HandlePointerDown(new Point(50, 50));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); Check(open);
            dismissal.Poll(other, false); Check(!open && closes == 1);
            // The old assertion (open && menuAboveDock) fails here even with unchanged Z-order.
            open = true;
            dismissal.Poll(other, false); Check(open);
            dismissal.HandlePointerDown(new Point(0, 0));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); Check(!open && closes == 2);
            open = true;
            dismissal.Poll(other, true); Check(!open && closes == 3);
        });
    }
}
