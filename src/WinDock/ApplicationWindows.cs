using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using WinDock.Core;

namespace WinDock;

internal sealed record ApplicationWindow(nint Handle, int ProcessId, string? Path, string? ApplicationId, string Title = "");

internal enum WindowActivationResult { Activated, Unavailable, Refused }

internal interface IApplicationWindows
{
    Task<IReadOnlyList<ApplicationWindow>> ListAsync(DockEntry entry, CancellationToken token);
    Task<WindowActivationResult> ActivateAsync(DockEntry entry, ApplicationWindow window, CancellationToken token);
    Task<int> CountAsync(DockEntry entry, CancellationToken token);
    Task<int> CloseAsync(DockEntry entry, CancellationToken token);
}

/// <summary>Lists, activates and requests normal closure of matching windows; never terminates a process.</summary>
internal sealed class ApplicationWindows : IApplicationWindows
{
    private readonly Func<IReadOnlyList<ApplicationWindow>> enumerate;
    private readonly Func<nint, ApplicationWindow?> inspect;
    private readonly Func<nint, int> postClose;
    private readonly Func<nint, bool> activate;

    internal ApplicationWindows() : this(Enumerate, Inspect, PostClose) { }

    internal ApplicationWindows(Func<IReadOnlyList<ApplicationWindow>> enumerate,
        Func<nint, ApplicationWindow?> inspect, Func<nint, int> postClose, Func<nint, bool>? activate = null)
    {
        this.enumerate = enumerate;
        this.inspect = inspect;
        this.postClose = postClose;
        this.activate = activate ?? Activate;
    }

    private static Func<ApplicationWindow, bool> Matcher(DockEntry entry)
    {
        var target = new RunningApplications().ResolveTarget(entry.Path);
        return window => !entry.IsSeparator && !entry.IsGroup && (
            entry.ApplicationId is { } id && string.Equals(id, window.ApplicationId, StringComparison.OrdinalIgnoreCase) ||
            entry.SourcePath?.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase) != true &&
            target is not null && RunningApplications.MatchesExecutable(target, window.Path));
    }

    public Task<IReadOnlyList<ApplicationWindow>> ListAsync(DockEntry entry, CancellationToken token) =>
        StaWorker.RunAsync<IReadOnlyList<ApplicationWindow>>(_ =>
        {
            if (entry.IsSeparator || entry.IsGroup) return [];
            var matches = Matcher(entry);
            token.ThrowIfCancellationRequested();
            return enumerate().Where(matches).DistinctBy(window => window.Handle).ToArray();
        }, token);

    public async Task<int> CountAsync(DockEntry entry, CancellationToken token) =>
        (await ListAsync(entry, token).ConfigureAwait(false)).Count;

    public Task<WindowActivationResult> ActivateAsync(DockEntry entry, ApplicationWindow window, CancellationToken token) =>
        StaWorker.RunAsync(_ =>
        {
            var matches = Matcher(entry);
            var current = inspect(window.Handle);
            if (current is null || current.ProcessId != window.ProcessId || !matches(current) ||
                !string.Equals(current.Path, window.Path, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(current.ApplicationId, window.ApplicationId, StringComparison.OrdinalIgnoreCase))
                return WindowActivationResult.Unavailable;
            token.ThrowIfCancellationRequested();
            return activate(window.Handle) ? WindowActivationResult.Activated : WindowActivationResult.Refused;
        }, token);

    public Task<int> CloseAsync(DockEntry entry, CancellationToken token) => StaWorker.RunAsync(_ =>
    {
        if (entry.IsSeparator || entry.IsGroup) return 0;
        var matches = Matcher(entry);
        var windows = enumerate().Where(matches).DistinctBy(window => window.Handle).ToArray();
        var failures = 0;
        foreach (var window in windows)
        {
            token.ThrowIfCancellationRequested();
            var current = inspect(window.Handle);
            if (current is null || current.ProcessId != window.ProcessId || !matches(current)) continue;
            var error = postClose(window.Handle);
            if (error != 0 && error != 1400) failures++;
        }
        return failures;
    }, token);

    private static bool Activate(nint window)
    {
        if (IsIconic(window)) ShowWindowAsync(window, 9); // SW_RESTORE
        return SetForegroundWindow(window);
    }

    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder title, int size);

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
        var title = new StringBuilder(1024);
        GetWindowText(window, title, title.Capacity);
        return new(window, processId, identity.Path, identity.ApplicationId, title.ToString());
    }
}
