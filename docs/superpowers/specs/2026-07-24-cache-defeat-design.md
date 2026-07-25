# Gentle sized cache defeat - design + validation

Date: 2026-07-24
Status: apply-mechanism opt-in (proven); promote-to-default is a follow-up
Scope: CUETools.Ripper.SCSI (CDDriveReader probe + FlushCache), CUETools.Wpf (DriveCalibration,
DriveCalibrationService, RipService). Built measure-first, interactively with the drive.

## Plain English

Many drives (the ASUS BW-16D1HT here) cache re-reads: the second read of a window comes from the
drive cache, identical to the first by construction. That defeats Secure mode's own error detection
- the two "passes" agree because the second is just the cached first, so a single-read error is
accepted during the rip. (AccurateRip still catches it at the end, but not on a disc AR has never
seen - which is exactly what verify history is for.) Cache defeat forces the second read to hit the
platter so the comparison is real.

An earlier attempt (a 6.5 MB flush before every window) was reverted: too big and it destabilized
the drive into INVALID FIELD IN CDB. This is the gentle version - the flush is sized minimally per
drive, bounded strictly inside the audio program, and proven on a full rip.

## FUA ruled out first

The clean mechanism would be Force Unit Access (bypass the cache on the read command). But FUA lives
only on the data path (`Device.Read` = READ(10)/(12), 2048-byte sectors). The audio read commands -
READ CD (0xBE) and READ CD-DA (0xD8) - carry no FUA bit, and a raw audio track cannot be read
through READ(10). So a rip's reads cannot carry FUA. Cache defeat has to be a flush.

## Measure first: the flush-size probe

A read-only calibration probe (extends the existing cache-behaviour probe): once it confirms the
drive caches, it doubles-then-binary-searches the SMALLEST flush that still evicts the target (warm
the target, flush N bytes, re-read; the re-read hits media speed once N reaches the drive cache),
plus a 512 KB margin. Refinements, all owner-driven:

- Each "does size S evict?" test repeats 3x and requires ALL three to read at media speed. Unanimity
  errs conservative: one cached blip reads as "not evicting", so the search moves to a LARGER flush,
  never a smaller one. Guards against timing jitter / erratic caching.
- The search runs at 3 speeds (fastest / middle / slowest) and takes the LARGEST size - cache
  eviction can differ by speed, so one stored size must cover all.
- Every flush read stays strictly inside the audio program; a past-end read is what throws INVALID
  FIELD IN CDB.
- Also extended the min-speed probe to test SUB-1x rungs (down to 0.25x / 44 kB/s) for an even lower
  slow-to-floor.

Result stored in DriveCalibration as `CacheDefeat = "Flush:<bytes>"` (Confirmed). Measured on the
ASUS: `flushEvict=786432` (768 KB) - ~8.5x smaller than the reverted 6.5 MB - identical across 4
runs and across the 3 speeds (zero variance). Min speed: `44 kB/s` (0.25x), 4x lower than the 1x
the old probe stopped at.

## The apply-mechanism

`CDDriveReader.SetCacheDefeat(flushBytes)` + `FlushCache()`: before a secure re-read (pass >= 1),
read the calibrated size from an unrelated in-program region into scratch, evicting the current
window so the re-read hits media. Scratch-only - it can recover error detection but can never touch
the audio.

Gated (opt-in proving phase): RipService turns it on only when Deep recovery is on AND the drive is
calibrated as caching (`Flush:<N>`), using the drive's OWN calibrated N (self-sizes for any drive,
not a hardcode).

## Validation (2026-07-24)

Clean Genesis disc, Deep recovery on so cache defeat engaged (`cache defeat on: flush 786432B before
each secure re-read`). Full verify, ~125 windows each flushed before its secure re-read:

- STABLE: completed with zero INVALID FIELD IN CDB / no destabilization. The exact per-window flush
  pattern that wedged the drive with 6.5 MB sails through at 768 KB, in-program-bounded.
- BIT-EXACT: `accurate=True`, AR `107/424`, CTDB `114/544` - IDENTICAL to the same disc verified with
  cache defeat OFF. Identical AccurateRip CRCs = byte-identical audio. The flush recovers error
  detection without altering output.
- Cost: 369 s vs ~320 s off = ~15% slower for the per-window flushes.

The owner "opt-in first, prove before default" gate is passed.

## Persistence + portability

Calibration is keyed by drive identity (model + firmware), stored locally, loaded on detect, re-run
only on an explicit Calibrate. So unplug/replug on the same machine skips recalibration. Read offset
is already cross-machine portable via AccurateRip's public drive-offset DB. The cache/flush/speed
characteristics have no public community DB (EAC and dBpoweramp keep proprietary ones); a community
characteristics DB would need a backend, and export/import of the calibration file is the cheap
cross-machine bridge. Recalibration is a ~30 s one-click action, so this is low priority.

## Follow-ups

- Damaged-disc stress test (error-recovery re-reads + slow-to-floor + cache-defeat flushes together).
- Promote to default for Secure/Paranoid on a caching drive (decouple from Deep recovery) once
  proven on more drives/discs.
- Surface the probed Min Drive Speed in the calibration panel.
- Community drive-characteristics DB (needs a backend) or export/import for cross-machine.
