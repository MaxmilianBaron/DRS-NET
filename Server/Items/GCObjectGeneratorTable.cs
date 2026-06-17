using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DungeonRunners.Engine;
using DungeonRunners.Data;
using DungeonRunners.Combat;
using DungeonRunners.Core;

namespace DungeonRunners.Gameplay
{

    public class GCObjectGeneratorTable
    {
        private static GCObjectGeneratorTable _instance;
        public static GCObjectGeneratorTable Instance => _instance ??= new GCObjectGeneratorTable();

        private bool _initialized;

        private Dictionary<string, TreasureGenerator> _itemGenerators;
        private Dictionary<string, CurrencyGenerator> _currencyGenerators;

        private static readonly string[] PackageBackedItemGeneratorNames =
        {
            "DefaultIG",
            "ChampionIG",
            "HeroIG",
            "TreasureChestIG",
            "TreasureChestSmallIG",
            "TreasureChestBossIG",
            "TreasureChestMediumIG",
            "TreasureChestLargeIG"
        };

        private static readonly string[] PackageBackedCurrencyGeneratorNames =
        {
            "DefaultGG",
            "ChampionGG",
            "HeroGG",
            "LegendGG",
            "TreasureChestSmallGG"
        };

        private int RollLootRandom(int maxExclusive, string phase, string owner)
        {
            if (maxExclusive <= 0)
                return 0;
            uint raw = RandomStreams.GenerateGlobalStatic(
                phase ?? "GCObjectGeneratorTable::GenerateObjectFromTable",
                owner ?? "GCObjectGeneratorTable");
            int value = (int)(raw % (uint)maxExclusive);
            Debug.LogError($"[GC-OBJECT-GENERATOR-TABLE] stream=globalStatic phase={phase ?? "draw"} raw=0x{raw:X8} value={value} max={maxExclusive} owner='{owner ?? "unknown"}' sourceFunction=GCObjectGeneratorTable<Item>::GenerateObjectFromTable@0x0059DC30 RandomItemGenerator::GenerateObjectFromTable@0x005A02E0");
            return value;
        }

        private int RollLootRandom(int minInclusive, int maxExclusive, string phase, string owner)
        {
            if (maxExclusive <= minInclusive)
                return minInclusive;
            uint raw = RandomStreams.GenerateGlobalStatic(
                phase ?? "GCObjectGeneratorTable::GenerateObjectFromTable.range",
                owner ?? "GCObjectGeneratorTable");
            int span = maxExclusive - minInclusive;
            int value = minInclusive + (int)(raw % (uint)span);
            Debug.LogError($"[GC-OBJECT-GENERATOR-TABLE] stream=globalStatic phase={phase ?? "draw"} raw=0x{raw:X8} value={value} range=[{minInclusive},{maxExclusive}) owner='{owner ?? "unknown"}' sourceFunction=GCObjectGeneratorTable<Item>::GenerateObjectFromTable@0x0059DC30 Random::generate@0x0044B1F0");
            return value;
        }

        private Dictionary<string, CreatureTreasureData> _creatureTreasure;


        public void Initialize()
        {
            if (_initialized) return;

            BuildItemGenerators();
            BuildCurrencyGenerators();
            LoadCreatureTreasureData();
            LogTreasureCoverage();
            LoadClientExcludedItems();

            _initialized = true;
            Debug.LogError($"[GC-OBJECT-GENERATOR-TABLE] loaded itemGenerators={_itemGenerators.Count} currencyGenerators={_currencyGenerators.Count} creatureTreasure={_creatureTreasure.Count} clientExcluded={_clientExcludedItems.Count}");
        }

        private static HashSet<string> _clientExcludedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly Regex _newClassRegex = new Regex(
            @"^[\t ]*([A-Za-z0-9][A-Za-z0-9_-]*)\s+extends\s+\S+\s*//.*NEW!",
            RegexOptions.Compiled);

        private static readonly Regex _renamedClassRegex = new Regex(
            @"^[\t ]*([A-Za-z0-9][A-Za-z0-9_-]*)\s+extends\s+\S+\s*//\s*was\s",
            RegexOptions.Compiled);

        private void LoadClientExcludedItems()
        {
            _clientExcludedItems.Clear();

            string gcDir = DungeonRunners.Core.DataPaths.GcDir;

            if (!Directory.Exists(gcDir))
            {
                Debug.LogError($"[GC-OBJECT-GENERATOR-TABLE] gcDirMissing path={gcDir} filter=disabled");
                return;
            }

            int filesScanned = 0;
            try
            {
                foreach (string filePath in Directory.GetFiles(gcDir, "*.gc"))
                {
                    string ns = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();
                    string[] lines;
                    try
                    {
                        lines = File.ReadAllLines(filePath);
                    }
                    catch (Exception readEx)
                    {
                        Debug.LogError($"[GC-OBJECT-GENERATOR-TABLE] readFailed path={filePath} error={readEx.Message}");
                        continue;
                    }
                    filesScanned++;
                    foreach (string line in lines)
                    {
                        var newClassMatch = _newClassRegex.Match(line);
                        if (newClassMatch.Success)
                        {
                            string className = newClassMatch.Groups[1].Value.ToLowerInvariant();
                            _clientExcludedItems.Add($"{ns}.{className}");
                            continue;
                        }
                        var renamedClassMatch = _renamedClassRegex.Match(line);
                        if (renamedClassMatch.Success)
                        {
                            string className = renamedClassMatch.Groups[1].Value.ToLowerInvariant();
                            _clientExcludedItems.Add($"{ns}.{className}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GC-OBJECT-GENERATOR-TABLE] clientExcludedItems error={ex.Message}");
            }

            Debug.LogError($"[GC-OBJECT-GENERATOR-TABLE] gcFiles={filesScanned} clientExcluded={_clientExcludedItems.Count}");
        }


        private void BuildItemGenerators()
        {
            _itemGenerators = new Dictionary<string, TreasureGenerator>(StringComparer.OrdinalIgnoreCase);
            if (GCDatabase.Instance == null || !GCDatabase.Instance.IsLoaded)
                throw new InvalidDataException("GCDatabase must be loaded before package-backed loot generators");

            var missing = new List<string>();
            foreach (string generatorName in PackageBackedItemGeneratorNames)
            {
                if (TryBuildAuthoredTreasureGenerator(generatorName, out var generator))
                {
                    _itemGenerators[generatorName] = generator;
                    continue;
                }
                missing.Add(generatorName);
            }

            if (missing.Count > 0)
                throw new InvalidDataException($"Package-backed item generators missing: {string.Join(",", missing)}");

            Debug.LogError($"[GC-OBJECT-GENERATOR-TABLE] kind=item source=GC-DATABASE+PACKAGE-CATALOG packageBacked={PackageBackedItemGeneratorNames.Length} total={_itemGenerators.Count}");
        }


        private void BuildCurrencyGenerators()
        {
            _currencyGenerators = new Dictionary<string, CurrencyGenerator>(StringComparer.OrdinalIgnoreCase);

            var missing = new List<string>();
            foreach (string generatorName in PackageBackedCurrencyGeneratorNames)
            {
                if (TryBuildAuthoredCurrencyGenerator(generatorName, out var generator))
                {
                    _currencyGenerators[generatorName] = generator;
                    continue;
                }
                missing.Add(generatorName);
            }

            if (missing.Count > 0)
                throw new InvalidDataException($"Package-backed gold generators missing: {string.Join(",", missing)}");

            Debug.LogError($"[GC-OBJECT-GENERATOR-TABLE] kind=currency source=GC-DATABASE+PACKAGE-CATALOG packageBacked={PackageBackedCurrencyGeneratorNames.Length} total={_currencyGenerators.Count}");
        }

        private bool TryBuildAuthoredTreasureGenerator(string generatorName, out TreasureGenerator generator)
        {
            generator = null;
            GCNode node = ResolvePackageGeneratorNode(generatorName);
            if (node == null)
                return false;

            var entries = new List<(TreasureEntry entry, int order)>();
            int order = 0;
            int skipped = 0;
            foreach (GCNode child in EnumerateOrderedChildren(node))
            {
                GCNode entryNode = ResolveChildWithInheritance(child);
                if (!TryResolveItemRarity(entryNode, out var rarity))
                {
                    skipped++;
                    continue;
                }

                int chance = Math.Max(1, entryNode.GetInt("Chance", 1));
                int minLevel = entryNode.GetInt("MinLevel", 0);
                int maxLevel = entryNode.GetInt("MaxLevel", 999);
                entries.Add((new TreasureEntry(rarity, chance, minLevel, maxLevel), order++));
            }

            if (entries.Count == 0)
                return false;

            generator = new TreasureGenerator(entries
                .OrderByDescending(x => x.entry.Chance)
                .ThenBy(x => x.order)
                .Select(x => x.entry)
                .ToArray());

            Debug.LogError($"[GC-OBJECT-GENERATOR-TABLE] kind=item generator={generatorName} entries={generator.Entries.Length} skippedNonItem={skipped} source=package-backed");
            return true;
        }

        private bool TryBuildAuthoredCurrencyGenerator(string generatorName, out CurrencyGenerator generator)
        {
            generator = null;
            GCNode node = ResolvePackageGeneratorNode(generatorName);
            if (node == null)
                return false;

            var entries = new List<(CurrencyEntry entry, int order)>();
            int order = 0;
            int skipped = 0;
            foreach (GCNode child in EnumerateOrderedChildren(node))
            {
                GCNode entryNode = ResolveChildWithInheritance(child);
                if (!entryNode.HasProperty("GoldValue"))
                {
                    skipped++;
                    continue;
                }

                entries.Add((new CurrencyEntry(
                    Math.Max(1, entryNode.GetInt("Chance", 1)),
                    entryNode.GetFloat("GoldValue", 0f),
                    entryNode.GetFloat("Volatility", 0f),
                    entryNode.GetInt("MinLevel", 0),
                    entryNode.GetInt("MaxLevel", 999)), order++));
            }

            if (entries.Count == 0)
                return false;

            generator = new CurrencyGenerator(entries
                .OrderByDescending(x => x.entry.Chance)
                .ThenBy(x => x.order)
                .Select(x => x.entry)
                .ToArray());

            Debug.LogError($"[GC-OBJECT-GENERATOR-TABLE] kind=currency generator={generatorName} entries={generator.Entries.Length} skippedNonCurrency={skipped} source=package-backed");
            return true;
        }

        private static GCNode ResolvePackageGeneratorNode(string generatorName)
        {
            if (string.IsNullOrWhiteSpace(generatorName))
                return null;

            var catalog = PackageCatalog.Instance;
            if (!catalog.IsLoaded)
                catalog.LoadFromAssets();
            if (!catalog.TryGetGcText(generatorName, out var document) || document == null || string.IsNullOrWhiteSpace(document.Text))
                return null;

            return GcParser.Parse(document.Text, document.Stem);
        }

        private static IEnumerable<GCNode> EnumerateOrderedChildren(GCNode node)
        {
            if (node?.Children == null || node.Children.Count == 0)
                yield break;

            var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (node.ChildOrder != null)
            {
                foreach (string childName in node.ChildOrder)
                {
                    if (string.IsNullOrWhiteSpace(childName))
                        continue;
                    if (node.Children.TryGetValue(childName, out var child) && child != null && yielded.Add(childName))
                        yield return child;
                }
            }

            foreach (var childEntry in node.Children)
            {
                if (childEntry.Value == null || !yielded.Add(childEntry.Key))
                    continue;
                yield return childEntry.Value;
            }
        }

        private static GCNode ResolveChildWithInheritance(GCNode child)
        {
            if (child == null)
                return null;
            if (string.IsNullOrWhiteSpace(child.Extends))
                return child;

            GCNode parent = GCDatabase.Instance?.ResolveWithInheritance(child.Extends);
            if (parent == null)
                return child;

            var merged = new GCNode
            {
                Name = child.Name,
                Extends = child.Extends,
                IsStatic = child.IsStatic || parent.IsStatic,
                IsAnonymous = child.IsAnonymous || parent.IsAnonymous,
                SourceFile = child.SourceFile
            };

            foreach (var propertyEntry in parent.Properties)
                merged.Properties[propertyEntry.Key] = propertyEntry.Value;
            foreach (var propertyEntry in child.Properties)
                merged.Properties[propertyEntry.Key] = propertyEntry.Value;

            foreach (var childEntry in parent.Children)
                merged.Children[childEntry.Key] = childEntry.Value;
            foreach (string childName in parent.ChildOrder)
                if (!merged.ChildOrder.Contains(childName))
                    merged.ChildOrder.Add(childName);
            foreach (var childEntry in child.Children)
            {
                merged.Children[childEntry.Key] = childEntry.Value;
                if (!merged.ChildOrder.Contains(childEntry.Key))
                    merged.ChildOrder.Add(childEntry.Key);
            }

            merged.AnonymousChildren.AddRange(parent.AnonymousChildren);
            merged.AnonymousChildren.AddRange(child.AnonymousChildren);
            return merged;
        }

        private static bool TryResolveItemRarity(GCNode entry, out ItemRarity rarity)
        {
            rarity = ItemRarity.Normal;
            string token = ((entry?.Name ?? "") + " " + (entry?.Extends ?? "")).ToLowerInvariant();
            if (token.Contains("mythic"))
            {
                rarity = ItemRarity.Mythic;
                return true;
            }
            if (token.Contains("unique"))
            {
                rarity = ItemRarity.Unique;
                return true;
            }
            if (token.Contains("rare"))
            {
                rarity = ItemRarity.Rare;
                return true;
            }
            if (token.Contains("magic"))
            {
                rarity = ItemRarity.Magical;
                return true;
            }
            if (token.Contains("superior"))
            {
                rarity = ItemRarity.Superior;
                return true;
            }
            if (token.Contains("normal"))
            {
                rarity = ItemRarity.Normal;
                return true;
            }
            return false;
        }

        private void LoadCreatureTreasureData()
        {
            _creatureTreasure = new Dictionary<string, CreatureTreasureData>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var connection = Database.GameDatabase.GetConnection();
                using var reader = Database.GameDatabase.ExecuteReader(connection,
                    @"SELECT gc_type, treasure_gen1, treasure_count1, treasure_gen2, treasure_count2,
                             treasure_gen3, treasure_count3, treasure_gen4, treasure_count4
                      FROM creatures WHERE treasure_gen1 IS NOT NULL AND treasure_gen1 != ''");
                while (reader.Read())
                {
                    string gcType = reader.GetString(0);
                    var treasureData = new CreatureTreasureData();
                    for (int generatorSlot = 0; generatorSlot < 4; generatorSlot++)
                    {
                        string generatorType = reader.IsDBNull(1 + generatorSlot * 2) ? "" : reader.GetString(1 + generatorSlot * 2);
                        int generatorCount = 0;
                        if (!reader.IsDBNull(2 + generatorSlot * 2))
                        {
                            try { generatorCount = Convert.ToInt32(reader.GetValue(2 + generatorSlot * 2)); }
                            catch { generatorCount = 0; }
                        }
                        if (!string.IsNullOrEmpty(generatorType) && generatorCount > 0)
                            treasureData.Generators.Add((generatorType, generatorCount));
                    }
                    if (treasureData.Generators.Count > 0)
                        _creatureTreasure[gcType] = treasureData;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GC-OBJECT-GENERATOR-TABLE] creatureTreasureLoadFailed error={ex.Message}");
            }
        }

        private void LogTreasureCoverage()
        {
            int creatureRows = DatabaseLoader.Creatures?.Count ?? 0;
            int missing = 0;
            var samples = new List<string>();
            if (DatabaseLoader.Creatures != null)
            {
                foreach (var creature in DatabaseLoader.Creatures)
                {
                    if (creature == null || string.IsNullOrWhiteSpace(creature.gcType))
                        continue;
                    if (_creatureTreasure.ContainsKey(creature.gcType))
                        continue;
                    missing++;
                    if (samples.Count < 10)
                        samples.Add(creature.gcType);
                }
            }

            Debug.LogError($"[AUTHORED-COVERAGE] area=loot creatureRows={creatureRows} dbWithTreasure={_creatureTreasure.Count} dbMissingTreasure={missing} dbCreatureTreasureSlots=1-4 authoredTreasureSlots=1-10 currencyTokenLinkGenerators=limited missingSamples={string.Join("|", samples)}");
        }

        private CreatureTreasureData ResolveAuthoredCreatureTreasure(Monster monster)
        {
            if (monster == null || GCDatabase.Instance == null)
                return null;

            var paths = new List<string>();
            if (!string.IsNullOrWhiteSpace(monster.SpawnGCType)) paths.Add(monster.SpawnGCType);
            if (!string.IsNullOrWhiteSpace(monster.GCType)) paths.Add(monster.GCType);
            if (monster.AuthoredArchetypeAncestry != null)
                paths.AddRange(monster.AuthoredArchetypeAncestry);

            foreach (string path in paths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                GCNode node = GCDatabase.Instance.ResolveWithInheritance(path);
                if (node == null)
                    continue;

                var data = new CreatureTreasureData();
                for (int slot = 1; slot <= 10; slot++)
                {
                    string genKey = slot == 1 ? "TreasureGenerator" : $"TreasureGenerator{slot}";
                    string countKey = slot == 1 ? "TreasureCount" : $"TreasureCount{slot}";
                    string generator = GetEffectiveString(node, genKey);
                    int count = GetEffectiveInt(node, countKey, string.IsNullOrWhiteSpace(generator) ? 0 : 1);
                    if (!string.IsNullOrWhiteSpace(generator) && count > 0)
                        data.Generators.Add((generator, count));
                }

                if (data.Generators.Count > 0)
                {
                    Debug.LogError($"[LOOT-AUTHORED] source=PKG creature={monster.Name}#{monster.EntityId} path='{path}' generators={string.Join(",", data.Generators.Select(g => $"{g.gen}x{g.count}"))}");
                    return data;
                }
            }

            return null;
        }


        public List<LootDrop> GenerateMobLoot(Monster monster, int playerLevel, bool isMember = false)
        {
            if (!_initialized) Initialize();
            var drops = new List<LootDrop>();

            var treasure = ResolveAuthoredCreatureTreasure(monster);
            if (treasure == null)
                _creatureTreasure.TryGetValue(monster.GCType, out treasure);
            if (treasure == null)
            {
                Debug.LogError($"[AUTHORED-COVERAGE] area=loot reason=missing-treasure monster={monster.Name} gc={monster.GCType} spawnGc={monster.SpawnGCType ?? ""} tier={monster.Tier}");
                return drops;
            }

            RollTreasureData(treasure, playerLevel, isMember, drops, $"mob:{monster.Name}#{monster.EntityId}", $"source=mob monster={monster.Name} gc={monster.GCType}");

            if (drops.Count > 0)
                Debug.LogError($"[GC-OBJECT-GENERATOR-TABLE] monster={monster.Name} tier={monster.Tier} gold={drops.Count(d => d.IsGold)} items={drops.Count(d => d.IsItem)} kingsCoin={drops.Count(d => d.IsKingsCoin)}");
            return drops;
        }

        public List<LootDrop> GenerateDestroyableLoot(Monster monster, int playerLevel, bool isMember = false)
        {
            if (!_initialized) Initialize();
            var drops = new List<LootDrop>();

            var treasure = ResolveAuthoredCreatureTreasure(monster);
            if (treasure != null)
            {
                RollTreasureData(treasure, playerLevel, isMember, drops, $"destroyable:{monster.Name}#{monster.EntityId}", $"source=destroyable monster={monster.Name} gc={monster.GCType}");
                Debug.LogError($"[LOOT-AUTHORED] source=destroyable monster={monster.Name}#{monster.EntityId} gc={monster.GCType} drops={drops.Count}");
                return drops;
            }

            Debug.LogError($"[AUTHORED-COVERAGE] area=loot reason=missing-destroyable-treasure monster={monster?.Name ?? "<null>"} gc={monster?.GCType ?? "<null>"}");
            return drops;
        }

        private void RollTreasureData(CreatureTreasureData treasure, int playerLevel, bool isMember, List<LootDrop> drops, string source, string fallbackContext)
        {
            foreach (var (generatorType, rollCount) in treasure.Generators)
            {
                for (int rollIndex = 0; rollIndex < rollCount; rollIndex++)
                {
                    if (_currencyGenerators.TryGetValue(generatorType, out var currencyGen))
                    {
                        int gold = RollCurrency(currencyGen, playerLevel, isMember);
                        if (gold > 0) drops.Add(LootDrop.Gold(gold));
                    }
                    else if (_itemGenerators.TryGetValue(generatorType, out var itemGen))
                    {
                        var item = RollItem(itemGen, playerLevel);
                        if (item != null) drops.Add(item);
                    }
                    else if (TryRollAuthoredGenerator(generatorType, playerLevel, isMember, out var authoredDrops, source))
                    {
                        drops.AddRange(authoredDrops);
                    }
                    else
                    {
                        RuntimeEvidence.LogFallbackHit(
                            "gc-object-generator-table",
                            "missing-generator",
                            $"{fallbackContext} generator={generatorType}",
                            32);
                        Debug.LogError($"[LOOT-FALLBACK] reason=missing-generator {fallbackContext} generator={generatorType}");
                    }
                }
            }
        }

        public List<LootDrop> GenerateChestLoot(string generatorName, int itemCount, int playerLevel)
        {
            if (!_initialized) Initialize();
            var drops = new List<LootDrop>();

            if (_itemGenerators.TryGetValue(generatorName, out var itemGenerator))
                for (int itemIndex = 0; itemIndex < itemCount; itemIndex++)
                {
                    var item = RollItem(itemGenerator, playerLevel);
                    if (item != null) drops.Add(item);
                }
            else if (TryRollAuthoredGenerator(generatorName, playerLevel, false, out var authoredDrops, "chest"))
            {
                drops.AddRange(authoredDrops.Take(Math.Max(1, itemCount)));
            }
            else
            {
                RuntimeEvidence.LogFallbackHit(
                    "gc-object-generator-table",
                    "missing-chest-generator",
                    $"generator={generatorName ?? "<null>"} itemCount={itemCount} playerLevel={playerLevel}",
                    32);
                Debug.LogError($"[LOOT-FALLBACK] reason=missing-chest-generator generator={generatorName ?? "<null>"} itemCount={itemCount} playerLevel={playerLevel}");
            }

            return drops;
        }

        public List<LootDrop> GenerateAuthoredGeneratorLoot(string generatorName, int itemCount, int playerLevel, bool isMember = false, string source = null)
        {
            if (!_initialized) Initialize();
            var drops = new List<LootDrop>();
            int rollCount = Math.Max(1, itemCount);
            for (int rollIndex = 0; rollIndex < rollCount; rollIndex++)
            {
                if (_itemGenerators.TryGetValue(generatorName, out var itemGen))
                {
                    var item = RollItem(itemGen, playerLevel);
                    if (item != null) drops.Add(item);
                    continue;
                }
                if (_currencyGenerators.TryGetValue(generatorName, out var currencyGen))
                {
                    int gold = RollCurrency(currencyGen, playerLevel, isMember);
                    if (gold > 0) drops.Add(LootDrop.Gold(gold));
                    continue;
                }
                if (TryRollAuthoredGenerator(generatorName, playerLevel, isMember, out var authoredDrops, source ?? "authored-generator"))
                    drops.AddRange(authoredDrops);
            }
            return drops;
        }

        public bool CanResolveAuthoredGenerator(string generatorName)
        {
            if (!_initialized) Initialize();
            if (string.IsNullOrWhiteSpace(generatorName))
                return false;
            if (_itemGenerators.ContainsKey(generatorName) || _currencyGenerators.ContainsKey(generatorName))
                return true;
            return GCDatabase.Instance?.ResolveWithInheritance(generatorName) != null;
        }

        private bool TryRollAuthoredGenerator(string generatorName, int playerLevel, bool isMember, out List<LootDrop> drops, string source)
        {
            drops = new List<LootDrop>();
            if (string.IsNullOrWhiteSpace(generatorName))
                return false;
            var node = GCDatabase.Instance?.ResolveWithInheritance(generatorName);
            if (node == null)
                return false;

            RollAuthoredGeneratorNode(node, generatorName, playerLevel, isMember, drops, new HashSet<string>(StringComparer.OrdinalIgnoreCase), source);
            return true;
        }

        private void RollAuthoredGeneratorNode(GCNode node, string generatorName, int playerLevel, bool isMember, List<LootDrop> drops, HashSet<string> stack, string source)
        {
            if (node == null || !stack.Add(generatorName ?? node.Name ?? "anonymous"))
                return;

            if (ProcessAuthoredGeneratorEntry(node, generatorName, playerLevel, isMember, drops, stack, source))
                return;

            var entries = new List<GCNode>();
            if (node.AnonymousChildren != null)
                entries.AddRange(node.AnonymousChildren.Where(c => c != null));
            if (node.Children != null)
                entries.AddRange(EnumerateOrderedChildren(node)
                    .Select(ResolveChildWithInheritance)
                    .Where(c => c != null));

            var eligibleGroups = entries
                .Select((entry, order) => new
                {
                    Entry = entry,
                    Order = order,
                    Chance = Math.Max(1, entry.GetInt("Chance", 1)),
                    MinLevel = entry.GetInt("MinLevel", 0),
                    MaxLevel = entry.GetInt("MaxLevel", 999)
                })
                .Where(x => playerLevel >= x.MinLevel && playerLevel <= x.MaxLevel)
                .GroupBy(x => x.Chance)
                .OrderByDescending(g => g.Key);

            foreach (var group in eligibleGroups)
            {
                if (RollLootRandom(group.Key, "authored-generator-chance", $"{source ?? "unknown"}:{generatorName}:chance={group.Key}") != 0)
                    continue;

                var candidates = group.OrderBy(x => x.Order).ToList();
                var selected = candidates[RollLootRandom(candidates.Count, "authored-generator-select", $"{source ?? "unknown"}:{generatorName}:candidates={candidates.Count}")];
                ProcessAuthoredGeneratorEntry(selected.Entry, generatorName, playerLevel, isMember, drops, stack, source);
                return;
            }
        }

        private bool ProcessAuthoredGeneratorEntry(GCNode entry, string generatorName, int playerLevel, bool isMember, List<LootDrop> drops, HashSet<string> stack, string source)
        {
            if (entry == null)
                return false;

            string linked = entry.GetString("LinkedGenerator", "");
            if (!string.IsNullOrWhiteSpace(linked))
            {
                if (_itemGenerators.TryGetValue(linked, out var itemGen))
                {
                    var item = RollItem(itemGen, playerLevel);
                    if (item != null) drops.Add(item);
                }
                else if (_currencyGenerators.TryGetValue(linked, out var currencyGen))
                {
                    int gold = RollCurrency(currencyGen, playerLevel, isMember);
                    if (gold > 0) drops.Add(LootDrop.Gold(gold));
                }
                else
                {
                    var linkedNode = GCDatabase.Instance.ResolveWithInheritance(linked);
                    RollAuthoredGeneratorNode(linkedNode, linked, playerLevel, isMember, drops, stack, source);
                }
                return true;
            }

            string itemGenerator = entry.GetString("ItemGenerator", "");
            if (!string.IsNullOrWhiteSpace(itemGenerator))
            {
                int before = drops.Count;
                if (_itemGenerators.TryGetValue(itemGenerator, out var itemGen))
                {
                    var item = RollItem(itemGen, playerLevel);
                    if (item != null) drops.Add(item);
                }
                else if (_currencyGenerators.TryGetValue(itemGenerator, out var currencyGen))
                {
                    int gold = RollCurrency(currencyGen, playerLevel, isMember);
                    if (gold > 0) drops.Add(LootDrop.Gold(gold));
                }
                else
                {
                    var generatorNode = GCDatabase.Instance.ResolveWithInheritance(itemGenerator);
                    RollAuthoredGeneratorNode(generatorNode, itemGenerator, playerLevel, isMember, drops, stack, source);
                }

                if (drops.Count > before)
                {
                    Debug.LogError($"[LOOT-AUTHORED] source={source ?? "unknown"} generator={generatorName} itemGenerator={itemGenerator} produced={drops.Count - before} modGenerators={CountItemModGenerators(entry)}");
                }
                else
                {
                    Debug.LogError($"[AUTHORED-COVERAGE] area=loot reason=item-generator-empty source={source ?? "unknown"} generator={generatorName} itemGenerator={itemGenerator}");
                }
                return true;
            }

            string itemPath = entry.GetString("Item", "");
            if (!string.IsNullOrWhiteSpace(itemPath))
            {
                int forcedLevel = entry.GetInt("ForcedLevel", entry.GetInt("Level", playerLevel));
                ItemRarity rarity = RPGSettings.GetRarityFromTier(RPGSettings.GetTierFromGcType(itemPath));
                string scaleMod = RPGSettings.GetRandomScaleMod(rarity);
                drops.Add(LootDrop.Item(itemPath, GetItemLabel(itemPath), rarity, scaleMod, forcedLevel > 0 ? forcedLevel : playerLevel));
                Debug.LogError($"[LOOT-AUTHORED] source={source ?? "unknown"} generator={generatorName} item={itemPath} level={forcedLevel}");
                return true;
            }

            if (entry.HasProperty("GoldValue"))
            {
                var goldEntry = new CurrencyEntry(
                    Math.Max(1, entry.GetInt("Chance", 1)),
                    entry.GetFloat("GoldValue", 0f),
                    entry.GetFloat("Volatility", 0f),
                    entry.GetInt("MinLevel", 0),
                    entry.GetInt("MaxLevel", 999));
                int gold = RollCurrencyAmount(goldEntry, playerLevel, isMember);
                if (gold > 0) drops.Add(LootDrop.Gold(gold));
                Debug.LogError($"[LOOT-AUTHORED] source={source ?? "unknown"} generator={generatorName} gold={gold}");
                return true;
            }

            return false;
        }

        private static int CountItemModGenerators(GCNode entry)
        {
            if (entry == null || entry.Properties == null)
                return 0;
            int count = 0;
            foreach (var key in entry.Properties.Keys)
            {
                if (key != null && key.StartsWith("ItemModGenerator", StringComparison.OrdinalIgnoreCase))
                    count++;
            }
            return count;
        }

        private LootDrop RollItem(TreasureGenerator gen, int playerLevel)
        {
            var eligibleGroups = gen.Entries
                .Select((entry, order) => new
                {
                    Entry = entry,
                    Order = order,
                    Chance = Math.Max(1, entry.Chance)
                })
                .Where(x => playerLevel >= x.Entry.MinLevel && playerLevel <= x.Entry.MaxLevel)
                .GroupBy(x => x.Chance)
                .OrderByDescending(g => g.Key);

            foreach (var group in eligibleGroups)
            {
                if (RollLootRandom(group.Key, "item-generator-chance", $"chance={group.Key}") != 0)
                    continue;

                var candidates = group.OrderBy(x => x.Order).ToList();
                var selected = candidates[RollLootRandom(candidates.Count, "item-generator-select", $"chance={group.Key}:candidates={candidates.Count}")].Entry;
                string gcType = PickRandomItem(selected.Rarity, playerLevel);
                if (gcType != null)
                {
                    Debug.LogError($"[LOOT-ROLL] rarity={selected.Rarity} gc='{gcType}'");
                    string scaleMod = RPGSettings.GetRandomScaleMod(selected.Rarity);
                    int itemLevel = RPGSettings.GetItemLevel(gcType);
                    return LootDrop.Item(gcType, GetItemLabel(gcType), selected.Rarity,
                        scaleMod, itemLevel > 0 ? itemLevel : playerLevel);
                }
            }
            return null;
        }

        private int RollCurrency(CurrencyGenerator gen, int playerLevel, bool isMember = false)
        {
            var eligibleGroups = gen.Entries
                .Select((entry, order) => new
                {
                    Entry = entry,
                    Order = order,
                    Chance = Math.Max(1, entry.Chance)
                })
                .Where(x => playerLevel >= x.Entry.MinLevel && playerLevel <= x.Entry.MaxLevel)
                .GroupBy(x => x.Chance)
                .OrderByDescending(g => g.Key);

            foreach (var group in eligibleGroups)
            {
                if (RollLootRandom(group.Key, "gold-generator-chance", $"chance={group.Key}") != 0)
                    continue;

                var candidates = group.OrderBy(x => x.Order).ToList();
                var selected = candidates[RollLootRandom(candidates.Count, "gold-generator-select", $"chance={group.Key}:candidates={candidates.Count}")].Entry;
                return RollCurrencyAmount(selected, playerLevel, isMember);
            }
            return 0;
        }

        private int RollCurrencyAmount(CurrencyEntry entry, int playerLevel, bool isMember)
        {
            int itemGoldPerLevelF32 = DamageResolver.Fixed32FromAuthoredDecimal(
                GCDatabase.Instance != null ? GCDatabase.Instance.GetKnob("ItemGoldValuePerLevel", 50f) : 50f);
            int memberGoldF32 = DamageResolver.Fixed32FromAuthoredDecimal(
                isMember && GCDatabase.Instance != null ? GCDatabase.Instance.GetKnob("MemberGoldMod", 1.15f) : 1f);
            int levelF32 = Math.Max(1, playerLevel) << 8;
            int goldValueF32 = DamageResolver.Fixed32FromAuthoredDecimal(entry.GoldValue);
            int volatilityF32 = DamageResolver.Fixed32FromAuthoredDecimal(entry.Volatility);

            int baseF32 = DamageResolver.FixedMul(
                DamageResolver.FixedMul(
                    DamageResolver.FixedMul(itemGoldPerLevelF32, memberGoldF32),
                    levelF32),
                goldValueF32);
            int spreadF32 = DamageResolver.FixedMul(baseF32, volatilityF32);
            int minF32 = Math.Max(0x100, baseF32 - spreadF32);
            int minGold = Math.Max(1, minF32 >> 8);
            int maxGold = (baseF32 + spreadF32) >> 8;
            if (maxGold <= minGold)
                return minGold;

            return minGold + RollLootRandom(maxGold - minGold, "gold-amount", $"min={minGold}:max={maxGold}");
        }


        private static bool IsPollutedNonItemClass(string gcType)
        {
            if (string.IsNullOrEmpty(gcType)) return true;
            if (gcType.IndexOf(".manipulators.", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (gcType.StartsWith("avatar.", StringComparison.OrdinalIgnoreCase))
                return true;
            if (gcType.StartsWith("testpal.", StringComparison.OrdinalIgnoreCase))
                return true;
            if (gcType.EndsWith(".visual", StringComparison.OrdinalIgnoreCase))
                return true;

            if (gcType.IndexOf(".effect.", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (gcType.EndsWith(".effect", StringComparison.OrdinalIgnoreCase))
                return true;
            if (gcType.IndexOf(".modifier.", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (gcType.EndsWith(".modifier", StringComparison.OrdinalIgnoreCase))
                return true;
            int lastDot = gcType.LastIndexOf('.');
            if (lastDot >= 0)
            {
                string suffix = gcType.Substring(lastDot + 1);
                if (suffix.Length >= 4 && suffix.StartsWith("mod", StringComparison.OrdinalIgnoreCase)
                    && char.IsDigit(suffix[3]))
                    return true;
            }
            if (gcType.IndexOf(".description.", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (gcType.EndsWith(".description", StringComparison.OrdinalIgnoreCase))
                return true;
            if (gcType.EndsWith(".spellsoundeffect", StringComparison.OrdinalIgnoreCase))
                return true;
            if (gcType.EndsWith(".sound", StringComparison.OrdinalIgnoreCase))
                return true;

            if (gcType.IndexOf('.') < 0)
                return true;

            if (_clientExcludedItems.Contains(gcType))
                return true;
            return false;
        }

        private string PickRandomItem(ItemRarity rarity, int playerLevel)
        {
            var pool = new List<string>();

            int maxItemLevel = playerLevel + 10;

            if (DatabaseLoader.AllWeapons != null)
                foreach (var weaponData in DatabaseLoader.AllWeapons)
                {
                    if (IsPollutedNonItemClass(weaponData.gcType)) continue;
                    if (!MatchesRarity(weaponData.gcType, rarity)) continue;
                    int itemLevel = RPGSettings.GetItemLevel(weaponData.gcType);
                    if (itemLevel > maxItemLevel) continue;
                    pool.Add(weaponData.gcType);
                }

            if (DatabaseLoader.AllArmor != null)
                foreach (var armorData in DatabaseLoader.AllArmor)
                {
                    if (IsPollutedNonItemClass(armorData.gcType)) continue;
                    if (!MatchesRarity(armorData.gcType, rarity)) continue;
                    int itemLevel = RPGSettings.GetItemLevel(armorData.gcType);
                    if (itemLevel > maxItemLevel) continue;
                    pool.Add(armorData.gcType);
                }

            if (rarity >= ItemRarity.Rare)
            {
                AddMatchingItemsLevelFiltered(pool, DatabaseLoader.Rings, rarity, maxItemLevel);
                AddMatchingItemsLevelFiltered(pool, DatabaseLoader.Amulets, rarity, maxItemLevel);
            }

            if (pool.Count == 0 && DatabaseLoader.ItemDatabase != null)
            {
                Debug.LogError($"[AUTHORED-COVERAGE] area=loot reason=empty-client-pool rarity={rarity} playerLevel={playerLevel} maxItemLevel={maxItemLevel} itemDatabaseFallbackSuppressed=True");
            }

            if (pool.Count == 0)
            {
                Debug.LogError($"[AUTHORED-COVERAGE] area=loot reason=empty-client-pool-suppressed rarity={rarity} playerLevel={playerLevel} maxItemLevel={maxItemLevel}");
            }

            return pool.Count > 0 ? pool[RollLootRandom(pool.Count, "item-pool-select", $"rarity={rarity}:level={playerLevel}:count={pool.Count}")] : null;
        }

        private void AddMatchingItemsLevelFiltered(List<string> pool, List<GeneralItemData> items, ItemRarity rarity, int maxItemLevel)
        {
            if (items == null) return;
            foreach (var item in items)
            {
                if (!MatchesRarity(item.gcType, rarity)) continue;
                int itemLv = RPGSettings.GetItemLevel(item.gcType);
                if (itemLv > maxItemLevel) continue;
                pool.Add(item.gcType);
            }
        }

        private bool MatchesRarity(string gcType, ItemRarity target)
        {
            if (string.IsNullOrEmpty(gcType)) return false;
            if (target == ItemRarity.Mythic) return RPGSettings.IsMythicPALItem(gcType);
            if (RPGSettings.IsMythicPALItem(gcType)) return false;
            return RPGSettings.GetRarityFromTier(RPGSettings.GetTierFromGcType(gcType)) == target;
        }

        private string GetItemLabel(string gcType)
        {
            if (DatabaseLoader.ItemDatabase?.TryGetValue(gcType, out var item) == true)
                return item.name ?? gcType;
            if (DatabaseLoader.GeneralItemDatabase?.TryGetValue(gcType, out var gi) == true)
                return gi.Label ?? gcType;
            return gcType;
        }

        private string GetEffectiveString(GCNode node, string key)
        {
            if (node == null || string.IsNullOrWhiteSpace(key))
                return "";
            GCNode desc = node.GetChild("Description");
            if (desc != null && desc.HasProperty(key))
                return desc.GetString(key, "");
            return node.GetString(key, "");
        }

        private int GetEffectiveInt(GCNode node, string key, int fallback)
        {
            if (node == null || string.IsNullOrWhiteSpace(key))
                return fallback;
            GCNode desc = node.GetChild("Description");
            if (desc != null && desc.HasProperty(key))
                return desc.GetInt(key, fallback);
            return node.GetInt(key, fallback);
        }

        public LootDrop RollKingsCoin(string tier)
        {
            int pct;
            switch ((tier ?? "").ToUpperInvariant())
            {
                case "HERO":
                case "BOSS":
                case "DUNGEON_BOSS":
                    pct = ServerSettings.Get("kingsCoinPctHero", 10);
                    break;
                case "CHAMPION":
                    pct = ServerSettings.Get("kingsCoinPctChampion", 5);
                    break;
                case "VETERAN":
                case "WARMONGER":
                    pct = ServerSettings.Get("kingsCoinPctVeteran", 2);
                    break;
                default:
                    pct = ServerSettings.Get("kingsCoinPctGrunt", 1);
                    break;
            }
            if (pct <= 0) return null;
            if (RollLootRandom(100, "kings-coin-chance", tier) >= pct) return null;

            int count = ServerSettings.Get("kingsCoinDropAmount", 1);
            if (count < 1) count = 1;
            Debug.LogError($"[LOOT-KC] tier={tier} pct={pct} count={count}");
            return LootDrop.KingsCoin(count);
        }
    }


    public class LootDrop
    {
        public bool IsGold;
        public int GoldAmount;
        public bool IsKingsCoin;
        public int KingsCoinCount;
        public bool IsItem => !IsGold && !IsKingsCoin;
        public string GCType;
        public string Label;
        public ItemRarity Rarity;
        public string ScaleMod;
        public int ItemLevel;

        public static LootDrop Gold(int amount) =>
            new LootDrop { IsGold = true, GoldAmount = amount };

        public static LootDrop Item(string gcType, string label, ItemRarity rarity, string scaleMod, int level) =>
            new LootDrop { GCType = gcType, Label = label, Rarity = rarity, ScaleMod = scaleMod, ItemLevel = level };

        public static LootDrop KingsCoin(int count) =>
            new LootDrop { IsKingsCoin = true, KingsCoinCount = count };
    }

    public class TreasureEntry
    {
        public ItemRarity Rarity;
        public int Chance;
        public int MinLevel;
        public int MaxLevel;
        public TreasureEntry(ItemRarity r, int c, int min, int max)
        { Rarity = r; Chance = c; MinLevel = min; MaxLevel = max; }
    }

    public class TreasureGenerator
    {
        public TreasureEntry[] Entries;
        public TreasureGenerator(TreasureEntry[] entries) { Entries = entries; }
    }

    public class CurrencyEntry
    {
        public int Chance;
        public float GoldValue;
        public float Volatility;
        public int MinLevel;
        public int MaxLevel;
        public CurrencyEntry(int c, float gv, float v, int min, int max)
        { Chance = c; GoldValue = gv; Volatility = v; MinLevel = min; MaxLevel = max; }
    }

    public class CurrencyGenerator
    {
        public CurrencyEntry[] Entries;
        public CurrencyGenerator(CurrencyEntry[] entries) { Entries = entries; }
    }

    public class CreatureTreasureData
    {
        public List<(string gen, int count)> Generators = new();
    }
}
