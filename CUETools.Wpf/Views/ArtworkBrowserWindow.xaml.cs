using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using CUETools.Wpf.Services;
using CUETools.Wpf.Services.Artwork;
using CUETools.Wpf.ViewModels;

namespace CUETools.Wpf.Views;

public partial class ArtworkBrowserWindow : Window, INotifyPropertyChanged
{
    private readonly IArtworkBrowserHost _rip;
    private readonly IAlbumArtService _service;
    private readonly CancellationTokenSource _cts = new();
    private ArtworkCandidateViewModel? _selectedRow;
    private bool _rebuildScheduled;

    public ObservableCollection<ArtworkCandidateViewModel> Rows { get; } = new();
    public string ReleaseTitle =>
        string.IsNullOrWhiteSpace(_rip.AlbumArtist)
            ? _rip.AlbumTitle
            : _rip.AlbumArtist + " - " + _rip.AlbumTitle;
    public ArtworkCandidateViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (ReferenceEquals(_selectedRow, value)) return;
            _selectedRow = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedRow)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelectedSourcePage)));
        }
    }
    public bool HasSelectedSourcePage => SelectedRow?.Candidate.InfoUri != null;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ArtworkBrowserWindow(IArtworkBrowserHost rip, IAlbumArtService service)
    {
        InitializeComponent();
        _rip = rip;
        _service = service;
        DataContext = this;
        RebuildRows();
        _rip.ArtworkCandidates.CollectionChanged += ArtworkCandidates_CollectionChanged;
        Closed += (_, _) =>
        {
            _rip.ArtworkCandidates.CollectionChanged -= ArtworkCandidates_CollectionChanged;
            _cts.Cancel();
        };
        Loaded += async (_, _) => await LoadThumbnailsAsync();
    }

    private void RebuildRows()
    {
        if (_rip.SelectedArtwork is { IsFront: false })
        {
            _showAllArtwork = true;
            AllArtworkCheckBox.IsChecked = true;
        }
        Rows.Clear();
        ArtworkCandidate[] candidates = _rip.ArtworkCandidates
            .OrderBy(candidate => candidate, ArtworkCandidateComparer.Recommended)
            .ToArray();
        for (int index = 0; index < candidates.Length; index++)
            Rows.Add(new ArtworkCandidateViewModel(candidates[index], index, _service, new WpfArtworkPreviewFactory()));
        SelectedRow = Rows.FirstOrDefault(
            row => row.Candidate.CandidateId == _rip.SelectedArtwork?.CandidateId)
            ?? Rows.FirstOrDefault();
        ICollectionView view = CollectionViewSource.GetDefaultView(Rows);
        view.Filter = item => _showAllArtwork ||
                              item is ArtworkCandidateViewModel row &&
                              row.Candidate.IsFront;
        ApplyRecommendedSort(view);
    }

    private static void ApplyRecommendedSort(ICollectionView view)
    {
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(
            new SortDescription(nameof(ArtworkCandidateViewModel.RecommendedOrder),
                ListSortDirection.Ascending));
    }

    private async Task LoadThumbnailsAsync()
    {
        foreach (ArtworkCandidateViewModel row in Rows)
        {
            if (_cts.IsCancellationRequested) break;
            await row.LoadThumbnailAsync(_cts.Token);
        }
    }

    private async void UseSelected_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow == null) return;
        IsEnabled = false;
        try
        {
            await _rip.SelectArtworkAsync(SelectedRow.Candidate);
            DialogResult = true;
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private async void Automatic_Click(object sender, RoutedEventArgs e)
    {
        ArtworkCandidateViewModel? automatic = Rows
            .OrderBy(row => row.RecommendedOrder)
            .FirstOrDefault(row =>
                row.Candidate.AutomaticEligible && row.Candidate.IsFront);
        if (automatic == null) return;
        SelectedRow = automatic;
        await _rip.SelectArtworkAsync(automatic.Candidate);
        DialogResult = true;
    }

    private void NoCover_Click(object sender, RoutedEventArgs e)
    {
        _rip.ChooseNoArtwork();
        DialogResult = true;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _rip.RefreshArtwork();
    }

    private bool _showAllArtwork;

    private void AllArtwork_Changed(object sender, RoutedEventArgs e)
    {
        _showAllArtwork = sender is CheckBox { IsChecked: true };
        CollectionViewSource.GetDefaultView(Rows).Refresh();
    }

    private void Recommended_Click(object sender, RoutedEventArgs e)
    {
        foreach (DataGridColumn column in ResultsGrid.Columns)
            column.SortDirection = null;
        ApplyRecommendedSort(CollectionViewSource.GetDefaultView(Rows));
    }

    private async void AddLocal_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose album artwork",
            Filter = "Supported images|*.jpg;*.jpeg;*.png;*.bmp|JPEG|*.jpg;*.jpeg|PNG|*.png|BMP|*.bmp",
            Multiselect = false,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        await _rip.ImportLocalArtworkAsync(dialog.FileName);
        DialogResult = true;
    }

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = HasOneDroppedFile(e.Data)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!HasOneDroppedFile(e.Data))
        {
            MessageBox.Show(
                this,
                "Drop exactly one JPEG, PNG, or BMP image.",
                "Cover not accepted",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
        await _rip.ImportLocalArtworkAsync(files[0]);
        DialogResult = true;
    }

    private static bool HasOneDroppedFile(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) &&
        data.GetData(DataFormats.FileDrop) is string[] { Length: 1 };

    private void OpenSource_Click(object sender, RoutedEventArgs e)
    {
        Uri? uri = SelectedRow?.Candidate.InfoUri;
        if (uri == null) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void ArtworkCandidates_CollectionChanged(
        object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_rebuildScheduled) return;
        _rebuildScheduled = true;
        _ = Dispatcher.BeginInvoke(async () =>
        {
            _rebuildScheduled = false;
            RebuildRows();
            await LoadThumbnailsAsync();
        });
    }

    private void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedRow != null)
            UseSelected_Click(sender, new RoutedEventArgs());
    }

    private void ResultsGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        string member = e.Column.SortMemberPath;
        if (string.IsNullOrEmpty(member)) return;
        e.Handled = true;
        ListSortDirection direction =
            e.Column.SortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
        foreach (DataGridColumn column in ResultsGrid.Columns)
            if (!ReferenceEquals(column, e.Column)) column.SortDirection = null;
        e.Column.SortDirection = direction;

        ICollectionView view = CollectionViewSource.GetDefaultView(Rows);
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            if (member == nameof(ArtworkCandidateViewModel.PixelArea))
                view.SortDescriptions.Add(new SortDescription(
                    nameof(ArtworkCandidateViewModel.HasDimensions),
                    ListSortDirection.Descending));
            if (member == nameof(ArtworkCandidateViewModel.ByteLength))
                view.SortDescriptions.Add(new SortDescription(
                    nameof(ArtworkCandidateViewModel.HasByteLength),
                    ListSortDirection.Descending));
            view.SortDescriptions.Add(new SortDescription(member, direction));
            view.SortDescriptions.Add(new SortDescription(
                nameof(ArtworkCandidateViewModel.RecommendedOrder),
                ListSortDirection.Ascending));
        }
    }
}
