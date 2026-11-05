using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Tiles.Core;

/// <summary>
/// Языки программы. Ключ перевода — сама русская строка: <c>Loc.T("Открыть пульт")</c>.
/// Переводы лежат в lang/&lt;код&gt;.json внутри программы; чего нет — показываем русскую строку.
/// </summary>
public static class Loc
{
    /// <summary>10 главных языков (решение владельца 06.11.2026).</summary>
    public static readonly string[] Codes = { "ru", "en", "zh", "es", "pt", "fr", "de", "ja", "ko", "tr" };

    /// <summary>Имена языков на самих этих языках — для списка «Язык».</summary>
    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        ["ru"] = "Русский", ["en"] = "English", ["zh"] = "中文（简体）", ["es"] = "Español", ["pt"] = "Português (Brasil)", // lang-ok
        ["fr"] = "Français", ["de"] = "Deutsch", ["ja"] = "日本語", ["ko"] = "한국어", ["tr"] = "Türkçe",
    };

    private static readonly IReadOnlyDictionary<string, string> Cultures = new Dictionary<string, string>
    {
        ["ru"] = "ru-RU", ["en"] = "en-US", ["zh"] = "zh-CN", ["es"] = "es-ES", ["pt"] = "pt-BR",
        ["fr"] = "fr-FR", ["de"] = "de-DE", ["ja"] = "ja-JP", ["ko"] = "ko-KR", ["tr"] = "tr-TR",
    };

    private static Dictionary<string, string>? _map;

    /// <summary>Текущий язык (код из <see cref="Codes"/>).</summary>
    public static string Lang { get; private set; } = "ru";

    /// <summary>Числа, даты и дни недели по текущему языку.</summary>
    public static CultureInfo Culture { get; private set; } = new("ru-RU");

    /// <summary>Язык сменился: перерисовать надписи.</summary>
    public static event Action? Changed;

    /// <summary>Какой язык взять: из настройки, иначе язык Windows, а если его нет среди наших — английский.</summary>
    public static string Resolve(string? setting, CultureInfo windows)
    {
        if (!string.IsNullOrEmpty(setting) && Codes.Contains(setting)) return setting;
        var two = windows.TwoLetterISOLanguageName;
        return Codes.Contains(two) ? two : "en";
    }

    /// <summary>Включить язык по настройке ("" — как в Windows). Событие только если язык правда сменился.</summary>
    public static void Set(string? setting)
    {
        var code = Resolve(setting, CultureInfo.CurrentUICulture);
        if (code == Lang && (code == "ru" || _map != null)) return;
        Lang = code;
        _map = code == "ru" ? null : Table(code);
        Culture = new CultureInfo(Cultures[code]);
        Changed?.Invoke();
    }

    /// <summary>Перевод строки на текущий язык.</summary>
    public static string T(string ru) => _map != null && _map.TryGetValue(ru, out var t) && t.Length > 0 ? t : ru;

    /// <summary>Перевод строки с местами {0}, {1}…</summary>
    public static string T(string ru, params object?[] args) => string.Format(Culture, T(ru), args);

    /// <summary>Таблица переводов одного языка (пустая, если файла нет).</summary>
    public static Dictionary<string, string> Table(string code)
    {
        using var s = typeof(Loc).Assembly.GetManifestResourceStream($"lang.{code}.json");
        if (s == null) return new();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? new();
    }

    /// <summary>Места подстановки в строке: {0}, {1:0.0}… → номера.</summary>
    public static SortedSet<int> Slots(string s)
    {
        var set = new SortedSet<int>();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '{') continue;
            if (i + 1 < s.Length && s[i + 1] == '{') { i++; continue; }
            int j = i + 1, n = 0;
            bool digit = false;
            while (j < s.Length && char.IsDigit(s[j])) { n = n * 10 + (s[j] - '0'); j++; digit = true; }
            if (digit && j < s.Length && (s[j] == '}' || s[j] == ':' || s[j] == ',')) set.Add(n);
        }
        return set;
    }
}
