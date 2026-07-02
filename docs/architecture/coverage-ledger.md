# Coverage Ledger

Tracks which areas of the repo have been reviewed under the anti-dark-code workflow, at what confidence, and what the next pass should be. Started 2026-07-02 by pass 05.

Do not claim full coverage unless the evidence in this ledger supports the claim.

Status values, classification labels, and risk levels come from `.claude/skills/anti-dark-code/references/00-conventions.md`. Slice definitions live in `repo-slices.md`.

## Ledger

| Slice | Paths | Risk | Status | Label | Evidence used | Next pass |
| --- | --- | --- | --- | --- | --- | --- |
| S1 Network clients | `CUETools.AccurateRip/`, `CUETools.CTDB*/`, `Freedb/`, `MusicBrainz/` | high | mapped | owned-risky / legacy-unclear | system map §5-6; HTTPS probes; `CUEToolsDB.cs`, `AccurateRip.cs`, `FreedbHelper.cs` reads | 04 logging audit |
| S2 Ripping/SCSI | `CUETools.Ripper*/`, `Bwg.*/` | high | mapped | legacy-unclear | system map §2,6; upstream C2 commit history; no code-level read of `Device.cs` internals yet | 03 comments |
| S3 Processor engine | `CUETools.Processor/` | high | mapped | owned-risky | system map §2-4; `CUEProcessorPlugins.cs`, `SettingsReader.cs` reads; `CUESheet.cs` internals unread | 03 comments |
| S4 Archive handling | `CUETools.Compression*/` + unrar/SharpZipLib binaries | high | mapped | owned-risky / third-party mirror | binary inventory; csproj reference scan; extraction behavior unread | 07 focused review |
| S5 Parity/repair | `CUETools.Parity/`, `CUETools.CDRepair/` | high | mapped | owned-risky | system map §2; test projects located; math unread | 03 comments (after S14) |
| S6 Managed codecs | `CUETools.Codecs*/` (managed), `ttalib-1.1/` | high | mapped | owned-risky | system map §2; unsafe-count scan; bitstream code unread | 03 comments |
| S7 Native wrappers | `CUETools.Codecs.lib*/`, `.MACLib/`, `.ffmpeg/`, `.lame_enc/` | high | mapped | owned-risky / cross-repo-boundary | DllImport scan; submodule pins verified | 03 comments |
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
