using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers;

public class OnScreenMessageFilterTests
{
    private const string Absorption = "an Ebony Horror's spell is absorbed by your magical protection!";
    private const string Cooldown = "You must wait to use something again.";

    [Fact]
    public void RepeatFiltersShowFirstNoticeAndUseIndependentWindows()
    {
        var profile = new Profile { ReduceSpellAbsorptionMessages = true, ReduceItemCooldownMessages = true };
        var filter = new OnScreenMessageFilter();

        Assert.False(filter.IsMuted(profile, Absorption, 0));
        Assert.True(filter.IsMuted(profile, Absorption.Replace("Ebony Horror", "Occult Liche"), 500));
        Assert.False(filter.IsMuted(profile, Cooldown, 600));
        Assert.True(filter.IsMuted(profile, Cooldown, 2999));
        Assert.False(filter.IsMuted(profile, Absorption, 3000));
        Assert.True(filter.IsMuted(profile, Cooldown, 3599));
        Assert.False(filter.IsMuted(profile, Cooldown, 3600));
    }

    [Fact]
    public void CustomPhrasesMatchIgnoringCaseOnlyWhenEnabled()
    {
        var profile = new Profile();
        var filter = new OnScreenMessageFilter();

        Assert.False(filter.IsMuted(profile, Absorption, 0));
        Assert.False(filter.IsMuted(profile, Absorption, 1));

        const string words = "I call upon the forces of darkness";
        profile.OnScreenMessagePhrases.Add(words, true);
        Assert.True(filter.IsMuted(profile, "I CALL UPON THE FORCES OF DARKNESS to attend me!", 2));
        Assert.False(filter.IsMuted(profile, "You are poisoned.", 3));

        profile.OnScreenMessagePhrases[words] = false;
        Assert.False(filter.IsMuted(profile, "I call upon the forces of darkness to attend me!", 4));
    }

    [Fact]
    public void SwitchingProfilesClearsRepeatWindows()
    {
        var oldProfile = new Profile { ReduceItemCooldownMessages = true };
        var newProfile = new Profile { ReduceItemCooldownMessages = true };
        var filter = new OnScreenMessageFilter();

        Assert.False(filter.IsMuted(oldProfile, Cooldown, 0));
        Assert.True(filter.IsMuted(oldProfile, Cooldown, 1));
        Assert.False(filter.IsMuted(newProfile, Cooldown, 2));
    }
}
