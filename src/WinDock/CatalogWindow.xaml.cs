using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Media;
using WinDock.Core;

namespace WinDock;

internal sealed class CatalogChoice(CatalogApplication application, Action changed) : INotifyPropertyChanged
{
    public CatalogApplication Application { get; } = application;
    private bool selected, added;
    public bool IsSelected { get => selected; set { selected = value; Notify(nameof(IsSelected)); changed(); } }
    public bool CanAdd => !added;
    public string Description => added ? "Déjà dans le dock" : Application.ShortcutPath is not null ? "Application classique" : "Application Windows";
    public string InstallationPath
    {
        get
        {
            var target = Application.LaunchKey.Split('\0')[0];
            return Path.IsPathFullyQualified(target)
                ? Path.GetDirectoryName(target) ?? target
                : "Emplacement géré par Windows";
        }
    }
    public string Details => InstallationPath + "\n" + (Application.ShortcutPath ?? Application.ApplicationId ?? Application.Name);
    internal void SetAdded(bool value)
    {
        added = value;
        if (value) selected = false;
        Notify(nameof(CanAdd)); Notify(nameof(Description)); Notify(nameof(IsSelected));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

/// <summary>Searchable app picker; shell work is cancellable and runs outside the UI thread.</summary>
internal partial class CatalogWindow : Window
{
    private readonly Func<IReadOnlyList<DockEntry>> entries;
    private readonly Func<CatalogApplication, Task> add;
    private readonly Action<Window> manual, folder;
    private readonly Action separator;
    private readonly Func<CancellationToken, Task<CatalogResult>> load;
    private CancellationTokenSource? loading;
    private List<CatalogChoice> choices = [];
    private ListCollectionView? view;
    private bool closed, adding;
    private readonly CatalogCache? cache;
    private CatalogResult? pending;
    private string? warning;

    internal CatalogWindow(Func<IReadOnlyList<DockEntry>> entries, Func<CatalogApplication, Task> add,
        Action<Window> manual, Action<Window> folder, Action separator,
        Func<CancellationToken, Task<CatalogResult>>? load = null, CatalogCache? cache = null)
    {
        this.entries = entries; this.add = add; this.manual = manual; this.folder = folder; this.separator = separator;
        this.cache = cache ?? (load is null ? CatalogCache.Shared : null);
        this.load = load ?? (_ => this.cache!.RefreshAsync());
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            Search.Focus();
            if (this.cache is not null)
            {
                await this.cache.PreloadAsync();
                if (closed) return;
                if (this.cache.Current is { } snapshot) await ApplyResultAsync(snapshot);
            }
            if (!closed) await ReloadAsync();
        };
        Closing += (_, e) => { if (adding) e.Cancel = true; };
        Closed += (_, _) => { closed = true; loading?.Cancel(); };
    }

    private async Task ReloadAsync()
    {
        loading?.Cancel();
        var request = new CancellationTokenSource(); loading = request;
        RefreshButton.IsEnabled = false; UpdateSelection();
        UpdateStatus();
        try
        {
            var result = await load(request.Token);
            if (closed || request != loading) return;
            if (adding) pending = result;
            else await ApplyResultAsync(result);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (!closed) warning = "Impossible de charger les applications. Réessayez avec Actualiser ou utilisez l’ajout manuel.";
        }
        finally
        {
            if (request == loading) { loading = null; if (!closed) { RefreshButton.IsEnabled = true; UpdateSelection(); UpdateStatus(); } }
            request.Dispose();
        }
    }

    private async Task ApplyResultAsync(CatalogResult result)
    {
        if (closed) return;
        var selected = choices.Where(c => c.IsSelected).Select(c => c.Application.Identity).ToHashSet(StringComparer.Ordinal);
        var scroll = FindScrollViewer(Applications);
        var offset = scroll?.VerticalOffset ?? 0;
        warning = result.Warning;
        choices = result.Applications.Select(app => new CatalogChoice(app, UpdateSelection)
            { IsSelected = selected.Contains(app.Identity) }).ToList();
        view = new ListCollectionView(choices) { Filter = item => ApplicationCatalog.Matches(((CatalogChoice)item).Application.Name, Search.Text) };
        Applications.ItemsSource = view;
        Applications.UpdateLayout();
        scroll?.ScrollToVerticalOffset(offset);
        await MarkExistingAsync();
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        if (parent is ScrollViewer scroll) return scroll;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
        return null;
    }

    private async Task MarkExistingAsync()
    {
        var snapshot = entries().ToArray();
        var keys = await StaWorker.RunAsync(_ => ApplicationCatalog.ExistingKeys(snapshot), CancellationToken.None);
        if (closed) return;
        foreach (var choice in choices) choice.SetAdded(ApplicationCatalog.IsAdded(choice.Application, keys));
        UpdateStatus(); UpdateSelection();
    }

    private void UpdateSelection()
    {
        var count = choices.Count(c => c.IsSelected && c.CanAdd);
        AddButton.Content = count == 0 ? "Ajouter la sélection" : $"Ajouter la sélection ({count})";
        AddButton.IsEnabled = count > 0 && !adding;
    }

    private void UpdateStatus()
    {
        var count = view?.Count ?? 0;
        Status.Text = (count == 0 ? "Aucune application trouvée. Vous pouvez utiliser l’ajout manuel." : $"{count} applications — cochez celles à ajouter.") +
            (loading is null ? "" : choices.Count == 0 ? "\nChargement des applications…" : "\nActualisation en cours…") +
            (warning is null ? "" : "\n" + warning);
    }

    private void OnSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    { if (view is null) return; view.Refresh(); UpdateStatus(); }
    private async void OnRefresh(object sender, RoutedEventArgs e) => await ReloadAsync();
    private async void OnManual(object sender, RoutedEventArgs e) => await AddManuallyAsync(manual);
    private async void OnFolder(object sender, RoutedEventArgs e) => await AddManuallyAsync(folder);
    private async Task AddManuallyAsync(Action<Window> action)
    {
        try { action(this); await MarkExistingAsync(); }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidOperationException) { if (!closed) ShowError(ex.Message); }
    }
    private void OnSeparator(object sender, RoutedEventArgs e)
    {
        try { separator(); Status.Text = "Séparateur ajouté au dock."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError(ex.Message); }
    }
    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private async void OnAdd(object sender, RoutedEventArgs e)
    {
        if (adding) return;
        var selected = choices.Where(c => c.IsSelected && c.CanAdd).ToArray();
        adding = true;
        ManualButton.IsEnabled = FolderButton.IsEnabled = SeparatorButton.IsEnabled = RefreshButton.IsEnabled = Applications.IsEnabled = false;
        UpdateSelection();
        var errors = new List<string>(); var succeeded = 0;
        try
        {
            foreach (var choice in selected)
            {
                Status.Text = $"Ajout de {choice.Application.Name}…";
                try { await add(choice.Application); choice.SetAdded(true); succeeded++; }
                catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
                { errors.Add(choice.Application.Name + " : " + ex.Message); }
            }
            await MarkExistingAsync();
            Status.Text = $"{succeeded} application(s) ajoutée(s).";
            if (errors.Count > 0) ShowError(string.Join("\n\n", errors));
        }
        catch (OperationCanceledException) { }
        finally
        {
            adding = false;
            if (pending is { } result) { pending = null; await ApplyResultAsync(result); }
            ManualButton.IsEnabled = FolderButton.IsEnabled = SeparatorButton.IsEnabled = Applications.IsEnabled = true;
            RefreshButton.IsEnabled = loading is null;
            UpdateSelection();
        }
    }

    private void ShowError(string message) => MessageBox.Show(this, message, "Ajout au dock", MessageBoxButton.OK, MessageBoxImage.Warning);
}
