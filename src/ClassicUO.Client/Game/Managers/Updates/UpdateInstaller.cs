#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ClassicUO.Configuration;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers.Updates;

/// <summary>
/// Downloads a release zip and swaps its files into the install folder while the client is still running.
/// </summary>
/// <remarks>
/// A running program cannot overwrite its own files on Windows, but it can rename them. Each file being replaced is
/// renamed to <c>&lt;name&gt;</c> + <see cref="BACKUP_SUFFIX" /> and the new copy written in its place; the client then
/// restarts into the new files and <see cref="DeleteBackups" /> removes the leftovers on the next start. If any step
/// fails, every file already swapped is restored, so a failed update leaves the old version intact.
/// Not thread-safe: run one install at a time.
/// </remarks>
internal static class UpdateInstaller
{
    /// <summary>Suffix given to replaced files until the next start deletes them.</summary>
    public const string BACKUP_SUFFIX = ".update-old";

    private const int DOWNLOAD_BUFFER_SIZE = 81920;
    private const string PendingUpdateMarker = ".update-in-progress";
    private static readonly TimeSpan DownloadIdleTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Player-owned files and folders (relative to the install folder). Existing files here are never overwritten,
    /// even if a release zip carries a file with the same name; new files are still added.
    /// </summary>
    private static readonly string[] _preservedPaths =
    [
        "Data",
        "LegionScripts",
        "ExternalImages",
        "Logs",
        ClassicUO.Game.Managers.Launcher.LauncherSave.FOLDER,
        ClassicUO.Configuration.Settings.SETTINGS_FILENAME
    ];

    /// <summary>
    /// Downloads <paramref name="update" />, unpacks it and installs it over <paramref name="installDirectory" />.
    /// </summary>
    /// <param name="update">The release to install.</param>
    /// <param name="installDirectory">Folder the running client was started from.</param>
    /// <param name="httpClient">Client used for the download; must follow redirects.</param>
    /// <param name="progress">Receives download progress from 0 to 1; reports 1 before files are swapped.</param>
    /// <param name="cancellationToken">Cancels the download. Ignored once files start being swapped.</param>
    /// <exception cref="HttpRequestException">The download failed.</exception>
    /// <exception cref="InvalidDataException">The download is not a valid zip.</exception>
    /// <exception cref="IOException">A file could not be replaced; the install folder was restored.</exception>
    public static async Task InstallAsync(
        AvailableUpdate update,
        string installDirectory,
        HttpClient httpClient,
        IProgress<float> progress,
        CancellationToken cancellationToken
    )
    {
        string stagingDirectory = Path.Combine(Path.GetTempPath(), $"{ClientBranding.SHORT_NAME}_update_{Guid.NewGuid():N}");
        string zipPath = Path.Combine(stagingDirectory, "update.zip");
        string extractedDirectory = Path.Combine(stagingDirectory, "files");

        try
        {
            Directory.CreateDirectory(stagingDirectory);
            await DownloadAsync(update, zipPath, httpClient, progress, cancellationToken);
            progress.Report(1f);

            ZipFile.ExtractToDirectory(zipPath, extractedDirectory);
            using var installLock = CrossProcessPathLock.TryAcquire(installDirectory, TimeSpan.Zero)
                ?? throw new IOException(TazLang.Get("launcher_update_busy"));
            string marker = Path.Combine(installDirectory, "v.txt");
            string pendingMarker = Path.Combine(installDirectory, PendingUpdateMarker);
            if (!File.Exists(pendingMarker) && File.Exists(marker)
                && Version.TryParse(File.ReadAllText(marker).Trim(), out Version? installed)
                && installed >= update.Version)
                return;

            // A process that dies during replacement leaves its backup files available for recovery.
            File.WriteAllText(pendingMarker, update.Version.ToString());
            ReplaceFiles(extractedDirectory, installDirectory);
            File.Delete(pendingMarker);
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
        }
    }

    /// <summary>
    /// Deletes files left behind by a previous update. Best effort: files still locked are left for the next start.
    /// </summary>
    /// <param name="installDirectory">Folder the running client was started from.</param>
    public static void DeleteBackups(string installDirectory)
    {
        try
        {
            using var installLock = CrossProcessPathLock.TryAcquire(installDirectory, TimeSpan.Zero);
            if (installLock == null || File.Exists(Path.Combine(installDirectory, PendingUpdateMarker)))
                return;

            foreach (string file in Directory.EnumerateFiles(installDirectory, "*" + BACKUP_SUFFIX, SearchOption.AllDirectories))
            {
                if (IsPreserved(Path.GetRelativePath(installDirectory, file)))
                    continue;

                try
                {
                    File.Delete(file);
                }
                catch (Exception e)
                {
                    Log.Warn($"Updater: could not delete leftover '{file}': {e.Message}");
                }
            }
        }
        catch (Exception e)
        {
            Log.Warn($"Updater: could not scan for leftover update files: {e.Message}");
        }
    }

    private static async Task DownloadAsync(
        AvailableUpdate update,
        string zipPath,
        HttpClient httpClient,
        IProgress<float> progress,
        CancellationToken cancellationToken
    )
    {
        using var inactivity = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        inactivity.CancelAfter(DownloadIdleTimeout);
        CancellationToken token = inactivity.Token;
        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(
                update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? update.Size;
            await using Stream source = await response.Content.ReadAsStreamAsync(token);
            await using FileStream destination = File.Create(zipPath);

            var buffer = new byte[DOWNLOAD_BUFFER_SIZE];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, token)) > 0)
            {
                inactivity.CancelAfter(DownloadIdleTimeout);
                await destination.WriteAsync(buffer.AsMemory(0, read), token);
                received += read;
                if (totalBytes > 0)
                    progress.Report(Math.Min(1f, (float)received / totalBytes));
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && inactivity.IsCancellationRequested)
        {
            throw new TimeoutException(TazLang.Get("launcher_update_timedout"));
        }
    }

    /// <summary>Copies every file from <paramref name="sourceDirectory" /> over the install, rolling back on failure.</summary>
    internal static void ReplaceFiles(string sourceDirectory, string installDirectory)
    {
        var swapped = new List<(string Target, string? Backup)>();

        try
        {
            foreach (string sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
                string targetFile = Path.Combine(installDirectory, relativePath);
                bool exists = File.Exists(targetFile);

                if (exists && IsPreserved(relativePath))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);

                string? backup = exists ? MoveAside(targetFile) : null;
                swapped.Add((targetFile, backup));
                File.Copy(sourceFile, targetFile);
            }

            EnsureExecutable(Environment.ProcessPath, installDirectory);
        }
        catch (Exception e)
        {
            Log.Error($"Updater: install failed, restoring previous files - {e}");
            Restore(swapped);
            throw;
        }
    }

    /// <summary>Renames <paramref name="file" /> out of the way and returns its new path.</summary>
    private static string MoveAside(string file)
    {
        string backup = file + BACKUP_SUFFIX;

        if (File.Exists(backup))
        {
            try
            {
                File.Delete(backup);
            }
            catch (IOException)
            {
                // A backup from an earlier update that is somehow still locked; pick a fresh name instead.
                backup = $"{file}.{Guid.NewGuid():N}{BACKUP_SUFFIX}";
            }
            catch (UnauthorizedAccessException)
            {
                backup = $"{file}.{Guid.NewGuid():N}{BACKUP_SUFFIX}";
            }
        }

        File.Move(file, backup);
        return backup;
    }

    private static void Restore(List<(string Target, string? Backup)> swapped)
    {
        for (int i = swapped.Count - 1; i >= 0; i--)
        {
            (string target, string? backup) = swapped[i];

            try
            {
                if (File.Exists(target))
                    File.Delete(target);

                if (backup != null)
                    File.Move(backup, target);
            }
            catch (Exception e)
            {
                Log.Error($"Updater: could not restore '{target}': {e.Message}");
            }
        }
    }

    private static bool IsPreserved(string relativePath)
    {
        foreach (string preserved in _preservedPaths)
        {
            if (relativePath.Equals(preserved, StringComparison.OrdinalIgnoreCase))
                return true;

            if (relativePath.Length > preserved.Length
                && relativePath.StartsWith(preserved, StringComparison.OrdinalIgnoreCase)
                && (relativePath[preserved.Length] == Path.DirectorySeparatorChar
                    || relativePath[preserved.Length] == Path.AltDirectorySeparatorChar))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Zips built on Windows carry no Unix permissions, so the client binary could lose its execute bit on
    /// Linux/macOS and fail to restart. No-op on Windows, and when the client runs through a shared
    /// <c>dotnet</c> host outside the install folder.
    /// </summary>
    private static void EnsureExecutable(string? path, string installDirectory)
    {
        if (OperatingSystem.IsWindows() || string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(installDirectory), StringComparison.Ordinal))
            return;

        UnixFileMode mode = File.GetUnixFileMode(path);
        File.SetUnixFileMode(path, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
        catch (Exception e)
        {
            Log.Warn($"Updater: could not delete temporary folder '{directory}': {e.Message}");
        }
    }
}
