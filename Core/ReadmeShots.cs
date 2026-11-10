using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AIMouse.Capture;
using AIMouse.Native;
using AIMouse.UI;
using SD = System.Drawing;

namespace AIMouse;

/// <summary>
/// "--readme-shots &lt;dir&gt; --visible-to-capture": shows a sample English web article, runs the real features on it
/// and saves screenshots for the README. Nothing from the user's own screen is captured except this window and ours.
/// </summary>
public partial class App
{
    async Task ReadmeShotsAsync(string outDir)
    {
        Directory.CreateDirectory(outDir);
        const int X = 40, Y = 40, W = 1400, H = 860;
        double s = WindowHelper.GetMonitor(new POINT(X, Y)).Scale;
        var page = BuildArticle(W / s, H / s, out var target, out var para1, out var para2, out var article);
        const int ExtraW = 600, ExtraH = 300;   // plain "desk" around the page, so nothing else on screen shows in the shots
        var desk = new Canvas { Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xED)) };
        desk.Children.Add(page);
        var host = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowActivated = false, Topmost = true, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0, Content = desk,
        };
        host.SourceInitialized += (_, _) =>
            Win32.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(host).Handle, Win32.HWND_TOPMOST, X, Y, W + ExtraW, H + ExtraH, Win32.SWP_NOACTIVATE);
        host.Show();
        await Task.Delay(1200);

        RECT Phys(FrameworkElement fe, double pad = 0)
        {
            var p = fe.TransformToAncestor(desk).Transform(new Point(0, 0));
            return new RECT(X + (int)((p.X - pad) * s), Y + (int)((p.Y - pad) * s),
                            X + (int)((p.X + fe.ActualWidth + pad) * s), Y + (int)((p.Y + fe.ActualHeight + pad) * s));
        }
        void Shot(string name, int extraRight = 0, int extraBottom = 0)
        {
            using var bmp = ScreenCapture.Capture(new RECT(X, Y, X + W + extraRight, Y + H + extraBottom));
            bmp.Save(Path.Combine(outDir, name), SD.Imaging.ImageFormat.Png);
        }

        Shot("0_page.png");

        // 1. Hold Ctrl and point at a word
        var start = target.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        var end = target.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
        var tp = target.Parent is TextBlock tb ? tb : (FrameworkElement)para1;
        var mid = tp.TransformToAncestor(desk).Transform(new Point((start.Left + end.Right) / 2, (start.Top + start.Bottom) / 2));
        _controller!.DemoWord(new POINT(X + (int)(mid.X * s), Y + (int)(mid.Y * s)));
        await Task.Delay(7000);
        Shot("1_word.png");
        _controller.DemoReset();
        await Task.Delay(600);

        // 2. Ctrl + left-drag: side panel
        _controller.Demo(Phys(para1, 10), false, ViewMode.Side);
        await Task.Delay(9000);
        Shot("2_side.png", extraRight: 580);
        _controller.DemoReset();
        await Task.Delay(600);

        // 3. Ctrl + left-drag: under each paragraph (对照)
        _controller.Demo(Phys(para2, 10), false, ViewMode.Under);
        await Task.Delay(9000);
        Shot("3_under.png", extraBottom: 260);
        _controller.DemoReset();
        await Task.Delay(600);

        // 4. Full-page Chinese in place (译文)
        _controller.Demo(Phys(article, 6), false, ViewMode.Replace);
        await Task.Delay(12000);
        Shot("4_replace.png");
        _controller.DemoReset();
        await Task.Delay(600);

        // 5. Ctrl + right-drag: copy text
        _controller.CopyRegion(Phys(para2, 8));
        await Task.Delay(1600);
        Shot("5_copy.png", extraBottom: 160);
        await Task.Delay(500);
        Shutdown();
    }

    static FrameworkElement BuildArticle(double w, double h, out Run target, out TextBlock para1, out TextBlock para2, out FrameworkElement article)
    {
        var ui = new FontFamily("Segoe UI Variable Text, Segoe UI");
        var serif = new FontFamily("Georgia");
        var ink = new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x28));
        var grey = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));

        var root = new DockPanel { Width = w, Height = h, Background = Brushes.White, LastChildFill = true };

        // browser chrome
        var chrome = new Border { Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF3, 0xF4)), Height = 46, BorderBrush = new SolidColorBrush(Color.FromRgb(0xDA, 0xDC, 0xE0)), BorderThickness = new Thickness(0, 0, 0, 1) };
        var bar = new DockPanel { Margin = new Thickness(14, 0, 14, 0) };
        var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in new[] { "#FF5F57", "#FEBC2E", "#28C840" })
            dots.Children.Add(new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, Margin = new Thickness(0, 0, 8, 0), Fill = (Brush)new BrushConverter().ConvertFromString(c)! });
        bar.Children.Add(dots);
        var url = new Border
        {
            Background = Brushes.White, CornerRadius = new CornerRadius(15), Height = 30, Margin = new Thickness(18, 0, 0, 0), Padding = new Thickness(16, 0, 16, 0),
            Child = new TextBlock { Text = "🔒  www.campus-science.org/health/why-sleep-matters", FontFamily = ui, FontSize = 13, Foreground = grey, VerticalAlignment = VerticalAlignment.Center },
        };
        bar.Children.Add(url);
        chrome.Child = bar;
        DockPanel.SetDock(chrome, Dock.Top);
        root.Children.Add(chrome);

        var col = new StackPanel { Width = 760, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(90, 34, 0, 0) };
        col.Children.Add(new TextBlock { Text = "HEALTH  ·  5 MIN READ", FontFamily = ui, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)), Margin = new Thickness(0, 0, 0, 10) });
        col.Children.Add(new TextBlock { Text = "Why Sleep Matters More Than Cramming", FontFamily = serif, FontSize = 34, FontWeight = FontWeights.Bold, Foreground = ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        col.Children.Add(new TextBlock { Text = "By Maria Lopez  ·  March 14, 2026", FontFamily = ui, FontSize = 13, Foreground = grey, Margin = new Thickness(0, 0, 0, 22) });

        TextBlock P(double bottom = 18) => new() { FontFamily = ui, FontSize = 17, LineHeight = 28, Foreground = ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, bottom) };
        para1 = P();
        para1.Inlines.Add(new Run("A recent study found that students who slept less than six hours a night scored 12% lower on exams. Researchers were "));
        target = new Run("reluctant");
        para1.Inlines.Add(target);
        para1.Inlines.Add(new Run(" to blame screens alone, but they agree that sleep is when the brain files away what you learned during the day."));
        col.Children.Add(para1);

        para2 = P();
        para2.Inlines.Add(new Run("However, many students still pull all-nighters before finals. Is it worth it? Probably not. Even one night without sleep can reduce memory recall by up to 40 percent."));
        col.Children.Add(para2);

        var p3 = P(10);
        p3.Inlines.Add(new Run("Simple habits help:") { FontWeight = FontWeights.SemiBold });
        col.Children.Add(p3);
        foreach (var t in new[] { "Go to bed at the same time every day", "Avoid caffeine after 3 p.m.", "Keep your phone out of the bedroom" })
            col.Children.Add(new TextBlock { Text = "•  " + t, FontFamily = ui, FontSize = 17, Foreground = ink, Margin = new Thickness(4, 0, 0, 8) });

        root.Children.Add(col);
        article = col;
        return root;
    }
}
