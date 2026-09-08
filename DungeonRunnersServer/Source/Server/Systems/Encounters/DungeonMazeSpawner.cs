using System;
using System.Collections.Generic;
using System.IO;
using DungeonRunners.Engine;
using DungeonRunners.Combat;
using DungeonRunners.Core;
using DungeonRunners.Data;
using DungeonRunners.Utilities;
using CombatRandom = DungeonRunners.Combat.MersenneTwister;

namespace DungeonRunners.Gameplay
{
    public static class DungeonMazeSpawner
    {
        public sealed class ProceduralDungeonSnapshot
        {
            public string ZoneName;
            public uint LayoutSeed;
            public uint RoomSeed;
            public int MazeWidth;
            public int MazeHeight;
            public int EntryGridX;
            public int EntryGridY;
            public int ExitGridX;
            public int ExitGridY;
            public int PlayerSpawnFixedX;
            public int PlayerSpawnFixedY;
            public int PlayerSpawnFixedZ;
            public int PlayerHeadingFixed;
            public int ExitPlayerSpawnFixedX;
            public int ExitPlayerSpawnFixedY;
            public int ExitPlayerSpawnFixedZ;
            public int ExitPlayerHeadingFixed;
            public int EntryPortalSpawnFixedX;
            public int EntryPortalSpawnFixedY;
            public int EntryPortalSpawnFixedZ;
            public int EntryPortalHeadingFixed;
            public int ExitPortalSpawnFixedX;
            public int ExitPortalSpawnFixedY;
            public int ExitPortalSpawnFixedZ;
            public int ExitPortalHeadingFixed;
            public int EntrySourceIndex = -1;
            public int ExitSourceIndex = -1;
            public string EntryTileType;
            public string ExitTileType;
            public string EntryLinkToZone;
            public string EntryLinkToSpawn;
            public string EntrySpawnName;
            public string EntryPortalGcType;
            public string ExitLinkToZone;
            public string ExitLinkToSpawn;
            public string ExitSpawnName;
            public string ExitPortalGcType;
            public string PlayerAnchorSource;
            public string ExitPlayerAnchorSource;
            public string EntryPortalAnchorSource;
            public string ExitPortalAnchorSource;
            public int PlayerAnchorLocalFixedX;
            public int PlayerAnchorLocalFixedY;
            public int PlayerAnchorLocalFixedZ;
            public int ExitPlayerAnchorLocalFixedX;
            public int ExitPlayerAnchorLocalFixedY;
            public int ExitPlayerAnchorLocalFixedZ;
            public int EntryPortalAnchorLocalFixedX;
            public int EntryPortalAnchorLocalFixedY;
            public int EntryPortalAnchorLocalFixedZ;
            public int ExitPortalAnchorLocalFixedX;
            public int ExitPortalAnchorLocalFixedY;
            public int ExitPortalAnchorLocalFixedZ;
            public bool PlayerAnchorWalkable;
            public bool ExitPlayerAnchorWalkable;
            public bool EntryPortalAnchorWalkable;
            public bool ExitPortalAnchorWalkable;
            public bool AnchorsResolved;
            public List<MazeGenerator.MazeCell> Cells = new();
            public List<MazeGenerator.MazeCell> WorldCells = new();
            public PathMap PathMap;
            public List<MazeGenerator.PlacedRoomNode> RoomNodes = new();
            public List<DungeonSpawnData> Spawns = new();
            public List<EncounterObjectMirror> EncounterObjects = new();
        }

        public sealed class EncounterObjectMirror
        {
            public string ZoneName;
            public string GroupKey;
            public string Role;
            public string AuthoredPath;
            public int EntryId;
            public int ChoiceIndex;
            public int MarkerIndex;
            public int PackSlots;
            public int UnitRows;
            public int GridX = -1;
            public int GridY = -1;
            public string TileType;
            public int WorldOriginFixedX;
            public int WorldOriginFixedY;
            public string Source;
            public string ManifestSource;
            public int ChoiceChance;
        }


        private readonly struct EncounterMarker
        {
            public readonly int LocalFixedX;
            public readonly int LocalFixedY;
            public readonly int LocalFixedZ;
            public readonly int HeadingFixed;
            public readonly int SizeFixedX;
            public readonly int SizeFixedY;
            public readonly string Source;

            public EncounterMarker(int localFixedX, int localFixedY, int localFixedZ, int headingFixed = 0, int sizeFixedX = 0, int sizeFixedY = 0, string source = "authored")
            {
                LocalFixedX = localFixedX;
                LocalFixedY = localFixedY;
                LocalFixedZ = localFixedZ;
                HeadingFixed = headingFixed;
                SizeFixedX = sizeFixedX;
                SizeFixedY = sizeFixedY;
                Source = source;
            }
        }

        private readonly struct AuthoredAnchor
        {
            public readonly int LocalFixedX;
            public readonly int LocalFixedY;
            public readonly int LocalFixedZ;
            public readonly int HeadingFixed;
            public readonly string Source;

            public AuthoredAnchor(int localFixedX, int localFixedY, int localFixedZ, int headingFixed, string source)
            {
                LocalFixedX = localFixedX;
                LocalFixedY = localFixedY;
                LocalFixedZ = localFixedZ;
                HeadingFixed = headingFixed;
                Source = source;
            }
        }

        private static EncounterMarker WholeEncounterMarker(int x, int y, int z, int heading = 0, int sizeX = 0, int sizeY = 0, string source = "authored")
        {
            return new EncounterMarker(
                x * UnitMover.Fixed,
                y * UnitMover.Fixed,
                z * UnitMover.Fixed,
                heading * UnitMover.Fixed,
                sizeX * UnitMover.Fixed,
                sizeY * UnitMover.Fixed,
                source);
        }

        private static AuthoredAnchor WholeAuthoredAnchor(int x, int y, int z, int heading, string source)
        {
            return new AuthoredAnchor(
                x * UnitMover.Fixed,
                y * UnitMover.Fixed,
                z * UnitMover.Fixed,
                heading * UnitMover.Fixed,
                source);
        }

        private struct SpawnUnit
        {
            public string GcType;
            public string SpawnGcTypeOverride;
            public int Count;
            public int DifficultyF32;
            public int LevelOffset;
            public string AuthoredType => SpawnGcTypeOverride ?? GcType;
            public SpawnUnit(string gcType, int count, int difficultyF32 = 0x100, string spawnGcTypeOverride = null, int levelOffset = 0)
            {
                GcType = gcType;
                Count = count;
                DifficultyF32 = difficultyF32;
                SpawnGcTypeOverride = spawnGcTypeOverride;
                LevelOffset = levelOffset;
            }
        }

        private sealed class EncounterTableManifest
        {
            public readonly string AuthoredPath;
            public readonly int EntryId;
            private SpawnUnit[][] _pkgChoices;
            private int[] _pkgChoiceChances;
            private bool _pkgResolved;
            private bool _pkgResolutionLogged;
            private string _pkgSource = "";
            private string _pkgDetail = "";

            public EncounterTableManifest(string authoredPath, int entryId)
            {
                AuthoredPath = authoredPath;
                EntryId = entryId;
            }

            public SpawnUnit[][] Choices
            {
                get
                {
                    EnsurePkgResolved();
                    return _pkgChoices;
                }
            }

            public int Length => Choices.Length;
            public SpawnUnit[] this[int index] => Choices[index];

            public string Source
            {
                get
                {
                    EnsurePkgResolved();
                    return _pkgSource;
                }
            }

            public string Detail
            {
                get
                {
                    EnsurePkgResolved();
                    return _pkgDetail;
                }
            }

            public int ChoiceChance(int index)
            {
                EnsurePkgResolved();
                return index >= 0 && index < _pkgChoiceChances.Length ? _pkgChoiceChances[index] : 1;
            }

            public void LogResolution()
            {
                EnsurePkgResolved();
            }

            private void EnsurePkgResolved()
            {
                if (_pkgResolved)
                    return;
                if (GCDatabase.Instance == null || !GCDatabase.Instance.IsLoaded)
                    throw new InvalidDataException($"Encounter table authored data is not loaded path='{AuthoredPath}' entry={EntryId}");

                _pkgResolved = true;
                if (TryResolveEncounterTableFromGc(this, out var choices, out var chances, out var source, out var detail))
                {
                    _pkgChoices = choices;
                    _pkgChoiceChances = chances;
                    _pkgSource = source;
                    _pkgDetail = detail;
                }
                else
                {
                    _pkgDetail = detail;
                    throw new InvalidDataException($"Encounter table authored data is incomplete path='{AuthoredPath}' entry={EntryId} detail='{detail}'");
                }

                if (!_pkgResolutionLogged)
                {
                    _pkgResolutionLogged = true;
                    Debug.LogError($"[ENCOUNTER-MANIFEST] authored='{AuthoredPath}' entry={EntryId} source='{_pkgSource}' detail='{_pkgDetail}' choices={Length} packCount=EncounterUnit.Count chanceWeighting=generator-table difficulty=EncounterUnit sourceFunction=RoomNode::prep+EncounterObject::update");
                }
            }
        }

        private static bool TryResolveEncounterTableFromGc(
            EncounterTableManifest manifest,
            out SpawnUnit[][] choices,
            out int[] choiceChances,
            out string source,
            out string detail)
        {
            choices = null;
            choiceChances = null;
            source = "";
            detail = "";

            if (manifest == null)
            {
                detail = "manifest-null";
                return false;
            }

            var gc = GCDatabase.Instance;
            if (gc == null || !gc.IsLoaded)
            {
                detail = "GCDatabase-not-loaded";
                return false;
            }

            GCNode table = gc.ResolveWithInheritance(manifest.AuthoredPath);
            if (table == null)
            {
                detail = "table-not-found";
                return false;
            }

            if (table.AnonymousChildren == null || table.AnonymousChildren.Count == 0)
            {
                detail = "no-encounter-choices";
                return false;
            }

            var parsedChoices = new List<SpawnUnit[]>();
            var parsedChances = new List<int>();
            var unresolved = new List<string>();
            for (int choiceIndex = 0; choiceIndex < table.AnonymousChildren.Count; choiceIndex++)
            {
                GCNode encounter = table.AnonymousChildren[choiceIndex];
                var units = new List<SpawnUnit>();
                if (encounter.AnonymousChildren != null)
                {
                    for (int unitIndex = 0; unitIndex < encounter.AnonymousChildren.Count; unitIndex++)
                    {
                        GCNode unitNode = encounter.AnonymousChildren[unitIndex];
                        if (TryBuildSpawnUnitFromEncounterUnit(unitNode, out SpawnUnit unit, out string unitDetail))
                        {
                            units.Add(unit);
                        }
                        else
                        {
                            string type = unitNode?.GetString("Type", "") ?? "";
                            unresolved.Add($"choice={choiceIndex}:unit={unitIndex}:type='{type}':{unitDetail}");
                        }
                    }
                }

                if (units.Count == 0)
                {
                    if (unresolved.Count == 0)
                        unresolved.Add($"choice={choiceIndex}:no-units");
                    continue;
                }

                parsedChoices.Add(units.ToArray());
                parsedChances.Add(Math.Max(1, encounter.GetInt("Chance", 1)));
            }

            if (parsedChoices.Count == 0 || unresolved.Count > 0)
            {
                detail = unresolved.Count > 0 ? string.Join(";", unresolved) : "no-parsed-choices";
                return false;
            }

            choices = parsedChoices.ToArray();
            choiceChances = parsedChances.ToArray();
            source = "GCDatabase";
            detail = $"choices={choices.Length} sourceFile='{table.SourceFile ?? ""}'";
            return true;
        }

        private static bool TryBuildSpawnUnitFromEncounterUnit(GCNode unitNode, out SpawnUnit unit, out string detail)
        {
            unit = default;
            detail = "";
            if (unitNode == null)
            {
                detail = "unit-null";
                return false;
            }

            string typePath = unitNode.GetString("Type", "");
            if (string.IsNullOrWhiteSpace(typePath))
            {
                detail = "missing-Type";
                return false;
            }

            int difficultyF32 = unitNode.GetFixed32("Difficulty", 0x100);
            int levelOffset = unitNode.GetInt("LevelOffset", 0);
            if (!TryResolveSpawnBaseGcType(typePath.Trim(), out string gcType, out string spawnOverride, out detail))
                return false;

            unit = new SpawnUnit(gcType, 1, difficultyF32, spawnOverride, levelOffset);
            return true;
        }

        private static bool TryResolveSpawnBaseGcType(string typePath, out string gcType, out string spawnOverride, out string detail)
        {
            gcType = null;
            spawnOverride = null;
            detail = "";
            if (string.IsNullOrWhiteSpace(typePath))
            {
                detail = "empty-type";
                return false;
            }

            if (AuthoredGameplayCatalog.FindCreature(typePath) != null)
            {
                gcType = typePath;
                return true;
            }

            var gc = GCDatabase.Instance;
            GCNode raw = gc?.Resolve(typePath);
            string currentPath = typePath;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int depth = 0; depth < 16 && raw != null && !string.IsNullOrWhiteSpace(currentPath); depth++)
            {
                if (!visited.Add(currentPath))
                    break;

                if (AuthoredGameplayCatalog.FindCreature(currentPath) != null)
                {
                    gcType = currentPath;
                    spawnOverride = string.Equals(currentPath, typePath, StringComparison.OrdinalIgnoreCase) ? null : typePath;
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(raw.Extends) && AuthoredGameplayCatalog.FindCreature(raw.Extends) != null)
                {
                    gcType = raw.Extends;
                    spawnOverride = typePath;
                    return true;
                }

                currentPath = raw.Extends;
                raw = string.IsNullOrWhiteSpace(currentPath) ? null : gc?.Resolve(currentPath);
            }

            if (IsNonCreatureEncounterEntity(typePath))
            {
                gcType = typePath;
                detail = "non-creature encounter entity; spawn path preserved";
                return true;
            }

            detail = "base-creature-not-found";
            return false;
        }

        private static bool IsNonCreatureEncounterEntity(string typePath)
        {
            if (string.IsNullOrWhiteSpace(typePath))
                return false;
            return typePath.StartsWith("terrain.", StringComparison.OrdinalIgnoreCase) ||
                   typePath.StartsWith("misc.", StringComparison.OrdinalIgnoreCase) ||
                   typePath.IndexOf(".interactives.", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static readonly EncounterTableManifest Level01Encounter =
            new EncounterTableManifest("world.dungeon00.enc.level01_encounter", 23853);

        private static readonly EncounterTableManifest Level01LeaderEncounter =
            new EncounterTableManifest("world.dungeon00.enc.level01_leader_encounter", 23854);

        private static readonly EncounterTableManifest Level02Encounter =
            new EncounterTableManifest("world.dungeon00.enc.level02_encounter", 23855);

        private static readonly EncounterTableManifest Level02LeaderEncounter =
            new EncounterTableManifest("world.dungeon00.enc.level02_leader_encounter", 23856);

        private static readonly EncounterTableManifest Level03Encounter =
            new EncounterTableManifest("world.dungeon00.enc.level03_encounter", 23857);

        private static readonly EncounterTableManifest Level03LeaderEncounter =
            new EncounterTableManifest("world.dungeon00.enc.level03_leader_encounter", 23858);

        private static readonly EncounterTableManifest Level04Encounter =
            new EncounterTableManifest("world.dungeon00.enc.level04_encounter", 23860);

        private static readonly EncounterTableManifest Level04LeaderEncounter =
            new EncounterTableManifest("world.dungeon00.enc.level04_leader_encounter", 23861);

        private static readonly EncounterTableManifest Dungeon01Level01Encounter =
            new EncounterTableManifest("world.dungeon01.enc.base.level01_encounter", 23946);

        private static readonly EncounterTableManifest Dungeon01Level01LeaderEncounter =
            new EncounterTableManifest("world.dungeon01.enc.base.level01_leader1_encounter", 23947);

        private static readonly EncounterTableManifest Dungeon01Level01QuestEncounter =
            new EncounterTableManifest("world.dungeon01.enc.level01_quest_encounter", 23973);

        private static readonly EncounterTableManifest Dungeon01SqueakeasyEncounter =
            new EncounterTableManifest("world.dungeon01.enc.dungeon01_squeakeasy_bouncer_encounter", 23968);

        private static readonly EncounterTableManifest[] DewValleyEncounterManifests =
        {
            Level01Encounter,
            Level01LeaderEncounter,
            Level02Encounter,
            Level02LeaderEncounter,
            Level03Encounter,
            Level03LeaderEncounter,
            Level04Encounter,
            Level04LeaderEncounter
        };

        public static void RunStartupManifestCheck()
        {
            int pkgTables = 0;
            int localTables = 0;
            for (int manifestIndex = 0; manifestIndex < DewValleyEncounterManifests.Length; manifestIndex++)
            {
                var table = DewValleyEncounterManifests[manifestIndex];
                table.LogResolution();
                if (table.Source.Equals("GCDatabase", StringComparison.OrdinalIgnoreCase))
                    pkgTables++;
                else
                    localTables++;
            }

            Debug.LogError($"[ENCOUNTER-MANIFEST-CHECK] dewValleyTables={DewValleyEncounterManifests.Length} pkgTables={pkgTables} localTables={localTables} packCount=EncounterUnit.Count chanceWeighting=generator-table difficulty=EncounterUnit sourceFunction=RoomNode::prep+EncounterObject::update");
        }



        private class RoomNodeDef
        {
            public string TileSet;
            public int? GridX;
            public int? GridY;
            public int Chance = 100;
            public EncounterTableManifest EncounterTable;
            public string LinkToSpawn;
            public string LinkToZone;
            public string SpawnName;
            public string PortalGcType;
        }

        private class LevelDef
        {
            public int MazeWidth;
            public int MazeHeight;
            public int MazeRandomness;
            public int MazeSparseness;
            public int MazeDeadEndRemovalChance;
            public int TileSize = MazeGenerator.TILE_SIZE;
            public string TileSetPrefix = "elmforest_tileset_";
            public RoomNodeDef[] RoomNodes;
            public EncounterTableManifest EncounterTable;
            public EncounterTableManifest LeaderEncounterTable;
            public int[] LeaderGridYs;
            public int EntryGridX;
            public int EntryGridY;
            public int ExitGridX;
            public int ExitGridY;
        }

        private static readonly Dictionary<string, LevelDef> LevelDefs =
            new Dictionary<string, LevelDef>(StringComparer.OrdinalIgnoreCase)
        {
            {
                "dungeon00_level01", new LevelDef
                {
                    MazeWidth = 4, MazeHeight = 5,
                    MazeRandomness = 90, MazeSparseness = 5,
                    MazeDeadEndRemovalChance = 100,
                    RoomNodes = new[]
                    {
                        new RoomNodeDef { TileSet = "elmforest_hub_", GridY = 4, LinkToSpawn = "start1", LinkToZone = "tutorial", SpawnName = "start1", PortalGcType = "misc.zoneportal_hub" },
                        new RoomNodeDef { TileSet = "elmforest_down_", GridY = 0, LinkToSpawn = "start2", LinkToZone = "dungeon00_level02", SpawnName = "start2", PortalGcType = "misc.zoneportal_agg" },
                        new RoomNodeDef { TileSet = "elmforest_questfindring_", GridY = 3, EncounterTable = Level01LeaderEncounter },
                        new RoomNodeDef { TileSet = "tutorial_loot_", GridY = 2, EncounterTable = Level01LeaderEncounter },
                    },
                    EncounterTable = Level01Encounter,
                    LeaderEncounterTable = Level01LeaderEncounter,
                    LeaderGridYs = new[] { 3, 2 },
                    EntryGridX = 3, EntryGridY = 4,
                    ExitGridX = 3, ExitGridY = 0,
                }
            },
            {
                "dungeon00_level02", new LevelDef
                {
                    MazeWidth = 4, MazeHeight = 4,
                    MazeRandomness = 90, MazeSparseness = 5,
                    MazeDeadEndRemovalChance = 100,
                    RoomNodes = new[]
                    {
                        new RoomNodeDef { TileSet = "elmforest_up_", GridY = 3, LinkToSpawn = "start2", LinkToZone = "dungeon00_level01", SpawnName = "start2", PortalGcType = "misc.zoneportal_agg" },
                        new RoomNodeDef { TileSet = "elmforest_down_", GridY = 0, LinkToSpawn = "start3", LinkToZone = "dungeon00_level03", SpawnName = "start3", PortalGcType = "misc.zoneportal_agg" },
                        new RoomNodeDef { TileSet = "tutorial_loot_", GridY = 1, EncounterTable = Level02LeaderEncounter },
                    },
                    EncounterTable = Level02Encounter,
                    LeaderEncounterTable = Level02LeaderEncounter,
                    LeaderGridYs = new[] { 1 },
                    EntryGridX = 3, EntryGridY = 3,
                    ExitGridX = 3, ExitGridY = 0,
                }
            },
            {
                "dungeon00_level03", new LevelDef
                {
                    MazeWidth = 4, MazeHeight = 4,
                    MazeRandomness = 90, MazeSparseness = 5,
                    MazeDeadEndRemovalChance = 100,
                    RoomNodes = new[]
                    {
                        new RoomNodeDef { TileSet = "elmforest_up_", GridY = 3, LinkToSpawn = "start3", LinkToZone = "dungeon00_level02", SpawnName = "start3", PortalGcType = "misc.zoneportal_agg" },
                        new RoomNodeDef { TileSet = "elmforest_undergroundentrance_", GridY = 0, LinkToSpawn = "boss_spawn", LinkToZone = "dungeon00_level03_boss", SpawnName = "boss_spawn", PortalGcType = "misc.zoneportal_agg" },
                        new RoomNodeDef { TileSet = "tutorial_loot_", GridY = 1, EncounterTable = Level03LeaderEncounter },
                    },
                    EncounterTable = Level03Encounter,
                    LeaderEncounterTable = Level03LeaderEncounter,
                    LeaderGridYs = new[] { 1 },
                    EntryGridX = 3, EntryGridY = 3,
                    ExitGridX = 3, ExitGridY = 0,
                }
            },
            {
                "dungeon01_level01", new LevelDef
                {
                    MazeWidth = 5, MazeHeight = 5,
                    MazeRandomness = 100, MazeSparseness = 5,
                    MazeDeadEndRemovalChance = 100,
                    TileSize = 360,
                    TileSetPrefix = "cave_small_tileset_",
                    RoomNodes = new[]
                    {
                        new RoomNodeDef { TileSet = "cave_small_hub_", GridX = 2, GridY = 4, LinkToSpawn = "dungeon_spawn", LinkToZone = "town", SpawnName = "dungeon_spawn", PortalGcType = "misc.zoneportal_hub" },
                        new RoomNodeDef { TileSet = "dungeon_one_cave_small_tracks_down_", GridY = 0, LinkToSpawn = "start2", LinkToZone = "dungeon01_level02", SpawnName = "start2", PortalGcType = "misc.zoneportal_agg" },
                        new RoomNodeDef { TileSet = "dungeon_one_one_oneoff_entrance_", GridX = 4, GridY = 3, LinkToSpawn = "off1", LinkToZone = "dungeon01_level01_off1a", SpawnName = "off1", PortalGcType = "misc.zoneportal_agg" },
                        new RoomNodeDef { TileSet = "dungeon_one_one_quest_", GridX = 1, GridY = 3 },
                        new RoomNodeDef { TileSet = "dungeon_one_one_questgate_", GridX = 0, GridY = 2 },
                        new RoomNodeDef { TileSet = "level_one_cave_small_squeakeasy_entrance_", EncounterTable = Dungeon01SqueakeasyEncounter },
                        new RoomNodeDef { TileSet = "cave_small_deadend_", EncounterTable = Dungeon01Level01QuestEncounter },
                        new RoomNodeDef { TileSet = "dungeon_one_fizz_rock_deadend_" },
                        new RoomNodeDef { TileSet = "dungeon_one_fizz_rock_corner_" },
                        new RoomNodeDef { TileSet = "dungeon_one_fizz_rock_corner_" },
                        new RoomNodeDef { TileSet = "cave_small_corner_", EncounterTable = Dungeon01Level01LeaderEncounter },
                        new RoomNodeDef { TileSet = "cave_small_corner_", Chance = 25, EncounterTable = Dungeon01Level01LeaderEncounter },
                        new RoomNodeDef { TileSet = "cave_small_shrine_", Chance = 50, EncounterTable = Dungeon01Level01LeaderEncounter },
                    },
                    EncounterTable = Dungeon01Level01Encounter,
                    LeaderEncounterTable = Dungeon01Level01LeaderEncounter,
                    LeaderGridYs = new[] { 0 },
                    EntryGridX = 2, EntryGridY = 4,
                    ExitGridX = 2, ExitGridY = 0,
                }
            },
        };
        private const int RegularEncounterDifficultyBudgetF32 = 0x180;

        private static int StableSpotSeed(string key)
        {
            if (string.IsNullOrEmpty(key))
                return 0;
            uint h = 2166136261u;
            for (int i = 0; i < key.Length; i++)
            {
                h ^= key[i];
                h *= 16777619u;
            }
            return (int)h;
        }

        private static int ResolveSpotBudgetF32(int spotSeed, int difficultyBudgetF32)
        {
            uint h = (uint)spotSeed * 2654435761u;
            h ^= h >> 15;
            int fractionNumerator = (int)(h & 0xFFFF);
            const int fractionDenominator = 65535;
            long numerator = (long)difficultyBudgetF32 * (fractionDenominator + fractionNumerator);
            long denominator = 2L * fractionDenominator;
            return (int)((numerator + denominator / 2) / denominator);
        }

        private static List<SpawnUnit> ExpandEncounterGroup(SpawnUnit[] group, int spotSeed, int difficultyBudgetF32)
        {
            var result = new List<SpawnUnit>();
            if (group == null || group.Length == 0)
                return result;

            var weighted = new List<SpawnUnit>();
            int spentF32 = 0;
            foreach (var unit in group)
            {
                int authoredCount = Math.Max(1, unit.Count);
                for (int copy = 0; copy < authoredCount; copy++)
                {
                    result.Add(unit);
                    if (unit.DifficultyF32 > 0)
                    {
                        weighted.Add(unit);
                        spentF32 += unit.DifficultyF32;
                    }
                }
            }

            if (weighted.Count == 0)
                return result;

            int spotBudgetF32 = Math.Max(spentF32, ResolveSpotBudgetF32(spotSeed, difficultyBudgetF32));

            int minWeightF32 = int.MaxValue;
            foreach (var unit in weighted)
                if (unit.DifficultyF32 < minWeightF32)
                    minWeightF32 = unit.DifficultyF32;

            int fillIndex = 0;
            while (spotBudgetF32 - spentF32 >= minWeightF32)
            {
                var unit = weighted[fillIndex % weighted.Count];
                if (spentF32 + unit.DifficultyF32 > spotBudgetF32)
                    break;
                result.Add(unit);
                spentF32 += unit.DifficultyF32;
                fillIndex++;
            }

            return result;
        }

        private static List<SpawnUnit> CopyAuthoredEncounterGroup(SpawnUnit[] group)
        {
            var result = new List<SpawnUnit>();
            if (group == null)
                return result;

            foreach (var unit in group)
            {
                int authoredCount = Math.Max(1, unit.Count);
                for (int copy = 0; copy < authoredCount; copy++)
                    result.Add(unit);
            }

            return result;
        }

        private static int NormalizeHeadingFixed(int headingFixed)
        {
            int fullRotationFixed = 360 * UnitMover.Fixed;
            headingFixed %= fullRotationFixed;
            if (headingFixed < 0)
                headingFixed += fullRotationFixed;
            return headingFixed;
        }

        private const int EncounterSpotClearanceFixed = 30 * UnitMover.Fixed;
        private const int EncounterSpotClearanceDiagonalFixed = 0x1537;
        private const int EncounterUnitSpacingFixed = 30 * UnitMover.Fixed;

        private static bool IsOpenSpotFixed(PathMap pathMap, int xFixed, int yFixed)
        {
            if (pathMap == null) return false;
            if (!pathMap.IsWalkableFixed(xFixed, yFixed)) return false;
            if (!pathMap.IsWalkableFixed(xFixed + EncounterSpotClearanceFixed, yFixed)) return false;
            if (!pathMap.IsWalkableFixed(xFixed - EncounterSpotClearanceFixed, yFixed)) return false;
            if (!pathMap.IsWalkableFixed(xFixed, yFixed + EncounterSpotClearanceFixed)) return false;
            if (!pathMap.IsWalkableFixed(xFixed, yFixed - EncounterSpotClearanceFixed)) return false;
            if (!pathMap.IsWalkableFixed(xFixed + EncounterSpotClearanceDiagonalFixed, yFixed + EncounterSpotClearanceDiagonalFixed)) return false;
            if (!pathMap.IsWalkableFixed(xFixed - EncounterSpotClearanceDiagonalFixed, yFixed + EncounterSpotClearanceDiagonalFixed)) return false;
            if (!pathMap.IsWalkableFixed(xFixed + EncounterSpotClearanceDiagonalFixed, yFixed - EncounterSpotClearanceDiagonalFixed)) return false;
            if (!pathMap.IsWalkableFixed(xFixed - EncounterSpotClearanceDiagonalFixed, yFixed - EncounterSpotClearanceDiagonalFixed)) return false;
            return true;
        }

        private static List<PathNode> CollectEncounterSpotNodes(PathMap pathMap, MazeGenerator.MazeCell cell)
        {
            var result = new List<PathNode>();
            if (pathMap == null || cell == null)
                return result;

            int minFixedX = cell.WorldOriginFixedX;
            int minFixedY = cell.WorldOriginFixedY;
            int tileSizeFixed = cell.TileSizeFixed > 0 ? cell.TileSizeFixed : PathMapBuilder.MazeTileSizeFixed;
            int maxFixedX = checked(minFixedX + tileSizeFixed);
            int maxFixedY = checked(minFixedY + tileSizeFixed);
            var minGrid = pathMap.WorldToGridFixed(minFixedX, minFixedY);
            var maxGrid = pathMap.WorldToGridFixed(maxFixedX - 1, maxFixedY - 1);
            for (int gridY = minGrid.gridY; gridY <= maxGrid.gridY; gridY++)
            {
                for (int gridX = minGrid.gridX; gridX <= maxGrid.gridX; gridX++)
                {
                    PathNode node = pathMap.GetNodeAt(gridX, gridY);
                    if (node == null || !node.IsWalkable)
                        continue;
                    if (node.WorldFixedX < minFixedX || node.WorldFixedX >= maxFixedX
                        || node.WorldFixedY < minFixedY || node.WorldFixedY >= maxFixedY)
                        continue;
                    if (!IsOpenSpotFixed(pathMap, node.WorldFixedX, node.WorldFixedY))
                        continue;
                    result.Add(node);
                }
            }
            return result;
        }

        private static List<PathNode> SelectEncounterSpotNodes(PathMap pathMap, MazeGenerator.MazeCell cell, string groupKey, int count)
        {
            var candidates = CollectEncounterSpotNodes(pathMap, cell);
            var selected = new List<PathNode>();
            if (candidates.Count == 0 || count <= 0)
                return selected;

            int anchorIndex = (int)((uint)StableSpotSeed(groupKey) % (uint)candidates.Count);
            PathNode anchor = candidates[anchorIndex];
            candidates.Sort((left, right) =>
            {
                long leftX = (long)left.WorldFixedX - anchor.WorldFixedX;
                long leftY = (long)left.WorldFixedY - anchor.WorldFixedY;
                long rightX = (long)right.WorldFixedX - anchor.WorldFixedX;
                long rightY = (long)right.WorldFixedY - anchor.WorldFixedY;
                int distance = (leftX * leftX + leftY * leftY).CompareTo(rightX * rightX + rightY * rightY);
                if (distance != 0)
                    return distance;
                int gridY = left.GridY.CompareTo(right.GridY);
                return gridY != 0 ? gridY : left.GridX.CompareTo(right.GridX);
            });

            long spacingSquared = (long)EncounterUnitSpacingFixed * EncounterUnitSpacingFixed;
            for (int candidateIndex = 0; candidateIndex < candidates.Count && selected.Count < count; candidateIndex++)
            {
                PathNode candidate = candidates[candidateIndex];
                bool separated = true;
                for (int selectedIndex = 0; selectedIndex < selected.Count; selectedIndex++)
                {
                    long dx = (long)candidate.WorldFixedX - selected[selectedIndex].WorldFixedX;
                    long dy = (long)candidate.WorldFixedY - selected[selectedIndex].WorldFixedY;
                    if (dx * dx + dy * dy < spacingSquared)
                    {
                        separated = false;
                        break;
                    }
                }
                if (separated)
                    selected.Add(candidate);
            }

            return selected;
        }

        private static List<PathNode> SelectStaticEncounterSpotNodes(
            PathMap pathMap,
            EncounterMarker marker,
            int count,
            List<(int XFixed, int YFixed)> reservedSpots)
        {
            var selected = new List<PathNode>();
            if (pathMap == null || count <= 0)
                return selected;
            PathNode anchor = pathMap.GetClosestNodeFixed(marker.LocalFixedX, marker.LocalFixedY, marker.LocalFixedZ);
            if (anchor == null)
                return selected;

            var candidates = new List<PathNode>();
            var minGrid = pathMap.WorldToGridFixed(pathMap.MinWorldFixedX, pathMap.MinWorldFixedY);
            var maxGrid = pathMap.WorldToGridFixed(pathMap.MaxWorldFixedX, pathMap.MaxWorldFixedY);
            for (int gridY = minGrid.gridY; gridY <= maxGrid.gridY; gridY++)
            {
                for (int gridX = minGrid.gridX; gridX <= maxGrid.gridX; gridX++)
                {
                    PathNode node = pathMap.GetNodeAt(gridX, gridY);
                    if (node == null || !node.IsWalkable)
                        continue;
                    if (!IsOpenSpotFixed(pathMap, node.WorldFixedX, node.WorldFixedY))
                        continue;
                    if (!pathMap.CanPathToFixed(anchor.WorldFixedX, anchor.WorldFixedY, node.WorldFixedX, node.WorldFixedY))
                        continue;
                    candidates.Add(node);
                }
            }
            candidates.Sort((left, right) =>
            {
                long leftX = (long)left.WorldFixedX - marker.LocalFixedX;
                long leftY = (long)left.WorldFixedY - marker.LocalFixedY;
                long rightX = (long)right.WorldFixedX - marker.LocalFixedX;
                long rightY = (long)right.WorldFixedY - marker.LocalFixedY;
                int distance = (leftX * leftX + leftY * leftY).CompareTo(rightX * rightX + rightY * rightY);
                if (distance != 0)
                    return distance;
                int gridY = left.GridY.CompareTo(right.GridY);
                return gridY != 0 ? gridY : left.GridX.CompareTo(right.GridX);
            });

            long spacingSquared = (long)EncounterUnitSpacingFixed * EncounterUnitSpacingFixed;
            for (int candidateIndex = 0; candidateIndex < candidates.Count && selected.Count < count; candidateIndex++)
            {
                PathNode candidate = candidates[candidateIndex];
                bool separated = true;
                for (int reservedIndex = 0; reservedIndex < reservedSpots.Count; reservedIndex++)
                {
                    long dx = (long)candidate.WorldFixedX - reservedSpots[reservedIndex].XFixed;
                    long dy = (long)candidate.WorldFixedY - reservedSpots[reservedIndex].YFixed;
                    if (dx * dx + dy * dy < spacingSquared)
                    {
                        separated = false;
                        break;
                    }
                }
                for (int selectedIndex = 0; separated && selectedIndex < selected.Count; selectedIndex++)
                {
                    long dx = (long)candidate.WorldFixedX - selected[selectedIndex].WorldFixedX;
                    long dy = (long)candidate.WorldFixedY - selected[selectedIndex].WorldFixedY;
                    if (dx * dx + dy * dy < spacingSquared)
                        separated = false;
                }
                if (separated)
                    selected.Add(candidate);
            }
            return selected;
        }

        private static void TrackEncounterPlacement(string zoneName, string groupKey, string role,
            MazeGenerator.MazeCell cell, int choiceIndex, int unitCount, int candidateCount, string state, string source)
        {
            if (!ServerDiagnostics.IsEnabled("encounterTracking"))
                return;
            Debug.LogError($"[ENCOUNTER-TRACK] zone='{zoneName ?? ""}' group='{groupKey ?? ""}' role='{role ?? ""}' cell=({cell?.GridX ?? -1},{cell?.GridY ?? -1}) tile='{cell?.TileType ?? ""}' choice={choiceIndex} units={unitCount} candidates={candidateCount} state={state ?? ""} source='{source ?? ""}'");
        }

        private static readonly EncounterMarker[] Level03BossRegularMarkers =
        {
            WholeEncounterMarker(110, -990, 40, source: "PKG dungeon00_level03_boss base.Encounter"),
            WholeEncounterMarker(-170, -960, 40, source: "PKG dungeon00_level03_boss base.Encounter"),
            WholeEncounterMarker(-500, -530, 40, source: "PKG dungeon00_level03_boss base.Encounter"),
            new EncounterMarker(160 * UnitMover.Fixed, -320 * UnitMover.Fixed, 10239, source: "PKG dungeon00_level03_boss base.Encounter"),
            WholeEncounterMarker(60, -110, 40, source: "PKG dungeon00_level03_boss base.Encounter"),
            new EncounterMarker(-20 * UnitMover.Fixed, -230 * UnitMover.Fixed, 10239, source: "PKG dungeon00_level03_boss base.Encounter"),
            WholeEncounterMarker(-250, -190, 40, source: "PKG dungeon00_level03_boss base.Encounter"),
            WholeEncounterMarker(-150, -450, 40, source: "PKG dungeon00_level03_boss base.Encounter"),
            new EncounterMarker(-260 * UnitMover.Fixed, -580 * UnitMover.Fixed, 10239, source: "PKG dungeon00_level03_boss base.Encounter"),
            new EncounterMarker(-150 * UnitMover.Fixed, -790 * UnitMover.Fixed, 10239, source: "PKG dungeon00_level03_boss base.Encounter"),
            WholeEncounterMarker(20, -850, 40, source: "PKG dungeon00_level03_boss base.Encounter"),
        };

        private static readonly EncounterMarker Level03BossLootGuardMarker =
            WholeEncounterMarker(-440, -660, 40, source: "PKG dungeon00_level03_boss LootGuardEncounter");

        private static readonly (SpawnUnit Unit, int XFixed, int YFixed, int ZFixed, int HeadingFixed)[] Level03BossPosse =
        {
            (new SpawnUnit("world.dungeon00.mob.boss", 1, 0x100, "world.dungeon00.mob.boss_posse.RattleTooth"), 405 * UnitMover.Fixed, -1195 * UnitMover.Fixed, 40 * UnitMover.Fixed, 0),
            (new SpawnUnit("creatures.whiskers.blademaster.Basic.Grunt", 1, 0x100, "world.dungeon00.mob.boss_posse.Blademaster"), 430 * UnitMover.Fixed, -1205 * UnitMover.Fixed, 40 * UnitMover.Fixed, 0),
            (new SpawnUnit("creatures.whiskers.blademaster.Basic.Grunt", 1, 0x100, "world.dungeon00.mob.boss_posse.Blademaster"), 380 * UnitMover.Fixed, -1205 * UnitMover.Fixed, 40 * UnitMover.Fixed, 0),
            (new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 0x100, "world.dungeon00.mob.boss_posse.Broodling"), 450 * UnitMover.Fixed, -1260 * UnitMover.Fixed, 40 * UnitMover.Fixed, 0),
            (new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 0x100, "world.dungeon00.mob.boss_posse.Broodling"), 360 * UnitMover.Fixed, -1260 * UnitMover.Fixed, 40 * UnitMover.Fixed, 0),
            (new SpawnUnit("creatures.forestCreatures.Warg.Basic.Grunt", 1, 0x100, "world.dungeon00.mob.boss_posse.boss_guard"), 420 * UnitMover.Fixed, -1230 * UnitMover.Fixed, 40 * UnitMover.Fixed, 0),
            (new SpawnUnit("creatures.forestCreatures.Warg.Basic.Grunt", 1, 0x100, "world.dungeon00.mob.boss_posse.boss_guard"), 390 * UnitMover.Fixed, -1230 * UnitMover.Fixed, 40 * UnitMover.Fixed, 0),
        };

        private static string NormalizeBaseZone(string zoneName)
        {
            if (string.IsNullOrEmpty(zoneName))
                return zoneName;
            int instIdx = zoneName.IndexOf("_inst", StringComparison.OrdinalIgnoreCase);
            return instIdx > 0 ? zoneName.Substring(0, instIdx) : zoneName;
        }

        private static int NextTableIndex(CombatRandom rng, EncounterTableManifest table, string phase, string owner)
        {
            int count = table?.Length ?? 0;
            if (count <= 0)
                return -1;

            string drawPhase = phase ?? "DungeonMazeSpawner::EncounterTableChoice";
            string drawOwner = owner ?? table.AuthoredPath;
            var groups = new SortedDictionary<int, List<int>>(Comparer<int>.Create((left, right) => right.CompareTo(left)));
            for (int choiceIndex = 0; choiceIndex < count; choiceIndex++)
            {
                int chance = Math.Max(1, table.ChoiceChance(choiceIndex));
                if (!groups.TryGetValue(chance, out var choices))
                {
                    choices = new List<int>();
                    groups[chance] = choices;
                }

                choices.Add(choiceIndex);
            }

            foreach (var group in groups)
            {
                int chance = group.Key;
                uint gate = RngLedger.Generate(
                    rng,
                    "layout",
                    $"{drawPhase}:chance",
                    0,
                    (uint)(chance - 1),
                    $"{drawOwner}:chance={chance}");
                if (gate != 0)
                    continue;

                var choices = group.Value;
                uint selected = RngLedger.Generate(
                    rng,
                    "layout",
                    $"{drawPhase}:select",
                    0,
                    (uint)(choices.Count - 1),
                    $"{drawOwner}:chance={chance}:candidates={choices.Count}");
                return choices[(int)selected];
            }

            return -1;
        }

        private static void AddSpawn(List<DungeonSpawnData> spawns, string zoneName, SpawnUnit unit,
            int posFixedX, int posFixedY, int posFixedZ, int headingFixed, string groupKey)
        {
            spawns.Add(new DungeonSpawnData
            {
                zoneName = zoneName,
                gcType = unit.GcType,
                spawnGcTypeOverride = unit.SpawnGcTypeOverride,
                PosFixedX = posFixedX,
                PosFixedY = posFixedY,
                PosFixedZ = posFixedZ,
                HeadingFixed = NormalizeHeadingFixed(headingFixed),
                encounterGroupKey = groupKey,
                encounterDifficultyF32 = unit.DifficultyF32,
                encounterLevelOffset = unit.LevelOffset
            });
        }

        private static void RecordEncounterObjectMirror(ProceduralDungeonSnapshot snapshot, string zoneName, string groupKey,
            EncounterTableManifest table, int choiceIndex, string role, MazeGenerator.MazeCell cell, int markerIndex,
            SpawnUnit[] group, string source, bool materialized)
        {
            bool leader = role != null && role.Contains("leader", StringComparison.OrdinalIgnoreCase);
            int packSlots = leader
                ? CopyAuthoredEncounterGroup(group).Count
                : ExpandEncounterGroup(group, StableSpotSeed(groupKey), RegularEncounterDifficultyBudgetF32).Count;
            int unitRows = group?.Length ?? 0;
            int choiceChance = table?.ChoiceChance(choiceIndex) ?? 1;
            string manifestSource = table?.Source ?? "";
            if (snapshot != null)
            {
                snapshot.EncounterObjects.Add(new EncounterObjectMirror
                {
                    ZoneName = zoneName,
                    GroupKey = groupKey,
                    Role = role,
                    AuthoredPath = table?.AuthoredPath,
                    EntryId = table?.EntryId ?? 0,
                    ChoiceIndex = choiceIndex,
                    MarkerIndex = markerIndex,
                    PackSlots = packSlots,
                    UnitRows = unitRows,
                    GridX = cell?.GridX ?? -1,
                    GridY = cell?.GridY ?? -1,
                    TileType = cell?.TileType,
                    WorldOriginFixedX = cell?.WorldOriginFixedX ?? 0,
                    WorldOriginFixedY = cell?.WorldOriginFixedY ?? 0,
                    Source = source,
                    ManifestSource = manifestSource,
                    ChoiceChance = choiceChance
                });
            }

            Debug.LogError($"[ENCOUNTER-OBJECT] zone={zoneName} group={groupKey} role={role} authored='{table?.AuthoredPath ?? ""}' entry={table?.EntryId ?? 0} choice={choiceIndex} chance={choiceChance} manifestSource='{manifestSource}' packSlots={packSlots} unitRows={unitRows} cell=({cell?.GridX ?? -1},{cell?.GridY ?? -1}) tile='{cell?.TileType ?? ""}' marker={markerIndex} source='{source ?? ""}' mirrorOnly={!materialized} packCount=EncounterUnit.Count difficulty=EncounterUnit sourceFunction=RoomNode::prep+EncounterObject::writeInit");
        }

        private static int AddProceduralEncounterSpawns(ProceduralDungeonSnapshot snapshot, List<DungeonSpawnData> spawns,
            string zoneName, EncounterTableManifest table, MazeGenerator.MazeCell cell, string groupKey,
            CombatRandom rng, string role, string source)
        {
            int choiceIndex = NextTableIndex(rng, table, "DungeonMazeSpawner::procedural-encounter-choice", $"{groupKey}:{table?.AuthoredPath}");
            if (choiceIndex < 0)
            {
                TrackEncounterPlacement(zoneName, groupKey, role, cell, choiceIndex, 0, 0, "choice-blocked", source);
                return 0;
            }

            SpawnUnit[] group = table[choiceIndex];
            bool leader = role != null && role.Contains("leader", StringComparison.OrdinalIgnoreCase);
            List<SpawnUnit> expandedGroup = leader
                ? CopyAuthoredEncounterGroup(group)
                : ExpandEncounterGroup(group, StableSpotSeed(groupKey), RegularEncounterDifficultyBudgetF32);
            List<PathNode> spots = SelectEncounterSpotNodes(snapshot?.PathMap, cell, groupKey, expandedGroup.Count);
            string placementSource = $"{source}:server-reconstruction:EncounterObject-placeholder:pathmap-native-grid";
            bool materialized = expandedGroup.Count > 0 && spots.Count == expandedGroup.Count;
            RecordEncounterObjectMirror(snapshot, zoneName, groupKey, table, choiceIndex, role, cell, -1, group, placementSource, materialized);
            if (!materialized)
            {
                Debug.LogError($"[MAZE-SPAWNER] encounter blocked zone={zoneName} group={groupKey} cell=({cell?.GridX ?? -1},{cell?.GridY ?? -1}) tile='{cell?.TileType ?? ""}' reason=insufficient-native-pathmap-capacity units={expandedGroup.Count} spots={spots.Count} state=blocked");
                TrackEncounterPlacement(zoneName, groupKey, role, cell, choiceIndex, expandedGroup.Count, spots.Count, "pathmap-capacity-blocked", placementSource);
                return 0;
            }

            for (int unitIndex = 0; unitIndex < expandedGroup.Count; unitIndex++)
            {
                PathNode spot = spots[unitIndex];
                int headingFixed = (int)((uint)StableSpotSeed($"{groupKey}:{unitIndex}") % (uint)(360 * UnitMover.Fixed));
                AddSpawn(spawns, zoneName, expandedGroup[unitIndex], spot.WorldFixedX, spot.WorldFixedY, spot.HeightFixed, headingFixed, groupKey);
            }
            TrackEncounterPlacement(zoneName, groupKey, role, cell, choiceIndex, expandedGroup.Count, spots.Count, "materialized", placementSource);
            return expandedGroup.Count;
        }

        private static int AddStaticEncounterSpawns(List<DungeonSpawnData> spawns, string zoneName,
            EncounterTableManifest table, EncounterMarker marker, string groupKey, CombatRandom rng, string role,
            PathMap pathMap, List<(int XFixed, int YFixed)> reservedSpots)
        {
            int choiceIndex = NextTableIndex(rng, table, "DungeonMazeSpawner::static-encounter-choice", $"{groupKey}:{table?.AuthoredPath}");
            if (choiceIndex < 0)
                return 0;

            var group = table[choiceIndex];
            bool leader = role != null && role.Contains("leader", StringComparison.OrdinalIgnoreCase);
            var expandedGroup = leader
                ? CopyAuthoredEncounterGroup(group)
                : ExpandEncounterGroup(group, StableSpotSeed(groupKey), RegularEncounterDifficultyBudgetF32);
            List<PathNode> spots = SelectStaticEncounterSpotNodes(pathMap, marker, expandedGroup.Count, reservedSpots);
            string placementSource = $"{marker.Source}:server-reconstruction:EncounterObject-placeholder:pathmap-native-grid";
            bool materialized = expandedGroup.Count > 0 && spots.Count == expandedGroup.Count;
            RecordEncounterObjectMirror(null, zoneName, groupKey, table, choiceIndex, role, null, -1, group, placementSource, materialized);
            if (!materialized)
            {
                TrackEncounterPlacement(zoneName, groupKey, role, null, choiceIndex, expandedGroup.Count, spots.Count, "pathmap-capacity-blocked", placementSource);
                return 0;
            }
            for (int unitIndex = 0; unitIndex < expandedGroup.Count; unitIndex++)
            {
                PathNode spot = spots[unitIndex];
                AddSpawn(spawns, zoneName, expandedGroup[unitIndex], spot.WorldFixedX, spot.WorldFixedY, spot.HeightFixed, marker.HeadingFixed, groupKey);
                reservedSpots.Add((spot.WorldFixedX, spot.WorldFixedY));
            }
            TrackEncounterPlacement(zoneName, groupKey, role, null, choiceIndex, expandedGroup.Count, spots.Count, "materialized", placementSource);
            return expandedGroup.Count;
        }



        public static bool IsStaticBossZone(string zoneName)
        {
            return string.Equals(NormalizeBaseZone(zoneName), "dungeon00_level03_boss", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsProceduralZone(string zoneName)
        {
            return LevelDefs.ContainsKey(NormalizeBaseZone(zoneName));
        }

        public static List<DungeonSpawnData> GenerateStaticBossSpawns(string zoneName, uint seed)
        {
            string baseZone = NormalizeBaseZone(zoneName);
            var spawns = new List<DungeonSpawnData>();
            if (!IsStaticBossZone(baseZone))
                return spawns;

            var rng = new CombatRandom(seed);
            RngLedger.LogSeed("layout", "DungeonMazeSpawner::static-boss-seed", seed, baseZone);
            PathMap pathMap = PathMapCatalog.Instance.GetPathMap(baseZone);
            var reservedSpots = new List<(int XFixed, int YFixed)>();
            for (int posseIndex = 0; posseIndex < Level03BossPosse.Length; posseIndex++)
                reservedSpots.Add((Level03BossPosse[posseIndex].XFixed, Level03BossPosse[posseIndex].YFixed));
            int regular = 0;
            for (int markerIndex = 0; markerIndex < Level03BossRegularMarkers.Length; markerIndex++)
                regular += AddStaticEncounterSpawns(spawns, baseZone, Level04Encounter, Level03BossRegularMarkers[markerIndex], $"{baseZone}:enc:{markerIndex}", rng, "static-regular", pathMap, reservedSpots);

            int leaders = AddStaticEncounterSpawns(spawns, baseZone, Level04LeaderEncounter, Level03BossLootGuardMarker, $"{baseZone}:leader:0", rng, "static-loot-guard", pathMap, reservedSpots);
            foreach (var posse in Level03BossPosse)
                AddSpawn(spawns, baseZone, posse.Unit, posse.XFixed, posse.YFixed, posse.ZFixed, posse.HeadingFixed, $"{baseZone}:boss:0");
            Debug.LogError($"[ENCOUNTER-OBJECT] zone={baseZone} group={baseZone}:boss:0 role=static-boss-posse authored='world.dungeon00.mob.boss_posse_table' entry=23867 choice=0 packSlots={Level03BossPosse.Length} unitRows={Level03BossPosse.Length} cell=(-1,-1) tile='' marker=-1 source='world.dungeon00.data.BossFightNCI01' mirrorOnly=True packCount=static difficulty=static sourceFunction=EncounterObject::update");

            Debug.LogError($"[MAZE-SPAWNER] staticBoss zone={baseZone} total={spawns.Count} regular={regular} leaders={leaders} bossPosse={Level03BossPosse.Length}");
            return spawns;
        }

        public static bool TryGetMazeDimensions(string zoneName,
            out int width, out int height, out int entryX, out int entryY,
            out int randomness, out int sparseness, out int deadEndRemoval)
        {
            if (LevelDefs.TryGetValue(NormalizeBaseZone(zoneName), out var levelDef))
            {
                width = levelDef.MazeWidth;
                height = levelDef.MazeHeight;
                entryX = levelDef.EntryGridX;
                entryY = levelDef.EntryGridY;
                randomness = levelDef.MazeRandomness;
                sparseness = levelDef.MazeSparseness;
                deadEndRemoval = levelDef.MazeDeadEndRemovalChance;
                return true;
            }
            width = height = entryX = entryY = randomness = sparseness = deadEndRemoval = 0;
            return false;
        }

        public static bool TryResolveExploredBitCount(string zoneName, out ushort exploredBitCount)
        {
            exploredBitCount = 0;
            string baseZone = NormalizeBaseZone(zoneName);
            if (string.IsNullOrWhiteSpace(baseZone))
                return false;

            PackageTextDocument document = null;
            foreach (PackageTextDocument candidate in PackageCatalog.Instance.RuntimeTextDocuments)
            {
                if (candidate == null
                    || candidate.TypeCode != 15
                    || !string.Equals(candidate.Name, baseZone, StringComparison.OrdinalIgnoreCase))
                    continue;
                document = candidate;
                break;
            }
            if (document == null)
                return false;

            GCNode world = GCDatabase.Instance.ResolveWithInheritance(document.GcPath);
            if (world == null || !world.GetBool("Generated", false))
                return false;

            int mazeWidth = world.GetInt("MazeWidth", 0);
            int mazeHeight = world.GetInt("MazeHeight", 0);
            int tileSize = world.GetInt("TileSize", 0);
            if (mazeWidth <= 0 || mazeHeight <= 0 || tileSize <= 0)
                return false;

            long spanX = checked((long)mazeWidth * tileSize);
            long spanY = checked((long)mazeHeight * tileSize);
            long xCells = ((spanX + 256L) >> 7) + 1L;
            long yCells = ((spanY + 256L) >> 7) + 1L;
            long wordsPerRow = (xCells + 31L) / 32L;
            long count = checked(wordsPerRow * yCells);
            if (count <= 0 || count > ushort.MaxValue)
                return false;

            exploredBitCount = (ushort)count;
            Debug.LogError($"[MINIMAP] zone='{baseZone}' maze={mazeWidth}x{mazeHeight} tileSize={tileSize} cells=({xCells},{yCells}) wordsPerRow={wordsPerRow} exploredBitCount={exploredBitCount} source=authored-generated-world-bounds sourceFunction=MiniMapExplored::init@0x004BD980 MiniMapExplored::ReadExploredBits@0x004BDD50");
            return true;
        }

        public static bool TryResolveExploredBitCount(ProceduralDungeonSnapshot snapshot, out ushort exploredBitCount)
        {
            exploredBitCount = 0;
            if (snapshot?.Cells == null || snapshot.Cells.Count == 0)
                return false;

            int minX = 0;
            int minY = 0;
            int maxX = 0;
            int maxY = 0;
            bool hasMinimapObject = false;
            for (int cellIndex = 0; cellIndex < snapshot.Cells.Count; cellIndex++)
            {
                MazeGenerator.MazeCell cell = snapshot.Cells[cellIndex];
                if (cell == null || string.IsNullOrWhiteSpace(cell.TileType))
                    continue;
                TileLayout layout;
                try
                {
                    layout = TileLayoutLoader.LoadAuthored(cell.TileType);
                }
                catch (Exception)
                {
                    continue;
                }
                for (int placementIndex = 0; placementIndex < layout.Placements.Count; placementIndex++)
                {
                    TilePlacement placement = layout.Placements[placementIndex];
                    GCNode staticObject = GCDatabase.Instance.ResolveWithInheritance(placement.ExtendsPath);
                    GCNode description = staticObject?.GetChild("Description")
                        ?? staticObject?.GetChild("Object")?.GetChild("Description")
                        ?? staticObject;
                    if (description == null || string.IsNullOrWhiteSpace(description.GetString("MinimapTexture", "")))
                        continue;
                    int tileSize = Math.Max(description.GetInt("MinimapTileWidth", 0), description.GetInt("MinimapTileHeight", 0));
                    if (tileSize == 0)
                        tileSize = 40;
                    int centerX = checked(cell.WorldOriginFixedX + placement.XFixed) / 256;
                    int centerY = checked(cell.WorldOriginFixedY + placement.YFixed) / 256;
                    int halfSize = tileSize / 2;
                    int objectMinX = centerX - halfSize;
                    int objectMinY = centerY - halfSize;
                    int objectMaxX = centerX + halfSize;
                    int objectMaxY = centerY + halfSize;
                    minX = Math.Min(minX, objectMinX);
                    minY = Math.Min(minY, objectMinY);
                    maxX = Math.Max(maxX, objectMaxX);
                    maxY = Math.Max(maxY, objectMaxY);
                    hasMinimapObject = true;
                }
            }
            if (!hasMinimapObject)
                return false;

            int xCells = checked(((maxX + 128 - (minX - 128)) >> 7) + 1);
            int yCells = checked(((maxY + 128 - (minY - 128)) >> 7) + 1);
            int wordsPerRow = checked((xCells + 31) / 32);
            int count = checked(wordsPerRow * yCells);
            if (count <= 0 || count > ushort.MaxValue)
                return false;

            exploredBitCount = (ushort)count;
            Debug.LogError($"[MINIMAP] zone='{snapshot.ZoneName ?? ""}' bounds=({minX},{minY})-({maxX},{maxY}) padding=128 cells=({xCells},{yCells}) wordsPerRow={wordsPerRow} exploredBitCount={exploredBitCount} sourceFunction=MiniMap::init@0x004BD980 MiniMapExplored::ReadExploredBits@0x004C1600");
            return true;
        }

        private static string CellKey(MazeGenerator.MazeCell cell)
        {
            return $"{cell.GridX}:{cell.GridY}";
        }

        private static MazeGenerator.MazeCell FindCell(List<MazeGenerator.MazeCell> cells, int gridX, int gridY)
        {
            if (cells == null) return null;
            for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
            {
                var cell = cells[cellIndex];
                if (cell.GridX == gridX && cell.GridY == gridY)
                    return cell;
            }
            return null;
        }


        private static MazeGenerator.PlacedRoomNode FindPlacedRoomNode(ProceduralDungeonSnapshot snapshot, int sourceIndex, params string[] tileSetFallbacks)
        {
            if (snapshot?.RoomNodes == null)
                return null;

            for (int roomNodeIndex = 0; roomNodeIndex < snapshot.RoomNodes.Count; roomNodeIndex++)
            {
                var placed = snapshot.RoomNodes[roomNodeIndex];
                if (placed != null && placed.SourceIndex == sourceIndex)
                    return placed;
            }

            if (tileSetFallbacks == null || tileSetFallbacks.Length == 0)
                return null;

            for (int roomNodeIndex = 0; roomNodeIndex < snapshot.RoomNodes.Count; roomNodeIndex++)
            {
                var placed = snapshot.RoomNodes[roomNodeIndex];
                if (placed == null || string.IsNullOrEmpty(placed.TileSet))
                    continue;

                for (int fallbackIndex = 0; fallbackIndex < tileSetFallbacks.Length; fallbackIndex++)
                {
                    if (placed.TileSet.StartsWith(tileSetFallbacks[fallbackIndex], StringComparison.OrdinalIgnoreCase))
                        return placed;
                }
            }

            return null;
        }

        private static RoomNodeDef GetRoomNodeDef(LevelDef level, int sourceIndex)
        {
            if (level?.RoomNodes == null || sourceIndex < 0 || sourceIndex >= level.RoomNodes.Length)
                return null;
            return level.RoomNodes[sourceIndex];
        }

        private static bool MatchesSpawnName(string requested, string spawnName, string linkToSpawn)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return false;

            string normalized = requested.Trim();
            return (!string.IsNullOrEmpty(spawnName) && normalized.Equals(spawnName, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(linkToSpawn) && normalized.Equals(linkToSpawn, StringComparison.OrdinalIgnoreCase));
        }

        public static bool TryResolveSpawnPointFixed(
            ProceduralDungeonSnapshot snapshot,
            string spawnPoint,
            out int posFixedX,
            out int posFixedY,
            out int posFixedZ,
            out int headingFixed,
            out int sourceIndex,
            out string tileType,
            out int gridX,
            out int gridY,
            out int localFixedX,
            out int localFixedY,
            out int localFixedZ,
            out string source)
        {
            posFixedX = 0;
            posFixedY = 0;
            posFixedZ = 0;
            headingFixed = 0;
            sourceIndex = -1;
            tileType = "";
            gridX = 0;
            gridY = 0;
            localFixedX = 0;
            localFixedY = 0;
            localFixedZ = 0;
            source = "";

            if (snapshot == null || string.IsNullOrWhiteSpace(spawnPoint))
                return false;

            if (MatchesSpawnName(spawnPoint, snapshot.EntrySpawnName, snapshot.EntryLinkToSpawn))
            {
                posFixedX = snapshot.PlayerSpawnFixedX;
                posFixedY = snapshot.PlayerSpawnFixedY;
                posFixedZ = snapshot.PlayerSpawnFixedZ;
                headingFixed = snapshot.PlayerHeadingFixed;
                sourceIndex = snapshot.EntrySourceIndex;
                tileType = snapshot.EntryTileType;
                gridX = snapshot.EntryGridX;
                gridY = snapshot.EntryGridY;
                localFixedX = snapshot.PlayerAnchorLocalFixedX;
                localFixedY = snapshot.PlayerAnchorLocalFixedY;
                localFixedZ = snapshot.PlayerAnchorLocalFixedZ;
                source = snapshot.PlayerAnchorSource;
                return true;
            }

            if (MatchesSpawnName(spawnPoint, snapshot.ExitSpawnName, snapshot.ExitLinkToSpawn))
            {
                posFixedX = snapshot.ExitPlayerSpawnFixedX;
                posFixedY = snapshot.ExitPlayerSpawnFixedY;
                posFixedZ = snapshot.ExitPlayerSpawnFixedZ;
                headingFixed = snapshot.ExitPlayerHeadingFixed;
                sourceIndex = snapshot.ExitSourceIndex;
                tileType = snapshot.ExitTileType;
                gridX = snapshot.ExitGridX;
                gridY = snapshot.ExitGridY;
                localFixedX = snapshot.ExitPlayerAnchorLocalFixedX;
                localFixedY = snapshot.ExitPlayerAnchorLocalFixedY;
                localFixedZ = snapshot.ExitPlayerAnchorLocalFixedZ;
                source = snapshot.ExitPlayerAnchorSource;
                return true;
            }

            return false;
        }

        private static string PortalRoomSuffix(string tileType)
        {
            if (string.IsNullOrEmpty(tileType))
                return null;

            string[] prefixes =
            {
                "elmforest_hub_",
                "elmforest_down_",
                "elmforest_up_"
            };

            for (int prefixIndex = 0; prefixIndex < prefixes.Length; prefixIndex++)
            {
                string prefix = prefixes[prefixIndex];
                if (tileType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return tileType.Substring(prefix.Length);
            }

            return null;
        }

        private static bool TryGetAuthoredDungeonAnchor(string tileType, bool playerSpawn, out AuthoredAnchor anchor)
        {
            anchor = default;
            string source = playerSpawn ? "PKG misc.Waypoint SpawnPoint" : "PKG zoneportal model";

            if (!string.IsNullOrEmpty(tileType))
            {
                bool caveHub = tileType.StartsWith("cave_small_hub_", StringComparison.OrdinalIgnoreCase);
                bool caveTracksDown = tileType.StartsWith("dungeon_one_cave_small_tracks_down_", StringComparison.OrdinalIgnoreCase);
                if (caveHub || caveTracksDown)
                {
                    string prefix = caveHub ? "cave_small_hub_" : "dungeon_one_cave_small_tracks_down_";
                    string suffix = tileType.Substring(prefix.Length);
                    if (playerSpawn)
                    {
                        switch (suffix)
                        {
                            case "1n":
                                anchor = WholeAuthoredAnchor(162, 238, 10, 180, source);
                                return true;
                            case "1e":
                                anchor = WholeAuthoredAnchor(242, 192, 10, 90, source);
                                return true;
                            case "1s":
                                anchor = WholeAuthoredAnchor(200, 130, 10, 180, source);
                                return true;
                            case "1w":
                                anchor = WholeAuthoredAnchor(122, 160, 10, -90, source);
                                return true;
                        }
                    }
                    else
                    {
                        string portalSource = caveHub ? "PKG misc.ZonePortal_hub" : "PKG misc.ZonePortal_agg";
                        switch (suffix)
                        {
                            case "1n":
                                anchor = WholeAuthoredAnchor(160, 305, 30, 0, portalSource);
                                return true;
                            case "1e":
                                anchor = WholeAuthoredAnchor(305, 200, 30, 90, portalSource);
                                return true;
                            case "1s":
                                anchor = WholeAuthoredAnchor(200, 55, 30, 0, portalSource);
                                return true;
                            case "1w":
                                anchor = WholeAuthoredAnchor(55, 160, 30, 90, portalSource);
                                return true;
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(tileType) &&
                tileType.StartsWith("elmforest_undergroundentrance_", StringComparison.OrdinalIgnoreCase))
            {
                string suffix = tileType.Substring("elmforest_undergroundentrance_".Length);
                if (playerSpawn)
                {
                    switch (suffix)
                    {
                        case "1n":
                            anchor = WholeAuthoredAnchor(234, 73, 10, -180, "PKG misc.Waypoint SpawnPoint");
                            return true;
                        case "1e":
                            anchor = WholeAuthoredAnchor(70, 170, 10, 90, "PKG misc.Waypoint SpawnPoint");
                            return true;
                        case "1s":
                            anchor = WholeAuthoredAnchor(165, 305, 10, 0, "PKG misc.Waypoint SpawnPoint");
                            return true;
                        case "1w":
                            anchor = WholeAuthoredAnchor(330, 240, 10, -90, "PKG misc.Waypoint SpawnPoint");
                            return true;
                    }
                }
                else
                {
                    switch (suffix)
                    {
                        case "1n":
                            anchor = WholeAuthoredAnchor(236, 128, 14, 0, "PKG misc.ZonePortal_agg");
                            return true;
                        case "1e":
                            anchor = WholeAuthoredAnchor(127, 164, 10, 90, "PKG misc.ZonePortal_agg");
                            return true;
                        case "1s":
                            anchor = WholeAuthoredAnchor(165, 273, 15, 0, "PKG misc.ZonePortal_agg");
                            return true;
                        case "1w":
                            anchor = WholeAuthoredAnchor(272, 236, 10, 90, "PKG misc.ZonePortal_agg");
                            return true;
                    }
                }
            }

            string portalSuffix = PortalRoomSuffix(tileType);
            if (portalSuffix == null)
                return false;

            if (playerSpawn)
            {
                switch (portalSuffix)
                {
                    case "1n":
                        anchor = WholeAuthoredAnchor(150, 70, 10, 180, source);
                        return true;
                    case "1e":
                        anchor = WholeAuthoredAnchor(100, 245, 10, 90, source);
                        return true;
                    case "1s":
                        anchor = WholeAuthoredAnchor(239, 331, 10, 0, source);
                        return true;
                    case "1w":
                        anchor = WholeAuthoredAnchor(323, 161, 10, -90, source);
                        return true;
                }
            }
            else
            {
                switch (portalSuffix)
                {
                    case "1n":
                        anchor = WholeAuthoredAnchor(160, 147, 30, 0, source);
                        return true;
                    case "1e":
                        anchor = WholeAuthoredAnchor(150, 240, 30, -90, source);
                        return true;
                    case "1s":
                        anchor = WholeAuthoredAnchor(240, 251, 30, 0, source);
                        return true;
                    case "1w":
                        anchor = WholeAuthoredAnchor(252, 160, 30, 90, source);
                        return true;
                }
            }

            return false;
        }

        private static (int XFixed, int YFixed, int ZFixed) ResolveAuthoredAnchorFixed(PathMap pathMap, MazeGenerator.MazeCell cell, AuthoredAnchor anchor, out bool walkable)
        {
            walkable = false;
            if (cell == null)
                return (0, 0, 0);

            int worldFixedX = cell.WorldOriginFixedX + anchor.LocalFixedX;
            int worldFixedY = cell.WorldOriginFixedY + anchor.LocalFixedY;
            walkable = IsOpenSpotFixed(pathMap, worldFixedX, worldFixedY);
            return (worldFixedX, worldFixedY, anchor.LocalFixedZ);
        }

        private static bool ResolveSnapshotAnchors(ProceduralDungeonSnapshot snapshot, LevelDef level, PathMap pathMap, List<MazeGenerator.MazeCell> cells)
        {
            if (snapshot == null || level == null || cells == null || cells.Count == 0)
                return false;

            snapshot.MazeWidth = level.MazeWidth;
            snapshot.MazeHeight = level.MazeHeight;

            var entryNode = FindPlacedRoomNode(snapshot, 0, "elmforest_hub_", "elmforest_up_", "cave_small_hub_", "dungeon_one_cave_small_tracks_up_");
            var exitNode = FindPlacedRoomNode(snapshot, 1, "elmforest_down_", "elmforest_undergroundentrance_", "dungeon_one_cave_small_tracks_down_");

            var entryCell = entryNode != null
                ? FindCell(cells, entryNode.GridX, entryNode.GridY)
                : FindCell(cells, level.EntryGridX, level.EntryGridY);
            var exitCell = exitNode != null
                ? FindCell(cells, exitNode.GridX, exitNode.GridY)
                : FindCell(cells, level.ExitGridX, level.ExitGridY);

            if (entryCell == null || exitCell == null)
            {
                Debug.LogError($"[DUNGEON-ANCHOR] zone={snapshot.ZoneName} entryCell={(entryCell != null)} exitCell={(exitCell != null)} reason=missing-authored-room-cell state=blocked");
                snapshot.AnchorsResolved = false;
                return false;
            }

            snapshot.EntrySourceIndex = entryNode?.SourceIndex ?? -1;
            snapshot.ExitSourceIndex = exitNode?.SourceIndex ?? -1;
            var entryDef = GetRoomNodeDef(level, snapshot.EntrySourceIndex);
            var exitDef = GetRoomNodeDef(level, snapshot.ExitSourceIndex);
            snapshot.EntryGridX = entryCell.GridX;
            snapshot.EntryGridY = entryCell.GridY;
            snapshot.ExitGridX = exitCell.GridX;
            snapshot.ExitGridY = exitCell.GridY;
            snapshot.EntryTileType = entryNode?.TileType ?? entryCell.TileType;
            snapshot.ExitTileType = exitNode?.TileType ?? exitCell.TileType;
            snapshot.EntryLinkToZone = entryDef?.LinkToZone;
            snapshot.EntryLinkToSpawn = entryDef?.LinkToSpawn;
            snapshot.EntrySpawnName = entryDef?.SpawnName;
            snapshot.EntryPortalGcType = entryDef?.PortalGcType;
            snapshot.ExitLinkToZone = exitDef?.LinkToZone;
            snapshot.ExitLinkToSpawn = exitDef?.LinkToSpawn;
            snapshot.ExitSpawnName = exitDef?.SpawnName;
            snapshot.ExitPortalGcType = exitDef?.PortalGcType;

            bool playerResolved = TryGetAuthoredDungeonAnchor(snapshot.EntryTileType, true, out var playerAnchor);
            bool exitPlayerResolved = TryGetAuthoredDungeonAnchor(snapshot.ExitTileType, true, out var exitPlayerAnchor);
            bool entryPortalResolved = TryGetAuthoredDungeonAnchor(snapshot.EntryTileType, false, out var entryPortalAnchor);
            bool exitPortalResolved = TryGetAuthoredDungeonAnchor(snapshot.ExitTileType, false, out var exitPortalAnchor);
            if (!playerResolved || !exitPlayerResolved || !entryPortalResolved || !exitPortalResolved)
            {
                Debug.LogError($"[DUNGEON-ANCHOR] zone={snapshot.ZoneName} entry='{snapshot.EntryTileType}' exit='{snapshot.ExitTileType}' player={playerResolved} exitPlayer={exitPlayerResolved} entryPortal={entryPortalResolved} exitPortal={exitPortalResolved} reason=missing-authored-anchor");
                snapshot.AnchorsResolved = false;
                return false;
            }

            snapshot.PlayerAnchorLocalFixedX = playerAnchor.LocalFixedX;
            snapshot.PlayerAnchorLocalFixedY = playerAnchor.LocalFixedY;
            snapshot.PlayerAnchorLocalFixedZ = playerAnchor.LocalFixedZ;
            snapshot.ExitPlayerAnchorLocalFixedX = exitPlayerAnchor.LocalFixedX;
            snapshot.ExitPlayerAnchorLocalFixedY = exitPlayerAnchor.LocalFixedY;
            snapshot.ExitPlayerAnchorLocalFixedZ = exitPlayerAnchor.LocalFixedZ;
            snapshot.EntryPortalAnchorLocalFixedX = entryPortalAnchor.LocalFixedX;
            snapshot.EntryPortalAnchorLocalFixedY = entryPortalAnchor.LocalFixedY;
            snapshot.EntryPortalAnchorLocalFixedZ = entryPortalAnchor.LocalFixedZ;
            snapshot.ExitPortalAnchorLocalFixedX = exitPortalAnchor.LocalFixedX;
            snapshot.ExitPortalAnchorLocalFixedY = exitPortalAnchor.LocalFixedY;
            snapshot.ExitPortalAnchorLocalFixedZ = exitPortalAnchor.LocalFixedZ;
            snapshot.PlayerAnchorSource = playerAnchor.Source;
            snapshot.ExitPlayerAnchorSource = exitPlayerAnchor.Source;
            snapshot.EntryPortalAnchorSource = entryPortalAnchor.Source;
            snapshot.ExitPortalAnchorSource = exitPortalAnchor.Source;

            var playerWorld = ResolveAuthoredAnchorFixed(pathMap, entryCell, playerAnchor, out bool playerWalkable);
            snapshot.PlayerSpawnFixedX = playerWorld.XFixed;
            snapshot.PlayerSpawnFixedY = playerWorld.YFixed;
            snapshot.PlayerSpawnFixedZ = playerWorld.ZFixed;
            snapshot.PlayerHeadingFixed = NormalizeHeadingFixed(playerAnchor.HeadingFixed);
            snapshot.PlayerAnchorWalkable = playerWalkable;

            var exitPlayerWorld = ResolveAuthoredAnchorFixed(pathMap, exitCell, exitPlayerAnchor, out bool exitPlayerWalkable);
            snapshot.ExitPlayerSpawnFixedX = exitPlayerWorld.XFixed;
            snapshot.ExitPlayerSpawnFixedY = exitPlayerWorld.YFixed;
            snapshot.ExitPlayerSpawnFixedZ = exitPlayerWorld.ZFixed;
            snapshot.ExitPlayerHeadingFixed = NormalizeHeadingFixed(exitPlayerAnchor.HeadingFixed);
            snapshot.ExitPlayerAnchorWalkable = exitPlayerWalkable;

            var entryPortalWorld = ResolveAuthoredAnchorFixed(pathMap, entryCell, entryPortalAnchor, out bool entryPortalWalkable);
            snapshot.EntryPortalSpawnFixedX = entryPortalWorld.XFixed;
            snapshot.EntryPortalSpawnFixedY = entryPortalWorld.YFixed;
            snapshot.EntryPortalSpawnFixedZ = entryPortalWorld.ZFixed;
            snapshot.EntryPortalHeadingFixed = NormalizeHeadingFixed(entryPortalAnchor.HeadingFixed);
            snapshot.EntryPortalAnchorWalkable = entryPortalWalkable;

            var exitPortalWorld = ResolveAuthoredAnchorFixed(pathMap, exitCell, exitPortalAnchor, out bool exitPortalWalkable);
            snapshot.ExitPortalSpawnFixedX = exitPortalWorld.XFixed;
            snapshot.ExitPortalSpawnFixedY = exitPortalWorld.YFixed;
            snapshot.ExitPortalSpawnFixedZ = exitPortalWorld.ZFixed;
            snapshot.ExitPortalHeadingFixed = NormalizeHeadingFixed(exitPortalAnchor.HeadingFixed);
            snapshot.ExitPortalAnchorWalkable = exitPortalWalkable;
            snapshot.AnchorsResolved = true;

            Debug.LogError($"[DUNGEON-TRANSFORM] role=player src={snapshot.EntrySourceIndex} tile='{snapshot.EntryTileType}' grid=({snapshot.EntryGridX},{snapshot.EntryGridY}) worldGridY={entryCell.WorldGridY} originFixed=({entryCell.WorldOriginFixedX},{entryCell.WorldOriginFixedY}) centerFixed=({entryCell.WorldCenterFixedX},{entryCell.WorldCenterFixedY}) localFixed=({snapshot.PlayerAnchorLocalFixedX},{snapshot.PlayerAnchorLocalFixedY},{snapshot.PlayerAnchorLocalFixedZ}) sentFixed=({snapshot.PlayerSpawnFixedX},{snapshot.PlayerSpawnFixedY},{snapshot.PlayerSpawnFixedZ}) headingFixed={snapshot.PlayerHeadingFixed} walkable={snapshot.PlayerAnchorWalkable} source='{snapshot.PlayerAnchorSource}'");
            Debug.LogError($"[DUNGEON-TRANSFORM] role=exit-player src={snapshot.ExitSourceIndex} tile='{snapshot.ExitTileType}' grid=({snapshot.ExitGridX},{snapshot.ExitGridY}) worldGridY={exitCell.WorldGridY} originFixed=({exitCell.WorldOriginFixedX},{exitCell.WorldOriginFixedY}) centerFixed=({exitCell.WorldCenterFixedX},{exitCell.WorldCenterFixedY}) localFixed=({snapshot.ExitPlayerAnchorLocalFixedX},{snapshot.ExitPlayerAnchorLocalFixedY},{snapshot.ExitPlayerAnchorLocalFixedZ}) sentFixed=({snapshot.ExitPlayerSpawnFixedX},{snapshot.ExitPlayerSpawnFixedY},{snapshot.ExitPlayerSpawnFixedZ}) headingFixed={snapshot.ExitPlayerHeadingFixed} walkable={snapshot.ExitPlayerAnchorWalkable} source='{snapshot.ExitPlayerAnchorSource}'");
            Debug.LogError($"[DUNGEON-TRANSFORM] role=entry-portal src={snapshot.EntrySourceIndex} tile='{snapshot.EntryTileType}' grid=({snapshot.EntryGridX},{snapshot.EntryGridY}) worldGridY={entryCell.WorldGridY} originFixed=({entryCell.WorldOriginFixedX},{entryCell.WorldOriginFixedY}) centerFixed=({entryCell.WorldCenterFixedX},{entryCell.WorldCenterFixedY}) localFixed=({snapshot.EntryPortalAnchorLocalFixedX},{snapshot.EntryPortalAnchorLocalFixedY},{snapshot.EntryPortalAnchorLocalFixedZ}) sentFixed=({snapshot.EntryPortalSpawnFixedX},{snapshot.EntryPortalSpawnFixedY},{snapshot.EntryPortalSpawnFixedZ}) headingFixed={snapshot.EntryPortalHeadingFixed} walkable={snapshot.EntryPortalAnchorWalkable} source='{snapshot.EntryPortalAnchorSource}'");
            Debug.LogError($"[DUNGEON-TRANSFORM] role=exit-portal src={snapshot.ExitSourceIndex} tile='{snapshot.ExitTileType}' grid=({snapshot.ExitGridX},{snapshot.ExitGridY}) worldGridY={exitCell.WorldGridY} originFixed=({exitCell.WorldOriginFixedX},{exitCell.WorldOriginFixedY}) centerFixed=({exitCell.WorldCenterFixedX},{exitCell.WorldCenterFixedY}) localFixed=({snapshot.ExitPortalAnchorLocalFixedX},{snapshot.ExitPortalAnchorLocalFixedY},{snapshot.ExitPortalAnchorLocalFixedZ}) sentFixed=({snapshot.ExitPortalSpawnFixedX},{snapshot.ExitPortalSpawnFixedY},{snapshot.ExitPortalSpawnFixedZ}) headingFixed={snapshot.ExitPortalHeadingFixed} walkable={snapshot.ExitPortalAnchorWalkable} source='{snapshot.ExitPortalAnchorSource}'");
            return true;
        }

        private static void ResolvePathMapBuildSeedFixed(
            MazeGenerator maze,
            List<MazeGenerator.MazeCell> cells,
            out int seedFixedX,
            out int seedFixedY,
            out int seedFixedZ,
            out string source)
        {
            MazeGenerator.MazeCell entryCell = null;
            if (maze?.PlacedRoomNodes != null)
            {
                foreach (var roomNode in maze.PlacedRoomNodes)
                {
                    if (roomNode.SourceIndex != 0)
                        continue;
                    entryCell = FindCell(cells, roomNode.GridX, roomNode.GridY);
                    if (entryCell != null)
                        break;
                }
            }

            entryCell ??= cells != null && cells.Count > 0 ? cells[0] : null;
            if (entryCell == null)
            {
                seedFixedX = 0;
                seedFixedY = 0;
                seedFixedZ = 0;
                source = "missing-cell";
                return;
            }

            seedFixedX = entryCell.WorldCenterFixedX;
            seedFixedY = entryCell.WorldCenterFixedY;
            seedFixedZ = 0;
            source = "entry-cell-center";
            if (TryGetAuthoredDungeonAnchor(entryCell.TileType, true, out var playerAnchor))
            {
                seedFixedX = entryCell.WorldOriginFixedX + playerAnchor.LocalFixedX;
                seedFixedY = entryCell.WorldOriginFixedY + playerAnchor.LocalFixedY;
                seedFixedZ = playerAnchor.LocalFixedZ;
                source = "authored-entry-player-anchor";
            }
        }

        public static List<DungeonSpawnData> GenerateSpawns(string zoneName, uint seed)
        {
            var snapshot = GenerateSnapshot(zoneName, seed);
            return snapshot?.Spawns;
        }

        public static ProceduralDungeonSnapshot GenerateSnapshot(string zoneName, uint seed, uint roomSeed = 0, string instanceKey = null)
        {
            string baseZone = NormalizeBaseZone(zoneName);
            string pathMapKey = string.IsNullOrWhiteSpace(instanceKey) ? baseZone : instanceKey;
            var snapshot = new ProceduralDungeonSnapshot
            {
                ZoneName = baseZone,
                LayoutSeed = seed,
                RoomSeed = roomSeed
            };
            var spawns = snapshot.Spawns;

            if (!LevelDefs.TryGetValue(baseZone, out LevelDef level))
            {
                Debug.LogError($"[MAZE-SPAWNER] zone='{zoneName}' reason=no-level-definition");
                return snapshot;
            }

            Debug.LogError($"[MAZE-SPAWNER] begin zone={baseZone} size={level.MazeWidth}x{level.MazeHeight} seed=0x{seed:X8}");

            Debug.LogError($"[MAZE-SPAWNER] rng layoutSeed=0x{seed:X8} entityManagerOpcode0CSeed=0x{roomSeed:X8}");
            var rng = new CombatRandom(seed);
            RngLedger.LogSeed("layout", "DungeonMazeSpawner::layout-seed", seed, baseZone);

            var maze = new MazeGenerator(
                level.MazeWidth, level.MazeHeight, seed,
                level.MazeRandomness, level.MazeSparseness,
                level.MazeDeadEndRemovalChance,
                rng,
                level.TileSize
            );
            Debug.LogError($"[MAZE-SPAWNER] worldRoot=client-integer-half-grid tileSize={level.TileSize} pathMapCenterOverride=False");
            if (level.RoomNodes != null)
            {
                for (int nodeIndex = 0; nodeIndex < level.RoomNodes.Length; nodeIndex++)
                {
                    var node = level.RoomNodes[nodeIndex];
                    maze.AddRoomNode(node.TileSet, node.GridX, node.GridY, node.Chance, nodeIndex);
                }
            }
            var cells = maze.BuildWorld(level.TileSetPrefix);
            var worldCells = new List<MazeGenerator.MazeCell>(maze.WorldCells);

            Debug.LogError($"[MAZE-SPAWNER] cells={cells.Count} worldTiles={worldCells.Count} paddingTiles={maze.PaddingCellCount}");

            WorldCollision.Instance.PrepareProceduralInstance(baseZone, pathMapKey, worldCells);

            ResolvePathMapBuildSeedFixed(
                maze,
                cells,
                out int pathMapSeedFixedX,
                out int pathMapSeedFixedY,
                out int pathMapSeedFixedZ,
                out string pathMapSeedSource);
            Debug.LogError(
                $"[PATHMAP-SEED] zone={baseZone} instance='{pathMapKey}' fixed8=({pathMapSeedFixedX},{pathMapSeedFixedY},{pathMapSeedFixedZ}) source={pathMapSeedSource}");
            var mazePathMap = DungeonRunners.Utilities.PathMapBuilder.Build(
                pathMapKey,
                worldCells,
                pathMapSeedFixedX,
                pathMapSeedFixedY,
                pathMapSeedFixedZ);
            snapshot.PathMap = mazePathMap;
            if (mazePathMap == null)
                Debug.LogError($"[MAZE-SPAWNER] zone={baseZone} reason=pathmap-build-null cells={cells.Count}");

            int encIdx = 0;
            int totalRegular = 0;

            cells.Sort((a, b) =>
            {
                int cmp = b.GridY.CompareTo(a.GridY);
                return cmp != 0 ? cmp : a.GridX.CompareTo(b.GridX);
            });

            var cellsByKey = new Dictionary<string, MazeGenerator.MazeCell>();
            foreach (var cell in cells)
                cellsByKey[CellKey(cell)] = cell;

            var roomCellKeys = new HashSet<string>();
            var encounterRooms = new List<(MazeGenerator.MazeCell Cell, RoomNodeDef Node, MazeGenerator.PlacedRoomNode Placed)>();
            foreach (var placed in maze.PlacedRoomNodes)
            {
                string key = $"{placed.GridX}:{placed.GridY}";
                roomCellKeys.Add(key);
                if (cellsByKey.TryGetValue(key, out var cell))
                {
                    RoomNodeDef node = null;
                    if (level.RoomNodes != null && placed.SourceIndex >= 0 && placed.SourceIndex < level.RoomNodes.Length)
                        node = level.RoomNodes[placed.SourceIndex];

                    Debug.LogError($"[MAZE-SPAWNER] roomNode src={placed.SourceIndex} tileSet='{placed.TileSet}' tile='{placed.TileType}' grid=({placed.GridX},{placed.GridY}) encounter={(node?.EncounterTable != null)}");
                    if (node?.EncounterTable != null)
                        encounterRooms.Add((cell, node, placed));
                }
                else
                {
                    Debug.LogError($"[MAZE-SPAWNER] roomNode src={placed.SourceIndex} grid=({placed.GridX},{placed.GridY}) reason=no-cell");
                }
            }

            if (encounterRooms.Count == 0 && level.LeaderEncounterTable != null)
                Debug.LogError($"[MAZE-SPAWNER] zone={baseZone} reason=missing-authored-leader-room state=blocked");

            foreach (var cell in cells)
            {
                if (roomCellKeys.Contains(CellKey(cell)))
                    continue;
                if (level.EncounterTable != null)
                {
                    string regularGroupKey = $"{baseZone}:enc:{encIdx}";
                    totalRegular += AddProceduralEncounterSpawns(
                        snapshot,
                        spawns,
                        baseZone,
                        level.EncounterTable,
                        cell,
                        regularGroupKey,
                        rng,
                        "procedural-regular",
                        $"world-cell:{cell.GridX}:{cell.GridY}");
                    encIdx++;
                }
            }

            Debug.LogError($"[MAZE-SPAWNER] regular={totalRegular} markers={encIdx}");

            int leaderIdx = 0;
            int totalLeaders = 0;

            var leaderRooms = new List<(MazeGenerator.MazeCell Cell, EncounterTableManifest Table, string Source)>();
            foreach (var encounterRoom in encounterRooms)
                leaderRooms.Add((encounterRoom.Cell, encounterRoom.Node.EncounterTable, $"roomNode:{encounterRoom.Placed.SourceIndex}"));
            if (leaderRooms.Count > 0)
            {
                foreach (var leaderRoom in leaderRooms)
                {
                    var bestCell = leaderRoom.Cell;
                    int leaderGroupOrdinal = leaderIdx;
                    string leaderGroupKey = $"{baseZone}:leader:{leaderGroupOrdinal}";
                    leaderIdx++;
                    Debug.LogError($"[MAZE-SPAWNER] leader source={leaderRoom.Source} group={leaderGroupKey} cell=({bestCell.GridX},{bestCell.GridY}) tile='{bestCell.TileType}'");
                    totalLeaders += AddProceduralEncounterSpawns(
                        snapshot,
                        spawns,
                        baseZone,
                        leaderRoom.Table,
                        bestCell,
                        leaderGroupKey,
                        rng,
                        "procedural-leader",
                        leaderRoom.Source);
                }
            }

            snapshot.Cells = new List<MazeGenerator.MazeCell>(cells);
            snapshot.WorldCells = worldCells;
            snapshot.RoomNodes = new List<MazeGenerator.PlacedRoomNode>(maze.PlacedRoomNodes);
            ResolveSnapshotAnchors(snapshot, level, mazePathMap, cells);
            Debug.LogError($"[MAZE-SPAWNER] total={spawns.Count} regular={totalRegular} leaders={totalLeaders} zone={baseZone}");
            Debug.LogError($"[DUNGEON-SNAPSHOT] zone={baseZone} layoutSeed=0x{snapshot.LayoutSeed:X8} roomSeed=0x{snapshot.RoomSeed:X8} cells={snapshot.Cells.Count} roomNodes={snapshot.RoomNodes.Count} spawns={snapshot.Spawns.Count} entry=({snapshot.EntryGridX},{snapshot.EntryGridY}) entryTile='{snapshot.EntryTileType}' playerFixed=({snapshot.PlayerSpawnFixedX},{snapshot.PlayerSpawnFixedY},{snapshot.PlayerSpawnFixedZ}) entryPortalFixed=({snapshot.EntryPortalSpawnFixedX},{snapshot.EntryPortalSpawnFixedY},{snapshot.EntryPortalSpawnFixedZ}) exit=({snapshot.ExitGridX},{snapshot.ExitGridY}) exitTile='{snapshot.ExitTileType}' exitPortalFixed=({snapshot.ExitPortalSpawnFixedX},{snapshot.ExitPortalSpawnFixedY},{snapshot.ExitPortalSpawnFixedZ}) yTransform=worldGridY=gridY/BuildWorld");
            return snapshot;
        }
    }
}
