using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinDock.Core;

namespace WinDock;

internal sealed record Display(nint Handle, string Device, PixelRect Bounds, PixelRect Work, bool Primary, double Scale)
{
    public string Label => $"{Device.Replace(@"\\.\", "")} {(Primary ? "(principal)" : "")} — {Bounds.Width:0} × {Bounds.Height:0}";
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] private struct Size { public int Width, Height; }
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(Size size, uint flags, out nint bitmap);
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly PixelRect Pixels => new(Left, Top, Right - Left, Bottom - Top);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ShellFileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    private delegate bool MonitorCallback(nint monitor, nint dc, nint rect, nint data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern nint SHGetFileInfo(string path, uint attributes, out ShellFileInfo info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint context, ref Guid iid, out IShellItemImageFactory? factory);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);

    public static List<Display> Displays()
    {
        var displays = new List<Display>();
        EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
            if (GetMonitorInfo(monitor, ref info))
            {
                var result = GetDpiForMonitor(monitor, 0, out var dpi, out _);
                displays.Add(new(monitor, info.Device, info.Monitor.Pixels, info.Work.Pixels, (info.Flags & 1) != 0, result == 0 ? dpi / 96.0 : 1));
            }
            return true;
        }, 0);
        return displays;
    }

    public static void MakeNonActivating(nint hwnd) => SetWindowLongPtr(hwnd, -20, GetWindowLongPtr(hwnd, -20) | 0x08000000 | 0x80);
    public static void MakeClickThrough(nint hwnd)
    {
        MakeNonActivating(hwnd);
        SetWindowLongPtr(hwnd, -20, GetWindowLongPtr(hwnd, -20) | 0x20);
    }
    public static void Position(nint hwnd, PixelRect rect) => SetWindowPos(hwnd, new nint(-1),
        (int)Math.Round(rect.X), (int)Math.Round(rect.Y), (int)Math.Round(rect.Width), (int)Math.Round(rect.Height), 0x0010);

    public static bool FullScreen(Display display, nint ownWindow)
    {
        var foreground = GetForegroundWindow();
        if (foreground == 0 || foreground == ownWindow) return false;
        var className = new StringBuilder(128);
        GetClassName(foreground, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        if (!GetWindowRect(foreground, out var rect)) return false;
        var b = display.Bounds;
        return rect.Left <= b.X && rect.Top <= b.Y && rect.Right >= b.Right && rect.Bottom >= b.Bottom;
    }

    public static ImageSource? Icon(string path)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(path, 0, ref iid, out var factory) == 0 && factory is not null)
        {
            nint bitmap = 0;
            try
            {
                if (factory.GetImage(new Size { Width = 96, Height = 96 }, 4, out bitmap) == 0 && bitmap != 0)
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
            }
            finally
            {
                if (bitmap != 0) DeleteObject(bitmap);
                Marshal.ReleaseComObject(factory);
            }
        }
        SHGetFileInfo(path, 0, out var info, (uint)Marshal.SizeOf<ShellFileInfo>(), 0x100);
        if (info.Icon == 0) return null;
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally { DestroyIcon(info.Icon); }
    }
}
