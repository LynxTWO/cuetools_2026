using System;
using System.Windows;
using System.Windows.Media;

namespace CUETools.Wpf.Controls;

/// <summary>
/// A pair of analog VU meters (L/R), amber needles on a tick scale. Retained-mode WPF
/// drawing, GPU-composited. Idle demo animation for now; binds to real read levels later.
/// Drawn from computed tick points + needle lines (no arc geometry) so it renders predictably.
/// </summary>
public sealed class VuMeter : FrameworkElement
{
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(
        nameof(Active), typeof(bool), typeof(VuMeter), new PropertyMetadata(true));

    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }

    private static readonly Color Amber = Color.FromRgb(0xE9, 0xA6, 0x3F);
    private static readonly Color Crit = Color.FromRgb(0xEF, 0x6D, 0x6D);

    private double _l = 0.5, _r = 0.42;
    private TimeSpan _last;
    private readonly Random _rng = new();

    public VuMeter()
    {
        Loaded += (_, _) => CompositionTarget.Rendering += OnRendering;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var t = ((RenderingEventArgs)e).RenderingTime;
        double dt = _last == default ? 0 : (t - _last).TotalSeconds;
        _last = t;
        double amt = Active ? 8.0 * dt : 2.0 * dt;
        _l = Clamp(_l + (_rng.NextDouble() - 0.5) * amt);
        _r = Clamp(_r + (_rng.NextDouble() - 0.5) * amt);
        InvalidateVisual();
    }

    private static double Clamp(double v) => Math.Max(0.18, Math.Min(0.92, v));

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double R = Math.Min(w * 0.2, h * 0.66);
        double cy = h * 0.86;
        Meter(dc, w * 0.27, cy, R, "L", _l);
        Meter(dc, w * 0.73, cy, R, "R", _r);
    }

    private void Meter(DrawingContext dc, double cx, double cy, double R, string label, double val)
    {
        const double a0 = -Math.PI * 0.75, a1 = -Math.PI * 0.25; // sweep across the top, left=0 to right=1

        // faint backing arc as a sampled polyline
        var fig = new PathFigure { StartPoint = Pt(cx, cy, R, a0), IsClosed = false };
        for (int i = 1; i <= 24; i++)
            fig.Segments.Add(new LineSegment(Pt(cx, cy, R, a0 + (a1 - a0) * i / 24.0), true));
        var geo = new PathGeometry(new[] { fig });
        geo.Freeze();
        var arcPen = new Pen(new SolidColorBrush(Color.FromArgb(40, Amber.R, Amber.G, Amber.B)), Math.Max(2, R * 0.16)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        arcPen.Freeze();
        dc.DrawGeometry(null, arcPen, geo);

        // ticks
        for (int i = 0; i <= 10; i++)
        {
            double a = a0 + (a1 - a0) * i / 10.0;
            bool over = i > 7;
            var pen = new Pen(new SolidColorBrush(over ? Crit : Color.FromArgb(140, 210, 216, 200)), i % 5 == 0 ? 1.6 : 1);
            pen.Freeze();
            dc.DrawLine(pen, Pt(cx, cy, R - R * 0.06, a), Pt(cx, cy, R + R * 0.06, a));
        }

        // needle (glow under, bright over) + pivot
        double na = a0 + (a1 - a0) * Math.Max(0, Math.Min(1, val));
        var tip = Pt(cx, cy, R + R * 0.04, na);
        var glow = new Pen(new SolidColorBrush(Color.FromArgb(90, Amber.R, Amber.G, Amber.B)), 4.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        glow.Freeze();
        dc.DrawLine(glow, new Point(cx, cy), tip);
        var needle = new Pen(new SolidColorBrush(Amber), 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        needle.Freeze();
        dc.DrawLine(needle, new Point(cx, cy), tip);
        dc.DrawEllipse(new SolidColorBrush(Amber), null, new Point(cx, cy), 2.6, 2.6);

        // label
        var ft = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Consolas"), 10, new SolidColorBrush(Color.FromArgb(180, 200, 208, 196)), 1.0);
        dc.DrawText(ft, new Point(cx - ft.Width / 2, cy + R * 0.05));
    }

    private static Point Pt(double cx, double cy, double r, double a) => new(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r);
}
