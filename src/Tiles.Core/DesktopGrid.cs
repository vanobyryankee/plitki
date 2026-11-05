namespace Tiles.Core;

/// <summary>Сетка ярлыков Windows: куда поставить ярлык, чтобы он не оказался под плиткой.</summary>
public static class DesktopGrid
{
    /// <summary>Доля клетки, закрытая плиткой, после которой ярлык считается спрятанным.</summary>
    private const double HiddenShare = 0.25;

    /// <summary>
    /// Первая свободная клетка в порядке Windows (по столбцам сверху вниз, слева направо):
    /// не под плиткой и без другого ярлыка. Нет места — null.
    /// </summary>
    public static PointD? FreeCell(RectD area, double originX, double originY, double sw, double sh,
        IEnumerable<PointD> occupied, IEnumerable<RectD> blocked)
    {
        var busy = occupied.ToList();
        var tiles = blocked.ToList();
        double x0 = originX + Math.Ceiling((area.X - originX) / sw) * sw;
        double y0 = originY + Math.Ceiling((area.Y - originY) / sh) * sh;
        for (double x = x0; x + sw <= area.Right + 0.5; x += sw)
            for (double y = y0; y + sh <= area.Bottom + 0.5; y += sh)
            {
                var cell = new PointD(x, y);
                if (Covered(cell, sw, sh, tiles)) continue;
                if (busy.Any(p => Math.Abs(p.X - x) < sw / 2 && Math.Abs(p.Y - y) < sh / 2)) continue;
                return cell;
            }
        return null;
    }

    /// <summary>Ярлык в этой клетке заметно закрыт плиткой.</summary>
    public static bool Covered(PointD item, double sw, double sh, IEnumerable<RectD> blocked)
    {
        foreach (var b in blocked)
        {
            double w = Math.Min(item.X + sw, b.Right) - Math.Max(item.X, b.X);
            double h = Math.Min(item.Y + sh, b.Bottom) - Math.Max(item.Y, b.Y);
            if (w > 0 && h > 0 && w * h > HiddenShare * sw * sh) return true;
        }
        return false;
    }
}
