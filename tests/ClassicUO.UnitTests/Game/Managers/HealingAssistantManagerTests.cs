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
    private const uint Bandage = 0x40000005;

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

    [Theory]
    [InlineData(81, 0u)]
    [InlineData(80, Bandage)]
    [InlineData(61, Bandage)]
    [InlineData(60, GreaterHeal)]
    [InlineData(41, GreaterHeal)]
    [InlineData(40, HalfTamala)]
    [InlineData(26, HalfTamala)]
    [InlineData(25, MagicFish)]
    [InlineData(1, MagicFish)]
    public void EachHpRangeSelectsItsOwnMethodWithBandagesConfiguredFirst(int hits, uint expected)
    {
        var manager = new HealingAssistantManager();
        Profile profile = CreateProfile();
        profile.EnableBandageAgent = true;
        var used = new List<uint>();

        Assert.Equal(expected != 0, manager.TryHeal(profile, hits, 100, 0, Use, hits <= 80, () => Use(Bandage)));
        if (expected == 0)
            Assert.Empty(used);
        else
            Assert.Equal(expected, Assert.Single(used));

        bool Use(uint serial)
        {
            used.Add(serial);
            return true;
        }
    }

    [Fact]
    public void AMethodOnCooldownDoesNotGiveItsRangeToAnotherMethod()
    {
        var manager = new HealingAssistantManager();
        Profile profile = CreateProfile();
        profile.EnableBandageAgent = true;

        Assert.True(manager.TryHeal(profile, 25, 100, 0, serial => serial == MagicFish, true, UnexpectedBandage));
        Assert.False(manager.TryHeal(profile, 25, 100, 2000, _ => throw new System.InvalidOperationException(), true, UnexpectedBandage));
        Assert.True(manager.TryHeal(profile, 40, 100, 2100, serial => serial == HalfTamala, true, UnexpectedBandage));

        static bool UnexpectedBandage() => throw new System.InvalidOperationException();
    }

    [Fact]
    public void MissingStackDoesNotGiveItsRangeToAnotherMethod()
    {
        var manager = new HealingAssistantManager();
        Profile profile = CreateProfile();
        profile.EnableBandageAgent = true;
        profile.HealingAssistantMagicFishSerial = 0;

        Assert.False(manager.TryHeal(profile, 25, 100, 0, _ => throw new System.InvalidOperationException(),
            true, () => throw new System.InvalidOperationException()));
    }

    [Fact]
    public void DisablingAMethodRemovesItsRange()
    {
        var manager = new HealingAssistantManager();
        Profile profile = CreateProfile();
        profile.HealingAssistantUseMagicFish = false;

        Assert.True(manager.TryHeal(profile, 25, 100, 0, serial =>
        {
            Assert.Equal(HalfTamala, serial);
            return true;
        }));
    }

    [Fact]
    public void BandageForPoisonCanStillRunAboveTheConfiguredHealthRanges()
    {
        var manager = new HealingAssistantManager();
        Profile profile = CreateProfile();
        profile.EnableBandageAgent = true;

        Assert.True(manager.TryHeal(profile, 90, 100, 0, _ => throw new System.InvalidOperationException(), true, () => true));
    }

    [Fact]
    public void ChangingFirstChoiceReassignsCutoffsAndOnlyChangesWhichHpRangesTheMethodsOwn()
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
        Assert.True(manager.TryHeal(profile, 80, 100, 0, serial => serial == GreaterHeal));
        Assert.True(manager.TryHeal(profile, 25, 100, 1, serial => serial == MagicFish, true,
            () => throw new System.InvalidOperationException()));
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
    public void FittingEnabledRangesMakesRoomForNewlyEnabledMethods()
    {
        Profile profile = CreateProfile();
        profile.EnableBandageAgent = true;
        profile.BandageAgentHPPercentage = 1;

        HealingAssistantManager.FitEnabledThresholds(profile);

        Assert.Equal(4, profile.BandageAgentHPPercentage);
        Assert.Equal(97, profile.HealingAssistantGreaterHealMissingHPPercentage);
        Assert.Equal(98, profile.HealingAssistantTamalaHealMissingHPPercentage);
        Assert.Equal(99, profile.HealingAssistantMagicFishMissingHPPercentage);
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

    [Theory]
    [InlineData(20, HealingMethod.NaturesTouch)]
    [InlineData(17, HealingMethod.HealSpell)]
    [InlineData(14, HealingMethod.GreaterHealSpell)]
    [InlineData(11, HealingMethod.AngelicAura)]
    [InlineData(8, HealingMethod.SongOfLife)]
    public void SpellRangesPickOnlyTheMatchingSpell(int hits, HealingMethod expected)
    {
        var manager = new HealingAssistantManager();
        var profile = CreateProfile();
        profile.HealingAssistantUseNaturesTouch = true;
        profile.HealingAssistantUseHealSpell = true;
        profile.HealingAssistantUseGreaterHealSpell = true;
        profile.HealingAssistantUseAngelicAura = true;
        profile.HealingAssistantUseSongOfLife = true;
        var casts = new List<HealingMethod>();

        bool TryHeal(int hp, long now) => manager.TryHeal(profile, hp, 100, now,
            _ => throw new System.InvalidOperationException("The spell range must not use an item."),
            tryCast: method =>
            {
                casts.Add(method);
                return true;
            });

        Assert.True(TryHeal(hits, 0));
        Assert.Equal(expected, Assert.Single(casts));
        Assert.False(TryHeal(8, profile.HealingAssistantSpellCooldownMs - 1));
        Assert.True(TryHeal(8, profile.HealingAssistantSpellCooldownMs));
        Assert.Equal(HealingMethod.SongOfLife, casts[1]);
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
