using Macrofy.Core.Input;
using Xunit;

namespace Macrofy.Tests;

public class DeviceNameResolverTests
{
    [Fact]
    public void Parses_a_usb_path()
    {
        Assert.True(DeviceNameResolver.TryParseVidPid(@"\\?\HID#VID_046D&PID_C31C&MI_00#a", out var vid, out var pid));
        Assert.Equal(0x046D, vid);
        Assert.Equal(0xC31C, pid);
    }

    [Fact]
    public void Parses_a_bluetooth_path_taking_the_last_four_vid_digits()
    {
        Assert.True(DeviceNameResolver.TryParseVidPid(@"\\?\HID#{...}_VID&0002046D_PID&B35B#c", out var vid, out var pid));
        Assert.Equal(0x046D, vid);
        Assert.Equal(0xB35B, pid);
    }

    [Fact]
    public void Legacy_parser_only_understands_the_usb_form()
    {
        Assert.True(DeviceNameResolver.TryParseLegacyVidPid(@"\\?\HID#VID_046D&PID_C31C#a", out var vid, out _));
        Assert.Equal(0x046D, vid);
        Assert.False(DeviceNameResolver.TryParseLegacyVidPid(@"\\?\HID#VID&0002046D_PID&B35B#c", out _, out _));
    }

    [Fact]
    public void Detects_virtual_devices()
    {
        Assert.True(DeviceNameResolver.IsVirtual(@"\\?\Root#RDP_KBD#0000#x"));
        Assert.False(DeviceNameResolver.IsVirtual(@"\\?\HID#VID_046D&PID_C31C#a"));
    }
}
