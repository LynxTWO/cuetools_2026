using System;
using System.Collections.Generic;

namespace CUETools.Wpf.Accuracy
{
    public enum TestCopyOutcome { Passed, Held }

    /// <summary>Per-track verdict: whether two reads agreed, which read's file to commit (a staged
    /// read, or -1 when held), and the agreeing pair (for the log).</summary>
    public sealed class TrackVerdict
    {
        public int TrackIndex { get; set; }
        public bool Agreed { get; set; }
        public int SourceReadIndex { get; set; } = -1;
        public int[] AgreeingReads { get; set; } = Array.Empty<int>();
    }

    public sealed class TestCopyResult
    {
        public TestCopyOutcome Outcome { get; set; }
        public int ReadsUsed { get; set; }
        public TrackVerdict[] Tracks { get; set; } = Array.Empty<TrackVerdict>();
        public int[] HeldTracks { get; set; } = Array.Empty<int>();
    }

    /// <summary>Pure Test & Copy resolver: given the per-read checksum records and which reads are
    /// staged (have audio on disk), decide per track whether two same-drive reads agree on both their
    /// full-range CRC32 and AccurateRip checksum, and which staged read's file to commit. No hardware,
    /// no I/O - fully unit-testable.</summary>
    public static class TestAndCopyResolver
    {
        public static TestCopyResult Resolve(IReadOnlyList<VerifyRecord> reads, IReadOnlyList<bool> staged)
        {
            if (reads == null) throw new ArgumentNullException(nameof(reads));
            if (staged == null) throw new ArgumentNullException(nameof(staged));
            if (staged.Count != reads.Count)
                throw new ArgumentException("one staged flag per read is required", nameof(staged));

            int trackCount = 0;
            foreach (var r in reads) trackCount = Math.Max(trackCount, r?.Tracks?.Length ?? 0);

            var verdicts = new TrackVerdict[trackCount];
            var held = new List<int>();

            for (int t = 0; t < trackCount; t++)
            {
                var v = new TrackVerdict { TrackIndex = t };
                // smallest staged read that agrees with some other read on this track
                for (int i = 0; i < reads.Count && !v.Agreed; i++)
                {
                    if (!staged[i]) continue;
                    var ti = Track(reads[i], t);
                    if (ti == null) continue;
                    for (int j = 0; j < reads.Count; j++)
                    {
                        if (j == i) continue;
                        var tj = Track(reads[j], t);
                        if (tj == null) continue;
                        if (VerifyHistoryStore.SameAudioForTestAndCopy(ti, tj))
                        {
                            v.Agreed = true;
                            v.SourceReadIndex = i;
                            v.AgreeingReads = new[] { Math.Min(i, j), Math.Max(i, j) };
                            break;
                        }
                    }
                }
                if (!v.Agreed) held.Add(t);
                verdicts[t] = v;
            }

            return new TestCopyResult
            {
                Outcome = held.Count == 0 ? TestCopyOutcome.Passed : TestCopyOutcome.Held,
                ReadsUsed = reads.Count,
                Tracks = verdicts,
                HeldTracks = held.ToArray(),
            };
        }

        private static TrackCrc Track(VerifyRecord r, int t)
        {
            var arr = r?.Tracks;
            return (arr != null && t >= 0 && t < arr.Length) ? arr[t] : null;
        }

        /// <summary>The smallest staged read index that has a full-range Test &amp; Copy agreement with
        /// some other read on EVERY track, or -1 if no single read is clean throughout. Used to commit
        /// one read's files wholesale (never misaligns) instead of assembling per track.</summary>
        public static int FullyVerifiedReadIndex(IReadOnlyList<VerifyRecord> reads, IReadOnlyList<bool> staged)
        {
            if (reads == null || staged == null || staged.Count != reads.Count) return -1;
            int trackCount = 0;
            foreach (var r in reads) trackCount = Math.Max(trackCount, r?.Tracks?.Length ?? 0);
            for (int i = 0; i < reads.Count; i++)
            {
                if (!staged[i]) continue;
                bool coversAll = true;
                for (int t = 0; t < trackCount && coversAll; t++)
                {
                    var ti = Track(reads[i], t);
                    bool agrees = false;
                    for (int j = 0; j < reads.Count && !agrees; j++)
                    {
                        if (j == i) continue;
                        if (VerifyHistoryStore.SameAudioForTestAndCopy(ti, Track(reads[j], t))) agrees = true;
                    }
                    if (!agrees) coversAll = false;
                }
                if (coversAll && trackCount > 0) return i;
            }
            return -1;
        }
    }
}
