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

### D7. CUEControls resgen under dotnet build - DONE 2026-07-29

- **Decision:** fix so the solution builds without full Visual Studio.
- **What shipped 2026-07-02, hardened 2026-07-30:** `Directory.Build.targets` at repo root adds `GenerateResourceUsePreserializedResources=true` + `System.Resources.Extensions` for net47 first-party projects only when `$(MSBuildRuntimeType) == 'Core'`, so `dotnet build` can process binary resources. Full MSBuild declares the same package identity only as `ExcludeAssets=all` restore evidence, and does the same for Core's implicit net20 reference-assembly package. The two hosts therefore share reviewed lock graphs while devenv/CI keeps the classic resource format and shipping runtime closure unchanged.
- **Progress 2026-07-29:** the reachable FLACCL plugin and command host are now
  SDK-style net47 projects. Locked Core restore, Core/full-MSBuild builds, exact
  resource/kernel comparisons, executable architecture checks, and the live
  RTX 3060 matrix pass. BluTools (`CUETools.eac3ui`) is now the first classic
  GUI pilot: its API/member shape, PE flags, generated config, 19 embedded
  images, and live WPF startup are preserved. CUERipper and ProgressODoom
  followed with preserved managed/PE/config/localization/image contracts and
  old/new live main-form parity. CUEPlayer followed with preserved managed,
  PE/config/decoded-image contracts and old/new live main-form parity. Classic
  CUETools completed the set with preserved managed/PE/config/main/localized
  resource contracts and old/new live main-form parity. Its 237 exact duplicate
  resource nodes were safely removed and are now guarded against recurrence.
- **Verified:** CUEControls, the FLACCL pair, BluTools, CUERipper,
  ProgressODoom, CUEPlayer, and CUETools build under `dotnet build`;
  TestParity 18/18 and TestCodecs 34/34 remain the historical focused baseline;
  the FLACCL live matrix is recorded under R88 and the BluTools equivalence
  evidence under R90. Final hosted classic run `30518472651` passes the
  converted solution graph, and release run `30518479906` independently closes
  the exact 97-file classic artifact/receipt boundary.

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
  `CUETools.FLACCL.cmd`) with live OpenCL evidence, followed by the first
  classic-GUI pilot (`CUETools.eac3ui` / BluTools), the CUERipper /
  ProgressODoom pair, CUEPlayer, and classic CUETools.
- **The remaining sub-decision:** the remaining big pieces - async/`HttpClient`
  and installer modernization - all need the GUI to be **run** to confirm
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
  - **Classic GUI conversion complete:** BluTools, CUERipper/ProgressODoom,
    CUEPlayer, and CUETools were converted and validated one captured contract
    at a time under R90-R93.

### R13 decision - RESOLVED 2026-07-29

- **Current pins and upstream check:** libFLAC 1.5.0, WavPack 5.9.0,
  Monkey's Audio 13.20, and taglib-sharp 2.3.0.0 match current upstream releases.
  Vendored LAME 3.100 trails the newly
  released official 4.0. The standalone, currently unshipped FFmpeg path is now
  current at 8.1.2 with FFmpeg.AutoGen 8.1.0 and both-architecture runtime
  evidence.
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
  runtime; TAK is proprietary; and exhale grants no patent rights. Musepack is
  no longer in this group: R85 packages CUETools' deterministic r495 build with
  its complete corresponding source, reviewed patch, recipe, build notes, and
  LGPL-2.1 notice.
- **OptimFROG notification sent:** Daniel Boyd notified the author at the
  license-specified address on 2026-07-29, identified this repository, and
  described the intended unmodified x64 CLI redistribution. The notification
  condition is no longer an owner-decision blocker; package integration remains
  a separate exact-byte/license/artifact change.
- **Signing identity remains external:** the repository now enforces the
  SHA-256 Authenticode/RFC 3161 policy and refuses an unsigned tagged release.
  The owner still needs to acquire/select the public-trust certificate and
  configure the protected GitHub environment values described in
  `docs/security/release-signing.md`.

## Open decisions

### D8. Classic secure-rip calibration and cache-defeat port - RESOLVED 2026-08-01 (B)

- **User chose (B):** classic stays a frozen legacy surface with no behavior
  change. Implemented same day: the secure-mode slider tooltip states that
  classic re-reads do not defeat the drive cache and points at CUETools 2026,
  and the EAC-style log gains one behavior-conditioned note line (emitted only
  when secure re-reads actually ran without cache defeat, so WPF logs never
  carry it). R113's honest CRC comparison and truthful header lines stand.
  CLAUDE.md's calibration/held-state invariants are now scoped to the modern
  path. R117 closes.

Original decision record:

### D8 (original). Classic secure-rip calibration and cache-defeat port - OPEN 2026-08-01

- **Finding:** classic CUERipper and `CUETools.Ripper.Console` run Secure and
  Paranoid with no calibration gate and no cache defeat (`frmCUERipper.cs:715`;
  `ICDRipper` lacks the members), so on a caching drive the secure vote can
  agree with the drive cache. The WPF path fails closed on exactly this. Full
  evidence: R113/R117 and the 2026-08-01 adversarial addendum.
- **What is already being fixed without a decision (R113):** classic Test CRC
  vs Copy CRC comparison, truthful log lines, and Paranoid persistence.
- **Decision required:** pick one:
  - (A) Port the WPF calibration/cache-defeat gate to the classic secure path.
    Honest but behavior-changing: classic secure rips on caching drives would
    refuse until calibrated, and `ICDRipper` gains members.
  - (B) Freeze classic as a legacy surface: keep R113's honest logs, add a
    one-line log/UI note that classic secure mode does not defeat drive
    caching, and point users at the WPF app for assured rips. No behavior
    change.
  - (C) Retire classic Secure/Paranoid labels (Burst-only), strongest truth,
    largest user-visible change.
- **Recommendation:** (B) now; revisit (A) only if classic remains a shipped
  rip surface after the WPF app is the default.

### D9. Burst-mode re-read semantics - RESOLVED 2026-08-01 (gate to Secure/Paranoid)

- **User chose:** gate the deep-recovery extension to quality > 0. Implemented
  same day: the engine derives `deepRecoveryActive` from the setting AND the
  quality (extension, slip probe, and policy window all keyed on it),
  `RipService` gates its floor-speed drops and the recorded `DeepRecovery`
  flag the same way and logs the gated state, and the Burst tooltip plus the
  deep-recovery settings tooltip both say Burst keeps the classic 16-pass
  cap. Test & Copy stays forced-Secure by design. Contract tests pin both
  layers.

Original decision record:

### D9 (original). Burst-mode re-read semantics - OPEN 2026-08-01

- **Observation (user, live):** Burst appears to "get recovery". Two causes,
  both by design today: Test & Copy always forces at least Secure plus cache
  defeat regardless of the dropdown (documented in `IRipService`), and in a
  plain Rip the engine has always re-read windows the vote flags even at
  quality 0 (legacy 16-pass cap), which default-on DeepRecovery now extends
  with the progress-aware zone.
- **Decision required:** keep and label, or gate the deep-recovery extension
  to quality > 0 so Burst retains only the legacy 16-pass behavior.
- **Recommendation:** gate the extension to quality > 0 (Burst stays "fast
  until trouble" with the historical cap; Secure/Paranoid keep deep
  recovery), and say in the Burst tooltip that flagged windows still re-read
  up to 16 passes. Test & Copy stays forced-Secure: it is the assured mode.

## Resolved / actioned

- **D1 AccurateRip HTTPS - DONE 2026-07-02.** Flipped both `http://www.accuraterip.com` literals to `https://` (`AccurateRip.cs:833` dBAR lookup, `:1247` DriveOffsets.bin). No http fallback: a failed AR lookup degrades to "not verified" (corroborative, no data loss), so retrying over cleartext buys nothing. Verified: AccurateRip builds; the HTTPS dBAR path returns 404 for a fake id (proves TLS+routing) and DriveOffsets.bin returned 200 earlier; TestParity 18/18 green. Committed as `9f89253`.
