using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Macrofy.App.ViewModels;
using Macrofy.Core.Input;
using Macrofy.Core.Macros;
using Wpf.Ui.Controls;

namespace Macrofy.App;

public partial class MainWindow : FluentWindow
{
    private const string KeyHintFlag = "key-capture-hint";
    private const string TrayHintFlag = "tray-hint";

    private readonly MainViewModel _viewModel;
    private System.Windows.Forms.NotifyIcon? _tray;
    private System.Windows.Forms.ToolStripMenuItem? _autoStartItem;
    private bool _reallyExit;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel(new WhKeyboardBackend());
        _viewModel.CaptureEngaged += OnCaptureEngaged;
        DataContext = _viewModel;

        InitTray();

        // Pause capture while any text field has focus so the captured keyboard can type into
        // it, and resume the moment focus moves elsewhere. Both focus events feed one rule so
        // every transition (into/out of/between fields, or losing focus to another app) is right.
        AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnKeyboardFocusChanged), handledEventsToo: true);
        AddHandler(Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnKeyboardFocusChanged), handledEventsToo: true);

        Closed += (_, _) => _viewModel.Dispose();
    }

    private void OnKeyboardFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
        => _viewModel.SetCaptureSuspended(e.NewFocus is System.Windows.Controls.TextBox);

    // ---- system tray ----

    private void InitTray()
    {
        _autoStartItem = new System.Windows.Forms.ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = AutoStartManager.IsEnabled,
        };
        _autoStartItem.CheckedChanged += (_, _) => AutoStartManager.SetEnabled(_autoStartItem.Checked);

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open Macrofy", null, (_, _) => ShowFromTray());
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Quit Macrofy", null, (_, _) => ExitApp());

        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Macrofy",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ShowFromTray();
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "macrofy.ico");
            if (File.Exists(path))
                return new System.Drawing.Icon(path, System.Windows.Forms.SystemInformation.SmallIconSize);
        }
        catch { /* fall back below */ }
        return System.Drawing.SystemIcons.Application;
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false; // nudge to the foreground without staying pinned
    }

    // Closing the window hides to tray instead of quitting; Quit (tray menu) really exits.
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_reallyExit)
        {
            e.Cancel = true;
            Hide();
            if (!OnboardingState.HasSeen(TrayHintFlag))
            {
                OnboardingState.MarkSeen(TrayHintFlag);
                _tray?.ShowBalloonTip(3000, "Macrofy is still running",
                    "Macros stay active in the background. Right-click the tray icon to quit.",
                    System.Windows.Forms.ToolTipIcon.Info);
            }
            return;
        }
        base.OnClosing(e);
    }

    private void ExitApp()
    {
        _reallyExit = true;
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        Application.Current.Shutdown();
    }

    // First time the user turns on capture, explain the keys that can't be macro'd.
    private void OnCaptureEngaged(object? sender, EventArgs e)
    {
        if (OnboardingState.HasSeen(KeyHintFlag))
            return;
        OnboardingState.MarkSeen(KeyHintFlag); // mark first, so a hiccup never re-shows it
        new FirstRunKeyHintWindow { Owner = this }.ShowDialog();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a program to launch",
            Filter = "Programs (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) == true)
            _viewModel.BindTarget = dlg.FileName;
    }

    // Records a hotkey combo into the binding form. The field is read-only; pressing keys
    // here builds a string the macro executor understands (e.g. "Ctrl+Shift+Esc").
    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            _viewModel.BindTarget = string.Empty; // Esc clears the recording
            return;
        }
        if (IsModifierKey(key))
            return; // wait for a non-modifier to complete the combo

        string? name = KeyToHotkeyName(key);
        if (name is null)
            return; // unsupported key — don't record a combo that won't fire

        var parts = new List<string>();
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(name);
        _viewModel.BindTarget = string.Join("+", parts);
    }

    private static bool IsModifierKey(Key key) => key
        is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System;

    // Maps a WPF key to a name MacroExecutor.SendHotkey can parse back to a VK.
    private static string? KeyToHotkeyName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (key - Key.NumPad0))).ToString(),
        >= Key.F1 and <= Key.F24 => key.ToString(),
        Key.Enter => "Enter",
        Key.Tab => "Tab",
        Key.Space => "Space",
        Key.Back => "Backspace",
        Key.Delete => "Delete",
        Key.Home => "Home",
        Key.End => "End",
        Key.Up => "Up",
        Key.Down => "Down",
        Key.Left => "Left",
        Key.Right => "Right",
        _ => null,
    };

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
        => _viewModel.RefreshDevices();

    private void ClearButton_Click(object sender, RoutedEventArgs e)
        => _viewModel.ClearLog();

    private void RenameButton_Click(object sender, RoutedEventArgs e)
        => _viewModel.RenameSelected();

    private void AddBindingButton_Click(object sender, RoutedEventArgs e)
        => _viewModel.AddBinding();

    private void RemoveBindingButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MacroBinding binding })
            _viewModel.RemoveBinding(binding);
    }

    private void AddLayerButton_Click(object sender, RoutedEventArgs e)
        => _viewModel.AddLayer();

    private void RemoveLayerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedLayer is { } layer)
            _viewModel.RemoveLayer(layer);
    }
}
