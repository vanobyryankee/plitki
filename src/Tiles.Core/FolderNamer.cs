namespace Tiles.Core;

/// <summary>Название папки по тому, что в ней лежит: по имени ярлыка и куда он ведёт.</summary>
public static class FolderNamer
{
    // Порядок важен: первое совпадение решает.
    private static readonly (string Name, string[] Keys)[] Groups =
    {
        ("Игры", new[] { "steam", "epicgames", "epic games", @"\games\", "xboxgames", "battle.net", "riot", "gaijin", // lang-ok
            "tlauncher", "minecraft", "ubisoft", "gog galaxy", "wargaming", "lesta", "counter-strike", "deadlock",
            "arena breakout", "heroes of might" }),
        ("Музыка", new[] { "fl studio", "fl64", "spotify", "яндекс музыка", "yandex music", "ableton", "guitar rig", // lang-ok
            "arturia", "auto-tune", "izotope", "audacity", "aiva", "reaper", "cubase", "kontakt", "native instruments",
            "ua connect" }),
        ("Видео", new[] { "davinci", "resolve", "obs64", "obs studio", "obs-studio", "premiere", "after effects", // lang-ok
            "capcut", "video downloader", "vegas", "handbrake", "kdenlive" }),
        ("Графика", new[] { "photoshop", "illustrator", "figma", "blender", "krita", "gimp", "freecad", "lightshot" }), // lang-ok
        ("Общение", new[] { "telegram", "discord", "whatsapp", "viber", "zoom", "skype", "element", "slack", "teams", // lang-ok
            "vencord" }),
        ("Браузеры", new[] { "chrome", "firefox", "msedge", "zen", "browser", "opera" }), // lang-ok
        ("Работа", new[] { "winword", "excel", "powerpnt", "powerpoint", "acrobat", "obsidian", "ticktick", "notion", // lang-ok
            "onenote", "anydo", "outlook", "libreoffice" }),
        ("Разработка", new[] { "visual studio", "vs code", @"\code.exe", "android studio", @"\git\", "docker", // lang-ok
            "jetbrains", "pycharm", "cursor", "claude" }),
        ("Сеть", new[] { "vpn", "amnezia", "v2ray", "rustdesk", "tailscale", "anydesk", "zapret" }), // lang-ok
        ("Утилиты", new[] { "7-zip", "winrar", "ccleaner", "everything", "glary", "windhawk", "displayfusion", // lang-ok
            "easeus", "memreduct", "mem reduct", "cpu-z", "afterburner" }),
    };

    public static string? Category(string name, string target)
    {
        var text = (name + " " + target).ToLowerInvariant();
        foreach (var (group, keys) in Groups)
            if (keys.Any(text.Contains)) return group;
        return null;
    }

    /// <summary>
    /// Самая частая группа, если она занимает хотя бы половину папки. Если групп много и ни одна
    /// не главная — «Разное». Если узнать нечего — null (название не трогаем).
    /// </summary>
    public static string? Suggest(IEnumerable<(string Name, string Target)> items)
    {
        var list = items.ToList();
        var known = list.Select(i => Category(i.Name, i.Target)).OfType<string>().ToList();
        if (known.Count == 0) return null;
        var top = known.GroupBy(g => g).OrderByDescending(g => g.Count()).First();
        if (top.Count() * 2 >= list.Count) return top.Key;
        return known.Count >= 3 ? "Разное" : null; // lang-ok
    }
}
