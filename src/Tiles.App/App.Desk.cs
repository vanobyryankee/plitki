using System.Windows;
using System.Windows.Threading;
using Tiles.Core;

namespace Tiles.App;

/// <summary>
/// Рабочий стол целиком: папки и виджеты в одной раскладке, выделение (рамкой и Ctrl+клик),
/// перенос группой, соседи расступаются.
/// </summary>
public partial class App
{
    private readonly Dictionary<string, WidgetWindow> _widgets = new();
    private readonly HashSet<string> _selection = new();
    private List<(IDeskItem Item, Native.RECT Start)> _drag = new();
    private IDeskItem? _dragLead;
    private readonly DispatcherTimer _saveSoon = new() { Interval = TimeSpan.FromSeconds(1) };

    internal IEnumerable<IDeskItem> DeskItems() => _tiles.Cast<IDeskItem>().Concat(_widgets.Values);

    internal IEnumerable<WidgetWindow> WidgetWindows => _widgets.Values;

    internal void SaveConfigSoon()
    {
        if (_saveSoon.Tag == null) { _saveSoon.Tag = 1; _saveSoon.Tick += (_, _) => { _saveSoon.Stop(); SaveConfig(); }; }
        _saveSoon.Stop(); _saveSoon.Start();
    }

    // ---- расстановки ----

    internal event Action? PresetsChanged;

    /// <summary>Спросить имя и сохранить текущий стол как расстановку.</summary>
    internal void SavePresetAsk()
    {
        var name = Config.ActivePreset ?? Loc.T("Моя расстановка");
        var dlg = new RenameWindow(name, Loc.T("Название расстановки"));
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value)) return;
        Presets.Save(Config, dlg.Value);
        SaveConfig();
        Log($"preset saved {dlg.Value}");
        RebuildTray();
        PresetsChanged?.Invoke();
    }

    /// <summary>Поставить расстановку: места, размеры, вид, виджеты, сетка — всё как при сохранении.</summary>
    internal void ApplyPreset(string name)
    {
        if (Presets.Find(Config, name) is not { } p) return;
        bool binWas = Config.Widget("bin").Enabled;
        Presets.Apply(Config, p);
        ReadGridMetrics();
        foreach (var w in _widgets.Values.ToList()) { _selection.Remove(w.Id); w.View.Stop(); w.Close(); }
        _widgets.Clear();
        StartWidgets();
        foreach (var w in _tiles) Restyle(w);
        PlaceAll();
        bool binNow = Config.Widget("bin").Enabled;
        if (binNow != binWas) { try { RecycleBin.HideWindowsIcon(binNow); } catch { } }
        SaveConfig();
        Log($"preset applied {p.Name}");
        RebuildTray();
        WidgetsChanged?.Invoke();
        PresetsChanged?.Invoke();
        RefreshGlass();
    }

    internal void RenamePreset(string name)
    {
        if (Presets.Find(Config, name) is not { } p) return;
        var dlg = new RenameWindow(p.Name, Loc.T("Название расстановки"));
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value)) return;
        bool active = string.Equals(Config.ActivePreset, p.Name, StringComparison.CurrentCultureIgnoreCase);
        p.Name = dlg.Value.Trim();
        if (active) Config.ActivePreset = p.Name;
        SaveConfig();
        RebuildTray();
        PresetsChanged?.Invoke();
    }

    internal void DeletePreset(string name)
    {
        if (Presets.Find(Config, name) is not { } p) return;
        if (MessageBox.Show(Loc.T("Удалить расстановку «{0}»? Папки и виджеты останутся на местах.", p.Name), Loc.T("Плитки"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Config.Presets.Remove(p);
        if (string.Equals(Config.ActivePreset, p.Name, StringComparison.CurrentCultureIgnoreCase)) Config.ActivePreset = null;
        SaveConfig();
        RebuildTray();
        PresetsChanged?.Invoke();
    }

    // ---- смена экранов ----

    /// <summary>Расклад мониторов, под который расставлены плитки («дом»).</summary>
    private string _homeScreens = "";
    private System.Windows.Threading.DispatcherTimer? _adoptScreens;

    private string ScreensKey() => string.Join(";", Screens.Select(s => $"{s.X:0},{s.Y:0},{s.W:0}x{s.H:0}"));

    /// <summary>
    /// Игра на весь экран меняет разрешение — раньше плитки поджимались под него и так и сохранялись
    /// (слипались, наезжали друг на друга). Теперь: пока расклад экранов не «домашний», места не трогаем;
    /// вернулся домашний — встаём ровно туда, где были. Новый расклад держится 10 минут или нажали
    /// «Обновить» — значит, это надолго: принимаем его как домашний и расставляем заново.
    /// </summary>
    private void OnDisplayChanged()
    {
        ReadGridMetrics();
        var key = ScreensKey();
        _adoptScreens?.Stop();
        if (key == _homeScreens)
        {
            Log($"screens back home {key}");
            // решение владельца: вернулся домашний расклад — ставим последнюю выбранную расстановку
            if (Presets.Find(Config, Config.ActivePreset) is { } p) { ApplyPreset(p.Name); return; }
            PlaceAll();
            RefreshGlass();
            return;
        }
        if (ScreenLayout.IsShrunk(_homeScreens, key))
        {
            // игра уменьшила экран: это временно, сколько бы она ни шла, дом не меняем
            Log($"screens shrunk {key} (home {_homeScreens}) — game, places kept");
            return;
        }
        if (ScreenLayout.IsShrunk(key, _homeScreens))
        {
            // экран стал больше домашнего: дом был записан неверно, этот и есть настоящий
            Log($"screens grew {key} (home {_homeScreens})");
            AdoptScreens();
            if (Presets.Find(Config, Config.ActivePreset) is { } ap) ApplyPreset(ap.Name);
            return;
        }
        Log($"screens changed {key} (home {_homeScreens}) — places kept");
        _adoptScreens ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
        _adoptScreens.Tick -= AdoptTick;
        _adoptScreens.Tick += AdoptTick;
        _adoptScreens.Start();
    }

    private void AdoptTick(object? s, EventArgs e) { _adoptScreens?.Stop(); AdoptScreens(); }

    /// <summary>Текущий расклад экранов становится домашним: всё расставить под него.</summary>
    internal void AdoptScreens()
    {
        ReadGridMetrics();
        var now = ScreensKey();
        if (ScreenLayout.IsShrunk(_homeScreens, now))
        {
            // «Обновить» во время игры: дом не трогаем, только стекло
            Log($"screens adopt skipped {now} (home {_homeScreens}) — game");
            RefreshGlass();
            return;
        }
        _homeScreens = now;
        Config.HomeScreens = now;
        SaveConfigSoon();
        Log($"screens adopted {_homeScreens}");
        PlaceAll();
        foreach (var w in _widgets.Values) { var p = Fit(w.Rect, w.Snap); w.MoveTo(p.X, p.Y); }
        Untangle();
        RefreshGlass();
    }

    /// <summary>Папки и виджеты, вставшие друг на друга, расходятся по свободным клеткам рядом.</summary>
    internal void Untangle()
    {
        var placed = new List<RectD>();
        bool moved = false;
        foreach (var item in DeskItems().OrderBy(i => i.Locked ? 0 : 1).ToList())
        {
            var r = item.Rect;
            if (!item.Locked && placed.Any(p => Arrange.Overlaps(p, r)))
            {
                var c = CellAt(r);
                if (Arrange.FreeSpot(r, placed, WorkAreaAt(r), c.W, c.H) is { } spot)
                {
                    Log($"untangle {item.Id} {r.X:0},{r.Y:0} -> {spot.X:0},{spot.Y:0}");
                    item.MoveTo(spot.X, spot.Y);
                    r = item.Rect;
                    moved = true;
                }
            }
            placed.Add(r);
        }
        if (moved) SaveConfigSoon();
    }

    // ---- виджеты ----

    private void StartWidgets()
    {
        foreach (var w in Config.Widgets.Where(w => w.Enabled)) OpenWidget(w);
        if (_widgets.Count > 0) Stage.Ensure(); // сцена для плавного растягивания открыта заранее, пустая
    }

    internal void SetWidget(string kind, bool on)
    {
        var info = Config.Widget(kind);
        info.Enabled = on;
        if (on) OpenWidget(info);
        else if (_widgets.Remove(kind, out var win))
        {
            _selection.Remove(win.Id);
            win.View.Stop();
            win.Close();
        }
        if (kind == "bin") { try { RecycleBin.HideWindowsIcon(on); } catch (Exception ex) { Log("bin icon toggle failed: " + ex.Message); } }
        SaveConfig();
        Log($"widget {kind} {(on ? "on" : "off")}");
        WidgetsChanged?.Invoke();
    }

    internal event Action? WidgetsChanged;

    private void OpenWidget(WidgetInfo info)
    {
        if (_widgets.ContainsKey(info.Kind)) return;
        var view = WidgetView.Create(info);
        var win = new WidgetWindow(view);
        _widgets[info.Kind] = win;
        if (double.IsNaN(info.X) || double.IsNaN(info.Y))
        {
            // первый раз: музыка — в левый нижний угол, остальные — в свободное место рядом
            var b = win.WindowBase;
            var want = info.Kind == "music"
                ? Layout.BottomLeft(WorkArea, b.Width, b.Height, 4)
                : new RectD(WorkArea.Right - b.Width - CellW * 3, WorkArea.Y + CellH, b.Width, b.Height);
            var spot = Arrange.FreeSpot(want, DeskItems().Where(i => i != win).Select(i => i.Rect).ToList(), WorkArea, CellW, CellH);
            var taken = DeskItems().Where(i => i != win).Any(i => Arrange.Overlaps(i.Rect, want));
            var p = taken && spot is { } s ? s : want;
            info.X = p.X; info.Y = p.Y;
        }
        var pos = Fit(win.Rect, win.Snap);
        win.MoveTo(pos.X, pos.Y);
        if (view.InitiallyVisible) { win.Show(); win.ApplyLook(); }
        view.Start();
        if (info.Kind == "bin") { try { RecycleBin.HideWindowsIcon(true); } catch { } }
        ScheduleIconCheck();
    }

    /// <summary>Привязка к клеткам (если включена) и в пределах экрана.</summary>
    private PointD Fit(RectD r, bool snap)
        => snap ? Layout.Snap(r.X, r.Y, CellAt(r).W, CellAt(r).H, r.W, r.H, WorkAreaAt(r)) : Layout.Clamp(r.X, r.Y, r.W, r.H, WorkAreaAt(r));

    /// <summary>Виджет растянули или переключили привязку: выровнять и раздвинуть соседей (плавно).</summary>
    internal void SettleWidget(WidgetWindow w)
    {
        var r = w.Rect;
        var p = Fit(r, w.Snap);
        w.Remember(p.X, p.Y);
        Settle(new Dictionary<string, RectD> { [w.Id] = r with { X = p.X, Y = p.Y } });
        Log($"widget {w.Info.Kind} at {w.Info.X:0},{w.Info.Y:0} size {r.W:0}x{r.H:0}");
    }

    /// <summary>Поставили (moved) — раздвинуть мешающих, сохранить.</summary>
    internal void Settle(Dictionary<string, RectD> moved)
    {
        var items = DeskItems().ToDictionary(i => i.Id);
        var before = items.ToDictionary(p => p.Key, p => p.Value.Rect);
        var locked = items.Values.Where(i => i.Locked).Select(i => i.Id).ToHashSet();
        var cell = CellAt(moved.Values.First());
        var after = Arrange.Push(before, moved, WorkAreaAt(moved.Values.First()), cell.W, cell.H, locked); // раздвигаем на том экране, куда поставили
        foreach (var (id, r) in after)
        {
            bool isMoved = moved.ContainsKey(id);
            if (!isMoved && r == before[id]) continue;
            Fly(items[id], r, slow: !isMoved); // соседи уплывают мягче и дольше
            if (!isMoved) Log($"pushed {id} -> {r.X:0},{r.Y:0}");
        }
        SaveConfig();
        ScheduleIconCheck();
    }

    // ---- плавный полёт на новое место (и в новый размер), как значки на телефоне ----

    private readonly List<(IDeskItem Item, RectD From, RectD To, double Start, double Ms)> _flights = new();
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew(); // точный, без шагов по 15 мс
    private bool _flying;

    /// <summary>Место запоминаем сразу, окно доплывает до него; в конце — точная постановка и стекло.</summary>
    private void Fly(IDeskItem item, RectD to, bool slow = false)
    {
        item.Remember(to.X, to.Y);
        double k = DpiScale;
        var w = Native.WindowRect(item.Window);
        var from = item is WidgetWindow { ShownRect: { } shown } ? shown // растягивали: окно-холст больше виджета
            : new RectD(w.Left / k, w.Top / k, (w.Right - w.Left) / k, (w.Bottom - w.Top) / k);
        _flights.RemoveAll(f => f.Item == item);
        if (Math.Abs(from.X - to.X) < 1 && Math.Abs(from.Y - to.Y) < 1 && Math.Abs(from.W - to.W) < 1 && Math.Abs(from.H - to.H) < 1)
        { item.MoveTo(to.X, to.Y); return; }
        _flights.Add((item, from, to, Clock.Elapsed.TotalMilliseconds, slow ? 560 : 380));
        if (!_flying)
        {
            // кадры только пока кто-то летит — синхронно с экраном, потом подписка снимается
            _flying = true;
            System.Windows.Media.CompositionTarget.Rendering += FlyStep;
        }
    }

    private double _renderShift = double.NaN;

    private void FlyStep(object? sender, EventArgs e)
    {
        // время кадра, а не «сейчас»: все окна в кадре в одной фазе, шаги ровные (часы кадров сводим с нашими на первом кадре)
        double now = Clock.Elapsed.TotalMilliseconds;
        if (e is System.Windows.Media.RenderingEventArgs re)
        {
            if (double.IsNaN(_renderShift)) _renderShift = re.RenderingTime.TotalMilliseconds - now;
            now = re.RenderingTime.TotalMilliseconds - _renderShift;
        }
        foreach (var f in _flights.ToList())
        {
            double t = Math.Clamp((now - f.Start) / f.Ms, 0, 1);
            double q = 1 - Math.Pow(1 - t, 3); // мягкое торможение, как значки на телефоне
            q = q * 0.65 + q * q * (3 - 2 * q) * 0.35;
            if (t >= 1)
            {
                _flights.Remove(f);
                f.Item.MoveTo(f.To.X, f.To.Y);
                continue;
            }
            f.Item.Preview(new RectD(f.From.X + (f.To.X - f.From.X) * q, f.From.Y + (f.To.Y - f.From.Y) * q,
                f.From.W + (f.To.W - f.From.W) * q, f.From.H + (f.To.H - f.From.H) * q));
        }
        if (_flights.Count == 0)
        {
            _flying = false;
            System.Windows.Media.CompositionTarget.Rendering -= FlyStep;
        }
    }
    // ---- выделение ----

    internal void ToggleSelect(IDeskItem item)
    {
        if (!_selection.Remove(item.Id)) _selection.Add(item.Id);
        item.Selected = _selection.Contains(item.Id);
    }

    internal void ClearSelection()
    {
        if (_selection.Count == 0) return;
        _selection.Clear();
        foreach (var i in DeskItems()) i.Selected = false;
    }

    /// <summary>Рамка выделения на рабочем столе (точки экрана): подсветить тех, кого задела.</summary>
    internal void MarqueeSelect(RectD marquee, bool add, IReadOnlySet<string>? keep)
    {
        var items = DeskItems().ToDictionary(i => i.Id, i => i.Rect);
        var hit = Arrange.InMarquee(items, marquee).ToHashSet();
        if (add && keep != null) hit.UnionWith(keep);
        _selection.Clear();
        _selection.UnionWith(hit);
        foreach (var i in DeskItems()) { bool s = hit.Contains(i.Id); if (i.Selected != s) i.Selected = s; }
    }

    internal IReadOnlySet<string> Selection => _selection;

    // ---- перенос группой ----

    internal void BeginDrag(IDeskItem lead, bool ctrl)
    {
        if (!_selection.Contains(lead.Id) && !ctrl) ClearSelection();
        var group = _selection.Contains(lead.Id) ? DeskItems().Where(i => _selection.Contains(i.Id)).ToList() : new List<IDeskItem> { lead };
        group.RemoveAll(i => i.Locked); // закреплённые стоят на месте
        if (lead.Locked) { _drag.Clear(); _dragLead = null; return; }
        _drag = group.Select(i => (i, Native.WindowRect(i.Window))).ToList();
        _dragLead = lead;
        _panel?.Close();
    }

    internal void CancelDrag() { _drag.Clear(); _dragLead = null; }

    /// <summary>Двигаем окна напрямую, в обход WPF — плавно; группа упирается в край целиком.</summary>
    internal void DragGroup(double dxPx, double dyPx)
    {
        if (_drag.Count == 0) return;
        double k = DpiScale;
        var rects = _drag.Select(d => new RectD(d.Start.Left / k, d.Start.Top / k, (d.Start.Right - d.Start.Left) / k, (d.Start.Bottom - d.Start.Top) / k));
        var (dx, dy) = Arrange.ClampGroup(rects, dxPx / k, dyPx / k, AllScreens); // можно на соседний монитор
        if (_dragLead is { Snap: false, Align: true } lead)
        {
            // без сетки: ведущий прилипает к краям и серединам соседей, видны красные линии
            var start = _drag.First(d => d.Item == lead).Start;
            var r = new RectD(start.Left / k + dx, start.Top / k + dy, (start.Right - start.Left) / k, (start.Bottom - start.Top) / k);
            var group = _drag.Select(d => d.Item).ToHashSet();
            var (g, lines) = Guides.Move(r, DeskItems().Where(i => !group.Contains(i)).Select(i => i.Rect).ToList(), 8);
            dx += g.X - r.X; dy += g.Y - r.Y;
            GuideOverlay.Show(lines);
        }
        foreach (var (item, start) in _drag)
        {
            Native.MoveTo(item.Window, start.Left + (int)Math.Round(dx * k), start.Top + (int)Math.Round(dy * k));
            item.FollowGlass(); // стекло как линза: показывает, что под ним сейчас
        }
    }

    internal void EndDrag()
    {
        GuideOverlay.Hide();
        if (_drag.Count == 0 || _dragLead == null) return;
        double k = DpiScale;
        // ведущий выравнивается по сетке, остальные едут на тот же сдвиг — форма группы не ломается
        var leadNow = Native.WindowRect(_dragLead.Window);
        var leadRect = _dragLead.Rect with { X = leadNow.Left / k, Y = leadNow.Top / k };
        var fit = Fit(leadRect, _dragLead.Snap);
        double ax = fit.X - leadRect.X, ay = fit.Y - leadRect.Y;
        var moved = new Dictionary<string, RectD>();
        foreach (var (item, _) in _drag)
        {
            var now = Native.WindowRect(item.Window);
            var r = item.Rect with { X = now.Left / k + ax, Y = now.Top / k + ay };
            var p = Layout.Clamp(r.X, r.Y, r.W, r.H, WorkAreaAt(r));
            item.Window.Left = now.Left / k; item.Window.Top = now.Top / k; // окно двигали в обход WPF — сверяем с тем, где оно сейчас, и оттуда доплывает до клетки
            moved[item.Id] = r with { X = p.X, Y = p.Y };
        }
        Log($"drop {string.Join(",", moved.Keys)}");
        CancelDrag();
        Settle(moved);
    }
}
