using Tiles.Core;

namespace Tiles.Tests;

public class FolderNamerTests
{
    private static string? Name(params (string name, string target)[] items) => FolderNamer.Suggest(items);

    [Fact]
    public void Games_BySteamAndEpic()
    {
        Assert.Equal("Игры", Name(
            ("Deadlock", "steam://rungameid/1422450"),
            ("WARDOGS", @"E:\Steam\steamapps\common\Wardogs\Wardogs.exe"),
            ("Fortnite", "com.epicgames.launcher://apps/fn")));
    }

    [Fact]
    public void Music()
    {
        Assert.Equal("Музыка", Name(
            ("FL Studio 2025", @"E:\Program Files\Image-Line\FL Studio 2025\FL64.exe"),
            ("Spotify", @"C:\Users\user\AppData\Roaming\Spotify\Spotify.exe")));
    }

    [Fact]
    public void Work_FromYourScreenshot()
    {
        // два своих приложения без признаков + TickTick + Obsidian
        Assert.Equal("Работа", Name(
            ("Проверка турникета", @"E:\dev\turnstile\build\app.exe"),
            ("РХГА Демо", @"E:\dev\rhga\build\demo.exe"),
            ("TickTick", @"E:\Program Files\TickTick\TickTick.exe"),
            ("Obsidian", @"C:\Users\user\AppData\Local\Programs\Obsidian\Obsidian.exe")));
    }

    [Fact]
    public void Majority_Wins()
    {
        Assert.Equal("Общение", Name(
            ("Telegram", @"E:\Telegram\Telegram.exe"),
            ("Discord", @"C:\Users\user\AppData\Local\Discord\Update.exe"),
            ("Google Chrome", @"E:\Program Files\Google\Chrome\Application\chrome.exe")));
    }

    [Fact]
    public void AllDifferent_IsRaznoe()
    {
        Assert.Equal("Разное", Name(
            ("Telegram", @"E:\Telegram\Telegram.exe"),
            ("Spotify", @"E:\Spotify\Spotify.exe"),
            ("Steam", @"E:\Steam\steam.exe"),
            ("DaVinci Resolve", @"E:\Program Files\Blackmagic Design\DaVinci Resolve\Resolve.exe")));
    }

    [Fact]
    public void Unknown_OrEmpty_GivesNull()
    {
        Assert.Null(Name());
        Assert.Null(Name(("Проверка турникета", @"E:\dev\turnstile\app.exe")));
    }
}
