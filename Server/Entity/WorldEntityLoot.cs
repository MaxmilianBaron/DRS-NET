using System;
using System.Collections.Generic;
using DungeonRunners.Data;

namespace DungeonRunners.Gameplay
{
    internal static class WorldEntityLoot
    {
        public static IEnumerable<(string Generator, int Count, int Slot)> GetNonCombatInteractiveItemGenerators(
            string gcType,
            IReadOnlyList<(string Generator, int Count)> storedGenerators,
            string fallbackGenerator,
            int fallbackCount)
        {
            var yielded = new HashSet<int>();
            GCNode desc = ResolveDescription(gcType);
            if (desc != null)
            {
                for (int slot = 1; slot <= 5; slot++)
                {
                    string suffix = slot == 1 ? "" : slot.ToString();
                    string generator = desc.GetString($"ItemGenerator{suffix}", "");
                    if (string.IsNullOrWhiteSpace(generator))
                        continue;
                    string countKey = $"ItemCount{suffix}";
                    int count = desc.HasProperty(countKey) ? desc.GetInt(countKey, 1) : 1;
                    if (count <= 0)
                        continue;
                    yielded.Add(slot);
                    yield return (generator, count, slot);
                }
            }

            if (storedGenerators != null)
            {
                int max = Math.Min(5, storedGenerators.Count);
                for (int index = 0; index < max; index++)
                {
                    int slot = index + 1;
                    if (yielded.Contains(slot))
                        continue;
                    var stored = storedGenerators[index];
                    if (string.IsNullOrWhiteSpace(stored.Generator) || stored.Count <= 0)
                        continue;
                    yielded.Add(slot);
                    yield return (stored.Generator, stored.Count, slot);
                }
            }

            if (yielded.Count == 0 && !string.IsNullOrWhiteSpace(fallbackGenerator) && fallbackCount > 0)
                yield return (fallbackGenerator, fallbackCount, 1);
        }

        private static GCNode ResolveDescription(string gcType)
        {
            if (string.IsNullOrWhiteSpace(gcType))
                return null;
            var db = GCDatabase.Instance;
            if (db == null || !db.IsLoaded)
                return null;
            GCNode node = db.ResolveWithInheritance(gcType);
            if (node == null)
                return null;
            return node.GetChild("Description");
        }
    }
}
