# Unknowns: logging audit

Pass 04, 2026-07-02. Entry shape from `.claude/skills/anti-dark-code/references/00-conventions.md`.

## Entries

### ProxyPassword storage format at rest

- **Area or file:** `CUETools.Processor\CUEConfigAdvanced.cs:76`, `CUETools.Processor\Settings\SettingsWriter.cs`
- **Concern:** `ProxyPassword` is a plain string property; the settings writer serializes config to an INI-style profile file. It appears to be written in cleartext, but exactly which properties SettingsWriter persists (and whether ProxyPassword is among them) was not traced field by field.
- **Why it matters:** a cleartext proxy credential on disk is readable by any process running as the user; also ends up in backups.
- **Evidence found so far:** property is public with `[DefaultValue("")]`; used at `CUEConfig.cs:559` to build `NetworkCredential`. Persistence path not fully traced.
- **Confidence:** inferred
- **Likely owner:** repo owner
- **Next best check:** read `SettingsWriter.Save` and the CUEConfigAdvanced save/load to confirm ProxyPassword is written, then decide DPAPI vs Credential Manager vs documenting the exposure.
- **Risk level:** medium
- **Status:** open

### Bwg.Scsi\Device.cs log verbosity

- **Area or file:** `Bwg.Scsi\Device.cs` (~89 log calls), `Bwg.Logging`
- **Concern:** sampled, not fully read. Confirmed to log SCSI commands/sense data (not personal data), but whether any path dumps large raw read buffers to a sink was not exhaustively checked.
- **Why it matters:** raw buffer dumps would bloat logs and could, in principle, include disc-identifying data; low but unverified.
- **Evidence found so far:** grep count and spot reads.
- **Confidence:** inferred
- **Likely owner:** repo owner
- **Next best check:** read the log calls in the read/correct paths of `Device.cs` and `SCSIDrive.cs` for buffer-dumping.
- **Risk level:** low
- **Status:** open

## Closed items

(none yet)
