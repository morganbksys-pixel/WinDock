using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace WinDock;

internal interface IExplorerRestarter
{
    Task RestartAsync(CancellationToken token);
}

internal sealed record ExplorerShell(int ProcessId, int SessionId, string? Executable, long StartedAt);

/// <summary>Restarts only the current desktop's verified Windows Explorer shell, never its child processes.</summary>
internal sealed class ExplorerRestarter : IExplorerRestarter
{
    private readonly Func<ExplorerShell?> inspect;
    private readonly Func<ExplorerShell, Task> stop;
    private readonly Action start;
    private readonly Func<TimeSpan, Task> delay;
    private readonly int sessionId;
    private int restarting;

    internal ExplorerRestarter() : this(Inspect, StopAsync, Start, time => Task.Delay(time), CurrentSession()) { }

    internal ExplorerRestarter(Func<ExplorerShell?> inspect, Func<ExplorerShell, Task> stop, Action start,
        Func<TimeSpan, Task> delay, int sessionId)
    {
        this.inspect = inspect; this.stop = stop; this.start = start; this.delay = delay; this.sessionId = sessionId;
    }

    public async Task RestartAsync(CancellationToken token)
    {
        if (Interlocked.CompareExchange(ref restarting, 1, 0) != 0) return;
        try
        {
            token.ThrowIfCancellationRequested();
            var shell = inspect();
            if (shell is not null)
            {
                Validate(shell);
                token.ThrowIfCancellationRequested();
                await stop(shell).ConfigureAwait(false);
                // Once stopped, finish recovery even if the dock is closing.
                if (await WaitForShellAsync(20).ConfigureAwait(false)) return;
            }
            if (shell is null) token.ThrowIfCancellationRequested();
            // Recheck immediately before launching: Windows may already have recovered its shell.
            if (ReadValidShell() is not null) return;
            start();
            if (!await WaitForShellAsync(100).ConfigureAwait(false))
                throw new TimeoutException("Le bureau Windows n’a pas réapparu après le redémarrage de l’Explorateur.");
        }
        finally { Volatile.Write(ref restarting, 0); }
    }

    private ExplorerShell? ReadValidShell()
    {
        var shell = inspect();
        if (shell is not null) Validate(shell);
        return shell;
    }

    private void Validate(ExplorerShell shell)
    {
        if (shell.SessionId != sessionId || !ApplicationIdentityResolver.IsExplorerExecutable(shell.Executable))
            throw new InvalidOperationException("Le processus du bureau n’est pas l’Explorateur Windows de cette session.");
    }

    private async Task<bool> WaitForShellAsync(int attempts)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (ReadValidShell() is not null) return true;
            await delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
        }
        return ReadValidShell() is not null;
    }

    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out int processId);

    private static int CurrentSession()
    {
        using var process = Process.GetCurrentProcess();
        return process.SessionId;
    }

    private static ExplorerShell? Inspect()
    {
        var window = GetShellWindow();
        if (window == 0) return null;
        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0) throw new Win32Exception("Impossible d’identifier le processus du bureau Windows.");
        try
        {
            using var process = Process.GetProcessById(processId);
            return new(processId, process.SessionId, RunningApplications.ReadIdentity(processId).Path, process.StartTime.ToUniversalTime().Ticks);
        }
        catch (ArgumentException) { return null; } // The shell exited between the window and process queries.
    }

    private static async Task StopAsync(ExplorerShell expected)
    {
        using var process = Process.GetProcessById(expected.ProcessId);
        _ = process.Handle; // Keep the validated process object alive across the final checks and Kill.
        if (Inspect() != expected || process.SessionId != expected.SessionId || process.StartTime.ToUniversalTime().Ticks != expected.StartedAt)
            throw new InvalidOperationException("Le processus de l’Explorateur a changé. Réessayez le redémarrage.");
        process.Kill(entireProcessTree: false);
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }

    private static void Start()
    {
        if (!File.Exists(ApplicationIdentityResolver.ExplorerExecutable)) throw new FileNotFoundException("L’Explorateur Windows est introuvable.");
        using var process = Process.Start(new ProcessStartInfo(ApplicationIdentityResolver.ExplorerExecutable) { UseShellExecute = false });
        if (process is null) throw new InvalidOperationException("Impossible de relancer l’Explorateur Windows.");
    }
}
