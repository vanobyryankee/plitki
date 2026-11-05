namespace Tiles.Core;

/// <summary>Логика виджета музыки без окон: когда показывать, время, прогресс.</summary>
public static class Music
{
    /// <summary>На паузе виджет остаётся, без плеера прячется.</summary>
    public static bool Visible(bool hasPlayer, bool playing) => hasPlayer;

    public static string Time(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }

    public static double Progress(TimeSpan pos, TimeSpan length)
        => length <= TimeSpan.Zero ? 0 : Math.Clamp(pos.TotalSeconds / length.TotalSeconds, 0, 1);

    /// <summary>Windows сообщает позицию редко: пока играет, прибавляем прошедшее время.</summary>
    public static TimeSpan Position(TimeSpan reported, DateTimeOffset reportedAt, DateTimeOffset now, bool playing, TimeSpan length)
    {
        var p = playing ? reported + (now - reportedAt) : reported;
        if (p < TimeSpan.Zero) p = TimeSpan.Zero;
        return length > TimeSpan.Zero && p > length ? length : p;
    }
}

/// <summary>Подписи плитки-корзины.</summary>
public static class Bin
{
    public static string Caption(long count)
    {
        if (count <= 0) return Loc.T("Пусто");
        long n10 = count % 10, n100 = count % 100;
        return n10 == 1 && n100 != 11 ? Loc.T("{0} файл", count)
            : n10 is >= 2 and <= 4 && n100 is < 12 or > 14 ? Loc.T("{0} файла", count)
            : Loc.T("{0} файлов", count);
    }
}
