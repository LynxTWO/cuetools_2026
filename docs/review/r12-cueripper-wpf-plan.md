# R12: CUERipper, rebuilt as a first-class WPF / .NET 8 app

Owner decisions (2026-07-10): front-end is **WPF on net8.0-windows**; the first vertical
slice is **CUERipper (rip -> encode -> verify)**. Recorded in `decisions-needed.md`.

## The idea in plain English

The whole engine already works and is reusable. The rebuild is a new user interface on
top of the existing libraries, not a rewrite of the logic. The GUI map (2026-07-10)
showed every non-UI library already builds as `netstandard2.0` and everything the UI needs
funnels through one event-driven class, `CUESheet`. So this is a presentation-layer
project: reference the engine, drive it through `CUESheet`, and re-express the WinForms
screens as XAML plus view models.

## What we reuse unchanged

- `CUETools.Processor` (`CUESheet`, `CUEConfig`) - the façade the UI drives.
- `CUETools.Codecs` (+ codec plugins), `CUETools.CDImage`, `CUETools.AccurateRip`,
  `CUETools.CTDB(.Types)`, `CUETools.Compression`, `CUETools.Ripper` (the `ICDRipper`
  interface). All already `netstandard2.0`.
- The `CUESheet` event model maps straight onto WPF: `CUEToolsProgress` ->
  `IProgress<T>`/progress bar, `PasswordRequired` -> a dialog service, `CUEToolsSelection`
  -> a metadata-choice dialog. Ripper pipeline is `new CUESheet(config)` -> `OpenCD(reader)`
  -> `UseCUEToolsDB(...)` -> `Go()` on a worker, with `CTDB.Submit(...)` after.

## The one real risk: the SCSI ripping stack on .NET 8

`CUETools.Ripper.SCSI` (`CDDriveReader`), `Bwg.Scsi`, and `Bwg.Logging` are Windows
`DeviceIoControl` SCSI-passthrough P/Invoke, targeting `net47;net20` only. They must run on
`net8.0-windows`. P/Invoke is fully supported on .NET 8, so this is expected to be a
retarget-and-fix-gaps job, not a rewrite - but it is unproven, so we prove it first.
`CUETools.Processor` also references old-style `Freedb` (net47 WinForms) and uses
`System.Drawing` for album art (Windows-only on .NET 6+, needs the compat package). Those
are the known integration snags to clear.

## Phases

1. **net8 foundation (de-risk first).** Add `net8.0-windows` to the ripping stack
   (`Bwg.Logging`, `Bwg.Scsi`, `CUETools.Ripper.SCSI`) and confirm the engine chain builds
   for net8. Clear the Freedb / System.Drawing snags. Deliver a tiny **net8 console smoke
   test** that enumerates CD drives through `ICDRipper` - the owner runs it against the
   ASUS Blu-ray on K: to prove SCSI passthrough works on .NET 8 before any UI is built.
2. **WPF scaffold.** New SDK-style `net8.0-windows` WPF project, MVVM base (view-model +
   relay-command + a dialog/progress service), DI, app shell, referencing the engine.
3. **Ripper vertical slice.** Drive-selection VM; metadata lookup (CTDB + AccurateRip +
   Freedb/gnudb fallback); editable track list; secure rip + encode + AccurateRip/CTDB
   verify driven through `CUESheet` on a worker; live progress, log, pause/cancel; the
   metadata-choice and error surfaces. This is the end-to-end "rip a CD" story.
4. **Settings + polish + packaging.** Options screen bound to `CUEConfig`; output-format
   picker over the codec matrix (ties into R13); theming; `collect_files` + CI wiring.

## Verification split

- **Here (headless Build-Tools box):** everything compiles (`dotnet build` net8.0-windows),
  view-model logic unit-tests, drive enumeration code paths.
- **Owner's box (5950X, ASUS Blu-ray on K:):** the actual rip/encode/verify against a real
  disc, since ripping needs hardware and a CD.

## Status

- [x] **Phase 1 (net8 foundation) - build proven 2026-07-10.** Added `net8.0-windows`
  to `Bwg.Logging`, `Bwg.Scsi`, `CUETools.Ripper.SCSI` (additive; net47/net20 untouched).
  The whole graph builds for net8 with 0 errors - the SCSI stack uses only raw
  `kernel32`/`ntdll` P/Invoke, no net8-hostile APIs. `DriveProbe/` is a net8 console that
  enumerates drives via `CDDrivesList.DrivesAvailable()` and opens each with
  `CDDriveReader` (real SCSI INQUIRY + ReadTOC). It runs on .NET 8.0.28 here; this
  sandbox exposes no optical drive, so the live SCSI read against the BD-RE on K: is the
  owner's step: `dotnet DriveProbe/bin/Release/net8.0-windows/DriveProbe.dll` with an
  audio CD inserted.
- [ ] Phase 2: WPF scaffold
- [ ] Phase 3: ripper vertical slice
- [ ] Phase 4: settings, polish, packaging, CI
