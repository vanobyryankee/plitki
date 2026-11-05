using Tiles.Core;

namespace Tiles.Tests;

public sealed class TileStyleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tiles-style-" + Guid.NewGuid().ToString("N"));
    public TileStyleTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Defaults_MatchCurrentLook()
    {
        var s = new TileStyle();
        Assert.True(s.SnapToGrid);
        Assert.False(s.TileCaptions);
        Assert.True(s.PanelCaptions);
        Assert.Equal(48, s.IconSize);
        Assert.Equal(0x2EFFFFFFu, s.TintArgb());
    }

    [Fact]
    public void Tint_DarkHalf()
    {
        var s = new TileStyle { Tint = "#000000", GlassOpacity = 50 };
        Assert.Equal(0x80000000u, s.TintArgb());
    }

    [Fact]
    public void Normalize_ClampsAndFixesColor()
    {
        var s = new TileStyle { GlassOpacity = 250, Blur = -5, IconSize = 500, Corner = 99, Tint = "розовый" };
        s.Normalize();
        Assert.Equal(100, s.GlassOpacity);
        Assert.Equal(0, s.Blur);
        Assert.Equal(TileStyle.MaxIcon, s.IconSize);
        Assert.Equal(TileStyle.MaxCorner, s.Corner);
        Assert.Equal("#FFFFFF", s.Tint);
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        var a = new TileStyle();
        var b = a.Clone();
        b.IconSize = 64;
        Assert.Equal(48, a.IconSize);
    }

    [Fact]
    public void PerTileStyles_SurviveRestart_AndOldConfigGetsDefaults()
    {
        var path = Path.Combine(_dir, "tiles.json");
        var c = new TilesConfig();
        c.Defaults.IconSize = 40;
        c.Tiles.Add(new TileInfo { Name = "Игры", Style = new TileStyle { SnapToGrid = false, Tint = "#7A5CFF" } });
        c.Tiles.Add(new TileInfo { Name = "Работа" });
        ConfigStore.Save(path, c);

        var back = ConfigStore.Load(path);
        Assert.False(back.Tiles[0].Style.SnapToGrid);
        Assert.Equal("#7A5CFF", back.Tiles[0].Style.Tint);
        Assert.True(back.Tiles[1].Style.SnapToGrid);
        Assert.Equal(40, back.Defaults.IconSize);

        // старый tiles.json без настроек
        File.WriteAllText(path, """{ "Tiles": [ { "Name": "Папка", "X": 1, "Y": 2 } ] }""");
        var old = ConfigStore.Load(path);
        Assert.NotNull(old.Tiles[0].Style);
        Assert.Equal(48, old.Tiles[0].Style.IconSize);
    }

    [Fact]
    public void ApplyToAll_CopiesTemplate_ButEachStaysOwn()
    {
        var c = new TilesConfig();
        c.Tiles.Add(new TileInfo { Name = "A" });
        c.Tiles.Add(new TileInfo { Name = "B" });
        c.Defaults.Shadow = 70;

        c.ApplyDefaultsToAll();
        c.Tiles[0].Style.Shadow = 10;

        Assert.Equal(10, c.Tiles[0].Style.Shadow);
        Assert.Equal(70, c.Tiles[1].Style.Shadow);
    }

    [Fact]
    public void FreePlacement_StaysWhereDropped_InsideScreen()
    {
        var work = new RectD(0, 0, 1707, 912);
        Assert.Equal(new PointD(333, 217), Layout.Clamp(333, 217, 150, 200, work));
        var edge = Layout.Clamp(1650, 900, 150, 200, work);
        Assert.Equal(new PointD(1557, 712), edge);
    }
}
