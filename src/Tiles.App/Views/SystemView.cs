using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Tiles.Core;

namespace Tiles.App;

/// <summary>Нагрузка ПК: процессор, память, видеокарта кольцами. Раз в 2 секунды (в экономном — раз в 5).</summary>
internal sealed class SystemView : WidgetView
{
    private readonly Ring _cpu = new(Loc.T("Процессор")), _ram = new(Loc.T("Память")), _gpu = new(Loc.T("Видеокарта"));
    private readonly DispatcherTimer _tick = new();
    private ulong _idle0, _kernel0, _user0;
    private IntPtr _query, _gpuCounter, _vramCounter;

    public SystemView(WidgetInfo info) : base(info)
    {
        _tick.Tick += (_, _) => Sample();
        _cpu.Detail = Loc.T("{0} потоков", Environment.ProcessorCount);
        ApplyOptions();
    }

    public override IReadOnlyList<(string, Func<bool>, Action<bool>)> Options => new[]
    {
        Opt(Loc.T("Процессор"), "cpu"), Opt(Loc.T("Память"), "ram"), Opt(Loc.T("Видеокарта"), "gpu"),
        Opt(Loc.T("Подробно: гигабайты, потоки, видеопамять"), "detail", false),
    };

    private Ring[] Shown => new[] { (_cpu, "cpu"), (_ram, "ram"), (_gpu, "gpu") }.Where(p => Flag(p.Item2)).Select(p => p.Item1).ToArray();

    public override void ApplyOptions()
    {
        foreach (var r in new[] { _cpu, _ram, _gpu }) r.ShowDetail = Flag("detail", false);
        Relayout();
    }

    protected override void OnShape(WidgetShape s) => Relayout();

    /// <summary>Кольца в ряд (длинный), столбиком (высокий) или по два в строке (квадрат).</summary>
    private void Relayout()
    {
        foreach (var r in new[] { _cpu, _ram, _gpu }) (r.Parent as Panel)?.Children.Remove(r);
        Children.Clear();
        var rings = Shown;
        int perRow = Shape switch { WidgetShape.Wide => Math.Max(1, rings.Length), WidgetShape.Tall => 1, _ => 2 };
        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(8, 10, 8, 8) };
        for (int i = 0; i < rings.Length; i += perRow)
        {
            var row = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = Math.Min(perRow, rings.Length - i), Margin = new Thickness(0, i == 0 ? 0 : 12, 0, 0) };
            if (perRow == 2 && rings.Length - i == 1) row.Width = 100; // одно кольцо в последней строке — по центру
            foreach (var r in rings.Skip(i).Take(perRow)) row.Children.Add(r);
            col.Children.Add(row);
        }
        Children.Add(col);
    }

    public override string Title => Loc.T("Нагрузка ПК");
    public override string Description => Loc.T("Процессор, память и видеокарта кольцами. Растяни вверх — встанут столбиком, квадратом — по два.");
    public override Size BaseSize => BaseFor(WidgetShape.Wide);

    public override Size BaseFor(WidgetShape s)
    {
        int n = Math.Max(1, Shown.Length);
        double ring = Flag("detail", false) ? 124 : 104; // кольцо с подписью (и строкой подробностей)
        return s switch
        {
            WidgetShape.Wide => new(Math.Max(110, n * 100), ring + 20),
            WidgetShape.Tall => new(120, n * ring + (n - 1) * 12 + 20),
            _ => new(220, Math.Ceiling(n / 2.0) * ring + (n > 2 ? 12 : 0) + 22),
        };
    }

    public override void Demo() { _cpu.Set(23); _ram.Set(41); _gpu.Set(9); _ram.Detail = Loc.T("{0:0.0} из {1:0.0} ГБ", 13.1, 31.9); _gpu.Detail = Loc.T("видео {0:0.0} ГБ", 2.4); }

    protected override void OnStart()
    {
        GetSystemTimes(out _idle0, out _kernel0, out _user0);
        try
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out _query) == 0)
            {
                PdhAddEnglishCounter(_query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out _gpuCounter);
                PdhAddEnglishCounter(_query, @"\GPU Adapter Memory(*)\Dedicated Usage", IntPtr.Zero, out _vramCounter);
            }
            PdhCollectQueryData(_query);
        }
        catch { _query = IntPtr.Zero; }
        _tick.Interval = TimeSpan.FromSeconds(App.Current.Config.EcoMode ? 5 : 2);
        _tick.Start();
        Sample();
    }

    protected override void OnStop()
    {
        _tick.Stop();
        if (_query != IntPtr.Zero) { PdhCloseQuery(_query); _query = IntPtr.Zero; }
    }

    private void Sample()
    {
        _tick.Interval = TimeSpan.FromSeconds(App.Current.Config.EcoMode ? 5 : 2);
        // процессор: доля не-простоя за промежуток
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            ulong di = idle - _idle0, dk = kernel - _kernel0, du = user - _user0, total = dk + du;
            if (total > 0) _cpu.Set(100.0 * (total - di) / total);
            _idle0 = idle; _kernel0 = kernel; _user0 = user;
        }
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref mem))
        {
            _ram.Set(mem.dwMemoryLoad);
            const double Gb = 1024.0 * 1024 * 1024;
            _ram.Detail = Loc.T("{0:0.0} из {1:0.0} ГБ", (mem.ullTotalPhys - mem.ullAvailPhys) / Gb, mem.ullTotalPhys / Gb);
        }
        if (_query != IntPtr.Zero && PdhCollectQueryData(_query) == 0)
        {
            _gpu.Set(Sum(_gpuCounter, 100));
            if (_vramCounter != IntPtr.Zero) _gpu.Detail = Loc.T("видео {0:0.0} ГБ", Sum(_vramCounter, double.MaxValue) / (1024.0 * 1024 * 1024));
        }
    }

    /// <summary>Сумма счётчика по всем экземплярам (движки 3D, память адаптеров).</summary>
    private static double Sum(IntPtr counter, double max)
    {
        if (counter == IntPtr.Zero) return 0;
        uint size = 0;
        PdhGetFormattedCounterArray(counter, 0x200, ref size, out uint count, IntPtr.Zero); // PDH_FMT_DOUBLE
        if (size == 0) return 0;
        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(counter, 0x200, ref size, out count, buf) != 0) return 0;
            double sum = 0;
            int item = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM>();
            for (int i = 0; i < count; i++)
                sum += Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM>(buf + i * item).Value.doubleValue;
            return Math.Clamp(sum, 0, max);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    /// <summary>Кольцо с процентом в середине и подписью снизу.</summary>
    private sealed class Ring : StackPanel
    {
        private const double D = 64, T = 6;
        private readonly Path _arc = new() { Stroke = Brushes.White, StrokeThickness = T, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        private readonly TextBlock _value = Text(17, FontWeights.SemiBold);
        private readonly TextBlock _detail = Text(11, FontWeights.Medium, Soft);
        private double _shown = -1;

        public string Detail { set { if (_detail.Text != value) _detail.Text = value; } }
        public bool ShowDetail { set => _detail.Visibility = value ? Visibility.Visible : Visibility.Collapsed; }

        public Ring(string caption)
        {
            var track = new Ellipse { Width = D, Height = D, Stroke = Faint, StrokeThickness = T };
            _value.HorizontalAlignment = HorizontalAlignment.Center;
            _value.VerticalAlignment = VerticalAlignment.Center;
            var g = new Grid { Width = D, Height = D, Children = { track, _arc, _value } };
            var label = Text(12, FontWeights.SemiBold, Soft);
            label.Text = caption;
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.Margin = new Thickness(0, 7, 0, 0);
            _detail.HorizontalAlignment = HorizontalAlignment.Center;
            _detail.Margin = new Thickness(0, 2, 0, 0);
            Children.Add(g);
            Children.Add(label);
            Children.Add(_detail);
        }

        public void Set(double pct)
        {
            pct = Math.Clamp(pct, 0, 100);
            if (Math.Abs(pct - _shown) < 0.5) return; // ничего не перерисовываем зря
            _shown = pct;
            _value.Text = $"{pct:0}%";
            double r = (D - T) / 2, c = D / 2, a = Math.Min(pct, 99.9) / 100 * 2 * Math.PI;
            var start = new Point(c, c - r);
            var end = new Point(c + r * Math.Sin(a), c - r * Math.Cos(a));
            var fig = new PathFigure { StartPoint = start, IsClosed = false };
            fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true));
            var geo = new PathGeometry(new[] { fig });
            geo.Freeze();
            _arc.Data = pct <= 0.5 ? null : geo;
        }
    }


    // ---- Windows ----

    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE { public uint CStatus; public double doubleValue; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE_ITEM { public IntPtr szName; public PDH_FMT_COUNTERVALUE Value; }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string? src, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, out uint count, IntPtr items);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
}
