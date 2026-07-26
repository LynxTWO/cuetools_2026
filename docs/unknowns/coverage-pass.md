# Unknowns: Coverage Pass

Current-state refresh: 2026-07-26. These entries identify evidence that is
still absent; skipped, excluded, or availability-gated behavior is not counted
as covered.

## Entries

### Legacy TestRipper is non-hermetic and excluded

- **Area or file:** `CUETools/TestRipper/TestRipper.csproj`,
  `CUETools/TestRipper/CDDriveReaderTest.cs`
- **Concern:** the only test initializes from 64 raw audio/C2 captures under
  hardcoded `Y:\Temp\dbg\960` paths and uses the retired VS2010 QualityTools
  adapter.
- **Why it matters:** the historical C2 voting experiment cannot run from a
  clean checkout and must not be confused with current automated ripper
  coverage.
- **Evidence found so far:** source and
  `eng/ci/test-suites.json` were inspected. The project is explicitly excluded
  with the same reason. The separate modern `CUETools.Ripper.Tests` suite
  discovered and passed 8/8 tests locally, but does not load these captures or
  exercise a physical drive.
- **Confidence:** verified
- **Likely owner:** upstream/repo maintainer
- **Next best check:** replace the capture dependency with deterministic
  generated fixtures and a current test adapter, or remove the experiment with
  an explicit owner decision.
- **Risk level:** medium
- **Status:** open

### Hardware and availability-gated runtime matrix

- **Area or file:** `CUETools.Ripper.SCSI/`, `CUETools.Codecs.WMA/`,
  `CUETools.Codecs.Icecast/`, `CUETools.Codecs.FLACCL/`,
  `CUETools.Wpf/`
- **Concern:** local automated tests cannot cover physical optical drives, every
  Windows Media codec installation, real Icecast TLS/auth servers, Mono's
  private `HttpWebRequest` behavior, or OpenCL devices.
- **Why it matters:** these are live product paths whose correctness depends on
  hardware, installed codecs, service behavior, or runtime internals.
- **Evidence found so far:** service-level tests and source contracts pass. The
  real WMA round trip was one of two expected codec skips; Icecast integration,
  physical Test & Copy/repair, and OpenCL matrices were not available locally.
- **Confidence:** unknown
- **Likely owner:** release/test maintainer
- **Next best check:** maintain a named manual/integration matrix with a known
  disc/image, WMA-capable Windows host, controlled Icecast TLS/auth endpoint,
  supported Mono target if retained, and representative OpenCL hardware.
- **Risk level:** high
- **Status:** open

### First current hosted CI/release evidence

- **Area or file:** `.github/workflows/CI-windows.yml`,
  `.github/workflows/release-windows.yml`
- **Concern:** local TRX and repository scripts do not establish that the
  GitHub-hosted image can complete both legacy and modern lanes or package the
  classic artifact.
- **Why it matters:** tool/image drift can leave a documented gate unreachable
  or produce a different artifact than local checks.
- **Evidence found so far:** local results discovered 388 tests, passed 381,
  failed 0, and skipped 7 expected. Workflow steps and discovery/skip gates are
  present. A current successful hosted run has not been supplied.
- **Confidence:** unknown
- **Likely owner:** CI/release maintainer
- **Next best check:** run both workflows from the intended branch and retain
  per-suite TRX, artifact-validator, native-probe, provenance, and SBOM output.
- **Risk level:** high
- **Status:** open

### ProgressODoom and ttalib provenance

- **Area or file:** `ProgressODoom/`, `ttalib-1.1/`
- **Concern:** both appear to be mirrored third-party source, but the upstream
  version and local modifications are not recorded.
- **Why it matters:** mirrored code should follow upstream security/correctness
  fixes and should not be treated as fully first-party reviewed.
- **Evidence found so far:** directory/project shape and TTA version suffix;
  no authoritative upstream diff is recorded.
- **Confidence:** inferred
- **Likely owner:** dependency/release maintainer
- **Next best check:** identify authoritative upstream revisions, diff local
  changes, and record mirror-versus-owned classification and artifact reach.
- **Risk level:** low
- **Status:** open

## Closed items

- **CUEPlayer and eac3ui internals unscanned:** closed 2026-07-02. Their main
  forms/entrypoints, settings, Icecast threading, typed DataSet, and eac3to
  process boundary were inventoried. Later credential and endpoint hardening is
  reflected in the current architecture/logging documents.
- **TestProcessor fixtures missing:** closed 2026-07-26. Fixture copy/layout is
  now part of the project. The suite discovered eight tests, passed seven, and
  skipped only the deliberately ignored `CTDBResponseTest` tied to
  `Z:\ctdb.xml`.
- **Do current automated suites pass locally?:** closed for the recorded
  2026-07-26 run. Codecs 107/109 with 2 skips, parity 18/22 with 4 skips,
  Processor 7/8 with 1 skip, modern ripper 8/8, and WPF 241/241. Aggregate: 388
  discovered, 381 passed, 0 failed, 7 expected skipped. This dated result is not
  a claim about future commits or excluded environments.
- **FlaCuda/CUDA.NET release reachability:** closed 2026-07-23. FlaCuda projects
  and their CUDA.NET dependency were deleted in commit `4e1b02d`; they have no
  current scope or release reachability.
- **BitReader and Windows reserved-name regression coverage:** closed
  2026-07-26 at the automated-test boundary. The fixes and corresponding test
  cases are present; broad malformed-codec/filesystem matrices remain bounded by
  the open runtime entry above.
