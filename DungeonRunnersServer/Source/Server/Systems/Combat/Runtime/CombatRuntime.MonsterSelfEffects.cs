using System;
using System.Collections.Generic;
using DungeonRunners.Core;
using DungeonRunners.Data;
using DungeonRunners.Engine;

namespace DungeonRunners.Combat
{
    public partial class CombatRuntime
    {
        private enum MonsterSelfEffectNodeKind : byte
        {
            Sequence,
            FriendAoe,
            Cowardice,
            HitPointRegen,
            AuthoredAttributeModifier,
            EnemyDamageAura,
            VisualOnly
        }

        private sealed class MonsterSelfEffectNode
        {
            public MonsterSelfEffectNodeKind Kind;
            public readonly List<MonsterSelfEffectNode> Children = new List<MonsterSelfEffectNode>();
            public string EffectPath;
            public string ModifierPath;
            public int ChanceWire = 0x6400;
            public int RadiusF32;
            public int MaxTargets = int.MaxValue;
            public int PowerLevelF32;
            public int DurationTicks;
            public bool RemoveOnDeath;
            public bool AvoidCorners = true;
            public int HitPointRegenBonus;
            public bool OverrideTable;
            public string StackRule;
            public Dictionary<string, int> Attributes;
            public MonsterAuraPlan Aura;
        }

        private sealed class MonsterSelfSkillPlan
        {
            public string SkillPath;
            public string EffectPath;
            public MonsterSelfEffectNode Root;
            public MonsterCastModifierPlan CastModifier;
        }

        private bool TryBuildMonsterSelfActiveSkillEffectPlan(Monster monster, out MonsterSelfSkillPlan plan, out string reason)
        {
            plan = null;
            reason = null;
            MonsterActiveSkillRuntime activeSkill = monster?.SelectedActiveSkill;
            if (activeSkill == null || string.IsNullOrWhiteSpace(activeSkill.Path))
            {
                reason = "missing-selected-skill";
                return false;
            }
            var gc = GCDatabase.Instance;
            if (gc == null || !gc.IsLoaded)
            {
                reason = "authored-database-unavailable";
                return false;
            }
            GCNode skill = gc.ResolveWithInheritance(activeSkill.Path);
            GCNode skillDesc = skill?.GetChild("Description") ?? skill;
            string effectPath = skillDesc?.GetString("Effect", activeSkill.Effect) ?? activeSkill.Effect;
            GCNode effect = ResolveAuthoredNodeReference(effectPath, skill);
            if (effect == null)
            {
                reason = "missing-authored-effect";
                return false;
            }
            SpellData spell = SpellDatabase.GetSpell(activeSkill.Path);
            if (spell == null)
            {
                reason = "missing-authored-spell";
                return false;
            }
            int skillLevel = Math.Max(1, (int)activeSkill.SkillLevel);
            int powerLevelF32 = spell.ResolvePowerLevelF32(skillLevel);
            if (!TryBuildMonsterSelfEffectNode(
                    effect,
                    effectPath,
                    skill,
                    skillLevel,
                    powerLevelF32,
                    out MonsterSelfEffectNode root,
                    out reason))
                return false;
            MonsterCastModifierPlan castModifier = null;
            if (!activeSkill.InstantUse
                && !string.IsNullOrWhiteSpace(activeSkill.CastModifier)
                && activeSkill.AddModifierWhileClosing)
            {
                reason = "self-cast-modifier-while-closing-unproven";
                return false;
            }
            if (!activeSkill.InstantUse
                && !string.IsNullOrWhiteSpace(activeSkill.CastModifier)
                && !TryBuildMonsterCastModifierPlan(
                    activeSkill.CastModifier,
                    skill,
                    activeSkill.Path,
                    skillLevel,
                    unchecked((uint)Math.Max(0, powerLevelF32)),
                    out castModifier,
                    out reason))
                return false;
            plan = new MonsterSelfSkillPlan
            {
                SkillPath = activeSkill.Path,
                EffectPath = effectPath,
                Root = root,
                CastModifier = castModifier
            };
            return true;
        }

        private bool TryBuildMonsterSelfEffectNode(
            GCNode authoredNode,
            string effectPath,
            GCNode skillContext,
            int skillLevel,
            int powerLevelF32,
            out MonsterSelfEffectNode plan,
            out string reason)
        {
            plan = null;
            reason = null;
            if (authoredNode == null)
            {
                reason = "missing-effect-node";
                return false;
            }
            string nodePath = BuildAuthoredEffectPath(effectPath, authoredNode);
            if (AuthoredExtends(authoredNode, "SpellSoundEffect"))
            {
                plan = new MonsterSelfEffectNode
                {
                    Kind = MonsterSelfEffectNodeKind.VisualOnly,
                    EffectPath = nodePath,
                    ChanceWire = ResolveSpellEffectChanceWire(authoredNode)
                };
                return true;
            }
            if (AuthoredExtends(authoredNode, "SpellEffectEffect"))
            {
                plan = new MonsterSelfEffectNode
                {
                    Kind = MonsterSelfEffectNodeKind.VisualOnly,
                    EffectPath = nodePath,
                    ChanceWire = ResolveSpellEffectChanceWire(authoredNode)
                };
                return true;
            }
            if (AuthoredExtends(authoredNode, "SpellAOEEffect"))
            {
                string targetType = authoredNode.GetString("TargetType", "ENEMY");
                if (!string.Equals(targetType, "FRIEND", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(targetType, "1", StringComparison.OrdinalIgnoreCase))
                {
                    reason = $"unsupported-self-aoe-target:{targetType}";
                    return false;
                }
                int chanceWire = ResolveSpellEffectChanceWire(authoredNode);
                if (chanceWire != 0x6400)
                {
                    reason = "aoe-effect-chance-unproven";
                    return false;
                }
                int authoredRadiusF32 = ResolveMonsterAoeRadiusF32(authoredNode, skillLevel);
                if (authoredRadiusF32 <= 0)
                {
                    reason = "missing-positive-aoe-radius";
                    return false;
                }
                plan = new MonsterSelfEffectNode
                {
                    Kind = MonsterSelfEffectNodeKind.FriendAoe,
                    EffectPath = nodePath,
                    ChanceWire = chanceWire,
                    RadiusF32 = authoredRadiusF32 > int.MaxValue - 0xA00 ? int.MaxValue : authoredRadiusF32 + 0xA00,
                    MaxTargets = ResolveMonsterAoeNumTargets(authoredNode, skillLevel)
                };
                return TryBuildMonsterSelfEffectChildren(authoredNode, nodePath, skillContext, skillLevel, powerLevelF32, plan, out reason);
            }
            if (AuthoredExtends(authoredNode, "SpellModEffect"))
                return TryBuildMonsterSelfModifierNode(authoredNode, nodePath, skillContext, skillLevel, powerLevelF32, out plan, out reason);
            if (AuthoredExtends(authoredNode, "SpellEffect")
                || AuthoredExtends(authoredNode, "SpellSnapToGroundEffect")
                || string.IsNullOrWhiteSpace(authoredNode.Extends))
            {
                plan = new MonsterSelfEffectNode
                {
                    Kind = MonsterSelfEffectNodeKind.Sequence,
                    EffectPath = nodePath
                };
                return TryBuildMonsterSelfEffectChildren(authoredNode, nodePath, skillContext, skillLevel, powerLevelF32, plan, out reason);
            }
            reason = $"unsupported-self-effect-family:{authoredNode.Extends ?? "unknown"}";
            return false;
        }

        private bool TryBuildMonsterSelfEffectChildren(
            GCNode authoredNode,
            string effectPath,
            GCNode skillContext,
            int skillLevel,
            int powerLevelF32,
            MonsterSelfEffectNode parent,
            out string reason)
        {
            reason = null;
            foreach (GCNode child in authoredNode.EnumerateChildrenInOrder())
            {
                if (!TryBuildMonsterSelfEffectNode(child, effectPath, skillContext, skillLevel, powerLevelF32, out MonsterSelfEffectNode childPlan, out reason))
                    return false;
                parent.Children.Add(childPlan);
            }
            if (parent.Children.Count == 0)
            {
                reason = "effect-node-has-no-child-effect";
                return false;
            }
            return true;
        }

        private bool TryBuildMonsterSelfModifierNode(
            GCNode modEffect,
            string effectPath,
            GCNode skillContext,
            int skillLevel,
            int powerLevelF32,
            out MonsterSelfEffectNode plan,
            out string reason)
        {
            plan = null;
            reason = null;
            string modifierPath = modEffect.GetString("Modifier", null);
            GCNode modifier = ResolveAuthoredNodeReference(modifierPath, skillContext);
            GCNode modifierDesc = modifier?.GetChild("Description") ?? modifier;
            if (modifier == null || modifierDesc == null)
            {
                reason = "missing-authored-modifier";
                return false;
            }
            int durationF32 = ResolveMonsterSpellModDurationF32(modEffect, skillLevel);
            int durationTicks = ComputeSpellModDurationTicks(durationF32);
            int chanceWire = ResolveSpellEffectChanceWire(modEffect);
            if (AuthoredExtends(modifier, "AuraMod"))
                return TryBuildMonsterAuraEffectNode(
                    modEffect,
                    effectPath,
                    skillContext,
                    skillLevel,
                    powerLevelF32,
                    modifierPath,
                    modifier,
                    modifierDesc,
                    durationTicks,
                    chanceWire,
                    out plan,
                    out reason);
            if (AuthoredExtends(modifier, "CowardiceModifier"))
            {
                string stackRule = modifierDesc.GetString("StackRule", "UNIQUEBYTYPE");
                if (!string.Equals(stackRule, "UNIQUEBYTYPE", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(stackRule, "1", StringComparison.OrdinalIgnoreCase))
                {
                    reason = $"unsupported-cowardice-stack:{stackRule}";
                    return false;
                }
                if (modifierDesc.GetInt("TerminateWhenHitChance", 0) != 0)
                {
                    reason = "cowardice-terminate-when-hit-unimplemented";
                    return false;
                }
                plan = new MonsterSelfEffectNode
                {
                    Kind = MonsterSelfEffectNodeKind.Cowardice,
                    EffectPath = effectPath,
                    ModifierPath = modifierPath,
                    ChanceWire = chanceWire,
                    PowerLevelF32 = powerLevelF32,
                    DurationTicks = durationTicks,
                    RemoveOnDeath = modifierDesc.GetBool("RemoveOnDeath", false),
                    AvoidCorners = modifierDesc.GetBool("AvoidCorners", true)
                };
                return true;
            }
            if (durationTicks <= 0)
                durationTicks = 0;
            if (!AuthoredExtends(modifier, "AttributeModifier"))
            {
                reason = $"unsupported-self-modifier-family:{modifier.Extends ?? "unknown"}";
                return false;
            }
            var attributes = new List<GCNode>();
            CollectMonsterModifierAttributeNodes(modifierDesc, attributes, new HashSet<GCNode>());
            if (attributes.Count == 0)
            {
                reason = "empty-attribute-modifier";
                return false;
            }
            var authoredAttributes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            bool onlyHitPointRegen = true;
            foreach (GCNode attribute in attributes)
            {
                string attributeName = attribute.GetString("Attribute", null);
                if (string.IsNullOrWhiteSpace(attributeName))
                {
                    reason = "missing-self-attribute-name";
                    return false;
                }
                if (!IsMonsterAuthoredAttributeSupported(attributeName))
                {
                    reason = $"unsupported-self-attribute:{attributeName}";
                    return false;
                }
                int value = ResolveMonsterAttributeLevelValue(attribute, skillLevel);
                authoredAttributes[attributeName] = value;
                onlyHitPointRegen &= string.Equals(attributeName, "HIT_POINT_REGEN_BONUS", StringComparison.OrdinalIgnoreCase);
            }
            if (onlyHitPointRegen && authoredAttributes.Count == 1 && authoredAttributes.TryGetValue("HIT_POINT_REGEN_BONUS", out int regenValue))
            {
                plan = new MonsterSelfEffectNode
                {
                    Kind = MonsterSelfEffectNodeKind.HitPointRegen,
                    EffectPath = effectPath,
                    ModifierPath = modifierPath,
                    ChanceWire = chanceWire,
                    DurationTicks = durationTicks,
                    RemoveOnDeath = modifierDesc.GetBool("RemoveOnDeath", false),
                    HitPointRegenBonus = regenValue,
                    OverrideTable = attributes[0].GetBool("OverrideTable", false)
                };
                return true;
            }
            plan = new MonsterSelfEffectNode
            {
                Kind = MonsterSelfEffectNodeKind.AuthoredAttributeModifier,
                EffectPath = effectPath,
                ModifierPath = modifierPath,
                ChanceWire = chanceWire,
                DurationTicks = durationTicks,
                RemoveOnDeath = modifierDesc.GetBool("RemoveOnDeath", false),
                PowerLevelF32 = powerLevelF32,
                StackRule = modifierDesc.GetString("StackRule", ""),
                Attributes = authoredAttributes
            };
            return true;
        }

        private static bool IsMonsterAuthoredAttributeSupported(string attribute)
        {
            switch ((attribute ?? string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant())
            {
                case "HITPOINTREGENBONUS":
                case "SPEEDMOD":
                case "SIZEMOD":
                case "ATTACKSPEEDMOD":
                case "DAMAGEMOD":
                case "MELEEDAMAGEMOD":
                case "ATTACKRATINGMOD":
                case "DEFENSERATINGMOD":
                    return true;
                default:
                    return false;
            }
        }

        private static void CollectMonsterModifierAttributeNodes(GCNode node, List<GCNode> result, HashSet<GCNode> visited)
        {
            if (node == null || !visited.Add(node))
                return;
            if (AuthoredExtends(node, "Attribute") || node.HasProperty("Attribute"))
                result.Add(node);
            foreach (GCNode child in node.EnumerateChildrenInOrder())
                CollectMonsterModifierAttributeNodes(child, result, visited);
        }

        private static int ResolveMonsterSpellModDurationF32(GCNode modEffect, int skillLevel)
        {
            long duration = modEffect.GetFixed32("Duration", 0);
            long increment = modEffect.GetFixed32("DurationInc", 0);
            long resolved = duration + (long)Math.Max(0, skillLevel - 1) * increment;
            if (resolved <= 0)
                return 0;
            return resolved >= int.MaxValue ? int.MaxValue : (int)resolved;
        }

        private static int ResolveMonsterAttributeLevelValue(GCNode attribute, int skillLevel)
        {
            GCNode bestCurve = null;
            int bestLevel = int.MinValue;
            foreach (GCNode child in attribute?.EnumerateChildrenInOrder() ?? Array.Empty<GCNode>())
            {
                if (!AuthoredExtends(child, "CurveTableEntry"))
                    continue;
                int level = child.GetInt("Level", int.MinValue);
                if (level <= Math.Max(1, skillLevel) && level >= bestLevel)
                {
                    bestCurve = child;
                    bestLevel = level;
                }
            }
            if (bestCurve != null)
                return bestCurve.GetInt("Value", 0);
            long value = attribute.GetInt("Value", 0);
            long increment = attribute.GetInt("ValueInc", 0);
            long resolved = value + (long)Math.Max(0, skillLevel - 1) * increment;
            if (resolved >= int.MaxValue)
                return int.MaxValue;
            if (resolved <= int.MinValue)
                return int.MinValue;
            return (int)resolved;
        }

        private static int ResolveMonsterAoeRadiusF32(GCNode aoe, int skillLevel)
        {
            int minimum = aoe.HasProperty("RadiusMin") ? aoe.GetFixed32("RadiusMin", 0) : aoe.GetFixed32("Radius", 0);
            int maximum = aoe.HasProperty("RadiusMax") ? aoe.GetFixed32("RadiusMax", minimum) : minimum;
            int increment = aoe.GetFixed32("RadiusInc", 0);
            long resolved = minimum + (long)Math.Max(1, skillLevel) * increment;
            if (maximum > 0 && resolved > maximum)
                resolved = maximum;
            if (resolved <= 0)
                return 0;
            return resolved >= int.MaxValue ? int.MaxValue : (int)resolved;
        }

        private static int ResolveMonsterAoeNumTargets(GCNode aoe, int skillLevel)
        {
            int minimumF32 = aoe.HasProperty("NumTargetsMin") ? aoe.GetFixed32("NumTargetsMin", 0) : aoe.GetFixed32("NumTargets", 0);
            int maximumF32 = aoe.GetFixed32("NumTargetsMax", 0);
            int incrementF32 = aoe.GetFixed32("NumTargetsInc", 0);
            if (minimumF32 == 0 && maximumF32 == 0 && incrementF32 == 0)
                return int.MaxValue;
            long resolvedF32 = minimumF32 + (long)Math.Max(1, skillLevel) * incrementF32;
            if (maximumF32 > 0 && resolvedF32 > maximumF32)
                resolvedF32 = maximumF32;
            if (resolvedF32 <= 0)
                return int.MaxValue;
            long count = resolvedF32 >> 8;
            if (count <= 0)
                return 1;
            return count >= int.MaxValue ? int.MaxValue : (int)count;
        }

        private bool ExecuteMonsterSelfEffectPlan(MonsterSelfEffectNode plan, Monster sourceMonster, Monster target)
        {
            if (plan == null || sourceMonster == null || target == null)
                return false;
            if (plan.Kind == MonsterSelfEffectNodeKind.Sequence)
            {
                bool handled = true;
                foreach (MonsterSelfEffectNode child in plan.Children)
                    handled &= ExecuteMonsterSelfEffectPlan(child, sourceMonster, target);
                return handled;
            }
            if (plan.Kind == MonsterSelfEffectNodeKind.FriendAoe)
            {
                List<Monster> targets = CollectMonsterFriendAoeTargets(target, plan.RadiusF32, plan.MaxTargets);
                Debug.LogError($"[MON-FRIEND-AOE] source={sourceMonster.Name}#{sourceMonster.EntityId} center={target.Name}#{target.EntityId} effect={plan.EffectPath ?? "none"} radiusF32={plan.RadiusF32} maxTargets={plan.MaxTargets} targets={string.Join(",", targets.ConvertAll(candidate => candidate.EntityId.ToString()))} sourceFunction=SpellAOEEffect::doEffect@0x00549250->UnitFinder2::findFriends@0x00510E50");
                bool handled = true;
                foreach (Monster friend in targets)
                    foreach (MonsterSelfEffectNode child in plan.Children)
                        handled &= ExecuteMonsterSelfEffectPlan(child, sourceMonster, friend);
                return handled;
            }
            if (!PassesMonsterSelfEffectChance(sourceMonster, target, plan))
                return true;
            if (plan.Kind == MonsterSelfEffectNodeKind.Cowardice)
                return ApplyMonsterCowardiceModifier(
                    sourceMonster,
                    target,
                    sourceMonster.SelectedActiveSkill?.Path,
                    plan.EffectPath,
                    plan.ModifierPath,
                    plan.PowerLevelF32,
                    plan.DurationTicks,
                    plan.RemoveOnDeath,
                    plan.AvoidCorners);
            if (plan.Kind == MonsterSelfEffectNodeKind.HitPointRegen)
            {
                ApplyMonsterRegenBonusModifier(
                    target,
                    plan.ModifierPath,
                    plan.HitPointRegenBonus,
                    plan.OverrideTable,
                    plan.DurationTicks,
                    plan.RemoveOnDeath);
                return true;
            }
            if (plan.Kind == MonsterSelfEffectNodeKind.AuthoredAttributeModifier)
                return ApplyMonsterAuthoredAttributeModifier(
                    target,
                    plan.ModifierPath,
                    plan.Attributes,
                    plan.DurationTicks,
                    plan.RemoveOnDeath,
                    plan.StackRule,
                    unchecked((uint)Math.Max(0, plan.PowerLevelF32)),
                    sourceMonster.EntityId,
                    sourceMonster.SelectedActiveSkill?.Path,
                    plan.EffectPath);
            if (plan.Kind == MonsterSelfEffectNodeKind.EnemyDamageAura)
                return ApplyMonsterAuraModifier(sourceMonster, plan.Aura);
            Debug.LogError($"[MON-SKILL-VISUAL] source={sourceMonster.Name}#{sourceMonster.EntityId} target={target.Name}#{target.EntityId} effect={plan.EffectPath ?? "none"} modifier={plan.ModifierPath ?? "none"} durationTicks={plan.DurationTicks} sourceFunction=SpellEffect::doEffect@0x00545DD0");
            return true;
        }

        private bool PassesMonsterSelfEffectChance(Monster sourceMonster, Monster target, MonsterSelfEffectNode plan)
        {
            int chanceWire = Math.Clamp(plan?.ChanceWire ?? 0x6400, 0, 0x6400);
            if (chanceWire >= 0x6400)
                return true;
            MersenneTwister rng = GetRoomRngForMonster(sourceMonster);
            if (rng == null)
                return false;
            uint raw = RngLedger.Generate(rng, "room", $"MON-SELF:{plan.EffectPath ?? "effect"}:SpellEffect::CheckChance", sourceMonster.InstanceKey ?? sourceMonster.ZoneName, sourceMonster.EntityId);
            uint roll = raw % 0x6464u;
            bool passed = roll < (uint)chanceWire;
            Debug.LogError($"[MON-SKILL-CHANCE] source={sourceMonster.Name}#{sourceMonster.EntityId} target={target.Name}#{target.EntityId} effect={plan.EffectPath ?? "none"} chanceWire={chanceWire} raw=0x{raw:X8} roll={roll} passed={passed} sourceFunction=SpellEffect::CheckChance@0x00545FF0");
            return passed;
        }
    }
}
