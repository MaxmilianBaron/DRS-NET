using System.Collections.Generic;
using DungeonRunners.Engine;
using DungeonRunners.Gameplay;

namespace DungeonRunners.Core
{
    public class PathMapCatalog
    {
        private static PathMapCatalog _instance;
        public static PathMapCatalog Instance => _instance ??= new PathMapCatalog();

        private Dictionary<string, PathMap> _pathMaps = new Dictionary<string, PathMap>();
        private HashSet<string> _proceduralInstancePathMapMissLogged = new HashSet<string>();
        private bool _loaded = false;

        public void LoadAllPathMaps()
        {
            if (_loaded) return;

            try
            {
                using (var connection = DungeonRunners.Database.GameDatabase.GetConnection())
                using (var reader = DungeonRunners.Database.GameDatabase.ExecuteReader(connection,
                    "SELECT zone_name FROM pathmap_zones"))
                {
                    var zones = new List<string>();
                    while (reader.Read())
                        zones.Add(reader.GetString(0));

                    foreach (var zone in zones)
                    {
                        var pathMap = PathMap.LoadFromSQLite(zone);
                        if (pathMap != null)
                        {
                            string key = zone.ToLowerInvariant();
                            _pathMaps[key] = pathMap;
                            Debug.Log($"[PATHMAP-CATALOG] loaded zone='{key}' source=sqlite");
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[PATHMAP-CATALOG] source=sqlite error={ex.Message}");
            }

            _loaded = true;
            Debug.Log($"[PATHMAP-CATALOG] loaded count={_pathMaps.Count}");
        }

        public void RegisterInstancePathMap(string zoneName, PathMap pathMap)
        {
            if (string.IsNullOrWhiteSpace(zoneName) || pathMap == null) return;
            string key = zoneName.ToLowerInvariant();
            _pathMaps[key] = pathMap;
            _proceduralInstancePathMapMissLogged.Remove(key);
            Debug.Log($"[PATHMAP-CATALOG] register instance='{key}' nodes={pathMap.NodeCount}");
            Debug.LogError($"[PATHMAP-VERIFY] zone='{key}' nodes={pathMap.NodeCount} walkable={pathMap.WalkableCount} bounds=({pathMap.MinWorldX:F0},{pathMap.MinWorldY:F0})->({pathMap.MaxWorldX:F0},{pathMap.MaxWorldY:F0})");
        }

        public void UnregisterInstancePathMap(string zoneName)
        {
            if (string.IsNullOrWhiteSpace(zoneName)) return;
            string key = zoneName.ToLowerInvariant();
            if (_pathMaps.Remove(key))
                Debug.Log($"[PATHMAP-CATALOG] unregister instance='{key}'");
        }

        public PathMap FindByPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return null;
            if (!_loaded) LoadAllPathMaps();
            string key = prefix.ToLowerInvariant();
            foreach (var kv in _pathMaps)
            {
                if (kv.Key.StartsWith(key, System.StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }
            return null;
        }

        public PathMap GetPathMap(string zoneName)
        {
            if (!_loaded) LoadAllPathMaps();
            if (string.IsNullOrWhiteSpace(zoneName)) return null;
            string key = zoneName.ToLowerInvariant();
            if (_pathMaps.TryGetValue(key, out var pathMap)) return pathMap;
            int instIndex = key.IndexOf("_inst", System.StringComparison.OrdinalIgnoreCase);
            if (instIndex > 0)
            {
                string baseKey = key.Substring(0, instIndex);
                if (DungeonMazeSpawner.IsProceduralZone(baseKey))
                {
                    if (_proceduralInstancePathMapMissLogged.Add(key))
                        Debug.LogError($"[PATHMAP-CATALOG] instance='{zoneName}' base='{baseKey}' reason=missing-instance-pathmap");
                    return null;
                }
                if (_pathMaps.TryGetValue(baseKey, out pathMap)) return pathMap;
            }
            return pathMap;
        }

        public float GetHeight(string zoneName, float worldX, float worldY, float defaultHeight = 50f)
        {
            var pathMap = GetPathMap(zoneName);
            if (pathMap == null) return defaultHeight;
            return pathMap.GetHeightAt(worldX, worldY, defaultHeight);
        }

        public bool IsWalkable(string zoneName, float worldX, float worldY)
        {
            var pathMap = GetPathMap(zoneName);
            return pathMap?.IsWalkable(worldX, worldY) ?? true;
        }
    }
}
