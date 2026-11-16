using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using WinDock;
using WinDock.Core;

internal static partial class Program
{
    private static void RunIsolatedWindowTests(string temporary, string desktop, string output)
    {
        IsolatedDesktop.VerifyWorker(desktop);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        app.Resources = new ResourceDictionary { Source = new Uri("/WinDock;component/Styles.xaml", UriKind.Relative) };
        Run("Real menu HWNDs stay above the dock during periodic positioning on twelve isolated layouts", () =>
        {
            foreach (var layout in AllLayouts())
            {
                IsolatedDesktop.VerifyWorker(desktop);
                var group = new DockEntry(Guid.NewGuid(), "", DisplayName: "Outils", IsGroup: true);
                var entry = new DockEntry(Guid.NewGuid(), temporary, DisplayName: "Une application avec un libellé long près du coin");
                var settings = new DockSettings { AutoHide = false, PlacementMode = layout.Mode, Edge = layout.Edge, Corner = layout.Corner, Orientation = layout.Orientation,
                    Entries = [entry, group, new(Guid.NewGuid(), temporary, GroupId: group.Id), new(Guid.NewGuid(), temporary, GroupId: group.Id)] };
                var window = new DockWindow(settings, new SettingsStore(Path.Combine(temporary, "isolated.json")), new FakeRecycleBin(), new FakeApplicationWindows());
                try
                {
                    window.Show();
                    PumpUntil(() => window.OwnedWindows.OfType<IndicatorWindow>().Any());
                    // Read real HWND Z-order while driving the exact positioning routine. Input
                    // dismissal is tested separately; the worker never polls the user's keyboard.
                    ((DispatcherTimer)typeof(DockWindow).GetField("timer", PrivateInstance)!.GetValue(window)!).Stop();
                    var hwnd = new WindowInteropHelper(window).Handle;
                    IsolatedDesktop.VerifyWindow(hwnd, desktop);
                    window.UpdateLayout();
                    var panel = (InsertionPanel)((Viewbox)((Border)window.Content).Child).Child;
                    var button = panel.Children.OfType<Button>().Single(b => b.Tag is DockEntry e && e.Id == entry.Id);
                    var menu = button.ContextMenu; menu.PlacementTarget = button; menu.IsOpen = true;
                    PumpUntil(() => PresentationSource.FromVisual(menu) is HwndSource);
                    var popup = ((HwndSource)PresentationSource.FromVisual(menu)).Handle;
                    IsolatedDesktop.VerifyWindow(popup, desktop);
                    var updates = 0;
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    timer.Tick += (_, _) =>
                    {
                        typeof(DockWindow).GetMethod("RefreshPlacement", PrivateInstance)!.Invoke(window, null);
                        IsolatedDesktop.VerifyWindow(hwnd, desktop);
                        Check(menu.IsOpen && IsAbove(popup, hwnd)); updates++;
                    };
                    timer.Start();
                    try { PumpUntil(() => updates >= 2, 8000); }
                    finally { timer.Stop(); }
                    Check(menu.IsOpen && IsAbove(popup, hwnd)); menu.IsOpen = false;
                    window.ToggleGroup(group);
                    var groupPanel = (GroupPanel)typeof(DockWindow).GetField("groupPanel", PrivateInstance)!.GetValue(window)!;
                    var groupHwnd = new WindowInteropHelper(groupPanel).Handle;
                    IsolatedDesktop.VerifyWindow(groupHwnd, desktop);
                    typeof(DockWindow).GetMethod("RefreshPlacement", PrivateInstance)!.Invoke(window, null);
                    Check(groupPanel.IsVisible && IsAbove(groupHwnd, hwnd));
                    window.CloseGroup();
                    var label = (ToolTip)button.ToolTip; label.IsOpen = true;
                    PumpUntil(() => PresentationSource.FromVisual(label) is HwndSource);
                    IsolatedDesktop.VerifyWindow(((HwndSource)PresentationSource.FromVisual(label)).Handle, desktop);
                    var origin = label.PointToScreen(new Point());
                    var end = label.PointToScreen(new Point(label.ActualWidth, label.ActualHeight));
                    Check(Native.Displays().Any(d => origin.X >= d.Work.X - 1 && origin.Y >= d.Work.Y - 1 && end.X <= d.Work.Right + 1 && end.Y <= d.Work.Bottom + 1));
                    label.IsOpen = false;
                    Console.WriteLine($"REAL HWND PASS: {layout.Mode}/{layout.EffectiveEdge}/{layout.Corner}/{layout.Orientation}; {updates} periodic placements; group and label isolated.");
                    if (layout == AllLayouts().Last())
                    {
                        window.ContextMenu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Réglages…")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                        var dialog = Application.Current.Windows.Cast<Window>().Single(w => w.Title == "Réglages — WinDock");
                        IsolatedDesktop.VerifyWindow(new WindowInteropHelper(dialog).Handle, desktop);
                        var view = (DockSettingsView)dialog.Content;
                        foreach (var tab in new[] { "Emplacement", "Apparence", "Général" })
                        {
                            view.SelectPage(tab); dialog.UpdateLayout();
                            SavePreview(dialog, Path.Combine(output, "settings-" + tab + ".png"));
                            Check(view.CloseButton.IsVisible && !dialog.Topmost);
                        }
                        view.SelectPage("Apparence"); dialog.UpdateLayout();
                        var autoHide = view.AppearancePanel.Children.OfType<CheckBox>().Single(c => Equals(c.Content, "Masquage automatique"));
                        autoHide.IsChecked = true; autoHide.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                        var recycle = view.AppearancePanel.Children.OfType<CheckBox>().Single(c => Equals(c.Content, "Afficher la corbeille"));
                        recycle.IsChecked = true; recycle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                        view.AppearancePanel.Children.OfType<Slider>().Single().Value = 58;
                        view.SelectPage("Emplacement"); dialog.UpdateLayout();
                        var picker = view.PositionPanel.Children.OfType<DockPositionPicker>().Single();
                        var exact = VisualChildren(picker).OfType<CheckBox>().Single();
                        Check(exact.IsEnabled); exact.IsChecked = true;
                        Check(settings.RequireExactCorner && settings.AutoHide && settings.ShowRecycleBin && settings.BackgroundOpacity == 0.58);
                        SavePreview(dialog, Path.Combine(output, "settings-Emplacement.png"));
                        var oldHeight = dialog.Height;
                        dialog.Height = 300;
                        foreach (var tab in new[] { "Emplacement", "Apparence", "Général" })
                        {
                            view.SelectPage(tab); dialog.UpdateLayout();
                            var page = (ScrollViewer)((TabItem)view.Tabs.SelectedItem).Content;
                            Check(page.ScrollableHeight > 0 && view.CloseButton.IsVisible);
                            var footer = view.CloseButton.TransformToAncestor(view).TransformBounds(new Rect(view.CloseButton.RenderSize));
                            Check(footer.Bottom <= view.ActualHeight + 1 && footer.Y >= 0);
                        }
                        dialog.Height = oldHeight; view.SelectPage("Emplacement"); dialog.UpdateLayout();
                        dialog.Close();
                        var restored = new SettingsStore(Path.Combine(temporary, "isolated.json")).Load(out var warning);
                        Check(warning is null && restored.RequireExactCorner && restored.AutoHide && restored.ShowRecycleBin && restored.BackgroundOpacity == 0.58);
                        Console.WriteLine("SETTINGS PASS: three pages, live switches, opacity, exact-corner persistence and fixed footer at 300 DIP height.");
                    }
                }
                finally { window.Close(); }
            }
        });
        IsolatedDesktop.VerifyWorker(desktop);
        Console.WriteLine("Real menus, group panels, labels and settings verified on an inactive desktop; input desktop never switched.");
        app.Shutdown();
    }
}
