using CUETools.Wpf.Mvvm;

namespace CUETools.Wpf.ViewModels;

/// <summary>
/// "How a CD Works" - a self-guided 3D lesson (stage 2 of the disc visualization). Orbit and zoom the
/// disc freely, from the whole disc down to the data track, alongside a cross-section of the CD's
/// layers. Not driven by a live rip; it is the explorable companion to the live disc on the Rip page.
/// </summary>
public sealed class ExploreViewModel : PageViewModel
{
    public ExploreViewModel()
    {
        Title = "How a CD Works";
        Group = "Learn";
        Subtitle = "Explore the disc in 3D - drag to orbit, scroll to zoom from the whole disc down to the data spiral.";
    }
}
