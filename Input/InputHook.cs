using System.Runtime.InteropServices;
using System.Windows.Threading;
using AIMouse.Native;
using static AIMouse.Native.Win32;

namespace AIMouse.Input;

/// <summary>
/// Global keyboard + mouse hooks on a dedicated thread. Turns "hold Ctrl alone" into AI-mouse gestures.
/// All events are raised on the WPF UI thread.
/// </summary>
public sealed class InputHook : IDisposable
{
    public static readonly IntPtr OurMagic = new(0x41494D53);
    const uint WM_APP_CLICK = WM_APP + 1;

    public event Action<bool, POINT>? ArmedChanged;
    public event Action<POINT>? HoverStill;
    public event Action<POINT>? MouseMoved;
    /// <summary>bool = copy (right-button drag) instead of translate (left-button drag).</summary>
    public event Action<POINT, bool>? DragStarted;
    public event Action<POINT, POINT>? DragMoved;
    public event Action<RECT, bool>? DragEnded;
    public event Action? DragCancelled;
    public event Action? DoubleTapCtrl;
    public event Action? Escape;
    /// <summary>Push-to-talk: the talk key combination (default Ctrl+Win) went fully down / was released.</summary>
    public event Action? TalkDown, TalkUp;
    public Func<bool> TalkEnabled { get; set; } = () => true;
    public volatile HotKey TalkKey = HotKey.Parse(HotKey.Default);
    bool _talkDown;
    readonly HashSet<uint> _held = new();
    readonly HashSet<uint> _swallowedDown = new();   // main keys whose "down" we ate, so we eat their "up" too
    bool _maskWin, _maskAlt;                           // a talk with Win/Alt happened: hide their release from Windows
    const uint VK_MASK = 0xE8;                         // unassigned key, injected so Win/Alt release doesn't open Start / menus

    // ---- key capture for the Settings "change key" button ----
    Action<HotKey?>? _capture;
    readonly List<uint> _captureKeys = new();
    /// <summary>Next key combination pressed anywhere is reported (null = Esc, cancelled) instead of reaching apps.</summary>
    public void BeginCapture(Action<HotKey?> done)
    {
        _captureKeys.Clear();
        _capture = done;
    }
    public void CancelCapture() => _capture = null;

    public volatile bool Paused;
    public volatile int HoldDelayMs = 180;

    readonly Dispatcher _ui;
    Thread? _thread;
    uint _threadId;
    IntPtr _kbHook, _msHook;
    LowLevelProc? _kbProc, _msProc;

    // ---- state, touched only on the hook thread ----
    bool _ctrlDown, _ctrlAlone, _ctrlUsedMouse, _armed;
    bool _swallowing, _swallowRight, _dragging, _dragCancelled, _hoverFired;
    long _ctrlDownTick, _lastTapUpTick, _stillSinceTick, _lastMoveRaise, _lastDragRaise;
    POINT _downPt, _lastPt, _stillSincePt;

    public InputHook(Dispatcher ui) { _ui = ui; }

    public void Start()
    {
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "AIMouse.InputHook" };
        _thread.Start();
        ready.Wait(3000);
    }

    public void Stop()
    {
        if (_thread == null) return;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _thread = null;
    }

    public void Dispose() => Stop();

    void Run(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE); // make sure the thread has a message queue
        _kbProc = KbProc;
        _msProc = MsProc;
        var hMod = GetModuleHandle(null);
        _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, hMod, 0);
        _msHook = SetWindowsHookEx(WH_MOUSE_LL, _msProc, hMod, 0);
        if (_kbHook == IntPtr.Zero || _msHook == IntPtr.Zero)
            Core.Log.Write($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
        GetCursorPos(out _lastPt);
        _stillSincePt = _lastPt;
        _stillSinceTick = Environment.TickCount64;
        var timer = SetTimer(IntPtr.Zero, UIntPtr.Zero, 30, IntPtr.Zero);
        ready.Set();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_TIMER) OnTick();
            else if (msg.message == WM_APP_CLICK) InjectClick(msg.wParam != IntPtr.Zero);
            else
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }

        KillTimer(IntPtr.Zero, timer);
        if (_kbHook != IntPtr.Zero) UnhookWindowsHookEx(_kbHook);
        if (_msHook != IntPtr.Zero) UnhookWindowsHookEx(_msHook);
    }

    void Raise(Action a) => _ui.BeginInvoke(DispatcherPriority.Normal, a);

    void Disarm()
    {
        if (!_armed) return;
        _armed = false;
        var p = _lastPt;
        Raise(() => ArmedChanged?.Invoke(false, p));
    }

    void OnTick()
    {
        long now = Environment.TickCount64;
        if (_swallowing && GetCursorPos(out var cur) && (cur.X != _lastPt.X || cur.Y != _lastPt.Y))
            HandleMove(cur, now);
        if (!_armed && _ctrlDown && _ctrlAlone && !Paused && now - _ctrlDownTick >= HoldDelayMs)
        {
            _armed = true;
            _hoverFired = false;
            GetCursorPos(out var p);
            if (Math.Abs(p.X - _stillSincePt.X) > 3 || Math.Abs(p.Y - _stillSincePt.Y) > 3)
            {
                _stillSincePt = p;
                _stillSinceTick = now;
            }
            _lastPt = p;
            Raise(() => ArmedChanged?.Invoke(true, p));
        }
        if (_armed && !_swallowing && !_hoverFired && now - _stillSinceTick >= 150)
        {
            _hoverFired = true;
            var p = _lastPt;
            Raise(() => HoverStill?.Invoke(p));
        }
    }

    IntPtr KbProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (k.dwExtraInfo != OurMagic)
            {
                int msg = (int)wParam;
                bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                bool up = msg == WM_KEYUP || msg == WM_SYSKEYUP;
                bool isCtrl = k.vkCode == VK_LCONTROL || k.vkCode == VK_RCONTROL || k.vkCode == VK_CONTROL;
                long now = Environment.TickCount64;

                if (down) _held.Add(k.vkCode);
                else if (up) _held.Remove(k.vkCode);

                // Settings is waiting for a new talk key: eat everything until the first key is released.
                if (_capture is { } cap)
                {
                    if (down)
                    {
                        if (k.vkCode == VK_ESCAPE && _captureKeys.Count == 0)
                        {
                            _capture = null;
                            Raise(() => cap(null));
                        }
                        else if (!_captureKeys.Contains(k.vkCode)) _captureKeys.Add(k.vkCode);
                    }
                    else if (up && _captureKeys.Count > 0)
                    {
                        _capture = null;
                        var hk = HotKey.FromVks(_captureKeys.ToList());
                        Raise(() => cap(hk));
                        // apps never saw these keys go down: eat their release too
                        foreach (var v in _captureKeys) if (v != k.vkCode && _held.Contains(v)) _swallowedDown.Add(v);
                    }
                    return (IntPtr)1;
                }

                var talk = TalkKey;
                if (isCtrl && talk.OwnsCtrlKey(k.vkCode)) isCtrl = false; // e.g. right Ctrl is the talk key: not a translate gesture

                if (down && !_talkDown && talk.Uses(k.vkCode) && talk.AllHeld(_held) && TalkEnabled() && !Paused && !_dragging)
                {
                    _talkDown = true;
                    _maskWin |= talk.HasWin;
                    _maskAlt |= talk.HasAlt;
                    _ctrlAlone = false;
                    Disarm();
                    Raise(() => TalkDown?.Invoke());
                }
                if (down && _swallowedDown.Contains(k.vkCode) && !talk.Uses(k.vkCode)) return (IntPtr)1; // key repeat after a capture
                if (down && talk.IsMainKey(k.vkCode) && (_talkDown || _swallowedDown.Contains(k.vkCode)))
                {
                    _swallowedDown.Add(k.vkCode);
                    return (IntPtr)1;   // the app never sees the main key of the combination
                }
                if (up)
                {
                    if (_talkDown && talk.Uses(k.vkCode))
                    {
                        _talkDown = false;
                        Raise(() => TalkUp?.Invoke());
                    }
                    if (_swallowedDown.Remove(k.vkCode)) return (IntPtr)1;
                    bool isWin = k.vkCode is 0x5B or 0x5C, isAlt = k.vkCode is 0xA4 or 0xA5;
                    if ((isWin && _maskWin) || (isAlt && _maskAlt))
                    {
                        if (isWin) _maskWin = false; else _maskAlt = false;
                        MaskRelease(k.vkCode);
                        return (IntPtr)1;
                    }
                }

                if (isCtrl)
                {
                    if (down && !_ctrlDown)
                    {
                        _ctrlDown = true;
                        _ctrlDownTick = now;
                        _ctrlAlone = !_talkDown;
                        _ctrlUsedMouse = false;
                    }
                    else if (up && _ctrlDown)
                    {
                        if (_ctrlAlone && !_ctrlUsedMouse && now - _ctrlDownTick < 300)
                        {
                            if (_lastTapUpTick != 0 && now - _lastTapUpTick < 400)
                            {
                                _lastTapUpTick = 0;
                                if (!Paused) Raise(() => DoubleTapCtrl?.Invoke());
                            }
                            else _lastTapUpTick = now;
                        }
                        else _lastTapUpTick = 0;
                        _ctrlDown = false;
                        Disarm();
                    }
                }
                else if (down)
                {
                    if (_ctrlDown)
                    {
                        _ctrlAlone = false;
                        Disarm();
                    }
                    _lastTapUpTick = 0;
                    if (k.vkCode == VK_ESCAPE)
                    {
                        if (_dragging)
                        {
                            _dragging = false;
                            _dragCancelled = true;
                            Raise(() => DragCancelled?.Invoke());
                        }
                        Raise(() => Escape?.Invoke());
                    }
                }
            }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    void HandleMove(POINT pt, long now)
    {
        _lastPt = pt;
        if (Math.Abs(pt.X - _stillSincePt.X) > 3 || Math.Abs(pt.Y - _stillSincePt.Y) > 3)
        {
            _stillSincePt = pt;
            _stillSinceTick = now;
            _hoverFired = false;
        }
        if (now - _lastMoveRaise >= 15)
        {
            _lastMoveRaise = now;
            Raise(() => MouseMoved?.Invoke(pt));
        }
        if (_swallowing && !_dragCancelled)
        {
            int dx = pt.X - _downPt.X, dy = pt.Y - _downPt.Y;
            if (!_dragging && dx * dx + dy * dy > 25)
            {
                _dragging = true;
                var d = _downPt;
                bool copy = _swallowRight;
                Raise(() => DragStarted?.Invoke(d, copy));
            }
            if (_dragging && now - _lastDragRaise >= 15)
            {
                _lastDragRaise = now;
                var a = _downPt;
                Raise(() => DragMoved?.Invoke(a, pt));
            }
        }
    }

    IntPtr MsProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var m = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if (m.dwExtraInfo != OurMagic)
            {
                int msg = (int)wParam;
                long now = Environment.TickCount64;
                var pt = m.pt;
                switch (msg)
                {
                    case WM_MOUSEMOVE:
                        HandleMove(pt, now);
                        break;

                    case WM_LBUTTONDOWN:
                    case WM_RBUTTONDOWN:
                        if (_armed && !_swallowing)
                        {
                            _swallowing = true;
                            _swallowRight = msg == WM_RBUTTONDOWN;
                            _dragging = false;
                            _dragCancelled = false;
                            _downPt = pt;
                            _ctrlUsedMouse = true;
                            return new IntPtr(1);
                        }
                        if (_ctrlDown) _ctrlUsedMouse = true;
                        break;

                    case WM_LBUTTONUP:
                    case WM_RBUTTONUP:
                        if (_swallowing && (msg == WM_RBUTTONUP) == _swallowRight)
                        {
                            _swallowing = false;
                            bool right = _swallowRight;
                            int ux = pt.X - _downPt.X, uy = pt.Y - _downPt.Y;
                            if (!_dragging && !_dragCancelled && ux * ux + uy * uy > 25) _dragging = true;
                            if (!_dragCancelled)
                            {
                                if (_dragging)
                                {
                                    var r = RECT.FromPoints(_downPt, pt);
                                    Raise(() => DragEnded?.Invoke(r, right));
                                }
                                else
                                {
                                    PostThreadMessage(_threadId, WM_APP_CLICK, right ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero);
                                }
                            }
                            _dragging = false;
                            _dragCancelled = false;
                            _hoverFired = true; // no word lookup until the mouse moves again
                            return new IntPtr(1);
                        }
                        break;

                    case WM_MBUTTONDOWN:
                    case WM_MOUSEWHEEL:
                    case WM_MOUSEHWHEEL:
                    case WM_XBUTTONDOWN:
                        if (_ctrlDown) _ctrlUsedMouse = true;
                        break;
                }
            }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    /// <summary>Release Win/Alt without Windows reacting (no Start menu, no menu bar): a dummy key goes first.</summary>
    static void MaskRelease(uint vk)
    {
        var inputs = new INPUT[3];
        inputs[0].type = inputs[1].type = inputs[2].type = INPUT_KEYBOARD;
        inputs[0].U.ki = new KEYBDINPUT { wVk = (ushort)VK_MASK, dwExtraInfo = OurMagic };
        inputs[1].U.ki = new KEYBDINPUT { wVk = (ushort)VK_MASK, dwFlags = KEYEVENTF_KEYUP, dwExtraInfo = OurMagic };
        inputs[2].U.ki = new KEYBDINPUT { wVk = (ushort)vk, dwFlags = KEYEVENTF_KEYUP, dwExtraInfo = OurMagic };
        SendInput(3, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>Paste into whatever app has the keyboard focus (Ctrl+V), marked as ours so the hook ignores it.</summary>
    public static void SendPaste()
    {
        var inputs = new INPUT[4];
        uint[] vk = { VK_LCONTROL, VK_V, VK_V, VK_LCONTROL };
        for (int i = 0; i < 4; i++)
        {
            inputs[i].type = INPUT_KEYBOARD;
            inputs[i].U.ki = new KEYBDINPUT { wVk = (ushort)vk[i], dwFlags = i >= 2 ? KEYEVENTF_KEYUP : 0, dwExtraInfo = OurMagic };
        }
        SendInput(4, inputs, Marshal.SizeOf<INPUT>());
    }

    void InjectClick(bool right)
    {
        var inputs = new INPUT[2];
        inputs[0].type = INPUT_MOUSE;
        inputs[0].U.mi = new MOUSEINPUT { dwFlags = right ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN, dwExtraInfo = OurMagic };
        inputs[1].type = INPUT_MOUSE;
        inputs[1].U.mi = new MOUSEINPUT { dwFlags = right ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP, dwExtraInfo = OurMagic };
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }
}
