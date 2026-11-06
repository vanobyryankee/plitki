using Tiles.Core;
using Xunit;

public class ScreenLayoutTests
{
    const string Home = "0,-960,1707x960;0,0,1707x906";
    const string Game = "0,-960,1707x960;0,0,1280x666";

    [Fact] public void GameResolution_IsShrunk() => Assert.True(ScreenLayout.IsShrunk(Home, Game));
    [Fact] public void BackTo2K_IsNotShrunk() => Assert.False(ScreenLayout.IsShrunk(Game, Home));
    [Fact] public void SameLayout_IsNotShrunk() => Assert.False(ScreenLayout.IsShrunk(Home, Home));
    [Fact] public void MonitorRemoved_IsNotShrunk() => Assert.False(ScreenLayout.IsShrunk(Home, "0,0,1707x906"));
    [Fact] public void Garbage_IsNotShrunk() => Assert.False(ScreenLayout.IsShrunk("", Game));

    [Fact] public void StartDuringGame_KeepsSavedHome() => Assert.Equal(Home, ScreenLayout.HomeOnStart(Home, Game));
    [Fact] public void StartAt2K_AfterBadSavedHome_Takes2K() => Assert.Equal(Home, ScreenLayout.HomeOnStart(Game, Home));
    [Fact] public void NoSaved_TakesCurrent() => Assert.Equal(Game, ScreenLayout.HomeOnStart(null, Game));
}
