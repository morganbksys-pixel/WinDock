using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinDock;

/// <summary>Shares a catalogue refresh and keeps a disposable, atomic disk snapshot.</summary>
internal sealed class CatalogCache
{
    private sealed record CachedApp(string Name, string? ApplicationId, string? ShortcutPath, string LaunchKey, string? Png);
    private sealed record Snapshot(int Version, List<CachedApp> Applications);
    internal static CatalogCache Shared { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinDock", "CatalogCache"));
    private readonly string path;
    private readonly Func<CancellationToken, Task<CatalogResult>> loader;
    private readonly CancellationTokenSource lifetime = new();
    private readonly object gate = new();
    private Task? preload;
    private Task<CatalogResult>? refresh;
    private CatalogResult? current;
    internal CatalogResult? Current => Volatile.Read(ref current);

    internal CatalogCache(string directory, Func<CancellationToken, Task<CatalogResult>>? loader = null)
    { path = Path.Combine(directory, "catalog.json"); this.loader = loader ?? ApplicationCatalog.LoadAsync; }

    internal Task PreloadAsync()
    {
        lock (gate) return preload ??= Task.Run(() =>
        {
            try
            {
                var data = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path));
                if (data is not { Version: 1, Applications: not null } || data.Applications.Any(a =>
                    a is null || string.IsNullOrWhiteSpace(a.Name) || string.IsNullOrEmpty(a.LaunchKey) ||
                    (string.IsNullOrEmpty(a.ApplicationId) && string.IsNullOrEmpty(a.ShortcutPath)))) return;
                var apps = data.Applications.Select(a => new CatalogApplication(a.Name, a.ApplicationId,
                    a.ShortcutPath, a.LaunchKey, Decode(a.Png))).ToArray();
                Volatile.Write(ref current, new(apps, null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { }
        });
    }

    internal Task<CatalogResult> RefreshAsync()
    {
        lock (gate)
        {
            if (refresh is { IsCompleted: false }) return refresh;
            return refresh = RefreshCoreAsync();
        }
    }

    private async Task<CatalogResult> RefreshCoreAsync()
    {
        await PreloadAsync().ConfigureAwait(false);
        try
        {
            var result = await loader(lifetime.Token).ConfigureAwait(false);
            lifetime.Token.ThrowIfCancellationRequested();
            if (result.Warning is not null) return Current is { } previous ? previous with { Warning = result.Warning } : result;
            Volatile.Write(ref current, result);
            try { await Task.Run(() => Save(result), lifetime.Token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            { return result with { Warning = "Le catalogue est à jour, mais son cache n’a pas pu être enregistré." }; }
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            if (Current is { } previous) return previous with { Warning = "Actualisation indisponible. Le dernier catalogue connu est conservé." };
            throw;
        }
    }

    private void Save(CatalogResult result)
    {
        var snapshot = new Snapshot(1, result.Applications.Select(a =>
            new CachedApp(a.Name, a.ApplicationId, a.ShortcutPath, a.LaunchKey, Encode(a.Icon))).ToList());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot));
            lifetime.Token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string? Encode(ImageSource? image)
    {
        if (image is not BitmapSource bitmap) return null;
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); return Convert.ToBase64String(stream.ToArray());
    }

    private static ImageSource? Decode(string? png)
    {
        if (png is null) return null;
        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(png));
            var bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            bitmap.Freeze(); return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or FormatException or System.Runtime.InteropServices.COMException)
        { return null; }
    }

    internal void Stop() => lifetime.Cancel();
}
