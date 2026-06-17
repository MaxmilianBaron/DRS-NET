using System;
using System.Collections.Generic;
using DungeonRunners.Data;
using DungeonRunners.Engine;

namespace DungeonRunners.Combat
{
    public sealed class MonsterAttackProfile
    {
        public string GCType;
        public string ResolvedName;

        public float AttackSpeed;
        public float AttackRating;
        public float DefenseRating;
        public float DamageMod;
        public float CritChance;
        public float MaxHealth;
        public float HealthRegen;
        public float Speed;
        public int   FactionID;

        public int    WeaponID;
        public float  WeaponDamage;
        public string WeaponDamageType;
        public float  WeaponDamageVolatility;
        public float  WeaponRange;
        public float  WeaponCoolDown;
        public int    WeaponSpeed;
        public string WeaponClass;
        public bool   HasPrimaryWeapon;

        public string AttackStyle;
        public string IdleAction;
        public int   AgroRange;
        public int   ShoutRange;
        public int   LeashRange;
        public bool  Leashed;

        public override string ToString() =>
            $"MonsterAttackProfile[{GCType}: AS={AttackSpeed} AR={AttackRating} DR={DefenseRating} DMod={DamageMod} Crit={CritChance} HP={MaxHealth} " +
            $"wpn=[ID={WeaponID} dmg={WeaponDamage} type={WeaponDamageType} vol={WeaponDamageVolatility} " +
            $"range={WeaponRange} cd={WeaponCoolDown} spd={WeaponSpeed} class={WeaponClass}] " +
            $"style={AttackStyle} agro={AgroRange}]";
    }

    public sealed class MonsterAttackData
    {
        private static MonsterAttackData _instance;
        public static MonsterAttackData Instance => _instance ??= new MonsterAttackData();

        private readonly Dictionary<string, MonsterAttackProfile> _profiles =
            new Dictionary<string, MonsterAttackProfile>(StringComparer.OrdinalIgnoreCase);

        public bool IsInitialized { get; private set; }

        public int MonsterAttackSpeed { get; private set; }
        public int MonsterDamageMod { get; private set; }
        public int MonsterCriticalChance { get; private set; }
        public int MonsterStunMod { get; private set; }
        public int MonsterHealthRegen { get; private set; }
        public int MonsterPowerRegen { get; private set; }
        public int MonsterStunResist { get; private set; }
        public int MonsterMissChance { get; private set; }
        public int MonsterMissDamageMod { get; private set; }
        public int MonsterStunChance { get; private set; }

        public int HeroMissChance { get; private set; }
        public int HeroMissDamageMod { get; private set; }
        public int HeroStunChance { get; private set; }
        public int HeroCriticalChance { get; private set; }
        public int HeroStunResist { get; private set; }
        public int HeroStunMod { get; private set; }
        public int HeroAttackSpeed { get; private set; }
        public int HeroHealthRegen { get; private set; }
        public int HeroPowerRegen { get; private set; }

        public int WeaponDamagePerLevel { get; private set; }
        public int SkillDamagePerLevel { get; private set; }
        public float DPSModifier { get; private set; }

        public float MeleeDamagePerStrength { get; private set; }
        public float RangedDamagePerAgility { get; private set; }
        public float SkillDamagePerIntellect { get; private set; }
        public float DefenseRatingPerStrength { get; private set; }
        public float AttackRatingPerAgility { get; private set; }

        public void Init()
        {
            if (IsInitialized) return;

            var knobs = GCDatabase.Instance.GlobalKnobs;
            if (knobs == null)
            {
                Debug.LogError("[MONSTER-ATTACK-DATA] missing GlobalKnobs.gc multipliers=0");
                return;
            }

            MonsterAttackSpeed     = knobs.GetInt("MonsterAttackSpeed", 100);
            MonsterDamageMod       = knobs.GetInt("MonsterDamageMod", 0);
            MonsterCriticalChance  = knobs.GetInt("MonsterCriticalChance", 6);
            MonsterStunMod         = knobs.GetInt("MonsterStunMod", 100);
            MonsterHealthRegen     = knobs.GetInt("MonsterHealthRegen", 2);
            MonsterPowerRegen      = knobs.GetInt("MonsterPowerRegen", 2);
            MonsterStunResist      = knobs.GetInt("MonsterStunResist", 1);
            MonsterMissChance      = knobs.GetInt("MonsterMissChance", 100);
            MonsterMissDamageMod   = knobs.GetInt("MonsterMissDamageMod", 0);
            MonsterStunChance      = knobs.GetInt("MonsterStunChance", 50);

            HeroMissChance         = knobs.GetInt("HeroMissChance", 100);
            HeroMissDamageMod      = knobs.GetInt("HeroMissDamageMod", 0);
            HeroStunChance         = knobs.GetInt("HeroStunChance", 100);
            HeroCriticalChance     = knobs.GetInt("HeroCriticalChance", 3);
            HeroStunResist         = knobs.GetInt("HeroStunResist", 1);
            HeroStunMod            = knobs.GetInt("HeroStunMod", 100);
            HeroAttackSpeed        = knobs.GetInt("HeroAttackSpeed", 100);
            HeroHealthRegen        = knobs.GetInt("HeroHealthRegen", 2);
            HeroPowerRegen         = knobs.GetInt("HeroPowerRegen", 3);

            WeaponDamagePerLevel   = knobs.GetInt("WeaponDamagePerLevel", 10);
            SkillDamagePerLevel    = knobs.GetInt("SkillDamagePerLevel", 15);
            DPSModifier            = knobs.GetFloat("DPSModifier", 1.0f);

            MeleeDamagePerStrength    = knobs.GetFloat("MeleeDamagePerStrength", 2.3364f);
            RangedDamagePerAgility    = knobs.GetFloat("RangedDamagePerAgility", 2.124f);
            SkillDamagePerIntellect   = knobs.GetFloat("SkillDamagePerIntellect", 1.5f);
            DefenseRatingPerStrength  = knobs.GetFloat("DefenseRatingPerStrength", 14f);
            AttackRatingPerAgility    = knobs.GetFloat("AttackRatingPerAgility", 14f);

            IsInitialized = true;

            Debug.LogError(
                $"[MONSTER-ATTACK-DATA] GlobalKnobs cached. " +
                $"MonsterAttackSpeed={MonsterAttackSpeed} MonsterCriticalChance={MonsterCriticalChance} " +
                $"MonsterMissChance={MonsterMissChance} MonsterDamageMod={MonsterDamageMod} " +
                $"HeroCriticalChance={HeroCriticalChance} WeaponDamagePerLevel={WeaponDamagePerLevel} " +
                $"DPSModifier={DPSModifier:F2}");
        }

        public bool TryGetProfile(string gcType, out MonsterAttackProfile profile)
        {
            profile = null;
            if (string.IsNullOrEmpty(gcType)) return false;

            if (_profiles.TryGetValue(gcType, out profile))
                return profile != null;

            var node = GCDatabase.Instance.ResolveWithInheritance(gcType);
            if (node == null)
            {
                _profiles[gcType] = null;
                return false;
            }

            var desc = node.GetChild("Description");
            if (desc == null)
            {
                _profiles[gcType] = null;
                return false;
            }

            var built = new MonsterAttackProfile
            {
                GCType        = gcType,
                ResolvedName  = node.Name,

                AttackSpeed   = desc.GetFloat("AttackSpeed", 1.0f),
                AttackRating  = desc.GetFloat("AttackRating", 0f),
                DefenseRating = desc.GetFloat("DefenseRating", 0f),
                DamageMod     = desc.GetFloat("DamageMod", 0f),
                CritChance    = desc.GetFloat("CriticalChance", 0f),
                MaxHealth     = desc.GetFloat("MaxHealth", 0f),
                HealthRegen   = desc.GetFloat("HealthRegen", 0f),
                Speed         = desc.GetFloat("Speed", 0f),
                FactionID     = desc.GetInt("FactionID", 0),
            };

            var manips = node.GetChild("Manipulators");
            var primary = manips?.GetChild("PrimaryWeapon");
            if (primary != null)
            {
                built.HasPrimaryWeapon = true;
                built.WeaponID = primary.GetInt("ID", 0);

                var weaponMerged = primary;
                if (!string.IsNullOrEmpty(primary.Extends))
                {
                    var resolvedWeapon = GCDatabase.Instance.ResolveWithInheritance(primary.Extends);
                    if (resolvedWeapon != null)
                    {
                        weaponMerged = new GCNode
                        {
                            Name = primary.Name,
                            Extends = primary.Extends,
                        };
                        var parentDesc = resolvedWeapon.GetChild("Description");
                        var primaryDesc = primary.GetChild("Description");
                        var mergedDesc = new GCNode { Name = "Description" };
                        if (parentDesc != null)
                            foreach (var kvp in parentDesc.Properties) mergedDesc.Properties[kvp.Key] = kvp.Value;
                        if (primaryDesc != null)
                            foreach (var kvp in primaryDesc.Properties) mergedDesc.Properties[kvp.Key] = kvp.Value;
                        weaponMerged.Children["Description"] = mergedDesc;
                    }
                }

                var wpnDesc = weaponMerged.GetChild("Description");
                if (wpnDesc != null)
                {
                    built.WeaponDamage           = wpnDesc.GetFloat("Damage", 1.0f);
                    built.WeaponDamageType       = wpnDesc.GetString("DamageType", "");
                    built.WeaponDamageVolatility = wpnDesc.GetFloat("DamageVolatility", 0.5f);
                    built.WeaponRange            = wpnDesc.GetFloat("Range", 5f);
                    built.WeaponCoolDown         = wpnDesc.GetFloat("CoolDown", 1.75f);
                    built.WeaponSpeed            = wpnDesc.GetInt("WeaponSpeed", 100);
                    built.WeaponClass            = wpnDesc.GetString("WeaponClass", "");
                }
            }

            var beh = node.GetChild("Behavior");
            var behDesc = beh?.GetChild("Description");
            if (behDesc != null)
            {
                built.AttackStyle = behDesc.GetString("AttackStyle", "");
                built.IdleAction  = behDesc.GetString("IdleAction", "");
                built.AgroRange   = behDesc.GetInt("AgroRange", 0);
                built.ShoutRange  = behDesc.GetInt("ShoutRange", 0);
                built.LeashRange  = behDesc.GetInt("LeashRange", 0);
                built.Leashed     = behDesc.GetBool("Leashed", false);
            }

            _profiles[gcType] = built;
            profile = built;
            return true;
        }

        public int CachedProfileCount => _profiles.Count;
    }
}
