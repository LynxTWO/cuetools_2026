using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    /// <summary>
    /// The disc-swap guard. A release is chosen for the disc that was in the drive when it was read, and
    /// nothing else notices a later swap: the tray poll and the disc re-read are both suppressed while a
    /// job runs (deliberately - a mid-rip re-read used to file results against a different album), and no
    /// code invalidates the cached release afterwards. Without this check a swapped disc is ripped under
    /// the PREVIOUS disc's album name and track titles.
    ///
    /// The design rule these tests pin: unknown is never a mismatch. Rejecting a legitimate rip because a
    /// metadata source carried no TOC id would be worse than the bug.
    /// </summary>
    [TestClass]
    public class DiscSwapGuardTests
    {
        private static bool Disagrees(string releaseId, int releaseTracks, string loadedId, int loadedTracks)
            => RipService.DiscDisagreesWithRelease(releaseId, releaseTracks, loadedId, loadedTracks, out _);

        [TestMethod]
        public void SameDisc_Agrees()
        {
            Assert.IsFalse(Disagrees("TOCID-A", 11, "TOCID-A", 11));
        }

        [TestMethod]
        public void DifferentTocId_Disagrees()
        {
            // the real swap: same track count, different disc
            Assert.IsTrue(Disagrees("TOCID-A", 11, "TOCID-B", 11));
        }

        [TestMethod]
        public void DifferentTrackCount_Disagrees()
        {
            Assert.IsTrue(Disagrees("TOCID-A", 11, "TOCID-A", 20));
        }

        [TestMethod]
        public void BothDiffer_Disagrees()
        {
            Assert.IsTrue(Disagrees("TOCID-A", 11, "TOCID-B", 20));
        }

        // ---- "unknown" must never block a rip ----

        [TestMethod]
        public void MissingReleaseId_IsNotAMismatch()
        {
            // a source that carried no TOC id must still be rippable
            Assert.IsFalse(Disagrees("", 11, "TOCID-A", 11));
        }

        [TestMethod]
        public void MissingLoadedId_IsNotAMismatch()
        {
            Assert.IsFalse(Disagrees("TOCID-A", 11, "", 11));
        }

        [TestMethod]
        public void ZeroTrackCounts_AreNotAMismatch()
        {
            Assert.IsFalse(Disagrees("TOCID-A", 0, "TOCID-A", 11));
            Assert.IsFalse(Disagrees("TOCID-A", 11, "TOCID-A", 0));
        }

        [TestMethod]
        public void MissingIdButDifferentTrackCount_StillDisagrees()
        {
            // the track count alone is enough evidence of a different disc
            Assert.IsTrue(Disagrees("", 11, "", 20));
        }

        [TestMethod]
        public void IdIsComparedExactly_NotCaseInsensitively()
        {
            // a TOC id is a computed hash, not a name - a case difference means a different value
            Assert.IsTrue(Disagrees("TOCID-a", 11, "TOCID-A", 11));
        }

        [TestMethod]
        public void TheIdFlag_ReportsWhichCheckFired()
        {
            RipService.DiscDisagreesWithRelease("A", 11, "B", 11, out bool idOnly);
            Assert.IsTrue(idOnly, "an id difference should be reported as an id disagreement");
            RipService.DiscDisagreesWithRelease("A", 11, "A", 20, out bool idForCount);
            Assert.IsFalse(idForCount, "a track-count difference is not an id disagreement");
        }
    }
}
