using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Macrofy.App.ViewModels;
using Macrofy.Core.Input;
using Macrofy.Core.Macros;

namespace Macrofy.App.Views;

public partial class KeyboardsView : UserControl
{
    private MainViewModel? _vm;

    public KeyboardsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as MainViewModel);
    }

    private MainViewModel Vm => _vm!;

    private void Attach(MainViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.CaptureLocked -= OnCaptureLocked;
        }
        _vm = vm;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.CaptureLocked += OnCaptureLocked;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.SelectedLayer):
                Anim.Flash(KeyboardBox);
                break;
            case nameof(MainViewModel.BindKeyCode) when Vm.HasBindKey:
                Anim.FadeIn(EditorContent, rise: 6, ms: 200);
                break;
            case nameof(MainViewModel.IsIdentifying) when Vm.IsIdentifying:
                // Take focus off any text box so the identifying key press doesn't type into it.
                IdentifyOverlay.Focus();
                break;
        }
    }

    // ---- keyboard picking ----

    private void SwitchKeyboard_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = SwitchKeyboardButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var kb in Vm.Keyboards)
        {
            var item = new MenuItem
            {
                Header = $"{kb.DisplayName}   ·   {ConnectionLabel(kb.Connection)}",
                IsCheckable = true,
                IsChecked = kb.Id == Vm.SelectedKeyboard?.Id,
            };
            item.Click += (_, _) => Vm.SelectKeyboard(kb);
            menu.Items.Add(item);
        }
        if (Vm.Keyboards.Count == 0)
            menu.Items.Add(new MenuItem { Header = "No keyboards found", IsEnabled = false });
        menu.Items.Add(new Separator());
        var refresh = new MenuItem { Header = "Look for keyboards again" };
        refresh.Click += (_, _) => Vm.RefreshDevices();
        menu.Items.Add(refresh);
        var all = new MenuItem { Header = "Show devices that aren't keyboards", IsCheckable = true, IsChecked = Vm.ShowAllDevices };
        all.Click += (_, _) => Vm.ShowAllDevices = !Vm.ShowAllDevices;
        menu.Items.Add(all);
        menu.IsOpen = true;
    }

    private static string ConnectionLabel(ConnectionKind kind) => kind switch
    {
        ConnectionKind.Usb => "USB",
        ConnectionKind.Bluetooth => "Bluetooth",
        ConnectionKind.BuiltIn => "Built-in",
        ConnectionKind.Virtual => "Virtual",
        _ => "Keyboard",
    };

    private void IdentifyKeyboard_Click(object sender, RoutedEventArgs e) => Vm.BeginIdentify();

    private void CancelIdentify_Click(object sender, RoutedEventArgs e) => Vm.CancelIdentify();

    private void RenameKeyboard_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedKeyboard is not { } kb)
            return;
        var dialog = new TextPromptWindow("Rename keyboard", "Keyboard name (leave empty to use the hardware name)", kb.DisplayName)
        {
            Owner = Window.GetWindow(this),
        };
        if (dialog.ShowDialog() == true)
            Vm.RenameSelected(dialog.Value);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Vm.RefreshDevices();

    private void OnCaptureLocked(object? sender, EventArgs e)
    {
        var shake = new DoubleAnimationUsingKeyFrames();
        double[] offsets = { 0, -6, 6, -4, 4, -2, 0 };
        for (int i = 0; i < offsets.Length; i++)
            shake.KeyFrames.Add(new EasingDoubleKeyFrame(offsets[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(i * 45))));
        CaptureToggleShake.BeginAnimation(TranslateTransform.XProperty, shake);
    }

    // ---- profile on this keyboard ----

    private void ProfileMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ProfileButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        string? activeId = Vm.ActiveProfile?.Id;
        foreach (var profile in Vm.LibraryProfiles)
        {
            var item = new MenuItem
            {
                Header = profile.Name,
                IsCheckable = true,
                IsChecked = string.Equals(profile.Id, activeId, StringComparison.OrdinalIgnoreCase),
            };
            item.Click += (_, _) => Vm.ApplyProfile(profile);
            menu.Items.Add(item);
        }
        if (Vm.LibraryProfiles.Count == 0)
            menu.Items.Add(new MenuItem { Header = "No saved profiles yet", IsEnabled = false });
        menu.Items.Add(new Separator());
        var manage = new MenuItem { Header = "Manage profiles and templates…" };
        manage.Click += (_, _) => Vm.RequestNavigate(AppPage.Profiles);
        menu.Items.Add(manage);
        menu.IsOpen = true;
    }

    // ---- layers ----

    private void AddLayer_Click(object sender, RoutedEventArgs e) => Vm.AddLayer();

    private void RemoveLayer_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedLayer is not { } layer)
            return;
        if (layer.Bindings.Count > 0)
        {
            var confirm = MessageBox.Show(Window.GetWindow(this),
                $"Remove the \"{layer.Name}\" layer and its {layer.Bindings.Count} macro(s)?",
                "Remove layer", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK)
                return;
        }
        Vm.RemoveLayer(layer);
    }

    private void LayerList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm.SelectedLayer is not { } layer)
            return;
        var dialog = new TextPromptWindow("Rename layer", "Layer name", layer.Name) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
            Vm.RenameLayer(layer, dialog.Value);
    }

    // ---- layout ----

    private void LearnKeys_Click(object sender, RoutedEventArgs e) => Vm.StartLearning();
    private void SaveLearned_Click(object sender, RoutedEventArgs e) => Vm.SaveLearned();
    private void CancelLearn_Click(object sender, RoutedEventArgs e) => Vm.CancelLearning();

    // ---- picking a key ----

    private void KeyCap_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: KeyCapViewModel { Key: int key, Capturable: true, IsSpacer: false } })
            Vm.PickKey(key);
    }

    private void BoundChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MacroBinding binding })
            Vm.PickKey(binding.KeyCode);
    }

    private void CloseEditor_Click(object sender, RoutedEventArgs e) => Vm.CloseEditor();

    // ---- editing ----

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose what this key opens",
            Filter = "Programs and shortcuts (*.exe;*.lnk;*.bat;*.cmd;*.url)|*.exe;*.lnk;*.bat;*.cmd;*.url|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true)
            Vm.BindTarget = dlg.FileName;
    }

    // Records a shortcut into the form. The field is read-only; pressing keys here builds a
    // string the macro executor understands (e.g. "Ctrl+Shift+Esc"). Esc clears it.
    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            Vm.BindTarget = string.Empty;
            return;
        }
        if (HotkeyNames.IsModifier(key))
            return; // wait for a non-modifier to complete the combo

        string? name = HotkeyNames.FromKey(key);
        if (name is null)
            return;

        var parts = new List<string>();
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(name);
        Vm.BindTarget = string.Join("+", parts);
    }

    private void AddStep_Click(object sender, RoutedEventArgs e) => Vm.AddStep();

    private void StepUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MacroStep step })
            Vm.MoveStep(step, -1);
    }

    private void StepDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MacroStep step })
            Vm.MoveStep(step, +1);
    }

    private void StepRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MacroStep step })
            Vm.RemoveStep(step);
    }

    private void SaveMacro_Click(object sender, RoutedEventArgs e) => Vm.SaveMacro();

    private void RemoveMacro_Click(object sender, RoutedEventArgs e) => Vm.RemoveCurrentMacro();
}
