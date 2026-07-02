# LAME v4 — Quality-First Plan

User mandate 2026-07-02: **do not sacrifice quality for speed; improve the quality of the LAME MP3 codec at all reasonable costs**, and where *more* cost might justify the outcome, plan it and say so. This supersedes the speed-led ordering in `lame-v4-proposal.md` (that doc's baseline numbers and survey remain valid).

## Non-negotiables

1. **Speed work is only legitimate when bit-exact.** Multithreading and SIMD must produce byte-identical MP3 output to the reference path at the same settings, or they don't ship. Their role is to *fund* expensive quality (deeper search per frame), not to compete with it.
2. **Every quality claim is measured.** No encoder change lands without moving a metric or passing a listening gate. "Sounds fine" is not evidence.
3. **MP3 the format is fixed.** All gains come from making better encoding decisions inside the ISO bitstream: where the noise goes, not what the decoder does.

## Where MP3 encode quality can still improve in 2026

Ranked by expected audible payoff per unit risk:

### Q-A. Deep-search quantization — the flagship (high payoff, low risk, high CPU)
LAME's inner loop (`quantize.c`, `vbrquantize.c`, `takehiro.c`) picks global gain, scalefactors, and Huffman regions with greedy heuristics tuned for 2002 CPUs. Noise allocation is a discrete optimization problem, and a much deeper search — exhaustive/trellis over scalefactor-band noise shaping, joint gain+scalefactor+table selection — finds encodings the heuristics miss, *measurably lowering noise-to-mask at the same bitrate*. Precedent: x264 trellis, aoTuV Vorbis, RD-optimized AAC encoders. Low risk because the psymodel's own thresholds judge the winner; we're just searching harder inside the existing quality metric.
**Cost to say out loud:** 10–100× CPU per frame in a new `--quality max` mode. Justified — this is exactly the "reasonable cost" the mandate invites, and frame/chunk parallelism across the 5950X's 32 threads brings a 50× search back to ~real-time-ish batch speeds.

### Q-B. Multi-frame bit-reservoir lookahead (high payoff, medium risk)
Reservoir spending is local and greedy today; transients starve while easy frames hoard bits. N-frame lookahead allocation (spend reservoir where the psymodel says frames are hard) attacks MP3's classic pre-echo/transient weakness at its budget root.
**Cost:** encoder latency (fine for offline), significant surgery in `reservoir.c`/frame loop.

### Q-C. Joint-stereo decision search (medium payoff, low risk)
Per-granule MS/LR choice is threshold-heuristic. Trying both and keeping the lower perceived-noise result is mechanical once Q-A's search exists.

### Q-D. Pre-echo / short-block decisions (high payoff, HIGH risk)
Attack detection and window switching are where killer samples (castanets, harpsichord) actually break. Better transient detection is possible with modern methods, but the psymodel is two decades of listening-test tuning — changes here can regress subtly.
**Cost that needs YOU:** human ABX listening time on killer samples before anything ships default-on. I'll produce paired ABX-ready outputs; your ears (or trusted HydrogenAudio-style testers) are the gate. Ships behind a flag until then.

### Q-E. "Free" correctness wins (small but real)
Replace the weak internal resampler (only matters for non-44.1k input — CD rips bypass it), audit float→16-bit input dithering, double-precision where the noise floor of the *math* is above the noise floor of the *format*, and fix documented 3.100 defects. Low risk, gated by the regression harness.

## Measurement ladder (built before touching encode code)

1. **Bit-exact regression baseline** — corpus encoded with pristine 3.100 at reference settings; SHA-256 of MP3 bytes and of decoded PCM. Any refactor/threading change must reproduce it exactly.
2. **Objective perceptual metrics** — PEAQ ODG (BS.1387) and/or ViSQOL-Audio scored decoded-vs-original per corpus item. Gains must show here before listening tests are requested.
   **Cost note:** open PEAQ implementations (GstPEAQ, peaqb) are Linux-leaning; getting a trustworthy Windows build may take real effort or a WSL step. If neither proves trustworthy, an interim in-house NMR meter (noise-to-mask via an independent psymodel) is a planned fallback — more work, stated now.
3. **Killer-sample corpus** — synthetic critical signals first (impulse trains/pre-echo traps, sparse high tonals, plucked harmonics, noise, sweeps), then real music: **you supplying a handful of personal killer tracks (castanets/harpsichord/percussion) would materially improve the corpus** — copyright keeps me from downloading the classic HydrogenAudio samples.
4. **Human ABX** — final gate for any default-behavior change (Q-D especially). Your time; I prepare the pairs.

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
