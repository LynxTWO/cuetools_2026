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
