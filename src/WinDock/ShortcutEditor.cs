using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using WinDock.Core;

namespace WinDock;

/// <summary>Edits a draft; disk and launcher state change only when Save succeeds.</summary>
internal sealed class ShortcutEditor : Window
{
    private readonly TextBox name = new(), target = new(), arguments = new(), work = new();
    private readonly TextBlock hint = new() { Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap };
    private string source;
    private ShortcutDetails loaded;
    private bool saving;
    private readonly DockEntry original;

    internal ShortcutEditor(DockEntry entry, ManagedShortcuts shortcuts, Action<DockEntry> commit)
    {
        original = entry;
        Title = "Modifier / réparer — WinDock";
        Width = 560; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(27, 30, 39));
        source = entry.Path;
        loaded = ShellShortcut.Read(source);
        name.Text = ManagedShortcuts.Name(entry);
        var panel = new StackPanel { Margin = new Thickness(24) };
        Content = panel;
        panel.Children.Add(new TextBlock { Text = "Votre raccourci", Foreground = Brushes.White, FontSize = 22, Margin = new Thickness(0, 0, 0, 16) });
        void Field(string label, TextBox box)
        {
            panel.Children.Add(new TextBlock { Text = label, Foreground = Brushes.LightGray });
            box.Padding = new Thickness(8); box.Margin = new Thickness(0, 6, 0, 12);
            panel.Children.Add(box);
        }
        Field("Nom affiché", name);
        Field("Cible", target);
        var browse = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        Button Button(string text, Action action)
        {
            var button = new Button { Content = text, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(Color.FromRgb(62, 77, 111)) };
            button.Click += (_, _) => action(); return button;
        }
        browse.Children.Add(Button("Parcourir…", () =>
        {
            var picker = new OpenFileDialog { Filter = "Applications et raccourcis|*.exe;*.lnk", DereferenceLinks = false };
            if (picker.ShowDialog(this) == true) LoadSource(picker.FileName);
        }));
        browse.Children.Add(Button("Choisir un dossier…", () =>
        {
            var picker = new OpenFolderDialog();
            if (picker.ShowDialog(this) == true) LoadSource(picker.FolderName);
        }));
        panel.Children.Add(browse);
        Field("Arguments", arguments);
        Field("Dossier de travail (facultatif)", work);
        panel.Children.Add(hint);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        controls.Children.Add(Button("Annuler", Close));
        var save = Button("Enregistrer", () => { });
        save.Click += async (_, _) =>
        {
            if (saving) return;
            LoadTypedLink();
            var application = Path.GetExtension(target.Text.Trim().Trim('"')).Equals(".exe", StringComparison.OrdinalIgnoreCase);
            var details = new ShortcutDetails(target.Text, application ? arguments.Text : "", application ? work.Text : "",
                application || Directory.Exists(target.Text));
            var sourcePath = source; var displayName = name.Text;
            saving = true; IsEnabled = false;
            try
            {
                var updated = await Task.Run(() => shortcuts.PrepareEdit(entry, sourcePath, displayName, details));
                commit(updated);
                saving = false;
                DialogResult = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or COMException)
            { MessageBox.Show(this, ex.Message, "Modification impossible", MessageBoxButton.OK, MessageBoxImage.Warning); }
            finally { saving = false; IsEnabled = true; }
        };
        controls.Children.Add(save); panel.Children.Add(controls);
        Closing += (_, e) => { if (saving) e.Cancel = true; };
        target.TextChanged += (_, _) => UpdateState();
        target.LostKeyboardFocus += (_, _) => LoadTypedLink();
        LoadSource(source);
    }

    private void LoadSource(string path)
    {
        source = path;
        loaded = ShellShortcut.Read(path);
        if (path == original.Path && original.SourcePath?.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase) == true)
            loaded = new("", "", "", false);
        target.Text = loaded.Editable ? loaded.Target : path;
        arguments.Text = loaded.Arguments;
        work.Text = loaded.WorkingDirectory;
        UpdateState();
    }

    private void LoadTypedLink()
    {
        var path = target.Text.Trim().Trim('"');
        if (ShellShortcut.IsLink(path) && File.Exists(path) && !string.Equals(path, source, StringComparison.OrdinalIgnoreCase)) LoadSource(path);
    }

    private void UpdateState()
    {
        var application = Path.GetExtension(target.Text.Trim().Trim('"')).Equals(".exe", StringComparison.OrdinalIgnoreCase);
        arguments.IsEnabled = work.IsEnabled = application;
        hint.Text = application
            ? "Les arguments remplacent ceux du raccourci. Entourez de guillemets les valeurs contenant des espaces."
            : ShellShortcut.IsLink(source) && !loaded.Editable && target.Text == source
                ? "Ce raccourci est conservé tel quel. Pour modifier ses paramètres, choisissez un exécutable ou un autre raccourci."
                : "Les dossiers ne nécessitent ni arguments ni dossier de travail.";
    }
}
