using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Tiles.Core;

namespace Tiles.App;

/// <summary>
/// Содержимое виджета без окна: одно и то же живёт на рабочем столе и в карточке мастерской.
/// Рисуется в базовом размере, окно масштабирует его целиком.
/// </summary>
internal abstract class WidgetView : Grid
{
    public static readonly FontFamily Display = new("Segoe UI Variable Display, Segoe UI");
    public static readonly FontFamily Body = new("Segoe UI Variable Text, Segoe UI");
    public static readonly Brush Soft = new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF));
    public static readonly Brush Faint = new SolidColorBrush(Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF));
    public static readonly Effect Shadow = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.7 };

    protected WidgetView(WidgetInfo info) { Info = info; }

    public WidgetInfo Info { get; }
    public abstract string Title { get; }
    public abstract string Description { get; }
    /// <summary>Размер стекла при масштабе 1.</summary>
    public abstract Size BaseSize { get; }

    /// <summary>Форма сейчас: длинный, квадрат или высокий — от неё зависит раскладка.</summary>
    public WidgetShape Shape { get; private set; } = WidgetShape.Wide;
    /// <summary>Размер стекла при масштабе 1 для этой формы (длинный — BaseSize).</summary>
    public virtual Size BaseFor(WidgetShape s) => BaseSize;
    /// <summary>Самый маленький из базовых размеров — меньше этого (с запасом) не сжать.</summary>
    public virtual Size MinSize => new(Enum.GetValues<WidgetShape>().Min(s => BaseFor(s).Width), Enum.GetValues<WidgetShape>().Min(s => BaseFor(s).Height));
    public void SetShape(WidgetShape s) { if (s == Shape) return; Shape = s; OnShape(s); }
    /// <summary>Размер стекла в точках (до масштаба): виджет может показать больше или меньше.</summary>
    public virtual void SetArea(double w, double h) { }
    /// <summary>Переложить содержимое под форму: в ряд, столбиком или квадратом.</summary>
    protected virtual void OnShape(WidgetShape s) { }
    /// <summary>Подпись под стеклом, как имя у ярлыка (корзина); null — без подписи.</summary>
    public virtual string? Label => null;
    public bool Live { get; private set; }

    /// <summary>Виджет просит показать или спрятать окно (музыка без плеера прячется).</summary>
    public event Action<bool>? WantVisible;
    protected void RequestVisible(bool v) => WantVisible?.Invoke(v);
    public virtual bool InitiallyVisible => true;

    public void Start() { Live = true; OnStart(); }
    public void Stop() { Live = false; OnStop(); }
    protected virtual void OnStart() { }
    protected virtual void OnStop() { }
    /// <summary>Картинка для мастерской: выдуманные данные, без таймеров и подписок.</summary>
    public virtual void Demo() { }
    /// <summary>Свои пункты в меню по правой кнопке.</summary>
    public virtual IEnumerable<object> MenuItems() => Array.Empty<object>();
    /// <summary>Свои галочки виджета в окне «Настроить вид»: что показывать.</summary>
    public virtual IReadOnlyList<(string Label, Func<bool> Get, Action<bool> Set)> Options => Array.Empty<(string, Func<bool>, Action<bool>)>();

    protected bool Flag(string key, bool def = true) => Info.Props.TryGetValue(key, out var v) ? v == "1" : def;

    /// <summary>Галочка для окна настроек: хранится в Props, применяется сразу.</summary>
    protected (string, Func<bool>, Action<bool>) Opt(string label, string key, bool def = true)
        => (label, () => Flag(key, def), v => { Info.Props[key] = v ? "1" : "0"; SaveProps(); ApplyOptions(); LayoutChanged?.Invoke(); });

    /// <summary>Галочка поменяла размеры содержимого — окну переложить его.</summary>
    public event Action? LayoutChanged;

    /// <summary>Перерисовать по своим галочкам.</summary>
    public virtual void ApplyOptions() { }

    /// <summary>Клик без перетаскивания по пустому месту виджета.</summary>
    public virtual void Click() { }
    /// <summary>Виджет сам обрабатывает мышь (кнопки, поле ввода) — тогда окно не тащим.</summary>
    public virtual bool OwnsMouse(DependencyObject? hit) => false;

    protected static TextBlock Text(double size, FontWeight weight, Brush? brush = null, FontFamily? font = null) => new()
    {
        FontSize = size, FontWeight = weight, Foreground = brush ?? Brushes.White, FontFamily = font ?? Display,
        TextTrimming = TextTrimming.CharacterEllipsis, Effect = Shadow,
    };

    protected void SaveProps() => App.Current.SaveConfigSoon();

    public static WidgetView Create(WidgetInfo info) => info.Kind switch
    {
        "music" => new MusicView(info),
        "bin" => new BinView(info),
        "clock" => new ClockView(info),
        "system" => new SystemView(info),
        "weather" => new WeatherView(info),
        "note" => new NoteView(info),
        "net" => new NetView(info),
        _ => throw new ArgumentException(info.Kind),
    };
}
