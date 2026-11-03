using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using WinDock.Core;

namespace WinDock;

internal sealed record CatalogApplication(string Name, string? ApplicationId, string? ShortcutPath,
    string LaunchKey, ImageSource? Icon = null)
{
    internal string Identity => ApplicationId is { Length: > 0 } id ? "app:" + id.ToUpperInvariant() : "launch:" + LaunchKey;
}
internal sealed record CatalogResult(IReadOnlyList<CatalogApplication> Applications, string? Warning);

/// <summary>Enumerates Windows launchable apps, preserving distinct command lines.</summary>
internal static class ApplicationCatalog
{
    private sealed record ShellApp(string Name, string Id, string Target, string Arguments, string WorkingDirectory);
    private sealed record Presence(HashSet<string>? Ids, DateTime Observed);
    private static Presence presence = new(null, DateTime.MinValue);
    internal static void RecordPresence(IEnumerable<string>? ids) => Volatile.Write(ref presence,
        new Presence(ids?.ToHashSet(StringComparer.OrdinalIgnoreCase), DateTime.UtcNow));

    internal static bool IsKnownMissing(string id)
    {
        var current = Volatile.Read(ref presence);
        return current.Ids is not null && DateTime.UtcNow - current.Observed < TimeSpan.FromMinutes(2) && !current.Ids.Contains(id);
    }

    internal static Task RefreshPresenceAsync(CancellationToken token) => StaWorker.RunAsync(ct =>
    {
        try { ReadShellApps(ct); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        { RecordPresence(null); }
        return true;
    }, token);

    internal static Task<CatalogResult> LoadAsync(CancellationToken token) => StaWorker.RunAsync(Load, token);

    private static CatalogResult Load(CancellationToken token)
    {
        var applications = new List<CatalogApplication>();
        var byLaunch = new Dictionary<string, CatalogApplication>(StringComparer.Ordinal);
        string? warning = null;
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms) })
        {
            foreach (var path in FindShortcuts(root, token, () => warning = "Certains raccourcis du menu Démarrer sont inaccessibles. Le catalogue peut être incomplet."))
            {
                token.ThrowIfCancellationRequested();
                var details = ShellShortcut.Read(path);
                if (!details.Editable) continue;
                var key = LaunchKey(details);
                if (byLaunch.ContainsKey(key)) continue;
                var app = new CatalogApplication(Path.GetFileNameWithoutExtension(path), null, path, key);
                byLaunch.Add(key, app);
                applications.Add(app);
            }
        }
        try
        {
            foreach (var shell in ReadShellApps(token))
            {
                var key = LaunchKey(new(shell.Target, shell.Arguments, shell.WorkingDirectory, true));
                byLaunch.TryGetValue(key, out var shortcut);
                if (shortcut is null && shell.Target.Length > 0 && shell.WorkingDirectory.Length == 0)
                {
                    // AppsFolder sometimes omits the working directory. Only merge an unambiguous command.
                    var prefix = Environment.ExpandEnvironmentVariables(shell.Target).Trim().ToUpperInvariant() + "\0" + shell.Arguments + "\0";
                    var candidates = byLaunch.Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal)).Take(2).ToArray();
                    if (candidates.Length == 1) shortcut = candidates[0].Value;
                }
                if (shell.Target.Length > 0 && shortcut is not null)
                {
                    // Keep the real .lnk (and all its flags/arguments), attach the Windows identity.
                    if (shortcut.ApplicationId is null)
                    {
                        var combined = shortcut with { Name = shell.Name, ApplicationId = shell.Id };
                        applications[applications.IndexOf(shortcut)] = combined;
                        byLaunch[shortcut.LaunchKey] = combined;
                    }
                    continue;
                }
                applications.Add(new(shell.Name, shell.Id, null, shell.Target.Length == 0 ? "shell:" + shell.Id : key));
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            RecordPresence(null);
            warning = "Le catalogue Windows est indisponible. Les raccourcis du menu Démarrer et l’ajout manuel restent disponibles.";
        }
        var result = Deduplicate(applications);
        for (var i = 0; i < result.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var app = result[i];
            try { result[i] = app with { Icon = Native.Icon(app.ShortcutPath ?? "shell:AppsFolder\\" + app.ApplicationId) }; }
            catch (Exception ex) when (ex is COMException or ArgumentException or IOException) { }
        }
        return new(result, warning);
    }

    internal static List<CatalogApplication> Deduplicate(IEnumerable<CatalogApplication> applications)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var launches = new HashSet<string>(StringComparer.Ordinal);
        return applications.Where(app =>
        {
            if (identities.Contains(app.Identity) || launches.Contains(app.LaunchKey)) return false;
            identities.Add(app.Identity); launches.Add(app.LaunchKey); return true;
        }).OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    internal static string LaunchKey(ShortcutDetails details) =>
        Environment.ExpandEnvironmentVariables(details.Target).Trim().ToUpperInvariant() + "\0" + details.Arguments + "\0" +
        Environment.ExpandEnvironmentVariables(details.WorkingDirectory).Trim().ToUpperInvariant();

    internal static bool Matches(string name, string search) => CultureInfo.CurrentCulture.CompareInfo.IndexOf(name, search.Trim(),
        CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    internal static HashSet<string> ExistingKeys(IEnumerable<DockEntry> entries)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries.Where(e => !e.IsSeparator))
        {
            if (entry.ApplicationId is { Length: > 0 } id) keys.Add("app:" + id.ToUpperInvariant());
            var details = ShellShortcut.Read(entry.Path);
            if (details.Editable) keys.Add("launch:" + LaunchKey(details));
            keys.Add("source:" + (entry.SourcePath ?? entry.Path).ToUpperInvariant());
        }
        return keys;
    }

    internal static bool IsAdded(CatalogApplication app, HashSet<string> existing) =>
        (app.ApplicationId is { } id && existing.Contains("app:" + id.ToUpperInvariant())) ||
        existing.Contains("launch:" + app.LaunchKey) ||
        (app.ShortcutPath is { } path && existing.Contains("source:" + path.ToUpperInvariant()));

    private static IEnumerable<string> FindShortcuts(string root, CancellationToken token, Action incomplete)
    {
        if (!Directory.Exists(root)) yield break;
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            string[] files, directories;
            try { files = Directory.GetFiles(directory, "*.lnk"); directories = Directory.GetDirectories(directory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { incomplete(); continue; }
            foreach (var file in files) yield return file;
            foreach (var child in directories)
            {
                try { if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { incomplete(); }
            }
        }
    }

    private static List<ShellApp> ReadShellApps(CancellationToken token)
    {
        object? shell = null, folder = null, items = null;
        var result = new List<ShellApp>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application") ?? throw new InvalidOperationException("Windows Shell indisponible.");
            shell = Activator.CreateInstance(type)!;
            folder = ((dynamic)shell).NameSpace("shell:AppsFolder");
            if (folder is null) throw new InvalidOperationException("AppsFolder indisponible.");
            items = ((dynamic)folder).Items();
            var count = (int)((dynamic)items).Count;
            for (var index = 0; index < count; index++)
            {
                token.ThrowIfCancellationRequested();
                object? item = null;
                try
                {
                    item = ((dynamic)items).Item(index);
                    string id = ((dynamic)item).Path;
                    string name = ((dynamic)item).Name;
                    // The namespace also exposes help files and folders: those are not apps.
                    if (Path.IsPathFullyQualified(id) && !Path.GetExtension(id).Equals(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    string Property(string key) => Convert.ToString(((dynamic)item).ExtendedProperty(key), CultureInfo.InvariantCulture) ?? "";
                    var target = Property("System.Link.TargetParsingPath");
                    if (target.Length > 0 && !Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    ids.Add(id);
                    result.Add(new(name, id, target, Property("System.Link.Arguments"), Property("System.Link.WorkingDirectory")));
                }
                finally { if (item is not null) Marshal.ReleaseComObject(item); }
            }
            RecordPresence(ids);
            return result;
        }
        finally
        {
            if (items is not null) Marshal.ReleaseComObject(items);
            if (folder is not null) Marshal.ReleaseComObject(folder);
            if (shell is not null) Marshal.ReleaseComObject(shell);
        }
    }
}
