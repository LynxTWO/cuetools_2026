using System.Windows.Controls;
using System.Windows;
using CUETools.Wpf.ViewModels;

namespace CUETools.Wpf.Views;

public partial class AdvancedView : UserControl
{
    public AdvancedView()
    {
        InitializeComponent();
    }

    private void SetProxyPassword_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AdvancedViewModel viewModel &&
            viewModel.SetProxyPassword(ProxyPasswordInput.Password))
            ProxyPasswordInput.Clear();
    }

    private void ClearProxyPassword_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AdvancedViewModel viewModel)
            viewModel.ClearProxyPassword();
        ProxyPasswordInput.Clear();
    }
}
