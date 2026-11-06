using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using System.Windows.Threading;
using Tiles.Core;

namespace Tiles.App;

/// <summary>
/// Окно приложения: системное стекло Windows 11 (акрил), боковое меню и четыре страницы —
/// мастерская виджетов, папки, внешний вид, нагрузка. Закрыли — окно освобождается целиком.
/// </summary>
internal sealed class HubWindow : Window
{
    private readonly bool _preview;
    private readonly ContentControl _page = new();
    private readonly Dictionary<string, Border> _nav = new();
    private readonly DispatcherTimer _load = new() { Interval = TimeSpan.FromSeconds(1) };
    private string _current = "";

    public HubWindow(bool preview = false)
    {
        _preview = preview;
        Title = Loc.T("Плитки");
        Width = 1120; Height = 740; MinWidth = 900; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        Background = Brushes.Transparent;
        FontFamily = Ui.Body;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            GlassFrameThickness = new Thickness(-1), CaptionHeight = 0, ResizeBorderThickness = new Thickness(6),
            CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false,
        });
        SourceInitialized += (_, _) => Backdrop();

        var side = Sidebar();
        var content = new Grid();
        content.Children.Add(new ScrollViewer
        {
            Content = _page, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Padding = new Thickness(44, 64, 44, 40), // колесо крутит, полосы нет
            Focusable = false,
        });
        content.Children.Add(Caption());

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(248) });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        // лёгкий тон поверх системного стекла: текст читается на любых обоях
        root.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x70, 0x12, 0x13, 0x18)) });
        Grid.SetColumnSpan(root.Children[0], 2);
        var sideBack = new Border { Background = new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0x00, 0x00)), BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 1, 0) };
        root.Children.Add(sideBack);
        root.Children.Add(side);
        Grid.SetColumn(content, 1);
        root.Children.Add(content);
        Content = root;

        _load.Tick += (_, _) => _loadTick?.Invoke();
        Closed += (_, _) => { _load.Stop(); App.Current.WidgetsChanged -= OnWidgetsChanged; App.Current.PresetsChanged -= OnPresetsChanged; };
        App.Current.WidgetsChanged += OnWidgetsChanged;
        App.Current.PresetsChanged += OnPresetsChanged;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    private void OnWidgetsChanged() { if (_current == "workshop") ShowPage("workshop", animate: false); }
    private void OnPresetsChanged() { if (_current == "folders") ShowPage("folders", animate: false); }

    // ---- стекло Windows 11 ----

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private void Backdrop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (HwndSource.FromHwnd(hwnd) is { } src) src.CompositionTarget.BackgroundColor = Colors.Transparent;
        int dark = 1, mica = 2, round = 2;
        DwmSetWindowAttribute(hwnd, 20, ref dark, 4);     // тёмная рамка
        DwmSetWindowAttribute(hwnd, 33, ref round, 4);    // скруглённые углы
        if (DwmSetWindowAttribute(hwnd, 38, ref mica, 4) != 0) // слюду не дали — тёмная заливка
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x1C, 0x1D, 0x22));
    }

    // ---- рамка окна: перетаскивание, свернуть, закрыть ----

    private FrameworkElement Caption()
    {
        var bar = new Grid { Height = 48, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent };
        bar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else DragMove();
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        buttons.Children.Add(CapButton("M6,12 L18,12", () => WindowState = WindowState.Minimized, false));
        buttons.Children.Add(CapButton("M6.5,6.5 L17.5,17.5 M17.5,6.5 L6.5,17.5", Close, true));
        bar.Children.Add(buttons);
        return bar;
    }

    private static Border CapButton(string glyph, Action click, bool danger)
    {
        var b = new Border { Width = 46, Height = 36, Background = Brushes.Transparent, Child = Glyphs.Make(glyph, 14, 1.4) };
        var hover = danger ? new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C)) : Ui.CardHover;
        b.MouseEnter += (_, _) => b.Background = hover;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonDown += (_, e) => e.Handled = true; // иначе полоса окна начинает перетаскивание и съедает клик
        b.MouseLeftButtonUp += (_, e) => { click(); e.Handled = true; };
        return b;
    }

    // ---- боковое меню ----

    private FrameworkElement Sidebar()
    {
        var logo = new Grid { Width = 40, Height = 40 };
        logo.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)),
            Background = new LinearGradientBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF), 45),
        });
        var dots = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Rows = 2, Margin = new Thickness(9) };
        foreach (var c in new[] { 0xFFFFFF, 0x6CB8FF, 0x6CB8FF, 0xFFFFFF })
            dots.Children.Add(new Border { CornerRadius = new CornerRadius(3), Margin = new Thickness(1.5), Background = new SolidColorBrush(Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c)) });
        logo.Children.Add(dots);

        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 26, 0, 30) };
        brand.Children.Add(logo);
        var names = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Ui.T(Loc.T("Плитки"), 20, FontWeights.SemiBold, Ui.Text, Ui.Display));
        names.Children.Add(Ui.T(Loc.T("стеклянный рабочий стол"), 12, FontWeights.Normal, Ui.Faint));
        brand.Children.Add(names);

        var list = new StackPanel { Margin = new Thickness(12, 0, 12, 0) };
        list.Children.Add(NavItem("workshop", Loc.T("Мастерская"), Glyphs.Workshop));
        list.Children.Add(NavItem("folders", Loc.T("Папки"), Glyphs.Folders));
        list.Children.Add(NavItem("look", Loc.T("Внешний вид"), Glyphs.Brush));
        list.Children.Add(NavItem("load", Loc.T("Нагрузка"), Glyphs.Gauge));

        var foot = Ui.T(Loc.T("Двигай что угодно мышью. Рамкой по столу или Ctrl+клик — выделить несколько."), 12, FontWeights.Normal, Ui.Faint);
        foot.Margin = new Thickness(24, 0, 24, 24);
        foot.VerticalAlignment = VerticalAlignment.Bottom;

        var dock = new DockPanel();
        DockPanel.SetDock(brand, Dock.Top);
        DockPanel.SetDock(foot, Dock.Bottom);
        dock.Children.Add(brand);
        dock.Children.Add(foot);
        dock.Children.Add(list);
        dock.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == dock || e.OriginalSource is TextBlock) try { DragMove(); } catch { } };
        dock.Background = Brushes.Transparent;
        return dock;
    }

    private Border NavItem(string id, string text, string glyph)
    {
        var pill = new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(2), Background = Ui.Accent, Opacity = 0, Margin = new Thickness(0, 0, 10, 0) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(pill);
        row.Children.Add(Glyphs.Make(glyph, 19, 1.7));
        var t = Ui.T(text, 14.5, FontWeights.SemiBold);
        t.Margin = new Thickness(12, 0, 0, 1);
        t.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(t);
        var b = new Border { Height = 42, CornerRadius = new CornerRadius(10), Padding = new Thickness(4, 0, 12, 0), Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent, Child = row, Cursor = Cursors.Hand, Tag = pill };
        b.MouseEnter += (_, _) => { if (_current != id) b.Background = Ui.Card; };
        b.MouseLeave += (_, _) => { if (_current != id) b.Background = Brushes.Transparent; };
        b.MouseLeftButtonUp += (_, _) => ShowPage(id);
        _nav[id] = b;
        return b;
    }

    // ---- страницы ----

    private Action? _loadTick;

    public void ShowPage(string id, bool animate = true)
    {
        _current = id;
        foreach (var (key, b) in _nav)
        {
            b.Background = key == id ? Ui.CardHover : Brushes.Transparent;
            ((Border)b.Tag).Opacity = key == id ? 1 : 0;
        }
        _load.Stop();
        _loadTick = null;
        FrameworkElement page = id switch
        {
            "folders" => FoldersPage(),
            "look" => LookPage(),
            "load" => LoadPage(),
            _ => WorkshopPage(),
        };
        _page.Content = page;
        if (!animate || _preview) return;
        var tr = new TranslateTransform();
        page.RenderTransform = tr;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
    }

    private static StackPanel Header(string title, string sub, FrameworkElement? action = null)
    {
        var head = new Grid { Margin = new Thickness(0, 0, 0, 26) };
        var text = new StackPanel();
        text.Children.Add(Ui.H1(title));
        var s = Ui.Sub(sub);
        s.Margin = new Thickness(0, 6, 0, 0);
        s.MaxWidth = 620; s.HorizontalAlignment = HorizontalAlignment.Left;
        text.Children.Add(s);
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(text);
        if (action != null)
        {
            action.HorizontalAlignment = HorizontalAlignment.Right; action.VerticalAlignment = VerticalAlignment.Bottom;
            action.Margin = new Thickness(24, 0, 0, 2);
            Grid.SetColumn(action, 1);
            head.Children.Add(action);
        }
        var page = new StackPanel();
        page.Children.Add(head);
        return page;
    }

    /// <summary>Тёмная сцена под превью: глубокий градиент с мягким цветным пятном, у каждой карточки своё.</summary>
    private static readonly Color[] Hues =
    {
        Color.FromRgb(0x3B, 0x5B, 0xA8), Color.FromRgb(0x6A, 0x3F, 0x9E), Color.FromRgb(0x23, 0x7A, 0x8A),
        Color.FromRgb(0x9A, 0x4A, 0x6E), Color.FromRgb(0x2F, 0x7D, 0x5B), Color.FromRgb(0xA0, 0x6A, 0x30),
    };

    private static Brush Wall(int i)
    {
        var hue = Hues[i % Hues.Length];
        var b = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.25, 0.2), Center = new Point(0.3, 0.25), RadiusX = 0.9, RadiusY = 1.1,
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0xD0, hue.R, hue.G, hue.B), 0),
                new GradientStop(Color.FromRgb(0x16, 0x18, 0x20), 1),
            },
        };
        b.Freeze();
        return b;
    }

    /// <summary>Стекло как у плиток — для превью в окне.</summary>
    private static Border GlassOf(FrameworkElement child, double corner)
    {
        var s = App.Current.Config.Defaults;
        uint tint = s.TintArgb();
        return new Border
        {
            CornerRadius = new CornerRadius(corner), BorderThickness = new Thickness(1), Child = child,
            Background = new SolidColorBrush(Color.FromArgb((byte)Math.Max(0x26, tint >> 24), (byte)(tint >> 16), (byte)(tint >> 8), (byte)tint)),
            BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(s.Edge * 255 / 100), 0xFF, 0xFF, 0xFF)),
        };
    }

    private FrameworkElement WorkshopPage()
    {
        var page = Header(Loc.T("Мастерская"), Loc.T("Виджеты для рабочего стола. Включи нужные — они встанут на стол стеклом, как папки. Двигай мышью, тяни за край, чтобы увеличить."));
        var wrap = new WrapPanel();
        int i = 0;
        foreach (var info in App.Current.Config.Widgets)
            wrap.Children.Add(WidgetCard(info, i++));
        page.Children.Add(wrap);
        return page;
    }

    private Border WidgetCard(WidgetInfo info, int index)
    {
        // своё превью: отдельная копия виджета с выдуманными данными, без таймеров
        var demo = WidgetView.Create(new WidgetInfo { Kind = info.Kind, Props = new(info.Props) });
        var b = demo.BaseSize;
        demo.Width = b.Width; demo.Height = b.Height;
        demo.Demo();
        var glass = GlassOf(demo, Math.Min(App.Current.Config.Defaults.Corner, 24));
        FrameworkElement shown = glass;
        if (demo.Label is { } label)
        {
            var sp = new StackPanel { Children = { glass } };
            var lt = Ui.T(label, 13, FontWeights.SemiBold, Ui.Text, Ui.Display);
            lt.HorizontalAlignment = HorizontalAlignment.Center; lt.Margin = new Thickness(0, 4, 0, 0);
            lt.Effect = WidgetView.Shadow;
            sp.Children.Add(lt);
            shown = sp;
        }
        var stage = new Border
        {
            Height = 168, CornerRadius = new CornerRadius(14, 14, 0, 0), Background = Wall(index), ClipToBounds = true,
            Child = new Viewbox { Stretch = Stretch.Uniform, Margin = new Thickness(26, 22, 26, 22), Child = shown, MaxHeight = demo.Label != null ? 110 : 140 },
        };

        var toggle = new Toggle(info.Enabled);
        toggle.Changed += on => App.Current.SetWidget(info.Kind, on);
        var name = Ui.T(demo.Title, 16, FontWeights.SemiBold, Ui.Text, Ui.Display);
        var titleRow = new DockPanel { Margin = new Thickness(18, 14, 16, 4) };
        DockPanel.SetDock(toggle, Dock.Right);
        titleRow.Children.Add(toggle);
        titleRow.Children.Add(name);
        var desc = Ui.T(demo.Description, 12.5, FontWeights.Normal, Ui.Soft);
        desc.Margin = new Thickness(18, 0, 18, 16);
        var state = Ui.T(info.Enabled ? Loc.T("На столе") : Loc.T("Выключен"), 11.5, FontWeights.SemiBold, info.Enabled ? Ui.Accent : Ui.Faint);
        state.Margin = new Thickness(18, 0, 18, 16);

        var card = new Border
        {
            Width = 316, Margin = new Thickness(0, 0, 18, 18), CornerRadius = new CornerRadius(15), Background = Ui.Card,
            BorderBrush = Ui.Line, BorderThickness = new Thickness(1),
            Child = new StackPanel { Children = { stage, titleRow, desc, state } },
        };
        card.MouseEnter += (_, _) => { card.Background = Ui.CardHover; card.BorderBrush = Ui.Faint; };
        card.MouseLeave += (_, _) => { card.Background = Ui.Card; card.BorderBrush = Ui.Line; };
        if (!_preview)
        {
            card.Opacity = 0;
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = TimeSpan.FromMilliseconds(40 * index) });
        }
        return card;
    }

    private FrameworkElement FoldersPage()
    {
        var page = Header(Loc.T("Папки"), Loc.T("Плитки на рабочем столе. Ярлыки внутри — обычные файлы в папке «Desktop-Плитки»."),
            Ui.Button(Loc.T("Новая папка"), () => { App.Current.NewFolder(); ShowPage("folders", false); }, primary: true, glyph: Glyphs.Plus));
        foreach (var tile in App.Current.Tiles.OrderBy(t => t.Folder))
        {
            var items = App.Current.OrderedItems(tile.Folder);
            var icons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Width = 132 };
            foreach (var p in items.Take(4))
            {
                var img = Icons.Image(p, 28);
                img.Width = img.Height = 28;
                img.Margin = new Thickness(0, 0, 5, 0);
                icons.Children.Add(img);
            }
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            names.Children.Add(Ui.T(tile.Folder, 15.5, FontWeights.SemiBold, Ui.Text, Ui.Display));
            names.Children.Add(Ui.T(Count(items.Count), 12.5, FontWeights.Normal, Ui.Soft));
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var t = tile;
            actions.Children.Add(Ui.Button(Loc.T("Настроить"), () => App.Current.OpenSettings(t)));
            actions.Children.Add(new Border { Width = 8 });
            actions.Children.Add(Ui.Button(Loc.T("Переименовать"), () => { App.Current.RenameFolder(t); ShowPage("folders", false); }));
            actions.Children.Add(new Border { Width = 8 });
            actions.Children.Add(Ui.Button(Loc.T("Удалить"), () => { App.Current.DeleteFolder(t); ShowPage("folders", false); }));
            var row = new DockPanel();
            DockPanel.SetDock(icons, Dock.Left);
            DockPanel.SetDock(actions, Dock.Right);
            row.Children.Add(icons);
            row.Children.Add(actions);
            row.Children.Add(names);
            var card = new Border
            {
                CornerRadius = new CornerRadius(13), Background = Ui.Card, BorderBrush = Ui.Line, BorderThickness = new Thickness(1),
                Padding = new Thickness(18, 14, 14, 14), Margin = new Thickness(0, 0, 0, 10), Child = row,
            };
            card.MouseEnter += (_, _) => card.Background = Ui.CardHover;
            card.MouseLeave += (_, _) => card.Background = Ui.Card;
            page.Children.Add(card);
        }
        AddPresets(page);
        return page;
    }

    /// <summary>Расстановки стола: сохранить текущую, поставить, переименовать, удалить.</summary>
    private void AddPresets(Panel page)
    {
        var head = new DockPanel { Margin = new Thickness(0, 26, 0, 4) };
        var save = Ui.Button(Loc.T("Сохранить текущую…"), () => App.Current.SavePresetAsk(), glyph: Glyphs.Plus);
        DockPanel.SetDock(save, Dock.Right);
        head.Children.Add(save);
        head.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Ui.T(Loc.T("Расстановки"), 20, FontWeights.SemiBold, Ui.Text, Ui.Display),
                Ui.T(Loc.T("Запоминают всё: где и какого размера папки и виджеты, их вид и сетку. Последняя выбранная возвращается сама после игры или смены мониторов."), 12.5, FontWeights.Normal, Ui.Soft),
            },
        });
        page.Children.Add(head);
        foreach (var p in App.Current.Config.Presets)
        {
            var name = p.Name;
            bool active = string.Equals(name, App.Current.Config.ActivePreset, StringComparison.CurrentCultureIgnoreCase);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(Ui.T(active ? name + "  ✓" : name, 15.5, FontWeights.SemiBold, Ui.Text, Ui.Display));
            names.Children.Add(Ui.T(Loc.T("Сохранена {0}", p.Saved.ToString("g", Loc.Culture)) + " · " + Loc.T("{0} папок, {1} виджетов", p.Tiles.Count, p.Widgets.Count(w => w.Enabled)), 12.5, FontWeights.Normal, Ui.Soft));
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(Ui.Button(Loc.T("Поставить"), () => App.Current.ApplyPreset(name), primary: true));
            actions.Children.Add(new Border { Width = 8 });
            actions.Children.Add(Ui.Button(Loc.T("Переименовать"), () => App.Current.RenamePreset(name)));
            actions.Children.Add(new Border { Width = 8 });
            actions.Children.Add(Ui.Button(Loc.T("Удалить"), () => App.Current.DeletePreset(name)));
            var row = new DockPanel();
            DockPanel.SetDock(actions, Dock.Right);
            row.Children.Add(actions);
            row.Children.Add(names);
            page.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(13), Background = Ui.Card, BorderBrush = active ? Ui.Accent : Ui.Line, BorderThickness = new Thickness(1),
                Padding = new Thickness(18, 14, 14, 14), Margin = new Thickness(0, 0, 0, 10), Child = row,
            });
        }
    }

    private static string Count(int n)
    {
        int n10 = n % 10, n100 = n % 100;
        string w = n10 == 1 && n100 != 11 ? Loc.T("{0} ярлык", n) : n10 is >= 2 and <= 4 && n100 is < 12 or > 14 ? Loc.T("{0} ярлыка", n) : Loc.T("{0} ярлыков", n);
        return n == 0 ? Loc.T("Пусто") : w;
    }

    private FrameworkElement LookPage()
    {
        var s = App.Current.Config.Defaults;
        var page = Header(Loc.T("Внешний вид"), Loc.T("Общее стекло для виджетов и новых папок. Виджеты меняются сразу, папки — кнопкой «Применить ко всем»."),
            Ui.Button(Loc.T("Применить ко всем папкам"), () => App.Current.ApplyDefaultsToAllTiles(), primary: true));

        var preview = new Border { CornerRadius = new CornerRadius(16), Background = Wall(5), Height = 300, ClipToBounds = true };
        void Repaint()
        {
            var clock = new ClockView(new WidgetInfo { Kind = "clock" });
            clock.Width = 250; clock.Height = 124;
            var glass = GlassOf(clock, Math.Min(s.Corner, 40));
            preview.Child = new Viewbox { Margin = new Thickness(40), Child = glass };
            App.Current.RestyleWidgets();
            App.Current.SaveConfigSoon();
        }

        var controls = new StackPanel { Margin = new Thickness(0, 0, 36, 0) };
        var glassToggle = new Toggle(s.Glass);
        glassToggle.Changed += on => { s.Glass = on; Repaint(); };
        var glassRow = new DockPanel { Margin = new Thickness(0, 0, 0, 22) };
        DockPanel.SetDock(glassToggle, Dock.Right);
        glassRow.Children.Add(glassToggle);
        glassRow.Children.Add(new StackPanel { Children = { Ui.T(Loc.T("Стеклянная подложка"), 13.5, FontWeights.SemiBold), Ui.T(Loc.T("Выключи — на столе останутся только значки и подписи"), 12, FontWeights.Normal, Ui.Faint) } });
        controls.Children.Add(glassRow);
        controls.Children.Add(SliderRow(Loc.T("Густота оттенка"), 0, 100, s.GlassOpacity, "%", v => { s.GlassOpacity = (int)v; Repaint(); }));
        controls.Children.Add(SliderRow(Loc.T("Размытие"), 0, TileStyle.MaxBlur, s.Blur, "", v => { s.Blur = (int)v; Repaint(); }));
        controls.Children.Add(SliderRow(Loc.T("Тень"), 0, 100, s.Shadow, "%", v => { s.Shadow = (int)v; Repaint(); }));
        controls.Children.Add(SliderRow(Loc.T("Светлая кромка"), 0, 100, s.Edge, "%", v => { s.Edge = (int)v; Repaint(); }));
        controls.Children.Add(SliderRow(Loc.T("Скругление углов"), 0, TileStyle.MaxCorner, s.Corner, "", v => { s.Corner = (int)v; Repaint(); }));
        controls.Children.Add(SliderRow(Loc.T("Размер значков в папках"), TileStyle.MinIcon, TileStyle.MaxIcon, s.IconSize, Loc.T(" пикс."), v => { s.IconSize = (int)v; App.Current.SaveConfigSoon(); }));

        var tints = new WrapPanel { Margin = new Thickness(0, 6, 0, 18) };
        foreach (var (hex, nm) in new[] { ("#FFFFFF", Loc.T("Белое")), ("#9CD4FF", Loc.T("Голубое")), ("#B9A6FF", Loc.T("Сиреневое")), ("#FFB3C7", Loc.T("Розовое")), ("#A8F0C8", Loc.T("Мятное")), ("#FFD59C", Loc.T("Тёплое")), ("#1A1A22", Loc.T("Тёмное")) })
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var dot = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 0, 10, 0), Cursor = Cursors.Hand, ToolTip = nm,
                BorderThickness = new Thickness(2), BorderBrush = s.Tint.Equals(hex, StringComparison.OrdinalIgnoreCase) ? Brushes.White : Brushes.Transparent,
                Child = new Border { CornerRadius = new CornerRadius(12), Margin = new Thickness(2), Background = new SolidColorBrush(c) },
            };
            dot.MouseLeftButtonUp += (_, _) =>
            {
                s.Tint = hex;
                foreach (Border d in tints.Children) d.BorderBrush = Brushes.Transparent;
                dot.BorderBrush = Brushes.White;
                Repaint();
            };
            tints.Children.Add(dot);
        }
        controls.Children.Add(Ui.T(Loc.T("Оттенок стекла"), 13.5, FontWeights.SemiBold));
        controls.Children.Add(tints);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        grid.Children.Add(controls);
        Grid.SetColumn(preview, 1);
        preview.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(preview);
        page.Children.Add(grid);
        Repaint();
        return page;
    }

    private static FrameworkElement SliderRow(string name, double min, double max, double value, string unit, Action<double> set)
    {
        var val = Ui.T($"{value:0}{unit}", 13, FontWeights.SemiBold, Ui.Soft);
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(val, Dock.Right);
        head.Children.Add(val);
        head.Children.Add(Ui.T(name, 13.5, FontWeights.SemiBold));
        var slider = new GlassSlider(min, max, value);
        slider.Changed += v => { val.Text = $"{v:0}{unit}"; set(v); };
        return new StackPanel { Margin = new Thickness(0, 0, 0, 18), Children = { head, slider } };
    }

    private FrameworkElement LoadPage()
    {
        var page = Header(Loc.T("Нагрузка"), Loc.T("Сколько «Плитки» берут у компьютера прямо сейчас. В простое — почти ноль: всё обновляется только по событию."));
        var cpu = Stat(Loc.T("Процессор"), "…", Loc.T("от всего компьютера"));
        var ram = Stat(Loc.T("Память"), "…", Loc.T("занято программой"));
        var wid = Stat(Loc.T("Виджеты"), App.Current.Config.Widgets.Count(w => w.Enabled).ToString(), Loc.T("на столе"));
        var fol = Stat(Loc.T("Папки"), App.Current.Tiles.Count.ToString(), Loc.T("плиток"));
        var stats = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(0, 0, -14, 22) };
        foreach (var b in new[] { cpu.Card, ram.Card, wid.Card, fol.Card }) stats.Children.Add(b);
        page.Children.Add(stats);

        var proc = Process.GetCurrentProcess();
        var last = (proc.TotalProcessorTime, DateTime.Now);
        void Sample()
        {
            proc.Refresh();
            var now = (proc.TotalProcessorTime, DateTime.Now);
            double pct = (now.TotalProcessorTime - last.TotalProcessorTime).TotalMilliseconds / Math.Max(1, (now.Now - last.Now).TotalMilliseconds) / Environment.ProcessorCount * 100;
            last = now;
            cpu.Value.Text = $"{pct:0.0}%";
            ram.Value.Text = Loc.T("{0} МБ", proc.WorkingSet64 / 1024 / 1024);
        }
        Sample();
        _loadTick = Sample;
        if (!_preview) _load.Start();

        page.Children.Add(OptionRow(Loc.T("Экономный режим"), Loc.T("Стекло не обновляется по таймеру, нагрузка ПК — раз в 5 секунд, погода — раз в час."),
            App.Current.Config.EcoMode, on => App.Current.SetEco(on), Glyphs.Leaf));
        page.Children.Add(OptionRow(Loc.T("Запускать вместе с Windows"), Loc.T("Плитки и виджеты появляются сразу после входа."),
            App.Current.Config.Autostart, on => App.Current.SetAutostart(on), Glyphs.Clock));
        page.Children.Add(LanguageRow());
        return page;
    }

    public string CurrentPage => _current;

    /// <summary>«Язык»: как в Windows или один из 10; смена сразу, без перезапуска.</summary>
    private static Border LanguageRow()
    {
        string set = App.Current.Config.Language;
        string Name(string code) => code == "" ? Loc.T("Как в Windows ({0})", Loc.Names[Loc.Resolve("", System.Globalization.CultureInfo.CurrentUICulture)]) : Loc.Names[code];
        Border? pick = null;
        pick = Ui.Button(Name(set) + "  ⌄", () =>
        {
            var menu = new ContextMenu { PlacementTarget = pick, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var code in Loc.Codes.Prepend(""))
            {
                var c = code;
                var item = new MenuItem { Header = Name(c), IsCheckable = true, IsChecked = c == set };
                item.Click += (_, _) => App.Current.SetLanguage(c);
                menu.Items.Add(item);
                if (c == "") menu.Items.Add(new Separator());
            }
            menu.IsOpen = true;
        });
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 16, 0) };
        names.Children.Add(Ui.T(Loc.T("Язык"), 15, FontWeights.SemiBold, Ui.Text, Ui.Display));
        names.Children.Add(Ui.T(Loc.T("Надписи в окне, меню и виджетах. Новые папки получают названия на этом языке."), 12.5, FontWeights.Normal, Ui.Soft));
        var icon = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(10), Background = Ui.AccentSoft,
            Child = new TextBlock { Text = "A文", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Ui.Text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(pick, Dock.Right);
        pick.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(icon);
        row.Children.Add(pick);
        row.Children.Add(names);
        return new Border
        {
            CornerRadius = new CornerRadius(13), Background = Ui.Card, BorderBrush = Ui.Line, BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 14, 18, 14), Margin = new Thickness(0, 0, 0, 10), Child = row,
        };
    }

    private static (Border Card, TextBlock Value) Stat(string name, string value, string hint)
    {
        var v = Ui.T(value, 30, FontWeights.SemiBold, Ui.Text, Ui.Display);
        var card = new Border
        {
            CornerRadius = new CornerRadius(14), Background = Ui.Card, BorderBrush = Ui.Line, BorderThickness = new Thickness(1),
            Padding = new Thickness(18, 16, 18, 16), Margin = new Thickness(0, 0, 14, 0),
            Child = new StackPanel { Children = { Ui.T(name, 13, FontWeights.SemiBold, Ui.Soft), v, Ui.T(hint, 12, FontWeights.Normal, Ui.Faint) } },
        };
        return (card, v);
    }

    private static Border OptionRow(string title, string text, bool on, Action<bool> set, string glyph)
    {
        var toggle = new Toggle(on);
        toggle.Changed += set;
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 16, 0) };
        names.Children.Add(Ui.T(title, 15, FontWeights.SemiBold, Ui.Text, Ui.Display));
        names.Children.Add(Ui.T(text, 12.5, FontWeights.Normal, Ui.Soft));
        var icon = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(10), Background = Ui.AccentSoft, Child = Glyphs.Make(glyph, 20, 1.7) };
        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(toggle, Dock.Right);
        row.Children.Add(icon);
        row.Children.Add(toggle);
        row.Children.Add(names);
        return new Border
        {
            CornerRadius = new CornerRadius(13), Background = Ui.Card, BorderBrush = Ui.Line, BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 14, 18, 14), Margin = new Thickness(0, 0, 0, 10), Child = row,
        };
    }
}
