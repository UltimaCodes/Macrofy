using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using Macrofy.App;
using Macrofy.Core.Input;
using Macrofy.Core.Macros;

namespace Macrofy.App.ViewModels;

// Drives the Devices view. Captured-key events are dropped into a lock-free queue on the
// backend (decider) thread and drained by a UI timer — UI work must never run on that
// thread, which has a hard latency budget for answering the hook's block/pass question.
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const int MaxLogEntries = 200;

    private readonly IInputBackend _backend;
    private readonly DeviceNameStore _nameStore = new();
    private readonly MacroEngine _macroEngine = new();
    private readonly MacroProfileStore _profileStore = new();
    private readonly ConcurrentQueue<DeviceKeyEvent> _pending = new();
    private readonly DispatcherTimer _drainTimer;

    private MacroProfile? _profile;

    public ObservableCollection<KeyboardDevice> Keyboards { get; } = new();
    public ObservableCollection<KeyLogEntry> KeyLog { get; } = new();
    public ObservableCollection<MacroBinding> Bindings { get; } = new();
    public KeyboardLayoutViewModel KeyboardLayout { get; } = new();

    public MacroActionKind[] ActionKinds { get; } =
    {
        MacroActionKind.LaunchApp, MacroActionKind.OpenUrl, MacroActionKind.TypeText,
        MacroActionKind.SendHotkey, MacroActionKind.RunCommand,
    };

    public MainViewModel(IInputBackend backend)
    {
        _backend = backend;
        _backend.CapturedKey += OnCapturedKey;
        _backend.Start();

        _drainTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _drainTimer.Tick += (_, _) => Drain();
        _drainTimer.Start();

        RefreshDevices();
    }

    private KeyboardDevice? _selectedKeyboard;
    public KeyboardDevice? SelectedKeyboard
    {
        get => _selectedKeyboard;
        set
        {
            if (SetProperty(ref _selectedKeyboard, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                RenameText = value?.DisplayName ?? string.Empty;
                IsCapturing = false; // switching devices always drops capture first
                LoadProfileForSelected();
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public bool HasSelection => _selectedKeyboard is not null;

    private string _renameText = string.Empty;
    public string RenameText
    {
        get => _renameText;
        set => SetProperty(ref _renameText, value);
    }

    private bool _isCapturing;
    public bool IsCapturing
    {
        get => _isCapturing;
        set
        {
            if (SetProperty(ref _isCapturing, value))
            {
                ApplyCapture();
                if (!value)
                    KeyboardLayout.Reset();
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    private bool _showAllDevices;
    public bool ShowAllDevices
    {
        get => _showAllDevices;
        set
        {
            if (SetProperty(ref _showAllDevices, value))
                RefreshDevices();
        }
    }

    public string StatusText => _selectedKeyboard is null
        ? "No keyboard selected."
        : _isCapturing
            ? $"Capturing \"{_selectedKeyboard.DisplayName}\" — its keys are isolated and run your macros instead of typing."
            : $"\"{_selectedKeyboard.DisplayName}\" is passing through normally. Toggle capture to take it over.";

    // ---- binding editor state ----

    private int _bindVk;
    public int BindVk
    {
        get => _bindVk;
        set { if (SetProperty(ref _bindVk, value)) OnPropertyChanged(nameof(HasBindKey)); }
    }

    private string _bindKeyName = string.Empty;
    public string BindKeyName
    {
        get => _bindKeyName;
        set => SetProperty(ref _bindKeyName, value);
    }

    public bool HasBindKey => _bindVk != 0;

    private MacroActionKind _bindKind = MacroActionKind.LaunchApp;
    public MacroActionKind BindKind
    {
        get => _bindKind;
        set => SetProperty(ref _bindKind, value);
    }

    private string _bindTarget = string.Empty;
    public string BindTarget
    {
        get => _bindTarget;
        set => SetProperty(ref _bindTarget, value);
    }

    private string _bindArgs = string.Empty;
    public string BindArgs
    {
        get => _bindArgs;
        set => SetProperty(ref _bindArgs, value);
    }

    public void RefreshDevices()
    {
        string? previous = _selectedKeyboard?.Id;
        Keyboards.Clear();
        foreach (var kb in _backend.GetKeyboards(_showAllDevices))
        {
            var custom = _nameStore.Get(kb.Id);
            Keyboards.Add(custom is null ? kb : kb with { DisplayName = custom });
        }

        SelectedKeyboard = Keyboards.FirstOrDefault(k => k.Id == previous)
            ?? Keyboards.FirstOrDefault();
    }

    public void RenameSelected()
    {
        if (_selectedKeyboard is null)
            return;
        _nameStore.Set(_selectedKeyboard.Id, RenameText);
        RefreshDevices();
    }

    private void LoadProfileForSelected()
    {
        Bindings.Clear();
        if (_selectedKeyboard is null)
        {
            _profile = null;
            return;
        }
        _profile = _profileStore.Load(_selectedKeyboard.Id, _selectedKeyboard.DisplayName);
        foreach (var b in _profile.Bindings)
            Bindings.Add(b);
    }

    private void ApplyCapture()
    {
        if (_isCapturing && _selectedKeyboard is not null)
        {
            _backend.SetCapturedDevices(_selectedKeyboard.DevicePaths);
            _macroEngine.SetBindings(_profile?.Bindings ?? Enumerable.Empty<MacroBinding>());
        }
        else
        {
            _backend.SetCapturedDevices(Array.Empty<string>());
            _macroEngine.Clear();
        }
    }

    public void AddBinding()
    {
        if (_selectedKeyboard is null || _profile is null || _bindVk == 0 || string.IsNullOrWhiteSpace(_bindTarget))
            return;

        // Replace any existing binding for the same key.
        var existing = _profile.Bindings.FirstOrDefault(b => b.VirtualKey == _bindVk);
        if (existing is not null)
        {
            _profile.Bindings.Remove(existing);
            Bindings.Remove(existing);
        }

        var binding = new MacroBinding
        {
            VirtualKey = _bindVk,
            KeyName = _bindKeyName,
            Action = new MacroAction { Kind = _bindKind, Target = _bindTarget.Trim(), Arguments = _bindArgs.Trim() },
        };
        _profile.Bindings.Add(binding);
        Bindings.Add(binding);
        _profileStore.Save(_profile);

        if (_isCapturing)
            _macroEngine.SetBindings(_profile.Bindings);

        BindTarget = string.Empty;
        BindArgs = string.Empty;
    }

    public void RemoveBinding(MacroBinding binding)
    {
        if (_profile is null)
            return;
        _profile.Bindings.Remove(binding);
        Bindings.Remove(binding);
        _profileStore.Save(_profile);
        if (_isCapturing)
            _macroEngine.SetBindings(_profile.Bindings);
    }

    // Decider thread: do the absolute minimum and return.
    private void OnCapturedKey(object? sender, DeviceKeyEvent e)
    {
        _pending.Enqueue(e);
        _macroEngine.OnCapturedKey(e);
    }

    // UI thread: drain the queue into the visuals + the binding picker.
    private void Drain()
    {
        bool any = false;
        while (_pending.TryDequeue(out var e))
        {
            any = true;
            KeyboardLayout.SetPressed(e.VirtualKey, e.IsKeyDown);
            KeyLog.Insert(0, KeyLogEntry.From(e));
            if (e.IsKeyDown)
            {
                BindVk = e.VirtualKey;
                BindKeyName = VirtualKeyNames.Name(e.VirtualKey);
            }
        }

        if (any)
            while (KeyLog.Count > MaxLogEntries)
                KeyLog.RemoveAt(KeyLog.Count - 1);
    }

    public void ClearLog() => KeyLog.Clear();

    public void Dispose()
    {
        _drainTimer.Stop();
        _backend.CapturedKey -= OnCapturedKey;
        _backend.Dispose();
    }
}
