using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class NamingTokenTests
    {
        private static NamingScheme Tpl(string t) => new NamingScheme { Template = t, ReleaseDescriptor = false };

        [TestMethod]
        public void NewTokens_RenderFromContext()
        {
            var c = new NamingContext
            {
                AlbumArtist = "A", Artist = "A", Album = "Alb", Title = "T", TrackNumber = 1,
                Label = "Sub Pop", Catalog = "SP-123", Barcode = "0987654321",
                Country = "US", Genre = "Rock", OriginalYear = "1991", Isrc = "USAB11700001",
            };
            Assert.AreEqual("Sub Pop", NamingEngine.Render(c, Tpl("%label%")));
            Assert.AreEqual("SP-123", NamingEngine.Render(c, Tpl("%catalog%")));
            Assert.AreEqual("0987654321", NamingEngine.Render(c, Tpl("%barcode%")));
            Assert.AreEqual("US", NamingEngine.Render(c, Tpl("%country%")));
            Assert.AreEqual("Rock", NamingEngine.Render(c, Tpl("%genre%")));
            Assert.AreEqual("1991", NamingEngine.Render(c, Tpl("%originalyear%")));
            Assert.AreEqual("USAB11700001", NamingEngine.Render(c, Tpl("%isrc%")));
        }

        [TestMethod]
        public void TypeStatusTokens_EmptyWhenUnset()
        {
            // phase 1: the mapper leaves these blank, so the tokens render empty (no literal token left)
            var c = new NamingContext { Album = "Alb", Title = "T", TrackNumber = 1, PrimaryType = "", ReleaseStatus = "" };
            Assert.AreEqual("", NamingEngine.Render(c, Tpl("%releasetype%")));
            Assert.AreEqual("", NamingEngine.Render(c, Tpl("%releasestatus%")));
        }

        [TestMethod]
        public void ReleaseType_DerivesFromPrimaryAndSecondary()
        {
            var c = new NamingContext { Album = "Alb", Title = "T", TrackNumber = 1,
                PrimaryType = "album", SecondaryTypes = new[] { "live" } };
            Assert.AreEqual("Live Album", NamingEngine.Render(c, Tpl("%releasetype%")));
        }

        [DataTestMethod]
        [DataRow("compilation", "Compilation Album")]
        [DataRow("soundtrack", "Soundtrack")]
        [DataRow("remix", "Remix Album")]
        [DataRow("demo", "Demo Album")]
        public void ReleaseTypeCoversEveryNamedSecondaryPrecedence(
            string secondary,
            string expected)
        {
            var context = new NamingContext
            {
                PrimaryType = "album",
                SecondaryTypes = new[] { secondary }
            };

            Assert.AreEqual(expected, NamingEngine.Render(context, Tpl("%releasetype%")));
        }

        [DataTestMethod]
        [DataRow("single", " [Single]")]
        [DataRow("ep", " [EP]")]
        [DataRow("broadcast", " [FM]")]
        [DataRow("other", " [Other]")]
        [DataRow("album", "")]
        [DataRow("", "")]
        public void ReleaseDescriptorDistinguishesEveryPrimaryType(
            string primary,
            string expected)
        {
            var context = new NamingContext
            {
                PrimaryType = primary,
                Year = "",
                ReleaseStatus = "official"
            };
            var scheme = new NamingScheme { Template = "%releasedescriptor%" };

            Assert.AreEqual(expected.Trim(), NamingEngine.Render(context, scheme));
        }

        [TestMethod]
        public void NullSecondaryEntriesAreIgnored()
        {
            var context = new NamingContext
            {
                PrimaryType = "album",
                SecondaryTypes = new string[] { null, "live" }
            };

            Assert.AreEqual("Live Album", NamingEngine.Render(context, Tpl("%releasetype%")));
            Assert.AreEqual(
                "[Live]",
                NamingEngine.Render(
                    context,
                    new NamingScheme { Template = "%releasedescriptor%" }));
        }

        [TestMethod]
        public void BareArticleIsNotSwappedIntoAnEmptyArtistName()
        {
            var context = new NamingContext { AlbumArtist = "The ", Artist = "The " };

            Assert.AreEqual("The", NamingEngine.Render(context, Tpl("%albumartist%")));
        }
    }
}
