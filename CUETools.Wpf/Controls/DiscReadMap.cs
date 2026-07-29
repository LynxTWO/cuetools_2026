using System;
using System.Windows;
using System.Windows.Media;
using CUETools.Wpf.Services;

namespace CUETools.Wpf.Controls;

/// <summary>
/// The disc read-map. A CD is read as one spiral from the centre outward, so it fills
/// inside-out: the green region grows to <see cref="Progress"/> and the teal pickup sits at
/// that radius. Retained-mode WPF drawing (GPU-composited), animated via
/// CompositionTarget.Rendering. In Phase 3 this binds to the live ReadProgress stream
/// (Position -> Progress). Radius is the accurate axis; the visible spin is slowed
/// for legibility while retaining the inner-faster CLV relationship.
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
        _angle +=
            dt *
            DiscModel3D.VisualSpinDegreesPerSecond(Progress) *
            Math.PI /
            180.0;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var c = new Point(w / 2, h / 2);
        double radius = Math.Min(w, h) * 0.47;
        double holeRadius = radius * 7.5 / 60.0;
        double stackRadius = radius * 16.5 / 60.0;
        double dataInner = radius * 25.0 / 60.0;
        double dataOuter = radius * 58.0 / 60.0;

        Color ground = ThemeColor.Get(
            this,
            "Ground",
            Color.FromRgb(0x0C, 0x0F, 0x0D));
        Color data = ThemeColor.Get(
            this,
            "DiscData",
            Color.FromRgb(0x93, 0xA3, 0x9F));
        Color hub = ThemeColor.Get(
            this,
            "DiscHub",
            Color.FromRgb(0xBC, 0xC8, 0xC4));
        Color edge = ThemeColor.Get(
            this,
            "DiscEdge",
            Color.FromRgb(0xDD, 0xE6, 0xE2));
        Color back = ThemeColor.Get(
            this,
            "DiscBack",
            Color.FromRgb(0x30, 0x3A, 0x36));

        var body = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.38, 0.34),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        body.GradientStops.Add(new GradientStop(hub, 0));
        body.GradientStops.Add(new GradientStop(data, 0.48));
        body.GradientStops.Add(new GradientStop(Blend(data, back, 0.26), 0.78));
        body.GradientStops.Add(new GradientStop(back, 1));
        body.Freeze();
        var outerPen = new Pen(new SolidColorBrush(edge), Math.Max(1, radius * 0.012));
        outerPen.Freeze();
        dc.DrawEllipse(body, outerPen, c, radius, radius);

        // The fallback mirrors the 3D model's representative spiral and narrow
        // diffraction arcs. It never claims these are the disc's literal EFM bits.
        dc.PushTransform(new RotateTransform(_angle * 180.0 / Math.PI, c.X, c.Y));
        var ringPen = new Pen(
            new SolidColorBrush(Color.FromArgb(34, 0xF0, 0xFA, 0xF6)),
            Math.Max(0.55, radius * 0.004));
        ringPen.Freeze();
        for (double r = dataInner; r < dataOuter; r += Math.Max(2.2, radius * 0.018))
            dc.DrawEllipse(null, ringPen, c, r, r);
        DrawArc(
            dc,
            c,
            radius * 0.77,
            -38,
            84,
            Color.FromArgb(82, 0x5A, 0xE5, 0xD9),
            radius * 0.030);
        DrawArc(
            dc,
            c,
            radius * 0.82,
            -30,
            70,
            Color.FromArgb(68, 0x8A, 0x86, 0xF2),
            radius * 0.025);
        DrawArc(
            dc,
            c,
            radius * 0.72,
            142,
            72,
            Color.FromArgb(60, 0xEE, 0xB4, 0x52),
            radius * 0.020);
        dc.Pop();

        double readR =
            radius *
            DiscModel3D.DataRadius(Progress) /
            60.0;
        var ripped = new CombinedGeometry(GeometryCombineMode.Exclude,
            new EllipseGeometry(c, readR, readR),
            new EllipseGeometry(c, dataInner, dataInner));
        ripped.Freeze();
        var green = new SolidColorBrush(Color.FromArgb(40, Good.R, Good.G, Good.B));
        green.Freeze();
        dc.DrawGeometry(green, null, ripped);
        var leadPen = new Pen(new SolidColorBrush(Color.FromArgb(150, Good.R, Good.G, Good.B)), 1.4);
        leadPen.Freeze();
        dc.DrawEllipse(null, leadPen, c, readR, readR);

        // Pickup lens at a fixed chassis angle while the medium rotates above it.
        const double pa = 0.5;
        var head = new Point(c.X + Math.Cos(pa) * readR, c.Y + Math.Sin(pa) * readR);
        var glow = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(190, Teal.R, Teal.G, Teal.B), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, Teal.R, Teal.G, Teal.B), 1));
        glow.Freeze();
        dc.DrawEllipse(glow, null, head, 13, 13);
        dc.DrawEllipse(
            new SolidColorBrush(Color.FromRgb(0xFF, 0x6A, 0x58)),
            null,
            head,
            2.4,
            2.4);

        // Clear hub, stack ring, and centre hole retain the real radial proportions.
        var hubBrush = new RadialGradientBrush(hub, Color.FromArgb(150, hub.R, hub.G, hub.B));
        hubBrush.Freeze();
        dc.DrawEllipse(hubBrush, null, c, dataInner, dataInner);
        var hubPen = new Pen(new SolidColorBrush(edge), Math.Max(1, radius * 0.008));
        hubPen.Freeze();
        dc.DrawEllipse(null, hubPen, c, dataInner, dataInner);
        dc.DrawEllipse(null, hubPen, c, stackRadius, stackRadius);
        dc.DrawEllipse(
            new SolidColorBrush(ground),
            hubPen,
            c,
            holeRadius,
            holeRadius);
    }

    private static void DrawArc(
        DrawingContext dc,
        Point centre,
        double radius,
        double startDegrees,
        double sweepDegrees,
        Color color,
        double thickness)
    {
        double start = startDegrees * Math.PI / 180.0;
        double end = (startDegrees + sweepDegrees) * Math.PI / 180.0;
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(
                new Point(
                    centre.X + Math.Cos(start) * radius,
                    centre.Y + Math.Sin(start) * radius),
                isFilled: false,
                isClosed: false);
            context.ArcTo(
                new Point(
                    centre.X + Math.Cos(end) * radius,
                    centre.Y + Math.Sin(end) * radius),
                new Size(radius, radius),
                rotationAngle: 0,
                isLargeArc: Math.Abs(sweepDegrees) > 180,
                sweepDirection: sweepDegrees >= 0
                    ? SweepDirection.Clockwise
                    : SweepDirection.Counterclockwise,
                isStroked: true,
                isSmoothJoin: true);
        }
        geometry.Freeze();
        var pen = new Pen(new SolidColorBrush(color), Math.Max(0.7, thickness))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        pen.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static Color Blend(Color a, Color b, double amount)
    {
        amount = Math.Max(0, Math.Min(1, amount));
        return Color.FromRgb(
            (byte)Math.Round(a.R + (b.R - a.R) * amount),
            (byte)Math.Round(a.G + (b.G - a.G) * amount),
            (byte)Math.Round(a.B + (b.B - a.B) * amount));
    }
}
