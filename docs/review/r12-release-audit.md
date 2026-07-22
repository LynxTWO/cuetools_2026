# R12 release audit - CUETools 2026 (WPF) v2026.1.0

Date: 2026-07-22. Scope: the whole `CUETools.Wpf` app plus the engine seams it touches, audited
before the first packaged release. Method: three parallel review passes (correctness, silent
failures, release blockers), fuzzing, a privacy pass on the diagnostic log, a writing pass on all
user-facing text, and build/publish verification. Every finding below is fixed and verified unless
marked otherwise.

## Verdict

Ship. All P0/P1 findings fixed and re-verified; the published self-contained build launches, loads
settings, reads a disc, and carries correct version metadata.

## What the audit ran

| Check | Result |
|---|---|
| Correctness review (threading, leaks, hardware events) | 2 important findings, both fixed |
| Silent-failure review (55 catch blocks classified) | 1 hang bug + 3 silent-critical, all fixed |
| Release-blocker scan (paths, placeholders, persistence, branding) | all blockers fixed |
| Fuzz: SCSI parsers + CodecMath, 300k iters | PASS (0 crashes, all outputs bounded) |
| Fuzz: GUI random-walk, 400 steps incl. the new 3D pages | PASS (process healthy) |
| Fuzz: Settings toggle sweep, 4096 combinations | PASS |
| Privacy: DiagnosticLog scrub coverage | PASS (see caveat below) |
| Writing: em dashes / typographic Unicode / AI tells | PASS (zero found) |
| Release build warnings | nullable-annotation noise only, key ones inspected benign |

## Fixed: the P0s

- **DiagnosticLog scrub hang.** `Scrub` restarted its search from 0 after each replacement, so a
  registered secret matching inside the `<redacted>` token itself (the artist "Red" is a real
  case) looped forever, hanging whichever thread logged next - mid-rip. The search now resumes
  after the inserted token. (`DiagnosticLog.cs`)
- **Chosen release metadata silently dropped.** `CopyMetadata` failure now logs and warns in the
  status line instead of ripping with wrong tags unannounced. (`RipService.cs`)
- **Verify results misreported as "not in database".** An AccurateRip/CTDB result-read crash now
  logs as a failed check instead of silently rendering as a clean miss; the CTDB entries walk that
  gates the Repair button logs too. `VerifyService` now has the diagnostic log injected.
- **History store hardening.** Write failures log; a corrupt `history.json` is kept as `.bak`
  instead of being silently overwritten; the app-data dir create is guarded (it runs during DI,
  before the startup retry loop could protect it). (`HistoryStore.cs`)

## Fixed: important correctness

- **Stuck-busy Rip page.** `ReadDiscAsync` had no try/finally around `IsBusy`; a drive query throw
  (drive letter vanishing) left Rip/Verify/Eject disabled forever with the tray watcher parked.
  Now guarded, and `GetDriveDetails`/`GetTrayState` fail safe (invalid details / Unknown) instead
  of throwing into the UI. (`RipViewModel.cs`, `DriveService.cs`)
- **Theme-event leak.** `MainWindow` subscribed to the singleton `ThemeService.Changed` and never
  detached, pinning any window the startup retry loop discarded. Detached on `Closed`.
- **Rip vs tray-poll device race.** The rip's drive open now takes the same app-wide SCSI gate the
  tray poller and capability queries use (held only for the open, not the rip).
- **Invisible lingering process.** If all six window-show retries fail the app now exits with
  code 1 instead of running forever with no window.
- Smaller: read-offset lookup failure logs (was indistinguishable from a real zero-offset drive);
  keep-awake rejection logs; tray unlock failure logs; cover-art inject builds the picture before
  clearing the lists (a throw mid-way could strip ALL art); cover resize failure now says
  "database cover will be embedded" instead of letting the preview overpromise; open-folder on a
  deleted folder reports instead of no-op; theme save failure logs.

## Fixed: release blockers

- **Settings persistence.** New `SettingsStore` persists the ENGINE config (via the engine's own
  `CUEConfig.Save/Load`, all ~90 options, the same profile format classic CUETools uses) plus the
  app prefs (keep-awake, lock-tray, stop-on-unrecoverable, output folder, format, correction
  quality) to `%AppData%\CUETools2026\settings.txt`. Loaded at startup, saved on exit. Verified
  round-trip on both the dev build and the published exe: first run logs "no saved settings",
  graceful exit writes 348 keys, relaunch logs "settings loaded".
- **Honest Settings page.** Removed the Single instance and Check for updates switches (this app
  implements neither - a switch that does nothing is a lie); implemented Eject after rip for real
  (tray opens after a successful rip); replaced the user-visible "settings persistence are the
  next increments" admission with "Changes apply immediately and are saved when the app closes."
- **R16 plugin loading.** `Assembly.LoadFrom` fix + probe pass (see the remediation backlog entry)
  - third-party codec DLLs dropped into `plugins\` now genuinely load on .NET 8.
- **Identity.** `app.ico` (the classic cue2 icon), `Version 2026.1.0`, product/company/copyright
  (GPL v3) in the csproj; verified on the published exe's VersionInfo. Stale "Phase 2 shell"
  comments and the dead placeholder page template removed; dev-hardware comment removed.

## Published artifact

`dotnet publish -c Release -r win-x64 --self-contained` to `publish/CUETools2026-win-x64/`
(~167 MB, no .NET install needed on the target). Folder publish on purpose: the engine locates
`plugins\` via `Assembly.Location`, which is empty under single-file extraction - single-file would
break codec discovery. A publish-time target ships `plugins\` (Flake FLAC + ALAC). Verified: the
published exe launches (real window handle), loads settings, reads a disc, saves settings on exit,
and reports `ProductVersion 2026.1.0+<commit>`.

## Accepted / noted, not changed

- The ~40 remaining `catch { }` blocks classified ACCEPTABLE (cosmetic/best-effort, documented).
- Scrub caveat: secrets shorter than 3 chars (artist "U2") are not registered - scrubbing 2-char
  substrings would mangle the log, and structural log lines never embed names.
- CTDB traffic is cleartext HTTP by design until the server answers TLS (tracked as D2, upstream
  issue filed).
- `ReportStore` is session-only by design; the persistent record is `history.json`.
- `DriveCalibration`'s JSON store shares the old silent-overwrite pattern; it is not yet on a
  shipping path (hardware calibration lands with the rip-accuracy program). Flagged for that work.
- unrar 6.11 (D3) stays deferred: hygiene-only per the adversarial pass, swap needs a RAR
  round-trip harness.

## Next phase (owner-requested, after this release)

Lossy codecs with honest per-codec visualizations. In-repo encoders make MP3 (libmp3lame) and WMA
lossy (OS-provided) the wire-able candidates; Opus/AAC-encode need the R13 native-codec program.
The visualizations must draw each codec's REAL pipeline (filterbank/MDCT, psychoacoustic masking,
quantization - where data is discarded - and entropy coding), never the lossless predictor/residual
stages; the codec-visualization skill already carries that honesty rule.
