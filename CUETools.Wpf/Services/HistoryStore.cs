using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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

    public HistoryStore()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CUETools2026");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "history.json");
        _rows = Read(_path);
    }

    public void Add(RipReport r)
    {
        _rows.Insert(0, new Row
        {
            When = r.Timestamp,
            Album = r.Album,
            Artist = r.Artist,
            Mode = r.Mode,
            Ar = r.ArConfidence,
            Ctdb = r.CtdbConfidence,
            Tracks = r.TrackCount,
            Confirmed = r.Confirmed
        });
        if (_rows.Count > Cap) _rows.RemoveRange(Cap, _rows.Count - Cap);
        Write();
    }

    public IReadOnlyList<RecentRip> Recent(int max) => _rows
        .Take(max)
        .Select(r => new RecentRip
        {
            Title = string.IsNullOrWhiteSpace(r.Album) ? "Unknown album" : r.Album,
            Artist = r.Artist,
            When = Relative(r.When),
            Result = r.Confirmed
                ? $"{r.Mode.ToLowerInvariant()} - verified (AR {r.Ar}{(r.Ctdb > 0 ? ", CTDB " + r.Ctdb : "")})"
                : $"{r.Mode.ToLowerInvariant()} - {(r.Tracks > 0 ? r.Tracks + " tracks, " : "")}not confirmed"
        })
        .ToList();

    private void Write()
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(_rows)); } catch { }
    }

    private static List<Row> Read(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<List<Row>>(File.ReadAllText(path)) ?? new List<Row>();
        }
        catch { }
        return new List<Row>();
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
    }
}
