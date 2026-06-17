using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DungeonRunners.Data;
using DungeonRunners.Engine;
using DungeonRunners.Networking;

namespace DungeonRunners.Combat
{
    public static class SpellDatabase
    {
        private static Dictionary<string, SpellData> _spells;
        private static bool _initialized;
        private static bool _authoredLoaded;
        private static readonly string[] PLAYER_ANIMATION_LIST_PATHS =
        {
            "avatar.races.humanmale.HumanMaleAnimations",
            "avatar.races.humanfemale.HumanFemaleAnimations",
            "HumanMaleAnimations",
            "HumanFemaleAnimations"
        };

        public static void Initialize()
        {
            EnsureStorage();
            EnsureAuthoredLoaded();
        }

        private static void EnsureStorage()
        {
            if (_initialized) return;
            _spells = new Dictionary<string, SpellData>(StringComparer.OrdinalIgnoreCase);
            _initialized = true;
        }

        private static void EnsureAuthoredLoaded()
        {
            EnsureStorage();
            if (_authoredLoaded) return;

            var gc = GCDatabase.Instance;
            if (gc == null || !gc.IsLoaded) return;

            foreach (string path in gc.RegisteredPaths)
            {
                if (!IsTopLevelGenericSkillPath(path)) continue;
                if (TryBuildAuthoredSpell(path, out SpellData spell))
                    Register(spell.ShortName, spell);
            }

            _authoredLoaded = true;
            LogLoadSummary();
        }

        private static bool IsTopLevelGenericSkillPath(string path)
        {
            string normalized = NormalizePath(path);
            const string prefix = "skills.generic.";
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
            string rest = normalized.Substring(prefix.Length);
            return rest.Length > 0 && rest.IndexOf('.') < 0;
        }

        private static bool TryBuildAuthoredSpell(string nameOrPath, out SpellData spell)
        {
            spell = null;

            var gc = GCDatabase.Instance;
            if (gc == null || !gc.IsLoaded)
                return false;

            string path = NormalizeSkillCandidate(nameOrPath);
            GCNode skill = gc.ResolveWithInheritance(path);
            if (skill == null)
            {
                string shortName = LastSegment(path);
                if (!string.IsNullOrWhiteSpace(shortName))
                {
                    path = "skills.generic." + shortName;
                    skill = gc.ResolveWithInheritance(path);
                }
            }

            if (skill == null)
                return false;

            GCNode desc = ResolveInheritedNode(skill.GetChild("Description") ?? skill);
            if (desc == null || !HasSkillShape(desc))
                return false;

            string skillId = ResolveSkillId(path, skill);
            string shortKey = LastSegment(skillId);
            string label = desc.GetString("Label", shortKey);
            string category = desc.GetString("Category", "");

            spell = new SpellData
            {
                ShortName = shortKey,
                SkillId = skillId,
                DisplayName = string.IsNullOrWhiteSpace(label) ? shortKey : label,
                AttackType = ParseAttackType(desc.GetString("WeaponType", null), AttackType.MAGIC),
                DamageType = ParseDamageElement(desc.GetString("DamageType", null), DamageElement.PHYSICAL),
                Cooldown = desc.GetFloat("CoolDown", desc.GetFloat("Cooldown", 0f)),
                Range = desc.GetInt("Range", 0),
                RepeatCount = Math.Max(1, desc.GetInt("RepeatCount", 1)),
                AnimationId = desc.GetInt("AnimationID", 0),
                ManaCostMod = desc.GetFloat("ManaCostMod", 0f),
                GoldValueMod = desc.GetFloat("GoldValueMod", 0f),
                MaxSkillLevel = desc.GetInt("MaxSkillLevel", 0),
                RequiredLevel = desc.GetInt("RequiredLevel", 1),
                RequiredLevelInc = desc.GetInt("RequiredLevelInc", 5),
                ProfessionType = desc.GetString("ProfessionType", ""),
                TargetType = desc.GetString("TargetType", ""),
                AdjustCooldownByWeapon = desc.GetBool("AdjustCooldownByWeapon", false)
            };

            string effectPath = desc.GetString("Effect", null);
            GCNode effectRoot = ResolveAuthoredNodeReference(effectPath, skill);
            if (effectRoot != null)
                ParseEffectTree(spell, effectRoot, skill);

            if (TryResolvePlayerAnimationTimingKey(spell.AnimationId, out int frames, out int trigger))
            {
                spell.AnimationLengthFrames = frames;
                spell.AnimationTriggerFrames = trigger;
            }

            spell.SkillCategory = ParseSkillCategory(category, spell);
            return true;
        }

        private static bool HasSkillShape(GCNode desc)
        {
            return desc.HasProperty("Effect") ||
                   desc.HasProperty("MaxSkillLevel") ||
                   desc.HasProperty("CoolDown") ||
                   desc.HasProperty("Cooldown") ||
                   desc.HasProperty("ManaCostMod") ||
                   desc.HasProperty("ProfessionType") ||
                   desc.HasProperty("TargetType") ||
                   desc.HasProperty("Category");
        }

        private static void ParseEffectTree(SpellData spell, GCNode effectRoot, GCNode skillContext)
        {
            GCNode aoe = FindFirstEffectNode(effectRoot, "SpellAOEEffect");
            if (aoe != null)
                ParseAoE(spell, aoe);

            GCNode weaponDamage = FindFirstEffectNode(effectRoot, "SpellWeaponDamageEffect");
            if (weaponDamage != null)
            {
                ParseWeaponDamage(spell, weaponDamage);
                GCNode inheritedWeaponDamage = ResolveInheritedNode(weaponDamage);
                string nestedEffectPath = inheritedWeaponDamage?.GetString("Effect", null);
                if (!string.IsNullOrWhiteSpace(nestedEffectPath))
                {
                    GCNode nestedEffect = ResolveAuthoredNodeReference(nestedEffectPath, skillContext);
                    if (nestedEffect != null)
                    {
                        if (spell.HasImmediateWeaponDamageEffect && string.IsNullOrWhiteSpace(spell.ProjectileEffectId))
                            spell.ProjectileEffectId = nestedEffectPath;
                        ParseModifier(spell, nestedEffect, skillContext);
                        GCNode nestedDamage = FindFirstEffectNode(nestedEffect, "SpellDamageEffect");
                        if (nestedDamage != null && spell.DamageMod <= 0f)
                            ParseDamage(spell, nestedDamage);
                        GCNode nestedKnockDown = FindFirstEffectNode(nestedEffect, "SpellKnockDownEffect");
                        if (nestedKnockDown != null)
                            ParseKnockDown(spell, nestedKnockDown);
                    }
                }
            }

            GCNode damage = FindFirstEffectNode(effectRoot, "SpellDamageEffect");
            if (damage != null)
                ParseDamage(spell, damage);

            GCNode chain = FindFirstEffectNode(effectRoot, "SpellChainEffect");
            if (chain != null)
                ParseChain(spell, chain);

            ParseProjectile(spell, effectRoot, skillContext);
            ParseModifier(spell, effectRoot, skillContext);

            GCNode knockDown = FindFirstEffectNode(effectRoot, "SpellKnockDownEffect");
            if (knockDown != null)
                ParseKnockDown(spell, knockDown);
            spell.HasFear = FindFirstEffectNode(effectRoot, "SpellFearEffect") != null;
            spell.HasSlow = FindFirstEffectNode(effectRoot, "SpellSlowEffect") != null;
        }

        private static void ParseAoE(SpellData spell, GCNode node)
        {
            node = ResolveInheritedNode(node);
            spell.IsAoE = true;
            spell.HasAoEEffect = true;
            spell.AoERadiusMin = GetFloatAny(node, "RadiusMin", "Radius", 0f);
            spell.AoERadiusMax = GetFloatAny(node, "RadiusMax", null, spell.AoERadiusMin);
            spell.AoERadiusInc = GetFloatAny(node, "RadiusInc", null, 0f);
            spell.AoERadius = spell.ResolveAoERadius(1);
            ParseNumTargets(spell, node);
        }

        private static void ParseWeaponDamage(SpellData spell, GCNode node)
        {
            node = ResolveInheritedNode(node);
            spell.IsWeaponSkill = true;
            spell.AttackType = ParseAttackType(node.GetString("AttackType", null), spell.AttackType);
            spell.DamageType = ParseDamageElement(node.GetString("DamageType", null), spell.DamageType);
            spell.DamageMod = spell.DamageMod > 0f ? spell.DamageMod : 1f;
            spell.SkillDamageModMin = node.GetInt("DamageModMin", spell.SkillDamageModMin);
            spell.SkillDamageModMax = node.HasProperty("DamageModMax") ? node.GetInt("DamageModMax", 0) : spell.SkillDamageModMax;
            spell.SkillDamageModInc = node.GetInt("DamageModInc", spell.SkillDamageModInc);
            spell.WeaponEffectDamageModMin = spell.SkillDamageModMin;
            spell.WeaponEffectDamageModMax = spell.SkillDamageModMax;
            spell.WeaponEffectDamageModInc = spell.SkillDamageModInc;
            spell.ARModMin = node.GetInt("ARModMin", spell.ARModMin);
            spell.ARModMax = node.HasProperty("ARModMax") ? node.GetInt("ARModMax", 0) : spell.ARModMax;
            spell.ARModInc = node.GetInt("ARModInc", spell.ARModInc);
            spell.HasImmediateWeaponDamageEffect = true;
            ParseNumTargets(spell, node);
            if (spell.NumTargetsMax > 1f || spell.NumTargetsMin > 1f)
                spell.IsAoE = true;
        }

        private static void ParseDamage(SpellData spell, GCNode node)
        {
            node = ResolveInheritedNode(node);
            spell.HasSpellDamageEffect = true;
            spell.AttackType = ParseAttackType(node.GetString("AttackType", null), spell.AttackType);
            spell.DamageType = ParseDamageElement(node.GetString("DamageType", null), spell.DamageType);
            spell.DamageMod = node.GetFloat("DamageMod", spell.DamageMod);
            spell.DamageVolatility = node.GetFloat("DamageVolatility", spell.DamageVolatility);
            spell.CriticalChance = node.GetFloat("CriticalChance", spell.CriticalChance);
            spell.ChanceF32 = ResolveSpellEffectChanceWire(node);
        }

        private static void ParseKnockDown(SpellData spell, GCNode node)
        {
            node = ResolveInheritedNode(node);
            spell.HasSpellKnockDownEffect = true;
            spell.SpellKnockDownStrengthMin = node.GetInt("StrengthMin", spell.SpellKnockDownStrengthMin);
            spell.SpellKnockDownStrengthMax = node.HasProperty("StrengthMax") ? node.GetInt("StrengthMax", spell.SpellKnockDownStrengthMin) : spell.SpellKnockDownStrengthMax;
            spell.SpellKnockDownStrengthInc = node.GetInt("StrengthInc", spell.SpellKnockDownStrengthInc);
            spell.SpellKnockDownChanceF32 = ResolveSpellEffectChanceWire(node);
        }

        private static void ParseChain(SpellData spell, GCNode node)
        {
            node = ResolveInheritedNode(node);
            spell.IsChainSpell = true;
            spell.NumChains = node.GetInt("NumChains", node.GetInt("NumTargets", spell.NumChains));
            spell.ChainRange = node.GetInt("ChainRange", node.GetInt("Range", spell.ChainRange));
        }

        private static void ParseProjectile(SpellData spell, GCNode effectRoot, GCNode skillContext)
        {
            GCNode projectileEffect = FindFirstEffectNode(effectRoot, "SpellProjectileEffect");
            if (projectileEffect == null)
                return;

            projectileEffect = ResolveInheritedNode(projectileEffect);
            string projectilePath = projectileEffect.GetString("Projectile", null);
            GCNode projectile = ResolveAuthoredNodeReference(projectilePath, skillContext);
            GCNode projectileDesc = ResolveInheritedNode(projectile?.GetChild("Description") ?? projectile);
            if (projectileDesc != null)
            {
                spell.ProjectileSpeed = projectileDesc.GetFloat("ProjectileSpeed", spell.ProjectileSpeed);
                spell.ProjectileSize = projectileDesc.GetFloat("ProjectileSize", spell.ProjectileSize);
                spell.ProjectileLifespan = projectileDesc.GetFloat("ProjectileLifespan", spell.ProjectileLifespan);
                spell.ProjectileEffectId = projectileDesc.GetString("Effect", spell.ProjectileEffectId);
            }

            spell.RepeatCount = Math.Max(1, projectileEffect.GetInt("RepeatCount", spell.RepeatCount));
            if (string.IsNullOrWhiteSpace(spell.ProjectileEffectId))
                return;

            GCNode projectileRuntimeEffect = ResolveAuthoredNodeReference(spell.ProjectileEffectId, projectile ?? skillContext);
            if (projectileRuntimeEffect == null)
                return;

            GCNode damage = FindFirstEffectNode(projectileRuntimeEffect, "SpellDamageEffect");
            if (damage != null)
                ParseDamage(spell, damage);

            ParseModifier(spell, projectileRuntimeEffect, projectile ?? skillContext);
        }

        private static void ParseModifier(SpellData spell, GCNode effectRoot, GCNode skillContext)
        {
            GCNode modEffect = FindFirstEffectNode(effectRoot, "SpellModEffect");
            if (modEffect == null)
                return;

            modEffect = ResolveInheritedNode(modEffect);
            string modifierPath = modEffect.GetString("Modifier", null);
            if (string.IsNullOrWhiteSpace(modifierPath))
                return;

            spell.ProjectileModifierId = modifierPath;
            spell.ProjectileModifierDuration = modEffect.GetFloat("Duration", spell.ProjectileModifierDuration);

            GCNode modifier = ResolveAuthoredNodeReference(modifierPath, skillContext);
            GCNode modifierDesc = ResolveInheritedNode(modifier?.GetChild("Description") ?? modifier);
            if (modifierDesc == null)
                return;

            spell.ProjectileModifierFrequency = modifierDesc.GetFloat("Frequency", spell.ProjectileModifierFrequency);
            spell.ProjectileModifierStackRule = modifierDesc.GetString("StackRule", spell.ProjectileModifierStackRule);
            string modifierEffectPath = modifierDesc.GetString("Effect", null);
            if (string.IsNullOrWhiteSpace(modifierEffectPath))
                return;

            spell.ProjectileModifierEffectId = modifierEffectPath;
            GCNode modifierEffect = ResolveAuthoredNodeReference(modifierEffectPath, modifier ?? skillContext);
            GCNode damage = FindFirstEffectNode(modifierEffect, "SpellDamageEffect");
            if (damage == null)
                return;

            damage = ResolveInheritedNode(damage);
            spell.ProjectileModifierAttackType = ParseAttackType(damage.GetString("AttackType", null), spell.AttackType);
            spell.ProjectileModifierDamageType = ParseDamageElement(damage.GetString("DamageType", null), spell.DamageType);
            spell.ProjectileModifierDamageMod = damage.GetFloat("DamageMod", spell.ProjectileModifierDamageMod);
            spell.ProjectileModifierDamageVolatility = damage.GetFloat("DamageVolatility", spell.ProjectileModifierDamageVolatility);
            spell.ProjectileModifierCriticalChance = damage.GetFloat("CriticalChance", spell.ProjectileModifierCriticalChance);
        }

        private static IEnumerable<GCNode> EnumerateDirectEffectChildren(GCNode node)
        {
            if (node == null)
                yield break;

            foreach (GCNode child in node.AnonymousChildren)
                yield return child;

            foreach (string childName in node.ChildOrder)
                if (node.Children.TryGetValue(childName, out GCNode child))
                    yield return child;

            foreach (var childEntry in node.Children)
            {
                if (node.ChildOrder.Contains(childEntry.Key))
                    continue;
                yield return childEntry.Value;
            }
        }

        private static void ParseNumTargets(SpellData spell, GCNode node)
        {
            spell.NumTargetsMin = GetFloatAny(node, "NumTargetsMin", "NumTargets", spell.NumTargetsMin);
            spell.NumTargetsMax = GetFloatAny(node, "NumTargetsMax", null, spell.NumTargetsMax);
            spell.NumTargetsInc = GetFloatAny(node, "NumTargetsInc", null, spell.NumTargetsInc);
        }

        private static SkillCategory ParseSkillCategory(string category, SpellData spell)
        {
            string value = CompactKey(category);
            if (value.Equals("Offensive", StringComparison.OrdinalIgnoreCase)) return spell.IsWeaponSkill ? SkillCategory.WeaponSkill : SkillCategory.Offensive;
            if (value.Equals("WeaponSkill", StringComparison.OrdinalIgnoreCase)) return SkillCategory.WeaponSkill;
            if (value.Equals("CrowdControl", StringComparison.OrdinalIgnoreCase)) return SkillCategory.CrowdControl;
            if (value.Equals("Debuff", StringComparison.OrdinalIgnoreCase) || value.Equals("Curse", StringComparison.OrdinalIgnoreCase) || value.Equals("Curses", StringComparison.OrdinalIgnoreCase)) return SkillCategory.Debuff;
            if (value.Equals("Heal", StringComparison.OrdinalIgnoreCase) || value.Equals("Healing", StringComparison.OrdinalIgnoreCase)) return SkillCategory.Heal;
            if (value.Equals("Summon", StringComparison.OrdinalIgnoreCase) || value.Equals("Summoning", StringComparison.OrdinalIgnoreCase)) return SkillCategory.Summon;
            if (value.Equals("Passive", StringComparison.OrdinalIgnoreCase)) return SkillCategory.Passive;
            if (spell.IsWeaponSkill) return SkillCategory.WeaponSkill;
            if (spell.HasAnyDamage) return SkillCategory.Offensive;
            string name = spell.ShortName ?? "";
            if (name.IndexOf("Passive", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Trait", StringComparison.OrdinalIgnoreCase) >= 0)
                return SkillCategory.Passive;
            return SkillCategory.Utility;
        }

        public static bool TryResolvePlayerAnimationTiming(int animationId, PlayerState state, out int frames, out int trigger)
        {
            int animationKey = ResolvePlayerAnimationKey(animationId, state);
            return TryResolvePlayerAnimationTimingKey(animationKey, out frames, out trigger);
        }

        public static bool TryResolvePlayerAnimationSourceOffset(int animationId, PlayerState state, out Vector3 sourceOffset)
        {
            sourceOffset = Vector3.zero;
            int animationKey = ResolvePlayerAnimationKey(animationId, state);
            return TryResolvePlayerAnimationSourceOffsetKey(animationKey, out sourceOffset);
        }

        private static int ResolvePlayerAnimationKey(int animationId, PlayerState state)
        {
            if (animationId <= 0)
                return animationId;
            int weaponClassId = DamageResolver.ResolveWeaponClassId(state);
            if (weaponClassId <= 0)
                return animationId;
            return animationId + weaponClassId * 100;
        }

        private static bool TryResolvePlayerAnimationTimingKey(int animationKey, out int frames, out int trigger)
        {
            frames = 0;
            trigger = 0;
            if (animationKey <= 0)
                return false;

            var gc = GCDatabase.Instance;
            if (gc == null || !gc.IsLoaded)
                return false;

            var matches = new List<(int frames, int trigger)>();
            var seen = new HashSet<GCNode>();
            foreach (string path in PLAYER_ANIMATION_LIST_PATHS)
            {
                GCNode animations = gc.ResolveWithInheritance(path);
                if (animations == null)
                    continue;
                if (!seen.Add(animations))
                    continue;
                AppendAnimationTimingMatches(animations, animationKey, matches);
            }

            if (matches.Count == 0)
            {
                foreach (string path in gc.RegisteredPaths)
                {
                    string normalized = NormalizePath(path);
                    if (!normalized.StartsWith("avatar.races.", StringComparison.OrdinalIgnoreCase) ||
                        !normalized.EndsWith("Animations", StringComparison.OrdinalIgnoreCase))
                        continue;

                    GCNode animations = gc.ResolveWithInheritance(path);
                    if (animations == null)
                        continue;
                    if (!seen.Add(animations))
                        continue;
                    AppendAnimationTimingMatches(animations, animationKey, matches);
                }
            }

            if (matches.Count == 0)
                return false;

            var first = matches[0];
            if (matches.Any(m => m.frames != first.frames || m.trigger != first.trigger))
                return false;

            frames = first.frames;
            trigger = first.trigger;
            return true;
        }

        private static bool TryResolvePlayerAnimationSourceOffsetKey(int animationKey, out Vector3 sourceOffset)
        {
            sourceOffset = Vector3.zero;
            if (animationKey <= 0)
                return false;

            var gc = GCDatabase.Instance;
            if (gc == null || !gc.IsLoaded)
                return false;

            var matches = new List<Vector3>();
            var seen = new HashSet<GCNode>();
            foreach (string path in PLAYER_ANIMATION_LIST_PATHS)
            {
                GCNode animations = gc.ResolveWithInheritance(path);
                if (animations == null)
                    continue;
                if (!seen.Add(animations))
                    continue;
                AppendAnimationSourceOffsetMatches(animations, animationKey, matches);
            }

            if (matches.Count == 0)
            {
                foreach (string path in gc.RegisteredPaths)
                {
                    string normalized = NormalizePath(path);
                    if (!normalized.StartsWith("avatar.races.", StringComparison.OrdinalIgnoreCase) ||
                        !normalized.EndsWith("Animations", StringComparison.OrdinalIgnoreCase))
                        continue;

                    GCNode animations = gc.ResolveWithInheritance(path);
                    if (animations == null)
                        continue;
                    if (!seen.Add(animations))
                        continue;
                    AppendAnimationSourceOffsetMatches(animations, animationKey, matches);
                }
            }

            if (matches.Count == 0)
                return false;

            Vector3 first = matches[0];
            if (matches.Any(m => Math.Abs(m.x - first.x) > 0.001f || Math.Abs(m.y - first.y) > 0.001f || Math.Abs(m.z - first.z) > 0.001f))
                return false;

            sourceOffset = first;
            return true;
        }

        private static void AppendAnimationTimingMatches(GCNode animations, int animationKey, List<(int frames, int trigger)> matches)
        {
            foreach (GCNode row in EnumerateAnimationRows(animations))
            {
                if (!row.HasProperty("ID"))
                    continue;
                int rowId = row.GetInt("ID", -1);
                if (rowId < 0)
                    continue;
                if (!MatchesAnimationKey(rowId, animationKey))
                    continue;
                int rowFrames = row.GetInt("NumFrames", 0);
                int rowTrigger = row.GetInt("TriggerTime", 0);
                if (rowFrames > 0 && rowTrigger > 0)
                    matches.Add((rowFrames, rowTrigger));
            }
        }

        private static void AppendAnimationSourceOffsetMatches(GCNode animations, int animationKey, List<Vector3> matches)
        {
            foreach (GCNode row in EnumerateAnimationRows(animations))
            {
                if (!row.HasProperty("ID"))
                    continue;
                int rowId = row.GetInt("ID", -1);
                if (rowId < 0)
                    continue;
                if (!MatchesAnimationKey(rowId, animationKey))
                    continue;
                if (TryParseSourceOffset(row, out Vector3 sourceOffset))
                    matches.Add(sourceOffset);
            }
        }

        private static bool TryParseSourceOffset(GCNode animation, out Vector3 sourceOffset)
        {
            sourceOffset = Vector3.zero;
            string raw = animation?.GetString("SourceOffset", null);
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            int marker = raw.IndexOf("//", StringComparison.Ordinal);
            if (marker >= 0)
                raw = raw.Substring(0, marker);

            string[] parts = raw.Split(',');
            if (parts.Length < 3)
                return false;

            if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x))
                return false;
            if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                return false;
            if (!float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                return false;

            sourceOffset = new Vector3(x, y, z);
            return true;
        }

        private static bool MatchesAnimationKey(int rowId, int animationKey)
        {
            if (rowId < 0 || animationKey <= 0)
                return false;
            return rowId == animationKey;
        }

        private static IEnumerable<GCNode> EnumerateAnimationRows(GCNode animations)
        {
            foreach (GCNode row in animations.AnonymousChildren)
                yield return ResolveInheritedNode(row);
            foreach (GCNode row in animations.Children.Values)
                yield return ResolveInheritedNode(row);
        }

        private static GCNode ResolveAuthoredNodeReference(string path, GCNode contextRoot)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string normalized = NormalizePath(path);
            var gc = GCDatabase.Instance;
            GCNode node = gc?.ResolveWithInheritance(normalized);
            if (node != null)
                return node;

            if (contextRoot == null)
                return null;

            string rootName = contextRoot.Name;
            if (!string.IsNullOrWhiteSpace(rootName) &&
                normalized.StartsWith(rootName + ".", StringComparison.OrdinalIgnoreCase))
            {
                string subPath = normalized.Substring(rootName.Length + 1);
                return ResolveChildPath(contextRoot, subPath);
            }

            int dot = normalized.IndexOf('.');
            while (dot >= 0 && dot + 1 < normalized.Length)
            {
                string suffix = normalized.Substring(dot + 1);
                GCNode child = ResolveChildPath(contextRoot, suffix);
                if (child != null)
                    return child;
                dot = normalized.IndexOf('.', dot + 1);
            }

            return null;
        }

        private static GCNode ResolveChildPath(GCNode root, string dottedPath)
        {
            if (root == null || string.IsNullOrWhiteSpace(dottedPath))
                return root;

            GCNode current = root;
            foreach (string part in dottedPath.Split('.'))
            {
                if (current == null || string.IsNullOrWhiteSpace(part))
                    return null;
                current = current.GetChild(part);
            }
            return current;
        }

        private static GCNode FindFirstEffectNode(GCNode root, string clientExtends)
        {
            foreach (GCNode node in EnumerateEffectTree(root, 0, new HashSet<GCNode>()))
                if (AuthoredExtends(node, clientExtends))
                    return node;
            return null;
        }

        private static IEnumerable<GCNode> EnumerateEffectTree(GCNode node, int depth, HashSet<GCNode> seen)
        {
            if (node == null || depth > 48)
                yield break;
            if (!seen.Add(node))
                yield break;

            GCNode resolved = ResolveInheritedNode(node);
            yield return resolved;

            foreach (GCNode child in resolved.AnonymousChildren)
                foreach (GCNode inner in EnumerateEffectTree(child, depth + 1, seen))
                    yield return inner;

            foreach (string childName in resolved.ChildOrder)
                if (resolved.Children.TryGetValue(childName, out GCNode child))
                    foreach (GCNode inner in EnumerateEffectTree(child, depth + 1, seen))
                        yield return inner;

            foreach (var childEntry in resolved.Children)
            {
                if (resolved.ChildOrder.Contains(childEntry.Key))
                    continue;
                foreach (GCNode inner in EnumerateEffectTree(childEntry.Value, depth + 1, seen))
                    yield return inner;
            }
        }

        private static bool AuthoredExtends(GCNode node, string clientExtends)
        {
            return AuthoredExtends(node, clientExtends, 0);
        }

        private static bool AuthoredExtends(GCNode node, string clientExtends, int depth)
        {
            if (node == null || string.IsNullOrWhiteSpace(clientExtends) || depth > 32)
                return false;
            string ext = NormalizePath(node.Extends ?? string.Empty);
            if (string.IsNullOrWhiteSpace(ext))
                return false;
            if (string.Equals(ext, clientExtends, StringComparison.OrdinalIgnoreCase))
                return true;
            if (ext.EndsWith("." + clientExtends, StringComparison.OrdinalIgnoreCase))
                return true;

            GCNode parent = GCDatabase.Instance?.ResolveWithInheritance(ext);
            return parent != null && AuthoredExtends(parent, clientExtends, depth + 1);
        }

        private static GCNode ResolveInheritedNode(GCNode node)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.Extends))
                return node;

            GCNode parent = GCDatabase.Instance?.ResolveWithInheritance(NormalizePath(node.Extends));
            if (parent == null)
                return node;

            return MergeNodes(parent, node);
        }

        private static GCNode MergeNodes(GCNode parent, GCNode child)
        {
            var merged = new GCNode
            {
                Name = child.Name,
                Extends = child.Extends ?? parent.Extends,
                IsStatic = child.IsStatic || parent.IsStatic,
                IsAnonymous = child.IsAnonymous || parent.IsAnonymous,
                SourceFile = child.SourceFile
            };

            foreach (var propertyEntry in parent.Properties)
                merged.Properties[propertyEntry.Key] = propertyEntry.Value;
            foreach (var propertyEntry in child.Properties)
                merged.Properties[propertyEntry.Key] = propertyEntry.Value;

            foreach (var childEntry in parent.Children)
                merged.Children[childEntry.Key] = childEntry.Value;
            foreach (string childName in parent.ChildOrder)
                if (!merged.ChildOrder.Contains(childName))
                    merged.ChildOrder.Add(childName);

            foreach (var childEntry in child.Children)
            {
                if (merged.Children.ContainsKey(childEntry.Key))
                    merged.Children[childEntry.Key] = MergeNodes(merged.Children[childEntry.Key], childEntry.Value);
                else
                    merged.Children[childEntry.Key] = childEntry.Value;
                if (!merged.ChildOrder.Contains(childEntry.Key))
                    merged.ChildOrder.Add(childEntry.Key);
            }

            merged.AnonymousChildren.AddRange(parent.AnonymousChildren);
            merged.AnonymousChildren.AddRange(child.AnonymousChildren);
            return merged;
        }

        private static AttackType ParseAttackType(string value, AttackType fallback)
        {
            string key = CompactKey(value);
            if (key.Equals("Melee", StringComparison.OrdinalIgnoreCase)) return AttackType.MELEE;
            if (key.Equals("Magic", StringComparison.OrdinalIgnoreCase)) return AttackType.MAGIC;
            if (key.Equals("Ranged", StringComparison.OrdinalIgnoreCase) || key.Equals("Range", StringComparison.OrdinalIgnoreCase)) return AttackType.RANGED;
            return fallback;
        }

        private static DamageElement ParseDamageElement(string value, DamageElement fallback)
        {
            string key = CompactKey(value);
            if (key.Equals("Divine", StringComparison.OrdinalIgnoreCase)) return DamageElement.DIVINE;
            if (key.Equals("Fire", StringComparison.OrdinalIgnoreCase)) return DamageElement.FIRE;
            if (key.Equals("Ice", StringComparison.OrdinalIgnoreCase) || key.Equals("Cold", StringComparison.OrdinalIgnoreCase)) return DamageElement.ICE;
            if (key.Equals("Poison", StringComparison.OrdinalIgnoreCase)) return DamageElement.POISON;
            if (key.Equals("Shadow", StringComparison.OrdinalIgnoreCase)) return DamageElement.SHADOW;
            if (key.Equals("Physical", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Crushing", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Piercing", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Slashing", StringComparison.OrdinalIgnoreCase))
                return DamageElement.PHYSICAL;
            return fallback;
        }

        private static int ResolveSpellEffectChanceWire(GCNode node)
        {
            if (node == null)
                return 0x6400;
            float chance = node.GetFloat("Chance", 100f);
            if (float.IsNaN(chance) || float.IsInfinity(chance))
                chance = 100f;
            return Mathf.Clamp(Mathf.RoundToInt(chance * 256f), 0, 0x6400);
        }

        private static float GetFloatAny(GCNode node, string primary, string secondary, float fallback)
        {
            if (node == null)
                return fallback;
            if (!string.IsNullOrWhiteSpace(primary) && node.HasProperty(primary))
                return node.GetFloat(primary, fallback);
            if (!string.IsNullOrWhiteSpace(secondary) && node.HasProperty(secondary))
                return node.GetFloat(secondary, fallback);
            return fallback;
        }

        private static string ResolveSkillId(string path, GCNode skill)
        {
            string normalized = NormalizePath(path);
            if (normalized.StartsWith("skills.generic.", StringComparison.OrdinalIgnoreCase))
                return normalized;
            string name = skill?.Name;
            if (!string.IsNullOrWhiteSpace(name))
                return "skills.generic." + name;
            return normalized;
        }

        private static string NormalizeSkillCandidate(string nameOrPath)
        {
            string normalized = NormalizePath(nameOrPath);
            if (string.IsNullOrWhiteSpace(normalized))
                return normalized;
            if (normalized.IndexOf('.') < 0)
                return "skills.generic." + normalized;
            return normalized;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "";
            string normalized = path.Trim().Trim('"').Replace('\\', '.').Replace('/', '.');
            if (normalized.EndsWith(".gc", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(0, normalized.Length - 3);
            while (normalized.Contains("..", StringComparison.Ordinal))
                normalized = normalized.Replace("..", ".");
            return normalized.Trim('.');
        }

        private static string LastSegment(string path)
        {
            string normalized = NormalizePath(path);
            int dot = normalized.LastIndexOf('.');
            return dot >= 0 && dot + 1 < normalized.Length ? normalized.Substring(dot + 1) : normalized;
        }

        private static string CompactKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";
            char[] buffer = new char[value.Length];
            int count = 0;
            foreach (char ch in value)
                if (char.IsLetterOrDigit(ch))
                    buffer[count++] = ch;
            return new string(buffer, 0, count);
        }

        private static void Register(string shortName, SpellData data)
        {
            if (data == null)
                return;
            if (string.IsNullOrWhiteSpace(data.ShortName))
                data.ShortName = shortName;
            RegisterKey(data.ShortName, data);
            RegisterKey(data.SkillId, data);
            RegisterKey(NormalizePath(data.SkillId), data);
            RegisterKey((data.SkillId ?? "").Replace('.', '/'), data);
            RegisterKey(CompactKey(data.DisplayName), data);
        }

        private static void RegisterKey(string key, SpellData data)
        {
            if (string.IsNullOrWhiteSpace(key) || data == null)
                return;
            _spells[key] = data;
        }

        private static void LogLoadSummary()
        {
            int offensive = 0, weapon = 0, utility = 0, passive = 0, summon = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SpellData spell in GetAllSpells())
            {
                if (!seen.Add(spell.SkillId ?? spell.ShortName)) continue;
                switch (spell.SkillCategory)
                {
                    case SkillCategory.Offensive: offensive++; break;
                    case SkillCategory.WeaponSkill: weapon++; break;
                    case SkillCategory.Passive: passive++; break;
                    case SkillCategory.Summon: summon++; break;
                    default: utility++; break;
                }
            }
            Debug.LogError($"[SPELLDB] source=gc loaded={seen.Count} offensive={offensive} weapon={weapon} utility={utility} summon={summon} passive={passive}");
        }

        public static SpellData GetSpell(string name)
        {
            EnsureAuthoredLoaded();
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (_spells.TryGetValue(name, out SpellData data))
                return data;

            string normalized = NormalizePath(name);
            if (_spells.TryGetValue(normalized, out data))
                return data;

            string compact = CompactKey(name);
            if (_spells.TryGetValue(compact, out data))
                return data;

            if (TryBuildAuthoredSpell(name, out data))
            {
                Register(data.ShortName, data);
                return data;
            }

            return null;
        }

        public static IEnumerable<SpellData> GetAllSpells()
        {
            EnsureAuthoredLoaded();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var spellEntry in _spells)
            {
                string key = spellEntry.Value.SkillId ?? spellEntry.Value.ShortName;
                if (seen.Add(key))
                    yield return spellEntry.Value;
            }
        }

        public static IEnumerable<SpellData> GetOffensiveSpells()
        {
            foreach (SpellData spell in GetAllSpells())
            {
                if ((spell.SkillCategory == SkillCategory.Offensive ||
                     spell.SkillCategory == SkillCategory.WeaponSkill) && spell.HasAnyDamage)
                    yield return spell;
            }
        }

        public static bool IsDamageSkill(string name)
        {
            SpellData spell = GetSpell(name);
            if (spell == null) return false;
            return (spell.SkillCategory == SkillCategory.Offensive ||
                    spell.SkillCategory == SkillCategory.WeaponSkill) && spell.HasAnyDamage;
        }

    }

    public class SpellData
    {
        public string ShortName;
        public string SkillId;
        public string DisplayName;
        public AttackType AttackType;
        public DamageElement DamageType;
        public int ChanceF32 = 0x6400;
        public float DamageMod;
        public float DamageVolatility;
        public float CriticalChance;
        public float Cooldown;
        public int Range;
        public float ProjectileSpeed;
        public float ProjectileSize;
        public float ProjectileLifespan;
        public int RepeatCount = 1;
        public int AnimationId;
        public int AnimationLengthFrames = 30;
        public int AnimationTriggerFrames;
        public float ManaCostMod;
        public float GoldValueMod;
        public int MaxSkillLevel;
        public int RequiredLevel;
        public int RequiredLevelInc;
        public string ProfessionType;
        public string TargetType;
        public SkillCategory SkillCategory;
        public bool IsAoE;
        public bool HasAoEEffect;
        public bool IsChainSpell;
        public int NumChains;
        public int ChainRange;
        public bool IsWeaponSkill;
        public bool HasSpellDamageEffect;
        public bool HasSpellKnockDownEffect;
        public int SpellKnockDownStrengthMin;
        public int SpellKnockDownStrengthMax;
        public int SpellKnockDownStrengthInc;
        public int SpellKnockDownChanceF32 = 0x6400;
        public bool HasFear;
        public bool HasSlow;
        public bool AdjustCooldownByWeapon;
        public bool HasImmediateWeaponDamageEffect;
        public string ProjectileEffectId;
        public string ProjectileModifierId;
        public string ProjectileModifierEffectId;
        public AttackType? ProjectileModifierAttackType;
        public DamageElement? ProjectileModifierDamageType;
        public float ProjectileModifierDuration;
        public float ProjectileModifierFrequency;
        public string ProjectileModifierStackRule;
        public float ProjectileModifierDamageMod;
        public float ProjectileModifierDamageVolatility;
        public float ProjectileModifierCriticalChance;
        public int ARModMin;
        public int ARModMax;
        public int ARModInc;
        public int WeaponEffectDamageModMin;
        public int WeaponEffectDamageModMax;
        public int WeaponEffectDamageModInc;
        public int SkillDamageModMin;
        public int SkillDamageModMax;
        public int SkillDamageModInc;
        public float NumTargetsMin;
        public float NumTargetsMax;
        public float NumTargetsInc;
        public float AoERadius;
        public float AoERadiusMin;
        public float AoERadiusMax;
        public float AoERadiusInc;

        public bool HasDirectDamageEffect => HasSpellDamageEffect;

        public bool HasDeferredProjectileModifierDamage =>
            !string.IsNullOrEmpty(ProjectileModifierEffectId) &&
            ProjectileModifierDamageMod > 0f &&
            ProjectileModifierFrequency > 0f;

        public bool HasProjectileModifierDamage => HasDeferredProjectileModifierDamage;
        public bool HasAnyDamage => HasDirectDamageEffect || HasImmediateWeaponDamageEffect || HasDeferredProjectileModifierDamage;
        public AttackType EffectiveProjectileModifierAttackType => ProjectileModifierAttackType ?? AttackType;
        public DamageElement EffectiveProjectileModifierDamageType => ProjectileModifierDamageType ?? DamageType;

        public float ResolveAoERadius(int skillLevel)
        {
            float min = AoERadiusMin > 0f ? AoERadiusMin : AoERadius;
            if (min <= 0f)
                return 0f;
            float value = min + Math.Max(1, skillLevel) * AoERadiusInc;
            float max = AoERadiusMax > 0f ? AoERadiusMax : 0f;
            if (max > 0f && value > max)
                value = max;
            return value;
        }

        public int ResolveNumTargets(int skillLevel)
        {
            if (NumTargetsMin <= 0f && NumTargetsMax <= 0f)
                return int.MaxValue;
            float value = NumTargetsMin + Math.Max(1, skillLevel) * NumTargetsInc;
            if (NumTargetsMax > 0f && value > NumTargetsMax)
                value = NumTargetsMax;
            int result = (int)value;
            return result < 1 ? 1 : result;
        }

        public int ResolveSpellKnockDownStrength(int skillLevel)
        {
            int level = Math.Max(0, skillLevel);
            int value = SpellKnockDownStrengthMin + SpellKnockDownStrengthInc * level;
            if (SpellKnockDownStrengthMax > 0 && value > SpellKnockDownStrengthMax)
                value = SpellKnockDownStrengthMax;
            return value;
        }
    }

    public enum AttackType { MELEE, MAGIC, RANGED }
    public enum DamageElement { PHYSICAL, DIVINE, FIRE, ICE, POISON, SHADOW }
    public enum SkillCategory
    {
        Offensive,
        WeaponSkill,
        CrowdControl,
        Debuff,
        Heal,
        Utility,
        Summon,
        Passive
    }
}
