using CUETools.Processor;

namespace CUETools.Wpf.Models;

/// <summary>
/// One metadata release that matched the inserted disc's TOC, with enough detail to see why it
/// matched and how good it is. Every entry already matches the disc layout (that is why it was
/// returned); ranking is by source quality (MusicBrainz/CTDB &gt; freedb &gt; embedded) and how
/// complete the metadata is (track titles, year, label, cover). The best-scoring one is marked.
/// </summary>
public sealed class ReleaseMatch
{
    public int Index { get; init; }
    public string Source { get; init; } = "";       // e.g. MusicBrainz, CTDB, freedb, cue, tags, local
    public string Artist { get; init; } = "";
    public string Title { get; init; } = "";
    public string Year { get; init; } = "";
    public string Detail { get; init; } = "";        // label / country / disc, if any
    public int TrackCount { get; init; }
    public int TitledTracks { get; init; }
    public bool HasCover { get; init; }
    public int Score { get; init; }
    public string Why { get; init; } = "";
    public bool IsBest { get; set; }

    /// <summary>The underlying metadata, so choosing this release applies it to the rip.</summary>
    public CUEMetadata? Metadata { get; init; }

    public string Header => (Year.Length > 0 ? Year + "  " : "") +
        (Artist.Length > 0 ? Artist : "Unknown Artist") + " - " + (Title.Length > 0 ? Title : "Unknown Title");

    public string SubLine
    {
        get
        {
            var parts = new System.Collections.Generic.List<string> { Source };
            parts.Add($"{TitledTracks}/{TrackCount} titles");
            if (HasCover) parts.Add("cover");
            if (Detail.Length > 0) parts.Add(Detail);
            return string.Join("  .  ", parts);
        }
    }
}
