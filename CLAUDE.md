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
- First-party PackageReference projects are enumerated explicitly in
  `Directory.Build.props` and commit `packages.lock.json`. GitHub's `CI=true`
  enables locked restore. Regenerate locks only with an intentional
  `--force-evaluate`, then run `eng/ci/Test-NuGetLockFiles.ps1` and locked
  solution/WPF/ripper restores. Never generate lock files in `ThirdParty` or
  `obj/vendor-sources`.
- Keep Core and full-MSBuild restore inputs identical. Core-only build assets
  such as `System.Resources.Extensions` and net20 reference assemblies must
  remain declared as `ExcludeAssets=all` restore evidence under full MSBuild;
  do not remove those nodes or allow them into the shipping compile/runtime
  closure. Exercise the real devenv build and prove lock hashes do not change;
  Visual Studio may perform another restore with a different legacy-project
  evaluation after the explicit restore.
- Run classic release clean/build/receipt/collect/validate through
  `eng/release/Invoke-ClassicRelease.ps1`. A clean staging directory does not prove
  fresh compiled inputs. The orchestrator holds one repo-wide lease across recovery,
  exact-leaf cleanup, build, receipt, collection, and publication. Do not bypass it
  through the receipt or collector helpers. The orchestrator uses `/Build` after
  exact-leaf cleanup. Do not change this to parallel `/Rebuild`: project-level clean
  operations race in the shared `bin\Release` output tree.
- Classic receipts must account for every consumed native binary and ignored source
  input. They bind the expanded Monkey's Audio tree to its archive, and bind the exact
  Release|x64 and Release|Win32 command logs plus warning-baseline hash before
  collection. A new warning retains the intent and logs and must not publish.
- A retry may archive a source-stale failed build only through
  `Invoke-ClassicRelease.ps1 -ArchiveStalePendingIntent`. The explicit path preserves
  the old intent byte-for-byte before cleanup, including a superseded command plan
  that is never executed. Same-source recovery and every new intent still require the
  current canonical plan. Do not delete or rename pending intent evidence manually.
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
  explicitly regenerates them. Publication requires a fresh named AccurateRip report,
  a named CTDB repair report, `repair.verify` with source and output SHA-256 proofs,
  and `.cuetools-complete` written last. Revalidate the proofs before the atomic move.
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
- Concurrent optical jobs run in separate process-per-drive windows. Claim the drive
  letter before querying device identity, then hold both the letter and physical-device
  lease for the complete operation. A second claimant for either identity fails before
  touching the hardware. Each window owns its Stop/status state and collision-safe log;
  secondary windows do not publish shared settings. Launch arguments carry only the
  window role and validated drive letter. While a job runs, choosing another drive in
  the active selector launches that isolated window; it must not retarget the current
  window's immutable drive, Stop command, metadata, CRC evidence, or status.
- Publish immutable named evidence at the end of each completed phase. In Test &
  Copy, Test CRC appears before Copy starts and Copy CRC appears before any
  tie-break; a later phase must not erase a prior role it did not replace.
- Keep machine control artifacts (`.cuetools-complete`, `rip.verify`, ownership and
  proof markers) contract-stable. Human-facing cue, rip, AccurateRip, and Test &
  Copy logs carry one sanitized, length-bounded artist/album stem. Repair and
  overwrite discovery must accept legacy `album.*` names, detect the new names by
  type, and reject multiple cue candidates instead of guessing.
- If a Test & Copy confirmation fails after Copy completed, keep the staged Copy in
  an explicit Held state. Do not publish it automatically, but do not delete the only
  completed encoded result. Test & Copy outputs must carry CTDB repair evidence to
  the same source-preserving post-rip repair path as ordinary lossless rips. Matching
  reads with unrecoverable windows are `CONSISTENT`, not cleanly verified; retain
  database presence even when no exact AccurateRip or CTDB match exists.
- READ CD payload medium errors are untrusted media evidence, not proof that the
  drive is dead. Split a failed batch to isolate sectors and feed persistent
  single-sector medium errors into the existing flagged vote and retry policy.
  Transport, removal, not-ready, unit-attention, illegal-command, and hardware
  failures remain fatal. `StopOnUnrecoverable` is applied only after the configured
  evidence and retry policy has classified a sector as unrecoverable.
- An accepted optical-drive control command does not prove the payload path is ready.
  Serialize speed, seek, cache, and mode transitions with READ CD, apply only a
  bounded measured settle, and retry only a transition-bound failure class that has
  real hardware evidence. A repeated or unrelated failure stays fatal. Include
  relative sector, transfer count, command mode, and applied speed in scrubbed
  failure context; never include sector payload bytes.
- A rejected multi-sector READ CD transfer is not damaged-media evidence. The exact
  observed `DeviceFailed / IllegalRequest / 24/00` batch shape may fall back to
  single-sector payload reads. Each child must succeed independently unless the exact
  child repeats `24/00` after one bounded retry; the rejected parent plus two exact
  child command shapes may then mark only that address untrusted. Never consume a
  rejected payload. Every different child failure remains fatal with its exact sector
  and sense context. Count successful batch fallbacks and corroborated pinpoints.
- Cache defeat is complete or explicit. If every unrelated region rejects the
  normal multi-sector `READ CD` shape with exact `IllegalRequest / 24/00`, reduce
  the transfer chunk deterministically down to one sector while preserving the
  requested byte count and in-program range. Only a fully completed eviction
  authorizes the next secure read; every other failure remains fatal and diagnostic.
- Curated command encoders are prepared only through
  `eng/ci/Prepare-ExternalCommandEncoders.ps1` and
  `eng/release/external-command-encoders.json`. The manifest pins source pages,
  archive bytes, selected executable hashes, licenses, and source obligations.
  WPF release and runtime checks repeat the hashes. A receipt-bound user import is
  resolved first and may override the bundled fallback; never replace or trust a
  packaged executable by filename alone.
- Keep executable support separate from redistribution. A working imported codec
  does not authorize packaging. Real CLI execution, license/source compliance,
  runtime dependencies, and patent or notification boundaries must all be recorded.
- Keep the FFmpeg wrapper and native artifact version-locked as one unit. The
  standalone path uses FFmpeg.AutoGen 8.1.0 with FFmpeg 8.1.2#3 from vcpkg commit
  `9e593bb18ea69cc5095e012465dcd675a822ed0d`. Both x64 and x86 must run the
  16/24-bit path/stream/nonzero-seek worker and retain license, port-manifest,
  version, size, and SHA-256 evidence. Pass the same `PlatformTarget` to restore and
  no-restore build, and fail immediately if either command fails. Do not copy this
  unshipped path into either primary artifact without a separate reachability and
  packaging review.
- Signing changes bytes. Apply `eng/release/signing-policy.json` only after both
  artifacts first validate; sign only its contract-selected publisher files;
  require SHA-256 Authenticode plus an RFC 3161 SHA-256 timestamp; regenerate
  plugin hash manifests; revalidate; and generate provenance/SBOMs last. A tag
  or explicit signed dispatch must fail when protected credentials or the
  expected subject are unavailable. An unsigned manual evidence build must say
  `unsigned-evaluation` and is not releaseable.
- Treat `docs/review/remediation-backlog.md` status lines as authoritative. After
  each batch, reconcile the ordering, remaining-work summary, decisions, and
  historical next-step text so a closed R-item does not stay in the active queue.
- Preserve nested SCSI identity. When a batch reports medium error, snapshot each
  failed pinpoint sector before another command overwrites device sense. A pinpoint
  `IllegalRequest / 24/00` may retry once only with that parent medium-error
  corroboration or the exact rejected-batch ancestry above. Use only a successful
  retry; a repeated identical rejection may mark that exact sector untrusted, while
  every different repeat remains fatal. Never report a child failure using its
  parent's sector count or range.
- Keep the Rip page operable at the 1200-pixel default width. Primary actions,
  Test/Copy CRC evidence, and drive selection must remain reachable. Use bounded
  proportional layout and wrapping at supported widths, vertical scrolling for
  rail overflow, and horizontal scrolling instead of clipping below the supported
  work-area minimum. Trim long identity text only with its full value in a tooltip.

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
