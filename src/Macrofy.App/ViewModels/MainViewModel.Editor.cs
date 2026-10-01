using System.Windows.Threading;
using Macrofy.App;
using Macrofy.Core.Input;
using Macrofy.Core.Macros;

namespace Macrofy.App.ViewModels;

// The key editor: pick a key (click it, or press it on the captured keyboard), choose what it
// does, optionally chain more steps, and save it into the active profile.
public sealed partial class MainViewModel
{
    public IReadOnlyList<ActionOption> ActionOptions => ActionUi.Options;

    public MediaKeyOption[] MediaOptions { get; } =
    {
        new("Play / Pause", "PlayPause"),
        new("Next track", "Next"),
        new("Previous track", "Prev"),
        new("Stop", "Stop"),
        new("Volume up", "VolumeUp"),
        new("Volume down", "VolumeDown"),
        new("Mute", "Mute"),
    };

    // The physical key being edited (a KeyCodes code), 0 = none.
    private int _bindKeyCode;
    public int BindKeyCode
    {
        get => _bindKeyCode;
        private set
        {
            if (SetProperty(ref _bindKeyCode, value))
                OnPropertyChanged(nameof(HasBindKey));
        }
    }

    public bool HasBindKey => _bindKeyCode != 0;

    private string _bindKeyName = string.Empty;
    public string BindKeyName
    {
        get => _bindKeyName;
        private set => SetProperty(ref _bindKeyName, value);
    }

    // The selected key already has a macro on this layer (so "Remove" makes sense).
    private bool _isExistingBinding;
    public bool IsExistingBinding
    {
        get => _isExistingBinding;
        private set => SetProperty(ref _isExistingBinding, value);
    }

    // What's on the key right now when it's inherited from Base on another layer.
    private string _inheritedNote = string.Empty;
    public string InheritedNote
    {
        get => _inheritedNote;
        private set
        {
            if (SetProperty(ref _inheritedNote, value))
                OnPropertyChanged(nameof(HasInheritedNote));
        }
    }

    public bool HasInheritedNote => _inheritedNote.Length > 0;

    private string _bindLabel = string.Empty;
    public string BindLabel
    {
        get => _bindLabel;
        set => SetProperty(ref _bindLabel, value ?? string.Empty);
    }

    // The label the key gets if the user doesn't type one.
    public string BindLabelPlaceholder
    {
        get
        {
            var current = CurrentAction();
            int total = PendingSteps.Count + (current is null ? 0 : 1);
            if (total > 1)
                return $"{total} steps";
            var only = PendingSteps.Count == 1 ? PendingSteps[0].Action : current;
            return only is null ? "Label on the key (optional)" : BindingLabels.ForAction(only);
        }
    }

    private MacroActionKind _bindKind = MacroActionKind.LaunchApp;
    public MacroActionKind BindKind
    {
        get => _bindKind;
        set
        {
            if (SetProperty(ref _bindKind, value))
            {
                // Each action means something different, so start its field fresh and
                // refresh all the per-action labels/visibility the form binds to.
                BindTarget = string.Empty;
                BindArgs = string.Empty;
                OnPropertyChanged(nameof(SelectedActionOption));
                OnPropertyChanged(nameof(BindTargetLabel));
                OnPropertyChanged(nameof(BindTargetPlaceholder));
                OnPropertyChanged(nameof(BindTargetHelp));
                OnPropertyChanged(nameof(ShowArguments));
                OnPropertyChanged(nameof(ShowBrowse));
                OnPropertyChanged(nameof(IsHotkeyKind));
                OnPropertyChanged(nameof(IsMultilineTarget));
                OnPropertyChanged(nameof(IsStandardTarget));
                OnPropertyChanged(nameof(IsMediaKind));
                OnPropertyChanged(nameof(IsLayerKind));
                OnPropertyChanged(nameof(ShowLayerPicker));
                OnPropertyChanged(nameof(ShowAddLayerHint));
                OnPropertyChanged(nameof(HotkeyError));
            }
        }
    }

    public ActionOption? SelectedActionOption
    {
        get => ActionOptions.FirstOrDefault(o => o.Kind == _bindKind);
        set
        {
            if (value is not null)
                BindKind = value.Kind;
        }
    }

    private string _bindTarget = string.Empty;
    public string BindTarget
    {
        get => _bindTarget;
        set
        {
            if (SetProperty(ref _bindTarget, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(BindLabelPlaceholder));
                OnPropertyChanged(nameof(HotkeyError));
            }
        }
    }

    private string _bindArgs = string.Empty;
    public string BindArgs
    {
        get => _bindArgs;
        set => SetProperty(ref _bindArgs, value ?? string.Empty);
    }

    private int _bindDelayMs = 100;
    public int BindDelayMs
    {
        get => _bindDelayMs;
        set => SetProperty(ref _bindDelayMs, Math.Max(0, value));
    }

    // Keep firing this key's macro while it's held down (its auto-repeat). Off by default so
    // holding, say, a "launch app" key can't open it dozens of times.
    private bool _repeatWhileHeld;
    public bool RepeatWhileHeld
    {
        get => _repeatWhileHeld;
        set => SetProperty(ref _repeatWhileHeld, value);
    }

    public bool HasPendingSteps => PendingSteps.Count > 0;

    // Why the typed hotkey can't be sent, or null.
    public string? HotkeyError =>
        IsHotkeyKind && !string.IsNullOrWhiteSpace(_bindTarget)
        && !MacroExecutor.TryParseHotkey(_bindTarget, out _, out _, out var error) ? error : null;

    // ---- per-action presentation (drives the contextual form) ----

    public string BindTargetLabel => _bindKind switch
    {
        MacroActionKind.LaunchApp => "App or file to open",
        MacroActionKind.OpenUrl => "Link to open",
        MacroActionKind.TypeText => "Text to type",
        MacroActionKind.SendHotkey => "Shortcut to send",
        MacroActionKind.RunCommand => "Command to run",
        MacroActionKind.MediaKey => "Media key",
        MacroActionKind.LayerHold => "Layer to hold",
        MacroActionKind.LayerToggle => "Layer to switch to",
        _ => "Target",
    };

    public string BindTargetPlaceholder => _bindKind switch
    {
        MacroActionKind.LaunchApp => "Browse… or paste a path",
        MacroActionKind.OpenUrl => "https://… or whatsapp://send?phone=…",
        MacroActionKind.TypeText => "What should this key type for you?",
        MacroActionKind.SendHotkey => "Click here, then press the keys (e.g. Ctrl+C)",
        MacroActionKind.RunCommand => "e.g. shutdown /s /t 0",
        _ => string.Empty,
    };

    public string BindTargetHelp => _bindKind switch
    {
        MacroActionKind.LaunchApp => "Opens a program, file or folder. Arguments are optional.",
        MacroActionKind.OpenUrl => "Opens a website, or an app link like whatsapp://, spotify: or ms-settings:.",
        MacroActionKind.TypeText => "Types this wherever your cursor is. Line breaks become Enter.",
        MacroActionKind.SendHotkey => "Sends a shortcut to the app in front. Press Esc to clear it.",
        MacroActionKind.RunCommand => "Runs a command line (via cmd), hidden. For advanced use.",
        MacroActionKind.MediaKey => "Controls music and volume in most players.",
        MacroActionKind.LayerHold => "While this key is held, the keyboard uses the other layer.",
        MacroActionKind.LayerToggle => "Tap to switch to the other layer, tap again to come back to Base.",
        _ => string.Empty,
    };

    public bool ShowArguments => _bindKind == MacroActionKind.LaunchApp;
    public bool ShowBrowse => _bindKind == MacroActionKind.LaunchApp;
    public bool IsHotkeyKind => _bindKind == MacroActionKind.SendHotkey;
    public bool IsMultilineTarget => _bindKind == MacroActionKind.TypeText;
    public bool IsMediaKind => _bindKind == MacroActionKind.MediaKey;
    public bool IsLayerKind => _bindKind is MacroActionKind.LayerHold or MacroActionKind.LayerToggle;
    public bool IsStandardTarget =>
        _bindKind is MacroActionKind.LaunchApp or MacroActionKind.OpenUrl or MacroActionKind.RunCommand;

    // Layer kinds pick their target from a dropdown of the OTHER layers; if there aren't any
    // yet, show a nudge to create one instead of an empty combo.
    public bool ShowLayerPicker => IsLayerKind && LayerTargets.Count > 0;
    public bool ShowAddLayerHint => IsLayerKind && LayerTargets.Count == 0;

    // ---- selecting a key ----

    // Click on the on-screen keyboard (or the bound-keys list).
    public void PickKey(int keyCode)
    {
        SelectKeyForEdit(keyCode);
        if (_isCapturing)
            FlashKey(keyCode);
    }

    private void SelectKeyForEdit(int keyCode)
    {
        BindKeyCode = keyCode;
        BindKeyName = VirtualKeyNames.NameForKey(keyCode);
        KeyboardLayout.Select(keyCode);
        LoadBindingForEdit(keyCode);
    }

    public void CloseEditor()
    {
        BindKeyCode = 0;
        BindKeyName = string.Empty;
        KeyboardLayout.Select(0);
        ClearForm();
    }

    // Briefly light the key on the tester, as if it was pressed, for click feedback.
    private void FlashKey(int keyCode)
    {
        KeyboardLayout.SetPressed(keyCode, true);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            KeyboardLayout.SetPressed(keyCode, false);
        };
        timer.Start();
    }

    // Pull the key's macro on this layer into the form, or start a clean form for it.
    private void LoadBindingForEdit(int keyCode)
    {
        var existing = _selectedLayer?.Bindings.FirstOrDefault(b => b.KeyCode == keyCode);
        IsExistingBinding = existing is not null;

        var inherited = existing is null && _profile is not null && !ReferenceEquals(_selectedLayer, _profile.BaseLayer)
            ? _profile.BaseLayer.Bindings.FirstOrDefault(b => b.KeyCode == keyCode)
            : null;
        InheritedNote = inherited is null
            ? string.Empty
            : $"On this layer it still does \"{BindingLabels.For(inherited)}\" from Base. Save a macro here to change that.";

        if (existing is null)
        {
            ClearForm();
            return;
        }

        PendingSteps.Clear();
        if (existing.HasSteps)
        {
            foreach (var s in existing.Steps)
                PendingSteps.Add(s.Clone());
            BindKind = MacroActionKind.LaunchApp; // the sequence lives in the step list
            BindTarget = string.Empty;
            BindArgs = string.Empty;
        }
        else
        {
            BindKind = existing.Action.Kind;
            BindTarget = existing.Action.Target;
            BindArgs = existing.Action.Arguments;
        }
        BindLabel = existing.Label;
        RepeatWhileHeld = existing.RepeatWhileHeld;
        OnPropertyChanged(nameof(HasPendingSteps));
        OnPropertyChanged(nameof(BindLabelPlaceholder));
        MarkClean();
    }

    private void ClearForm()
    {
        PendingSteps.Clear();
        BindTarget = string.Empty;
        BindArgs = string.Empty;
        BindLabel = string.Empty;
        RepeatWhileHeld = false;
        OnPropertyChanged(nameof(HasPendingSteps));
        OnPropertyChanged(nameof(BindLabelPlaceholder));
        MarkClean();
    }

    // ---- unsaved-changes tracking ----

    private string _cleanSnapshot = string.Empty;

    private string Snapshot() => string.Join('\u001f',
        _bindKind, _bindTarget, _bindArgs, _bindLabel, _repeatWhileHeld,
        string.Join('\u001e', PendingSteps.Select(s => $"{s.Action.Kind}|{s.Action.Target}|{s.Action.Arguments}|{s.DelayMsAfter}")));

    private void MarkClean() => _cleanSnapshot = Snapshot();

    // The form differs from what was loaded or last saved.
    public bool IsDirty => HasBindKey && Snapshot() != _cleanSnapshot;

    // ---- building and saving ----

    // The action currently configured in the form, or null if it isn't filled in.
    private MacroAction? CurrentAction()
    {
        var action = new MacroAction
        {
            Kind = _bindKind,
            Target = _bindKind == MacroActionKind.TypeText ? _bindTarget : _bindTarget.Trim(),
            Arguments = _bindArgs.Trim(),
        };
        return action.IsEmpty ? null : action;
    }

    // Append the configured action to the sequence and clear the fields for the next one.
    public void AddStep()
    {
        var action = CurrentAction();
        if (action is null)
        {
            ShowToast("Fill in this step first.");
            return;
        }
        if (HotkeyError is { } error)
        {
            ShowToast(error);
            return;
        }
        PendingSteps.Add(new MacroStep { Action = action, DelayMsAfter = _bindDelayMs });
        OnPropertyChanged(nameof(HasPendingSteps));
        BindTarget = string.Empty;
        BindArgs = string.Empty;
        OnPropertyChanged(nameof(BindLabelPlaceholder));
    }

    public void RemoveStep(MacroStep step)
    {
        PendingSteps.Remove(step);
        OnPropertyChanged(nameof(HasPendingSteps));
        OnPropertyChanged(nameof(BindLabelPlaceholder));
    }

    public void MoveStep(MacroStep step, int direction)
    {
        int i = PendingSteps.IndexOf(step);
        int j = i + direction;
        if (i >= 0 && j >= 0 && j < PendingSteps.Count)
            PendingSteps.Move(i, j);
    }

    public void SaveMacro()
    {
        if (_selectedKeyboard is null || _profile is null || _selectedLayer is null || _bindKeyCode == 0)
            return;
        if (HotkeyError is { } error)
        {
            ShowToast(error);
            return;
        }

        // Assemble the macro: the steps already added, plus whatever's currently configured.
        var steps = PendingSteps.Select(s => s.Clone()).ToList();
        var current = CurrentAction();
        if (current is not null)
            steps.Add(new MacroStep { Action = current });
        if (steps.Count == 0)
        {
            ShowToast(IsLayerKind && LayerTargets.Count == 0
                ? "Add another layer first, then pick it here."
                : "Choose what the key should do first.");
            return;
        }

        var binding = new MacroBinding
        {
            KeyCode = _bindKeyCode,
            VirtualKey = KeyCodes.ToVk(_bindKeyCode),
            KeyName = _bindKeyName,
            Label = _bindLabel.Trim(),
            RepeatWhileHeld = _repeatWhileHeld,
        };
        if (steps.Count == 1)
        {
            steps[0].DelayMsAfter = 0;
            binding.Action = steps[0].Action;   // single action - keep it simple/back-compatible
        }
        else
        {
            steps[^1].DelayMsAfter = 0;
            binding.Steps = steps;              // a real sequence
        }

        // Replace any existing binding for the same key on this layer.
        _selectedLayer.Bindings.RemoveAll(b => b.KeyCode == _bindKeyCode);
        _selectedLayer.Bindings.Add(binding);
        RefreshBindingsForLayer();
        SaveProfile();

        LoadBindingForEdit(_bindKeyCode); // show it as saved
        ShowToast($"Saved {_bindKeyName}");
    }

    public void RemoveCurrentMacro()
    {
        if (_selectedLayer is null || _bindKeyCode == 0)
            return;
        if (_selectedLayer.Bindings.RemoveAll(b => b.KeyCode == _bindKeyCode) > 0)
        {
            RefreshBindingsForLayer();
            SaveProfile();
            ShowToast($"Removed the macro from {_bindKeyName}");
        }
        LoadBindingForEdit(_bindKeyCode);
    }
}
