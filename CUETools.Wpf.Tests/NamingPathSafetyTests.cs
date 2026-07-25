using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    /// <summary>
    /// Path-safety of rendered names. This matters more than it looks: NamingEngine.Render now produces
    /// the REAL output path, and Render splits its result on '/' to form folders. So any '/' or '\'
    /// arriving from metadata (artist "AC/DC", a title like "and/or") would silently become a directory
    /// separator and scatter tracks into unintended subfolders. Separator neutralisation therefore has to
    /// happen on the FIELD VALUES, before the template is split, and cannot be optional.
    /// </summary>
    [TestClass]
    public class NamingPathSafetyTests
    {
        private static NamingScheme Tpl(string t, bool stripIllegal = true) =>
            new NamingScheme { Template = t, ReleaseDescriptor = false, StripIllegal = stripIllegal };

        [TestMethod]
        public void SlashInArtist_DoesNotCreateAFolder()
        {
            var c = new NamingContext
            {
                AlbumArtist = "AC/DC", Artist = "AC/DC", Album = "Back in Black",
                Title = "Hells Bells", TrackNumber = 1,
            };
            string path = NamingEngine.Render(c, Tpl("%albumartist% - %album%/%tracknumber% - %title%"));
            // exactly one separator: the one the TEMPLATE asked for
            Assert.AreEqual(1, path.Split('/').Length - 1, "artist slash leaked an extra folder: " + path);
            StringAssert.StartsWith(path, "AC-DC - Back in Black/");
        }

        [TestMethod]
        public void SlashInTitle_StaysInTheFilename()
        {
            var c = new NamingContext
            {
                AlbumArtist = "A", Artist = "A", Album = "B", Title = "Him/Her", TrackNumber = 3,
            };
            string path = NamingEngine.Render(c, Tpl("%album%/%tracknumber% - %title%"));
            Assert.AreEqual(1, path.Split('/').Length - 1, "title slash leaked a folder: " + path);
            StringAssert.EndsWith(path, "03 - Him-Her");
        }

        [TestMethod]
        public void BackslashInTitle_StaysInTheFilename()
        {
            var c = new NamingContext
            {
                AlbumArtist = "A", Artist = "A", Album = "B", Title = @"Rock\Roll", TrackNumber = 4,
            };
            string path = NamingEngine.Render(c, Tpl("%album%/%tracknumber% - %title%"));
            Assert.IsFalse(path.Contains("\\"), "backslash survived into the path: " + path);
            StringAssert.EndsWith(path, "04 - Rock-Roll");
        }

        [TestMethod]
        public void SeparatorsAreNeutralisedEvenWithStripIllegalOff()
        {
            // StripIllegal is a cosmetic rule the user can turn off; path structure is not negotiable
            var c = new NamingContext
            {
                AlbumArtist = "AC/DC", Artist = "AC/DC", Album = "B", Title = "T", TrackNumber = 1,
            };
            string path = NamingEngine.Render(c, Tpl("%albumartist%/%tracknumber% - %title%", stripIllegal: false));
            Assert.AreEqual(1, path.Split('/').Length - 1, "separator leaked with StripIllegal off: " + path);
        }
    }
}
