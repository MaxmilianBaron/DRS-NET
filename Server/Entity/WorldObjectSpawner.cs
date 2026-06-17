using System;
using System.Collections.Generic;
using DungeonRunners.Combat;
using DungeonRunners.Engine;

namespace DungeonRunners.Gameplay
{

    public static class WorldObjectSpawner
    {
        private const string WorldObjectRngContract = "GCObjectGeneratorTable<WorldEntity>::GenerateObjectFromTable@0x00568150 Random::generate@0x0044B1F0";

        private static readonly string[] BarrelTypes =
        {
            "terrain.misc.interactives.Breakiable_Barrel_01",
            "terrain.misc.interactives.Breakiable_Barrel_02",
            "terrain.misc.interactives.Breakiable_Barrel_03",
        };
        private const string CrateType = "world.objects.crate.breakable";

        private static readonly string[] SmallChestTypes =
        {
            "terrain.interactives.loot.Chest_Sm_01",
            "terrain.interactives.loot.Chest_Sm_02",
            "terrain.interactives.loot.Chest_Sm_03",
        };
        private static readonly string[] MediumChestTypes =
        {
            "terrain.interactives.loot.Chest_Md_01",
            "terrain.interactives.loot.Chest_Md_02",
            "terrain.interactives.loot.Chest_Md_03",
        };
        private const string LargeChestType = "terrain.interactives.loot.Chest_Lg_01";

        private static readonly float[][] ObjectOffsets =
        {
            new[] { -40f, -30f }, new[] {  35f, -25f },
            new[] { -30f,  40f }, new[] {  45f,  35f },
            new[] { -50f,   0f }, new[] {   0f, -50f },
            new[] {  25f,  50f }, new[] { -45f,  20f },
        };


        public static List<DatabaseLoader.DungeonSpawnData> GenerateBarrels(
            string zoneName, uint seed = 0xBEEFBEEF)
        {
            var spawns = new List<DatabaseLoader.DungeonSpawnData>();
            if (!DungeonMazeSpawner.IsProceduralZone(zoneName)) return spawns;

            var cells = GetMazeCells(zoneName, seed);

            int cellIndex = 0;
            foreach (var cell in cells)
            {
                if (cell.isEntry)
                {
                    cellIndex++;
                    continue;
                }

                string cellOwner = $"{zoneName}:cell{cellIndex}";

                if (NextWorldObjectChance(40, "WorldObjectSpawner.barrel.cellChance", cellOwner))
                {
                    int count = NextWorldObjectIntInclusive(1, 2, "WorldObjectSpawner.barrel.count", cellOwner);
                    for (int barrelIndex = 0; barrelIndex < count; barrelIndex++)
                    {
                        string barrelOwner = $"{cellOwner}:barrel{barrelIndex}";
                        var off = ObjectOffsets[NextWorldObjectIntInclusive(0, ObjectOffsets.Length - 1, "WorldObjectSpawner.barrel.offset", barrelOwner)];
                        float spawnX = cell.cx + off[0] + NextWorldObjectFloat(-5f, 5f, "WorldObjectSpawner.barrel.jitterX", barrelOwner);
                        float spawnY = cell.cy + off[1] + NextWorldObjectFloat(-5f, 5f, "WorldObjectSpawner.barrel.jitterY", barrelOwner);
                        spawns.Add(new DatabaseLoader.DungeonSpawnData
                        {
                            zoneName = zoneName,
                            gcType = BarrelTypes[NextWorldObjectIntInclusive(0, BarrelTypes.Length - 1, "WorldObjectSpawner.barrel.type", barrelOwner)],
                            posX = spawnX,
                            posY = spawnY,
                            posZ = Core.PathMapCatalog.Instance.GetHeight(zoneName, spawnX, spawnY, 10f),
                            heading = NextWorldObjectIntInclusive(0, 359, "WorldObjectSpawner.barrel.heading", barrelOwner)
                        });
                    }
                }

                if (NextWorldObjectChance(15, "WorldObjectSpawner.crate.cellChance", cellOwner))
                {
                    var off = ObjectOffsets[NextWorldObjectIntInclusive(0, ObjectOffsets.Length - 1, "WorldObjectSpawner.crate.offset", cellOwner)];
                    float spawnX = cell.cx + off[0];
                    float spawnY = cell.cy + off[1];
                    spawns.Add(new DatabaseLoader.DungeonSpawnData
                    {
                        zoneName = zoneName,
                        gcType = CrateType,
                        posX = spawnX,
                        posY = spawnY,
                        posZ = Core.PathMapCatalog.Instance.GetHeight(zoneName, spawnX, spawnY, 10f),
                        heading = NextWorldObjectIntInclusive(0, 359, "WorldObjectSpawner.crate.heading", cellOwner)
                    });
                }

                cellIndex++;
            }

            Debug.LogError($"[WORLD-OBJECTS] zone={zoneName} kind=barrel-crate count={spawns.Count}");
            return spawns;
        }


        public static List<ChestSpawnData> GenerateChests(
            string zoneName, uint seed = 0xBEEFBEEF)
        {
            var chests = new List<ChestSpawnData>();
            if (!DungeonMazeSpawner.IsProceduralZone(zoneName)) return chests;

            var cells = GetMazeCells(zoneName, seed);

            int cellIndex = 0;
            foreach (var cell in cells)
            {
                if (cell.isEntry) { cellIndex++; continue; }

                string cellOwner = $"{zoneName}:cell{cellIndex}";

                if (NextWorldObjectChance(20, "WorldObjectSpawner.chest.smallChance", cellOwner))
                {
                    var off = ObjectOffsets[(cellIndex + 3) % ObjectOffsets.Length];
                    float spawnX = cell.cx + off[0];
                    float spawnY = cell.cy + off[1];
                    SnapAndGetHeight(zoneName, ref spawnX, ref spawnY, out float spawnZ);
                    chests.Add(new ChestSpawnData
                    {
                        GCType = SmallChestTypes[NextWorldObjectIntInclusive(0, SmallChestTypes.Length - 1, "WorldObjectSpawner.chest.smallType", cellOwner)],
                        Label = "Treasure Chest",
                        PosX = spawnX,
                        PosY = spawnY,
                        PosZ = spawnZ,
                        Heading = NextWorldObjectIntInclusive(0, 359, "WorldObjectSpawner.chest.smallHeading", cellOwner),
                    });
                }

                if (NextWorldObjectChance(5, "WorldObjectSpawner.chest.mediumChance", cellOwner))
                {
                    var off = ObjectOffsets[(cellIndex + 5) % ObjectOffsets.Length];
                    float spawnX = cell.cx + off[0];
                    float spawnY = cell.cy + off[1];
                    SnapAndGetHeight(zoneName, ref spawnX, ref spawnY, out float spawnZ);
                    chests.Add(new ChestSpawnData
                    {
                        GCType = MediumChestTypes[NextWorldObjectIntInclusive(0, MediumChestTypes.Length - 1, "WorldObjectSpawner.chest.mediumType", cellOwner)],
                        Label = "Large Treasure Chest",
                        PosX = spawnX,
                        PosY = spawnY,
                        PosZ = spawnZ,
                        Heading = NextWorldObjectIntInclusive(0, 359, "WorldObjectSpawner.chest.mediumHeading", cellOwner),
                    });
                }

                cellIndex++;
            }

            if (cells.Count > 2)
            {
                var lastCell = cells[cells.Count - 1];
                float largeChestX = lastCell.cx;
                float largeChestY = lastCell.cy + 30f;
                SnapAndGetHeight(zoneName, ref largeChestX, ref largeChestY, out float largeChestZ);
                chests.Add(new ChestSpawnData
                {
                    GCType = LargeChestType,
                    Label = "Grand Treasure Chest",
                    PosX = largeChestX,
                    PosY = largeChestY,
                    PosZ = largeChestZ,
                    Heading = 0,
                });
            }

            Debug.LogError($"[WORLD-OBJECTS] zone={zoneName} kind=treasure-chest count={chests.Count}");
            return chests;
        }

        private static void SnapAndGetHeight(string zoneName, ref float x, ref float y, out float z)
        {
            z = Core.PathMapCatalog.Instance.GetHeight(zoneName, x, y, 50f) + 3f;
        }


        private static uint NextWorldObjectRaw(string phase, string owner)
        {
            uint raw = RandomStreams.GenerateGlobalStatic(phase, owner);
            Debug.LogError($"[WORLD-OBJECT-RNG] stream=globalStatic phase={phase} raw=0x{raw:X8} owner='{owner}' sourceFunction={WorldObjectRngContract}");
            return raw;
        }

        private static int NextWorldObjectIntInclusive(int minInclusive, int maxInclusive, string phase, string owner)
        {
            if (maxInclusive <= minInclusive)
                return minInclusive;
            uint value = RandomStreams.GenerateGlobalStaticRangeInclusive((uint)minInclusive, (uint)maxInclusive, phase, owner);
            Debug.LogError($"[WORLD-OBJECT-RNG] stream=globalStatic phase={phase} value={value} range=[{minInclusive}..{maxInclusive}] owner='{owner}' sourceFunction={WorldObjectRngContract}");
            return (int)value;
        }

        private static bool NextWorldObjectChance(int percent, string phase, string owner)
        {
            int clamped = Math.Max(0, Math.Min(100, percent));
            int roll = NextWorldObjectIntInclusive(0, 99, phase, owner);
            bool result = roll < clamped;
            Debug.LogError($"[WORLD-OBJECT-RNG] stream=globalStatic phase={phase}.result roll={roll} percent={clamped} result={result} owner='{owner}' sourceFunction={WorldObjectRngContract}");
            return result;
        }

        private static float NextWorldObjectFloat(float minInclusive, float maxExclusive, string phase, string owner)
        {
            if (maxExclusive <= minInclusive)
                return minInclusive;
            uint raw = NextWorldObjectRaw(phase, owner);
            float unit = (raw & 0x00FFFFFF) / 16777216f;
            float value = minInclusive + unit * (maxExclusive - minInclusive);
            Debug.LogError($"[WORLD-OBJECT-RNG] stream=globalStatic phase={phase} value={value:F3} range=[{minInclusive:F3}..{maxExclusive:F3}) owner='{owner}' sourceFunction={WorldObjectRngContract}");
            return value;
        }

        public static bool IsDestroyableObject(string gcType)
        {
            if (string.IsNullOrEmpty(gcType)) return false;
            if (gcType.StartsWith("world.objects.barrel", StringComparison.OrdinalIgnoreCase) ||
                gcType.StartsWith("world.objects.crate", StringComparison.OrdinalIgnoreCase))
                return true;
            for (int barrelTypeIndex = 0; barrelTypeIndex < BarrelTypes.Length; barrelTypeIndex++)
            {
                if (string.Equals(gcType, BarrelTypes[barrelTypeIndex], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private struct CellPos { public float cx, cy; public bool isEntry; }

        private static List<CellPos> GetMazeCells(string zoneName, uint seed)
        {
            if (!DungeonMazeSpawner.TryGetMazeDimensions(zoneName,
                out int width, out int height, out int entryX, out int entryY,
                out int randomness, out int sparseness, out int deadEndRemoval))
                return new List<CellPos>();

            var maze = new MazeGenerator(width, height, seed,
                randomness, sparseness, deadEndRemoval);
            var cells = maze.BuildWorld();
            var result = new List<CellPos>();
            foreach (var mazeCell in cells)
                result.Add(new CellPos
                {
                    cx = mazeCell.WorldCenterX,
                    cy = mazeCell.WorldCenterY,
                    isEntry = (mazeCell.GridX == entryX && mazeCell.GridY == entryY)
                });
            return result;
        }
    }


    public class ChestSpawnData
    {
        public string GCType;
        public string Label;
        public float PosX, PosY, PosZ;
        public float Heading;
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
}
