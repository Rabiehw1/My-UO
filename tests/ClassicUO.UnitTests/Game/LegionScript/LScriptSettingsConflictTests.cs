#nullable enable
using System;
using System.IO;
using ClassicUO.Configuration;
using ClassicUO.LegionScripting;
using ClassicUO.UnitTests.Fixtures;
using Xunit;

namespace ClassicUO.UnitTests.Game.LegionScript;

[Collection(CorruptFileReportCollection.Name)]
public sealed class LScriptSettingsConflictTests : IDisposable
{
    private const string First = "account/shard/First";
    private const string Second = "account/shard/Second";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"legion-save-tests-{Guid.NewGuid():N}");
    private string FilePath => Path.Combine(_directory, "lscript.json");

    [Fact]
    public void UnchangedSessionCanLogOutAfterAnotherCharacterSavesAnEmptyAutorun()
    {
        var session = Open();
        var other = Open();
        other.AutoRunScriptPaths[Second] = "";
        other.Save();
        byte[] disk = File.ReadAllBytes(FilePath);
        JsonSaveConflictHandler.Prompt = _ => Assert.Fail("An unchanged session must not prompt on logout.");

        session.Save();

        Assert.Equal(disk, File.ReadAllBytes(FilePath));
        Assert.Equal("", Open().AutoRunScriptPaths[Second]);
    }

    [Fact]
    public void TwoCharactersCanSaveTheirAutorunsAndSequencesIndependently()
    {
        var session = Open();
        var other = Open();
        JsonSaveConflictHandler.Prompt = _ => Assert.Fail("Edits to separate characters should merge.");

        other.AutoRunScriptPaths[Second] = "second.py";
        other.ScriptSequences[Second] = ["other.py"];
        other.Save();
        session.AutoRunScriptPaths[First] = "first.py";
        session.ScriptSequences[First] = ["before.py", "after.py"];
        session.GroupCollapsed["Helpers"] = true;
        session.Save();

        var saved = Open();
        Assert.Equal("first.py", saved.AutoRunScriptPaths[First]);
        Assert.Equal("second.py", saved.AutoRunScriptPaths[Second]);
        Assert.Equal(new[] { "before.py", "after.py" }, saved.ScriptSequences[First]);
        Assert.Equal(new[] { "other.py" }, saved.ScriptSequences[Second]);
        Assert.True(saved.GroupCollapsed["Helpers"]);
        Assert.Equal("second.py", session.AutoRunScriptPaths[Second]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChoosingOneOverlappingEditPreservesIndependentChanges(bool keepSession)
    {
        var session = Open();
        var other = Open();
        JsonSaveConflict? conflict = null;
        JsonSaveConflictHandler.Prompt = raised => conflict = raised;

        other.ScriptSequences[First] = ["other-a.py", "other-b.py"];
        other.AutoRunScriptPaths[Second] = "other.py";
        other.Save();
        session.ScriptSequences[First] = ["mine-a.py", "mine-b.py"];
        session.DisableModuleCache = true;
        session.Save();

        Assert.NotNull(conflict);
        Assert.All(conflict.Changes, change => Assert.StartsWith($"$.ScriptSequences.{First}", change.Path));
        Assert.NotEmpty(conflict.Changes);
        conflict.Resolve(keepSession);

        var saved = Open();
        string[] expected = keepSession ? ["mine-a.py", "mine-b.py"] : ["other-a.py", "other-b.py"];
        Assert.Equal(expected, saved.ScriptSequences[First]);
        Assert.Equal(expected, session.ScriptSequences[First]);
        Assert.Equal("other.py", saved.AutoRunScriptPaths[Second]);
        Assert.True(saved.DisableModuleCache);

        JsonSaveConflictHandler.Prompt = _ => Assert.Fail("A resolved setting should not prompt again on logout.");
        session.Save();
    }

    [Fact]
    public void ANewOverlappingDiskEditRequiresASeparateChoice()
    {
        var session = Open();
        var other = Open();
        JsonSaveConflict? conflict = null;
        JsonSaveConflictHandler.Prompt = raised => conflict = raised;

        session.AutoRunScriptPaths[First] = "mine.py";
        other.AutoRunScriptPaths[First] = "theirs.py";
        other.Save();
        session.Save();
        Assert.NotNull(conflict);
        var firstPrompt = conflict;

        other.AutoRunScriptPaths[First] = "new.py";
        other.Save();
        firstPrompt.Resolve(true);

        Assert.NotSame(firstPrompt, conflict);
        Assert.Equal("new.py", Open().AutoRunScriptPaths[First]);
        conflict!.Resolve(true);
        Assert.Equal("mine.py", Open().AutoRunScriptPaths[First]);
    }

    private LScriptSettings Open() => TestSettings.Open(FilePath);

    private sealed class TestSettings : LScriptSettings
    {
        internal static LScriptSettings Open(string path) => LoadFrom(path);
    }

    public void Dispose()
    {
        JsonSaveConflictHandler.Prompt = null;
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
