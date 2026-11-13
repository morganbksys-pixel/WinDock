using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinDock.Core;

public enum DockEdge { Bottom, Top, Left, Right }
/// <summary>Whether settings were absent, read successfully, or recovered from an invalid file.</summary>
public enum SettingsLoadStatus { Missing, Loaded, Recovered }
/// <summary>A launcher target, movable visual separator, or root group tile.</summary>
/// <param name="Id">Stable identity used for ordering.</param>
/// <param name="Path">Launch path, including a managed shortcut copy; empty for a separator.</param>
/// <param name="IsSeparator">Whether this entry separates groups instead of launching a target.</param>
/// <param name="DisplayName">User-facing label, independent of the managed filename.</param>
/// <param name="SourcePath">Original imported path, retained for reference and duplicate detection.</param>
/// <param name="ApplicationId">Optional Windows AppsFolder identity for catalogue entries.</param>
/// <param name="IsGroup">Whether this entry is a root group tile with an empty path.</param>
/// <param name="GroupId">Parent group identity, or null for a root entry.</param>
public sealed record DockEntry(Guid Id, string Path, bool IsSeparator = false, string? DisplayName = null, string? SourcePath = null, string? ApplicationId = null, bool IsGroup = false, Guid? GroupId = null);
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

    public DockSettings Load(out string? warning) => Load(out warning, out _);

    /// <summary>Loads settings and distinguishes a first launch from an existing empty or invalid configuration.</summary>
    /// <param name="warning">Recovery information, if any.</param>
    /// <param name="status">The source of the returned configuration.</param>
    /// <returns>The loaded settings, or defaults when absent or recovered.</returns>
    public DockSettings Load(out string? warning, out SettingsLoadStatus status)
    {
        warning = null;
        status = SettingsLoadStatus.Loaded;
        try
        {
            string data;
            try { data = File.ReadAllText(path); }
            catch (FileNotFoundException) { status = SettingsLoadStatus.Missing; return new(); }
            catch (DirectoryNotFoundException) { status = SettingsLoadStatus.Missing; return new(); }
            var settings = JsonSerializer.Deserialize<DockSettings>(data, Options)
                ?? throw new JsonException("Configuration vide.");
            if (!Enum.IsDefined(settings.Edge) || !double.IsFinite(settings.BackgroundOpacity) ||
                settings.BackgroundOpacity is < 0.1 or > 1 || settings.Entries is null ||
                !EntryRules.IsValid(settings.Entries) ||
                settings.Entries.Select(e => e.Id).Distinct().Count() != settings.Entries.Count)
                throw new JsonException("Configuration invalide.");
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            status = SettingsLoadStatus.Recovered;
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
        if (!EntryRules.IsValid(settings.Entries)) throw new JsonException("Organisation du dock invalide.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        if (settings.Entries.Any(e => e.IsGroup) && File.Exists(path))
        {
            var previous = JsonSerializer.Deserialize<DockSettings>(File.ReadAllText(path), Options);
            if (previous is not null && !previous.Entries.Any(e => e.IsGroup))
                File.Copy(path, path + ".before-groups-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N"), false);
        }
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
    /// <summary>Validates a flat collection with at most one level of group membership.</summary>
    /// <param name="entries">Entries to validate.</param>
    /// <returns>Whether identities, paths and group memberships are valid.</returns>
    public static bool IsValid(IReadOnlyList<DockEntry> entries)
    {
        if (entries.Any(e => e is null || e.Id == Guid.Empty) || entries.Select(e => e.Id).Distinct().Count() != entries.Count) return false;
        var groups = entries.Where(e => e.IsGroup).Select(e => e.Id).ToHashSet();
        return entries.All(e => e.IsGroup
            ? !e.IsSeparator && e.GroupId is null && string.IsNullOrWhiteSpace(e.Path) && !string.IsNullOrWhiteSpace(e.DisplayName)
            : (e.IsSeparator || !string.IsNullOrWhiteSpace(e.Path)) &&
              (e.GroupId is null || !e.IsSeparator && groups.Contains(e.GroupId.Value)));
    }

    /// <summary>Returns a new collection after a move, preserving shortcut identities and paths.</summary>
    /// <param name="entries">The current collection.</param>
    /// <param name="sourceId">Entry to move.</param>
    /// <param name="groupId">Destination group, or null for the dock.</param>
    /// <param name="beforeId">Destination sibling to insert before, or null to append.</param>
    /// <returns>A new collection; invalid destinations leave the collection unchanged.</returns>
    public static List<DockEntry> MoveTo(IReadOnlyList<DockEntry> entries, Guid sourceId, Guid? groupId, Guid? beforeId = null)
    {
        var result = entries.ToList();
        var source = entries.FirstOrDefault(e => e.Id == sourceId);
        if (source is null || sourceId == beforeId ||
            groupId is not null && (source.IsGroup || source.IsSeparator || !entries.Any(e => e.Id == groupId && e.IsGroup)) ||
            beforeId is not null && !entries.Any(e => e.Id == beforeId && e.GroupId == groupId)) return result;
        result.Remove(source);
        var index = beforeId is null ? -1 : result.FindIndex(e => e.Id == beforeId);
        result.Insert(index < 0 ? result.Count : index, source with { GroupId = groupId });
        return result;
    }

    /// <summary>Replaces a group with its members, in their current order.</summary>
    /// <param name="entries">The current collection.</param>
    /// <param name="groupId">Group to dissolve.</param>
    /// <returns>A new collection retaining every member and its managed shortcut.</returns>
    public static List<DockEntry> Dissolve(IReadOnlyList<DockEntry> entries, Guid groupId)
    {
        var result = entries.ToList();
        var group = result.FirstOrDefault(e => e.Id == groupId && e.IsGroup);
        if (group is null) return result;
        var children = result.Where(e => e.GroupId == groupId).Select(e => e with { GroupId = null }).ToArray();
        result.RemoveAll(e => e.GroupId == groupId);
        var index = result.IndexOf(group);
        result.RemoveAt(index);
        result.InsertRange(index, children);
        return result;
    }

    /// <summary>Dissolves groups with fewer than two pinned shortcuts, retaining all members.</summary>
    /// <param name="entries">Structurally valid entries, including legacy empty groups.</param>
    /// <returns>A new collection with no undersized groups.</returns>
    public static List<DockEntry> NormalizeGroups(IReadOnlyList<DockEntry> entries)
    {
        var result = entries.ToList();
        foreach (var group in entries.Where(e => e.IsGroup))
            if (result.Count(e => e.GroupId == group.Id) < 2) result = Dissolve(result, group.Id);
        return result;
    }

    /// <summary>Groups a dragged shortcut with a root shortcut in one candidate collection.</summary>
    /// <param name="entries">The current entries.</param>
    /// <param name="sourceId">Dragged shortcut, including a member of another group.</param>
    /// <param name="targetId">Root shortcut to replace with a group.</param>
    /// <param name="groupId">Unique identity for the new group.</param>
    /// <returns>A normalized collection, or an unchanged copy for an invalid pair.</returns>
    public static List<DockEntry> GroupPair(IReadOnlyList<DockEntry> entries, Guid sourceId, Guid targetId, Guid groupId)
    {
        var source = entries.FirstOrDefault(e => e.Id == sourceId);
        var target = entries.FirstOrDefault(e => e.Id == targetId);
        if (source is null || target is null || sourceId == targetId || source.IsGroup || source.IsSeparator ||
            target.IsGroup || target.IsSeparator || target.GroupId is not null || groupId == Guid.Empty || entries.Any(e => e.Id == groupId)) return entries.ToList();
        var result = entries.ToList(); result.Remove(source);
        result[result.IndexOf(target)] = new DockEntry(groupId, "", DisplayName: "Nouveau groupe", IsGroup: true);
        result.Add(target with { GroupId = groupId }); result.Add(source with { GroupId = groupId });
        return NormalizeGroups(result);
    }

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
