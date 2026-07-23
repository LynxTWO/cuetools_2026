using System.Windows;
using System.Windows.Controls;
using CUETools.Wpf.ViewModels;

namespace CUETools.Wpf.Views;

public partial class NamingView : UserControl
{
    public NamingView()
    {
        InitializeComponent();
        // pull in the tray disc (if any) each time the page opens - deferred out of the VM ctor,
        // which runs during container build when the disc lookup would re-enter the container
        Loaded += (_, _) => Vm?.Refresh();
    }

    private NamingViewModel? Vm => DataContext as NamingViewModel;

    // insert the clicked palette field at the caret in the template box, then keep focus + caret
    private void Field_Click(object sender, RoutedEventArgs e)
    {
        if (Vm == null || sender is not Button b || b.Content is not string field) return;
        int caret = TemplateBox.CaretIndex;
        int newCaret = Vm.InsertField(field, caret);
        TemplateBox.Focus();
        TemplateBox.CaretIndex = newCaret;
    }

    private void Preset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Vm == null || PresetBox.SelectedItem is not string name) return;
        Vm.ApplyPreset(name);
    }
}
