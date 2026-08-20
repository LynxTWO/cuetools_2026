using CUETools.Wpf.Mvvm;

namespace CUETools.Wpf.ViewModels;

/// <summary>
/// "How a CD Works" - a self-guided lesson (stage 2 of the disc visualization). Explore the disc
/// freely, from the whole disc down to the data track, alongside a cross-section of the CD's
/// layers. Not driven by a live rip; it is the explorable companion to the live disc on the Rip
/// page. The stage depends on the head: the WPF head orbits a 3D disc; a head without
/// hardware 3D pans and zooms a to-scale 2D disc instead, and the subtitle must not
/// promise the other head's stage.
/// </summary>
public sealed class ExploreViewModel : PageViewModel
{
    public ExploreViewModel(bool orbital3D = true)
    {
        Title = "How a CD Works";
        Group = "Learn";
        Subtitle = orbital3D
            ? "Explore the disc in 3D - drag to orbit, scroll to zoom from the whole disc down to the data spiral."
            : "Explore the disc up close - drag to move, scroll to zoom from the whole disc down to the data spiral.";
    }
}
