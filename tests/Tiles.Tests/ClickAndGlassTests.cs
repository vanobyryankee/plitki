using Tiles.Core;

namespace Tiles.Tests;

public class ClickAndGlassTests
{
    [Fact]
    public void NewSettings_Defaults()
    {
        var s = new TileStyle();
        Assert.False(s.ClickLaunch);   // по умолчанию клик открывает папку, как раньше
        Assert.True(s.ShellMenu);      // меню Windows на значках включено
    }

    [Fact]
    public void GlassToggle_DefaultOn_SurvivesSaveLoad()
    {
        Assert.True(new TileStyle().Glass);
        var cfg = new TilesConfig();
        cfg.Defaults.Glass = false;
        var path = Path.Combine(Path.GetTempPath(), $"tiles-{Guid.NewGuid():N}.json");
        ConfigStore.Save(path, cfg);
        Assert.False(ConfigStore.Load(path).Defaults.Glass);
        File.Delete(path);
    }

    [Fact]
    public void NewSettings_Survive_Clone()
    {
        var s = new TileStyle { ClickLaunch = true, ShellMenu = false }.Clone();
        Assert.True(s.ClickLaunch);
        Assert.False(s.ShellMenu);
    }

    [Fact]
    public void GlassCrop_FromQuarterSnapshot()
    {
        // рабочий стол начинается на 1440 выше (второй монитор сверху), снимок уменьшен в 4 раза
        var r = Layout.ScaledCrop(screen: new RectD(400, 200, 300, 200), deskX: 0, deskY: -1440, scale: 0.25, margin: 10,
            snapW: 640, snapH: 720);
        Assert.Equal(new RectD(90, 400, 95, 70), r);
    }

    [Fact]
    public void GlassCrop_ClampedAtEdges()
    {
        var r = Layout.ScaledCrop(new RectD(0, 0, 100, 100), 0, 0, 0.25, 10, 640, 720);
        Assert.Equal(0, r.X);
        Assert.Equal(0, r.Y);
        Assert.Equal(35, r.W);
    }
}
