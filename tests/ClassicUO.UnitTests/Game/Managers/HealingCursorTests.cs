using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers;

public sealed class HealingCursorTests
{
    [Theory]
    [InlineData(TargetType.Beneficial)]
    [InlineData(TargetType.Neutral)]
    public void QuickHealWaitsForOneCompatibleObjectCursor(TargetType acceptedType)
    {
        var party = new PartyManager(null);
        const uint character = 0x1234;

        party.BeginHealCast(character);

        Assert.Equal(0u, party.TakeHealCursor(CursorTarget.Position, TargetType.Beneficial));
        Assert.Equal(0u, party.TakeHealCursor(CursorTarget.Object, TargetType.Harmful));
        Assert.Equal(0u, party.TakeHealCursor(CursorTarget.Object, TargetType.Cancel));
        Assert.Equal(0u, party.TakeHealCursor(CursorTarget.CallbackTarget, TargetType.Neutral));
        Assert.True(party.HasPendingHealCursor);
        Assert.Equal(character, party.TakeHealCursor(CursorTarget.Object, acceptedType));
        Assert.Equal(0u, party.TakeHealCursor(CursorTarget.Object, acceptedType));
    }

    [Fact]
    public void QueuedQuickHealWaitsForCastBeforeAnsweringACursor()
    {
        var party = new PartyManager(null);

        Assert.True(party.TryReserveHealCast(out long requestId));
        Assert.True(party.IsReservedHealCast(requestId));
        Assert.True(party.HasPendingHealCursor);
        Assert.False(party.TryReserveHealCast(out _));
        Assert.Equal(0u, party.TakeHealCursor(CursorTarget.Object, TargetType.Beneficial));

        party.BeginHealCast(0x1234);

        Assert.False(party.IsReservedHealCast(requestId));
        Assert.Equal(0x1234u, party.TakeHealCursor(CursorTarget.Object, TargetType.Beneficial));
    }

    [Fact]
    public void AreaQuickHealAllowsABeneficialPositionCursor()
    {
        var party = new PartyManager(null);
        party.BeginHealCast(0x1234, allowPosition: true);

        Assert.Equal(0u, party.TakeHealCursor(CursorTarget.Position, TargetType.Harmful));
        Assert.Equal(0x1234u, party.TakeHealCursor(CursorTarget.Position, TargetType.Beneficial));
    }

    [Fact]
    public void PendingAutoTargetRemainsAvailableAfterUnrelatedCursors()
    {
        var request = new AutoTargetInfo();
        request.Set(0x1234, TargetType.Beneficial);

        Assert.False(request.CanAnswer(CursorTarget.Object, TargetType.Harmful));
        Assert.False(request.CanAnswer(CursorTarget.Object, TargetType.Neutral));
        Assert.False(request.CanAnswer(CursorTarget.Object, TargetType.Cancel));
        Assert.False(request.CanAnswer(CursorTarget.MultiPlacement, TargetType.Beneficial));
        Assert.True(request.CanAnswer(CursorTarget.Object, TargetType.Beneficial));
    }

    [Fact]
    public void AutoTargetAnyTypeStillRequiresALiveServerCursor()
    {
        var request = new AutoTargetInfo();
        request.Set(0x1234, TargetType.Neutral, matchAnyTargetType: true);

        Assert.False(request.CanAnswer(CursorTarget.Object, TargetType.Cancel));
        Assert.False(request.CanAnswer(CursorTarget.CallbackTarget, TargetType.Neutral));
        Assert.True(request.CanAnswer(CursorTarget.Position, TargetType.Neutral));
    }
}
