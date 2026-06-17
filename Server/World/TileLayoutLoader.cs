using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DungeonRunners.Data;

namespace DungeonRunners.Utilities
{
    public static class TileLayoutLoader
    {
        public static TileLayout Load(string filePath)
        {
            GCNode root = GcParser.ParseFile(filePath);
            if (root == null) throw new InvalidDataException($"GcParser returned null for {filePath}");

            var placements = new List<TilePlacement>();
            Collect(root, placements);
            return new TileLayout(filePath, root.Extends, placements);
        }

        public static TileLayout LoadFromText(string text, string sourceName = "")
        {
            GCNode root = GcParser.Parse(text, sourceName);
            if (root == null) throw new InvalidDataException("GcParser returned null");

            var placements = new List<TilePlacement>();
            Collect(root, placements);
            return new TileLayout(sourceName, root.Extends, placements);
        }

        private static void Collect(GCNode node, List<TilePlacement> output)
        {
            if (node == null) return;

            if (node.IsAnonymous
                && !string.IsNullOrEmpty(node.Extends)
                && node.HasProperty("Position"))
            {
                if (TryParseVector3(node.GetString("Position"), out float x, out float y, out float z))
                {
                    float heading = node.GetFloat("Heading", 0f);
                    output.Add(new TilePlacement(node.Extends, x, y, z, heading));
                }
            }

            foreach (var child in node.AnonymousChildren)
                Collect(child, output);
            foreach (var kv in node.Children)
                Collect(kv.Value, output);
        }

        private static bool TryParseVector3(string raw, out float x, out float y, out float z)
        {
            x = y = z = 0f;
            if (string.IsNullOrEmpty(raw)) return false;

            string[] parts = raw.Split(',');
            if (parts.Length != 3) return false;

            var inv = CultureInfo.InvariantCulture;
            return float.TryParse(parts[0].Trim(), NumberStyles.Float, inv, out x)
                && float.TryParse(parts[1].Trim(), NumberStyles.Float, inv, out y)
                && float.TryParse(parts[2].Trim(), NumberStyles.Float, inv, out z);
        }
    }

    public sealed class TileLayout
    {
        public string SourcePath { get; }
        public string RootExtends { get; }
        public IReadOnlyList<TilePlacement> Placements { get; }

        public TileLayout(string sourcePath, string rootExtends, List<TilePlacement> placements)
        {
            SourcePath = sourcePath;
            RootExtends = rootExtends;
            Placements = placements;
        }
    }

    public readonly struct TilePlacement
    {
        public readonly string ExtendsPath;
        public readonly float X;
        public readonly float Y;
        public readonly float Z;
        public readonly float Heading;

        public TilePlacement(string extendsPath, float x, float y, float z, float heading)
        {
            ExtendsPath = extendsPath;
            X = x;
            Y = y;
            Z = z;
            Heading = heading;
        }

        public string LeafName
        {
            get
            {
                if (string.IsNullOrEmpty(ExtendsPath)) return "";
                int dot = ExtendsPath.LastIndexOf('.');
                return dot < 0 ? ExtendsPath : ExtendsPath.Substring(dot + 1);
            }
        }
    }
}
