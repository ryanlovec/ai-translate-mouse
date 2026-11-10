using System.Windows.Media;
using System.Windows.Threading;
using AIMouse.Input;
using AIMouse.Native;
using AIMouse.Ocr;
using AIMouse.Text;
using AIMouse.Translate;
using AIMouse.UI;

namespace AIMouse.Core;

/// <summary>Wires gestures to OCR, translation and the on-screen windows.</summary>
public sealed class Controller
{
    static readonly Color Accent = Color.FromRgb(0x00, 0x7A, 0xFF);
    static readonly Color Green = Color.FromRgb(0x34, 0xC7, 0x59);

    public Dispatcher Dispatcher { get; }
    public OcrService Ocr { get; }
    public ITranslator Translator { get; }
    public TranslationCache Cache { get; } = new();
    public Config Config { get; }

    readonly InputHook _hook;
    readonly BadgeWindow _badge = new();
    readonly WordCard _card = new();
    readonly RectWindow _selection = new(Accent, 2, Color.FromArgb(0x1A, 0x00, 0x7A, 0xFF));
    readonly RectWindow _copySelection = new(Green, 2, Color.FromArgb(0x1A, 0x34, 0xC7, 0x59));
    readonly RectWindow _underline = new(Accent, 0, Accent, radius: 1);
    readonly CopyToast _toast = new();
    readonly Dictionary<string, WordInfo> _lookupCache = new();

    RegionSession? _session;
    CancellationTokenSource? _lookupCts, _phraseCts;
    string? _cardKey;
    bool _armed, _cardLinger, _dragCopy, _dragActive;
    int _hoverVersion, _copyVersion;

    public Controller(Config cfg, bool mock, Dispatcher dispatcher)
    {
        Config = cfg;
        Dispatcher = dispatcher;
        Ocr = new OcrService();
        Translator = mock ? new MockTranslator() : new DeepSeekTranslator(cfg);
        OfflineDict.LoadInBackground();
        _hook = new InputHook(dispatcher) { HoldDelayMs = Math.Clamp(cfg.HoldDelayMs, 80, 1000) };
        _hook.ArmedChanged += OnArmedChanged;
        _hook.HoverStill += OnHoverStill;
        _hook.MouseMoved += OnMouseMoved;
        _hook.DragStarted += (p, copy) =>
        {
            HideCard();
            _dragCopy = copy;
            _dragActive = true;
            if (copy) HideCopy();
            DragRect.ShowAt(new RECT(p.X, p.Y, p.X + 1, p.Y + 1));
        };
        _hook.DragMoved += (a, b) => DragRect.ShowAt(RECT.FromPoints(a, b));
        _hook.DragEnded += OnDragEnded;
        _hook.DragCancelled += () => { _dragActive = false; _selection.Hide(); _copySelection.Hide(); };
        _hook.DoubleTapCtrl += OnDoubleTap;
        _hook.Escape += () =>
        {
            HideCard();
            _selection.Hide();
            HideCopy();
            // Esc leaves the immersive modes (译文 / 对照 / full screen) at any time.
            if (_session is { } ss && (ss.View != ViewMode.Side || ss.Mode == SessionMode.FullScreen)) ss.Close();
        };
        _hook.TalkEnabled = () => Config.VoiceInput;
        _hook.TalkKey = HotKey.Parse(cfg.VoiceKey);
        Instance = this;
        _hook.TalkDown += OnTalkDown;
        _hook.TalkUp += OnTalkUp;
        _toast.AutoHidden += () => { if (!(_dragActive && _dragCopy)) _copySelection.Hide(); };
    }

    public void Start() => _hook.Start();

    public void Stop()
    {
        _hook.Stop();
        _session?.Close();
    }

    public bool Paused
    {
        get => _hook.Paused;
        set
        {
            _hook.Paused = value;
            if (value)
            {
                HideCard();
                _badge.Hide();
                _selection.Hide();
                HideCopy();
            }
        }
    }

    RectWindow DragRect => _dragCopy ? _copySelection : _selection;

    public void OnUi(Action a)
    {
        if (Dispatcher.CheckAccess()) a();
        else Dispatcher.BeginInvoke(a);
    }

    // ---------------- gestures ----------------

    void OnArmedChanged(bool armed, POINT p)
    {
        _armed = armed;
        if (armed)
        {
            _cardLinger = false;
            _badge.FollowCursor(p);
        }
        else
        {
            _badge.Hide();
            if (_card.IsVisible) _cardLinger = true;
        }
    }

    void OnMouseMoved(POINT p)
    {
        if (_armed && _badge.IsVisible) _badge.FollowCursor(p);
        if (_card.IsVisible && _cardLinger)
        {
            var s = WindowHelper.GetMonitor(p).Scale;
            if (!WindowHelper.GetPhysicalRect(_card).Inflate((int)(60 * s)).Contains(p)) HideCard();
        }
        if (!_armed) _session?.OnMouseOver(p);
    }

    async void OnHoverStill(POINT p)
    {
        if (_card.IsVisible && WindowHelper.GetPhysicalRect(_card).Contains(p)) return;
        int ver = ++_hoverVersion;
        string word, sentence;
        RECT wordRect, lineRect;
        double scale = WindowHelper.GetMonitor(p).Scale;

        var panel = _session?.Panel;
        if (panel is { IsVisible: true } && WindowHelper.GetPhysicalRect(panel).Contains(p))
        {
            var hit = panel.TryGetWordAt(p);
            if (hit == null) return;
            (word, sentence, wordRect, lineRect) = (hit.Word, hit.Sentence, hit.WordRect, hit.WordRect);
        }
        else
        {
            var mon = WindowHelper.GetMonitor(p);
            int w = (int)Math.Round(760 * scale), h = (int)Math.Round(100 * scale);
            var r = new RECT(p.X - w / 2, p.Y - h / 2, p.X + w / 2, p.Y + h / 2).Intersect(mon.Bounds);
            if (r.IsEmpty) return;
            OcrPage page;
            try { page = await Ocr.RecognizeScreenAsync(r, 2.0); }
            catch (Exception ex) { Log.Write("Hover OCR failed: " + ex.Message); return; }
            if (ver != _hoverVersion || !_armed) return;
            var found = FindWord(page, p, scale);
            if (found == null) { HideCard(); return; }
            (word, sentence, wordRect, lineRect) = (found.Value.word.Text, found.Value.line.Text, found.Value.word.Bounds, found.Value.line.Bounds);
        }

        var clean = CleanWord(word);
        if (!clean.Any(char.IsLetter)) { HideCard(); return; }
        var key = clean.ToLowerInvariant() + "|" + sentence;
        if (_card.IsVisible && key == _cardKey) return;

        _cardKey = key;
        _cardLinger = !_armed;
        _lookupCts?.Cancel();
        _phraseCts?.Cancel();
        var cts = _lookupCts = new CancellationTokenSource();
        int uh = Math.Max(2, (int)Math.Round(2 * scale));
        _underline.ShowAt(new RECT(wordRect.Left, wordRect.Bottom + 1, wordRect.Right, wordRect.Bottom + 1 + uh));
        _card.ShowLoading(clean, sentence);
        if (Config.AutoSpeakWord) Speech.Say(clean);
        bool cached = _lookupCache.TryGetValue(key, out var info);
        var dict = cached ? null : OfflineDict.Lookup(clean);
        if (dict != null) _card.ShowDict(dict, aiPending: true); // instant meaning while the AI thinks
        _card.PlaceNear(lineRect, wordRect.Left);

        if (!cached)
        {
            string? failure = null;
            try
            {
                info = await Translator.LookupAsync(clean, sentence, cts.Token);
                _lookupCache[key] = info;
            }
            catch (OperationCanceledException) { return; }
            catch (TranslateException ex) { failure = ex.Message; }
            catch (Exception ex)
            {
                Log.Write("Lookup failed: " + ex);
                failure = "查词失败";
            }
            if (failure != null)
            {
                if (_cardKey != key) return;
                if (dict != null) _card.MarkOffline(failure);
                else _card.ShowError(failure);
                _card.Reposition();
                return;
            }
        }
        if (_cardKey != key || cts.IsCancellationRequested) return;
        _card.ShowInfo(info);
        _card.Reposition();
    }

    static (OcrWordM word, OcrLineM line)? FindWord(OcrPage page, POINT p, double scale)
    {
        foreach (var line in page.Lines)
            foreach (var w in line.Words)
                if (w.Bounds.Inflate(2).Contains(p)) return (w, line);

        (OcrWordM, OcrLineM)? best = null;
        double bestDist = 8 * scale;
        foreach (var line in page.Lines)
        {
            if (p.Y < line.Bounds.Top - 2 || p.Y > line.Bounds.Bottom + 2) continue;
            foreach (var w in line.Words)
            {
                int dx = p.X < w.Bounds.Left ? w.Bounds.Left - p.X : p.X > w.Bounds.Right ? p.X - w.Bounds.Right : 0;
                if (dx < bestDist) { bestDist = dx; best = (w, line); }
            }
        }
        return best;
    }

    static string CleanWord(string w)
    {
        static bool Keep(char c) => char.IsLetterOrDigit(c) || c == '\'' || c == '-' || c == '’';
        int a = 0, b = w.Length - 1;
        while (a <= b && !Keep(w[a])) a++;
        while (b >= a && !Keep(w[b])) b--;
        var s = a <= b ? w[a..(b + 1)] : "";
        return s.Trim('-', '\'', '’');
    }

    void HideCard()
    {
        _lookupCts?.Cancel();
        _phraseCts?.Cancel();
        _cardKey = null;
        _cardLinger = false;
        if (_card.IsVisible) _card.Hide();
        if (_underline.IsVisible) _underline.Hide();
    }

    void OnDragEnded(RECT r, bool copy)
    {
        _dragActive = false;
        if (copy)
        {
            if (r.Width < 8 || r.Height < 8) _copySelection.Hide();
            else CopyRegion(r);
            return;
        }
        _selection.Hide();
        if (r.Width < 12 || r.Height < 12) return;
        _session?.Close();
        _session = new RegionSession(this, r, SessionMode.Box);
        _session.Start();
    }

    // ---------------- box copy (Ctrl + right-drag) ----------------

    internal async void CopyRegion(RECT r, string okTitle = "已复制")
    {
        int ver = ++_copyVersion;
        _copySelection.ShowAt(r);
        _toast.ShowBusy(r);
        string text;
        try { text = await Ocr.RecognizeTextAsync(r); }
        catch (Exception ex)
        {
            Log.Write("Copy OCR failed: " + ex);
            text = "";
        }
        if (ver != _copyVersion) return;
        if (string.IsNullOrWhiteSpace(text))
            _toast.ShowResult(r, false, "没有识别到文字", "");
        else if (!TrySetClipboard(text))
            _toast.ShowResult(r, false, "复制失败：剪贴板被别的程序占用，请再试一次", "");
        else
            _toast.ShowResult(r, true, $"✓ {okTitle} · {text.Count(c => !char.IsWhiteSpace(c))} 字", text);
    }

    // ---------------- voice input: hold right Alt, speak, let go → text is typed where the cursor is ----------------

    /// <summary>Asks the app to open Settings (e.g. to download the speech model).</summary>
    public event Action? OpenSettingsRequested;
    public static Controller? Instance { get; private set; }
    public HotKey TalkKey => _hook.TalkKey;
    /// <summary>Call after Settings saved: picks up a new talk key.</summary>
    public void ApplySettings() => _hook.TalkKey = HotKey.Parse(Config.VoiceKey);
    public void BeginKeyCapture(Action<HotKey?> done) => _hook.BeginCapture(done);
    public void CancelKeyCapture() => _hook.CancelCapture();
    VoicePill? _pill;
    AIMouse.Voice.Recorder? _recorder;
    bool _recording, _recognizing;
    long _talkStart;

    void OnTalkDown()
    {
        if (_recognizing) return;
        _pill ??= new VoicePill();
        if (!AIMouse.Voice.SpeechToText.Installed)
        {
            _pill.ShowResult("语音输入需要先下载语音模型", "在设置里一键下载 · 约 230MB", 4);
            OpenSettingsRequested?.Invoke();
            return;
        }
        if (!AIMouse.Voice.Recorder.HasMicrophone)
        {
            _pill.ShowResult("没有找到麦克风", "请接上麦克风或耳机再试", 3);
            return;
        }
        try
        {
            AIMouse.Voice.SpeechToText.Warm();
            if (_recorder == null)
            {
                _recorder = new AIMouse.Voice.Recorder();
                _recorder.Level += lv => Dispatcher.BeginInvoke(() => { if (_recording) _pill?.SetLevel(lv); });
            }
            _recorder.Start();
            _recording = true;
            _talkStart = Environment.TickCount64;
            _pill.ShowListening($"松开 {_hook.TalkKey.Display} 结束");
        }
        catch (Exception ex)
        {
            Log.Write("Mic start failed: " + ex.Message);
            _pill.ShowResult("打不开麦克风", "请在 Windows 设置 → 隐私 → 麦克风 里允许桌面应用使用", 4);
        }
    }

    async void OnTalkUp()
    {
        if (!_recording || _recorder == null || _pill == null) return;
        _recording = false;
        var samples = _recorder.Stop();
        if (Environment.TickCount64 - _talkStart < 350 || samples.Length < AIMouse.Voice.SpeechToText.SampleRate / 3)
        {
            _pill.Hide();
            return;
        }
        _recognizing = true;
        _pill.ShowWorking();
        try
        {
            var text = await AIMouse.Voice.SpeechToText.RecognizeAsync(samples);
            if (string.IsNullOrWhiteSpace(text))
            {
                _pill.ShowResult("没有听清，请再说一次", "", 2);
                return;
            }
            if (!TrySetClipboard(text))
            {
                _pill.ShowResult("剪贴板被占用，请再试一次", "", 2.5);
                return;
            }
            await Task.Delay(60);
            InputHook.SendPaste();
            _pill.ShowResult(text, "已输入 · 也已复制", 2.6);
        }
        catch (Exception ex)
        {
            Log.Write("Voice input failed: " + ex);
            _pill.ShowResult("语音识别出错了", ex.Message, 3);
        }
        finally { _recognizing = false; }
    }

    static bool TrySetClipboard(string text)
    {
        for (int i = 0; i < 6; i++)
        {
            try
            {
                System.Windows.Clipboard.SetDataObject(text, true);
                return true;
            }
            catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(80); }
        }
        return false;
    }

    void HideCopy()
    {
        _copyVersion++;
        _toast.HideNow();
        _copySelection.Hide();
    }

    void OnDoubleTap()
    {
        if (_session is { Mode: SessionMode.FullScreen })
        {
            _session.Close();
            return;
        }
        _session?.Close();
        HideCard();
        var mon = WindowHelper.GetMonitor(WindowHelper.CursorPos());
        _session = new RegionSession(this, mon.Bounds, SessionMode.FullScreen);
        _session.Start();
    }

    internal void OnSessionClosed(RegionSession s)
    {
        if (ReferenceEquals(_session, s)) _session = null;
    }

    /// <summary>A one-sentence box: show it as a card under the box instead of a panel.</summary>
    internal async void ShowPhrase(Sentence s, RECT region)
    {
        HideCard();
        _cardKey = "phrase|" + s.En;
        var key = _cardKey;
        _cardLinger = true;
        _card.ShowPhrase(s.En);
        _card.PlaceNear(region, region.Left);
        var cached = Cache.Get(s.En);
        if (cached != null)
        {
            _card.SetPhraseZh(cached);
            _card.Reposition();
            return;
        }
        var cts = _phraseCts = new CancellationTokenSource();
        string last = "";
        try
        {
            await Translator.TranslateAsync(new[] { s.En }, (_, zh) => OnUi(() =>
            {
                if (_cardKey != key) return;
                last = zh;
                _card.SetPhraseZh(zh);
                _card.Reposition();
            }), cts.Token);
            if (last.Length > 0) Cache.Set(s.En, last);
        }
        catch (OperationCanceledException) { }
        catch (TranslateException ex) { if (_cardKey == key) { _card.SetPhraseError(ex.Message); _card.Reposition(); } }
        catch (Exception ex) { Log.Write("Phrase failed: " + ex); }
    }

    /// <summary>For testing: open a box session on a region directly.</summary>
    internal RegionSession? CurrentSession => _session;

    /// <summary>README screenshots: show the "译" badge and the word card at a point, as if Ctrl were held there.</summary>
    internal void DemoWord(POINT p) { OnArmedChanged(true, p); OnHoverStill(p); }
    internal void DemoReset() { HideCard(); OnArmedChanged(false, new POINT(0, 0)); _session?.Close(); }

    public void Demo(RECT r, bool fullScreen, ViewMode? view = null, bool live = false)
    {
        _session?.Close();
        _session = new RegionSession(this, r, fullScreen ? SessionMode.FullScreen : live ? SessionMode.Live : SessionMode.Box, view);
        _session.Start();
    }
}
