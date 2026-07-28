using System.Windows.Controls;
using System.Windows;

namespace CUETools.Wpf.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void SetTheAudioDbApiKey_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.SettingsViewModel viewModel &&
            viewModel.SetTheAudioDbApiKey(TheAudioDbApiKeyInput.Password))
        {
            TheAudioDbApiKeyInput.Clear();
            return;
        }
        MessageBox.Show(
            "Enter a TheAudioDB API key without spaces or URL punctuation.",
            "API key not accepted",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void ClearTheAudioDbApiKey_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.SettingsViewModel viewModel)
            viewModel.ClearTheAudioDbApiKey();
        TheAudioDbApiKeyInput.Clear();
    }
}
