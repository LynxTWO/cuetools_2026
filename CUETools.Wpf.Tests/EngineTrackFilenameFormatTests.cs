using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class EngineTrackFilenameFormatTests
    {
        // The WPF archival template - the exact value the Naming page pushes into the engine.
        private const string Archival =
            "%albumartist% - %album%[%releasedescriptor%]/[%disc%]%tracknumber% - %title%[%featsuffix%]";

        [TestMethod]
        public void Archival_ReducesToEngineSafeTrackName()
        {
            // folder dropped, WPF-only tokens + their [...] groups stripped -> a clean flat track name
            Assert.AreEqual("%tracknumber% - %title%", RipService.EngineTrackFilenameFormat(Archival));
        }

        [TestMethod]
        public void AlbumArtistTokenIsMappedToArtist()
        {
            // no folder here, so %albumartist% survives translation to the engine's %artist%
            Assert.AreEqual("%artist% - %tracknumber% - %title%",
                RipService.EngineTrackFilenameFormat("%albumartist% - %tracknumber% - %title%"));
        }

        [TestMethod]
        public void FolderPartIsDropped()
        {
            Assert.AreEqual("%tracknumber% - %title%",
                RipService.EngineTrackFilenameFormat("Whatever/Folder/%tracknumber% - %title%"));
        }

        [TestMethod]
        public void NoTrackNumber_FallsBackToDefault()
        {
            Assert.AreEqual("%tracknumber% - %title%", RipService.EngineTrackFilenameFormat("%title%"));
            Assert.AreEqual("%tracknumber% - %title%", RipService.EngineTrackFilenameFormat(""));
            Assert.AreEqual("%tracknumber% - %title%", RipService.EngineTrackFilenameFormat(null));
        }
    }
}
