using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Tiles.Core;

namespace Tiles.App;

/// <summary>
/// Часы и дата. Длинные — время над датой; квадрат — время, под ним день недели и число;
/// высокие — часы над минутами крупно. Будится раз в минуту (с секундами — раз в секунду).
/// </summary>
internal sealed class ClockView : WidgetView
{
    private readonly TextBlock _time = Text(64, FontWeights.SemiBold);
    private readonly TextBlock _hours = Text(84, FontWeights.SemiBold), _minutes = Text(84, FontWeights.SemiBold, Soft);
    private readonly TextBlock _sec = Text(20, FontWeights.SemiBold, Soft);
    private readonly TextBlock _date = Text(16, FontWeights.Medium, Soft);
    private readonly TextBlock _day = Text(18, FontWeights.SemiBold);
    private readonly StackPanel _stack = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _tick = new();

    public ClockView(WidgetInfo info) : base(info)
    {
        foreach (var t in new[] { _time, _hours, _minutes, _date, _day }) t.HorizontalAlignment = HorizontalAlignment.Center;
        _time.Margin = new Thickness(0, -8, 0, -4);
        _hours.Margin = new Thickness(0, -14, 0, -22);
        _minutes.Margin = new Thickness(0, -14, 0, -10);
        _sec.VerticalAlignment = VerticalAlignment.Bottom;
        _sec.Margin = new Thickness(4, 0, 0, 10);
        Children.Add(_stack);
        _tick.Tick += (_, _) => Show(DateTime.Now);
        ApplyOptions();
    }

    public override IReadOnlyList<(string, Func<bool>, Action<bool>)> Options => new[]
    {
        Opt(Loc.T("Дата"), "date"),
        Opt(Loc.T("День недели отдельной строкой"), "day", false),
        Opt(Loc.T("Секунды"), "sec", false),
        Opt(Loc.T("12-часовой формат"), "h12", false),
    };

    public override void ApplyOptions() { Relayout(); Show(DateTime.Now); if (Live) { _tick.Stop(); _tick.Start(); } }

    protected override void OnShape(WidgetShape s) { Relayout(); Show(DateTime.Now); }

    private void Relayout()
    {
        foreach (var t in new FrameworkElement[] { _time, _hours, _minutes, _sec, _date, _day }) (t.Parent as Panel)?.Children.Remove(t);
        _stack.Children.Clear();
        bool date = Flag("date"), day = Flag("day", false) || Shape != WidgetShape.Wide, sec = Flag("sec", false);
        if (Shape == WidgetShape.Tall)
        {
            _stack.Children.Add(_hours);
            _stack.Children.Add(_minutes);
            if (sec) { _sec.HorizontalAlignment = HorizontalAlignment.Center; _sec.Margin = new Thickness(0, 0, 0, 4); _stack.Children.Add(_sec); }
        }
        else
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { _time } };
            if (sec) { _sec.Margin = new Thickness(4, 0, 0, 10); row.Children.Add(_sec); }
            _stack.Children.Add(row);
        }
        if (day && date) _stack.Children.Add(_day);
        if (date) _stack.Children.Add(_date);
        _date.Margin = new Thickness(0, day ? 0 : 2, 0, 0);
    }

    public override string Title => Loc.T("Часы");
    public override string Description => Loc.T("Крупное время и дата. Вытяни вверх — часы над минутами. Нагрузки нет.");
    public override Size BaseSize => BaseFor(WidgetShape.Wide);

    public override Size BaseFor(WidgetShape s) => s switch
    {
        WidgetShape.Wide => new(Flag("sec", false) ? 280 : 250, Flag("day", false) ? 146 : 124),
        WidgetShape.Square => new(190, 170),
        _ => new(150, Flag("sec", false) ? 290 : 260),
    };

    private void Show(DateTime now)
    {
        bool h12 = Flag("h12", false), sec = Flag("sec", false);
        _time.Text = h12 ? now.ToString("h:mm") : now.ToString("HH:mm");
        _hours.Text = h12 ? now.ToString("%h").PadLeft(2, '0') : now.ToString("HH");
        _minutes.Text = now.ToString("mm");
        _sec.Text = now.ToString("ss");
        var d = now.ToString(DatePattern(withDay: !(Flag("day", false) || Shape != WidgetShape.Wide)), Loc.Culture);
        _date.Text = char.ToUpper(d[0]) + d[1..];
        var w = now.ToString("dddd", Loc.Culture);
        _day.Text = char.ToUpper(w[0]) + w[1..];
        _tick.Interval = sec ? TimeSpan.FromMilliseconds(1000 - now.Millisecond + 20) : TimeSpan.FromSeconds(60 - now.Second) + TimeSpan.FromMilliseconds(50);
    }

    protected override void OnStart() { Show(DateTime.Now); _tick.Start(); }
    protected override void OnStop() => _tick.Stop();
    public override void Demo() => Show(DateTime.Now);

    /// <summary>Порядок даты по языку: «6 ноября», «November 6», «11月6日»…</summary>
    private static string DatePattern(bool withDay)
    {
        var md = Loc.Culture.DateTimeFormat.MonthDayPattern; // у каждого языка свой: «d MMMM», «MMMM d», «M月d日»
        if (!withDay) return md;
        return Loc.Lang is "zh" or "ja" or "ko" ? md + " dddd" : "dddd, " + md;
    }
}
