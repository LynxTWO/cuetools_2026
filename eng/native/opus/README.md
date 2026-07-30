# CUETools Opus encoder build

This project builds the Opus command encoder from the official
`opus-tools` 0.2, `libopusenc` 0.3, `libopus` 1.6.1, and `libogg` 1.3.6 source
archives pinned in `eng/release/external-command-encoders.json`.

Configure this directory with all four extracted source paths:

```text
-DOPUS_TOOLS_SOURCE_DIR=<opus-tools-0.2>
-DLIBOPUSENC_SOURCE_DIR=<libopusenc-0.3>
-DOPUS_SOURCE_DIR=<opus-1.6.1>
-DLIBOGG_SOURCE_DIR=<libogg-1.3.6>
```

From an x64 MSVC developer shell, the release build is:

```text
cmake -S eng/native/opus -B <build> -G Ninja -DCMAKE_BUILD_TYPE=Release ^
  -DOPUS_TOOLS_SOURCE_DIR=<opus-tools-0.2> ^
  -DLIBOPUSENC_SOURCE_DIR=<libopusenc-0.3> ^
  -DOPUS_SOURCE_DIR=<opus-1.6.1> ^
  -DLIBOGG_SOURCE_DIR=<libogg-1.3.6>
cmake --build <build>
```

Before configuring, apply `ThirdParty/opus-1.6.1-cuetools.patch` to the
extracted libopus tree and `ThirdParty/libopusenc-0.3-cuetools.patch` to the
extracted libopusenc tree. The libopus patch corrects the upstream CMake
variable assignment for clang-cl detection. Without it, `false;BOOL` is
truthy and current MSVC is incorrectly passed clang's `-msse4.1` option. The
libopusenc patch makes already-bounded Ogg wire-format conversions explicit
and uses float constants in a float-only LPC window. It also rejects comment
blocks that cannot fit libopusenc's signed 32-bit length fields instead of
silently truncating `strlen()` results, and carries picture sizes through the
base64 helper as `size_t`. Together those changes allow a warning-clean
current-MSVC build without globally suppressing conversion diagnostics.

The Speex-derived `resample.c` remains source-identical. Its filter design
deliberately computes in double precision before storing float coefficients
and compares signed loop counters with unsigned public sample counts. MSVC
diagnostics C4244 and C4018 are therefore disabled for that file alone; every
other level-3 diagnostic remains fatal.

The release binary is x64, statically links its codec dependencies and the
Microsoft runtime, and uses MSVC `/Brepro` with embedded object debug
information. `/experimental:deterministic` enables `/pathmap`, which gives
each extracted source and build tree a stable logical name so assertions and
embedded debug records cannot leak the staging path or break reproducibility.
Independent clean builds must produce the executable SHA-256 recorded in the
release manifest.

The release qualification used two separately extracted and configured source
trees. Their `opusenc.exe` files were byte-identical. Three 44.1 kHz, 16-bit
stereo WAVE streams covering noise, tones, and transients were then encoded
through standard input with CUETools' 192-kbps and Vorbis-comment arguments.
Independent ffmpeg decodes of all six old/current comparison outputs were
stereo, 48 kHz, exactly eight seconds, and carried the requested tags. This is
contract and regression evidence, not a claim that lossy encoded bytes remain
identical across libopus versions.

The source build intentionally omits optional FLAC input. CUETools supplies a
WAVE stream over standard input, so including another decoder would add unused
native code and another supply-chain boundary. WAVE/AIFF input, Unicode paths,
Vorbis comments, pictures, channel mappings, resampling, and the Opus encoder
remain the unmodified upstream implementations.
