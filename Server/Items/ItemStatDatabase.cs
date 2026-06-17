using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mono.Data.Sqlite;
using DungeonRunners.Engine;

namespace DungeonRunners.Data
{
    public class ItemStatDatabase
    {
        private static ItemStatDatabase _instance;
        public static ItemStatDatabase Instance => _instance ??= new ItemStatDatabase();

        public bool IsLoaded { get; private set; }

        public static bool PathBEnabled = true;


        struct PoolFormula
        {
            public (int Level, float Value)[] Points;
            public float Eval(int level)
            {
                if (Points == null || Points.Length == 0) return 0f;
                if (level <= Points[0].Level) return Points[0].Value;
                var last = Points[Points.Length - 1];
                if (level >= last.Level) return last.Value;
                for (int pointIndex = 0; pointIndex < Points.Length - 1; pointIndex++)
                {
                    var lowerPoint = Points[pointIndex];
                    var upperPoint = Points[pointIndex + 1];
                    if (level >= lowerPoint.Level && level <= upperPoint.Level)
                        return lowerPoint.Value + (float)(level - lowerPoint.Level) / (upperPoint.Level - lowerPoint.Level) * (upperPoint.Value - lowerPoint.Value);
                }
                return last.Value;
            }
        }
        struct ResolvedMod
        {
            public int ModSlot;
            public string Attribute;
            public string Pool;
            public float ValueMult;
        }

        private Dictionary<string, PoolFormula> _pools = new();
        private Dictionary<string, List<ResolvedMod>> _itemMods = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, List<(int Slot, string ModRef)>> _itemWireMods = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, int> _itemReadDataSlotCounts = new(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _itemReadDataSlotMisses = new(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _gcClassNames = new(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, (string Attr, string Pool)> _attrMap = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _modPalRefs = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _weaponMythicRefs = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, List<(int Slot, string GeneratorPath)>> _directItemIGEntries = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, int> _directItemIGCountByRarity = new(StringComparer.OrdinalIgnoreCase);
        private List<(string Rarity, string TargetIGRef, List<(int Slot, string GeneratorPath)> Generators)> _wrapperIGEntries = new();
        private Dictionary<string, List<string>> _modGenerators = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _modGeneratorParents = new(StringComparer.OrdinalIgnoreCase);

        private string _gcDir;

        private struct GcTextSource
        {
            public string FileName;
            public string Stem;
            public string Text;
            public string SourcePath;
        }

        private IEnumerable<GcTextSource> EnumerateGcSources(string searchPattern)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(_gcDir))
            {
                foreach (var file in Directory.GetFiles(_gcDir, searchPattern))
                {
                    string fileName = Path.GetFileName(file);
                    seen.Add(fileName);
                    yield return new GcTextSource
                    {
                        FileName = fileName,
                        Stem = Path.GetFileNameWithoutExtension(file),
                        Text = File.ReadAllText(file),
                        SourcePath = file
                    };
                }
            }

            var packageCatalog = PackageCatalog.Instance;
            if (!packageCatalog.IsLoaded)
                packageCatalog.LoadFromAssets();
            if (!packageCatalog.IsLoaded)
                yield break;

            foreach (var doc in packageCatalog.EnumerateGcTextDocuments(searchPattern))
            {
                if (doc == null || string.IsNullOrWhiteSpace(doc.FileName) || seen.Contains(doc.FileName))
                    continue;
                seen.Add(doc.FileName);
                yield return new GcTextSource
                {
                    FileName = doc.FileName,
                    Stem = doc.Stem,
                    Text = doc.Text,
                    SourcePath = doc.Name
                };
            }
        }

        private bool TryReadGcSource(string fileName, out GcTextSource source)
        {
            source = default;
            string path = Path.Combine(_gcDir, fileName);
            if (File.Exists(path))
            {
                source = new GcTextSource
                {
                    FileName = Path.GetFileName(path),
                    Stem = Path.GetFileNameWithoutExtension(path),
                    Text = File.ReadAllText(path),
                    SourcePath = path
                };
                return true;
            }

            var packageCatalog = PackageCatalog.Instance;
            if (!packageCatalog.IsLoaded)
                packageCatalog.LoadFromAssets();
            if (packageCatalog.TryGetGcText(fileName, out var doc))
            {
                source = new GcTextSource
                {
                    FileName = doc.FileName,
                    Stem = doc.Stem,
                    Text = doc.Text,
                    SourcePath = doc.Name
                };
                return true;
            }
            return false;
        }


        public void Load()
        {
            try
            {
                _gcDir = DungeonRunners.Core.DataPaths.GcDir;
                bool hasBundledGcDir = Directory.Exists(_gcDir);
                if (!PackageCatalog.Instance.IsLoaded)
                    PackageCatalog.Instance.LoadFromAssets();
                if (!hasBundledGcDir && !PackageCatalog.Instance.IsLoaded)
                {
                    Debug.LogError($"[ITEM-STAT-DB] reason=gc-dir-missing packageCatalog=False path={_gcDir}");
                    return;
                }

                using var conn = Database.GameDatabase.GetConnection();
                CreateTables(conn);

                using (var command = conn.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(*) FROM item_resolved_mods";
                    long count = (long)command.ExecuteScalar();

                    bool needsRepopulate = count == 0;
                    if (!needsRepopulate)
                    {
                        command.CommandText = "SELECT COUNT(*) FROM item_resolved_mods WHERE full_gc_key='2haxemythicpal.2haxemythic101'";
                        long baselineItemCount = (long)command.ExecuteScalar();
                        needsRepopulate = baselineItemCount == 0;
                    }
                    if (!needsRepopulate)
                    {
                        command.CommandText = "SELECT COUNT(*) FROM item_wire_mods WHERE full_gc_key LIKE '2hcrossbow%:rare' LIMIT 1";
                        long weaponCount = (long)command.ExecuteScalar();
                        needsRepopulate = weaponCount == 0;
                    }
                    if (!needsRepopulate)
                    {
                        command.CommandText = "SELECT COUNT(*) FROM item_wire_mods WHERE full_gc_key LIKE 'items.pal.%' LIMIT 1";
                        long staleCount = (long)command.ExecuteScalar();
                        needsRepopulate = staleCount > 0;
                    }
                    if (!needsRepopulate)
                    {
                        command.CommandText = "SELECT COUNT(*) FROM item_resolved_mods WHERE full_gc_key='magebodypal.normal001:magic'";
                        long wrapperStatCount = (long)command.ExecuteScalar();
                        needsRepopulate = wrapperStatCount == 0;
                    }

                    if (needsRepopulate)
                    {
                        Debug.LogError($"[ITEM-STAT-DB] populate source=gc-files existingRows={count} mode=full-rebuild");
                        using (var deleteCommand = conn.CreateCommand())
                        {
                            deleteCommand.CommandText = "DELETE FROM item_resolved_mods; DELETE FROM item_wire_mods;";
                            deleteCommand.ExecuteNonQuery();
                        }
                        PopulateFromGCFiles(conn);
                    }
                    else
                    {
                        Debug.LogError($"[ITEM-STAT-DB] populate=False modEntries={count}");
                    }
                }

                _pools = DefaultPoolCurves();
                LoadResolvedMods(conn);
                LoadWireMods(conn);
                LoadGCDictionary();

                IsLoaded = true;

                Debug.LogError($"[ITEM-STAT-DB] loaded pools={_pools.Count} items={_itemMods.Count} mods={_itemMods.Values.Sum(v => v.Count)} wireItems={_itemWireMods.Count} gcClassNames={_gcClassNames.Count}");

                foreach (var itemKey in new[] {
                    "2haxemythicpal.2haxemythic101",
                    "magebodypal.rare001",
                    "magebodypal.unique001",
                    "platepal.plateuniquearmor1",
                    "magebodypal.normal001:magic",
                    "magebodypal.normal001:superior"
                })
                {
                    if (_itemWireMods.TryGetValue(itemKey, out var mods))
                        Debug.LogError($"[ITEM-STAT-DB] item={itemKey} wireMods={mods.Count} refs=[{string.Join(", ", mods.Select(p => p.ModRef))}]");
                    else
                        Debug.LogError($"[ITEM-STAT-DB] item={itemKey} wireMods=missing");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ITEM-STAT-DB] load error={ex.Message}\n{ex.StackTrace}");
            }
        }


        private void PopulateFromGCFiles(SqliteConnection conn)
        {
            ParseAttributesPAL();
            Debug.LogError($"[ITEM-STAT-DB] phase=1 attributeMappings={_attrMap.Count}");

            ParseAllModPALs();
            Debug.LogError($"[ITEM-STAT-DB] phase=2 modPalRefs={_modPalRefs.Count}");

            ParseWeaponMythicModPAL();
            Debug.LogError($"[ITEM-STAT-DB] phase=3 weaponMythicRefs={_weaponMythicRefs.Count}");

            int itemCount = 0, modCount = 0;
            using var transaction = conn.BeginTransaction();

            foreach (var source in EnumerateGcSources("*MythicPAL.gc"))
            {
                string fileName = source.Stem;
                if (fileName.Contains("Mod")) continue;

                var items = ParseItemFile(source.Text, fileName, false);
                foreach (var (itemName, mods) in items)
                {
                    string fullKey = $"{fileName}.{itemName}".ToLowerInvariant();
                    var resolved = ResolveMods(mods);
                    if (resolved.Count > 0)
                    {
                        InsertResolvedMods(conn, fullKey, resolved);
                        modCount += resolved.Count;
                        itemCount++;
                    }
                }
            }

            foreach (var source in EnumerateGcSources("*PAL.gc"))
            {
                string fileName = source.Stem;
                if (fileName.Contains("Mod") || fileName.Contains("Enhancement") ||
                    fileName.Contains("Attribute") || fileName.Contains("Mythic") ||
                    fileName.Contains("Pool") || fileName.Contains("Weapon") && fileName.EndsWith("PAL")) continue;
                string peek = source.Text;
                if (!peek.Contains("MythicPreBuilt")) continue;

                var items = ParseItemFile(source.Text, fileName, true);
                foreach (var (itemName, mods) in items)
                {
                    string fullKey = $"{fileName}.{itemName}".ToLowerInvariant();
                    var resolved = ResolveMods(mods);
                    if (resolved.Count > 0)
                    {
                        InsertResolvedMods(conn, fullKey, resolved);
                        modCount += resolved.Count;
                        itemCount++;
                    }
                }
            }

            Debug.LogError($"[ITEM-STAT-DB] phase=4 items={itemCount} resolvedMods={modCount}");

            ParseAllIGFiles();
            string rarityBreakdown = string.Join(" ", _directItemIGCountByRarity.OrderBy(k => k.Key).Select(k => $"{k.Key.ToLowerInvariant()}={k.Value}"));
            Debug.LogError($"[ITEM-STAT-DB] phase=5a directItemIG={_directItemIGEntries.Count} rarity='{rarityBreakdown}' wrapperIG={_wrapperIGEntries.Count}");

            ParseAllMGFiles();
            Debug.LogError($"[ITEM-STAT-DB] phase=5b modGenerators={_modGenerators.Count}");

            int igItemCount = 0, igStatCount = 0, igWireCount = 0;
            foreach (var entry in _directItemIGEntries)
            {
                string itemPalPath = entry.Key;
                var generators = entry.Value;

                var perSlotModRefs = new List<(int Slot, string ModRef)>();
                foreach (var (slot, generatorPath) in generators)
                {
                    string normGen = generatorPath;
                    if (normGen.StartsWith("items.mg.", StringComparison.OrdinalIgnoreCase))
                        normGen = normGen.Substring("items.mg.".Length);
                    if (_modGenerators.TryGetValue(normGen, out var modRefs) && modRefs.Count > 0)
                    {
                        perSlotModRefs.Add((slot, modRefs[0]));
                    }
                }
                if (perSlotModRefs.Count == 0) continue;

                var statResolved = ResolveMods(perSlotModRefs.Select(modRefEntry => (modRefEntry.Slot, modRefEntry.ModRef)).ToList());
                if (statResolved.Count > 0)
                {
                    InsertResolvedMods(conn, itemPalPath, statResolved);
                    igStatCount += statResolved.Count;
                }

                InsertWireMods(conn, itemPalPath, perSlotModRefs);
                igWireCount += perSlotModRefs.Count;
                igItemCount++;
            }

            int wrapItemCount = 0, wrapStatCount = 0, wrapWireCount = 0;
            foreach (var entry in _wrapperIGEntries)
            {
                string targetIGName = entry.TargetIGRef.Split('.').Last();
                if (!TryReadGcSource(targetIGName + ".gc", out var targetSource)) continue;

                var targetItems = ParseDirectItemEntries(targetSource.Text);
                if (targetItems.Count == 0) continue;

                var perSlotModRefs = new List<(int Slot, string ModRef)>();
                foreach (var (slot, generatorPath) in entry.Generators)
                {
                    string normGen = generatorPath;
                    if (normGen.StartsWith("items.mg.", StringComparison.OrdinalIgnoreCase))
                        normGen = normGen.Substring("items.mg.".Length);
                    if (_modGenerators.TryGetValue(normGen, out var modRefs) && modRefs.Count > 0)
                        perSlotModRefs.Add((slot, modRefs[0]));
                }
                if (perSlotModRefs.Count == 0) continue;

                var statResolved = ResolveMods(perSlotModRefs.Select(modRefEntry => (modRefEntry.Slot, modRefEntry.ModRef)).ToList());
                foreach (var targetPalPath in targetItems)
                {
                    string compositeKey = $"{targetPalPath}:{entry.Rarity}".ToLowerInvariant();
                    if (statResolved.Count > 0)
                    {
                        InsertResolvedMods(conn, compositeKey, statResolved);
                        wrapStatCount += statResolved.Count;
                    }
                    InsertWireMods(conn, compositeKey, perSlotModRefs);
                    wrapWireCount += perSlotModRefs.Count;
                    wrapItemCount++;
                }
            }

            transaction.Commit();
            Debug.LogError($"[ITEM-STAT-DB] phase=5 directItemItems={igItemCount} statMods={igStatCount} wireMods={igWireCount} phase5cWrapperRows={wrapItemCount} phase5cStatMods={wrapStatCount} phase5cWireMods={wrapWireCount}");

            _attrMap.Clear();
            _modPalRefs.Clear();
            _weaponMythicRefs.Clear();
            _directItemIGEntries.Clear();
            _directItemIGCountByRarity.Clear();
            _wrapperIGEntries.Clear();
            _modGenerators.Clear();
            _modGeneratorParents.Clear();
        }


        private void ParseAttributesPAL()
        {
            if (!TryReadGcSource("AttributesPAL.gc", out var source)) { Debug.LogError("[ITEM-STAT-DB] reason=attributes-pal-missing file=AttributesPAL.gc"); return; }

            string content = source.Text.Replace("\r", "");
            string currentName = null, currentPool = null;

            foreach (string line in content.Split('\n'))
            {
                string t = line.Trim();
                var poolMatch = Regex.Match(t, @"^(\w+)\s+extends\s+PoolTables\.(\w+)");
                if (poolMatch.Success) { currentName = poolMatch.Groups[1].Value; currentPool = poolMatch.Groups[2].Value; continue; }
                var attributeMatch = Regex.Match(t, @"Attribute\s*=\s*(\w+);");
                if (attributeMatch.Success && currentName != null) { _attrMap[currentName] = (attributeMatch.Groups[1].Value, currentPool); currentName = null; }
            }
        }


        private void ParseAllModPALs()
        {
            foreach (var source in EnumerateGcSources("*ModPAL.gc"))
            {
                string fileName = source.Stem;
                if (fileName == "WeaponMythicModPAL") continue;
                ParseSingleModPAL(source.Text, fileName);
            }
        }

        private void ParseSingleModPAL(string sourceText, string fileName)
        {
            string content = sourceText.Replace("\r", "");
            string currentQuality = null;
            string pendingSection = null;

            foreach (string line in content.Split('\n'))
            {
                string t = line.Trim();

                if (!t.Contains("extends") && !t.Contains("=") && !t.Contains("//") && !t.StartsWith("*"))
                {
                    if (t == "{" && pendingSection != null)
                    {
                        currentQuality = pendingSection;
                        pendingSection = null;
                        continue;
                    }

                    if (t.EndsWith("{") && !t.Contains("Description"))
                    {
                        string section = t.TrimEnd('{', ' ', '\t');
                        if (section.Length > 0 && section.Length < 30)
                            currentQuality = section;
                        continue;
                    }

                    var sectionMatch = Regex.Match(t, @"^(\w+)$");
                    if (sectionMatch.Success && t.Length < 30 && !t.Contains("Description"))
                        pendingSection = t;
                }

                var modMatch = Regex.Match(t, @"^(Mod\d+)\s+extends\s+(\S+)");
                if (modMatch.Success && currentQuality != null)
                {
                    string key = $"{fileName}|{currentQuality}|{modMatch.Groups[1].Value}";
                    _modPalRefs[key] = modMatch.Groups[2].Value.TrimEnd('{', ' ');
                }
            }
        }


        private void ParseWeaponMythicModPAL()
        {
            if (!TryReadGcSource("WeaponMythicModPAL.gc", out var source)) return;

            string content = source.Text.Replace("\r", "");
            foreach (string line in content.Split('\n'))
            {
                string t = line.Trim();
                var enhancementMatch = Regex.Match(t, @"^(\w+)\s+extends\s+(\d*EnhancementsPAL\.\w+)");
                if (enhancementMatch.Success)
                    _weaponMythicRefs[enhancementMatch.Groups[1].Value] = enhancementMatch.Groups[2].Value;
            }
        }


        private List<(string Name, List<(int Slot, string Ref)>)> ParseItemFile(string sourceText, string fileName, bool prebuiltOnly)
        {
            var result = new List<(string, List<(int, string)>)>();
            string content = sourceText.Replace("\r", "");
            string[] lines = content.Split('\n');

            string currentItem = null;
            var currentMods = new List<(int, string)>();

            foreach (string rawLine in lines)
            {
                string t = rawLine.Trim();
                if (t.StartsWith("//") || t.StartsWith("/*")) continue;

                var itemMatch = Regex.Match(t, @"^(\w+)\s+extends\s+\S+");
                if (itemMatch.Success)
                {
                    string name = itemMatch.Groups[1].Value;
                    bool isPrebuilt = name.StartsWith("MythicPreBuilt", StringComparison.OrdinalIgnoreCase);
                    bool isNamedMythic = !isPrebuilt && !name.StartsWith("Mod") && !name.StartsWith("Description") &&
                        !name.StartsWith("One") && !name.StartsWith("Two") && !name.StartsWith("Three") &&
                        !name.StartsWith("Four") && !name.StartsWith("Five") && !name.StartsWith("static") &&
                        !name.StartsWith("Base") && name != fileName;

                    bool shouldParse = prebuiltOnly ? isPrebuilt : isNamedMythic;
                    if (shouldParse)
                    {
                        if (currentItem != null && currentMods.Count > 0)
                            result.Add((currentItem, new List<(int, string)>(currentMods)));
                        currentItem = name;
                        currentMods.Clear();
                    }
                }

                if (currentItem != null)
                {
                    var modMatch = Regex.Match(t, @"^Mod(\d+)\s+extends\s+(\S+)");
                    if (modMatch.Success)
                    {
                        int slot = int.Parse(modMatch.Groups[1].Value);
                        string extendsRef = modMatch.Groups[2].Value.TrimEnd('{', ' ');
                        if (!extendsRef.Contains("ItemModifier"))
                            currentMods.Add((slot, extendsRef));
                    }
                }
            }

            if (currentItem != null && currentMods.Count > 0)
                result.Add((currentItem, currentMods));

            return result;
        }


        private void ParseAllIGFiles()
        {
            string[] rarityPrefixes = { "Rare", "Unique", "Magic", "Superior", "Mythic" };
            foreach (var prefix in rarityPrefixes)
            {
                _directItemIGCountByRarity[prefix] = 0;
                foreach (var source in EnumerateGcSources(prefix + "*IG.gc"))
                    ParseSingleIGFile(source.Text, prefix);
            }
        }

        private void ParseSingleIGFile(string sourceText, string fileRarity)
        {
            string content = sourceText.Replace("\r", "");
            string currentItemPalPath = null;
            string currentItemGenRef = null;
            var currentGenerators = new List<(int, string)>();

            void Flush()
            {
                if (currentGenerators.Count > 0)
                {
                    if (currentItemPalPath != null)
                    {
                        _directItemIGEntries[currentItemPalPath] = new List<(int, string)>(currentGenerators);
                        _directItemIGCountByRarity[fileRarity]++;
                    }
                    else if (currentItemGenRef != null)
                    {
                        _wrapperIGEntries.Add((fileRarity, currentItemGenRef, new List<(int, string)>(currentGenerators)));
                    }
                }
                currentItemPalPath = null;
                currentItemGenRef = null;
                currentGenerators.Clear();
            }

            foreach (string rawLine in content.Split('\n'))
            {
                string t = rawLine.Trim();
                if (t.StartsWith("//") || t.StartsWith("/*")) continue;

                if (Regex.IsMatch(t, @"^\w+\s+extends\s+(ItemTimeline\.\w+|RandomItemGenerator|SingleItemGenerator)", RegexOptions.IgnoreCase))
                {
                    Flush();
                    continue;
                }

                var itemMatch = Regex.Match(t, @"^Item\s*=\s*([^;\s]+)");
                if (itemMatch.Success)
                {
                    string rawItem = itemMatch.Groups[1].Value.ToLowerInvariant();
                    if (rawItem.StartsWith("items.pal."))
                        rawItem = rawItem.Substring("items.pal.".Length);
                    currentItemPalPath = rawItem;
                    continue;
                }

                var wrapperMatch = Regex.Match(t, @"^ItemGenerator\s*=\s*([^;\s]+)");
                if (wrapperMatch.Success)
                {
                    currentItemGenRef = wrapperMatch.Groups[1].Value;
                    continue;
                }

                var genMatch = Regex.Match(t, @"^ItemModGenerator(\d+)\s*=\s*([^;\s]+)");
                if (genMatch.Success)
                {
                    int slot = int.Parse(genMatch.Groups[1].Value);
                    currentGenerators.Add((slot, genMatch.Groups[2].Value));
                }
            }

            Flush();
        }

        private List<string> ParseDirectItemEntries(string sourceText)
        {
            var result = new List<string>();
            string content = sourceText.Replace("\r", "");
            foreach (string rawLine in content.Split('\n'))
            {
                string t = rawLine.Trim();
                if (t.StartsWith("//") || t.StartsWith("/*")) continue;
                var itemMatch = Regex.Match(t, @"^Item\s*=\s*([^;\s]+)");
                if (itemMatch.Success)
                {
                    string raw = itemMatch.Groups[1].Value.ToLowerInvariant();
                    if (raw.StartsWith("items.pal."))
                        raw = raw.Substring("items.pal.".Length);
                    result.Add(raw);
                }
            }
            return result;
        }


        private void ParseAllMGFiles()
        {
            foreach (var source in EnumerateGcSources("*MG.gc"))
            {
                string fileName = source.Stem;
                if (fileName.EndsWith("IG", StringComparison.OrdinalIgnoreCase)) continue;

                string content = source.Text.Replace("\r", "");
                string currentSection = null;
                var currentMods = new List<string>();

                foreach (string rawLine in content.Split('\n'))
                {
                    string t = rawLine.Trim();
                    if (t.StartsWith("//") || t.StartsWith("/*")) continue;

                    void FlushPrevious()
                    {
                        if (currentSection != null)
                            _modGenerators[$"{fileName}.{currentSection}"] = new List<string>(currentMods);
                    }

                    var sectionMatch = Regex.Match(t, @"^(\w+)\s+extends\s+ItemModifierGeneratorTable");
                    if (sectionMatch.Success)
                    {
                        FlushPrevious();
                        currentSection = sectionMatch.Groups[1].Value;
                        currentMods.Clear();
                        continue;
                    }

                    var inheritMatch = Regex.Match(t, @"^(\w+)\s+extends\s+items\.mg\.(\w+)\.(\w+)");
                    if (inheritMatch.Success)
                    {
                        FlushPrevious();
                        currentSection = inheritMatch.Groups[1].Value;
                        currentMods.Clear();
                        _modGeneratorParents[$"{fileName}.{currentSection}"] = $"{inheritMatch.Groups[2].Value}.{inheritMatch.Groups[3].Value}";
                        continue;
                    }

                    var modMatch = Regex.Match(t, @"^ItemModifier\s*=\s*([^;\s]+)");
                    if (modMatch.Success && currentSection != null)
                        currentMods.Add(modMatch.Groups[1].Value);
                }

                if (currentSection != null)
                    _modGenerators[$"{fileName}.{currentSection}"] = new List<string>(currentMods);
            }

            ResolveModGeneratorInheritance();
        }

        private void ResolveModGeneratorInheritance()
        {
            foreach (var key in _modGeneratorParents.Keys.ToList())
            {
                if (_modGenerators.TryGetValue(key, out var own) && own.Count > 0) continue;

                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { key };
                string parent = _modGeneratorParents[key];
                while (parent != null && !visited.Contains(parent))
                {
                    visited.Add(parent);
                    if (_modGenerators.TryGetValue(parent, out var parentMods) && parentMods.Count > 0)
                    {
                        _modGenerators[key] = new List<string>(parentMods);
                        break;
                    }
                    _modGeneratorParents.TryGetValue(parent, out parent);
                }
            }
        }


        private List<ResolvedMod> ResolveMods(List<(int Slot, string Ref)> mods)
        {
            var result = new List<ResolvedMod>();

            foreach (var (slot, rawRef) in mods)
            {
                string modRef = rawRef.Replace("items.modpal.", "").Replace("items.pal.", "");
                string enhancementRef = ResolveToEnhancement(modRef);
                if (enhancementRef == null) continue;

                var enhMatch = Regex.Match(enhancementRef, @"(\d*)EnhancementsPAL\.(\w+)");
                if (!enhMatch.Success) continue;

                int enhCount = string.IsNullOrEmpty(enhMatch.Groups[1].Value) ? 1 : int.Parse(enhMatch.Groups[1].Value);
                string enhName = enhMatch.Groups[2].Value;
                float valueMult = enhCount > 0 ? 1.0f / enhCount : 1.0f;

                if (enhCount == 3) valueMult = 0.33f;
                else if (enhCount == 4) valueMult = 0.25f;
                else if (enhCount == 2) valueMult = 0.5f;

                var attrNames = SplitEnhancementName(enhName, enhCount);
                foreach (string attrName in attrNames)
                {
                    if (_attrMap.TryGetValue(attrName, out var mapping))
                    {
                        result.Add(new ResolvedMod
                        {
                            ModSlot = slot,
                            Attribute = mapping.Attr,
                            Pool = mapping.Pool,
                            ValueMult = valueMult
                        });
                    }
                    else
                    {
                        Debug.LogWarning($"[ITEM-STAT-DB] reason=unknown-attribute attribute={attrName} source={enhName}");
                    }
                }
            }

            return result;
        }

        private string ResolveToEnhancement(string modRef)
        {
            if (modRef.Contains("EnhancementsPAL")) return modRef;
            if (modRef.Contains("ItemModifier") || modRef.Contains("ProcModPAL")) return null;

            if (modRef.StartsWith("WeaponMythicModPAL.", StringComparison.OrdinalIgnoreCase))
            {
                string name = modRef.Substring("WeaponMythicModPAL.".Length);
                return _weaponMythicRefs.TryGetValue(name, out string enhancementRef) ? enhancementRef : null;
            }

            string[] parts = modRef.Split('.');
            if (parts.Length >= 3)
            {
                string key = $"{parts[0]}|{parts[1]}|{parts[2]}";
                if (_modPalRefs.TryGetValue(key, out string enhRef))
                    return enhRef.Contains("EnhancementsPAL") ? enhRef : ResolveToEnhancement(enhRef);
            }

            return null;
        }

        private List<string> SplitEnhancementName(string name, int expectedCount)
        {
            var result = new List<string>();
            if (expectedCount <= 1) { result.Add(name); return result; }

            string remaining = name;
            while (remaining.Length > 0 && result.Count < expectedCount)
            {
                bool found = false;
                for (int len = remaining.Length; len > 0; len--)
                {
                    string candidate = remaining.Substring(0, len);
                    if (_attrMap.ContainsKey(candidate))
                    {
                        result.Add(candidate);
                        remaining = remaining.Substring(len);
                        if (remaining.StartsWith("_")) remaining = remaining.Substring(1);
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    int separatorIndex = remaining.IndexOf('_');
                    if (separatorIndex > 0)
                    {
                        string attributePart = remaining.Substring(0, separatorIndex);
                        if (_attrMap.ContainsKey(attributePart)) result.Add(attributePart);
                        remaining = remaining.Substring(separatorIndex + 1);
                    }
                    else
                    {
                        if (_attrMap.ContainsKey(remaining)) result.Add(remaining);
                        break;
                    }
                }
            }

            return result;
        }


        private void CreateTables(SqliteConnection conn)
        {
            using var command = conn.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS item_resolved_mods (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    full_gc_key TEXT NOT NULL,
                    mod_slot INTEGER NOT NULL,
                    attribute TEXT NOT NULL,
                    pool_name TEXT NOT NULL,
                    value_mult REAL NOT NULL,
                    UNIQUE(full_gc_key, mod_slot, attribute));
                CREATE INDEX IF NOT EXISTS idx_item_mods_key ON item_resolved_mods(full_gc_key);
                CREATE TABLE IF NOT EXISTS item_wire_mods (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    full_gc_key TEXT NOT NULL,
                    mod_slot INTEGER NOT NULL,
                    mod_ref TEXT NOT NULL,
                    UNIQUE(full_gc_key, mod_slot));
                CREATE INDEX IF NOT EXISTS idx_item_wire_mods_key ON item_wire_mods(full_gc_key);";
            command.ExecuteNonQuery();
        }

        private void InsertResolvedMods(SqliteConnection conn, string fullKey, List<ResolvedMod> mods)
        {
            using var command = conn.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO item_resolved_mods (full_gc_key,mod_slot,attribute,pool_name,value_mult) VALUES(@k,@s,@a,@p,@v)";
            var keyParameter = command.Parameters.Add("@k", System.Data.DbType.String);
            var slotParameter = command.Parameters.Add("@s", System.Data.DbType.Int32);
            var attributeParameter = command.Parameters.Add("@a", System.Data.DbType.String);
            var poolParameter = command.Parameters.Add("@p", System.Data.DbType.String);
            var valueParameter = command.Parameters.Add("@v", System.Data.DbType.Double);

            keyParameter.Value = fullKey;
            foreach (var mod in mods)
            {
                slotParameter.Value = mod.ModSlot; attributeParameter.Value = mod.Attribute; poolParameter.Value = mod.Pool; valueParameter.Value = mod.ValueMult;
                command.ExecuteNonQuery();
            }
        }

        private void InsertWireMods(SqliteConnection conn, string fullKey, List<(int Slot, string ModRef)> wireMods)
        {
            using var command = conn.CreateCommand();
            command.CommandText = "INSERT OR REPLACE INTO item_wire_mods (full_gc_key,mod_slot,mod_ref) VALUES(@k,@s,@r)";
            var keyParameter = command.Parameters.Add("@k", System.Data.DbType.String);
            var slotParameter = command.Parameters.Add("@s", System.Data.DbType.Int32);
            var modRefParameter = command.Parameters.Add("@r", System.Data.DbType.String);
            keyParameter.Value = fullKey;
            foreach (var (slot, modRef) in wireMods)
            {
                slotParameter.Value = slot;
                modRefParameter.Value = modRef;
                command.ExecuteNonQuery();
            }
        }

        private static Dictionary<string, PoolFormula> DefaultPoolCurves()
        {
            return new Dictionary<string, PoolFormula>
            {
                ["MaxPointBonusPool"] = new PoolFormula { Points = new[] { (1, 50f), (110, 8000f) } },
                ["PrimaryAttributeBonusPool"] = new PoolFormula { Points = new[] { (1, 20f), (110, 1110f) } },
                ["DamageBonusPool"] = new PoolFormula { Points = new[] { (1, 10f), (110, 882f) } },
                ["DamageModPool"] = new PoolFormula { Points = new[] { (1, 5f), (100, 20f), (125, 25f) } },
                ["AttackRatingBonusPool"] = new PoolFormula { Points = new[] { (1, 32f), (110, 3520f) } },
                ["AttackDefenseRatingModPool"] = new PoolFormula { Points = new[] { (1, 10f), (100, 50f), (105, 55f) } },
                ["DefenseRatingBonusPool"] = new PoolFormula { Points = new[] { (1, 19f), (110, 1550f) } },
                ["DamageResistBonusPool"] = new PoolFormula { Points = new[] { (1, 26f), (110, 2860f) } },
                ["SpeedModPool"] = new PoolFormula { Points = new[] { (1, 10f), (100, 40f), (105, 45f) } },
                ["SizeModPool"] = new PoolFormula { Points = new[] { (1, 20f), (100, 50f), (110, 55f) } },
                ["BlockModPool"] = new PoolFormula { Points = new[] { (1, 3f), (100, 13f), (110, 15f) } },
                ["CriticalHitModPool"] = new PoolFormula { Points = new[] { (1, 100f), (110, 544f) } },
                ["StealBonusPool"] = new PoolFormula { Points = new[] { (1, 10f), (100, 20f), (115, 25f) } },
                ["RegenModPool"] = new PoolFormula { Points = new[] { (1, 10f), (100, 100f), (110, 105f) } },
                ["StunBonusPool"] = new PoolFormula { Points = new[] { (1, 20f), (100, 90f), (120, 95f) } },
                ["DamageReflectBonusPool"] = new PoolFormula { Points = new[] { (1, 50f), (110, 380f) } },
            };
        }

        private void LoadResolvedMods(SqliteConnection conn)
        {
            _itemMods.Clear();
            using var command = conn.CreateCommand();
            command.CommandText = "SELECT full_gc_key,mod_slot,attribute,pool_name,value_mult FROM item_resolved_mods";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string key = reader.GetString(0);
                if (!_itemMods.TryGetValue(key, out var list)) { list = new List<ResolvedMod>(); _itemMods[key] = list; }
                list.Add(new ResolvedMod { ModSlot = reader.GetInt32(1), Attribute = reader.GetString(2), Pool = reader.GetString(3), ValueMult = reader.GetFloat(4) });
            }
        }

        private void LoadWireMods(SqliteConnection conn)
        {
            _itemWireMods.Clear();
            using var command = conn.CreateCommand();
            command.CommandText = "SELECT full_gc_key,mod_slot,mod_ref FROM item_wire_mods ORDER BY full_gc_key, mod_slot";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string key = reader.GetString(0);
                if (!_itemWireMods.TryGetValue(key, out var list))
                {
                    list = new List<(int, string)>();
                    _itemWireMods[key] = list;
                }
                list.Add((reader.GetInt32(1), reader.GetString(2)));
            }
        }

        private void LoadGCDictionary()
        {
            _gcClassNames.Clear();
            string path = Path.Combine(_gcDir, "..", "GCDictionary.dict");
            if (!File.Exists(path))
            {
                Debug.LogError($"[ITEM-STAT-DB] reason=gc-dictionary-missing path={path} phase=2");
                return;
            }
            using var sr = new StreamReader(path);
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                int sp = line.IndexOf(' ');
                if (sp <= 0 || sp >= line.Length - 1) continue;
                string name = line.Substring(sp + 1).Trim();
                if (name.Length > 0)
                    _gcClassNames.Add(name);
            }
        }

        public uint GetGCClassHash(string className)
        {
            if (!IsLoaded || string.IsNullOrEmpty(className)) return 0;
            if (_gcClassNames.Contains(className))
                return ComputeDJB2(className);
            if (className.StartsWith("items.modpal.", StringComparison.OrdinalIgnoreCase))
            {
                string stripped = className.Substring("items.modpal.".Length);
                if (_gcClassNames.Contains(stripped))
                    return ComputeDJB2(stripped);
            }
            else
            {
                string prefixed = "items.modpal." + className;
                if (_gcClassNames.Contains(prefixed))
                    return ComputeDJB2(prefixed);
            }
            return 0;
        }

        private static uint ComputeDJB2(string s)
        {
            uint h = 5381;
            foreach (char c in s.ToLowerInvariant()) h = h * 33 + (uint)c;
            return h;
        }

        public bool TryGetItemReadDataSlotCount(string gcClass, out int slotCount)
        {
            slotCount = 0;
            if (string.IsNullOrWhiteSpace(gcClass))
                return false;

            string key = NormalizeGCClass(gcClass);
            if (_itemReadDataSlotCounts.TryGetValue(key, out slotCount))
                return true;
            if (_itemReadDataSlotMisses.Contains(key))
                return false;

            GCNode node = ResolveItemReadDataNode(key, out string resolvedKey);
            if (node == null)
            {
                _itemReadDataSlotMisses.Add(key);
                return false;
            }

            int childSlots = CountItemReadDataModifierChildren(node);
            slotCount = Math.Max(1, 1 + childSlots);
            _itemReadDataSlotCounts[key] = slotCount;
            if (!string.IsNullOrWhiteSpace(resolvedKey))
                _itemReadDataSlotCounts[resolvedKey] = slotCount;
            return true;
        }

        private GCNode ResolveItemReadDataNode(string key, out string resolvedKey)
        {
            resolvedKey = key;
            var gc = GCDatabase.Instance;
            if (gc == null || !gc.IsLoaded)
                return null;

            foreach (string candidate in GetItemReadDataCandidates(key))
            {
                var node = gc.ResolveWithInheritance(candidate);
                if (node != null)
                {
                    resolvedKey = NormalizeGCClass(candidate);
                    return node;
                }
            }
            return null;
        }

        private IEnumerable<string> GetItemReadDataCandidates(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                yield break;

            string normalized = key.Replace('\\', '.').Replace('/', '.').Trim();
            yield return normalized;

            if (normalized.StartsWith("items.pal.", StringComparison.OrdinalIgnoreCase))
            {
                yield return normalized.Substring("items.pal.".Length);
            }
            else
            {
                yield return "items.pal." + normalized;
            }
        }

        private int CountItemReadDataModifierChildren(GCNode node)
        {
            int count = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string childName in node.ChildOrder)
            {
                if (!seen.Add(childName))
                    continue;
                if (node.Children.TryGetValue(childName, out var child) && IsItemReadDataModifierChild(child))
                    count++;
            }

            foreach (var childEntry in node.Children)
            {
                if (!seen.Add(childEntry.Key))
                    continue;
                if (IsItemReadDataModifierChild(childEntry.Value))
                    count++;
            }

            foreach (var child in node.AnonymousChildren)
            {
                if (IsItemReadDataModifierChild(child))
                    count++;
            }

            return count;
        }

        private bool IsItemReadDataModifierChild(GCNode node)
        {
            if (node == null || string.Equals(node.Name, "Description", StringComparison.OrdinalIgnoreCase))
                return false;
            return ExtendsItemModifier(node.Extends, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        private bool ExtendsItemModifier(string typeName, HashSet<string> visited)
        {
            if (IsItemModifierBase(typeName))
                return true;
            if (string.IsNullOrWhiteSpace(typeName))
                return false;

            string key = typeName.Trim().TrimEnd('{', ';');
            if (!visited.Add(key))
                return false;

            var gc = GCDatabase.Instance;
            if (gc == null || !gc.IsLoaded)
                return false;

            GCNode parent = gc.Resolve(key);
            if (parent == null && key.StartsWith("items.modpal.", StringComparison.OrdinalIgnoreCase))
                parent = gc.Resolve(key.Substring("items.modpal.".Length));
            if (parent == null && key.StartsWith("items.pal.", StringComparison.OrdinalIgnoreCase))
                parent = gc.Resolve(key.Substring("items.pal.".Length));
            if (parent == null)
                return false;

            if (IsItemModifierBase(parent.Name) || IsItemModifierBase(parent.Extends))
                return true;
            return ExtendsItemModifier(parent.Extends, visited);
        }

        private static bool IsItemModifierBase(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return false;
            string leaf = typeName.Trim();
            int dot = leaf.LastIndexOf('.');
            if (dot >= 0)
                leaf = leaf.Substring(dot + 1);
            return leaf.Equals("ItemModifier", StringComparison.OrdinalIgnoreCase) ||
                   leaf.Equals("ItemAttributeModifier", StringComparison.OrdinalIgnoreCase);
        }


        public Dictionary<string, int> GetItemStats(string gcClass, int playerLevel, int slotDivisor = 8)
        {
            return GetItemStatsAtItemLevel(gcClass, playerLevel + 3, slotDivisor);
        }

        public Dictionary<string, int> GetItemStatsAtItemLevel(string gcClass, int itemLevel, int slotDivisor = 8, int rarity = -1)
        {
            var result = new Dictionary<string, int>();
            if (!IsLoaded || string.IsNullOrEmpty(gcClass)) return result;

            string key = ResolveResolvedModKey(gcClass, rarity);
            if (!_itemMods.TryGetValue(key, out var mods)) return result;

            itemLevel = Math.Max(1, itemLevel);

            foreach (var mod in mods)
            {
                if (!_pools.TryGetValue(mod.Pool, out var pool)) continue;
                float poolValue = pool.Eval(itemLevel);
                int bonus = (int)(poolValue * mod.ValueMult / slotDivisor);

                if (result.ContainsKey(mod.Attribute))
                    result[mod.Attribute] += bonus;
                else
                    result[mod.Attribute] = bonus;
            }

            return result;
        }

        public (int hp, int endurance, int mana) GetItemHPStats(string gcClass, int playerLevel)
        {
            var stats = GetItemStats(gcClass, playerLevel);
            stats.TryGetValue("MAX_HIT_POINTS", out int hp);
            stats.TryGetValue("ENDURANCE", out int end);
            stats.TryGetValue("MAX_MANA_POINTS", out int mana);
            return (hp, end, mana);
        }

        public List<(int Slot, string ModRef)> GetItemWireMods(string gcClass)
        {
            if (!IsLoaded || string.IsNullOrEmpty(gcClass)) return new List<(int, string)>();
            string key = NormalizeGCClass(gcClass);
            if (_itemWireMods.TryGetValue(key, out var list))
                return new List<(int, string)>(list);
            return new List<(int, string)>();
        }

        public List<(int Slot, string ModRef)> GetWrapperIGWireMods(string gcClass, string rarity)
        {
            if (!IsLoaded || string.IsNullOrEmpty(gcClass) || string.IsNullOrEmpty(rarity)) return new List<(int, string)>();
            string normalized = NormalizeGCClass(gcClass);
            string authoredRarity = NormalizeAuthoredRarityName(rarity);
            string key = string.IsNullOrEmpty(authoredRarity) ? "" : $"{normalized}:{authoredRarity}";
            if (_itemWireMods.TryGetValue(key, out var list))
                return new List<(int, string)>(list);
            return new List<(int, string)>();
        }

        public bool HasItem(string gcClass, int rarity = -1)
        {
            if (!IsLoaded) return false;
            return _itemMods.ContainsKey(ResolveResolvedModKey(gcClass, rarity));
        }

        public List<string> GetItemAttributes(string gcClass, int rarity = -1)
        {
            string key = ResolveResolvedModKey(gcClass, rarity);
            if (_itemMods.TryGetValue(key, out var mods))
                return mods.Select(mod => mod.Attribute).Distinct().ToList();
            return new List<string>();
        }


        private string NormalizeGCClass(string gcClass)
        {
            string lower = gcClass.ToLowerInvariant();
            if (lower.StartsWith("items.pal."))
                lower = lower.Substring("items.pal.".Length);
            return lower;
        }

        private static string NormalizeAuthoredRarityName(string rarity)
        {
            if (string.IsNullOrWhiteSpace(rarity)) return null;
            string normalizedRarity = rarity.Trim().ToLowerInvariant();
            return normalizedRarity == "magical" ? "magic" : normalizedRarity;
        }

        private static string NormalizeAuthoredRarityName(int rarity)
        {
            return rarity switch
            {
                0 => "normal",
                1 => "superior",
                2 => "magic",
                3 => "rare",
                4 => "unique",
                5 => "mythic",
                _ => null
            };
        }

        private string ResolveResolvedModKey(string gcClass, int rarity)
        {
            string key = NormalizeGCClass(gcClass);
            string authoredRarity = NormalizeAuthoredRarityName(rarity);
            if (!string.IsNullOrEmpty(authoredRarity))
            {
                string compositeKey = $"{key}:{authoredRarity}";
                if (_itemMods.ContainsKey(compositeKey))
                    return compositeKey;
            }
            return key;
        }

        public static string ExtractPattern(string gcClass)
        {
            if (string.IsNullOrEmpty(gcClass)) return "";
            string[] parts = gcClass.Split('.');
            return parts.Length > 0 ? parts[parts.Length - 1] : gcClass;
        }
    }
}
