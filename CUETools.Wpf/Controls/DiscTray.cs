using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CUETools.Wpf.Controls;

/// <summary>
/// An optical drive drawn in 3/4 oblique perspective: the body recedes up-and-right, the tray
/// slides out toward the viewer (down-and-left) carrying the disc. Bind <see cref="Open"/> to the
/// tray state.
///
/// Physics: a real motorised tray does not bounce - it accelerates off the stop, glides at speed,
/// then decelerates into a firm mechanical stop. <see cref="Animate"/> uses an ease-in-out spline
/// (no overshoot) for exactly that feel; opening is a touch slower than the firmer close.
///
/// Projection: a parallel (cabinet) oblique - screen point = origin + x*R + y*Dn + z*Dv, where R is
/// rightward on the face, Dn is down the face, and Dv is the depth axis. The disc lies on the tray
/// top surface (spanned by R and the tray-out axis) so a unit circle mapped through that basis
/// renders as the correctly foreshortened ellipse.
/// </summary>
public sealed class DiscTray : FrameworkElement
{
    public static readonly DependencyProperty OpenProperty = DependencyProperty.Register(
        nameof(Open), typeof(bool), typeof(DiscTray), new PropertyMetadata(false, OnOpenChanged));

    public bool Open { get => (bool)GetValue(OpenProperty); set => SetValue(OpenProperty, value); }

    public static readonly DependencyProperty OpennessProperty = DependencyProperty.Register(
        nameof(Openness), typeof(double), typeof(DiscTray),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Openness { get => (double)GetValue(OpennessProperty); set => SetValue(OpennessProperty, value); }

    public static readonly DependencyProperty HasDiscProperty = DependencyProperty.Register(
        nameof(HasDisc), typeof(bool), typeof(DiscTray),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool HasDisc { get => (bool)GetValue(HasDiscProperty); set => SetValue(HasDiscProperty, value); }

    private static readonly Color Teal = Color.FromRgb(0x34, 0xcf, 0xc0);
    private static readonly Color Amber = Color.FromRgb(0xE9, 0xA6, 0x3F);

    private static void OnOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((DiscTray)d).Animate((bool)e.NewValue);

    private void Animate(bool open)
    {
        // ease-in-out motor curve: accelerate off the stop, glide, decelerate into a firm stop.
        // The spline is monotonic (no value > 1) so the tray never overshoots / bounces.
        var k = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };
        k.KeyFrames.Add(new SplineDoubleKeyFrame(open ? 1.0 : 0.0,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(open ? 1150 : 950)),
            new KeySpline(0.32, 0.0, 0.16, 1.0)));
        BeginAnimation(OpennessProperty, k);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double open = Math.Max(0, Math.Min(1, Openness));

        // oblique basis (screen-space). Depth recedes up-right; the tray slides the opposite way.
        var R = new Vector(1, 0);
        var Dn = new Vector(0, 1);
        var Dv = new Vector(0.52, -0.30);     // +z into the screen (up-right)
        var Tout = new Vector(-0.52, 0.30);   // tray-out toward the viewer (down-left)

        double bw = w * 0.60;                 // body width
        double bh = h * 0.16;                 // front-face height (drive thickness)
        double bd = h * 0.44;                 // body depth
        var P0 = new Point(w * 0.30, h * 0.30);  // front-face top-left

        Point F(double x, double y) => new Point(P0.X + x * R.X + y * Dn.X, P0.Y + x * R.Y + y * Dn.Y);
        Point Depth(Point p, double z) => new Point(p.X + z * Dv.X, p.Y + z * Dv.Y);

        Point flT = F(0, 0), frT = F(bw, 0), frB = F(bw, bh), flB = F(0, bh);
        Point flT2 = Depth(flT, bd), frT2 = Depth(frT, bd), frB2 = Depth(frB, bd);

        // --- drive body (static): top face, right side, front bezel ---
        var topBrush = new LinearGradientBrush(Color.FromRgb(0x24, 0x2a, 0x25), Color.FromRgb(0x18, 0x1d, 0x19), 90);
        topBrush.Freeze();
        Poly(dc, topBrush, EdgePen, flT, frT, frT2, flT2);

        var sideBrush = new SolidColorBrush(Color.FromRgb(0x12, 0x16, 0x13)); sideBrush.Freeze();
        Poly(dc, sideBrush, EdgePen, frT, frB, frB2, frT2);

        var faceBrush = new LinearGradientBrush(Color.FromRgb(0x1d, 0x22, 0x1e), Color.FromRgb(0x0e, 0x12, 0x0f), 90);
        faceBrush.Freeze();
        Poly(dc, faceBrush, EdgePen, flT, frT, frB, flB);

        // slot across the front face, a little above the bottom edge
        double slotY = bh * 0.60;
        Point slotL = F(bw * 0.09, slotY), slotR = F(bw * 0.91, slotY);
        var slotPen = new Pen(new SolidColorBrush(Color.FromRgb(0x04, 0x06, 0x05)), 2.4); slotPen.Freeze();
        dc.DrawLine(slotPen, slotL, slotR);

        // --- tray (slides out toward the viewer along Tout) ---
        double extend = open * (h * 0.92);
        if (extend > 1.5)
        {
            Vector outv = new Vector(Tout.X * extend, Tout.Y * extend);
            Point tlB = slotL, trB = slotR;
            Point tlF = tlB + outv, trF = trB + outv;

            // shadow cast on the ground, offset straight down and softened by openness
            var sh = new SolidColorBrush(Color.FromArgb((byte)(60 * open), 0, 0, 0)); sh.Freeze();
            Poly(dc, sh, null, Add(tlB, 3, 8), Add(trB, 3, 8), Add(trF, 3, 8), Add(tlF, 3, 8));

            // tray front lip (thickness) then the top surface the disc rides on
            double lip = 7;
            var lipBrush = new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x11)); lipBrush.Freeze();
            Poly(dc, lipBrush, EdgePen, tlF, trF, Add(trF, 0, lip), Add(tlF, 0, lip));

            var trayBrush = new LinearGradientBrush(Color.FromRgb(0x30, 0x37, 0x31), Color.FromRgb(0x1b, 0x20, 0x1c), Tout.Y > 0 ? 60 : 120);
            trayBrush.Freeze();
            var trayEdge = new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 0.8); trayEdge.Freeze();
            Poly(dc, trayBrush, trayEdge, tlB, trB, trF, tlF);

            // disc on the tray top surface, drawn in unit space mapped through the (R, Tout) basis
            double discR = Math.Min((trB - tlB).Length, outv.Length) * 0.40;
            if (discR > 5 && HasDisc)
            {
                Point centre = Mid(tlB, trB) + new Vector(outv.X * 0.52, outv.Y * 0.52);
                Vector ux = R * discR;                    // disc local x-axis -> tray width
                Vector uy = new Vector(Tout.X, Tout.Y) * discR;  // disc local y-axis -> tray length
                var m = new Matrix(ux.X, ux.Y, uy.X, uy.Y, centre.X, centre.Y);
                dc.PushTransform(new MatrixTransform(m));

                var body = new RadialGradientBrush { GradientOrigin = new Point(0.38, 0.36) };
                body.GradientStops.Add(new GradientStop(Color.FromRgb(0x1c, 0x24, 0x20), 0));
                body.GradientStops.Add(new GradientStop(Color.FromRgb(0x0c, 0x10, 0x0e), 0.72));
                body.GradientStops.Add(new GradientStop(Color.FromRgb(0x06, 0x09, 0x07), 1));
                body.Freeze();
                dc.DrawEllipse(body, null, new Point(0, 0), 1, 1);

                var ring = new Pen(new SolidColorBrush(Color.FromArgb(52, Teal.R, Teal.G, Teal.B)), 0.05); ring.Freeze();
                dc.DrawEllipse(null, ring, new Point(0, 0), 0.74, 0.74);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x0d, 0x12, 0x0f)), null, new Point(0, 0), 0.30, 0.30);
                var hub = new Pen(new SolidColorBrush(Color.FromArgb(120, Teal.R, Teal.G, Teal.B)), 0.05); hub.Freeze();
                dc.DrawEllipse(null, hub, new Point(0, 0), 0.30, 0.30);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x04, 0x06, 0x05)), null, new Point(0, 0), 0.13, 0.13);
                dc.Pop();
            }
        }

        // --- bezel details: eject button + activity LED (amber while the tray is out) ---
        Point btn = F(bw * 0.80, bh * 0.30);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x2a, 0x30, 0x2b)), null, new Rect(btn.X, btn.Y, 13, 8), 2, 2);
        Color lc = open > 0.02 ? Amber : Teal;
        Point led = F(bw * 0.12, bh * 0.34);
        var glow = new RadialGradientBrush(); glow.GradientStops.Add(new GradientStop(lc, 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, lc.R, lc.G, lc.B), 1)); glow.Freeze();
        dc.DrawEllipse(glow, null, led, 7, 7);
        dc.DrawEllipse(new SolidColorBrush(lc), null, led, 2.3, 2.3);
    }

    private static readonly Pen EdgePen = MakeEdgePen();
    private static Pen MakeEdgePen()
    {
        var p = new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), 0.8);
        p.Freeze();
        return p;
    }

    private static Point Add(Point p, double dx, double dy) => new Point(p.X + dx, p.Y + dy);
    private static Point Mid(Point a, Point b) => new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private static void Poly(DrawingContext dc, Brush fill, Pen pen, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true, true);
            for (int i = 1; i < pts.Length; i++) c.LineTo(pts[i], true, false);
        }
        g.Freeze();
        dc.DrawGeometry(fill, pen, g);
    }
}
