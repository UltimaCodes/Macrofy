using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Macrofy.Core.Input;
using Macrofy.Core.IO;

namespace Macrofy.App;

// The chosen on-screen layout for a device: a preset, or "Custom" with the exact set of keys
// learned from the device.
public sealed class DeviceLayout
{
    public const int CurrentVersion = 2;

    public KeyboardLayoutKind Kind { get; set; } = KeyboardLayoutKind.Full;
    public List<int> Keys { get; set; } = new();   // learned keys (only used for Custom)
    public bool IsIso { get; set; }                // ISO shape (tall Enter, extra key by left Shift)

    // 1 (or missing): Keys are virtual keys. 2: Keys are physical key codes (KeyCodes).
    public int Version { get; set; } = CurrentVersion;

    // Learned layouts from before physical-key codes stored virtual keys, with Shift/Ctrl/Alt
    // as the generic codes that don't say which side. A keyboard that has one side almost
    // always has both, so those become both.
    public void Migrate()
    {
        Keys ??= new List<int>();
        if (Version >= CurrentVersion)
            return;
        var codes = new SortedSet<int>();
        foreach (int vk in Keys)
        {
            switch (vk)
            {
                case 0x10: codes.Add(0x2A); codes.Add(0x36); break;
                case 0x11: codes.Add(0x1D); codes.Add(0xE01D); break;
                case 0x12: codes.Add(0x38); codes.Add(0xE038); break;
                default: codes.Add(KeyCodes.FromVk(vk)); break;
            }
        }
        Keys = codes.ToList();
        Version = CurrentVersion;
    }
}

// Persists per-device layouts to %AppData%/Macrofy/layouts.json (device id -> layout).
public sealed class DeviceLayoutStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly Dictionary<string, DeviceLayout> _map;

    public DeviceLayoutStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Macrofy");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "layouts.json");
        _map = Load();
    }

    public bool Contains(string id) => _map.ContainsKey(id);

    public DeviceLayout Get(string id) => _map.TryGetValue(id, out var v) ? v : new DeviceLayout();

    public void Set(string id, DeviceLayout layout)
    {
        _map[id] = layout;
        Save();
    }

    public void CopyIfMissing(string fromId, string toId)
    {
        if (_map.TryGetValue(fromId, out var layout) && !_map.ContainsKey(toId))
            Set(toId, new DeviceLayout { Kind = layout.Kind, Keys = new List<int>(layout.Keys), IsIso = layout.IsIso });
    }

    private Dictionary<string, DeviceLayout> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                // Files written before versioning have no Version field; treat them as 1.
                using var doc = JsonDocument.Parse(File.ReadAllText(_path));
                var map = new Dictionary<string, DeviceLayout>();
                foreach (var entry in doc.RootElement.EnumerateObject())
                {
                    var layout = entry.Value.Deserialize<DeviceLayout>(Options) ?? new DeviceLayout();
                    if (!entry.Value.TryGetProperty(nameof(DeviceLayout.Version), out _))
                        layout.Version = 1;
                    layout.Migrate();
                    map[entry.Name] = layout;
                }
                return map;
            }
        }
        catch { /* corrupt file -> start fresh */ }
        return new();
    }

    private void Save()
    {
        try { AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(_map, Options)); }
        catch { /* best effort */ }
    }
}
