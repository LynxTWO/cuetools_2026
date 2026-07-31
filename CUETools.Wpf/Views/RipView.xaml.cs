using System.Windows;
using System.Windows.Controls;
using CUETools.Processor;
using CUETools.Wpf.Services;
using CUETools.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CUETools.Wpf.Views;

public partial class RipView : UserControl
{
    public RipView()
    {
        InitializeComponent();
    }

    // opens the per-encoder settings dialog for the currently selected output format
    private void CodecPicker_Click(object sender, RoutedEventArgs e)
    {
        var services = App.Services;
        if (services == null || DataContext is not RipViewModel viewModel) return;
        var window = new CodecPickerWindow(
            viewModel.CodecChoices,
            viewModel.SelectedCodecChoice?.StableId)
        {
            Owner = Window.GetWindow(this)
        };
        if (window.ShowDialog() == true && window.SelectedChoice != null)
            viewModel.SelectCodec(window.SelectedChoice);
    }

    // opens the per-encoder settings dialog for the currently selected output format
    private void EncoderSettings_Click(object sender, RoutedEventArgs e)
    {
        var sp = App.Services;
        var vm = DataContext as RipViewModel;
        if (sp == null || vm == null) return;
        sp.GetRequiredService<IDiagnosticLog>().Info("ui", "encoder settings opened for " + vm.SelectedFormat);
        EncoderSettingsWindow.Open(Window.GetWindow(this)!,
            sp.GetRequiredService<CUEConfig>(), sp.GetRequiredService<EncoderCatalog>(), vm.SelectedFormat);
    }

    private void Artwork_Click(object sender, RoutedEventArgs e)
    {
        var services = App.Services;
        if (services == null || DataContext is not RipViewModel viewModel) return;
        var window = new ArtworkBrowserWindow(
            viewModel,
            services.GetRequiredService<IAlbumArtService>())
        {
            Owner = Window.GetWindow(this)
        };
        window.ShowDialog();
    }

    private void Artwork_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = HasOneDroppedFile(e.Data)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Artwork_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!HasOneDroppedFile(e.Data))
        {
            MessageBox.Show(
                "Drop exactly one JPEG, PNG, or BMP image.",
                "Cover not accepted",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        if (DataContext is not RipViewModel viewModel)
            return;
        string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
        await viewModel.ImportLocalArtworkAsync(files[0]);
    }

    private static bool HasOneDroppedFile(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) &&
        data.GetData(DataFormats.FileDrop) is string[] { Length: 1 };
}
