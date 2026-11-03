using System.IO;
using System.Windows;
using WinDock.Core;

namespace WinDock;

public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, @"Local\WinDock.Launcher", out var first);
        if (!first) { instance.Dispose(); instance = null; Shutdown(); return; }
        _ = CatalogCache.Shared.PreloadAsync();
        var store = new SettingsStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinDock", "settings.json"));
        try
        {
            var settings = store.Load(out var warning);
            try { new ManagedShortcuts(store).Migrate(settings); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { warning = (warning is null ? "" : warning + "\n\n") + "Certains raccourcis n’ont pas pu être copiés. Les entrées existantes sont conservées.\n" + ex.Message; }
            var dock = new DockWindow(settings, store);
            MainWindow = dock;
            dock.Show();
            if (warning is not null) dock.ShowNotice(warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, "WinDock", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        CatalogCache.Shared.Stop();
        if (instance is not null) { instance.ReleaseMutex(); instance.Dispose(); }
        base.OnExit(e);
    }
}
