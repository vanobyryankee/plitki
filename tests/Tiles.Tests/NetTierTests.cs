using Tiles.Core;

namespace Tiles.Tests;

public class NetTierTests
{
    [Theory]
    [InlineData(66, 66, NetTier.Mini)]       // одна клетка — только переключатель
    [InlineData(110, 90, NetTier.Mini)]
    [InlineData(150, 150, NetTier.Button)]   // переключатель, сервер и пинг
    [InlineData(330, 160, NetTier.Medium)]   // + скорость
    [InlineData(160, 300, NetTier.Medium)]
    [InlineData(420, 220, NetTier.Full)]     // + пинг, связь, zapret и Телеграм
    [InlineData(220, 440, NetTier.Full)]
    public void Tier_BySize(double w, double h, NetTier want) => Assert.Equal(want, NetTiers.Of(w, h));

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1_312_500, "10,5")]      // 10,5 Мбит/с
    [InlineData(125_000, "1,0")]
    [InlineData(25_000_000, "200")]      // от сотни — без дробной части
    [InlineData(12_000, "0,1")]
    public void Mbit_LikePanel(double bytesPerSec, string want) => Assert.Equal(want, NetTiers.Mbit(bytesPerSec));

    [Theory]
    [InlineData(0, "0 Б")]
    [InlineData(1_310_720_000, "1,22 ГБ")]
    [InlineData(89_128_960, "85 МБ")]
    public void Total_LikePanel(long bytes, string want) => Assert.Equal(want, NetTiers.Total(bytes));
}
