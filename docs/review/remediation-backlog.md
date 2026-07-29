# Remediation Backlog

Current-state refresh, 2026-07-28. This ledger began in pass 11 on 2026-07-02 and
consolidates the anti-dark-code coverage, logging, adversarial, scenario, and
unknowns passes. Closed entries remain here as dated evidence; status lines are the
current authority. The implementation and local-gate evidence for the 2026-07-26
wave is summarized in `2026-07-26-autonomous-audit.md`. Vocabulary:
`.claude/skills/anti-dark-code/references/00-conventions.md`.

Buckets: **A** safe to do now (behavior-preserving / additive / docs), **B** approval-gated or behavior-changing, **C** needs more evidence first.

## Ranked items

### R1. BitReader out-of-bounds read on malformed input - DONE 2026-07-10, risk high

- **Where:** `CUETools.Codecs\BitReader.cs` (`fill`, `read_unary`, `read_rice_block`).
- **Why:** no bounds check vs the buffer length - `buffer_len_m` was stored but never read, so the check had been optimized out. A crafted/truncated FLAC frame (oversized blocksize, or an unbounded zero-run in the rice/unary path) drove `bptr_m` past the managed 128 KB frame buffer: an out-of-bounds read (CWE-125) reachable through the Flake `AudioDecoder`, a user-selectable decoder for untrusted `.flac`. DoS at minimum, potential info-disclosure into decoded output.
- **Exploitability (the C step, done):** the Flake decoder feeds a fixed 128 KB ring buffer and hands `DecodeFrame` the remaining length, which the reader ignored. Reachable at EOF (truncated last frame) or via a crafted long zero-run; the OOB was confirmed by fuzzing the managed decoder from a `MemoryStream` (harness in scratchpad, not committed).
- **Fix (the A step):** track `end_m = buffer + pos + len`; speculative cache top-up reads zero without dereferencing past `end_m`. A later real-disc run exposed that bounding unary scans by the speculative pointer falsely rejected a legal Rice terminator already held in the cache. The final fix also tracks logical `remaining_bits_m`: fixed-width, unary, and Rice reads reject only after genuine input exhaustion, independent of cache lookahead.
- **Verified:** exact-buffer tests accept a cached terminator and reject the same stream when the terminator is absent; fixed-width over-read rejects; the malformed corpus remains bounded; 24 real FLAC tracks, including both former false rejections, decode to their declared sample counts; FFmpeg independently accepts all 24. Managed Flake modes and the FLACCL RTX 3060 modes 0-8/24-bit/exact-boundary matrix pass without lookahead padding.
- **Follow-up (new):** the R1 finding said "FLAC/ALAC/lossyWAV"; in fact only the **Flake FLAC decoder** consumes the shared `BitReader` for decode. **ALAC decode uses its own inline bit reader** (`ALACDotNet.cs` `readbits`/`basterdised_rice_decompress`, borrowing only `BitReader`'s static table) and was NOT covered here - tracked as R15. lossyWAV has no managed decoder (encode-side preprocessor only).

### R2. MOTD image fetched over HTTP and rendered via GDI+ - DONE 2026-07-26, risk medium

- **Where:** `CUETools\frmCUETools.cs` MOTD path (S9, SC3).
- **Fix:** the updater now fetches only bounded, strict UTF-8 text from
  `https://cue.tools/motd/motd.txt`, applies finite request/read timeouts, never
  downloads or decodes a remote image, never falls back to HTTP, and deletes the
  legacy unauthenticated text/image cache.
- **Verified:** source-invariant tests cover the transport and parser contract. A
  live Windows TLS request to `cue.tools` returned HTTP 200 on 2026-07-26. The
  superficially similar `cuetools.net` host was rejected by Schannel for a
  certificate-name mismatch and is deliberately not used.

### R3. AccurateRip -> HTTPS (decision D1) - DONE

- **Where:** `CUETools.AccurateRip\AccurateRip.cs:837,1248`.
- **Fix:** both the disc lookup and `DriveOffsets.bin` use
  `https://www.accuraterip.com`; there is no HTTP downgrade.
- **Residual:** live service behavior remains external and should stay in a release
  smoke test.

### R4. CTDB HTTPS - upstream request filed (decision D2) - external boundary

- **Done:** filed LynxTWO/cuetools_2026 issue #1 requesting TLS for
  `db.cuetools.net`. Revisit `CUEToolsDB.cs` when the server answers HTTPS; no
  insecure client-side inference or forced switch is appropriate before then.

### R5. unrar.dll upgrade (decision D3) - DONE 2026-07-26

- Replaced both 6.11 DLLs with official RARLAB UnRAR 7.23.0 from the versioned
  signed SFX. The SFX and both architecture DLLs have valid win.rar GmbH
  signatures; exact bytes and SHA-256 values are pinned in the native provenance
  inventory. The wrapper's six required exports are present. Production
  `RarCompressionProvider`/`RarStream` passed RARLAB's real `test.rar` on x64 and
  x86: 14 entries, exact full payload, and independent backward-seek SHA-256.
  A committed 280-byte RAR5 fixture then exposed and locked down a backward-seek
  race where stale EOF could end replay before rewind was acknowledged.
  `RarStream.Read` now waits while rewind is pending; the focused TestCodecs case
  and 20/20 repeated no-build full-read/seek runs pass. The old 6.11 import
  evidence is retained as history.

### R6. SharpZipLib upgrade (decision D4) - DONE 2026-07-02

- SharpZipLib 1.4.2 via NuGet for net47/netstandard2.0 (net20 keeps vendored 0.85.5). Password path adapted for the modern API (up-front password for AES). Packaging scripts fixed. Verified with a net8 round-trip harness (6/6 checks incl. AES).

### R7. MusicBrainz client replacement (decision D6) - DONE 2026-07-02

- **Finding:** no live MusicBrainz client existed to replace - the `MusicBrainz/` library was dead (unbuilt, unreferenced) and CUERipper's direct query was commented out; MB metadata comes via CTDB's proxy + Freedb fallback. The user chose option A: retain the CTDB-proxied path and remove the dead mirror/binary and stale references. Full scope and the optional future direct-provider design remain in `docs/review/musicbrainz-replacement-scope.md`.

### R8. CUEControls resgen under dotnet build (decision D7) - PARTIAL, 2026-07-02

- **Done:** repo-root `Directory.Build.targets` (Core-MSBuild-gated) makes all SDK-style net47 first-party projects build under `dotnet build`; zero impact on the shipping devenv/CI build.
- **Remaining (folded into R12):** SDK-style conversion of the old-style WinForms GUIs (`CUETools`, `CUERipper`, `CUEPlayer`, `CUETools.eac3ui`, old-style `CLParity`/`FLACCL`) so they too `dotnet build`. Needs GUI runtime verification; not a blind headless change.

### R9. ProxyPassword stored plaintext at rest (F1) - DONE 2026-07-26, risk medium

- **Where:** `CUEConfigAdvanced.ProxyPassword`, `SettingsWriter`, classic
  `CUEConfig`, and WPF `SettingsStore`.
- **Fix:** current-user DPAPI protection, legacy-plaintext migration, atomic
  same-directory settings publication, a password-only UI, and an allowlisted
  polymorphic JSON binder. A nonempty secret fails closed on unsupported platforms;
  classic UI save boundaries dispose the writer, report that no settings were
  saved, and do not silently drop the credential.
- **Verified:** encrypted round trip, migration, corruption, unsupported-platform,
  atomic-write, and rejected-type tests are part of the WPF suite.

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

### R10b. TestRipper synthetic multi-pass dumps - bucket A, risk medium - DONE (2026-07-26)

- **Original blocker (verified):** `TestRipper/CDDriveReaderTest.MyTestInitialize`
  read hardcoded machine-specific dumps under `Y:\Temp\dbg\960` (64 passes, roughly
  360 MB total). Those private, drive-specific files were never valid checkout/CI
  fixtures.
- **Additional finding:** the old test carried a stale copy of the algorithm: it
  ignored the C2 vote plane and indexed C2 differently from shipping `SCSIDrive`, so
  merely replacing its files would not have tested production behavior.
- **Implemented 2026-07-26:** the shipping vote is extracted as
  `SecureSectorVote.CorrectSector`; `SCSIDrive` and the SDK net47 test call that same
  helper. The test creates deterministic in-memory truth data for 64 passes over 32
  sectors, injects minority byte corruption with both C2-flagged and unflagged cases,
  verifies exact recovery, and separately verifies that one clean pass does not satisfy
  a two-pass confidence policy. A third case verifies C2-plane reconstruction without
  granting false secure assurance. It is enrolled in the canonical suite manifest
  with minimum discovery 3 and zero skips.
- **Result:** the canonical run passed all 3 tests with 0 failures and 0 skips.

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
 - ThirdParty submodules and their local patches: `flac` (libFLAC 1.5.0, current upstream), `WavPack` (5.8.1 vs upstream 5.9.0), MAC_SDK (13.20, current upstream, with an adapted CUETools `IAPEIO` wrapper), taglib-sharp (the current 2.3.0.0 release with local changes), `libmp3lame` (3.100 vs the July 2026 upstream 4.0 release), and ffmpeg (standalone/unshipped 7.1.1 path vs upstream 8.1.2). Each bump means re-checking local patches, ABI, packaging, and audio behavior.
 - Managed wrappers in S6/S7 that must match new native ABIs (P/Invoke signatures, struct layouts).
 - FlaCuda (`CUETools.Codecs.FlaCuda`, `CUETools.FlaCudaExe`): DELETED 2026-07-23 (decision D5).
   Confirmed dead first: absent from the sln, referenced by no csproj/cs/sln outside its own two
   dirs, and superseded by FLACCL (OpenCL, live in the sln). 12 tracked files removed via git rm
   (recoverable from history). FLACCL's corrected exact-length verifier now builds and
   runs on an RTX 3060 across OpenCL modes 0-8, CPU workers, 24-bit input, and an exact
   frame boundary. Cross-vendor coverage and managed-SIMD modernization (idea 14)
   remain future work.
 - Missing/desired codecs: enumerate against current CUETools upstream and user wants (e.g. Opus, newer ALAC, DSD?) - needs a requirements list from the user before implementation.
- **TTA build evidence:** redirected x64 and Win32 C++/CLI builds pass. Runtime
  workers encode 16-bit stereo and 24-bit six-channel PCM, reproduce every PCM byte
  through both the managed decoder and ffmpeg, and produce identical x64/Win32
  bitstreams. The wrapper independently verifies finalized output before publication.
  The tests also found and fixed `ttalib`'s short-file final-frame length bug.
- **Why it needs care:** codec upgrades are behavior-affecting (bit-exactness must be preserved; the golden-corpus tests in idea 3 should exist first). Approval-gated where they touch release output.
- **Next step:** preserve and reconcile existing submodule work, then upgrade one
  codec at a time with native rebuild, ABI/package probes, and round-trip/corpus
  verification. The format-addition half still needs a product wishlist.
- **Confidence:** verified
- **Status 2026-07-26:** reachability and verification claims are refreshed in
  `docs/review/codec-audit.md`, and the upstream version table is refreshed in
  `codec-refresh-scope.md`. libFLAC and taglib-sharp match current releases.
  WavPack and LAME have known version drift; the unshipped FFmpeg
  path is also behind. Upgrades remain per-codec integration work, not binary
  swaps. Monkey's Audio 13.20 is upgraded and verified on Win32 and x64. FlaCuda
  is deleted. FLACCL has real RTX 3060/OpenCL verification evidence.
  Format additions still require a product decision.

### R14. LAME v4 modernization initiative (user request 2026-07-02) - bucket B, large, separate project

- **Scope (user):** improve the LAME MP3 encoder enough to justify a version 4 release (stuck at 3.100 since 2017). Source at `C:\Users\usaft\Downloads\lame-3.100`. Separate from CUETools; start after the CUETools decisions are resolved. Permission granted to run any tests needed.
- **Next step:** build 3.100 as a baseline; assess plausible 20-year gains (AVX2 SIMD for FFT/psychoacoustics/quantization hot loops - Zen 3 is AVX2, not AVX-512; multithreaded frame encoding; build/CI modernization; VBR/reservoir quality tuning) and quantify with speed + quality benchmarks before proposing a v4 feature set. Bench on the 5950X (see hardware note).
- **Confidence:** unknown
- **Status 2026-07-26:** the 3.100 baseline, quality harness, and experimental
  fixes remain useful, but upstream released official LAME 4.0 in July 2026.
  The local project must not claim the same version line. Next work is an
  authoritative official-4.0 diff/rebase, rerun of the bitstream/decode/quality
  corpus, and a distinct downstream naming decision before CUETools integration.

### R15. ALAC decoder inline bit reader - DONE 2026-07-10, risk medium

- **Where:** `CUETools.Codecs.ALAC\ALACDotNet.cs` - its own `readbits(_framesBuffer, ref pos, bps)` and `basterdised_rice_decompress`, NOT the shared `BitReader` (it borrows only `BitReader`'s static unary table).
- **Why:** surfaced while fixing R1. ALAC is a user-selectable decoder for untrusted `.m4a`/ALAC input; its hand-rolled bit reading was never checked for the same past-the-buffer read that R1 fixed in the FLAC path.
- **Fix:** the inline reader and Rice paths now enforce the real frame boundary
  instead of relying on array capacity or trailing slack.
- **Verified:** valid-stream identity/round-trip coverage and deterministic
  malformed/truncated ALAC corpus checks are in the current codec/fuzz gates.

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
  Test & Copy requires matching nonzero full CRC32 and matching AR CRC. Focused
  comparison tests passed; aggregate totals are refreshed by the final canonical gate.
  A live H: run then passed two independent full reads (413s/410s), published 11
  verified FLAC files with matching AR 107/424 and CTDB 114/544, recorded zero
  reread/failed windows, and round-tripped decoded-and-compared output assurance through
  `rip.verify`. That proves the mechanism; a final-source no-build H: rerun is pending
  because the behavior-preserving `SecureSectorVote` extraction landed afterward.

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
- **Status:** fixed and verified 2026-07-26. `VerifyService` now drives the
  repair script with generated destinations inside an owned same-volume staging
  directory, requires the CTDB repair branch to have applied, independently decodes
  and verifies every nonempty staged output, rejects escaping/reparse paths, and
  atomically publishes a unique `- repaired` sibling without modifying the source.
  Success, no-fix, write/verify failure, collision, traversal, reparse, and cleanup
  cases are covered by `VerifyRepairTransactionTests`. A live opt-in run also repaired
  a deliberately damaged known image, independently post-verified the published
  sibling, and confirmed that source hashes were unchanged. Completed WPF lossless
  rips now gather CTDB status immediately and offer the same repair transaction when
  recoverable errors remain. Track sets resolve through their album cue, image rips
  use their sole audio file, and ambiguous or escaping paths fail closed. Fifteen
  focused transaction and post-rip routing tests pass. A live K: damaged-disc rip
  subsequently proved the completed-rip route: 24 final-output-proven FLACs were
  published, the Rip page offered six-sector CTDB repair, and the separately verified
  sibling was published without changing the source aggregate hash. FFmpeg independently
  decoded every repaired FLAC.

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
  the canonical WPF gate covers the resulting settings/runtime surface.

### R22. Rip publication is not album-transactional or concurrency-safe - bucket B, risk high

- **Area or slice:** standard rip output, Test & Copy commit, `OutputGuard`.
- **Why it matters:** a disk-full or denied write can leave completed tracks and sidecars in the
  final folder. Test & Copy copies with overwrite enabled and no rollback. A check-then-use
  output probe permits two processes to choose the same destination.
- **Evidence found:** sequential final-directory writes, active-file-only cleanup, recursive
  overwrite copy, and no output lease.
- **Confidence:** verified
- **Approval needed:** yes, approved by the user on 2026-07-26.
- **Smallest safe next step:** reserve a destination, stage on the destination volume, write a
  completion marker, publish by atomic rename where supported, and quarantine or remove
  incomplete staging on failure.
- **Verification plan:** injected disk-full, access denied, cancellation, copy failure, process
  interruption recovery, retry, and two concurrent publishers.
- **Owner:** repo owner.
- **Status:** fixed and verified 2026-07-26 for the modern WPF product.
  `AlbumOutputTransaction` acquires a cross-process reservation, creates an owned
  same-volume sibling stage, checks containment and reparse points, and publishes the
  populated album with one directory rename. Rip, convert, Test & Copy, cancellation,
  collision, ownership-loss, injected failure, and orphan handling are covered.
  Classic multi-track output remains atomic per file, not album-wide; that narrower
  residual is not a WPF release blocker.

### R23. CI and release gates do not prove expected work - bucket B, risk high

- **Area or slice:** GitHub workflows, legacy release collection, modern WPF publish.
- **Why it matters:** modern test projects are omitted, zero-test discovery can pass, release
  collection has no required-file manifest, and a successful classic release does not build a
  WPF artifact.
- **Evidence found:** workflow and pack-script trace plus local zero-test reproduction for
  legacy TestRipper.
- **Confidence:** verified
- **Approval needed:** yes, approved by the user on 2026-07-26.
- **Smallest safe next step:** add a versioned test-selection manifest and count assertions,
  run every buildable suite, publish WPF from a clean output, validate required files and plugin
  loads, and emit a source/toolchain/artifact build receipt.
- **Verification plan:** local workflow-equivalent scripts, structured test results, clean
  publish manifest check, and first hosted workflow run.
- **Owner:** repo owner.
- **Status:** implemented and locally verified 2026-07-26. A versioned suite manifest
  asserts discovery/skip floors; aggregate counts are refreshed by the final canonical
  gate and declared skips remain visible. Warning and native warning baselines reject
  new fingerprints. Clean WPF publication validates 36
  required files, 14 trust-manifest/runtime entries, 19 registrations, and 5 native
  probes, then emits receipts, hashes, native inventory, and SBOMs. The first hosted
  workflow run remains required. The native x64 dependency gate passes. A classic
  AnyCPU pass found and fixed a declared-net20
  blocker by replacing the slip-correlation tuple API with a named result/out-parameter
  contract. The next pass found net35-only `SortedSet<T>` in the adaptive-speed ladder;
  its ordered `List<T>` replacement preserves semantics because rungs are strictly
  ascending and the 97% cutoff keeps them below the appended real maximum. The
  redirected-output classic AnyCPU solution build then completed successfully. The
  current exact matrix passes at Any CPU 58/0/11 and x64 and Win32 9/0/60.
  TTA compiled and linked for both
  into valid CLR PE files. Installer Projects 3.0.0, using
  `DisableOutOfProcBuild`, passed 8 projects with 0 failures and produced a
  929,792-byte MSI. The local route used the Visual Studio 18 resolver with the
  VS2022 v143 toolset. The local frozen receipt and exact collection now pass;
  parity on the pinned hosted VS2022 image remains.

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
- **Status:** fixed and verified 2026-07-26. External encoders now have a monotonic
  deadline, bounded cleanup, process-tree termination where supported, absolute
  executable resolution, create-only/replace-aware publication, and deterministic
  failure cleanup. Lossless command encoders require an explicit independent decoder
  contract and compare decoded PCM digest plus sample count before publication.
  Fake-process tests cover hang, timeout, early/nonzero exit, missing/truncated output,
  cleanup failures, stale callbacks, and competing publishers.

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
- **Status:** fixed and verified 2026-07-26. WMA Lossless writes to a staged file,
  closes the Windows Media writer, independently decodes the completed stream, and
  compares every sample and the total count before publication. Deterministic
  mismatch/truncation/count tests pass. A real net8 Windows Media Lossless encode,
  finalize, independent decode, and PCM verification also passed on this host; retain
  that integration as non-skippable release-machine evidence.

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
- **Status:** fixed and verified 2026-07-26; see R9. Both classic and WPF settings
  use current-user DPAPI and atomic publication. Legacy plaintext migrates only after
  successful protection/write, unsupported protection fails the whole save, and
  `KnownSettingsSerializationBinder` rejects unregistered `$type` values. The WPF
  settings UI edits a detached draft; Cancel discards it instead of mutating persisted
  configuration before Save.

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
- **Status:** fixed for packaged WPF plugins, explicit per-user plugins, and imported
  executables, verified 2026-07-26. The packaged plugin set is bound to a versioned
  manifest containing normalized relative path, size, SHA-256, managed
  identity/architecture, and deterministic order. Loading rejects missing, modified,
  wrong-path, wrong-identity, or preloaded lookalike assemblies and rehashes the actual
  loaded location. Encoder, decoder, and ripper discovery now uses exact
  `IsAssignableFrom` contract identity rather than interface short-name matching
  followed by a nullable `as` cast; a regression invariant rejects incompatible
  lookalikes. Compression types carrying the plugin attribute must also implement the
  real `ICompressionProvider` contract. HDCD types must implement `IAudioDest`,
  `IAudioFilter`, and `IFormattable`, carry the `HDCDDotNet` name, and expose the
  public `(int,int,int,bool)` constructor. Valid and impostor cases are tested. The
  release includes a strict DLL-only user enrollment script that
  publishes a separate exact-hash manifest under `%AppData%\CUETools2026\plugins`;
  replacement is opt-in and preserves the prior set as a timestamped backup. Imported
  executables retain origin/integrity metadata and require reapproval after change.
  These are integrity allowlists, not a code-signing policy.
- **Migration boundary:** legacy loose DLLs are not automatically approved or moved.
  Releases must be extracted to a clean directory; an extra legacy DLL beside the
  packaged manifest remains a visible integrity failure. The user guide describes how
  to prepare and enroll a separate set while retaining the old installation for
  rollback.

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
- **Status:** fixed at the policy and credential-storage boundary 2026-07-26.
  HTTPS is the default for source and metadata requests; cleartext is rejected unless
  the user explicitly enables insecure transport. Passwords use current-user DPAPI,
  legacy plaintext migrates proactively, and rejected connections are disposed. A
  disposable Icecast 2.5.0 instance passed authentication rejection, source streaming,
  exact metadata, listener bytes, flush/close, and teardown. A real HTTPS/certificate
  endpoint and supported Mono behavior remain unobserved external/runtime checks.
  Configured MP3 bitrate and joint-stereo values are now propagated into the LAME
  writer rather than silently using its defaults. Unsupported bitrates are rejected
  before any network connection or credential transmission. If persistence fails,
  CUEPlayer restores both the active settings object and the prior in-memory DPAPI
  blob, so a "not saved" result cannot silently alter the live stream configuration.

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
- **Status:** fixed and verified 2026-07-26. The deterministic corpus harness now
  distinguishes expected rejection from unexpected exception, applies accepted-value
  invariants, and covers CUE, managed FLAC, ALAC, archive/tag paths, plus safe SCSI
  parsing. A 20,000-iteration run with seed 20260712 passed all seven executable
  checks; one truncated SCSI case is an explicit unsafe/native boundary skip rather
  than a false pass.

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
- **Status:** fixed 2026-07-26. Architecture, coverage, slice, logging, unknowns,
  codec, capability, scenario, and remediation records now carry a current-state
  layer while preserving dated historical evidence. A citation/path check is part of
  the closeout gate.

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
- **Status:** fixed for the audited modern/codec paths and locally gated
  2026-07-26. Same-thread rethrows use `throw`; cross-thread modern paths use
  `ExceptionDispatchInfo`; .NET 2.0 deliberately preserves the original exception
  type/identity with `throw exception`, accepting a reset visible throw site. A
  runtime net20 compatibility probe enforces that choice. The Release warning gate
  observed 37 baseline fingerprints and no new fingerprint (378 emitted lines).
  Older constructed-exception throws in legacy Freedb/SCSI code remain style debt,
  not caught-exception rethrows covered by this item.

### R32. Dependency and artifact provenance controls are incomplete - bucket B, risk medium

- **Area or slice:** NuGet/native dependencies and GitHub Actions.
- **Why it matters:** there are no lock files, central source policy, SBOM, signed manifest, or
  immutable action pins. Native and external executable provenance is not recorded.
- **Evidence found:** project/workflow inventory and current dependency scan.
- **Confidence:** verified
- **Approval needed:** yes for release controls, approved by the user on 2026-07-26.
- **Smallest safe next step:** pin actions to full SHAs, introduce reproducible dependency
  resolution where compatible, generate an SBOM and signed/hash manifest, and document every
  native artifact's source/build recipe.
- **Verification plan:** clean restore/build twice, compare manifests, dependency scan, and
  artifact hash verification.
- **Owner:** repo owner.
- **Status:** partially closed 2026-07-26. All four actions workflows use immutable
  action SHAs; release inputs have native source/build/hash inventory; clean WPF
  artifacts receive a SHA-256 manifest, build receipt, contract snapshot, CycloneDX
  SBOM, and SPDX file inventory. The Microsoft SPDX result must be read as a file
  inventory rather than a complete dependency graph. Provenance now records
  byte-identical RareWares LAME archives, official signed/runtime-tested RARLAB
  UnRAR 7.23 plus the import-era 6.11 evidence, and the official TTA
  archive/import delta. Remaining: signing, NuGet lock-file rollout, frozen
  classic-artifact receipts and hosted parity, HDCD's exact
  source/revision/recipe, RareWares' exact LAME build flags/revision, the historical
  TTA archive checksum, and reconciliation of the dirty detached submodules.

### R33. Custom WPF job paths can leak through exception diagnostics - bucket A, risk medium

- **Area or slice:** `DiagnosticLog`, WPF rip/Test & Copy, verify, and repair
  service boundaries.
- **Why it matters:** full local exception chains are useful for debugging, but a
  custom source or output outside the default profile/music roots could appear in
  a log a user later shares.
- **Evidence found:** the logger scrubbed only registered substrings; verify input
  and custom output/staging roots were not consistently registered before work.
- **Confidence:** verified.
- **Approval needed:** no.
- **Smallest safe next step:** register raw and normalized paths at each job
  boundary and test direct, nested, stack, casing, and overlapping-value behavior
  through a real isolated log file.
- **Verification plan:** focused diagnostic test and full WPF suite.
- **Owner:** repo owner.
- **Status:** fixed and verified 2026-07-26. Job and staging boundaries register
  their paths before diagnostic-producing work. Redaction is case-insensitive,
  chooses the longest overlapping value, and scans original text only. The focused
  real-file test passed, and the canonical WPF gate covers the integration.

### R34. Managed encoder approval was not bound to process launch - bucket A, risk medium

- **Area or slice:** WPF external-encoder catalog and shared command-line encoder.
- **Why it matters:** a managed executable could pass its import receipt check, change before an
  immediate or deferred `%I` launch, and still execute under the earlier approval claim.
- **Evidence found:** `EncoderCatalog.ResolveExe` checked size/hash, but cloned command settings
  carried no expected identity and `AudioEncoder` checked only path existence.
- **Confidence:** verified.
- **Approval needed:** no.
- **Smallest safe next step:** carry a runtime-only size/hash into cloned settings, rehash through
  a read-only handle immediately before launch, and retain the deny-write/delete lease through
  encoder and self-verifier completion.
- **Verification plan:** immediate and deferred mutation tests, JSON self-approval refusal, and a
  real ffmpeg/ALAC self-verification run under the retained lease.
- **Owner:** repo owner.
- **Status:** fixed and verified 2026-07-26. Both mutation windows fail before process start,
  receipt fields are excluded from JSON, and the real ffmpeg integration passes without copying
  or renaming the executable.

### R35. Native plugin hashes were checked before, not at, deferred load - bucket A, risk medium

- **Area or slice:** packaged plugin manifest, plugin bootstrap, HDCD/libFLAC/WavPack/MAC loaders.
- **Why it matters:** native bytes could change after startup enumeration, and an absolute-load
  failure fell back to an unmanifested bare DLL name from process search.
- **Evidence found:** the manifest enumerator hashed native files, but only managed assemblies were
  loaded then; all four wrappers had a bare-name `LoadLibrary` fallback.
- **Confidence:** verified.
- **Approval needed:** no.
- **Smallest safe next step:** pair each packaged wrapper with its architecture-specific manifest
  entry, rehash under deny-write/delete sharing, load by restricted full path, verify the returned
  module path, retain the handle, and remove bare-name fallback.
- **Verification plan:** pairing/missing-dependency, post-enumeration tamper, preloaded-lookalike,
  native integration, and clean artifact probes.
- **Owner:** repo owner.
- **Status:** fixed and verified 2026-07-26, then extended when the same deferred-load gap
  was found in packaged LAME. The focused manifest tests passed 13/13, native codec
  integration passed 16/16, and the production-shaped artifact passed 14 trust entries,
  19 registrations, and 5 native probes.

### R36. Native lossless writers lacked terminal lifecycle state - bucket A, risk low

- **Area or slice:** libFLAC, libwavpack, and MACLib encoders.
- **Why it matters:** a second close re-entered non-retryable finalization after publication, while
  write-after-close could recreate an unverified work file or dereference disposed verification
  state.
- **Evidence found:** unlike ALAC, WMA, and command-line encoders, these three wrappers had no
  terminal closed flag.
- **Confidence:** verified.
- **Approval needed:** no.
- **Smallest safe next step:** set terminal state before cleanup, make repeated close a no-op, and
  reject writes after close.
- **Verification plan:** extend the real 16/24-bit FLAC, WavPack, and Monkey's Audio round trips
  with close-twice and write-after-close assertions.
- **Owner:** repo owner.
- **Status:** fixed and verified 2026-07-26. All lifecycle assertions and the full 241-test WPF
  suite pass.

### R37. Packaged LAME native library bypassed the runtime manifest - bucket A, risk medium

- **Area or slice:** WPF libmp3lame packaging, plugin trust bootstrap, and native wrapper.
- **Why it matters:** the managed LAME plugin was manifest-bound, but its native DLL was
  copied to the app root and its wrapper retained a bare-name fallback. Those bytes could
  execute without the load-time native hash/path guarantee applied to the other packaged
  codecs.
- **Evidence found:** WPF copy target, native dependency map, and
  `libmp3lamedll` static loader.
- **Confidence:** verified.
- **Approval needed:** no.
- **Smallest safe next step:** package LAME under the architecture plugin directory,
  pair it with the managed wrapper, rehash/preload it by approved full path, remove the
  bare fallback, and add a real initialization/encode probe.
- **Verification plan:** mapping/tamper/lookalike/fallback tests, real LAME version and
  encode integration, legacy framework builds, and clean artifact validation.
- **Owner:** repo owner.
- **Status:** fixed and verified 2026-07-26. LAME is present only at
  `plugins/x64/libmp3lame.dll`, is one of 14 runtime trust entries, and is the fifth
  native artifact probe. Focused trust tests passed 13/13, native codec integration
  passed 16/16, net20/net47 builds passed, and the clean artifact rejected a root
  fallback by contract.

### R38. Classic RAR could load an unapproved native UnRAR library - bucket A, risk medium

- **Area or slice:** classic compression plugin discovery, `PluginTrustManifest`, and
  the RAR native wrapper.
- **Why it matters:** hash-binding the managed RAR plugin alone did not constrain the
  `unrar.dll` bytes selected later by native resolution.
- **Evidence found:** `CUETools.Compression.Rar.dll` was reachable in the classic plugin
  set, while its native dependency was absent from the managed-to-native trust map.
- **Confidence:** verified.
- **Approval needed:** no.
- **Smallest safe next step:** map the managed RAR plugin to the architecture-specific
  UnRAR DLL, preload and retain only the approved module, and keep the wrapper free of
  alternate native-name fallbacks.
- **Verification plan:** dependency mapping and missing-native tests, focused manifest
  trust selection, and net20/net47 RAR/Processor builds.
- **Owner:** repo owner.
- **Status:** fixed and verified 2026-07-26. The classic trust bootstrap now binds the
  RAR wrapper to `unrar.dll`; focused trust tests passed 13/13 and the affected legacy
  projects built for their exercised target frameworks.

### R39. Release cleanup and provenance could cross filesystem trust boundaries - bucket A, risk high

- **Area or slice:** WPF artifact cleanup, artifact contract validation, and provenance
  evidence generation.
- **Why it matters:** recursive cleanup beneath a junction, an escaping artifact name,
  an evidence path inside the artifact, or a pre-existing receipt link could delete or
  overwrite data outside the intended release roots. Generated native intermediates
  also obscured which untracked files were real source inputs.
- **Evidence found:** adversarial PowerShell harnesses for descendant reparse points,
  equal/contained output paths, artifact-name traversal, receipt-leaf links, and
  untracked submodule outputs.
- **Confidence:** verified.
- **Approval needed:** no.
- **Smallest safe next step:** reject reparse points throughout a cleanup tree before
  recursion; require external evidence roots and simple artifact names; recheck every
  receipt leaf immediately before writing; classify generated untracked files; and add
  an additive `forbiddenFiles` artifact contract.
- **Verification plan:** run the release-safety harness under PowerShell 7 and Windows
  PowerShell 5.1, then publish and validate a clean WPF artifact.
- **Owner:** repo owner.
- **Status:** fixed and script-harness verified 2026-07-26. The WPF contract explicitly
  forbids root `libmp3lame.dll`; real untracked FLAC project inputs remain hashable while
  generated native intermediates are counted by class. The final clean publish is part
  of the release gate recorded in the autonomous audit.

### R40. Icecast cleanup failures could escape or mask the primary connection error - bucket A, risk medium

- **Area or slice:** legacy CUEPlayer Icecast construction, connect, rejected-response,
  and background shutdown paths.
- **Why it matters:** a writer-constructor failure bypassed the guarded UI path, while
  `Close` or `Delete` failures could terminate the worker or replace the error the user
  needed to act on.
- **Evidence found:** direct control-flow review of `CUEPlayer/Icecast.cs`.
- **Confidence:** verified.
- **Approval needed:** no.
- **Smallest safe next step:** include construction in the primary try, independently
  contain cleanup, always clear the shared writer in `finally`, and log exception types
  rather than messages.
- **Verification plan:** focused source-invariant test, full WPF regression suite, and
  hosted legacy CUEPlayer build/service smoke.
- **Owner:** repo owner.
- **Status:** fixed and locally verified 2026-07-26. The focused regression passed and
  the canonical WPF suite includes the new invariant. Disposable Icecast 2.5.0 also
  passed source/auth/metadata/listener/teardown smoke, and focused tests prove that
  bitrate/joint-stereo settings reach LAME. The classic AnyCPU solution lane is green;
  a direct frozen-output CUEPlayer compilation receipt is still pending.

### R41. Album reservation cleanup could report failure after publication committed - bucket A, risk high

- **Area or slice:** WPF album-level output transaction.
- **Why it matters:** `Directory.Move` could make the complete album visible and then a
  `DeleteOnClose` disposal failure could escape, falsely telling Rip/Convert to retry a
  transaction that had already committed.
- **Evidence found:** commit-point review of `AlbumOutputTransaction.Publish`.
- **Confidence:** verified.
- **Approval needed:** no.
- **Smallest safe next step:** mark the rename as committed first and make all subsequent
  marker/reservation cleanup best-effort.
- **Verification plan:** inject a reservation `FileStream` that throws after base
  disposal and prove `Publish` still returns the committed destination.
- **Owner:** repo owner.
- **Status:** fixed and verified 2026-07-26. The behavioral failure-injection test passed;
  later `Dispose` cannot rethrow because the reservation field is cleared before cleanup.

### R42. Fuzz child termination could wait forever after its timeout - bucket A, risk medium

- **Area or slice:** isolated corrupted-corpus decoder harness.
- **Why it matters:** after the five-second child timeout, a failed process-tree kill was
  ignored and followed by unbounded `WaitForExit()`, defeating the nontermination gate.
- **Evidence found:** `CorpusFuzzer.RunCorruptionChild`.
- **Confidence:** verified.
- **Approval needed:** no.
- **Smallest safe next step:** bound the post-kill reap, record kill/wait failure by type,
  and return an explicit failed check without any unbounded wait.
- **Verification plan:** source review plus the deterministic 20,000-iteration fuzz gate.
- **Owner:** repo owner.
- **Status:** fixed 2026-07-26; final deterministic fuzz execution is recorded in the
  autonomous audit.

### R43. Partial native codec initialization leaked owned resources - bucket A, risk high

- **Area or slice:** libFLAC and WavPack encoders/readers, the HDCD filter, and the
  libmp3lame writer.
- **Why it matters:** several failure paths allocated a native context, metadata
  objects, an unmanaged callback table, a self-rooting `GCHandle`, or an output
  stream before setting the flag that gated normal cleanup. Constructor or first-write
  failure could therefore leak native state, keep files locked, or retain an entire
  managed codec instance.
- **Evidence found:** direct ownership and failure-path review, including libFLAC
  metadata's borrowed-pointer lifetime and close-before-first-write behavior.
- **Confidence:** verified.
- **Approval needed:** no.
- **Smallest safe next step:** make acquisition transactional, null-check factory
  results, retain borrowed metadata through native finish, release any nonzero handle
  regardless of initialization state, and preserve the primary exception during
  rollback.
- **Verification plan:** safe failure injection, real native round trips, source
  ownership invariants for unsafe-to-inject vendor branches, all-target builds, and
  the canonical codec/full test gates.
- **Owner:** repo owner.
- **Status:** fixed and verified 2026-07-26. The focused lifetime suite passed 10/10;
  libFLAC, WavPack, HDCD, and LAME built across all declared target frameworks; and
  an independent static ownership review found no remaining blocker in these paths.
  Aggregate suite totals are refreshed by the final canonical gate.

### R44. Test & Copy can detach final-output assurance from its proof - bucket A, risk high

- **Area or slice:** CUESheet final-output verification and WPF Test & Copy
  publication.
- **Why it matters:** the source stage can change after its PCM decode. A later copy
  can faithfully reproduce the changed bytes while carrying the earlier Boolean
  assurance claim.
- **Evidence found:** `CUESheet` retains the PCM receipt only through finalization;
  `VerifyResult` carries Boolean/text fields; `CopyDirectoryRecursiveVerified`
  compares source and destination only at copy time.
- **Confidence:** verified.
- **Approval needed:** no; the user approved verification and publication hardening.
- **Smallest safe next step:** expose immutable completed per-output proofs, hold a
  source lease through transfer, and revalidate the exact proof set at the
  destination. Clear the claim when a transform cannot carry the proof.
- **Verification plan:** mutation, path-set, multi-file/HTOA, held acceptance, and
  concurrent-writer tests, followed by WPF and live optical gates.
- **Owner:** repo owner.
- **Status:** fixed and adversarially verified 2026-07-26. `CUESheet` now emits
  immutable exact-output proofs, all three proof-bearing publication paths use one
  destination-bound handoff, cross-format audio and path-order mismatches fail
  closed, and a coordinated replacement after the directory move is quarantined
  without success/history. The focused publication/proof suite passed 33/33 after
  crash recovery hardening. Final canonical and H: optical gates remain in the
  release matrix rather than in the implementation status.

### R45. Classic collection can replace an unowned directory without crash recovery - bucket A, risk high

- **Area or slice:** classic release artifact publication.
- **Why it matters:** a matching versioned path is treated as disposable without an
  ownership receipt. Concurrent collectors or a crash between backup and publish can
  delete or strand a valid artifact.
- **Evidence found:** the collector moves any regular destination to backup, has no
  interprocess lease or recovery journal, and cleans stages by prefix rather than an
  exact ownership token.
- **Confidence:** verified.
- **Approval needed:** no; the user approved release-tooling hardening.
- **Smallest safe next step:** add a cross-session lease, token/tree-bound receipts,
  foreign-destination refusal, journal-before-backup recovery, destination
  revalidation, and exact-token cleanup.
- **Verification plan:** owned/foreign replacement, contention, mismatched token,
  injected move/rollback failures, retained recovery state, restart recovery, and
  reparse tests.
- **Owner:** repo owner.
- **Status:** fixed and locally verified 2026-07-27. The real orchestrator held one
  repo-wide lease through recovery, exact cleanup, four build commands, receipt,
  collection, and publication. The 86-check harness covers ownership, contention,
  rollback, restart, foreign intent, source-stale intent, and malformed evidence.

### R46. A fresh classic stage does not prove fresh compiled inputs - bucket A, risk medium

- **Area or slice:** classic build-to-collection provenance.
- **Why it matters:** same-version binaries left in `bin/Release` can pass the
  collector and artifact validator even when they were built from different source.
- **Evidence found:** collection hashes prove source-to-stage copy equality, while
  the current provenance receipt is generated only after collection.
- **Confidence:** verified.
- **Approval needed:** no; the user approved release-tooling hardening.
- **Smallest safe next step:** generate a pre-collection build receipt binding the
  source commit or dirty-worktree fingerprint, configuration, platform/toolchain, and
  every compiled input hash/length.
- **Verification plan:** reject a prior same-version binary, mismatched dirty-source
  receipt, and modified/missing source before the prior artifact moves.
- **Owner:** repo owner.
- **Status:** fixed and locally verified 2026-07-27. The exact receipt binds the
  source fingerprint, Visual Studio 18.8/v143 toolchain, four command logs, warning
  result, and 95 collection inputs before the 97-file artifact becomes visible.

### R47. Optical visualization allocates and dispatches from the read loop - bucket A, risk medium

- **Area or slice:** `LevelMeteringRipper`, rip view model, and codec scope telemetry.
- **Why it matters:** a new sample array and captured dispatcher callbacks are
  created during optical reads. Reusing one array is unsafe because the UI consumes
  it later.
- **Evidence found:** `LevelMeteringRipper.Read` creates `float[]` windows about every
  20 ms; `RipViewModel` queues captured `BeginInvoke` callbacks; `CodecScope` copies
  only when the UI callback runs.
- **Confidence:** verified.
- **Approval needed:** no; the user approved optical and telemetry hardening.
- **Smallest safe next step:** preallocate a bounded SPSC mailbox before reading.
  Drop presentation windows when full; never block or alter the audio path.
- **Verification plan:** producer allocation, stalled consumer, slot lifetime,
  ordering/scaling, reuse, and queue-full tests, then WPF and live optical gates.
- **Owner:** repo owner.
- **Status:** fixed and focused-test verified 2026-07-26. The optical producer now
  publishes into a bounded preallocated SPSC mailbox, the UI drains a bounded latest
  sample, and codec-scope reset clears stale state. Tests cover the cold
  allocation-free producer, byte decoding, slot lifetime, ordering, reset,
  concurrency, and a stalled/full consumer. The final H: run remains a hardware
  release gate.

### R48. Final-metadata proof lacks real CUESheet output-style coverage - bucket A, risk medium

- **Area or slice:** CUESheet/TagLib/final-output integration tests.
- **Why it matters:** fake receipt tests do not exercise metadata saves, multi-file
  routing, gap styles, HTOA, or one bad output within a set.
- **Evidence found:** current focused tests construct fake audio sources and
  destinations without executing `CUESheet.Go`.
- **Confidence:** verified.
- **Approval needed:** no.
- **Smallest safe next step:** add tiny real lossless fixtures for all output styles
  and a narrow post-TagLib mutation seam.
- **Verification plan:** metadata-only rewrites, one-sample mutation, one bad track,
  and decoder/finalization failure cases.
- **Owner:** repo owner.
- **Status:** fixed and focused-test verified 2026-07-26. Real managed-Flake
  `CUESheet.Go` tests cover all five output styles, HTOA, final TagLib writes,
  one-sample mutation, one bad track in a set, decoder lifecycle, and finalization
  failure.

### R49. FLACCL verification trust is based on spoofable names - bucket A, risk medium

- **Area or slice:** packaged plugin registration and output-assurance evaluation.
- **Why it matters:** matching an assembly simple name and type full name does not
  identify one runtime `Type`; another load context can present both names.
- **Evidence found:** referenced codecs use direct `Type` equality, while FLACCL used
  string comparisons because WPF does not reference the classic plugin.
- **Confidence:** verified.
- **Approval needed:** no; the user approved plugin and verification hardening.
- **Smallest safe next step:** let the manifest-validated package loader register the
  exact FLACCL settings `Type` as a runtime-only capability.
- **Verification plan:** two distinct runtime types with identical assembly/type
  names, packaged FLACCL acceptance, and unrelated/subclass rejection.
- **Owner:** repo owner.
- **Status:** fixed and focused-test verified 2026-07-26. Packaged plugin
  registration enrolls the exact runtime settings `Type`; user/development
  registration does not. Identically named types from another assembly or load
  context, subclasses, and a malformed `DoVerify` property are rejected.

### R50. A crash during destination-bound proof validation leaves an ambiguous album - bucket A, risk medium

- **Area or slice:** proof-bearing album publication and restart recovery.
- **Why it matters:** the process can stop after the same-volume stage move but
  before destination revalidation and final reservation release. The bytes had
  passed the pre-move proof, but the visible directory still represented an
  unfinished transaction and had no explicit restart policy.
- **Evidence found:** adversarial review of `PublishPendingValidation` and
  `CompletePublication`.
- **Confidence:** verified.
- **Approval needed:** no; this is fail-closed publication hardening.
- **Smallest safe next step:** persist a token-bound proof-pending marker before the
  move and recover it only while holding the matching destination reservation.
- **Verification plan:** simulate process loss by disposing a pending publication,
  require exact-token quarantine before name reuse, and prove that an ownership
  marker without the pending receipt is never moved.
- **Owner:** repo owner.
- **Status:** fixed and focused-test verified 2026-07-26. A matching owner/pending
  pair is quarantined under an explicit recovered-incomplete name before reuse.
  Missing or mismatched pending evidence leaves the existing directory untouched.

### R51. Build preparation dirties four pinned vendor submodules - bucket A, risk medium

- **Area or slice:** vendor source preparation, managed/native project references,
  classic release receipts, and Windows workflows.
- **Why it matters:** the checked CUETools patches are reproducible, but applying them
  in place makes clean pinned submodules indistinguishable from later local edits and
  leaves native build residue in vendored worktrees.
- **Evidence found:** all four worktree diffs exactly reverse-checked against the
  tracked patch files; `README.md`, three workflows, the native build helper, five
  managed projects, and `CUETools.sln` consume the patched worktrees directly.
- **Confidence:** verified.
- **Approval needed:** yes for CI/release controls; explicitly approved by the user on
  2026-07-27.
- **Smallest safe next step:** materialize pinned commits plus checked patches beneath
  an ignored, owned staging root; point every patched-source consumer at that root;
  bind the stage identity into classic receipts; and fail if preparation changes a
  submodule.
- **Verification plan:** clean/idempotent preparation, malformed/tampered-stage and
  dirty-submodule rejection, managed and native builds, classic receipt contracts,
  workflow text checks, and before/after recursive submodule status equality.
- **Owner:** repo owner.
- **Status:** fixed and verified 2026-07-27. The ignored stage binds four gitlinks,
  four patch hashes, 1,549 staged files, and the complete file manifest. Its focused
  harness passes 15 checks, native preparation passes 21, and every managed, native,
  fuzz, packaging, and release consumer now uses the stage. Six native targets and
  the exact classic Release matrix built successfully while all five initialized
  submodules remained clean. The classic matrix passed at Any CPU 58/0/11 and x64
  and Win32 9/0/60. The exact release receipt bound 95 collection inputs and the
  published artifact contained 97 files.

### R52. CTDB repaired copies lose source filenames and optional metadata - bucket A, risk high

- **Area or slice:** WPF file/post-rip CTDB repair transaction and the legacy
  `CUESheet` encode adapter.
- **Why it matters:** repaired PCM can verify and publish successfully while the new
  copy uses generic `01.flac`/`album.flac` names. Tag and artwork retention also
  follows ordinary conversion preferences, so disabling those preferences can
  silently strip metadata from a preservation operation.
- **Evidence found:** `VerifyService.CreateRepairConfig` explicitly sets
  `keepOriginalFilenames = false`, `%tracknumber%`, and `album`; it does not override
  `copyBasicTags`, `copyUnknownTags`, `CopyAlbumArt`, or `embedAlbumArt`.
- **Confidence:** verified.
- **Approval needed:** no; the user explicitly requested filename and tag
  preservation on 2026-07-27.
- **Smallest safe next step:** make the private repair config preserve source
  basenames and force representable standard tags, custom tags, and embedded artwork
  into the isolated FLAC copy without enabling verify-time source writes.
- **Verification plan:** config isolation tests plus real Flake/TagLib track-set and
  image-mode integration tests, final-output proof checks after TagLib save, repair
  rollback/publication tests, and the opt-in live damaged-disc CTDB probe.
- **Owner:** repo owner.
- **Status:** fixed and deterministic-test verified 2026-07-27. Repair now retains
  source-derived audio basenames, source-authoritative basic tags, representable
  unknown tags, exact CDTOC values, and embedded artwork into the sibling copy
  without enabling source or sidecar writes. Stale AccurateRip/CTDB proof tags are
  deliberately removed after payload repair. Duplicate final paths are rejected
  before any write. Real managed-FLAC track and disc-image preservation fixtures and
  transaction tests pass 14/14. The opt-in test also asserts live repaired
  basenames; the K: damaged-disc run remains the final external check.

### R53. Calibration noise can disable cache defeat and edge probes were not consumed - bucket A, risk high

- **Area or slice:** WPF first-use drive calibration, SCSI secure rereads, overread,
  Test & Copy evidence, and Rip track UI.
- **Why it matters:** the same caching drive produced different timing buckets.
  Replacing a larger proven flush, or accepting a later apparent "no cache" result,
  makes repeated reads non-independent and can under-report damaged sectors. The
  existing overread fields were always false and the reader always zero-padded disc
  edges. Test/Copy CRC roles were not visible or durable.
- **Evidence found:** repeated ASUS calibration alternated between 786,432 and
  1,048,576 bytes. Paranoid Test & Copy then failed after 19 seconds at the
  flush/seek/payload CDB boundary with `INVALID FIELD IN CDB`. Calibration wrote
  hardcoded false overread flags. The Rip grid had no named Test/Copy CRC fields.
- **Confidence:** verified.
- **Approval needed:** no; the user explicitly requested these corrections on
  2026-07-27.
- **Smallest safe next step:** make calibration a versioned first-read gate, retain
  positive cache evidence and the largest proven flush, make eviction
  complete-or-explicit, probe and consume exact offset-sized edges, and persist named
  Test/Copy CRC roles.
- **Verification plan:** deterministic policy/history/third-read tests; net47/net8
  SCSI builds; full WPF suite; an isolated H: Paranoid run beyond the former failure;
  final damaged K: Test & Copy and end-of-disc overread after device reset.
- **Owner:** repo owner.
- **Status:** fixed and software/H:-hardware verified 2026-07-27. H: completed 25
  Paranoid cache-defeat windows twice; the final-source run used the real offset,
  consumed the end-of-disc path, and passed in 2 minutes 53 seconds. The WPF suite
  passes 350/350, ripper suites pass net8 8/8 and net47 17/17, and SCSI builds for
  net8/net47/net20. After an elevated device restart and tray cycle, K: reopened
  and completed real Paranoid Test and Copy phases. Named Test and Copy CRC snapshots
  appeared after their respective reads. Mismatches on two tracks correctly triggered
  a confirming third read. That later read failed its cache-defeat assurance after
  953 seconds; R66 tracks the failure identity and staging outcome.

### R54. Multiple drives cannot rip concurrently under one safe job controller - bucket B, risk high

- **Area or slice:** WPF multi-drive UX, rip job ownership, Stop/keep-awake state,
  cross-process drive leases, shared persistence, and output publication.
- **Why it matters:** users with two or more drives should be able to rip in
  parallel without launching arbitrary duplicate windows, selecting the same drive
  twice, corrupting shared caches, losing settings, or allowing one job's Stop or
  cleanup to affect another.
- **Evidence found:** drive letters are enumerated dynamically and independent
  device opens can stream concurrently, but `RipService` currently has one
  `_current` CUESheet and one `_stopRequested` latch, and `RipViewModel` owns one
  active job. Separate processes isolate those fields, and album reservations plus
  gzip calibration/history updates are cross-process safe. Settings are
  last-writer-wins, diagnostic names can collide within one second, legacy metadata
  and LocalDB caches are not transaction-safe, and no cross-process lease prevents
  two instances from opening the same physical drive.
- **Confidence:** verified by code trace.
- **Approval needed:** no; the user requested concurrent multi-drive ripping on
  2026-07-27.
- **Smallest safe next step:** use an explicit process-per-drive window boundary.
  Add a cross-process lease keyed by both the current letter and Windows physical
  storage identity, unique per-process logs, a single-writer settings policy,
  non-sensitive launch arguments, and independently owned status/Stop controls.
  Do not emulate concurrency by sharing the current singleton job state.
- **Verification plan:** simultaneous real H:/K: rips; a negative test that a
  second worker cannot claim the same drive; cross-process output-name collision,
  history/calibration update, settings-exit, log-uniqueness, worker-crash, Stop,
  CTDB repair, and machine-sleep/tray-lock tests.
- **Owner:** repo owner.
- **Status:** implementation staged and focused-test verified 2026-07-27. A running
  Rip page offers only other attached drives and opens the selected one in a
  separately titled CUETools process. The job lease holds both drive letter and
  Windows device number across calibration and every child read; nesting is
  thread-owner-bound, while other threads/processes fail before touching hardware.
  Secondary windows load but never save shared settings, settings publication is
  cross-process serialized, command lines contain only role and drive letter, and
  log names include process identity plus a nonce. Album publication and gzip
  evidence stores retain their existing cross-process transactions. The new lease
  launch, and settings-writer contracts pass 6/6 focused tests. The full WPF suite
  passes 358/358, release safety passes 41 checks, all five staged vendor worktrees
  remain clean, and a separate clean self-contained artifact passed its 36-file,
  14-entry runtime-trust, 19-registration, and five-native-probe contract.
  The first live run showed that the separate launcher was not discoverable through
  the disabled active-drive selector. R67 now makes that selector launch another
  attached drive without retargeting the current job. Simultaneous H:/K:, same-drive
  denial, independent Stop, and crash-release hardware proof remain.

### R55. Payload medium errors bypass damaged-sector recovery - bucket A, risk high

- **Area or slice:** SCSI `FetchSectors`, secure voting, failed-sector reporting,
  Test & Copy, and post-rip CTDB repair.
- **Why it matters:** a disc may complete all configured retries on several damaged
  windows, then lose the entire Test & Copy transaction when a later READ CD command
  reports a medium error that is not the one legacy special case.
- **Evidence found:** the K: Copy read retained unreadable windows at 84% and 86%,
  then aborted after 744 seconds with `medium error: NO SEEK COMPLETE`.
  `FetchSectors` degrades only `DeviceFailed` sense `64/00` to single-sector reads;
  all other payload failures throw.
- **Confidence:** verified.
- **Approval needed:** no; the user requested damaged-disc recovery and CTDB repair.
- **Recommended next reference or pass type:** pass 11 bounded safe fix.
- **Smallest safe next step:** split payload medium-error batches into single-sector
  reads, mark persistent medium-error sectors as untrusted input to the existing
  vote, and continue only under the existing retry/stop policy.
- **Verification plan:** pure failure-policy tests, all SCSI target builds, ripper
  and WPF suites, then repeat the damaged K: Test & Copy lane.
- **Owner:** repo owner.
- **Status:** implemented and deterministic-test verified 2026-07-27. Only
  device-reported medium errors are converted into damaged-sector evidence.
  Multi-sector payload failures split into individual reads; persistent
  single-sector medium errors enter the existing low-confidence vote and retry
  path. Hardware, not-ready, unit-attention, command, removal, and transport
  failures remain fatal. The modern ripper suite passes 14/14, the WPF suite passes
  352/352, and the SCSI project builds for net8/net47/net20. The damaged K: rerun is
  still required to prove the real `NO SEEK COMPLETE` path completes as failed
  sectors and reaches CTDB.

### R56. Fixed Rip-page rails clip actions at narrow widths - bucket A, risk medium

- **Area or slice:** modern WPF Rip page layout.
- **Why it matters:** the default 1200-pixel window can hide the correction-quality
  selector and primary commands, and long album headings are clipped without an
  accessible full value.
- **Evidence found:** user captures at 1200 and 1784 pixels; `RipView.xaml` fixes
  the side rails at 252 and 234 pixels and places every setting and action in one
  non-wrapping `DockPanel`.
- **Confidence:** verified.
- **Approval needed:** no; this is a presentation-only accessibility fix.
- **Recommended next reference or pass type:** pass 11 bounded safe fix.
- **Smallest safe next step:** use bounded proportional rails, wrapping actions,
  rail scrolling, title trimming/tooltips, and a below-minimum horizontal fallback.
  Keep the default-on Deep recovery policy in Settings instead of spending the
  primary action row on a per-rip-looking checkbox.
- **Verification plan:** XAML layout-contract test, full WPF suite, then visual
  inspection at 1784, 1200, and 1024 logical pixels.
- **Owner:** repo owner.
- **Status:** implemented and deterministic-test verified 2026-07-27. The layout
  contract requires viewport-bound work grids, bounded proportional rails,
  vertically scrolling side content, wrapped actions, and tooltip-backed trimming.
  Deep recovery remains a durable default-on expert setting but no longer consumes
  the primary action row.
  The WPF suite passes 352/352. A loaded-disc 1200-pixel capture proved the action
  controls and CRC columns remain reachable after the viewport fix. Final-source
  1784-, 1200-, and 1024-pixel presentation captures remain.

### R57. Accepted speed changes can leave the next payload briefly unready - bucket A, risk high

- **Area or slice:** adaptive optical speed control, SCSI `PrefetchSector`, payload
  failure context, Test & Copy, and damaged-disc recovery.
- **Why it matters:** an intermittent command-state rejection aborts Test & Copy
  before CTDB can receive the damaged-sector evidence, even though an identical
  payload succeeds on other runs.
- **Evidence found:** K: firmware 3.11 failed Test read 1 after 28 seconds with
  `IllegalRequest` ASC/ASCQ `24/00` at the ordinary `FetchSectors` call. Archived
  runs on the same drive failed at 18 and 19 seconds with the same sense, while
  otherwise identical runs progressed for minutes or completed. SET CD SPEED is
  serialized at the fresh-window boundary but the first payload followed
  immediately. The prior claim that serialization alone eliminated the failure was
  false.
- **Confidence:** verified.
- **Approval needed:** no; the user requested autonomous damaged-disc completion.
- **Recommended next reference or pass type:** pass 11 bounded safe fix.
- **Smallest safe next step:** add a short bounded settle after an accepted speed
  change and retry once only for transition-bound `IllegalRequest 24/00`. Capture
  scrubbed payload shape and transition state in the fatal exception. Never make
  illegal requests generally retryable. Record the bounded retry count on a
  completed phase so a recovered firmware transition remains observable.
- **Verification plan:** pure transition-policy tests, all SCSI target builds,
  ripper and WPF suites, then repeat K: Test & Copy beyond the former failure point.
- **Owner:** repo owner.
- **Status:** implemented and deterministic-test verified 2026-07-27. The modern
  ripper suite passes 14/14, the WPF suite passes 352/352, and SCSI builds pass for
  net8/net47/net20. K: then completed the full 1,179-second Test phase and advanced
  its Copy phase to relative sector 283328 without another transition-bound
  failure; the later nested pinpoint rejection is tracked separately as R58.

### R58. A medium-error batch can expose a transient illegal-request pinpoint - bucket A, risk high

- **Area or slice:** SCSI nested failure identity, medium-error decomposition, Test
  and Copy phases, secure
  read evidence, and damaged-disc completion.
- **Why it matters:** a batch-level medium error is valid damaged-media evidence,
  but the drive can answer one subsequent pinpoint read with a different transient
  command failure. Reusing the parent context hides the exact sector and makes a
  safe recovery decision impossible. Treating every nested command failure as media
  damage would invent evidence; aborting every corroborated transient loses an
  otherwise recoverable long-running job.
- **Evidence found:** the first post-R57 run completed Test, then failed Copy near
  relative sector 283328. The `e2c3ba6` rerun failed Test at the same displayed
  16-sector context after 752 seconds. Its exact stack stopped at the non-medium
  child guard inside the medium-error split, proving that the outer 16-sector
  command reported `MediumError` and a subsequent single-sector command reported
  `IllegalRequest 24/00`. The exception incorrectly reused the outer range. A
  read-only production-reader probe then consumed the full 2,400-sector
  neighborhood from 283328, and a second probe consumed the actual 283200 window
  at the failed run's 4224 kB/s request. Both passed, proving the address and
  one-sector command shape are valid outside the intermittent state.
- **Confidence:** verified.
- **Approval needed:** no; the user requested autonomous damaged-disc completion.
- **Recommended next reference or pass type:** pass 11 bounded safe fix.
- **Smallest safe next step:** preserve exact child context and sense immediately.
  Only after a parent medium error, retry an exact pinpoint `24/00` once after a
  bounded settle. Consume only a successful retry. If the retry reports medium
  error or repeats the same corroborated LBA-specific `24/00`, mark that exact
  sector untrusted for the existing vote/CTDB path. Any other repeat remains fatal.
  Count retries and corroborated unreadable pinpoints.
- **Verification plan:** pure classifier positives and negatives; modern ripper and
  WPF suites; net8/net47/net20 SCSI builds; retained opt-in window probe; then
  repeat K: Test & Copy beyond relative sector 283328 and confirm a completed Copy
  CRC or an exact child failure.
- **Owner:** repo owner.
- **Status:** implemented and bounded-test verified 2026-07-27. The modern ripper
  policy suite passes 18/18, the WPF suite passes 352/352, SCSI builds pass for
  net8/net47/net20, and both bounded K: window probes pass. The end-to-end rerun
  remains.

### R59. Rejected-batch decomposition can expose the same failing pinpoint - bucket A, risk high

- **Area or slice:** SCSI rejected-payload decomposition, nested failure ancestry,
  Test & Copy, secure vote input, and CTDB repair reachability.
- **Why it matters:** the same exact one-sector `24/00` can be reached from two
  different parents. R58 covered a parent medium error, but a parent multi-sector
  `24/00` takes the transfer-shape fallback first. Throwing its failed child aborts
  a twelve-minute damaged-disc read before the vote and CTDB paths can consume
  explicit untrusted-sector evidence.
- **Evidence found:** the final-source K: rerun started at 18:14:04 and failed after
  739 seconds at relative sector 283328, one sector, `READ CD BEh`, 4224 kB/s.
  The exact context included `batch-fallback=True`, proving the parent entered
  rejected-batch decomposition rather than R58's medium-error branch. This is why
  the covered-looking R58 policy never ran.
- **Confidence:** verified from the production diagnostic and source branch.
- **Approval needed:** no; the user requested autonomous damaged-disc completion.
- **Smallest safe next step:** snapshot the rejected-batch child sense. Retry only
  an exact child `24/00` once after the same bounded settle. Consume only successful
  retry bytes. An exact medium error, or a repeated exact `24/00` corroborated by
  the different parent/child command shapes, marks only that sector untrusted.
  Every other child or repeat remains fatal.
- **Verification plan:** classifier route positives/negatives; modern ripper and
  WPF suites; net8/net47/net20 SCSI builds; then repeat K: Test & Copy past sector
  283328 and require completion or a differently classified exact failure.
- **Owner:** repo owner.
- **Status:** implemented and deterministic-test verified 2026-07-27. No rejected
  payload is consumed. The modern ripper policy suite passes 20/20, the WPF suite
  passes 358/358, and the SCSI project builds for net8/net47/net20 with the exact
  Visual Studio 18.8 full-MSBuild host used for net20. Release safety passes 41
  checks, all five staged vendor worktrees remain clean, and the separate clean
  self-contained artifact passes its production contract. The K: end-to-end rerun
  remains.

### R60. Legacy satellite linking receives incompatible revision metadata - bucket A, risk low

- **Area or slice:** net20 resource-bearing projects, satellite resource linking,
  and the checked modern warning baseline.
- **Why it matters:** repeated legacy build warnings conceal new warnings and make
  a successful receipt look less trustworthy than it is.
- **Evidence found:** 36 projects target net20; five contain source resources:
  Bwg.Scsi, CUETools.Ripper.SCSI, CUEControls, CUETools.Codecs.Flake, and
  CUETools.Codecs.WMA. Full MSBuild reproduced AL1053 where revision-suffixed
  product versions reached the legacy Assembly Linker. Setting
  `IncludeSourceRevisionInInformationalVersion=false` only for net20 removed every
  AL1053 while retaining numeric assembly versions. A prior MSB3088 disappeared
  on a clean full-MSBuild rebuild and was traced to an unsupported `dotnet` net20
  attempt. `CDDriveReader.cdtext` has one declaration, one commented historical
  assignment, and no live reads or writes.
- **Confidence:** verified.
- **Approval needed:** no; the user requested this warning cleanup and previously
  approved autonomous remediation.
- **Smallest safe next step:** condition the informational-version property on
  `net20` in all five resource-bearing projects, remove the dead field and its
  accepted warning fingerprint, and use full MSBuild for the legacy resource lane.
- **Verification plan:** require a warning-free full-MSBuild net20 rebuild, build
  net47/net8, run ripper/WPF tests and the warning gate, and keep vendor worktrees
  clean.
- **Owner:** repo owner.
- **Status:** implemented and verified 2026-07-27. Full Visual Studio MSBuild
  rebuilds all five net20 resource-bearing projects with zero AL1053 warnings.
  The net47 and net8 SCSI builds are also clean; ripper tests pass 20/20, WPF
  tests pass 358/358, and the modern warning gate passes with zero emitted
  warnings against an empty baseline. The transient MSB3088 requires no source
  suppression.

### R61. Dead codec paths and native-populated fields share one warning class - bucket A, risk medium

- **Area or slice:** managed ALAC and Flake encoders, libFLAC decoder interop, and
  ZIP decompression.
- **Why it matters:** treating every CS0649 as dead code could remove fields that
  native libFLAC writes into unmanaged callback memory. Leaving genuinely dead
  encoder paths beside those fields makes that mistake easier.
- **Evidence found:** ALAC has 13 candidate C# files; `cbits` and `porder` have one
  declaration each and no live use. Its window loop always breaks before the
  compiler-reported tail. Flake has 19 candidate C# files; `sr_code1` is read but
  never assigned, while initialization explicitly rejects sample rates outside its
  table. The four libFLAC C# files show `FLAC__Frame*` arriving from the native
  decoder write callback and the header fields being consumed there. The two ZIP
  C# files show password acquisition owned by `ZipCompressionProvider`; the
  stream's duplicate event has no subscriber or invocation, but it is a public
  compatibility surface and must not be deleted as dead private code. A full
  net20 rebuild also found `MediaSlider.Dispose()` intentionally occupying the
  inherited public signature without declaring `new`; adding the keyword preserves
  its existing behavior and binary member.
- **Confidence:** verified.
- **Approval needed:** no.
- **Smallest safe next step:** delete only proven-dead internal fields and
  branches, retain executed statements and public API, and scope documented
  suppressions to the native callback packet and compatibility-only ZIP event.
- **Verification plan:** touched-project builds, encode verification tests, full
  WPF tests, warning gate, publish, and native plugin probes.
- **Owner:** repo owner.
- **Status:** fixed and verified 2026-07-27. Touched codec projects build with
  zero warnings for their applicable netstandard2.0, net47, and net20 targets.
  The public ZIP event and native-owned libFLAC packet remain intact. The WPF
  suite passes 358/358, and the modern warning gate emits zero warnings.

### R62. Nullable warnings hide missing result and persistence guards - bucket A, risk high

- **Area or slice:** WPF startup, drive calibration, metadata naming, persisted
  verify history, Test & Copy, output layout, and rip result publication.
- **Why it matters:** most locations already degrade null to an empty display value,
  but Test & Copy currently adds nullable checksum records to a non-null comparison
  list after checking only `Ok`. A record-build failure on a verify-only phase can
  therefore escape its intended boundary and fail later without a phase-specific
  result.
- **Evidence found:** the 34 checked fingerprints expand to 51 warning locations in
  no-incremental WPF and fuzz builds. `VerifyResult.Record` is explicitly nullable;
  encode requires it, but verify-only allows record construction to fail while the
  optical read remains successful. Other locations are optional store returns,
  legacy metadata, WPF nullable drawing parameters, or BCL methods such as
  `Path.GetDirectoryName`.
- **Confidence:** verified.
- **Approval needed:** no; the user explicitly requested the warning cleanup.
- **Smallest safe next step:** express nullable inputs and returns, add explicit
  invariants where null was already fatal, and reject a missing Test & Copy record
  immediately. Empty the baseline only after both warning-gated builds emit zero.
- **Verification plan:** focused policy and persistence tests, all 358 WPF tests,
  no-incremental WPF/fuzz builds, empty warning gate, clean publish/artifact
  contract, and vendor-clean check.
- **Owner:** repo owner.
- **Status:** fixed and verified 2026-07-27. WPF and fuzz no-incremental builds
  emit zero warnings, the checked warning baseline is empty, and all 358 WPF
  tests pass. Test & Copy rejects a missing checksum record at its phase boundary.

### R63. The classic solution has a separate unmanaged warning budget - bucket A, risk medium

- **Area or slice:** classic managed solution build, FLACCL/OpenCL, legacy UI,
  CoreAudio, resampler, LossyWAV, and three command-line target frameworks.
- **Why it matters:** the modern gate can be empty while the full solution still
  emits warnings. Mixing GPU-populated fields, pinned third-party compatibility
  calls, dead state, and unsupported frameworks in one unclassified set makes
  future warning cleanup unsafe.
- **Evidence found:** the Visual Studio Release Any CPU rebuild succeeded for 58
  projects but emitted 28 warning lines with 19 distinct messages. Four FLACCL
  task fields are read from a pinned OpenCL buffer after a device-to-host copy.
  Two LossyWAV fields and one resampler local have no live use. CoreAudio's
  private sample offset is never changed, so its public position has always been
  zero. FLACCL advertises and parses `--ignore-chunk-sizes` but does not apply it
  to file input. Three command-line projects target unsupported netcoreapp2.0.
  The remaining four warnings come from the immutable OpenCLNet gitlink.
- **Confidence:** verified from a full clean build and bounded source searches.
- **Approval needed:** no; this is part of the requested warning cleanup.
- **Smallest safe next step:** fix the proven first-party cases, retain and
  document GPU-owned packet fields, move the three command-line targets to net8.0,
  and scope exact warning suppression to the pinned OpenCLNet project.
- **Verification plan:** affected-project rebuilds, available-device FLACCL
  exercise, codec/WPF tests, warning gate, and a zero-managed-warning classic
  solution rebuild.
- **Owner:** repo owner.
- **Status:** fixed and verified 2026-07-27. Release Any CPU rebuilt 58 projects
  with zero managed warnings and no failures; Release x64 and Win32 each rebuilt
  nine selected projects with zero managed warnings and no failures. Native
  warnings remain governed by their separate checked baseline. FLACCL verify
  passed on the RTX 3060 through OpenCL 3.0 with the repaired option enabled.
  Codec tests pass 112/113 with one pre-existing skip, WPF tests pass 358/358,
  and the three net8 command-line outputs start with complete dependency closures.

### R64. Native libFLAC conversions hide one truncating repair path - bucket A, risk high

- **Area or slice:** staged libFLAC 1.5.0 bit writing, fixed and LPC residuals,
  metadata size checks, stream decoding, channel decorrelation, and encoder
  apodization parsing.
- **Why it matters:** the checked native allowance contains arithmetic in the
  lossless codec path. Most warnings represent intentional narrowing after a
  local bound, but the missing-frame silence calculation narrows an untrusted
  64-bit sample-number gap before its safety cap. Large gaps can wrap and bypass
  the intended repair length.
- **Evidence found:** 61 warning emissions across Win32 and x64 builds map to 31
  exact source sites and eight normalized fingerprints in seven libFLAC files.
  Current upstream master replaces the bit-writer byte multiplication with a
  word-capacity check. The fixed-predictor selector rejects any residual above
  `INT32_MAX`; LPC limit paths check the same bound; constant and warm-up decoder
  branches require at most 32 bits. The decoder gap is a `FLAC__uint64`
  subtraction assigned directly to `uint32_t` before the five-second and
  50-frame caps. Windows PowerShell 5.1 also reads the UTF-8 staging manifest
  with its legacy default encoding, corrupting a non-ASCII libFLAC test path.
  Expanding verification through upstream CMake exposed three more MSVC warnings:
  a redundant non-CRT `getenv` declaration and a late CRT math-constant include.
- **Confidence:** verified from both native build logs, bounded source searches,
  the pinned 1.5.0 source, and current upstream source.
- **Approval needed:** no; the user explicitly requested the native correctness
  pass.
- **Smallest safe next step:** carry the gap through its cap in 64 bits, validate
  33-bit decorrelation before narrowing, apply the upstream bit-writer fix, and
  make only range-proven conversions explicit. Empty the native warning baseline
  only after both architectures emit zero warnings. Pin manifest reads to UTF-8
  and exercise staging under both installed PowerShell engines.
- **Verification plan:** Win32/x64 native rebuilds and warning gate, native FLAC
  round trips and verify-on-encode tests, available upstream libFLAC tests,
  vendor-clean gate, then refreshed release receipts after the active rip ends.
- **Owner:** repo owner.
- **Status:** fixed and locally verified 2026-07-27. All six native dependency
  builds emit zero warnings against an empty baseline. The expanded clean CMake
  build also emits zero warnings. Native-backed FLAC tests pass 25/25, upstream
  libFLAC tests pass 2/2, classic codec tests pass 112/113 with one established
  skip, and WPF tests pass 358/358. Staging passes 15 checks under PowerShell 7,
  the final stage validates under Windows PowerShell 5.1, and all five submodules
  remain clean. The source-bound classic receipt remains pending until the
  active rip ends.

### R65. Core MSBuild cannot resolve legacy Visual Studio rulesets - bucket A, risk low

- **Area or slice:** old-style managed projects built through `dotnet`, first
  observed in `CUETools.TestHelpers`.
- **Why it matters:** a clean native verification run still emits MSB3884 while
  building its managed test harness, which makes the repository's zero managed
  warning claim false for that supported command.
- **Evidence found:** 10 projects name `AllRules.ruleset`; no ruleset is tracked
  in the repository. Full Visual Studio installs the named file in its static
  analysis directory, while Core MSBuild does not search that directory and
  reports that the file cannot be found.
- **Confidence:** verified from the build diagnostic, bounded project search,
  and installed Visual Studio paths.
- **Approval needed:** no; the user requested autonomous warning cleanup.
- **Smallest safe next step:** clear only the unresolved Core MSBuild property
  while preserving the declaration and full Visual Studio behavior.
- **Verification plan:** rerun the net47 codec suite with no MSB3884, then
  confirm the full Visual Studio solution still rebuilds with zero managed
  warnings.
- **Owner:** repo owner.
- **Status:** fixed and locally verified 2026-07-27. The direct Core MSBuild
  rebuild completes with zero warnings. Full Visual Studio receipt verification
  remains paired with the post-rip classic release run.

### R66. A failed confirming read deleted a completed Test & Copy result - bucket A, risk high

- **Area or slice:** secure cache defeat, Test & Copy confirmation, staging
  ownership, and phase evidence.
- **Why it matters:** Test and Copy can finish and disagree on a damaged disc, then
  a later confirming read can fail. Treating the whole operation as an ordinary
  failure deletes the completed encoded Copy and forces another hour-long read.
- **Evidence found:** the K: diagnostic completed Test and Copy, published both CRC
  roles, started read 3, and failed after 953 seconds with only the generic
  cache-independence message. `FlushCache` discarded command status and sense.
  `RunTestAndCopy` returned an error for every failed confirming read, so its final
  cleanup deleted the owned Copy staging.
- **Confidence:** verified from the production diagnostic, screenshot, and source
  trace. The exact failed cache command sense remains unknown because the old code
  discarded it.
- **Approval needed:** no; the user requested the correction and damaged-disc
  completion.
- **Smallest safe next step:** retain strict independent-read assurance, retry only
  an exact transient `DeviceFailed/IllegalRequest/24/00` cache command once, report
  the final command identity, and hold a completed Copy when confirmation cannot
  finish.
- **Verification plan:** classifier negatives, ripper and WPF suites, all SCSI
  targets, loaded-drive cache probe, then the exact K: damaged window and full
  Test & Copy after media is loaded.
- **Owner:** repo owner.
- **Status:** implemented and software/H:-hardware verified 2026-07-27. The cache
  retry is bounded to one exact 24/00 per flush and is counted in completion logs.
  A final command rejection reports relative sector, transfer size, status, sense,
  ASC/ASCQ, region count, and retry count; an unexpected exception reports its type.
  A failed confirmation returns Held, preserves the completed Copy, writes nothing
  to the destination, and keeps Accept/Discard/Re-run as explicit choices. Ripper
  tests pass 21/21, WPF tests pass 359/359, SCSI builds pass net8/net47/net20, and H:
  passed a Paranoid 786,432-byte cache-defeat window at 4224 kB/s in 15 seconds. K:
  later reproduced a first-Test cache-defeat failure after 1,227 seconds of damaged
  media recovery. All three 16-sector eviction regions ended in exact
  `IllegalRequest 24/00` after the bounded retry. R69 owns that new command-shape
  evidence.

### R67. The active drive selector hid the safe parallel-drive path - bucket A, risk high

- **Area or slice:** Rip-page drive selection, process-per-drive launch, immutable
  job ownership, and independent Stop/status state.
- **Why it matters:** an alternate launcher does not help when the visible drive
  selector is disabled and the user cannot discover how to start the second drive.
  Re-enabling ordinary selection without a job snapshot would instead retarget the
  current window's evidence and cancellation state.
- **Evidence found:** the Rip selector bound `IsEnabled` to `ControlsUnlocked`,
  which becomes false for the full optical job. The safe secondary process,
  physical-drive lease, and bottom action-row launcher already existed.
- **Confidence:** verified from the live UI and source trace.
- **Approval needed:** no; the user explicitly requested concurrent multi-drive
  operation.
- **Smallest safe next step:** keep the active job pinned, leave the selector
  available, and interpret another attached drive as a request to launch the
  existing isolated worker. Reassert the owned drive in the current ComboBox.
- **Verification plan:** XAML and launch-contract tests, full WPF suite, then a live
  H:/K: run proving different-drive launch, same-drive denial, and independent Stop.
- **Owner:** repo owner.
- **Status:** implemented and hardware verified 2026-07-27. The top
  selector remains available during Rip, Verify, and Test & Copy. Choosing another
  drive launches only a validated drive letter in a secondary process and restores
  the current selector to the immutable job drive. H: completed an 11-track Test &
  Copy while the isolated K: process performed its damaged-disc Test read. The
  current-window CRCs, status, and completion output remained bound to H:. Same-drive
  denial, independent Stop, and crash release remain separate live checks.

### R68. Test & Copy outputs dropped CTDB repair reachability - bucket A, risk high

- **Area or slice:** Test & Copy result transfer, held acceptance, committed output,
  and post-rip CTDB repair.
- **Why it matters:** the Copy read already computes recoverable CTDB damage, but a
  verified or manually accepted Test & Copy output could not offer the same repair
  button as an ordinary lossless rip.
- **Evidence found:** `VerifyResult` carried repair assessment and source identity,
  while `TestCopyRunResult` carried only CTDB confidence totals. The Test & Copy
  completion and Accept paths never called `SetPostRipRepair`.
- **Confidence:** verified from source trace.
- **Approval needed:** no; the user requested post-rip CTDB repair for completed
  damaged output.
- **Smallest safe next step:** carry the Copy or selected committed read's repair
  assessment, store only its contained relative source during staging, rebind it
  against the exact published album, and use the existing source-preserving repair
  transaction.
- **Verification plan:** contained/missing/traversal path tests, full WPF suite,
  then the K: damaged-disc completion and repair route.
- **Owner:** repo owner.
- **Status:** implemented and live-verified 2026-07-28. Passed and
  accepted Test & Copy outputs now expose CTDB repair only when the published repair
  source exists inside the committed album. Missing and traversal candidates fail
  closed. The K: damaged-disc output offered and completed a six-sector repair. Its
  independently decoded sibling matched AccurateRip at 55/82 and CTDB at 207/234,
  while all 25 source hashes remained unchanged.

### R69. Cache defeat used only the drive's rejected 16-sector command shape - bucket A, risk high

- **Area or slice:** secure reread cache eviction and damaged-disc recovery.
- **Why it matters:** K: can accept the normal cache-defeat command for many
  minutes, then reject every unrelated 16-sector region with `IllegalRequest
  24/00`. Stopping at that shape discards an otherwise valuable long read, while
  ignoring the failure would falsely claim an independent reread.
- **Evidence found:** the 2026-07-27 K: diagnostic reached two damaged windows,
  performed up to 63 recovery passes, then failed the first Test after 1,227
  seconds. The final diagnostic recorded relative sector 246134, 16 sectors,
  three attempted regions, one transient retry, and exact `24/00`.
- **Confidence:** verified from the published-build diagnostic and screenshot.
- **Approval needed:** no; this preserves the existing fail-closed contract.
- **Smallest safe next step:** after all normal regions reject only the proven
  command shape, retry the same required eviction volume with deterministic
  8/4/2/1-sector chunks. Consume no rejected payload and authorize the next secure
  read only after the full byte count succeeds.
- **Verification plan:** classifier and SCSI suites, net8/net47/net20 builds, then
  a K: deep-recovery probe at the damaged window with the measured 786,432-byte
  cache volume and the full Test & Copy repeat.
- **Owner:** repo owner.
- **Status:** implemented and software verified 2026-07-27. The fallback is
  limited to exact `DeviceFailed/IllegalRequest/24/00`, preserves the requested
  sector count and in-program bounds, stops after one-sector commands, and reports
  both transient retries and chunk fallbacks. Ripper tests pass 22/22. K: hardware
  proof remains.

### R70. Human-facing rip sidecars were not identifiable outside their folder - bucket A, risk medium

- **Area or slice:** rip, convert, Test & Copy, repair discovery, and overwrite
  protection.
- **Why it matters:** `album.cue`, `album.log`, `album.accurip`, and `Test &
  Copy.log` lose their identity when attached, indexed, or copied away from the
  album directory. Blindly renaming every artifact would also break transaction
  ownership and legacy repair discovery.
- **Evidence found:** the successful H: output used all four generic names beside
  eleven FLAC files. `GenerateFilenames` derives the rip and AccurateRip log stems
  from the cue output path. Repair discovery required literal `album.cue`, and the
  overwrite guard listed only fixed names.
- **Confidence:** verified from the published output and source trace.
- **Approval needed:** no; the user requested identifiable sidecar names.
- **Smallest safe next step:** use one sanitized, bounded artist/album/year/disc
  stem for human-facing cue and logs. Keep `.cuetools-complete`, `rip.verify`, and
  ownership/proof markers stable. Accept legacy cues, require exactly one cue for
  repair, and detect named sidecars by extension.
- **Verification plan:** naming, legacy compatibility, ambiguity, overwrite,
  repair, convert, proof-transfer, and full WPF tests, followed by a real rip.
- **Owner:** repo owner.
- **Status:** implemented and software verified 2026-07-27. New outputs use
  `<artist> - <album> (<year>).cue/.log/.accurip` and `<stem> - Test & Copy.log`,
  with optional disc identity. Legacy `album.*` remains supported. Multiple cues
  fail repair discovery closed. WPF tests pass 367/367. Live output proof remains.

### R71. Damaged Test & Copy and repaired outputs overstated their evidence - bucket A, risk high

- **Area or slice:** Test & Copy result wording, CTDB status, repair publication,
  user receipts, and diagnostics.
- **Why it matters:** two agreeing reads prove repeatability, not pristine media,
  when both contain unrecoverable windows. A published repaired sibling also needs
  durable proof of the source, repaired payload, database result, and completion
  order.
- **Evidence found:** K: produced matching Test and Copy CRCs with six repairable
  sectors, but its log said `Test & Copy PASSED` and `CTDB: not found`. The first
  repaired sibling verified correctly but contained no completion marker, machine
  receipt, AccurateRip report, repair report, or success diagnostic.
- **Confidence:** verified from the K: output, logs, null comparison, and source
  trace.
- **Approval needed:** no; the user requested correction of all observed issues.
- **Smallest safe next step:** classify clean verification separately from damaged
  consistency, report database presence separately from exact-match confidence,
  and seal a repair receipt from SHA-256 source/output proofs before atomic
  publication. Write the completion marker last.
- **Verification plan:** damaged/clean wording tests, source and output mutation
  tests, receipt/artifact tests, full WPF suite, and the opt-in live CTDB repair.
- **Owner:** repo owner.
- **Status:** fixed and live-verified 2026-07-28. Damaged agreement is reported as
  `CONSISTENT`, with repair-required wording and CTDB presence retained. Repair
  publication now requires unchanged source proofs, unchanged independently decoded
  output proofs, a fresh AccurateRip report, a human CTDB repair report,
  `repair.verify`, and a final `.cuetools-complete` marker. Focused tests pass 45/45,
  the WPF suite passes 375/375, and the live 86-sample/six-sector repair published
  all evidence with AccurateRip 55/82 and CTDB 207/234.

### R72. Album art selection loses release identity and uses an unsuitable Apple path - bucket B, risk high

- **Area or slice:** WPF metadata selection, artwork networking, image decoding,
  settings, Rip UI, and output publication.
- **Why it matters:** the current service selects the first Apple result, downloads
  an undocumented high-resolution URL, and embeds it without showing alternatives
  or proving the release edition. Apple documents Search API art as store
  promotional content. A separate processor CTDB fallback can choose different art
  from the UI.
- **Evidence found:** `AlbumArtService` is Apple-only, bypasses the app proxy, and
  collapses all failures to null. `CUEMetadata.FillFromCtdb`,
  `CUEMetadataEntry`, and `ReleaseMatch` drop CTDB source IDs before WPF lookup.
  `CUESheet.LoadAndResizeAlbumArt` is a second hidden selection path. Apple,
  MusicBrainz/Cover Art Archive, and TheAudioDB provider contracts were checked on
  2026-07-28.
- **Confidence:** verified from source and current official provider documents.
- **Approval needed:** yes; this intentionally changes provider behavior and adds
  a network/image trust boundary. The owner requested the feature and delegated
  ranking logic, so implementation may start after review of the recorded plan.
- **Smallest safe next step:** execute Slice A in
  `docs/review/album-art-discovery-plan.md`: preserve source-specific release IDs,
  commit bounded provider fixtures, and add pure candidate/ranking contracts
  without changing the visible provider result.
- **Verification plan:** pure rank and parser tests; response, redirect, byte,
  pixel, proxy, cache, cancellation, privacy, and secret tests; responsive and
  accessible selector tests; full WPF/publish gates; then real Rip and Test & Copy
  inspection of embedded bytes.
- **Owner:** repo owner and WPF maintainer.
- **Status:** implemented and software-verified 2026-07-28. Provider identity,
  CTDB/Cover Art Archive discovery, MusicBrainz throttled disc/fuzzy lookup,
  release-group labeling, deterministic rank, shared proxy/bounds/redirect/pixel
  controls, the sortable selector, immutable job selection, and removal of the
  hidden processor fallback are in place. Apple artwork has no runtime reach.
  WPF tests pass 395/395, the warning gate is empty, the self-contained x64
  artifact contract passes, and live CAA/MusicBrainz/TheAudioDB probes return
  HTTP 200. Dark/light/high-contrast/DPI captures and real embedded-output
  inspection remain before release closure. TheAudioDB is available only as an
  off-by-default user-key provider pending a distribution-tier/default decision.

### R73. Local artwork import and optional provider credentials need explicit trust boundaries - bucket B, risk high

- **Area or slice:** WPF artwork importer, selector, app settings, secret
  persistence, and TheAudioDB provider.
- **Why it matters:** a retained local path can change between preview and Rip;
  an unbounded image can exhaust memory; a non-front image can become an automatic
  choice; and collecting an account password would violate TheAudioDB's documented
  API-key contract.
- **Evidence found:** the first selector slice accepts network JPEG/PNG only,
  shows no local drop target, retains only front Cover Art Archive results, and
  has no optional-provider credential setting. The existing DPAPI helper uses
  proxy-specific entropy. Official TheAudioDB documentation describes V1 path
  keys and V2 header keys, not username/password authentication.
- **Confidence:** verified from source and current official provider documents.
- **Approval needed:** approved by the owner's request for autonomous
  implementation and protected user-supplied provider settings.
- **Smallest safe next step:** add purpose-separated secret protection, one-read
  bounded local image import, a release-scoped local override, and deterministic
  provider fixtures before enabling any live TheAudioDB request.
- **Verification plan:** local format/size/pixel/TOCTOU tests; override and frozen
  job tests; protected-key/redaction tests; provider parser, error, cancellation,
  rate, host, and response-bound tests; selector filter/sort/drop/theme tests;
  warning, publish, and real embedded-output gates.
- **Owner:** WPF maintainer.
- **Status:** implemented and software-verified 2026-07-28. Local JPEG/PNG/BMP
  import reads one regular file once under 30 MiB and 100-megapixel limits,
  applies quality-92 JPEG conversion and RIOT resizing, binds the override to the
  release generation, and clones job bytes. TheAudioDB accepts only a
  purpose-separated DPAPI-protected API key, stays off by default, labels source
  and match class, validates text fallback identity, host-binds requests, rate
  gates calls, and retries one bounded 429. Front/All filtering keeps non-front
  art out of automatic selection. WPF tests pass 395/395, zero warnings and the
  x64 artifact contract pass, live provider probes return HTTP 200, and the
  local anti-dark-code skill validates. Interactive capture and independent
  embedded-output inspection remain the release evidence gap.

### R74. Archival output defaults lag the intended profile - bucket A, risk low

- **Area or slice:** WPF startup defaults, advanced settings, and album-art settings.
- **Why it matters:** fresh and upgraded profiles currently omit TOC files and
  detailed CTDB evidence, perform only a primary art search, and cap art at 1000 px.
- **Evidence found:** `CUEConfigAdvanced` defaults these switches off/Primary,
  while `App.OnStartup` applies a one-time 1000 px migration.
- **Confidence:** verified.
- **Approval needed:** no; the owner explicitly selected the new defaults.
- **Smallest safe next step:** add one idempotent default migration to enable the
  two evidence options, select Extensive art search, and move only the prior
  1000 px default to 1500 px.
- **Verification plan:** first-run, upgraded-profile, and post-migration user-choice
  persistence tests.
- **Owner:** WPF maintainer.
- **Status:** implemented and software-verified 2026-07-28. Fresh WPF profiles
  use TOC, detailed CTDB evidence, Extensive artwork search, and a 1500 px art
  limit. The one-time migration moves only the former 1000 px owner default and
  never rewrites later choices. Extensive, Primary, and None now also govern
  the WPF artwork browser rather than only the processor path.

### R75. Curated AAC and Vorbis imports reject compatible executable names - bucket A, risk medium

- **Area or slice:** `EncoderCatalog` import, approval receipt, and Settings UI.
- **Why it matters:** official packages can present `qaac64.exe` or `oggenc2.exe`,
  but the exact-name importer currently rejects them before compatibility can be used.
- **Evidence found:** the catalog accepts only one `ExeName` per row; qaac releases
  and RareWares document the requested executable variants.
- **Confidence:** verified.
- **Approval needed:** no.
- **Smallest safe next step:** curate accepted aliases per encoder, preserve the
  selected executable name, and bind the existing hash/size receipt to that exact file.
- **Verification plan:** import, restart resolution, changed-byte refusal, wrong-name
  refusal, and UI filter tests for every alias.
- **Owner:** codec and WPF maintainer.
- **Status:** implemented and software-verified 2026-07-28. The catalog accepts
  `qaac.exe`/`qaac64.exe` and `oggenc.exe`/`oggenc2.exe`. Import preserves the
  selected basename and binds the existing SHA-256/length approval to those
  exact bytes. Alias import, catalog display, tamper refusal, and settings
  round-trip tests pass.

### R76. Verify history cannot surface durable per-CRC and cross-drive agreement - bucket B, risk high

- **Area or slice:** verify-history persistence, Test & Copy commit, Rip grid, and logs.
- **Why it matters:** named CRC values persist, but carried-forward fields make a
  naive history count overstate independent jobs and the UI cannot distinguish
  same-drive repetition from cross-drive corroboration.
- **Evidence found:** records have one current `Crc32` plus carried Test/Copy fields,
  no read-role id, no durable count, and a five-record retention cap.
- **Confidence:** verified.
- **Approval needed:** no; this is additive local evidence.
- **Smallest safe next step:** persist an explicit read role and aggregate each
  displayed CRC only when that role actually completed. Retain a non-identifying
  drive fingerprint set for distinct-drive counts and keep legacy records readable.
- **Verification plan:** legacy migration, role isolation, repeated same CRC,
  changed CRC reset, distinct-drive deduplication, Test & Copy two-role counting,
  grid colors, tooltips, and human log wording.
- **Owner:** accuracy and WPF maintainer.
- **Status:** implemented and software-verified 2026-07-28. Records now identify
  Test, Copy, and TestAndCopy contributions. Per-role match counts and hashed
  distinct-drive sets survive the five-record retention window. The Rip grid
  shows `xN`, matching and mismatching theme colors, cross-drive corroboration,
  and a detailed tooltip. The Test & Copy log includes the local per-track
  agreement counts.

### R77. Redundant multi-disc metadata can outrank the physical single-disc release - bucket A, risk medium

- **Area or slice:** release candidate scoring and naming context.
- **Why it matters:** a generic two-disc MusicBrainz candidate for the Kenny G disc
  produced a false `[2-CD Set]/Disc 1 - Disc 1` folder even though an exact one-disc
  release with the same barcode and track list exists.
- **Evidence found:** both candidates receive the same source/completeness score;
  `List.Sort` then has no semantic tie-break. The retained cue proves the selected
  candidate asserted `TOTALDISCS 2` and generic `Disc 1`.
- **Confidence:** verified for the observed disc; inferred for other duplicate-release shapes.
- **Approval needed:** no.
- **Smallest safe next step:** add a narrow duplicate-candidate preference for the
  fewer-media release when album identity and track metadata agree and the larger
  set has no meaningful disc subtitle. Keep the alternate in the release selector.
- **Verification plan:** Kenny G fixture, genuine uniquely subtitled box set, stable
  tie order, and existing naming tests.
- **Owner:** metadata and naming maintainer.
- **Status:** implemented and software-verified 2026-07-28. An otherwise
  identical generic multi-disc candidate loses a narrow tie-break only when a
  single-disc candidate has the same artist, album, year, barcode, and every
  track title and artist. Named box discs and barcode differences remain
  untouched. A live reread of the Kenny G disc remains the final metadata
  service check.

### R78. Light mode swaps structural brushes but custom controls retain dark constants - bucket A, risk medium

- **Area or slice:** WPF theme service, shared templates, and drawing controls.
- **Why it matters:** screenshots show dark switch hardware, shadows, card fills,
  and light-on-light custom-control text after the structural palette changes.
- **Evidence found:** `ThemeService` owns separate structural palettes, but shared
  templates and `CodecScope`/other drawing controls contain dark-only color constants.
- **Confidence:** verified.
- **Approval needed:** no.
- **Smallest safe next step:** expand the central palette with control tokens and
  resolve drawing-control ink, muted, line, and surface colors from live resources.
- **Verification plan:** palette completeness, no dark-only structural constants
  outside intentional media illustrations, light/dark render samples, and contrast checks.
- **Owner:** WPF maintainer.
- **Status:** implemented and software-verified 2026-07-28. One central dark
  and light palette now owns switch hardware, borders, shadows, and drawing
  control surfaces/text. Codec, conversion, VU, reread, repair, and disc-layer
  drawings resolve live theme resources. Palette type/parity tests and XAML
  compilation pass. Interactive light/dark captures remain before release
  closure.

### R79. WPF hides the engine's single-FLAC embedded-CUE output - bucket B, risk high

- **Area or slice:** Rip output contract, settings persistence, Test & Copy, CTDB repair,
  and final-output verification.
- **Why it matters:** the engine supports `SingleFileWithCUE`, but WPF hardcodes
  `GapsAppended`, leaving users no preservation-image layout.
- **Evidence found:** `RipService.Run` assigns `CUEStyle.GapsAppended`; processor
  integration tests already cover final decode proof for `SingleFileWithCUE`.
- **Confidence:** verified.
- **Approval needed:** no for exposing one existing output style.
- **Smallest safe next step:** add persisted Tracks and Image + embedded CUE choices,
  freeze the selection into each job, and keep Tracks as the compatibility default.
  Defer Both until one transaction can bind both output sets without duplicate reads
  or ambiguous repair ownership.
- **Verification plan:** Rip and Test & Copy style propagation, one-file count,
  embedded CUE tags, optional external cue, final decode proof, repair source
  selection, persistence, and responsive UI.
- **Owner:** rip pipeline and WPF maintainer.
- **Status:** implemented and software-verified 2026-07-28. Tracks remains the
  default. Rip and Test & Copy snapshot a persisted Image + embedded CUE choice
  and map it to `SingleFileWithCUE`; verify-only runs remain `GapsAppended`.
  Existing processor integration covers embedded-CUE final decode proof. A
  live optical image rip and repair-source check remain before release closure.

## Ordering

The post-restart assurance batch is active. Remaining work is ordered by evidence
dependency:

1. Finish R72's interactive dark/light/responsive capture and real
   automatic/manual embedded-output proof. The core path does not wait on optional
   Apple or TheAudioDB policy decisions.
2. Finish and adversarially review any open R44-R49 follow-ups.
3. Refresh the classic receipt after the source commit and retain its exact
   AnyCPU/x64/Win32/TTA/MSI evidence, collection hashes, notices, and SBOM.
4. Run R59/R66/R69/R71's final-source K: Test & Copy lane. The CTDB repair half of
   R68/R71 is complete; retain the
   passing H: cache-defeat, simultaneous-drive, WMA,
   FLACCL, CTDB-repair, Icecast, and actionlint checks in the release matrix.
5. Prove R54's simultaneous H:/K: operation, same-drive denial, independent Stop,
   and crash release without shared-state collisions.
6. Run the pinned hosted workflows and compare them with the local receipts.
7. Choose and implement a publisher signing/attestation identity and policy.
8. Continue R5/R8/R12/R13 modernization and lock-file rollout.
9. Capture R78 in light and dark mode, rerun the Kenny G lookup for R77, and
   perform one live FLAC image rip for R79.

## Holes / external boundaries

- CTDB TLS (R4) is out-of-repo (server operator).
- CI depends on GitHub-hosted Windows image + VS Enterprise devenv path.
- Signing identity/policy is an owner decision; hashes establish byte identity, not
  publisher identity.
- Remaining vendored provenance is a supply-chain surface even though shipped bytes,
  immutable gitlinks, patches, and staged source manifests are hash-bound and
  inventoried.
- MusicBrainz/gnudb/AccurateRip/CTDB servers are external; their behavior can change independent of this repo.
- Apple artwork embedding lacks a documented Search API permission for this use.
  TheAudioDB remains optional until a production account and attribution placement
  are selected. Cover Art Archive images remain copyrighted and are used at the
  user's risk.
- The local classic build, frozen receipt, exact collection, and MSI matrix pass, but
  hosted image parity remains. External FLAC/TAK pairs, Icecast
  HTTPS/certificate/Mono, cross-vendor OpenCL, and deliberate optical failure injection
  remain explicit unobserved states, not inferred passes.

## Changelog

- 2026-07-02 - backlog created from the first full anti-dark-code rollout (comment loop S1-S13, logging audit, adversarial, scenario passes) and the user's decisions D1-D7.
- 2026-07-26 - added R19-R43 from the modern WPF, codec, security, CI, release, and
  scenario-stress audit. User approved autonomous remediation, including protected areas.
- 2026-07-26 - closed the locally actionable R19-R31 work, partially closed R32,
  refreshed earlier R2/R3/R9/R15 statuses, and replaced implementation ordering with
  the remaining hosted/hardware/external evidence queue.
- 2026-07-27 - closed R51 with immutable vendor staging, R52 with filename/tag/art
  preservation, and R53 with first-use calibration, monotonic cache defeat,
  offset-sized overread, and named CRC evidence. The exact classic release then
  rebuilt, receipted, and published all three configurations locally.
- 2026-07-27 - added R54 after the two-drive hardware run exposed the difference
  between dynamic drive selection and safe concurrent job ownership.
- 2026-07-27 - implemented R55's damaged-media failure classification and R56's
  responsive Rip layout. Deterministic gates pass; the damaged K: rerun and
  final-source presentation captures remain external evidence.
- 2026-07-27 - added R57 after the damaged K: rerun reproduced the previously
  overclaimed adaptive-speed boundary at 28 seconds. The retry is limited to the
  exact transition-bound `IllegalRequest 24/00` state.
- 2026-07-27 - corrected R58 after the exact stack proved the displayed 16-sector
  `24/00` was a nested pinpoint failure following a parent medium error. Two
  bounded reads proved the same address and command shape succeed outside that
  intermittent state.
- 2026-07-27 - added R59 when the next K: run proved the same exact child failure
  was also reachable through rejected-batch decomposition. Added classifier-route
  coverage rather than widening R58's medium-parent claim.
- 2026-07-27 - closed R60 by making source-revision metadata target-local to
  all five net20 resource-bearing projects, removing a proven-dead CD-Text field,
  and pruning its warning fingerprint. The full-MSBuild resource lane is now free
  of AL1053.
- 2026-07-27 - closed R61 and R62 by separating dead managed paths from native
  and public compatibility contracts, making WPF null boundaries explicit, and
  replacing the 34-fingerprint modern warning allowance with an empty baseline.
- 2026-07-27 - added R63 after the full classic solution rebuild exposed 19
  distinct managed warnings outside the modern gate.
- 2026-07-27 - closed R63 with zero managed warnings across the classic
  Any CPU, x64, and Win32 rebuilds, an RTX 3060 FLACCL verification run, and
  working net8 command-line dependency closures.
- 2026-07-27 - added R64 after the checked native warning allowance exposed a
  64-bit missing-frame gap narrowed before its repair cap.
- 2026-07-27 - added R65 after the native verification lane exposed a dead
  `AllRules.ruleset` reference under Core MSBuild.
- 2026-07-27 - proved simultaneous H:/K: jobs in isolated windows, added R69 from
  K:'s exact late cache-defeat command-shape rejection, and added R70 for portable
  human-facing album sidecar names with legacy repair compatibility.
- 2026-07-28 - added R72 after the multi-provider artwork design traced lost CTDB
  release IDs, divergent WPF/processor selection paths, missing network/image
  bounds, and Apple Search API terms that do not document file embedding.
- 2026-07-28 - added R73 after the artwork adversarial pass defined one-read
  bounded local JPEG/PNG/BMP import, release-scoped override lifetime,
  non-front automatic-selection exclusion, provider metadata enrichment, and a
  DPAPI-protected off-by-default TheAudioDB API-key boundary.
- 2026-07-28 - closed the software portions of R74-R79 with one-time archival
  defaults, artwork search-mode enforcement, curated encoder aliases,
  role-aware CRC agreement, narrow single-disc duplicate preference, live
  theme tokens, and an explicit image-with-embedded-CUE rip layout. The WPF
  suite passes 414/414, the warning budget is empty, and the self-contained x64
  artifact contract passes.
