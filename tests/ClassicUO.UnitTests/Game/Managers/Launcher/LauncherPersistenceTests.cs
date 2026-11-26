#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Managers.Launcher;
using ClassicUO.Game.Scenes;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers.Launcher;

public class LauncherPersistenceTests
{
    [Fact]
    public void SavingAStaleLauncherViewPreservesIndependentProfileEditsAndAdditions()
    {
        var baseline = new LauncherSave
        {
            SelectedId = "main",
            Profiles = [ new LauncherProfile { Id = "main", Name = "Main", Account = "old account" } ]
        };
        var edited = new LauncherSave
        {
            SelectedId = "main", KeepOpenAfterPlay = false,
            Profiles = [ new LauncherProfile { Id = "main", Name = "Renamed", Account = "old account" } ]
        };
        var latest = new LauncherSave
        {
            SelectedId = "helper", OpenOnStart = false,
            Profiles =
            [
                new LauncherProfile { Id = "main", Name = "Main", Account = "new account" },
                new LauncherProfile { Id = "helper", Name = "Helper" }
            ]
        };

        LauncherSave saved = LauncherSave.Merge(baseline, edited, latest);

        Assert.Equal("helper", saved.SelectedId);
        Assert.False(saved.OpenOnStart);
        Assert.False(saved.KeepOpenAfterPlay);
        Assert.Equal(2, saved.Profiles.Count);
        Assert.Equal("Renamed", saved.Profiles[0].Name);
        Assert.Equal("new account", saved.Profiles[0].Account);
    }

    [Fact]
    public void DeletionInOneLauncherIsNotRevertedByAnEditInAnother()
    {
        var baseline = new LauncherSave { Profiles = [ new LauncherProfile { Id = "deleted" } ] };
        var edited = new LauncherSave { SelectedId = "deleted", Profiles = [ new LauncherProfile { Id = "deleted", Name = "Old tab" } ] };
        var latest = new LauncherSave { Profiles = [ new LauncherProfile { Id = "remaining" } ] };

        LauncherSave saved = LauncherSave.Merge(baseline, edited, latest);

        Assert.Equal("remaining", saved.SelectedId);
        Assert.Equal("remaining", Assert.Single(saved.Profiles).Id);
    }

    [Fact]
    public async Task IndependentLastCharacterWritesAreMergedUnderTheFileLock()
    {
        string root = Path.Combine(Path.GetTempPath(), $"last-character-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "lastcharacter.json");
        try
        {
            await Task.WhenAll(
                Task.Run(() => LastCharacterManager.SaveAt(path, "first", "Shard", "First Character")),
                Task.Run(() => LastCharacterManager.SaveAt(path, "second", "Shard", "Second Character")));

            List<LastCharacterInfo>? saved = JsonSerializer.Deserialize(
                File.ReadAllText(path), LastCharacterJsonContext.Default.ListLastCharacterInfo);
            Assert.NotNull(saved);
            Assert.Equal(2, saved.Count);
            Assert.Contains(saved, item => item.AccountName == "first" && item.LastCharacterName == "First Character");
            Assert.Contains(saved, item => item.AccountName == "second" && item.LastCharacterName == "Second Character");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("Missing", true, -1)]
    [InlineData("Missing", false, 1)]
    [InlineData("", false, 1)]
    [InlineData("other", true, 2)]
    public void PreferredCharacterMustExistAndFallbackSkipsEmptySlots(string preferred, bool matchRequired, int expected)
    {
        Assert.Equal((expected, true), LoginScene.FindAutomaticCharacter(["", "First", "Other"], preferred, matchRequired));
        Assert.Equal((-1, false), LoginScene.FindAutomaticCharacter(["", ""], preferred, matchRequired));
    }
}
