using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    /// <summary>
    /// Regressions found by the settings audit, stated as properties so they cannot come back.
    /// Both were real defects in the naming path: a scheme that produces no shared album directory,
    /// and the "extract featured artists" toggle deleting the credit it was supposed to move.
    /// </summary>
    [TestClass]
    public class NamingAuditRegressionTests
    {
        // ---- C6: there must always be an album directory ----

        [TestMethod]
        public void TemplateWithNoFolderPart_LeavesNoSharedAlbumDirectory()
        {
            // documents WHY the services need a fallback: Split legitimately returns "" here, and the
            // caller must not use that as the album folder or the .cue/log/cover land in the output root
            var scheme = new NamingScheme { Template = "%tracknumber% - %title%", ReleaseDescriptor = false };
            var rel = new[]
            {
                NamingEngine.Render(Ctx("A", "Alb", "One", 1), scheme),
                NamingEngine.Render(Ctx("A", "Alb", "Two", 2), scheme),
            };
            var split = NamingPaths.Split(rel);
            Assert.AreEqual("", split.commonDir, "expected no album dir for a folder-less template");
        }

        [TestMethod]
        public void VariousArtistsUnderTheSimplePreset_LeavesNoSharedAlbumDirectory()
        {
            // the "Simple" preset leads with %artist%, which differs per track on a VA disc, so there is
            // no shared leading segment - the second confirmed form of the same defect
            var simple = new NamingScheme
            {
                Template = "%artist%/%album%/%tracknumber% - %title%",
                ReleaseDescriptor = false, ExtractFeatured = false,
            };
            var rel = new[]
            {
                NamingEngine.Render(Ctx("Muddy Waters", "Blues", "One", 1), simple),
                NamingEngine.Render(Ctx("Howlin Wolf", "Blues", "Two", 2), simple),
            };
            var split = NamingPaths.Split(rel);
            Assert.AreEqual("", split.commonDir, "expected no album dir when the leading segment varies");
            // each track still keeps its own artist folder
            StringAssert.StartsWith(split.remainders[0], "Muddy Waters/");
            StringAssert.StartsWith(split.remainders[1], "Howlin Wolf/");
        }

        // ---- C7: the featured-artist toggle must never lose information ----

        [TestMethod]
        public void ExtractFeaturedOff_KeepsTheGuestCreditInline()
        {
            var off = new NamingScheme
            {
                Template = "%albumartist%/%tracknumber% - %title%",
                ExtractFeatured = false, ReleaseDescriptor = false, HandleArticles = false,
            };
            string path = NamingEngine.Render(Ctx("The Weeknd feat. Daft Punk", "Starboy", "Starboy", 1), off);
            StringAssert.Contains(path, "Daft Punk", "the guest credit was deleted with the toggle OFF: " + path);
        }

        [TestMethod]
        public void ExtractFeaturedOn_MovesTheCreditToTheSuffix()
        {
            var on = new NamingScheme
            {
                Template = "%albumartist%/%tracknumber% - %title%%featsuffix%",
                ExtractFeatured = true, ReleaseDescriptor = false, HandleArticles = false,
            };
            string path = NamingEngine.Render(Ctx("The Weeknd feat. Daft Punk", "Starboy", "Starboy", 1), on);
            StringAssert.Contains(path, "(feat. Daft Punk)", "expected the credit as a suffix: " + path);
            // and the artist folder itself no longer carries it
            Assert.IsFalse(path.Split('/')[0].Contains("feat."), "artist folder kept the credit: " + path);
        }

        [TestMethod]
        public void EitherWay_NoInformationIsLost()
        {
            // the property that matters: OFF must not produce LESS of the credited artist than ON
            var on = new NamingScheme { Template = "%albumartist% - %title%%featsuffix%", ExtractFeatured = true, ReleaseDescriptor = false, HandleArticles = false };
            var off = new NamingScheme { Template = "%albumartist% - %title%%featsuffix%", ExtractFeatured = false, ReleaseDescriptor = false, HandleArticles = false };
            var ctx = Ctx("Santana feat. Rob Thomas", "Supernatural", "Smooth", 1);
            Assert.IsTrue(NamingEngine.Render(ctx, off).Contains("Rob Thomas"));
            Assert.IsTrue(NamingEngine.Render(ctx, on).Contains("Rob Thomas"));
        }

        [TestMethod]
        public void ATitleContainingFeat_IsNeverTruncated()
        {
            foreach (bool extract in new[] { true, false })
            {
                var s = new NamingScheme
                {
                    Template = "%tracknumber% - %title%",
                    ExtractFeatured = extract, ReleaseDescriptor = false,
                };
                string path = NamingEngine.Render(Ctx("Nas", "Album", "Intro feat. AZ", 1), s);
                StringAssert.Contains(path, "AZ", $"title truncated with ExtractFeatured={extract}: {path}");
            }
        }

        private static NamingContext Ctx(string artist, string album, string title, int track) => new NamingContext
        {
            AlbumArtist = artist, Artist = artist, Album = album, Title = title,
            TrackNumber = track, TotalTracks = 12,
        };
    }
}
