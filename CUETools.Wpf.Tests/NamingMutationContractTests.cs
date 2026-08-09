using System;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class NamingMutationContractTests
{
    private static NamingScheme Scheme(string template) => new()
    {
        Template = template,
        ExtractFeatured = false,
        UnifySeparators = false,
        HandleArticles = false,
        StripIllegal = true,
        ReleaseDescriptor = false,
    };

    [TestMethod]
    public void DefaultContextAndSchemeValuesRemainStable()
    {
        var context = new NamingContext();
        Assert.AreEqual("", context.AlbumArtist);
        Assert.AreEqual("", context.Artist);
        Assert.AreEqual("", context.Album);
        Assert.AreEqual("", context.Title);
        Assert.AreEqual("", context.Year);
        Assert.AreEqual(1, context.DiscNumber);
        Assert.AreEqual(1, context.TotalDiscs);
        Assert.AreEqual("", context.DiscSubtitle);
        Assert.AreEqual(1, context.TrackNumber);
        Assert.AreEqual(1, context.TotalTracks);
        Assert.AreEqual("album", context.PrimaryType);
        Assert.AreEqual(0, context.SecondaryTypes.Count);
        Assert.AreEqual("official", context.ReleaseStatus);
        Assert.AreEqual("", context.Label);
        Assert.AreEqual("", context.Catalog);
        Assert.AreEqual("", context.Barcode);
        Assert.AreEqual("", context.Country);
        Assert.AreEqual("", context.Genre);
        Assert.AreEqual("", context.OriginalYear);
        Assert.AreEqual("", context.Isrc);

        var scheme = new NamingScheme();
        Assert.AreEqual(NamingScheme.ArchivalTemplate, scheme.Template);
        Assert.IsTrue(scheme.ExtractFeatured);
        Assert.IsTrue(scheme.UnifySeparators);
        Assert.IsTrue(scheme.HandleArticles);
        Assert.IsTrue(scheme.StripIllegal);
        Assert.IsTrue(scheme.ReleaseDescriptor);

        NamingScheme clone = scheme.Clone();
        Assert.AreNotSame(scheme, clone);
        Assert.AreEqual(scheme.Template, clone.Template);
        Assert.AreEqual(scheme.ExtractFeatured, clone.ExtractFeatured);
        Assert.AreEqual(scheme.UnifySeparators, clone.UnifySeparators);
        Assert.AreEqual(scheme.HandleArticles, clone.HandleArticles);
        Assert.AreEqual(scheme.StripIllegal, clone.StripIllegal);
        Assert.AreEqual(scheme.ReleaseDescriptor, clone.ReleaseDescriptor);
    }

    [TestMethod]
    public void EveryNormalizedMetadataTokenUsesItsIntendedArticlePolicy()
    {
        var context = new NamingContext
        {
            AlbumArtist = "The Album Artist",
            Artist = "The Track Artist",
            Album = "The Album",
            Title = "The Title",
            DiscSubtitle = "The Disc",
            Label = "The Label",
            Catalog = "The Catalog",
            Barcode = "The Barcode",
            Country = "The Country",
            Genre = "The Genre",
            Isrc = "The Isrc",
        };
        NamingScheme scheme = Scheme(
            "%albumartist%~%artist%~%album%~%title%~%discsubtitle%~%label%~%catalog%~%barcode%~%country%~%genre%~%isrc%");
        scheme.HandleArticles = true;

        Assert.AreEqual(
            "Album Artist, The~Track Artist, The~The Album~The Title~The Disc~The Label~The Catalog~The Barcode~The Country~The Genre~The Isrc",
            NamingEngine.Render(context, scheme));
    }

    [TestMethod]
    public void ScalarAndDerivedTokensRenderExactValues()
    {
        var context = new NamingContext
        {
            AlbumArtist = "Artist",
            Artist = "Track Artist",
            Album = "Album",
            Title = "Title",
            Year = "2026-08-08",
            OriginalYear = "1999-01-01",
            TrackNumber = 7,
            DiscNumber = 2,
            TotalDiscs = 2,
            ReleaseStatus = "official",
            PrimaryType = "album",
        };

        Assert.AreEqual(
            "2026~07~2~2~1999~Album~Official",
            NamingEngine.Render(
                context,
                Scheme("%year%~%tracknumber%~%discnumber%~%totaldiscs%~%originalyear%~%releasetype%~%releasestatus%")));
    }

    [TestMethod]
    public void ExactlyFourDigitYearsAndOriginalYearFallbackArePreserved()
    {
        var context = new NamingContext { Year = "2026", OriginalYear = "" };

        Assert.AreEqual(
            "2026~2026",
            NamingEngine.Render(context, Scheme("%year%~%originalyear%")));
    }

    [TestMethod]
    public void MissingPrimaryFieldsUseStableFallbacks()
    {
        var context = new NamingContext
        {
            AlbumArtist = " ",
            Artist = null,
            Album = null,
            Title = "",
        };

        Assert.AreEqual(
            "Unknown Artist~Unknown Artist~Unknown Album~Untitled",
            NamingEngine.Render(context, Scheme("%albumartist%~%artist%~%album%~%title%")));
    }

    [TestMethod]
    public void UnknownTokensRemainVisibleAndDataBracketsSurvive()
    {
        var context = new NamingContext { Title = "[Live]" };

        Assert.AreEqual(
            "[Live]-%futuretoken%",
            NamingEngine.Render(context, Scheme("%title%-%futuretoken%")));
    }

    [TestMethod]
    public void EmptyOptionalSegmentsDoNotCreateEmptyDirectories()
    {
        var context = new NamingContext { Title = "Track", TotalDiscs = 1 };

        Assert.AreEqual(
            "Track",
            NamingEngine.Render(context, Scheme("[%disc%]/%title%")));
    }

    [TestMethod]
    public void DisabledDerivedTokensRemainEmptyEvenWhenTheirInputsArePopulated()
    {
        var context = new NamingContext
        {
            Artist = "Main feat. Guest",
            Year = "2026",
            TotalDiscs = 2,
        };
        NamingScheme scheme = Scheme("%releasedescriptor%~%featsuffix%");

        Assert.AreEqual("~", NamingEngine.Render(context, scheme));
    }

    [TestMethod]
    public void ControlCharactersAndTemplateLiteralInvalidCharactersAreAlwaysRemoved()
    {
        var context = new NamingContext { Title = "A\u001FB" };
        NamingScheme scheme = Scheme("\u001F\"%title%");
        scheme.StripIllegal = false;

        Assert.AreEqual("AB", NamingEngine.Render(context, scheme));
    }

    [TestMethod]
    public void StripIllegalChangesPresentationButNeverFilesystemSafety()
    {
        var context = new NamingContext { Title = "A:B" };
        NamingScheme on = Scheme("%title%");
        NamingScheme off = Scheme("%title%");
        off.StripIllegal = false;

        Assert.AreEqual("A - B", NamingEngine.Render(context, on));
        Assert.AreEqual("AB", NamingEngine.Render(context, off));
    }

    [TestMethod]
    public void StripIllegalAlsoAppliesToLiteralTemplateText()
    {
        NamingScheme on = Scheme("A:B");
        NamingScheme off = Scheme("A:B");
        off.StripIllegal = false;

        Assert.AreEqual("A - B", NamingEngine.Render(new NamingContext(), on));
        Assert.AreEqual("AB", NamingEngine.Render(new NamingContext(), off));
    }

    [TestMethod]
    public void SegmentLengthAndTrailingDotRulesApplyAtTheirBoundaries()
    {
        var context = new NamingContext { Title = new string('X', 101) };
        Assert.AreEqual(new string('X', 100), NamingEngine.Render(context, Scheme("%title%")));

        context.Title = "Name...   ";
        Assert.AreEqual("Name", NamingEngine.Render(context, Scheme("%title%")));

        Assert.AreEqual(
            "Literal Text",
            NamingEngine.Render(new NamingContext(), Scheme("Literal   Text...   ")));
    }

    [DataTestMethod]
    [DataRow("The")]
    [DataRow("A")]
    [DataRow("An")]
    [DataRow("Die")]
    [DataRow("Der")]
    [DataRow("Das")]
    [DataRow("Le")]
    [DataRow("La")]
    [DataRow("Les")]
    [DataRow("El")]
    [DataRow("Los")]
    [DataRow("Las")]
    [DataRow("Il")]
    [DataRow("Gli")]
    public void EverySupportedLeadingArticleIsSwapped(string article)
    {
        var context = new NamingContext { AlbumArtist = article + " Artist" };
        NamingScheme scheme = Scheme("%albumartist%");
        scheme.HandleArticles = true;

        Assert.AreEqual("Artist, " + article, NamingEngine.Render(context, scheme));
    }

    [DataTestMethod]
    [DataRow(" featuring ")]
    [DataRow(" feat. ")]
    [DataRow(" feat ")]
    [DataRow(" ft. ")]
    [DataRow(" ft ")]
    [DataRow(" pheaturing ")]
    public void EverySupportedFeaturedArtistMarkerMovesTheCredit(string marker)
    {
        var context = new NamingContext
        {
            AlbumArtist = "Main",
            Artist = "Main" + marker + "Guest",
        };
        NamingScheme scheme = Scheme("%artist%%featsuffix%");
        scheme.ExtractFeatured = true;

        Assert.AreEqual("Main (feat. Guest)", NamingEngine.Render(context, scheme));
    }

    [TestMethod]
    public void FeaturedMarkerAtTheStartAndAnEmptyGuestAreHandledExactly()
    {
        NamingScheme scheme = Scheme("%artist%%featsuffix%");
        scheme.ExtractFeatured = true;

        Assert.AreEqual(
            "(feat. Guest)",
            NamingEngine.Render(
                new NamingContext { Artist = " feat. Guest" },
                scheme));
        Assert.AreEqual(
            "Main",
            NamingEngine.Render(
                new NamingContext { Artist = "Main feat. " },
                scheme));
    }

    [TestMethod]
    public void FeaturedGuestsUseTheSeparatorToggleAndFiftyCharacterLimit()
    {
        var context = new NamingContext { Artist = "Main feat. Left and Right" };
        NamingScheme unified = Scheme("%featsuffix%");
        unified.ExtractFeatured = true;
        unified.UnifySeparators = true;
        NamingScheme original = Scheme("%featsuffix%");
        original.ExtractFeatured = true;

        Assert.AreEqual("(feat. Left & Right)", NamingEngine.Render(context, unified));
        Assert.AreEqual("(feat. Left and Right)", NamingEngine.Render(context, original));

        context.Artist = "Main feat. " + new string('G', 51);
        Assert.AreEqual(
            "(feat. " + new string('G', 50) + ")",
            NamingEngine.Render(context, unified));
    }

    [DataTestMethod]
    [DataRow(" meets ")]
    [DataRow(" X ")]
    [DataRow(" x ")]
    [DataRow(" vs. ")]
    [DataRow(" vs ")]
    [DataRow(" with ")]
    [DataRow(" and ")]
    [DataRow(" + ")]
    [DataRow(" \u00D7 ")]
    [DataRow("; ")]
    [DataRow(" | ")]
    [DataRow(" \u2022 ")]
    [DataRow(" \u00B7 ")]
    public void EverySupportedCollaborationSeparatorIsUnified(string separator)
    {
        var context = new NamingContext { Title = "Left" + separator + "Right" };
        NamingScheme scheme = Scheme("%title%");
        scheme.UnifySeparators = true;

        Assert.AreEqual("Left & Right", NamingEngine.Render(context, scheme));
    }

    [DataTestMethod]
    [DataRow("promo", "Promo")]
    [DataRow("promotional", "Promo")]
    [DataRow("bootleg", "Bootleg")]
    [DataRow("pseudo-release", "Pseudo")]
    public void ReleaseDescriptorCoversEveryNamedStatus(string status, string expected)
    {
        var context = new NamingContext
        {
            ReleaseStatus = status,
            PrimaryType = "album",
            Year = "",
        };

        Assert.AreEqual(
            "[" + expected + "]",
            NamingEngine.Render(context, new NamingScheme { Template = "%releasedescriptor%" }));
    }

    [DataTestMethod]
    [DataRow("soundtrack", "OST")]
    [DataRow("live", "Live")]
    [DataRow("dj-mix", "DJ Mix")]
    [DataRow("remix", "Remix")]
    [DataRow("mixtape/street", "Mixtape")]
    [DataRow("demo", "Demo")]
    [DataRow("compilation", "Compilation")]
    public void ReleaseDescriptorCoversEveryNamedSecondaryType(string secondary, string expected)
    {
        var context = new NamingContext
        {
            ReleaseStatus = "official",
            PrimaryType = "album",
            Year = "",
            SecondaryTypes = new[] { secondary },
        };

        Assert.AreEqual(
            "[" + expected + "]",
            NamingEngine.Render(context, new NamingScheme { Template = "%releasedescriptor%" }));
    }

    [TestMethod]
    public void ReleaseDescriptorIncludesDiscCountAndExactlyFourDigitYear()
    {
        var context = new NamingContext
        {
            TotalDiscs = 3,
            Year = "2026",
            ReleaseStatus = "official",
            PrimaryType = "album",
        };

        Assert.AreEqual(
            "[3-CD Set] (2026)",
            NamingEngine.Render(context, new NamingScheme { Template = "%releasedescriptor%" }));
    }

    [TestMethod]
    public void NullReleaseTypeInputsHaveDefinedOutputs()
    {
        var context = new NamingContext
        {
            PrimaryType = null,
            SecondaryTypes = null,
            ReleaseStatus = null,
            Year = null,
        };

        Assert.AreEqual("", NamingEngine.Render(context, Scheme("%releasetype%")));
        Assert.AreEqual(
            "",
            NamingEngine.Render(context, new NamingScheme { Template = "%releasedescriptor%" }));

        context.PrimaryType = "ep";
        Assert.AreEqual("EP", NamingEngine.Render(context, Scheme("%releasetype%")));
    }

    [TestMethod]
    public void NullOptionalTokenInputsRenderEmpty()
    {
        var context = new NamingContext
        {
            Year = null,
            DiscSubtitle = null,
            Label = null,
            Catalog = null,
            Barcode = null,
            Country = null,
            Genre = null,
            OriginalYear = null,
            Isrc = null,
            PrimaryType = null,
            ReleaseStatus = null,
        };

        Assert.AreEqual(
            "~~~~~~~~~~",
            NamingEngine.Render(
                context,
                Scheme("%year%~%discsubtitle%~%label%~%catalog%~%barcode%~%country%~%genre%~%originalyear%~%isrc%~%releasetype%~%releasestatus%")));
    }

    [TestMethod]
    public void EnabledFeatureSuffixWithoutAMarkerRendersEmpty()
    {
        NamingScheme scheme = Scheme("%featsuffix%");
        scheme.ExtractFeatured = true;

        Assert.AreEqual(
            "",
            NamingEngine.Render(new NamingContext { Artist = "Solo Artist" }, scheme));
    }

    [TestMethod]
    public void DiscFolderDoesNotSwapTheSubtitleArticle()
    {
        var context = new NamingContext
        {
            DiscNumber = 2,
            TotalDiscs = 2,
            DiscSubtitle = "The Bonus Disc",
        };
        NamingScheme scheme = Scheme("%disc%x");
        scheme.HandleArticles = true;

        Assert.AreEqual(
            "Disc 2 - The Bonus Disc/x",
            NamingEngine.Render(context, scheme));
    }
}
