using System.IO;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    /// <summary>
    /// Box sets at scale. The critical review finding was that a commit path which kept only the LAST
    /// rendered segment collapsed every disc of every multi-disc set into one shared "Disc N" folder and
    /// overwrote. These tests pin the behaviour the fix relies on - that the rendered album directory can
    /// be MORE than one segment, and that re-homing it under the output base stays contained - at 2, 100
    /// and 250 discs, so nothing about the fix is specific to the 2-CD case.
    /// </summary>
    [TestClass]
    public class BoxSetNamingTests
    {
        private static string RenderTrack(int discNumber, int totalDiscs, int trackNumber)
        {
            var c = new NamingContext
            {
                AlbumArtist = "Bach", Artist = "Bach", Album = "Complete Works",
                Title = "Cantata", Year = "2000",
                DiscNumber = discNumber, TotalDiscs = totalDiscs,
                TrackNumber = trackNumber, TotalTracks = 12,
            };
            return NamingEngine.Render(c, new NamingScheme());   // the archival default
        }

        [TestMethod]
        public void MultiDisc_RendersAnAlbumFolderPlusADiscFolder()
        {
            string p = RenderTrack(discNumber: 2, totalDiscs: 2, trackNumber: 1);
            var segs = p.Split('/');
            Assert.AreEqual(3, segs.Length, "expected album/disc/file, got: " + p);
            StringAssert.Contains(segs[0], "[2-CD Set]");
            Assert.AreEqual("Disc 2", segs[1]);
        }

        [TestMethod]
        public void HundredDiscSet_EachDiscKeepsItsOwnFolderUnderOneAlbum()
        {
            // the exact scenario the finding warned about, at 100 discs: every disc must resolve to a
            // DIFFERENT directory, and all of them under the SAME album folder
            string album = null;
            var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int disc = 1; disc <= 100; disc++)
            {
                string p = RenderTrack(disc, 100, 1);
                var split = NamingPaths.Split(new[] { p });
                string commonDir = split.commonDir;

                var segs = commonDir.Split('/');
                Assert.AreEqual(2, segs.Length, "album dir should be album + disc, got: " + commonDir);
                StringAssert.Contains(segs[0], "[100-CD Set]");
                // padded to the set's width (100 discs -> 3 digits) so folders sort in disc order;
                // see DiscPaddingTests for the padding rules themselves
                Assert.AreEqual("Disc " + disc.ToString("000"), segs[1]);

                album ??= segs[0];
                Assert.AreEqual(album, segs[0], "all discs must share one album folder");
                Assert.IsTrue(seen.Add(commonDir), "two discs collided on the same folder: " + commonDir);
            }
            Assert.AreEqual(100, seen.Count);
        }

        [TestMethod]
        public void RehomingAMultiSegmentAlbumDir_StaysContainedAndNested()
        {
            // this is what the commit path does: Path.Combine(base, <rendered relative dir>)
            string p = RenderTrack(discNumber: 57, totalDiscs: 100, trackNumber: 3);
            string commonDir = NamingPaths.Split(new[] { p }).commonDir;
            string baseDir = @"D:\Music";

            string combined = Path.Combine(baseDir, commonDir.Replace('/', Path.DirectorySeparatorChar));
            StringAssert.StartsWith(combined, baseDir + Path.DirectorySeparatorChar);
            StringAssert.EndsWith(combined, Path.DirectorySeparatorChar + "Disc 057");
            // and it is genuinely nested, not flattened onto the base
            Assert.AreEqual(baseDir.Split(Path.DirectorySeparatorChar).Length + 2,
                combined.Split(Path.DirectorySeparatorChar).Length,
                "expected base/album/Disc 57: " + combined);
        }

        [TestMethod]
        public void VeryLargeSet_DiscFoldersRemainDistinct()
        {
            // 250 discs: guards against any padding/formatting change silently merging discs
            var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int disc = 1; disc <= 250; disc++)
                Assert.IsTrue(seen.Add(NamingPaths.Split(new[] { RenderTrack(disc, 250, 1) }).commonDir),
                    "disc " + disc + " collided with an earlier disc's folder");
            Assert.AreEqual(250, seen.Count);
        }
    }
}
