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

public sealed class DockWindow : Window
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

    internal DockWindow(DockSettings settings, SettingsStore store, IRecycleBinService recycleBin)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);
        this.settings = settings;
        this.store = store;
        this.recycleBin = recycleBin;
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
        Content = new Border
        {
            Background = dockBackground,
            BorderBrush = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18),
            Padding = new Thickness(8), Child = fittedItems
        };
        ContextMenu = CreateMenu(null);
        PreviewDragOver += OnDragOver;
        Drop += OnDrop;
        DragLeave += (_, e) =>
        {
            if (!new Rect(RenderSize).Contains(e.GetPosition(this))) items.ShowInsertion(null);
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
        Closed += (_, _) => { closed = true; CloseLabels(); lifetime.Cancel(); timer.Stop(); if (activeMenu is not null) activeMenu.IsOpen = false; menuDismissal?.Dispose(); indicator?.Close(); settingsWindow?.Close(); };
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
        var displays = Native.Displays();
        display = displays.FirstOrDefault(d => d.Device == settings.MonitorDevice)
            ?? displays.FirstOrDefault(d => d.Primary) ?? displays.FirstOrDefault();
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
        foreach (var entry in settings.Entries)
        {
            ImageSource? icon = null;
            if (!entry.IsSeparator && !icons.TryGetValue(entry.Path, out icon)) icons[entry.Path] = icon = Native.Icon(entry.Path);
            var button = new Button
            {
                Width = entry.IsSeparator && Horizontal ? 22 : 60,
                Height = entry.IsSeparator && !Horizontal ? 22 : 60, Tag = entry,
                Focusable = false,
                Content = entry.IsSeparator ? new Border
                {
                    Width = Horizontal ? 1 : 32, Height = Horizontal ? 32 : 1,
                    Background = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)),
                    CornerRadius = new CornerRadius(0.5)
                } : icon is null
                    ? new TextBlock { Text = "◇", FontSize = 32 }
                    : new Image { Source = icon, Width = 48, Height = 48, Stretch = Stretch.Uniform },
                ContextMenu = CreateMenu(entry)
            };
            if (!entry.IsSeparator)
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
            if (!entry.IsSeparator) button.Click += (_, _) => Launch(entry);
            button.PreviewMouseLeftButtonDown += (_, e) => { dragStart = e.GetPosition(this); pressedEntry = entry.Id; };
            button.PreviewMouseLeftButtonUp += (_, _) => pressedEntry = null;
            button.PreviewMouseMove += (_, e) =>
            {
                var current = e.GetPosition(this);
                if (e.LeftButton != MouseButtonState.Pressed || pressedEntry != entry.Id || dragging ||
                    (Math.Abs(current.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                     Math.Abs(current.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)) return;
                dragging = true;
                CloseLabels();
                pressedEntry = null;
                button.Opacity = 0.35;
                button.RenderTransformOrigin = new Point(0.5, 0.5);
                button.RenderTransform = new ScaleTransform(0.92, 0.92);
                try
                {
                    if (DragRemoval.Run(button, this, new DataObject(DragFormat, entry.Id.ToString())))
                    {
                        try { shortcuts.Commit(settings, settings.Entries.Where(e => e.Id != entry.Id).ToList()); RenderItems(); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowNotice(ex.Message); }
                    }
                }
                finally
                {
                    button.Opacity = 1; button.RenderTransform = Transform.Identity;
                    items.ShowInsertion(null); dragging = false; outsideSince = 0;
                }
            };
            items.Children.Add(button);
        }
        if (settings.ShowRecycleBin)
        {
            recycleButton = new Button { Width = 60, Height = 60, Focusable = false, ContextMenu = CreateMenu(null, true) };
            recycleButton.Click += async (_, _) => await RunRecycleActionAsync(false);
            items.Children.Add(recycleButton);
            UpdateRecycleButton();
        }
        if (hwnd != 0) RefreshPlacement();
    }

    private void AttachLabel(Button button, string text) => DockLabels.Attach(button, text,
        () => revealed && !closed && activeMenu is null && !dragging && !externalDrag);

    private void CloseLabels()
    {
        foreach (var button in items.Children.OfType<Button>())
            if (button.ToolTip is ToolTip label) label.IsOpen = false;
    }

    private void UpdateRecycleButton()
    {
        if (recycleButton is null) return;
        recycleButton.Content = recycleState.Icon is { } icon
            ? new Image { Source = icon, Width = 48, Height = 48, Stretch = Stretch.Uniform }
            : new TextBlock { Text = "♻", FontSize = 32 };
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
        ContextMenuDismissal? dismissal = null;
        menu.Opened += (_, _) =>
        {
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
            if (!entry.IsSeparator) Item("Modifier / réparer…", () => EditEntry(entry));
            Item("Retirer du dock", () =>
            {
                try { shortcuts.Commit(settings, settings.Entries.Where(e => e.Id != entry.Id).ToList()); RenderItems(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowNotice(ex.Message); }
            });
            menu.Items.Add(new Separator());
        }
        Item("Ajouter…", OpenCatalog);
        Item("Ajouter un séparateur", () =>
        {
            var index = entry is null ? settings.Entries.Count : settings.Entries.FindIndex(e => e.Id == entry.Id) + 1;
            settings.Entries.Insert(index, new(Guid.NewGuid(), "", true));
            PersistAndRender();
        });
        menu.Items.Add(new Separator());
        Item("Réglages…", OpenSettings);
        Item("Quitter WinDock", Close);
        return menu;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        CloseLabels();
        externalDrag = e.Data.GetDataPresent(DataFormats.FileDrop);
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
        if (e.Effects != DragDropEffects.None)
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
        externalDrag = false;
        items.ShowInsertion(null);
        e.Handled = true;
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && IsOverRecycleBin(e)) return;
        interactionLocks++;
        try
        {
            if (e.Data.GetData(DragFormat) is string id && Guid.TryParse(id, out var source))
            {
                var (before, _) = FindInsertion(e.GetPosition(items));
                EntryRules.Move(settings.Entries, source, before);
                PersistAndRender();
            }
            else if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                var (before, _) = FindInsertion(e.GetPosition(items));
                AddPaths(paths, before);
            }
        }
        finally { interactionLocks--; outsideSince = 0; }
    }

    private void AddPaths(IEnumerable<string> paths, Guid? before = null)
    {
        var rejected = new List<string>();
        foreach (var path in paths)
        {
            if (!EntryRules.IsSupported(path)) { rejected.Add(Path.GetFileName(path)); continue; }
            var fullPath = Path.GetFullPath(path);
            if (settings.Entries.Any(e => string.Equals(e.SourcePath ?? e.Path, fullPath, StringComparison.OrdinalIgnoreCase))) continue;
            var index = before is null ? -1 : settings.Entries.FindIndex(e => e.Id == before);
            try
            {
                var added = shortcuts.Import(new(Guid.NewGuid(), fullPath));
                var entries = settings.Entries.ToList();
                entries.Insert(index < 0 ? entries.Count : index, added);
                shortcuts.Commit(settings, entries);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowNotice(ex.Message); }
        }
        RenderItems();
        if (rejected.Count > 0) ShowNotice("Seuls les raccourcis Windows (.lnk), exécutables (.exe) et dossiers sont acceptés.\n\nÉléments ignorés : " + string.Join(", ", rejected));
    }

    private void PickApplication(Window? owner = null)
    {
        interactionLocks++;
        try
        {
            var picker = new OpenFileDialog { Title = "Ajouter une application ou un raccourci", Filter = "Applications et raccourcis|*.exe;*.lnk", Multiselect = true, DereferenceLinks = false };
            if (picker.ShowDialog(owner ?? this) == true) AddPaths(picker.FileNames);
        }
        finally { interactionLocks--; }
    }

    private void PickFolder(Window? owner = null)
    {
        interactionLocks++;
        try
        {
            var picker = new OpenFolderDialog { Title = "Ajouter un dossier", Multiselect = true };
            if (picker.ShowDialog(owner ?? this) == true) AddPaths(picker.FolderNames);
        }
        finally { interactionLocks--; }
    }

    private void OpenCatalog()
    {
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
                shortcuts.Commit(settings, [..settings.Entries, imported]);
                lastRunningCheck = 0;
                RenderItems();
            }, PickApplication, PickFolder, () =>
            {
                shortcuts.Commit(settings, [..settings.Entries, new DockEntry(Guid.NewGuid(), "", true)]);
                RenderItems();
            }) { Owner = this };
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
            var (snapshot, missing) = await Task.Run(() => (runningApplications.Snapshot(entries),
                entries.Where(shortcuts.IsMissing).Select(e => e.Id).ToHashSet()));
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
        settingsWindow.Closed += (_, _) => { settingsWindow = null; Persist(); interactionLocks--; outsideSince = 0; };
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
