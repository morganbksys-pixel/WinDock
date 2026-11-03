using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace WinDock;

/// <summary>
/// Dismisses menus belonging to a non-activating dock, including clicks in the
/// application that already had focus. The hook observes clicks without consuming them.
/// </summary>
internal sealed class ContextMenuDismissal : IDisposable
{
    private delegate nint MouseCallback(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct MouseData
    {
        public int X, Y;
        public uint MouseDataValue, Flags, Time;
        public nuint ExtraInfo;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, MouseCallback callback, nint module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    private readonly ContextMenu menu;
    private readonly MouseCallback callback;
    private readonly nint owner;
    private nint hook;
    private nint foreground;

    internal ContextMenuDismissal(ContextMenu menu, nint owner)
    {
        this.menu = menu;
        this.owner = owner;
        foreground = Native.GetForegroundWindow();
        callback = OnMouse;
        hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0); // WH_MOUSE_LL, only while open.
        if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    internal void Poll()
    {
        var current = Native.GetForegroundWindow();
        var popup = (PresentationSource.FromVisual(menu) as HwndSource)?.Handle ?? 0;
        if ((current != foreground && current != 0 && current != owner && current != popup) ||
            (Native.GetAsyncKeyState(0x1B) & 0x8000) != 0)
            menu.IsOpen = false;
        foreground = current;
    }

    private nint OnMouse(int code, nint message, nint data)
    {
        if (code >= 0 && menu.IsOpen && message is 0x0201 or 0x0204 or 0x0207 or 0x020B)
        {
            var point = Marshal.PtrToStructure<MouseData>(data);
            HandlePointerDown(new Point(point.X, point.Y));
        }
        return CallNextHookEx(hook, code, message, data);
    }

    internal void HandlePointerDown(Point screenPoint)
    {
        if (!menu.IsOpen || PresentationSource.FromVisual(menu) is null) return;
        var local = menu.PointFromScreen(screenPoint);
        if (!new Rect(menu.RenderSize).Contains(local))
            menu.Dispatcher.BeginInvoke(() => menu.IsOpen = false, DispatcherPriority.Input);
    }

    public void Dispose()
    {
        if (hook == 0) return;
        UnhookWindowsHookEx(hook);
        hook = 0;
        GC.KeepAlive(callback);
    }
}
