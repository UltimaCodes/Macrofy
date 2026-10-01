namespace Macrofy.Core.Input;

// Abstraction over the capture mechanism so the macro engine and UI never touch
// raw Win32. Lets a driver-based backend replace the driver-free one later.
public interface IInputBackend : IDisposable
{
    IReadOnlyList<KeyboardDevice> GetKeyboards(bool includeNonKeyboards = false);

    // Captured devices (by DevicePath) have their keys swallowed and surfaced via
    // CapturedKey instead; everything else types normally. An empty set releases
    // everything and removes the hook.
    void SetCapturedDevices(IEnumerable<string> devicePaths);

    // Fires on the backend thread for every captured key; handlers must return fast.
    event EventHandler<DeviceKeyEvent>? CapturedKey;

    // A keyboard was plugged in or removed (backend thread; may fire in bursts).
    event EventHandler? DevicesChanged;

    // Captured keys went unblocked in this foreground process (backend thread).
    event EventHandler<uint>? IsolationMiss;

    // The hook was installed, removed, or failed to install (backend thread).
    event EventHandler? HookStatusChanged;

    // "Press a key on the keyboard you want": after BeginIdentify, the next fresh key press on
    // any keyboard raises DeviceIdentified once with that keyboard's device path (backend
    // thread). Nothing is blocked; it only reports where the key came from.
    void BeginIdentify();
    void CancelIdentify();
    event EventHandler<string>? DeviceIdentified;

    bool IsHookInstalled { get; }

    // Why the hook couldn't be installed, or null.
    string? HookError { get; }

    void Start();
    void Stop();
}
