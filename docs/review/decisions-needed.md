# Decisions Needed

Items surfaced by the anti-dark-code passes that require a human decision before action. The autonomous loop parks them here instead of guessing.

Vocabulary: `.claude/skills/anti-dark-code/references/00-conventions.md`.

**User decisions recorded 2026-07-02.** Implementation happens after the anti-dark-code review passes complete (user's sequencing).

## Approved - queued for implementation

### D1. AccurateRip: switch lookups to HTTPS - APPROVED

- **Decision:** flip HTTP -> HTTPS. Server answers HTTPS; no reason to stay on HTTP.
- **Evidence:** `www.accuraterip.com` answered HTTPS 200 in a 2026-07 probe. Client hardcodes `http://` at `CUETools.AccurateRip\AccurateRip.cs:829,1230`.
- **Plan:** change the two scheme literals; keep an http fallback on TLS failure; verify a real lookup still parses and DriveOffsets.bin still downloads.

### D2. CTDB HTTPS - DONE 2026-07-02 (tracking issue filed)

- **Decision:** file an issue / ask upstream to enable TLS on db.cuetools.net; revisit the client once the server answers TLS.
- **Done:** filed tracking issue LynxTWO/cuetools_2026#1 (had to enable Issues on the fork first; the third-party gchudov repo was NOT posted to). Includes upstream-ready ask text. Client `CUEToolsDB.cs` left unchanged until the server answers TLS.

### D3. unrar.dll upgrade - APPROVED

- **Decision:** upgrade the bundled unrar DLL.
- **Evidence:** `unrar.dll` is 6.11 (2022-05-28), predating the 6.12 fix for CVE-2022-30333 (path traversal). `CUETools.Compression.Rar\Unrar.cs` wraps it.
- **Urgency note (adversarial pass 07, 2026-07-02):** the RAR *input* path (`RarStream` -> `Unrar.Test()` + `DataAvailable` callback) streams into memory and never extracts to a filesystem path, so the CVE-2022-30333 traversal vector is NOT reachable via normal use. Upgrade is hygiene (the DLL still decompresses untrusted data in memory), not an urgent fix.
- **Status 2026-07-02: SCOPED, DEFERRED (hygiene, needs functional verification).** Current vendored DLLs: `ThirdParty\Win32\unrar.dll` and `ThirdParty\x64\Unrar.dll`, both 6.11.0. The old direct URL `https://www.rarlab.com/rar/UnRARDLL.exe` now 404s (rarlab versions the file). Not swapped because: (a) it's hygiene-only per pass 07, and (b) a blind binary swap can't be functionally verified headless (no RAR encoder here to build a test archive; only a round-trip through `RarStream` would prove the classic P/Invoke API still matches).
- **Plan when picked up:** find the current rarlab UnRAR DLL download (their SDK page), replace both DLLs keeping filenames, confirm the classic API exports (`RAROpenArchiveEx`, `RARReadHeaderEx`, `RARProcessFile`, `RARSetCallback`, `RARSetPassword`) are unchanged, then verify with a real RAR: create a small `.rar` (WinRAR/`rar.exe` or another encoder) and round-trip it through `RarStream`/`RarCompressionProvider` (mirror the ziptest harness in scratchpad). The classic UnRAR API has been ABI-stable for ~20 years, so the swap is expected clean, but ship it only after that round-trip passes.

### D4. SharpZipLib upgrade - DONE 2026-07-02

- **Done:** `CUETools.Compression.Zip` now uses the SharpZipLib 1.4.2 NuGet package for net47 + netstandard2.0; net20 keeps the vendored 0.85.5 DLL (modern SharpZipLib dropped net20). Adapted the password path for the modern API: `GetInputStream` returns a plain `Stream` (not `ZipInputStream`) and AES needs the password on the `ZipFile` *before* opening the entry, so `ZipCompressionProvider.Decompress` now requests and sets the password up front instead of lazily on first read. Both `collect_files.bat` and `collect_files_debug.bat` fixed to ship the modern DLL from the build output, not the old vendored copy.
- **Verified:** net47 + netstandard2.0 build; a net8 round-trip harness against the built provider passes all 6 checks (contents list, full read byte-exact, Length, forward seek, backward-seek reopen, and AES-encrypted read via the PasswordRequired event).

### D6. MusicBrainz client replacement - SCOPED; STOPPED FOR YOUR DECISION 2026-07-02

- **Premise changed - there is no live MusicBrainz client to replace.** Full analysis in `docs/review/musicbrainz-replacement-scope.md`. In short: the `MusicBrainz/` musicbrainz-sharp library is dead code (not built, referenced by no project), and CUERipper's direct MusicBrainz query is commented out (`frmCUERipper.cs:893-918`). CUETools gets MusicBrainz metadata via **CTDB's server-side proxy** (`cueSheet.CTDB.Metadata`) plus a Freedb/gnudb fallback. So a "replacement" would reintroduce a direct-to-MusicBrainz path the project deliberately removed - a product decision, not a mechanical swap.
- **I stopped rather than implement**, per your conditional approval: I'm confident I *could* build a modern MB v2 JSON provider, but whether it *should* exist (vs. relying on CTDB's proxy) is your call.
- **Your decision (see the scope doc):** (A) rely on CTDB's proxy and delete the dead library (recommended); (B) add a NEW direct MusicBrainz v2 provider behind the metadata seam as a fallback/richer source (I'll implement + verify on request); (C) leave as-is. Either A or B retires the dead `MusicBrainz/` project (folds into D5).
- **DONE 2026-07-02 - you chose A.** Deleted the dead `MusicBrainz/` project (musicbrainz-sharp mirror), `ThirdParty/MusicBrainz.dll`, the `CUERipper.csproj` datasource reference + file, and the sln solution-items entry. Removed the commented-out direct-MB block in `frmCUERipper.cs` (replaced with a note pointing at the CTDB-proxy path). MB metadata continues via CTDB + Freedb/gnudb. The "look up on musicbrainz.org" website buttons (frmChoice, frmCUERipper) are unrelated to the library and were kept (and flipped http->https). TestCodecs 34/34 green; GUI projects verified by CI/devenv on push.

### D7. CUEControls resgen under dotnet build - PARTIALLY DONE; old-style GUIs deferred to R12

- **Decision:** fix so the solution builds without full Visual Studio.
- **What shipped 2026-07-02:** `Directory.Build.targets` at repo root adds `GenerateResourceUsePreserializedResources=true` + `System.Resources.Extensions` for net47 first-party projects, **gated on `$(MSBuildRuntimeType) == 'Core'`** so it applies ONLY to `dotnet build` and is a provable no-op under devenv/CI (the shipping build's resource format and runtime deps are unchanged - important because CUEControls loads binary icons at runtime, and forcing preserialized resources into the shipping build would have required deploying a new DLL). Result: all **SDK-style** net47 first-party projects (CUEControls + the codec/lib projects) now build under `dotnet build`.
- **Why not complete:** the old-style (non-SDK) csproj do not restore a PackageReference injected via the shared targets, so the old-style WinForms GUIs (`CUETools`, `CUERipper`, `CUEPlayer`, `CUETools.eac3ui`, and old-style `CLParity`/`FLACCL`) still cannot `dotnet build`. The clean fix is SDK-style conversion of those projects - real work that needs the GUI run to verify resource loading (cannot be done headless) and belongs to the modernization program (R12), not a blind headless change to the shipping GUIs.
- **Verified:** CUEControls builds under `dotnet build`; TestParity 18/18, TestCodecs 34/34 green; devenv/CI unaffected by construction (Core-gated).

## Still deferred (your earlier call)

### D5. Delete dead projects/binaries - DEFERRED

- FlaCuda projects (absent from sln), `CUDA.NET.dll`, `Freedb.dll`, `MusicBrainz.dll` (no csproj refs). Keep until the initial rollout completes. Note: D6 may retire the MusicBrainz source project; revisit D5 together with D6's outcome.

## Resolved / actioned

- **D1 AccurateRip HTTPS - DONE 2026-07-02.** Flipped both `http://www.accuraterip.com` literals to `https://` (`AccurateRip.cs:833` dBAR lookup, `:1247` DriveOffsets.bin). No http fallback: a failed AR lookup degrades to "not verified" (corroborative, no data loss), so retrying over cleartext buys nothing. Verified: AccurateRip builds; the HTTPS dBAR path returns 404 for a fake id (proves TLS+routing) and DriveOffsets.bin returned 200 earlier; TestParity 18/18 green. Commit pending in this batch.
