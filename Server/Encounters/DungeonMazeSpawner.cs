using System;
using System.Collections.Generic;
using DungeonRunners.Engine;
using DungeonRunners.Combat;
using DungeonRunners.Core;
using DungeonRunners.Data;
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
            public Vector3 PlayerSpawn;
            public float PlayerHeading;
            public Vector3 ExitPlayerSpawn;
            public float ExitPlayerHeading;
            public Vector3 EntryPortalSpawn;
            public float EntryPortalHeading;
            public Vector3 ExitPortalSpawn;
            public float ExitPortalHeading;
            public Vector3 PortalSpawn;
            public float PortalHeading;
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
            public string PortalAnchorSource;
            public Vector3 PlayerAnchorLocal;
            public Vector3 ExitPlayerAnchorLocal;
            public Vector3 EntryPortalAnchorLocal;
            public Vector3 ExitPortalAnchorLocal;
            public Vector3 PortalAnchorLocal;
            public bool PlayerAnchorWalkable;
            public bool ExitPlayerAnchorWalkable;
            public bool EntryPortalAnchorWalkable;
            public bool ExitPortalAnchorWalkable;
            public bool PortalAnchorWalkable;
            public List<MazeGenerator.MazeCell> Cells = new();
            public PathMap PathMap;
            public List<MazeGenerator.PlacedRoomNode> RoomNodes = new();
            public List<DatabaseLoader.DungeonSpawnData> Spawns = new();
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
            public float WorldOriginX;
            public float WorldOriginY;
            public string Source;
            public string ManifestSource;
            public float ChoiceChance;
        }


        private struct EncounterMarker
        {
            public float LocalX;
            public float LocalY;
            public float LocalZ;
            public float Heading;
            public float SizeX;
            public float SizeY;
            public string Source;

            public EncounterMarker(float x, float y, float z, float heading = 0f, float sizeX = 0f, float sizeY = 0f, string source = "PKG tile placeholder")
            {
                LocalX = x; LocalY = y; LocalZ = z;
                Heading = heading;
                SizeX = sizeX;
                SizeY = sizeY;
                Source = source;
            }
        }

        private struct AuthoredAnchor
        {
            public float LocalX;
            public float LocalY;
            public float LocalZ;
            public float Heading;
            public string Source;

            public AuthoredAnchor(float x, float y, float z, float heading, string source)
            {
                LocalX = x;
                LocalY = y;
                LocalZ = z;
                Heading = heading;
                Source = source;
            }
        }

        private static readonly Dictionary<string, List<EncounterMarker>> TileEncounterPlaceholders =
     new Dictionary<string, List<EncounterMarker>>(StringComparer.OrdinalIgnoreCase)
 {
    { "elmforest_tileset_1n_a",       new List<EncounterMarker> { new(200, 80, 10) } },
    { "elmforest_tileset_1e_a",       new List<EncounterMarker> { new(180, 200, 30, 0f, 0f, 150f) } },
    { "elmforest_tileset_1s_a",       new List<EncounterMarker> { new(210, 340, 10) } },
    { "elmforest_tileset_1w_a",       new List<EncounterMarker> { new(340, 210, 10) } },
    { "elmforest_tileset_1n1s_a",     new List<EncounterMarker> { new(160, 200, -10) } },
    { "elmforest_tileset_1e1w_a",     new List<EncounterMarker> { new(230, 170, 10, -180f, 125f) } },
    { "elmforest_tileset_1n1e_a",     new List<EncounterMarker> { new(230, 70, 10, -180f, 85f, 85f), new(70, 250, 10) } },
    { "elmforest_tileset_1n1w_a",     new List<EncounterMarker> { new(250, 210, 30) } },
    { "elmforest_tileset_1e1s_a",     new List<EncounterMarker> { new(180, 310, 10, 0f, 150f, 150f) } },
    { "elmforest_tileset_1s1w_a",     new List<EncounterMarker> { new(320, 180, 10), new(160, 270, 10) } },
    { "elmforest_tileset_1n1e1s_a",   new List<EncounterMarker> { new(220, 280, 10), new(90, 110, 10) } },
    { "elmforest_tileset_1n1e1w_a",   new List<EncounterMarker> { new(200, 120, 10) } },
    { "elmforest_tileset_1n1s1w_a",   new List<EncounterMarker> { new(200, 190, 10) } },
    { "elmforest_tileset_1e1s1w_a",   new List<EncounterMarker> { new(220, 150, 30) } },
    { "elmforest_tileset_1n1e1s1w_a", new List<EncounterMarker> { new(190, 190, 10, -720f, 125f, 125f) } },
    { "tutorial_loot_1n",             new List<EncounterMarker> { new(200, 80, 10, -180f) } },
    { "tutorial_loot_1e",             new List<EncounterMarker> { new(180, 200, 30, 0f, 100f, 150f) } },
    { "tutorial_loot_1s",             new List<EncounterMarker> { new(190, 360, 10, -900f) } },
    { "tutorial_loot_1w",             new List<EncounterMarker> { new(340, 210, 10, -900f) } },
    { "elmforest_questfindring_1n1e1w", new List<EncounterMarker> { new(300, 230, 10) } },
    { "elmforest_questfindring_1w",     new List<EncounterMarker> { new(340, 180, 10) } },
    { "elmforest_tileset_0n_a",       new List<EncounterMarker>() },
 };



        private struct SpawnUnit
        {
            public string GcType;
            public string SpawnGcTypeOverride;
            public int Count;
            public float Difficulty;
            public string AuthoredType => SpawnGcTypeOverride ?? GcType;
            public SpawnUnit(string gcType, int count, float difficulty = 1f, string spawnGcTypeOverride = null)
            {
                GcType = gcType;
                Count = count;
                Difficulty = difficulty;
                SpawnGcTypeOverride = spawnGcTypeOverride;
            }
        }

        private sealed class EncounterTableManifest
        {
            public readonly string AuthoredPath;
            public readonly int EntryId;
            private readonly SpawnUnit[][] _fallbackChoices;
            private readonly float[] _fallbackChoiceChances;
            private SpawnUnit[][] _pkgChoices;
            private float[] _pkgChoiceChances;
            private bool _pkgResolved;
            private bool _pkgResolutionLogged;
            private string _pkgSource = "static-fallback";
            private string _pkgDetail = "";

            public EncounterTableManifest(string authoredPath, int entryId, SpawnUnit[][] choices)
            {
                AuthoredPath = authoredPath;
                EntryId = entryId;
                _fallbackChoices = choices ?? Array.Empty<SpawnUnit[]>();
                _fallbackChoiceChances = BuildDefaultChoiceChances(_fallbackChoices.Length);
            }

            public SpawnUnit[][] Choices
            {
                get
                {
                    EnsurePkgResolved();
                    return _pkgChoices ?? _fallbackChoices;
                }
            }

            public int FallbackLength => _fallbackChoices.Length;
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

            public float ChoiceChance(int index)
            {
                EnsurePkgResolved();
                var chances = _pkgChoiceChances ?? _fallbackChoiceChances;
                return index >= 0 && index < chances.Length ? chances[index] : 1f;
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
                    return;

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
                    _pkgSource = "static-fallback";
                    _pkgDetail = detail;
                }

                if (!_pkgResolutionLogged)
                {
                    _pkgResolutionLogged = true;
                    Debug.LogError($"[ENCOUNTER-MANIFEST] authored='{AuthoredPath}' entry={EntryId} source='{_pkgSource}' detail='{_pkgDetail}' choices={Length} fallbackChoices={FallbackLength} packCount=EncounterUnit.Count chanceWeighting=generator-table difficulty=EncounterUnit sourceFunction=RoomNode::prep+EncounterObject::update");
                }
            }
        }

        private static float[] BuildDefaultChoiceChances(int count)
        {
            if (count <= 0)
                return Array.Empty<float>();
            var chances = new float[count];
            for (int chanceIndex = 0; chanceIndex < chances.Length; chanceIndex++)
                chances[chanceIndex] = 1f;
            return chances;
        }

        private static bool TryResolveEncounterTableFromGc(
            EncounterTableManifest manifest,
            out SpawnUnit[][] choices,
            out float[] choiceChances,
            out string source,
            out string detail)
        {
            choices = null;
            choiceChances = null;
            source = "static-fallback";
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
            var parsedChances = new List<float>();
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
                parsedChances.Add(encounter.GetFloat("Chance", 1f));
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

            float difficulty = unitNode.GetFloat("Difficulty", 1f);
            if (!TryResolveSpawnBaseGcType(typePath.Trim(), out string gcType, out string spawnOverride, out detail))
                return false;

            unit = new SpawnUnit(gcType, 1, difficulty, spawnOverride);
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

            if (DatabaseLoader.FindCreature(typePath) != null)
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

                if (DatabaseLoader.FindCreature(currentPath) != null)
                {
                    gcType = currentPath;
                    spawnOverride = string.Equals(currentPath, typePath, StringComparison.OrdinalIgnoreCase) ? null : typePath;
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(raw.Extends) && DatabaseLoader.FindCreature(raw.Extends) != null)
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
            new EncounterTableManifest("world.dungeon00.enc.level01_encounter", 23853, new SpawnUnit[][]
            {
                new[] { new SpawnUnit("creatures.forestCreatures.Warg.Basic.Pup", 1, 0.75f, "world.dungeon00.mob.melee01.rank1") },
                new[] { new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 0.75f, "world.dungeon00.mob.melee03.rank1") },
                new[] { new SpawnUnit("creatures.forestCreatures.Warg.Basic.Pup", 1, 0.50f, "world.dungeon00.mob.melee01.rank1") },
                new[] { new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 0.60f, "world.dungeon00.mob.melee03.rank1") },
                new[] { new SpawnUnit("creatures.forestCreatures.Warg.Basic.Pup", 1, 0.25f, "world.dungeon00.mob.melee01.rank1"),
                        new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 0.50f, "world.dungeon00.mob.melee03.rank1") },
            });

        private static readonly EncounterTableManifest Level01LeaderEncounter =
            new EncounterTableManifest("world.dungeon00.enc.level01_leader_encounter", 23854, new SpawnUnit[][]
            {
                new[] { new SpawnUnit("creatures.forestCreatures.Warg.Basic.Pup", 1, 0f, "world.dungeon00.mob.melee01.rank1"),
                        new SpawnUnit("creatures.forestCreatures.Warg.Basic.Grunt", 1, 0.5f, "world.dungeon00.mob.melee02.rank1") },
            });

        private static readonly EncounterTableManifest Level02Encounter =
            new EncounterTableManifest("world.dungeon00.enc.level02_encounter", 23855, new SpawnUnit[][]
            {
                new[] { new SpawnUnit("creatures.forestCreatures.Warg.Basic.Pup", 1, 0.75f, "world.dungeon00.mob.melee01.rank2") },
                new[] { new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 0.75f, "world.dungeon00.mob.melee03.rank2") },
                new[] { new SpawnUnit("creatures.forestCreatures.Warg.Basic.Pup", 1, 0.25f, "world.dungeon00.mob.melee01.rank2"),
                        new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 0.50f, "world.dungeon00.mob.melee03.rank2") },
                new[] { new SpawnUnit("creatures.forestCreatures.Warg.Basic.Grunt", 1, 0.50f, "world.dungeon00.mob.melee02.rank2") },
                new[] { new SpawnUnit("creatures.forestCreatures.Warg.Basic.Pup", 1, 0.50f, "world.dungeon00.mob.melee01.rank2"),
                        new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 0.50f, "world.dungeon00.mob.melee03.rank2") },
                new[] { new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 0.25f, "world.dungeon00.mob.melee03.rank2"),
                        new SpawnUnit("creatures.forestCreatures.Warg.Basic.Pup", 1, 0.50f, "world.dungeon00.mob.melee01.rank2") },
                new[] { new SpawnUnit("creatures.forestCreatures.Warg.Basic.Pup", 1, 0.25f, "world.dungeon00.mob.melee01.rank2"),
                        new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 0.50f, "world.dungeon00.mob.melee03.rank2") },
                new[] { new SpawnUnit("creatures.whiskers.blademaster.Basic.Grunt", 1, 0.50f, "world.dungeon00.mob.melee04.rank2") },
            });

        private static readonly EncounterTableManifest Level02LeaderEncounter =
            new EncounterTableManifest("world.dungeon00.enc.level02_leader_encounter", 23856, new SpawnUnit[][]
            {
                new[] { new SpawnUnit("creatures.forestCreatures.Warg.Basic.Grunt", 1, 0f, "world.dungeon00.mob.melee02.rank2"),
                        new SpawnUnit("creatures.forestCreatures.Warg.Basic.Pup", 1, 0.60f, "world.dungeon00.mob.melee01.rank2") },
                new[] { new SpawnUnit("creatures.whiskers.blademaster.Basic.Grunt", 1, 0f, "world.dungeon00.mob.melee04.rank2"),
                        new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 0.60f, "world.dungeon00.mob.melee03.rank2") },
            });

        private static readonly EncounterTableManifest Level03Encounter =
            new EncounterTableManifest("world.dungeon00.enc.level03_encounter", 23857, new SpawnUnit[][]
            {
                new[] { new SpawnUnit("creatures.forestCreatures.Warg.Basic.Pup", 1, 0.75f, "world.dungeon00.mob.melee01.rank3") },
                new[] { new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 0.75f, "world.dungeon00.mob.melee03.rank3") },
                new[] { new SpawnUnit("creatures.forestCreatures.Warg.Basic.Pup", 1, 0.50f, "world.dungeon00.mob.melee01.rank3"),
                        new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 0.50f, "world.dungeon00.mob.melee03.rank3") },
                new[] { new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 0.50f, "world.dungeon00.mob.melee03.rank3"),
                        new SpawnUnit("creatures.forestCreatures.Warg.Basic.Pup", 1, 0.25f, "world.dungeon00.mob.melee01.rank3") },
                new[] { new SpawnUnit("creatures.whiskers.blademaster.Basic.Grunt", 1, 0.25f, "world.dungeon00.mob.melee04.rank3"),
                        new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 0.50f, "world.dungeon00.mob.melee03.rank3") },
                new[] { new SpawnUnit("creatures.forestCreatures.Warg.Basic.Grunt", 1, 0f, "world.dungeon00.mob.melee02.rank3"),
                        new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 0.50f, "world.dungeon00.mob.melee03.rank3") },
                new[] { new SpawnUnit("creatures.forestCreatures.Warg.Basic.Grunt", 1, 0f, "world.dungeon00.mob.melee02.rank3"),
                        new SpawnUnit("creatures.forestCreatures.Warg.Basic.Pup", 1, 0.50f, "world.dungeon00.mob.melee01.rank3") },
            });

        private static readonly EncounterTableManifest Level03LeaderEncounter =
            new EncounterTableManifest("world.dungeon00.enc.level03_leader_encounter", 23858, new SpawnUnit[][]
            {
                new[] { new SpawnUnit("creatures.forestCreatures.Warg.Basic.Grunt", 1, 0.25f, "world.dungeon00.mob.melee02.rank3"),
                        new SpawnUnit("creatures.forestCreatures.Warg.Basic.Pup", 1, 0.75f, "world.dungeon00.mob.melee01.rank3") },
                new[] { new SpawnUnit("creatures.whiskers.blademaster.Basic.Grunt", 1, 0.25f, "world.dungeon00.mob.melee04.rank3"),
                        new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 0.75f, "world.dungeon00.mob.melee03.rank3") },
            });

        private static readonly EncounterTableManifest Level04Encounter =
            new EncounterTableManifest("world.dungeon00.enc.level04_encounter", 23860, new SpawnUnit[][]
            {
                new[] { new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 0.75f, "world.dungeon00.mob.melee03.rank3"),
                    new SpawnUnit("terrain.misc.interactives.Breakiable_Barrel_01", 1, 0f),
                    new SpawnUnit("terrain.misc.interactives.Breakiable_Barrel_01", 1, 0f) },
                new[] { new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 1.0f, "world.dungeon00.mob.melee03.rank3"),
                    new SpawnUnit("terrain.misc.interactives.Breakiable_Barrel_01", 1, 0f) },
                new[] { new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 0.65f, "world.dungeon00.mob.melee03.rank3"),
                    new SpawnUnit("creatures.forestCreatures.Warg.Basic.Pup", 1, 0.35f, "world.dungeon00.mob.melee01.rank3"),
                    new SpawnUnit("terrain.misc.interactives.Breakiable_Barrel_01", 1, 0f) },
                new[] { new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 0.65f, "world.dungeon00.mob.melee03.rank3"),
                    new SpawnUnit("creatures.forestCreatures.Warg.Basic.Grunt", 1, 0.35f, "world.dungeon00.mob.melee02.rank3"),
                    new SpawnUnit("terrain.misc.interactives.Breakiable_Barrel_01", 1, 0f) },
                new[] { new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 0.65f, "world.dungeon00.mob.melee03.rank3"),
                    new SpawnUnit("creatures.whiskers.blademaster.Basic.Grunt", 1, 0.35f, "world.dungeon00.mob.melee04.rank3"),
                    new SpawnUnit("terrain.misc.interactives.Breakiable_Barrel_01", 1, 0f) },
                new[] { new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 0.65f, "world.dungeon00.mob.melee03.rank3"),
                    new SpawnUnit("creatures.whiskers.blademaster.Basic.Grunt", 1, 0.35f, "world.dungeon00.mob.melee04.rank3"),
                    new SpawnUnit("terrain.misc.interactives.Breakiable_Barrel_01", 1, 0f) },
            });

        private static readonly EncounterTableManifest Level04LeaderEncounter =
            new EncounterTableManifest("world.dungeon00.enc.level04_leader_encounter", 23861, new SpawnUnit[][]
            {
                new[] { new SpawnUnit("creatures.whiskers.blademaster.Basic.Grunt", 1, 0f, "world.dungeon00.mob.melee04.rank3"),
                        new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 0.5f, "world.dungeon00.mob.melee03.rank3") },
                new[] { new SpawnUnit("creatures.whiskers.blademaster.Basic.Grunt", 1, 0f, "world.dungeon00.mob.melee04.rank3"),
                        new SpawnUnit("creatures.forestCreatures.Warg.Basic.Pup", 1, 0.5f, "world.dungeon00.mob.melee01.rank3") },
                new[] { new SpawnUnit("creatures.forestCreatures.Warg.Basic.Grunt", 1, 0f, "world.dungeon00.mob.melee02.rank3"),
                        new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 0.5f, "world.dungeon00.mob.melee03.rank3") },
                new[] { new SpawnUnit("creatures.forestCreatures.Warg.Basic.Grunt", 1, 0f, "world.dungeon00.mob.melee02.rank3"),
                        new SpawnUnit("creatures.forestCreatures.Warg.Basic.Pup", 1, 0.5f, "world.dungeon00.mob.melee01.rank3") },
            });

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
        };
        private const float MinEncounterUnitSpacing = 12f;
        private const float EncounterDifficultyBudget = 2.25f;

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

        private static float ResolveSpotBudget(int spotSeed)
        {
            uint h = (uint)spotSeed * 2654435761u;
            h ^= h >> 15;
            float frac = (h & 0xFFFF) / 65535f;
            return EncounterDifficultyBudget * (0.5f + 0.5f * frac);
        }

        private static List<SpawnUnit> ExpandEncounterGroup(SpawnUnit[] group, int spotSeed)
        {
            var result = new List<SpawnUnit>();
            if (group == null || group.Length == 0)
                return result;

            var weighted = new List<SpawnUnit>();
            float spent = 0f;
            foreach (var unit in group)
            {
                int authoredCount = Math.Max(1, unit.Count);
                for (int copy = 0; copy < authoredCount; copy++)
                {
                    result.Add(unit);
                    if (unit.Difficulty > 0f)
                    {
                        weighted.Add(unit);
                        spent += unit.Difficulty;
                    }
                }
            }

            if (weighted.Count == 0)
                return result;

            float spotBudget = Math.Max(spent, ResolveSpotBudget(spotSeed));

            float minWeight = float.MaxValue;
            foreach (var unit in weighted)
                if (unit.Difficulty < minWeight)
                    minWeight = unit.Difficulty;

            int fillIndex = 0;
            while (spotBudget - spent >= minWeight)
            {
                var unit = weighted[fillIndex % weighted.Count];
                if (spent + unit.Difficulty > spotBudget)
                    break;
                result.Add(unit);
                spent += unit.Difficulty;
                fillIndex++;
            }

            return result;
        }

        private static Vector3 ResolveEncounterSpawnPoint(
            MazeGenerator.MazeCell cell,
            EncounterMarker marker,
            out bool groundAdjusted,
            out string groundSource)
        {
            groundAdjusted = false;
            groundSource = "none";
            if (cell == null)
                return Vector3.zero;

            float worldX = cell.WorldOriginX + marker.LocalX;
            float worldY = cell.WorldOriginY + marker.LocalY;
            var resolved = (x: worldX, y: worldY);

            float localX = resolved.x - cell.WorldOriginX;
            float localY = resolved.y - cell.WorldOriginY;
            float clampedLocalX = Mathf.Clamp(localX, 0f, MazeGenerator.TILE_SIZE);
            float clampedLocalY = Mathf.Clamp(localY, 0f, MazeGenerator.TILE_SIZE);
            bool clamped = Mathf.Abs(clampedLocalX - localX) > 0.01f || Mathf.Abs(clampedLocalY - localY) > 0.01f;
            if (clamped)
            {
                localX = clampedLocalX;
                localY = clampedLocalY;
                resolved.x = cell.WorldOriginX + localX;
                resolved.y = cell.WorldOriginY + localY;
            }

            float groundZ = ResolveSameTileGroundZ(cell, marker, localX, localY, out groundSource);
            groundAdjusted = clamped || Mathf.Abs(groundZ - marker.LocalZ) > 0.01f;
            if (clamped)
                groundSource += ";xyClampedSameTile";

            return new Vector3(resolved.x, resolved.y, groundZ);
        }

        private static float ResolveSameTileGroundZ(MazeGenerator.MazeCell cell, EncounterMarker marker, float localX, float localY, out string source)
        {
            source = $"same-tile authored marker z={marker.LocalZ:F1}";
            float bestZ = NormalizeUnitGroundZ(marker.LocalZ);
            float bestDistSq = 0f;

            if (TileEncounterPlaceholders.TryGetValue(cell.TileType, out var markers) && markers != null)
            {
                bool found = false;
                for (int markerIndex = 0; markerIndex < markers.Count; markerIndex++)
                {
                    var candidate = markers[markerIndex];
                    float dx = localX - candidate.LocalX;
                    float dy = localY - candidate.LocalY;
                    float distSq = dx * dx + dy * dy;
                    if (!found || distSq < bestDistSq)
                    {
                        found = true;
                        bestDistSq = distSq;
                        bestZ = NormalizeUnitGroundZ(candidate.LocalZ);
                        source = $"same-tile PKG encounter anchor tile='{cell.TileType}' marker={markerIndex} authoredZ={candidate.LocalZ:F1} groundZ={bestZ:F1}";
                    }
                }
            }

            if (Mathf.Abs(bestZ - marker.LocalZ) > 0.01f)
                source += ";unitGroundFromNearestAnchor";
            return bestZ;
        }

        private static float NormalizeUnitGroundZ(float authoredZ)
        {
            return authoredZ;
        }

        private static float NormalizeHeading(float heading)
        {
            heading %= 360f;
            if (heading < 0f) heading += 360f;
            return heading;
        }


        private const float EncounterSpotClearance = 30f;

        private static bool IsOpenSpot(PathMap pathMap, float x, float y)
        {
            if (pathMap == null) return false;
            if (!pathMap.IsWalkable(x, y)) return false;
            const float diag = EncounterSpotClearance * 0.70710678f;
            if (!pathMap.IsWalkable(x + EncounterSpotClearance, y)) return false;
            if (!pathMap.IsWalkable(x - EncounterSpotClearance, y)) return false;
            if (!pathMap.IsWalkable(x, y + EncounterSpotClearance)) return false;
            if (!pathMap.IsWalkable(x, y - EncounterSpotClearance)) return false;
            if (!pathMap.IsWalkable(x + diag, y + diag)) return false;
            if (!pathMap.IsWalkable(x - diag, y + diag)) return false;
            if (!pathMap.IsWalkable(x + diag, y - diag)) return false;
            if (!pathMap.IsWalkable(x - diag, y - diag)) return false;
            return true;
        }

        private static bool HasEncounterClearance(List<Vector2> occupied, float x, float y)
        {
            float minDistSq = MinEncounterUnitSpacing * MinEncounterUnitSpacing;
            for (int occupiedIndex = 0; occupiedIndex < occupied.Count; occupiedIndex++)
            {
                float dx = occupied[occupiedIndex].x - x;
                float dy = occupied[occupiedIndex].y - y;
                if (dx * dx + dy * dy < minDistSq)
                    return false;
            }
            return true;
        }

        private const float DefaultEncounterArea = 100f;

        private static (float x, float y, bool found) ResolveEncounterAreaSpot(
            PathMap pathMap, float centerX, float centerY, int slot, int slotCount, float sizeX, float sizeY, List<Vector2> occupied)
        {
            float areaX = sizeX > 0f ? sizeX : DefaultEncounterArea;
            float areaY = sizeY > 0f ? sizeY : DefaultEncounterArea;
            float halfX = areaX * 0.5f;
            float halfY = areaY * 0.5f;
            int columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(slotCount)));
            int rows = Math.Max(1, (int)Math.Ceiling(slotCount / (float)columns));
            int col = slot % columns;
            int row = slot / columns;
            float stepX = columns > 1 ? areaX / (columns - 1) : 0f;
            float stepY = rows > 1 ? areaY / (rows - 1) : 0f;
            float targetX = centerX - halfX + (columns > 1 ? col * stepX : halfX);
            float targetY = centerY - halfY + (rows > 1 ? row * stepY : halfY);
            var spot = FindWalkableSpot(pathMap, targetX, targetY, Math.Max(halfX, halfY), occupied);
            if (spot.found)
                return spot;
            return FindWalkableSpot(pathMap, centerX, centerY, 250f, occupied);
        }

        internal static (float x, float y, bool found) FindWalkableSpot(PathMap pathMap, float x, float y, float radius = 250f)
        {
            if (IsOpenSpot(pathMap, x, y))
                return (x, y, true);

            for (float r = 5f; r <= radius; r += 5f)
            {
                for (float angle = 0; angle < 360; angle += 20)
                {
                    float rad = angle * Mathf.Deg2Rad;
                    float testX = x + Mathf.Cos(rad) * r;
                    float testY = y + Mathf.Sin(rad) * r;
                    if (IsOpenSpot(pathMap, testX, testY))
                        return (testX, testY, true);
                }
            }
            return (x, y, false);
        }

        internal static (float x, float y, bool found) FindWalkableSpot(PathMap pathMap, float x, float y, float radius, List<Vector2> occupied)
        {
            if (IsOpenSpot(pathMap, x, y) && HasEncounterClearance(occupied, x, y))
                return (x, y, true);

            for (float r = 5f; r <= radius; r += 5f)
            {
                for (float angle = 0; angle < 360; angle += 20)
                {
                    float rad = angle * Mathf.Deg2Rad;
                    float testX = x + Mathf.Cos(rad) * r;
                    float testY = y + Mathf.Sin(rad) * r;
                    if (IsOpenSpot(pathMap, testX, testY) && HasEncounterClearance(occupied, testX, testY))
                        return (testX, testY, true);
                }
            }
            for (float r = (float)MinEncounterUnitSpacing; r <= radius; r += (float)MinEncounterUnitSpacing)
            {
                for (float angle = 0; angle < 360; angle += 30)
                {
                    float rad = angle * Mathf.Deg2Rad;
                    float testX = x + Mathf.Cos(rad) * r;
                    float testY = y + Mathf.Sin(rad) * r;
                    if (pathMap != null && pathMap.IsWalkable(testX, testY) && HasEncounterClearance(occupied, testX, testY))
                        return (testX, testY, true);
                }
            }
            return (x, y, false);
        }

        private static readonly EncounterMarker[] Level03BossRegularMarkers =
        {
            new(110f, -990f, 40f),
            new(-170f, -960f, 40f),
            new(-500f, -530f, 40f),
            new(160f, -320f, 39.9961f),
            new(60f, -110f, 40f),
            new(-20f, -230f, 39.9961f),
            new(-250f, -190f, 40f),
            new(-150f, -450f, 40f),
            new(-260f, -580f, 39.9961f),
            new(-150f, -790f, 39.9961f),
            new(20f, -850f, 40f),
        };

        private static readonly EncounterMarker Level03BossLootGuardMarker =
            new(-440f, -660f, 40f);

        private static readonly (SpawnUnit Unit, float X, float Y, float Z, float Heading)[] Level03BossPosse =
        {
            (new SpawnUnit("world.dungeon00.mob.boss", 1, 1f, "world.dungeon00.mob.boss_posse.RattleTooth"), 405f, -1195f, 40f, 0f),
            (new SpawnUnit("creatures.whiskers.blademaster.Basic.Grunt", 1, 1f, "world.dungeon00.mob.boss_posse.Blademaster"), 430f, -1205f, 40f, 0f),
            (new SpawnUnit("creatures.whiskers.blademaster.Basic.Grunt", 1, 1f, "world.dungeon00.mob.boss_posse.Blademaster"), 380f, -1205f, 40f, 0f),
            (new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 1f, "world.dungeon00.mob.boss_posse.Broodling"), 450f, -1260f, 40f, 0f),
            (new SpawnUnit("creatures.whiskers.broodling.Basic.Grunt", 1, 1f, "world.dungeon00.mob.boss_posse.Broodling"), 360f, -1260f, 40f, 0f),
            (new SpawnUnit("creatures.forestCreatures.Warg.Basic.Grunt", 1, 1f, "world.dungeon00.mob.boss_posse.boss_guard"), 420f, -1230f, 40f, 0f),
            (new SpawnUnit("creatures.forestCreatures.Warg.Basic.Grunt", 1, 1f, "world.dungeon00.mob.boss_posse.boss_guard"), 390f, -1230f, 40f, 0f),
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
                int chance = Math.Max(1, (int)Math.Round(table.ChoiceChance(choiceIndex), MidpointRounding.AwayFromZero));
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

        private static void AddSpawn(List<DatabaseLoader.DungeonSpawnData> spawns, string zoneName, SpawnUnit unit,
            float x, float y, float z, float heading, string groupKey)
        {
            spawns.Add(new DatabaseLoader.DungeonSpawnData
            {
                zoneName = zoneName,
                gcType = unit.GcType,
                spawnGcTypeOverride = unit.SpawnGcTypeOverride,
                posX = x,
                posY = y,
                posZ = z,
                heading = NormalizeHeading(heading),
                encounterGroupKey = groupKey,
                encounterDifficulty = unit.Difficulty
            });
        }

        private static void RecordEncounterObjectMirror(ProceduralDungeonSnapshot snapshot, string zoneName, string groupKey,
            EncounterTableManifest table, int choiceIndex, string role, MazeGenerator.MazeCell cell, int markerIndex,
            SpawnUnit[] group, string source)
        {
            int packSlots = ExpandEncounterGroup(group, StableSpotSeed(groupKey)).Count;
            int unitRows = group?.Length ?? 0;
            float choiceChance = table?.ChoiceChance(choiceIndex) ?? 1f;
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
                    WorldOriginX = cell?.WorldOriginX ?? 0f,
                    WorldOriginY = cell?.WorldOriginY ?? 0f,
                    Source = source,
                    ManifestSource = manifestSource,
                    ChoiceChance = choiceChance
                });
            }

            Debug.LogError($"[ENCOUNTER-OBJECT] zone={zoneName} group={groupKey} role={role} authored='{table?.AuthoredPath ?? ""}' entry={table?.EntryId ?? 0} choice={choiceIndex} chance={choiceChance:F3} manifestSource='{manifestSource}' packSlots={packSlots} unitRows={unitRows} cell=({cell?.GridX ?? -1},{cell?.GridY ?? -1}) tile='{cell?.TileType ?? ""}' marker={markerIndex} source='{source ?? ""}' mirrorOnly=True packCount=EncounterUnit.Count difficulty=EncounterUnit sourceFunction=RoomNode::prep+EncounterObject::update");
        }

        private static int AddStaticEncounterSpawns(List<DatabaseLoader.DungeonSpawnData> spawns, string zoneName,
            EncounterTableManifest table, EncounterMarker marker, string groupKey, CombatRandom rng, string role)
        {
            int choiceIndex = NextTableIndex(rng, table, "DungeonMazeSpawner::static-encounter-choice", $"{groupKey}:{table?.AuthoredPath}");
            if (choiceIndex < 0)
                return 0;

            var group = table[choiceIndex];
            RecordEncounterObjectMirror(null, zoneName, groupKey, table, choiceIndex, role, null, -1, group, marker.Source);
            int count = 0;
            foreach (var unit in ExpandEncounterGroup(group, StableSpotSeed(groupKey)))
            {
                AddSpawn(spawns, zoneName, unit, marker.LocalX, marker.LocalY, marker.LocalZ, marker.Heading, groupKey);
                count++;
            }
            return count;
        }



        public static bool IsStaticBossZone(string zoneName)
        {
            return string.Equals(NormalizeBaseZone(zoneName), "dungeon00_level03_boss", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsProceduralZone(string zoneName)
        {
            return LevelDefs.ContainsKey(NormalizeBaseZone(zoneName));
        }

        public static List<DatabaseLoader.DungeonSpawnData> GenerateStaticBossSpawns(string zoneName, uint seed = 0xBEEFBEEF)
        {
            string baseZone = NormalizeBaseZone(zoneName);
            var spawns = new List<DatabaseLoader.DungeonSpawnData>();
            if (!IsStaticBossZone(baseZone))
                return spawns;

            var rng = new CombatRandom(seed);
            RngLedger.LogSeed("layout", "DungeonMazeSpawner::static-boss-seed", seed, baseZone);
            int regular = 0;
            for (int markerIndex = 0; markerIndex < Level03BossRegularMarkers.Length; markerIndex++)
                regular += AddStaticEncounterSpawns(spawns, baseZone, Level04Encounter, Level03BossRegularMarkers[markerIndex], $"{baseZone}:enc:{markerIndex}", rng, "static-regular");

            int leaders = AddStaticEncounterSpawns(spawns, baseZone, Level04LeaderEncounter, Level03BossLootGuardMarker, $"{baseZone}:leader:0", rng, "static-loot-guard");
            foreach (var posse in Level03BossPosse)
                AddSpawn(spawns, baseZone, posse.Unit, posse.X, posse.Y, posse.Z, posse.Heading, $"{baseZone}:boss:0");
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
            if (!TryGetMazeDimensions(zoneName, out int width, out int height, out _, out _, out _, out _, out _))
                return false;

            int count = width * height - 2;
            if (count <= 0 || count > ushort.MaxValue)
                return false;

            exploredBitCount = (ushort)count;
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

        public static bool TryResolveSpawnPoint(
            ProceduralDungeonSnapshot snapshot,
            string spawnPoint,
            out Vector3 position,
            out float heading,
            out int sourceIndex,
            out string tileType,
            out int gridX,
            out int gridY,
            out Vector3 local,
            out string source)
        {
            position = Vector3.zero;
            heading = 0f;
            sourceIndex = -1;
            tileType = "";
            gridX = 0;
            gridY = 0;
            local = Vector3.zero;
            source = "";

            if (snapshot == null || string.IsNullOrWhiteSpace(spawnPoint))
                return false;

            if (MatchesSpawnName(spawnPoint, snapshot.EntrySpawnName, snapshot.EntryLinkToSpawn))
            {
                position = snapshot.PlayerSpawn;
                heading = snapshot.PlayerHeading;
                sourceIndex = snapshot.EntrySourceIndex;
                tileType = snapshot.EntryTileType;
                gridX = snapshot.EntryGridX;
                gridY = snapshot.EntryGridY;
                local = snapshot.PlayerAnchorLocal;
                source = snapshot.PlayerAnchorSource;
                return true;
            }

            if (MatchesSpawnName(spawnPoint, snapshot.ExitSpawnName, snapshot.ExitLinkToSpawn))
            {
                position = snapshot.ExitPlayerSpawn;
                heading = snapshot.ExitPlayerHeading;
                sourceIndex = snapshot.ExitSourceIndex;
                tileType = snapshot.ExitTileType;
                gridX = snapshot.ExitGridX;
                gridY = snapshot.ExitGridY;
                local = snapshot.ExitPlayerAnchorLocal;
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

            if (!string.IsNullOrEmpty(tileType) &&
                tileType.StartsWith("elmforest_undergroundentrance_", StringComparison.OrdinalIgnoreCase))
            {
                string suffix = tileType.Substring("elmforest_undergroundentrance_".Length);
                if (playerSpawn)
                {
                    switch (suffix)
                    {
                        case "1n":
                            anchor = new AuthoredAnchor(234f, 73f, 10f, -180f, "PKG misc.Waypoint SpawnPoint");
                            return true;
                        case "1e":
                            anchor = new AuthoredAnchor(70f, 170f, 10f, 90f, "PKG misc.Waypoint SpawnPoint");
                            return true;
                        case "1s":
                            anchor = new AuthoredAnchor(165f, 305f, 10f, 0f, "PKG misc.Waypoint SpawnPoint");
                            return true;
                        case "1w":
                            anchor = new AuthoredAnchor(330f, 240f, 10f, -90f, "PKG misc.Waypoint SpawnPoint");
                            return true;
                    }
                }
                else
                {
                    switch (suffix)
                    {
                        case "1n":
                            anchor = new AuthoredAnchor(236f, 128f, 14f, 0f, "PKG misc.ZonePortal_agg");
                            return true;
                        case "1e":
                            anchor = new AuthoredAnchor(127f, 164f, 10f, 90f, "PKG misc.ZonePortal_agg");
                            return true;
                        case "1s":
                            anchor = new AuthoredAnchor(165f, 273f, 15f, 0f, "PKG misc.ZonePortal_agg");
                            return true;
                        case "1w":
                            anchor = new AuthoredAnchor(272f, 236f, 10f, 90f, "PKG misc.ZonePortal_agg");
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
                        anchor = new AuthoredAnchor(150f, 70f, 10f, 180f, source);
                        return true;
                    case "1e":
                        anchor = new AuthoredAnchor(100f, 245f, 10f, 90f, source);
                        return true;
                    case "1s":
                        anchor = new AuthoredAnchor(239f, 331f, 10f, 0f, source);
                        return true;
                    case "1w":
                        anchor = new AuthoredAnchor(323f, 161f, 10f, -90f, source);
                        return true;
                }
            }
            else
            {
                switch (portalSuffix)
                {
                    case "1n":
                        anchor = new AuthoredAnchor(160f, 147f, 30f, 0f, source);
                        return true;
                    case "1e":
                        anchor = new AuthoredAnchor(150f, 240f, 30f, -90f, source);
                        return true;
                    case "1s":
                        anchor = new AuthoredAnchor(240f, 251f, 30f, 0f, source);
                        return true;
                    case "1w":
                        anchor = new AuthoredAnchor(252f, 160f, 30f, 90f, source);
                        return true;
                }
            }

            return false;
        }

        private static bool IsInsideCell(MazeGenerator.MazeCell cell, float x, float y)
        {
            if (cell == null)
                return false;

            return x >= cell.WorldOriginX &&
                   x <= cell.WorldOriginX + MazeGenerator.TILE_SIZE &&
                   y >= cell.WorldOriginY &&
                   y <= cell.WorldOriginY + MazeGenerator.TILE_SIZE;
        }

        private static (float x, float y, bool found) FindWalkableSpotInCell(PathMap pathMap, MazeGenerator.MazeCell cell, float x, float y, float radius = 60f)
        {
            if (cell == null)
                return (x, y, false);

            if (IsInsideCell(cell, x, y) && IsOpenSpot(pathMap, x, y))
                return (x, y, true);

            float maxRadius = Mathf.Min(radius, MazeGenerator.TILE_SIZE * 0.25f);
            for (float r = 5f; r <= maxRadius; r += 5f)
            {
                for (float angle = 0; angle < 360; angle += 20)
                {
                    float rad = angle * Mathf.Deg2Rad;
                    float testX = x + Mathf.Cos(rad) * r;
                    float testY = y + Mathf.Sin(rad) * r;
                    if (IsInsideCell(cell, testX, testY) && IsOpenSpot(pathMap, testX, testY))
                        return (testX, testY, true);
                }
            }

            return (x, y, false);
        }

        private static Vector3 ResolveAuthoredAnchor(PathMap pathMap, MazeGenerator.MazeCell cell, AuthoredAnchor anchor, out bool walkable, out Vector3 rawWorld, bool snapToWalkable)
        {
            walkable = false;
            rawWorld = Vector3.zero;
            if (cell == null)
                return Vector3.zero;

            float x = cell.WorldOriginX + anchor.LocalX;
            float y = cell.WorldOriginY + anchor.LocalY;
            rawWorld = new Vector3(x, y, anchor.LocalZ);
            if (snapToWalkable)
            {
                var safe = FindWalkableSpotInCell(pathMap, cell, x, y);
                if (safe.found)
                {
                    x = safe.x;
                    y = safe.y;
                    walkable = true;
                }

                float z = pathMap?.GetHeightAt(x, y, anchor.LocalZ) ?? anchor.LocalZ;
                return new Vector3(x, y, z);
            }

            return rawWorld;
        }

        private static void ResolveSnapshotAnchors(ProceduralDungeonSnapshot snapshot, LevelDef level, PathMap pathMap, List<MazeGenerator.MazeCell> cells)
        {
            if (snapshot == null || level == null || cells == null || cells.Count == 0)
                return;

            snapshot.MazeWidth = level.MazeWidth;
            snapshot.MazeHeight = level.MazeHeight;

            var entryNode = FindPlacedRoomNode(snapshot, 0, "elmforest_hub_", "elmforest_up_");
            var exitNode = FindPlacedRoomNode(snapshot, 1, "elmforest_down_", "elmforest_undergroundentrance_");

            var entryCell = entryNode != null
                ? FindCell(cells, entryNode.GridX, entryNode.GridY)
                : FindCell(cells, level.EntryGridX, level.EntryGridY);
            var exitCell = exitNode != null
                ? FindCell(cells, exitNode.GridX, exitNode.GridY)
                : FindCell(cells, level.ExitGridX, level.ExitGridY);

            entryCell ??= cells[0];
            exitCell ??= entryCell;

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

            if (!TryGetAuthoredDungeonAnchor(snapshot.EntryTileType, true, out var playerAnchor))
                playerAnchor = new AuthoredAnchor(200f, 200f, 0f, 0f, "fallback local tile center");
            if (!TryGetAuthoredDungeonAnchor(snapshot.ExitTileType, true, out var exitPlayerAnchor))
                exitPlayerAnchor = new AuthoredAnchor(200f, 200f, 0f, 0f, "fallback local tile center");
            if (!TryGetAuthoredDungeonAnchor(snapshot.EntryTileType, false, out var entryPortalAnchor))
                entryPortalAnchor = new AuthoredAnchor(200f, 200f, 0f, 0f, "fallback local tile center");
            if (!TryGetAuthoredDungeonAnchor(snapshot.ExitTileType, false, out var exitPortalAnchor))
                exitPortalAnchor = new AuthoredAnchor(200f, 200f, 0f, 0f, "fallback local tile center");

            snapshot.PlayerAnchorLocal = new Vector3(playerAnchor.LocalX, playerAnchor.LocalY, playerAnchor.LocalZ);
            snapshot.ExitPlayerAnchorLocal = new Vector3(exitPlayerAnchor.LocalX, exitPlayerAnchor.LocalY, exitPlayerAnchor.LocalZ);
            snapshot.EntryPortalAnchorLocal = new Vector3(entryPortalAnchor.LocalX, entryPortalAnchor.LocalY, entryPortalAnchor.LocalZ);
            snapshot.ExitPortalAnchorLocal = new Vector3(exitPortalAnchor.LocalX, exitPortalAnchor.LocalY, exitPortalAnchor.LocalZ);
            snapshot.PlayerAnchorSource = playerAnchor.Source;
            snapshot.ExitPlayerAnchorSource = exitPlayerAnchor.Source;
            snapshot.EntryPortalAnchorSource = entryPortalAnchor.Source;
            snapshot.ExitPortalAnchorSource = exitPortalAnchor.Source;
            snapshot.PlayerSpawn = ResolveAuthoredAnchor(pathMap, entryCell, playerAnchor, out bool playerWalkable, out Vector3 playerRawWorld, snapToWalkable: false);
            snapshot.PlayerHeading = NormalizeHeading(playerAnchor.Heading);
            snapshot.PlayerAnchorWalkable = playerWalkable;
            snapshot.ExitPlayerSpawn = ResolveAuthoredAnchor(pathMap, exitCell, exitPlayerAnchor, out bool exitPlayerWalkable, out Vector3 exitPlayerRawWorld, snapToWalkable: false);
            snapshot.ExitPlayerHeading = NormalizeHeading(exitPlayerAnchor.Heading);
            snapshot.ExitPlayerAnchorWalkable = exitPlayerWalkable;
            snapshot.EntryPortalSpawn = ResolveAuthoredAnchor(pathMap, entryCell, entryPortalAnchor, out bool entryPortalWalkable, out Vector3 entryPortalRawWorld, snapToWalkable: false);
            snapshot.EntryPortalHeading = NormalizeHeading(entryPortalAnchor.Heading);
            snapshot.EntryPortalAnchorWalkable = entryPortalWalkable;
            snapshot.ExitPortalSpawn = ResolveAuthoredAnchor(pathMap, exitCell, exitPortalAnchor, out bool exitPortalWalkable, out Vector3 exitPortalRawWorld, snapToWalkable: false);
            snapshot.ExitPortalHeading = NormalizeHeading(exitPortalAnchor.Heading);
            snapshot.ExitPortalAnchorWalkable = exitPortalWalkable;

            snapshot.PortalSpawn = snapshot.ExitPortalSpawn;
            snapshot.PortalHeading = snapshot.ExitPortalHeading;
            snapshot.PortalAnchorLocal = snapshot.ExitPortalAnchorLocal;
            snapshot.PortalAnchorSource = snapshot.ExitPortalAnchorSource;
            snapshot.PortalAnchorWalkable = snapshot.ExitPortalAnchorWalkable;

            Debug.LogError($"[DUNGEON-TRANSFORM] role=player src={snapshot.EntrySourceIndex} tile='{snapshot.EntryTileType}' grid=({snapshot.EntryGridX},{snapshot.EntryGridY}) worldGridY={entryCell.WorldGridY} origin=({entryCell.WorldOriginX:F1},{entryCell.WorldOriginY:F1}) center=({entryCell.WorldCenterX:F1},{entryCell.WorldCenterY:F1}) local=({snapshot.PlayerAnchorLocal.x:F1},{snapshot.PlayerAnchorLocal.y:F1},{snapshot.PlayerAnchorLocal.z:F1}) rawAuthoredWorld=({playerRawWorld.x:F1},{playerRawWorld.y:F1},{playerRawWorld.z:F1}) sentWorld=({snapshot.PlayerSpawn.x:F1},{snapshot.PlayerSpawn.y:F1},{snapshot.PlayerSpawn.z:F1}) snapApplied=False walkable={snapshot.PlayerAnchorWalkable}");
            Debug.LogError($"[DUNGEON-TRANSFORM] role=exit-player src={snapshot.ExitSourceIndex} tile='{snapshot.ExitTileType}' grid=({snapshot.ExitGridX},{snapshot.ExitGridY}) worldGridY={exitCell.WorldGridY} origin=({exitCell.WorldOriginX:F1},{exitCell.WorldOriginY:F1}) center=({exitCell.WorldCenterX:F1},{exitCell.WorldCenterY:F1}) local=({snapshot.ExitPlayerAnchorLocal.x:F1},{snapshot.ExitPlayerAnchorLocal.y:F1},{snapshot.ExitPlayerAnchorLocal.z:F1}) rawAuthoredWorld=({exitPlayerRawWorld.x:F1},{exitPlayerRawWorld.y:F1},{exitPlayerRawWorld.z:F1}) sentWorld=({snapshot.ExitPlayerSpawn.x:F1},{snapshot.ExitPlayerSpawn.y:F1},{snapshot.ExitPlayerSpawn.z:F1}) snapApplied=False walkable={snapshot.ExitPlayerAnchorWalkable}");
            Debug.LogError($"[DUNGEON-TRANSFORM] role=entry-portal src={snapshot.EntrySourceIndex} tile='{snapshot.EntryTileType}' grid=({snapshot.EntryGridX},{snapshot.EntryGridY}) worldGridY={entryCell.WorldGridY} origin=({entryCell.WorldOriginX:F1},{entryCell.WorldOriginY:F1}) center=({entryCell.WorldCenterX:F1},{entryCell.WorldCenterY:F1}) local=({snapshot.EntryPortalAnchorLocal.x:F1},{snapshot.EntryPortalAnchorLocal.y:F1},{snapshot.EntryPortalAnchorLocal.z:F1}) rawAuthoredWorld=({entryPortalRawWorld.x:F1},{entryPortalRawWorld.y:F1},{entryPortalRawWorld.z:F1}) sentWorld=({snapshot.EntryPortalSpawn.x:F1},{snapshot.EntryPortalSpawn.y:F1},{snapshot.EntryPortalSpawn.z:F1}) snapApplied=False walkable={snapshot.EntryPortalAnchorWalkable}");
            Debug.LogError($"[DUNGEON-TRANSFORM] role=exit-portal src={snapshot.ExitSourceIndex} tile='{snapshot.ExitTileType}' grid=({snapshot.ExitGridX},{snapshot.ExitGridY}) worldGridY={exitCell.WorldGridY} origin=({exitCell.WorldOriginX:F1},{exitCell.WorldOriginY:F1}) center=({exitCell.WorldCenterX:F1},{exitCell.WorldCenterY:F1}) local=({snapshot.ExitPortalAnchorLocal.x:F1},{snapshot.ExitPortalAnchorLocal.y:F1},{snapshot.ExitPortalAnchorLocal.z:F1}) rawAuthoredWorld=({exitPortalRawWorld.x:F1},{exitPortalRawWorld.y:F1},{exitPortalRawWorld.z:F1}) sentWorld=({snapshot.ExitPortalSpawn.x:F1},{snapshot.ExitPortalSpawn.y:F1},{snapshot.ExitPortalSpawn.z:F1}) snapApplied=False walkable={snapshot.ExitPortalAnchorWalkable}");
            Debug.LogError($"[DUNGEON-SNAPSHOT] anchor player role=entry src={snapshot.EntrySourceIndex} spawnName={snapshot.EntrySpawnName} linkToSpawn={snapshot.EntryLinkToSpawn} linkToZone={snapshot.EntryLinkToZone} tile='{snapshot.EntryTileType}' grid=({snapshot.EntryGridX},{snapshot.EntryGridY}) local=({snapshot.PlayerAnchorLocal.x:F1},{snapshot.PlayerAnchorLocal.y:F1},{snapshot.PlayerAnchorLocal.z:F1}) rawAuthoredWorld=({playerRawWorld.x:F1},{playerRawWorld.y:F1},{playerRawWorld.z:F1}) sentWorld=({snapshot.PlayerSpawn.x:F1},{snapshot.PlayerSpawn.y:F1},{snapshot.PlayerSpawn.z:F1}) heading={snapshot.PlayerHeading:F1} snapApplied=False source='{snapshot.PlayerAnchorSource}'");
            Debug.LogError($"[DUNGEON-SNAPSHOT] anchor player role=exit src={snapshot.ExitSourceIndex} spawnName={snapshot.ExitSpawnName} linkToSpawn={snapshot.ExitLinkToSpawn} linkToZone={snapshot.ExitLinkToZone} tile='{snapshot.ExitTileType}' grid=({snapshot.ExitGridX},{snapshot.ExitGridY}) local=({snapshot.ExitPlayerAnchorLocal.x:F1},{snapshot.ExitPlayerAnchorLocal.y:F1},{snapshot.ExitPlayerAnchorLocal.z:F1}) rawAuthoredWorld=({exitPlayerRawWorld.x:F1},{exitPlayerRawWorld.y:F1},{exitPlayerRawWorld.z:F1}) sentWorld=({snapshot.ExitPlayerSpawn.x:F1},{snapshot.ExitPlayerSpawn.y:F1},{snapshot.ExitPlayerSpawn.z:F1}) heading={snapshot.ExitPlayerHeading:F1} snapApplied=False source='{snapshot.ExitPlayerAnchorSource}'");
            Debug.LogError($"[DUNGEON-SNAPSHOT] anchor portal role=entry src={snapshot.EntrySourceIndex} gc={snapshot.EntryPortalGcType} target={snapshot.EntryLinkToZone} spawnPoint={snapshot.EntryLinkToSpawn} tile='{snapshot.EntryTileType}' grid=({snapshot.EntryGridX},{snapshot.EntryGridY}) local=({snapshot.EntryPortalAnchorLocal.x:F1},{snapshot.EntryPortalAnchorLocal.y:F1},{snapshot.EntryPortalAnchorLocal.z:F1}) rawAuthoredWorld=({entryPortalRawWorld.x:F1},{entryPortalRawWorld.y:F1},{entryPortalRawWorld.z:F1}) sentWorld=({snapshot.EntryPortalSpawn.x:F1},{snapshot.EntryPortalSpawn.y:F1},{snapshot.EntryPortalSpawn.z:F1}) heading={snapshot.EntryPortalHeading:F1} snapApplied=False source='{snapshot.EntryPortalAnchorSource}'");
            Debug.LogError($"[DUNGEON-SNAPSHOT] anchor portal role=exit src={snapshot.ExitSourceIndex} gc={snapshot.ExitPortalGcType} target={snapshot.ExitLinkToZone} spawnPoint={snapshot.ExitLinkToSpawn} tile='{snapshot.ExitTileType}' grid=({snapshot.ExitGridX},{snapshot.ExitGridY}) local=({snapshot.ExitPortalAnchorLocal.x:F1},{snapshot.ExitPortalAnchorLocal.y:F1},{snapshot.ExitPortalAnchorLocal.z:F1}) rawAuthoredWorld=({exitPortalRawWorld.x:F1},{exitPortalRawWorld.y:F1},{exitPortalRawWorld.z:F1}) sentWorld=({snapshot.ExitPortalSpawn.x:F1},{snapshot.ExitPortalSpawn.y:F1},{snapshot.ExitPortalSpawn.z:F1}) heading={snapshot.ExitPortalHeading:F1} snapApplied=False source='{snapshot.ExitPortalAnchorSource}'");
        }

        private static List<MazeGenerator.MazeCell> SelectLeaderCells(List<MazeGenerator.MazeCell> cells, LevelDef level)
        {
            var selected = new List<MazeGenerator.MazeCell>();
            if (level.LeaderEncounterTable == null || level.LeaderGridYs == null)
                return selected;

            var used = new HashSet<string>();
            foreach (int leaderY in level.LeaderGridYs)
            {
                MazeGenerator.MazeCell bestCell = null;
                int bestDist = int.MaxValue;

                foreach (var cell in cells)
                {
                    if (cell.GridY != leaderY)
                        continue;

                    string key = CellKey(cell);
                    if (used.Contains(key))
                        continue;

                    int dist = Math.Abs(cell.GridX - level.MazeWidth / 2);
                    if (bestCell == null || dist < bestDist)
                    {
                        bestCell = cell;
                        bestDist = dist;
                    }
                }

                if (bestCell != null)
                {
                    selected.Add(bestCell);
                    used.Add(CellKey(bestCell));
                }
            }

            return selected;
        }

        public static List<DatabaseLoader.DungeonSpawnData> GenerateSpawns(string zoneName, uint seed = 0xBEEFBEEF)
        {
            return GenerateSnapshot(zoneName, seed).Spawns;
        }

        public static ProceduralDungeonSnapshot GenerateSnapshot(string zoneName, uint seed = 0xBEEFBEEF, uint roomSeed = 0)
        {
            string baseZone = NormalizeBaseZone(zoneName);
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
                rng
            );
            Debug.LogError($"[MAZE-SPAWNER] worldRoot=client-integer-half-grid tileSize={MazeGenerator.TILE_SIZE} pathMapCenterOverride=False");
            if (level.RoomNodes != null)
            {
                for (int nodeIndex = 0; nodeIndex < level.RoomNodes.Length; nodeIndex++)
                {
                    var node = level.RoomNodes[nodeIndex];
                    maze.AddRoomNode(node.TileSet, node.GridX, node.GridY, node.Chance, nodeIndex);
                }
            }
            var cells = maze.BuildWorld();

            Debug.LogError($"[MAZE-SPAWNER] cells={cells.Count}");

            var mazePathMap = DungeonRunners.Utilities.PathMapBuilder.Build(baseZone, cells);
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

            var fallbackLeaderCells = encounterRooms.Count == 0 ? SelectLeaderCells(cells, level) : new List<MazeGenerator.MazeCell>();
            foreach (var leaderCell in fallbackLeaderCells)
                roomCellKeys.Add(CellKey(leaderCell));

            foreach (var cell in cells)
            {
                if (roomCellKeys.Contains(CellKey(cell)))
                    continue;

                if (!TileEncounterPlaceholders.TryGetValue(cell.TileType, out var markers) || markers.Count == 0)
                    continue;

                for (int markerIndex = 0; markerIndex < markers.Count; markerIndex++)
                {
                    var marker = markers[markerIndex];
                    int groupOrdinal = encIdx;
                    string groupKey = $"{baseZone}:enc:{groupOrdinal}";
                    encIdx++;
                    int choiceIndex = NextTableIndex(rng, level.EncounterTable, "DungeonMazeSpawner::encounter-choice", $"{groupKey}:{level.EncounterTable?.AuthoredPath}:marker={markerIndex}");
                    if (choiceIndex < 0)
                        continue;

                    var group = level.EncounterTable[choiceIndex];
                    RecordEncounterObjectMirror(snapshot, baseZone, groupKey, level.EncounterTable, choiceIndex, "regular", cell, markerIndex, group, marker.Source);

                    var occupied = new List<Vector2>();
                    var expandedGroup = ExpandEncounterGroup(group, StableSpotSeed(groupKey));
                    int packCount = expandedGroup.Count;
                    for (int packIndex = 0; packIndex < packCount; packIndex++)
                    {
                        var unit = expandedGroup[packIndex];
                        {
                            Vector3 spawnPoint = ResolveEncounterSpawnPoint(cell, marker, out bool groundApplied, out string groundSource);
                            var scattered = ResolveEncounterAreaSpot(mazePathMap, spawnPoint.x, spawnPoint.y, packIndex, packCount, marker.SizeX, marker.SizeY, occupied);
                            float sx = scattered.found ? scattered.x : spawnPoint.x;
                            float sy = scattered.found ? scattered.y : spawnPoint.y;
                            occupied.Add(new Vector2(sx, sy));

                            spawns.Add(new DatabaseLoader.DungeonSpawnData
                            {
                                zoneName = baseZone,
                                gcType = unit.GcType,
                                spawnGcTypeOverride = unit.SpawnGcTypeOverride,
                                posX = sx,
                                posY = sy,
                                posZ = mazePathMap?.GetHeightAt(sx, sy, spawnPoint.z) ?? spawnPoint.z,
                                heading = NormalizeHeading(marker.Heading),
                                encounterGroupKey = groupKey,
                                encounterDifficulty = unit.Difficulty,
                                gridX = cell.GridX,
                                gridY = cell.GridY,
                                tileType = cell.TileType,
                                worldOriginX = cell.WorldOriginX,
                                worldOriginY = cell.WorldOriginY,
                                localX = marker.LocalX,
                                localY = marker.LocalY,
                                localZ = marker.LocalZ,
                                placementRole = "regular",
                                placeholderSource = $"{marker.Source};ground={groundSource}",
                                placeholderIndex = markerIndex,
                                placeholderSizeX = marker.SizeX,
                                placeholderSizeY = marker.SizeY,
                                encounterChoiceIndex = choiceIndex,
                                snapApplied = groundApplied
                            });
                            totalRegular++;
                        }
                    }
                }
            }

            Debug.LogError($"[MAZE-SPAWNER] regular={totalRegular} markers={encIdx}");

            int leaderIdx = 0;
            int totalLeaders = 0;

            var leaderRooms = new List<(MazeGenerator.MazeCell Cell, EncounterTableManifest Table, string Source)>();
            foreach (var encounterRoom in encounterRooms)
                leaderRooms.Add((encounterRoom.Cell, encounterRoom.Node.EncounterTable, $"roomNode:{encounterRoom.Placed.SourceIndex}"));
            if (leaderRooms.Count == 0 && level.LeaderEncounterTable != null && level.LeaderGridYs != null)
            {
                foreach (var fallbackCell in fallbackLeaderCells)
                    leaderRooms.Add((fallbackCell, level.LeaderEncounterTable, "fallback-gridY"));
            }

            if (leaderRooms.Count > 0)
            {
                foreach (var leaderRoom in leaderRooms)
                {
                    var bestCell = leaderRoom.Cell;
                    int leaderGroupOrdinal = leaderIdx;
                    string leaderGroupKey = $"{baseZone}:leader:{leaderGroupOrdinal}";
                    leaderIdx++;
                    int choiceIndex = NextTableIndex(rng, leaderRoom.Table, "DungeonMazeSpawner::leader-encounter-choice", $"{leaderGroupKey}:{leaderRoom.Table?.AuthoredPath}");
                    if (choiceIndex < 0)
                        continue;

                    var leaderGroup = leaderRoom.Table[choiceIndex];
                    RecordEncounterObjectMirror(snapshot, baseZone, leaderGroupKey, leaderRoom.Table, choiceIndex, "leader", bestCell, -1, leaderGroup, leaderRoom.Source);
                    Debug.LogError($"[MAZE-SPAWNER] leader source={leaderRoom.Source} group={leaderGroupKey} cell=({bestCell.GridX},{bestCell.GridY}) tile='{bestCell.TileType}'");
                    if (!TileEncounterPlaceholders.TryGetValue(bestCell.TileType, out var leaderMarkers) || leaderMarkers.Count == 0)
                        leaderMarkers = new List<EncounterMarker> { new(200f, 200f, 0f, source: "fallback tile center; package placeholder missing") };

                    int leaderMobs = 0;
                    var leaderOccupied = new List<Vector2>();
                    var expandedLeaderGroup = ExpandEncounterGroup(leaderGroup, StableSpotSeed(leaderGroupKey));
                    int packCount = expandedLeaderGroup.Count;
                    for (int packIndex = 0; packIndex < packCount; packIndex++)
                    {
                        var unit = expandedLeaderGroup[packIndex];
                        {
                            int slot = leaderMobs++;
                            int markerIndex = Math.Min(slot, leaderMarkers.Count - 1);
                            EncounterMarker marker = leaderMarkers[markerIndex];

                            Vector3 spawnPoint = ResolveEncounterSpawnPoint(bestCell, marker, out bool groundApplied, out string groundSource);
                            var leaderScattered = ResolveEncounterAreaSpot(mazePathMap, spawnPoint.x, spawnPoint.y, packIndex, packCount, marker.SizeX, marker.SizeY, leaderOccupied);
                            float lsx = leaderScattered.found ? leaderScattered.x : spawnPoint.x;
                            float lsy = leaderScattered.found ? leaderScattered.y : spawnPoint.y;
                            leaderOccupied.Add(new Vector2(lsx, lsy));
                            spawns.Add(new DatabaseLoader.DungeonSpawnData
                            {
                                zoneName = baseZone,
                                gcType = unit.GcType,
                                spawnGcTypeOverride = unit.SpawnGcTypeOverride,
                                posX = lsx,
                                posY = lsy,
                                posZ = mazePathMap?.GetHeightAt(lsx, lsy, spawnPoint.z) ?? spawnPoint.z,
                                heading = NormalizeHeading(marker.Heading),
                                encounterGroupKey = leaderGroupKey,
                                encounterDifficulty = unit.Difficulty,
                                gridX = bestCell.GridX,
                                gridY = bestCell.GridY,
                                tileType = bestCell.TileType,
                                worldOriginX = bestCell.WorldOriginX,
                                worldOriginY = bestCell.WorldOriginY,
                                localX = marker.LocalX,
                                localY = marker.LocalY,
                                localZ = marker.LocalZ,
                                placementRole = "leader",
                                placeholderSource = $"{marker.Source};ground={groundSource}",
                                placeholderIndex = markerIndex,
                                placeholderSizeX = marker.SizeX,
                                placeholderSizeY = marker.SizeY,
                                encounterChoiceIndex = choiceIndex,
                                snapApplied = groundApplied
                            });
                            totalLeaders++;
                        }
                    }
                }
            }

            snapshot.Cells = new List<MazeGenerator.MazeCell>(cells);
            snapshot.RoomNodes = new List<MazeGenerator.PlacedRoomNode>(maze.PlacedRoomNodes);
            ResolveSnapshotAnchors(snapshot, level, mazePathMap, cells);
            Debug.LogError($"[MAZE-SPAWNER] total={spawns.Count} regular={totalRegular} leaders={totalLeaders} zone={baseZone}");
            Debug.LogError($"[DUNGEON-SNAPSHOT] zone={baseZone} layoutSeed=0x{snapshot.LayoutSeed:X8} roomSeed=0x{snapshot.RoomSeed:X8} cells={snapshot.Cells.Count} roomNodes={snapshot.RoomNodes.Count} spawns={snapshot.Spawns.Count} entry=({snapshot.EntryGridX},{snapshot.EntryGridY}) entryTile='{snapshot.EntryTileType}' player=({snapshot.PlayerSpawn.x:F1},{snapshot.PlayerSpawn.y:F1},{snapshot.PlayerSpawn.z:F1}) entryPortal=({snapshot.EntryPortalSpawn.x:F1},{snapshot.EntryPortalSpawn.y:F1},{snapshot.EntryPortalSpawn.z:F1}) exit=({snapshot.ExitGridX},{snapshot.ExitGridY}) exitTile='{snapshot.ExitTileType}' exitPortal=({snapshot.ExitPortalSpawn.x:F1},{snapshot.ExitPortalSpawn.y:F1},{snapshot.ExitPortalSpawn.z:F1}) yTransform=worldGridY=gridY/BuildWorld");
            return snapshot;
        }
    }
}
