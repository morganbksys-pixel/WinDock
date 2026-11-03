using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WinDock;
using WinDock.Core;

internal static class Program
{
    private static int passed;
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);

    private static bool IsAbove(nint upper, nint lower)
    {
        for (var current = GetWindow(lower, 3); current != 0; current = GetWindow(current, 3)) // GW_HWNDPREV
            if (current == upper) return true;
        return false;
    }
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--running-smoke")
        {
            var entry = new DockEntry(Guid.NewGuid(), args[1]);
            var running = StaWorker.RunAsync(_ => new RunningApplications().Snapshot([entry]).Contains(entry.Id), default).GetAwaiter().GetResult();
            var windows = new ApplicationWindows().CountAsync(entry, default).GetAwaiter().GetResult();
            Console.WriteLine($"Running: {running}; recognized windows: {windows}. Read-only check; no close request sent.");
            return running && windows > 0 ? 0 : 1;
        }
        if (args.Length == 2 && args[0] == "--hold")
        {
            File.WriteAllText(args[1], "ready");
            Thread.Sleep(30000);
            return 0;
        }
        if (args.Length == 3 && args[0] == "--record")
        {
            File.WriteAllText(args[1], Environment.CurrentDirectory + "|" + args[2]);
            return 0;
        }
        var temporary = Path.Combine(Path.GetTempPath(), "WinDock.Tests-" + Guid.NewGuid());
        Directory.CreateDirectory(temporary);
        try
        {
            Run("Versioned Squirrel applications share indicator and window identity without matching unrelated executables", () =>
            {
                var root = Path.Combine(temporary, "AnthropicClaude");
                Directory.CreateDirectory(root);
                var launcher = Path.Combine(root, "claude.exe");
                var versioned = Path.Combine(root, "app-4.4212.0", "claude.exe");
                var commandLine = Path.Combine(temporary, ".local", "bin", "claude.exe");
                Check(!RunningApplications.MatchesExecutable(launcher, versioned));
                File.WriteAllText(Path.Combine(root, "Update.exe"), "test marker");
                Check(RunningApplications.MatchesExecutable(launcher, versioned));
                Check(RunningApplications.MatchesExecutable(launcher, versioned.ToUpperInvariant()));
                Check(RunningApplications.MatchesExecutable(launcher, Path.Combine(root, "app-4.5000.0", "claude.exe")));
                Check(!RunningApplications.MatchesExecutable(launcher, commandLine));
                Check(!RunningApplications.MatchesExecutable(launcher, Path.Combine(root + "-other", "app-4.4212.0", "claude.exe")));
                Check(!RunningApplications.MatchesExecutable(launcher, Path.Combine(root, "app-invalid", "claude.exe")));
                Check(!RunningApplications.MatchesExecutable(launcher, Path.Combine(root, "app-4.4212.0", "other.exe")));
                Check(!RunningApplications.MatchesExecutable(launcher, Path.Combine(root, "app-4.4212.0", "nested", "claude.exe")));
                var entry = new DockEntry(Guid.NewGuid(), launcher);
                var tracker = new RunningApplications();
                Check(tracker.Snapshot([entry], [versioned], []).Contains(entry.Id));
                Check(!tracker.Snapshot([entry], [commandLine], []).Contains(entry.Id));
                Check(!tracker.Snapshot([entry with { SourcePath = "shell:AppsFolder\\other!App" }], [versioned], []).Contains(entry.Id));
                ApplicationWindow[] windows = [new(1, 10, versioned, null), new(2, 11, commandLine, null)];
                var sent = new List<nint>();
                var service = new ApplicationWindows(() => windows, handle => windows.Single(w => w.Handle == handle),
                    handle => { sent.Add(handle); return 0; });
                Check(service.CountAsync(entry, default).GetAwaiter().GetResult() == 1);
                Check(service.CloseAsync(entry, default).GetAwaiter().GetResult() == 0);
                Check(sent.SequenceEqual(new nint[] { 1 }));
                var replaced = new ApplicationWindows(() => windows, handle => windows[0] with { Path = commandLine },
                    _ => throw new InvalidOperationException("Unrelated process must never receive WM_CLOSE"));
                Check(replaced.CloseAsync(entry, default).GetAwaiter().GetResult() == 0);
            });
            Run("Application closure matches exact identities and revalidates disappearing or reused windows", () =>
            {
                var executable = Path.Combine(temporary, "app.exe");
                var entry = new DockEntry(Guid.NewGuid(), executable);
                ApplicationWindow[] windows = [new(1, 10, executable, null), new(2, 11, executable, null),
                    new(3, 12, Path.Combine(temporary, "other", "app.exe"), null), new(4, 13, null, "package!app")];
                var sent = new List<nint>();
                var service = new ApplicationWindows(() => windows,
                    handle => windows.SingleOrDefault(window => window.Handle == handle),
                    handle => { sent.Add(handle); return handle == 2 ? 5 : 0; });
                Check(service.CountAsync(entry, default).GetAwaiter().GetResult() == 2);
                Check(service.CloseAsync(entry, default).GetAwaiter().GetResult() == 1);
                Check(sent.SequenceEqual(new nint[] { 1, 2 }));
                Check(service.CountAsync(new(Guid.NewGuid(), temporary), default).GetAwaiter().GetResult() == 0);
                Check(service.CountAsync(entry with { IsSeparator = true }, default).GetAwaiter().GetResult() == 0);
                var store = entry with { SourcePath = "shell:AppsFolder\\package!app", ApplicationId = "package!app" };
                Check(service.CountAsync(store, default).GetAwaiter().GetResult() == 1);
                sent.Clear();
                var vanished = new ApplicationWindows(() => windows, handle => handle == 1 ? null : windows[1] with { ProcessId = 99 },
                    handle => { sent.Add(handle); return 0; });
                Check(vanished.CloseAsync(entry, default).GetAwaiter().GetResult() == 0 && sent.Count == 0);
                var expired = new ApplicationWindows(() => windows, handle => windows.Single(w => w.Handle == handle), _ => 1400);
                Check(expired.CloseAsync(entry, default).GetAwaiter().GetResult() == 0);
            });
            Run("Startup shortcut toggles safely and preserves an existing registration on failure", () =>
            {
                var startupDirectory = Path.Combine(temporary, "démarrage test");
                var executable = Environment.ProcessPath!;
                var registration = new StartupRegistration(startupDirectory, executable);
                Check(!registration.IsEnabled);
                registration.SetEnabled(true);
                var shortcut = Path.Combine(startupDirectory, "WinDock.lnk");
                var details = ShellShortcut.Read(shortcut);
                Check(details.Target == executable && details.Arguments == "" && details.WorkingDirectory == Path.GetDirectoryName(executable));
                registration.SetEnabled(true);
                var original = File.ReadAllBytes(shortcut);
                try
                {
                    new StartupRegistration(startupDirectory, Path.Combine(temporary, "absent.exe")).SetEnabled(true);
                    throw new InvalidOperationException("Missing executable was accepted");
                }
                catch (FileNotFoundException) { }
                Check(File.ReadAllBytes(shortcut).SequenceEqual(original));
                registration.SetEnabled(false);
                registration.SetEnabled(false);
                Check(!registration.IsEnabled && File.Exists(executable));
            });
            Run("Missing settings use defaults", () =>
            {
                var settings = new SettingsStore(Path.Combine(temporary, "missing.json")).Load(out var warning);
                Check(warning is null && settings.Edge == DockEdge.Bottom && settings.Entries.Count == 0);
            });
            Run("Settings round trip preserves order, IDs, Unicode and screen", () =>
            {
                var store = new SettingsStore(Path.Combine(temporary, "nested", "settings.json"));
                var entries = new List<DockEntry> { new(Guid.NewGuid(), @"C:\Applications\Éditeur été.lnk"), new(Guid.NewGuid(), @"D:\Mes dossiers") };
                store.Save(new() { Edge = DockEdge.Left, MonitorDevice = @"\\.\DISPLAY2", Entries = entries });
                var result = store.Load(out var warning);
                Check(warning is null && result.Edge == DockEdge.Left && result.MonitorDevice == @"\\.\DISPLAY2" && result.Entries.SequenceEqual(entries));
                result.Entries.RemoveAt(0);
                store.Save(result);
                Check(store.Load(out _).Entries.SequenceEqual(entries.Skip(1)));
            });
            Run("Invalid JSON is backed up without losing original content", () =>
            {
                var path = Path.Combine(temporary, "corrupt.json");
                File.WriteAllText(path, "{broken");
                var settings = new SettingsStore(path).Load(out var warning);
                Check(warning is not null && settings.Entries.Count == 0);
                var backup = Directory.GetFiles(temporary, "corrupt.json.invalid-*").Single();
                Check(File.ReadAllText(backup) == "{broken");
            });
            Run("Legacy settings and separator/opacity persistence", () =>
            {
                var path = Path.Combine(temporary, "legacy.json");
                var id = Guid.NewGuid();
                File.WriteAllText(path, $"{{\"Entries\":[{{\"Id\":\"{id}\",\"Path\":\"app.exe\"}}]}}");
                var store = new SettingsStore(path);
                var settings = store.Load(out var warning);
                Check(warning is null && !settings.Entries[0].IsSeparator && settings.Entries[0].Id == id);
                Check(settings.BackgroundOpacity == 238.0 / 255);
                Check(settings.AutoHide && !settings.ShowRecycleBin);
                settings.AutoHide = false;
                settings.ShowRecycleBin = true;
                var separator = new DockEntry(Guid.NewGuid(), "", true);
                settings.Entries.Add(separator);
                settings.BackgroundOpacity = 0.35;
                EntryRules.Move(settings.Entries, separator.Id, id);
                store.Save(settings);
                var loaded = store.Load(out warning);
                Check(warning is null && loaded.Entries[0] == separator && loaded.BackgroundOpacity == 0.35);
                Check(loaded.Entries[1].Id == id);
                Check(!loaded.AutoHide && loaded.ShowRecycleBin);
            });
            Run("Structurally invalid settings reset safely", () =>
            {
                foreach (var json in new[] { "null", "{\"Entries\":null}", "{\"Edge\":99}", "{\"Entries\":[null]}", "{\"BackgroundOpacity\":2}", "{\"BackgroundOpacity\":0}" })
                {
                    var path = Path.Combine(temporary, Guid.NewGuid() + ".json");
                    File.WriteAllText(path, json);
                    Check(new SettingsStore(path).Load(out var warning).Entries.Count == 0 && warning is not null);
                }
            });
            Run("Move forward, backward, to end and onto self", () =>
            {
                var a = new DockEntry(Guid.NewGuid(), "a");
                var b = new DockEntry(Guid.NewGuid(), "b");
                var c = new DockEntry(Guid.NewGuid(), "c");
                List<DockEntry> entries = [a, b, c];
                EntryRules.Move(entries, a.Id, c.Id); Check(entries.SequenceEqual([b, a, c]));
                EntryRules.Move(entries, c.Id, b.Id); Check(entries.SequenceEqual([c, b, a]));
                EntryRules.Move(entries, c.Id, null); Check(entries.SequenceEqual([b, a, c]));
                EntryRules.Move(entries, a.Id, a.Id); Check(entries.SequenceEqual([b, a, c]));
                EntryRules.Move(entries, Guid.NewGuid(), null); Check(entries.SequenceEqual([b, a, c]));
            });
            Run("Accepted targets and names with spaces and accents", () =>
            {
                var folder = Path.Combine(temporary, "Dossier été"); Directory.CreateDirectory(folder);
                Check(EntryRules.IsSupported(folder) && EntryRules.DisplayName(folder) == "Dossier été");
                foreach (var extension in new[] { ".exe", ".LNK", ".txt" })
                {
                    var path = Path.Combine(folder, "Éditeur test" + extension); File.WriteAllText(path, "");
                    Check(EntryRules.IsSupported(path) == (extension != ".txt"));
                }
                Check(!EntryRules.IsSupported(Path.Combine(folder, "absent.exe")));
            });
            Run("All edges respect work area and a three-pixel trigger", () =>
            {
                var work = new PixelRect(0, 0, 1920, 1040);
                foreach (var edge in Enum.GetValues<DockEdge>())
                {
                    var p = DockGeometry.Calculate(work, edge, 400, 78, 1);
                    Check(p.Window.X >= work.X && p.Window.Y >= work.Y && p.Window.Right <= work.Right && p.Window.Bottom <= work.Bottom);
                    Check(edge is DockEdge.Top or DockEdge.Bottom ? p.Trigger.Height == 3 : p.Trigger.Width == 3);
                    Check(DockGeometry.InteractionArea(p).Contains(p.Trigger.X + 1, p.Trigger.Y + 1));
                    if (edge == DockEdge.Bottom) Check(p.Window.Bottom == 1032 && p.Trigger.Bottom == 1040);
                    if (edge == DockEdge.Top) Check(p.Window.Y == 8 && p.Trigger.Y == 0);
                    if (edge == DockEdge.Left) Check(p.Window.X == 8 && p.Trigger.X == 0);
                    if (edge == DockEdge.Right) Check(p.Window.Right == 1912 && p.Trigger.Right == 1920);
                }
            });
            Run("Negative monitor origins and mixed DPI stay in physical pixels", () =>
            {
                var p = DockGeometry.Calculate(new(-2560, -200, 2560, 1400), DockEdge.Bottom, 400, 78, 1.5);
                Check(p.Window.Width == 600 && p.Window.Height == 117 && p.Window.X == -1580 && p.Window.Bottom == 1188);
                Check(p.Trigger.Height == 3 && p.Trigger.Bottom == 1200);
            });
            Run("Long docks clamp to available space", () =>
            {
                var p = DockGeometry.Calculate(new(0, 0, 1280, 720), DockEdge.Top, 10000, 78, 2);
                Check(p.Window.Width == 1248 && p.Window.X == 16 && p.Window.Height == 156);
            });
            Run("WPF window and native handle initialize on every edge", () =>
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                app.Resources = new ResourceDictionary { Source = new Uri("/WinDock;component/Styles.xaml", UriKind.Relative) };
                foreach (var edge in Enum.GetValues<DockEdge>())
                foreach (var count in new[] { 0, 1, 8, 80 })
                {
                    var settings = new DockSettings { Edge = edge, MonitorDevice = "disconnected-screen", BackgroundOpacity = 0.35 };
                    for (var i = 0; i < count; i++)
                        settings.Entries.Add(i % 3 == 1 ? new(Guid.NewGuid(), "", true) : new(Guid.NewGuid(), temporary));
                    var window = new DockWindow(settings, new SettingsStore(Path.Combine(temporary, "window.json")));
                    Check(new WindowInteropHelper(window).EnsureHandle() != 0);
                    window.Show();
                    PumpUntil(() => window.OwnedWindows.Cast<Window>().Any(w => w is IndicatorWindow));
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    Check(window.Width > 0 && window.Height > 0);
                    Check(window.Icon is BitmapSource dockIcon && dockIcon.PixelWidth == 256 && dockIcon.PixelHeight == 256);
                    window.UpdateLayout();
                    var frame = (Border)window.Content;
                    Check(frame.Background.Opacity == 0.35);
                    var panel = (StackPanel)((Viewbox)frame.Child).Child;
                    var buttons = panel.Children.OfType<Button>().ToArray();
                    Check(buttons.Length == count);
                    if (count == 0) Check(panel.Children.OfType<TextBlock>().Single().Text == "Clic droit pour ajouter" && window.AllowDrop);
                    var commonActions = new[] { "Ajouter…", "Ajouter un séparateur", "Réglages…", "Quitter WinDock" };
                    Check(window.ContextMenu.Items.OfType<MenuItem>().Select(item => (string)item.Header).SequenceEqual(commonActions));
                    foreach (var button in buttons)
                    {
                        var entry = (DockEntry)button.Tag;
                        var actions = entry.IsSeparator ? new[] { "Retirer du dock" } : new[] { "Modifier / réparer…", "Retirer du dock" };
                        Check(button.ContextMenu.Items.OfType<MenuItem>().Select(item => (string)item.Header).SequenceEqual(actions.Concat(commonActions)));
                        var bounds = button.TransformToAncestor(frame).TransformBounds(new Rect(button.RenderSize));
                        if (!(bounds.Left >= 8 && bounds.Top >= 8 && bounds.Right <= frame.ActualWidth - 8 && bounds.Bottom <= frame.ActualHeight - 8))
                            throw new InvalidOperationException($"Clipped button: {edge}, {count} entries, bounds {bounds}, frame {frame.ActualWidth} x {frame.ActualHeight}");
                    }
                    var indicator = window.OwnedWindows.Cast<Window>().OfType<IndicatorWindow>().Single();
                    Check(!indicator.ShowActivated && !indicator.ShowInTaskbar && !indicator.IsHitTestVisible);
                    Check(edge is DockEdge.Top or DockEdge.Bottom ? indicator.Width > indicator.Height : indicator.Height > indicator.Width);
                    window.Close();
                    Check(!indicator.IsVisible);
                }
            });
            Run("Fixed dock remains visible and restores after full screen; auto hide still works", () =>
            {
                var settings = new DockSettings { AutoHide = false };
                var window = new DockWindow(settings, new SettingsStore(Path.Combine(temporary, "fixed.json")));
                try
                {
                    window.UpdateVisibility(false, false, false, 1000);
                    window.UpdateVisibility(false, false, false, 3000);
                    Check(window.IsHitTestVisible);
                    window.UpdateVisibility(true, false, false, 4000);
                    Check(!window.IsHitTestVisible);
                    window.UpdateVisibility(false, false, false, 5000);
                    Check(window.IsHitTestVisible);
                    settings.AutoHide = true;
                    window.UpdateVisibility(false, false, false, 6000);
                    window.UpdateVisibility(false, false, false, 6700);
                    Check(!window.IsHitTestVisible);
                    window.UpdateVisibility(false, true, true, 7000);
                    window.UpdateVisibility(false, true, true, 7160);
                    Check(window.IsHitTestVisible);
                }
                finally { window.Close(); }
            });
            Run("Recycle bin retains state on failure, stays last, and uses only simulated actions", () =>
            {
                var fake = new FakeRecycleBin();
                var settings = new DockSettings { ShowRecycleBin = true, Entries = [new(Guid.NewGuid(), temporary), new(Guid.NewGuid(), "", true)] };
                var window = new DockWindow(settings, new SettingsStore(Path.Combine(temporary, "recycle.json")), fake);
                try
                {
                    var panel = (StackPanel)((Viewbox)((Border)window.Content).Child).Child;
                    var button = panel.Children.OfType<Button>().Last();
                    Check(button.Tag is null && panel.Children.Count == 3 && settings.Entries.Count == 2);
                    string Label() => ((TextBlock)((Border)((ToolTip)button.ToolTip).Content).Child).Text;
                    void Refresh()
                    {
                        var task = window.RefreshRecycleAsync();
                        var duplicate = window.RefreshRecycleAsync();
                        Check(duplicate.IsCompleted);
                        PumpUntil(() => task.IsCompleted);
                        task.GetAwaiter().GetResult();
                    }
                    Refresh();
                    Check(Label().Contains("indisponible"));
                    fake.EmptyState = true;
                    Refresh();
                    Check(Label() == "Corbeille vide");
                    fake.EmptyState = false;
                    Refresh();
                    Check(Label() == "Corbeille pleine");
                    fake.EmptyState = null;
                    Refresh();
                    Check(Label() == "Corbeille pleine");
                    fake.ThrowOnRead = true;
                    Refresh();
                    Check(Label() == "Corbeille pleine" && fake.MaximumConcurrentReads == 1 && fake.Reads == 5);
                    fake.ThrowOnRead = false;
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    PumpUntil(() => fake.Opens == 1 && fake.Reads >= 6);
                    var menuItems = button.ContextMenu.Items.OfType<MenuItem>().ToArray();
                    Check(menuItems.Select(i => (string)i.Header).SequenceEqual(new[] { "Ouvrir", "Vider la corbeille…", "Ajouter…", "Ajouter un séparateur", "Réglages…", "Quitter WinDock" }));
                    menuItems[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    PumpUntil(() => fake.Empties == 1 && Label() == "Corbeille vide");
                    Check(settings.Entries.Count == 2);
                    settings.ShowRecycleBin = false;
                    var reads = fake.Reads;
                    window.RefreshRecycleAsync().GetAwaiter().GetResult();
                    Check(reads == fake.Reads);
                }
                finally { window.Close(); }
            });
            Run("Settings restore, activate without permanent topmost, and persist switches", () =>
            {
                var store = new SettingsStore(Path.Combine(temporary, "settings-switches.json"));
                var settings = new DockSettings();
                var window = new DockWindow(settings, store, new FakeRecycleBin());
                var item = window.ContextMenu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Réglages…"));
                try
                {
                    item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    var dialog = Application.Current.Windows.Cast<Window>().Single(w => w.Title == "Réglages — WinDock");
                    Check(!dialog.Topmost && dialog.IsActive);
                    dialog.WindowState = WindowState.Minimized;
                    item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Check(dialog.WindowState == WindowState.Normal && !dialog.Topmost && dialog.IsActive);
                    var panel = (StackPanel)((ScrollViewer)dialog.Content).Content;
                    dialog.MaxHeight = 260;
                    dialog.UpdateLayout();
                    Check(((ScrollViewer)dialog.Content).ScrollableHeight > 0 && dialog.ActualHeight <= 260);
                    var autoHide = panel.Children.OfType<CheckBox>().Single(c => Equals(c.Content, "Masquage automatique"));
                    autoHide.IsChecked = false;
                    autoHide.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                    var recycle = panel.Children.OfType<CheckBox>().Single(c => Equals(c.Content, "Afficher la corbeille"));
                    recycle.IsChecked = true;
                    recycle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                    Check(!store.Load(out _).AutoHide && store.Load(out _).ShowRecycleBin);
                    var dockPanel = (StackPanel)((Viewbox)((Border)window.Content).Child).Child;
                    Check(dockPanel.Children.Count == 1 && dockPanel.Children[0] is Button { Tag: null });
                    var other = new Window { Width = 200, Height = 100 };
                    try { other.Show(); other.Activate(); Check(!dialog.Topmost && other.IsActive); }
                    finally { other.Close(); }
                    dialog.Close();
                }
                finally { window.Close(); }
            });
            Run("Dock labels use opaque rounded styling and stay within the screen on all edges", () =>
            {
                foreach (var edge in Enum.GetValues<DockEdge>())
                {
                    var settings = new DockSettings { AutoHide = false, Edge = edge, Entries = [new(Guid.NewGuid(), temporary, DisplayName: "Une application avec un nom très long pour vérifier le retour à la ligne du libellé")] };
                    var window = new DockWindow(settings, new SettingsStore(Path.Combine(temporary, "labels.json")));
                    try
                    {
                        window.Show();
                        window.UpdateLayout();
                        var panel = (StackPanel)((Viewbox)((Border)window.Content).Child).Child;
                        var button = panel.Children.OfType<Button>().Single();
                        var label = (ToolTip)button.ToolTip;
                        label.IsOpen = true;
                        PumpUntil(() => label.ActualWidth > 0 && PresentationSource.FromVisual(label) is not null);
                        var border = (Border)label.Content;
                        Check(border.CornerRadius.TopLeft == 8 && ((SolidColorBrush)border.Background).Color.A == 255);
                        Check(((TextBlock)border.Child).FontSize == 12 && !label.IsHitTestVisible && label.ActualWidth <= 281);
                        var origin = label.PointToScreen(new Point());
                        var end = label.PointToScreen(new Point(label.ActualWidth, label.ActualHeight));
                        Check(Native.Displays().Any(d => origin.X >= d.Work.X - 1 && origin.Y >= d.Work.Y - 1 && end.X <= d.Work.Right + 1 && end.Y <= d.Work.Bottom + 1));
                        if (edge == DockEdge.Bottom) Check(end.Y <= button.PointToScreen(new Point()).Y);
                        if (args.Length == 2 && args[0] == "--preview" && edge == DockEdge.Bottom)
                        {
                            Directory.CreateDirectory(args[1]);
                            var labelBitmap = new RenderTargetBitmap((int)Math.Ceiling(label.ActualWidth * 2), (int)Math.Ceiling(label.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
                            labelBitmap.Render(label);
                            var labelEncoder = new PngBitmapEncoder(); labelEncoder.Frames.Add(BitmapFrame.Create(labelBitmap));
                            using var labelStream = File.Create(Path.Combine(args[1], "label.png"));
                            labelEncoder.Save(labelStream);
                        }
                        button.ContextMenu.PlacementTarget = button;
                        button.ContextMenu.IsOpen = true;
                        PumpUntil(() => !label.IsOpen);
                        button.ContextMenu.IsOpen = false;
                        label.IsOpen = true;
                        window.UpdateVisibility(true, false, false, 1000);
                        Check(!label.IsOpen);
                    }
                    finally { window.Close(); }
                }
            });
            Run("Dock menus stay above the dock across periodic placement updates", () =>
            {
                foreach (var edge in Enum.GetValues<DockEdge>())
                {
                    var settings = new DockSettings { Edge = edge, Entries = [new(Guid.NewGuid(), temporary)] };
                    var window = new DockWindow(settings, new SettingsStore(Path.Combine(temporary, "menu-order.json")));
                    try
                    {
                        window.Show();
                        PumpUntil(() => window.OwnedWindows.Cast<Window>().Any(w => w is IndicatorWindow));
                        var panel = (StackPanel)((Viewbox)((Border)window.Content).Child).Child;
                        var button = panel.Children.OfType<Button>().Single();
                        var menu = button.ContextMenu;
                        menu.PlacementTarget = button;
                        menu.IsOpen = true;
                        PumpUntil(() => PresentationSource.FromVisual(menu) is HwndSource);
                        var popup = ((HwndSource)PresentationSource.FromVisual(menu)).Handle;
                        var dock = new WindowInteropHelper(window).Handle;
                        var watch = Stopwatch.StartNew();
                        PumpUntil(() =>
                        {
                            Check(menu.IsOpen && IsAbove(popup, dock));
                            return watch.ElapsedMilliseconds >= 2300;
                        });
                        menu.IsOpen = false;
                    }
                    finally { window.Close(); }
                }
            });
            Run("Non-activating menu keeps inside clicks and dismisses outside clicks", () =>
            {
                var owner = new Window { Width = 200, Height = 100, ShowActivated = false, ShowInTaskbar = false };
                var target = new Button { Content = "Test" };
                owner.Content = target;
                owner.Show();
                var menu = new ContextMenu { PlacementTarget = target, Items = { new MenuItem { Header = "Test action" } } };
                menu.IsOpen = true;
                owner.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                using (var dismissal = new ContextMenuDismissal(menu, new WindowInteropHelper(owner).Handle))
                {
                    Check(menu.IsOpen && menu.ActualWidth > 0);
                    dismissal.HandlePointerDown(menu.PointToScreen(new Point(menu.ActualWidth / 2, menu.ActualHeight / 2)));
                    owner.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    Check(menu.IsOpen);
                    dismissal.HandlePointerDown(menu.PointToScreen(new Point(-20, -20)));
                    owner.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    Check(!menu.IsOpen);
                }
                owner.Close();
            });
            Run("Real shell shortcut preserves arguments and working directory", () =>
            {
                var workingDirectory = Path.Combine(temporary, "Répertoire de travail");
                Directory.CreateDirectory(workingDirectory);
                var resultPath = Path.Combine(temporary, "launch result.txt");
                var linkPath = Path.Combine(temporary, "Éditeur avec arguments.lnk");
                var shellType = Type.GetTypeFromProgID("WScript.Shell")!;
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic link = shell.CreateShortcut(linkPath);
                try
                {
                    link.TargetPath = Path.Combine(AppContext.BaseDirectory, "WinDock.Tests.exe");
                    link.Arguments = $"--record \"{resultPath}\" \"été avec espaces\"";
                    link.WorkingDirectory = workingDirectory;
                    link.WindowStyle = 7;
                    link.Save();
                }
                finally { Marshal.ReleaseComObject(link); Marshal.ReleaseComObject(shell); }
                var tracker = new RunningApplications();
                Check(string.Equals(tracker.ResolveTarget(linkPath), Path.Combine(AppContext.BaseDirectory, "WinDock.Tests.exe"), StringComparison.OrdinalIgnoreCase));
                Check(tracker.ResolveTarget(workingDirectory) is null);
                var settings = new DockSettings { Entries = [new(Guid.NewGuid(), linkPath)] };
                var window = new DockWindow(settings, new SettingsStore(Path.Combine(temporary, "launch.json")));
                var panel = (StackPanel)((Viewbox)((Border)window.Content).Child).Child;
                ((Button)panel.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var watch = Stopwatch.StartNew();
                while (!File.Exists(resultPath) && watch.ElapsedMilliseconds < 10000) Thread.Sleep(50);
                Check(File.Exists(resultPath));
                Check(File.ReadAllText(resultPath) == workingDirectory + "|été avec espaces");
                window.Close();
            });
            Run("WM_CLOSE allows cancellation and closes dedicated minimized windows", () =>
            {
                var first = new Window { Title = "WinDock closure test", Width = 240, Height = 120 };
                var second = new Window { Title = "WinDock minimized test", Width = 240, Height = 120 };
                var requests = 0;
                var cancel = true;
                first.Closing += (_, e) => { requests++; e.Cancel = cancel; };
                first.Show(); second.Show(); second.WindowState = WindowState.Minimized;
                try
                {
                    Check(ApplicationWindows.PostClose(new WindowInteropHelper(first).Handle) == 0);
                    PumpUntil(() => requests == 1);
                    Check(first.IsVisible);
                    Check(ApplicationWindows.PostClose(new WindowInteropHelper(second).Handle) == 0);
                    PumpUntil(() => !second.IsVisible);
                    cancel = false;
                    Check(ApplicationWindows.PostClose(new WindowInteropHelper(first).Handle) == 0);
                    PumpUntil(() => !first.IsVisible);
                    Check(requests == 2);
                }
                finally { cancel = false; first.Close(); second.Close(); }
            });
            Run("Close menu refreshes counts, rejects stale results and dispatches one close request", () =>
            {
                var entry = new DockEntry(Guid.NewGuid(), Path.Combine(temporary, "menu-app.exe"));
                var service = new FakeApplicationWindows();
                var window = new DockWindow(new DockSettings { Entries = [entry] },
                    new SettingsStore(Path.Combine(temporary, "close-menu.json")), new FakeRecycleBin(), service);
                window.Show();
                try
                {
                    var panel = (StackPanel)((Viewbox)((Border)window.Content).Child).Child;
                    var button = panel.Children.OfType<Button>().Single();
                    var menu = button.ContextMenu;
                    menu.PlacementTarget = button;
                    bool HasClose() => menu.Items.OfType<MenuItem>().Any(i => ((string?)i.Header)?.StartsWith("Fermer") == true);
                    menu.IsOpen = true;
                    PumpUntil(() => service.Pending.Count == 1);
                    menu.IsOpen = false;
                    menu.IsOpen = true;
                    PumpUntil(() => service.Pending.Count == 2);
                    service.Pending[0].SetResult(2);
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    Check(!HasClose());
                    service.Pending[1].SetResult(0);
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    Check(!HasClose());
                    foreach (var count in new[] { 1, 2 })
                    {
                        menu.IsOpen = false; menu.IsOpen = true;
                        service.Pending[^1].SetResult(count);
                        PumpUntil(HasClose);
                        Check(Equals(((MenuItem)menu.Items[0]).Header, count == 1 ? "Fermer l’application" : "Fermer toutes les fenêtres"));
                        Check(Equals(((MenuItem)menu.Items[1]).Header, "Modifier / réparer…"));
                    }
                    if (args.Length == 2 && args[0] == "--preview")
                    {
                        menu.UpdateLayout();
                        Directory.CreateDirectory(args[1]);
                        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth * 2), (int)Math.Ceiling(menu.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
                        bitmap.Render(menu);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var stream = File.Create(Path.Combine(args[1], "close-menu.png")); encoder.Save(stream);
                    }
                    ((MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Check(!menu.IsOpen && service.Closes == 1);
                }
                finally { window.Close(); }
            });
            Run("Managed shortcuts survive original deletion, preserve parameters and repair atomically", () =>
            {
                var root = Path.Combine(temporary, "managed");
                Directory.CreateDirectory(root);
                var work = Path.Combine(root, "Travail été");
                Directory.CreateDirectory(work);
                var source = Path.Combine(root, "Mon application.lnk");
                var output = Path.Combine(root, "result.txt");
                var executable = Path.Combine(AppContext.BaseDirectory, "WinDock.Tests.exe");
                var argumentText = "--record \"" + output + "\" \"été \\\"cité\\\" espaces\"";
                ShellShortcut.Write(source, null, new(executable, argumentText, work, true));
                var originalBytes = File.ReadAllBytes(source);
                var store = new SettingsStore(Path.Combine(root, "settings.json"));
                var service = new ManagedShortcuts(store);
                var original = new DockEntry(Guid.NewGuid(), source);
                var separator = new DockEntry(Guid.NewGuid(), "", true);
                var missing = new DockEntry(Guid.NewGuid(), Path.Combine(root, "absent.lnk"));
                var settings = new DockSettings { Entries = [original, separator, missing] };
                store.Save(settings);
                service.Migrate(settings);
                var managed = settings.Entries[0];
                Check(managed.Id == original.Id && managed.SourcePath == source && managed.DisplayName == "Mon application");
                Check(File.ReadAllBytes(managed.Path).SequenceEqual(originalBytes));
                Check(settings.Entries[1] == separator && settings.Entries[2] == missing);
                File.Delete(source);
                var reloaded = store.Load(out var warning);
                Check(warning is null && !service.IsMissing(reloaded.Entries[0]) && service.IsMissing(missing));
                using (var child = Process.Start(new ProcessStartInfo(managed.Path) { UseShellExecute = true }))
                {
                    var watch = Stopwatch.StartNew();
                    while (!File.Exists(output) && watch.ElapsedMilliseconds < 10000) Thread.Sleep(50);
                    Check(File.ReadAllText(output) == work + "|été \"cité\" espaces");
                    child?.WaitForExit(5000);
                }
                var details = ShellShortcut.Read(managed.Path);
                Check(details.Editable && details.Arguments == argumentText && details.WorkingDirectory == work);
                var cleared = service.PrepareEdit(managed, managed.Path, "Sans arguments", details with { Arguments = "", WorkingDirectory = "" });
                Check(ShellShortcut.Read(cleared.Path).Arguments == "" && ShellShortcut.Read(cleared.Path).WorkingDirectory == "");
                service.Commit(settings, [cleared, separator, missing]);
                Check(!File.Exists(managed.Path) && File.Exists(cleared.Path));
                var staged = service.PrepareEdit(cleared, cleared.Path, "Nouvelle version", details);
                using (var locked = new FileStream(Path.Combine(root, "settings.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var failed = false;
                    try { service.Commit(settings, [staged, separator, missing]); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
                    Check(failed && settings.Entries[0] == cleared && File.Exists(cleared.Path) && !File.Exists(staged.Path));
                }
                var beforeCancel = Directory.GetFiles(Path.Combine(root, "Shortcuts")).Length;
                var editor = new ShortcutEditor(cleared, service, _ => throw new Exception("Cancel must not commit"));
                editor.Show();
                if (args.Length == 2 && args[0] == "--preview")
                {
                    Directory.CreateDirectory(args[1]);
                    editor.UpdateLayout();
                    var content = (FrameworkElement)editor;
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(content);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var preview = File.Create(Path.Combine(args[1], "shortcut-editor.png"));
                    encoder.Save(preview);
                }
                editor.Close();
                Check(Directory.GetFiles(Path.Combine(root, "Shortcuts")).Length == beforeCancel);
                var repaired = service.PrepareEdit(missing, executable, "Réparé", new(executable, argumentText, work, true));
                service.Commit(settings, [cleared, separator, repaired]);
                Check(settings.Entries[2].Id == missing.Id && !service.IsMissing(repaired));
                Check(new RunningApplications().ResolveTarget(repaired.Path) == executable);
                var folder = service.PrepareEdit(cleared, work, "Mon dossier", new(work, "", "", false));
                service.Commit(settings, [folder, separator, repaired]);
                Check(settings.Entries[0].Path == work && !File.Exists(cleared.Path));
                // Unreadable links are retained exactly, never reconstructed from empty fields.
                var opaque = Path.Combine(root, "opaque.lnk");
                File.WriteAllBytes(opaque, [1, 2, 3, 4]);
                var opaqueEntry = new DockEntry(Guid.NewGuid(), opaque);
                var preserved = service.PrepareEdit(opaqueEntry, opaque, "Spécial", new(opaque, "", "", false));
                Check(File.ReadAllBytes(preserved.Path).SequenceEqual(File.ReadAllBytes(opaque)));
            });
            Run("Catalogue search and deduplication preserve distinct launch parameters", () =>
            {
                Check(ApplicationCatalog.Matches("Éditeur français", "editeur"));
                Check(ApplicationCatalog.Matches("Éditeur français", " FRANCAIS "));
                Check(!ApplicationCatalog.Matches("Calculatrice", "navigateur"));
                var key = ApplicationCatalog.LaunchKey(new(@"C:\app.exe", "--profile A", "", true));
                var otherKey = ApplicationCatalog.LaunchKey(new(@"C:\app.exe", "--profile a", "", true));
                var a = new CatalogApplication("Application", "app1", null, key);
                var b = new CatalogApplication("Application", "app2", null, otherKey);
                Check(ApplicationCatalog.Deduplicate([a, a with { Name = "Copie" }, b]).Count == 2);
                Check(ApplicationCatalog.Deduplicate([a with { ApplicationId = null }, b with { ApplicationId = null }]).Count == 2);
                Check(ApplicationCatalog.Deduplicate([a, b with { LaunchKey = key }]).Count == 1);
            });
            Run("Catalogue multi-select, existing markers, filtering and cancellation", () =>
            {
                var a = new CatalogApplication("Éditeur", "test.editor", null, "test.editor");
                var b = new CatalogApplication("Calculatrice", "test.calculator", null, "test.calculator");
                var existing = new DockEntry(Guid.NewGuid(), "absent.lnk", ApplicationId: "test.existing");
                var c = new CatalogApplication("Déjà ajouté", "test.existing", null, "test.existing");
                var entries = new List<DockEntry> { existing };
                var added = new List<string>();
                var manual = 0; var folders = 0; var separators = 0;
                var picker = new CatalogWindow(() => entries, app =>
                {
                    added.Add(app.ApplicationId!);
                    entries.Add(new(Guid.NewGuid(), "test.lnk", ApplicationId: app.ApplicationId));
                    return Task.CompletedTask;
                }, _ => manual++, _ => folders++, () => separators++, _ => Task.FromResult(new CatalogResult([a, b, c], null)));
                picker.Show();
                PumpUntil(() => picker.Applications.Items.Count == 3 && picker.RefreshButton.IsEnabled);
                var choices = picker.Applications.Items.Cast<CatalogChoice>().ToArray();
                Check(!choices.Single(choice => choice.Application == c).CanAdd);
                choices.Single(choice => choice.Application == a).IsSelected = true;
                choices.Single(choice => choice.Application == b).IsSelected = true;
                picker.Search.Text = "editeur";
                Check(picker.Applications.Items.Count == 1 && picker.AddButton.IsEnabled);
                picker.AddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(() => added.Count == 2 && picker.ManualButton.IsEnabled);
                Check(!choices[0].CanAdd && !choices[1].CanAdd && !picker.AddButton.IsEnabled);
                picker.ManualButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                picker.FolderButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                picker.SeparatorButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(manual == 1 && folders == 1 && separators == 1);
                picker.Search.Text = "introuvable";
                Check(picker.Applications.Items.Count == 0);
                picker.Close();
                var pending = new TaskCompletionSource<CatalogResult>();
                CancellationToken token = default;
                var closing = new CatalogWindow(() => [], _ => Task.CompletedTask, _ => { }, _ => { }, () => { },
                    ct => { token = ct; return pending.Task; });
                closing.Show();
                PumpUntil(() => token.CanBeCanceled);
                closing.Close();
                Check(token.IsCancellationRequested);
                pending.SetResult(new([a], null));
                var empty = new CatalogWindow(() => [], _ => Task.CompletedTask, _ => { }, _ => { }, () => { },
                    _ => Task.FromResult(new CatalogResult([], "Catalogue indisponible")));
                empty.Show();
                PumpUntil(() => empty.RefreshButton.IsEnabled && empty.Status.Text.Contains("indisponible"));
                Check(empty.ManualButton.IsEnabled && !empty.AddButton.IsEnabled);
                empty.Close();
            });
            Run("Catalogue cache persists icons, shares refresh and survives partial failures", () =>
            {
                var directory = Path.Combine(temporary, "catalog-cache");
                var icon = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 10, 20, 30, 255 }, 4);
                icon.Freeze();
                var app = new CatalogApplication("Éditeur", "test.cached", null, "cached", icon);
                var calls = 0;
                var completion = new TaskCompletionSource<CatalogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                var cache = new CatalogCache(directory, _ => { Interlocked.Increment(ref calls); return completion.Task; });
                cache.PreloadAsync().GetAwaiter().GetResult();
                Check(cache.Current is null);
                var first = cache.RefreshAsync();
                Check(ReferenceEquals(first, cache.RefreshAsync()) && calls == 1);
                completion.SetResult(new([app], null));
                first.GetAwaiter().GetResult();
                var restored = new CatalogCache(directory, _ => Task.FromResult(new CatalogResult([], "Lecture partielle")));
                restored.PreloadAsync().GetAwaiter().GetResult();
                Check(restored.Current!.Applications.Single().Icon is BitmapSource { IsFrozen: true });
                Check(restored.RefreshAsync().GetAwaiter().GetResult().Applications.Count == 1);
                Check(restored.Current.Applications.Single().Name == "Éditeur");
                var failing = new CatalogCache(directory, _ => Task.FromException<CatalogResult>(new IOException("indisponible")));
                Check(failing.RefreshAsync().GetAwaiter().GetResult().Applications.Count == 1);
                var empty = new CatalogCache(directory, _ => Task.FromResult(new CatalogResult([], null)));
                Check(empty.RefreshAsync().GetAwaiter().GetResult().Applications.Count == 0);
                var emptyRestored = new CatalogCache(directory);
                emptyRestored.PreloadAsync().GetAwaiter().GetResult();
                Check(emptyRestored.Current!.Applications.Count == 0);
                File.WriteAllText(Path.Combine(directory, "catalog.json"), "{broken");
                var broken = new CatalogCache(directory);
                broken.PreloadAsync().GetAwaiter().GetResult(); Check(broken.Current is null);
                File.WriteAllText(Path.Combine(directory, "catalog.json"), "{\"Version\":99,\"Applications\":[]}");
                var incompatible = new CatalogCache(directory);
                incompatible.PreloadAsync().GetAwaiter().GetResult(); Check(incompatible.Current is null);
                File.WriteAllText(Path.Combine(directory, "catalog.json"), "{\"Version\":1,\"Applications\":[{\"Name\":\"Test\",\"ApplicationId\":\"test\",\"LaunchKey\":\"test\",\"Png\":\"AQID\"}]}");
                var badIcon = new CatalogCache(directory);
                badIcon.PreloadAsync().GetAwaiter().GetResult(); Check(badIcon.Current!.Applications.Single().Icon is null);
                File.WriteAllText(Path.Combine(directory, "catalog.json"), File.ReadAllText(Path.Combine(directory, "catalog.json")).Replace("AQID", "not-base64!"));
                var badEncoding = new CatalogCache(directory);
                badEncoding.PreloadAsync().GetAwaiter().GetResult(); Check(badEncoding.Current!.Applications.Single().Icon is null);
                var blocked = Path.Combine(temporary, "cache-blocked"); File.WriteAllText(blocked, "file");
                var unwritable = new CatalogCache(blocked, _ => Task.FromResult(new CatalogResult([app], null)));
                Check(unwritable.RefreshAsync().GetAwaiter().GetResult().Warning is not null);
                Check(unwritable.Current!.Applications.Count == 1);
            });
            Run("Cached catalogue stays interactive and preserves selection during shared refresh", () =>
            {
                var app = new CatalogApplication("Éditeur", "cached.editor", null, "cached.editor");
                var directory = Path.Combine(temporary, "catalog-ui-cache");
                new CatalogCache(directory, _ => Task.FromResult(new CatalogResult([app], null))).RefreshAsync().GetAwaiter().GetResult();
                var pending = new TaskCompletionSource<CatalogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                var calls = 0;
                var cache = new CatalogCache(directory, _ => { Interlocked.Increment(ref calls); return pending.Task; });
                var picker = new CatalogWindow(() => [], _ => Task.CompletedTask, _ => { }, _ => { }, () => { }, cache: cache);
                picker.Show();
                PumpUntil(() => picker.Applications.Items.Count == 1 && calls == 1);
                ((CatalogChoice)picker.Applications.Items[0]).IsSelected = true;
                picker.Search.Text = "editeur";
                Check(picker.AddButton.IsEnabled && !picker.RefreshButton.IsEnabled);
                pending.SetResult(new([app with { Name = "Éditeur actualisé" }], null));
                PumpUntil(() => picker.RefreshButton.IsEnabled);
                Check(picker.Search.Text == "editeur" && ((CatalogChoice)picker.Applications.Items[0]).IsSelected);
                picker.Close();
                var delayed = new TaskCompletionSource<CatalogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                CancellationToken refreshToken = default;
                var shared = new CatalogCache(directory, ct => { refreshToken = ct; return delayed.Task; });
                var closing = new CatalogWindow(() => [], _ => Task.CompletedTask, _ => { }, _ => { }, () => { }, cache: shared);
                closing.Show(); PumpUntil(() => refreshToken.CanBeCanceled); closing.Close();
                Check(!refreshToken.IsCancellationRequested);
                var reopening = new CatalogWindow(() => [], _ => Task.CompletedTask, _ => { }, _ => { }, () => { }, cache: shared);
                reopening.Show(); PumpUntil(() => reopening.Applications.Items.Count == 1);
                delayed.SetResult(new([app], null));
                PumpUntil(() => reopening.RefreshButton.IsEnabled); reopening.Close();
                shared.Stop(); Check(refreshToken.IsCancellationRequested);
            });
            Run("Removal commits before cleanup and preserves original targets", () =>
            {
                var root = Path.Combine(temporary, "removal"); Directory.CreateDirectory(root);
                var store = new SettingsStore(Path.Combine(root, "settings.json"));
                var manager = new ManagedShortcuts(store);
                var original = Path.Combine(root, "Original.lnk");
                var executable = Path.Combine(AppContext.BaseDirectory, "WinDock.Tests.exe");
                ShellShortcut.Write(original, null, new(executable, "", root, true));
                var app = manager.Import(new(Guid.NewGuid(), original));
                var folder = new DockEntry(Guid.NewGuid(), root);
                var separator = new DockEntry(Guid.NewGuid(), "", true);
                var settings = new DockSettings { Entries = [app, folder, separator] }; store.Save(settings);
                using (var locked = new FileStream(Path.Combine(root, "settings.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var failed = false;
                    try { manager.Commit(settings, [folder, separator]); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
                    Check(failed && settings.Entries.Contains(app) && File.Exists(app.Path));
                }
                manager.Commit(settings, [folder, separator]);
                Check(!File.Exists(app.Path) && File.Exists(original) && File.Exists(executable));
                manager.Commit(settings, [separator]); Check(Directory.Exists(root));
                manager.Commit(settings, []); Check(store.Load(out _).Entries.Count == 0);
            });
            Run("Drag removal uses scaled margin and explicit release, never Escape", () =>
            {
                foreach (var scale in new[] { 1.0, 1.5, 2.0 })
                {
                    var dock = new Rect(-1000, 200, 400 * scale, 60 * scale);
                    Check(!DragRemoval.IsArmed(dock, new Point(dock.Left - 23 * scale, dock.Top), scale));
                    Check(DragRemoval.IsArmed(dock, new Point(dock.Left - 25 * scale, dock.Top), scale));
                    Check(DragRemoval.IsArmed(dock, new Point(dock.Right + 25 * scale, dock.Top), scale));
                    Check(DragRemoval.IsArmed(dock, new Point(dock.Left, dock.Top - 25 * scale), scale));
                    Check(DragRemoval.IsArmed(dock, new Point(dock.Left, dock.Bottom + 25 * scale), scale));
                    Check(!DragRemoval.IsArmed(dock, new Point(dock.Left + 1, dock.Top + 1), scale));
                }
                Check(DragRemoval.ShouldRemove(true, false, System.Windows.DragDropKeyStates.None));
                Check(!DragRemoval.ShouldRemove(true, true, System.Windows.DragDropKeyStates.None));
                Check(!DragRemoval.ShouldRemove(false, false, System.Windows.DragDropKeyStates.None));
                Check(!DragRemoval.ShouldRemove(true, false, System.Windows.DragDropKeyStates.LeftMouseButton));
            });
            Run("Missing catalogue apps handle unknown availability and retain identity through rename", () =>
            {
                var root = Path.Combine(temporary, "shell-presence");
                Directory.CreateDirectory(root);
                var store = new SettingsStore(Path.Combine(root, "settings.json"));
                var service = new ManagedShortcuts(store);
                var link = Path.Combine(root, "opaque.lnk");
                File.WriteAllBytes(link, [1, 2, 3, 4]);
                var entry = new DockEntry(Guid.NewGuid(), link, SourcePath: "shell:AppsFolder\\test.app", ApplicationId: "test.app");
                ApplicationCatalog.RecordPresence(null);
                Check(!service.IsMissing(entry));
                ApplicationCatalog.RecordPresence([]);
                Check(service.IsMissing(entry));
                ApplicationCatalog.RecordPresence(null);
                Check(!service.IsMissing(entry));
                var renamed = service.PrepareEdit(entry, entry.Path, "Nouveau nom", new(entry.Path, "", "", false));
                Check(renamed.ApplicationId == entry.ApplicationId);
                var exe = Path.Combine(AppContext.BaseDirectory, "WinDock.Tests.exe");
                var repaired = service.PrepareEdit(entry, exe, "Application classique", new(exe, "", "", true));
                Check(repaired.ApplicationId is null);
                var impossible = entry with { ApplicationId = "WinDock.NoSuchApp", Path = exe };
                Check(!new RunningApplications().Snapshot([impossible]).Contains(entry.Id));
            });
            Run("Running indicators follow a real process lifetime and exact executable path", () =>
            {
                var childDirectory = Path.Combine(temporary, "process-test");
                Directory.CreateDirectory(childDirectory);
                foreach (var file in Directory.GetFiles(AppContext.BaseDirectory))
                    File.Copy(file, Path.Combine(childDirectory, Path.GetFileName(file)));
                var executable = Path.Combine(childDirectory, "WinDock.Tests.exe");
                var ready = Path.Combine(childDirectory, "ready.txt");
                var entry = new DockEntry(Guid.NewGuid(), executable);
                var separator = new DockEntry(Guid.NewGuid(), "", true);
                var tracker = new RunningApplications();
                Check(tracker.Snapshot([entry, separator]).Count == 0);
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("--hold"); start.ArgumentList.Add(ready);
                using var process = Process.Start(start)!;
                try
                {
                    var wait = Stopwatch.StartNew();
                    while (!File.Exists(ready) && !process.HasExited && wait.ElapsedMilliseconds < 10000) Thread.Sleep(50);
                    Check(File.Exists(ready));
                    Check(tracker.Snapshot([entry, separator]).SetEquals([entry.Id]));
                }
                finally { if (!process.HasExited) process.Kill(); process.WaitForExit(); }
                Check(tracker.Snapshot([entry, separator]).Count == 0);
            });
            if (args.Length == 2 && args[0] == "--catalog-smoke")
            {
                Run("Real Windows catalogue, persisted Shell link and Calculator activation", () =>
                {
                    var task = ApplicationCatalog.LoadAsync(CancellationToken.None);
                    PumpUntil(() => task.IsCompleted, 60000);
                    var result = task.GetAwaiter().GetResult();
                    Check(result.Applications.Count > 0 && result.Warning is null);
                    Console.WriteLine($"CATALOG: {result.Applications.Count} apps; {result.Applications.Count(a => a.ShortcutPath is not null)} classic shortcuts; {result.Applications.Count(a => a.ApplicationId?.Contains('!') == true)} packaged apps.");
                    var picker = new CatalogWindow(() => [], _ => Task.CompletedTask, _ => { }, _ => { }, () => { },
                        _ => Task.FromResult(result));
                    picker.Show();
                    PumpUntil(() => picker.RefreshButton.IsEnabled && picker.Applications.Items.Count > 0);
                    picker.UpdateLayout();
                    Directory.CreateDirectory(args[1]);
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(picker.ActualWidth), (int)Math.Ceiling(picker.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(picker);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var stream = File.Create(Path.Combine(args[1], "catalogue.png"))) encoder.Save(stream);
                    picker.Close();
                    var calculator = result.Applications.First(app => app.ApplicationId?.StartsWith("Microsoft.WindowsCalculator_", StringComparison.OrdinalIgnoreCase) == true);
                    var store = new SettingsStore(Path.Combine(temporary, "catalog-smoke", "settings.json"));
                    var manager = new ManagedShortcuts(store);
                    var settings = new DockSettings();
                    var entry = manager.ImportApplication(calculator);
                    manager.Commit(settings, [entry]);
                    var persisted = store.Load(out var warning).Entries.Single();
                    Check(warning is null && persisted.ApplicationId == calculator.ApplicationId && !manager.IsMissing(persisted));
                    Check(ApplicationCatalog.IsAdded(calculator, ApplicationCatalog.ExistingKeys([persisted])));
                    using var activation = Process.Start(new ProcessStartInfo(persisted.Path) { UseShellExecute = true });
                    var tracker = new RunningApplications();
                    PumpUntil(() => tracker.Snapshot([persisted]).Contains(persisted.Id), 20000);
                    Console.WriteLine("STORE: Calculator launched from persisted .lnk; running indicator recognized its Windows identity.");
                    // Also exercise the catalogue import path with a real, argument-bearing classic shortcut.
                    var source = Path.Combine(temporary, "Classic catalogue test.lnk");
                    var output = Path.Combine(temporary, "classic-catalogue.txt");
                    var executable = Path.Combine(AppContext.BaseDirectory, "WinDock.Tests.exe");
                    var details = new ShortcutDetails(executable, "--record \"" + output + "\" \"catalogue été\"", temporary, true);
                    ShellShortcut.Write(source, null, details);
                    var classic = manager.ImportApplication(new("Classic test", null, source, ApplicationCatalog.LaunchKey(details)));
                    manager.Commit(settings, [persisted, classic]);
                    File.Delete(source);
                    var classicReloaded = store.Load(out _).Entries[1];
                    using var classicProcess = Process.Start(new ProcessStartInfo(classicReloaded.Path) { UseShellExecute = true });
                    PumpUntil(() => File.Exists(output), 10000);
                    Check(File.ReadAllText(output) == temporary + "|catalogue été");
                    classicProcess?.WaitForExit(5000);
                });
            }
            if (args.Length == 2 && args[0] == "--preview")
            {
                Run("Render populated and empty WPF docks", () =>
                {
                    Directory.CreateDirectory(args[1]);
                    foreach (var populated in new[] { false, true })
                    {
                        var settings = new DockSettings();
                        if (populated)
                        {
                            settings.ShowRecycleBin = true;
                            settings.BackgroundOpacity = 0.55;
                            settings.Entries.Add(new(Guid.NewGuid(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")));
                            settings.Entries.Add(new(Guid.NewGuid(), Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
                            settings.Entries.Add(new(Guid.NewGuid(), "", true));
                            settings.Entries.Add(new(Guid.NewGuid(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe")));
                        }
                        var window = new DockWindow(settings, new SettingsStore(Path.Combine(temporary, "preview.json")));
                        new WindowInteropHelper(window).EnsureHandle();
                        if (populated)
                        {
                            var refresh = window.RefreshRecycleAsync();
                            PumpUntil(() => refresh.IsCompleted);
                            refresh.GetAwaiter().GetResult();
                            var bin = ((StackPanel)((Viewbox)((Border)window.Content).Child).Child).Children.OfType<Button>().Last();
                            Check(bin.Content is Image { Source.IsFrozen: true });
                        }
                        var content = (FrameworkElement)window.Content;
                        content.Measure(new Size(window.Width, window.Height));
                        content.Arrange(new Rect(0, 0, window.Width, window.Height));
                        content.UpdateLayout();
                        var bitmap = new RenderTargetBitmap((int)window.Width * 2, (int)window.Height * 2, 192, 192, PixelFormats.Pbgra32);
                        bitmap.Render(content);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var stream = File.Create(Path.Combine(args[1], populated ? "dock-populated.png" : "dock-empty.png"));
                        encoder.Save(stream);
                        if (populated)
                        {
                            var settingsItem = window.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Réglages…"));
                            settingsItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                            var settingsWindow = Application.Current.Windows.Cast<Window>().Single(w => w.Title == "Réglages — WinDock");
                            try
                            {
                                settingsWindow.UpdateLayout();
                                var settingsPanel = (StackPanel)((ScrollViewer)settingsWindow.Content).Content;
                                Check(settingsWindow.Icon is BitmapSource settingsIcon && settingsIcon.PixelWidth == 256 && settingsIcon.PixelHeight == 256);
                                Check(settingsPanel.Children.OfType<TextBlock>().Any(text => text.Text.StartsWith("WinDock — Version 0.1.0-beta.")));
                                var settingsBitmap = new RenderTargetBitmap((int)Math.Ceiling(settingsWindow.ActualWidth), (int)Math.Ceiling(settingsWindow.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                                settingsBitmap.Render(settingsWindow);
                                var settingsEncoder = new PngBitmapEncoder();
                                settingsEncoder.Frames.Add(BitmapFrame.Create(settingsBitmap));
                                using var settingsStream = File.Create(Path.Combine(args[1], "settings.png"));
                                settingsEncoder.Save(settingsStream);
                            }
                            finally { settingsWindow.Close(); }
                        }
                        window.Close();
                    }
                });
            }
            Console.WriteLine($"PASS: {passed} test groups.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(temporary, true); }
    }

    private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed"); }
    private sealed class FakeApplicationWindows : IApplicationWindows
    {
        internal readonly List<TaskCompletionSource<int>> Pending = [];
        internal int Closes;
        public Task<int> CountAsync(DockEntry entry, CancellationToken token)
        {
            var request = new TaskCompletionSource<int>(); Pending.Add(request); return request.Task;
        }
        public Task<int> CloseAsync(DockEntry entry, CancellationToken token) { Closes++; return Task.FromResult(0); }
    }
    private sealed class FakeRecycleBin : IRecycleBinService
    {
        internal bool? EmptyState;
        internal bool ThrowOnRead;
        internal int Reads, Opens, Empties, MaximumConcurrentReads;
        private int concurrentReads;
        public RecycleBinSnapshot Read()
        {
            var active = Interlocked.Increment(ref concurrentReads);
            MaximumConcurrentReads = Math.Max(MaximumConcurrentReads, active);
            Interlocked.Increment(ref Reads);
            try
            {
                Check(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA);
                Thread.Sleep(30);
                if (ThrowOnRead) throw new IOException("Simulated unavailable recycle bin");
                return new(EmptyState, null);
            }
            finally { Interlocked.Decrement(ref concurrentReads); }
        }
        public void Open() => Interlocked.Increment(ref Opens);
        public void Empty(nint owner) { EmptyState = true; Interlocked.Increment(ref Empties); }
    }
    private static void PumpUntil(Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        var frame = new DispatcherFrame();
        var watch = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (condition() || watch.ElapsedMilliseconds > timeoutMilliseconds) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Check(condition());
    }
    private static void Run(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
}
