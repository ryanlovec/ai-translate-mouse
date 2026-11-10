using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIMouse.Native;

namespace AIMouse.UI;

/// <summary>One paragraph to paint over: where it is on screen, its translation, and the page's colors there.</summary>
public sealed record ReplaceBlock(RECT Rect, string Zh, Color Background, Color Foreground, double LineHeightPx);

/// <summary>
/// "译文模式": click-through layer over a screen region that paints each English paragraph over with its
/// Chinese translation, in the page's own background/text colors. Excluded from screen capture, so OCR
/// keeps seeing the original underneath.
/// </summary>
public sealed class ReplaceOverlay : Window
{
    readonly Canvas _canvas = new();
    RECT _region;

    public ReplaceOverlay()
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
        Focusable = false;
        IsHitTestVisible = false;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Content = _canvas;
        SourceInitialized += (_, _) => WindowHelper.MakeToolWindow(this, clickThrough: true);
    }

    public void Render(RECT region, IReadOnlyList<ReplaceBlock> blocks)
    {
        _region = region;
        double s = WindowHelper.GetMonitor(region.Center).Scale;
        _canvas.Children.Clear();
        foreach (var b in blocks)
        {
            if (string.IsNullOrWhiteSpace(b.Zh)) continue;
            var r = b.Rect.Inflate(3);
            int minH = (int)Math.Ceiling(b.LineHeightPx * 1.3);       // short labels still get one full line of space
            if (r.Height < minH)
            {
                int grow = minH - r.Height;
                r = new RECT(r.Left, r.Top - grow / 2, r.Right, r.Bottom + (grow - grow / 2));
            }
            double w = r.Width / s, h = r.Height / s;
            if (h < b.LineHeightPx / s * 2.2)
            {
                // One-line label: if the Chinese is wider than the English, widen the box to the right instead of shrinking the text.
                var probe = new TextBlock { Text = b.Zh, FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = Math.Clamp(b.LineHeightPx / s * 0.8, 9, 40) };
                probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                w = Math.Max(w, Math.Min(probe.DesiredSize.Width + 8, (region.Right - r.Left) / s));
            }
            var tb = new TextBlock
            {
                Text = b.Zh,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                Foreground = new SolidColorBrush(b.Foreground),
                Width = Math.Max(10, w - 4),
            };
            Fit(tb, b.LineHeightPx / s, h - 2);
            var box = new Border
            {
                Width = w,
                Height = h,
                Background = new SolidColorBrush(b.Background),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(2, 1, 2, 1),
                ClipToBounds = true,
                Child = tb,
            };
            Canvas.SetLeft(box, (r.Left - region.Left) / s);
            Canvas.SetTop(box, (r.Top - region.Top) / s);
            _canvas.Children.Add(box);
        }
        WindowHelper.PlacePhysical(this, region.Left, region.Top, region.Width, region.Height);
    }

    /// <summary>Start near the original line height and shrink until the Chinese fits the paragraph's box.</summary>
    static void Fit(TextBlock tb, double origLine, double maxHeight)
    {
        double size = Math.Clamp(origLine * 0.80, 9, 40);
        for (int i = 0; i < 30; i++)
        {
            tb.FontSize = size;
            tb.LineHeight = size * 1.3;
            tb.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            tb.Measure(new Size(tb.Width, double.PositiveInfinity));
            if (tb.DesiredSize.Height <= maxHeight || size <= 8) break;
            size *= 0.93;
        }
    }

    public void Clear()
    {
        _canvas.Children.Clear();
        if (IsVisible) Hide();
    }
}
