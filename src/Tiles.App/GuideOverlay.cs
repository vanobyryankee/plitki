using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Tiles.Core;

namespace Tiles.App;

/// <summary>
/// Красные направляющие при перетаскивании и растягивании без сетки: тонкие окна-полоски,
/// мышь сквозь них проходит. Живут только пока тянут.
/// </summary>
internal static class GuideOverlay
{
    private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x5E));
    private static readonly List<Window> Bars = new();

    public static void Show(IReadOnlyList<GuideLine> lines)
    {
        while (Bars.Count < lines.Count) Bars.Add(Make());
        for (int i = 0; i < Bars.Count; i++)
        {
            var w = Bars[i];
            if (i >= lines.Count) { if (w.IsVisible) w.Hide(); continue; }
            var l = lines[i];
            const double T = 1.5;
            if (l.Vertical) { w.Left = l.At - T / 2; w.Top = l.From - 12; w.Width = T; w.Height = l.To - l.From + 24; }
            else { w.Left = l.From - 12; w.Top = l.At - T / 2; w.Width = l.To - l.From + 24; w.Height = T; }
            if (!w.IsVisible) w.Show();
        }
    }

    public static void Hide()
    {
        foreach (var w in Bars) if (w.IsVisible) w.Hide();
    }

    private static Window Make()
    {
        var w = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true,
            ShowActivated = false, Background = Red, Width = 2, Height = 2, Focusable = false, Title = "Плитки: направляющая", // lang-ok
        };
        w.SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(w).Handle;
            const int GWL_EXSTYLE = -20;
            long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex | 0x20 | 0x80 | 0x08000000)); // TRANSPARENT | TOOLWINDOW | NOACTIVATE
        };
        return w;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
}
