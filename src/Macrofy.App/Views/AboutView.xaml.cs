using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Macrofy.App.ViewModels;
using Macrofy.Core;

namespace Macrofy.App.Views;

public partial class AboutView : UserControl
{
    // Pressing the logo cycles through these.
    private static readonly string[] Taglines =
    {
        "Give your spare keyboard a job.",
        "That key you never press can open Spotify now.",
        "A macro pad you already own.",
        "Fewer clicks. Same desk.",
        "Every key gets a second career.",
    };

    private int _tagline;

    public AboutView()
    {
        InitializeComponent();
        Tagline.Text = Taglines[0];
        DataPath.Text = ShortPath(AppPaths.DataDir);
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    // The logo is a key, so it presses like one.
    private void LogoKey_Click(object sender, RoutedEventArgs e)
    {
        _tagline = (_tagline + 1) % Taglines.Length;
        Tagline.Text = Taglines[_tagline];
        Anim.FadeIn(Tagline, rise: 6, ms: 220);

        if (!SystemParameters.ClientAreaAnimation || Logo.RenderTransform is not ScaleTransform scale)
            return;
        var press = new DoubleAnimation(1, 0.9, TimeSpan.FromMilliseconds(70))
        {
            AutoReverse = true,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, press);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, press);
    }

    // %AppData%\Macrofy reads better than the full C:\Users\...\AppData\Roaming path.
    private static string ShortPath(string path)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return path.StartsWith(appData, StringComparison.OrdinalIgnoreCase)
            ? "%AppData%" + path[appData.Length..]
            : path;
    }

    private void OpenData_Click(object sender, RoutedEventArgs e) => Open(AppPaths.DataDir);

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) => await Vm.CheckForUpdatesAsync(userAsked: true);

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e) => await Vm.InstallUpdateAsync();

    private void OpenRepo_Click(object sender, RoutedEventArgs e) => Open(UpdateService.RepoUrl);

    private void ReportProblem_Click(object sender, RoutedEventArgs e) => Open(UpdateService.RepoUrl + "/issues/new");

    private void ReleaseNotes_Click(object sender, RoutedEventArgs e) => Open(UpdateService.RepoUrl + "/releases");

    private static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { /* best effort */ }
    }
}
