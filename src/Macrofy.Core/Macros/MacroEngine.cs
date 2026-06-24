using Macrofy.Core.Input;

namespace Macrofy.Core.Macros;

// Turns captured key-presses into macro actions. Bindings are set when a device is
// captured; on each captured key-DOWN the matching action runs on a thread-pool thread
// so a slow launch never stalls the capture pipeline.
public sealed class MacroEngine
{
    private readonly object _gate = new();
    private Dictionary<int, MacroAction> _byVk = new();

    public void SetBindings(IEnumerable<MacroBinding> bindings)
    {
        var map = new Dictionary<int, MacroAction>();
        foreach (var b in bindings)
            if (!b.Action.IsEmpty)
                map[b.VirtualKey] = b.Action;
        lock (_gate)
            _byVk = map;
    }

    public void Clear()
    {
        lock (_gate)
            _byVk = new Dictionary<int, MacroAction>();
    }

    // Called from the capture backend (decider thread) — must return fast.
    public void OnCapturedKey(DeviceKeyEvent e)
    {
        if (!e.IsKeyDown)
            return;
        MacroAction? action;
        lock (_gate)
            _byVk.TryGetValue(e.VirtualKey, out action);
        if (action is not null)
            Task.Run(() => MacroExecutor.Execute(action));
    }
}
