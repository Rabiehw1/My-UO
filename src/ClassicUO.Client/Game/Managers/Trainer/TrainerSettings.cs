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
    public uint UnstitchTool { get; set; }
    public List<TrainerTarget> BeggingVendors { get; set; } = [];
    public uint HerdingTool { get; set; }
    public HerdingTile? HerdingDestination { get; set; }
    public TrainerTarget? ProvokeOpponent { get; set; }
    public sealed record HerdingTile(int Facet, ushort X, ushort Y, short Z, ushort Graphic);
    public uint Forge { get; set; }

    /// <summary>Picked targets by skill ID.</summary>
    public Dictionary<string, TrainerTarget> Targets { get; set; } = new();
}
