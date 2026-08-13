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

    private sealed class RenderedLayout
    {
        public string RelativeDir { get; init; } = "";
        public string[] TrackRemainders { get; init; } = Array.Empty<string>();
    }

    /// <summary>Compute and create the layout for a job, and hand the sheet its per-track names.
    /// <paramref name="albumFallback"/> supplies an album folder when the naming scheme produces no
    /// shared one; it must never return empty.</summary>
    public static Plan PrepareAndApply(CUESheet cue, string baseDir, string format,
        NamingScheme scheme, Func<string> albumFallback, Action<string>? onNote = null)
    {
        RenderedLayout rendered = Render(cue, scheme, albumFallback);
        // Refuse to write over an earlier rip. Must happen before anything is created.
        string relDir = OutputGuard.NonClobberingAlbumDir(baseDir, rendered.RelativeDir, format, onNote);

        string outDir = Path.Combine(baseDir, relDir);
        Directory.CreateDirectory(outDir);
        string[] finalNames = ApplyTrackNames(cue, rendered.TrackRemainders, outDir, outDir.Length);
        return new Plan { OutputDir = outDir, RelativeDir = relDir, TrackNames = finalNames };
    }

    /// <summary>
    /// Prepare a standard rip in an owned same-volume staging directory while atomically reserving
    /// its final album name. The returned <paramref name="publication"/> must be published or
    /// disposed by the caller.
    /// </summary>
    public static Plan PrepareAndApplyTransactional(CUESheet cue, string baseDir,
        NamingScheme scheme, Func<string> albumFallback, out AlbumOutputTransaction publication,
        Action<string>? onNote = null)
    {
        RenderedLayout rendered = Render(cue, scheme, albumFallback);
        publication = AlbumOutputTransaction.Reserve(baseDir, rendered.RelativeDir, onNote);
        try
        {
            string[] finalNames = ApplyTrackNames(cue, rendered.TrackRemainders,
                publication.StagingDirectory,
                Math.Max(publication.StagingDirectory.Length,
                    publication.DestinationDirectory.Length));
            return new Plan
            {
                OutputDir = publication.StagingDirectory,
                RelativeDir = publication.RelativeDirectory,
                TrackNames = finalNames,
            };
        }
        catch
        {
            publication.Dispose();
            throw;
        }
    }

    private static RenderedLayout Render(CUESheet cue, NamingScheme scheme,
        Func<string> albumFallback)
    {
        int trackCount = Math.Max(0, cue.TrackCount);
        if (trackCount == 0)
        {
            string onlyDir = albumFallback();
            if (string.IsNullOrWhiteSpace(onlyDir)) onlyDir = "Unknown Album";
            return new RenderedLayout { RelativeDir = onlyDir };
        }

        var rel = new string[trackCount];
        for (int t = 0; t < trackCount; t++)
            rel[t] = NamingEngine.Render(
                NamingContextMapper.FromMetadata(cue.Metadata, t, trackCount), scheme);

        var split = NamingPaths.Split(rel);

        // There must ALWAYS be an album folder. A template with no folder part, or one whose leading
        // segment differs per track (the "Simple" preset on a various-artists disc), yields no shared
        // leading directory - and then album.cue, the log, the cover and rip.verify would be written
        // into the output base and overwritten by the next such job.
        string relDir = string.IsNullOrWhiteSpace(split.commonDir) ? albumFallback() : split.commonDir;
        if (string.IsNullOrWhiteSpace(relDir)) relDir = "Unknown Album";
        return new RenderedLayout { RelativeDir = relDir, TrackRemainders = split.remainders };
    }

    private static string[] ApplyTrackNames(CUESheet cue, string[] remainders, string workingDir,
        int destinationDirLength)
    {
        if (remainders.Length == 0) return Array.Empty<string>();
        // Cap first, THEN uniquify: truncation can create a collision, and only this order lets the
        // uniquifier separate the result.
        var capped = NamingPaths.CapPathLength(remainders, destinationDirLength);
        var finalNames = NamingPaths.EnsureUniqueTrackNames(capped);

        // A track name may carry its own subdirectory ("Disc 2/..."), which must exist before the
        // engine writes into it.
        string root = Path.GetFullPath(workingDir);
        RequirePlainDirectory(root);
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var name in finalNames)
        {
            string full = Path.GetFullPath(Path.Combine(root, name));
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new IOException(
                    "A rendered track path escaped the album output directory.");
            string? sub = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(sub))
            {
                Directory.CreateDirectory(sub);
                RequirePlainDirectoryAncestry(root, sub);
            }
        }

        cue.SetExplicitTrackNames(finalNames);
        return finalNames;
    }

    private static void RequirePlainDirectoryAncestry(string root, string target)
    {
        string relative = Path.GetRelativePath(root, target);
        if (relative == ".") return;
        string current = root;
        foreach (string part in relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            RequirePlainDirectory(current);
        }
    }

    private static void RequirePlainDirectory(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0 ||
            (attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException(
                "An album output directory is not a regular directory.");
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
