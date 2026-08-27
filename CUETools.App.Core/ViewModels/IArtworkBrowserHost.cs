using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CUETools.Wpf.Services;
using CUETools.Wpf.Services.Artwork;

namespace CUETools.Wpf.ViewModels;

/// <summary>
/// Everything the artwork browser needs from the page that opened it. RipViewModel is the
/// production host; the seam exists so the browser can be shown against synthetic candidates
/// for layout and scaling evidence without a disc, a network, or the full service graph.
/// </summary>
public interface IArtworkBrowserHost
{
    string AlbumTitle { get; }
    string AlbumArtist { get; }
    ObservableCollection<ArtworkCandidate> ArtworkCandidates { get; }
    ArtworkCandidate? SelectedArtwork { get; }
    Task SelectArtworkAsync(ArtworkCandidate candidate);
    void ChooseNoArtwork();
    void RefreshArtwork();
    Task ImportLocalArtworkAsync(string path);
}
