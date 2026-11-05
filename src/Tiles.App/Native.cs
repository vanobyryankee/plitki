using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tiles.Core;

namespace Tiles.App;

internal static class Native
{
    // ---- рабочий стол ----
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? cls, string? title);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    public static IntPtr Progman => FindWindow("Progman", null);

    /// <summary>Окно «принадлежит» рабочему столу: не прячется по Win+D, нет в Alt+Tab.</summary>
    public static void PinToDesktop(Window w)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        const int GWL_EXSTYLE = -20, GWLP_HWNDPARENT = -8;
        const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr((ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW));
        SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, Progman);
        SendToBottom(hwnd);
        HwndSource.FromHwnd(hwnd)?.AddHook(KeepAtBottom);
    }

    public static void SendToBottom(IntPtr hwnd)
        => SetWindowPos(hwnd, new IntPtr(1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // NOSIZE|NOMOVE|NOACTIVATE

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }

    /// <summary>Только для замера плавности: окно поверх всех, чтобы его было видно на снимке экрана.</summary>
    public static bool BenchOnTop;

    private static IntPtr KeepAtBottom(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        const int WM_WINDOWPOSCHANGING = 0x46;
        Diag.Msg(msg);
        if (msg == WM_WINDOWPOSCHANGING && !BenchOnTop)
        {
            var pos = Marshal.PtrToStructure<WINDOWPOS>(l);
            if ((pos.flags & 0x0004) == 0) // SWP_NOZORDER не стоит — значит, окно пытаются поднять
            {
                pos.hwndInsertAfter = new IntPtr(1); // HWND_BOTTOM
                Marshal.StructureToPtr(pos, l, false);
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>Шаг сетки ярлыков рабочего стола в физических пикселях.</summary>
    public static (int W, int H)? DesktopIconSpacing()
    {
        var view = DesktopListView();
        if (view == IntPtr.Zero) return null;
        long v = SendMessage(view, 0x1000 + 51, IntPtr.Zero, IntPtr.Zero).ToInt64(); // LVM_GETITEMSPACING
        int cx = (int)(v & 0xFFFF), cy = (int)((v >> 16) & 0xFFFF);
        return cx > 0 && cy > 0 ? (cx, cy) : null;
    }

    /// <summary>Список ярлыков рабочего стола (окно проводника).</summary>
    public static IntPtr DesktopListView()
    {
        IntPtr view = IntPtr.Zero;
        var defView = FindWindowEx(Progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defView == IntPtr.Zero)
        {
            IntPtr worker = IntPtr.Zero;
            while ((worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
            {
                defView = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView != IntPtr.Zero) break;
            }
        }
        if (defView != IntPtr.Zero) view = FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
        return view;
    }

    // ---- значки ----
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr h, int size, out BITMAP bmp);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    /// <summary>Значок файла/ярлыка с прозрачностью, как в проводнике.</summary>
    public static BitmapSource? ShellIcon(string path, int px)
    {
        IntPtr hbm = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
            // SIIGBF_BIGGERSIZEOK | SIIGBF_ICONONLY: без «можно крупнее» ярлыки Steam (.url) приходят глобусом.
            // Крупный значок уменьшает WPF в режиме высокого качества (Icons.Image), края чинит FixAlpha.
            if (factory.GetImage(new SIZE { cx = px, cy = px }, 0x1 | 0x4, out hbm) != 0 || hbm == IntPtr.Zero) return null;
            GetObject(hbm, Marshal.SizeOf<BITMAP>(), out var bmp);
            int w = bmp.bmWidth, h = bmp.bmHeight;
            var bi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            var buf = new byte[w * h * 4];
            var dc = GetDC(IntPtr.Zero);
            try { GetDIBits(dc, hbm, 0, (uint)h, buf, ref bi, 0); }
            finally { ReleaseDC(IntPtr.Zero, dc); }
            FixAlpha(buf);
            var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, buf, w * 4);
            src.Freeze();
            return src;
        }
        catch { return null; }
        finally { if (hbm != IntPtr.Zero) DeleteObject(hbm); }
    }

    /// <summary>
    /// Windows отдаёт значки то с умноженной на прозрачность краской, то без. Без умножения на краях
    /// вылезает тёмная/светлая кайма — домножаем сами. Значок совсем без прозрачности делаем непрозрачным.
    /// </summary>
    private static void FixAlpha(byte[] px)
    {
        bool anyAlpha = false, straight = false;
        for (int i = 0; i < px.Length; i += 4)
        {
            byte a = px[i + 3];
            if (a != 0) anyAlpha = true;
            if (px[i] > a || px[i + 1] > a || px[i + 2] > a) straight = true;
        }
        if (!anyAlpha)
        {
            for (int i = 3; i < px.Length; i += 4) px[i] = 255;
            return;
        }
        if (!straight) return;
        for (int i = 0; i < px.Length; i += 4)
        {
            int a = px[i + 3];
            px[i] = (byte)(px[i] * a / 255);
            px[i + 1] = (byte)(px[i + 1] * a / 255);
            px[i + 2] = (byte)(px[i + 2] * a / 255);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);

    /// <summary>Сдвинуть окно без перерисовки — плавное перетаскивание прозрачного окна.</summary>
    public static void MoveTo(Window w, int x, int y)
        => SetWindowPos(new WindowInteropHelper(w).Handle, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010); // NOSIZE|NOZORDER|NOACTIVATE

    /// <summary>Место и размер одним вызовом: край не «догоняет» другой край на следующем кадре.</summary>
    public static void SetBounds(Window w, RectD r, double k)
    {
        int x = (int)Math.Round(r.X * k), y = (int)Math.Round(r.Y * k);
        int cx = (int)Math.Round(r.Right * k) - x, cy = (int)Math.Round(r.Bottom * k) - y;
        SetWindowPos(new WindowInteropHelper(w).Handle, IntPtr.Zero, x, y, cx, cy, 0x0004 | 0x0010 | 0x0200); // NOZORDER|NOACTIVATE|NOOWNERZORDER
    }

    public static RECT WindowRect(Window w)
    {
        GetWindowRect(new WindowInteropHelper(w).Handle, out var r);
        return r;
    }
}
