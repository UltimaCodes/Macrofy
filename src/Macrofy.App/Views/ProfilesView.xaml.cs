using System.IO;
using System.Windows;
using System.Windows.Controls;
using Macrofy.App.ViewModels;

namespace Macrofy.App.Views;

public partial class ProfilesView : UserControl
{
    public ProfilesView() => InitializeComponent();

    private ProfilesViewModel Profiles => ((MainViewModel)DataContext).Profiles;
    private Window? Owner => Window.GetWindow(this);

    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TextPromptWindow("New profile", "Profile name", "My profile") { Owner = Owner };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Value))
            Profiles.NewProfile(dialog.Value);
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import a Macrofy profile",
            Filter = "Macrofy profile (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(Owner) != true)
            return;
        if (!Profiles.Import(dlg.FileName))
            MessageBox.Show(Owner!, "That file couldn't be read as a Macrofy profile.",
                "Import failed", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void ApplyProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProfileCardViewModel card })
            Profiles.Apply(card.Profile);
    }

    private void UseTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TemplateCardViewModel card })
            Profiles.UseTemplate(card.Template);
    }

    private void ProfileMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ProfileCardViewModel card } button)
            return;
        var profile = card.Profile;
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };

        var rename = new MenuItem { Header = "Rename…" };
        rename.Click += (_, _) =>
        {
            var dialog = new TextPromptWindow("Rename profile", "Profile name", profile.Name) { Owner = Owner };
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Value))
                Profiles.Rename(profile, dialog.Value);
        };

        var duplicate = new MenuItem { Header = "Duplicate" };
        duplicate.Click += (_, _) => Profiles.Duplicate(profile);

        var export = new MenuItem { Header = "Export…" };
        export.Click += (_, _) =>
        {
            string fileName = string.Concat(profile.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export profile",
                Filter = "Macrofy profile (*.json)|*.json",
                FileName = fileName + ".macrofy.json",
                DefaultExt = ".json",
            };
            if (dlg.ShowDialog(Owner) == true)
                Profiles.Export(profile, dlg.FileName);
        };

        var delete = new MenuItem { Header = "Delete…" };
        delete.Click += (_, _) =>
        {
            string usedBy = card.UsedByText.StartsWith("On ", StringComparison.Ordinal)
                ? $" It's in use ({card.UsedByText}); those keyboards will start empty."
                : string.Empty;
            var confirm = MessageBox.Show(Owner!,
                $"Delete \"{profile.Name}\" and its {card.Summary}?{usedBy} This can't be undone.",
                "Delete profile", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.OK)
                Profiles.Delete(profile);
        };

        menu.Items.Add(rename);
        menu.Items.Add(duplicate);
        menu.Items.Add(export);
        menu.Items.Add(new Separator());
        menu.Items.Add(delete);
        menu.IsOpen = true;
    }
}
