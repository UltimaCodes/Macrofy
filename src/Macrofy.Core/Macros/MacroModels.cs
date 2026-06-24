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
}

// A single action fired when a captured key is pressed.
public sealed class MacroAction
{
    public MacroActionKind Kind { get; set; } = MacroActionKind.None;
    public string Target { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;

    public bool IsEmpty => Kind == MacroActionKind.None || string.IsNullOrWhiteSpace(Target);

    public string Description => Kind switch
    {
        MacroActionKind.LaunchApp => $"Launch  {Target}",
        MacroActionKind.OpenUrl => $"Open  {Target}",
        MacroActionKind.TypeText => $"Type  \"{Target}\"",
        MacroActionKind.SendHotkey => $"Hotkey  {Target}",
        MacroActionKind.RunCommand => $"Run  {Target}",
        _ => "(unassigned)",
    };
}

// One captured key bound to an action.
public sealed class MacroBinding
{
    public int VirtualKey { get; set; }
    public string KeyName { get; set; } = string.Empty;
    public MacroAction Action { get; set; } = new();
}

// All bindings for one physical device, persisted as JSON.
public sealed class MacroProfile
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public List<MacroBinding> Bindings { get; set; } = new();
}
