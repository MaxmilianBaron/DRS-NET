using System;
using System.Collections.Generic;
using System.Linq;
using DungeonRunners.Engine;
using DungeonRunners.Combat;
using DungeonRunners.Core;

namespace DungeonRunners.Gameplay
{
    public class ZoneSpawner
    {
        private static ZoneSpawner _instance;
        public static ZoneSpawner Instance => _instance ??= new ZoneSpawner();

        private HashSet<string> _spawnedZones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, DungeonMazeSpawner.ProceduralDungeonSnapshot> _proceduralSnapshots
            = new Dictionary<string, DungeonMazeSpawner.ProceduralDungeonSnapshot>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _instancePathMapsBuilt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private const uint MAZE_SEED = 0xBEEFBEEF;

        private const float EncounterScanRadiusFloor = 500f;
        private const float EncounterDefaultExtentXY = 50f;
        private const int ENCOUNTER_SCAN_TICKS = 30;
        private const float SpawnCollisionRadius = 6f;

        private readonly Dictionary<string, List<EncounterObject>> _encounterObjects
            = new Dictionary<string, List<EncounterObject>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _pendingZone
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool HasEncounterObjects(string instanceKey) =>
            !string.IsNullOrEmpty(instanceKey) && _encounterObjects.ContainsKey(instanceKey);

        private sealed class EncounterObject
        {
            public string GroupKey;
            public List<DatabaseLoader.DungeonSpawnData> Defs = new List<DatabaseLoader.DungeonSpawnData>();
            public float CenterX;
            public float CenterY;
            public float ScanRadius;
            public float MinX;
            public float MinY;
            public float MaxX;
            public float MaxY;
            public int ScanTimer;
            public bool Spawned;
            public int ChoiceIndex = -1;
        }

        public List<Monster> SpawnZoneMobs(string zoneName)
        {
            var spawned = new List<Monster>();

            if (string.IsNullOrEmpty(zoneName))
                return spawned;

            if (_spawnedZones.Contains(zoneName))
            {
                Debug.LogError($"[ZONE-SPAWNER] zone='{zoneName}' reason=already-spawned");
                return spawned;
            }

            List<DatabaseLoader.DungeonSpawnData> spawnDefs = GetSpawnData(zoneName);

            if (spawnDefs == null || spawnDefs.Count == 0)
            {
                Debug.LogError($"[ZONE-SPAWNER] zone='{zoneName}' reason=no-spawn-data");
                return spawned;
            }

            Debug.LogError($"[ZONE-SPAWNER] spawn zone='{zoneName}' count={spawnDefs.Count}");

            var pathMap = DungeonRunners.Core.PathMapCatalog.Instance.GetPathMap(zoneName);
            bool proceduralSpawns = DungeonMazeSpawner.IsProceduralZone(zoneName);

            foreach (var spawnDef in spawnDefs)
            {
                float correctedZ = spawnDef.posZ;

                var monster = CombatRuntime.Instance.SpawnMonster(
                    spawnDef.gcType,
                    spawnDef.posX,
                    spawnDef.posY,
                    correctedZ,
                    spawnDef.heading,
                    zoneName,
                    spawnDef.encounterGroupKey,
                    spawnDef.encounterDifficulty >= 0f ? spawnDef.encounterDifficulty : 1f,
                    spawnDef.spawnGcTypeOverride
                );

                if (monster != null)
                {
                    spawned.Add(monster);
                    if (correctedZ != spawnDef.posZ)
                        Debug.LogError($"[ZONE-SPAWNER] spawned monster={monster.Name} gc='{spawnDef.gcType}' pos=({spawnDef.posX},{spawnDef.posY},{correctedZ}) zSource=pathmap originalZ={spawnDef.posZ}");
                    else
                        Debug.LogError($"[ZONE-SPAWNER] spawned monster={monster.Name} gc='{spawnDef.gcType}' pos=({spawnDef.posX},{spawnDef.posY},{spawnDef.posZ})");
                }
                else
                {
                    Debug.LogError($"[ZONE-SPAWNER] gc='{spawnDef.gcType}' reason=missing-creature");
                }
            }


            _spawnedZones.Add(zoneName);
            Debug.LogError($"[ZONE-SPAWNER] complete zone='{zoneName}' spawned={spawned.Count}/{spawnDefs.Count}");
            return spawned;
        }

        private List<DatabaseLoader.DungeonSpawnData> GetSpawnData(string zoneName, uint? seed = null)
        {
            return GetNormalSpawnData(zoneName, seed);
        }

        private List<DatabaseLoader.DungeonSpawnData> GetNormalSpawnData(string zoneName, uint? seed = null)
        {
            if (DungeonMazeSpawner.IsProceduralZone(zoneName))
            {
                uint spawnSeed = seed ?? MAZE_SEED;
                Debug.LogError($"[ZONE-SPAWNER] zone='{zoneName}' mode=procedural seed=0x{spawnSeed:X8}");
                return DungeonMazeSpawner.GenerateSpawns(zoneName, spawnSeed);
            }

            if (DungeonMazeSpawner.IsStaticBossZone(zoneName))
            {
                uint spawnSeed = seed ?? MAZE_SEED;
                Debug.LogError($"[ZONE-SPAWNER] zone='{zoneName}' mode=static-boss seed=0x{spawnSeed:X8}");
                return DungeonMazeSpawner.GenerateStaticBossSpawns(zoneName, spawnSeed);
            }

            List<DatabaseLoader.DungeonSpawnData> staticSpawns;
            if (DatabaseLoader.DungeonSpawns.TryGetValue(zoneName, out staticSpawns) && staticSpawns.Count > 0)
            {
                Debug.LogError($"[ZONE-SPAWNER] zone='{zoneName}' mode=static count={staticSpawns.Count}");
                return staticSpawns;
            }

            return null;
        }

        public DungeonMazeSpawner.ProceduralDungeonSnapshot GetOrCreateProceduralSnapshot(
            string zoneName, string instanceKey, uint layoutSeed, uint roomSeed = 0)
        {
            if (string.IsNullOrEmpty(zoneName) || !DungeonMazeSpawner.IsProceduralZone(zoneName))
                return null;

            string key = string.IsNullOrEmpty(instanceKey) ? zoneName : instanceKey;
            uint clientLayoutSeed = layoutSeed != 0 ? layoutSeed : MAZE_SEED;
            if (layoutSeed == 0 && roomSeed != 0)
                Debug.LogError($"[DUNGEON-SNAPSHOT] instance='{key}' zone={zoneName} reason=missing-layout-seed roomSeed=0x{roomSeed:X8} fallbackLayout=0x{clientLayoutSeed:X8} state=partial source=separate-room-layout-stream");
            if (_proceduralSnapshots.TryGetValue(key, out var snapshot))
            {
                bool layoutMismatch = snapshot.LayoutSeed != clientLayoutSeed;
                bool roomMismatch = roomSeed != 0 && snapshot.RoomSeed != 0 && snapshot.RoomSeed != roomSeed;
                if ((layoutMismatch || roomMismatch) && !_spawnedZones.Contains(key))
                {
                    Debug.LogError($"[DUNGEON-SNAPSHOT] rebuild instance='{key}' zone={zoneName} oldLayout=0x{snapshot.LayoutSeed:X8} newLayout=0x{clientLayoutSeed:X8} requestedLayout=0x{layoutSeed:X8} oldRoom=0x{snapshot.RoomSeed:X8} newRoom=0x{roomSeed:X8}");
                    snapshot = DungeonMazeSpawner.GenerateSnapshot(zoneName, clientLayoutSeed, roomSeed);
                    _proceduralSnapshots[key] = snapshot;
                    BuildAndRegisterInstancePathMap(key, snapshot, rebuild: true);
                }
                else
                {
                    if (snapshot.RoomSeed == 0 && roomSeed != 0)
                    {
                        snapshot.RoomSeed = roomSeed;
                        Debug.LogError($"[DUNGEON-SNAPSHOT] resolved room seed instance='{key}' zone={zoneName} roomSeed=0x{roomSeed:X8}");
                    }
                    if (layoutMismatch || roomMismatch)
                        Debug.LogError($"[DUNGEON-SNAPSHOT] keep spawned snapshot instance='{key}' zone={zoneName} layoutSeed=0x{snapshot.LayoutSeed:X8} requestedLayout=0x{layoutSeed:X8} clientLayout=0x{clientLayoutSeed:X8} roomSeed=0x{snapshot.RoomSeed:X8} requestedRoom=0x{roomSeed:X8}");
                }
                Debug.LogError($"[DUNGEON-SNAPSHOT] reuse instance='{key}' zone={zoneName} layoutSeed=0x{snapshot.LayoutSeed:X8} requestedLayout=0x{layoutSeed:X8} roomSeed=0x{snapshot.RoomSeed:X8} entry=({snapshot.EntryGridX},{snapshot.EntryGridY}) tile='{snapshot.EntryTileType}' player=({snapshot.PlayerSpawn.x:F1},{snapshot.PlayerSpawn.y:F1},{snapshot.PlayerSpawn.z:F1}) entryPortal=({snapshot.EntryPortalSpawn.x:F1},{snapshot.EntryPortalSpawn.y:F1},{snapshot.EntryPortalSpawn.z:F1}) exit=({snapshot.ExitGridX},{snapshot.ExitGridY}) tile='{snapshot.ExitTileType}' exitPortal=({snapshot.ExitPortalSpawn.x:F1},{snapshot.ExitPortalSpawn.y:F1},{snapshot.ExitPortalSpawn.z:F1}) spawns={snapshot.Spawns.Count} yTransform=worldGridY=gridY/BuildWorld");
                return snapshot;
            }

            snapshot = DungeonMazeSpawner.GenerateSnapshot(zoneName, clientLayoutSeed, roomSeed);
            _proceduralSnapshots[key] = snapshot;
            BuildAndRegisterInstancePathMap(key, snapshot, rebuild: false);
            Debug.LogError($"[DUNGEON-SNAPSHOT] cache instance='{key}' zone={zoneName} layoutSeed=0x{clientLayoutSeed:X8} requestedLayout=0x{layoutSeed:X8} roomSeed=0x{roomSeed:X8} entry=({snapshot.EntryGridX},{snapshot.EntryGridY}) tile='{snapshot.EntryTileType}' player=({snapshot.PlayerSpawn.x:F1},{snapshot.PlayerSpawn.y:F1},{snapshot.PlayerSpawn.z:F1}) entryPortal=({snapshot.EntryPortalSpawn.x:F1},{snapshot.EntryPortalSpawn.y:F1},{snapshot.EntryPortalSpawn.z:F1}) exit=({snapshot.ExitGridX},{snapshot.ExitGridY}) tile='{snapshot.ExitTileType}' exitPortal=({snapshot.ExitPortalSpawn.x:F1},{snapshot.ExitPortalSpawn.y:F1},{snapshot.ExitPortalSpawn.z:F1}) cells={snapshot.Cells.Count} roomNodes={snapshot.RoomNodes.Count} spawns={snapshot.Spawns.Count} yTransform=worldGridY=gridY/BuildWorld");
            return snapshot;
        }

        private void BuildAndRegisterInstancePathMap(
            string key, DungeonMazeSpawner.ProceduralDungeonSnapshot snapshot, bool rebuild)
        {
            if (string.IsNullOrEmpty(key) || snapshot == null || snapshot.Cells == null || snapshot.Cells.Count == 0)
                return;
            if (!rebuild && _instancePathMapsBuilt.Contains(key))
                return;
            var pathMap = snapshot.PathMap ?? DungeonRunners.Utilities.PathMapBuilder.Build(key, snapshot.Cells);
            if (pathMap == null)
            {
                Debug.LogError($"[DUNGEON-SNAPSHOT] instance='{key}' zone={snapshot.ZoneName} reason=pathmap-build-null cells={snapshot.Cells.Count}");
                return;
            }
            PathMapCatalog.Instance.RegisterInstancePathMap(key, pathMap);
            _instancePathMapsBuilt.Add(key);
        }

        public bool TryGetProceduralSnapshot(string instanceKey, out DungeonMazeSpawner.ProceduralDungeonSnapshot snapshot)
        {
            snapshot = null;
            if (string.IsNullOrEmpty(instanceKey))
                return false;
            return _proceduralSnapshots.TryGetValue(instanceKey, out snapshot);
        }

        public List<Monster> SpawnZoneMobsForInstance(string zoneName, string instanceKey, uint? seed = null, uint roomSeed = 0)
        {
            var spawned = new List<Monster>();

            if (string.IsNullOrEmpty(zoneName))
                return spawned;

            if (_spawnedZones.Contains(instanceKey))
            {
                Debug.LogError($"[ZONE-SPAWNER] instance='{instanceKey}' reason=already-spawned");
                return spawned;
            }

            List<DatabaseLoader.DungeonSpawnData> spawnDefs;
            bool proceduralSpawns = DungeonMazeSpawner.IsProceduralZone(zoneName);
            if (proceduralSpawns)
            {
                uint spawnSeed = seed ?? MAZE_SEED;
                var snapshot = GetOrCreateProceduralSnapshot(zoneName, instanceKey, spawnSeed, roomSeed);
                spawnDefs = snapshot?.Spawns;
            }
            else
            {
                spawnDefs = GetSpawnData(zoneName, seed);
            }

            if (spawnDefs == null || spawnDefs.Count == 0)
            {
                Debug.LogError($"[ZONE-SPAWNER] zone='{zoneName}' reason=no-spawn-data");
                return spawned;
            }

            Debug.LogError($"[ZONE-SPAWNER] spawn instance='{instanceKey}' zone='{zoneName}' count={spawnDefs.Count}");

            if (proceduralSpawns)
            {
                RegisterEncounterObjects(instanceKey, zoneName, spawnDefs);
                _spawnedZones.Add(instanceKey);
                int encounterCount = _encounterObjects.TryGetValue(instanceKey, out var pe) ? pe.Count : 0;
                Debug.LogError($"[ENCOUNTER-OBJECT] instance='{instanceKey}' zone={zoneName} deferred {spawnDefs.Count} spawns into {encounterCount} encounters scan={ENCOUNTER_SCAN_TICKS}t sourceFunction=EncounterObject::update+ScanForPlayer");
                return spawned;
            }

            var pathMap = DungeonRunners.Core.PathMapCatalog.Instance.GetPathMap(instanceKey)
                ?? DungeonRunners.Core.PathMapCatalog.Instance.GetPathMap(zoneName);

            foreach (var spawnDef in spawnDefs)
            {
                float correctedZ = spawnDef.posZ;

                var monster = CombatRuntime.Instance.SpawnMonster(
                    spawnDef.gcType,
                    spawnDef.posX,
                    spawnDef.posY,
                    correctedZ,
                    spawnDef.heading,
                    zoneName,
                    spawnDef.encounterGroupKey,
                    spawnDef.encounterDifficulty >= 0f ? spawnDef.encounterDifficulty : 1f,
                    spawnDef.spawnGcTypeOverride,
                    instanceKey
                );

                if (monster != null)
                {
                    spawned.Add(monster);
                    Debug.LogError($"[DUNGEON-SPAWN] instance='{instanceKey}' zone={zoneName} role={spawnDef.placementRole ?? ""} group={spawnDef.encounterGroupKey ?? ""} grid=({spawnDef.gridX},{spawnDef.gridY}) tile='{spawnDef.tileType ?? ""}' origin=({spawnDef.worldOriginX:F1},{spawnDef.worldOriginY:F1}) local=({spawnDef.localX:F1},{spawnDef.localY:F1},{spawnDef.localZ:F1}) placeholder='{spawnDef.placeholderSource ?? ""}' marker={spawnDef.placeholderIndex} size=({spawnDef.placeholderSizeX:F1},{spawnDef.placeholderSizeY:F1}) choice={spawnDef.encounterChoiceIndex} snapApplied={spawnDef.snapApplied} gc='{spawnDef.gcType}' spawnGc='{spawnDef.spawnGcTypeOverride ?? ""}' pos=({spawnDef.posX:F1},{spawnDef.posY:F1},{correctedZ:F1}) heading={spawnDef.heading:F1} difficulty={spawnDef.encounterDifficulty:F2} zSource={(proceduralSpawns ? "snapshot" : "pathmap")} monster={monster.Name} level={monster.Level} tier={monster.Tier ?? ""} maxHP={monster.MaxHPWire}");
                }
            }


            _spawnedZones.Add(instanceKey);
            Debug.LogError($"[ZONE-SPAWNER] complete instance='{instanceKey}' spawned={spawned.Count}/{spawnDefs.Count}");
            return spawned;
        }

        private void RegisterEncounterObjects(string instanceKey, string zoneName, List<DatabaseLoader.DungeonSpawnData> spawnDefs)
        {
            _pendingZone[instanceKey] = zoneName;
            var byGroup = new Dictionary<string, EncounterObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var spawnDef in spawnDefs)
            {
                string key = string.IsNullOrEmpty(spawnDef.encounterGroupKey)
                    ? $"solo:{spawnDef.posX:F0},{spawnDef.posY:F0}"
                    : spawnDef.encounterGroupKey;
                if (!byGroup.TryGetValue(key, out var encounter))
                {
                    encounter = new EncounterObject { GroupKey = key, ScanTimer = 0 };
                    byGroup[key] = encounter;
                }
                encounter.Defs.Add(spawnDef);
            }

            var list = new List<EncounterObject>();
            foreach (var encounter in byGroup.Values)
            {
                float sumX = 0f, sumY = 0f;
                int choiceIndex = -1;
                foreach (var spawnDef in encounter.Defs)
                {
                    sumX += spawnDef.posX;
                    sumY += spawnDef.posY;
                    if (choiceIndex < 0 && spawnDef.encounterChoiceIndex >= 0)
                        choiceIndex = spawnDef.encounterChoiceIndex;
                }
                encounter.CenterX = sumX / encounter.Defs.Count;
                encounter.CenterY = sumY / encounter.Defs.Count;
                encounter.ScanRadius = ResolveEncounterScanRadius(encounter.Defs);
                ResolveEncounterBounds(encounter.Defs, encounter.CenterX, encounter.CenterY, encounter.ScanRadius,
                    out encounter.MinX, out encounter.MinY, out encounter.MaxX, out encounter.MaxY);
                encounter.ChoiceIndex = choiceIndex;
                list.Add(encounter);
            }
            _encounterObjects[instanceKey] = list;
        }

        private static float ResolveEncounterScanRadius(List<DatabaseLoader.DungeonSpawnData> defs)
        {
            float radius = 0f;
            if (defs != null)
            {
                foreach (var def in defs)
                {
                    if (def == null)
                        continue;

                    radius = Mathf.Max(radius, ResolveEncounterExtentX(def));
                    radius = Mathf.Max(radius, ResolveEncounterExtentY(def));
                }
            }

            return Mathf.Max(EncounterScanRadiusFloor, radius);
        }

        private static float ResolveEncounterExtentX(DatabaseLoader.DungeonSpawnData def)
        {
            return def != null && def.placeholderSizeX > 0f
                ? def.placeholderSizeX * 0.5f
                : EncounterDefaultExtentXY;
        }

        private static float ResolveEncounterExtentY(DatabaseLoader.DungeonSpawnData def)
        {
            return def != null && def.placeholderSizeY > 0f
                ? def.placeholderSizeY * 0.5f
                : EncounterDefaultExtentXY;
        }

        private static void ResolveEncounterBounds(List<DatabaseLoader.DungeonSpawnData> defs, float centerX, float centerY, float scanRadius,
            out float minX, out float minY, out float maxX, out float maxY)
        {
            minX = float.MaxValue;
            minY = float.MaxValue;
            maxX = float.MinValue;
            maxY = float.MinValue;

            if (defs != null)
            {
                foreach (var def in defs)
                {
                    if (def == null)
                        continue;

                    float triggerX = def.worldOriginX + def.localX;
                    float triggerY = def.worldOriginY + def.localY;
                    if (Mathf.Abs(triggerX) <= 0.001f && Mathf.Abs(triggerY) <= 0.001f)
                    {
                        triggerX = def.posX;
                        triggerY = def.posY;
                    }

                    float halfX = ResolveEncounterExtentX(def);
                    float halfY = ResolveEncounterExtentY(def);
                    minX = Mathf.Min(minX, triggerX - halfX);
                    minY = Mathf.Min(minY, triggerY - halfY);
                    maxX = Mathf.Max(maxX, triggerX + halfX);
                    maxY = Mathf.Max(maxY, triggerY + halfY);
                }
            }

            if (minX == float.MaxValue)
            {
                float radius = scanRadius > 0f ? scanRadius : EncounterScanRadiusFloor;
                minX = centerX - radius;
                minY = centerY - radius;
                maxX = centerX + radius;
                maxY = centerY + radius;
            }
        }

        public List<Monster> UpdateEncounterObjects(string instanceKey, List<(float x, float y)> playerPositions)
        {
            var spawnedMonsters = new List<Monster>();
            if (string.IsNullOrEmpty(instanceKey) || playerPositions == null || playerPositions.Count == 0)
                return spawnedMonsters;
            if (!_encounterObjects.TryGetValue(instanceKey, out var list) || list == null)
                return spawnedMonsters;
            string zoneName = _pendingZone.TryGetValue(instanceKey, out var pendingZoneName) ? pendingZoneName : instanceKey;

            foreach (var encounter in list)
            {
                if (encounter.Spawned)
                    continue;
                if (encounter.ScanTimer > 0)
                {
                    encounter.ScanTimer--;
                    continue;
                }
                encounter.ScanTimer = ENCOUNTER_SCAN_TICKS;

                float scanRadius = encounter.ScanRadius > 0f ? encounter.ScanRadius : EncounterScanRadiusFloor;
                bool playerInRange = false;
                foreach (var playerPosition in playerPositions)
                {
                    float nearestX = playerPosition.x < encounter.MinX ? encounter.MinX : (playerPosition.x > encounter.MaxX ? encounter.MaxX : playerPosition.x);
                    float nearestY = playerPosition.y < encounter.MinY ? encounter.MinY : (playerPosition.y > encounter.MaxY ? encounter.MaxY : playerPosition.y);
                    float ddx = playerPosition.x - nearestX;
                    float ddy = playerPosition.y - nearestY;
                    if (ddx * ddx + ddy * ddy <= scanRadius * scanRadius)
                    {
                        playerInRange = true;
                        break;
                    }
                }
                if (!playerInRange)
                    continue;

                encounter.Spawned = true;
                int spawnedCount = 0;
                foreach (var spawnDef in encounter.Defs)
                {
                    float spawnX = spawnDef.posX;
                    float spawnY = spawnDef.posY;
                    if (WorldCollision.Instance.TryFindClearSpawnPosition(zoneName, instanceKey, spawnDef.posX, spawnDef.posY, spawnDef.posZ, SpawnCollisionRadius, out float clearX, out float clearY))
                    {
                        spawnX = clearX;
                        spawnY = clearY;
                        Debug.LogError($"[ENCOUNTER-SPAWN-COBJ] instance='{instanceKey}' group='{encounter.GroupKey}' gc='{spawnDef.gcType}' raw=({spawnDef.posX:F1},{spawnDef.posY:F1}) clear=({clearX:F1},{clearY:F1}) sourceFunction=EncounterObject::PendingSpawnUnit+PathMap::FindFirstConnectedPointInDir+WorldCollision");
                    }
                    float spawnZ = spawnDef.posZ;
                    var monster = CombatRuntime.Instance.SpawnMonster(
                        spawnDef.gcType,
                        spawnX,
                        spawnY,
                        spawnZ,
                        spawnDef.heading,
                        zoneName,
                        spawnDef.encounterGroupKey,
                        spawnDef.encounterDifficulty >= 0f ? spawnDef.encounterDifficulty : 1f,
                        spawnDef.spawnGcTypeOverride,
                        instanceKey);
                    if (monster != null)
                    {
                        spawnedMonsters.Add(monster);
                        spawnedCount++;
                    }
                }
                Debug.LogError($"[ENCOUNTER-OBJECT] gen instance='{instanceKey}' group='{encounter.GroupKey}' center=({encounter.CenterX:F1},{encounter.CenterY:F1}) bbox=({encounter.MinX:F1},{encounter.MinY:F1})-({encounter.MaxX:F1},{encounter.MaxY:F1}) choice={encounter.ChoiceIndex} spawned={spawnedCount} scanRadius={encounter.ScanRadius:F1} sourceFunction=EncounterObject::update+GenerateObjectFromTable");
            }

            if (list.TrueForAll(encounter => encounter.Spawned))
            {
                _encounterObjects.Remove(instanceKey);
                _pendingZone.Remove(instanceKey);
            }
            return spawnedMonsters;
        }

        public bool HasSpawnsForZone(string zoneName)
        {
            return DungeonMazeSpawner.IsProceduralZone(zoneName) ||
                   DungeonMazeSpawner.IsStaticBossZone(zoneName) ||
                   DatabaseLoader.DungeonSpawns.ContainsKey(zoneName);
        }

        public bool IsZoneSpawned(string zoneName)
        {
            return _spawnedZones.Contains(zoneName);
        }

        public void ResetZone(string zoneName)
        {
            _spawnedZones.Remove(zoneName);
            _proceduralSnapshots.Remove(zoneName);
            _encounterObjects.Remove(zoneName);
            _pendingZone.Remove(zoneName);
        }

        public void ResetZoneAndInstances(string baseZoneName)
        {
            var toRemove = _spawnedZones
                .Where(z => z.Equals(baseZoneName, StringComparison.OrdinalIgnoreCase) ||
                            z.StartsWith(baseZoneName + "_inst", StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var zoneKey in toRemove)
            {
                _spawnedZones.Remove(zoneKey);
                _proceduralSnapshots.Remove(zoneKey);
            }
            var snapshotKeys = _proceduralSnapshots.Keys
                .Where(z => z.Equals(baseZoneName, StringComparison.OrdinalIgnoreCase) ||
                            z.StartsWith(baseZoneName + "_inst", StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var zoneKey in snapshotKeys)
                _proceduralSnapshots.Remove(zoneKey);
            _proceduralSnapshots.Remove(baseZoneName);
            foreach (var zoneKey in toRemove)
            {
                _encounterObjects.Remove(zoneKey);
                _pendingZone.Remove(zoneKey);
            }
            _encounterObjects.Remove(baseZoneName);
            _pendingZone.Remove(baseZoneName);
            Debug.LogError($"[ZONE-SPAWNER] reset zone='{baseZoneName}' cleared={toRemove.Count}");
        }

        public void ResetAll()
        {
            _spawnedZones.Clear();
            _proceduralSnapshots.Clear();
            _encounterObjects.Clear();
            _pendingZone.Clear();
        }
    }
}
