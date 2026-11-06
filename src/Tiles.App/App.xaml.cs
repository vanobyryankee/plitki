using System.IO;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Tiles.Core;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Tiles.App;

public partial class App : Application
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "Плитки"; // lang-ok

    private Mutex? _single;
    private Forms.NotifyIcon? _tray;
    private FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly List<TileWindow> _tiles = new();
    private string _configPath = "";
    private PanelWindow? _panel;
    private (string Folder, DateTime At) _lastPanelClose;

    internal FolderStore Store { get; private set; } = null!;
    internal TilesConfig Config { get; private set; } = null!;
    private double _winCellW = 75, _winCellH = 100;
    /// <summary>Клетка: своя, если подогнали руками в «Редактировать сетку», иначе как у ярлыков Windows.</summary>
    internal double CellW => Config is { } c && !double.IsNaN(c.GridW) ? c.GridW : _winCellW;
    internal double WinCellW => _winCellW;
    internal double WinCellH => _winCellH;
    internal double CellH => Config is { } c && !double.IsNaN(c.GridH) ? c.GridH : _winCellH;
    internal double DpiScale { get; private set; } = 1;
    private readonly DispatcherTimer _wallpaperCheck = new() { Interval = TimeSpan.FromMinutes(10) };

    internal static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // ошибка в одном окне не должна убирать с рабочего стола всё: пишем в журнал и живём дальше
        DispatcherUnhandledException += (_, ex) =>
        {
            try { Log("ERROR " + ex.Exception.ToString().Replace(Environment.NewLine, " | ")); } catch { }
            ex.Handled = true;
        };
        if (e.Args.Length == 3 && e.Args[0] == "--resize-bench") { RunBench(e.Args[1], e.Args[2]); return; }
        if (e.Args.Length == 3 && e.Args[0] == "--resize-frames") { RunFrames(e.Args[1], e.Args[2]); return; }
        if (e.Args.Contains("--settings-preview"))
        {
            // только посмотреть окно настроек (проверка вида), без плиток
            Store = new FolderStore(Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "tiles-preview"));
            Config = new TilesConfig();
            DpiScale = GetDpiForSystem() / 96.0;
            new StyleWindow(Loc.T("Папка «{0}»", Loc.T("Игры")), Config.Defaults, _ => { }, (Loc.T("Как в общих"), _ => null), (Loc.T("Сделать общими"), _ => null)).ShowDialog();
            Shutdown();
            return;
        }
        // снимки на другом языке: TILES_LANG=en и т.п. (проверка переводов без экрана)
        if (Environment.GetEnvironmentVariable("TILES_LANG") is { Length: > 0 } previewLang && e.Args.Any(a => a.EndsWith("-preview"))) Loc.Set(previewLang);
        if (e.Args.Length == 2 && e.Args[0] == "--widgets-preview")
        {
            // снимок вида виджетов в файлы (проверка без экрана)
            Store = new FolderStore(Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "tiles-preview"));
            Config = new TilesConfig();
            DpiScale = GetDpiForSystem() / 96.0;
            ReadGridMetrics();
            Config.Normalize();
            var shots = new List<(Window, string)>();
            double x = 40;
            foreach (var info in Config.Widgets)
            {
                var v = WidgetView.Create(info);
                var w = new WidgetWindow(v) { Left = x, Top = 40 };
                w.ApplySize();
                w.Show(); w.ApplyLook(); v.Demo();
                shots.Add((w, info.Kind));
                x += w.Width + 20;
            }
            {
                // растянутая музыка: должна заполнять всё стекло, а не висеть островком
                var wide = new WidgetInfo { Kind = "music", W = 720, H = 180, X = 40, Y = 300 };
                var v = WidgetView.Create(wide);
                var w = new WidgetWindow(v) { Left = 40, Top = 300 };
                w.ApplySize(); w.Show(); w.ApplyLook(); v.Demo();
                shots.Add((w, "music-wide"));
            }
            {
                // высокие и квадратные формы всех виджетов, с подробностями
                double x2 = 40;
                foreach (var (kind, ww, hh, tag) in new[] {
                    ("music", 230.0, 400.0, "tall"), ("music", 290.0, 240.0, "square"), ("system", 150.0, 400.0, "tall"), ("system", 260.0, 290.0, "square"),
                    ("clock", 180.0, 320.0, "tall"), ("clock", 220.0, 210.0, "square"), ("weather", 200.0, 340.0, "tall"), ("system", 340.0, 170.0, "wide"),
                    ("net", 96.0, 96.0, "mini"), ("net", 96.0, 96.0, "mini-off"), ("net", 152.0, 152.0, "button"), ("net", 342.0, 152.0, "medium"), ("net", 342.0, 152.0, "medium-off"), ("net", 342.0, 152.0, "medium-hybrid"), ("net", 342.0, 152.0, "medium-glass"), ("net", 482.0, 312.0, "full"), ("net", 242.0, 522.0, "tall"), ("net", 332.0, 402.0, "square") })
                {
                    var info = new WidgetInfo { Kind = kind, W = ww, H = hh, X = x2, Y = 520 };
                    info.Props["album"] = info.Props["detail"] = info.Props["days"] = info.Props["feel"] = "1";
                    if (tag.EndsWith("-off")) info.Props["demo-off"] = "1";
                    if (tag.EndsWith("-hybrid")) info.Props["mix"] = "50";
                    if (tag.EndsWith("-glass")) info.Props["mix"] = "0";
                    var v = WidgetView.Create(info);
                    var w = new WidgetWindow(v) { Left = x2, Top = 520 };
                    w.ApplySize(); w.Show(); w.ApplyLook(); v.Demo();
                    shots.Add((w, $"{kind}-{tag}"));
                    x2 += ww + 16;
                }
            }
            Dispatcher.BeginInvoke(() =>
            {
                foreach (var (w, name) in shots) Snap(w, Path.Combine(e.Args[1], $"w-{name}.png"));
                Shutdown();
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--hub-preview")
        {
            // окно приложения со всеми страницами в файлы
            Store = new FolderStore(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop-Плитки")); // lang-ok
            _configPath = Path.Combine(Path.GetTempPath(), "tiles-preview.json");
            Config = ConfigStore.Load(Path.Combine(Store.Root, "tiles.json"));
            DpiScale = GetDpiForSystem() / 96.0;
            ReadGridMetrics();
            var hub = new HubWindow(preview: true);
            hub.Show();
            var pages = new[] { "workshop", "folders", "look", "load" };
            int i = 0;
            void Next()
            {
                if (i > 0) Snap(hub, Path.Combine(e.Args[1], $"hub-{pages[i - 1]}.png"));
                if (i == pages.Length) { Shutdown(); return; }
                hub.ShowPage(pages[i++]);
                Dispatcher.BeginInvoke(Next, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            Dispatcher.BeginInvoke(Next, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            return;
        }
        _single = new Mutex(true, "Tiles.DesktopFolders.Single", out bool first);
        if (!first)
        {
            // уже запущено — просим ту копию показать окно приложения
            try { using var ev = EventWaitHandle.OpenExisting(HubEventName); ev.Set(); } catch { }
            Shutdown();
            return;
        }
        ListenForHub();

        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop-Плитки"); // lang-ok
        Store = new FolderStore(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), root);
        _configPath = Path.Combine(root, "tiles.json");
        Config = ConfigStore.Load(_configPath);
        // настройки из версий до языков: остаёмся на русском, а не прыгаем на язык Windows
        try { if (File.Exists(_configPath) && !File.ReadAllText(_configPath).Contains("\"Language\"")) { Config.Language = "ru"; SaveConfigSoon(); } } catch { }
        Loc.Set(Config.Language);
        Loc.Changed += OnLanguageChanged;

        ReadGridMetrics();
        CreateTray();
        Sync();
        if (_tiles.Count == 0) NewFolder();
        ApplyAutostart();
        WatchDesktop();
        CatchUpGlass();
        WatchTimeZone();

        _watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
            EnableRaisingEvents = true,
        };
        // свои файлы (tiles.json, его временная копия, журнал) лежат тут же: их запись не повод
        // пересобирать плитки, иначе сохранение будит само себя по кругу
        FileSystemEventHandler h = (_, a) =>
        {
            if (OwnFile(root, a.FullPath)) return;
            Dispatcher.BeginInvoke(() => { _debounce.Stop(); _debounce.Start(); });
        };
        _watcher.Created += h; _watcher.Deleted += h; _watcher.Renamed += (s, a) => h(s, a);
        _debounce.Tick += (_, _) => { _debounce.Stop(); Sync(); };

        _homeScreens = ScreensKey();
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(OnDisplayChanged);
        StartWidgets();
        Untangle();
        Marquee.Start();
        EnsureShortcuts();
        if (e.Args.Contains("--hub")) OpenHub("workshop");
        // обои сменили (в том числе «Windows: интересное» раз в день) — перерисовать стекло
        SystemEvents.UserPreferenceChanged += (_, a) =>
        {
            Log($"pref changed {a.Category}");
            if (a.Category == UserPreferenceCategory.Desktop) Dispatcher.BeginInvoke(RefreshGlass);
        };
        _wallpaperCheck.Tick += (_, _) => RefreshGlass();
        _wallpaperCheck.Start();
        Diag.Start();
    }

    private static void Snap(Window w, string file)
    {
        var el = (FrameworkElement)w.Content;
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(w.ActualWidth * 2), (int)(w.ActualHeight * 2), 192, 192, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(el);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using var f = File.Create(file);
        enc.Save(f);
    }

    internal static bool OwnFile(string root, string path)
        => FolderStore.IsServiceFile(root, path);

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        // без программы корзина нужна обычная — возвращаем значок Windows
        if (_widgets.ContainsKey("bin")) try { RecycleBin.HideWindowsIcon(false); } catch { }
        Marquee.Stop();
        base.OnExit(e);
    }

    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();

    private void ReadGridMetrics()
    {
        double scale = GetDpiForSystem() / 96.0;
        DpiScale = scale;
        var sp = Native.DesktopIconSpacing();
        if (sp is { } s) { _winCellW = s.W / scale; _winCellH = s.H / scale; }
    }

    internal RectD WorkArea
    {
        get { var w = SystemParameters.WorkArea; return new RectD(w.X, w.Y, w.Width, w.Height); }
    }

    /// <summary>Рабочие области всех экранов (без панелей задач), в точках экрана.</summary>
    internal IReadOnlyList<RectD> Screens
    {
        get
        {
            double k = DpiScale;
            var list = Forms.Screen.AllScreens.Select(s => new RectD(s.WorkingArea.X / k, s.WorkingArea.Y / k, s.WorkingArea.Width / k, s.WorkingArea.Height / k)).ToList();
            return list.Count > 0 ? list : new[] { WorkArea };
        }
    }

    /// <summary>Клетка сетки на экране под прямоугольником: ровно делит этот экран до краёв и до панели задач.</summary>
    internal (double W, double H) CellAt(RectD r) => Layout.FitCell(WorkAreaAt(r), CellW, CellH);

    /// <summary>Где и какого размера папка: её клетки на её экране.</summary>
    internal RectD TileRect(TileInfo t)
    {
        var c = CellAt(new RectD(t.X, t.Y, t.Cols * CellW, t.Rows * CellH));
        return new RectD(t.X, t.Y, t.Cols * c.W, t.Rows * c.H);
    }

    /// <summary>Экран, на котором стоит прямоугольник: плитки и виджеты живут на любом мониторе.</summary>
    internal RectD WorkAreaAt(RectD r) => Layout.ScreenFor(r, Screens);

    /// <summary>Все экраны вместе — куда можно утащить группу.</summary>
    internal RectD AllScreens
    {
        get
        {
            var s = Screens;
            double l = s.Min(x => x.X), t = s.Min(x => x.Y), r = s.Max(x => x.Right), b = s.Max(x => x.Bottom);
            return new RectD(l, t, r - l, b - t);
        }
    }

    // ---- плитки ----

    /// <summary>Сверяет плитки с папками на диске: новые папки получают плитку, пропавшие — убираются.</summary>
    internal void Sync()
    {
        var dirs = Directory.GetDirectories(Store.Root).Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        Config.Tiles.RemoveAll(t => !dirs.Contains(t.Name));
        foreach (var t in Config.Tiles.ToList()) ApplyAutoName(t.Name);
        dirs = Directory.GetDirectories(Store.Root).Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var d in dirs.Where(d => Config.Tiles.All(t => !t.Name.Equals(d, StringComparison.OrdinalIgnoreCase))))
            Config.Tiles.Add(FreeSpot(d));

        foreach (var w in _tiles.Where(w => Config.Tiles.All(t => t.Name != w.Folder)).ToList())
        { w.Close(); _tiles.Remove(w); }
        foreach (var t in Config.Tiles.Where(t => _tiles.All(w => w.Folder != t.Name)))
        {
            var w = new TileWindow(t.Name);
            _tiles.Add(w);
            w.Show();
        }
        PlaceAll();
        foreach (var w in _tiles) w.ApplyStyle();
        _panel?.Rebuild();
        SaveConfig();
    }

    internal TileInfo Info(TileWindow w) => Config.Tiles.First(x => x.Name == w.Folder);

    /// <summary>Запустить ярлык так же, как двойной клик в проводнике.</summary>
    internal void Launch(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
                { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) });
            Log($"launch {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.T("Не запускается «{0}»: {1}", Icons.DisplayName(path), ex.Message), Loc.T("Плитки"));
        }
    }

    private void PlaceAll()
    {
        foreach (var w in _tiles) Place(w, Info(w), glass: true);
        ScheduleIconCheck();
    }

    /// <summary>Окно плитки ровно по её клеткам cols×rows: стекло и подпись под ним.</summary>
    private void Place(TileWindow w, TileInfo t, bool glass)
    {
        var r = TileRect(t);
        var c = CellAt(r);
        var p = t.Style.SnapToGrid ? Layout.Snap(t.X, t.Y, c.W, c.H, r.W, r.H, WorkAreaAt(r)) : Layout.Clamp(t.X, t.Y, r.W, r.H, WorkAreaAt(r));
        t.X = p.X; t.Y = p.Y;
        r = TileRect(t); // на другом экране клетка своя
        w.Left = r.X;
        w.Top = r.Y;
        w.Width = r.W;
        w.Height = r.H; // отступ и подпись внутри своих клеток, на соседей не залезает
        if (glass) w.UpdateGlass();
    }

    /// <summary>Сменили привязку к сетке — встать заново и раздвинуть соседей.</summary>
    internal void MoveTo(TileWindow w)
    {
        Place(w, Info(w), glass: true);
        Settle(new Dictionary<string, RectD> { [w.Id] = w.Rect });
    }

    internal void PlaceTile(TileWindow w, double x, double y)
    {
        var t = Info(w);
        t.X = x; t.Y = y;
        Place(w, t, glass: true);
    }

    // ---- ярлыки Windows не должны прятаться под плитками ----

    private readonly DispatcherTimer _iconsTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private readonly List<FileSystemWatcher> _desktopWatchers = new();

    private void WatchDesktop()
    {
        _iconsTimer.Tick += (_, _) => { _iconsTimer.Stop(); RelocateCoveredIcons(); };
        foreach (var dir in new[] { Store.Desktop, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) })
        {
            if (!Directory.Exists(dir)) continue;
            var fw = new FileSystemWatcher(dir) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName, EnableRaisingEvents = true };
            fw.Created += (_, _) => Dispatcher.BeginInvoke(ScheduleIconCheck);
            fw.Renamed += (_, _) => Dispatcher.BeginInvoke(ScheduleIconCheck);
            _desktopWatchers.Add(fw);
        }
        ScheduleIconCheck();
    }

    /// <summary>Проверить ярлыки чуть позже: проводнику нужно время, чтобы показать новый ярлык.</summary>
    internal void ScheduleIconCheck() { _iconsTimer.Stop(); _iconsTimer.Start(); }

    /// <summary>Ярлыки Windows, закрытые плиткой, переставляет в ближайшую свободную клетку.</summary>
    private void RelocateCoveredIcons()
    {
        try
        {
            using var snap = DesktopIcons.Read();
            if (snap == null) { Log("icons: no desktop view"); return; }
            if (snap.AutoArrange) { Log("icons: auto-arrange is on, skip"); return; }
            Native.GetWindowRect(Native.DesktopListView(), out var lv);
            double s = DpiScale, sw = snap.SpacingX, sh = snap.SpacingY;
            if (sw <= 0 || sh <= 0) return;

            var tiles = _tiles.Cast<Window>().Concat(_widgets.Values.Where(w => w.IsVisible)).Select(w =>
            {
                var r = Native.WindowRect(w);
                return new RectD(r.Left - lv.Left, r.Top - lv.Top, r.Right - r.Left, r.Bottom - r.Top);
            }).ToList();
            var covered = snap.Icons.Where(i => DesktopGrid.Covered(i.Pos, sw, sh, tiles)).ToList();
            Log($"icons check: {snap.Icons.Count} icons, spacing {sw}x{sh}, view at {lv.Left},{lv.Top}, under tiles {covered.Count}");
            if (covered.Count == 0) return;

            var wa = SystemParameters.WorkArea;
            var area = new RectD(wa.X * s - lv.Left, wa.Y * s - lv.Top, wa.Width * s, wa.Height * s);
            // сетка проводника может быть сдвинута: берём самый частый остаток
            double gx = Mode(snap.Icons.Select(i => Mod(i.Pos.X, sw)));
            double gy = Mode(snap.Icons.Select(i => Mod(i.Pos.Y, sh)));
            var occupied = snap.Icons.Except(covered).Select(i => i.Pos).ToList();
            foreach (var icon in covered)
            {
                if (DesktopGrid.FreeCell(area, gx, gy, sw, sh, occupied, tiles) is not { } cell)
                {
                    Log("icons: no free cell left");
                    break;
                }
                bool ok = snap.MoveTo(icon, cell);
                occupied.Add(cell);
                Log($"icon {Path.GetFileName(icon.Path)} {icon.Pos.X:0},{icon.Pos.Y:0} -> {cell.X:0},{cell.Y:0} ok={ok}");
            }
        }
        catch (Exception ex) { Log("icons failed: " + ex.ToString().Replace(Environment.NewLine, " | ")); }
    }

    private static double Mod(double v, double m) => ((v % m) + m) % m;

    private static double Mode(IEnumerable<double> values) =>
        values.Select(v => Math.Round(v)).GroupBy(v => v).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();

    private void RefreshGlass()
    {
        Wallpaper.Invalidate();
        foreach (var w in _tiles) w.UpdateGlass();
        foreach (var g in _widgets.Values) g.UpdateGlass();
    }

    /// <summary>Свободное место для новой плитки 2×2: справа сверху вниз.</summary>
    private TileInfo FreeSpot(string name)
    {
        var wa = WorkArea;
        var (cw, ch) = Layout.FitCell(wa, CellW, CellH);
        double tw = 2 * cw, th = 2 * ch;
        for (double x = wa.Right - tw - cw * 2; x >= wa.X; x -= cw)
            for (double y = wa.Y; y + th <= wa.Bottom; y += ch)
            {
                var p = Layout.Snap(x, y, cw, ch, tw, th, wa);
                var cand = new RectD(p.X, p.Y, tw, th);
                bool busy = Config.Tiles.Any(t => Arrange.Overlaps(cand, TileRect(t)))
                    || _widgets.Values.Any(wg => Arrange.Overlaps(cand, wg.Rect));
                if (!busy) return new TileInfo { Name = name, X = p.X, Y = p.Y, Style = Config.Defaults.Clone() };
            }
        return new TileInfo { Name = name, X = wa.X, Y = wa.Y, Style = Config.Defaults.Clone() };
    }

    internal void MoveTile(TileWindow w, double left, double top)
    {
        var t = Info(w);
        w.Left = left; w.Top = top; // окно двигали в обход WPF — сверяем, иначе он не сдвинет его обратно
        t.X = left;
        t.Y = top;
        Place(w, t, glass: true);
        SaveConfig();
        ScheduleIconCheck();
    }

    /// <summary>Живое растягивание: стекло перерисуем, когда отпустят мышь.</summary>
    internal void ResizeTile(TileWindow w, int cols, int rows, double x, double y, RectD live)
    {
        var t = Info(w);
        if (t.Cols != cols || t.Rows != rows)
        {
            t.Cols = cols; t.Rows = rows;
            w.Refresh(); // сколько ярлыков влезает — по ближайшим клеткам
        }
        t.X = x; t.Y = y;
        Native.SetBounds(w, live, DpiScale); // место и размер разом — край не дрожит
        w.Left = live.X; w.Top = live.Y; w.Width = live.W; w.Height = live.H;
        w.FollowGlass();
    }

    internal void FinishResize(TileWindow w)
    {
        var t = Info(w);
        var target = TileRect(t);
        var c = CellAt(target);
        var p = t.Style.SnapToGrid ? Layout.Snap(t.X, t.Y, c.W, c.H, target.W, target.H, WorkAreaAt(target)) : Layout.Clamp(t.X, t.Y, target.W, target.H, WorkAreaAt(target));
        t.X = p.X; t.Y = p.Y;
        Log($"resize {t.Name} -> {t.Cols}x{t.Rows}");
        Settle(new Dictionary<string, RectD> { [w.Id] = TileRect(t) }); // выросла — соседи расступаются
    }

    internal void NewFolder()
    {
        var name = Store.CreateFolder(Loc.T("Папка"));
        Config.Tiles.Add(FreeSpot(name));
        Sync();
    }

    internal void RenameFolder(TileWindow w)
    {
        var dlg = new RenameWindow(w.Folder);
        if (dlg.ShowDialog() != true) return;
        var t = Info(w);
        var renamed = Store.RenameFolder(w.Folder, dlg.Value);
        t.Name = renamed;
        t.AutoName = false; // назвал сам — больше не трогаем
        w.Folder = renamed;
        w.Refresh();
        SaveConfig();
    }

    /// <summary>Настройки одной папки: меняются сразу, «Отмена» возвращает как было.</summary>
    internal void OpenSettings(TileWindow w)
    {
        _panel?.Close();
        var t = Info(w);
        var before = t.Style.Clone();
        var dlg = new StyleWindow(Loc.T("Папка «{0}»", t.Name), t.Style, s => { t.Style = s; Restyle(w); },
            (Loc.T("Как в общих"), _ => Config.Defaults.Clone()),
            (Loc.T("Сделать общими"), s => { Config.Defaults = s.Clone(); SaveConfig(); return null; }));
        if (dlg.ShowDialog() != true) { t.Style = before; Restyle(w); }
        SaveConfig();
        Log($"settings {t.Name}: snap={t.Style.SnapToGrid} icon={t.Style.IconSize} tint={t.Style.Tint} opacity={t.Style.GlassOpacity}");
    }

    /// <summary>Общие настройки — шаблон для новых папок и кнопка «Применить ко всем».</summary>
    internal void OpenDefaults()
    {
        _panel?.Close();
        var dlg = new StyleWindow(Loc.T("Общие настройки папок"), Config.Defaults, _ => { },
            (Loc.T("Применить ко всем папкам"), s =>
            {
                Config.Defaults = s.Clone();
                Config.ApplyDefaultsToAll();
                foreach (var w in _tiles) Restyle(w);
                SaveConfig();
                Log("defaults applied to all");
                return null;
            }));
        if (dlg.ShowDialog() == true) { Config.Defaults = dlg.Result.Clone(); SaveConfig(); }
        RestyleWidgets();
    }

    /// <summary>Виджеты одеты по общим настройкам.</summary>
    internal void RestyleWidgets()
    {
        foreach (var g in _widgets.Values) g.ApplyLook();
    }


    private void Restyle(TileWindow w)
    {
        w.ApplyStyle();
        Place(w, Info(w), glass: true);
        if (_panel?.Folder == w.Folder) _panel.Rebuild();
    }

    internal void ToggleAutoName(TileWindow w)
    {
        var t = Info(w);
        t.AutoName = !t.AutoName;
        SaveConfig();
        if (t.AutoName) ItemsChanged(w.Folder);
    }

    internal void DeleteFolder(TileWindow w)
    {
        var ask = MessageBox.Show(Loc.T("Убрать папку «{0}»?\nВсе ярлыки из неё вернутся на рабочий стол.", w.Folder),
            Loc.T("Плитки"), MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (ask != MessageBoxResult.OK) return;
        Guard(() => Store.DeleteFolder(w.Folder));
        Sync();
    }

    internal void AddToFolder(string folder, IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (p.StartsWith(Store.FolderPath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            Guard(() => Store.MoveIn(p, folder), Path.GetFileName(p));
        }
        ItemsChanged(folder);
    }

    /// <summary>Содержимое папки поменялось: подобрать название и перерисовать.</summary>
    internal void ItemsChanged(string folder)
    {
        folder = ApplyAutoName(folder);
        foreach (var w in _tiles.Where(x => x.Folder == folder)) w.Refresh();
        if (_panel?.Folder == folder) _panel.Rebuild();
    }

    /// <summary>Группа FolderNamer (русская) → имя новой папки на языке программы.</summary>
    private static string GroupName(string group) => group switch
    {
        "Игры" => Loc.T("Игры"), // lang-ok
        "Музыка" => Loc.T("Музыка"), // lang-ok
        "Видео" => Loc.T("Видео"), // lang-ok
        "Графика" => Loc.T("Графика"), // lang-ok
        "Общение" => Loc.T("Общение"), // lang-ok
        "Браузеры" => Loc.T("Браузеры"), // lang-ok
        "Работа" => Loc.T("Работа"), // lang-ok
        "Разработка" => Loc.T("Разработка"), // lang-ok
        "Сеть" => Loc.T("Сеть"), // lang-ok
        "Утилиты" => Loc.T("Утилиты"), // lang-ok
        "Разное" => Loc.T("Разное"), // lang-ok
        _ => group,
    };

    /// <summary>Переименовывает папку по содержимому, если владелец не назвал её сам. Возвращает имя.</summary>
    private string ApplyAutoName(string folder)
    {
        var t = Config.Tiles.FirstOrDefault(x => x.Name == folder);
        if (t is not { AutoName: true }) return folder;
        var items = Store.Items(folder).Select(p => (Icons.DisplayName(p), Shortcuts.Target(p)));
        var group = FolderNamer.Suggest(items);
        if (group == null) return folder;
        var name = GroupName(group);
        // старое русское имя группы тоже «своё»: уже созданные папки не переименовываем
        if (folder == name || folder == group || Regex.IsMatch(folder, "^(" + Regex.Escape(name) + "|" + Regex.Escape(group) + @") \d+$")) return folder;
        try
        {
            var renamed = Store.RenameFolder(folder, name);
            t.Name = renamed;
            foreach (var w in _tiles.Where(x => x.Folder == folder)) w.Folder = renamed;
            SaveConfig();
            Log($"autoname {folder} -> {renamed}");
            return renamed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"autoname failed {folder}: {ex.Message}");
            return folder;
        }
    }

    internal void RefreshAll()
    {
        foreach (var w in _tiles) w.Refresh();
        _panel?.Rebuild();
    }
    // ---- панель ----

    internal void TogglePanel(TileWindow tile)
    {
        // клик по плитке, чья панель только что закрылась от этого же клика, — не открываем снова
        Log($"tile click {tile.Folder}");
        if (_lastPanelClose.Folder == tile.Folder && DateTime.Now - _lastPanelClose.At < TimeSpan.FromMilliseconds(350)) return;
        _panel?.Close();
        var panel = new PanelWindow(tile);
        _panel = panel;
        panel.Closed += (_, _) =>
        {
            Log($"panel closed {tile.Folder}");
            _lastPanelClose = (tile.Folder, DateTime.Now);
            if (_panel == panel) _panel = null;
        };
        panel.ShowNear(new RectD(tile.Left, tile.Top, tile.Width, tile.Height));
        Log($"panel shown {tile.Folder} at {panel.Left:0},{panel.Top:0} {panel.Width:0}x{panel.Height:0} active={panel.IsActive}");
    }

    // ---- служебное ----

    private void Guard(Action a, string? what = null)
    {
        try { a(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            string msg = ex is UnauthorizedAccessException
                ? Loc.T("Windows не даёт переместить «{0}»: ярлык общий для всех пользователей или открыт.", what)
                : Loc.T("Не получилось переместить «{0}»: {1}", what, ex.Message);
            _tray?.ShowBalloonTip(4000, Loc.T("Плитки"), msg, Forms.ToolTipIcon.Warning);
        }
    }

    private void Guard(Func<string> f, string what) => Guard(() => { f(); }, what);

    internal void SaveConfig() => ConfigStore.Save(_configPath, Config);

    /// <summary>Журнал рядом с настройками: Desktop-Плитки\tiles.log.</summary>
    internal void Log(string line)
    {
        try { File.AppendAllText(Path.Combine(Store.Root, "tiles.log"), $"{DateTime.Now:dd.MM HH:mm:ss.fff} {line}\n"); }
        catch { }
    }

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Loc.T("Открыть «Плитки»"), null, (_, _) => OpenHub("workshop"));
        menu.Items.Add(Loc.T("Мастерская виджетов"), null, (_, _) => OpenHub("workshop"));
        menu.Items.Add(Loc.T("Обновить"), null, (_, _) => RefreshEverything());
        menu.Items.Add(Loc.T("Новая папка"), null, (_, _) => NewFolder());
        menu.Items.Add(Loc.T("Показать / спрятать сетку"), null, (_, _) => ToggleGrid());
        menu.Items.Add(Loc.T("Редактировать сетку…"), null, (_, _) => EditGrid());
        var auto = new Forms.ToolStripMenuItem(Loc.T("Запускать с Windows")) { Checked = Config.Autostart, CheckOnClick = true };
        auto.CheckedChanged += (_, _) => { Config.Autostart = auto.Checked; SaveConfig(); ApplyAutostart(); };
        menu.Items.Add(auto);
        menu.Items.Add(Loc.T("Открыть папку с плитками"), null, (_, _) =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Store.Root) { UseShellExecute = true }));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Loc.T("Выход"), null, (_, _) => Shutdown());
        if (_tray != null)
        {
            // смена языка: меню у значка в трее новое, сам значок на месте
            var old = _tray.ContextMenuStrip;
            _tray.ContextMenuStrip = menu;
            _tray.Text = Loc.T("Плитки");
            old?.Dispose();
            return;
        }

        _tray = new Forms.NotifyIcon { Icon = AppIcon(), Text = Loc.T("Плитки"), ContextMenuStrip = menu, Visible = true };
        _tray.MouseClick += (_, a) => { if (a.Button == Forms.MouseButtons.Left) OpenHub("workshop"); };
    }

    private void RebuildTray() => CreateTray();

    private const string StartTask = "Плитки"; // lang-ok

    /// <summary>
    /// Автозапуск. Обычный «Run» Windows придерживает до двух минут после входа, а Плитки — это рабочий стол.
    /// Поэтому главный путь — задача планировщика «при входе» (её ставит установка с правами), а «Run» —
    /// запасной, только если задачи нет. Выключение — задача выключается, «Run» убирается.
    /// </summary>
    private void ApplyAutostart()
    {
        bool task = Schtasks($"/query /tn \"{StartTask}\"") == 0;
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (Config.Autostart && !task && Environment.ProcessPath is { } exe) key.SetValue(RunName, $"\"{exe}\"");
        else key.DeleteValue(RunName, false);
        if (task)
        {
            int rc = Schtasks($"/change /tn \"{StartTask}\" /{(Config.Autostart ? "enable" : "disable")}");
            Log($"autostart task {(Config.Autostart ? "on" : "off")} rc={rc}");
        }
    }

    private static int Schtasks(string args)
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("schtasks.exe", args)
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
            if (p == null) return -1;
            p.WaitForExit(5000);
            return p.HasExited ? p.ExitCode : -1;
        }
        catch { return -1; }
    }

    /// <summary>
    /// Сразу после входа Wallpaper Engine ещё не нарисовал обои, и стекло снимало пустой фон.
    /// Первые две минуты переснимаем обои несколько раз — стекло догоняет настоящую картинку.
    /// </summary>
    private void CatchUpGlass()
    {
        var marks = new[] { 3, 8, 15, 30, 60, 120 };
        int i = 0;
        var t = new System.Windows.Threading.DispatcherTimer();
        var start = DateTime.Now;
        t.Interval = TimeSpan.FromSeconds(marks[0]);
        t.Tick += (_, _) =>
        {
            RefreshGlass();
            Log($"glass catch-up {marks[i]}s");
            if (++i >= marks.Length) { t.Stop(); return; }
            t.Interval = start.AddSeconds(marks[i]) - DateTime.Now is { TotalMilliseconds: > 100 } d ? d : TimeSpan.FromMilliseconds(100);
        };
        t.Start();
    }

    /// <summary>Значок программы (он же в Пуске и на ярлыке); не вышло — нарисованный.</summary>
    private static Drawing.Icon AppIcon()
    {
        try { if (Environment.ProcessPath is { } exe && Drawing.Icon.ExtractAssociatedIcon(exe) is { } ic) return ic; } catch { }
        return MakeTrayIcon();
    }

    private static Drawing.Icon MakeTrayIcon()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var glass = new Drawing.SolidBrush(Drawing.Color.FromArgb(200, 255, 255, 255));
            using var dot = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 60, 140, 255));
            using var path = RoundRect(2, 2, 28, 28, 8);
            g.FillPath(glass, path);
            foreach (var (x, y) in new[] { (8, 8), (17, 8), (8, 17), (17, 17) })
                g.FillEllipse(dot, x, y, 7, 7);
        }
        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    private static Drawing.Drawing2D.GraphicsPath RoundRect(int x, int y, int w, int h, int r)
    {
        var p = new Drawing.Drawing2D.GraphicsPath();
        p.AddArc(x, y, r, r, 180, 90);
        p.AddArc(x + w - r, y, r, r, 270, 90);
        p.AddArc(x + w - r, y + h - r, r, r, 0, 90);
        p.AddArc(x, y + h - r, r, r, 90, 90);
        p.CloseFigure();
        return p;
    }
}
