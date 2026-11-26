#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using ClassicUO.Configuration;
using ClassicUO.IO;
using ClassicUO.Setup;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers.Launcher;

/// <summary>First thing a profile still needs before it can play, in the order a new player fills them in.</summary>
internal enum LauncherSetupStep
{
    Folder,
    Address,
    Account,
    Ready
}

/// <summary>
/// Starts clients for launcher profiles and tracks how this process was started.
/// </summary>
/// <remarks>
/// Three ways to run: the launcher (no game window, only the launcher), a client started by the launcher
/// (<c>-launchprofile</c>), or a plain client started with other options. Main thread only.
/// </remarks>
internal static class ClientLauncher
{
    #region Public accessors

    /// <summary>Command-line option naming the profile a client was started for.</summary>
    public const string PROFILE_ARGUMENT = "launchprofile";

    /// <summary>This process is the launcher: its game window stays hidden.</summary>
    public static bool IsLauncherOnly { get; private set; }

    /// <summary>This process was started by the launcher, which already checks for updates.</summary>
    public static bool IsLaunchedClient => _launchedProfileId != null;

    #endregion

    #region Private members

    private const string SETTINGS_SUFFIX = ".settings.json";
    private const int INSTALL_HASH_LENGTH = 12;

    private static string? _launchedProfileId;

    // Held for the life of a launched client so the launcher can see that its profile is running.
    private static Mutex? _runningMarker;

    #endregion

    #region Public methods

    /// <summary>Records the profile from <c>-launchprofile</c> and marks it as running.</summary>
    /// <param name="profileId">The profile's ID.</param>
    public static void SetLaunchedProfile(string profileId)
    {
        // Options are read twice at start; mark only once.
        if (string.IsNullOrWhiteSpace(profileId) || _launchedProfileId != null)
            return;

        _launchedProfileId = profileId;

        try
        {
            _runningMarker = new Mutex(false, MarkerName(profileId));
        }
        catch (Exception e)
        {
            Log.Warn($"Launcher: could not mark profile as running - {e.Message}");
        }
    }

    /// <summary>
    /// Becomes the launcher when the program was started with no options and the launcher is turned on.
    /// Call after settings are loaded.
    /// </summary>
    /// <param name="args">The program's command-line options.</param>
    public static void DecideMode(string[] args) =>
        IsLauncherOnly = args.Length == 0 && LauncherSave.Current.OpenOnStart;

    /// <summary>Whether a client started for <paramref name="profile" /> is open right now.</summary>
    /// <param name="profile">Profile to check.</param>
    /// <returns>True while that client runs.</returns>
    public static bool IsRunning(LauncherProfile profile)
    {
        try
        {
            if (!Mutex.TryOpenExisting(MarkerName(profile.Id), out Mutex? marker))
                return false;

            marker.Dispose();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Starts a client that logs straight into <paramref name="profile" />'s character.</summary>
    /// <param name="profile">Profile to play.</param>
    /// <returns>Null when started; otherwise why it couldn't start, ready to show.</returns>
    public static string? Play(LauncherProfile profile)
    {
        // Say what's missing here rather than open a game window that then asks for it.
        switch (FindMissingStep(profile))
        {
            case LauncherSetupStep.Folder:
                return TazLang.Get("launcher_error_nofolder", [profile.Name]);
            case LauncherSetupStep.Address:
                return TazLang.Get("launcher_error_noaddress", [profile.Name]);
            case LauncherSetupStep.Account:
                return TazLang.Get("launcher_error_noaccount", [profile.Name]);
        }

        try
        {
            using var installLock = CrossProcessPathLock.TryAcquire(CUOEnviroment.ExecutablePath, TimeSpan.Zero);
            if (installLock == null)
                return TazLang.Get("launcher_update_busy");

            if (IsRunning(profile))
                return TazLang.Get("launcher_already_running", [profile.Name]);

            string settingsPath = WriteSettings(profile);
            ProcessStartInfo startInfo = ClientProcess.CreateStartInfo();
            startInfo.ArgumentList.Add("-settings");
            startInfo.ArgumentList.Add(settingsPath);
            startInfo.ArgumentList.Add("-" + PROFILE_ARGUMENT);
            startInfo.ArgumentList.Add(profile.Id);

            if (!string.IsNullOrWhiteSpace(profile.Character))
            {
                startInfo.ArgumentList.Add("-lastcharactername");
                startInfo.ArgumentList.Add(profile.Character);
            }

            Process.Start(startInfo);
            return null;
        }
        catch (Exception e)
        {
            Log.Error($"Launcher: could not start '{profile.Name}' - {e}");
            return TazLang.Get("launcher_error_start", [profile.Name, e.Message]);
        }
    }

    /// <summary>The first setup step <paramref name="profile" /> still needs.</summary>
    /// <param name="profile">Profile to check.</param>
    /// <returns><see cref="LauncherSetupStep.Ready" /> when it can play.</returns>
    public static LauncherSetupStep FindMissingStep(LauncherProfile profile)
    {
        if (!FirstRunSetup.IsUltimaOnlineFolder(profile.UltimaOnlineDirectory))
            return LauncherSetupStep.Folder;

        if (string.IsNullOrWhiteSpace(profile.Address) || profile.Port == 0)
            return LauncherSetupStep.Address;

        if (string.IsNullOrWhiteSpace(profile.Account) || string.IsNullOrEmpty(profile.EncryptedPassword))
            return LauncherSetupStep.Account;

        return LauncherSetupStep.Ready;
    }

    #endregion

    #region Private methods

    /// <summary>
    /// Writes the profile's own client settings file. The first time it copies this client's settings, so things
    /// like encryption and plugins carry over; later it only updates the profile's fields and keeps the rest
    /// (window position and so on) as that client left them.
    /// </summary>
    private static string WriteSettings(LauncherProfile profile)
    {
        string path = Path.Combine(LauncherSave.FolderPath, profile.Id + SETTINGS_SUFFIX);
        Settings settings = ConfigurationResolver.Load(path, SettingsJsonContext.RealDefault.Settings) ?? CopyCurrentSettings();

        settings.UltimaOnlineDirectory = profile.UltimaOnlineDirectory;
        settings.ClientVersion = profile.ClientVersion;
        settings.IP = profile.Address;
        settings.Port = profile.Port;
        settings.Username = profile.Account;
        settings.Password = profile.EncryptedPassword;
        settings.LastServerName = profile.ShardName;
        settings.SaveAccount = true;
        settings.AutoLogin = true;
        settings.Reconnect = profile.Reconnect;
        if (string.IsNullOrWhiteSpace(settings.ProfilesPath))
            settings.ProfilesPath = Settings.GlobalSettings.ProfilesPath;

        AtomicFile.Write(path, JsonSerializer.Serialize(settings, SettingsJsonContext.RealDefault.Settings));
        return path;
    }

    private static Settings CopyCurrentSettings()
    {
        string json = JsonSerializer.Serialize(Settings.GlobalSettings, SettingsJsonContext.RealDefault.Settings);
        return JsonSerializer.Deserialize(json, SettingsJsonContext.RealDefault.Settings) ?? new Settings();
    }

    /// <summary>Name shared by a launched client and the launcher; includes the install so two installs don't mix.</summary>
    private static string MarkerName(string profileId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(CUOEnviroment.ExecutablePath.ToLowerInvariant()));
        string install = Convert.ToHexString(hash)[..INSTALL_HASH_LENGTH];
        return $"{ClientBranding.SHORT_NAME}-{install}-{profileId}";
    }

    #endregion
}
