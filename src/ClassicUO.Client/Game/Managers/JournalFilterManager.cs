using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ClassicUO.Configuration;
using ClassicUO.Utility;

namespace ClassicUO.Game.Managers;

public class JournalFilterManager
{
    private JournalFilterSave _save = new();

    public HashSet<string> Filters => _save.Filters;

    private static JournalFilterManager _instance;
    public static JournalFilterManager Instance { get
        {
            if (_instance == null)
                _instance = new();
            return _instance;
        }
    }

    private JournalFilterManager()
    {
        Load();
    }

    public void AddFilter(string filter) => _save.Filters.Add(filter);

    public void RemoveFilter(string filter)
    {
        _save.Filters.Remove(filter);
        _save.DisabledFilters.Remove(filter);
    }

    internal bool IsEnabled(string filter) => !_save.DisabledFilters.Contains(filter);

    internal void SetEnabled(string filter, bool enabled)
    {
        if (!_save.Filters.Contains(filter))
            return;

        if (enabled)
            _save.DisabledFilters.Remove(filter);
        else
            _save.DisabledFilters.Add(filter);
    }

    internal bool ReplaceFilter(string oldFilter, string newFilter)
    {
        if (string.IsNullOrWhiteSpace(newFilter) || !_save.Filters.Contains(oldFilter) || !_save.Filters.Add(newFilter))
            return false;

        _save.Filters.Remove(oldFilter);
        if (_save.DisabledFilters.Remove(oldFilter))
            _save.DisabledFilters.Add(newFilter);
        return true;
    }

    public bool IgnoreMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return false;

        foreach (string filter in _save.Filters)
        {
            if (!string.IsNullOrWhiteSpace(filter) && IsEnabled(filter) && message.Contains(filter, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public void Save(bool resetInstance = true)
    {
        _save.Save();

        if (resetInstance)
            _instance = null;
    }

    public void Load() => _save = JournalFilterSave.LoadForCurrentProfile();

    #nullable enable
    public string? GetJsonExport()
    {
        try
        {
            return JsonSerializer.Serialize(_save.Filters.Where(IsEnabled).ToHashSet(), HashSetContext.Default.HashSetString);
        }
        catch (Exception e)
        {
            Utility.Logging.Log.Error($"Error exporting journal filters to JSON: {e}");
        }

        return null;
    }
    #nullable disable

    public bool ImportFromJson(string json) => ImportFromJson(json, message => GameActions.Print(message, Constants.HUE_SUCCESS));

    internal bool ImportFromJson(string json, Action<string> report)
    {
        try
        {
            HashSet<string> importedFilters = JsonSerializer.Deserialize(json, HashSetContext.Default.HashSetString);

            if (importedFilters != null)
            {
                int addedCount = 0;
                int duplicateCount = 0;

                foreach (string filter in importedFilters)
                {
                    if (!string.IsNullOrWhiteSpace(filter) && _save.Filters.Add(filter))
                    {
                        addedCount++;
                    }
                    else
                    {
                        duplicateCount++;
                    }
                }

                Save(false);

                string message = $"Imported {addedCount} journal filters from clipboard";
                if (duplicateCount > 0)
                    message += $" ({duplicateCount} duplicates skipped)";
                report(message);
                return true;
            }
        }
        catch (Exception e)
        {
            Utility.Logging.Log.Error($"Error importing journal filters from JSON: {e}");
        }

        return false;
    }
}

/// <summary>
/// JSON-backed store for a character's journal filters. Persisted to <c>journal_filters.json</c> in the
/// current profile folder. Saving/loading (with rotating backups) is handled by <see cref="JsonSave{T}"/>.
/// </summary>
public sealed class JournalFilterSave : JsonSave<JournalFilterSave>, INotifyPropertyChanged
{
    public const string JournalFiltersFileName = "journal_filters.json";

    public HashSet<string> Filters { get; set; } = new();
    public HashSet<string> DisabledFilters { get; set; } = new();

    protected override SettingsScope Scope => SettingsScope.Char;

    protected override string FileName => JournalFiltersFileName;

    protected override JsonTypeInfo<JournalFilterSave> TypeInfo => JournalFilterJsonContext.Default.JournalFilterSave;

    /// <summary>Migrates any legacy bare-array file to the wrapped format, then loads the current profile's save.</summary>
    public static JournalFilterSave LoadForCurrentProfile()
    {
        MigrateLegacyFormatIfNeeded();
        return Load();
    }

    // Older versions stored a bare JSON array; rewrite it once into the wrapped format JsonSave expects.
    private static void MigrateLegacyFormatIfNeeded()
    {
        string path = Path.Combine(JsonSaveLocationHelper.GetScopeDirectory(SettingsScope.Char), JournalFiltersFileName);

        if (!File.Exists(path))
            return;

        try
        {
            string json = File.ReadAllText(path);

            if (!json.TrimStart().StartsWith('['))
                return;

            HashSet<string> legacy = JsonSerializer.Deserialize(json, HashSetContext.Default.HashSetString) ?? new HashSet<string>();
            new JournalFilterSave { Filters = legacy }.Save();
        }
        catch (Exception ex)
        {
            Utility.Logging.Log.Error($"Error migrating legacy journal filters: {ex.Message}");
        }
    }
}

[JsonSerializable(typeof(JournalFilterSave), GenerationMode = JsonSourceGenerationMode.Metadata)]
internal partial class JournalFilterJsonContext : JsonSerializerContext
{
}

[JsonSerializable(typeof(HashSet<string>))]
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    IgnoreReadOnlyProperties = false,
    IncludeFields = false)]
public partial class HashSetContext : JsonSerializerContext
{
}
