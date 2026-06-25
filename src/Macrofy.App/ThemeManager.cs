using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace Macrofy.App;

public enum AppTheme { System, Light, Dark }

// Applies light/dark across both WPF-UI's themed brushes and Macrofy's own palette. Our
// custom Rm* brushes are shared SolidColorBrush instances, so changing their .Color live
// updates everything that references them (no DynamicResource needed).
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

    private static void Set(string key, string hex)
    {
        if (Application.Current.Resources[key] is SolidColorBrush brush)
            brush.Color = (Color)ColorConverter.ConvertFromString(hex);
    }
}
