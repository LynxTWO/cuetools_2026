using System;
using System.Collections.Generic;
using CUETools.CDImage;
using CUETools.CTDB;
using CUETools.Processor;
using CUETools.Ripper;
using CUETools.Ripper.SCSI;
using CUETools.Wpf.Models;

namespace CUETools.Wpf.Services;

/// <summary>
/// Real drive access via the CUETools SCSI ripper (proven to run on .NET 8 in Phase 1) and
/// the CUESheet metadata lookup (CTDB / MusicBrainz / Discogs / freedb). Blocking SCSI +
/// network calls - callers marshal this onto a background thread.
/// </summary>
public sealed class DriveService : IDriveService
{
    private readonly CUEConfig _config;
    private readonly IDiagnosticLog _log;

    // A single physical drive cannot be opened by two callers at once - the startup Rip read and
    // the Drive & Read detect would otherwise race for the device. Serialise every device touch.
    private static readonly object _scsiGate = new object();

    /// <summary>The app-wide device gate. RipService takes it around its own drive open so a rip
    /// start cannot collide with an in-flight tray poll or capability query.</summary>
    internal static object ScsiGate => _scsiGate;

    public DriveService(CUEConfig config, IDiagnosticLog log) { _config = config; _log = log; }

    public IReadOnlyList<char> GetDrives()
    {
        try { return CDDrivesList.DrivesAvailable(); }
        catch (Exception ex) { _log.Error("drive", "drive enumeration failed", ex); return Array.Empty<char>(); }
    }

    public DiscInfo? ReadDisc(char drive, Action<string>? onStatus = null)
    {
      lock (_scsiGate)
      {
        var reader = new CDDriveReader();
        bool opened = false;
        try
        {
            if (!reader.Open(drive)) { _log.Info("disc", $"read drive={drive}: no readable audio disc"); return null; }
            opened = true;

            int driveOffset = 0;
            try { CUETools.AccurateRip.AccurateRipVerify.FindDriveReadOffset(reader.ARName, out driveOffset); } catch { }

            var cue = new CUESheet(_config);
            if (onStatus != null) cue.CUEToolsProgress += (s, e) => { if (!string.IsNullOrEmpty(e.status)) onStatus(e.status); };
            cue.OpenCD(reader);
            CDImageLayout toc = reader.TOC;

            string album = "Unknown album", artist = "", year = "";
            var matches = new List<ReleaseMatch>();

            // metadata lookup is best-effort: keep generic names if the disc isn't found or we're offline
            try
            {
                onStatus?.Invoke("Looking up disc in the databases...");
                var releases = cue.LookupAlbumInfo(false, false, true, CTDBMetadataSearch.Extensive);
                int idx = 0;
                foreach (var r in releases)
                    if (r is CUEMetadataEntry e) matches.Add(BuildMatch(e, idx++, (int)toc.AudioTracks));

                // rank best-first by source quality + metadata completeness; apply the best.
                matches.Sort((a, b) => b.Score.CompareTo(a.Score));
                if (matches.Count > 0 && matches[0].Metadata != null)
                {
                    matches[0].IsBest = true;
                    cue.CopyMetadata(matches[0].Metadata);
                    artist = cue.Metadata?.Artist ?? "";
                    if (!string.IsNullOrEmpty(cue.Metadata?.Title)) album = cue.Metadata!.Title;
                    year = cue.Metadata?.Year ?? "";
                }
                // log the outcome by source/count only (no titles); scrub the chosen names after
                _log.Info("disc", $"read ok drive='{(reader.ARName ?? "").Trim()}' tracks={toc.AudioTracks} " +
                    $"releases={matches.Count} best_source={(matches.Count > 0 ? matches[0].Source : "none")}");
                _log.Redact(artist, album);
            }
            catch (Exception ex) { _log.Error("disc", "metadata lookup failed", ex); /* offline - generic names */ }

            var metaTracks = cue.Metadata?.Tracks;
            var tracks = new List<TrackItem>();
            for (int i = toc.FirstAudio; i < toc.FirstAudio + (int)toc.AudioTracks; i++)
            {
                CDTrack t = toc[i];
                int idx = i - toc.FirstAudio;
                string title = (metaTracks != null && idx < metaTracks.Count && !string.IsNullOrEmpty(metaTracks[idx].Title))
                    ? metaTracks[idx].Title
                    : $"Track {t.Number:00}";
                tracks.Add(new TrackItem { Number = (int)t.Number, Title = title, Length = TimeSpan.FromSeconds(t.Length / 75.0) });
            }

            var info = new DiscInfo
            {
                DriveName = (reader.ARName ?? "").Trim(),
                Offset = driveOffset,
                Album = album,
                Artist = artist,
                Year = year,
                TrackCount = toc.TrackCount,
                AudioTracks = (int)toc.AudioTracks,
                Tracks = tracks,
                TotalLength = TimeSpan.FromSeconds(toc.AudioLength / 75.0),
                TocId = toc.ToString() ?? "",
                ReleaseMatches = matches.ConvertAll(m => m.Header),
                Releases = matches
            };

            cue.Close();
            opened = false;
            return info;
        }
        catch (Exception ex)
        {
            _log.Warn("disc", $"read drive={drive} failed (no disc / not ready / data disc): {ex.GetType().Name}");
            return null; // caller shows the empty state
        }
        finally
        {
            try { if (opened) reader.Close(); else reader.Dispose(); } catch { }
        }
      }
    }

    // Every returned release already matches the disc TOC; scoring ranks them by source quality
    // and metadata completeness so the user can see which is best and why.
    private static ReleaseMatch BuildMatch(CUEMetadataEntry e, int index, int audioTracks)
    {
        var m = e.metadata;
        int total = (m.Tracks != null && m.Tracks.Count > 0) ? m.Tracks.Count : audioTracks;
        int titled = 0;
        if (m.Tracks != null) foreach (var t in m.Tracks) if (!string.IsNullOrWhiteSpace(t.Title)) titled++;
        bool cover = e.cover != null && e.cover.Length > 0;
        string source = PrettySource(e.ImageKey);
        string detail = !string.IsNullOrWhiteSpace(m.ReleaseDateAndLabel) ? m.ReleaseDateAndLabel : (m.Country ?? "");

        int score = SourceRank(e.ImageKey)
            + (string.IsNullOrEmpty(m.Year) ? 0 : 12)
            + (string.IsNullOrEmpty(detail) ? 0 : 8)
            + (cover ? 15 : 0)
            + (total > 0 ? (int)(25.0 * titled / total) : 0);

        var q = new List<string>
        {
            (titled >= total && total > 0) ? "all track titles" : titled > 0 ? $"{titled}/{total} track titles" : "no track titles"
        };
        if (!string.IsNullOrEmpty(m.Year)) q.Add("year");
        if (!string.IsNullOrEmpty(detail)) q.Add("label/date");
        if (cover) q.Add("cover art");

        return new ReleaseMatch
        {
            Index = index,
            Source = source,
            Artist = m.Artist ?? "",
            Title = m.Title ?? "",
            Year = m.Year ?? "",
            Detail = detail,
            TrackCount = total,
            TitledTracks = titled,
            HasCover = cover,
            Score = score,
            Why = $"{source}: matches the disc layout; {string.Join(", ", q)}",
            Metadata = m
        };
    }

    private static string PrettySource(string key)
    {
        string s = (key ?? "").ToLowerInvariant();
        if (s.Contains("musicbrainz")) return "MusicBrainz";
        if (s.Contains("discogs")) return "Discogs";
        if (s.Contains("freedb") || s.Contains("gnudb")) return "freedb";
        if (s.Contains("ctdb") || s.Contains("cuetools")) return "CTDB";
        if (s == "local") return "local cache";
        if (s == "cue") return "embedded cue";
        if (s == "tags") return "file tags";
        return string.IsNullOrWhiteSpace(key) ? "database" : key;
    }

    private static int SourceRank(string key)
    {
        string s = (key ?? "").ToLowerInvariant();
        if (s.Contains("musicbrainz")) return 100;
        if (s.Contains("discogs")) return 90;
        if (s.Contains("ctdb") || s.Contains("cuetools")) return 85;
        if (s.Contains("freedb") || s.Contains("gnudb")) return 45;
        if (s == "local") return 35;
        if (s == "cue") return 25;
        if (s == "tags") return 20;
        return 40;
    }

    // Read the drive's full capability snapshot (identity + GET CONFIGURATION + speeds + tray
    // state) and fold in the AccurateRip read offset. Works with an empty tray - identity comes
    // from INQUIRY, which needs no disc. Blocking SCSI: callers run this off the UI thread.
    public DriveDetails GetDriveDetails(char drive)
    {
        DriveCapabilities caps;
        // fail safe like ReadDisc does: a flaky/vanishing drive must degrade, not throw into the UI
        try { lock (_scsiGate) { caps = DriveInspector.Query(drive); } }
        catch (Exception ex)
        {
            _log.Warn("drive", $"details drive={drive} query failed: {ex.GetType().Name}");
            return new DriveDetails { Letter = drive, Error = "The drive did not answer." };
        }
        int offset = 0; bool known = false;
        if (caps.Valid)
        {
            try { known = CUETools.AccurateRip.AccurateRipVerify.FindDriveReadOffset(caps.ARName, out offset); }
            catch { /* offline or no cached offset table - offset stays unknown */ }
        }
        _log.Info("drive", $"details drive={drive} valid={caps.Valid} model='{caps.DisplayName}' fw='{caps.Firmware}' " +
            $"profile={caps.CurrentProfileName} readx={caps.MaxReadCdX} tray={caps.Tray} offsetKnown={known}");
        return DriveDetails.From(caps, offset, known);
    }

    // Fast tray/media state query (SCSI GET EVENT STATUS NOTIFICATION - does not spin the disc),
    // used both for the Eject/Close button label and for the disc-insertion watcher.
    public DriveTrayState GetTrayState(char drive)
    {
        // polled every 2s by the tray watcher - a throw here would spam the crash handler
        try { lock (_scsiGate) { return DriveInspector.GetTray(drive); } }
        catch { return DriveTrayState.Unknown; }
    }

    // Tray control via the storage IOCTLs (the proven path): eject opens the tray with or without
    // a disc; load pulls it back in. Both are no-ops if the mechanism is already in that state.
    public void OpenTray(char drive) => TrayIoctl(drive, 0x2D4808 /*IOCTL_STORAGE_EJECT_MEDIA*/, "open");
    public void CloseTray(char drive) => TrayIoctl(drive, 0x2D480C /*IOCTL_STORAGE_LOAD_MEDIA*/, "close");

    private void TrayIoctl(char drive, uint code, string what)
    {
      lock (_scsiGate)
      {
        var h = CreateFileW($@"\\.\{char.ToUpperInvariant(drive)}:", 0x80000000 /*GENERIC_READ*/,
            0x1 | 0x2 /*share read+write*/, IntPtr.Zero, 3 /*OPEN_EXISTING*/, 0, IntPtr.Zero);
        if (h == new IntPtr(-1)) { _log.Warn("drive", $"tray {what} {drive}: cannot open device"); return; }
        try
        {
            bool ok = DeviceIoControl(h, code, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
            _log.Info("drive", $"tray {what} {drive} ok={ok}");
        }
        finally { CloseHandle(h); }
      }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "CreateFileW")]
    private static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sa, uint creation, uint flags, IntPtr template);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr h, uint code, IntPtr inBuf, uint inSize, IntPtr outBuf, uint outSize, out uint returned, IntPtr overlapped);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr h);
}
