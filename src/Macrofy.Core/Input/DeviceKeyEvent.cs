namespace Macrofy.Core.Input;

// A key press/release from a captured device. KeyCode is the physical key (see KeyCodes)
// and is what macros bind to; VirtualKey is the left/right-specific VK for display.
// IsRepeat marks the keyboard's auto-repeat while a key is held.
public readonly record struct DeviceKeyEvent(
    int KeyCode,
    int VirtualKey,
    bool IsKeyDown,
    bool IsRepeat = false);
