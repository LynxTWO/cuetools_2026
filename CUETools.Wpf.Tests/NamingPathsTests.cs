using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class NamingPathsTests
    {
        [TestMethod]
        public void SingleDisc_CommonDirIsAlbum_RemaindersAreFilenames()
        {
            var (dir, rem) = NamingPaths.Split(new[] { "Artist - Album/01 - A", "Artist - Album/02 - B" });
            Assert.AreEqual("Artist - Album", dir);
            CollectionAssert.AreEqual(new[] { "01 - A", "02 - B" }, rem);
        }

        [TestMethod]
        public void MultiDisc_CommonDirIsAlbum_RemaindersKeepDiscFolder()
        {
            var (dir, rem) = NamingPaths.Split(new[] { "Alb/Disc 1/01 - A", "Alb/Disc 2/01 - B" });
            Assert.AreEqual("Alb", dir);
            CollectionAssert.AreEqual(new[] { "Disc 1/01 - A", "Disc 2/01 - B" }, rem);
        }

        [TestMethod]
        public void SingleTrack_DirIsAlbum_RemainderIsFilename()
        {
            var (dir, rem) = NamingPaths.Split(new[] { "Alb/01 - Only" });
            Assert.AreEqual("Alb", dir);
            CollectionAssert.AreEqual(new[] { "01 - Only" }, rem);
        }

        [TestMethod]
        public void NoCommonAlbumFolder_CommonDirEmpty()
        {
            var (dir, rem) = NamingPaths.Split(new[] { "A/01", "B/02" });
            Assert.AreEqual("", dir);
            CollectionAssert.AreEqual(new[] { "A/01", "B/02" }, rem);
        }

        [TestMethod]
        public void Empty_ReturnsEmpty()
        {
            var (dir, rem) = NamingPaths.Split(System.Array.Empty<string>());
            Assert.AreEqual("", dir);
            Assert.AreEqual(0, rem.Length);
        }

        // ---- EnsureUniqueTrackNames: no track may be lost to an empty or duplicate name ----

        [TestMethod]
        public void Unique_AllEmptyNames_BecomeTrackNumbers()
        {
            // measured failure this guards: a "%isrc%" template on a disc with no ISRCs rendered "" for
            // every track, so all of them resolved to the same file and a 12-track rip produced one file
            var r = NamingPaths.EnsureUniqueTrackNames(new[] { "", "", "" });
            CollectionAssert.AreEqual(new[] { "01", "02", "03" }, r);
        }

        [TestMethod]
        public void Unique_DuplicatesAreDisambiguated()
        {
            var r = NamingPaths.EnsureUniqueTrackNames(new[] { "Untitled", "Untitled", "Untitled" });
            Assert.AreEqual("Untitled", r[0]);
            Assert.AreNotEqual(r[0], r[1]);
            Assert.AreNotEqual(r[0], r[2]);
            Assert.AreNotEqual(r[1], r[2]);
        }

        [TestMethod]
        public void Unique_CaseInsensitiveDuplicatesAreCaught()
        {
            // Windows paths are case-insensitive, so these would collide on disk
            var r = NamingPaths.EnsureUniqueTrackNames(new[] { "Track", "TRACK" });
            Assert.AreNotEqual(r[0].ToLowerInvariant(), r[1].ToLowerInvariant());
        }

        [TestMethod]
        public void Unique_AlreadyUnique_IsUnchanged()
        {
            var input = new[] { "01 - A", "02 - B", "03 - C" };
            CollectionAssert.AreEqual(input, NamingPaths.EnsureUniqueTrackNames(input));
        }

        [TestMethod]
        public void Unique_PreservesDirectoryParts()
        {
            var r = NamingPaths.EnsureUniqueTrackNames(new[] { "Disc 1/", "Disc 2/" });
            StringAssert.StartsWith(r[0], "Disc 1/");
            StringAssert.StartsWith(r[1], "Disc 2/");
            Assert.AreEqual("Disc 1/01", r[0]);   // empty filename part -> track number
            Assert.AreEqual("Disc 2/02", r[1]);
        }

        // ---- CapPathLength: the old engine's total-length guard, restored ----

        [TestMethod]
        public void Cap_ShortNameIsUntouched()
        {
            var r = NamingPaths.CapPathLength(new[] { "01 - Short" }, outDirLength: 20, maxTotal: 250);
            Assert.AreEqual("01 - Short", r[0]);
        }

        [TestMethod]
        public void Cap_OverLongNameIsTruncatedToFit()
        {
            string longName = new string('X', 300);
            var r = NamingPaths.CapPathLength(new[] { longName }, outDirLength: 20, maxTotal: 250);
            Assert.IsTrue(20 + 1 + r[0].Length <= 250, "still over budget: " + r[0].Length);
        }

        [TestMethod]
        public void Cap_PreservesDirectoryPartAndKeepsAtLeastEightChars()
        {
            string longName = "Disc 1/" + new string('Y', 300);
            var r = NamingPaths.CapPathLength(new[] { longName }, outDirLength: 240, maxTotal: 250);
            StringAssert.StartsWith(r[0], "Disc 1/");
            string name = r[0].Substring("Disc 1/".Length);
            Assert.IsTrue(name.Length >= 8, "truncated below 8 chars of filename: " + name.Length);
        }

        [TestMethod]
        public void Fuzz_RecombinesToOriginal()
        {
            var rnd = new System.Random(20260725);
            for (int it = 0; it < 3000; it++)
            {
                int n = 1 + rnd.Next(12);
                var paths = new string[n];
                for (int i = 0; i < n; i++)
                {
                    int depth = rnd.Next(4);
                    var segs = new System.Collections.Generic.List<string>();
                    for (int d = 0; d < depth; d++) segs.Add("d" + rnd.Next(3));
                    segs.Add("f" + i);
                    paths[i] = string.Join("/", segs);
                }
                var (dir, rem) = NamingPaths.Split(paths);
                for (int i = 0; i < n; i++)
                {
                    string recombined = dir.Length > 0 ? dir + "/" + rem[i] : rem[i];
                    Assert.AreEqual(paths[i], recombined, "split must recombine to the original path");
                }
            }
        }
    }
}
