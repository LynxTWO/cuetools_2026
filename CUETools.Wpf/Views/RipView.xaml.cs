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
    private void EncoderSettings_Click(object sender, RoutedEventArgs e)
    {
        var sp = App.Services;
        var vm = DataContext as RipViewModel;
        if (sp == null || vm == null) return;
        sp.GetRequiredService<IDiagnosticLog>().Info("ui", "encoder settings opened for " + vm.SelectedFormat);
        EncoderSettingsWindow.Open(Window.GetWindow(this)!,
            sp.GetRequiredService<CUEConfig>(), sp.GetRequiredService<EncoderCatalog>(), vm.SelectedFormat);
    }
}
