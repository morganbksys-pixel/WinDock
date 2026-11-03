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
        return entries.Where(entry => !entry.IsSeparator &&
            ((entry.ApplicationId is { } id && applicationIds.Contains(id)) ||
             (entry.SourcePath?.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase) != true &&
              ResolveTarget(entry.Path) is { } target && running.Contains(target))))
            .Select(entry => entry.Id).ToHashSet();
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

    private static (HashSet<string> Paths, HashSet<string> ApplicationIds) ExecutablePaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var applicationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            using (var handle = OpenProcess(0x1000, false, process.Id)) // PROCESS_QUERY_LIMITED_INFORMATION
            {
                if (handle.IsInvalid) continue;
                var name = new StringBuilder(32768);
                var length = name.Capacity;
                if (QueryFullProcessImageName(handle, 0, name, ref length) && Normalize(name.ToString()) is { } path)
                    paths.Add(path);
                uint appIdLength = 0;
                if (GetApplicationUserModelId(handle, ref appIdLength, null) == 122 && appIdLength is > 0 and < 32768)
                {
                    var appId = new StringBuilder((int)appIdLength);
                    if (GetApplicationUserModelId(handle, ref appIdLength, appId) == 0) applicationIds.Add(appId.ToString());
                }
            }
        }
        return (paths, applicationIds);
    }
}
