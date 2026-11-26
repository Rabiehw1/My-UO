#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClassicUO.Configuration;
using ClassicUO.IO;
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

    /// <summary>Open each game's Assistant automatically. Games started with Play wait until world entry.</summary>
    public bool OpenAssistantWithClient { get; set; } = true;

    #endregion

    #region Private members

    private const string FILE_NAME = "launcher.json";
    private static readonly TimeSpan SaveTimeout = TimeSpan.FromSeconds(5);

    private static LauncherSave? _current;
    private LauncherSave? _baseline;
    private static string SavePath => Path.Combine(FolderPath, FILE_NAME);

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
            using var editLock = CrossProcessPathLock.TryAcquire(SavePath, SaveTimeout)
                ?? throw new IOException("Another process is saving launcher profiles.");
            LauncherSave baseline = _baseline ?? new LauncherSave();
            LauncherSave latest = ReadStored() ?? baseline;
            LauncherSave result = Merge(baseline, this, latest);
            WriteStored(result);
            bool selectionChanged = SelectedId != result.SelectedId;
            bool listChanged = !Profiles.Select(profile => (profile.Id, profile.Name))
                .SequenceEqual(result.Profiles.Select(profile => (profile.Id, profile.Name)));
            Adopt(result);
            _baseline = result.Snapshot();
            if (listChanged)
                ListChanged?.Invoke();
            if (selectionChanged)
                ViewChanged?.Invoke();
        }
        catch (Exception e)
        {
            Log.Error($"Launcher: could not save profiles - {e.Message}");
        }
    }

    /// <summary>Applies only changes made locally to the most recently saved document.</summary>
    internal static LauncherSave Merge(LauncherSave baseline, LauncherSave edited, LauncherSave latest)
    {
        var result = latest.Snapshot();
        result.SelectedId = LauncherProfile.EditedValue(latest.SelectedId, baseline.SelectedId, edited.SelectedId);
        result.OpenOnStart = LauncherProfile.EditedValue(latest.OpenOnStart, baseline.OpenOnStart, edited.OpenOnStart);
        result.KeepOpenAfterPlay = LauncherProfile.EditedValue(
            latest.KeepOpenAfterPlay, baseline.KeepOpenAfterPlay, edited.KeepOpenAfterPlay);
        result.OpenAssistantWithClient = LauncherProfile.EditedValue(
            latest.OpenAssistantWithClient, baseline.OpenAssistantWithClient, edited.OpenAssistantWithClient);

        result.Profiles.RemoveAll(profile => baseline.Profiles.Exists(original => original.Id == profile.Id)
            && !edited.Profiles.Exists(local => local.Id == profile.Id));
        foreach (LauncherProfile local in edited.Profiles)
        {
            LauncherProfile? original = baseline.Profiles.Find(profile => profile.Id == local.Id);
            LauncherProfile? saved = result.Profiles.Find(profile => profile.Id == local.Id);
            if (original == null && saved == null)
                result.Profiles.Add(local.Snapshot());
            else if (original != null)
                saved?.ApplyEdits(original, local);
        }

        result.SelectedId = result.GetSelected()?.Id ?? "";
        return result;
    }

    #endregion

    #region Private methods

    private static LauncherSave Load()
    {
        try
        {
            using var editLock = CrossProcessPathLock.TryAcquire(SavePath, SaveTimeout)
                ?? throw new IOException("Another process is saving launcher profiles.");
            LauncherSave? save = ReadStored();
            if (save == null)
            {
                save = CreateDefault();
                WriteStored(save);
            }

            save._baseline = save.Snapshot();
            return save;
        }
        catch (Exception e)
        {
            Log.Error($"Launcher: could not load profiles - {e.Message}");
            LauncherSave save = CreateDefault();
            save._baseline = save.Snapshot();
            return save;
        }
    }

    private static LauncherSave CreateDefault()
    {
        // First run: start with one profile matching how this client is set up today.
        var save = new LauncherSave();
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
        return save;
    }

    private static LauncherSave? ReadStored() => File.Exists(SavePath)
        ? ConfigurationResolver.Load(SavePath, LauncherSaveJsonContext.Default.LauncherSave)
        : null;

    private static void WriteStored(LauncherSave save) =>
        AtomicFile.Write(SavePath, JsonSerializer.Serialize(save, LauncherSaveJsonContext.Default.LauncherSave));

    private LauncherSave Snapshot()
    {
        var snapshot = (LauncherSave)MemberwiseClone();
        snapshot._baseline = null;
        snapshot.Profiles = Profiles.Select(profile => profile.Snapshot()).ToList();
        return snapshot;
    }

    private void Adopt(LauncherSave saved)
    {
        SelectedId = saved.SelectedId;
        OpenOnStart = saved.OpenOnStart;
        KeepOpenAfterPlay = saved.KeepOpenAfterPlay;
        OpenAssistantWithClient = saved.OpenAssistantWithClient;
        // Keep existing profile references valid for live editor callbacks.
        Profiles = saved.Profiles.Select(profile =>
        {
            LauncherProfile? existing = Profiles.Find(local => local.Id == profile.Id);
            if (existing == null)
                return profile.Snapshot();

            existing.ApplyEdits(null, profile);
            return existing;
        }).ToList();
    }

    #endregion
}
