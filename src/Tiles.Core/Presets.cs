using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tiles.Core;

/// <summary>Сохранённая расстановка стола: папки, виджеты, их вид, общий шаблон и сетка.</summary>
public sealed class LayoutPreset
{
    public string Name { get; set; } = "";
    public DateTime Saved { get; set; }
    public List<TileInfo> Tiles { get; set; } = new();
    public List<WidgetInfo> Widgets { get; set; } = new();
    public TileStyle Defaults { get; set; } = new();
    public double GridW { get; set; } = double.NaN;
    public double GridH { get; set; } = double.NaN;
}

/// <summary>
/// Расстановки: снять копию стола и вернуть её. Содержимое папок не трогается никогда:
/// плитки сопоставляются по имени папки, у найденных берётся всё остальное.
/// </summary>
public static class Presets
{
    private static readonly JsonSerializerOptions Json = new() { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    private static T Copy<T>(T v) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Json), Json)!;

    public static LayoutPreset Capture(TilesConfig c, string name) => new()
    {
        Name = name.Trim(),
        Saved = DateTime.Now,
        Tiles = Copy(c.Tiles),
        Widgets = Copy(c.Widgets),
        Defaults = Copy(c.Defaults),
        GridW = c.GridW,
        GridH = c.GridH,
    };

    /// <summary>Сохранить текущий стол под именем (то же имя без учёта регистра — заменить) и сделать её последней.</summary>
    public static LayoutPreset Save(TilesConfig c, string name)
    {
        var p = Capture(c, name);
        int i = c.Presets.FindIndex(x => string.Equals(x.Name, p.Name, StringComparison.CurrentCultureIgnoreCase));
        if (i >= 0) c.Presets[i] = p; else c.Presets.Add(p);
        c.ActivePreset = p.Name;
        return p;
    }

    public static LayoutPreset? Find(TilesConfig c, string? name)
        => name == null ? null : c.Presets.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase));

    /// <summary>Вернуть расстановку. Папки, которых в ней нет, остаются как есть; пропавшие папки пропускаются.</summary>
    public static void Apply(TilesConfig c, LayoutPreset p)
    {
        foreach (var saved in p.Tiles)
        {
            var t = c.Tiles.FirstOrDefault(x => string.Equals(x.Name, saved.Name, StringComparison.OrdinalIgnoreCase));
            if (t == null) continue;
            t.X = saved.X; t.Y = saved.Y; t.Cols = saved.Cols; t.Rows = saved.Rows;
            t.AutoName = saved.AutoName; t.Locked = saved.Locked; t.Align = saved.Align;
            t.Style = Copy(saved.Style);
            t.Order = (saved.Order ?? new()).ToList();
        }
        foreach (var saved in p.Widgets)
        {
            int i = c.Widgets.FindIndex(x => x.Kind == saved.Kind);
            var w = Copy(saved);
            if (i >= 0) c.Widgets[i] = w; else c.Widgets.Add(w);
        }
        c.Defaults = Copy(p.Defaults);
        c.GridW = p.GridW;
        c.GridH = p.GridH;
        c.ActivePreset = p.Name;
    }
}
