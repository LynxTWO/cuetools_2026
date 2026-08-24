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

    // GridViewColumn has no star sizing, so the last column is measured here instead. See
    // QueueColumnLayout for the arithmetic and why it is a pure function.
    private void QueueList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged)
            return;
        ResultColumn.Width =
            QueueColumnLayout.ResultWidth(e.NewSize.Width, QueueColumnLayout.Chrome);
    }
}
