using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Macrofy.App.Views;

public partial class AboutView : UserControl
{
    public AboutView() => InitializeComponent();

    private void OpenRepo_Click(object sender, RoutedEventArgs e) => Open(UpdateService.RepoUrl);

    private void ReportProblem_Click(object sender, RoutedEventArgs e) => Open(UpdateService.RepoUrl + "/issues/new");

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* best effort */ }
    }
}
