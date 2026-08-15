# USB-bridged drives wedge under error-recovery grinding

**Both USB-attached matrix drives have entered a state that rejects every
command with IllegalRequest 24/00 - including READ TOC and the kernel's
own reads - recoverable only by replugging the USB cable. Both incidents
involved the damaged reference disc; the SATA-attached internal drive
survived a full deep-recovery pass of the same disc without wedging.**

Incident receipts, both on the Linux head's SG_IO transport:

- 2026-08-14 morning, HL-DT-ST BD-RE WH16NS40 1.05 (USB): first
  cache-defeat eviction of a verify rejected 24/00 at every chunk size
  down to one sector (30 regions, per-command retries, wake attempted);
  afterward the drive rejected TOC reads and the kernel logged Invalid
  field in cdb for its own READ(10). A USB replug restored it fully; it
  then calibrated, flushed, and verified a clean disc byte-exactly.
- 2026-08-14 evening, ASUS BW-16D1HT 3.11 (USB), during the owner's
  live Paranoid Test & Copy of the damaged disc: the Test read completed
  honestly (three given-up windows, seven suspicious sectors, reread
  peak 61, CTDB differs-112-samples confidence 207); 20 minutes into the
  Copy read, at the disc's final windows and the 44 kB/s recovery floor
  (1,472 corroborated unreadable pinpoints, 91 batch fallbacks by then),
  eviction reads rejected 24/00 at every chunk size; the engine failed
  closed; the drive then rejected TOC reads until replug.

Attribution history, corrected: the morning incident was first blamed on
processes killed mid-command during unrelated experiments. The evening
incident rules that out - it occurred organically in a single healthy
session. The common factors across both incidents are the USB transport
and extended error-recovery activity on the damaged disc; the counter
hypothesis (SATA survives) has one supporting observation, the internal
PLDS completing a 54-minute deep-recovery verify of the same disc.

Confidence: the wedge state and its replug cure are verified twice. The
USB-bridge-degradation cause is inferred from two incidents plus one
SATA control; it is not yet verified. What would sharpen it: a repeat on
either USB drive with a clean disc under equally long grinding (isolates
disc damage from duration), and a wedge on any SATA path (would refute
the bridge hypothesis).

Engine behavior in both incidents was correct: cache defeat is complete
or explicit, the eviction failures were fatal and diagnostic, no partial
output was published, and completed Test evidence was retained. The open
design question - whether any bounded reset/recovery belongs in the
engine, or the honest answer stays "fail closed and tell the user to
reset the drive" - is D11 in decisions-needed.

## Live characterization, third incident (2026-08-14 evening)

The wedge recurred on the ASUS BW-16D1HT after ~24 minutes of Paranoid
Test & Copy grinding on the damaged reference disc, with the app closed
afterward and the drive left untouched. This was the first incident
probed while stuck. All results below are measured, not inferred.

**The stuck state, from the drive's own sense data** (SG_IO to the sg
node, unprivileged):

- INQUIRY answers GOOD, 2 ms.
- TEST UNIT READY, READ TOC (43h), and READ CD (BEh, one sector) all
  return CHECK CONDITION in 4 ms with one identical sense:
  `70 00 05 00 00 00 00 0a 00 00 00 00 24 00 00 00 00 00`
  (current error, IllegalRequest, ASC/ASCQ 24/00 INVALID FIELD IN CDB).
- A TEST UNIT READY has an all-zero CDB; there is no field to be
  invalid. The rejection is a canned response from a wedged command
  dispatcher, not a real CDB evaluation.
- The kernel saw nothing at any point: no USB errors, no resets, no
  sense storms in the kernel log; the SCSI device state stayed
  `running`. The block layer's TOC ioctl returned EIO while
  CDROM_DRIVE_STATUS still reported a disc present.

**Reset ladder, mildest first, each followed by a TOC probe:**

| Rung | Action | Result |
| --- | --- | --- |
| 1 | SCSI device-level reset (SG_SCSI_RESET DEVICE, sudo) | issued ok; still wedged |
| 2 | SCSI host-level reset (SG_SCSI_RESET HOST, sudo) | ioctl itself EIO (transport cannot deliver); still wedged |
| 3 | USB port reset (usbreset; kernel logged the reset) | executed; still wedged |
| 4 | Physical USB cable replug, enclosure power maintained | full re-enumeration observed in kernel log; still wedged |
| 5 | Enclosure power cycle | cured; TOC reads 1-24 immediately |

The enclosure is an OWC Mercury Pro Optical (USB id 1e91:de2c, USB 2.0
high speed). The wedge survives every host-issuable reset and complete
USB re-enumeration, and clears only with power loss. The stuck state
therefore lives in the powered firmware of the drive or bridge, below
everything the host can reach. The earlier "replug cures" almost
certainly interrupted power; a cable replug alone is proven
insufficient on this hardware.

**Consequences:**

- D11 option (b), an engine-issued bounded device reset, is closed by
  evidence: the app is unprivileged (SG_SCSI_RESET and USBDEVFS_RESET
  both require CAP_SYS_ADMIN and both failed to cure even under sudo),
  and no host-side reset works. The fatal stuck-drive message shipped
  as option (a) is the complete engine-side answer; its wording now
  leads with the power cycle.
- The productive follow-up is user guidance, not recovery: a guided
  physical ladder (cable replug, then power cycle) whose steps the app
  verifies live by watching re-enumeration and probing the TOC
  unprivileged, plus a per-drive incident record so a drive with a
  known cure leads with it. Parked as the SLICE-011 candidate in the
  Linux repository.
- Detection scope: the wedge signature is currently classified only in
  the cache-defeat path. A wedge during ordinary payload reads should
  feed the same classifier before any guided ladder is built.

## Fourth incident, and the first one a tool found (2026-08-15)

The ASUS wedged again after the roughly eleven-minute
StopOnUnrecoverable verify that ended at 00:15, and nobody noticed at
the time. The new recovery probe found it hours later during a routine
hardware check of the SLICE-011 code: the ASUS answered its
table-of-contents read with `errno=5` (EIO) while the internal PLDS and
the WH16NS40 answered theirs in 190 ms and 8 ms. That is the first time
the stuck state was detected by a tool rather than by a failed rip, and
it is the same fingerprint as the third incident.

Two hardware facts came out of the same check, both measured:

- **Serial numbers need two-step symlink resolution.** `/sys/block/srN`
  is itself a symlink, so resolving `/sys/block/srN/device` in one hop
  does literal path arithmetic from `/sys/block` and lands at
  `/6:0:0:0`. Resolving the block node first, then joining the device
  link's own relative target onto that real path, reaches the USB
  device directory.
- **An enclosure here reports a placeholder serial.** The WH16NS40's
  enclosure reports `0123456789ABCDEF`, a value shared across units of
  that design; the ASUS enclosure reports a real one
  (`2309248804E1`, the OWC Mercury Pro). Drive identity therefore
  requires the whole vendor/model/revision/serial tuple, and the
  resolver refuses to guess when more than one node still matches.

The owner power-cycled the enclosure at 08:33 and the same probe
reported the drive `Responsive` with a full 24-track table of contents
in 10 ms. Before and after, from one tool, with nothing else changed:
that is the cure-detection half of the recovery ladder verified on real
hardware before any dialog exists.

Confidence updated: the wedge state, its sense fingerprint, its
survival of all host-side resets, and the power-cycle cure are verified
on this enclosure. The clean-disc counter-experiment (two full Paranoid
runs, no wedge) keeps damage-grinding implicated as the trigger. The
precise wedged component (drive firmware vs bridge firmware) remains
unknown; distinguishing them would need a different enclosure for the
same mechanism or a bus analyzer, and no current decision depends on
the distinction.
