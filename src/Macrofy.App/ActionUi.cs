using Macrofy.Core.Macros;
using Wpf.Ui.Controls;

namespace Macrofy.App;

// One choice in the "What should this key do?" picker.
public sealed record ActionOption(MacroActionKind Kind, string Title, SymbolRegular Icon);

// How each action kind looks in the app: its picker card and the icon shown on bound keys.
public static class ActionUi
{
    public static IReadOnlyList<ActionOption> Options { get; } = new[]
    {
        new ActionOption(MacroActionKind.LaunchApp, "Open app", SymbolRegular.Rocket24),
        new ActionOption(MacroActionKind.OpenUrl, "Open link", SymbolRegular.Globe24),
        new ActionOption(MacroActionKind.TypeText, "Type text", SymbolRegular.TextT24),
        new ActionOption(MacroActionKind.SendHotkey, "Shortcut", SymbolRegular.KeyCommand24),
        new ActionOption(MacroActionKind.MediaKey, "Media", SymbolRegular.MusicNote224),
        new ActionOption(MacroActionKind.RunCommand, "Command", SymbolRegular.WindowConsole20),
        new ActionOption(MacroActionKind.LayerHold, "Hold layer", SymbolRegular.Layer24),
        new ActionOption(MacroActionKind.LayerToggle, "Switch layer", SymbolRegular.ArrowSwap24),
    };

    public static SymbolRegular IconFor(MacroBinding binding)
        => binding.HasSteps && binding.Steps.Count > 1 ? SymbolRegular.Flash24 : IconFor(binding.PrimaryAction);

    public static SymbolRegular IconFor(MacroAction action) => action.Kind switch
    {
        MacroActionKind.MediaKey when action.Target is "VolumeUp" or "VolumeDown" or "Mute" => SymbolRegular.Speaker224,
        _ => Options.FirstOrDefault(o => o.Kind == action.Kind)?.Icon ?? SymbolRegular.Empty,
    };
}
