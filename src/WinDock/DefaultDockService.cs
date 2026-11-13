using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using WinDock.Core;

namespace WinDock;

internal sealed record DefaultDockApplication(string Name, string? Executable = null, CatalogApplication? Application = null, string? IconPath = null, int IconIndex = 0);
internal sealed record DefaultDockSources(string? Explorer, DefaultDockApplication? Browser, DefaultDockApplication? Mail,
    string? Home, string? Downloads, string? Documents);
internal sealed record ProtocolAssociation(string? ApplicationId, string? Executable, string? Name, string? IconResource = null);

internal interface IDefaultDockEnvironment
{
    DefaultDockSources Resolve(CancellationToken token);
}

internal interface IDefaultDockService
{
    Task<PreparedDefaultDock> PrepareAsync(CancellationToken token);
}

/// <summary>Builds a disposable selection on a Shell STA, independently of the live dock.</summary>
internal sealed class DefaultDockService(SettingsStore store, IDefaultDockEnvironment? environment = null) : IDefaultDockService
{
    private readonly ManagedShortcuts shortcuts = new(store);
    private readonly DefaultApplicationIconCache iconCache = new(store.DataDirectory);
    private readonly IDefaultDockEnvironment environment = environment ?? new WindowsDefaultDockEnvironment();

    public Task<PreparedDefaultDock> PrepareAsync(CancellationToken token) => StaWorker.RunAsync(ct =>
    {
        var staged = new List<DockEntry>();
        try
        {
            var sources = environment.Resolve(ct);
            var apps = new List<DockEntry>(); var folders = new List<DockEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Application(DefaultDockApplication? source)
            {
                ct.ThrowIfCancellationRequested();
                if (source is null) return;
                var keys = new List<string>();
                if (source.Application?.ApplicationId is { Length: > 0 } id) keys.Add("app:" + id);
                var parts = source.Application?.LaunchKey.Split('\0');
                var executable = source.Executable ?? (parts is { Length: >= 2 } && parts[1].Length == 0 && Path.IsPathFullyQualified(parts[0]) ? parts[0] : null);
                if (executable is not null) keys.Add("exe:" + Normalize(executable));
                if (keys.Count == 0) keys.Add("launch:" + source.Application?.LaunchKey);
                if (keys.Any(seen.Contains)) return;
                DockEntry? entry = null;
                try
                {
                    if (source.Application is { } app)
                    {
                        try { entry = shortcuts.ImportApplication(app with { Name = source.Name }); }
                        catch (Exception ex) when (ex is COMException or FileNotFoundException or DirectoryNotFoundException)
                        {
                            if (source.Executable is not { } fallback || !File.Exists(fallback)) return;
                            entry = shortcuts.CreateApplication(fallback, source.Name);
                        }
                    }
                    else if (source.Executable is { } path && File.Exists(path)) entry = shortcuts.CreateApplication(path, source.Name);
                    else return;
                    staged.Add(entry);
                    if (source.IconPath is { } iconPath && File.Exists(iconPath))
                    {
                        var png = Path.GetExtension(iconPath).Equals(".png", StringComparison.OrdinalIgnoreCase);
                        var resource = png ? iconCache.FromPng(iconPath, ct) : iconPath;
                        ShellShortcut.SetIcon(entry.Path, resource, png ? 0 : source.IconIndex);
                    }
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or COMException)
                {
                    if (entry is not null) { shortcuts.Discard(entry); staged.Remove(entry); }
                    return;
                }
                foreach (var key in keys) seen.Add(key);
                apps.Add(entry);
            }
            Application(sources.Explorer is { } explorer ? new("Explorateur", explorer) : null);
            Application(sources.Browser);
            Application(sources.Mail);
            foreach (var (name, path) in new[] { ("Dossier personnel", sources.Home), ("Téléchargements", sources.Downloads), ("Documents", sources.Documents) })
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) continue;
                var fullPath = Normalize(path);
                if (seen.Add("folder:" + fullPath)) folders.Add(new(Guid.NewGuid(), fullPath, DisplayName: name));
            }
            var entries = new List<DockEntry>(apps);
            if (folders.Count > 0)
            {
                if (entries.Count > 0) entries.Add(new(Guid.NewGuid(), "", true));
                entries.AddRange(folders);
            }
            if (entries.Count > 0) entries.Add(new(Guid.NewGuid(), "", true)); // The recycle bin follows this section.
            ct.ThrowIfCancellationRequested();
            return new PreparedDefaultDock(shortcuts, entries);
        }
        catch { foreach (var entry in staged) shortcuts.Discard(entry); throw; }
    }, token);

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)));
}

/// <summary>Owns staged copies until a single successful commit; disposing a canceled selection cleans only those copies.</summary>
internal sealed class PreparedDefaultDock(ManagedShortcuts shortcuts, IReadOnlyList<DockEntry> entries) : IDisposable
{
    private bool applied, disposed;
    internal IReadOnlyList<DockEntry> Entries { get; } = entries.ToArray();
    internal void Apply(DockSettings settings)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (applied) throw new InvalidOperationException("Cette sélection a déjà été enregistrée.");
        var oldRecycleBin = settings.ShowRecycleBin;
        settings.ShowRecycleBin = true;
        try { shortcuts.Commit(settings, Entries.ToList()); applied = true; }
        catch { settings.ShowRecycleBin = oldRecycleBin; throw; }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (!applied) foreach (var entry in Entries) shortcuts.Discard(entry);
    }
}

internal static class DefaultDockInitializer
{
    internal static async Task InitializeAsync(DockSettings settings, SettingsLoadStatus status, IDefaultDockService service, CancellationToken token)
    {
        if (status != SettingsLoadStatus.Missing) return;
        using var prepared = await service.PrepareAsync(token);
        token.ThrowIfCancellationRequested();
        prepared.Apply(settings);
    }
}

/// <summary>Read-only Windows discovery. No association, known folder or startup registration is changed.</summary>
internal sealed class WindowsDefaultDockEnvironment : IDefaultDockEnvironment
{
    private static readonly Guid Profile = new("5E6C858F-0E22-4760-9AFE-EA3317B67173");
    private static readonly Guid Downloads = new("374DE290-123F-4565-9164-39C4925E467B");
    private static readonly Guid Documents = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7");
    private readonly Func<string, ProtocolAssociation> association;
    private readonly Func<IReadOnlyList<CatalogApplication>> cachedApplications;
    private readonly Func<Guid, string?> knownFolder;
    private readonly string windowsDirectory;
    private readonly Func<string, string?> indirectIcon;

    internal WindowsDefaultDockEnvironment(Func<string, ProtocolAssociation>? association = null,
        Func<IReadOnlyList<CatalogApplication>>? cachedApplications = null, Func<Guid, string?>? knownFolder = null,
        string? windowsDirectory = null, Func<string, string?>? indirectIcon = null)
    {
        this.association = association ?? ReadAssociation;
        // Only an already available in-memory snapshot is consulted. No scan, disk load or icon decoding.
        this.cachedApplications = cachedApplications ?? (() => CatalogCache.Shared.Current?.Applications ?? []);
        this.knownFolder = knownFolder ?? KnownFolder;
        this.windowsDirectory = windowsDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        this.indirectIcon = indirectIcon ?? ResolveIndirectIcon;
    }

    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(in Guid folder, uint flags, nint token, out nint path);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int AssocQueryString(uint flags, uint kind, string association, string? extra, StringBuilder? output, ref uint length);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int SHLoadIndirectString(string source, StringBuilder output, uint size, nint reserved);

    public DefaultDockSources Resolve(CancellationToken token)
    {
        var applications = cachedApplications();
        token.ThrowIfCancellationRequested();
        DefaultDockApplication? Application(string protocol)
        {
            var value = association(protocol);
            if (value.IconResource?.StartsWith("@{", StringComparison.Ordinal) == true)
                value = value with { IconResource = indirectIcon(value.IconResource) };
            return SelectApplication(value, applications, File.Exists);
        }
        var browser = Application("https");
        token.ThrowIfCancellationRequested();
        var mail = Application("mailto");
        token.ThrowIfCancellationRequested();
        var explorer = Path.Combine(windowsDirectory, "explorer.exe");
        string? Folder(Guid id) { token.ThrowIfCancellationRequested(); return knownFolder(id); }
        return new(File.Exists(explorer) ? explorer : null, browser, mail, Folder(Profile), Folder(Downloads), Folder(Documents));
    }

    internal static DefaultDockApplication? SelectApplication(ProtocolAssociation association,
        IReadOnlyList<CatalogApplication> applications, Func<string, bool> fileExists)
    {
        static string? GenericTarget(CatalogApplication? app)
        {
            var parts = app?.LaunchKey.Split('\0');
            return parts is { Length: >= 2 } && parts[1].Length == 0 ? parts[0] : null;
        }
        string? ValidExecutable(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (!Path.IsPathFullyQualified(path) || !Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) || !fileExists(path)) return null;
            return Path.GetFileName(path).ToLowerInvariant() is "rundll32.exe" or "applicationframehost.exe" or "explorer.exe" or "openwith.exe" or "msedge_proxy.exe" or "chrome_proxy.exe" ? null : path;
        }
        var executable = ValidExecutable(association.Executable);
        var app = !string.IsNullOrWhiteSpace(association.ApplicationId)
            ? applications.FirstOrDefault(a => string.Equals(a.ApplicationId, association.ApplicationId, StringComparison.OrdinalIgnoreCase))
            : executable is not null ? applications.FirstOrDefault(a => string.Equals(GenericTarget(a), executable, StringComparison.OrdinalIgnoreCase)) : null;
        executable ??= ValidExecutable(GenericTarget(app));
        var id = string.IsNullOrWhiteSpace(association.ApplicationId) ? app?.ApplicationId : association.ApplicationId;
        if (string.IsNullOrWhiteSpace(id) && executable is null) return null;
        var name = !string.IsNullOrWhiteSpace(association.Name) ? association.Name : app?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            if (executable is null) return null;
            name = Path.GetFileNameWithoutExtension(executable);
        }
        // Never copy a registered URL/recipient/profile command line into a default launcher.
        CatalogApplication? launch = null;
        if (!string.IsNullOrWhiteSpace(id))
            launch = new(name, id, null, executable is null ? "shell:" + id : ApplicationCatalog.LaunchKey(new(executable, "", "", true)));
        else if (app is not null && GenericTarget(app) is not null)
            launch = app with { Name = name };
        var associatedIcon = ParseIconResource(association.IconResource, fileExists);
        var resource = associatedIcon is { } image && Path.GetExtension(image.Item1).Equals(".png", StringComparison.OrdinalIgnoreCase)
            ? associatedIcon : executable is not null ? (executable, 0) : associatedIcon;
        return new(name, executable, launch, resource?.Item1, resource?.Item2 ?? 0);
    }

    internal static (string, int)? ParseIconResource(string? reference, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference.StartsWith("@{", StringComparison.Ordinal)) return null;
        var path = reference.Trim().TrimStart('@'); var index = 0;
        var comma = path.LastIndexOf(',');
        if (comma >= 0 && int.TryParse(path[(comma + 1)..].Trim(), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed)) { index = parsed; path = path[..comma]; }
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return Path.IsPathFullyQualified(path) && fileExists(path) ? (path, index) : null;
    }

    private static string? ResolveIndirectIcon(string reference)
    {
        var buffer = new StringBuilder(32768);
        return SHLoadIndirectString(reference, buffer, (uint)buffer.Capacity, 0) == 0 ? buffer.ToString() : null;
    }

    private static ProtocolAssociation ReadAssociation(string protocol) => new(
        AssociationString(protocol, 21), AssociationString(protocol, 2), AssociationString(protocol, 4), AssociationString(protocol, 15));

    private static string? AssociationString(string protocol, uint kind)
    {
        const uint isProtocol = 0x1000; // ASSOCF_IS_PROTOCOL
        uint length = 0;
        AssocQueryString(isProtocol, kind, protocol, "open", null, ref length);
        if (length is < 2 or > 32768) return null;
        var buffer = new StringBuilder((int)length);
        return AssocQueryString(isProtocol, kind, protocol, "open", buffer, ref length) == 0 ? buffer.ToString() : null;
    }

    private static string? KnownFolder(Guid id)
    {
        nint path = 0;
        try { return SHGetKnownFolderPath(in id, 0, 0, out path) >= 0 && path != 0 ? Marshal.PtrToStringUni(path) : null; }
        finally { if (path != 0) Marshal.FreeCoTaskMem(path); }
    }
}
