using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinDock.Core;
using static WinDock.DockMenuPreferences;

namespace WinDock;

public sealed partial class DockWindow
{
    private long contextRequest;

    private sealed class MenuPolicy(Category category)
    {
        internal Category Category = category;
        internal Action? Prepare;
        internal Action<FrameworkElement, bool>? OpenIfAvailable;
        internal bool HasWindows;
    }

    private ContextMenu NewMenu(Category category)
    {
        var menu = new ContextMenu { StaysOpen = false, Tag = new MenuPolicy(category) };
        ConfigureMenu(menu);
        return menu;
    }

    private static MenuItem AddConfigured(ContextMenu menu, Command command, Action action)
    {
        var item = AddMenuAction(menu, command.Label, action);
        item.Tag = command;
        return item;
    }

    internal bool RefreshMenuChoices(ContextMenu menu)
    {
        var policy = (MenuPolicy)menu.Tag;
        var seen = false;
        Separator? divider = null;
        foreach (var element in menu.Items.OfType<FrameworkElement>())
        {
            if (element is Separator separator)
            {
                separator.Visibility = Visibility.Collapsed;
                divider = seen ? separator : null;
            }
            else if (element.Tag is Command command)
            {
                var show = Shows(settings, policy.Category, command) && (command != CloseWindows || policy.HasWindows);
                element.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (!show) continue;
                if (divider is not null) divider.Visibility = Visibility.Visible;
                divider = null;
                seen = true;
            }
        }
        return seen;
    }

    private void AttachMenu(FrameworkElement owner)
    {
        owner.ContextMenuOpening += (_, e) =>
        {
            if (e.Handled) return;
            // A child with its own menu handles this event before the dock fallback.
            if (owner == this && e.OriginalSource is DependencyObject source &&
                items.Children.OfType<Button>().Any(button => button.IsAncestorOf(source) || button == source)) return;
            if (owner.ContextMenu is not { Tag: MenuPolicy policy } menu) return;
            contextRequest++;
            policy.Prepare?.Invoke();
            if (RefreshMenuChoices(menu)) return;
            e.Handled = true;
            policy.OpenIfAvailable?.Invoke(owner, e.CursorLeft >= 0);
        };
    }

    private ContextMenu CreateEntryMenu(DockEntry entry)
    {
        var isExplorer = ApplicationIdentityResolver.IsExplorerExecutable(entry.Path) ||
            ApplicationIdentityResolver.IsExplorerExecutable(entry.SourcePath);
        var menu = NewMenu(entry.IsSeparator ? DockMenuPreferences.Separator : isExplorer ? Explorer : DockMenuPreferences.App);
        var policy = (MenuPolicy)menu.Tag;
        if (entry.IsSeparator)
        {
            AddConfigured(menu, Duplicate, () =>
            {
                var index = settings.Entries.IndexOf(entry);
                if (index < 0) return;
                var next = settings.Entries.ToList();
                next.Insert(index + 1, new DockEntry(Guid.NewGuid(), "", IsSeparator: true));
                CommitEntries(next);
            });
            AddRemove();
            RefreshMenuChoices(menu);
            return menu;
        }

        var launch = AddConfigured(menu, isExplorer ? Restart : DockMenuPreferences.Launch, async () =>
        {
            if (!IsCurrentEntry(entry)) return;
            if (isExplorer) await RestartExplorerAsync();
            else this.Launch(entry);
        });
        launch.IsEnabled = !isExplorer || !restartingExplorer;
        AddConfigured(menu, Edit, () => EditEntry(entry));
        if (settings.Entries.Any(e => e.IsGroup))
        {
            var moveItem = CreateMoveMenu(entry);
            moveItem.Tag = Move;
            menu.Items.Add(moveItem);
        }
        AddRemove();
        menu.Items.Add(new System.Windows.Controls.Separator());
        var close = AddConfigured(menu, CloseWindows, async () => await CloseEntryWindowsAsync(entry));
        var generation = 0;
        var keyboardNavigation = false;
        var delayedOpen = false;
        var needsIdentity = !isExplorer && (entry.ApplicationId is not null || entry.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase));
        var lastResolvedExplorer = isExplorer;
        policy.Prepare = () => ApplyIdentity(lastResolvedExplorer);
        menu.PreviewKeyDown += (_, _) => keyboardNavigation = true;
        menu.Opened += (_, _) =>
        {
            keyboardNavigation = false;
            if (delayedOpen)
            {
                delayedOpen = false;
                return;
            }
            policy.Prepare();
            RefreshMenuChoices(menu);
            _ = RefreshWindowsAsync(null, false);
        };
        menu.Closed += (_, _) =>
        {
            generation++;
            policy.HasWindows = false;
            RefreshMenuChoices(menu);
        };
        policy.OpenIfAvailable = (owner, usePointer) => _ = RefreshWindowsAsync(owner, usePointer);
        RefreshMenuChoices(menu);
        return menu;

        void AddRemove() => AddConfigured(menu, Remove, () =>
            CommitEntries(settings.Entries.Where(e => e.Id != entry.Id).ToList()));

        void ApplyIdentity(bool explorer)
        {
            isExplorer = explorer;
            policy.Category = isExplorer ? Explorer : DockMenuPreferences.App;
            launch.Tag = isExplorer ? Restart : DockMenuPreferences.Launch;
            launch.Header = ((Command)launch.Tag).Label;
            launch.IsEnabled = !isExplorer || !restartingExplorer;
        }

        async Task RefreshWindowsAsync(FrameworkElement? pendingOwner, bool usePointer)
        {
            var request = ++generation;
            var requestContext = contextRequest;
            var requestOpen = openGeneration;
            var foreground = Native.GetForegroundWindow();
            var cancelled = false;
            PreProcessInputEventHandler cancel = (_, input) =>
            {
                if (input.StagingItem.Input is KeyEventArgs { IsDown: true } ||
                    input.StagingItem.Input is MouseButtonEventArgs { ButtonState: MouseButtonState.Pressed }) cancelled = true;
            };
            if (pendingOwner is not null) InputManager.Current.PreProcessInput += cancel;
            try
            {
                var identityTask = needsIdentity
                    ? StaWorker.RunAsync(_ => ApplicationIdentityResolver.Shared.Resolve(entry), lifetime.Token)
                    : null;
                var countTask = Shows(settings, policy.Category, CloseWindows)
                    ? applicationWindows.CountAsync(entry, lifetime.Token)
                    : Task.FromResult(0);
                var identity = identityTask is null ? null : await identityTask;
                var resolvedExplorer = identity?.IsExplorer ?? isExplorer;
                var category = resolvedExplorer ? Explorer : DockMenuPreferences.App;
                var count = await countTask.WaitAsync(lifetime.Token);
                if (!Current()) return;
                lastResolvedExplorer = resolvedExplorer;
                if (category != policy.Category && Shows(settings, category, CloseWindows) &&
                    !Shows(settings, policy.Category, CloseWindows))
                    count = await applicationWindows.CountAsync(entry, lifetime.Token);
                if (!Current()) return;
                if (pendingOwner is null && (count > 0 || category != policy.Category))
                {
                    while (Current() && !keyboardNavigation && PointerOverMenu(menu))
                        await Task.Delay(40, lifetime.Token);
                    if (!Current() || keyboardNavigation) return;
                }
                if (pendingOwner is not null && (!pendingOwner.IsLoaded || (usePointer
                    ? !Native.GetCursorPos(out var pointer) ||
                        !ContextMenuDismissal.ContainsElement(pendingOwner, new Point(pointer.X, pointer.Y))
                    : !pendingOwner.IsKeyboardFocusWithin))) return;

                ApplyIdentity(resolvedExplorer);
                policy.HasWindows = count > 0;
                close.Header = count > 1 ? "Fermer toutes les fenêtres" : CloseWindows.Label;
                var visible = RefreshMenuChoices(menu);
                if (!visible && menu.IsOpen) menu.IsOpen = false;
                if (pendingOwner is not null && visible)
                {
                    delayedOpen = true;
                    menu.PlacementTarget = pendingOwner;
                    menu.IsOpen = true;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException) { }
            finally
            {
                if (pendingOwner is not null) InputManager.Current.PreProcessInput -= cancel;
            }

            bool Current() => request == generation && IsCurrentEntry(entry) &&
                (pendingOwner is null ? menu.IsOpen : !cancelled && requestContext == contextRequest &&
                    requestOpen == openGeneration && activeMenu is null && foreground == Native.GetForegroundWindow());
        }
    }

    private static bool PointerOverMenu(ContextMenu menu) =>
        Native.GetCursorPos(out var pointer) && ContextMenuDismissal.ContainsMenu(menu, new Point(pointer.X, pointer.Y));

    private async Task CloseEntryWindowsAsync(DockEntry entry)
    {
        if (!IsCurrentEntry(entry)) return;
        interactionLocks++;
        try
        {
            var failures = await applicationWindows.CloseAsync(entry, lifetime.Token);
            if (!closed && failures > 0)
                ShowNotice("Impossible de demander la fermeture de certaines fenêtres. L’accès peut être refusé par Windows.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        { if (!closed) ShowNotice("Impossible de demander la fermeture de l’application.\n\n" + ex.Message); }
        finally { interactionLocks--; lastRunningCheck = 0; }
    }

    private static FrameworkElement SettingsGlyph()
    {
        var gear = Geometry.Parse("M 20,5 L 28,5 29.5,10.5 33,12 38,9.5 43,15 40,20 41,23 46,25 45,32 39,33 36,36 36,42 29.5,45 26,40.5 22,40.5 18.5,45 12,42 12,36 9,33 3,32 2,25 7,23 8,20 5,15 10,9.5 15,12 18.5,10.5 Z M 24,16 A 9,9 0 1 0 24,34 A 9,9 0 1 0 24,16 Z");
        var glyph = new DrawingGroup();
        glyph.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(84, 144, 205)), null, gear));
        var ring = Geometry.Combine(new EllipseGeometry(new Point(24, 25), 12, 12),
            new EllipseGeometry(new Point(24, 25), 8.5, 8.5), GeometryCombineMode.Exclude, null);
        glyph.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(177, 220, 255)), null, ring));
        glyph.Freeze();
        return new Grid
        {
            Width = 48, Height = 48, IsHitTestVisible = false,
            Children = { new Image { Width = 42, Height = 42, Source = new DrawingImage(glyph) } }
        };
    }
}
