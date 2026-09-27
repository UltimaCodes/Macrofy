using Macrofy.Core.Input;
using Macrofy.Core.Macros;
using Xunit;

namespace Macrofy.Tests;

public class MacroModelsTests
{
    [Fact]
    public void Normalize_creates_a_base_layer_when_there_are_none()
    {
        var p = new MacroProfile();
        p.Normalize();
        Assert.Single(p.Layers);
        Assert.Equal("Base", p.Layers[0].Name);
    }

    [Fact]
    public void Normalize_migrates_legacy_top_level_bindings_into_base()
    {
        var p = new MacroProfile
        {
            LegacyBindings = new List<MacroBinding>
            {
                new() { VirtualKey = 0x41, Action = new MacroAction { Kind = MacroActionKind.TypeText, Target = "x" } },
            },
        };
        p.Normalize();
        Assert.Single(p.Layers);
        Assert.Single(p.BaseLayer.Bindings);
        Assert.Null(p.LegacyBindings);
    }

    [Fact]
    public void Normalize_fills_key_codes_from_virtual_keys_on_old_profiles()
    {
        var p = LayerWith(new MacroBinding
        {
            VirtualKey = 0x41,
            Action = new MacroAction { Kind = MacroActionKind.TypeText, Target = "x" },
        });
        p.Normalize();
        Assert.Equal(KeyCodes.FromVk(0x41), p.BaseLayer.Bindings[0].KeyCode);
    }

    [Fact]
    public void Normalize_remaps_generic_modifier_bindings_to_the_left_side()
    {
        var p = LayerWith(new MacroBinding
        {
            VirtualKey = 0x10, // generic Shift, how old builds saved it
            Action = new MacroAction { Kind = MacroActionKind.TypeText, Target = "x" },
        });
        p.Normalize();
        var b = p.BaseLayer.Bindings[0];
        Assert.Equal(0xA0, b.VirtualKey);                 // Left Shift
        Assert.Equal(KeyCodes.FromVk(0xA0), b.KeyCode);
    }

    [Fact]
    public void Normalize_drops_a_binding_with_no_key()
    {
        var p = LayerWith(new MacroBinding
        {
            VirtualKey = 0,
            KeyCode = 0,
            Action = new MacroAction { Kind = MacroActionKind.TypeText, Target = "x" },
        });
        p.Normalize();
        Assert.Empty(p.BaseLayer.Bindings);
    }

    [Fact]
    public void Normalize_deduplicates_two_bindings_on_the_same_physical_key()
    {
        var p = LayerWith(
            new MacroBinding { KeyCode = 0x1E, Action = new MacroAction { Kind = MacroActionKind.TypeText, Target = "a" } },
            new MacroBinding { KeyCode = 0x1E, Action = new MacroAction { Kind = MacroActionKind.TypeText, Target = "b" } });
        p.Normalize();
        Assert.Single(p.BaseLayer.Bindings);
        Assert.Equal("a", p.BaseLayer.Bindings[0].Action.Target); // first wins
    }

    [Fact]
    public void Normalize_makes_duplicate_layer_names_unique()
    {
        var p = new MacroProfile
        {
            Layers = new List<MacroLayer>
            {
                new() { Name = "Base" },
                new() { Name = "Base" },
            },
        };
        p.Normalize();
        Assert.Equal("Base", p.Layers[0].Name);
        Assert.NotEqual("Base", p.Layers[1].Name);
    }

    [Fact]
    public void Normalize_is_idempotent()
    {
        var p = LayerWith(new MacroBinding
        {
            VirtualKey = 0x41,
            Action = new MacroAction { Kind = MacroActionKind.TypeText, Target = "x" },
        });
        p.Normalize();
        int keyCode = p.BaseLayer.Bindings[0].KeyCode;
        p.Normalize();
        Assert.Equal(keyCode, p.BaseLayer.Bindings[0].KeyCode);
        Assert.Single(p.BaseLayer.Bindings);
    }

    [Fact]
    public void Layer_action_is_empty_without_a_target()
    {
        var hold = new MacroAction { Kind = MacroActionKind.LayerHold, Target = "" };
        Assert.True(hold.IsEmpty);
        hold.Target = "Gaming";
        Assert.False(hold.IsEmpty);
    }

    private static MacroProfile LayerWith(params MacroBinding[] bindings)
        => new() { Layers = new List<MacroLayer> { new() { Name = "Base", Bindings = bindings.ToList() } } };
}
