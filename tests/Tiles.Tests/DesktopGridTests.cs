using Tiles.Core;

namespace Tiles.Tests;

public class DesktopGridTests
{
    private static readonly RectD Area = new(0, 0, 300, 300);
    private static readonly RectD Tile = new(0, 0, 200, 200);

    [Fact]
    public void NewIcon_GoesToFirstFreeCell_PastTheTile()
    {
        var p = DesktopGrid.FreeCell(Area, 0, 0, 100, 100, [], [Tile]);
        Assert.Equal(new PointD(0, 200), p);
    }

    [Fact]
    public void SkipsCellsWithIcons_ColumnByColumn()
    {
        var p = DesktopGrid.FreeCell(Area, 0, 0, 100, 100, [new PointD(0, 200)], [Tile]);
        Assert.Equal(new PointD(100, 200), p);
    }

    [Fact]
    public void GridOffset_IsRespected()
    {
        var p = DesktopGrid.FreeCell(new RectD(0, 0, 320, 320), 10, 10, 100, 100, [], [new RectD(0, 0, 215, 215)]);
        Assert.Equal(new PointD(10, 210), p);
    }

    [Fact]
    public void NoRoom_GivesNull()
    {
        var full = Enumerable.Range(0, 3).SelectMany(x => Enumerable.Range(0, 3).Select(y => new PointD(x * 100, y * 100)));
        Assert.Null(DesktopGrid.FreeCell(Area, 0, 0, 100, 100, full, []));
    }

    [Fact]
    public void Covered_WhenUnderTile_NotWhenJustTouching()
    {
        Assert.True(DesktopGrid.Covered(new PointD(50, 50), 100, 100, [Tile]));
        Assert.False(DesktopGrid.Covered(new PointD(200, 0), 100, 100, [Tile]));
        Assert.False(DesktopGrid.Covered(new PointD(190, 0), 100, 100, [Tile])); // чуть задело краем
    }
}
