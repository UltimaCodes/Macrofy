using Macrofy.App;
using Macrofy.Core;
using Macrofy.Core.Input;

namespace Macrofy.App.ViewModels;

// The Settings page (and the bits of it the tray and window use): startup, appearance,
// capture options, administrator mode, updates, and window placement.
public sealed partial class MainViewModel
{
    private readonly UpdateService _updates = new();

    public string AppVersion => UpdateService.CurrentVersion;

    // ---- startup & tray ----

    public bool StartWithWindows
    {
        get => AutoStartManager.IsEnabled;
        set { AutoStartManager.SetEnabled(value); OnPropertyChanged(); }
    }

    public bool MinimizeToTrayOnClose
    {
        get => _settings.MinimizeToTrayOnClose;
        set { if (value != _settings.MinimizeToTrayOnClose) { _settings.MinimizeToTrayOnClose = value; _settings.Save(); OnPropertyChanged(); } }
    }

    public bool StartMinimized
    {
        get => _settings.StartMinimized;
        set { if (value != _settings.StartMinimized) { _settings.StartMinimized = value; _settings.Save(); OnPropertyChanged(); } }
    }

    public bool ShowTrayNotifications
    {
        get => _settings.ShowTrayNotifications;
        set { if (value != _settings.ShowTrayNotifications) { _settings.ShowTrayNotifications = value; _settings.Save(); OnPropertyChanged(); } }
    }

    // ---- appearance ----

    public AppTheme[] Themes { get; } = { AppTheme.System, AppTheme.Light, AppTheme.Dark };

    public AppTheme SelectedTheme
    {
        get => _settings.Theme;
        set
        {
            if (value == _settings.Theme)
                return;
            _settings.Theme = value;
            _settings.Save();
            OnPropertyChanged();
            ThemeManager.Apply(value);
        }
    }

    public bool SmoothScrolling
    {
        get => _settings.SmoothScrolling;
        set
        {
            if (value == _settings.SmoothScrolling)
                return;
            _settings.SmoothScrolling = value;
            _settings.Save();
            SmoothScroll.GlobalEnabled = value;
            OnPropertyChanged();
        }
    }

    // ---- devices ----

    private bool _showAllDevices;
    public bool ShowAllDevices
    {
        get => _showAllDevices;
        set
        {
            if (!SetProperty(ref _showAllDevices, value))
                return;
            _settings.ShowAllDevices = value;
            _settings.Save();
            RefreshDevices();
        }
    }

    // ---- auto-capture ----

    public bool AutoCaptureOnLaunch
    {
        get => _settings.AutoCaptureOnLaunch;
        set
        {
            if (value == _settings.AutoCaptureOnLaunch)
                return;
            _settings.AutoCaptureOnLaunch = value;
            // Default the target to the current keyboard the first time it's switched on.
            if (value && string.IsNullOrEmpty(_settings.AutoCaptureDeviceId))
                _settings.AutoCaptureDeviceId = _selectedKeyboard?.Id;
            _settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(AutoCaptureDevice));
        }
    }

    public KeyboardDevice? AutoCaptureDevice
    {
        get => Keyboards.FirstOrDefault(k => k.Id == _settings.AutoCaptureDeviceId);
        set
        {
            // While the list is rebuilding, the combo pushes a transient null - ignore it so
            // the saved keyboard isn't wiped by a Refresh or a rename.
            if (_refreshingDevices || value?.Id == _settings.AutoCaptureDeviceId)
                return;
            _settings.AutoCaptureDeviceId = value?.Id;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    // ---- global capture hotkey ----

    // Raised when the global-hotkey setting changes so the window can (un)register it live.
    public event EventHandler? GlobalHotkeyToggled;

    public bool GlobalHotkeyEnabled
    {
        get => _settings.GlobalHotkeyEnabled;
        set
        {
            if (value == _settings.GlobalHotkeyEnabled)
                return;
            _settings.GlobalHotkeyEnabled = value;
            _settings.Save();
            OnPropertyChanged();
            GlobalHotkeyToggled?.Invoke(this, EventArgs.Empty);
        }
    }

    public int GlobalHotkeyModifiers => _settings.GlobalHotkeyModifiers;
    public int GlobalHotkeyVk => _settings.GlobalHotkeyVk;
    public string GlobalHotkeyDisplay => _settings.GlobalHotkeyDisplay;

    public void SetGlobalHotkey(int modifiers, int vk, string display)
    {
        _settings.GlobalHotkeyModifiers = modifiers;
        _settings.GlobalHotkeyVk = vk;
        _settings.GlobalHotkeyDisplay = display;
        _settings.Save();
        OnPropertyChanged(nameof(GlobalHotkeyDisplay));
        GlobalHotkeyToggled?.Invoke(this, EventArgs.Empty); // re-register with the new combo
    }

    // The window couldn't register the hotkey (another app owns it). Shown under the setting.
    private string _globalHotkeyProblem = string.Empty;
    public string GlobalHotkeyProblem
    {
        get => _globalHotkeyProblem;
        set
        {
            if (SetProperty(ref _globalHotkeyProblem, value ?? string.Empty))
                OnPropertyChanged(nameof(HasGlobalHotkeyProblem));
        }
    }

    public bool HasGlobalHotkeyProblem => _globalHotkeyProblem.Length > 0;

    // ---- administrator ----

    public bool CanElevate => !ProcessElevation.IsElevated;

    public string ElevationStatus => ProcessElevation.IsElevated
        ? "Running as administrator, so capture also covers apps that run as administrator. Apps your macros open run as administrator too."
        : "Run as administrator to capture keys in apps that run as administrator. Microsoft Store apps and games that read the keyboard directly still aren't covered.";

    public bool AlwaysRunAsAdmin
    {
        get => _settings.AlwaysRunAsAdmin;
        set { if (value != _settings.AlwaysRunAsAdmin) { _settings.AlwaysRunAsAdmin = value; _settings.Save(); OnPropertyChanged(); } }
    }

    // ---- profiles ----

    public void DeleteAllProfiles()
    {
        _library.DeleteAll();
        LoadProfileForSelected();
        CloseEditor();
        ShowToast("All profiles deleted");
    }

    // ---- updates ----

    public bool CanUpdate => _updates.IsInstalled;

    public bool CheckForUpdates
    {
        get => _settings.CheckForUpdates;
        set { if (value != _settings.CheckForUpdates) { _settings.CheckForUpdates = value; _settings.Save(); OnPropertyChanged(); } }
    }

    private string _updateStatus = string.Empty;
    public string UpdateStatus
    {
        get => _updateStatus.Length > 0 ? _updateStatus
            : CanUpdate ? $"You're on version {AppVersion}."
            : $"Version {AppVersion}. Updates arrive automatically when Macrofy is installed with the installer.";
        private set => SetProperty(ref _updateStatus, value);
    }

    private string? _availableVersion;
    public bool IsUpdateAvailable => _availableVersion is not null;

    private bool _isUpdating;
    public bool IsUpdating
    {
        get => _isUpdating;
        private set => SetProperty(ref _isUpdating, value);
    }

    // userAsked: from the Settings button (say "up to date"); otherwise a quiet startup check
    // that only speaks up when there is something new.
    public async Task CheckForUpdatesAsync(bool userAsked)
    {
        if (!CanUpdate || _isUpdating)
            return;
        IsUpdating = true;
        if (userAsked)
            UpdateStatus = "Checking for updates…";
        try
        {
            _availableVersion = await _updates.CheckAsync();
            OnPropertyChanged(nameof(IsUpdateAvailable));
            if (_availableVersion is not null)
            {
                UpdateStatus = $"Version {_availableVersion} is ready to install.";
                if (!userAsked)
                    ShowToast($"Macrofy {_availableVersion} is out. Install it from Settings.");
            }
            else if (userAsked)
            {
                UpdateStatus = $"You're up to date (version {AppVersion}).";
            }
        }
        catch (Exception ex)
        {
            if (userAsked)
                UpdateStatus = $"Couldn't check for updates: {ex.Message}";
        }
        finally
        {
            IsUpdating = false;
        }
    }

    public async Task InstallUpdateAsync()
    {
        if (!IsUpdateAvailable || _isUpdating)
            return;
        IsUpdating = true;
        try
        {
            await _updates.InstallAndRestartAsync(p => RunOnUi(() => UpdateStatus = $"Downloading… {p}%"));
        }
        catch (Exception ex)
        {
            UpdateStatus = $"The update didn't install: {ex.Message}";
        }
        finally
        {
            IsUpdating = false;
        }
    }

    // ---- remembered window placement ----

    public (double Left, double Top, double Width, double Height, bool Maximized) GetSavedPlacement()
        => (_settings.WindowLeft, _settings.WindowTop, _settings.WindowWidth, _settings.WindowHeight, _settings.WindowMaximized);

    public void SaveWindowPlacement(double left, double top, double width, double height, bool maximized)
    {
        _settings.WindowLeft = left;
        _settings.WindowTop = top;
        _settings.WindowWidth = width;
        _settings.WindowHeight = height;
        _settings.WindowMaximized = maximized;
        _settings.Save();
    }
}
