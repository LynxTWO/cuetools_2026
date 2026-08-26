using System.Collections.Generic;

namespace CUETools.Wpf.Services;

/// <summary>
/// Presentation data for the naming editor. These catalogs are kept outside the behavior-bearing
/// renderer so mutation results for path safety and naming semantics are not dominated by sample text.
/// </summary>
public static partial class NamingEngine
{
    public static readonly (string Name, NamingScheme Scheme)[] Presets =
    {
        ("Archival (default)", new NamingScheme()),
        ("Artist - Album (year)", new NamingScheme { Template = "%artist% - %album% (%year%)/%tracknumber% - %title%", ReleaseDescriptor = false }),
        ("Simple", new NamingScheme { Template = "%artist%/%album%/%tracknumber% - %title%", ReleaseDescriptor = false, ExtractFeatured = false }),
    };

    public static readonly string[] PaletteFields =
    {
        "%albumartist%", "%artist%", "%album%", "%title%", "%tracknumber%", "%year%",
        "%disc%", "%discnumber%", "%totaldiscs%", "%discsubtitle%", "%releasedescriptor%", "%featsuffix%",
        "%label%", "%catalog%", "%barcode%", "%country%", "%genre%", "%originalyear%", "%isrc%",
        "%releasetype%", "%releasestatus%",
    };

    public static IReadOnlyList<(string Label, NamingContext[] Tracks)> Examples()
    {
        return new List<(string, NamingContext[])>
        {
            ("Single artist", new[]
            {
                new NamingContext { AlbumArtist = "Radiohead", Artist = "Radiohead", Album = "OK Computer", Title = "Airbag", Year = "1997", TrackNumber = 1, TotalTracks = 12 },
                new NamingContext { AlbumArtist = "Radiohead", Artist = "Radiohead", Album = "OK Computer", Title = "Paranoid Android", Year = "1997", TrackNumber = 2, TotalTracks = 12 },
            }),
            ("Leading article + guest", new[]
            {
                new NamingContext { AlbumArtist = "The Weeknd", Artist = "The Weeknd feat. Daft Punk", Album = "Starboy", Title = "Starboy", Year = "2016", TrackNumber = 1, TotalTracks = 18 },
            }),
            ("Multi-disc live set", new[]
            {
                new NamingContext { AlbumArtist = "Pink Floyd", Artist = "Pink Floyd", Album = "Pulse", Title = "Shine On You Crazy Diamond", Year = "1995", DiscNumber = 1, TotalDiscs = 2, TrackNumber = 1, SecondaryTypes = new[] { "live" } },
                new NamingContext { AlbumArtist = "Pink Floyd", Artist = "Pink Floyd", Album = "Pulse", Title = "Money", Year = "1995", DiscNumber = 2, TotalDiscs = 2, TrackNumber = 3, SecondaryTypes = new[] { "live" } },
            }),
            ("Various-artists soundtrack", new[]
            {
                new NamingContext { AlbumArtist = "Various Artists", Artist = "a-ha", Album = "Grosse Pointe Blank", Title = "Take On Me", Year = "1997", TrackNumber = 1, SecondaryTypes = new[] { "soundtrack", "compilation" } },
            }),
        };
    }
}
