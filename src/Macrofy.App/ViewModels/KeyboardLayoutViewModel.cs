using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Macrofy.App;

namespace Macrofy.App.ViewModels;

// The on-screen keyboard. The shape depends on the chosen layout (Full/TKL/75/65/60/Numpad,
// ANSI or ISO) or a Custom set of learned keys. Rows feed the UI; SetPressed lights keys live.
//
// Alphanumeric keys are placed by scan code (physical position) and the active keyboard
// layout decides which virtual key lives there and what to print on it. That way an AZERTY
// keyboard shows A where A actually is, and a UK keyboard's ' key isn't labelled ~.
public sealed class KeyboardLayoutViewModel
{
    private const double Unit = 30;       // px per 1u key
    public double KeyHeight => 30;

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    private const uint MAPVK_VSC_TO_VK_EX = 3;

    private readonly List<List<KeyCapViewModel>> _rows = new();
    private readonly Dictionary<int, List<KeyCapViewModel>> _byVk = new();

    public IReadOnlyList<IReadOnlyList<KeyCapViewModel>> Rows => _rows;

    public KeyboardLayoutViewModel(KeyboardLayoutKind kind = KeyboardLayoutKind.Full,
                                   IReadOnlyList<int>? customKeys = null,
                                   bool iso = false)
        => Build(kind, customKeys, iso);

    public void SetPressed(int vk, bool pressed)
    {
        if (_byVk.TryGetValue(vk, out var caps))
            foreach (var cap in caps)
                cap.IsPressed = pressed;
    }

    public void Reset()
    {
        foreach (var row in _rows)
            foreach (var cap in row)
                cap.IsPressed = false;
    }

    private List<KeyCapViewModel> _current = null!;

    private void Row() => _rows.Add(_current = new List<KeyCapViewModel>());

    private void K(string label, int vk, double u = 1, bool capturable = true)
    {
        var cap = new KeyCapViewModel(label, vk, u * Unit, capturable: capturable);
        _current.Add(cap);
        if (!_byVk.TryGetValue(vk, out var list))
            _byVk[vk] = list = new List<KeyCapViewModel>();
        list.Add(cap);
    }

    // A key identified by its physical position. The active layout maps scan -> VK and
    // VK -> label; the US values are the fallback if the layout can't answer.
    private void KS(int scan, string usLabel, int usVk, double u = 1)
    {
        int vk = (int)MapVirtualKey((uint)scan, MAPVK_VSC_TO_VK_EX);
        if (vk == 0)
        {
            K(usLabel, usVk, u);
            return;
        }
        string label = VirtualKeyNames.Name(vk);
        if (label.StartsWith("VK ", StringComparison.Ordinal))
            label = usLabel;
        K(label, vk, u);
    }

    private void Sp(double u) => _current.Add(new KeyCapViewModel(string.Empty, null, u * Unit, isSpacer: true));

    private void Build(KeyboardLayoutKind kind, IReadOnlyList<int>? customKeys, bool iso)
    {
        switch (kind)
        {
            case KeyboardLayoutKind.Custom: BuildCustom(customKeys ?? Array.Empty<int>()); return;
            case KeyboardLayoutKind.Numpad: BuildNumpad(); return;
        }

        bool fnRow = kind is KeyboardLayoutKind.Full or KeyboardLayoutKind.TenKeyless or KeyboardLayoutKind.SeventyFive;
        bool navCluster = kind is KeyboardLayoutKind.Full or KeyboardLayoutKind.TenKeyless;
        bool arrows = kind is KeyboardLayoutKind.Full or KeyboardLayoutKind.TenKeyless
                      or KeyboardLayoutKind.SeventyFive or KeyboardLayoutKind.SixtyFive;
        bool numpad = kind is KeyboardLayoutKind.Full;

        // Function row
        if (fnRow)
        {
            Row();
            K("Esc", 0x1B); Sp(1);
            K("F1", 0x70); K("F2", 0x71); K("F3", 0x72); K("F4", 0x73); Sp(0.5);
            K("F5", 0x74); K("F6", 0x75); K("F7", 0x76); K("F8", 0x77); Sp(0.5);
            K("F9", 0x78); K("F10", 0x79); K("F11", 0x7A); K("F12", 0x7B);
            if (navCluster) { Sp(0.5); K("PrSc", 0x2C); K("ScLk", 0x91); K("Pause", 0x13); }
        }

        // Number row
        Row();
        KS(0x29, "~", 0xC0);
        KS(0x02, "1", 0x31); KS(0x03, "2", 0x32); KS(0x04, "3", 0x33); KS(0x05, "4", 0x34);
        KS(0x06, "5", 0x35); KS(0x07, "6", 0x36); KS(0x08, "7", 0x37); KS(0x09, "8", 0x38);
        KS(0x0A, "9", 0x39); KS(0x0B, "0", 0x30);
        KS(0x0C, "-", 0xBD); KS(0x0D, "=", 0xBB); K("Bksp", 0x08, 2);
        if (navCluster) { Sp(0.5); K("Ins", 0x2D); K("Home", 0x24); K("PgUp", 0x21); }
        if (numpad) { Sp(0.5); K("NumLk", 0x90); K("/", 0x6F); K("*", 0x6A); K("-", 0x6D); }

        // Tab row. On ISO the backslash spot is the top half of the tall Enter.
        Row();
        K("Tab", 0x09, 1.5);
        KS(0x10, "Q", 0x51); KS(0x11, "W", 0x57); KS(0x12, "E", 0x45); KS(0x13, "R", 0x52);
        KS(0x14, "T", 0x54); KS(0x15, "Y", 0x59); KS(0x16, "U", 0x55); KS(0x17, "I", 0x49);
        KS(0x18, "O", 0x4F); KS(0x19, "P", 0x50); KS(0x1A, "[", 0xDB); KS(0x1B, "]", 0xDD);
        if (iso) K("Enter", 0x0D, 1.5);
        else KS(0x2B, "\\", 0xDC, 1.5);
        if (navCluster) { Sp(0.5); K("Del", 0x2E); K("End", 0x23); K("PgDn", 0x22); }
        if (numpad) { Sp(0.5); K("7", 0x67); K("8", 0x68); K("9", 0x69); K("+", 0x6B); }

        // Caps row. ISO keeps one more key here (the ANSI backslash's scan code) before
        // the lower half of Enter.
        Row();
        K("Caps", 0x14, 1.75);
        KS(0x1E, "A", 0x41); KS(0x1F, "S", 0x53); KS(0x20, "D", 0x44); KS(0x21, "F", 0x46);
        KS(0x22, "G", 0x47); KS(0x23, "H", 0x48); KS(0x24, "J", 0x4A); KS(0x25, "K", 0x4B);
        KS(0x26, "L", 0x4C); KS(0x27, ";", 0xBA); KS(0x28, "'", 0xDE);
        if (iso) { KS(0x2B, "#", 0xDC); K("Enter", 0x0D, 1.25); }
        else K("Enter", 0x0D, 2.25);
        if (numpad) { Sp(0.5); Sp(3); Sp(0.5); K("4", 0x64); K("5", 0x65); K("6", 0x66); }

        // Shift row. ISO: short left Shift plus the extra key beside it (VK_OEM_102).
        Row();
        if (iso) { K("Shift", 0xA0, 1.25); KS(0x56, "\\", 0xE2); }
        else K("Shift", 0xA0, 2.25);
        KS(0x2C, "Z", 0x5A); KS(0x2D, "X", 0x58); KS(0x2E, "C", 0x43); KS(0x2F, "V", 0x56);
        KS(0x30, "B", 0x42); KS(0x31, "N", 0x4E); KS(0x32, "M", 0x4D);
        KS(0x33, ",", 0xBC); KS(0x34, ".", 0xBE); KS(0x35, "/", 0xBF);
        K("Shift", 0xA1, 2.75);
        if (arrows) { Sp(0.5); Sp(1); K("↑", 0x26); Sp(1); }
        if (numpad) { Sp(0.5); K("1", 0x61); K("2", 0x62); K("3", 0x63); K("Ent", 0x0D); }

        // Control row
        Row();
        K("Ctrl", 0xA2, 1.25); K("Win", 0x5B, 1.25, capturable: false); K("Alt", 0xA4, 1.25);
        K("Space", 0x20, 6.25);
        K("Alt", 0xA5, 1.25); K("Win", 0x5C, 1.25, capturable: false); K("Menu", 0x5D, 1.25); K("Ctrl", 0xA3, 1.25);
        if (arrows) { Sp(0.5); K("←", 0x25); K("↓", 0x28); K("→", 0x27); }
        if (numpad) { Sp(0.5); K("0", 0x60, 2); K(".", 0x6E); }
    }

    private void BuildNumpad()
    {
        Row(); K("NumLk", 0x90); K("/", 0x6F); K("*", 0x6A); K("-", 0x6D);
        Row(); K("7", 0x67); K("8", 0x68); K("9", 0x69); K("+", 0x6B);
        Row(); K("4", 0x64); K("5", 0x65); K("6", 0x66);
        Row(); K("1", 0x61); K("2", 0x62); K("3", 0x63); K("Ent", 0x0D);
        Row(); K("0", 0x60, 2); K(".", 0x6E);
    }

    // Learned/custom keyboards: we know the key set but not the physical positions, so lay the
    // keys out in a tidy wrapping grid, labelled by name.
    private void BuildCustom(IReadOnlyList<int> keys)
    {
        const int perRow = 10;
        for (int i = 0; i < keys.Count; i++)
        {
            if (i % perRow == 0) Row();
            int vk = keys[i];
            string label = VirtualKeyNames.Name(vk);
            double u = Math.Clamp(0.6 + label.Length * 0.22, 1.0, 2.6);
            K(label, vk, u);
        }
    }
}
