using ClassicUO.Game;
using ClassicUO.Game.UI.MyraWindows;
using ClassicUO.Input;
using Xunit;
using static SDL3.SDL;

namespace ClassicUO.UnitTests.Game.UI;

public class DetachedMapNavigationTests
{
    [Fact]
    public void ClickButtonRemainsUsableOnItsReleaseEvent()
        => Assert.True(DetachedWorldMapWindow.IsMapMouseBindingHeld(MouseButtonType.Left, (byte)MouseButtonType.Left, 0));

    [Fact]
    public void OtherMouseButtonsNeedToBeHeld()
    {
        const byte click = (byte)MouseButtonType.Left;
        Assert.True(DetachedWorldMapWindow.IsMapMouseBindingHeld(MouseButtonType.XButton1, click, SDL_MouseButtonFlags.SDL_BUTTON_X1MASK));
        Assert.False(DetachedWorldMapWindow.IsMapMouseBindingHeld(MouseButtonType.XButton1, click, SDL_MouseButtonFlags.SDL_BUTTON_X2MASK));
    }

    [Fact]
    public void PendingRouteCannotStartAfterTheWalkerIsStopped()
    {
        var pathfinder = new Pathfinder(null);
        int version = pathfinder.AutoWalkVersion;

        Assert.True(pathfinder.CanApplyComputedPath(version, append: false, stillAtStart: true));
        Assert.False(pathfinder.CanApplyComputedPath(version, append: false, stillAtStart: false));
        Assert.False(pathfinder.CanApplyComputedPath(version, append: true, stillAtStart: true));

        pathfinder.StopAutoWalk();

        Assert.False(pathfinder.CanApplyComputedPath(version, append: false, stillAtStart: true));
        Assert.False(pathfinder.CanApplyComputedPath(version, append: true, stillAtStart: true));
    }
}
