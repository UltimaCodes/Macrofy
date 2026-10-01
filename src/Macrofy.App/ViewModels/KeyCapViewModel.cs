using Wpf.Ui.Controls;

namespace Macrofy.App.ViewModels;

// One key on the visual keyboard. Width/Height are in pixels; Key (a KeyCodes physical key)
// is null for layout spacers. A bound key shows what it does (icon + short label).
public sealed class KeyCapViewModel : ObservableObject
{
    public string Label { get; }
    public int? Key { get; }
    public double Width { get; }
    public double Height { get; }
    public bool IsSpacer { get; }

    // False for keys Macrofy can never capture (the OS handles them before Raw Input).
    // The UI shows these dimmed so users don't think binding them is just broken.
    public bool Capturable { get; }

    private bool _isPressed;
    public bool IsPressed
    {
        get => _isPressed;
        set => SetProperty(ref _isPressed, value);
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private string _bindingLabel = string.Empty;
    public string BindingLabel
    {
        get => _bindingLabel;
        private set => SetProperty(ref _bindingLabel, value);
    }

    private SymbolRegular _bindingIcon = SymbolRegular.Empty;
    public SymbolRegular BindingIcon
    {
        get => _bindingIcon;
        private set => SetProperty(ref _bindingIcon, value);
    }

    private bool _isBound;
    public bool IsBound
    {
        get => _isBound;
        private set => SetProperty(ref _isBound, value);
    }

    // Bound on the Base layer and showing through on another layer (it isn't redefined there).
    private bool _isInherited;
    public bool IsInherited
    {
        get => _isInherited;
        private set => SetProperty(ref _isInherited, value);
    }

    public KeyCapViewModel(string label, int? key, double width, double height, bool isSpacer = false, bool capturable = true)
    {
        Label = label;
        Key = key;
        Width = width;
        Height = height;
        IsSpacer = isSpacer;
        Capturable = capturable;
    }

    public void ShowBinding(string label, SymbolRegular icon, bool inherited)
    {
        BindingLabel = label;
        BindingIcon = icon;
        IsInherited = inherited;
        IsBound = true;
    }

    public void ClearBinding()
    {
        IsBound = false;
        IsInherited = false;
        BindingLabel = string.Empty;
        BindingIcon = SymbolRegular.Empty;
    }
}
