using System;
using System.Security.Cryptography;
using System.Text;

namespace CUETools.Wpf.Services;

/// <summary>
/// Model B, layer 1 of the rip-log tamper-evidence scheme (see the rip-accuracy +
/// log-integrity design spec): a local self-checksum over a rip log's canonical text.
/// Canonical form is UTF-8 with LF line endings and no trailing blank lines, with the
/// integrity footer line itself excluded from the hash. Pure and offline.
///
/// This is the algorithm the engine's CUESheetLogWriter will eventually own; it lives here
/// so the Report page can seal and verify a log without waiting on that engine work. Keep it
/// pure (no I/O, no UI) so it stays unit-testable.
/// </summary>
public static class LogIntegrity
{
    public const string FooterPrefix = "==== Log integrity: SHA-256 ";
    public const string FooterSuffix = " ====";

    /// <summary>SHA-256 (lowercase hex) over the canonical form of <paramref name="body"/>.
    /// The body must NOT contain the footer line - that is what makes verification a fixed
    /// point (recomputing over the body-without-footer reproduces the embedded value).</summary>
    public static string ComputeDigest(string body)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Canonicalize(body)));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>The footer line embedded at the end of a sealed log.</summary>
    public static string Footer(string digest) => FooterPrefix + digest + FooterSuffix;

    /// <summary>Body plus its integrity footer, ready to write to a .log file (LF endings).</summary>
    public static string Seal(string body) => Canonicalize(body) + "\n" + Footer(ComputeDigest(body)) + "\n";

    /// <summary>Recompute over a sealed log and compare to its embedded footer. True only when a
    /// footer is present and the recomputed hash matches it (i.e. the body is unaltered).</summary>
    public static bool Verify(string sealedLog, out string embedded, out string recomputed)
    {
        embedded = "";
        recomputed = "";
        string[] lines = Canonicalize(sealedLog).Split('\n');
        int footerIdx = -1;
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            string t = lines[i].Trim();
            if (t.StartsWith(FooterPrefix, StringComparison.Ordinal) && t.EndsWith(FooterSuffix, StringComparison.Ordinal))
            {
                footerIdx = i;
                embedded = t.Substring(FooterPrefix.Length, t.Length - FooterPrefix.Length - FooterSuffix.Length).Trim();
                break;
            }
        }
        if (footerIdx < 0) return false;

        string body = string.Join("\n", lines, 0, footerIdx);
        recomputed = ComputeDigest(body);
        return string.Equals(embedded, recomputed, StringComparison.OrdinalIgnoreCase);
    }

    private static string Canonicalize(string s)
        => (s ?? "").Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd('\n');
}
