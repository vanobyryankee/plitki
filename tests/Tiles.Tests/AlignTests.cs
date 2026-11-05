using Tiles.Core;

namespace Tiles.Tests;

/// <summary>Выравнивание выделенных по «донору» (по кому нажали правой кнопкой).</summary>
public class AlignTests
{
    private static readonly RectD Clock = new(900, 10, 250, 100);   // донор: центр по X = 1025
    private static readonly RectD Music = new(700, 950, 400, 124);

    [Fact]
    public void CenterVertically_MusicUnderClock()
    {
        var r = AlignOps.Apply(Clock, Music, AlignMode.CenterX);
        Assert.Equal(1025 - 200, r.X);
        Assert.Equal(950, r.Y); // по вертикали не трогаем
    }

    [Theory]
    [InlineData(AlignMode.Left, 900, 950)]
    [InlineData(AlignMode.Right, 750, 950)]
    [InlineData(AlignMode.Top, 700, 10)]
    [InlineData(AlignMode.Bottom, 700, -14)]
    [InlineData(AlignMode.CenterY, 700, -2)]
    public void Edges(AlignMode mode, double x, double y)
    {
        var r = AlignOps.Apply(Clock, Music, mode);
        Assert.Equal(x, r.X);
        Assert.Equal(y, r.Y);
        Assert.Equal(400, r.W);
    }

    [Fact]
    public void SameWidth_And_SameHeight()
    {
        Assert.Equal(new RectD(700, 950, 250, 124), AlignOps.Apply(Clock, Music, AlignMode.SameWidth));
        Assert.Equal(new RectD(700, 950, 400, 100), AlignOps.Apply(Clock, Music, AlignMode.SameHeight));
    }

    [Fact]
    public void Distribute_EqualGaps()
    {
        var rects = new[] { new RectD(0, 0, 100, 50), new RectD(130, 0, 100, 50), new RectD(500, 0, 100, 50) };
        var r = AlignOps.Distribute(rects, horizontal: true);
        Assert.Equal(0, r[0].X);
        Assert.Equal(250, r[1].X); // промежутки по 150
        Assert.Equal(500, r[2].X);
    }
}
