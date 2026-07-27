using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CUETools.Wpf.Models;

namespace CUETools.Wpf.Services;

/// <summary>Persistent "recently ripped" history for the Rip page's no-disc screen - the last
/// discs handled in THIS app, so a user working through a stack of CDs can tell them apart.
/// Real jobs only (fed from each finished RipReport); no seeded/fake rows. Stored as JSON under
/// the user's app-data so it survives restarts.</summary>
public interface IHistoryStore
{
    void Add(RipReport report);
    IReadOnlyList<RecentRip> Recent(int max);
}

public sealed class HistoryStore : IHistoryStore
{
    private const int Cap = 50;
    private readonly string _path;
    private readonly List<Row> _rows;
    private readonly object _rowsGate = new object();
    private readonly IDiagnosticLog _log;
    private bool _loadFailed;

    public HistoryStore(IDiagnosticLog log)
        : this(log, null)
    {
    }

    /// <summary>Explicit path for isolated tests. Production uses the one-argument constructor.</summary>
    internal HistoryStore(IDiagnosticLog log, string? path)
    {
        _log = log;
        string defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CUETools2026");
        string selectedPath = string.IsNullOrWhiteSpace(path) ? Path.Combine(defaultDir, "history.json") : path;
        string? dir = Path.GetDirectoryName(Path.GetFullPath(selectedPath));
        // guarded: this runs during DI resolution, before the window-show retry loop can protect us
        try { if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir); } catch { }
        _path = selectedPath;
        _rows = Read(_path);
    }

    public void Add(RipReport r)
    {
        lock (_rowsGate)
        {
            // The UI reads this list immediately after Add. Publish to memory only after the same
            // snapshot is visible on disk; otherwise a failed write looks successful until restart.
            // Hold the instance gate through publication so two callers cannot expose snapshots in
            // the reverse order from their serialized disk commits.
            List<Row>? next = TryAppend(r);
            if (next != null)
            {
                _rows.Clear();
                _rows.AddRange(next);
            }
        }
    }

    public IReadOnlyList<RecentRip> Recent(int max)
    {
        lock (_rowsGate)
        {
            return _rows
                .Take(max)
                .Select(r => new RecentRip
                {
                    Title = string.IsNullOrWhiteSpace(r.Album)
                        ? "Unknown album"
                        : r.Album,
                    Artist = r.Artist,
                    When = Relative(r.When),
                    Result = ResultText(r)
                })
                .ToList();
        }
    }

    private static string ResultText(Row r)
    {
        string mode = r.Mode.ToLowerInvariant();
        string databases = r.Ar > 0 && r.Ctdb > 0 ? $"AR {r.Ar}, CTDB {r.Ctdb}"
            : r.Ar > 0 ? $"AR {r.Ar}"
            : r.Ctdb > 0 ? $"CTDB {r.Ctdb}"
            : "";
        string output = !r.OutputVerificationKnown
            ? ""
            : r.LosslessOutput
                ? r.OutputVerificationPerformed
                    ? "; final output PCM verified after metadata"
                    : "; WARNING: encoded output not verified"
                : "; lossy output (output verification not applicable)";
        if (r.OpticalReadsUsed >= 2 && r.MinimumAgreeingReads >= 2)
            return $"{mode} - verified after {r.OpticalReadsUsed} optical reads; "
                + $"at least {r.MinimumAgreeingReads} agreed per track"
                + (databases.Length > 0 ? $" ({databases})" : " (not found in AR/CTDB)")
                + output;
        if (r.Confirmed)
            return $"{mode} - verified ({databases})" + output;
        return $"{mode} - {(r.Tracks > 0 ? r.Tracks + " tracks, " : "")}not confirmed"
            + output;
    }

    private List<Row>? TryAppend(RipReport r)
    {
        if (_loadFailed)
        {
            _log.Warn("history",
                "history write blocked because the existing store could not be read");
            return null;
        }
        try
        {
            return GzJson.Update<List<Row>, List<Row>>(
                _path,
                loaded =>
                {
                    if (loaded.Status == GzJsonLoadStatus.Failed)
                        throw new InvalidDataException(
                            "The recent-history store exists but could not be read.",
                            loaded.Error);
                    var rows = loaded.Status == GzJsonLoadStatus.Loaded
                        ? loaded.Value!
                        : new List<Row>();
                    ValidateRows(rows);
                    rows.Insert(0, new Row
                    {
                        When = r.Timestamp,
                        Album = r.Album,
                        Artist = r.Artist,
                        Mode = r.Mode,
                        Ar = r.ArConfidence,
                        Ctdb = r.CtdbConfidence,
                        Tracks = r.TrackCount,
                        Confirmed = r.DatabaseConfirmed,
                        OpticalReadsUsed = r.OpticalReadsUsed,
                        MinimumAgreeingReads = r.MinimumAgreeingReads,
                        OutputVerificationKnown = r.OutputVerificationKnown,
                        LosslessOutput = r.LosslessOutput,
                        OutputVerificationPerformed = r.OutputVerificationPerformed
                    });
                    if (rows.Count > Cap)
                        rows.RemoveRange(Cap, rows.Count - Cap);
                    return (rows, rows);
                });
        }
        catch (Exception ex)
        {
            _log.Warn("history", "history write failed: " + ex.GetType().Name);
            return null;
        }
    }

    private List<Row> Read(string path)
    {
        GzJsonLoadResult<List<Row>> loaded = GzJson.TryLoad<List<Row>>(path);
        if (loaded.Status == GzJsonLoadStatus.Loaded)
        {
            try
            {
                ValidateRows(loaded.Value!);
                return loaded.Value!;
            }
            catch (InvalidDataException)
            {
                PreserveUnreadableStore(path);
                return new List<Row>();
            }
        }
        if (loaded.Status == GzJsonLoadStatus.Failed)
            PreserveUnreadableStore(path);
        return new List<Row>();
    }

    private void PreserveUnreadableStore(string path)
    {
        _loadFailed = true;
        _log.Warn("history", "history read failed (keeping the bad file as .bak)");
        try { File.Copy(path, path + ".bak", overwrite: true); } catch { }
    }

    private static void ValidateRows(List<Row> rows)
    {
        if (rows == null)
            throw new InvalidDataException(
                "The recent-history store contains a null root.");
        foreach (Row row in rows)
        {
            if (row == null ||
                row.Album == null ||
                row.Artist == null ||
                row.Mode == null)
            {
                throw new InvalidDataException(
                    "The recent-history store contains an invalid row.");
            }
        }
    }

    private static string Relative(DateTime when)
    {
        var span = DateTime.Now - when;
        if (span.TotalSeconds < 60) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} hr ago";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays} day(s) ago";
        return when.ToString("yyyy-MM-dd");
    }

    private sealed class Row
    {
        public DateTime When { get; set; }
        public string Album { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Mode { get; set; } = "";
        public int Ar { get; set; }
        public int Ctdb { get; set; }
        public int Tracks { get; set; }
        public bool Confirmed { get; set; }
        public int OpticalReadsUsed { get; set; }
        public int MinimumAgreeingReads { get; set; }
        public bool OutputVerificationKnown { get; set; }
        public bool LosslessOutput { get; set; }
        public bool OutputVerificationPerformed { get; set; }
    }
}
