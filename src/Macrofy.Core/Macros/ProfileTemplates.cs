namespace Macrofy.Core.Macros;

// A ready-made profile someone can start from. Build() makes a fresh copy each time, so a
// profile made from a template never shares bindings with another one.
public sealed record ProfileTemplate(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<string> Setup,   // what to do first; empty when it works out of the box
    string Icon,                   // a Fluent icon name for the card
    string Accent,                 // card accent colour, #RRGGBB
    Func<MacroProfile> Build);

// The built-in templates. Keys are bound by physical position (scan code), so they land on
// the same keys whatever the keyboard's language layout.
public static class ProfileTemplates
{
    public static IReadOnlyList<ProfileTemplate> All { get; } = new[]
    {
        new ProfileTemplate(
            "template:obs", "OBS Studio",
            "Record, stream, switch scenes and mute your mic from the macro keyboard.",
            new[]
            {
                "OBS has no shortcuts until you set them, so do this once.",
                "In OBS, open Settings > Hotkeys. Click a hotkey box, then press the matching key on your macro keyboard (with capture on). Macrofy sends the shortcut, so OBS fills the box in for you.",
                "Give Start and Stop the same key so it works as a toggle: F1 = Start/Stop Recording, F2 = Start/Stop Streaming, F3 = Pause/Unpause Recording, F4 = Save Replay.",
                "1 to 6 = each scene's \"Switch to scene\". M = Mic/Aux mute and unmute, N = Desktop Audio mute and unmute, S = Screenshot Output, V = Start/Stop Virtual Camera.",
            },
            "Video24", "#A78BFA", BuildObs),
        new ProfileTemplate(
            "template:photoshop", "Photoshop",
            "Tools, layers, undo and zoom on dedicated keys. Uses Photoshop's default shortcuts.",
            new[] { "Works with Photoshop's default shortcuts. Keep Photoshop in front when you press the keys." },
            "PaintBrush24", "#38BDF8", BuildPhotoshop),
        new ProfileTemplate(
            "template:vscode", "VS Code",
            "Command palette, panels, debugging and editing on one hand. Uses VS Code's default shortcuts.",
            new[] { "Works with VS Code's default keyboard shortcuts (US layout). Keep VS Code in front when you press the keys." },
            "Code24", "#34D399", BuildVsCode),
    };

    public static ProfileTemplate? Find(string? id) => All.FirstOrDefault(t => t.Id == id);

    // Scan codes (set 1) for the keys the templates use.
    private const int F1 = 0x3B, F2 = 0x3C, F3 = 0x3D, F4 = 0x3E, F5 = 0x3F, F6 = 0x40,
                      F7 = 0x41, F8 = 0x42, F9 = 0x43, F10 = 0x44, F11 = 0x57, F12 = 0x58;
    private const int N1 = 0x02, N2 = 0x03, N3 = 0x04, N4 = 0x05, N5 = 0x06, N6 = 0x07,
                      N7 = 0x08, N8 = 0x09, N9 = 0x0A, N0 = 0x0B;
    private const int Q = 0x10, W = 0x11, E = 0x12, R = 0x13, T = 0x14,
                      A = 0x1E, S = 0x1F, D = 0x20, F = 0x21, G = 0x22,
                      Z = 0x2C, X = 0x2D, C = 0x2E, V = 0x2F, N = 0x31, M = 0x32, O = 0x18;

    private static MacroProfile BuildObs()
    {
        const string combo = "Ctrl+Alt+Shift+";
        return Profile("OBS Studio",
            Hotkey(F1, "F1", "Record", combo + "F1"),
            Hotkey(F2, "F2", "Stream", combo + "F2"),
            Hotkey(F3, "F3", "Pause rec", combo + "F3"),
            Hotkey(F4, "F4", "Save replay", combo + "F4"),
            Hotkey(N1, "1", "Scene 1", combo + "1"),
            Hotkey(N2, "2", "Scene 2", combo + "2"),
            Hotkey(N3, "3", "Scene 3", combo + "3"),
            Hotkey(N4, "4", "Scene 4", combo + "4"),
            Hotkey(N5, "5", "Scene 5", combo + "5"),
            Hotkey(N6, "6", "Scene 6", combo + "6"),
            Hotkey(M, "M", "Mute mic", combo + "M"),
            Hotkey(N, "N", "Mute desktop", combo + "N"),
            Hotkey(S, "S", "Screen shot", combo + "S"),
            Hotkey(V, "V", "Virtual cam", combo + "V"),
            Bind(O, "O", "Open OBS", MacroActionKind.LaunchApp, @"C:\Program Files\obs-studio\bin\64bit\obs64.exe"));
    }

    private static MacroProfile BuildPhotoshop() => Profile("Photoshop",
        Hotkey(Q, "Q", "Brush", "B"),
        Hotkey(W, "W", "Eraser", "E"),
        Hotkey(E, "E", "Move", "V"),
        Hotkey(R, "R", "Marquee", "M"),
        Hotkey(T, "T", "Lasso", "L"),
        Hotkey(A, "A", "Quick select", "W"),
        Hotkey(S, "S", "Crop", "C"),
        Hotkey(D, "D", "Eyedropper", "I"),
        Hotkey(F, "F", "Text", "T"),
        Hotkey(G, "G", "Hand", "H"),
        Hotkey(Z, "Z", "Undo", "Ctrl+Z", repeat: true),
        Hotkey(X, "X", "Redo", "Ctrl+Shift+Z", repeat: true),
        Hotkey(C, "C", "Brush -", "[", repeat: true),
        Hotkey(V, "V", "Brush +", "]", repeat: true),
        Hotkey(N1, "1", "New layer", "Ctrl+Shift+Alt+N"),
        Hotkey(N2, "2", "Duplicate", "Ctrl+J"),
        Hotkey(N3, "3", "Merge down", "Ctrl+E"),
        Hotkey(N4, "4", "Group", "Ctrl+G"),
        Hotkey(N5, "5", "Transform", "Ctrl+T"),
        Hotkey(N6, "6", "Deselect", "Ctrl+D"),
        Hotkey(N7, "7", "Fit screen", "Ctrl+0"),
        Hotkey(N8, "8", "Zoom in", "Ctrl++", repeat: true),
        Hotkey(N9, "9", "Zoom out", "Ctrl+-", repeat: true),
        Hotkey(N0, "0", "Save", "Ctrl+S"));

    private static MacroProfile BuildVsCode() => Profile("VS Code",
        Hotkey(F1, "F1", "Commands", "Ctrl+Shift+P"),
        Hotkey(F2, "F2", "Go to file", "Ctrl+P"),
        Hotkey(F3, "F3", "Terminal", "Ctrl+`"),
        Hotkey(F4, "F4", "Sidebar", "Ctrl+B"),
        Hotkey(F5, "F5", "Debug", "F5"),
        Hotkey(F6, "F6", "Stop debug", "Shift+F5"),
        Hotkey(F7, "F7", "Step over", "F10", repeat: true),
        Hotkey(F8, "F8", "Step into", "F11", repeat: true),
        Hotkey(F9, "F9", "Breakpoint", "F9"),
        Hotkey(F10, "F10", "Definition", "F12"),
        Hotkey(F11, "F11", "Rename", "F2"),
        Hotkey(F12, "F12", "Format", "Shift+Alt+F"),
        Hotkey(N1, "1", "Explorer", "Ctrl+Shift+E"),
        Hotkey(N2, "2", "Search", "Ctrl+Shift+F"),
        Hotkey(N3, "3", "Git", "Ctrl+Shift+G"),
        Hotkey(N4, "4", "Extensions", "Ctrl+Shift+X"),
        Hotkey(N5, "5", "New terminal", "Ctrl+Shift+`"),
        Hotkey(Q, "Q", "Comment", "Ctrl+/"),
        Hotkey(W, "W", "Next match", "Ctrl+D", repeat: true),
        Hotkey(E, "E", "Line up", "Alt+Up", repeat: true),
        Hotkey(R, "R", "Line down", "Alt+Down", repeat: true),
        // VS Code's "Save All" is the chord Ctrl+K then S - a two-step macro.
        Sequence(T, "T", "Save all",
            new MacroStep { Action = new MacroAction { Kind = MacroActionKind.SendHotkey, Target = "Ctrl+K" }, DelayMsAfter = 60 },
            new MacroStep { Action = new MacroAction { Kind = MacroActionKind.SendHotkey, Target = "S" } }));

    private static MacroProfile Profile(string name, params MacroBinding[] bindings)
    {
        var profile = new MacroProfile
        {
            Name = name,
            Layers = new List<MacroLayer> { new() { Name = "Base", Bindings = bindings.ToList() } },
        };
        profile.Normalize();
        return profile;
    }

    private static MacroBinding Hotkey(int scan, string keyName, string label, string combo, bool repeat = false)
        => Bind(scan, keyName, label, MacroActionKind.SendHotkey, combo, repeat);

    private static MacroBinding Bind(int scan, string keyName, string label, MacroActionKind kind, string target, bool repeat = false)
        => new()
        {
            KeyCode = scan,
            KeyName = keyName,
            Label = label,
            Action = new MacroAction { Kind = kind, Target = target },
            RepeatWhileHeld = repeat,
        };

    private static MacroBinding Sequence(int scan, string keyName, string label, params MacroStep[] steps)
        => new() { KeyCode = scan, KeyName = keyName, Label = label, Steps = steps.ToList() };
}
