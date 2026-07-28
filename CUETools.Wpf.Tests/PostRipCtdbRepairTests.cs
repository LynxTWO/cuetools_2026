using System;
using System.IO;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public sealed class PostRipCtdbRepairTests
    {
        private string _root;

        [TestInitialize]
        public void SetUp()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "cuetools-post-rip-repair-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TestCleanup]
        public void CleanUp()
        {
            string tempPrefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullRoot = Path.GetFullPath(_root);
            if (fullRoot.StartsWith(
                    tempPrefix,
                    StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(fullRoot).StartsWith(
                    "cuetools-post-rip-repair-",
                    StringComparison.Ordinal) &&
                Directory.Exists(fullRoot))
            {
                Directory.Delete(fullRoot, true);
            }
        }

        [TestMethod]
        public void TrackSetUsesItsAlbumCue()
        {
            string first = Write("01.flac");
            string second = Write("02.flac");
            Write("album.cue");

            string relative = RipService.FindRepairSourceRelativePath(
                _root,
                new[] { first, second });

            Assert.AreEqual("album.cue", relative);
        }

        [TestMethod]
        public void TrackSetUsesItsPortableNamedCue()
        {
            string first = Write("01.flac");
            string second = Write("02.flac");
            Write("Artist - Album (1997).cue");

            string relative = RipService.FindRepairSourceRelativePath(
                _root,
                new[] { first, second });

            Assert.AreEqual("Artist - Album (1997).cue", relative);
        }

        [TestMethod]
        public void MultipleAlbumCuesAreAmbiguousAndRefused()
        {
            string first = Write("01.flac");
            string second = Write("02.flac");
            Write("album.cue");
            Write("Artist - Album.cue");

            string relative = RipService.FindRepairSourceRelativePath(
                _root,
                new[] { first, second });

            Assert.AreEqual("", relative);
        }

        [TestMethod]
        public void SingleImageWithoutExternalCueUsesTheOnlyAudioFile()
        {
            string image = Write("disc-image.flac");

            string relative = RipService.FindRepairSourceRelativePath(
                _root,
                new[] { image });

            Assert.AreEqual("disc-image.flac", relative);
        }

        [TestMethod]
        public void LooseTrackSetWithoutCueIsAmbiguousAndRefused()
        {
            string first = Write("01.flac");
            string second = Write("02.flac");

            string relative = RipService.FindRepairSourceRelativePath(
                _root,
                new[] { first, second });

            Assert.AreEqual("", relative);
        }

        [TestMethod]
        public void SinglePathOutsidePublishedAlbumIsRefused()
        {
            string outside = Path.Combine(
                Path.GetDirectoryName(_root),
                "outside-" + Guid.NewGuid().ToString("N") + ".flac");
            File.WriteAllText(outside, "audio");
            try
            {
                string relative = RipService.FindRepairSourceRelativePath(
                    _root,
                    new[] { outside });

                Assert.AreEqual("", relative);
            }
            finally
            {
                File.Delete(outside);
            }
        }

        private string Write(string name)
        {
            string path = Path.Combine(_root, name);
            File.WriteAllText(path, "fixture");
            return path;
        }
    }
}
