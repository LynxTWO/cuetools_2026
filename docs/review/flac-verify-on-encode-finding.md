# FLAC verify-on-encode crashes the encode (Flake encoder)

Date: 2026-07-25
Status: FIXED and ON by default. Root cause found, fixed in the shared bit reader, and gated by
exact-buffer and encoder tests. ALAC checked too and is not affected.
Severity: was blocking - every FLAC encode failed while the option defaulted to ON.

## Plain English

FLAC can check its own work by decoding each frame right after encoding it and comparing the samples
(the classic `flac -V`). Turning that on by default (commit 4e0c274) broke every FLAC rip: the encode
died about 16 s in. The failure is in the CHECKER, not the audio. The encoder's output is valid FLAC.

## What was measured

- Enabling the option (`EncoderSettings.DoVerify` default true) made a Test & Copy encode fail:
  `IndexOutOfRangeException: BitReader.read_rice_block: read past end of buffer (corrupt or truncated
  stream)`, raised from `CUESheet.WriteAudioFilesPass` -> `CUESheet.Go` -> `RipService.Run`.
  Log: `cuetools-20260725-081124.log`, 08:22:07, `mode=encode format=flac`.
- The exception originates inside `verify.DecodeFrame(frame_buffer, 0, fs)` in
  `CUETools.Codecs.Flake/AudioEncoder.cs` `output_frame()` (the verify block at ~line 2020). That block
  has a try/catch whose handler ends in `throw ex;` (~line 2046), so a decoder crash aborts the encode.
- The 11 FLAC files from an earlier rip of the same disc (encoded with verify OFF) ALL decode cleanly
  under ffmpeg, an independent decoder that validates FLAC frame CRC-8 headers and CRC-16 footers:
  11/11 clean, zero errors.
- Those files were from an earlier rip, but the evidence still applies to the CURRENT encoder:
  `git log -- CUETools.Codecs.Flake/AudioEncoder.cs` shows the encoding algorithm has only ever been
  touched by copyright-year bumps. The single functional change in that project during this work was
  the `DoVerify` default (4e0c274) and its revert (67d55fc). Same encoder code, so the clean decode is
  not stale evidence.

## What that means

- Verified: the Flake FLAC encoder produces structurally valid, CRC-clean FLAC. Existing rips are
  sound; nothing needs re-ripping.
- Verified: with verify ON, the encode aborts. So the regression was the default flip, not the audio.
- Inferred (not yet proven): the defect is in the verify DECODE plumbing - the verify `AudioDecoder`
  is being handed a frame it cannot parse in isolation, or is not carrying the stream context it needs
  between frames. "Read past end of buffer" means the decoder expected more bytes than the `fs` bytes
  the encoder just wrote, which points at framing/decoder-state, not at corrupt samples. A genuine
  sample mismatch would instead throw `ExceptionValidationFailed` (the explicit check at ~line 2024
  and the MemCmp at ~line 2029), which is NOT what happened.

## Root cause (found)

The verify path hands the decoder one frame in a buffer that ends exactly at that frame's last byte:
`verify.DecodeFrame(frame_buffer, 0, fs)`. `BitReader` keeps a 56-bit cache filled speculatively, so
its raw cache pointer can move several bytes beyond the logical input while valid, unread bits remain
in the cache. The hardened unary/Rice scan compared that speculative pointer directly with `end_m`.
It therefore rejected a valid terminator already present in the cache as soon as the pointer crossed
the end. The same collision occurs in ordinary file decoding at the final frame when no later frame
bytes happen to provide physical lookahead; a damaged-disc run proved this with two valid, independently
FFmpeg-decoded FLAC tracks.

It is data-dependent, which is why it looked random: the guard only trips when a rice unary run near
the frame's end needs another byte. A long unary run means a LARGE residual, i.e. a big prediction
miss - common on real music transients, absent from smooth synthetic test signals. Measured: a clean
tone, uniform noise, silence and full-scale square waves all encode fine with verify ON even at 30 s
and every compression mode, while real CD audio failed at frame 111 (sample 454656) and a synthetic
signal with sparse full-scale spikes over a quiet tone fails in ~250 ms.

## The fix

`CUETools.Codecs/BitReader.cs` now tracks logical source bits independently from its speculative
cache pointer. Fixed-width reads reject when logical input is exhausted. Unary and Rice scans consume
and bound-check real logical bits while allowing a terminator already held in the cache. Speculative
refill still substitutes zero without dereferencing past `end_m`, but those zeroes never become valid
encoded input.

`CUETools.Codecs.Flake/AudioEncoder.cs` and `CUETools.Codecs.FLACCL/FLACCLWriter.cs` consequently verify
the exact `fs` bytes again. Neither relies on an artificial lookahead pad or adjacent frame data.

`CUETools.Codecs.Flake/EncoderSettings.cs`: `DoVerify` is `[DefaultValue(true)]`. The owner's saved
config carries no `DoVerify` entry for FLAC, so the new default takes effect; a user who turns it off
persists that choice via `[JsonProperty]`.

## Verification

- `CUETools.Wpf.Tests/BitReaderBoundsTests.cs` proves the exact two-byte Rice boundary that used to
  fail, rejects the same input without its unary terminator, and rejects fixed-width over-read.
- `CUETools.Wpf.Tests/FlacVerifyOnEncodeTests.cs` - the permanent encoder gate. Its transient-content case
  FAILS without the fix and passes with it (RED then GREEN, confirmed in that order). Also sweeps every
  compression mode, covers silence/noise/loud/music, and asserts verify-ON produces byte-identical
  output to verify-OFF (verify must only observe).
- The full real 345 s track that crashed in production now encodes with verify ON.
- All 24 retained FLAC tracks from the damaged-disc run decode to their declared sample counts with
  the corrected managed decoder; the two former failures independently decode cleanly with FFmpeg.
- Full suite green.
- Independent check: the encoder's output decodes cleanly under ffmpeg and each file's decoded audio
  matches its own STREAMINFO MD5 (11/11 on a real rip), so encoder output was and remains valid FLAC.

## ALAC checked as well - not affected

`CUETools.Codecs.ALAC/ALACWriter.cs:1331` uses the same exact-length `DecodeFrame(frame_buffer, 0, fs)`
shape AND ships with `DoVerify` already true in the config, so it was a prime suspect. It is NOT
affected: ALAC's decoder has its own bit-reading code and does not use the guarded
`BitReader.read_rice_block`. Measured by `CUETools.Wpf.Tests/AlacVerifyOnEncodeTests.cs`, which runs
the same transient content that reproduces the FLAC bug, at ALAC's archival mode 10 - passes. No ALAC
change was made; the test stays as a gate.
