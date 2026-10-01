using System.Windows.Input;

namespace Macrofy.App;

// Turns a key pressed in the shortcut recorder into a name MacroExecutor can parse back.
// Punctuation is recorded as the character it types on the current layout ("/", "`", "[").
public static class HotkeyNames
{
    public static string? FromKey(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (key - Key.NumPad0),
        >= Key.F1 and <= Key.F24 => key.ToString(),
        Key.Enter => "Enter",
        Key.Tab => "Tab",
        Key.Space => "Space",
        Key.Back => "Backspace",
        Key.Delete => "Delete",
        Key.Insert => "Insert",
        Key.Home => "Home",
        Key.End => "End",
        Key.PageUp => "PageUp",
        Key.PageDown => "PageDown",
        Key.Up => "Up",
        Key.Down => "Down",
        Key.Left => "Left",
        Key.Right => "Right",
        Key.CapsLock => "CapsLock",
        Key.NumLock => "NumLock",
        Key.Scroll => "ScrollLock",
        Key.PrintScreen => "PrintScreen",
        Key.Pause => "Pause",
        Key.Apps => "Menu",
        Key.Multiply => "NumMultiply",
        Key.Add => "NumAdd",
        Key.Subtract => "NumSubtract",
        Key.Decimal => "NumDecimal",
        Key.Divide => "NumDivide",
        Key.MediaPlayPause => "PlayPause",
        Key.MediaNextTrack => "NextTrack",
        Key.MediaPreviousTrack => "PrevTrack",
        Key.MediaStop => "Stop",
        Key.VolumeUp => "VolumeUp",
        Key.VolumeDown => "VolumeDown",
        Key.VolumeMute => "Mute",
        _ => VirtualKeyNames.CharFor(KeyInterop.VirtualKeyFromKey(key)),
    };

    public static bool IsModifier(Key key) => key
        is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System;
}
