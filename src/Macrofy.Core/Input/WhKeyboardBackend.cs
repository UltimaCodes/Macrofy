using System.Runtime.InteropServices;
using static Macrofy.Core.Input.Interop.NativeMethods;

namespace Macrofy.Core.Input;

// The capture backend. A native WH_KEYBOARD hook DLL (MacrofyHook.dll) is loaded into every
// process that reads keyboard input; it fires when an app pulls the cooked keyboard message -
// AFTER Raw Input has already identified the source device - and asks this decider window
// whether to block. Because blocking happens at the cooked stage, it does NOT destroy the Raw
// Input we need, which is the catch-22 that made the in-process WH_KEYBOARD_LL approach
// impossible. We register Raw Input here too, so by the time the hook asks, the device for
// that key is known: block only the captured device and pass everything else through
// untouched - no re-injection. 32-bit apps can't load the 64-bit DLL, so Windows runs the
// hook for them on this thread instead, which works the same way.
//
// The hook is only installed while something is captured, so with capture off nothing of
// Macrofy's is loaded into other apps.
//
// Inherent driver-free limits: keys handled before Raw Input (Windows key) can't be
// attributed; apps that read the keyboard themselves (Raw Input, DirectInput, polling) still
// see captured keys; Microsoft Store apps never run desktop hooks; apps running as admin are
// only covered when Macrofy is too.
public sealed class WhKeyboardBackend : IInputBackend
{
    public const string HookMessageName = "Macrofy.HookQuery.v1";
    private const string DeciderWindowClass = "MacrofyDeciderWnd";
    private const int ExpectedHookVersion = 2;
    private const uint WmSyncHook = WM_APP + 1;
    private const int VkSnapshot = 0x2C;

    // Isolation-miss reporting: a few unanswered presses in one app within a short window,
    // then at most one report per app per minute.
    private const int MissThreshold = 3;
    private const long MissWindowMs = 10_000;
    private const long MissReportIntervalMs = 60_000;

    private readonly object _gate = new();
    private readonly HashSet<string> _capturedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<nint, bool> _capturedByHandle = new();
    private readonly HashSet<(nint Device, int Key)> _heldRaw = new();
    private readonly CaptureDecider _decider = new();
    private readonly List<uint> _missedPids = new();
    private readonly Dictionary<uint, MissTally> _missTallies = new();

    private readonly WndProc _wndProc;
    private Thread? _thread;
    private nint _window;
    private uint _hookMessage;
    private volatile bool _running;
    private volatile bool _capturing;
    private readonly ManualResetEventSlim _ready = new(false);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool HookFn();

    private HookFn? _startHook;
    private HookFn? _stopHook;
    private long _hookQueries;

    public event EventHandler<DeviceKeyEvent>? CapturedKey;
    public event EventHandler? DevicesChanged;
    public event EventHandler<uint>? IsolationMiss;
    public event EventHandler? HookStatusChanged;

    private volatile bool _hookInstalled;
    public bool IsHookInstalled => _hookInstalled;

    private volatile string? _hookError;
    public string? HookError => _hookError;

    // Where the hook DLL was loaded from, and how many "block?" questions it has asked
    // (diagnostics).
    public string? HookDllPath { get; private set; }
    public long HookQueries => Interlocked.Read(ref _hookQueries);

    public WhKeyboardBackend()
    {
        _wndProc = WindowProc;
        _decider.Missed += pid => _missedPids.Add(pid);
    }

    // Keys the OS routes before Raw Input can attribute them - left untouched.
    private static bool IsExcluded(int vk) => vk is 0x5B or 0x5C; // L/R Windows key

    public IReadOnlyList<KeyboardDevice> GetKeyboards(bool includeNonKeyboards = false)
        => RawInputDeviceEnumerator.GetKeyboards(includeNonKeyboards);

    public void SetCapturedDevices(IEnumerable<string> devicePaths)
    {
        lock (_gate)
        {
            _capturedPaths.Clear();
            foreach (var p in devicePaths)
                _capturedPaths.Add(p);
            _capturedByHandle.Clear(); // re-evaluate handle->captured against the new set
            _decider.Reset();
            _missedPids.Clear();
            _capturing = _capturedPaths.Count > 0;
        }
        if (_window != nint.Zero)
            PostMessage(_window, WmSyncHook, nint.Zero, nint.Zero);
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(DeciderThread)
        {
            IsBackground = true,
            Name = "Macrofy.Decider",
            Priority = ThreadPriority.Highest,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(3000);
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        if (_window != nint.Zero)
            PostMessage(_window, WM_CLOSE, nint.Zero, nint.Zero);
        _thread?.Join(2000);
        _thread = null;
        _ready.Reset();
    }

    public void Dispose() => Stop();

    // Raw Input reports modifiers as the generic VKs (0x10-0x12), but the UI and saved
    // bindings use the left/right-specific ones. MakeCode tells the shifts apart; the E0
    // flag tells right Ctrl/Alt from left. The block/pass path is unaffected - the decider
    // folds these back to generic.
    private static ushort SplitLeftRight(ushort vk, ushort makeCode, ushort flags) => vk switch
    {
        0x10 => makeCode == 0x36 ? (ushort)0xA1 : (ushort)0xA0,
        0x11 => (flags & RI_KEY_E0) != 0 ? (ushort)0xA3 : (ushort)0xA2,
        0x12 => (flags & RI_KEY_E0) != 0 ? (ushort)0xA5 : (ushort)0xA4,
        _ => vk,
    };

    private void DeciderThread()
    {
        try
        {
            SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_TIME_CRITICAL);
            nint hInstance = GetModuleHandle(null);
            _hookMessage = RegisterWindowMessage(HookMessageName);

            var wndClass = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = _wndProc,
                hInstance = hInstance,
                lpszClassName = DeciderWindowClass,
            };
            RegisterClassEx(ref wndClass);

            // A real top-level window the hook DLL can locate by class (FindWindow) and that
            // receives background Raw Input (message-only windows don't get RIDEV_INPUTSINK).
            _window = CreateWindowEx(
                WS_EX_TOOLWINDOW, DeciderWindowClass, "Macrofy", 0, 0, 0, 0, 0,
                nint.Zero, nint.Zero, hInstance, nint.Zero);

            var rid = new[]
            {
                new RAWINPUTDEVICE
                {
                    usUsagePage = HID_USAGE_PAGE_GENERIC,
                    usUsage = HID_USAGE_GENERIC_KEYBOARD,
                    dwFlags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY,
                    hwndTarget = _window,
                },
            };
            RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());

            LoadHookDll();
            _ready.Set();
            SyncHook(); // capture may have been requested before the window existed

            while (GetMessage(out var msg, nint.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        catch (Exception ex)
        {
            // Never take the whole app down over the hook; the UI shows HookError instead.
            _hookError = "The keyboard hook stopped unexpectedly: " + ex.Message;
            HookStatusChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _ready.Set();
            try
            {
                if (_hookInstalled)
                    _stopHook?.Invoke();
                _hookInstalled = false;
                var remove = new[]
                {
                    new RAWINPUTDEVICE
                    {
                        usUsagePage = HID_USAGE_PAGE_GENERIC,
                        usUsage = HID_USAGE_GENERIC_KEYBOARD,
                        dwFlags = RIDEV_REMOVE,
                        hwndTarget = nint.Zero,
                    },
                };
                RegisterRawInputDevices(remove, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
            }
            catch { /* shutting down */ }
            _window = nint.Zero;
        }
    }

    // Resolve the native exports by hand instead of [DllImport]: a missing, quarantined or
    // stale DLL then becomes a message in the UI rather than an exception that kills the app.
    private void LoadHookDll()
    {
        string? path = HookDllLocator.Resolve(out string? problem);
        if (path is null)
        {
            _hookError = problem;
            return;
        }
        HookDllPath = path;
        if (!NativeLibrary.TryLoad(path, out nint lib))
        {
            _hookError = "Windows couldn't load MacrofyHook.dll. Your antivirus may be blocking it.";
            return;
        }
        if (!NativeLibrary.TryGetExport(lib, "StartHook", out nint start)
            || !NativeLibrary.TryGetExport(lib, "StopHook", out nint stop)
            || !NativeLibrary.TryGetExport(lib, "MacrofyHookVersion", out nint version)
            || Marshal.GetDelegateForFunctionPointer<VersionFn>(version)() != ExpectedHookVersion)
        {
            _hookError = "MacrofyHook.dll doesn't match this version of Macrofy. Download Macrofy again and keep the two files together.";
            return;
        }
        _startHook = Marshal.GetDelegateForFunctionPointer<HookFn>(start);
        _stopHook = Marshal.GetDelegateForFunctionPointer<HookFn>(stop);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int VersionFn();

    // Decider thread: install the hook while something is captured, remove it otherwise.
    private void SyncHook()
    {
        bool want = _capturing;
        if (want && !_hookInstalled)
        {
            if (_startHook is not null)
            {
                if (_startHook())
                {
                    _hookInstalled = true;
                    _hookError = null;
                }
                else
                    _hookError = "Windows refused to install Macrofy's keyboard hook.";
            }
            HookStatusChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (!want && _hookInstalled)
        {
            _stopHook?.Invoke();
            _hookInstalled = false;
            HookStatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_INPUT)
        {
            ProcessRawInput(lParam);
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }
        if (msg == _hookMessage && _hookMessage != 0)
        {
            Interlocked.Increment(ref _hookQueries);
            return Decide((int)wParam, (long)lParam) ? 1 : nint.Zero;
        }
        if (msg == WM_INPUT_DEVICE_CHANGE)
        {
            // A device came or went: its handle may be reused for something else.
            lock (_gate)
            {
                _capturedByHandle.Remove(lParam);
                _heldRaw.RemoveWhere(k => k.Device == lParam);
            }
            DevicesChanged?.Invoke(this, EventArgs.Empty);
            return nint.Zero;
        }
        if (msg == WmSyncHook)
        {
            SyncHook();
            return nint.Zero;
        }
        if (msg == WM_DESTROY)
        {
            PostQuitMessage(0);
            return nint.Zero;
        }
        if (msg == WM_CLOSE)
        {
            DestroyWindow(hWnd);
            return nint.Zero;
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    // The hook asks: block this key? Drain any pending Raw Input first so the device
    // verdict for THIS key is current, then answer. Default = pass (never block on doubt).
    private bool Decide(int vk, long lParam)
    {
        if (!_capturing || IsExcluded(vk))
            return false;

        bool isDown = (lParam & 0x80000000L) == 0;   // bit 31: 1 = released
        bool isRepeat = (lParam & 0x40000000L) != 0; // bit 30: 1 = was already down

        DrainRawInput();

        bool block;
        lock (_gate)
            block = _decider.Decide(vk, isDown, isRepeat, Environment.TickCount64);
        ReportMisses();
        return block;
    }

    private void DrainRawInput()
    {
        while (PeekMessage(out var m, _window, WM_INPUT, WM_INPUT, PM_REMOVE))
            ProcessRawInput(m.lParam);
    }

    private void ProcessRawInput(nint hRawInput)
    {
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        if (GetRawInputData(hRawInput, RID_INPUT, nint.Zero, ref size, headerSize) != 0 || size == 0)
            return;

        nint buffer = Marshal.AllocHGlobal((int)size);
        DeviceKeyEvent? surfaced = null;
        try
        {
            if (GetRawInputData(hRawInput, RID_INPUT, buffer, ref size, headerSize) != size)
                return;
            var raw = Marshal.PtrToStructure<RAWINPUT>(buffer);
            if (raw.header.dwType != RIM_TYPEKEYBOARD)
                return;
            ushort vkey = raw.keyboard.VKey;
            if (vkey is 0 or 0xFF) // 0xFF = fake keys from escaped scan-code sequences
                return;

            int keyCode = KeyCodes.FromRaw(raw.keyboard.MakeCode, raw.keyboard.Flags, vkey);
            int vk = SplitLeftRight(vkey, raw.keyboard.MakeCode, raw.keyboard.Flags);
            bool isDown = (raw.keyboard.Flags & RI_KEY_BREAK) == 0;
            nint device = raw.header.hDevice;
            bool captured = IsCaptured(device);

            lock (_gate)
            {
                // Raw Input carries the keyboard's auto-repeat as more presses; a press of a
                // key this device already holds down is a repeat.
                var held = (device, keyCode);
                bool isRepeat = isDown && !_heldRaw.Add(held);
                if (!isDown)
                    _heldRaw.Remove(held);

                if (_capturing && !IsExcluded(vk))
                {
                    // Print Screen never produces a key-down message, so no hook query is
                    // coming for it; don't count that as a miss.
                    uint foreground = captured && isDown && !isRepeat && vk != VkSnapshot ? ForegroundPid() : 0;
                    _decider.Record(vk, isDown, isRepeat, captured, foreground, Environment.TickCount64);
                }

                if (captured && !IsExcluded(vk))
                    surfaced = new DeviceKeyEvent(keyCode, vk, isDown, isRepeat);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        ReportMisses();
        if (surfaced is { } e)
            CapturedKey?.Invoke(this, e);
    }

    private static uint ForegroundPid()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
        return pid;
    }

    // Decider thread, outside the lock: turn recorded misses into (rate-limited) reports.
    private void ReportMisses()
    {
        if (_missedPids.Count == 0)
            return;
        long now = Environment.TickCount64;
        var report = new List<uint>();
        lock (_gate)
        {
            foreach (uint pid in _missedPids)
            {
                if (!_missTallies.TryGetValue(pid, out var tally) || now - tally.WindowStart > MissWindowMs)
                    tally = new MissTally(now, 0, tally.LastReport);
                tally = tally with { Count = tally.Count + 1 };
                if (tally.Count >= MissThreshold && now - tally.LastReport > MissReportIntervalMs)
                {
                    tally = new MissTally(now, 0, now);
                    report.Add(pid);
                }
                _missTallies[pid] = tally;
            }
            _missedPids.Clear();
        }
        foreach (uint pid in report)
            IsolationMiss?.Invoke(this, pid);
    }

    private readonly record struct MissTally(long WindowStart, int Count, long LastReport);

    private bool IsCaptured(nint hDevice)
    {
        if (hDevice == nint.Zero)
            return false;
        lock (_gate)
        {
            if (_capturedByHandle.TryGetValue(hDevice, out var known))
                return known;
            if (_capturedPaths.Count == 0)
                return false;
        }
        string path = RawInputDeviceEnumerator.GetDeviceName(hDevice) ?? string.Empty;
        bool captured;
        lock (_gate)
        {
            captured = _capturedPaths.Contains(path);
            _capturedByHandle[hDevice] = captured;
        }
        return captured;
    }
}
