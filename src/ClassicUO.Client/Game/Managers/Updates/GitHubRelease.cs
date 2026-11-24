#nullable enable

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ClassicUO.Game.Managers.Updates;

/// <summary>The subset of a GitHub release the updater reads.</summary>
internal sealed class GitHubRelease
{
    /// <summary>Git tag, e.g. <c>v26.1124.7</c>; the version is parsed from it.</summary>
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    /// <summary>Release notes in Markdown, shown on the update screen.</summary>
    [JsonPropertyName("body")]
    public string? Body { get; set; }

    /// <summary>Files attached to the release, one zip per platform.</summary>
    [JsonPropertyName("assets")]
    public List<GitHubReleaseAsset>? Assets { get; set; }
}

/// <summary>A file attached to a <see cref="GitHubRelease" />.</summary>
internal sealed class GitHubReleaseAsset
{
    /// <summary>File name, e.g. <c>LordBritishKW-win-x64.zip</c>.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Public download link; GitHub redirects it to the file host.</summary>
    [JsonPropertyName("browser_download_url")]
    public string? DownloadUrl { get; set; }

    /// <summary>File size in bytes, used for download progress when the server sends no length.</summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }
}

[JsonSerializable(typeof(GitHubRelease))]
internal partial class GitHubReleaseJsonContext : JsonSerializerContext
{
}
