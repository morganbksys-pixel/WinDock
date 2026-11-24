using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinDock;
using WinDock.Core;
using Path = System.IO.Path;

internal static partial class Program
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private static IEnumerable<DockLayout> CornerLayouts() =>
        from corner in Enum.GetValues<DockCorner>()
        from orientation in Enum.GetValues<DockOrientation>()
        select new DockLayout(DockPlacementMode.Corner, DockEdge.Bottom, corner, orientation);

    private static IEnumerable<DockLayout> AllLayouts() => Enum.GetValues<DockEdge>()
        .Select(edge => new DockLayout(DockPlacementMode.Edge, edge, DockCorner.BottomRight, DockOrientation.Horizontal))
        .Concat(CornerLayouts());

    private static void RunCornerCoreTests(string temporary)
    {
        RunReverseIconOrderCoreTests(temporary);
        RunExactCornerTests(temporary);
        Run("Corner settings retain legacy edges and round trip all eight layouts without changing entries", () =>
        {
            var path = Path.Combine(temporary, "corner-settings.json");
            File.WriteAllText(path, "{\"Edge\":\"Left\",\"AutoHide\":false,\"Entries\":[]}");
            var store = new SettingsStore(path);
            var legacy = store.Load(out var warning);
            Check(warning is null && legacy.PlacementMode == DockPlacementMode.Edge && legacy.Layout.EffectiveEdge == DockEdge.Left && !legacy.AutoHide);
            Check(legacy.Corner == DockCorner.BottomRight && legacy.Orientation == DockOrientation.Horizontal);
            var entries = new List<DockEntry> { new(Guid.NewGuid(), temporary), new(Guid.NewGuid(), "", true) };
            foreach (var layout in CornerLayouts())
            {
                var settings = new DockSettings { PlacementMode = layout.Mode, Edge = DockEdge.Left, Corner = layout.Corner, Orientation = layout.Orientation, Entries = entries };
                store.Save(settings);
                var restored = store.Load(out warning);
                Check(warning is null && restored.Layout == settings.Layout && restored.Entries.SequenceEqual(entries));
                Check(!File.ReadAllText(path).Contains("\"Layout\""));
            }
            foreach (var property in new[] { "PlacementMode", "Corner", "Orientation" })
            {
                File.WriteAllText(path, "{\"" + property + "\":999,\"Entries\":[]}");
                var restored = store.Load(out warning, out var status);
                Check(warning is not null && status == SettingsLoadStatus.Recovered && restored.PlacementMode == DockPlacementMode.Edge);
            }
            Check(Directory.GetFiles(temporary, "corner-settings.json.invalid-*").Length == 3);
        });
        Run("Corner docks and triggers stay in the available work area at every corner, axis and DPI", () =>
        {
            foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
            foreach (var layout in CornerLayouts())
            foreach (var inset in new[] { 0, 2, 60, 120 })
            foreach (var work in new PixelRect[]
            {
                new(-1920, -400, 1920, 1080 - inset), new(-1920, -400 + inset, 1920, 1080 - inset),
                new(-1920 + inset, -400, 1920 - inset, 1080), new(-1920, -400, 1920 - inset, 1080)
            })
            {
                var bounds = new PixelRect(-1920, -400, 1920, 1080);
                var p = DockGeometry.Calculate(bounds, work, layout, layout.Horizontal ? 700 : 78, layout.Horizontal ? 78 : 700, scale);
                Check(p.Window.X >= work.X && p.Window.Y >= work.Y && p.Window.Right <= work.Right && p.Window.Bottom <= work.Bottom);
                Check(layout.Right ? p.Window.Right == work.Right - 8 * scale : p.Window.X == work.X + 8 * scale);
                Check(layout.Bottom ? p.Window.Bottom == work.Bottom - 8 * scale : p.Window.Y == work.Y + 8 * scale);
                Check(p.Trigger.Width == 36 * scale && p.Trigger.Height == 36 * scale);
                Check(p.Trigger == DockGeometry.IndicatorArea(bounds, work, layout, scale));
                Check(p.Trigger.X >= work.X && p.Trigger.Y >= work.Y && p.Trigger.Right <= work.Right && p.Trigger.Bottom <= work.Bottom);
                Check(layout.Right ? p.Trigger.Right == work.Right : p.Trigger.X == work.X);
                Check(layout.Bottom ? p.Trigger.Bottom == work.Bottom : p.Trigger.Y == work.Y);
                var oversized = DockGeometry.Calculate(bounds, work, layout, 10000, 10000, scale);
                Check(oversized.Window.Width == work.Width - 16 * scale && oversized.Window.Height == work.Height - 16 * scale);
                var group = GroupGeometry.Place(work, p.Window, layout.EffectiveEdge, 340 * scale, 250 * scale, scale);
                Check(group.X >= work.X && group.Y >= work.Y && group.Right <= work.Right && group.Bottom <= work.Bottom);
                var edge = new DockLayout(DockPlacementMode.Edge, DockEdge.Left, layout.Corner, layout.Orientation);
                Check(DockGeometry.Calculate(bounds, work, edge, 78, 400, scale) == DockGeometry.Calculate(work, DockEdge.Left, 78, 400, scale));
                // The safe passage goes only toward the first tile, not across the taskbar.
                var startX = p.Trigger.X + p.Trigger.Width / 2;
                var startY = p.Trigger.Y + p.Trigger.Height / 2;
                var endX = layout.Right ? p.Window.Right - 20 * scale : p.Window.X + 20 * scale;
                var endY = layout.Bottom ? p.Window.Bottom - 20 * scale : p.Window.Y + 20 * scale;
                for (var step = 0; step <= 10; step++)
                    Check(DockGeometry.ContainsInteraction(p, layout, scale, startX + (endX - startX) * step / 10, startY + (endY - startY) * step / 10));
                var edgeX = layout.Horizontal ? bounds.X + bounds.Width / 2 : layout.Right ? bounds.Right - 1 : bounds.X;
                var edgeY = layout.Horizontal ? layout.Bottom ? bounds.Bottom - 1 : bounds.Y : bounds.Y + bounds.Height / 2;
                Check(!p.Trigger.Contains(edgeX, edgeY) && !DockGeometry.ContainsInteraction(p, layout, scale, edgeX, edgeY));
            }
        });
    }

    private static void RunCornerWindowTests(string temporary, string[] args)
    {
        RunIndicatorAppearanceTests(args);
        RunWorkAreaWindowTests(temporary);
        RunReverseIconOrderWindowTests(temporary);
        Run("Corner dwell cancels on departure and preserves fixed, fullscreen and hide delays", () =>
        {
            var settings = new DockSettings { PlacementMode = DockPlacementMode.Corner };
            var window = new DockWindow(settings, new SettingsStore(Path.Combine(temporary, "corner-dwell.json")), new FakeRecycleBin());
            try
            {
                window.UpdateVisibility(false, false, false, 1000);
                window.UpdateVisibility(false, false, false, 1600); Check(!window.IsHitTestVisible);
                window.UpdateVisibility(false, true, true, 2000);
                window.UpdateVisibility(false, true, true, 2299); Check(!window.IsHitTestVisible);
                window.UpdateVisibility(false, false, false, 2300);
                window.UpdateVisibility(false, true, true, 2400);
                window.UpdateVisibility(false, true, true, 2699); Check(!window.IsHitTestVisible);
                window.UpdateVisibility(false, true, true, 2700); Check(window.IsHitTestVisible);
                window.UpdateVisibility(false, false, true, 3000); Check(window.IsHitTestVisible);
                window.UpdateVisibility(false, false, false, 4000);
                window.UpdateVisibility(false, false, false, 4599); Check(window.IsHitTestVisible);
                window.UpdateVisibility(false, false, false, 4600); Check(!window.IsHitTestVisible);
                settings.AutoHide = false;
                window.UpdateVisibility(false, false, false, 5000); Check(window.IsHitTestVisible);
                window.UpdateVisibility(true, true, true, 6000); Check(!window.IsHitTestVisible);
                window.UpdateVisibility(false, false, false, 7000); Check(window.IsHitTestVisible);
            }
            finally { window.Close(); }
        });
        RunExactCornerWindowTests(temporary);
        if (args.Length > 0 && args[0] == "--background") return;
        Run("Corner item order, insertion markers, popups and indicators follow all eight opening directions", () =>
        {
            foreach (var layout in CornerLayouts())
            {
                var group = new DockEntry(Guid.NewGuid(), "", DisplayName: "Outils", IsGroup: true);
                var first = new DockEntry(Guid.NewGuid(), temporary, DisplayName: "Première application avec un nom long");
                var separator = new DockEntry(Guid.NewGuid(), "", true);
                var last = new DockEntry(Guid.NewGuid(), temporary, DisplayName: "Dernière application");
                var entries = new List<DockEntry> { first, separator, group, last, new(Guid.NewGuid(), temporary, GroupId: group.Id), new(Guid.NewGuid(), temporary, GroupId: group.Id) };
                var settings = new DockSettings { PlacementMode = layout.Mode, Corner = layout.Corner, Orientation = layout.Orientation, AutoHide = false, ShowRecycleBin = true, Entries = entries };
                var window = new DockWindow(settings, new SettingsStore(Path.Combine(temporary, "corner-window.json")), new FakeRecycleBin());
                try
                {
                    window.Show(); window.UpdateLayout();
                    var panel = (InsertionPanel)((Viewbox)((Border)window.Content).Child).Child;
                    Check(panel.Orientation == (layout.Horizontal ? Orientation.Horizontal : Orientation.Vertical));
                    var physical = panel.Children.OfType<Button>().Where(b => b != window.SettingsButton).ToArray();
                    var logical = layout.Reversed ? physical.Reverse().ToArray() : physical;
                    Check(logical.Take(4).Select(b => ((DockEntry)b.Tag).Id).SequenceEqual(entries.Take(4).Select(e => e.Id)) && logical[^1].Tag is null);
                    foreach (var button in logical.Take(4))
                    {
                        var origin = button.TranslatePoint(new Point(), panel);
                        var before = layout.Horizontal ? new Point(origin.X + button.ActualWidth * (layout.Reversed ? 0.9 : 0.1), origin.Y + 5)
                            : new Point(origin.X + 5, origin.Y + button.ActualHeight * (layout.Reversed ? 0.9 : 0.1));
                        var insertion = window.FindInsertion(before);
                        Check(insertion.Before == ((DockEntry)button.Tag).Id);
                        var expected = layout.Horizontal ? origin.X + (layout.Reversed ? button.ActualWidth : 0) : origin.Y + (layout.Reversed ? button.ActualHeight : 0);
                        Check(Math.Abs(insertion.Offset - expected) < 0.1);
                    }
                    var lastButton = logical[3]; var end = lastButton.TranslatePoint(new Point(), panel);
                    Check(window.FindInsertion(layout.Horizontal ? new Point(end.X + lastButton.ActualWidth * (layout.Reversed ? 0.1 : 0.9), end.Y + 5)
                        : new Point(end.X + 5, end.Y + lastButton.ActualHeight * (layout.Reversed ? 0.1 : 0.9))).Before is null);
                    Check(settings.Entries.SequenceEqual(entries));
                    var display = Native.Displays().Single(d => d.Primary);
                    var indicator = window.OwnedWindows.OfType<IndicatorWindow>().Single();
                    Check(!indicator.ShowActivated && !indicator.ShowInTaskbar && !indicator.IsHitTestVisible);
                    var mark = ((Grid)indicator.Content).Children.OfType<System.Windows.Shapes.Path>().Single();
                    Check(mark.Visibility == Visibility.Visible && mark.Data.IsFrozen && mark.Data is EllipseGeometry { RadiusX: 2.5, RadiusY: 2.5 });
                    var indicatorOrigin = indicator.PointToScreen(new Point());
                    var expectedIndicator = DockGeometry.IndicatorArea(display.Bounds, display.Work, layout, display.Scale);
                    Check(Math.Abs(indicatorOrigin.X - expectedIndicator.X) <= 1 && Math.Abs(indicatorOrigin.Y - expectedIndicator.Y) <= 1);
                    var menu = logical[0].ContextMenu;
                    menu.PlacementTarget = logical[0]; menu.IsOpen = true;
                    PumpUntil(() => PresentationSource.FromVisual(menu) is HwndSource);
                    var popup = ((HwndSource)PresentationSource.FromVisual(menu)).Handle;
                    typeof(DockWindow).GetMethod("RefreshPlacement", PrivateInstance)!.Invoke(window, null);
                    Check(menu.IsOpen && IsAbove(popup, new WindowInteropHelper(window).Handle)); menu.IsOpen = false;
                    window.ToggleGroup(group);
                    var groupPanel = (GroupPanel)typeof(DockWindow).GetField("groupPanel", PrivateInstance)!.GetValue(window)!;
                    Check(groupPanel.Items.Children.Count == 2);
                    var groupStart = groupPanel.PointToScreen(new Point());
                    var groupEnd = groupPanel.PointToScreen(new Point(groupPanel.ActualWidth, groupPanel.ActualHeight));
                    Check(groupStart.X >= display.Work.X - 1 && groupStart.Y >= display.Work.Y - 1 && groupEnd.X <= display.Work.Right + 1 && groupEnd.Y <= display.Work.Bottom + 1);
                    if (args.Length == 2 && args[0] == "--preview")
                    {
                        SavePreview((FrameworkElement)window.Content, Path.Combine(args[1], $"corner-{layout.Corner}-{layout.Orientation}.png"));
                        SavePreview((FrameworkElement)indicator.Content, Path.Combine(args[1], $"corner-indicator-{layout.Corner}.png"));
                        if (layout.Corner == DockCorner.BottomRight) SavePreview(groupPanel, Path.Combine(args[1], $"corner-group-{layout.Orientation}.png"));
                    }
                    settings.Corner = settings.Corner == DockCorner.TopLeft ? DockCorner.BottomLeft : DockCorner.TopLeft;
                    typeof(DockWindow).GetMethod("RefreshPlacement", PrivateInstance)!.Invoke(window, null);
                    Check(typeof(DockWindow).GetField("groupPanel", PrivateInstance)!.GetValue(window) is null);
                }
                finally { window.Close(); }
            }
        });
        Run("Position picker persists visual choices, preserves edge preferences and reveals the selected dock", () =>
        {
            var store = new SettingsStore(Path.Combine(temporary, "position-picker.json"));
            var settings = new DockSettings { Edge = DockEdge.Left, Entries = [new(Guid.NewGuid(), temporary)] };
            var window = new DockWindow(settings, store, new FakeRecycleBin());
            try
            {
                window.Show();
                window.SettingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var dialog = Application.Current.Windows.Cast<Window>().Single(w => w.Title == "Réglages — WinDock");
                var picker = ((DockSettingsView)dialog.Content).PositionPanel.Children.OfType<DockPositionPicker>().Single();
                RadioButton Choice(string name) => VisualChildren(picker).OfType<RadioButton>().Single(b => AutomationProperties.GetName(b) == name);
                Choice("Coin").IsChecked = true;
                Check(settings.Corner == DockCorner.BottomRight && settings.Orientation == DockOrientation.Horizontal);
                Choice("Coin haut gauche").IsChecked = true;
                Choice("Vertical").IsChecked = true;
                dialog.UpdateLayout();
                Check(window.IsHitTestVisible && !dialog.Topmost && settings.Edge == DockEdge.Left);
                var restored = store.Load(out var warning);
                Check(warning is null && restored.Layout == settings.Layout);
                Check(VisualChildren(picker).OfType<TextBlock>().Any(t => t.Text.Contains("Haut gauche · Vertical · Vers le bas")));
                Check(Choice("Coin haut gauche").Focusable && Choice("Vertical").Focusable);
                var reverse = VisualChildren(picker).OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == "Inverser l’ordre des icônes");
                reverse.IsChecked = true;
                Check(settings.ReverseIconOrder && store.Load(out _).ReverseIconOrder);
                Check(VisualChildren(picker).OfType<TextBlock>().Any(t => t.Text.Contains("Vers le bas\nOrdre inversé")));
                Check(!MenuHeaders(window.ContextMenu).Contains(ReverseOrderLabel));
                reverse.IsChecked = false;
                Check(!settings.ReverseIconOrder && reverse.IsChecked == false && !store.Load(out _).ReverseIconOrder);
                reverse.IsChecked = true;
                if (args.Length == 2 && args[0] == "--preview") SavePreview(dialog, Path.Combine(args[1], "corner-settings.png"));
                Choice("Bord").IsChecked = true;
                Check(settings.Layout.EffectiveEdge == DockEdge.Left && settings.Orientation == DockOrientation.Vertical && settings.Corner == DockCorner.TopLeft && settings.ReverseIconOrder);
                Choice("Coin").IsChecked = true;
                Check(reverse.IsChecked == true && settings.Layout.Reversed);
                dialog.Close();
            }
            finally { window.Close(); }
        });
    }

    private static void RunIndicatorAppearanceTests(string[] args)
    {
        Run("Corner dots keep constant visibility until opening while edge appearance and hiding are preserved", () =>
        {
            var indicator = new IndicatorWindow();
            var sheet = new StackPanel();
            var previews = args.Length == 2 && args[0] == "--preview" ? args[1]
                : args.Length == 3 && args[0] == "--background" && args[1] == "--preview" ? args[2] : null;
            try
            {
                foreach (var corner in Enum.GetValues<DockCorner>())
                {
                    indicator.Place(new PixelRect(-10000, -10000, 36, 36),
                        new DockLayout(DockPlacementMode.Corner, DockEdge.Bottom, corner, DockOrientation.Horizontal), 1);
                    var content = (Grid)indicator.Content;
                    content.Measure(new Size(36, 36)); content.Arrange(new Rect(0, 0, 36, 36));
                    content.UpdateLayout();
                    content.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                    var mark = content.Children.OfType<System.Windows.Shapes.Path>().Single();
                    Check(mark.Visibility == Visibility.Visible && ((SolidColorBrush)mark.Fill).Color == Color.FromRgb(37, 132, 214));
                    Check(!indicator.ShowActivated && !indicator.IsHitTestVisible && !indicator.ShowInTaskbar);
                    var row = new StackPanel { Orientation = Orientation.Horizontal };
                    row.Children.Add(new TextBlock { Text = corner.ToString(), Width = 110, VerticalAlignment = VerticalAlignment.Center });
                    foreach (var state in new[] { (Name: "Repos", Visible: true, Near: false, Over: false, Opacity: 1.0),
                        (Name: "Proximité", Visible: true, Near: true, Over: false, Opacity: 1.0),
                        (Name: "Survol", Visible: true, Near: true, Over: true, Opacity: 1.0),
                        (Name: "Dock ouvert", Visible: false, Near: true, Over: true, Opacity: 0.0) })
                    {
                        indicator.SetAppearance(state.Visible, state.Near, state.Over);
                        indicator.BeginAnimation(UIElement.OpacityProperty, null);
                        Check(indicator.Opacity == state.Opacity);
                        if (previews is null) continue;
                        var bitmap = new RenderTargetBitmap(72, 72, 192, 192, PixelFormats.Pbgra32);
                        bitmap.Render(content); bitmap.Freeze();
                        foreach (var background in new[] { Colors.White, Color.FromRgb(32, 32, 32) })
                        {
                            var cell = new StackPanel { Width = 94, Margin = new Thickness(2) };
                            cell.Children.Add(new TextBlock { Text = state.Name, HorizontalAlignment = HorizontalAlignment.Center });
                            cell.Children.Add(new Border { Height = 60, Background = new SolidColorBrush(background),
                                Child = new Image { Source = bitmap, Width = 36, Height = 36, Opacity = indicator.Opacity } });
                            row.Children.Add(cell);
                        }
                    }
                    sheet.Children.Add(row);
                    Check(mark.Effect is null && mark.Stroke is null);
                    var dot = (EllipseGeometry)mark.Data;
                    Check(dot.RadiusX == 2.5 && dot.RadiusY == 2.5);
                    Check(dot.Center == new Point(corner is DockCorner.TopRight or DockCorner.BottomRight ? 29 : 7,
                        corner is DockCorner.BottomLeft or DockCorner.BottomRight ? 29 : 7));
                    indicator.SetAppearance(false, true, true);
                    indicator.BeginAnimation(UIElement.OpacityProperty, null); Check(indicator.Opacity == 0);
                    indicator.SetAppearance(true, false, false);
                    indicator.BeginAnimation(UIElement.OpacityProperty, null); Check(indicator.Opacity == 1);
                }
                indicator.Place(new PixelRect(-10000, -10000, 100, 36), DockEdge.Bottom, 1);
                Check(((Grid)indicator.Content).Children.OfType<Border>().Single().Background == Brushes.White);
                foreach (var state in new[] { (Near: false, Over: false, Opacity: 0.38),
                    (Near: true, Over: false, Opacity: 0.7), (Near: true, Over: true, Opacity: 0.95) })
                {
                    indicator.SetAppearance(true, state.Near, state.Over);
                    indicator.BeginAnimation(UIElement.OpacityProperty, null); Check(indicator.Opacity == state.Opacity);
                }
                indicator.SetAppearance(false, false, false);
                indicator.BeginAnimation(UIElement.OpacityProperty, null); Check(indicator.Opacity == 0);
                if (previews is not null)
                {
                    sheet.Background = Brushes.White;
                    sheet.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    sheet.Arrange(new Rect(sheet.DesiredSize));
                    SavePreview(sheet, Path.Combine(previews, "corner-indicator-contrast.png"));
                }
            }
            finally { indicator.Close(); }
        });
    }

    private static void RunCornerOffscreenTests(string temporary, string? previews)
    {
        Run("Offscreen corner docks preserve order, insertion and real drop persistence at all eight anchors", () =>
        {
            foreach (var layout in CornerLayouts())
            foreach (var count in new[] { 0, 1, 8, 80 })
            {
                var entries = Enumerable.Range(0, count).Select(i => i % 3 == 1 ? new DockEntry(Guid.NewGuid(), "", true)
                    : new DockEntry(Guid.NewGuid(), temporary, DisplayName: "Raccourci " + i)).ToList();
                var settings = new DockSettings { PlacementMode = layout.Mode, Corner = layout.Corner, Orientation = layout.Orientation, ShowRecycleBin = true, Entries = entries };
                var store = new SettingsStore(Path.Combine(temporary, "offscreen-dock.json"));
                var window = new DockWindow(settings, store, new FakeRecycleBin());
                try
                {
                    var surface = (Border)window.Content;
                    var panel = (InsertionPanel)((Viewbox)surface.Child).Child;
                    surface.Measure(new Size(600, 600)); surface.Arrange(new Rect(new Point(), surface.DesiredSize)); surface.UpdateLayout();
                    var physical = panel.Children.OfType<Button>().Where(b => b != window.SettingsButton).ToArray();
                    var logical = layout.Reversed ? physical.Reverse().ToArray() : physical;
                    Check(logical.Take(count).Select(b => ((DockEntry)b.Tag).Id).SequenceEqual(entries.Select(e => e.Id)) && logical[^1].Tag is null);
                    foreach (var button in logical.Take(count))
                    {
                        var origin = button.TranslatePoint(new Point(), panel);
                        var before = layout.Horizontal ? new Point(origin.X + button.ActualWidth * (layout.Reversed ? 0.9 : 0.1), origin.Y + 5)
                            : new Point(origin.X + 5, origin.Y + button.ActualHeight * (layout.Reversed ? 0.9 : 0.1));
                        var insertion = window.FindInsertion(before);
                        Check(insertion.Before == ((DockEntry)button.Tag).Id);
                        var expected = layout.Horizontal ? origin.X + (layout.Reversed ? button.ActualWidth : 0) : origin.Y + (layout.Reversed ? button.ActualHeight : 0);
                        Check(Math.Abs(insertion.Offset - expected) < 0.1);
                    }
                    if (count > 1)
                    {
                        var last = logical[count - 1]; var origin = last.TranslatePoint(new Point(), panel);
                        var after = layout.Horizontal ? new Point(origin.X + last.ActualWidth * (layout.Reversed ? 0.1 : 0.9), origin.Y + 5)
                            : new Point(origin.X + 5, origin.Y + last.ActualHeight * (layout.Reversed ? 0.1 : 0.9));
                        Check(window.FindInsertion(after).Before is null);
                        window.DropEntries(new DataObject("WinDock.EntryId", entries[^1].Id.ToString()), null, entries[0].Id);
                        Check(settings.Entries[0].Id == entries[^1].Id && store.Load(out _).Entries.SequenceEqual(settings.Entries));
                        // The same operation works toward the far end, independently of physical direction.
                        window.DropEntries(new DataObject("WinDock.EntryId", entries[^1].Id.ToString()), null);
                        Check(settings.Entries.SequenceEqual(entries) && store.Load(out _).Entries.SequenceEqual(entries));
                    }
                    Check(new WindowInteropHelper(window).Handle == 0);
                    if (previews is not null && count == 8)
                    {
                        surface.Measure(new Size(600, 600)); surface.Arrange(new Rect(new Point(), surface.DesiredSize)); surface.UpdateLayout();
                        SavePreview(surface, Path.Combine(previews, $"corner-{layout.Corner}-{layout.Orientation}.png"));
                        settings.ReverseIconOrder = true;
                        typeof(DockWindow).GetMethod("RenderItems", PrivateInstance)!.Invoke(window, null);
                        surface.Measure(new Size(600, 600)); surface.Arrange(new Rect(new Point(), surface.DesiredSize)); surface.UpdateLayout();
                        SavePreview(surface, Path.Combine(previews, $"corner-{layout.Corner}-{layout.Orientation}-reversed.png"));
                        settings.ReverseIconOrder = false;
                        typeof(DockWindow).GetMethod("RenderItems", PrivateInstance)!.Invoke(window, null);
                    }
                    if (count == 8)
                    {
                        settings.PlacementMode = DockPlacementMode.Edge;
                        typeof(DockWindow).GetMethod("RenderItems", PrivateInstance)!.Invoke(window, null);
                        Check(panel.Children.OfType<Button>().Take(count).Select(b => ((DockEntry)b.Tag).Id).SequenceEqual(entries.Select(e => e.Id)));
                        Check(panel.Children.OfType<Button>().Last(b => b != window.SettingsButton).Tag is null && settings.Entries.SequenceEqual(entries));
                    }
                }
                finally { window.Close(); }
            }
        });
        Run("Offscreen position picker supports every selection with readable labels and retained preferences", () =>
        {
            var settings = new DockSettings { Edge = DockEdge.Left };
            var changes = 0;
            var picker = new DockPositionPicker(settings, () => changes++) { Width = 355 };
            void Arrange()
            {
                picker.Measure(new Size(355, double.PositiveInfinity));
                picker.Arrange(new Rect(new Point(), picker.DesiredSize)); picker.UpdateLayout();
            }
            Arrange();
            RadioButton Choice(string name) => VisualChildren(picker).OfType<RadioButton>().Single(b => AutomationProperties.GetName(b) == name);
            Choice("Coin").IsChecked = true;
            Check(settings.Corner == DockCorner.BottomRight && settings.Orientation == DockOrientation.Horizontal);
            var exact = VisualChildren(picker).OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == "Exiger le coin exact");
            exact.IsChecked = true; Arrange();
            Check(settings.RequireExactCorner && exact.Focusable);
            Check(VisualChildren(picker).OfType<TextBlock>().Any(t => t.Text.Contains("Visez le point bleu")));
            exact.IsChecked = false; Arrange();
            Check(!settings.RequireExactCorner && VisualChildren(picker).OfType<TextBlock>().Any(t => t.Text.Contains("Survolez le repère")));
            exact.IsChecked = true; Arrange();
            foreach (var layout in CornerLayouts())
            {
                var name = layout.Corner switch { DockCorner.TopLeft => "Coin haut gauche", DockCorner.TopRight => "Coin haut droite", DockCorner.BottomLeft => "Coin bas gauche", _ => "Coin bas droite" };
                Choice(name).IsChecked = true;
                Choice(layout.Horizontal ? "Horizontal" : "Vertical").IsChecked = true;
                Arrange();
                Check(settings.Layout == layout with { Edge = DockEdge.Left });
                foreach (var button in VisualChildren(picker).OfType<RadioButton>().Where(b => b.Visibility == Visibility.Visible && b.ActualWidth > 0))
                {
                    Check(button.Focusable && !string.IsNullOrEmpty(AutomationProperties.GetName(button)));
                    var label = (TextBlock)button.Content;
                    label.Measure(new Size(label.ActualWidth, double.PositiveInfinity));
                    Check(label.DesiredSize.Height <= label.ActualHeight + 1);
                }
                if (previews is not null)
                {
                    SavePreview(picker, Path.Combine(previews, $"corner-settings-{layout.Corner}-{layout.Orientation}.png"));
                    var reverse = VisualChildren(picker).OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == ReverseOrderLabel);
                    reverse.IsChecked = true; Arrange();
                    SavePreview(picker, Path.Combine(previews, $"corner-settings-{layout.Corner}-{layout.Orientation}-reversed.png"));
                    reverse.IsChecked = false; Arrange();
                }
            }
            Choice("Bord").IsChecked = true; Arrange();
            Check(settings.Edge == DockEdge.Left && settings.Corner == DockCorner.BottomRight && settings.Orientation == DockOrientation.Vertical && settings.RequireExactCorner && changes > 8);
        });
        Run("Interrupted opacity transitions settle on the requested final state", () =>
        {
            var border = new Border { Opacity = 0 };
            DockMotion.SetOpacity(border, 1); DockMotion.SetOpacity(border, 0); DockMotion.SetOpacity(border, 1);
            // Removing the presentation animation reveals the already-correct semantic value.
            border.BeginAnimation(UIElement.OpacityProperty, null); Check(border.Opacity == 1);
            DockMotion.SetOpacity(border, 0); border.BeginAnimation(UIElement.OpacityProperty, null); Check(border.Opacity == 0);
        });
    }

    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in VisualChildren(child)) yield return descendant;
        }
    }
}
