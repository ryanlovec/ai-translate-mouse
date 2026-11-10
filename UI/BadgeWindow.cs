using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIMouse.Native;

namespace AIMouse.UI;

/// <summary>The small "译" pill that follows the cursor while the AI mouse is armed.</summary>
public sealed class BadgeWindow : Window
{
    public BadgeWindow()
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
        Width = 26;
        Height = 18;
        IsHitTestVisible = false;

        Content = new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = new LinearGradientBrush(Color.FromRgb(0x5B, 0x8C, 0xFF), Color.FromRgb(0x4F, 0x46, 0xE5), 45),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "译",
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        SourceInitialized += (_, _) => WindowHelper.MakeToolWindow(this, clickThrough: true);
    }

    public void FollowCursor(POINT p)
    {
        var s = WindowHelper.GetMonitor(p).Scale;
        WindowHelper.PlacePhysical(this, p.X + (int)(16 * s), p.Y + (int)(18 * s), (int)(26 * s), (int)(18 * s));
    }
}
