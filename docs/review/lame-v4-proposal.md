# LAME v4 — Baseline, Benchmark, and Proposal

Requested by the user 2026-07-02: improve the LAME MP3 encoder enough to justify a version 4 release (LAME has been at 3.100 since 2017). Source: `C:\Users\usaft\Downloads\lame-3.100`. This doc is the first deliverable: a measured baseline, an honest survey of where the 20-year-old encoder is behind, and a prioritized v4 feature set with a verification plan. Implementation is a multi-stage effort to start after the user picks the direction.

## Baseline — built and measured

Built LAME 3.100 from source on this machine (VS2022 Build Tools, 64-bit, no NASM: `nmake -f Makefile.MSVC MSVCVER=Win64 ASM=NO "MACHINE=/machine:x64"` — the Makefile hardcodes `/machine:I386`, which had to be overridden; that alone shows the build system's age). `lame.exe --version` → "LAME 64bits version 3.100".

Encode benchmark, 180 s of 44.1 kHz/16-bit stereo music-like content, single run each, on the Ryzen 9 5950X (32 logical cores):

| Setting | Time | Realtime factor | Output |
| --- | --- | --- | --- |
| `-V2` (VBR ~190k) | 2229 ms | 80.8× | 3190 KB |
| `-V0` (VBR ~245k) | 2412 ms | 74.6× | 4389 KB |
| `-b320` (CBR) | 1943 ms | 92.6× | 7034 KB |
| `-q0 -b320` (max quality, slowest) | 10344 ms | 17.4× | 7034 KB |
| `-V2 -q9` (fastest) | 1077 ms | 167× | 3016 KB |

**The headline:** LAME uses **1 of 32 logical cores**. Encoding is entirely single-threaded, and the high-quality `-q0` path is the slow one (17× realtime) — exactly where archivists feel it, and exactly where parallelism and modern SIMD would help most.

## Where 3.100 is behind (surveyed in-source)

- **No threading at all.** There is no `pthread`/OpenMP/native-thread encode path anywhere in `libmp3lame` or the frontend. One file = one core. On a 32-thread CPU that leaves ~97% of the machine idle for batch work.
- **SIMD stuck at SSE1 (1999).** The only vectorized hot code is `libmp3lame/vector/xmm_quantize_sub.c` (SSE1 `xmmintrin.h` intrinsics, gated behind `HAVE_XMMINTRIN_H`), plus legacy MMX/3DNow **NASM** assembly. The FFT (`fft.c`), psychoacoustic model (`psymodel.c`), MDCT (`newmdct.c`), and much of quantization are **scalar**. Zen 3 supports **AVX2** (not AVX-512) — a large untapped speedup on the hot loops.
- **Archaic build system.** VS2008 `.vcproj` files + a hand-written `Makefile.MSVC` that needed a manual machine-flag override to link 64-bit. No CMake, no CI, no test automation shipped.
- **C89-era code, no fuzzing.** `mpglib` (the bundled MP3 *decoder*) parses untrusted MP3 — a real attack surface with no fuzz harness. Compiler-warning hygiene is dated.
- **Loudness/quality features frozen.** No EBU R128 / ReplayGain 2.0 loudness; VBR tuning and defaults are as of 2017.

## Proposed v4 feature set (ranked by impact)

### 1. Multithreaded encoding — the flagship v4 feature (biggest measured win)
MP3 frames are largely independent (the bit reservoir couples them, but block-parallel encoding with a reservoir-stitch pass is well understood). Two tiers:
- **Frontend/batch parallelism (fast to ship):** encode independent files, or split one file into N chunks encoded on N cores and concatenated with a reservoir fix-up. On this 5950X, ~10–20× throughput for `-q0` batch encodes is realistic.
- **Library-level pipeline (bigger effort):** a threaded `libmp3lame` path (analysis/psymodel/quantize pipelined) so even single-file encodes use multiple cores.
This is the single most defensible "why v4": LAME has never been multithreaded.

### 2. AVX2 SIMD on the scalar hot loops
Port the FFT, psychoacoustic energy/threshold calcs, MDCT, and quantization inner loops to AVX2 (with SSE2 and scalar fallbacks, runtime CPUID dispatch). Targets the `-q0`/`-V0` cost directly. Retire the NASM MMX/3DNow paths in favor of portable intrinsics.

### 3. Modern build + CI + tests
CMake build (Windows/Linux/macOS), GitHub Actions matrix, and an automated regression suite: bit-exact where output must not change, and ABX/PEAQ-style quality gates where it may. This is table stakes for calling something a 2026 release and de-risks 1 and 2.

### 4. Safety: fuzz mpglib, modernize C, fix warnings
SharpFuzz-style/libFuzzer harness over the MP3 decoder, warning-clean build, and targeted C modernization. Low glamour, high value for a codec that parses untrusted input.

### 5. Loudness + quality updates
EBU R128 / ReplayGain 2.0 loudness metadata, VBR default re-tuning validated by listening tests, and gapless-metadata polish.

## What justifies calling it v4

Not any single item, but the combination: **the first multithreaded LAME**, **AVX2-era SIMD** (vs. SSE1), a **modern cross-platform build/CI with real tests**, **fuzz-hardened decoding**, and **loudness/quality updates** — a generational step after a 20-year gap, with measurable speedups on modern hardware (the 5950X sat 97% idle during every encode above).

## Verification plan (mandatory before shipping encoder changes)

- **Bit-exactness gate for non-quality changes** (threading, build, SIMD that must not alter output): the multithreaded and AVX2 paths must produce output bit-identical to the scalar path at the same settings, verified across the LAME test vectors + a content corpus. Any intentional quality change goes through ABX/listening tests, never silent.
- **Speedups measured** against this baseline table on the same hardware.
- **Decoder fuzzing** must run clean before release.

## Status and next step

Baseline built + benchmarked + surveyed (this doc). **Awaiting the user's direction on where to start.** Recommended first implementation: **item 1, frontend/batch multithreading** — highest measured payoff, shippable without touching the bitstream math, and it makes the v4 case on its own. I can start there (or with an AVX2 FFT hot-loop) on your go-ahead. This work lives in the LAME source tree, separate from the CUETools repo; libmp3lame 3.100 is what CUETools bundles today, so a v4 would eventually feed back into the codec refresh (R13).
