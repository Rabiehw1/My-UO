#nullable enable

using System.Collections.Generic;

namespace ClassicUO.Game.Managers.Trainer;

/// <summary>A target picked for a skill, remembered per character.</summary>
public sealed class TrainerTarget
{
    public uint Serial { get; set; }

    /// <summary>Name when picked, for messages once it is gone.</summary>
    public string Name { get; set; } = "";
}

/// <summary>The Trainer page's per-character settings, saved with the profile.</summary>
public sealed class TrainerSettings
{
    /// <summary>Seconds between skill uses; the shard shares one cooldown across all skills.</summary>
    public int WaitSeconds { get; set; } = TrainerManager.DEFAULT_WAIT_SECONDS;

    /// <summary>Skill IDs ticked for training.</summary>
    public List<string> Selected { get; set; } = [];

    public uint SmeltTool { get; set; }
    public uint DragonLeather { get; set; }
    public uint UnstitchTool { get; set; }
    public string TailorOutfit { get; set; } = "";
    public uint Forge { get; set; }

    /// <summary>Picked targets by skill ID.</summary>
    public Dictionary<string, TrainerTarget> Targets { get; set; } = new();
}
