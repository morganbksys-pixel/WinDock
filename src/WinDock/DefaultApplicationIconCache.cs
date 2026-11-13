using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinDock;

/// <summary>Converts packaged app PNG resources to reusable ICO files understood by Shell links.</summary>
internal sealed class DefaultApplicationIconCache(string dataDirectory)
{
    internal string FromPng(string source, CancellationToken token)
    {
        var candidates = new List<string> { source };
        var stem = Path.GetFileNameWithoutExtension(source);
        var qualifier = stem.IndexOf(".scale-", StringComparison.OrdinalIgnoreCase);
        if (qualifier < 0) qualifier = stem.IndexOf(".targetsize-", StringComparison.OrdinalIgnoreCase);
        if (qualifier >= 0) stem = stem[..qualifier];
        try { candidates.AddRange(Directory.EnumerateFiles(Path.GetDirectoryName(source)!, stem + ".*.png")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        BitmapFrame? best = null; var bestPath = source; var bestPreference = -1;
        static int Preference(string path) => path.Contains("_altform-unplated", StringComparison.OrdinalIgnoreCase) ? 2 :
            path.Contains("light", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var frame = BitmapDecoder.Create(new Uri(candidate), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                if (frame.PixelWidth > 1024 || frame.PixelHeight > 1024) continue;
                var size = Math.Min(frame.PixelWidth, frame.PixelHeight);
                var currentSize = best is null ? 0 : Math.Min(best.PixelWidth, best.PixelHeight);
                if (best is null || size > currentSize || size == currentSize && Preference(candidate) > bestPreference)
                { best = frame; bestPath = candidate; bestPreference = Preference(candidate); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileFormatException or NotSupportedException) { }
        }
        if (best is null) throw new IOException("L’icône de l’application n’a pas pu être lue.");
        best.Freeze();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bestPath + "|" + File.GetLastWriteTimeUtc(bestPath).Ticks)));
        var directory = Path.Combine(dataDirectory, "IconCache");
        var destination = Path.Combine(directory, key + ".ico");
        if (File.Exists(destination)) return destination;
        var sizes = new[] { 32, 48, 64, 96, 128, 256 };
        var frames = new List<byte[]>();
        foreach (var size in sizes)
        {
            token.ThrowIfCancellationRequested();
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                var factor = Math.Min((double)size / best.PixelWidth, (double)size / best.PixelHeight);
                var width = best.PixelWidth * factor; var height = best.PixelHeight * factor;
                context.DrawImage(best, new Rect((size - width) / 2, (size - height) / 2, width, height));
            }
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(drawing); bitmap.Freeze();
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var bytes = new MemoryStream(); encoder.Save(bytes); frames.Add(bytes.ToArray());
        }
        Directory.CreateDirectory(directory);
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = File.Create(temporary))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
                uint offset = (uint)(6 + 16 * sizes.Length);
                for (var i = 0; i < sizes.Length; i++)
                {
                    writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                    writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                    writer.Write((uint)frames[i].Length); writer.Write(offset); offset += (uint)frames[i].Length;
                }
                foreach (var frame in frames) writer.Write(frame);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return destination;
    }
}
