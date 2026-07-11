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

    public ICommand ReadDiscCommand { get; }

    public RipViewModel(IDriveService drives)
    {
        Title = "Rip";
        Group = "Work";
        Subtitle = "Rip a CD: read, encode, and verify against AccurateRip and CTDB.";
        _drives = drives;

        ReadDiscCommand = new RelayCommand(_ => { _ = ReadDiscAsync(); });

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
        StatusText = "Reading disc...";
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
            AlbumTitle = "Unknown album";
            AlbumArtist = string.IsNullOrWhiteSpace(info.DriveName) ? $"Drive {drive}:" : info.DriveName;
            DiscInfoText = $"{info.AudioTracks} tracks   {Fmt(info.TotalLength)}   {info.TocId}";
            foreach (var t in info.Tracks) Tracks.Add(t);
            StatusText = $"Disc read: {info.AudioTracks} tracks. Metadata lookup and ripping come next.";
        }
        IsBusy = false;
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
