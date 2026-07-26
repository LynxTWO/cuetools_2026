using System;
using System.IO;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    /// <summary>
    /// The overwrite gate. A post-merge review found this app had NO equivalent of the legacy
    /// OutputExists() prompt, while every encoder opens with FileMode.Create - so a second rip that
    /// rendered the same album folder silently destroyed the first one's audio and artifacts. The worst
    /// reachable case needed no deliberate re-rip: a CD-Text or freedb release carries no disc number, so
    /// both discs of a multi-disc set render one identical folder and disc 2 lands on disc 1.
    /// </summary>
    [TestClass]
    public class OutputGuardTests
    {
        private string _root;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "outguard-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private string MakeAlbum(string name, params string[] files)
        {
            string dir = Path.Combine(_root, name);
            Directory.CreateDirectory(dir);
            foreach (var f in files) File.WriteAllText(Path.Combine(dir, f), "x");
            return dir;
        }

        [TestMethod]
        public void FreeName_IsUsedAsIs()
        {
            Assert.AreEqual("Artist - Album",
                OutputGuard.NonClobberingAlbumDir(_root, "Artist - Album", "flac"));
        }

        [TestMethod]
        public void EmptyDirectory_IsNotTreatedAsARip()
        {
            MakeAlbum("Artist - Album");   // exists but holds nothing
            Assert.AreEqual("Artist - Album",
                OutputGuard.NonClobberingAlbumDir(_root, "Artist - Album", "flac"));
        }

        [TestMethod]
        public void ExistingAudio_ForcesANewFolder()
        {
            MakeAlbum("Artist - Album", "01 - One.flac");
            Assert.AreEqual("Artist - Album (2)",
                OutputGuard.NonClobberingAlbumDir(_root, "Artist - Album", "flac"));
        }

        [DataTestMethod]
        [DataRow("album.cue")]
        [DataRow("album.log")]
        [DataRow("album.accurip")]
        [DataRow("folder.jpg")]
        [DataRow("rip.verify")]
        [DataRow("Test & Copy.log")]
        [DataRow(".cuetools-complete")]
        public void AnyFixedNameArtifact_CountsAsARip(string artifact)
        {
            // these cannot protect themselves: the engine's %unique% loop never applies to them
            MakeAlbum("Artist - Album", artifact);
            Assert.AreEqual("Artist - Album (2)",
                OutputGuard.NonClobberingAlbumDir(_root, "Artist - Album", "flac"));
        }

        [TestMethod]
        public void DifferentFormatInTheSameFolder_StillCountsViaArtifacts()
        {
            // ripping FLAC into a folder that holds an MP3 rip must not clobber its cue/log/cover
            MakeAlbum("Artist - Album", "01 - One.mp3", "album.cue");
            Assert.AreEqual("Artist - Album (2)",
                OutputGuard.NonClobberingAlbumDir(_root, "Artist - Album", "flac"));
        }

        [TestMethod]
        public void ItKeepsCountingUntilItFindsAFreeName()
        {
            MakeAlbum("Artist - Album", "album.cue");
            MakeAlbum("Artist - Album (2)", "album.cue");
            MakeAlbum("Artist - Album (3)", "album.cue");
            Assert.AreEqual("Artist - Album (4)",
                OutputGuard.NonClobberingAlbumDir(_root, "Artist - Album", "flac"));
        }

        [TestMethod]
        public void TheMultiDiscCollision_TheReviewFound_IsPrevented()
        {
            // A CD-Text release sets no disc number, so NamingEngine renders no "Disc N" level and both
            // discs of a set produce the SAME folder. Disc 1 is ripped, then disc 2 renders the same
            // name - it must NOT land on top of disc 1.
            const string rendered = "Various Artists - Blues Giants";
            MakeAlbum(rendered, "01 - Recession Blues.flac", "album.cue", "rip.verify");

            string forDisc2 = OutputGuard.NonClobberingAlbumDir(_root, rendered, "flac");
            Assert.AreNotEqual(rendered, forDisc2, "disc 2 would have overwritten disc 1");
            Assert.IsFalse(Directory.Exists(Path.Combine(_root, forDisc2)),
                "the chosen folder must be free");
            // and disc 1's audio is untouched
            Assert.IsTrue(File.Exists(Path.Combine(_root, rendered, "01 - Recession Blues.flac")));
        }

        [TestMethod]
        public void MultiSegmentAlbumDir_IsHandled()
        {
            // a multi-disc scheme renders "Album [3-CD Set]/Disc 2"
            string nested = Path.Combine(_root, "Album [3-CD Set]", "Disc 2");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "album.cue"), "x");
            string rel = "Album [3-CD Set]" + Path.DirectorySeparatorChar + "Disc 2";
            Assert.AreNotEqual(rel, OutputGuard.NonClobberingAlbumDir(_root, rel, "flac"));
        }

        [TestMethod]
        public void NothingIsEverDeleted()
        {
            string dir = MakeAlbum("Artist - Album", "01 - One.flac", "album.cue");
            OutputGuard.NonClobberingAlbumDir(_root, "Artist - Album", "flac");
            Assert.IsTrue(File.Exists(Path.Combine(dir, "01 - One.flac")));
            Assert.IsTrue(File.Exists(Path.Combine(dir, "album.cue")));
        }

        [TestMethod]
        public void FileOccupyingAlbumPathForcesANewFolder()
        {
            File.WriteAllText(Path.Combine(_root, "Artist - Album"), "foreign");

            Assert.AreEqual("Artist - Album (2)",
                OutputGuard.NonClobberingAlbumDir(
                    _root, "Artist - Album", "flac"));
        }

        [TestMethod]
        public void TraversalAndProbeFailuresFailClosed()
        {
            Assert.ThrowsException<ArgumentException>(() =>
                OutputGuard.NonClobberingAlbumDir(
                    _root, "", "flac"));
            Assert.ThrowsException<IOException>(() =>
                OutputGuard.NonClobberingAlbumDir(
                    _root, Path.Combine("..", "escape"), "flac"));
            Assert.ThrowsException<IOException>(() =>
                OutputGuard.NonClobberingAlbumDir(
                    _root, "invalid\0album", "flac"));
        }
    }
}
