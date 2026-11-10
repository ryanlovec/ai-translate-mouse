using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AIMouse.Capture;
using AIMouse.Core;
using AIMouse.Native;
using AIMouse.Ocr;
using AIMouse.Text;
using AIMouse.Translate;
using AIMouse.UI;
using SD = System.Drawing;
using WF = System.Windows.Forms;

namespace AIMouse;

public partial class App : Application
{
    Mutex? _mutex;
    Controller? _controller;
    WF.NotifyIcon? _tray;
    Config _cfg = new();
    SettingsWindow? _settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) => { Log.Write("UI exception: " + ex.Exception); ex.Handled = true; };
        TaskScheduler.UnobservedTaskException += (_, ex) => { Log.Write("Task exception: " + ex.Exception); ex.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Write("Fatal: " + ex.ExceptionObject);

        var args = e.Args;
        if (args.Contains("--visible-to-capture")) WindowHelper.ExcludeFromCapture = false;
        if (args.Length > 0 && args[0].StartsWith("--selftest", StringComparison.Ordinal))
        {
            _ = RunSelfTestAsync(args);
            return;
        }

        _mutex = new Mutex(true, "AIMouse_SingleInstance", out bool created);
        if (!created)
        {
            Shutdown();
            return;
        }

        _cfg = Config.Load();
        bool mock = args.Contains("--mock") || _cfg.Mock;
        try
        {
            _controller = new Controller(_cfg, mock, Dispatcher);
        }
        catch (Exception ex)
        {
            Log.Write("Startup failed: " + ex);
            MessageBox.Show("启动失败：" + ex.Message, "AI翻译鼠标");
            Shutdown();
            return;
        }
        _controller.Start();
        SetupTray();

        int demoScroll = Array.IndexOf(args, "--demo-scroll");
        if (demoScroll >= 0 && args.Length >= demoScroll + 4)
        {
            _ = DemoScrollAsync(args[demoScroll + 1], args[demoScroll + 2], args[demoScroll + 3]);
            return;
        }
        int demoImg = Array.IndexOf(args, "--demo-image");
        if (demoImg >= 0 && args.Length >= demoImg + 4)
        {
            _ = DemoImageAsync(args[demoImg + 1], args[demoImg + 2], args[demoImg + 3]);
            return;
        }
        int demo = Array.IndexOf(args, "--demo");
        if (demo >= 0 && args.Length >= demo + 5)
        {
            var r = ParseRect(args, demo + 1);
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _controller.Demo(r, false));
        }
        else if (!mock && !_cfg.UseLocal && string.IsNullOrWhiteSpace(_cfg.GetApiKey()))
        {
            OpenSettings();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Stop();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.OnExit(e);
    }

    // ---------------- tray ----------------

    void SetupTray()
    {
        var menu = new WF.ContextMenuStrip
        {
            Font = new SD.Font("Microsoft YaHei UI", 9.5f),
            Renderer = new WF.ToolStripProfessionalRenderer(new MenuColors()) { RoundedEdges = false },
            Padding = new WF.Padding(2, 4, 2, 4),
        };
        var title = new WF.ToolStripMenuItem("AI翻译鼠标") { Enabled = false, Font = new SD.Font("Microsoft YaHei UI", 9.5f, SD.FontStyle.Bold) };
        menu.Items.Add(title);
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("使用方法 / 设置", null, (_, _) => OpenSettings());
        menu.Items.Add("打开生词本", null, (_, _) => OpenWordBook());
        var pause = new WF.ToolStripMenuItem("暂停") { CheckOnClick = true };
        pause.CheckedChanged += (_, _) =>
        {
            if (_controller != null) _controller.Paused = pause.Checked;
            if (_tray != null) _tray.Text = pause.Checked ? "AI翻译鼠标（已暂停）" : "AI翻译鼠标";
        };
        menu.Items.Add(pause);
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Shutdown());

        _tray = new WF.NotifyIcon
        {
            Icon = MakeIcon(),
            Text = "AI翻译鼠标",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => OpenSettings();
        _tray.ShowBalloonTip(5000, "AI翻译鼠标已启动", "按住 Ctrl：指单词查词 / 左键拖框翻译 / 右键拖框复制文字。连按两下 Ctrl 全屏翻译", WF.ToolTipIcon.Info);
    }

    void OpenSettings()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_settings is { IsLoaded: true })
            {
                _settings.Activate();
                return;
            }
            _settings = new SettingsWindow(_cfg);
            _settings.Closed += (_, _) => _settings = null;
            _settings.Show();
            _settings.Activate();
        });
    }

    static void OpenWordBook()
    {
        try
        {
            WordBook.EnsureFile();
            Process.Start(new ProcessStartInfo(WordBook.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Write("Open wordbook failed: " + ex.Message); }
    }

    static SD.Icon MakeIcon()
    {
        var res = GetResourceStream(new Uri("pack://application:,,,/Assets/logo.ico"));
        using var stream = res.Stream;
        int size = WF.SystemInformation.SmallIconSize.Width;
        return new SD.Icon(stream, size, size);
    }

    /// <summary>Light, flat tray menu colors matching the app.</summary>
    sealed class MenuColors : WF.ProfessionalColorTable
    {
        static readonly SD.Color Hover = SD.Color.FromArgb(0xEE, 0xF2, 0xFF);
        static readonly SD.Color Border = SD.Color.FromArgb(0xD5, 0xDC, 0xE8);
        public override SD.Color MenuItemSelected => Hover;
        public override SD.Color MenuItemSelectedGradientBegin => Hover;
        public override SD.Color MenuItemSelectedGradientEnd => Hover;
        public override SD.Color MenuItemBorder => Hover;
        public override SD.Color MenuBorder => Border;
        public override SD.Color ToolStripDropDownBackground => SD.Color.White;
        public override SD.Color ImageMarginGradientBegin => SD.Color.White;
        public override SD.Color ImageMarginGradientMiddle => SD.Color.White;
        public override SD.Color ImageMarginGradientEnd => SD.Color.White;
        public override SD.Color SeparatorDark => SD.Color.FromArgb(0xE3, 0xE8, 0xF0);
        public override SD.Color SeparatorLight => SD.Color.White;
        public override SD.Color CheckBackground => SD.Color.FromArgb(0xE0, 0xE7, 0xFF);
        public override SD.Color CheckSelectedBackground => SD.Color.FromArgb(0xE0, 0xE7, 0xFF);
        public override SD.Color CheckPressedBackground => SD.Color.FromArgb(0xE0, 0xE7, 0xFF);
    }

    // ---------------- self tests (for automated checks) ----------------

    /// <summary>Test: show an image on screen, translate it in the given view, photograph the result, quit.</summary>
    async Task DemoImageAsync(string imagePath, string view, string outPng)
    {
        var img = new System.Windows.Media.Imaging.BitmapImage(new Uri(Path.GetFullPath(imagePath)));
        var host = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowActivated = false, Topmost = true, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0,
            Content = new System.Windows.Controls.Image { Source = img, Stretch = Stretch.Fill },
        };
        if (Environment.GetEnvironmentVariable("AIMOUSE_DEMO_BLINK") == "1")
        {
            // Test: small blinking status dots, like live web dashboards have.
            double sc = WindowHelper.GetMonitor(new POINT(100, 100)).Scale;
            var grid = new System.Windows.Controls.Grid();
            grid.Children.Add(new System.Windows.Controls.Image { Source = img, Stretch = Stretch.Fill });
            var cv = new System.Windows.Controls.Canvas();
            var dots = new List<System.Windows.Shapes.Ellipse>();
            foreach (var (x, y, c) in new[] { (1600, 488, Colors.Red), (150, 480, Colors.MediumSeaGreen), (170, 480, Colors.MediumSeaGreen) })
            {
                var e = new System.Windows.Shapes.Ellipse { Width = 22 / sc, Height = 22 / sc, Fill = new SolidColorBrush(c) };
                System.Windows.Controls.Canvas.SetLeft(e, x / sc);
                System.Windows.Controls.Canvas.SetTop(e, y / sc);
                cv.Children.Add(e);
                dots.Add(e);
            }
            grid.Children.Add(cv);
            host.Content = grid;
            int tick = 0;
            new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Normal, (_, _) =>
            {
                tick++;
                for (int i = 0; i < dots.Count; i++) dots[i].Opacity = ((tick + i) % 3) / 2.0;
            }, Dispatcher).Start();
        }
        host.SourceInitialized += (_, _) =>
        {
            var h = new System.Windows.Interop.WindowInteropHelper(host).Handle;
            Win32.SetWindowPos(h, Win32.HWND_TOPMOST, 100, 100, img.PixelWidth, img.PixelHeight, Win32.SWP_NOACTIVATE);
        };
        host.Show();
        await Task.Delay(800);
        var r = new RECT(100, 100, 100 + img.PixelWidth, 100 + img.PixelHeight);
        var v = view switch { "under" => ViewMode.Under, "replace" => ViewMode.Replace, _ => ViewMode.Side };
        _controller!.Demo(r, view.StartsWith("full"), view.StartsWith("full") ? ViewMode.Replace : v);
        await Task.Delay(int.TryParse(Environment.GetEnvironmentVariable("AIMOUSE_DEMO_WAIT"), out var w) ? w : 9000);
        using (var bmp = ScreenCapture.Capture(new RECT(60, 20, 140 + img.PixelWidth + 420, 140 + img.PixelHeight))) bmp.Save(outPng, SD.Imaging.ImageFormat.Png);
        Shutdown();
    }

    /// <summary>Test: 译文 mode on a fake "page" that scrolls, switches to another page, and comes back. Screenshots each step.</summary>
    async Task DemoScrollAsync(string pageA, string pageB, string outDir)
    {
        const int X = 60, Y = 80, W = 1600, H = 620, Side = 240, Bar = 70;
        var imgA = new System.Windows.Media.Imaging.BitmapImage(new Uri(Path.GetFullPath(pageA)));
        var imgB = new System.Windows.Media.Imaging.BitmapImage(new Uri(Path.GetFullPath(pageB)));
        double s = WindowHelper.GetMonitor(new POINT(X, Y)).Scale;
        var pic = new System.Windows.Controls.Image { Source = imgA, Stretch = Stretch.Fill, Width = imgA.PixelWidth / s, Height = imgA.PixelHeight / s };
        var canvas = new System.Windows.Controls.Canvas { Background = Brushes.White, ClipToBounds = true };
        canvas.Children.Add(pic);
        System.Windows.Controls.Canvas.SetLeft(pic, Side / s);
        // A fixed sidebar and toolbar that never scroll, like a real reader app.
        var side = new System.Windows.Controls.StackPanel { Width = Side / s, Height = H / s, Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF2, 0xF5)) };
        foreach (var t in new[] { "Library", "Recent papers", "Highlights", "Notes", "Settings and privacy" })
            side.Children.Add(new System.Windows.Controls.TextBlock { Text = t, FontSize = 15, Margin = new Thickness(16, 14, 0, 0) });
        var bar = new System.Windows.Controls.TextBlock { Text = "  File   Edit   View   Go   Window   Help        Page 3 of 12", FontSize = 15, Padding = new Thickness(8, 14, 0, 0),
            Width = W / s, Height = Bar / s, Background = new SolidColorBrush(Color.FromRgb(0xE4, 0xE7, 0xEC)) };
        canvas.Children.Add(side);
        canvas.Children.Add(bar);
        var host = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowActivated = false, Topmost = true, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0, Content = canvas,
        };
        host.SourceInitialized += (_, _) =>
            Win32.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(host).Handle, Win32.HWND_TOPMOST, X, Y, W, H, Win32.SWP_NOACTIVATE);
        host.Show();
        var region = new RECT(X, Y, X + W, Y + H);
        void Shot(string name)
        {
            // The Chinese layer is invisible to screen capture (on purpose), so paint it onto the screenshot ourselves.
            using var bmp = ScreenCapture.Capture(region);
            var ov = _controller!.CurrentSession?.Overlay;
            if (ov is { IsVisible: true, Content: FrameworkElement fe })
            {
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(region.Width, region.Height, 96 * s, 96 * s, PixelFormats.Pbgra32);
                rtb.Render(fe);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using var ms = new MemoryStream();
                enc.Save(ms);
                ms.Position = 0;
                using var layer = new SD.Bitmap(ms);
                using var g = SD.Graphics.FromImage(bmp);
                g.DrawImage(layer, 0, 0, region.Width, region.Height);
            }
            bmp.Save(Path.Combine(outDir, name), SD.Imaging.ImageFormat.Png);
        }
        async Task ScrollTo(double py)
        {
            double from = System.Windows.Controls.Canvas.GetTop(pic);
            if (double.IsNaN(from)) from = 0;
            for (int i = 1; i <= 8; i++)
            {
                System.Windows.Controls.Canvas.SetTop(pic, from + ((Bar - py) / s - from) * i / 8);
                await Task.Delay(40);
            }
        }
        System.Windows.Controls.Canvas.SetTop(pic, Bar / s);
        await Task.Delay(800);
        _controller!.Demo(region, false, ViewMode.Replace, live: true);
        await Task.Delay(9000);
        Shot("scroll_1_start.png");
        Log.Write("[test] wheel scrolls start");
        foreach (var py in new[] { 100, 200, 300 })     // three wheel flicks, pausing between them like a reader
        {
            await ScrollTo(py);
            await Task.Delay(300);
            Shot($"scroll_2_moving_{py}.png");
            await Task.Delay(2500);
        }
        Log.Write("[test] wheel scrolls done");
        await Task.Delay(3000);
        Shot("scroll_3_settled.png");
        pic.Source = imgB; pic.Width = imgB.PixelWidth / s; pic.Height = imgB.PixelHeight / s;
        System.Windows.Controls.Canvas.SetTop(pic, Bar / s);
        await Task.Delay(8000);
        Shot("scroll_4_otherpage.png");
        pic.Source = imgA; pic.Width = imgA.PixelWidth / s; pic.Height = imgA.PixelHeight / s;
        System.Windows.Controls.Canvas.SetTop(pic, (Bar - 300) / s);
        await Task.Delay(500);
        Shot("scroll_5_back.png");              // back to page A: restored from memory almost at once
        Shutdown();
    }

    static RECT ParseRect(string[] a, int i)
    {
        int x = int.Parse(a[i]), y = int.Parse(a[i + 1]), w = int.Parse(a[i + 2]), h = int.Parse(a[i + 3]);
        return new RECT(x, y, x + w, y + h);
    }

    async Task RunSelfTestAsync(string[] args)
    {
        var outPath = Path.Combine(Path.GetTempPath(), "aimouse_selftest.txt");
        var sb = new StringBuilder();
        try
        {
            switch (args[0])
            {
                case "--selftest-ocr":
                {
                    var r = ParseRect(args, 1);
                    var page = await new OcrService().RecognizeScreenAsync(r, OcrService.UpscaleFor(r));
                    Dump(page, sb);
                    break;
                }
                case "--selftest-copy":
                {
                    var svc = new OcrService();
                    sb.AppendLine($"CHINESE_OCR {svc.HasChinese}");
                    sb.AppendLine(await svc.RecognizeTextAsync(ParseRect(args, 1)));
                    break;
                }
                case "--selftest-copy-image":
                {
                    using var bmp = new SD.Bitmap(args[1]);
                    double up = OcrService.UpscaleFor(new RECT(0, 0, bmp.Width, bmp.Height));
                    if (args.Length > 2) OcrService.ZhUpscaleOverride = double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
                    var svc = new OcrService();
                    sb.AppendLine($"CHINESE_OCR {svc.HasChinese}");
                    sb.AppendLine(await svc.RecognizeTextAsync(bmp, 0, 0, up));
                    break;
                }
                case "--selftest-render":
                {
                    // Draw each window to a PNG (no window ever appears on screen or takes focus).
                    var dir = args[1];
                    var cfg = new Config();
                    var st = new SettingsWindow(cfg);
                    await RenderAsync(st, Path.Combine(dir, "render_settings.png"), keepOpen: true);
                    st.LocalRadio.IsChecked = true;
                    await RenderAsync(st, Path.Combine(dir, "render_settings_local.png"));

                    var panel = new PanelWindow();
                    panel.SetMode("框选翻译", false, true);
                    panel.SetStatus("完成 · 3 句");
                    var items = new[]
                    {
                        new SentenceItem { En = "Rising ocean temperatures are forcing many marine species to migrate toward the poles.", Zh = "海洋温度上升正迫使许多海洋物种向两极迁移。" },
                        new SentenceItem { En = "Scientists are reluctant to predict exactly how fast this will happen, but the trend is clear.", Zh = "科学家们不愿准确预测这会以多快的速度发生，但趋势是明确的。", IsHighlighted = true },
                        new SentenceItem { En = "Is this the new normal?", Zh = "" },
                    };
                    panel.SetItems(items);
                    panel.Width = 380;
                    panel.Height = 420;
                    await RenderAsync(panel, Path.Combine(dir, "render_panel.png"));

                    var card = new WordCard();
                    card.ShowLoading("reluctant", "Scientists are reluctant to predict exactly how fast this will happen.");
                    card.ShowInfo(new WordInfo
                    {
                        Word = "reluctant", Phonetic = "/rɪˈlʌktənt/", Pos = "adj.", Meaning = "不愿意的，勉强的",
                        Note = "be reluctant to do sth 不愿做某事", SentenceZh = "科学家们不愿准确预测这种情况会以多快的速度发生。",
                    });
                    await RenderAsync(card, Path.Combine(dir, "render_card.png"));

                    OfflineDict.LoadInBackground();
                    for (int i = 0; i < 50 && !OfflineDict.Available; i++) await Task.Delay(100);
                    var dcard = new WordCard();
                    dcard.ShowLoading("vulnerable", "Coral reefs are especially vulnerable.");
                    if (OfflineDict.Lookup("vulnerable") is { } dinfo) dcard.ShowDict(dinfo, aiPending: true);
                    await RenderAsync(dcard, Path.Combine(dir, "render_card_dict.png"));
                    sb.AppendLine("rendered");
                    break;
                }
                case "--selftest-settings-shot":
                {
                    // Show the real settings window (no focus) for a moment and photograph it, title bar included.
                    var st = new SettingsWindow(new Config()) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = 60, Top = 60 };
                    st.Show();
                    await Task.Delay(1200);
                    var hwnd = new System.Windows.Interop.WindowInteropHelper(st).Handle;
                    Win32.GetWindowRect(hwnd, out var wr);
                    var r = wr.Inflate(24);
                    using (var bmp = ScreenCapture.Capture(r)) bmp.Save(args[1], SD.Imaging.ImageFormat.Png);
                    st.Close();
                    sb.AppendLine($"window {wr}");
                    break;
                }
                case "--selftest-shift":
                {
                    // Same page, second copy scrolled by dy, with a fixed toolbar on top and sidebar on the left.
                    using var src = new SD.Bitmap(args[1]);
                    int dy = int.Parse(args[2]);
                    SD.Bitmap Page(int off)
                    {
                        var b = new SD.Bitmap(1600, 620);
                        using var g = SD.Graphics.FromImage(b);
                        g.Clear(SD.Color.White);
                        g.DrawImage(src, new SD.Rectangle(240, 70 - off, src.Width, src.Height), new SD.Rectangle(0, 0, src.Width, src.Height), SD.GraphicsUnit.Pixel);
                        g.FillRectangle(new SD.SolidBrush(SD.Color.FromArgb(0xF0, 0xF2, 0xF5)), 0, 0, 240, 620);
                        g.FillRectangle(new SD.SolidBrush(SD.Color.FromArgb(0xE4, 0xE7, 0xEC)), 0, 0, 1600, 70);
                        using var f = new SD.Font("Segoe UI", 15);
                        g.DrawString("File  Edit  View   Page 3 of 12", f, SD.Brushes.Black, 10, 20);
                        g.DrawString("Library\nRecent papers\nNotes", f, SD.Brushes.Black, 10, 100);
                        return b;
                    }
                    using var a = Page(0);
                    using var c = Page(dy);
                    sb.Append(ScrollTracker.Explain(ScrollTracker.Profile(a), ScrollTracker.Profile(c), 465));
                    break;
                }
                case "--selftest-wordhit":
                {
                    // Where would a click on each word land? (no real mouse involved)
                    var panel = new PanelWindow();
                    panel.SetItems(new[] { new SentenceItem { En = "Scientists are \"reluctant\" to predict exactly how fast this will happen, but the trend is clear.", Zh = "x" } });
                    panel.Width = 380; panel.Height = 300;
                    panel.ShowActivated = false; panel.Left = -20000; panel.Top = -20000; panel.Show();
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    var card = new WordCard();
                    card.ShowPhrase("Is this the new normal?");
                    card.ShowActivated = false; card.Left = -20000; card.Top = -20000; card.Show();
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    var boxes = FindAll<System.Windows.Controls.TextBox>(panel).Concat(FindAll<System.Windows.Controls.TextBox>(card)).ToList();
                    foreach (var tb in boxes)
                    {
                        foreach (var probe in new[] { "Scientists", "reluctant", "predict", "happen", "clear", "this", "normal" })
                        {
                            int i = tb.Text.IndexOf(probe, StringComparison.Ordinal);
                            if (i < 0) continue;
                            var r = tb.GetRectFromCharacterIndex(i + probe.Length / 2);
                            var hit = TextHit.WordAt(tb, new Point(r.X + 1, r.Y + r.Height / 2));
                            sb.AppendLine($"{probe,-12} → {(hit == null ? "(none)" : tb.Text.Substring(hit.Value.Start, hit.Value.Length))}");
                        }
                        var gap = tb.GetRectFromCharacterIndex(tb.Text.IndexOf(' ') );
                        sb.AppendLine($"space        → {(TextHit.WordAt(tb, new Point(gap.X + 1, gap.Y + gap.Height / 2)) == null ? "(none)" : "WRONG")}");
                    }
                    panel.Close(); card.Close();
                    break;
                }
                case "--selftest-dict":
                {
                    OfflineDict.LoadInBackground();
                    for (int i = 0; i < 50 && !OfflineDict.Available; i++) await Task.Delay(100);
                    var sw = Stopwatch.StartNew();
                    foreach (var w in args.Skip(1))
                    {
                        var d = OfflineDict.Lookup(w);
                        sb.AppendLine(d == null ? $"{w}: (none)" : $"{w}: {d.Word} {d.Phonetic} {d.Pos} {d.Meaning} | {d.Note}");
                    }
                    sb.AppendLine($"{args.Length - 1} lookups in {sw.ElapsedMilliseconds} ms");
                    break;
                }
                case "--selftest-diag":
                {
                    // Which request shape does the server accept right now? (key stays inside the app)
                    var cfg = Config.Load();
                    using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(40) };
                    foreach (var (model, thinking, stream) in new[] { (cfg.Model, true, false), (cfg.Model, false, false), (cfg.Model, true, true), ("deepseek-chat", false, false) })
                    {
                        var body = new System.Text.Json.Nodes.JsonObject
                        {
                            ["model"] = model, ["stream"] = stream, ["max_tokens"] = 20,
                            ["messages"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["role"] = "user", ["content"] = "Say hi" }),
                        };
                        if (thinking) body["thinking"] = new System.Text.Json.Nodes.JsonObject { ["type"] = "disabled" };
                        using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, cfg.BaseUrl.TrimEnd('/') + "/chat/completions");
                        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cfg.GetApiKey().Trim());
                        req.Content = new System.Net.Http.StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                        var sw = Stopwatch.StartNew();
                        try
                        {
                            using var resp = await http.SendAsync(req);
                            var txt = await resp.Content.ReadAsStringAsync();
                            sb.AppendLine($"{model} thinking={thinking} stream={stream}: {(int)resp.StatusCode} {sw.ElapsedMilliseconds}ms {txt[..Math.Min(120, txt.Length)].Replace('\n', ' ')}");
                        }
                        catch (Exception ex) { sb.AppendLine($"{model}: EXC {ex.Message}"); }
                    }
                    break;
                }
                case "--selftest-pipeline":
                {
                    using var bmp = new SD.Bitmap(args[1]);
                    double up = OcrService.UpscaleFor(new RECT(0, 0, bmp.Width, bmp.Height));
                    var page = await new OcrService().RecognizeBitmapAsync(bmp, 0, 0, up);
                    var sents = Segmenter.Segment(page);
                    var en = sents.Select(x => x.En).ToList();
                    var zh = new string[en.Count];
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    ITranslator t = new DeepSeekTranslator(Config.Load());
                    await t.TranslateAsync(en, (i, z) => zh[i] = z, CancellationToken.None);
                    sb.AppendLine($"{en.Count} sentences, {sw.ElapsedMilliseconds} ms");
                    for (int i = 0; i < en.Count; i++) sb.AppendLine($"#{i + 1} {en[i]}").AppendLine($"   → {zh[i]}");
                    break;
                }
                case "--selftest-image":
                {
                    using var bmp = new SD.Bitmap(args[1]);
                    double up = OcrService.UpscaleFor(new RECT(0, 0, bmp.Width, bmp.Height));
                    var page = await new OcrService().RecognizeBitmapAsync(bmp, 0, 0, up);
                    Dump(page, sb);
                    break;
                }
                case "--selftest-translate":
                {
                    var cfg = Config.Load();
                    if (args.Contains("--local")) { cfg.Provider = "local"; cfg.LocalModel = "qwen3:4b"; }
                    ITranslator t = args.Contains("--mock") ? new MockTranslator() : new DeepSeekTranslator(cfg);
                    var segs = new[] { args[1] };
                    string zh = "";
                    var sw = Stopwatch.StartNew();
                    await t.TranslateAsync(segs, (_, z) => zh = z, CancellationToken.None);
                    sb.AppendLine($"translate ({sw.ElapsedMilliseconds} ms): {zh}");
                    sw.Restart();
                    var info = await t.LookupAsync("reluctant", "Scientists are reluctant to predict exactly how fast this will happen.", CancellationToken.None);
                    sb.AppendLine($"lookup ({sw.ElapsedMilliseconds} ms): {info.Word} {info.Phonetic} {info.Pos} {info.Meaning} | {info.Note} | {info.SentenceZh}");
                    break;
                }
                case "--selftest-affinity":
                {
                    var r = new RECT(100, 100, 300, 200);
                    var win = new RectWindow(Colors.Red, 0, Colors.Red, radius: 0);
                    win.ShowAt(r);
                    await Task.Delay(500);
                    using var bmp = ScreenCapture.Capture(r);
                    var c = bmp.GetPixel(100, 50);
                    win.Close();
                    bool red = c.R > 200 && c.G < 60 && c.B < 60;
                    sb.AppendLine($"center pixel = {c}; excluded_from_capture = {!red}");
                    break;
                }
            }
        }
        catch (Exception ex) { sb.AppendLine("ERROR: " + ex); }
        await File.WriteAllTextAsync(outPath, sb.ToString(), new UTF8Encoding(false));
        Shutdown();
    }

    static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) yield return t;
            foreach (var d in FindAll<T>(c)) yield return d;
        }
    }

    static async Task RenderAsync(Window w, string path, bool keepOpen = false)
    {
        if (!w.IsVisible)
        {
            w.ShowActivated = false;
            w.ShowInTaskbar = false;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = -20000;
            w.Top = -20000;
            w.Show();
        }
        await w.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        w.UpdateLayout();
        var el = (FrameworkElement)w.Content;
        const double scale = 1.5;
        var size = new Rect(0, 0, el.ActualWidth, el.ActualHeight);
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var bg = w.Background is SolidColorBrush { Color.A: > 0 } b ? b : new SolidColorBrush(Color.FromRgb(0xE4, 0xE8, 0xEF));
            dc.DrawRectangle(bg, null, size);
            dc.DrawRectangle(new VisualBrush(el), null, size);
        }
        rtb.Render(dv);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using (var fs = File.Create(path)) enc.Save(fs);
        if (!keepOpen) w.Close();
    }

    static void Dump(OcrPage page, StringBuilder sb)
    {
        sb.AppendLine($"LINES {page.Lines.Count}");
        foreach (var l in page.Lines) sb.AppendLine($"  {l.Bounds} {l.Text}");
        var sents = Segmenter.Segment(page);
        sb.AppendLine($"SENTENCES {sents.Count}");
        for (int i = 0; i < sents.Count; i++)
            sb.AppendLine($"  #{i + 1} {sents[i].En}\n      frags: {string.Join(" ", sents[i].Fragments)}");
    }
}
