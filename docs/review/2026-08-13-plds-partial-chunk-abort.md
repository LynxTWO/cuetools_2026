# PLDS DU8A5SH: deterministic abort of 15- and 2-sector BEh reads

**The laptop's internal PLDS DVD-RW DU8A5SH (firmware BU51) deterministically
aborts BEh+C2 payload reads of exactly 15 or 2 sectors, at any disc
location, with ABORTED COMMAND (sense 0B, ASC/ASCQ 00/00).** Every other
count probed (16, 14, 10, 8, 4, 1) succeeds every time. The receipt is the
probe table below, taken 2026-08-13 late evening on the Linux head's SG_IO
transport with a pressed audio CD loaded.

How it surfaced: the first two secure verifies driven through the Linux
app's rip page both failed closed at 87-88 s with "Drive-cache flush
failed" at the same relative sector. The cache-defeat eviction plan needed
335 sectors, chunked as 20 x 16 + 15; the final 15-sector chunk aborted in
all three candidate regions, both runs, identical counters. The engine's
fail-closed behavior was correct: cache defeat is complete or explicit, and
an aborted eviction read is neither.

Probe (dev-only --rip-probe, three well-separated regions, counts in issue
order; "abort" = DeviceFailed with sense 0B/00/00):

    count:   16    15    14    10    8     4     2     1     16
    PLDS     ok    abort ok    ok    ok    ok    abort ok    ok   (all 3 regions)
    ASUS     ok    ok    ok    ok    ok    ok    ok    ok    ok   (all 3 regions)
    WH16NS40 ok    ok    ok    ok    ok    ok    ok    ok    ok   (all 3 regions)

Observation counts: 6 probe aborts on the PLDS (3 regions x counts 15 and
2), plus the 2 identical live flush failures; 0 aborts in 54 probe reads
across the ASUS BW-16D1HT 3.11 and HL-DT-ST WH16NS40 1.05.

**Fix applied (this commit): the eviction plan pads up to a whole number of
chunks**, so every eviction read uses the full chunk shape. This evicts at
least the required bytes (strictly more when padded), keeps the
complete-or-explicit rule intact, and removes the failing shape without
classifying any sense code. The 24/00 chunk-fallback and every other
failure classification are untouched. Verified: the same secure verify
that failed twice completes after the change (see the Linux head's
SLICE-009 brief for the run receipt).

**Open question (decision needed): payload-tail batch shapes.** Normal
window reads batch by 16 sectors, so a window whose length is not a
multiple of 16 issues a partial tail batch. On this drive a tail of 15 or
2 sectors would abort, and ABORTED COMMAND is not in any enumerated
retry/fallback class, so a rip of a disc whose geometry lands on those
shapes fails fatally partway through. The loaded disc's windows end on a
count-4 tail, so this has not fired live yet. Candidate remedies, both
needing owner sign-off on scope: (a) universal - split any partial tail
batch into counts the drive has proven (e.g. 8+7 or 1+1); (b) drive-scoped
- avoid only counts 15 and 2 on the exact PLDS DU8A5SH BU51 identity, in
the spirit of the existing WH16NS40 08/0A carve-out. Recorded in
decisions-needed.

Resolution 2026-08-14: the owner chose (b). SafeBatchSectors in
PayloadReadFailurePolicy splits the two failing shapes into proven-safe
sub-reads (15 -> 8+7, 2 -> 1+1) for only the exact drive identity; the
slip probe shrinks to the safe count on the same identity.
