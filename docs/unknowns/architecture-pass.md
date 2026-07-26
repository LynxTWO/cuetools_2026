# Unknowns: Architecture Pass

Current-state refresh: 2026-07-26. This ledger holds architecture questions
that still block an honest claim. Resolved 2026-07-02 findings are retained
below as history.

## Entries

### Patch level and provenance of vendored binaries

- **Area or file:** `ThirdParty/*.dll`, `ThirdParty/Win32/`,
  `ThirdParty/x64/`, codec SDK assets, and
  `CUETools.CTDB.EACPlugin/Interop.HelperFunctionsLib.dll`
- **Concern:** release contracts can identify and hash expected shipped bytes,
  but patch/CVE status, upstream origin, and maintainable replacement paths are
  incomplete for several binaries.
- **Why it matters:** these binaries parse media or run inside trusted desktop
  and EAC processes without first-party source-level assurance.
- **Evidence found so far:** artifact contracts and runtime manifests enumerate
  current membership; submodule pins and local patches are recorded. Remaining
  provenance gaps named by the release evidence include HDCD, LAME, UnRAR, and
  TTA. The reviewed RAR input flow does not extract attacker-controlled paths to
  disk, so the earlier path-traversal reachability concern is closed without
  claiming the parser has no other CVEs.
- **Confidence:** unknown
- **Likely owner:** release maintainer and upstream dependency owners
- **Next best check:** complete `eng/release/native-dependencies.json` entries
  with authoritative upstream/version evidence, then make replace/update/retain
  decisions per shipped binary.
- **Risk level:** high
- **Status:** in progress

### CTDB and gnudb plaintext transports

- **Area or file:** `CUETools.CTDB/CUEToolsDB.cs`,
  `Freedb/FreedbHelper.cs`
- **Concern:** reachable verification, repair, submission, and metadata traffic
  still uses HTTP.
- **Why it matters:** an on-path party can observe or modify responses and
  requests. The repair CRC gate detects ordinary corruption but is not server
  authentication or a cryptographic signature.
- **Evidence found so far:** hardcoded/request construction paths were inspected.
  A 2026-07-02 probe found no usable TLS endpoint for `db.cuetools.net`.
  AccurateRip, MOTD, and default Icecast transport have separately moved to
  HTTPS and are not part of this unknown.
- **Confidence:** unknown
- **Likely owner:** CTDB/gnudb service operators plus client maintainer
- **Next best check:** coordinate a CTDB TLS endpoint and test it before changing
  the client; verify gnudb TLS/protocol options and choose migrate, proxy, or
  retire.
- **Risk level:** high
- **Status:** open

### Hosted and full classic release execution

- **Area or file:** `.github/workflows/CI-windows.yml`,
  `.github/workflows/release-windows.yml`, `eng/ci/`, `eng/release/`,
  `collect_files*.bat`
- **Concern:** current workflow definitions and local gates are inspected, but a
  successful hosted run and the full classic devenv artifact path have not been
  observed on the current source state.
- **Why it matters:** hosted image contents, Visual Studio Installer Projects,
  legacy resources/dependencies, native builds, and artifact packaging can fail
  despite static and local script checks.
- **Evidence found so far:** suite count/skip gates, warning gates, fuzz smoke,
  net20 probe, artifact contracts, plugin manifests, native probes, provenance,
  and SBOM steps exist. The local environment does not provide the complete
  classic Visual Studio toolchain.
- **Confidence:** unknown
- **Likely owner:** release maintainer
- **Next best check:** run CI and release on the intended hosted image, retain
  TRX/artifact-validator/provenance/SBOM evidence, and record tool versions.
- **Risk level:** high
- **Status:** open

## Closed items

- **RAR path-traversal reachability:** resolved 2026-07-02. The reachable
  `RarStream` path uses `Unrar.Test()` and an in-memory callback rather than
  extracting archive paths to disk. Parser patch/provenance work remains open
  above.
- **MusicBrainz client shipping/reachability:** resolved 2026-07-26. The
  legacy client source/project was deleted and direct lookup was retired.
  MusicBrainz tag names and browser links remain, but no in-repo client library
  is loaded.
- **Loose production plugin loading:** resolved 2026-07-26. Packaged plugins are
  manifest-bound and rehashed at the managed/native load boundary. Native module
  paths are checked and bare-name fallback was removed. Loose enumeration is
  reachable only through the explicit `CUETOOLS_ALLOW_UNMANIFESTED_PLUGINS=1`
  local-development switch.
- **AccurateRip HTTPS capability:** resolved. Current client endpoints are
  HTTPS-only.
- **Classic MOTD transport/render boundary:** resolved 2026-07-26. The current
  exact endpoint is bounded HTTPS text; the remote image decode/cache path is
  gone.
- **CUERipper.WPF intent:** deferred by owner decision. It remains a historical
  stub distinct from the live `CUETools.Wpf` application.
- **Git history/remote and ThirdParty submodule restoration:** resolved
  2026-07-02; pinned submodules and local patches were restored and checked.
- **freedb service identity:** resolved 2026-07-02. The client uses the gnudb
  community mirror; its remaining HTTP transport is tracked above.
