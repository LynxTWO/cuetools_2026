using System;
using System.IO;
using CUETools.Processor;

namespace CUETools.Wpf.Services;

/// <summary>
/// The one place that decides WHERE a job's files go and WHAT each track is called.
///
/// This exists because the sequence it performs - render every track's relative path, split off the
/// shared album folder, guarantee that folder exists and is album-specific, refuse to overwrite an
/// earlier rip, cap the assembled path length, guarantee non-empty and unique track names, create the
/// whole directory tree, and hand the engine explicit names - was duplicated in the rip service and the
/// convert service. Every past divergence between those two copies became a real defect: the album
/// folder collapsing to the output root, a Test and Copy committing under its temp staging name, and
/// two untagged sources overwriting each other were all "fixed in one copy, not the other".
///
/// Correctness note, deliberately not optimised away: each step here is a guard, not decoration. The
/// order matters - cap BEFORE uniquify, so a collision that truncation creates can still be
/// disambiguated - and the overwrite check must run before any directory is created. This runs once per
/// job, not per sample, so clarity wins over micro-optimisation; see docs/review/code-audit-prompt.md.
/// </summary>
public static class OutputLayout
{
    /// <summary>Where a job's output goes.</summary>
    public readonly struct Plan
    {
        /// <summary>The absolute album directory, already created.</summary>
        public string OutputDir { get; init; }
        /// <summary>That directory relative to the base - what a later commit must re-home with.</summary>
        public string RelativeDir { get; init; }
        /// <summary>Per-track names relative to <see cref="OutputDir"/>, extension-less, already
        /// capped and de-duplicated. Empty when the sheet had no tracks.</summary>
        public string[] TrackNames { get; init; }
    }

    /// <summary>Compute and create the layout for a job, and hand the sheet its per-track names.
    /// <paramref name="albumFallback"/> supplies an album folder when the naming scheme produces no
    /// shared one; it must never return empty.</summary>
    public static Plan PrepareAndApply(CUESheet cue, string baseDir, string format,
        NamingScheme scheme, Func<string> albumFallback, Action<string>? onNote = null)
    {
        int trackCount = Math.Max(0, cue.TrackCount);
        if (trackCount == 0)
        {
            // Nothing to name, but the job still needs its own folder for the cue, log and cover.
            string onlyDir = OutputGuard.NonClobberingAlbumDir(baseDir, albumFallback(), format, onNote);
            string onlyFull = Path.Combine(baseDir, onlyDir);
            Directory.CreateDirectory(onlyFull);
            return new Plan { OutputDir = onlyFull, RelativeDir = onlyDir, TrackNames = Array.Empty<string>() };
        }

        var rel = new string[trackCount];
        for (int t = 0; t < trackCount; t++)
            rel[t] = NamingEngine.Render(NamingContextMapper.FromMetadata(cue.Metadata, t, trackCount), scheme);

        var split = NamingPaths.Split(rel);

        // There must ALWAYS be an album folder. A template with no folder part, or one whose leading
        // segment differs per track (the "Simple" preset on a various-artists disc), yields no shared
        // leading directory - and then album.cue, the log, the cover and rip.verify would be written
        // into the output base and overwritten by the next such job.
        string relDir = string.IsNullOrWhiteSpace(split.commonDir) ? albumFallback() : split.commonDir;
        if (string.IsNullOrWhiteSpace(relDir)) relDir = "Unknown Album";

        // Refuse to write over an earlier rip. Must happen before anything is created.
        relDir = OutputGuard.NonClobberingAlbumDir(baseDir, relDir, format, onNote);

        string outDir = Path.Combine(baseDir, relDir);
        Directory.CreateDirectory(outDir);

        // Cap first, THEN uniquify: truncation can create a collision, and only this order lets the
        // uniquifier separate the result.
        var capped = NamingPaths.CapPathLength(split.remainders, outDir.Length);
        var finalNames = NamingPaths.EnsureUniqueTrackNames(capped);

        // A track name may carry its own subdirectory ("Disc 2/..."), which must exist before the
        // engine writes into it.
        foreach (var name in finalNames)
        {
            string sub = Path.GetDirectoryName(Path.Combine(outDir, name));
            if (!string.IsNullOrEmpty(sub)) Directory.CreateDirectory(sub);
        }

        cue.SetExplicitTrackNames(finalNames);
        return new Plan { OutputDir = outDir, RelativeDir = relDir, TrackNames = finalNames };
    }

    /// <summary>How many audio files a job actually wrote under its album folder.
    ///
    /// Recursive on purpose. A track name may carry its own subdirectory - "Disc 2/..." for a box set,
    /// or a whole per-track chain when the template has no shared leading folder (the "Simple" preset on
    /// a various-artists disc). A non-recursive count reported "Ripped 0 flac files" for those rips even
    /// though every file was written and verified, and wrote that 0 into the archived report as well.</summary>
    public static int CountAudioFiles(string dir, string format)
    {
        try
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
            return Directory.GetFiles(dir, "*." + format, SearchOption.AllDirectories).Length;
        }
        catch { return 0; }   // a count is cosmetic; never fail a completed rip over it
    }

    /// <summary>"Artist - Album", or "Unknown Album" when the metadata carries neither. Shared so the
    /// rip and convert paths cannot drift on what an unnamed album is called.</summary>
    public static string AlbumFolderFallback(CUEMetadata? meta, Func<string, string> cleanse)
    {
        string artist = cleanse(meta?.Artist ?? ""), title = cleanse(meta?.Title ?? "");
        if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(title)) return "Unknown Album";
        return $"{artist} - {title}".Trim(' ', '-');
    }
}
