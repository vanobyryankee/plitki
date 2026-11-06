using System.Text.Encodings.Web;
using System.Text.Json;

namespace Tiles.Core;

public sealed class TileInfo
{
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Размер в ярлыках рабочего стола (2×2 = «как четыре ярлыка»).</summary>
    public int Cols { get; set; } = 2;
    public int Rows { get; set; } = 2;
    /// <summary>Название подбирается по содержимому, пока владелец не переименует сам.</summary>
    public bool AutoName { get; set; } = true;
    /// <summary>Закреплена: не двигается, не растягивается, соседи её не сдвигают.</summary>
    public bool Locked { get; set; }
    /// <summary>Без сетки — прилипать к краям других плиток (красные линии).</summary>
    public bool Align { get; set; } = true;
    public TileStyle Style { get; set; } = new();
    /// <summary>Свой порядок ярлыков (имена файлов); пусто — по алфавиту. Первые идут в превью.</summary>
    public List<string> Order { get; set; } = new();
}

/// <summary>Виджет на столе. Место и размер в точках экрана (NaN — ещё не ставили), Props — его настройки.</summary>
public sealed class WidgetInfo
{
    public string Kind { get; set; } = "";
    public bool Enabled { get; set; }
    public double X { get; set; } = double.NaN;
    public double Y { get; set; } = double.NaN;
    /// <summary>Масштаб содержимого из прошлой версии (1 = базовый размер); теперь размер — W×H.</summary>
    public double Scale { get; set; } = 1;
    /// <summary>Размер окна виджета в точках экрана; NaN — базовый.</summary>
    public double W { get; set; } = double.NaN;
    public double H { get; set; } = double.NaN;
    public bool Snap { get; set; } = true;
    public bool Locked { get; set; }
    public bool Align { get; set; } = true;
    /// <summary>Свой вид виджета (стекло, оттенок, углы); null — как в общих настройках.</summary>
    public TileStyle? Style { get; set; }
    public Dictionary<string, string> Props { get; set; } = new();
}

public sealed class TilesConfig
{
    /// <summary>Все виджеты мастерской по порядку.</summary>
    public static readonly string[] WidgetKinds = { "music", "bin", "clock", "system", "weather", "note", "net" };

    public List<WidgetInfo> Widgets { get; set; } = new();
    /// <summary>Экономный режим: стекло не обновляется по таймеру, виджеты обновляются реже.</summary>
    public bool EcoMode { get; set; }
    /// <summary>Язык программы: код из Loc.Codes, "" — как в Windows.</summary>
    public string Language { get; set; } = "";
    /// <summary>Сохранённые расстановки стола и последняя выбранная (её ставим сами после игры/смены мониторов).</summary>
    public List<LayoutPreset> Presets { get; set; } = new();
    public string? ActivePreset { get; set; }
    /// <summary>Домашний расклад мониторов (под него расставлен стол); игра его не перезаписывает.</summary>
    public string HomeScreens { get; set; } = "";
    /// <summary>Ярлык «Плитки» на стол уже клали (удалённый не возвращаем).</summary>
    public bool ShortcutsMade { get; set; }
    /// <summary>Своя клетка сетки (подогнанная руками под ярлыки на столе); NaN — как у Windows.</summary>
    public double GridW { get; set; } = double.NaN;
    public double GridH { get; set; } = double.NaN;

    public List<TileInfo> Tiles { get; set; } = new();
    public bool Autostart { get; set; } = true;
    /// <summary>Шаблон: даётся новым папкам, «Применить ко всем» копирует его во все.</summary>
    public TileStyle Defaults { get; set; } = new();
    /// <summary>Виджет музыки в левом нижнем углу.</summary>
    public bool MusicWidget { get; set; } = true;
    /// <summary>Своя плитка-корзина вместо корзины Windows; место — NaN, пока не ставили.</summary>
    public bool BinTile { get; set; } = true;
    public double BinX { get; set; } = double.NaN;
    public double BinY { get; set; } = double.NaN;

    public void ApplyDefaultsToAll()
    {
        foreach (var t in Tiles) t.Style = Defaults.Clone();
    }

    public void Normalize()
    {
        Defaults ??= new TileStyle();
        Defaults.Normalize();
        foreach (var t in Tiles)
        {
            t.Style ??= Defaults.Clone();
            t.Style.Normalize();
            t.Order ??= new();
        }
        Widgets ??= new();
        Widgets.RemoveAll(w => !WidgetKinds.Contains(w.Kind));
        foreach (var kind in WidgetKinds.Where(k => Widgets.All(w => w.Kind != k)))
        {
            // первый запуск новой версии: музыка и корзина как были, остальное выключено
            var w = new WidgetInfo { Kind = kind, Enabled = kind switch { "music" => MusicWidget, "bin" => BinTile, _ => false } };
            if (kind == "bin") { w.X = BinX; w.Y = BinY; }
            Widgets.Add(w);
        }
        foreach (var w in Widgets)
        {
            w.Props ??= new();
            w.Style?.Normalize();
            w.Scale = double.IsFinite(w.Scale) ? Math.Clamp(w.Scale, WidgetSizing.MinScale, WidgetSizing.MaxScale) : 1;
        }
    }

    public WidgetInfo Widget(string kind) => Widgets.First(w => w.Kind == kind);
}

public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static TilesConfig Load(string path)
    {
        if (!File.Exists(path)) { var fresh = new TilesConfig(); fresh.Normalize(); return fresh; }
        try
        {
            var cfg = JsonSerializer.Deserialize<TilesConfig>(File.ReadAllText(path), Options) ?? new TilesConfig();
            cfg.Normalize();
            return cfg;
        }
        catch (JsonException)
        {
            File.Move(path, $"{path}.broken-{DateTime.Now:yyyyMMdd-HHmmss}");
            return new TilesConfig();
        }
    }

    public static void Save(string path, TilesConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, Options));
        File.Move(tmp, path, overwrite: true);
    }
}
