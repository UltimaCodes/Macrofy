using System.Windows;
using Wpf.Ui.Controls;

namespace Macrofy.App;

// One-time friendly intro shown on first run. Skippable.
public partial class WelcomeWindow : FluentWindow
{
    public WelcomeWindow() => InitializeComponent();

    private void Ok_Click(object sender, RoutedEventArgs e) => Close();

    private void Skip_Click(object sender, RoutedEventArgs e) => Close();
}
