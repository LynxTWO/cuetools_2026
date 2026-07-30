# Decisions Needed

Items surfaced by the anti-dark-code passes that require a human decision before action. The autonomous loop parks them here instead of guessing.

Vocabulary: `.claude/skills/anti-dark-code/references/00-conventions.md`.

**User decisions recorded 2026-07-02.** Implementation happens after the anti-dark-code review passes complete (user's sequencing).

## Approved decisions and current status

### D1. AccurateRip: switch lookups to HTTPS - DONE

- **Decision:** flip HTTP -> HTTPS. Server answers HTTPS; no reason to stay on HTTP.
- **Evidence:** `www.accuraterip.com` answered HTTPS 200 in a 2026-07 probe.
  `CUETools.AccurateRip\AccurateRip.cs:837,1248` now uses HTTPS for both the disc
  response and `DriveOffsets.bin`.
- **Policy:** there is no HTTP downgrade. A TLS failure is a failed external check,
  not permission to accept unauthenticated confidence data.

### D2. CTDB HTTPS - DONE 2026-07-02 (tracking issue filed)

- **Decision:** file an issue / ask upstream to enable TLS on db.cuetools.net; revisit the client once the server answers TLS.
- **Done:** filed tracking issue LynxTWO/cuetools_2026#1 (had to enable Issues on the fork first; the third-party gchudov repo was NOT posted to). Includes upstream-ready ask text. Client `CUEToolsDB.cs` left unchanged until the server answers TLS.

### D3. unrar.dll upgrade - DONE 2026-07-26

- **Decision:** upgrade the bundled unrar DLL.
- **Historical evidence:** the prior 6.11 DLLs predated the 6.12 fix for
  CVE-2022-30333. The reachable `RarStream` path uses `Unrar.Test()` plus an
  in-memory callback and never extracts attacker-selected paths, so that traversal
  vector was not reachable through normal CUETools use. The prior bytes remain traced
  to an import-era snapshot of RARLAB's signed `UnRARDLL.exe`.
- **Completed evidence:** both packaged architectures now use official RARLAB UnRAR
  7.23.0 from the versioned, 825,320-byte `unrardll-723.exe` SFX. The SFX and both
  DLLs have valid win.rar GmbH Authenticode signatures; the checked-in x86 DLL is
  339,664 bytes and the x64 DLL is 412,368 bytes, with exact SHA-256 values pinned in
  [`native-dependencies.json`](../../eng/release/native-dependencies.json). Both
  expose the wrapper's six required exports.
- **Runtime result:** the production `RarCompressionProvider`/`RarStream` path read
  RARLAB's real `test.rar` under x64 PowerShell and x86 Windows PowerShell, listed all
  14 entries, matched the exact full-read payload, and matched the independent
  backward-seek SHA-256 result. The native inventory, notices, and 7.23 license were
  updated. A committed 280-byte RAR5 fixture and its 2,083-byte source oracle add this
  production path to TestCodecs. Its first run exposed a real backward-seek race:
  `Read` could treat the previous pass's stale EOF as final before the worker
  acknowledged rewind. The wait predicate now remains blocked while rewind is
  pending; the focused test passes and 20/20 repeated no-build full-read/seek runs
  passed.

### D4. SharpZipLib upgrade - DONE 2026-07-02

- **Done:** `CUETools.Compression.Zip` now uses the SharpZipLib 1.4.2 NuGet package for net47 + netstandard2.0; net20 keeps the vendored 0.85.5 DLL (modern SharpZipLib dropped net20). Adapted the password path for the modern API: `GetInputStream` returns a plain `Stream` (not `ZipInputStream`) and AES needs the password on the `ZipFile` *before* opening the entry, so `ZipCompressionProvider.Decompress` now requests and sets the password up front instead of lazily on first read. Both `collect_files.bat` and `collect_files_debug.bat` fixed to ship the modern DLL from the build output, not the old vendored copy.
- **Verified:** net47 + netstandard2.0 build; a net8 round-trip harness against the built provider passes all 6 checks (contents list, full read byte-exact, Length, forward seek, backward-seek reopen, and AES-encrypted read via the PasswordRequired event).

### D6. MusicBrainz client replacement - DONE 2026-07-02

- **Premise changed - there is no live MusicBrainz client to replace.** Full analysis in `docs/review/musicbrainz-replacement-scope.md`. In short: the `MusicBrainz/` musicbrainz-sharp library is dead code (not built, referenced by no project), and CUERipper's direct MusicBrainz query is commented out (`frmCUERipper.cs:893-918`). CUETools gets MusicBrainz metadata via **CTDB's server-side proxy** (`cueSheet.CTDB.Metadata`) plus a Freedb/gnudb fallback. So a "replacement" would reintroduce a direct-to-MusicBrainz path the project deliberately removed - a product decision, not a mechanical swap.
- **DONE 2026-07-02 - you chose A.** Deleted the dead `MusicBrainz/` project (musicbrainz-sharp mirror), `ThirdParty/MusicBrainz.dll`, the `CUERipper.csproj` datasource reference + file, and the sln solution-items entry. Removed the commented-out direct-MB block in `frmCUERipper.cs` (replaced with a note pointing at the CTDB-proxy path). MB metadata continues via CTDB + Freedb/gnudb. The "look up on musicbrainz.org" website buttons (frmChoice, frmCUERipper) are unrelated to the library and were kept (and flipped http->https). TestCodecs 34/34 green; GUI projects verified by CI/devenv on push.

### D7. CUEControls resgen under dotnet build - PARTIALLY DONE; old-style GUIs deferred to R12

- **Decision:** fix so the solution builds without full Visual Studio.
- **What shipped 2026-07-02:** `Directory.Build.targets` at repo root adds `GenerateResourceUsePreserializedResources=true` + `System.Resources.Extensions` for net47 first-party projects, **gated on `$(MSBuildRuntimeType) == 'Core'`** so it applies ONLY to `dotnet build` and is a provable no-op under devenv/CI (the shipping build's resource format and runtime deps are unchanged - important because CUEControls loads binary icons at runtime, and forcing preserialized resources into the shipping build would have required deploying a new DLL). Result: all **SDK-style** net47 first-party projects (CUEControls + the codec/lib projects) now build under `dotnet build`.
- **Progress 2026-07-29:** the reachable FLACCL plugin and command host are now
  SDK-style net47 projects. Locked Core restore, Core/full-MSBuild builds, exact
  resource/kernel comparisons, executable architecture checks, and the live
  RTX 3060 matrix pass.
- **Why not complete:** the old-style WinForms GUIs (`CUETools`, `CUERipper`,
  `CUEPlayer`, `CUETools.eac3ui`) still need SDK conversion plus real UI/resource
  verification. The disabled, unconsumed, non-building `CLParity` experiment
  was retired under R89 rather than modernized into a false feature.
- **Verified:** CUEControls and the FLACCL pair build under `dotnet build`;
  TestParity 18/18 and TestCodecs 34/34 remain the historical focused baseline;
  the FLACCL live matrix is recorded under R88.

## Previously deferred decisions

### D5. Delete dead projects/binaries - PARTIALLY DONE

- The dead MusicBrainz mirror was removed after D6 selected CTDB-proxied metadata.
- FlaCuda and its console wrapper were deleted on 2026-07-23 after confirming they
  were absent from the solution and unreferenced outside their own directories.
  FLACCL is the live OpenCL path.
- Do not infer that every old binary is dead: `Freedb.dll` remains reachable in the
  current products, and legacy/package reachability must be checked per artifact.

## R12 / R13 - large programs (R13 product boundary resolved)

The bucket-A remediation items are now done (R1 + R15 decoder hardening, R11
CleanseString, R10a TestProcessor fixtures). What is left in R12 (modernization) and
R13 (codec refresh) is behavior-affecting. The user has now authorized its product
boundary; only the explicitly recorded vendor/legal boundaries remain external.

### R12 decision - which modernization slice next, and how to verify GUIs

- **What is already done** (see D1, D2, D4, D7): HTTPS lookups, SharpZipLib 1.4.2,
  the Core-gated resgen fix so SDK-style net47 projects build under `dotnet build`,
  and the first paired R12 conversion (`CUETools.Codecs.FLACCL` plus
  `CUETools.FLACCL.cmd`) with live OpenCL evidence.
- **The remaining sub-decision:** the remaining big pieces - SDK-style conversion of the
  old-style WinForms GUIs (`CUETools`, `CUERipper`, `CUEPlayer`, `CUETools.eac3ui`),
  then async/`HttpClient`, then installer - all need the GUI to be **run** to confirm
  resource/icon loading and behavior. The current classic baseline is locally green:
  AnyCPU completed 53/0, x64 and Win32 each completed 2/0 with 59 skipped
  configuration entries, TTA compiled and linked for both, and the targeted Installer
  Projects build completed 8/0 and produced a 929,792-byte MSI. That removes the
  former local toolchain blocker; it does not answer who will perform interactive GUI
  behavior checks for each future conversion. Options:
  - (A) You (or CI with a GUI runner) verify each converted GUI; I do the conversions in
    reviewable branches one project at a time.
  - (B) Defer all GUI-touching modernization until the codec/rollout work settles, and I
    keep to non-GUI slices (build/test plumbing, library-layer cleanups).
  - (C) Pick one specific GUI to convert first as a pilot.
  - **Recommend (A) with `CUETools.eac3ui` as the pilot** - it is the smallest GUI.

### R13 decision - RESOLVED 2026-07-29

- **Current pins and upstream check:** libFLAC 1.5.0, WavPack 5.9.0,
  Monkey's Audio 13.20, and taglib-sharp 2.3.0.0 match current upstream releases.
  Vendored LAME 3.100 trails the newly
  released official 4.0. The standalone, currently unshipped FFmpeg 7.1.1 path
  trails 8.1.2.
- **Why the remaining bumps are separate:** taglib-sharp contains existing local
  work that must be preserved; LAME has
  a new major release without a packaged MP3 decode/quality gate; and FFmpeg is
  not part of either primary
  artifact. The current native build/probe gate is available, but it does not
  replace per-codec compatibility and corpus evidence.
- **Product choice:** add xHE-AAC, OptimFROG, WavPack, and every redistributable
  encoder available from `C:\_Audio Codecs_`. User-provided newer executables must
  override bundled copies.
- **Completed implementation:** WavPack 5.9.0 is source-built for both
  architectures. The WPF catalog includes Musepack, TAK, Vorbis, Opus, qaac,
  exhale/xHE-AAC, and OptimFROG with exact contracts, archival defaults,
  implementation selection, help, aliases, receipt-bound imports, and user
  override precedence. The package includes hash-pinned Opus Tools and oggenc2;
  oggenc2 ships with its exact source archive. Release and runtime checks fail
  closed on hash drift.
- **No product decision remains for import-only codecs:** qaac requires an Apple
  runtime; TAK is proprietary; exhale grants no patent rights; Musepack has not
  been added to the package until its matching source-compliance set is curated.
- **One external action remains before OptimFROG can be bundled:** its license
  requires notification to the author. CUETools supports a user import and a
  verified lossless encode today; packaging must wait for a project-owner
  notification rather than inventing an identity or silently violating the term.

## Resolved / actioned

- **D1 AccurateRip HTTPS - DONE 2026-07-02.** Flipped both `http://www.accuraterip.com` literals to `https://` (`AccurateRip.cs:833` dBAR lookup, `:1247` DriveOffsets.bin). No http fallback: a failed AR lookup degrades to "not verified" (corroborative, no data loss), so retrying over cleartext buys nothing. Verified: AccurateRip builds; the HTTPS dBAR path returns 404 for a fake id (proves TLS+routing) and DriveOffsets.bin returned 200 earlier; TestParity 18/18 green. Committed as `27b565f`.
