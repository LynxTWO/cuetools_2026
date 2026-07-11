using System;
using System.Windows;
using System.Windows.Media;

namespace CUETools.Wpf.Controls;

/// <summary>
/// A live read-speed trace, drawn like a bench oscilloscope: a scrolling teal line + area fill
/// over a faint grid, newest sample on the right. Retained-mode WPF drawing, GPU-composited.
/// The view model feeds it <see cref="Level"/> (0..1 of the speed cap); the control samples that
/// at a fixed cadence into a rolling history so the trace scrolls smoothly regardless of how
/// often the ripper reports progress. <see cref="Active"/> gates the animation.
/// </summary>
public sealed class SpeedGraph : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(SpeedGraph), new PropertyMetadata(0.0));

    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(
        nameof(Active), typeof(bool), typeof(SpeedGraph), new PropertyMetadata(false));

    /// <summary>Current speed as a fraction 0..1 of the display cap.</summary>
    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }

    private static readonly Color Teal = Color.FromRgb(0x34, 0xCF, 0xC0);
    private const int N = 140;                 // samples held
    private const double Cadence = 0.14;       // seconds between samples (~20s of history)

    private readonly double[] _buf = new double[N];
    private TimeSpan _last;
    private double _accum;

    public SpeedGraph()
    {
        Loaded += (_, _) => CompositionTarget.Rendering += OnRendering;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var t = ((RenderingEventArgs)e).RenderingTime;
        double dt = _last == default ? 0 : (t - _last).TotalSeconds;
        _last = t;
        _accum += dt;
        if (_accum < Cadence) return;
        _accum = 0;

        // shift left, append newest on the right; decay to baseline when idle
        Array.Copy(_buf, 1, _buf, 0, N - 1);
        double target = Active ? Math.Max(0, Math.Min(1, Level)) : 0;
        _buf[N - 1] = _buf[N - 2] + (target - _buf[N - 2]) * 0.5;  // light smoothing
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        const double padT = 6, padB = 6;
        double gh = h - padT - padB;
        double Y(double v) => padT + gh * (1 - Math.Max(0, Math.Min(1, v)));

        // faint horizontal grid at 1/4, 1/2, 3/4
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(30, 210, 216, 200)), 1);
        grid.Freeze();
        for (int i = 1; i <= 3; i++)
            dc.DrawLine(grid, new Point(0, Y(i / 4.0)), new Point(w, Y(i / 4.0)));

        // build the trace geometry
        var line = new PathFigure { StartPoint = new Point(0, Y(_buf[0])), IsClosed = false };
        for (int i = 1; i < N; i++)
            line.Segments.Add(new LineSegment(new Point(w * i / (N - 1.0), Y(_buf[i])), true));
        var lineGeo = new PathGeometry(new[] { line });
        lineGeo.Freeze();

        // area fill under the trace
        var area = new PathFigure { StartPoint = new Point(0, h - padB), IsClosed = true };
        area.Segments.Add(new LineSegment(new Point(0, Y(_buf[0])), true));
        for (int i = 1; i < N; i++)
            area.Segments.Add(new LineSegment(new Point(w * i / (N - 1.0), Y(_buf[i])), true));
        area.Segments.Add(new LineSegment(new Point(w, h - padB), true));
        var areaGeo = new PathGeometry(new[] { area });
        areaGeo.Freeze();
        var fill = new LinearGradientBrush(
            Color.FromArgb(0x66, Teal.R, Teal.G, Teal.B),
            Color.FromArgb(0x00, Teal.R, Teal.G, Teal.B), 90);
        fill.Freeze();
        dc.DrawGeometry(fill, null, areaGeo);

        var pen = new Pen(new SolidColorBrush(Teal), 1.6) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, lineGeo);

        // bright endpoint dot with glow
        var end = new Point(w, Y(_buf[N - 1]));
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x55, Teal.R, Teal.G, Teal.B)), null, end, 5, 5);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xEA, 0xFF, 0xFB)), null, end, 2, 2);
    }
}
