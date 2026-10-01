using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Macrofy.App.ViewModels;
using Macrofy.Core;

namespace Macrofy.App.Views;

public partial class SettingsView : UserControl
{
    private const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8;

    public SettingsView() => InitializeComponent();

    private MainViewModel Vm => (MainViewModel)DataContext;
    private Window? Owner => Window.GetWindow(this);

    // Record a new global shortcut (it must include a modifier so it doesn't hijack a bare key).
    private void GlobalHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (HotkeyNames.IsModifier(key))
            return;
        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None)
            return;

        uint winMods = 0;
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) { winMods |= ModControl; parts.Add("Ctrl"); }
        if (mods.HasFlag(ModifierKeys.Alt)) { winMods |= ModAlt; parts.Add("Alt"); }
        if (mods.HasFlag(ModifierKeys.Shift)) { winMods |= ModShift; parts.Add("Shift"); }
        if (mods.HasFlag(ModifierKeys.Windows)) { winMods |= ModWin; parts.Add("Win"); }
        parts.Add(key switch
        {
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (key - Key.NumPad0),
            _ => key.ToString(),
        });
        Vm.SetGlobalHotkey((int)winMods, KeyInterop.VirtualKeyFromKey(key), string.Join(" + ", parts));
    }

    private void RestartAsAdmin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = "--relaunch",
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return; // user declined the UAC prompt
        }
        (Owner as MainWindow)?.ExitApp();
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) => await Vm.CheckForUpdatesAsync(userAsked: true);

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e) => await Vm.InstallUpdateAsync();

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(AppPaths.DataDir) { UseShellExecute = true }); }
        catch { /* best effort */ }
    }

    private void DeleteAllProfiles_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(Owner!,
            "This deletes every profile and its macros. Keyboard names and layouts are kept. This can't be undone.",
            "Delete all profiles", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm == MessageBoxResult.OK)
            Vm.DeleteAllProfiles();
    }
}
