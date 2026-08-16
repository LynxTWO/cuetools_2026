using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CUETools.Wpf.Services;

/// <summary>
/// A privacy-safe diagnostic log for bug reports. It records the STRUCTURE of what the app does -
/// phases, counts, confidences, timings, and full exception detail - but never the user's music:
/// no album/artist/track titles, and any path or exception text is scrubbed of the user name,
/// home folder, and registered sensitive strings (the current album folder/metadata) before it is
/// written. One file per run under %AppData%\CUETools2026\logs. Thread-safe; failures to log are
/// swallowed so logging never affects a rip.
/// </summary>
public interface IDiagnosticLog
{
    void Info(string category, string message);
    void Warn(string category, string message);
    void Error(string category, string message, Exception? ex = null);

    /// <summary>Register a string (e.g. an album/artist name or output folder) that must be
    /// scrubbed out of any future log line. Call at the start of a job.</summary>
    void Redact(params string?[] sensitive);

    string LogPath { get; }
}

public sealed class DiagnosticLog : IDiagnosticLog
{
    private readonly object _gate = new();
    private readonly List<string> _secrets = new();
    public string LogPath { get; }

    public DiagnosticLog() : this(null)
    {
    }

    /// <summary>
    /// The explicit path is for isolated tests. Production always uses the per-user diagnostics
    /// directory selected by the parameterless constructor.
    /// </summary>
    internal DiagnosticLog(string? logPath)
    {
        string defaultDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CUETools2026",
            "logs");
        string selectedPath = string.IsNullOrWhiteSpace(logPath)
            // Multiple drive workers can start in the same second. Include both the process
            // identity and a nonce so every job retains its own complete diagnostic evidence.
            ? Path.Combine(
                defaultDir,
                $"cuetools-{DateTime.Now:yyyyMMdd-HHmmss-fff}-p{Environment.ProcessId}-" +
                $"{Guid.NewGuid():N}.log")
            : logPath;
        string? dir = Path.GetDirectoryName(selectedPath);
        try
        {
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
        }
        catch { }
        LogPath = selectedPath;
        if (string.IsNullOrWhiteSpace(logPath) && !string.IsNullOrWhiteSpace(dir))
            PruneOldLogs(dir!);

        // always-on scrub targets: user name + home + music folders
        AddSecret(Environment.UserName);
        AddSecret(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        AddSecret(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));

        WriteRaw($"CUETools 2026 diagnostic log. Started {DateTime.Now:yyyy-MM-dd HH:mm:ss}. " +
                 "Structural only - no album/artist/track names, paths and errors scrubbed.");
    }

    public void Info(string category, string message) => Write("INFO", category, message);
    public void Warn(string category, string message) => Write("WARN", category, message);

    public void Error(string category, string message, Exception? ex = null)
    {
        var sb = new StringBuilder(message);
        for (var e = ex; e != null; e = e.InnerException)
            sb.Append("\n    ").Append(e.GetType().Name).Append(": ").Append(e.Message)
              .Append("\n").Append(e.StackTrace);
        Write("ERROR", category, sb.ToString());
    }

    /// <summary>Keep at least this many launches, however old they are.</summary>
    public const int RetainedLogCount = 200;

    /// <summary>Keep everything from at least this long ago, however many launches that is.</summary>
    public const int RetainedLogDays = 90;

    /// <summary>
    /// Set to keep every log forever. Off by default; the archival choice belongs to the
    /// person whose disk it is, so nothing here deletes a log they asked to keep.
    /// </summary>
    public static bool KeepLogsForever { get; set; }

    /// <summary>
    /// Delete logs that are both older than the day limit and outside the newest N. Whichever
    /// rule keeps more, wins, so a heavy week is not truncated by the day limit and a quiet
    /// year is not truncated by the count.
    ///
    /// Pruning is per file rather than per line on purpose: one file is one launch, and half a
    /// launch's log reads like a complete record of a session that did less than it did.
    /// Never throws: losing a log must not stop the app starting.
    /// </summary>
    private static void PruneOldLogs(string directory)
    {
        if (KeepLogsForever) return;
        try
        {
            var logs = new DirectoryInfo(directory).GetFiles("cuetools-*.log");
            if (logs.Length <= RetainedLogCount) return;
            DateTime cutoff = DateTime.UtcNow.AddDays(-RetainedLogDays);
            Array.Sort(logs, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            for (int i = RetainedLogCount; i < logs.Length; i++)
            {
                if (logs[i].LastWriteTimeUtc >= cutoff) continue;
                try { logs[i].Delete(); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Redact(params string?[] sensitive)
    {
        lock (_gate) foreach (var s in sensitive) AddSecret(s);
    }

    private void AddSecret(string? s)
    {
        if (!string.IsNullOrWhiteSpace(s) && s.Length >= 3 &&
            !_secrets.Exists(existing =>
                string.Equals(existing, s, StringComparison.OrdinalIgnoreCase)))
            _secrets.Add(s);
    }

    private void Write(string level, string category, string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} {level,-5} {category,-14} {Scrub(message)}";
        WriteRaw(line);
    }

    private void WriteRaw(string line)
    {
        try { lock (_gate) File.AppendAllText(LogPath, line + Environment.NewLine); }
        catch { /* never let logging break anything */ }
    }

    // Replace registered sensitive substrings with a token, case-insensitively, in one pass over the
    // ORIGINAL text. Replacement text must never be searched: "Red" is a real artist/title and also
    // occurs inside "<redacted>".
    private string Scrub(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        const string Token = "<redacted>";
        List<string> secrets;
        lock (_gate) secrets = new List<string>(_secrets);
        // A custom root normally contains the profile path and user name. Scrub the most specific
        // value first; otherwise replacing the short prefix would prevent the full root from
        // matching and leave the identifying remainder in the log.
        secrets.Sort((left, right) => right.Length.CompareTo(left.Length));
        var scrubbed = new StringBuilder(s.Length);
        int position = 0;
        while (position < s.Length)
        {
            int next = -1;
            string? match = null;
            foreach (string secret in secrets)
            {
                int candidate = s.IndexOf(
                    secret,
                    position,
                    StringComparison.OrdinalIgnoreCase);
                if (candidate >= 0 &&
                    (next < 0 || candidate < next ||
                     (candidate == next && secret.Length > match!.Length)))
                {
                    next = candidate;
                    match = secret;
                }
            }

            if (next < 0)
            {
                scrubbed.Append(s, position, s.Length - position);
                break;
            }

            scrubbed.Append(s, position, next - position);
            scrubbed.Append(Token);
            position = next + match!.Length;
        }
        return scrubbed.ToString();
    }
}
