using System.Collections.ObjectModel;
using System.Windows.Media;
using Macrofy.Core.Macros;
using Wpf.Ui.Controls;

namespace Macrofy.App.ViewModels;

// A key and what it does, for the little previews on profile and template cards.
public sealed record KeyChip(string Key, string Label);

// One saved profile on the Profiles page.
public sealed class ProfileCardViewModel
{
    public ProfileCardViewModel(MacroProfile profile, IReadOnlyList<string> usedBy, bool isActiveOnSelected)
    {
        Profile = profile;
        var template = ProfileTemplates.Find(profile.TemplateId);
        Description = !string.IsNullOrWhiteSpace(profile.Description) ? profile.Description
            : template?.Description ?? string.Empty;
        Summary = ProfileUi.Summary(profile);
        UsedByText = usedBy.Count == 0 ? "Not on a keyboard yet" : "On " + string.Join(", ", usedBy);
        IsActiveOnSelected = isActiveOnSelected;
        Icon = ProfileUi.Icon(template?.Icon, SymbolRegular.Album24);
        Accent = ProfileUi.Brush(template?.Accent ?? "#2DD4BF");
        AccentMuted = ProfileUi.Brush(template?.Accent ?? "#2DD4BF", 0x2E);
        Preview = ProfileUi.Preview(profile);
    }

    public MacroProfile Profile { get; }
    public string Name => Profile.Name;
    public string Description { get; }
    public bool HasDescription => Description.Length > 0;
    public string Summary { get; }
    public string UsedByText { get; }
    public bool IsActiveOnSelected { get; }
    public SymbolRegular Icon { get; }
    public Brush Accent { get; }
    public Brush AccentMuted { get; }
    public IReadOnlyList<KeyChip> Preview { get; }
}

// One built-in template on the Profiles page.
public sealed class TemplateCardViewModel
{
    public TemplateCardViewModel(ProfileTemplate template)
    {
        Template = template;
        var sample = template.Build();
        Summary = ProfileUi.Summary(sample);
        Icon = ProfileUi.Icon(template.Icon, SymbolRegular.Album24);
        Accent = ProfileUi.Brush(template.Accent);
        AccentMuted = ProfileUi.Brush(template.Accent, 0x2E);
        Preview = ProfileUi.Preview(sample);
    }

    public ProfileTemplate Template { get; }
    public string Name => Template.Name;
    public string Description => Template.Description;
    public IReadOnlyList<string> Setup => Template.Setup;
    public string Summary { get; }
    public SymbolRegular Icon { get; }
    public Brush Accent { get; }
    public Brush AccentMuted { get; }
    public IReadOnlyList<KeyChip> Preview { get; }
}

// Drives the Profiles page: the saved profiles, the templates, and applying one to the
// keyboard selected on the Keyboards page.
public sealed class ProfilesViewModel : ObservableObject
{
    private readonly ProfileLibrary _library;
    private readonly MainViewModel _main;

    public ProfilesViewModel(ProfileLibrary library, MainViewModel main)
    {
        _library = library;
        _main = main;
        Templates = ProfileTemplates.All.Select(t => new TemplateCardViewModel(t)).ToList();
    }

    public ObservableCollection<ProfileCardViewModel> Profiles { get; } = new();
    public IReadOnlyList<TemplateCardViewModel> Templates { get; }

    public bool HasProfiles => Profiles.Count > 0;
    public bool CanApply => _main.SelectedKeyboard is not null;
    public string ApplyTarget => _main.SelectedKeyboard?.DisplayName ?? string.Empty;
    public string ApplyHint => CanApply
        ? $"\"Use\" puts a profile on {ApplyTarget}. Pick a different keyboard on the Keyboards page."
        : "Connect a keyboard to use a profile on it.";

    public void Refresh()
    {
        string? activeId = _main.ActiveProfile?.Id;
        Profiles.Clear();
        foreach (var p in _library.Profiles)
        {
            var usedBy = _library.DevicesUsing(p.Id).Select(_main.DisplayNameFor).ToList();
            bool active = CanApply && !string.IsNullOrEmpty(activeId)
                && string.Equals(p.Id, activeId, StringComparison.OrdinalIgnoreCase);
            Profiles.Add(new ProfileCardViewModel(p, usedBy, active));
        }
        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(ApplyTarget));
        OnPropertyChanged(nameof(ApplyHint));
    }

    // A new, empty profile, put on the selected keyboard so it can be filled in right away.
    public void NewProfile(string name)
    {
        var profile = _library.Create(name);
        if (CanApply)
        {
            _main.ApplyProfile(profile);
            _main.RequestNavigate(AppPage.Keyboards);
        }
    }

    public void Apply(MacroProfile profile) => _main.ApplyProfile(profile);

    public void UseTemplate(ProfileTemplate template)
    {
        var profile = _library.CreateFromTemplate(template);
        if (CanApply)
        {
            _main.ApplyProfile(profile);
            _main.RequestNavigate(AppPage.Keyboards);
        }
        else
        {
            _main.ShowToast($"Added \"{profile.Name}\" to your profiles");
        }
    }

    public void Duplicate(MacroProfile profile)
    {
        var copy = _library.Duplicate(profile);
        _main.ShowToast($"Made \"{copy.Name}\"");
    }

    public void Rename(MacroProfile profile, string name)
    {
        _library.Rename(profile, name);
        _main.OnProfileRenamed(profile);
    }

    public void Delete(MacroProfile profile)
    {
        _library.Delete(profile.Id);
        _main.OnProfileDeleted(profile.Id);
        _main.ShowToast($"Deleted \"{profile.Name}\"");
    }

    public void Export(MacroProfile profile, string path)
    {
        _library.Export(profile, path);
        _main.ShowToast("Profile exported");
    }

    public bool Import(string path)
    {
        var imported = _library.Import(path);
        if (imported is null)
            return false;
        _main.ShowToast($"Imported \"{imported.Name}\"");
        return true;
    }
}

// Shared bits for profile and template cards.
internal static class ProfileUi
{
    private const int PreviewCount = 6;

    public static string Summary(MacroProfile profile)
    {
        int keys = profile.KeyCount;
        int layers = profile.Layers.Count;
        string keyText = keys == 1 ? "1 key" : $"{keys} keys";
        return layers <= 1 ? keyText : $"{keyText} · {layers} layers";
    }

    public static IReadOnlyList<KeyChip> Preview(MacroProfile profile) => profile.BaseLayer.Bindings
        .Where(b => !b.IsEmpty)
        .Take(PreviewCount)
        .Select(b => new KeyChip(VirtualKeyNames.NameForKey(b.KeyCode), BindingLabels.For(b)))
        .ToList();

    public static SymbolRegular Icon(string? name, SymbolRegular fallback)
        => name is not null && Enum.TryParse(name, out SymbolRegular icon) ? icon : fallback;

    public static Brush Brush(string hex, byte alpha = 0xFF)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        color.A = alpha;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
