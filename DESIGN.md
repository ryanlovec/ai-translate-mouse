# AI Mouse (AI翻译鼠标) — Implementation Spec

A Windows 11 tray app that turns the mouse into an English→Chinese translator for a Chinese student
learning English. Works on top of every application. Translation by DeepSeek API, text recognition by
the built-in Windows OCR (`Windows.Media.Ocr`, en-US is installed). **Translations never cover the
English text** — they appear in a card below the line or in a side panel.

All UI text is Simplified Chinese.

## 1. User gestures (the whole UX)

| Gesture | Result |
|---|---|
| Hold **Ctrl** alone, point at a word, keep still ~150 ms | Word card appears **below that line** (never over it). Word gets a thin underline. Move to another word → card updates. |
| Hold **Ctrl** + left-drag a box | Box translation. 1-line box → result shown as a card below the box. Multi-line box → bilingual side panel next to the box. |
| Click 📌 in the panel | Box becomes **live**: re-translates automatically when its content changes (scrolling). |
| **Double-tap Ctrl** | Full-screen live translation of the monitor under the cursor (panel docked at right). Double-tap again closes it. |
| **Esc** | Closes word card / cancels an in-progress drag (never swallowed). |

While Ctrl is "armed", a small pill badge **译** follows the cursor (offset +16,+18 px) so the user knows the mouse is in AI mode.

Normal shortcuts must keep working: Ctrl+C/V/Z etc. never arm; quick Ctrl+click passes through; slow Ctrl+click (armed, no drag) is re-injected so the app still receives Ctrl+click; Ctrl+wheel passes through.

## 2. Tech

- C# / .NET 9, WPF + WinForms (for NotifyIcon only). Project file `AIMouse.csproj` in repo root:
  - `<TargetFramework>net9.0-windows10.0.19041.0</TargetFramework>`, `<OutputType>WinExe</OutputType>`,
    `<UseWPF>true</UseWPF>`, `<UseWindowsForms>true</UseWindowsForms>`, `<Nullable>enable</Nullable>`,
    `<ImplicitUsings>enable</ImplicitUsings>`, `<AssemblyName>AIMouse</AssemblyName>`, `<ApplicationManifest>app.manifest</ApplicationManifest>`.
  - Beware ambiguous types between WPF and WinForms/System.Drawing (Point, Rect, Color, Brush, Application, MessageBox, Clipboard...). Use explicit aliases / fully-qualified names. Remove `System.Windows.Forms` and `System.Drawing` from implicit usings if needed (`<Using Remove="System.Windows.Forms" />`, `<Using Remove="System.Drawing" />`).
  - NuGet: `System.Security.Cryptography.ProtectedData`, `System.Speech`.
- `app.manifest`: `<dpiAwareness>PerMonitorV2</dpiAwareness>` + `<dpiAware>true/pm</dpiAware>`, Windows 10 supportedOS.
- **Everything internal uses physical screen pixels** (hooks, capture, OCR rects, window placement). Place WPF windows with Win32 `SetWindowPos` in physical pixels (helper below), not via WPF Left/Top.
- No external assets: tray icon is drawn at runtime with System.Drawing (32x32 rounded blue square #3B6FF5, white "译", Microsoft YaHei UI bold) → `Icon.FromHandle(bmp.GetHicon())`.

## 3. Files

```
AIMouse.csproj, app.manifest, App.xaml, App.xaml.cs
Native/Win32.cs            P/Invoke + constants
Native/WindowHelper.cs     ex-styles, capture exclusion, physical placement, monitor info
Input/InputHook.cs         hook thread + gesture state machine
Capture/ScreenCapture.cs   physical rect -> System.Drawing.Bitmap / SoftwareBitmap
Ocr/OcrService.cs          Windows OCR -> words/lines in screen coords
Text/Segmenter.cs          lines -> paragraphs -> sentences (+ per-line fragment rects)
Translate/ITranslator.cs, DeepSeekTranslator.cs, MockTranslator.cs, TranslationCache.cs
Core/Config.cs, Core/WordBook.cs, Core/Log.cs, Core/Controller.cs, Core/RegionSession.cs
UI/RectWindow.cs           click-through colored rectangle window (pooled)
UI/BadgeWindow.cs          the 译 cursor pill
UI/WordCard.xaml(.cs)
UI/PanelWindow.xaml(.cs)
UI/SettingsWindow.xaml(.cs)
```

## 4. Native / window rules

Constants: `WH_KEYBOARD_LL=13, WH_MOUSE_LL=14, WM_KEYDOWN=0x100, WM_KEYUP=0x101, WM_SYSKEYDOWN=0x104, WM_SYSKEYUP=0x105, WM_MOUSEMOVE=0x200, WM_LBUTTONDOWN=0x201, WM_LBUTTONUP=0x202, WM_RBUTTONDOWN=0x204, WM_MBUTTONDOWN=0x207, WM_MOUSEWHEEL=0x20A, WM_XBUTTONDOWN=0x20B, WM_MOUSEHWHEEL=0x20E, WM_TIMER=0x113, WM_QUIT=0x12, WM_APP=0x8000, VK_LCONTROL=0xA2, VK_RCONTROL=0xA3, VK_CONTROL=0x11, VK_ESCAPE=0x1B, GWL_EXSTYLE=-20, WS_EX_TRANSPARENT=0x20, WS_EX_TOOLWINDOW=0x80, WS_EX_LAYERED=0x80000, WS_EX_NOACTIVATE=0x08000000, WDA_EXCLUDEFROMCAPTURE=0x11, HWND_TOPMOST=-1, SWP_NOACTIVATE=0x10, SWP_SHOWWINDOW=0x40, MONITOR_DEFAULTTONEAREST=2, MOUSEEVENTF_LEFTDOWN=0x2, MOUSEEVENTF_LEFTUP=0x4, INPUT_MOUSE=0`.
Structs: `KBDLLHOOKSTRUCT{uint vkCode,scanCode,flags,time; IntPtr dwExtraInfo}`, `MSLLHOOKSTRUCT{POINT pt; uint mouseData,flags,time; IntPtr dwExtraInfo}`, `MONITORINFO{int cbSize; RECT rcMonitor, rcWork; uint dwFlags}`. Use `GetDpiForMonitor` (shcore.dll, MDT_EFFECTIVE_DPI=0). `SetWindowLongPtr`/`GetWindowLongPtr` (64-bit).

Every window we create (badge, rect, card, panel): `WindowStyle=None, AllowsTransparency=true, Background=Transparent, Topmost=true, ShowInTaskbar=false, ShowActivated=false, ResizeMode=NoResize` (panel: CanResizeWithGrip). In `SourceInitialized`:
- add `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` (RectWindow and BadgeWindow also `WS_EX_TRANSPARENT | WS_EX_LAYERED` = click-through);
- `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)` so our windows never appear in our own screen captures.

`WindowHelper.PlacePhysical(Window w, int x, int y, int width, int height)` → `SetWindowPos(hwnd, HWND_TOPMOST, x, y, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW)`. Ensure the HWND exists first (`new WindowInteropHelper(w).EnsureHandle()`), and call `Show()` before placing.
`WindowHelper.GetMonitor(POINT)` → record `(RECT bounds, RECT work, double scale)` where scale = dpi/96.
`WindowHelper.GetPhysicalRect(Window)` via `GetWindowRect`.

## 5. Input hook (Input/InputHook.cs) — most critical

Runs on its **own background thread** with its own Win32 message loop (`GetMessage`), not the WPF thread, so the mouse never lags. Install both LL hooks there (`SetWindowsHookEx(id, proc, GetModuleHandle(null), 0)`), keep delegates in fields. A `SetTimer(IntPtr.Zero, 0, 30, IntPtr.Zero)` thread timer drives arming and hover detection. Stop: `PostThreadMessage(threadId, WM_QUIT)` then unhook. Hook callbacks must be fast: never block, never touch WPF; raise events by `Application.Current.Dispatcher.BeginInvoke`.

`const nint OurMagic = 0x41494D53;` Events whose `dwExtraInfo == OurMagic` are ours → `CallNextHookEx` immediately. (Do NOT ignore all injected events — external automation must still work.)

State: `ctrlDown, ctrlDownTick, ctrlAlone, ctrlUsedMouse, armed, lastTapUpTick, swallowing, dragging, downPt, lastPt, stillSincePt, stillSinceTick, hoverFired, paused`.

Keyboard:
- Ctrl down (VK_LCONTROL/VK_RCONTROL/VK_CONTROL, WM_KEYDOWN or WM_SYSKEYDOWN): if `!ctrlDown` → `ctrlDown=true; ctrlDownTick=now; ctrlAlone=true; ctrlUsedMouse=false`. Auto-repeat downs ignored.
- Any other key down while ctrlDown → `ctrlAlone=false`; if armed → disarm (raise `ArmedChanged(false)`).
- Esc down → raise `Escape` (always pass through).
- Ctrl up: if `ctrlAlone && !ctrlUsedMouse && now-ctrlDownTick < 300` it is a **tap**: if `now-lastTapUpTick < 400` → raise `DoubleTapCtrl`, `lastTapUpTick=0`; else `lastTapUpTick=now`. Then `ctrlDown=false`; if armed → disarm.
- Never swallow keys.

Timer tick (30 ms): if `!paused && ctrlDown && ctrlAlone && !armed && now-ctrlDownTick >= Config.HoldDelayMs (180)` → `armed=true`, raise `ArmedChanged(true)` with cursor pos. If armed && !dragging && cursor stayed within 3 px since `stillSinceTick` for ≥150 ms && !hoverFired → raise `HoverStill(pt)`, `hoverFired=true`.

Mouse:
- MOUSEMOVE: `lastPt=pt`; raise `MouseMoved(pt)` throttled to ≥15 ms apart. If moved >3 px from `stillSincePt` → reset `stillSincePt/stillSinceTick`, `hoverFired=false`. If `swallowing && !dragging && dist(pt,downPt)>5` → `dragging=true`, raise `DragStarted(downPt)`. If dragging → raise `DragMoved(downPt, pt)` (throttled 15 ms). Never swallow moves.
- LBUTTONDOWN: if armed → `swallowing=true; downPt=pt; ctrlUsedMouse=true;` **return 1** (swallow).
- LBUTTONUP: if swallowing → `swallowing=false`; if dragging → `dragging=false`, raise `DragEnded(rect(downPt, pt))`; else → re-inject a click: `PostThreadMessage(threadId, WM_APP+1)`; in the message loop handle WM_APP+1 by `SendInput` LEFTDOWN then LEFTUP with `dwExtraInfo=OurMagic`. **return 1**.
- Wheel / right / middle / X buttons: pass through, set `ctrlUsedMouse=true` if ctrlDown.
- Esc while dragging → cancel: `dragging=false` (keep swallowing until the button-up, then do nothing), raise `DragCancelled`.

Public API: `Start()`, `Stop()`, `bool Paused {set;}`, events `ArmedChanged(bool armed, POINT pt)`, `HoverStill(POINT)`, `MouseMoved(POINT)`, `DragStarted(POINT)`, `DragMoved(POINT a, POINT b)`, `DragEnded(RECT)`, `DragCancelled`, `DoubleTapCtrl`, `Escape`. Events are raised on the WPF UI thread.

## 6. Capture + OCR

`ScreenCapture.Capture(RECT r)` → `System.Drawing.Bitmap` (Format32bppArgb) via `Graphics.CopyFromScreen` (process is PMv2-aware so coords are physical).

`OcrService` (singleton, created once): `OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"))`, fallback `TryCreateFromUserProfileLanguages()`.
`Task<OcrPage> RecognizeAsync(RECT screenRect, double upscale)`:
1. capture; if upscale>1 draw into a larger bitmap with `InterpolationMode.HighQualityBicubic`;
2. convert: LockBits → byte[] (BGRA) → `SoftwareBitmap.CreateCopyFromBuffer(CryptographicBuffer.CreateFromByteArray(bytes), BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Premultiplied)`;
3. `engine.RecognizeAsync(sb)`; map each word's `BoundingRect` back to screen: `x = r.Left + bx/upscale` etc.
Returns `OcrPage { List<OcrLineM> Lines }`, `OcrLineM { List<OcrWordM> Words; RECT Bounds (union); string Text }`, `OcrWordM { string Text; RECT Bounds }`.
Upscale rule: hover crops use 2.0; regions: `max(w,h)*2 <= OcrEngine.MaxImageDimension && w*h <= 2_500_000 ? 2.0 : (max(w,h)*1.5 <= MaxImageDimension ? 1.5 : 1.0)`.
Run capture+OCR inside `Task.Run` (do not block UI).

## 7. Segmenter (Text/Segmenter.cs)

Input: OcrPage lines (keep OCR order). Output `List<Sentence>`; `Sentence { string En; List<RECT> Fragments }` (one fragment per source line = union of that sentence's words on that line).
1. **Paragraphs**: line i+1 joins line i's paragraph if `gap = next.Top - cur.Bottom < 0.9 * avgLineHeight(cur,next)` AND horizontal ranges overlap (or `|left diff| < 3*lineHeight`) AND line heights differ < 40%. Otherwise new paragraph.
2. Join words with single spaces. Line-end hyphenation: a word ending with `-` whose next word (next line) starts lowercase → merge without the hyphen.
3. **Sentence split** after a word ending with `.`, `!`, `?` (optionally followed by `"`, `'`, `)`, `”`, `’`) when the next word starts with an uppercase letter, digit, or quote, unless the word (lowercased, without trailing quotes) is an abbreviation: `mr. mrs. ms. dr. prof. sr. jr. st. vs. etc. e.g. i.e. u.s. u.k. fig. no. inc. ltd. co. a.m. p.m. jan. feb. aug. sept. oct. nov. dec.` or is a single uppercase letter + `.` (initial).
4. Drop sentences with no ASCII letters.

## 8. Translator

`ITranslator`:
- `Task TranslateAsync(IReadOnlyList<string> segments, Action<int,string> onPartial, CancellationToken ct)` — onPartial(index, zhSoFar) may be called many times (streaming); raised on any thread (callers marshal to UI).
- `Task<WordInfo> LookupAsync(string word, string sentence, CancellationToken ct)`; `WordInfo { Word, Phonetic, Pos, Meaning, Note, SentenceZh }`.
- `Task<string> TestAsync(CancellationToken ct)` → translates "Hello, nice to meet you." for the settings test button.

`DeepSeekTranslator`: `POST {BaseUrl}/chat/completions` (default `https://api.deepseek.com`), header `Authorization: Bearer {key}`, JSON body:
`{ "model": Config.Model (default "deepseek-flash"), "messages": [...], "stream": true/false, "temperature": 0.3, "thinking": {"type":"disabled"} }`.
If the server returns HTTP 400 and the body mentions `thinking`, retry once without the `thinking` field and remember (static flag) not to send it again.
Streaming: read SSE lines `data: {...}` until `data: [DONE]`; append `choices[0].delta.content` (ignore `reasoning_content`).
Batch translation prompt (system):
> You are a professional English→Simplified Chinese translator helping a Chinese student read English. Translate every numbered segment into natural, accurate Simplified Chinese. Output exactly one line per segment in the form `[n] 译文`, same numbers, same order, no merging, no skipping, no extra text. Keep names, code, URLs, numbers as they are. If a segment is a UI label or fragment, still translate it briefly.
User: segments as `[1] ...\n[2] ...`.
Parse while streaming: split the accumulated text by `\n`; for each line (including the unfinished last line) match `^\s*\[(\d+)\]\s*(.*)$` → `onPartial(n-1, text)`. Segments missing at the end → one retry request with only the missing ones; still missing → `"（翻译失败）"`.
Batching: ≤30 segments or ≤2500 chars per request, up to 3 requests concurrently, all sharing the CancellationToken.
Lookup prompt (non-stream, max_tokens 300): ask for **only** a JSON object
`{"word":"<dictionary form>","phonetic":"<US IPA with slashes>","pos":"<adj./n./v./...>","meaning":"<Chinese meaning in THIS sentence, ≤12 chars>","note":"<optional ≤24-char Chinese hint, e.g. common other meaning or phrase>","sentence_zh":"<Chinese translation of the sentence>"}`; parse the first `{...}` block with System.Text.Json (case-insensitive).
Errors → throw `TranslateException` with Chinese message: 401 "API 密钥不正确，请在设置里检查", 402 "DeepSeek 余额不足", 429 "请求太频繁，请稍后再试", network/timeout "网络连接失败", missing key "请先在设置里填写 DeepSeek API 密钥". HttpClient timeout 60 s, single static instance.

`MockTranslator` (used when `--mock` arg or Config.Mock): translate → streams `"（模拟）" + first 30 chars of the segment` char by char, 15 ms/char; lookup → returns fake WordInfo after 300 ms. Lets the app be tested without a key.

`TranslationCache`: in-memory `Dictionary<string,string>` keyed by whitespace-normalized English, max 5000 entries (drop oldest).

## 9. Controller (Core/Controller.cs)

Owns: InputHook, OcrService, ITranslator, BadgeWindow, WordCard, a selection RectWindow, a pool of RectWindows, current `RegionSession?`.

- `ArmedChanged(true)`: show badge at pt. `MouseMoved` while armed: move badge (`PlacePhysical`, size ~ 30x20 DIP * scale). `ArmedChanged(false)`: hide badge; word card enters "linger" state: it hides when the cursor moves >60 px outside the card's rect, on Esc, or on any new action.
- `HoverStill(pt)`:
  - if pt is inside the panel window → ask panel `TryGetWordAt(pt)` (see §11); if it returns (word, sentence, wordRect) use those, skip OCR. If inside the word card → ignore.
  - else capture rect centered on pt: `w=round(420*s), h=round(90*s)` clipped to the monitor, OCR with upscale 2. Find the word whose bounds (inflated 2 px) contain pt; else nearest word on a line whose vertical range contains pt.y within `8*s` px. Strip leading/trailing chars that are not letters/digits/`'`/`-`. No letters → hide card.
  - Same word + same line text as the current card → do nothing. Otherwise cancel previous lookup, show underline (RectWindow height `max(2, round(2*s))` at word bottom+1, color #3B6FF5) and show the card immediately with the word + "…", then fill when `LookupAsync` returns. Sentence = text of the OCR line (if the line looks cut, fine).
- `DragStarted/DragMoved`: selection RectWindow showing rect(a,b) — border 2 DIP #3B6FF5, fill #3B6FF5 at 10% alpha. `DragCancelled`/`Escape`: hide it. `Escape` also hides the card.
- `DragEnded(rect)`: hide selection; ignore if w or h < 12 px. Close current session; create `RegionSession(rect, Mode.Box)`.
- `DoubleTapCtrl`: if current session is FullScreen → close it; else close current and start `RegionSession(monitorBoundsUnderCursor, Mode.FullScreen)`.
- `MouseMoved` when not armed and a session exists → `session.OnMouseOver(pt)`.
- Paused (tray) → hook.Paused = true, close card/badge.

## 10. RegionSession (Core/RegionSession.cs)

Modes: `Box`, `Live`, `FullScreen` (FullScreen is always live).
1. Status "识别中…"; OCR region (upscale rule §6); segment.
2. **Box mode with exactly 1 sentence and the region height < 3 line heights** → no panel: show the WordCard in "phrase" layout below the region (English phrase + streaming Chinese), done.
3. Otherwise show `PanelWindow`, items = sentences. Cached translations fill instantly, the rest stream in (status "翻译中 x/y", then "完成 · n 句"). Each item: `En`, `Zh` (empty → shows "…"), `Fragments`.
4. Region frame: a RectWindow drawn 3 px outside the region: border 1.5 DIP #3B6FF5 dashed (Box) or solid (Live); no frame for FullScreen.
5. **Highlights (the learning link)**: hovering a panel item → RectWindows around that sentence's fragments (border 2 DIP #FFB000, fill #FFB000 at 12% alpha, inflated 2 px); leave → hide. `OnMouseOver(pt)`: if pt is inside a fragment of sentence k → same on-screen highlight + panel item k gets highlighted background and is scrolled into view (throttle 50 ms; clear when pt leaves).
6. **Live** (Live/FullScreen): every 700 ms take a cheap fingerprint of the region (capture, downscale to 64x64 grayscale, compare mean abs difference). If changed > threshold (2.0) set `dirty`; when dirty and the fingerprint equals the previous tick (content stopped moving) → re-run steps 1–3 replacing items (cache keeps old translations so only new sentences call DeepSeek), cancel in-flight requests of the previous run. Skip while a run is in progress.
7. Panel buttons: 📌 toggles Box↔Live (hidden for FullScreen), 📋 copies all as `English\n中文\n\n`, ✕ closes the session (hide panel, frame, highlights, stop timer, cancel requests).
8. Errors (TranslateException) → panel status shows the Chinese message in red.

Panel placement (physical px, monitor work area `wa` of the region center, scale `s`): `W=round(380*s)`, `H=clamp(region.Height, 260*s, 0.8*wa.Height)`. Right side if `region.Right+12*s+W <= wa.Right`; else left if `region.Left-12*s-W >= wa.Left`; else `x=wa.Right-W-8*s`. `y=clamp(region.Top, wa.Top, wa.Bottom-H)`. FullScreen: `x=wa.Right-W-8*s, y=wa.Top+8*s, H=wa.Height-16*s`. After the user drags/resizes the panel, keep their position for the session.

## 11. Windows

**BadgeWindow**: rounded pill (corner 9), background #3B6FF5, white text "译" 12px bold Microsoft YaHei UI, size 26x18 DIP; click-through.

**RectWindow**: `Border` with configurable stroke color/thickness/dash/fill; click-through; `Show(RECT physical)`, `Hide()`.

**WordCard** (not click-through, non-activating): rounded 10, background #FFFFFF @97%, border #D5DCE8, drop shadow, padding 12, max width 360 DIP. Layout:
- Row 1: word (18px bold #111827) · phonetic (13px #6B7280) · 🔊 button · ⭐ button (Segoe MDL2 Assets glyphs: speaker E767, star E734 / filled E735).
- Row 2: pos (13px #3B6FF5) + meaning (16px #111827, Microsoft YaHei UI).
- Row 3: note (12px #6B7280), hidden if empty.
- Row 4: separator + sentence_zh (12px #4B5563), hidden if empty.
- Loading: row 2 shows "…"; error: row 2 shows error text in #DC2626.
- Phrase layout (box with one sentence): English (13px #6B7280) above, Chinese (16px #111827) streaming.
Placement: below the word's **line** bounds: `x = word.Left`, `y = line.Bottom + 6*s`; clamp horizontally into work area; if it doesn't fit below → `y = line.Top - cardH - 6*s`. Measure the card (`UpdateLayout`, ActualWidth/Height * s) before placing; re-place after content changes size.
🔊 → `System.Speech.Synthesis.SpeechSynthesizer` with an installed en-US voice (`GetInstalledVoices()` where Culture starts with "en"); hide 🔊 if none. `SpeakAsyncCancelAll(); SpeakAsync(word)`.
⭐ → `WordBook.Add(...)`, star becomes filled.

**PanelWindow** (non-activating, resizable with grip, draggable by header via `DragMove()`): rounded 10, background #F7F9FC @97%, border #D5DCE8, drop shadow.
- Header (36 DIP): title ("框选翻译" / "实时翻译 ●" / "全屏翻译 ●", the dot #22C55E), status text (12px #6B7280), buttons 📌 (E718) 📋 (E8C8) ✕ (E711), flat 28x28 hover #E6ECF5.
- Body: ScrollViewer + ItemsControl (VirtualizingStackPanel). Item: English (12px #6B7280 Segoe UI, wrap) above Chinese (15px #111827 Microsoft YaHei UI, wrap), item padding 10x8, corner 6, highlighted background #FFF4D6.
- English text uses a read-only borderless transparent `TextBox` (TextWrapping=Wrap, IsReadOnly, Cursor=Arrow) so `TryGetWordAt(POINT physical)` can use `GetCharacterIndexFromPoint` to find the word under the cursor (convert physical→element coords via `PointFromScreen` — it expects physical screen px under PMv2). Return the word, the item's English sentence, and the word rect (physical, from `GetRectFromCharacterIndex`).
- Mouse enter/leave on items → session highlight callbacks.

**SettingsWindow** (normal activatable window, 460x520, centered): title "AI翻译鼠标 · 设置".
- DeepSeek API 密钥 (PasswordBox) + small link text "在 platform.deepseek.com 获取".
- 模型 (editable ComboBox: deepseek-flash, deepseek-v4-pro).
- 开机自动启动 (CheckBox → HKCU\Software\Microsoft\Windows\CurrentVersion\Run value "AIMouse" = `"<exe path>"`).
- [测试连接] → shows result/error below. [保存].
- A "使用方法" box listing the 4 gestures from §1 in plain Chinese.

## 12. App / Config / misc

- `App.OnStartup`: single instance (named Mutex `AIMouse_SingleInstance`, exit silently if taken); load config; create Controller; start hook; tray icon; if no API key (and not mock) → open SettingsWindow. Balloon on start: "AI翻译鼠标已启动：按住 Ctrl 指单词 / 按住 Ctrl 拖框 / 双击 Ctrl 全屏".
- `ShutdownMode = OnExplicitShutdown`. On exit: stop hook, dispose tray icon.
- Tray menu: 使用方法 (opens settings), 设置, 打开生词本 (opens csv with shell), 暂停 (checkable), 退出. Double-click tray → settings.
- `Config` at `%APPDATA%\AIMouse\config.json`: `ApiKeyProtected` (DPAPI `ProtectedData.Protect`, CurrentUser, base64), `BaseUrl`, `Model`, `HoldDelayMs`=180, `StartWithWindows`, `Mock`. If no key saved, fall back to env var `DEEPSEEK_API_KEY`.
- `WordBook` → `%APPDATA%\AIMouse\生词本.csv`, UTF-8 **with BOM**, header `日期,单词,音标,词性,意思,例句,例句翻译`, proper CSV quoting.
- `Log` → `%APPDATA%\AIMouse\log.txt` (append, timestamp). Hook `DispatcherUnhandledException` (log, mark handled), `TaskScheduler.UnobservedTaskException`, `AppDomain.UnhandledException`.
- **Self-test args** (no UI, exit after writing the file, used for automated testing):
  - `--selftest-ocr X Y W H` → OCR that physical screen rect, segment, write to `%TEMP%\aimouse_selftest.txt`: each line, then each sentence with fragment rects.
  - `--selftest-image <path>` → same for an image file (load with System.Drawing, skip capture).
  - `--mock` → use MockTranslator.

## 13. Done criteria

`dotnet build -c Release` → 0 errors, 0 warnings that indicate real bugs. App starts to tray, all gestures work, no focus stealing, normal Ctrl shortcuts unaffected.
