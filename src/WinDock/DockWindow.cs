using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;
using Microsoft.Win32;
using WinDock.Core;

namespace WinDock;

public sealed partial class DockWindow : Window
{
    private const string DragFormat = "WinDock.EntryId";
    private readonly DockSettings settings;
    private readonly SettingsStore store;
    private readonly InsertionPanel items = new();
    private readonly Viewbox fittedItems = new() { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly Dictionary<string, ImageSource?> icons = new(StringComparer.OrdinalIgnoreCase);
    private nint hwnd;
    private Display? display;
    private DockPlacement placement;
    private long triggerSince, outsideSince, lastDisplayCheck;
    private bool revealed = true, dragging, externalDrag;
    private int interactionLocks;
    private Point dragStart;
    private Guid? pressedEntry;
    private Window? settingsWindow;
    private IndicatorWindow? indicator;
    private PixelRect indicatorArea;
    private readonly SolidColorBrush dockBackground = new(Color.FromRgb(27, 30, 39));
    private ContextMenu? activeMenu;
    private ContextMenuDismissal? menuDismissal;
    private readonly RunningApplications runningApplications = new();
    private readonly IApplicationWindows applicationWindows;
    private readonly IDefaultDockService defaultDock;
    private bool restoringDefaults;
    private readonly HashSet<Guid> openingEntries = [];
    private int openGeneration;
    private readonly Dictionary<Guid, Ellipse> runningIndicators = [];
    private HashSet<Guid> runningEntries = [];
    private bool checkingRunning, closed;
    private long lastRunningCheck;
    private readonly ManagedShortcuts shortcuts;
    private HashSet<Guid> missingEntries = [];
    private readonly CancellationTokenSource lifetime = new();
    private bool checkingPresence;
    private long lastPresenceCheck;
    private CatalogWindow? catalogWindow;
    private readonly IRecycleBinService recycleBin;
    private RecycleBinSnapshot recycleState = new(null, null);
    private Button? recycleButton;
    private bool checkingRecycle, emptyingRecycle;
    private long lastRecycleCheck;

    public DockWindow(DockSettings settings, SettingsStore store) : this(settings, store, new RecycleBinService()) { }

    internal DockWindow(DockSettings settings, SettingsStore store, IRecycleBinService recycleBin,
        IApplicationWindows? applicationWindows = null, IDefaultDockService? defaultDock = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);
        this.settings = settings;
        this.store = store;
        this.recycleBin = recycleBin;
        this.applicationWindows = applicationWindows ?? new ApplicationWindows();
        this.defaultDock = defaultDock ?? new DefaultDockService(store);
        shortcuts = new ManagedShortcuts(store);
        dockBackground.Opacity = settings.BackgroundOpacity;
        Title = "WinDock";
        var iconDecoder = new System.Windows.Media.Imaging.IconBitmapDecoder(
            new Uri("pack://application:,,,/WinDock;component/Assets/WinDock.ico"),
            System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        Icon = iconDecoder.Frames.MaxBy(frame => frame.PixelWidth);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowDrop = true;
        fittedItems.Child = items;
        Content = DockAppearance.Surface(dockBackground, fittedItems, new Thickness(8));
        ContextMenu = CreateMenu(null);
        PreviewDragOver += OnDragOver;
        Drop += OnDrop;
        DragLeave += (_, e) =>
        {
            if (!new Rect(RenderSize).Contains(e.GetPosition(this)))
            { items.ShowInsertion(null); ClearDropFeedback(); hoveredGroup = null; }
        };
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            Native.MakeNonActivating(hwnd);
            HwndSource.FromHwnd(hwnd)?.AddHook(WindowMessage);
            RefreshPlacement();
            timer.Start();
        };
        timer.Tick += (_, _) => Tick();
        Loaded += (_, _) =>
        {
            if (indicator is not null) return;
            indicator = new IndicatorWindow { Owner = this };
            RefreshPlacement();
            indicator.Show();
        };
        Closed += (_, _) => { closed = true; ClearDropFeedback(); CloseGroup(); CloseLabels(); lifetime.Cancel(); timer.Stop(); if (activeMenu is not null) activeMenu.IsOpen = false; menuDismissal?.Dispose(); indicator?.Close(); settingsWindow?.Close(); };
        RenderItems();
    }

    private bool Horizontal => settings.Edge is DockEdge.Bottom or DockEdge.Top;
    private nint WindowMessage(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x21) { handled = true; return 3; } // MA_NOACTIVATE, preserving mouse clicks.
        if (message == 0x84 && !revealed) { handled = true; return -1; } // HTTRANSPARENT
        if (message is 0x7E or 0x1A or 0x02E0)
            Dispatcher.BeginInvoke(RefreshPlacement, DispatcherPriority.Loaded);
        return 0;
    }

    private void RefreshPlacement()
    {
        var previousDisplay = display;
        var displays = Native.Displays();
        display = displays.FirstOrDefault(d => d.Device == settings.MonitorDevice)
            ?? displays.FirstOrDefault(d => d.Primary) ?? displays.FirstOrDefault();
        if (groupPanel is not null && (display is null || display.Device != previousDisplay?.Device || display.Work != previousDisplay.Work || display.Scale != previousDisplay.Scale || settings.Edge != groupEdge)) CloseGroup();
        if (display is null) return;
        // Measure the actual content rather than duplicating its dimensions.
        items.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var contentSize = items.DesiredSize;
        const double frame = 18; // Padding and border on both sides.
        const double outerMargins = 16;
        var availableWidth = Math.Max(1, display.Work.Width / display.Scale - outerMargins - frame);
        var availableHeight = Math.Max(1, display.Work.Height / display.Scale - outerMargins - frame);
        var fit = Math.Min(1, Math.Min(availableWidth / Math.Max(1, contentSize.Width),
            availableHeight / Math.Max(1, contentSize.Height)));
        var width = contentSize.Width * fit + frame;
        var height = contentSize.Height * fit + frame;
        placement = DockGeometry.Calculate(display.Work, settings.Edge, width, height, display.Scale);
        indicatorArea = DockGeometry.IndicatorArea(display.Work, settings.Edge, display.Scale);
        indicator?.Place(indicatorArea, settings.Edge, display.Scale);
        // WPF sizes use DIPs; native positioning uses physical pixels on the selected monitor.
        Width = placement.Window.Width / display.Scale;
        Height = placement.Window.Height / display.Scale;
        if (hwnd != 0) Native.Position(hwnd, placement.Window);
    }

    private void Tick()
    {
        menuDismissal?.Poll();
        if (!dragging && !externalDrag && groupPanel is { } openPanel)
            groupDismissal?.Poll(Native.GetForegroundWindow(), openPanel.ShouldDismissOnEscape((Native.GetAsyncKeyState(0x1B) & 0x8000) != 0));
        UpdateGroupHover();
        var now = Environment.TickCount64;
        if (settings.ShowRecycleBin && !checkingRecycle && !emptyingRecycle && now - lastRecycleCheck >= 2000)
            _ = RefreshRecycleAsync();
        if (!checkingPresence && now - lastPresenceCheck > 30000 && settings.Entries.Any(e => e.ApplicationId is not null))
        {
            lastPresenceCheck = now;
            _ = RefreshPresenceAsync();
        }
        if (!checkingRunning && now - lastRunningCheck >= 1500)
        {
            lastRunningCheck = now;
            _ = RefreshRunningAsync();
        }
        if (now - lastDisplayCheck > 1000)
        {
            lastDisplayCheck = now;
            RefreshPlacement();
        }
        if (display is null || !Native.GetCursorPos(out var pointer)) return;
        var fullScreen = Native.FullScreen(display, hwnd);
        var overIndicator = indicatorArea.Contains(pointer.X, pointer.Y);
        var proximity = 40 * display.Scale;
        var nearIndicator = new PixelRect(indicatorArea.X - proximity, indicatorArea.Y - proximity,
            indicatorArea.Width + proximity * 2, indicatorArea.Height + proximity * 2).Contains(pointer.X, pointer.Y);
        indicator?.SetAppearance(settings.AutoHide && !revealed && !fullScreen, nearIndicator, overIndicator);
        if (externalDrag && (Native.GetAsyncKeyState(1) & 0x8000) == 0 && (Native.GetAsyncKeyState(2) & 0x8000) == 0) externalDrag = false;
        UpdateVisibility(fullScreen, overIndicator || placement.Trigger.Contains(pointer.X, pointer.Y),
            overIndicator || DockGeometry.InteractionArea(placement).Contains(pointer.X, pointer.Y), now);
    }

    internal void UpdateVisibility(bool fullScreen, bool overTrigger, bool overDock, long now)
    {
        if (fullScreen) CloseGroup();
        if (interactionLocks > 0 || dragging || externalDrag) { outsideSince = triggerSince = 0; return; }
        if (fullScreen)
        {
            SetRevealed(false);
            outsideSince = triggerSince = 0;
            return;
        }
        if (!settings.AutoHide) { SetRevealed(true); outsideSince = triggerSince = 0; return; }
        if (!revealed)
        {
            if (overTrigger)
            {
                if (triggerSince == 0) triggerSince = now;
                if (now - triggerSince >= 150) SetRevealed(true);
            }
            else triggerSince = 0;
        }
        else if (overDock) outsideSince = 0;
        else
        {
            if (outsideSince == 0) outsideSince = now;
            if (now - outsideSince >= 600) SetRevealed(false);
        }
    }

    private void SetRevealed(bool value)
    {
        if (revealed == value) return;
        revealed = value;
        if (!value) CloseLabels();
        IsHitTestVisible = value;
        triggerSince = outsideSince = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(value ? 1 : 0, TimeSpan.FromMilliseconds(150)));
    }

    private void RenderItems()
    {
        CloseLabels();
        recycleButton = null;
        runningIndicators.Clear();
        items.ShowInsertion(null);
        items.Children.Clear();
        items.Orientation = Horizontal ? Orientation.Horizontal : Orientation.Vertical;
        if (settings.Entries.Count == 0 && !settings.ShowRecycleBin)
        {
            items.Children.Add(new TextBlock
            {
                Text = "Clic droit pour ajouter", Foreground = Brushes.LightGray,
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Width = Horizontal ? 236 : 58, Margin = new Thickness(2, 8, 2, 8), FontSize = 12
            });
        }
        foreach (var entry in settings.Entries.Where(e => e.GroupId is null)) items.Children.Add(CreateEntryButton(entry));
        if (settings.ShowRecycleBin)
        {
            recycleButton = new Button { Width = 60, Height = 60, Focusable = false, ContextMenu = CreateMenu(null, true) };
            recycleButton.Click += async (_, _) => await RunRecycleActionAsync(false);
            items.Children.Add(recycleButton);
            UpdateRecycleButton();
        }
        if (hwnd != 0) RefreshPlacement();
        RefreshGroupPanel();
    }

    private void AttachLabel(Button button, string text) => DockLabels.Attach(button, text,
        () => revealed && !closed && activeMenu is null && !dragging && !externalDrag);

    private void CloseLabels()
    {
        foreach (var button in items.Children.OfType<Button>().Concat(groupPanel?.Items.Children.OfType<Button>() ?? []))
            if (button.ToolTip is ToolTip label) label.IsOpen = false;
    }

    private void UpdateRecycleButton()
    {
        if (recycleButton is null) return;
        recycleButton.Content = recycleState.Icon is { } icon
            ? new Image { Source = icon, Width = 48, Height = 48, Stretch = Stretch.Uniform }
            : new TextBlock { Text = "♻", FontSize = 32 };
        if (recycleButton.Content is Image image) RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var label = recycleState.IsEmpty switch { true => "Corbeille vide", false => "Corbeille pleine", _ => "Corbeille — état indisponible" };
        if (recycleButton.ToolTip is ToolTip { Content: Border { Child: TextBlock text } }) text.Text = label;
        else AttachLabel(recycleButton, label);
    }

    internal async Task RefreshRecycleAsync()
    {
        if (checkingRecycle || emptyingRecycle || closed || !settings.ShowRecycleBin) return;
        checkingRecycle = true;
        try
        {
            var result = await StaWorker.RunAsync(_ => recycleBin.Read(), lifetime.Token);
            if (closed || !settings.ShowRecycleBin) return;
            if (result.IsEmpty is not null || recycleState.IsEmpty is null)
            {
                var updated = result with { Icon = result.Icon ?? recycleState.Icon };
                if (updated != recycleState) { recycleState = updated; UpdateRecycleButton(); }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
        finally { checkingRecycle = false; lastRecycleCheck = Environment.TickCount64; }
    }

    private async Task RunRecycleActionAsync(bool empty)
    {
        if (emptyingRecycle) return;
        emptyingRecycle = true;
        interactionLocks++;
        try
        {
            await StaWorker.RunAsync(_ => { if (empty) recycleBin.Empty(hwnd); else recycleBin.Open(); return true; }, lifetime.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { if (!closed) ShowNotice("Impossible d’accéder à la corbeille.\n\n" + ex.Message); }
        finally { emptyingRecycle = false; interactionLocks--; lastRecycleCheck = 0; }
        await RefreshRecycleAsync();
    }

    private ContextMenu CreateMenu(DockEntry? entry, bool isRecycleBin = false)
    {
        var menu = new ContextMenu { StaysOpen = false };
        MenuItem? closeItem = null;
        var generation = 0;
        ConfigureMenu(menu);
        menu.Opened += async (_, _) =>
        {
            var request = ++generation;
            if (closeItem is null || entry is null || !menu.IsOpen) return;
            menu.Items.Remove(closeItem);
            try
            {
                var count = await applicationWindows.CountAsync(entry, lifetime.Token);
                if (closed || !menu.IsOpen || request != generation || !settings.Entries.Contains(entry)) return;
                if (count > 0)
                {
                    closeItem.Header = count == 1 ? "Fermer l’application" : "Fermer toutes les fenêtres";
                    menu.Items.Insert(0, closeItem);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
            { /* Unavailable windows must not expose a stale close command. */ }
        };
        menu.Closed += (_, _) => generation++;
        void Item(string title, Action action)
        {
            var item = new MenuItem { Header = title };
            item.Click += (_, _) => { menu.IsOpen = false; action(); };
            menu.Items.Add(item);
        }
        if (isRecycleBin)
        {
            Item("Ouvrir", async () => await RunRecycleActionAsync(false));
            Item("Vider la corbeille…", async () => await RunRecycleActionAsync(true));
            menu.Items.Add(new Separator());
        }
        if (entry is not null)
        {
            if (!entry.IsSeparator)
            {
                closeItem = new MenuItem();
                closeItem.Click += async (_, _) =>
                {
                    menu.IsOpen = false;
                    interactionLocks++;
                    try
                    {
                        var failures = await applicationWindows.CloseAsync(entry, lifetime.Token);
                        if (!closed && failures > 0)
                            ShowNotice("Impossible de demander la fermeture de certaines fenêtres. L’accès peut être refusé par Windows.");
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
                    { if (!closed) ShowNotice("Impossible de demander la fermeture de l’application.\n\n" + ex.Message); }
                    finally { interactionLocks--; lastRunningCheck = 0; }
                };
            }
            if (!entry.IsSeparator)
            {
                Item("Lancer à nouveau", () => Launch(entry));
                Item("Modifier / réparer…", () => EditEntry(entry));
            }
            Item("Retirer du dock", () =>
            {
                try { shortcuts.Commit(settings, settings.Entries.Where(e => e.Id != entry.Id).ToList()); RenderItems(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowNotice(ex.Message); }
            });
            menu.Items.Add(new Separator());
        }
        if (entry is { IsSeparator: false } && settings.Entries.Any(e => e.IsGroup)) menu.Items.Add(CreateMoveMenu(entry));
        Item("Ajouter…", () => OpenCatalog(entry?.GroupId));
        if (entry?.GroupId is null) Item("Ajouter un séparateur", () =>
        {
            var index = entry is null ? settings.Entries.Count : settings.Entries.FindIndex(e => e.Id == entry.Id) + 1;
            var next = settings.Entries.ToList(); next.Insert(index, new(Guid.NewGuid(), "", true));
            CommitEntries(next);
        });
        menu.Items.Add(new Separator());
        Item("Réglages…", OpenSettings);
        Item("Quitter WinDock", Close);
        return menu;
    }

    private void ConfigureMenu(ContextMenu menu)
    {
        ContextMenuDismissal? dismissal = null;
        menu.Opened += (_, _) =>
        {
            openGeneration++;
            CloseLabels();
            if (activeMenu is not null && activeMenu != menu) activeMenu.IsOpen = false;
            activeMenu = menu;
            interactionLocks++;
            try { menuDismissal = dismissal = new ContextMenuDismissal(menu, hwnd); }
            catch (System.ComponentModel.Win32Exception ex)
            {
                menu.IsOpen = false;
                ShowNotice("Impossible d’ouvrir le menu.\n\n" + ex.Message);
            }
        };
        menu.Closed += (_, _) =>
        {
            dismissal?.Dispose(); dismissal = null;
            if (activeMenu == menu) { menuDismissal = null; activeMenu = null; }
            interactionLocks--; outsideSince = 0;
        };
    }

    private bool IsCurrentEntry(DockEntry entry) => !closed && settings.Entries.Contains(entry);

    internal async Task OpenEntryAsync(DockEntry entry, Button button)
    {
        if (!IsCurrentEntry(entry) || entry.IsSeparator || entry.IsGroup || !openingEntries.Add(entry.Id)) return;
        if (entry.GroupId is null) CloseGroup();
        var request = ++openGeneration;
        interactionLocks++;
        try
        {
            if (shortcuts.IsMissing(entry)) { OfferRepair(entry); return; }
            if (Directory.Exists(entry.Path)) { Launch(entry); return; }
            if (activeMenu is not null) activeMenu.IsOpen = false;
            var windows = await applicationWindows.ListAsync(entry, lifetime.Token);
            if (!IsCurrentEntry(entry) || !button.IsLoaded || request != openGeneration) return;
            if (windows.Count == 0) { CloseGroup(); Launch(entry); }
            else if (windows.Count == 1) await ActivateEntryWindowAsync(entry, windows[0]);
            else ShowWindowMenu(entry, button, windows);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (IsCurrentEntry(entry) && button.IsLoaded && request == openGeneration)
                ShowWindowMenu(entry, button, [], true);
        }
        finally { openingEntries.Remove(entry.Id); interactionLocks--; outsideSince = 0; }
    }

    private async Task ActivateEntryWindowAsync(DockEntry entry, ApplicationWindow window)
    {
        CloseGroup();
        if (!IsCurrentEntry(entry)) return;
        interactionLocks++;
        try
        {
            var result = await applicationWindows.ActivateAsync(entry, window, lifetime.Token);
            if (!IsCurrentEntry(entry)) return;
            if (result == WindowActivationResult.Unavailable) ShowNotice("Cette fenêtre n’est plus disponible.");
            else if (result == WindowActivationResult.Refused) ShowNotice("Windows n’a pas autorisé le passage de cette fenêtre au premier plan.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        { if (IsCurrentEntry(entry)) ShowNotice("Impossible d’activer cette fenêtre.\n\n" + ex.Message); }
        finally { interactionLocks--; outsideSince = 0; }
    }

    private void ShowWindowMenu(DockEntry entry, Button button, IReadOnlyList<ApplicationWindow> windows, bool failed = false)
    {
        var menu = new ContextMenu
        {
            StaysOpen = false, PlacementTarget = button,
            Placement = settings.Edge switch
            {
                DockEdge.Bottom => System.Windows.Controls.Primitives.PlacementMode.Top,
                DockEdge.Top => System.Windows.Controls.Primitives.PlacementMode.Bottom,
                DockEdge.Left => System.Windows.Controls.Primitives.PlacementMode.Right,
                _ => System.Windows.Controls.Primitives.PlacementMode.Left
            }
        };
        ConfigureMenu(menu);
        if (failed) menu.Items.Add(new MenuItem { Header = "Recherche des fenêtres indisponible", IsEnabled = false });
        foreach (var window in windows)
        {
            var item = new MenuItem
            {
                Header = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(window.Title) ? "Fenêtre sans titre" : window.Title,
                    MaxWidth = 400, TextTrimming = TextTrimming.CharacterEllipsis
                },
                ToolTip = window.Title
            };
            item.Click += async (_, _) =>
            {
                menu.IsOpen = false;
                await ActivateEntryWindowAsync(entry, window);
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var launch = new MenuItem { Header = "Lancer à nouveau" };
        launch.Click += (_, _) => { menu.IsOpen = false; if (IsCurrentEntry(entry)) Launch(entry); };
        menu.Items.Add(launch);
        menu.IsOpen = true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        CloseLabels();
        externalDrag = e.Data.GetDataPresent(DataFormats.FileDrop);
        UpdateDropHover(DropTargetUnderPointer(e), Environment.TickCount64);
        if (externalDrag && IsOverRecycleBin(e))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            items.ShowInsertion(null);
            return;
        }
        e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move
            : e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        outsideSince = 0;
        if (e.Effects != DragDropEffects.None && GroupUnderPointer(e) is null && !groupingHover.IsReady(Environment.TickCount64))
        {
            var (_, offset) = FindInsertion(e.GetPosition(items));
            items.ShowInsertion(offset);
        }
        else items.ShowInsertion(null);
    }

    private (Guid? Before, double Offset) FindInsertion(Point point)
    {
        double offset = 0;
        foreach (var button in items.Children.OfType<Button>().Where(b => b.Tag is DockEntry))
        {
            var length = Horizontal ? button.ActualWidth : button.ActualHeight;
            if ((Horizontal ? point.X : point.Y) < offset + length / 2)
                return (((DockEntry)button.Tag).Id, offset);
            offset += length;
        }
        return (null, offset);
    }

    private bool IsOverRecycleBin(DragEventArgs e) => recycleButton is not null &&
        new Rect(recycleButton.RenderSize).Contains(e.GetPosition(recycleButton));

    private void OnDrop(object sender, DragEventArgs e)
    {
        var pairTarget = groupingHover.Target;
        var pairReady = groupingHover.IsReady(Environment.TickCount64);
        externalDrag = false;
        items.ShowInsertion(null);
        e.Handled = true;
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && IsOverRecycleBin(e)) return;
        interactionLocks++;
        try
        {
            var underPointer = DropTargetUnderPointer(e);
            if (pairReady && pairTarget is { IsGroup: false } && underPointer?.Id == pairTarget.Id && activeDragEntry is { } dragged)
            {
                ClearDropFeedback();
                CreateGroupFromDrop(dragged.Id, pairTarget.Id);
                return;
            }
            ClearDropFeedback();
            var (before, _) = FindInsertion(e.GetPosition(items));
            var destination = GroupUnderPointer(e);
            DropEntries(e.Data, destination?.Id, destination is null ? before : null);
        }
        finally { interactionLocks--; outsideSince = 0; }
    }

    private void AddPaths(IEnumerable<string> paths, Guid? before = null, Guid? groupId = null)
    {
        if (!DestinationExists(groupId)) return;
        var rejected = new List<string>();
        foreach (var path in paths)
        {
            if (!EntryRules.IsSupported(path)) { rejected.Add(Path.GetFileName(path)); continue; }
            var fullPath = Path.GetFullPath(path);
            if (settings.Entries.Any(e => string.Equals(e.SourcePath ?? e.Path, fullPath, StringComparison.OrdinalIgnoreCase))) continue;
            var index = before is null ? -1 : settings.Entries.FindIndex(e => e.Id == before);
            try
            {
                var added = shortcuts.Import(new(Guid.NewGuid(), fullPath, GroupId: groupId));
                var entries = settings.Entries.ToList();
                entries.Insert(index < 0 ? entries.Count : index, added);
                shortcuts.Commit(settings, entries);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowNotice(ex.Message); }
        }
        RenderItems();
        if (rejected.Count > 0) ShowNotice("Seuls les raccourcis Windows (.lnk), exécutables (.exe) et dossiers sont acceptés.\n\nÉléments ignorés : " + string.Join(", ", rejected));
    }

    private void PickApplication(Window? owner = null, Guid? groupId = null)
    {
        interactionLocks++;
        try
        {
            var picker = new OpenFileDialog { Title = "Ajouter une application ou un raccourci", Filter = "Applications et raccourcis|*.exe;*.lnk", Multiselect = true, DereferenceLinks = false };
            if (picker.ShowDialog(owner ?? this) == true) AddPaths(picker.FileNames, groupId: groupId);
        }
        finally { interactionLocks--; }
    }

    private void PickFolder(Window? owner = null, Guid? groupId = null)
    {
        interactionLocks++;
        try
        {
            var picker = new OpenFolderDialog { Title = "Ajouter un dossier", Multiselect = true };
            if (picker.ShowDialog(owner ?? this) == true) AddPaths(picker.FolderNames, groupId: groupId);
        }
        finally { interactionLocks--; }
    }

    private void OpenCatalog(Guid? groupId = null)
    {
        if (!DestinationExists(groupId)) return;
        CloseGroup();
        if (catalogWindow is not null) { catalogWindow.Activate(); return; }
        interactionLocks++;
        try
        {
            catalogWindow = new CatalogWindow(() => settings.Entries.ToArray(), async app =>
            {
                var entries = settings.Entries.ToArray();
                var keys = await StaWorker.RunAsync(_ => ApplicationCatalog.ExistingKeys(entries), lifetime.Token);
                if (ApplicationCatalog.IsAdded(app, keys)) return;
                var imported = await StaWorker.RunAsync(_ => shortcuts.ImportApplication(app), lifetime.Token);
                if (!DestinationExists(groupId)) { shortcuts.Discard(imported); return; }
                shortcuts.Commit(settings, [..settings.Entries, imported with { GroupId = groupId }]);
                lastRunningCheck = 0;
                RenderItems();
            }, owner => PickApplication(owner, groupId), owner => PickFolder(owner, groupId), () =>
            {
                shortcuts.Commit(settings, [..settings.Entries, new DockEntry(Guid.NewGuid(), "", true)]);
                RenderItems();
            }) { Owner = this };
            if (groupId is not null)
            {
                catalogWindow.SeparatorButton.Visibility = Visibility.Collapsed;
                catalogWindow.Title = "Ajouter au groupe — " + ManagedShortcuts.Name(settings.Entries.Single(e => e.Id == groupId));
                catalogWindow.Heading.Text = catalogWindow.Title;
            }
            catalogWindow.ShowDialog();
        }
        finally { catalogWindow = null; interactionLocks--; outsideSince = 0; }
    }

    private async Task RefreshPresenceAsync()
    {
        checkingPresence = true;
        try { await ApplicationCatalog.RefreshPresenceAsync(lifetime.Token); }
        catch (OperationCanceledException) { }
        finally { checkingPresence = false; }
    }

    private void Launch(DockEntry entry)
    {
        if (entry.IsGroup || entry.IsSeparator) return;
        CloseGroup();
        try
        {
            if (shortcuts.IsMissing(entry)) { OfferRepair(entry); return; }
            Process.Start(new ProcessStartInfo(entry.Path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        { OfferRepair(entry, ex.Message); }
    }

    private void OfferRepair(DockEntry entry, string? error = null)
    {
        interactionLocks++;
        try
        {
            var panel = new StackPanel { Margin = new Thickness(24) };
            panel.Children.Add(new TextBlock
            {
                Text = $"Impossible d’ouvrir « {ManagedShortcuts.Name(entry)} ».\n\n{error ?? "La cible est introuvable."}",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 20)
            });
            var prompt = new Window
            {
                Title = "Réparer le raccourci", Owner = this, Width = 460, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(Color.FromRgb(27, 30, 39)), Content = panel
            };
            var choose = new Button { Content = "Choisir une nouvelle cible", Padding = new Thickness(12), Background = new SolidColorBrush(Color.FromRgb(62, 77, 111)) };
            choose.Click += (_, _) => prompt.DialogResult = true;
            panel.Children.Add(choose);
            var cancel = new Button { Content = "Annuler", Padding = new Thickness(12), IsCancel = true };
            panel.Children.Add(cancel);
            if (prompt.ShowDialog() == true) EditEntry(entry);
        }
        finally { interactionLocks--; }
    }

    private void EditEntry(DockEntry entry)
    {
        interactionLocks++;
        try
        {
            var editor = new ShortcutEditor(entry, shortcuts, updated =>
            {
                shortcuts.Commit(settings, settings.Entries.Select(e => e.Id == entry.Id ? updated : e).ToList());
                icons.Remove(entry.Path); icons.Remove(updated.Path);
                runningEntries.Remove(entry.Id); missingEntries.Remove(entry.Id);
                lastRunningCheck = 0;
                RenderItems();
            }) { Owner = this };
            editor.ShowDialog();
        }
        finally { interactionLocks--; outsideSince = 0; }
    }

    private async Task RefreshRunningAsync()
    {
        checkingRunning = true;
        var entries = settings.Entries.ToArray();
        try
        {
            var (snapshot, missing) = await StaWorker.RunAsync(_ => (runningApplications.Snapshot(entries),
                entries.Where(shortcuts.IsMissing).Select(e => e.Id).ToHashSet()), lifetime.Token);
            if (closed) return;
            if (!settings.Entries.SequenceEqual(entries)) { lastRunningCheck = 0; return; }
            runningEntries = snapshot;
            missingEntries = missing;
            foreach (var (id, badge) in runningIndicators)
            {
                badge.Fill = missing.Contains(id) ? Brushes.Orange : new SolidColorBrush(Color.FromRgb(65, 218, 119));
                badge.Visibility = missing.Contains(id) || snapshot.Contains(id) ? Visibility.Visible : Visibility.Hidden;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            // A process can exit or deny access during enumeration; retry on the next refresh.
        }
        finally { checkingRunning = false; }
    }

    private void PersistAndRender()
    {
        Persist();
        RenderItems();
    }

    private void Persist()
    {
        try { store.Save(settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { ShowNotice("Les modifications restent actives, mais n’ont pas pu être enregistrées.\n\n" + ex.Message); }
    }

    public void ShowNotice(string text)
    {
        interactionLocks++;
        try { MessageBox.Show(this, text, "WinDock", MessageBoxButton.OK, MessageBoxImage.Information); }
        finally { interactionLocks--; outsideSince = 0; }
    }

    private void OpenSettings()
    {
        CloseGroup();
        if (settingsWindow is not null) { BringSettingsForward(settingsWindow); return; }
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Position du dock", FontSize = 22, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 20) });
        panel.Children.Add(new TextBlock { Text = "Bord de l’écran", Foreground = Brushes.LightGray });
        var edges = new[] { (DockEdge.Bottom, "Bas"), (DockEdge.Top, "Haut"), (DockEdge.Left, "Gauche"), (DockEdge.Right, "Droite") };
        var edgePicker = new ComboBox { ItemsSource = edges.Select(e => e.Item2), SelectedIndex = Array.FindIndex(edges, e => e.Item1 == settings.Edge), Margin = new Thickness(0, 8, 0, 20) };
        panel.Children.Add(edgePicker);
        panel.Children.Add(new TextBlock { Text = "Écran", Foreground = Brushes.LightGray });
        var displays = Native.Displays();
        var monitorPicker = new ComboBox { ItemsSource = displays.Select(d => d.Label), SelectedIndex = Math.Max(0, displays.FindIndex(d => d.Device == display?.Device)), Margin = new Thickness(0, 8, 0, 20) };
        panel.Children.Add(monitorPicker);
        var autoHideToggle = new CheckBox { Content = "Masquage automatique", IsChecked = settings.AutoHide, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 16) };
        autoHideToggle.SetResourceReference(StyleProperty, "SettingsSwitch");
        autoHideToggle.Click += (_, _) =>
        {
            settings.AutoHide = autoHideToggle.IsChecked == true;
            Persist();
            outsideSince = triggerSince = 0;
            if (!settings.AutoHide) { indicator?.SetAppearance(false, false, false); SetRevealed(display is null || !Native.FullScreen(display, hwnd)); }
        };
        panel.Children.Add(autoHideToggle);
        var recycleToggle = new CheckBox { Content = "Afficher la corbeille", IsChecked = settings.ShowRecycleBin, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 20) };
        recycleToggle.SetResourceReference(StyleProperty, "SettingsSwitch");
        recycleToggle.Click += async (_, _) =>
        {
            settings.ShowRecycleBin = recycleToggle.IsChecked == true;
            PersistAndRender();
            lastRecycleCheck = 0;
            await RefreshRecycleAsync();
        };
        panel.Children.Add(recycleToggle);
        var opacityLabel = new TextBlock { Foreground = Brushes.LightGray, Text = $"Opacité du fond : {settings.BackgroundOpacity:P0}" };
        panel.Children.Add(opacityLabel);
        var opacitySlider = new Slider
        {
            Minimum = 10, Maximum = 100, Value = settings.BackgroundOpacity * 100,
            TickFrequency = 1, IsSnapToTickEnabled = true,
            Margin = new Thickness(0, 8, 0, 20), ToolTip = "Transparence du fond uniquement"
        };
        panel.Children.Add(opacitySlider);
        var startup = StartupRegistration.Current();
        var startupToggle = new CheckBox
        {
            Content = "Lancer WinDock au démarrage de Windows", IsChecked = startup.IsEnabled,
            Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(startupToggle);
        startupToggle.SetResourceReference(StyleProperty, "SettingsSwitch");
        panel.Children.Add(new TextBlock
        {
            Text = "Conservez le dossier portable à son emplacement. Après un déplacement, désactivez puis réactivez cette option.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0, 0, 0, 20)
        });
        startupToggle.Click += async (_, _) =>
        {
            startupToggle.IsEnabled = false;
            try
            {
                var enabled = startupToggle.IsChecked == true;
                await StaWorker.RunAsync(_ => { startup.SetEnabled(enabled); return true; }, CancellationToken.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            { ShowNotice("Le démarrage automatique n’a pas pu être modifié.\n\n" + ex.Message); }
            finally { startupToggle.IsChecked = startup.IsEnabled; startupToggle.IsEnabled = true; }
        };
        opacitySlider.ValueChanged += (_, _) =>
        {
            settings.BackgroundOpacity = opacitySlider.Value / 100;
            dockBackground.Opacity = settings.BackgroundOpacity;
            opacityLabel.Text = $"Opacité du fond : {settings.BackgroundOpacity:P0}";
        };
        panel.Children.Add(new TextBlock { Text = "Sans masquage automatique, le dock reste visible sauf en plein écran.\n\nLa taille du dock s’adapte aux icônes.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray });
        var restore = new Button
        {
            Content = "Restaurer le dock par défaut…", Padding = new Thickness(12, 10, 12, 10),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 20, 0, 8),
            Background = new SolidColorBrush(Color.FromRgb(48, 55, 71)), IsEnabled = !restoringDefaults
        };
        restoreDefaultsButton = restore;
        panel.Children.Add(restore);
        panel.Children.Add(new TextBlock
        {
            Text = "Remplace les raccourcis et groupes et réactive la corbeille. L’apparence et l’emplacement sont conservés.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray
        });
        restore.Click += async (_, _) =>
        {
            restore.IsEnabled = false;
            try { await RestoreDefaultsAsync(); }
            finally { recycleToggle.IsChecked = settings.ShowRecycleBin; restore.IsEnabled = true; }
        };
        var close = new Button { Content = "Terminer", Padding = new Thickness(16, 10, 16, 10), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0), Background = new SolidColorBrush(Color.FromRgb(62, 77, 111)) };
        panel.Children.Add(close);
        var version = typeof(App).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion.Split('+')[0]
            ?? typeof(App).Assembly.GetName().Version?.ToString() ?? "inconnue";
        panel.Children.Add(new TextBlock
        {
            Text = $"WinDock — Version {version}", Foreground = Brushes.LightGray,
            FontSize = 12, Margin = new Thickness(0, 16, 0, 0), TextAlignment = TextAlignment.Center
        });
        settingsWindow = new Window { Title = "Réglages — WinDock", Icon = Icon,
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
            Width = 420, MaxHeight = Math.Max(200, (display?.Work.Height ?? SystemParameters.WorkArea.Height) / (display?.Scale ?? 1) - 24),
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = new SolidColorBrush(Color.FromRgb(27, 30, 39)) };
        interactionLocks++;
        settingsWindow.Closed += (_, _) => { if (restoreDefaultsButton == restore) restoreDefaultsButton = null; settingsWindow = null; Persist(); interactionLocks--; outsideSince = 0; };
        close.Click += (_, _) => settingsWindow?.Close();
        edgePicker.SelectionChanged += (_, _) => { settings.Edge = edges[edgePicker.SelectedIndex].Item1; PersistAndRender(); SetRevealed(true); };
        monitorPicker.SelectionChanged += (_, _) =>
        {
            if (monitorPicker.SelectedIndex < 0) return;
            settings.MonitorDevice = displays[monitorPicker.SelectedIndex].Device;
            PersistAndRender(); SetRevealed(true);
        };
        settingsWindow.Show();
        var origin = settingsWindow.PointToScreen(new Point());
        var settingsDisplay = Native.Displays().FirstOrDefault(d => d.Bounds.Contains(origin.X, origin.Y));
        if (settingsDisplay is not null)
            settingsWindow.MaxHeight = Math.Max(200, settingsDisplay.Work.Height / VisualTreeHelper.GetDpi(settingsWindow).DpiScaleY - 24);
        BringSettingsForward(settingsWindow);
    }

    internal static void BringSettingsForward(Window window)
    {
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Topmost = true;
        window.Activate();
        window.Topmost = false;
        window.Focus();
    }
}
