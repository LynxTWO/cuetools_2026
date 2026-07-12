using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CUETools.Wpf.Views;

public partial class ExploreView : UserControl
{
    private Point _last;
    private bool _drag;

    public ExploreView()
    {
        InitializeComponent();
    }

    private void Stage_Down(object sender, MouseButtonEventArgs e)
    {
        _drag = true;
        _last = e.GetPosition(Stage);
        Stage.CaptureMouse();
    }

    private void Stage_Up(object sender, MouseButtonEventArgs e)
    {
        _drag = false;
        Stage.ReleaseMouseCapture();
    }

    private void Stage_Move(object sender, MouseEventArgs e)
    {
        if (!_drag) return;
        var p = e.GetPosition(Stage);
        Disc.Orbit((p.X - _last.X) * 0.01, -(p.Y - _last.Y) * 0.01);
        _last = p;
    }

    private void Stage_Wheel(object sender, MouseWheelEventArgs e)
        => Disc.Zoom(e.Delta > 0 ? 0.9 : 1.1);
}
