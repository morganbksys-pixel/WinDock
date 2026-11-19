using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using WinDock.Core;

namespace WinDock;

internal sealed class DockHotkeyEditor : StackPanel
{
    private readonly TextBox input = new() { IsReadOnly = true, MinWidth = 150, Padding = new Thickness(8, 5, 8, 5) };
    private readonly Button apply = new() { Content = "Appliquer", IsEnabled = false };
    private readonly TextBlock status = new() { Foreground = DockSettingsView.Muted, TextWrapping = TextWrapping.Wrap };
    private DockHotkey? pending;

    internal DockHotkeyEditor(string label, DockHotkey? current, DockHotkey defaultValue, bool unavailable,
        Func<DockHotkey?, string?> save)
    {
        Margin = new Thickness(0, 0, 0, 14);
        Children.Add(new TextBlock { Text = label, Foreground = DockSettingsView.Ink });
        AutomationProperties.SetName(input, label);
        AutomationProperties.SetHelpText(input, "Appuyez sur la combinaison souhaitée, puis sélectionnez Appliquer.");
        input.Text = Describe(current);
        input.PreviewKeyDown += Capture;

        var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 4) };
        input.Margin = new Thickness(0, 0, 6, 4);
        actions.Children.Add(input);
        var disable = new Button { Content = "Désactiver", IsEnabled = current is not null };
        AddButton(apply, () => Save(pending));
        AddButton(disable, () => Save(null));
        AddButton(new Button { Content = "Par défaut" }, () => Save(defaultValue));
        Children.Add(actions);
        Children.Add(status);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        ShowStatus(unavailable ? "Ce raccourci est indisponible. Choisissez une autre combinaison." : "");

        void AddButton(Button button, Action action)
        {
            button.SetResourceReference(StyleProperty, "SettingsActionButton");
            button.Padding = new Thickness(8, 5, 8, 5);
            button.Margin = new Thickness(0, 0, 6, 4);
            AutomationProperties.SetName(button, $"{button.Content} — {label}");
            button.Click += (_, _) => action();
            actions.Children.Add(button);
        }

        void Save(DockHotkey? value)
        {
            var error = save(value);
            if (error is not null)
            {
                ShowStatus($"{error} Réglage conservé : {Describe(current)}.");
                return;
            }
            current = value;
            input.Text = Describe(value);
            apply.IsEnabled = false;
            disable.IsEnabled = value is not null;
            ShowStatus("");
        }
    }

    internal bool TryCaptureRegistered(DockHotkey key)
    {
        if (!input.IsKeyboardFocused) return false;
        SetPending(key);
        return true;
    }

    private void Capture(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = (int)Keyboard.Modifiers;
        if ((key == Key.Tab && (modifiers & 11) == 0) || (key == Key.Escape && modifiers == 0)) return;
        e.Handled = true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var combination = new DockHotkey(modifiers, KeyInterop.VirtualKeyFromKey(key));
        if (combination.IsValid) SetPending(combination);
        else ShowStatus("Choisissez une touche avec Ctrl, Alt ou Windows. F12 est réservée.");
    }

    private void SetPending(DockHotkey key)
    {
        pending = key;
        input.Text = Describe(key);
        apply.IsEnabled = true;
        ShowStatus("");
    }

    private void ShowStatus(string message)
    {
        status.Text = message;
        status.Visibility = message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string Describe(DockHotkey? key)
    {
        if (key is null) return "Désactivé";
        var parts = new List<string>();
        foreach (var (mask, label) in new[] { (2, "Ctrl"), (1, "Alt"), (4, "Maj"), (8, "Windows") })
            if ((key.Modifiers & mask) != 0) parts.Add(label);
        var converted = KeyInterop.KeyFromVirtualKey(key.VirtualKey);
        parts.Add(converted == Key.None ? $"0x{key.VirtualKey:X2}" : converted.ToString());
        return string.Join(" + ", parts);
    }
}
