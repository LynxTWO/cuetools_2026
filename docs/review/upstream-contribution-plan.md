# Upstream Contribution Plan

Current inventory: 2026-07-31.

## Objective

Move generally useful CUETools fixes upstream as small, independently reviewable
pull requests. Do not rebase or submit the CUETools 2026 fork as one change.

`gchudov/cuetools.net` `master` is an ancestor of the fork. The merged fork delta
contains 307 commits and 640 changed files. Draft PR 3 adds one more commit. That
history is evidence and a source of candidate patches, not the history to publish
upstream.

## Extraction contract

Each upstream contribution must:

1. Start from the current `upstream/master` in a separate worktree.
2. Implement one behavior or one inseparable correctness invariant.
3. Omit fork-only audit references, branding, UI, release policy, binaries, and
   unrelated cleanup.
4. Add or adapt focused tests when the upstream test structure can express the
   behavior.
5. Build every directly affected upstream target and run the smallest relevant
   existing suite.
6. Pass `git diff --check` and contain no vendor-worktree changes.
7. Push to a dedicated fork branch and open a draft PR against upstream.
8. Wait for review or CI feedback before opening another PR that touches the same
   subsystem.

## Dependency-ordered queue

| ID | Contribution | Source evidence | State | Boundary before publication |
| --- | --- | --- | --- | --- |
| U01 | AccurateRip lookups use HTTPS with no cleartext fallback | `27b565f`, upstream draft PR 402 | submitted | one production file; net20/net47/netstandard2.0 build passed; both HTTPS routes confirmed |
| U02 | Managed FLAC `BitReader` bounds malformed input | `624879c` | queued | reconstruct a focused malformed-input regression test before publication |
| U03 | ALAC inline reader and frame sample count stay in bounds | `6243757` | queued | keep separate from U02 unless upstream requests one decoder-hardening PR; add focused tests |
| U04 | TestProcessor copies its tracked CUE fixtures | `2492d95` | queued | test-infrastructure-only prerequisite for later processor regressions |
| U05 | Windows output names reject reserved devices and trailing dots/spaces | `e93532e` | queued | adapt tests to upstream's existing framework; avoid importing fork naming policy |
| U06 | SharpZipLib moves from 0.85.5 to 1.4.2 | `a71c13c` | queued | dependency/license/API review and archive regression suite |
| U07 | Remove the unreferenced legacy MusicBrainz client | `63d7de6` | queued | prove zero production/project references on upstream and keep metadata behavior unchanged |
| U08 | Lossless/native encoders honor terminal failure returns | `2e3a455`, later codec hardening | queued | split by codec and include real encode/finalize/decode evidence |
| U09 | Optical/SCSI recovery fixes | later R55-R69 commits | deferred | one observed classifier route per PR, deterministic policy tests, then named hardware evidence |
| U10 | Classic project SDK conversions | `27b3d22`, `9fd819d`, `531a1c3`, `6711d57` | deferred | one application plus its inseparable control dependency per PR; exact resource/PE/runtime parity |
| U11 | Hosted CI and release modernization | July 29-30 build commits | deferred | ask upstream whether it wants policy/tooling changes before extracting privileged CI/CD work |
| U12 | Shared native plugin path binding and codec health | `99041d4` | deferred | separate shared wrapper correctness from the CUETools 2026 picker and manifest architecture |

## Do not upstream by default

- CUETools 2026 WPF product UI, branding, artwork, visualization, and product
  defaults. These are fork product decisions, not bug fixes for classic CUETools.
- Fork audit journals, local skills, screenshots, hardware logs, and remediation
  numbering. Translate relevant evidence into the PR body instead.
- SignPath policy, fork release manifests, fork privacy copy, and CUETools 2026
  packaging. Offer isolated tooling only if upstream asks for it.
- Bundled codec executables, SDK archives, reconstructed vendor payloads, or
  provenance records whose redistribution terms and ownership are fork-specific.
- Generated output, build artifacts, local drive calibration, verification
  history, and user-library evidence.

## Publication order

U01 is submitted as upstream draft PR 402. It is independent,
security-relevant, and small enough to reveal upstream review preferences
without coupling later work to it. Follow with U02 and U03 as separate
memory-safety fixes unless upstream asks to combine them.
Use U04 before U05 if the upstream test layout otherwise prevents the path
regression from running. Do not stack SCSI, project conversion, codec packaging,
or CI contributions while an earlier PR in the same subsystem is unresolved.
