using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

internal static class IsolatedDesktop
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved, Desktop, Title;
        public int X, Y, Width, Height, XChars, YChars, Fill, Flags;
        public short Show, ReservedSize;
        public nint ReservedBytes, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public nint Process, Thread; public uint ProcessId, ThreadId; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateDesktop(string name, nint device, nint mode, uint flags, uint access, nint security);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll")] private static extern nint GetThreadDesktop(uint thread);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetUserObjectInformation(nint obj, int index, StringBuilder info, uint length, out uint required);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcess(string application, StringBuilder command, nint processSecurity, nint threadSecurity, bool inherit, uint flags, nint environment, string directory, ref StartupInfo startup, out ProcessInformation process);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool GetExitCodeProcess(nint process, out uint exitCode);
    [DllImport("kernel32.dll")] private static extern bool TerminateProcess(nint process, uint exitCode);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);

    private static string Name(nint desktop)
    {
        if (desktop == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var result = new StringBuilder(256);
        if (!GetUserObjectInformation(desktop, 2, result, 512, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return result.ToString();
    }

    internal static string InputName()
    {
        var desktop = OpenInputDesktop(0, false, 0x0001);
        try { return Name(desktop); }
        finally { if (desktop != 0) CloseDesktop(desktop); }
    }

    internal static void VerifyWorker(string expected)
    {
        if (!expected.StartsWith("WinDock-tests-", StringComparison.Ordinal) || Name(GetThreadDesktop(GetCurrentThreadId())) != expected || InputName() == expected)
            throw new InvalidOperationException("Isolation absente : aucune fenêtre de test ne sera créée.");
    }

    internal static void VerifyWindow(nint window, string expected)
    {
        if (Name(GetThreadDesktop(GetWindowThreadProcessId(window, out _))) != expected)
            throw new InvalidOperationException("La fenêtre ne se trouve pas sur le bureau isolé.");
        VerifyWorker(expected);
    }

    // No SwitchDesktop import or switch-desktop access: this desktop is never made visible.
    // https://learn.microsoft.com/windows/win32/api/processthreadsapi/ns-processthreadsapi-startupinfow
    internal static int Run(string outputDirectory)
    {
        var output = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);
        var inputName = InputName();
        var name = "WinDock-tests-" + Guid.NewGuid().ToString("N");
        var desktop = CreateDesktop(name, 0, 0, 0, 0x00CF, 0);
        if (desktop == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Impossible de créer le bureau isolé ; aucun repli sur le bureau utilisateur.");
        var child = new ProcessInformation();
        var complete = false;
        try
        {
            var executable = Path.Combine(AppContext.BaseDirectory, "WinDock.Tests.exe");
            var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Desktop = name };
            var command = new StringBuilder($"\"{executable}\" --isolated-worker \"{name}\" \"{output}\"");
            if (!CreateProcess(executable, command, 0, 0, false, 0x08000000, 0, Environment.CurrentDirectory, ref startup, out child))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            Console.WriteLine($"Isolated test desktop: {name}. Input desktop stays {inputName}.");
            var watch = Stopwatch.StartNew();
            while (true)
            {
                var state = WaitForSingleObject(child.Process, 1000);
                if (state == 0) break;
                if (state != 0x00000102) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (watch.ElapsedMilliseconds > 120000) throw new TimeoutException("Le processus isolé a dépassé deux minutes.");
            }
            complete = true;
            if (!GetExitCodeProcess(child.Process, out var code)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (InputName() != inputName) throw new InvalidOperationException("Le bureau d’entrée a changé pendant le test.");
            var log = Path.Combine(output, "isolated-tests.log");
            if (File.Exists(log)) Console.Write(File.ReadAllText(log));
            return checked((int)code);
        }
        finally
        {
            if (!complete && child.Process != 0) { TerminateProcess(child.Process, 1); WaitForSingleObject(child.Process, 5000); }
            if (child.Thread != 0) CloseHandle(child.Thread);
            if (child.Process != 0) CloseHandle(child.Process);
            CloseDesktop(desktop);
        }
    }
}
