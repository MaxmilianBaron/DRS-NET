using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DungeonRunners.Engine;

namespace DungeonRunners.Data
{
    public sealed class PackageCatalog
    {
        private static PackageCatalog _instance;
        public static PackageCatalog Instance => _instance ??= new PackageCatalog();

        private readonly List<PackageTextDocument> _gcDocuments = new List<PackageTextDocument>();
        private readonly Dictionary<string, PackageTextDocument> _gcByName = new Dictionary<string, PackageTextDocument>(StringComparer.OrdinalIgnoreCase);

        public bool IsLoaded { get; private set; }
        public string RootPath { get; private set; } = "";
        public string SourceManifest { get; private set; } = "";
        public int ManifestGcTextDocs { get; private set; }
        public int GcTextDocumentCount => _gcDocuments.Count;
        public IEnumerable<PackageTextDocument> GcTextDocuments => _gcDocuments;

        public bool LoadFromAssets()
        {
            _gcDocuments.Clear();
            _gcByName.Clear();
            IsLoaded = false;
            SourceManifest = "";
            ManifestGcTextDocs = 0;

            string root = DungeonRunners.Core.DataPaths.SidecarDir;
            RootPath = root;
            string manifestPath = Path.Combine(root, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                Debug.LogError($"[CLIENT-PACKAGE-CATALOG] source=missing path='{root}'");
                return false;
            }

            SourceManifest = File.ReadAllText(manifestPath);
            ManifestGcTextDocs = GetInt(SourceManifest, "gcTextDocs");
            LoadGcTextDocuments(Path.Combine(root, "texts", "gc_text.jsonl"));
            IsLoaded = _gcDocuments.Count > 0;
            Debug.LogError($"[CLIENT-PACKAGE-CATALOG] source=Sidecar loaded={IsLoaded} gcTextDocs={_gcDocuments.Count}/{ManifestGcTextDocs} path='{root}' runtimePkgDependency=false");
            return IsLoaded;
        }

        public IEnumerable<PackageTextDocument> EnumerateGcTextDocuments(string searchPattern = "*.gc")
        {
            string pattern = string.IsNullOrWhiteSpace(searchPattern) ? "*.gc" : searchPattern.Trim();
            foreach (var doc in _gcDocuments)
            {
                if (WildcardMatch(doc.FileName, pattern) ||
                    WildcardMatch(doc.Name, pattern) ||
                    WildcardMatch(doc.SourceVirtualPath, pattern) ||
                    WildcardMatch(doc.GcPath, pattern))
                    yield return doc;
            }
        }

        public bool TryGetGcText(string nameOrPath, out PackageTextDocument document)
        {
            document = null;
            if (string.IsNullOrWhiteSpace(nameOrPath))
                return false;
            return _gcByName.TryGetValue(nameOrPath.Trim(), out document);
        }

        private void LoadGcTextDocuments(string path)
        {
            foreach (string line in ReadJsonLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var document = new PackageTextDocument
                {
                    EntryId = GetInt(line, "entryId"),
                    PackageId = GetInt(line, "packageId"),
                    EntryIndex = GetInt(line, "entryIndex"),
                    Name = GetString(line, "name"),
                    TypeCode = GetInt(line, "typeCode"),
                    TextSha1 = GetString(line, "textSha1"),
                    DecodedSha1 = GetString(line, "decodedSha1"),
                    SourceVirtualPath = GetString(line, "sourceVirtualPath"),
                    Text = GetString(line, "text")
                };
                if (document.EntryId == 0 && string.IsNullOrWhiteSpace(document.Name))
                    continue;
                document.GcPath = NormalizeGcPath(document.Name);
                document.FileName = ResolveFileName(document);
                document.Stem = Path.GetFileNameWithoutExtension(document.FileName);
                _gcDocuments.Add(document);
                Register(document);
            }
        }

        private void Register(PackageTextDocument document)
        {
            AddName(document.FileName, document);
            AddName(document.Stem, document);
            AddName(document.Name, document);
            AddName(document.Name.Replace('/', '\\'), document);
            AddName(document.Name.Replace('\\', '/'), document);
            AddName(document.SourceVirtualPath, document);
            AddName(document.GcPath, document);
            if (!string.IsNullOrWhiteSpace(document.GcPath))
                AddName(document.GcPath + ".gc", document);
        }

        private void AddName(string name, PackageTextDocument document)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;
            string key = name.Trim();
            if (!_gcByName.ContainsKey(key))
                _gcByName[key] = document;
        }

        private static IEnumerable<string> ReadJsonLines(string path)
        {
            if (File.Exists(path))
            {
                foreach (string line in File.ReadLines(path))
                    yield return line;
                yield break;
            }

            string partsDir = path + ".parts";
            if (!Directory.Exists(partsDir))
                yield break;

            string[] parts = Directory.GetFiles(partsDir, "*.jsonl");
            Array.Sort(parts, StringComparer.OrdinalIgnoreCase);
            foreach (string part in parts)
            {
                foreach (string line in File.ReadLines(part))
                    yield return line;
            }
        }

        private static string ResolveFileName(PackageTextDocument document)
        {
            string source = document.SourceVirtualPath;
            if (!string.IsNullOrWhiteSpace(source))
            {
                string fileName = Path.GetFileName(source.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
                if (!string.IsNullOrWhiteSpace(fileName))
                    return fileName;
            }

            string leaf = (document.Name ?? "").Replace('/', '\\');
            int slash = leaf.LastIndexOf('\\');
            if (slash >= 0)
                leaf = leaf.Substring(slash + 1);
            if (string.IsNullOrWhiteSpace(leaf))
                leaf = document.EntryId.ToString();
            if (!leaf.EndsWith(".gc", StringComparison.OrdinalIgnoreCase))
                leaf += ".gc";
            return leaf;
        }

        private static string NormalizeGcPath(string value)
        {
            string path = (value ?? "").Replace('\\', '.').Replace('/', '.').Trim();
            if (path.EndsWith(".gc", StringComparison.OrdinalIgnoreCase))
                path = path.Substring(0, path.Length - 3);
            return path;
        }

        private static int GetInt(string json, string key)
        {
            string marker = "\"" + key + "\":";
            int start = json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
                return 0;
            int pos = start + marker.Length;
            while (pos < json.Length && char.IsWhiteSpace(json[pos]))
                pos++;
            int sign = 1;
            if (pos < json.Length && json[pos] == '-')
            {
                sign = -1;
                pos++;
            }
            int value = 0;
            bool any = false;
            while (pos < json.Length && char.IsDigit(json[pos]))
            {
                any = true;
                value = (value * 10) + (json[pos] - '0');
                pos++;
            }
            return any ? value * sign : 0;
        }

        private static string GetString(string json, string key)
        {
            string marker = "\"" + key + "\":\"";
            int start = json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
                return "";
            int pos = start + marker.Length;
            var sb = new StringBuilder();
            while (pos < json.Length)
            {
                char c = json[pos++];
                if (c == '"')
                    break;
                if (c != '\\' || pos >= json.Length)
                {
                    sb.Append(c);
                    continue;
                }

                char esc = json[pos++];
                switch (esc)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (pos + 4 <= json.Length && int.TryParse(json.Substring(pos, 4), System.Globalization.NumberStyles.HexNumber, null, out int code))
                        {
                            sb.Append((char)code);
                            pos += 4;
                        }
                        break;
                    default:
                        sb.Append(esc);
                        break;
                }
            }
            return sb.ToString();
        }

        private static bool WildcardMatch(string text, string pattern)
        {
            text = text ?? "";
            pattern = string.IsNullOrWhiteSpace(pattern) ? "*" : pattern;
            int ti = 0, pi = 0, star = -1, mark = 0;
            while (ti < text.Length)
            {
                if (pi < pattern.Length &&
                    (pattern[pi] == '?' || char.ToLowerInvariant(pattern[pi]) == char.ToLowerInvariant(text[ti])))
                {
                    ti++;
                    pi++;
                }
                else if (pi < pattern.Length && pattern[pi] == '*')
                {
                    star = pi++;
                    mark = ti;
                }
                else if (star >= 0)
                {
                    pi = star + 1;
                    ti = ++mark;
                }
                else
                {
                    return false;
                }
            }
            while (pi < pattern.Length && pattern[pi] == '*')
                pi++;
            return pi == pattern.Length;
        }
    }

    public sealed class PackageTextDocument
    {
        public int EntryId;
        public int PackageId;
        public int EntryIndex;
        public string Name;
        public int TypeCode;
        public string TextSha1;
        public string DecodedSha1;
        public string SourceVirtualPath;
        public string Text;
        public string FileName;
        public string Stem;
        public string GcPath;
    }
}
