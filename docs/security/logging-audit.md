# Logging, Telemetry, and Sensitive-Data Audit

Current-state refresh: 2026-07-30. Scope is first-party logging, local
diagnostics, credential persistence, and credential-bearing network paths.
Vendored/submodule implementations are outside the code-level audit. Evidence is
labeled `verified`, `inferred`, or `unknown`.

## 1. Current telemetry model

No external analytics, crash-reporting, tracing, or telemetry SDK was found in
the shipped first-party application runtime. Classic applications use `Trace`,
`Console`, and the opt-in `Bwg.Logging` framework. CUETools 2026 adds an
intentional local diagnostic log and global exception handlers.

The MSTest toolchain transitively carries Microsoft Testing Platform telemetry
components. This is a build/test dependency, not an application runtime client.
All checked GitHub Actions workflows now set
`DOTNET_CLI_TELEMETRY_OPTOUT=1` and
`TESTINGPLATFORM_TELEMETRY_OPTOUT=1`.

`CUETools.Wpf/Services/DiagnosticLog.cs` creates one file per run at:

`%AppData%\CUETools2026\logs\cuetools-<timestamp>.log`

It records phases, counts, timing, drive/rip structure, and exception details.
Writes are thread-safe and failures are swallowed so logging cannot break a rip.
No automatic upload path was found. Retention/purge behavior is not claimed.

## 2. Findings

| # | Area | Current behavior | Sensitive-data assessment | State |
| --- | --- | --- | --- | --- |
| F1 | Classic proxy settings | `CUEConfig.Save` removes `advanced.ProxyPassword` from serialized Advanced JSON and stores a DPAPI CurrentUser `ProxyPasswordProtected` value; corrupt/wrong-user/unsupported blobs are rejected | verified: the prior plaintext-at-rest finding is closed; no plaintext fallback | fixed |
| F2 | CUETools 2026 proxy settings | `SettingsStore` uses `SecretProtector` and `WpfProxyPasswordProtected`, migrates legacy plaintext, registers the in-memory secret with the diagnostic redactor, and atomically publishes settings | verified: credential read rejection fails closed; save failure is logged/caught, but UI visibility is not established | fixed with UI-observability limit |
| F3 | CUEPlayer Icecast settings | `IcecastCredentialStore` implements a bounded DPAPI CurrentUser blob; source ordering attempts protected persistence before clearing legacy plaintext, and UI set/clear semantics do not redisplay the stored secret | verified source invariant; real `ApplicationSettingsBase.Save()` persistence, failure, and migration behavior remain unobserved | implemented, integration gap |
| F4 | Icecast network auth | endpoint policy defaults source and metadata requests to HTTPS; HTTP needs explicit persisted opt-in and a UI warning; trace failures record exception type, not credential value | verified control plus disposable Icecast 2.5.0 source/auth rejection, metadata, listener-byte, flush/close, and teardown smoke; HTTPS certificate and Mono interoperability remain unknown | bounded external gap |
| F5 | CUETools 2026 diagnostic log | registers username, profile/music roots, proxy password, album metadata, user-selected input/output roots, and owned staging paths before work; error records include exception type, message, stack, and inner exceptions | verified: case-insensitive longest-match redaction scrubs direct messages, nested exception messages, and stack text without reprocessing the replacement token | fixed |
| F6 | Plugin discovery traces | records manifest/development-mode/load failures and exception details through `Trace` | paths and error details, not credential values in inspected calls | low |
| F7 | `Bwg.Scsi/Device.cs` and `Bwg.Logging` | emits SCSI command, sense, drive, and sector diagnostics when enabled | sampled only; no credential data found, but raw-buffer verbosity is not exhaustively ruled out | open low-risk audit |
| F8 | CLI and classic GUI traces | progress, drive selection, offsets, file/playlist errors, and exception types | paths and device information may be user-identifying in shared logs; no secret values found in inspected call sites | local disclosure boundary |
| F9 | Classic MOTD | bounded HTTPS text is held in memory for display; former remote JPEG/text cache is gone | remote input, not telemetry; prior disk-cache/render finding is closed | fixed |
| F10 | CUERipper CTDB contribution | successful rips previously called `CTDB.Submit` unconditionally, ignoring both advanced submission preferences | disc layout, checksums/parity, drive name, pseudonymous machine-derived ID, barcode, artist, and title were sent over plaintext HTTP | fixed: contribution defaults off, uses one shared policy boundary, and an enabled ask preference displays a detailed yes/no disclosure |
| F11 | Release/test telemetry | MSTest dependencies include Microsoft Testing Platform telemetry components | build-runner environment and test usage, not end-user audio or application runtime data | fixed for checked hosted workflows with both documented opt-out variables |

## 3. CUETools 2026 diagnostic content

Verified structural categories include:

- process/runtime and font initialization;
- drive model, firmware, capabilities, speeds, cache behavior, and tray actions;
- rip mode, offset, read windows, C2/error counts, recovery passes, timings, and
  completion state;
- disc/verification identifiers, match counts, and history state;
- settings, encoder catalog, history, transaction, verification, and repair
  outcomes;
- exception type, message, stack trace, and inner-exception chain for error
  calls.

The logger always registers the Windows user name, UserProfile, and MyMusic
paths. Rip, Test & Copy, verify, repair, accept-anyway, and staging cleanup
entrypoints now register user-selected and generated path roots before work that
can log an exception. The focused real-file test covers direct log text, nested
exception messages, case changes, overlapping secrets, and stack text. Future
entrypoints still have to follow the same `IDiagnosticLog.Redact` rule. Drive and
disc identifiers are deliberately structural, but may still be identifying when
a user shares a log. The UI should therefore treat the log as user-approved
diagnostic material, not anonymous telemetry.

## 4. Credential and trust boundaries

- DPAPI CurrentUser protects local proxy and Icecast secrets against casual file
  disclosure. It does not protect against code already running as that Windows
  user, and protected blobs are intentionally not portable between users.
- Unsupported DPAPI platforms do not receive a plaintext fallback.
- Classic CUETools and CUERipper catch protection/publication failures and state
  that settings were not saved.
- CUEPlayer source ordering requests protected persistence before clearing legacy
  plaintext. Real `ApplicationSettingsBase.Save()` success/failure and
  migration persistence have not been exercised.
- Icecast Basic authentication is acceptable only inside the default TLS
  transport. Explicit HTTP opt-in is a conscious disclosure tradeoff.
- No log call should receive a raw password, authorization header, token, or
  protected DPAPI blob.

## 5. Safe logging rules

- Call `IDiagnosticLog.Redact` for every user-selected input/output root and for
  album, artist, track, proxy, or external-encoder value before a code path can
  log exceptions containing it.
- Prefer exception type and a bounded operational message. Use full exception
  text only in the local diagnostic logger, where redaction is applied.
- Never log `ProxyPassword`, Icecast source passwords, RAR passwords, raw
  `Authorization` values, DPAPI blobs, or full request/response bodies.
- Keep SCSI diagnostics opt-in; do not add raw audio-sector dumps.
- Treat file paths, drive model/serial-like data, disc IDs, and music metadata as
  user-identifying even when they are not authentication secrets.
- Any future remote diagnostics must add explicit consent, destination,
  retention, and schema review. The current audit does not authorize upload.
- Ripping, verification, repair, metadata lookup, and CTDB contribution are
  separate purposes. Never infer submission consent from use of the first four.

## 6. Coverage and limits

Verified: first-party credential save/load paths; classic `Trace` call sites
around credentials and plugin loading; CUETools 2026 diagnostic implementation,
job-boundary registration, and nested-exception redaction test; current MOTD and
Icecast policy; all first-party CTDB submission call sites; and telemetry
opt-outs in every checked GitHub Actions workflow.

Sampled, not exhaustive: all 89-style `Bwg.Scsi/Device.cs` log calls and every
possible exception message from native/external components.

External/unknown: Icecast HTTPS certificate and Mono behavior, hosted runner
logs, and any logging performed internally by vendored native/managed
dependencies. Local Icecast 2.5.0 auth/source/metadata/listener/teardown behavior
has been observed.

Open questions and closed historical findings are maintained in
`docs/unknowns/logging-audit.md`.
