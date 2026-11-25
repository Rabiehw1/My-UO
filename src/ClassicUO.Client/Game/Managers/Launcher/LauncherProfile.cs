#nullable enable

using System;

namespace ClassicUO.Game.Managers.Launcher;

/// <summary>
/// One character setup the launcher can start: which game files, which shard, which account and character.
/// </summary>
/// <remarks>Each profile gets its own client settings file, so clients started together don't overwrite each other.</remarks>
internal sealed class LauncherProfile
{
    /// <summary>Stable key for the profile's settings file and its running check; never shown or edited.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "";

    /// <summary>Folder holding the Ultima Online data files.</summary>
    public string UltimaOnlineDirectory { get; set; } = "";

    public string ClientVersion { get; set; } = "";

    /// <summary>Server name picked on the server list; empty picks the first.</summary>
    public string ShardName { get; set; } = "";

    public string Address { get; set; } = "";

    public ushort Port { get; set; } = 2593;

    public string Account { get; set; } = "";

    /// <summary>Password as stored in client settings (encrypted with <c>Crypter</c>), never plain text.</summary>
    public string EncryptedPassword { get; set; } = "";

    /// <summary>Character to enter the game with; empty uses the last one played on the account.</summary>
    public string Character { get; set; } = "";

    /// <summary>Whether "Play all" starts this profile.</summary>
    public bool IncludeInPlayAll { get; set; } = true;

    public bool Reconnect { get; set; } = true;

    /// <summary>Copies every field into a new profile with its own ID.</summary>
    /// <param name="name">Name for the copy.</param>
    /// <returns>The copy.</returns>
    public LauncherProfile Duplicate(string name) => new()
    {
        Name = name,
        UltimaOnlineDirectory = UltimaOnlineDirectory,
        ClientVersion = ClientVersion,
        ShardName = ShardName,
        Address = Address,
        Port = Port,
        Account = Account,
        EncryptedPassword = EncryptedPassword,
        Character = Character,
        IncludeInPlayAll = IncludeInPlayAll,
        Reconnect = Reconnect
    };
}
