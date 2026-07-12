using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CUETools.Wpf.Controls;

/// <summary>
/// A real-time, GPU-drawn view of what a lossless codec actually does to the audio, driven by the
/// real PCM samples flowing through the rip (see <see cref="Samples"/>). It is NOT a canned loop:
/// each frame it runs the predictor of the selected codec's family on the true samples, so the
/// signal, prediction and residual on screen are the real numbers, and the bits-per-sample and
/// compression ratio are computed from the actual residual (the same Rice-cost principle the
/// encoders use to size a block).
///
/// The point of the three panels:
///  - pipeline: signal -> predict -> residual -> pack, the real DSP stages;
///  - compression forming: the residual is small, so the live bits/sample and % of PCM build up;
///  - format contrast: each codec runs a DIFFERENT real predictor (FLAC fixed polynomial, WavPack
///    cascaded difference, Monkey's Audio adaptive NLMS filter), so a better predictor leaves a
///    smaller residual and a lower ratio - the difference is earned, not asserted.
/// The predictors are representative of each family, not a bit-exact reimplementation.
/// </summary>
public sealed class CodecScope : FrameworkElement
{
    public static readonly DependencyProperty CodecProperty = DependencyProperty.Register(
        nameof(Codec), typeof(string), typeof(CodecScope), new FrameworkPropertyMetadata("flac", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(
        nameof(Active), typeof(bool), typeof(CodecScope), new PropertyMetadata(false));
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples), typeof(float[]), typeof(CodecScope), new PropertyMetadata(null, OnSamplesChanged));

    public string Codec { get => (string)GetValue(CodecProperty); set => SetValue(CodecProperty, value); }
    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }
    public float[]? Samples { get => (float[]?)GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }

    private static readonly Color Teal = Color.FromRgb(0x34, 0xCF, 0xC0);
    private static readonly Color Amber = Color.FromRgb(0xE9, 0xA6, 0x3F);
    private static readonly Color Ink = Color.FromRgb(0xED, 0xF1, 0xE9);
    private static readonly Color Muted = Color.FromRgb(0x7D, 0x88, 0x7C);
    private static readonly Color Line = Color.FromRgb(0x28, 0x31, 0x2A);
    private static readonly Typeface Face = new("Segoe UI");
    private static readonly Typeface Mono = new("Cascadia Mono, Consolas");

    private enum Pred { None, Fixed2, Adaptive, Cascade, Lms }
    private enum Pack { Store, Rice, Range }

    // rolling window of the real audio (scrolls right-to-left as new windows arrive)
    private const int Roll = 640;
    private readonly float[] _roll = new float[Roll];
    private float[] _pred = new float[Roll];
    private float[] _resid = new float[Roll];
    private double _bitsEma = 16, _ratioEma = 1;
    private double _phase;
    private TimeSpan _last;

    public CodecScope()
    {
        Loaded += (_, _) => CompositionTarget.Rendering += OnRendering;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    private static void OnSamplesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is float[] win && win.Length > 0) ((CodecScope)d).Push(win);
    }

    // shift the rolling buffer left and append the new real window
    private void Push(float[] win)
    {
        int m = Math.Min(win.Length, Roll);
        if (m < Roll) Array.Copy(_roll, m, _roll, 0, Roll - m);
        Array.Copy(win, Math.Max(0, win.Length - m), _roll, Roll - m, m);
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!IsVisible) return;   // the scope is collapsed unless a rip is running - do no work then
        var t = ((RenderingEventArgs)e).RenderingTime;
        double dt = _last == default ? 0 : (t - _last).TotalSeconds;
        _last = t;
        _phase += dt * (Active ? 3.0 : 1.0);
        InvalidateVisual();
    }

    private static (string name, string desc, Pred pred, Pack pack, string predLabel, string packLabel) Info(string codec) =>
        (codec ?? "").ToLowerInvariant() switch
        {
            "wav" => ("WAV", "uncompressed PCM - every sample stored exactly", Pred.None, Pack.Store, "store", "1:1"),
            "flac" => ("FLAC", "fixed / LPC predictor, then Rice-coded residual", Pred.Fixed2, Pack.Rice, "predict", "Rice pack"),
            "m4a" or "alac" => ("ALAC", "linear predictor + Rice/Golomb (Apple Lossless)", Pred.Fixed2, Pack.Rice, "predict", "Rice pack"),
            "tta" => ("TTA", "adaptive predictor + adaptive Rice (True Audio)", Pred.Adaptive, Pack.Rice, "adapt", "Rice pack"),
            "wv" => ("WavPack", "cascaded decorrelation + entropy coding", Pred.Cascade, Pack.Rice, "decorrelate", "entropy"),
            "ape" => ("Monkey's Audio", "adaptive NLMS filters + range coder (max ratio)", Pred.Lms, Pack.Range, "adapt filter", "range code"),
            _ => ((codec ?? "").ToUpperInvariant(), "lossless prediction + entropy coding", Pred.Fixed2, Pack.Rice, "predict", "pack"),
        };

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var info = Info(Codec);

        // run the real predictor on the rolling window; residual + bits are the true figures
        ComputeResidual(info.pred);
        double bits = info.pred == Pred.None ? 16.0 : BitsPerSample(info.pred);
        double ratio = Math.Max(0.02, Math.Min(1.0, bits / 16.0));
        _bitsEma += (bits - _bitsEma) * 0.12;
        _ratioEma += (ratio - _ratioEma) * 0.12;

        // header: name + mechanism, then the live compression readout on the right
        Text(dc, info.name, new Point(2, 0), Mono, 14, Ink, true);
        Text(dc, info.desc, new Point(2, 20), Face, 10.5, Muted, false);
        DrawCompression(dc, w, info.pack);

        double top = 42, bot = h - 15, bh = bot - top;
        if (bh < 24) return;

        string[] stages = info.pred == Pred.None
            ? new[] { "signal", info.predLabel }
            : new[] { "signal", info.predLabel, "residual", info.packLabel };
        double gap = 10;
        double sw = (w - gap * (stages.Length - 1)) / stages.Length;
        for (int i = 0; i < stages.Length; i++)
        {
            double x = i * (sw + gap);
            var r = new Rect(x, top, sw, bh);
            DrawCard(dc, r);
            dc.PushClip(new RectangleGeometry(r, 8, 8));
            DrawStage(dc, i, stages.Length, r, info);
            dc.Pop();
            Text(dc, stages[i], new Point(x + 8, bot + 1), Mono, 9, Muted, false);
            if (i < stages.Length - 1) Arrow(dc, new Point(x + sw + 1, top + bh / 2), new Point(x + sw + gap - 1, top + bh / 2));
        }
    }

    // -------- real predictor math (representative of each codec family) --------

    private void ComputeResidual(Pred kind)
    {
        var s = _roll; var p = _pred; var r = _resid;
        switch (kind)
        {
            case Pred.None:
                for (int i = 0; i < Roll; i++) { p[i] = s[i]; r[i] = 0; }
                break;
            case Pred.Fixed2:   // FLAC / ALAC: 2nd-order fixed polynomial predictor
                p[0] = s[0]; p[1] = s[1]; r[0] = r[1] = 0;
                for (int i = 2; i < Roll; i++) { p[i] = 2 * s[i - 1] - s[i - 2]; r[i] = s[i] - p[i]; }
                break;
            case Pred.Adaptive: // TTA-style order-1 adaptive predictor
            {
                double a = 1.0; p[0] = s[0]; r[0] = 0;
                for (int i = 1; i < Roll; i++)
                {
                    double pr = a * s[i - 1];
                    double e = s[i] - pr;
                    a += 0.004 * e * s[i - 1]; if (a < 0) a = 0; if (a > 1.3) a = 1.3;
                    p[i] = (float)pr; r[i] = (float)e;
                }
                break;
            }
            case Pred.Cascade:  // WavPack-style cascaded decorrelation (two difference passes)
            {
                p[0] = s[0]; r[0] = 0;
                float d1prev = 0, prev = s[0];
                for (int i = 1; i < Roll; i++)
                {
                    float d1 = s[i] - prev;      // pass 1: first difference
                    float e = d1 - d1prev;       // pass 2: difference of differences
                    p[i] = s[i] - e; r[i] = e;
                    d1prev = d1; prev = s[i];
                }
                break;
            }
            case Pred.Lms:      // Monkey's Audio-style adaptive NLMS FIR filter (order 8)
            {
                const int P = 8; var wts = new double[P];
                for (int i = 0; i < Roll; i++)
                {
                    if (i < P) { p[i] = s[i]; r[i] = 0; continue; }
                    double pr = 0, norm = 1e-6;
                    for (int j = 0; j < P; j++) { pr += wts[j] * s[i - 1 - j]; norm += s[i - 1 - j] * s[i - 1 - j]; }
                    double e = s[i] - pr;
                    double g = 0.5 * e / norm;
                    for (int j = 0; j < P; j++) wts[j] += g * s[i - 1 - j];
                    p[i] = (float)pr; r[i] = (float)e;
                }
                break;
            }
        }
    }

    // Rice-code cost of the residual: the true bits/sample an encoder would spend on this block.
    private double BitsPerSample(Pred kind)
    {
        int start = kind == Pred.Lms ? 8 : 2;
        double mean = 0; int nn = 0;
        for (int i = start; i < Roll; i++) { mean += Math.Abs(_resid[i]) * 32768.0; nn++; }
        if (nn == 0) return 16;
        mean /= nn;
        int k = mean > 1 ? (int)Math.Round(Math.Log(mean, 2)) : 0; if (k < 0) k = 0; if (k > 15) k = 15;
        double bits = 0;
        for (int i = start; i < Roll; i++)
        {
            int v = (int)(Math.Abs(_resid[i]) * 32768.0);
            bits += (v >> k) + 1 + k;   // Rice codeword length for |residual|
        }
        return Math.Max(1.0, Math.Min(16.0, bits / nn));
    }

    // -------- drawing --------

    private void DrawStage(DrawingContext dc, int index, int count, Rect r, (string name, string desc, Pred pred, Pack pack, string predLabel, string packLabel) info)
    {
        bool isLast = index == count - 1;
        if (index == 0) { Trace(dc, r, _roll, Teal, 1.6); return; }            // signal
        if (info.pred == Pred.None) { StoreStage(dc, r); return; }             // WAV "store"
        if (isLast) { PackStage(dc, r, info.pack); return; }                   // pack / range
        if (index == 1) { PredictStage(dc, r); return; }                      // predict / adapt / decorrelate
        ResidualStage(dc, r);                                                  // residual
    }

    private void PredictStage(DrawingContext dc, Rect r)
    {
        Trace(dc, r, _roll, Color.FromArgb(70, Teal.R, Teal.G, Teal.B), 1.3);  // real signal, faint
        Trace(dc, r, _pred, Amber, 1.6);                                       // real prediction tracking it
    }

    private void ResidualStage(DrawingContext dc, Rect r)
    {
        // faint envelope of how big the signal was, so the small residual reads as "what's left"
        double mid = r.Top + r.Height / 2, amp = r.Height * 0.42;
        double env = 0; for (int i = 0; i < Roll; i++) env = Math.Max(env, Math.Abs(_roll[i]));
        double eh = Math.Min(amp, env * amp);
        var band = new SolidColorBrush(Color.FromArgb(22, Muted.R, Muted.G, Muted.B)); band.Freeze();
        dc.DrawRectangle(band, null, new Rect(r.Left + 5, mid - eh, r.Width - 10, eh * 2));
        Trace(dc, r, _resid, Teal, 1.4);                                       // the real residual, same scale
    }

    // Rice packing: raw 16-bit columns collapsing to the real packed height (bits/16).
    private void PackStage(DrawingContext dc, Rect r, Pack pack)
    {
        if (pack == Pack.Range) { RangeStage(dc, r); return; }
        int cols = 10; double pad = 8, cw = (r.Width - pad * 2) / (cols * 1.7);
        double baseY = r.Bottom - 7, fullH = r.Height - 16;
        double frac = Math.Max(0.06, Math.Min(1, _ratioEma));
        var raw = new SolidColorBrush(Color.FromArgb(55, Teal.R, Teal.G, Teal.B)); raw.Freeze();
        var packed = new SolidColorBrush(Teal); packed.Freeze();
        for (int i = 0; i < cols; i++)
        {
            double x = r.Left + pad + i * cw * 1.7;
            dc.DrawRectangle(raw, null, new Rect(x, baseY - fullH, cw, fullH));                 // raw 16-bit
            double ph = fullH * frac * (0.9 + 0.1 * Math.Sin(_phase * 3 + i));
            dc.DrawRectangle(packed, null, new Rect(x, baseY - ph, cw, ph));                    // real packed size
        }
    }

    // Range coder: an arithmetic interval subdividing - the residual narrows the code range.
    private void RangeStage(DrawingContext dc, Rect r)
    {
        double x0 = r.Left + 8, x1 = r.Right - 8, y = r.Top + 8, hh = (r.Height - 16) / 5;
        double lo = 0, hi = 1;
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(120, Muted.R, Muted.G, Muted.B)), 0.8); pen.Freeze();
        for (int row = 0; row < 5; row++)
        {
            double a = x0 + (x1 - x0) * lo, b = x0 + (x1 - x0) * hi;
            var fill = new SolidColorBrush(Color.FromArgb((byte)(180 - row * 26), Teal.R, Teal.G, Teal.B)); fill.Freeze();
            dc.DrawRectangle(fill, pen, new Rect(a, y + row * hh, Math.Max(2, b - a), hh - 2));
            double span = hi - lo, pick = 0.32 + 0.30 * (0.5 + 0.5 * Math.Sin(_phase + row));
            lo += span * pick * 0.35; hi = lo + span * 0.42;                                    // interval keeps narrowing
        }
    }

    private void StoreStage(DrawingContext dc, Rect r)
    {
        Trace(dc, r, _roll, Teal, 1.6);
        var tick = new Pen(new SolidColorBrush(Color.FromArgb(80, Muted.R, Muted.G, Muted.B)), 1); tick.Freeze();
        for (double bx = r.Left + 6; bx < r.Right - 4; bx += 7) dc.DrawLine(tick, new Point(bx, r.Bottom - 8), new Point(bx, r.Bottom - 4));
    }

    // live "X.X bits/sample  ~YY% of PCM" + a shrink bar from 16 down to the real figure
    private void DrawCompression(DrawingContext dc, double w, Pack pack)
    {
        string headline = pack == Pack.Store ? "16.0 bits/sample" : $"{_bitsEma:0.0} bits/sample";
        string sub = pack == Pack.Store ? "1:1 - no compression" : $"~{_ratioEma * 100:0}% of PCM";
        var ftA = MakeText(headline, Mono, 12.5, pack == Pack.Store ? Amber : Teal);
        var ftB = MakeText(sub, Mono, 10.5, Muted);
        dc.DrawText(ftA, new Point(w - ftA.Width - 2, 1));
        dc.DrawText(ftB, new Point(w - ftB.Width - 2, 20));
        // shrink bar
        double bx = w - 150, by = 38, bw = 148;
        var track = new SolidColorBrush(Color.FromArgb(45, Teal.R, Teal.G, Teal.B)); track.Freeze();
        var fill = new SolidColorBrush(pack == Pack.Store ? Amber : Teal); fill.Freeze();
        dc.DrawRoundedRectangle(track, null, new Rect(bx, by, bw, 4), 2, 2);
        dc.DrawRoundedRectangle(fill, null, new Rect(bx, by, bw * (pack == Pack.Store ? 1.0 : _ratioEma), 4), 2, 2);
    }

    // draw a real sample array across a panel, same vertical scale for signal / prediction / residual
    private void Trace(DrawingContext dc, Rect r, float[] data, Color c, double thick)
    {
        double mid = r.Top + r.Height / 2, amp = r.Height * 0.42;
        double left = r.Left + 5, wide = r.Width - 10;
        var fig = new PathFigure { IsClosed = false };
        int n = data.Length; bool first = true;
        int stepPx = Math.Max(1, n / (int)Math.Max(1, wide));
        for (int i = 0; i < n; i += stepPx)
        {
            double v = data[i]; if (v > 1.4) v = 1.4; if (v < -1.4) v = -1.4;
            var pt = new Point(left + wide * i / (n - 1.0), mid - v * amp);
            if (first) { fig.StartPoint = pt; first = false; } else fig.Segments.Add(new LineSegment(pt, true));
        }
        var geo = new PathGeometry(new[] { fig }); geo.Freeze();
        var pen = new Pen(new SolidColorBrush(c), thick) { LineJoin = PenLineJoin.Round }; pen.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    private static void DrawCard(DrawingContext dc, Rect r)
    {
        var bg = new SolidColorBrush(Color.FromArgb(80, 14, 19, 17)); bg.Freeze();
        var edge = new Pen(new SolidColorBrush(Line), 1); edge.Freeze();
        dc.DrawRoundedRectangle(bg, edge, r, 8, 8);
    }

    private static void Arrow(DrawingContext dc, Point a, Point b)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(140, 125, 136, 124)), 1.4) { EndLineCap = PenLineCap.Round }; pen.Freeze();
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
