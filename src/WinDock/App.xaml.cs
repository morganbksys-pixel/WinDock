using System.IO;
using System.Windows;
using WinDock.Core;

namespace WinDock;

public partial class App : Application
{
    private Mutex? instance;
    private readonly CancellationTokenSource startup = new();
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, @"Local\WinDock.Launcher", out var first);
        if (!first) { instance.Dispose(); instance = null; Shutdown(); return; }
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _ = CatalogCache.Shared.PreloadAsync();
        var store = new SettingsStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinDock", "settings.json"));
        try
        {
            var settings = store.Load(out var warning, out var status);
            try { await DefaultDockInitializer.InitializeAsync(settings, status, new DefaultDockService(store), startup.Token); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException)
            { warning = "Le dock de départ n’a pas pu être enregistré. Vous pouvez réessayer avec Restaurer le dock par défaut dans les réglages.\n\n" + ex.Message; }
            try { await StaWorker.RunAsync(_ => { new ManagedShortcuts(store).Migrate(settings); return true; }, startup.Token); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { warning = (warning is null ? "" : warning + "\n\n") + "Certains raccourcis n’ont pas pu être copiés. Les entrées existantes sont conservées.\n" + ex.Message; }
            var dock = new DockWindow(settings, store);
            MainWindow = dock;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            dock.Show();
            if (warning is not null) dock.ShowNotice(warning);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, "WinDock", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        startup.Cancel();
        CatalogCache.Shared.Stop();
        if (instance is not null) { instance.ReleaseMutex(); instance.Dispose(); }
        base.OnExit(e);
    }
}
