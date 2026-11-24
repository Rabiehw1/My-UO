#nullable enable

using System;
using System.IO;
using ClassicUO.Game.Managers.Updates;
using FluentAssertions;
using Xunit;

namespace ClassicUO.UnitTests.Game.Managers.Updates;

public sealed class UpdateInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "UpdateInstallerTests_" + Guid.NewGuid().ToString("N"));
    private readonly string _release;
    private readonly string _install;

    public UpdateInstallerTests()
    {
        _release = Path.Combine(_root, "release");
        _install = Path.Combine(_root, "install");
        Directory.CreateDirectory(_release);
        Directory.CreateDirectory(_install);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    [Fact]
    public void ReplaceFiles_ExistingProgramFile_IsReplacedAndOldCopyKeptAsBackup()
    {
        Write(_install, "LordBritishKW.dll", "old");
        Write(_release, "LordBritishKW.dll", "new");

        UpdateInstaller.ReplaceFiles(_release, _install);

        Read(_install, "LordBritishKW.dll").Should().Be("new");
        Read(_install, "LordBritishKW.dll" + UpdateInstaller.BACKUP_SUFFIX).Should().Be("old");
    }

    [Fact]
    public void ReplaceFiles_NewFileInSubfolder_IsAdded()
    {
        Write(_release, Path.Combine("iplib", "os.py"), "new");

        UpdateInstaller.ReplaceFiles(_release, _install);

        Read(_install, Path.Combine("iplib", "os.py")).Should().Be("new");
    }

    [Theory]
    [InlineData("settings.json")]
    [InlineData("Data/Profiles/account/profile.json")]
    [InlineData("LegionScripts/my_script.py")]
    public void ReplaceFiles_PlayerFile_IsNotOverwritten(string relativePath)
    {
        Write(_install, relativePath, "mine");
        Write(_release, relativePath, "shipped");

        UpdateInstaller.ReplaceFiles(_release, _install);

        Read(_install, relativePath).Should().Be("mine");
        File.Exists(Path.Combine(_install, relativePath + UpdateInstaller.BACKUP_SUFFIX)).Should().BeFalse();
    }

    [Fact]
    public void ReplaceFiles_FolderNamedLikePreservedFolder_IsStillUpdated()
    {
        Write(_install, Path.Combine("DataTools", "tool.dll"), "old");
        Write(_release, Path.Combine("DataTools", "tool.dll"), "new");

        UpdateInstaller.ReplaceFiles(_release, _install);

        Read(_install, Path.Combine("DataTools", "tool.dll")).Should().Be("new");
    }

    [Fact]
    public void DeleteBackups_RemovesOnlyBackupFiles()
    {
        Write(_install, "LordBritishKW.dll", "new");
        Write(_install, "LordBritishKW.dll" + UpdateInstaller.BACKUP_SUFFIX, "old");
        Write(_install, Path.Combine("lib", "native.so" + UpdateInstaller.BACKUP_SUFFIX), "old");

        UpdateInstaller.DeleteBackups(_install);

        File.Exists(Path.Combine(_install, "LordBritishKW.dll")).Should().BeTrue();
        Directory.GetFiles(_install, "*" + UpdateInstaller.BACKUP_SUFFIX, SearchOption.AllDirectories).Should().BeEmpty();
    }

    private static void Write(string directory, string relativePath, string content)
    {
        string path = Path.Combine(directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Read(string directory, string relativePath) => File.ReadAllText(Path.Combine(directory, relativePath));
}
