#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using ClassicUO.Configuration;
using ClassicUO.Utility;
using ClassicUO.Utility.Logging;
using SDL3;

namespace ClassicUO.Setup;

/// <summary>
/// The welcome screen shown at startup when the client cannot find the Ultima Online game files. It walks the player
/// through choosing the UO folder, works out the client version and saves both to <c>settings.json</c>, so nobody
/// has to edit that file by hand. Also warns when the client sits in a folder it cannot write to.
/// Runs on the main thread before the game window exists; uses native dialogs only.
/// </summary>
internal static class FirstRunSetup
{
    #region Private members

    /// <summary>File every UO data folder has; the client refuses to start without it.</summary>
    private const string UO_MARKER_FILE = "tiledata.mul";

    private const string CLIENT_EXE = "client.exe";

    /// <summary>File present only in UOP-era data (client 7.0.24 and later).</summary>
    private const string UOP_MARKER_FILE = "artLegacyMUL.uop";

    /// <summary>
    /// Fallback client versions when no client.exe is available to read. Guesses by data format: the player can
    /// correct them in the login screen's Edit settings window.
    /// </summary>
    private const string FALLBACK_UOP_CLIENT_VERSION = "7.0.95.0";

    private const string FALLBACK_MUL_CLIENT_VERSION = "7.0.15.1";

    /// <summary>Install locations checked automatically, relative to each drive's root.</summary>
    private static readonly string[] _commonInstallFolders =
    [
        @"Program Files (x86)\Electronic Arts\Ultima Online Classic",
        @"Program Files\Electronic Arts\Ultima Online Classic",
        @"Program Files (x86)\EA Games\Ultima Online Classic",
        @"Program Files (x86)\Ultima Online Classic",
        @"Program Files (x86)\Ultima Online",
        @"Ultima Online Classic",
        @"Ultima Online",
        @"Games\Ultima Online Classic",
        @"Games\Ultima Online"
    ];

    #endregion

    #region Public methods

    /// <summary>
    /// Makes sure the client can start: warns about an unwritable install folder, and runs the welcome screen if
    /// the UO folder in the settings is missing or wrong. Saves any answers to <see cref="Settings.GlobalSettings" />.
    /// </summary>
    /// <returns>False if the player chose to quit.</returns>
    public static bool EnsureReady()
    {
        if (!WarnIfInstallFolderReadOnly())
            return false;

        Settings settings = Settings.GlobalSettings;

        if (IsUltimaOnlineFolder(settings.ResolvedUltimaOnlineDirectory))
            return true;

        string? folder = AskForUltimaOnlineFolder();

        if (folder == null)
            return false;

        settings.UltimaOnlineDirectory = folder;
        string versionMessage = ApplyClientVersion(settings, folder);
        settings.Save();

        Log.Info($"Setup: UO folder set to '{folder}', client version {settings.ClientVersion}.");

        NativeDialogs.ShowChoice(
            TazLang.Get("setup_title", [ClientBranding.NAME]),
            TazLang.Get("setup_done", [folder, versionMessage]),
            SDL.SDL_MessageBoxFlags.SDL_MESSAGEBOX_INFORMATION,
            TazLang.Get("setup_play")
        );

        return true;
    }

    /// <summary>Whether <paramref name="folder" /> holds Ultima Online game files.</summary>
    /// <param name="folder">Folder to test; null or empty is never valid.</param>
    /// <returns>True if the folder contains <c>tiledata.mul</c>.</returns>
    public static bool IsUltimaOnlineFolder(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(folder, UO_MARKER_FILE));

    #endregion

    #region Private methods

    /// <returns>False if the player chose to quit.</returns>
    private static bool WarnIfInstallFolderReadOnly()
    {
        if (CanWriteTo(CUOEnviroment.ExecutablePath))
            return true;

        int choice = NativeDialogs.ShowChoice(
            TazLang.Get("setup_title", [ClientBranding.NAME]),
            TazLang.Get(
                "setup_readonly",
                [ClientBranding.NAME, CUOEnviroment.ExecutablePath.TrimEnd(Path.DirectorySeparatorChar)]
            ),
            SDL.SDL_MessageBoxFlags.SDL_MESSAGEBOX_WARNING,
            TazLang.Get("setup_quit"),
            TazLang.Get("setup_continueanyway")
        );

        return choice == 1;
    }

    /// <summary>Runs the welcome and folder-picking dialogs until the player picks a valid folder or quits.</summary>
    /// <returns>The chosen UO folder, or null if the player quit.</returns>
    private static string? AskForUltimaOnlineFolder()
    {
        string title = TazLang.Get("setup_title", [ClientBranding.NAME]);
        string? found = FindInstalledUltimaOnline();
        string message = TazLang.Get("setup_welcome", [ClientBranding.NAME]);

        if (found != null)
        {
            int choice = NativeDialogs.ShowChoice(
                title,
                message + "\n\n" + TazLang.Get("setup_found", [found]),
                SDL.SDL_MessageBoxFlags.SDL_MESSAGEBOX_INFORMATION,
                TazLang.Get("setup_usefound"),
                TazLang.Get("setup_choosefolder"),
                TazLang.Get("setup_quit")
            );

            if (choice == 0)
                return found;

            if (choice == 2)
                return null;
        }
        else
        {
            int choice = NativeDialogs.ShowChoice(
                title,
                message + "\n\n" + TazLang.Get("setup_notfound"),
                SDL.SDL_MessageBoxFlags.SDL_MESSAGEBOX_INFORMATION,
                TazLang.Get("setup_choosefolder"),
                TazLang.Get("setup_quit")
            );

            if (choice != 0)
                return null;
        }

        return PickFolderUntilValid(title, found);
    }

    private static string? PickFolderUntilValid(string title, string? startFolder)
    {
        while (true)
        {
            FolderPickResult result = NativeDialogs.PickFolder(startFolder, out string? picked);

            if (result == FolderPickResult.Unavailable)
            {
                NativeDialogs.ShowChoice(
                    title,
                    TazLang.Get("setup_pickerunavailable", [Settings.GetSettingsFilepath()]),
                    SDL.SDL_MessageBoxFlags.SDL_MESSAGEBOX_ERROR,
                    TazLang.Get("setup_quit")
                );
                return null;
            }

            if (result == FolderPickResult.Picked && picked != null)
            {
                string? folder = ResolveUltimaOnlineFolder(picked);

                if (folder != null)
                    return folder;

                startFolder = picked;
            }

            string retryMessage = result == FolderPickResult.Cancelled
                ? TazLang.Get("setup_cancelled")
                : TazLang.Get("setup_invalidfolder", [picked ?? string.Empty]);

            int choice = NativeDialogs.ShowChoice(
                title,
                retryMessage,
                SDL.SDL_MessageBoxFlags.SDL_MESSAGEBOX_WARNING,
                TazLang.Get("setup_choosefolder"),
                TazLang.Get("setup_quit")
            );

            if (choice != 0)
                return null;
        }
    }

    /// <summary>
    /// Accepts the picked folder or, as a convenience, a direct subfolder holding the files (players often pick
    /// the parent, e.g. "Electronic Arts").
    /// </summary>
    private static string? ResolveUltimaOnlineFolder(string picked)
    {
        if (IsUltimaOnlineFolder(picked))
            return picked;

        try
        {
            foreach (string child in Directory.EnumerateDirectories(picked))
            {
                if (IsUltimaOnlineFolder(child))
                    return child;
            }
        }
        catch (Exception e)
        {
            Log.Warn($"Setup: could not look inside '{picked}': {e.Message}");
        }

        return null;
    }

    /// <summary>Looks for a UO install in the usual places on every fixed drive.</summary>
    private static string? FindInstalledUltimaOnline()
    {
        foreach (string root in GetDriveRoots())
        {
            foreach (string relative in _commonInstallFolders)
            {
                string candidate = Path.Combine(root, relative);

                if (IsUltimaOnlineFolder(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetDriveRoots()
    {
        if (!OperatingSystem.IsWindows())
            yield break;

        DriveInfo[] drives;

        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception e)
        {
            Log.Warn($"Setup: could not list drives: {e.Message}");
            yield break;
        }

        foreach (DriveInfo drive in drives)
        {
            if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                yield return drive.RootDirectory.FullName;
        }
    }

    /// <summary>
    /// Keeps an already valid client version; otherwise reads it from client.exe, or falls back to a guess based
    /// on the data format.
    /// </summary>
    /// <returns>A sentence for the closing dialog describing where the version came from.</returns>
    private static string ApplyClientVersion(Settings settings, string folder)
    {
        if (ClientVersionHelper.IsClientVersionValid(settings.ClientVersion, out _))
            return TazLang.Get("setup_version_kept", [settings.ClientVersion]);

        if (ClientVersionHelper.TryParseFromFile(Path.Combine(folder, CLIENT_EXE), out string detected)
            && ClientVersionHelper.IsClientVersionValid(detected, out _))
        {
            settings.ClientVersion = detected;
            return TazLang.Get("setup_version_detected", [detected]);
        }

        settings.ClientVersion = File.Exists(Path.Combine(folder, UOP_MARKER_FILE))
            ? FALLBACK_UOP_CLIENT_VERSION
            : FALLBACK_MUL_CLIENT_VERSION;

        return TazLang.Get("setup_version_guessed", [settings.ClientVersion]);
    }

    private static bool CanWriteTo(string folder)
    {
        string probe = Path.Combine(folder, $".write-test-{Guid.NewGuid():N}");

        try
        {
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            Log.Warn($"Setup: install folder is not writable: {e.Message}");
            return false;
        }
    }

    #endregion
}
