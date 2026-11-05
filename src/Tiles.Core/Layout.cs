namespace Tiles.Core;

public readonly record struct RectD(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
}

public readonly record struct PointD(double X, double Y);

[Flags]
public enum Edge { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

public static class Layout
{
    public const int MaxCols = 8, MaxRows = 6;

    /// <summary>
    /// Растягивание плитки за край: шаг — один ярлык. Shift — на сколько клеток сдвинулся левый/верхний
    /// край (минус — влево/вверх).
    /// </summary>
    public static (int Cols, int Rows, int ShiftCols, int ShiftRows) Resize(
        int cols, int rows, double dx, double dy, double cellW, double cellH, Edge edges)
    {
        int dc = (int)Math.Round(dx / cellW), dr = (int)Math.Round(dy / cellH);
        int newCols = cols, newRows = rows, shiftCols = 0, shiftRows = 0;
        if (edges.HasFlag(Edge.Right)) newCols = Math.Clamp(cols + dc, 1, MaxCols);
        if (edges.HasFlag(Edge.Left)) { newCols = Math.Clamp(cols - dc, 1, MaxCols); shiftCols = cols - newCols; }
        if (edges.HasFlag(Edge.Bottom)) newRows = Math.Clamp(rows + dr, 1, MaxRows);
        if (edges.HasFlag(Edge.Top)) { newRows = Math.Clamp(rows - dr, 1, MaxRows); shiftRows = rows - newRows; }
        return (newCols, newRows, shiftCols, shiftRows);
    }

    /// <summary>Сколько ярлыков видно на плитке и сколько уходит в «+N».</summary>
    public static (int Shown, int More) Preview(int items, int cols, int rows)
    {
        int cap = cols * rows;
        return items <= cap ? (items, 0) : (cap, items - cap);
    }

    /// <summary>
    /// Клетка, подогнанная под экран: целое число колонок и рядов ровно от края до края
    /// и от верха до панели задач. У каждого монитора своя.
    /// </summary>
    public static (double W, double H) FitCell(RectD work, double cellW, double cellH)
    {
        double cols = Math.Max(1, Math.Round(work.W / cellW)), rows = Math.Max(1, Math.Round(work.H / cellH));
        return (work.W / cols, work.H / rows);
    }

    /// <summary>Прилипание плитки к сетке ярлыков, не вылезая за рабочую область.</summary>
    public static PointD Snap(double x, double y, double cellW, double cellH, double tileW, double tileH, RectD work)
    {
        double maxCol = Math.Max(0, Math.Floor((work.W - tileW) / cellW));
        double maxRow = Math.Max(0, Math.Floor((work.H - tileH) / cellH));
        double col = Math.Clamp(Math.Round((x - work.X) / cellW), 0, maxCol);
        double row = Math.Clamp(Math.Round((y - work.Y) / cellH), 0, maxRow);
        double gx = work.X + col * cellW, gy = work.Y + row * cellH;
        // у правого и нижнего края остаток меньше клетки: даём прижаться к самому краю экрана
        double ex = Math.Max(work.X, work.Right - tileW), ey = Math.Max(work.Y, work.Bottom - tileH);
        return new PointD(Math.Abs(x - ex) < Math.Abs(x - gx) ? ex : gx, Math.Abs(y - ey) < Math.Abs(y - gy) ? ey : gy);
    }

    /// <summary>Кусок уменьшенного снимка рабочего стола под окном (с запасом под размытие).</summary>
    public static RectD ScaledCrop(RectD screen, double deskX, double deskY, double scale, double margin,
        double snapW, double snapH)
    {
        double x = (screen.X - deskX) * scale - margin, y = (screen.Y - deskY) * scale - margin;
        double r = x + screen.W * scale + 2 * margin, b = y + screen.H * scale + 2 * margin;
        x = Math.Max(0, x); y = Math.Max(0, y);
        r = Math.Min(snapW, r); b = Math.Min(snapH, b);
        return new RectD(Math.Round(x), Math.Round(y), Math.Max(1, Math.Round(r - x)), Math.Max(1, Math.Round(b - y)));
    }

    /// <summary>Экран, на котором больше всего прямоугольника (вне всех — ближайший к его середине).</summary>
    public static RectD ScreenFor(RectD r, IReadOnlyList<RectD> screens)
    {
        static double Area(RectD a, RectD b) =>
            Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X)) * Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y));
        var best = screens.OrderByDescending(s => Area(r, s)).First();
        if (Area(r, best) > 0) return best;
        double cx = r.X + r.W / 2, cy = r.Y + r.H / 2;
        return screens.OrderBy(s => Math.Pow(Math.Clamp(cx, s.X, s.Right) - cx, 2) + Math.Pow(Math.Clamp(cy, s.Y, s.Bottom) - cy, 2)).First();
    }

    /// <summary>Место виджета: левый нижний угол рабочей области с отступом.</summary>
    public static RectD BottomLeft(RectD work, double w, double h, double margin)
        => new(work.X + margin, work.Bottom - h - margin, w, h);

    /// <summary>Без привязки к сетке: где отпустили, только не за краем экрана.</summary>
    public static PointD Clamp(double x, double y, double w, double h, RectD work)
        => new(Math.Clamp(x, work.X, Math.Max(work.X, work.Right - w)), Math.Clamp(y, work.Y, Math.Max(work.Y, work.Bottom - h)));

    /// <summary>Где открыть панель: справа, иначе слева, иначе снизу/сверху.</summary>
    public static RectD PlacePanel(RectD tile, double panelW, double panelH, RectD work, double gap)
    {
        double ClampY(double y) => Math.Clamp(y, work.Y, Math.Max(work.Y, work.Bottom - panelH));
        double ClampX(double x) => Math.Clamp(x, work.X, Math.Max(work.X, work.Right - panelW));

        if (tile.Right + gap + panelW <= work.Right)
            return new RectD(tile.Right + gap, ClampY(tile.Y), panelW, panelH);
        if (tile.X - gap - panelW >= work.X)
            return new RectD(tile.X - gap - panelW, ClampY(tile.Y), panelW, panelH);
        if (tile.Bottom + gap + panelH <= work.Bottom)
            return new RectD(ClampX(tile.X), tile.Bottom + gap, panelW, panelH);
        return new RectD(ClampX(tile.X), ClampY(tile.Y - gap - panelH), panelW, panelH);
    }

    /// <summary>Сколько колонок и видимых рядов у панели.</summary>
    public static (int Cols, int Rows) PanelGrid(int count, int cols, int maxRows)
    {
        if (count <= 0) return (1, 1);
        int c = Math.Min(count, cols);
        int r = Math.Min((count + cols - 1) / cols, maxRows);
        return (c, r);
    }
}
