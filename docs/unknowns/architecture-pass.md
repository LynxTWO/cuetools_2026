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
  current membership. UnRAR has been upgraded to official RARLAB 7.23.0 x86/x64
  DLLs from a versioned signed SFX; signatures, byte identity, required exports,
  and real production `RarStream` full-read/backward-seek behavior pass in both
  process architectures. A committed RAR5 fixture also exposed and fixed a
  backward-seek/stale-EOF race, with 20/20 repeated regression runs. The prior
  6.11 bytes remain traced to an import-era
  snapshot of RARLAB's signed `UnRARDLL.exe`. The LAME 3.100
  binaries are byte-identical to the
  SHA-256-recorded RareWares x86/x64 archives; TTA 1.1 is traced to the
  official SourceForge archive and its reviewable 2009 import delta; and HDCD
  is attributed and licensed to Christopher Key. Source-built codec patches
  are now SHA-256 pinned. Remaining gaps are narrower: HDCD's exact
  source/revision/build recipe, RareWares' exact LAME source revision and
  flags, and a checksum for the TTA archive captured contemporaneously with
  the import. The reviewed RAR input flow does not extract
  attacker-controlled paths to disk, so the earlier path-traversal reachability
  concern is closed without claiming the parser has no other CVEs. The detached
  `WavPack`, `WindowsMediaLib`, `flac`, and `taglib-sharp` submodules also contain
  pre-existing local changes; their intent and provenance must be reconciled rather
  than overwritten during an upgrade.
- **Confidence:** medium
- **Likely owner:** release maintainer and upstream dependency owners
- **Next best check:** reconcile and preserve the four dirty submodule worktrees,
  replace the remaining unverifiable binary build inputs with reproducible source
  builds or document an explicit retain decision, then execute pending version
  upgrades one codec at a time.
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
  successful hosted run and final frozen classic artifact receipts have not been
  observed on the current source state.
- **Why it matters:** hosted image contents, Visual Studio Installer Projects,
  legacy resources/dependencies, native builds, and artifact packaging can fail
  despite static and local script checks.
- **Evidence found so far:** suite count/skip gates, warning gates, fuzz smoke,
  net20 probe, artifact contracts, plugin manifests, native probes, provenance,
  SBOM steps, and local `actionlint` evidence exist. The native x64 dependency
  gate passes. A classic AnyCPU pass also found and fixed a real declared-net20 blocker:
  slip-correlation exposed tuple metadata unavailable to that framework; its named
  result/out-parameter replacement is in place. The next pass found net35-only
  `SortedSet<T>` in the adaptive-speed ladder; a net20-compatible ordered `List<T>`
  preserves the strictly ascending rungs and 97% cutoff semantics. After those fixes,
  the redirected-output classic AnyCPU solution build completed with 53 succeeded and
  0 failed. The x64 and Win32 configurations each completed with 2 succeeded,
  0 failed, and 59 skipped configuration entries. TTA compiled and linked for both
  into valid CLR PE files. Installer Projects 3.0.0, with
  `DisableOutOfProcBuild`, passed 8 projects with 0 failures and produced a
  929,792-byte MSI. This route used the Visual Studio 18 resolver with the VS2022
  v143 toolset rather than the exact hosted VS2022 image.
- **Confidence:** medium
- **Likely owner:** release maintainer
- **Next best check:** finish the local frozen 97-path artifact validation and direct
  CUEPlayer/receipt checks. Repeat the complete gate on the intended hosted image and retain
  TRX/artifact-validator/provenance/SBOM evidence with tool versions.
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
  paths are checked and bare-name fallback was removed. Supported per-user plugins
  are enrolled into a separate exact-hash DLL-only manifest under
  `%AppData%\CUETools2026\plugins`; replacement is explicit and backed up. Loose
  discovery also requires exact encoder/decoder/ripper contract identity rather than
  accepting an interface short-name lookalike. Compression-plugin attributes require
  the real `ICompressionProvider` contract, and HDCD registration requires its complete
  destination/filter/formattable/constructor shape. Loose enumeration is reachable
  only through the `CUETOOLS_ALLOW_UNMANIFESTED_PLUGINS=1` local-development switch.
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
