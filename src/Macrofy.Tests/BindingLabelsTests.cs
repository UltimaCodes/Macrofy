using Macrofy.Core.Input;
using Macrofy.Core.Macros;
using Xunit;

namespace Macrofy.Tests;

public class BindingLabelsTests
{
    private static string Label(MacroActionKind kind, string target)
        => BindingLabels.For(new MacroBinding { KeyCode = 1, Action = new MacroAction { Kind = kind, Target = target } });

    [Fact]
    public void The_users_label_wins()
    {
        var b = new MacroBinding { Label = " Record ", Action = new MacroAction { Kind = MacroActionKind.SendHotkey, Target = "Ctrl+R" } };
        Assert.Equal("Record", BindingLabels.For(b));
    }

    [Theory]
    [InlineData(MacroActionKind.LaunchApp, @"""C:\Program Files\Spotify\Spotify.exe""", "Spotify")]
    [InlineData(MacroActionKind.LaunchApp, @"C:\Users\me\Desktop\Notes.lnk", "Notes")]
    [InlineData(MacroActionKind.OpenUrl, "https://www.youtube.com/watch?v=1", "youtube.com")]
    [InlineData(MacroActionKind.OpenUrl, "whatsapp://send?phone=123", "Whatsapp")]
    [InlineData(MacroActionKind.OpenUrl, "ms-settings:bluetooth", "Ms-settings")]
    [InlineData(MacroActionKind.SendHotkey, "Ctrl+Shift+Esc", "Ctrl+Shift+Esc")]
    [InlineData(MacroActionKind.MediaKey, "VolumeUp", "Vol +")]
    [InlineData(MacroActionKind.MediaKey, "PlayPause", "Play/Pause")]
    [InlineData(MacroActionKind.RunCommand, "shutdown /s /t 0", "shutdown")]
    [InlineData(MacroActionKind.LayerToggle, "Gaming", "Gaming")]
    public void Labels_are_made_from_the_action(MacroActionKind kind, string target, string expected)
        => Assert.Equal(expected, Label(kind, target));

    [Fact]
    public void Long_text_is_shortened_onto_one_line()
    {
        string label = Label(MacroActionKind.TypeText, "Best regards,\r\nthe Macrofy team");
        Assert.DoesNotContain("\n", label);
        Assert.EndsWith("…", label);
        Assert.True(label.Length <= 14);
    }

    [Fact]
    public void Sequences_say_how_many_steps()
    {
        var b = new MacroBinding
        {
            Steps =
            {
                new MacroStep { Action = new MacroAction { Kind = MacroActionKind.SendHotkey, Target = "Ctrl+K" } },
                new MacroStep { Action = new MacroAction { Kind = MacroActionKind.SendHotkey, Target = "S" } },
            },
        };
        Assert.Equal("2 steps", BindingLabels.For(b));
    }

    [Theory]
    [InlineData(@"\\?\HID#VID_046D&PID_C31C&MI_00#7&abc#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}", ConnectionKind.Usb)]
    [InlineData(@"\\?\HID#{00001124-0000-1000-8000-00805f9b34fb}_VID&0002046d_PID&b35b&Col01#9&x#{884b96c3}", ConnectionKind.Bluetooth)]
    [InlineData(@"\\?\HID#{00001812-0000-1000-8000-00805f9b34fb}_Dev_VID&02046d_PID&b35b#a&y#{884b96c3}", ConnectionKind.Bluetooth)]
    [InlineData(@"\\?\ACPI#PNP0303#4&abc#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}", ConnectionKind.BuiltIn)]
    [InlineData(@"\\?\Root#RDP_KBD#0000#{884b96c3}", ConnectionKind.Virtual)]
    public void Connection_type_comes_from_the_device_path(string path, ConnectionKind expected)
        => Assert.Equal(expected, DeviceNameResolver.Connection(path));
}
