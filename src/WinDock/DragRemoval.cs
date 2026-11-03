using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinDock.Core;

namespace WinDock;

/// <summary>Explicit release outside the dock removes an entry; cancellation never does.</summary>
internal static class DragRemoval
{
    internal static bool IsArmed(Rect dockPixels, Point cursorPixels, double scale)
    {
        dockPixels.Inflate(24 * scale, 24 * scale);
        return !dockPixels.Contains(cursorPixels);
    }

    internal static bool ShouldRemove(bool armed, bool escape, DragDropKeyStates keys) =>
        armed && !escape && (keys & (DragDropKeyStates.LeftMouseButton | DragDropKeyStates.RightMouseButton | DragDropKeyStates.MiddleMouseButton)) == 0;

    internal static bool Run(FrameworkElement source, Window dock, DataObject data)
    {
        var remove = false;
        var displays = Native.Displays();
        var hint = new Window
        {
            Owner = dock, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false,
            ShowActivated = false, Topmost = true, IsHitTestVisible = false, Width = 168, Height = 40,
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(48, 27, 33)), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8, 10, 8), Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Children =
                    {
                        new TextBlock { Text = "\uE74D", FontFamily = new FontFamily("Segoe MDL2 Assets"), Foreground = Brushes.Salmon, FontSize = 18, Margin = new Thickness(0, 0, 8, 0) },
                        new TextBlock { Text = "Retirer du dock", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center }
                    }
                }
            }
        };
        hint.SourceInitialized += (_, _) => Native.MakeClickThrough(new WindowInteropHelper(hint).Handle);
        bool Update()
        {
            if (!Native.GetCursorPos(out var cursor)) { hint.Hide(); return false; }
            var scale = VisualTreeHelper.GetDpi(dock).DpiScaleX;
            var origin = dock.PointToScreen(new Point());
            var armed = IsArmed(new Rect(origin.X, origin.Y, dock.ActualWidth * scale, dock.ActualHeight * scale), new Point(cursor.X, cursor.Y), scale);
            if (!armed) { hint.Hide(); return false; }
            var screen = displays.FirstOrDefault(d => d.Bounds.Contains(cursor.X, cursor.Y)) ?? displays.FirstOrDefault();
            var hintScale = screen?.Scale ?? scale;
            var width = 168 * hintScale; var height = 40 * hintScale;
            var x = cursor.X + 18 * hintScale; var y = cursor.Y + 18 * hintScale;
            if (screen is not null)
            {
                x = Math.Clamp(x, screen.Work.X, Math.Max(screen.Work.X, screen.Work.Right - width));
                y = Math.Clamp(y, screen.Work.Y, Math.Max(screen.Work.Y, screen.Work.Bottom - height));
            }
            if (!hint.IsVisible) hint.Show();
            Native.Position(new WindowInteropHelper(hint).Handle, new PixelRect(x, y, width, height));
            return true;
        }
        void Query(object sender, QueryContinueDragEventArgs e)
        {
            var armed = Update();
            if (e.EscapePressed || (e.KeyStates & (DragDropKeyStates.RightMouseButton | DragDropKeyStates.MiddleMouseButton)) != 0)
            { remove = false; e.Action = DragAction.Cancel; e.Handled = true; }
            else if (ShouldRemove(armed, e.EscapePressed, e.KeyStates))
            { remove = true; e.Action = DragAction.Cancel; e.Handled = true; }
        }
        void Feedback(object sender, GiveFeedbackEventArgs e) { Update(); }
        source.QueryContinueDrag += Query;
        source.GiveFeedback += Feedback;
        try { DragDrop.DoDragDrop(source, data, DragDropEffects.Move); return remove; }
        finally
        {
            source.QueryContinueDrag -= Query; source.GiveFeedback -= Feedback; hint.Close();
        }
    }
}
