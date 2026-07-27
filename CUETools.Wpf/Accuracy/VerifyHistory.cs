using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CUETools.Wpf.Services;

namespace CUETools.Wpf.Accuracy
{
    /// <summary>One track's checksums as read. AR checksums support cross-drive history comparison;
    /// Crc32 is the full-range same-drive Test &amp; Copy checksum.</summary>
    public sealed class TrackCrc
    {
        public uint ArV1 { get; set; }
        public uint ArV2 { get; set; }
        /// <summary>The checksum from this particular optical read.</summary>
        public uint Crc32 { get; set; }
        /// <summary>Most recent verify/Test-pass full-range CRC for this disc track.</summary>
        public uint TestCrc32 { get; set; }
        /// <summary>Most recent rip/Copy-pass full-range CRC for this disc track.</summary>
        public uint CopyCrc32 { get; set; }
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
        /// <summary>Encoded-file assurance carried in the album's rip.verify sidecar. It is not
        /// consulted for optical-read history matching.</summary>
        public string Format { get; set; } = "";
        public bool OutputVerificationKnown { get; set; }
        public bool LosslessOutput { get; set; }
        public bool OutputVerificationPerformed { get; set; }
        public string OutputVerificationDetail { get; set; } = "";
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
            if (r == null)
                throw new ArgumentNullException(nameof(r));
            return GzJson.Update<
                Dictionary<string, List<VerifyRecord>>,
                VerifyOutcome>(
                _path,
                loaded =>
                {
                    if (loaded.Status == GzJsonLoadStatus.Failed)
                        throw new InvalidDataException(
                            "The verify-history store exists but could not be read; it was left unchanged.",
                            loaded.Error);
                    var all = loaded.Status == GzJsonLoadStatus.Loaded
                        ? loaded.Value!
                        : new Dictionary<string, List<VerifyRecord>>();
                    string key = (r.DiscId ?? "").Trim();
                    all.TryGetValue(key, out var reads);
                    reads ??= new List<VerifyRecord>();

                    var outcome = new VerifyOutcome
                    {
                        KnownDisc = reads.Count > 0,
                        PriorReads = reads.Count
                    };
                    if (outcome.KnownDisc)
                    {
                        var prev = reads[reads.Count - 1];   // newest stored read
                        MergePersistentCrcEvidence(
                            r.Tracks ?? Array.Empty<TrackCrc>(),
                            prev?.Tracks ?? Array.Empty<TrackCrc>());
                        // A corrupt-but-valid-gzip history file can deserialize a record whose
                        // Tracks is null (JSON "Tracks":null); treat that as an empty track list
                        // rather than crash.
                        var pt = prev?.Tracks ?? Array.Empty<TrackCrc>();
                        var rt = r.Tracks ?? Array.Empty<TrackCrc>();
                        int diff = 0;
                        int n = Math.Min(pt.Length, rt.Length);
                        for (int i = 0; i < n; i++)
                            if (!SameTrackForHistory(pt[i], rt[i])) diff++;
                        diff += Math.Abs(pt.Length - rt.Length);
                        outcome.DiffTrackCount = diff;
                        outcome.Matches = diff == 0;
                    }

                    reads.Add(r);
                    if (reads.Count > MaxPerDisc)
                        reads.RemoveRange(0, reads.Count - MaxPerDisc);
                    all[key] = reads;
                    return (all, outcome);
                });
        }

        /// <summary>
        /// Return the newest persisted Test/Copy CRC evidence for a disc. The UI uses this when a
        /// known disc is inserted so the two columns survive restarts and drive changes.
        /// </summary>
        public TrackCrc[] GetLatestCrcEvidence(string discId)
        {
            GzJsonLoadResult<Dictionary<string, List<VerifyRecord>>> loaded =
                GzJson.TryLoad<Dictionary<string, List<VerifyRecord>>>(_path);
            if (loaded.Status == GzJsonLoadStatus.Failed)
                throw new InvalidDataException(
                    "The verify-history store exists but could not be read; it was left unchanged.",
                    loaded.Error);
            if (loaded.Status != GzJsonLoadStatus.Loaded ||
                !loaded.Value!.TryGetValue((discId ?? "").Trim(), out var reads) ||
                reads == null ||
                reads.Count == 0)
                return Array.Empty<TrackCrc>();
            TrackCrc[] source = reads[reads.Count - 1]?.Tracks ?? Array.Empty<TrackCrc>();
            var copy = new TrackCrc[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                TrackCrc? track = source[i];
                copy[i] = track == null
                    ? new TrackCrc()
                    : new TrackCrc
                    {
                        ArV1 = track.ArV1,
                        ArV2 = track.ArV2,
                        Crc32 = track.Crc32,
                        TestCrc32 = track.TestCrc32,
                        CopyCrc32 = track.CopyCrc32,
                    };
            }
            return copy;
        }

        internal static void MergePersistentCrcEvidence(
            TrackCrc[] now,
            IReadOnlyList<TrackCrc> before)
        {
            int count = Math.Min(now.Length, before.Count);
            for (int i = 0; i < count; i++)
            {
                if (now[i] == null || before[i] == null) continue;
                if (now[i].TestCrc32 == 0)
                    now[i].TestCrc32 = before[i].TestCrc32;
                if (now[i].CopyCrc32 == 0)
                    now[i].CopyCrc32 = before[i].CopyCrc32;
            }
        }

        // History must remain comparable across drives and offsets. Prefer v2, then use v1 for older
        // records; raw CRC32 is deliberately excluded because it is not an offset-corrected contract.
        // Null-tolerant for corrupt history.
        public static bool SameAudioForHistory(TrackCrc a, TrackCrc b)
        {
            if (a == null || b == null) return false;
            if (a.ArV2 != 0 && b.ArV2 != 0) return a.ArV2 == b.ArV2;
            if (a.ArV1 != 0 && b.ArV1 != 0) return a.ArV1 == b.ArV1;
            return false;
        }

        // Test & Copy compares independent reads from the same drive at the same offset. Require the
        // full-range checksum, so AccurateRip's intentional disc-edge exclusions cannot certify a
        // difference there; retain the AR match as an independent corroborating signal.
        public static bool SameAudioForTestAndCopy(TrackCrc a, TrackCrc b) =>
            a != null && b != null &&
            a.Crc32 != 0 && b.Crc32 != 0 && a.Crc32 == b.Crc32 &&
            SameAudioForHistory(a, b);

        private static bool SameTrackForHistory(TrackCrc a, TrackCrc b) => SameAudioForHistory(a, b);

        public static string ToJson(VerifyRecord r) =>
            JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true });
    }
}
