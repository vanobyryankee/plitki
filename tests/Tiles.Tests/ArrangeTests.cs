using Tiles.Core;

namespace Tiles.Tests;

public class ArrangeTests
{
    private static readonly RectD Work = new(0, 0, 1000, 800);
    private const double S = 100; // клетка

    private static bool NoOverlaps(IReadOnlyDictionary<string, RectD> r)
    {
        var list = r.Values.ToList();
        for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
                if (Arrange.Overlaps(list[i], list[j])) return false;
        return true;
    }

    private static bool Inside(RectD r) => r.X >= Work.X - 0.5 && r.Y >= Work.Y - 0.5 && r.Right <= Work.Right + 0.5 && r.Bottom <= Work.Bottom + 0.5;

    [Fact]
    public void Overlaps_TouchingIsNotOverlap()
    {
        Assert.False(Arrange.Overlaps(new RectD(0, 0, 100, 100), new RectD(100, 0, 100, 100)));
        Assert.True(Arrange.Overlaps(new RectD(0, 0, 100, 100), new RectD(99, 0, 100, 100)));
    }

    [Fact]
    public void Push_NeighbourMovesAside_MovedStays()
    {
        var items = new Dictionary<string, RectD>
        {
            ["A"] = new(400, 400, 200, 200),
            ["B"] = new(0, 0, 200, 200),
            ["C"] = new(200, 0, 200, 200),
        };
        var moved = new Dictionary<string, RectD> { ["A"] = new(0, 0, 200, 200) };
        var r = Arrange.Push(items, moved, Work, S, S);
        Assert.Equal(new RectD(0, 0, 200, 200), r["A"]);
        Assert.Equal(new RectD(200, 0, 200, 200), r["C"]); // не мешал — не трогаем
        Assert.NotEqual(new RectD(0, 0, 200, 200), r["B"]);
        Assert.True(NoOverlaps(r));
        Assert.All(r.Values, x => Assert.True(Inside(x)));
    }

    [Fact]
    public void Push_PicksNearestFreeSpot()
    {
        var items = new Dictionary<string, RectD>
        {
            ["A"] = new(800, 600, 100, 100),
            ["B"] = new(500, 400, 100, 100),
        };
        var moved = new Dictionary<string, RectD> { ["A"] = new(500, 400, 100, 100) };
        var r = Arrange.Push(items, moved, Work, S, S);
        var b = r["B"];
        double dist = Math.Abs(b.X - 500) + Math.Abs(b.Y - 400);
        Assert.Equal(100, dist); // соседняя клетка
    }

    [Fact]
    public void Push_GroupOfTwo()
    {
        var items = new Dictionary<string, RectD>
        {
            ["A"] = new(0, 0, 100, 100),
            ["B"] = new(100, 0, 100, 100),
            ["C"] = new(300, 0, 100, 100),
        };
        var moved = new Dictionary<string, RectD> { ["A"] = new(200, 0, 100, 100), ["B"] = new(300, 0, 100, 100) };
        var r = Arrange.Push(items, moved, Work, S, S);
        Assert.Equal(new RectD(200, 0, 100, 100), r["A"]);
        Assert.Equal(new RectD(300, 0, 100, 100), r["B"]);
        Assert.True(NoOverlaps(r));
    }

    [Fact]
    public void ClampGroup_StopsAtEdge_KeepsShape()
    {
        var group = new[] { new RectD(800, 0, 100, 100), new RectD(900, 0, 100, 100) };
        var (dx, dy) = Arrange.ClampGroup(group, 250, -50, Work);
        Assert.Equal(0, dx); // правая уже у края
        Assert.Equal(0, dy);
        (dx, dy) = Arrange.ClampGroup(group, -300, 120, Work);
        Assert.Equal(-300, dx);
        Assert.Equal(120, dy);
    }

    [Fact]
    public void Marquee_SelectsIntersecting()
    {
        var items = new Dictionary<string, RectD>
        {
            ["A"] = new(0, 0, 100, 100),
            ["B"] = new(150, 150, 100, 100),
            ["C"] = new(600, 600, 100, 100),
        };
        var sel = Arrange.InMarquee(items, new RectD(50, 50, 120, 120)).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "A", "B" }, sel);
    }

    [Fact]
    public void WidgetResize_Free_EachAxisOwn()
    {
        // растягиваем только вниз: ширина та же, высота своя — можно выровнять с соседом по высоте
        var r = WidgetSizing.Resize(new RectD(0, 0, 300, 136), 0, 64, Edge.Bottom, false, 100, 100, 120, 80);
        Assert.Equal(300, r.W, 1);
        Assert.Equal(200, r.H, 1);
    }

    [Fact]
    public void WidgetResize_Snap_BothAxesInCells()
    {
        var r = WidgetSizing.Resize(new RectD(0, 0, 300, 136), 130, 30, Edge.Right | Edge.Bottom, true, 100, 100, 120, 80);
        Assert.Equal(400, r.W, 1); // 430 → 4 клетки
        Assert.Equal(200, r.H, 1); // 166 → 2 клетки
    }

    [Fact]
    public void WidgetResize_LeftTop_KeepsOppositeCorner()
    {
        var r = WidgetSizing.Resize(new RectD(500, 300, 200, 100), -200, -100, Edge.Left | Edge.Top, false, 100, 100, 120, 80);
        Assert.Equal(new RectD(300, 200, 400, 200), r);
    }

    [Fact]
    public void WidgetResize_NotSmallerThanMinimum()
    {
        var r = WidgetSizing.Resize(new RectD(0, 0, 300, 136), -1000, -1000, Edge.Right | Edge.Bottom, false, 100, 100, 120, 80);
        Assert.Equal(120, r.W, 1);
        Assert.Equal(80, r.H, 1);
    }

    [Fact]
    public void ContentScale_FitsInsideKeepingShape()
    {
        Assert.Equal(1.0, WidgetSizing.ContentScale(300, 136, 300, 136), 3);
        Assert.Equal(1.0, WidgetSizing.ContentScale(300, 136, 600, 136), 3); // шире — масштаб по высоте
        Assert.Equal(2.0, WidgetSizing.ContentScale(300, 136, 600, 272), 3);
    }

    [Fact]
    public void ScreenFor_PicksMonitorWithMostOverlap()
    {
        var screens = new[] { new RectD(0, 0, 1707, 912), new RectD(0, -960, 1707, 960) };
        Assert.Equal(screens[1], Layout.ScreenFor(new RectD(100, -300, 200, 200), screens));
        Assert.Equal(screens[0], Layout.ScreenFor(new RectD(100, -50, 200, 200), screens)); // большая часть внизу
        Assert.Equal(screens[0], Layout.ScreenFor(new RectD(5000, 5000, 10, 10), screens)); // вне экранов — ближайший
    }
    [Fact]
    public void Config_Widgets_DefaultSet()
    {
        var cfg = new TilesConfig();
        cfg.Normalize();
        var kinds = cfg.Widgets.Select(w => w.Kind).ToHashSet();
        Assert.Equal(new HashSet<string> { "music", "bin", "clock", "system", "weather", "note", "net" }, kinds);
        Assert.False(cfg.Widgets.Single(w => w.Kind == "net").Enabled); // пульт сети включают в мастерской
        Assert.True(cfg.Widgets.Single(w => w.Kind == "music").Enabled);
        Assert.True(cfg.Widgets.Single(w => w.Kind == "bin").Enabled);
        Assert.False(cfg.Widgets.Single(w => w.Kind == "clock").Enabled);
    }

    [Fact]
    public void Config_OldFlags_Migrate()
    {
        var cfg = new TilesConfig { MusicWidget = false, BinTile = true, BinX = 10, BinY = 20 };
        cfg.Normalize();
        Assert.False(cfg.Widgets.Single(w => w.Kind == "music").Enabled);
        var bin = cfg.Widgets.Single(w => w.Kind == "bin");
        Assert.True(double.IsNaN(bin.W)); // размер ещё не задан — возьмётся базовый
        Assert.Equal(10, bin.X);
        Assert.Equal(20, bin.Y);
    }
}
