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

                if (isCtrl)
                {
                    if (down && !_ctrlDown)
                    {
                        _ctrlDown = true;
                        _ctrlDownTick = now;
                        _ctrlAlone = true;
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
