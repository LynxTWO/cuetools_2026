using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CUETools.Wpf.Theme;

namespace CUETools.Wpf.Tests
{
    // The SLICE-013 port's shared contracts: the breakpoint numbers match the
    // Linux head's (both read the same App.Core values), and every rail title
    // carries parseable glyph data. Visual verification is the owner's eyes on
    // a Windows machine; these keep the data honest in CI meanwhile.
    [TestClass]
    public class RailStripPortTests
    {
        [TestMethod]
        public void TheBreakpointsMatchD076()
        {
            Assert.AreEqual(1140, RailBreakpointValues.FullAt);
            Assert.AreEqual(860, RailBreakpointValues.FloorBelow);
            Assert.AreEqual(860, RailBreakpointValues.HeldLayoutWidth);
            Assert.AreEqual(640, RailBreakpointValues.MinWindowWidth);
            Assert.AreEqual(480, RailBreakpointValues.MinWindowHeight);
        }

        [TestMethod]
        public void EveryRailTitleHasParseableGlyphData()
        {
            Assert.AreEqual(10, RailIconPaths.All.Length);
            Assert.AreEqual(10, RailIconPaths.All.Select(e => e.Title).Distinct().Count());
            foreach (var (title, path) in RailIconPaths.All)
            {
                var geometry = System.Windows.Media.Geometry.Parse(path);
                Assert.IsFalse(geometry.IsEmpty(), title + " parsed to an empty geometry");
            }
        }

        [TestMethod]
        public void AnUnknownTitleFallsBackToNull()
        {
            Assert.IsNull(RailIconPaths.ForTitle("No Such Page"));
            Assert.IsNull(RailIconPaths.ForTitle(null));
            Assert.IsNotNull(RailIconPaths.ForTitle("Rip"));
        }
    }
}
