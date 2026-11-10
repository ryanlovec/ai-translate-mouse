using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using AIMouse.Native;

namespace AIMouse.UI;

/// <summary>Small click-through note under a copied region: "已复制 · N 字" plus a preview of the text.</summary>
public sealed class CopyToast : Window
{
    readonly TextBlock _title, _preview;
    readonly DispatcherTimer _timer;
    RECT _region;

    public event Action? AutoHidden;

    public CopyToast()
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

        _title = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Segoe UI Variable Text, Microsoft YaHei UI") };
        _preview = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x73)),
            FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 116,
            Margin = new Thickness(0, 5, 0, 0),
        };
        var stack = new StackPanel();
        stack.Children.Add(_title);
        stack.Children.Add(_preview);
        Content = new Border
        {
            Margin = new Thickness(10),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromRgb(0xFC, 0xFC, 0xFD)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE3, 0xE3, 0xE8)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 14, 11),
            MinWidth = 150,
            MaxWidth = 380,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.14 },
            Child = stack,
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2600) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Hide();
            AutoHidden?.Invoke();
        };
        SourceInitialized += (_, _) => WindowHelper.MakeToolWindow(this, clickThrough: true);
    }

    public void ShowBusy(RECT region)
    {
        _timer.Stop();
        Set(region, "识别中…", Color.FromRgb(0x86, 0x86, 0x8B), "");
    }

    public void ShowResult(RECT region, bool ok, string title, string preview)
    {
        Set(region, title, ok ? Color.FromRgb(0x24, 0x8A, 0x3D) : Color.FromRgb(0xFF, 0x3B, 0x30), preview);
        _timer.Stop();
        _timer.Interval = TimeSpan.FromMilliseconds(ok ? 2600 : 2000);
        _timer.Start();
    }

    public void HideNow()
    {
        _timer.Stop();
        if (IsVisible) Hide();
    }

    void Set(RECT region, string title, Color color, string preview)
    {
        _region = region;
        _title.Text = title;
        _title.Foreground = new SolidColorBrush(color);
        _preview.Text = preview.Length > 400 ? preview[..400] : preview;
        _preview.Visibility = preview.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Reposition();
    }

    /// <summary>Just below the region (above it if no room), like the word card.</summary>
    void Reposition()
    {
        var mon = WindowHelper.GetMonitor(new POINT(_region.Left, _region.Bottom));
        double s = mon.Scale;
        if (!IsVisible) WindowHelper.MovePhysical(this, -32000, -32000);
        UpdateLayout();
        int cw = (int)Math.Ceiling(ActualWidth * s), ch = (int)Math.Ceiling(ActualHeight * s);
        int m = (int)(10 * s), gap = (int)(6 * s);
        var wa = mon.Work;
        int x = _region.Left - m;
        int y = _region.Bottom + gap - m;
        if (y + ch > wa.Bottom) y = _region.Top - gap - ch + m;
        x = Math.Max(wa.Left, Math.Min(x, wa.Right - cw));
        y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - ch));
        WindowHelper.MovePhysical(this, x, y);
    }
}
