using Tiles.Core;

namespace Tiles.Tests;

public class MusicSourceTests
{
    [Theory]
    [InlineData("ru.yandex.desktop.music", Music.Source.Music)]
    [InlineData("Spotify.exe", Music.Source.Music)]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", Music.Source.Music)]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", Music.Source.Music)]
    [InlineData("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", Music.Source.Music)]
    [InlineData("AIMP.exe", Music.Source.Music)]
    [InlineData("com.vk.music", Music.Source.Music)]
    [InlineData("DC1DE74E6830E8D8", Music.Source.Browser)]          // Zen/Firefox: код вместо имени
    [InlineData("308046B0AF4A39CB", Music.Source.Browser)]
    [InlineData("Chrome", Music.Source.Browser)]
    [InlineData("MSEdge", Music.Source.Browser)]
    [InlineData("opera.exe", Music.Source.Browser)]
    [InlineData("Yandex.6KZIA7DHP2Y2YFIRJXFAUXRQOE", Music.Source.Browser)] // Яндекс Браузер, не Музыка
    [InlineData("vlc.exe", Music.Source.Video)]
    [InlineData("Microsoft.ZuneVideo_8wekyb3d8bbwe!Microsoft.ZuneVideo", Music.Source.Video)]
    public void Kind_ByApp(string app, Music.Source want) => Assert.Equal(want, Music.Kind(app));

    [Fact]
    public void Pick_SkipsYouTubeInBrowser()
    {
        var s = new[] { ("DC1DE74E6830E8D8", true), ("ru.yandex.desktop.music", false) };
        Assert.Equal(1, Music.Pick(s, current: "DC1DE74E6830E8D8", browsers: false));
    }

    [Fact]
    public void Pick_OnlyBrowser_NothingUnlessAllowed()
    {
        var s = new[] { ("DC1DE74E6830E8D8", true) };
        Assert.Equal(-1, Music.Pick(s, "DC1DE74E6830E8D8", browsers: false));
        Assert.Equal(0, Music.Pick(s, "DC1DE74E6830E8D8", browsers: true));
    }

    [Fact]
    public void Pick_PrefersPlayingMusic()
    {
        var s = new[] { ("ru.yandex.desktop.music", false), ("Spotify.exe", true) };
        Assert.Equal(1, Music.Pick(s, "ru.yandex.desktop.music", false));
    }
}
