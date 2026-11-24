#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClassicUO.Configuration;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers.Updates;

/// <summary>An update found by <see cref="ClientUpdater.CheckAsync" />.</summary>
/// <param name="Version">Version of the release.</param>
/// <param name="ReleaseNotes">Release notes in Markdown; may be empty.</param>
/// <param name="DownloadUrl">Zip for the current platform.</param>
/// <param name="Size">Zip size in bytes, or 0 when unknown.</param>
internal sealed record AvailableUpdate(Version Version, string ReleaseNotes, string DownloadUrl, long Size);

/// <summary>Outcome of <see cref="ClientUpdater.CheckAsync" />.</summary>
internal enum UpdateCheckStatus
{
    /// <summary>No newer release, or none published yet.</summary>
    UpToDate,

    /// <summary>A newer release exists; see <see cref="UpdateCheckResult.Update" />.</summary>
    Available,

    /// <summary>The release server could not be reached or answered with something unusable.</summary>
    Failed,

    /// <summary>This install cannot self-update: a local build, or a platform releases aren't built for.</summary>
    Unsupported
}

/// <summary>Result of an update check.</summary>
/// <param name="Status">What the check found.</param>
/// <param name="Update">The newer release; set only when <paramref name="Status" /> is Available.</param>
internal sealed record UpdateCheckResult(UpdateCheckStatus Status, AvailableUpdate? Update = null);

/// <summary>
/// Finds new releases of this client on GitHub, installs them and restarts into them.
/// All members are safe to call from any thread except <see cref="RestartClient" />, which must run on the game thread.
/// </summary>
internal static class ClientUpdater
{
    /// <summary>
    /// Written next to the executable by the release workflow. Its absence marks a local build, which never
    /// offers updates: a developer's build would otherwise be "updated" back to the last release.
    /// </summary>
    private const string RELEASE_MARKER_FILE = "v.txt";

    private const string TAG_PREFIX = "v";
    private const int CHECK_TIMEOUT_SECONDS = 30;

    /// <summary>Version of the running client, as major.minor.build.</summary>
    /// <remarks>Declared before <see cref="_httpClient" />: static initializers run in source order and the
    /// client's User-Agent embeds this value.</remarks>
    public static Version CurrentVersion { get; } = ParseVersion(CUOEnviroment.Version) ?? new Version(0, 0, 0);

    private static readonly HttpClient _httpClient = CreateHttpClient();
    private static readonly string? _platformAssetName = ResolvePlatformAssetName();
    private static int _installing;

    /// <summary>Whether this install can update itself at all.</summary>
    public static bool IsSupported =>
        _platformAssetName != null && File.Exists(Path.Combine(CUOEnviroment.ExecutablePath, RELEASE_MARKER_FILE));

    /// <summary>Whether to look for updates when the login screen opens.</summary>
    public static bool IsAutomaticCheckEnabled => IsSupported && (ProfileManager.GlobalSettings?.CheckForUpdates ?? true);

    /// <summary>Asks GitHub for the newest release and compares it with <see cref="CurrentVersion" />.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The result; never throws except on cancellation.</returns>
    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
            return new UpdateCheckResult(UpdateCheckStatus.Unsupported);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(CHECK_TIMEOUT_SECONDS));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ClientBranding.LATEST_RELEASE_API_URL);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using HttpResponseMessage response = await _httpClient.SendAsync(request, timeout.Token);

            // GitHub answers 404 until the first release is published.
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate);

            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync(timeout.Token);
            GitHubRelease? release = JsonSerializer.Deserialize(json, GitHubReleaseJsonContext.Default.GitHubRelease);
            AvailableUpdate? update = ToAvailableUpdate(release);

            if (update == null)
            {
                Log.Warn($"Updater: the latest release has no usable version or no {_platformAssetName} file.");
                return new UpdateCheckResult(UpdateCheckStatus.Failed);
            }

            return update.Version > CurrentVersion
                ? new UpdateCheckResult(UpdateCheckStatus.Available, update)
                : new UpdateCheckResult(UpdateCheckStatus.UpToDate);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Log.Warn($"Updater: update check failed - {e.Message}");
            return new UpdateCheckResult(UpdateCheckStatus.Failed);
        }
    }

    /// <summary>Downloads and installs <paramref name="update" />. Call <see cref="RestartClient" /> afterwards.</summary>
    /// <param name="update">Release found by <see cref="CheckAsync" />.</param>
    /// <param name="progress">Receives download progress from 0 to 1.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <exception cref="InvalidOperationException">Another install is already running.</exception>
    /// <exception cref="Exception">The download or install failed; the previous version is left in place.</exception>
    public static async Task InstallAsync(AvailableUpdate update, IProgress<float> progress, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _installing, 1) == 1)
            throw new InvalidOperationException("An update is already being installed.");

        try
        {
            await UpdateInstaller.InstallAsync(update, CUOEnviroment.ExecutablePath, _httpClient, progress, cancellationToken);
        }
        finally
        {
            Interlocked.Exchange(ref _installing, 0);
        }
    }

    /// <summary>Starts the newly installed client with the same arguments, then closes this one.</summary>
    /// <exception cref="InvalidOperationException">The executable path is unknown.</exception>
    public static void RestartClient()
    {
        string processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unknown.");

        var startInfo = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            WorkingDirectory = CUOEnviroment.ExecutablePath
        };

        // Started as "dotnet LordBritishKW.dll": the host needs the dll path back as its first argument.
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            startInfo.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);

        string[] arguments = Environment.GetCommandLineArgs();

        for (int i = 1; i < arguments.Length; i++)
            startInfo.ArgumentList.Add(arguments[i]);

        Process.Start(startInfo);
        Client.Game.Exit();
    }

    /// <summary>Removes files replaced by the previous update, on a background thread.</summary>
    public static void DeleteLeftoversInBackground() =>
        Task.Run(() => UpdateInstaller.DeleteBackups(CUOEnviroment.ExecutablePath));

    private static AvailableUpdate? ToAvailableUpdate(GitHubRelease? release)
    {
        if (release?.TagName == null || release.Assets == null)
            return null;

        string tag = release.TagName.StartsWith(TAG_PREFIX, StringComparison.OrdinalIgnoreCase)
            ? release.TagName.Substring(TAG_PREFIX.Length)
            : release.TagName;

        Version? version = ParseVersion(tag);

        if (version == null)
            return null;

        foreach (GitHubReleaseAsset asset in release.Assets)
        {
            if (asset.DownloadUrl != null && string.Equals(asset.Name, _platformAssetName, StringComparison.OrdinalIgnoreCase))
                return new AvailableUpdate(version, release.Body ?? string.Empty, asset.DownloadUrl, asset.Size);
        }

        return null;
    }

    /// <summary>
    /// Parses to major.minor.build so "26.1124.5" and the assembly's "26.1124.5.0" compare equal; the revision
    /// part is never set by the release workflow.
    /// </summary>
    private static Version? ParseVersion(string? text)
    {
        if (!Version.TryParse(text, out Version? parsed))
            return null;

        return new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
    }

    /// <summary>Release zip name for this platform, or null where the release workflow builds none.</summary>
    private static string? ResolvePlatformAssetName()
    {
        string? runtimeId = null;

        if (OperatingSystem.IsWindows() && RuntimeInformation.OSArchitecture == Architecture.X64)
            runtimeId = "win-x64";
        else if (OperatingSystem.IsLinux() && RuntimeInformation.OSArchitecture == Architecture.X64)
            runtimeId = "linux-x64";
        else if (OperatingSystem.IsMacOS() && RuntimeInformation.OSArchitecture == Architecture.Arm64)
            runtimeId = "osx-arm64";

        return runtimeId == null ? null : $"{ClientBranding.SHORT_NAME}-{runtimeId}.zip";
    }

    private static HttpClient CreateHttpClient()
    {
        // No overall timeout: a large download on a slow line can legitimately take minutes. The check applies
        // its own deadline; downloads are cancelled by the player.
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        // GitHub's API rejects requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{ClientBranding.SHORT_NAME}/{CurrentVersion}");
        return client;
    }
}
