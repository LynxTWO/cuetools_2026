# Coverage Ledger

Tracks which areas of the repo have been reviewed under the anti-dark-code workflow, at what confidence, and what the next pass should be. Started 2026-07-02 by pass 05.

Do not claim full coverage unless the evidence in this ledger supports the claim.

Status values, classification labels, and risk levels come from `.claude/skills/anti-dark-code/references/00-conventions.md`. Slice definitions live in `repo-slices.md`.

## Ledger

| Slice | Paths | Risk | Status | Label | Evidence used | Next pass |
| --- | --- | --- | --- | --- | --- | --- |
| S1 Network clients | `CUETools.AccurateRip/`, `CUETools.CTDB*/`, `Freedb/`, `MusicBrainz/` | high | commented | owned-risky / legacy-unclear | 03+06 loop 2026-07-02: trust-boundary and invariant comments on `CUEToolsDB.cs` (HTTP scheme, parity fetch/Range layout, syndrome conflict check, submitter UUID), `AccurateRip.cs` (unsigned lookup, dBAR format, drive-offset fetch), `FreedbHelper.cs` (gnudb mirror). MusicBrainz withheld (mirrored tree). Open questions in `docs/unknowns/critical-paths.md` | 04 logging audit |
| S2 Ripping/SCSI | `CUETools.Ripper*/`, `Bwg.*/` | high | commented | legacy-unclear | 03 loop 2026-07-02: commented the secure-ripping core in `SCSIDrive.cs` (C2-weighted accumulation `ReorganiseSectors` incl. the PIONEER C2-placement quirk, the weighted majority vote `CorrectSectors`, and the failed-sector sentinel). Hardware smoke passed on drive K:. `Bwg.Scsi\Device.cs` (131 KB raw SCSI pass-through) still unread; not required for the secure-vote invariant | 04 logging audit |
| S3 Processor engine | `CUETools.Processor/` | high | commented | owned-risky | 03+06 loop 2026-07-02: commented the metadata->filename sanitization boundary (`CUEConfig.CleanseString`), the unsigned plugin-load trust boundary (`CUEProcessorPlugins.AddPlugin`), and local-DB crash-safe save semantics (`CUEToolsLocalDB.Save`). `CUESheet.cs` write/verify internals still largely unread (212 KB); reserved-name gap logged in critical-paths | 04 logging audit |
| S4 Archive handling | `CUETools.Compression*/` + unrar/SharpZipLib binaries | high | mapped | owned-risky / third-party mirror | binary inventory; csproj reference scan; extraction behavior unread | 07 focused review |
| S5 Parity/repair | `CUETools.Parity/`, `CUETools.CDRepair/`, `CUETools.AccurateRip\CDRepair.cs` | high | commented | owned-risky | 03 loop 2026-07-02: commented the load-bearing CRC self-check in the live repair path (`AccurateRip\CDRepair.cs` VerifyParity) that closed the post-repair-verification unknown; TestParity 18/18 covers the RS math. `Galois.cs` left uncommented (UTF-16 encoded; field polynomials GF(2^8)=0x11d, GF(2^16)=0x1100B noted here instead) | 04 logging audit |
| S6 Managed codecs | `CUETools.Codecs*/` (managed), `ttalib-1.1/` | high | commented | owned-risky | 03 loop 2026-07-02: commented the shared `BitReader` untrusted-input path; `fill()` has no buffer bounds check (out-of-bounds read on malformed streams) - logged high-risk in critical-paths. TestCodecs 34/34 green after edit. Per-codec bitstream internals (Flake/ALAC) still unread | 04 logging audit; fuzz target |
| S7 Native wrappers | `CUETools.Codecs.lib*/`, `.MACLib/`, `.ffmpeg/`, `.lame_enc/` | high | commented | owned-risky / cross-repo-boundary | 03 loop 2026-07-02: commented the native-callback GC-lifetime invariant in `libFLAC\Writer.cs` (delegates field-rooted so native code cannot call freed memory). Representative of the P/Invoke ownership pattern across the wrappers | 04 logging audit |
| S8 GPU codecs | `CUETools.Codecs.FLACCL/`, `.FlaCuda/`, `CUETools.CLParity/`, exes | medium | mapped | legacy-unclear | system map §2; FlaCuda verified orphaned (absent from sln, 2026-07-02) | 07 focused review |
| S9 GUI apps | `CUETools/`, `CUERipper/`, `CUEPlayer/`, `CUETools.eac3ui/`, `CUEControls/`, `ProgressODoom/` | medium | mapped (CUETools, CUERipper); unscanned (CUEPlayer, eac3ui, ProgressODoom internals) | owned-risky / legacy-unclear | MOTD path read; frm sizes; others directory-level only | 02 fill-in, then 03 |
| S10 CLI tools | `CUETools.Converter/`, `.ARCUE/`, `.Flake/`, etc. | low | mapped | owned-clear | system map §2; Program.cs heads only | 03 (light) |
| S11 DSP/output | `CUETools.DSP.*/`, `.CoreAudio/`, `.DirectSound/`, `.Icecast/` | medium | unscanned | legacy-unclear | directory-level inventory only; `IcecastWriter.cs` skimmed | 04 (Icecast), 03 |
| S12 EAC plugin | `CUETools.CTDB.EACPlugin*/` | high | mapped | owned-risky, approval-gated | file listing; net2.0 target verified; plugin logic unread | 03 comments |
| S13 Build/release | `.github/`, `collect_files*.bat`, patches, sln | high | mapped | owned-risky, external-control-plane | workflows read; patches applied cleanly; test gates added to CI and release workflows 2026-07-02 (safe-fix batch 1) | 10 maintenance harness |
| S14 Tests | `CUETools/CUETools.Test*/`, `TestRipper/`, `CUETools.TestHelpers/` | medium | mapped | owned-clear | TestParity, TestCodecs, TestProcessor migrated to SDK-style MSTest v2 2026-07-02 (safe-fix batch 1); post-migration runs match baseline: TestParity 18/18, TestCodecs 34/34. TestProcessor blocked on never-committed fixtures; TestRipper still MSTest v1, needs hardware. See `docs/review/safe-fix-plan.md` | 11 Step 2 (synthetic fixtures); TestRipper with S2 |
| ThirdParty submodules | `ThirdParty/{flac,taglib-sharp,openclnet,WavPack,WindowsMediaLib}` | medium | excluded | cross-repo-boundary, vendored | pins + patch application verified 2026-07-02 | none; explained in system map |
| ThirdParty binaries | `ThirdParty/*.dll`, `Win32/`, `x64/`, `MAC_SDK/` | high | excluded (code passes); audited (inventory) | binary or asset-heavy | version/date/reference inventory 2026-07-02 | 11 (upgrade decisions) |
| Generated code | `GeneratedAssembly.cs`, `*.Designer.cs` | low | excluded | generated | file inspection | none |
| CUERipper.WPF | `CUERipper.WPF/` | low | deferred | owned-clear | user decision 2026-07-02: keep until initial rollout completes | revisit after rollout |

## Notes

- "mapped" here means the pass 02 inventory covered the slice's shape and boundaries. It does not mean the code inside was read line by line; the Status column plus Evidence column say which is which.
- Out-of-repo surfaces that shape live behavior and are not rows here: CTDB server (no HTTPS), AccurateRip server, gnudb, MusicBrainz API, GitHub-hosted runners, EAC host application, cue.tools download site. Named in system map §5-6.
- When a risky slice changes, move its row; do not quietly rewrite history.

## Changelog

- 2026-07-02 — ledger created by pass 05; S1-S14 defined; exclusions recorded; CUERipper.WPF and unreferenced ThirdParty DLLs deferred by user decision.
