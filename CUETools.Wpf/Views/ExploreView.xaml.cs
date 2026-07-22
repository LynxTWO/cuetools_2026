using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CUETools.Wpf.Views;

public partial class ExploreView : UserControl
{
    private Point _last;
    private bool _drag;   // left = orbit
    private bool _pan;    // right = pan the look-at target

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
        if (!_pan) Stage.ReleaseMouseCapture();
    }

    private void Stage_RightDown(object sender, MouseButtonEventArgs e)
    {
        _pan = true;
        _last = e.GetPosition(Stage);
        Stage.CaptureMouse();
    }

    private void Stage_RightUp(object sender, MouseButtonEventArgs e)
    {
        _pan = false;
        if (!_drag) Stage.ReleaseMouseCapture();
    }

    private void Stage_Move(object sender, MouseEventArgs e)
    {
        if (!_drag && !_pan) return;
        var p = e.GetPosition(Stage);
        double dx = p.X - _last.X, dy = p.Y - _last.Y;
        if (_pan) Disc.Pan(dx, dy);
        else Disc.Orbit(dx * 0.01, -dy * 0.01);
        _last = p;
    }

    private void Stage_Wheel(object sender, MouseWheelEventArgs e)
        => Disc.Zoom(e.Delta > 0 ? 0.9 : 1.1);
}
