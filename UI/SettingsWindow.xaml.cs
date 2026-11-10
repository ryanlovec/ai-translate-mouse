using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIMouse.Core;
using AIMouse.Translate;

namespace AIMouse.UI;

public partial class SettingsWindow : Window
{
    readonly Config _cfg;

    public SettingsWindow(Config cfg)
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 16; // never taller than the screen; the body scrolls instead
        _cfg = cfg;
        ModelBox.Text = cfg.Model;
        LocalUrlBox.Text = LocalModels.NormalizeUrl(cfg.LocalUrl);
        LocalModelBox.Text = cfg.LocalModel;
        AutoStartBox.IsChecked = cfg.StartWithWindows;
        AutoSpeakBox.IsChecked = cfg.AutoSpeakWord;
        if (!Speech.Available) AutoSpeakBox.Visibility = Visibility.Collapsed;
        VoiceBox.IsChecked = cfg.VoiceInput;
        _voiceKey = AIMouse.Input.HotKey.Parse(cfg.VoiceKey);
        ShowVoiceKey();
        Closed += (_, _) => { if (_capturing) Controller.Instance?.CancelKeyCapture(); };
        AIMouse.Voice.ModelDownloader.Progress += OnModelProgress;
        AIMouse.Voice.ModelDownloader.Finished += OnModelFinished;
        Closed += (_, _) =>
        {
            AIMouse.Voice.ModelDownloader.Progress -= OnModelProgress;
            AIMouse.Voice.ModelDownloader.Finished -= OnModelFinished;
        };
        ShowModelState();
        if (cfg.HasSavedKey) KeyStateRun.Text = "密钥已保存（不显示），留空即继续使用。";
        if (cfg.UseLocal) LocalRadio.IsChecked = true;
        else CloudRadio.IsChecked = true;
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        int round = 2; // DWMWCP_ROUND: Windows 11 rounded corners
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    bool LocalSelected => LocalRadio.IsChecked == true;

    string EffectiveKey() => KeyBox.Password.Trim().Length > 0 ? KeyBox.Password.Trim() : _cfg.GetApiKey();

    void Provider_Changed(object sender, RoutedEventArgs e)
    {
        if (CloudPanel == null || LocalPanel == null) return;
        CloudPanel.Visibility = LocalSelected ? Visibility.Collapsed : Visibility.Visible;
        LocalPanel.Visibility = LocalSelected ? Visibility.Visible : Visibility.Collapsed;
        ResultText.Text = "";
        if (LocalSelected && LocalChips.Children.Count == 0) Detect_Click(this, e);
    }

    void ModelChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string m }) ModelBox.Text = m;
    }

    async void Detect_Click(object sender, RoutedEventArgs e)
    {
        DetectBtn.IsEnabled = false;
        LocalStatus.Text = "正在查找电脑上的本地模型…";
        LocalChips.Children.Clear();
        try
        {
            var servers = await LocalModels.DetectAsync(LocalUrlBox.Text);
            var withModels = servers.Where(s => s.Models.Count > 0).ToList();
            if (servers.Count == 0)
            {
                LocalStatus.Text = "没找到正在运行的本地模型软件。装好 Ollama 或 LM Studio 并打开后，再点“自动查找”。";
                return;
            }
            if (withModels.Count == 0)
            {
                LocalStatus.Text = $"找到了 {string.Join("、", servers.Select(s => s.Name))}，但里面还没有模型，请先下载一个（推荐 Qwen）。";
                return;
            }
            foreach (var s in withModels)
                foreach (var m in s.Models)
                {
                    var chip = new Button { Style = (Style)FindResource("ChipBtn"), Content = $"{m} · {s.Name}", Tag = (s.Url, m) };
                    chip.Click += (_, _) =>
                    {
                        LocalUrlBox.Text = s.Url;
                        LocalModelBox.Text = m;
                    };
                    LocalChips.Children.Add(chip);
                }
            LocalStatus.Text = "点一下上面的模型就能选中。";
            if (string.IsNullOrWhiteSpace(LocalModelBox.Text))
            {
                LocalUrlBox.Text = withModels[0].Url;
                LocalModelBox.Text = withModels[0].Models[0];
            }
        }
        finally { DetectBtn.IsEnabled = true; }
    }

    /// <summary>Copy what is on screen into a config (without saving).</summary>
    void ApplyTo(Config c)
    {
        c.Provider = LocalSelected ? "local" : "deepseek";
        c.Model = string.IsNullOrWhiteSpace(ModelBox.Text) ? "deepseek-flash" : ModelBox.Text.Trim();
        c.LocalUrl = LocalModels.NormalizeUrl(LocalUrlBox.Text);
        c.LocalModel = LocalModelBox.Text.Trim();
    }

    async void Test_Click(object sender, RoutedEventArgs e)
    {
        TestBtn.IsEnabled = false;
        Show(LocalSelected ? "正在连接本地模型（第一次可能要等十几秒）…" : "正在连接 DeepSeek…", false);
        var backup = (_cfg.Provider, _cfg.Model, _cfg.LocalUrl, _cfg.LocalModel);
        try
        {
            ApplyTo(_cfg);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(LocalSelected ? 90 : 30));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var zh = await new DeepSeekTranslator(_cfg, LocalSelected ? null : EffectiveKey()).TestAsync(cts.Token);
            Show($"连接成功 ✓ {sw.Elapsed.TotalSeconds:0.0} 秒  “Hello, nice to meet you.” → {zh}", false, true);
        }
        catch (TranslateException ex) { Show(ex.Message, true); }
        catch (OperationCanceledException) { Show("等太久了没有回应，请检查后再试", true); }
        catch (Exception ex) { Show("连接失败：" + ex.Message, true); }
        finally
        {
            (_cfg.Provider, _cfg.Model, _cfg.LocalUrl, _cfg.LocalModel) = backup;
            TestBtn.IsEnabled = true;
        }
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (LocalSelected && string.IsNullOrWhiteSpace(LocalModelBox.Text))
        {
            Show("请先选一个本地模型（点“自动查找”）", true);
            return;
        }
        if (KeyBox.Password.Trim().Length > 0) _cfg.SetApiKey(KeyBox.Password);
        ApplyTo(_cfg);
        _cfg.AutoSpeakWord = AutoSpeakBox.IsChecked == true;
        _cfg.VoiceInput = VoiceBox.IsChecked == true;
        _cfg.VoiceKey = _voiceKey.ToString();
        bool auto = AutoStartBox.IsChecked == true;
        if (auto != _cfg.StartWithWindows) Config.ApplyStartWithWindows(auto);
        _cfg.StartWithWindows = auto;
        try
        {
            _cfg.Save();
            Close();
        }
        catch (Exception ex) { Show("保存失败：" + ex.Message, true); }
    }

    // ---------- talk key ----------

    AIMouse.Input.HotKey _voiceKey = AIMouse.Input.HotKey.Parse(null);
    bool _capturing;

    void ShowVoiceKey(string? note = null, bool warn = false)
    {
        Fill(VoiceKeyCaps, _voiceKey);
        Fill(TalkGestureKeys, _voiceKey);
        VoiceKeyStatus.Text = note ?? AIMouse.Input.HotKey.Caution(_voiceKey) ?? "按住说话，松开结束";
        VoiceKeyStatus.Foreground = warn || AIMouse.Input.HotKey.Caution(_voiceKey) != null && note == null
            ? new SolidColorBrush(Color.FromRgb(0xC9, 0x3C, 0x20)) : (Brush)FindResource("Secondary");
    }

    void Fill(StackPanel panel, AIMouse.Input.HotKey hk)
    {
        panel.Children.Clear();
        foreach (var t in hk.DisplayTokens)
            panel.Children.Add(new Border { Style = (Style)FindResource("KeyCap"), Child = new TextBlock { Style = (Style)FindResource("KeyText"), Text = t } });
    }

    void VoiceKey_Click(object sender, RoutedEventArgs e)
    {
        var c = Controller.Instance;
        if (c == null) return;
        if (_capturing)
        {
            c.CancelKeyCapture();
            _capturing = false;
            VoiceKeyBtn.Content = "更改";
            ShowVoiceKey();
            return;
        }
        _capturing = true;
        VoiceKeyBtn.Content = "取消";
        VoiceKeyCaps.Children.Clear();
        VoiceKeyStatus.Text = "现在按下想用的按键或组合键（如 Ctrl + Win、F8），Esc 取消";
        VoiceKeyStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xFF));
        c.BeginKeyCapture(hk =>
        {
            _capturing = false;
            VoiceKeyBtn.Content = "更改";
            if (hk == null) { ShowVoiceKey(); return; }
            var problem = AIMouse.Input.HotKey.Problem(hk);
            if (problem != null) { ShowVoiceKey($"{hk.Display}：{problem}", warn: true); return; }
            _voiceKey = hk;
            ShowVoiceKey(AIMouse.Input.HotKey.Caution(hk) ?? "已设置，点“完成”保存");
        });
    }

    void VoiceKeyReset_Click(object sender, RoutedEventArgs e)
    {
        if (_capturing) { Controller.Instance?.CancelKeyCapture(); _capturing = false; VoiceKeyBtn.Content = "更改"; }
        _voiceKey = AIMouse.Input.HotKey.Parse(AIMouse.Input.HotKey.Default);
        ShowVoiceKey("已恢复默认 Ctrl + Win");
    }

    // ---------- offline speech model ----------

    void ShowModelState(string? error = null)
    {
        if (AIMouse.Voice.ModelDownloader.Running)
        {
            VoiceModelBtn.IsEnabled = false;
            VoiceModelBtn.Content = "下载中";
            if (VoiceModelStatus.Text.Length == 0) VoiceModelStatus.Text = "正在下载…";
        }
        else if (AIMouse.Voice.SpeechToText.Installed)
        {
            VoiceModelBtn.IsEnabled = true;
            VoiceModelBtn.Content = "删除";
            VoiceModelStatus.Text = "已安装 ✓ · 约 230MB，可离线使用";
        }
        else
        {
            VoiceModelBtn.IsEnabled = true;
            VoiceModelBtn.Content = "下载";
            VoiceModelStatus.Text = error ?? "未安装 · 约 230MB，下载一次即可";
        }
    }

    void VoiceModel_Click(object sender, RoutedEventArgs e)
    {
        if (AIMouse.Voice.SpeechToText.Installed)
        {
            if (MessageBox.Show(this, "删除离线语音模型？删除后语音输入不能用，需要时可再下载。", "AI翻译鼠标", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
            AIMouse.Voice.ModelDownloader.Delete();
        }
        else
        {
            VoiceModelStatus.Text = "正在连接…";
            AIMouse.Voice.ModelDownloader.Start();
        }
        ShowModelState();
    }

    void OnModelProgress(long done, long total) => Dispatcher.BeginInvoke(() =>
    {
        VoiceModelStatus.Text = total > 0
            ? $"正在下载 {done * 100 / total}% · {done / 1048576} / {total / 1048576} MB"
            : $"正在下载 {done / 1048576} MB";
    });

    void OnModelFinished(string? error) => Dispatcher.BeginInvoke(() => ShowModelState(error));

    void Show(string text, bool error, bool ok = false)
    {
        ResultText.Text = text;
        ResultText.Foreground = new SolidColorBrush(error ? Color.FromRgb(0xDC, 0x26, 0x26)
            : ok ? Color.FromRgb(0x16, 0xA3, 0x4A) : Color.FromRgb(0x6B, 0x72, 0x80));
    }
}
