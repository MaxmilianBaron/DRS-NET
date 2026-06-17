using System;
using System.Collections.Generic;
using System.IO;
using DungeonRunners.Engine;
using DungeonRunners.Core;
using Debug = DungeonRunners.Engine.Debug;
using DungeonRunners.Gameplay;
using Mono.Data.Sqlite;
using DB = DungeonRunners.Database.GameDatabase;

public static class DatabaseLoader
{
    public static string GetDatabaseFilePath(string filename)
    {
        return DungeonRunners.Core.DataPaths.DatabaseFile(filename);
    }

    public static Dictionary<string, List<DungeonSpawnData>> DungeonSpawns = new Dictionary<string, List<DungeonSpawnData>>();
    public static List<ItemData> AllArmor = new List<ItemData>();
    public static List<ItemData> AllWeapons = new List<ItemData>();
    public static Dictionary<string, ItemData> ItemDatabase = new Dictionary<string, ItemData>();
    public static List<SkillData> Skills = new List<SkillData>();
    public static List<QuestData> Quests = new List<QuestData>();
    public static Dictionary<uint, QuestData> QuestsByHash = new Dictionary<uint, QuestData>();
    public static List<CheckpointData> Checkpoints = new List<CheckpointData>();
    public static List<NPCData> TownNPCs = new List<NPCData>();
    public static List<NPCData> TutorialNPCs = new List<NPCData>();
    public static List<NPCData> PvpNPCs = new List<NPCData>();
    public static List<CreatureData> Creatures = new List<CreatureData>();
    public static List<SummonData> Summons = new List<SummonData>();
    public static Dictionary<string, CreatureData> CreatureDatabase = new Dictionary<string, CreatureData>();
    public static Dictionary<string, SummonData> SummonDatabase = new Dictionary<string, SummonData>();
    public static List<ZonePortalData> ZonePortals = new List<ZonePortalData>();
    public static List<ZoneWaypointData> ZoneWaypoints = new List<ZoneWaypointData>();
    public static Dictionary<string, List<ZonePortalData>> PortalsByZone = new Dictionary<string, List<ZonePortalData>>();
    public static Dictionary<string, List<ZoneWaypointData>> WaypointsByZone = new Dictionary<string, List<ZoneWaypointData>>();
    public static List<ZoneCheckpointData> ZoneCheckpoints = new List<ZoneCheckpointData>();
    public static Dictionary<string, List<ZoneCheckpointData>> CheckpointsByZone = new Dictionary<string, List<ZoneCheckpointData>>();
    public static Dictionary<string, CheckpointData> CheckpointDatabase = new Dictionary<string, CheckpointData>(StringComparer.OrdinalIgnoreCase);
    public static Dictionary<string, List<ItemData>> Helmets = new Dictionary<string, List<ItemData>>();
    public static Dictionary<string, List<ItemData>> Armors = new Dictionary<string, List<ItemData>>();
    public static Dictionary<string, List<ItemData>> Gloves = new Dictionary<string, List<ItemData>>();
    public static Dictionary<string, List<ItemData>> Boots = new Dictionary<string, List<ItemData>>();
    public static Dictionary<string, List<ItemData>> MeleeWeapons = new Dictionary<string, List<ItemData>>();
    public static Dictionary<string, List<ItemData>> RangedWeapons = new Dictionary<string, List<ItemData>>();
    public static List<GeneralItemData> QuestItems = new List<GeneralItemData>();
    public static List<GeneralItemData> Relics = new List<GeneralItemData>();
    public static List<GeneralItemData> Rings = new List<GeneralItemData>();
    public static List<GeneralItemData> Amulets = new List<GeneralItemData>();
    public static List<GeneralItemData> Potions = new List<GeneralItemData>();
    public static List<GeneralItemData> Skillbooks = new List<GeneralItemData>();
    public static List<GeneralItemData> Keys = new List<GeneralItemData>();
    public static List<GeneralItemData> Scrolls = new List<GeneralItemData>();
    public static List<GeneralItemData> DungeonItems = new List<GeneralItemData>();
    public static List<GeneralItemData> Vouchers = new List<GeneralItemData>();
    public static List<GeneralItemData> ItemPacks = new List<GeneralItemData>();
    public static List<GeneralItemData> Consumables = new List<GeneralItemData>();
    public static Dictionary<string, GeneralItemData> GeneralItemDatabase = new Dictionary<string, GeneralItemData>(StringComparer.OrdinalIgnoreCase);
    public static List<MerchantData> Merchants = new List<MerchantData>();
    public static Dictionary<string, MerchantData> MerchantsByNpc = new Dictionary<string, MerchantData>(StringComparer.OrdinalIgnoreCase);

    public class QuestKillDropEntry
    {
        public string QuestId;
        public string ItemGcType;
        public int Chance;
    }
    public static Dictionary<string, List<QuestKillDropEntry>> QuestKillDropsByMonster
        = new Dictionary<string, List<QuestKillDropEntry>>(StringComparer.OrdinalIgnoreCase);

    [Serializable] public class DungeonSpawnData { public string zoneName; public string gcType; public string spawnGcTypeOverride; public float posX; public float posY; public float posZ; public float heading; public string encounterGroupKey; public float encounterDifficulty = -1f; public int gridX = -1; public int gridY = -1; public string tileType; public float worldOriginX; public float worldOriginY; public float localX; public float localY; public float localZ; public string placementRole; public string placeholderSource; public int placeholderIndex = -1; public float placeholderSizeX; public float placeholderSizeY; public int encounterChoiceIndex = -1; public bool snapApplied; }

    public static void LoadAll()
    {
        Debug.Log("Loading ALL data direct from SQLite columns...");
        try
        {
            DB.Initialize();
            LoadSkills(); LoadQuests(); LoadCheckpoints();
            LoadNPCsForZone("town", TownNPCs); LoadNPCsForZone("tutorial", TutorialNPCs); LoadNPCsForZone("pvp_start", PvpNPCs);
            LoadZonePortals(); LoadZoneWaypoints(); LoadZoneCheckpoints();
            LoadCreatures(); LoadSummons();
            LoadGeneralItems(); LoadMerchants(); LoadDungeonSpawns();
            LoadQuestKillDrops();
            PathMapCatalog.Instance.LoadAllPathMaps();
            LoadEquipment("armor", AllArmor); LoadEquipment("weapons", AllWeapons);
            BuildEquipmentMappings();
            Debug.Log($"ALL LOADED: Skills:{Skills.Count} Quests:{Quests.Count} Creatures:{Creatures.Count} Weapons:{AllWeapons.Count} Armor:{AllArmor.Count} Merchants:{Merchants.Count} TownNPCs:{TownNPCs.Count} TutorialNPCs:{TutorialNPCs.Count} QuestKillDrops:{QuestKillDropsByMonster.Count}");
        }
        catch (Exception ex) { Debug.LogError($"[DB-LOAD] error={ex.Message}\n{ex.StackTrace}"); throw; }
    }

    private static void LoadSkills()
    {
        Skills.Clear();
        using (var connection = DB.GetConnection()) using (var reader = DB.ExecuteReader(connection, "SELECT * FROM skills"))
            while (reader.Read())
                Skills.Add(new SkillData
                {
                    id = DB.GetString(reader, "gc_type"),
                    name = DB.GetString(reader, "name"),
                    description = DB.GetString(reader, "description"),
                    level = DB.GetInt(reader, "level", 1),
                    experience = DB.GetInt(reader, "experience"),
                    maxLevel = DB.GetInt(reader, "max_level", 10),
                    attributes = new SkillAttributes
                    {
                        strength = DB.GetInt(reader, "attr_strength"),
                        dexterity = DB.GetInt(reader, "attr_dexterity"),
                        vitality = DB.GetInt(reader, "attr_vitality"),
                        intelligence = DB.GetInt(reader, "attr_intelligence"),
                        wisdom = DB.GetInt(reader, "attr_wisdom"),
                        spirit = DB.GetInt(reader, "attr_spirit"),
                        perception = DB.GetInt(reader, "attr_perception"),
                        agility = DB.GetInt(reader, "attr_agility")
                    }
                });
    }

    private static void LoadQuests()
    {
        Quests.Clear(); QuestsByHash.Clear();
        var objectivesByQuest = new Dictionary<string, List<QuestObjective>>(StringComparer.OrdinalIgnoreCase);
        using (var connection = DB.GetConnection()) using (var reader = DB.ExecuteReader(connection, "SELECT * FROM quest_objective_templates"))
            while (reader.Read())
            {
                string questId = DB.GetString(reader, "quest_id");
                if (!objectivesByQuest.ContainsKey(questId)) objectivesByQuest[questId] = new List<QuestObjective>();
                objectivesByQuest[questId].Add(new QuestObjective
                {
                    name = DB.GetString(reader, "name"),
                    type = DB.GetString(reader, "type"),
                    target = DB.GetString(reader, "target"),
                    count = DB.GetInt(reader, "required_count", 1),
                    label = DB.GetString(reader, "label")
                });
            }
        var rewardItemsByQuest = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        using (var connection = DB.GetConnection()) using (var reader = DB.ExecuteReader(connection, "SELECT * FROM quest_reward_items"))
            while (reader.Read())
            {
                string questId = DB.GetString(reader, "quest_id");
                if (!rewardItemsByQuest.ContainsKey(questId)) rewardItemsByQuest[questId] = new List<string>();
                rewardItemsByQuest[questId].Add(DB.GetString(reader, "gc_type"));
            }
        using (var connection = DB.GetConnection()) using (var reader = DB.ExecuteReader(connection, "SELECT * FROM quests"))
            while (reader.Read())
            {
                string questId = DB.GetString(reader, "id");
                var quest = new QuestData
                {
                    id = questId,
                    name = DB.GetString(reader, "name"),
                    description = DB.GetString(reader, "description"),
                    level = DB.GetInt(reader, "level"),
                    maxLevel = DB.GetInt(reader, "max_level"),
                    type = DB.GetString(reader, "quest_type", "quest"),
                    faction = DB.GetString(reader, "faction"),
                    npc = DB.GetString(reader, "npc"),
                    npc2 = DB.GetString(reader, "npc2"),
                    onAcceptItem = DB.GetString(reader, "on_accept_item"),
                    requiredQuest = DB.GetString(reader, "required_quest"),
                    followupQuest = DB.GetString(reader, "followup_quest"),
                    uiZoneInfo = DB.GetString(reader, "ui_zone_info"),
                    status = DB.GetString(reader, "status", "available"),
                    hash = DB.GetUInt(reader, "hash"),
                    tokenReward = DB.GetInt(reader, "token_reward"),
                    cashReward = DB.GetFloat(reader, "cash_reward"),
                    grantXPBuff = DB.GetInt(reader, "grant_xp_buff") == 1,
                    repeatable = DB.GetInt(reader, "repeatable") == 1,
                    baseClass = DB.GetString(reader, "base_class", "Quest"),
                    rewardItemGenerator = DB.GetString(reader, "reward_item_generator"),
                    numRewardItems = DB.GetInt(reader, "num_reward_items"),
                    rewardItemDescription = DB.GetString(reader, "reward_item_description"),
                    rewardIconGenerator = DB.GetString(reader, "reward_icon_generator"),
                    rewardItemsSoulBound = DB.GetInt(reader, "reward_items_soulbound") == 1,
                    rewardItemsNoSell = DB.GetInt(reader, "reward_items_nosell") == 1,
                    rewardItemsDropped = DB.GetInt(reader, "reward_items_dropped") == 1,
                    rewardItemsMemberOnly = DB.GetInt(reader, "reward_items_member_only") == 1,
                    onAcceptSoulBound = DB.GetInt(reader, "on_accept_soulbound") == 1,
                    minRepeatSeconds = DB.GetInt(reader, "min_repeat_seconds"),
                    objectives = objectivesByQuest.ContainsKey(questId) ? objectivesByQuest[questId] : new List<QuestObjective>(),
                    rewards = new QuestRewards
                    {
                        experience = DB.GetInt(reader, "reward_experience"),
                        gold = DB.GetInt(reader, "reward_gold"),
                        items = rewardItemsByQuest.ContainsKey(questId) ? rewardItemsByQuest[questId] : new List<string>()
                    }
                };
                Quests.Add(quest); if (quest.hash != 0) QuestsByHash[quest.hash] = quest;
            }
    }

    private static void LoadCheckpoints()
    {
        Checkpoints.Clear(); CheckpointDatabase.Clear();
        using (var connection = DB.GetConnection()) using (var reader = DB.ExecuteReader(connection, "SELECT * FROM checkpoints"))
            while (reader.Read())
            {
                string checkpointName = DB.GetString(reader, "name");
                var checkpoint = new CheckpointData
                {
                    id = "world.checkpoints." + checkpointName.Replace(" ", ""),
                    name = checkpointName,
                    description = DB.GetString(reader, "description"),
                    zone = DB.GetString(reader, "zone"),
                    mapId = DB.GetString(reader, "map_id"),
                    spawnPoint = DB.GetString(reader, "spawn_point"),
                    position = new PositionData { x = DB.GetFloat(reader, "pos_x"), y = DB.GetFloat(reader, "pos_y"), z = DB.GetFloat(reader, "pos_z") },
                    rotation = new PositionData { x = 0, y = 0, z = 0 },
                    order = DB.GetInt(reader, "display_order"),
                    isActive = DB.GetInt(reader, "is_active", 1) == 1,
                    levelRequirement = DB.GetInt(reader, "level_requirement", 1),
                    unlockQuest = DB.GetString(reader, "unlock_quest"),
                    image = DB.GetString(reader, "image")
                };
                Checkpoints.Add(checkpoint); CheckpointDatabase[checkpoint.id] = checkpoint;
            }
    }

    private static void LoadNPCsForZone(string zoneType, List<NPCData> target)
    {
        target.Clear();
        using (var connection = DB.GetConnection()) using (var reader = DB.ExecuteReader(connection, "SELECT * FROM npcs WHERE zone_type=@z", ("@z", zoneType)))
            while (reader.Read())
                target.Add(new NPCData
                {
                    gcType = DB.GetString(reader, "gc_type"),
                    name = DB.GetString(reader, "name"),
                    posX = DB.GetFloat(reader, "pos_x"),
                    posY = DB.GetFloat(reader, "pos_y"),
                    posZ = DB.GetFloat(reader, "pos_z"),
                    heading = DB.GetFloat(reader, "heading"),
                    hitPoints = DB.GetInt(reader, "hit_points"),
                    manaPoints = DB.GetInt(reader, "mana_points")
                });
    }

    private static void LoadZonePortals()
    {
        ZonePortals.Clear(); PortalsByZone.Clear();
        using (var connection = DB.GetConnection()) using (var reader = DB.ExecuteReader(connection, "SELECT * FROM zone_portals"))
            while (reader.Read())
            {
                var portal = new ZonePortalData
                {
                    id = DB.GetInt(reader, "id"),
                    zone = DB.GetString(reader, "zone"),
                    name = DB.GetString(reader, "name"),
                    gcType = DB.GetString(reader, "gc_type"),
                    posX = DB.GetFloat(reader, "pos_x"),
                    posY = DB.GetFloat(reader, "pos_y"),
                    posZ = DB.GetFloat(reader, "pos_z"),
                    heading = DB.GetFloat(reader, "heading"),
                    width = DB.GetInt(reader, "width"),
                    height = DB.GetInt(reader, "height"),
                    targetZone = DB.GetString(reader, "target_zone"),
                    spawnPoint = DB.GetString(reader, "spawn_point"),
                    color = DB.GetUInt(reader, "color")
                };
                ZonePortals.Add(portal); string zoneKey = portal.zone.ToLower();
                if (!PortalsByZone.ContainsKey(zoneKey)) PortalsByZone[zoneKey] = new List<ZonePortalData>();
                PortalsByZone[zoneKey].Add(portal);
            }
    }

    private static void LoadZoneWaypoints()
    {
        ZoneWaypoints.Clear(); WaypointsByZone.Clear();
        using (var connection = DB.GetConnection()) using (var reader = DB.ExecuteReader(connection, "SELECT * FROM zone_waypoints"))
            while (reader.Read())
            {
                var waypoint = new ZoneWaypointData
                {
                    id = DB.GetInt(reader, "id"),
                    zone = DB.GetString(reader, "zone"),
                    name = DB.GetString(reader, "name"),
                    posX = DB.GetFloat(reader, "pos_x"),
                    posY = DB.GetFloat(reader, "pos_y"),
                    posZ = DB.GetFloat(reader, "pos_z"),
                    heading = DB.GetFloat(reader, "heading")
                };
                ZoneWaypoints.Add(waypoint); string zoneKey = waypoint.zone.ToLower();
                if (!WaypointsByZone.ContainsKey(zoneKey)) WaypointsByZone[zoneKey] = new List<ZoneWaypointData>();
                WaypointsByZone[zoneKey].Add(waypoint);
            }
        SeedPvpArenaWaypoints();
    }

    private static void SeedPvpArenaWaypoints()
    {
        var arenas = new (string zone, float rx, float ry, float rz, float rh, float bx, float by, float bz, float bh)[]
        {
            ("DeathMatch01",  480f, 160f, 20f,  90f,  -480f, 160f, 10f, 270f),
            ("DeathMatch02",  435f, 140f, 10f,  90f,  -440f, 140f, 10f, 270f),
            ("DeathMatch03",  300f, 160f, 10f,  90f,  -300f, 160f, 10f, 270f),
            ("DeathMatch04", -140f, 305f, 40f, 180f,   260f,-250f, 40f,   0f),
        };
        foreach (var a in arenas)
        {
            AddWaypointIfAbsent(a.zone, "red_team_start",  a.rx, a.ry, a.rz, a.rh);
            AddWaypointIfAbsent(a.zone, "blue_team_start", a.bx, a.by, a.bz, a.bh);
        }
    }

    private static void AddWaypointIfAbsent(string zone, string name, float x, float y, float z, float heading)
    {
        string key = zone.ToLower();
        if (!WaypointsByZone.TryGetValue(key, out var list))
        {
            list = new List<ZoneWaypointData>();
            WaypointsByZone[key] = list;
        }
        foreach (var existing in list)
            if (existing.name != null && existing.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return;
        var wp = new ZoneWaypointData { zone = zone, name = name, posX = x, posY = y, posZ = z, heading = heading };
        list.Add(wp);
        ZoneWaypoints.Add(wp);
    }

    private static void LoadZoneCheckpoints()
    {
        ZoneCheckpoints.Clear(); CheckpointsByZone.Clear();
        using (var connection = DB.GetConnection()) using (var reader = DB.ExecuteReader(connection, "SELECT * FROM zone_checkpoints"))
            while (reader.Read())
            {
                var zoneCheckpoint = new ZoneCheckpointData
                {
                    id = DB.GetInt(reader, "id"),
                    zone = DB.GetString(reader, "zone"),
                    name = DB.GetString(reader, "name"),
                    gcType = DB.GetString(reader, "gc_type"),
                    posX = DB.GetFloat(reader, "pos_x"),
                    posY = DB.GetFloat(reader, "pos_y"),
                    posZ = DB.GetFloat(reader, "pos_z"),
                    heading = DB.GetFloat(reader, "heading")
                };
                ZoneCheckpoints.Add(zoneCheckpoint); string zoneKey = zoneCheckpoint.zone.ToLower();
                if (!CheckpointsByZone.ContainsKey(zoneKey)) CheckpointsByZone[zoneKey] = new List<ZoneCheckpointData>();
                CheckpointsByZone[zoneKey].Add(zoneCheckpoint);
            }
    }

    private static void LoadCreatures()
    {
        Creatures.Clear(); CreatureDatabase.Clear();
        using (var connection = DB.GetConnection()) using (var reader = DB.ExecuteReader(connection, "SELECT * FROM creatures"))
            while (reader.Read())
            {
                var creature = new CreatureData
                {
                    gcType = DB.GetString(reader, "gc_type"),
                    behaviourType = DB.GetString(reader, "behaviour_type"),
                    name = DB.GetString(reader, "name"),
                    faction = DB.GetString(reader, "faction"),
                    creatureType = DB.GetString(reader, "creature_type"),
                    element = DB.GetString(reader, "element"),
                    creatureDifficulty = DB.GetString(reader, "creature_difficulty"),
                    hitPoints = DB.GetInt(reader, "hit_points"),
                    manaPoints = DB.GetInt(reader, "mana_points"),
                    baseDamage = DB.GetInt(reader, "base_damage"),
                    speed = DB.GetString(reader, "speed"),
                    attackRange = DB.GetString(reader, "attack_range"),
                    attackRating = DB.GetString(reader, "attack_rating"),
                    defenseRating = DB.GetString(reader, "defense_rating"),
                    criticalChance = DB.GetString(reader, "critical_chance"),
                    damageMod = DB.GetString(reader, "damage_mod"),
                    divineResist = DB.GetString(reader, "divine_resist"),
                    fireResist = DB.GetString(reader, "fire_resist"),
                    iceResist = DB.GetString(reader, "ice_resist"),
                    poisonResist = DB.GetString(reader, "poison_resist"),
                    shadowResist = DB.GetString(reader, "shadow_resist")
                };
                Creatures.Add(creature); CreatureDatabase[creature.gcType.ToLower()] = creature;
            }
        using (var connection = DB.GetConnection()) using (var reader = DB.ExecuteReader(connection, "SELECT * FROM creature_manipulators"))
            while (reader.Read())
            {
                string creatureGcType = DB.GetString(reader, "creature_gc_type").ToLower();
                if (CreatureDatabase.TryGetValue(creatureGcType, out var creature))
                {
                    if (creature.manipulators == null) creature.manipulators = new Dictionary<string, ManipulatorData>();
                    string slot = DB.GetString(reader, "slot");
                    var manipulatorData = new ManipulatorData { gcType = DB.GetString(reader, "gc_type") };
                    manipulatorData.properties["Equipable"] = DB.GetString(reader, "equipable"); manipulatorData.properties["SlotType"] = DB.GetString(reader, "slot_type");
                    manipulatorData.properties["WeaponClass"] = DB.GetString(reader, "weapon_class"); manipulatorData.properties["Range"] = DB.GetString(reader, "weapon_range");
                    manipulatorData.properties["CoolDown"] = DB.GetString(reader, "cooldown"); manipulatorData.properties["Damage"] = DB.GetString(reader, "damage");
                    creature.manipulators[slot] = manipulatorData;
                }
            }
        Debug.Log($"Loaded {Creatures.Count} creatures");
    }

    private static void LoadSummons()
    {
        Summons.Clear(); SummonDatabase.Clear();
        using (var connection = DB.GetConnection()) using (var reader = DB.ExecuteReader(connection, "SELECT * FROM summons"))
            while (reader.Read())
            {
                var summon = new SummonData
                {
                    gcType = DB.GetString(reader, "gc_type"),
                    behaviourType = DB.GetString(reader, "behaviour_type"),
                    name = DB.GetString(reader, "name"),
                    hitPoints = DB.GetInt(reader, "hit_points"),
                    manaPoints = DB.GetInt(reader, "mana_points"),
                    summonType = DB.GetString(reader, "summon_type"),
                    element = DB.GetString(reader, "element"),
                    description = DB.GetString(reader, "description")
                };
                Summons.Add(summon); SummonDatabase[summon.gcType.ToLower()] = summon;
            }
    }

    private static void LoadGeneralItems()
    {
        GeneralItemDatabase.Clear();
        var itemsByCategory = new Dictionary<string, List<GeneralItemData>> {
            {"quest_items",QuestItems},{"relics",Relics},{"rings",Rings},{"amulets",Amulets},
            {"potions",Potions},{"skillbooks",Skillbooks},{"keys",Keys},{"scrolls",Scrolls},
            {"dungeon_items",DungeonItems},{"vouchers",Vouchers},{"item_packs",ItemPacks},{"consumables",Consumables} };
        foreach (var categoryEntry in itemsByCategory) categoryEntry.Value.Clear();
        using (var connection = DB.GetConnection()) using (var reader = DB.ExecuteReader(connection, "SELECT * FROM items"))
            while (reader.Read())
            {
                var item = new GeneralItemData
                {
                    gcType = DB.GetString(reader, "gc_type"),
                    baseType = DB.GetString(reader, "base_type"),
                    Label = DB.GetString(reader, "label"),
                    InventoryIcon = DB.GetString(reader, "inventory_icon"),
                    GroundObject = DB.GetString(reader, "ground_object"),
                    Stackable = DB.GetInt(reader, "stackable") == 1,
                    InventoryWidth = DB.GetInt(reader, "inventory_width", 1),
                    InventoryHeight = DB.GetInt(reader, "inventory_height", 1),
                    modCount = DB.GetInt(reader, "mod_count", 1),
                    DropLevel = DB.GetInt(reader, "drop_level"),
                    LevelReq = DB.GetInt(reader, "level_req")
                };
                string category = DB.GetString(reader, "category");
                if (itemsByCategory.ContainsKey(category)) itemsByCategory[category].Add(item);
                if (!string.IsNullOrEmpty(item.gcType)) GeneralItemDatabase[item.gcType.ToLower()] = item;
            }
    }

    private static void LoadMerchants()
    {
        Merchants.Clear(); MerchantsByNpc.Clear();
        var merchantsById = new Dictionary<int, MerchantData>();
        using (var connection = DB.GetConnection()) using (var reader = DB.ExecuteReader(connection, "SELECT * FROM merchants"))
            while (reader.Read())
            {
                int merchantId = DB.GetInt(reader, "id");
                var merchantData = new MerchantData
                {
                    npcGcType = DB.GetString(reader, "npc_gc_type"),
                    merchantGcType = DB.GetString(reader, "merchant_gc_type"),
                    inventories = new List<MerchantInventoryData>()
                };
                merchantsById[merchantId] = merchantData; Merchants.Add(merchantData); MerchantsByNpc[merchantData.npcGcType] = merchantData;
            }
        using (var connection = DB.GetConnection()) using (var reader = DB.ExecuteReader(connection, "SELECT * FROM merchant_inventories ORDER BY merchant_id,inv_id"))
            while (reader.Read())
            {
                int merchantId = DB.GetInt(reader, "merchant_id");
                if (!merchantsById.ContainsKey(merchantId)) continue;
                merchantsById[merchantId].inventories.Add(new MerchantInventoryData
                {
                    name = DB.GetString(reader, "name"),
                    gcType = DB.GetString(reader, "gc_type"),
                    id = DB.GetInt(reader, "inv_id"),
                    label = DB.GetString(reader, "label"),
                    staticContents = DB.GetInt(reader, "static_contents", 1) == 1,
                    autoGenerateItems = DB.GetInt(reader, "auto_generate") == 1,
                    itemGenerator = DB.GetString(reader, "item_generator"),
                    minItemLevel = DB.GetInt(reader, "min_item_level"),
                    maxItemLevel = DB.GetInt(reader, "max_item_level"),
                    regenerateIntervalSeconds = DB.GetInt(reader, "regen_seconds"),
                    width = DB.GetInt(reader, "width", 10),
                    height = DB.GetInt(reader, "height", 10),
                    items = new List<MerchantItemData>()
                });
            }
        using (var connection = DB.GetConnection()) using (var reader = DB.ExecuteReader(connection, "SELECT * FROM merchant_inventory_items ORDER BY merchant_id,inv_id"))
            while (reader.Read())
            {
                int merchantId = DB.GetInt(reader, "merchant_id"); int inventoryId = DB.GetInt(reader, "inv_id");
                if (!merchantsById.ContainsKey(merchantId)) continue;
                var inventory = merchantsById[merchantId].inventories.Find(merchantInventory => merchantInventory.id == inventoryId); if (inventory == null) continue;
                inventory.items.Add(new MerchantItemData
                {
                    gcType = DB.GetString(reader, "item_gc_type"),
                    inventoryX = DB.GetInt(reader, "inventory_x"),
                    inventoryY = DB.GetInt(reader, "inventory_y"),
                    id = DB.GetInt(reader, "item_slot_id"),
                    quantity = DB.GetInt(reader, "quantity", 1)
                });
            }
        Debug.Log($"Loaded {Merchants.Count} merchants");
    }

    private static void LoadDungeonSpawns()
    {
        DungeonSpawns.Clear();
        using (var connection = DB.GetConnection()) using (var reader = DB.ExecuteReader(connection, "SELECT * FROM dungeon_spawns"))
            while (reader.Read())
            {
                var dungeonSpawn = new DungeonSpawnData
                {
                    zoneName = DB.GetString(reader, "zone_name"),
                    gcType = DB.GetString(reader, "gc_type"),
                    posX = DB.GetFloat(reader, "pos_x"),
                    posY = DB.GetFloat(reader, "pos_y"),
                    posZ = DB.GetFloat(reader, "pos_z"),
                    heading = DB.GetFloat(reader, "heading")
                };
                if (!DungeonSpawns.ContainsKey(dungeonSpawn.zoneName)) DungeonSpawns[dungeonSpawn.zoneName] = new List<DungeonSpawnData>();
                DungeonSpawns[dungeonSpawn.zoneName].Add(dungeonSpawn);
            }
    }

    private static void LoadQuestKillDrops()
    {
        QuestKillDropsByMonster.Clear();
        try
        {
            using (var connection = DB.GetConnection())
            using (var reader = DB.ExecuteReader(connection,
                "SELECT quest_id, monster_type, item_gc_type, chance FROM quest_kill_drops"))
            {
                while (reader.Read())
                {
                    string monsterType = DB.GetString(reader, "monster_type");
                    if (string.IsNullOrEmpty(monsterType)) continue;
                    string monsterKey = monsterType.ToLowerInvariant();

                    if (!QuestKillDropsByMonster.TryGetValue(monsterKey, out var dropRules))
                    {
                        dropRules = new List<QuestKillDropEntry>();
                        QuestKillDropsByMonster[monsterKey] = dropRules;
                    }
                    dropRules.Add(new QuestKillDropEntry
                    {
                        QuestId = DB.GetString(reader, "quest_id"),
                        ItemGcType = DB.GetString(reader, "item_gc_type"),
                        Chance = DB.GetInt(reader, "chance", 100),
                    });
                }
            }
            int totalRules = 0;
            foreach (var dropRuleEntry in QuestKillDropsByMonster) totalRules += dropRuleEntry.Value.Count;
            Debug.LogError($"[QUEST-KILL-DROPS] rules={totalRules} monsters={QuestKillDropsByMonster.Count}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[QUEST-KILL-DROPS] state=failed table=quest_kill_drops message='{ex.Message}'");
            throw;
        }
    }

    private static void LoadEquipment(string table, List<ItemData> target)
    {
        target.Clear();
        using (var connection = DB.GetConnection()) using (var reader = DB.ExecuteReader(connection, $"SELECT * FROM [{table}]"))
            while (reader.Read())
            {
                var item = new ItemData
                {
                    gcType = DB.GetString(reader, "gc_type"),
                    name = DB.GetString(reader, "label"),
                    description = DB.GetString(reader, "description"),
                    goldValue = DB.GetFloat(reader, "gold_value"),
                    defenseRating = DB.GetFloat(reader, "defense_rating"),
                    damage = DB.GetFloat(reader, "damage"),
                    range = DB.GetInt(reader, "weapon_range"),
                    cooldown = DB.GetFloat(reader, "cooldown", 0f),
                    weaponSpeed = DB.GetFloat(reader, "weapon_speed", DB.GetFloat(reader, "weaponSpeed", DB.GetFloat(reader, "WeaponSpeed", 0f))),
                    slotType = DB.GetString(reader, "slot_type"),
                    weaponClass = DB.GetString(reader, "weapon_class"),
                    inventoryWidth = DB.GetInt(reader, "inventory_width", 1),
                    inventoryHeight = DB.GetInt(reader, "inventory_height", 1),
                    inventoryIcon = DB.GetString(reader, "inventory_icon"),
                    groundObject = DB.GetString(reader, "ground_object"),
                    equipable = DB.GetInt(reader, "equipable", 1) == 1,
                    modCount = DB.GetInt(reader, "mod_count", 1)
                };
                target.Add(item); ItemDatabase[item.gcType.ToLower()] = item;
            }
    }


    public static int GetRingModSlotCount(string gcType) { var generalItem = FindGeneralItem(gcType); return (generalItem != null && generalItem.modCount > 0) ? generalItem.modCount : 1; }
    public static int GetAmuletModSlotCount(string gcType) { var generalItem = FindGeneralItem(gcType); return (generalItem != null && generalItem.modCount > 0) ? generalItem.modCount : 1; }
    public static string GetRingModifierClass(string gcType)
    {
        string lowerGcType = gcType.ToLower(); if (lowerGcType.Contains("mythic")) return "RingModPAL.Mythic.Mod1";
        if (lowerGcType.Contains("rare")) return "RingModPAL.Rare.Mod1"; if (lowerGcType.Contains("superior")) return "RingModPAL.Superior.Mod1";
        if (lowerGcType.Contains("magic")) return "RingModPAL.Magic.Mod1"; return "RingModPAL.Mod1";
    }
    public static string GetAmuletModifierClass(string gcType)
    {
        string lowerGcType = gcType.ToLower(); if (lowerGcType.Contains("mythic")) return "AmuletModPAL.Mythic.Mod1";
        if (lowerGcType.Contains("rare")) return "AmuletModPAL.Rare.Mod1"; if (lowerGcType.Contains("superior")) return "AmuletModPAL.Superior.Mod1";
        if (lowerGcType.Contains("magic")) return "AmuletModPAL.Magic.Mod1"; return "AmuletModPAL.Mod1";
    }

    public static GeneralItemData FindGeneralItem(string gcType)
    {
        if (string.IsNullOrEmpty(gcType)) return null;
        if (GeneralItemDatabase.TryGetValue(gcType, out var generalItem)) return generalItem;
        if (gcType.StartsWith("items.pal.", StringComparison.OrdinalIgnoreCase))
            if (GeneralItemDatabase.TryGetValue(gcType.Substring(10), out generalItem)) return generalItem;
        return null;
    }

    public static ItemData FindItem(string gcType)
    {
        if (string.IsNullOrEmpty(gcType)) return null;
        string itemKey = gcType.ToLower();
        if (ItemDatabase.TryGetValue(itemKey, out ItemData item)) return item;
        if (itemKey.StartsWith("items.pal.")) if (ItemDatabase.TryGetValue(itemKey.Substring(10), out item)) return item;
        return null;
    }

    public static CreatureData FindCreature(string gcType)
    {
        if (string.IsNullOrEmpty(gcType)) return null;
        CreatureDatabase.TryGetValue(gcType.ToLower(), out var creature); return creature;
    }

    public static List<CreatureData> GetCreaturesByFaction(string faction)
    {
        var matchingCreatures = new List<CreatureData>(); foreach (var creature in Creatures)
            if (creature.faction != null && creature.faction.Equals(faction, StringComparison.OrdinalIgnoreCase)) matchingCreatures.Add(creature); return matchingCreatures;
    }
    public static List<CreatureData> GetCreaturesByTier(string tier)
    {
        var matchingCreatures = new List<CreatureData>(); foreach (var creature in Creatures)
            if (creature.creatureDifficulty != null && creature.creatureDifficulty.Equals(tier, StringComparison.OrdinalIgnoreCase)) matchingCreatures.Add(creature); return matchingCreatures;
    }
    public static List<CreatureData> GetCreaturesByElement(string element)
    {
        var matchingCreatures = new List<CreatureData>(); foreach (var creature in Creatures)
            if (creature.element != null && creature.element.Equals(element, StringComparison.OrdinalIgnoreCase)) matchingCreatures.Add(creature); return matchingCreatures;
    }

    private static void BuildEquipmentMappings()
    {
        Helmets.Clear(); Armors.Clear(); Gloves.Clear(); Boots.Clear(); MeleeWeapons.Clear(); RangedWeapons.Clear();
        foreach (var item in AllArmor)
        {
            string category = GetItemCategory(item.gcType); string slotType = item.slotType ?? "0"; string lowerGcType = item.gcType.ToLower();
            Dictionary<string, List<ItemData>> target = null;
            if (slotType == "1" || lowerGcType.Contains("helm") || lowerGcType.Contains("hat") || lowerGcType.Contains("cap")) target = Helmets;
            else if (slotType == "6" || lowerGcType.Contains("armor") || lowerGcType.Contains("chest") || lowerGcType.Contains("body") || lowerGcType.Contains("vest")) target = Armors;
            else if (slotType == "3" || lowerGcType.Contains("glove") || lowerGcType.Contains("hand") || lowerGcType.Contains("gauntlet")) target = Gloves;
            else if (slotType == "4" || lowerGcType.Contains("boot") || lowerGcType.Contains("foot") || lowerGcType.Contains("feet")) target = Boots;
            if (target != null) { if (!target.ContainsKey(category)) target[category] = new List<ItemData>(); target[category].Add(item); }
        }
        foreach (var item in AllWeapons)
        {
            string category = GetItemCategory(item.gcType); string lowerGcType = item.gcType.ToLower();
            if (item.weaponClass?.Contains("RANGED") == true || lowerGcType.Contains("gun") || lowerGcType.Contains("bow") || lowerGcType.Contains("crossbow"))
            {
                if (!RangedWeapons.ContainsKey(category)) RangedWeapons[category] = new List<ItemData>(); RangedWeapons[category].Add(item);
            }
            else { if (!MeleeWeapons.ContainsKey(category)) MeleeWeapons[category] = new List<ItemData>(); MeleeWeapons[category].Add(item); }
        }
    }
    private static string GetItemCategory(string gcType)
    {
        if (string.IsNullOrEmpty(gcType)) return "unknown"; string family = gcType.ToLower().Split('.')[0];
        if (family.EndsWith("pal")) family = family.Substring(0, family.Length - 3);
        while (family.Length > 0 && char.IsDigit(family[family.Length - 1])) family = family.Substring(0, family.Length - 1); return family;
    }

    public static int GetRingModifierCount(string gcType)
    {
        if (string.IsNullOrEmpty(gcType)) return 0;
        string baseKey = gcType.ToLower();
        int count = 0;
        for (int modIndex = 1; modIndex <= 10; modIndex++)
            if (GeneralItemDatabase.ContainsKey($"{baseKey}.mod{modIndex}")) count++;
        return count;
    }

    public static List<string> GetRingModifiers(string gcType)
    {
        var modifiers = new List<string>();
        if (string.IsNullOrEmpty(gcType)) { modifiers.Add("RingModPAL.Mod1"); return modifiers; }
        string baseKey = gcType.ToLowerInvariant();
        if (baseKey.StartsWith("items.pal.")) baseKey = baseKey.Substring("items.pal.".Length);
        for (int modIndex = 1; modIndex <= 10; modIndex++)
        {
            string modKey = $"{baseKey}.mod{modIndex}";
            if (GeneralItemDatabase.ContainsKey(modKey)) modifiers.Add(modKey);
        }
        if (modifiers.Count == 0) modifiers.Add("ringmodpal.mod1");
        return modifiers;
    }

    public static List<string> GetAmuletModifiers(string gcType)
    {
        var modifiers = new List<string>();
        if (string.IsNullOrEmpty(gcType)) { modifiers.Add("AmuletModPAL.Mod1"); return modifiers; }
        string baseKey = gcType.ToLowerInvariant();
        if (baseKey.StartsWith("items.pal.")) baseKey = baseKey.Substring("items.pal.".Length);
        for (int modIndex = 1; modIndex <= 10; modIndex++)
        {
            string modKey = $"{baseKey}.mod{modIndex}";
            if (GeneralItemDatabase.ContainsKey(modKey)) modifiers.Add(modKey);
        }
        if (modifiers.Count == 0) modifiers.Add("amuletmodpal.mod1");
        return modifiers;
    }

    public static int GetAmuletModifierCount(string gcType)
    {
        if (string.IsNullOrEmpty(gcType)) return 0;
        string baseKey = gcType.ToLower();
        int count = 0;
        for (int modIndex = 1; modIndex <= 10; modIndex++)
            if (GeneralItemDatabase.ContainsKey($"{baseKey}.mod{modIndex}")) count++;
        return count;
    }

    public static uint ComputeDJB2Hash(string value)
    {
        uint hash = 5381;
        foreach (char character in value.ToLowerInvariant()) hash = hash * 33 + character;
        return hash;
    }

    public static List<ZoneCheckpointData> GetCheckpointsForZone(string zoneName)
    {
        string key = zoneName.ToLower();
        if (CheckpointsByZone.TryGetValue(key, out var checkpoints)) return checkpoints;
        return new List<ZoneCheckpointData>();
    }

    public static ZoneCheckpointData GetCheckpointByGcType(string gcType)
    {
        if (string.IsNullOrEmpty(gcType) || CheckpointsByZone == null) return null;
        string searchKey = gcType.ToLower();
        foreach (var checkpointList in CheckpointsByZone.Values)
            foreach (var checkpoint in checkpointList)
                if (checkpoint.gcType.ToLower() == searchKey) return checkpoint;
        return null;
    }

    public static CheckpointData FindCheckpoint(string checkpointId)
    {
        if (string.IsNullOrEmpty(checkpointId)) return null;
        CheckpointDatabase.TryGetValue(checkpointId, out var cp);
        return cp;
    }

    public static List<ZonePortalData> GetPortalsForZone(string zoneName)
    {
        string key = zoneName.ToLower();
        if (PortalsByZone.TryGetValue(key, out var portals)) return portals;
        return new List<ZonePortalData>();
    }

    public static List<ZoneWaypointData> GetWaypointsForZone(string zoneName)
    {
        string key = zoneName.ToLower();
        if (WaypointsByZone.TryGetValue(key, out var waypoints)) return waypoints;
        return new List<ZoneWaypointData>();
    }

    public static SummonData FindSummon(string gcType)
    {
        if (string.IsNullOrEmpty(gcType)) return null;
        SummonDatabase.TryGetValue(gcType.ToLower(), out var summon);
        return summon;
    }

}

[Serializable]
public class GeneralItemData
{
    public string gcType; public string baseType; public string Label;
    public string InventoryIcon; public string GroundObject; public bool Stackable;
    public int InventoryWidth = 1; public int InventoryHeight = 1; public int DropLevel; public int LevelReq; public int modCount;
}

[Serializable]
public class ItemData
{
    public string gcType; public string name; public string description;
    public float goldValue; public float defenseRating; public float damage; public int range; public float cooldown; public float weaponSpeed;
    public string slotType; public string weaponClass; public int inventoryWidth; public int inventoryHeight;
    public string inventoryIcon; public string groundObject; public bool equipable; public int modCount = 3; public int requiredLevel = 1;
    public static int GetRequiredLevelFromGCClass(string gcClass)
    {
        if (string.IsNullOrEmpty(gcClass)) return 1;
        int palIdx = gcClass.LastIndexOf("PAL", System.StringComparison.OrdinalIgnoreCase);
        if (palIdx > 0)
        {
            int numEnd = palIdx;
            int numStart = numEnd - 1;
            while (numStart >= 0 && char.IsDigit(gcClass[numStart]))
                numStart--;
            numStart++;
            if (numStart < numEnd)
            {
                string tierStr = gcClass.Substring(numStart, numEnd - numStart);
                if (int.TryParse(tierStr, out int palTier))
                {
                    if (palTier <= 1) return 1;
                    return (palTier - 1) * 10 + 1;
                }
            }
        }
        return 1;
    }
}

[Serializable] public class SkillData { public string id; public string name; public string description; public int level; public int experience; public int maxLevel; public SkillAttributes attributes; }
[Serializable] public class SkillAttributes { public int strength; public int dexterity; public int vitality; public int intelligence; public int wisdom; public int spirit; public int perception; public int agility; }
[Serializable] public class QuestData { public string id; public string name; public string description; public int level; public int maxLevel; public string type; public string faction; public string npc; public string npc2; public string onAcceptItem; public string baseClass; public int tokenReward; public float cashReward; public bool grantXPBuff; public bool repeatable; public string requiredQuest; public string followupQuest; public string uiZoneInfo; public List<QuestObjective> objectives; public QuestRewards rewards; public string status; public uint hash; public string rewardItemGenerator; public int numRewardItems; public string rewardItemDescription; public string rewardIconGenerator; public bool rewardItemsSoulBound; public bool rewardItemsNoSell; public bool rewardItemsDropped; public bool rewardItemsMemberOnly; public bool onAcceptSoulBound; public int minRepeatSeconds; }
[Serializable] public class QuestObjective { public string type; public string target; public int count; public bool completed; public string label; public string name; }
[Serializable] public class QuestRewards { public int experience; public int gold; public List<string> items; }
[Serializable] public class CheckpointData { public string id; public string name; public string description; public PositionData position; public PositionData rotation; public string mapId; public string zone; public int order; public string spawnPoint; public bool isActive; public int levelRequirement; public string unlockQuest; public string image; }
[Serializable] public class NPCData { public string name; public string gcType; public float posX; public float posY; public float posZ; public float heading; public int hitPoints; public int manaPoints; }
[Serializable] public class PositionData { public float x; public float y; public float z; }

public static class MonsterHealthTable
{
    private static readonly Dictionary<string, float> DifficultyModifiers = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase) { { "FODDER", 0.5f }, { "RECRUIT", 1f }, { "VETERAN", 2f }, { "WARMONGER", 2.5f }, { "CHAMPION", 4f }, { "HERO", 7f }, { "DUNGEON_BOSS", 8f }, { "BOSS", 8f } };
    public static float GetBaseHP(int level) { level = Math.Max(1, Math.Min(110, level)); return DungeonRunners.Data.GCDatabase.Instance.GetCurveValue("MonsterHealth", level); }
    public static float GetDifficultyModifier(string difficulty) { if (string.IsNullOrEmpty(difficulty)) return 1f; return DifficultyModifiers.TryGetValue(difficulty, out float modifier) ? modifier : 1f; }
    public static int CalculateHP(int level, string diff) { return CalculateHP(level, diff, 1f); }
    public static int CalculateHP(int level, string diff, float mod) { return CalculateHP(level, GetDifficultyModifier(diff), mod); }
    public static int CalculateHP(int level, float difficulty, float mod) { int baseF32 = DungeonRunners.Data.GCDatabase.Instance.GetCurveValueFixed32("MonsterHealth", Math.Max(1, Math.Min(110, level))); int diffF32 = (int)(Math.Max(0f, difficulty) * 256f); int modF32 = (int)(Math.Max(0f, mod) * 256f); long hpF32 = ((long)baseF32 * modF32) >> 8; hpF32 = (hpF32 * diffF32) >> 8; return Math.Max(1, (int)(hpF32 >> 8)); }
    public static uint CalculateHPWire(int level, string diff, float mod = 1f) { return (uint)(CalculateHP(level, diff, mod) * 256); }
    public static uint CalculateHPWire(int level, float difficulty, float mod = 1f) { return (uint)(CalculateHP(level, difficulty, mod) * 256); }
}

[Serializable]
public class CreatureData
{
    public string gcType; public string behaviourType; public string name; public string faction; public string creatureType;
    public string element; public string creatureDifficulty; public string tier => creatureDifficulty;
    public string speed; public string attackRange; public int hitPoints; public int manaPoints; public int baseDamage; public float maxHealth = 1f;
    public string attackRating; public string defenseRating; public string damageMod; public string criticalChance;
    public string divineResist; public string fireResist; public string iceResist; public string poisonResist; public string shadowResist;
    [NonSerialized] public Dictionary<string, ManipulatorData> manipulators;
    public float GetFloat(string value, float fallback = 0f) { if (string.IsNullOrEmpty(value)) return fallback; return float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float result) ? result : fallback; }
    public float AttackRatingF => GetFloat(attackRating, 1f); public float DefenseRatingF => GetFloat(defenseRating, 1f);
    public float DamageModF => GetFloat(damageMod, 1f); public float CritChanceF => GetFloat(criticalChance, 0f);
    public float DivineResistF => GetFloat(divineResist, 0f); public float FireResistF => GetFloat(fireResist, 0f);
    public float IceResistF => GetFloat(iceResist, 0f); public float PoisonResistF => GetFloat(poisonResist, 0f);
    public float ShadowResistF => GetFloat(shadowResist, 0f);
}

[Serializable] public class ManipulatorData { public string gcType; public Dictionary<string, string> properties; public ManipulatorData() { properties = new Dictionary<string, string>(); } }
[Serializable] public class SummonData { public string gcType; public string behaviourType; public string name; public int hitPoints; public int manaPoints; public string summonType; public string element; public string description; }
[Serializable] public class ZonePortalData { public int id; public string zone; public string name; public string gcType; public float posX; public float posY; public float posZ; public float heading; public int width; public int height; public string targetZone; public string spawnPoint; public uint color; }
[Serializable] public class ZoneWaypointData { public int id; public string zone; public string name; public float posX; public float posY; public float posZ; public float heading; }
[Serializable] public class ZoneCheckpointData { public int id; public string zone; public string name; public string gcType; public float posX; public float posY; public float posZ; public float heading; }
[Serializable] public class DRClassChildGroup { public string name; public DRChildEntity[] entities; public string gcType; }
[Serializable] public class DRChildEntity { public string @extends; public Dictionary<string, string> properties; public Dictionary<string, DRChildGroup> children; }
[Serializable] public class DRChildGroup { public DRChildEntity[] entities; }
