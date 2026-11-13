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
    private readonly Func<bool> isOpen;
    private readonly Func<Point, bool> contains;
    private readonly Func<nint, bool> owns;
    private readonly Action close;
    private readonly MouseCallback callback;
    private readonly nint owner;
    private nint hook;
    private nint foreground;

    internal ContextMenuDismissal(ContextMenu menu, nint owner)
        : this(() => menu.IsOpen, point => ContainsMenu(menu, point), handle => OwnsMenu(menu, handle), () => menu.IsOpen = false, owner) { }

    internal ContextMenuDismissal(Window panel, nint owner, Func<Point, bool> extraContains, Func<nint, bool> extraOwns, Action close)
        : this(() => panel.IsVisible, point => ContainsElement(panel, point) || extraContains(point),
            handle => new WindowInteropHelper(panel).Handle == handle || extraOwns(handle), close, owner) { }

    private ContextMenuDismissal(Func<bool> isOpen, Func<Point, bool> contains, Func<nint, bool> owns, Action close, nint owner)
    {
        this.isOpen = isOpen; this.contains = contains; this.owns = owns; this.close = close;
        this.owner = owner;
        foreground = Native.GetForegroundWindow();
        callback = OnMouse;
        hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
        if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    internal static bool ContainsElement(FrameworkElement element, Point screenPoint)
    {
        if (!element.IsVisible || PresentationSource.FromVisual(element) is null) return false;
        return new Rect(element.RenderSize).Contains(element.PointFromScreen(screenPoint));
    }

    private static IEnumerable<FrameworkElement> MenuSurfaces(ItemsControl items)
    {
        yield return items;
        foreach (var item in items.Items.OfType<MenuItem>().Where(i => i.IsSubmenuOpen))
            foreach (var child in MenuSurfaces(item)) yield return child;
        // Child items are hosted in a separate HWND when their parent's submenu is open.
        if (items is MenuItem)
            foreach (var child in items.Items.OfType<FrameworkElement>()) yield return child;
    }

    internal static bool ContainsMenu(ContextMenu menu, Point point) => menu.IsOpen && MenuSurfaces(menu).Any(e => ContainsElement(e, point));
    internal static bool OwnsMenu(ContextMenu menu, nint handle) => menu.IsOpen && MenuSurfaces(menu).Any(e => (PresentationSource.FromVisual(e) as HwndSource)?.Handle == handle);

    internal void Poll() => Poll(Native.GetForegroundWindow(), (Native.GetAsyncKeyState(0x1B) & 0x8000) != 0);

    internal void Poll(nint current, bool escape)
    {
        if ((current != foreground && current != 0 && current != owner && !owns(current)) ||
            escape)
            close();
        foreground = current;
    }

    private nint OnMouse(int code, nint message, nint data)
    {
        if (code >= 0 && isOpen() && message is 0x0201 or 0x0204 or 0x0207 or 0x020B)
        {
            var point = Marshal.PtrToStructure<MouseData>(data);
            HandlePointerDown(new Point(point.X, point.Y));
        }
        return CallNextHookEx(hook, code, message, data);
    }

    internal void HandlePointerDown(Point screenPoint)
    {
        if (isOpen() && !contains(screenPoint))
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(() => { if (isOpen()) close(); }, DispatcherPriority.Input);
    }

    public void Dispose()
    {
        if (hook == 0) return;
        UnhookWindowsHookEx(hook);
        hook = 0;
        GC.KeepAlive(callback);
    }
}
