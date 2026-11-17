using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using WinDock;
using WinDock.Core;

internal static partial class Program
{
    private const string ReverseOrderLabel = "Inverser l’ordre des icônes";

    private static MenuItem ReverseOrderItem(ContextMenu menu) => menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, ReverseOrderLabel));

    private static void RunReverseIconOrderCoreTests(string temporary)
    {
        Run("Reverse icon order retains legacy defaults, stored entries and all corner and edge preferences", () =>
        {
            var path = Path.Combine(temporary, "reverse-order.json");
            var store = new SettingsStore(path);
            File.WriteAllText(path, "{\"PlacementMode\":\"Corner\",\"Entries\":[]}");
            Check(!store.Load(out _).ReverseIconOrder);
            var entries = new List<DockEntry> { new(Guid.NewGuid(), temporary), new(Guid.NewGuid(), "", true) };
            foreach (var layout in AllLayouts())
            foreach (var reverse in new[] { false, true })
            {
                var settings = new DockSettings { PlacementMode = layout.Mode, Edge = layout.Edge, Corner = layout.Corner,
                    Orientation = layout.Orientation, ReverseIconOrder = reverse, Entries = entries };
                var expectedReversed = layout.Mode == DockPlacementMode.Corner &&
                    ((layout.Horizontal ? layout.Corner is DockCorner.TopRight or DockCorner.BottomRight
                        : layout.Corner is DockCorner.BottomLeft or DockCorner.BottomRight) != reverse);
                Check(settings.Layout.Reversed == expectedReversed);
                Check(settings.Layout.EffectiveEdge == layout.EffectiveEdge);
                store.Save(settings);
                var restored = store.Load(out var warning);
                Check(warning is null && restored.ReverseIconOrder == reverse && restored.Layout == settings.Layout);
                Check(restored.Entries.SequenceEqual(entries) && !File.ReadAllText(path).Contains("\"Layout\""));
                var bounds = new PixelRect(-1920, -400, 1920, 1080);
                foreach (var scale in new[] { 1.0, 1.5, 2.0 })
                {
                    var original = DockGeometry.Calculate(bounds, bounds, layout, 400, 78, scale);
                    Check(DockGeometry.Calculate(bounds, bounds, settings.Layout, 400, 78, scale) == original);
                    var x = original.Trigger.X + original.Trigger.Width / 2;
                    var y = original.Trigger.Y + original.Trigger.Height / 2;
                    Check(DockGeometry.ContainsInteraction(original, layout, scale, x, y) ==
                        DockGeometry.ContainsInteraction(original, settings.Layout, scale, x, y));
                }
            }
        });
    }

    private static void RunReverseIconOrderWindowTests(string temporary)
    {
        Run("Corner inversion mirrors the whole row twice and preserves drag insertion and group members", () =>
        {
            foreach (var layout in CornerLayouts())
            foreach (var showRecycleBin in new[] { false, true })
            foreach (var count in new[] { 0, 1, 6 })
            {
                var group = new DockEntry(Guid.NewGuid(), "", DisplayName: "Outils", IsGroup: true);
                DockEntry[] candidates = [new(Guid.NewGuid(), temporary), new(Guid.NewGuid(), "", true), group,
                    new(Guid.NewGuid(), temporary), new(Guid.NewGuid(), temporary, GroupId: group.Id), new(Guid.NewGuid(), temporary, GroupId: group.Id)];
                var entries = candidates.Take(count).ToList();
                var store = new SettingsStore(Path.Combine(temporary, "reverse-window.json"));
                var settings = new DockSettings { PlacementMode = layout.Mode, Corner = layout.Corner, Orientation = layout.Orientation,
                    ShowRecycleBin = showRecycleBin, Entries = entries };
                var window = new DockWindow(settings, store, new FakeRecycleBin(), new FakeApplicationWindows());
                try
                {
                    var surface = (Border)window.Content;
                    var panel = (InsertionPanel)((Viewbox)surface.Child).Child;
                    void Arrange()
                    {
                        surface.Measure(new Size(600, 600));
                        surface.Arrange(new Rect(new Point(), surface.DesiredSize)); surface.UpdateLayout();
                    }
                    Arrange();
                    var original = panel.Children.OfType<Button>().Select(b => b.Tag).ToArray();
                    var command = ReverseOrderItem(window.ContextMenu);
                    Check(command.IsCheckable && command.Visibility == Visibility.Visible && !command.IsChecked);
                    for (var flip = 0; flip < 2; flip++)
                    {
                        command.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Arrange();
                        Check(settings.ReverseIconOrder == (flip == 0) && command.IsChecked == (flip == 0));
                        Check(panel.Children.OfType<Button>().Select(b => b.Tag).SequenceEqual(flip == 0 ? original.Reverse() : original));
                        Check(settings.Entries.SequenceEqual(entries) && store.Load(out _).Entries.SequenceEqual(entries));
                        Check(store.Load(out _).ReverseIconOrder == (flip == 0));
                        var physical = panel.Children.OfType<Button>().ToArray();
                        var logical = settings.Layout.Reversed ? physical.Reverse().ToArray() : physical;
                        foreach (var button in logical.Where(b => b.Tag is DockEntry))
                        {
                            var origin = button.TranslatePoint(new Point(), panel);
                            var fraction = settings.Layout.Reversed ? 0.9 : 0.1;
                            var point = layout.Horizontal ? new Point(origin.X + button.ActualWidth * fraction, origin.Y + 5)
                                : new Point(origin.X + 5, origin.Y + button.ActualHeight * fraction);
                            var insertion = window.FindInsertion(point);
                            Check(insertion.Before == ((DockEntry)button.Tag).Id);
                            var expectedOffset = layout.Horizontal ? origin.X + (settings.Layout.Reversed ? button.ActualWidth : 0)
                                : origin.Y + (settings.Layout.Reversed ? button.ActualHeight : 0);
                            Check(Math.Abs(insertion.Offset - expectedOffset) < 0.1);
                        }
                        if (count == 6)
                        {
                            foreach (var button in physical) Check(ReverseOrderItem(button.ContextMenu).IsChecked == (flip == 0));
                            var target = logical.First(b => b.Tag is DockEntry);
                            var origin = target.TranslatePoint(new Point(), panel);
                            var fraction = settings.Layout.Reversed ? 0.9 : 0.1;
                            var point = layout.Horizontal ? new Point(origin.X + target.ActualWidth * fraction, origin.Y + 5)
                                : new Point(origin.X + 5, origin.Y + target.ActualHeight * fraction);
                            window.DropEntries(new DataObject("WinDock.EntryId", candidates[3].Id.ToString()), null, window.FindInsertion(point).Before);
                            Check(settings.Entries[0].Id == candidates[3].Id);
                            Check(store.Load(out _).Entries.SequenceEqual(settings.Entries));
                            window.DropEntries(new DataObject("WinDock.EntryId", candidates[3].Id.ToString()), null);
                            Check(settings.Entries.Where(e => e.GroupId is null).SequenceEqual(entries.Where(e => e.GroupId is null)));
                            Check(settings.Entries.Where(e => e.GroupId == group.Id).SequenceEqual(entries.Where(e => e.GroupId == group.Id)));
                            // Moving a root entry may change its interleaving with group members.
                            entries = settings.Entries.ToList();
                            Arrange();
                        }
                    }
                    command.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    settings.PlacementMode = DockPlacementMode.Edge;
                    typeof(DockWindow).GetMethod("RenderItems", PrivateInstance)!.Invoke(window, null);
                    Check(panel.Children.OfType<Button>().Where(b => b.Tag is DockEntry).Select(b => (DockEntry)b.Tag)
                        .SequenceEqual(entries.Where(e => e.GroupId is null)));
                    foreach (var button in panel.Children.OfType<Button>()) Check(ReverseOrderItem(button.ContextMenu).Visibility == Visibility.Collapsed);
                    command.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Check(settings.ReverseIconOrder);
                    settings.PlacementMode = DockPlacementMode.Corner;
                    typeof(DockWindow).GetMethod("RenderItems", PrivateInstance)!.Invoke(window, null);
                    Check(panel.Children.OfType<Button>().Select(b => b.Tag).SequenceEqual(original.Reverse()));
                }
                finally { window.Close(); }
            }
        });
        Run("Reverse switch refresh is silent and narrow previews retain their physical opening direction", () =>
        {
            var settings = new DockSettings { PlacementMode = DockPlacementMode.Corner, Corner = DockCorner.BottomRight };
            var changes = 0;
            var picker = new DockPositionPicker(settings, () => changes++);
            picker.Measure(new Size(355, double.PositiveInfinity));
            picker.Arrange(new Rect(new Point(), picker.DesiredSize)); picker.UpdateLayout();
            var toggle = VisualChildren(picker).OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == ReverseOrderLabel);
            Check(toggle.Focusable && toggle.Visibility == Visibility.Visible);
            var preview = VisualChildren(picker).OfType<Canvas>().Single();
            var first = preview.Children.OfType<System.Windows.Shapes.Rectangle>().Single(r => r.Fill == Brushes.White);
            var originalX = Canvas.GetLeft(first);
            toggle.IsChecked = true;
            Check(changes == 1 && settings.ReverseIconOrder);
            first = preview.Children.OfType<System.Windows.Shapes.Rectangle>().Single(r => r.Fill == Brushes.White);
            Check(Canvas.GetLeft(first) != originalX);
            Check(VisualChildren(picker).OfType<TextBlock>().Any(t => t.Text.Contains("Vers la gauche\nOrdre inversé")));
            settings.ReverseIconOrder = false; picker.Refresh();
            Check(toggle.IsChecked == false && changes == 1);
        });
    }
}
