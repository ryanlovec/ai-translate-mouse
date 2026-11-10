using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using AIMouse.Native;

namespace AIMouse.UI;

/// <summary>Click-through, capture-excluded colored rectangle on top of everything.</summary>
public sealed class RectWindow : Window
{
    readonly Rectangle _shape;

    public RectWindow(Color stroke, double thickness, Color fill, bool dashed = false, double radius = 2)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        Width = 4;
        Height = 4;
        Focusable = false;
        IsHitTestVisible = false;

        _shape = new Rectangle
        {
            Stroke = new SolidColorBrush(stroke),
            StrokeThickness = thickness,
            Fill = new SolidColorBrush(fill),
            RadiusX = radius,
            RadiusY = radius,
            SnapsToDevicePixels = true,
        };
        SetDashed(dashed);
        Content = _shape;
        SourceInitialized += (_, _) => WindowHelper.MakeToolWindow(this, clickThrough: true);
    }

    public void SetDashed(bool dashed) =>
        _shape.StrokeDashArray = dashed ? new DoubleCollection { 4, 3 } : null;

    public void ShowAt(RECT r) => WindowHelper.PlacePhysical(this, r.Left, r.Top, r.Width, r.Height);
}
