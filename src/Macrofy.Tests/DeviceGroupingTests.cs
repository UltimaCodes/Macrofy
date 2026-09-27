using Macrofy.Core.Input;
using Xunit;

namespace Macrofy.Tests;

public class DeviceGroupingTests
{
    private static string Name(IReadOnlyList<string> paths) => "Test Keyboard";

    private static RawKeyboard Kb(string path, ushort vid, ushort pid, Guid? container = null, int keys = 104)
        => new(Handle: 1, Path: path, HasVidPid: true, Vid: vid, Pid: pid, KeysTotal: keys, IsVirtual: false, ContainerId: container);

    [Fact]
    public void A_single_device_gets_a_clean_vid_pid_id()
    {
        var raws = new[] { Kb(@"\\?\HID#VID_046D&PID_C31C&MI_00#a", 0x046D, 0xC31C) };
        var devices = DeviceGrouping.Group(raws, includeNonKeyboards: false, Name);
        Assert.Single(devices);
        Assert.Equal("046D:C31C", devices[0].Id);
    }

    [Fact]
    public void Collections_of_one_device_are_grouped_by_container_id()
    {
        var container = Guid.NewGuid();
        var raws = new[]
        {
            Kb(@"\\?\HID#VID_046D&PID_C31C&MI_00#a", 0x046D, 0xC31C, container),
            Kb(@"\\?\HID#VID_046D&PID_C31C&MI_01#b", 0x046D, 0xC31C, container),
        };
        var devices = DeviceGrouping.Group(raws, includeNonKeyboards: false, Name);
        Assert.Single(devices);
        Assert.Equal(2, devices[0].CollectionCount);
    }

    [Fact]
    public void Two_identical_keyboards_are_kept_apart_by_container_id()
    {
        var raws = new[]
        {
            Kb(@"\\?\HID#VID_046D&PID_C31C&MI_00#a", 0x046D, 0xC31C, Guid.NewGuid()),
            Kb(@"\\?\HID#VID_046D&PID_C31C&MI_00#b", 0x046D, 0xC31C, Guid.NewGuid()),
        };
        var devices = DeviceGrouping.Group(raws, includeNonKeyboards: false, Name);

        Assert.Equal(2, devices.Count);
        Assert.NotEqual(devices[0].Id, devices[1].Id);
        Assert.All(devices, d => Assert.StartsWith("046D:C31C@", d.Id));
        // Each remembers the shared old id so existing macros can be carried over.
        Assert.All(devices, d => Assert.Contains("046D:C31C", d.LegacyIds));
        // Same product name, so they're numbered to tell them apart.
        Assert.NotEqual(devices[0].DisplayName, devices[1].DisplayName);
    }

    [Fact]
    public void Non_keyboards_are_hidden_unless_requested()
    {
        var raws = new[] { Kb(@"\\?\HID#VID_1234&PID_5678&MI_02#x", 0x1234, 0x5678, keys: 0) };
        Assert.Empty(DeviceGrouping.Group(raws, includeNonKeyboards: false, Name));
        Assert.Single(DeviceGrouping.Group(raws, includeNonKeyboards: true, Name));
    }

    [Fact]
    public void A_device_without_vid_pid_falls_back_to_its_path()
    {
        var raws = new[]
        {
            new RawKeyboard(1, @"\\?\ACPI#PNP0303#4&abc", HasVidPid: false, 0, 0, KeysTotal: 104, IsVirtual: false),
        };
        var devices = DeviceGrouping.Group(raws, includeNonKeyboards: false, Name);
        Assert.Single(devices);
        Assert.Equal(@"\\?\ACPI#PNP0303#4&abc", devices[0].Id);
    }
}
