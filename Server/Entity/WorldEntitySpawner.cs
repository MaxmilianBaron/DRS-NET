using System;
using System.Collections.Generic;
using System.Linq;
using DungeonRunners.Engine;
using DB = DungeonRunners.Database.GameDatabase;
using DungeonRunners.Utilities;

namespace DungeonRunners.Gameplay
{
    public class WorldEntitySpawner
    {
        private static WorldEntitySpawner _instance;
        public static WorldEntitySpawner Instance => _instance ??= new WorldEntitySpawner();

        private Dictionary<string, List<WorldEntityData>> _entitiesByZone
            = new Dictionary<string, List<WorldEntityData>>(StringComparer.OrdinalIgnoreCase);

        private Dictionary<ushort, WorldEntityData> _spawnedEntities
            = new Dictionary<ushort, WorldEntityData>();


        public void Initialize()
        {
            _entitiesByZone.Clear();
            int count = 0;

            using (var connection = DB.GetConnection())
            {
                try
                {
                    using (var reader = DB.ExecuteReader(connection, "SELECT * FROM zone_world_entities"))
                    {
                        while (reader.Read())
                        {
                            var entityData = new WorldEntityData
                            {
                                Id = DB.GetInt(reader, "id"),
                                Zone = DB.GetString(reader, "zone"),
                                Name = DB.GetString(reader, "name"),
                                GCType = DB.GetString(reader, "gc_type"),
                                EntityType = DB.GetString(reader, "entity_type"),
                                PosX = DB.GetFloat(reader, "pos_x"),
                                PosY = DB.GetFloat(reader, "pos_y"),
                                PosZ = DB.GetFloat(reader, "pos_z"),
                                Heading = DB.GetFloat(reader, "heading"),
                                Flags = (uint)DB.GetInt(reader, "flags"),
                                ItemGenerator = DB.GetString(reader, "item_generator"),
                                ItemCount = DB.GetInt(reader, "item_count"),
                                ItemGenerator2 = DB.GetString(reader, "item_generator2"),
                                ItemCount2 = DB.GetInt(reader, "item_count2"),
                                ItemGenerator3 = DB.GetString(reader, "item_generator3"),
                                ItemCount3 = DB.GetInt(reader, "item_count3"),
                                ItemGenerator4 = DB.GetString(reader, "item_generator4"),
                                ItemCount4 = DB.GetInt(reader, "item_count4"),
                                ItemGenerator5 = DB.GetString(reader, "item_generator5"),
                                ItemCount5 = DB.GetInt(reader, "item_count5"),
                                TargetZone = DB.GetString(reader, "target_zone"),
                                TargetSpawn = DB.GetString(reader, "target_spawn"),
                                Label = DB.GetString(reader, "label"),
                                AllowMultiple = DB.GetInt(reader, "allow_multiple") != 0
                            };

                            string key = entityData.Zone.ToLower();
                            if (!_entitiesByZone.ContainsKey(key))
                                _entitiesByZone[key] = new List<WorldEntityData>();
                            _entitiesByZone[key].Add(entityData);
                            count++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[WORLD-ENTITY-SPAWNER] table=zone_world_entities state=error message='{ex.Message}'");
                    Debug.LogError($"[WORLD-ENTITY-SPAWNER] table=zone_world_entities state=missing");
                    return;
                }
            }

            Debug.LogError($"[WORLD-ENTITY-SPAWNER] loaded={count} zones={_entitiesByZone.Count}");
        }


        public List<SpawnedWorldEntity> GetEntitiesForZone(string zoneName)
        {
            var result = new List<SpawnedWorldEntity>();
            if (string.IsNullOrEmpty(zoneName)) return result;

            string key = zoneName.ToLower();
            if (!_entitiesByZone.TryGetValue(key, out var entities))
                return result;

            foreach (var e in entities)
                result.Add(new SpawnedWorldEntity { Data = e });

            return result;
        }

        public static void WriteEntitySpawn(LEWriter writer, ushort entityId, ushort behaviorId, WorldEntityData data)
        {
            writer.WriteByte(0x01);
            writer.WriteUInt16(entityId);
            WriteGCType(writer, data.GCType);

            writer.WriteByte(0x02);
            writer.WriteUInt16(entityId);
            writer.WriteUInt32(data.Flags);

            writer.WriteInt32((int)(data.PosX * 256));
            writer.WriteInt32((int)(data.PosY * 256));
            writer.WriteInt32((int)(data.PosZ * 256));
            writer.WriteInt32((int)(data.Heading * 256));

            writer.WriteByte(0x00);

            if (data.IsGate)
            {
                writer.WriteByte(0x00);
                writer.WriteByte(0x00);
            }
            else
            {
                writer.WriteByte(0x00);
                writer.WriteByte(0x00);
                writer.WriteUInt16(0);
                writer.WriteUInt16(0);

                writer.WriteByte(0x00);
                writer.WriteByte(0x00);
                writer.WriteUInt16(0);

                writer.WriteByte(0x32);
                writer.WriteUInt16(entityId);
                writer.WriteUInt16(behaviorId);
                WriteGCType(writer, "base.noncombatinteractive.behavior");

                writer.WriteByte(0x01);

                writer.WriteByte(0xFF);
                writer.WriteByte(0x00);
                writer.WriteByte(0x00);
                writer.WriteByte(0x01);

                writer.WriteByte(0x08);
                writer.WriteInt32((int)(data.Heading * 256));
                writer.WriteInt32((int)(data.Heading * 256));
                writer.WriteByte(0x00);

                writer.WriteByte(0xFF);
                writer.WriteByte(0x00);
                writer.WriteByte(0x00);
            }
        }


        public void TrackSpawnedEntity(ushort entityId, WorldEntityData data)
        {
            _spawnedEntities[entityId] = data;
        }

        public bool TryGetEntity(ushort entityId, out WorldEntityData data)
        {
            return _spawnedEntities.TryGetValue(entityId, out data);
        }

        public bool FindEntityByName(string name, string zoneName, out ushort entityId, out WorldEntityData data)
        {
            string baseZone = zoneName;
            int instIdx = zoneName.IndexOf("_inst", StringComparison.OrdinalIgnoreCase);
            if (instIdx > 0) baseZone = zoneName.Substring(0, instIdx);

            foreach (var entityEntry in _spawnedEntities)
            {
                var entity = entityEntry.Value;
                if (string.Equals(entity.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(entity.Zone, baseZone, StringComparison.OrdinalIgnoreCase))
                {
                    entityId = entityEntry.Key;
                    data = entity;
                    return true;
                }
            }
            entityId = 0;
            data = null;
            return false;
        }

        public Dictionary<ushort, WorldEntityData> GetSpawnedEntities()
        {
            return _spawnedEntities;
        }

        public void RemoveEntity(ushort entityId)
        {
            _spawnedEntities.Remove(entityId);
        }

        public void ClearZoneEntities()
        {
            _spawnedEntities.Clear();
        }

        public void SeedZoneEntity(WorldEntityData data)
        {
            if (data == null || string.IsNullOrEmpty(data.Zone)) return;
            string key = data.Zone.ToLower();
            if (!_entitiesByZone.ContainsKey(key))
                _entitiesByZone[key] = new List<WorldEntityData>();
            _entitiesByZone[key].Add(data);
        }

        public void SeedPvpArenaGates()
        {
            void Gate(string zone, string side, float x, float y, float z, float heading)
            {
                SeedZoneEntity(new WorldEntityData
                {
                    Zone = zone,
                    Name = "pvp_" + side + "_gate",
                    GCType = "world.pvp.data.pvp_" + side + "_gate",
                    EntityType = "gate",
                    Label = "pvp_" + side + "_gate",
                    PosX = x, PosY = y, PosZ = z, Heading = heading,
                    Flags = 7,
                    AllowMultiple = true
                });
            }
            Gate("DeathMatch01", "blue", -380, 160, 10, 0);
            Gate("DeathMatch01", "red", 340, 160, 10, 0);
            Gate("DeathMatch02", "blue", -300, 140, 10, 0);
            Gate("DeathMatch02", "red", 300, 135, 10, 0);
            Gate("DeathMatch03", "blue", -260, 250, 10, 0);
            Gate("DeathMatch03", "blue", -260, 70, 10, 0);
            Gate("DeathMatch03", "red", 260, 70, 10, 0);
            Gate("DeathMatch03", "red", 260, 250, 10, 0);
            Gate("DeathMatch04", "blue", 264, -185, 41, 90);
            Gate("DeathMatch04", "red", -140, 246, 41, 90);

            int total = 0;
            foreach (var z in new[] { "deathmatch01", "deathmatch02", "deathmatch03", "deathmatch04" })
                if (_entitiesByZone.TryGetValue(z, out var l)) total += l.Count(e => e.IsGate);
            Debug.LogError($"[PVP-GATES] seeded {total} arena gate entities across DeathMatch01-04 (Flags=6)");
        }


        private static void WriteGCType(LEWriter writer, string typeName)
        {
            writer.WriteByte(0xFF);
            writer.WriteCString(typeName);
        }
    }


    [Serializable]
    public class WorldEntityData
    {
        public int Id;
        public string Zone;
        public string Name;
        public string GCType;
        public string EntityType;
        public float PosX, PosY, PosZ;
        public float Heading;
        public uint Flags;

        public string ItemGenerator;
        public int ItemCount;
        public string ItemGenerator2;
        public int ItemCount2;
        public string ItemGenerator3;
        public int ItemCount3;
        public string ItemGenerator4;
        public int ItemCount4;
        public string ItemGenerator5;
        public int ItemCount5;

        public string TargetZone;
        public string TargetSpawn;

        public string Label;
        public bool AllowMultiple;

        public bool IsChest => EntityType == "chest";
        public bool IsTeleporter => EntityType == "teleporter";
        public bool IsShrine => EntityType == "shrine";
        public bool IsGate => EntityType == "gate";
        public bool IsNPC => EntityType == "npc";

        public IEnumerable<(string Generator, int Count, int Slot)> GetChestGenerators(string fallbackGenerator, int fallbackCount)
        {
            var storedGenerators = new (string Generator, int Count)[]
            {
                (ItemGenerator, ItemCount),
                (ItemGenerator2, ItemCount2),
                (ItemGenerator3, ItemCount3),
                (ItemGenerator4, ItemCount4),
                (ItemGenerator5, ItemCount5)
            };
            foreach (var generator in WorldEntityLoot.GetNonCombatInteractiveItemGenerators(GCType, storedGenerators, fallbackGenerator, fallbackCount))
                yield return generator;
        }
    }

    public class SpawnedWorldEntity
    {
        public WorldEntityData Data;
        public ushort EntityId;
    }
}
