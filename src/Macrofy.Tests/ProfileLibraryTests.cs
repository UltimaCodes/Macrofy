using System.IO;
using Macrofy.Core.Macros;
using Xunit;

namespace Macrofy.Tests;

public sealed class ProfileLibraryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "macrofy-tests-" + Guid.NewGuid().ToString("N"));

    public ProfileLibraryTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp */ }
    }

    private static MacroBinding TypeX(int keyCode) => new()
    {
        KeyCode = keyCode,
        Action = new MacroAction { Kind = MacroActionKind.TypeText, Target = "x" },
    };

    [Fact]
    public void A_saved_profile_survives_a_reload()
    {
        var lib = new ProfileLibrary(_dir);
        var p = lib.Create("Work");
        p.BaseLayer.Bindings.Add(TypeX(0x1E));
        lib.Save(p);

        var reloaded = new ProfileLibrary(_dir).Get(p.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("Work", reloaded!.Name);
        Assert.Single(reloaded.BaseLayer.Bindings);
        Assert.Equal(0x1E, reloaded.BaseLayer.Bindings[0].KeyCode);
    }

    [Fact]
    public void Names_are_kept_unique()
    {
        var lib = new ProfileLibrary(_dir);
        var a = lib.Create("Gaming");
        var b = lib.Create("Gaming");
        var c = lib.Create("gaming");
        Assert.Equal("Gaming", a.Name);
        Assert.Equal("Gaming (2)", b.Name);
        Assert.Equal("gaming (3)", c.Name);
    }

    [Fact]
    public void Renaming_to_its_own_name_with_different_case_is_allowed()
    {
        var lib = new ProfileLibrary(_dir);
        var p = lib.Create("work");
        lib.Rename(p, "Work");
        Assert.Equal("Work", p.Name);
    }

    [Fact]
    public void Deleting_a_profile_removes_its_file_and_keyboard_assignments()
    {
        var lib = new ProfileLibrary(_dir);
        var p = lib.Create("Temp");
        lib.Assign("046D:C31C", p.Id);
        lib.Delete(p.Id);

        Assert.Null(lib.Get(p.Id));
        Assert.Null(lib.AssignedProfileId("046D:C31C"));
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "library")));
    }

    [Fact]
    public void Two_keyboards_can_share_a_profile()
    {
        var lib = new ProfileLibrary(_dir);
        var p = lib.Create("Shared");
        lib.Assign("AAAA:0001", p.Id);
        lib.Assign("BBBB:0002", p.Id);
        Assert.Equal(2, lib.DevicesUsing(p.Id).Count);
        Assert.Same(p, lib.ForDevice("AAAA:0001", "A"));
        Assert.Same(p, lib.ForDevice("BBBB:0002", "B"));
    }

    [Fact]
    public void A_keyboard_with_no_profile_gets_an_unsaved_one_named_after_it()
    {
        var lib = new ProfileLibrary(_dir);
        var p = lib.ForDevice("046D:C31C", "Logitech K120");
        Assert.Equal(string.Empty, p.Id);
        Assert.Equal("Logitech K120", p.Name);
        Assert.Empty(lib.Profiles); // not in the library until something is saved

        p.BaseLayer.Bindings.Add(TypeX(0x1E));
        lib.Save(p);
        lib.Assign("046D:C31C", p.Id);

        Assert.NotEqual(string.Empty, p.Id);
        Assert.Same(p, lib.ForDevice("046D:C31C", "Logitech K120"));
    }

    [Fact]
    public void A_keyboard_finds_its_profile_under_an_older_id()
    {
        var lib = new ProfileLibrary(_dir);
        var p = lib.Create("Old");
        lib.Assign("046D:C31C", p.Id);

        var found = lib.ForDevice("046D:C31C@1a2b3c4d", "Keyboard", new[] { "046D:C31C" });
        Assert.Same(p, found);
        Assert.Equal(p.Id, lib.AssignedProfileId("046D:C31C@1a2b3c4d"));
    }

    [Fact]
    public void Old_per_keyboard_profiles_are_moved_into_the_library()
    {
        string legacyDir = Path.Combine(_dir, "profiles");
        Directory.CreateDirectory(legacyDir);
        // The exact shape older versions wrote, including the duplicated BaseLayer and computed fields.
        File.WriteAllText(Path.Combine(legacyDir, "3151_502D.json"), """
            {
              "DeviceId": "3151:502D",
              "DeviceName": "AttackShark X65HE",
              "Layers": [ { "Name": "Base", "Bindings": [
                { "KeyCode": 30, "VirtualKey": 65, "KeyName": "A",
                  "Action": { "Kind": "MediaKey", "Target": "VolumeUp", "Arguments": "", "IsEmpty": false, "Description": "Media  Volume up" },
                  "Steps": [], "RepeatWhileHeld": false, "HasSteps": false, "IsEmpty": false, "Description": "Media  Volume up" } ] } ],
              "BaseLayer": { "Name": "Base", "Bindings": [] }
            }
            """);
        File.WriteAllText(Path.Combine(legacyDir, "1D57_FA60.json"), """
            { "DeviceId": "1D57:FA60", "DeviceName": "2.4G Wireless Device", "Layers": [ { "Name": "Base", "Bindings": [] } ] }
            """);

        var lib = new ProfileLibrary(_dir);

        var migrated = Assert.Single(lib.Profiles); // the empty one isn't worth a library entry
        Assert.Equal("AttackShark X65HE", migrated.Name);
        Assert.Equal(migrated.Id, lib.AssignedProfileId("3151:502D"));
        var binding = Assert.Single(migrated.BaseLayer.Bindings);
        Assert.Equal(30, binding.KeyCode);
        Assert.Equal(MacroActionKind.MediaKey, binding.Action.Kind);
        Assert.True(File.Exists(Path.Combine(legacyDir, "3151_502D.json.migrated")));
        Assert.True(File.Exists(Path.Combine(legacyDir, "1D57_FA60.json.migrated")));

        // Loading again doesn't migrate twice.
        Assert.Single(new ProfileLibrary(_dir).Profiles);
    }

    [Fact]
    public void Moved_profiles_take_the_name_the_user_gave_the_keyboard()
    {
        string legacyDir = Path.Combine(_dir, "profiles");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllText(Path.Combine(legacyDir, "8808_660D.json"), """
            { "DeviceId": "8808:660D", "DeviceName": "Keyboard 8808:660D", "Layers": [ { "Name": "Base", "Bindings": [
              { "KeyCode": 31, "Action": { "Kind": "SendHotkey", "Target": "Ctrl+C" } } ] } ] }
            """);

        var lib = new ProfileLibrary(_dir, id => id == "8808:660D" ? "osukeypad" : null);

        Assert.Equal("osukeypad", Assert.Single(lib.Profiles).Name);
    }

    [Fact]
    public void Saved_files_do_not_contain_computed_fields()
    {
        var lib = new ProfileLibrary(_dir);
        var p = lib.Create("Clean");
        p.BaseLayer.Bindings.Add(TypeX(0x1E));
        lib.Save(p);

        string json = File.ReadAllText(Path.Combine(_dir, "library", p.Id + ".json"));
        Assert.DoesNotContain("BaseLayer", json);
        Assert.DoesNotContain("IsEmpty", json);
        Assert.DoesNotContain("Description\": \"Type", json);
        Assert.DoesNotContain("DeviceId", json);
    }

    [Fact]
    public void An_unreadable_profile_file_is_set_aside_not_deleted()
    {
        string libraryDir = Path.Combine(_dir, "library");
        Directory.CreateDirectory(libraryDir);
        File.WriteAllText(Path.Combine(libraryDir, "broken.json"), "{ not json");

        var lib = new ProfileLibrary(_dir);

        Assert.Empty(lib.Profiles);
        var backup = Assert.Single(lib.SetAsideFiles);
        Assert.True(File.Exists(backup));
    }

    [Fact]
    public void Importing_an_old_export_gives_it_a_new_id_and_a_name()
    {
        string file = Path.Combine(_dir, "My Pad.macrofy.json");
        File.WriteAllText(file, """
            { "DeviceId": "X", "DeviceName": "My Pad", "Layers": [ { "Name": "Base", "Bindings": [
              { "KeyCode": 30, "Action": { "Kind": "TypeText", "Target": "hi" } } ] } ] }
            """);
        var lib = new ProfileLibrary(_dir);

        var imported = lib.Import(file);

        Assert.NotNull(imported);
        Assert.Equal("My Pad", imported!.Name);
        Assert.NotEqual(string.Empty, imported.Id);
        Assert.Null(imported.DeviceId);
        Assert.Same(imported, lib.Get(imported.Id));
    }

    [Fact]
    public void Export_then_import_round_trips_as_a_separate_profile()
    {
        var lib = new ProfileLibrary(_dir);
        var p = lib.Create("Streaming");
        p.BaseLayer.Bindings.Add(TypeX(0x1E));
        lib.Save(p);
        string file = Path.Combine(_dir, "out.json");

        lib.Export(p, file);
        var copy = lib.Import(file);

        Assert.NotNull(copy);
        Assert.NotEqual(p.Id, copy!.Id);
        Assert.Equal("Streaming (2)", copy.Name);
        Assert.Single(copy.BaseLayer.Bindings);
    }

    [Fact]
    public void Importing_something_that_is_not_a_profile_returns_null()
    {
        string file = Path.Combine(_dir, "nope.json");
        File.WriteAllText(file, "hello");
        Assert.Null(new ProfileLibrary(_dir).Import(file));
    }

    [Fact]
    public void Profiles_made_from_a_template_are_independent_copies()
    {
        var lib = new ProfileLibrary(_dir);
        var template = ProfileTemplates.All[0];

        var first = lib.CreateFromTemplate(template);
        var second = lib.CreateFromTemplate(template);
        first.BaseLayer.Bindings.Clear();

        Assert.Equal(template.Id, first.TemplateId);
        Assert.NotEmpty(second.BaseLayer.Bindings);
        Assert.Equal(template.Name + " (2)", second.Name);
    }

    [Fact]
    public void Duplicate_copies_bindings_under_a_new_name()
    {
        var lib = new ProfileLibrary(_dir);
        var p = lib.Create("Work");
        p.BaseLayer.Bindings.Add(TypeX(0x1E));
        lib.Save(p);

        var dup = lib.Duplicate(p);

        Assert.Equal("Work (copy)", dup.Name);
        Assert.Single(dup.BaseLayer.Bindings);
        Assert.NotSame(p.BaseLayer.Bindings[0], dup.BaseLayer.Bindings[0]);
    }

    [Fact]
    public void Changed_is_raised_when_a_keyboard_switches_profile()
    {
        var lib = new ProfileLibrary(_dir);
        var p = lib.Create("A");
        int changes = 0;
        lib.Changed += (_, _) => changes++;

        lib.Assign("DEV", p.Id);
        lib.Assign("DEV", p.Id); // no-op

        Assert.Equal(1, changes);
    }
}
