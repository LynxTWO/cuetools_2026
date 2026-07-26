# Managed FLAC short-metadata nontermination

Status: resolved on 2026-07-26. The deterministic fuzz lane now requires this exact input to be
rejected by an isolated decoder process within five seconds.

Corpus source: the first 16 bytes of `CUETools/CUETools.TestCodecs/Data/test.flac`.

- Hex: `66 4C 61 43 00 00 00 22 10 00 10 00 00 07 58 00`
- SHA-256: `072c63e8319dd654b0f67a8c33e6eb993c9ab894e75b406382a5c08c5374aea1`
- Expected: a bounded corrupt/truncated-stream rejection.
- Before the fix: no exit within either a 3-second or 5-second boundary.
- After the fix: the decoder rejects the incomplete metadata block, and a timeout or acceptance is
  a gate failure.

Reproduction (where `short.flac` contains the bytes above):

```powershell
CUETools.Fuzz\bin\Release\net8.0-windows\CUETools.Fuzz.exe --corpus-child flac short.flac
```

Managed stack captured after 1.2 seconds with `dotnet-stack` 9.0.661903:

```text
System.IO.Strategies.BufferedFileStreamStrategy.ReadSpan(...)
CUETools.Codecs.Flake.AudioDecoder.decode_metadata()
CUETools.Codecs.Flake.AudioDecoder..ctor(...)
CUETools.Fuzz.CorpusFuzzer.TryDecodeFlac(...)
CUETools.Fuzz.CorpusFuzzer.RunChild(...)
```

Likely loop: `decode_metadata()` sees a 34-byte STREAMINFO block in the short input, advances past
EOF, then starts another metadata iteration. `fill_frames_buffer()` returns no bytes, but the loop
still constructs a zero-length `BitReader`; zero bits decode as another non-final, zero-length
STREAMINFO block, so it repeats indefinitely.

Resolution: the managed decoder now requires a complete FLAC header and STREAMINFO body and bounds
metadata consumption on non-seekable streams. `CorpusFuzzer.RunFlacCorpus` preserves this exact
16-byte prefix as a mandatory isolated-process regression, in addition to the general fixed
corruption corpus.
