using System;
using System.Collections.Generic;
using System.Text;

namespace CUETools.Wpf.Accuracy
{
    /// <summary>Renders the human-readable Test & Copy log - the local proof a disc was read at least
    /// twice and every committed track agreed bit-for-bit. Pure text, no I/O. ASCII only.</summary>
    public static class TestAndCopyLog
    {
        public static string Format(TestCopyResult result, IReadOnlyList<VerifyRecord> reads,
            string discId, string drive, int readOffset, int failedWindows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Test & Copy log");
            sb.AppendLine("Disc:   " + (discId ?? ""));
            sb.AppendLine("Drive:  " + (drive ?? "") + "   read offset " + readOffset);
            sb.AppendLine("Reads: " + result.ReadsUsed);
            sb.AppendLine();

            foreach (var v in result.Tracks)
            {
                sb.Append("Track " + (v.TrackIndex + 1).ToString("00") + ":  ");
                for (int i = 0; i < reads.Count; i++)
                {
                    var tr = Track(reads[i], v.TrackIndex);
                    sb.Append("R" + (i + 1) + "=" + (tr != null ? tr.Crc32.ToString("X8") : "--------") + " ");
                }
                if (v.Agreed)
                    sb.Append(" agreed(R" + (v.AgreeingReads[0] + 1) + ",R" + (v.AgreeingReads[1] + 1) +
                              ") -> committed from R" + (v.SourceReadIndex + 1));
                else
                    sb.Append(" NO AGREEMENT - held");
                sb.AppendLine();
            }
            sb.AppendLine();

            if (result.Outcome == TestCopyOutcome.Passed)
                sb.AppendLine("Test & Copy PASSED - every track verified by >=2 independent reads.");
            else
            {
                var oneBased = new List<string>();
                foreach (var h in result.HeldTracks) oneBased.Add((h + 1).ToString());
                sb.AppendLine("Test & Copy HELD - no agreement on track(s): " + string.Join(", ", oneBased));
            }

            var last = reads.Count > 0 ? reads[reads.Count - 1] : null;
            int arConf = last?.ArConfidence ?? 0, ctConf = last?.CtdbConfidence ?? 0;
            sb.AppendLine("AccurateRip: " + (arConf > 0 ? "accurate, confidence " + arConf : "not found"));
            sb.AppendLine("CTDB:        " + (ctConf > 0 ? "match, confidence " + ctConf : "not found"));
            if (failedWindows > 0)
                sb.AppendLine("WARNING: " + failedWindows + " unrecoverable sector window(s) during reads - " +
                              "agreement over damaged media is consistency, not proof the region is pristine.");
            return sb.ToString();
        }

        private static TrackCrc Track(VerifyRecord r, int t)
        {
            var arr = r?.Tracks;
            return (arr != null && t >= 0 && t < arr.Length) ? arr[t] : null;
        }
    }
}
