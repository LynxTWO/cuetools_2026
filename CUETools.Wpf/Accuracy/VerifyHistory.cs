using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CUETools.Wpf.Services;

namespace CUETools.Wpf.Accuracy
{
    /// <summary>One track's checksums as read. ArV2 is the match criterion (offset-corrected AR CRC),
    /// ArV1 the fallback for reads predating v2, Crc32 corroboration.</summary>
    public sealed class TrackCrc
    {
        public uint ArV1 { get; set; }
        public uint ArV2 { get; set; }
        public uint Crc32 { get; set; }
    }

    /// <summary>One read of one disc: its identity, the per-track checksums, and the context of the read.
    /// Title/Artist are for the user's own display; the shareable log never carries them.</summary>
    public sealed class VerifyRecord
    {
        public string DiscId { get; set; } = "";
        public TrackCrc[] Tracks { get; set; } = Array.Empty<TrackCrc>();
        public int ArConfidence { get; set; }
        public int ArTotal { get; set; }
        public int CtdbConfidence { get; set; }
        public int CtdbTotal { get; set; }
        public string Drive { get; set; } = "";
        public int ReadOffset { get; set; }
        public int CorrectionQuality { get; set; }
        public bool DeepRecovery { get; set; }
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public DateTime Utc { get; set; }
        public string RipperVersion { get; set; } = "";
    }

    /// <summary>Result of comparing a new read against this disc's stored history.</summary>
    public sealed class VerifyOutcome
    {
        public bool KnownDisc { get; set; }
        public bool Matches { get; set; }
        public int PriorReads { get; set; }
        public int DiffTrackCount { get; set; }
    }

    /// <summary>Your own local verify history: disc id -> up to 5 recent reads, gzip-persisted. On each
    /// read it compares per-track AccurateRip CRCs against the newest stored read (a second, offline,
    /// AccurateRip-independent bit-exactness check) and appends the record. Pure I/O, no drive.</summary>
    public sealed class VerifyHistoryStore
    {
        private const int MaxPerDisc = 5;
        private readonly string _path;

        public VerifyHistoryStore(string path = null)
        {
            _path = path ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CUETools2026", "verify-history.json.gz");
        }

        public VerifyOutcome CompareAndUpsert(VerifyRecord r)
        {
            var all = GzJson.Load<Dictionary<string, List<VerifyRecord>>>(_path)
                      ?? new Dictionary<string, List<VerifyRecord>>();
            string key = (r.DiscId ?? "").Trim();
            all.TryGetValue(key, out var reads);
            reads ??= new List<VerifyRecord>();

            var outcome = new VerifyOutcome { KnownDisc = reads.Count > 0, PriorReads = reads.Count };
            if (outcome.KnownDisc)
            {
                var prev = reads[reads.Count - 1];   // newest stored read
                int diff = 0;
                int n = Math.Min(prev.Tracks.Length, r.Tracks.Length);
                for (int i = 0; i < n; i++)
                    if (!SameTrack(prev.Tracks[i], r.Tracks[i])) diff++;
                diff += Math.Abs(prev.Tracks.Length - r.Tracks.Length);   // track-count change counts as diff
                outcome.DiffTrackCount = diff;
                outcome.Matches = diff == 0;
            }

            reads.Add(r);
            if (reads.Count > MaxPerDisc) reads.RemoveRange(0, reads.Count - MaxPerDisc);
            all[key] = reads;
            GzJson.Save(_path, all);
            return outcome;
        }

        // A track matches on the AccurateRip CRC: prefer v2, fall back to v1 when either side lacks v2.
        private static bool SameTrack(TrackCrc a, TrackCrc b)
        {
            if (a.ArV2 != 0 && b.ArV2 != 0) return a.ArV2 == b.ArV2;
            return a.ArV1 == b.ArV1;
        }

        public static string ToJson(VerifyRecord r) =>
            JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true });
    }
}
