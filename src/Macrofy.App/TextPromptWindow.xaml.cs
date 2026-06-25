using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace Macrofy.App;

// Tiny modal input dialog (used for renaming layers). Returns the entered text via Value.
public partial class TextPromptWindow : FluentWindow
{
    public string? Value { get; private set; }

    public TextPromptWindow(string title, string prompt, string initial)
    {
        InitializeComponent();
        Title = title;
        TitleBarControl.Title = title;
        PromptText.Text = prompt;
        InputBox.Text = initial;
        Loaded += (_, _) => { InputBox.Focus(); InputBox.SelectAll(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Value = InputBox.Text;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // Enter confirms, Esc cancels.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Ok_Click(this, new RoutedEventArgs());
        else if (e.Key == Key.Escape) Cancel_Click(this, new RoutedEventArgs());
        base.OnKeyDown(e);
    }
}
