using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CUETools.Wpf.Controls;

/// <summary>
/// Physical 3D rubber-button feel. The button template raises a "face" layer above a darker "edge"
/// layer (the visible thickness); this behavior animates that face: it LIFTS a touch on hover, and
/// on press it drops down INTO the edge and tilts TOWARD the exact point clicked - press a corner
/// and that corner dips more than the far one, like squashing a soft rubber cap. Release springs
/// back with an elastic rubber-hose overshoot. Attached so any Button opts in via style
/// (BendyButton.Enabled="True"); the template must name the lifted layer "face". Honors
/// reduced-motion / no-GPU (snaps instead of animating).
/// </summary>
public static class BendyButton
{
    private const double RaisedBy = 6;   // px the face sits above the edge at rest
    private const double HoverBy = 8;    // lifts to here on hover
    private const double PressBy = 1.5;  // drops to here when pressed (into the edge)

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
            b.Loaded += OnLoaded;
            b.MouseEnter += OnEnter;
            b.MouseLeave += OnLeave;
            b.PreviewMouseLeftButtonDown += OnDown;
            b.PreviewMouseLeftButtonUp += OnUp;
            if (b.IsLoaded) Setup(b);
        }
        else
        {
            b.Loaded -= OnLoaded;
            b.MouseEnter -= OnEnter;
            b.MouseLeave -= OnLeave;
            b.PreviewMouseLeftButtonDown -= OnDown;
            b.PreviewMouseLeftButtonUp -= OnUp;
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Setup((Button)sender);

    // the interactive transform group living on the template's "face" element
    private sealed class Rig
    {
        public FrameworkElement Face = null!;
        public ScaleTransform Scale = new(1, 1);
        public SkewTransform Skew = new(0, 0);
        public TranslateTransform Move = new(0, -RaisedBy);
    }

    private static readonly DependencyProperty RigProperty =
        DependencyProperty.RegisterAttached("Rig", typeof(Rig), typeof(BendyButton));

    private static Rig? Get(Button b)
    {
        if (b.GetValue(RigProperty) is Rig r) return r;
        return Setup(b);
    }

    private static Rig? Setup(Button b)
    {
        if (b.GetValue(RigProperty) is Rig existing) return existing;
        // the template names the lifted layer "face"; without it, do nothing (plain button)
        if (b.Template?.FindName("face", b) is not FrameworkElement face) return null;
        var rig = new Rig { Face = face };
        var g = new TransformGroup();
        g.Children.Add(rig.Scale);
        g.Children.Add(rig.Skew);
        g.Children.Add(rig.Move);
        face.RenderTransform = g;
        face.RenderTransformOrigin = new Point(0.5, 0.5);
        b.SetValue(RigProperty, rig);
        return rig;
    }

    private static bool Motion => SystemParameters.ClientAreaAnimation && RenderCapability.Tier > 0;

    private static void OnEnter(object sender, MouseEventArgs e)
    {
        var rig = Get((Button)sender);
        if (rig == null) return;
        if (!Motion) { rig.Move.Y = -HoverBy; return; }
        Anim(rig.Move, TranslateTransform.YProperty, -HoverBy, 160, new QuadraticEase { EasingMode = EasingMode.EaseOut });
    }

    private static void OnLeave(object sender, MouseEventArgs e)
    {
        var rig = Get((Button)sender);
        if (rig == null) return;
        if (!Motion) { rig.Move.Y = -RaisedBy; rig.Scale.ScaleX = rig.Scale.ScaleY = 1; rig.Skew.AngleX = rig.Skew.AngleY = 0; return; }
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        Anim(rig.Move, TranslateTransform.YProperty, -RaisedBy, 220, ease);
        Anim(rig.Scale, ScaleTransform.ScaleXProperty, 1, 220, ease);
        Anim(rig.Scale, ScaleTransform.ScaleYProperty, 1, 220, ease);
        Anim(rig.Skew, SkewTransform.AngleXProperty, 0, 220, ease);
        Anim(rig.Skew, SkewTransform.AngleYProperty, 0, 220, ease);
    }

    private static void OnDown(object sender, MouseButtonEventArgs e)
    {
        var b = (Button)sender;
        var rig = Get(b);
        if (rig == null) return;

        Point p = e.GetPosition(rig.Face);
        double ox = rig.Face.ActualWidth > 0 ? Clamp01(p.X / rig.Face.ActualWidth) : 0.5;
        double oy = rig.Face.ActualHeight > 0 ? Clamp01(p.Y / rig.Face.ActualHeight) : 0.5;

        // A real rubber button presses mostly straight DOWN into its base; the off-centre lean is
        // SUBTLE, not a full parallelogram shear (that looked like a tilting card). So the dominant
        // motion is the drop into the edge + a small vertical squish; the skew toward the click is
        // just a few degrees so the side you pressed gives a touch more.
        double skewY = (ox - 0.5) * 7;    // clicked horizontal side dips slightly
        double skewX = (oy - 0.5) * 4;    // clicked vertical side leans slightly
        if (!Motion)
        {
            rig.Move.Y = -PressBy; rig.Scale.ScaleX = 0.99; rig.Scale.ScaleY = 0.94;
            rig.Skew.AngleX = skewX; rig.Skew.AngleY = skewY; return;
        }
        var q = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        Anim(rig.Move, TranslateTransform.YProperty, -PressBy, 80, q);
        Anim(rig.Scale, ScaleTransform.ScaleXProperty, 0.99, 80, q);
        Anim(rig.Scale, ScaleTransform.ScaleYProperty, 0.94, 80, q);
        Anim(rig.Skew, SkewTransform.AngleXProperty, skewX, 80, q);
        Anim(rig.Skew, SkewTransform.AngleYProperty, skewY, 80, q);
    }

    private static void OnUp(object sender, MouseButtonEventArgs e)
    {
        var b = (Button)sender;
        var rig = Get(b);
        if (rig == null) return;
        double rest = b.IsMouseOver ? -HoverBy : -RaisedBy;
        if (!Motion)
        {
            rig.Move.Y = rest; rig.Scale.ScaleX = rig.Scale.ScaleY = 1; rig.Skew.AngleX = rig.Skew.AngleY = 0; return;
        }
        // spring back like real rubber: one small overshoot, then settle quickly (damped, not a
        // cartoon wobble)
        var elastic = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 6 };
        Anim(rig.Move, TranslateTransform.YProperty, rest, 340, elastic);
        Anim(rig.Scale, ScaleTransform.ScaleXProperty, 1, 340, elastic);
        Anim(rig.Scale, ScaleTransform.ScaleYProperty, 1, 340, elastic);
        Anim(rig.Skew, SkewTransform.AngleXProperty, 0, 340, elastic);
        Anim(rig.Skew, SkewTransform.AngleYProperty, 0, 340, elastic);
    }

    private static void Anim(DependencyObject t, DependencyProperty p, double to, int ms, IEasingFunction ease)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
        ((Animatable)t).BeginAnimation(p, a);
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
