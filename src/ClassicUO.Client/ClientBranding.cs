#nullable enable

namespace ClassicUO
{
    /// <summary>
    /// Product name and project links shown to players. Every player-facing mention of the client's name or
    /// home page reads from here, so a rebrand is a one-file change.
    /// </summary>
    /// <remarks>
    /// The executable name lives in <c>ClassicUO.Client.csproj</c> (<c>AssemblyName</c>) and must match
    /// <see cref="SHORT_NAME" />: FNA loads the window icon from <c>&lt;AssemblyName&gt;.png</c>, and generated
    /// script projects reference <c>&lt;SHORT_NAME&gt;.dll</c>.
    /// </remarks>
    internal static class ClientBranding
    {
        /// <summary>Full display name, used in window titles, crash reports and messages.</summary>
        public const string NAME = "LordBritishKW Client";

        /// <summary>Name without spaces, used for the executable, folders and file paths.</summary>
        public const string SHORT_NAME = "LordBritishKW";

        /// <summary>Project home page; the login screen's website button opens it.</summary>
        public const string REPOSITORY_URL = "https://github.com/Rabiehw1/My-UO";

        /// <summary>Where crash logs and in-game error messages send players to report problems.</summary>
        public const string ISSUES_URL = REPOSITORY_URL + "/issues";

        /// <summary>Changelog opened from the version history window.</summary>
        public const string CHANGELOG_URL = REPOSITORY_URL + "/blob/main/CHANGELOG.md";
    }
}
