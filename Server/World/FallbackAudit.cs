using System;
using System.Collections.Generic;
using System.Linq;
using DungeonRunners.Data;
using DungeonRunners.Engine;

namespace DungeonRunners.Core
{
    public static class FallbackAudit
    {
        private static bool _startupFallbackLogged;

        public static void RunStartupCoverage()
        {
            if (_startupFallbackLogged)
                return;
            _startupFallbackLogged = true;

            try
            {
                ReportCreatureCoverage();
                ReportKnownDungeonCreature("dew-valley-pup", "world.dungeon00.mob.melee01.rank1", "PIERCING", null, float.NaN);
                ReportKnownDungeonCreature("whisker-ratling", "world.dungeon00.mob.melee03.rank1", null, "UnarmedWeapon", 6f);
                ReportClassDamageModCoverage();
                ReportRangerStarterDamageLane();
                ReportChestGeneratorCoverage();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AUTHORED-COVERAGE] area=startup status=error message='{Sanitize(ex.Message)}'");
            }
        }

        private static void ReportCreatureCoverage()
        {
            var creatures = global::DatabaseLoader.Creatures ?? new List<global::CreatureData>();
            int total = 0;
            int unresolved = 0;
            int missingAnimations = 0;
            int missingManipulators = 0;
            int emptyManipulators = 0;
            int dbManipulatorFallbackCandidates = 0;
            var missingAnimSamples = new List<string>();
            var missingManipSamples = new List<string>();

            foreach (var creature in creatures)
            {
                if (creature == null || string.IsNullOrWhiteSpace(creature.gcType))
                    continue;
                total++;
                GCNode node = GCDatabase.Instance.ResolveWithInheritance(creature.gcType);
                if (node == null)
                {
                    unresolved++;
                    AddSample(missingAnimSamples, creature.gcType);
                    AddSample(missingManipSamples, creature.gcType);
                    continue;
                }

                string animationsPath = GetEffectiveString(node, "Animations");
                bool hasAnimations = !string.IsNullOrWhiteSpace(animationsPath) &&
                                     GCDatabase.Instance.ResolveWithInheritance(animationsPath) != null;
                if (!hasAnimations)
                {
                    missingAnimations++;
                    AddSample(missingAnimSamples, creature.gcType);
                }

                GCNode manipulators = node.GetChild("Manipulators");
                bool hasManipulatorNode = manipulators != null;
                bool hasManipulatorContent = hasManipulatorNode &&
                    ((manipulators.Children != null && manipulators.Children.Count > 0) ||
                     (manipulators.AnonymousChildren != null && manipulators.AnonymousChildren.Count > 0));
                if (!hasManipulatorNode)
                {
                    missingManipulators++;
                    AddSample(missingManipSamples, creature.gcType);
                }
                else if (!hasManipulatorContent)
                {
                    emptyManipulators++;
                    AddSample(missingManipSamples, creature.gcType + ":empty");
                }

                if (!hasManipulatorContent && creature.manipulators != null && creature.manipulators.Count > 0)
                    dbManipulatorFallbackCandidates++;
            }

            Debug.LogError($"[AUTHORED-COVERAGE] area=creatures total={total} unresolved={unresolved} missingAnimations={missingAnimations} missingManipulators={missingManipulators} emptyManipulators={emptyManipulators} dbManipulatorFallbackCandidates={dbManipulatorFallbackCandidates} missingAnimationSamples='{string.Join(",", missingAnimSamples)}' missingManipulatorSamples='{string.Join(",", missingManipSamples)}'");
        }

        private static void ReportKnownDungeonCreature(string key, string path, string expectedDamageType, string expectedPrimaryContains, float expectedRange)
        {
            GCNode node = GCDatabase.Instance.ResolveWithInheritance(path);
            GCNode manipulators = node?.GetChild("Manipulators");
            GCNode primary = FindPrimaryManipulator(manipulators);
            GCNode primaryInherited = !string.IsNullOrWhiteSpace(primary?.Extends)
                ? GCDatabase.Instance.ResolveWithInheritance(primary.Extends)
                : null;
            string animationsPath = GetEffectiveString(node, "Animations");
            bool animationsResolved = !string.IsNullOrWhiteSpace(animationsPath) &&
                                      GCDatabase.Instance.ResolveWithInheritance(animationsPath) != null;
            string primaryPath = primary?.Extends ?? "";
            string damageType = GetEffectiveString(primary, "DamageType");
            if (string.IsNullOrWhiteSpace(damageType))
                damageType = GetEffectiveString(primaryInherited, "DamageType");
            float range = GetEffectiveFloat(primary, "Range", float.NaN);
            if (float.IsNaN(range))
                range = GetEffectiveFloat(primaryInherited, "Range", float.NaN);
            bool hasDbFallback = false;
            var dbCreature = global::DatabaseLoader.FindCreature(path);
            if (dbCreature == null && key.Equals("dew-valley-pup", StringComparison.OrdinalIgnoreCase))
                dbCreature = global::DatabaseLoader.FindCreature("creatures.forestCreatures.Warg.Basic.Pup");
            if (dbCreature == null && key.Equals("whisker-ratling", StringComparison.OrdinalIgnoreCase))
                dbCreature = global::DatabaseLoader.FindCreature("creatures.whiskers.broodling.Basic.Grunt");
            hasDbFallback = dbCreature?.manipulators != null && dbCreature.manipulators.Count > 0;

            bool damageMatches = string.IsNullOrWhiteSpace(expectedDamageType) ||
                                 string.Equals(damageType, expectedDamageType, StringComparison.OrdinalIgnoreCase);
            bool primaryMatches = string.IsNullOrWhiteSpace(expectedPrimaryContains) ||
                                  primaryPath.IndexOf(expectedPrimaryContains, StringComparison.OrdinalIgnoreCase) >= 0;
            bool rangeMatches = float.IsNaN(expectedRange) ||
                                (!float.IsNaN(range) && Math.Abs(range - expectedRange) <= 0.01f);
            if (!damageMatches || !primaryMatches || !rangeMatches)
            {
                RuntimeEvidence.LogFallbackHit(
                    "spawn-manipulator",
                    key + "-coverage-mismatch",
                    $"path='{path}' primary='{Sanitize(primaryPath)}' damageType='{Sanitize(damageType)}' range={(float.IsNaN(range) ? "NaN" : range.ToString("F2"))}",
                    1);
            }

            Debug.LogError($"[AUTHORED-COVERAGE] area=known-creature key={key} path='{path}' resolved={node != null} animations='{Sanitize(animationsPath)}' animationsResolved={animationsResolved} manipChildren={(manipulators?.Children?.Count ?? 0)} manipAnonymous={(manipulators?.AnonymousChildren?.Count ?? 0)} primary='{Sanitize(primaryPath)}' damageType='{Sanitize(damageType)}' range={(float.IsNaN(range) ? "NaN" : range.ToString("F2"))} dbFallback={hasDbFallback} expectedDamageType='{Sanitize(expectedDamageType)}' expectedPrimaryContains='{Sanitize(expectedPrimaryContains)}' expectedRange={(float.IsNaN(expectedRange) ? "NaN" : expectedRange.ToString("F2"))}");
        }

        private static void ReportClassDamageModCoverage()
        {
            string[] classes = { "FighterBase", "RangerBase", "WarlockBase" };
            foreach (string classBase in classes)
            {
                GCNode node = GCDatabase.Instance.ResolveWithInheritance(classBase) ??
                              GCDatabase.Instance.ResolveWithInheritance("avatar.classes." + classBase);
                GCNode desc = node?.GetChild("Description") ?? node;
                bool hasRanged = desc != null && desc.HasProperty("RangedDamagePerAgilityMod");
                bool hasMelee = desc != null && desc.HasProperty("MeleeDamagePerStrengthMod");
                bool hasSkill = desc != null && desc.HasProperty("SkillDamagePerIntellectMod");
                string rangedSource = hasRanged ? "gc" : "client-default";
                string meleeSource = hasMelee ? "gc" : "client-default";
                string skillSource = hasSkill ? "gc" : "client-default";
                float rangedMod = hasRanged ? desc.GetFloat("RangedDamagePerAgilityMod", 1f) : 1f;
                float meleeMod = hasMelee ? desc.GetFloat("MeleeDamagePerStrengthMod", 1f) : 1f;
                float skillMod = hasSkill ? desc.GetFloat("SkillDamagePerIntellectMod", 1f) : 1f;
                Debug.LogError($"[AUTHORED-COVERAGE] area=class-damage class={classBase} resolved={node != null} rangedMod={rangedMod:F3} rangedSource={rangedSource} meleeMod={meleeMod:F3} meleeSource={meleeSource} skillMod={skillMod:F3} skillSource={skillSource} sourceFunction=HeroDesc-class-stat-default");
            }
        }

        private static void ReportChestGeneratorCoverage()
        {
            Debug.LogError("[AUTHORED-COVERAGE] area=chest-generators stockUnitTreasureSlots=1-10 nonCombatInteractiveItemSlots=1-5 currencyGenerators=package-backed itemGenerators=package-backed source=server-startup-report");
        }

        private static GCNode FindPrimaryManipulator(GCNode manipulators)
        {
            if (manipulators == null)
                return null;
            GCNode primary = manipulators.GetChild("PrimaryWeapon");
            if (primary != null)
                return primary;

            IEnumerable<GCNode> authoredChildren = Enumerable.Empty<GCNode>();
            if (manipulators.Children != null)
                authoredChildren = authoredChildren.Concat(manipulators.Children.Values);
            if (manipulators.AnonymousChildren != null)
                authoredChildren = authoredChildren.Concat(manipulators.AnonymousChildren);

            return authoredChildren.FirstOrDefault(child =>
                ContainsToken(child?.Name, "PrimaryWeapon") ||
                ContainsToken(child?.Extends, "PrimaryWeapon") ||
                ContainsToken(child?.Name, "UnarmedWeapon") ||
                ContainsToken(child?.Extends, "UnarmedWeapon") ||
                ContainsToken(child?.Name, "Weapon") ||
                ContainsToken(child?.Extends, "Weapon"));
        }

        private static void ReportRangerStarterDamageLane()
        {
            GCNode ranger = GCDatabase.Instance.ResolveWithInheritance("RangerBase") ??
                            GCDatabase.Instance.ResolveWithInheritance("avatar.classes.RangerBase");
            GCNode rangerDesc = ranger?.GetChild("Description") ?? ranger;
            float rangedMod = rangerDesc != null && rangerDesc.HasProperty("RangedDamagePerAgilityMod")
                ? rangerDesc.GetFloat("RangedDamagePerAgilityMod", 1f)
                : 1f;
            float rangedPerAgility = GCDatabase.Instance.GetKnob("RangedDamagePerAgility", 2.124f);
            int agility = 20;
            int rangedBonus = Mathf.FloorToInt(rangedPerAgility * agility * rangedMod);

            GCNode crossbow = GCDatabase.Instance.ResolveWithInheritance("2HCrossbow1PAL.2HCrossbow1-1") ??
                              GCDatabase.Instance.ResolveWithInheritance("2HCrossbow1PAL.2HCrossbow1");
            GCNode crossbowDesc = crossbow?.GetChild("Description") ?? crossbow;
            float storedLevel = GetEffectiveFloat(crossbow, "Level", float.NaN);
            float damage = GetEffectiveFloat(crossbow, "Damage", float.NaN);
            float volatility = GetEffectiveFloat(crossbow, "DamageVolatility", float.NaN);
            float range = GetEffectiveFloat(crossbow, "Range", float.NaN);
            float projectileSpeed = GetEffectiveFloat(crossbow, "ProjectileSpeed", float.NaN);
            float projectileSize = GetEffectiveFloat(crossbow, "ProjectileSize", float.NaN);
            string weaponClass = GetEffectiveString(crossbow, "WeaponClass");
            string damageType = GetEffectiveString(crossbow, "DamageType");
            string useProjectile = crossbowDesc?.GetString("UseProjectile", "") ?? "";

            bool missing = rangerDesc == null || crossbowDesc == null ||
                           float.IsNaN(storedLevel) ||
                           Math.Abs(storedLevel - 1f) > 0.01f ||
                           Math.Abs(rangedMod - 1f) > 0.01f;
            if (missing)
            {
                RuntimeEvidence.LogFallbackHit(
                    "damage-level",
                    "ranger-starter-coverage",
                    $"rangerResolved={rangerDesc != null} crossbowResolved={crossbowDesc != null} storedLevel={(float.IsNaN(storedLevel) ? "NaN" : storedLevel.ToString("F2"))} rangedMod={rangedMod:F3}",
                    1);
            }

            Debug.LogError($"[AUTHORED-COVERAGE] area=damage-check lane=ranger-starter rangerResolved={rangerDesc != null} crossbowResolved={crossbowDesc != null} storedLevel={(float.IsNaN(storedLevel) ? "NaN" : storedLevel.ToString("F2"))} classModRangerBase={rangedMod:F3} rangedDamagePerAgility={rangedPerAgility:F3} agility={agility} rangedBonus={rangedBonus} weaponClass='{Sanitize(weaponClass)}' damageType='{Sanitize(damageType)}' damage={(float.IsNaN(damage) ? "NaN" : damage.ToString("F3"))} volatility={(float.IsNaN(volatility) ? "NaN" : volatility.ToString("F3"))} range={(float.IsNaN(range) ? "NaN" : range.ToString("F2"))} useProjectile='{Sanitize(useProjectile)}' projectileSpeed={(float.IsNaN(projectileSpeed) ? "NaN" : projectileSpeed.ToString("F2"))} projectileSize={(float.IsNaN(projectileSize) ? "NaN" : projectileSize.ToString("F2"))}");
        }

        private static string GetEffectiveString(GCNode node, string key)
        {
            if (node == null || string.IsNullOrWhiteSpace(key))
                return "";
            GCNode desc = node.GetChild("Description");
            if (desc != null && desc.HasProperty(key))
                return desc.GetString(key, "");
            return node.GetString(key, "");
        }

        private static float GetEffectiveFloat(GCNode node, string key, float fallback)
        {
            if (node == null || string.IsNullOrWhiteSpace(key))
                return fallback;
            GCNode desc = node.GetChild("Description");
            if (desc != null && desc.HasProperty(key))
                return desc.GetFloat(key, fallback);
            return node.GetFloat(key, fallback);
        }

        private static void AddSample(List<string> samples, string value)
        {
            if (samples == null || samples.Count >= 8 || string.IsNullOrWhiteSpace(value))
                return;
            samples.Add(value);
        }

        private static bool ContainsToken(string value, string token)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   !string.IsNullOrWhiteSpace(token) &&
                   value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Sanitize(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? ""
                : value.Replace("'", "").Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
