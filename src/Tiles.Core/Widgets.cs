namespace Tiles.Core;

/// <summary>Логика виджета музыки без окон: когда показывать, время, прогресс.</summary>
public static class Music
{
    public enum Source { Music, Browser, Video }

    private static readonly string[] Browsers = { "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "zen", "floorp",
        "librewolf", "waterfox", "thebrowsercompany", "arc.exe", "chromium", "browser" };
    private static readonly string[] VideoApps = { "vlc", "mpc-hc", "mpc-be", "potplayer", "zunevideo", "photos", "mpv",
        "kmplayer", "netflix", "kinopoisk", "ivi", "okko", "twitch", "obs" };

    /// <summary>
    /// Чья это медиасессия Windows: музыкальная программа, браузер (YouTube и всё подряд) или видеоплеер.
    /// Браузеры Firefox и Zen подписываются 16-значным шестнадцатеричным кодом — тоже браузер.
    /// </summary>
    public static Source Kind(string? app)
    {
        var a = (app ?? "").Trim().ToLowerInvariant();
        if (a.Length == 0) return Source.Browser;
        if (a.Length == 16 && a.All(Uri.IsHexDigit)) return Source.Browser;
        if (a.Contains("music") || a.Contains("spotify") || a.Contains("aimp") || a.Contains("foobar")) return Source.Music;
        if (a.Contains("yandex")) return a.Contains("browser") || a.StartsWith("yandex.") ? Source.Browser : Source.Music;
        if (VideoApps.Any(a.Contains)) return Source.Video;
        if (Browsers.Any(a.Contains)) return Source.Browser;
        return Source.Music;
    }

    public static bool Allowed(string? app, bool browsers) => Kind(app) switch
    {
        Source.Music => true,
        Source.Browser => browsers,
        _ => false,
    };

    /// <summary>
    /// Какую сессию показывать: играющую музыку, иначе текущую (если она музыкальная), иначе любую музыкальную.
    /// -1 — показывать нечего.
    /// </summary>
    public static int Pick(IReadOnlyList<(string App, bool Playing)> sessions, string? current, bool browsers)
    {
        int playing = -1, cur = -1, any = -1;
        for (int i = 0; i < sessions.Count; i++)
        {
            if (!Allowed(sessions[i].App, browsers)) continue;
            if (any < 0) any = i;
            if (sessions[i].App == current && cur < 0) cur = i;
            if (sessions[i].Playing && (playing < 0 || sessions[i].App == current)) playing = i;
        }
        return playing >= 0 ? playing : cur >= 0 ? cur : any;
    }

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
