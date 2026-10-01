using Macrofy.Core.Macros;
using Xunit;

namespace Macrofy.Tests;

public class ProfileTemplatesTests
{
    public static IEnumerable<object[]> Templates => ProfileTemplates.All.Select(t => new object[] { t.Id });

    private static MacroProfile Build(string id) => ProfileTemplates.Find(id)!.Build();

    [Fact]
    public void There_are_obs_photoshop_and_vscode_templates()
    {
        var names = ProfileTemplates.All.Select(t => t.Name).ToList();
        Assert.Contains("OBS Studio", names);
        Assert.Contains("Photoshop", names);
        Assert.Contains("VS Code", names);
        Assert.Equal(names.Count, ProfileTemplates.All.Select(t => t.Id).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Every_binding_is_complete_and_labelled(string id)
    {
        var profile = Build(id);
        var bindings = profile.Layers.SelectMany(l => l.Bindings).ToList();
        Assert.NotEmpty(bindings);
        Assert.All(bindings, b =>
        {
            Assert.False(b.IsEmpty);
            Assert.False(string.IsNullOrWhiteSpace(b.Label));
            Assert.NotEqual(0, b.KeyCode);
        });
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void No_two_bindings_share_a_key(string id)
    {
        foreach (var layer in Build(id).Layers)
            Assert.Equal(layer.Bindings.Count, layer.Bindings.Select(b => b.KeyCode).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Every_shortcut_is_one_macrofy_can_send(string id)
    {
        var actions = Build(id).Layers.SelectMany(l => l.Bindings)
            .SelectMany(b => b.HasSteps ? b.Steps.Select(s => s.Action) : new[] { b.Action })
            .Where(a => a.Kind == MacroActionKind.SendHotkey);
        Assert.All(actions, a =>
            Assert.True(MacroExecutor.TryParseHotkey(a.Target, out _, out _, out var error), $"{a.Target}: {error}"));
    }

    [Fact]
    public void Building_twice_gives_independent_profiles()
    {
        var a = Build("template:photoshop");
        var b = Build("template:photoshop");
        a.BaseLayer.Bindings.Clear();
        Assert.NotEmpty(b.BaseLayer.Bindings);
    }

    [Fact]
    public void Obs_template_explains_the_setup_it_needs()
        => Assert.NotEmpty(ProfileTemplates.Find("template:obs")!.Setup);
}
