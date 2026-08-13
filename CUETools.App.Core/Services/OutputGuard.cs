using System;
using System.IO;

namespace CUETools.Wpf.Services;

/// <summary>
/// Stops one rip or convert from silently destroying another's output.
///
/// The legacy WinForms app gated every write behind CUESheet.OutputExists() and a Yes/No prompt. This
/// rebuild dropped that gate and never replaced it, while every encoder opens its file with
/// FileMode.Create - which TRUNCATES. So a second run that rendered the same album folder overwrote the
/// first one's audio, cue sheet, rip log, cover and rip.verify with no prompt and no error.
///
/// That is not only a deliberate re-rip. A CD-Text or freedb release carries no disc number, so both
/// discs of a multi-disc set render ONE identical folder with no "Disc N" level - rip disc 1, rip disc 2,
/// and disc 1 is gone. The engine's own %unique% collision loop cannot save the sidecars either, because
/// ArLogFilenameFormat is "%filename%.accurip" and AlArtFilenameFormat is "folder.jpg", neither of which
/// contains %unique%.
///
/// The rule here is conservative on purpose: never delete, never merge into an occupied folder, just
/// pick the next free name. Losing a rip is unrecoverable without the disc; an extra folder is not.
/// </summary>
public static class OutputGuard
{
    /// <summary>The machine-stable artifacts a finished rip leaves behind. Human-facing cue and log
    /// sidecars carry the album identity and are detected by extension below.</summary>
    private static readonly string[] ExactArtifacts =
        { "folder.jpg", "rip.verify", RepairEvidence.ReceiptFileName,
            AlbumOutputTransaction.CompletionMarkerName };

    private static readonly string[] SidecarExtensions =
        { ".cue", ".log", ".accurip" };

    /// <summary><paramref name="albumRel"/> when nothing would be overwritten under
    /// <paramref name="baseDir"/>, else the same name with " (2)", " (3)" ... appended until free.
    /// <paramref name="format"/> is the audio extension about to be written (may be empty).</summary>
    public static string NonClobberingAlbumDir(string baseDir, string albumRel, string format,
        Action<string>? onNote = null)
    {
        if (string.IsNullOrWhiteSpace(baseDir))
            throw new ArgumentException("An output base directory is required.", nameof(baseDir));
        if (string.IsNullOrWhiteSpace(albumRel))
            throw new ArgumentException("An album directory is required.", nameof(albumRel));
        try
        {
            string baseFull = Path.GetFullPath(baseDir);
            for (int n = 1; n <= 999; n++)
            {
                string candidate = n == 1 ? albumRel : albumRel + " (" + n + ")";
                string full = ResolveContainedPath(baseFull, candidate);
                if (!TryGetAttributes(full, out FileAttributes attributes) ||
                    ((attributes & FileAttributes.Directory) != 0 &&
                     !HoldsARip(full, format)))
                {
                    if (!string.Equals(candidate, albumRel, StringComparison.Ordinal))
                        onNote?.Invoke("output folder already held a rip - writing to \"" + candidate + "\" instead");
                    return candidate;
                }
            }

            // Keep a random suffix after pathological sequential occupancy, but still probe it.
            // Returning an unprobed timestamp can collide; returning the original name on a probe
            // exception can clobber. Failure to prove a free path must stop this obsolete path.
            for (int attempt = 0; attempt < 32; attempt++)
            {
                string candidate = albumRel + " (" + Guid.NewGuid().ToString("N") + ")";
                string full = ResolveContainedPath(baseFull, candidate);
                if (!TryGetAttributes(full, out _))
                {
                    onNote?.Invoke("output folder is heavily occupied - writing to \"" +
                        candidate + "\" instead");
                    return candidate;
                }
            }
            throw new IOException("Could not prove a unique output directory.");
        }
        catch (Exception ex)
        {
            throw new IOException("Could not safely probe the output directory.", ex);
        }
    }

    /// <summary>True when a directory already holds rip output: any fixed-name artifact, or any audio
    /// file of the format about to be written.</summary>
    public static bool HoldsARip(string dir, string format)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(dir, "*",
            SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(entry);
            foreach (string artifact in ExactArtifacts)
                if (string.Equals(name, artifact, StringComparison.OrdinalIgnoreCase))
                    return true;
            foreach (string extension in SidecarExtensions)
                if (string.Equals(Path.GetExtension(name), extension,
                    StringComparison.OrdinalIgnoreCase))
                    return true;
            if (!string.IsNullOrWhiteSpace(format) &&
                string.Equals(Path.GetExtension(name), "." + format,
                    StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string ResolveContainedPath(string baseFull, string relative)
    {
        string full = Path.GetFullPath(Path.Combine(baseFull, relative));
        string prefix = baseFull.TrimEnd(Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The album path escapes the selected output directory.");
        return full;
    }

    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            attributes = default;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }
}
