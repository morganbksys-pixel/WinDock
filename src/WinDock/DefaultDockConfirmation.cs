using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinDock;

/// <summary>Shows immediately, prepares in the background, and transfers ownership only after confirmation.</summary>
internal sealed class DefaultDockConfirmation : Window, IDisposable
{
    internal readonly Button RestoreButton = new() { Content = "Restaurer", IsEnabled = false, Opacity = 0.45, Padding = new Thickness(14, 10, 14, 10), Background = new SolidColorBrush(Color.FromRgb(62, 77, 111)) };
    internal readonly Button CancelButton = new() { Content = "Annuler", IsCancel = true, IsDefault = true, Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 8, 0) };
    internal readonly Button RetryButton = new() { Content = "Réessayer", Visibility = Visibility.Collapsed, Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 8, 0) };
    internal readonly TextBlock Status = new() { Text = "Préparation de la sélection…", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0, 8, 0, 8) };
    private readonly ProgressBar progress = new() { IsIndeterminate = true, Height = 4, Margin = new Thickness(0, 4, 0, 12) };
    private readonly StackPanel selection = new();
    private readonly IDefaultDockService service;
    private readonly CancellationTokenSource lifetime;
    private PreparedDefaultDock? prepared, accepted;
    private bool closed, disposed, preparing;
    internal Task? PreparationTask { get; private set; }
    internal bool HasRenderedContent { get; private set; }

    internal DefaultDockConfirmation(IDefaultDockService service, ImageSource? icon, CancellationToken token = default)
    {
        this.service = service; lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        Title = "Restaurer le dock par défaut"; Icon = icon;
        Width = 440; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(27, 30, 39)); Foreground = Brushes.White;
        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock { Text = "Restaurer le dock par défaut ?", FontSize = 20, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        content.Children.Add(new TextBlock { Text = "Vos raccourcis et groupes actuels seront remplacés par cette sélection. Vos fichiers d’origine sont conservés.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0, 0, 0, 12) });
        content.Children.Add(Status); content.Children.Add(progress);
        content.Children.Add(new ScrollViewer { Content = selection, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        content.Children.Add(new TextBlock { Text = "Écran, bord, opacité, masquage et démarrage automatique restent inchangés.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0, 16, 0, 16) });
        content.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { CancelButton, RetryButton, RestoreButton } });
        Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        RestoreButton.IsEnabledChanged += (_, _) => RestoreButton.Opacity = RestoreButton.IsEnabled ? 1 : 0.45;
        RestoreButton.Click += (_, _) =>
        {
            if (prepared is null) return;
            accepted = prepared; prepared = null; DialogResult = true;
        };
        CancelButton.Click += (_, _) => DialogResult = false;
        RetryButton.Click += (_, _) => PreparationTask = PrepareAsync();
        Loaded += (_, _) =>
        {
            var scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
            var origin = PointToScreen(new Point());
            var display = Native.Displays().FirstOrDefault(d => d.Bounds.Contains(origin.X, origin.Y));
            MaxHeight = Math.Max(200, (display?.Work.Height ?? SystemParameters.WorkArea.Height) / scale - 24);
            CancelButton.Focus();
        };
        ContentRendered += (_, _) =>
        {
            HasRenderedContent = true;
            if (PreparationTask is null) PreparationTask = PrepareAsync();
        };
        Closed += (_, _) =>
        {
            closed = true; lifetime.Cancel(); prepared?.Dispose(); prepared = null;
        };
    }

    private async Task PrepareAsync()
    {
        if (closed || preparing) return;
        preparing = true;
        var token = lifetime.Token;
        PreparedDefaultDock? result = null;
        RestoreButton.IsEnabled = false; RetryButton.Visibility = Visibility.Collapsed;
        Status.Text = "Préparation de la sélection…"; Status.Visibility = Visibility.Visible;
        progress.Visibility = Visibility.Visible; selection.Children.Clear();
        try
        {
            // Input and the first render run before any environment/Shell discovery is requested.
            await Dispatcher.Yield(DispatcherPriority.Background);
            token.ThrowIfCancellationRequested();
            result = await service.PrepareAsync(token);
            if (closed || token.IsCancellationRequested) return;
            foreach (var entry in result.Entries)
            {
                if (entry.IsSeparator) selection.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)), Margin = new Thickness(0, 8, 0, 8) });
                else selection.Children.Add(new TextBlock { Text = ManagedShortcuts.Name(entry), FontSize = 14, Margin = new Thickness(4, 3, 4, 3), TextWrapping = TextWrapping.Wrap });
            }
            selection.Children.Add(new TextBlock { Text = "Corbeille", FontSize = 14, Margin = new Thickness(4, 3, 4, 3) });
            prepared = result; result = null;
            Status.Visibility = Visibility.Collapsed;
            selection.UpdateLayout();
            RestoreButton.IsEnabled = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or InvalidOperationException)
        {
            if (!closed && !token.IsCancellationRequested)
            {
                Status.Text = "Impossible de préparer la sélection. Réessayez ou annulez.\n\n" + ex.Message;
                RetryButton.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            result?.Dispose(); preparing = false;
            if (!closed) progress.Visibility = Visibility.Collapsed;
        }
    }

    internal PreparedDefaultDock? TakeSelection()
    {
        var result = accepted; accepted = null; return result;
    }

    public void Dispose()
    {
        if (disposed) return;
        if (IsVisible) Close();
        disposed = true; lifetime.Cancel();
        prepared?.Dispose(); prepared = null;
        accepted?.Dispose(); accepted = null;
        lifetime.Dispose();
    }
}
