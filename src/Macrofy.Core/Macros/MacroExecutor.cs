using System.Diagnostics;
using System.Runtime.InteropServices;
using static Macrofy.Core.Input.Interop.NativeMethods;

namespace Macrofy.Core.Macros;

// Runs a MacroAction. Always called off the input threads (Task.Run from the engine),
// so a slow launch can never stall capture.
public static class MacroExecutor
{
    public static void Execute(MacroAction action)
    {
        try
        {
            switch (action.Kind)
            {
                case MacroActionKind.LaunchApp:
                    Process.Start(new ProcessStartInfo(action.Target, action.Arguments) { UseShellExecute = true });
                    break;
                case MacroActionKind.OpenUrl:
                    Process.Start(new ProcessStartInfo(action.Target) { UseShellExecute = true });
                    break;
                case MacroActionKind.RunCommand:
                    Process.Start(new ProcessStartInfo("cmd.exe", "/c " + action.Target)
                    { UseShellExecute = false, CreateNoWindow = true });
                    break;
                case MacroActionKind.TypeText:
                    TypeText(action.Target);
                    break;
                case MacroActionKind.SendHotkey:
                    SendHotkey(action.Target);
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Macro action failed ({action.Kind} {action.Target}): {ex.Message}");
        }
    }

    private static void TypeText(string text)
    {
        foreach (char c in text)
        {
            SendUnicode(c, down: true);
            SendUnicode(c, down: false);
        }
    }

    private static void SendUnicode(char c, bool down)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wScan = c,
                    dwFlags = KEYEVENTF_UNICODE | (down ? 0u : KEYEVENTF_KEYUP),
                },
            },
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static void SendHotkey(string combo)
    {
        var parts = combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return;

        var mods = new List<ushort>();
        ushort key = 0;
        foreach (var p in parts)
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl" or "control": mods.Add(0x11); break;
                case "shift": mods.Add(0x10); break;
                case "alt": mods.Add(0x12); break;
                case "win" or "windows": mods.Add(0x5B); break;
                default: key = KeyNameToVk(p); break;
            }
        }
        if (key == 0 && mods.Count == 0) return;

        foreach (var m in mods) SendVk(m, down: true);
        if (key != 0) { SendVk(key, down: true); SendVk(key, down: false); }
        for (int i = mods.Count - 1; i >= 0; i--) SendVk(mods[i], down: false);
    }

    private static void SendVk(ushort vk, bool down)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = down ? 0u : KEYEVENTF_KEYUP } },
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static ushort KeyNameToVk(string name)
    {
        if (name.Length == 1)
        {
            char c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z') return c;
            if (c is >= '0' and <= '9') return c;
        }
        if (name.Length is 2 or 3 && (name[0] is 'f' or 'F') && int.TryParse(name[1..], out int fn) && fn is >= 1 and <= 24)
            return (ushort)(0x70 + (fn - 1)); // F1..F24

        return name.ToLowerInvariant() switch
        {
            "enter" or "return" => 0x0D,
            "esc" or "escape" => 0x1B,
            "tab" => 0x09,
            "space" => 0x20,
            "backspace" => 0x08,
            "delete" or "del" => 0x2E,
            "home" => 0x24,
            "end" => 0x23,
            "up" => 0x26,
            "down" => 0x28,
            "left" => 0x25,
            "right" => 0x27,
            _ => 0,
        };
    }
}
