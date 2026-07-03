# LAME v4 - Quality-First Plan

User mandate 2026-07-02: **do not sacrifice quality for speed; improve the quality of the LAME MP3 codec at all reasonable costs**, and where *more* cost might justify the outcome, plan it and say so. This supersedes the speed-led ordering in `lame-v4-proposal.md` (that doc's baseline numbers and survey remain valid).

## Non-negotiables

1. **Speed work is only legitimate when bit-exact.** Multithreading and SIMD must produce byte-identical MP3 output to the reference path at the same settings, or they don't ship. Their role is to *fund* expensive quality (deeper search per frame), not to compete with it.
2. **Every quality claim is measured.** No encoder change lands without moving a metric or passing a listening gate. "Sounds fine" is not evidence.
3. **MP3 the format is fixed.** All gains come from making better encoding decisions inside the ISO bitstream: where the noise goes, not what the decoder does.

## Where MP3 encode quality can still improve in 2026

Ranked by expected audible payoff per unit risk:

### Q-A. Deep-search quantization - the flagship (high payoff, low risk, high CPU)
LAME's inner loop (`quantize.c`, `vbrquantize.c`, `takehiro.c`) picks global gain, scalefactors, and Huffman regions with greedy heuristics tuned for 2002 CPUs. Noise allocation is a discrete optimization problem, and a much deeper search - exhaustive/trellis over scalefactor-band noise shaping, joint gain+scalefactor+table selection - finds encodings the heuristics miss, *measurably lowering noise-to-mask at the same bitrate*. Precedent: x264 trellis, aoTuV Vorbis, RD-optimized AAC encoders. Low risk because the psymodel's own thresholds judge the winner; we're just searching harder inside the existing quality metric.
**Cost to say out loud:** 10-100x CPU per frame in a new `--quality max` mode. Justified - this is exactly the "reasonable cost" the mandate invites, and frame/chunk parallelism across the 5950X's 32 threads brings a 50x search back to ~real-time-ish batch speeds.

### Q-B. Multi-frame bit-reservoir lookahead (high payoff, medium risk)
Reservoir spending is local and greedy today; transients starve while easy frames hoard bits. N-frame lookahead allocation (spend reservoir where the psymodel says frames are hard) attacks MP3's classic pre-echo/transient weakness at its budget root.
**Cost:** encoder latency (fine for offline), significant surgery in `reservoir.c`/frame loop.

### Q-C. Joint-stereo decision search (medium payoff, low risk)
Per-granule MS/LR choice is threshold-heuristic. Trying both and keeping the lower perceived-noise result is mechanical once Q-A's search exists.

### Q-D. Pre-echo / short-block decisions (high payoff, HIGH risk)
Attack detection and window switching are where killer samples (castanets, harpsichord) actually break. Better transient detection is possible with modern methods, but the psymodel is two decades of listening-test tuning - changes here can regress subtly.
**Cost that needs YOU:** human ABX listening time on killer samples before anything ships default-on. I'll produce paired ABX-ready outputs; your ears (or trusted HydrogenAudio-style testers) are the gate. Ships behind a flag until then.

### Q-E. "Free" correctness wins (small but real)
Replace the weak internal resampler (only matters for non-44.1k input - CD rips bypass it), audit float->16-bit input dithering, double-precision where the noise floor of the *math* is above the noise floor of the *format*, and fix documented 3.100 defects. Low risk, gated by the regression harness.

## Measurement ladder (built before touching encode code)

**Status 2026-07-02: rungs 1 and 3 (synthetic) are BUILT and committed in `c:\DEV\lame_v4`** - pristine 3.100 imported to git, `tests/corpusgen` generates a deterministic critical-signal corpus, and `tests/regress.ps1` locks a 70-case bit-exact baseline (verified green). Details below.

1. **Bit-exact regression baseline** *(built)* - corpus encoded with pristine 3.100 at V0/V2/V5, CBR 320/128, q0-CBR320, ABR192; SHA-256 of MP3 bytes **and** decoded PCM. Any refactor/threading/SIMD change must reproduce it exactly (`regress.ps1`). This is the behavior-preserving gate.

2. **Primary quality signal - LAME's own noise-to-mask objective (refined approach).** The sharpest, most honest feedback loop for the deep-search flagship is LAME's *own* psychoacoustic model: it already computes masking thresholds per scalefactor band and the quantization noise the encoder achieves. A deeper search that lowers LAME's aggregate noise-to-mask (NMR) at **equal bitrate** is better *by the encoder's own definition of quality* - deterministic, no external dependency, and it directly targets what Q-A optimizes. Plan: a small instrumentation build of LAME that reports achieved total/median NMR per encode, so every search improvement is quantified against 3.100 on the corpus. **This becomes the day-to-day optimization metric.**

3. **Independent cross-validation - external perceptual metrics.** Confirm that lowering LAME's internal NMR actually tracks *perceived* quality, using a decoded-vs-original metric independent of LAME's model: PEAQ ODG (BS.1387) and/or ViSQOL-Audio.
   **Cost note (stated per the mandate):** open PEAQ builds (GstPEAQ, peaqb) are Linux-leaning; a trustworthy Windows build may need real effort or a WSL step. Mitigation: this is a *cross-check*, not the primary loop (rung 2 is), so a delay here does not block Q-A progress. A seeded in-house segmental-NMR meter over the decoded PCM (independent psymodel) is the fallback cross-check if PEAQ/ViSQOL prove impractical on Windows.

4. **Killer-sample corpus** *(synthetic built)* - 10 seeded critical signals (impulse trains/pre-echo traps, castanet-like transients, sparse HF tonals, log sweep, pink/white noise, plucked harmonics, near-Nyquist square, silence+clicks, busy music mix). **Real music would materially strengthen it: a handful of your personal killer tracks (castanets/harpsichord/percussion/massed strings) dropped into `tests/corpus/` are picked up automatically** - copyright keeps me from fetching the classic HydrogenAudio problem samples.

5. **Human ABX** - final gate for any default-behavior change (Q-D especially). Your time; I prepare the paired clips.

## Execution order

| Phase | Deliverable | Gate |
| --- | --- | --- |
| 0 | LAME workspace under git (`c:\DEV\lame_v4`), pristine 3.100 commit, clean rebuild | builds; baseline benchmark reproduced |
| 1 | Corpus generator + regression harness + baseline hashes committed | harness runs green on pristine build |
| 2 | PEAQ/ViSQOL scoring integrated; baseline ODG scores recorded | scores stable and sane |
| 3 | **Q-A deep-search quantization** behind `--quality max` | ODG improves on corpus at equal bitrate; bit-exact default path untouched |
| 4 | Threading (chunked, bit-exact) to fund Q-A's cost | byte-identical output, near-linear scaling |
| 5 | Q-B reservoir lookahead, Q-C stereo search | ODG improves; no regressions |
| 6 | Q-D pre-echo work behind flag + ABX packet for you | your ears |
| 7 | Q-E correctness wins + CMake/CI/fuzzing hardening | regression harness green |

Work happens in `c:\DEV\lame_v4` (its own git history), not in cuetools_2026; this doc tracks direction. A future LAME v4 feeds back into CUETools' bundled libmp3lame (R13).
