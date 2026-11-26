using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using ClassicUO.Game;
using ClassicUO.Utility.Logging;
using IronPython.Hosting;

namespace ClassicUO.LegionScripting;

public class ZipScriptFile : ScriptFile
{
    public string ZipPath;
    public string EntryPath;

    public ZipScriptFile(World world, string zipPath, string entryPath, string group, string subGroup)
        : base(world, LegionScripting.ScriptPath, System.IO.Path.GetFileName(entryPath), readSource: false)
    {
        ZipPath = zipPath;
        EntryPath = entryPath;
        Group = group;
        SubGroup = subGroup;
        FullPath = $"{zipPath}::{entryPath}";
        ReadFromFile();
    }

    public override bool FileExists()
    {
        if (!File.Exists(ZipPath)) return false;
        try
        {
            using var archive = ZipFile.OpenRead(ZipPath);
            return archive.GetEntry(EntryPath) != null;
        }
        catch { return false; }
    }

    protected override string[] ReadSourceLines()
    {
        using var archive = ZipFile.OpenRead(ZipPath);
        ZipArchiveEntry entry = archive.GetEntry(EntryPath)
            ?? throw new FileNotFoundException($"Archive entry '{EntryPath}' does not exist.", EntryPath);
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd().Split('\n');
    }

    public override void OverrideFileContents(string contents)
    {
        try
        {
            using var archive = ZipFile.Open(ZipPath, ZipArchiveMode.Update);
            archive.GetEntry(EntryPath)?.Delete();
            ZipArchiveEntry newEntry = archive.CreateEntry(EntryPath);
            using var writer = new StreamWriter(newEntry.Open(), Encoding.UTF8);
            writer.Write(contents);

            GameActions.Print(World, $"Saved {FileName}.");
        }
        catch (Exception ex)
        {
            GameActions.Print(World, ex.ToString());
        }
    }

    public override void SetupPythonEngine()
    {
        if (PythonEngine != null && !LegionScripting.LScriptSettings.DisableModuleCache)
            return;

        PythonEngine = Python.CreateEngine(new Dictionary<string, object>() { { "RecursionLimit", 100 } });

        ICollection<string> paths = PythonEngine.GetSearchPaths();
        paths.Add(System.IO.Path.Combine(CUOEnviroment.ExecutablePath, "iplib"));
        paths.Add(System.IO.Path.Combine(CUOEnviroment.ExecutablePath, "LegionScripts"));
        paths.Add(ZipPath);
        paths.Add(System.IO.Path.GetDirectoryName(ZipPath) ?? Environment.CurrentDirectory);

        PythonEngine.SetSearchPaths(paths);
    }
}
