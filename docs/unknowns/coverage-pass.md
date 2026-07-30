# Unknowns: Coverage Pass

Current-state refresh: 2026-07-30. These entries identify evidence that is
still absent; skipped, excluded, or availability-gated behavior is not counted
as covered.

## Entries

### Residual hardware and availability-gated runtime matrix

- **Area or file:** `CUETools.Ripper.SCSI/`, `CUETools.Codecs.WMA/`,
  `CUETools.Codecs.Icecast/`, `CUETools.Codecs.FLACCL/`,
  `CUETools.Wpf/`
- **Concern:** one host/device/service success path cannot cover optical-drive failure
  behavior, every Windows Media installation, Icecast TLS/certificate and Mono
  behavior, or every OpenCL vendor/driver.
- **Why it matters:** these are live product paths whose correctness depends on
  hardware, installed codecs, service behavior, or runtime internals.
- **Evidence found so far:** availability is no longer the broad blocker. A real net8
  WMA Lossless encode/finalize/independent-decode/PCM verification passed. Disposable
  Icecast 2.5.0 passed auth rejection, source streaming, exact metadata, listener
  bytes, flush/close, and teardown. FLACCL passed on an RTX 3060 across OpenCL modes
  0-8, two CPU workers, 24-bit input, and the exact 4096-sample boundary. H: and K:
  completed full-disc read-only verification with zero read errors, H: completed a
  full 11-track FLAC rip, and both drives answered simultaneous SCSI inquiry/TOC. H:
  also passed same-drive Test & Copy with a confirmed 786,432-byte flush, two
  independent full reads, matching AR 107/424 and CTDB 114/544, zero reread/failed
  windows, 11 FLAC outputs, and result/`rip.verify` proof that lossless output
  verification decoded and compared the encoded files.
  A deliberately damaged known image also passed opt-in CTDB repair, independent
  post-verification, and source-hash preservation. Remaining gaps are failure
  injection/cancellation/disagreement, cross-vendor OpenCL, Icecast HTTPS/certificate
  and Mono, and release-lane repeatability. A first H: attempt's transient SCSI
  ASC/ASCQ 08/0A during an overlapping build is retained as diagnostic evidence; the
  isolated final-source rerun crossed the same Copy phase and passed. Simultaneous
  H:/K: jobs and damaged K:'s completed Test & Copy/CTDB repair add real
  multi-drive and error-media evidence without claiming a universal drive matrix.
- **Confidence:** medium
- **Likely owner:** release/test maintainer
- **Next best check:** retain the observed fixtures in a named repeatable matrix,
  then add optical cancellation/disagreement/error media, a second OpenCL
  vendor, Icecast HTTPS/certificate cases, and the supported Mono target if
  retained.
- **Risk level:** high
- **Status:** open

### First current hosted CI/release evidence - closed 2026-07-30

- **Area or file:** `.github/workflows/CI-windows.yml`,
  `.github/workflows/release-windows.yml`
- **Concern:** local TRX and repository scripts do not establish that the
  GitHub-hosted image can complete both legacy and modern lanes or package the
  classic artifact.
- **Why it matters:** tool/image drift can leave a documented gate unreachable
  or produce a different artifact than local checks.
- **Evidence found so far:** all four workflows parse and pass official
  `actionlint` v1.7.12 locally. Source-bound FFmpeg run `30516040154`, classic
  CI `30518472651`, WPF CI `30518472662`, and release run `30518479906`
  succeeded with zero final annotations. The classic and modern lanes
  discovered 630 tests, passed 623, and retained seven declared skips. The
  downloaded release independently passed both artifact contracts, exact
  SHA-256/SPDX closures, populated CycloneDX graphs, native-source closure, and
  clean provenance.
- **Confidence:** high
- **Likely owner:** CI/release maintainer
- **Next best check:** refresh the retained source-bound evidence after
  runner-image, action, Visual Studio, SDK, or release-policy changes.
- **Risk level:** high
- **Status:** closed

### ProgressODoom and residual ttalib provenance

- **Area or file:** `ProgressODoom/`, `ttalib-1.1/`
- **Concern:** both appear to be mirrored third-party source, but the upstream
  version and local modifications are not recorded.
- **Why it matters:** mirrored code should follow upstream security/correctness
  fixes and should not be treated as fully first-party reviewed.
- **Evidence found so far:** TTA 1.1 is traced to the official SourceForge archive
  and its exact reviewable 2009 import delta. The remaining TTA gap is a checksum
  captured contemporaneously with that import. ProgressODoom still lacks an
  authoritative upstream revision/diff record. Separate x64/Win32 TTA C++/CLI builds
  pass, but runtime round-trip/corpus coverage is still needed before changing the
  observed packing-state and sample-count-narrowing warnings.
- **Confidence:** medium
- **Likely owner:** dependency/release maintainer
- **Next best check:** preserve the recovered TTA evidence and resolve the remaining
  historical checksum gap if an immutable source can be found; identify
  ProgressODoom's authoritative upstream revision and diff local changes.
- **Risk level:** low
- **Status:** open

## Closed items

- **TestRipper production-vote fixture:** closed 2026-07-26. The old private
  `Y:\Temp` captures and stale copied C2 algorithm were replaced by SDK net47 tests
  against the same production `SecureSectorVote.CorrectSector` helper as `SCSIDrive`.
  The canonical run passed all 3 deterministic recovery/confidence/C2-plane tests
  with 0 failures and 0 skips.
- **CUEPlayer and eac3ui internals unscanned:** closed 2026-07-02. Their main
  forms/entrypoints, settings, Icecast threading, typed DataSet, and eac3to
  process boundary were inventoried. Later credential and endpoint hardening is
  reflected in the current architecture/logging documents.
- **TestProcessor fixtures missing:** closed 2026-07-26. Fixture copy/layout is
  now part of the project. The suite discovered eight tests, passed seven, and
  skipped only the deliberately ignored `CTDBResponseTest` tied to
  `Z:\ctdb.xml`.
- **Do current automated suites pass locally?:** the earlier 2026-07-26
  388/381/7 run is retained only as historical evidence. Current aggregate counts
  are refreshed by the final canonical gate after all changes land; no stale total is
  promoted to current evidence.
- **FlaCuda/CUDA.NET release reachability:** closed 2026-07-23. FlaCuda projects
  and their CUDA.NET dependency were deleted in commit `4e1b02d`; they have no
  current scope or release reachability.
- **BitReader and Windows reserved-name regression coverage:** closed
  2026-07-26 at the automated-test boundary. The fixes and corresponding test
  cases are present; broad malformed-codec/filesystem matrices remain bounded by
  the open runtime entry above.
