using System.Windows;
using System.Windows.Controls;
using CUETools.Wpf.ViewModels;

namespace CUETools.Wpf.Views;

public partial class QueueView : UserControl
{
    public QueueView()
    {
        InitializeComponent();
    }

    private void CodecPicker_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not QueueViewModel viewModel) return;
        var window = new CodecPickerWindow(
            viewModel.CodecChoices,
            viewModel.SelectedCodecChoice?.StableId)
        {
            Owner = Window.GetWindow(this)
        };
        if (window.ShowDialog() == true && window.SelectedChoice != null)
            viewModel.SelectCodec(window.SelectedChoice);
    }
}
