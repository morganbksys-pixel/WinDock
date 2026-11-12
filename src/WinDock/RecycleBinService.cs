using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinDock;

internal sealed record RecycleBinSnapshot(bool? IsEmpty, ImageSource? Icon);

internal interface IRecycleBinService
{
    RecycleBinSnapshot Read();
    void Open();
    void Empty(nint owner);
}

internal sealed class RecycleBinService(
    Func<bool, BitmapSource?>? highResolutionLoader = null,
    Func<bool, BitmapSource?>? standardLoader = null) : IRecycleBinService
{
    private const uint StockIcon = 0x100, StockSystemIndex = 0x4000;
    private const int JumboImageList = 4;
    [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        // Preserve the native vtable order through GetIcon; later methods are unused.
        [PreserveSig] int Add(nint image, nint mask, out int index);
        [PreserveSig] int ReplaceIcon(int index, nint icon, out int replacedIndex);
        [PreserveSig] int SetOverlayImage(int image, int overlay);
        [PreserveSig] int Replace(int index, nint image, nint mask);
        [PreserveSig] int AddMasked(nint image, uint maskColor, out int index);
        [PreserveSig] int Draw(nint parameters);
        [PreserveSig] int Remove(int index);
        [PreserveSig] int GetIcon(int index, uint flags, out nint icon);
    }
    [StructLayout(LayoutKind.Sequential)] private struct QueryInfo { public uint Size; public long Bytes, Items; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StockInfo
    {
        public uint Size;
        public nint Icon;
        public int SystemIndex, IconIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Path;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHQueryRecycleBin(string? root, ref QueryInfo info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHEmptyRecycleBin(nint owner, string? root, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHGetStockIconInfo(uint id, uint flags, ref StockInfo info);
    [DllImport("shell32.dll")] private static extern int SHGetImageList(int list, ref Guid iid, out IImageList? images);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    private readonly Dictionary<bool, BitmapSource> icons = [];

    public RecycleBinSnapshot Read()
    {
        var query = new QueryInfo { Size = (uint)Marshal.SizeOf<QueryInfo>() };
        bool? empty = SHQueryRecycleBin(null, ref query) >= 0 ? query.Items == 0 : null;
        return new(empty, GetIcon(empty != false));
    }

    /// <summary>Loads and caches either stock icon without querying or modifying recycle bin contents. Call on an STA worker.</summary>
    internal BitmapSource? GetIcon(bool empty)
    {
        if (icons.TryGetValue(empty, out var cached)) return cached;
        var image = (highResolutionLoader ?? LoadHighResolutionIcon)(empty) ?? (standardLoader ?? LoadStandardIcon)(empty);
        if (image is not null) icons[empty] = image;
        return image;
    }

    private static BitmapSource? LoadHighResolutionIcon(bool empty)
    {
        var info = new StockInfo { Size = (uint)Marshal.SizeOf<StockInfo>(), Path = "" };
        if (SHGetStockIconInfo(empty ? 31u : 32u, StockSystemIndex, ref info) < 0) return null;
        IImageList? images = null;
        nint icon = 0;
        try
        {
            var iid = typeof(IImageList).GUID;
            if (SHGetImageList(JumboImageList, ref iid, out images) < 0 || images is null ||
                images.GetIcon(info.SystemIndex, 1, out icon) < 0 || icon == 0) return null; // ILD_TRANSPARENT
            return CopyIcon(icon);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or System.ComponentModel.Win32Exception) { return null; }
        finally
        {
            if (icon != 0) DestroyIcon(icon);
            if (images is not null) Marshal.ReleaseComObject(images);
        }
    }

    internal static BitmapSource? LoadStandardIcon(bool empty)
    {
        var info = new StockInfo { Size = (uint)Marshal.SizeOf<StockInfo>(), Path = "" };
        var result = SHGetStockIconInfo(empty ? 31u : 32u, StockIcon, ref info);
        try { return result >= 0 && info.Icon != 0 ? CopyIcon(info.Icon) : null; }
        finally { if (info.Icon != 0) DestroyIcon(info.Icon); }
    }

    private static BitmapSource CopyIcon(nint icon)
    {
        var image = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        image.Freeze();
        return image;
    }

    public void Open() => Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true });

    public void Empty(nint owner)
    {
        var result = SHEmptyRecycleBin(owner, null, 0); // Keep Windows confirmation, progress and errors.
        if (result is unchecked((int)0x800704C7) or unchecked((int)0x80004004)) return;
        Marshal.ThrowExceptionForHR(result);
    }
}
