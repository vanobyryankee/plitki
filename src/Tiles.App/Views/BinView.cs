using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Tiles.Core;

namespace Tiles.App;

/// <summary>Корзина размером с ярлык: свой значок (пустая / полная), клик открывает, файлы на неё бросать.</summary>
internal sealed class BinView : WidgetView
{
    private static readonly Brush Fullness = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
    private readonly Border _icon = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly List<FileSystemWatcher> _watchers = new();
    private long _count = -1;

    public BinView(WidgetInfo info) : base(info)
    {
        Children.Add(_icon);
        _icon.Child = Glyphs.Make(Glyphs.BinEmpty, 40, 1.6);
        _debounce.Tick += (_, _) => { _debounce.Stop(); Refresh(); };
        _poll.Tick += (_, _) => Refresh();
    }

    public override string Title => Loc.T("Корзина");
    public override string Description => Loc.T("Свой значок вместо корзины Windows: клик открывает, файлы бросать прямо на неё.");
    public override string? Label => Loc.T("Корзина");
    public override Size BaseSize
    {
        get
        {
            double w = App.Current.CellW - 2 * WidgetWindow.Pad, h = App.Current.CellH - 2 * WidgetWindow.Pad - WidgetWindow.LabelH;
            return new Size(Math.Max(48, w), Math.Max(48, h));
        }
    }

    public override IEnumerable<object> MenuItems() => new object[]
    {
        WidgetWindow.Item(Loc.T("Открыть корзину"), RecycleBin.Open),
        WidgetWindow.Item(Loc.T("Очистить корзину"), () =>
        {
            var w = Window.GetWindow(this);
            RecycleBin.Empty(w != null ? new WindowInteropHelper(w).Handle : IntPtr.Zero);
            Refresh();
        }),
    };

    public override void Click() => RecycleBin.Open();

    public bool AcceptsFiles => true;

    public void DropFiles(string[] paths)
    {
        var w = Window.GetWindow(this);
        bool ok = RecycleBin.Delete(w != null ? new WindowInteropHelper(w).Handle : IntPtr.Zero, paths);
        App.Current.Log($"bin drop {paths.Length} ok={ok}");
        Refresh();
        App.Current.RefreshAll();
    }

    public override void Demo() => _icon.Child = Glyphs.Make(Glyphs.BinFull, 40, 1.6, Fullness);

    protected override void OnStart()
    {
        foreach (var dir in RecycleBin.Folders())
        {
            try
            {
                var fw = new FileSystemWatcher(dir) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName, EnableRaisingEvents = true };
                FileSystemEventHandler h = (_, _) => Dispatcher.BeginInvoke(() => { _debounce.Stop(); _debounce.Start(); });
                fw.Created += h; fw.Deleted += h;
                _watchers.Add(fw);
            }
            catch (Exception ex) { App.Current.Log($"bin watch {dir}: {ex.Message}"); }
        }
        _poll.Start();
        Refresh();
    }

    protected override void OnStop()
    {
        _poll.Stop();
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
    }

    public void Refresh()
    {
        var (count, bytes) = RecycleBin.Query();
        ToolTip = count > 0 ? Loc.T("Корзина: {0}, {1}", Bin.Caption(count), Size(bytes)) : Loc.T("Корзина пуста");
        bool changed = _count < 0 || (count > 0) != (_count > 0);
        _count = count;
        if (!changed) return;
        _icon.Child = count > 0 ? Glyphs.Make(Glyphs.BinFull, 40, 1.6, Fullness) : Glyphs.Make(Glyphs.BinEmpty, 40, 1.6);
        App.Current.Log($"bin {Bin.Caption(count)}");
    }

    private static string Size(long b) => b switch
    {
        < 1024 * 1024 => Loc.T("{0} КБ", Math.Max(1, b / 1024)),
        < 1024L * 1024 * 1024 => Loc.T("{0:0.#} МБ", b / 1024.0 / 1024),
        _ => Loc.T("{0:0.#} ГБ", b / 1024.0 / 1024 / 1024),
    };
}
