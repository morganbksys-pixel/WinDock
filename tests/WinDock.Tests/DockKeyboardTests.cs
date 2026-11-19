using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using WinDock;
using WinDock.Core;

internal static partial class Program
{
    private static void RunDockKeyboardTests(string temporary, string[] args)
    {
        Run("Legacy settings keep defaults and an invalid hotkey does not discard other settings", () =>
        {
            var path = Path.Combine(temporary, "keyboard-legacy.json");
            var store = new SettingsStore(path);
            File.WriteAllText(path, """{"AutoHide":false}""");
            var settings = store.Load(out var warning);
            Check(warning is null && !settings.AutoHide && settings.AutoHideHotkey == DockHotkey.ToggleAutoHide &&
                settings.SettingsHotkey == DockHotkey.OpenSettings);
            File.WriteAllText(path, """{"AutoHide":false,"AutoHideHotkey":{"Modifiers":4,"VirtualKey":68},"SettingsHotkey":null}""");
            settings = store.Load(out warning);
            Check(warning is null && !settings.AutoHide && settings.AutoHideHotkey is null && settings.SettingsHotkey is null);
        });

        Run("Global shortcuts dispatch and persist, keep previous registrations on conflict, and release on close", () =>
        {
            var live = new Dictionary<int, DockHotkey>();
            var blocked = new DockHotkey(3, 'X');
            var modified = new DockHotkey(7, 'D');
            var settings = new DockSettings();
            var store = new SettingsStore(Path.Combine(temporary, "keyboard.json"));
            var window = new DockWindow(settings, store)
            {
                HotkeyFactory = _ => new GlobalHotkeys((id, flags, key) =>
                {
                    Check((flags & 0x4000) != 0);
                    var binding = new DockHotkey(flags & ~0x4000, key);
                    return binding != blocked && !live.ContainsValue(binding) && live.TryAdd(id, binding);
                }, id => Check(live.Remove(id)))
            };
            try
            {
                window.Show();
                int Id(DockHotkey binding) => live.Single(pair => pair.Value == binding).Key;
                var original = Id(DockHotkey.ToggleAutoHide);
                Check(window.ApplyHotkey(HotkeyAction.AutoHide, blocked) is not null && live.ContainsKey(original));
                Check(window.ApplyHotkey(HotkeyAction.AutoHide, DockHotkey.OpenSettings) is not null && live.ContainsKey(original));
                Check(window.ApplyHotkey(HotkeyAction.AutoHide, modified) is null && !window.HandleHotkey(original));
                Check(store.Load(out _).AutoHideHotkey == modified);

                Check(AutomationProperties.GetName(window.SettingsButton) == "Réglages");
                window.SettingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var dialog = Application.Current.Windows.OfType<Window>().Single(w => w.Title == "Réglages — WinDock");
                var view = (DockSettingsView)dialog.Content;
                var toggle = view.AppearancePanel.Children.OfType<CheckBox>().Single(c => Equals(c.Content, "Masquage automatique"));
                Check(window.HandleHotkey(Id(modified)) && toggle.IsChecked == false && !store.Load(out _).AutoHide);
                Check(window.HandleHotkey(Id(modified)) && toggle.IsChecked == true && store.Load(out _).AutoHide);
                dialog.WindowState = WindowState.Minimized;
                Check(window.HandleHotkey(Id(DockHotkey.OpenSettings)) && dialog.WindowState == WindowState.Normal);
                view.SelectPage("Clavier");
                if (args.Length == 2 && args[0] == "--preview")
                {
                    view.UpdateLayout();
                    SavePreview(dialog, Path.Combine(args[1], "settings-keyboard.png"));
                }
                var settingsId = Id(DockHotkey.OpenSettings);
                Check(window.ApplyHotkey(HotkeyAction.Settings, null) is null &&
                    !window.HandleHotkey(settingsId) && store.Load(out _).SettingsHotkey is null);
            }
            finally { window.Close(); }
            Check(live.Count == 0);
        });

        Run("Duplicating a separator inserts an independent adjacent entry in either icon order", () =>
        {
            foreach (var reversed in new[] { false, true })
            {
                var separator = new DockEntry(Guid.NewGuid(), "", IsSeparator: true);
                var app = new DockEntry(Guid.NewGuid(), temporary);
                var settings = new DockSettings
                {
                    PlacementMode = DockPlacementMode.Corner, Corner = DockCorner.BottomLeft,
                    ReverseIconOrder = reversed, Entries = [separator, app]
                };
                var store = new SettingsStore(Path.Combine(temporary, "duplicate-separator.json"));
                var window = new DockWindow(settings, store);
                try
                {
                    var panel = (StackPanel)((Viewbox)((Border)window.Content).Child).Child;
                    var button = panel.Children.OfType<Button>().Single(b => Equals(b.Tag, separator));
                    button.ContextMenu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Dupliquer le séparateur"))
                        .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    var copy = settings.Entries[1];
                    Check(copy.IsSeparator && copy.Id != separator.Id && settings.Entries.SequenceEqual(new[] { separator, copy, app }));
                    Check(store.Load(out _).Entries.SequenceEqual(settings.Entries));
                    var physical = panel.Children.OfType<Button>().Select(b => b.Tag).OfType<DockEntry>();
                    Check(physical.SequenceEqual(reversed ? settings.Entries.AsEnumerable().Reverse() : settings.Entries));
                }
                finally { window.Close(); }
            }
        });
    }
}
