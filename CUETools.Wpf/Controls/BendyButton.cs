using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CUETools.Wpf.Controls;

/// <summary>
/// Makes a button feel like squishy rubber: it deforms TOWARD the exact point clicked (a
/// press-in scale anchored at the cursor plus a slight shear in the drag direction) and springs
/// back with an elastic "rubber-hose" overshoot on release. Attached behavior so any Button can
/// opt in via style (BendyButton.Enabled="True") with no per-button code. Honors
/// prefers-reduced-motion by respecting the system's animations-enabled setting.
/// </summary>
public static class BendyButton
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(BendyButton),
            new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject o, bool v) => o.SetValue(EnabledProperty, v);
    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Button b) return;
        if ((bool)e.NewValue)
        {
            b.PreviewMouseLeftButtonDown += OnDown;
            b.PreviewMouseLeftButtonUp += OnUp;
            b.MouseLeave += OnUp;
        }
        else
        {
            b.PreviewMouseLeftButtonDown -= OnDown;
            b.PreviewMouseLeftButtonUp -= OnUp;
            b.MouseLeave -= OnUp;
        }
    }

    // one shared transform per button: scale (the squish) + skew (the lean toward the click)
    private sealed class Rig
    {
        public ScaleTransform Scale = new(1, 1);
        public SkewTransform Skew = new(0, 0);
    }

    private static Rig Ensure(Button b)
    {
        if (b.RenderTransform is TransformGroup g && g.Children.Count == 2
            && g.Children[0] is ScaleTransform s && g.Children[1] is SkewTransform k)
            return new Rig { Scale = s, Skew = k };

        var rig = new Rig();
        var grp = new TransformGroup();
        grp.Children.Add(rig.Scale);
        grp.Children.Add(rig.Skew);
        b.RenderTransform = grp;
        return rig;
    }

    private static bool Motion => SystemParameters.ClientAreaAnimation && RenderCapability.Tier > 0;

    private static void OnDown(object sender, MouseButtonEventArgs e)
    {
        var b = (Button)sender;
        var rig = Ensure(b);

        // anchor the squish at the click point (0..1 within the button)
        Point p = e.GetPosition(b);
        double ox = b.ActualWidth > 0 ? Clamp01(p.X / b.ActualWidth) : 0.5;
        double oy = b.ActualHeight > 0 ? Clamp01(p.Y / b.ActualHeight) : 0.5;
        b.RenderTransformOrigin = new Point(ox, oy);

        // lean the shear toward the side clicked: a click off-centre bends that way
        double skew = (ox - 0.5) * 7.0;   // degrees

        if (!Motion)
        {
            rig.Scale.ScaleX = rig.Scale.ScaleY = 0.94;
            rig.Skew.AngleX = skew;
            return;
        }
        var q = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        Animate(rig.Scale, ScaleTransform.ScaleXProperty, 0.93, 90, q);
        Animate(rig.Scale, ScaleTransform.ScaleYProperty, 0.90, 90, q);
        Animate(rig.Skew, SkewTransform.AngleXProperty, skew, 90, q);
    }

    private static void OnUp(object sender, MouseEventArgs e)
    {
        var b = (Button)sender;
        var rig = Ensure(b);
        if (!Motion)
        {
            rig.Scale.ScaleX = rig.Scale.ScaleY = 1; rig.Skew.AngleX = 0;
            return;
        }
        // rubber-hose spring back: overshoot then settle (the "bendy" bounce)
        var elastic = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 4 };
        Animate(rig.Scale, ScaleTransform.ScaleXProperty, 1.0, 480, elastic);
        Animate(rig.Scale, ScaleTransform.ScaleYProperty, 1.0, 480, elastic);
        Animate(rig.Skew, SkewTransform.AngleXProperty, 0.0, 480, elastic);
    }

    private static void Animate(DependencyObject t, DependencyProperty p, double to, int ms, IEasingFunction ease)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
        ((System.Windows.Media.Animation.Animatable)t).BeginAnimation(p, a);
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
