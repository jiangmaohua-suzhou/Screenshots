using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;

namespace Screenshot.Windows;

public partial class CaptureOverlayWindow : Window
{
    private Point _startPoint;
    private bool _isSelecting;

    public Int32Rect? SelectedRegion { get; private set; }

    public CaptureOverlayWindow()
    {
        InitializeComponent();

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        OverlayCanvas.MouseLeftButtonDown += OverlayCanvas_MouseLeftButtonDown;
        OverlayCanvas.MouseMove += OverlayCanvas_MouseMove;
        OverlayCanvas.MouseLeftButtonUp += OverlayCanvas_MouseLeftButtonUp;
    }

    private void OverlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(OverlayCanvas);
        _isSelecting = true;
        OverlayCanvas.CaptureMouse();
        HintPanel.Visibility = Visibility.Collapsed;
        UpdateSelectionRect(_startPoint, _startPoint);
        SelectionRect.Visibility = Visibility.Visible;
    }

    private void OverlayCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isSelecting)
        {
            return;
        }

        UpdateSelectionRect(_startPoint, e.GetPosition(OverlayCanvas));
    }

    private void OverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting)
        {
            return;
        }

        _isSelecting = false;
        OverlayCanvas.ReleaseMouseCapture();

        var endPoint = e.GetPosition(OverlayCanvas);
        var x = (int)Math.Min(_startPoint.X, endPoint.X) + (int)SystemParameters.VirtualScreenLeft;
        var y = (int)Math.Min(_startPoint.Y, endPoint.Y) + (int)SystemParameters.VirtualScreenTop;
        var width = (int)Math.Abs(endPoint.X - _startPoint.X);
        var height = (int)Math.Abs(endPoint.Y - _startPoint.Y);

        if (width < 5 || height < 5)
        {
            DialogResult = false;
            Close();
            return;
        }

        SelectedRegion = new Int32Rect(x, y, width, height);
        DialogResult = true;
        Close();
    }

    private void UpdateSelectionRect(Point start, Point end)
    {
        var x = Math.Min(start.X, end.X);
        var y = Math.Min(start.Y, end.Y);
        var width = Math.Abs(end.X - start.X);
        var height = Math.Abs(end.Y - start.Y);

        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = width;
        SelectionRect.Height = height;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }
}
