using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Network;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers
{
    using System.Text.Json.Serialization;
    using ClassicUO.Utility;

    [JsonSerializable(typeof(List<BuySellItemConfig>))]
    [JsonSerializable(typeof(BuySellItemConfig))]
    public partial class BuySellAgentJsonContext : JsonSerializerContext
    {
    }
    public class BuySellAgent
    {
        public static BuySellAgent Instance
        {
            get
            {
                if (field == null)
                    field = new();
                return field;
            }
            private set => field = value;
        }

        public List<BuySellItemConfig> SellConfigs => sellItems;
        public List<BuySellItemConfig> BuyConfigs => buyItems;

        private List<BuySellItemConfig> sellItems;
        private List<BuySellItemConfig> buyItems;
        private ShopListSave shopLists;

        /// <summary>The character's buy and sell lists; each agent uses its side's active list.</summary>
        public ShopListSave ShopLists =>
            shopLists ??= ShopListSave.LoadOrMigrate(ProfileManager.CurrentProfile, buyItems, sellItems);

        private readonly Dictionary<uint, VendorSellInfo> sellPackets = new Dictionary<uint, VendorSellInfo>();
        public static void Load()
        {
            Instance = new BuySellAgent();

            try
            {
                string savePath = Path.Combine(ProfileManager.ProfilePath, "SellAgentConfig.json");
                if (File.Exists(savePath))
                {
                    Instance.sellItems = JsonSerializer.Deserialize(File.ReadAllText(savePath), BuySellAgentJsonContext.Default.ListBuySellItemConfig);
                }

                savePath = Path.Combine(ProfileManager.ProfilePath, "BuyAgentConfig.json");
                if (File.Exists(savePath))
                {
                    Instance.buyItems = JsonSerializer.Deserialize(File.ReadAllText(savePath), BuySellAgentJsonContext.Default.ListBuySellItemConfig);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error loading BuySellAgent config: {ex}");
            }
        }

        public void DeleteConfig(BuySellItemConfig config)
        {
            if (config == null) return;

            SellConfigs?.Remove(config);
            BuyConfigs?.Remove(config);
        }

        public bool TryGetSellConfig(ushort graphic, ushort hue, out BuySellItemConfig config)
        {
            config = sellItems?.FirstOrDefault(c => c.Graphic == graphic && c.Hue == hue);
            return config != null;
        }

        public BuySellItemConfig NewSellConfig()
        {
            var r = new BuySellItemConfig();

            sellItems ??= new List<BuySellItemConfig>();

            sellItems.Add(r);
            return r;
        }
        public BuySellItemConfig NewBuyConfig()
        {
            var r = new BuySellItemConfig();

            buyItems ??= new List<BuySellItemConfig>();

            buyItems.Add(r);
            return r;
        }

        #nullable enable
        public static string? GetJsonExport(AgentType type)
        {
            try
            {
                switch (type)
                {
                    case AgentType.Buy:
                        return JsonSerializer.Serialize(Instance.buyItems ?? [], BuySellAgentJsonContext.Default.ListBuySellItemConfig);
                    case AgentType.Sell:
                        return JsonSerializer.Serialize(Instance.sellItems ?? [], BuySellAgentJsonContext.Default.ListBuySellItemConfig);
                }
            }
            catch (Exception e)
            {
                Log.Error(e.ToString());
            }

            return null;
        }
        #nullable disable

        public static bool ImportFromJson(string json, AgentType type)
        {
            try
            {
                List<BuySellItemConfig> res = JsonSerializer.Deserialize(json, BuySellAgentJsonContext.Default.ListBuySellItemConfig);
                if (res == null || res.Any(static rule => rule == null))
                    return false;

                switch (type)
                {
                    case AgentType.Buy:
                        (Instance.buyItems ??= []).AddRange(res);
                        break;
                    case AgentType.Sell:
                        (Instance.sellItems ??= []).AddRange(res);
                        break;
                }

                return true;
            }
            catch (Exception e)
            {
                Log.Error(e.ToString());
            }

            return false;
        }

        public static void Unload()
        {
            if (Instance != null)
            {
                if (Instance.sellItems != null)
                {
                    string savePath = Path.Combine(ProfileManager.ProfilePath, "SellAgentConfig.json");
                    FileSystemHelper.WriteAllTextSafe(savePath, JsonSerializer.Serialize(Instance.sellItems, BuySellAgentJsonContext.Default.ListBuySellItemConfig));
                }

                Instance.shopLists?.Save();

                if (Instance.buyItems != null)
                {
                    string savePath = Path.Combine(ProfileManager.ProfilePath, "BuyAgentConfig.json");
                    FileSystemHelper.WriteAllTextSafe(savePath, JsonSerializer.Serialize(Instance.buyItems, BuySellAgentJsonContext.Default.ListBuySellItemConfig));
                }
            }

            Instance = null;
        }

        public void HandleBuyPacket(List<Item> items, uint shopSerial)
        {
            if (!ProfileManager.CurrentProfile.BuyAgentEnabled) return;

            // Only the active buy list is used.
            if (ShopLists.Buy.GetActive() is not { } activeList) return;

            var buyList = new Dictionary<uint, ushort>();
            long val = 0;
            int total_count = 0;
            int unique_items = 0;
            int max_total_items = ProfileManager.CurrentProfile.BuyAgentMaxItems;
            bool limit_total_items = max_total_items > 0;
            int max_unique_items = ProfileManager.CurrentProfile.BuyAgentMaxUniques;
            bool limit_unique_items = max_unique_items > 0;

            foreach (BuySellItemConfig buyConfigEntry in activeList.SyncRules())
            {
                if (!buyConfigEntry.Enabled) continue;

                int current_count = 0;
                int maxToBuy = activeList.TakeAll ? ushort.MaxValue : buyConfigEntry.MaxAmount;

                // Check restock functionality
                if (buyConfigEntry.RestockUpTo > 0)
                {
                    int currentBackpackAmount = GetItemCount(buyConfigEntry.Graphic, buyConfigEntry.Hue);
                    if (currentBackpackAmount >= buyConfigEntry.RestockUpTo)
                    {
                        continue; // Already have enough, skip this item
                    }
                    maxToBuy = Math.Min(buyConfigEntry.RestockUpTo - currentBackpackAmount, maxToBuy);
                }

                if (!activeList.TakeAll || buyConfigEntry.RestockUpTo > 0)
                    maxToBuy -= items.Sum(item => buyConfigEntry.IsMatch(item.Graphic, item.Hue) ? buyList.GetValueOrDefault(item.Serial) : 0);

                foreach (Item item in items)
                {
                    if (!buyConfigEntry.IsMatch(item.Graphic, item.Hue)) continue;

                    // Skip if item price exceeds configured max price
                    if (buyConfigEntry.MaxPrice > 0 && item.Price > buyConfigEntry.MaxPrice) continue;

                    if (current_count >= maxToBuy) break;

                    if (limit_unique_items && unique_items >= max_unique_items) break;

                    if (limit_total_items && current_count + total_count >= max_total_items) break;

                    int amount_buyable = maxToBuy - current_count;
                    if (limit_total_items)
                    {
                        amount_buyable = Math.Min(amount_buyable, max_total_items - total_count - current_count);
                    }

                    int reserved = ReserveStock(buyList, item.Serial, item.Amount, amount_buyable);
                    current_count += reserved;
                    val += (long)item.Price * reserved;
                }

                if (current_count > 0)
                    unique_items++;

                total_count += current_count;
            }

            if (buyList.Count == 0) return;

            AsyncNetClient.Socket.Send_BuyRequest(shopSerial, buyList.Select(item => Tuple.Create(item.Key, item.Value)).ToArray());
            GameActions.Print(Client.Game.UO.World, $"Purchased {total_count} items for {val} gold.");
            UIManager.GetGump(shopSerial)?.Dispose();
        }

        private int GetItemCount(ushort graphic, ushort hue, Item container = null)
        {
            Item searchContainer = container ?? World.Instance.Player?.Backpack;
            if (searchContainer == null) return 0;

            bool subContainers = ProfileManager.CurrentProfile.BuyAgentSubContainers;

            int count = 0;
            var item = (Item)searchContainer.Items;
            while (item != null)
            {
                if (item.Graphic == graphic && (hue == ushort.MaxValue || item.Hue == hue))
                {
                    count += item.Amount;
                }

                if(subContainers && !item.IsEmpty)
                {
                    count += GetItemCount(graphic, hue, item);
                }
                item = (Item)item.Next;
            }
            return count;
        }

        public void HandleSellPacket(uint vendorSerial, uint serial, ushort graphic, ushort hue, ushort amount, uint price)
        {
            if (!ProfileManager.CurrentProfile.SellAgentEnabled) return;

            if (!sellPackets.ContainsKey(vendorSerial))
                sellPackets.Add(vendorSerial, new VendorSellInfo());

            sellPackets[vendorSerial].HandleSellPacketItem(serial, graphic, hue, amount, price);
        }

        public void HandleSellPacketFinished(uint vendorSerial)
        {
            if (!ProfileManager.CurrentProfile.SellAgentEnabled) return;

            // Only the active sell list is used.
            if (ShopLists.Sell.GetActive() is not { } activeList)
            {
                sellPackets.Remove(vendorSerial);
                return;
            }

            if (!sellPackets.Remove(vendorSerial, out VendorSellInfo vendorStock))
                return;

            var sellList = new Dictionary<uint, ushort>();
            long val = 0;
            int total_count = 0;
            int unique_items = 0;
            int max_total_items = ProfileManager.CurrentProfile.SellAgentMaxItems;
            bool limit_total_items = max_total_items > 0;
            int max_unique_items = ProfileManager.CurrentProfile.SellAgentMaxUniques;
            bool limit_unique_items = max_unique_items > 0;

            foreach (BuySellItemConfig sellConfig in activeList.SyncRules())
            {
                if (!sellConfig.Enabled) continue;

                int current_count = 0;
                int maxToSell = activeList.TakeAll ? ushort.MaxValue : sellConfig.MaxAmount;

                // Check minimum on hand logic
                if (sellConfig.RestockUpTo > 0)
                {
                    int backpackTotal = GetItemCount(sellConfig.Graphic, sellConfig.Hue);
                    maxToSell = Math.Min(maxToSell, backpackTotal - sellConfig.RestockUpTo);
                }

                if (!activeList.TakeAll || sellConfig.RestockUpTo > 0)
                    maxToSell -= vendorStock.AvailableItems.Sum(item => sellConfig.IsMatch(item.Graphic, item.Hue)
                        ? sellList.GetValueOrDefault(item.Serial) : 0);

                foreach (VendorSellItemData item in vendorStock.AvailableItems)
                {
                    if (!sellConfig.IsMatch(item.Graphic, item.Hue)) continue;

                    if (current_count >= maxToSell) break;

                    if (limit_unique_items && unique_items >= max_unique_items) break;

                    if (limit_total_items && current_count + total_count >= max_total_items) break;

                    int amount_sellable = maxToSell - current_count;
                    if (limit_total_items)
                    {
                        amount_sellable = Math.Min(amount_sellable, max_total_items - total_count - current_count);
                    }

                    int reserved = ReserveStock(sellList, item.Serial, item.Amount, amount_sellable);
                    current_count += reserved;
                    val += (long)item.Price * reserved;
                }

                if (current_count > 0)
                    unique_items++;

                total_count += current_count;
            }
            if (sellList.Count == 0) return;

            AsyncNetClient.Socket.Send_SellRequest(vendorSerial, sellList.Select(item => Tuple.Create(item.Key, item.Value)).ToArray());
            GameActions.Print(Client.Game.UO.World, $"Sold {total_count} items for {val} gold.");
            UIManager.ForEach<ModernShopGump>(g => g.Dispose(), vendorSerial);
            UIManager.ForEach<ShopGump>(g => g.Dispose(), vendorSerial);
        }

        internal static int ReserveStock(Dictionary<uint, ushort> order, uint serial, ushort available, int requested)
        {
            ushort alreadyReserved = order.GetValueOrDefault(serial);
            int amount = Math.Max(0, Math.Min(available - alreadyReserved, requested));
            if (amount > 0)
                order[serial] = (ushort)(alreadyReserved + amount);
            return amount;
        }
    }

    public class BuySellItemConfig
    {
        public ushort Graphic { get; set; }
        public ushort Hue { get; set; } = ushort.MaxValue;
        public ushort MaxAmount { get; set; } = ushort.MaxValue;
        public ushort RestockUpTo { get; set; } = 0;
        public uint MaxPrice { get; set; } = 0;
        public bool Enabled { get; set; } = true;

        public bool IsMatch(ushort graphic, ushort hue) => graphic == Graphic && (hue == Hue || Hue == ushort.MaxValue);
    }

    public class VendorSellInfo
    {
        public List<VendorSellItemData> AvailableItems { get; set; } = new List<VendorSellItemData>();
        public void HandleSellPacketItem(uint serial, ushort graphic, ushort hue, ushort amount, uint price) => AvailableItems.Add(new VendorSellItemData(serial, graphic, hue, amount, price));
    }

    public class VendorSellItemData
    {
        public uint Serial;
        public ushort Graphic;
        public ushort Hue;
        public ushort Amount;
        public uint Price;

        public VendorSellItemData(uint serial, ushort graphic, ushort hue, ushort amount, uint price)
        {
            Serial = serial;
            Graphic = graphic;
            Hue = hue;
            Amount = amount;
            Price = price;
        }
    }

    public enum AgentType
    {
        Buy,
        Sell
    }
}
