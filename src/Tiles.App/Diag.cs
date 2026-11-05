using System.Windows.Media;
using System.Windows.Threading;

namespace Tiles.App;

/// <summary>Счётчики простоя: сколько сообщений окнам и кадров отрисовки за 30 секунд. МАРКЕР «idle».</summary>
internal static class Diag
{
    private static readonly Dictionary<int, int> Msgs = new();
    private static TimeSpan _cpu;
    private static DispatcherTimer? _timer;

    public static void Msg(int msg)
    {
        Msgs.TryGetValue(msg, out var n);
        Msgs[msg] = n + 1;
    }

    private static readonly Dictionary<string, int> Calls = new();

    /// <summary>Сколько раз за 30 секунд вызвали тяжёлое (стекло, раскладку, перестройку).</summary>
    public static void Count(string what)
    {
        Calls.TryGetValue(what, out var n);
        Calls[what] = n + 1;
    }

    public static void Start()
    {
        // кадры не считаем: подписка на кадры сама заставляет WPF рисовать без остановки
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += (_, _) =>
        {
            var top = string.Join(" ", Msgs.OrderByDescending(p => p.Value).Take(6).Select(p => $"0x{p.Key:X}={p.Value}"));
            var cpu = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime;
            var calls = string.Join(" ", Calls.Select(p => $"{p.Key}={p.Value}"));
            // в журнал — только когда в простое заметно грузим процессор (больше 1% одного ядра)
            if ((cpu - _cpu).TotalMilliseconds > 300 || Calls.Values.Sum() > 60) App.Current.Log($"idle 30s: cpu {(cpu - _cpu).TotalMilliseconds:0} ms calls {calls} msgs {top}");
            Calls.Clear();
            _cpu = cpu;
            Msgs.Clear();
        };
        _timer.Start();
    }
}
