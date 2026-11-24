using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using WinDock.Core;

namespace WinDock;

internal static class OpenApplications
{
    internal static List<DockEntry> Unpinned(IReadOnlyList<ApplicationWindow> windows, IReadOnlyList<DockEntry> favorites)
    {
        var identities = favorites.Where(e => !e.IsGroup && !e.IsSeparator)
            .Select(ApplicationIdentityResolver.Shared.Resolve).ToArray();
        var apps = new Dictionary<string, DockEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var window in windows)
        {
            if (identities.Any(identity => identity.Matches(window.Path, window.ApplicationId))) continue;
            var path = window.ApplicationId is { Length: > 0 } id ? @"shell:AppsFolder\" + id : window.Path;
            if (string.IsNullOrWhiteSpace(path) || apps.ContainsKey(path)) continue;
            var name = Path.GetFileNameWithoutExtension(window.Path);
            apps.Add(path, new(Guid.NewGuid(), path, DisplayName: string.IsNullOrEmpty(name) ? window.ApplicationId : name,
                SourcePath: path, ApplicationId: window.ApplicationId));
        }
        return apps.Values.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

public sealed partial class DockWindow
{
    private Dictionary<string, ImageSource?> openIcons = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<DockEntry> openApplications = [];
    private IReadOnlyList<DockEntry> openSectionFavorites = [];

    private void AddOpenApplicationsSection()
    {
        if (openApplications.Count == 0) return;
        if (items.Children.Count > 0)
            items.Children.Add(new Border
            {
                Width = Horizontal ? 1 : 32, Height = Horizontal ? 32 : 1,
                Margin = new Thickness(6), Background = Brushes.Gray, IsHitTestVisible = false
            });
        foreach (var entry in openApplications)
        {
            var name = ManagedShortcuts.Name(entry);
            var menu = new ContextMenu();
            ConfigureMenu(menu);
            AddMenuAction(menu, "Épingler au dock", async () => await PinOpenApplicationAsync(entry));
            var button = new Button
            {
                Width = 60, Height = 60, ContextMenu = menu,
                Content = openIcons[entry.Path] is { } icon ? new Image { Source = icon, Width = 48, Height = 48 }
                    : new TextBlock { Text = name[..1], FontSize = 28 }
            };
            AutomationProperties.SetName(button, name + " — application ouverte non épinglée");
            AttachLabel(button, name);
            button.Click += async (_, _) => await OpenEntryAsync(entry, button);
            items.Children.Add(button);
        }
    }

    internal async Task PinOpenApplicationAsync(DockEntry entry)
    {
        if (!openApplications.Contains(entry)) return;
        var favorites = settings.Entries.ToArray();
        try
        {
            var pinned = entry;
            if (entry.ApplicationId is { } id)
                pinned = await StaWorker.RunAsync(_ => shortcuts.ImportApplication(
                    new(ManagedShortcuts.Name(entry), id, null, entry.Path)), lifetime.Token);
            if (closed || !openApplications.Contains(entry) || !settings.Entries.SequenceEqual(favorites))
            {
                shortcuts.Discard(pinned);
                return;
            }
            shortcuts.Commit(settings, [.. favorites, pinned]);
            openApplications = openApplications.Where(e => e != entry).ToArray();
            openSectionFavorites = settings.Entries.ToArray();
            lastRunningCheck = 0;
            RenderItems();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or Win32Exception or ArgumentException or InvalidOperationException)
        { if (!closed) ShowNotice("Impossible d’épingler cette application.\n\n" + ex.Message); }
    }
}
