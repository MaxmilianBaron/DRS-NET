using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DungeonRunners.Data
{
    public sealed class PassiveAttributeTotals
    {
        private readonly Dictionary<string, decimal> _attributes = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, decimal> Attributes => _attributes;
        public int Strength => ToInt(Get("STRENGTH"));
        public int Agility => ToInt(Get("AGILITY"));
        public int Endurance => ToInt(Get("ENDURANCE"));
        public int Intellect => ToInt(Get("INTELLECT"));
        public int HealthPerEnduranceMod => ToInt(Get("HEALTH_PER_ENDURANCE_MOD"));
        public int ManaPerIntellectMod => ToInt(Get("MANA_PER_INTELLECT_MOD"));
        public decimal HealthMod => Get("HEALTH_MOD");
        public int MeleeAttackRatingMod => ToInt(Get("MELEE_ATTACK_RATING_MOD"));
        public decimal MeleeAttackSpeedMod => Get("MELEE_ATTACK_SPEED_MOD");
        public decimal RangeAttackSpeedMod => Get("RANGE_ATTACK_SPEED_MOD");
        public decimal MagicDamageMod => Get("MAGIC_DAMAGE_MOD");

        public void Add(string attribute, decimal value)
        {
            if (string.IsNullOrWhiteSpace(attribute)) return;
            string key = NormalizeAttribute(attribute);
            _attributes.TryGetValue(key, out decimal current);
            _attributes[key] = current + value;
        }

        public decimal Get(string attribute)
        {
            if (string.IsNullOrWhiteSpace(attribute)) return 0m;
            _attributes.TryGetValue(NormalizeAttribute(attribute), out decimal value);
            return value;
        }

        private static int ToInt(decimal value)
        {
            if (value > int.MaxValue) return int.MaxValue;
            if (value < int.MinValue) return int.MinValue;
            return (int)decimal.Truncate(value);
        }

        private static string NormalizeAttribute(string attribute)
        {
            return attribute.Trim().Replace(" ", "_").Replace("-", "_").ToUpperInvariant();
        }
    }

    public static class PassiveAttributeModifiers
    {
        public static PassiveAttributeTotals Resolve(IEnumerable<(string Skill, int Level)> skills)
        {
            var totals = new PassiveAttributeTotals();
            if (skills == null) return totals;

            foreach (var skill in skills)
            {
                if (string.IsNullOrWhiteSpace(skill.Skill)) continue;
                string modifierPath = ResolveModifierPath(skill.Skill);
                if (string.IsNullOrWhiteSpace(modifierPath)) continue;
                AddModifierAttributes(totals, modifierPath, Math.Max(1, skill.Level));
            }

            return totals;
        }

        public static string ResolveModifierPath(string skillPath)
        {
            if (string.IsNullOrWhiteSpace(skillPath) || GCDatabase.Instance == null)
                return null;

            GCNode skillNode = GCDatabase.Instance.ResolveWithInheritance(skillPath);
            GCNode description = skillNode?.GetChild("Description") ?? skillNode;
            string modifierPath = description?.GetString("Modifier", null);
            if (!string.IsNullOrWhiteSpace(modifierPath))
                return modifierPath.Trim();

            string nestedPath = skillPath.Trim() + ".Modifier";
            return GCDatabase.Instance.ResolveWithInheritance(nestedPath) != null ? nestedPath : null;
        }

        private static void AddModifierAttributes(PassiveAttributeTotals totals, string modifierPath, int level)
        {
            GCNode modifier = GCDatabase.Instance.ResolveWithInheritance(modifierPath);
            GCNode description = modifier?.GetChild("Description") ?? modifier;
            if (description == null) return;

            foreach (GCNode attributeNode in EnumerateAttributeNodes(description))
            {
                string attribute = attributeNode.GetString("Attribute", null);
                if (string.IsNullOrWhiteSpace(attribute)) continue;
                decimal value = GetDecimal(attributeNode, "Value") + GetDecimal(attributeNode, "ValueInc") * Math.Max(0, level - 1);
                totals.Add(attribute, value);
            }
        }

        private static IEnumerable<GCNode> EnumerateAttributeNodes(GCNode node)
        {
            foreach (GCNode child in node.AnonymousChildren ?? Enumerable.Empty<GCNode>())
                if (IsAttributeNode(child))
                    yield return child;
            if (node.Children == null) yield break;
            foreach (GCNode child in node.Children.Values)
                if (IsAttributeNode(child))
                    yield return child;
        }

        private static bool IsAttributeNode(GCNode node)
        {
            if (node == null) return false;
            if (node.HasProperty("Attribute")) return true;
            return !string.IsNullOrWhiteSpace(node.Extends) &&
                   node.Extends.IndexOf("Attribute", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static decimal GetDecimal(GCNode node, string property)
        {
            if (node?.Properties == null || !node.Properties.TryGetValue(property, out string value))
                return 0m;
            decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal result);
            return result;
        }
    }
}
