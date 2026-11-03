using System.IO;
using WinDock.Core;

namespace WinDock;

/// <summary>Stages immutable shortcut revisions and commits settings before retiring previous copies.</summary>
internal sealed class ManagedShortcuts(SettingsStore store)
{
    private readonly string directory = Path.Combine(store.DataDirectory, "Shortcuts");

    internal static string Name(DockEntry entry) => entry.DisplayName ?? EntryRules.DisplayName(entry.SourcePath ?? entry.Path);

    internal DockEntry Import(DockEntry entry)
    {
        if (entry.IsSeparator || !ShellShortcut.IsLink(entry.Path) || !File.Exists(entry.Path) || IsManaged(entry)) return entry;
        var destination = NewPath(entry.Id);
        File.Copy(entry.Path, destination);
        return entry with { Path = destination, DisplayName = Name(entry), SourcePath = entry.SourcePath ?? entry.Path };
    }

    internal DockEntry ImportApplication(CatalogApplication application)
    {
        var id = Guid.NewGuid();
        if (application.ShortcutPath is { } source)
        {
            if (!File.Exists(source)) throw new FileNotFoundException("Le raccourci n’est plus disponible. Actualisez le catalogue.", source);
            return Import(new(id, source, DisplayName: application.Name, ApplicationId: application.ApplicationId));
        }
        if (string.IsNullOrWhiteSpace(application.ApplicationId)) throw new ArgumentException("L’application ne possède pas d’identité Windows.");
        var destination = NewPath(id);
        try { ShellShortcut.WriteShellApplication(destination, application.ApplicationId); }
        catch { if (File.Exists(destination)) File.Delete(destination); throw; }
        return new(id, destination, DisplayName: application.Name, SourcePath: "shell:AppsFolder\\" + application.ApplicationId, ApplicationId: application.ApplicationId);
    }

    internal DockEntry PrepareEdit(DockEntry entry, string source, string name, ShortcutDetails details)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Indiquez un nom pour cette entrée.");
        var target = Environment.ExpandEnvironmentVariables(details.Target.Trim().Trim('"'));
        if (!details.Editable && string.Equals(target, source, StringComparison.OrdinalIgnoreCase) && ShellShortcut.IsLink(source) && File.Exists(source))
        {
            // Opaque links remain byte-for-byte intact; only their displayed name can change.
            var copy = NewPath(entry.Id);
            File.Copy(source, copy);
            return entry with { Path = copy, DisplayName = name.Trim(), SourcePath = Source(entry, source), ApplicationId = source == entry.Path ? entry.ApplicationId : null };
        }
        if (!EntryRules.IsSupported(target) || ShellShortcut.IsLink(target))
            throw new ArgumentException("Choisissez un exécutable ou un dossier existant, ou importez un raccourci avec Parcourir.");
        if (Directory.Exists(target))
            return entry with { Path = Path.GetFullPath(target), DisplayName = name.Trim(), SourcePath = target, ApplicationId = null };
        if (!string.IsNullOrWhiteSpace(details.WorkingDirectory) && !Directory.Exists(Environment.ExpandEnvironmentVariables(details.WorkingDirectory)))
            throw new ArgumentException("Le dossier de travail est introuvable.");
        var destination = NewPath(entry.Id);
        try
        {
            ShellShortcut.Write(destination, ShellShortcut.IsLink(source) && File.Exists(source) ? source : null,
                details with { Target = Path.GetFullPath(target) });
        }
        catch { if (File.Exists(destination)) File.Delete(destination); throw; }
        var sameTarget = source == entry.Path && string.Equals(ShellShortcut.Read(entry.Path).Target, target, StringComparison.OrdinalIgnoreCase);
        return entry with { Path = destination, DisplayName = name.Trim(), SourcePath = Source(entry, source), ApplicationId = sameTarget ? entry.ApplicationId : null };
    }

    internal void Commit(DockSettings settings, List<DockEntry> entries)
    {
        var old = settings.Entries;
        settings.Entries = entries;
        try { store.Save(settings); }
        catch
        {
            settings.Entries = old;
            foreach (var entry in entries.Where(e => !old.Any(o => o.Path == e.Path))) DeleteManaged(entry);
            throw;
        }
        foreach (var entry in old.Where(e => !entries.Any(n => n.Path == e.Path))) DeleteManaged(entry);
    }

    internal void Migrate(DockSettings settings)
    {
        var entries = new List<DockEntry>();
        try
        {
            foreach (var entry in settings.Entries) entries.Add(Import(entry));
            if (!entries.SequenceEqual(settings.Entries)) Commit(settings, entries);
        }
        catch
        {
            foreach (var entry in entries.Where(e => !settings.Entries.Any(old => old.Path == e.Path))) DeleteManaged(entry);
            throw;
        }
    }

    internal bool IsMissing(DockEntry entry)
    {
        if (entry.IsSeparator) return false;
        if (!File.Exists(entry.Path) && !Directory.Exists(entry.Path)) return true;
        if (entry.ApplicationId is { } appId && entry.SourcePath?.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase) == true)
            return ApplicationCatalog.IsKnownMissing(appId);
        if (!ShellShortcut.IsLink(entry.Path)) return false;
        var target = ShellShortcut.Read(entry.Path).Target;
        if (!Path.IsPathFullyQualified(target) && entry.ApplicationId is { } id) return ApplicationCatalog.IsKnownMissing(id);
        return Path.IsPathFullyQualified(target) && !File.Exists(target) && !Directory.Exists(target);
    }

    private string NewPath(Guid id)
    {
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{id:N}-{Guid.NewGuid():N}.lnk");
    }

    private bool IsManaged(DockEntry entry) => string.Equals(Path.GetDirectoryName(Path.GetFullPath(entry.Path)),
        Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase) &&
        Path.GetFileName(entry.Path).StartsWith(entry.Id.ToString("N") + "-", StringComparison.OrdinalIgnoreCase) && ShellShortcut.IsLink(entry.Path);

    private void DeleteManaged(DockEntry entry)
    {
        if (entry.IsSeparator || !IsManaged(entry)) return;
        try { File.Delete(entry.Path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Keep an unused copy if Windows still holds it. */ }
    }

    private static string Source(DockEntry entry, string source) => source == entry.Path ? entry.SourcePath ?? source : source;
}
