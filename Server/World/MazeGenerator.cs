using System;
using System.Collections.Generic;
using DungeonRunners.Engine;
using DungeonRunners.Combat;
using CombatRandom = DungeonRunners.Combat.MersenneTwister;

namespace DungeonRunners.Gameplay
{
    public class MazeGenerator
    {
        public const int TILE_SIZE = 400;

        public const int NORTH = 0;
        public const int EAST = 1;
        public const int SOUTH = 2;
        public const int WEST = 3;

        private static readonly int[] DX = { 0, 1, 0, -1 };
        private static readonly int[] DY = { 1, 0, -1, 0 };
        private static readonly int[] OPPOSITE = { SOUTH, WEST, NORTH, EAST };

        private const byte DIR_NORTH = 0x01;
        private const byte DIR_SOUTH = 0x02;
        private const byte DIR_EAST = 0x04;
        private const byte DIR_WEST = 0x08;
        private const byte CELL_ROOM = 0x10;

        private static readonly byte[] OrderedDirs = { DIR_NORTH, DIR_EAST, DIR_SOUTH, DIR_WEST };
        private static readonly byte[] EmptyNeighborDirs = { DIR_NORTH, DIR_SOUTH, DIR_WEST, DIR_EAST };

        private static readonly Dictionary<string, string[]> RoomTileVariants =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "elmforest_hub_", new[] { "elmforest_hub_1e", "elmforest_hub_1n", "elmforest_hub_1s", "elmforest_hub_1w" } },
                { "elmforest_down_", new[] { "elmforest_down_1e", "elmforest_down_1n", "elmforest_down_1s", "elmforest_down_1w" } },
                { "elmforest_questfindring_", new[] { "elmforest_questfindring_1n1e1w", "elmforest_questfindring_1w" } },
                { "elmforest_up_", new[] { "elmforest_up_1e", "elmforest_up_1n", "elmforest_up_1s", "elmforest_up_1w" } },
                { "elmforest_undergroundentrance_", new[] { "elmforest_undergroundentrance_1e", "elmforest_undergroundentrance_1n", "elmforest_undergroundentrance_1s", "elmforest_undergroundentrance_1w" } },
                { "tutorial_loot_", new[] { "tutorial_loot_1e", "tutorial_loot_1n", "tutorial_loot_1s", "tutorial_loot_1w" } },
            };

        public int Width { get; private set; }
        public int Height { get; private set; }
        public uint Seed { get; private set; }
        public int Randomness { get; private set; }
        public int Sparseness { get; private set; }
        public int DeadEndRemovalChance { get; private set; }

        private HashSet<int>[][] _openings;
        private CombatRandom _rng;
        private byte[,] _cells;
        private bool[,] _occupied;
        private byte[,] _forcedExits;
        private string[,] _roomTileTypes;
        private readonly List<RoomNodeSpec> _roomNodes = new List<RoomNodeSpec>();
        private readonly List<PlacedRoomNode> _placedRoomNodes = new List<PlacedRoomNode>();
        public IReadOnlyList<PlacedRoomNode> PlacedRoomNodes => _placedRoomNodes;

        public class RoomNodeSpec
        {
            public string TileSet;
            public int? GridX;
            public int? GridY;
            public int Chance = 100;
            public int SourceIndex = -1;
        }

        public class PlacedRoomNode
        {
            public int SourceIndex;
            public string TileSet;
            public string TileType;
            public int GridX;
            public int GridY;
        }

        public class MazeCell
        {
            public int GridX;
            public int GridY;
            public int WorldGridY;
            public string Connections;
            public string TileType;
            public float WorldOriginX;
            public float WorldOriginY;
            public float WorldCenterX;
            public float WorldCenterY;
            public bool HasNorth => Connections.Contains("1n");
            public bool HasEast => Connections.Contains("1e");
            public bool HasSouth => Connections.Contains("1s");
            public bool HasWest => Connections.Contains("1w");
        }

        public MazeGenerator(int width, int height, uint seed,
                             int randomness = 90, int sparseness = 5,
                             int deadEndRemovalChance = 100,
                             CombatRandom rng = null)
        {
            Width = width;
            Height = height;
            Seed = seed;
            Randomness = randomness;
            Sparseness = sparseness;
            DeadEndRemovalChance = deadEndRemovalChance;
            _rng = rng ?? new CombatRandom(seed);

            _openings = new HashSet<int>[height][];
            _cells = new byte[height, width];
            _occupied = new bool[height, width];
            _forcedExits = new byte[height, width];
            _roomTileTypes = new string[height, width];
            for (int y = 0; y < height; y++)
            {
                _openings[y] = new HashSet<int>[width];
                for (int x = 0; x < width; x++)
                    _openings[y][x] = new HashSet<int>();
            }
        }

        private bool InBounds(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        private void Connect(int x1, int y1, int dir)
        {
            int x2 = x1 + DX[dir];
            int y2 = y1 + DY[dir];
            if (InBounds(x2, y2))
            {
                _openings[y1][x1].Add(dir);
                _openings[y2][x2].Add(OPPOSITE[dir]);
            }
        }

        public float CenterOverrideX = float.NaN;
        public float CenterOverrideY = float.NaN;

        public void AddRoomNode(string tileSet, int? gridX = null, int? gridY = null, int chance = 100, int sourceIndex = -1)
        {
            if (string.IsNullOrEmpty(tileSet))
                return;

            _roomNodes.Add(new RoomNodeSpec
            {
                TileSet = tileSet,
                GridX = gridX,
                GridY = gridY,
                Chance = chance,
                SourceIndex = sourceIndex
            });
        }

        public List<MazeCell> BuildWorld(string tileSetPrefix = "elmforest_tileset_")
        {
            PlaceRoomNodes();
            GenerateCooridors();
            ApplyForcedRoomExits();
            Sparsify();
            RemoveDeadEnds();
            ApplyOpeningsFromBits();
            return BuildResult(tileSetPrefix);
        }

        private void PlaceRoomNodes()
        {
            _placedRoomNodes.Clear();

            foreach (var spec in _roomNodes)
            {
                if (Roll100() > spec.Chance)
                    continue;

                var variants = GetTileVariants(spec.TileSet);
                while (variants.Count > 0)
                {
                    int variantIndex = NextInt(0, variants.Count);
                    string tileType = variants[variantIndex];
                    variants.RemoveAt(variantIndex);

                    byte exits = ParseExitBits(spec.TileSet, tileType);
                    var candidates = GetRoomCandidates(spec, exits);
                    if (candidates.Count == 0)
                        continue;

                    var cell = candidates[NextInt(0, candidates.Count)];
                    _occupied[cell.y, cell.x] = true;
                    _forcedExits[cell.y, cell.x] = exits;
                    _roomTileTypes[cell.y, cell.x] = tileType;
                    _placedRoomNodes.Add(new PlacedRoomNode
                    {
                        SourceIndex = spec.SourceIndex,
                        TileSet = spec.TileSet,
                        TileType = tileType,
                        GridX = cell.x,
                        GridY = cell.y
                    });
                    Debug.LogError($"[MAZE-GENERATOR] roomNode src={spec.SourceIndex} tileSet='{spec.TileSet}' tile='{tileType}' grid=({cell.x},{cell.y})");
                    break;
                }
            }
        }

        private void GenerateCooridors()
        {
            int emptyRemaining = Width * Height - 1;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (_occupied[y, x])
                        emptyRemaining--;
                }
            }
            if (emptyRemaining < 0)
                emptyRemaining = 0;

            var current = GetRandomEmptyCell();
            bool hasCurrent = current.HasValue;
            bool useCorridorFallback = false;
            byte lastDir = 0;
            int straightStreak = 0;

            while (emptyRemaining > 0)
            {
                if (!hasCurrent)
                {
                    current = useCorridorFallback ? GetRandomCorridorCell() : GetRandomEmptyCell();
                    useCorridorFallback = false;
                    if (!current.HasValue)
                        break;
                    hasCurrent = true;
                }

                int x = current.Value.x;
                int y = current.Value.y;
                byte currentBits = CellByte(x, y);
                byte blocked = BlockedDirections(x, y, currentBits);
                byte chosenDir = 0;

                if ((currentBits & CELL_ROOM) == 0)
                {
                    if (Roll100() > Randomness && CanContinueDir(x, y, lastDir, straightStreak))
                    {
                        chosenDir = lastDir;
                        straightStreak++;
                    }
                    else
                    {
                        straightStreak = 0;
                    }
                }

                bool accepted = false;
                while (true)
                {
                    if (chosenDir != 0 && (blocked & chosenDir) == 0)
                    {
                        var next = Neighbor(x, y, chosenDir);
                        SetDir(x, y, chosenDir);
                        SetDir(next.x, next.y, OppositeDir(chosenDir));
                        current = next;
                        hasCurrent = true;
                        lastDir = chosenDir;
                        emptyRemaining--;
                        accepted = true;
                        break;
                    }

                    chosenDir = RandomDirection();
                    var neighbor = Neighbor(x, y, chosenDir);
                    if (!InBounds(neighbor.x, neighbor.y))
                    {
                        blocked |= chosenDir;
                    }
                    else if (CellByte(neighbor.x, neighbor.y) == 0)
                    {
                        continue;
                    }
                    else
                    {
                        blocked |= chosenDir;
                    }

                    if ((blocked & 0x0F) == 0x0F)
                        break;
                }

                if (!accepted)
                {
                    hasCurrent = false;
                    useCorridorFallback = true;
                }
            }
        }

        private void ApplyForcedRoomExits()
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    byte exits = _forcedExits[y, x];
                    if (exits == 0)
                        continue;

                    foreach (byte dir in OrderedDirs)
                    {
                        var neighbor = Neighbor(x, y, dir);
                        if (!InBounds(neighbor.x, neighbor.y))
                            continue;

                        if ((exits & dir) != 0)
                        {
                            SetDir(x, y, dir);
                            SetDir(neighbor.x, neighbor.y, OppositeDir(dir));
                        }
                        else if ((_cells[y, x] & dir) != 0)
                        {
                            UnsetDir(x, y, dir);
                            UnsetDir(neighbor.x, neighbor.y, OppositeDir(dir));
                        }
                    }
                }
            }
        }

        private void Sparsify()
        {
            if (Sparseness <= 0)
                return;

            for (int sparsenessStep = 0; sparsenessStep < Sparseness; sparsenessStep++)
            {
                var cell = GetRandomDeadEndCell();
                if (!cell.HasValue)
                    return;

                int x = cell.Value.x;
                int y = cell.Value.y;
                byte dir = _cells[y, x];
                var neighbor = Neighbor(x, y, dir);
                if (!InBounds(neighbor.x, neighbor.y) || _occupied[neighbor.y, neighbor.x])
                    continue;

                _cells[y, x] = 0;
                UnsetDir(neighbor.x, neighbor.y, OppositeDir(dir));
            }
        }

        private void RemoveDeadEnds()
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (_occupied[y, x] || !IsDeadEnd(_cells[y, x]))
                        continue;

                    if (Roll100() > DeadEndRemovalChance)
                        continue;

                    int cx = x;
                    int cy = y;
                    while (true)
                    {
                        byte currentBits = _cells[cy, cx];
                        byte blocked = 0;
                        byte chosenDir = 0;
                        (int x, int y) next = (0, 0);

                        while (chosenDir == 0)
                        {
                            byte dir = RandomDirection();
                            var candidate = Neighbor(cx, cy, dir);
                            if (!InBounds(candidate.x, candidate.y) ||
                                currentBits == dir ||
                                _occupied[candidate.y, candidate.x])
                            {
                                blocked |= dir;
                            }
                            else
                            {
                                chosenDir = dir;
                                next = candidate;
                            }

                            if ((blocked & 0x0F) == 0x0F)
                                break;
                        }

                        if (chosenDir == 0)
                            break;

                        SetDir(cx, cy, chosenDir);
                        SetDir(next.x, next.y, OppositeDir(chosenDir));
                        cx = next.x;
                        cy = next.y;
                        if (_cells[cy, cx] != OppositeDir(chosenDir))
                            break;
                    }
                }
            }
        }

        private List<string> GetTileVariants(string tileSet)
        {
            if (RoomTileVariants.TryGetValue(tileSet, out var variants))
                return new List<string>(variants);
            return new List<string> { tileSet };
        }

        private byte ParseExitBits(string tileSet, string tileType)
        {
            string suffix = tileType ?? "";
            if (!string.IsNullOrEmpty(tileSet) && suffix.StartsWith(tileSet, StringComparison.OrdinalIgnoreCase))
                suffix = suffix.Substring(tileSet.Length);

            byte bits = 0;
            if (suffix.Contains("1n")) bits |= DIR_NORTH;
            if (suffix.Contains("1s")) bits |= DIR_SOUTH;
            if (suffix.Contains("1e")) bits |= DIR_WEST;
            if (suffix.Contains("1w")) bits |= DIR_EAST;
            return bits;
        }

        private List<(int x, int y)> GetRoomCandidates(RoomNodeSpec spec, byte exits)
        {
            int minX = 0;
            int maxX = Width - 1;
            int minY = 0;
            int maxY = Height - 1;

            if (spec.GridX.HasValue)
            {
                int x = spec.GridX.Value;
                if (x < 0)
                    return new List<(int x, int y)>();
                if (x >= Width)
                    x = Width - 1;
                minX = maxX = x;
            }

            if (spec.GridY.HasValue)
            {
                int y = spec.GridY.Value;
                if (y < 0)
                    return new List<(int x, int y)>();
                if (y >= Height)
                    y = Height - 1;
                minY = maxY = y;
            }

            var candidates = new List<(int x, int y)>();
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (_occupied[y, x])
                        continue;
                    if (!ForcedExitsFit(x, y, exits))
                        continue;
                    if (!MapFullyConnectedWithCandidate(x, y, exits))
                        continue;
                    candidates.Add((x, y));
                }
            }
            return candidates;
        }

        private bool ForcedExitsFit(int x, int y, byte exits)
        {
            if (exits == 0)
                return true;

            foreach (byte dir in OrderedDirs)
            {
                if ((exits & dir) == 0)
                    continue;
                var neighbor = Neighbor(x, y, dir);
                if (!InBounds(neighbor.x, neighbor.y))
                    return false;
            }
            return true;
        }

        private bool MapFullyConnectedWithCandidate(int candidateX, int candidateY, byte candidateExits)
        {
            int total = Width * Height;
            var visited = new HashSet<int>();
            var queue = new Queue<(int x, int y)>();
            queue.Enqueue((candidateX, candidateY));
            visited.Add(CellKey(candidateX, candidateY));

            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                byte bits = CandidateCellByte(cell.x, cell.y, candidateX, candidateY, candidateExits);
                byte dirs = (byte)(bits & 0x0F);
                bool currentSolid = bits != 0;
                if (bits == 0)
                    dirs = 0x0F;

                foreach (byte dir in EmptyNeighborDirs)
                {
                    if ((dirs & dir) == 0)
                        continue;

                    var neighbor = Neighbor(cell.x, cell.y, dir);
                    if (!InBounds(neighbor.x, neighbor.y))
                    {
                        if (currentSolid)
                            return false;
                        continue;
                    }

                    byte neighborBits = CandidateCellByte(neighbor.x, neighbor.y, candidateX, candidateY, candidateExits);
                    if (neighborBits != 0 && (neighborBits & OppositeDir(dir)) == 0)
                    {
                        if (currentSolid)
                            return false;
                        continue;
                    }

                    int key = CellKey(neighbor.x, neighbor.y);
                    if (visited.Add(key))
                        queue.Enqueue(neighbor);
                }
            }

            return visited.Count == total;
        }

        private byte CandidateCellByte(int x, int y, int candidateX, int candidateY, byte candidateExits)
        {
            if (x == candidateX && y == candidateY)
                return (byte)(CELL_ROOM | (candidateExits & 0x0F));
            if (_occupied[y, x])
                return (byte)(CELL_ROOM | (_forcedExits[y, x] & 0x0F) | (_cells[y, x] & 0x0F));
            return _cells[y, x];
        }

        private (int x, int y)? GetRandomEmptyCell()
        {
            var candidates = new List<(int x, int y)>(Width * Height);
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (CellByte(x, y) == 0)
                        candidates.Add((x, y));
                }
            }
            if (candidates.Count == 0)
                return null;
            return candidates[NextInt(0, candidates.Count)];
        }

        private (int x, int y)? GetRandomCorridorCell()
        {
            var normal = new List<(int x, int y)>(Width * Height);
            var forced = new List<(int x, int y)>(_roomNodes.Count);

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    byte bits = CellByte(x, y);
                    if ((bits & 0x0F) == 0)
                        continue;

                    byte emptyMask = GetEmptyNeighborCellMask(x, y);
                    if (emptyMask == 0)
                        continue;

                    if (_occupied[y, x])
                    {
                        if ((emptyMask & bits & 0x0F) != 0)
                            forced.Add((x, y));
                        continue;
                    }

                    normal.Add((x, y));
                }
            }

            if (normal.Count > 0)
                return normal[NextInt(0, normal.Count)];
            if (forced.Count > 0)
                return forced[NextInt(0, forced.Count)];
            return null;
        }

        private (int x, int y)? GetRandomDeadEndCell()
        {
            var candidates = new List<(int x, int y)>(Width * Height);
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (!_occupied[y, x] && IsDeadEnd(_cells[y, x]))
                        candidates.Add((x, y));
                }
            }
            if (candidates.Count == 0)
                return null;
            return candidates[NextInt(0, candidates.Count)];
        }

        private byte GetEmptyNeighborCellMask(int x, int y)
        {
            byte mask = 0;
            foreach (byte dir in EmptyNeighborDirs)
            {
                var neighbor = Neighbor(x, y, dir);
                if (InBounds(neighbor.x, neighbor.y) && CellByte(neighbor.x, neighbor.y) == 0)
                    mask |= dir;
            }
            return mask;
        }

        private byte BlockedDirections(int x, int y, byte bits)
        {
            byte blocked = (byte)(bits & 0x0F);
            if ((bits & CELL_ROOM) != 0)
                blocked = (byte)((~bits) & 0x0F);

            if (y <= 0) blocked |= DIR_NORTH;
            if (y + 1 >= Height) blocked |= DIR_SOUTH;
            if (x <= 0) blocked |= DIR_WEST;
            if (x + 1 >= Width) blocked |= DIR_EAST;
            return blocked;
        }

        private bool CanContinueDir(int x, int y, byte dir, int streak)
        {
            if (dir == 0 || streak >= StraightLimit(dir))
                return false;

            var neighbor = Neighbor(x, y, dir);
            return InBounds(neighbor.x, neighbor.y) && CellByte(neighbor.x, neighbor.y) == 0;
        }

        private int StraightLimit(byte dir)
        {
            switch (dir)
            {
                case DIR_EAST:
                case DIR_WEST:
                    return Width <= 1 ? 0 : Width / 2;
                case DIR_NORTH:
                case DIR_SOUTH:
                    return Height <= 1 ? 0 : Height / 2;
                default:
                    return 0;
            }
        }

        private byte RandomDirection()
        {
            switch (GenerateLayoutRaw("MazeGenerator::RandomDirection") & 3)
            {
                case 0: return DIR_NORTH;
                case 1: return DIR_SOUTH;
                case 2: return DIR_WEST;
                default: return DIR_EAST;
            }
        }

        private int Roll100()
        {
            return (int)(GenerateLayoutRaw("MazeGenerator::Roll100") % 100) + 1;
        }

        private byte CellByte(int x, int y)
        {
            byte bits = _cells[y, x];
            if (_occupied[y, x])
                bits = (byte)(bits | _forcedExits[y, x] | CELL_ROOM);
            return bits;
        }

        private void SetDir(int x, int y, byte dir)
        {
            if (InBounds(x, y))
                _cells[y, x] |= dir;
        }

        private void UnsetDir(int x, int y, byte dir)
        {
            if (InBounds(x, y))
                _cells[y, x] = (byte)(_cells[y, x] & ~dir);
        }

        private (int x, int y) Neighbor(int x, int y, byte dir)
        {
            switch (dir)
            {
                case DIR_NORTH: return (x, y - 1);
                case DIR_SOUTH: return (x, y + 1);
                case DIR_EAST: return (x + 1, y);
                case DIR_WEST: return (x - 1, y);
                default: return (x, y);
            }
        }

        private byte OppositeDir(byte dir)
        {
            switch (dir)
            {
                case DIR_NORTH: return DIR_SOUTH;
                case DIR_SOUTH: return DIR_NORTH;
                case DIR_EAST: return DIR_WEST;
                case DIR_WEST: return DIR_EAST;
                default: return 0;
            }
        }

        private bool IsDeadEnd(byte bits)
        {
            return bits == DIR_NORTH || bits == DIR_SOUTH || bits == DIR_EAST || bits == DIR_WEST;
        }

        private int CellKey(int x, int y)
        {
            return y * Width + x;
        }

        private void ApplyOpeningsFromBits()
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    _openings[y][x].Clear();
                    byte bits = CellByte(x, y);
                    if ((bits & DIR_NORTH) != 0) _openings[y][x].Add(NORTH);
                    if ((bits & DIR_EAST) != 0) _openings[y][x].Add(EAST);
                    if ((bits & DIR_SOUTH) != 0) _openings[y][x].Add(SOUTH);
                    if ((bits & DIR_WEST) != 0) _openings[y][x].Add(WEST);
                }
            }
        }

        private List<MazeCell> BuildResult(string tileSetPrefix)
        {
            int halfGridX = Width / 2;
            int halfGridY = Height / 2;
            float clientRootX = 0f;
            float clientRootY = 0f;
            Debug.LogError($"[DUNGEON-TRANSFORM] clientRoot=({clientRootX:F1},{clientRootY:F1}) tileSize={TILE_SIZE} grid={Width}x{Height} halfGrid=({halfGridX},{halfGridY}) centerOverrideIgnored=({CenterOverrideX:F1},{CenterOverrideY:F1}) yTransform=worldGridY=gridY/BuildWorld");

            var cells = new List<MazeCell>();

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    string conns = "";
                    if (_openings[y][x].Contains(NORTH)) conns += "1n";
                    if (_openings[y][x].Contains(WEST)) conns += "1e";
                    if (_openings[y][x].Contains(SOUTH)) conns += "1s";
                    if (_openings[y][x].Contains(EAST)) conns += "1w";

                    if (string.IsNullOrEmpty(conns))
                        conns = "0n";

                    int worldGridY = y;
                    float cellOX = clientRootX + (x - halfGridX) * TILE_SIZE;
                    float cellOY = clientRootY + (worldGridY - halfGridY) * TILE_SIZE;

                    string tileType = _roomTileTypes[y, x];
                    if (string.IsNullOrEmpty(tileType))
                        tileType = $"{tileSetPrefix}{conns}_a";

                    cells.Add(new MazeCell
                    {
                        GridX = x,
                        GridY = y,
                        WorldGridY = worldGridY,
                        Connections = conns,
                        TileType = tileType,
                        WorldOriginX = cellOX,
                        WorldOriginY = cellOY,
                        WorldCenterX = cellOX + TILE_SIZE / 2f,
                        WorldCenterY = cellOY + TILE_SIZE / 2f,
                    });
                }
            }

            return cells;
        }

        public string GetConnections(int gx, int gy)
        {
            if (!InBounds(gx, gy)) return null;
            string conns = "";
            if (_openings[gy][gx].Contains(NORTH)) conns += "1n";
            if (_openings[gy][gx].Contains(WEST)) conns += "1e";
            if (_openings[gy][gx].Contains(SOUTH)) conns += "1s";
            if (_openings[gy][gx].Contains(EAST)) conns += "1w";
            return conns;
        }

        public int NextInt(int minInclusive, int maxExclusive, string phase = null, string owner = null)
        {
            if (maxExclusive <= minInclusive)
                return minInclusive;
            return (int)RngLedger.Generate(
                _rng,
                "layout",
                phase ?? "MazeGenerator::NextInt",
                (uint)minInclusive,
                (uint)(maxExclusive - 1),
                owner ?? $"seed=0x{Seed:X8}");
        }

        public float NextFloat(float minInclusive, float maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                return minInclusive;
            uint raw = GenerateLayoutRaw("MazeGenerator::NextFloat") >> 8;
            float t = raw / 16777216f;
            return minInclusive + (maxExclusive - minInclusive) * t;
        }

        private uint GenerateLayoutRaw(string phase)
        {
            return RngLedger.Generate(_rng, "layout", phase, $"seed=0x{Seed:X8}");
        }

        public void PrintMaze()
        {
            for (int y = Height - 1; y >= 0; y--)
            {
                string top = "";
                string mid = "";
                for (int x = 0; x < Width; x++)
                {
                    bool hasN = _openings[y][x].Contains(NORTH);
                    bool hasW = _openings[y][x].Contains(WEST);
                    top += "+" + (hasN ? "   " : "---");
                    mid += (hasW ? " " : "|") + $"({x},{y})";
                }
                top += "+";
                mid += "|";
                Debug.LogError(top);
                Debug.LogError(mid);
            }
            string bottom = "";
            for (int x = 0; x < Width; x++)
                bottom += "+---";
            bottom += "+";
            Debug.LogError(bottom);
        }
    }
}
