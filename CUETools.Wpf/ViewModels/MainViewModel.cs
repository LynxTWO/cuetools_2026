using System.Collections.Generic;
using System.Collections.ObjectModel;
using CUETools.Wpf.Mvvm;

namespace CUETools.Wpf.ViewModels;

/// <summary>Shell view model: the nav destinations and the current page.</summary>
public sealed class MainViewModel : ViewModelBase
{
    public ObservableCollection<PageViewModel> Pages { get; }

    private PageViewModel _currentPage;
    public PageViewModel CurrentPage
    {
        get => _currentPage;
        set => Set(ref _currentPage, value);
    }

    // Placeholder until Phase 3 wires the real drive enumeration through ICDRipper.
    public string DriveStatus => "ASUS BW-16D1HT   K:   offset +6";

    public MainViewModel(IEnumerable<PageViewModel> pages)
    {
        Pages = new ObservableCollection<PageViewModel>(pages);
        _currentPage = Pages[0];
    }
}
