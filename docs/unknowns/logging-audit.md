# Unknowns: Logging Audit

Current-state refresh: 2026-07-30.

## Entries

### CUEPlayer settings persistence and Icecast migration

- **Area or file:** `CUEPlayer/IcecastCredentialStore.cs`,
  `CUEPlayer/Properties/Settings.settings`
- **Concern:** source ordering requests a protected DPAPI save before clearing a
  legacy plaintext password, but real `ApplicationSettingsBase.Save()`
  persistence/reopen and migration have not been exercised against an isolated
  user-config file.
- **Why it matters:** source ordering alone cannot prove the protected blob
  reaches disk or that a failed save leaves the previous on-disk configuration
  recoverable.
- **Evidence found so far:** DPAPI bounds and call ordering were inspected;
  source handlers report failure and avoid redisplaying a stored password.
  Failure injection now proves a rejected save restores both the live settings
  object and the prior in-memory DPAPI blob, so active stream configuration does
  not drift after "not saved."
  The separate Icecast 2.5.0 source/auth/metadata smoke passed, but it did not
  exercise `ApplicationSettingsBase` migration and therefore does not close
  this persistence-specific entry.
- **Confidence:** unknown
- **Likely owner:** CUEPlayer maintainer
- **Next best check:** run migration against an isolated real user-config file,
  reopen the application, and verify plaintext removal plus DPAPI recovery from
  the persisted file.
- **Risk level:** medium
- **Status:** open

### Bwg SCSI log verbosity

- **Area or file:** `Bwg.Scsi/Device.cs`, `Bwg.Logging/`,
  `CUETools.Ripper.SCSI/SCSIDrive.cs`
- **Concern:** the many opt-in device log calls were sampled rather than read
  exhaustively. Commands, sense data, sector counts, and drive state are
  expected; raw audio-buffer dumping has not been fully ruled out.
- **Why it matters:** raw dumps could make logs unexpectedly large and expose
  disc content or identifying data.
- **Evidence found so far:** grep inventory and representative reads found
  structural SCSI diagnostics, not credentials.
- **Confidence:** inferred
- **Likely owner:** ripper/SCSI maintainer
- **Next best check:** classify every read/correct-path log call as command,
  device identity, count/timing, sense payload, path, or raw audio; add an
  explicit no-audio-buffer rule/test where practical.
- **Risk level:** low
- **Status:** open

## Closed items

- **CUERipper CTDB contribution ignored consent settings:** closed 2026-07-30.
  The rip-success path now crosses `CtdbSubmissionPolicy`, the shared default is
  off, and an enabled `CTDBAsk` preference displays the submitted field classes
  and plaintext-HTTP warning before calling `CTDB.Submit`. Verification, repair,
  and metadata lookup do not enable contribution.
- **Hosted test-tool telemetry:** closed 2026-07-30. All checked GitHub Actions
  workflows set both the .NET CLI and Microsoft Testing Platform telemetry
  opt-outs. The transitive MSTest telemetry package is not part of the shipped
  application runtime.
- **Classic ProxyPassword plaintext at rest:** closed 2026-07-26.
  `CUEConfig.Save` clears the plaintext property while serializing Advanced
  settings and stores only a DPAPI CurrentUser `ProxyPasswordProtected` value.
  Load rejects corrupt, wrong-user, and unsupported-platform blobs. CUETools and
  CUERipper show save failure rather than silently dropping the credential.
- **CUETools 2026 proxy plaintext/migration:** closed 2026-07-26.
  `SettingsStore` uses DPAPI CurrentUser, migrates legacy plaintext, registers
  the in-memory secret for log redaction, and publishes settings through the
  same-directory staging writer.
- **CUETools 2026 custom-path exception redaction:** closed 2026-07-26.
  Rip, Test & Copy, verify, repair, accept-anyway, and staging-cleanup boundaries
  register raw and normalized user/generated paths before work. `DiagnosticLog`
  applies case-insensitive longest-match replacement in one pass over original
  text. `DiagnosticLogTests` verifies direct messages, nested exceptions,
  synthetic stack text, overlapping values, and replacement-token safety against
  an isolated real log file.
- **CUEPlayer Icecast plaintext settings:** closed 2026-07-26.
  `IcecastCredentialStore` implements a bounded DPAPI CurrentUser blob and
  source-level ordering that requests protected save before clearing legacy
  plaintext. Failed-save tests restore both live settings and the prior
  in-memory protected blob. Real `ApplicationSettingsBase.Save()` persistence/
  reopen and migration remain an integration gap, so this closure is limited to
  the former source-level plaintext design and in-process rollback behavior.
- **Classic MOTD disk cache:** closed 2026-07-26. The live path displays bounded
  HTTPS text; the remote image/text cache path was removed.
