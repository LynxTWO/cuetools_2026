# Logging, Telemetry, and Sensitive-Data Audit

Pass 04, 2026-07-02. Scope: first-party code. Vendored/submodule trees (ThirdParty/*, taglib-sharp, WindowsMediaLib tests) are excluded and noted. Vocabulary and sensitive-data classes: `.claude/skills/anti-dark-code/references/00-conventions.md`.

## 1. Scope and method

Searched the repo for logging/telemetry sinks (`Trace.*`, `Console.*`, `Debug.*`, `*.WriteLine`, the `Bwg.Logging` framework) and for sensitive tokens (`password`, `token`, `secret`, auth headers, UUID). Read the credential-handling call sites. This is a Windows desktop app: there is **no analytics SDK, no crash reporter, no tracing/telemetry vendor**, so the telemetry surface is small and local. The only outbound network paths are the verification/metadata services (audited as slice S1) and the MOTD fetch (S9); none of them transmit user secrets.

Confidence is high for the managed first-party sinks; `Bwg.Scsi\Device.cs` (89 log calls) was sampled, not read line by line.

## 2. Findings

| # | Area / file | Sink | What it emits | Sensitive? | Risk |
| --- | --- | --- | --- | --- | --- |
| F1 | `CUETools.Processor\CUEConfigAdvanced.cs:76` (`ProxyPassword`) | profile settings file (SettingsWriter), at rest | proxy auth password as a plain serialized string | yes — stored credential | medium |
| F2 | `CUETools.Codecs.Icecast\IcecastWriter.cs:59` | network (HTTP Basic header) | Icecast source password base64 over plain HTTP | yes — credential in transit | medium (commented S11) |
| F3 | `CUETools.Processor\CUEProcessorPlugins.cs` | `Trace.WriteLine` | plugin load exception messages | no (paths/errors), but silent-swallow | low (commented S3) |
| F4 | `Bwg.Scsi\Device.cs` (~89 calls) | `Bwg.Logging` sink (opt-in) | SCSI CDBs, sense data, sector counts | no personal data; verbose device I/O | low |
| F5 | CLI tools (`*/Program.cs`), `SCSIDrive.cs` | `Console.Error` | rip progress, drive/read-command detection, offsets | no | low |
| F6 | `CUETools\frmCUETools.cs` MOTD | disk write + GDI+ render | remote JPEG/text cached under profile dir | remote-controlled input, not a leak | medium (commented S9) |

No sink was found that writes password, token, session, or raw-personal-data **values** into logs, traces, or console output. F1 is data-at-rest, not a log leak.

## 3. Approval status

No protected-area edits attempted in this pass (audit is read-only). F1 (credential at rest) touches the config/secrets area, which is approval-gated; any change to how `ProxyPassword` is stored (DPAPI, Windows Credential Manager, or at minimum documenting the exposure) is queued as a remediation item, not applied here.

## 4. Fixes applied

None. This pass is read-only; no redaction was needed because no secret is being logged.

## 5. Safe logging rules for this repo

- Never log `ProxyPassword`, `IcecastSettingsData.Password`, RAR `Password`, or the raw `Authorization` header value.
- Keep the CTDB submitter id hashed (it already is; `CUEToolsDB.GetUUID`); never log the raw machine identifiers it is derived from.
- SCSI/`Bwg.Logging` output is for device debugging; keep it opt-in and do not add audio sample data or disc-identifying personal metadata to it.
- CLI/console output may include file paths and drive info; do not add credentials or full request/response bodies.
- If a crash reporter or analytics is ever added (modernization), exclude the config object (it carries `ProxyPassword`) from any automatic capture.

## 6. High-risk domain data

None. CUETools handles audio files, disc TOCs, and music metadata. No health/financial/biometric/child data. The only stored credential is the optional proxy password (F1); the only in-transit credential is the optional Icecast source password (F2).

## 7. Unknowns and follow-up

Recorded in `docs/unknowns/logging-audit.md`.

## 8. Coverage note

Covered: managed first-party logging sinks, credential handling, outbound network paths, MOTD. Sampled (not exhaustively read): `Bwg.Scsi\Device.cs`. Excluded: ThirdParty submodules and vendored binaries (they have their own logging; out of scope for first-party audit). No mobile/game/worker telemetry exists in this repo.
