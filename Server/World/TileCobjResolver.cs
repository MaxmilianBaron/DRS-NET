using System;
using System.Collections.Generic;
using System.IO;

namespace DungeonRunners.Utilities
{
    public static class TileCobjResolver
    {
        private const string PkiExportEnvVar = "DR_GAME_PKI_EXPORT";
        private const string PkiExportDefaultPath = @"C:\Users\tippi\Documents\Dungeon Runners\666 game.pki export";

        private static readonly object _initLock = new object();
        private static Dictionary<string, string> _leafToPath;
        private static Dictionary<string, string> _tileToPath;

        public static int CobjFileCount
        {
            get
            {
                EnsureIndexed();
                return _leafToPath?.Count ?? 0;
            }
        }

        public static int TileFileCount
        {
            get
            {
                EnsureIndexed();
                return _tileToPath?.Count ?? 0;
            }
        }

        public static string ResolveTilePath(string tileTypeName)
        {
            if (string.IsNullOrEmpty(tileTypeName)) return null;
            EnsureIndexed();
            if (_tileToPath == null) return null;
            return _tileToPath.TryGetValue(tileTypeName.ToLowerInvariant(), out string fullPath) ? fullPath : null;
        }

        public static string ResolveCobjPath(string extendsPath)
        {
            if (string.IsNullOrEmpty(extendsPath)) return null;

            EnsureIndexed();
            if (_leafToPath == null) return null;

            string leaf = LeafOf(extendsPath).ToLowerInvariant();
            return _leafToPath.TryGetValue(leaf, out string fullPath) ? fullPath : null;
        }

        public static string ResolveCobjPathByLeaf(string leafName)
        {
            if (string.IsNullOrEmpty(leafName)) return null;
            EnsureIndexed();
            if (_leafToPath == null) return null;
            return _leafToPath.TryGetValue(leafName.ToLowerInvariant(), out string fullPath) ? fullPath : null;
        }

        public static CobjData LoadCobj(string extendsPath)
        {
            string path = ResolveCobjPath(extendsPath);
            return path == null ? null : CobjParser.ParseFile(path);
        }

        private static void EnsureIndexed()
        {
            if (_leafToPath != null) return;

            lock (_initLock)
            {
                if (_leafToPath != null) return;

                var cobjIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var tileIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (string dir in EnumerateCobjSourceDirs())
                    IndexDir(dir, "*.cobj", cobjIndex);
                foreach (string dir in EnumerateTileSourceDirs())
                {
                    IndexDir(dir, "*.tile", tileIndex);
                    IndexDir(dir, "*.gc", tileIndex);
                }

                _leafToPath = cobjIndex;
                _tileToPath = tileIndex;
            }
        }

        private static void IndexDir(string dir, string pattern, Dictionary<string, string> index)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            foreach (string file in Directory.GetFiles(dir, pattern))
            {
                string leaf = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                index[leaf] = file;
            }
        }

        private static IEnumerable<string> EnumerateCobjSourceDirs()
        {
            yield return DungeonRunners.Core.DataPaths.CobjDir;
            string exportDir = ResolveFlatPkiExportDir();
            if (exportDir != null) yield return exportDir;
        }

        private static IEnumerable<string> EnumerateTileSourceDirs()
        {
            yield return DungeonRunners.Core.DataPaths.GcDir;
            string exportDir = ResolveFlatPkiExportDir();
            if (exportDir != null) yield return exportDir;
        }

        private static string ResolveFlatPkiExportDir()
        {
            string envPath = Environment.GetEnvironmentVariable(PkiExportEnvVar);
            if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath)) return envPath;
            if (Directory.Exists(PkiExportDefaultPath)) return PkiExportDefaultPath;
            return null;
        }

        private static string LeafOf(string dottedPath)
        {
            int dot = dottedPath.LastIndexOf('.');
            return dot < 0 ? dottedPath : dottedPath.Substring(dot + 1);
        }
    }
}
