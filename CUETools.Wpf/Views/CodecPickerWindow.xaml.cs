using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using CUETools.Wpf.Services;

namespace CUETools.Wpf.Views;

public partial class CodecPickerWindow : Window, INotifyPropertyChanged
{
    private CodecChoice? _selectedChoice;
    private string _selectedSort = "Recommended";
    private bool _showUnavailable = true;

    public ObservableCollection<CodecChoice> Choices { get; } = new();
    public ICollectionView ChoicesView { get; }
    public IReadOnlyList<string> SortModes { get; } = new[]
    {
        "Recommended",
        "Compression guidance",
        "Perceptual efficiency",
        "Name",
    };

    public CodecChoice? SelectedChoice
    {
        get => _selectedChoice;
        set
        {
            if (ReferenceEquals(_selectedChoice, value)) return;
            _selectedChoice = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(SelectedChoice)));
        }
    }

    public string SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (string.Equals(_selectedSort, value, StringComparison.Ordinal)) return;
            _selectedSort = value;
            ApplySort();
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(SelectedSort)));
        }
    }

    public bool ShowUnavailable
    {
        get => _showUnavailable;
        set
        {
            if (_showUnavailable == value) return;
            _showUnavailable = value;
            ChoicesView.Refresh();
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(ShowUnavailable)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CodecPickerWindow(
        IEnumerable<CodecChoice> choices,
        string? selectedStableId)
    {
        InitializeComponent();
        foreach (CodecChoice choice in choices)
            Choices.Add(choice);
        ChoicesView = CollectionViewSource.GetDefaultView(Choices);
        ChoicesView.GroupDescriptions.Add(
            new PropertyGroupDescription(nameof(CodecChoice.Category)));
        ChoicesView.Filter = item =>
            ShowUnavailable || item is CodecChoice { CanSelect: true };
        ApplySort();
        SelectedChoice = Choices.FirstOrDefault(choice =>
                string.Equals(
                    choice.StableId,
                    selectedStableId,
                    StringComparison.OrdinalIgnoreCase))
            ?? Choices.FirstOrDefault(choice => choice.CanSelect)
            ?? Choices.FirstOrDefault();
        DataContext = this;
    }

    private void ApplySort()
    {
        if (ChoicesView == null) return;
        using (ChoicesView.DeferRefresh())
        {
            ChoicesView.SortDescriptions.Clear();
            ChoicesView.SortDescriptions.Add(
                new SortDescription(
                    nameof(CodecChoice.CategoryOrder),
                    ListSortDirection.Ascending));
            ChoicesView.SortDescriptions.Add(
                new SortDescription(
                    nameof(CodecChoice.CanSelect),
                    ListSortDirection.Descending));
            string rank = SelectedSort switch
            {
                "Compression guidance" => nameof(CodecChoice.CompressionRank),
                "Perceptual efficiency" => nameof(CodecChoice.EfficiencyRank),
                "Name" => nameof(CodecChoice.FormatName),
                _ => nameof(CodecChoice.RecommendedRank),
            };
            ChoicesView.SortDescriptions.Add(
                new SortDescription(rank, ListSortDirection.Ascending));
            if (rank != nameof(CodecChoice.FormatName))
                ChoicesView.SortDescriptions.Add(
                    new SortDescription(
                        nameof(CodecChoice.FormatName),
                        ListSortDirection.Ascending));
            ChoicesView.SortDescriptions.Add(
                new SortDescription(
                    nameof(CodecChoice.Implementation),
                    ListSortDirection.Ascending));
        }
    }

    private void UseSelected_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedChoice?.CanSelect != true) return;
        DialogResult = true;
    }

    private void CodecList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedChoice?.CanSelect != true) return;
        DialogResult = true;
    }
}
