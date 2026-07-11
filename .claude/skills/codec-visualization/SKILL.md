---
name: codec-visualization
description: Use when building, extending, or refining the educational codec-encoding animation (CodecScope) - the GPU-drawn view of what an audio codec does to the sound while encoding, or when adding a new codec's depiction or improving how a stage is drawn. WPF/XAML specific.
---

# Codec Visualization

A living, extensible model for depicting *what a codec actually does* to audio while it encodes.
The goal is honest education: show the real pipeline of the codec family, animated, without
faking measurements. Keep this skill updated as codec knowledge and the animation improve.

Reference implementation: `CUETools.Wpf/Controls/CodecScope.cs` (a `FrameworkElement` with
`OnRender` + `CompositionTarget.Rendering`, GPU-composited like the disc map / VU / speed graph).

## The model

Most lossless audio codecs share one pipeline; depict it as left-to-right stages with arrows:

1. **signal** - the PCM waveform (scrolling).
2. **predict** - a predictor (LPC / adaptive FIR) tracking the signal; the codec stores how to
   predict, not the samples.
3. **residual** - signal minus prediction: a much smaller, near-zero signal - "what is left to
   store".
4. **pack** - entropy coding (Rice / Golomb / range) packs the small residuals into fewer bits;
   show raw vs packed columns and a compression figure.

Uncompressed PCM (WAV) is the degenerate case: **signal -> store**, 1:1, no prediction.
Lossy codecs (Opus/AAC/MP3/Vorbis) are a *different* pipeline - add stages for a filterbank/MDCT,
a psychoacoustic model, quantization (where data is actually discarded), and entropy coding - and
say plainly that they discard inaudible detail. Do not draw a lossy codec with the lossless
"residual" stage; that would misrepresent it.

## Per-codec knowledge (extend this)

The control's `Info(codec)` switch is the single place to add or refine a codec. Each entry:
name, one-line description, compressed?, typical ratio. Current entries:

| codec | name | one-liner | typical ratio |
|---|---|---|---|
| wav | WAV | uncompressed PCM - every sample kept exactly | 1.0 |
| flac | FLAC | fit a linear predictor, then Rice-code the residual | ~0.58 |
| m4a/alac | ALAC | adaptive FIR prediction + Rice/Golomb (Apple Lossless) | ~0.62 |
| tta | TTA | adaptive prediction + Rice coding (True Audio) | ~0.62 |
| ape | Monkey's Audio | high-order adaptive prediction + range coding | ~0.55 |
| wv | WavPack | decorrelation + entropy coding | ~0.60 |

When adding a codec, cite its real mechanism (predictor order, entropy coder) in the one-liner.
Ratios are typical-catalogue figures - keep them labelled "typical ~x%", never present them as a
measurement of the current disc. If a live encode exposes the real encoded/raw byte ratio, show
that instead and label it "measured".

## Honesty rules

- The predictor/residual/pack drawings are stylized representations of the algorithm, not the
  actual samples. That is fine for teaching, but never imply the numbers are measured.
- Match the pipeline to the codec family (lossless prediction vs lossy transform). A wrong
  pipeline teaches the wrong thing.
- WAV and other uncompressed formats must read as 1:1 - do not animate a fake compression.

## Verify by rendering

The animation phase comes from `CompositionTarget.Rendering`, which does not tick under a
headless `RenderTargetBitmap`; a single frame at phase 0 is representative. A tiny net8 WPF
console (see `scratchpad/CodecRender`) builds a `CodecScope` per codec in a card, renders to PNG,
and the PNG is read back to check legibility and correctness of each stage before shipping.

## Extending

- New codec: add a case to `Info`. If its family differs (lossy), give it its own `stages[]` and
  `DrawStage` cases rather than forcing the lossless pipeline.
- Better animation: the `Wave`, `PackBars`, and stage drawings are independent; refine one at a
  time and re-render. Keep the palette teal (signal) / amber (predictor) / muted (structure).
- Keep this SKILL.md's codec table and honesty rules in sync with the control.
