using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Pred = CUETools.Wpf.Controls.CodecMath.Pred;
using Pack = CUETools.Wpf.Controls.CodecMath.Pack;

namespace CUETools.Wpf.Controls;

/// <summary>
/// A real-time, GPU-drawn view of what a lossless codec actually does to the audio, driven by the
/// real PCM samples flowing through the rip (see <see cref="Samples"/>). It is NOT a canned loop:
/// each frame it runs the predictor of the selected codec's family (via <see cref="CodecMath"/>) on
/// the true samples, so the signal, prediction and residual on screen are the real numbers, and the
/// bits/sample and ratio are computed from the actual residual.
///
/// The three things it shows at once:
///  - pipeline: signal -> predict -> residual -> pack, the real DSP stages;
///  - compression forming: the residual is small, so the live bits/sample and % of PCM build up;
///  - format contrast: each codec runs a DIFFERENT real predictor, so a better predictor leaves a
///    smaller residual and a lower ratio - the difference is earned, not asserted.
/// </summary>
public sealed class CodecScope : FrameworkElement
{
    public static readonly DependencyProperty CodecProperty = DependencyProperty.Register(
        nameof(Codec), typeof(string), typeof(CodecScope), new FrameworkPropertyMetadata("flac", FrameworkPropertyMetadataOptions.AffectsRender));
    // The user's configured encoder mode for this codec (e.g. "320", "V2", a compression level).
    // Shown in the header so the scope echoes the ACTUAL setting, not just the codec family.
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(string), typeof(CodecScope), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    // True while the drive re-reads a stuck window: the scope veils its (frozen) scene and says so.
    public static readonly DependencyProperty RecoveringProperty = DependencyProperty.Register(
        nameof(Recovering), typeof(bool), typeof(CodecScope), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(
        nameof(Active), typeof(bool), typeof(CodecScope), new PropertyMetadata(false));
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples), typeof(float[]), typeof(CodecScope), new PropertyMetadata(null, OnSamplesChanged));

    public string Codec { get => (string)GetValue(CodecProperty); set => SetValue(CodecProperty, value); }
    public string Mode { get => (string)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public bool Recovering { get => (bool)GetValue(RecoveringProperty); set => SetValue(RecoveringProperty, value); }
    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }
    public float[]? Samples { get => (float[]?)GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }

    private static readonly Color Teal = Color.FromRgb(0x34, 0xCF, 0xC0);
    private static readonly Color Amber = Color.FromRgb(0xE9, 0xA6, 0x3F);
    private static readonly Color Ink = Color.FromRgb(0xED, 0xF1, 0xE9);
    private static readonly Color Muted = Color.FromRgb(0x7D, 0x88, 0x7C);
    private static readonly Color Line = Color.FromRgb(0x28, 0x31, 0x2A);
    private static readonly Typeface Face = new("Segoe UI");
    private static readonly Typeface Mono = new("Consolas");

    // The ripper delivers audio in BURSTS (reads a buffer fast, then pauses while the drive
    // re-reads in secure mode). So we do not scroll in lockstep with delivery - we append into a
    // ring and let the render loop CONSUME it at a steady, servo-controlled rate. That turns the
    // "run fast / freeze / run fast" lurch into a smooth scroll.
    private const int Roll = 640;         // window shown / fed to the predictor
    private const int RingSize = 96000;   // ~2s of headroom to scroll through during long reads
    private readonly float[] _ring = new float[RingSize];
    private long _ringWrite;              // total real samples appended
    private double _readPos;              // smooth consume position (samples)
    private readonly float[] _view = new float[Roll];   // window the predictor + drawing use
    private readonly float[] _demo = new float[Roll];
    private float[] _show;                // _view when real audio flows, else the idle demo
    private readonly float[] _pred = new float[Roll];
    private readonly float[] _resid = new float[Roll];
    private double _bitsEma = 16, _ratioEma = 1;
    private double _phase;
    private TimeSpan _last;

    // lossy pipeline state: a longer window for the FFT analysis + smoothed readouts
    private readonly float[] _fftWin = new float[LossyMath.N];
    private readonly float[] _fftDemo = new float[LossyMath.N];
    private double _kbpsEma = 192, _discEma = 50;

    public CodecScope()
    {
        _show = _view;
        Loaded += (_, _) => CompositionTarget.Rendering += OnRendering;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    private static void OnSamplesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is float[] win && win.Length > 0) ((CodecScope)d).Push(win);
    }

    // append the new real window to the ring (producer); the render loop consumes it steadily
    private void Push(float[] win)
    {
        for (int i = 0; i < win.Length; i++) _ring[(int)((_ringWrite + i) % RingSize)] = win[i];
        _ringWrite += win.Length;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!IsVisible) return;   // the scope is collapsed unless a rip is running - do no work then
        var t = ((RenderingEventArgs)e).RenderingTime;
        double dt = _last == default ? 0 : (t - _last).TotalSeconds;
        _last = t;
        _phase += dt * (Active ? 3.0 : 1.0);
        AdvanceRead(dt);
        InvalidateVisual();
    }

    // Steady consumer with a gentle servo: consume near the average delivery rate, speeding up a
    // little when the buffer builds after a burst and coasting slowly when it drains during a
    // pause - so the scroll never lurches or hard-freezes.
    private void AdvanceRead(double dt)
    {
        if (dt <= 0 || dt > 0.25) return;
        double lag = _ringWrite - _readPos;
        // Exponential follow: scroll quickly right after a read delivers a big chunk, ease off as
        // the buffer runs low during a long secure-mode re-read. rate -> 0 as it nears the head, so
        // it never overtakes and never hard-freezes - it just keeps flowing, slower, until the next
        // chunk arrives. tau sets how far behind live it trails (~1.2s).
        const double tau = 1.2;
        _readPos += (lag / tau) * dt;
        if (_readPos > _ringWrite) _readPos = _ringWrite;
        double floorPos = _ringWrite - (RingSize - Roll);
        if (_readPos < floorPos) _readPos = floorPos;                              // overflowed: skip old
        if (_readPos < Roll) _readPos = Roll;
    }

    // extract the Roll-length window ending at the smooth read position
    private void BuildView()
    {
        long end = (long)_readPos;
        for (int i = 0; i < Roll; i++)
        {
            long idx = end - Roll + i;
            _view[i] = idx >= 0 ? _ring[(int)(((idx % RingSize) + RingSize) % RingSize)] : 0f;
        }
    }

    // Veil drawn over the frozen scene while the drive re-reads a stuck window: dim the stalled
    // visualization and label it, so a long pause reads as deliberate error recovery, not a hang.
    private void DrawRecoveryVeil(DrawingContext dc, double w, double h)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xB4, 0x0F, 0x11, 0x15)), null, new Rect(0, 0, w, h));
        var ft = MakeText("recovering read errors", Mono, 13, Amber);
        dc.DrawText(ft, new Point((w - ft.Width) / 2, h / 2 - 16));
        var ft2 = MakeText("re-reading the disc - audio paused until the window is clean", Face, 10, Muted);
        dc.DrawText(ft2, new Point((w - ft2.Width) / 2, h / 2 + 4));
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        RenderContent(dc, w, h);
        // during a stuck-window re-read no new audio flows, so the scope would otherwise sit frozen;
        // veil it and say so, so the pause reads as intentional recovery, not a hang
        if (Recovering) DrawRecoveryVeil(dc, w, h);
    }

    private void RenderContent(DrawingContext dc, double w, double h)
    {
        // lossy codecs get their OWN pipeline (perceptual: spectrum -> mask -> quantize -> pack);
        // they never draw the lossless predictor/residual stages - different family, different truth
        var lossy = LossyMath.Info(Codec);
        if (lossy != null) { RenderLossy(dc, w, h, lossy); return; }

        var info = CodecMath.Info(Codec);

        // real audio when it is flowing; a gentle demo when idle so the pipeline stays legible
        BuildView();
        if (CodecMath.HasSignal(_view)) _show = _view;
        else { CodecMath.FillDemo(_demo, _phase); _show = _demo; }

        // run the real predictor on the shown window; residual + bits are the true figures
        CodecMath.ComputeResidual(_show, info.Predictor, _pred, _resid);
        double bits = info.Predictor == Pred.None ? 16.0 : CodecMath.BitsPerSample(_resid, info.Predictor);
        double ratio = Math.Max(0.02, Math.Min(1.0, bits / 16.0));
        _bitsEma += (bits - _bitsEma) * 0.12;
        _ratioEma += (ratio - _ratioEma) * 0.12;

        // header: name + mechanism, then the live compression readout on the right
        Text(dc, info.Name, new Point(2, 0), Mono, 14, Ink, true);
        Text(dc, info.Desc, new Point(2, 20), Face, 10.5, Muted, false);
        DrawCompression(dc, w, info.Packer);

        double top = 42, bot = h - 15, bh = bot - top;
        if (bh < 24) return;

        string[] stages = info.Predictor == Pred.None
            ? new[] { "signal", info.PredLabel }
            : new[] { "signal", info.PredLabel, "residual", info.PackLabel };
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

    private void DrawStage(DrawingContext dc, int index, int count, Rect r, CodecMath.CodecInfo info)
    {
        bool isLast = index == count - 1;
        if (index == 0) { Trace(dc, r, _show, Teal, 1.6); return; }            // signal
        if (info.Predictor == Pred.None) { StoreStage(dc, r); return; }        // WAV "store"
        if (isLast) { PackStage(dc, r, info.Packer); return; }                 // pack / range
        if (index == 1) { PredictStage(dc, r); return; }                      // predict / adapt / decorrelate
        ResidualStage(dc, r);                                                  // residual
    }

    private void PredictStage(DrawingContext dc, Rect r)
    {
        Trace(dc, r, _show, Color.FromArgb(70, Teal.R, Teal.G, Teal.B), 1.3);  // real signal, faint
        Trace(dc, r, _pred, Amber, 1.6);                                       // real prediction tracking it
    }

    private void ResidualStage(DrawingContext dc, Rect r)
    {
        // faint envelope of how big the signal was, so the small residual reads as "what's left"
        double mid = r.Top + r.Height / 2, amp = r.Height * 0.42;
        double env = 0; for (int i = 0; i < Roll; i++) env = Math.Max(env, Math.Abs(_show[i]));
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
        Trace(dc, r, _show, Teal, 1.6);
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

    // ---------------- lossy pipeline (perceptual coding, a different truth) ----------------

    private static readonly Color Crit = Color.FromRgb(0xEF, 0x6D, 0x6D);

    // extract the LossyMath.N-length window ending at the smooth read position
    private void BuildFftView()
    {
        long end = (long)_readPos;
        for (int i = 0; i < LossyMath.N; i++)
        {
            long idx = end - LossyMath.N + i;
            _fftWin[i] = idx >= 0 ? _ring[(int)(((idx % RingSize) + RingSize) % RingSize)] : 0f;
        }
    }

    private void RenderLossy(DrawingContext dc, double w, double h, LossyMath.Profile prof)
    {
        BuildFftView();
        float[] show = _fftWin;
        if (!CodecMath.HasSignal(_fftWin)) { CodecMath.FillDemo(_fftDemo, _phase); show = _fftDemo; }

        // the REAL perceptual pipeline on the real window
        var a = LossyMath.Analyze(show, prof);
        _kbpsEma += (a.EstKbps - _kbpsEma) * 0.10;
        _discEma += (a.PercentDiscarded - _discEma) * 0.10;

        // header + live readout: estimated rate and how much was judged inaudible. The tagline is
        // clipped so it never collides with the right-side readout at narrow widths.
        // header LEFT: codec + the user's configured mode, so the scope echoes the real setting
        string title = string.IsNullOrWhiteSpace(Mode) ? prof.Name : $"{prof.Name} - {Mode}";
        Text(dc, title, new Point(2, 0), Mono, 14, Ink, true);
        // header RIGHT: what the CONTENT needs to stay transparent (from the masking model), NOT the
        // encoder's output rate - it honestly dips low on simple/quiet passages.
        string head = $"content ~{_kbpsEma:0} kbps";
        var ft = MakeText(head, Mono, 14, Amber);
        dc.DrawText(ft, new Point(w - ft.Width - 2, 0));
        string sub = $"{_discEma:0}% of spectral detail inaudible - discarded";
        var ft2 = MakeText(sub, Face, 10, Muted);
        dc.DrawText(ft2, new Point(w - ft2.Width - 2, 20));
        var tag = MakeText(prof.Tagline, Face, 10.5, Muted);
        tag.MaxTextWidth = Math.Max(40, w - ft2.Width - 24);
        tag.MaxLineCount = 1;
        tag.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(tag, new Point(2, 20));

        double top = 42, bot = h - 15, bh = bot - top;
        if (bh < 24) return;

        var stages = prof.Stages;
        double gap = 10;
        double sw = (w - gap * (stages.Length - 1)) / stages.Length;
        for (int i = 0; i < stages.Length; i++)
        {
            double x = i * (sw + gap);
            var r = new Rect(x, top, sw, bh);
            DrawCard(dc, r);
            dc.PushClip(new RectangleGeometry(r, 8, 8));
            switch (i)
            {
                case 0: SpectrumStage(dc, r, a, withMask: false); break;
                case 1: SpectrumStage(dc, r, a, withMask: true); break;
                case 2: QuantStage(dc, r, a); break;
                default: LossyPackStage(dc, r, a, prof); break;
            }
            dc.Pop();
            Text(dc, stages[i], new Point(x + 8, bot + 1), Mono, 9, Muted, false);
            if (i < stages.Length - 1) Arrow(dc, new Point(x + sw + 1, top + bh / 2), new Point(x + sw + gap - 1, top + bh / 2));
        }
    }

    // log-frequency x mapping (50 Hz .. 20 kHz), dB y mapping (-96 .. 0)
    private static double Fx(int bin, Rect r)
    {
        double f = Math.Max(50, bin * 44100.0 / LossyMath.N);
        return r.Left + 4 + (r.Width - 8) * Math.Log(f / 50.0) / Math.Log(20000.0 / 50.0);
    }
    private static double Dy(double db, Rect r) => r.Top + 5 + (r.Height - 10) * Math.Min(1, Math.Max(0, -db / 96.0));

    // Stage 1/2: the real spectrum; with the mask on, the amber threshold curve overlays it and
    // everything below the curve turns dim red - the discard, made visible.
    private void SpectrumStage(DrawingContext dc, Rect r, LossyMath.Analysis a, bool withMask)
    {
        var keep = new Pen(new SolidColorBrush(Teal), 1.2); keep.Freeze();
        var drop = new Pen(new SolidColorBrush(Color.FromArgb(120, Crit.R, Crit.G, Crit.B)), 1.0); drop.Freeze();
        for (int k = 2; k < LossyMath.Bins; k += 2)
        {
            double x = Fx(k, r);
            double y = Dy(a.SpectrumDb[k], r);
            bool dropped = withMask && !a.Kept[k];
            dc.DrawLine(dropped ? drop : keep, new Point(x, r.Bottom - 4), new Point(x, y));
        }
        if (withMask)
        {
            var maskPen = new Pen(new SolidColorBrush(Amber), 1.6); maskPen.Freeze();
            Point? prev = null;
            for (int k = 2; k < LossyMath.Bins; k += 4)
            {
                var p = new Point(Fx(k, r), Dy(a.MaskDb[k], r));
                if (prev != null) dc.DrawLine(maskPen, prev.Value, p);
                prev = p;
            }
        }
    }

    // Stage 3: kept components redrawn at their QUANTIZED (stepped) levels - the coarse steps are
    // where the bits are saved; dropped bins are simply gone.
    private void QuantStage(DrawingContext dc, Rect r, LossyMath.Analysis a)
    {
        var q = new Pen(new SolidColorBrush(Teal), 2.2) { StartLineCap = PenLineCap.Flat }; q.Freeze();
        var tick = new SolidColorBrush(Color.FromArgb(200, Amber.R, Amber.G, Amber.B));
        tick.Freeze();
        for (int k = 2; k < LossyMath.Bins; k += 4)
        {
            if (!a.Kept[k]) continue;
            double x = Fx(k, r);
            double y = Dy(a.QuantDb[k], r);
            dc.DrawLine(q, new Point(x, r.Bottom - 4), new Point(x, y));
            dc.DrawRectangle(tick, null, new Rect(x - 1.6, y - 1, 3.2, 2));   // the step top
        }
    }

    // Stage 4: the entropy pack. Huffman (MP3): variable-width bars, short codes for common small
    // values. Run-level (WMA family): runs of dropped bins collapse to a marker + one level block.
    private void LossyPackStage(DrawingContext dc, Rect r, LossyMath.Analysis a, LossyMath.Profile prof)
    {
        double x = r.Left + 6, baseY = r.Bottom - 8, maxH = r.Height - 18;
        var bar = new SolidColorBrush(Teal); bar.Freeze();
        var runB = new SolidColorBrush(Color.FromArgb(90, Muted.R, Muted.G, Muted.B)); runB.Freeze();
        int run = 0;
        for (int k = 2; k < LossyMath.Bins && x < r.Right - 8; k += 4)
        {
            if (!a.Kept[k]) { run++; continue; }
            if (prof.HuffmanPack)
            {
                // bar width ~ code length: rarer/larger values cost more bits
                double snr = Math.Max(0, a.SpectrumDb[k] - a.MaskDb[k]);
                double bits = Math.Max(1, Math.Log2(1 + Math.Pow(10, snr / 20.0)));
                double bw = 1.5 + bits * 0.8;
                double bhh = Math.Min(maxH, 4 + bits * (maxH / 14.0));
                dc.DrawRectangle(bar, null, new Rect(x, baseY - bhh, bw, bhh));
                x += bw + 1.5;
            }
            else
            {
                // run-level: a thin grey run marker (skipped bins) then one level block
                if (run > 0) { dc.DrawRectangle(runB, null, new Rect(x, baseY - 3, Math.Min(10, 2 + run), 3)); x += Math.Min(10, 2 + run) + 1; }
                double lvl = Math.Max(0, a.QuantDb[k] + 96) / 96.0;
                double bhh = Math.Min(maxH, 4 + lvl * maxH);
                dc.DrawRectangle(bar, null, new Rect(x, baseY - bhh, 3.5, bhh));
                x += 5;
            }
            run = 0;
        }
    }
}
