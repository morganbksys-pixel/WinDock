using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinDock.Core;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace WinDock;

public sealed partial class DockWindow
{
    private GroupPanel? groupPanel;
    private ContextMenuDismissal? groupDismissal;
    private DockEdge groupEdge;
    private DockEntry? activeDragEntry;
    private readonly GroupingHover groupingHover = new();
    private GroupingFeedback? dropFeedback;
    private Guid? hoveredGroup;
    private long groupHoverSince;

    private Button CreateEntryButton(DockEntry entry, bool grouped = false)
    {
        ImageSource? icon = null;
        if (!entry.IsSeparator && !entry.IsGroup) icon = EntryIcon(entry);
        var button = new Button
        {
            Width = entry.IsSeparator && Horizontal ? 22 : 60,
            Height = entry.IsSeparator && !Horizontal ? 22 : 60, Tag = entry,
            Focusable = false,
            Content = entry.IsGroup ? GroupTile(entry) : entry.IsSeparator ? new Border
            {
                Width = Horizontal ? 1 : 32, Height = Horizontal ? 32 : 1,
                Background = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)),
                CornerRadius = new CornerRadius(0.5)
            } : icon is null
                ? new TextBlock { Text = "◇", FontSize = 32 }
                : new Image { Source = icon, Width = 48, Height = 48, Stretch = Stretch.Uniform },
            ContextMenu = entry.IsGroup ? CreateGroupMenu(entry) : CreateMenu(entry)
        };
        if (!entry.IsSeparator && !entry.IsGroup)
        {
            var iconContent = (UIElement)button.Content;
            button.Content = null;
            var badge = new Ellipse
            {
                Width = 9, Height = 9, Fill = new SolidColorBrush(Color.FromRgb(65, 218, 119)),
                Stroke = new SolidColorBrush(Color.FromRgb(27, 30, 39)), StrokeThickness = 1.5,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 1, 1), IsHitTestVisible = false,
                Visibility = runningEntries.Contains(entry.Id) || missingEntries.Contains(entry.Id) ? Visibility.Visible : Visibility.Hidden
            };
            if (missingEntries.Contains(entry.Id)) badge.Fill = Brushes.Orange;
            button.Content = new Grid { Width = 48, Height = 48, Children = { iconContent, badge } };
            runningIndicators[entry.Id] = badge;
        }
        AttachLabel(button, entry.IsSeparator ? "Séparateur — glisser pour déplacer" : ManagedShortcuts.Name(entry));
        if (entry.IsGroup) button.Click += (_, _) => ToggleGroup(entry);
        else if (!entry.IsSeparator) button.Click += async (_, _) => await OpenEntryAsync(entry, button);
        button.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (groupPanel is { IsEditing: true } editing && !editing.FinishRename(true)) return;
            dragStart = e.GetPosition(this); pressedEntry = entry.Id;
        };
        button.PreviewMouseLeftButtonUp += (_, _) => pressedEntry = null;
        button.PreviewMouseMove += (_, e) =>
        {
            var current = e.GetPosition(this);
            if (e.LeftButton != MouseButtonState.Pressed || pressedEntry != entry.Id || dragging ||
                (Math.Abs(current.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                 Math.Abs(current.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)) return;
            dragging = true;
            activeDragEntry = entry;
            CloseLabels();
            pressedEntry = null;
            button.Opacity = 0.35;
            button.RenderTransformOrigin = new Point(0.5, 0.5);
            button.RenderTransform = new ScaleTransform(0.92, 0.92);
            try
            {
                if (DragRemoval.Run(button, this, new DataObject(DragFormat, entry.Id.ToString()), DragSurfaces, !entry.IsGroup))
                {
                    try { shortcuts.Commit(settings, settings.Entries.Where(e => e.Id != entry.Id).ToList()); RenderItems(); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowNotice(ex.Message); }
                }
            }
            finally
            {
                button.Opacity = 1; button.RenderTransform = Transform.Identity;
                items.ShowInsertion(null); groupPanel?.Items.ShowInsertion(null); dragging = false; activeDragEntry = null; hoveredGroup = null; ClearDropFeedback(); outsideSince = 0;
            }
        };
        if (grouped)
        {
            button.Width = double.NaN; button.Height = 100;
            var content = (UIElement)button.Content;
            button.Content = null;
            button.Content = new StackPanel
            {
                Children =
                {
                    content,
                    new TextBlock { Text = ManagedShortcuts.Name(entry), Foreground = Brushes.White, FontSize = 11, Width = 74, Height = 30, LineHeight = 15, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0) }
                }
            };
        }
        return button;
    }

    private ImageSource? EntryIcon(DockEntry entry)
    {
        if (!icons.TryGetValue(entry.Path, out var icon)) icons[entry.Path] = icon = Native.Icon(entry.Path);
        return icon;
    }

    private UIElement GroupTile(DockEntry entry)
    {
        var preview = new System.Windows.Controls.Primitives.UniformGrid { Rows = 2, Columns = 2, Width = 42, Height = 42 };
        foreach (var member in settings.Entries.Where(e => e.GroupId == entry.Id).Take(4))
            preview.Children.Add(EntryIcon(member) is { } icon
                ? new Image { Source = icon, Width = 19, Height = 19, Stretch = Stretch.Uniform }
                : new TextBlock { Text = "◇", Foreground = Brushes.White, FontSize = 18, TextAlignment = TextAlignment.Center });
        return new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
            Child = preview.Children.Count > 0 ? preview : new TextBlock { Text = "▦", Foreground = Brushes.White, FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
    }

    private bool DestinationExists(Guid? groupId) => groupId is null || settings.Entries.Any(e => e.Id == groupId && e.IsGroup);

    private bool CommitEntries(List<DockEntry> next)
    {
        try { shortcuts.Commit(settings, next); RenderItems(); lastRunningCheck = 0; return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowNotice("Impossible d’enregistrer cette modification.\n\n" + ex.Message); return false; }
    }

    private ContextMenu CreateGroupMenu(DockEntry group)
    {
        var menu = new ContextMenu { StaysOpen = false }; ConfigureMenu(menu);
        void Add(string name, Action action)
        {
            var item = new MenuItem { Header = name };
            item.Click += (_, _) => { menu.IsOpen = false; action(); }; menu.Items.Add(item);
        }
        Add("Ajouter…", () => OpenCatalog(group.Id));
        Add("Renommer…", () => RenameGroup(group));
        Add("Dissoudre le groupe", () => { CloseGroup(); CommitEntries(EntryRules.Dissolve(settings.Entries, group.Id)); });
        return menu;
    }

    private MenuItem CreateMoveMenu(DockEntry entry)
    {
        var move = new MenuItem { Header = "Déplacer vers…" };
        void Add(string name, Guid? groupId)
        {
            var target = new MenuItem { Header = name, IsEnabled = entry.GroupId != groupId };
            target.Click += (_, _) => { if (activeMenu is not null) activeMenu.IsOpen = false; CommitEntries(EntryRules.MoveTo(settings.Entries, entry.Id, groupId)); };
            move.Items.Add(target);
        }
        Add("Dock", null);
        foreach (var group in settings.Entries.Where(e => e.IsGroup)) Add(ManagedShortcuts.Name(group), group.Id);
        return move;
    }

    private void RenameGroup(DockEntry group)
    {
        if (groupPanel?.GroupId != group.Id) OpenGroup(group);
        groupPanel?.BeginRename();
    }

    internal bool CreateGroupFromDrop(Guid source, Guid target)
    {
        var id = Guid.NewGuid();
        var next = EntryRules.GroupPair(settings.Entries, source, target, id);
        if (!next.Any(e => e.Id == id) || !CommitEntries(next)) return false;
        OpenGroup(settings.Entries.Single(e => e.Id == id));
        return true;
    }

    internal void ToggleGroup(DockEntry group)
    {
        if (groupPanel?.GroupId == group.Id) { CloseGroup(); return; }
        OpenGroup(group);
    }

    private void OpenGroup(DockEntry group)
    {
        if (!IsCurrentEntry(group) || !group.IsGroup || display is null) return;
        if (groupPanel is { } existing && dragging)
        {
            existing.GroupId = group.Id; RefreshGroupPanel(); return;
        }
        CloseGroup();
        CloseLabels();
        if (activeMenu is not null) activeMenu.IsOpen = false;
        var panel = new GroupPanel(group.Id, () => OpenCatalog(groupPanel?.GroupId), name =>
        {
            var id = groupPanel?.GroupId;
            return id is not null && CommitEntries(settings.Entries.Select(e => e.Id == id ? e with { DisplayName = name } : e).ToList());
        }, dockBackground) { Owner = this };
        groupPanel = panel; groupEdge = settings.Edge; interactionLocks++;
        panel.PreviewDragOver += GroupDragOver;
        panel.PreviewDrop += GroupDrop;
        panel.DragLeave += (_, _) => panel.Items.ShowInsertion(null);
        panel.Closed += (_, _) =>
        {
            if (groupPanel != panel) return;
            groupDismissal?.Dispose(); groupDismissal = null; groupPanel = null;
            openGeneration++; interactionLocks--; outsideSince = 0;
        };
        RefreshGroupPanel(); panel.Show(); PlaceGroupPanel();
        try
        {
            groupDismissal = new ContextMenuDismissal(panel, hwnd,
                point => dragging || externalDrag || items.Children.OfType<Button>().Any(b => b.Tag is DockEntry e && e.Id == panel.GroupId && ContextMenuDismissal.ContainsElement(b, point)) || activeMenu is not null && ContextMenuDismissal.ContainsMenu(activeMenu, point),
                handle => activeMenu is not null && ContextMenuDismissal.OwnsMenu(activeMenu, handle), CloseGroup);
        }
        catch (System.ComponentModel.Win32Exception ex) { CloseGroup(); ShowNotice("Impossible d’ouvrir le groupe.\n\n" + ex.Message); }
    }

    internal void CloseGroup()
    {
        if (groupPanel is null) return;
        if (!groupPanel.FinishRename(!closed)) return;
        if (activeMenu is not null) activeMenu.IsOpen = false;
        groupPanel.Close();
    }

    private void RefreshGroupPanel()
    {
        if (groupPanel is not { } panel || display is null) return;
        var group = settings.Entries.FirstOrDefault(e => e.Id == panel.GroupId && e.IsGroup);
        if (group is null) { CloseGroup(); return; }
        var offset = panel.Scroll.VerticalOffset;
        panel.Items.Children.Clear();
        var members = settings.Entries.Where(e => e.GroupId == group.Id).ToArray();
        foreach (var member in members) panel.Items.Children.Add(CreateEntryButton(member, true));
        panel.Configure(ManagedShortcuts.Name(group), display.Work.Width / display.Scale, display.Work.Height / display.Scale, members.Length);
        panel.UpdateLayout(); panel.Scroll.ScrollToVerticalOffset(offset);
        if (panel.IsVisible) PlaceGroupPanel();
    }

    private void PlaceGroupPanel()
    {
        if (groupPanel is not { IsVisible: true } panel || display is null) return;
        var button = items.Children.OfType<Button>().FirstOrDefault(b => b.Tag is DockEntry e && e.Id == panel.GroupId);
        if (button is null) { CloseGroup(); return; }
        panel.UpdateLayout();
        var start = button.PointToScreen(new Point());
        var end = button.PointToScreen(new Point(button.ActualWidth, button.ActualHeight));
        var bounds = GroupGeometry.Place(display.Work, new PixelRect(start.X, start.Y, end.X - start.X, end.Y - start.Y), settings.Edge, panel.ActualWidth * display.Scale, panel.ActualHeight * display.Scale, display.Scale);
        Native.Position(new WindowInteropHelper(panel).Handle, bounds);
    }

    private IReadOnlyList<Window> DragSurfaces() => groupPanel is { IsVisible: true } panel ? [this, panel] : [this];

    private DockEntry? DropTargetUnderPointer(DragEventArgs e) => items.Children.OfType<Button>().FirstOrDefault(b =>
        b.Tag is DockEntry entry && EligibleDropTarget(entry) && GroupingHover.IsCenter(b.RenderSize, e.GetPosition(b)))?.Tag as DockEntry;

    private DockEntry? GroupUnderPointer(DragEventArgs e) => DropTargetUnderPointer(e) is { IsGroup: true } group ? group : null;

    private bool EligibleDropTarget(DockEntry entry) => !entry.IsSeparator && entry.GroupId is null &&
        activeDragEntry is not ({ IsGroup: true } or { IsSeparator: true }) && entry.Id != activeDragEntry?.Id &&
        (entry.IsGroup || activeDragEntry is not null);

    private void UpdateGroupHover()
    {
        if ((!dragging && !externalDrag) || !Native.GetCursorPos(out var cursor)) { ClearDropFeedback(); hoveredGroup = null; return; }
        var point = new Point(cursor.X, cursor.Y);
        var target = items.Children.OfType<Button>().FirstOrDefault(b => b.Tag is DockEntry entry && EligibleDropTarget(entry) &&
            GroupingHover.IsCenter(b.RenderSize, b.PointFromScreen(point)))?.Tag as DockEntry;
        UpdateDropHover(target, Environment.TickCount64);
    }

    internal void UpdateDropHover(DockEntry? target, long now)
    {
        if (target is not null && !EligibleDropTarget(target)) target = null;
        groupingHover.Update(activeDragEntry?.Id, target, now);
        if (target is null) { ClearDropFeedback(); hoveredGroup = null; return; }
        var button = items.Children.OfType<Button>().FirstOrDefault(b => b.Tag is DockEntry entry && entry.Id == target.Id);
        if (button is not null && display is not null)
        {
            dropFeedback ??= new GroupingFeedback { Owner = this };
            var first = button.PointToScreen(new Point());
            var last = button.PointToScreen(new Point(button.ActualWidth, button.ActualHeight));
            dropFeedback.Update(new PixelRect(first.X, first.Y, last.X - first.X, last.Y - first.Y), display,
                target.IsGroup ? "Ajouter à « " + ManagedShortcuts.Name(target) + " »" : groupingHover.IsReady(now) ? "Relâcher pour créer un groupe" : "Maintenir pour grouper",
                target.IsGroup || groupingHover.IsReady(now), activeDragEntry is { } source ? EntryIcon(source) : null,
                target.IsGroup ? null : EntryIcon(target));
        }
        if (target.IsGroup) TrackGroupHover(target, now);
        else
        {
            hoveredGroup = null;
            if (groupingHover.IsReady(now)) items.ShowInsertion(null);
        }
    }

    private void ClearDropFeedback()
    {
        groupingHover.Clear(); dropFeedback?.Close(); dropFeedback = null;
    }

    internal void TrackGroupHover(DockEntry? group, long now)
    {
        if (group is null) { hoveredGroup = null; return; }
        if (hoveredGroup != group.Id) { hoveredGroup = group.Id; groupHoverSince = now; }
        if (now - groupHoverSince >= 500 && groupPanel?.GroupId != group.Id) OpenGroup(group);
    }

    private void GroupDragOver(object sender, DragEventArgs e)
    {
        ClearDropFeedback();
        externalDrag = e.Data.GetDataPresent(DataFormats.FileDrop);
        var valid = externalDrag || e.Data.GetDataPresent(DragFormat) && activeDragEntry is { IsSeparator: false, IsGroup: false };
        e.Effects = valid ? (externalDrag ? DragDropEffects.Copy : DragDropEffects.Move) : DragDropEffects.None;
        e.Handled = true;
        if (groupPanel is { } panel)
        {
            if (valid)
            {
                var cursor = e.GetPosition(panel.Scroll);
                if (cursor.Y < 24) panel.Scroll.LineUp();
                else if (cursor.Y > panel.Scroll.ActualHeight - 24) panel.Scroll.LineDown();
            }
            panel.Items.ShowInsertion(valid ? panel.Items.FindInsertion(e.GetPosition(panel.Items)) : null);
        }
    }

    private void GroupDrop(object sender, DragEventArgs e)
    {
        e.Handled = true; externalDrag = false;
        if (groupPanel is not { } panel) return;
        var members = settings.Entries.Where(x => x.GroupId == panel.GroupId).ToArray();
        var index = panel.Items.FindInsertion(e.GetPosition(panel.Items));
        Guid? before = index < members.Length ? members[index].Id : null;
        panel.Items.ShowInsertion(null);
        DropEntries(e.Data, panel.GroupId, before);
    }

    internal void DropEntries(IDataObject data, Guid? groupId, Guid? before = null)
    {
        if (!DestinationExists(groupId)) return;
        if (data.GetData(DragFormat) is string text && Guid.TryParse(text, out var source))
            CommitEntries(EntryRules.MoveTo(settings.Entries, source, groupId, before));
        else if (data.GetData(DataFormats.FileDrop) is string[] paths) AddPaths(paths, before, groupId);
    }
}
