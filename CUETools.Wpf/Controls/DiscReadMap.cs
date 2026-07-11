using System;
using System.Windows;
using System.Windows.Media;

namespace CUETools.Wpf.Controls;

/// <summary>
/// The disc read-map. A CD is read as one spiral from the centre outward, so it fills
/// inside-out: the green region grows to <see cref="Progress"/> and the teal pickup sits at
/// that radius. Retained-mode WPF drawing (GPU-composited), animated via
/// CompositionTarget.Rendering. In Phase 3 this binds to the live ReadProgress stream
/// (Position -> Progress, RetryCount/FailedSectors -> the amber marks); for now it spins as
/// an idle demo. Radius is the accurate axis (time into the disc); the spin is representational.
/// </summary>
public sealed class DiscReadMap : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(DiscReadMap),
        new FrameworkPropertyMetadata(0.27, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public static readonly DependencyProperty SpinningProperty = DependencyProperty.Register(
        nameof(Spinning), typeof(bool), typeof(DiscReadMap),
        new PropertyMetadata(true));

    public bool Spinning
    {
        get => (bool)GetValue(SpinningProperty);
        set => SetValue(SpinningProperty, value);
    }

    // palette (matches the app theme)
    private static readonly Color Teal = Color.FromRgb(0x34, 0xcf, 0xc0);
    private static readonly Color Good = Color.FromRgb(0x5c, 0xcb, 0x8b);

    private double _angle;
    private TimeSpan _last;

    public DiscReadMap()
    {
        Loaded += (_, _) => CompositionTarget.Rendering += OnRendering;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!Spinning) return;
        var t = ((RenderingEventArgs)e).RenderingTime;
        double dt = _last == default ? 0 : (t - _last).TotalSeconds;
        _last = t;
        _angle += dt * 0.55;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var c = new Point(w / 2, h / 2);
        double R = Math.Min(w, h) * 0.47, Ri = R * 0.30, Ro = R * 0.9;

        // disc body
        var body = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
        body.GradientStops.Add(new GradientStop(Color.FromRgb(0x0b, 0x0f, 0x0d), 0));
        body.GradientStops.Add(new GradientStop(Color.FromRgb(0x16, 0x1d, 0x18), 0.55));
        body.GradientStops.Add(new GradientStop(Color.FromRgb(0x09, 0x0c, 0x0a), 1));
        body.Freeze();
        dc.DrawEllipse(body, null, c, R, R);

        // rotating data rings (the platter spinning)
        dc.PushTransform(new RotateTransform(_angle * 180.0 / Math.PI, c.X, c.Y));
        var ringPen = new Pen(new SolidColorBrush(Color.FromArgb(12, 255, 255, 255)), 1);
        ringPen.Freeze();
        for (double r = R * 0.32; r < Ro; r += 2.6) dc.DrawEllipse(null, ringPen, c, r, r);
        dc.Pop();

        // read-map: green annulus Ri..readR (static - radius = progress into the disc)
        double readR = Ri + Math.Max(0, Math.Min(1, Progress)) * (Ro - Ri);
        var ripped = new CombinedGeometry(GeometryCombineMode.Exclude,
            new EllipseGeometry(c, readR, readR),
            new EllipseGeometry(c, Ri, Ri));
        ripped.Freeze();
        var green = new SolidColorBrush(Color.FromArgb(40, Good.R, Good.G, Good.B));
        green.Freeze();
        dc.DrawGeometry(green, null, ripped);
        var leadPen = new Pen(new SolidColorBrush(Color.FromArgb(150, Good.R, Good.G, Good.B)), 1.4);
        leadPen.Freeze();
        dc.DrawEllipse(null, leadPen, c, readR, readR);

        // pickup lens at a fixed angle, at the current read radius
        const double pa = 0.5;
        var head = new Point(c.X + Math.Cos(pa) * readR, c.Y + Math.Sin(pa) * readR);
        var glow = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(190, Teal.R, Teal.G, Teal.B), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, Teal.R, Teal.G, Teal.B), 1));
        glow.Freeze();
        dc.DrawEllipse(glow, null, head, 13, 13);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xea, 0xff, 0xfb)), null, head, 2.6, 2.6);

        // hub + spindle
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x0d, 0x12, 0x0f)), null, c, Ri, Ri);
        var hubPen = new Pen(new SolidColorBrush(Color.FromArgb(120, Teal.R, Teal.G, Teal.B)), 1.4);
        hubPen.Freeze();
        dc.DrawEllipse(null, hubPen, c, Ri, Ri);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x05, 0x08, 0x06)), null, c, R * 0.15, R * 0.15);
    }
}
