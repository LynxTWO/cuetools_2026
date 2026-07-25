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
    /// <summary>The fixed-name artifacts a finished rip leaves behind. Any one of them means the folder
    /// already holds a rip, even if the audio format differs.</summary>
    private static readonly string[] Artifacts =
        { "album.cue", "album.log", "album.accurip", "folder.jpg", "rip.verify", "Test & Copy.log" };

    /// <summary><paramref name="albumRel"/> when nothing would be overwritten under
    /// <paramref name="baseDir"/>, else the same name with " (2)", " (3)" ... appended until free.
    /// <paramref name="format"/> is the audio extension about to be written (may be empty).</summary>
    public static string NonClobberingAlbumDir(string baseDir, string albumRel, string format,
        Action<string>? onNote = null)
    {
        if (string.IsNullOrWhiteSpace(albumRel)) return albumRel;
        try
        {
            string candidate = albumRel;
            for (int n = 2; n <= 99; n++)
            {
                string full = Path.Combine(baseDir, candidate);
                if (!Directory.Exists(full) || !HoldsARip(full, format))
                {
                    if (!string.Equals(candidate, albumRel, StringComparison.Ordinal))
                        onNote?.Invoke("output folder already held a rip - writing to \"" + candidate + "\" instead");
                    return candidate;
                }
                candidate = albumRel + " (" + n + ")";
            }
            // 98 collisions is pathological; fall back to something guaranteed unique rather than clobber
            return albumRel + " (" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ")";
        }
        catch
        {
            // A probe failure must not stop a rip; the worst case is the pre-existing behaviour.
            return albumRel;
        }
    }

    /// <summary>True when a directory already holds rip output: any fixed-name artifact, or any audio
    /// file of the format about to be written.</summary>
    public static bool HoldsARip(string dir, string format)
    {
        foreach (var name in Artifacts)
            if (File.Exists(Path.Combine(dir, name))) return true;
        if (!string.IsNullOrWhiteSpace(format))
        {
            try { if (Directory.GetFiles(dir, "*." + format).Length > 0) return true; } catch { }
        }
        return false;
    }
}
