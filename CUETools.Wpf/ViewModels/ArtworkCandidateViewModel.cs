using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using CUETools.Wpf.Mvvm;
using CUETools.Wpf.Services;
using CUETools.Wpf.Services.Artwork;

namespace CUETools.Wpf.ViewModels;

public sealed class ArtworkCandidateViewModel : ViewModelBase
{
    private readonly IAlbumArtService _service;
    private ImageSource? _thumbnail;
    private string _loadStatus = "";

    public ArtworkCandidate Candidate { get; }
    public int RecommendedOrder { get; }
    public string Source => Candidate.Provider;
    public string Match => Candidate.MatchText;
    public string Why => Candidate.MatchReason;
    public string Dimensions => Candidate.DimensionsText;
    public string FileSize => Candidate.SizeText;
    public string ArtworkType => Candidate.ArtworkType;
    public long PixelArea => Candidate.Width is > 0 && Candidate.Height is > 0
        ? (long)Candidate.Width.Value * Candidate.Height.Value
        : -1;
    public bool HasDimensions => PixelArea >= 0;
    public long ByteLength => Candidate.ByteLength ?? -1;
    public bool HasByteLength => ByteLength >= 0;
    public int MatchOrder => (int)Candidate.MatchTier;
    public string Approval => Candidate.IsApproved
        ? "Approved"
        : Candidate.IsPrimary ? "Primary" : "";
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        private set => Set(ref _thumbnail, value);
    }
    public string LoadStatus
    {
        get => _loadStatus;
        private set => Set(ref _loadStatus, value);
    }

    public ArtworkCandidateViewModel(
        ArtworkCandidate candidate,
        int recommendedOrder,
        IAlbumArtService service)
    {
        Candidate = candidate;
        RecommendedOrder = recommendedOrder;
        _service = service;
    }

    public async Task LoadThumbnailAsync(CancellationToken ct)
    {
        if (Thumbnail != null) return;
        LoadStatus = "loading";
        try
        {
            AlbumArt? art = await _service.DownloadAsync(Candidate, thumbnail: true, ct);
            if (art == null) { LoadStatus = "unavailable"; return; }
            Thumbnail = RipViewModel.MakeArtworkImage(art.Bytes, 220);
            LoadStatus = Thumbnail == null ? "unavailable" : "";
        }
        catch (OperationCanceledException) { }
        catch { LoadStatus = "unavailable"; }
    }
}
