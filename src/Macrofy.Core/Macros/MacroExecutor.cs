using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Macrofy.Core.Input;
using static Macrofy.Core.Input.Interop.NativeMethods;

namespace Macrofy.Core.Macros;

// Runs a MacroAction. Always called off the input threads (Task.Run from the engine),
// so a slow launch can never stall capture.
public static class MacroExecutor
{
    private const ushort VkReturn = 0x0D;
    private const ushort VkTab = 0x09;

    // An action couldn't run (missing file, unknown hotkey...). Raised on the thread-pool
    // thread with a message fit to show the user.
    public static event Action<MacroAction, string>? ActionFailed;

    // Runs a whole binding: a single action, or a multi-step sequence with delays between
    // steps. Sleeping between steps is fine here; this never runs on an input thread.
    public static void Run(MacroBinding binding)
    {
        if (!binding.HasSteps)
        {
            Execute(binding.Action);
            return;
        }
        foreach (var step in binding.Steps)
        {
            if (!Execute(step.Action))
                return; // later steps usually depend on earlier ones
            if (step.DelayMsAfter > 0)
                Thread.Sleep(step.DelayMsAfter);
        }
    }

    public static bool Execute(MacroAction action)
    {
        try
        {
            switch (action.Kind)
            {
                case MacroActionKind.LaunchApp:
                    Launch(action.Target, action.Arguments);
                    break;
                case MacroActionKind.OpenUrl:
                    Launch(action.Target, string.Empty);
                    break;
                case MacroActionKind.RunCommand:
                    // Runs with Macrofy's own rights (as admin when Macrofy is), on purpose:
                    // that's the one way to bind an admin command to a key.
                    using (Process.Start(new ProcessStartInfo("cmd.exe", "/c " + action.Target)
                           { UseShellExecute = false, CreateNoWindow = true })) { }
                    break;
                case MacroActionKind.TypeText:
                    TypeText(action.Target);
                    break;
                case MacroActionKind.SendHotkey:
                    SendHotkey(action.Target);
                    break;
                case MacroActionKind.MediaKey:
                    SendMediaKey(action.Target);
                    break;
            }
            return true;
        }
        catch (Exception ex)
        {
            ActionFailed?.Invoke(action, Describe(action, ex));
            return false;
        }
    }

    private static string Describe(MacroAction action, Exception ex) => ex switch
    {
        FileNotFoundException or Win32Exception { NativeErrorCode: 2 or 3 } => $"Couldn't find \"{action.Target}\".",
        Win32Exception { NativeErrorCode: 1223 } => $"Launching \"{action.Target}\" was cancelled.",
        _ => $"{action.Description.Split("  ")[0]} failed: {ex.Message}",
    };

    // Launch an app, file or link. Paths pasted with "Copy as path" arrive quoted, and %VARS%
    // are handy for portable setups. The app starts in its own folder (not Macrofy's, which is
    // System32 when Windows autostarts it), and never with Macrofy's admin rights.
    private static void Launch(string target, string arguments)
    {
        string file = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
        bool isPath = Path.IsPathRooted(file) && !file.Contains("://");
        if (isPath && !File.Exists(file) && !Directory.Exists(file))
            throw new FileNotFoundException(null, file);

        string ext = Path.GetExtension(file);
        string? workingDir = isPath && File.Exists(file)
            && !ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            && !ext.Equals(".url", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(file)
            : null;

        if (ProcessElevation.IsElevated && ShellLauncher.TryOpen(file, arguments, workingDir))
            return;

        using var _ = Process.Start(new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = true,
            WorkingDirectory = workingDir ?? string.Empty,
        });
    }

    // The whole text goes out in one SendInput call, so keys you're typing at the same time
    // can't interleave with it. Line breaks and tabs are sent as real Enter/Tab presses: the
    // text box stores "\r\n", and sending those as two characters doubles line breaks in some apps.
    private static void TypeText(string text)
    {
        var inputs = new List<INPUT>(text.Length * 2);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                AddTap(inputs, VkReturn);
            }
            else if (c == '\t')
            {
                AddTap(inputs, VkTab);
            }
            else
            {
                inputs.Add(Unicode(c, up: false));
                inputs.Add(Unicode(c, up: true));
            }
        }
        Send(inputs);
    }

    private static void SendHotkey(string combo)
    {
        if (!TryParseHotkey(combo, out var mods, out ushort key, out string? error))
            throw new ArgumentException(error);

        var inputs = new List<INPUT>();
        foreach (var m in mods) inputs.Add(Key(m, up: false));
        if (key != 0) AddTap(inputs, key);
        for (int i = mods.Count - 1; i >= 0; i--) inputs.Add(Key(mods[i], up: true));
        Send(inputs);
    }

    // Splits "Ctrl+Shift+Esc" into modifier VKs and one key VK. "Ctrl++" means Ctrl and the
    // plus key (plain splitting on '+' would lose it). On failure, error says why.
    public static bool TryParseHotkey(string combo, out List<ushort> modifiers, out ushort key, out string? error)
    {
        modifiers = new List<ushort>();
        key = 0;
        error = null;
        combo = (combo ?? string.Empty).Trim();

        string? plusKey = null;
        if (combo.EndsWith("++", StringComparison.Ordinal))
        {
            plusKey = "+";
            combo = combo[..^2];
        }
        else if (combo == "+")
        {
            plusKey = "+";
            combo = string.Empty;
        }

        var tokens = combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (plusKey is not null)
            tokens.Add(plusKey);
        foreach (var p in tokens)
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers.Add(0x11); break;
                case "shift": modifiers.Add(0x10); break;
                case "alt": modifiers.Add(0x12); break;
                case "win" or "windows": modifiers.Add(0x5B); break;
                default:
                    key = KeyNameToVk(p);
                    if (key == 0)
                    {
                        error = $"\"{p}\" isn't a key Macrofy knows how to send.";
                        return false;
                    }
                    break;
            }
        }
        if (key == 0 && modifiers.Count == 0)
        {
            error = "The hotkey is empty.";
            return false;
        }
        return true;
    }

    private static void SendMediaKey(string token)
    {
        ushort vk = token switch
        {
            "Mute" => 0xAD,
            "VolumeDown" => 0xAE,
            "VolumeUp" => 0xAF,
            "Next" => 0xB0,
            "Prev" => 0xB1,
            "Stop" => 0xB2,
            "PlayPause" => 0xB3,
            _ => 0,
        };
        if (vk == 0)
            throw new ArgumentException($"Unknown media key \"{token}\".");
        var inputs = new List<INPUT>();
        AddTap(inputs, vk);
        Send(inputs);
    }

    private static void AddTap(List<INPUT> inputs, ushort vk)
    {
        inputs.Add(Key(vk, up: false));
        inputs.Add(Key(vk, up: true));
    }

    // A virtual key with its scan code and extended flag filled in. Without the extended flag,
    // arrows/Home/End/Delete look like numpad keys to apps when NumLock is on (breaking
    // Shift+arrow selection), and games that read scan codes ignore VK-only input.
    private static INPUT Key(ushort vk, bool up)
    {
        int code = KeyCodes.FromVk(vk);
        bool hasScan = (code & KeyCodes.VkFlag) == 0 && (code & 0xFF00) != 0xE100;
        uint flags = up ? KEYEVENTF_KEYUP : 0;
        if (KeyCodes.IsExtended(code))
            flags |= KEYEVENTF_EXTENDEDKEY;
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION
            {
                ki = new KEYBDINPUT { wVk = vk, wScan = hasScan ? (ushort)(code & 0xFF) : (ushort)0, dwFlags = flags },
            },
        };
    }

    private static INPUT Unicode(char c, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION
        {
            ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0u) },
        },
    };

    private static void Send(List<INPUT> inputs)
    {
        if (inputs.Count == 0)
            return;
        uint sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        if (sent == 0)
            throw new InvalidOperationException("Windows didn't accept the keystrokes (is the screen locked?).");
    }

    // Names written by the hotkey recorder (and typed by hand). A single character that isn't
    // a letter or digit is looked up in the current keyboard layout.
    public static ushort KeyNameToVk(string name)
    {
        if (name.Length == 1)
        {
            char c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
                return c;
            short scan = VkKeyScan(name[0]);
            return scan == -1 ? (ushort)0 : (ushort)(scan & 0xFF);
        }

        string lower = name.ToLowerInvariant();
        if (lower.Length is 2 or 3 && lower[0] == 'f' && int.TryParse(lower[1..], out int fn) && fn is >= 1 and <= 24)
            return (ushort)(0x70 + (fn - 1)); // F1..F24
        if (lower.Length == 4 && lower.StartsWith("num", StringComparison.Ordinal) && lower[3] is >= '0' and <= '9')
            return (ushort)(0x60 + (lower[3] - '0')); // Num0..Num9
        if (lower.StartsWith("vk_", StringComparison.Ordinal)
            && ushort.TryParse(lower[3..], System.Globalization.NumberStyles.HexNumber, null, out ushort raw))
            return raw;

        return lower switch
        {
            "enter" or "return" => 0x0D,
            "esc" or "escape" => 0x1B,
            "tab" => 0x09,
            "space" => 0x20,
            "backspace" => 0x08,
            "delete" or "del" => 0x2E,
            "insert" or "ins" => 0x2D,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" or "pgup" => 0x21,
            "pagedown" or "pgdn" => 0x22,
            "up" => 0x26,
            "down" => 0x28,
            "left" => 0x25,
            "right" => 0x27,
            "capslock" => 0x14,
            "numlock" => 0x90,
            "scrolllock" => 0x91,
            "printscreen" or "prtsc" => 0x2C,
            "pause" => 0x13,
            "menu" or "apps" => 0x5D,
            "nummultiply" or "num*" => 0x6A,
            "numadd" => 0x6B,             // no "num+": '+' separates the keys of a combo
            "numsubtract" or "num-" => 0x6D,
            "numdecimal" or "num." => 0x6E,
            "numdivide" or "num/" => 0x6F,
            "mute" => 0xAD,
            "volumedown" => 0xAE,
            "volumeup" => 0xAF,
            "nexttrack" => 0xB0,
            "prevtrack" => 0xB1,
            "stop" => 0xB2,
            "playpause" => 0xB3,
            _ => 0,
        };
    }
}
