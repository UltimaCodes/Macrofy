using Macrofy.App;

namespace Macrofy.App.ViewModels;

// The on-screen keyboard: which layout to draw for the selected keyboard (a preset, ISO, or
// keys learned by pressing them), and keeping its keycaps in sync with the active profile.
public sealed partial class MainViewModel
{
    private KeyboardLayoutViewModel _keyboardLayout = new();
    public KeyboardLayoutViewModel KeyboardLayout
    {
        get => _keyboardLayout;
        private set => SetProperty(ref _keyboardLayout, value);
    }

    private KeyboardLayoutKind _layoutKind = KeyboardLayoutKind.Full;
    private List<int> _customKeys = new();
    private bool _isIso;

    public KeyboardLayoutKind[] LayoutKinds { get; } =
    {
        KeyboardLayoutKind.Full, KeyboardLayoutKind.TenKeyless, KeyboardLayoutKind.SeventyFive,
        KeyboardLayoutKind.SixtyFive, KeyboardLayoutKind.Sixty, KeyboardLayoutKind.Numpad,
        KeyboardLayoutKind.Custom,
    };

    public KeyboardLayoutKind SelectedLayoutKind
    {
        get => _layoutKind;
        set
        {
            if (!SetProperty(ref _layoutKind, value))
                return;
            SaveLayoutForSelected();
            RebuildKeyboardLayout();
            OnPropertyChanged(nameof(ShowLearnPrompt));
            OnPropertyChanged(nameof(ShowIsoToggle));
        }
    }

    // ISO (European) shape: tall Enter, short left Shift with an extra key beside it.
    public bool IsIsoLayout
    {
        get => _isIso;
        set
        {
            if (!SetProperty(ref _isIso, value))
                return;
            SaveLayoutForSelected();
            RebuildKeyboardLayout();
        }
    }

    // The ANSI/ISO distinction only means something for the full-size presets.
    public bool ShowIsoToggle => _layoutKind is not (KeyboardLayoutKind.Numpad or KeyboardLayoutKind.Custom);

    // The Custom layout is empty until the user calibrates - nudge them to learn keys.
    public bool ShowLearnPrompt => _layoutKind == KeyboardLayoutKind.Custom && _customKeys.Count == 0 && !_isLearning;

    private void SaveLayoutForSelected()
    {
        if (_selectedKeyboard is not null)
            _layoutStore.Set(_selectedKeyboard.Id,
                new DeviceLayout { Kind = _layoutKind, Keys = _customKeys, IsIso = _isIso });
    }

    private void RebuildKeyboardLayout()
    {
        KeyboardLayout = new KeyboardLayoutViewModel(_layoutKind, _customKeys, _isIso);
        RefreshKeycaps();
        if (HasBindKey)
            KeyboardLayout.Select(_bindKeyCode);
    }

    private void LoadLayoutForSelected()
    {
        var layout = _selectedKeyboard is null ? new DeviceLayout() : _layoutStore.Get(_selectedKeyboard.Id);
        _layoutKind = layout.Kind;
        _customKeys = layout.Keys ?? new List<int>();
        _isIso = layout.IsIso;
        OnPropertyChanged(nameof(SelectedLayoutKind));
        OnPropertyChanged(nameof(IsIsoLayout));
        OnPropertyChanged(nameof(ShowIsoToggle));
        OnPropertyChanged(nameof(ShowLearnPrompt));
        RebuildKeyboardLayout();
    }

    // Put the selected layer's macros on the keycaps (with Base showing through elsewhere).
    private void RefreshKeycaps()
    {
        if (_profile is null || _selectedLayer is null)
        {
            KeyboardLayout.ShowBindings(Array.Empty<Macrofy.Core.Macros.MacroBinding>(), null);
            return;
        }
        bool onBase = ReferenceEquals(_selectedLayer, _profile.BaseLayer);
        KeyboardLayout.ShowBindings(_selectedLayer.Bindings, onBase ? null : _profile.BaseLayer.Bindings);
    }

    // ---- learn / calibrate ----

    private bool _isLearning;
    public bool IsLearning
    {
        get => _isLearning;
        private set
        {
            if (SetProperty(ref _isLearning, value))
            {
                UpdateDrainTimer();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(ShowLearnPrompt));
            }
        }
    }

    // Physical key codes seen during calibration.
    private readonly HashSet<int> _learnedKeyCodes = new();

    public void StartLearning()
    {
        if (_selectedKeyboard is null || _isLearning)
            return;
        if (_isIdentifying)
            CancelIdentify();
        _learnedKeyCodes.Clear();
        LearnedKeys.Clear();
        IsLearning = true;
        KeyboardLayout.Reset();
        // Capture this device so we receive its keys, but don't run macros while calibrating.
        _backend.SetCapturedDevices(_selectedKeyboard.DevicePaths);
        _macroEngine.Clear();
    }

    public void SaveLearned()
    {
        if (!_isLearning)
            return;
        IsLearning = false;
        if (_learnedKeyCodes.Count > 0 && _selectedKeyboard is not null)
        {
            _customKeys = _learnedKeyCodes.OrderBy(v => v).ToList();
            _layoutKind = KeyboardLayoutKind.Custom;
            SaveLayoutForSelected();
            OnPropertyChanged(nameof(SelectedLayoutKind));
            OnPropertyChanged(nameof(ShowIsoToggle));
            OnPropertyChanged(nameof(ShowLearnPrompt));
            RebuildKeyboardLayout();
            ShowToast("Layout saved");
        }
        ApplyCapture(); // restore capture to whatever the switch says
    }

    public void CancelLearning()
    {
        if (!_isLearning)
            return;
        IsLearning = false;
        ApplyCapture();
    }
}
