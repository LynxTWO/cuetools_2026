using CUETools.Processor;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class NamingContextMapperTests
    {
        private static CUEMetadata TwoTrack()
        {
            var m = new CUEMetadata("id", 2)
            {
                Artist = "Genesis", Title = "Calling All Stations", Year = "1997", Genre = "Rock",
                DiscNumber = "1", TotalDiscs = "1", Barcode = "724385591020",
                Label = "Virgin", LabelNo = "CDV 2850", Country = "GB",
            };
            m.Tracks[0].Title = "Calling All Stations"; m.Tracks[0].Artist = "Genesis"; m.Tracks[0].ISRC = "GBAAA9700001";
            m.Tracks[1].Title = "Congo"; m.Tracks[1].Artist = "Genesis";
            return m;
        }

        [TestMethod]
        public void MapsExistingFields()
        {
            var c = NamingContextMapper.FromMetadata(TwoTrack(), 0, 2);
            Assert.AreEqual("Genesis", c.AlbumArtist);
            Assert.AreEqual("Calling All Stations", c.Album);
            Assert.AreEqual("Calling All Stations", c.Title);
            Assert.AreEqual("1997", c.Year);
            Assert.AreEqual("Rock", c.Genre);
            Assert.AreEqual("Virgin", c.Label);
            Assert.AreEqual("CDV 2850", c.Catalog);       // from LabelNo, not Barcode
            Assert.AreEqual("724385591020", c.Barcode);
            Assert.AreEqual("GB", c.Country);
            Assert.AreEqual("GBAAA9700001", c.Isrc);
            Assert.AreEqual(1, c.TrackNumber);
            Assert.AreEqual(2, c.TotalTracks);
        }

        [TestMethod]
        public void PhaseThreeFieldsAreEmpty()
        {
            var c = NamingContextMapper.FromMetadata(TwoTrack(), 1, 2);
            Assert.AreEqual("", c.PrimaryType);
            Assert.AreEqual("", c.ReleaseStatus);
            Assert.AreEqual(0, c.SecondaryTypes.Count);
            Assert.AreEqual("Congo", c.Title);
        }
    }
}
