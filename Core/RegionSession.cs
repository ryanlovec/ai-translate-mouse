using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Threading;
using AIMouse.Capture;
using AIMouse.Native;
using AIMouse.Ocr;
using AIMouse.Text;
using AIMouse.Translate;
using AIMouse.UI;

namespace AIMouse.Core;

public enum SessionMode { Box, Live, FullScreen }

/// <summary>
/// One translated screen region (box / live box / full screen). Three ways to show it:
/// 侧栏 = panel beside the text, 对照 = bilingual panel laid over the text, 译文 = Chinese painted over the English in place.
/// </summary>
public sealed class RegionSession
{
    static readonly Color Accent = Color.FromRgb(0x00, 0x7A, 0xFF);

    readonly Controller _c;
    public RECT Region { get; }
    public SessionMode Mode { get; private set; }
    public ViewMode View { get; private set; }
    public PanelWindow? Panel { get; private set; }
    internal ReplaceOverlay? Overlay => _overlay;

    RectWindow? _frame;
    ReplaceOverlay? _overlay;
    ModeBar? _bar;
    readonly List<RectWindow> _hl = new();
    List<SentenceItem> _items = new();
    Dictionary<int, (Color Bg, Color Fg)> _paraColors = new();
    CancellationTokenSource? _runCts;
    DispatcherTimer? _liveTimer, _renderTimer;
    byte[]? _processedFp, _prevFp;
    bool _dirty, _running, _closed;
    SentenceItem? _screenHover;
    long _lastOverTick;

    // ---- 译文 mode: follow scrolling + remember pages ----
    float[][]? _shownProfile, _lastProfile;          // row profile of the screen the Chinese layer currently matches
    bool _needRefresh, _tracking, _anchored, _moving;
    int _stableTicks;

    sealed record PageMemo(RECT Region, float[][] Profile, List<(string En, string Zh, int Para, List<RECT> Frags)> Items,
                           Dictionary<int, (Color Bg, Color Fg)> Colors);
    static readonly List<PageMemo> Memory = new();

    public RegionSession(Controller c, RECT region, SessionMode mode, ViewMode? view = null)
    {
        _c = c;
        Region = region;
        Mode = mode;
        View = view ?? Parse(mode == SessionMode.FullScreen ? c.Config.FullView : c.Config.BoxView);
    }

    static ViewMode Parse(string s) => s switch { "under" => ViewMode.Under, "replace" => ViewMode.Replace, _ => ViewMode.Side };
    static string Name(ViewMode v) => v switch { ViewMode.Under => "under", ViewMode.Replace => "replace", _ => "side" };

    public bool IsClosed => _closed;

    public async void Start()
    {
        UpdateFrame();
        if (View == ViewMode.Replace) EnsureUi();
        await RunAsync(first: true);
        if (!_closed && Mode != SessionMode.Box) StartLive();
    }

    void SetStatus(string text, bool error = false)
    {
        Panel?.SetStatus(text, error);
        _bar?.SetStatus(text, error);
    }

    async Task RunAsync(bool first)
    {
        if (_running || _closed) return;
        _running = true;
        _runCts?.Cancel();
        var cts = _runCts = new CancellationTokenSource();
        try
        {
            SetStatus("识别中…");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _processedFp = await Task.Run(() => ScreenCapture.Fingerprint(Region));
            var region = Region;
            var (page, sampler, profile) = await Task.Run(async () =>
            {
                using var bmp = ScreenCapture.Capture(region);
                var pg = await _c.Ocr.RecognizeBitmapAsync(bmp, region.Left, region.Top, OcrService.UpscaleFor(region));
                return (pg, new ColorSampler(bmp), ScrollTracker.Profile(bmp));
            });
            if (_closed || cts.IsCancellationRequested) return;
            var sentences = Keep(Segmenter.Segment(page));
            long ocrMs = sw.ElapsedMilliseconds;

            if (first && Mode == SessionMode.Box && View == ViewMode.Side && sentences.Count == 1 && page.Lines.Count <= 2)
            {
                _running = false;
                _c.ShowPhrase(sentences[0], Region);
                Close();
                return;
            }

            if (first && Mode == SessionMode.Box && sentences.Count == 0)
            {
                // Nothing English to translate (e.g. Chinese): just copy whatever text is in the box.
                _running = false;
                Close();
                _c.CopyRegion(region, "没有英文，已直接复制");
                return;
            }

            EnsureUi();
            ClearHighlights();
            foreach (var old in _items) old.PropertyChanged -= Item_Changed;
            _items = Anchor(sentences.Select(s => new SentenceItem { En = s.En, Fragments = s.Fragments, Para = s.Para, Zh = _c.Cache.Get(s.En) ?? "" }).ToList());
            foreach (var it in _items) it.PropertyChanged += Item_Changed;
            _paraColors = await Task.Run(() => SampleParagraphColors(_items, sampler, region));
            Panel!.SetItems(_items);
            _shownProfile = profile;
            _dirty = false;
            _anchored = true;
            RenderReplace();

            if (_items.Count == 0)
            {
                SetStatus("没有识别到英文");
                return;
            }
            var todo = _items.Where(i => string.IsNullOrEmpty(i.Zh)).ToList();
            if (todo.Count == 0)
            {
                SetStatus($"完成 · {_items.Count} 句");
                Remember(profile);
                return;
            }

            if (Environment.GetEnvironmentVariable("AIMOUSE_DEBUG") == "1") Log.Write($"[translate] {todo.Count} new sentences");
            long firstMs = -1;
            SetStatus($"翻译中 0/{todo.Count}");
            await _c.Translator.TranslateAsync(todo.Select(t => t.En).ToList(), (i, zh) => _c.OnUi(() =>
            {
                if (cts.IsCancellationRequested || _closed) return;
                todo[i].Zh = zh;
                if (firstMs < 0) firstMs = sw.ElapsedMilliseconds;
                SetStatus($"翻译中 {todo.Count(t => t.Zh.Length > 0)}/{todo.Count}");
            }), cts.Token);

            if (cts.IsCancellationRequested || _closed) return;
            foreach (var t in todo)
                if (t.Zh.Length > 0 && t.Zh != "（翻译失败）") _c.Cache.Set(t.En, t.Zh);
            SetStatus($"完成 · {_items.Count} 句");
            Remember(profile);
            Log.Write($"[timing] read screen {ocrMs}ms, {todo.Count} sentences ({todo.Sum(t => t.En.Length)} chars), first Chinese {firstMs}ms, all done {sw.ElapsedMilliseconds}ms");
        }
        catch (OperationCanceledException) { }
        catch (TranslateException ex) { SetStatus(ex.Message, true); }
        catch (Exception ex)
        {
            Log.Write("Session run failed: " + ex);
            SetStatus("出错了：" + ex.Message, true);
        }
        finally { _running = false; }
    }

    /// <summary>
    /// Drop OCR noise. Decided per paragraph, so a paragraph is either fully translated or left alone
    /// (important for 译文 mode, which paints over the whole paragraph). Full screen also skips short UI labels.
    /// </summary>
    List<Sentence> Keep(List<Sentence> all)
    {
        static int Words(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Count(w => w.Count(char.IsLetter) >= 2);
        var keep = new List<Sentence>();
        foreach (var g in all.GroupBy(s => s.Para))
        {
            var text = string.Join(" ", g.Select(s => s.En));
            if (text.Count(c => c < 128 && char.IsLetter(c)) < 2) continue;
            if (Mode == SessionMode.FullScreen && Words(text) < 3 && !HasRealWord(text)) continue;
            keep.AddRange(g.Where(s => s.En.Any(char.IsLetter)));
        }
        return keep;
    }

    /// <summary>Short UI labels ("Healthy", "No branches") are worth translating if they contain a real English word,
    /// not just names, codes or IDs ("xgaicn", "ap-northeast-1").</summary>
    static bool HasRealWord(string text)
    {
        var words = System.Text.RegularExpressions.Regex.Matches(text, "[A-Za-z]{3,}").Select(m => m.Value).ToList();
        if (words.Count == 0) return false;
        if (!OfflineDict.Available) return true;
        return words.Any(w => OfflineDict.Lookup(w) != null);
    }

    static RECT ParaRect(IEnumerable<SentenceItem> g)
    {
        var frags = g.SelectMany(i => i.Fragments).ToList();
        var u = frags[0];
        foreach (var f in frags) u = RECT.Union(u, f);
        return u;
    }

    static Dictionary<int, (Color, Color)> SampleParagraphColors(List<SentenceItem> items, ColorSampler sampler, RECT region)
    {
        var d = new Dictionary<int, (Color, Color)>();
        foreach (var g in items.GroupBy(i => i.Para))
        {
            if (!g.SelectMany(i => i.Fragments).Any()) continue;
            var r = ParaRect(g);
            d[g.Key] = sampler.Sample(r.Left - region.Left, r.Top - region.Top, r.Right - region.Left, r.Bottom - region.Top);
        }
        return d;
    }

    // ---------------- views ----------------

    void EnsureUi()
    {
        if (Panel == null)
        {
            Panel = new PanelWindow();
            Panel.CloseRequested += Close;
            Panel.PinToggled += TogglePin;
            Panel.ViewSwitch.Changed += SetView;
            Panel.ItemHovered += it =>
            {
                foreach (var x in Panel.CurrentItems) x.IsHighlighted = ReferenceEquals(x, it);
                if (View == ViewMode.Side) ShowHighlight(it);
            };
            UpdateMode();
        }
        ApplyView();
    }

    void SetView(ViewMode v)
    {
        if (v == View || _closed) return;
        View = v;
        if (Mode == SessionMode.FullScreen) _c.Config.FullView = Name(v);
        else _c.Config.BoxView = Name(v);
        try { _c.Config.Save(); } catch (Exception ex) { Log.Write("Save view failed: " + ex.Message); }
        ClearHighlights();
        ApplyView();
        if (_liveTimer != null) _liveTimer.Interval = TimeSpan.FromMilliseconds(View == ViewMode.Replace ? 150 : 600);
    }

    void ApplyView()
    {
        if (_closed) return;
        Panel?.ViewSwitch.Set(View);
        UpdateFrame();
        if (View == ViewMode.Replace)
        {
            Panel?.Hide();
            _overlay ??= new ReplaceOverlay();
            if (_bar == null)
            {
                _bar = new ModeBar();
                _bar.CloseRequested += Close;
                _bar.Switch.Changed += SetView;
            }
            _bar.Switch.Set(View);
            _bar.PlaceAt(Region);
            RenderReplace();
        }
        else
        {
            _overlay?.Clear();
            if (_bar is { IsVisible: true }) _bar.Hide();
            PlacePanel();
        }
    }

    void Item_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SentenceItem.Zh) || View != ViewMode.Replace) return;
        _renderTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, (_, _) =>
        {
            _renderTimer!.Stop();
            RenderReplace();
        }, _c.Dispatcher);
        _renderTimer.Stop();
        _renderTimer.Start();
    }

    /// <summary>Paint each fully translated paragraph over its English (untranslated ones stay visible).</summary>
    void RenderReplace()
    {
        if (View != ViewMode.Replace || _overlay == null || _closed || _dirty || _moving) return;
        var heights = _items.SelectMany(i => i.Fragments).Select(f => f.Height).OrderBy(h => h).ToList();
        double typical = heights.Count > 0 ? heights[heights.Count / 2] : 20;
        var blocks = _items.GroupBy(i => i.Para)
            .Where(g => g.All(i => i.Zh.Length > 0) && g.SelectMany(i => i.Fragments).Any())
            .Select(g =>
            {
                var (bg, fg) = _paraColors.TryGetValue(g.Key, out var c) ? c : (Colors.White, Color.FromRgb(0x1D, 0x1D, 0x1F));
                // Same size as the page's text; tiny labels ("Tips:") don't get tiny Chinese.
                var lineH = Math.Max(g.SelectMany(i => i.Fragments).Average(f => f.Height), typical * 0.9);
                return new ReplaceBlock(ParaRect(g), string.Concat(g.Select(i => i.Zh.Trim())), bg, fg, lineH);
            }).ToList();
        _overlay.Render(Region, blocks);
    }

    void PlacePanel()
    {
        if (Panel == null) return;
        var mon = WindowHelper.GetMonitor(Region.Center);
        var wa = mon.Work;
        double s = mon.Scale;
        int m = (int)(14 * s);                       // the panel's invisible shadow margin
        int x, y, w, h;
        if (View == ViewMode.Under)
        {
            if (Mode == SessionMode.FullScreen)
            {
                // Reading column in the middle of the screen.
                w = Math.Min((int)(900 * s), wa.Width);
                x = wa.Left + (wa.Width - w) / 2;
                y = wa.Top;
                h = wa.Height;
            }
            else
            {
                // Laid exactly over the box: same width, header just above it.
                int header = (int)(45 * s);
                w = Math.Max(Region.Width + 2 * m, (int)(320 * s));
                h = Math.Max(Region.Height + 2 * m + header, (int)(260 * s));
                x = Region.Left - m;
                y = Region.Top - m - header;
                x = Math.Clamp(x, wa.Left, Math.Max(wa.Left, wa.Right - w));
                y = Math.Clamp(y, wa.Top, Math.Max(wa.Top, wa.Bottom - h));
                h = Math.Min(h, wa.Height);
            }
        }
        else if (Mode == SessionMode.FullScreen)
        {
            w = (int)Math.Round(400 * s);
            x = wa.Right - w;
            y = wa.Top;
            h = wa.Height;
        }
        else
        {
            w = (int)Math.Round(400 * s);
            int gap = (int)(4 * s);
            h = (int)Math.Clamp(Region.Height + 2 * m, 280 * s, 0.85 * wa.Height);
            if (Region.Right + gap + w - m <= wa.Right) x = Region.Right + gap - m;
            else if (Region.Left - gap - w + m >= wa.Left) x = Region.Left - gap - w + m;
            else x = wa.Right - w;
            y = Math.Clamp(Region.Top - m, wa.Top, Math.Max(wa.Top, wa.Bottom - h));
        }
        WindowHelper.PlacePhysical(Panel, x, y, w, h);
    }

    void UpdateMode()
    {
        switch (Mode)
        {
            case SessionMode.Box: Panel?.SetMode("框选翻译", false, true); break;
            case SessionMode.Live: Panel?.SetMode("实时翻译", true, true); break;
            case SessionMode.FullScreen: Panel?.SetMode("全屏翻译", true, false); break;
        }
    }

    void UpdateFrame()
    {
        if (Mode == SessionMode.FullScreen || View != ViewMode.Side)
        {
            if (_frame is { IsVisible: true }) _frame.Hide();
            return;
        }
        _frame ??= new RectWindow(Accent, 1.5, Colors.Transparent, dashed: true, radius: 3);
        _frame.SetDashed(Mode == SessionMode.Box);
        _frame.ShowAt(Region.Inflate(3));
    }

    void TogglePin()
    {
        if (Mode == SessionMode.FullScreen) return;
        if (Mode == SessionMode.Box)
        {
            Mode = SessionMode.Live;
            StartLive();
        }
        else
        {
            Mode = SessionMode.Box;
            _liveTimer?.Stop();
        }
        UpdateMode();
        UpdateFrame();
    }

    void StartLive()
    {
        _prevFp = null;
        _liveTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(600), DispatcherPriority.Background, async (_, _) => await LiveTick(), _c.Dispatcher);
        _liveTimer.Interval = TimeSpan.FromMilliseconds(View == ViewMode.Replace ? 150 : 600);
        _liveTimer.Start();
    }

    async Task LiveTick()
    {
        if (View == ViewMode.Replace)
        {
            await TrackTick();
            return;
        }
        if (_running || _closed || Mode == SessionMode.Box) return;
        byte[] fp;
        try { fp = await Task.Run(() => ScreenCapture.Fingerprint(Region)); }
        catch (Exception ex) { Log.Write("Fingerprint failed: " + ex.Message); return; }
        if (_closed) return;
        if (_processedFp != null && ScreenCapture.Diff(fp, _processedFp) > 2.0 && !_dirty)
        {
            _dirty = true;
            // The page moved (scrolling…): take the Chinese layer away so it never sits on the wrong text.
            if (View == ViewMode.Replace)
            {
                _overlay?.Clear();
                SetStatus("内容变了，更新中…");
            }
        }
        bool stable = _prevFp != null && ScreenCapture.Diff(fp, _prevFp) < 1.0;
        _prevFp = fp;
        if (_dirty && stable && !_running)
        {
            _dirty = false;
            await RunAsync(first: false);
        }
    }

    /// <summary>
    /// 译文 mode, every 150 ms. The Chinese is pinned to "_shownProfile" (what the page looked like where the
    /// translations sit). If the page scrolled, slide the Chinese with it. A blurry frame in the middle of a
    /// scroll animation is not trusted: the Chinese hides for a moment and we match again once the page settles.
    /// Only a settled page that still doesn't match counts as a new page (restored from memory if seen before).
    /// </summary>
    async Task TrackTick()
    {
        if (_closed || _tracking || Mode == SessionMode.Box || _shownProfile == null) return;
        _tracking = true;
        try
        {
            var region = Region;
            var anchor = _shownProfile;
            var prof = await Task.Run(() => ScrollTracker.Profile(region));
            if (_closed || !ReferenceEquals(anchor, _shownProfile)) return;
            bool settled = _lastProfile != null && ScrollTracker.Same(_lastProfile, prof);
            _lastProfile = prof;
            _stableTicks = settled ? _stableTicks + 1 : 0;
            bool debug = Environment.GetEnvironmentVariable("AIMOUSE_DEBUG") == "1";

            if (ScrollTracker.Same(anchor, prof))
            {
                if (_moving) { _moving = false; RenderReplace(); }
                if (_stableTicks >= 2 && _needRefresh && !_running)
                {
                    _needRefresh = false;
                    await RunAsync(first: false);
                }
                return;
            }
            _needRefresh = true;

            if (!_dirty)
            {
                var shift = await Task.Run(() => ScrollTracker.FindShift(anchor, prof, region.Height * 3 / 4));
                if (_closed) return;
                if (shift is { } sh)
                {
                    if (debug) Log.Write($"[track] scroll dy={sh.Dy} moved={string.Join("", sh.Moved.Select(m => m ? 1 : 0))}");
                    ShiftItems(sh);
                    _shownProfile = prof;
                    _moving = false;
                    RenderReplace();
                    return;
                }
                if (!settled)
                {
                    // Mid-animation frame we can't read reliably: hide for now, keep the pins, try again next tick.
                    if (!_moving) { _moving = true; _overlay?.Clear(); }
                    return;
                }
            }
            else if (!settled) return;

            // The page has settled and isn't a scroll of what we had: a different page.
            var memo = await Task.Run(() => Recall(region, prof));
            if (_closed) return;
            if (memo is { } mm)
            {
                if (debug) Log.Write($"[track] restored page from memory dy={mm.Dy}");
                Restore(mm.Memo, mm.Dy);
                _shownProfile = prof;
                _dirty = false;
                _moving = false;
                _anchored = true;
                RenderReplace();
                SetStatus("已从记忆恢复");
                return;
            }
            if (!_dirty)
            {
                if (debug) Log.Write("[track] new page");
                _dirty = true;
                _anchored = false;
                _overlay?.Clear();
                SetStatus("新页面，翻译中…");
            }
            if (!_running)
            {
                _needRefresh = false;
                await RunAsync(first: false);
            }
        }
        catch (Exception ex) { Log.Write("Track failed: " + ex.Message); }
        finally { _tracking = false; }
    }

    /// <summary>Slide the Chinese of the columns that scrolled; keep still columns; drop text in columns that changed otherwise.</summary>
    void ShiftItems(ScrollTracker.Shift sh)
    {
        var keep = new List<SentenceItem>();
        foreach (var it in _items)
        {
            if (it.Fragments.Count == 0) continue;
            var u = it.Fragments[0];
            foreach (var f in it.Fragments) u = RECT.Union(u, f);
            int band = ScrollTracker.BandOf((u.Left + u.Right) / 2 - Region.Left, Region.Width);
            bool inFixedBar = u.Bottom <= Region.Top + sh.Header + 2 || u.Top >= Region.Bottom - sh.Footer - 2;
            if (inFixedBar)
            {
                keep.Add(it);   // text on a fixed toolbar stays exactly where it is
                continue;
            }
            if (sh.Moved[band])
            {
                for (int i = 0; i < it.Fragments.Count; i++)
                {
                    var f = it.Fragments[i];
                    it.Fragments[i] = new RECT(f.Left, f.Top + sh.Dy, f.Right, f.Bottom + sh.Dy);
                }
            }
            else if (!sh.Still[band]) continue;
            // gone off screen, or slid under a fixed toolbar at the top/bottom → don't draw it there
            int top = Region.Top + (sh.Moved[band] ? sh.Header : 0), bottom = Region.Bottom - (sh.Moved[band] ? sh.Footer : 0);
            if (it.Fragments.All(f => f.Top >= top - 2 && f.Bottom <= bottom + 2)) keep.Add(it);
        }
        _items = keep;
        _anchored = true;
    }

    // ---------- anchoring: after a re-check, keep sentences that are already translated at that spot ----------

    static string[] Tokens(string s) =>
        new string(s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>New OCR text is the same sentence as the old one, or a cut-off piece of it (at the screen edge).</summary>
    static bool SameOrPart(string oldEn, string newEn)
    {
        var a = Tokens(oldEn);
        var b = Tokens(newEn);
        if (b.Length == 0) return true;
        if (b.Length > a.Length + 1) return false;          // more text now → it was cut before; translate the full one
        var set = new HashSet<string>(a);
        return b.Count(set.Contains) >= 0.85 * b.Length;
    }

    static bool Overlap(SentenceItem a, SentenceItem b) =>
        a.Fragments.Any(f => b.Fragments.Any(g =>
            Math.Min(f.Bottom, g.Bottom) - Math.Max(f.Top, g.Top) > 0.5 * Math.Min(f.Height, g.Height) &&
            Math.Min(f.Right, g.Right) > Math.Max(f.Left, g.Left)));

    /// <summary>Reuse translated items that sit where the new OCR found the same text, so nothing gets re-translated.</summary>
    List<SentenceItem> Anchor(List<SentenceItem> fresh)
    {
        if (View != ViewMode.Replace || !_anchored || _items.Count == 0) return fresh;
        var used = new HashSet<SentenceItem>();
        var result = new List<SentenceItem>();
        foreach (var n in fresh)
        {
            var o = _items.FirstOrDefault(o => !used.Contains(o) && o.Zh.Length > 0 && Overlap(o, n) && SameOrPart(o.En, n.En));
            if (o == null)
            {
                result.Add(n);
                continue;
            }
            used.Add(o);
            // keep the old translation and its (possibly larger, partly off-screen) position, in the new paragraph numbering
            result.Add(new SentenceItem { En = o.En, Zh = o.Zh, Para = n.Para, Fragments = o.Fragments.ToList() });
        }
        return result;
    }

    void Remember(float[][] profile)
    {
        if (_items.Count == 0 || _items.Any(i => i.Zh.Length == 0)) return;
        if (Environment.GetEnvironmentVariable("AIMOUSE_DEBUG") == "1")
            foreach (var i in _items) Log.Write($"[page] {i.Para} | {i.En} => {i.Zh}");
        var memo = new PageMemo(Region, profile,
            _items.Select(i => (i.En, i.Zh, i.Para, i.Fragments.ToList())).ToList(),
            new Dictionary<int, (Color, Color)>(_paraColors));
        lock (Memory)
        {
            Memory.RemoveAll(m => m.Region.Equals(Region) && ScrollTracker.Same(m.Profile, profile));
            Memory.Add(memo);
            if (Memory.Count > 40) Memory.RemoveAt(0);
        }
    }

    static (PageMemo Memo, int Dy)? Recall(RECT region, float[][] prof)
    {
        List<PageMemo> list;
        lock (Memory) list = Memory.Where(m => m.Region.Equals(region)).ToList();
        (PageMemo, int, int)? best = null;
        foreach (var m in list)
        {
            if (ScrollTracker.Same(m.Profile, prof)) return (m, 0);
            var sh = ScrollTracker.FindShift(m.Profile, prof, region.Height / 2);
            if (sh == null) continue;
            int good = sh.Moved.Count(x => x) + sh.Still.Count(x => x);
            if (good >= ScrollTracker.Bands - 1 && (best == null || good > best.Value.Item3)) best = (m, sh.Dy, good);
        }
        return best == null ? null : (best.Value.Item1, best.Value.Item2);
    }

    void Restore(PageMemo memo, int dy)
    {
        foreach (var old in _items) old.PropertyChanged -= Item_Changed;
        _items = memo.Items.Select(t => new SentenceItem
        {
            En = t.En, Zh = t.Zh, Para = t.Para,
            Fragments = t.Frags.Select(f => new RECT(f.Left, f.Top + dy, f.Right, f.Bottom + dy)).ToList(),
        }).ToList();
        foreach (var it in _items) it.PropertyChanged += Item_Changed;
        _paraColors = new Dictionary<int, (Color, Color)>(memo.Colors);
        Panel?.SetItems(_items);
    }

    void ShowHighlight(SentenceItem? item)
    {
        var frags = item?.Fragments ?? new List<RECT>();
        while (_hl.Count < frags.Count) _hl.Add(new RectWindow(Accent, 2, Color.FromArgb(0x1A, 0x00, 0x7A, 0xFF), radius: 3));
        for (int i = 0; i < _hl.Count; i++)
        {
            if (i < frags.Count) _hl[i].ShowAt(frags[i].Inflate(2));
            else if (_hl[i].IsVisible) _hl[i].Hide();
        }
    }

    void ClearHighlights()
    {
        _screenHover = null;
        foreach (var h in _hl) if (h.IsVisible) h.Hide();
    }

    /// <summary>Mouse moving over the original text (Ctrl not held): link it to the panel.</summary>
    public void OnMouseOver(POINT p)
    {
        if (Panel == null || _closed || View != ViewMode.Side) return;
        long now = Environment.TickCount64;
        if (now - _lastOverTick < 50) return;
        _lastOverTick = now;
        if (Panel.IsVisible && WindowHelper.GetPhysicalRect(Panel).Contains(p)) return;

        SentenceItem? hit = null;
        if (Region.Contains(p))
            hit = Panel.CurrentItems.FirstOrDefault(it => it.Fragments.Any(f => f.Inflate(2).Contains(p)));
        if (ReferenceEquals(hit, _screenHover)) return;
        _screenHover = hit;
        foreach (var x in Panel.CurrentItems) x.IsHighlighted = ReferenceEquals(x, hit);
        ShowHighlight(hit);
        if (hit != null) Panel.ScrollIntoView(hit);
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        _runCts?.Cancel();
        _liveTimer?.Stop();
        _renderTimer?.Stop();
        Panel?.Close();
        _frame?.Close();
        _overlay?.Close();
        _bar?.Close();
        foreach (var h in _hl) h.Close();
        _hl.Clear();
        _c.OnSessionClosed(this);
    }
}
