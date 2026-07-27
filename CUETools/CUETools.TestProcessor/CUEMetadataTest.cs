using CUETools.Processor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.TestProcessor
{
    [TestClass]
    public class CUEMetadataTest
    {
        [TestMethod]
        public void MergeCopiesTrackCommentWithoutTrackArtist()
        {
            var target = new CUEMetadata("target", 1);
            var source = new CUEMetadata("source", 1);
            source.Tracks[0].Comment = "Source note";

            target.Merge(source, overwrite: false);

            Assert.AreEqual("Source note", target.Tracks[0].Comment);
        }

        [TestMethod]
        public void MergeDoesNotOverwriteTrackCommentWithEmptySource()
        {
            var target = new CUEMetadata("target", 1);
            target.Tracks[0].Comment = "Keep this note";
            var source = new CUEMetadata("source", 1);
            source.Tracks[0].Artist = "Source artist";

            target.Merge(source, overwrite: true);

            Assert.AreEqual("Keep this note", target.Tracks[0].Comment);
        }
    }
}
