using System;
using System.Collections.Generic;
using System.IO;
using DungeonRunners.Engine;

namespace DungeonRunners.Core
{
    public enum PathReachability
    {
        CoverageMissing,
        Reachable,
        Blocked
    }

    public class PathNode
    {
        public int GridX;
        public int GridY;
        public float WorldX;
        public float WorldY;
        public float Height;
        public byte ConnectionFlags;
        public byte SolidFlag;

        public bool IsWalkable => SolidFlag < 0xFE;
    }

    public class PathMap
    {
        public string ZoneName { get; private set; }
        public float WorldOffsetX { get; private set; }
        public float WorldOffsetY { get; private set; }

        private const float TILE_SIZE = 10.0f;

        private Dictionary<(int, int), PathNode> _nodeGrid = new Dictionary<(int, int), PathNode>();

        private float _minWorldX, _maxWorldX;
        private float _minWorldY, _maxWorldY;
        public float MinWorldX => _minWorldX;
        public float MaxWorldX => _maxWorldX;
        public float MinWorldY => _minWorldY;
        public float MaxWorldY => _maxWorldY;
        public int NodeCount => _nodeGrid.Count;
        public float NodeResolution => TILE_SIZE;

        public int WalkableCount
        {
            get
            {
                int count = 0;
                foreach (var kv in _nodeGrid)
                    if (kv.Value.IsWalkable) count++;
                return count;
            }
        }

        public static PathMap CreateEmpty(string zoneName, float minX, float maxX, float minY, float maxY)
        {
            return new PathMap
            {
                ZoneName = zoneName,
                WorldOffsetX = minX,
                WorldOffsetY = minY,
                _minWorldX = minX,
                _maxWorldX = maxX,
                _minWorldY = minY,
                _maxWorldY = maxY,
            };
        }

        public void SetNode(PathNode node)
        {
            if (node == null) return;
            _nodeGrid[(node.GridX, node.GridY)] = node;
        }

        public static readonly (int dx, int dy)[] Directions =
        {
            (0, 1), (1, 1), (1, 0), (1, -1),
            (0, -1), (-1, -1), (-1, 0), (-1, 1),
        };

        public static readonly int[] DirectionCosts = { 10, 14, 10, 14, 10, 14, 10, 14 };

        public static int GetDirFromAToB(PathNode a, PathNode b)
        {
            if (a == null || b == null) return -1;
            int dxRaw = b.GridX - a.GridX;
            int dyRaw = b.GridY - a.GridY;
            int dx = dxRaw < 0 ? -1 : (dxRaw > 0 ? 1 : 0);
            int dy = dyRaw < 0 ? -1 : (dyRaw > 0 ? 1 : 0);
            for (int directionIndex = 0; directionIndex < Directions.Length; directionIndex++)
            {
                if (Directions[directionIndex].dx == dx && Directions[directionIndex].dy == dy)
                    return directionIndex;
            }
            return -1;
        }

        public static PathMap LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                DungeonRunners.Engine.Debug.LogError($"[PATHMAP] path='{filePath}' reason=file-missing");
                return null;
            }

            string json = File.ReadAllText(filePath);
            return LoadFromJson(json);
        }

        public static PathMap LoadFromSQLite(string zoneName)
        {
            var pathMap = new PathMap();
            try
            {
                using (var connection = DungeonRunners.Database.GameDatabase.GetConnection())
                {
                    using (var zoneReader = DungeonRunners.Database.GameDatabase.ExecuteReader(connection,
                        "SELECT * FROM pathmap_zones WHERE zone_name = @z", ("@z", zoneName)))
                    {
                        if (!zoneReader.Read())
                        {
                            DungeonRunners.Engine.Debug.LogWarning($"[PATHMAP] zone='{zoneName}' reason=missing");
                            return null;
                        }
                        pathMap.ZoneName = zoneName;
                        pathMap.WorldOffsetX = (float)DungeonRunners.Database.GameDatabase.GetFloat(zoneReader, "world_offset_x");
                        pathMap.WorldOffsetY = (float)DungeonRunners.Database.GameDatabase.GetFloat(zoneReader, "world_offset_y");
                        pathMap._minWorldX = (float)DungeonRunners.Database.GameDatabase.GetFloat(zoneReader, "world_min_x");
                        pathMap._maxWorldX = (float)DungeonRunners.Database.GameDatabase.GetFloat(zoneReader, "world_max_x");
                        pathMap._minWorldY = (float)DungeonRunners.Database.GameDatabase.GetFloat(zoneReader, "world_min_y");
                        pathMap._maxWorldY = (float)DungeonRunners.Database.GameDatabase.GetFloat(zoneReader, "world_max_y");
                    }

                    using (var nodeReader = DungeonRunners.Database.GameDatabase.ExecuteReader(connection,
                        "SELECT gx, gy, wx, wy, h, c, s FROM pathmap_nodes WHERE zone_name = @z", ("@z", zoneName)))
                    {
                        while (nodeReader.Read())
                        {
                            var node = new PathNode
                            {
                                GridX = nodeReader.GetInt32(0),
                                GridY = nodeReader.GetInt32(1),
                                WorldX = (float)nodeReader.GetDouble(2),
                                WorldY = (float)nodeReader.GetDouble(3),
                                Height = (float)nodeReader.GetDouble(4),
                                ConnectionFlags = (byte)nodeReader.GetInt32(5),
                                SolidFlag = (byte)nodeReader.GetInt32(6)
                            };
                            pathMap._nodeGrid[(node.GridX, node.GridY)] = node;
                        }
                    }
                }
                DungeonRunners.Engine.Debug.Log($"[PATHMAP] loaded zone='{zoneName}' source=sqlite nodes={pathMap._nodeGrid.Count}");
                return pathMap;
            }
            catch (Exception ex)
            {
                DungeonRunners.Engine.Debug.LogError($"[PATHMAP] zone='{zoneName}' source=sqlite error={ex.Message}");
                return null;
            }
        }

        public static PathMap LoadFromJson(string json)
        {
            var pathMap = new PathMap();

            try
            {
                pathMap.ZoneName = ExtractString(json, "zoneName");
                pathMap.WorldOffsetX = ExtractFloat(json, "worldOffsetX");
                pathMap.WorldOffsetY = ExtractFloat(json, "worldOffsetY");

                int boundsStart = json.IndexOf("\"worldBounds\"");
                if (boundsStart >= 0)
                {
                    int braceStart = json.IndexOf("{", boundsStart);
                    int braceEnd = json.IndexOf("}", braceStart);
                    string boundsJson = json.Substring(braceStart, braceEnd - braceStart + 1);
                    pathMap._minWorldX = ExtractFloat(boundsJson, "minX");
                    pathMap._maxWorldX = ExtractFloat(boundsJson, "maxX");
                    pathMap._minWorldY = ExtractFloat(boundsJson, "minY");
                    pathMap._maxWorldY = ExtractFloat(boundsJson, "maxY");
                }

                int nodesStart = json.IndexOf("\"nodes\"");
                if (nodesStart >= 0)
                {
                    int arrayStart = json.IndexOf("[", nodesStart);
                    int arrayEnd = json.LastIndexOf("]");
                    string nodesJson = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);

                    int pos = 0;
                    while (pos < nodesJson.Length)
                    {
                        int objStart = nodesJson.IndexOf("{", pos);
                        if (objStart < 0) break;

                        int objEnd = nodesJson.IndexOf("}", objStart);
                        if (objEnd < 0) break;

                        string nodeJson = nodesJson.Substring(objStart, objEnd - objStart + 1);

                        var node = new PathNode
                        {
                            GridX = ExtractInt(nodeJson, "gx"),
                            GridY = ExtractInt(nodeJson, "gy"),
                            WorldX = ExtractFloat(nodeJson, "wx"),
                            WorldY = ExtractFloat(nodeJson, "wy"),
                            Height = ExtractFloat(nodeJson, "h"),
                            ConnectionFlags = (byte)ExtractInt(nodeJson, "c"),
                            SolidFlag = (byte)ExtractInt(nodeJson, "s")
                        };

                        pathMap._nodeGrid[(node.GridX, node.GridY)] = node;
                        pos = objEnd + 1;
                    }
                }

                DungeonRunners.Engine.Debug.Log($"[PATHMAP] loaded zone='{pathMap.ZoneName}' source=json nodes={pathMap._nodeGrid.Count}");
                return pathMap;
            }
            catch (Exception ex)
            {
                DungeonRunners.Engine.Debug.LogError($"[PATHMAP] source=json error={ex.Message}");
                return null;
            }
        }

        private static string ExtractString(string json, string key)
        {
            string search = $"\"{key}\"";
            int keyPos = json.IndexOf(search);
            if (keyPos < 0) return "";

            int colonPos = json.IndexOf(":", keyPos);
            int quoteStart = json.IndexOf("\"", colonPos);
            int quoteEnd = json.IndexOf("\"", quoteStart + 1);
            return json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
        }

        private static float ExtractFloat(string json, string key)
        {
            string search = $"\"{key}\"";
            int keyPos = json.IndexOf(search);
            if (keyPos < 0) return 0f;

            int colonPos = json.IndexOf(":", keyPos);
            int valueStart = colonPos + 1;

            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
                valueStart++;

            int valueEnd = valueStart;
            while (valueEnd < json.Length && (char.IsDigit(json[valueEnd]) || json[valueEnd] == '.' || json[valueEnd] == '-'))
                valueEnd++;

            string valueStr = json.Substring(valueStart, valueEnd - valueStart);
            float.TryParse(valueStr, out float result);
            return result;
        }

        private static int ExtractInt(string json, string key)
        {
            return (int)ExtractFloat(json, key);
        }

        public (int gridX, int gridY) WorldToGrid(float worldX, float worldY)
        {
            int gridX = (int)((worldX - WorldOffsetX) / TILE_SIZE);
            int gridY = (int)((worldY - WorldOffsetY) / TILE_SIZE);
            return (gridX, gridY);
        }

        public PathNode GetNodeAt(int gridX, int gridY)
        {
            _nodeGrid.TryGetValue((gridX, gridY), out var node);
            return node;
        }

        public PathNode GetNodeAtWorld(float worldX, float worldY)
        {
            var (gx, gy) = WorldToGrid(worldX, worldY);
            return GetNodeAt(gx, gy);
        }

        public float GetHeightAt(float worldX, float worldY, float defaultHeight = 50f)
        {
            if (worldX < _minWorldX || worldX > _maxWorldX ||
                worldY < _minWorldY || worldY > _maxWorldY)
            {
                return defaultHeight;
            }

            float cellX = (worldX - WorldOffsetX) / TILE_SIZE;
            float cellY = (worldY - WorldOffsetY) / TILE_SIZE;
            int gx = (int)Math.Floor(cellX);
            int gy = (int)Math.Floor(cellY);
            float fracX = cellX - gx;
            float fracY = cellY - gy;

            var n00 = GetNodeAt(gx, gy);
            var n10 = GetNodeAt(gx + 1, gy);
            var n01 = GetNodeAt(gx, gy + 1);
            var n11 = GetNodeAt(gx + 1, gy + 1);

            float h00 = n00 != null ? n00.Height : defaultHeight;
            float h10 = n10 != null ? n10.Height : h00;
            float h01 = n01 != null ? n01.Height : h00;
            float h11 = n11 != null ? n11.Height : h00;

            if (n00 == null)
                return defaultHeight;

            float w00 = (1f - fracX) * (1f - fracY);
            float w10 = fracX * (1f - fracY);
            float w01 = (1f - fracX) * fracY;
            float w11 = fracX * fracY;

            return h00 * w00 + h10 * w10 + h01 * w01 + h11 * w11;
        }

        public bool IsWalkable(float worldX, float worldY)
        {
            var node = GetNodeAtWorld(worldX, worldY);
            return node?.IsWalkable == true;
        }

        public bool CanReachPoint(float startX, float startY, float endX, float endY)
        {
            if (TryCanReachPoint(startX, startY, endX, endY, out bool canReach))
                return canReach;
            return true;
        }

        public PathReachability GetReachability(float startX, float startY, float endX, float endY)
        {
            if (!TryCanReachPoint(startX, startY, endX, endY, out bool canReach))
                return PathReachability.CoverageMissing;
            return canReach ? PathReachability.Reachable : PathReachability.Blocked;
        }

        public bool TryCanReachPoint(float startX, float startY, float endX, float endY, out bool canReach)
        {
            canReach = false;
            var startNode = GetReachNodeAtWorld(startX, startY);
            var endNode = GetReachNodeAtWorld(endX, endY);
            if (startNode == null || endNode == null) return false;
            if (startNode.GridX == endNode.GridX && startNode.GridY == endNode.GridY)
            {
                canReach = true;
                return true;
            }

            float dx = endX - startX;
            float dy = endY - startY;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            if (dist <= 0.001f)
            {
                canReach = true;
                return true;
            }

            canReach = !CastGroundRayBlocked(startX, startY, endX, endY);
            return true;
        }

        private PathNode GetReachNodeAtWorld(float worldX, float worldY)
        {
            var (gx, gy) = WorldToGrid(worldX, worldY);
            var node = GetNodeAt(gx, gy);
            if (node != null && node.IsWalkable) return node;
            for (int radius = 1; radius <= 2; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        if (Math.Abs(dx) != radius && Math.Abs(dy) != radius) continue;
                        node = GetNodeAt(gx + dx, gy + dy);
                        if (node != null && node.IsWalkable) return node;
                    }
                }
            }
            return null;
        }

        private const float NodeHalfExtent = TILE_SIZE * 0.5f;

        public bool CastGroundRayBlocked(float startX, float startY, float endX, float endY)
        {
            var node = GetReachNodeAtWorld(startX, startY);
            if (node == null) return false;

            float dx = endX - startX;
            float dy = endY - startY;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            if (dist <= 0f) return false;
            float dirX = dx / dist;
            float dirY = dy / dist;

            int maxIterations = Mathf.Max(_nodeGrid.Count, 1);
            for (int iter = 0; iter < maxIterations; iter++)
            {
                if (!CollideRayWithNode(node.WorldX, node.WorldY, startX, startY, dirX, dirY,
                        out float exitDist, out int exitEdge) || exitEdge < 0)
                    return false;
                if (exitDist >= dist) return false;
                if ((node.ConnectionFlags & (1 << exitEdge)) == 0) return true;
                var (ddx, ddy) = Directions[exitEdge];
                var next = GetNodeAt(node.GridX + ddx, node.GridY + ddy);
                if (next == null || !next.IsWalkable) return true;
                node = next;
            }
            return true;
        }

        public bool CastGroundRaySlide(float startX, float startY, float targetX, float targetY, out float slidX, out float slidY)
        {
            if (!CastGroundRayHit(startX, startY, targetX, targetY, out float hitX, out float hitY, out int exitEdge, out float remaining))
            {
                slidX = targetX; slidY = targetY;
                return false;
            }
            float dx = targetX - startX;
            float dy = targetY - startY;
            bool horizontal = exitEdge == 0 || exitEdge == 4;
            bool vertical = exitEdge == 2 || exitEdge == 6;
            if (!horizontal && !vertical)
            {
                if (Math.Abs(dx) >= Math.Abs(dy)) horizontal = true; else vertical = true;
            }
            int sdx = 0, sdy = 0;
            if (horizontal)
            {
                if (dx > 0f) sdx = 1; else if (dx < 0f) sdx = -1;
            }
            else
            {
                if (dy > 0f) sdy = 1; else if (dy < 0f) sdy = -1;
            }
            if (sdx == 0 && sdy == 0)
            {
                slidX = hitX; slidY = hitY;
                return true;
            }
            float slideEndX = hitX + sdx * remaining;
            float slideEndY = hitY + sdy * remaining;
            if (CastGroundRayHit(hitX, hitY, slideEndX, slideEndY, out float fx, out float fy, out int _, out float _))
            {
                slidX = fx; slidY = fy;
            }
            else
            {
                slidX = slideEndX; slidY = slideEndY;
            }
            return true;
        }

        private bool CastGroundRayHit(float startX, float startY, float endX, float endY,
            out float hitX, out float hitY, out int hitEdge, out float remaining)
        {
            hitX = endX; hitY = endY; hitEdge = -1; remaining = 0f;
            var node = GetReachNodeAtWorld(startX, startY);
            if (node == null) return false;
            float dx = endX - startX;
            float dy = endY - startY;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            if (dist <= 0f) return false;
            float dirX = dx / dist;
            float dirY = dy / dist;
            int maxIterations = Mathf.Max(_nodeGrid.Count, 1);
            for (int iter = 0; iter < maxIterations; iter++)
            {
                if (!CollideRayWithNode(node.WorldX, node.WorldY, startX, startY, dirX, dirY,
                        out float exitDist, out int exitEdge) || exitEdge < 0)
                    return false;
                if (exitDist >= dist) return false;
                bool blocked = (node.ConnectionFlags & (1 << exitEdge)) == 0;
                if (!blocked)
                {
                    var (ddx, ddy) = Directions[exitEdge];
                    var next = GetNodeAt(node.GridX + ddx, node.GridY + ddy);
                    if (next == null || !next.IsWalkable) blocked = true;
                    else { node = next; continue; }
                }
                hitX = startX + dirX * exitDist;
                hitY = startY + dirY * exitDist;
                hitEdge = exitEdge;
                remaining = dist - exitDist;
                return true;
            }
            return false;
        }

        private static bool CollideRayWithNode(float nx, float ny, float sx, float sy, float dirX, float dirY,
            out float exitDist, out int exitEdge)
        {
            float tNear = float.NegativeInfinity;
            exitDist = float.PositiveInfinity;
            exitEdge = -1;

            if (dirX == 0f)
            {
                if (sx < nx - NodeHalfExtent || nx + NodeHalfExtent < sx) return false;
            }
            else
            {
                float t1 = ((nx - sx) - NodeHalfExtent) / dirX;
                float t2 = ((nx - sx) + NodeHalfExtent) / dirX;
                float near = t1, far = t2; int ef = 2;
                if (t2 < t1) { near = t2; far = t1; ef = 6; }
                tNear = near; exitDist = far; exitEdge = ef;
            }

            if (dirY == 0f)
            {
                if (sy < ny - NodeHalfExtent || ny + NodeHalfExtent < sy) return false;
            }
            else
            {
                float t1 = ((ny - sy) - NodeHalfExtent) / dirY;
                float t2 = ((ny - sy) + NodeHalfExtent) / dirY;
                float near = t1, far = t2; int ef = 0;
                if (t2 < t1) { near = t2; far = t1; ef = 4; }
                if (tNear < near) tNear = near;
                if (far < exitDist) { exitDist = far; exitEdge = ef; }
                else if (far == exitDist) { exitEdge = (ef == 0 && exitEdge == 6) ? 7 : (exitEdge + ef) / 2; }
            }

            return exitDist >= 0f && tNear <= exitDist;
        }
    }
}
