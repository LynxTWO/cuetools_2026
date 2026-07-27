# cuetools_2026 project instructions

Fork of cuetools.net being reviewed and modernized. Anti-dark-code artifacts are split by type:
maps and ledgers live in `docs/architecture/`, logging and telemetry reviews in `docs/security/`,
unresolved evidence in `docs/unknowns/`, and findings, decisions, plans, and remediation history
in `docs/review/`. Read `docs/review/decisions-needed.md` before starting work that needs owner
approval.

The LAME v4 encoder work lives in its own repository (LynxTWO/lame_v4); only plan and
progress documents for it belong here, under `docs/review/`.

## Build and test

- .NET Framework 4.7 solution builds use VS 2022 Build Tools 17.14 or Visual Studio
  Community 2026 18.8, both installed on this workstation. Record the exact host,
  toolset, target framework, configuration, and platform for each receipt; do not
  infer them from whichever `MSBuild.exe` happens to resolve first.
- MSTest v2 is used for the migrated test projects. See `docs/review/` for the
  recorded green baseline and per-project quirks (CUEControls resgen, AccurateRip
  net20 flavor).
- Prefer `dotnet build` per project. Use explicit full-MSBuild or `devenv.com` paths
  for legacy GUI, C++/CLI, and installer targets, and record any target not exercised.
- Run `eng/ci/Prepare-VendorSources.ps1` before restore or build. Four pinned
  submodules are combined with tracked patches under `obj/vendor-sources/current`;
  managed, native, classic, CI, and release consumers must use that staged tree.
  Never apply the patches in place or use a submodule worktree as an intermediate.
  Finish a real build with `eng/ci/Test-VendorSubmodulesClean.ps1`.
- Run classic release clean/build/receipt/collect/validate through
  `eng/release/Invoke-ClassicRelease.ps1`. A clean staging directory does not prove
  fresh compiled inputs. The orchestrator holds one repo-wide lease across recovery,
  exact-leaf cleanup, build, receipt, collection, and publication. Do not bypass it
  through the receipt or collector helpers.
- Classic receipts must account for every consumed native binary and ignored source
  input. They bind the expanded Monkey's Audio tree to its archive, and bind the exact
  Release|x64 and Release|Win32 command logs plus warning-baseline hash before
  collection. A new warning retains the intent and logs and must not publish.
- A retry may archive a source-stale failed build only through
  `Invoke-ClassicRelease.ps1 -ArchiveStalePendingIntent`. The explicit path preserves
  the old intent byte-for-byte before cleanup. Do not delete or rename pending intent
  evidence manually.
- Monkey's Audio is pinned to the official 13.20 SDK archive. Prepare and build it
  through `eng/ci/Build-NativeDependencies.ps1`; the script byte-validates all 423
  archive files plus four hash-pinned CUETools wrapper overrides before building
  the Win32 and x64 outputs. Both the archive project and CUETools wrapper must pin
  v143 explicitly; never inherit `DefaultPlatformToolset`.
- Do not assume that a Windows file handle opened with delete sharing permits its
  parent directory to move. Assurance-bearing publication must revalidate the exact
  destination before releasing its reservation or reporting success.
- CTDB repair is a preservation transaction. It must retain source-derived audio
  basenames, standard and unknown tags, and embedded artwork in the repaired sibling
  copy, reject destination-name collisions before writing, and leave the source set
  byte-for-byte unchanged. Preserve source CDTOC identity, but drop stale
  AccurateRip/CTDB payload-proof tags unless an independent post-repair result
  explicitly regenerates them.
- Drive calibration is a versioned prerequisite. The first Rip, Verify, or Test &
  Copy refreshes missing/stale capability data. Secure and Paranoid operations fail
  closed unless an independent reread strategy is established. Once a drive has
  demonstrated caching, retain the largest proven safe flush size across noisy later
  calibrations. Lead-in/out flags are valid only when the exact offset-sized boundary
  range was probed and the SCSI reader consumes it.
- Multi-drive UI evidence must remain bound to its physical drive. Keep Drive & Read
  selection synchronized with Rip, clear stale identity/calibration on changes, and
  lock selection while an operation owns the hardware. UI observers and progress
  listeners are ancillary; their exceptions must not change calibration, rip, or
  cleanup outcomes.
- Publish immutable named evidence at the end of each completed phase. In Test &
  Copy, Test CRC appears before Copy starts and Copy CRC appears before any
  tie-break; a later phase must not erase a prior role it did not replace.

## Writing rules for all human-facing text

Docs here follow the same voice as the LAME v4 writing guide (`docs/writing-guide.md` in the
lame_v4 repository). The hard rules:

- No em dashes, no en dashes, no typographic Unicode (arrows, checkmarks, unicode minus,
  curly quotes). Use ASCII forms: " - ", "->", "x", "~", "<=", "...".
- Plain-English claim first, then the receipt (the measurement, file, or commit that backs
  it), then engineer detail. Tables for clustered numbers with a lead-in sentence.
- Short sentences, one claim per paragraph, bold only for outcomes and numbers that matter.
- Precise status verbs (measured, verified, inferred, unknown, rejected, pending); never
  upgrade an inferred claim to verified in prose.
- Never alter numbers, flags, commit hashes, paths, or commands when editing prose.
- Do not call the non-engineer reader a "layman"; use "plain English" or "normal reader".
