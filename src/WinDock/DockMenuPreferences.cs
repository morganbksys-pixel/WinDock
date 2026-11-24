using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using WinDock.Core;

namespace WinDock;

internal static class DockMenuPreferences
{
    internal sealed record Command(string Id, string Label);
    internal sealed record Category(string Id, string Label, Command[] Commands)
    {
        public override string ToString() => Label;
    }

    internal static readonly Command AddApplication = new("add-application", "Ajouter une application…");
    internal static readonly Command AddSeparator = new("add-separator", "Ajouter un séparateur");
    internal static readonly Command Quit = new("quit", "Quitter");
    internal static readonly Command Launch = new("launch", "Lancer à nouveau");
    internal static readonly Command OpenDocument = new("open", "Ouvrir le document");
    internal static readonly Command Restart = new("restart", "Redémarrer l’Explorateur Windows…");
    internal static readonly Command Edit = new("edit", "Modifier le raccourci…");
    internal static readonly Command Move = new("move", "Déplacer vers…");
    internal static readonly Command Remove = new("remove", "Retirer du dock");
    internal static readonly Command CloseWindows = new("close", "Fermer");
    internal static readonly Command Rename = new("rename", "Renommer…");
    internal static readonly Command Dissolve = new("dissolve", "Dissoudre le groupe");
    internal static readonly Command Duplicate = new("duplicate", "Dupliquer le séparateur");
    internal static readonly Command Empty = new("empty", "Vider la corbeille…");
    internal static readonly Command Hide = new("hide", "Masquer du dock");

    internal static readonly Category Dock = new("dock", "Dock et engrenage", [AddApplication, AddSeparator, Quit]);
    internal static readonly Category App = new("app", "Applications et raccourcis", [Launch, Edit, Move, Remove, CloseWindows]);
    internal static readonly Category Document = new("document", "Documents", [OpenDocument, Edit, Move, Remove]);
    internal static readonly Category Explorer = new("explorer", "Explorateur Windows", [Restart, Edit, Move, Remove, CloseWindows]);
    internal static readonly Category Group = new("group", "Groupes", [AddApplication, Rename, Dissolve]);
    internal static readonly Category Separator = new("separator", "Séparateurs", [Duplicate, Remove]);
    internal static readonly Category Recycle = new("recycle", "Corbeille", [Empty, Hide]);

    internal static string Key(Category category, Command command) => $"{category.Id}/{command.Id}";

    internal static bool Shows(DockSettings settings, Category category, Command command) =>
        !settings.HiddenMenuCommands.Contains(Key(category, command));

    internal static void Populate(StackPanel panel, DockSettings settings, Action changed)
    {
        var categories = new ComboBox { ItemsSource = new[] { Dock, App, Document, Explorer, Group, Separator, Recycle },
            Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetName(categories, "Menu à personnaliser");
        var commands = new StackPanel();
        var reset = new Button
        {
            Content = "Rétablir les menus par défaut", HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 12, 0, 0)
        };
        reset.SetResourceReference(FrameworkElement.StyleProperty, "SettingsActionButton");
        panel.Children.Add(categories);
        panel.Children.Add(commands);
        panel.Children.Add(reset);
        categories.SelectionChanged += (_, _) => Refresh();
        reset.Click += (_, _) =>
        {
            settings.HiddenMenuCommands.Clear();
            changed();
            Refresh();
        };
        categories.SelectedIndex = 0;

        void Refresh()
        {
            commands.Children.Clear();
            if (categories.SelectedItem is not Category category) return;
            foreach (var command in category.Commands)
            {
                var check = new CheckBox { Content = command.Label, Foreground = DockSettingsView.Ink,
                    IsChecked = Shows(settings, category, command), Margin = new Thickness(0, 6, 0, 6) };
                check.Click += (_, _) =>
                {
                    var key = Key(category, command);
                    if (check.IsChecked == true) settings.HiddenMenuCommands.Remove(key);
                    else settings.HiddenMenuCommands.Add(key);
                    changed();
                };
                commands.Children.Add(check);
            }
        }
    }
}
