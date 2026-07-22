---
name: codec-visualization
description: Use when building, extending, or refining a real-time visualization of audio codec compression - what a lossless codec does to the sound (signal, prediction, residual, entropy pack), how formats differ, or a convert round trip (source -> PCM -> target). Portable: the DSP core is UI-free and reusable in any program; the reference rendering is WPF.
---

# Codec / Compression Visualization

A living model for showing *what a codec actually does to audio*, driven by the **real samples** and
the **real predictor math** - not a canned loop. A better predictor genuinely leaves a smaller
residual and earns a lower ratio, so the picture and the numbers are true, and the differences
between formats fall out of the computation instead of being asserted.

Reusable in other programs: the algorithm core is UI-free. Keep this skill updated as the model and
the drawing improve.

## Two layers: portable core + rendering

- **`CodecMath`** (`CUETools.Wpf/Controls/CodecMath.cs`) is the portable, UI-free core. Given a
  window of real PCM it runs a family predictor and returns the true residual, then estimates
  bits/sample from that residual with the Rice cost an encoder actually spends. No WPF types - lift
  it into any program (port the ~120 lines to any language). This is the piece worth reusing.
- **Rendering** is WPF reference only: `CodecScope.cs` (rip: one codec's pipeline) and
  `ConvertScope.cs` (convert: source -> PCM -> target round trip). Both are `FrameworkElement` +
  `OnRender` + `CompositionTarget.Rendering`, GPU-composited like the disc map / VU / speed graph.

## The real math (the important part)

Every lossless codec family: predict each sample from earlier ones, store the small error. The core
computes the **real** residual per family, so it is measured, not decoration.

| Pred kind | Family | Real predictor used |
|---|---|---|
| `None` | WAV | none - store raw PCM |
| `Fixed2` | FLAC / ALAC / TAK | 2nd-order fixed polynomial: `2*s[n-1] - s[n-2]` |
| `Adaptive` | TTA | order-1 adaptive (weight tracks the signal) |
| `Cascade` | WavPack | cascaded decorrelation (two difference passes) |
| `Lms` | Monkey's Audio | order-8 adaptive NLMS FIR filter |

`residual = signal - prediction`. Then `BitsPerSample` = the Rice-code cost of that residual
(`(|r|>>k) + 1 + k` per sample, optimal `k` from the residual mean) - the same figure an encoder
uses to size a block. `ratio = bits/16`. WAV is 16.0 bits, 1:1.

Verified ordering on a real test signal (earned, not asserted): WAV 16.0 > FLAC 10.5 > TTA 10.4 >
WavPack 10.2 > Monkey's Audio 9.9 bits/sample. Monkey's Audio wins because its adaptive filter
predicts best, leaving the smallest residual.

## Feeding real samples

Tap **consecutive** PCM (not decimated - decimation destroys the sample-to-sample correlation the
predictor needs). Reference pattern:

- Rip: `LevelMeteringRipper` wraps the `ICDRipper`, and on each `Read` hands a window of `WindowSize`
  (16384) consecutive mono samples (normalized to [-1,1]) to an `onSamples` callback, throttled to
  ~20ms. Threaded RipService -> `RipViewModel.SampleWindow` -> the scope's `Samples` DP.
- Convert: `ConvertService.PreloadSource` decodes a short real snippet of the source (resolving a
  .cue/folder to its largest audio file) into windows; the VM loops them through the scope during
  the convert. Real source audio, no contention with the encoder.

**Delivery cadence matters more than it looks.** The disc delivers audio in bursts (a read chunk,
then nothing while the drive re-reads a hard sector), so if the scope consumes exactly what arrives
each frame it "zips across, then waits" - a visible stutter. The fix is a **producer/consumer ring**:
deliver a large window (16384) into a ring (`RingSize` ~96000, ~2s of headroom) and let the scope's
render loop consume from it with an **exponential-follow** read pointer, not the raw arrival rate:
`lag = write - read; read += (lag / tau) * dt`, clamped so it never passes the write head or falls
more than `RingSize - Roll` behind. That decouples the smooth on-screen scroll from the bursty feed -
the picture glides through the buffered audio while the drive stalls, then catches up. Measure a
suspected stutter objectively (crop-hash run-lengths at ~15 fps), do not eyeball it; the "zip/wait"
signature is long identical-frame runs punctuated by big jumps.

## What to draw (three things at once)

1. **DSP pipeline**: `signal -> predict -> residual -> pack`, real waveforms flowing through. WAV is
   the degenerate `signal -> store`.
2. **Compression forming**: live bits/sample, % of PCM, a shrink bar from 16 down to the real
   figure, and raw-vs-packed columns. In the residual panel draw a faint envelope of how big the
   signal was, so the small residual reads as "what is left".
3. **Format contrast**: each codec's predictor and pack visuals differ - Rice bars vs a narrowing
   range-coder interval - and the earned ratio differs. The round-trip scope adds a
   "9.7 -> 16.0 bits/sample, larger" / "16.0 -> 8.6, smaller" verdict comparing two formats on the
   same PCM.

Palette: teal (signal / residual), amber (predictor / uncompressed), muted (structure).

## The lossy pipeline (a different truth, drawn differently)

Lossy codecs get their OWN pipeline via `LossyMath` (portable, UI-free like `CodecMath`):
**spectrum -> mask -> quantize -> pack**, all computed from the real PCM window:

1. a 1024-point FFT of the Hann-windowed samples (honest: LAME's psychoacoustic model IS
   FFT-based; the data path's hybrid polyphase+MDCT is named in the stage labels);
2. Bark-band energies (Traunmuller), Schroeder-style spreading (+25 dB/Bark down, -10 up), a
   ~14 dB tonal offset, and the Terhardt absolute threshold of hearing -> the real MASKING
   THRESHOLD per bin;
3. bins below the mask are inaudible -> DISCARDED (drawn dim red under the amber mask curve); the
   "% discarded" is measured, not asserted;
4. kept bins quantize with a mask-shaped step (noise sits at the mask - the real noise-shaping
   idea), drawn as visibly stepped levels; an entropy estimate of the quantized values gives the
   ~kbps headline.

Per-codec `LossyMath.Profile` makes each lossy codec its own visualization: MP3 = hybrid-filterbank
labels + Huffman pack (variable-width bars, short codes for common values); WMA = pure-MDCT labels,
a coarser noise margin, and run-level packing (grey run markers + level blocks). The differences on
screen are the real design differences. Never draw a lossy codec with the lossless
predictor/residual stages, and never draw a lossless codec with a mask - the families are different
and the scope branches on `LossyMath.IsLossy(codec)` before anything else.

Encoder side (the app): MP3 encodes in-process via the bundled LAME 3.100 (`CUETools.Codecs.libmp3lame`
plugin + vendored native `libmp3lame.dll` x64). Format lists must offer ONLY formats whose encoder is
in-process (`Settings is not CommandLine.EncoderSettings`) - offering a format that fails at encode
time because lame.exe/oggenc.exe is absent is a lie. A settings file saved by an older build can pin
a stale CLI encoder choice; heal it at load (see `SettingsStore.HealEncoderChoices`).

## Honesty rules

- The signal, prediction, residual, bits/sample and ratio ARE computed from the real audio - say so.
- The predictors are **representative of each family**, not bit-exact reimplementations of the real
  encoder. State that (a comment in `CodecMath` does). Do not claim the on-screen ratio equals the
  encoder's final file size; it is a faithful estimate from the real residual.
- Match the pipeline to the family. Lossy codecs (Opus/AAC/MP3/Vorbis) are a *different* pipeline -
  filterbank/MDCT, psychoacoustic model, quantization (where data is discarded), entropy coding.
  Give them their own stages; never draw them with the lossless "residual" stage.
- WAV / uncompressed must read as 1:1 - no fake compression animation.

## Extending

- **New lossless codec**: add a case to `CodecMath.Info` (name, one-liner, `Pred` kind, `Pack`
  kind, stage labels). If none of the existing predictors matches its family, add a `Pred` case with
  its real predictor.
- **New pack style**: Rice bars and the range-coder interval are separate draws; add another for a
  new entropy coder.
- **Lossy**: add a `Pred`/pipeline that models transform + quantization, with its own stages.
- Keep this table and the honesty rules in sync with `CodecMath`.

## Verify by rendering

`CompositionTarget.Rendering` does not tick under a bare `RenderTargetBitmap`, so the harness shows a
real off-screen `Window`, feeds real sample windows on a `DispatcherTimer`, lets the bits/ratio EMA
settle (~700ms after switching codec), then captures each codec/round-trip to PNG and reads it back.
See `scratchpad/CodecRender` (per-codec) and `scratchpad/ConvertRender` (round trip). Always let the
EMA settle before the shot, or the headline number lags the previous codec.
