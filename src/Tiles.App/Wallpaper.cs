using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Tiles.Core;

namespace Tiles.App;

/// <summary>
/// «Стекло»: кусок рабочего стола под окном, размытый. Системное размытие Windows для окон на
/// рабочем столе не работает, поэтому снимаем сам рабочий стол (обои, в том числе живые из
/// Wallpaper Engine, и ярлыки — без чужих окон и без самих плиток).
/// Снимок хранится уменьшенным в 4 раза и обновляется раз в минуту: стекло всё равно размыто,
/// а открытие панели не ждёт снимка экрана.
/// </summary>
internal static class Wallpaper
{
    private const double Scale = 0.25;
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(60);

    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr old);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, uint rop);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    private static BitmapSource? _snap;
    private static Int32Rect _deskRect;
    private static DateTime _at;

    /// <summary>Следующее стекло снимет рабочий стол заново (сменились обои).</summary>
    public static void Invalidate() => _at = DateTime.MinValue;

    /// <summary>Размытый фон для прямоугольника экрана (физические пиксели).</summary>
    public static BitmapSource? Glass(Int32Rect screen, double blur)
    {
        Diag.Count("glass");
        try
        {
            if (_snap == null || DateTime.Now - _at > MaxAge) Capture();
            if (_snap == null) return null;
            return Render(screen, blur);
        }
        catch { return null; }
    }

    private static void Capture()
    {
        var progman = Native.Progman;
        if (progman == IntPtr.Zero || !GetWindowRect(progman, out var r)) return;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w <= 0 || h <= 0) return;
        int sw = Math.Max(1, (int)(w * Scale)), sh = Math.Max(1, (int)(h * Scale));

        var big = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var small = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = sw, biHeight = -sh, biPlanes = 1, biBitCount = 32 };
        IntPtr dcBig = CreateCompatibleDC(IntPtr.Zero), dcSmall = CreateCompatibleDC(IntPtr.Zero);
        IntPtr bmpBig = CreateDIBSection(dcBig, ref big, 0, out _, IntPtr.Zero, 0);
        IntPtr bmpSmall = CreateDIBSection(dcSmall, ref small, 0, out var bits, IntPtr.Zero, 0);
        IntPtr oldBig = SelectObject(dcBig, bmpBig), oldSmall = SelectObject(dcSmall, bmpSmall);
        try
        {
            if (!PrintWindow(progman, dcBig, 2)) return; // PW_RENDERFULLCONTENT — с DirectX-обоями
            SetStretchBltMode(dcSmall, 4); // HALFTONE — сглаженное уменьшение
            SetBrushOrgEx(dcSmall, 0, 0, IntPtr.Zero);
            StretchBlt(dcSmall, 0, 0, sw, sh, dcBig, 0, 0, w, h, 0x00CC0020); // SRCCOPY
            var src = BitmapSource.Create(sw, sh, 96, 96, PixelFormats.Bgr32, null, bits, sw * sh * 4, sw * 4);
            src.Freeze();
            _snap = src;
            _deskRect = new Int32Rect(r.Left, r.Top, w, h);
            _at = DateTime.Now;
            App.Current.Log($"glass capture {w}x{h} -> {sw}x{sh}");
        }
        finally
        {
            SelectObject(dcBig, oldBig);
            SelectObject(dcSmall, oldSmall);
            DeleteObject(bmpBig);
            DeleteObject(bmpSmall);
            DeleteDC(dcBig);
            DeleteDC(dcSmall);
        }
    }

    // ---- линза: весь стол размыт один раз, стекло показывает тот кусок, над которым стоит ----

    private static readonly Dictionary<int, BitmapSource> Blurred = new();
    private static BitmapSource? _blurredOf;

    /// <summary>
    /// Кисть стекла для прямоугольника экрана (физические пиксели). Та же кисть переиспользуется:
    /// при переносе меняется только «окошко» в размытый стол — дёшево, можно каждый кадр.
    /// live — идёт перенос: стол заново не снимаем, чтобы не было рывка.
    /// </summary>
    public static ImageBrush? Lens(Int32Rect screen, double blur, ImageBrush? reuse, bool live = false)
    {
        try
        {
            if (_snap == null || (!live && DateTime.Now - _at > MaxAge)) Capture();
            if (_snap == null) return null;
            if (_blurredOf != _snap) { Blurred.Clear(); _blurredOf = _snap; }
            int key = (int)Math.Round(blur);
            if (!Blurred.TryGetValue(key, out var img)) { img = BlurWhole(blur); Blurred[key] = img; }
            var vb = new Rect((screen.X - _deskRect.X) * Scale, (screen.Y - _deskRect.Y) * Scale, Math.Max(1, screen.Width * Scale), Math.Max(1, screen.Height * Scale));
            if (reuse != null && !reuse.IsFrozen && reuse.ImageSource == img) { if (reuse.Viewbox != vb) reuse.Viewbox = vb; return reuse; }
            return new ImageBrush(img) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = vb };
        }
        catch { return null; }
    }

    /// <summary>Весь уменьшенный стол, размытый; края дорисованы растянутой копией, чтобы у края экрана не темнело.</summary>
    private static BitmapSource BlurWhole(double blur)
    {
        var snap = _snap!;
        double r = Math.Max(0.5, blur * Scale);
        int m = (int)Math.Ceiling(r * 2) + 2;
        int w = snap.PixelWidth, h = snap.PixelHeight;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(snap, new Rect(0, 0, w + 2 * m, h + 2 * m));
            dc.DrawImage(snap, new Rect(m, m, w, h));
        }
        if (blur > 0) dv.Effect = new BlurEffect { Radius = r, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality };
        var rtb = new RenderTargetBitmap(w + 2 * m, h + 2 * m, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var cut = new CroppedBitmap(rtb, new Int32Rect(m, m, w, h));
        cut.Freeze();
        App.Current.Log($"glass blurred whole {w}x{h} blur {blur:0}");
        return cut;
    }

    /// <summary>Вырезает из уменьшенного снимка кусок с запасом по краям и размывает его.</summary>
    private static BitmapSource Render(Int32Rect screen, double blur)
    {
        var snap = _snap!;
        double r = Math.Max(0.5, blur * Scale);
        double margin = Math.Ceiling(r * 2) + 1;
        var crop = Layout.ScaledCrop(new RectD(screen.X, screen.Y, screen.Width, screen.Height),
            _deskRect.X, _deskRect.Y, Scale, margin, snap.PixelWidth, snap.PixelHeight);

        int w = (int)crop.W, h = (int)crop.H;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
            dc.DrawImage(snap, new Rect(-crop.X, -crop.Y, snap.PixelWidth, snap.PixelHeight));
        }
        if (blur > 0) dv.Effect = new BlurEffect { Radius = r, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality };
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);

        // внутренняя часть — ровно под окном, без запаса
        int ix = (int)Math.Round((screen.X - _deskRect.X) * Scale - crop.X);
        int iy = (int)Math.Round((screen.Y - _deskRect.Y) * Scale - crop.Y);
        int iw = Math.Max(1, Math.Min(w - Math.Max(0, ix), (int)Math.Round(screen.Width * Scale)));
        int ih = Math.Max(1, Math.Min(h - Math.Max(0, iy), (int)Math.Round(screen.Height * Scale)));
        var cut = new CroppedBitmap(rtb, new Int32Rect(Math.Max(0, ix), Math.Max(0, iy), iw, ih));
        cut.Freeze();
        return cut;
    }

    /// <summary>Физический прямоугольник окна на экране.</summary>
    public static Int32Rect ScreenRect(Window w)
    {
        double s = App.Current.DpiScale;
        return new Int32Rect((int)Math.Round(w.Left * s), (int)Math.Round(w.Top * s),
            Math.Max(1, (int)Math.Round(w.Width * s)), Math.Max(1, (int)Math.Round(w.Height * s)));
    }
}
