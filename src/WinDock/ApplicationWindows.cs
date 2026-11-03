using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using WinDock.Core;

namespace WinDock;

internal sealed record ApplicationWindow(nint Handle, int ProcessId, string? Path, string? ApplicationId);

internal interface IApplicationWindows
{
    Task<int> CountAsync(DockEntry entry, CancellationToken token);
    Task<int> CloseAsync(DockEntry entry, CancellationToken token);
}

/// <summary>Requests normal window closure; never terminates a process.</summary>
internal sealed class ApplicationWindows : IApplicationWindows
{
    private readonly Func<IReadOnlyList<ApplicationWindow>> enumerate;
    private readonly Func<nint, ApplicationWindow?> inspect;
    private readonly Func<nint, int> postClose;

    internal ApplicationWindows() : this(Enumerate, Inspect, PostClose) { }

    internal ApplicationWindows(Func<IReadOnlyList<ApplicationWindow>> enumerate,
        Func<nint, ApplicationWindow?> inspect, Func<nint, int> postClose)
    {
        this.enumerate = enumerate;
        this.inspect = inspect;
        this.postClose = postClose;
    }

    public Task<int> CountAsync(DockEntry entry, CancellationToken token) => RunAsync(entry, false, token);
    public Task<int> CloseAsync(DockEntry entry, CancellationToken token) => RunAsync(entry, true, token);

    private Task<int> RunAsync(DockEntry entry, bool close, CancellationToken token) => StaWorker.RunAsync(_ =>
    {
        if (entry.IsSeparator) return 0;
        // Each operation owns its resolver and COM apartment, so no cache is shared across workers.
        var target = new RunningApplications().ResolveTarget(entry.Path);
        bool Matches(ApplicationWindow window) =>
            entry.ApplicationId is { } id && string.Equals(id, window.ApplicationId, StringComparison.OrdinalIgnoreCase) ||
            entry.SourcePath?.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase) != true &&
            target is not null && RunningApplications.MatchesExecutable(target, window.Path);
        var windows = enumerate().Where(Matches).DistinctBy(window => window.Handle).ToArray();
        if (!close) return windows.Length;
        var failures = 0;
        foreach (var window in windows)
        {
            token.ThrowIfCancellationRequested();
            var current = inspect(window.Handle);
            if (current is null || current.ProcessId != window.ProcessId || !Matches(current)) continue;
            var error = postClose(window.Handle);
            if (error != 0 && error != 1400) failures++; // ERROR_INVALID_WINDOW_HANDLE: already closed.
        }
        return failures;
    }, token);

    private delegate bool WindowCallback(nint window, nint data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, nint data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, WindowCallback callback, nint data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out int processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    internal static int PostClose(nint window) => PostMessage(window, 0x0010, 0, 0) ? 0 : Marshal.GetLastWin32Error();

    private static IReadOnlyList<ApplicationWindow> Enumerate()
    {
        var result = new List<ApplicationWindow>();
        if (!EnumWindows((window, _) =>
        {
            if (Inspect(window) is { } match) result.Add(match);
            return true;
        }, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return result;
    }

    private static ApplicationWindow? Inspect(nint window)
    {
        if (!IsWindowVisible(window) || GetWindow(window, 4) != 0) return null; // GW_OWNER
        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0 || processId == Environment.ProcessId) return null;
        var name = new StringBuilder(256);
        GetClassName(window, name, name.Capacity);
        if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return null;
        var identity = RunningApplications.ReadIdentity(processId);
        if (string.Equals(Path.GetFileName(identity.Path), "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
        {
            // A frame host can contain several applications. Resolve only this frame's children.
            var children = new List<(string? Path, string? ApplicationId)>();
            EnumChildWindows(window, (child, _) =>
            {
                GetWindowThreadProcessId(child, out var childProcess);
                if (childProcess != processId && childProcess != 0)
                {
                    var childIdentity = RunningApplications.ReadIdentity(childProcess);
                    if (childIdentity.ApplicationId is not null) children.Add(childIdentity);
                }
                return true;
            }, 0);
            var identities = children.Distinct().ToArray();
            if (identities.Length != 1) return null;
            identity = identities[0];
        }
        return new(window, processId, identity.Path, identity.ApplicationId);
    }
}
