using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Tiles.Core;

namespace Tiles.App;

/// <summary>
/// Погода: температура, небо, мин/макс; по желанию «ощущается», ветер и прогноз на 3 дня.
/// Длинный — значок слева, квадрат и высокий — всё столбиком по центру.
/// Open-Meteo без ключа, раз в полчаса (в экономном — раз в час).
/// </summary>
internal sealed class WeatherView : WidgetView
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly Border _icon = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _temp = Text(48, FontWeights.SemiBold);
    private readonly TextBlock _sky = Text(15, FontWeights.SemiBold);
    private readonly TextBlock _place = Text(12.5, FontWeights.Medium, Soft);
    private readonly TextBlock _feel = Text(12.5, FontWeights.Medium, Soft);
    private readonly Grid _days = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly DispatcherTimer _tick = new();
    private Data _data = new(null, 0, true, null, null, null, null, Array.Empty<Day>(), null);

    private sealed record Day(DateTime Date, int Code, double Max, double Min);
    private sealed record Data(double? Temp, int Code, bool IsDay, double? Max, double? Min, double? Feel, double? Wind, Day[] Days, string? Note);

    public WeatherView(WidgetInfo info) : base(info)
    {
        _temp.Margin = new Thickness(0, -6, 0, -2);
        _tick.Tick += async (_, _) => await Load();
        ApplyOptions();
    }

    public override IReadOnlyList<(string, Func<bool>, Action<bool>)> Options => new[]
    {
        Opt(Loc.T("Город и мин/макс"), "place"),
        Opt(Loc.T("Ощущается и ветер"), "feel", false),
        Opt(Loc.T("Прогноз на 3 дня"), "days", false),
    };

    public override void ApplyOptions() { Relayout(); Paint(); }

    protected override void OnShape(WidgetShape s) => Relayout();

    private void Relayout()
    {
        foreach (var e in new FrameworkElement[] { _icon, _temp, _sky, _place, _feel, _days }) (e.Parent as Panel)?.Children.Remove(e);
        Children.Clear();
        _place.Visibility = Flag("place") ? Visibility.Visible : Visibility.Collapsed;
        _feel.Visibility = Flag("feel", false) ? Visibility.Visible : Visibility.Collapsed;
        _days.Visibility = Flag("days", false) ? Visibility.Visible : Visibility.Collapsed;
        bool wide = Shape == WidgetShape.Wide;
        foreach (var t in new[] { _temp, _sky, _place, _feel }) { t.HorizontalAlignment = wide ? HorizontalAlignment.Left : HorizontalAlignment.Center; t.TextAlignment = wide ? TextAlignment.Left : TextAlignment.Center; }
        _feel.TextWrapping = wide ? TextWrapping.NoWrap : TextWrapping.Wrap; // в узком — в две строки, а не «ветер…»
        _icon.HorizontalAlignment = HorizontalAlignment.Center;
        var root = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 8, 14, 8) };
        if (wide)
        {
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), Children = { _temp, _sky, _place, _feel } };
            var row = new DockPanel { Margin = new Thickness(4, 0, 0, 0) };
            DockPanel.SetDock(_icon, Dock.Left);
            row.Children.Add(_icon);
            row.Children.Add(text);
            root.Children.Add(row);
        }
        else
        {
            _icon.Margin = new Thickness(0, 0, 0, 4);
            foreach (var e in new FrameworkElement[] { _icon, _temp, _sky, _place, _feel }) root.Children.Add(e);
        }
        root.Children.Add(_days);
        Children.Add(root);
    }

    public override string Title => Loc.T("Погода");
    public override string Description => Loc.T("Температура и небо за окном, по желанию прогноз на 3 дня. Город меняется правой кнопкой.");
    public override Size BaseSize => BaseFor(WidgetShape.Wide);

    public override Size BaseFor(WidgetShape s)
    {
        double extra = (Flag("feel", false) ? (s == WidgetShape.Wide ? 18 : 36) : 0) + (Flag("days", false) ? (s == WidgetShape.Wide ? 62 : 70) : 0);
        return s switch
        {
            WidgetShape.Wide => new(290, 124 + extra),
            WidgetShape.Square => new(210, 196 + extra),
            _ => new(170, 220 + extra),
        };
    }

    private string City => Info.Props.GetValueOrDefault("city", "Москва"); // lang-ok

    public override IEnumerable<object> MenuItems() => new object[]
    {
        WidgetWindow.Item(Loc.T("Город: {0}…", City), AskCity),
        WidgetWindow.Item(Loc.T("Обновить"), async () => await Load()),
    };

    public override void Demo()
    {
        var today = DateTime.Today;
        _data = new(-3, 3, true, -1, -6, -8, 4, new[] { new Day(today.AddDays(1), 71, 0, -7), new Day(today.AddDays(2), 2, 2, -4), new Day(today.AddDays(3), 0, 3, -5) }, null);
        Info.Props.TryAdd("city", "Москва"); // lang-ok
        Paint();
    }

    protected override async void OnStart()
    {
        _tick.Interval = TimeSpan.FromMinutes(App.Current.Config.EcoMode ? 60 : 30);
        _tick.Start();
        await Load();
    }

    protected override void OnStop() => _tick.Stop();

    private async void AskCity()
    {
        var dlg = new RenameWindow(City, Loc.T("Город для погоды"));
        if (dlg.ShowDialog() != true) return;
        Info.Props["city"] = dlg.Value.Trim();
        Info.Props.Remove("lat"); Info.Props.Remove("lon");
        SaveProps();
        await Load();
    }

    private async Task Load()
    {
        _tick.Interval = TimeSpan.FromMinutes(App.Current.Config.EcoMode ? 60 : 30);
        try
        {
            if (!Info.Props.ContainsKey("lat"))
            {
                var g = await Http.GetStringAsync($"https://geocoding-api.open-meteo.com/v1/search?count=1&language=ru&name={Uri.EscapeDataString(City)}");
                using var gj = JsonDocument.Parse(g);
                if (!gj.RootElement.TryGetProperty("results", out var res) || res.GetArrayLength() == 0)
                { _data = _data with { Note = Loc.T("{0}: не найден", City) }; Paint(); return; }
                var r0 = res[0];
                Info.Props["lat"] = r0.GetProperty("latitude").GetDouble().ToString(CultureInfo.InvariantCulture);
                Info.Props["lon"] = r0.GetProperty("longitude").GetDouble().ToString(CultureInfo.InvariantCulture);
                Info.Props["city"] = r0.GetProperty("name").GetString() ?? City;
                SaveProps();
            }
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={Info.Props["lat"]}&longitude={Info.Props["lon"]}" +
                      "&current=temperature_2m,apparent_temperature,wind_speed_10m,weather_code,is_day" +
                      "&daily=weather_code,temperature_2m_max,temperature_2m_min&wind_speed_unit=ms&timezone=auto&forecast_days=4";
            using var j = JsonDocument.Parse(await Http.GetStringAsync(url));
            var cur = j.RootElement.GetProperty("current");
            var daily = j.RootElement.GetProperty("daily");
            var days = Enumerable.Range(1, Math.Max(0, daily.GetProperty("time").GetArrayLength() - 1)).Take(3).Select(i => new Day(
                DateTime.Parse(daily.GetProperty("time")[i].GetString()!, CultureInfo.InvariantCulture), daily.GetProperty("weather_code")[i].GetInt32(),
                daily.GetProperty("temperature_2m_max")[i].GetDouble(), daily.GetProperty("temperature_2m_min")[i].GetDouble())).ToArray();
            _data = new(cur.GetProperty("temperature_2m").GetDouble(), cur.GetProperty("weather_code").GetInt32(), cur.GetProperty("is_day").GetInt32() == 1,
                daily.GetProperty("temperature_2m_max")[0].GetDouble(), daily.GetProperty("temperature_2m_min")[0].GetDouble(),
                cur.GetProperty("apparent_temperature").GetDouble(), cur.GetProperty("wind_speed_10m").GetDouble(), days, null);
            Paint();
        }
        catch (Exception ex)
        {
            App.Current.Log("weather failed: " + ex.Message);
            if (_data.Temp == null) { _data = _data with { Note = Loc.T("Нет связи") }; Paint(); }
        }
    }

    private static string Deg(double v) => $"{Math.Round(v):0;−0;0}°";

    private void Paint()
    {
        var d = _data;
        _temp.Text = d.Temp is { } t ? Deg(t) : "—";
        var (sky, glyph) = Sky(d.Code, d.IsDay);
        _sky.Text = d.Temp == null ? Loc.T("Погода") : sky;
        _icon.Child = Glyphs.Make(glyph, 58, 1.5);
        _place.Text = d.Note ?? (d.Max is { } hi && d.Min is { } lo ? $"{City} · {Deg(lo)} … {Deg(hi)}" : City);
        _feel.Text = d.Feel is { } f ? Loc.T("Ощущается {0} · ветер {1:0} м/с", Deg(f), d.Wind) : "";
        _days.Children.Clear();
        _days.ColumnDefinitions.Clear();
        for (int i = 0; i < d.Days.Length; i++)
        {
            var day = d.Days[i];
            _days.ColumnDefinitions.Add(new ColumnDefinition());
            var name = Text(12, FontWeights.SemiBold, Soft);
            var n = day.Date.ToString("ddd", Loc.Culture);
            name.Text = char.ToUpper(n[0]) + n[1..];
            var temps = Text(12.5, FontWeights.SemiBold);
            temps.Text = $"{Deg(day.Min)} {Deg(day.Max)}";
            var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Children = { name, Glyphs.Make(Sky(day.Code, true).Item2, 24, 1.6), temps } };
            foreach (FrameworkElement c in cell.Children) c.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(cell, i);
            _days.Children.Add(cell);
        }
    }

    /// <summary>Коды погоды WMO → слово и значок.</summary>
    private static (string, string) Sky(int code, bool day) => code switch
    {
        0 => (day ? Loc.T("Ясно") : Loc.T("Ясная ночь"), day ? Glyphs.Sun : Glyphs.Moon),
        1 or 2 => (Loc.T("Переменная облачность"), Glyphs.PartCloud),
        3 => (Loc.T("Пасмурно"), Glyphs.Cloud),
        45 or 48 => (Loc.T("Туман"), Glyphs.Fog),
        >= 51 and <= 67 or >= 80 and <= 82 => (Loc.T("Дождь"), Glyphs.Rain),
        >= 71 and <= 77 or 85 or 86 => (Loc.T("Снег"), Glyphs.Snow),
        >= 95 => (Loc.T("Гроза"), Glyphs.Storm),
        _ => (Loc.T("Облачно"), Glyphs.Cloud),
    };
}
