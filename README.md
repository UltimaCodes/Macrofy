# Macrofy

Turn a spare keyboard into a macro pad on Windows.

Plug in two keyboards. Macrofy **captures one of them** and turns it into a
Stream Deck–style macro pad — its keys stop typing and instead fire macros
(launch an app, open a URL, type text, send a hotkey, run a command) — while
your **other keyboard keeps working completely normally**.

> Status: capture **and** macros work. Pick a keyboard, toggle capture, press a
> key to select it, bind it to an action. Driver-free.

## Why a native Windows app (not web / Electron)

The core feature is low-level Windows keyboard input — identifying *which*
physical keyboard a keystroke came from and blocking it from the OS. That's
in-process Win32 plus a small native hook DLL. Electron would still need exactly
that native code, wrapped in a 150 MB Chromium runtime. So this is **.NET 8 + WPF**,
styled with [WPF-UI](https://github.com/lepoco/wpfui) for a Fluent (Windows 11) look,
with a tiny C hook DLL for the part that has to be native.

## The core challenge: per-device isolation

Blocking input from *one specific keyboard* on Windows is genuinely hard, because
the two relevant APIs don't naturally combine:

- **Raw Input** tells you which device sent each key — but is read-only; it can't
  block.
- **A low-level keyboard hook (`WH_KEYBOARD_LL`)** can block keys — but is global,
  carries no device info, and (the killer) fires *before* Raw Input. Worse: when
  it blocks a key, Windows never generates that key's Raw Input at all. So you
  can't both block a key and learn which device it came from — blocking destroys
  the only evidence. (Macrofy went down this road first; it's a dead end.)

### How Macrofy does it (driver-free)

The fix is to block **later** in the pipeline, with a global **`WH_KEYBOARD`** hook
(not `WH_KEYBOARD_LL`). `WH_KEYBOARD` fires when an app pulls the *cooked* keyboard
message — *after* Raw Input has already identified the device — so blocking there
keeps the device info intact. `WH_KEYBOARD` must live in a DLL injected into other
processes, so Macrofy ships a tiny native hook (`native/hook.c` → `MacrofyHook.dll`):

1. The DLL installs the global `WH_KEYBOARD` hook. For each key it asks Macrofy's
   hidden **decider window** (`SendMessage(WM_HOOK)`) whether to block.
2. Macrofy registers **Raw Input**, so by the time the hook asks, it already knows
   the source device for that key. It answers *block* only for the captured
   keyboard and *pass* for everything else — **no re-injection**, so your other
   keyboards are never touched.

This is the approach proven by projects like
[NotEnoughHotkeys](https://github.com/VollRahm/NotEnoughHotkeys).

### Why not the Interception driver?

A kernel filter driver ([Interception](https://github.com/oblitum/Interception))
would be the most robust option, but **anti-cheat systems flag it by mere
presence** (Vanguard, EAC, BattlEye…), so it's deliberately not used. Macrofy's
`WH_KEYBOARD` approach has no kernel driver and no persistent footprint — close
the app (or toggle capture off) and nothing is loaded.

**Driver-free limitations** (inherent, not bugs): the Windows key and a couple of
keys the OS handles before Raw Input can't be attributed and pass through; and the
DLL can't inject into *elevated* apps (unless Macrofy is run elevated) or some
sandboxed Store apps, so the captured keyboard won't be blocked while one of those
is foreground.

## Macros

Capture a keyboard, press a key to select it, and bind it to an action:

| Action       | Target field            |
|--------------|-------------------------|
| Launch app   | exe / path (+ arguments)|
| Open URL     | https://…               |
| Type text    | literal text to type    |
| Send hotkey  | e.g. `Ctrl+Shift+Esc`   |
| Run command  | a shell command line    |

Bindings are saved per device to `%AppData%\Macrofy\profiles\` as JSON, and run on
a background thread so a slow launch never stalls capture.

## Using it

1. `dotnet run --project src/Macrofy.App`
2. On **Devices**, pick a keyboard (named from its HID product string).
3. Toggle **Capture**. Press a key on that keyboard — it's isolated from your apps
   and selected in the **Macros** panel.
4. Choose an action, fill the target, **Add binding**. Now that key runs the macro.
5. Toggle capture off (or close the app) to hand the keyboard back to Windows.

## Build & run

```powershell
# one-time: a C compiler to build the native hook DLL
winget install BrechtSanders.WinLibs.POSIX.UCRT
native\build.ps1        # produces native\MacrofyHook.dll

dotnet build            # MacrofyHook.dll is copied next to the app automatically
dotnet run --project src/Macrofy.App
```

Requires the .NET 8 SDK on Windows (x64). A prebuilt `MacrofyHook.dll` is committed,
so you only need the compiler if you change `native/hook.c`.

## Project layout

```
Macrofy.sln
native/
  hook.c                 the native WH_KEYBOARD hook DLL (build.ps1 -> MacrofyHook.dll)
src/
  Macrofy.App/           WPF (.NET 8) UI
    ViewModels/MainViewModel.cs   devices, capture toggle, macro binding editor
    MainWindow.xaml               shell, device list, on-screen keyboard, Macros panel
  Macrofy.Core/          input + macros, no UI dependency
    Input/
      WhKeyboardBackend.cs        the working capture engine (WH_KEYBOARD + Raw Input)
      RawInputHookBackend.cs      earlier WH_KEYBOARD_LL engine (kept for the diag tool)
      IInputBackend.cs            abstraction over the capture mechanism
      RawInputDeviceEnumerator.cs keyboard enumeration & grouping
      Interop/NativeMethods.cs    Win32 P/Invoke
    Macros/
      MacroModels.cs              action / binding / profile
      MacroEngine.cs              captured key -> action dispatch
      MacroExecutor.cs            runs actions (launch / url / text / hotkey / command)
      MacroProfileStore.cs        JSON persistence per device
  Macrofy.Diag/          console diagnostics (list / monitor / capture / selftest)
```

## The phases

- **Phase 1 — Detection** ✅
  Enumerate every physical keyboard via Raw Input, group a device's HID collections
  together by VID/PID, filter out non-keyboard HID devices, and auto-name each from
  its HID product string. Custom names can be saved per device.

- **Phase 2 — Isolation** ✅
  Block the captured keyboard's keys system-wide while leaving every other keyboard
  native. This was the hard part. The first approach — an in-process `WH_KEYBOARD_LL`
  hook correlated with Raw Input — is a **dead end**: that hook fires *before* Raw
  Input, and when it blocks a key Windows never generates that key's Raw Input, so
  blocking destroys the only evidence of which device the key came from. The working
  approach is a global **`WH_KEYBOARD`** hook (native `MacrofyHook.dll`) that fires at
  the cooked-message stage, *after* Raw Input has identified the device; it blocks
  only the captured keyboard and passes the rest through with no re-injection.

- **Phase 3 — Macros** ✅
  Bind each captured key to an action — launch app, open URL, type text, send hotkey,
  run command — saved as per-device JSON profiles in `%AppData%\Macrofy\profiles\`,
  and executed on a background thread so a slow launch never stalls capture.

- **Phase 4 — Polish** (next)
  System tray + autostart, multi-step macros, and profile import/export.

## License

MIT © Ryaan Aaqil
