using Tiles.Core;

namespace Tiles.Tests;

/// <summary>Сохранение настроек не должно будить пересборку плиток (была петля: 23 перерисовки в секунду).</summary>
public class WatcherTests
{
    private const string Root = @"E:\Users\x\Desktop-Плитки";

    [Theory]
    [InlineData(@"E:\Users\x\Desktop-Плитки\tiles.json", true)]
    [InlineData(@"E:\Users\x\Desktop-Плитки\tiles.json.tmp", true)]
    [InlineData(@"E:\Users\x\Desktop-Плитки\tiles.log", true)]
    [InlineData(@"E:\Users\x\Desktop-Плитки\tiles.json.broken-20261106-010000", true)]
    [InlineData(@"E:\Users\x\Desktop-Плитки\Игры", false)]
    [InlineData(@"E:\Users\x\Desktop-Плитки\Игры\Steam.lnk", false)]
    [InlineData(@"E:\Users\x\Desktop-Плитки\Игры\tiles.json", false)]
    public void ServiceFiles(string path, bool own) => Assert.Equal(own, FolderStore.IsServiceFile(Root, path));
}
