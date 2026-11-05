using Tiles.Core;

namespace Tiles.Tests;

public class WidgetsTests
{
    [Theory]
    [InlineData(true, true, true)]    // играет
    [InlineData(true, false, true)]   // пауза
    [InlineData(false, false, false)] // плеера нет
    public void Music_Visibility(bool hasPlayer, bool playing, bool visible)
        => Assert.Equal(visible, Music.Visible(hasPlayer, playing));

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(65, "1:05")]
    [InlineData(3725, "1:02:05")]
    public void Music_Time(double sec, string text) => Assert.Equal(text, Music.Time(TimeSpan.FromSeconds(sec)));

    [Theory]
    [InlineData(30, 120, 0.25)]
    [InlineData(200, 120, 1)]
    [InlineData(-5, 120, 0)]
    [InlineData(10, 0, 0)]
    public void Music_Progress(double pos, double len, double expected)
        => Assert.Equal(expected, Music.Progress(TimeSpan.FromSeconds(pos), TimeSpan.FromSeconds(len)), 3);

    [Fact]
    public void Music_Position_AdvancesWhilePlaying()
    {
        var at = new DateTimeOffset(2026, 11, 6, 1, 0, 0, TimeSpan.Zero);
        var len = TimeSpan.FromSeconds(100);
        Assert.Equal(TimeSpan.FromSeconds(15), Music.Position(TimeSpan.FromSeconds(10), at, at.AddSeconds(5), true, len));
        Assert.Equal(TimeSpan.FromSeconds(10), Music.Position(TimeSpan.FromSeconds(10), at, at.AddSeconds(5), false, len));
        Assert.Equal(len, Music.Position(TimeSpan.FromSeconds(98), at, at.AddSeconds(5), true, len));
    }

    [Theory]
    [InlineData(0, "Пусто")]
    [InlineData(1, "1 файл")]
    [InlineData(3, "3 файла")]
    [InlineData(5, "5 файлов")]
    [InlineData(11, "11 файлов")]
    [InlineData(21, "21 файл")]
    [InlineData(112, "112 файлов")]
    public void Bin_Caption(long count, string text) => Assert.Equal(text, Bin.Caption(count));

    [Fact]
    public void Widget_BottomLeft()
    {
        var r = Layout.BottomLeft(new RectD(0, 0, 1707, 912), 340, 96, 16);
        Assert.Equal(new RectD(16, 912 - 96 - 16, 340, 96), r);
    }

    [Fact]
    public void Config_NewWidgets_DefaultOn_AndSurvive()
    {
        var cfg = new TilesConfig();
        Assert.True(cfg.MusicWidget);
        Assert.True(cfg.BinTile);
        var path = Path.Combine(Path.GetTempPath(), $"tiles-{Guid.NewGuid():N}.json");
        cfg.MusicWidget = false;
        cfg.BinX = 100; cfg.BinY = 200;
        ConfigStore.Save(path, cfg);
        var back = ConfigStore.Load(path);
        Assert.False(back.MusicWidget);
        Assert.Equal(100, back.BinX);
        Assert.Equal(200, back.BinY);
        File.WriteAllText(path, "{\"Tiles\":[]}");
        Assert.True(ConfigStore.Load(path).BinTile);
        File.Delete(path);
    }
}
