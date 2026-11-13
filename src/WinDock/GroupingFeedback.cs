using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using WinDock.Core;

namespace WinDock;

/// <summary>Tracks a stable drag target without changing the pinned entries.</summary>
internal sealed class GroupingHover
{
    internal DockEntry? Target { get; private set; }
    private Guid? source;
    private long since;
    internal void Update(Guid? sourceId, DockEntry? target, long now)
    {
        if (target is { } candidate && (candidate.IsSeparator || candidate.GroupId is not null || candidate.Id == sourceId || !candidate.IsGroup && sourceId is null)) target = null;
        if (target?.Id != Target?.Id || source != sourceId) since = now;
        source = sourceId; Target = target;
    }
    internal bool IsReady(long now) => source is not null && Target is { IsGroup: false } && now - since >= 500;
    internal void Clear() { Target = null; source = null; since = 0; }
    internal static bool IsCenter(Size size, Point point) => new Rect(6, 6, Math.Max(0, size.Width - 12), Math.Max(0, size.Height - 12)).Contains(point);
}

/// <summary>A click-through visual layer for grouping; no target geometry is modified.</summary>
internal sealed class GroupingFeedback : Window
{
    private readonly Canvas canvas = new();
    private readonly Border halo = new() { CornerRadius = new CornerRadius(14), IsHitTestVisible = false };
    private readonly TextBlock caption = new() { Foreground = Brushes.White, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center };
    private readonly Border hint;

    internal GroupingFeedback()
    {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; IsHitTestVisible = false;
        hint = new Border { Child = caption, Background = new SolidColorBrush(Color.FromRgb(27, 30, 39)), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7, 10, 7) };
        canvas.Children.Add(halo); canvas.Children.Add(hint); Content = canvas;
        SourceInitialized += (_, _) => Native.MakeClickThrough(new WindowInteropHelper(this).Handle);
    }

    internal void Update(PixelRect target, Display display, string text, bool ready, ImageSource? source, ImageSource? destination)
    {
        var scale = display.Scale;
        caption.Text = text;
        var width = Math.Min(280 * scale, display.Work.Width);
        var x = Math.Clamp(target.X + target.Width / 2 - width / 2, display.Work.X, display.Work.Right - width);
        var above = target.Bottom + 44 * scale > display.Work.Bottom;
        var y = above ? target.Y - 44 * scale : target.Y - 4 * scale;
        y = Math.Clamp(y, display.Work.Y, Math.Max(display.Work.Y, display.Work.Bottom - target.Height - 48 * scale));
        Width = width / scale; Height = target.Height / scale + 48;
        halo.Width = target.Width / scale + 8; halo.Height = target.Height / scale + 8;
        halo.BorderBrush = ready ? Brushes.DeepSkyBlue : new SolidColorBrush(Color.FromArgb(145, 0, 191, 255));
        halo.BorderThickness = new Thickness(ready ? 3 : 2);
        halo.Background = new SolidColorBrush(Color.FromArgb(ready ? (byte)100 : (byte)38, 0, 150, 220));
        halo.Child = null;
        if (ready && source is not null && destination is not null)
        {
            var preview = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Width = Math.Max(16, target.Width / scale - 12), Height = Math.Max(16, target.Height / scale - 12) };
            preview.Children.Add(new Image { Source = destination, Margin = new Thickness(1) });
            preview.Children.Add(new Image { Source = source, Margin = new Thickness(1) });
            halo.Child = preview;
        }
        else if (destination is not null) halo.Child = new Image { Source = destination, Width = Math.Max(16, target.Width / scale * 0.8), Height = Math.Max(16, target.Height / scale * 0.8) };
        Canvas.SetLeft(halo, (target.X - x) / scale - 4); Canvas.SetTop(halo, (target.Y - y) / scale - 4);
        hint.Width = Width;
        Canvas.SetLeft(hint, 0); Canvas.SetTop(hint, above ? 0 : (target.Bottom - y) / scale + 5);
        if (!IsVisible) Show();
        Native.Position(new WindowInteropHelper(this).Handle, new PixelRect(x, y, width, Height * scale));
    }
}
