using System.Globalization;
using System.Text.RegularExpressions;
using Tiles.Core;

namespace Tiles.Tests;

public class LocTests
{
    [Theory]
    [InlineData("", "de-DE", "de")]   // как в Windows
    [InlineData("", "ru-RU", "ru")]
    [InlineData("", "ar-SA", "en")]   // нашего нет — английский
    [InlineData("", "pt-PT", "pt")]
    [InlineData("fr", "de-DE", "fr")] // выбран вручную
    [InlineData("xx", "ja-JP", "ja")] // неизвестная настройка — как в Windows
    public void Resolve_PicksLanguage(string setting, string windows, string want)
        => Assert.Equal(want, Loc.Resolve(setting, new CultureInfo(windows)));

    [Fact]
    public void Slots_FindsPlaceholders()
    {
        Assert.Equal(new[] { 0, 1 }, Loc.Slots("{0} из {1:0.0}"));
        Assert.Empty(Loc.Slots("без мест {{0}}"));
    }

    [Fact]
    public void English_Translates_KnownKey()
        => Assert.Equal("Open control panel", Loc.Table("en")["Открыть пульт"]);

    /// <summary>Все строки Loc.T("…") из кода — с переводом на каждом языке, с теми же местами {0}.</summary>
    [Fact]
    public void EveryKey_TranslatedInEveryLanguage()
    {
        var keys = Keys();
        Assert.True(keys.Count > 150, $"в коде всего {keys.Count} строк Loc.T — надписи не обёрнуты");
        var missing = new List<string>();
        foreach (var code in Loc.Codes.Where(c => c != "ru"))
        {
            var table = Loc.Table(code);
            foreach (var k in keys)
            {
                if (!table.TryGetValue(k, out var t) || string.IsNullOrWhiteSpace(t)) { missing.Add($"{code}: нет «{k}»"); continue; }
                if (!Loc.Slots(k).SetEquals(Loc.Slots(t))) missing.Add($"{code}: места не те в «{k}» → «{t}»");
            }
        }
        Assert.True(missing.Count == 0, string.Join("\n", missing.Take(40)) + $"\n…всего {missing.Count}");
    }

    /// <summary>Русских надписей мимо Loc.T в коде окон и виджетов не осталось (журнал и служебное не в счёт).</summary>
    [Fact]
    public void NoBareRussianText()
    {
        var bad = new List<string>();
        foreach (var f in Directory.EnumerateFiles(Path.Combine(Root(), "src", "Tiles.App"), "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            foreach (var (line, i) in File.ReadLines(f).Select((l, i) => (l, i)))
            {
                var code = line.Trim();
                if (code.StartsWith("//") || code.StartsWith("///") || code.StartsWith("*")) continue;
                if (code.Contains("Log(") || code.Contains("// lang-ok")) continue;
                foreach (Match m in Literal.Matches(line))
                {
                    var s = m.Groups[1].Value;
                    if (!Cyr.IsMatch(s)) continue;
                    int at = m.Index;
                    bool wrapped = Regex.IsMatch(line[..at], @"Loc\.T\(\s*$");
                    if (!wrapped) bad.Add($"{Path.GetFileName(f)}:{i + 1}: {s}");
                }
            }
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad.Take(40)) + $"\n…всего {bad.Count}");
    }

    private static readonly Regex Literal = new(@"\$?@?""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);
    private static readonly Regex Cyr = new("[А-Яа-яЁё]", RegexOptions.Compiled);
    private static readonly Regex Call = new(@"Loc\.T\(\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

    internal static HashSet<string> Keys()
    {
        var keys = new HashSet<string>();
        foreach (var f in Directory.EnumerateFiles(Path.Combine(Root(), "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            foreach (Match m in Call.Matches(File.ReadAllText(f))) keys.Add(Regex.Unescape(m.Groups[1].Value));
        }
        return keys;
    }

    internal static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "src", "Tiles.App"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("не нашёл папку проекта");
    }
}
