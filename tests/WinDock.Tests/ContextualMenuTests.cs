using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using WinDock;
using WinDock.Core;

internal static partial class Program
{
    private static string[] MenuHeaders(ContextMenu menu) => menu.Items.OfType<MenuItem>().Select(i => (string)i.Header).ToArray();

    private static void RunContextualMenuTests(string temporary, string[] args)
    {
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
                    Check(MenuHeaders(window.ContextMenu).SequenceEqual(new[] { "Ajouter…", "Ajouter un séparateur", "Réglages…", "Quitter WinDock" }));
                    var applicationMenu = Menu(entry);
                    Check(MenuHeaders(applicationMenu).SequenceEqual(new[] { "Lancer à nouveau", "Modifier / réparer…", "Déplacer vers…", "Retirer du dock" }));
                    Check(applicationMenu.Items.OfType<Separator>().Count() == 1 && applicationMenu.Items[1] is Separator);
                    Check(MenuHeaders(Menu(group)).SequenceEqual(new[] { "Ajouter…", "Renommer…", "Dissoudre le groupe" }));
                    Check(MenuHeaders(Menu(separator)).SequenceEqual(new[] { "Retirer du dock" }) && Menu(separator).Items.Count == 1);
                    var recycleMenu = panel.Children.OfType<Button>().Single(b => b.Tag is null).ContextMenu;
                    Check(MenuHeaders(recycleMenu).SequenceEqual(new[] { "Vider la corbeille…", "Masquer du dock" }) && recycleMenu.Items.Count == 2);
                    Check(!MenuHeaders(window.ContextMenu).Contains(ReverseOrderLabel));
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
                Check(MenuHeaders(button.ContextMenu).SequenceEqual(new[] { "Lancer à nouveau", "Modifier / réparer…", "Déplacer vers…", "Retirer du dock" }));
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
                var button = panel.Children.OfType<Button>().Single();
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
                window.ContextMenu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Réglages…")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                var dialog = Application.Current.Windows.OfType<Window>().Single(w => w.Title == "Réglages — WinDock");
                var view = (DockSettingsView)dialog.Content;
                var toggle = view.AppearancePanel.Children.OfType<CheckBox>().Single(c => Equals(c.Content, "Afficher la corbeille"));
                menu.IsOpen = true;
                ((MenuItem)menu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(!menu.IsOpen && !settings.ShowRecycleBin && toggle.IsChecked == false && !store.Load(out _).ShowRecycleBin);
                Check(!panel.Children.OfType<Button>().Any() && panel.Children.OfType<TextBlock>().Any());
                toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                Check(settings.ShowRecycleBin && store.Load(out _).ShowRecycleBin && panel.Children.OfType<Button>().Count() == 1);
                dialog.Close();
                Preview(window.ContextMenu, window, "menu-dock");
            }
            finally { release.Set(); window.Close(); }
        });
    }
}
