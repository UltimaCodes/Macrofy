using System.Windows;
using Wpf.Ui.Controls;

namespace Macrofy.App;

// One-time popup explaining that the Windows keys can't be turned into macros.
public partial class FirstRunKeyHintWindow : FluentWindow
{
    public FirstRunKeyHintWindow() => InitializeComponent();

    private void Ok_Click(object sender, RoutedEventArgs e) => Close();
}
