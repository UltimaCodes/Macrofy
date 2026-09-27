namespace Macrofy.Core.Input;

// Groups raw keyboard collections into physical devices and gives each a stable id.
//
// Ids: "VID:PID" when only one connected device has that VID/PID, so a keyboard keeps its
// macros whichever USB port it's in. Two identical keyboards can't share that, so while both
// are connected each gets "VID:PID@<container>" instead (and starts from a copy of the shared
// profile, see LegacyIds). Devices with no VID/PID use their first collection path.
public static class DeviceGrouping
{
    public static IReadOnlyList<KeyboardDevice> Group(
        IEnumerable<RawKeyboard> raws, bool includeNonKeyboards, Func<IReadOnlyList<string>, string> resolveName)
    {
        var groups = raws
            .GroupBy(r => r.GroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.ToList())
            .Select(members =>
            {
                var withVid = members.FirstOrDefault(m => m.HasVidPid);
                string? vidPid = withVid is null ? null : $"{withVid.Vid:X4}:{withVid.Pid:X4}";
                return (Members: members, VidPid: vidPid);
            })
            .ToList();

        var vidPidCounts = groups
            .Where(g => g.VidPid is not null)
            .GroupBy(g => g.VidPid!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var devices = new List<KeyboardDevice>();
        foreach (var (members, vidPid) in groups)
        {
            bool isKeyboard = members.Any(m => m.IsLikelyKeyboard);
            if (!includeNonKeyboards && !isKeyboard)
                continue;

            var paths = members.Select(m => m.Path)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string id = vidPid is null ? paths[0]
                : vidPidCounts[vidPid] == 1 ? vidPid
                : $"{vidPid}@{InstanceTag(members, paths)}";

            var legacy = paths.Select(LegacyId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(l => !string.Equals(l, id, StringComparison.OrdinalIgnoreCase))
                .ToList();

            devices.Add(new KeyboardDevice(id, resolveName(paths), paths, isKeyboard, legacy));
        }

        // Two of the same model get the same product name; number them so they can be told apart.
        foreach (var same in devices.GroupBy(d => d.DisplayName).Where(g => g.Count() > 1).ToList())
        {
            int n = 1;
            foreach (var d in same.OrderBy(d => d.Id, StringComparer.Ordinal).ToList())
                devices[devices.IndexOf(d)] = d with { DisplayName = $"{d.DisplayName} ({n++})" };
        }

        return devices
            .OrderByDescending(d => d.IsLikelyKeyboard)
            .ThenBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    // The id older versions gave the device a collection belongs to.
    public static string LegacyId(string path)
        => DeviceNameResolver.TryParseLegacyVidPid(path, out var vid, out var pid) ? $"{vid:X4}:{pid:X4}" : path;

    private static string InstanceTag(List<RawKeyboard> members, List<string> paths)
    {
        var container = members.Select(m => m.ContainerId).FirstOrDefault(c => c is not null);
        if (container is { } c)
            return c.ToString("N")[..8];
        // Stable across runs (unlike string.GetHashCode).
        uint hash = 2166136261;
        foreach (char ch in paths[0].ToUpperInvariant())
            hash = (hash ^ ch) * 16777619;
        return hash.ToString("x8");
    }
}
