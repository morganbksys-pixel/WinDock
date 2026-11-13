using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinDock.Core;

namespace WinDock;

/// <summary>A dock-styled group surface, activated only for explicit title editing.</summary>
internal sealed class GroupPanel : Window
{
    internal readonly GroupGrid Items = new();
    internal readonly ScrollViewer Scroll;
    internal readonly TextBlock TitleLabel = new() { Foreground = Brushes.White, FontSize = 16, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Double-cliquez pour renommer" };
    internal readonly TextBox TitleEditor = new() { Visibility = Visibility.Collapsed, FontSize = 16, MaxLength = 120, Padding = new Thickness(4), Height = 32, Foreground = Brushes.White, CaretBrush = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)), BorderBrush = Brushes.DeepSkyBlue, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
    internal readonly Button AddButton = new() { Content = "+", FontSize = 22, Width = 32, Height = 32, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Ajouter au groupe" };
    private readonly Func<string, bool> rename;
    private string currentName = "";
    private bool finishingRename, suppressEscape;
    internal bool IsEditing { get; private set; }
    internal Guid GroupId { get; set; }

    internal GroupPanel(Guid groupId, Action add, Func<string, bool> rename, Brush background)
    {
        GroupId = groupId; this.rename = rename;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; AllowDrop = true;
        SizeToContent = SizeToContent.Height;
        var header = new DockPanel { Margin = new Thickness(4, 0, 4, 10), LastChildFill = true };
        AddButton.Click += (_, _) => add();
        AutomationProperties.SetName(AddButton, "Ajouter au groupe");
        AutomationProperties.SetName(TitleEditor, "Nom du groupe");
        DockPanel.SetDock(AddButton, Dock.Right); header.Children.Add(AddButton);
        header.Children.Add(new Grid { Children = { TitleLabel, TitleEditor } });
        Scroll = new ScrollViewer { Content = Items, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false };
        var stack = new StackPanel(); stack.Children.Add(header); stack.Children.Add(Scroll);
        Content = DockAppearance.Surface(background, stack, new Thickness(12));
        TitleLabel.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { BeginRename(); e.Handled = true; } };
        TitleEditor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Escape)) return;
            suppressEscape = e.Key == Key.Escape;
            FinishRename(e.Key == Key.Enter); e.Handled = true;
        };
        TitleEditor.LostKeyboardFocus += (_, _) => { if (IsEditing) FinishRename(true); };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            Native.MakeNonActivating(handle);
            HwndSource.FromHwnd(handle)?.AddHook((nint w, int message, nint wp, nint lp, ref bool handled) =>
            {
                if (message == 0x21 && !IsEditing) { handled = true; return 3; }
                return 0;
            });
        };
    }

    internal void BeginRename()
    {
        if (IsEditing) return;
        IsEditing = true; TitleEditor.Text = currentName;
        TitleLabel.Visibility = Visibility.Collapsed; TitleEditor.Visibility = Visibility.Visible;
        Native.SetActivationEnabled(new WindowInteropHelper(this).Handle, true);
        Activate(); TitleEditor.Focus(); TitleEditor.SelectAll();
    }

    internal bool FinishRename(bool save)
    {
        if (!IsEditing || finishingRename) return !finishingRename;
        finishingRename = true;
        try
        {
            var name = TitleEditor.Text.Trim();
            if (save && name.Length > 0 && name != currentName && !rename(name)) { TitleEditor.Focus(); return false; }
            IsEditing = false;
            TitleEditor.Visibility = Visibility.Collapsed; TitleLabel.Visibility = Visibility.Visible;
            TitleLabel.Text = currentName;
            Native.SetActivationEnabled(new WindowInteropHelper(this).Handle, false);
            return true;
        }
        finally { finishingRename = false; }
    }

    internal bool ShouldDismissOnEscape(bool pressed)
    {
        if (!pressed) suppressEscape = false;
        return pressed && !IsEditing && !suppressEscape;
    }

    internal void Configure(string name, double workWidth, double workHeight, int count)
    {
        currentName = name; TitleLabel.Text = name; Title = "Groupe — " + name;
        Width = Math.Min(378, Math.Max(100, workWidth - 16));
        Items.Columns = Math.Clamp((int)((Width - 42) / 84), 1, 4);
        Scroll.MaxHeight = Math.Max(40, Math.Min(workHeight * 0.6, workHeight - 80));
        Items.InvalidateMeasure();
    }
}

/// <summary>A wrapping grid with an insertion marker that never shifts hit targets.</summary>
internal sealed class GroupGrid : Panel
{
    internal int Columns { get; set; } = 4;
    private int? insertion;
    internal void ShowInsertion(int? index) { insertion = index; InvalidateVisual(); }
    internal int FindInsertion(Point point)
    {
        var width = ActualWidth / Columns;
        var row = Math.Max(0, (int)(point.Y / 100));
        var column = Math.Clamp((int)(point.X / Math.Max(1, width) + 0.5), 0, Columns);
        return Math.Clamp(row * Columns + column, 0, Children.Count);
    }
    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? Columns * 84 : available.Width;
        foreach (UIElement child in Children) child.Measure(new Size(width / Columns, 100));
        return new Size(width, Math.Ceiling((double)Children.Count / Columns) * 100);
    }
    protected override Size ArrangeOverride(Size final)
    {
        var width = final.Width / Columns;
        for (var i = 0; i < Children.Count; i++) Children[i].Arrange(new Rect(i % Columns * width, i / Columns * 100, width, 100));
        return final;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (insertion is not { } index) return;
        var column = index == Children.Count && index > 0 ? (index - 1) % Columns + 1 : index % Columns;
        var row = index == Children.Count && index > 0 ? (index - 1) / Columns : index / Columns;
        var x = Math.Clamp(column * ActualWidth / Columns, 2, Math.Max(2, ActualWidth - 2));
        dc.DrawLine(new Pen(Brushes.DeepSkyBlue, 3), new Point(x, row * 100 + 6), new Point(x, row * 100 + 94));
    }
}
