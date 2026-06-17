using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using DungeonRunners.Engine;
using DungeonRunners.Utilities;
using DungeonRunners.Networking;
using DungeonRunners.Core;
using DungeonRunners.Data;
namespace DungeonRunners.Gameplay

{
    public static class MerchantRuntime
    {
        private static Dictionary<string, MerchantData> _merchants = new Dictionary<string, MerchantData>(StringComparer.OrdinalIgnoreCase);

        private static Dictionary<string, MerchantRuntimeData> _runtimeMerchants = new Dictionary<string, MerchantRuntimeData>(StringComparer.OrdinalIgnoreCase);

        private static Dictionary<string, DateTime> _lastRegeneration = new Dictionary<string, DateTime>();

        private static readonly List<PendingMerchantRefreshAdd> _pendingMerchantRefreshAdds = new List<PendingMerchantRefreshAdd>();
        private static readonly object _merchantRandomLock = new object();
        private static readonly System.Random _merchantRandom = new System.Random();
        private static bool VerboseMerchantItemLogging => ServerSettings.GetBool("verboseMerchantItemLogging", false);

        private static List<SellableItem> _sellableItems = new List<SellableItem>();

        public static readonly Dictionary<string, uint> _buyPrices = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        public static void SetBuyPrice(string connId, string gcClass, uint price)
        {
            string key = connId + ":" + gcClass.ToLowerInvariant();
            _buyPrices[key] = price;
        }

        public static uint GetBuyPrice(string connId, string gcClass)
        {
            string key = connId + ":" + gcClass.ToLowerInvariant();
            return _buyPrices.TryGetValue(key, out uint p) ? p : 0;
        }
        private static Dictionary<string, ItemDimensions> _itemDimensions = new Dictionary<string, ItemDimensions>();

        private static bool _initialized = false;

        private const int CLIENT_REFRESH_TIMER_TICKS = 0x2328;
        private const double REFRESH_TIMER_TICKS_PER_SECOND = 30.0;
        public const float DEFAULT_REFRESH_INTERVAL_SECONDS = (float)(CLIENT_REFRESH_TIMER_TICKS / REFRESH_TIMER_TICKS_PER_SECOND);
        private const double REFRESH_ADD_DELAY_SECONDS = 0x000F / REFRESH_TIMER_TICKS_PER_SECOND;

        public static void Initialize()
        {
            if (_initialized) return;

            Debug.LogError("[MERCHANT-RUNTIME] init");

            LoadItemDimensions();
            LogAuthoredModSlotCoverage();
            LoadSellableItems();
            LoadMerchants();
            InitializeRuntimeMerchants();

            _initialized = true;
        }

        public static void ValidateAllSellableItems()
        {
            Debug.LogError("[MERCHANT-RUNTIME] validateSellableItems start");
            int bad = 0;
            int notInDb = 0;
            foreach (var item in _sellableItems)
            {
                string lookup = item.gcType.ToLowerInvariant();
                if (lookup.StartsWith("items.pal."))
                    lookup = lookup.Substring(10);

                var data = DatabaseLoader.FindItem(lookup);
                if (data == null)
                {
                    Debug.LogError($"[MERCHANT-VALIDATE] item={item.gcType} lookup={lookup} reason=missing-db");
                    notInDb++;
                    bad++;
                    continue;
                }

                if (data.modCount < 0 || data.modCount > 10)
                {
                    Debug.LogError($"[MERCHANT-VALIDATE] item={item.gcType} modCount={data.modCount} reason=mod-count");
                    bad++;
                }

                if (data.inventoryWidth <= 0 || data.inventoryHeight <= 0)
                {
                    Debug.LogError($"[MERCHANT-VALIDATE] item={item.gcType} dimensions={data.inventoryWidth}x{data.inventoryHeight} reason=dimensions");
                    bad++;
                }

                string lower = item.gcType.ToLowerInvariant();
                if (lower.Contains("shield") || lower.Contains("buckler") || lower.Contains("shoulder") || lower.Contains("pauldron"))
                {
                    Debug.LogError($"[MERCHANT-VALIDATE] item={item.gcType} modCount={data.modCount} dimensions={data.inventoryWidth}x{data.inventoryHeight} slot={data.slotType ?? "NULL"} class=shield-shoulder");
                }
            }
            Debug.LogError($"[MERCHANT-RUNTIME] validateSellableItems items={_sellableItems.Count} problems={bad} missingDb={notInDb}");
        }



        public static void ResetAllTimers()
        {
            foreach (var key in _lastRegeneration.Keys.ToList())
            {
                _lastRegeneration[key] = DateTime.UtcNow;
            }
            Debug.LogError("[MERCHANT-RUNTIME] timersReset source=server-ready");
        }





        private static void LoadSellableItems()
        {
            try
            {
                _sellableItems = new List<SellableItem>();
                using (var connection = DungeonRunners.Database.GameDatabase.GetConnection())
                using (var reader = DungeonRunners.Database.GameDatabase.ExecuteReader(connection, "SELECT gc_type, name, gold_value, gc_gold_value FROM sellable_items"))
                {
                    while (reader.Read())
                    {
                        _sellableItems.Add(new SellableItem
                        {
                            gcType = DungeonRunners.Database.GameDatabase.GetString(reader, "gc_type"),
                            name = DungeonRunners.Database.GameDatabase.GetString(reader, "name"),
                            goldValue = DungeonRunners.Database.GameDatabase.GetFloat(reader, "gold_value"),
                            gcGoldValue = DungeonRunners.Database.GameDatabase.GetFloat(reader, "gc_gold_value")
                        });
                    }
                }
                Debug.LogError($"[MERCHANT-RUNTIME] sellableItems={_sellableItems.Count} source=sqlite");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MERCHANT-RUNTIME] sellableItems error={ex.Message}");
            }
        }

        private static void LoadItemDimensions()
        {
            try
            {
                using (var connection = DungeonRunners.Database.GameDatabase.GetConnection())
                using (var reader = DungeonRunners.Database.GameDatabase.ExecuteReader(connection,
                    "SELECT gc_type, width, height FROM item_dimensions"))
                {
                    while (reader.Read())
                    {
                        string key = reader.GetString(0);
                        _itemDimensions[key] = new ItemDimensions
                        {
                            width = reader.GetInt32(1),
                            height = reader.GetInt32(2)
                        };
                    }
                }
                Debug.LogError($"[MERCHANT-RUNTIME] itemDimensions={_itemDimensions.Count} source=sqlite");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MERCHANT-RUNTIME] itemDimensions error={ex.Message}");
            }
        }

        private static void LoadMerchants()
        {
            _merchants.Clear();

            if (DatabaseLoader.Merchants == null || DatabaseLoader.Merchants.Count == 0)
            {
                Debug.LogError("[MERCHANT-RUNTIME] merchants=0 source=database");
                return;
            }

            foreach (var merchantData in DatabaseLoader.Merchants)
            {
                _merchants[merchantData.npcGcType] = merchantData;
                Debug.LogError($"[MERCHANT-RUNTIME] merchant={merchantData.npcGcType} loaded=True");

                foreach (var inv in merchantData.inventories)
                {
                    float regenInterval = inv.regenerateIntervalSeconds > 0 ? inv.regenerateIntervalSeconds : DEFAULT_REFRESH_INTERVAL_SECONDS;
                    string invType = inv.staticContents ? "STATIC" : $"DYNAMIC (regen every {regenInterval}s)";
                    Debug.LogError($"[MERCHANT-RUNTIME] inventory='{inv.name}' id={inv.id} type={invType} items={inv.items.Count}");
                }
            }

            Debug.LogError($"[MERCHANT-RUNTIME] merchants={_merchants.Count}");
        }

        private static void InitializeRuntimeMerchants()
        {
            _runtimeMerchants.Clear();
            _lastRegeneration.Clear();

            foreach (var merchantEntry in _merchants)
            {
                var merchantData = merchantEntry.Value;
                var runtimeMerchant = CreateRuntimeMerchant(merchantData);
                _runtimeMerchants[merchantEntry.Key] = runtimeMerchant;
            }

            Debug.LogError($"[MERCHANT-RUNTIME] runtimeMerchants={_runtimeMerchants.Count}");
        }

        private static MerchantRuntimeData CreateRuntimeMerchant(MerchantData merchantData)
        {
            var runtime = new MerchantRuntimeData
            {
                npcGcType = merchantData.npcGcType,
                merchantGcType = merchantData.merchantGcType,
                inventories = new List<MerchantInventoryRuntimeData>()
            };
            foreach (var invData in merchantData.inventories)
            {
                string itemGenerator = invData.itemGenerator;
                int minItemLevel = invData.minItemLevel;
                int maxItemLevel = invData.maxItemLevel;
                float regenerateIntervalSeconds = invData.regenerateIntervalSeconds > 0 ? invData.regenerateIntervalSeconds : DEFAULT_REFRESH_INTERVAL_SECONDS;
                string label = invData.label;
                ApplyMerchantInventoryOverrides(merchantData.npcGcType, invData, ref itemGenerator, ref minItemLevel, ref maxItemLevel, ref regenerateIntervalSeconds, ref label);

                var runtimeInv = new MerchantInventoryRuntimeData
                {
                    name = invData.name,
                    gcType = invData.gcType,
                    id = invData.id,
                    label = label,
                    width = invData.width,
                    height = invData.height,
                    staticContents = invData.staticContents,
                    autoGenerateItems = invData.autoGenerateItems,
                    itemGenerator = itemGenerator,
                    minItemLevel = minItemLevel,
                    maxItemLevel = maxItemLevel,
                    regenerateIntervalSeconds = regenerateIntervalSeconds,
                    items = new List<MerchantItemRuntimeData>()
                };
                if (invData.staticContents)
                {
                    foreach (var item in invData.items)
                    {
                        int tier = RPGSettings.GetTierFromGcType(item.gcType);
                        var itemRarity = RPGSettings.GetRarityFromTier(tier);

                        if (RPGSettings.IsMythicPALItem(item.gcType) || IsEnabledMythicItem(item.gcType))
                            itemRarity = ItemRarity.Mythic;

                        int level = RPGSettings.GetItemLevel(item.gcType);

                        float baseGoldValue = RPGSettings.GetBaseGoldValue(item.gcType);
                        if (_sellableItems != null)
                        {
                            string lookupType = item.gcType.ToLowerInvariant();
                            if (lookupType.StartsWith("items.pal."))
                                lookupType = lookupType.Substring(10);
                            var sellable = _sellableItems.FirstOrDefault(s =>
                                string.Equals(s.gcType, lookupType, StringComparison.OrdinalIgnoreCase) ||
                                s.gcType.EndsWith(lookupType, StringComparison.OrdinalIgnoreCase));
                            if (sellable != null && sellable.gcGoldValue > 0)
                                baseGoldValue = sellable.gcGoldValue;
                        }

                        uint itemPrice = RPGSettings.CalculateBuyPrice(level, itemRarity, baseGoldValue);

                        runtimeInv.items.Add(new MerchantItemRuntimeData
                        {
                            gcType = item.gcType,
                            inventoryX = item.inventoryX,
                            inventoryY = item.inventoryY,
                            id = item.id,
                            quantity = item.quantity > 0 ? item.quantity : 1,
                            level = level,
                            rarity = itemRarity,
                            price = itemPrice,
                            goldValue = baseGoldValue,
                            scaleMod = RPGSettings.GetRandomScaleMod(itemRarity)
                        });
                        if (item.id >= runtime.nextItemId)
                            runtime.nextItemId = item.id + 1;
                    }
                    bool hasQuestItems = runtimeInv.items.Any(runtimeItem =>
                        runtimeItem.gcType.IndexOf("QuestItem", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (hasQuestItems)
                    {
                        runtimeInv.serverSendsItems = true;
                        int nextQuestId = 500;
                        foreach (var questItem in runtimeInv.items)
                        {
                            questItem.id = nextQuestId++;
                            Debug.LogError($"[MERCHANT-RUNTIME] questItem={questItem.gcType} uniqueId={questItem.id}");
                        }
                        if (nextQuestId > runtime.nextItemId)
                            runtime.nextItemId = nextQuestId;
                    }
                }
                else if (invData.autoGenerateItems)
                {
                    GenerateInventoryItems(runtime, runtimeInv, invData, 1);
                    string regenKey = $"{merchantData.npcGcType}_{invData.id}";
                    _lastRegeneration[regenKey] = DateTime.UtcNow;
                }
                runtime.inventories.Add(runtimeInv);
            }
            return runtime;
        }

        private static void ApplyMerchantInventoryOverrides(string npcGcType, MerchantInventoryData invData, ref string itemGenerator, ref int minItemLevel, ref int maxItemLevel, ref float regenerateIntervalSeconds, ref string label)
        {
            if (string.Equals(npcGcType, "world.tutorial.npc.HermitVendor", StringComparison.OrdinalIgnoreCase) && invData.id == 2)
            {
                itemGenerator = "MerchantSuperiorIG";
                minItemLevel = 3;
                maxItemLevel = 10;
                regenerateIntervalSeconds = DEFAULT_REFRESH_INTERVAL_SECONDS;
            }

            if (string.Equals(npcGcType, "world.town.npc.VendorWeapon1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(npcGcType, "world.town.npc.VendorWeapon2", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(npcGcType, "world.town.npc.VendorWeapon3", StringComparison.OrdinalIgnoreCase))
            {
                regenerateIntervalSeconds = DEFAULT_REFRESH_INTERVAL_SECONDS;
                if (invData.id == 3)
                    label = "Scrap Heap";
            }

            if (string.Equals(npcGcType, "world.town.npc.VendorWeapon1", StringComparison.OrdinalIgnoreCase))
            {
                minItemLevel = 3;
                maxItemLevel = 20;
            }
            else if (string.Equals(npcGcType, "world.town.npc.VendorWeapon2", StringComparison.OrdinalIgnoreCase))
            {
                minItemLevel = 20;
                maxItemLevel = 50;
            }
            else if (string.Equals(npcGcType, "world.town.npc.VendorWeapon3", StringComparison.OrdinalIgnoreCase))
            {
                minItemLevel = 40;
                maxItemLevel = 100;
            }
        }



        public static bool CheckAndRegenerateInventories(string npcGcType)
        {
            if (!_runtimeMerchants.TryGetValue(npcGcType, out var runtimeMerchant))
                return false;
            if (!_merchants.TryGetValue(npcGcType, out var merchantData))
                return false;

            var now = DateTime.UtcNow;
            bool anyRegenerated = false;

            for (int inventoryIndex = 0; inventoryIndex < runtimeMerchant.inventories.Count; inventoryIndex++)
            {
                var runtimeInventory = runtimeMerchant.inventories[inventoryIndex];

                if (runtimeInventory.staticContents)
                    continue;

                string regenKey = $"{npcGcType}_{runtimeInventory.id}";

                if (!_lastRegeneration.TryGetValue(regenKey, out var lastRegen))
                {
                    lastRegen = DateTime.MinValue;
                }

                float intervalSeconds = runtimeInventory.regenerateIntervalSeconds > 0 ? runtimeInventory.regenerateIntervalSeconds : DEFAULT_REFRESH_INTERVAL_SECONDS;
                var elapsed = (now - lastRegen).TotalSeconds;

                if (elapsed >= intervalSeconds)
                {
                    Debug.LogError($"[MERCHANT-RUNTIME] regenerate npc={npcGcType} inv='{runtimeInventory.name}'");

                    var invData = merchantData.inventories.FirstOrDefault(inv => inv.id == runtimeInventory.id);

                    if (invData != null)
                    {
                        GenerateInventoryItems(runtimeMerchant, runtimeInventory, invData, runtimeInventory.generatedForLevel > 0 ? runtimeInventory.generatedForLevel : 100);
                    }

                    _lastRegeneration[regenKey] = now;
                    anyRegenerated = true;
                }
            }

            return anyRegenerated;
        }

        public static void WriteMerchantRefreshUpdate(LEWriter writer, string npcGcType, ushort merchantComponentId)
        {
            if (!_runtimeMerchants.TryGetValue(npcGcType, out var runtimeMerchant))
            {
                Debug.LogError($"[MERCHANT-RUNTIME] npc={npcGcType} reason=runtime-missing");
                return;
            }

            Debug.LogError($"[MERCHANT-RUNTIME] writeRefresh npc={npcGcType}");

            writer.WriteByte(0x35);
            writer.WriteUInt16(merchantComponentId);
            writer.WriteByte(0x1E);


            foreach (var inventory in runtimeMerchant.inventories)
            {
                if (inventory.staticContents || inventory.items.Count == 0)
                    continue;

                Debug.LogError($"[MERCHANT-RUNTIME] refreshInventory='{inventory.name}' items={inventory.items.Count}");

                writer.WriteByte((byte)inventory.id);

                writer.WriteByte((byte)inventory.items.Count);

                foreach (var item in inventory.items)
                {
                    WriteItem(writer, item);
                }
            }
        }

        public static List<string> GetDynamicMerchantTypes()
        {
            var result = new List<string>();
            foreach (var runtimeMerchantEntry in _runtimeMerchants)
            {
                bool hasDynamic = runtimeMerchantEntry.Value.inventories.Any(inv => !inv.staticContents && inv.autoGenerateItems);
                if (hasDynamic)
                {
                    result.Add(runtimeMerchantEntry.Key);
                }
            }
            return result;
        }

        public static int GetSecondsUntilRegeneration(string npcGcType, int inventoryId)
        {
            int ticks = GetTicksUntilRegeneration(npcGcType, inventoryId);
            return Math.Max(0, (int)Math.Ceiling(ticks / REFRESH_TIMER_TICKS_PER_SECOND));
        }

        public static int GetTicksUntilRegeneration(string npcGcType, int inventoryId)
        {
            string regenKey = $"{npcGcType}_{inventoryId}";

            if (!_runtimeMerchants.TryGetValue(npcGcType, out var runtimeMerchant))
                return 0;

            var inventory = runtimeMerchant.inventories.FirstOrDefault(inv => inv.id == inventoryId);
            if (inventory == null || inventory.staticContents)
                return 0;

            float intervalSeconds = inventory.regenerateIntervalSeconds > 0 ? inventory.regenerateIntervalSeconds : DEFAULT_REFRESH_INTERVAL_SECONDS;

            if (!_lastRegeneration.TryGetValue(regenKey, out var lastRegen))
            {
                return Math.Max(0, (int)Math.Round(intervalSeconds * REFRESH_TIMER_TICKS_PER_SECOND));
            }

            var elapsed = (DateTime.UtcNow - lastRegen).TotalSeconds;
            var remainingTicks = (intervalSeconds - elapsed) * REFRESH_TIMER_TICKS_PER_SECOND;

            return Math.Max(0, (int)Math.Ceiling(remainingTicks));
        }

        public static void RegenerateNow(string npcGcType, int playerLevel = 100)
        {
            if (!_runtimeMerchants.TryGetValue(npcGcType, out var runtimeMerchant))
                return;

            if (!_merchants.TryGetValue(npcGcType, out var merchantData))
                return;

            foreach (var runtimeInv in runtimeMerchant.inventories)
            {
                if (runtimeInv.staticContents)
                    continue;

                var invData = merchantData.inventories.FirstOrDefault(inv => inv.id == runtimeInv.id);
                if (invData != null)
                {
                    Debug.LogError($"[MERCHANT-RUNTIME] regenerateNow npc={npcGcType} inv='{runtimeInv.name}' playerLevel={playerLevel}");
                    GenerateInventoryItems(runtimeMerchant, runtimeInv, invData, playerLevel);

                    SetLastRegeneration(npcGcType, runtimeInv.id, DateTime.UtcNow);
                }
            }
        }

        public static bool EnsureInventoryForLevel(string npcGcType, int playerLevel)
        {
            if (!_runtimeMerchants.TryGetValue(npcGcType, out var runtimeMerchant))
                return false;

            if (!_merchants.TryGetValue(npcGcType, out var merchantData))
                return false;

            bool anyRegenerated = false;

            foreach (var runtimeInv in runtimeMerchant.inventories)
            {
                if (runtimeInv.staticContents)
                    continue;

                if (runtimeInv.generatedForLevel != playerLevel)
                {
                    var invData = merchantData.inventories.FirstOrDefault(inv => inv.id == runtimeInv.id);
                    if (invData != null)
                    {
                        Debug.LogError($"[MERCHANT-RUNTIME] regenerateForLevel inv='{runtimeInv.name}' playerLevel={playerLevel} previousLevel={runtimeInv.generatedForLevel}");
                        GenerateInventoryItems(runtimeMerchant, runtimeInv, invData, playerLevel);
                        SetLastRegeneration(npcGcType, runtimeInv.id, DateTime.UtcNow);

                        anyRegenerated = true;
                    }
                }
            }
            return anyRegenerated;
        }

        private static bool UsesGeneratedRefresh(MerchantInventoryRuntimeData inventory)
        {
            return inventory != null
                && !inventory.staticContents
                && inventory.autoGenerateItems
                && !string.IsNullOrWhiteSpace(inventory.itemGenerator);
        }

        private static void SetLastRegeneration(string npcGcType, int inventoryId, DateTime now)
        {
            _lastRegeneration[$"{npcGcType}_{inventoryId}"] = now;
        }


        public static (int width, int height) GetItemDimensions(string gcType)
        {
            string lookupType = gcType;

            if (lookupType.StartsWith("items.pal.", StringComparison.OrdinalIgnoreCase))
            {
                lookupType = lookupType.Substring(10);
            }
            else if (lookupType.StartsWith("items.consumables.", StringComparison.OrdinalIgnoreCase))
            {
                lookupType = lookupType.Substring(18);
            }

            lookupType = lookupType.ToLowerInvariant();

            if (lookupType.Contains("potion") || lookupType.Contains("scroll") ||
                lookupType.Contains("consumable") || lookupType.Contains("townportal") ||
                lookupType.Contains("questitem") ||
                gcType.IndexOf("consumables", StringComparison.OrdinalIgnoreCase) >= 0 ||
                gcType.IndexOf("questitem", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return (1, 1);
            }

            var itemData = DatabaseLoader.FindItem(lookupType);
            if (itemData != null && itemData.inventoryWidth > 0 && itemData.inventoryHeight > 0)
            {
                return (itemData.inventoryWidth, itemData.inventoryHeight);
            }

            Debug.LogError($"[MERCHANT-DIMS] item={gcType} fallback=2x2 key={lookupType} dbFound={itemData != null}");
            return (2, 2);
        }

        private static (int x, int y) FindSpot(bool[,] occupied, int gridWidth, int gridHeight, int itemWidth, int itemHeight)
        {
            for (int y = 0; y <= gridHeight - itemHeight; y++)
            {
                for (int x = 0; x <= gridWidth - itemWidth; x++)
                {
                    bool canPlace = true;
                    for (int dx = 0; dx < itemWidth && canPlace; dx++)
                    {
                        for (int dy = 0; dy < itemHeight && canPlace; dy++)
                        {
                            if (occupied[x + dx, y + dy])
                                canPlace = false;
                        }
                    }
                    if (canPlace)
                        return (x, y);
                }
            }
            return (-1, -1);
        }

        public static int GetOP5ModCount(string gcType)
        {
            if (!TryGetAuthoredMerchantModSlots(gcType, out int merchantModSlots))
                return -1;

            int op5ModCount = merchantModSlots - 1;
            return op5ModCount < 0 ? 0 : op5ModCount;
        }

        public static bool HasAuthoredMerchantModSlots(string gcType)
        {
            return TryGetAuthoredMerchantModSlots(gcType, out _);
        }

        public static bool TryGetAuthoredMerchantModSlots(string gcType, out int merchantModSlots)
        {
            merchantModSlots = 0;
            string key = NormalizeAuthoredModSlotKey(gcType);
            if (string.IsNullOrEmpty(key))
                return false;

            if (!IsSpecialAuthoredSlotCandidate(key))
                return false;

            if (DungeonRunners.Data.ItemStatDatabase.Instance.TryGetItemReadDataSlotCount(key, out int authoredSlots))
            {
                merchantModSlots = authoredSlots;
                return true;
            }

            return false;
        }

        private static string NormalizeAuthoredModSlotKey(string gcType)
        {
            if (string.IsNullOrWhiteSpace(gcType))
                return "";
            string key = gcType.Replace('\\', '.').Replace('/', '.').Trim().ToLowerInvariant();
            if (key.StartsWith("items.pal."))
                key = key.Substring("items.pal.".Length);
            return key;
        }

        private static bool IsSpecialAuthoredSlotCandidate(string key)
        {
            return key.IndexOf("mythic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   key.IndexOf("prebuilt", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   key.IndexOf("partialbuilt", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   key.IndexOf("generated", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   key.IndexOf("boss", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   key.IndexOf("seasonal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   key.IndexOf("wishingwell", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void LogAuthoredModSlotCoverage()
        {
            var keys = LoadAuthoredModSlotAuditKeys();
            int authored = 0;
            var misses = new List<string>();

            foreach (string key in keys)
            {
                if (DungeonRunners.Data.ItemStatDatabase.Instance.TryGetItemReadDataSlotCount(key, out _))
                {
                    authored++;
                }
                else if (misses.Count < 8)
                {
                    misses.Add(key);
                }
            }

            string samples = misses.Count == 0 ? "" : $" samples=[{string.Join(", ", misses)}]";
            Debug.LogError($"[MERCHANT-RUNTIME] authoredModSlots source=GC-DATABASE+PACKAGE-CATALOG special={authored}/{keys.Count} missing={keys.Count - authored}{samples}");
        }

        private static SortedSet<string> LoadAuthoredModSlotAuditKeys()
        {
            var keys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var connection = DungeonRunners.Database.GameDatabase.GetConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT gc_type FROM sellable_items UNION SELECT gc_type FROM items";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string key = NormalizeAuthoredModSlotKey(reader.GetString(0));
                    if (IsSpecialAuthoredSlotCandidate(key))
                        keys.Add(key);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MERCHANT-RUNTIME] authoredModSlots auditDb={ex.Message}");
            }
            return keys;
        }


        private static readonly bool DISABLE_MYTHICPAL = false;
        private static readonly bool ENABLE_1H_WEAPON_PREBUILT = true;
        private static readonly bool ENABLE_FIGHTER_CLASS_ARMOR = true;
        private static readonly bool ENABLE_MAGE_CLASS_ARMOR = true;
        private static readonly bool ENABLE_RANGER_CLASS_ARMOR = true;

        private static bool IsEnabledMythicItem(string gcType)
        {
            if (string.IsNullOrEmpty(gcType)) return false;
            string lower = gcType.ToLowerInvariant();
            if (lower.StartsWith("items.pal.")) lower = lower.Substring(10);
            if (lower.Contains("mythicpal")) return !DISABLE_MYTHICPAL;
            if (!HasAuthoredMerchantModSlots(lower)) return false;
            string pal = lower.Split('.')[0];
            if (ENABLE_1H_WEAPON_PREBUILT && (pal == "1haxepal" || pal == "1hmacepal" || pal == "1hpickpal" || pal == "1hstaffpal" || pal == "1hswordpal"))
                return true;
            if (ENABLE_FIGHTER_CLASS_ARMOR && pal.StartsWith("fighter"))
                return true;
            if (ENABLE_MAGE_CLASS_ARMOR && pal.StartsWith("mage"))
                return true;
            if (ENABLE_RANGER_CLASS_ARMOR && pal.StartsWith("ranger"))
                return true;
            return false;
        }

        private static bool IsWeaponType(string gcType)
        {
            string lower = gcType.ToLowerInvariant();
            return lower.Contains("axe") || lower.Contains("sword") ||
                   lower.Contains("mace") || lower.Contains("pick") ||
                   lower.Contains("staff") || lower.Contains("crossbow") ||
                   lower.Contains("gun") || lower.Contains("cannon");
        }

        private static bool IsArmorType(string gcType)
        {
            string lower = gcType.ToLowerInvariant();
            return lower.Contains("armor") || lower.Contains("boot") ||
                   lower.Contains("glove") || lower.Contains("helm") ||
                   lower.Contains("shoulder") || lower.Contains("pauldron") ||
                   lower.Contains("shield") || lower.Contains("buckler") ||
                   lower.Contains("body") || lower.Contains("chest");
        }

        private static bool IsJewelryType(string gcType)
        {
            string lower = gcType.ToLowerInvariant();
            return lower.Contains("ring") || lower.Contains("amulet");
        }

        private static int RollMerchantStockLevel(MerchantInventoryRuntimeData runtimeInv, System.Random random)
        {
            int minLevel = runtimeInv.minItemLevel > 0 ? runtimeInv.minItemLevel : 1;
            int maxLevel = runtimeInv.maxItemLevel > 0 ? runtimeInv.maxItemLevel : minLevel;
            if (maxLevel < minLevel)
                maxLevel = minLevel;
            if (maxLevel == minLevel)
                return minLevel;
            return random.Next(minLevel, maxLevel + 1);
        }

        private static void GenerateInventoryItems(MerchantRuntimeData merchant, MerchantInventoryRuntimeData runtimeInv, MerchantInventoryData invData, int playerLevel = 100)
        {
            runtimeInv.items.Clear();
            runtimeInv.generatedForLevel = playerLevel;

            var safeItems = _sellableItems.Where(sellableItem =>
                (System.Text.RegularExpressions.Regex.IsMatch(sellableItem.gcType, @"-\d+$") || IsEnabledMythicItem(sellableItem.gcType)) &&
                sellableItem.gcType.IndexOf("PreBuilt", StringComparison.OrdinalIgnoreCase) < 0 &&
                sellableItem.gcType.IndexOf("PartialBuilt", StringComparison.OrdinalIgnoreCase) < 0 &&
                sellableItem.gcType.IndexOf("Seasonal", StringComparison.OrdinalIgnoreCase) < 0 &&
                sellableItem.gcType.IndexOf("WishingWell", StringComparison.OrdinalIgnoreCase) < 0 &&
                sellableItem.gcType.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) < 0 &&
                sellableItem.gcType.IndexOf("Generated", StringComparison.OrdinalIgnoreCase) < 0 &&
                !sellableItem.gcType.EndsWith(".Visual", StringComparison.OrdinalIgnoreCase) &&
                !sellableItem.gcType.Contains("test", StringComparison.OrdinalIgnoreCase) &&
                (IsWeaponType(sellableItem.gcType) || IsArmorType(sellableItem.gcType))
            );

            int authoredMinItemLevel = runtimeInv.minItemLevel > 0 ? runtimeInv.minItemLevel : 1;
            int authoredMaxItemLevel = runtimeInv.maxItemLevel > 0 ? runtimeInv.maxItemLevel : Math.Max(authoredMinItemLevel, playerLevel);
            if (authoredMaxItemLevel < authoredMinItemLevel)
                authoredMaxItemLevel = authoredMinItemLevel;
            safeItems = safeItems.Where(sellableItem =>
            {
                int itemLevel = RPGSettings.GetItemLevel(sellableItem.gcType);
                return itemLevel <= authoredMaxItemLevel;
            });

            string itemGeneratorType = runtimeInv.itemGenerator ?? invData.itemGenerator ?? "";
            if (VerboseMerchantItemLogging)
                Debug.LogError($"[MERCHANT-RUNTIME] itemGeneratorFilter inv='{runtimeInv.name}' itemGenerator='{itemGeneratorType}'");
            System.Func<string, bool> isMythicItem = (gcType) => IsEnabledMythicItem(gcType);
            var random = CreateMerchantRandom();

            if (itemGeneratorType.Equals("MerchantWeaponIG", StringComparison.OrdinalIgnoreCase))
            {
                safeItems = safeItems.Where(sellableItem =>
                {
                    if (isMythicItem(sellableItem.gcType)) return false;
                    if (!IsWeaponType(sellableItem.gcType)) return false;
                    int suffix = RPGSettings.GetTierFromGcType(sellableItem.gcType);
                    if (suffix == 4) return true;
                    if (suffix == 5) return random.Next(20) == 0;
                    return false;
                });
            }
            else if (itemGeneratorType.Equals("MerchantArmorIG", StringComparison.OrdinalIgnoreCase))
            {
                safeItems = safeItems.Where(sellableItem =>
                {
                    if (isMythicItem(sellableItem.gcType)) return false;
                    if (!IsArmorType(sellableItem.gcType)) return false;
                    int suffix = RPGSettings.GetTierFromGcType(sellableItem.gcType);
                    if (suffix == 4) return true;
                    if (suffix == 5) return random.Next(20) == 0;
                    return false;
                });
            }
            else if (itemGeneratorType.Equals("MerchantTrashIG", StringComparison.OrdinalIgnoreCase))
            {
                safeItems = safeItems.Where(sellableItem =>
                {
                    if (isMythicItem(sellableItem.gcType)) return false;
                    int suffix = RPGSettings.GetTierFromGcType(sellableItem.gcType);
                    return suffix >= 2 && suffix <= 3;
                });
            }
            else if (itemGeneratorType.Equals("MerchantSuperiorIG", StringComparison.OrdinalIgnoreCase))
            {
                safeItems = safeItems.Where(sellableItem =>
                {
                    if (isMythicItem(sellableItem.gcType)) return false;
                    int suffix = RPGSettings.GetTierFromGcType(sellableItem.gcType);
                    return suffix == 2;
                });
            }
            else if (itemGeneratorType.Equals("MerchantSpecialEvent01IG", StringComparison.OrdinalIgnoreCase))
            {
                safeItems = safeItems.Where(sellableItem =>
                {
                    if (isMythicItem(sellableItem.gcType))
                    {
                        string mythicKey = sellableItem.gcType.ToLowerInvariant();
                        if (mythicKey.StartsWith("items.pal.")) mythicKey = mythicKey.Substring(10);
                        if (!TryGetAuthoredMerchantModSlots(mythicKey, out int mythicSlots)) return false;
                        if (mythicSlots < 3 && DungeonRunners.Data.ItemStatDatabase.Instance.GetItemWireMods(mythicKey).Count == 0)
                            return false;
                        return random.Next(10) == 0;
                    }
                    int suffix = RPGSettings.GetTierFromGcType(sellableItem.gcType);
                    if (suffix == 4) return true;
                    if (suffix == 5) return random.Next(4) == 0;
                    return false;
                });
            }

            safeItems = safeItems.Where(sellableItem =>
            {
                if (!IsEnabledMythicItem(sellableItem.gcType)) return true;
                if (sellableItem.gcType.IndexOf("MythicPAL", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                string key = sellableItem.gcType.ToLowerInvariant();
                if (key.StartsWith("items.pal.")) key = key.Substring(10);
                if (!TryGetAuthoredMerchantModSlots(key, out int slots)) return false;
                if (slots <= 0) return false;
                return true;
            });

            var safeList = safeItems.ToList();
            if (VerboseMerchantItemLogging)
                Debug.LogError($"[MERCHANT-DETAIL] candidates inv='{runtimeInv.name}' itemGenerator={itemGeneratorType} count={safeList.Count} maxLvl={authoredMaxItemLevel} playerLvl={playerLevel}");
            if (VerboseMerchantItemLogging)
                Debug.LogError($"[MERCHANT-DETAIL] filtered inv='{runtimeInv.name}' itemGenerator={itemGeneratorType} count={safeList.Count}");
            foreach (var dbgItem in safeList)
            {
                if (IsEnabledMythicItem(dbgItem.gcType))
                {
                    string mythicKey = dbgItem.gcType.ToLowerInvariant();
                    if (mythicKey.StartsWith("items.pal.")) mythicKey = mythicKey.Substring(10);
                    int authoredSlots = TryGetAuthoredMerchantModSlots(mythicKey, out int modSlots) ? modSlots : -1;
                    bool isWeapon = IsWeaponType(dbgItem.gcType);
                    bool isArmor = IsArmorType(dbgItem.gcType);
                    if (VerboseMerchantItemLogging)
                        Debug.LogError($"[MERCHANT-MYTHIC] tab='{runtimeInv.name}' itemGenerator='{itemGeneratorType}' item={dbgItem.gcType} weapon={isWeapon} armor={isArmor} modSlots={authoredSlots}");
                }
            }
            if (safeList.Count == 0) return;
            var available = safeList.OrderBy(x => random.Next()).ToList();
            bool[,] grid = new bool[invData.width, invData.height];
            foreach (var itemData in available)
            {
                var (w, h) = GetItemDimensions(itemData.gcType);
                int placeX = -1, placeY = -1;
                for (int x = 0; x <= invData.width - w && placeX < 0; x++)
                {
                    for (int y = 0; y <= invData.height - h && placeX < 0; y++)
                    {
                        bool canPlace = true;
                        for (int dx = 0; dx < w && canPlace; dx++)
                            for (int dy = 0; dy < h && canPlace; dy++)
                                if (grid[x + dx, y + dy]) canPlace = false;
                        if (canPlace) { placeX = x; placeY = y; }
                    }
                }
                if (placeX >= 0)
                {
                    for (int dx = 0; dx < w; dx++)
                        for (int dy = 0; dy < h; dy++)
                            grid[placeX + dx, placeY + dy] = true;

                    int tier = RPGSettings.GetTierFromGcType(itemData.gcType);
                    var itemRarity = RPGSettings.GetRarityFromTier(tier);

                    if (RPGSettings.IsMythicPALItem(itemData.gcType) || IsEnabledMythicItem(itemData.gcType))
                    {
                        Debug.LogError($"[MERCHANT-MYTHIC] item={itemData.gcType} rarity={itemRarity} resolvedRarity=Mythic");
                        itemRarity = ItemRarity.Mythic;
                    }

                    bool isMythicGen = IsEnabledMythicItem(itemData.gcType);
                    int level;
                    if (isMythicGen)
                    {
                        int rolledLevel = RollMerchantStockLevel(runtimeInv, random);
                        int mythicMin = DungeonRunners.Core.ServerSettings.Get("mythicMinLevel", 15);
                        int mythicMax = DungeonRunners.Core.ServerSettings.Get("mythicMaxLevel", 100);
                        level = Math.Max(mythicMin, Math.Min(mythicMax, rolledLevel));
                    }
                    else
                    {
                        level = RollMerchantStockLevel(runtimeInv, random);
                    }

                    float baseGoldValue = itemData.gcGoldValue > 0 ? itemData.gcGoldValue : RPGSettings.GetBaseGoldValue(itemData.gcType);
                    uint finalPrice = RPGSettings.CalculateBuyPrice(level, itemRarity, baseGoldValue);

                    runtimeInv.items.Add(new MerchantItemRuntimeData
                    {
                        gcType = itemData.gcType,
                        id = merchant.nextItemId++,
                        inventoryX = placeX,
                        inventoryY = placeY,
                        quantity = 1,
                        level = level,
                        rarity = itemRarity,
                        price = finalPrice,
                        goldValue = baseGoldValue,
                        scaleMod = RPGSettings.GetRandomScaleMod(itemRarity)
                    });
                    if (isMythicGen)
                    {
                        string palItemKey = itemData.gcType.ToLowerInvariant();
                        if (palItemKey.StartsWith("items.pal.")) palItemKey = palItemKey.Substring(10);
                        int palModSlots = TryGetAuthoredMerchantModSlots(palItemKey, out int authoredModSlots) ? authoredModSlots : -1;
                        if (VerboseMerchantItemLogging)
                            Debug.LogError($"[MERCHANT-MYTHIC] placed tab='{runtimeInv.name}' item={itemData.gcType} modSlots={palModSlots} pos=({placeX},{placeY})");
                    }
                }
            }
            Debug.LogError($"[MERCHANT-RUNTIME] generated inv='{runtimeInv.name}' items={runtimeInv.items.Count}");
        }

        private static System.Random CreateMerchantRandom()
        {
            lock (_merchantRandomLock)
                return new System.Random(_merchantRandom.Next());
        }






        public static bool IsMerchant(string npcGcType)
        {
            if (!_initialized) Initialize();
            return _merchants.ContainsKey(npcGcType);
        }

        public static MerchantData GetMerchant(string npcGcType)
        {
            if (!_initialized) Initialize();
            _merchants.TryGetValue(npcGcType, out var merchant);
            return merchant;
        }

        public static MerchantRuntimeData GetRuntimeMerchant(string npcGcType)
        {
            if (!_initialized) Initialize();
            _runtimeMerchants.TryGetValue(npcGcType, out var merchant);
            return merchant;
        }



        public static void WriteMerchantComponent(LEWriter writer, string npcGcType, ushort npcId, ushort merchantId)
        {
            if (!_initialized) Initialize();


            var runtimeMerchant = GetRuntimeMerchant(npcGcType);
            if (runtimeMerchant == null)
            {
                Debug.LogError($"[MERCHANT-RUNTIME] reason=runtime-missing npc={npcGcType}");
                return;
            }

            if (VerboseMerchantItemLogging)
            {
                Debug.LogError($"[MERCHANT-RUNTIME] writeComponent npc={npcGcType}");
            }

            int merchantCompStart = writer.Position;
            writer.WriteByte(0x32);
            writer.WriteUInt16(npcId);
            writer.WriteUInt16(merchantId);

            writer.WriteByte(0xFF);
            writer.WriteCString("merchant");

            writer.WriteByte(0x01);

            WriteMerchantInitPayload(writer, npcGcType, runtimeMerchant);

            if (VerboseMerchantItemLogging)
                Debug.LogError($"[MERCHANT-RUNTIME] componentBytes={writer.Position - merchantCompStart} start={merchantCompStart} end={writer.Position}");
        }

        private static void WriteMerchantInitPayload(LEWriter writer, string npcGcType, MerchantRuntimeData runtimeMerchant, bool includeDynamicItems = true)
        {
            writer.WriteUInt32(0x000000FF);
            writer.WriteUInt32(0x00000000);

            int invCount = runtimeMerchant.inventories.Count;
            writer.WriteByte((byte)invCount);
            if (VerboseMerchantItemLogging)
                Debug.LogError($"[MERCHANT-RUNTIME] writeInventories count={invCount}");

            foreach (var inventory in runtimeMerchant.inventories.OrderBy(inv => inv.id))
            {
                int invStartPos = writer.Position;
                WriteInventory(writer, inventory, npcGcType, includeDynamicItems);
                int invEndPos = writer.Position;
                if (VerboseMerchantItemLogging)
                    Debug.LogError($"[MERCHANT-RUNTIME] writeInventory inv='{inventory.name}' id={inventory.id} gcType='{inventory.gcType}' bytes={invEndPos - invStartPos} start={invStartPos} end={invEndPos}");
            }

            int resetTimeTicks = 0;
            foreach (var inv in runtimeMerchant.inventories)
            {
                if (!inv.staticContents)
                {
                    resetTimeTicks = GetTicksUntilRegeneration(npcGcType, inv.id);
                    if (VerboseMerchantItemLogging)
                        Debug.LogError($"[MERCHANT-RUNTIME] timer seconds={resetTimeTicks / REFRESH_TIMER_TICKS_PER_SECOND:F2}");
                    break;
                }
            }

            if (resetTimeTicks > 0xFFFF) resetTimeTicks = 0xFFFF;

            writer.WriteByte(0x01);
            writer.WriteUInt16((ushort)resetTimeTicks);
            writer.WriteUInt16(0x000F);

            if (VerboseMerchantItemLogging)
                Debug.LogError($"[MERCHANT-RUNTIME] resetTime seconds={resetTimeTicks / REFRESH_TIMER_TICKS_PER_SECOND:F2} ticks={resetTimeTicks} minutes={resetTimeTicks / REFRESH_TIMER_TICKS_PER_SECOND / 60.0:F1}");
        }

        private static void WriteInventory(LEWriter writer, MerchantInventoryRuntimeData inventory, string npcGcType, bool includeDynamicItems = true)
        {
            int invStart = writer.Position;
            writer.WriteByte(0xFF);
            writer.WriteCString(inventory.gcType.ToLowerInvariant());

            writer.WriteByte((byte)inventory.id);
            if (VerboseMerchantItemLogging)
                Debug.LogError($"[MERCHANT-WRITE-INVENTORY] start inv='{inventory.name}' gcType='{inventory.gcType}' id={inventory.id} static={inventory.staticContents} serverSends={inventory.serverSendsItems} items={inventory.items.Count} pos={invStart}");

            if (inventory.serverSendsItems)
            {
                writer.WriteByte(0x01);

                foreach (var item in inventory.items)
                {
                    writer.WriteUInt32((uint)item.id);
                    writer.WriteByte((byte)item.inventoryX);
                    writer.WriteByte((byte)item.inventoryY);
                    writer.WriteByte((byte)item.quantity);
                    writer.WriteByte(0x01);
                    writer.WriteByte(0x00);
                    writer.WriteByte(0x00);
                    if (VerboseMerchantItemLogging)
                        Debug.LogError($"[MERCHANT-RUNTIME] serverItem item={item.gcType} id={item.id} pos=({item.inventoryX},{item.inventoryY})");
                }

                writer.WriteByte(0x00);

                if (VerboseMerchantItemLogging)
                    Debug.LogError($"[MERCHANT-RUNTIME] inventory inv='{inventory.name}' id={inventory.id} mode=server items={inventory.items.Count}");
            }
            else if (inventory.staticContents || inventory.items.Count == 0 || (!includeDynamicItems && inventory.autoGenerateItems))
            {
                writer.WriteByte(0x00);
                if (VerboseMerchantItemLogging)
                    Debug.LogError($"[MERCHANT-RUNTIME] inventory inv='{inventory.name}' id={inventory.id} mode=client items={inventory.items.Count}");
            }
            else
            {
                writer.WriteByte(0x01);
                int itemCount = Math.Min(inventory.items.Count, 255);
                writer.WriteByte((byte)itemCount);
                if (VerboseMerchantItemLogging)
                    Debug.LogError($"[MERCHANT-RUNTIME] writeInventory inv='{inventory.name}' items={itemCount}");

                if (VerboseMerchantItemLogging)
                {
                    for (int v = 0; v < itemCount; v++)
                    {
                        var vi = inventory.items[v];
                        string vk = vi.gcType.ToLowerInvariant();
                        if (vk.StartsWith("items.pal.")) vk = vk.Substring(10);
                        int vSlots;
                        if (TryGetAuthoredMerchantModSlots(vk, out int vs)) vSlots = vs;
                        else
                        {
                            var vd = DatabaseLoader.FindItem(vk);
                            if (vd != null) vSlots = vd.modCount;
                            else vSlots = IsWeaponType(vk) ? 1 : 2;
                        }
                        if (vk.StartsWith("chain") && !vk.Contains("shield") && !vk.Contains("mythic"))
                            vSlots = 1;
                        Debug.LogError($"[MERCHANT-PRE-WRITE] index={v} item={vi.gcType} modSlots={vSlots} id={vi.id}");
                    }
                    Debug.LogError("[MERCHANT-PRE-WRITE] end");
                }

                for (int itemIndex = 0; itemIndex < itemCount; itemIndex++)
                {
                    WriteItem(writer, inventory.items[itemIndex]);
                }

                int remainingSeconds = GetSecondsUntilRegeneration(npcGcType, inventory.id);
                if (VerboseMerchantItemLogging)
                    Debug.LogError($"[MERCHANT-RUNTIME] inventory inv='{inventory.name}' items={itemCount} resetsIn={remainingSeconds}");
            }
        }
        public static void ProcessRefreshes(
            Dictionary<uint, List<GameServer.ZoneNPC>> zoneNPCs,
            Dictionary<int, RRConnection> connections,
            Action<RRConnection, byte, byte, byte[]> sendPacket,
            ref uint nextEntityId)
        {
            FlushPendingMerchantRefreshAdds(connections, sendPacket);
            var activeZoneIds = new HashSet<uint>(connections.Values.Where(conn => conn.IsConnected).Select(conn => conn.CurrentZoneId));

            foreach (var zoneNpcEntry in zoneNPCs)
            {
                uint zoneId = zoneNpcEntry.Key;
                if (!activeZoneIds.Contains(zoneId))
                    continue;

                foreach (var npc in zoneNpcEntry.Value)
                {
                    if (!npc.IsMerchant) continue;

                    if (!_runtimeMerchants.TryGetValue(npc.GCClass, out var runtimeMerchant))
                        continue;
                    if (!_merchants.TryGetValue(npc.GCClass, out var merchantData))
                        continue;

                    if (!TryRegenerateInventories(npc.GCClass, runtimeMerchant, merchantData, DateTime.UtcNow, out int refreshedViews, out int refreshedItems, out var removedItemIds))
                        continue;

                    Debug.LogError($"[MERCHANT-RUNTIME] regenerated npc={npc.GCClass} views={refreshedViews} items={refreshedItems}");
                    QueueMerchantRefreshAdd(zoneId, npc.GCClass, (ushort)npc.MerchantId, DateTime.UtcNow, removedItemIds);
                }
            }
        }

        private static void QueueMerchantRefreshAdd(uint zoneId, string npcGcClass, ushort componentId, DateTime now, List<uint> removedItemIds)
        {
            _pendingMerchantRefreshAdds.RemoveAll(p => p.zoneId == zoneId && p.componentId == componentId);
            _pendingMerchantRefreshAdds.Add(new PendingMerchantRefreshAdd
            {
                zoneId = zoneId,
                npcGcClass = npcGcClass,
                componentId = componentId,
                removedItemIds = removedItemIds != null ? removedItemIds.Distinct().ToList() : new List<uint>(),
                sendAfterUtc = now.AddSeconds(REFRESH_ADD_DELAY_SECONDS)
            });
            Debug.LogError($"[MERCHANT-RUNTIME] queuedRefresh npc={npcGcClass} delay={REFRESH_ADD_DELAY_SECONDS:0.###}");
        }

        private static void FlushPendingMerchantRefreshAdds(
            Dictionary<int, RRConnection> connections,
            Action<RRConnection, byte, byte, byte[]> sendPacket)
        {
            var now = DateTime.UtcNow;
            for (int pendingIndex = _pendingMerchantRefreshAdds.Count - 1; pendingIndex >= 0; pendingIndex--)
            {
                var pending = _pendingMerchantRefreshAdds[pendingIndex];
                if (pending.sendAfterUtc > now)
                    continue;

                _pendingMerchantRefreshAdds.RemoveAt(pendingIndex);

                if (!_runtimeMerchants.TryGetValue(pending.npcGcClass, out var runtimeMerchant))
                    continue;

                byte[] data = BuildMerchantInventoryRefreshPacket(pending.componentId, runtimeMerchant, pending.removedItemIds);
                if (data.Length <= 2)
                    continue;

                int sent = 0;
                foreach (var conn in connections.Values)
                {
                    if (conn.CurrentZoneId != pending.zoneId || !conn.IsConnected || !conn.IsSpawned || !conn.AllowFlush)
                        continue;

                    bool hasActiveRefresh = conn.HasActiveMerchantRefresh
                        && string.Equals(conn.ActiveMerchantNpcGcClass, pending.npcGcClass, StringComparison.OrdinalIgnoreCase);
                    sendPacket(conn, 0x01, 0x0F, data);
                    if (hasActiveRefresh)
                        ScheduleClientMerchantRefresh(conn, pending.npcGcClass, pending.componentId, now, true);
                    sent++;
                }

                Debug.LogError($"[MERCHANT-RUNTIME] sentRefresh npc={pending.npcGcClass} bytes={data.Length} clients={sent}");
                if (VerboseMerchantItemLogging)
                {
                    int totalInv = runtimeMerchant.inventories.Count(inv => !inv.staticContents);
                    int totalItems = runtimeMerchant.inventories.Where(inv => !inv.staticContents).Sum(inv => inv.items.Count);
                    Debug.LogError($"[MERCHANT-DETAIL] flushA npc={pending.npcGcClass} compId=0x{pending.componentId:X4} dataLen={data.Length} removed={pending.removedItemIds.Count} dynInv={totalInv} dynItems={totalItems} sentTo={sent}");
                }
            }
        }

        public static void ScheduleClientMerchantRefresh(RRConnection conn, string npcGcType, ushort componentId, DateTime now, bool forceReschedule = false)
        {
            if (conn == null || string.IsNullOrWhiteSpace(npcGcType) || componentId == 0)
                return;

            bool sameActiveRefresh = conn.HasActiveMerchantRefresh
                && conn.ActiveMerchantComponentId == componentId
                && string.Equals(conn.ActiveMerchantNpcGcClass, npcGcType, StringComparison.OrdinalIgnoreCase);

            if (!forceReschedule && sameActiveRefresh && (conn.ActiveMerchantRefreshReady || conn.ActiveMerchantRefreshDueUtc <= now))
            {
                conn.ActiveMerchantRefreshReady = true;
                Debug.LogError($"[MERCHANT-RUNTIME] preserveRefresh npc={npcGcType} reason=client-boundary");
                return;
            }

            double delaySeconds = GetGeneratedRefreshDelaySeconds(npcGcType);
            DateTime dueUtc = now.AddSeconds(delaySeconds);
            if (!forceReschedule && sameActiveRefresh && conn.ActiveMerchantRefreshDueUtc > DateTime.MinValue && conn.ActiveMerchantRefreshDueUtc < dueUtc)
            {
                dueUtc = conn.ActiveMerchantRefreshDueUtc;
                delaySeconds = Math.Max(0.0, (dueUtc - now).TotalSeconds);
            }

            conn.ActiveMerchantNpcGcClass = npcGcType;
            conn.ActiveMerchantComponentId = componentId;
            conn.ActiveMerchantRefreshDueUtc = dueUtc;
            conn.ActiveMerchantRefreshReady = false;
            conn.HasActiveMerchantRefresh = true;

            Debug.LogError($"[MERCHANT-RUNTIME] scheduleRefresh npc={npcGcType} delay={delaySeconds:0.###}");
        }

        private static void FlushClientMerchantRefreshes(
            Dictionary<int, RRConnection> connections,
            Action<RRConnection, byte, byte, byte[]> sendPacket)
        {
            var now = DateTime.UtcNow;

            foreach (var conn in connections.Values)
            {
                if (!conn.IsConnected || !conn.IsSpawned || !conn.AllowFlush || !conn.HasActiveMerchantRefresh || conn.ActiveMerchantRefreshDueUtc > now)
                    continue;

                if (string.IsNullOrWhiteSpace(conn.ActiveMerchantNpcGcClass) || conn.ActiveMerchantComponentId == 0)
                {
                    conn.HasActiveMerchantRefresh = false;
                    continue;
                }

                FlushClientMerchantRefresh(conn, sendPacket);
            }
        }

        public static bool FlushClientMerchantRefreshOnClientBoundary(
            RRConnection conn,
            Action<RRConnection, byte, byte, byte[]> sendPacket)
        {
            if (conn == null || !conn.IsConnected || !conn.HasActiveMerchantRefresh)
                return false;

            var nowBoundary = DateTime.UtcNow;
            if (VerboseMerchantItemLogging)
            {
                double overdueSec = (nowBoundary - conn.ActiveMerchantRefreshDueUtc).TotalSeconds;
                Debug.LogError($"[MERCHANT-DETAIL] boundary conn={conn.ConnId} npc={conn.ActiveMerchantNpcGcClass} dueUtcOverdue={overdueSec:F2}s ready={conn.ActiveMerchantRefreshReady}");
            }
            if (conn.ActiveMerchantRefreshDueUtc <= nowBoundary)
            {
                conn.ActiveMerchantRefreshReady = true;
                return FlushClientMerchantRefresh(conn, sendPacket);
            }

            return false;
        }

        private static bool FlushClientMerchantRefresh(
            RRConnection conn,
            Action<RRConnection, byte, byte, byte[]> sendPacket)
        {
            if (conn == null || !conn.IsConnected || !conn.HasActiveMerchantRefresh)
                return false;

            string npcGcType = conn.ActiveMerchantNpcGcClass;
            ushort componentId = conn.ActiveMerchantComponentId;
            if (string.IsNullOrWhiteSpace(npcGcType) || componentId == 0)
            {
                conn.HasActiveMerchantRefresh = false;
                conn.ActiveMerchantRefreshReady = false;
                return false;
            }

            var now = DateTime.UtcNow;
            if (!_runtimeMerchants.TryGetValue(npcGcType, out var runtimeMerchantBefore))
            {
                conn.HasActiveMerchantRefresh = false;
                conn.ActiveMerchantRefreshReady = false;
                return false;
            }

            var removedItemIds = CaptureDynamicInventoryItemIds(runtimeMerchantBefore);
            int preItemsB = runtimeMerchantBefore.inventories.Where(inv => !inv.staticContents).Sum(inv => inv.items.Count);
            RegenerateNow(npcGcType, conn.PlayerLevel > 0 ? conn.PlayerLevel : 1);

            if (!_runtimeMerchants.TryGetValue(npcGcType, out var runtimeMerchant))
            {
                conn.HasActiveMerchantRefresh = false;
                conn.ActiveMerchantRefreshReady = false;
                if (VerboseMerchantItemLogging)
                    Debug.LogError($"[MERCHANT-DETAIL] flushB-abort npc={npcGcType} reason=runtimeMissingPostRegen");
                return false;
            }

            int postItemsB = runtimeMerchant.inventories.Where(inv => !inv.staticContents).Sum(inv => inv.items.Count);
            byte[] data = BuildMerchantInventoryRefreshPacket(componentId, runtimeMerchant, removedItemIds);
            if (data.Length > 2)
            {
                sendPacket(conn, 0x01, 0x0F, data);
                Debug.LogError($"[MERCHANT-RUNTIME] sentClientRefresh npc={npcGcType} bytes={data.Length} conn={conn.LoginName ?? conn.ConnId.ToString()}");
            }
            if (VerboseMerchantItemLogging)
                Debug.LogError($"[MERCHANT-DETAIL] flushB conn={conn.ConnId} npc={npcGcType} dataLen={data.Length} removed={removedItemIds.Count} itemsBefore={preItemsB} itemsAfter={postItemsB} playerLvl={conn.PlayerLevel}");

            ScheduleClientMerchantRefresh(conn, npcGcType, componentId, now, true);
            return true;
        }

        private static bool IsClientMerchantRefreshActive(RRConnection conn, string npcGcType)
        {
            if (conn == null || string.IsNullOrWhiteSpace(npcGcType))
                return false;

            if (!string.IsNullOrWhiteSpace(conn.PendingMerchantNpcGcClass)
                && string.Equals(conn.PendingMerchantNpcGcClass, npcGcType, StringComparison.OrdinalIgnoreCase))
                return true;

            return conn.HasActiveMerchantRefresh
                && string.Equals(conn.ActiveMerchantNpcGcClass, npcGcType, StringComparison.OrdinalIgnoreCase);
        }

        private static double GetGeneratedRefreshIntervalSeconds(string npcGcType)
        {
            if (!_runtimeMerchants.TryGetValue(npcGcType, out var runtimeMerchant))
                return DEFAULT_REFRESH_INTERVAL_SECONDS;

            foreach (var runtimeInv in runtimeMerchant.inventories)
            {
                if (!UsesGeneratedRefresh(runtimeInv))
                    continue;

                return runtimeInv.regenerateIntervalSeconds > 0
                    ? runtimeInv.regenerateIntervalSeconds
                    : DEFAULT_REFRESH_INTERVAL_SECONDS;
            }

            return DEFAULT_REFRESH_INTERVAL_SECONDS;
        }

        private static double GetGeneratedRefreshDelaySeconds(string npcGcType)
        {
            if (!_runtimeMerchants.TryGetValue(npcGcType, out var runtimeMerchant))
                return DEFAULT_REFRESH_INTERVAL_SECONDS + REFRESH_ADD_DELAY_SECONDS;

            foreach (var runtimeInv in runtimeMerchant.inventories)
            {
                if (!UsesGeneratedRefresh(runtimeInv))
                    continue;

                int ticks = GetTicksUntilRegeneration(npcGcType, runtimeInv.id);
                return (ticks / REFRESH_TIMER_TICKS_PER_SECOND) + REFRESH_ADD_DELAY_SECONDS;
            }

            return DEFAULT_REFRESH_INTERVAL_SECONDS + REFRESH_ADD_DELAY_SECONDS;
        }

        private static bool TryRegenerateInventories(string npcGcClass, MerchantRuntimeData runtimeMerchant, MerchantData merchantData, DateTime now, out int refreshedViews, out int refreshedItems, out List<uint> removedItemIds)
        {
            refreshedViews = 0;
            refreshedItems = 0;
            removedItemIds = new List<uint>();

            foreach (var runtimeInv in runtimeMerchant.inventories)
            {
                if (runtimeInv.staticContents || !runtimeInv.autoGenerateItems || string.IsNullOrWhiteSpace(runtimeInv.itemGenerator))
                    continue;

                string regenKey = $"{npcGcClass}_{runtimeInv.id}";
                if (!_lastRegeneration.TryGetValue(regenKey, out var lastRegen))
                    lastRegen = DateTime.MinValue;

                float intervalSeconds = runtimeInv.regenerateIntervalSeconds > 0 ? runtimeInv.regenerateIntervalSeconds : DEFAULT_REFRESH_INTERVAL_SECONDS;
                var elapsed = (now - lastRegen).TotalSeconds;
                if (elapsed < intervalSeconds)
                    continue;

                var invData = merchantData.inventories.FirstOrDefault(inv => inv.id == runtimeInv.id);
                if (invData == null)
                    continue;

                int preItemCount = runtimeInv.items.Count;
                Debug.LogError($"[MERCHANT-RUNTIME] refreshInventory npc={npcGcClass} inv='{runtimeInv.name}'");
                foreach (var item in runtimeInv.items)
                {
                    if (item.id >= 0)
                        removedItemIds.Add((uint)item.id);
                }
                GenerateInventoryItems(runtimeMerchant, runtimeInv, invData, runtimeInv.generatedForLevel > 0 ? runtimeInv.generatedForLevel : 100);
                _lastRegeneration[regenKey] = now;
                if (VerboseMerchantItemLogging)
                    Debug.LogError($"[MERCHANT-DETAIL] regenA npc={npcGcClass} inv={runtimeInv.id} '{runtimeInv.name}' itemGenerator={runtimeInv.itemGenerator} lvl={runtimeInv.generatedForLevel} elapsed={elapsed:F1}s itemsBefore={preItemCount} itemsAfter={runtimeInv.items.Count}");
                refreshedViews++;
                refreshedItems += runtimeInv.items.Count;
            }

            return refreshedViews > 0;
        }

        private static List<uint> CaptureDynamicInventoryItemIds(MerchantRuntimeData runtimeMerchant)
        {
            var itemIds = new List<uint>();
            if (runtimeMerchant == null)
                return itemIds;

            foreach (var runtimeInv in runtimeMerchant.inventories)
            {
                if (!UsesGeneratedRefresh(runtimeInv))
                    continue;

                foreach (var item in runtimeInv.items)
                {
                    if (item.id >= 0)
                        itemIds.Add((uint)item.id);
                }
            }

            return itemIds.Distinct().ToList();
        }

        private static byte[] BuildMerchantInventoryAddPacket(ushort componentId, MerchantRuntimeData runtimeMerchant)
        {
            var writer = new LEWriter();
            writer.WriteByte(0x07);
            WriteDynamicInventoryAddUpdates(writer, componentId, runtimeMerchant);
            writer.WriteByte(0x06);
            return writer.ToArray();
        }

        private static byte[] BuildMerchantInventoryRefreshPacket(ushort componentId, MerchantRuntimeData runtimeMerchant, IEnumerable<uint> removedItemIds)
        {
            var writer = new LEWriter();
            writer.WriteByte(0x07);
            WriteDynamicInventoryRemoveUpdates(writer, componentId, removedItemIds);
            WriteDynamicInventoryAddUpdates(writer, componentId, runtimeMerchant);
            writer.WriteByte(0x06);
            return writer.ToArray();
        }

        private static void WriteDynamicInventoryRemoveUpdates(LEWriter writer, ushort componentId, IEnumerable<uint> itemIds)
        {
            if (itemIds == null)
                return;

            foreach (var itemId in itemIds.Distinct())
            {
                writer.WriteByte(0x35);
                writer.WriteUInt16(componentId);
                writer.WriteByte(0x1F);
                writer.WriteUInt32(itemId);
                writer.WriteByte(0x02);
                writer.WriteUInt32(0x00000000);
            }
        }

        private static void WriteDynamicInventoryAddUpdates(LEWriter writer, ushort componentId, MerchantRuntimeData runtimeMerchant)
        {
            foreach (var runtimeInv in runtimeMerchant.inventories.OrderBy(inv => inv.id))
            {
                if (runtimeInv.staticContents || !runtimeInv.autoGenerateItems || runtimeInv.items.Count == 0)
                    continue;

                foreach (var item in runtimeInv.items)
                {
                    writer.WriteByte(0x35);
                    writer.WriteUInt16(componentId);
                    writer.WriteByte(0x1E);
                    writer.WriteByte((byte)runtimeInv.id);
                    WriteItem(writer, item);
                    writer.WriteByte(0x02);
                    writer.WriteUInt32(0x00000000);
                }
            }
        }

        public static void HandleBuyItem(RRConnection conn, ushort componentId, ushort itemId,
        Dictionary<uint, List<GameServer.ZoneNPC>> zoneNPCs,
        Dictionary<string, DungeonRunners.Data.GCObject> selectedCharacters,
        Action<RRConnection, byte, byte, byte[]> sendPacket,
        GameServer server, byte buyByte1 = 0, byte buyByte2 = 0, bool isFree = false)
        {
            ushort targetId = itemId;
            Debug.LogError($"[MERCHANT-BUY] request itemId={targetId} hex=0x{targetId:X4} buyByte1=0x{buyByte1:X2} buyByte2=0x{buyByte2:X2}");
            Debug.LogError($"[MERCHANT-BUY] lookup itemId={targetId}");

            foreach (var zoneNpcEntry in zoneNPCs)
            {
                foreach (var npc in zoneNpcEntry.Value)
                {
                    if (!npc.IsMerchant || npc.MerchantId != componentId) continue;
                    if (!_runtimeMerchants.TryGetValue(npc.GCClass, out var runtimeMerchant))
                        continue;

                    foreach (var inv in runtimeMerchant.inventories)
                    {
                        MerchantItemRuntimeData item = null;
                        if (inv.staticContents && !inv.serverSendsItems)
                        {
                            int index = targetId - 255;
                            if (index >= 0 && index < inv.items.Count)
                                item = inv.items[index];
                        }
                        else
                        {
                            item = inv.items.FirstOrDefault(i => i.id == targetId);
                        }
                        if (item == null) continue;

                        var inv_final = inv;
                        var item_final = item;

                        Debug.LogError($"[MERCHANT-BUY] found item={item_final.gcType} inv='{inv_final.name}' invId={inv_final.id}");

                        if (!selectedCharacters.TryGetValue(conn.LoginName, out var gcObj))
                        {
                            Debug.LogError($"[MERCHANT-BUY] reason=no-character login={conn.LoginName}");
                            return;
                        }

                        var savedChar = DungeonRunners.Database.CharacterRepository.GetCharacter(gcObj.Id);
                        if (savedChar == null)
                        {
                            Debug.LogError("[MERCHANT-BUY] reason=no-saved-character");
                            return;
                        }

                        string memberPrefix = isFree ? "free_" : "member_";
                        uint price = item_final.price;
                        string gcLowerBuy = item_final.gcType.ToLowerInvariant();
                        bool isConsumableBuy = gcLowerBuy.Contains("consumable") || gcLowerBuy.Contains("potion")
                                            || gcLowerBuy.Contains("townportal");

                        if (isFree)
                        {
                            bool isMajorPotion = gcLowerBuy.Contains("majorhealthpotion") || gcLowerBuy.Contains("majormanapotion");
                            bool isMemberEquip = !isConsumableBuy && (
                                item_final.rarity == ItemRarity.Rare ||
                                item_final.rarity == ItemRarity.Unique ||
                                item_final.rarity == ItemRarity.Mythic);

                            if (isMajorPotion || isMemberEquip)
                            {
                                Debug.LogError($"[MERCHANT-BUY] reason=membership-required login={conn.LoginName} item={item_final.gcType} rarity={item_final.rarity}");
                                return;
                            }
                        }
                        if (isConsumableBuy)
                        {
                            var playerState = server.GetPlayerState(conn.ConnId.ToString());
                            int playerLevel = playerState != null && playerState.Level > 0 ? playerState.Level : (int)savedChar.level;
                            if (playerLevel < 1) playerLevel = 1;

                            float gcGold = 0.175f;
                            bool scaleToObserverLevel = true;

                            if (gcLowerBuy.Contains("majorhealthpotion"))
                                gcGold = 0.2f;
                            else if (gcLowerBuy.Contains("healthpotion"))
                                gcGold = 0.175f;
                            else if (gcLowerBuy.Contains("majormanapotion"))
                                gcGold = 0.2f;
                            else if (gcLowerBuy.Contains("manapotion"))
                                gcGold = 0.175f;
                            else if (gcLowerBuy.Contains("townportal"))
                            {
                                gcGold = 2.0f;
                                scaleToObserverLevel = false;
                            }

                            int effectiveLevel = scaleToObserverLevel ? Math.Max(playerLevel, 3) : 1;

                            string goldValuePerLevelText = ServerSettings.GetString(memberPrefix + "itemGoldValuePerLevel", null)
                                         ?? ServerSettings.GetString("itemGoldValuePerLevel", null);
                            float goldPerLevel = goldValuePerLevelText != null && float.TryParse(goldValuePerLevelText,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float goldValuePerLevelParsed)
                                ? goldValuePerLevelParsed : 50f;

                            string buyModifierText = ServerSettings.GetString(memberPrefix + "itemBuyValueModifier", null)
                                        ?? ServerSettings.GetString("itemBuyValueModifier", null);
                            float buyMod = buyModifierText != null && float.TryParse(buyModifierText,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float buyModifierParsed)
                                ? buyModifierParsed : 1.0f;

                            long unitPrice = Math.Max(1, (long)(goldPerLevel * effectiveLevel * gcGold * buyMod));

                            int qty = item_final.quantity > 0 ? item_final.quantity : 1;
                            price = (uint)(unitPrice * qty);

                            Debug.LogError($"[MERCHANT-BUY] consumablePrice account={memberPrefix} playerStateLevel={playerState?.Level} dbLevel={savedChar.level} effectiveLevel={effectiveLevel} gcGold={gcGold} goldPerLevel={goldPerLevel} buyMod={buyMod} unit={unitPrice} qty={qty} price={price}");
                        }
                        else if (gcLowerBuy.Contains("questitem"))
                        {
                            price = 1;
                        }
                        else
                        {
                            if (item_final.goldValue > 0)
                            {
                                price = RPGSettings.CalculatePriceWithGoldValue(
                                    item_final.level, item_final.rarity, item_final.goldValue, memberPrefix);
                                Debug.LogError($"[MERCHANT-BUY] equipmentPrice account={memberPrefix} level={item_final.level} rarity={item_final.rarity} goldValue={item_final.goldValue} price={price}");
                            }
                        }
                        Debug.LogError($"[MERCHANT-BUY] item={item_final.gcType} price={price} rarity={item_final.rarity} level={item_final.level}");
                        if (RPGSettings.IsMythicPALItem(item_final.gcType))
                            Debug.LogError($"[MERCHANT-BUY] mythic item={item_final.gcType} price={price} rarity={item_final.rarity} level={item_final.level}");

                        Debug.LogError($"[MERCHANT-BUY] gold player={savedChar.gold} price={price}");
                        if (savedChar.gold < price)
                        {
                            Debug.LogError("[MERCHANT-BUY] reason=gold");
                            return;
                        }
                        {
                            var itemDimensions = GetItemDimensions(item_final.gcType);
                            int itemWidth = itemDimensions.width, itemHeight = itemDimensions.height;
                            string connectionId = conn.ConnId.ToString();
                            bool hasSpace = false;
                            for (byte rowY = 0; rowY < 8 && !hasSpace; rowY++)
                                for (byte columnX = 0; columnX < 10 && !hasSpace; columnX++)
                                    if (!server.IsInventorySlotOccupied(connectionId, columnX, rowY, itemWidth, itemHeight))
                                        hasSpace = true;
                            if (!hasSpace)
                            {
                                Debug.LogError("[MERCHANT-BUY] reason=inventory-full");
                                return;
                            }
                        }
                        savedChar.gold -= price;
                        DungeonRunners.Database.CharacterRepository.SaveCharacter(savedChar);
                        Debug.LogError($"[MERCHANT-BUY] goldDeducted amount={price} balance={savedChar.gold}");

                        if (!inv_final.staticContents)
                        {
                            int itemIdToRemove = item_final.id;
                            int listIndex = inv_final.items.IndexOf(item_final);
                            inv_final.items.Remove(item_final);

                            var writer = new LEWriter();
                            writer.WriteByte(0x07);
                            writer.WriteByte(0x35);
                            writer.WriteUInt16(componentId);
                            writer.WriteByte(0x1F);
                            writer.WriteUInt32((uint)itemIdToRemove);

                            writer.WriteByte(0x02);
                            writer.WriteUInt32(0x00000000);
                            writer.WriteByte(0x06);

                            byte[] packet = writer.ToArray();
                            Debug.LogError($"[MERCHANT-BUY] removePacket bytes={BitConverter.ToString(packet)}");
                            Debug.LogError($"[MERCHANT-BUY] remove invId={inv.id} inv='{inv.name}' itemId={itemIdToRemove}");
                            sendPacket(conn, 0x01, 0x0F, packet);
                        }
                        else
                        {
                            Debug.LogError($"[MERCHANT-BUY] staticItem item={item_final.gcType}");
                        }

                        if (conn.UnitContainerId != 0)
                        {
                            var goldWriter = new LEWriter();
                            goldWriter.WriteByte(0x07);
                            goldWriter.WriteByte(0x35);
                            goldWriter.WriteUInt16(conn.UnitContainerId);
                            goldWriter.WriteByte(0x20);
                            goldWriter.WriteInt32(-(int)price);
                            goldWriter.WriteByte(0x00);
                            goldWriter.WriteUInt32(0x00000000);
                            goldWriter.WriteByte(0x01);
                            server.WritePlayerEntitySynch(conn, goldWriter);
                            goldWriter.WriteByte(0x06);

                            byte[] goldPacket = goldWriter.ToArray();
                            Debug.LogError($"[MERCHANT-BUY] goldUpdate amount=-{price} unitContainer=0x{conn.UnitContainerId:X4}");
                            Debug.LogError($"[MERCHANT-BUY] goldPacket bytes={BitConverter.ToString(goldPacket)}");
                            sendPacket(conn, 0x01, 0x0F, goldPacket);
                        }
                        {
                            string buyGcType = item.gcType;
                            if (buyGcType.StartsWith("items.pal.", StringComparison.OrdinalIgnoreCase))
                                buyGcType = buyGcType.Substring(10);
                            buyGcType = MapConsumableGcType(buyGcType);
                            buyGcType = buyGcType.ToLowerInvariant();

                            string buyPacketGcType = buyGcType;
                            if (HasAuthoredMerchantModSlots(buyGcType) && !buyGcType.Contains("mythicpal"))
                                buyPacketGcType = "items.pal." + buyGcType;
                            buyPacketGcType = DungeonRunners.Data.GCObject.GetPacketGCClassFor(buyPacketGcType);

                            int itemWidth = 2, itemHeight = 2;
                            var dims = GetItemDimensions(item_final.gcType);
                            itemWidth = dims.width;
                            itemHeight = dims.height;

                            string connId = conn.ConnId.ToString();

                            string gcLower2 = buyGcType.ToLower();
                            string dfcClass = "Armor";
                            if (gcLower2.Contains("consumable") || gcLower2.Contains("questitem") || gcLower2.Contains("ring") || gcLower2.Contains("amulet") || gcLower2.Contains("scroll") || gcLower2.Contains("potion"))
                                dfcClass = "Item";
                            else if (gcLower2.Contains("sword") || gcLower2.Contains("axe") || gcLower2.Contains("mace") || gcLower2.Contains("dagger") || gcLower2.Contains("hammer") || gcLower2.Contains("staff") || gcLower2.Contains("spear"))
                                dfcClass = "MeleeWeapon";
                            else if (gcLower2.Contains("bow") || gcLower2.Contains("gun") || gcLower2.Contains("crossbow"))
                                dfcClass = "RangedWeapon";

                            bool isQuestItem2 = buyGcType.Contains("questitem");

                            SetBuyPrice(conn.ConnId.ToString(), buyGcType, price);
                            Debug.LogError($"[MERCHANT-BUY] buyPriceStored item={buyGcType} price={price}");

                            bool isConsumable = !isQuestItem2 &&
                                             (item.gcType.IndexOf("consumable", StringComparison.OrdinalIgnoreCase) >= 0
                                             || buyGcType.StartsWith("potionpal.", StringComparison.OrdinalIgnoreCase));

                            int buyQty = item.quantity > 0 ? item.quantity : 1;

                            if (isQuestItem2)
                            {
                                byte slotX = 0, slotY = 0;
                                bool foundSlot = false;
                                for (byte row = 0; row < 8 && !foundSlot; row++)
                                    for (byte col = 0; col < 10 && !foundSlot; col++)
                                        if (!server.IsInventorySlotOccupied(connId, col, row, itemWidth, itemHeight))
                                        { slotX = col; slotY = row; foundSlot = true; }

                                if (!foundSlot)
                                {
                                    Debug.LogError("[MERCHANT-BUY] reason=inventory-full");
                                    return;
                                }

                                uint slot = server.GetNextInventorySlot(connId);

                                var itemWriter = new LEWriter();
                                itemWriter.WriteByte(0x07);
                                itemWriter.WriteByte(0x35);
                                itemWriter.WriteUInt16(conn.UnitContainerId);
                                itemWriter.WriteByte(0x1E);
                                itemWriter.WriteByte(0x0B);
                                itemWriter.WriteByte(0xFF);
                                itemWriter.WriteCString(buyPacketGcType);
                                itemWriter.WriteUInt32(slot);
                                itemWriter.WriteByte(slotX);
                                itemWriter.WriteByte(slotY);
                                itemWriter.WriteByte(0x01);
                                itemWriter.WriteByte(0x01);
                                itemWriter.WriteByte(0x00);
                                itemWriter.WriteByte(0x00);
                                server.WritePlayerEntitySynch(conn, itemWriter);
                                itemWriter.WriteByte(0x06);
                                sendPacket(conn, 0x01, 0x0F, itemWriter.ToArray());

                                var newItem = new DungeonRunners.Data.GCObject { GCClass = buyPacketGcType, DFCClass = "Item" };
                                server.TrackInventoryItem(connId, slot, newItem, slotX, slotY);
                                server.OccupyInventorySlots(connId, slotX, slotY, itemWidth, itemHeight);
                                server.SetStackCount(connId, slot, 1);

                                Debug.LogError($"[MERCHANT-BUY] questItem item={buyGcType} pos=({slotX},{slotY})");
                                server.SavePlayerInventoryPublic(conn);
                                server.NotifyQuestItemAcquired(conn, buyGcType);
                            }
                            else if (isConsumable)
                            {
                                int maxStack;
                                if (gcLower2.Contains("majorhealthpotion") || gcLower2.Contains("majormanapotion"))
                                    maxStack = 10;
                                else if (gcLower2.Contains("townportal"))
                                    maxStack = 5;
                                else if (gcLower2.Contains("healthpotion") || gcLower2.Contains("manapotion"))
                                    maxStack = isFree ? 5 : 10;
                                else
                                    maxStack = isFree ? 5 : 10;

                                uint partialSlot = 0;
                                byte partialX = 0, partialY = 0;
                                int partialCount = 0;
                                bool foundPartial = false;

                                var allInv = server.GetAllInventoryItems(connId);
                                if (allInv != null)
                                {
                                    string searchGc = buyPacketGcType.ToLowerInvariant();
                                    Debug.LogError($"[MERCHANT-STACK] search gc='{searchGc}' max={maxStack} items={allInv.Count}");
                                    foreach (var inventoryEntry in allInv)
                                    {
                                        string storedGc = inventoryEntry.Value.Item1.GCClass.ToLowerInvariant();
                                        int storedCount = server.GetStackCount(connId, inventoryEntry.Key);
                                        Debug.LogError($"[MERCHANT-STACK] slot={inventoryEntry.Key} gc='{storedGc}' count={storedCount}");
                                        if (storedGc == searchGc && storedCount < maxStack)
                                        {
                                            partialSlot = inventoryEntry.Key;
                                            partialX = inventoryEntry.Value.Item2;
                                            partialY = inventoryEntry.Value.Item3;
                                            partialCount = storedCount;
                                            foundPartial = true;
                                            Debug.LogError($"[MERCHANT-STACK] found slot={partialSlot} count={storedCount}");
                                            break;
                                        }
                                    }
                                    if (!foundPartial)
                                        Debug.LogError("[MERCHANT-STACK] result=new-slot");
                                }

                                bool addedToExisting = false;

                                if (foundPartial)
                                {
                                    int newCount = Math.Min(partialCount + buyQty, maxStack);

                                    var itemWriter = new LEWriter();
                                    itemWriter.WriteByte(0x07);
                                    itemWriter.WriteByte(0x35);
                                    itemWriter.WriteUInt16(conn.UnitContainerId);
                                    itemWriter.WriteByte(0x1F);
                                    itemWriter.WriteUInt32(partialSlot);
                                    server.WritePlayerEntitySynch(conn, itemWriter);
                                    itemWriter.WriteByte(0x35);
                                    itemWriter.WriteUInt16(conn.UnitContainerId);
                                    itemWriter.WriteByte(0x1E);
                                    itemWriter.WriteByte(0x0B);
                                    itemWriter.WriteByte(0xFF);
                                    itemWriter.WriteCString(buyPacketGcType);
                                    itemWriter.WriteUInt32(partialSlot);
                                    itemWriter.WriteByte(partialX);
                                    itemWriter.WriteByte(partialY);
                                    itemWriter.WriteByte((byte)newCount);
                                    itemWriter.WriteByte(0x01);
                                    itemWriter.WriteByte(0x00);
                                    itemWriter.WriteByte(0x00);
                                    server.WritePlayerEntitySynch(conn, itemWriter);
                                    itemWriter.WriteByte(0x06);
                                    sendPacket(conn, 0x01, 0x0F, itemWriter.ToArray());

                                    server.SetStackCount(connId, partialSlot, newCount);
                                    Debug.LogError($"[MERCHANT-BUY] stack item={buyGcType} old={partialCount} add={buyQty} count={newCount} pos=({partialX},{partialY}) slot={partialSlot}");
                                    server.SavePlayerInventoryPublic(conn);
                                    addedToExisting = true;
                                }

                                if (!addedToExisting)
                                {
                                    byte slotX = 0, slotY = 0;
                                    bool foundSlot = false;
                                    for (byte row = 0; row < 8 && !foundSlot; row++)
                                        for (byte col = 0; col < 10 && !foundSlot; col++)
                                            if (!server.IsInventorySlotOccupied(connId, col, row, itemWidth, itemHeight))
                                            { slotX = col; slotY = row; foundSlot = true; }

                                    if (!foundSlot)
                                    {
                                        Debug.LogError("[MERCHANT-BUY] reason=inventory-full");
                                        return;
                                    }

                                    uint slot = server.GetNextInventorySlot(connId);

                                    var itemWriter = new LEWriter();
                                    itemWriter.WriteByte(0x07);
                                    itemWriter.WriteByte(0x35);
                                    itemWriter.WriteUInt16(conn.UnitContainerId);
                                    itemWriter.WriteByte(0x1E);
                                    itemWriter.WriteByte(0x0B);
                                    itemWriter.WriteByte(0xFF);
                                    itemWriter.WriteCString(buyPacketGcType);
                                    itemWriter.WriteUInt32(slot);
                                    itemWriter.WriteByte(slotX);
                                    itemWriter.WriteByte(slotY);
                                    itemWriter.WriteByte((byte)buyQty);
                                    itemWriter.WriteByte(0x01);
                                    itemWriter.WriteByte(0x00);
                                    itemWriter.WriteByte(0x00);
                                    server.WritePlayerEntitySynch(conn, itemWriter);
                                    itemWriter.WriteByte(0x06);
                                    sendPacket(conn, 0x01, 0x0F, itemWriter.ToArray());

                                    var newItem = new DungeonRunners.Data.GCObject { GCClass = buyPacketGcType, DFCClass = "Item" };
                                    server.TrackInventoryItem(connId, slot, newItem, slotX, slotY);
                                    server.OccupyInventorySlots(connId, slotX, slotY, itemWidth, itemHeight);
                                    server.SetStackCount(connId, slot, buyQty);

                                    Debug.LogError($"[MERCHANT-BUY] newStack item={buyGcType} qty={buyQty} pos=({slotX},{slotY})");
                                    server.SavePlayerInventoryPublic(conn);
                                }
                            }
                            else
                            {
                                byte slotX = 0, slotY = 0;
                                bool foundSlot = false;
                                for (byte row = 0; row < 8 && !foundSlot; row++)
                                    for (byte col = 0; col < 10 && !foundSlot; col++)
                                        if (!server.IsInventorySlotOccupied(connId, col, row, itemWidth, itemHeight))
                                        { slotX = col; slotY = row; foundSlot = true; }

                                if (!foundSlot)
                                {
                                    Debug.LogError("[MERCHANT-BUY] reason=inventory-full");
                                    return;
                                }

                                uint slot = server.GetNextInventorySlot(connId);
                                var newItem = new DungeonRunners.Data.GCObject { GCClass = buyPacketGcType, DFCClass = dfcClass };
                                newItem.PresetScaleMod = item_final.scaleMod;
                                newItem.StoredRarity = (int)item_final.rarity;
                                newItem.StoredLevel = item_final.level > 0 ? item_final.level : RPGSettings.GetItemLevel(buyGcType);

                                var itemWriter = new LEWriter();
                                itemWriter.WriteByte(0x07);
                                itemWriter.WriteByte(0x35);
                                itemWriter.WriteUInt16(conn.UnitContainerId);
                                itemWriter.WriteByte(0x1E);
                                itemWriter.WriteByte(0x0B);
                                int actualItemLevel = item_final.level > 0 ? item_final.level : RPGSettings.GetItemLevel(buyGcType);
                                newItem.WriteInitForInventory(itemWriter, slotX, slotY, slot, actualItemLevel);
                                server.WritePlayerEntitySynch(conn, itemWriter);
                                itemWriter.WriteByte(0x06);
                                sendPacket(conn, 0x01, 0x0F, itemWriter.ToArray());

                                server.TrackInventoryItem(connId, slot, newItem, slotX, slotY);
                                server.OccupyInventorySlots(connId, slotX, slotY, itemWidth, itemHeight);

                                Debug.LogError($"[MERCHANT-BUY] equipment item={buyGcType} pos=({slotX},{slotY})");
                                server.SavePlayerInventoryPublic(conn);
                            }
                        }

                        Debug.LogError($"[MERCHANT-BUY] complete item={item.gcType} price={price}");
                        return;
                    }
                }
            }
            Debug.LogError($"[MERCHANT-BUY] reason=not-found itemId={targetId}");
        }

        public static void HandleSellItem(RRConnection conn, ushort componentId, ushort itemId, ushort entityRef,
      Dictionary<uint, List<GameServer.ZoneNPC>> zoneNPCs,
      Dictionary<string, DungeonRunners.Data.GCObject> selectedCharacters,
      Action<RRConnection, byte, byte, byte[]> sendPacket,
      PlayerState playerState,
      GameServer server,
      uint synchValue)
        {
            Debug.LogError($"[MERCHANT-SELL] itemId={itemId} entityRef=0x{entityRef:X4} componentId=0x{componentId:X4}");
            Debug.LogError($"[MERCHANT-SELL] unitContainer=0x{conn.UnitContainerId:X4} synch=0x{synchValue:X8}");

            var activeItem = playerState?.ActiveItem;
            bool isShiftClick = (activeItem == null);
            DungeonRunners.Data.GCObject sellItem = activeItem;
            byte removeX = 0, removeY = 0;

            if (isShiftClick)
            {
                var invItem = server.GetInventoryItemBySlot(conn.ConnId.ToString(), (uint)itemId);
                if (invItem == null)
                {
                    Debug.LogError($"[MERCHANT-SELL] reason=no-slot-item slot={itemId}");
                    return;
                }
                sellItem = invItem.Value.item;
                removeX = invItem.Value.x;
                removeY = invItem.Value.y;
                Debug.LogError($"[MERCHANT-SELL] mode=shift item={sellItem.GCClass} slot={itemId} pos=({removeX},{removeY})");
            }
            else
            {
                Debug.LogError($"[MERCHANT-SELL] mode=cursor item={activeItem.GCClass}");
            }

            uint sellPrice = 1;
            string sellGcType = sellItem.GCClass.ToLowerInvariant();
            if (sellGcType.StartsWith("items.pal."))
                sellGcType = sellGcType.Substring(10);
            else if (sellGcType.StartsWith("items.consumables."))
                sellGcType = sellGcType.Substring(18);
            {
                int itemLevel = RPGSettings.GetItemLevel(sellItem.GCClass);
                if (itemLevel < 1) itemLevel = 1;

                float goldValue = 1.0f;
                if (_sellableItems != null)
                {
                    var sellable = _sellableItems.FirstOrDefault(s =>
                        string.Equals(s.gcType, sellGcType, StringComparison.OrdinalIgnoreCase));
                    if (sellable == null)
                        sellable = _sellableItems.FirstOrDefault(s =>
                            s.gcType.ToLower().Contains(sellGcType) || sellGcType.Contains(s.gcType.ToLower()));
                    if (sellable != null && sellable.gcGoldValue > 0)
                        goldValue = sellable.gcGoldValue;
                    else
                        goldValue = RPGSettings.GetBaseGoldValue(sellGcType);
                }
                else
                    goldValue = RPGSettings.GetBaseGoldValue(sellGcType);

                int tierSuffix = RPGSettings.GetTierFromGcType(sellGcType);
                var itemRarity = RPGSettings.GetRarityFromTier(tierSuffix);

                bool isMythicSell = RPGSettings.IsMythicPALItem(sellGcType);
                int pLevel = (playerState != null && playerState.Level > 0) ? playerState.Level : 0;
                if (isMythicSell)
                {
                    itemRarity = ItemRarity.Mythic;
                    if (pLevel > 0)
                        itemLevel = pLevel + 3;
                    Debug.LogError($"[MERCHANT-SELL] mythic item={sellGcType} rarity=Mythic level={itemLevel} playerLevel={pLevel}");
                }

                int adjustedLevel = RPGSettings.GetEquipRequiredLevel(itemLevel, itemRarity);

                sellPrice = RPGSettings.CalculateSellPrice(adjustedLevel, goldValue, itemRarity, isMythicSell, pLevel);

                uint buyPrice = RPGSettings.CalculatePriceWithGoldValue(adjustedLevel, itemRarity, goldValue);
                if (sellPrice > buyPrice)
                {
                    Debug.LogError($"[MERCHANT-SELL] priceCap sell={sellPrice} buy={buyPrice} level={itemLevel} adjustedLevel={adjustedLevel} rarity={itemRarity} goldValue={goldValue}");
                    sellPrice = buyPrice;
                }

                Debug.LogError($"[MERCHANT-SELL] price level={itemLevel} adjustedLevel={adjustedLevel} goldValue={goldValue} rarity={itemRarity} mythic={isMythicSell} playerLevel={pLevel} sell={sellPrice} buy={buyPrice}");
            }

            if (!selectedCharacters.TryGetValue(conn.LoginName, out var gcObj)) return;
            var savedChar = DungeonRunners.Database.CharacterRepository.GetCharacter(gcObj.Id);
            if (savedChar == null) return;

            uint oldGold = savedChar.gold;
            savedChar.gold += sellPrice;
            DungeonRunners.Database.CharacterRepository.SaveCharacter(savedChar);
            Debug.LogError($"[MERCHANT-SELL] gold old={oldGold} new={savedChar.gold} delta={sellPrice}");

            if (conn.UnitContainerId != 0)
            {
                var writer = new LEWriter();
                writer.WriteByte(0x07);

                if (isShiftClick)
                {
                    writer.WriteByte(0x35);
                    writer.WriteUInt16(conn.UnitContainerId);
                    writer.WriteByte(0x1F);
                    writer.WriteUInt32((uint)itemId);
                    server.WritePlayerEntitySynch(conn, writer);
                }
                else
                {
                    writer.WriteByte(0x35);
                    writer.WriteUInt16(conn.UnitContainerId);
                    writer.WriteByte(0x29);
                    server.WritePlayerEntitySynch(conn, writer);
                }

                writer.WriteByte(0x35);
                writer.WriteUInt16(conn.UnitContainerId);
                writer.WriteByte(0x20);
                writer.WriteUInt32(sellPrice);
                writer.WriteByte(0x00);
                writer.WriteUInt32(0x00000000);
                writer.WriteByte(0x01);
                server.WritePlayerEntitySynch(conn, writer);

                writer.WriteByte(0x06);

                byte[] sellPacket = writer.ToArray();
                Debug.LogError($"[MERCHANT-SELL] packet bytes={sellPacket.Length} data={BitConverter.ToString(sellPacket)}");
                sendPacket(conn, 0x01, 0x0F, sellPacket);
            }

            string connId = conn.ConnId.ToString();
            var removed = server.GetAndRemoveInventoryItem(connId, (uint)itemId);
            if (removed != null)
                Debug.LogError($"[MERCHANT-SELL] removed slot={itemId} item={removed.Value.item.GCClass}");
            else
                Debug.LogError($"[MERCHANT-SELL] reason=not-tracked slot={itemId}");

            if (!isShiftClick)
                playerState.ActiveItem = null;
            server.SavePlayerInventoryPublic(conn);

            Debug.LogError($"[MERCHANT-SELL] complete item={sellItem.GCClass} price={sellPrice} gold={savedChar.gold}");
        }








        private static string MapConsumableGcType(string gcType)
        {
            string lower = gcType.ToLowerInvariant();
            if (lower.Contains("consumable_majorhealthpotion"))
                return "potionpal.healthpotion_itempack";
            if (lower.Contains("consumable_minorhealthpotion") || lower.Contains("consumable_healthpotion"))
                return "potionpal.healthpotion_noob";
            if (lower.Contains("consumable_majormanapotion"))
                return "potionpal.manapotion_itempack";
            if (lower.Contains("consumable_minormanapotion") || lower.Contains("consumable_manapotion"))
                return "potionpal.manapotion_noob";
            if (lower.Contains("consumable_townportal"))
                return "items.consumables.Consumable_TownPortal";
            return gcType;
        }


        private static void WriteItem(LEWriter writer, MerchantItemRuntimeData item)
        {
            bool verboseItemLogging = VerboseMerchantItemLogging;
            string gcTypeToSend = item.gcType;
            if (gcTypeToSend.StartsWith("items.pal.", StringComparison.OrdinalIgnoreCase))
                gcTypeToSend = gcTypeToSend.Substring(10);
            else if (gcTypeToSend.StartsWith("items.consumables.", StringComparison.OrdinalIgnoreCase))
                gcTypeToSend = gcTypeToSend.Substring(18);
            gcTypeToSend = gcTypeToSend.ToLowerInvariant();

            bool isConsumable = item.gcType.IndexOf("consumable", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isQuestItem = item.gcType.IndexOf("QuestItem", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isSimpleItem = isConsumable || isQuestItem;

            var itemData = DatabaseLoader.FindItem(gcTypeToSend);
            if (itemData == null)
            {
                Debug.LogError($"[MERCHANT-WRITE-ITEM] itemMissing gc={gcTypeToSend} original={item.gcType} source=default");
            }
            int modSlots;
            if (isSimpleItem)
            {
                modSlots = itemData?.modCount ?? 1;
            }
            else if (TryGetAuthoredMerchantModSlots(gcTypeToSend, out int specialSlots))
            {
                modSlots = specialSlots;
                if (verboseItemLogging)
                    Debug.LogError($"[MERCHANT-WRITE-ITEM] modSlots={modSlots} source=lookup gc={gcTypeToSend}");
            }
            else
            {
                if (itemData != null)
                {
                    modSlots = itemData.modCount;
                }
                else
                {
                    bool isFallbackWeapon = IsWeaponType(gcTypeToSend);
                    bool isFallbackChain = gcTypeToSend.StartsWith("chain") && !gcTypeToSend.Contains("shield");
                    modSlots = (isFallbackWeapon || isFallbackChain) ? 1 : 2;
                    Debug.LogError($"[MERCHANT-WRITE-ITEM] itemMissing gc={gcTypeToSend} source=type-default modSlots={modSlots} fallbackWeapon={isFallbackWeapon}");
                }
                if (gcTypeToSend.StartsWith("chain") && !gcTypeToSend.Contains("shield") && !gcTypeToSend.Contains("mythic"))
                    modSlots = 1;
                if (verboseItemLogging)
                    Debug.LogError($"[MERCHANT-WRITE-ITEM] modSlots={modSlots} source={(itemData != null ? "db" : "default")} gc={gcTypeToSend}");
            }
            if (modSlots < 0 || modSlots > 12)
            {
                Debug.LogError($"[MERCHANT-WRITE-ITEM] reason=bad-modSlots gc={gcTypeToSend} modSlots={modSlots} clamp=2");
                modSlots = 2;
            }
            string packetGcType = gcTypeToSend;
            if (HasAuthoredMerchantModSlots(gcTypeToSend) && !gcTypeToSend.Contains("mythicpal"))
                packetGcType = "items.pal." + gcTypeToSend;
            packetGcType = DungeonRunners.Data.GCObject.GetPacketGCClassFor(packetGcType);

            if (verboseItemLogging)
            {
                Debug.LogError($"[MERCHANT-WRITE-ITEM] write item={item.gcType} id={item.id} rarity={item.rarity} qty={item.quantity} scaleMod={item.scaleMod ?? "NULL"} modSlots={modSlots}");
                Debug.LogError($"[MERCHANT-WRITE-ITEM] gc={gcTypeToSend} consumable={isConsumable} quest={isQuestItem} modSlots={modSlots} dbFound={itemData != null}");
            }
            writer.WriteByte(0xFF);
            writer.WriteCString(packetGcType);
            writer.WriteUInt32((uint)item.id);
            writer.WriteByte((byte)item.inventoryX);
            writer.WriteByte((byte)item.inventoryY);
            writer.WriteByte((byte)item.quantity);
            int level = item.level > 0 ? item.level : (isSimpleItem ? 1 : RPGSettings.GetItemLevel(item.gcType));
            writer.WriteByte((byte)level);


            for (int modSlot = 0; modSlot < modSlots; modSlot++)
            {
                writer.WriteByte(0x00);
            }

            if (isConsumable)
            {
                writer.WriteByte(0x01);
                writer.WriteByte(0xFF);
                writer.WriteCString("mods.itemscale.normal");
                writer.WriteByte(0x03);
                writer.WriteByte(0x15);
                writer.WriteUInt32(0x11111111);
                if (verboseItemLogging)
                    Debug.LogError($"[MERCHANT-WRITE-ITEM] consumable gc={gcTypeToSend} modSlots={modSlots}");
            }
            else if (isQuestItem)
            {
                writer.WriteByte(0x00);
                if (verboseItemLogging)
                    Debug.LogError($"[MERCHANT-WRITE-ITEM] quest gc={gcTypeToSend} modSlots={modSlots}");
            }
            else
            {
                bool hasModChildren = gcTypeToSend.Contains("mythic") ||
                    gcTypeToSend.Contains("prebuilt") ||
                    gcTypeToSend.Contains("partialbuilt") ||
                    gcTypeToSend.Contains("seasonal") ||
                    gcTypeToSend.Contains("wishingwell") ||
                    gcTypeToSend.Contains("boss") ||
                    gcTypeToSend.Contains("generated");

                var wireMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetItemWireMods(gcTypeToSend);
                string wireSource = wireMods.Count > 0 ? "direct" : null;
                if (wireMods.Count == 0)
                {
                    wireMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetWrapperIGWireMods(gcTypeToSend, item.rarity.ToString());
                    if (wireMods.Count > 0) wireSource = $"wrapper:{item.rarity}";
                }
                bool hasInjectableMods = DungeonRunners.Data.ItemStatDatabase.PathBEnabled && wireMods.Count > 0;

                if (hasModChildren || hasInjectableMods)
                {
                    if (wireMods.Count > 0)
                    {
                        var resolved = new List<uint>(wireMods.Count);
                        var skipped = new List<string>();
                        foreach (var (slot, modRef) in wireMods.OrderBy(w => w.Slot))
                        {
                            uint h = DungeonRunners.Data.ItemStatDatabase.Instance.GetGCClassHash(modRef);
                            if (h != 0) resolved.Add(h);
                            else skipped.Add(modRef);
                        }
                        if (resolved.Count == 0)
                        {
                            writer.WriteByte(0x00);
                            Debug.LogError($"[MERCHANT-WRITE-ITEM] wireMods gc={gcTypeToSend} rarity={item.rarity} src={wireSource} count=0 skipped=[{string.Join(",", skipped)}]");
                        }
                        else
                        {
                            writer.WriteByte((byte)resolved.Count);
                            foreach (uint h in resolved)
                            {
                                writer.WriteByte(0x04);
                                writer.WriteUInt32(h);
                                writer.WriteByte(0x00);
                            }
                            if (verboseItemLogging)
                            {
                                string hashes = string.Join(",", resolved.Select(x => $"0x{x:X8}"));
                                Debug.LogError($"[MERCHANT-WRITE-ITEM] wireMods gc={gcTypeToSend} rarity={item.rarity} src={wireSource} modSlots={modSlots} mods={resolved.Count} skipped={skipped.Count} djb2=[{hashes}]");
                            }
                        }
                    }
                    else
                    {
                        writer.WriteByte(0x00);
                        if (verboseItemLogging)
                            Debug.LogError($"[MERCHANT-WRITE-ITEM] special gc={gcTypeToSend} rarity={item.rarity} modSlots={modSlots} dbMod={itemData?.modCount}");
                    }
                }
                else
                {
                    string scaleMod = !string.IsNullOrEmpty(item.scaleMod) ? item.scaleMod : RPGSettings.GetRandomScaleMod(item.rarity);
                    if (verboseItemLogging)
                        Debug.LogError($"[MERCHANT-WRITE-ITEM] scaleMod gc={gcTypeToSend} rarity={item.rarity} scaleMod={scaleMod} modSlots={modSlots} dbMod={itemData?.modCount}");
                    writer.WriteByte(0x01);
                    writer.WriteByte(0xFF);
                    writer.WriteCString(scaleMod);
                    writer.WriteByte(0x03);
                    writer.WriteByte(0x15);
                    writer.WriteUInt32(0x11111111);
                }
            }
        }

    }


    [Serializable]
    public class SellableItem
    {
        public string gcType;
        public string name;
        public float goldValue;
        public float gcGoldValue;
        public string rarity;
    }

    [Serializable]
    public class ItemDimensions
    {
        public int width;
        public int height;
    }

    public class PendingMerchantRefreshAdd
    {
        public uint zoneId;
        public string npcGcClass;
        public ushort componentId;
        public List<uint> removedItemIds = new List<uint>();
        public DateTime sendAfterUtc;
    }

    [Serializable]
    public class MerchantData
    {
        public string npcGcType;
        public string merchantGcType;
        public List<MerchantInventoryData> inventories = new List<MerchantInventoryData>();
    }

    [Serializable]
    public class MerchantInventoryData
    {
        public string name;
        public string gcType;
        public int id;
        public bool staticContents;
        public bool autoGenerateItems;
        public string itemGenerator;
        public int minItemLevel;
        public int maxItemLevel;
        public float regenerateIntervalSeconds;
        public string label;
        public int width;
        public int height;
        public List<MerchantItemData> items = new List<MerchantItemData>();
    }

    [Serializable]
    public class MerchantItemData
    {
        public string gcType;
        public int inventoryX;
        public int inventoryY;
        public int id;
        public int quantity = 1;
    }

    public class MerchantRuntimeData
    {
        public string npcGcType;
        public string merchantGcType;
        public int nextItemId = 0;
        public List<MerchantInventoryRuntimeData> inventories = new List<MerchantInventoryRuntimeData>();
    }

    public class MerchantInventoryRuntimeData
    {
        public string name;
        public string gcType;
        public int id;
        public string label;
        public int width;
        public int height;
        public bool staticContents;
        public bool serverSendsItems;
        public bool autoGenerateItems;
        public string itemGenerator;
        public int minItemLevel;
        public int maxItemLevel;
        public float regenerateIntervalSeconds;
        public int generatedForLevel;
        public List<MerchantItemRuntimeData> items = new List<MerchantItemRuntimeData>();
    }

    public class MerchantItemRuntimeData
    {
        public string gcType;
        public int inventoryX;
        public int inventoryY;
        public int id;
        public int quantity;
        public int level;
        public ItemRarity rarity = ItemRarity.Rare;
        public uint price;
        public float goldValue;
        public string scaleMod;
    }
    public enum ItemRarity
    {
        Normal,
        Superior,
        Magical,
        Rare,
        Unique,
        Mythic
    }


    public static class RPGSettings
    {
        private static readonly Dictionary<ItemRarity, int> QualityModifiersFixed32 = new Dictionary<ItemRarity, int>
    {
        { ItemRarity.Normal, 67 },
        { ItemRarity.Superior, 144 },
        { ItemRarity.Magical, 274 },
        { ItemRarity.Rare, 520 },
        { ItemRarity.Unique, 1170 },
        { ItemRarity.Mythic, 9961 }
    };

        private static readonly Dictionary<ItemRarity, int> LevelDeltas = new Dictionary<ItemRarity, int>
    {
        { ItemRarity.Normal, -12 },
        { ItemRarity.Superior, -10 },
        { ItemRarity.Magical, -7 },
        { ItemRarity.Rare, -5 },
        { ItemRarity.Unique, -2 },
        { ItemRarity.Mythic, 3 }
    };

        private static readonly Dictionary<ItemRarity, string[]> ScaleMods = new Dictionary<ItemRarity, string[]>
    {
        { ItemRarity.Normal, new[] { "ScaleModPAL.Binder.Mod1" } },
        { ItemRarity.Superior, new[] { "ScaleModPAL.Superior.Mod1", "ScaleModPAL.Superior.Mod2", "ScaleModPAL.Superior.Mod3" } },
        { ItemRarity.Magical, new[] { "ScaleModPAL.Magic.Mod1", "ScaleModPAL.Magic.Mod2", "ScaleModPAL.Magic.Mod3", "ScaleModPAL.Magic.Mod4", "ScaleModPAL.Magic.Mod5", "ScaleModPAL.Magic.Mod6" } },
        { ItemRarity.Rare, new[] { "ScaleModPAL.Rare.Mod1", "ScaleModPAL.Rare.Mod2", "ScaleModPAL.Rare.Mod3", "ScaleModPAL.Rare.Mod4", "ScaleModPAL.Rare.Mod5" } },
        { ItemRarity.Unique, new[] { "ScaleModPAL.Unique.Mod0", "ScaleModPAL.Unique.Mod1", "ScaleModPAL.Unique.Mod2", "ScaleModPAL.Unique.Mod3", "ScaleModPAL.Unique.Mod4", "ScaleModPAL.Unique.Mod5", "ScaleModPAL.Unique.Mod6", "ScaleModPAL.Unique.Mod7", "ScaleModPAL.Unique.Mod8" } },
        { ItemRarity.Mythic, new[] { "ScaleModPAL.Rare.Mod1" } }
    };

        private static readonly (ItemRarity rarity, int weight)[] RarityWeights = new[]
        {
        (ItemRarity.Normal, 15),
        (ItemRarity.Superior, 25),
        (ItemRarity.Magical, 30),
        (ItemRarity.Rare, 20),
        (ItemRarity.Unique, 8),
        (ItemRarity.Mythic, 2)
    };

        private static System.Random _random = new System.Random();

        public static float GetPriceModifier(ItemRarity rarity)
        {
            return GetQualityModFixed32(rarity) / 256f;
        }

        public static int GetEquipRequiredLevel(int itemLevel, ItemRarity rarity)
        {
            int delta;
            switch (rarity)
            {
                case ItemRarity.Normal: delta = (int)ServerSettings.GetFloat("itemLevelDeltaNormal", -12); break;
                case ItemRarity.Superior: delta = (int)ServerSettings.GetFloat("itemLevelDeltaSuperior", -10); break;
                case ItemRarity.Magical: delta = (int)ServerSettings.GetFloat("itemLevelDeltaMagical", -7); break;
                case ItemRarity.Rare: delta = (int)ServerSettings.GetFloat("itemLevelDeltaRare", -5); break;
                case ItemRarity.Unique: delta = (int)ServerSettings.GetFloat("itemLevelDeltaUnique", -2); break;
                case ItemRarity.Mythic: delta = (int)ServerSettings.GetFloat("itemLevelDeltaMythic", 3); break;
                default: delta = LevelDeltas.TryGetValue(rarity, out var ld) ? ld : 0; break;
            }
            return Math.Max(1, itemLevel + delta);
        }

        public static string GetRandomScaleMod(ItemRarity rarity)
        {
            if (ScaleMods.TryGetValue(rarity, out var mods) && mods.Length > 0)
                return mods[_random.Next(mods.Length)];
            return "ScaleModPAL.Rare.Mod1";
        }

        public static string GetDeterministicScaleMod(string gcClass, ItemRarity rarity)
        {
            if (ScaleMods.TryGetValue(rarity, out var mods) && mods.Length > 0)
            {
                uint h = 5381;
                if (!string.IsNullOrEmpty(gcClass))
                    foreach (char c in gcClass.ToLowerInvariant()) h = h * 33u + (uint)c;
                return mods[(int)(h % (uint)mods.Length)];
            }
            return "ScaleModPAL.Rare.Mod1";
        }

        public static ItemRarity GetRandomRarity()
        {
            int totalWeight = 0;
            foreach (var (_, weight) in RarityWeights)
                totalWeight += weight;

            int roll = _random.Next(totalWeight);
            int cumulative = 0;

            foreach (var (rarity, weight) in RarityWeights)
            {
                cumulative += weight;
                if (roll < cumulative)
                    return rarity;
            }
            return ItemRarity.Rare;
        }

        public static ItemRarity GetRarityFromTier(int tier)
        {
            return tier switch
            {
                1 => ItemRarity.Normal,
                2 => ItemRarity.Superior,
                3 => ItemRarity.Magical,
                4 => ItemRarity.Rare,
                5 => ItemRarity.Unique,
                _ => ItemRarity.Rare
            };
        }

        public static bool IsMythicPALItem(string gcType)
        {
            if (string.IsNullOrEmpty(gcType)) return false;
            return gcType.IndexOf("mythicpal", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static int GetTierFromGcType(string gcType)
        {
            if (string.IsNullOrEmpty(gcType)) return 1;
            int dashIdx = gcType.LastIndexOf('-');
            if (dashIdx > 0 && dashIdx < gcType.Length - 1)
            {
                if (int.TryParse(gcType.Substring(dashIdx + 1), out int tier))
                    return tier;
            }
            return 1;
        }

        public static int GetItemLevel(string gcType)
        {
            if (string.IsNullOrEmpty(gcType)) return 1;
            int palIdx = gcType.LastIndexOf("PAL", StringComparison.OrdinalIgnoreCase);
            if (palIdx > 0)
            {
                int numEnd = palIdx;
                int numStart = numEnd - 1;
                while (numStart >= 0 && char.IsDigit(gcType[numStart]))
                    numStart--;
                numStart++;
                if (numStart < numEnd)
                {
                    string tierStr = gcType.Substring(numStart, numEnd - numStart);
                    if (int.TryParse(tierStr, out int palTier))
                    {
                        if (palTier <= 1) return 1;
                        return (palTier - 1) * 10 + 1;
                    }
                }
            }
            return 1;
        }

        public static float GetBaseGoldValue(string gcType)
        {
            if (string.IsNullOrEmpty(gcType)) return 1.0f;
            string lower = gcType.ToLowerInvariant();
            if (lower.Contains("armor") || lower.Contains("robe")) return 4.0f;
            if (lower.Contains("shoulder") || lower.Contains("pauldron")) return 2.0f;
            if (lower.Contains("shield") || lower.Contains("buckler")) return 2.5f;
            if (lower.Contains("staff")) return 1.0f;
            if (lower.Contains("2h") || lower.Contains("cannon") || lower.Contains("crossbow") || lower.Contains("rifle")) return 2.0f;
            if (lower.Contains("helm") || lower.Contains("hat") || lower.Contains("hood") || lower.Contains("cap")) return 1.5f;
            if (lower.Contains("boot") || lower.Contains("shoe") || lower.Contains("greave")) return 1.25f;
            if (lower.Contains("potion")) return 0.2f;
            return 1.0f;
        }

        public static uint CalculatePrice(int tier, ItemRarity rarity)
        {
            int level = tier <= 1 ? 1 : (tier - 1) * 10 + 1;
            return CalculatePriceWithGoldValue(level, rarity, 1.0f);
        }

        public static uint CalculatePriceWithGoldValue(int level, ItemRarity rarity, float goldValue, string prefix = "")
        {
            int adjustedLevel = GetEquipRequiredLevel(level, rarity);

            string goldValuePerLevelText = (!string.IsNullOrEmpty(prefix))
                ? (ServerSettings.GetString(prefix + "itemGoldValuePerLevel", null) ?? ServerSettings.GetString("itemGoldValuePerLevel", null))
                : ServerSettings.GetString("itemGoldValuePerLevel", null);
            int goldPerLevel = goldValuePerLevelText != null && float.TryParse(goldValuePerLevelText,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float goldValuePerLevelParsed) ? (int)goldValuePerLevelParsed : 50;

            int qualityModFixed32 = GetQualityModFixed32(rarity, prefix);

            string buyModifierText = (!string.IsNullOrEmpty(prefix))
                ? (ServerSettings.GetString(prefix + "itemBuyValueModifier", null) ?? ServerSettings.GetString("itemBuyValueModifier", null))
                : ServerSettings.GetString("itemBuyValueModifier", null);
            float buyMod = buyModifierText != null && float.TryParse(buyModifierText,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float buyModifierParsed) ? buyModifierParsed : 1.0f;

            int goldValueFixed32 = (int)Math.Round(goldValue * 256);
            int buyModFixed32 = (int)Math.Round(buyMod * 256);

            long numerator = (long)goldPerLevel * adjustedLevel * goldValueFixed32 * qualityModFixed32;
            long price = numerator / 65536;

            price = (price * buyModFixed32) / 256;

            return (uint)Math.Max(1, price);
        }

        private static int GetQualityModFixed32(ItemRarity rarity, string prefix = "")
        {
            string key = null;
            switch (rarity)
            {
                case ItemRarity.Normal: key = "itemPriceModifierNormal"; break;
                case ItemRarity.Superior: key = "itemPriceModifierSuperior"; break;
                case ItemRarity.Magical: key = "itemPriceModifierMagical"; break;
                case ItemRarity.Rare: key = "itemPriceModifierRare"; break;
                case ItemRarity.Unique: key = "itemPriceModifierUnique"; break;
                case ItemRarity.Mythic: key = "itemPriceModifierMythic"; break;
            }

            if (key == null)
                throw new InvalidDataException($"No client ItemPriceModifier key for rarity {rarity}");

            var globalKnobs = GCDatabase.Instance.GlobalKnobs;
            if (globalKnobs == null || !globalKnobs.HasProperty(key))
                throw new InvalidDataException($"GlobalKnobs missing client RPGSettings field {key}");

            if (!QualityModifiersFixed32.TryGetValue(rarity, out var qm))
                throw new InvalidDataException($"No client Fixed32 mirror for GlobalKnobs.{key}");

            return qm;
        }

        public static uint CalculateBuyPrice(int level, ItemRarity rarity, float goldValue)
        {
            return CalculatePriceWithGoldValue(level, rarity, goldValue);
        }

        public static uint CalculateSellPrice(int level, float goldValue, ItemRarity rarity = ItemRarity.Normal, bool isMythicPAL = false, int playerLevel = 0)
        {
            if (level < 1) level = 1;

            int sellLevel = level;
            if (playerLevel > 0)
            {
                int cap = playerLevel + 5;
                if (sellLevel > cap) sellLevel = cap;
            }
            if (isMythicPAL && playerLevel > 0)
                sellLevel = playerLevel + 5;

            int goldPerLevel = (int)ServerSettings.GetFloat("itemGoldValuePerLevel", 50f);
            int qualityModFixed32 = GetQualityModFixed32(rarity);
            int goldValueFixed32 = (int)Math.Round(goldValue * 256);

            int gpl_Q8 = goldPerLevel * 256;
            int level_Q8 = sellLevel * 256;
            long step1 = ((long)gpl_Q8 * level_Q8) / 256;

            int modifiedGV = (int)((long)goldValueFixed32 * qualityModFixed32 / 256);
            long step2 = (step1 * modifiedGV) / 256;

            int getValueResult = (int)(step2 >> 8);
            if (getValueResult < 1) getValueResult = 1;

            long valueQ8 = (long)getValueResult * 256;

            int sellModFixed32 = 52;
            long sellQ8 = (valueQ8 * sellModFixed32) / 256;

            int sellPrice = (int)(sellQ8 >> 8);
            return (uint)Math.Max(1, sellPrice);
        }
    }
}
