# Adds scroll-follow + page memory to RegionSession (译文 mode). Run once.
import os
os.chdir(os.path.join(os.path.dirname(__file__), ".."))
p = "Core/RegionSession.cs"
s = open(p, encoding="utf-8").read()


def rep(a, b):
    global s
    assert a in s, "missing: " + a[:70]
    s = s.replace(a, b, 1)


rep('''    SentenceItem? _screenHover;
    long _lastOverTick;''', '''    SentenceItem? _screenHover;
    long _lastOverTick;

    // ---- 译文 mode: follow scrolling + remember pages ----
    float[]? _shownProfile;          // row profile of the screen the Chinese layer currently matches
    bool _needRefresh, _tracking;
    int _stableTicks;

    sealed record PageMemo(RECT Region, float[] Profile, List<(string En, string Zh, int Para, List<RECT> Frags)> Items,
                           Dictionary<int, (Color Bg, Color Fg)> Colors);
    static readonly List<PageMemo> Memory = new();''')

rep('''            var (page, sampler) = await Task.Run(async () =>
            {
                using var bmp = ScreenCapture.Capture(region);
                var pg = await _c.Ocr.RecognizeBitmapAsync(bmp, region.Left, region.Top, OcrService.UpscaleFor(region));
                return (pg, new ColorSampler(bmp));
            });''', '''            var (page, sampler, profile) = await Task.Run(async () =>
            {
                using var bmp = ScreenCapture.Capture(region);
                var pg = await _c.Ocr.RecognizeBitmapAsync(bmp, region.Left, region.Top, OcrService.UpscaleFor(region));
                return (pg, new ColorSampler(bmp), ScrollTracker.Profile(bmp));
            });''')

rep('''            Panel!.SetItems(_items);
            RenderReplace();''', '''            Panel!.SetItems(_items);
            _shownProfile = profile;
            _dirty = false;
            RenderReplace();''')

rep('''            if (todo.Count == 0)
            {
                SetStatus($"完成 · {_items.Count} 句");
                return;
            }''', '''            if (todo.Count == 0)
            {
                SetStatus($"完成 · {_items.Count} 句");
                Remember(profile);
                return;
            }''')

rep('''                if (t.Zh.Length > 0 && t.Zh != "（翻译失败）") _c.Cache.Set(t.En, t.Zh);
            SetStatus($"完成 · {_items.Count} 句");''', '''                if (t.Zh.Length > 0 && t.Zh != "（翻译失败）") _c.Cache.Set(t.En, t.Zh);
            SetStatus($"完成 · {_items.Count} 句");
            Remember(profile);''')

rep('''        _liveTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(600), DispatcherPriority.Background, async (_, _) => await LiveTick(), _c.Dispatcher);
        _liveTimer.Start();
    }

    async Task LiveTick()
    {
        if (_running || _closed || Mode == SessionMode.Box) return;''', '''        _liveTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(600), DispatcherPriority.Background, async (_, _) => await LiveTick(), _c.Dispatcher);
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
        if (_running || _closed || Mode == SessionMode.Box) return;''')

rep('''        ClearHighlights();
        ApplyView();
    }''', '''        ClearHighlights();
        ApplyView();
        if (_liveTimer != null) _liveTimer.Interval = TimeSpan.FromMilliseconds(View == ViewMode.Replace ? 150 : 600);
    }''')

rep('''    void ShowHighlight(SentenceItem? item)''', '''    /// <summary>
    /// 译文 mode, every 150 ms: if the page scrolled, slide the Chinese with it; once it stops, quietly re-check
    /// (new lines get translated, known ones come from the cache). A page seen before is restored instantly.
    /// </summary>
    async Task TrackTick()
    {
        if (_closed || _tracking || Mode == SessionMode.Box || _shownProfile == null) return;
        _tracking = true;
        try
        {
            var region = Region;
            var shown = _shownProfile;
            var prof = await Task.Run(() => ScrollTracker.Profile(region));
            if (_closed || !ReferenceEquals(shown, _shownProfile)) return;

            var (same, _) = ScrollTracker.Score(shown, prof, 0);
            if (same >= 0.97)
            {
                if (++_stableTicks >= 2 && _needRefresh && !_running)
                {
                    _needRefresh = false;
                    await RunAsync(first: false);
                }
                return;
            }
            _stableTicks = 0;
            _needRefresh = true;

            if (!_dirty)
            {
                var shift = await Task.Run(() => ScrollTracker.FindShift(shown, prof, region.Height * 3 / 4));
                if (_closed) return;
                if (shift is { } sh)
                {
                    ShiftItems(sh.Dy);
                    _shownProfile = prof;
                    RenderReplace();
                    return;
                }
            }

            var memo = await Task.Run(() => Recall(region, prof));
            if (_closed) return;
            if (memo is { } mm)
            {
                Restore(mm.Memo, mm.Dy);
                _shownProfile = prof;
                _dirty = false;
                RenderReplace();
                SetStatus("已从记忆恢复");
                return;
            }

            // A page we don't know yet: take the Chinese away and translate it once the screen settles.
            if (!_dirty)
            {
                _dirty = true;
                _overlay?.Clear();
                SetStatus("新页面，翻译中…");
            }
            _shownProfile = prof;
        }
        catch (Exception ex) { Log.Write("Track failed: " + ex.Message); }
        finally { _tracking = false; }
    }

    void ShiftItems(int dy)
    {
        if (dy == 0) return;
        foreach (var it in _items)
            for (int i = 0; i < it.Fragments.Count; i++)
            {
                var f = it.Fragments[i];
                it.Fragments[i] = new RECT(f.Left, f.Top + dy, f.Right, f.Bottom + dy);
            }
        // keep only what is still (partly) on screen
        _items = _items.Where(it => it.Fragments.Any(f => f.Bottom > Region.Top && f.Top < Region.Bottom)).ToList();
    }

    void Remember(float[] profile)
    {
        if (_items.Count == 0 || _items.Any(i => i.Zh.Length == 0)) return;
        var memo = new PageMemo(Region, profile,
            _items.Select(i => (i.En, i.Zh, i.Para, i.Fragments.ToList())).ToList(),
            new Dictionary<int, (Color, Color)>(_paraColors));
        lock (Memory)
        {
            Memory.RemoveAll(m => m.Region.Equals(Region) && ScrollTracker.Score(m.Profile, profile, 0).Score > 0.97);
            Memory.Add(memo);
            if (Memory.Count > 40) Memory.RemoveAt(0);
        }
    }

    static (PageMemo Memo, int Dy)? Recall(RECT region, float[] prof)
    {
        List<PageMemo> list;
        lock (Memory) list = Memory.Where(m => m.Region.Equals(region)).ToList();
        (PageMemo, int, double)? best = null;
        foreach (var m in list)
        {
            var sh = ScrollTracker.FindShift(m.Profile, prof, region.Height / 2);
            if (sh is { } x && (best == null || x.Score > best.Value.Item3)) best = (m, x.Dy, x.Score);
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

    void ShowHighlight(SentenceItem? item)''')

open(p, "w", encoding="utf-8").write(s)
print("patched")
