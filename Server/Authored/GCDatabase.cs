using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DungeonRunners.Engine;

namespace DungeonRunners.Data
{
    public class GCDatabase
    {
        private static GCDatabase _instance;
        public static GCDatabase Instance => _instance ??= new GCDatabase();

        private Dictionary<string, GCNode> _nodes = new Dictionary<string, GCNode>(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, GCNode> _pathRegistry = new Dictionary<string, GCNode>(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, GCNode> _resolvedCache = new Dictionary<string, GCNode>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, List<(int levelF32, int valueF32)>> _curveCache = new Dictionary<string, List<(int, int)>>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string> _pathAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "creatures.whiskers.broodling.Basic.Grunt", "Whisker_BroodlingBase_Grunt" },
            { "creatures.whiskers.blademaster.Basic.Grunt", "Whisker_BlademasterBase_Grunt" }
        };


        public GCNode GlobalKnobs => GetNode("GlobalKnobs");
        public GCNode Tables => GetNode("Tables");

        public bool IsLoaded { get; private set; }
        public int FileCount { get; private set; }
        public int FlatFileCount { get; private set; }
        public int PackageFileCount { get; private set; }
        public int NodeCount => _pathRegistry.Count;
        public IEnumerable<string> RegisteredPaths => _pathRegistry.Keys.ToArray();


        public void Load(string directoryPath)
        {
            IsLoaded = false;
            if (!Directory.Exists(directoryPath))
            {
                Debug.LogError($"[GC-DATABASE] directoryNotFound path='{directoryPath}'");
                throw new DirectoryNotFoundException(directoryPath);
            }

            _nodes.Clear();
            _pathRegistry.Clear();
            _resolvedCache.Clear();
            _curveCache.Clear();
            _xpCurve = null;

            string[] files = Directory.GetFiles(directoryPath, "*.gc");
            FlatFileCount = files.Length;
            PackageFileCount = 0;
            FileCount = FlatFileCount;
            if (FlatFileCount == 0)
                throw new InvalidDataException($"No GC files found in {directoryPath}");
            int parseErrors = 0;

            foreach (string file in files)
            {
                try
                {
                    GCNode node = GcParser.ParseFile(file);
                    if (node != null && !string.IsNullOrEmpty(node.Name))
                    {
                        string fileName = Path.GetFileNameWithoutExtension(file);
                        RegisterParsedNode(node, ResolveRegistryName(node, fileName), null, true);
                    }
                }
                catch (Exception ex)
                {
                    parseErrors++;
                    if (parseErrors <= 10)
                        Debug.LogError($"[GC-DATABASE] parseError file='{Path.GetFileName(file)}' message='{ex.Message}'");
                }
            }

            try
            {
                var packageCatalog = PackageCatalog.Instance;
                if (!packageCatalog.IsLoaded)
                    packageCatalog.LoadFromAssets();
                if (packageCatalog.IsLoaded)
                {
                    foreach (var doc in packageCatalog.GcTextDocuments)
                    {
                        if (doc == null || string.IsNullOrWhiteSpace(doc.Text))
                            continue;
                        try
                        {
                            GCNode node = GcParser.Parse(doc.Text, doc.Stem);
                            if (node == null || string.IsNullOrEmpty(node.Name))
                                continue;
                            RegisterParsedNode(node, ResolveRegistryName(node, doc.Stem), doc.GcPath, false);
                            PackageFileCount++;
                        }
                        catch (Exception ex)
                        {
                            parseErrors++;
                            if (parseErrors <= 10)
                                Debug.LogError($"[GC-DATABASE] packageParseError doc='{doc.Name}' message='{ex.Message}'");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GC-DATABASE] packageLoadFailed message='{ex.Message}'");
            }
            FileCount = FlatFileCount + PackageFileCount;

            Debug.LogError($"[GC-DATABASE] loadSummary phase=start");
            Debug.LogError($"[GC-DATABASE] files={FileCount} flat={FlatFileCount} packageGc={PackageFileCount} paths={_pathRegistry.Count}");
            if (parseErrors > 0)
            {
                Debug.LogError($"[GC-DATABASE] parseErrors={parseErrors}");
                throw new InvalidDataException($"GC-DATABASE parseErrors={parseErrors}");
            }

            if (GlobalKnobs == null)
                throw new InvalidDataException("GlobalKnobs.gc not found");
            else
                Debug.LogError($"[GC-DATABASE] globalKnobs weaponDamagePerLevel={GlobalKnobs.GetFloat("WeaponDamagePerLevel")} meleeDamagePerStrength={GlobalKnobs.GetFloat("MeleeDamagePerStrength")}");

            if (Tables == null)
                throw new InvalidDataException("Tables.gc not found");
            string[] requiredCurveTables =
            {
                "Experience",
                "MonsterHealth",
                "MonsterDamage",
                "MonsterAttackRating",
                "MonsterDefenseRating",
                "ReSpecCost"
            };
            foreach (string tableName in requiredCurveTables)
            {
                var table = Tables.GetChild(tableName);
                if (table == null || table.AnonymousChildren.Count == 0)
                    throw new InvalidDataException($"Tables.{tableName} not found");
            }

            IsLoaded = true;
            Debug.LogError($"[GC-DATABASE] loadSummary phase=end");
        }

        private static string ResolveRegistryName(GCNode node, string fallbackName)
        {
            string name = node != null ? node.Name : "";
            return string.Equals(name, "*", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(name)
                ? fallbackName
                : name;
        }

        private void RegisterParsedNode(GCNode node, string registryName, string packagePath, bool overwriteExisting)
        {
            if (node == null || string.IsNullOrWhiteSpace(registryName))
                return;

            RegisterNode(_nodes, registryName, node, overwriteExisting);
            RegisterPath(registryName, node, overwriteExisting);
            if (!string.Equals(node.Name, "*", StringComparison.Ordinal) &&
                !string.Equals(registryName, node.Name, StringComparison.OrdinalIgnoreCase))
                RegisterPath(node.Name, node, overwriteExisting);
            RegisterChildren(registryName, node, overwriteExisting);

            if (!string.IsNullOrWhiteSpace(packagePath))
            {
                RegisterNode(_nodes, packagePath, node, false);
                RegisterPath(packagePath, node, false);
                RegisterChildren(packagePath, node, false);
            }
        }

        private static void RegisterNode(Dictionary<string, GCNode> nodes, string key, GCNode node, bool overwriteExisting)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;
            if (overwriteExisting || !nodes.ContainsKey(key))
                nodes[key] = node;
        }

        private void RegisterPath(string key, GCNode node, bool overwriteExisting)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;
            if (overwriteExisting || !_pathRegistry.ContainsKey(key))
                _pathRegistry[key] = node;
        }

        private void RegisterChildren(string parentPath, GCNode parent, bool overwriteExisting)
        {
            foreach (var childEntry in parent.Children)
            {
                string childPath = parentPath + "." + childEntry.Key;
                RegisterPath(childPath, childEntry.Value, overwriteExisting);
                RegisterChildren(childPath, childEntry.Value, overwriteExisting);
            }
        }


        public GCNode GetNode(string nameOrPath)
        {
            if (_pathRegistry.TryGetValue(nameOrPath, out GCNode node))
                return node;
            return null;
        }

        public GCNode Resolve(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            if (_pathRegistry.TryGetValue(path, out GCNode exact))
                return exact;

            if (_pathAliases.TryGetValue(path, out string alias) && _pathRegistry.TryGetValue(alias, out GCNode aliased))
                return aliased;

            string lastSegment = path;
            int lastDot = path.LastIndexOf('.');
            if (lastDot >= 0)
                lastSegment = path.Substring(lastDot + 1);

            if (_pathRegistry.TryGetValue(lastSegment, out GCNode byLast))
                return byLast;

            if (lastDot > 0)
            {
                int prevDot = path.LastIndexOf('.', lastDot - 1);
                if (prevDot >= 0)
                {
                    string lastTwo = path.Substring(prevDot + 1);
                    if (_pathRegistry.TryGetValue(lastTwo, out GCNode byLastTwo))
                        return byLastTwo;
                }
            }

            return null;
        }

        public List<string> GetInheritanceChainPaths(string path, int maxDepth = 32)
        {
            var chain = new List<string>();
            if (string.IsNullOrWhiteSpace(path))
                return chain;

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string currentPath = path.Trim();
            for (int depth = 0; depth < maxDepth && !string.IsNullOrWhiteSpace(currentPath); depth++)
            {
                if (!visited.Add(currentPath))
                    break;
                GCNode node = ResolveExactOrAlias(currentPath);
                if (node == null)
                    break;
                chain.Add(currentPath);
                currentPath = node.Extends;
            }
            return chain;
        }

        private GCNode ResolveExactOrAlias(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            if (_pathRegistry.TryGetValue(path, out GCNode exact))
                return exact;
            if (_pathAliases.TryGetValue(path, out string alias) && _pathRegistry.TryGetValue(alias, out GCNode aliased))
                return aliased;
            return null;
        }

        public GCNode ResolveWithInheritance(string path)
        {
            if (_resolvedCache.TryGetValue(path, out GCNode cached))
                return cached;

            GCNode node = Resolve(path);
            if (node == null) return null;

            GCNode resolved = FlattenInheritance(node, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            _resolvedCache[path] = resolved;
            return resolved;
        }

        private GCNode FlattenInheritance(GCNode node, HashSet<string> visited)
        {
            string key = node.Name + "|" + (node.Extends ?? "");
            if (visited.Contains(key)) return node;
            visited.Add(key);

            if (string.IsNullOrEmpty(node.Extends))
                return node;

            GCNode parent = Resolve(node.Extends);
            if (parent == null)
                return node;

            GCNode resolvedParent = FlattenInheritance(parent, visited);

            var merged = new GCNode
            {
                Name = node.Name,
                Extends = node.Extends,
                IsStatic = node.IsStatic,
                IsAnonymous = node.IsAnonymous,
                SourceFile = node.SourceFile
            };

            foreach (var propertyEntry in resolvedParent.Properties)
                merged.Properties[propertyEntry.Key] = propertyEntry.Value;
            foreach (var propertyEntry in node.Properties)
                merged.Properties[propertyEntry.Key] = propertyEntry.Value;

            foreach (var childEntry in resolvedParent.Children)
                merged.Children[childEntry.Key] = childEntry.Value;
            foreach (string childName in resolvedParent.ChildOrder)
                if (!merged.ChildOrder.Contains(childName))
                    merged.ChildOrder.Add(childName);
            foreach (var childEntry in node.Children)
            {
                if (merged.Children.ContainsKey(childEntry.Key))
                {
                    merged.Children[childEntry.Key] = MergeNodes(merged.Children[childEntry.Key], childEntry.Value);
                }
                else
                {
                    merged.Children[childEntry.Key] = childEntry.Value;
                }
                if (!merged.ChildOrder.Contains(childEntry.Key))
                    merged.ChildOrder.Add(childEntry.Key);
            }

            merged.AnonymousChildren.AddRange(resolvedParent.AnonymousChildren);
            merged.AnonymousChildren.AddRange(node.AnonymousChildren);

            return merged;
        }

        private GCNode MergeNodes(GCNode parent, GCNode child)
        {
            var merged = new GCNode
            {
                Name = child.Name,
                Extends = child.Extends ?? parent.Extends,
                IsStatic = child.IsStatic || parent.IsStatic,
                IsAnonymous = child.IsAnonymous || parent.IsAnonymous,
                SourceFile = child.SourceFile
            };

            foreach (var propertyEntry in parent.Properties) merged.Properties[propertyEntry.Key] = propertyEntry.Value;
            foreach (var propertyEntry in child.Properties) merged.Properties[propertyEntry.Key] = propertyEntry.Value;

            foreach (var childEntry in parent.Children) merged.Children[childEntry.Key] = childEntry.Value;
            foreach (string childName in parent.ChildOrder)
                if (!merged.ChildOrder.Contains(childName))
                    merged.ChildOrder.Add(childName);
            foreach (var childEntry in child.Children)
            {
                if (merged.Children.ContainsKey(childEntry.Key))
                    merged.Children[childEntry.Key] = MergeNodes(merged.Children[childEntry.Key], childEntry.Value);
                else
                    merged.Children[childEntry.Key] = childEntry.Value;
                if (!merged.ChildOrder.Contains(childEntry.Key))
                    merged.ChildOrder.Add(childEntry.Key);
            }

            merged.AnonymousChildren.AddRange(parent.AnonymousChildren);
            merged.AnonymousChildren.AddRange(child.AnonymousChildren);

            return merged;
        }


        public float GetKnob(string name, float fallback = 0f)
        {
            return GlobalKnobs?.GetFloat(name, fallback) ?? fallback;
        }

        public int GetKnobInt(string name, int fallback = 0)
        {
            return GlobalKnobs?.GetInt(name, fallback) ?? fallback;
        }


        private List<(int level, float value)> _xpCurve;

        public List<(int level, float value)> GetXPCurve()
        {
            if (_xpCurve != null) return _xpCurve;

            _xpCurve = new List<(int, float)>();
            var tables = GetNode("Tables");
            if (tables == null) throw new InvalidDataException("Tables.gc not loaded");

            var xpTable = tables.GetChild("Experience");
            if (xpTable == null) throw new InvalidDataException("Tables.Experience not found");

            foreach (var entry in xpTable.AnonymousChildren)
            {
                int level = entry.GetInt("Level", 0);
                float value = entry.GetFloat("Value", 0f);
                if (level > 0)
                    _xpCurve.Add((level, value));
            }

            _xpCurve.Sort((a, b) => a.level.CompareTo(b.level));
            if (_xpCurve.Count == 0)
                throw new InvalidDataException("Tables.Experience has no entries");
            Debug.LogError($"[GC-DATABASE] xpCurve entries={_xpCurve.Count}");
            foreach (var e in _xpCurve)
                Debug.LogError($"[GC-DATABASE] xpCurve level={e.level} kills={e.value}");

            return _xpCurve;
        }

        public float InterpolateXPCurve(int targetLevel)
        {
            var curve = GetXPCurve();
            if (curve.Count == 0) throw new InvalidDataException("Tables.Experience has no entries");

            for (int curveIndex = 0; curveIndex < curve.Count; curveIndex++)
            {
                if (targetLevel <= curve[curveIndex].level)
                {
                    if (curveIndex == 0) return curve[curveIndex].value;
                    float t = (float)(targetLevel - curve[curveIndex - 1].level) / (curve[curveIndex].level - curve[curveIndex - 1].level);
                    return curve[curveIndex - 1].value + t * (curve[curveIndex].value - curve[curveIndex - 1].value);
                }
            }
            return curve[curve.Count - 1].value;
        }

        public int GetCurveValueFixed32(string tableName, int targetLevel, int fallbackF32 = 0)
        {
            var curve = GetCurveFixed32(tableName);
            if (curve.Count == 0) return fallbackF32;
            return InterpolateCurveValueFixed32(curve, targetLevel);
        }

        public int RequireCurveValueFixed32(string tableName, int targetLevel)
        {
            var curve = GetCurveFixed32(tableName);
            if (curve.Count == 0)
                throw new InvalidDataException($"Tables.{tableName} not found");
            return InterpolateCurveValueFixed32(curve, targetLevel);
        }

        public float RequireCurveValue(string tableName, int targetLevel)
        {
            return RequireCurveValueFixed32(tableName, targetLevel) / 256f;
        }

        private static int InterpolateCurveValueFixed32(List<(int levelF32, int valueF32)> curve, int targetLevel)
        {
            int levelF32 = ToFixed32(Math.Max(0, targetLevel));
            if (levelF32 <= curve[0].levelF32) return curve[0].valueF32;

            int last = curve.Count - 1;
            if (levelF32 >= curve[last].levelF32) return curve[last].valueF32;

            for (int curveIndex = 0; curveIndex < last; curveIndex++)
            {
                var lo = curve[curveIndex];
                var hi = curve[curveIndex + 1];
                if (levelF32 > hi.levelF32) continue;
                if (hi.levelF32 == lo.levelF32) return hi.valueF32;

                long ratioQ16 = ((long)(levelF32 - lo.levelF32) * 0x10000L) / (hi.levelF32 - lo.levelF32);
                long delta = (long)(hi.valueF32 - lo.valueF32) * ratioQ16;
                return lo.valueF32 + (int)(delta / 0x10000L);
            }

            return curve[last].valueF32;
        }

        public float GetCurveValue(string tableName, int targetLevel, float fallback = 0f)
        {
            int fallbackF32 = ToFixed32(fallback);
            return GetCurveValueFixed32(tableName, targetLevel, fallbackF32) / 256f;
        }

        private List<(int levelF32, int valueF32)> GetCurveFixed32(string tableName)
        {
            if (_curveCache.TryGetValue(tableName, out var cached)) return cached;

            var curve = new List<(int levelF32, int valueF32)>();
            var table = Tables?.GetChild(tableName);
            if (table?.AnonymousChildren != null)
            {
                foreach (var entry in table.AnonymousChildren)
                {
                    float level = entry.GetFloat("Level", 0f);
                    float value = entry.GetFloat("Value", 0f);
                    if (entry.HasProperty("Level") && level >= 0f)
                        curve.Add((ToFixed32(level), ToFixed32(value)));
                }
            }
            curve.Sort((a, b) => a.levelF32.CompareTo(b.levelF32));
            _curveCache[tableName] = curve;
            return curve;
        }

        private static int ToFixed32(float value)
        {
            return (int)(value * 256f);
        }


        public (float damage, float volatility, float range, float cooldown, string weaponClass, float weaponSpeed,
                string damageType, string weaponCategory, bool useProjectile, float projectileSpeed, float projectileSize, int burstCount)
            GetWeaponStats(string weaponGCPath)
        {
            var node = ResolveWithInheritance(weaponGCPath);
            if (node == null)
                return (0f, 0f, 0f, 0f, "", 0f, "", "", false, 0f, 0f, 1);

            var desc = node.GetChild("Description");
            if (desc == null) desc = node;

            return (
                damage: desc.GetFloat("Damage", 1.0f),
                volatility: desc.GetFloat("DamageVolatility", 0.25f),
                range: desc.GetFloat("Range", 0f),
                cooldown: desc.GetFloat("CoolDown", 1.75f),
                weaponClass: desc.GetString("WeaponClass", "HTH"),
                weaponSpeed: desc.GetFloat("WeaponSpeed", 0f),
                damageType: desc.GetString("DamageType", "CRUSHING"),
                weaponCategory: desc.GetString("WeaponCategory", ""),
                useProjectile: desc.GetBool("UseProjectile", false),
                projectileSpeed: desc.GetFloat("ProjectileSpeed", 0f),
                projectileSize: desc.GetFloat("ProjectileSize", 0f),
                burstCount: Math.Max(1, desc.GetInt("BurstCount", 1))
            );
        }

        public float GetArmorDefenseRating(string armorGCPath)
        {
            var node = ResolveWithInheritance(armorGCPath);
            if (node == null) return 0f;

            var desc = node.GetChild("Description");
            if (desc == null) desc = node;
            return desc.GetFloat("DefenseRating", 0f);
        }


        public GCNode GetCreatureStats(string creatureGCPath)
        {
            var node = ResolveWithInheritance(creatureGCPath);
            if (node == null) return null;

            return node.GetChild("Description") ?? node;
        }

        public GCNode GetCreatureWeaponStats(string creatureGCPath)
        {
            var node = ResolveWithInheritance(creatureGCPath);
            if (node == null) return null;

            var manip = node.GetChild("Manipulators");
            if (manip == null) return null;

            var weapon = manip.GetChild("PrimaryWeapon");
            if (weapon == null) return null;

            return weapon.GetChild("Description") ?? weapon;
        }


        public void DumpNode(string path, int maxDepth = 3)
        {
            var node = Resolve(path);
            if (node == null)
            {
                Debug.LogError($"[GC-DATABASE] dumpNode path='{path}' state=missing");
                return;
            }
            DumpNodeRecursive(node, 0, maxDepth);
        }

        private void DumpNodeRecursive(GCNode node, int depth, int maxDepth)
        {
            if (depth > maxDepth) return;
            string indent = new string(' ', depth * 2);

            Debug.LogError($"{indent}[{node.Name}] extends={node.Extends ?? "none"} src={node.SourceFile}");
            foreach (var propertyEntry in node.Properties)
                Debug.LogError($"{indent}  {propertyEntry.Key} = {propertyEntry.Value}");
            foreach (var childEntry in node.Children)
                DumpNodeRecursive(childEntry.Value, depth + 1, maxDepth);
            foreach (var anon in node.AnonymousChildren)
                DumpNodeRecursive(anon, depth + 1, maxDepth);
        }
    }
}
