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

    /// <summary>Open for a format, resolving lossy-ness the same way the format lists do.</summary>
    public static void Open(Window owner, CUEConfig config, Services.EncoderCatalog catalog, string format)
    {
        if (string.IsNullOrWhiteSpace(format) || !config.formats.ContainsKey(format)) return;
        bool lossy = catalog.IsLossyFormat(config.formats[format]);
        try
        {
            var w = new EncoderSettingsWindow(config, format, lossy) { Owner = owner };
            w.ShowDialog();
        }
        catch (System.Exception ex)
        {
            // a user-invoked dialog must not fail silently
            MessageBox.Show("Could not open encoder settings: " + ex.Message, "Encoder settings",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
