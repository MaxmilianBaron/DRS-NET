using System;
using System.Collections.Generic;
using DungeonRunners.Core;
using DungeonRunners.Gameplay;
using DungeonRunners.Engine;

namespace DungeonRunners.Utilities
{
    public static class PathMapBuilder
    {
        public const float NodeResolution = 10f;

        public const float MazeTileSize = 400f;

        public const int WallHeightThreshold = 30;

        public const float HeightConnectThreshold = 0xa00;

        private static readonly (int a0, int b0, int a1, int b1)[] DiagonalCornerEdges =
        {
            default, (2, 4, 0, 6), default, (4, 6, 2, 0),
            default, (6, 0, 4, 2), default, (0, 2, 6, 4),
        };

        public static PathMap Build(string zoneName, IReadOnlyList<MazeGenerator.MazeCell> cells)
        {
            if (cells == null || cells.Count == 0)
            {
                Debug.LogError($"[PATHMAP-BUILD] zone='{zoneName}' skip=emptyCellList");
                return null;
            }

            ComputeWorldBounds(cells, out float minX, out float maxX, out float minY, out float maxY);
            var pathMap = PathMap.CreateEmpty(zoneName, minX, maxX, minY, maxY);

            float baseGroundHeight = ResolveBaseGroundHeight(zoneName);

            int gridW = (int)Math.Ceiling((maxX - minX) / NodeResolution) + 1;
            int gridH = (int)Math.Ceiling((maxY - minY) / NodeResolution) + 1;
            var blocked = new bool[gridW, gridH];
            var heightGrid = new float[gridW, gridH];
            var inMazeFootprint = new bool[gridW, gridH];

            int tilesProcessed = 0;
            int tilesSkippedNoFile = 0;
            int placementsProcessed = 0;
            int placementsSkippedNoCobj = 0;
            int blockedCellsTotal = 0;
            List<string> missingTileTypes = null;

            foreach (var cell in cells)
            {
                MarkMazeFootprint(cell, inMazeFootprint, gridW, gridH, minX, minY);

                string tilePath = TileCobjResolver.ResolveTilePath(cell.TileType);
                if (tilePath == null)
                {
                    tilesSkippedNoFile++;
                    (missingTileTypes ??= new List<string>()).Add(cell.TileType);
                    continue;
                }

                TileLayout layout;
                try { layout = TileLayoutLoader.Load(tilePath); }
                catch (Exception e)
                {
                    Debug.LogError($"[PATHMAP-BUILD] tileParse state=failed tile={cell.TileType} message='{e.Message}'");
                    continue;
                }

                tilesProcessed++;
                foreach (var placement in layout.Placements)
                {
                    string cobjPath = TileCobjResolver.ResolveCobjPath(placement.ExtendsPath);
                    if (cobjPath == null) { placementsSkippedNoCobj++; continue; }

                    CobjData cobj;
                    try { cobj = CobjParser.ParseFile(cobjPath); }
                    catch (Exception)
                    {
                        placementsSkippedNoCobj++;
                        continue;
                    }

                    placementsProcessed++;
                    int blockedHere = ApplyCobjToBlockedGrid(
                        cobj, placement, cell,
                        blocked, heightGrid, gridW, gridH, minX, minY);
                    blockedCellsTotal += blockedHere;
                }
            }

            bool Walk(int gx, int gy)
            {
                return gx >= 0 && gx < gridW && gy >= 0 && gy < gridH
                    && inMazeFootprint[gx, gy] && !blocked[gx, gy];
            }

            int walkable = 0;
            int blockedCount = 0;

            bool HeightConnected(int ax, int ay, int bx, int by)
                => Mathf.Abs(heightGrid[ax, ay] - heightGrid[bx, by]) <= HeightConnectThreshold;

            var cardinalFlags = new byte[gridW, gridH];
            for (int gx = 0; gx < gridW; gx++)
            {
                for (int gy = 0; gy < gridH; gy++)
                {
                    if (!inMazeFootprint[gx, gy] || blocked[gx, gy]) continue;
                    byte cardinal = 0;
                    for (int directionIndex = 0; directionIndex < 8; directionIndex += 2)
                    {
                        var (directionX, directionY) = PathMap.Directions[directionIndex];
                        int nx = gx + directionX, ny = gy + directionY;
                        if (Walk(nx, ny) && HeightConnected(gx, gy, nx, ny))
                            cardinal |= (byte)(1 << directionIndex);
                    }
                    cardinalFlags[gx, gy] = cardinal;
                }
            }

            for (int gx = 0; gx < gridW; gx++)
            {
                for (int gy = 0; gy < gridH; gy++)
                {
                    if (!inMazeFootprint[gx, gy]) continue;

                    bool isBlocked = blocked[gx, gy];
                    byte flags = isBlocked ? (byte)0 : cardinalFlags[gx, gy];
                    if (!isBlocked)
                    {
                        for (int directionIndex = 1; directionIndex < 8; directionIndex += 2)
                        {
                            var (directionX, directionY) = PathMap.Directions[directionIndex];
                            int nx = gx + directionX, ny = gy + directionY;
                            if (!Walk(nx, ny) || !HeightConnected(gx, gy, nx, ny)) continue;
                            var corner = DiagonalCornerEdges[directionIndex];
                            bool path0 = (cardinalFlags[gx, gy] & (1 << corner.a0)) != 0
                                && (cardinalFlags[nx, ny] & (1 << corner.b0)) != 0;
                            bool path1 = (cardinalFlags[gx, gy] & (1 << corner.a1)) != 0
                                && (cardinalFlags[nx, ny] & (1 << corner.b1)) != 0;
                            if (path0 || path1)
                                flags |= (byte)(1 << directionIndex);
                        }
                    }

                    var node = new PathNode
                    {
                        GridX = gx,
                        GridY = gy,
                        WorldX = minX + gx * NodeResolution,
                        WorldY = minY + gy * NodeResolution,
                        Height = heightGrid[gx, gy] != 0f ? heightGrid[gx, gy] : baseGroundHeight,
                        ConnectionFlags = flags,
                        SolidFlag = isBlocked ? (byte)0xFE : (byte)0x00,
                    };
                    pathMap.SetNode(node);
                    if (isBlocked) blockedCount++; else walkable++;
                }
            }

            Debug.LogError(
                $"[PATHMAP-BUILD] zone='{zoneName}' cells={cells.Count} tiles={tilesProcessed}/{cells.Count} ({tilesSkippedNoFile} no-file) " +
                $"placements={placementsProcessed} ({placementsSkippedNoCobj} no-cobj) " +
                $"nodes={pathMap.NodeCount} (walkable={walkable} blocked={blockedCount}) " +
                $"bounds=({minX:F0},{minY:F0})->({maxX:F0},{maxY:F0}) blockedCobjCells={blockedCellsTotal}");

            if (missingTileTypes != null && missingTileTypes.Count > 0)
                Debug.LogError($"[PATHMAP-BUILD] missing tile files for zone='{zoneName}': {string.Join(", ", missingTileTypes)}");

            return pathMap;
        }

        private static float ResolveBaseGroundHeight(string zoneName)
        {
            string baseZone = zoneName;
            int instIdx = baseZone.IndexOf("_inst", StringComparison.OrdinalIgnoreCase);
            if (instIdx > 0) baseZone = baseZone.Substring(0, instIdx);
            try
            {
                using (var connection = DungeonRunners.Database.GameDatabase.GetConnection())
                using (var reader = DungeonRunners.Database.GameDatabase.ExecuteReader(connection,
                    "SELECT h, COUNT(*) c FROM pathmap_nodes WHERE zone_name = @z AND s < 254 GROUP BY h ORDER BY c DESC LIMIT 1",
                    ("@z", baseZone)))
                {
                    if (reader.Read() && !reader.IsDBNull(0))
                        return (float)reader.GetDouble(0);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[PATHMAP-BUILD] baseGroundHeight state=failed zone='{baseZone}' message='{e.Message}'");
            }
            return 0f;
        }

        private static void ComputeWorldBounds(
            IReadOnlyList<MazeGenerator.MazeCell> cells,
            out float minX, out float maxX, out float minY, out float maxY)
        {
            minX = float.PositiveInfinity; minY = float.PositiveInfinity;
            maxX = float.NegativeInfinity; maxY = float.NegativeInfinity;
            foreach (var cell in cells)
            {
                if (cell.WorldOriginX < minX) minX = cell.WorldOriginX;
                if (cell.WorldOriginY < minY) minY = cell.WorldOriginY;
                float maxCellX = cell.WorldOriginX + MazeTileSize;
                float maxCellY = cell.WorldOriginY + MazeTileSize;
                if (maxCellX > maxX) maxX = maxCellX;
                if (maxCellY > maxY) maxY = maxCellY;
            }
        }

        private static void MarkMazeFootprint(
            MazeGenerator.MazeCell cell, bool[,] inMaze,
            int gridW, int gridH, float minX, float minY)
        {
            int gx0 = (int)Math.Floor((cell.WorldOriginX - minX) / NodeResolution);
            int gy0 = (int)Math.Floor((cell.WorldOriginY - minY) / NodeResolution);
            int gx1 = (int)Math.Ceiling((cell.WorldOriginX + MazeTileSize - minX) / NodeResolution);
            int gy1 = (int)Math.Ceiling((cell.WorldOriginY + MazeTileSize - minY) / NodeResolution);
            for (int gx = Math.Max(0, gx0); gx < Math.Min(gridW, gx1); gx++)
            {
                for (int gy = Math.Max(0, gy0); gy < Math.Min(gridH, gy1); gy++)
                {
                    inMaze[gx, gy] = true;
                }
            }
        }

        private static int ApplyCobjToBlockedGrid(
            CobjData cobj, TilePlacement placement, MazeGenerator.MazeCell cell,
            bool[,] blocked, float[,] heightGrid, int gridW, int gridH, float minX, float minY)
        {
            int degT = Mathf.RoundToInt(placement.Heading);
            float cosT = DungeonRunners.Combat.UnitMover.ZRotateCos(degT);
            float sinT = DungeonRunners.Combat.UnitMover.ZRotateSin(degT);

            int blockedHere = 0;
            if (cobj.Width1 > 0 && cobj.Height1 > 0)
            {
                int cs = cobj.CellSize1;
                for (int cy = 0; cy < cobj.Height1; cy++)
                {
                    for (int cx = 0; cx < cobj.Width1; cx++)
                    {
                        ushort h = cobj.Heightmap[cy * cobj.Width1 + cx];

                        float lx = cobj.OriginX1 + (cx + 0.5f) * cs;
                        float ly = cobj.OriginY1 + (cy + 0.5f) * cs;

                        float rx = lx * cosT - ly * sinT;
                        float ry = lx * sinT + ly * cosT;

                        float wx = cell.WorldOriginX + placement.X + rx;
                        float wy = cell.WorldOriginY + placement.Y + ry;

                        int gxNode = (int)Math.Round((wx - minX) / NodeResolution);
                        int gyNode = (int)Math.Round((wy - minY) / NodeResolution);
                        if (gxNode < 0 || gxNode >= gridW || gyNode < 0 || gyNode >= gridH) continue;

                        if (h <= WallHeightThreshold)
                        {
                            heightGrid[gxNode, gyNode] = Mathf.Max(heightGrid[gxNode, gyNode], placement.Z + h);
                            continue;
                        }

                        if (!blocked[gxNode, gyNode])
                        {
                            blocked[gxNode, gyNode] = true;
                            blockedHere++;
                        }
                    }
                }
            }

            if (cobj.Width2 > 0 && cobj.Height2 > 0 && cobj.Cells != null)
            {
                int cs2 = cobj.CellSize2;
                for (int cy = 0; cy < cobj.Height2; cy++)
                {
                    for (int cx = 0; cx < cobj.Width2; cx++)
                    {
                        var bboxCell = cobj.Cells[cy * cobj.Width2 + cx];
                        if (bboxCell == null || bboxCell.BBoxes.Length == 0) continue;
                        bool occupiesWalkBand = false;
                        foreach (var box in bboxCell.BBoxes)
                        {
                            if (cobj.OriginZ2 + box.ZLow <= 40 && cobj.OriginZ2 + box.ZHigh >= 5) { occupiesWalkBand = true; break; }
                        }
                        if (!occupiesWalkBand) continue;

                        float lx = cobj.OriginX2 + (cx + 0.5f) * cs2;
                        float ly = cobj.OriginY2 + (cy + 0.5f) * cs2;

                        float rx = lx * cosT - ly * sinT;
                        float ry = lx * sinT + ly * cosT;

                        float wx = cell.WorldOriginX + placement.X + rx;
                        float wy = cell.WorldOriginY + placement.Y + ry;

                        int gxNode = (int)Math.Round((wx - minX) / NodeResolution);
                        int gyNode = (int)Math.Round((wy - minY) / NodeResolution);
                        if (gxNode < 0 || gxNode >= gridW || gyNode < 0 || gyNode >= gridH) continue;

                        if (!blocked[gxNode, gyNode])
                        {
                            blocked[gxNode, gyNode] = true;
                            blockedHere++;
                        }
                    }
                }
            }
            return blockedHere;
        }
    }
}
