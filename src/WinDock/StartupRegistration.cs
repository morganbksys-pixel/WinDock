using System.IO;

namespace WinDock;

/// <summary>Manages this user's startup shortcut without changing application settings.</summary>
internal sealed class StartupRegistration(string startupDirectory, string executable)
{
    private string ShortcutPath => Path.Combine(startupDirectory, "WinDock.lnk");

    internal static StartupRegistration Current() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        Path.Combine(AppContext.BaseDirectory, "WinDock.exe"));

    internal bool IsEnabled => File.Exists(ShortcutPath);

    internal void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            File.Delete(ShortcutPath);
            return;
        }
        if (!File.Exists(executable))
            throw new FileNotFoundException("L’exécutable WinDock est introuvable. Lancez la version portable pour activer le démarrage automatique.", executable);
        Directory.CreateDirectory(startupDirectory);
        var temporary = Path.Combine(startupDirectory, $"WinDock-{Guid.NewGuid():N}.tmp");
        try
        {
            ShellShortcut.Write(temporary, null, new(Path.GetFullPath(executable), "", Path.GetDirectoryName(Path.GetFullPath(executable))!, true));
            File.Move(temporary, ShortcutPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
