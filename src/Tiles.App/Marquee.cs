using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;
using System.Windows.Threading;
using Tiles.Core;

namespace Tiles.App;

/// <summary>
/// Выделение рамкой Windows: когда тянут синюю рамку по пустому столу, подсвечиваем папки и виджеты под ней.
/// За мышью следим только пока активен сам рабочий стол — в играх и других окнах слежки нет.
/// Слежка живёт в своём потоке, чтобы занятость окон программы никогда не тормозила мышь.
/// </summary>
internal static class Marquee
{
    private const int WH_MOUSE_LL = 14, WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202;
    private const int WM_APP_INSTALL = 0x8001, WM_APP_REMOVE = 0x8002, WM_QUIT = 0x12;
    private const uint EVENT_SYSTEM_FOREGROUND = 3, WINEVENT_OUTOFCONTEXT = 0;

    private delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
    private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);

    private static readonly HookProc MouseProc = OnMouse;
    private static readonly WinEventProc ForegroundProc = OnForeground;
    private static IntPtr _winEvent, _mouseHook;
    private static Thread? _thread;
    private static uint _threadId;
    private static Dispatcher? _ui;

    // общие для потока слежки и окон программы
    private static volatile bool _tracking;
    private static int _x, _y, _x0, _y0;

    // только в потоке окон
    private static readonly DispatcherTimer Frame = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(33) };
    private static bool _active, _dragging, _add;
    private static HashSet<string>? _keep;

    public static void Start()
    {
        _ui = Dispatcher.CurrentDispatcher;
        Frame.Tick += (_, _) => Tick();
        _thread = new Thread(Loop) { IsBackground = true, Name = "Плитки: рамка выделения" }; // lang-ok
        _thread.Start();
        _winEvent = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, ForegroundProc, 0, 0, WINEVENT_OUTOFCONTEXT);
        OnForeground(IntPtr.Zero, 0, GetForegroundWindow(), 0, 0, 0, 0);
    }

    public static void Stop()
    {
        if (_winEvent != IntPtr.Zero) UnhookWinEvent(_winEvent);
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }

    private static void Loop()
    {
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0); // завести очередь сообщений
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_APP_INSTALL && _mouseHook == IntPtr.Zero)
                _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, MouseProc, GetModuleHandle(null), 0);
            else if (msg.message == WM_APP_REMOVE && _mouseHook != IntPtr.Zero && !_tracking)
            { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        }
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
    }

    /// <summary>Сменилось активное окно: на рабочем столе — следим, иначе — нет.</summary>
    private static void OnForeground(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
    {
        bool desk = IsDesktop(hwnd) || IsOurs(hwnd);
        if (_threadId != 0) PostThreadMessage(_threadId, desk ? WM_APP_INSTALL : WM_APP_REMOVE, IntPtr.Zero, IntPtr.Zero);
        // стол стал активным от этого же нажатия — само нажатие слежка пропустила, начинаем отсюда
        if (desk && !_tracking && GetAsyncKeyState(0x01) < 0 && GetCursorPos(out var p) && IsDesktopView(WindowFromPoint(p)))
            Down(p.X, p.Y);
    }

    private static IntPtr OnMouse(int code, IntPtr w, IntPtr l)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(l);
            int msg = (int)w;
            if (msg == WM_LBUTTONDOWN && !_tracking && IsDesktopView(WindowFromPoint(info.pt))) Down(info.pt.X, info.pt.Y);
            else if (msg == WM_MOUSEMOVE && _tracking) { _x = info.pt.X; _y = info.pt.Y; }
            else if (msg == WM_LBUTTONUP && _tracking)
            {
                _x = info.pt.X; _y = info.pt.Y;
                _tracking = false;
                _ui?.BeginInvoke(Up);
            }
        }
        return CallNextHookEx(IntPtr.Zero, code, w, l);
    }

    private static void Down(int x, int y)
    {
        _x0 = _x = x; _y0 = _y = y;
        _tracking = true;
        bool ctrl = GetAsyncKeyState(0x11) < 0;
        _ui?.BeginInvoke(() => Begin(ctrl));
    }

    // ---- в потоке окон ----

    private static void Begin(bool ctrl)
    {
        // нажали на ярлык Windows — это перетаскивание ярлыка, а не рамка
        if (OnDesktopIcon(_x0, _y0)) { _active = false; return; }
        _active = true; _dragging = false; _add = ctrl;
        _keep = ctrl ? new HashSet<string>(App.Current.Selection) : null;
        Frame.Start();
    }

    private static void Tick()
    {
        if (!_active) { Frame.Stop(); return; }
        // отпускание мог не увидеть никто (слежку ставили в этот момент) — смотрим кнопку сами
        if (_tracking && GetAsyncKeyState(0x01) >= 0) { _tracking = false; Up(); return; }
        int x = _x, y = _y;
        if (!_dragging && Math.Abs(x - _x0) < 4 && Math.Abs(y - _y0) < 4) return;
        _dragging = true;
        double k = App.Current.DpiScale;
        var r = new RectD(Math.Min(x, _x0) / k, Math.Min(y, _y0) / k, Math.Abs(x - _x0) / k, Math.Abs(y - _y0) / k);
        App.Current.MarqueeSelect(r, _add, _keep);
    }

    private static void Up()
    {
        if (!_active) return;
        _active = false;
        Frame.Stop();
        _active = true;
        Tick();
        Frame.Stop();
        _active = false;
        if (!_dragging && !_add) App.Current.ClearSelection(); // клик по пустому столу снимает выделение
        if (_dragging) App.Current.Log($"marquee selected {App.Current.Selection.Count}");
        if (_threadId != 0 && !IsDesktop(GetForegroundWindow()) && !IsOurs(GetForegroundWindow()))
            PostThreadMessage(_threadId, WM_APP_REMOVE, IntPtr.Zero, IntPtr.Zero);
    }

    private static bool OnDesktopIcon(int x, int y)
    {
        try
        {
            using var snap = DesktopIcons.Read();
            if (snap == null) return false;
            Native.GetWindowRect(Native.DesktopListView(), out var lv);
            double vx = x - lv.Left, vy = y - lv.Top;
            return snap.Icons.Any(i => vx >= i.Pos.X && vx < i.Pos.X + snap.SpacingX && vy >= i.Pos.Y && vy < i.Pos.Y + snap.SpacingY);
        }
        catch { return false; }
    }

    // ---- окна ----

    private static string ClassOf(IntPtr h)
    {
        var sb = new StringBuilder(64);
        return GetClassName(h, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    private static bool IsDesktop(IntPtr h) => ClassOf(h) is "Progman" or "WorkerW";

    private static bool IsOurs(IntPtr h)
    {
        GetWindowThreadProcessId(h, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    /// <summary>Под курсором список ярлыков рабочего стола (а не окно поверх).</summary>
    private static bool IsDesktopView(IntPtr h)
    {
        if (ClassOf(h) != "SysListView32") return false;
        var parent = GetParent(h);
        return ClassOf(parent) == "SHELLDLL_DefView" && IsDesktop(GetParent(parent));
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public int message; public IntPtr w, l; public uint time; public POINT pt; }

    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG msg, IntPtr h, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
}
