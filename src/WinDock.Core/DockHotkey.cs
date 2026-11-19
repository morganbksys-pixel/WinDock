using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinDock.Core;

/// <summary>A Windows virtual key and its modifier flags, independent of WPF.</summary>
/// <param name="Modifiers">Alt (1), Ctrl (2), Shift (4) and Windows (8).</param>
/// <param name="VirtualKey">Windows virtual key code.</param>
[JsonConverter(typeof(DockHotkeyConverter))]
public sealed record DockHotkey(int Modifiers, int VirtualKey)
{
    /// <summary>Default shortcut for automatic hiding.</summary>
    public static DockHotkey ToggleAutoHide { get; } = new(3, 'D');
    /// <summary>Default shortcut for settings.</summary>
    public static DockHotkey OpenSettings { get; } = new(3, 'P');

    /// <summary>Whether the combination can be offered for registration.</summary>
    public bool IsValid => (Modifiers & ~15) == 0 && (Modifiers & 11) != 0 &&
        VirtualKey is >= 8 and <= 254 and not (0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0x7B or >= 0xA0 and <= 0xA5);
}

internal sealed class DockHotkeyConverter : JsonConverter<DockHotkey>
{
    public override DockHotkey? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !ReadNumber(nameof(DockHotkey.Modifiers), out var modifiers) ||
            !ReadNumber(nameof(DockHotkey.VirtualKey), out var key)) return null;
        var combination = new DockHotkey(modifiers, key);
        return combination.IsValid ? combination : null;

        bool ReadNumber(string name, out int number)
        {
            number = 0;
            return root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt32(out number);
        }
    }

    public override void Write(Utf8JsonWriter writer, DockHotkey value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(nameof(DockHotkey.Modifiers), value.Modifiers);
        writer.WriteNumber(nameof(DockHotkey.VirtualKey), value.VirtualKey);
        writer.WriteEndObject();
    }
}
