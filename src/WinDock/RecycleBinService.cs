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

internal sealed class RecycleBinService : IRecycleBinService
{
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
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    private readonly Dictionary<bool, ImageSource> icons = [];

    public RecycleBinSnapshot Read()
    {
        var query = new QueryInfo { Size = (uint)Marshal.SizeOf<QueryInfo>() };
        bool? empty = SHQueryRecycleBin(null, ref query) >= 0 ? query.Items == 0 : null;
        var key = empty != false;
        if (!icons.TryGetValue(key, out var image))
        {
            var info = new StockInfo { Size = (uint)Marshal.SizeOf<StockInfo>(), Path = "" };
            var result = SHGetStockIconInfo(key ? 31u : 32u, 0x100, ref info);
            try
            {
                if (result >= 0 && info.Icon != 0)
                {
                    image = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    image.Freeze();
                    icons[key] = image;
                }
            }
            finally { if (info.Icon != 0) DestroyIcon(info.Icon); }
        }
        return new(empty, image);
    }

    public void Open() => Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true });

    public void Empty(nint owner)
    {
        var result = SHEmptyRecycleBin(owner, null, 0); // Keep Windows confirmation, progress and errors.
        if (result is unchecked((int)0x800704C7) or unchecked((int)0x80004004)) return;
        Marshal.ThrowExceptionForHR(result);
    }
}
