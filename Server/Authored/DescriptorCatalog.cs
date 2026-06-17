using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DungeonRunners.Engine;

namespace DungeonRunners.Data
{
    public sealed class DescriptorCatalog
    {
        private static DescriptorCatalog _instance;
        public static DescriptorCatalog Instance => _instance ??= new DescriptorCatalog();

        private static readonly Regex DungeonZoneName = new Regex(@"^dungeon\d{2}_level", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public readonly List<DescriptorRecord> Records = new List<DescriptorRecord>();
        public readonly List<DescriptorRecord> EncounterTables = new List<DescriptorRecord>();
        public readonly List<DescriptorRecord> ZoneWorldDocs = new List<DescriptorRecord>();
        public readonly List<DescriptorRecord> StaticObjects = new List<DescriptorRecord>();

        private bool _built;
        private string _source = "unknown";

        public bool BuildFromSidecar(SidecarCatalog sidecar)
        {
            Records.Clear();
            EncounterTables.Clear();
            ZoneWorldDocs.Clear();
            StaticObjects.Clear();
            _built = false;

            if (sidecar == null || !sidecar.IsLoaded)
                return false;

            foreach (var entry in sidecar.ZoneWorldEntries)
                AddSidecarRecord(entry, DescriptorKind.ZoneWorld);
            foreach (var entry in sidecar.EncounterTableEntries)
                AddSidecarRecord(entry, DescriptorKind.EncounterTable);
            foreach (var entry in sidecar.StaticObjectEntries)
                AddSidecarRecord(entry, DescriptorKind.StaticObject);

            if (Records.Count == 0 && sidecar.Entries.Count > 0)
            {
                foreach (var entry in sidecar.Entries)
                    AddSidecarRecord(entry, ClassifySidecarEntry(entry));
            }

            _built = true;
            _source = "Sidecar";
            Debug.LogError($"[CLIENT-DESCRIPTOR-CATALOG] records={Records.Count} packageEntries={sidecar.Entries.Count}/{sidecar.ManifestEntryCount} packageIndexComplete={sidecar.HasCompletePackageEntryIndex} zoneWorld={ZoneWorldDocs.Count}/239 encounterTables={EncounterTables.Count} staticObjects={StaticObjects.Count} source=Sidecar runtimePkgDependency=false sourceFunction=GCClassRegistry+DFCClass");
            Debug.LogError($"[CLIENT-SIDECAR] status=loaded packageEntries={sidecar.Entries.Count}/{sidecar.ManifestEntryCount} packageIndexComplete={sidecar.HasCompletePackageEntryIndex} zoneWorld={sidecar.ZoneWorldEntries.Count} encounterTables={sidecar.EncounterTableEntries.Count} staticObjects={sidecar.StaticObjectEntries.Count} monsterSkills={sidecar.MonsterSkills.Count} effectiveMonsterSkills={sidecar.EffectiveMonsterSkills.Count} source=Sidecar");
            return true;
        }

        private void AddSidecarRecord(SidecarEntry entry, DescriptorKind kind)
        {
            var record = new DescriptorRecord
            {
                AuthoredName = entry.Name ?? "",
                RegistryPath = entry.Name ?? "",
                ExtendsPath = "",
                Kind = kind
            };
            record.Properties["entryId"] = entry.EntryId.ToString();
            record.Properties["typeCode"] = entry.TypeCode.ToString();
            record.Properties["rawSha1"] = entry.RawSha1 ?? "";
            record.Properties["decodedSha1"] = entry.DecodedSha1 ?? "";
            record.Properties["textSha1"] = entry.TextSha1 ?? "";
            Records.Add(record);
            if (record.Kind == DescriptorKind.EncounterTable)
                EncounterTables.Add(record);
            else if (record.Kind == DescriptorKind.ZoneWorld)
                ZoneWorldDocs.Add(record);
            else if (record.Kind == DescriptorKind.StaticObject)
                StaticObjects.Add(record);
        }

        public void BuildFromGCDatabase(GCDatabase db)
        {
            Records.Clear();
            EncounterTables.Clear();
            ZoneWorldDocs.Clear();
            StaticObjects.Clear();
            _built = false;

            if (db == null || !db.IsLoaded)
                return;

            var seenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in db.RegisteredPaths)
            {
                GCNode node = db.Resolve(path);
                if (node == null)
                    continue;
                string source = string.IsNullOrWhiteSpace(node.SourceFile) ? path : node.SourceFile;
                if (!seenSources.Add(source))
                    continue;

                var record = CreateRecord(path, node);
                Records.Add(record);
                if (record.Kind == DescriptorKind.EncounterTable)
                    EncounterTables.Add(record);
                else if (record.Kind == DescriptorKind.ZoneWorld)
                    ZoneWorldDocs.Add(record);
                else if (record.Kind == DescriptorKind.StaticObject)
                    StaticObjects.Add(record);
            }

            _built = true;
            _source = "GC-DATABASE";
            Debug.LogError($"[CLIENT-DESCRIPTOR-CATALOG] records={Records.Count} zoneWorld={ZoneWorldDocs.Count}/239 encounterTables={EncounterTables.Count} staticObjects={StaticObjects.Count} source=GC-DATABASE expected=PKG missingZoneWorld={Math.Max(0, 239 - ZoneWorldDocs.Count)} sourceFunction=GCClassRegistry+DFCClass");
            bool dewValleyBossZonePresent = ContainsZone("dungeon00_level03_boss");
            bool level03MasterPresent = ContainsRecord("level03_master_encounter");
            bool level04MasterPresent = ContainsRecord("level04_master_encounter");
            Debug.LogError($"[CLIENT-DESCRIPTOR-CATALOG] dewValleyBossZonePresent={dewValleyBossZonePresent} level03MasterPresent={level03MasterPresent} level04MasterPresent={level04MasterPresent} allDungeonReplacement=partial pendingSpawnUnit=unresolved encounterDifficultyConsumer=unresolved");
        }

        public void RunStartupCheck()
        {
            if (!_built)
                BuildFromGCDatabase(GCDatabase.Instance);

            int wetLike = Records.Count(r => r.HasProperty("WorldEntityTable") || r.HasProperty("WorldEntityGenerator") || r.HasProperty("TableSelector"));
            int lootLike = Records.Count(r => r.AuthoredName.IndexOf("loot", StringComparison.OrdinalIgnoreCase) >= 0 || r.HasProperty("TreasureGenerator") || r.HasProperty("TreasureGenerator2"));
            int monsterLike = Records.Count(r => r.HasProperty("Behavior") || r.HasProperty("CreatureType") || r.HasProperty("CollisionRadius"));
            Debug.LogError($"[CLIENT-DESCRIPTOR-CATALOG] records={Records.Count} zoneWorld={ZoneWorldDocs.Count}/239 encounterTables={EncounterTables.Count} wetLike={wetLike} lootLike={lootLike} monsterLike={monsterLike} status=partial source={_source} pkgExpectedZoneWorld=239 pkgMissingZoneWorld={Math.Max(0, 239 - ZoneWorldDocs.Count)}");
        }

        public bool ContainsZone(string name)
        {
            return ZoneWorldDocs.Any(r => string.Equals(r.AuthoredName, name, StringComparison.OrdinalIgnoreCase));
        }

        public bool ContainsRecord(string name)
        {
            return Records.Any(r => string.Equals(r.AuthoredName, name, StringComparison.OrdinalIgnoreCase));
        }

        private static DescriptorRecord CreateRecord(string path, GCNode node)
        {
            var record = new DescriptorRecord
            {
                AuthoredName = string.IsNullOrWhiteSpace(node.SourceFile) ? path : node.SourceFile,
                RegistryPath = path,
                ExtendsPath = node.Extends ?? "",
                Kind = Classify(path, node)
            };

            foreach (var propertyEntry in node.Properties)
                record.Properties[propertyEntry.Key] = propertyEntry.Value;
            CollectProperties(node, record);
            return record;
        }

        private static void CollectProperties(GCNode node, DescriptorRecord record)
        {
            if (node == null)
                return;
            foreach (var child in node.Children.Values)
            {
                foreach (var propertyEntry in child.Properties)
                    AddProperty(record, $"{child.Name}.{propertyEntry.Key}", propertyEntry.Value);
                CollectProperties(child, record);
            }
            for (int anonymousChildIndex = 0; anonymousChildIndex < node.AnonymousChildren.Count; anonymousChildIndex++)
            {
                GCNode child = node.AnonymousChildren[anonymousChildIndex];
                foreach (var propertyEntry in child.Properties)
                    AddProperty(record, $"*.{anonymousChildIndex}.{propertyEntry.Key}", propertyEntry.Value);
                CollectProperties(child, record);
            }
        }

        private static DescriptorKind Classify(string path, GCNode node)
        {
            string name = string.IsNullOrWhiteSpace(node.SourceFile) ? path : node.SourceFile;
            string extends = node.Extends ?? "";
            if (DungeonZoneName.IsMatch(name))
                return DescriptorKind.ZoneWorld;
            if (extends.IndexOf("EncounterTable", StringComparison.OrdinalIgnoreCase) >= 0)
                return DescriptorKind.EncounterTable;
            if (extends.IndexOf("StaticObject", StringComparison.OrdinalIgnoreCase) >= 0 || node.GetChild("Description")?.HasProperty("CollisionObject") == true)
                return DescriptorKind.StaticObject;
            if (node.HasProperty("Behavior") || node.GetChild("Description")?.HasProperty("CollisionRadius") == true)
                return DescriptorKind.Monster;
            if (name.IndexOf("loot", StringComparison.OrdinalIgnoreCase) >= 0)
                return DescriptorKind.Loot;
            return DescriptorKind.Other;
        }

        private static DescriptorKind ClassifySidecarEntry(SidecarEntry entry)
        {
            string name = entry?.Name ?? "";
            if (DungeonZoneName.IsMatch(name) || entry?.TypeCode == 15 || entry?.TypeCode == 16)
                return DescriptorKind.ZoneWorld;
            if (entry?.TypeCode == 4)
                return DescriptorKind.StaticObject;
            if (name.IndexOf("enc", StringComparison.OrdinalIgnoreCase) >= 0 &&
                name.IndexOf("world", StringComparison.OrdinalIgnoreCase) >= 0 &&
                name.IndexOf("dungeon", StringComparison.OrdinalIgnoreCase) >= 0)
                return DescriptorKind.EncounterTable;
            if (name.IndexOf("loot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("treasure", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("itemgenerator", StringComparison.OrdinalIgnoreCase) >= 0)
                return DescriptorKind.Loot;
            if (name.IndexOf("creatures", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("mob", StringComparison.OrdinalIgnoreCase) >= 0)
                return DescriptorKind.Monster;
            return DescriptorKind.Other;
        }

        private static void AddProperty(DescriptorRecord record, string key, string value)
        {
            if (!record.Properties.ContainsKey(key))
                record.Properties[key] = value;
        }
    }

    public sealed class DescriptorRecord
    {
        public string AuthoredName;
        public string RegistryPath;
        public string ExtendsPath;
        public DescriptorKind Kind;
        public readonly Dictionary<string, string> Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool HasProperty(string name)
        {
            return Properties.ContainsKey(name) || Properties.Keys.Any(k => k.EndsWith("." + name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public enum DescriptorKind
    {
        Other,
        ZoneWorld,
        EncounterTable,
        Monster,
        StaticObject,
        Loot
    }
}
