using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using Macrofy.App;
using Macrofy.Core;
using Macrofy.Core.Input;
using Macrofy.Core.Macros;

namespace Macrofy.App.ViewModels;

public enum AppPage { Keyboards, Profiles, Settings, About }

// Drives the whole window. This file: keyboards, capture, the profile on the selected
// keyboard, layers, and the captured-key pipeline. The key editor, the on-screen layout and
// the settings live in the other MainViewModel.*.cs files.
//
// Captured-key events are dropped into a lock-free queue on the backend (decider) thread and
// drained by a UI timer - UI work must never run on that thread, which has a hard latency
// budget for answering the hook's block/pass question.
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IInputBackend _backend;
    private readonly DeviceNameStore _nameStore = new();
    private readonly DeviceLayoutStore _layoutStore = new();
    private readonly ProfileLibrary _library;
    private readonly MacroEngine _macroEngine = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ConcurrentQueue<DeviceKeyEvent> _pending = new();
    private readonly DispatcherTimer _drainTimer;

    private MacroProfile? _profile;
    private bool _refreshingDevices;

    public ObservableCollection<KeyboardDevice> Keyboards { get; } = new();
    public ObservableCollection<MacroBinding> Bindings { get; } = new();
    public ObservableCollection<MacroLayer> Layers { get; } = new();
    public ObservableCollection<string> LayerTargets { get; } = new();
    public ObservableCollection<string> LearnedKeys { get; } = new();
    public ObservableCollection<MacroStep> PendingSteps { get; } = new();

    public ProfilesViewModel Profiles { get; }

    public MainViewModel(IInputBackend backend)
    {
        _backend = backend;
        _library = new ProfileLibrary(AppPaths.DataDir, _nameStore.Get);
        Profiles = new ProfilesViewModel(_library, this);
        _showAllDevices = _settings.ShowAllDevices;

        _backend.CapturedKey += OnCapturedKey;
        _backend.DevicesChanged += OnDevicesChanged;
        _backend.IsolationMiss += OnIsolationMiss;
        _backend.HookStatusChanged += OnHookStatusChanged;
        _backend.DeviceIdentified += OnDeviceIdentified;
        _library.Changed += OnLibraryChanged;
        MacroExecutor.ActionFailed += OnActionFailed;
        _backend.Start();

        _macroEngine.ActiveLayerChanged += OnActiveLayerChanged;
        SmoothScroll.GlobalEnabled = _settings.SmoothScrolling;

        // Only runs while capturing or calibrating - no 60 Hz wakeups sitting idle in the tray.
        _drainTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _drainTimer.Tick += (_, _) => Drain();

        RefreshDevices();
        Profiles.Refresh();
        ApplyAutoCapture();

        if (_library.SetAsideFiles.Count > 0)
            PostToast("A damaged profile file was set aside instead of loaded. It's in the library folder as *.bak.");
    }

    // ---- events the window listens to ----

    // Raised the first moment capture is turned on, so the window can show the one-time
    // "some keys can't be macro'd" hint at a point where it's actually relevant.
    public event EventHandler? CaptureEngaged;

    // A small confirmation toast in the window.
    public event EventHandler<string>? Toast;

    // Something wants the window to show a page (e.g. after using a template).
    public event EventHandler<AppPage>? NavigateRequested;

    // A keyboard switch was refused because capture is on; the view nudges the capture switch.
    public event EventHandler? CaptureLocked;

    public void ShowToast(string message) => Toast?.Invoke(this, message);

    // Toast once the window is listening (used during startup).
    private static void PostToast(Action show)
        => System.Windows.Application.Current?.Dispatcher.BeginInvoke(show, DispatcherPriority.ApplicationIdle);

    private void PostToast(string message) => PostToast(() => ShowToast(message));

    public void RequestNavigate(AppPage page) => NavigateRequested?.Invoke(this, page);

    // Run an action on the UI thread. The backend raises its events from the decider thread,
    // which must never touch the ObservableCollections or the dispatcher-bound UI directly.
    private static void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action);
    }

    // ---- keyboards ----

    private KeyboardDevice? _selectedKeyboard;
    public KeyboardDevice? SelectedKeyboard => _selectedKeyboard;
    public bool HasSelection => _selectedKeyboard is not null;
    public bool HasKeyboards => Keyboards.Count > 0;

    public string KeyboardName => _selectedKeyboard?.DisplayName ?? "No keyboard";

    public string ConnectionText => _selectedKeyboard?.Connection switch
    {
        ConnectionKind.Usb => "USB",
        ConnectionKind.Bluetooth => "Bluetooth",
        ConnectionKind.BuiltIn => "Built-in",
        ConnectionKind.Virtual => "Virtual",
        null => string.Empty,
        _ => "Keyboard",
    };

    // Switch to another keyboard. Refused while capturing: capture stays on the keyboard it
    // was turned on for until it's switched off.
    public bool SelectKeyboard(KeyboardDevice? device)
    {
        if (_isCapturing && device?.Id != _selectedKeyboard?.Id)
        {
            CaptureLocked?.Invoke(this, EventArgs.Empty);
            return false;
        }
        SetSelectedKeyboard(device);
        return true;
    }

    private void SetSelectedKeyboard(KeyboardDevice? device)
    {
        bool sameDevice = device is not null && device.Id == _selectedKeyboard?.Id;
        _selectedKeyboard = device;
        OnPropertyChanged(nameof(SelectedKeyboard));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(KeyboardName));
        OnPropertyChanged(nameof(ConnectionText));
        if (!sameDevice)
        {
            LoadProfileForSelected();
            CloseEditor();
        }
        OnPropertyChanged(nameof(StatusText));
        Profiles.Refresh();
    }

    public void RefreshDevices()
    {
        string? previous = _selectedKeyboard?.Id;
        // Clearing the list makes the auto-capture dropdown null its own binding; the guard
        // stops that from erasing the saved device.
        _refreshingDevices = true;
        try
        {
            Keyboards.Clear();
            foreach (var kb in _backend.GetKeyboards(_showAllDevices))
            {
                // Older versions keyed names and layouts by a different id; bring them forward.
                foreach (var legacy in kb.LegacyIds)
                {
                    _nameStore.CopyIfMissing(legacy, kb.Id);
                    _layoutStore.CopyIfMissing(legacy, kb.Id);
                }
                var custom = _nameStore.Get(kb.Id);
                Keyboards.Add(custom is null ? kb : kb with { DisplayName = custom });
            }
        }
        finally
        {
            _refreshingDevices = false;
        }

        var match = Keyboards.FirstOrDefault(k => k.Id == previous);
        if (match is null && _isCapturing)
        {
            // The captured keyboard went away (unplugged, out of battery). Auto-capture grabs
            // it again when it comes back.
            string name = _selectedKeyboard?.DisplayName ?? "The keyboard";
            IsCapturing = false;
            ShowToast($"{name} disconnected, so capture stopped.");
        }
        SetSelectedKeyboard(match ?? Keyboards.FirstOrDefault());
        OnPropertyChanged(nameof(HasKeyboards));
        OnPropertyChanged(nameof(AutoCaptureDevice)); // re-point the dropdown at the saved id
    }

    public void RenameSelected(string? name)
    {
        if (_selectedKeyboard is null)
            return;
        _nameStore.Set(_selectedKeyboard.Id, name);
        RefreshDevices();
    }

    // A keyboard's name for messages and the Profiles page, whether or not it's plugged in.
    public string DisplayNameFor(string deviceId)
        => Keyboards.FirstOrDefault(k => k.Id == deviceId)?.DisplayName
           ?? _nameStore.Get(deviceId)
           ?? deviceId;

    // At launch, if enabled, select the chosen keyboard and start capturing it so an autostarted
    // Macrofy is immediately live without opening the window. Runs before the window subscribes
    // to CaptureEngaged, so the first-run key hint won't pop during a silent startup.
    private void ApplyAutoCapture()
    {
        if (!_settings.AutoCaptureOnLaunch || string.IsNullOrEmpty(_settings.AutoCaptureDeviceId))
            return;
        var device = Keyboards.FirstOrDefault(k => k.Id == _settings.AutoCaptureDeviceId);
        if (device is null)
            return;
        SetSelectedKeyboard(device);
        IsCapturing = true;
    }

    // A keyboard was plugged in or removed. Refresh the list, and if auto-capture is armed and
    // we're idle, grab the chosen keyboard now that it may have appeared.
    private void OnDevicesChanged(object? sender, EventArgs e) => RunOnUi(() =>
    {
        RefreshDevices();
        if (!_isCapturing && !_isLearning)
            ApplyAutoCapture();
    });

    // ---- pick a keyboard by pressing one of its keys ----

    private bool _isIdentifying;
    public bool IsIdentifying
    {
        get => _isIdentifying;
        private set => SetProperty(ref _isIdentifying, value);
    }

    public void BeginIdentify()
    {
        if (_isCapturing)
        {
            CaptureLocked?.Invoke(this, EventArgs.Empty);
            ShowToast("Turn capture off to pick a different keyboard.");
            return;
        }
        if (_isLearning)
            return;
        _backend.BeginIdentify();
        IsIdentifying = true;
    }

    public void CancelIdentify()
    {
        _backend.CancelIdentify();
        IsIdentifying = false;
    }

    private void OnDeviceIdentified(object? sender, string path) => RunOnUi(() =>
    {
        if (!_isIdentifying)
            return;
        IsIdentifying = false;
        var device = FindByPath(path);
        if (device is null && !_showAllDevices)
        {
            // Some macro pads don't describe themselves as keyboards; show everything.
            ShowAllDevices = true;
            device = FindByPath(path);
        }
        if (device is null)
        {
            ShowToast("That key came from a device Macrofy can't use.");
            return;
        }
        if (SelectKeyboard(device))
            ShowToast($"Picked {device.DisplayName}");
    });

    private KeyboardDevice? FindByPath(string path)
        => Keyboards.FirstOrDefault(k => k.DevicePaths.Contains(path, StringComparer.OrdinalIgnoreCase));

    // ---- capture ----

    private bool _isCapturing;
    public bool IsCapturing
    {
        get => _isCapturing;
        set
        {
            if (value && _selectedKeyboard is null)
                value = false;
            if (SetProperty(ref _isCapturing, value))
            {
                if (value && _isIdentifying)
                    CancelIdentify();
                ApplyCapture();
                UpdateDrainTimer();
                if (value)
                    CaptureEngaged?.Invoke(this, EventArgs.Empty);
                else
                    KeyboardLayout.Reset();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(CaptureStateText));
            }
        }
    }

    public string CaptureStateText => _isCapturing ? (_captureSuspended ? "Paused" : "Capturing") : "Off";

    public string StatusText => _selectedKeyboard is null
        ? "Connect a keyboard to get started."
        : _isLearning
            ? "Press every key on this keyboard once, then hit Save layout."
            : _isCapturing && _captureSuspended
                ? "Paused while you type in a text box. Click anywhere else to resume."
                : _isCapturing
                    ? "Capturing. Its keys run your macros instead of typing."
                    : "Not captured. It types normally until you turn capture on.";

    // While a text field is focused we pause the actual blocking (so the captured keyboard can
    // type into it) without flipping IsCapturing - the switch stays on and we resume on blur.
    private bool _captureSuspended;
    public void SetCaptureSuspended(bool suspend)
    {
        if (_isLearning || suspend == _captureSuspended)
            return;
        _captureSuspended = suspend;
        if (suspend)
            KeyboardLayout.Reset(); // don't leave a key lit while blocking is paused
        ApplyCapture();
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CaptureStateText));
    }

    // Invoked by the global hotkey and the tray menu.
    public void ToggleCapture()
    {
        if (_selectedKeyboard is not null && !_isLearning)
            IsCapturing = !_isCapturing;
    }

    private void ApplyCapture()
    {
        // Calibration owns the capture state (device grabbed, macros off). Don't let a focus
        // change or the global hotkey pull the keyboard out from under it.
        if (_isLearning)
            return;

        if (_isCapturing && !_captureSuspended && _selectedKeyboard is not null && _profile is not null)
        {
            _backend.SetCapturedDevices(_selectedKeyboard.DevicePaths);
            _macroEngine.SetProfile(_profile);
        }
        else
        {
            // Stop blocking the device. Only tear down the engine when capture is truly off -
            // a temporary suspend (still capturing) keeps the profile so resume is instant.
            _backend.SetCapturedDevices(Array.Empty<string>());
            if (!_isCapturing)
                _macroEngine.Clear();
        }
    }

    // A problem that stopped capture working (missing/blocked hook DLL, hook refused). Null when
    // all is well. Drives a warning banner on the Keyboards page and the tray icon.
    public string? HookError => _backend.HookError;
    public bool HasHookIssue => !string.IsNullOrEmpty(_backend.HookError);

    private void OnHookStatusChanged(object? sender, EventArgs e) => RunOnUi(() =>
    {
        OnPropertyChanged(nameof(HookError));
        OnPropertyChanged(nameof(HasHookIssue));
    });

    // The hook let captured keys through in some app. Tell the user which app and why, so a
    // silent isolation failure (a game, a Store app, an elevated app) isn't a mystery.
    private void OnIsolationMiss(object? sender, uint pid) => RunOnUi(() =>
    {
        var (name, kind) = ForegroundApp.Describe(pid);
        string why = kind switch
        {
            AppKind.StoreApp => "it's a Microsoft Store app, which Windows won't let Macrofy hook",
            AppKind.Elevated => "it's running as administrator - restart Macrofy as administrator to cover it",
            _ => "it reads the keyboard directly, so capture can't block it there",
        };
        ShowToast($"Keys from this keyboard are still reaching {name} ({why}).");
    });

    private void OnActionFailed(MacroAction action, string message) => RunOnUi(() => ShowToast(message));

    // ---- the profile on the selected keyboard ----

    public MacroProfile? ActiveProfile => _profile;
    public string ActiveProfileName => _profile is null ? "No profile" : _profile.Name;
    public IReadOnlyList<MacroProfile> LibraryProfiles => _library.Profiles;

    public string KeyboardSubtitle
    {
        get
        {
            if (_selectedKeyboard is null)
                return string.Empty;
            int keys = _profile?.KeyCount ?? 0;
            string bound = keys == 0 ? "no macros yet" : keys == 1 ? "1 macro" : $"{keys} macros";
            return string.IsNullOrEmpty(ConnectionText) ? bound : $"{ConnectionText} · {bound}";
        }
    }

    // Put a library profile on the selected keyboard.
    public void ApplyProfile(MacroProfile profile)
    {
        if (_selectedKeyboard is null || string.IsNullOrEmpty(profile.Id))
            return;
        _library.Assign(_selectedKeyboard.Id, profile.Id);
        LoadProfileForSelected();
        CloseEditor();
        ShowToast($"Using \"{profile.Name}\" on {_selectedKeyboard.DisplayName}");
    }

    public void OnProfileDeleted(string id)
    {
        if (string.Equals(_profile?.Id, id, StringComparison.OrdinalIgnoreCase))
        {
            LoadProfileForSelected();
            CloseEditor();
        }
    }

    public void OnProfileRenamed(MacroProfile profile) => NotifyProfile();

    private void OnLibraryChanged(object? sender, EventArgs e)
    {
        Profiles.Refresh();
        NotifyProfile();
    }

    private void NotifyProfile()
    {
        OnPropertyChanged(nameof(ActiveProfile));
        OnPropertyChanged(nameof(ActiveProfileName));
        OnPropertyChanged(nameof(KeyboardSubtitle));
        OnPropertyChanged(nameof(LibraryProfiles));
    }

    private void LoadProfileForSelected()
    {
        Layers.Clear();
        Bindings.Clear();
        LayerTargets.Clear();
        LoadLayoutForSelected();
        if (_selectedKeyboard is null)
        {
            _profile = null;
            SelectedLayer = null;
            NotifyProfile();
            return;
        }
        _profile = _library.ForDevice(_selectedKeyboard.Id, _selectedKeyboard.DisplayName, _selectedKeyboard.LegacyIds);
        foreach (var l in _profile.Layers)
            Layers.Add(l);
        SelectedLayer = Layers.FirstOrDefault(); // Base; refreshes Bindings, LayerTargets, keycaps
        if (_isCapturing)
            _macroEngine.SetProfile(_profile);
        NotifyProfile();
    }

    // Save the active profile after an edit. A keyboard's first macro adds its (until then
    // unsaved) profile to the library and assigns it.
    private void SaveProfile()
    {
        if (_profile is null || _selectedKeyboard is null)
            return;
        bool firstSave = string.IsNullOrEmpty(_profile.Id);
        _library.Save(_profile);
        if (firstSave)
            _library.Assign(_selectedKeyboard.Id, _profile.Id);
        if (_isCapturing)
            _macroEngine.SetProfile(_profile);
        RefreshKeycaps();
        NotifyProfile();
    }

    // ---- layers ----

    private string _activeLayerName = "Base";
    public string ActiveLayerName
    {
        get => _activeLayerName;
        private set => SetProperty(ref _activeLayerName, value);
    }

    // The engine raises this from the decider thread when a LayerHold/Toggle fires; hop to the
    // UI thread BEFORE touching the Layers collection (it isn't safe to read off-thread).
    private void OnActiveLayerChanged(object? sender, int index)
        => RunOnUi(() => ActiveLayerName = index >= 0 && index < Layers.Count ? Layers[index].Name : "Base");

    private MacroLayer? _selectedLayer;
    public MacroLayer? SelectedLayer
    {
        get => _selectedLayer;
        set
        {
            if (SetProperty(ref _selectedLayer, value))
            {
                RefreshBindingsForLayer();
                RefreshLayerTargets();
                RefreshKeycaps();
                OnPropertyChanged(nameof(CanRemoveLayer));
                if (HasBindKey)
                    LoadBindingForEdit(_bindKeyCode);
            }
        }
    }

    public bool CanRemoveLayer =>
        _profile is not null && _selectedLayer is not null
        && _profile.Layers.Count > 1 && !ReferenceEquals(_selectedLayer, _profile.BaseLayer);

    private void RefreshBindingsForLayer()
    {
        Bindings.Clear();
        if (_selectedLayer is null)
            return;
        foreach (var b in _selectedLayer.Bindings.OrderBy(b => b.KeyCode))
            Bindings.Add(b);
    }

    private void RefreshLayerTargets()
    {
        LayerTargets.Clear();
        if (_profile is not null && _selectedLayer is not null)
            foreach (var l in _profile.Layers)
                if (!ReferenceEquals(l, _selectedLayer))
                    LayerTargets.Add(l.Name);
        OnPropertyChanged(nameof(ShowLayerPicker));
        OnPropertyChanged(nameof(ShowAddLayerHint));
    }

    public void AddLayer()
    {
        if (_profile is null)
            return;
        int n = _profile.Layers.Count + 1;
        string name;
        do { name = $"Layer {n}"; n++; }
        while (_profile.Layers.Any(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)));

        var layer = new MacroLayer { Name = name };
        _profile.Layers.Add(layer);
        Layers.Add(layer);
        SaveProfile();
        SelectedLayer = layer;
    }

    public void RemoveLayer(MacroLayer layer)
    {
        if (_profile is null || _profile.Layers.Count <= 1 || ReferenceEquals(layer, _profile.BaseLayer))
            return;
        _profile.Layers.Remove(layer);
        Layers.Remove(layer);
        SaveProfile();
        SelectedLayer = _profile.BaseLayer;
    }

    public void RenameLayer(MacroLayer layer, string? newName)
    {
        if (_profile is null)
            return;
        newName = (newName ?? string.Empty).Trim();
        if (newName.Length == 0 || string.Equals(layer.Name, newName, StringComparison.Ordinal))
            return;
        if (_profile.Layers.Any(l => !ReferenceEquals(l, layer)
                && string.Equals(l.Name, newName, StringComparison.OrdinalIgnoreCase)))
            return; // name already in use

        string old = layer.Name;
        layer.Name = newName;

        // Keep any LayerHold/LayerToggle bindings that point at this layer working.
        foreach (var l in _profile.Layers)
            foreach (var b in l.Bindings)
                foreach (var a in b.HasSteps ? b.Steps.Select(s => s.Action) : new[] { b.Action })
                    if (a.Kind is MacroActionKind.LayerHold or MacroActionKind.LayerToggle
                        && string.Equals(a.Target, old, StringComparison.OrdinalIgnoreCase))
                        a.Target = newName;

        SaveProfile();
        // MacroLayer has no change notification, so rebuild the chips to show the new name.
        var keep = _selectedLayer;
        Layers.Clear();
        foreach (var l in _profile.Layers)
            Layers.Add(l);
        SelectedLayer = keep;
        RefreshLayerTargets();
    }

    // ---- captured keys ----

    // Decider thread: do the absolute minimum and return.
    private void OnCapturedKey(object? sender, DeviceKeyEvent e)
    {
        _pending.Enqueue(e);
        _macroEngine.OnCapturedKey(e);
    }

    // The drain timer only needs to run while keys can actually arrive.
    private void UpdateDrainTimer()
    {
        bool active = _isCapturing || _isLearning;
        if (active && !_drainTimer.IsEnabled)
            _drainTimer.Start();
        else if (!active && _drainTimer.IsEnabled)
        {
            _drainTimer.Stop();
            _pending.Clear();
        }
    }

    // UI thread: drain the queue into the visuals + the binding picker.
    private void Drain()
    {
        while (_pending.TryDequeue(out var e))
        {
            if (_isLearning)
            {
                // Calibration: record the unique keys this device emits (one per physical key).
                if (e.IsKeyDown && !e.IsRepeat && _learnedKeyCodes.Add(e.KeyCode))
                    LearnedKeys.Add(VirtualKeyNames.NameForKey(e.KeyCode));
                continue;
            }
            KeyboardLayout.SetPressed(e.KeyCode, e.IsKeyDown);

            // Pressing a key selects it in the editor and loads its macro - but not while the
            // editor has unsaved changes, so testing keys can't throw away an edit.
            if (e.IsKeyDown && !e.IsRepeat && !IsDirty)
                SelectKeyForEdit(e.KeyCode);
        }
    }

    public void Dispose()
    {
        _drainTimer.Stop();
        _macroEngine.ActiveLayerChanged -= OnActiveLayerChanged;
        _backend.CapturedKey -= OnCapturedKey;
        _backend.DevicesChanged -= OnDevicesChanged;
        _backend.IsolationMiss -= OnIsolationMiss;
        _backend.HookStatusChanged -= OnHookStatusChanged;
        _backend.DeviceIdentified -= OnDeviceIdentified;
        _library.Changed -= OnLibraryChanged;
        MacroExecutor.ActionFailed -= OnActionFailed;
        _backend.Dispose();
    }
}

// One entry in the media-key dropdown: a friendly label and the token the executor maps.
public sealed class MediaKeyOption
{
    public MediaKeyOption(string label, string token)
    {
        Label = label;
        Token = token;
    }

    public string Label { get; }
    public string Token { get; }
}
