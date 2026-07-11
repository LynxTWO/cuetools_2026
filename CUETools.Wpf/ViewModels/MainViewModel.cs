using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CUETools.Wpf.Mvvm;
using CUETools.Wpf.Services;

namespace CUETools.Wpf.ViewModels;

/// <summary>Shell view model: the nav destinations, the current page, and the theme toggle.</summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly ThemeService _theme;

    public ObservableCollection<PageViewModel> Pages { get; }

    private PageViewModel _currentPage;
    public PageViewModel CurrentPage
    {
        get => _currentPage;
        set => Set(ref _currentPage, value);
    }

    /// <summary>Real optical drives, not a fabricated model - the drive model + offset are shown
    /// (and detected) on the Drive & Read page.</summary>
    public string DriveStatus { get; }

    public bool IsLightTheme
    {
        get => _theme.Current == AppTheme.Light;
        set { _theme.Apply(value ? AppTheme.Light : AppTheme.Dark); OnPropertyChanged(); }
    }

    public MainViewModel(IEnumerable<PageViewModel> pages, IDriveService drives, ThemeService theme)
    {
        Pages = new ObservableCollection<PageViewModel>(pages);
        _currentPage = Pages[0];
        _theme = theme;

        var d = drives.GetDrives();
        DriveStatus = d.Count > 0
            ? "optical drive " + string.Join(", ", d.Select(x => x + ":"))
            : "no optical drive";
    }
}
