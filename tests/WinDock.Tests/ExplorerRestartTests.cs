using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinDock;
using WinDock.Core;

internal static partial class Program
{
    private static void RunExplorerRestartCoreTests()
    {
        Run("Explorer restart stops only the verified shell and handles Windows auto-recovery without launching twice", () =>
        {
            var original = new ExplorerShell(10, 1, ApplicationIdentityResolver.ExplorerExecutable, 100);
            ExplorerShell? current = original; var stops = 0; var starts = 0;
            var automatic = new ExplorerRestarter(() => current, shell =>
            {
                Check(shell == original); stops++; current = original with { ProcessId = 20 }; return Task.CompletedTask;
            }, () => starts++, _ => Task.CompletedTask, 1);
            automatic.RestartAsync(default).GetAwaiter().GetResult();
            Check(stops == 1 && starts == 0);
            current = original;
            using var canceledAfterStop = new CancellationTokenSource();
            var manual = new ExplorerRestarter(() => current, shell =>
            {
                Check(shell == original); stops++; current = null; canceledAfterStop.Cancel(); return Task.CompletedTask;
            }, () => { starts++; current = original with { ProcessId = 30 }; }, _ => Task.CompletedTask, 1);
            manual.RestartAsync(canceledAfterStop.Token).GetAwaiter().GetResult();
            Check(stops == 2 && starts == 1 && current!.ProcessId == 30);
            current = null;
            manual.RestartAsync(default).GetAwaiter().GetResult();
            Check(stops == 2 && starts == 2);
        });
        Run("Explorer restart rejects another session or executable and cancellation never stops a process", () =>
        {
            foreach (var shell in new[] { new ExplorerShell(10, 2, ApplicationIdentityResolver.ExplorerExecutable, 100),
                new ExplorerShell(10, 1, Path.Combine(Path.GetTempPath(), "explorer.exe"), 100) })
            {
                var calls = 0;
                var service = new ExplorerRestarter(() => shell, _ => { calls++; return Task.CompletedTask; }, () => calls++, _ => Task.CompletedTask, 1);
                try { service.RestartAsync(default).GetAwaiter().GetResult(); Check(false); }
                catch (InvalidOperationException) { }
                Check(calls == 0);
            }
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            var canceled = new ExplorerRestarter(() => throw new InvalidOperationException("Must not inspect"), _ => Task.CompletedTask,
                () => throw new InvalidOperationException("Must not start"), _ => Task.CompletedTask, 1);
            try { canceled.RestartAsync(cancellation.Token).GetAwaiter().GetResult(); Check(false); }
            catch (OperationCanceledException) { }
        });
        Run("Explorer restart serializes concurrent requests and reports a missing shell after recovery", () =>
        {
            ExplorerShell? current = new(10, 1, ApplicationIdentityResolver.ExplorerExecutable, 100);
            var stopped = new TaskCompletionSource(); var stopCount = 0; var startCount = 0;
            var service = new ExplorerRestarter(() => current, _ => { stopCount++; current = null; return stopped.Task; },
                () => startCount++, _ => Task.CompletedTask, 1);
            var first = service.RestartAsync(default);
            Check(!first.IsCompleted && service.RestartAsync(default).IsCompleted && stopCount == 1);
            stopped.SetResult();
            try { first.GetAwaiter().GetResult(); Check(false); }
            catch (TimeoutException) { }
            Check(startCount == 1);
        });
        Run("Explorer stop failures never launch a duplicate shell and allow a later retry", () =>
        {
            var original = new ExplorerShell(10, 1, ApplicationIdentityResolver.ExplorerExecutable, 100);
            ExplorerShell? current = original; var attempts = 0; var starts = 0;
            var service = new ExplorerRestarter(() => current, _ =>
            {
                attempts++;
                if (attempts == 1) return Task.FromException(new System.ComponentModel.Win32Exception(5));
                current = null; return Task.CompletedTask;
            }, () => { starts++; current = original with { ProcessId = 20 }; }, _ => Task.CompletedTask, 1);
            try { service.RestartAsync(default).GetAwaiter().GetResult(); Check(false); }
            catch (System.ComponentModel.Win32Exception) { }
            Check(starts == 0 && current == original);
            service.RestartAsync(default).GetAwaiter().GetResult();
            Check(attempts == 2 && starts == 1);
        });
    }

    private static void RunExplorerRestartWindowTests(string temporary, string[] args)
    {
        Run("Explorer menu confirms restart, blocks duplicates and never calls the real restart service", () =>
        {
            var entry = new DockEntry(Guid.NewGuid(), ApplicationIdentityResolver.ExplorerExecutable, DisplayName: "Explorateur");
            var fake = new FakeExplorerRestarter(); var accept = false; var confirmations = 0;
            var window = new DockWindow(new DockSettings { AutoHide = false, Entries = [entry] },
                new SettingsStore(Path.Combine(temporary, "explorer-menu.json")), new FakeRecycleBin(), new FakeApplicationWindows(),
                explorerRestarter: fake, confirmExplorerRestart: () => { confirmations++; return accept; });
            try
            {
                window.Show();
                ((DispatcherTimer)typeof(DockWindow).GetField("timer", PrivateInstance)!.GetValue(window)!).Stop();
                var refresh = (Task)typeof(DockWindow).GetMethod("RefreshRunningAsync", PrivateInstance)!.Invoke(window, null)!;
                PumpUntil(() => refresh.IsCompleted); refresh.GetAwaiter().GetResult();
                var panel = (InsertionPanel)((Viewbox)((Border)window.Content).Child).Child;
                var button = panel.Children.OfType<Button>().Single();
                Check(((Grid)button.Content).Children.OfType<System.Windows.Shapes.Ellipse>().Single().Visibility == Visibility.Hidden);
                var menu = button.ContextMenu;
                Check(MenuHeaders(menu).SequenceEqual(new[] { "Redémarrer l’Explorateur Windows…", "Modifier / réparer…", "Retirer du dock" }));
                var restart = menu.Items.OfType<MenuItem>().First();
                restart.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(confirmations == 1 && fake.Calls == 0 && restart.IsEnabled);
                if (args.Length == 2 && args[0] == "--preview")
                {
                    menu.PlacementTarget = button; menu.IsOpen = true;
                    PumpUntil(() => PresentationSource.FromVisual(menu) is HwndSource);
                    SavePreview(menu, Path.Combine(args[1], "menu-explorer.png"));
                    SavePreview((FrameworkElement)window.Content, Path.Combine(args[1], "explorer-without-badge.png"));
                    menu.IsOpen = false;
                }
                accept = true;
                restart.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                PumpUntil(() => fake.Calls == 1);
                Check(!restart.IsEnabled);
                restart.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(confirmations == 2 && fake.Calls == 1);
                fake.Completion.SetResult();
                PumpUntil(() => restart.IsEnabled);
                Check(!menu.IsOpen && confirmations == 2);
            }
            finally { fake.Completion.TrySetResult(); window.Close(); }
        });
    }

    private sealed class FakeExplorerRestarter : IExplorerRestarter
    {
        internal int Calls;
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task RestartAsync(CancellationToken token) { Interlocked.Increment(ref Calls); return Completion.Task; }
    }
}
