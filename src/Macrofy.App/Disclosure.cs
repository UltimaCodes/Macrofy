using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Macrofy.App;

// A "show more" section that opens smoothly. The stock Expander makes the content full
// height in one jump and then slides it in, so everything below it leaps; here the height
// itself eases open (and closed), so whatever sits underneath moves with it. The look is
// set in App.xaml (Style TargetType="local:Disclosure").
public class Disclosure : HeaderedContentControl
{
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(Disclosure),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                (d, e) => ((Disclosure)d).Animate((bool)e.NewValue)));

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    private FrameworkElement? _host;    // clips the content and has its Height animated
    private FrameworkElement? _content;
    private RotateTransform? _chevron;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _host = GetTemplateChild("PART_Host") as FrameworkElement;
        _content = GetTemplateChild("PART_Content") as FrameworkElement;
        // The rotation is made here, not in the template: transforms declared in a template are
        // frozen and shared between every copy, so they can't be animated.
        _chevron = new RotateTransform(IsOpen ? 180 : 0);
        if (GetTemplateChild("PART_Chevron") is UIElement chevron)
            chevron.RenderTransform = _chevron;
        if (_host is not null)
            _host.Height = IsOpen ? double.NaN : 0;
        if (_content is not null)
            _content.Opacity = IsOpen ? 1 : 0;
    }

    private void Animate(bool open)
    {
        if (_host is null || _content is null)
            return;

        bool smooth = SystemParameters.ClientAreaAnimation && IsLoaded;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(open ? 240 : 180);

        if (_chevron is not null)
        {
            if (smooth)
                _chevron.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(open ? 180 : 0, duration) { EasingFunction = ease });
            else
            {
                _chevron.BeginAnimation(RotateTransform.AngleProperty, null);
                _chevron.Angle = open ? 180 : 0;
            }
        }

        // Where the height starts (mid-animation if it's toggled again quickly) and where it
        // ends: the content's natural height at the current width, or nothing.
        double from = _host.ActualHeight;
        double to = 0;
        if (open)
        {
            double width = _host.ActualWidth > 0 ? _host.ActualWidth : ActualWidth;
            _content.Measure(new Size(width, double.PositiveInfinity));
            to = _content.DesiredSize.Height;
        }

        if (!smooth)
        {
            _host.BeginAnimation(HeightProperty, null);
            _host.Height = open ? double.NaN : 0;
            _content.Opacity = 1;
            return;
        }

        var grow = new DoubleAnimation(from, to, duration) { EasingFunction = ease };
        grow.Completed += (_, _) =>
        {
            if (IsOpen != open)
                return; // toggled again before this finished; the newer animation owns it
            _host.BeginAnimation(HeightProperty, null);
            // Once open, go back to auto height so it follows the window being resized.
            _host.Height = open ? double.NaN : 0;
        };
        _host.BeginAnimation(HeightProperty, grow);
        _content.BeginAnimation(OpacityProperty,
            new DoubleAnimation(open ? 1 : 0, duration) { EasingFunction = ease });
    }
}
