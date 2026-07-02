# Decisions Needed

Items surfaced by the anti-dark-code passes that require a human decision before action. The autonomous loop parks them here instead of guessing.

Vocabulary: `.claude/skills/anti-dark-code/references/00-conventions.md`.

**User decisions recorded 2026-07-02.** Implementation happens after the anti-dark-code review passes complete (user's sequencing).

## Approved — queued for implementation

### D1. AccurateRip: switch lookups to HTTPS — APPROVED

- **Decision:** flip HTTP → HTTPS. Server answers HTTPS; no reason to stay on HTTP.
- **Evidence:** `www.accuraterip.com` answered HTTPS 200 in a 2026-07 probe. Client hardcodes `http://` at `CUETools.AccurateRip\AccurateRip.cs:829,1230`.
- **Plan:** change the two scheme literals; keep an http fallback on TLS failure; verify a real lookup still parses and DriveOffsets.bin still downloads.

### D2. CTDB HTTPS — APPROVED (file upstream, revisit client later)

- **Decision:** file an issue / ask upstream to enable TLS on db.cuetools.net; revisit the client once the server answers TLS.
- **Evidence:** `db.cuetools.net` failed the TLS handshake (`CUEToolsDB.cs:74` hardcodes http).
- **Plan:** open a tracking issue on LynxTWO/cuetools_2026 documenting the request and the upstream ask (do not modify the client yet).

### D3. unrar.dll upgrade — APPROVED

- **Decision:** upgrade the bundled unrar DLL.
- **Evidence:** `unrar.dll` is 6.11 (2022-05-28), predating the 6.12 fix for CVE-2022-30333 (path traversal). `CUETools.Compression.Rar\Unrar.cs` wraps it.
- **Urgency note (adversarial pass 07, 2026-07-02):** the RAR *input* path (`RarStream` → `Unrar.Test()` + `DataAvailable` callback) streams into memory and never extracts to a filesystem path, so the CVE-2022-30333 traversal vector is NOT reachable via normal use. Upgrade is hygiene (the DLL still decompresses untrusted data in memory), not an urgent fix.
- **Plan:** drop in the current unrar DLL (Win32 + x64), confirm the P/Invoke signatures still match, re-test the "read from RAR without unpacking" flow.

### D4. SharpZipLib upgrade — APPROVED

- **Decision:** upgrade SharpZipLib.
- **Evidence:** `ICSharpCode.SharpZipLib.dll` 0.85.5 (2010); live in `CUETools.Compression.Zip`.
- **Plan:** replace the vendored DLL with the current NuGet package, adapt the Zip wrapper API, re-test.

### D6. MusicBrainz client replacement — APPROVED (conditional on confidence)

- **Decision:** think hard, scope carefully against the current MusicBrainz web service + Cover Art Archive, and implement the replacement **only if confident it can be done and any breakage fixed**. Otherwise scope it and stop for review.
- **Evidence:** `MusicBrainz/` mirrors the abandoned musicbrainz-sharp (v1.1-era XML API). `MusicBrainzObject.cs:393` uses the old API.
- **Plan:** first produce a written scope (call sites, data shape consumed by CUERipper/eac3ui, the current MB web service v2 JSON endpoints, Cover Art Archive for art, rate-limit + user-agent rules). Decide go/no-go from that scope. This is the highest-risk item; keep it last.

### D7. CUEControls resgen under dotnet build — PARTIALLY DONE; old-style GUIs deferred to R12

- **Decision:** fix so the solution builds without full Visual Studio.
- **What shipped 2026-07-02:** `Directory.Build.targets` at repo root adds `GenerateResourceUsePreserializedResources=true` + `System.Resources.Extensions` for net47 first-party projects, **gated on `$(MSBuildRuntimeType) == 'Core'`** so it applies ONLY to `dotnet build` and is a provable no-op under devenv/CI (the shipping build's resource format and runtime deps are unchanged — important because CUEControls loads binary icons at runtime, and forcing preserialized resources into the shipping build would have required deploying a new DLL). Result: all **SDK-style** net47 first-party projects (CUEControls + the codec/lib projects) now build under `dotnet build`.
- **Why not complete:** the old-style (non-SDK) csproj do not restore a PackageReference injected via the shared targets, so the old-style WinForms GUIs (`CUETools`, `CUERipper`, `CUEPlayer`, `CUETools.eac3ui`, and old-style `CLParity`/`FLACCL`) still cannot `dotnet build`. The clean fix is SDK-style conversion of those projects — real work that needs the GUI run to verify resource loading (cannot be done headless) and belongs to the modernization program (R12), not a blind headless change to the shipping GUIs.
- **Verified:** CUEControls builds under `dotnet build`; TestParity 18/18, TestCodecs 34/34 green; devenv/CI unaffected by construction (Core-gated).

## Still deferred (your earlier call)

### D5. Delete dead projects/binaries — DEFERRED

- FlaCuda projects (absent from sln), `CUDA.NET.dll`, `Freedb.dll`, `MusicBrainz.dll` (no csproj refs). Keep until the initial rollout completes. Note: D6 may retire the MusicBrainz source project; revisit D5 together with D6's outcome.

## Resolved / actioned

- **D1 AccurateRip HTTPS — DONE 2026-07-02.** Flipped both `http://www.accuraterip.com` literals to `https://` (`AccurateRip.cs:833` dBAR lookup, `:1247` DriveOffsets.bin). No http fallback: a failed AR lookup degrades to "not verified" (corroborative, no data loss), so retrying over cleartext buys nothing. Verified: AccurateRip builds; the HTTPS dBAR path returns 404 for a fake id (proves TLS+routing) and DriveOffsets.bin returned 200 earlier; TestParity 18/18 green. Commit pending in this batch.
