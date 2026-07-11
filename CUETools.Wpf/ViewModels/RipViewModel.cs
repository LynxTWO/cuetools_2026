using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using CUETools.Wpf.Models;
using CUETools.Wpf.Mvvm;
using CUETools.Wpf.Services;

namespace CUETools.Wpf.ViewModels;

/// <summary>
/// Rip page. Increment 1: enumerate drives, read the inserted disc's TOC through the
/// ripper, show the track list; no-disc shows the recently-ripped list. Encode/verify and
/// the live disc read-map come in later increments.
/// </summary>
public sealed class RipViewModel : PageViewModel
{
    private readonly IDriveService _drives;
    private readonly IRipService _rip;

    public ObservableCollection<char> Drives { get; } = new();
    public ObservableCollection<TrackItem> Tracks { get; } = new();
    public ObservableCollection<RecentRip> Recent { get; } = new();

    private char _selectedDrive;
    public char SelectedDrive
    {
        get => _selectedDrive;
        set { if (Set(ref _selectedDrive, value)) _ = ReadDiscAsync(); }
    }

    private bool _isDiscPresent;
    public bool IsDiscPresent { get => _isDiscPresent; private set => Set(ref _isDiscPresent, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

    private string _albumTitle = "Insert a disc";
    public string AlbumTitle { get => _albumTitle; private set => Set(ref _albumTitle, value); }

    private string _albumArtist = "";
    public string AlbumArtist { get => _albumArtist; private set => Set(ref _albumArtist, value); }

    private string _discInfoText = "";
    public string DiscInfoText { get => _discInfoText; private set => Set(ref _discInfoText, value); }

    private string _statusText = "Ready";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    private double _ripProgress;
    public double RipProgress { get => _ripProgress; private set => Set(ref _ripProgress, value); }

    private bool _isRipping;
    public bool IsRipping { get => _isRipping; private set => Set(ref _isRipping, value); }

    private string _ripStatus = "";
    public string RipStatus { get => _ripStatus; private set => Set(ref _ripStatus, value); }

    public ICommand ReadDiscCommand { get; }
    public ICommand VerifyCommand { get; }

    public RipViewModel(IDriveService drives, IRipService rip)
    {
        Title = "Rip";
        Group = "Work";
        Subtitle = "Rip a CD: read, encode, and verify against AccurateRip and CTDB.";
        _drives = drives;
        _rip = rip;

        ReadDiscCommand = new RelayCommand(_ => { _ = ReadDiscAsync(); });
        VerifyCommand = new RelayCommand(_ => { _ = VerifyAsync(); }, _ => IsDiscPresent && !IsRipping && !IsBusy);

        foreach (var d in drives.GetDrives()) Drives.Add(d);
        SeedRecent();

        if (Drives.Count > 0)
        {
            _selectedDrive = Drives[0];   // set the field to avoid a double read from the setter
            OnPropertyChanged(nameof(SelectedDrive));
            _ = ReadDiscAsync();
        }
        else
        {
            StatusText = "No optical drive found.";
        }
    }

    private async Task ReadDiscAsync()
    {
        if (_selectedDrive == '\0' || _isBusy) return;
        IsBusy = true;
        StatusText = "Reading disc and looking up metadata...";
        char drive = _selectedDrive;

        DiscInfo? info = await Task.Run(() => _drives.ReadDisc(drive));

        Tracks.Clear();
        if (info == null)
        {
            IsDiscPresent = false;
            AlbumTitle = "No disc in drive " + drive + ":";
            AlbumArtist = "";
            DiscInfoText = "";
            StatusText = "Drive ready - insert an audio CD to begin.";
        }
        else
        {
            IsDiscPresent = true;
            AlbumTitle = info.Album;
            AlbumArtist = string.IsNullOrWhiteSpace(info.Artist)
                ? (string.IsNullOrWhiteSpace(info.DriveName) ? $"Drive {drive}:" : info.DriveName)
                : (string.IsNullOrWhiteSpace(info.Year) ? info.Artist : $"{info.Artist}  ({info.Year})");
            DiscInfoText = $"{info.AudioTracks} tracks   {Fmt(info.TotalLength)}   {info.ReleaseMatches.Count} release match(es)";
            foreach (var t in info.Tracks) Tracks.Add(t);
            StatusText = info.ReleaseMatches.Count > 0
                ? $"Identified: {info.Artist} - {info.Album}. Ripping comes next."
                : "Disc read; not found in the metadata databases (generic track names).";
        }
        IsBusy = false;
    }

    private async Task VerifyAsync()
    {
        if (!IsDiscPresent || IsRipping || IsBusy) return;
        char drive = _selectedDrive;
        IsRipping = true;
        RipProgress = 0;
        StatusText = "Starting verify...";
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        var result = await Task.Run(() => _rip.RunVerify(drive, (frac, status) =>
        {
            // ReadProgress fires on the ripper's thread; marshal to the UI.
            dispatcher?.BeginInvoke(new Action(() => { RipProgress = frac; StatusText = status; }));
        }));

        RipProgress = result.Ok ? 1 : RipProgress;
        StatusText = result.Ok ? result.Status : ("Verify failed: " + result.Error);
        IsRipping = false;
    }

    private static string Fmt(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    private void SeedRecent()
    {
        // Placeholder until the local verification DB (UseLocalDB) is wired in.
        Recent.Add(new RecentRip { Title = "The Four Seasons", Artist = "Vivaldi - Il Giardino Armonico", When = "2 min ago", Result = "12/12 verified" });
        Recent.Add(new RecentRip { Title = "Cello Suites", Artist = "J.S. Bach - Pablo Casals", When = "9 min ago", Result = "12/12 verified" });
        Recent.Add(new RecentRip { Title = "Kind of Blue", Artist = "Miles Davis", When = "16 min ago", Result = "5/5 verified" });
        Recent.Add(new RecentRip { Title = "A Love Supreme", Artist = "John Coltrane", When = "48 min ago", Result = "4/4 verified" });
    }
}
