using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Macrofy.Core.IO;

namespace Macrofy.Core.Macros;

// Persists one macro profile per device to %AppData%/Macrofy/profiles/<deviceId>.json.
public sealed class MacroProfileStore
{
    private readonly string _dir;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public MacroProfileStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Macrofy", "profiles"))
    {
    }

    public MacroProfileStore(string directory)
    {
        _dir = directory;
        Directory.CreateDirectory(_dir);
    }

    private string PathFor(string deviceId)
    {
        // Device ids are "VID:PID" or a raw path; keep the file name filesystem-safe.
        var safe = string.Concat(deviceId.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(_dir, safe + ".json");
    }

    public bool Exists(string deviceId) => File.Exists(PathFor(deviceId));

    // Loads a device's profile. A file that can't be read (damaged, or written by a newer
    // Macrofy) is moved aside rather than silently replaced, and its backup path comes back
    // in recoveredBackup so the user can be told.
    public MacroProfile Load(string deviceId, string deviceName, out string? recoveredBackup)
    {
        recoveredBackup = null;
        var path = PathFor(deviceId);
        if (File.Exists(path))
        {
            try
            {
                var profile = JsonSerializer.Deserialize<MacroProfile>(File.ReadAllText(path), Options)
                    ?? throw new JsonException("empty profile");
                profile.DeviceId = deviceId;
                profile.Normalize(); // migrate older files, repair nulls
                return profile;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Couldn't read it right now (locked?). Don't touch the file; start empty for
                // this session.
            }
            catch (Exception)
            {
                recoveredBackup = BackUpDamaged(path);
            }
        }
        var fresh = new MacroProfile { DeviceId = deviceId, DeviceName = deviceName };
        fresh.Normalize();
        return fresh;
    }

    private static string? BackUpDamaged(string path)
    {
        try
        {
            string backup = Path.Combine(Path.GetDirectoryName(path)!,
                $"{Path.GetFileNameWithoutExtension(path)}.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}.json.bak");
            File.Move(path, backup);
            return backup;
        }
        catch { return null; }
    }

    public void Save(MacroProfile profile)
        => AtomicFile.WriteAllText(PathFor(profile.DeviceId), JsonSerializer.Serialize(profile, Options));

    // Copy one device's profile to another id, unless the destination already has one.
    public void CopyIfMissing(string fromId, string toId)
    {
        try
        {
            string from = PathFor(fromId), to = PathFor(toId);
            if (File.Exists(from) && !File.Exists(to))
                File.Copy(from, to);
        }
        catch { /* best effort */ }
    }

    // Write a profile to an arbitrary path (export / share / back up).
    public void Export(MacroProfile profile, string path)
        => AtomicFile.WriteAllText(path, JsonSerializer.Serialize(profile, Options));

    // Delete every saved profile (Reset all macros). Best effort.
    public void DeleteAll()
    {
        try
        {
            foreach (var f in Directory.GetFiles(_dir, "*.json"))
                File.Delete(f);
        }
        catch { /* best effort */ }
    }

    // Read a profile from an arbitrary path. Returns null if the file isn't a valid profile.
    public MacroProfile? Import(string path)
    {
        try
        {
            var profile = JsonSerializer.Deserialize<MacroProfile>(File.ReadAllText(path), Options);
            profile?.Normalize();
            return profile;
        }
        catch { return null; }
    }
}
