using System.Collections.Concurrent;
using Macrofy.Core.Input;

namespace Macrofy.Core.Macros;

// Turns captured key-presses into macro actions, with layer support. A profile holds one
// or more layers; LayerHold/LayerToggle bindings change which layer is "active", and a key
// undefined on the active layer falls through to Base (so layers can be sparse). Lookups
// are pre-built into per-layer maps because OnCapturedKey runs on the time-critical decider
// thread and must return fast; real actions run on a thread-pool thread.
public sealed class MacroEngine
{
    private readonly object _gate = new();
    private readonly Action<MacroBinding> _run;

    // Bindings whose macro is still running; a second press while it runs is ignored so
    // two copies of a sequence never interleave.
    private readonly ConcurrentDictionary<MacroBinding, byte> _running = new();

    private string[] _layerNames = Array.Empty<string>();
    private Dictionary<int, MacroBinding>[] _layerMaps = Array.Empty<Dictionary<int, MacroBinding>>();

    private int _activeLayer;       // index into _layerMaps
    private int _holdSourceLayer;   // layer to return to when a held layer key releases
    private int _holdKey;           // key code currently holding a layer (0 = none)

    // Fired (on the decider thread) when the active layer changes, arg = new layer index.
    public event EventHandler<int>? ActiveLayerChanged;

    // run: executes a binding's actions (the thread-pool hop is the engine's job). Tests pass
    // a recorder; the app uses MacroExecutor.Run.
    public MacroEngine(Action<MacroBinding>? run = null) => _run = run ?? MacroExecutor.Run;

    public void SetProfile(MacroProfile profile)
    {
        int layer;
        lock (_gate)
        {
            profile.Normalize();
            _layerNames = profile.Layers.Select(l => l.Name).ToArray();
            _layerMaps = profile.Layers.Select(BuildMap).ToArray();

            // Keep the current layer if it's still in range, so live edits don't yank it.
            if (_activeLayer >= _layerMaps.Length)
                _activeLayer = 0;
            _holdKey = 0;
            layer = _activeLayer;
        }
        ActiveLayerChanged?.Invoke(this, layer);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _layerNames = Array.Empty<string>();
            _layerMaps = Array.Empty<Dictionary<int, MacroBinding>>();
            _activeLayer = 0;
            _holdKey = 0;
        }
        ActiveLayerChanged?.Invoke(this, 0);
    }

    private static Dictionary<int, MacroBinding> BuildMap(MacroLayer layer)
    {
        var map = new Dictionary<int, MacroBinding>();
        foreach (var b in layer.Bindings)
            if (!b.IsEmpty && b.KeyCode != 0)
                map[b.KeyCode] = b;
        return map;
    }

    // Called from the capture backend (decider thread) - must return fast.
    public void OnCapturedKey(DeviceKeyEvent e)
    {
        MacroBinding? toRun = null;
        bool layerChanged = false;
        int newLayer = 0;

        lock (_gate)
        {
            if (_layerMaps.Length == 0)
                return;

            if (!e.IsKeyDown)
            {
                // Releasing the key that engaged a momentary layer returns us to where we were.
                if (_holdKey != 0 && e.KeyCode == _holdKey)
                {
                    _activeLayer = _holdSourceLayer;
                    _holdKey = 0;
                    layerChanged = true;
                    newLayer = _activeLayer;
                }
            }
            else
            {
                var binding = Resolve(e.KeyCode);
                switch (binding?.Action.Kind)
                {
                    case MacroActionKind.LayerHold when !binding!.HasSteps:
                    {
                        if (e.IsRepeat)
                            break;
                        int target = IndexOfLayer(binding.Action.Target);
                        if (target >= 0 && target != _activeLayer)
                        {
                            _holdSourceLayer = _activeLayer;
                            _holdKey = e.KeyCode;
                            _activeLayer = target;
                            layerChanged = true;
                            newLayer = target;
                        }
                        break;
                    }
                    case MacroActionKind.LayerToggle when !binding!.HasSteps:
                    {
                        if (e.IsRepeat)
                            break; // holding a toggle key must not flip layers back and forth
                        int target = IndexOfLayer(binding.Action.Target);
                        if (target >= 0)
                        {
                            _activeLayer = _activeLayer == target ? 0 : target;
                            _holdKey = 0;
                            layerChanged = true;
                            newLayer = _activeLayer;
                        }
                        break;
                    }
                    default:
                        // A normal action or a sequence. Auto-repeat only re-fires it when the
                        // binding asks for that.
                        if (binding is not null && (!e.IsRepeat || binding.RepeatWhileHeld))
                            toRun = binding;
                        break;
                }
            }
        }

        if (layerChanged)
            ActiveLayerChanged?.Invoke(this, newLayer);
        if (toRun is not null && _running.TryAdd(toRun, 0))
        {
            var binding = toRun;
            Task.Run(() =>
            {
                try { _run(binding); }
                finally { _running.TryRemove(binding, out _); }
            });
        }
    }

    // Active layer first; if the key isn't defined there, fall through to Base (transparent).
    private MacroBinding? Resolve(int keyCode)
    {
        if (_layerMaps[_activeLayer].TryGetValue(keyCode, out var b))
            return b;
        if (_activeLayer != 0 && _layerMaps[0].TryGetValue(keyCode, out var baseB))
            return baseB;
        return null;
    }

    private int IndexOfLayer(string name)
    {
        for (int i = 0; i < _layerNames.Length; i++)
            if (string.Equals(_layerNames[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }
}
