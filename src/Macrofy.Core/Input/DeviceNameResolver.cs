using System.Globalization;
using System.Text.RegularExpressions;

namespace Macrofy.Core.Input;

// Display names and VID/PID parsing from device interface paths.
public static partial class DeviceNameResolver
{
    // USB paths say "VID_046D&PID_C31C". Bluetooth paths say "VID&0002046d_PID&b35b"
    // (classic: a 4-digit vendor-id source, then the VID) or "VID&02046d" (LE: a 2-digit
    // source), so take the last four digits.
    public static bool TryParseVidPid(string path, out ushort vid, out ushort pid)
    {
        vid = pid = 0;
        var u = path.ToUpperInvariant();
        var mv = VidRegex().Match(u);
        var mp = PidRegex().Match(u);
        if (!mv.Success || !mp.Success)
            return false;
        string v = mv.Groups[1].Value;
        return ushort.TryParse(v[^4..], NumberStyles.HexNumber, null, out vid)
            && ushort.TryParse(mp.Groups[1].Value, NumberStyles.HexNumber, null, out pid);
    }

    // The parser older versions used (USB form only). Device ids built with it are how
    // existing names, layouts and profiles are keyed, so they're kept for migration.
    public static bool TryParseLegacyVidPid(string path, out ushort vid, out ushort pid)
    {
        vid = pid = 0;
        var u = path.ToUpperInvariant();
        var mv = LegacyVidRegex().Match(u);
        var mp = LegacyPidRegex().Match(u);
        return mv.Success && mp.Success
            && ushort.TryParse(mv.Groups[1].Value, NumberStyles.HexNumber, null, out vid)
            && ushort.TryParse(mp.Groups[1].Value, NumberStyles.HexNumber, null, out pid);
    }

    public static bool IsVirtual(string path)
    {
        var u = path.ToUpperInvariant();
        return u.Contains("RDP_KBD") || u.Contains("ROOT#") || u.Contains("VIRTUAL");
    }

    // How a keyboard is connected, read from its device path. Bluetooth HID shows up with the
    // HID-over-Bluetooth service ids (classic and LE); laptop and PS/2 keyboards come through
    // ACPI/i8042. Wireless dongles are USB devices, so they read as USB.
    public static ConnectionKind Connection(string path)
    {
        var u = path.ToUpperInvariant();
        if (IsVirtual(path))
            return ConnectionKind.Virtual;
        if (u.Contains("{00001124-0000-1000-8000-00805F9B34FB}") || u.Contains("{00001812-0000-1000-8000-00805F9B34FB}")
            || u.Contains("BTHENUM") || u.Contains("BTHLE"))
            return ConnectionKind.Bluetooth;
        if (u.Contains("ACPI") || u.Contains("PNP03") || u.Contains("I8042"))
            return ConnectionKind.BuiltIn;
        if (u.Contains("VID_"))
            return ConnectionKind.Usb;
        return ConnectionKind.Unknown;
    }

    // Name for one physical device (no per-collection suffix).
    public static string ResolveGroup(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Unknown device";

        var u = path.ToUpperInvariant();
        if (u.Contains("RDP_KBD"))
            return "Remote Desktop keyboard";
        if (TryParseVidPid(path, out var vid, out var pid))
            return $"Keyboard {vid:X4}:{pid:X4}";
        if (u.Contains("ACPI") || u.Contains("PNP0303"))
            return "Built-in keyboard";
        if (u.Contains("ROOT#"))
            return "Virtual keyboard";
        return "Keyboard";
    }

    [GeneratedRegex(@"VID[_&]([0-9A-F]{4,8})")] private static partial Regex VidRegex();
    [GeneratedRegex(@"PID[_&]([0-9A-F]{4})")] private static partial Regex PidRegex();
    [GeneratedRegex(@"VID_([0-9A-F]{4})")] private static partial Regex LegacyVidRegex();
    [GeneratedRegex(@"PID_([0-9A-F]{4})")] private static partial Regex LegacyPidRegex();
}
