using System.IO;
using M = System.Windows.Media;
using MI = System.Windows.Media.Imaging;
using System.Windows.Controls;
using Tiles.Core;

namespace Tiles.App;

/// <summary>Мелочи для перфекционистов: свой вид виджета, выравнивание выделенных по «донору», показ сетки.</summary>
public partial class App
{
    // ---- свой вид виджета ----

    internal void OpenWidgetStyle(WidgetWindow w)
    {
        _panel?.Close();
        var info = w.Info;
        var before = info.Style?.Clone();
        var start = (info.Style ?? Config.Defaults).Clone();
        var dlg = new StyleWindow(Loc.T("Виджет «{0}»", w.View.Title), start, s => { info.Style = s; w.ApplyLook(); }, w.View.Options,
            new (string, Func<TileStyle, TileStyle?>)[]
            {
                (Loc.T("Как в общих"), _ => { info.Style = null; w.ApplyLook(); return Config.Defaults.Clone(); }),
            });
        if (dlg.ShowDialog() != true) { info.Style = before; w.ApplyLook(); }
        else if (info.Style != null) info.Style = dlg.Result.Clone();
        SaveConfig();
        Log($"widget style {info.Kind}: own={info.Style != null} glass={(info.Style ?? Config.Defaults).Glass}");
    }

    // ---- выравнивание выделенных по тому, по кому нажали правой кнопкой ----

    /// <summary>Пункты «Выровнять…» для меню плитки или виджета.</summary>
    internal IEnumerable<object> AlignMenu(IDeskItem donor)
    {
        var others = DeskItems().Where(i => i != donor && _selection.Contains(i.Id)).ToList();
        var root = new MenuItem { Header = Loc.T("Выровнять выделенные по этой") };
        if (others.Count == 0)
        {
            root.Items.Add(new MenuItem { Header = Loc.T("Сначала выдели ещё что-то: Ctrl+клик или рамкой"), IsEnabled = false });
            return new object[] { root };
        }
        void Add(string text, AlignMode mode) => root.Items.Add(WidgetWindow.Item(text, () => AlignTo(donor, others, mode)));
        Add(Loc.T("Левые края"), AlignMode.Left);
        Add(Loc.T("По центру (одна вертикальная ось)"), AlignMode.CenterX);
        Add(Loc.T("Правые края"), AlignMode.Right);
        root.Items.Add(new Separator());
        Add(Loc.T("Верхние края"), AlignMode.Top);
        Add(Loc.T("По середине (одна горизонтальная ось)"), AlignMode.CenterY);
        Add(Loc.T("Нижние края"), AlignMode.Bottom);
        if (others.OfType<WidgetWindow>().Any())
        {
            root.Items.Add(new Separator());
            Add(Loc.T("Такая же ширина"), AlignMode.SameWidth);
            Add(Loc.T("Такая же высота"), AlignMode.SameHeight);
        }
        if (others.Count >= 2)
        {
            root.Items.Add(new Separator());
            root.Items.Add(WidgetWindow.Item(Loc.T("Равные промежутки по горизонтали"), () => Distribute(donor, others, true)));
            root.Items.Add(WidgetWindow.Item(Loc.T("Равные промежутки по вертикали"), () => Distribute(donor, others, false)));
        }
        return new object[] { root, new Separator() };
    }

    private void AlignTo(IDeskItem donor, List<IDeskItem> targets, AlignMode mode)
    {
        var moved = new Dictionary<string, RectD> { [donor.Id] = donor.Rect }; // донор стоит, его не толкаем
        foreach (var t in targets.Where(t => !t.Locked))
        {
            if (mode is AlignMode.SameWidth or AlignMode.SameHeight && t is not WidgetWindow) continue; // у папок размер в клетках
            var r = AlignOps.Apply(donor.Rect, t.Rect, mode);
            if (t is WidgetWindow w) w.SetSize(r.W, r.H);
            OffGrid(t, r);
            moved[t.Id] = r;
        }
        Log($"align {mode} to {donor.Id}: {string.Join(",", moved.Keys)}");
        Settle(moved);
    }

    private void Distribute(IDeskItem donor, List<IDeskItem> targets, bool horizontal)
    {
        var all = targets.Append(donor).Where(i => !i.Locked).ToList();
        var rects = AlignOps.Distribute(all.Select(i => i.Rect).ToList(), horizontal);
        var moved = new Dictionary<string, RectD>();
        for (int i = 0; i < all.Count; i++) { OffGrid(all[i], rects[i]); moved[all[i].Id] = rects[i]; }
        Settle(moved);
    }

    /// <summary>Выровненное мимо сетки снимаем с сетки — иначе при следующем запуске оно снова прилипнет к клетке.</summary>
    private void OffGrid(IDeskItem item, RectD r)
    {
        var onGrid = Layout.Snap(r.X, r.Y, CellAt(r).W, CellAt(r).H, r.W, r.H, WorkAreaAt(r));
        if (Math.Abs(onGrid.X - r.X) < 0.5 && Math.Abs(onGrid.Y - r.Y) < 0.5) return;
        if (item is WidgetWindow w) w.Info.Snap = false;
        else if (item is TileWindow t) Info(t).Style.SnapToGrid = false;
    }

    // ---- замер плавности растягивания (без мыши) ----

    /// <summary>
    /// Тянем музыку за левый край по кадрам и снимаем экран: где на деле левый и правый край.
    /// Правый край должен стоять на месте, левый — ровно идти за «мышью».
    /// </summary>
    /// <summary>
    /// Кадры растягивания виджета: окно проходит тот же путь, что под мышью (сцена, перестройка, стекло),
    /// а содержимое каждого кадра сохраняется картинкой в папку — мигание видно глазами по кадрам.
    /// </summary>
    internal void RunFrames(string kind, string dir)
    {
        Store = new FolderStore(Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "tiles-preview"));
        Config = new TilesConfig();
        DpiScale = GetDpiForSystem() / 96.0;
        ReadGridMetrics();
        Config.Normalize();
        Directory.CreateDirectory(dir);
        var top = Screens.OrderBy(s => s.Y).First();
        var start = new RectD(top.X + 600, top.Y + 200, 233, 203);
        var info = new WidgetInfo { Kind = kind, X = start.X, Y = start.Y, W = start.W, H = start.H };
        info.Props["own"] = "1";
        var v = WidgetView.Create(info);
        var w = new WidgetWindow(v) { Left = start.X, Top = start.Y };
        w.ApplySize(); w.Show(); w.ApplyLook(); v.Demo();
        StartWidgets();
        var log = new List<string>();
        int i = -5;
        EventHandler? h = null;
        h = (_, _) =>
        {
            var r = start;
            if (i >= 0 && i < 30) r = r with { W = start.W + 4 * i };
            else if (i >= 30) r = r with { W = start.W + 116, H = start.H + 4 * (i - 30) };
            if (i >= 0) w.BenchStep(r, "canvas");
            var inner = w.Inner;
            inner.UpdateLayout();
            double iw = Math.Max(1, inner.ActualWidth), ih = Math.Max(1, inner.ActualHeight);
            var dv = new M.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(M.Brushes.Magenta, null, new System.Windows.Rect(0, 0, iw, ih));
                dc.DrawRectangle(new M.VisualBrush(inner) { Stretch = M.Stretch.None, AlignmentX = M.AlignmentX.Left, AlignmentY = M.AlignmentY.Top }, null, new System.Windows.Rect(0, 0, iw, ih));
            }
            var bmp = new MI.RenderTargetBitmap((int)iw, (int)ih, 96, 96, M.PixelFormats.Pbgra32);
            bmp.Render(dv);
            var enc = new MI.PngBitmapEncoder();
            enc.Frames.Add(MI.BitmapFrame.Create(bmp));
            using (var f = File.Create(Path.Combine(dir, $"f{i + 5:000}.png"))) enc.Save(f);
            log.Add($"{i} rect {r.W}x{r.H} inner {iw:0}x{ih:0} view {v.ActualWidth:0}x{v.ActualHeight:0} shape {v.Shape}");
            if (++i > 50)
            {
                M.CompositionTarget.Rendering -= h;
                File.WriteAllLines(Path.Combine(dir, "frames.txt"), log);
                Shutdown();
            }
        };
        M.CompositionTarget.Rendering += h;
    }

    internal void RunBench(string file, string mode)
    {
        Store = new FolderStore(Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "tiles-preview"));
        Config = new TilesConfig();
        DpiScale = GetDpiForSystem() / 96.0;
        ReadGridMetrics();
        Config.Normalize();
        double k = DpiScale;
        var top = Screens.OrderBy(s => s.Y).First(); // верхний экран: там обычно пусто
        var start = new RectD(top.X + 700, top.Y + 260, 420, 140);
        var cap = new System.Drawing.Rectangle((int)((start.X - 300) * k), (int)((start.Y + 70) * k), (int)(760 * k), 1); // одна строка через середину виджета — снимок быстрый
        var baseline = Grab(cap); // живёт до конца замера
        var info = new WidgetInfo { Kind = "music", X = start.X, Y = start.Y, W = start.W, H = start.H };
        var v = WidgetView.Create(info);
        var w = new WidgetWindow(v) { Left = start.X, Top = start.Y };
        Native.BenchOnTop = true;
        w.Opacity = 1;
        w.ApplySize(); w.Show(); w.ApplyLook(); v.Demo();
        w.Topmost = true;
        var log = new List<string> { $"mode {mode} k {k}" };
        var shots = new System.Collections.Concurrent.ConcurrentQueue<string>();
        int frame = -40;
        bool running = true;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        // снимаем экран из отдельного потока: снимок с прозрачными окнами из своего же потока окна виснет
        var shooter = new Thread(() =>
        {
            while (Volatile.Read(ref running))
            {
                try
                {
                    using var bmp = Grab(cap);
                    var (l, r) = Edges(bmp, baseline, 0);
                    shots.Enqueue($"shot t={clock.Elapsed.TotalMilliseconds:0.0} gotL={l} gotR={r}");
                }
                catch (Exception ex) { shots.Enqueue("err " + ex.Message); }
                Thread.Sleep(4);
            }
        }) { IsBackground = true };
        EventHandler? h = null;
        h = (_, _) =>
        {
            frame++;
            if (frame == 0) shooter.Start();
            if (frame % 30 == 0) File.AppendAllText(file + ".progress", $"{frame} ");
            if (frame < 0) return;
            if (frame >= 150)
            {
                System.Windows.Media.CompositionTarget.Rendering -= h;
                Volatile.Write(ref running, false);
                shooter.Join(2000);
                File.WriteAllLines(file, log.Concat(shots));
                w.Close();
                Shutdown();
                return;
            }
            var rr = start with { X = start.X - Math.Min(frame, 100) * 2, W = start.W + Math.Min(frame, 100) * 2 };
            w.BenchStep(rr, mode);
            log.Add($"want t={clock.Elapsed.TotalMilliseconds:0.0} L={(rr.X * k - cap.X):0} R={(rr.Right * k - cap.X):0}");
        };
        System.Windows.Media.CompositionTarget.Rendering += h;
    }

    private static System.Drawing.Bitmap Grab(System.Drawing.Rectangle r)
    {
        var b = new System.Drawing.Bitmap(r.Width, r.Height);
        using var g = System.Drawing.Graphics.FromImage(b);
        g.CopyFromScreen(r.X, r.Y, 0, 0, r.Size, (Environment.GetEnvironmentVariable("TILES_CAPBLT") == "1" ? System.Drawing.CopyPixelOperation.SourceCopy | System.Drawing.CopyPixelOperation.CaptureBlt : System.Drawing.CopyPixelOperation.SourceCopy)); // без CaptureBlt прозрачные окна не попадают в снимок
        return b;
    }

    /// <summary>Крайние точки строки, где снимок заметно отличается от пустого стола.</summary>
    private static (int L, int R) Edges(System.Drawing.Bitmap now, System.Drawing.Bitmap empty, int row)
    {
        int l = -1, r = -1;
        for (int x = 0; x < now.Width; x++)
        {
            var a = now.GetPixel(x, row); var b = empty.GetPixel(x, row);
            if (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) > 45) { if (l < 0) l = x; r = x; }
        }
        return (l, r);
    }

    // ---- сетка ярлыков на столе ----

    internal bool GridShown => GridOverlay.Shown;

    internal void ToggleGrid()
    {
        if (GridOverlay.Shown) GridOverlay.Hide();
        else GridOverlay.Show(Screens, CellAt, DpiScale);
        foreach (var s in Screens) Log($"grid screen {s.X:0},{s.Y:0} {s.W:0}x{s.H:0} cell {CellAt(s).W:0.0}x{CellAt(s).H:0.0}");
        Log($"grid {GridOverlay.Shown}");
    }

    private bool _gridHooked;

    /// <summary>Правка сетки: тянут мышью, плитки тут же встают по новым клеткам.</summary>
    internal void EditGrid()
    {
        if (!_gridHooked)
        {
            _gridHooked = true;
            GridOverlay.CellChanged += (w, h) =>
            {
                Config.GridW = w; Config.GridH = h;
                GridOverlay.Redraw();
                PlaceAll();
                foreach (var g in _widgets.Values.Where(g => g.Snap)) { var p = Fit(g.Rect, true); g.MoveTo(p.X, p.Y); }
                SaveConfigSoon();
            };
            GridOverlay.EditDone += () =>
            {
                GridOverlay.Hide();
                SaveConfig();
                Log($"grid edited: cell {CellW:0.0}x{CellH:0.0} (windows {WinCellW:0.0}x{WinCellH:0.0})");
            };
        }
        GridOverlay.Show(Screens, CellAt, DpiScale, edit: true);
    }

    /// <summary>Пункты сетки для меню: показать и подогнать.</summary>
    internal IEnumerable<object> GridMenu() => new object[]
    {
        WidgetWindow.Item((GridShown ? "✓ " : "") + Loc.T("Показать сетку"), ToggleGrid),
        WidgetWindow.Item(Loc.T("Редактировать сетку…"), EditGrid),
    };
}
