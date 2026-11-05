using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Tiles.App;

/// <summary>
/// Свои значки в одном стиле: белые линии со скруглёнными концами, рисунок 24×24.
/// Рисуются вектором — чёткие при любом масштабе экрана.
/// </summary>
internal static class Glyphs
{
    public const string Prev = "M6,5.5 L6,18.5 M19,6 L9.5,12 L19,18 Z";
    public const string Next = "M18,5.5 L18,18.5 M5,6 L14.5,12 L5,18 Z";
    public const string Play = "M8,5.5 L18.5,12 L8,18.5 Z";
    public const string Pause = "M8.5,5.5 L8.5,18.5 M15.5,5.5 L15.5,18.5";
    public const string Note = "M9,17.5 m-2.5,0 a2.5,2.5 0 1,0 5,0 a2.5,2.5 0 1,0 -5,0 M11.5,17.5 L11.5,5.5 L19,4 L19,15.5 M19,15.5 m-2.5,0 a2.5,2.5 0 1,0 5,0 a2.5,2.5 0 1,0 -5,0";
    public const string BinEmpty = "M4,7 L20,7 M9.5,7 L9.5,4.5 L14.5,4.5 L14.5,7 M6,7 L7.2,20 L16.8,20 L18,7 M10,10.5 L10,16.5 M14,10.5 L14,16.5";
    /// <summary>Полная: крышка приподнята, внутри бумажки.</summary>
    public const string BinFull = "M4.5,6 L19.5,3.5 M10,5 L9.6,3 L14.4,2.2 L14.8,4.2 M6,9 L7.2,20 L16.8,20 L18,9 Z M8,9 L9.5,6.8 L11.5,8 L13.5,6.2 L16,9";

    // погода
    public const string Sun = "M12,12 m-4,0 a4,4 0 1,0 8,0 a4,4 0 1,0 -8,0 M12,2.5 L12,4.5 M12,19.5 L12,21.5 M2.5,12 L4.5,12 M19.5,12 L21.5,12 M5.3,5.3 L6.7,6.7 M17.3,17.3 L18.7,18.7 M5.3,18.7 L6.7,17.3 M17.3,6.7 L18.7,5.3";
    public const string Moon = "M19,14.5 A7.5,7.5 0 1,1 9.5,5 A6,6 0 0,0 19,14.5 Z";
    public const string Cloud = "M7,18.5 L17.5,18.5 A4,4 0 0,0 17.5,10.5 A5.5,5.5 0 0,0 6.8,11.6 A3.5,3.5 0 0,0 7,18.5 Z";
    public const string PartCloud = "M9.5,6.5 m-3,0 a3,3 0 1,0 6,0 a3,3 0 1,0 -6,0 M9.5,1.8 L9.5,2.6 M4.8,6.5 L4,6.5 M6.2,3.2 L5.6,2.6 M12.8,3.2 L13.4,2.6 M8,19.5 L17.5,19.5 A3.6,3.6 0 0,0 17.5,12.3 A5,5 0 0,0 7.8,13.3 A3.1,3.1 0 0,0 8,19.5 Z";
    public const string Rain = "M7,14.5 L17.5,14.5 A4,4 0 0,0 17.5,6.5 A5.5,5.5 0 0,0 6.8,7.6 A3.5,3.5 0 0,0 7,14.5 Z M8.5,17.5 L7.5,20.5 M12.5,17.5 L11.5,20.5 M16.5,17.5 L15.5,20.5";
    public const string Snow = "M7,14.5 L17.5,14.5 A4,4 0 0,0 17.5,6.5 A5.5,5.5 0 0,0 6.8,7.6 A3.5,3.5 0 0,0 7,14.5 Z M8,18 L8,18.1 M12,19.5 L12,19.6 M16,18 L16,18.1 M10,21.5 L10,21.6 M14,21.5 L14,21.6";
    public const string Storm = "M7,14 L17.5,14 A4,4 0 0,0 17.5,6 A5.5,5.5 0 0,0 6.8,7.1 A3.5,3.5 0 0,0 7,14 Z M12.5,15.5 L10.5,18.5 L13.5,18.5 L11.5,21.5";
    public const string Fog = "M7,12.5 L17.5,12.5 A4,4 0 0,0 17.5,4.5 A5.5,5.5 0 0,0 6.8,5.6 A3.5,3.5 0 0,0 7,12.5 Z M4,16 L20,16 M6,19.5 L18,19.5";

    // окно приложения
    public const string Workshop = "M4,4 L10.5,4 L10.5,10.5 L4,10.5 Z M13.5,4 L20,4 L20,10.5 L13.5,10.5 Z M4,13.5 L10.5,13.5 L10.5,20 L4,20 Z M16.75,13.5 L16.75,20 M13.5,16.75 L20,16.75";
    public const string Folders = "M3.5,7 L3.5,18.5 L20.5,18.5 L20.5,8.5 L11.5,8.5 L9.5,6 L3.5,6 Z";
    public const string Brush = "M12,3.5 C16.7,3.5 20.5,7 20.5,11.3 C20.5,14 18.4,15 16.5,15 L14.5,15 C13.4,15 12.8,16.2 13.4,17.1 C14.1,18.2 13.5,20.5 12,20.5 C7.3,20.5 3.5,16.7 3.5,12 C3.5,7.3 7.3,3.5 12,3.5 Z M8,11 L8,11.1 M10.5,7.5 L10.5,7.6 M15,8 L15,8.1";
    public const string Gauge = "M4,17 A8.5,8.5 0 1,1 20,17 M12,13.5 L16,8.5 M12,13.5 m-1.4,0 a1.4,1.4 0 1,0 2.8,0 a1.4,1.4 0 1,0 -2.8,0";
    public const string Plus = "M12,5 L12,19 M5,12 L19,12";
    public const string Clock = "M12,12 m-8.5,0 a8.5,8.5 0 1,0 17,0 a8.5,8.5 0 1,0 -17,0 M12,7 L12,12 L15.5,14";
    public const string Pencil = "M5,19 L5.8,15.6 L15.6,5.8 A1.8,1.8 0 0,1 18.2,8.4 L8.4,18.2 Z M14,7.4 L16.6,10";
    public const string Leaf = "M5,19 C5,10 10,5 19.5,4.5 C19,14 14,19 5,19 Z M5,19 L13,11";

    /// <summary>Значок в квадрате size×size.</summary>
    public static FrameworkElement Make(string data, double size, double stroke = 1.7, Brush? fill = null, Brush? color = null)
        => new Viewbox
        {
            Width = size, Height = size,
            Child = new Canvas
            {
                Width = 24, Height = 24,
                Children =
                {
                    new Path
                    {
                        Data = Geometry.Parse(data), Stroke = color ?? Brushes.White, StrokeThickness = stroke, Fill = fill,
                        StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                    },
                },
            },
        };
}
