using System.Linq;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class NamingPresentationTests
{
    [TestMethod]
    public void PresetsExposeTheThreeReviewedSchemes()
    {
        CollectionAssert.AreEqual(
            new[] { "Archival (default)", "Artist - Album (year)", "Simple" },
            NamingEngine.Presets.Select(preset => preset.Name).ToArray());
        Assert.AreEqual(NamingScheme.ArchivalTemplate, NamingEngine.Presets[0].Scheme.Template);
        Assert.IsFalse(NamingEngine.Presets[1].Scheme.ReleaseDescriptor);
        Assert.IsFalse(NamingEngine.Presets[2].Scheme.ExtractFeatured);
    }

    [TestMethod]
    public void PaletteContainsEverySupportedTokenExactlyOnce()
    {
        string[] expected =
        {
            "%albumartist%", "%artist%", "%album%", "%title%", "%tracknumber%", "%year%",
            "%disc%", "%discnumber%", "%totaldiscs%", "%discsubtitle%", "%releasedescriptor%", "%featsuffix%",
            "%label%", "%catalog%", "%barcode%", "%country%", "%genre%", "%originalyear%", "%isrc%",
            "%releasetype%", "%releasestatus%",
        };

        CollectionAssert.AreEqual(expected, NamingEngine.PaletteFields);
        Assert.AreEqual(expected.Length, NamingEngine.PaletteFields.Distinct().Count());
    }

    [TestMethod]
    public void PreviewExamplesRetainTheirReviewedLabelsAndTrackShapes()
    {
        var examples = NamingEngine.Examples();

        CollectionAssert.AreEqual(
            new[]
            {
                "Single artist",
                "Leading article + guest",
                "Multi-disc live set",
                "Various-artists soundtrack"
            },
            examples.Select(example => example.Label).ToArray());
        CollectionAssert.AreEqual(new[] { 2, 1, 2, 1 }, examples.Select(example => example.Tracks.Length).ToArray());
        Assert.AreEqual("Radiohead", examples[0].Tracks[0].AlbumArtist);
        Assert.AreEqual("Daft Punk", examples[1].Tracks[0].Artist.Split('.').Last().Trim());
        CollectionAssert.AreEqual(new[] { 1, 2 }, examples[2].Tracks.Select(track => track.DiscNumber).ToArray());
        CollectionAssert.AreEqual(
            new[] { "soundtrack", "compilation" },
            examples[3].Tracks[0].SecondaryTypes.ToArray());
    }
}
