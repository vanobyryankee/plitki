using Tiles.Core;

namespace Tiles.Tests;

public class LayoutTests
{
    private static readonly RectD Work = new(0, 0, 1707, 912);

    [Fact]
    public void Snap_GoesToNearestCell()
    {
        var p = Layout.Snap(80, 160, cellW: 75, cellH: 100, tileW: 150, tileH: 200, Work);
        Assert.Equal(75, p.X);
        Assert.Equal(200, p.Y);
    }

    [Fact]
    public void Snap_KeepsTileInsideWorkArea()
    {
        var p = Layout.Snap(1700, 900, 75, 100, 150, 200, Work);
        Assert.True(p.X + 150 <= Work.Right);
        Assert.True(p.Y + 200 <= Work.Bottom);
        Assert.True(p.X >= 0 && p.Y >= 0);
    }

    [Fact]
    public void FitCell_DividesScreenEvenly()
    {
        // экран 2560×1440 при 150 %, панель задач 48 px: 1706.7 × 928 точек, клетка Windows 76.7 × 100.7
        var (w, h) = Layout.FitCell(new RectD(0, 0, 1706.67, 928), 76.67, 100.67);
        Assert.Equal(1706.67 / 22, w, 2); // 22 колонки ровно до края
        Assert.Equal(928.0 / 9, h, 2);    // 9 рядов ровно до панели задач
        var (w2, h2) = Layout.FitCell(new RectD(0, -960, 1706.67, 960), 76.67, 100.67);
        Assert.Equal(w, w2, 2);
        Assert.Equal(960.0 / 10, h2, 2);   // верхний экран без панели — своя высота клетки
    }

    [Fact]
    public void Snap_CanHugBottomRightEdge()
    {
        // виджет 400×117 у правого нижнего угла: сетка не даёт прижаться (остаток меньше клетки),
        // а прилипание к краю экрана — даёт
        var p = Layout.Snap(1300, 790, 75, 100, 400, 117, Work);
        Assert.Equal(Work.Right - 400, p.X);
        Assert.Equal(Work.Bottom - 117, p.Y);
    }

    [Fact]
    public void Snap_FarFromEdge_StaysOnGrid()
    {
        var p = Layout.Snap(610, 420, 75, 100, 400, 117, Work);
        Assert.Equal(600, p.X);
        Assert.Equal(400, p.Y);
    }

    [Fact]
    public void Panel_OpensRight_WhenRoom()
    {
        var r = Layout.PlacePanel(new RectD(100, 100, 150, 150), 400, 300, Work, gap: 8);
        Assert.Equal(258, r.X);
        Assert.Equal(100, r.Y);
    }

    [Fact]
    public void Panel_OpensLeft_AtRightEdge()
    {
        var r = Layout.PlacePanel(new RectD(1500, 100, 150, 150), 400, 300, Work, 8);
        Assert.Equal(1500 - 8 - 400, r.X);
    }

    [Fact]
    public void Panel_StaysInsideVertically()
    {
        var r = Layout.PlacePanel(new RectD(100, 800, 150, 100), 400, 300, Work, 8);
        Assert.True(r.Bottom <= Work.Bottom);
        Assert.True(r.Y >= Work.Y);
    }

    [Fact]
    public void Panel_NoSideRoom_GoesBelowOrAbove()
    {
        var narrow = new RectD(0, 0, 500, 912);
        var r = Layout.PlacePanel(new RectD(150, 100, 150, 150), 400, 300, narrow, 8);
        Assert.Equal(258, r.Y); // под плиткой
        Assert.True(r.X >= 0 && r.Right <= 500);
    }

    [Fact]
    public void PanelSize_FourColumns_RowsGrowThenScroll()
    {
        Assert.Equal((1, 1), Layout.PanelGrid(1, cols: 4, maxRows: 4));
        Assert.Equal((4, 2), Layout.PanelGrid(5, 4, 4));
        Assert.Equal((4, 4), Layout.PanelGrid(30, 4, 4));
        Assert.Equal((1, 1), Layout.PanelGrid(0, 4, 4));
    }
}
