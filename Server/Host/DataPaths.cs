using System.IO;
using DungeonRunners.Engine;

namespace DungeonRunners.Core
{
    public static class DataPaths
    {
        public static string DatabaseDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Database"));
        public static string GcDir => Path.Combine(DatabaseDir, "gc");
        public static string CobjDir => Path.Combine(DatabaseDir, "cobj");
        public static string SidecarDir => Path.Combine(DatabaseDir, "Sidecar");
        public static string DatabaseFile(string filename) => Path.Combine(DatabaseDir, filename);
    }
}
