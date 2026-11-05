using Tiles.Core;

namespace Tiles.Tests;

public class TileGridTests
{
    [Fact]
    public void Resize_RightEdge_GrowsByWholeIcons()
    {
        var r = Layout.Resize(2, 2, dx: 80, dy: 0, cellW: 75, cellH: 100, Edge.Right);
        Assert.Equal((3, 2, 0, 0), r);
    }

    [Fact]
    public void Resize_LeftEdge_GrowsLeft_AndShifts()
    {
        var r = Layout.Resize(2, 2, dx: -80, dy: 0, cellW: 75, cellH: 100, Edge.Left);
        Assert.Equal((3, 2, -1, 0), r);
    }

    [Fact]
    public void Resize_SmallPull_DoesNothing()
    {
        Assert.Equal((2, 2, 0, 0), Layout.Resize(2, 2, 20, 30, 75, 100, Edge.Right | Edge.Bottom));
    }

    [Fact]
    public void Resize_Corner_ChangesBoth()
    {
        Assert.Equal((3, 3, 0, -1), Layout.Resize(2, 2, 75, -100, 75, 100, Edge.Right | Edge.Top));
    }

    [Fact]
    public void Resize_ClampsToOneIcon_AndMax()
    {
        Assert.Equal((1, 1, 1, 0), Layout.Resize(2, 1, 300, 0, 75, 100, Edge.Left));
        var big = Layout.Resize(2, 2, 5000, 5000, 75, 100, Edge.Right | Edge.Bottom);
        Assert.Equal(Layout.MaxCols, big.Cols);
        Assert.Equal(Layout.MaxRows, big.Rows);
    }

    [Theory]
    [InlineData(3, 2, 2, 3, 0)]   // всё влезает
    [InlineData(4, 2, 2, 4, 0)]
    [InlineData(7, 2, 2, 4, 3)]   // 4 видно, «+3»
    [InlineData(10, 3, 2, 6, 4)]
    [InlineData(0, 2, 2, 0, 0)]
    public void Preview_ShowsCells_RestInBadge(int items, int cols, int rows, int shown, int more)
    {
        Assert.Equal((shown, more), Layout.Preview(items, cols, rows));
    }
}
