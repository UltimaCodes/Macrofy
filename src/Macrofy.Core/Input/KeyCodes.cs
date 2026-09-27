using static Macrofy.Core.Input.Interop.NativeMethods;

namespace Macrofy.Core.Input;

// A physical-key identity: the scan code, with 0xE000 / 0xE100 added for E0/E1-prefixed
// keys. Macros bind to these rather than virtual keys so a macro stays on the same physical
// key when the Windows input language changes (AZERTY moves A/Q/Z/W) or NumLock flips the
// numpad between digits and arrows. Keys that report no scan code (a few HID-only keys)
// fall back to VkFlag | vk.
public static class KeyCodes
{
    public const int VkFlag = 0x10000;
    public const int NumpadEnter = 0xE01C;

    // From a Raw Input keyboard event.
    public static int FromRaw(ushort makeCode, ushort flags, int vk)
    {
        if (makeCode is 0 or >= 0xFF)
            return VkFlag | (vk & 0xFF);
        if ((flags & RI_KEY_E1) != 0)
            return 0xE100 | makeCode;
        if ((flags & RI_KEY_E0) != 0)
            return 0xE000 | makeCode;
        return makeCode;
    }

    // From a virtual key (legacy profiles, the on-screen keyboard). Windows maps the
    // navigation keys to their numpad twins' scan codes and Print Screen to SysRq, so those
    // get the dedicated keys' codes explicitly.
    public static int FromVk(int vk)
    {
        switch (vk)
        {
            case 0x13: return 0xE11D; // Pause
            case 0x21: return 0xE049; // Page Up
            case 0x22: return 0xE051; // Page Down
            case 0x23: return 0xE04F; // End
            case 0x24: return 0xE047; // Home
            case 0x25: return 0xE04B; // Left
            case 0x26: return 0xE048; // Up
            case 0x27: return 0xE04D; // Right
            case 0x28: return 0xE050; // Down
            case 0x2C: return 0xE037; // Print Screen
            case 0x2D: return 0xE052; // Insert
            case 0x2E: return 0xE053; // Delete
            case 0x90: return 0x45;   // Num Lock
        }
        uint sc = MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC_EX);
        return sc == 0 ? VkFlag | (vk & 0xFF) : (int)sc;
    }

    // The virtual key this physical key produces under the current layout (for names and
    // labels). Unprefixed numpad scan codes map to their digit keys, not the NumLock-off
    // navigation meaning Windows would otherwise report.
    public static int ToVk(int code)
    {
        if ((code & VkFlag) != 0)
            return code & 0xFF;
        switch (code)
        {
            case 0x47: return 0x67; // Numpad 7
            case 0x48: return 0x68;
            case 0x49: return 0x69;
            case 0x4B: return 0x64;
            case 0x4C: return 0x65;
            case 0x4D: return 0x66;
            case 0x4F: return 0x61;
            case 0x50: return 0x62;
            case 0x51: return 0x63;
            case 0x52: return 0x60; // Numpad 0
            case 0x53: return 0x6E; // Numpad .
            case NumpadEnter: return 0x0D;
        }
        return (int)MapVirtualKey((uint)code, MAPVK_VSC_TO_VK_EX);
    }

    // True for keys that need KEYEVENTF_EXTENDEDKEY when injected.
    public static bool IsExtended(int code) => (code & 0xFF00) == 0xE000;
}
