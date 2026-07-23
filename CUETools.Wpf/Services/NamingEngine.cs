using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CUETools.Wpf.Services;

/// <summary>The metadata for one track, the input to the naming engine. Populated from whichever
/// source filled the release (MusicBrainz/CTDB is richest; freedb and CD-Text fill fewer fields).
/// Missing fields degrade cleanly - a rule that references an absent field simply omits it.</summary>
public sealed class NamingContext
{
    public string AlbumArtist = "";
    public string Artist = "";        // the track artist (may carry featured guests)
    public string Album = "";
    public string Title = "";
    public string Year = "";          // 4-digit
    public int DiscNumber = 1;
    public int TotalDiscs = 1;
    public string DiscSubtitle = "";
    public int TrackNumber = 1;
    public int TotalTracks = 1;
    public string PrimaryType = "album";           // album | single | ep | broadcast | other
    public IReadOnlyList<string> SecondaryTypes = Array.Empty<string>();  // live, compilation, soundtrack, remix, demo, dj-mix, ...
    public string ReleaseStatus = "official";      // official | promo | bootleg | pseudo-release
}

/// <summary>The user's naming scheme: the path template plus the clean-up rule toggles. Serialized
/// compactly into the app settings.</summary>
public sealed class NamingScheme
{
    public string Template = ArchivalTemplate;
    public bool ExtractFeatured = true;      // guest performers -> " (feat. X)"
    public bool UnifySeparators = true;      // " X ", " vs ", " meets ", " + " ... -> " & "
    public bool HandleArticles = true;       // "The Beatles" -> "Beatles, The" (sort-friendly)
    public bool StripIllegal = true;         // remove " * ? < > | ; ':' -> ' - '
    public bool ReleaseDescriptor = true;    // " (year) [Live] [OST] [EP] [N-CD Set] ..."

    // The default = the owner's Picard scheme, CD-adapted. The OPTIONAL derived tokens are wrapped
    // in [ ] - the CUETools processor's conditional-section syntax, so an empty descriptor / disc /
    // feat-suffix disappears in real rip output instead of leaving a literal token. The naming
    // engine strips these template brackets before substituting (data brackets in a title survive).
    public const string ArchivalTemplate =
        "%albumartist% - %album%[%releasedescriptor%]/[%disc%]%tracknumber% - %title%[%featsuffix%]";

    public NamingScheme Clone() => (NamingScheme)MemberwiseClone();
}

/// <summary>
/// Portable, UI-free naming engine. Turns a <see cref="NamingContext"/> + a <see cref="NamingScheme"/>
/// into a relative path ('/' = folder separator), each segment cleansed for the filesystem. The
/// distilled intent of the owner's MusicBrainz Picard script lives in the rule methods below - no
/// scripting interpreter, just a fixed set of well-tested transforms.
/// </summary>
public static class NamingEngine
{
    // built-in presets shown in the editor's picker
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
    };

    /// <summary>Render a full relative path for one track.</summary>
    public static string Render(NamingContext c, NamingScheme s)
    {
        var v = BuildVars(c, s);
        // strip the processor's conditional-section brackets from the TEMPLATE before substituting,
        // so the preview matches real output; data brackets inside a title are added during
        // substitution and therefore survive
        string tpl = (s.Template ?? "").Replace("[", "").Replace("]", "");
        string result = Substitute(tpl, v);
        // split on '/' into path segments and cleanse each; keep the separators
        var segments = result.Split('/');
        for (int i = 0; i < segments.Length; i++)
            segments[i] = TidySegment(segments[i], s);
        // drop empty segments (e.g. %disc% empty on single-disc) but keep the final filename
        return string.Join("/", segments.Where(seg => seg.Length > 0));
    }

    private static Dictionary<string, string> BuildVars(NamingContext c, NamingScheme s)
    {
        // article swap (Picard $swapprefix) is an ARTIST-only transform - it makes "The Beatles"
        // sort as "Beatles, The" in folders, but must never touch a title ("A Hard Day's Night"
        // stays put). So only the artist fields pass swapArticles:true.
        string aa = Normalize(FirstNonEmpty(c.AlbumArtist, c.Artist, "Unknown Artist"), s, 80, swapArticles: true);
        string ta = Normalize(FirstNonEmpty(c.Artist, c.AlbumArtist, "Unknown Artist"), s, 80, swapArticles: true);
        string album = Normalize(FirstNonEmpty(c.Album, "Unknown Album"), s, 100, swapArticles: false);
        string title = Normalize(FirstNonEmpty(c.Title, "Untitled"), s, 100, swapArticles: false);

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["albumartist"] = aa,
            ["artist"] = ta,
            ["album"] = album,
            ["title"] = title,
            ["year"] = (c.Year ?? "").Length >= 4 ? c.Year.Substring(0, 4) : c.Year ?? "",
            ["tracknumber"] = c.TrackNumber.ToString("00"),
            ["discnumber"] = c.DiscNumber.ToString(),
            ["totaldiscs"] = c.TotalDiscs.ToString(),
            ["discsubtitle"] = Normalize(c.DiscSubtitle ?? "", s, 80, swapArticles: false),
            ["disc"] = DiscFolder(c, s),
            ["releasedescriptor"] = s.ReleaseDescriptor ? ReleaseDescriptorText(c) : "",
            ["featsuffix"] = s.ExtractFeatured ? FeatSuffix(c, s) : "",
        };
    }

    private static string Substitute(string template, Dictionary<string, string> vars)
    {
        return Regex.Replace(template, "%([a-zA-Z0-9]+)%", m =>
            vars.TryGetValue(m.Groups[1].Value, out var val) ? val : m.Value);
    }

    // "Disc N/" (optionally " - Subtitle") only for multi-disc sets, else empty
    private static string DiscFolder(NamingContext c, NamingScheme s)
    {
        if (c.TotalDiscs <= 1) return "";
        string sub = Normalize(c.DiscSubtitle ?? "", s, 80, swapArticles: false);
        return sub.Length > 0 ? $"Disc {c.DiscNumber} - {sub}/" : $"Disc {c.DiscNumber}/";
    }

    // ---- the distilled Picard rules ----

    private static string Normalize(string value, NamingScheme s, int maxLen, bool swapArticles)
    {
        string v = value ?? "";
        v = StripFeaturing(v);                     // album/track names never carry the feat credit
        if (s.UnifySeparators) v = UnifySeparators(v);
        if (s.HandleArticles && swapArticles) v = SwapArticle(v);
        v = v.Replace("\"", "");
        if (s.StripIllegal) v = StripIllegalChars(v);
        v = Regex.Replace(v, @"\s{2,}", " ").Trim();
        if (v.Length > maxLen) v = v.Substring(0, maxLen).Trim();
        v = Regex.Replace(v, @"[.\s]+$", "");      // no trailing dots/spaces (Windows folder rule)
        return v;
    }

    // one segment of the final path: never let cleansing produce an empty or dotted name
    private static string TidySegment(string seg, NamingScheme s)
    {
        string v = seg;
        if (s.StripIllegal) v = StripIllegalChars(v);
        v = Regex.Replace(v, @"\s{2,}", " ").Trim();
        v = Regex.Replace(v, @"[.\s]+$", "");
        return v;
    }

    private static readonly string[] FeatKeywords =
        { " featuring ", " feat. ", " feat ", " ft. ", " ft ", " pheaturing " };

    private static string StripFeaturing(string v)
    {
        foreach (var kw in FeatKeywords)
        {
            int i = v.IndexOf(kw, StringComparison.OrdinalIgnoreCase);
            if (i >= 0) v = v.Substring(0, i);
        }
        return v;
    }

    // the guest-artist extraction: when the track artist carries featured performers the album
    // artist does not, produce " (feat. X & Y)". Also handles a leading-article difference.
    private static string FeatSuffix(NamingContext c, NamingScheme s)
    {
        string artist = c.Artist ?? "";
        foreach (var kw in FeatKeywords)
        {
            int i = artist.IndexOf(kw, StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
            {
                string guests = artist.Substring(i + kw.Length).Trim();
                if (s.UnifySeparators) guests = UnifySeparators(guests);
                guests = StripIllegalChars(guests.Replace("\"", "")).Trim();
                guests = Regex.Replace(guests, @"\s{2,}", " ");
                if (guests.Length > 50) guests = guests.Substring(0, 50).Trim();
                return guests.Length > 0 ? $" (feat. {guests})" : "";
            }
        }
        return "";
    }

    // "The Beatles" -> "Beatles, The" (Picard $swapprefix - sort-friendly folder names)
    private static readonly string[] Articles =
        { "The", "A", "An", "Die", "Der", "Das", "Le", "La", "Les", "El", "Los", "Las", "Il", "Gli" };

    private static string SwapArticle(string v)
    {
        foreach (var a in Articles)
        {
            string prefix = a + " ";
            if (v.StartsWith(prefix, StringComparison.Ordinal) && v.Length > prefix.Length)
                return v.Substring(prefix.Length) + ", " + a;
        }
        return v;
    }

    // unify the many "collaboration" separators the owner's script maps to " & "
    private static string UnifySeparators(string v)
    {
        string[] seps = { " meets ", " X ", " x ", " vs. ", " vs ", " with ", " and ", " + ",
                          " × ", "; ", " | ", " • ", " · " };
        foreach (var sep in seps)
            v = v.Replace(sep, " & ");
        v = Regex.Replace(v, @"(?:& )+", "& ");    // collapse runs of "& "
        return v;
    }

    private static string StripIllegalChars(string v)
    {
        v = v.Replace(":", " - ");
        v = Regex.Replace(v, @"[""*?<>|]+", "");
        return v;
    }

    // the bracketed suffix: " (year) [Live] [OST] [EP] [N-CD Set] [Promo] ..." - CD-only subset
    private static string ReleaseDescriptorText(NamingContext c)
    {
        var sb = new StringBuilder();
        if (c.TotalDiscs > 1) sb.Append($" [{c.TotalDiscs}-CD Set]");

        string status = (c.ReleaseStatus ?? "").ToLowerInvariant();
        if (status.StartsWith("promo")) sb.Append(" [Promo]");
        else if (status == "bootleg") sb.Append(" [Bootleg]");
        else if (status == "pseudo-release") sb.Append(" [Pseudo]");

        if ((c.Year ?? "").Length >= 4) sb.Append($" ({c.Year.Substring(0, 4)})");

        string primary = (c.PrimaryType ?? "album").ToLowerInvariant();
        if (primary == "single") sb.Append(" [Single]");
        else if (primary == "ep") sb.Append(" [EP]");
        else if (primary == "broadcast") sb.Append(" [FM]");
        else if (primary != "album" && primary.Length > 0) sb.Append(" [Other]");

        var sec = new HashSet<string>((c.SecondaryTypes ?? Array.Empty<string>())
            .Select(x => (x ?? "").ToLowerInvariant()));
        if (sec.Contains("soundtrack")) sb.Append(" [OST]");
        if (sec.Contains("live")) sb.Append(" [Live]");
        if (sec.Contains("dj-mix")) sb.Append(" [DJ Mix]");
        if (sec.Contains("remix")) sb.Append(" [Remix]");
        if (sec.Contains("mixtape/street")) sb.Append(" [Mixtape]");
        if (sec.Contains("demo")) sb.Append(" [Demo]");
        if (sec.Contains("compilation")) sb.Append(" [Compilation]");
        return sb.ToString();
    }

    private static string FirstNonEmpty(params string[] xs)
        => xs.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";

    // ---- canned example albums for the live preview (no disc needed) ----
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
