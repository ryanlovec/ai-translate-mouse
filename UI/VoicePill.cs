using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using AIMouse.Native;

namespace AIMouse.UI;

/// <summary>
/// Dark floating pill at the bottom of the screen while you talk: red dot + live sound bars + "正在听… 松开结束",
/// then "识别中…", then the recognized text for a moment. Click-through, never takes focus.
/// </summary>
public sealed class VoicePill : Window
{
    readonly Ellipse _dot;
    readonly Rectangle[] _bars = new Rectangle[5];
    readonly StackPanel _barPanel;
    readonly TextBlock _text, _hint;
    readonly DispatcherTimer _hide;
    readonly double[] _weights = { 0.55, 0.8, 1.0, 0.8, 0.55 };

    static readonly FontFamily Font = new("Segoe UI Variable Text, Microsoft YaHei UI");

    public VoicePill()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        Focusable = false;
        IsHitTestVisible = false;

        _dot = new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        _barPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Height = 22, Margin = new Thickness(0, 0, 12, 0) };
        for (int i = 0; i < _bars.Length; i++)
        {
            _bars[i] = new Rectangle { Width = 3, Height = 4, RadiusX = 1.5, RadiusY = 1.5, Fill = Brushes.White, Margin = new Thickness(1.5, 0, 1.5, 0), VerticalAlignment = VerticalAlignment.Center };
            _barPanel.Children.Add(_bars[i]);
        }
        _text = new TextBlock { FontFamily = Font, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 520, TextTrimming = TextTrimming.CharacterEllipsis };
        _hint = new TextBlock { FontFamily = Font, FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 1, 0, 0) };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_dot);
        row.Children.Add(_barPanel);
        row.Children.Add(_text);
        row.Children.Add(_hint);
        Content = new Border
        {
            Margin = new Thickness(16),
            CornerRadius = new CornerRadius(22),
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x1D, 0x1D, 0x1F)),
            Padding = new Thickness(18, 10, 20, 10),
            Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 8, Direction = 270, Opacity = 0.28 },
            Child = row,
        };
        _hide = new DispatcherTimer();
        _hide.Tick += (_, _) => { _hide.Stop(); Hide(); };
        SourceInitialized += (_, _) => WindowHelper.MakeToolWindow(this, clickThrough: true);
    }

    public void ShowListening()
    {
        _hide.Stop();
        _dot.Visibility = Visibility.Visible;
        _barPanel.Visibility = Visibility.Visible;
        _dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(650)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        _text.Text = "正在听…";
        _hint.Text = "松开右 Alt 结束";
        SetLevel(0);
        Place();
    }

    public void SetLevel(double level)
    {
        for (int i = 0; i < _bars.Length; i++)
            _bars[i].Height = 4 + 18 * Math.Clamp(level * _weights[i] * (0.8 + 0.4 * Random.Shared.NextDouble()), 0, 1);
    }

    public void ShowWorking()
    {
        _hide.Stop();
        _dot.BeginAnimation(OpacityProperty, null);
        _dot.Visibility = Visibility.Collapsed;
        _barPanel.Visibility = Visibility.Collapsed;
        _text.Text = "识别中…";
        _hint.Text = "";
        Place();
    }

    public void ShowResult(string text, string hint, double seconds = 2.2)
    {
        _dot.BeginAnimation(OpacityProperty, null);
        _dot.Visibility = Visibility.Collapsed;
        _barPanel.Visibility = Visibility.Collapsed;
        _text.Text = text;
        _hint.Text = hint;
        Place();
        _hide.Interval = TimeSpan.FromSeconds(seconds);
        _hide.Start();
    }

    /// <summary>Bottom-center of the monitor under the mouse, just above the taskbar.</summary>
    void Place()
    {
        if (!IsVisible) Show();
        UpdateLayout();
        Win32.GetCursorPos(out var p);
        var mon = WindowHelper.GetMonitor(p);
        var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        int w = (int)(ActualWidth * mon.Scale), ht = (int)(ActualHeight * mon.Scale);
        var wa = mon.Work;
        Win32.SetWindowPos(h, Win32.HWND_TOPMOST, wa.Left + (wa.Width - w) / 2, wa.Bottom - ht - (int)(24 * mon.Scale), w, ht, Win32.SWP_NOACTIVATE);
    }
}
