// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClassicUO.Configuration;
using ClassicUO.IO;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers
{
    [JsonSerializable(typeof(LastCharacterInfo))]
    [JsonSerializable(typeof(List<LastCharacterInfo>))]
    sealed partial class LastCharacterJsonContext : JsonSerializerContext { }

    public static class LastCharacterManager
    {
        private static readonly string _lastCharacterFilePath = Path.Combine(CUOEnviroment.ExecutablePath, "Data", "Profiles");
        private static readonly string _lastCharacterFile = Path.Combine(_lastCharacterFilePath, "lastcharacter.json");
        private static readonly TimeSpan SaveTimeout = TimeSpan.FromSeconds(5);

        private static List<LastCharacterInfo> LastCharacters { get; set; }
        private static LastCharacterInfo _selectedCharacter;

        private static string LastCharacterNameOverride { get; set; }
        internal static bool HasCharacterOverride => !string.IsNullOrEmpty(LastCharacterNameOverride);

        public static void Load()
        {
            try
            {
                LastCharacters = ReadStored(_lastCharacterFile);
            }
            catch (Exception e)
            {
                Log.Error(e.ToString());
            }

            // safety check
            if (LastCharacters == null)
            {
                LastCharacters = new List<LastCharacterInfo>();
            }
        }

        public static void Save(string account, string server, string name)
        {
            // Entering the world must load this process's selected profile even if a second client
            // updates the shared account history before the shard finishes logging us in.
            _selectedCharacter = new LastCharacterInfo { AccountName = account, ServerName = server, LastCharacterName = name };

            // Check to see if they passed in -lastcharactername but picked another character, clear override then
            if (HasCharacterOverride && !string.Equals(LastCharacterNameOverride, name, StringComparison.OrdinalIgnoreCase))
            {
                LastCharacterNameOverride = string.Empty;
            }

            try
            {
                SaveAt(_lastCharacterFile, account, server, name);
            }
            catch (Exception e)
            {
                Log.Error($"Could not save the last character: {e.Message}");
            }
        }

        /// <summary>Updates one account's character after re-reading the shared file under its write lock.</summary>
        internal static void SaveAt(string path, string account, string server, string name)
        {
            using var editLock = CrossProcessPathLock.TryAcquire(path, SaveTimeout)
                ?? throw new IOException("Another process is saving character history.");

            List<LastCharacterInfo> characters = ReadStored(path);
            LastCharacterInfo lastChar = characters.Find(c => c?.AccountName == account && c?.ServerName == server);
            if (lastChar == null)
            {
                lastChar = new LastCharacterInfo { AccountName = account, ServerName = server };
                characters.Add(lastChar);
            }

            lastChar.LastCharacterName = name;
            AtomicFile.Write(path, JsonSerializer.Serialize(characters, LastCharacterJsonContext.Default.ListLastCharacterInfo));
        }

        public static string GetLastCharacter(string account, string server)
        {
            if (_selectedCharacter is { } selected && selected.AccountName == account && selected.ServerName == server)
                return selected.LastCharacterName;

            return GetPreferredCharacter(account, server);
        }

        /// <summary>Gets the requested character or the latest account history for a new selection.</summary>
        internal static string GetPreferredCharacter(string account, string server)
        {
            // If they passed in a -lastcharactername param, ignore json value, use that value instead
            if (HasCharacterOverride)
            {
                return LastCharacterNameOverride;
            }

            Load();
            return LastCharacters.Find(c => c?.AccountName == account && c?.ServerName == server)?.LastCharacterName ?? string.Empty;
        }

        public static void OverrideLastCharacter(string name) => LastCharacterNameOverride = name;

        private static List<LastCharacterInfo> ReadStored(string path) => File.Exists(path)
            ? ConfigurationResolver.Load(path, LastCharacterJsonContext.Default.ListLastCharacterInfo) ?? []
            : [];
    }

    public class LastCharacterInfo
    {
        public string AccountName { get; set; }
        public string ServerName { get; set; }
        public string LastCharacterName { get; set; }
    }
}
