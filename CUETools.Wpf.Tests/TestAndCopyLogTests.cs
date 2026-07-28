using System.Collections.Generic;
using CUETools.Wpf.Accuracy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class TestAndCopyLogTests
    {
        private static VerifyRecord Read(int arConf, int ctConf, params uint[] c32)
        {
            var t = new TrackCrc[c32.Length];
            for (int i = 0; i < c32.Length; i++) t[i] = new TrackCrc { ArV2 = c32[i], Crc32 = c32[i] };
            return new VerifyRecord { Tracks = t, ArConfidence = arConf, CtdbConfidence = ctConf };
        }

        [TestMethod]
        public void Passed_LogSaysPassedAndListsReads()
        {
            var reads = new List<VerifyRecord> { Read(3, 5, 0xAA, 0xBB), Read(3, 5, 0xAA, 0xBB) };
            var res = TestAndCopyResolver.Resolve(reads, new[] { false, true });
            string log = TestAndCopyLog.Format(res, reads, "DISC1", "TEST DRIVE", 6, 0);
            StringAssert.Contains(log, "Test & Copy PASSED");
            StringAssert.Contains(log, "Reads: 2");
            StringAssert.Contains(log, "AccurateRip: accurate, confidence 3");
            StringAssert.Contains(log, "AA");        // CRC32 rendered
            Assert.IsFalse(log.Contains("HELD"));
        }

        [TestMethod]
        public void Held_LogNamesHeldTrackAndUnrecoverableWarning()
        {
            var reads = new List<VerifyRecord> { Read(0, 0, 0xAA, 0x11), Read(0, 0, 0xAA, 0x22) };
            var res = TestAndCopyResolver.Resolve(reads, new[] { false, true });
            string log = TestAndCopyLog.Format(res, reads, "DISC1", "TEST DRIVE", 6, 2);
            StringAssert.Contains(log, "Test & Copy HELD");
            StringAssert.Contains(log, "track(s): 2");        // 1-based
            StringAssert.Contains(log, "AccurateRip: not found");
            StringAssert.Contains(log, "unrecoverable");
        }

        [TestMethod]
        public void DamagedAgreement_LogSaysConsistentAndCtdbRepairable_NotPassedOrNotFound()
        {
            var first = Read(0, 0, 0xAA, 0xBB);
            var second = Read(0, 0, 0xAA, 0xBB);
            first.ArTotal = second.ArTotal = 82;
            first.CtdbTotal = second.CtdbTotal = 234;
            var reads = new List<VerifyRecord> { first, second };
            var result = TestAndCopyResolver.Resolve(reads, new[] { false, true });

            string log = TestAndCopyLog.Format(
                result,
                reads,
                "DISC1",
                "TEST DRIVE",
                6,
                failedWindows: 3,
                ctdbHasErrors: true,
                ctdbCanRecover: true,
                ctdbRepairSectors: 6,
                ctdbRepairRanges: "261388,261409");

            StringAssert.Contains(log, "Test & Copy CONSISTENT");
            StringAssert.Contains(log, "CTDB repair required");
            StringAssert.Contains(
                log,
                "CTDB:        found, no exact match; repairable (6 sector(s))");
            StringAssert.Contains(log, "AccurateRip: found, no exact match");
            StringAssert.Contains(log, "CTDB repair sectors: 261388,261409");
            Assert.IsFalse(log.Contains("Test & Copy PASSED"));
            Assert.IsFalse(log.Contains("CTDB:        not found"));
        }
    }
}
