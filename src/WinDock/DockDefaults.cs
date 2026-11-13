using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using WinDock.Core;

namespace WinDock;

public sealed partial class DockWindow
{
    private Button? restoreDefaultsButton;
    internal async Task<bool> RestoreDefaultsAsync()
    {
        if (closed || restoringDefaults) return false;
        CloseGroup();
        if (groupPanel is not null) return false; // A title edit could not be saved.
        restoringDefaults = true; interactionLocks++;
        if (restoreDefaultsButton is not null) restoreDefaultsButton.IsEnabled = false;
        var owner = settingsWindow;
        var applied = false;
        try
        {
            using var confirmation = new DefaultDockConfirmation(defaultDock, Icon, lifetime.Token) { Owner = owner ?? this };
            void OwnerClosing(object? sender, System.ComponentModel.CancelEventArgs args)
            {
                if (confirmation.IsVisible) confirmation.DialogResult = false;
            }
            bool accepted;
            var dialogOwner = owner ?? this;
            dialogOwner.Closing += OwnerClosing;
            try { accepted = confirmation.ShowDialog() == true; }
            finally { dialogOwner.Closing -= OwnerClosing; }
            using var prepared = confirmation.TakeSelection();
            if (!accepted || prepared is null || closed || owner is not null && settingsWindow != owner) return false;
            lifetime.Token.ThrowIfCancellationRequested();
            prepared.Apply(settings);
            applied = true;
            if (activeMenu is not null) activeMenu.IsOpen = false;
            openGeneration++;
            runningEntries.Clear(); missingEntries.Clear(); icons.Clear();
            lastRunningCheck = lastRecycleCheck = 0;
            RenderItems();
            await RefreshRecycleAsync();
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or InvalidOperationException)
        {
            if (!closed && (owner is null || settingsWindow == owner)) ShowNotice((applied ? "Le contenu a été enregistré, mais son affichage n’a pas pu être actualisé. Relancez WinDock." : "Impossible de restaurer le dock. Le contenu précédent est conservé.") + "\n\n" + ex.Message);
            return false;
        }
        finally { restoringDefaults = false; if (restoreDefaultsButton is not null) restoreDefaultsButton.IsEnabled = true; interactionLocks--; outsideSince = 0; }
    }
}
