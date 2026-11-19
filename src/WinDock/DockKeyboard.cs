using System.Windows;
using System.Windows.Controls;
using WinDock.Core;

namespace WinDock;

public sealed partial class DockWindow
{
    private CheckBox? settingsAutoHideToggle;
    private DockHotkeyEditor[] hotkeyEditors = [];
    private GlobalHotkeys? hotkeys;

    internal Func<nint, GlobalHotkeys>? HotkeyFactory { get; init; }

    private void InitializeHotkeys()
    {
        hotkeys = HotkeyFactory?.Invoke(hwnd);
        hotkeys?.TrySet(HotkeyAction.AutoHide, settings.AutoHideHotkey);
        if (settings.SettingsHotkey != settings.AutoHideHotkey)
            hotkeys?.TrySet(HotkeyAction.Settings, settings.SettingsHotkey);
    }

    internal bool HandleHotkey(int id)
    {
        if (closed || hotkeys is null || !hotkeys.TryResolve(id, out var action, out var key)) return false;
        if (settingsWindow?.IsActive == true && hotkeyEditors.Any(editor => editor.TryCaptureRegistered(key!))) return true;
        if (action == HotkeyAction.AutoHide) SetAutoHide(!settings.AutoHide);
        else if (!dragging && !externalDrag) OpenSettings();
        return true;
    }

    private void SetAutoHide(bool enabled)
    {
        if (settings.AutoHide == enabled) return;
        settings.AutoHide = enabled;
        if (settingsAutoHideToggle is not null) settingsAutoHideToggle.IsChecked = enabled;
        settingsPositionPicker?.Refresh();
        outsideSince = triggerSince = 0;
        if (!enabled)
        {
            indicator?.SetAppearance(false, false, false);
            SetRevealed(display is null || !Native.FullScreen(display, hwnd));
        }
        Persist();
    }

    private void AddHotkeyEditors(StackPanel panel)
    {
        panel.Children.Add(new TextBlock { Text = "Raccourcis clavier", Foreground = DockSettingsView.Ink, FontSize = 18 });
        panel.Children.Add(new TextBlock
        {
            Text = "Sélectionnez un champ, pressez la combinaison souhaitée, puis cliquez sur Appliquer.",
            Foreground = DockSettingsView.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 14)
        });
        hotkeyEditors =
        [
            Editor("Basculer le masquage automatique", HotkeyAction.AutoHide, settings.AutoHideHotkey, DockHotkey.ToggleAutoHide),
            Editor("Ouvrir les réglages", HotkeyAction.Settings, settings.SettingsHotkey, DockHotkey.OpenSettings)
        ];
        foreach (var editor in hotkeyEditors) panel.Children.Add(editor);

        DockHotkeyEditor Editor(string label, HotkeyAction action, DockHotkey? current, DockHotkey defaultValue) =>
            new(label, current, defaultValue, current is not null && hotkeys?.IsRegistered(action) == false,
                key => ApplyHotkey(action, key));
    }

    internal string? ApplyHotkey(HotkeyAction action, DockHotkey? key)
    {
        var autoHide = action == HotkeyAction.AutoHide;
        if (key is not null && key == (autoHide ? settings.SettingsHotkey : settings.AutoHideHotkey))
            return "Cette combinaison est déjà attribuée à l’autre action.";
        if (hotkeys?.TrySet(action, key) != true)
            return "Windows n’a pas pu inscrire cette combinaison. Choisissez-en une autre.";
        if (autoHide) settings.AutoHideHotkey = key;
        else settings.SettingsHotkey = key;
        Persist();
        return null;
    }
}
