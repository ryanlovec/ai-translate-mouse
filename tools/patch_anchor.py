# Column-aware scroll following + anchored (keep-what's-translated) re-checks for RegionSession. Run once.
import os
os.chdir(os.path.join(os.path.dirname(__file__), ".."))
p = "Core/RegionSession.cs"
s = open(p, encoding="utf-8").read()


def rep(a, b):
    global s
    assert a in s, "missing: " + a[:80]
    s = s.replace(a, b, 1)


rep("float[]? _shownProfile;", "float[][]? _shownProfile;")
rep("bool _needRefresh, _tracking;", "bool _needRefresh, _tracking, _anchored;")
rep("sealed record PageMemo(RECT Region, float[] Profile,", "sealed record PageMemo(RECT Region, float[][] Profile,")
rep("void Remember(float[] profile)", "void Remember(float[][] profile)")
rep("static (PageMemo Memo, int Dy)? Recall(RECT region, float[] prof)", "static (PageMemo Memo, int Dy)? Recall(RECT region, float[][] prof)")
rep("Memory.RemoveAll(m => m.Region.Equals(Region) && ScrollTracker.Score(m.Profile, profile, 0).Score > 0.97);",
    "Memory.RemoveAll(m => m.Region.Equals(Region) && ScrollTracker.Same(m.Profile, profile));")

# Recall: column-aware
rep('''        (PageMemo, int, double)? best = null;
        foreach (var m in list)
        {
            var sh = ScrollTracker.FindShift(m.Profile, prof, region.Height / 2);
            if (sh is { } x && (best == null || x.Score > best.Value.Item3)) best = (m, x.Dy, x.Score);
        }
        return best == null ? null : (best.Value.Item1, best.Value.Item2);''',
    '''        (PageMemo, int, int)? best = null;
        foreach (var m in list)
        {
            if (ScrollTracker.Same(m.Profile, prof)) return (m, 0);
            var sh = ScrollTracker.FindShift(m.Profile, prof, region.Height / 2);
            if (sh == null) continue;
            int good = sh.Moved.Count(x => x) + sh.Still.Count(x => x);
            if (good >= ScrollTracker.Bands - 1 && (best == null || good > best.Value.Item3)) best = (m, sh.Dy, good);
        }
        return best == null ? null : (best.Value.Item1, best.Value.Item2);''')

# Tick: use Same / Shift
rep('''            var (same, _) = ScrollTracker.Score(shown, prof, 0);
            if (same >= 0.97)
            {''', '''            if (ScrollTracker.Same(shown, prof))
            {''')
rep('''                if (shift is { } sh)
                {
                    ShiftItems(sh.Dy);''', '''                if (shift is { } sh)
                {
                    ShiftItems(sh);''')
rep('''                Restore(mm.Memo, mm.Dy);
                _shownProfile = prof;
                _dirty = false;''', '''                Restore(mm.Memo, mm.Dy);
                _shownProfile = prof;
                _dirty = false;
                _anchored = true;''')
rep('''            if (!_dirty)
            {
                _dirty = true;
                _overlay?.Clear();''', '''            if (!_dirty)
            {
                _dirty = true;
                _anchored = false;
                _overlay?.Clear();''')

# ShiftItems: per column
rep('''    void ShiftItems(int dy)
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
    }''', '''    /// <summary>Slide the Chinese of the columns that scrolled; keep still columns; drop text in columns that changed otherwise.</summary>
    void ShiftItems(ScrollTracker.Shift sh)
    {
        var keep = new List<SentenceItem>();
        foreach (var it in _items)
        {
            if (it.Fragments.Count == 0) continue;
            var u = it.Fragments[0];
            foreach (var f in it.Fragments) u = RECT.Union(u, f);
            int band = ScrollTracker.BandOf((u.Left + u.Right) / 2 - Region.Left, Region.Width);
            if (sh.Moved[band])
            {
                for (int i = 0; i < it.Fragments.Count; i++)
                {
                    var f = it.Fragments[i];
                    it.Fragments[i] = new RECT(f.Left, f.Top + sh.Dy, f.Right, f.Bottom + sh.Dy);
                }
            }
            else if (!sh.Still[band]) continue;
            if (it.Fragments.Any(f => f.Bottom > Region.Top && f.Top < Region.Bottom)) keep.Add(it);
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
    }''')

# RunAsync: merge fresh OCR with anchored items
rep('''            _items = sentences.Select(s => new SentenceItem { En = s.En, Fragments = s.Fragments, Para = s.Para, Zh = _c.Cache.Get(s.En) ?? "" }).ToList();''',
    '''            _items = Anchor(sentences.Select(s => new SentenceItem { En = s.En, Fragments = s.Fragments, Para = s.Para, Zh = _c.Cache.Get(s.En) ?? "" }).ToList());''')
rep('''            Panel!.SetItems(_items);
            _shownProfile = profile;
            _dirty = false;''', '''            Panel!.SetItems(_items);
            _shownProfile = profile;
            _dirty = false;
            _anchored = true;''')

open(p, "w", encoding="utf-8").write(s)
print("patched")
