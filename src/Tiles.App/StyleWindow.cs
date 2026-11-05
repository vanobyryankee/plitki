using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Tiles.Core;

namespace Tiles.App;

/// <summary>
/// Окно настроек вида: одной папки или общего шаблона. Всё меняется сразу, «Отмена» возвращает как было.
/// </summary>
internal sealed class StyleWindow : Window
{
    private static (string Name, string Hex)[] Tints => new (string, string)[]
    {
        (Loc.T("Светлое"), "#FFFFFF"), (Loc.T("Тёмное"), "#000000"), (Loc.T("Синее"), "#3D7BFF"), (Loc.T("Фиолетовое"), "#8A5CFF"),
        (Loc.T("Розовое"), "#FF5C9A"), (Loc.T("Зелёное"), "#3CCB7F"), (Loc.T("Янтарное"), "#FFB547"),
    };
    private static readonly Brush Text = Brushes.White;
    private static readonly Brush Dim = new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF));

    private readonly TileStyle _style;
    private readonly Action<TileStyle> _changed;
    private readonly StackPanel _body = new();
    private readonly List<Action> _reload = new();

    public TileStyle Result => _style;

    /// <param name="extraButtons">Кнопки внизу: подпись и что сделать с текущими настройками.</param>
    public StyleWindow(string title, TileStyle style, Action<TileStyle> changed,
        params (string Text, Func<TileStyle, TileStyle?> Act)[] extraButtons)
        : this(title, style, changed, null, extraButtons) { }

    /// <param name="widgetOptions">Для виджета: его собственные галочки вместо раздела про значки папок.</param>
    public StyleWindow(string title, TileStyle style, Action<TileStyle> changed,
        IReadOnlyList<(string Label, Func<bool> Get, Action<bool> Set)>? widgetOptions,
        (string Text, Func<TileStyle, TileStyle?> Act)[] extraButtons)
    {
        _style = style.Clone();
        _changed = changed;
        Title = title;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 460;
        Topmost = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10), Background = Brushes.Transparent };
        var close = new Button
        {
            Content = "✕", Width = 28, Height = 28, Foreground = Text, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Cursor = Cursors.Hand, FontSize = 13,
        };
        close.Click += (_, _) => { DialogResult = false; };
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(new TextBlock { Text = title, Foreground = Text, FontSize = 17, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        header.MouseLeftButtonDown += (_, _) => DragMove();

        if (widgetOptions != null)
        {
            if (widgetOptions.Count > 0) Section(Loc.T("Что показывать"));
            foreach (var (label, get, set) in widgetOptions) Check(label, get, v => set(v));
        }
        else
        {
            Section(Loc.T("Расположение"));
            Check(Loc.T("Прилипать к клеткам ярлыков Windows"), () => _style.SnapToGrid, v => _style.SnapToGrid = v);
            Check(Loc.T("Показывать название папки"), () => _style.ShowName, v => _style.ShowName = v);

            Section(Loc.T("Значки"));
            Slide(Loc.T("Размер значков"), TileStyle.MinIcon, TileStyle.MaxIcon, () => _style.IconSize, v => _style.IconSize = v, Loc.T(" пикс."));
            Check(Loc.T("Имена под значками на плитке"), () => _style.TileCaptions, v => _style.TileCaptions = v);
            Check(Loc.T("Имена под значками в открытой папке"), () => _style.PanelCaptions, v => _style.PanelCaptions = v);
            Check(Loc.T("Клик по значку на плитке сразу запускает"), () => _style.ClickLaunch, v => _style.ClickLaunch = v);
            Check(Loc.T("Меню Windows по правой кнопке на значках"), () => _style.ShellMenu, v => _style.ShellMenu = v);
        }

        Section(Loc.T("Стекло"));
        Check(Loc.T("Стеклянная подложка (выкл — только значки)"), () => _style.Glass, v => _style.Glass = v);
        Slide(Loc.T("Густота оттенка"), 0, 100, () => _style.GlassOpacity, v => _style.GlassOpacity = v, " %");
        Slide(Loc.T("Размытие"), 0, TileStyle.MaxBlur, () => _style.Blur, v => _style.Blur = v, "");
        TintRow();
        Slide(Loc.T("Тень"), 0, 100, () => _style.Shadow, v => _style.Shadow = v, " %");
        Slide(Loc.T("Светлая кромка"), 0, 100, () => _style.Edge, v => _style.Edge = v, " %");
        Slide(Loc.T("Скругление углов"), 0, TileStyle.MaxCorner, () => _style.Corner, v => _style.Corner = v, "");

        var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (text, act) in extraButtons)
        {
            var b = MakeButton(text, false);
            b.Click += (_, _) =>
            {
                var replaced = act(_style);
                if (replaced != null) { Copy(replaced, _style); Notify(); foreach (var r in _reload) r(); }
            };
            buttons.Children.Add(b);
        }
        var cancel = MakeButton(Loc.T("Отмена"), false);
        cancel.Click += (_, _) => DialogResult = false;
        var ok = MakeButton(Loc.T("Готово"), true);
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        Content = new Border
        {
            CornerRadius = new CornerRadius(16), Padding = new Thickness(20, 14, 20, 18),
            Background = new SolidColorBrush(Color.FromArgb(0xF4, 0x22, 0x22, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
            Child = new StackPanel { Children = { header, _body, buttons } },
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) DialogResult = false; };
    }

    private void Notify() => _changed(_style.Clone());

    private static void Copy(TileStyle from, TileStyle to)
    {
        foreach (var p in typeof(TileStyle).GetProperties()) p.SetValue(to, p.GetValue(from));
    }

    private void Section(string text) => _body.Children.Add(new TextBlock
    {
        Text = text.ToUpperInvariant(), Foreground = Dim, FontSize = 11, FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, _body.Children.Count == 0 ? 0 : 14, 0, 6),
    });

    private void Check(string text, Func<bool> get, Action<bool> set)
    {
        var cb = new CheckBox
        {
            Content = new TextBlock { Text = text, Foreground = Text, FontSize = 13 },
            IsChecked = get(), Margin = new Thickness(0, 4, 0, 4), VerticalContentAlignment = VerticalAlignment.Center,
        };
        cb.Click += (_, _) => { set(cb.IsChecked == true); Notify(); };
        _reload.Add(() => cb.IsChecked = get());
        _body.Children.Add(cb);
    }

    private void Slide(string text, int min, int max, Func<int> get, Action<int> set, string unit)
    {
        var value = new TextBlock { Foreground = Dim, FontSize = 12, Width = 60, TextAlignment = TextAlignment.Right };
        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = get(), IsSnapToTickEnabled = true, TickFrequency = 1,
            Margin = new Thickness(0, 2, 0, 4),
        };
        void Show() => value.Text = (int)slider.Value + unit;
        Show();
        slider.ValueChanged += (_, _) => { set((int)slider.Value); Show(); Notify(); };
        _reload.Add(() => slider.Value = get());
        var top = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(value, Dock.Right);
        top.Children.Add(value);
        top.Children.Add(new TextBlock { Text = text, Foreground = Text, FontSize = 13 });
        _body.Children.Add(top);
        _body.Children.Add(slider);
    }

    private void TintRow()
    {
        _body.Children.Add(new TextBlock { Text = Loc.T("Оттенок"), Foreground = Text, FontSize = 13, Margin = new Thickness(0, 6, 0, 6) });
        var row = new WrapPanel();
        var swatches = new List<(Border B, string Hex)>();
        void Mark()
        {
            foreach (var (b, hex) in swatches)
                b.BorderBrush = string.Equals(hex, _style.Tint, StringComparison.OrdinalIgnoreCase) ? Brushes.White : Brushes.Transparent;
        }
        foreach (var (name, hex) in Tints)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var b = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 0, 8, 0),
                BorderThickness = new Thickness(2), Cursor = Cursors.Hand, ToolTip = name,
                Child = new Border { CornerRadius = new CornerRadius(12), Margin = new Thickness(2), Background = new SolidColorBrush(color) },
            };
            b.MouseLeftButtonUp += (_, _) => { _style.Tint = hex; Mark(); Notify(); };
            swatches.Add((b, hex));
            row.Children.Add(b);
        }
        Mark();
        _reload.Add(Mark);
        _body.Children.Add(row);
    }

    private static Button MakeButton(string text, bool primary)
    {
        var b = new Button
        {
            Content = text, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 6, 0, 0),
            Foreground = Brushes.White, Cursor = Cursors.Hand, FontSize = 13, BorderThickness = new Thickness(0),
            Background = primary ? new SolidColorBrush(Color.FromRgb(0x3D, 0x7B, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
        };
        // скруглённая кнопка без стандартной рамки
        var tpl = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        bd.AppendChild(cp);
        tpl.VisualTree = bd;
        b.Template = tpl;
        return b;
    }
}
