using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using WinDock.Core;

namespace WinDock;

/// <summary>Matches launcher targets to running executable paths without inspecting process contents.</summary>
internal sealed class RunningApplications
{
    private readonly Dictionary<string, (DateTime Modified, string? Target)> shortcuts = new(StringComparer.OrdinalIgnoreCase);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetApplicationUserModelId(SafeProcessHandle process, ref uint length, StringBuilder? appId);

    internal HashSet<Guid> Snapshot(IReadOnlyList<DockEntry> entries)
    {
        var (running, applicationIds) = ExecutablePaths();
        return Snapshot(entries, running, applicationIds);
    }

    internal HashSet<Guid> Snapshot(IReadOnlyList<DockEntry> entries, HashSet<string> running, HashSet<string> applicationIds)
    {
        return entries.Where(entry => !entry.IsSeparator &&
            ((entry.ApplicationId is { } id && applicationIds.Contains(id)) ||
             (entry.SourcePath?.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase) != true &&
              ResolveTarget(entry.Path) is { } target && running.Any(path => MatchesExecutable(target, path)))))
            .Select(entry => entry.Id).ToHashSet();
    }

    /// <summary>Recognizes a Squirrel launcher and its versioned executable within the same installation.</summary>
    internal static bool MatchesExecutable(string target, string? processPath)
    {
        if (processPath is null) return false;
        if (string.Equals(target, processPath, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.Equals(Path.GetFileName(target), Path.GetFileName(processPath), StringComparison.OrdinalIgnoreCase)) return false;
        var root = Path.GetDirectoryName(target);
        var versionDirectory = Path.GetDirectoryName(processPath);
        if (root is null || versionDirectory is null ||
            !string.Equals(root, Path.GetDirectoryName(versionDirectory), StringComparison.OrdinalIgnoreCase)) return false;
        var versionName = Path.GetFileName(versionDirectory);
        return versionName.StartsWith("app-", StringComparison.OrdinalIgnoreCase) &&
            Version.TryParse(versionName[4..], out _) && File.Exists(Path.Combine(root, "Update.exe"));
    }

    internal string? ResolveTarget(string path)
    {
        if (Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return Normalize(path);
        if (!Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
        var modified = File.GetLastWriteTimeUtc(path);
        if (shortcuts.TryGetValue(path, out var cached) && cached.Modified == modified) return cached.Target;
        object? shell = null;
        object? shortcut = null;
        string? target = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return null;
            shell = Activator.CreateInstance(type);
            if (shell is null) return null;
            shortcut = ((dynamic)shell).CreateShortcut(path);
            string candidate = ((dynamic)shortcut).TargetPath;
            if (Path.GetExtension(candidate).Equals(".exe", StringComparison.OrdinalIgnoreCase)) target = Normalize(candidate);
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException) { }
        finally
        {
            if (shortcut is not null) Marshal.ReleaseComObject(shortcut);
            if (shell is not null) Marshal.ReleaseComObject(shell);
        }
        shortcuts[path] = (modified, target);
        return target;
    }

    private static string? Normalize(string path)
    {
        try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    internal static (string? Path, string? ApplicationId) ReadIdentity(int processId)
    {
        using var handle = OpenProcess(0x1000, false, processId);
        if (handle.IsInvalid) return (null, null);
        var name = new StringBuilder(32768);
        var length = name.Capacity;
        var path = QueryFullProcessImageName(handle, 0, name, ref length) ? Normalize(name.ToString()) : null;
        uint appIdLength = 0;
        string? applicationId = null;
        if (GetApplicationUserModelId(handle, ref appIdLength, null) == 122 && appIdLength is > 0 and < 32768)
        {
            var appId = new StringBuilder((int)appIdLength);
            if (GetApplicationUserModelId(handle, ref appIdLength, appId) == 0) applicationId = appId.ToString();
        }
        return (path, applicationId);
    }

    private static (HashSet<string> Paths, HashSet<string> ApplicationIds) ExecutablePaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var applicationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var identity = ReadIdentity(process.Id);
                if (identity.Path is { } path) paths.Add(path);
                if (identity.ApplicationId is { } appId) applicationIds.Add(appId);
            }
        }
        return (paths, applicationIds);
    }
}
