using System.Windows;
using Macrofy.App.ViewModels;
using Macrofy.Core.Input;
using Macrofy.Core.Macros;
using Wpf.Ui.Controls;

namespace Macrofy.App;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel(new WhKeyboardBackend());
        DataContext = _viewModel;

        Closed += (_, _) => _viewModel.Dispose();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
        => _viewModel.RefreshDevices();

    private void ClearButton_Click(object sender, RoutedEventArgs e)
        => _viewModel.ClearLog();

    private void RenameButton_Click(object sender, RoutedEventArgs e)
        => _viewModel.RenameSelected();

    private void AddBindingButton_Click(object sender, RoutedEventArgs e)
        => _viewModel.AddBinding();

    private void RemoveBindingButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MacroBinding binding })
            _viewModel.RemoveBinding(binding);
    }
}
