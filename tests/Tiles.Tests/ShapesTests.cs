using Tiles.Core;

namespace Tiles.Tests;

public class ShapesTests
{
    [Theory]
    [InlineData(300, 124, WidgetShape.Wide)]
    [InlineData(200, 200, WidgetShape.Square)]
    [InlineData(120, 330, WidgetShape.Tall)]
    public void Shape_FromSides(double w, double h, WidgetShape want)
        => Assert.Equal(want, Shapes.Of(w, h, WidgetShape.Square));

    [Fact]
    public void Shape_HoldsNearBorder()
    {
        // 1.45 — уже не «длинный» с нуля, но длинный не перескакивает в квадрат на самом краю
        Assert.Equal(WidgetShape.Wide, Shapes.Of(145, 100, WidgetShape.Wide));
        Assert.Equal(WidgetShape.Square, Shapes.Of(145, 100, WidgetShape.Square));
        Assert.Equal(WidgetShape.Wide, Shapes.Of(165, 100, WidgetShape.Square));
        Assert.Equal(WidgetShape.Tall, Shapes.Of(85, 100, WidgetShape.Tall));
        Assert.Equal(WidgetShape.Square, Shapes.Of(85, 100, WidgetShape.Square));
    }
}
