using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClassicUO.Configuration;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers
{
    public class SoundFilterManager
    {
        private static readonly HashSet<int> _emptyFilters = new();

        public static SoundFilterManager Instance { get; } = new SoundFilterManager();

        #warning Remove migration >= 10/26/26
        /// <summary>
        /// One-time migration of the pre-<see cref="GlobalSettingsSave"/> per-account sound/music filter
        /// lists from SQL settings into the machine-wide global settings. The legacy rows are cleared so
        /// this is idempotent per account. Requires the current profile to be loaded so the account scope
        /// resolves to the account that actually stored the lists.
        /// </summary>
        public static void MigrateLegacySqlSettings()
        {
            if (Client.Settings == null || ProfileManager.CurrentProfile == null || ProfileManager.GlobalSettings == null)
            {
                return;
            }

            GlobalSettingsSave globalSettings = ProfileManager.GlobalSettings;

            MigrateLegacySet(Constants.SqlSettings.SOUND_FILTER_IDS, globalSettings.FilteredSounds);
            MigrateLegacySet(Constants.SqlSettings.MUSIC_FILTER_IDS, globalSettings.FilteredMusic);
        }

        private static bool MigrateLegacySet(string sqlKey, HashSet<int> target)
        {
            string json = Client.Settings.Get(SettingsScope.Account, sqlKey, null);

            if (string.IsNullOrWhiteSpace(json) || json == "[]")
            {
                return false;
            }

            HashSet<int> legacy;
            try
            {
                legacy = JsonSerializer.Deserialize(json, HashSetIntContext.Default.HashSetInt32) ?? new HashSet<int>();
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to migrate legacy SQL setting '{sqlKey}': {ex.Message}");
                return false;
            }

            bool changed = false;

            foreach (int id in legacy)
            {
                changed |= target.Add(id);
            }

            Client.Settings.Set(SettingsScope.Account, sqlKey, "[]");

            return changed;
        }

        public HashSet<int> FilteredSounds => ProfileManager.GlobalSettings?.FilteredSounds ?? _emptyFilters;

        public HashSet<int> FilteredMusic => ProfileManager.GlobalSettings?.FilteredMusic ?? _emptyFilters;

        public void AddFilter(int soundId, bool isMusic = false)
        {
            GlobalSettingsSave globalSettings = ProfileManager.GlobalSettings;
            if (globalSettings != null && GetFilters(globalSettings, isMusic).Add(soundId))
                Client.Game?.Audio?.StopFilteredPlayback(soundId, isMusic);
        }

        public void RemoveFilter(int soundId, bool isMusic = false)
        {
            GlobalSettingsSave globalSettings = ProfileManager.GlobalSettings;
            if (globalSettings == null)
                return;

            GetFilters(globalSettings, isMusic).Remove(soundId);
            GetDisabled(globalSettings, isMusic).Remove(soundId);
        }

        public bool IsSoundFiltered(int soundId, bool isMusic = false)
        {
            GlobalSettingsSave globalSettings = ProfileManager.GlobalSettings;
            return globalSettings != null && GetFilters(globalSettings, isMusic).Contains(soundId)
                && !GetDisabled(globalSettings, isMusic).Contains(soundId);
        }

        internal void SetEnabled(int id, bool enabled, bool isMusic)
        {
            GlobalSettingsSave settings = ProfileManager.GlobalSettings;
            if (settings == null || !GetFilters(settings, isMusic).Contains(id))
                return;

            HashSet<int> disabled = GetDisabled(settings, isMusic);
            if (enabled)
            {
                if (disabled.Remove(id))
                    Client.Game?.Audio?.StopFilteredPlayback(id, isMusic);
            }
            else
                disabled.Add(id);
        }

        internal bool ReplaceFilter(int oldId, int newId, bool isMusic)
        {
            GlobalSettingsSave settings = ProfileManager.GlobalSettings;
            if (settings == null)
                return false;

            HashSet<int> filters = GetFilters(settings, isMusic);
            if (!filters.Contains(oldId) || !filters.Add(newId))
                return false;

            filters.Remove(oldId);
            HashSet<int> disabled = GetDisabled(settings, isMusic);
            if (disabled.Remove(oldId))
                disabled.Add(newId);
            else
                Client.Game?.Audio?.StopFilteredPlayback(newId, isMusic);

            return true;
        }

        public void Clear(bool isMusic = false)
        {
            GlobalSettingsSave globalSettings = ProfileManager.GlobalSettings;
            if (globalSettings == null)
                return;

            GetFilters(globalSettings, isMusic).Clear();
            GetDisabled(globalSettings, isMusic).Clear();
        }

        public void Reset(bool isMusic = false) => Clear(isMusic);

        private static HashSet<int> GetFilters(GlobalSettingsSave settings, bool isMusic) =>
            isMusic ? settings.FilteredMusic : settings.FilteredSounds;

        private static HashSet<int> GetDisabled(GlobalSettingsSave settings, bool isMusic) =>
            isMusic ? settings.DisabledMusicFilters : settings.DisabledSoundFilters;
    }

    [JsonSerializable(typeof(HashSet<int>))]
    [JsonSourceGenerationOptions(
        WriteIndented = false,
        PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        IgnoreReadOnlyProperties = false,
        IncludeFields = false)]
    public partial class HashSetIntContext : JsonSerializerContext
    {
    }
}
