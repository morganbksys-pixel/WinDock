using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using WinDock;
using WinDock.Core;

internal static partial class Program
{
    private static string[] MenuHeaders(ContextMenu menu) => menu.Items.OfType<MenuItem>().Where(i => i.Visibility == Visibility.Visible).Select(i => (string)i.Header).ToArray();

    private static void RunContextualMenuTests(string temporary, string[] args)
    {
        Run("Menu choices apply by category, persist, and can restore an entirely hidden menu", () =>
        {
            var path = Path.Combine(temporary, "menu-choices.json");
            File.WriteAllText(path, """{"AutoHide":false}""");
            var store = new SettingsStore(path);
            var settings = store.Load(out var warning);
            Check(warning is null && settings.HiddenMenuCommands.Count == 0);
            var app = new DockEntry(Guid.NewGuid(), temporary);
            var explorer = new DockEntry(Guid.NewGuid(), ApplicationIdentityResolver.ExplorerExecutable);
            settings.Entries = [app, explorer];
            var window = new DockWindow(settings, store);
            try
            {
                window.SettingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var dialog = Application.Current.Windows.OfType<Window>().Single(w => w.Title == "Réglages — WinDock");
                var view = (DockSettingsView)dialog.Content;
                view.SelectPage("Menus");
                var category = view.MenusPanel.Children.OfType<ComboBox>().Single();
                var switches = view.MenusPanel.Children.OfType<StackPanel>().Single();
                foreach (var check in switches.Children.OfType<CheckBox>())
                {
                    check.IsChecked = false;
                    check.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                }
                Check(!window.RefreshMenuChoices(window.ContextMenu) && !window.RefreshMenuChoices(window.SettingsButton.ContextMenu));
                Check(MenuHeaders(window.ContextMenu).Length == 0 && store.Load(out _).HiddenMenuCommands.Count == 3);

                category.SelectedIndex = 1;
                var edit = switches.Children.OfType<CheckBox>().Single(c => Equals(c.Content, "Modifier le raccourci…"));
                edit.IsChecked = false;
                edit.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                var panel = (InsertionPanel)((Viewbox)((Border)window.Content).Child).Child;
                ContextMenu Menu(DockEntry entry) => panel.Children.OfType<Button>().Single(b => Equals(b.Tag, entry)).ContextMenu;
                Check(window.RefreshMenuChoices(Menu(app)) && !MenuHeaders(Menu(app)).Contains("Modifier le raccourci…"));
                Check(window.RefreshMenuChoices(Menu(explorer)) && MenuHeaders(Menu(explorer)).Contains("Modifier le raccourci…"));
                if (args.Length == 2 && args[0] == "--preview")
                {
                    view.UpdateLayout();
                    SavePreview(dialog, Path.Combine(args[1], "settings-menus.png"));
                }
                view.MenusPanel.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(store.Load(out _).HiddenMenuCommands.Count == 0 && window.RefreshMenuChoices(window.ContextMenu));
                Check(switches.Children.OfType<CheckBox>().All(check => check.IsChecked == true));
            }
            finally { window.Close(); }
        });

        Run("Styled menus render both palettes and keep submenu rows and their popup available", () =>
        {
            var group = new DockEntry(Guid.NewGuid(), "", DisplayName: "Outils", IsGroup: true);
            var app = new DockEntry(Guid.NewGuid(), temporary);
            var settings = new DockSettings { AutoHide = false, PlacementMode = DockPlacementMode.Corner, Entries = [app, group] };
            var window = new DockWindow(settings, new SettingsStore(Path.Combine(temporary, "menu-theme.json")));
            try
            {
                window.Show();
                var panel = (InsertionPanel)((Viewbox)((Border)window.Content).Child).Child;
                var button = panel.Children.OfType<Button>().Single(b => Equals(b.Tag, app));
                var menu = button.ContextMenu;
                menu.PlacementTarget = button;
                menu.IsOpen = true;
                PumpUntil(() => PresentationSource.FromVisual(menu) is HwndSource);
                var move = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Déplacer vers…"));
                foreach (var dark in new[] { false, true })
                {
                    DockMenuTheme.Apply(menu, dark, false);
                    menu.UpdateLayout();
                    Check(menu.Template.FindName("DockMenuSurface", menu) is Border { CornerRadius.TopLeft: 8 });
                    Check(move.ActualHeight >= 32 && move.Items.Count == 2);
                    move.IsSubmenuOpen = true;
                    var destination = (MenuItem)move.Items[0];
                    PumpUntil(() => PresentationSource.FromVisual(destination) is HwndSource);
                    if (args.Length == 2 && args[0] == "--preview")
                    {
                        var variant = dark ? "sombre" : "clair";
                        SavePreview(menu, Path.Combine(args[1], $"menu-{variant}.png"));
                        var popup = (System.Windows.Controls.Primitives.Popup)move.Template.FindName("PART_Popup", move);
                        SavePreview((FrameworkElement)popup.Child, Path.Combine(args[1], $"sous-menu-{variant}.png"));
                    }
                    move.IsSubmenuOpen = false;
                }
            }
            finally { window.Close(); }
        });

        Run("Contextual menus contain only local actions in every layout and icon order", () =>
        {
            foreach (var layout in AllLayouts())
            foreach (var reversed in new[] { false, true })
            {
                var entry = new DockEntry(Guid.NewGuid(), temporary);
                var group = new DockEntry(Guid.NewGuid(), "", DisplayName: "Outils", IsGroup: true);
                var separator = new DockEntry(Guid.NewGuid(), "", true);
                var settings = new DockSettings { PlacementMode = layout.Mode, Edge = layout.Edge, Corner = layout.Corner,
                    Orientation = layout.Orientation, ReverseIconOrder = reversed, ShowRecycleBin = true,
                    Entries = [entry, separator, group, new(Guid.NewGuid(), temporary, GroupId: group.Id)] };
                var window = new DockWindow(settings, new SettingsStore(Path.Combine(temporary, "menu-layout.json")), new FakeRecycleBin());
                try
                {
                    var surface = (Border)window.Content;
                    Check(surface.Padding == new Thickness(8)); // The background remains a usable menu target.
                    var panel = (InsertionPanel)((Viewbox)surface.Child).Child;
                    ContextMenu Menu(DockEntry target) => panel.Children.OfType<Button>().Single(b => Equals(b.Tag, target)).ContextMenu;
                    Check(MenuHeaders(window.ContextMenu).SequenceEqual(new[] { "Ajouter une application…", "Ajouter un séparateur", "Quitter" }));
                    var applicationMenu = Menu(entry);
                    Check(MenuHeaders(applicationMenu).SequenceEqual(new[] { "Lancer à nouveau", "Modifier le raccourci…", "Déplacer vers…", "Retirer du dock" }));
                    Check(applicationMenu.Items.OfType<Separator>().Single().Visibility == Visibility.Collapsed);
                    Check(MenuHeaders(Menu(group)).SequenceEqual(new[] { "Ajouter une application…", "Renommer…", "Dissoudre le groupe" }));
                    Check(MenuHeaders(Menu(separator)).SequenceEqual(new[] { "Dupliquer le séparateur", "Retirer du dock" }) && Menu(separator).Items.Count == 2);
                    var recycleMenu = panel.Children.OfType<Button>().Single(b => b.Tag is null && b != window.SettingsButton).ContextMenu;
                    Check(MenuHeaders(recycleMenu).SequenceEqual(new[] { "Vider la corbeille…", "Masquer du dock" }) && recycleMenu.Items.Count == 2);
                    Check(!MenuHeaders(window.ContextMenu).Contains(ReverseOrderLabel));
                    Check(MenuHeaders(window.SettingsButton.ContextMenu).SequenceEqual(MenuHeaders(window.ContextMenu)));
                }
                finally { window.Close(); }
            }
        });
        Run("Group member menus keep move destinations and persist moves back to the dock", () =>
        {
            var group = new DockEntry(Guid.NewGuid(), "", DisplayName: "Outils", IsGroup: true);
            var entry = new DockEntry(Guid.NewGuid(), temporary, GroupId: group.Id);
            var settings = new DockSettings { Entries = [group, entry] };
            var store = new SettingsStore(Path.Combine(temporary, "menu-group.json"));
            var window = new DockWindow(settings, store, new FakeRecycleBin(), new FakeApplicationWindows());
            try
            {
                window.Show(); window.ToggleGroup(group);
                var groupPanel = (GroupPanel)typeof(DockWindow).GetField("groupPanel", PrivateInstance)!.GetValue(window)!;
                var button = groupPanel.Items.Children.OfType<Button>().Single();
                Check(MenuHeaders(button.ContextMenu).SequenceEqual(new[] { "Lancer à nouveau", "Modifier le raccourci…", "Déplacer vers…", "Retirer du dock" }));
                var move = button.ContextMenu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Déplacer vers…"));
                Check(move.Items.OfType<MenuItem>().Select(i => (string)i.Header).SequenceEqual(new[] { "Dock", "Outils" }));
                Check(((MenuItem)move.Items[0]).IsEnabled && !((MenuItem)move.Items[1]).IsEnabled);
                ((MenuItem)move.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(settings.Entries.Single(e => e.Id == entry.Id).GroupId is null);
                Check(store.Load(out _).Entries.Single(e => e.Id == entry.Id).GroupId is null);
            }
            finally { window.Close(); }
        });
        Run("Recycle menu follows live state, prevents duplicate actions and synchronizes hiding with settings", () =>
        {
            using var release = new ManualResetEventSlim();
            var fake = new FakeRecycleBin();
            var settings = new DockSettings { AutoHide = false, ShowRecycleBin = true };
            var store = new SettingsStore(Path.Combine(temporary, "menu-recycle.json"));
            var window = new DockWindow(settings, store, fake);
            try
            {
                window.Show();
                ((DispatcherTimer)typeof(DockWindow).GetField("timer", PrivateInstance)!.GetValue(window)!).Stop();
                var panel = (InsertionPanel)((Viewbox)((Border)window.Content).Child).Child;
                var button = panel.Children.OfType<Button>().Single(b => b != window.SettingsButton);
                var menu = button.ContextMenu;
                var empty = (MenuItem)menu.Items[0];
                void Refresh()
                {
                    PumpUntil(() => !(bool)typeof(DockWindow).GetField("checkingRecycle", PrivateInstance)!.GetValue(window)!);
                    var task = window.RefreshRecycleAsync(); PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult();
                }
                void Preview(ContextMenu target, FrameworkElement placementTarget, string name)
                {
                    if (args.Length != 2 || args[0] != "--preview") return;
                    target.PlacementTarget = placementTarget; target.IsOpen = true;
                    PumpUntil(() => PresentationSource.FromVisual(target) is HwndSource);
                    target.UpdateLayout(); SavePreview(target, Path.Combine(args[1], name + ".png"));
                    target.IsOpen = false;
                }
                Refresh(); Check(empty.IsEnabled); // Unknown state still permits the Windows-confirmed action.
                menu.PlacementTarget = button; menu.IsOpen = true;
                fake.EmptyState = true; Refresh(); Check(!empty.IsEnabled);
                Preview(menu, button, "menu-recycle-empty");
                fake.EmptyState = false; Refresh(); Check(empty.IsEnabled);
                Preview(menu, button, "menu-recycle-full");
                fake.EmptyRelease = release;
                empty.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                PumpUntil(() => fake.Empties == 1);
                Check(!empty.IsEnabled && !menu.IsOpen);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                empty.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(fake.Empties == 1 && fake.Opens == 0);
                release.Set();
                PumpUntil(() => !(bool)typeof(DockWindow).GetField("emptyingRecycle", PrivateInstance)!.GetValue(window)!);
                Refresh(); Check(!empty.IsEnabled);
                window.SettingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var dialog = Application.Current.Windows.OfType<Window>().Single(w => w.Title == "Réglages — WinDock");
                var view = (DockSettingsView)dialog.Content;
                var toggle = view.AppearancePanel.Children.OfType<CheckBox>().Single(c => Equals(c.Content, "Afficher la corbeille"));
                menu.IsOpen = true;
                ((MenuItem)menu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(!menu.IsOpen && !settings.ShowRecycleBin && toggle.IsChecked == false && !store.Load(out _).ShowRecycleBin);
                Check(panel.Children.OfType<Button>().Single() == window.SettingsButton && panel.Children.OfType<TextBlock>().Any());
                toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                Check(settings.ShowRecycleBin && store.Load(out _).ShowRecycleBin && panel.Children.OfType<Button>().Count() == 2);
                dialog.Close();
                Preview(window.ContextMenu, window, "menu-dock");
            }
            finally { release.Set(); window.Close(); }
        });
    }
}
