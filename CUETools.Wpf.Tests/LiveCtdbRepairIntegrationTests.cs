#if CTDB_LIVE_PROBE
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CUETools.Codecs;
using CUETools.Processor;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    /// <summary>
    /// Opt-in hardware/service evidence. Build with RunCtdbLiveProbe=true and provide a known
    /// damaged-but-decodable image through CUETOOLS_CTDB_REPAIR_FIXTURE. Ordinary test discovery
    /// does not contain this class, so an unavailable external fixture is never mislabeled a pass.
    /// </summary>
    [TestClass]
    public sealed class LiveCtdbRepairIntegrationTests
    {
        [TestMethod]
        [TestCategory("Hardware")]
        public void DamagedImageIsDetectedRepairedAndIndependentlyVerified()
        {
            string source = Environment.GetEnvironmentVariable(
                "CUETOOLS_CTDB_REPAIR_FIXTURE") ?? "";
            Assert.IsFalse(string.IsNullOrWhiteSpace(source),
                "Set CUETOOLS_CTDB_REPAIR_FIXTURE to a known damaged .cue or lossless image.");
            source = Path.GetFullPath(source);
            Assert.IsTrue(File.Exists(source), "The CTDB repair fixture does not exist.");

            // This probe runs from a testhost rather than the packaged plugins directory. Register
            // the same managed Flake FLAC codec explicitly; no loose assembly discovery is enabled.
            if (!CUEProcessorPlugins.encs.Any(
                    codec => codec is CUETools.Codecs.Flake.EncoderSettings))
                CUEProcessorPlugins.encs.Add(new CUETools.Codecs.Flake.EncoderSettings());
            if (!CUEProcessorPlugins.decs.Any(
                    codec => codec is CUETools.Codecs.Flake.DecoderSettings))
                CUEProcessorPlugins.decs.Add(new CUETools.Codecs.Flake.DecoderSettings());

            string[] sourceAudioPaths = ReadSourceAudioPaths(source);
            var before = HashSourceSet(source);
            var service = new VerifyService(new CUEConfig(), new ProbeLog());

            VerifyFilesResult damaged = service.Verify(source, (_, _) => { });
            Assert.IsTrue(damaged.Ok, damaged.Error);
            Assert.IsTrue(damaged.HasErrors,
                "The fixture unexpectedly matched CTDB exactly; no repair path was exercised.");
            Assert.IsTrue(damaged.CanRecover,
                "CTDB detected damage but did not offer enough parity to recover it.");
            Assert.IsTrue(damaged.RepairSectors > 0);

            VerifyFilesResult repaired = service.Repair(source, (_, _) => { });
            Assert.IsTrue(repaired.Ok, repaired.Error);
            Assert.IsTrue(repaired.RepairApplied);
            Assert.IsFalse(string.IsNullOrWhiteSpace(repaired.OutputPath));
            Assert.IsTrue(Directory.Exists(repaired.OutputPath));
            Assert.IsTrue(repaired.Accurate || repaired.CtdbConfidence > 0,
                "The independently decoded repaired copy did not match AccurateRip or CTDB.");

            string[] expectedNames = sourceAudioPaths
                .Select(path => Path.ChangeExtension(Path.GetFileName(path), ".flac"))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string[] actualNames = Directory
                .EnumerateFiles(repaired.OutputPath, "*.flac", SearchOption.AllDirectories)
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            CollectionAssert.AreEqual(
                expectedNames,
                actualNames,
                "CTDB repair did not preserve the source audio basenames.");

            CollectionAssert.AreEquivalent(
                before.Select(pair => pair.Key + "=" + pair.Value).ToArray(),
                HashSourceSet(source).Select(pair => pair.Key + "=" + pair.Value).ToArray(),
                "Repair modified the source set instead of publishing a sibling copy.");

            TestContext.WriteLine("Published verified repaired copy: " + repaired.OutputPath);
        }

        public TestContext TestContext { get; set; }

        private static string[] ReadSourceAudioPaths(string source)
        {
            var sheet = new CUESheet(new CUEConfig());
            try
            {
                sheet.Open(source);
                return sheet.SourcePaths
                    .Select(Path.GetFullPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            finally
            {
                sheet.Close();
            }
        }

        private static Dictionary<string, string> HashSourceSet(string cuePath)
        {
            string directory = Path.GetDirectoryName(cuePath)
                ?? throw new InvalidDataException("Fixture has no parent directory.");
            return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => path.EndsWith(".cue", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".flac", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(
                    path => Path.GetFileName(path),
                    path =>
                    {
                        using FileStream stream = File.OpenRead(path);
                        return Convert.ToHexString(SHA256.HashData(stream));
                    },
                    StringComparer.OrdinalIgnoreCase);
        }

        private sealed class ProbeLog : IDiagnosticLog
        {
            public string LogPath => "";
            public void Info(string category, string message)
                => Console.WriteLine($"INFO {category}: {message}");
            public void Warn(string category, string message)
                => Console.WriteLine($"WARN {category}: {message}");
            public void Error(string category, string message, Exception ex = null)
                => Console.WriteLine($"ERROR {category}: {message} ({ex?.GetType().Name})");
            public void Redact(params string[] sensitive) { }
        }
    }
}
#endif
