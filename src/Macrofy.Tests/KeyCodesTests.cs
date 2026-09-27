using Macrofy.Core.Input;
using Xunit;

namespace Macrofy.Tests;

public class KeyCodesTests
{
    private const ushort RI_KEY_E0 = 0x02;
    private const ushort RI_KEY_E1 = 0x04;

    [Theory]
    [InlineData(0x24)] // Home
    [InlineData(0x23)] // End
    [InlineData(0x21)] // Page Up
    [InlineData(0x22)] // Page Down
    [InlineData(0x2D)] // Insert
    [InlineData(0x2E)] // Delete
    [InlineData(0x25)] // Left
    [InlineData(0x26)] // Up
    [InlineData(0x27)] // Right
    [InlineData(0x28)] // Down
    public void Navigation_keys_are_extended(int vk)
    {
        int code = KeyCodes.FromVk(vk);
        Assert.True(KeyCodes.IsExtended(code));
        Assert.Equal(vk, KeyCodes.ToVk(code));
    }

    [Theory]
    [InlineData(0x67, 0x47)] // Numpad 7 -> its scan code
    [InlineData(0x60, 0x52)] // Numpad 0
    [InlineData(0x6E, 0x53)] // Numpad .
    public void Numpad_keys_map_to_their_scan_codes_and_back(int vk, int expectedScan)
    {
        int code = KeyCodes.FromVk(vk);
        Assert.Equal(expectedScan, code);
        Assert.False(KeyCodes.IsExtended(code));
        Assert.Equal(vk, KeyCodes.ToVk(code));
    }

    [Fact]
    public void Numpad_enter_is_distinct_from_return()
    {
        Assert.Equal(0x0D, KeyCodes.ToVk(KeyCodes.NumpadEnter));
        Assert.True(KeyCodes.IsExtended(KeyCodes.NumpadEnter));
        Assert.NotEqual(KeyCodes.NumpadEnter, KeyCodes.FromVk(0x0D)); // main Enter is not extended
    }

    [Fact]
    public void NumLock_uses_the_dedicated_scan_code_not_the_pause_prefix()
    {
        int code = KeyCodes.FromVk(0x90);
        Assert.Equal(0x45, code);
        Assert.False(KeyCodes.IsExtended(code));
    }

    [Fact]
    public void Letters_round_trip_through_scan_codes()
    {
        int a = KeyCodes.FromVk(0x41);
        Assert.False(KeyCodes.IsExtended(a));
        Assert.Equal(0x41, KeyCodes.ToVk(a));
    }

    [Fact]
    public void FromRaw_applies_the_E0_prefix()
    {
        Assert.Equal(0xE047, KeyCodes.FromRaw(0x47, RI_KEY_E0, 0x24)); // extended Home
        Assert.Equal(0x47, KeyCodes.FromRaw(0x47, 0, 0x67));           // numpad 7, no prefix
    }

    [Fact]
    public void FromRaw_applies_the_E1_prefix()
        => Assert.Equal(0xE11D, KeyCodes.FromRaw(0x1D, RI_KEY_E1, 0x13)); // Pause

    [Fact]
    public void FromRaw_falls_back_to_the_vk_when_there_is_no_scan_code()
    {
        int code = KeyCodes.FromRaw(0, 0, 0xAF); // volume up, HID-only, no make code
        Assert.Equal(KeyCodes.VkFlag | 0xAF, code);
        Assert.Equal(0xAF, KeyCodes.ToVk(code));
    }
}
