using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Tiles.Core;

namespace Tiles.App;

/// <summary>Что стоит на столе и подчиняется общей раскладке: папки и виджеты.</summary>
internal interface IDeskItem
{
    string Id { get; }
    Window Window { get; }
    /// <summary>Место и размер в точках экрана (из настроек).</summary>
    RectD Rect { get; }
    bool Snap { get; }
    /// <summary>Закреплён: не двигается и не растягивается, соседи его не сдвигают.</summary>
    bool Locked { get; }
    /// <summary>Без сетки — прилипать к краям соседей.</summary>
    bool Align { get; }
    bool Selected { get; set; }
    /// <summary>Поставить на место (уже выровненное раскладкой) и запомнить.</summary>
    void MoveTo(double x, double y);
    /// <summary>Только запомнить место (окно туда ещё доплывёт).</summary>
    void Remember(double x, double y);
    /// <summary>Кадр анимации: окно в прямоугольник r, ничего не запоминая.</summary>
    void Preview(RectD r);
    /// <summary>Кадр переноса: стекло показывает то, что под ним сейчас.</summary>
    void FollowGlass();
}

/// <summary>
/// Окно виджета: стекло, содержимое в масштабе, перетаскивание (в том числе группой),
/// растягивание за края с сохранением пропорций, меню по правой кнопке.
/// </summary>
internal sealed class WidgetWindow : GlassWidget, IDeskItem
{
    public const double Pad = 6, LabelH = 24, Grip = 9;

    private readonly Viewbox _box;
    private readonly Grid _inner;
    internal Grid Inner => _inner;
    private readonly TextBlock? _label;
    private Point _downScreen;
    private bool _pressed, _moved, _ctrl;
    private Edge _edge;
    private RectD _startRect;
    private (double Dx, double Dy) _lastDelta;

    public WidgetView View { get; }
    public WidgetInfo Info => View.Info;
    public string Id => "w:" + Info.Kind;
    Window IDeskItem.Window => this;
    public bool Snap => Info.Snap;
    public bool Locked => Info.Locked;
    public bool Align => Info.Align;
    protected override TileStyle Look => Info.Style ?? App.Current.Config.Defaults;

    /// <summary>Размер окна при масштабе 1: стекло, отступ под тень, подпись.</summary>
    public Size WindowBase
    {
        get
        {
            var b = View.BaseSize;
            return new Size(b.Width + 2 * Pad, b.Height + 2 * Pad + (View.Label != null ? LabelH : 0));
        }
    }

    /// <summary>Окно: место и размер из настроек; размер не задан — базовый (с масштабом из прошлой версии).</summary>
    public RectD Rect
    {
        get
        {
            var b = WindowBase;
            double w = double.IsNaN(Info.W) ? b.Width * Info.Scale : Info.W;
            double h = double.IsNaN(Info.H) ? b.Height * Info.Scale : Info.H;
            return new RectD(Info.X, Info.Y, w, h);
        }
    }

    /// <summary>Меньше не сжать: самая узкая и самая низкая из форм, с запасом масштаба.</summary>
    private double MinW => View.MinSize.Width * WidgetSizing.MinScale + 2 * Pad;
    private double MinH => View.MinSize.Height * WidgetSizing.MinScale + 2 * Pad + (_label != null ? LabelH : 0);

    /// <summary>Во сколько раз увеличено содержимое: вписываем базовый размер в окно.</summary>
    private double ContentScale
    {
        get { var r = Rect; var b = WindowBase; return WidgetSizing.ContentScale(b.Width, b.Height, r.W, r.H); }
    }

    public WidgetWindow(WidgetView view) : base(Loc.T("Плитки: {0}", view.Title))
    {
        View = view;
        var b = view.BaseSize;
        view.Width = b.Width; view.Height = b.Height;
        // стекло заполняет всё окно, содержимое вписывается в него целиком (вектором — чётко в любом размере)
        _box = new Viewbox { Stretch = Stretch.Uniform, Child = view };
        Frame.Child = _box;
        Frame.ClipToBounds = true;

        _inner = new Grid { Margin = new Thickness(Pad) };
        _inner.RowDefinitions.Add(new RowDefinition());
        _inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _inner.Children.Add(Glass);
        if (view.Label is { } label)
        {
            _label = new TextBlock
            {
                Text = label, Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold,
                FontFamily = WidgetView.Display, Height = LabelH, Padding = new Thickness(0, 3, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center, Effect = TextShadow,
            };
            Grid.SetRow(_label, 1);
            _inner.Children.Add(_label);
        }
        Content = _inner;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

        view.WantVisible += v => { if (v) ShowWidget(); else HideWidget(); };
        view.LayoutChanged += () => { if (IsLoaded) ApplySize(); };
        if (view is BinView bin)
        {
            // бросили файлы на корзину — в корзину, как в Windows
            AllowDrop = true;
            DragEventHandler over = (_, e) =>
            {
                bool files = e.Data.GetDataPresent(DataFormats.FileDrop);
                e.Effects = files ? DragDropEffects.Move : DragDropEffects.None;
                if (files) Frame.BorderBrush = SelectEdge;
                e.Handled = true;
            };
            DragEnter += over; DragOver += over;
            DragLeave += (_, _) => Paint(false);
            Drop += (_, e) =>
            {
                Paint(false);
                e.Handled = true;
                if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) bin.DropFiles(paths);
            };
        }

        MouseEnter += (_, _) => Paint(true);
        MouseLeave += (_, _) => { Paint(false); if (!_pressed) Cursor = null; };
        PreviewMouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        ContextMenuOpening += (_, _) => ContextMenu = BuildMenu();
        ContextMenu = BuildMenu();
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        foreach (var item in View.MenuItems()) menu.Items.Add(item);
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        menu.Items.Add(Item(Loc.T("Настроить вид…"), () => App.Current.OpenWidgetStyle(this)));
        menu.Items.Add(new Separator());
        foreach (var a in App.Current.AlignMenu(this)) menu.Items.Add(a);
        menu.Items.Add(Item((Info.Locked ? "✓ " : "") + Loc.T("Закрепить на месте"), () => { Info.Locked = !Info.Locked; App.Current.SaveConfig(); }));
        menu.Items.Add(Item((Info.Snap ? "✓ " : "") + Loc.T("Привязка к сетке"), () => { Info.Snap = !Info.Snap; App.Current.SettleWidget(this); }));
        menu.Items.Add(Item((Info.Align ? "✓ " : "") + Loc.T("Выравнивать по соседям (без сетки)"), () => { Info.Align = !Info.Align; App.Current.SaveConfig(); }));
        menu.Items.Add(Item(Loc.T("Обычный размер"), () => { Info.Scale = 1; Info.W = Info.H = double.NaN; ApplySize(); App.Current.SettleWidget(this); }));
        foreach (var g in App.Current.GridMenu()) menu.Items.Add(g);
        menu.Items.Add(Item(Loc.T("Убрать с рабочего стола"), () => App.Current.SetWidget(Info.Kind, false)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Loc.T("Открыть «Плитки»…"), () => App.Current.OpenHub("workshop")));
        return menu;
    }

    internal static MenuItem Item(string text, Action act)
    {
        var mi = new MenuItem { Header = text };
        mi.Click += (_, _) => act();
        return mi;
    }

    public void ApplySize() => SizeContent(Rect);

    /// <summary>
    /// Окно в размер r: содержимое масштабируется по меньшей стороне и раскладывается на всё стекло
    /// (длинный виджет растягивает полоску и строки, а не висит островком посередине).
    /// </summary>
    private void SizeContent(RectD r)
    {
        Width = r.W;
        Height = r.H;
        LayoutFor(r);
    }

    /// <summary>Содержимое под размер r (окно не трогаем).</summary>
    private void LayoutFor(RectD r)
    {
        // форма по стеклу: в ряд, квадратом или столбиком; масштаб — от базового размера этой формы
        double label0 = _label != null ? LabelH : 0;
        View.SetArea(r.W - 2 * Pad, r.H - 2 * Pad - label0);
        View.SetShape(Shapes.Of(r.W - 2 * Pad, r.H - 2 * Pad - label0, View.Shape));
        var bs = View.BaseFor(View.Shape);
        double k = WidgetSizing.ContentScale(bs.Width + 2 * Pad, bs.Height + 2 * Pad + label0, r.W, r.H);
        double label = 0;
        if (_label != null)
        {
            _label.FontSize = 13 * k;
            _label.Height = label = LabelH * k;
        }
        View.Width = Math.Max(1, (r.W - 2 * Pad - 2) / k);
        View.Height = Math.Max(1, (r.H - 2 * Pad - 2 - label) / k);
    }

    /// <summary>Кадр полёта: окно в r без запоминания.</summary>
    public void Preview(RectD r)
    {
        var cur = Native.WindowRect(this);
        double k = App.Current.DpiScale;
        if (_canvas == null && Math.Abs((cur.Right - cur.Left) - r.W * k) < 1 && Math.Abs((cur.Bottom - cur.Top) - r.H * k) < 1)
        {
            Native.MoveTo(this, (int)Math.Round(r.X * k), (int)Math.Round(r.Y * k));
            FollowGlass();
        }
        else { if (_canvas == null) BeginCanvas(Shown); Bounds(r); }
    }

    /// <summary>Где виджет виден прямо сейчас (на сцене или в своём окне).</summary>
    private RectD Shown
    {
        get
        {
            if (ShownRect is { } s) return s;
            var w = Native.WindowRect(this);
            double k = App.Current.DpiScale;
            return new RectD(w.Left / k, w.Top / k, (w.Right - w.Left) / k, (w.Bottom - w.Top) / k);
        }
    }

    /// <summary>Место и размер: на сцене — двигаем содержимое; иначе окно одним вызовом.</summary>
    private void Bounds(RectD r)
    {
        if (_canvas is { } c) { PlaceInner(r, c); return; }
        LayoutFor(r);
        Native.SetBounds(this, r, App.Current.DpiScale);
        Width = r.W; Height = r.H; Left = r.X; Top = r.Y; // сверяем WPF с окном — повторного сдвига не будет
    }

    // ---- «сцена» на время растягивания и доводки ----
    // Прозрачное окно Windows меняет место и размер на кадр раньше, чем WPF перерисует содержимое:
    // край дрожал, а при перестройке окна виджет мигал в углу экрана. Пока размер меняется,
    // содержимое живёт на общей сцене (окно на все экраны, которое не двигается).
    private RectD? _canvas;
    /// <summary>Где виджет виден на сцене; null — он в своём окне.</summary>
    public RectD? ShownRect { get; private set; }

    /// <summary>Переезд на сцену: в том же кадре содержимое пропадает из окна и появляется на сцене на том же месте.</summary>
    private void BeginCanvas(RectD r)
    {
        var (host, origin) = Stage.Ensure();
        _canvas = origin;
        // окно и сцена показывают кадр не строго вместе: пустое окно на кадр было видно миганием
        // (у тёмного пульта особенно). Пару кадров в окне держим снимок, пока сцена не покажет живое.
        var snap = Still();
        Content = snap;
        if (snap != null) AfterFrames(3, () => { if (Content == snap) Content = null; });
        _inner.Margin = new Thickness(0);
        _inner.HorizontalAlignment = HorizontalAlignment.Left;
        _inner.VerticalAlignment = VerticalAlignment.Top;
        host.Children.Add(_inner);
        PlaceInner(r, origin);
    }

    /// <summary>Неподвижная картинка содержимого как оно видно сейчас (для бесшовного переезда окно ↔ сцена).</summary>
    private System.Windows.Controls.Image? Still()
    {
        try
        {
            double w = _inner.ActualWidth, h = _inner.ActualHeight, k = App.Current.DpiScale;
            if (w < 1 || h < 1) return null;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
                dc.DrawRectangle(new VisualBrush(_inner) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
                    ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, w, h) }, null, new Rect(0, 0, w, h));
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(w * k), (int)Math.Ceiling(h * k), 96 * k, 96 * k, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return new System.Windows.Controls.Image
            {
                Source = bmp, Width = w, Height = h, Stretch = Stretch.Fill, IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Pad),
            };
        }
        catch (Exception ex) { App.Current.Log("snap failed: " + ex.Message); return null; }
    }

    private static void AfterFrames(int n, Action act)
    {
        EventHandler? h = null;
        h = (_, _) => { if (--n > 0) return; CompositionTarget.Rendering -= h; act(); };
        CompositionTarget.Rendering += h;
    }

    private void PlaceInner(RectD r, RectD c)
    {
        Canvas.SetLeft(_inner, r.X - c.X + Pad);
        Canvas.SetTop(_inner, r.Y - c.Y + Pad);
        _inner.Width = Math.Max(1, r.W - 2 * Pad);
        _inner.Height = Math.Max(1, r.H - 2 * Pad);
        ShownRect = r;
        LayoutFor(r);
        FollowGlass();
    }

    protected override Int32Rect? GlassOnScreen()
    {
        if (ShownRect is not { } s) return base.GlassOnScreen();
        double k = App.Current.DpiScale, label = _label?.Height ?? 0;
        return new Int32Rect((int)Math.Round((s.X + Pad) * k), (int)Math.Round((s.Y + Pad) * k),
            Math.Max(1, (int)Math.Round((s.W - 2 * Pad) * k)), Math.Max(1, (int)Math.Round((s.H - 2 * Pad - label) * k)));
    }

    /// <summary>Замер плавности: один кадр растягивания выбранным способом.</summary>
    internal void BenchStep(RectD r, string mode)
    {
        Info.X = r.X; Info.Y = r.Y; Info.W = r.W; Info.H = r.H;
        if (mode == "wpf") { SizeContent(r); Left = r.X; Top = r.Y; return; }
        if (mode == "canvas" && _canvas == null) BeginCanvas(r);
        Bounds(r);
    }

    /// <summary>Размер устоялся: пустое окно встаёт по виджету (незаметно), содержимое возвращается в него в том же кадре.</summary>
    private void EndCanvas(RectD r)
    {
        if (_canvas == null) return;
        _canvas = null;
        ShownRect = null;
        Native.SetBounds(this, r, App.Current.DpiScale);
        Width = r.W; Height = r.H; Left = r.X; Top = r.Y;
        // обратный переезд: на сцене пару кадров остаётся снимок, пока окно не покажет живое
        if (_inner.Parent is Canvas host && Still() is { } snap)
        {
            Canvas.SetLeft(snap, Canvas.GetLeft(_inner));
            Canvas.SetTop(snap, Canvas.GetTop(_inner));
            snap.Margin = new Thickness(0);
            host.Children.Add(snap);
            AfterFrames(3, () => host.Children.Remove(snap));
        }
        (_inner.Parent as Panel)?.Children.Remove(_inner);
        _inner.ClearValue(Canvas.LeftProperty);
        _inner.ClearValue(Canvas.TopProperty);
        _inner.HorizontalAlignment = HorizontalAlignment.Stretch;
        _inner.VerticalAlignment = VerticalAlignment.Stretch;
        _inner.Width = _inner.Height = double.NaN;
        _inner.Margin = new Thickness(Pad);
        Content = _inner;
        LayoutFor(r);
        UpdateLayout();
    }

    // живое растягивание: мышь может присылать сотни событий в секунду, а рисуем раз в кадр
    private RectD? _live;
    private bool _liveHooked;

    private void Live(RectD r)
    {
        _live = r;
        if (_liveHooked) return;
        _liveHooked = true;
        CompositionTarget.Rendering += LiveFrame;
    }

    private void LiveFrame(object? sender, EventArgs e)
    {
        if (_live is { } r) { _live = null; Bounds(r); }
        else if (!_pressed) { CompositionTarget.Rendering -= LiveFrame; _liveHooked = false; }
    }

    public void SetSize(double w, double h) { Info.W = w; Info.H = h; }

    public void Remember(double x, double y) { Info.X = x; Info.Y = y; }

    public void MoveTo(double x, double y)
    {
        Info.X = x; Info.Y = y;
        if (_canvas != null) EndCanvas(Rect); else { ApplySize();
        Left = x; Top = y; }
        UpdateGlass();
    }

    private bool _visibleWanted = true;

    public void ShowWidget()
    {
        _visibleWanted = true;
        if (IsVisible && Opacity >= 1) return;
        Show();
        ApplyLook();
        BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(220)));
    }

    public void HideWidget()
    {
        _visibleWanted = false;
        if (!IsVisible) return;
        var fade = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(380));
        fade.Completed += (_, _) => { if (!_visibleWanted) Hide(); };
        BeginAnimation(OpacityProperty, fade);
    }

    // ---- мышь: клик, перетаскивание (группой), растягивание ----

    private Edge EdgeAt(Point p)
    {
        // край стекла, а не всего окна (в отступе под тень мышь не ловится)
        var e = Edge.None;
        double l = Pad, t = Pad, r = ActualWidth - Pad, bt = ActualHeight - Pad - (_label?.ActualHeight ?? 0);
        if (p.Y > bt + 3) return Edge.None;
        if (p.X <= l + Grip) e |= Edge.Left;
        if (p.X >= r - Grip) e |= Edge.Right;
        if (p.Y <= t + Grip) e |= Edge.Top;
        if (p.Y >= bt - Grip) e |= Edge.Bottom;
        return e;
    }

    private static Cursor? CursorFor(Edge e) => e switch
    {
        Edge.Left or Edge.Right => Cursors.SizeWE,
        Edge.Top or Edge.Bottom => Cursors.SizeNS,
        Edge.Left | Edge.Top or Edge.Right | Edge.Bottom => Cursors.SizeNWSE,
        Edge.Right | Edge.Top or Edge.Left | Edge.Bottom => Cursors.SizeNESW,
        _ => null,
    };

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        _edge = Info.Locked ? Edge.None : EdgeAt(p); // закреплён — не растягиваем
        if (_edge == Edge.None && View.OwnsMouse(e.OriginalSource as DependencyObject)) return; // кнопка, поле ввода
        _pressed = true; _moved = false;
        _ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        _downScreen = PointToScreen(p);
        _startRect = Rect;
        if (_edge == Edge.None) App.Current.BeginDrag(this, _ctrl);
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_pressed)
        {
            Cursor = Info.Locked ? null : CursorFor(EdgeAt(e.GetPosition(this)));
            return;
        }
        var now = PointToScreen(e.GetPosition(this));
        double dx = now.X - _downScreen.X, dy = now.Y - _downScreen.Y;
        if (!_moved && Math.Abs(dx) < 5 && Math.Abs(dy) < 5) return;
        _moved = true;
        if (_edge != Edge.None)
        {
            double k = App.Current.DpiScale;
            var b = WindowBase;
            // тянется плавно за мышью; к клеткам доводим уже после отпускания (тоже плавно)
            var r = WidgetSizing.Resize(_startRect, dx / k, dy / k, _edge, false, App.Current.CellAt(_startRect).W, App.Current.CellAt(_startRect).H,
                MinW, MinH);
            _lastDelta = (dx / k, dy / k);
            if (!Info.Snap && Info.Align)
            {
                // без сетки: тянущийся край прилипает к краям соседей, видна красная линия
                var (g, lines) = Guides.Resize(r, _edge, App.Current.DeskItems().Where(i => i != this).Select(i => i.Rect).ToList(), 8);
                r = g;
                GuideOverlay.Show(lines);
            }
            Info.X = r.X; Info.Y = r.Y; Info.W = r.W; Info.H = r.H;
            if (_canvas == null) BeginCanvas(_startRect); // окно с запасом — дальше меняется только картинка внутри
            Live(r);
        }
        else App.Current.DragGroup(dx, dy);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed) return;
        _pressed = false;
        ReleaseMouseCapture();
        if (!_moved)
        {
            App.Current.CancelDrag();
            if (_ctrl) App.Current.ToggleSelect(this);
            else { App.Current.ClearSelection(); View.Click(); }
            return;
        }
        GuideOverlay.Hide();
        if (_edge != Edge.None)
        {
            if (_live is { } last) { _live = null; Bounds(last); } // последний кадр за мышью — от него и доплываем
            if (Info.Snap)
            {
                var b = WindowBase;
                var r = WidgetSizing.Resize(_startRect, _lastDelta.Dx, _lastDelta.Dy, _edge, true, App.Current.CellAt(_startRect).W, App.Current.CellAt(_startRect).H,
                    MinW, MinH);
                Info.W = r.W; Info.H = r.H; Info.X = r.X; Info.Y = r.Y;
            }
            App.Current.SettleWidget(this);
        }
        else App.Current.EndDrag();
    }
}
