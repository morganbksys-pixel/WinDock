using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WinDock.Core;

namespace WinDock;

// A schematic keeps the selected corner and the opening direction visible together.
internal sealed class DockPositionPicker : StackPanel
{
    private readonly DockSettings settings;
    private readonly List<(RadioButton Button, DockEdge Edge)> edgeButtons = [];
    private readonly List<(RadioButton Button, DockCorner Corner)> cornerButtons = [];
    private readonly StackPanel orientationPanel = new();
    private readonly TextBlock summary = new() { Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 20) };
    private readonly Canvas preview = new() { Width = 104, Height = 62, IsHitTestVisible = false };
    private readonly string groupPrefix = Guid.NewGuid().ToString("N");
    private readonly Grid layoutBody = new();
    private readonly Grid orientations = new();
    private readonly List<RadioButton> orientationButtons = [];
    private CheckBox exactCorner = null!;
    private CheckBox reverseOrder = null!;

    internal DockPositionPicker(DockSettings settings, Action changed)
    {
        this.settings = settings;
        Children.Add(new TextBlock { Text = "Emplacement", Foreground = Brushes.LightGray });
        var modes = new Grid { Margin = new Thickness(0, 8, 0, 12) };
        modes.ColumnDefinitions.Add(new()); modes.ColumnDefinitions.Add(new());
        foreach (var (mode, label) in new[] { (DockPlacementMode.Edge, "Bord"), (DockPlacementMode.Corner, "Coin") })
        {
            var button = Choice(label, "mode", settings.PlacementMode == mode);
            Grid.SetColumn(button, (int)mode); modes.Children.Add(button);
            button.Checked += (_, _) => { settings.PlacementMode = mode; UpdatePreview(); changed(); };
        }
        Children.Add(modes);
        var screen = new Grid { Margin = new Thickness(8) };
        for (var i = 0; i < 3; i++)
        {
            screen.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 1 ? new GridLength(1, GridUnitType.Star) : new GridLength(100) });
            screen.RowDefinitions.Add(new RowDefinition { Height = i == 1 ? new GridLength(74) : new GridLength(48) });
        }
        Grid.SetRow(preview, 1); Grid.SetColumn(preview, 1); screen.Children.Add(preview);
        foreach (var (edge, label, row, column) in new[]
        {
            (DockEdge.Top, "Haut", 0, 1), (DockEdge.Bottom, "Bas", 2, 1),
            (DockEdge.Left, "Gauche", 1, 0), (DockEdge.Right, "Droite", 1, 2)
        })
        {
            var button = Choice(label, "edge", settings.Edge == edge);
            AutomationProperties.SetName(button, "Bord " + label.ToLowerInvariant());
            Grid.SetRow(button, row); Grid.SetColumn(button, column); screen.Children.Add(button);
            edgeButtons.Add((button, edge));
            button.Checked += (_, _) => { settings.Edge = edge; UpdatePreview(); changed(); };
        }
        foreach (var (corner, label, row, column) in new[]
        {
            (DockCorner.TopLeft, "Haut gauche", 0, 0), (DockCorner.TopRight, "Haut droite", 0, 2),
            (DockCorner.BottomLeft, "Bas gauche", 2, 0), (DockCorner.BottomRight, "Bas droite", 2, 2)
        })
        {
            var button = Choice(label, "corner", settings.Corner == corner);
            AutomationProperties.SetName(button, "Coin " + label.ToLowerInvariant());
            Grid.SetRow(button, row); Grid.SetColumn(button, column); screen.Children.Add(button);
            cornerButtons.Add((button, corner));
            button.Checked += (_, _) => { settings.Corner = corner; UpdatePreview(); changed(); };
        }
        var screenFrame = new Border
        {
            Child = screen, CornerRadius = new CornerRadius(12), BorderBrush = new SolidColorBrush(Color.FromRgb(92, 104, 124)),
            BorderThickness = new Thickness(1), Background = new SolidColorBrush(Color.FromRgb(20, 23, 31))
        };
        layoutBody.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        layoutBody.ColumnDefinitions.Add(new() { Width = new GridLength(0) });
        layoutBody.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layoutBody.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layoutBody.Children.Add(screenFrame);
        orientationPanel.Margin = new Thickness(0, 12, 0, 0);
        orientationPanel.Children.Add(new TextBlock { Text = "Orientation", Foreground = Brushes.LightGray });
        orientations.Margin = new Thickness(0, 8, 0, 0);
        orientations.ColumnDefinitions.Add(new()); orientations.ColumnDefinitions.Add(new());
        orientations.RowDefinitions.Add(new() { Height = GridLength.Auto }); orientations.RowDefinitions.Add(new() { Height = GridLength.Auto });
        foreach (var (orientation, label) in new[] { (DockOrientation.Horizontal, "Horizontal"), (DockOrientation.Vertical, "Vertical") })
        {
            var button = Choice(label, "orientation", settings.Orientation == orientation);
            Grid.SetColumn(button, (int)orientation); orientations.Children.Add(button);
            orientationButtons.Add(button);
            button.Checked += (_, _) => { settings.Orientation = orientation; UpdatePreview(); changed(); };
        }
        orientationPanel.Children.Add(orientations);
        reverseOrder = new CheckBox
        {
            Content = new TextBlock { Text = "Inverser l’ordre des icônes", TextWrapping = TextWrapping.Wrap },
            IsChecked = settings.ReverseIconOrder, Foreground = Brushes.White, Margin = new Thickness(0, 14, 0, 0),
            ToolTip = "Inverse aussi les séparateurs, les tuiles de groupes et la corbeille."
        };
        AutomationProperties.SetName(reverseOrder, "Inverser l’ordre des icônes");
        reverseOrder.SetResourceReference(StyleProperty, "SettingsSwitch");
        void ReverseOrderChanged(object sender, RoutedEventArgs e)
        {
            var reversed = reverseOrder.IsChecked == true;
            if (settings.ReverseIconOrder == reversed) return;
            settings.ReverseIconOrder = reversed;
            UpdatePreview();
            changed();
        }
        reverseOrder.Checked += ReverseOrderChanged;
        reverseOrder.Unchecked += ReverseOrderChanged;
        orientationPanel.Children.Add(reverseOrder);
        exactCorner = new CheckBox
        {
            Content = new TextBlock { Text = "Exiger le coin exact", TextWrapping = TextWrapping.Wrap },
            IsChecked = settings.RequireExactCorner, Foreground = Brushes.White, Margin = new Thickness(0, 14, 0, 0),
            ToolTip = "Placez la souris tout au bout du coin pendant 300 ms pour ouvrir le dock."
        };
        AutomationProperties.SetName(exactCorner, "Exiger le coin exact");
        exactCorner.SetResourceReference(StyleProperty, "SettingsSwitch");
        exactCorner.Checked += (_, _) => { settings.RequireExactCorner = true; UpdatePreview(); changed(); };
        exactCorner.Unchecked += (_, _) => { settings.RequireExactCorner = false; UpdatePreview(); changed(); };
        orientationPanel.Children.Add(exactCorner);
        Grid.SetRow(orientationPanel, 1); layoutBody.Children.Add(orientationPanel);
        Children.Add(layoutBody);
        Children.Add(summary);
        UpdatePreview();
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var wide = constraint.Width >= 500 && settings.PlacementMode == DockPlacementMode.Corner;
        layoutBody.ColumnDefinitions[1].Width = new GridLength(wide ? 180 : 0);
        Grid.SetColumn(orientationPanel, wide ? 1 : 0); Grid.SetRow(orientationPanel, wide ? 0 : 1);
        orientationPanel.Margin = wide ? new Thickness(16, 0, 0, 0) : new Thickness(0, 12, 0, 0);
        orientations.ColumnDefinitions[1].Width = wide ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(orientationButtons[1], wide ? 0 : 1); Grid.SetRow(orientationButtons[1], wide ? 1 : 0);
        return base.MeasureOverride(constraint);
    }

    internal void Refresh() => UpdatePreview();

    private RadioButton Choice(string label, string group, bool selected)
    {
        var button = new RadioButton
        {
            Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
            GroupName = groupPrefix + group, IsChecked = selected, Margin = new Thickness(2), MinHeight = 32,
            VerticalAlignment = VerticalAlignment.Center
        };
        button.SetResourceReference(StyleProperty, "PositionChoice");
        AutomationProperties.SetName(button, label);
        return button;
    }

    private void UpdatePreview()
    {
        var cornerMode = settings.PlacementMode == DockPlacementMode.Corner;
        foreach (var (button, _) in edgeButtons) button.Visibility = cornerMode ? Visibility.Collapsed : Visibility.Visible;
        foreach (var (button, _) in cornerButtons) button.Visibility = cornerMode ? Visibility.Visible : Visibility.Collapsed;
        orientationPanel.Visibility = cornerMode ? Visibility.Visible : Visibility.Collapsed;
        reverseOrder.IsChecked = settings.ReverseIconOrder;
        exactCorner.IsEnabled = settings.AutoHide;
        var layout = settings.Layout;
        var edge = layout.EffectiveEdge;
        var horizontal = layout.Horizontal;
        var width = horizontal ? 66 : 18;
        var height = horizontal ? 18 : 50;
        var x = cornerMode ? layout.Right ? 100 - width : 4 : edge == DockEdge.Left ? 4 : edge == DockEdge.Right ? 100 - width : (104 - width) / 2;
        var y = cornerMode ? layout.Bottom ? 58 - height : 4 : edge == DockEdge.Top ? 4 : edge == DockEdge.Bottom ? 58 - height : (62 - height) / 2;
        preview.Children.Clear();
        var surface = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(Color.FromRgb(67, 103, 133)) };
        Canvas.SetLeft(surface, x); Canvas.SetTop(surface, y); preview.Children.Add(surface);
        for (var i = 0; i < 3; i++)
        {
            var index = layout.Reversed ? 2 - i : i;
            var slot = new Rectangle { Width = 8, Height = 8, RadiusX = 2, RadiusY = 2, Fill = i == 0 ? Brushes.White : Brushes.LightSteelBlue };
            Canvas.SetLeft(slot, x + (horizontal ? 5 + index * 22 : 5));
            Canvas.SetTop(slot, y + (horizontal ? 5 : 5 + index * 16));
            preview.Children.Add(slot);
        }
        var position = cornerMode ? settings.Corner switch
        {
            DockCorner.TopLeft => "Haut gauche", DockCorner.TopRight => "Haut droite",
            DockCorner.BottomLeft => "Bas gauche", _ => "Bas droite"
        } : edge switch { DockEdge.Top => "Haut", DockEdge.Bottom => "Bas", DockEdge.Left => "Gauche", _ => "Droite" };
        var direction = horizontal ? layout.Right ? "Vers la gauche" : "Vers la droite" : layout.Bottom ? "Vers le haut" : "Vers le bas";
        summary.Text = cornerMode ? $"{position} · {(horizontal ? "Horizontal" : "Vertical")} · {direction}\n" +
            (settings.ReverseIconOrder ? "Ordre inversé\n" : "Ordre normal\n") +
            (!settings.AutoHide ? "Dock toujours visible, sauf en plein écran." : settings.RequireExactCorner ? "Placez la souris tout au bout du coin pendant 300 ms." : "Survolez le repère du coin pendant 300 ms.")
            : $"Bord {position.ToLowerInvariant()} · Dock centré";
    }
}
