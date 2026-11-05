using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Tiles.Core;

namespace Tiles.App;

/// <summary>
/// «Пульт сети» в стиле самого пульта: тёмные карточки, голубой переключатель, крупная тонкая скорость.
/// В одну клетку: точка и переключатель VPN; побольше: сервер и пинг; ещё больше: скорость в обе стороны;
/// крупный: пинг, связь, страна, zapret и Телеграм со своими переключателями.
/// Всё переключается прямо отсюда через вход пульта; скорость берётся с сетевого адаптера туннеля.
/// </summary>
internal sealed class NetView : WidgetView
{
    private const string PanelExe = @"D:\NetPanel\app\NetPanel.exe";

    // цвета и шрифты пульта
    private static readonly Color AccC = Color.FromRgb(0x9C, 0xC0, 0xFF);
    private static readonly Color EmberC = Color.FromRgb(0xFF, 0x92, 0x74);
    private static readonly Brush Acc = Fz(new SolidColorBrush(AccC));
    private static readonly Brush Fg = Fz(new SolidColorBrush(Color.FromRgb(0xEE, 0xF1, 0xF7)));
    private static readonly SolidColorBrush Fg2 = new(Color.FromRgb(0x95, 0x9C, 0xAD));
    private static readonly SolidColorBrush Fg3 = new(Color.FromRgb(0x5D, 0x64, 0x76));
    private static readonly Brush BigFg = Fz(new SolidColorBrush(Color.FromRgb(0xE9, 0xF0, 0xFF)));
    private static readonly Brush Ember = Fz(new SolidColorBrush(EmberC));
    private static readonly Brush CardFill = Fz(new LinearGradientBrush(Color.FromArgb(0x0B, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x05, 0xFF, 0xFF, 0xFF), 90));
    private static readonly Brush CardEdge = Fz(new SolidColorBrush(Color.FromArgb(0x0E, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush LineBrush = Fz(new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)));
    private static readonly FontFamily Ui = new("Segoe UI Variable Text, Segoe UI");
    private static readonly FontFamily Disp = new("Segoe UI Variable Display, Segoe UI");
    private static readonly FontFamily Num = new("Bahnschrift Light, Bahnschrift, Segoe UI Variable Display");
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    private static readonly IEasingFunction Out = Fz(new CubicEase { EasingMode = EasingMode.EaseOut });

    private static T Fz<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private readonly Grid _root = new();
    private readonly Border _base = new();
    private readonly Border _glow = new() { Opacity = 0 };
    private readonly Switch _vpnSw = new(false), _zapSw = new(true), _tgSw = new(true);
    private readonly Dot _dot = new(), _zapDot = new(), _tgDot = new();
    private readonly TextBlock _title = T("VPN", 16, FontWeights.SemiBold, Fg, Disp);
    private readonly TextBlock _server = T("", 12.5, FontWeights.SemiBold, Fg2);
    private readonly TextBlock _ip = T("", 12, FontWeights.Normal, Fg2, Mono);
    private readonly TextBlock _down = T("0", 58, FontWeights.Normal, BigFg, Num);
    private readonly TextBlock _up = T("0", 28, FontWeights.Normal, Fg2, Num);
    private readonly TextBlock _downTotal = T("", 11.5, FontWeights.Medium, Fg3);
    private readonly TextBlock _upTotal = T("", 11.5, FontWeights.Medium, Fg3);
    private readonly TextBlock _ping = T("—", 22, FontWeights.Normal, Fg, Num);
    private readonly TextBlock _link = T("", 15, FontWeights.SemiBold, Fg);
    private readonly TextBlock _country = T("", 15, FontWeights.SemiBold, Fg);
    private readonly TextBlock _offNote = T(Loc.T("Выключен"), 13, FontWeights.Medium, Fg3);
    private readonly TextBlock _zapNote = T("", 11.5, FontWeights.Medium, Fg3), _tgNote = T("", 11.5, FontWeights.Medium, Fg3);
    private readonly TextBlock _miniPing = T("", 11, FontWeights.Medium, Fg3, Num);

    private Grid? _speedBox;
    private bool _firstReply = true;
    private readonly DispatcherTimer _tick = new();
    private NetState? _s;
    private bool _offline = true, _asking, _demo;
    private DateTime _fastUntil;
    // что сейчас переключается и куда: пока пульт не подтвердит, переключатель не дёргается назад
    private string? _pending;
    private bool _pendingTo;
    private DateTime _pendingUntil;
    private NetTier _tier = NetTier.Medium;

    private NetworkInterface? _nic;
    private (long Rx, long Tx, DateTime At)? _last;
    private double _rx, _tx, _shownRx = -1, _shownTx = -1;
    private long _rxTotal, _txTotal;

    private sealed record NetState(bool VpnOn, bool VpnBusy, string Server, string Country, string Ip, long? Ping, bool ZapretOn, bool ZapretOk, bool TgOn, bool TgOk);

    public NetView(WidgetInfo info) : base(info)
    {
        _vpnSw.Click = ToggleVpn;
        _zapSw.Click = () => Command("zapret", !(_s?.ZapretOn ?? false), _zapSw);
        _tgSw.Click = () => Command("tg", !(_s?.TgOn ?? false), _tgSw);
        _server.Cursor = Cursors.Hand;
        _server.MouseLeftButtonUp += (_, e) => { e.Handled = true; OpenPanel(); };
        _down.Margin = new Thickness(0, -6, 0, -8);
        _up.Margin = new Thickness(0, 0, 0, -4);
        _glow.Background = Fz(new RadialGradientBrush(Color.FromArgb(0x34, AccC.R, AccC.G, AccC.B), Color.FromArgb(0, AccC.R, AccC.G, AccC.B))
            { Center = new Point(0.5, -0.1), GradientOrigin = new Point(0.5, -0.1), RadiusX = 0.9, RadiusY = 0.75 });
        _base.Background = Fz(new RadialGradientBrush(Color.FromRgb(0x14, 0x19, 0x29), Color.FromRgb(0x08, 0x09, 0x0D))
            { Center = new Point(0.5, -0.18), GradientOrigin = new Point(0.5, -0.18), RadiusX = 1.2, RadiusY = 0.9 });
        Children.Add(_root);
        SizeChanged += (_, _) => Clip = new RectangleGeometry(new Rect(RenderSize), Corner, Corner);
        _tick.Tick += (_, _) => Tick();
        Relayout();
    }

    private double Corner => Math.Max(0, Math.Min((Info.Style ?? App.Current.Config.Defaults).Corner, 40) - 1);

    private static TextBlock T(string text, double size, FontWeight weight, Brush brush, FontFamily? font = null) => new()
    {
        Text = text, FontSize = size, FontWeight = weight, Foreground = brush, FontFamily = font ?? Ui, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private static TextBlock Caps(string text) => new()
    {
        Text = text.ToUpper(CultureInfo.CurrentCulture), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Fg3, FontFamily = Ui,
        Margin = new Thickness(0, 0, 0, 3),
    };

    private static TextBlock Unit(string t) => new()
    {
        Text = t, FontSize = 12, FontWeight = FontWeights.Medium, Foreground = Fg3, FontFamily = Ui,
        VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(5, 0, 0, 3),
    };

    public override string Title => Loc.T("Пульт сети");
    public override string Description => Loc.T("VPN, zapret и Телеграм одним касанием, скорость и пинг как в пульте. Сжимается до одной клетки.");
    public override Size BaseSize => BaseFor(WidgetShape.Wide);
    public override Size MinSize => new(92, 92);

    public override IReadOnlyList<(string, Func<bool>, Action<bool>)> Options => new[]
    {
        Opt(Loc.T("Спрашивать перед выключением VPN"), "confirm", false),
    };

    /// <summary>Сколько фона пульта поверх стекла: 0 — прозрачное стекло Плиток, 50 — смешанный, 100 — как в пульте.</summary>
    private double Mix
    {
        get => Info.Props.TryGetValue("mix", out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? Math.Clamp(d, 0, 100) : Flag("own") ? 100 : 0;
        set => Info.Props["mix"] = Math.Round(Math.Clamp(value, 0, 100)).ToString(CultureInfo.InvariantCulture);
    }

    private static string MixName(double m) => m >= 99.5 ? Loc.T("как в пульте") : m <= 0.5 ? Loc.T("прозрачный") : Loc.T("{0}% пульта", Math.Round(m));

    /// <summary>Фон пульта проявляется поверх стекла; чем прозрачнее, тем светлее подписи и с тенью, как у остальных виджетов.</summary>
    private void ShowMix(double m, bool animate)
    {
        var d = TimeSpan.FromMilliseconds(animate ? 450 : 0);
        _base.BeginAnimation(OpacityProperty, new DoubleAnimation(m / 100, d) { EasingFunction = Out });
        double t = m / 100;
        static Color Lerp(Color a, Color b, double t) => Color.FromArgb((byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        var c2 = Lerp(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF), Color.FromRgb(0x95, 0x9C, 0xAD), t);
        var c3 = Lerp(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF), Color.FromRgb(0x5D, 0x64, 0x76), t);
        Fg2.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(c2, d) { EasingFunction = Out });
        Fg3.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(c3, d) { EasingFunction = Out });
        if (_body != null) _body.Effect = m < 60 ? Shadow : null;
    }

    private UIElement? _body;

    public override IEnumerable<object> MenuItems()
    {
        var label = new TextBlock { Text = Loc.T("Фон: {0}", MixName(Mix)) };
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = Mix, Width = 190, Margin = new Thickness(0, 6, 0, 2) };
        slider.ValueChanged += (_, e) => { Mix = e.NewValue; ShowMix(e.NewValue, false); label.Text = Loc.T("Фон: {0}", MixName(e.NewValue)); SaveProps(); };
        MenuItem Preset(string text, double v) => WidgetWindow.Item(text, () => { Mix = v; SaveProps(); ShowMix(v, true); });
        return new object[]
        {
            WidgetWindow.Item(Loc.T("Открыть пульт"), OpenPanel),
            new Separator(),
            Preset(Loc.T("Стиль пульта"), 100), Preset(Loc.T("Смешанный"), 50), Preset(Loc.T("Прозрачный"), 0),
            new MenuItem { Header = new StackPanel { Children = { label, slider } }, StaysOpenOnClick = true },
        };
    }

    public override bool OwnsMouse(DependencyObject? hit) => Inside(hit, _vpnSw) || Inside(hit, _zapSw) || Inside(hit, _tgSw) || Inside(hit, _server);

    private static bool Inside(DependencyObject? d, DependencyObject parent)
    {
        while (d != null) { if (d == parent) return true; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d); }
        return false;
    }

    // ---- размеры ----

    public override void SetArea(double w, double h)
    {
        var t = NetTiers.Of(w, h);
        if (t == _tier) return;
        _tier = t;
        Relayout();
    }

    protected override void OnShape(WidgetShape s) => Relayout();

    public override Size BaseFor(WidgetShape s) => _tier switch
    {
        NetTier.Mini => new(96, 96),
        NetTier.Button => new(170, 160),
        NetTier.Medium => s switch { WidgetShape.Wide => new(360, 176), WidgetShape.Square => new(290, 250), _ => new(190, 320) },
        _ => s switch { WidgetShape.Wide => new(480, 340), WidgetShape.Square => new(330, 440), _ => new(240, 520) },
    };

    public override void ApplyOptions() => Relayout();

    // ---- раскладка ----

    private static void Detach(UIElement e)
    {
        switch (LogicalTreeHelper.GetParent(e) ?? VisualTreeHelper.GetParent(e))
        {
            case Panel p: p.Children.Remove(e); break;
            case Decorator d: d.Child = null; break;
        }
    }

    private static Border Card(UIElement child, Thickness pad) => new()
    {
        Background = CardFill, BorderBrush = CardEdge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16),
        Padding = pad, Child = child,
    };

    private void Relayout()
    {
        foreach (var e in new UIElement[] { _base, _glow, _vpnSw, _zapSw, _tgSw, _dot, _zapDot, _tgDot, _title, _server, _ip, _down, _up,
                     _downTotal, _upTotal, _ping, _link, _country, _offNote, _zapNote, _tgNote, _miniPing })
            Detach(e);
        _root.Children.Clear();
        ShowMix(Mix, false);
        _root.Children.Add(_base);
        _root.Children.Add(_glow);
        _vpnSw.Big = _tier == NetTier.Mini;
        _vpnSw.Margin = new Thickness(0);
        _vpnSw.HorizontalAlignment = HorizontalAlignment.Stretch;
        _title.FontSize = 16;
        _server.Margin = new Thickness(0);
        _ip.Margin = new Thickness(0);
        UIElement body = _tier switch
        {
            NetTier.Mini => Mini(),
            NetTier.Button => Small(),
            NetTier.Medium => Shape == WidgetShape.Wide ? Pad(WideBody(), new Thickness(18, 16, 18, 14)) : Pad(TallBody(Shape == WidgetShape.Tall), new Thickness(16)),
            _ => Shape == WidgetShape.Wide ? WideFull() : TallFull(),
        };
        _body = body;
        body.Effect = Mix < 60 ? Shadow : null;
        _root.Children.Add(body);
        Paint(false);
    }

    private static Border Pad(UIElement c, Thickness t) => new() { Padding = t, Child = c, VerticalAlignment = VerticalAlignment.Center };

    private DockPanel Header()
    {
        _dot.Margin = new Thickness(0, 0, 8, 0);
        _title.VerticalAlignment = VerticalAlignment.Center;
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _dot, _title } };
        var g = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(_vpnSw, Dock.Right);
        g.Children.Add(_vpnSw);
        g.Children.Add(left);
        return g;
    }

    /// <summary>Одна клетка: точка и «VPN», под ними большой переключатель, под ним пинг.</summary>
    private UIElement Mini()
    {
        _dot.Margin = new Thickness(0, 0, 6, 0);
        _title.FontSize = 14;
        var top = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { _dot, _title } };
        _vpnSw.HorizontalAlignment = HorizontalAlignment.Center;
        _vpnSw.Margin = new Thickness(0, 9, 0, 6);
        _miniPing.HorizontalAlignment = HorizontalAlignment.Center;
        return new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { top, _vpnSw, _miniPing } };
    }

    /// <summary>Кнопка: заголовок с переключателем, сервер, крупный пинг.</summary>
    private UIElement Small()
    {
        _ping.FontSize = 34;
        var ping = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0), Children = { _ping, Unit(Loc.T("мс")) } };
        _offNote.Margin = new Thickness(0, 12, 0, 0);
        _offNote.FontSize = 13;
        _offNote.FontFamily = Ui;
        _offNote.FontWeight = FontWeights.Medium;
        var g = new Grid { Children = { ping, _offNote } };
        _server.Margin = new Thickness(15, 2, 0, 0);
        return new StackPanel { Margin = new Thickness(14), VerticalAlignment = VerticalAlignment.Center, Children = { Header(), _server, g } };
    }

    private static StackPanel Arrow(string arrow, string unit, Brush color) => new()
    {
        VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6, 0, 0, 2),
        Children =
        {
            new TextBlock { Text = arrow, FontSize = 14, Foreground = color, FontFamily = Ui },
            new TextBlock { Text = unit, FontSize = 11.5, FontWeight = FontWeights.Medium, Foreground = Fg3, FontFamily = Ui },
        },
    };

    /// <summary>Скорость как в пульте: большая «вниз», малая «вверх», под ними сколько прошло. Выключен: надпись на её месте.</summary>
    private UIElement Speed(bool stacked)
    {
        _down.FontSize = stacked && _tier == NetTier.Medium ? 50 : 58;
        _up.FontSize = 28;
        var dnCol = new StackPanel { Children = { new StackPanel { Orientation = Orientation.Horizontal, Children = { _down, Arrow("↓", Loc.T("Мбит/с"), Acc) } }, _downTotal } };
        var upCol = new StackPanel { Children = { new StackPanel { Orientation = Orientation.Horizontal, Children = { _up, Arrow("↑", Loc.T("Мбит/с"), Fg3) } }, _upTotal } };
        _downTotal.Margin = new Thickness(2, 8, 0, 0);
        _upTotal.Margin = new Thickness(2, 4, 0, 0);
        _offNote.Margin = new Thickness(0, 14, 0, 0);
        _offNote.FontSize = 22;
        _offNote.FontWeight = FontWeights.Normal;
        _offNote.FontFamily = Num;
        _speedBox = new Grid();
        if (stacked)
        {
            _up.FontSize = 24;
            upCol.Margin = new Thickness(0, 10, 0, 0);
            _speedBox.Children.Add(new StackPanel { Children = { dnCol, upCol } });
        }
        else
        {
            upCol.HorizontalAlignment = HorizontalAlignment.Right;
            upCol.VerticalAlignment = VerticalAlignment.Bottom;
            _speedBox.Children.Add(dnCol);
            _speedBox.Children.Add(upCol);
        }
        return new Grid { Margin = new Thickness(0, 12, 0, 0), Children = { _speedBox, _offNote } };
    }

    private UIElement WideBody()
    {
        _server.VerticalAlignment = _ip.VerticalAlignment = VerticalAlignment.Center;
        _ip.Margin = new Thickness(10, 1, 0, 0);
        var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(15, 2, 0, 0), Children = { _server, _ip } };
        return new StackPanel { Children = { Header(), sub, Speed(false) } };
    }

    private UIElement TallBody(bool stacked = true)
    {
        _ip.Margin = new Thickness(0, 2, 0, 0);
        var sub = new StackPanel { Margin = new Thickness(15, 2, 0, 0), Children = { _server, _ip } };
        return new StackPanel { Children = { Header(), sub, Speed(stacked) } };
    }

    private Grid Stats(bool column)
    {
        _ping.FontSize = 22;
        if (column)
        {
            // узкий: подпись слева, значение справа, строки плотно
            _ping.FontSize = 18;
            var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            list.Children.Add(new Border { Height = 1, Background = LineBrush, Margin = new Thickness(0, 0, 0, 4) });
            foreach (var (cap, val) in new (string, UIElement)[]
                     { (Loc.T("Пинг"), new StackPanel { Orientation = Orientation.Horizontal, Children = { _ping, Unit(Loc.T("мс")) } }), (Loc.T("Связь"), _link), (Loc.T("Страна"), _country) })
            {
                var c = Caps(cap);
                c.VerticalAlignment = VerticalAlignment.Center;
                c.Margin = new Thickness(0);
                if (val is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
                var dp = new DockPanel { Margin = new Thickness(0, 6, 0, 0), LastChildFill = false };
                DockPanel.SetDock(val, Dock.Right);
                dp.Children.Add(val);
                dp.Children.Add(c);
                list.Children.Add(dp);
            }
            return new Grid { Children = { list } };
        }
        var cells = new (string Cap, UIElement Val)[]
        {
            (Loc.T("Пинг"), new StackPanel { Orientation = Orientation.Horizontal, Children = { _ping, Unit(Loc.T("мс")) } }),
            (Loc.T("Связь"), _link),
            (Loc.T("Страна"), _country),
        };
        var g = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        var line = new Border { Height = 1, Background = LineBrush, VerticalAlignment = VerticalAlignment.Top };
        for (int i = 0; i < cells.Length; i++)
        {
            var cell = new StackPanel { Children = { Caps(cells[i].Cap), cells[i].Val } };
            if (column) { cell.Margin = new Thickness(0, i == 0 ? 12 : 10, 0, 0); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Grid.SetRow(cell, i); }
            else { cell.Margin = new Thickness(i == 0 ? 0 : 14, 12, 0, 0); g.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetColumn(cell, i); }
            g.Children.Add(cell);
        }
        if (column) Grid.SetRowSpan(line, 3); else Grid.SetColumnSpan(line, 3);
        g.Children.Add(line);
        return g;
    }

    private static Border Row(string name, Switch sw, TextBlock note, Dot d)
    {
        d.Margin = new Thickness(0, 0, 8, 0);
        note.Margin = new Thickness(15, 1, 0, 0);
        var head = new StackPanel { Orientation = Orientation.Horizontal, Children = { d, T(name, 14, FontWeights.SemiBold, Fg, Disp) } };
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { head, note } };
        var dp = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(sw, Dock.Right);
        sw.VerticalAlignment = VerticalAlignment.Center;
        dp.Children.Add(sw);
        dp.Children.Add(left);
        return Card(dp, new Thickness(14, 10, 12, 10));
    }

    private UIElement WideFull()
    {
        var vpn = Card(new StackPanel { Children = { WideBody(), Stats(false) } }, new Thickness(18, 16, 18, 12));
        var rows = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        rows.ColumnDefinitions.Add(new ColumnDefinition());
        rows.ColumnDefinitions.Add(new ColumnDefinition());
        var z = Row("zapret", _zapSw, _zapNote, _zapDot); z.Margin = new Thickness(0, 0, 5, 0);
        var t = Row(Loc.T("Телеграм"), _tgSw, _tgNote, _tgDot); t.Margin = new Thickness(5, 0, 0, 0); Grid.SetColumn(t, 1);
        rows.Children.Add(z); rows.Children.Add(t);
        return new StackPanel { Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Center, Children = { vpn, rows } };
    }

    private UIElement TallFull()
    {
        bool tall = Shape == WidgetShape.Tall;
        var vpn = Card(new StackPanel { Children = { TallBody(tall), Stats(tall) } }, new Thickness(16));
        var z = Row("zapret", _zapSw, _zapNote, _zapDot); z.Margin = new Thickness(0, 10, 0, 0);
        var t = Row(Loc.T("Телеграм"), _tgSw, _tgNote, _tgDot); t.Margin = new Thickness(0, 8, 0, 0);
        return new StackPanel { Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Center, Children = { vpn, z, t } };
    }

    // ---- данные ----

    public override void Demo()
    {
        _demo = true;
        _offline = false;
        bool off = Flag("demo-off", false);
        _s = new NetState(!off, false, Loc.T("Алматы"), "KZ", off ? "" : "203.0.113.47", off ? null : 64, true, true, !off, true);
        _rx = 1_312_500; _tx = 112_000; _rxTotal = 1_310_720_000; _txTotal = 89_128_960;
        _shownRx = _rx; _shownTx = _tx;
        Paint(false);
    }

    protected override void OnStart()
    {
        if (_demo) return;
        _tick.Interval = Interval;
        _tick.Start();
        Tick();
    }

    protected override void OnStop() => _tick.Stop();

    private TimeSpan Interval => DateTime.Now < _fastUntil ? TimeSpan.FromMilliseconds(600)
        : TimeSpan.FromSeconds(App.Current.Config.EcoMode ? 3 : _tier >= NetTier.Medium && _s is { VpnOn: true } ? 1 : 2);

    private void Tick()
    {
        _tick.Interval = Interval;
        if (_s is { VpnOn: true }) ReadSpeed(); else { _rx = _tx = 0; _last = null; }
        Send();
    }

    private void ReadSpeed()
    {
        try
        {
            if (_nic == null || _nic.OperationalStatus != OperationalStatus.Up)
            {
                _nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up &&
                    new[] { "WireGuard", "Amnezia", "Wintun", "sing-box" }.Any(k => n.Description.Contains(k, StringComparison.OrdinalIgnoreCase)));
                _last = null;
            }
            if (_nic == null) { _rx = _tx = 0; return; }
            var st = _nic.GetIPStatistics();
            var now = DateTime.UtcNow;
            if (_last is { } p && now > p.At)
            {
                double sec = (now - p.At).TotalSeconds;
                _rx = Math.Max(0, (st.BytesReceived - p.Rx) / sec);
                _tx = Math.Max(0, (st.BytesSent - p.Tx) / sec);
            }
            _last = (st.BytesReceived, st.BytesSent, now);
            _rxTotal = st.BytesReceived; _txTotal = st.BytesSent;
        }
        catch { _nic = null; }
    }

    private async void Send()
    {
        if (_demo || _asking) return;
        _asking = true;
        try
        {
            var reply = await Task.Run(() => Talk("state"));
            bool was = _offline;
            _offline = reply == null;
            if (reply != null && reply.Trim() != "{}")
            {
                var n = JsonSerializer.Deserialize<NetState>(reply, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (n != null && _pending != null && (DateTime.Now > _pendingUntil || _pending switch
                    {
                        "vpn" => n.VpnOn == _pendingTo && !n.VpnBusy,
                        "zapret" => n.ZapretOn == _pendingTo,
                        _ => n.TgOn == _pendingTo,
                    }))
                    _pending = null;
                _s = n;
            }
            if (was != _offline || _firstReply) App.Current.Log(_offline ? "net: пульт не отвечает" : "net: пульт на связи");
            _firstReply = false;
            Paint(true);
        }
        catch (Exception ex) { App.Current.Log("net failed: " + ex.Message); }
        finally { _asking = false; }
    }

    private static string? Talk(string line)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", "NetPanel-widget", PipeDirection.InOut);
            pipe.Connect(400);
            using var w = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
            using var r = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            w.WriteLine(line);
            return r.ReadLine();
        }
        catch { return null; }
    }

    private void ToggleVpn()
    {
        if (_offline || _s == null) { OpenPanel(); return; }
        if (_s.VpnBusy || _pending == "vpn") return;
        if (_s.VpnOn && Flag("confirm", false) &&
            MessageBox.Show(Loc.T("Выключить VPN?"), Loc.T("Пульт сети"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Command("vpn", !_s.VpnOn, _vpnSw);
    }

    /// <summary>Команда пульту: переключатель сразу едет в новое положение и крутит ожидание, пока пульт не подтвердит.</summary>
    private async void Command(string what, bool on, Switch sw)
    {
        if (_demo) return;
        if (_offline) { OpenPanel(); return; }
        var line = $"{what} {(on ? 1 : 0)}";
        App.Current.Log("net " + line);
        _pending = what;
        _pendingTo = on;
        _pendingUntil = DateTime.Now.AddSeconds(what == "vpn" ? 25 : 10);
        _fastUntil = DateTime.Now.AddSeconds(12);
        sw.Set(on, busy: true);
        if (what == "vpn") Paint(true);
        _tick.Interval = Interval;
        var reply = await Task.Run(() => Talk(line));
        if (reply == null) { _pending = null; _offline = true; App.Current.Log("net: пульт не принял " + line); Paint(true); return; }
        await Task.Delay(500);
        Send();
    }

    private void OpenPanel()
    {
        try { if (File.Exists(PanelExe)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(PanelExe) { UseShellExecute = true }); }
        catch (Exception ex) { App.Current.Log("net open failed: " + ex.Message); }
    }

    private static Dictionary<string, string> Countries => new()
    {
        ["KZ"] = Loc.T("Казахстан"), ["NL"] = Loc.T("Нидерланды"), ["DE"] = Loc.T("Германия"), ["FI"] = Loc.T("Финляндия"), ["US"] = Loc.T("США"), ["TR"] = Loc.T("Турция"),
        ["AE"] = Loc.T("ОАЭ"), ["GB"] = Loc.T("Великобритания"), ["FR"] = Loc.T("Франция"), ["PL"] = Loc.T("Польша"), ["SE"] = Loc.T("Швеция"), ["EE"] = Loc.T("Эстония"),
        ["LV"] = Loc.T("Латвия"), ["LT"] = Loc.T("Литва"), ["AM"] = Loc.T("Армения"), ["GE"] = Loc.T("Грузия"), ["RS"] = Loc.T("Сербия"), ["JP"] = Loc.T("Япония"),
        ["SG"] = Loc.T("Сингапур"), ["HK"] = Loc.T("Гонконг"), ["CA"] = Loc.T("Канада"), ["CH"] = Loc.T("Швейцария"), ["KG"] = Loc.T("Киргизия"), ["UZ"] = Loc.T("Узбекистан"),
        ["RU"] = Loc.T("Россия"), ["BY"] = Loc.T("Беларусь"), ["CZ"] = Loc.T("Чехия"), ["AT"] = Loc.T("Австрия"), ["IT"] = Loc.T("Италия"), ["ES"] = Loc.T("Испания"),
    };

    private static string CountryName(string code)
    {
        if (code.Length != 2) return code;
        if (Countries.TryGetValue(code.ToUpperInvariant(), out var ru)) return ru;
        try { return new RegionInfo(code).EnglishName; } catch { return code; }
    }

    // ---- вид по состоянию ----

    private void Paint(bool animate)
    {
        var s = _s;
        bool vpnPending = _pending == "vpn";
        bool on = !_offline && s is { VpnOn: true };
        bool busy = !_offline && (s is { VpnBusy: true } || vpnPending);
        bool goingOn = vpnPending ? _pendingTo : on;
        _glow.BeginAnimation(OpacityProperty, new DoubleAnimation(goingOn && !_offline ? 1 : 0, TimeSpan.FromMilliseconds(animate ? 700 : 0)) { EasingFunction = Out });
        _dot.Set(_offline ? Dot.Off : busy ? Dot.Busy : on ? Dot.On : Dot.Off);
        if (!vpnPending) _vpnSw.Set(on, s is { VpnBusy: true } && !_offline, !animate);
        if (_speedBox != null) _speedBox.Visibility = on && !busy ? Visibility.Visible : Visibility.Hidden;

        if (_offline || s == null)
        {
            _server.Text = Loc.T("Пульт не запущен");
            _ip.Text = "";
            _offNote.Text = Loc.T("Нажми, чтобы открыть пульт");
            _offNote.Visibility = Visibility.Visible;
            _ping.Text = "—";
            _miniPing.Text = Loc.T("нет пульта");
            _link.Text = _country.Text = "—";
        }
        else
        {
            _server.Text = (s.Server.Length > 0 ? s.Server : CountryName(s.Country)) + "  ›";
            _ip.Text = on ? s.Ip : "";
            _offNote.Text = busy ? (goingOn ? Loc.T("Подключаюсь…") : Loc.T("Отключаюсь…")) : Loc.T("Выключен");
            _offNote.Foreground = busy ? Acc : Fg3;
            _offNote.Visibility = on && !busy ? Visibility.Collapsed : Visibility.Visible;
            _country.Text = s.Country.Length > 0 ? CountryName(s.Country) : "—";
            _link.Text = on ? (s.Ping != null ? Loc.T("есть") : Loc.T("нет ответа")) : "—";
            _link.Foreground = on && s.Ping == null ? Ember : Fg;
            _ping.Text = on && s.Ping is { } p ? p.ToString(CultureInfo.InvariantCulture) : "—";
            _miniPing.Text = busy ? "…" : on && s.Ping is { } mp ? Loc.T("{0} мс", mp) : Loc.T("выкл");
            if (_pending != "zapret") _zapSw.Set(s.ZapretOn, false, !animate);
            if (_pending != "tg") _tgSw.Set(s.TgOn, false, !animate);
            _zapDot.Set(s.ZapretOn ? (s.ZapretOk ? Dot.On : Dot.Warn) : Dot.Off);
            _tgDot.Set(s.TgOn ? (s.TgOk ? Dot.On : Dot.Warn) : Dot.Off);
            _zapNote.Text = _pending == "zapret" ? Loc.T("переключаю…") : s.ZapretOn ? (s.ZapretOk ? Loc.T("YouTube и Discord") : Loc.T("не отвечает")) : Loc.T("выключен");
            _tgNote.Text = _pending == "tg" ? Loc.T("переключаю…") : s.TgOn ? (s.TgOk ? Loc.T("работает") : Loc.T("не отвечает")) : Loc.T("выключен");
            _zapNote.Foreground = s.ZapretOn && !s.ZapretOk ? Ember : Fg3;
            _tgNote.Foreground = s.TgOn && !s.TgOk ? Ember : Fg3;
        }
        _downTotal.Text = NetTiers.Total(_rxTotal, Loc.Culture);
        _upTotal.Text = NetTiers.Total(_txTotal, Loc.Culture);
        Count(animate);
    }

    // плавный пересчёт цифр скорости за полсекунды, без вечной анимации
    private double _fromRx, _fromTx;
    private DateTime _countStart;
    private bool _counting;

    private void Count(bool animate)
    {
        if (!animate || _shownRx < 0) { _shownRx = _rx; _shownTx = _tx; ShowSpeed(); return; }
        _fromRx = _shownRx; _fromTx = _shownTx; _countStart = DateTime.Now;
        if (_counting) return;
        _counting = true;
        CompositionTarget.Rendering += CountFrame;
    }

    private void CountFrame(object? sender, EventArgs e)
    {
        double t = Math.Min(1, (DateTime.Now - _countStart).TotalMilliseconds / 500);
        double q = 1 - Math.Pow(1 - t, 3);
        _shownRx = _fromRx + (_rx - _fromRx) * q;
        _shownTx = _fromTx + (_tx - _fromTx) * q;
        ShowSpeed();
        if (t >= 1) { CompositionTarget.Rendering -= CountFrame; _counting = false; }
    }

    private void ShowSpeed()
    {
        _down.Text = NetTiers.Mbit(_shownRx, Loc.Culture);
        _up.Text = NetTiers.Mbit(_shownTx, Loc.Culture);
    }

    // ---- переключатель и точка как в пульте ----

    /// <summary>Переключатель пульта: серая дорожка, голубой градиент во включённом, ручка едет с пружинкой, при ожидании крутится дуга.</summary>
    private sealed class Switch : Grid
    {
        private static readonly Brush KnobOff = Fz(new SolidColorBrush(Color.FromRgb(0xC3, 0xC8, 0xD3)));
        private readonly Border _track = new() { Background = Fz(new SolidColorBrush(Color.FromArgb(0x13, 0xFF, 0xFF, 0xFF))), BorderBrush = Fz(new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF))), BorderThickness = new Thickness(1) };
        private readonly Border _fill = new() { Opacity = 0, Background = Fz(new LinearGradientBrush(Color.FromRgb(0xB3, 0xCD, 0xFF), Color.FromRgb(0x7F, 0xA6, 0xFF), 90)) };
        private readonly Ellipse _knob = new() { Fill = KnobOff };
        private readonly Ellipse _spin = new() { StrokeThickness = 2.2, Stroke = Fz(new SolidColorBrush(Color.FromArgb(0xF2, 0x30, 0x52, 0xA8))), Opacity = 0, StrokeDashCap = PenLineCap.Round };
        private readonly TranslateTransform _move = new();
        private readonly RotateTransform _turn = new();
        private readonly Grid _knobBox = new();
        private readonly bool _small;
        private double _w, _h, _k;
        private bool _busy;
        public Action? Click;
        public bool On { get; private set; }

        public Switch(bool small)
        {
            _small = small;
            Cursor = Cursors.Hand;
            Background = Brushes.Transparent;
            _fill.Effect = new DropShadowEffect { Color = AccC, BlurRadius = 20, ShadowDepth = 0, Opacity = 0.4 };
            _knob.Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1.5, Opacity = 0.45 };
            _spin.RenderTransform = _turn;
            _knobBox.Children.Add(_knob);
            _knobBox.Children.Add(_spin);
            _knobBox.HorizontalAlignment = HorizontalAlignment.Left;
            _knobBox.VerticalAlignment = VerticalAlignment.Top;
            _knobBox.RenderTransform = _move;
            Children.Add(_track); Children.Add(_fill); Children.Add(_knobBox);
            Resize(1);
            MouseLeftButtonDown += (_, e) => e.Handled = true;
            MouseLeftButtonUp += (_, e) => { e.Handled = true; Click?.Invoke(); };
            MouseEnter += (_, _) => _track.BeginAnimation(OpacityProperty, new DoubleAnimation(1.6, TimeSpan.FromMilliseconds(150)));
            MouseLeave += (_, _) => _track.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
        }

        public bool Big { set => Resize(value ? 1.3 : 1); }

        private double Travel => _w - _k - (_h - _k);

        private void Resize(double scale)
        {
            _w = (_small ? 40 : 46) * scale; _h = (_small ? 24 : 28) * scale; _k = (_small ? 18 : 22) * scale;
            Width = _w; Height = _h;
            _track.CornerRadius = _fill.CornerRadius = new CornerRadius(_h / 2);
            double pad = (_h - _k) / 2;
            _knobBox.Width = _knobBox.Height = _k;
            _knob.Width = _knob.Height = _k;
            double sp = _k * 0.62;
            _spin.Width = _spin.Height = sp;
            _spin.StrokeDashArray = new DoubleCollection { sp * Math.PI * 0.6 / 2.2, 100 };
            _turn.CenterX = _turn.CenterY = sp / 2;
            _knobBox.Margin = new Thickness(pad, pad, 0, 0);
            _move.BeginAnimation(TranslateTransform.XProperty, null);
            _move.X = On ? Travel : 0;
        }

        public void Set(bool on, bool busy, bool instant = false)
        {
            if (on != On)
            {
                On = on;
                _knob.Fill = on ? Brushes.White : KnobOff;
                if (instant)
                {
                    _move.BeginAnimation(TranslateTransform.XProperty, null);
                    _fill.BeginAnimation(OpacityProperty, null);
                    _move.X = on ? Travel : 0;
                    _fill.Opacity = on ? 1 : 0;
                }
                else
                {
                    _move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(on ? Travel : 0, TimeSpan.FromMilliseconds(500))
                        { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } });
                    _fill.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(350)) { EasingFunction = Out });
                }
            }
            if (busy == _busy) return;
            _busy = busy;
            _spin.BeginAnimation(OpacityProperty, new DoubleAnimation(busy ? 1 : 0, TimeSpan.FromMilliseconds(200)));
            if (busy)
            {
                var a = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(800)) { RepeatBehavior = RepeatBehavior.Forever };
                Timeline.SetDesiredFrameRate(a, 30);
                _turn.BeginAnimation(RotateTransform.AngleProperty, a);
                var pulse = new DoubleAnimation(1, 0.55, TimeSpan.FromMilliseconds(650)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                Timeline.SetDesiredFrameRate(pulse, 24);
                _fill.BeginAnimation(OpacityProperty, pulse);
            }
            else
            {
                _turn.BeginAnimation(RotateTransform.AngleProperty, null);
                _fill.BeginAnimation(OpacityProperty, new DoubleAnimation(On ? 1 : 0, TimeSpan.FromMilliseconds(250)));
            }
        }
    }

    /// <summary>Точка у заголовка: голубая со свечением (работает), серая (выключено), оранжевая (беда), мигает при переключении.</summary>
    private sealed class Dot : Grid
    {
        public const int Off = 0, On = 1, Busy = 2, Warn = 3;
        private readonly Ellipse _core = new() { Width = 7, Height = 7 };
        private readonly Ellipse _halo = new() { Width = 13, Height = 13 };
        private int _state = -1;

        public Dot()
        {
            Width = Height = 13;
            VerticalAlignment = VerticalAlignment.Center;
            Children.Add(_halo); Children.Add(_core);
        }

        public void Set(int state)
        {
            if (state == _state) return;
            _state = state;
            var c = state switch { On or Busy => AccC, Warn => EmberC, _ => Color.FromRgb(0x5D, 0x64, 0x76) };
            bool lit = state != Off;
            _core.Fill = new SolidColorBrush(c);
            _halo.Fill = new SolidColorBrush(Color.FromArgb(lit ? (byte)0x2A : (byte)0, c.R, c.G, c.B));
            _core.Effect = lit ? new DropShadowEffect { Color = c, BlurRadius = 9, ShadowDepth = 0, Opacity = 0.85 } : null;
            if (state == Busy)
            {
                var a = new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(650)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                Timeline.SetDesiredFrameRate(a, 24);
                BeginAnimation(OpacityProperty, a);
            }
            else BeginAnimation(OpacityProperty, null);
        }
    }
}
