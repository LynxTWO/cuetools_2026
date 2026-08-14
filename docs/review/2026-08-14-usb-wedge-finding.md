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
