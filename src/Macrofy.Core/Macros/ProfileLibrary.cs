using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Macrofy.Core.IO;

namespace Macrofy.Core.Macros;

// The saved profiles (one JSON file each in <data>\library) and which keyboard uses which
// (<data>\assignments.json, device id -> profile id). Any keyboard can use any profile, and
// two keyboards can share one.
//
// Older versions kept exactly one profile per keyboard in <data>\profiles\<device>.json.
// Those are moved into the library on first load (named after the keyboard and assigned to
// it); the originals are renamed to *.migrated rather than deleted.
public sealed class ProfileLibrary
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _libraryDir;
    private readonly string _legacyDir;
    private readonly string _assignmentsPath;
    private readonly Dictionary<string, MacroProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _assignments = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _setAside = new();
    private readonly Func<string, string?>? _nameForDevice;

    public ProfileLibrary() : this(AppPaths.DataDir)
    {
    }

    // nameForDevice: the name the user gave a keyboard (by device id), if any. Profiles moved
    // over from older versions are named after it.
    public ProfileLibrary(string dataDir, Func<string, string?>? nameForDevice = null)
    {
        _nameForDevice = nameForDevice;
        _libraryDir = Path.Combine(dataDir, "library");
        _legacyDir = Path.Combine(dataDir, "profiles");
        _assignmentsPath = Path.Combine(dataDir, "assignments.json");
        Directory.CreateDirectory(_libraryDir);
        Load();
    }

    // Something in the library changed (a profile added, removed, renamed or edited, or a
    // keyboard switched profiles). Raised on the caller's thread.
    public event EventHandler? Changed;

    // Files that couldn't be read and were moved aside (renamed *.unreadable-*.bak) during
    // loading, so the app can say so once instead of silently losing them.
    public IReadOnlyList<string> SetAsideFiles => _setAside;

    public IReadOnlyList<MacroProfile> Profiles => _profiles.Values
        .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    public MacroProfile? Get(string? id)
        => id is not null && _profiles.TryGetValue(id, out var p) ? p : null;

    // ---- create / edit / delete ----

    public MacroProfile Create(string name, string description = "")
    {
        var profile = new MacroProfile
        {
            Id = NewId(),
            Name = UniqueName(name),
            Description = description,
        };
        profile.Normalize();
        Store(profile);
        return profile;
    }

    public MacroProfile Duplicate(MacroProfile source)
    {
        var copy = source.CloneAs(NewId(), UniqueName(source.Name + " (copy)"));
        copy.Normalize();
        Store(copy);
        return copy;
    }

    public MacroProfile CreateFromTemplate(ProfileTemplate template)
    {
        var built = template.Build();
        var profile = built.CloneAs(NewId(), UniqueName(template.Name));
        profile.TemplateId = template.Id;
        profile.Normalize();
        Store(profile);
        return profile;
    }

    // Saves an edited profile. A profile that was never saved (empty Id) gets an id and a
    // unique name here, so keyboards can start with an unsaved profile and only add it to
    // the library once something is actually bound.
    public void Save(MacroProfile profile)
    {
        if (string.IsNullOrEmpty(profile.Id))
        {
            profile.Id = NewId();
            profile.Name = UniqueName(string.IsNullOrWhiteSpace(profile.Name) ? "My profile" : profile.Name);
        }
        Store(profile);
    }

    public void Rename(MacroProfile profile, string newName)
    {
        newName = (newName ?? string.Empty).Trim();
        if (newName.Length == 0 || string.Equals(profile.Name, newName, StringComparison.Ordinal))
            return;
        profile.Name = UniqueName(newName, profile.Id);
        if (!string.IsNullOrEmpty(profile.Id))
            Store(profile);
    }

    public void Delete(string id)
    {
        if (!_profiles.Remove(id))
            return;
        TryDelete(FileFor(id));
        foreach (var device in _assignments.Where(a => string.Equals(a.Value, id, StringComparison.OrdinalIgnoreCase))
                                           .Select(a => a.Key).ToList())
            _assignments.Remove(device);
        SaveAssignments();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Removes every saved profile and every keyboard's assignment ("Delete all profiles").
    public void DeleteAll()
    {
        foreach (var id in _profiles.Keys.ToList())
            TryDelete(FileFor(id));
        _profiles.Clear();
        _assignments.Clear();
        SaveAssignments();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // A name not already taken by another profile: "Name", then "Name (2)", "Name (3)"...
    public string UniqueName(string desired, string? exceptId = null)
    {
        desired = string.IsNullOrWhiteSpace(desired) ? "My profile" : desired.Trim();
        bool Taken(string n) => _profiles.Values.Any(p =>
            !string.Equals(p.Id, exceptId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.Name, n, StringComparison.CurrentCultureIgnoreCase));
        if (!Taken(desired))
            return desired;
        for (int n = 2; ; n++)
        {
            string candidate = $"{desired} ({n})";
            if (!Taken(candidate))
                return candidate;
        }
    }

    // ---- keyboards ----

    public string? AssignedProfileId(string deviceId)
        => _assignments.TryGetValue(deviceId, out var id) && _profiles.ContainsKey(id) ? id : null;

    public void Assign(string deviceId, string profileId)
    {
        if (string.IsNullOrEmpty(profileId) || !_profiles.ContainsKey(profileId))
            return;
        if (_assignments.TryGetValue(deviceId, out var current)
            && string.Equals(current, profileId, StringComparison.OrdinalIgnoreCase))
            return;
        _assignments[deviceId] = profileId;
        SaveAssignments();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<string> DevicesUsing(string profileId) => _assignments
        .Where(a => string.Equals(a.Value, profileId, StringComparison.OrdinalIgnoreCase))
        .Select(a => a.Key)
        .ToList();

    // The profile a keyboard uses. Looks under its current id, then any id older versions gave
    // it (and moves that assignment over). A keyboard with no profile gets a new, unsaved one
    // named after it; it joins the library the first time it's saved.
    public MacroProfile ForDevice(string deviceId, string deviceName, IEnumerable<string>? legacyIds = null)
    {
        if (Get(AssignedProfileId(deviceId)) is { } assigned)
            return assigned;

        foreach (var legacy in legacyIds ?? Enumerable.Empty<string>())
        {
            if (Get(AssignedProfileId(legacy)) is { } old)
            {
                _assignments[deviceId] = old.Id;
                SaveAssignments();
                return old;
            }
        }

        var fresh = new MacroProfile { Name = deviceName };
        fresh.Normalize();
        return fresh;
    }

    // ---- import / export ----

    // Adds a profile from a file (an export from this or an older version). Returns null if the
    // file isn't a Macrofy profile.
    public MacroProfile? Import(string path)
    {
        MacroProfile? imported;
        try
        {
            imported = JsonSerializer.Deserialize<MacroProfile>(File.ReadAllText(path), Options);
        }
        catch
        {
            return null;
        }
        if (imported is null)
            return null;

        imported.Normalize();
        string name = !string.IsNullOrWhiteSpace(imported.Name) ? imported.Name
            : !string.IsNullOrWhiteSpace(imported.DeviceName) ? imported.DeviceName!
            : Path.GetFileNameWithoutExtension(path).Replace(".macrofy", string.Empty);
        imported.Id = NewId();
        imported.Name = UniqueName(name);
        imported.DeviceId = null;
        imported.DeviceName = null;
        Store(imported);
        return imported;
    }

    public void Export(MacroProfile profile, string path)
    {
        var copy = profile.CloneAs(profile.Id, profile.Name);
        copy.UpdatedUtc = profile.UpdatedUtc;
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(copy, Options));
    }

    // ---- storage ----

    private void Store(MacroProfile profile)
    {
        profile.UpdatedUtc = DateTime.UtcNow;
        profile.DeviceId = null;
        profile.DeviceName = null;
        _profiles[profile.Id] = profile;
        AtomicFile.WriteAllText(FileFor(profile.Id), JsonSerializer.Serialize(profile, Options));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Load()
    {
        foreach (var file in Directory.GetFiles(_libraryDir, "*.json"))
        {
            var profile = ReadOrSetAside(file);
            if (profile is null)
                continue;
            // The file name is the identity; an id edited inside the file doesn't change it.
            profile.Id = Path.GetFileNameWithoutExtension(file);
            _profiles[profile.Id] = profile;
        }

        try
        {
            if (File.Exists(_assignmentsPath)
                && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_assignmentsPath)) is { } saved)
                foreach (var (device, id) in saved)
                    _assignments[device] = id;
        }
        catch { /* a damaged assignments file just means keyboards pick their profile again */ }

        MigrateLegacyProfiles();
    }

    private void MigrateLegacyProfiles()
    {
        if (!Directory.Exists(_legacyDir))
            return;
        bool changed = false;
        foreach (var file in Directory.GetFiles(_legacyDir, "*.json"))
        {
            var old = ReadOrSetAside(file);
            if (old is null)
                continue;
            string deviceId = old.DeviceId ?? Path.GetFileNameWithoutExtension(file);
            // A keyboard that never had a macro doesn't need an (empty) library entry.
            if (old.KeyCount > 0 || old.Layers.Count > 1)
            {
                string name = _nameForDevice?.Invoke(deviceId) is { Length: > 0 } custom ? custom
                    : !string.IsNullOrWhiteSpace(old.DeviceName) ? old.DeviceName!
                    : $"Keyboard {deviceId}";
                var profile = old.CloneAs(NewId(), UniqueName(name));
                profile.UpdatedUtc = DateTime.UtcNow;
                _profiles[profile.Id] = profile;
                AtomicFile.WriteAllText(FileFor(profile.Id), JsonSerializer.Serialize(profile, Options));
                _assignments.TryAdd(deviceId, profile.Id);
                changed = true;
            }
            try { File.Move(file, file + ".migrated", overwrite: true); } catch { /* retried next launch */ }
        }
        if (changed)
            SaveAssignments();
    }

    private MacroProfile? ReadOrSetAside(string file)
    {
        try
        {
            var profile = JsonSerializer.Deserialize<MacroProfile>(File.ReadAllText(file), Options)
                ?? throw new JsonException("empty profile");
            profile.Normalize();
            return profile;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // locked right now; leave it alone and try again next launch
        }
        catch
        {
            try
            {
                string backup = $"{file}.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}.bak";
                File.Move(file, backup);
                _setAside.Add(backup);
            }
            catch { /* best effort */ }
            return null;
        }
    }

    private void SaveAssignments()
    {
        try { AtomicFile.WriteAllText(_assignmentsPath, JsonSerializer.Serialize(_assignments, Options)); }
        catch { /* best effort */ }
    }

    private string FileFor(string id) => Path.Combine(_libraryDir, id + ".json");

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }
}
