using Tiles.Core;

namespace Tiles.Tests;

/// <summary>Выравнивание по соседям (без сетки) и закреплённые плитки.</summary>
public class GuidesTests
{
    private static readonly RectD[] Others = { new(100, 100, 200, 120) }; // L100 R300 T100 B220, центр 200,160

    [Fact]
    public void Move_SnapsLeftEdgeToNeighbourLeft_AndReportsLine()
    {
        var (r, lines) = Guides.Move(new RectD(105, 400, 150, 100), Others, 8);
        Assert.Equal(100, r.X);
        Assert.Equal(400, r.Y); // по вертикали рядом никого — не трогаем
        var v = Assert.Single(lines);
        Assert.True(v.Vertical);
        Assert.Equal(100, v.At);
        Assert.Equal(100, v.From); // линия от верха соседа
        Assert.Equal(500, v.To);   // до низа двигаемой
    }

    [Fact]
    public void Move_SnapsTopToNeighbourBottom_SideBySideStack()
    {
        var (r, lines) = Guides.Move(new RectD(400, 225, 150, 100), Others, 8);
        Assert.Equal(220, r.Y);
        Assert.Contains(lines, l => !l.Vertical && l.At == 220);
    }

    [Fact]
    public void Move_CentersAlign()
    {
        var (r, _) = Guides.Move(new RectD(500, 113, 100, 100), Others, 8); // центр 163 ≈ 160
        Assert.Equal(110, r.Y);
    }

    [Fact]
    public void Move_FarAway_NoChange_NoLines()
    {
        var (r, lines) = Guides.Move(new RectD(600, 600, 100, 100), Others, 8);
        Assert.Equal(new RectD(600, 600, 100, 100), r);
        Assert.Empty(lines);
    }

    [Fact]
    public void Resize_BottomEdgeMatchesNeighbourBottom_EqualHeight()
    {
        // виджет справа на той же высоте сверху; тянем низ — прилипает к низу соседа
        var (r, lines) = Guides.Resize(new RectD(320, 100, 150, 116), Edge.Bottom, Others, 8);
        Assert.Equal(120, r.H);
        Assert.Contains(lines, l => !l.Vertical && l.At == 220);
    }

    [Fact]
    public void Resize_OnlyMovingEdgeSnaps()
    {
        var (r, _) = Guides.Resize(new RectD(103, 300, 150, 100), Edge.Right, Others, 8);
        Assert.Equal(103, r.X); // левый край не трогаем, хоть он и рядом с соседом
    }

    [Fact]
    public void Push_LockedNeighbourStays_MovedOneStepsAside()
    {
        var items = new Dictionary<string, RectD>
        {
            ["A"] = new(400, 400, 100, 100),
            ["L"] = new(0, 0, 100, 100),
        };
        var moved = new Dictionary<string, RectD> { ["A"] = new(0, 0, 100, 100) };
        var r = Arrange.Push(items, moved, new RectD(0, 0, 1000, 800), 100, 100, locked: new HashSet<string> { "L" });
        Assert.Equal(new RectD(0, 0, 100, 100), r["L"]);
        Assert.False(Arrange.Overlaps(r["A"], r["L"]));
    }

    [Fact]
    public void Config_LockAndAlign_Defaults()
    {
        Assert.False(new TileInfo().Locked);
        Assert.True(new TileInfo().Align);
        Assert.False(new WidgetInfo().Locked);
        Assert.True(new WidgetInfo().Align);
    }
}
