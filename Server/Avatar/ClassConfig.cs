using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DungeonRunners.Engine;

namespace DungeonRunners.Data
{
    [Serializable]
    public class StartingInventoryItem
    {
        public string gcClass;
        public byte x;
        public byte y;
        public int count = 1;
    }

    [Serializable]
    public class ClassDefinition
    {
        public string displayName;
        public string description;
        public StartingEquipment startingEquipment;
        public List<string> startingSkills = new List<string>();
        public List<StartingInventoryItem> startingInventory = new List<StartingInventoryItem>();
    }

    [Serializable]
    public class StartingEquipment
    {
        public string weapon;
        public string armor;
        public string helmet;
        public string gloves;
        public string boots;
        public string shoulders;
        public string shield;
        public string ring1;
        public string ring2;
        public string amulet;
        public Dictionary<string, int> slotRarity = new Dictionary<string, int>();
        public Dictionary<string, int> slotLevel = new Dictionary<string, int>();
    }

    [Serializable]
    public class ClassConfigData
    {
        public Dictionary<string, ClassDefinition> classes = new Dictionary<string, ClassDefinition>();
    }

    [Serializable]
    public class SavedInventoryItem
    {
        public string gcClass;
        public byte x;
        public byte y;
        public int count = 1;
        public uint buyPrice = 0;
        public int rarity = 0;
        public int storedLevel = -1;
        public byte containerId = 0x0B;
    }

    [Serializable]
    public class SavedCharacter
    {
        public uint id;
        public string name;
        public uint accountId;
        public string accountName;
        public string className;
        public byte level;
        public uint experience;
        public uint gold;
        public StartingEquipment equipment;
        public List<string> skills = new List<string>();
        public List<SavedInventoryItem> inventory = new List<SavedInventoryItem>();
        public Vector3 position;
        public int zoneId;
        public int worldId;
        public string currentZoneName;
        public string avatarClass;
        public byte skin;
        public byte face;
        public byte faceFeature;
        public byte hair;
        public byte hairColor;

        public List<SavedQuest> activeQuests = new List<SavedQuest>();
        public List<string> completedQuests = new List<string>();
        public List<string> unlockedCheckpoints = new List<string>();

        public uint currentHP = 0;
        public uint currentMana = 0;

        public int maxHP = 0;
        public int maxMana = 0;
        public int statStrength = 0;
        public int statAgility = 0;
        public int statIntellect = 0;
        public int statEndurance = 0;
        public int lastRespecTime = 0;
        public int respecCount = 0;
        public int pvpWins = 0;
        public int pvpRating = 0;

        public string tpZone = "";
        public int tpZoneId = 0;
        public string tpTargetZone = "";
        public float tpPosX = 0, tpPosY = 0, tpPosZ = 0;

        public uint posseId = 0;
        public string posseName = "";
        public int posseJoinCooldown = 0;
        public int posseRankId = 1;

        public List<SkillLevelEntry> skillLevels = new List<SkillLevelEntry>();

        public List<HotbarSlotEntry> hotbarSlots = new List<HotbarSlotEntry>();

        public int GetSkillLevel(string skillGcClass)
        {
            for (int skillIndex = 0; skillIndex < skillLevels.Count; skillIndex++)
                if (string.Equals(skillLevels[skillIndex].skill, skillGcClass, StringComparison.OrdinalIgnoreCase))
                    return skillLevels[skillIndex].level;
            return 1;
        }

        public void SetSkillLevel(string skillGcClass, int level)
        {
            for (int skillIndex = 0; skillIndex < skillLevels.Count; skillIndex++)
            {
                if (string.Equals(skillLevels[skillIndex].skill, skillGcClass, StringComparison.OrdinalIgnoreCase))
                {
                    skillLevels[skillIndex] = new SkillLevelEntry { skill = skillGcClass, level = level };
                    return;
                }
            }
            skillLevels.Add(new SkillLevelEntry { skill = skillGcClass, level = level });
        }
    }

    public static class SavedCharacterLevel
    {
        public static int ResolveRuntimeLevel(SavedCharacter character)
        {
            return ResolveRuntimeLevel(character != null ? character.level : 1);
        }

        public static int ResolveRuntimeLevel(int persistedLevel)
        {
            int maxLevel = 100;
            try
            {
                if (GCDatabase.Instance != null)
                    maxLevel = Math.Max(1, GCDatabase.Instance.GetKnobInt("MaxLevel", 100));
            }
            catch
            {
                maxLevel = 100;
            }

            int persisted = Math.Max(0, persistedLevel);
            return Math.Max(1, Math.Min(maxLevel, persisted + 1));
        }

        public static byte ResolvePersistedLevel(int runtimeLevel)
        {
            int maxLevel = 100;
            try
            {
                if (GCDatabase.Instance != null)
                    maxLevel = Math.Max(1, GCDatabase.Instance.GetKnobInt("MaxLevel", 100));
            }
            catch
            {
                maxLevel = 100;
            }

            int persisted = Math.Max(0, Math.Min(maxLevel, runtimeLevel) - 1);
            return (byte)Math.Max(0, Math.Min(255, persisted));
        }
    }

    [Serializable]
    public class SkillLevelEntry
    {
        public string skill;
        public int level = 1;
    }

    [Serializable]
    public class HotbarSlotEntry
    {
        public uint slot;
        public string skill;
    }

    [Serializable]
    public class SavedQuest
    {
        public string questId;
        public string questGiverId;
        public string acceptedAt;
        public List<SavedQuestObjective> objectives = new List<SavedQuestObjective>();
    }

    [Serializable]
    public class SavedQuestObjective
    {
        public string objectiveName;
        public string type;
        public string target;
        public string label;
        public int required;
        public int current;
    }

    public static class ClassConfig
    {
        private static ClassConfigData _classConfig;
        private static bool _isLoaded = false;

        public static void Load()
        {
            if (_isLoaded) return;

            _classConfig = new ClassConfigData();

            try
            {
                _classConfig.classes = new Dictionary<string, ClassDefinition>();
                using (var connection = DungeonRunners.Database.GameDatabase.GetConnection())
                {
                    using (var reader = DungeonRunners.Database.GameDatabase.ExecuteReader(connection, "SELECT * FROM class_definitions"))
                    {
                        while (reader.Read())
                        {
                            string className = DungeonRunners.Database.GameDatabase.GetString(reader, "class_name");
                            var classDefinition = new ClassDefinition
                            {
                                displayName = DungeonRunners.Database.GameDatabase.GetString(reader, "display_name"),
                                description = DungeonRunners.Database.GameDatabase.GetString(reader, "description"),
                                startingEquipment = new StartingEquipment
                                {
                                    weapon = DungeonRunners.Database.GameDatabase.GetString(reader, "weapon"),
                                    armor = DungeonRunners.Database.GameDatabase.GetString(reader, "armor"),
                                    helmet = DungeonRunners.Database.GameDatabase.GetString(reader, "helmet"),
                                    gloves = DungeonRunners.Database.GameDatabase.GetString(reader, "gloves"),
                                    boots = DungeonRunners.Database.GameDatabase.GetString(reader, "boots"),
                                    shoulders = DungeonRunners.Database.GameDatabase.GetString(reader, "shoulders"),
                                    shield = DungeonRunners.Database.GameDatabase.GetString(reader, "shield"),
                                    ring1 = DungeonRunners.Database.GameDatabase.GetString(reader, "ring1"),
                                    ring2 = DungeonRunners.Database.GameDatabase.GetString(reader, "ring2"),
                                    amulet = DungeonRunners.Database.GameDatabase.GetString(reader, "amulet"),
                                    slotLevel = StartingEquipmentLevels(className)
                                },
                                startingSkills = new List<string>(),
                                startingInventory = new List<StartingInventoryItem>
                                {
                                    new StartingInventoryItem { gcClass = "items.consumables.Consumable_TownPortal", x = 0, y = 0 },
                                    new StartingInventoryItem { gcClass = "PotionPAL.HealthPotion_Noob", x = 1, y = 0, count = 20 },
                                    new StartingInventoryItem { gcClass = "PotionPAL.ManaPotion_Noob", x = 2, y = 0, count = 20 }
                                }
                            };
                            _classConfig.classes[className] = classDefinition;
                        }
                    }
                    using (var reader = DungeonRunners.Database.GameDatabase.ExecuteReader(connection, "SELECT class_name, skill_gc_type FROM class_starting_skills"))
                    {
                        while (reader.Read())
                        {
                            string className = reader.GetString(0);
                            string skillGcType = reader.GetString(1);
                            if (_classConfig.classes.TryGetValue(className, out var classDefinition) &&
                                !classDefinition.startingSkills.Any(startingSkill => string.Equals(startingSkill, skillGcType, StringComparison.OrdinalIgnoreCase)))
                                classDefinition.startingSkills.Add(skillGcType);
                        }
                    }
                }
                if (_classConfig.classes.Count == 0)
                    throw new InvalidDataException("class_definitions table returned zero rows");

                ApplyStartingSkillsFromGcExport();
                Debug.LogError($"[CLASS-CONFIG] loaded={_classConfig.classes.Count} source=sqlite+authored-gc-starting-skills");
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("ClassConfig dfc class tables/export failed; refusing non-authored class fallback", ex);
            }

            _isLoaded = true;
        }

        private static void ApplyStartingSkillsFromGcExport()
        {
            string gcDir = ResolveBundledGcDirectory();
            var packageCatalog = PackageCatalog.Instance;
            if (!packageCatalog.IsLoaded)
                packageCatalog.LoadFromAssets();
            if ((string.IsNullOrEmpty(gcDir) || !Directory.Exists(gcDir)) && !packageCatalog.IsLoaded)
                throw new DirectoryNotFoundException($"Bundled GC directory not found: {gcDir}");

            foreach (var classEntry in _classConfig.classes)
            {
                string fileName = GetStartingSkillsGcFileName(classEntry.Key);
                if (string.IsNullOrEmpty(fileName))
                    continue;

                string filePath = Path.Combine(gcDir, fileName);
                GCNode node = null;
                if (packageCatalog.IsLoaded && packageCatalog.TryGetGcText(fileName, out var packageDoc))
                    node = GcParser.Parse(packageDoc.Text, packageDoc.Stem);
                else if (File.Exists(filePath))
                    node = GcParser.ParseFile(filePath);
                else
                    throw new FileNotFoundException($"Class starting skills GC export missing for {classEntry.Key}", filePath);

                if (node == null)
                    throw new InvalidDataException($"Class starting skills GC export could not be parsed for {classEntry.Key}: {fileName}");

                var authoredSkills = node.AnonymousChildren
                    .Select(child => child.Extends)
                    .Where(skill => !string.IsNullOrWhiteSpace(skill)
                        && !skill.StartsWith("skills.professions.", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (authoredSkills.Count == 0)
                    throw new InvalidDataException($"Class starting skills GC export has no authored skills: {filePath}");

                foreach (string skill in authoredSkills)
                {
                    if (!classEntry.Value.startingSkills.Any(startingSkill => string.Equals(startingSkill, skill, StringComparison.OrdinalIgnoreCase)))
                        classEntry.Value.startingSkills.Add(skill);
                }
            }
        }

        private static string ResolveBundledGcDirectory()
        {
            var candidates = new List<string>();
            candidates.Add(DungeonRunners.Core.DataPaths.GcDir);
            candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "DR_Server", "Assets", "DungeonRunners", "Database", "gc"));
            candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "Database", "gc"));
            candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Database", "gc"));

            return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
        }

        private static string GetStartingSkillsGcFileName(string className)
        {
            string cls = (className ?? "").ToLowerInvariant();
            if (cls.Contains("fighter")) return "FighterStartingSkills.gc";
            if (cls.Contains("ranger")) return "RangerStartingSkills.gc";
            if (cls.Contains("mage") || cls.Contains("warlock")) return "WarlockStartingSkills.gc";
            return null;
        }

        private static Dictionary<string, int> StartingEquipmentLevels(string className)
        {
            string cls = (className ?? "").ToLowerInvariant();
            if (!cls.Contains("fighter") && !cls.Contains("mage") && !cls.Contains("warlock") && !cls.Contains("ranger"))
                return new Dictionary<string, int>();

            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["weapon"] = 1,
                ["armor"] = 1,
                ["gloves"] = 1,
                ["boots"] = 1
            };
        }

        public static ClassDefinition GetClassDefinition(string className)
        {
            if (!_isLoaded) Load();

            if (_classConfig.classes.TryGetValue(className, out var classDef))
            {
                return classDef;
            }

            Debug.LogError($"[CLASS-CONFIG] class='{className}' missing");
            return null;
        }
    }
}
