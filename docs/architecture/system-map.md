# System Map

A factual map of what runs in this repo, how it is entered, what data it holds, what it depends on, and where trust changes. Produced by an anti-dark-code pass 02 (architecture map) on 2026-07-02. Confidence labels follow `.claude/skills/anti-dark-code/references/00-conventions.md`.

## 1. System summary

- **What this repo is:** CUETools 2.2.6, a Windows desktop suite for lossless audio CD ripping, CUE-sheet-accurate image conversion, and rip verification. Snapshot of upstream `gchudov/cuetools.net`. (verified: `README.md`, `CUETools\Properties\AssemblyInfo.cs:32`)
- **Main runtime model:** desktop GUI apps plus CLI tools plus one plugin hosted inside a third-party process (EAC). No services, no databases beyond local files.
- **Main data stores:** local INI-style profile files, a local XML rip database, audio files and CUE sheets on disk.
- **Main external dependencies:** AccurateRip, CTDB (db.cuetools.net), MusicBrainz, freedb, cuetools.net MOTD. All over plain HTTP. (verified: grep for `http://` and `HttpWebRequest`)
- **Main high-risk domains:** untrusted network payload parsing, untrusted file and archive parsing in `unsafe` code, SCSI device I/O, unverified plugin loading, MSI installer.
- **Likely non-obvious live entrypoints:** EAC plugin (runs inside EAC), `collect_files.bat` (release packaging), GitHub Actions workflows, Icecast streaming output. (verified: repo files)

## 2. Runtime units and entrypoints

| Unit | What it does | Where it lives | Framework | Confidence |
| --- | --- | --- | --- | --- |
| CUETools (GUI) | Main conversion/verification app, WinForms | `CUETools/` (`frmCUETools.cs`, 131 KB) | .NET FW 4.7 | verified |
| CUERipper (GUI) | CD ripper, WinForms | `CUERipper/` | .NET FW 4.7 | verified |
| CUERipper.WPF | Single-window WPF stub; abandoned experiment | `CUERipper.WPF/` (only `Window1.xaml`) | .NET FW 4.7 | verified |
| CUEPlayer (GUI) | Audio player, WinForms + typed DataSet | `CUEPlayer/` | .NET FW 4.7 | verified |
| BLUTools / eac3ui (GUI) | Blu-ray audio demux UI over eac3to | `CUETools.eac3ui/` | .NET FW 4.7 | verified |
| CLI tools | Converter, ARCUE, ConsoleRipper, Flake, ALACEnc, LossyWAV, FLACCL.cmd, FlaCuda, eac3to, CTDB.Converter, ChaptersToCue | one dir each | mixed (old and SDK-style csproj) | verified |
| CTDB EAC plugin | Submits/fetches rip verification data inside EAC | `CUETools.CTDB.EACPlugin/` | .NET FW **2.0** (`csproj:13`) | verified |
| EAC plugin installer | MSI via Visual Studio Installer Projects | `CUETools.CTDB.EACPlugin.Installer/*.vdproj` | vdproj | verified |

Core libraries: `CUETools.Processor` (engine; `CUESheet.cs` is a 212 KB god class), `CUETools.Codecs` (+ ~15 codec plugin projects), `CUETools.CDImage`, `CUETools.AccurateRip`, `CUETools.CTDB(.Types)`, `CUETools.Parity` / `CDRepair` / `CLParity` (Reed-Solomon repair), `CUETools.Ripper(.SCSI)` + `Bwg.Scsi/Hardware/Logging` (drive I/O), `CUETools.Compression(.Rar/.Zip)`, `CUETools.DSP.*`, `MusicBrainz`, `Freedb`, `CUEControls`, `ProgressODoom`.

Native and GPU: `ttalib-1.1` (C++), `CUETools.Codecs.TTA` (C++/CLI), `CUETools.AVX` (C++), FLACCL (OpenCL via OpenCLNet), plus ThirdParty native codecs (libFLAC, MAC_SDK, libwavpack) built from submodules with local patches. The FlaCuda CUDA projects are orphaned: absent from `CUETools.sln`, referencing `CUDA.NET` from outside the repo (verified 2026-07-02).

## 3. Plugin discovery (hidden control surface)

`CUETools.Processor\CUEProcessorPlugins.cs:35-64`: at startup, every `CUETools.*.dll` in `<exe>/plugins` and `<exe>/plugins/<arch>` is reflection-loaded and instantiated. No signature or origin check. Load failures are swallowed to `Trace`. Arch detection includes a `Mono.Runtime` probe (historic mono support). (verified)

## 4. Data stores

| Store | What | Evidence |
| --- | --- | --- |
| Profile settings | custom INI-style files via `SettingsReader/Writer`, profile dir from `SettingsShared.GetProfileDir` | `CUETools.Processor\Settings\` (verified). Same source files copy-duplicated into the EAC plugin project (verified) |
| Local rip DB | `CUEToolsLocalDB(.Entry)` XML cache of verified rips | `CUETools.Processor\CUEToolsLocalDB.cs` (verified; format details not read: inferred) |
| Audio + CUE files | primary inputs/outputs, including inside RAR/Zip archives without unpacking | `CUETools.Compression.*`, `ArchiveFileAbstraction.cs` (verified) |

## 5. External dependencies (all out-of-repo control surfaces)

| System | Purpose | Transport | Evidence |
| --- | --- | --- | --- |
| AccurateRip | rip checksum verification, drive offset DB | plain HTTP, hardcoded | `CUETools.AccurateRip\AccurateRip.cs:829,1230` |
| CTDB (db.cuetools.net) | verification + parity repair + metadata; upload via `submit2.php` | plain HTTP, `"http://" + server` hardcoded | `CUETools.CTDB\CUEToolsDB.cs:74,346` |
| MusicBrainz | metadata lookup | HTTP, ancient client lib | `MusicBrainz\MusicBrainzObject.cs:393` |
| freedb (gnudb mirror) | metadata lookup; default host is `gnudb.gnudb.org`, the live community mirror of the shut-down freedb service | HTTP text protocol | `Freedb\FreedbHelper.cs:33,513-648` (verified) |
| cuetools.net MOTD | fetches and displays remote image + text in main GUI | plain HTTP | `CUETools\frmCUETools.cs:654,699` |
| Icecast | streaming audio output | HTTP PUT/SOURCE | `CUETools.Codecs.Icecast\IcecastWriter.cs` |
| GitHub Actions | CI + release build, VS2022 Enterprise `devenv.com` paths | out-of-repo runner | `.github/workflows/*.yml` |

All HTTP clients are synchronous `HttpWebRequest`. There is no `async`/`await` anywhere in first-party code (verified: repo-wide grep).

## 6. Trust boundaries and privilege edges

| Boundary | Risk | Evidence |
| --- | --- | --- |
| Network XML/binary -> parsers | CTDB XML deserialized via checked-in generated serializer (`GeneratedAssembly.cs`, 95 KB); AccurateRip binary parsing; all over plain HTTP so responses are spoofable in transit | verified |
| Remote MOTD image -> GUI render | remote-controlled image bytes rendered in-process from plain HTTP | `frmCUETools.cs:654` verified |
| Untrusted audio/archives -> `unsafe` decoders | 159 `unsafe` occurrences in 30+ files; RAR via `unrar.dll`, Zip via bundled old SharpZipLib | verified |
| App -> CD drive | raw SCSI pass-through via P/Invoke (`Bwg.Scsi\Device.cs`, 131 KB; 160 `DllImport` repo-wide) | verified |
| plugins dir -> app process | arbitrary matching DLLs loaded and instantiated | verified |
| EAC process -> plugin | plugin runs with EAC's privileges, ships `Interop.HelperFunctionsLib.dll` binary | verified |
| CI -> release artifact | release zip assembled by `collect_files.bat`, which scrapes the version from `CUESheet.cs` source text | verified |

## 7. Build, release, and version truth

- Solution: `CUETools.sln`, ~60 projects, configs for Any CPU / x64 / Win32. Mixed old-style csproj (.NET FW 4.7) and a few SDK-style projects. (verified)
- Git: local `master` tracks `https://github.com/LynxTWO/cuetools_2026` (a full mirror of upstream `gchudov/cuetools.net` history), reconnected 2026-07-02 at upstream HEAD `e90b1a86`. ThirdParty submodules (flac 1507800d, taglib-sharp b5ae84f2, openclnet 4a10612b, WavPack 4827b988, WindowsMediaLib 46d46042) are restored at their pinned commits, and all five `ThirdParty/*.patch` files apply cleanly. (verified)
- Prebuilt binaries vendored in `ThirdParty/`: `CSScriptLibrary.v1.1.dll` (2.3.2), `CUDA.NET.dll` (2.3.7), `Freedb.dll` (1.0.0.1), `MusicBrainz.dll` (0.0.0.0), `ICSharpCode.SharpZipLib.dll` (0.85.5) — all last committed 2010-02-08 — plus `Win32/` and `x64/` holding `hdcd.dll` (2010, no version info), `libmp3lame.dll` 3.100 (2021), and `unrar.dll` 6.11 (2022). ffmpeg DLLs are not vendored; `build_ffmpeg_dlls.yml` builds them on demand. Note `Freedb.dll` and `MusicBrainz.dll` shadow same-named source projects in this repo. (verified)
- Version truth is split: `AssemblyVersion 2.2.6.0` in `CUETools\Properties\AssemblyInfo.cs` vs `CUEToolsVersion` const in `CUESheet.cs` scraped by `collect_files.bat`. (verified)
- CI builds three configurations with `devenv.com` from a hardcoded VS Enterprise path and **never runs the test projects**. Tests exist (MSTest: TestCodecs, TestParity, TestProcessor, TestRipper). (verified)

## 8. Known gaps and rough edges

- `CUESheet.cs` (212 KB), `frmCUETools.cs` (131 KB), `Device.cs` (131 KB), `MediaSlider.cs` (182 KB) concentrate most behavior; nothing at that size has test coverage in CI.
- `CUERipper.WPF` is a dead stub next to the live WinForms CUERipper.
- freedb integration targets a service that no longer exists upstream.
- EAC plugin still targets .NET Framework 2.0.
- See `docs/unknowns/architecture-pass.md` for open questions.
