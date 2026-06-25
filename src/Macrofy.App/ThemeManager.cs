using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace Macrofy.App;

public enum AppTheme { System, Light, Dark }

// Applies light/dark across both WPF-UI's themed brushes and Macrofy's own palette. The
// custom Rm* brushes are referenced via DynamicResource, so swapping the resource entries
// here re-themes the whole UI (we can't mutate them in place - style setters freeze them).
public static class ThemeManager
{
    public static void Apply(AppTheme theme)
    {
        bool dark = theme switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            _ => IsWindowsDark(),
        };

        ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light);

        if (dark)
        {
            Set("RmWindowBrush", "#0A0A0B"); Set("RmSidebarBrush", "#000000"); Set("RmPanelBrush", "#121316");
            Set("RmCardBrush", "#1C1D22"); Set("RmCardBorderBrush", "#2A2C33"); Set("RmHoverBrush", "#14FFFFFF");
            Set("RmSubtleBrush", "#14FFFFFF"); Set("RmIconBgBrush", "#1FFFFFFF");
        }
        else
        {
            Set("RmWindowBrush", "#F1F2F4"); Set("RmSidebarBrush", "#FBFBFD"); Set("RmPanelBrush", "#FFFFFF");
            Set("RmCardBrush", "#FFFFFF"); Set("RmCardBorderBrush", "#E5E6EA"); Set("RmHoverBrush", "#0F000000");
            Set("RmSubtleBrush", "#09000000"); Set("RmIconBgBrush", "#12000000");
        }
    }

    public static bool IsWindowsDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return true; }
    }

    // Replace the resource entry outright (the brushes get frozen by style setters, so we
    // can't mutate them). Everything references these via DynamicResource, so swapping the
    // entry updates the whole UI.
    private static void Set(string key, string hex)
        => Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
}
