using System.Globalization;

namespace Tiles.Core;

/// <summary>
/// Сколько показывает виджет «Пульт сети»: в одну клетку — переключатель; побольше — сервер и пинг;
/// ещё больше — скорость как в пульте; крупный — всё, с zapret и Телеграмом.
/// </summary>
public enum NetTier { Mini, Button, Medium, Full }

public static class NetTiers
{
    private static readonly CultureInfo Ru = new("ru-RU");

    /// <summary>По размеру стекла в точках.</summary>
    public static NetTier Of(double w, double h)
    {
        if (w < 120 && h < 120) return NetTier.Mini;
        if (w < 200 && h < 200) return NetTier.Button;
        if ((w >= 400 && h >= 200) || (h >= 400 && w >= 200) || (w >= 280 && h >= 280)) return NetTier.Full;
        return NetTier.Medium;
    }

    /// <summary>Скорость в Мбит/с как в пульте: «10,5», от сотни — без дробной части.</summary>
    public static string Mbit(double bytesPerSec, CultureInfo? c = null)
    {
        c ??= Ru;
        double m = Math.Max(0, bytesPerSec) * 8 / 1_000_000;
        if (m <= 0) return "0";
        return m >= 100 ? m.ToString("0", c) : m.ToString("0.0", c);
    }

    /// <summary>Сколько прошло всего: «85 МБ», «1,22 ГБ».</summary>
    public static string Total(long bytes, CultureInfo? c = null)
    {
        c ??= Ru;
        if (bytes < 1024) return Loc.T("{0} Б", Math.Max(0, bytes).ToString(c));
        if (bytes < 1024L * 1024) return Loc.T("{0} КБ", (bytes / 1024.0).ToString("0", c));
        if (bytes < 1024L * 1024 * 1024) return Loc.T("{0} МБ", (bytes / 1024.0 / 1024).ToString("0", c));
        return Loc.T("{0} ГБ", (bytes / 1024.0 / 1024 / 1024).ToString("0.00", c));
    }
}
