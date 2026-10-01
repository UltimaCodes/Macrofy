using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Macrofy.App;

// Small motion helpers. Everything is skipped when Windows' "Animation effects" setting is
// off, so the app stays still for people who've asked for that.
public static class Anim
{
    private static bool Enabled => SystemParameters.ClientAreaAnimation;

    // Fade + rise whenever the element becomes visible (page switches, panels appearing).
    public static readonly DependencyProperty FadeInOnVisibleProperty =
        DependencyProperty.RegisterAttached("FadeInOnVisible", typeof(bool), typeof(Anim),
            new PropertyMetadata(false, OnFadeInChanged));

    public static void SetFadeInOnVisible(DependencyObject o, bool value) => o.SetValue(FadeInOnVisibleProperty, value);
    public static bool GetFadeInOnVisible(DependencyObject o) => (bool)o.GetValue(FadeInOnVisibleProperty);

    private static void OnFadeInChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
            return;
        if ((bool)e.NewValue)
            element.IsVisibleChanged += OnVisibleChanged;
        else
            element.IsVisibleChanged -= OnVisibleChanged;
    }

    private static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is UIElement element && (bool)e.NewValue)
            FadeIn(element);
    }

    public static void FadeIn(UIElement element, double rise = 12, int ms = 280)
    {
        if (!Enabled)
            return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(ms);
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        if (Translate(element) is { } t)
            t.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(rise, 0, duration) { EasingFunction = ease });
    }

    // A quick dip in opacity, for content that changes in place (switching layers).
    public static void Flash(UIElement element, int ms = 220)
    {
        if (!Enabled)
            return;
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new QuadraticEase() });
    }

    // Uses the element's TranslateTransform, adding one if it has no transform yet. Elements
    // with some other transform just fade.
    private static TranslateTransform? Translate(UIElement element)
    {
        if (element.RenderTransform is TranslateTransform t)
            return t;
        if (element.RenderTransform is null || element.RenderTransform == Transform.Identity)
        {
            t = new TranslateTransform();
            element.RenderTransform = t;
            return t;
        }
        return null;
    }
}
