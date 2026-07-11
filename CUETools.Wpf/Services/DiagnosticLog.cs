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

    public DiagnosticLog()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CUETools2026", "logs");
        try { Directory.CreateDirectory(dir); } catch { }
        LogPath = Path.Combine(dir, $"cuetools-{DateTime.Now:yyyyMMdd-HHmmss}.log");

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

    public void Redact(params string?[] sensitive)
    {
        lock (_gate) foreach (var s in sensitive) AddSecret(s);
    }

    private void AddSecret(string? s)
    {
        if (!string.IsNullOrWhiteSpace(s) && s.Length >= 3 && !_secrets.Contains(s))
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

    // Replace any registered sensitive substring with a token. Case-insensitive on the raw text.
    private string Scrub(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        List<string> secrets;
        lock (_gate) secrets = new List<string>(_secrets);
        foreach (var secret in secrets)
        {
            int idx;
            while ((idx = s.IndexOf(secret, StringComparison.OrdinalIgnoreCase)) >= 0)
                s = s.Substring(0, idx) + "<redacted>" + s.Substring(idx + secret.Length);
        }
        return s;
    }
}
