using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CUETools.Wpf.ViewModels;

namespace CUETools.Wpf.Views;

public partial class VerifyView : UserControl
{
    public VerifyView()
    {
        InitializeComponent();
    }

    private void Verify_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = CanAcceptFileDrop(e)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Verify_Drop(object sender, DragEventArgs e)
    {
        if (!CanAcceptFileDrop(e) || DataContext is not VerifyViewModel viewModel)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        string[] paths = ((string[])e.Data.GetData(DataFormats.FileDrop)!)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
        e.Effects = viewModel.LoadSources(paths)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private bool CanAcceptFileDrop(DragEventArgs e)
    {
        return DataContext is VerifyViewModel { IsBusy: false } &&
            e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 };
    }
}
