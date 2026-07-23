using System.Windows;
using System.Windows.Controls;
using CUETools.Processor;
using CUETools.Wpf.Services;
using CUETools.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CUETools.Wpf.Views;

public partial class ConvertView : UserControl
{
    public ConvertView()
    {
        InitializeComponent();
    }

    // opens the per-encoder settings dialog for the currently selected output format
    private void EncoderSettings_Click(object sender, RoutedEventArgs e)
    {
        var sp = App.Services;
        var vm = DataContext as ConvertViewModel;
        if (sp == null || vm == null) return;
        EncoderSettingsWindow.Open(Window.GetWindow(this)!,
            sp.GetRequiredService<CUEConfig>(), sp.GetRequiredService<EncoderCatalog>(), vm.SelectedFormat);
    }
}
