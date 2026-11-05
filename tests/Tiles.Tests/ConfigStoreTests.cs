using Tiles.Core;

namespace Tiles.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tiles-cfg-" + Guid.NewGuid().ToString("N"));
    private string PathJson => Path.Combine(_dir, "tiles.json");

    public ConfigStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Missing_GivesEmpty()
    {
        var c = ConfigStore.Load(PathJson);
        Assert.Empty(c.Tiles);
        Assert.True(c.Autostart);
    }

    [Fact]
    public void SaveLoad_RoundTrip_Cyrillic()
    {
        var c = new TilesConfig();
        c.Tiles.Add(new TileInfo { Name = "Игры", X = 75, Y = 200 });
        ConfigStore.Save(PathJson, c);

        var back = ConfigStore.Load(PathJson);
        Assert.Equal("Игры", back.Tiles[0].Name);
        Assert.Equal(200, back.Tiles[0].Y);
    }

    [Fact]
    public void Corrupt_IsBackedUp_AndEmptyReturned()
    {
        File.WriteAllText(PathJson, "{ это не json");
        var c = ConfigStore.Load(PathJson);
        Assert.Empty(c.Tiles);
        Assert.Single(Directory.GetFiles(_dir, "tiles.json.broken-*"));
    }
}
