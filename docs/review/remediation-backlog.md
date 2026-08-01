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

### R8. CUEControls resgen under dotnet build (decision D7) - DONE 2026-07-29

- **Done:** repo-root `Directory.Build.targets` (Core-MSBuild-gated) makes all SDK-style net47 first-party projects build under `dotnet build`; zero impact on the shipping devenv/CI build.
- **Done in R12/R88:** the reachable FLACCL plugin and its command host are now
  SDK-style net47 projects that restore and build consistently under both Core
  and full MSBuild.
- **Done in R12/R90:** `CUETools.eac3ui` / BluTools is the first classic-GUI
  pilot. Its SDK-style net47 project builds with both MSBuild runtimes while
  preserving its API, fields, methods, PE flags, config, image resources, and
  live WPF startup behavior.
- **Done in R12/R91:** CUERipper and its old ProgressODoom control dependency
  are SDK-style net47. Their managed and resource contracts are preserved, and
  old/new CUERipper builds create the same live `CUERipper 2.2.6` window set.
- **Done in R12/R92:** CUEPlayer is SDK-style net47. Its managed, PE, config,
  and decoded-image contracts are preserved, and old/new builds create the same
  live `CUEPlayer 2.2.6` window set.
- **Done in R12/R93:** classic CUETools is SDK-style net47. Its managed, PE,
  config, main/localized resource, and live-window contracts are preserved.
  All first-party classic GUIs now build through Core and full MSBuild.
  The `CLParity` reachability contradiction was closed by R89: the disabled,
  unconsumed, unshipped, non-building experiment was retired. GUI conversions
  need runtime verification; they are not blind headless changes.

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
- **First project slice (R88, 2026-07-29):** converted the reachable net47
  FLACCL plugin and command host to SDK projects without changing their source,
  assembly versions, plugin/output locations, localized resources, or OpenCL
  kernel. The conversion exposed and now explicitly preserves the host's
  historical 32-bit-preferred launch contract; a 64-bit host failed the
  qualified NVIDIA path with `OUT_OF_RESOURCES`, while the preserved 32-bit host
  passed modes 0-8, verify on/off identity, two CPU workers, 24-bit input, and
  the exact 4096-sample boundary.

### R13. Codec version refresh + FlaCuda retirement + add missing codecs (user request 2026-07-02) - bucket B, large

- **Scope (user):** upgrade all codecs to their latest versions/builds, add any missing/wanted ones, and note that FlaCuda has effectively been superseded by FLACCL (OpenCL), which confirms the CUDA path is a dead ancestor rather than a parallel feature.
- **What this touches:**
 - ThirdParty submodules and their local patches: `flac` (libFLAC 1.5.0, current upstream), `WavPack` (5.9.0, current upstream), MAC_SDK (13.20, current upstream, with an adapted CUETools `IAPEIO` wrapper), taglib-sharp (the current 2.3.0.0 release with local changes), `libmp3lame` (3.100 vs the July 2026 upstream 4.0 release), and ffmpeg (standalone/unshipped 7.1.1 path vs upstream 8.1.2). Each bump means re-checking local patches, ABI, packaging, and audio behavior.
 - Managed wrappers in S6/S7 that must match new native ABIs (P/Invoke signatures, struct layouts).
 - FlaCuda (`CUETools.Codecs.FlaCuda`, `CUETools.FlaCudaExe`): DELETED 2026-07-23 (decision D5).
   Confirmed dead first: absent from the sln, referenced by no csproj/cs/sln outside its own two
   dirs, and superseded by FLACCL (OpenCL, live in the sln). 12 tracked files removed via git rm
   (recoverable from history). FLACCL's corrected exact-length verifier now builds and
   runs on an RTX 3060 across OpenCL modes 0-8, CPU workers, 24-bit input, and an exact
   frame boundary. Cross-vendor coverage and managed-SIMD modernization (idea 14)
   remain future work.
 - Requested command-line codecs: xHE-AAC, OptimFROG, Musepack, Ogg Vorbis,
   Opus, qaac, and TAK. Support and bundling are separate decisions because
   executable contracts, patents, redistribution terms, and dependencies differ.
- **TTA build evidence:** redirected x64 and Win32 C++/CLI builds pass. Runtime
  workers encode 16-bit stereo and 24-bit six-channel PCM, reproduce every PCM byte
  through both the managed decoder and ffmpeg, and produce identical x64/Win32
  bitstreams. The wrapper independently verifies finalized output before publication.
  The tests also found and fixed `ttalib`'s short-file final-frame length bug.
- **Why it needs care:** codec upgrades are behavior-affecting (bit-exactness must be preserved; the golden-corpus tests in idea 3 should exist first). Approval-gated where they touch release output.
- **Next step:** keep LAME 4 and the unshipped FFmpeg refresh as separate
  behavior-changing projects. General OptimFROG input decoding remains outside
  the current receipt-bound command-decoder trust model.
- **Confidence:** verified
- **Status through 2026-07-29:** reachability and verification claims are refreshed in
  `docs/review/codec-audit.md`, and the upstream version table is refreshed in
  `codec-refresh-scope.md`. libFLAC and taglib-sharp match current releases.
  LAME has known version drift; the unshipped FFmpeg
  path is also behind. Upgrades remain per-codec integration work, not binary
  swaps. Monkey's Audio 13.20 is upgraded and verified on Win32 and x64. FlaCuda
  is deleted. FLACCL has real RTX 3060/OpenCL verification evidence.
  WavPack 5.9.0 is current: both architectures rebuild without warnings and the
  focused lifecycle plus real round-trip gate passes 2/2. The WPF catalog now
  registers Musepack, TAK, Vorbis, Opus, qaac, exhale/xHE-AAC, and OptimFROG
  with implementation selection, archival defaults, rich help, compatible
  executable aliases, and hash-bound user-import precedence. The WPF package
  includes only the redistributable, provenance-complete command encoders:
  CUETools' deterministic opus-tools 0.2/libopus 1.6.1 source build,
  RareWares `oggenc2.exe`, and CUETools' deterministic Musepack r495 source
  build. Release validation and runtime resolution enforce the executable and
  packaged-source hashes. Real stdin encodes passed for the exact packaged
  binaries; focused external-encoder trust/contract tests pass 22/22. exhale
  1.2.2 and OptimFROG 5.100 were built
  or exercised against their real CLIs, but are import-only because exhale
  grants no patent rights and OptimFROG redistribution requires author
  notification. qaac remains import-only because its Apple runtime is not
  redistributable with this product; TAK remains proprietary/import-only.

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
  `rip.verify`. A final-source isolated rerun later crossed the same Copy phase and
  passed, closing the observed same-drive agreement path.

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
  final hosted classic run `30518472651` and release run `30518479906` agree
  with that evidence. The downloaded classic payload passed its exact 97-file
  contract, SHA-256 manifest, provenance, and SPDX closure.

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
- **Status:** partially closed through 2026-07-29. All four actions workflows use immutable
  action SHAs; release inputs have native source/build/hash inventory; clean WPF
  artifacts receive a SHA-256 manifest, build receipt, contract snapshot, CycloneDX
  SBOM, and SPDX file inventory. The Microsoft SPDX result must be read as a file
  inventory rather than a complete dependency graph. Provenance now records
  byte-identical RareWares LAME archives, official signed/runtime-tested RARLAB
  UnRAR 7.23 plus the import-era 6.11 evidence, and the official TTA
  archive/import delta. The local frozen classic receipt and exact collection
  pass, dirty vendor staging is eliminated, and all 13 first-party
  `PackageReference` projects now commit their complete dependency closures.
  `CI=true` enables locked restore; a checked inventory fails when a first-party
  package project or lock file is added, removed, or drifts. Solution, WPF, and
  ripper-test locked restores pass without touching vendor submodules. Hosted
  parity, the signing policy, and unsigned-tag refusal now pass. Remaining is
  the external public-trust signing identity. The remaining historical
  provenance limits are now explicit retain decisions rather than actionable
  searches: HDCD's exact source/revision/recipe was not published with the
  surviving DLL ABI; RareWares' archives contain no LAME source identifier,
  flags, or build notes; and a checksum captured in 2009 cannot be created
  retroactively. Current bytes, archives where available, hashes, import
  history, build evidence, and replacement gates are recorded in
  `eng/release/native-dependencies.json`.

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
  converted CUEPlayer also builds in final hosted classic run `30518472651`.

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
- **Status:** fixed, focused-test verified, and live dual-drive verified through
  2026-07-29. A running
  Rip page offers only other attached drives and opens the selected one in a
  separately titled CUETools process. The job lease holds both drive letter and
  Windows device number across calibration and every child read; nesting is
  thread-owner-bound, while other threads/processes fail before touching hardware.
  Secondary windows load but never save shared settings, settings publication is
  cross-process serialized, command lines contain only role and drive letter, and
  log names include process identity plus a nonce. Album publication and gzip
  evidence stores retain their existing cross-process transactions. The new lease
  launch, settings-writer, independent-drive ownership, same-drive denial,
  ordinary release, and forced worker-crash release contracts pass 7/7 focused
  tests. The live H:/K: run held simultaneous Test & Copy jobs in independently
  titled processes; H: completed and K:'s separate cache-defeat failure did not
  alter H:'s CRCs, status, completion output, or publication. The complete WPF
  suite passes 439/439 and the clean self-contained artifact remains
  release-gated.
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
  remain clean. Superseding release run `30518479906` retains a clean
  source-bound classic receipt and exact downloaded artifact closure.

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
  read only after the full byte count succeeds. Scope the bounded settle/retry to
  each exact address/shape, calculate the full byte count without overflow, accept
  only exact transition `24/00` for the post-eviction retry, and make the
  independent-read gate consume the same positive flush parser as `Run`.
- **Verification plan:** classifier and SCSI suites, net8/net47/net20 builds, then
  a K: deep-recovery probe at the damaged window with the measured 786,432-byte
  cache volume and the full Test & Copy repeat.
- **Owner:** repo owner.
- **Status:** implemented and end-to-end hardware-verified 2026-07-29. The
  fallback is limited to exact
  `DeviceFailed/IllegalRequest/24/00`, preserves the requested sector count and
  in-program bounds, stops after one-sector commands, and reports both transient
  retries and chunk fallbacks. The 2026-07-29 K: repeat reached 92 percent of
  Copy, then rejected all three regions at 16/8/4/2/1 sectors. The first rejected
  command consumed the retry budget for the entire eviction, leaving fourteen
  distinct address/shape commands without their own bounded retry. Ripper tests
  passed 22/22 before this narrower route defect was found. The adversarial pass
  also found unchecked maximum-size arithmetic, a broad post-eviction exception
  retry, and a malformed-current-calibration fail-open; all are included in the
  same complete-or-fail correction before another hardware run. The
  source-bound rerun then proved all fifteen commands received their local
  retry, but K: rejected all fifteen with exact `24/00` and became unavailable
  to a new raw handle after release. The next bounded correction is one
  `START UNIT` on the still-open handle only after full exact-invalid-field
  exhaustion, followed by readiness and the complete eviction again. The next
  Copy run exercised that path: `START UNIT` succeeded, but the immediate
  readiness CDB returned exact `24/00`; CUETools failed closed after one wake.
  A later source-bound Test reached 92 percent after 946 seconds and disproved
  the delay-only assumption: both the settled readiness query and its one exact
  retry returned `24/00`, after which Windows again reported no loaded media.
  Because readiness is advisory while the complete unrelated-region read is the
  actual cache-independence proof, the next bounded build attempts that proof
  once after the two exact indeterminate readiness results. All other readiness
  failures and any repeated eviction exhaustion remain fatal. Ripper tests pass
  28/28, WPF tests pass 430/430, net47/net20 full-MSBuild lanes pass, the warning
  gate is empty, and the production artifact contract passes. Commit `5fa2c65`
  then completed an uninterrupted 2,275-second K: Test & Copy, crossed the
  former 92-percent Copy boundary, verified the final encoded PCM, and
  published a verified six-sector CTDB repair. The successful run recorded zero
  wake, readiness, command-retry, and chunk-fallback counters. The observed
  end-to-end blocker is cleared; exact hardware activation of the intermittent
  wake branch remains tracked as an unknown.

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
- **Status:** fixed and live-verified 2026-07-29. New outputs use
  `<artist> - <album> (<year>).cue/.log/.accurip` and `<stem> - Test & Copy.log`,
  with optional disc identity. Legacy `album.*` remains supported. Multiple cues
  fail repair discovery closed. The live image rip published portable album-named
  cue, log, AccurateRip/CTDB report, and TOC sidecars beside the single FLAC.

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
- **Status:** implemented and partially live-verified through 2026-07-29. Provider identity,
  CTDB/Cover Art Archive discovery, MusicBrainz throttled disc/fuzzy lookup,
  release-group labeling, deterministic rank, shared proxy/bounds/redirect/pixel
  controls, the sortable selector, immutable job selection, and removal of the
  hidden processor fallback are in place. Apple artwork has no runtime reach.
  The first live dark capture exposed a default-white DataGrid body; the browser
  now uses only dynamic palette resources and passed dark/light 1040x700 captures
  at 96 DPI. The live image FLAC contains exactly one selected cover byte-for-byte
  equal to the published `folder.jpg`. High-contrast and 150/200 percent DPI
  captures remain. TheAudioDB is available only as an off-by-default user-key
  provider pending a distribution-tier/default decision.

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
- **Status:** implemented and partially live-verified through 2026-07-29. Local JPEG/PNG/BMP
  import reads one regular file once under 30 MiB and 100-megapixel limits,
  applies quality-92 JPEG conversion and RIOT resizing, binds the override to the
  release generation, and clones job bytes. TheAudioDB accepts only a
  purpose-separated DPAPI-protected API key, stays off by default, labels source
  and match class, validates text fallback identity, host-binds requests, rate
  gates calls, and retries one bounded 429. Front/All filtering keeps non-front
  art out of automatic selection. Live normal-theme browser captures and
  independent selected-cover embedding inspection now pass. High-contrast and
  150/200 percent DPI browser captures remain the release evidence gap.

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
- **Evidence found:** both candidates initially received the same
  source/completeness score. The first strict tie-break also compared provider
  track-artist credits, so spelling differences such as featured-artist ordering
  prevented the physical duplicate from being recognized. The retained cue proves
  the selected candidate asserted `TOTALDISCS 2` and generic `Disc 1`.
- **Confidence:** verified for the observed disc; inferred for other duplicate-release shapes.
- **Approval needed:** no.
- **Smallest safe next step:** add a narrow duplicate-candidate preference for the
  fewer-media release when album identity and track metadata agree and the larger
  set has no meaningful disc subtitle. Keep the alternate in the release selector.
- **Verification plan:** Kenny G fixture, genuine uniquely subtitled box set, stable
  tie order, and existing naming tests.
- **Owner:** metadata and naming maintainer.
- **Status:** fixed and live-verified 2026-07-29. The duplicate check now binds
  album identity, year, barcode, track count, and normalized track titles, but
  does not treat provider credit spelling as physical-disc identity. Named box
  discs, different barcodes, and different track lists remain untouched. The
  live H: reread ranked the one-disc MusicBrainz candidate 145 and the generic
  two-disc candidate 142. The committed output used the single-disc folder with
  no set descriptor or disc subfolder.

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
- **Status:** fixed and live-verified 2026-07-29. One central dark
  and light palette now owns switch hardware, borders, shadows, and drawing
  control surfaces/text. Codec, conversion, VU, reread, repair, and disc-layer
  drawings resolve live theme resources. Palette type/parity tests and XAML
  compilation pass. Interactive 1590x880 and 1180x740 captures at the host's
  actual 96 DPI passed in both light and dark modes. Compact mode scrolls the
  wide evidence table while every job control remains reachable. The artwork
  browser also passed post-fix dark/light 1040x700 captures after its default
  DataGrid cell surface was moved onto the central palette.

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
- **Status:** fixed and live-verified 2026-07-29. Tracks remains the
  default. Rip and Test & Copy snapshot a persisted Image + embedded CUE choice
  and map it to `SingleFileWithCUE`; verify-only runs remain `GapsAppended`.
  A live H: Paranoid rip published one 330,738,757-byte FLAC for ten tracks.
  The embedded and external cue sheets each contain ten tracks, the managed
  FLAC decoder reached all 146,313,216 declared samples with CRC checking, and
  `rip.verify` records post-metadata decode-and-compare. Repair discovery selects
  the authoritative external cue, which names the single image. The FLAC contains
  exactly one cover whose 100,222 bytes equal the published `folder.jpg`.

### R80. The live CD model reads as a dark platter instead of optical media - bucket A, risk medium

- **Area or slice:** WPF live and fallback disc controls, theme palette, and
  optical-read presentation.
- **Why it matters:** the current 3D surface is one dark annulus with a broad
  rainbow wedge. It loses the clear hub, reflective data layer, clear outer rim,
  and edge thickness that make a compact disc recognizable, especially in light
  mode. The visible beam also extends above the disc even though a CD is read
  through its clear substrate from below.
- **Evidence found:** inspected 1590x880 dark and light release captures and traced
  `DiscModel3D` materials, geometry, laser placement, equal-area read radius, and
  damage-camera inputs.
- **Confidence:** verified.
- **Approval needed:** no; the owner requested the visual improvement and required
  the bad-sector autozoom to remain.
- **Smallest safe next step:** layer the physical regions with theme-owned
  materials, use a bounded optical texture, place the pickup beneath the disc,
  and make the software fallback theme-aware. Do not change the progress,
  re-read, unreadable, or camera-state bindings.
- **Verification plan:** radius and damage-camera contracts, palette parity,
  offscreen light/dark renders, full WPF suite, zero-warning gate, clean publish,
  and inspected live captures.
- **Owner:** WPF maintainer.
- **Status:** fixed and visually verified 2026-07-29. The live model separates
  the physical hole, hub, clamp ring, program area, clear rim, back, and edge.
  Its pickup sits below the substrate, the data texture is one representative
  spiral, and curved spectral highlights replace the broad rainbow wedge.
  Light/dark 1180x740 live captures pass at 96 DPI. The ten-frame offscreen
  matrix covers idle, reading, re-reading, unreadable, and tier-zero fallback
  states in both themes. Radius, CLV, live-binding, and damage-autozoom tests
  pass. A 1,000-frame hot-loop test stays below 128 KiB after warmup. The full
  WPF suite passes 423/423 and the warning budget is empty.

### R81. Live damaged-sector frame pacing lacks a source-bound receipt - bucket A, risk medium

- **Area or slice:** WPF `CompositionTarget.Rendering`, the R80 CD model,
  real optical re-read state, and release evidence.
- **Why it matters:** deterministic renders prove appearance and state mechanics,
  but not whether the live UI remains responsive while the optical worker is
  recovering bad sectors. A renderer can pass snapshots while stalling or
  allocating during the real workload.
- **Evidence found:** R80 has dark/light live captures, a ten-frame state matrix,
  and a post-warmup allocation test. `DiscModel3D.OnTick` is the single animation
  seam. The application diagnostic log independently timestamps real
  `rip.reread` and `rip.recovery` events. WPR failed with `0xc5585011`, and
  PresentMon 2.5.1 exited 6 because this non-administrative account lacks the
  system performance policy.
- **Confidence:** verified.
- **Approval needed:** no. The user explicitly requested the live benchmark on
  the damaged disc in K:.
- **Recommended next pass:** pass 11 bounded remediation and verification.
- **Smallest safe next step:** add an environment-gated, numeric-only sampler at
  `DiscModel3D.OnTick`, backed by fixed histograms and state-transition receipts,
  then run K: in Paranoid Test & Copy.
- **Verification plan:** require zero post-warmup sampling allocations; focused
  state and receipt tests; full WPF, warning, and publish gates; a real K:
  run containing at least one independently logged optical re-read; and separate
  normal-read versus re-read frame percentiles.
- **Owner:** CUETools WPF maintainers.
- **Status:** fixed and hardware-measured 2026-07-29. Commit `31d839b`
  recorded 87,832 normal-read frames over 1,249.4 seconds and 49,984 re-read
  frames over 714.9 seconds at tier 2. Both states held 14.3/14.7/15.0 ms
  p50/p95/p99. Re-reading had three frames above 33.33 ms and a 40.1 ms
  maximum. The model callback averaged 0.0227 ms and peaked at 1.1332 ms during
  re-reading. The independent optical log recorded 385 recovery passes.

### R82. Early Test & Copy failures omit their terminal diagnostic - bucket A, risk medium

- **Area or slice:** `RipService.RunTestAndCopy`, structural diagnostics, and
  unattended optical evidence.
- **Why it matters:** Test & Copy can correctly return an early Test or Copy
  phase failure to the UI while never logging `test&copy failed`. Monitoring
  cannot distinguish a completed failure from a hung operation and can leave
  evidence jobs waiting indefinitely.
- **Evidence found:** the R81 K: run showed the cache-defeat failure in the UI
  and the nested `RipService.Run` error in the diagnostic log. The outer method
  returned directly from `!testResult.Ok` and `!copyResult.Ok`, bypassing both
  terminal catch blocks.
- **Confidence:** verified.
- **Approval needed:** no.
- **Recommended next pass:** pass 11 bounded remediation.
- **Smallest safe next step:** route every early calibration, Test, Copy, and
  stopped confirming-read return through one phase-bound diagnostic helper.
- **Verification plan:** assert failed/stopped wording, phase identity, original
  error preservation, and sink-failure isolation; run the full WPF, warning, and
  publish gates.
- **Owner:** CUETools WPF maintainers.
- **Status:** fixed 2026-07-29. Early failures emit one numeric
  `test&copy failed|stopped ... phase=calibration|test|copy|confirm` line.
  Diagnostic exceptions remain ancillary and cannot change the returned error.

### R83. Encoded jobs can snapshot artwork before discovery finishes - bucket A, risk high

- **Area or slice:** `RipViewModel` artwork discovery and the Rip/Test & Copy
  job-input snapshot.
- **Why it matters:** with embedding enabled, a user can start an encoded job
  while release-bound artwork is still loading. The job freezes a null cover,
  then the UI shows the selected cover moments later. The published audio and
  repaired sibling omit the cover without explaining the mismatch.
- **Evidence found:** the final K: run started Test & Copy 1.089 seconds after
  disc identification. Artwork discovery completed 0.711 seconds after the job
  started. The source and repaired FLAC sets contain zero pictures even though
  the UI later showed the selected cover and embedding remained enabled.
- **Confidence:** verified from the structural log, output inspection, and
  source trace.
- **Approval needed:** no; the user requested release-ready embedding and
  immutable job artwork.
- **Recommended next pass:** pass 11 bounded remediation.
- **Smallest safe next step:** keep only encoded-job commands disabled while
  release-bound artwork is loading. Leave Verify available, make the early
  execution guard enforce the same policy, and requery commands when artwork
  loading changes.
- **Verification plan:** focused policy test, full WPF suite, warning and
  production publish gates, then a fast real encoded-output check started as
  soon as the disc is identified.
- **Owner:** CUETools WPF maintainers.
- **Status:** fixed and live-verified 2026-07-29. Rip and Test & Copy use
  one shared encoded-job gate, the private execution paths enforce the same
  condition, and artwork state changes requery both commands. Verify remains
  available. The focused regression passes, the full WPF suite passes 431/431,
  the warning gate emits zero warnings, and the production artifact contract
  passes 36 required files, 19 plugin registrations, and five native probes.
  A live H: transition trace showed Rip and Test & Copy disabled while artwork
  was searching and loading, Verify available throughout, and both encoded jobs
  enabled only after the cover became stable. The immediate Burst rip completed
  10 FLAC files with AccurateRip confidence 28 and CTDB confidence 241. Every
  FLAC contained exactly one 100,222-byte picture whose SHA-256 matched
  `folder.jpg`, and final output PCM verification passed after metadata.

### R84. Related open HDCD source is not a behavior-compatible replacement - bucket C, risk high

- **Area or slice:** retained `hdcd.dll` runtimes and the proposed
  `bp0/libhdcd` source-built replacement.
- **Why it matters:** replacing an unbuildable legacy binary with related open
  source looks like a supply-chain improvement, but an HDCD decoder can change
  detection, gain, packet accounting, statistics, and decoded PCM while still
  accepting the same input. A silent swap would change users' audio.
- **Evidence found:** the owner-provided `libhdcd-master` tree matches official
  v1.4 source at `c574f998` for all 42 files after line-ending normalization
  except `.gitignore`. It builds for x86 and x64 with current MSVC, but exposes
  a different API. A six-vector comparison found exact scaled PCM on the
  non-HDCD control, while every HDCD vector differed in packet accounting or
  decoded PCM. The combined vector matched only 2,604,083 of 2,646,016 scaled
  samples; the legacy false-negative/statistics-error case was detected as
  effectual HDCD with 200 packets by the modern library.
- **Confidence:** verified from official source identity, two-architecture
  builds, and the real upstream corpus.
- **Approval needed:** no for retention; yes only if product semantics later
  add a separately named modern decoding engine.
- **Recommended next pass:** preserve the legacy engine and its exact-hash
  runtime probe; treat modern libhdcd as a new behavior until compatibility is
  deliberately specified.
- **Smallest safe next step:** if replacement work resumes, commit a permanent
  corpus harness and adapter that covers detection, statistics, packet counts,
  gain, PCM, reset, and flush before any packaging change.
- **Verification plan:** require old-vs-new equality on the recorded corpus,
  non-HDCD controls, chunk-boundary permutations, and application-level HDCD
  metadata before changing the default.
- **Owner:** codec maintainers / product owner for any new-engine semantics.
- **Status:** investigated and bounded 2026-07-29. The source-built candidate
  is valid software but failed the compatibility gate, so the hash-bound legacy
  DLLs remain the honest default. Exact source/archive/build/corpus evidence is
  recorded in `eng/release/native-dependencies.json`.

### R85. Musepack's available binary lacked a corresponding-source proof - bucket A, risk high

- **Area or slice:** optional Musepack command encoder, release provenance, and
  external-encoder runtime precedence.
- **Why it matters:** shipping an owner-supplied executable merely because it
  runs would leave its source correspondence, build changes, license
  obligations, and future replacement policy unverifiable. The preserved r495
  source also contains a separately marked all-rights-reserved tag writer that
  must not be silently swept into an LGPL claim.
- **Evidence found:** the supplied executable reports 1.30.0 while the available
  source identifies 1.30.1. Debian preserves upstream r495 with a detailed
  copyright inventory. `common/tags.c` has a separate ambiguous notice, but the
  encoder, psychoacoustic, and required common sources are LGPL-2.1-or-later or
  BSD. Current MSVC exposed incorrect pointer declarations, CRT/math name
  collisions, and a real right-channel quantizer bug.
- **Confidence:** verified from source, licenses, current-compiler diagnostics,
  binary inspection, repeat builds, and real encode/decode runs.
- **Approval needed:** no; the user explicitly requested every safely
  redistributable codec with complete licensing and user-import precedence.
- **Recommended next pass:** retain the exact source/build/runtime gates and
  treat any Musepack source refresh as a codec-quality change.
- **Smallest safe next step:** none; this slice is closed.
- **Verification plan:** two independent clean builds must match the pinned
  executable SHA-256; stdin encoding must be deterministic and independently
  decodable; encoder-side tag arguments must remain unavailable; release
  preparation, notices, runtime trust, and artifact manifests must agree.
- **Owner:** codec and release maintainers.
- **Status:** fixed 2026-07-29. CUETools packages its reproducible 378,368-byte
  x64 r495 build, the complete upstream archive, reviewed patch, CMake recipe,
  build notes, and LGPL-2.1 notice. Two clean builds matched SHA-256
  `599771ff...`, repeated quality-7 stdin encodes matched, and both outputs
  decoded identically. The patch excludes `common/tags.c`, fixes the
  right-channel error-history defect, and gives the old declarations their
  actual types. Receipt-bound user imports remain higher precedence. The full
  WPF suite passes 439/439, the warning gate is empty, and the self-contained
  release contract passes all 44 required files.

### R86. Self-contained WPF publish rewrites committed NuGet locks - bucket A, risk medium

- **Area or slice:** `Publish-Wpf.ps1`, RID-specific restore, and clean-tree
  release evidence.
- **Why it matters:** a successful `win-x64` publish appended empty RID graphs
  to four committed `packages.lock.json` files. A later ordinary locked restore
  then rejected those files because the project no longer declared that RID.
  Release validation should not leave source changes or make the next canonical
  restore fail.
- **Evidence found:** the validated 44-path publish changed the CTDB, Codecs,
  Processor, and WPF locks. `dotnet restore --locked-mode` then failed NU1004
  on all four due to the transient `win-x64` graph. A canonical force-evaluated
  non-RID restore returned every lock byte-for-byte to `HEAD`.
- **Confidence:** verified by before/after hashes and NuGet's exact diagnostic.
- **Approval needed:** no; generated release work must not alter reviewed source
  inputs.
- **Recommended next pass:** keep committed locks as the canonical
  target-framework dependency closure and prevent the packaging-only RID
  restore from writing them.
- **Smallest safe next step:** none; this slice is closed.
- **Verification plan:** clean publish, 44-path artifact contract, unchanged
  hashes for all 13 committed lock files, then the normal lock-file gate.
- **Owner:** build and release maintainers.
- **Status:** fixed 2026-07-29. WPF publish redirects each project's
  packaging-only RID lock into its ignored intermediate directory and disables
  locked mode only for that staged restore; hosted/local dependency review
  remains enforced by the canonical lock lane. Release-safety tests pin the
  non-mutating invocation. The self-contained 44-path publish passed while all
  13 committed lock-file hashes remained unchanged.

### R87. Packaged Opus core is behind the current stable library - bucket B, risk medium

- **Area or slice:** packaged `opusenc.exe`, its libopus/libopusenc/libogg
  closure, and lossy-codec quality provenance.
- **Why it matters:** the latest official Windows `opus-tools` binary is still
  0.2 linked to libopus 1.3, while the official stable codec library is 1.6.1.
  Replacing it with an arbitrary owner binary would lose provenance; replacing
  it with a source build can change encoded bytes and quality even though the
  stream stays standards-compliant.
- **Evidence found:** the Opus project currently lists libopus 1.6.1, fixes
  since 1.6, libopusenc 0.3, and libogg 1.3.6, but still links Windows users to
  the 2018 opus-tools 0.2/libopus 1.3 bundle. The owner collection's
  `opus-main` snapshot has neither Git metadata nor a generated package-version
  identity.
- **Confidence:** verified from official release pages and local source/binary
  inspection.
- **Approval needed:** no; the user requested safe current codec builds.
- **Recommended next pass:** build only from official hash-pinned release
  archives, package every corresponding source/license/build input, and compare
  old/new decode, metadata, determinism, and representative signal behavior.
- **Smallest safe next step:** create a deterministic x64 static build recipe
  around opus-tools 0.2, libopusenc 0.3, libopus 1.6.1, and libogg 1.3.6.
- **Verification plan:** two clean builds with one executable hash; repeated
  stdin encodes; independent ffmpeg decode; duration/channel/rate/tag checks;
  old/new corpus comparison; release preparation/notices/trust/artifact gates;
  user-import precedence.
- **Owner:** codec and release maintainers.
- **Status:** fixed 2026-07-29. Four official release archives, the two
  warning-correctness patches, exact license texts, CMake recipe, and build
  notes now ship with the 665,088-byte x64 encoder. Two separately extracted
  builds produced SHA-256
  `c414aa0b6317aab4cc73ce659fd9527a42d51fa15e3ff975cd17a1502da2ddaa`.
  Three real WAVE-on-stdin encodes decoded to the exact expected
  duration/channel/rate shape with their requested tags. At 192 kbps, the
  codec-native weighted-error diagnostic was effectively unchanged on pink
  noise, improved on transients, and worsened on the synthetic tone vector.
  The archival default is therefore 256 kbps: old and current libopus produced
  byte-identical decoded PCM on all three 256-kbps vectors. Release preparation,
  notices, 22 focused trust/default tests, and the 52-path release contract
  pass. Receipt-bound user imports still take precedence.

### R88. FLACCL's old projects have split restore graphs and an implicit host architecture - bucket A, risk high

- **Area or slice:** `CUETools.Codecs.FLACCL`, `CUETools.FLACCL.cmd`, net47
  restore/build behavior, resource/kernel publication, and live OpenCL runtime.
- **Why it matters:** the old projects could not be restored once and then built
  consistently by Core and full MSBuild. Converting only the plugin also left
  the paired command host unable to exercise the result. An ordinary SDK
  conversion silently changed the executable from 32-bit preferred to 64-bit,
  which failed the qualified RTX 3060 path with OpenCL `OUT_OF_RESOURCES`.
- **Evidence found:** the old plugin required different RID/package graphs under
  the two MSBuild runtimes. The old command executable had PE32
  `32BITPREFERRED` flags despite its AnyCPU label. Source, kernel, public API,
  manifest-resource bytes, and satellite-resource bytes were unchanged across
  the project conversion; only the 64-bit host failed, and the packaged
  32-bit host passed with either plugin build.
- **Confidence:** high; binary flags, isolated host/plugin/OpenCL matrices, and
  live device runs separate project-shape behavior from hardware behavior.
- **Approval needed:** no; this preserves the shipping runtime contract while
  completing the user-authorized R12 slice.
- **Recommended next pass:** convert the remaining GUI projects one at a time
  with explicit PE/resource/UI contracts. R89 separately retired `CLParity`
  rather than treating its non-building project as a working optional path.
- **Smallest safe next step:** none; this slice is closed.
- **Verification plan:** locked restore; Core and full-MSBuild Release builds;
  public API/resource/kernel comparison; PE flag comparison; live modes 0-8,
  CPU-worker, 24-bit, exact-boundary, and verify-on/off checks.
- **Owner:** classic build and codec maintainers.
- **Status:** fixed 2026-07-29. Both projects are SDK-style net47. Core and full
  MSBuild complete with zero warnings. The plugin retains all 126 public IL
  declarations, its exact three manifest resource names and bytes, and the
  exact `flac.cl` SHA-256. The host explicitly retains 32-bit preference.
  The RTX 3060/OpenCL 3.0 matrix passed modes 0-8, two CPU workers, 24-bit
  input, the 4096-sample boundary, and byte-identical verify-on/off output.

### R89. CLParity was classified as current despite being disabled and non-building - bucket A, risk medium

- **Area or slice:** `CUETools.CLParity`, solution membership, GPU/parity
  reachability, and R8/R12 modernization scope.
- **Why it matters:** treating a disabled research experiment as a current
  optional path inflated release scope and suggested that making its project
  compile would restore a supported feature. Its settings/writer contract
  predates the current codec interface, so a mechanical SDK conversion would
  invent product behavior without a consumer or test oracle.
- **Evidence found:** 68 first-party project candidates and their C#/solution
  references were scanned. Only four files inside `CUETools.CLParity` plus the
  solution mentioned `CLParitySettings`, `CLParityWriter`, or the assembly.
  Its registration attribute was commented out, no project referenced it, no
  release collection included it, and its project referenced the absent
  `ThirdParty\OpenCLNet.dll`. `CLParitySettings` did not implement the current
  `IAudioEncoderSettings`, and the writer exposed the obsolete mutable
  `object Settings` shape.
- **Confidence:** high; source, project graph, solution, and release reachability
  all agree.
- **Approval needed:** no; dead-code removal D5 and autonomous R-item
  remediation are user-authorized.
- **Recommended next pass:** keep FLACCL as the only current GPU codec path. A
  future parity accelerator should begin from a current product requirement,
  interface, CPU oracle, and cross-device test matrix rather than reviving this
  tree by filename.
- **Smallest safe next step:** none; this slice is closed.
- **Verification plan:** remove all solution/project/release references, require
  a zero-match post-removal scan, run release safety and the canonical tests,
  and preserve recovery through Git history.
- **Owner:** codec and architecture maintainers.
- **Status:** fixed 2026-07-29. Removed the 35-file, 308,630-byte experiment
  and its solution configuration. The deleted set includes the old OpenCL
  wrapper/kernel plus its paper, MATLAB models, and native prototypes; all
  remain recoverable from the parent commit. No shipped artifact was removed.

### R90. BluTools' old WPF project blocks consistent Core/full-MSBuild builds - bucket A, risk medium

- **Area or slice:** `CUETools.eac3ui` / BluTools project shape, WPF resources,
  generated settings, solution membership, and the R8/R12 GUI pilot.
- **Why it matters:** the old project participates in the split legacy restore
  graph, while a mechanical WPF conversion can silently change assembly
  identity, executable architecture, generated configuration, embedded images,
  or compiled XAML behavior.
- **Evidence found:** the Release baseline was captured before conversion.
  Old and new binaries expose the same 33 public declarations, 44 fields, and
  59 method declarations; retain the same assembly identity and PE32/IL-only
  flags; and produce byte-identical generated configuration and 19 embedded
  image payloads. The WPF compiler rewrote only the `mainwindow.baml` encoding.
  Both baseline and converted executables constructed a live hidden `BluTools`
  window and remained healthy through the startup smoke.
- **Confidence:** high for build, startup, resource, and managed-contract
  preservation; broader interactive Blu-ray/eac3to workflows remain outside
  the automated smoke.
- **Approval needed:** no; this is the user-authorized one-at-a-time R12
  modernization.
- **Recommended next pass:** convert the remaining classic GUIs one at a time,
  starting from a captured binary/resource/runtime contract for each.
- **Smallest safe next step:** none; this pilot slice is closed.
- **Verification plan:** Core and full-MSBuild Release builds with zero
  warnings; declaration, field, method, identity, PE, config, and resource
  comparisons; old/new live-window startup; canonical WPF and release gates.
- **Owner:** classic desktop and build maintainers.
- **Status:** fixed 2026-07-29. BluTools is SDK-style net47 and builds under
  both MSBuild runtimes. All retained contracts above pass; the only resource
  payload change is compiler-generated BAML whose live startup is proven.

### R91. CUERipper and ProgressODoom retain old project and mixed-restore behavior - bucket A, risk high

- **Area or slice:** classic CUERipper, ProgressODoom controls, WinForms
  localization/resources, ClickOnce/manifest properties, Core/full-MSBuild
  restore ordering, and R8/R12.
- **Why it matters:** CUERipper could not consume one restored dependency graph
  under both MSBuild runtimes while ProgressODoom remained old-style. A
  mechanical conversion can also change executable architecture, localized
  satellites, designer images, settings/config probing, or the actual startup
  form. Core's binary-resource package is conditional at evaluation time but
  remains in `project.assets.json`; reusing that Core assets file in a later
  full build adds a non-shipping binding redirect unless full MSBuild performs
  its canonical restore first.
- **Evidence found:** the old packaged binaries were captured before conversion.
  CUERipper retains all 33 classes, 200 fields, 274 methods, and 179 public
  declarations; ProgressODoom retains 45 classes, 241 fields, 424 methods, and
  378 public declarations. Both retain PE32/IL-only architecture. ProgressODoom
  keeps its intentional nondeterministic `1.0.*` assembly version and exact
  file version. Its 26 icons and both CUERipper localization satellites are
  byte-identical. Of 511 main-form resource entries, 508 are byte-identical;
  the three newer-compiler serializations decode to the same 16 images with
  identical dimensions and pixel hashes. CUERipper's config is XML-equivalent,
  including the `plugins` probing path. Old and new executables each created
  13 top-level windows, including a responsive `CUERipper 2.2.6` main form.
- **Confidence:** high for build, managed shape, resource semantics,
  localization, architecture, config, and startup; full ripping workflows
  remain covered by the existing shared-engine/hardware evidence rather than
  this project-only smoke.
- **Approval needed:** no; this is the user-authorized one-at-a-time R12 work.
- **Recommended next pass:** convert CUETools or CUEPlayer only after capturing
  its own application, resource, configuration, and live-window contracts.
- **Smallest safe next step:** none; this slice is closed.
- **Verification plan:** separate Core and full restores followed by
  zero-warning Release builds; IL/config/PE/resource/satellite comparisons;
  decoded-image equality; old/new top-level-window smoke; canonical tests and
  release safety.
- **Owner:** classic desktop and build maintainers.
- **Status:** fixed 2026-07-29. CUERipper and ProgressODoom are SDK-style net47.
  Core and canonical full restore/build lanes pass with zero warnings. All
  retained contracts above pass, and no application source changed.

### R92. CUEPlayer retains an old project and unproven desktop-build contract - bucket A, risk high

- **Area or slice:** classic CUEPlayer, WinForms resources, generated settings,
  Icecast/playback dependencies, solution membership, and R8/R12.
- **Why it matters:** an SDK conversion can silently change executable
  architecture, assembly/config identity, designer resources, settings
  generation, or the startup form. CUEPlayer also sits beside sensitive
  credential and network-output code, so a project-only change must not alter
  application behavior.
- **Evidence found:** the Release executable and config were captured before
  conversion. Old and new builds retain 29 classes, 175 fields after
  normalizing compiler-generated RVA offsets, 236 methods, and 118 public
  declarations; the same `CUEPlayer, Version=2.2.6.0` identity; and PE32,
  IL-only, unsigned flags. The generated configurations are XML-equivalent.
  Six of eight manifest resources are byte-identical. The two newer-compiler
  serialization changes decode to the same five images with identical
  dimensions and pixel hashes. Old and new executables each create eight
  top-level windows, including a responsive `CUEPlayer 2.2.6` main form.
- **Confidence:** high for build, managed shape, resource semantics,
  architecture, config, and startup. Playback-device, real settings migration,
  and Icecast interoperability remain separate runtime boundaries.
- **Approval needed:** no; this is the user-authorized one-at-a-time R12 work.
- **Recommended next pass:** convert classic CUETools only after capturing its
  own application, resource, configuration, and live-window contracts.
- **Smallest safe next step:** none; this slice is closed.
- **Verification plan:** separate Core and canonical full-MSBuild
  restore/build lanes with zero warnings; IL/config/PE/resource comparisons;
  decoded-image equality; old/new top-level-window smoke; canonical tests and
  release safety.
- **Owner:** classic desktop and build maintainers.
- **Status:** fixed 2026-07-29. CUEPlayer is SDK-style net47 and all retained
  contracts above pass. It remains solution-buildable but is not collected by
  either primary release package, matching its pre-conversion reachability.

### R93. Classic CUETools retains an old project and silently duplicated resources - bucket A, risk high

- **Area or slice:** classic CUETools, WinForms/localized resources, generated
  settings, ClickOnce metadata, solution membership, and R8/R12.
- **Why it matters:** this was the final classic GUI that could not consume one
  trustworthy restore graph under Core and full MSBuild. Project conversion
  can change architecture, config probing, resource names/values, localization,
  icon/manifest behavior, or startup. Its main form also contained duplicate
  resource names that the old compiler silently resolved by keeping one value.
- **Evidence found:** the old Release executable, config, PDB, and satellites
  were captured first. Old and new builds retain 53 classes, 463 normalized
  fields, 434 methods, and 229 public declarations; the same
  `CUETools, Version=2.2.6.0` identity; and PE32, IL-only, unsigned flags.
  Configuration is XML-equivalent, including the `plugins` probe. All ten main
  manifest resources are byte-identical. The de-DE and ru-RU satellites retain
  the same 257 and 358 resource entries with byte-identical types and payloads.
  The source cleanup removed 237 second occurrences whose complete XML nodes
  were identical to the first occurrence, so no compiled resource value
  changed. Old and new executables each create 16 top-level windows, including
  a responsive `CUETools 2.2.6` main form.
- **Confidence:** high for build, managed shape, resource/localization
  semantics, architecture, config, and startup. Full interactive conversion,
  repair, settings, accessibility, and localization flows remain separate
  behavioral boundaries.
- **Approval needed:** no; this is the user-authorized final classic GUI slice.
- **Recommended next pass:** continue R12 outside the now-closed classic
  project-format boundary.
- **Smallest safe next step:** none; this slice is closed.
- **Verification plan:** separate Core and canonical full-MSBuild
  restore/build lanes with zero warnings; IL/config/PE/main/satellite resource
  comparisons; old/new top-level-window smoke; duplicate-resource-name,
  canonical test, lock, and release-safety gates.
- **Owner:** classic desktop and build maintainers.
- **Status:** fixed 2026-07-29. CUETools is SDK-style net47, exact duplicate
  resource nodes are removed, and `eng/ci/Test-ResxDuplicateNames.ps1` prevents
  recurrence across first-party `.resx` files.

### R94. The standalone FFmpeg path carried a stale major ABI and no runtime artifact proof - bucket A, risk high

- **Area or slice:** `CUETools.Codecs.ffmpeg`, FFmpeg.AutoGen, the manual native
  DLL workflow, native dependency inventory, and hosted artifact evidence.
- **Why it matters:** a binding/library major mismatch can fail at load or cross
  an unsafe native ABI. The old wrapper also let managed callback exceptions
  cross the native boundary, incompletely owned partial native state, did not
  drain delayed frames, and had no both-architecture behavior proof attached
  to the workflow artifact.
- **Evidence found:** FFmpeg.AutoGen is pinned and locked at 8.1.0. The workflow
  source-builds FFmpeg 8.1.2#3 for x64 and x86 from vcpkg commit
  `9e593bb18ea69cc5095e012465dcd675a822ed0d`, emits source/license/DLL hash
  evidence, and runs the managed worker in the matching process architecture.
  Both local process architectures report runtime 8.1.2 and passed exact
  5,003-frame 16/24-bit path and managed-stream PCM, nonzero seek replay, EOF
  drain, disposal, and callback containment.
- **Confidence:** high for the reachable AIFF decoder and binding/native major
  contract. This does not prove every demuxer/decoder in FFmpeg.
- **Approval needed:** no; the path remains standalone and unshipped.
- **Recommended next pass:** keep primary-package import separate and require a
  new reachability/artifact decision if proposed.
- **Smallest safe next step:** complete the hosted matrix.
- **Verification plan:** zero-warning dual-target wrapper build; real x64/x86
  worker; static workflow contract; actionlint; hosted artifact manifest and
  license inspection.
- **Owner:** codec and release maintainers.
- **Status:** local implementation and x64/x86 evidence complete 2026-07-29;
  hosted receipts are appended to the current live evidence record.

### R95. Release hashes established byte identity but not publisher identity - bucket A, risk high

- **Area or slice:** Windows release workflow, classic/WPF artifact contracts,
  plugin hash manifests, provenance, SBOMs, and signing credentials.
- **Why it matters:** a hash can prove that bytes did not change relative to a
  receipt, but it cannot tell a user who published them. Signing after
  provenance or without regenerating plugin manifests would also make existing
  evidence false or prevent the application from loading its own plugins.
- **Evidence found:** `signing-policy.json` selects 117 publisher-built PE files
  while excluding hash-pinned upstream and Microsoft runtime files. Production
  uses SHA-256 Authenticode and SHA-256 RFC 3161 timestamps, verifies all
  signatures and timestamps, regenerates plugin manifests, revalidates both
  artifacts, and only then generates provenance/SBOMs. Tag builds and explicit
  signed dispatches fail closed when credentials are unavailable. Manual
  evidence builds are labeled `unsigned-evaluation`, not silently releasable.
- **Confidence:** high for policy selection, ordering, credential absence, and
  fail-closed behavior; a public-trust certificate has not yet been configured.
- **Approval needed:** only for the external legal/publisher identity and
  certificate purchase or enrollment.
- **Recommended next pass:** configure the protected `release-signing`
  environment with independent approval and rotate/revoke per policy.
- **Smallest safe next step:** select the certificate subject and populate the
  two secrets and one subject-pattern variable.
- **Verification plan:** 261 static signing-policy checks, 117-file plan,
  actionlint, unsigned-evidence receipt, then a signed tag artifact with
  independent SignTool verification.
- **Owner:** project owner and release maintainers.
- **Status:** repository policy implemented 2026-07-29; public-trust identity
  provisioning remains an explicit external owner action.

### R96. Visual Studio re-evaluated a legacy locked graph and parallel rebuild deleted shared outputs - bucket A, risk high

- **Area or slice:** `CUETools.TestHelpers`, Core/full/IDE restore graphs, the
  classic release command plan, retained build intents, and hosted evidence.
- **Why it matters:** an explicit full-MSBuild restore passed while devenv's
  automatic restore evaluated the legacy helper differently and rewrote two
  reviewed locks. After that was corrected, transaction-wide cleanup followed
  by parallel project `/Rebuild` let one clean delete another project's newly
  produced shared dependency, causing 23 nondeterministic missing-metadata
  failures.
- **Evidence found:** the resource-free helper is now SDK-style net47 and
  explicitly excluded from the conditional resource package. Core MSBuild,
  full MSBuild, and Visual Studio agree on the two dependent lock graphs.
  Devenv built 58/58 projects with both lock hashes unchanged. The receipt
  still proves all declared output leaves absent before using `/Build`; the
  fresh Any CPU/x64/Win32 transaction completed with zero native warning
  fingerprints and published the exact classic artifact.
- **Confidence:** high for local Visual Studio 18 Community and the guarded
  hosted Visual Studio path.
- **Approval needed:** no; this is build/release correctness within the
  authorized conversion and evidence scope.
- **Recommended next pass:** retain hosted receipts and compare future IDE and
  runner-image changes.
- **Smallest safe next step:** none; the repo-local defect is closed.
- **Verification plan:** legacy/SDK assembly contract, Core/full locked
  restores, lock hashes across real devenv, receipt/orchestrator fault tests,
  three-configuration fresh build, native warning gate, and artifact collector.
- **Owner:** build and release maintainers.
- **Status:** fixed 2026-07-30.

### R97. Hosted success depended on deprecated action-runtime compatibility - bucket A, risk medium

- **Area or slice:** all four GitHub Actions workflows and their immutable
  checkout, .NET setup, and artifact-upload pins.
- **Why it matters:** GitHub completed the WPF workflow only by forcing two
  actions that declared Node 20 onto Node 24. A green conclusion therefore hid
  a platform migration warning and left future runner behavior dependent on a
  compatibility shim.
- **Evidence found:** the annotation named checkout and setup-dotnet. Their
  current upstream releases, plus the artifact and vcpkg actions used by the
  evidence workflows, provide supported replacements. All uses now pin the
  exact checkout 7.0.1, setup-dotnet 6.0.0, upload-artifact 7.0.1, and
  run-vcpkg 11.6 commits.
- **Confidence:** high for the repository workflows and hosted annotation.
- **Approval needed:** no; immutable pin maintenance is inside the authorized
  hosted-evidence scope.
- **Recommended next pass:** treat any future action-runtime annotation as a
  migration finding even when the job conclusion is success.
- **Smallest safe next step:** none; retain the source-bound annotation
  receipts as the new baseline.
- **Verification plan:** actionlint, FFmpeg/signing/release workflow contracts,
  classic CI, WPF CI, dual-architecture FFmpeg, and unsigned release evidence.
- **Owner:** CI and release maintainers.
- **Status:** closed 2026-07-30. FFmpeg matrix run `30516040154`, classic CI
  `30518472651`, WPF CI `30518472662`, and release run `30518479906` all
  succeeded; every associated final check run has zero annotations.

### R98. Hosted classic tests depended on checkout line endings and an installed net20 targeting pack - bucket A, risk high

- **Area or slice:** the RAR5 production-path fixture, repository checkout
  attributes, Core/full MSBuild reference-package roles, and the legacy hosted
  test lane.
- **Why it matters:** the archive contains a 2,083-byte LF payload, while hosted
  checkout expanded its text oracle to 2,118 CRLF bytes. The same run then found
  that Core MSBuild could not build net20 without a machine-installed targeting
  pack even though the dependency lock contained the intended fallback.
- **Evidence found:** `.gitattributes` now pins the exact RAR text oracle to LF
  and the archive to binary. Core MSBuild actively consumes
  `Microsoft.NETFramework.ReferenceAssemblies` for net20; full MSBuild retains
  the same direct dependency with all assets excluded. The first replacement
  hosted run proved the RAR repair with 111 passing codec tests and two expected
  skips, then exposed that full MSBuild's serialized asset graph was still being
  reused by the Core no-restore build. The probe now owns a locked,
  force-evaluated restore under a lane-isolated
  `MSBuildProjectExtensionsPath` and binds its no-restore build to that same
  graph. The lock hash is unchanged; the complete local legacy lane passes 156
  tests with six expected skips, and the net20 relay probe preserves exception
  type and identity.
- **Confidence:** high for both root causes; the hosted image supplies the
  missing-targeting-pack falsification the local workstation cannot.
- **Approval needed:** no; both fixes make existing test/build intent portable.
- **Recommended next pass:** retain exact checkout attributes for any future
  byte oracle and test reference-package fallbacks on an image without the pack.
- **Smallest safe next step:** none; the replacement hosted lane is green.
- **Verification plan:** attribute inspection; focused RAR extraction/seek;
  Core/full package-role gate; unchanged lock hash; net20 build/relay probe;
  complete hosted legacy test discovery.
- **Owner:** codec-test and build maintainers.
- **Status:** closed 2026-07-30. Hosted classic run `30518472651` passed all
  four enrolled suites: parity 18/22 with four intentional skips, codecs
  111/113 with two intentional skips, processor 9/10 with one intentional
  skip, and ripper 17/17 with no skips.

### R99. Hash-bound encoder build support changed bytes across hosted checkout - bucket A, risk high

- **Area or slice:** external-command encoder source-support manifests, Git
  checkout attributes, WPF publication, and release safety.
- **Why it matters:** the first hosted release passed release controls, classic
  builds, all test discovery, fuzzing, and classic artifact validation, then
  rejected an Opus patch whose 307 committed LF bytes had expanded to 317 CRLF
  bytes. Fixing only that first file would leave six more manifest-selected
  text inputs dependent on checkout configuration.
- **Evidence found:** all seven selected patches, CMake files, and build
  instructions match their declared lengths and SHA-256 digests in the
  repository. `.gitattributes` now fixes each one to LF, `git check-attr`
  reports `eol: lf` for the complete set, and release safety derives that set
  from the encoder manifest and requires an exact LF rule for every member.
- **Confidence:** high for root cause, repository repair, and final hosted
  publication.
- **Approval needed:** no; the fix preserves committed and contract-declared
  bytes across supported checkout hosts.
- **Recommended next pass:** model checkout representation explicitly whenever
  a repository text file enters an exact size/hash artifact contract.
- **Smallest safe next step:** none; the source-bound release artifact was
  independently inspected.
- **Verification plan:** attribute inspection; exact size/hash preparation;
  release safety; clean WPF publication; final artifact and provenance
  inspection.
- **Owner:** release and supply-chain maintainers.
- **Status:** closed 2026-07-30. Source-bound release run `30518479906`
  completed both publications and the downloaded classic/WPF payloads passed
  their artifact contracts and independent hash-manifest inspection.

### R100. Native provenance retained a stale libFLAC patch digest - bucket A, risk high

- **Area or slice:** native dependency inventory, libFLAC patch history,
  checkout-byte contracts, and deterministic provenance generation.
- **Why it matters:** the source-bound release completed both application
  publications, unsigned signing-policy evaluation, tests, fuzzing, and
  artifact validation, then correctly refused provenance because the inventory
  still described the libFLAC patch before 31 reviewed lines were added in
  `f17a83e`. A stale source digest would make a successful receipt false.
- **Evidence found:** the inventory expected SHA-256 `81a305c6...`; the current
  committed 47,573-byte mixed-EOL patch is `e57a0c47...`. History proves the
  content change, and every other pinned input matches its declared digest.
  The inventory now binds the current blob. All 13 pinned files have explicit
  binary or `-text` checkout contracts, and release safety derives the complete
  pinned set, rehashes it, and requires one of those exact rules.
- **Confidence:** high for root cause and repository repair.
- **Approval needed:** no; this repairs a false provenance statement without
  changing the reviewed patch or native build.
- **Recommended next pass:** update source inventories in the same commit as
  every hash-bound input and retain the complete-set gate.
- **Smallest safe next step:** none; local and hosted receipts bind the repaired
  inventory.
- **Verification plan:** full 13-file digest comparison; Git attribute
  inspection; native preparation; release safety; classic/WPF provenance;
  hosted artifact and receipt inspection.
- **Owner:** native build and supply-chain maintainers.
- **Status:** closed 2026-07-30. Release run `30518479906` accepted all current
  pinned native inputs. Both downloaded provenance receipts bind the same
  source commit and the independently rehashed native-inventory sidecars.

### R101. Provenance treated a validated SDK expansion as arbitrary untracked source - bucket A, risk high

- **Area or slice:** release provenance, Git ignore visibility, and the pinned
  Monkey's Audio 13.20 source closure.
- **Why it matters:** the successful hosted release labeled both artifacts
  `patched-or-untracked` because 390 archive-derived SDK files were visible in
  the clean clone. The same tree was hidden by this workstation's private
  `.git/info/exclude`, so source-state evidence depended on machine-local Git
  configuration. Six classified compiler/linker outputs also dirtied the
  verdict even though they were explicitly not source.
- **Evidence found:** the official 1,787,657-byte archive is pinned by SHA-256.
  The shared closure validator checks all 423 archive members and four
  CUETools overrides by exact path, byte length, and hash; rejects collisions,
  traversal, reparse points, missing members, drift, and foreign files; and
  emits an identity digest independent of Git visibility. Provenance exempts
  only those exact validated derived members, records the closure, keeps
  compiler residue visible by count/classification, and leaves any unknown
  untracked file source-dirty.
- **Confidence:** high for the local closure and policy tests plus the decisive
  clean-clone hosted receipts.
- **Approval needed:** no; this corrects the meaning of existing provenance
  without weakening source validation.
- **Recommended next pass:** retain a clean-clone regression whenever ignored
  generated source becomes a build input.
- **Smallest safe next step:** none; both replacement receipts have the clean
  state and exact closure.
- **Verification plan:** PowerShell 5.1 closure validation; release safety;
  local provenance; clean hosted release; receipt inspection with unknown-file
  refusal preserved.
- **Owner:** release and supply-chain maintainers.
- **Status:** closed 2026-07-30. Both downloaded receipts from release run
  `30518479906` report `source.state=clean`, no root patch, no unknown
  untracked files, five clean submodules, and the validated 423-member closure
  digest `5777ba9a6debcd55565ba49c2e713fdb46a62d81474bc17d394ef17893eeb578`.

### R102. PowerShell SBOM canonicalization corrupted SPDX arrays and stale-hashed the result - bucket A, risk critical

- **Area or slice:** deterministic SBOM generation, Windows PowerShell 5.1,
  Microsoft SBOM Tool, CycloneDX, and hosted annotations.
- **Why it matters:** one-element and other JSON arrays were rewritten as
  `{value, Count}` objects. Microsoft SBOM Tool rejected the retained SPDX
  document, while its `.sha256` sidecar still described the pre-normalized
  bytes. The hosted job nevertheless concluded success and emitted two
  no-package warnings, so generator exit alone was false evidence.
- **Evidence found:** raw SBOM Tool output contains correct arrays. A
  package-free net8 guard now canonicalizes the JSON tree without changing
  types; proves the exact artifact path/SHA-256/file-ID/root-package closure;
  verifies the sidecar after final normalization; and validates the non-empty
  CycloneDX graph. Microsoft SBOM Tool must independently report a successful
  97-file or 557-file artifact validation. Its expected zero dependency
  detection is logged at error verbosity because CycloneDX owns the dependency
  graph and the new postconditions prove the complementary SPDX file inventory.
- **Confidence:** high locally: both real artifacts pass the custom and
  Microsoft validators, with 24/25 classic and 37/38 WPF CycloneDX
  component/dependency-node counts.
- **Approval needed:** no; this repairs invalid retained evidence.
- **Recommended next pass:** require a schema/semantic validator and sidecar
  check after every future SBOM transform.
- **Smallest safe next step:** none; the source-bound release has zero
  annotations and independently validated SBOMs.
- **Verification plan:** zero-warning guard build; self-test that rejects the
  observed array wrapper; idempotent canonicalization; both real artifact
  inventories; Microsoft validation; hosted annotation inspection.
- **Owner:** release and supply-chain maintainers.
- **Status:** closed 2026-07-30. The hosted release and an independent
  downloaded-artifact audit both accepted exact 97-file classic and 557-file
  WPF SPDX closures. Microsoft validation reported zero failures, CycloneDX
  retained 24/25 and 37/38 component/dependency-node graphs, final sidecars
  matched, and the check run has zero annotations.

### R103. Production WPF native wrappers ignored the manifest-approved path - bucket A, risk high

- **Area or slice:** packaged plugin trust bootstrap, five native codec wrappers,
  WPF production layout, artifact probes, and Monkey's Audio failure cleanup.
- **Why it matters:** the app preloaded and hash-approved native DLLs from
  `plugins/x64`, but root-loaded managed wrapper duplicates independently searched
  a nonexistent root `x64` folder. A selected codec therefore failed only after a
  CD read began. Monkey's Audio then let its finalizer re-enter the failed type
  initializer, risking a fatal process termination.
- **Evidence found:** the installed build reproduced libFLAC, WavPack, and
  Monkey's Audio `TypeInitializationException` failures. Process module paths show
  native modules under `plugins/x64` and managed wrappers at the app root. The
  artifact validator passed because it loaded the managed wrappers from the plugin
  directory, not through the WPF apphost layout.
- **Confidence:** high for the production-layout root cause and validator gap.
- **Approval needed:** no; the user approved autonomous remediation. The exact
  manifest trust boundary must remain intact.
- **Recommended next pass:** pass 11 bounded remediation and verification.
- **Smallest safe next step:** hand the exact approved full path from the trust
  loader to the wrapper through a conflict-rejecting registry, then probe the
  published apphost in a child process.
- **Verification plan:** resolver conflict tests; five-wrapper guard; native
  version and round-trip tests; self-contained publish; WPF child-process probe;
  artifact validation; no root native duplicates.
- **Owner:** codec runtime and release maintainers.
- **Status:** closed 2026-07-31. A conflict-rejecting registry now joins the
  manifest-approved native path to root-loaded managed wrappers without adding a
  search path or duplicate DLL. The published apphost ran real FLAC, WavPack,
  Monkey's Audio, and LAME encodes plus HDCD initialization from the production
  layout. The artifact contract passed all five process probes, and Monkey's Audio
  finalizer cleanup contains partial-initialization failure.

### R104. Codec selection could retain invalid profiles and fail after disc reads began - bucket A, risk high

- **Area or slice:** command encoder settings migration, codec availability,
  Rip/Test & Copy startup, settings editor, and format picker.
- **Why it matters:** a lossy Ogg profile retained a lossless verification
  requirement and the generic editor exposed `Lossless` as a mutable setting.
  Raw extension-only dropdowns hid unavailable codecs and gave no implementation,
  origin, or readiness explanation. Native failures were discovered only after
  optical work had started.
- **Evidence found:** the installed settings file carries Ogg as lossy with
  `VerificationRequired=true`; `EncoderCatalog.IsUsable` applies that stale flag to
  every command encoder; `EncoderSettingsViewModel` exposes all browsable structural
  properties; Rip and Test & Copy do not validate a selected codec before entering
  their read lifecycle.
- **Confidence:** high for the invalid-state and late-failure paths.
- **Approval needed:** no; the user explicitly requested the full repair and rich
  grouped picker.
- **Recommended next pass:** pass 11 bounded remediation and verification.
- **Smallest safe next step:** normalize the invariant at settings load and catalog
  registration, centralize codec health, then use that result for both picker state
  and the pre-read gate.
- **Verification plan:** stale-profile JSON tests; property visibility guard;
  descriptor/group/sort tests; unavailable-selection refusal; pre-read gate tests;
  responsive XAML check; full WPF suite.
- **Owner:** WPF and codec-profile maintainers.
- **Status:** closed 2026-07-31. Controlled normalization removes stale lossy
  verifier state while retaining the live lossless-verification invariant. Rip,
  Convert, and Queue now use one grouped implementation-aware picker. Unavailable
  rows remain explained but unselectable; queue records carry the exact stable
  implementation id. Rip and Test & Copy refuse an unhealthy codec before claiming
  a drive and freeze one checked implementation for the complete operation.

### R105. Recurrent H: 08/0A communication failures aborted otherwise valid payload reads - bucket A, risk high

- **Area or slice:** `Bwg.Scsi` sense formatting and the top-level
  `SCSIDrive` normal payload loop.
- **Why it matters:** H: has repeatedly aborted Test & Copy at unrelated disc
  addresses with the same 16-sector BEh `HardwareError / ASC 08 / ASCQ 0A`.
  The UI reports `NO SENSE STRING`, losing the known communication-family identity,
  and makes no bounded recovery attempt even though an isolated rerun crossed an
  earlier occurrence.
- **Evidence found:** scrubbed diagnostic logs retain relative sectors 36,000,
  36,576, 192,224, and 241,968 with identical command shape and transition flags
  false. A later isolated run crossed the first failure point. The
  [T10 ASC/ASCQ table](https://www.t10.org/lists/asc-num.htm) assigns ASC 08 to
  logical-unit communication failures but does not assign qualifier 0A, so the raw
  qualifier must remain explicit and no official name may be invented.
- **Confidence:** high for the repeated drive/communication signature and missing
  diagnostic; medium for recovery because only a later independent rerun, not an
  in-command retry, has crossed the condition.
- **Approval needed:** no; the user supplied repeated evidence and requested the
  bounded fix.
- **Recommended next pass:** pass 11 bounded remediation and hardware proof.
- **Smallest safe next step:** one retry for only the first normal 16-sector BEh
  `DeviceFailed / HardwareError / 08/0A` on the observed H: model and firmware,
  outside a control/cache transition; keep any repeat or different retry failure
  fatal.
- **Verification plan:** positive/negative classifier tests, ASC-family formatter
  tests, route guard, SCSI multi-target builds, full managed gates, then an H:
  Test & Copy that records the retry counter if the transient recurs.
- **Owner:** optical/SCSI maintainer.
- **Status:** closed 2026-07-31. The classifier and route guards pass 32/32,
  net8/net47/net20 builds pass, the canonical 641/647 suite and empty warning gate
  pass, release safety passes, and the separate self-contained R105 artifact passes
  the production contract. Four source-bound address probes passed, followed by a
  full 846-second H: Test & Copy with 11 verified FLAC files, matching AR/CTDB
  evidence, zero reread/failed windows, and final decoded-output verification. Its
  retry counters remained zero; the open adversarial record keeps branch activation
  distinct from the passed user outcome.

## 2026-08-01 recovery adversarial-pass remediation wave

Source: the damaged-disc recovery addendum in `adversarial-pass.md` (2026-08-01).
The user approved the full wave ("do it all"). Findings labeled verified there
were re-opened line by line by the orchestrator; inferred items get a verify
step before any fix.

### R106. DeepRecovery breaks failed-sector accounting - bucket A, risk high

- **Area or slice:** `CUETools.Ripper.SCSI/SCSIDrive.cs` sentinel and retry
  bookkeeping; `CUETools.Wpf/Services/RipService.cs` failedWindows counter.
- **Why it matters:** never-converged sectors under DeepRecovery (default on)
  are reported clean in `FailedSectors`, so rip logs show no suspicious
  positions for low-confidence audio; the converse mislabels late-converged
  sectors as failed; `failedWindows` counts windows deep recovery later
  converged.
- **Evidence found:** `SCSIDrive.cs:213` exact-sentinel equality vs `:1798`
  `pass + 2` store vs `:2439-2443` extension break; `RipService.cs:695-698`.
  Verified 2026-08-01.
- **Confidence:** verified. **Approval needed:** no.
- **Smallest safe next step:** mark give-up as a state, not an exact pass
  count; fix the byte-cast wrap; correct the gave-up counter.
- **Verification plan:** deterministic ripper-suite cases for converged-late,
  never-converged, and baseline-cap sectors; canonical modern suites.
- **Status:** fixed 2026-08-01. `FailedSectorAccounting.FinalizeWindow` marks
  give-up as engine state at window end (legacy-sentinel equivalence proven by
  test), `FailedSectors` is engine-maintained, the one-shot
  `WindowGivenUpSectors` verdict rides `ReadProgressArgs` like the slip
  verdict, and `RipService` counts engine verdicts instead of the mid-pass
  heuristic. Ripper 40/40, WPF 453/453, legacy lane 22/113/10/17 all pass.

### R107. Deep-recovery pass counts can wrap 8-bit vote accumulators - bucket A, risk medium

- **Area or slice:** `SCSIDrive.cs` accumulators, `RecoveryPolicy.cs`.
- **Why it matters:** past 255 contributing passes the UserData bit lanes carry
  into neighbors and `C2Count` wraps, which can flip votes with confident
  margins across the whole stuck window.
- **Evidence found:** `SCSIDrive.cs:1438-1447`, `SecureSectorVote.cs:33-46`,
  no pass cap in `RecoveryPolicy.cs:9-23`. Inferred (wrap is arithmetic fact;
  reachability inside the 120 s ceiling unproven).
- **Confidence:** verified (arithmetic), inferred (reachability).
  **Approval needed:** no.
- **Smallest safe next step:** cap deep-recovery passes at 255 in the loop
  bound; add a deterministic wrap regression test.
- **Verification plan:** ripper suite; unknowns entry stays open for the
  telemetry ceiling question.
- **Status:** fixed 2026-08-01. `RecoveryPolicy.MaxPasses = 252` bounds the
  deep-recovery loop; the accumulator-capacity guard test pins pass + 2 within
  a byte and passes below the 256-observation lane carry. The reachability
  unknown stays open; the wrap is now unreachable by construction.

### R108. Held Copy deleted on Stop-during-confirm and on tray events - bucket A, risk high

- **Area or slice:** `RipService.cs` confirming-read failure routing and
  `RipViewModel.cs` `ClearDiscView`/eject.
- **Why it matters:** the only completed encoded result is destroyed: a
  `"Stopped."` confirm result takes the Fail path (workspace deleted) while
  every other confirm failure holds, StopOnUnrecoverable automates that Stop
  on damaged discs, and user eject or a multi-poll phantom tray event discards
  a held Copy with no confirmation.
- **Evidence found:** `RipService.cs:1342-1343` vs `:1344-1349`;
  `RipViewModel.cs:1270-1274`, `:1982-1990`, phantom-tray note `:688-696`.
  Verified 2026-08-01 (refuter and trigger-tracer concurred).
- **Confidence:** verified. **Approval needed:** no (CLAUDE.md already states
  the held-state contract; this enforces it).
- **Smallest safe next step:** route Stop-during-confirm to Held; require an
  explicit confirmation before a tray/eject path discards a held Copy (disc
  change semantics stay: a genuinely different disc still invalidates).
- **Verification plan:** WPF suite cases for stop-during-confirm hold,
  eject-with-held confirmation, and disc-change invalidation.
- **Status:** fixed 2026-08-01, stronger than the planned confirmation: a stop
  before or during the confirming read now holds the completed Copy
  (`BuildHeld` gained `honorStop`), and no tray-driven path deletes held
  staging at all - `ClearDiscView` parks the result keyed to its disc; the
  same disc returning restores the offer, while a different disc, a new job,
  or an explicit Discard frees it. Contract tests pin both; WPF 457/457.
  Remaining (tracked in R116): a held result still does not survive app exit,
  so the 24-hour startup sweep can free a crash-stranded stage.

### R109. Damage and evidence truth in report, history, and rip.verify - bucket A, risk high

- **Area or slice:** `RipReport.cs`, `ReportViewModel.cs`, `HistoryStore.cs`,
  `RipService.cs` committed-record assembly.
- **Why it matters:** damaged-consistent Test & Copy renders as "Verified"
  (headline, badge, history) while the log says CONSISTENT; committed AR/CTDB
  confidences come from the newest read, not the committed read, so a
  certificate can claim database verification for bytes it does not cover.
- **Evidence found:** `RipReport.cs:56-59` (no damage field);
  `RipService.cs:1737-1748` (`newest` under a committed-read comment), Held
  path `:1379`, `:1397-1408`. Verified 2026-08-01.
- **Confidence:** verified. **Approval needed:** no.
- **Smallest safe next step:** carry a damage field through report, history,
  and `rip.verify`; bind AR/CTDB numbers to the committed (or held Copy) read.
- **Verification plan:** WPF suite report/history/labeling cases.
- **Status:** fixed 2026-08-01. `RipReport` carries `FailedWindows` and
  `DamageRepairRequired`; `Verified` demotes damaged agreement everywhere
  (headline, badge, history rows, log body damage line), matching the Test &
  Copy log's CONSISTENT policy including a database match over damaged media.
  Committed and held evidence now binds to the committed (or Copy) read's own
  AR/CTDB checks in the result, the `rip.verify` record (which also gains
  `FailedWindows`), and the Test & Copy log. Old persisted rows and records
  deserialize to zero damage and keep their original wording. WPF 461/461.

### R110. StopOnUnrecoverable fires before classification and blocks deep recovery - bucket A, risk medium

- **Area or slice:** `RipViewModel.cs` reread handler, `RipService.cs` reread
  math.
- **Why it matters:** the stop latches mid-final-pass on the running error
  count, aborting jobs whose final pass would converge, branding media
  Unreadable without classification, and making the DeepRecovery extension
  unreachable whenever both settings are on. CLAUDE.md requires the stop only
  after the evidence policy classifies a sector unrecoverable.
- **Evidence found:** `RipViewModel.cs:1266-1274` (and the duplicate block),
  `RipService.cs:636-637`, `:700-703`. Verified 2026-08-01.
- **Confidence:** verified. **Approval needed:** no.
- **Smallest safe next step:** latch the stop only on a window the engine has
  classified given-up (post-window, not mid-pass), preserving the extension.
- **Verification plan:** WPF suite reread-handler cases.
- **Status:** fixed 2026-08-01. `RereadReport` carries the engine's one-shot
  verdict through `onReread`; the two duplicated VM closures collapsed into one
  `MakeRereadHandler` that latches Unreadable and Stop only on
  `WindowGivenUpSectors > 0`. Source-contract tests pin both the VM latch and
  the RipService forwarding; WPF 455/455.

### R111. CTDB repair applies the first server-ordered variant - bucket A, risk medium

- **Area or slice:** `RepairTransaction.cs`, `CUESheet.cs` choice assembly.
- **Why it matters:** with several recoverable variants the repair converges on
  whichever the server listed first, so a conf=1 pressing can outrank conf=40
  and the receipt records that variant as success.
- **Evidence found:** `RepairTransaction.cs:68` (`selection = 0`),
  `CUESheet.cs:4764-4783` (server-order list, no confidence sort). Verified
  2026-08-01.
- **Confidence:** verified. **Approval needed:** user approved the wave; the
  change is deterministic ranking inside the existing repair transaction.
- **Smallest safe next step:** select the highest-confidence recoverable
  entry (stable tie-break), leaving single-entry behavior unchanged.
- **Verification plan:** deterministic selection test; existing repair
  preservation suites unchanged.
- **Status:** fixed 2026-08-01. `CueRepairEngine.SelectBestVariant` ranks the
  engine's recoverable variants by `DBEntry.conf` with a stable earliest-entry
  tie-break; five deterministic selection tests pass and the repair
  preservation suites are unchanged. WPF 466/466.

### R112. SCSI transport evidence hardening - bucket A, risk high (latent)

- **Area or slice:** `Bwg.Scsi/Device.cs` pass-through and sense lifetime.
- **Why it matters:** GOOD-status underruns would fold stale buffer bytes into
  the vote (residual never checked); `SCSIException` NREs when autosense is
  absent, destroying failure identity; `IoctlFailed` leaves the previous
  command's sense readable as current.
- **Evidence found:** `Device.cs:836-852`/`887-903` (verified 2026-08-01);
  `Device.cs:1077`, `:828` (inferred, verify before fix).
- **Confidence:** verified (residual), inferred (NRE, stale sense).
  **Approval needed:** no.
- **Smallest safe next step:** fail reads whose transferred length differs
  from the request with a named counter; verify then fix the NRE and
  stale-sense paths without changing classifier identities.
- **Verification plan:** ripper suite; classifier route tests unchanged;
  unknowns entry for live underrun occurrence stays open.
- **Status:** fixed 2026-08-01 (software). Both pass-through twins now clear
  stale sense/status on IoctlFailed and capture the written-back transfer
  length; `FetchSectors` rejects GOOD-status payload underruns fatally with
  the `ShortPayloadTransferCount` counter; absent autosense reads as
  NoSense/0/0 (never matches a retry classifier) instead of a
  NullReferenceException. Ripper 43/43, legacy lanes green. Pending: one live
  H:/K: session to confirm the underrun guard passes on real hardware (its
  failure direction is loud and safe, not silent).

### R113. Classic path honesty - bucket A, risk high

- **Area or slice:** `CUESheet.cs` Test & Copy, `CUESheetLogWriter.cs`,
  `frmCUERipper.cs` persistence.
- **Why it matters:** classic Test & Copy never compares Test CRC to Copy CRC
  and prints "Copy OK" unconditionally; the EAC-style log hardcodes "Make use
  of C2 pointers : No" and "Defeat audio cache : Yes" regardless of reality;
  persisted Paranoid silently downgrades to Secure on restart.
- **Evidence found:** `CUESheetLogWriter.cs:184`, `:200-203`, `:115`;
  `CUESheet.cs:2823-2838` (`_arTestVerify` never compared);
  `frmCUERipper.cs:192`. Verified 2026-08-01.
- **Confidence:** verified. **Approval needed:** no for honesty fixes; the
  full calibration/cache-defeat port is R117 (decision).
- **Smallest safe next step:** compare Test vs Copy CRCs and report mismatch
  per track/range; make the two hardcoded log lines truthful; honor persisted
  Paranoid.
- **Verification plan:** processor-suite log cases; classic suites green.
- **Status:** fixed 2026-08-01. `CUESheet.TestCopyMismatchTracks` compares the
  Test and Copy CRCs; the EAC-style log prints "COPY MISMATCH" per
  track/range, a summary line, and counts mismatches as errors; the classic
  completion dialog escalates to the error form on mismatch. The cache-defeat
  and C2 header lines now report `ICDRipper.CacheDefeatBytes` and
  `DriveC2ErrorMode` truthfully (new interface member; all three implementors
  updated). Persisted Paranoid survives restart (`Maximum - 1` off-by-one).
  Legacy 18/112/9/17 and modern 43/466 lanes green; classic CUERipper GUI
  builds clean. No in-repo automated test drives `GetExactAudioCopyLog`
  (unchanged gap, noted); behavior verified by code read and suite compile.

### R114. Read-loop inferred defects: 24/00 transition ordering and 64/00 child guard - bucket A, risk medium

- **Area or slice:** `SCSIDrive.cs` FetchSectors decomposition and legacy
  batch split.
- **Why it matters:** if confirmed, a transition-state multi-sector 24/00 is
  decomposed as media evidence before the R57 transition retry can see it (and
  the documented one-shot retry is unreachable for multi-sector shapes); the
  legacy 64/00 split can mark transport/hardware/not-ready child failures as
  unreadable sectors.
- **Evidence found:** `SCSIDrive.cs:1519-1529` vs catch filters `:2313-2345`;
  `:1631-1637`, `:1723`. Inferred; not personally re-opened.
- **Confidence:** inferred. **Approval needed:** no, but verify first.
- **Smallest safe next step:** re-open the routes; if confirmed, check
  transition flags before decomposition and gate the child keep-fatal guard on
  failure class, with deterministic route tests for both orderings.
- **Verification plan:** new orchestration-seam tests (see R116 note); full
  ripper suite.
- **Status:** fixed 2026-08-01, both claims verified at source first.
  `ShouldDecomposeRejectedPayloadBatch` now refuses while a speed or
  cache-defeat transition is pending, so the multi-sector 24/00 reaches the
  one-shot transition retry (whose in-catch repeat already propagates
  fatally); `MayMarkSplitChildUnreadable` gates split children by failure
  class - media always, the exact 64/00 track fault only under the legacy
  parent (mixed-mode discs preserved), transport/hardware/readiness/
  unit-attention fatal under either parent. Policy tests cover both
  orderings; ripper 48/48, legacy lanes green. The orchestration-seam
  activation tests remain R116 work.

### R115. Recovery improvements, small and medium - bucket D, risk low

- **Area or slice:** repair headroom surfacing, C2ErrorMode.None downgrade
  warning, adaptive vote quorum in flapping regions, speed drops at pass
  boundaries inside stuck windows.
- **Why it matters:** each recovers more correct audio or surfaces evidence
  the user needs to choose re-rip vs repair; all reality-checked feasible
  2026-08-01.
- **Evidence found:** adversarial addendum improvement list; existing
  `AdaptiveSpeedController.cs` applies drops only at fresh windows.
- **Confidence:** verified (current behavior). **Approval needed:** no.
- **Smallest safe next step:** implement in that order, each with its own
  deterministic test.
- **Verification plan:** ripper and WPF suites per item.
- **Status:** partially fixed 2026-08-01 (2 of 4). Repair headroom:
  `CDRepairFix.WorstStripeErrors`/`StripeCapacity` populated in `VerifyParity`
  (parity decode test asserts it), threaded through the verify/rip results
  into the repair prompts and the `repair.verify` receipt with an at-capacity
  re-rip recommendation. C2 surfacing: a Secure rip on a no-C2 drive logs a
  named warning and the rip.drive telemetry line carries `c2_mode` and
  `cache_defeat_bytes` (the human-facing log line became truthful in R113).
  Re-scoped to R116 with named prerequisites: speed drops at pass boundaries
  are blocked by the recorded ASUS BW-16D1HT mid-window SET CD SPEED crash
  evidence (`SCSIDrive.cs` fresh-window comment) and need a live drive matrix
  session; the adaptive vote quorum changes which rips are declared secure
  and needs deterministic TestRipper corpus evidence before landing.

### R116. Recovery improvements, large - bucket D, risk medium

- **Area or slice:** targeted rereads of still-disagreeing sectors,
  CTDB-guided second-chance rereads, per-sector evidence persistence for
  resumable damaged-disc sessions, plus the injectable device seam for
  orchestration-route activation tests (unknowns entry 2026-08-01).
- **Why it matters:** multiplies useful passes on damaged regions, uses parity
  knowledge before surrendering, and turns a second session into a resume;
  the seam turns R55/R57/R58/R59 wiring into testable routes.
- **Evidence found:** adversarial addendum; reality-checked feasible.
- **Confidence:** inferred (designs). **Approval needed:** no, but each lands
  as its own reviewed slice.
- **Smallest safe next step:** the device seam first (it also verifies R114),
  then targeted rereads.
- **Verification plan:** deterministic seam tests; ripper suite; live
  hardware session for the reread strategies.
- **Status:** open.

### R118. Floor-speed damaged-region states starve the flush and payload paths - bucket B, risk high, hardware-gated

- **Area or slice:** `SCSIDrive.cs` cache-defeat flush and pinpoint reads;
  deep-recovery floor speed interaction.
- **Why it matters:** live 2026-08-01 evidence from two discs and both drives.
  After deep recovery drops to the 44 kB/s floor inside a damaged region:
  (a) the ASUS BW-16D1HT rejected cache-defeat flush reads with 24/00 until
  the full policy exhausted (30 regions, 30 transient retries, 8 chunk
  fallbacks, wake attempted) and the Test & Copy failed closed mid-Test at a
  deep position; (b) H: died with a bare IoctlFailed on a single-sector
  pinpoint (Win32 error now captured); (c) separately, the ASUS rejected the
  BEh read outright with ASC 20/00 at 40x on a scratched CD-R (fail-fast,
  correct, but a bounded read-command re-probe might recover the state).
  Each failure was policy-correct and cost the user a 25-minute Test phase.
- **Evidence found:** diagnostic logs `...-p41020-...` and `...-p52100-...`
  (2026-08-01 16:09/17:02 sessions); three user screenshots; scrubbed
  contexts carry exact sector/shape/speed/transition identity.
- **Confidence:** verified (logs). **Approval needed:** no, but every
  mitigation needs live drive-matrix evidence before landing.
- **Candidate mitigations (design, not yet decided):** restore a moderate
  eviction speed before flush reads and return to the floor afterward (speed
  transitions are already serialized and retry-covered); one bounded
  read-command re-probe after an exact 20/00 rejection; bound consecutive
  floor-speed given-up windows before easing speed back up; persist the Test
  phase so a late transport death does not discard 25 minutes of evidence
  (overlaps R116 persistence).
- **Status:** open.

### R117. Classic calibration/cache-defeat gate port - decision needed

- **Area or slice:** classic CUERipper and `CUETools.Ripper.Console` secure
  paths.
- **Why it matters:** classic Secure/Paranoid can vote against the drive
  cache; porting the WPF gates is a behavior change to a legacy product
  surface (rips that started silently may now refuse until calibration).
- **Evidence found:** R113 evidence; `ICDRipper` lacks the gate members.
- **Confidence:** verified. **Approval needed:** yes - product decision
  (port vs freeze-and-label); parked in `decisions-needed.md` as D8.
- **Status:** closed 2026-08-01 by decision D8 (B): classic is a frozen
  legacy surface. The secure-mode tooltip and a behavior-conditioned log
  note now state that classic re-reads do not defeat the drive cache and
  point at CUETools 2026; no classic behavior changed. CLAUDE.md scopes the
  calibration/held-state invariants to the modern path.

## Ordering

The locally actionable and hosted correctness queue is closed through R105.
The 2026-08-01 recovery wave landed the same day: R106-R114 are fixed,
R115 is fixed 2-of-4 with the remainder re-scoped, and R116 is the open next
slice, in this order: the injectable device seam (also activates the R114
routes), held-result persistence across app exit, the targeted-reread vote
design (requires per-sector pass counts - the current margin math assumes
uniform passes), CTDB-guided second-chance rereads, per-sector session
persistence, and the two hardware-gated R115 items (pass-boundary speed on
the drive matrix; adaptive quorum with TestRipper corpus evidence). One live
H:/K: session should retain the new `ShortPayloadTransferCount` and
`GivenUpWindowCount` counters. R117 awaits decision D8. Remaining work is
ordered by the authority or evidence it requires:

1. Finish R72/R73's optional high-contrast and 150/200-percent-DPI selector
   captures. The automatic and local-override embedded-output paths are already
   byte-proven; TheAudioDB remains explicitly opt-in.
2. Retain the source-bound hosted receipts recorded in
   `2026-07-29-live-release-evidence.md` and compare future runner-image updates
   against them.
3. Provision the public-trust signing identity in the protected environment;
   policy and unsigned-release refusal are already implemented.
4. Continue the deliberately large R8/R12 SDK/async modernization and the
   behavior-changing R13/R14 LAME project one verified slice at a time.

## Holes / external boundaries

- CTDB TLS (R4) is out-of-repo (server operator).
- CI depends on GitHub-hosted Windows image + VS Enterprise devenv path.
- Signing identity is an owner decision. The policy and tagged-release refusal
  are implemented; hashes alone still do not establish publisher identity.
- Retained vendored binaries remain a supply-chain surface even though shipped
  bytes, immutable gitlinks, patches, and staged source manifests are hash-bound
  and inventoried. The HDCD and RareWares LAME limitations are explicit retain
  decisions with source-built replacement gates; TTA is source-built from a
  reviewable tree and official archive comparison.
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
- 2026-08-01 - added R106-R117 from the damaged-disc recovery adversarial pass.
  User approved the full wave; R117 parked as decision D8.
- 2026-08-01 - wave executed: R106-R114 fixed, R115 fixed 2-of-4 (rest
  re-scoped to R116 with named evidence prerequisites), 12 commits, every
  batch gated on both test lanes. Plan fields lived in the R-entries
  themselves rather than a separate safe-fix-plan update. Scenario
  stress-test gained the SR1-SR8 recovery scenarios; the accumulator-wrap
  unknown resolved by construction, the underrun unknown moved to in
  progress behind its named counter.
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
- 2026-07-29 - closed R80/R81 with refreshed physical disc rendering and a
  35-minute-54-second source-bound damaged-media frame benchmark. Added and
  closed R82 after the same run exposed a missing terminal diagnostic on early
  Test & Copy failure. The run also refreshed R69's exact K: failure evidence.
- 2026-07-29 - cleared R69's observed hardware blocker with a 2,275-second
  Test & Copy and independently verified six-sector CTDB repair. Kept exact
  dormant-branch coverage open because all wake counters were zero. Added R83
  after the same run exposed an artwork-discovery/job-snapshot race.
- 2026-07-29 - bounded the open libhdcd replacement as R84, shipped a
  reproducible source-accompanied Musepack encoder as R85, and closed R86's
  RID-publish lock-file dirtiness. Added R87 for a current-libopus source build.
- 2026-07-29 - closed R87 with a deterministic current-libopus encoder, complete
  source/license/build closure, real stdin/decode/tag checks, mixed 192-kbps
  signal evidence, and a 256-kbps archival default whose old/current decoded
  corpus is identical.
- 2026-07-29 - closed R88 by converting the FLACCL plugin and command host to
  SDK-style net47 projects, preserving the discovered 32-bit-preferred runtime
  contract, and rerunning the full RTX 3060 correctness matrix.
- 2026-07-29 - closed R89 by correcting CLParity's reachability classification
  and retiring its disabled, unconsumed, unshipped, non-building experiment.
- 2026-07-29 - closed R90 by converting BluTools as the first classic-GUI
  SDK-project pilot and preserving its managed, PE, config, resource, and live
  WPF startup contracts.
- 2026-07-29 - closed R91 by converting CUERipper and ProgressODoom, preserving
  their managed/PE/localization/image/config contracts, proving old/new live
  main-form parity, and documenting the required per-MSBuild restore boundary.
- 2026-07-29 - closed R92 by converting CUEPlayer, preserving its managed,
  PE/config/decoded-image contracts and old/new live main-form parity while
  documenting that it remains outside the primary release packages.
- 2026-07-29 - closed R93 by converting classic CUETools, preserving its
  managed/PE/config/main/localized-resource contracts and old/new live
  main-form parity, removing 237 compiler-ignored exact resource duplicates,
  and adding a first-party `.resx` duplicate-name gate.
- 2026-07-30 - closed R96 by converting the resource-free legacy test helper,
  proving Core/full/IDE lock parity, replacing the unsafe pre-clean plus
  parallel `/Rebuild` sequence with a receipted fresh `/Build`, and completing
  the exact three-configuration release transaction.
- 2026-07-31 - closed R103/R104 by binding root-loaded WPF codec wrappers to
  their exact manifest-approved native modules, launching the production apphost
  for real native encode/finalize probes, normalizing stale command profiles, and
  replacing raw extension selectors with one health-aware grouped codec picker.
  The canonical local gate passed 637/643 with six declared skips, zero failures,
  and zero managed warning fingerprints.
- 2026-07-31 - implemented R105's one-shot normal BEh 08/0A communication retry,
  retained the unassigned qualifier and raw identity, and passed 32/32 ripper,
  641/647 canonical, empty-warning, release-safety, and production artifact gates.
  Four address probes and a full concurrent H: Test & Copy passed; both phases
  recorded zero retries, so live branch activation remains in the unknowns ledger.
