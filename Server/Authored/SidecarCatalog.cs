using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DungeonRunners.Engine;

namespace DungeonRunners.Data
{
    public sealed class SidecarCatalog
    {
        private const string ExpectedSidecarVersion = "sidecar-v2";
        private static SidecarCatalog _instance;
        public static SidecarCatalog Instance => _instance ??= new SidecarCatalog();

        private static readonly Regex JsonString = new Regex("\"(?<key>[^\"]+)\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"", RegexOptions.Compiled);
        private static readonly Regex JsonInt = new Regex("\"(?<key>[^\"]+)\"\\s*:\\s*(?<value>-?[0-9]+)", RegexOptions.Compiled);
        private static readonly Regex JsonBool = new Regex("\"(?<key>[^\"]+)\"\\s*:\\s*(?<value>true|false)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public readonly List<SidecarEntry> Entries = new List<SidecarEntry>();
        public readonly List<SidecarEntry> ZoneWorldEntries = new List<SidecarEntry>();
        public readonly List<SidecarEntry> EncounterTableEntries = new List<SidecarEntry>();
        public readonly List<SidecarEntry> WorldEntityGeneratorEntries = new List<SidecarEntry>();
        public readonly List<SidecarEntry> StaticObjectEntries = new List<SidecarEntry>();
        public readonly List<SidecarCobjPlacement> CobjPlacements = new List<SidecarCobjPlacement>();
        public readonly List<SidecarMonsterSkills> MonsterSkills = new List<SidecarMonsterSkills>();
        public readonly List<SidecarMonsterSkills> EffectiveMonsterSkills = new List<SidecarMonsterSkills>();
        public readonly List<SidecarPrimaryWeapon> EffectiveMonsterPrimaryWeapons = new List<SidecarPrimaryWeapon>();
        public readonly List<SidecarEncounterRow> EncounterTableRows = new List<SidecarEncounterRow>();
        public readonly List<SidecarWorldEntityGeneratorRow> WorldEntityGeneratorRows = new List<SidecarWorldEntityGeneratorRow>();
        private readonly Dictionary<string, SidecarEntry> _entriesByName = new Dictionary<string, SidecarEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, SidecarEntry> _entriesById = new Dictionary<int, SidecarEntry>();
        private readonly Dictionary<string, SidecarMonsterSkills> _effectiveMonsterSkillsByPath = new Dictionary<string, SidecarMonsterSkills>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SidecarPrimaryWeapon> _effectiveMonsterPrimaryWeaponsByPath = new Dictionary<string, SidecarPrimaryWeapon>(StringComparer.OrdinalIgnoreCase);
        public bool IsLoaded { get; private set; }
        public string RootPath { get; private set; } = "";
        public string SourceManifest { get; private set; } = "";
        public string ManifestVersion { get; private set; } = "";
        public bool IsExpectedVersion => string.Equals(ManifestVersion, ExpectedSidecarVersion, StringComparison.OrdinalIgnoreCase);
        public int ManifestEntryCount { get; private set; }
        public int IndexShardBytes { get; private set; }
        public bool HasCompletePackageEntryIndex => ManifestEntryCount > 0 && Entries.Count == ManifestEntryCount;

        public bool LoadFromAssets()
        {
            Entries.Clear();
            ZoneWorldEntries.Clear();
            EncounterTableEntries.Clear();
            WorldEntityGeneratorEntries.Clear();
            StaticObjectEntries.Clear();
            CobjPlacements.Clear();
            MonsterSkills.Clear();
            EffectiveMonsterSkills.Clear();
            EffectiveMonsterPrimaryWeapons.Clear();
            EncounterTableRows.Clear();
            WorldEntityGeneratorRows.Clear();
            _entriesByName.Clear();
            _entriesById.Clear();
            _effectiveMonsterSkillsByPath.Clear();
            _effectiveMonsterPrimaryWeaponsByPath.Clear();
            IsLoaded = false;
            ManifestVersion = "";
            ManifestEntryCount = 0;
            IndexShardBytes = 0;

            string root = DungeonRunners.Core.DataPaths.SidecarDir;
            RootPath = root;
            string manifestPath = Path.Combine(root, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                Debug.LogError($"[CLIENT-SIDECAR] source=missing path='{root}' runtimePkgDependency=false");
                return false;
            }

            SourceManifest = File.ReadAllText(manifestPath);
            ManifestVersion = GetString(SourceManifest, "version");
            ManifestEntryCount = GetInt(SourceManifest, "entries");
            IndexShardBytes = GetInt(SourceManifest, "indexShardBytes");
            LoadEntries(Path.Combine(root, "entries.jsonl"));
            LoadZoneWorld(Path.Combine(root, "indexes", "zone_world.jsonl"));
            LoadEncounterTables(Path.Combine(root, "indexes", "encounter_tables.jsonl"));
            LoadWorldEntityGenerators(Path.Combine(root, "indexes", "world_entity_generators.jsonl"));
            LoadStaticObjects(Path.Combine(root, "indexes", "cobj.jsonl"));
            LoadCobjPlacements(Path.Combine(root, "indexes", "cobj_placements.jsonl"));
            LoadMonsterSkills(Path.Combine(root, "indexes", "monster_skills.jsonl"));
            LoadEffectiveMonsterSkills(Path.Combine(root, "indexes", "effective_monster_skills.jsonl"));
            LoadEffectiveMonsterPrimaryWeapons(Path.Combine(root, "indexes", "effective_monster_primary_weapons.jsonl"));
            LoadEncounterTableRows(Path.Combine(root, "indexes", "encounter_table_rows.jsonl"));
            LoadWorldEntityGeneratorRows(Path.Combine(root, "indexes", "world_entity_generator_rows.jsonl"));
            IsLoaded = Entries.Count > 0 || ZoneWorldEntries.Count > 0 || EncounterTableEntries.Count > 0 || WorldEntityGeneratorEntries.Count > 0 || StaticObjectEntries.Count > 0 || CobjPlacements.Count > 0 || MonsterSkills.Count > 0 || EffectiveMonsterSkills.Count > 0 || EffectiveMonsterPrimaryWeapons.Count > 0 || EncounterTableRows.Count > 0 || WorldEntityGeneratorRows.Count > 0;
            Debug.LogError($"[CLIENT-SIDECAR] source=Sidecar loaded={IsLoaded} version='{ManifestVersion}' expected='{ExpectedSidecarVersion}' versionOk={IsExpectedVersion} packageEntries={Entries.Count}/{ManifestEntryCount} packageIndexComplete={HasCompletePackageEntryIndex} indexShardBytes={IndexShardBytes} zoneWorld={ZoneWorldEntries.Count} encounterTables={EncounterTableEntries.Count} encounterTableRows={EncounterTableRows.Count} worldEntityGenerators={WorldEntityGeneratorEntries.Count} worldEntityGeneratorRows={WorldEntityGeneratorRows.Count} staticObjects={StaticObjectEntries.Count} cobjPlacements={CobjPlacements.Count} monsterSkills={MonsterSkills.Count} effectiveMonsterSkills={EffectiveMonsterSkills.Count} effectiveMonsterPrimaryWeapons={EffectiveMonsterPrimaryWeapons.Count} path='{root}' runtimePkgDependency=false");
            LogCatalogStatus();
            return IsLoaded;
        }

        private static IEnumerable<string> ReadJsonLines(string path)
        {
            if (File.Exists(path))
            {
                foreach (string line in File.ReadLines(path))
                    yield return line;
                yield break;
            }

            string partsDir = path + ".parts";
            if (!Directory.Exists(partsDir))
                yield break;

            string[] parts = Directory.GetFiles(partsDir, "*.jsonl");
            Array.Sort(parts, StringComparer.OrdinalIgnoreCase);
            foreach (string part in parts)
            {
                foreach (string line in File.ReadLines(part))
                    yield return line;
            }
        }

        private void LoadEntries(string path)
        {
            foreach (string line in ReadJsonLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var entry = new SidecarEntry
                {
                    EntryId = GetInt(line, "entryId"),
                    PackageId = GetInt(line, "packageId"),
                    EntryIndex = GetInt(line, "entryIndex"),
                    Name = GetString(line, "name"),
                    TypeCode = GetInt(line, "typeCode"),
                    Compressed = GetBool(line, "compressed"),
                    RegionOffset = GetInt(line, "regionOffset"),
                    RegionSize = GetInt(line, "regionSize"),
                    UncSize = GetInt(line, "uncSize"),
                    CompSize = GetInt(line, "compSize"),
                    RawSha1 = GetString(line, "rawSha1"),
                    RawSha256 = GetString(line, "rawSha256"),
                    DecodedSha1 = GetString(line, "decodedSha1"),
                    DecodedSha256 = GetString(line, "decodedSha256"),
                    TextSha1 = GetString(line, "textSha1"),
                    SourceVirtualPath = GetString(line, "sourceVirtualPath"),
                    RawJson = line
                };
                if (entry.EntryId != 0 || !string.IsNullOrWhiteSpace(entry.Name))
                {
                    Entries.Add(entry);
                    RegisterEntry(entry);
                }
            }
        }

        private void RegisterEntry(SidecarEntry entry)
        {
            if (entry == null)
                return;
            if (entry.EntryId != 0 && !_entriesById.ContainsKey(entry.EntryId))
                _entriesById[entry.EntryId] = entry;
            AddEntryName(entry.Name, entry);
            AddEntryName((entry.Name ?? "").Replace('/', '\\'), entry);
            if (!string.IsNullOrWhiteSpace(entry.SourceVirtualPath))
                AddEntryName(entry.SourceVirtualPath, entry);
        }

        private void AddEntryName(string name, SidecarEntry entry)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;
            string key = name.Trim();
            if (!_entriesByName.ContainsKey(key))
                _entriesByName[key] = entry;
            string slashKey = key.Replace('\\', '/');
            if (!_entriesByName.ContainsKey(slashKey))
                _entriesByName[slashKey] = entry;
            string backslashKey = key.Replace('/', '\\');
            if (!_entriesByName.ContainsKey(backslashKey))
                _entriesByName[backslashKey] = entry;
        }

        private void LoadMonsterSkills(string path)
        {
            foreach (string line in ReadJsonLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var row = new SidecarMonsterSkills
                {
                    MonsterEntryId = GetInt(line, "monsterEntryId"),
                    MonsterPath = GetString(line, "monsterPath"),
                    ManipulatorsNodeId = GetString(line, "manipulatorsNodeId"),
                    SkillCount = CountArrayObjects(line, "skills")
                };
                if (row.MonsterEntryId != 0 || !string.IsNullOrWhiteSpace(row.MonsterPath))
                    MonsterSkills.Add(row);
            }
        }

        private void LoadEffectiveMonsterSkills(string path)
        {
            foreach (string line in ReadJsonLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var row = new SidecarMonsterSkills
                {
                    MonsterEntryId = GetInt(line, "monsterEntryId"),
                    MonsterPath = GetString(line, "monsterPath"),
                    MonsterNodeId = GetString(line, "monsterNodeId"),
                    ManipulatorsNodeId = GetString(line, "selectedManipulatorsNodeId"),
                    ManipulatorsSource = GetString(line, "manipulatorsSource"),
                    SkillCount = GetInt(line, "skillCount"),
                    Skills = ParseSkills(line)
                };
                if (row.SkillCount == 0 && row.Skills.Count > 0)
                    row.SkillCount = row.Skills.Count;
                if (row.MonsterEntryId != 0 || !string.IsNullOrWhiteSpace(row.MonsterPath))
                {
                    EffectiveMonsterSkills.Add(row);
                    string key = NormalizeGcPath(row.MonsterPath);
                    if (!string.IsNullOrWhiteSpace(key) && !_effectiveMonsterSkillsByPath.ContainsKey(key))
                        _effectiveMonsterSkillsByPath[key] = row;
                }
            }
        }

        public bool TryGetEffectiveMonsterSkills(string monsterPath, out SidecarMonsterSkills row)
        {
            row = null;
            if (string.IsNullOrWhiteSpace(monsterPath))
                return false;
            return _effectiveMonsterSkillsByPath.TryGetValue(NormalizeGcPath(monsterPath), out row);
        }

        private void LoadEffectiveMonsterPrimaryWeapons(string path)
        {
            foreach (string line in ReadJsonLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var row = new SidecarPrimaryWeapon
                {
                    MonsterEntryId = GetInt(line, "monsterEntryId"),
                    MonsterPath = GetString(line, "monsterPath"),
                    MonsterNodeId = GetString(line, "monsterNodeId"),
                    SelectedManipulatorsNodeId = GetString(line, "selectedManipulatorsNodeId"),
                    PrimaryWeaponNodeId = GetString(line, "primaryWeaponNodeId"),
                    PrimaryWeaponExtendsRaw = GetString(line, "primaryWeaponExtendsRaw"),
                    WeaponEntryId = GetInt(line, "weaponEntryId"),
                    WeaponEntryName = GetString(line, "weaponEntryName"),
                    WeaponNodeId = GetString(line, "weaponNodeId"),
                    WeaponPath = GetString(line, "weaponPath"),
                    Status = GetString(line, "status"),
                    EffectiveProperties = ParseStringMap(ExtractObject(line, "effectiveProperties")),
                    PropertySources = ParseStringMap(ExtractObject(line, "propertySources")),
                    RawJson = line
                };
                if (row.MonsterEntryId != 0 || !string.IsNullOrWhiteSpace(row.MonsterPath))
                {
                    EffectiveMonsterPrimaryWeapons.Add(row);
                    string key = NormalizeGcPath(row.MonsterPath);
                    if (!string.IsNullOrWhiteSpace(key) && !_effectiveMonsterPrimaryWeaponsByPath.ContainsKey(key))
                        _effectiveMonsterPrimaryWeaponsByPath[key] = row;
                }
            }
        }

        public bool TryGetEffectiveMonsterPrimaryWeapon(string monsterPath, out SidecarPrimaryWeapon row)
        {
            row = null;
            if (string.IsNullOrWhiteSpace(monsterPath))
                return false;
            return _effectiveMonsterPrimaryWeaponsByPath.TryGetValue(NormalizeGcPath(monsterPath), out row);
        }

        private void LoadZoneWorld(string path)
        {
            foreach (string line in ReadJsonLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var entry = new SidecarEntry
                {
                    EntryId = GetInt(line, "entryId"),
                    Name = GetString(line, "name"),
                    TypeCode = GetInt(line, "typeCode"),
                    RawJson = line
                };
                if (entry.EntryId != 0 || !string.IsNullOrWhiteSpace(entry.Name))
                    ZoneWorldEntries.Add(entry);
            }
        }

        private void LoadEncounterTables(string path)
        {
            foreach (string line in ReadJsonLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var entry = new SidecarEntry
                {
                    EntryId = GetInt(line, "entryId"),
                    Name = GetString(line, "name"),
                    TypeCode = 13,
                    TextSha1 = GetString(line, "textSha1"),
                    RawJson = line,
                    TableCount = GetInt(line, "tableCount")
                };
                if (entry.EntryId != 0 || !string.IsNullOrWhiteSpace(entry.Name))
                    EncounterTableEntries.Add(entry);
            }
        }

        private void LoadEncounterTableRows(string path)
        {
            foreach (string line in ReadJsonLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var row = new SidecarEncounterRow
                {
                    EntryId = GetInt(line, "entryId"),
                    Name = GetString(line, "name"),
                    EncounterNodeId = GetString(line, "encounterNodeId"),
                    EncounterOrder = GetInt(line, "encounterOrder"),
                    UnitCount = GetInt(line, "unitCount"),
                    RuntimeConsumerStatus = GetString(line, "runtimeConsumerStatus"),
                    UnitObjectCount = CountArrayObjects(line, "units"),
                    RawJson = line
                };
                if (row.EntryId != 0 || !string.IsNullOrWhiteSpace(row.Name))
                    EncounterTableRows.Add(row);
            }
        }

        private void LoadWorldEntityGenerators(string path)
        {
            foreach (string line in ReadJsonLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var entry = new SidecarEntry
                {
                    EntryId = GetInt(line, "entryId"),
                    Name = GetString(line, "name"),
                    TypeCode = 13,
                    TextSha1 = GetString(line, "textSha1"),
                    RawJson = line,
                    GeneratorCount = SumIntFields(line, "generatorCount")
                };
                if (entry.EntryId != 0 || !string.IsNullOrWhiteSpace(entry.Name))
                    WorldEntityGeneratorEntries.Add(entry);
            }
        }

        private void LoadWorldEntityGeneratorRows(string path)
        {
            foreach (string line in ReadJsonLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var row = new SidecarWorldEntityGeneratorRow
                {
                    EntryId = GetInt(line, "entryId"),
                    Name = GetString(line, "name"),
                    TableLocalName = GetString(line, "tableLocalName"),
                    TableNodeId = GetString(line, "tableNodeId"),
                    TableOrder = GetInt(line, "tableOrder"),
                    GeneratorNodeId = GetString(line, "generatorNodeId"),
                    GeneratorOrder = GetInt(line, "generatorOrder"),
                    WorldEntity = GetString(line, "worldEntity"),
                    ResolvedEntryId = GetInt(line, "resolvedEntryId"),
                    ResolvedEntryName = GetString(line, "resolvedEntryName"),
                    ResolvedNodeId = GetString(line, "resolvedNodeId"),
                    ResolvedNodePath = GetString(line, "resolvedNodePath"),
                    ResolvedPath = GetString(line, "resolvedPath"),
                    Chance = GetString(line, "chance"),
                    RuntimeConsumerStatus = GetString(line, "runtimeConsumerStatus"),
                    RawJson = line
                };
                if (row.EntryId != 0 || !string.IsNullOrWhiteSpace(row.Name) || !string.IsNullOrWhiteSpace(row.WorldEntity))
                    WorldEntityGeneratorRows.Add(row);
            }
        }

        private void LoadStaticObjects(string path)
        {
            foreach (string line in ReadJsonLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var entry = new SidecarEntry
                {
                    EntryId = GetInt(line, "entryId"),
                    Name = GetString(line, "name"),
                    TypeCode = 4,
                    RegionOffset = GetInt(line, "regionOffset"),
                    RegionSize = GetInt(line, "regionSize"),
                    RawSha1 = GetString(line, "rawSha1"),
                    DecodedSha1 = GetString(line, "decodedSha1")
                };
                if (entry.EntryId != 0 || !string.IsNullOrWhiteSpace(entry.Name))
                    StaticObjectEntries.Add(entry);
            }
        }

        private void LoadCobjPlacements(string path)
        {
            foreach (string line in ReadJsonLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var row = new SidecarCobjPlacement
                {
                    SourceEntryId = GetInt(line, "sourceEntryId"),
                    SourceEntryName = GetString(line, "sourceEntryName"),
                    SourceNodeId = GetString(line, "sourceNodeId"),
                    FieldName = GetString(line, "fieldName"),
                    RawTarget = GetString(line, "rawTarget"),
                    CobjEntryId = GetInt(line, "cobjEntryId"),
                    CobjName = GetString(line, "cobjName"),
                    Status = GetString(line, "status"),
                    GeometryStatus = GetString(line, "geometryStatus")
                };
                if (row.CobjEntryId != 0 || !string.IsNullOrWhiteSpace(row.CobjName))
                    CobjPlacements.Add(row);
            }
        }

        private void LogCatalogStatus()
        {
            string cobjPlacementStatus = CobjPlacements.Count > 0 ? "partial" : (StaticObjectEntries.Count > 0 ? "missing" : "none");
            int worldEntityGeneratorRows = WorldEntityGeneratorRows.Count;
            if (worldEntityGeneratorRows == 0)
            {
                foreach (var entry in WorldEntityGeneratorEntries)
                    worldEntityGeneratorRows += entry.GeneratorCount;
            }
            int expectedEffectiveMonsterPrimaryWeapons = GetInt(SourceManifest, "effectiveMonsterPrimaryWeaponRows");
            int expectedEncounterTableRows = GetInt(SourceManifest, "encounterTableRows");
            int expectedWorldEntityGeneratorRows = GetInt(SourceManifest, "worldEntityGeneratorRows");
            bool hasEffectiveMonsterSkills = EffectiveMonsterSkills.Count > 0;
            bool hasSchemaRows = EffectiveMonsterPrimaryWeapons.Count > 0 && EncounterTableRows.Count > 0 && WorldEntityGeneratorRows.Count > 0;
            bool hasManifestSchemaCounts = expectedEffectiveMonsterPrimaryWeapons > 0 && expectedEncounterTableRows > 0 && expectedWorldEntityGeneratorRows > 0;
            bool schemaCountsMatch = hasManifestSchemaCounts && EffectiveMonsterPrimaryWeapons.Count == expectedEffectiveMonsterPrimaryWeapons && EncounterTableRows.Count == expectedEncounterTableRows && worldEntityGeneratorRows == expectedWorldEntityGeneratorRows;
            int primaryWeaponConcreteRows = 0;
            int primaryWeaponAuthoritativeEmptyRows = 0;
            int primaryWeaponBlockedRows = 0;
            foreach (var row in EffectiveMonsterPrimaryWeapons)
            {
                string status = row.Status ?? string.Empty;
                if (row.WeaponEntryId != 0 || !string.IsNullOrWhiteSpace(row.WeaponPath))
                    primaryWeaponConcreteRows++;
                if (status.StartsWith("AUTHORITATIVE_EMPTY", StringComparison.OrdinalIgnoreCase))
                    primaryWeaponAuthoritativeEmptyRows++;
                if (status.StartsWith("BLOCKED", StringComparison.OrdinalIgnoreCase))
                    primaryWeaponBlockedRows++;
            }
            int dewValleyPrimaryWeaponSampleCount = 0;
            int dewValleyPrimaryWeaponConcreteSampleCount = 0;
            int dewValleyPrimaryWeaponAuthoritativeEmptySampleCount = 0;
            int dewValleyPrimaryWeaponBlockedSampleCount = 0;
            foreach (string sample in new[]
            {
                "world.dungeon00.mob.melee01.rank1",
                "world.dungeon00.mob.melee02.rank1",
                "world.dungeon00.mob.melee03.rank1",
                "world.dungeon00.mob.melee04.rank1"
            })
            {
                SidecarPrimaryWeapon primaryWeaponRow;
                if (!TryGetEffectiveMonsterPrimaryWeapon(sample, out primaryWeaponRow))
                    continue;
                dewValleyPrimaryWeaponSampleCount++;
                string status = primaryWeaponRow.Status ?? string.Empty;
                if (primaryWeaponRow.WeaponEntryId != 0 || !string.IsNullOrWhiteSpace(primaryWeaponRow.WeaponPath))
                    dewValleyPrimaryWeaponConcreteSampleCount++;
                if (status.StartsWith("AUTHORITATIVE_EMPTY", StringComparison.OrdinalIgnoreCase))
                    dewValleyPrimaryWeaponAuthoritativeEmptySampleCount++;
                if (status.StartsWith("BLOCKED", StringComparison.OrdinalIgnoreCase))
                    dewValleyPrimaryWeaponBlockedSampleCount++;
            }
            bool hasDewValleyPrimaryWeaponCoverage = dewValleyPrimaryWeaponSampleCount == 4;
            bool hasNoBlockedPrimaryWeaponRows = primaryWeaponBlockedRows == 0;
            int encounterUnitsWithType = CountJsonPattern(EncounterTableRows, "\"Type\"\\s*:\\s*\"");
            int encounterResolvedUnitTargets = CountJsonPattern(EncounterTableRows, "\"resolvedEntryId\"\\s*:\\s*[1-9][0-9]*");
            int authoredEmptyWorldEntityGeneratorRows = 0;
            int unresolvedWorldEntityGeneratorRows = 0;
            foreach (var row in WorldEntityGeneratorRows)
            {
                if (string.IsNullOrWhiteSpace(row.WorldEntity))
                {
                    authoredEmptyWorldEntityGeneratorRows++;
                    continue;
                }
                if (row.ResolvedEntryId == 0 && string.IsNullOrWhiteSpace(row.ResolvedPath))
                    unresolvedWorldEntityGeneratorRows++;
            }
            bool schemaQualityReady = hasDewValleyPrimaryWeaponCoverage && hasNoBlockedPrimaryWeaponRows && encounterUnitsWithType > 0 && encounterResolvedUnitTargets >= encounterUnitsWithType && unresolvedWorldEntityGeneratorRows == 0;
            bool ok = IsExpectedVersion && HasCompletePackageEntryIndex && hasEffectiveMonsterSkills && hasSchemaRows && schemaCountsMatch && schemaQualityReady && cobjPlacementStatus != "missing";
            Debug.LogError($"[CLIENT-SIDECAR] ok={ok} version='{ManifestVersion}' expected='{ExpectedSidecarVersion}' versionOk={IsExpectedVersion} packageEntries={Entries.Count}/{ManifestEntryCount} packageIndexComplete={HasCompletePackageEntryIndex} indexShardBytes={IndexShardBytes} effectiveMonsterSkills={EffectiveMonsterSkills.Count} effectiveMonsterPrimaryWeapons={EffectiveMonsterPrimaryWeapons.Count} expectedEffectiveMonsterPrimaryWeapons={expectedEffectiveMonsterPrimaryWeapons} encounterTables={EncounterTableEntries.Count} encounterTableRows={EncounterTableRows.Count} expectedEncounterTableRows={expectedEncounterTableRows} worldEntityGenerators={WorldEntityGeneratorEntries.Count} worldEntityGeneratorRows={worldEntityGeneratorRows} expectedWorldEntityGeneratorRows={expectedWorldEntityGeneratorRows} schemaCountsMatch={schemaCountsMatch} dewValleyPrimaryWeaponCoverage={hasDewValleyPrimaryWeaponCoverage} dewValleyPrimaryWeaponSampleCount={dewValleyPrimaryWeaponSampleCount} dewValleyPrimaryWeaponConcreteSampleCount={dewValleyPrimaryWeaponConcreteSampleCount} dewValleyPrimaryWeaponAuthoritativeEmptySampleCount={dewValleyPrimaryWeaponAuthoritativeEmptySampleCount} dewValleyPrimaryWeaponBlockedSampleCount={dewValleyPrimaryWeaponBlockedSampleCount} primaryWeaponConcreteRows={primaryWeaponConcreteRows} primaryWeaponAuthoritativeEmptyRows={primaryWeaponAuthoritativeEmptyRows} primaryWeaponBlockedRows={primaryWeaponBlockedRows} encounterUnitsWithType={encounterUnitsWithType} encounterResolvedUnitTargets={encounterResolvedUnitTargets} unresolvedWorldEntityGeneratorRows={unresolvedWorldEntityGeneratorRows} authoredEmptyWorldEntityGeneratorRows={authoredEmptyWorldEntityGeneratorRows} schemaQualityReady={schemaQualityReady} cobjRaw={StaticObjectEntries.Count} cobjPlacementRefs={CobjPlacements.Count} cobjPlacementStatus={cobjPlacementStatus} cobjTransformRows=0 cobjRuntimePlacementReady=false cobjGeometry=blocked-client-placement-transform runtimePkgDependency=false");
        }

        private static int CountJsonPattern(List<SidecarEncounterRow> rows, string pattern)
        {
            int count = 0;
            foreach (var row in rows)
                count += Regex.Matches(row.RawJson ?? "", pattern).Count;
            return count;
        }

        public bool TryGetEntryByName(string name, out SidecarEntry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(name))
                return false;
            if (_entriesByName.TryGetValue(name.Trim(), out entry))
                return true;
            string slashKey = name.Trim().Replace('\\', '/');
            if (_entriesByName.TryGetValue(slashKey, out entry))
                return true;
            string backslashKey = name.Trim().Replace('/', '\\');
            return _entriesByName.TryGetValue(backslashKey, out entry);
        }

        public bool TryGetEntryById(int entryId, out SidecarEntry entry)
        {
            entry = null;
            if (entryId == 0)
                return false;
            return _entriesById.TryGetValue(entryId, out entry);
        }

        public bool TryGetEntryByNameOrId(string name, int entryId, out SidecarEntry entry)
        {
            if (TryGetEntryByName(name, out entry))
                return true;
            return TryGetEntryById(entryId, out entry);
        }

        public int CountEntriesByTypeCode(int typeCode)
        {
            int count = 0;
            foreach (var entry in Entries)
            {
                if (entry.TypeCode == typeCode)
                    count++;
            }
            return count;
        }

        private static string GetString(string json, string key)
        {
            foreach (Match match in JsonString.Matches(json))
                if (string.Equals(match.Groups["key"].Value, key, StringComparison.OrdinalIgnoreCase))
                    return Regex.Unescape(match.Groups["value"].Value);
            return "";
        }

        private static int GetInt(string json, string key)
        {
            foreach (Match match in JsonInt.Matches(json))
                if (string.Equals(match.Groups["key"].Value, key, StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(match.Groups["value"].Value, out int value))
                    return value;
            return 0;
        }

        private static int SumIntFields(string json, string key)
        {
            int sum = 0;
            foreach (Match match in JsonInt.Matches(json))
                if (string.Equals(match.Groups["key"].Value, key, StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(match.Groups["value"].Value, out int value))
                    sum += value;
            return sum;
        }

        private static bool GetBool(string json, string key)
        {
            foreach (Match match in JsonBool.Matches(json))
                if (string.Equals(match.Groups["key"].Value, key, StringComparison.OrdinalIgnoreCase))
                    return string.Equals(match.Groups["value"].Value, "true", StringComparison.OrdinalIgnoreCase);
            return false;
        }

        private static List<SidecarSkill> ParseSkills(string json)
        {
            var skills = new List<SidecarSkill>();
            foreach (string skillJson in ExtractArrayObjects(json, "skills"))
            {
                var skill = new SidecarSkill
                {
                    LocalName = GetString(skillJson, "localName"),
                    SkillPath = GetString(skillJson, "skillPath"),
                    ExtendsRaw = GetString(skillJson, "extendsRaw"),
                    IsPrimaryAttack = GetBool(skillJson, "isPrimaryAttack"),
                    EffectiveProperties = ParseStringMap(ExtractObject(skillJson, "effectiveProperties")),
                    LocalOverrideProperties = ParseStringMap(ExtractObject(skillJson, "localOverrideProperties")),
                    PropertySources = ParseStringMap(ExtractObject(skillJson, "propertySources"))
                };
                if (!string.IsNullOrWhiteSpace(skill.SkillPath) || !string.IsNullOrWhiteSpace(skill.ExtendsRaw))
                    skills.Add(skill);
            }
            return skills;
        }

        private static List<string> ExtractArrayObjects(string json, string key)
        {
            var rows = new List<string>();
            string marker = $"\"{key}\":[";
            int start = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return rows;
            bool inString = false;
            int depth = 0;
            int objectStart = -1;
            for (int jsonIndex = start + marker.Length; jsonIndex < json.Length; jsonIndex++)
            {
                char c = json[jsonIndex];
                if (c == '"' && (jsonIndex == 0 || json[jsonIndex - 1] != '\\'))
                    inString = !inString;
                if (inString)
                    continue;
                if (c == '{')
                {
                    if (depth == 0)
                        objectStart = jsonIndex;
                    depth++;
                }
                else if (c == '}')
                {
                    depth = Math.Max(0, depth - 1);
                    if (depth == 0 && objectStart >= 0)
                    {
                        rows.Add(json.Substring(objectStart, jsonIndex - objectStart + 1));
                        objectStart = -1;
                    }
                }
                else if (c == ']' && depth == 0)
                    break;
            }
            return rows;
        }

        private static string ExtractObject(string json, string key)
        {
            string marker = $"\"{key}\":{{";
            int start = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return "";
            bool inString = false;
            int depth = 0;
            int objectStart = start + marker.Length - 1;
            for (int jsonIndex = objectStart; jsonIndex < json.Length; jsonIndex++)
            {
                char c = json[jsonIndex];
                if (c == '"' && (jsonIndex == 0 || json[jsonIndex - 1] != '\\'))
                    inString = !inString;
                if (inString)
                    continue;
                if (c == '{')
                    depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                        return json.Substring(objectStart, jsonIndex - objectStart + 1);
                }
            }
            return "";
        }

        private static Dictionary<string, string> ParseStringMap(string json)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json))
                return map;
            foreach (Match match in JsonString.Matches(json))
            {
                string key = Regex.Unescape(match.Groups["key"].Value);
                string value = Regex.Unescape(match.Groups["value"].Value);
                if (!string.IsNullOrWhiteSpace(key))
                    map[key] = value;
            }
            return map;
        }

        private static string NormalizeGcPath(string path)
        {
            return (path ?? "").Replace('\\', '.').Replace('/', '.').Trim().ToLowerInvariant();
        }

        private static int CountArrayObjects(string json, string key)
        {
            string marker = $"\"{key}\":[";
            int start = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return 0;
            int depth = 0;
            int count = 0;
            bool inString = false;
            for (int jsonIndex = start + marker.Length; jsonIndex < json.Length; jsonIndex++)
            {
                char c = json[jsonIndex];
                if (c == '"' && (jsonIndex == 0 || json[jsonIndex - 1] != '\\'))
                    inString = !inString;
                if (inString)
                    continue;
                if (c == '{')
                {
                    if (depth == 0) count++;
                    depth++;
                }
                else if (c == '}')
                    depth = Math.Max(0, depth - 1);
                else if (c == ']' && depth == 0)
                    break;
            }
            return count;
        }
    }

    public sealed class SidecarEntry
    {
        public int EntryId;
        public int PackageId;
        public int EntryIndex;
        public string Name;
        public int TypeCode;
        public bool Compressed;
        public int RegionOffset;
        public int RegionSize;
        public int UncSize;
        public int CompSize;
        public string RawSha1;
        public string RawSha256;
        public string DecodedSha1;
        public string DecodedSha256;
        public string TextSha1;
        public string SourceVirtualPath;
        public string RawJson;
        public int TableCount;
        public int GeneratorCount;
    }

    public sealed class SidecarMonsterSkills
    {
        public int MonsterEntryId;
        public string MonsterPath;
        public string MonsterNodeId;
        public string ManipulatorsNodeId;
        public string ManipulatorsSource;
        public int SkillCount;
        public List<SidecarSkill> Skills = new List<SidecarSkill>();
    }

    public sealed class SidecarPrimaryWeapon
    {
        public int MonsterEntryId;
        public string MonsterPath;
        public string MonsterNodeId;
        public string SelectedManipulatorsNodeId;
        public string PrimaryWeaponNodeId;
        public string PrimaryWeaponExtendsRaw;
        public int WeaponEntryId;
        public string WeaponEntryName;
        public string WeaponNodeId;
        public string WeaponPath;
        public string Status;
        public Dictionary<string, string> EffectiveProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> PropertySources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string RawJson;
    }

    public sealed class SidecarEncounterRow
    {
        public int EntryId;
        public string Name;
        public string EncounterNodeId;
        public int EncounterOrder;
        public int UnitCount;
        public int UnitObjectCount;
        public string RuntimeConsumerStatus;
        public string RawJson;
    }

    public sealed class SidecarWorldEntityGeneratorRow
    {
        public int EntryId;
        public string Name;
        public string TableLocalName;
        public string TableNodeId;
        public int TableOrder;
        public string GeneratorNodeId;
        public int GeneratorOrder;
        public string WorldEntity;
        public int ResolvedEntryId;
        public string ResolvedEntryName;
        public string ResolvedNodeId;
        public string ResolvedNodePath;
        public string ResolvedPath;
        public string Chance;
        public string RuntimeConsumerStatus;
        public string RawJson;
    }

    public sealed class SidecarCobjPlacement
    {
        public int SourceEntryId;
        public string SourceEntryName;
        public string SourceNodeId;
        public string FieldName;
        public string RawTarget;
        public int CobjEntryId;
        public string CobjName;
        public string Status;
        public string GeometryStatus;
    }

    public sealed class SidecarSkill
    {
        public string LocalName;
        public string SkillPath;
        public string ExtendsRaw;
        public bool IsPrimaryAttack;
        public Dictionary<string, string> EffectiveProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> LocalOverrideProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> PropertySources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
