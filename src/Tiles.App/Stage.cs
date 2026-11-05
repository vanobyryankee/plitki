using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Tiles.Core;

namespace Tiles.App;

/// <summary>
/// «Сцена»: одно прозрачное окно на все экраны, всегда открытое и пустое. Пока виджет растягивают
/// или он доплывает в новый размер, его содержимое переезжает сюда: окно сцены не двигается и не
/// меняет размер, поэтому край виджета и его содержимое меняются в одном кадре — без дрожи и
/// без вспышек. Окно самого виджета в это время пустое, его можно перестраивать незаметно.
/// Обе части рисуются в одном проходе (одно окно программы — один поток рисования).
/// </summary>
internal static class Stage
{
    private static Window? _w;
    private static readonly Canvas Host = new();
    private static RectD _origin;

    public static (Canvas Host, RectD Origin) Ensure()
    {
        var all = App.Current.AllScreens;
        if (_w != null && _origin == all) return (Host, _origin);
        if (_w == null)
        {
            _w = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false, ShowActivated = false, Title = "Плитки: сцена", Content = Host, // lang-ok
            };
            _w.SourceInitialized += (_, _) =>
            {
                Native.PinToDesktop(_w);
                var h = new WindowInteropHelper(_w).Handle;
                long ex = GetWindowLongPtr(h, -20).ToInt64();
                SetWindowLongPtr(h, -20, new IntPtr(ex | 0x20 | 0x80 | 0x08000000)); // мышь насквозь, без Alt+Tab, без активации
            };
        }
        // экраны поменялись (или первый раз) — пока сцена пустая, её можно двигать незаметно
        _origin = all;
        _w.Left = all.X; _w.Top = all.Y; _w.Width = all.W; _w.Height = all.H;
        if (!_w.IsVisible) _w.Show();
        Native.SetBounds(_w, all, App.Current.DpiScale);
        return (Host, _origin);
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
}
