using Tiles.Core;
using Xunit;

public class ItemOrderTests
{
    static string[] P(params string[] n) => n.Select(x => $@"C:\d\{x}.lnk").ToArray();
    static string[] Names(IEnumerable<string> paths) => paths.Select(p => Path.GetFileNameWithoutExtension(p)).ToArray();

    [Fact]
    public void EmptyOrder_Alphabetical()
        => Assert.Equal(new[] { "a", "b", "c" }, Names(ItemOrder.Sort(P("b", "a", "c"), new List<string>())));

    [Fact]
    public void Ordered_First_RestAtEnd()
        => Assert.Equal(new[] { "c", "a", "b" }, Names(ItemOrder.Sort(P("a", "b", "c"), new List<string> { "c.lnk", "a.lnk" })));

    [Fact]
    public void MissingNames_Ignored()
        => Assert.Equal(new[] { "a", "b" }, Names(ItemOrder.Sort(P("a", "b"), new List<string> { "x.lnk", "A.LNK" })));

    [Fact]
    public void Move_Forward()
        => Assert.Equal(new[] { "b", "c", "a", "d" }, ItemOrder.Move(new[] { "a", "b", "c", "d" }, 0, 2));

    [Fact]
    public void Move_Back()
        => Assert.Equal(new[] { "d", "a", "b", "c" }, ItemOrder.Move(new[] { "a", "b", "c", "d" }, 3, 0));

    [Fact]
    public void Move_OutOfRange_Clamped()
        => Assert.Equal(new[] { "b", "c", "a" }, ItemOrder.Move(new[] { "a", "b", "c" }, 0, 99));

    [Theory]
    [InlineData(10, 110, 4)]   // 2-я строка, 1-я колонка
    [InlineData(390, 300, 5)]  // за последним — последний
    [InlineData(-20, -20, 0)]  // выше и левее — первый
    [InlineData(250, 10, 2)]   // 1-я строка, 3-я колонка
    public void SlotAt_Grid(double x, double y, int expected)
        => Assert.Equal(expected, ItemOrder.SlotAt(x, y, itemW: 100, itemH: 100, cols: 4, count: 6));
}
