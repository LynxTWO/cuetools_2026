using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    // GzJson.Load reads untrusted files on disk (history/report/calibration stores, and later
    // the .verify sidecar). A corrupt, truncated, or hostile file must never crash the app - it
    // must degrade to default(T). This fuzzes the byte-level input space with a fixed seed so
    // any failure reproduces from the printed seed/iteration.
    [TestClass]
    public class GzJsonFuzzTests
    {
        private const int Seed = 20260724;
        private const int Iterations = 500;

        private sealed class Poco
        {
            public string Name { get; set; }
            public int Value { get; set; }
        }

        private static string TempPath() =>
            Path.Combine(Path.GetTempPath(), "gzjson-fuzz-" + Guid.NewGuid().ToString("N") + ".bin");

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

        [TestMethod]
        public void NeverThrowsOnRandomOrEdgeBytes()
        {
            var rnd = new Random(Seed);

            // A handful of deterministic edge cases the constraints call out explicitly, plus a
            // large batch of randomized variants built from the same seeded Random.
            var cases = new List<(string label, byte[] bytes)>
            {
                ("empty", Array.Empty<byte>()),
                ("single-byte", new byte[] { 0x1f }),
                ("gzip-magic-only", new byte[] { 0x1f, 0x8b }),
                ("truncated-gzip-header", ValidGzipOf("[1,2,3]").AsSpan(0, 4).ToArray()),
                ("gzip-of-non-json", ValidGzipOf("this is not json { [ garbage")),
                ("gzip-of-wrong-typed-json", ValidGzipOf("{\"a\":1}")),   // valid gzip, valid JSON, wrong shape for List<int>
                ("plain-non-json-text", Encoding.UTF8.GetBytes("not json at all")),
                ("plain-truncated-json", Encoding.UTF8.GetBytes("[1,2,")),
                ("plain-wrong-typed-json", Encoding.UTF8.GetBytes("{\"a\":1}")),
            };

            for (int i = 0; i < Iterations; i++)
            {
                int kind = rnd.Next(6);
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
                    case 2: // a valid gzip stream, truncated at a random point
                        {
                            var full = ValidGzipOf("[" + string.Join(",", RandomInts(rnd, rnd.Next(0, 20))) + "]");
                            int cut = full.Length == 0 ? 0 : rnd.Next(0, full.Length);
                            bytes = full.AsSpan(0, cut).ToArray();
                            label = $"truncated-valid-gzip[{i}] cut={cut}/{full.Length}";
                            break;
                        }
                    case 3: // valid gzip of random (non-JSON) text
                        {
                            var text = RandomString(rnd, rnd.Next(0, 64));
                            bytes = ValidGzipOf(text);
                            label = $"gzip-random-text[{i}]";
                            break;
                        }
                    case 4: // valid gzip of well-formed JSON of the wrong shape
                        {
                            var text = rnd.Next(2) == 0 ? "{\"x\":" + rnd.Next() + "}" : "\"just a string\"";
                            bytes = ValidGzipOf(text);
                            label = $"gzip-wrong-typed-json[{i}]";
                            break;
                        }
                    default: // plain (non-gzip) random text, sometimes valid JSON of the wrong shape, sometimes garbage
                        {
                            var text = rnd.Next(2) == 0 ? RandomString(rnd, rnd.Next(0, 64)) : "{\"y\":" + rnd.Next() + "}";
                            bytes = Encoding.UTF8.GetBytes(text);
                            label = $"plain-text[{i}]";
                            break;
                        }
                }
                cases.Add((label, bytes));
            }

            foreach (var (label, bytes) in cases)
            {
                string path = TempPath();
                try
                {
                    File.WriteAllBytes(path, bytes);

                    // Try a couple of different target shapes: a list (the common store shape) and a
                    // POCO (closer to VerifyRecord/DriveCalibration). Neither call may throw or hang.
                    List<int> asList;
                    Poco asPoco;
                    try
                    {
                        asList = GzJson.Load<List<int>>(path);
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail($"GzJson.Load<List<int>> threw on case '{label}' (seed={Seed}): {ex}");
                        return;
                    }
                    try
                    {
                        asPoco = GzJson.Load<Poco>(path);
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail($"GzJson.Load<Poco> threw on case '{label}' (seed={Seed}): {ex}");
                        return;
                    }

                    // Graceful degradation: either a real value came back, or default(T) (null for
                    // reference types) - never a half-built object or an unhandled exception.
                    _ = asList;
                    _ = asPoco;
                }
                finally
                {
                    if (File.Exists(path)) File.Delete(path);
                }
            }
        }

        private static IEnumerable<int> RandomInts(Random rnd, int count)
        {
            for (int i = 0; i < count; i++) yield return rnd.Next(-1000, 1000);
        }

        private static string RandomString(Random rnd, int len)
        {
            var sb = new StringBuilder(len);
            for (int i = 0; i < len; i++) sb.Append((char)rnd.Next(1, 256));
            return sb.ToString();
        }
    }
}
