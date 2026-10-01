# Macrofy

Turn a spare keyboard into a macro pad on Windows.

Got a keyboard lying around? Plug it in and Macrofy takes it over. Its keys stop
typing and start firing macros instead (launch an app, open a link, type text, send
a hotkey, control media, run a command), while the keyboard you actually type on
keeps working like nothing happened. No driver, nothing installed system-wide.

Think of it like a Stream Deck, except it's a whole keyboard and it's free.

## What it can do

- **Isolate one keyboard.** Pick a keyboard, flip on capture, and only that one gets
  taken over. Every other keyboard stays completely normal.
- **Bind keys to actions.** Launch apps, open URLs, type text, send hotkeys, control
  media and volume, or run commands.
- **Multi-step macros.** Chain several actions on one key, with delays between them.
- **Layers.** Hold or tap a key to flip the whole keyboard to another set of macros,
  like a Fn layer but for anything you want.
- **Profiles and templates.** Save sets of macros as profiles and switch between them
  whenever you like; any keyboard can use any profile. Start from the built-in OBS
  Studio, Photoshop or VS Code templates, and import or export profiles as files.
- **See what every key does.** Bound keys show their action right on the on-screen
  keyboard. Pick a keyboard by pressing any key on it.
- **Layout presets + calibration.** Tell it whether your board is full size, TKL, 75%,
  65%, 60% or a numpad (ANSI or ISO), or hit "Learn keys" and press every key once to
  build a custom layout for oddball devices.
- **Lives in the tray.** Minimize to tray, start with Windows, auto-capture a chosen
  keyboard at launch, and toggle capture with a global hotkey you pick.
- **Yours to look at.** Light or dark theme (or just follow your Windows setting), with
  smooth scrolling and little touches throughout.

## How it works

The hard part is blocking input from one *specific* keyboard, because the two Windows
APIs you'd reach for don't combine the way you'd want:

- **Raw Input** can tell you which physical keyboard a key came from, but it can't
  block anything.
- **A low-level keyboard hook (`WH_KEYBOARD_LL`)** can block keys, but it's global, has
  no idea which device a key came from, and fires *before* Raw Input. The killer: once
  it blocks a key, Windows never generates that key's Raw Input at all. So you can't
  block a key and also know which keyboard it came from. Blocking destroys the only
  evidence. (Macrofy tried this first. It's a dead end.)

The trick is to block *later*, with a global **`WH_KEYBOARD`** hook (not the LL one).
That hook fires when an app pulls the cooked keyboard message, which is *after* Raw
Input has already figured out the device, so the device info is still intact. It has to
live in a DLL that gets injected into other processes, so Macrofy ships a tiny native
hook (`native/hook.c`, built into `MacrofyHook.dll`):

1. The DLL installs the global `WH_KEYBOARD` hook and, for each key, asks Macrofy's
   hidden decider window whether to block it.
2. Macrofy registers Raw Input too, so by the time the hook asks, it already knows which
   keyboard sent the key. It says block only for the captured keyboard and pass for
   everything else. No re-injection, so your other keyboards are never touched.

### Why not a driver?

A kernel driver would be the bulletproof way to do this, but it's a heavier thing to
install and to trust, and anti-cheat systems can be wary of drivers. So Macrofy stays
driver-free. The hook only loads while you're actually capturing a keyboard, and unloads
the moment you toggle capture off or close the app.

That said, don't assume it's safe for competitive games with kernel-level anti-cheat
(Vanguard, EAC, BattlEye). Macrofy injects a small hook DLL into other processes, which is
the kind of thing those systems watch for. Use it in a game at your own risk.

### What it can't do (and that's fine)

These come with the driver-free approach, they aren't bugs:

- The left and right Windows keys can't be macros. Windows handles them before Macrofy
  ever sees them, so they show up dimmed and can't be bound.
- Capture only stops the keys reaching apps that read the keyboard the normal way. An app
  that reads Raw Input, DirectInput, or polls the keyboard itself (many games, some
  push-to-talk and streaming tools) still sees the captured keyboard. Macrofy shows a
  notice when it detects this happening.
- It can't capture inside apps running as administrator unless Macrofy is also running
  as administrator (there's a "Restart as administrator" button in Settings for that).
- Microsoft Store apps (like Calculator) won't load the hook at all, so the captured
  keyboard works normally while one of those is in focus.

## Getting it

Download **`Macrofy-win-Setup.exe`** from the latest
[release](https://github.com/UltimaCodes/Macrofy/releases/latest) and run it. It installs
for your user only (no admin needed), adds Start menu and desktop shortcuts, and fetches
the .NET 8 desktop runtime first if your PC doesn't have it. Macrofy then keeps itself up
to date. Prefer no installer? Grab `Macrofy-win-Portable.zip` instead.

Heads up: the build isn't code-signed yet, so the first time you run it Windows might show
a blue "Windows protected your PC" box. That's SmartScreen being cautious about an unknown
publisher. Click **More info**, then **Run anyway**.

To remove it, uninstall Macrofy from Windows Settings > Apps like any other app. Your
profiles stay in `%AppData%\Macrofy\` unless you delete that folder.

## How to use it

1. Open Macrofy. On **Keyboards**, pick the keyboard you want to take over: use
   **Switch keyboard**, or **Pick by pressing a key** and press any key on it.
2. Turn on **Capture**. That keyboard is now isolated: its keys stop typing.
3. Click a key on the on-screen keyboard (or press it on the captured keyboard), choose
   what it should do, and hit **Save macro**. Want a sequence? Add steps with delays.
4. Or skip the setup: open **Profiles** and use the OBS Studio, Photoshop or VS Code
   template. The keys light up with their actions right away.
5. Turn capture off (or close the app) any time to hand the keyboard back to Windows.

To make it always-on: in **Settings**, turn on "Start with Windows" and "Capture a
keyboard at startup", and Macrofy will quietly take over your macro keyboard every time
you sign in, and again whenever it's plugged back in.

## Where your stuff lives

Everything is in `%AppData%\Macrofy\`: your profiles (`library\`), which keyboard uses
which profile, device names, layouts, settings, and a `log.txt` if anything ever crashes.
There's an "Open folder" button in Settings. Nothing is sent anywhere, apart from checking
GitHub for a new version (which you can turn off).

## Releasing (for maintainers)

Push a version tag and GitHub Actions does the rest: it runs the tests, builds the
installer with [Velopack](https://velopack.io), and publishes a release with
`Macrofy-win-Setup.exe` plus the update files installed copies look for.

```
git tag v1.1.0
git push origin v1.1.0
```

To build the installer locally instead: `dotnet tool install -g vpk` once, then
`.\publish.ps1` (output in `dist\`).

## NOTE
Virtual keyboards dont work so VMs might be janky, please try to use a physical keyboard in a non-vm environment

## License

MIT, Ryaan Aaqil
