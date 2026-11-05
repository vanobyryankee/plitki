namespace Tiles.Core;

/// <summary>
/// Раскладка всего, что стоит на столе (папки и виджеты): никто ни на кого не наезжает,
/// отпущенное остаётся где отпустили, мешающие соседи расступаются в ближайшее свободное место.
/// </summary>
public static class Arrange
{
    private const double Tol = 0.5;

    public static bool Overlaps(RectD a, RectD b)
        => a.X < b.Right - Tol && b.X < a.Right - Tol && a.Y < b.Bottom - Tol && b.Y < a.Bottom - Tol;

    /// <summary>
    /// Поставить перемещённые (moved) на новые места и раздвинуть тех, кому они мешают.
    /// Возвращает новые прямоугольники всех.
    /// </summary>
    public static Dictionary<string, RectD> Push(IReadOnlyDictionary<string, RectD> items,
        IReadOnlyDictionary<string, RectD> moved, RectD work, double stepX, double stepY, IReadOnlySet<string>? locked = null)
    {
        var r = items.ToDictionary(p => p.Key, p => p.Value);
        foreach (var (id, rect) in moved) r[id] = rect;
        var settled = new HashSet<string>(moved.Keys);
        if (locked != null)
        {
            // закреплённых не сдвигаем: это поставленный сам отходит от них
            settled.UnionWith(locked.Where(r.ContainsKey));
            foreach (var id in moved.Keys.Where(m => !locked.Contains(m)))
                if (locked.Any(l => r.ContainsKey(l) && Overlaps(r[id], r[l])))
                    r[id] = FreeSpot(r[id], r.Where(p => p.Key != id).Select(p => p.Value).ToList(), work, stepX, stepY) ?? r[id];
        }

        for (int guard = 0; guard < 200; guard++)
        {
            // ближайший к уже поставленным мешающий сосед
            var victim = r.Where(p => !settled.Contains(p.Key) && settled.Any(s => Overlaps(p.Value, r[s])))
                .Select(p => p.Key).FirstOrDefault();
            if (victim == null) break;
            var others = r.Where(p => p.Key != victim).Select(p => p.Value).ToList();
            r[victim] = FreeSpot(r[victim], others, work, stepX, stepY) ?? r[victim];
            settled.Add(victim);
        }
        return r;
    }

    /// <summary>Ближайшее к want место того же размера, никого не задевающее, внутри рабочей области.</summary>
    public static RectD? FreeSpot(RectD want, IReadOnlyList<RectD> taken, RectD work, double stepX, double stepY)
    {
        want = Fit(want, work);
        int maxRing = (int)Math.Ceiling(Math.Max(work.W / stepX, work.H / stepY)) + 1;
        RectD? best = null;
        double bestDist = double.MaxValue;
        int foundRing = -1;
        for (int ring = 1; ring <= maxRing; ring++)
        {
            if (foundRing >= 0 && ring > foundRing + 1) break; // ещё одно кольцо: по прямой может быть ближе
            for (int i = -ring; i <= ring; i++)
                for (int j = -ring; j <= ring; j++)
                {
                    if (Math.Max(Math.Abs(i), Math.Abs(j)) != ring) continue;
                    var c = want with { X = want.X + i * stepX, Y = want.Y + j * stepY };
                    if (c.X < work.X - Tol || c.Y < work.Y - Tol || c.Right > work.Right + Tol || c.Bottom > work.Bottom + Tol) continue;
                    if (taken.Any(t => Overlaps(c, t))) continue;
                    double d = Math.Sqrt(i * stepX * i * stepX + j * stepY * j * stepY);
                    if (d < bestDist - 1e-6) { bestDist = d; best = c; if (foundRing < 0) foundRing = ring; }
                }
        }
        return best;
    }

    private static RectD Fit(RectD r, RectD work) => r with
    {
        X = Math.Clamp(r.X, work.X, Math.Max(work.X, work.Right - r.W)),
        Y = Math.Clamp(r.Y, work.Y, Math.Max(work.Y, work.Bottom - r.H)),
    };

    /// <summary>Сдвиг группы, урезанный так, чтобы вся группа осталась на экране (форма не ломается).</summary>
    public static (double Dx, double Dy) ClampGroup(IEnumerable<RectD> group, double dx, double dy, RectD work)
    {
        var g = group.ToList();
        if (g.Count == 0) return (dx, dy);
        double l = g.Min(x => x.X), t = g.Min(x => x.Y), rr = g.Max(x => x.Right), b = g.Max(x => x.Bottom);
        dx = Math.Clamp(dx, Math.Min(0, work.X - l), Math.Max(0, work.Right - rr));
        dy = Math.Clamp(dy, Math.Min(0, work.Y - t), Math.Max(0, work.Bottom - b));
        return (dx, dy);
    }

    /// <summary>Кто попал под рамку выделения.</summary>
    public static IEnumerable<string> InMarquee(IReadOnlyDictionary<string, RectD> items, RectD marquee)
        => items.Where(p => Overlaps(p.Value, marquee)).Select(p => p.Key);
}

/// <summary>
/// Растягивание виджета: ширина и высота каждая своя (с привязкой — кратно клеткам), содержимое
/// масштабируется целиком и вписывается, стекло заполняет всё окно. Так виджеты подгоняются друг под друга.
/// </summary>
public static class WidgetSizing
{
    public const double MinScale = 0.6, MaxScale = 3;

    public static RectD Resize(RectD rect, double dx, double dy, Edge edges, bool snap, double cellW, double cellH,
        double minW, double minH)
    {
        double w = rect.W, h = rect.H;
        if (edges.HasFlag(Edge.Right)) w = rect.W + dx;
        if (edges.HasFlag(Edge.Left)) w = rect.W - dx;
        if (edges.HasFlag(Edge.Bottom)) h = rect.H + dy;
        if (edges.HasFlag(Edge.Top)) h = rect.H - dy;
        if (snap && cellW > 0 && cellH > 0)
        {
            if (edges.HasFlag(Edge.Left) || edges.HasFlag(Edge.Right)) w = Math.Max(1, Math.Round(w / cellW)) * cellW;
            if (edges.HasFlag(Edge.Top) || edges.HasFlag(Edge.Bottom)) h = Math.Max(1, Math.Round(h / cellH)) * cellH;
        }
        w = Math.Max(minW, w);
        h = Math.Max(minH, h);
        double x = edges.HasFlag(Edge.Left) ? rect.Right - w : rect.X;
        double y = edges.HasFlag(Edge.Top) ? rect.Bottom - h : rect.Y;
        return new RectD(x, y, w, h);
    }

    /// <summary>Во сколько раз увеличить содержимое, чтобы оно целиком вписалось в окно.</summary>
    public static double ContentScale(double baseW, double baseH, double w, double h)
        => Math.Clamp(Math.Min(w / baseW, h / baseH), MinScale, MaxScale);
}
/// <summary>Красная направляющая: вертикальная (At — x) или горизонтальная (At — y), от From до To.</summary>
public readonly record struct GuideLine(bool Vertical, double At, double From, double To);

/// <summary>
/// Выравнивание по соседям без сетки: края и середины прилипают к краям и серединам других плиток,
/// если до них меньше порога, и рисуется линия, к кому прилипло.
/// </summary>
public static class Guides
{
    public static (RectD Rect, List<GuideLine> Lines) Move(RectD r, IReadOnlyList<RectD> others, double threshold)
    {
        var lines = new List<GuideLine>();
        var bx = Best(new[] { r.X, r.X + r.W / 2, r.Right }, others, o => new[] { o.X, o.X + o.W / 2, o.Right }, threshold);
        if (bx is { } x) r = r with { X = r.X + x.Diff };
        var by = Best(new[] { r.Y, r.Y + r.H / 2, r.Bottom }, others, o => new[] { o.Y, o.Y + o.H / 2, o.Bottom }, threshold);
        if (by is { } y) r = r with { Y = r.Y + y.Diff };
        if (bx is { } lx) lines.Add(new GuideLine(true, lx.Target, Math.Min(r.Y, lx.Other.Y), Math.Max(r.Bottom, lx.Other.Bottom)));
        if (by is { } ly) lines.Add(new GuideLine(false, ly.Target, Math.Min(r.X, ly.Other.X), Math.Max(r.Right, ly.Other.Right)));
        return (r, lines);
    }

    /// <summary>Растягивание: прилипает только тот край, который тянут.</summary>
    public static (RectD Rect, List<GuideLine> Lines) Resize(RectD r, Edge edges, IReadOnlyList<RectD> others, double threshold)
    {
        var lines = new List<GuideLine>();
        double l = r.X, t = r.Y, rr = r.Right, b = r.Bottom;
        RectD? ox = null, oy = null; double gx = 0, gy = 0;
        Func<RectD, double[]> xs = o => new[] { o.X, o.Right }, ys = o => new[] { o.Y, o.Bottom };
        if (edges.HasFlag(Edge.Left) && Best(new[] { l }, others, xs, threshold) is { } a) { l += a.Diff; ox = a.Other; gx = a.Target; }
        if (edges.HasFlag(Edge.Right) && Best(new[] { rr }, others, xs, threshold) is { } c) { rr += c.Diff; ox = c.Other; gx = c.Target; }
        if (edges.HasFlag(Edge.Top) && Best(new[] { t }, others, ys, threshold) is { } d) { t += d.Diff; oy = d.Other; gy = d.Target; }
        if (edges.HasFlag(Edge.Bottom) && Best(new[] { b }, others, ys, threshold) is { } e) { b += e.Diff; oy = e.Other; gy = e.Target; }
        r = new RectD(l, t, rr - l, b - t);
        if (ox is { } o1) lines.Add(new GuideLine(true, gx, Math.Min(r.Y, o1.Y), Math.Max(r.Bottom, o1.Bottom)));
        if (oy is { } o2) lines.Add(new GuideLine(false, gy, Math.Min(r.X, o2.X), Math.Max(r.Right, o2.Right)));
        return (r, lines);
    }

    private static (double Diff, double Target, RectD Other)? Best(double[] mine, IReadOnlyList<RectD> others,
        Func<RectD, double[]> theirs, double threshold)
    {
        (double Diff, double Target, RectD Other)? best = null;
        foreach (var o in others)
            foreach (var v in theirs(o))
                foreach (var m in mine)
                {
                    double d = v - m;
                    if (Math.Abs(d) < threshold && (best == null || Math.Abs(d) < Math.Abs(best.Value.Diff))) best = (d, v, o);
                }
        return best;
    }
}

public enum AlignMode { Left, CenterX, Right, Top, CenterY, Bottom, SameWidth, SameHeight }

/// <summary>Выравнивание выделенных по «донору»: края, оси, одинаковый размер, равные промежутки.</summary>
public static class AlignOps
{
    public static RectD Apply(RectD donor, RectD r, AlignMode mode) => mode switch
    {
        AlignMode.Left => r with { X = donor.X },
        AlignMode.CenterX => r with { X = donor.X + donor.W / 2 - r.W / 2 },
        AlignMode.Right => r with { X = donor.Right - r.W },
        AlignMode.Top => r with { Y = donor.Y },
        AlignMode.CenterY => r with { Y = donor.Y + donor.H / 2 - r.H / 2 },
        AlignMode.Bottom => r with { Y = donor.Bottom - r.H },
        AlignMode.SameWidth => r with { W = donor.W },
        AlignMode.SameHeight => r with { H = donor.H },
        _ => r,
    };

    /// <summary>Равные промежутки между крайними (крайние стоят на месте).</summary>
    public static RectD[] Distribute(IReadOnlyList<RectD> rects, bool horizontal)
    {
        var order = rects.Select((r, i) => (r, i)).OrderBy(p => horizontal ? p.r.X : p.r.Y).ToList();
        var result = rects.ToArray();
        if (order.Count < 3) return result;
        double start = horizontal ? order[0].r.X : order[0].r.Y;
        double end = horizontal ? order[^1].r.Right : order[^1].r.Bottom;
        double sizes = order.Sum(p => horizontal ? p.r.W : p.r.H);
        double gap = (end - start - sizes) / (order.Count - 1);
        double pos = start;
        foreach (var (r, i) in order)
        {
            result[i] = horizontal ? r with { X = pos } : r with { Y = pos };
            pos += (horizontal ? r.W : r.H) + gap;
        }
        return result;
    }
}
