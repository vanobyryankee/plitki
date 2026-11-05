using Tiles.Core;

namespace Tiles.Tests;

public sealed class FolderStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tiles-test-" + Guid.NewGuid().ToString("N"));
    private readonly string _desktop;
    private readonly string _tiles;
    private readonly FolderStore _store;

    public FolderStoreTests()
    {
        _desktop = Path.Combine(_root, "Desktop");
        _tiles = Path.Combine(_root, "Tiles");
        Directory.CreateDirectory(_desktop);
        _store = new FolderStore(_desktop, _tiles);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string DesktopFile(string name, string body = "x")
    {
        var p = Path.Combine(_desktop, name);
        File.WriteAllText(p, body);
        return p;
    }

    [Fact]
    public void CreateFolder_DefaultName_IsPapka_ThenNumbered()
    {
        Assert.Equal("Папка", _store.CreateFolder("Папка"));
        Assert.Equal("Папка 2", _store.CreateFolder("Папка"));
        Assert.True(Directory.Exists(Path.Combine(_tiles, "Папка 2")));
    }

    [Fact]
    public void MoveIn_RemovesFromDesktop_AndAppearsInFolder()
    {
        var f = _store.CreateFolder("Игры");
        var src = DesktopFile("Steam.lnk");

        var moved = _store.MoveIn(src, f);

        Assert.False(File.Exists(src));
        Assert.True(File.Exists(moved));
        Assert.Equal(new[] { moved }, _store.Items(f));
    }

    [Fact]
    public void MoveIn_SameName_GetsSuffix_NothingLost()
    {
        var f = _store.CreateFolder("Игры");
        var a = _store.MoveIn(DesktopFile("Steam.lnk", "a"), f);
        var b = _store.MoveIn(DesktopFile("Steam.lnk", "b"), f);

        Assert.Equal("Steam.lnk", Path.GetFileName(a));
        Assert.Equal("Steam (2).lnk", Path.GetFileName(b));
        Assert.Equal("a", File.ReadAllText(a));
        Assert.Equal("b", File.ReadAllText(b));
    }

    [Fact]
    public void MoveOut_ReturnsToDesktop()
    {
        var f = _store.CreateFolder("Игры");
        var inside = _store.MoveIn(DesktopFile("Steam.lnk"), f);

        var back = _store.MoveOut(inside);

        Assert.Equal(Path.Combine(_desktop, "Steam.lnk"), back);
        Assert.True(File.Exists(back));
        Assert.Empty(_store.Items(f));
    }

    [Fact]
    public void DeleteFolder_ReturnsEverything_AndRemovesDir()
    {
        var f = _store.CreateFolder("Музыка");
        _store.MoveIn(DesktopFile("FL.lnk"), f);
        _store.MoveIn(DesktopFile("Spotify.lnk"), f);
        DesktopFile("FL.lnk", "другой"); // на столе уже есть такой же

        _store.DeleteFolder(f);

        Assert.False(Directory.Exists(Path.Combine(_tiles, f)));
        Assert.True(File.Exists(Path.Combine(_desktop, "Spotify.lnk")));
        Assert.True(File.Exists(Path.Combine(_desktop, "FL.lnk")));
        Assert.True(File.Exists(Path.Combine(_desktop, "FL (2).lnk")));
    }

    [Fact]
    public void MoveIn_Directory_Works()
    {
        var f = _store.CreateFolder("Работа");
        var dir = Path.Combine(_desktop, "Отчёты");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.txt"), "1");

        var moved = _store.MoveIn(dir, f);

        Assert.False(Directory.Exists(dir));
        Assert.True(File.Exists(Path.Combine(moved, "a.txt")));
    }

    [Fact]
    public void RenameFolder_KeepsItems()
    {
        var f = _store.CreateFolder("Папка");
        _store.MoveIn(DesktopFile("Steam.lnk"), f);

        var renamed = _store.RenameFolder(f, "Игры");

        Assert.Equal("Игры", renamed);
        Assert.Single(_store.Items("Игры"));
    }

    [Fact]
    public void RenameFolder_BadChars_AreStripped()
    {
        var f = _store.CreateFolder("Папка");
        Assert.Equal("Игры и софт", _store.RenameFolder(f, "Игры:? и софт"));
    }

    [Fact]
    public void Items_SkipsDesktopIni()
    {
        var f = _store.CreateFolder("Игры");
        File.WriteAllText(Path.Combine(_tiles, f, "desktop.ini"), "");
        Assert.Empty(_store.Items(f));
    }
}
