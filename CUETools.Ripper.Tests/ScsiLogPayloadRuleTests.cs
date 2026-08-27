using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Ripper.Tests
{
    /// <summary>
    /// The no-audio-buffer rule for SCSI diagnostics, as source contracts. The 2026-08-27 audit
    /// classified every log call in Bwg.Scsi and CUETools.Ripper.SCSI: command echoes carry
    /// scalar arguments and the literal word "data" in place of the buffer, sense and CDB bytes
    /// are hex-formatted deliberately, the only DumpBuffer callers dump the raw Inquiry result
    /// (device identity), and the cue-sheet dump is a burn-time track layout. No read payload is
    /// ever formatted into a log line. These tests keep that true.
    /// </summary>
    [TestClass]
    public class ScsiLogPayloadRuleTests
    {
        [TestMethod]
        public void TheOnlyBufferDumpsAreTheRawInquiryResult()
        {
            string device = Read("Bwg.Scsi", "Device.cs");
            var titles = Regex.Matches(device, @"DumpBuffer\(\s*\d+\s*,\s*""([^""]*)""")
                .Select(m => m.Groups[1].Value)
                .ToArray();

            Assert.AreEqual(2, titles.Length, "DumpBuffer callers in Device.cs");
            foreach (string title in titles)
                Assert.AreEqual(
                    "Raw Inquiry Result", title,
                    "a DumpBuffer of anything but the Inquiry result needs a new audit entry");

            foreach (string file in Directory.GetFiles(
                         Path.Combine(FindRepositoryRoot(), "CUETools.Ripper.SCSI"), "*.cs"))
                Assert.IsFalse(
                    File.ReadAllText(file).Contains("DumpBuffer(", StringComparison.Ordinal),
                    Path.GetFileName(file) + " must not dump buffers into the log");
        }

        [TestMethod]
        public void CommandEchoesNeverFormatTheDataBuffer()
        {
            string device = Read("Bwg.Scsi", "Device.cs");
            string[] logLines = device.Split('\n')
                .Where(line => line.Contains("m_logger.", StringComparison.Ordinal))
                .ToArray();
            Assert.IsTrue(logLines.Length >= 80, "the inventory expects the full set of log calls");

            // The read and write echoes build their argument string with the literal "data"
            // where the buffer parameter sits. Formatting the buffer itself - through
            // ToString, Marshal reads, or a byte-array walk - is the failure this pins.
            string[] forbidden =
            {
                "data.ToString(", "Marshal.ReadByte(data", "Marshal.Copy(data",
                "buffer.ToString(", "Marshal.ReadByte(buffer",
            };
            foreach (string line in logLines)
                foreach (string token in forbidden)
                    Assert.IsFalse(
                        line.Contains(token, StringComparison.Ordinal),
                        "a log call formats a payload buffer: " + line.Trim());
        }

        [TestMethod]
        public void BwgLoggingEmitsNothingUntilASinkIsAttached()
        {
            // Every Device in this repository is built with a bare `new Logger()` and nothing
            // attaches a sink or raises a level, so the opt-in diagnostics stay off in every
            // shipping path. Pin both halves: the framework drops sinkless messages, and the
            // repository never wires a sink outside Bwg.Logging itself.
            string logger = Read("Bwg.Logging", "Logger.cs");
            StringAssert.Contains(
                logger, "if (m_sinks.ContainsKey(m.MType))",
                "LogMessage must gate on a registered sink");

            string root = FindRepositoryRoot();
            foreach (string project in new[] { "Bwg.Scsi", "CUETools.Ripper.SCSI", "CUETools.Ripper", "CUETools.App.Core", "CUETools.Wpf" })
            {
                string dir = Path.Combine(root, project);
                if (!Directory.Exists(dir)) continue;
                foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                        file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                        continue;
                    string text = File.ReadAllText(file);
                    Assert.IsFalse(
                        text.Contains(".SetSink(", StringComparison.Ordinal),
                        file + " attaches a Bwg.Logging sink; the SCSI diagnostics are meant to stay opt-in and off");
                }
            }
        }

        private static string Read(string project, string file)
            => File.ReadAllText(Path.Combine(FindRepositoryRoot(), project, file));

        private static string FindRepositoryRoot()
        {
            DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "CUETools.sln")))
                    return current.FullName;
                current = current.Parent;
            }
            Assert.Fail("Could not locate the repository root from the test output.");
            return null;
        }
    }
}
