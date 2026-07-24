using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using CUETools.Wpf.Accuracy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    // VerifyHistoryStore reads its on-disk history file through GzJson, which never throws and
    // returns default(T) on bad input - but the store itself must also treat that null as an
    // empty history (not crash, not misreport a corrupt file as a known disc). This points a
    // store at malformed/empty/garbage files written directly to its path and asserts
    // CompareAndUpsert degrades gracefully every time: never throws, and reports the disc as
    // unknown (KnownDisc=false) on this first-ever CompareAndUpsert call against that path,
    // exactly as it would for a genuinely empty history. Fixed seed so a failure reproduces.
    [TestClass]
    public class VerifyHistoryStoreFuzzTests
    {
        private const int Seed = 20260724;
        private const int Iterations = 200;

        private static string TempPath() =>
            Path.Combine(Path.GetTempPath(), "vh-fuzz-" + Guid.NewGuid().ToString("N") + ".json.gz");

        private static byte[] ValidGzipOf(string text)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                gz.Write(bytes, 0, bytes.Length);
            }
            return ms.ToArray();
        }

        private static VerifyRecord Rec(string disc, params uint[] v2)
        {
            var t = new TrackCrc[v2.Length];
            for (int i = 0; i < v2.Length; i++) t[i] = new TrackCrc { ArV1 = v2[i] ^ 0x1u, ArV2 = v2[i], Crc32 = v2[i] };
            return new VerifyRecord { DiscId = disc, Tracks = t, Drive = "TEST", Utc = DateTime.UtcNow };
        }

        [TestMethod]
        public void DegradesGracefullyOnMalformedHistoryFile()
        {
            var rnd = new Random(Seed);

            // Deterministic edge cases the constraints call out explicitly, plus a batch of
            // randomized variants built from the same seeded Random.
            var cases = new List<(string label, byte[] bytes, bool writeFile)>
            {
                ("no-file", null, false),
                ("empty-file", Array.Empty<byte>(), true),
                ("single-byte", new byte[] { 0x1f }, true),
                ("gzip-magic-only", new byte[] { 0x1f, 0x8b }, true),
                ("gzip-of-garbage", ValidGzipOf("this is not json { [ garbage"), true),
                ("valid-gzip-wrong-shape-array", ValidGzipOf("[1,2,3]"), true),               // valid gzip/JSON, wrong shape (array, not disc->reads map)
                ("valid-gzip-wrong-shape-object", ValidGzipOf("{\"a\":1}"), true),
                ("plain-non-json-text", Encoding.UTF8.GetBytes("not json at all"), true),
                ("plain-truncated-json", Encoding.UTF8.GetBytes("{\"D1\":[{\"DiscId\""), true),
                ("plain-wrong-shape", Encoding.UTF8.GetBytes("[1,2,3]"), true),
            };

            for (int i = 0; i < Iterations; i++)
            {
                int kind = rnd.Next(4);
                byte[] bytes;
                string label;
                switch (kind)
                {
                    case 0: // pure random bytes, random length (may coincidentally start with gzip magic)
                        {
                            int len = rnd.Next(0, 512);
                            bytes = new byte[len];
                            rnd.NextBytes(bytes);
                            label = $"random-bytes[{i}] len={len}";
                            break;
                        }
                    case 1: // random bytes forced to start with the gzip magic (exercises the decompress path with garbage payload)
                        {
                            int len = rnd.Next(2, 256);
                            bytes = new byte[len];
                            rnd.NextBytes(bytes);
                            bytes[0] = 0x1f;
                            bytes[1] = 0x8b;
                            label = $"fake-gzip-magic[{i}] len={len}";
                            break;
                        }
                    case 2: // a valid gzip stream (of well-formed but wrong-shaped JSON), truncated at a random point
                        {
                            var full = ValidGzipOf("{\"D1\":[" + rnd.Next() + "]}");
                            int cut = full.Length == 0 ? 0 : rnd.Next(0, full.Length);
                            bytes = full.AsSpan(0, cut).ToArray();
                            label = $"truncated-valid-gzip[{i}] cut={cut}/{full.Length}";
                            break;
                        }
                    default: // plain (non-gzip) random text
                        {
                            var sb = new StringBuilder();
                            int len = rnd.Next(0, 64);
                            for (int c = 0; c < len; c++) sb.Append((char)rnd.Next(1, 256));
                            bytes = Encoding.UTF8.GetBytes(sb.ToString());
                            label = $"plain-random-text[{i}]";
                            break;
                        }
                }
                cases.Add((label, bytes, true));
            }

            foreach (var (label, bytes, writeFile) in cases)
            {
                string path = TempPath();
                try
                {
                    if (writeFile) File.WriteAllBytes(path, bytes);

                    VerifyOutcome outcome;
                    try
                    {
                        var store = new VerifyHistoryStore(path);
                        outcome = store.CompareAndUpsert(Rec("D1", 10, 20, 30));
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail($"CompareAndUpsert threw on case '{label}' (seed={Seed}): {ex}");
                        return;
                    }

                    // A bad/unreadable store is treated as empty: this is reported as the disc's
                    // first-ever read against this (garbage) path, never as a false "known disc".
                    Assert.IsFalse(outcome.KnownDisc, $"case '{label}' (seed={Seed}): malformed store must read as empty, not a known disc");
                    Assert.AreEqual(0, outcome.PriorReads, $"case '{label}' (seed={Seed}): malformed store must report zero prior reads");
                }
                finally
                {
                    if (File.Exists(path)) File.Delete(path);
                }
            }
        }

        // A corrupt-but-valid-gzip history file can deserialize a stored VerifyRecord whose
        // Tracks is null (well-formed JSON, "Tracks":null). CompareAndUpsert must treat that as
        // an empty track list rather than dereference a null array - this is the disc's one
        // prior (null-Tracks) read, so it's a known disc, and the compare must not throw.
        [TestMethod]
        public void NullTracksInStoredRecordDoesNotThrow()
        {
            string path = TempPath();
            try
            {
                byte[] bytes = ValidGzipOf("{\"D1\":[{\"DiscId\":\"D1\",\"Tracks\":null}]}");
                File.WriteAllBytes(path, bytes);

                var store = new VerifyHistoryStore(path);
                VerifyOutcome outcome;
                try
                {
                    outcome = store.CompareAndUpsert(Rec("D1", 10, 20, 30));
                }
                catch (Exception ex)
                {
                    Assert.Fail($"CompareAndUpsert threw on a stored record with null Tracks: {ex}");
                    return;
                }

                Assert.IsTrue(outcome.KnownDisc, "one prior (null-Tracks) read is on file, so the disc must be known");
                Assert.AreEqual(1, outcome.PriorReads);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
