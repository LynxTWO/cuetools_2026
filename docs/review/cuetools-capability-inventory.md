# CUETools capability & settings inventory (for the unified GUI)

Purpose: a complete list of what the engine can do and every user-configurable setting,
organized by where it should live in the unified Rip / Verify & Repair / Convert app.
Built from two full read-only code audits on 2026-07-10. Source of truth for both the GUI
information architecture and the eventual data binding. Citations are `file:line`.

## The app is three modes over one engine

All three modes share the same `CUESheet` engine, metadata lookup, encoder set, tagging,
and AccurateRip/CTDB verification. They differ only in the input and the primary action:

- **Rip** - input is a CD in a drive; extract + encode + verify (+ optional CTDB submit).
- **Verify & Repair** - input is existing files (`.cue`, single file, folder, archive,
  `.m3u`); verify against AccurateRip + CTDB, and if CTDB has parity, **repair** the bad
  samples and re-verify. Also "fix offset".
- **Convert** - input is existing files; transcode to another format/layout (+ verify).

Actions enum (`CUETools.Processor/CUEAction.cs:3-9`): `Encode`, `Verify`,
`CreateDummyCUE`, `CorrectFilenames`. The verify/encode "scripts" that the mode dropdowns
expose (`CUESheet.cs:4479-4567`): `default`, `only if found`, `only if rip log present`,
`repair`, `fix offset`, `encode if verified`.

### Repair, precisely (the standout capability)

`CUETools.CTDB` computes per-entry `hasErrors` / `canRecover` and a `CDRepairFix repair`
(`DBEntry.cs`, `CUEToolsDB.cs:462-486`). The `repair` action verifies, lists the exact
damaged positions from `repair.AffectedSectorArray` as `mm:ss` times
(`CUESheet.cs:4500-4514`), reconstructs the correct samples in-stream
(`repair.Write(sampleBuffer)`, `:3663-3667`), and re-runs AccurateRip on the corrected
audio before writing it out. The affected-sector array is what the UI's repair map draws.

### Input types (`CUESheet.Open`, `CUESheet.cs:994-1164`)

CD drive root; `.cue`; single audio file (reads embedded cue/log/AR-id when the format
allows); folder (file-group scan); archive `.rar`/`.zip` (pick internal cue); `.m3u`.

### Output layout - `CUEStyle` (`CUEStyle.cs`)

`SingleFileWithCUE` (one file, embedded cue), `SingleFile` (one file + external cue),
`GapsPrepended`, `GapsAppended` (default, per-track), `GapsLeftOut`. Plus M3U, EAC-style or
plain rip log, AccurateRip `.accurip` log, optional `.toc`.

## Honesty note: mockup features not yet in the code

The v4 mockup implied three drive capabilities that **do not exist** in `SCSIDrive.cs`
today (grep for cache/lead-in/lead-out/speed/overread returned nothing):

- drive **cache defeat / flush**
- **lead-in / lead-out overread**
- **read-speed** control

These are worth building (they were on the accuracy-ideas list), but they are NEW work, not
existing bindings. The GUI should show them only once implemented, or clearly as "planned".
What the drive layer really exposes today: read offset, C2 error mode, read command
(BEh/D8h/auto), and secure depth (Burst/Secure/Paranoid). Dual-drive cross-verification is
also new work.

## Settings, grouped by proposed Settings sub-section

Three config stores exist and a unified GUI must reconcile them: `CUEConfig` (flat
`name=value` file, ~80 fields), `CUEConfigAdvanced` (one JSON blob, every property already
carries `[Category]`/`[DisplayName]`/`[DefaultValue]` - so this whole group can be
**reflection-generated** in the UI), and `CUERipperConfig` (per-drive XML). The EAC CTDB
plugin additionally keeps its own copy in the registry.

### 1. General
`oneInstance` (true), `checkForUpdates` (true), `language` (UI culture),
`separateDecodingThread` (true), `ejectAfterRip` (false), `disableEjectDisc` (false).
(`CUEConfig.cs`)

### 2. Metadata & lookup
CTDB: `CTDBServer` (db.cuetools.net), `metadataSearch` (None/Fast/Default/Extensive),
`CacheMetadata` (true). MusicBrainz: reached through the CTDB proxy. Freedb/gnudb:
`FreedbSiteAddress` (gnudb.gnudb.org), `FreedbUser`, `FreedbDomain` (used only at
`Extensive`). Proxy: `UseProxyMode` (None/System/Custom), `ProxyServer`, `ProxyPort`,
`ProxyUser`, `ProxyPassword`. (`CUEConfigAdvanced.cs`)

### 3. AccurateRip & CTDB
Verify thresholds: `fixOffsetMinimumConfidence` (2), `fixOffsetMinimumTracksPercent` (51),
`encodeWhenConfidence` (2), `encodeWhenPercent` (100), `encodeWhenZeroOffset` (false),
`fixOffset` (false), `fixOffsetToNearest` (true), `noUnverifiedOutput` (false),
`bruteForceDTL` (false). CTDB submit: `CTDBSubmit` (true), `CTDBAsk` (true),
`DetailedCTDBLog` (false). Logs/tags: `writeArTagsOnEncode`, `writeArLogOnConvert` (true),
`writeArTagsOnVerify`, `writeArLogOnVerify`, `arLogToSourceFolder`, `arLogVerbose` (true),
`ArLogFilenameFormat` (%filename%.accurip), `WriteCTDBTagsOnEncode` (true),
`WriteCTDBTagsOnVerify` (false).

### 4. Encoding & formats
The codec matrix (per-extension `allowLossless`/`allowLossy`/`allowEmbed`/tagger,
`CUEToolsCodecsConfig.cs:78-93`): flac, wv, ape, tta, wav, m4a, tak, wma, mp3, ogg, opus
(+ decode-only m2ts, mpls, mlp, aob, aiff). Per-encoder mode/quality + browsable settings:

| Encoder | Ext | Modes | Default | Notable options |
|---|---|---|---|---|
| Flake (managed FLAC) | flac | 0-8 / 0-11 | 5 | padding, verify, MD5, order/window/stereo methods |
| libFLAC | flac | 0-8 | 5 | padding, verify, MD5 |
| FLACCL (OpenCL) | flac | 0-8 / 0-11 | 8 | verify, MD5, non-subset |
| libwavpack | wv | fast/normal/high/high+ | normal | extra mode, MD5, threads |
| MAC (APE) | ape | fast..insane | high | padding |
| ALAC | m4a | 0-10 | 5 | verify |
| WMA | wma | (enumerated) | - | VBR, WMA9/10Pro |
| LAME VBR / CBR | mp3 | V9-V0 / 96-320 | V2 / 256 | - |

External CLI encoders (edit path + params with tokens `%O %M %P %I`): flac.exe,
flake.exe, takc.exe, ffmpeg.exe (ALAC), lame.exe VBR/CBR, oggenc.exe, opusenc.exe,
neroAacEnc.exe, qaac.exe. Output presets: Lossless / Lossy / Hybrid (lossy.flac).
Editing is fully data-bound via `EncoderListViewModel`/`DecoderListViewModel`.

### 5. File naming & output layout
`trackFilenameFormat` (%tracknumber%. %title%), `singleFilenameFormat` (%filename%),
`autoCorrectFilenames` (true), `keepOriginalFilenames` (false), `removeSpecial` (false),
`specialExceptions` (-()), `replaceSpaces` (false), `filenamesANSISafe` (true). Layout:
`gapsHandling` (GapsAppended) [the CUEStyle set], `createM3U` (false),
`createCUEFileInTracksMode` (true), `createCUEFileWhenEmbedded` (true),
`alwaysWriteUTF8CUEFile` (false), `writeUTF8BOM` (true), `writeDummyCUESheetComment`
(true), `fillUpCUE` (true), `overwriteCUEData` (false), `createEACLOG` (true),
`embedLog`/`extractLog` (true), `CreateTOC` (false).

### 6. Gaps & HTOA
`detectGaps` (true), `preserveHTOA` (true), `useHTOALengthThreshold` (true).

### 7. Tagging
`writeBasicTagsFromCUEData` (true), `copyBasicTags` (true), `copyUnknownTags` (true),
`UseId3v24` (false), `WriteCDTOCTag` (true). Taggers per format: TagLibSharp / APEv2 /
ID3v2.

### 8. Album art
`embedAlbumArt` (true), `extractAlbumArt` (true), `CopyAlbumArt` (true), `maxAlbumArtSize`
(300), `AlArtFilenameFormat` (folder.jpg), `CoverArtFiles` (folder.jpg;cover.jpg;...),
`CoverArtSearchSubdirs` (true), CTDB covers `coversSize` (Large), `coversSearch` (Primary).

### 9. HDCD
`detectHDCD` (true), `decodeHDCD` (false), `wait750FramesForHDCD` (true),
`decodeHDCDto24bit` (true), `decodeHDCDtoLW16` (false), `truncate4608ExtraSamples` (true).

### 10. Ripper & drives (per-drive)
Per-drive, keyed by drive AccurateRip signature (`CUERipperConfig.cs`): read offset
(`DriveOffsets`, fallback to `AccurateRipVerify.FindDriveReadOffset` then 0), C2 error mode
(`DriveC2ErrorModes`: None/Mode294/Mode296/Auto, default Auto), read command
(`ReadCDCommands`: BEh/D8h/Unknown-autodetect, default autodetect), plus session controls:
secure depth Burst/Secure/Paranoid (`CorrectionQuality` 0-3, drives retry budget
`16<<quality` and required good passes `1+quality`), default Lossless/Lossy/Hybrid format,
last-used drive. Extraction options proxied from `CUEConfig`: preserveHTOA, detectGaps,
createEACLOG, createM3U, embedAlbumArt, eject/disable-eject, trackFilenameFormat.

## Proposed information architecture

Shell: a mode switch (Rip / Verify & Repair / Convert), a shared metadata + track-list
area, a shared output/format bar, a Queue, a Report, and a Settings area. The three work
modes reuse one set of view models over `CUESheet`; only the input source and default
action change.

- **Rip** - drive + disc, confidence read-map, per-track Quality (C2 vote) + AccurateRip +
  C2, secure depth, format, verify + submit. (mockup Rip tab, corrected: Burst/Secure/
  Paranoid; drop cache/overread until built.)
- **Verify & Repair** - drop in files/folder/cue/archive; AccurateRip + CTDB result per
  track; when CTDB can recover, show the affected-sector map and a Repair action; fix
  offset. (new surface.)
- **Convert** - pick files, choose target format/layout/tagging, run (+ verify). (new
  surface.)
- **Queue** - multiple discs or jobs.
- **Report** - per-job accuracy log, exportable.
- **Settings** - the 10 sub-sections above. Section 2/3/7/8 (the `CUEConfigAdvanced`
  groups) can be reflection-generated from the existing attributes; the rest need explicit
  binding to `CUEConfig`. Reconcile the `CUEConfig` / `CUEConfigAdvanced` / `CUERipperConfig`
  stores behind one settings service.

## Build implications

- One settings service over the three stores; a property-grid-style auto-panel for the
  attributed `CUEConfigAdvanced` groups saves a lot of hand-binding.
- The encoder/decoder editor already has view models - reuse them directly.
- Rip / Verify&Repair / Convert are one shared shell + view-model set differing by input +
  action, not three apps.
- New accuracy features (cache defeat, overread, read speed, dual-drive) are additive to
  the SCSI layer and should be sequenced after the base rip flow.
