using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinDock.Core;

public enum DockEdge { Bottom, Top, Left, Right }
/// <summary>A launcher target or a movable visual separator.</summary>
/// <param name="Id">Stable identity used for ordering.</param>
/// <param name="Path">Launch path, including a managed shortcut copy; empty for a separator.</param>
/// <param name="IsSeparator">Whether this entry separates groups instead of launching a target.</param>
/// <param name="DisplayName">User-facing label, independent of the managed filename.</param>
/// <param name="SourcePath">Original imported path, retained for reference and duplicate detection.</param>
/// <param name="ApplicationId">Optional Windows AppsFolder identity for catalogue entries.</param>
public sealed record DockEntry(Guid Id, string Path, bool IsSeparator = false, string? DisplayName = null, string? SourcePath = null, string? ApplicationId = null);
public sealed class DockSettings
{
    public bool AutoHide { get; set; } = true;
    public bool ShowRecycleBin { get; set; }
    public DockEdge Edge { get; set; } = DockEdge.Bottom;
    public string? MonitorDevice { get; set; }
    /// <summary>Background opacity from 0.1 to 1; icons remain fully opaque.</summary>
    public double BackgroundOpacity { get; set; } = 238.0 / 255;
    public List<DockEntry> Entries { get; set; } = [];
}

public sealed class SettingsStore(string path)
{
    /// <summary>Directory containing this configuration and its managed shortcut files.</summary>
    public string DataDirectory => System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public DockSettings Load(out string? warning)
    {
        warning = null;
        if (!File.Exists(path)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<DockSettings>(File.ReadAllText(path), Options)
                ?? throw new JsonException("Configuration vide.");
            if (!Enum.IsDefined(settings.Edge) || !double.IsFinite(settings.BackgroundOpacity) ||
                settings.BackgroundOpacity is < 0.1 or > 1 || settings.Entries is null ||
                settings.Entries.Any(e => e is null || e.Id == Guid.Empty || (!e.IsSeparator && string.IsNullOrWhiteSpace(e.Path))) ||
                settings.Entries.Select(e => e.Id).Distinct().Count() != settings.Entries.Count)
                throw new JsonException("Configuration invalide.");
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Preserve the original before allowing a subsequent save to replace it.
            try { File.Copy(path, path + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"), false); }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException)
            {
                throw new IOException("Impossible de sauvegarder la configuration illisible. Le fichier original est conservé.", backupError);
            }
            warning = "La configuration était illisible. Une copie a été conservée ; les réglages par défaut sont chargés.";
            return new();
        }
    }

    public void Save(DockSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public static class EntryRules
{
    public static bool IsSupported(string path) => Directory.Exists(path) ||
        (File.Exists(path) && (System.IO.Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                              System.IO.Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)));

    public static string DisplayName(string path) => Directory.Exists(path)
        ? new DirectoryInfo(path).Name : System.IO.Path.GetFileNameWithoutExtension(path);

    public static void Move(List<DockEntry> entries, Guid sourceId, Guid? beforeId)
    {
        if (sourceId == beforeId) return;
        var source = entries.Find(e => e.Id == sourceId);
        if (source is null) return;
        entries.Remove(source);
        var index = beforeId is null ? -1 : entries.FindIndex(e => e.Id == beforeId);
        entries.Insert(index < 0 ? entries.Count : index, source);
    }
}
