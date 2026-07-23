using System.Windows;
using CUETools.Processor;
using CUETools.Wpf.ViewModels;

namespace CUETools.Wpf.Views;

/// <summary>The per-encoder settings dialog. Opened from the gear next to a format choice; the
/// content is built by reflection from whichever encoder the chosen format uses, so it shifts
/// with the codec. Changes apply immediately (they mutate the live encoder object) and persist
/// with the app settings on exit.</summary>
public partial class EncoderSettingsWindow : Window
{
    public EncoderSettingsWindow(CUEConfig config, string format, bool lossy)
    {
        InitializeComponent();
        DataContext = new EncoderSettingsViewModel(config, format, lossy);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Open for a format, resolving lossy-ness the same way the format lists do. A
    /// two-faced format (wma; m4a with an imported AAC) carries the TYPE picker: choosing the
    /// other type persists the choice, rebuilds every format list, and rebuilds this dialog
    /// around the other encoder.</summary>
    public static void Open(Window owner, CUEConfig config, Services.EncoderCatalog catalog, string format)
    {
        if (string.IsNullOrWhiteSpace(format) || !config.formats.ContainsKey(format)) return;
        bool lossy = catalog.IsLossyFormat(config.formats[format]);
        try
        {
            var w = new EncoderSettingsWindow(config, format, lossy) { Owner = owner };
            WireTypePicker(w, config, catalog, format, lossy);
            w.ShowDialog();
        }
        catch (System.Exception ex)
        {
            // a user-invoked dialog must not fail silently
            MessageBox.Show("Could not open encoder settings: " + ex.Message, "Encoder settings",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void WireTypePicker(EncoderSettingsWindow w, CUEConfig config,
        Services.EncoderCatalog catalog, string format, bool lossy)
    {
        if (w.DataContext is not EncoderSettingsViewModel vm) return;
        vm.HasTypeChoice = catalog.HasBothTypes(config.formats[format]);
        vm.IsLossyType = lossy;
        vm.TypeChanged += chooseLossy =>
        {
            catalog.SetFormatType(format, chooseLossy);   // persists + rebuilds the format lists
            w.DataContext = new EncoderSettingsViewModel(config, format, chooseLossy);
            WireTypePicker(w, config, catalog, format, chooseLossy);
        };
    }
}
