using System.IO;
using System.IO.Compression;
using ClassicUO.LegionScripting;
using ClassicUO.UnitTests.Fixtures;
using Xunit;
using Legion = ClassicUO.LegionScripting.LegionScripting;

namespace ClassicUO.UnitTests.Game.LegionScript;

[Collection(MainThreadCollection.Name)]
public sealed class ScriptFileReadTests
{
    [Fact]
    public void MissingFile_DoesNotStartTheCachedScript()
    {
        string oldPath = Legion.ScriptPath;
        DirectoryInfo directory = Directory.CreateTempSubdirectory("uo-script-read-");
        try
        {
            Legion.ScriptPath = directory.FullName;
            string path = Path.Combine(directory.FullName, "script.py");
            File.WriteAllText(path, "pass");
            var script = new ScriptFile(null, directory.FullName, "script.py");
            Assert.Equal("pass", script.FileContentsJoined);

            File.Delete(path);
            Assert.False(script.TryReadFromFile(out _, out string error));
            Assert.NotEmpty(error);
            Legion.PlayScript(script);
            Assert.Null(script.ScriptThread);

            File.WriteAllText(path, string.Empty);
            Assert.True(script.TryReadFromFile(out string[] lines, out error));
            Assert.Empty(lines);
            Assert.Empty(error);
            Assert.Empty(script.FileContentsJoined);
        }
        finally
        {
            Legion.ScriptPath = oldPath;
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void MissingArchiveEntry_IsAReadFailure()
    {
        string oldPath = Legion.ScriptPath;
        DirectoryInfo directory = Directory.CreateTempSubdirectory("uo-script-zip-");
        try
        {
            Legion.ScriptPath = directory.FullName;
            string path = Path.Combine(directory.FullName, "scripts.zip");
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("script.py").Open()))
            {
                writer.Write("pass");
            }

            var script = new ZipScriptFile(null, path, "script.py", string.Empty, string.Empty);
            Assert.Equal("pass", script.FileContentsJoined);

            using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
                archive.GetEntry("script.py").Delete();

            Assert.False(script.TryReadFromFile(out _, out string error));
            Assert.NotEmpty(error);
        }
        finally
        {
            Legion.ScriptPath = oldPath;
            directory.Delete(recursive: true);
        }
    }
}
