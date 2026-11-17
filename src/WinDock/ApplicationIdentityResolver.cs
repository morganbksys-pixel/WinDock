using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using WinDock.Core;

namespace WinDock;

internal sealed record ShellApplicationIdentity(string? Executable, bool IsPackaged);

internal sealed record ApplicationIdentity(string? ApplicationId, string? Executable)
{
    internal bool IsExplorer => ApplicationIdentityResolver.IsExplorerExecutable(Executable);

    internal bool Matches(string? executable, string? applicationId) =>
        ApplicationId is { } id && string.Equals(id, applicationId, StringComparison.OrdinalIgnoreCase) ||
        Executable is { } target && RunningApplications.MatchesExecutable(target, executable);
}

/// <summary>Resolves launch entries on an STA worker without changing their shortcuts or launch behavior.</summary>
internal sealed class ApplicationIdentityResolver
{
    internal static ApplicationIdentityResolver Shared { get; } = new();
    internal static string ExplorerExecutable { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
    private readonly object gate = new();
    private readonly Func<string, ShellApplicationIdentity?> readShellApplication;
    private readonly Func<DateTime> now;
    private readonly Dictionary<string, (DateTime ReadAt, ShellApplicationIdentity? Identity)> applications = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTime Modified, string? Target)> shortcuts = new(StringComparer.OrdinalIgnoreCase);

    internal ApplicationIdentityResolver(Func<string, ShellApplicationIdentity?>? readShellApplication = null, Func<DateTime>? now = null)
    {
        this.readShellApplication = readShellApplication ?? ReadShellApplication;
        this.now = now ?? (() => DateTime.UtcNow);
    }

    internal ApplicationIdentity Resolve(DockEntry entry)
    {
        if (entry.IsSeparator || entry.IsGroup) return new(null, null);
        const string prefix = "shell:AppsFolder\\";
        var shellSource = entry.SourcePath?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true;
        var id = entry.ApplicationId ?? (shellSource ? entry.SourcePath![prefix.Length..] : null);
        if (id is not null)
        {
            var application = ResolveShellApplication(id);
            if (application?.IsPackaged == true) return new(id, null);
            if (NormalizeExecutable(application?.Executable) is { } executable) return new(id, executable);
            if (shellSource) return new(id, null);
        }
        return new(id, ResolveTarget(entry.Path));
    }

    internal static bool IsExplorerExecutable(string? path) => path is not null &&
        string.Equals(NormalizeExecutable(path), ExplorerExecutable, StringComparison.OrdinalIgnoreCase);

    private ShellApplicationIdentity? ResolveShellApplication(string id)
    {
        lock (gate)
        {
            var observed = now();
            if (applications.TryGetValue(id, out var cached) && observed - cached.ReadAt < TimeSpan.FromSeconds(30)) return cached.Identity;
            ShellApplicationIdentity? identity = null;
            try { identity = readShellApplication(id); }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException or Win32Exception) { }
            applications[id] = (observed, identity);
            return identity;
        }
    }

    internal string? ResolveTarget(string path)
    {
        try
        {
            if (Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return NormalizeExecutable(path);
            if (!ShellShortcut.IsLink(path) || !File.Exists(path)) return null;
            lock (gate)
            {
                var modified = File.GetLastWriteTimeUtc(path);
                if (shortcuts.TryGetValue(path, out var cached) && cached.Modified == modified) return cached.Target;
                var target = NormalizeExecutable(ShellShortcut.Read(path).Target);
                shortcuts[path] = (modified, target);
                return target;
            }
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    private static string? NormalizeExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            path = Environment.ExpandEnvironmentVariables(path);
            return Path.IsPathFullyQualified(path) && Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFullPath(path) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    private static ShellApplicationIdentity? ReadShellApplication(string id)
    {
        object? shell = null, folder = null, item = null;
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null) return null;
            shell = Activator.CreateInstance(type);
            if (shell is null) return null;
            folder = ((dynamic)shell).NameSpace("shell:AppsFolder");
            if (folder is null) return null;
            item = ((dynamic)folder).ParseName(id);
            if (item is null || !string.Equals((string)((dynamic)item).Path, id, StringComparison.OrdinalIgnoreCase)) return null;
            string? Property(string key) => Convert.ToString(((dynamic)item).ExtendedProperty(key), CultureInfo.InvariantCulture);
            return new(Property("System.Link.TargetParsingPath"), !string.IsNullOrEmpty(Property("System.AppUserModel.PackageFullName")));
        }
        finally
        {
            if (item is not null) Marshal.ReleaseComObject(item);
            if (folder is not null) Marshal.ReleaseComObject(folder);
            if (shell is not null) Marshal.ReleaseComObject(shell);
        }
    }
}
