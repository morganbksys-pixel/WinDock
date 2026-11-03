using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace WinDock;

internal static class DockLabels
{
    internal static void Attach(Button button, string text, Func<bool> canOpen)
    {
        var label = new ToolTip
        {
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(27, 30, 39)), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6, 10, 6),
                Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 12,
                    TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 260 }
            },
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
            HasDropShadow = false, Placement = PlacementMode.Custom, PlacementTarget = button,
            IsHitTestVisible = false, Focusable = false,
            CustomPopupPlacementCallback = Placements
        };
        label.Opened += (_, _) =>
        {
            if (!canOpen()) { label.IsOpen = false; return; }
            if (PresentationSource.FromVisual(label) is HwndSource source) Native.MakeClickThrough(source.Handle);
        };
        button.ToolTip = label;
        ToolTipService.SetInitialShowDelay(button, 350);
        ToolTipService.SetBetweenShowDelay(button, 0);
        ToolTipService.SetShowDuration(button, int.MaxValue);
        button.MouseLeave += (_, _) => label.IsOpen = false;
    }

    internal static CustomPopupPlacement[] Placements(Size popup, Size target, Point offset) =>
    [
        new(new Point((target.Width - popup.Width) / 2, target.Height + 6), PopupPrimaryAxis.Horizontal),
        new(new Point((target.Width - popup.Width) / 2, -popup.Height - 6), PopupPrimaryAxis.Horizontal)
    ];
}
