using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Timers;
using ClassicUO.Common.Enums;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers.Structs;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Input;
using ClassicUO.Utility;

namespace ClassicUO.Game.Managers
{
    internal class OrganizerAgent
    {
        public static OrganizerAgent Instance
        {
            get
            {
                if (field == null)
                    field = new();
                return field;
            }
            private set => field = value;
        }

        private OrganizerConfigSave _save = new();

        public List<OrganizerConfig> OrganizerConfigs => _save.Configs;

        public static void Load()
        {
            Instance = new OrganizerAgent();
            Instance._save = OrganizerConfigSave.LoadForCurrentProfile();
            bool migrated = false;
            foreach (OrganizerConfig config in Instance.OrganizerConfigs)
                migrated |= config != null && MigrateLegacyItems(config);

            if (migrated)
                Instance.Save();
        }

        public void OrganizerCommand(string[] args)
        {
            if (args is not { Length: > 1 })
            {
                // Run all organizers
                Instance?.RunOrganizer();
                return;
            }

            if (int.TryParse(args[1], out int index))
            {
                // Run organizer by index
                Instance?.RunOrganizer(index);
            }
            else
            {
                // Run organizer by name - join all args after command
                string name = string.Join(" ", args.Skip(1));
                Instance?.RunOrganizer(name);
            }
        }

        public void Save() => _save.Save();

        public OrganizerConfig FindConfig(string nameOrIndex)
        {
            if (string.IsNullOrWhiteSpace(nameOrIndex))
                return null;

            nameOrIndex = nameOrIndex.Trim();

            if (int.TryParse(nameOrIndex, out int index))
            {
                if (index >= 0 && index < OrganizerConfigs.Count)
                    return OrganizerConfigs[index];

                return null;
            }

            return FindByNameOrId(nameOrIndex);
        }

        public void SetSourceContainerViaTarget(string nameOrIndex)
        {
            OrganizerConfig config = FindConfig(nameOrIndex);
            if (config == null)
            {
                GameActions.Print(World.Instance, $"Organizer '{nameOrIndex}' not found.", Constants.HUE_ERROR);
                return;
            }

            GameActions.Print(World.Instance, $"Target the source container for organizer '{config.Name}'.");
            World.Instance.TargetManager.SetTargeting((o) =>
            {
                if (o is Item item && item.ItemData.IsContainer)
                {
                    config.SourceContSerial = item.Serial;
                    Save();
                    GameActions.Print(World.Instance, $"Source container for organizer '{config.Name}' set.", Constants.HUE_SUCCESS);
                }
                else
                {
                    GameActions.Print(World.Instance, "That doesn't appear to be a valid container.", Constants.HUE_ERROR);
                }
            });
        }

        public static void Unload()
        {
            Instance?.Save();
            Instance = null;
        }

        public OrganizerConfig NewOrganizerConfig(string name)
        {
            var config = new OrganizerConfig { Name = GetUniqueName(name) };
            OrganizerConfigs.Add(config);
            Save();
            return config;
        }

        public void RenameConfig(OrganizerConfig config, string name)
        {
            config.Name = GetUniqueName(name, config);
            Save();
        }

        public bool DeleteConfig(OrganizerConfig config)
        {
            if (!OrganizerConfigs.Remove(config))
                return false;

            Save();
            return true;
        }

        public OrganizerConfig DupeConfig(OrganizerConfig config)
        {
            if (config == null) return null;

            var dupedConfig = new OrganizerConfig
            {
                Name = GetUniqueName(config.Name + " Copy"),
                SourceContSerial = config.SourceContSerial,
                DestContSerial = config.DestContSerial,
                SharedItemListId = config.SharedItemListId,
                IgnoreItemListId = config.IgnoreItemListId,
                Enabled = false
            };
            OrganizerConfigs.Add(dupedConfig);
            Save();
            return dupedConfig;
        }

        private string GetUniqueName(string baseName, OrganizerConfig except = null)
        {
            string name = baseName;
            int i = 2;
            while (OrganizerConfigs.Any(c => c != except && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                name = $"{baseName} ({i++})";
            return name;
        }

        private OrganizerConfig FindByNameOrId(string nameOrId)
        {
            if (Guid.TryParse(nameOrId, out Guid id) && OrganizerConfigs.Find(config => config.Id == id) is { } preset)
                return preset;

            return OrganizerConfigs.Find(config => string.Equals(config.Name, nameOrId, StringComparison.OrdinalIgnoreCase));
        }

        public void CreateOrganizerMacroButton(OrganizerConfig config)
        {
            var macroManager = MacroManager.TryGetMacroManager(World.Instance);

            if (macroManager == null) return;

            Save();
            var macro = new Macro($"Organizer: {config.Name}", SDL3.SDL.SDL_Keycode.SDLK_UNKNOWN, false, false, false)
            {
                Items = new MacroObjectString(MacroType.ClientCommand, MacroSubType.MSC_NONE, $"organize {config.Id}")
            };

            macroManager.PushToBack(macro);
            UIManager.Add(new MacroButtonGump(World.Instance, macro, Mouse.Position.X, Mouse.Position.Y));
        }
        public void ListOrganizers()
        {
            if (OrganizerConfigs.Count == 0)
            {
                GameActions.Print(World.Instance, "No organizers configured.");
                return;
            }

            GameActions.Print(World.Instance, $"Available organizers ({OrganizerConfigs.Count}):");
            for (int i = 0; i < OrganizerConfigs.Count; i++)
            {
                OrganizerConfig config = OrganizerConfigs[i];
                string status = config.Enabled ? "enabled" : "disabled";
                SharedItemList main = ItemListLibrary.Resolve(config.SharedItemListId);
                string listStatus = main == null ? "no main list" : $"{main.Name}, {main.Items?.Count ?? 0} list entries";
                GameActions.Print(World.Instance, $"  {i}: '{config.Name}' ({status}, {listStatus}, destination: {config.DestContSerial:X})");
            }
        }

        public void RunOrganizer()
        {
            Item backpack = World.Instance.Player?.Backpack;
            if (backpack == null)
            {
                GameActions.Print(World.Instance, "Cannot find player backpack.");
                return;
            }

            int totalOrganized = 0;
            foreach (OrganizerConfig config in OrganizerConfigs)
            {
                if (!config.Enabled) continue;

                Item sourceCont = config.SourceContSerial != 0
                    ? World.Instance.Items.Get(config.SourceContSerial)
                    : backpack;

                if (sourceCont == null)
                {
                    GameActions.Print(World.Instance, $"Cannot find source container for organizer '{config.Name}'.");
                    continue;
                }

                Item destCont = config.DestContSerial != 0
                    ? World.Instance.Items.Get(config.DestContSerial)
                    : backpack;

                if (destCont == null)
                {
                    GameActions.Print($"Cannot find destination container for organizer '{config.Name}'. Using backpack as default.");
                    destCont = backpack;
                }

                totalOrganized += OrganizeItems(sourceCont, destCont, config);
            }

            if (totalOrganized == 0)
            {
                GameActions.Print(World.Instance, "No items were organized.", 33);
            }
        }

        public void RunOrganizer(string name, uint source = 0, uint dest = 0)
        {
            OrganizerConfig config = FindByNameOrId(name);
            if (config == null)
            {
                GameActions.Print(World.Instance, $"Organizer '{name}' not found.", 33);
                return;
            }

            RunSingleOrganizer(config, source, dest);
        }

        internal void RunSelected(OrganizerConfig config) => RunSingleOrganizer(config);

        public void RunOrganizer(int index)
        {
            if (index < 0 || index >= OrganizerConfigs.Count)
            {
                GameActions.Print(World.Instance, $"Organizer index {index} is out of range. Available organizers: 0-{OrganizerConfigs.Count - 1}", 33);
                return;
            }

            OrganizerConfig config = OrganizerConfigs[index];
            RunSingleOrganizer(config);
        }

        private int OrganizeItems(Item sourceCont, Item destCont, OrganizerConfig config)
        {
            SharedItemList mainList = ItemListLibrary.Resolve(config.SharedItemListId);
            if (mainList == null)
                return 0;

            Item backpack = World.Instance.Player?.Backpack;

            // Group items by destination (either per-item destination or config destination)
            var itemsToMoveByDestination = new Dictionary<uint, List<(Item Item, SharedItem Rule)>>();

            var sourceItems = (Item)sourceCont.Items;
            SharedItemList ignoreList = ItemListLibrary.Resolve(config.IgnoreItemListId);

            // First pass: identify items to move and group by destination
            for (Item item = sourceItems; item != null; item = (Item)item.Next)
            {
                if (ignoreList?.Find(item.Graphic, item.Hue) != null)
                    continue;

                SharedItem rule = mainList.Find(item);
                if (rule == null)
                    continue;

                uint itemDestSerial = rule.DestinationContainer != 0 ? rule.DestinationContainer : destCont.Serial;
                if (!itemsToMoveByDestination.TryGetValue(itemDestSerial, out var moves))
                    itemsToMoveByDestination[itemDestSerial] = moves = [];

                moves.Add((item, rule));
            }

            int totalItemsMoved = 0;

            // Second pass: process each destination group
            foreach (var (destinationSerial, itemsForThisDest) in itemsToMoveByDestination)
            {
                Item thisDestCont = World.Instance.Items.Get(destinationSerial);
                if (thisDestCont == null)
                {
                    GameActions.Print($"Cannot find destination container {destinationSerial:X}. Using backpack as default.");
                    thisDestCont = backpack;
                    if (thisDestCont == null) continue;
                }

                var destItems = (Item)thisDestCont.Items;
                bool sameContainer = sourceCont.Serial == thisDestCont.Serial;

                if (sameContainer)
                {
                    // Same container logic
                    foreach (var (item, rule) in itemsForThisDest)
                    {
                        if (!item.ItemData.IsStackable) continue; // non-stackable items can't be organized in the same container

                        ushort amountToMove = rule.MaxAmount > 0 ? (ushort)Math.Min(rule.MaxAmount, ushort.MaxValue) : ushort.MaxValue;
                        ObjectActionQueue.Instance.Enqueue(new MoveRequest(item.Serial, thisDestCont.Serial, amountToMove).ToObjectActionQueueItem(), ActionPriority.MoveItem);
                        totalItemsMoved++;
                    }
                }
                else
                {
                    // Build a lookup of existing item counts in the destination container
                    var destItemCounts = new Dictionary<(ushort Graphic, ushort Hue), int>();
                    for (Item item = destItems; item != null; item = (Item)item.Next)
                    {
                        (ushort Graphic, ushort Hue) key = (item.Graphic, item.Hue);
                        if (destItemCounts.ContainsKey(key))
                            destItemCounts[key] += item.Amount;
                        else
                            destItemCounts[key] = item.Amount;
                    }

                    // Determine which items to move based on config and existing counts in destination
                    foreach (var (item, rule) in itemsForThisDest)
                    {
                        if (rule.MaxAmount <= 0)
                        {
                            // Move all items of this type
                            ObjectActionQueue.Instance.Enqueue(new MoveRequest(item.Serial, thisDestCont.Serial, ushort.MaxValue).ToObjectActionQueueItem(), ActionPriority.MoveItem);
                            totalItemsMoved++;
                        }
                        else
                        {
                            // Move up to the configured amount, considering existing items in destination
                            destItemCounts.TryGetValue((item.Graphic, item.Hue), out int existingCount);
                            int toMove = rule.MaxAmount - existingCount;
                            if (toMove > 0)
                            {
                                ushort actualAmount = (ushort)Math.Min(toMove, item.Amount);
                                ObjectActionQueue.Instance.Enqueue(new MoveRequest(item.Serial, thisDestCont.Serial, actualAmount).ToObjectActionQueueItem(), ActionPriority.MoveItem);
                                // Update the count to avoid over-moving if multiple stacks exist in source
                                destItemCounts[(item.Graphic, item.Hue)] = existingCount + actualAmount;
                                totalItemsMoved++;
                            }
                        }
                    }
                }
            }

            if (totalItemsMoved > 0)
            {
                GameActions.Print($"Organizing {totalItemsMoved} items from '{config.Name}'...", Constants.HUE_SUCCESS);
            }

            return totalItemsMoved;
        }

        private void RunSingleOrganizer(OrganizerConfig config, uint source = 0, uint dest = 0)
        {
            if (!config.Enabled)
            {
                GameActions.Print(World.Instance, $"Organizer '{config.Name}' is disabled.", Constants.HUE_ERROR);
                return;
            }

            if (ItemListLibrary.Resolve(config.SharedItemListId) == null)
            {
                GameActions.Print(World.Instance, $"Select a main list for organizer '{config.Name}'.", Constants.HUE_ERROR);
                return;
            }

            Item backpack = World.Instance.Player?.Backpack;
            if (backpack == null)
            {
                GameActions.Print(World.Instance, "Cannot find player backpack.");
                return;
            }

            Item sourceCont = source != 0
                ? World.Instance.Items.Get(source)
                : config.SourceContSerial != 0
                    ? World.Instance.Items.Get(config.SourceContSerial)
                    : backpack;

            if (sourceCont == null)
            {
                GameActions.Print($"Cannot find source container for organizer '{config.Name}'.");
                return;
            }

            Item destCont = dest != 0 ? World.Instance.Items.Get(dest) :
                config.DestContSerial != 0
                    ? World.Instance.Items.Get(config.DestContSerial)
                    : backpack;

            if (destCont == null)
            {
                GameActions.Print(World.Instance, $"Cannot find destination container for organizer '{config.Name}' (Serial: {config.DestContSerial:X})", Constants.HUE_ERROR);
                return;
            }

            int organized = OrganizeItems(sourceCont, destCont, config);
            if (organized == 0)
            {
                GameActions.Print(World.Instance, $"No items were organized by '{config.Name}'.", Constants.HUE_ERROR);
            }
        }

        #nullable enable
        public string? GetJsonExport(OrganizerConfig config)
        {
            try
            {
                var export = JsonSerializer.SerializeToNode(config, OrganizerAgentContext.Default.OrganizerConfig)!.AsObject();
                var listType = ItemListJsonContext.Default.SharedItemList;
                if (ItemListLibrary.Resolve(config.SharedItemListId) is { } main)
                    export["MainList"] = JsonSerializer.SerializeToNode(main, listType);
                if (ItemListLibrary.Resolve(config.IgnoreItemListId) is { } ignore)
                    export["IgnoreList"] = JsonSerializer.SerializeToNode(ignore, listType);
                return export.ToJsonString();
            }
            catch (Exception e)
            {
                Utility.Logging.Log.Error($"Error exporting organizer to JSON: {e}");
            }

            return null;
        }
        #nullable disable

        public bool ImportFromJson(string json)
        {
            try
            {
                var export = JsonNode.Parse(json)?.AsObject();
                OrganizerConfig importedConfig = export?.Deserialize(OrganizerAgentContext.Default.OrganizerConfig);
                if (importedConfig == null)
                    return false;

                var library = ItemListLibrary.Instance;
                var lists = new[]
                {
                    ReadMissingList("MainList", importedConfig.SharedItemListId),
                    ReadMissingList("IgnoreList", importedConfig.IgnoreItemListId)
                }.OfType<SharedItemList>().DistinctBy(list => list.Id).ToArray();
                if (lists.Length > 0 && !TryAddLists(library, lists))
                    return false;

                importedConfig.Name = GetUniqueName(importedConfig.Name);
                importedConfig.Id = Guid.NewGuid();
                importedConfig.Enabled = false;
                MigrateLegacyItems(importedConfig);
                OrganizerConfigs.Add(importedConfig);
                Save();
                GameActions.Print($"Imported organizer '{importedConfig.Name}'.", Constants.HUE_SUCCESS);
                return true;

                SharedItemList ReadMissingList(string key, Guid? id)
                {
                    if (id is not Guid listId || library.Find(listId) != null || export[key] is not { } node)
                        return null;

                    var list = node.Deserialize(ItemListJsonContext.Default.SharedItemList);
                    if (list == null || list.Id != listId || list.Items == null)
                        throw new JsonException($"Invalid organizer {key}.");

                    return list;
                }
            }
            catch (Exception e)
            {
                Utility.Logging.Log.Error($"Error importing organizer from JSON: {e}");
            }

            return false;
        }

        private static bool MigrateLegacyItems(OrganizerConfig config)
        {
            if (config.LegacyItemConfigs is not { Count: > 0 })
                return false;

            var library = ItemListLibrary.Instance;
            SharedItemList main = ItemListLibrary.Resolve(config.SharedItemListId);
            if (config.SharedItemListId.HasValue && main == null)
                return false;

            SharedItem[] items = config.BuildMigratedItems(main);
            if (main == null ? items.Length > 0 : !items.SequenceEqual(main.Items ?? []))
            {
                string owner = ProfileManager.CurrentProfile?.CharacterName ?? "Character";
                string name = $"{owner} Organizer - {config.Name}";
                var migrated = library.Lists.Find(list => list != null &&
                    (list.Name == name || list.Name?.StartsWith(name + " (", StringComparison.Ordinal) == true) &&
                    list.Items?.SequenceEqual(items) == true);
                if (migrated == null)
                {
                    migrated = new SharedItemList { Name = name, Items = items.ToList() };
                    if (!TryAddLists(library, migrated))
                        return false;
                }

                config.SharedItemListId = migrated.Id;
            }

            config.LegacyItemConfigs = null;
            return true;
        }

        private static bool TryAddLists(ItemListLibrary library, params SharedItemList[] lists)
        {
            if (!library.IsAvailable)
                return false;

            foreach (var list in lists)
            {
                list.Name = library.AvailableName(list.Name);
                library.Lists.Add(list);
            }

            // Organizer configs reference a separate file. Keep the old rules until the lists are written.
            library.SaveChanges();
            var saved = library.ReadPersisted();
            bool success = true;
            foreach (var list in lists)
            {
                if (saved?.Find(list.Id)?.Items?.SequenceEqual(list.Items) == true)
                    continue;

                library.Lists.Remove(list);
                success = false;
            }
            return success;
        }
    }

    [JsonSerializable(typeof(List<OrganizerConfig>))]
    [JsonSerializable(typeof(OrganizerConfig))]
    [JsonSerializable(typeof(OrganizerConfigSave))]
    internal partial class OrganizerAgentContext : JsonSerializerContext
    { }

    /// <summary>
    /// JSON-backed store for a character's organizer configs. Persisted to <c>OrganizerConfig.json</c> in the
    /// current profile folder. Saving/loading (with rotating backups) is handled by <see cref="JsonSave{T}"/>.
    /// </summary>
    internal sealed class OrganizerConfigSave : JsonSave<OrganizerConfigSave>, INotifyPropertyChanged
    {
        public const string OrganizerConfigFileName = "OrganizerConfig.json";

        public List<OrganizerConfig> Configs { get; set; } = new();

        protected override SettingsScope Scope => SettingsScope.Char;

        protected override string FileName => OrganizerConfigFileName;

        protected override JsonTypeInfo<OrganizerConfigSave> TypeInfo => OrganizerAgentContext.Default.OrganizerConfigSave;

        /// <summary>Migrates any legacy bare-array file to the wrapped format, then loads the current profile's save.</summary>
        public static OrganizerConfigSave LoadForCurrentProfile()
        {
            MigrateLegacyFormatIfNeeded();
            return Load();
        }

        internal static IEnumerable<(string Name, SharedItem[] Items)> GetOtherCharacterLists()
        {
            string root = string.IsNullOrWhiteSpace(Settings.GlobalSettings.ProfilesPath)
                ? Path.Combine(JsonSaveLocationHelper.DataDirectory, "Profiles")
                : Settings.GlobalSettings.ProfilesPath;

            foreach (var (character, path) in Utility.Extensions.GetAllCharacterPaths(root).OrderBy(entry => entry.Key))
            {
                if (path == ProfileManager.ProfilePath)
                    continue;

                string file = Path.Combine(path, OrganizerConfigFileName);
                if (!File.Exists(file))
                    continue;

                List<OrganizerConfig> configs;
                try
                {
                    string json = File.ReadAllText(file);
                    configs = json.TrimStart().StartsWith('[')
                        ? JsonSerializer.Deserialize(json, OrganizerAgentContext.Default.ListOrganizerConfig)
                        : JsonSerializer.Deserialize(json, OrganizerAgentContext.Default.OrganizerConfigSave)?.Configs;
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                {
                    Utility.Logging.Log.Warn($"Could not read legacy Organizer rules: {ex.Message}");
                    continue;
                }

                foreach (var config in configs ?? [])
                {
                    if (config?.LegacyItemConfigs is not { Count: > 0 })
                        continue;

                    var main = ItemListLibrary.Resolve(config.SharedItemListId);
                    if (config.SharedItemListId.HasValue && main == null)
                        continue;

                    var items = config.BuildMigratedItems(main);
                    if (items.Length > 0)
                        yield return ($"{character} · Organizer - {config.Name}", items);
                }
            }
        }

        // Older versions stored a bare JSON array; rewrite it once into the wrapped format JsonSave expects.
        private static void MigrateLegacyFormatIfNeeded()
        {
            string path = Path.Combine(JsonSaveLocationHelper.GetScopeDirectory(SettingsScope.Char), OrganizerConfigFileName);

            if (!File.Exists(path))
                return;

            try
            {
                string json = File.ReadAllText(path);

                if (!json.TrimStart().StartsWith('['))
                    return;

                List<OrganizerConfig> legacy = JsonSerializer.Deserialize(json, OrganizerAgentContext.Default.ListOrganizerConfig) ?? new List<OrganizerConfig>();
                new OrganizerConfigSave { Configs = legacy }.Save();
            }
            catch (Exception ex)
            {
                Utility.Logging.Log.Error($"Error migrating legacy organizer configs: {ex.Message}");
            }
        }
    }

    internal class OrganizerConfig
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "Organizer";
        public uint SourceContSerial { get; set; }
        public uint DestContSerial { get; set; }
        public bool Enabled { get; set; } = true;
        [JsonPropertyName("ItemConfigs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<OrganizerItemConfig> LegacyItemConfigs { get; set; }
        public Guid? SharedItemListId { get; set; }
        public Guid? IgnoreItemListId { get; set; }

        internal SharedItem[] BuildMigratedItems(SharedItemList main)
        {
            var rules = (LegacyItemConfigs ?? []).Where(rule => rule is { Graphic: > 0 });
            if (main == null)
            {
                return rules.Where(rule => rule.Enabled).Select(rule => new SharedItem(rule.Graphic, rule.Hue, "")
                {
                    MaxAmount = rule.Amount,
                    DestinationContainer = rule.DestContSerial
                }).ToArray();
            }

            // A linked list supplied membership. The first legacy rule of each exact key supplied settings.
            var overrides = rules.ToLookup(rule => (rule.Graphic, rule.Hue));
            var migrated = new List<SharedItem>();
            foreach (var item in main.Items ?? [])
            {
                if (item == null)
                    continue;

                var rule = overrides[(item.Graphic, item.Hue)].FirstOrDefault();
                if (rule == null)
                    migrated.Add(item);
                else if (rule.Enabled)
                    migrated.Add(item with { MaxAmount = rule.Amount, DestinationContainer = rule.DestContSerial });
            }

            return migrated.ToArray();
        }
    }

    internal class OrganizerItemConfig
    {
        public ushort Graphic { get; set; }
        public ushort Hue { get; set; } = ushort.MaxValue;
        public ushort Amount { get; set; } = 0; // 0 = move all; otherwise move up to this amount
        public bool Enabled { get; set; } = true;
        public uint DestContSerial { get; set; } = 0; // 0 = use configuration's destination

    }
}
