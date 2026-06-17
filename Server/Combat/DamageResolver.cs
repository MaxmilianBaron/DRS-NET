using System;
using System.Linq;
using System.Collections.Generic;
using DungeonRunners.Engine;
using DungeonRunners.Data;
using DungeonRunners.Networking;
using DungeonRunners.Core;

namespace DungeonRunners.Combat
{
    public static class DamageResolver
    {

        public static int FromInt(int n) => n << 8;
        public static int FromFloat(float f) => EncodeFixed32(f);
        public static int ToInt(int f) => f >> 8;
        public static float ToFloat(int f) => f / 256f;
        public static int FixedMul(int a, int b) => (int)(((long)a * (long)b) >> 8);

        public static int EncodeFixed32(float value)
        {
            int n = (int)(value * 256f);
            if (n / 256f < value) n++;
            return n;
        }

        public static int ResolvePerStatBonus(int stat, int rateF32, int statModF32, int pctMod)
        {
            if (stat < 0) stat = 0;
            int innerMod = (int)(((long)statModF32 * (((pctMod + 100) * 0x10000) / 0x6400)) >> 8);
            if (innerMod < 0) innerMod = 0;
            int scaledRate = (int)(((long)rateF32 * innerMod) >> 8);
            return (int)(((long)(stat << 8) * scaledRate) >> 8) >> 8;
        }

        public static readonly WeaponDamageAddSlot[] WeaponDamageAddSlots =
        {
            new WeaponDamageAddSlot
            {
                Element = "Divine",
                DamageTypeId = 7,
                WeaponAddStats = new[] { "DIVINE_DAMAGE_WEAPON_ADD" },
                DamageBonusStats = new[] { "DIVINE_DAMAGE_BONUS", "DIVINEDAMAGEBONUS" },
                DamageModStats = new[] { "DIVINE_DAMAGE_MOD", "DIVINEDAMAGEMOD", "DIVINE_DAMAGE_PCT", "DIVINEDAMAGEPCT" }
            },
            new WeaponDamageAddSlot
            {
                Element = "Fire",
                DamageTypeId = 3,
                WeaponAddStats = new[] { "FIRE_DAMAGE_WEAPON_ADD" },
                DamageBonusStats = new[] { "FIRE_DAMAGE_BONUS", "FIREDAMAGEBONUS" },
                DamageModStats = new[] { "FIRE_DAMAGE_MOD", "FIREDAMAGEMOD", "FIRE_DAMAGE_PCT", "FIREDAMAGEPCT" }
            },
            new WeaponDamageAddSlot
            {
                Element = "Ice",
                DamageTypeId = 4,
                WeaponAddStats = new[] { "ICE_DAMAGE_WEAPON_ADD", "COLD_DAMAGE_WEAPON_ADD" },
                DamageBonusStats = new[] { "ICE_DAMAGE_BONUS", "ICEDAMAGEBONUS", "COLD_DAMAGE_BONUS", "COLDDAMAGEBONUS" },
                DamageModStats = new[] { "ICE_DAMAGE_MOD", "ICEDAMAGEMOD", "ICE_DAMAGE_PCT", "ICEDAMAGEPCT", "COLD_DAMAGE_MOD", "COLDDAMAGEMOD" }
            },
            new WeaponDamageAddSlot
            {
                Element = "Poison",
                DamageTypeId = 5,
                WeaponAddStats = new[] { "POISON_DAMAGE_WEAPON_ADD" },
                DamageBonusStats = new[] { "POISON_DAMAGE_BONUS", "POISONDAMAGEBONUS" },
                DamageModStats = new[] { "POISON_DAMAGE_MOD", "POISONDAMAGEMOD", "POISON_DAMAGE_PCT", "POISONDAMAGEPCT" }
            },
            new WeaponDamageAddSlot
            {
                Element = "Shadow",
                DamageTypeId = 6,
                WeaponAddStats = new[] { "SHADOW_DAMAGE_WEAPON_ADD" },
                DamageBonusStats = new[] { "SHADOW_DAMAGE_BONUS", "SHADOWDAMAGEBONUS" },
                DamageModStats = new[] { "SHADOW_DAMAGE_MOD", "SHADOWDAMAGEMOD", "SHADOW_DAMAGE_PCT", "SHADOWDAMAGEPCT" }
            }
        };

        public static int RoundFixed32(int f)
        {
            if ((f & 0xFF) > 0x7E) f += 0x100;
            return f & ~0xFF;
        }

        public static int RollDamageRange(int minDmg, int maxDmg, uint raw)
        {
            int range = Math.Max(0, maxDmg - minDmg);
            return range > 0 ? (int)(raw % ((uint)range + 1u)) + minDmg : minDmg;
        }

        public static int RollSpellDamageRange(int minDmg, int maxDmg, uint raw)
        {
            int minHp = minDmg >> 8;
            int maxHp = maxDmg >> 8;
            if (minHp < 1) minHp = 1;
            if (maxHp < minHp) maxHp = minHp;

            int damageHp = minHp;
            int rangeHp = maxHp - minHp;
            if (rangeHp > 0)
                damageHp = (int)(raw % (uint)rangeHp) + minHp;

            return Math.Max(0x100, damageHp << 8);
        }

        public static WeaponDamageResult ResolveWeaponDamage(WeaponDamageInput input)
        {
            var result = new WeaponDamageResult();
            if (input == null)
                return result;

            result.AttackRating = input.AttackRating;
            result.DefenseRating = input.DefenseRating;
            result.AttackerLevel = input.AttackerLevel;
            result.DefenderLevel = input.DefenderLevel;
            result.BlockChance = input.BlockChance;
            result.DamageLevel = input.DamageLevel;
            result.DamageBonus = input.DamageBonus;
            result.DamageMod = input.DamageMod;
            result.WeaponClassId = input.WeaponClassId;
            result.DamageTypeId = input.DamageTypeId;
            result.WeaponDamageF32 = input.WeaponDamageF32;
            result.WeaponVolatilityF32 = input.WeaponVolatilityF32;
            result.CritThreshold = input.CritThreshold;
            result.CritDamagePercent = input.CritDamagePercent;
            result.AttackerState = input.AttackerState;

            if (input.Rng == null)
            {
                result.Type = AttackResultType.Miss;
                result.ResultName = "NO-RNG";
                return result;
            }

            result.HitThreshold = ResolveHitThreshold(
                input.AttackRating,
                input.DefenseRating,
                input.AttackerLevel,
                input.DefenderLevel);

            result.HitRaw = RngLedger.Generate(input.Rng, "unitOwnedCombat", $"{input.Source ?? "Weapon::applyDamage"}:hit", "Weapon::applyDamage");
            result.HitRoll = (int)(result.HitRaw % 25700u);

            result.BlockRaw = RngLedger.Generate(input.Rng, "unitOwnedCombat", $"{input.Source ?? "Weapon::applyDamage"}:block", "Weapon::applyDamage");
            result.BlockRoll = (int)(((result.BlockRaw >> 8) & 0xFF) % 100) + 1;

            result.IsHit = result.HitRoll < result.HitThreshold;
            result.IsBlocked = result.IsHit && result.BlockRoll < input.BlockChance;

            if (!result.IsHit)
            {
                result.Type = AttackResultType.Miss;
                result.ResultName = "MISS";
                result.RoomRngAfter = input.Rng.CallsSinceReseed;
                return result;
            }

            if (result.IsBlocked)
            {
                result.Type = AttackResultType.Block;
                result.ResultName = "BLOCK";
                result.RoomRngAfter = input.Rng.CallsSinceReseed;
                return result;
            }

            ComputeWeaponDamageRange(
                input.DamageLevel,
                input.DamageBonus,
                input.DamageMod,
                input.WeaponDamageF32,
                input.WeaponVolatilityF32,
                out int minDamage,
                out int maxDamage);
            result.MinDamageF32 = minDamage;
            result.MaxDamageF32 = maxDamage;

            result.DamageRaw = RngLedger.Generate(input.Rng, "unitOwnedCombat", $"{input.Source ?? "Weapon::applyDamage"}:damage", "Weapon::computeDamage");
            int damage = RollDamageRange(minDamage, maxDamage, result.DamageRaw);

            if (input.CritThreshold > 0 && result.HitRoll < input.CritThreshold)
            {
                result.IsCritical = true;
                int critPercent = input.CritDamagePercent > 0 ? input.CritDamagePercent : 200;
                damage = (damage * critPercent) / 100;
            }
            if (damage < 0x100) damage = 0x100;

            result.DamageF32 = damage;
            result.DamageWire = (uint)Math.Max(1, damage);
            result.Type = result.IsCritical ? AttackResultType.Critical : AttackResultType.Hit;
            result.ResultName = result.IsCritical ? "CRIT" : "HIT";
            if (input.IncludeWeaponDamageAdds && input.AttackerState != null)
                result.DamageAdds.AddRange(ResolveWeaponDamageAdds(input.AttackerState, input.WeaponDamageF32));
            result.TotalDamageWire = result.DamageWire;
            result.TotalDamageF32 = result.DamageF32;
            foreach (WeaponDamageEvent add in result.DamageAdds)
            {
                result.TotalDamageWire = ClampWireAdd(result.TotalDamageWire, add.DamageWire);
                result.TotalDamageF32 = ClampIntAdd(result.TotalDamageF32, add.DamageF32);
            }
            result.RoomRngAfter = input.Rng.CallsSinceReseed;
            return result;
        }

        public static List<WeaponDamageEvent> ResolveWeaponDamageAdds(PlayerState state, int weaponDamageF32)
        {
            var events = new List<WeaponDamageEvent>();
            if (state == null)
                return events;

            foreach (WeaponDamageAddSlot slot in WeaponDamageAddSlots)
            {
                int weaponAdd = GetEquipmentStat(state, slot.WeaponAddStats);
                int damageBonus = GetEquipmentStat(state, slot.DamageBonusStats);
                int damageMod = GetEquipmentStat(state, slot.DamageModStats);
                int damageF32 = ComputeWeaponDamageAddF32(weaponAdd, damageBonus, damageMod, weaponDamageF32);
                if (damageF32 <= 0)
                    continue;

                events.Add(new WeaponDamageEvent
                {
                    Element = slot.Element,
                    DamageTypeId = slot.DamageTypeId,
                    DamageF32 = damageF32,
                    DamageWire = (uint)damageF32,
                    WeaponAdd = weaponAdd,
                    DamageBonus = damageBonus,
                    DamageMod = damageMod,
                    WeaponDamageF32 = weaponDamageF32
                });
            }

            return events;
        }

        public static int ComputeWeaponDamageAddF32(int weaponAdd, int damageBonus, int damageMod, int weaponDamageF32)
        {
            long baseF32 = (long)weaponAdd * 0x100L;
            if (baseF32 <= 0)
                return 0;

            long value = baseF32;
            value += (long)damageBonus * 0x100L;
            value += (baseF32 * ((long)damageMod << 8)) / 0x6400L;
            if (weaponDamageF32 > 0)
                value = (value * weaponDamageF32) >> 8;
            if (value <= 0)
                return 0;

            value = (value >> 8) << 8;
            if (value > int.MaxValue)
                return int.MaxValue & ~0xFF;
            return (int)value;
        }

        private static uint ClampWireAdd(uint left, uint right)
        {
            ulong sum = (ulong)left + right;
            return sum >= uint.MaxValue ? uint.MaxValue : (uint)sum;
        }

        private static int ClampIntAdd(int left, int right)
        {
            long sum = (long)left + right;
            if (sum > int.MaxValue) return int.MaxValue;
            if (sum < int.MinValue) return int.MinValue;
            return (int)sum;
        }

        public static bool IsRangedWeapon(PlayerState state)
        {
            if (state == null) return false;

            string weaponClass = state.WeaponClass ?? string.Empty;
            string weaponCategory = state.WeaponCategory ?? string.Empty;

            if (ContainsIgnoreCase(weaponClass, "RANGED") ||
                ContainsIgnoreCase(weaponClass, "BOW") ||
                ContainsIgnoreCase(weaponClass, "CROSSBOW") ||
                ContainsIgnoreCase(weaponClass, "GUN") ||
                ContainsIgnoreCase(weaponClass, "CANNON"))
                return true;

            if (ContainsIgnoreCase(weaponCategory, "RANGED") ||
                ContainsIgnoreCase(weaponCategory, "BOW") ||
                ContainsIgnoreCase(weaponCategory, "CROSSBOW") ||
                ContainsIgnoreCase(weaponCategory, "GUN") ||
                ContainsIgnoreCase(weaponCategory, "CANNON"))
                return true;

            if (state.WeaponUsesProjectile || state.WeaponProjectileSpeed > 0f || state.WeaponProjectileSize > 0f)
                return true;

            return state.WeaponRange > 16;
        }

        public static bool IsProjectileWeapon(PlayerState state)
        {
            if (state == null) return false;
            if (state.WeaponUsesProjectile) return true;
            if (!IsRangedWeapon(state)) return false;
            return state.WeaponProjectileSpeed > 0f || state.WeaponProjectileSize > 0f;
        }

        public static string ResolveWeaponStatSource(PlayerState state)
        {
            return IsRangedWeapon(state) ? "UnitCache/RangedDamagePerAgility" : "UnitCache/MeleeDamagePerStrength";
        }

        public static float ResolveWeaponAttackSpeedPct(PlayerState state)
        {
            if (state == null) return 0f;
            return IsRangedWeapon(state) ? state.RangeAttackSpeedModPercent : state.MeleeAttackSpeedModPercent;
        }

        public static int ApplyAttackSpeedPctToTicks(int ticks, float pct)
        {
            if (ticks <= 0 || Math.Abs(pct) < 0.0001f) return Math.Max(0, ticks);
            double scale = 1.0d + (pct / 100.0d);
            if (scale < 0.05d) scale = 0.05d;
            int adjusted = (int)Math.Floor((ticks / scale) + 0.5d);
            return Math.Max(1, adjusted);
        }

        public static int ResolveBasicAttackCooldownTicks(PlayerState state)
        {
            float cooldown = state != null ? state.WeaponCooldown : 0f;
            if (cooldown <= 0f) return 0;
            return Math.Max(0, (int)Math.Floor((cooldown * 30f) + 0.0001f));
        }

        private static int RoundPositiveToInt(float value)
        {
            if (value <= 0f) return 0;
            return (int)Math.Floor(value + 0.5f);
        }

        public static int ResolveWeaponDamageBonus(PlayerState state)
        {
            if (state == null) return 0;
            int weaponClassId = ResolveWeaponClassId(state);
            int damageTypeId = ResolveDamageTypeId(state);
            int bonus = ResolveUnitBaseDamageBonus(state);
            bonus += ResolveUnitWeaponClassDamageBonus(state, weaponClassId);
            bonus += ResolveUnitDamageTypeBonus(state, damageTypeId);
            return ClampUShort(bonus);
        }

        private static bool ContainsIgnoreCase(string value, string needle)
        {
            return !string.IsNullOrEmpty(value) &&
                   !string.IsNullOrEmpty(needle) &&
                   value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static int ResolveWeaponDamageLevel(PlayerState state)
        {
            if (state == null) return 1;
            if (state.WeaponBaseDamageTracksPlayerLevel)
            {
                int trackedLevel = Math.Max(1, ClampUShort(state.Level));
                state.WeaponBaseDamage = trackedLevel;
                state.WeaponDamageLevel = trackedLevel;
                return trackedLevel;
            }
            if (state.WeaponBaseDamage > 0)
                return Math.Max(1, ClampUShort(state.WeaponBaseDamage));
            int level = state.WeaponDamageLevel > 0
                ? state.WeaponDamageLevel
                : Math.Max(1, state.WeaponLevel);
            return Math.Max(1, ClampUShort(level));
        }

        public static int ResolveWeaponRuntimeBaseDamageLevel(
            int playerLevel,
            int storedLevel,
            int fallbackItemLevel,
            out bool tracksPlayerLevel,
            out string source)
        {
            if (fallbackItemLevel > 0)
            {
                tracksPlayerLevel = false;
                source = storedLevel >= 0 ? "materialized-item-level" : "authored-item-level";
                return ScaleWeaponDamageLevel(Math.Max(1, ClampUShort(fallbackItemLevel)));
            }

            if (storedLevel >= 0)
            {
                tracksPlayerLevel = false;
                source = "materialized-item-level";
                return ScaleWeaponDamageLevel(Math.Max(1, ClampUShort(storedLevel)));
            }

            tracksPlayerLevel = false;
            source = "client-default-item-level";
            return ScaleWeaponDamageLevel(1);
        }

        private static int ScaleWeaponDamageLevel(int itemLevel)
        {
            int lvl = Math.Max(1, itemLevel);
            int wdplF32 = FromFloat(GCDatabase.Instance.GetKnob("WeaponDamagePerLevel", 10f));
            int dpsF32 = FromFloat(GCDatabase.Instance.GetKnob("DPSModifier", 1f));
            int raw = FixedMul(lvl << 8, wdplF32);
            int scaled = (int)(((long)raw * dpsF32) >> 16);
            return Math.Max(1, ClampUShort(scaled));
        }

        public static void ApplyWeaponRuntimeBaseDamage(
            PlayerState state,
            int playerLevel,
            int storedLevel,
            int fallbackItemLevel,
            string sourceTag)
        {
            if (state == null)
                return;

            int baseDamage = ResolveWeaponRuntimeBaseDamageLevel(
                playerLevel,
                storedLevel,
                fallbackItemLevel,
                out bool tracksPlayerLevel,
                out string source);

            if (string.Equals(source, "client-default-item-level", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError($"[DAMAGE-LEVEL] sourceTag={sourceTag ?? "unknown"} source={source} playerLevel={playerLevel} storedLevel={storedLevel} fallbackItemLevel={fallbackItemLevel} resolved={baseDamage} sourceFunction=Weapon::ComputeAttributes@0x00599240");
            }

            state.WeaponBaseDamage = baseDamage;
            state.WeaponBaseDamageTracksPlayerLevel = tracksPlayerLevel;
            state.WeaponBaseDamageSource = string.IsNullOrEmpty(sourceTag)
                ? source
                : $"{sourceTag}:{source}";
            state.WeaponDamageLevel = baseDamage;
            Debug.LogError($"[DAMAGE-LEVEL] sourceTag={sourceTag ?? "unknown"} source={source} playerLevel={playerLevel} storedLevel={storedLevel} fallbackItemLevel={fallbackItemLevel} resolved={baseDamage} tracksPlayerLevel={tracksPlayerLevel}");
        }

        public static int ResolveLevelDamageBonus(PlayerState state)
        {
            return ResolveWeaponDamageLevel(state);
        }

        public static int ResolveDamageMod(PlayerState state)
        {
            int weaponClassId = ResolveWeaponClassId(state);
            int damageTypeId = ResolveDamageTypeId(state);
            int damagePct = ResolveUnitBaseDamageModPct(state);
            damagePct += ResolveUnitWeaponClassDamageModPct(state, weaponClassId);
            damagePct += ResolveUnitDamageTypeModPct(state, damageTypeId);
            int damageModPct = 100 + damagePct;
            if (damageModPct < 0) damageModPct = 0;
            return ClampUShort(damageModPct);
        }

        public static void LogDamageSlots(PlayerState state, WeaponDamageInput input, Monster monster, string source)
        {
            int weaponClassId = input != null ? input.WeaponClassId : ResolveWeaponClassId(state);
            int damageTypeId = input != null ? input.DamageTypeId : ResolveDamageTypeId(state);
            int baseBonus = ResolveUnitBaseDamageBonus(state);
            int classBonus = ResolveUnitWeaponClassDamageBonus(state, weaponClassId);
            int typeBonus = ResolveUnitDamageTypeBonus(state, damageTypeId);
            int categoryBonus = ResolveUnitWeaponCategoryDamageBonus(state);
            int baseModPct = ResolveUnitBaseDamageModPct(state);
            int classModPct = ResolveUnitWeaponClassDamageModPct(state, weaponClassId);
            int typeModPct = ResolveUnitDamageTypeModPct(state, damageTypeId);
            int categoryModPct = ResolveUnitWeaponCategoryDamageModPct(state);
            int dpsModifierF32 = FromFloat(GCDatabase.Instance.GetKnob("DPSModifier", 1f));
            string target = monster != null ? $"{monster.Name}#{monster.EntityId}" : "<none>";
            int rngBefore = input?.Rng != null ? input.Rng.CallsSinceReseed : -1;

            Debug.LogError(
                $"[DAMAGE-CLIENT-SLOTS] source={source ?? input?.Source ?? "unknown"} class={state?.ClassName ?? "<none>"} level={state?.Level ?? 0} target={target} " +
                $"weaponClass={state?.WeaponClass ?? "<none>"} weaponCategory={state?.WeaponCategory ?? "<none>"} damageType={state?.WeaponDamageType ?? "<none>"} weaponClassId={weaponClassId} damageTypeId={damageTypeId} " +
                $"clientBaseDamage={state?.WeaponBaseDamage ?? 0} clientBaseSource={state?.WeaponBaseDamageSource ?? "<none>"} damageLevel={input?.DamageLevel ?? ResolveWeaponDamageLevel(state)} " +
                $"bonusBase={baseBonus} bonusClass={classBonus} bonusType={typeBonus} bonusCategoryAudit={categoryBonus} bonusTotal={input?.DamageBonus ?? ResolveWeaponDamageBonus(state)} " +
                $"modBasePct={baseModPct} modClassPct={classModPct} modTypePct={typeModPct} modCategoryAudit={categoryModPct} dpsModifierF32={dpsModifierF32} damageMod={input?.DamageMod ?? ResolveDamageMod(state)} " +
                $"weaponDamageF32={input?.WeaponDamageF32 ?? GetWeaponBaseDamageF32(state)} volatilityF32={input?.WeaponVolatilityF32 ?? GetWeaponVolatilityF32(state)} critThreshold={input?.CritThreshold ?? ResolveCriticalThreshold(state, monster)} critPct={input?.CritDamagePercent ?? ResolveCriticalDamagePercent(state)} rngBefore={rngBefore}");
        }

        private static float ResolveClassDamageStatMod(PlayerState state, string descKey, float fallback)
        {
            string className = state?.ClassName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(className))
            {
                Debug.LogError($"[DAMAGE-CLASS-MOD] reason=missing-class-name descKey={descKey} value={fallback} source=client-default sourceFunction=HeroDesc-class-stat-default");
                return fallback;
            }

            string resolvedClassBase = null;
            var node = ResolveClassBaseNode(className, out resolvedClassBase);
            if (node == null)
            {
                Debug.LogError($"[DAMAGE-CLASS-MOD] reason=missing-class-node class={className} descKey={descKey} value={fallback} source=client-default sourceFunction=HeroDesc-class-stat-default");
                return fallback;
            }
            var desc = node?.GetChild("Description") ?? node;
            if (desc == null)
            {
                Debug.LogError($"[DAMAGE-CLASS-MOD] reason=missing-class-desc classBase={resolvedClassBase} descKey={descKey} value={fallback} source=client-default sourceFunction=HeroDesc-class-stat-default");
                return fallback;
            }
            if (!desc.HasProperty(descKey))
            {
                Debug.LogError($"[DAMAGE-CLASS-MOD] reason=missing-property classBase={resolvedClassBase} descKey={descKey} value={fallback} source=client-default sourceFunction=HeroDesc-class-stat-default");
                return fallback;
            }
            float value = desc.GetFloat(descKey, fallback);
            Debug.LogError($"[DAMAGE-CLASS-MOD] classBase={resolvedClassBase} className={className} descKey={descKey} value={value} source=gc");
            return value;
        }

        private static GCNode ResolveClassBaseNode(string className, out string resolvedClassBase)
        {
            resolvedClassBase = null;
            if (string.IsNullOrWhiteSpace(className) || GCDatabase.Instance == null)
                return null;

            string classBase = className.Trim();
            if (!classBase.EndsWith("Base", StringComparison.OrdinalIgnoreCase))
                classBase += "Base";

            var candidates = new List<string> { classBase, $"avatar.classes.{classBase}" };
            if (classBase.Equals("MageBase", StringComparison.OrdinalIgnoreCase) ||
                classBase.Equals("WarlockBase", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add("WarlockBase");
                candidates.Add("avatar.classes.WarlockBase");
            }

            foreach (string candidate in candidates)
            {
                var node = GCDatabase.Instance.ResolveWithInheritance(candidate);
                if (node == null)
                    continue;
                resolvedClassBase = candidate;
                return node;
            }

            return null;
        }

        public static int ResolveWeaponClassId(PlayerState state)
        {
            if (state != null && state.WeaponClassId > 0 && state.WeaponStatsResolved)
                return state.WeaponClassId;
            return ResolveWeaponClassId(state?.WeaponClass);
        }

        public static int ResolveWeaponClassId(string weaponClass)
        {
            string value = CanonicalStatName(weaponClass);
            if (TryResolveWeaponClassId(value, out int weaponClassId))
                return weaponClassId;
            return LogUnknownWeaponClass(value);
        }

        public static bool TryResolveWeaponClassId(string weaponClass, out int weaponClassId)
        {
            string value = CanonicalStatName(weaponClass);
            weaponClassId = 0;
            if (string.IsNullOrEmpty(value)) return false;
            return value switch
            {
                "HTH" => AssignWeaponClass(1, out weaponClassId),
                "2HRANGED" => AssignWeaponClass(3, out weaponClassId),
                "1HMELEE" or "1HSTAFF" or "1HMACE" or "1HSWORD" or "1HAXE" => AssignWeaponClass(5, out weaponClassId),
                "2HMELEE" or "2HMACE" or "2HSWORD" or "2HAXE" => AssignWeaponClass(6, out weaponClassId),
                "POLEARM" => AssignWeaponClass(8, out weaponClassId),
                "1HRANGED" or "1HCROSSBOW" or "1HBOW" or "1HGUN" => AssignWeaponClass(9, out weaponClassId),
                "2HCANNON" => AssignWeaponClass(13, out weaponClassId),
                "2HCROSSBOW" or "2HBOW" or "2HGUN" => AssignWeaponClass(3, out weaponClassId),
                _ => false
            };
        }

        private static bool AssignWeaponClass(int value, out int weaponClassId)
        {
            weaponClassId = value;
            return true;
        }

        private static int LogUnknownWeaponClass(string value)
        {
            RuntimeEvidence.LogFallbackHit("damage-weapon-class", "unknown", $"weaponClass={value ?? "<null>"} sourceFunction=blocked compatibility=none", 64);
            return 0;
        }

        public static int ResolveDamageTypeId(PlayerState state)
        {
            if (state != null && state.DamageTypeId >= 0 && state.WeaponStatsResolved)
                return state.DamageTypeId;
            return ResolveDamageTypeId(state?.WeaponDamageType);
        }

        public static int ResolveDamageTypeId(string damageType)
        {
            string value = CanonicalStatName(damageType);
            if (TryResolveDamageTypeId(value, out int damageTypeId))
                return damageTypeId;
            return LogUnknownDamageType(value);
        }

        public static bool TryResolveDamageTypeId(string damageType, out int damageTypeId)
        {
            string value = CanonicalStatName(damageType);
            damageTypeId = -1;
            if (string.IsNullOrEmpty(value)) return false;
            return value switch
            {
                "CRUSHING" => AssignDamageType(0, out damageTypeId),
                "PIERCING" => AssignDamageType(1, out damageTypeId),
                "SLASHING" => AssignDamageType(2, out damageTypeId),
                "FIRE" => AssignDamageType(3, out damageTypeId),
                "ICE" => AssignDamageType(4, out damageTypeId),
                "POISON" => AssignDamageType(5, out damageTypeId),
                "SHADOW" => AssignDamageType(6, out damageTypeId),
                "DIVINE" => AssignDamageType(7, out damageTypeId),
                _ => false
            };
        }

        private static bool AssignDamageType(int value, out int damageTypeId)
        {
            damageTypeId = value;
            return true;
        }

        private static int LogUnknownDamageType(string value)
        {
            RuntimeEvidence.LogFallbackHit("damage-type", "unknown", $"damageType={value ?? "<null>"} sourceFunction=blocked compatibility=none", 64);
            return -1;
        }

        public static int ResolveDamageTypeId(DamageElement element)
        {
            return element switch
            {
                DamageElement.PHYSICAL => 0,
                DamageElement.FIRE => 3,
                DamageElement.ICE => 4,
                DamageElement.POISON => 5,
                DamageElement.SHADOW => 6,
                DamageElement.DIVINE => 7,
                _ => LogUnknownDamageElement(element)
            };
        }

        private static int LogUnknownDamageElement(DamageElement element)
        {
            RuntimeEvidence.LogFallbackHit("damage-type", "unknown-damage-element", $"element={element} sourceFunction=blocked compatibility=none", 64);
            return -1;
        }

        public static int ResolveWeaponComputedDamageLevel(int weaponLevel)
        {
            return Math.Max(1, ClampUShort(weaponLevel));
        }

        private static int ResolveUnitBaseDamageBonus(PlayerState state)
        {
            return GetEquipmentStat(state, "DAMAGE_BONUS", "DAMAGEBONUS");
        }

        private static int ResolveUnitWeaponClassDamageBonus(PlayerState state, int weaponClassId)
        {
            switch (weaponClassId)
            {
                case 1:
                    return ResolveUnitMeleeCommonDamageBonus(state);
                case 3:
                case 9:
                case 13:
                    return ResolveUnitRangedDamageBonus(state);
                case 5:
                    return ResolveUnitMelee1HDamageBonus(state) +
                           ResolveUnitMeleeCommonDamageBonus(state);
                case 6:
                case 8:
                    return ResolveUnitMelee2HDamageBonus(state) +
                           ResolveUnitMeleeCommonDamageBonus(state);
                default:
                    RuntimeEvidence.LogFallbackHit("damage-weapon-class", "bonus-unresolved", $"weaponClass={state?.WeaponClass ?? "<null>"} weaponClassId={weaponClassId}", 64);
                    return 0;
            }
        }

        private static int ResolveUnitRangedDamageBonus(PlayerState state)
        {
            int agility = Math.Max(0, state?.Agility ?? 10);
            int rateF32 = EncodeFixed32(GCDatabase.Instance.GetKnob("RangedDamagePerAgility", 2.124f));
            int statModF32 = EncodeFixed32(ResolveClassDamageStatMod(state, "RangedDamagePerAgilityMod", 1f));
            int bonus = ResolvePerStatBonus(agility, rateF32, statModF32, 0);
            bonus += GetEquipmentStat(state, "RANGE_DAMAGE_BONUS", "RANGED_DAMAGE_BONUS", "RANGEDAMAGEBONUS", "RANGEDDAMAGEBONUS");
            return bonus;
        }

        private static int ResolveUnitMeleeCommonDamageBonus(PlayerState state)
        {
            int strength = Math.Max(0, state?.Strength ?? 10);
            int rateF32 = EncodeFixed32(GCDatabase.Instance.GetKnob("MeleeDamagePerStrength", 2.3364f));
            int statModF32 = EncodeFixed32(ResolveClassDamageStatMod(state, "MeleeDamagePerStrengthMod", 1f));
            int bonus = ResolvePerStatBonus(strength, rateF32, statModF32, 0);
            bonus += GetEquipmentStat(state, "MELEE_DAMAGE_BONUS", "MELEEDAMAGEBONUS");
            return bonus;
        }

        private static int ResolveUnitMelee1HDamageBonus(PlayerState state)
        {
            return GetEquipmentStat(state, "MELEE1H_DAMAGE_BONUS", "1HMELEE_DAMAGE_BONUS", "MELEE_1H_DAMAGE_BONUS", "MELEE1HDAMAGEBONUS");
        }

        private static int ResolveUnitMelee2HDamageBonus(PlayerState state)
        {
            return GetEquipmentStat(state, "MELEE2H_DAMAGE_BONUS", "2HMELEE_DAMAGE_BONUS", "MELEE_2H_DAMAGE_BONUS", "MELEE2HDAMAGEBONUS");
        }

        private static int ResolveUnitDamageTypeBonus(PlayerState state, int damageTypeId)
        {
            return damageTypeId switch
            {
                0 => GetEquipmentStat(state, "CRUSHING_DAMAGE_BONUS", "CRUSHINGDAMAGEBONUS"),
                1 => GetEquipmentStat(state, "PIERCING_DAMAGE_BONUS", "PIERCINGDAMAGEBONUS"),
                2 => GetEquipmentStat(state, "SLASHING_DAMAGE_BONUS", "SLASHINGDAMAGEBONUS"),
                3 => GetEquipmentStat(state, "FIRE_DAMAGE_BONUS", "FIREDAMAGEBONUS"),
                4 => GetEquipmentStat(state, "ICE_DAMAGE_BONUS", "ICEDAMAGEBONUS", "COLD_DAMAGE_BONUS", "COLDDAMAGEBONUS"),
                5 => GetEquipmentStat(state, "POISON_DAMAGE_BONUS", "POISONDAMAGEBONUS"),
                6 => GetEquipmentStat(state, "SHADOW_DAMAGE_BONUS", "SHADOWDAMAGEBONUS"),
                7 => GetEquipmentStat(state, "DIVINE_DAMAGE_BONUS", "DIVINEDAMAGEBONUS"),
                _ => 0
            };
        }

        private static int ResolveUnitWeaponCategoryDamageBonus(PlayerState state)
        {
            string category = CanonicalStatName(state?.WeaponCategory);
            if (string.IsNullOrEmpty(category)) return 0;
            return GetEquipmentStat(state, category + "_DAMAGE_BONUS", category + "DAMAGEBONUS");
        }

        private static int ResolveUnitBaseDamageModPct(PlayerState state)
        {
            return GetEquipmentStat(state, "DAMAGE_MOD", "DAMAGEMOD", "DAMAGE_PCT", "DAMAGEPCT");
        }

        private static int ResolveUnitWeaponClassDamageModPct(PlayerState state, int weaponClassId)
        {
            switch (weaponClassId)
            {
                case 1:
                    return ResolveUnitMeleeCommonDamageModPct(state);
                case 3:
                case 9:
                case 13:
                    return ResolveUnitRangedDamageModPct(state);
                case 5:
                    return ResolveUnitMelee1HDamageModPct(state) +
                           ResolveUnitMeleeCommonDamageModPct(state);
                case 6:
                case 8:
                    return ResolveUnitMelee2HDamageModPct(state) +
                           ResolveUnitMeleeCommonDamageModPct(state);
                default:
                    RuntimeEvidence.LogFallbackHit("damage-weapon-class", "mod-unresolved", $"weaponClass={state?.WeaponClass ?? "<null>"} weaponClassId={weaponClassId}", 64);
                    return 0;
            }
        }

        private static int ResolveUnitRangedDamageModPct(PlayerState state)
        {
            return GetEquipmentStat(state, "RANGE_DAMAGE_MOD", "RANGED_DAMAGE_MOD", "RANGEDAMAGEMOD", "RANGEDDAMAGEMOD");
        }

        private static int ResolveUnitMeleeCommonDamageModPct(PlayerState state)
        {
            return GetEquipmentStat(state, "MELEE_DAMAGE_MOD", "MELEEDAMAGEMOD");
        }

        private static int ResolveUnitMelee1HDamageModPct(PlayerState state)
        {
            return GetEquipmentStat(state, "MELEE1H_DAMAGE_MOD", "1HMELEE_DAMAGE_MOD", "MELEE_1H_DAMAGE_MOD", "MELEE1HDAMAGEMOD");
        }

        private static int ResolveUnitMelee2HDamageModPct(PlayerState state)
        {
            return GetEquipmentStat(state, "MELEE2H_DAMAGE_MOD", "2HMELEE_DAMAGE_MOD", "MELEE_2H_DAMAGE_MOD", "MELEE2HDAMAGEMOD");
        }

        private static int ResolveUnitDamageTypeModPct(PlayerState state, int damageTypeId)
        {
            return damageTypeId switch
            {
                0 => GetEquipmentStat(state, "CRUSHING_DAMAGE_MOD", "CRUSHINGDAMAGEMOD", "CRUSHING_DAMAGE_PCT", "CRUSHINGDAMAGEPCT"),
                1 => GetEquipmentStat(state, "PIERCING_DAMAGE_MOD", "PIERCINGDAMAGEMOD", "PIERCING_DAMAGE_PCT", "PIERCINGDAMAGEPCT"),
                2 => GetEquipmentStat(state, "SLASHING_DAMAGE_MOD", "SLASHINGDAMAGEMOD", "SLASHING_DAMAGE_PCT", "SLASHINGDAMAGEPCT"),
                3 => GetEquipmentStat(state, "FIRE_DAMAGE_MOD", "FIREDAMAGEMOD", "FIRE_DAMAGE_PCT", "FIREDAMAGEPCT"),
                4 => GetEquipmentStat(state, "ICE_DAMAGE_MOD", "ICEDAMAGEMOD", "ICE_DAMAGE_PCT", "ICEDAMAGEPCT", "COLD_DAMAGE_MOD", "COLDDAMAGEMOD"),
                5 => GetEquipmentStat(state, "POISON_DAMAGE_MOD", "POISONDAMAGEMOD", "POISON_DAMAGE_PCT", "POISONDAMAGEPCT"),
                6 => GetEquipmentStat(state, "SHADOW_DAMAGE_MOD", "SHADOWDAMAGEMOD", "SHADOW_DAMAGE_PCT", "SHADOWDAMAGEPCT"),
                7 => GetEquipmentStat(state, "DIVINE_DAMAGE_MOD", "DIVINEDAMAGEMOD", "DIVINE_DAMAGE_PCT", "DIVINEDAMAGEPCT"),
                _ => 0
            };
        }

        private static int ResolveUnitWeaponCategoryDamageModPct(PlayerState state)
        {
            string category = CanonicalStatName(state?.WeaponCategory);
            if (string.IsNullOrEmpty(category)) return 0;
            return GetEquipmentStat(state, category + "_DAMAGE_MOD", category + "DAMAGEMOD", category + "_DAMAGE_PCT", category + "DAMAGEPCT");
        }

        private static int ClampUShort(int value)
        {
            return Math.Max(0, Math.Min(0xFFFF, value));
        }

        private static int GetEquipmentStat(PlayerState state, params string[] names)
        {
            if (state?.EquipmentStats == null || state.EquipmentStats.Count == 0 || names == null) return 0;
            int total = 0;
            foreach (var statEntry in state.EquipmentStats)
            {
                string key = CanonicalStatName(statEntry.Key);
                for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
                {
                    if (key == CanonicalStatName(names[nameIndex]))
                    {
                        total += statEntry.Value;
                        break;
                    }
                }
            }
            return total;
        }

        private static string CanonicalStatName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return value.Replace("_", string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();
        }

        public static int ResolveCriticalThreshold(PlayerState state, Monster defender)
        {
            int baseCrit = GCDatabase.Instance.GetKnobInt("HeroCriticalChance", 3);
            int threshold = baseCrit << 8;
            int attackerLevel = Math.Max(0, state?.Level ?? 1);
            int defenderLevel = Math.Max(0, defender?.Level ?? attackerLevel);
            int levelDelta = attackerLevel - defenderLevel;
            if (levelDelta > 0)
                threshold += levelDelta * 0x500;
            if (threshold > 0x5A00) threshold = 0x5A00;
            if (threshold < 0) threshold = 0;
            return threshold;
        }

        public static int ResolveCriticalDamagePercent(PlayerState state)
        {
            return 200;
        }

        public static int ResolveMonsterCriticalThreshold(Monster attacker, PlayerState defender)
        {
            int baseCrit = MonsterUnitStatsBuilder.ComputeBaseCriticalChance(attacker?.CritChance ?? 0f);
            if (baseCrit <= 0) return 0;
            int threshold = baseCrit << 8;
            int attackerLevel = attacker != null ? attacker.Level : 1;
            int defenderLevel = defender != null ? defender.Level : attackerLevel;
            int levelDelta = attackerLevel - defenderLevel;
            if (levelDelta > 0)
                threshold += levelDelta * 0x500;
            if (threshold > 0x5A00) threshold = 0x5A00;
            if (threshold < 0) threshold = 0;
            return threshold;
        }

        public static int ResolveAvatarAttackRating(PlayerState state)
        {
            if (state == null) return 0;
            int rateF32 = EncodeFixed32(GCDatabase.Instance.GetKnob("AttackRatingPerAgility", 14f));
            int statModF32 = EncodeFixed32(ResolveClassDamageStatMod(state, "AttackRatingPerAgilityMod", 1f));
            int modPercent = IsRangedWeapon(state) ? 0 : Math.Max(-100, state.MeleeAttackRatingModPercent);
            return Math.Max(0, ResolvePerStatBonus(state.Agility, rateF32, statModF32, modPercent));
        }

        public static int ResolveMonsterDefenseRating(Monster monster)
        {
            return ResolveMonsterCurveRating("MonsterDefenseRating", monster);
        }

        public static int ResolveMonsterAttackRating(Monster monster)
        {
            return ResolveMonsterCurveRating("MonsterAttackRating", monster, true);
        }

        private static int ResolveMonsterCurveRating(string curveName, Monster monster, bool attack = false)
        {
            if (monster == null) return 0;
            float authored = attack ? monster.AttackRating : monster.DefenseRating;
            int authoredF32 = Fixed32FromAuthoredDecimal(Mathf.Max(0f, authored));
            if (authoredF32 <= 0) return 0;
            int tableF32 = GCDatabase.Instance.RequireCurveValueFixed32(curveName, Mathf.Clamp(monster.Level, 1, 110));
            int rating = (int)(((long)authoredF32 * tableF32) >> 16);
            return (ushort)rating;
        }

        public static int ResolveHitThreshold(int attackRating, int defenseRating, int attackerLevel, int defenderLevel)
        {
            int attack = Math.Max(0, attackRating);
            int defense = Math.Max(0, defenseRating);
            int chancePercent = attack + defense == 0 ? 0 : (attack * 100) / (attack + defense);
            int threshold = chancePercent << 8;
            int levelDelta = Math.Max(0, Math.Min(110, defenderLevel)) - Math.Max(0, Math.Min(110, attackerLevel));
            threshold -= levelDelta * 0x500;
            if (threshold < 0x0A00) threshold = 0x0A00;
            return threshold;
        }

        public static void ComputeWeaponDamageRange(int damageLevel, int damageBonus, int damageMod, int weaponDamageF32, int volatilityF32, out int minDamage, out int maxDamage)
        {
            if (damageLevel < 0) damageLevel = 0;
            if (damageBonus < 0) damageBonus = 0;
            if (damageMod < 0) damageMod = 0;
            if (weaponDamageF32 <= 0) weaponDamageF32 = 0x100;
            if (volatilityF32 < 0) volatilityF32 = 0;

            int normalized = FixedMul((damageLevel + damageBonus) << 8, weaponDamageF32);
            normalized = (int)(((long)normalized * ((long)damageMod << 8)) / 0x6400L);
            if (normalized < 0x100) normalized = 0x100;

            int spread = FixedMul(normalized, volatilityF32);
            minDamage = RoundFixed32(normalized - spread);
            maxDamage = RoundFixed32(normalized + spread);
            if (minDamage < 0x100) minDamage = 0x100;
            if (maxDamage < 0x100) maxDamage = 0x100;
            minDamage = (minDamage >> 8) << 8;
            maxDamage = (maxDamage >> 8) << 8;
            if (maxDamage < minDamage) maxDamage = minDamage;
        }

        public static int GetWeaponBaseDamageF32(PlayerState state)
        {
            int f32 = Fixed32FromAuthoredDecimal(state.WeaponDamage);
            Debug.LogError($"[DMG] Weapon multiplier: {state.WeaponDamage} (F32: 0x{f32:X})");
            return f32;
        }

        public static int GetWeaponVolatilityF32(PlayerState state)
        {
            float vol = state != null ? state.WeaponDamageVolatility : 0.5f;
            if (state == null || !state.WeaponStatsResolved)
            {
                RuntimeEvidence.LogFallbackHit(
                    "damage-volatility",
                    "unresolved-player-weapon",
                    $"class={state?.WeaponClass ?? "<null>"} volatility={vol:F3}",
                    64);
            }
            vol = Mathf.Clamp(vol, 0f, 0.95f);
            return Fixed32FromAuthoredDecimal(vol);
        }

        public static int Fixed32FromAuthoredDecimal(float value)
        {
            return Mathf.CeilToInt(value * 256f);
        }


        public static float GC_SKILL_DAMAGE_PER_LEVEL => GCDatabase.Instance.GetKnob("SkillDamagePerLevel", 15f);
        public static float GC_SKILL_DAMAGE_PER_INTELLECT => GCDatabase.Instance.GetKnob("SkillDamagePerIntellect", 1.5f);

        public static int ResolveSkillPowerLevelF32(SpellData spell, int skillLevel)
        {
            int requiredLevel = Math.Max(1, spell?.RequiredLevel ?? 1);
            int requiredLevelIncF32 = FromFloat(Math.Max(0, spell?.RequiredLevelInc ?? 0));
            int levelF32 = Math.Max(1, skillLevel) << 8;
            int scaledInc = FixedMul(levelF32 - 0x100, requiredLevelIncF32);
            int powerLevelF32 = (requiredLevel << 8) + scaledInc;
            return powerLevelF32 & ~0xFF;
        }

        public static int ResolveSpellCriticalThreshold(PlayerState state, Monster defender, SpellData spell)
        {
            return ResolveSpellCriticalThreshold(state, defender, spell?.CriticalChance ?? 0f);
        }

        public static int ResolveSpellCriticalThreshold(PlayerState state, Monster defender, float spellCriticalChance)
        {
            if (spellCriticalChance <= 0f)
                return 0;

            float baseCrit = GCDatabase.Instance.GetKnob("HeroCriticalChance", 3f);
            int equipmentCrit = GetEquipmentStat(state, "MAGIC_CRITICAL_CHANCE", "MAGICCRITICALCHANCE");
            int threshold = FromFloat((baseCrit + equipmentCrit) * spellCriticalChance);
            if (threshold > 0x5A00) threshold = 0x5A00;
            if (threshold < 0) threshold = 0;
            return threshold;
        }

        private static int ResolveSpellDamageBonus(PlayerState state, int attackerIntellect, DamageElement damageType)
        {
            int damageTypeId = ResolveDamageTypeId(damageType);
            return ResolveUnitBaseDamageBonus(state) +
                   ResolveUnitDamageTypeBonus(state, damageTypeId) +
                   ResolveMagicDamageBonus(state, attackerIntellect);
        }

        private static int ResolveSpellDamageMod(PlayerState state, DamageElement damageType)
        {
            int damageTypeId = ResolveDamageTypeId(damageType);
            int damagePct = ResolveUnitBaseDamageModPct(state) + ResolveUnitDamageTypeModPct(state, damageTypeId);
            int damageModPct = 100 + damagePct;
            if (damageModPct < 0) damageModPct = 0;
            return ClampUShort(damageModPct);
        }

        private static int ResolveMagicDamageBonus(PlayerState state, int attackerIntellect)
        {
            int intellect = Math.Max(0, attackerIntellect);
            int statModF32 = EncodeFixed32(state != null ? ResolveClassDamageStatMod(state, "SkillDamagePerIntellectMod", 1f) : 1f);
            int rateF32 = EncodeFixed32(GCDatabase.Instance.GetKnob("SkillDamagePerIntellect", 1.5f));
            int bonus = ResolvePerStatBonus(intellect, rateF32, statModF32, 0);
            bonus += GetEquipmentStat(state, "MAGIC_DAMAGE_BONUS", "MAGICDAMAGEBONUS");
            return bonus;
        }

        private static int ResolveMagicDamageModPct(PlayerState state)
        {
            int classMod = 0;
            if (state != null)
                classMod = Mathf.FloorToInt(ResolveClassDamageStatMod(state, "MagicDamageMod", 0f));
            int equipmentMod = GetEquipmentStat(state, "MAGIC_DAMAGE_MOD", "MAGICDAMAGEMOD", "MAGIC_DAMAGE_PCT", "MAGICDAMAGEPCT");
            int passiveMod = state != null ? Mathf.FloorToInt(state.MagicDamageModPercent) : 0;
            return classMod + equipmentMod + passiveMod;
        }

        public static void ComputeSpellDamageRange(
            PlayerState attackerState,
            int skillPowerLevelF32,
            int attackerIntellect,
            DamageElement damageType,
            float spellDamageMod,
            float spellDamageVolatility,
            out int minDamage, out int maxDamage)
        {
            int levelDamageF32 = FixedMul(FromFloat(GCDatabase.Instance.GetKnob("SkillDamagePerLevel", 15f)), Math.Max(0, skillPowerLevelF32));
            int dpsDamageF32 = FixedMul(levelDamageF32, FromFloat(GCDatabase.Instance.GetKnob("DPSModifier", 1f)));
            int damageBonus = ResolveSpellDamageBonus(attackerState, attackerIntellect, damageType);
            int damageMod = ResolveSpellDamageMod(attackerState, damageType);
            int magicDamageMod = 100 + ResolveMagicDamageModPct(attackerState);
            if (magicDamageMod < 0) magicDamageMod = 0;

            int baseDmgF32 = dpsDamageF32 + (damageBonus << 8);
            int spellModF32 = Fixed32FromAuthoredDecimal(spellDamageMod);
            int normalized = FixedMul(baseDmgF32, spellModF32);
            normalized = (int)(((long)normalized * ((long)magicDamageMod << 8)) / 0x6400L);
            normalized = (int)(((long)normalized * ((long)damageMod << 8)) / 0x6400L);

            if (normalized < 0x100) normalized = 0x100;

            int volF32 = Fixed32FromAuthoredDecimal(spellDamageVolatility);
            int spread = FixedMul(normalized, volF32);

            minDamage = normalized - spread;
            maxDamage = normalized + spread;

            minDamage = RoundFixed32(minDamage);
            maxDamage = RoundFixed32(maxDamage);
            if (minDamage < 0x100) minDamage = 0x100;
            if (maxDamage < 0x100) maxDamage = 0x100;

            minDamage = (minDamage >> 8) << 8;
            maxDamage = (maxDamage >> 8) << 8;

            Debug.LogError($"[SPELL-DMG] DamageRange: power={skillPowerLevelF32 / 256f:F2} int={attackerIntellect} levelBase={levelDamageF32 / 256f:F2} bonus={damageBonus} spellMod={spellDamageMod:F2} magicMod={magicDamageMod} damageMod={damageMod} vol={spellDamageVolatility:F2} -> [{minDamage / 256},{maxDamage / 256}]");
        }

        public static void ComputeSpellDamageRange(
            int attackerLevel,
            int attackerIntellect,
            float spellDamageMod,
            float spellDamageVolatility,
            out int minDamage, out int maxDamage)
        {
            float levelComponent = GC_SKILL_DAMAGE_PER_LEVEL * attackerLevel;
            float intellectComponent = GC_SKILL_DAMAGE_PER_INTELLECT * attackerIntellect;
            float baseDamage = levelComponent + intellectComponent;

            int baseDmgF32 = FromFloat(baseDamage);
            int spellModF32 = FromFloat(spellDamageMod);
            int normalized = FixedMul(baseDmgF32, spellModF32);
            if (normalized < 0x100) normalized = 0x100;

            int volF32 = FromFloat(spellDamageVolatility);
            int spread = FixedMul(normalized, volF32);

            minDamage = normalized - spread;
            maxDamage = normalized + spread;

            minDamage = RoundFixed32(minDamage);
            maxDamage = RoundFixed32(maxDamage);
            if (minDamage < 0x100) minDamage = 0x100;
            if (maxDamage < 0x100) maxDamage = 0x100;

            minDamage = (minDamage >> 8) << 8;
            maxDamage = (maxDamage >> 8) << 8;

            Debug.LogError($"[SPELL-DMG] DamageRange: level={attackerLevel} int={attackerIntellect} base={baseDamage:F1} spellMod={spellDamageMod:F2} vol={spellDamageVolatility:F2} -> [{minDamage / 256},{maxDamage / 256}]");
        }

        public static float GC_RANGED_DAMAGE_PER_AGILITY => GCDatabase.Instance.GetKnob("RangedDamagePerAgility", 2.124f);

        public static void ComputeRangedDamageRange(
            int attackerLevel,
            int attackerAgility,
            float weaponDamageMultiplier,
            float spellDamageMod,
            float spellDamageVolatility,
            out int minDamage, out int maxDamage)
        {
            float levelComponent = GCDatabase.Instance.GetKnob("WeaponDamagePerLevel", 10f) * attackerLevel;
            float agilityComponent = GC_RANGED_DAMAGE_PER_AGILITY * attackerAgility;
            float baseDamage = levelComponent + agilityComponent;

            int baseDmgF32 = FromFloat(baseDamage);
            int weaponF32 = FromFloat(weaponDamageMultiplier);
            int weaponScaled = FixedMul(baseDmgF32, weaponF32);

            int spellModF32 = FromFloat(spellDamageMod);
            int normalized = FixedMul(weaponScaled, spellModF32);
            if (normalized < 0x100) normalized = 0x100;

            int volF32 = FromFloat(spellDamageVolatility);
            int spread = FixedMul(normalized, volF32);

            minDamage = normalized - spread;
            maxDamage = normalized + spread;
            minDamage = RoundFixed32(minDamage);
            maxDamage = RoundFixed32(maxDamage);
            if (minDamage < 0x100) minDamage = 0x100;
            if (maxDamage < 0x100) maxDamage = 0x100;
            minDamage = (minDamage >> 8) << 8;
            maxDamage = (maxDamage >> 8) << 8;

            Debug.LogError($"[RANGED-DMG] DamageRange: level={attackerLevel} agi={attackerAgility} base={baseDamage:F1} wpn={weaponDamageMultiplier:F2} spellMod={spellDamageMod:F2} vol={spellDamageVolatility:F2} -> [{minDamage / 256},{maxDamage / 256}]");
        }


        public static void ComputeWeaponSkillDamageRange(
            int attackerLevel,
            int attackerStrength,
            float weaponDamageMultiplier,
            float weaponVolatility,
            int skillLevel,
            int damageModMin, int damageModMax, int damageModInc,
            out int minDamage, out int maxDamage)
        {
            float levelComponent = GCDatabase.Instance.GetKnob("WeaponDamagePerLevel", 10f) * attackerLevel;
            float strengthComponent = GCDatabase.Instance.GetKnob("MeleeDamagePerStrength", 2.3364f) * attackerStrength;
            float baseDamage = levelComponent + strengthComponent;

            int baseDmgF32 = FromFloat(baseDamage);
            int weaponF32 = FromFloat(weaponDamageMultiplier);
            int normalized = FixedMul(baseDmgF32, weaponF32);
            if (normalized < 0x100) normalized = 0x100;

            int rawMod = damageModMin + (skillLevel * damageModInc);
            if (damageModMax > 0 && rawMod > damageModMax) rawMod = damageModMax;
            float skillModifier = (100f + rawMod) / 100f;
            int skillModF32 = FromFloat(skillModifier);
            normalized = FixedMul(normalized, skillModF32);
            if (normalized < 0x100) normalized = 0x100;

            int volF32 = FromFloat(weaponVolatility);
            int spread = FixedMul(normalized, volF32);

            minDamage = normalized - spread;
            maxDamage = normalized + spread;
            minDamage = RoundFixed32(minDamage);
            maxDamage = RoundFixed32(maxDamage);
            if (minDamage < 0x100) minDamage = 0x100;
            if (maxDamage < 0x100) maxDamage = 0x100;
            minDamage = (minDamage >> 8) << 8;
            maxDamage = (maxDamage >> 8) << 8;

            Debug.LogError($"[WPNSKILL-DMG] DamageRange: level={attackerLevel} str={attackerStrength} wpn={weaponDamageMultiplier:F2} skillLvl={skillLevel} mod={rawMod}({skillModifier:F2}x) -> [{minDamage / 256},{maxDamage / 256}]");
        }

        public static SpellAttackResult ProcessSpellAttack(
            MersenneTwister rng,
            int attackerLevel,
            int attackerIntellect,
            int attackerAgility,
            int attackerStrength,
            float weaponDamageMultiplier,
            float weaponVolatility,
            SpellData spell,
            Monster target,
            int skillLevel = 1,
            bool isChainTarget = false,
            int criticalDamagePercent = 200,
            PlayerState attackerState = null,
            int spellCriticalThreshold = -1)
        {
            var result = new SpellAttackResult();
            result.Spell = spell;
            result.HitRoll = -1;

            if (spell == null || !spell.HasDirectDamageEffect)
            {
                result.Type = AttackResultType.Miss;
                Debug.LogError($"[SPELL-DMG] NO-DIRECT-DAMAGE: {spell?.DisplayName ?? "unknown"} directDamage=False modifierDamage={spell?.HasProjectileModifierDamage ?? false}");
                return result;
            }

            int chanceF32 = Math.Min(0x6400, Math.Max(0, spell.ChanceF32));
            bool consumedChanceRng = false;
            if (!isChainTarget && chanceF32 < 0x6400)
            {
                uint hitRaw = RngLedger.Generate(rng, "room", $"{spell.DisplayName ?? "spell"}:SpellEffect::CheckChance", "SpellEffect");
                consumedChanceRng = true;
                result.HitRoll = (int)(hitRaw % 25700);

                if (result.HitRoll >= chanceF32)
                {
                    result.Type = AttackResultType.Miss;
                    Debug.LogError($"[SPELL-DMG] MISS: {spell.DisplayName} hitRoll={result.HitRoll} >= chance={chanceF32} (1 RNG consumed)");
                    return result;
                }
            }

            int minDmg, maxDmg;

            if (spell.IsWeaponSkill)
            {
                ComputeWeaponSkillDamageRange(
                    attackerLevel, attackerStrength,
                    weaponDamageMultiplier, weaponVolatility,
                    skillLevel,
                    spell.SkillDamageModMin, spell.SkillDamageModMax, spell.SkillDamageModInc,
                    out minDmg, out maxDmg);
            }
            else if (spell.AttackType == AttackType.RANGED)
            {
                ComputeRangedDamageRange(
                    attackerLevel, attackerAgility,
                    weaponDamageMultiplier,
                    spell.DamageMod, spell.DamageVolatility,
                    out minDmg, out maxDmg);
            }
            else
            {
                ComputeSpellDamageRange(
                    attackerState,
                    ResolveSkillPowerLevelF32(spell, skillLevel),
                    attackerIntellect,
                    spell.DamageType,
                    spell.DamageMod, spell.DamageVolatility,
                    out minDmg, out maxDmg);
            }

            uint damageRaw = RngLedger.Generate(rng, "room", $"{spell.DisplayName ?? "spell"}:SpellEffect::damage", "SpellEffect::virtualA0");
            int damage = spell.IsWeaponSkill
                ? RollDamageRange(minDmg, maxDmg, damageRaw)
                : RollSpellDamageRange(minDmg, maxDmg, damageRaw);

            result.Type = AttackResultType.Hit;
            result.DamageF32 = damage;
            result.MinDamageF32 = minDmg;
            result.MaxDamageF32 = maxDmg;
            result.DamageRaw = damageRaw;
            result.DamageTypeId = ResolveDamageTypeId(spell.DamageType);
            result.CritDamagePercent = Math.Max(100, criticalDamagePercent);

            int rngCalls = (consumedChanceRng ? 1 : 0) + 1;
            string formulaTag = spell.IsWeaponSkill ? "WPNSKILL" : spell.AttackType == AttackType.RANGED ? "RANGED" : "MAGIC";
            string rollTag = spell.IsWeaponSkill ? "weapon-fixed" : "spell-hp";
            Debug.LogError($"[SPELL-DMG] HIT: {spell.DisplayName} [{formulaTag}] {(isChainTarget ? "CHAIN" : "PRIMARY")} dmg={damage / 256} ({damage}) range=[{minDmg / 256},{maxDmg / 256}] dmgRaw=0x{damageRaw:X8} roll={rollTag} hitRoll={result.HitRoll} ({rngCalls} RNG consumed)");
            return result;
        }

        public static SpellAttackResult ProcessProjectileModifierTick(
            MersenneTwister rng,
            int attackerLevel,
            int attackerIntellect,
            int attackerAgility,
            int attackerStrength,
            float weaponDamageMultiplier,
            float weaponVolatility,
            SpellData spell,
            Monster target,
            int skillLevel = 1,
            int criticalDamagePercent = 200,
            PlayerState attackerState = null,
            int spellCriticalThreshold = -1)
        {
            var result = new SpellAttackResult
            {
                Spell = spell,
                HitRoll = -1
            };

            if (rng == null || spell == null || target == null)
            {
                result.Type = AttackResultType.Miss;
                return result;
            }

            float damageMod = spell.ProjectileModifierDamageMod > 0f
                ? spell.ProjectileModifierDamageMod
                : spell.DamageMod;
            float damageVolatility = spell.ProjectileModifierDamageVolatility > 0f
                ? spell.ProjectileModifierDamageVolatility
                : spell.DamageVolatility;
            float critChance = spell.ProjectileModifierCriticalChance;
            AttackType modifierAttackType = spell.EffectiveProjectileModifierAttackType;
            DamageElement modifierDamageType = spell.EffectiveProjectileModifierDamageType;

            int minDmg;
            int maxDmg;
            if (modifierAttackType == AttackType.RANGED)
            {
                ComputeRangedDamageRange(
                    attackerLevel,
                    attackerAgility,
                    weaponDamageMultiplier,
                    damageMod,
                    damageVolatility,
                    out minDmg,
                    out maxDmg);
            }
            else
            {
                ComputeSpellDamageRange(
                    attackerState,
                    ResolveSkillPowerLevelF32(spell, skillLevel),
                    attackerIntellect,
                    modifierDamageType,
                    damageMod,
                    damageVolatility,
                    out minDmg,
                    out maxDmg);
            }

            uint damageRaw = RngLedger.Generate(rng, "room", "modifier:SpellEffect::damage", "SpellModifier");
            int damage = RollSpellDamageRange(minDmg, maxDmg, damageRaw);

            bool isCrit = false;
            int critThreshold = spellCriticalThreshold >= 0
                ? spellCriticalThreshold
                : (int)(critChance * 25600);
            if (critThreshold > 23040) critThreshold = 23040;
            if (critThreshold > 0)
            {
                uint critRaw = RngLedger.Generate(rng, "room", "modifier:SpellEffect::crit", "SpellModifier");
                int critRoll = (int)(critRaw % 25700);
                if (critRoll < critThreshold)
                {
                    isCrit = true;
                    int critPercent = Math.Max(100, criticalDamagePercent);
                    damage = (int)(((long)damage * critPercent) / 100L);
                }
                Debug.LogError($"[SPELL-MOD-DMG] CritRoll: {critRoll} vs threshold={critThreshold} ({critChance * 100:F0}%) critPct={Math.Max(100, criticalDamagePercent)} -> {(isCrit ? "CRIT" : "no crit")}");
            }

            result.Type = isCrit ? AttackResultType.Critical : AttackResultType.Hit;
            result.DamageF32 = damage;
            result.MinDamageF32 = minDmg;
            result.MaxDamageF32 = maxDmg;
            result.DamageRaw = damageRaw;
            result.DamageTypeId = ResolveDamageTypeId(modifierDamageType);
            result.CritDamagePercent = Math.Max(100, criticalDamagePercent);

            Debug.LogError($"[SPELL-MOD-DMG] {(isCrit ? "CRIT" : "HIT")}: {spell.DisplayName} modifier [{spell.ProjectileModifierEffectId ?? "modifier"}] atk={modifierAttackType} dmgType={modifierDamageType} dmg={damage / 256} ({damage}) range=[{minDmg / 256},{maxDmg / 256}] dmgRaw=0x{damageRaw:X8} mod={damageMod:F2} vol={damageVolatility:F2} critPct={Math.Max(100, criticalDamagePercent)}");
            return result;
        }

    }

    public class SpellAttackResult
    {
        public AttackResultType Type;
        public SpellData Spell;
        public int DamageF32;
        public int MinDamageF32;
        public int MaxDamageF32;
        public uint DamageRaw;
        public int DamageTypeId;
        public int HitRoll;
        public int CritDamagePercent;
    }

    public class WeaponDamageInput
    {
        public MersenneTwister Rng;
        public int AttackerLevel;
        public int DefenderLevel;
        public int AttackRating;
        public int DefenseRating;
        public int BlockChance;
        public int DamageLevel;
        public int DamageBonus;
        public int DamageMod;
        public int WeaponClassId;
        public int DamageTypeId;
        public int WeaponDamageF32;
        public int WeaponVolatilityF32;
        public int CritThreshold;
        public int CritDamagePercent;
        public string Source;
        public PlayerState AttackerState;
        public bool IncludeWeaponDamageAdds = true;
    }

    public class WeaponDamageResult
    {
        public AttackResultType Type = AttackResultType.Miss;
        public string ResultName = "MISS";
        public uint HitRaw;
        public uint BlockRaw;
        public uint DamageRaw;
        public int HitRoll;
        public int BlockRoll;
        public int HitThreshold;
        public int AttackRating;
        public int DefenseRating;
        public int AttackerLevel;
        public int DefenderLevel;
        public int BlockChance;
        public int DamageLevel;
        public int DamageBonus;
        public int DamageMod;
        public int WeaponClassId;
        public int DamageTypeId;
        public int WeaponDamageF32;
        public int WeaponVolatilityF32;
        public int MinDamageF32;
        public int MaxDamageF32;
        public int DamageF32;
        public uint DamageWire;
        public int TotalDamageF32;
        public uint TotalDamageWire;
        public List<WeaponDamageEvent> DamageAdds = new List<WeaponDamageEvent>();
        public int CritThreshold;
        public int CritDamagePercent;
        public PlayerState AttackerState;
        public bool IsHit;
        public bool IsBlocked;
        public bool IsCritical;
        public int RoomRngAfter;
    }

    public class WeaponDamageAddSlot
    {
        public string Element;
        public int DamageTypeId;
        public string[] WeaponAddStats;
        public string[] DamageBonusStats;
        public string[] DamageModStats;
    }

    public class WeaponDamageEvent
    {
        public string Element;
        public int DamageTypeId;
        public int DamageF32;
        public uint DamageWire;
        public int WeaponAdd;
        public int DamageBonus;
        public int DamageMod;
        public int WeaponDamageF32;
    }

    public enum AttackResultType
    {
        Miss,
        Block,
        Hit,
        Critical
    }
}
