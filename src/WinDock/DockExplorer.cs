using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace WinDock;

public sealed partial class DockWindow
{
    private bool restartingExplorer;

    private bool ConfirmExplorerRestart() => MessageBox.Show(settingsWindow ?? this,
        "Le bureau et la barre des tâches vont disparaître brièvement. Des fenêtres de dossiers peuvent se fermer et des copies en cours peuvent être interrompues.\n\nRedémarrer l’Explorateur Windows ?",
        "Redémarrer l’Explorateur Windows", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void UpdateExplorerRestartItems()
    {
        var buttons = items.Children.OfType<Button>().Concat(groupPanel?.Items.Children.OfType<Button>() ?? []);
        foreach (var item in buttons.SelectMany(button => button.ContextMenu.Items.OfType<MenuItem>())
            .Where(item => Equals(item.Header, "Redémarrer l’Explorateur Windows…")))
            item.IsEnabled = !restartingExplorer;
    }

    private async Task RestartExplorerAsync()
    {
        if (restartingExplorer || closed) return;
        restartingExplorer = true;
        UpdateExplorerRestartItems();
        interactionLocks++;
        try
        {
            if (!confirmExplorerRestart()) return;
            await Task.Run(() => explorerRestarter.RestartAsync(lifetime.Token));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException or ArgumentException)
        { if (!closed) ShowNotice("Impossible de redémarrer l’Explorateur Windows.\n\n" + ex.Message); }
        finally
        {
            restartingExplorer = false;
            UpdateExplorerRestartItems();
            interactionLocks--; lastRunningCheck = lastDisplayCheck = 0;
        }
    }
}
