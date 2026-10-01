using System.IO;

namespace Macrofy.Core.Macros;

// The short text shown on a bound key: the user's own label, or one made up from the action
// ("Spotify", "youtube.com", "Ctrl+C", "Vol +").
public static class BindingLabels
{
    private const int MaxSnippet = 14;

    public static string For(MacroBinding binding)
    {
        if (!string.IsNullOrWhiteSpace(binding.Label))
            return binding.Label.Trim();
        if (binding.HasSteps && binding.Steps.Count > 1)
            return $"{binding.Steps.Count} steps";
        return ForAction(binding.PrimaryAction);
    }

    public static string ForAction(MacroAction action) => action.Kind switch
    {
        MacroActionKind.LaunchApp => AppName(action.Target),
        MacroActionKind.OpenUrl => LinkName(action.Target),
        MacroActionKind.TypeText => Snippet(action.Target),
        MacroActionKind.SendHotkey => action.Target.Trim(),
        MacroActionKind.RunCommand => CommandName(action.Target),
        MacroActionKind.MediaKey => MediaName(action.Target),
        MacroActionKind.LayerHold or MacroActionKind.LayerToggle => action.Target.Trim(),
        _ => string.Empty,
    };

    private static string AppName(string target)
    {
        string path = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
        string name = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    // Web links show their site; other links (whatsapp://, spotify:, ms-settings:) show the app.
    private static string LinkName(string target)
    {
        string t = target.Trim();
        if (Uri.TryCreate(t, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https")
                return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
            return Capitalize(uri.Scheme);
        }
        int colon = t.IndexOf(':');
        return colon > 0 ? Capitalize(t[..colon]) : t;
    }

    private static string Snippet(string text)
    {
        string flat = string.Join(' ', text.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return flat.Length <= MaxSnippet ? flat : flat[..(MaxSnippet - 1)].TrimEnd() + "…";
    }

    private static string CommandName(string command)
    {
        string first = command.Trim().Split(' ', 2)[0].Trim('"');
        string name = Path.GetFileNameWithoutExtension(first);
        return string.IsNullOrEmpty(name) ? "Command" : name;
    }

    private static string MediaName(string token) => token switch
    {
        "PlayPause" => "Play/Pause",
        "Next" => "Next",
        "Prev" => "Previous",
        "Stop" => "Stop",
        "VolumeUp" => "Vol +",
        "VolumeDown" => "Vol −",
        "Mute" => "Mute",
        _ => token,
    };

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
