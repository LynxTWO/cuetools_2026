using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CUETools.Wpf.Controls;

/// <summary>
/// An educational, GPU-drawn view of what a codec actually does to the audio while encoding.
/// For a lossless prediction codec (FLAC / ALAC / TTA) it animates the real pipeline: the signal,
/// a predictor tracking it, the small residual left over, and entropy (Rice) packing that shrinks
/// it. For uncompressed PCM (WAV) it shows the signal stored 1:1. The compression figure is
/// labelled "typical" - it is illustrative, not a measurement of the current disc.
/// </summary>
public sealed class CodecScope : FrameworkElement
{
    public static readonly DependencyProperty CodecProperty = DependencyProperty.Register(
        nameof(Codec), typeof(string), typeof(CodecScope), new FrameworkPropertyMetadata("flac", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(
        nameof(Active), typeof(bool), typeof(CodecScope), new PropertyMetadata(false));

    public string Codec { get => (string)GetValue(CodecProperty); set => SetValue(CodecProperty, value); }
    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }

    private static readonly Color Teal = Color.FromRgb(0x34, 0xCF, 0xC0);
    private static readonly Color Amber = Color.FromRgb(0xE9, 0xA6, 0x3F);
    private static readonly Color Ink = Color.FromRgb(0xED, 0xF1, 0xE9);
    private static readonly Color Muted = Color.FromRgb(0x7D, 0x88, 0x7C);
    private static readonly Color Line = Color.FromRgb(0x28, 0x31, 0x2A);
    private static readonly Typeface Face = new("Segoe UI");
    private static readonly Typeface Mono = new("Cascadia Mono, Consolas");

    private double _phase;
    private TimeSpan _last;

    public CodecScope()
    {
        Loaded += (_, _) => CompositionTarget.Rendering += OnRendering;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var t = ((RenderingEventArgs)e).RenderingTime;
        double dt = _last == default ? 0 : (t - _last).TotalSeconds;
        _last = t;
        _phase += dt * (Active ? 3.2 : 1.1);   // flows faster while actually encoding
        InvalidateVisual();
    }

    private static (string name, string desc, bool compressed, double ratio) Info(string codec) => (codec ?? "").ToLowerInvariant() switch
    {
        "wav" => ("WAV", "uncompressed PCM - every sample kept exactly", false, 1.0),
        "flac" => ("FLAC", "fit a linear predictor, then Rice-code the residual", true, 0.58),
        "m4a" or "alac" => ("ALAC", "adaptive FIR prediction + Rice/Golomb (Apple Lossless)", true, 0.62),
        "tta" => ("TTA", "adaptive prediction + Rice coding (True Audio)", true, 0.62),
        "ape" => ("Monkey's Audio", "high-order adaptive prediction + range coding", true, 0.55),
        "wv" => ("WavPack", "decorrelation + entropy coding", true, 0.60),
        _ => ((codec ?? "").ToUpperInvariant(), "lossless prediction + entropy coding", true, 0.60),
    };

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var info = Info(Codec);

        // header: codec name + one-line description
        Text(dc, info.name, new Point(2, 0), Mono, 14, Ink, true);
        Text(dc, info.desc, new Point(2, 20), Face, 11, Muted, false);

        double top = 44, bh = h - top - 22;
        if (bh < 30) return;
        string[] stages = info.compressed
            ? new[] { "signal", "predict", "residual", "pack" }
            : new[] { "signal", "store" };

        double gap = 12;
        double sw = (w - gap * (stages.Length - 1)) / stages.Length;
        for (int i = 0; i < stages.Length; i++)
        {
            double x = i * (sw + gap);
            var r = new Rect(x, top, sw, bh);
            DrawCard(dc, r);
            DrawStage(dc, stages[i], r, info);
            Text(dc, stages[i], new Point(x + 8, top + bh + 4), Mono, 9.5, Muted, false);
            if (i < stages.Length - 1) Arrow(dc, new Point(x + sw + 1, top + bh / 2), new Point(x + sw + gap - 1, top + bh / 2));
        }

        // compression readout (right-aligned under the header)
        string ratio = info.compressed ? $"typical ~{info.ratio * 100:0}% of PCM" : "1:1 - no compression";
        var ft = MakeText(ratio, Mono, 11, info.compressed ? Teal : Amber);
        dc.DrawText(ft, new Point(w - ft.Width - 2, 22));
    }

    private void DrawStage(DrawingContext dc, string stage, Rect r, (string name, string desc, bool compressed, double ratio) info)
    {
        double midY = r.Top + r.Height / 2;
        dc.PushClip(new RectangleGeometry(r, 8, 8));
        switch (stage)
        {
            case "signal":
                Wave(dc, r, midY, r.Height * 0.34, 2.4, Teal, 1.6, 0);
                break;
            case "predict":
                Wave(dc, r, midY, r.Height * 0.34, 2.4, Color.FromArgb(70, Teal.R, Teal.G, Teal.B), 1.4, 0);   // signal, faint
                Wave(dc, r, midY, r.Height * 0.34, 2.4, Amber, 1.6, 0.18);                                    // predictor, tracking
                break;
            case "residual":
                Wave(dc, r, midY, r.Height * 0.10, 5.5, Teal, 1.4, 0);   // small, jagged - what is left to store
                break;
            case "pack":
                PackBars(dc, r, info.ratio);
                break;
            case "store":
                Wave(dc, r, midY, r.Height * 0.34, 2.4, Teal, 1.6, 0);
                // "1:1" container ticks along the bottom
                var tick = new Pen(new SolidColorBrush(Color.FromArgb(90, Muted.R, Muted.G, Muted.B)), 1);
                tick.Freeze();
                for (double bx = r.Left + 6; bx < r.Right - 4; bx += 7)
                    dc.DrawLine(tick, new Point(bx, r.Bottom - 8), new Point(bx, r.Bottom - 4));
                break;
        }
        dc.Pop();
    }

    // one sine-ish wave, scrolling with _phase; predShift blends toward a smoother "prediction".
    private void Wave(DrawingContext dc, Rect r, double midY, double amp, double freq, Color c, double thick, double smooth)
    {
        var fig = new PathFigure { IsClosed = false };
        bool first = true;
        int n = Math.Max(8, (int)(r.Width / 2));
        for (int i = 0; i <= n; i++)
        {
            double u = (double)i / n;
            double px = r.Left + 6 + u * (r.Width - 12);
            double a = (u * freq * Math.PI * 2) + _phase * 2.0;
            double raw = Math.Sin(a) + 0.35 * Math.Sin(a * 2.7 + 1) + 0.2 * Math.Sin(a * 5.1 + 2);
            double sm = Math.Sin(a);                 // a smoother "predicted" version
            double v = raw * (1 - smooth) + sm * smooth;
            double py = midY - v * amp;
            if (first) { fig.StartPoint = new Point(px, py); first = false; }
            else fig.Segments.Add(new LineSegment(new Point(px, py), true));
        }
        var geo = new PathGeometry(new[] { fig });
        geo.Freeze();
        var pen = new Pen(new SolidColorBrush(c), thick) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    // entropy-coding bars: raw byte columns collapsing to the compressed size.
    private void PackBars(DrawingContext dc, Rect r, double ratio)
    {
        int cols = 9;
        double pad = 8, bw = (r.Width - pad * 2) / (cols * 1.7);
        double baseY = r.Bottom - 8, fullH = r.Height - 18;
        var raw = new SolidColorBrush(Color.FromArgb(60, Teal.R, Teal.G, Teal.B)); raw.Freeze();
        var packed = new SolidColorBrush(Teal); packed.Freeze();
        for (int i = 0; i < cols; i++)
        {
            double x = r.Left + pad + i * bw * 1.7;
            double jitter = 0.8 + 0.2 * Math.Sin(_phase * 3 + i);
            dc.DrawRectangle(raw, null, new Rect(x, baseY - fullH * jitter, bw, fullH * jitter));           // raw column
            double ph = fullH * ratio * (0.85 + 0.15 * Math.Sin(_phase * 4 + i * 0.7));
            dc.DrawRectangle(packed, null, new Rect(x, baseY - ph, bw, ph));                                 // packed column
        }
    }

    private static void DrawCard(DrawingContext dc, Rect r)
    {
        var bg = new SolidColorBrush(Color.FromArgb(80, 14, 19, 17)); bg.Freeze();
        var edge = new Pen(new SolidColorBrush(Line), 1); edge.Freeze();
        dc.DrawRoundedRectangle(bg, edge, r, 8, 8);
    }

    private static void Arrow(DrawingContext dc, Point a, Point b)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(140, 125, 136, 124)), 1.4) { EndLineCap = PenLineCap.Round };
        pen.Freeze();
        dc.DrawLine(pen, a, b);
        dc.DrawLine(pen, b, new Point(b.X - 4, b.Y - 3));
        dc.DrawLine(pen, b, new Point(b.X - 4, b.Y + 3));
    }

    private static FormattedText MakeText(string s, Typeface tf, double size, Color c)
        => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, size, new SolidColorBrush(c), 1.0);

    private static void Text(DrawingContext dc, string s, Point p, Typeface tf, double size, Color c, bool bold)
    {
        var ft = MakeText(s, bold ? new Typeface(tf.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal) : tf, size, c);
        dc.DrawText(ft, p);
    }
}
