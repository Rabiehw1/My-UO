#nullable enable

using System;
using System.IO;
using System.Text;
using ClassicUO.Configuration;
using ClassicUO.LegionScripting;

namespace ClassicUO.Game.Managers.Trainer;

/// <summary>
/// A built-in, read-only training script, read from the client's embedded resources each run so updates replace it.
/// Its <c>TARGET = 0</c> line is filled with the picked target. Never added to the script library.
/// </summary>
internal sealed class TrainerScriptFile : ScriptFile
{
    #region Private members

    private const string RESOURCE_PREFIX = "Trainer/";
    private const string TARGET_LINE = "TARGET = 0";

    private readonly string _resource;

    #endregion

    #region Public accessors

    /// <summary>Target written into the script on its next run; 0 for none.</summary>
    public uint TargetSerial { get; set; }

    #endregion

    #region Ctor

    /// <summary>Creates the script for a skill. Needs the scripting system started (its script folder known).</summary>
    /// <param name="world">Game world the script runs in.</param>
    /// <param name="skill">Skill whose script to run.</param>
    public TrainerScriptFile(World world, TrainerSkill skill)
        : base(world, LegionScripting.LegionScripting.ScriptPath, $"Trainer - {skill.Name}.py", readSource: false)
    {
        _resource = RESOURCE_PREFIX + skill.Id + ".py";
        FullPath = "builtin::" + _resource;
        Group = "Trainer";
        ReadFromFile();
    }

    #endregion

    #region Public methods

    /// <inheritdoc />
    public override bool FileExists() => true;

    /// <summary>Built-in scripts are read-only; saving is refused.</summary>
    /// <param name="contents">Ignored.</param>
    public override void OverrideFileContents(string contents) =>
        GameActions.Print(World, TazLang.Get("trainer_script_readonly"), Constants.HUE_WARN);

    #endregion

    #region Protected methods

    /// <inheritdoc />
    protected override string[] ReadSourceLines()
    {
        using Stream stream = typeof(TrainerScriptFile).Assembly.GetManifestResourceStream(_resource)
            ?? throw new FileNotFoundException($"Built-in script '{_resource}' is missing.", _resource);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string[] lines = reader.ReadToEnd().Replace("\r\n", "\n").Split('\n');

        for (int i = 0; i < lines.Length; i++)
            if (lines[i].StartsWith(TARGET_LINE, StringComparison.Ordinal))
                lines[i] = $"TARGET = 0x{TargetSerial:X8}";

        return lines;
    }

    #endregion
}
