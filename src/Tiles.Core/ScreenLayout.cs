namespace Tiles.Core;

/// <summary>
/// Расклад мониторов строкой «x,y,WxH;x,y,WxH». Игра на весь экран временно уменьшает разрешение
/// одного экрана: такой расклад никогда не становится «домашним», даже если игра шла час.
/// </summary>
public static class ScreenLayout
{
    private static (double X, double Y, double W, double H)[] Parse(string key)
    {
        var list = new List<(double, double, double, double)>();
        foreach (var part in (key ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var xy = part.Split(',');
            if (xy.Length != 3) return Array.Empty<(double, double, double, double)>();
            var wh = xy[2].Split('x');
            if (wh.Length != 2) return Array.Empty<(double, double, double, double)>();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (!double.TryParse(xy[0], System.Globalization.NumberStyles.Float, inv, out var x)
                || !double.TryParse(xy[1], System.Globalization.NumberStyles.Float, inv, out var y)
                || !double.TryParse(wh[0], System.Globalization.NumberStyles.Float, inv, out var w)
                || !double.TryParse(wh[1], System.Globalization.NumberStyles.Float, inv, out var h))
                return Array.Empty<(double, double, double, double)>();
            list.Add((x, y, w, h));
        }
        return list.ToArray();
    }

    /// <summary>
    /// Те же мониторы, но хотя бы один стал меньше, и ни один не стал больше: так делает игра.
    /// Такой расклад временный, места под него не переставляем.
    /// </summary>
    public static bool IsShrunk(string home, string now)
    {
        var a = Parse(home); var b = Parse(now);
        if (a.Length == 0 || a.Length != b.Length) return false;
        bool smaller = false;
        for (int i = 0; i < a.Length; i++)
        {
            if (b[i].W > a[i].W + 0.5 || b[i].H > a[i].H + 0.5) return false;
            if (b[i].W < a[i].W - 0.5 || b[i].H < a[i].H - 0.5) smaller = true;
        }
        return smaller;
    }

    /// <summary>Какой расклад считать домашним при запуске: сохранённый, если сейчас лишь временно меньше.</summary>
    public static string HomeOnStart(string? saved, string now)
        => !string.IsNullOrEmpty(saved) && IsShrunk(saved, now) ? saved : now;
}
