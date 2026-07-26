# Remediation Backlog

Pass 11, 2026-07-02. Consolidates every finding from the anti-dark-code passes into ranked, bucketed work. Sources: coverage ledger, logging audit, adversarial pass, scenario stress-test, unknowns files, decisions-needed. Vocabulary: `.claude/skills/anti-dark-code/references/00-conventions.md`.

Buckets: **A** safe to do now (behavior-preserving / additive / docs), **B** approval-gated or behavior-changing, **C** needs more evidence first.

## Ranked items

### R1. BitReader out-of-bounds read on malformed input - DONE 2026-07-10, risk high

- **Where:** `CUETools.Codecs\BitReader.cs` (`fill`, `read_unary`, `read_rice_block`).
- **Why:** no bounds check vs the buffer length - `buffer_len_m` was stored but never read, so the check had been optimized out. A crafted/truncated FLAC frame (oversized blocksize, or an unbounded zero-run in the rice/unary path) drove `bptr_m` past the managed 128 KB frame buffer: an out-of-bounds read (CWE-125) reachable through the Flake `AudioDecoder`, a user-selectable decoder for untrusted `.flac`. DoS at minimum, potential info-disclosure into decoded output.
- **Exploitability (the C step, done):** the Flake decoder feeds a fixed 128 KB ring buffer and hands `DecodeFrame` the remaining length, which the reader ignored. Reachable at EOF (truncated last frame) or via a crafted long zero-run; the OOB was confirmed by fuzzing the managed decoder from a `MemoryStream` (harness in scratchpad, not committed).
- **Fix (the A step):** track `end_m = buffer + pos + len`; the speculative cache top-up (`fill`, and `read_rice_block`'s `have_bits<56` loop) reads zero past `end_m` (byte-identical - those bits are discarded), and the unbounded unary scans (`read_unary`, `read_rice_block`'s `bits==8` loop) throw at `end_m`. Commit `624879c` on branch `r1-bitreader-bounds`.
- **Verified:** decoded-PCM SHA-256 identical to the pre-fix decoder on a 16-bit (test.flac) and a 24-bit (hires1.flac) seed; encoders unaffected (they use `BitReader` only for static tables/log2i); mutation + truncation fuzzing of both seeds (tens of thousands of malformed inputs) yields clean exceptions, no crash or hang. TestCodecs is the standing byte-identity gate (run via the documented net47 recipe).
- **Follow-up (new):** the R1 finding said "FLAC/ALAC/lossyWAV"; in fact only the **Flake FLAC decoder** consumes the shared `BitReader` for decode. **ALAC decode uses its own inline bit reader** (`ALACDotNet.cs` `readbits`/`basterdised_rice_decompress`, borrowing only `BitReader`'s static table) and was NOT covered here - tracked as R15. lossyWAV has no managed decoder (encode-side preprocessor only).

### R2. MOTD image fetched over HTTP and rendered via GDI+ - bucket B, risk medium

- **Where:** `CUETools\frmCUETools.cs` MOTD path (S9, SC3).
- **Next step:** move to HTTPS; consider dropping the remote image or making it opt-in default-off. Approval-gated (GUI network behavior).
- **Verify:** MOTD still displays over HTTPS; feature flag honored.

### R3. AccurateRip -> HTTPS (decision D1) - bucket B, approved

- **Where:** `CUETools.AccurateRip\AccurateRip.cs:829,1230`.
- **Next step:** flip the two scheme literals; http fallback on TLS failure; verify a real lookup + DriveOffsets.bin.
- **Verify:** live lookup parses; offsets download.

### R4. CTDB HTTPS - file upstream (decision D2) - bucket B, approved

- **Next step:** open a tracking issue on LynxTWO/cuetools_2026 for the db.cuetools.net TLS request; revisit `CUEToolsDB.cs:74` when the server answers TLS. No client change now.

### R5. unrar.dll upgrade (decision D3) - SCOPED, DEFERRED 2026-07-02 (hygiene)

- Current: both DLLs 6.11.0. rarlab's old direct download URL 404s (now versioned). Hygiene-only (CVE vector not reachable, pass 07) and can't be functionally verified headless (no RAR encoder for a test archive), so not swapped blind. Full plan + verification steps in `decisions-needed.md` (D3). Pick up with the codec refresh (R13) when a test RAR is available.

### R6. SharpZipLib upgrade (decision D4) - DONE 2026-07-02

- SharpZipLib 1.4.2 via NuGet for net47/netstandard2.0 (net20 keeps vendored 0.85.5). Password path adapted for the modern API (up-front password for AES). Packaging scripts fixed. Verified with a net8 round-trip harness (6/6 checks incl. AES).

### R7. MusicBrainz client replacement (decision D6) - SCOPED; STOPPED FOR USER DECISION 2026-07-02

- **Finding:** no live MusicBrainz client exists to replace - the `MusicBrainz/` library is dead (unbuilt, unreferenced) and CUERipper's direct query is commented out; MB metadata comes via CTDB's proxy + Freedb fallback. Full scope + MB v2/CoverArtArchive reference + options in `docs/review/musicbrainz-replacement-scope.md`. Awaiting the user's choice: (A) rely on CTDB, delete dead lib [recommended]; (B) add a new direct MB v2 provider; (C) leave as-is.

### R8. CUEControls resgen under dotnet build (decision D7) - PARTIAL, 2026-07-02

- **Done:** repo-root `Directory.Build.targets` (Core-MSBuild-gated) makes all SDK-style net47 first-party projects build under `dotnet build`; zero impact on the shipping devenv/CI build.
- **Remaining (folded into R12):** SDK-style conversion of the old-style WinForms GUIs (`CUETools`, `CUERipper`, `CUEPlayer`, `CUETools.eac3ui`, old-style `CLParity`/`FLACCL`) so they too `dotnet build`. Needs GUI runtime verification; not a blind headless change.

### R9. ProxyPassword stored plaintext at rest (F1) - bucket B, risk medium

- **Where:** `CUEConfigAdvanced.ProxyPassword` + SettingsWriter.
- **Next step:** (C) confirm persistence format; then DPAPI / Credential Manager, or document the exposure. Approval-gated (credentials).

### R10a. TestProcessor fixtures not copied to output - bucket A, risk medium - DONE (2026-07-10)

- **Where:** `docs/unknowns/coverage-pass.md`; `CUETools/CUETools.TestProcessor/CUETools.TestProcessor.csproj`.
- **What was wrong (verified):** the CC0 fixtures already exist in source under
  `CUETools/CUETools.TestProcessor/Test Images/` - each is a CUE sheet plus a tiny
  `.dummy` stub holding a `MM:SS:FF` duration that `AudioReadWrite.GetAudioSource`
  turns into a silent `NULL.AudioDecoder` (no real audio, so nothing copyrighted).
  The SDK-style test project never copied that tree to the output directory, so
  `OpenCDExtra`, `OpenEnhancedCD`, and `OpenOneTrackCD` threw
  `DirectoryNotFoundException` looking for `bin\...\Amarok\Amarok.cue`.
- **Fix:** a `Content Include="Test Images\**\*.*"` item with
  `Link=%(RecursiveDir)%(Filename)%(Extension)` (strips the `Test Images\` prefix)
  and `CopyToOutputDirectory=PreserveNewest`, plus a matching `None Remove` to avoid
  the SDK duplicate-item conflict.
- **Result (measured):** TestProcessor 7 passed, 0 failed, 1 skipped
  (`CTDBResponseTest` is `[Ignore]` and needs an out-of-repo `Z:\ctdb.xml`).

### R10b. TestRipper synthetic multi-pass dumps - bucket A, risk medium - PENDING

- **What blocks it (verified):** `TestRipper/CDDriveReaderTest.MyTestInitialize` reads
  hardcoded machine-specific dumps `Y:\Temp\dbg\960\960-{00..63}.bin` and `.c2` (64
  passes) plus `960.bin`. Those files were never in the repo, are ~5.6 MB each
  (~360 MB total), and are drive-specific, so committing them is not an option. The
  single test (`CorrectSectorsTest`) then asserts the C2-weighted majority vote across
  the 64 passes recovers `_realData` exactly (`_realErrors == 0`).
- **Next step:** replace the file-replay init with in-memory synthesis - generate a
  deterministic `_realData`, model each pass as that data with a minority of
  C2-flagged byte errors, and feed `UserData`/`C2Count` directly (the original author
  already sketched this in the commented-out `Random` block). Keep it purely in memory
  so it runs in CI with no hardware and no committed dumps. Note: TestRipper is an
  old-style (non-SDK) project and is absent from the buildable set today; sequence it
  with R8.

### R11. CleanseString: reserved names + trailing dots (SC4) - bucket A, risk low - DONE (2026-07-10)

- **Fix (merged e5f2026):** `CUEConfig.CleanseString` now maps trailing dots/spaces to
  underscores (Windows silently trims them, which collides distinct names) and prefixes
  reserved DOS device names (CON, PRN, AUX, NUL, COM1-9, LPT1-9) with `_`. Added
  `CleanseStringTest` (3 tests, green) covering both hardenings and confirming ordinary
  names like `CONcert` and `COM10` are untouched.

### R12. Modernization program (net8 SDK-style, async, HttpClient, SIMD, installer, dead-code removal D5) - bucket B, large

- **Reference:** the 14-item modernization list delivered 2026-07-02. Sequence: foundation (already: git/submodules/tests/CI) -> framework migration -> async -> the rest. D5 (delete FlaCuda/dead DLLs) revisit with D6's outcome.

### R13. Codec version refresh + FlaCuda retirement + add missing codecs (user request 2026-07-02) - bucket B, large

- **Scope (user):** upgrade all codecs to their latest versions/builds, add any missing/wanted ones, and note that FlaCuda has effectively been superseded by FLACCL (OpenCL), which confirms the CUDA path is a dead ancestor rather than a parallel feature.
- **What this touches:**
 - ThirdParty submodules and their local patches: `flac` (libFLAC 1.5.0 pinned; check for newer), `WavPack` (5.8.1; check newer), MAC_SDK (10.86; APE), taglib-sharp, `libmp3lame` (3.100, already current), ffmpeg (built on demand by `build_ffmpeg_dlls.yml`). Each bump means re-checking that `ThirdParty/*.patch` still applies.
 - Managed wrappers in S6/S7 that must match new native ABIs (P/Invoke signatures, struct layouts).
 - FlaCuda (`CUETools.Codecs.FlaCuda`, `CUETools.FlaCudaExe`): DELETED 2026-07-23 (decision D5).
   Confirmed dead first: absent from the sln, referenced by no csproj/cs/sln outside its own two
   dirs, and superseded by FLACCL (OpenCL, live in the sln). 12 tracked files removed via git rm
   (recoverable from history). Still open under R13: verify FLACCL builds/runs on current OpenCL
   runtimes, and consider managed-SIMD modernization (idea 14).
 - Missing/desired codecs: enumerate against current CUETools upstream and user wants (e.g. Opus, newer ALAC, DSD?) - needs a requirements list from the user before implementation.
- **Why it needs care:** codec upgrades are behavior-affecting (bit-exactness must be preserved; the golden-corpus tests in idea 3 should exist first). Approval-gated where they touch release output.
- **Next step:** produce a per-codec version-vs-latest table (current pin -> latest stable) and a patch-reapply risk note; get the user's list of missing codecs to add; then upgrade one codec at a time with round-trip verification. Sequence after R8 (buildable) and ideally after golden-corpus tests (R10/idea 3).
- **Confidence:** the FlaCuda->FLACCL supersession is `inferred` from the orphaned-project evidence plus the user's note; the specific latest versions need a lookup pass.
- **Status 2026-07-02:** full audit DONE (`docs/review/codec-audit.md`) - every codec's format/engine/version/works status, plus the version table (`codec-refresh-scope.md`). Key results: no AAC *encoder* (decode-only via ffmpeg); test coverage only spans WAV/FLAC/ALAC (build golden-corpus suite to make all "works" verified); highest-value additions are Opus and AAC-encode. libFLAC/WavPack/LAME/ffmpeg current; MAC/taglib-sharp behind; FlaCuda dead (retire under D5). Additions still BLOCKED on the user's format list.

### R14. LAME v4 modernization initiative (user request 2026-07-02) - bucket B, large, separate project

- **Scope (user):** improve the LAME MP3 encoder enough to justify a version 4 release (stuck at 3.100 since 2017). Source at `C:\Users\usaft\Downloads\lame-3.100`. Separate from CUETools; start after the CUETools decisions are resolved. Permission granted to run any tests needed.
- **Next step:** build 3.100 as a baseline; assess plausible 20-year gains (AVX2 SIMD for FFT/psychoacoustics/quantization hot loops - Zen 3 is AVX2, not AVX-512; multithreaded frame encoding; build/CI modernization; VBR/reservoir quality tuning) and quantify with speed + quality benchmarks before proposing a v4 feature set. Bench on the 5950X (see hardware note).
- **Confidence:** unscoped; needs the baseline build + benchmark pass first.
- **Status 2026-07-02:** baseline BUILT + BENCHMARKED + proposed (`docs/review/lame-v4-proposal.md`). Measured: LAME 3.100 is single-threaded (uses 1/32 cores), SIMD stuck at SSE1, `-q0` only 17x realtime. v4 case: first multithreaded LAME (flagship), AVX2 SIMD, CMake/CI/tests, fuzz mpglib, loudness updates. Awaiting user's pick of where to start (recommended: frontend/batch multithreading).

### R15. ALAC decoder inline bit reader - unaudited OOB surface - bucket C, risk medium

- **Where:** `CUETools.Codecs.ALAC\ALACDotNet.cs` - its own `readbits(_framesBuffer, ref pos, bps)` and `basterdised_rice_decompress`, NOT the shared `BitReader` (it borrows only `BitReader`'s static unary table).
- **Why:** surfaced while fixing R1. ALAC is a user-selectable decoder for untrusted `.m4a`/ALAC input; its hand-rolled bit reading was never checked for the same past-the-buffer read that R1 fixed in the FLAC path.
- **Next step:** (C) trace `_framesBuffer`/`pos` bounds and the rice loops; then apply the same end-pointer bound if needed. Reuse the R1 fuzz harness pattern (decode from a `MemoryStream`, truncate/mutate a seed `.m4a`).
- **Verify:** decoded-PCM identical to pre-change on a valid ALAC seed; malformed inputs fail cleanly; ALAC round-trip test green.

### R16. net8 plugin discovery cannot load an external `plugins\` DLL - bucket C, risk medium - DONE 2026-07-22

- **Fixed:** `AddPlugin` now uses `Assembly.LoadFrom(plugin_path)`. Verified with the probe this
  item prescribed: a net8 console app referencing Processor but NOT Flake, with
  `CUETools.Codecs.Flake.dll` present ONLY under `plugins\`, registers
  `CUETools.Codecs.Flake.EncoderSettings` (scratchpad R16Probe, "R16 PASS"). The trust-boundary
  comment stays; `LoadFrom` returns the already-loaded assembly when the identity matches a
  project reference, so no duplicate types. Original entry below for the record.
- **Where:** `CUETools.Processor\CUEProcessorPlugins.cs` `AddPlugin` uses `Assembly.Load(AssemblyName.GetAssemblyName(plugin_path))`.
- **Why:** surfaced while wiring the WPF Convert page (R12). On .NET Framework, `Assembly.Load(name)` probed the app base and found the codec DLL; on .NET 8 the default load context does NOT probe the `plugins\` subdirectory, so `Assembly.Load(name)` for a DLL that lives only under `plugins\` throws `FileNotFoundException`, which `AddPlugin` swallows to `Trace`. The codec then silently does not register, and format defaults fall back to an external command-line encoder (e.g. `flac.exe`) that may be absent - a hard failure at encode time. Proven: a probe with the Flake DLL only in `plugins\` fell back to `flac.exe` and threw; adding Flake as a project reference (so it is in the app probing path) fixed it and wrote real FLAC. The WPF app works ONLY because it project-references `CUETools.Codecs.Flake`; a genuinely external/third-party codec dropped into `plugins\` would not load.
- **Next step:** (C) in `AddPlugin`, load from the explicit path - `Assembly.LoadFrom(plugin_path)` (or an `AssemblyLoadContext` rooted at the plugins dir) instead of `Assembly.Load(name)`; keep the CUETools.*.dll trust-boundary gate noted in the file. Re-check the encoder/decoder/ripper interface scan still finds the exported types.
- **Verify:** a codec DLL present ONLY under `plugins\` (no project reference, not in the app root) registers and is selectable; encode to that format writes output with no external exe on PATH. Extend the VerifyProbe pattern (Flake only in `plugins\`, no project ref) - it must now transcode instead of falling back to `flac.exe`.

### R17. Stopped rip leaves a truncated track file that poisons later album operations - bucket C, risk low - DONE 2026-07-22

- **Fixed:** the cleanup catch in `CUESheet.WriteAudioFilesPass` (which `audioDest.Delete()`s the
  in-progress file on any exception, including StopException) was Release-only (`#if !DEBUG`);
  it is now unconditional. Verified live: mp3 rip started, partial file present, stopped
  mid-track, output folder left with zero partial files. The existing truncated leftover was
  quarantined as `.partial`. Original entry below for the record.

- **Where:** WPF `RipService` StopException path; the engine's `CUESheet.Go()` leaves the
  in-progress track file partially written when stopped.
- **Evidence (2026-07-22):** a Stop-button test in an earlier session left
  a truncated track 04 flac. Later, converting ANY file from that album pulled in the sibling
  `album.cue` (whole-album semantics) and the decode died at the truncated file with
  `BitReader.read_rice_block: read past end of buffer` - a Release-build convert fails with a
  confusing "corrupt stream" error far from its cause. Diagnosed while wiring MP3: three probes
  proved the decoder, seek, and engine-source paths all clean; only the cue-driven whole-album
  path hit the bad file.
- **Next step options:** (a) on StopException during encode, delete the newest/incomplete
  destination file (the completed earlier tracks stay); (b) write to a temp name and rename on
  track completion so a stop never leaves a plausible-looking .flac; (c) at least log which file
  is incomplete. (b) is the clean fix.
- **Verify:** stop a rip mid-track; the output dir contains only complete tracks (or a clearly
  non-audio temp name); converting the album afterwards succeeds.

### R18. WMA lossy encoder for the WPF app - bucket D (enhancement) - DONE 2026-07-22

- **Fixed:** `CUETools.Codecs.WMA` gained net8.0-windows WITHOUT touching the WindowsMediaLib
  submodule (its four interop sources compile directly into the WMA assembly for that TFM).
  Also fixed a real encoder bug surfaced on the modern path: a stale `EncoderMode` name (long
  pre-PCM form vs the short PCM-filtered form) made `GetWriter` throw "codec/format not found";
  a stale/empty mode now falls back to the highest-quality format for the PCM. Verified: net8
  probe encode produced real "Microsoft Multichannel WMA Audio"; app-path album convert wrote
  11 wma tracks in 51s; format lists show lossy mp3,wma. The app treats "wma" as WMA Standard
  (lossy) only - one meaning per dropdown entry; WMA Lossless waits for a lossless/lossy
  encoder picker. Original entry below for the record.
- The lossy visualization already carries a distinct WMA profile (pure-MDCT labels, run-level
  pack). The encoder needs `CUETools.Codecs.WMA` (net47/net20 only) and the `WindowsMediaLib`
  SUBMODULE multi-targeted to netstandard2.0/net8.0-windows, plus a net8 COM-interop encode
  verification. The format stays hidden from the app's lists until a real in-process encoder
  registers (the lists only offer encoders that exist).

## 2026-07-26 audit remediation wave

The user approved autonomous implementation of all verified audit findings on 2026-07-26,
including the protected repair, concurrency, credential, and release-control areas. Approval
does not relax evidence, rollback, or verification requirements.

### R19. Test & Copy ignores the full-track CRC - bucket B, risk high

- **Area or slice:** modern WPF rip verification; `VerifyHistory.cs`,
  `TestAndCopyResolver.cs`, and their tests.
- **Why it matters:** AccurateRip CRCs exclude the first and last five seconds at the disc
  boundaries. Test & Copy can therefore report independent reads as bit-identical while the
  already-recorded full `Crc32` differs.
- **Evidence found:** `SameAudio` compares only ARv2/ARv1; the resolver uses it for every
  agreement decision; two independent source reviews confirmed the trigger.
- **Confidence:** verified.
- **Approval needed:** no.
- **Smallest safe next step:** separate cross-drive AR comparison from same-drive Test & Copy
  comparison, and require a nonzero equal full CRC for Test & Copy.
- **Verification plan:** unit cases for equal AR/different raw CRC, equal raw CRC, missing raw
  CRC, held verdict, and full-read selection; full WPF test suite.
- **Owner:** repo owner.
- **Status:** fixed 2026-07-26. The history and Test & Copy comparators are separate.
  Test & Copy requires matching nonzero full CRC32 and matching AR CRC. Focused tests passed
  25/25; the full WPF suite passed 127/127 in both Debug and Release.

### R20. Modern WPF CTDB Repair fails before writing - bucket B, risk high

- **Area or slice:** protected data repair; `VerifyService.Repair` and the Processor repair
  script.
- **Why it matters:** the fresh `CUESheet` never receives generated destination paths. The
  second `Go()` switches to Encode and dereferences a null `_destPaths`, so recoverable repairs
  fail instead of repairing.
- **Evidence found:** two independent end-to-end source traces identified the same null path
  before directory creation or audio writes.
- **Confidence:** verified.
- **Approval needed:** yes, approved by the user on 2026-07-26.
- **Smallest safe next step:** design a same-volume staged repair with explicit source mapping,
  post-repair verification, backup, and rollback. Do not merely initialize a source-equal
  destination.
- **Verification plan:** synthetic recoverable fixture or injected repair seam; tests for
  success, no recoverable entry, cancellation, write failure, verification failure, backup,
  and rollback. Live CTDB verification remains a separate external check.
- **Owner:** repo owner.
- **Status:** ready.

### R21. Modern WPF exposes known-dead safety controls - bucket A, risk high

- **Area or slice:** WPF Settings and Advanced pages plus `DeadSwitchTests`.
- **Why it matters:** "No unverified output", write-offset correction, and CTDB submission
  controls claim behavior the modern WPF path does not perform. The test is green because those
  controls are explicitly allowlisted as dead.
- **Evidence found:** runtime-consumer scan and the `KnownDead` set in `DeadSwitchTests`.
  CTDB Submit/Ask remain live in classic WinForms and must not be removed from shared config.
- **Confidence:** verified.
- **Approval needed:** no for removing misleading WPF controls; yes for adding new network or
  publication behavior, approved on 2026-07-26.
- **Smallest safe next step:** remove the misleading WPF rows and pass-through properties now,
  drain the allowlist, and reintroduce controls only with executable behavior tests.
- **Verification plan:** dead-switch analyzer, settings tests, full WPF suite, and a source
  search proving classic consumers remain.
- **Owner:** repo owner.
- **Status:** fixed and verified on 2026-07-26. The five WPF-only controls and their
  pass-throughs are gone, shared settings still round-trip, classic CTDB consumers remain, and
  the WPF suite passes 129/129 in both Debug and Release with zero skips.

### R22. Rip publication is not album-transactional or concurrency-safe - bucket B, risk high

- **Area or slice:** standard rip output, Test & Copy commit, `OutputGuard`.
- **Why it matters:** a disk-full or denied write can leave completed tracks and sidecars in the
  final folder. Test & Copy copies with overwrite enabled and no rollback. A check-then-use
  output probe permits two processes to choose the same destination.
- **Evidence found:** sequential final-directory writes, active-file-only cleanup, recursive
  overwrite copy, and no output lease.
- **Confidence:** verified for control flow; exact residue is filesystem-timing dependent.
- **Approval needed:** yes, approved by the user on 2026-07-26.
- **Smallest safe next step:** reserve a destination, stage on the destination volume, write a
  completion marker, publish by atomic rename where supported, and quarantine or remove
  incomplete staging on failure.
- **Verification plan:** injected disk-full, access denied, cancellation, copy failure, process
  interruption recovery, retry, and two concurrent publishers.
- **Owner:** repo owner.
- **Status:** ready.

### R23. CI and release gates do not prove expected work - bucket B, risk high

- **Area or slice:** GitHub workflows, legacy release collection, modern WPF publish.
- **Why it matters:** modern test projects are omitted, zero-test discovery can pass, release
  collection has no required-file manifest, and a successful classic release does not build a
  WPF artifact.
- **Evidence found:** workflow and pack-script trace plus local zero-test reproduction for
  legacy TestRipper.
- **Confidence:** verified for repo controls; hosted runner behavior remains external.
- **Approval needed:** yes, approved by the user on 2026-07-26.
- **Smallest safe next step:** add a versioned test-selection manifest and count assertions,
  run every buildable suite, publish WPF from a clean output, validate required files and plugin
  loads, and emit a source/toolchain/artifact build receipt.
- **Verification plan:** local workflow-equivalent scripts, structured test results, clean
  publish manifest check, and first hosted workflow run.
- **Owner:** repo owner.
- **Status:** ready.

### R24. External encoder process lifecycle can hang or accept bad output - bucket B, risk high

- **Area or slice:** `CUETools.Codecs.CommandLine.AudioEncoder`.
- **Why it matters:** `WaitForExit()` has no bound or cancellation. Exit zero is accepted without
  checking that the expected output is present, complete, or decodable.
- **Evidence found:** close/cleanup path and caller cancellation trace.
- **Confidence:** verified.
- **Approval needed:** no for bounded process cleanup; output-validation policy is
  behavior-changing and approved by the user on 2026-07-26.
- **Smallest safe next step:** add a configurable bounded wait, kill the process tree when
  supported, always clean temporary input, require a nonempty output, and add independent
  decode/sample-count verification for lossless external formats.
- **Verification plan:** fake encoder processes for success, nonzero exit, hang, early exit,
  missing output, truncated output, timeout, and cleanup.
- **Owner:** repo owner.
- **Status:** ready.

### R25. WMA Lossless has no post-encode verification - bucket B, risk high

- **Area or slice:** `CUETools.Codecs.WMA`.
- **Why it matters:** a successful Windows Media `EndWriting` is treated as proof of a good
  lossless output. No decoded sample count or sample-by-sample comparison exists.
- **Evidence found:** writer close path, sample counter, and absence of WMA tests.
- **Confidence:** verified.
- **Approval needed:** no.
- **Smallest safe next step:** use the shared post-encode verification mechanism from R24 with
  an independent WMA decoder, while keeping lossy WMA exempt from bit-equality.
- **Verification plan:** Windows-only lossless round trip, mismatch injection, truncated output,
  sample-count mismatch, and lossy regression.
- **Owner:** repo owner.
- **Status:** ready.

### R26. Settings expose credentials and deserialize unrestricted type metadata - bucket B, risk high

- **Area or slice:** WPF settings storage and `CUEConfig` advanced JSON.
- **Why it matters:** the proxy password is displayed and serialized in plaintext; settings are
  truncated in place; `TypeNameHandling.Auto` has no allowlist binder.
- **Evidence found:** WPF view, `SettingsWriter`, `CUEConfig.Save/Load`, and absence of a
  credential protector or serialization binder.
- **Confidence:** verified.
- **Approval needed:** yes, approved by the user on 2026-07-26.
- **Smallest safe next step:** protect the credential with Windows DPAPI and migrate legacy
  plaintext on successful load, write settings through a same-directory temporary file plus
  replace, and bind polymorphic type names to the known codec/settings types.
- **Verification plan:** legacy migration, encrypted round trip, wrong-user/decrypt failure,
  interrupted save, corrupt settings recovery, allowed polymorphic types, and rejected unknown
  `$type`.
- **Owner:** repo owner.
- **Status:** ready.

### R27. Plugin and imported executable trust is filename-based - bucket B, risk medium

- **Area or slice:** plugin discovery and `EncoderCatalog.Import`.
- **Why it matters:** every matching DLL in the local plugins folder is loaded and imported
  encoders are accepted by filename alone.
- **Evidence found:** explicit `Assembly.LoadFrom` discovery and overwrite copy into AppData.
- **Confidence:** verified.
- **Approval needed:** no for integrity metadata and warnings; a mandatory signing policy would
  change compatibility.
- **Smallest safe next step:** record SHA-256, size, version, and origin for imported executables;
  warn and require reapproval when they change; constrain plugin loading to a versioned manifest
  for the packaged WPF app while retaining an explicit local-plugin mode.
- **Verification plan:** known manifest, modified binary, missing plugin, wrong architecture,
  user-approved external plugin, and migration from current installs.
- **Owner:** repo owner.
- **Status:** ready.

### R28. Icecast sends credentials over cleartext HTTP - bucket B, risk high

- **Area or slice:** legacy/plugin Icecast output.
- **Why it matters:** Basic authentication and metadata travel over HTTP, exposing the source
  password to an on-path observer.
- **Evidence found:** hardcoded `http://` stream and metadata URLs.
- **Confidence:** verified.
- **Approval needed:** yes for network compatibility, approved by the user on 2026-07-26.
- **Smallest safe next step:** support and prefer HTTPS, refuse cleartext credentials by default,
  and require an explicit insecure-transport opt-in for legacy servers.
- **Verification plan:** HTTPS integration against a test server, certificate failure, explicit
  HTTP opt-in, authorization header, and metadata update.
- **Owner:** repo owner.
- **Status:** ready.

### R29. Fuzz gate reports parser success without checking parser correctness - bucket A, risk medium

- **Area or slice:** `CUETools.Fuzz` and WPF CI.
- **Why it matters:** every caught parser exception is counted as an acceptable rejection and the
  SCSI lane is reported successful unconditionally. The harness also does not cover the main
  file/archive/codec parsers.
- **Evidence found:** catch-all parser loop and unconditional report call.
- **Confidence:** verified.
- **Approval needed:** no.
- **Smallest safe next step:** define expected reject exception types and invariants, fail on
  unexpected exceptions or invalid accepted results, stop masking boundaries with extra slack,
  and add deterministic corpus lanes for CUE, ALAC, FLAC, archive, and tag parsing.
- **Verification plan:** seeded valid/invalid cases, injected invariant violation, bounded memory,
  deterministic replay, and CI summary assertions.
- **Owner:** repo owner.
- **Status:** ready.

### R30. Architecture, coverage, scenario, and logging records are stale - bucket A, risk medium

- **Area or slice:** `docs/architecture`, `docs/review`, and `docs/unknowns`.
- **Why it matters:** the documents omit the modern WPF runtime and tests and retain false claims
  such as "no async" and "CI never runs tests".
- **Evidence found:** 242 commits and 279 changed paths since the original map, including a new
  runtime, workflows, tests, plugin packaging, and deletion of old units.
- **Confidence:** verified.
- **Approval needed:** no.
- **Smallest safe next step:** refresh the maps from the 2026-07-25/26 audit evidence after code
  remediation stabilizes, preserving closed historical findings as dated history.
- **Verification plan:** entrypoint/project/workflow inventory diff and citation-existence check.
- **Owner:** repo owner.
- **Status:** ready.

### R31. Diagnostics lose stack context and nullable warnings lack a budget - bucket A, risk medium

- **Area or slice:** Processor/WMA exception paths and modern WPF build warnings.
- **Why it matters:** `throw ex` destroys the original stack; recurring nullable and unused-code
  warnings make new warnings difficult to distinguish.
- **Evidence found:** `CUESheet` and WMA CA2200 sites plus the Release build warning set.
- **Confidence:** verified.
- **Approval needed:** no.
- **Smallest safe next step:** replace rethrows with `throw`, fix or narrowly suppress proven
  annotation-only warnings, and add a checked warning baseline that fails on new warnings.
- **Verification plan:** build affected frameworks and assert exception stack preservation in a
  focused test where practical.
- **Owner:** repo owner.
- **Status:** ready.

### R32. Dependency and artifact provenance controls are incomplete - bucket B, risk medium

- **Area or slice:** NuGet/native dependencies and GitHub Actions.
- **Why it matters:** there are no lock files, central source policy, SBOM, signed manifest, or
  immutable action pins. Native and external executable provenance is not recorded.
- **Evidence found:** project/workflow inventory and current dependency scan.
- **Confidence:** verified for missing controls; native vulnerability status remains incomplete.
- **Approval needed:** yes for release controls, approved by the user on 2026-07-26.
- **Smallest safe next step:** pin actions to full SHAs, introduce reproducible dependency
  resolution where compatible, generate an SBOM and signed/hash manifest, and document every
  native artifact's source/build recipe.
- **Verification plan:** clean restore/build twice, compare manifests, dependency scan, and
  artifact hash verification.
- **Owner:** repo owner.
- **Status:** ready.

## Ordering

1. R19-R21: verification truth, repair design, and removal of false controls.
2. R22: transactional publication and concurrency.
3. R24-R26: encoder lifecycle, WMA verification, and protected settings.
4. R23, R29, R32: CI, fuzz, release, and provenance gates.
5. R27-R28: plugin/executable integrity and Icecast transport.
6. R30-R31: document refresh, warning budget, and diagnostic cleanup.
7. Continue the older open R2, R5, R8, R10b, R12, and R13 items after the current
   WPF release blockers are closed.

## Holes / external boundaries

- CTDB TLS (R4) is out-of-repo (server operator).
- CI depends on GitHub-hosted Windows image + VS Enterprise devenv path.
- Vendored binary provenance (idea 7) remains a supply-chain surface until moved to NuGet with SBOM.
- MusicBrainz/gnudb/AccurateRip/CTDB servers are external; their behavior can change independent of this repo.

## Changelog

- 2026-07-02 - backlog created from the first full anti-dark-code rollout (comment loop S1-S13, logging audit, adversarial, scenario passes) and the user's decisions D1-D7.
- 2026-07-26 - added R19-R32 from the modern WPF, codec, security, CI, release, and
  scenario-stress audit. User approved autonomous remediation, including protected areas.
