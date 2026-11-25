#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using ClassicUO.Configuration;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers.Launcher;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(LauncherSave))]
internal sealed partial class LauncherSaveJsonContext : JsonSerializerContext;

/// <summary>
/// Launcher profiles and options, shared by every client started from this install folder.
/// </summary>
/// <remarks>Main thread only. Saved in <see cref="FOLDER" />, which updates never overwrite.</remarks>
internal sealed class LauncherSave
{
    #region Public events

    /// <summary>Raised when the profile list or a profile's name changes; Play bars rebuild their list.</summary>
    public static event Action? ListChanged;

    /// <summary>Raised when launcher views should rebuild: another profile was selected, or update status changed.</summary>
    public static event Action? ViewChanged;

    #endregion

    #region Public accessors

    /// <summary>Folder (inside the install) holding the launcher's own files.</summary>
    public const string FOLDER = "Launcher";

    public List<LauncherProfile> Profiles { get; set; } = [];

    /// <summary>ID of the profile shown in the Play bar.</summary>
    public string SelectedId { get; set; } = "";

    /// <summary>Open the launcher, instead of a game window, when the program is started without options.</summary>
    public bool OpenOnStart { get; set; } = true;

    /// <summary>Keep the launcher open after Play, for starting more characters.</summary>
    public bool KeepOpenAfterPlay { get; set; } = true;

    /// <summary>Open each game window's Assistant as soon as that window starts.</summary>
    public bool OpenAssistantWithClient { get; set; } = true;

    #endregion

    #region Private members

    private const string FILE_NAME = "launcher.json";

    private static LauncherSave? _current;

    #endregion

    /// <summary>Loaded on first use; created from the current client settings the first time.</summary>
    public static LauncherSave Current => _current ??= Load();

    /// <summary>Absolute path of the launcher folder.</summary>
    public static string FolderPath => Path.Combine(CUOEnviroment.ExecutablePath, FOLDER);

    #region Public methods

    /// <summary>The selected profile, falling back to the first; null when there are none.</summary>
    /// <returns>The profile shown in the Play bar.</returns>
    public LauncherProfile? GetSelected() =>
        Profiles.Find(profile => profile.Id == SelectedId) ?? (Profiles.Count > 0 ? Profiles[0] : null);

    /// <summary>Selects <paramref name="profile" />, saves, and tells open views.</summary>
    /// <param name="profile">Profile to show in the Play bar.</param>
    public void Select(LauncherProfile profile)
    {
        SelectedId = profile.Id;
        Save();
        ListChanged?.Invoke();
        ViewChanged?.Invoke();
    }

    /// <summary>Asks launcher views to rebuild, e.g. after the update status changed.</summary>
    public static void NotifyViewChanged() => ViewChanged?.Invoke();

    /// <summary>Saves, then tells Play bars that the list or a name changed.</summary>
    public void SaveAndNotify()
    {
        Save();
        ListChanged?.Invoke();
    }

    /// <summary>Writes the file. Doesn't notify views, so editing a field never rebuilds the page under the cursor.</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            ConfigurationResolver.Save(this, Path.Combine(FolderPath, FILE_NAME), LauncherSaveJsonContext.Default.LauncherSave);
        }
        catch (Exception e)
        {
            Log.Error($"Launcher: could not save profiles - {e.Message}");
        }
    }

    #endregion

    #region Private methods

    private static LauncherSave Load()
    {
        LauncherSave? save = ConfigurationResolver.Load(
            Path.Combine(FolderPath, FILE_NAME),
            LauncherSaveJsonContext.Default.LauncherSave
        );

        if (save != null)
            return save;

        // First run: start with one profile matching how this client is set up today.
        save = new LauncherSave();
        Settings settings = Settings.GlobalSettings;
        var main = new LauncherProfile
        {
            Name = TazLang.Get("launcher_default_profile"),
            UltimaOnlineDirectory = settings.ResolvedUltimaOnlineDirectory,
            ClientVersion = settings.ClientVersion,
            ShardName = settings.LastServerName,
            Address = string.IsNullOrWhiteSpace(settings.IP) ? ClientBranding.DEFAULT_SHARD_ADDRESS : settings.IP,
            Port = string.IsNullOrWhiteSpace(settings.IP) ? ClientBranding.DEFAULT_SHARD_PORT : settings.Port,
            Account = settings.Username,
            EncryptedPassword = settings.Password,
            Reconnect = settings.Reconnect
        };
        save.Profiles.Add(main);
        save.SelectedId = main.Id;

        // Saved now so the profile keeps its ID (and its own client settings file) from run to run.
        save.Save();
        return save;
    }

    #endregion
}
