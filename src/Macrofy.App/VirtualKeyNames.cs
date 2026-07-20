using System.Runtime.InteropServices;

namespace Macrofy.App;

// Friendly names for virtual-key codes. Named keys come from the table; punctuation/OEM
// keys ask Windows what character they type under the active keyboard layout, so a UK or
// German keyboard shows its real engravings instead of US ones. Unknown keys fall back to hex.
public static class VirtualKeyNames
{
    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    private const uint MAPVK_VK_TO_CHAR = 2;

    public static string Name(int vk) => vk switch
    {
        0x08 => "Backspace",
        0x09 => "Tab",
        0x0D => "Enter",
        0x10 => "Shift",   // legacy generic codes; new captures use the L/R ones below
        0x11 => "Ctrl",
        0x12 => "Alt",
        0x13 => "Pause",
        0x14 => "Caps Lock",
        0x1B => "Esc",
        0x20 => "Space",
        0x21 => "Page Up",
        0x22 => "Page Down",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x2C => "PrtSc",
        0x2D => "Insert",
        0x2E => "Delete",
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),            // 0-9
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),            // A-Z
        0x5B or 0x5C => "Win",
        0x5D => "Menu",
        >= 0x60 and <= 0x69 => "Numpad" + (vk - 0x60),           // Numpad0-9
        0x6A => "Numpad *",
        0x6B => "Numpad +",
        0x6D => "Numpad -",
        0x6E => "Numpad .",
        0x6F => "Numpad /",
        >= 0x70 and <= 0x87 => "F" + (vk - 0x6F),                // F1-F24
        0x90 => "NumLock",
        0x91 => "Scroll Lock",
        0xA0 => "Left Shift",
        0xA1 => "Right Shift",
        0xA2 => "Left Ctrl",
        0xA3 => "Right Ctrl",
        0xA4 => "Left Alt",
        0xA5 => "Right Alt",
        0xAD => "Mute",
        0xAE => "Volume Down",
        0xAF => "Volume Up",
        0xB0 => "Next Track",
        0xB1 => "Prev Track",
        0xB2 => "Stop",
        0xB3 => "Play/Pause",
        _ => OemOrHex(vk),
    };

    // OEM keys (; ' [ ] \ , . / ` and ISO's extra key) land on different VKs and characters
    // per layout, so ask the layout instead of hardcoding. Bit 31 of the result marks dead
    // keys (e.g. ´ on German); masking to the low word keeps the character itself.
    private static string OemOrHex(int vk)
    {
        uint ch = MapVirtualKey((uint)vk, MAPVK_VK_TO_CHAR) & 0xFFFF;
        if (ch > 0x20)
            return char.ConvertFromUtf32((int)ch);
        return $"VK 0x{vk:X2}";
    }
}
