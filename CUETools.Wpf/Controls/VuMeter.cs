using System;
using System.Windows;
using System.Windows.Media;

namespace CUETools.Wpf.Controls;

/// <summary>
/// A pair of analog VU meters (L/R), amber needles on a tick scale, GPU-composited. Driven by
/// REAL per-channel RMS loudness (<see cref="LevelL"/> / <see cref="LevelR"/>, 0..1 linear) tapped
/// from the disc audio as it is read. RMS, not peak: peak pins near full-scale for any loud music,
/// so the needle would sit frozen at the top; RMS moves with the loudness. Classic meter
/// ballistics: fast attack, slow decay, so the needle rises to a level and drifts back. When
/// <see cref="Active"/> is false (nothing ripping) the levels are zero and the needles sit at rest.
/// </summary>
public sealed class VuMeter : FrameworkElement
{
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(
        nameof(Active), typeof(bool), typeof(VuMeter), new PropertyMetadata(false));
    public static readonly DependencyProperty LevelLProperty = DependencyProperty.Register(
        nameof(LevelL), typeof(double), typeof(VuMeter), new PropertyMetadata(0.0));
    public static readonly DependencyProperty LevelRProperty = DependencyProperty.Register(
        nameof(LevelR), typeof(double), typeof(VuMeter), new PropertyMetadata(0.0));

    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }
    public double LevelL { get => (double)GetValue(LevelLProperty); set => SetValue(LevelLProperty, value); }
    public double LevelR { get => (double)GetValue(LevelRProperty); set => SetValue(LevelRProperty, value); }

    private static readonly Color Amber = Color.FromRgb(0xE9, 0xA6, 0x3F);
    private static readonly Color Crit = Color.FromRgb(0xEF, 0x6D, 0x6D);
    private const double Attack = 0.35, Decay = 0.06;

    private double _l, _r;   // smoothed needle deflection 0..1
    private double _lastL = -1, _lastR = -1;
    private int _staleL, _staleR;

    public VuMeter()
    {
        Loaded += (_, _) => CompositionTarget.Rendering += OnRendering;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // Levels arrive one per disc read, which in secure mode comes in bursts with multi-second
        // gaps while the drive re-reads a hard sector. If the level has not changed for a few
        // frames the read has paused, so let the needle RELEASE (decay) like a real analog VU
        // instead of freezing at the last value.
        if (LevelL != _lastL) { _lastL = LevelL; _staleL = 0; } else _staleL++;
        if (LevelR != _lastR) { _lastR = LevelR; _staleR = 0; } else _staleR++;
        double tL = Active ? Deflection(LevelL) * Stale(_staleL) : 0;
        double tR = Active ? Deflection(LevelR) * Stale(_staleR) : 0;
        _l += (tL - _l) * (tL > _l ? Attack : Decay);
        _r += (tR - _r) * (tR > _r ? Attack : Decay);
        // stop repainting once fully settled at rest
        if (_l < 0.0005 && _r < 0.0005 && tL == 0 && tR == 0) { _l = _r = 0; return; }
        InvalidateVisual();
    }

    // 1.0 while levels are fresh; eases toward 0 after ~100ms without an update (a read gap),
    // so the needle drifts down naturally during a pause instead of holding.
    private static double Stale(int frames) => frames <= 6 ? 1.0 : Math.Pow(0.94, frames - 6);

    // Map a 0..1 linear peak onto needle deflection with a dB-like VU scale (-40 dB .. 0 dB).
    private static double Deflection(double level)
    {
        if (level <= 0) return 0;
        double db = 20.0 * Math.Log10(Math.Min(1.0, level));
        return Math.Max(0, Math.Min(1, (db + 40.0) / 40.0));
    }

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
        const double a0 = -Math.PI * 0.75, a1 = -Math.PI * 0.25;

        var fig = new PathFigure { StartPoint = Pt(cx, cy, R, a0), IsClosed = false };
        for (int i = 1; i <= 24; i++)
            fig.Segments.Add(new LineSegment(Pt(cx, cy, R, a0 + (a1 - a0) * i / 24.0), true));
        var geo = new PathGeometry(new[] { fig });
        geo.Freeze();
        var arcPen = new Pen(new SolidColorBrush(Color.FromArgb(40, Amber.R, Amber.G, Amber.B)), Math.Max(2, R * 0.16)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        arcPen.Freeze();
        dc.DrawGeometry(null, arcPen, geo);

        for (int i = 0; i <= 10; i++)
        {
            double a = a0 + (a1 - a0) * i / 10.0;
            bool over = i > 7;
            var pen = new Pen(new SolidColorBrush(over ? Crit : Color.FromArgb(140, 210, 216, 200)), i % 5 == 0 ? 1.6 : 1);
            pen.Freeze();
            dc.DrawLine(pen, Pt(cx, cy, R - R * 0.06, a), Pt(cx, cy, R + R * 0.06, a));
        }

        double na = a0 + (a1 - a0) * Math.Max(0, Math.Min(1, val));
        var tip = Pt(cx, cy, R + R * 0.04, na);
        var glow = new Pen(new SolidColorBrush(Color.FromArgb(90, Amber.R, Amber.G, Amber.B)), 4.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        glow.Freeze();
        dc.DrawLine(glow, new Point(cx, cy), tip);
        var needle = new Pen(new SolidColorBrush(Amber), 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        needle.Freeze();
        dc.DrawLine(needle, new Point(cx, cy), tip);
        dc.DrawEllipse(new SolidColorBrush(Amber), null, new Point(cx, cy), 2.6, 2.6);

        var ft = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Consolas"), 10, new SolidColorBrush(Color.FromArgb(180, 200, 208, 196)), 1.0);
        dc.DrawText(ft, new Point(cx - ft.Width / 2, cy + R * 0.05));
    }

    private static Point Pt(double cx, double cy, double r, double a) => new(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r);
}
