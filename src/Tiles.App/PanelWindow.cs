using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using Tiles.Core;

namespace Tiles.App;

/// <summary>Стеклянная панель с содержимым папки, выезжает рядом с плиткой.</summary>
internal sealed class PanelWindow : Window
{
    private const int Cols = 4, MaxRows = 4;
    private const double Pad = 16, HeaderH = 40;
    private static readonly Brush ItemHover = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));

    private readonly TileWindow _tile;
    private readonly WrapPanel _items = new();
    private readonly TextBlock _header;
    private readonly TextBlock _empty;
    private readonly Border _glass;
    private readonly ScrollViewer _scroll;
    private bool _dragging;

    public string Folder => _tile.Folder;
    private TileStyle Style => App.Current.Info(_tile).Style;
    private double IconSize => Style.IconSize;
    private double ItemW => Style.PanelCaptions ? Math.Max(92, IconSize + 40) : IconSize + 28;
    private double ItemH => IconSize + (Style.PanelCaptions ? 52 : 22);

    public PanelWindow(TileWindow tile)
    {
        _tile = tile;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        AllowDrop = true;
        Title = Loc.T("Папка {0}", tile.Folder);

        _header = new TextBlock
        {
            Foreground = Brushes.White, FontSize = 17, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(6, 0, 0, 0), Height = HeaderH, VerticalAlignment = VerticalAlignment.Top,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _empty = new TextBlock
        {
            Text = Loc.T("Перетащи сюда ярлыки с рабочего стола"),
            Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
            FontSize = 13, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            Margin = new Thickness(8, 20, 8, 20),
        };
        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new StackPanel { Children = { _empty, _items } },
        };
        var dock = new DockPanel();
        DockPanel.SetDock(_header, Dock.Top);
        dock.Children.Add(_header);
        dock.Children.Add(_scroll);

        var tint = new Border
        {
            CornerRadius = new CornerRadius(24),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            Background = new SolidColorBrush(Color.FromArgb(0x5C, 0x14, 0x14, 0x18)),
            Padding = new Thickness(Pad, 12, Pad, Pad),
            Child = dock,
        };
        _glass = new Border
        {
            CornerRadius = new CornerRadius(24),
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x26, 0x26, 0x2A)),
            Child = tint,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
        };
        Content = _glass;

        // запуск ярлыка закрывает панель, и на закрытии Windows ещё раз шлёт «потеряла фокус» — второй Close ронял программу
        Closing += (_, _) => _closing = true;
        Deactivated += (_, _) => { if (!_dragging) Close(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) App.Current.AddToFolder(Folder, paths);
            e.Handled = true;
        };

        Rebuild();
    }

    public void ShowNear(RectD tileRect)
    {
        var (w, h) = Measure();
        var r = Layout.PlacePanel(tileRect, w, h, App.Current.WorkAreaAt(tileRect), gap: 10);
        Left = r.X; Top = r.Y; Width = w; Height = h;
        var img = Wallpaper.Glass(Wallpaper.ScreenRect(this), Math.Max(24, Style.Blur + 12));
        if (img != null) _glass.Background = new ImageBrush(img) { Stretch = Stretch.Fill };
        Opacity = 0;
        Show();
        Activate();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
        var st = (ScaleTransform)_glass.RenderTransform;
        var grow = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    private (double W, double H) Measure()
    {
        int count = App.Current.Store.Items(Folder).Count;
        var (cols, rows) = Layout.PanelGrid(count, Cols, MaxRows);
        double w = Math.Max(cols, 3) * ItemW + 2 * Pad + 4 + (count > Cols * MaxRows ? 14 : 0);
        double h = 12 + HeaderH + (count == 0 ? 70 : rows * ItemH) + Pad + 4;
        return (w, h);
    }

    public void Rebuild()
    {
        _header.Text = Folder;
        _items.Children.Clear();
        _items.ItemWidth = ItemW;
        _items.ItemHeight = ItemH;
        var items = App.Current.Store.Items(Folder);
        _empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var path in items) _items.Children.Add(MakeItem(path));
        _scroll.VerticalScrollBarVisibility = items.Count > Cols * MaxRows ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        if (IsLoaded)
        {
            var (w, h) = Measure();
            Width = w; Height = h;
        }
    }

    private FrameworkElement MakeItem(string path)
    {
        var img = Icons.Image(path, IconSize);
        img.Margin = new Thickness(0, 6, 0, 4);
        var text = new TextBlock
        {
            Text = Icons.DisplayName(path),
            Foreground = Brushes.White, FontSize = 12, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 32, Margin = new Thickness(2, 0, 2, 0),
        };
        var cell = new Border
        {
            CornerRadius = new CornerRadius(12), Margin = new Thickness(2),
            Background = Brushes.Transparent, ToolTip = Icons.DisplayName(path),
            Child = Style.PanelCaptions ? new StackPanel { Children = { img, text } } : img,
        };
        cell.MouseEnter += (_, _) => cell.Background = ItemHover;
        cell.MouseLeave += (_, _) => cell.Background = Brushes.Transparent;

        Point? down = null;
        cell.MouseLeftButtonDown += (_, e) => { down = e.GetPosition(this); cell.CaptureMouse(); e.Handled = true; };
        cell.MouseMove += (_, e) =>
        {
            if (down is not { } d || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - d.X) < 6 && Math.Abs(p.Y - d.Y) < 6) return;
            down = null;
            cell.ReleaseMouseCapture();
            DragOut(cell, path);
        };
        cell.MouseLeftButtonUp += (_, e) =>
        {
            cell.ReleaseMouseCapture();
            if (down is null) return;
            down = null;
            Launch(path);
            e.Handled = true;
        };
        if (Style.ShellMenu)
        {
            cell.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                _dragging = true; // меню проводника забирает фокус — панель не закрываем
                try { ShellMenu.Show(this, path); }
                finally { _dragging = false; }
                App.Current.ItemsChanged(Folder); // могли удалить или переименовать
            };
        }
        return cell;
    }

    private void DragOut(UIElement source, string path)
    {
        _dragging = true;
        try
        {
            var data = new DataObject(DataFormats.FileDrop, new[] { path });
            DragDrop.DoDragDrop(source, data, DragDropEffects.Move | DragDropEffects.Copy | DragDropEffects.Link);
        }
        finally { _dragging = false; }
        App.Current.ItemsChanged(Folder);
        if (!IsActive) Close();
    }

    private bool _closing;

    /// <summary>Закрыть, если ещё не закрывается.</summary>
    public new void Close() { if (_closing) return; _closing = true; base.Close(); }

    private void Launch(string path)
    {
        App.Current.Launch(path);
        Close();
    }
}
