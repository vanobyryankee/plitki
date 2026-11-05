using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Tiles.Core;

namespace Tiles.App;

/// <summary>
/// Сетка ярлыков на рабочем столе. У каждого экрана своя: от левого края до правого и от верха
/// до панели задач, клетки подогнаны так, что последняя линия ложится ровно на край.
/// В режиме правки сетку тянут мышью: влево-вправо — шире/уже колонки, вверх-вниз — выше/ниже ряды.
/// </summary>
internal static class GridOverlay
{
    private static readonly List<Window> Windows = new();
    private static readonly List<Lines> Drawers = new();
    public static bool Shown => Windows.Count > 0;
    public static bool Editing { get; private set; }

    /// <summary>Правка: (новая клетка) — пересчитать сетку и расставить по ней всё.</summary>
    public static event Action<double, double>? CellChanged;
    public static event Action? EditDone;

    public static void Show(IReadOnlyList<RectD> screens, Func<RectD, (double W, double H)> cellAt, double k, bool edit = false)
    {
        Hide();
        Editing = edit;
        foreach (var s in screens)
        {
            var lines = new Lines(s, cellAt, k, edit);
            var root = new Grid { Background = edit ? new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)) : Brushes.Transparent };
            root.Children.Add(lines);
            if (edit && s == screens[0]) root.Children.Add(EditBar());
            var w = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false, ShowActivated = edit, Left = s.X, Top = s.Y, Width = s.W, Height = s.H, Title = "Плитки: сетка", // lang-ok
                Content = root, IsHitTestVisible = edit,
            };
            w.SourceInitialized += (_, _) =>
            {
                Native.SetBounds(w, s, k); // ровно по пикселям экрана, без округлений WPF
                if (edit) { w.Topmost = true; return; } // правим поверх всего, мышью
                Native.PinToDesktop(w);
                var h = new WindowInteropHelper(w).Handle;
                long ex = GetWindowLongPtr(h, -20).ToInt64();
                SetWindowLongPtr(h, -20, new IntPtr(ex | 0x20 | 0x08000000)); // мышь насквозь, без активации
            };
            if (edit) Hook(w, lines);
            w.Show();
            Windows.Add(w);
            Drawers.Add(lines);
        }
    }

    public static void Hide()
    {
        foreach (var w in Windows) w.Close();
        Windows.Clear();
        Drawers.Clear();
        Editing = false;
    }

    /// <summary>Перерисовать после смены клетки.</summary>
    public static void Redraw() { foreach (var d in Drawers) d.InvalidateVisual(); }

    // ---- правка мышью ----

    private static Point _down;
    private static (double W, double H) _startCell;
    private static bool _dragging;

    private static void Hook(Window w, Lines lines)
    {
        w.Cursor = Cursors.SizeAll;
        w.KeyDown += (_, e) => { if (e.Key is Key.Escape or Key.Enter) EditDone?.Invoke(); };
        w.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && FindBar(d)) return;
            _down = e.GetPosition(w);
            _startCell = (App.Current.CellW, App.Current.CellH);
            _dragging = true;
            w.CaptureMouse();
            e.Handled = true;
        };
        w.MouseMove += (_, e) =>
        {
            if (!_dragging) return;
            var p = e.GetPosition(w);
            // линия под мышью уходит за ней: клетка меняется на сдвиг, делённый на номер линии
            double nx = Math.Max(1, Math.Round(_down.X / _startCell.W)), ny = Math.Max(1, Math.Round(_down.Y / _startCell.H));
            double cw = Math.Clamp(_startCell.W + (p.X - _down.X) / nx, 40, 260);
            double ch = Math.Clamp(_startCell.H + (p.Y - _down.Y) / ny, 40, 260);
            CellChanged?.Invoke(cw, ch);
        };
        w.MouseLeftButtonUp += (_, _) => { _dragging = false; w.ReleaseMouseCapture(); };
    }

    private static bool FindBar(DependencyObject d)
    {
        for (var x = d; x != null; x = VisualTreeHelper.GetParent(x) ?? LogicalTreeHelper.GetParent(x))
            if (x is FrameworkElement { Tag: "bar" }) return true;
        return false;
    }

    /// <summary>Плашка подсказки с кнопками «Как у Windows» и «Готово».</summary>
    private static FrameworkElement EditBar()
    {
        var text = new TextBlock
        {
            Text = Loc.T("Тяни мышью: влево-вправо — ширина клеток, вверх-вниз — высота. Подгони под свои ярлыки."),
            Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0), FontFamily = WidgetView.Display,
        };
        var reset = Ui.Button(Loc.T("Как у Windows"), () => CellChanged?.Invoke(double.NaN, double.NaN));
        var done = Ui.Button(Loc.T("Готово"), () => EditDone?.Invoke());
        reset.Margin = new Thickness(0, 0, 8, 0);
        return new Border
        {
            Tag = "bar", Cursor = Cursors.Arrow,
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1C, 0x1C, 0x22)), CornerRadius = new CornerRadius(14),
            Padding = new Thickness(18, 10, 10, 10), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 24, 0, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { text, reset, done } },
        };
    }

    /// <summary>Линии по клеткам этого экрана — ровно по пикселям, первая и последняя по краям.</summary>
    private sealed class Lines : FrameworkElement
    {
        private static readonly Brush Line = Frozen(new SolidColorBrush(Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF)));
        private static readonly Brush Edge = Frozen(new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)));
        private static readonly Brush Middle = Frozen(new SolidColorBrush(Color.FromArgb(0x2E, 0x7C, 0xC4, 0xFF)));
        private static readonly Brush Center = Frozen(new SolidColorBrush(Color.FromArgb(0xC0, 0x7C, 0xC4, 0xFF)));
        private static readonly Brush Dot = Frozen(new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)));
        private readonly RectD _s;
        private readonly Func<RectD, (double W, double H)> _cell;
        private readonly double _k;
        private readonly bool _edit;

        public Lines(RectD s, Func<RectD, (double W, double H)> cell, double k, bool edit) { _s = s; _cell = cell; _k = k; _edit = edit; }

        private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

        protected override void OnRender(DrawingContext dc)
        {
            var (cw, ch) = _cell(_s);
            int cols = (int)Math.Round(_s.W / cw), rows = (int)Math.Round(_s.H / ch);
            double px = 1 / _k; // одна точка экрана
            double W = _s.W, H = _s.H;
            // середина экрана: чётное число клеток — яркая линия посередине, нечётное — подсвеченный средний ряд/колонка
            if (cols % 2 == 1) dc.DrawRectangle(Middle, null, new Rect(cols / 2 * cw, 0, cw, H));
            if (rows % 2 == 1) dc.DrawRectangle(Middle, null, new Rect(0, rows / 2 * ch, W, ch));
            if (cols % 2 == 0) dc.DrawRectangle(Center, null, new Rect(Math.Round(cols / 2 * cw * _k) / _k - px, 0, 3 * px, H));
            if (rows % 2 == 0) dc.DrawRectangle(Center, null, new Rect(0, Math.Round(rows / 2 * ch * _k) / _k - px, W, 3 * px));
            for (int i = 0; i <= cols; i++)
            {
                // линия в целом пикселе; крайние — внутрь экрана, чтобы были видны
                double x = Math.Min(Math.Round(i * cw * _k), Math.Round(W * _k) - 1) / _k;
                dc.DrawRectangle(i == 0 || i == cols ? Edge : Line, null, new Rect(x, 0, px, H));
            }
            for (int j = 0; j <= rows; j++)
            {
                double y = Math.Min(Math.Round(j * ch * _k), Math.Round(H * _k) - 1) / _k;
                dc.DrawRectangle(j == 0 || j == rows ? Edge : Line, null, new Rect(0, y, W, px));
            }
            for (int i = 0; i <= cols; i++)
                for (int j = 0; j <= rows; j++)
                    dc.DrawEllipse(Dot, null, new Point(Math.Min(i * cw, W - px), Math.Min(j * ch, H - px)), 2, 2);
            if (_edit)
            {
                var info = new FormattedText($"{cols} × {rows}", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(WidgetView.Display, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 28, Brushes.White, _k);
                dc.DrawText(info, new Point((W - info.Width) / 2, H - info.Height - 24));
            }
        }
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
}
