namespace Macrofy.Core.Input;

public enum ConnectionKind { Unknown, Usb, Bluetooth, BuiltIn, Virtual }

// One physical keyboard, grouping all of its HID collections. DevicePaths are the
// stable per-collection identities used for capture; IsLikelyKeyboard is the
// heuristic that hides devices only misclassified as keyboards. LegacyIds are the ids
// older versions gave this device, so its saved names, layouts and macros can be found.
public sealed record KeyboardDevice(
    string Id,
    string DisplayName,
    IReadOnlyList<string> DevicePaths,
    bool IsLikelyKeyboard,
    IReadOnlyList<string> LegacyIds)
{
    public int CollectionCount => DevicePaths.Count;

    public ConnectionKind Connection => DevicePaths.Count == 0
        ? ConnectionKind.Unknown
        : DeviceNameResolver.Connection(DevicePaths[0]);
}
