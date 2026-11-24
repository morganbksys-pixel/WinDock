using Microsoft.Win32;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinDock;

internal static class DockMenuTheme
{
    internal static bool WindowsUsesDarkTheme()
    {
        var dark = false;
        try
        {
            dark = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1) is 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or System.IO.IOException) { }
        return dark;
    }

    internal static void Apply(ContextMenu menu, bool dark, bool highContrast)
    {
        Set("MenuSurface", "#F9F9FA", "#292A2F", SystemColors.MenuBrush);
        Set("MenuText", "#1B1B20", "#F4F5F8", SystemColors.MenuTextBrush);
        Set("MenuBorder", "#C8CBD2", "#555960", SystemColors.WindowTextBrush);
        Set("MenuDivider", "#D9DCE2", "#494D55", SystemColors.WindowTextBrush);
        Set("MenuHover", "#EAEFF7", "#393E48", SystemColors.HighlightBrush);
        Set("MenuHighlightedText", "#1B1B20", "#F4F5F8", SystemColors.HighlightTextBrush);
        Set("MenuDisabled", "#666871", "#A7AAB2", SystemColors.GrayTextBrush);
        Set("MenuFocus", "#365E99", "#A5CAFD", SystemColors.HighlightTextBrush);

        void Set(string key, string light, string night, Brush system) =>
            menu.Resources[key] = highContrast ? system : new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? night : light));
    }
}
