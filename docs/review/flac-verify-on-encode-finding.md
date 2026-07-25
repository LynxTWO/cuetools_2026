# FLAC verify-on-encode crashes the encode (Flake encoder)

Date: 2026-07-25
Status: default reverted to OFF (encodes work again). The verify path itself is BROKEN and needs a fix
before the option can default to ON, which is the owner's stated preference.
Severity: was blocking - every FLAC encode failed while the option defaulted to ON.

## Plain English

FLAC can check its own work by decoding each frame right after encoding it and comparing the samples
(the classic `flac -V`). Turning that on by default (commit d9198f2) broke every FLAC rip: the encode
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

## Fix taken now

`CUETools.Codecs.Flake/EncoderSettings.cs`: `DoVerify` back to `[DefaultValue(false)]`, with a comment
pointing here so nobody re-flips it without fixing the path first.

## To actually deliver the feature (owner wants it ON by default)

1. Reproduce in isolation: encode a known WAV with `DoVerify = true` in a unit test, no drive involved.
   That turns a 6-minute rip cycle into a fast test and pins the failing frame.
2. Inspect how `verify` (the `AudioDecoder` field) is constructed and whether it needs the STREAMINFO
   header, a per-frame reset, or the running blocksize/partition context that `DecodeFrame` assumes.
   Suspect the first partial/final frame of a track (`bs < m_blockSize`, handled at ~line 2049) and the
   `verifyBuffer` copy at ~line 1986, which copies `m_blockSize` samples but is compared over `bs`.
3. Once a frame-level test passes, flip the default and re-run a full FLAC rip plus a Test & Copy.
4. Keep the independent ffmpeg decode check as the outer gate: encoder output must stay CRC-clean.

Do not re-enable the default until step 3 passes.
