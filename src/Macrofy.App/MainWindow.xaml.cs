using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Macrofy.App.ViewModels;
using Macrofy.Core.Input;
using Wpf.Ui.Controls;

namespace Macrofy.App;

public partial class MainWindow : FluentWindow
{
    private const string KeyHintFlag = "key-capture-hint";
    private const string TrayHintFlag = "tray-hint";

    private readonly MainViewModel _viewModel;
    private System.Windows.Forms.NotifyIcon? _tray;
    private TrayIcons? _trayIcons;
    private bool _reallyExit;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel(new WhKeyboardBackend());
        _viewModel.CaptureEngaged += OnCaptureEngaged;
        _viewModel.Toast += OnToast;
        _viewModel.NavigateRequested += (_, page) => Navigate(page);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        DataContext = _viewModel;

        ApplyAppIcon();
        RestoreWindowPlacement();
        ClampToWorkArea();
        Loaded += OnLoaded;

        // Fallback dragging: if the title bar's built-in drag ever fails to engage, an
        // unhandled press on it still moves the window.
        AppTitleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };

        InitTray();

        // Pause capture while any text field has focus so the captured keyboard can type into
        // it, and resume the moment focus moves elsewhere. Both focus events feed one rule so
        // every transition (into/out of/between fields, or losing focus to another app) is right.
        AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnKeyboardFocusChanged), handledEventsToo: true);
        AddHandler(Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnKeyboardFocusChanged), handledEventsToo: true);

        _viewModel.GlobalHotkeyToggled += (_, _) => ApplyGlobalHotkey();
        Closed += (_, _) => _viewModel.Dispose();

        // Force the window handle so the global hotkey can register even if we launch hidden.
        new WindowInteropHelper(this).EnsureHandle();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        MaybeShowWelcome();
        if (_viewModel.CheckForUpdates)
            await _viewModel.CheckForUpdatesAsync(userAsked: false);
    }

    // ---- navigation ----

    private void Navigate(AppPage page)
    {
        var target = page switch
        {
            AppPage.Profiles => NavProfiles,
            AppPage.Settings => NavSettings,
            AppPage.About => NavAbout,
            _ => NavKeyboards,
        };
        target.IsChecked = true;
    }

    // ---- global capture hotkey ----

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int HotkeyId = 0xB001;
    private const uint ModNoRepeat = 0x4000;
    private const int WmHotkey = 0x0312;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndHook);
        ApplyGlobalHotkey();
    }

    private IntPtr WndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            _viewModel.ToggleCapture();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ApplyGlobalHotkey()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;
        UnregisterHotKey(handle, HotkeyId);
        _viewModel.GlobalHotkeyProblem = string.Empty;
        if (_viewModel.GlobalHotkeyEnabled && _viewModel.GlobalHotkeyVk != 0
            && !RegisterHotKey(handle, HotkeyId, (uint)_viewModel.GlobalHotkeyModifiers | ModNoRepeat, (uint)_viewModel.GlobalHotkeyVk))
        {
            _viewModel.GlobalHotkeyProblem =
                $"Another app is already using {_viewModel.GlobalHotkeyDisplay}, so it won't work here. Pick a different shortcut.";
        }
    }

    private void OnKeyboardFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
        => _viewModel.SetCaptureSuspended(e.NewFocus is System.Windows.Controls.TextBox);

    // ---- branding, window placement, toasts ----

    private void ApplyAppIcon()
    {
        try
        {
            if (Environment.ProcessPath is not { } exe)
                return;
            using var ico = System.Drawing.Icon.ExtractAssociatedIcon(exe);
            if (ico is null)
                return;
            var src = Imaging.CreateBitmapSourceFromHIcon(
                ico.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            Icon = src;
            AppTitleBar.Icon = new ImageIcon { Source = src };
        }
        catch { /* fall back to the default chrome icon */ }
    }

    private void RestoreWindowPlacement()
    {
        var (left, top, width, height, max) = _viewModel.GetSavedPlacement();
        if (double.IsNaN(left) || double.IsNaN(top) || width < 200 || height < 200)
            return;
        bool onScreen = left + width > SystemParameters.VirtualScreenLeft
            && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
            && top + height > SystemParameters.VirtualScreenTop
            && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
        if (!onScreen)
            return; // a monitor probably got unplugged; fall back to centered
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left; Top = top; Width = Math.Max(width, MinWidth); Height = Math.Max(height, MinHeight);
        if (max)
            WindowState = WindowState.Maximized;
    }

    // Fit small screens (1366x768 laptops): the default window centered on the primary monitor
    // can be taller than the work area, putting the title bar above the top edge with nothing
    // to grab. Shrink it to fit. A restored position was already validated against every monitor
    // in RestoreWindowPlacement, so it's left alone - re-clamping it to the PRIMARY monitor's
    // work area is exactly what used to drag windows off a second monitor.
    private void ClampToWorkArea()
    {
        if (WindowStartupLocation == WindowStartupLocation.Manual)
            return;

        var wa = SystemParameters.WorkArea; // primary monitor; CenterScreen centers there
        if (Width > wa.Width) Width = Math.Max(MinWidth, wa.Width - 16);
        if (Height > wa.Height) Height = Math.Max(MinHeight, wa.Height - 16);
    }

    private void SaveWindowPlacement()
    {
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (b.Width > 0 && b.Height > 0)
            _viewModel.SaveWindowPlacement(b.Left, b.Top, b.Width, b.Height, WindowState == WindowState.Maximized);
    }

    private void MaybeShowWelcome()
    {
        if (!IsVisible || OnboardingState.HasSeen("welcome"))
            return;
        OnboardingState.MarkSeen("welcome");
        new WelcomeWindow { Owner = this }.ShowDialog();
    }

    private void OnToast(object? sender, string message)
    {
        var snackbar = new Snackbar(SnackbarHost)
        {
            Content = message,
            Timeout = TimeSpan.FromSeconds(message.Length > 70 ? 6 : 2.5),
            Appearance = ControlAppearance.Secondary,
        };
        snackbar.Show();
    }

    // First time the user turns on capture, explain the keys that can't be macro'd.
    private void OnCaptureEngaged(object? sender, EventArgs e)
    {
        if (OnboardingState.HasSeen(KeyHintFlag) || !IsVisible)
            return;
        OnboardingState.MarkSeen(KeyHintFlag); // mark first, so a hiccup never re-shows it
        new FirstRunKeyHintWindow { Owner = this }.ShowDialog();
    }

    // ---- system tray ----

    private void InitTray()
    {
        _trayIcons = new TrayIcons(LoadTrayIcon());

        var open = new System.Windows.Forms.ToolStripMenuItem("Open Macrofy", null, (_, _) => BringToFront())
        {
            Font = new System.Drawing.Font("Segoe UI Semibold", 9.5f),
        };
        var capture = new System.Windows.Forms.ToolStripMenuItem("Capture", null, (_, _) => _viewModel.ToggleCapture());
        var profiles = new System.Windows.Forms.ToolStripMenuItem("Profile");
        var autoStart = new System.Windows.Forms.ToolStripMenuItem("Start with Windows", null,
            (_, _) => _viewModel.StartWithWindows = !_viewModel.StartWithWindows);
        var quit = new System.Windows.Forms.ToolStripMenuItem("Quit Macrofy", null, (_, _) => ExitApp());

        var menu = new System.Windows.Forms.ContextMenuStrip
        {
            Renderer = new System.Windows.Forms.ToolStripProfessionalRenderer(new DarkMenuColors()) { RoundedEdges = true },
            BackColor = DarkMenuColors.Background,
            ForeColor = DarkMenuColors.Text,
            Font = new System.Drawing.Font("Segoe UI", 9.5f),
            Padding = new System.Windows.Forms.Padding(3),
            ShowImageMargin = true,
        };
        menu.Items.AddRange(new System.Windows.Forms.ToolStripItem[]
        {
            open, new System.Windows.Forms.ToolStripSeparator(), capture, profiles,
            new System.Windows.Forms.ToolStripSeparator(), autoStart, quit,
        });
        foreach (System.Windows.Forms.ToolStripItem item in menu.Items)
        {
            item.ForeColor = DarkMenuColors.Text;
            item.Padding = new System.Windows.Forms.Padding(6, 3, 6, 3);
        }

        // Reflect live state each time the menu opens.
        menu.Opening += (_, _) =>
        {
            capture.Checked = _viewModel.IsCapturing;
            capture.Enabled = _viewModel.HasSelection;
            capture.Text = _viewModel.HasSelection ? $"Capture {_viewModel.KeyboardName}" : "Capture";
            autoStart.Checked = _viewModel.StartWithWindows;

            profiles.DropDownItems.Clear();
            profiles.Enabled = _viewModel.HasSelection && _viewModel.LibraryProfiles.Count > 0;
            string? activeId = _viewModel.ActiveProfile?.Id;
            foreach (var profile in _viewModel.LibraryProfiles)
            {
                var item = new System.Windows.Forms.ToolStripMenuItem(profile.Name, null, (_, _) => _viewModel.ApplyProfile(profile))
                {
                    Checked = string.Equals(profile.Id, activeId, StringComparison.OrdinalIgnoreCase),
                    ForeColor = DarkMenuColors.Text,
                    BackColor = DarkMenuColors.Background,
                };
                profiles.DropDownItems.Add(item);
            }
        };

        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = _trayIcons.Idle,
            Text = "Macrofy",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => BringToFront();
        UpdateTray();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsCapturing) or nameof(MainViewModel.HasHookIssue)
            or nameof(MainViewModel.SelectedKeyboard) or nameof(MainViewModel.CaptureStateText))
            UpdateTray();
    }

    // The tray icon shows capture state at a glance; its tooltip says which keyboard.
    private void UpdateTray()
    {
        if (_tray is null || _trayIcons is null)
            return;
        _tray.Icon = _viewModel.HasHookIssue ? _trayIcons.Problem
            : _viewModel.IsCapturing ? _trayIcons.Capturing
            : _trayIcons.Idle;
        string text = _viewModel.HasHookIssue ? "Macrofy: capture can't start (open for details)"
            : _viewModel.IsCapturing ? $"Macrofy: capturing {_viewModel.KeyboardName}"
            : "Macrofy: capture is off";
        _tray.Text = text.Length > 120 ? text[..120] : text;
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            if (Environment.ProcessPath is { } exe && System.Drawing.Icon.ExtractAssociatedIcon(exe) is { } ico)
                return ico;
        }
        catch { /* fall back below */ }
        return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
    }

    public void BringToFront()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false; // nudge to the foreground without staying pinned
    }

    // Closing the window hides to tray (when that setting is on); otherwise it quits.
    // Quit from the tray menu always exits.
    protected override void OnClosing(CancelEventArgs e)
    {
        SaveWindowPlacement();
        if (_reallyExit)
        {
            base.OnClosing(e);
            return;
        }

        if (_viewModel.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
            if (_viewModel.ShowTrayNotifications && !OnboardingState.HasSeen(TrayHintFlag))
            {
                OnboardingState.MarkSeen(TrayHintFlag);
                _tray?.ShowBalloonTip(3000, "Macrofy is still running",
                    "Macros keep working in the background. Right-click the tray icon to quit, or change this in Settings.",
                    System.Windows.Forms.ToolTipIcon.Info);
            }
            return;
        }

        // Tray-on-close is off: a window close should quit the app cleanly.
        e.Cancel = true;
        ExitApp();
    }

    public void ExitApp()
    {
        _reallyExit = true;
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        _trayIcons?.Dispose();
        _trayIcons = null;
        Application.Current.Shutdown();
    }
}
