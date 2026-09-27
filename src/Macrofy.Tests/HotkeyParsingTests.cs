using Macrofy.Core.Macros;
using Xunit;

namespace Macrofy.Tests;

public class HotkeyParsingTests
{
    [Theory]
    [InlineData("A", 0x41)]
    [InlineData("z", 0x5A)]
    [InlineData("5", 0x35)]
    [InlineData("F5", 0x74)]
    [InlineData("F12", 0x7B)]
    [InlineData("Enter", 0x0D)]
    [InlineData("Esc", 0x1B)]
    [InlineData("Space", 0x20)]
    [InlineData("PageUp", 0x21)]
    [InlineData("PageDown", 0x22)]
    [InlineData("Insert", 0x2D)]
    [InlineData("Delete", 0x2E)]
    [InlineData("Home", 0x24)]
    [InlineData("Num5", 0x65)]
    [InlineData("numadd", 0x6B)]
    [InlineData("PrintScreen", 0x2C)]
    [InlineData("PlayPause", 0xB3)]
    public void Known_names_map_to_their_virtual_keys(string name, int expected)
        => Assert.Equal(expected, MacroExecutor.KeyNameToVk(name));

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("F25")]
    public void Unknown_names_map_to_zero(string name)
        => Assert.Equal(0, MacroExecutor.KeyNameToVk(name));
}
