using System.Diagnostics;
using Macrofy.Core;
using Macrofy.Core.Input;

// Command-line diagnostics for the same capture backend the app uses (WhKeyboardBackend).
var command = args.Length > 0 ? args[0].ToLowerInvariant() : "list";
var consoleLock = new object();
var clock = Stopwatch.StartNew();

string Ts() => $"{clock.Elapsed.TotalMilliseconds,9:F1}ms";

void Log(string line)
{
    lock (consoleLock)
        Console.WriteLine(line);
}

switch (command)
{
    case "list":
        ListDevices(includeAll: args.Contains("--all"));
        break;
    case "capture":
        Capture(args.Length > 1 ? args[1] : "0");
        break;
    default:
        Console.WriteLine("Usage: Macrofy.Diag [list [--all] | capture <index>]");
        break;
}

void ListDevices(bool includeAll)
{
    using var backend = new WhKeyboardBackend();
    var devices = backend.GetKeyboards(includeAll);
    Console.WriteLine($"== Keyboards ({devices.Count}) ==");
    for (int i = 0; i < devices.Count; i++)
    {
        var d = devices[i];
        Console.WriteLine($"[{i}] {d.DisplayName}  (keyboard={d.IsLikelyKeyboard}, {d.CollectionCount} collection(s))");
        Console.WriteLine($"      id: {d.Id}");
        foreach (var p in d.DevicePaths)
            Console.WriteLine($"      {p}");
    }
}

void Capture(string indexArg)
{
    using var backend = new WhKeyboardBackend();
    var devices = backend.GetKeyboards(includeNonKeyboards: true);
    if (!int.TryParse(indexArg, out int index) || index < 0 || index >= devices.Count)
    {
        Console.WriteLine($"Invalid index. Run 'list --all' to see indices (0..{devices.Count - 1}).");
        return;
    }

    var device = devices[index];
    backend.CapturedKey += (_, e) =>
        Log($"{Ts()} [CAPTURED] {(e.IsKeyDown ? "DN" : "up")}{(e.IsRepeat ? " (repeat)" : "")}  key=0x{e.KeyCode:X}  vk=0x{e.VirtualKey:X2}");
    backend.IsolationMiss += (_, pid) =>
    {
        var (name, kind) = ForegroundApp.Describe(pid);
        Log($"{Ts()} [ISOLATION MISS] keys are reaching {name} ({kind}); it isn't being blocked.");
    };
    backend.HookStatusChanged += (_, _) =>
        Log($"{Ts()} [HOOK] installed={backend.IsHookInstalled}  error={backend.HookError ?? "(none)"}");
    backend.DevicesChanged += (_, _) => Log($"{Ts()} [DEVICES] a keyboard was plugged in or removed.");
    backend.Start();
    backend.SetCapturedDevices(device.DevicePaths);

    Console.WriteLine($"Elevated: {ProcessElevation.IsElevated}");
    Console.WriteLine($"Hook DLL: {backend.HookDllPath ?? "(not loaded)"}");
    Console.WriteLine($"Hook installed: {backend.IsHookInstalled}   {backend.HookError}");
    Console.WriteLine($"Capturing \"{device.DisplayName}\" ({device.CollectionCount} collection(s)).");
    Console.WriteLine("Press its keys - every one should print [CAPTURED] and NOT type here.");
    Console.WriteLine("Test with this console focused AND with another app focused. Press Enter to stop.");
    Console.ReadLine();
    Console.WriteLine($"Total hook queries answered: {backend.HookQueries}");
}
