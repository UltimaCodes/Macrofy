namespace Macrofy.Core.Input;

// One raw HID keyboard collection as reported by Raw Input (before grouping).
public sealed record RawKeyboard(
    nint Handle,
    string Path,
    bool HasVidPid,
    ushort Vid,
    ushort Pid,
    int KeysTotal,
    bool IsVirtual,
    Guid? ContainerId = null)
{
    public bool IsLikelyKeyboard => !IsVirtual && KeysTotal > 0;

    // Collections of one physical device share a container id, which also keeps two
    // identical keyboards apart (they share a VID/PID). Fall back to VID/PID, then the path.
    public string GroupKey => ContainerId is { } c ? c.ToString("N")
        : HasVidPid ? $"{Vid:X4}:{Pid:X4}"
        : Path;
}
