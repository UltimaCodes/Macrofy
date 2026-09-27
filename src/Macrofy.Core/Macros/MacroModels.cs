using System.Text.Json.Serialization;
using Macrofy.Core.Input;

namespace Macrofy.Core.Macros;

// What a bound key does.
public enum MacroActionKind
{
    None,
    LaunchApp,   // Target = exe/path, Arguments = args
    OpenUrl,     // Target = url
    TypeText,    // Target = literal text to type
    SendHotkey,  // Target = e.g. "Ctrl+Shift+Esc"
    RunCommand,  // Target = shell command line
    MediaKey,    // Target = media token (PlayPause/Next/Prev/Stop/VolumeUp/VolumeDown/Mute)
    LayerHold,   // Target = layer name; that layer is active only while this key is held
    LayerToggle, // Target = layer name; tap to switch to it, tap again to return to Base
}

// A single action fired when a captured key is pressed.
public sealed class MacroAction
{
    public MacroActionKind Kind { get; set; } = MacroActionKind.None;
    public string Target { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;

    // Every kind needs its target, including layer switches (the layer's name).
    public bool IsEmpty => Kind == MacroActionKind.None || string.IsNullOrWhiteSpace(Target);

    public string Description => Kind switch
    {
        MacroActionKind.LaunchApp => $"Launch  {Target}",
        MacroActionKind.OpenUrl => $"Open  {Target}",
        MacroActionKind.TypeText => $"Type  \"{Target}\"",
        MacroActionKind.SendHotkey => $"Hotkey  {Target}",
        MacroActionKind.RunCommand => $"Run  {Target}",
        MacroActionKind.MediaKey => $"Media  {MediaLabel(Target)}",
        MacroActionKind.LayerHold => $"Hold for layer \"{Target}\"",
        MacroActionKind.LayerToggle => $"Toggle layer \"{Target}\"",
        _ => "(unassigned)",
    };

    private static string MediaLabel(string token) => token switch
    {
        "PlayPause" => "Play / Pause",
        "Next" => "Next track",
        "Prev" => "Previous track",
        "Stop" => "Stop",
        "VolumeUp" => "Volume up",
        "VolumeDown" => "Volume down",
        "Mute" => "Mute",
        _ => token,
    };
}

// One action within a multi-step sequence, with an optional pause after it runs.
public sealed class MacroStep
{
    public MacroAction Action { get; set; } = new();
    public int DelayMsAfter { get; set; }   // milliseconds to wait after this step (0 = none)
}

// One captured key bound to an action - or, when Steps is non-empty, to a sequence of them.
public sealed class MacroBinding
{
    // The physical key (see KeyCodes). Older profiles only had VirtualKey; Normalize fills
    // this in from it.
    public int KeyCode { get; set; }

    // The key's virtual key when it was bound, kept for display and for older versions.
    public int VirtualKey { get; set; }
    public string KeyName { get; set; } = string.Empty;

    // The single action. Used when Steps is empty (the common case).
    public MacroAction Action { get; set; } = new();

    // Multi-step sequence. When non-empty it runs instead of Action.
    public List<MacroStep> Steps { get; set; } = new();

    // Keep firing while the key is held (the keyboard's auto-repeat), e.g. for volume.
    // Off by default so holding a key can't launch an app thirty times.
    public bool RepeatWhileHeld { get; set; }

    public bool HasSteps => Steps.Count > 0;

    public bool IsEmpty => HasSteps ? Steps.All(s => s.Action.IsEmpty) : Action.IsEmpty;

    // Friendly one-liner for the bound-keys list, covering single and multi-step bindings.
    public string Description => HasSteps
        ? (Steps.Count == 1 ? Steps[0].Action.Description : $"{Steps.Count}-step macro")
        : Action.Description;
}

// A named set of bindings. A profile always has at least the "Base" layer (index 0);
// extra layers are reached with LayerHold / LayerToggle bindings.
public sealed class MacroLayer
{
    public string Name { get; set; } = "Base";
    public List<MacroBinding> Bindings { get; set; } = new();
}

// All bindings for one physical device, persisted as JSON.
public sealed class MacroProfile
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public List<MacroLayer> Layers { get; set; } = new();

    // Pre-layers profiles stored bindings here at the top level. Kept only so old files
    // migrate cleanly on load; never written back (null once normalized).
    [JsonPropertyName("Bindings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<MacroBinding>? LegacyBindings { get; set; }

    public MacroLayer BaseLayer => Layers[0];

    // Make a loaded or imported profile safe to use: JSON can set any list or object to null
    // (a hand-edited or damaged file), and older files need migrating. Idempotent.
    public void Normalize()
    {
        DeviceId ??= string.Empty;
        DeviceName ??= string.Empty;
        Layers ??= new List<MacroLayer>();
        Layers.RemoveAll(l => l is null);
        if (Layers.Count == 0)
            Layers.Add(new MacroLayer { Name = "Base", Bindings = LegacyBindings ?? new List<MacroBinding>() });
        LegacyBindings = null;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in Layers)
        {
            layer.Name = string.IsNullOrWhiteSpace(layer.Name) ? "Layer" : layer.Name.Trim();
            string unique = layer.Name;
            for (int n = 2; !names.Add(unique); n++)
                unique = $"{layer.Name} ({n})";
            layer.Name = unique;

            layer.Bindings ??= new List<MacroBinding>();
            layer.Bindings.RemoveAll(b => b is null);
            foreach (var b in layer.Bindings)
            {
                b.KeyName ??= string.Empty;
                b.Action = Clean(b.Action);
                b.Steps ??= new List<MacroStep>();
                b.Steps.RemoveAll(s => s is null);
                foreach (var s in b.Steps)
                {
                    s.Action = Clean(s.Action);
                    s.DelayMsAfter = Math.Max(0, s.DelayMsAfter);
                }
            }
        }

        MigrateModifierKeys();
        MigrateToKeyCodes();
    }

    private static MacroAction Clean(MacroAction? action)
    {
        action ??= new MacroAction();
        action.Target ??= string.Empty;
        action.Arguments ??= string.Empty;
        return action;
    }

    // Older builds saved modifier bindings under the generic VKs (0x10/0x11/0x12) because
    // that's what Raw Input reported; the engine now splits left/right, so remap old files
    // to the left-hand codes. If a layer somehow has both (a generic press-to-bind plus a
    // click-to-bind that saved the specific code), the specific one wins and the generic
    // duplicate is dropped.
    private void MigrateModifierKeys()
    {
        foreach (var layer in Layers)
        {
            for (int i = layer.Bindings.Count - 1; i >= 0; i--)
            {
                var b = layer.Bindings[i];
                if (b.KeyCode != 0)
                    continue;
                (int vk, string name) = b.VirtualKey switch
                {
                    0x10 => (0xA0, "Left Shift"),
                    0x11 => (0xA2, "Left Ctrl"),
                    0x12 => (0xA4, "Left Alt"),
                    _ => (0, string.Empty),
                };
                if (vk == 0)
                    continue;
                if (layer.Bindings.Any(other => other.KeyCode == 0 && other.VirtualKey == vk))
                {
                    layer.Bindings.RemoveAt(i);
                    continue;
                }
                b.VirtualKey = vk;
                b.KeyName = name;
            }
        }
    }

    // Profiles from before physical-key binding only have a VirtualKey. Two old bindings can
    // land on one physical key (rare); the first one keeps it.
    private void MigrateToKeyCodes()
    {
        foreach (var layer in Layers)
        {
            var seen = new HashSet<int>();
            for (int i = 0; i < layer.Bindings.Count; i++)
            {
                var b = layer.Bindings[i];
                if (b.KeyCode == 0 && b.VirtualKey != 0)
                    b.KeyCode = KeyCodes.FromVk(b.VirtualKey);
                if (b.KeyCode == 0 || !seen.Add(b.KeyCode))
                    layer.Bindings.RemoveAt(i--);
            }
        }
    }
}
