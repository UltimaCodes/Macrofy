using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Macrofy.App;
using Macrofy.Core.Input;

namespace Macrofy.App.ViewModels;

// The on-screen keyboard. The shape depends on the chosen layout (Full/TKL/75/65/60/Numpad,
// ANSI or ISO) or a Custom set of learned keys. Rows feed the UI; SetPressed lights keys live.
//
// Keys are identified by physical key code (see KeyCodes), the same thing macros bind to.
// Alphanumeric keys are placed by scan code and the active keyboard layout decides what to
// print on them, so an AZERTY keyboard shows A where A actually is, and a UK keyboard's '
// key isn't labelled ~.
public sealed class KeyboardLayoutViewModel
{
    private const double Unit = 30;       // px per 1u key
    public double KeyHeight => 30;

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    private const uint MAPVK_VSC_TO_VK_EX = 3;

    private readonly List<List<KeyCapViewModel>> _rows = new();
    private readonly Dictionary<int, List<KeyCapViewModel>> _byKey = new();

    public IReadOnlyList<IReadOnlyList<KeyCapViewModel>> Rows => _rows;

    public KeyboardLayoutViewModel(KeyboardLayoutKind kind = KeyboardLayoutKind.Full,
                                   IReadOnlyList<int>? customKeys = null,
                                   bool iso = false)
        => Build(kind, customKeys, iso);

    public void SetPressed(int keyCode, bool pressed)
    {
        if (_byKey.TryGetValue(keyCode, out var caps))
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

    private void Add(string label, int keyCode, double u, bool capturable = true)
    {
        var cap = new KeyCapViewModel(label, keyCode, u * Unit, capturable: capturable);
        _current.Add(cap);
        if (!_byKey.TryGetValue(keyCode, out var list))
            _byKey[keyCode] = list = new List<KeyCapViewModel>();
        list.Add(cap);
    }

    // A key defined by its virtual key (function row, modifiers, navigation, numpad).
    private void K(string label, int vk, double u = 1, bool capturable = true)
        => Add(label, KeyCodes.FromVk(vk), u, capturable);

    // A key identified by its physical position. The active layout maps scan -> VK and
    // VK -> label; the US label is the fallback if the layout can't answer.
    private void KS(int scan, string usLabel, double u = 1)
    {
        int vk = (int)MapVirtualKey((uint)scan, MAPVK_VSC_TO_VK_EX);
        string label = vk == 0 ? usLabel : VirtualKeyNames.Name(vk);
        if (label.StartsWith("VK ", StringComparison.Ordinal))
            label = usLabel;
        Add(label, scan, u);
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
        KS(0x29, "~");
        KS(0x02, "1"); KS(0x03, "2"); KS(0x04, "3"); KS(0x05, "4"); KS(0x06, "5");
        KS(0x07, "6"); KS(0x08, "7"); KS(0x09, "8"); KS(0x0A, "9"); KS(0x0B, "0");
        KS(0x0C, "-"); KS(0x0D, "="); K("Bksp", 0x08, 2);
        if (navCluster) { Sp(0.5); K("Ins", 0x2D); K("Home", 0x24); K("PgUp", 0x21); }
        if (numpad) { Sp(0.5); K("NumLk", 0x90); K("/", 0x6F); K("*", 0x6A); K("-", 0x6D); }

        // Tab row. On ISO the backslash spot is the top half of the tall Enter.
        Row();
        K("Tab", 0x09, 1.5);
        KS(0x10, "Q"); KS(0x11, "W"); KS(0x12, "E"); KS(0x13, "R"); KS(0x14, "T");
        KS(0x15, "Y"); KS(0x16, "U"); KS(0x17, "I"); KS(0x18, "O"); KS(0x19, "P");
        KS(0x1A, "["); KS(0x1B, "]");
        if (iso) K("Enter", 0x0D, 1.5);
        else KS(0x2B, "\\", 1.5);
        if (navCluster) { Sp(0.5); K("Del", 0x2E); K("End", 0x23); K("PgDn", 0x22); }
        if (numpad) { Sp(0.5); K("7", 0x67); K("8", 0x68); K("9", 0x69); K("+", 0x6B); }

        // Caps row. ISO keeps one more key here (the ANSI backslash's scan code) before
        // the lower half of Enter.
        Row();
        K("Caps", 0x14, 1.75);
        KS(0x1E, "A"); KS(0x1F, "S"); KS(0x20, "D"); KS(0x21, "F"); KS(0x22, "G");
        KS(0x23, "H"); KS(0x24, "J"); KS(0x25, "K"); KS(0x26, "L");
        KS(0x27, ";"); KS(0x28, "'");
        if (iso) { KS(0x2B, "#"); K("Enter", 0x0D, 1.25); }
        else K("Enter", 0x0D, 2.25);
        if (numpad) { Sp(0.5); Sp(3); Sp(0.5); K("4", 0x64); K("5", 0x65); K("6", 0x66); }

        // Shift row. ISO: short left Shift plus the extra key beside it.
        Row();
        if (iso) { K("Shift", 0xA0, 1.25); KS(0x56, "\\"); }
        else K("Shift", 0xA0, 2.25);
        KS(0x2C, "Z"); KS(0x2D, "X"); KS(0x2E, "C"); KS(0x2F, "V"); KS(0x30, "B");
        KS(0x31, "N"); KS(0x32, "M"); KS(0x33, ","); KS(0x34, "."); KS(0x35, "/");
        K("Shift", 0xA1, 2.75);
        if (arrows) { Sp(0.5); Sp(1); K("↑", 0x26); Sp(1); }
        if (numpad) { Sp(0.5); K("1", 0x61); K("2", 0x62); K("3", 0x63); Add("Ent", KeyCodes.NumpadEnter, 1); }

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
        Row(); K("1", 0x61); K("2", 0x62); K("3", 0x63); Add("Ent", KeyCodes.NumpadEnter, 1);
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
            int code = keys[i];
            string label = VirtualKeyNames.NameForKey(code);
            double u = Math.Clamp(0.6 + label.Length * 0.22, 1.0, 2.6);
            Add(label, code, u);
        }
    }
}
