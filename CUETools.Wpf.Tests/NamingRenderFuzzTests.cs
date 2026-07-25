using System;
using System.IO;
using System.Text;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    /// <summary>
    /// Fuzzes NamingEngine.Render for filesystem safety. This is the gate the spec asked for and it is
    /// load-bearing now that Render produces the REAL output path: a rendered segment that is empty, is
    /// "." / "..", ends in a dot or space, carries an invalid character, or is drive-rooted would either
    /// abort the rip in Directory.CreateDirectory or write OUTSIDE the user's chosen output folder.
    /// Deterministic seed so any failure reproduces.
    /// </summary>
    [TestClass]
    public class NamingRenderFuzzTests
    {
        // characters that break paths, escape folders, or look like template syntax
        private const string Nasty = "/\\:*?<>|\"'%[]." + "\u0001\u001F" + " abcXY09";

        private static string NastyString(Random rnd, int maxLen)
        {
            int n = rnd.Next(0, maxLen + 1);
            var sb = new StringBuilder(n);
            for (int i = 0; i < n; i++) sb.Append(Nasty[rnd.Next(Nasty.Length)]);
            return sb.ToString();
        }

        [TestMethod]
        public void Fuzz_RenderedPathsAreAlwaysSafeAndContained()
        {
            var invalid = Path.GetInvalidFileNameChars();
            var rnd = new Random(20260725);
            string baseDir = @"D:\base";

            for (int iter = 0; iter < 4000; iter++)
            {
                // a random preset, with all four cleanup toggles independently flipped
                var preset = NamingEngine.Presets[rnd.Next(NamingEngine.Presets.Length)].Scheme.Clone();
                preset.ExtractFeatured = rnd.Next(2) == 0;
                preset.UnifySeparators = rnd.Next(2) == 0;
                preset.HandleArticles = rnd.Next(2) == 0;
                preset.StripIllegal = rnd.Next(2) == 0;      // cosmetic toggle must not affect SAFETY
                preset.ReleaseDescriptor = rnd.Next(2) == 0;

                var c = new NamingContext
                {
                    AlbumArtist = NastyString(rnd, 40),
                    Artist = NastyString(rnd, 40),
                    Album = NastyString(rnd, 40),
                    Title = NastyString(rnd, 40),
                    Year = rnd.Next(2) == 0 ? "1997" : NastyString(rnd, 6),
                    DiscNumber = rnd.Next(1, 4),
                    TotalDiscs = rnd.Next(1, 4),
                    DiscSubtitle = NastyString(rnd, 20),
                    TrackNumber = rnd.Next(1, 100),
                    TotalTracks = 99,
                    Label = NastyString(rnd, 20),
                    Catalog = NastyString(rnd, 20),
                    Barcode = NastyString(rnd, 20),
                    Country = NastyString(rnd, 8),
                    Genre = NastyString(rnd, 12),
                    OriginalYear = NastyString(rnd, 6),
                    Isrc = NastyString(rnd, 14),
                };
                // occasionally throw a very long value at it too
                if (iter % 97 == 0) c.Album = new string('X', 400);

                string result = NamingEngine.Render(c, preset);
                string why = $"iter {iter}, result '{result}'";

                Assert.IsFalse(Path.IsPathRooted(result), "rendered path is rooted - " + why);

                if (result.Length > 0)
                {
                    foreach (var seg in result.Split('/'))
                    {
                        Assert.AreNotEqual(0, seg.Length, "empty path segment - " + why);
                        Assert.AreNotEqual(".", seg, "'.' segment - " + why);
                        Assert.AreNotEqual("..", seg, "'..' segment - " + why);
                        Assert.IsFalse(seg.EndsWith(".") || seg.EndsWith(" "),
                            "segment ends with a dot or space - " + why);
                        Assert.AreEqual(-1, seg.IndexOfAny(invalid),
                            "segment contains an invalid filename char - " + why);
                    }
                }

                // must stay inside the chosen output folder
                string combined = Path.Combine(baseDir, result.Replace('/', Path.DirectorySeparatorChar));
                Assert.IsTrue(combined.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase),
                    "rendered path escaped the base folder - " + why + " -> " + combined);
            }
        }
    }
}
