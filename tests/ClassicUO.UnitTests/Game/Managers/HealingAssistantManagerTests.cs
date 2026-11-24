using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers;

public class HealingAssistantManagerTests
{
    private const uint GreaterHeal = 0x40000001;
    private const uint FullTamala = 0x40000002;
    private const uint HalfTamala = 0x40000003;
    private const uint MagicFish = 0x40000004;

    [Theory]
    [InlineData(61, 0u)]
    [InlineData(60, GreaterHeal)]
    [InlineData(40, HalfTamala)]
    [InlineData(25, MagicFish)]
    [InlineData(100, 0u)]
    [InlineData(0, 0u)]
    public void UsesOnlyTheHighestMatchingSerialEvenWithoutCachedStacks(int hits, uint expected)
    {
        var manager = new HealingAssistantManager();
        var used = new List<uint>();

        Assert.Equal(expected != 0, manager.TryHeal(CreateProfile(), hits, 100, 0, serial =>
        {
            used.Add(serial);
            return true;
        }, isKnownStack: _ => false));

        if (expected == 0)
            Assert.Empty(used);
        else
            Assert.Equal(expected, Assert.Single(used));
    }

    [Fact]
    public void TriesBandagesFirstEvenAtCriticalHealthThenRechecksHealthForBackup()
    {
        var manager = new HealingAssistantManager();
        Profile profile = CreateProfile();
        profile.EnableBandageAgent = true;

        Assert.True(manager.TryHeal(profile, 25, 100, 0, _ => throw new System.InvalidOperationException(), true, () => true));
        Assert.True(manager.TryHeal(profile, 40, 100, 4700, serial =>
        {
            Assert.Equal(HalfTamala, serial);
            return true;
        }, true, () => throw new System.InvalidOperationException()));
        Assert.True(manager.TryHeal(profile, 40, 100, 5500, _ => throw new System.InvalidOperationException(), true, () => true));
    }

    [Fact]
    public void UsesAnEligibleBackupWhenBandagesCannotStart()
    {
        var manager = new HealingAssistantManager();
        Profile profile = CreateProfile();
        profile.EnableBandageAgent = true;

        Assert.True(manager.TryHeal(profile, 60, 100, 0, serial =>
        {
            Assert.Equal(GreaterHeal, serial);
            return true;
        }, true, () => false));
    }

    [Fact]
    public void RecoveryRestoresTheFirstChoice()
    {
        var manager = new HealingAssistantManager();
        Profile profile = CreateProfile();
        profile.EnableBandageAgent = true;

        Assert.True(manager.TryHeal(profile, 70, 100, 0, _ => throw new System.InvalidOperationException(), true, () => true));
        Assert.False(manager.TryHeal(profile, 100, 100, 4700, _ => throw new System.InvalidOperationException()));
        Assert.True(manager.TryHeal(profile, 25, 100, 5500, _ => throw new System.InvalidOperationException(), true, () => true));
    }

    [Fact]
    public void ChangingFirstChoiceReassignsCutoffsAndGivesTheNewChoicePriority()
    {
        var manager = new HealingAssistantManager();
        Profile profile = CreateProfile();
        profile.EnableBandageAgent = true;
        profile.BandageAgentHPPercentage = 85;
        profile.HealingAssistantGreaterHealMissingHPPercentage = 35;

        HealingAssistantManager.ChangeFirstMethod(profile, HealingMethod.GreaterHeal);

        Assert.Equal(15, profile.HealingAssistantGreaterHealMissingHPPercentage);
        Assert.Equal(65, profile.BandageAgentHPPercentage);
        Assert.Equal(60, profile.HealingAssistantTamalaHealMissingHPPercentage);
        Assert.Equal(75, profile.HealingAssistantMagicFishMissingHPPercentage);
        Assert.True(manager.TryHeal(profile, 25, 100, 0, serial =>
        {
            Assert.Equal(GreaterHeal, serial);
            return true;
        }, true, () => throw new System.InvalidOperationException()));
    }

    [Fact]
    public void ChangingFirstChoiceMakesRoomForAllThresholds()
    {
        Profile profile = CreateProfile();
        profile.BandageAgentHPPercentage = 1;
        profile.HealingAssistantGreaterHealMissingHPPercentage = 99;
        profile.HealingAssistantTamalaHealMissingHPPercentage = 99;
        profile.HealingAssistantMagicFishMissingHPPercentage = 99;

        HealingAssistantManager.ChangeFirstMethod(profile, HealingMethod.MagicFish);

        Assert.Equal(96, profile.HealingAssistantMagicFishMissingHPPercentage);
        Assert.Equal(3, profile.BandageAgentHPPercentage);
        Assert.Equal(98, profile.HealingAssistantGreaterHealMissingHPPercentage);
        Assert.Equal(99, profile.HealingAssistantTamalaHealMissingHPPercentage);
    }

    [Fact]
    public void BothPotionsWaitForTheSharedCooldown()
    {
        var manager = new HealingAssistantManager();
        Profile profile = CreateProfile();

        Assert.True(manager.TryHeal(profile, 60, 100, 1000, serial => serial == GreaterHeal));
        Assert.False(manager.TryHeal(profile, 40, 100, 21_499, _ => throw new System.InvalidOperationException()));
        Assert.True(manager.TryHeal(profile, 40, 100, 21_500, serial => serial == HalfTamala));
    }

    [Fact]
    public void FishHasItsOwnConfigurableCooldown()
    {
        var manager = new HealingAssistantManager();
        Profile profile = CreateProfile();
        profile.HealingAssistantFishCooldownMs = 25_000;

        Assert.True(manager.TryHeal(profile, 25, 100, 0, serial => serial == MagicFish));
        Assert.True(manager.TryHeal(profile, 40, 100, 1000, serial => serial == HalfTamala));
        profile.HealingAssistantUseGreaterHeal = false;
        profile.HealingAssistantUseTamalaHeal = false;
        Assert.False(manager.TryHeal(profile, 25, 100, 24_999, _ => throw new System.InvalidOperationException()));
        Assert.True(manager.TryHeal(profile, 25, 100, 25_000, serial => serial == MagicFish));
    }

    [Fact]
    public void UsesFullTamalaWhenTheHalfFullStackIsMissing()
    {
        var manager = new HealingAssistantManager();
        var checkedSerials = new List<uint>();

        Assert.True(manager.TryHeal(CreateProfile(), 40, 100, 0, serial =>
        {
            checkedSerials.Add(serial);
            return serial == FullTamala;
        }));
        Assert.Equal([HalfTamala, FullTamala], checkedSerials);
    }

    [Theory]
    [InlineData(true, HalfTamala)]
    [InlineData(false, FullTamala)]
    public void PrefersTheHalfFullTamalaUnlessOnlyTheFullStackIsKnown(bool halfFullIsKnown, uint expected)
    {
        var manager = new HealingAssistantManager();
        var attempted = new List<uint>();

        Assert.True(manager.TryHeal(CreateProfile(), 40, 100, 0, serial =>
        {
            attempted.Add(serial);
            return true;
        }, isKnownStack: serial => serial == FullTamala || (serial == HalfTamala && halfFullIsKnown)));
        Assert.Equal(expected, Assert.Single(attempted));
    }

    [Fact]
    public void MissingOrInvalidStacksDoNotStartACooldown()
    {
        var manager = new HealingAssistantManager();
        Profile profile = CreateProfile();
        profile.HealingAssistantGreaterHealSerial = 42;

        Assert.False(manager.TryHeal(profile, 40, 100, 0, serial =>
        {
            Assert.NotEqual(42u, serial);
            return false;
        }));
        Assert.True(manager.TryHeal(profile, 40, 100, 100, serial => serial == FullTamala));
    }

    [Fact]
    public void TheMasterSwitchSuppressesConsumables()
    {
        var manager = new HealingAssistantManager();
        Profile profile = CreateProfile();
        profile.EnableHealingAssistant = false;

        Assert.False(manager.TryHeal(profile, 25, 100, 0, _ => throw new System.InvalidOperationException()));
    }

    private static Profile CreateProfile() => new()
    {
        HealingAssistantUseGreaterHeal = true,
        HealingAssistantGreaterHealSerial = GreaterHeal,
        HealingAssistantUseTamalaHeal = true,
        HealingAssistantFullTamalaSerial = FullTamala,
        HealingAssistantHalfTamalaSerial = HalfTamala,
        HealingAssistantUseMagicFish = true,
        HealingAssistantMagicFishSerial = MagicFish
    };
}
