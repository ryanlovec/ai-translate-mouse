# Rewrites TrackTick: tolerate blurry mid-scroll frames; decide "new page" only once the screen has settled.
import os, re
os.chdir(os.path.join(os.path.dirname(__file__), ".."))
p = "Core/RegionSession.cs"
s = open(p, encoding="utf-8").read()

start = s.index("    /// <summary>\n    /// 译文 mode, every 150 ms")
end = s.index("    /// <summary>Slide the Chinese of the columns that scrolled")
new = '''    /// <summary>
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

'''
s = s[:start] + new + s[end:]
s = s.replace("    float[][]? _shownProfile;", "    float[][]? _shownProfile, _lastProfile;", 1)
s = s.replace("    bool _needRefresh, _tracking, _anchored;", "    bool _needRefresh, _tracking, _anchored, _moving;", 1)
open(p, "w", encoding="utf-8").write(s)
print("patched")
