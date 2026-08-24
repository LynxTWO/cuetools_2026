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
        ApplyResultWidth(ResultColumn, e.NewSize.Width);
    }

    // Extracted so a test can drive the real SizeChanged behaviour against a real GridViewColumn
    // without constructing QueueView itself, which needs an Application with merged resource
    // dictionaries for its StaticResource lookups.
    internal static void ApplyResultWidth(GridViewColumn column, double listWidth)
    {
        column.Width = QueueColumnLayout.ResultWidth(listWidth, QueueColumnLayout.Chrome);
    }
}
