using Tiles.Core;

namespace Tiles.Tests;

public class PresetsTests
{
    private static TilesConfig Desk()
    {
        var c = new TilesConfig();
        c.Tiles.Add(new TileInfo { Name = "Игры", X = 0, Y = 0, Cols = 2, Rows = 2 });
        c.Tiles.Add(new TileInfo { Name = "Музыка", X = 0, Y = 300, Cols = 2, Rows = 1, Locked = true });
        c.Widgets.Add(new WidgetInfo { Kind = "clock", Enabled = true, X = 700, Y = 0, W = 310, H = 100 });
        c.Widgets.Add(new WidgetInfo { Kind = "net", Enabled = false, X = 900, Y = 0 });
        c.Tiles[0].Style.Blur = 8;
        c.GridW = 79; c.GridH = 98;
        return c;
    }

    [Fact]
    public void SaveMoveRestore_ReturnsEverything()
    {
        var c = Desk();
        var p = Presets.Capture(c, "Дом");
        c.Tiles[0].X = 500; c.Tiles[0].Cols = 1; c.Tiles[0].Style.Blur = 30;
        c.Tiles[1].Locked = false;
        c.Widgets[0].X = 5; c.Widgets[0].Props["sec"] = "1";
        c.Widgets[1].Enabled = true;
        c.GridW = double.NaN;

        Presets.Apply(c, p);

        Assert.Equal(0, c.Tiles[0].X);
        Assert.Equal(2, c.Tiles[0].Cols);
        Assert.Equal(8, c.Tiles[0].Style.Blur);
        Assert.True(c.Tiles[1].Locked);
        Assert.Equal(700, c.Widgets[0].X);
        Assert.False(c.Widgets[0].Props.ContainsKey("sec"));
        Assert.False(c.Widgets[1].Enabled);
        Assert.Equal(79, c.GridW);
    }

    [Fact]
    public void Capture_IsCopy_NotShared()
    {
        var c = Desk();
        var p = Presets.Capture(c, "Дом");
        c.Tiles[0].X = 999; c.Tiles[0].Style.Blur = 1;
        Assert.Equal(0, p.Tiles[0].X);
        Assert.Equal(8, p.Tiles[0].Style.Blur);
    }

    [Fact]
    public void MissingAndNewFolders_AreSafe()
    {
        var c = Desk();
        var p = Presets.Capture(c, "Дом");
        c.Tiles.RemoveAt(1);                                         // папку «Музыка» удалили
        c.Tiles.Add(new TileInfo { Name = "Новая", X = 40, Y = 40 }); // новую создали
        Presets.Apply(c, p);
        Assert.Equal(2, c.Tiles.Count);
        Assert.Equal(40, c.Tiles.Single(t => t.Name == "Новая").X);
    }

    [Fact]
    public void Save_SameName_Replaces()
    {
        var c = Desk();
        Presets.Save(c, "Дом");
        c.Tiles[0].X = 77;
        Presets.Save(c, "дом");
        Assert.Single(c.Presets);
        Assert.Equal(77, c.Presets[0].Tiles[0].X);
        Assert.Equal("дом", c.ActivePreset);
    }

    [Fact]
    public void Config_WithPresets_Roundtrips()
    {
        var c = Desk();
        Presets.Save(c, "Дом");
        var path = Path.Combine(Path.GetTempPath(), $"presets-{Guid.NewGuid():N}.json");
        try
        {
            ConfigStore.Save(path, c);
            var back = ConfigStore.Load(path);
            Assert.Equal("Дом", back.ActivePreset);
            Assert.Equal(2, back.Presets[0].Tiles.Count);
            Assert.Equal(700, back.Presets[0].Widgets[0].X);
        }
        finally { File.Delete(path); }
    }
}
