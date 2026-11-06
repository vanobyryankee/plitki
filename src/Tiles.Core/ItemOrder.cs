namespace Tiles.Core;

/// <summary>
/// Свой порядок ярлыков в папке: список имён файлов. Кто в списке — идут первыми по нему,
/// остальные (новые, переименованные) — следом по алфавиту. Пустой список — просто по алфавиту.
/// </summary>
public static class ItemOrder
{
    public static IReadOnlyList<string> Sort(IEnumerable<string> paths, IList<string>? order)
    {
        var alpha = paths.OrderBy(p => Path.GetFileName(p), StringComparer.CurrentCultureIgnoreCase).ToList();
        if (order == null || order.Count == 0) return alpha;
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < order.Count; i++) rank.TryAdd(order[i], i);
        return alpha
            .Select((p, i) => (p, i, r: rank.TryGetValue(Path.GetFileName(p), out var r) ? r : int.MaxValue))
            .OrderBy(x => x.r).ThenBy(x => x.i)
            .Select(x => x.p).ToList();
    }

    /// <summary>Переставить элемент с места from на место to (to зажимается в границы).</summary>
    public static string[] Move(IReadOnlyList<string> list, int from, int to)
    {
        var l = list.ToList();
        if (from < 0 || from >= l.Count) return l.ToArray();
        var x = l[from];
        l.RemoveAt(from);
        l.Insert(Math.Clamp(to, 0, l.Count), x);
        return l.ToArray();
    }

    /// <summary>Номер клетки сетки под точкой (x, y), зажатый в 0..count-1.</summary>
    public static int SlotAt(double x, double y, double itemW, double itemH, int cols, int count)
    {
        if (count <= 0) return 0;
        cols = Math.Max(1, cols);
        int c = Math.Clamp((int)Math.Floor(x / itemW), 0, cols - 1);
        int r = Math.Max(0, (int)Math.Floor(y / itemH));
        return Math.Clamp(r * cols + c, 0, count - 1);
    }
}
