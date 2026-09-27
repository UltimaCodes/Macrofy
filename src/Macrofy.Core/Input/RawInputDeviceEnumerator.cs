using System.Runtime.InteropServices;
using System.Text;
using static Macrofy.Core.Input.Interop.NativeMethods;

namespace Macrofy.Core.Input;

// Enumerates keyboard collections via Raw Input and groups them into devices.
internal static class RawInputDeviceEnumerator
{
    private const uint Error = unchecked((uint)-1);

    public static IReadOnlyList<KeyboardDevice> GetKeyboards(bool includeNonKeyboards)
        => DeviceGrouping.Group(EnumerateRaw(), includeNonKeyboards, ResolveName);

    public static IReadOnlyList<RawKeyboard> EnumerateRaw()
    {
        uint count = 0;
        uint structSize = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
        if (GetRawInputDeviceList(null, ref count, structSize) == Error || count == 0)
            return Array.Empty<RawKeyboard>();

        var list = new RAWINPUTDEVICELIST[count];
        uint written = GetRawInputDeviceList(list, ref count, structSize);
        if (written == Error)
            return Array.Empty<RawKeyboard>();

        var result = new List<RawKeyboard>();
        for (int i = 0; i < written; i++)
        {
            var d = list[i];
            if (d.dwType != RIM_TYPEKEYBOARD)
                continue;

            string? path = GetDeviceName(d.hDevice);
            if (string.IsNullOrEmpty(path))
                continue;

            bool hasVidPid = DeviceNameResolver.TryParseVidPid(path, out var vid, out var pid);
            result.Add(new RawKeyboard(
                d.hDevice, path, hasVidPid, vid, pid,
                GetKeyboardKeyCount(d.hDevice), DeviceNameResolver.IsVirtual(path),
                TryGetContainerId(path)));
        }
        return result;
    }

    // Prefer the device's real HID product string; fall back to VID:PID.
    private static string ResolveName(IReadOnlyList<string> paths)
    {
        foreach (var p in paths)
        {
            var product = HidProductName.TryGet(p);
            if (!string.IsNullOrEmpty(product))
                return product;
        }
        return DeviceNameResolver.ResolveGroup(paths[0]);
    }

    // Resolve the stable device path for a Raw Input handle.
    public static string? GetDeviceName(nint hDevice)
    {
        uint charCount = 0;
        // null buffer reports the required length in chars
        if (GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, nint.Zero, ref charCount) != 0
            || charCount == 0)
            return null;

        nint buffer = Marshal.AllocHGlobal((int)charCount * sizeof(char));
        try
        {
            if (GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, buffer, ref charCount) == Error)
                return null;
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int GetKeyboardKeyCount(nint hDevice)
    {
        uint size = (uint)Marshal.SizeOf<RID_DEVICE_INFO_KEYBOARD>();
        nint buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            Marshal.StructureToPtr(new RID_DEVICE_INFO_KEYBOARD { cbSize = size }, buffer, false);
            uint result = GetRawInputDeviceInfo(hDevice, RIDI_DEVICEINFO, buffer, ref size);
            if (result is 0 or Error)
                return 0;

            var info = Marshal.PtrToStructure<RID_DEVICE_INFO_KEYBOARD>(buffer);
            return info.dwType == RIM_TYPEKEYBOARD ? (int)info.dwNumberOfKeysTotal : 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // The container id Windows gives one physical device (all its functions share it).
    private static Guid? TryGetContainerId(string interfacePath)
    {
        try
        {
            string? instanceId = GetInstanceId(interfacePath);
            if (instanceId is null || CM_Locate_DevNodeW(out uint devInst, instanceId, 0) != CR_SUCCESS)
                return null;
            var key = DEVPKEY_Device_ContainerId;
            var buffer = new byte[16];
            uint size = (uint)buffer.Length;
            if (CM_Get_DevNode_PropertyW(devInst, ref key, out uint type, buffer, ref size, 0) != CR_SUCCESS
                || type != DEVPROP_TYPE_GUID || size != 16)
                return null;
            return new Guid(buffer);
        }
        catch { return null; }
    }

    private static string? GetInstanceId(string interfacePath)
    {
        var key = DEVPKEY_Device_InstanceId;
        uint size = 0;
        if (CM_Get_Device_Interface_PropertyW(interfacePath, ref key, out _, null, ref size, 0) == CR_BUFFER_SMALL && size > 0)
        {
            var buffer = new byte[size];
            if (CM_Get_Device_Interface_PropertyW(interfacePath, ref key, out _, buffer, ref size, 0) == CR_SUCCESS)
                return Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0');
        }
        return null;
    }
}
