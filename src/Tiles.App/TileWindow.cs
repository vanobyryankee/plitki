using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Tiles.Core;

namespace Tiles.App;

/// <summary>
/// Стеклянная плитка-папка на рабочем столе: первые ярлыки папки в натуральную величину,
/// название под стеклом, края тянутся с шагом в один ярлык. Вид — из настроек этой папки.
/// </summary>
internal sealed class TileWindow : Window, IDeskItem
{
    public string Id => "tile:" + Folder;
    Window IDeskItem.Window => this;
    public RectD Rect
    {
        get
        {
            return App.Current.TileRect(App.Current.Info(this));
        }
    }
    public bool Snap => Style.SnapToGrid;
    public bool Locked => App.Current.Info(this).Locked;
    public bool Align => App.Current.Info(this).Align;
    private bool _selected;
    public bool Selected
    {
        get => _selected;
        set { _selected = value; PaintFrame(false); }
    }
    public void MoveTo(double x, double y) => App.Current.PlaceTile(this, x, y);
    public void Remember(double x, double y) { var t = App.Current.Info(this); t.X = x; t.Y = y; }
    public void Preview(RectD r)
    {
        var cur = Native.WindowRect(this);
        double k = App.Current.DpiScale;
        if (Math.Abs((cur.Right - cur.Left) - r.W * k) < 1 && Math.Abs((cur.Bottom - cur.Top) - r.H * k) < 1)
            Native.MoveTo(this, (int)Math.Round(r.X * k), (int)Math.Round(r.Y * k));
        else { Native.SetBounds(this, r, k); Left = r.X; Top = r.Y; Width = r.W; Height = r.H; } // разом, без дрожи края
        FollowGlass();
    }

    private void PaintFrame(bool hover)
    {
        _frame.Background = _selected ? GlassWidget.SelectFill : hover ? _hover : _idle;
        _frame.BorderBrush = _selected ? GlassWidget.SelectEdge : _edge;
    }
    private bool _ctrl;

    public const double LabelH = 24, Pad = 6;
    private const double Grip = 10;
    private static readonly Brush EdgeDrop = new SolidColorBrush(Color.FromArgb(0xEE, 0x6C, 0xB4, 0xFF));
    private static readonly Brush NoGlass = new SolidColorBrush(Color.FromArgb(0x99, 0x30, 0x30, 0x30));
    private static readonly Effect TextShadow = new DropShadowEffect { BlurRadius = 5, ShadowDepth = 1, Opacity = 0.9 };

    private readonly Border _glass;
    private readonly Border _frame;
    private readonly UniformGrid _grid;
    private readonly Border _badge;
    private readonly TextBlock _badgeText;
    private readonly TextBlock _label;
    private readonly TextBlock _empty;
    private Brush _idle = Brushes.Transparent, _hover = Brushes.Transparent, _edge = Brushes.Transparent;

    private Point _downScreen;
    private Native.RECT _downRect;
    private bool _pressed, _moved;
    private Tiles.Core.Edge _resizeEdge;
    private (int Cols, int Rows, double X, double Y) _startSize;

    public string Folder { get; set; }
    private TileStyle Style => App.Current.Info(this).Style;

    public TileWindow(string folder)
    {
        Folder = folder;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Background = Brushes.Transparent;
        AllowDrop = true;
        Title = Loc.T("Плитка {0}", folder);

        _grid = new UniformGrid { Margin = new Thickness(2, 4, 2, 2) };
        _badgeText = new TextBlock { Foreground = Brushes.White, FontSize = 11.5, FontWeight = FontWeights.SemiBold };
        _badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x70, 0x10, 0x10, 0x14)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(7, 1, 7, 2), Margin = new Thickness(0, 0, 7, 7),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Child = _badgeText, Visibility = Visibility.Collapsed,
        };
        _empty = new TextBlock
        {
            Text = Loc.T("Перетащи сюда ярлыки"), Foreground = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)),
            FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10), Effect = TextShadow,
        };
        _frame = new Border { BorderThickness = new Thickness(1), Child = new Grid { Children = { _grid, _badge, _empty } } };
        _glass = new Border { Background = NoGlass, Child = _frame };

        _label = new TextBlock
        {
            Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, Effect = TextShadow,
        };

        var root = new Grid { Margin = new Thickness(Pad) };
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(LabelH) });
        root.Children.Add(_glass);
        Grid.SetRow(_label, 1);
        root.Children.Add(_label);
        Content = root;

        var menu = new ContextMenu();
        menu.Items.Add(Item(Loc.T("Настройки папки…"), () => App.Current.OpenSettings(this)));
        menu.Items.Add(Item(Loc.T("Переименовать"), () => App.Current.RenameFolder(this)));
        var autoName = Item("", () => App.Current.ToggleAutoName(this));
        menu.Items.Add(autoName);
        var lockItem = Item("", () => { var i = App.Current.Info(this); i.Locked = !i.Locked; App.Current.SaveConfig(); });
        var snapItem = Item("", () => { Style.SnapToGrid = !Style.SnapToGrid; App.Current.MoveTo(this); });
        var alignItem = Item("", () => { var i = App.Current.Info(this); i.Align = !i.Align; App.Current.SaveConfig(); });
        menu.Items.Add(new Separator());
        menu.Items.Add(lockItem);
        menu.Items.Add(snapItem);
        menu.Items.Add(alignItem);
        var gridItem = Item("", () => App.Current.ToggleGrid());
        menu.Items.Add(gridItem);
        menu.Items.Add(Item(Loc.T("Редактировать сетку…"), () => App.Current.EditGrid()));
        menu.Items.Add(Item(Loc.T("Удалить папку (ярлыки вернутся на стол)"), () => App.Current.DeleteFolder(this)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Loc.T("Новая папка"), () => App.Current.NewFolder()));
        menu.Items.Add(Item(Loc.T("Общие настройки…"), () => App.Current.OpenDefaults()));
        ContextMenu = menu;
        ContextMenuOpening += (_, e) =>
        {
            // правая кнопка по значку — меню проводника, по пустому месту — меню плиток
            if (Style.ShellMenu && IconAt(Mouse.GetPosition(this)) is { } path)
            {
                e.Handled = true;
                ShellMenu.Show(this, path);
                return;
            }
            var info = App.Current.Info(this);
            autoName.Header = (info.AutoName ? "✓ " : "") + Loc.T("Называть по содержимому");
            lockItem.Header = (info.Locked ? "✓ " : "") + Loc.T("Закрепить на месте");
            snapItem.Header = (Style.SnapToGrid ? "✓ " : "") + Loc.T("Привязка к сетке");
            alignItem.Header = (info.Align ? "✓ " : "") + Loc.T("Выравнивать по соседям (без сетки)");
            gridItem.Header = (App.Current.GridShown ? "✓ " : "") + Loc.T("Показать сетку");
            // «Выровнять выделенные по этой» — сверху меню, пересобираем при каждом открытии
            foreach (var old in menu.Items.OfType<FrameworkElement>().Where(x => Equals(x.Tag, "align")).ToList()) menu.Items.Remove(old);
            int at = 0;
            foreach (var a in App.Current.AlignMenu(this))
                if (a is FrameworkElement fe) { fe.Tag = "align"; menu.Items.Insert(at++, fe); }
        };

        SourceInitialized += (_, _) => Native.PinToDesktop(this);
        MouseLeave += (_, _) => { PaintFrame(false); if (!_pressed) Cursor = null; };
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += (_, _) => PaintFrame(false);
        Drop += OnDrop;
    }

    private static MenuItem Item(string text, Action act)
    {
        var mi = new MenuItem { Header = text };
        mi.Click += (_, _) => act();
        return mi;
    }

    private static Color Rgb(uint argb, byte alpha) =>
        Color.FromArgb(alpha, (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    /// <summary>Применить настройки вида этой папки.</summary>
    public void ApplyStyle()
    {
        var s = Style;
        uint tint = s.TintArgb();
        byte a = (byte)(tint >> 24);
        // без стекла: почти прозрачная заливка (совсем прозрачное окно не ловит мышь), подсветка — при наведении
        _idle = s.Glass ? new SolidColorBrush(Rgb(tint, a)) : HitOnly;
        _hover = s.Glass ? new SolidColorBrush(Rgb(tint, (byte)Math.Min(255, a + 26))) : BareHover;
        _edge = s.Glass ? new SolidColorBrush(Color.FromArgb((byte)(s.Edge * 255 / 100), 0xFF, 0xFF, 0xFF)) : Brushes.Transparent;
        PaintFrame(false);
        _frame.CornerRadius = _glass.CornerRadius = new CornerRadius(s.Corner);
        if (!s.Glass) _glass.Background = HitOnly;
        _glass.Effect = s.Glass && s.Shadow > 0
            ? new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Direction = 270, Opacity = s.Shadow / 100.0 }
            : null;
        _label.Visibility = s.ShowName ? Visibility.Visible : Visibility.Hidden;
        Refresh();
    }

    public void Refresh()
    {
        Diag.Count("refresh");
        var info = App.Current.Info(this);
        var s = info.Style;
        _label.Text = Folder;
        ToolTip = Folder;
        var items = App.Current.Store.Items(Folder);
        var (shown, more) = Layout.Preview(items.Count, info.Cols, info.Rows);
        _grid.Columns = info.Cols;
        _grid.Rows = info.Rows;
        _grid.Children.Clear();
        foreach (var path in items.Take(shown))
            _grid.Children.Add(MakeIcon(path, s));
        _badge.Visibility = more > 0 ? Visibility.Visible : Visibility.Collapsed;
        _badgeText.Text = "+" + more;
        _empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static readonly Brush IconHover = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));

    /// <summary>
    /// Ярлык как на рабочем столе; подпись — если включена. Обёртка помнит путь (Tag): по ней
    /// находим, по какому значку кликнули или нажали правой кнопкой.
    /// </summary>
    private static FrameworkElement MakeIcon(string path, TileStyle s)
    {
        var img = Icons.Image(path, s.IconSize);
        FrameworkElement content = img;
        if (s.TileCaptions)
        {
            img.Margin = new Thickness(0, 0, 0, 3);
            content = new StackPanel
            {
                Children =
                {
                    img,
                    new TextBlock
                    {
                        Text = Icons.DisplayName(path), Foreground = Brushes.White, FontSize = 12,
                        TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                        TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 33, Margin = new Thickness(4, 0, 4, 0),
                        Effect = TextShadow,
                    },
                },
            };
        }
        var cell = new Border
        {
            Tag = path, Child = content, CornerRadius = new CornerRadius(10), Padding = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent, ToolTip = Icons.DisplayName(path),
        };
        if (s.ClickLaunch)
        {
            // значок — кнопка: подсвечиваем, как ярлык на рабочем столе
            cell.MouseEnter += (_, _) => cell.Background = IconHover;
            cell.MouseLeave += (_, _) => cell.Background = Brushes.Transparent;
        }
        return cell;
    }

    /// <summary>Путь ярлыка под точкой или null, если там пустое стекло.</summary>
    private string? IconAt(Point p)
    {
        var d = InputHitTest(p) as DependencyObject;
        while (d != null && d != _grid)
        {
            if (d is FrameworkElement { Tag: string path }) return path;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    /// <summary>Перерисовать стекло под текущее место и обои (только само стекло).</summary>
    private static readonly Brush HitOnly = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
    private static readonly Brush BareHover = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));

    public void UpdateGlass() => ShowGlass(false);

    /// <summary>Кадр переноса: стекло как линза показывает то, что под ним сейчас.</summary>
    public void FollowGlass() => ShowGlass(true);

    private void ShowGlass(bool live)
    {
        if (!Style.Glass) { _glass.Background = HitOnly; return; }
        double s = App.Current.DpiScale;
        // место окна берём у Windows: при переносе его двигают в обход WPF
        var wr = Native.WindowRect(this);
        double w = (wr.Right - wr.Left) / s, h = (wr.Bottom - wr.Top) / s;
        var rect = new Int32Rect(wr.Left + (int)Math.Round(Pad * s), wr.Top + (int)Math.Round(Pad * s),
            Math.Max(1, (int)Math.Round((w - 2 * Pad) * s)), Math.Max(1, (int)Math.Round((h - 2 * Pad - LabelH) * s)));
        var brush = Wallpaper.Lens(rect, Style.Blur, _glass.Background as ImageBrush, live);
        if (!ReferenceEquals(brush, _glass.Background)) _glass.Background = (Brush?)brush ?? NoGlass;
    }

    // ---- край для растягивания ----

    private Tiles.Core.Edge EdgeAt(Point p)
    {
        double left = Pad, top = Pad, right = Width - Pad, bottom = Height - Pad - LabelH;
        if (p.Y > bottom + 3) return Tiles.Core.Edge.None; // название — не край
        var e = Tiles.Core.Edge.None;
        if (p.X <= left + Grip) e |= Tiles.Core.Edge.Left;
        if (p.X >= right - Grip) e |= Tiles.Core.Edge.Right;
        if (p.Y <= top + Grip) e |= Tiles.Core.Edge.Top;
        if (p.Y >= bottom - Grip) e |= Tiles.Core.Edge.Bottom;
        return e;
    }

    private static Cursor? CursorFor(Tiles.Core.Edge e) => e switch
    {
        Tiles.Core.Edge.Left or Tiles.Core.Edge.Right => Cursors.SizeWE,
        Tiles.Core.Edge.Top or Tiles.Core.Edge.Bottom => Cursors.SizeNS,
        Tiles.Core.Edge.Left | Tiles.Core.Edge.Top or Tiles.Core.Edge.Right | Tiles.Core.Edge.Bottom => Cursors.SizeNWSE,
        Tiles.Core.Edge.Right | Tiles.Core.Edge.Top or Tiles.Core.Edge.Left | Tiles.Core.Edge.Bottom => Cursors.SizeNESW,
        _ => null,
    };

    // ---- клик, перетаскивание и растягивание ----

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _pressed = true;
        _moved = false;
        _resizeEdge = Locked ? Tiles.Core.Edge.None : EdgeAt(e.GetPosition(this)); // закреплена — не растягиваем
        var info = App.Current.Info(this);
        _startSize = (info.Cols, info.Rows, info.X, info.Y);
        _downScreen = PointToScreen(e.GetPosition(this));
        _downRect = Native.WindowRect(this);
        _ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (_resizeEdge == Tiles.Core.Edge.None) App.Current.BeginDrag(this, _ctrl);
        CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_pressed)
        {
            var edge = Locked ? Tiles.Core.Edge.None : EdgeAt(e.GetPosition(this));
            Cursor = CursorFor(edge);
            PaintFrame(edge == Tiles.Core.Edge.None);
            return;
        }
        var now = PointToScreen(e.GetPosition(this));
        double dxPx = now.X - _downScreen.X, dyPx = now.Y - _downScreen.Y;
        if (!_moved && Math.Abs(dxPx) < 6 && Math.Abs(dyPx) < 6) return;
        _moved = true;

        if (_resizeEdge != Tiles.Core.Edge.None)
        {
            double scale = App.Current.DpiScale;
            var (cw, ch) = App.Current.CellAt(new RectD(_startSize.X, _startSize.Y, 1, 1));
            var r = Layout.Resize(_startSize.Cols, _startSize.Rows, dxPx / scale, dyPx / scale, cw, ch, _resizeEdge);
            // окно тянется плавно за мышью, клетки внутри пересчитываются; доводка до клеток — после отпускания
            double x0 = _startSize.X, y0 = _startSize.Y, w0 = _startSize.Cols * cw, h0 = _startSize.Rows * ch;
            double dx = dxPx / scale, dy = dyPx / scale;
            double lx = x0, ly = y0, lw = w0, lh = h0;
            if (_resizeEdge.HasFlag(Tiles.Core.Edge.Right)) lw = Math.Clamp(w0 + dx, cw, Layout.MaxCols * cw);
            if (_resizeEdge.HasFlag(Tiles.Core.Edge.Left)) { lw = Math.Clamp(w0 - dx, cw, Layout.MaxCols * cw); lx = x0 + w0 - lw; }
            if (_resizeEdge.HasFlag(Tiles.Core.Edge.Bottom)) lh = Math.Clamp(h0 + dy, ch, Layout.MaxRows * ch);
            if (_resizeEdge.HasFlag(Tiles.Core.Edge.Top)) { lh = Math.Clamp(h0 - dy, ch, Layout.MaxRows * ch); ly = y0 + h0 - lh; }
            App.Current.ResizeTile(this, r.Cols, r.Rows,
                _startSize.X + r.ShiftCols * cw, _startSize.Y + r.ShiftRows * ch, new RectD(lx, ly, lw, lh));
        }
        else
        {
            // двигаем окна напрямую, без перерисовки — так плавно; выделенные едут вместе
            App.Current.DragGroup(dxPx, dyPx);
        }
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed) return;
        _pressed = false;
        ReleaseMouseCapture();
        if (!_moved)
        {
            App.Current.CancelDrag();
            if (_ctrl) { App.Current.ToggleSelect(this); return; } // Ctrl+клик — выделить, как ярлык
            App.Current.ClearSelection();
            if (Style.ClickLaunch && IconAt(e.GetPosition(this)) is { } path) App.Current.Launch(path);
            else App.Current.TogglePanel(this);
            return;
        }
        if (_resizeEdge != Tiles.Core.Edge.None) { App.Current.FinishResize(this); return; }
        App.Current.EndDrag();
    }

    // ---- бросили ярлык на плитку ----

    private void OnDragOver(object sender, DragEventArgs e)
    {
        bool files = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = files ? DragDropEffects.Move : DragDropEffects.None;
        _frame.BorderBrush = files ? EdgeDrop : _edge;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        PaintFrame(false);
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            App.Current.AddToFolder(Folder, paths);
            e.Effects = DragDropEffects.Move;
        }
        e.Handled = true;
    }
}
