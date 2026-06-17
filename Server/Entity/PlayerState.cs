using DungeonRunners.Combat;
using DungeonRunners.Data;
using DungeonRunners.Core;
using DungeonRunners.Engine;
using System;
using System.Collections.Generic;

namespace DungeonRunners.Networking
{
    public class PlayerState
    {
        public float PositionX { get; set; } = 100f;
        public float PositionY { get; set; } = 100f;
        public GCObject ActiveItem { get; set; }

        public float WeaponDamage { get; set; } = 0f;
        public float WeaponDamageVolatility { get; set; } = 0f;
        public int WeaponLevel { get; set; } = 0;
        public string WeaponClass { get; set; } = "";
        public string WeaponDamageType { get; set; } = "";
        public string WeaponCategory { get; set; } = "";
        public bool WeaponStatsResolved { get; set; } = false;
        public int WeaponClassId { get; set; } = 0;
        public int DamageTypeId { get; set; } = -1;
        public int WeaponDamageLevel { get; set; } = 0;
        public int WeaponBaseDamage { get; set; } = 0;
        public bool WeaponBaseDamageTracksPlayerLevel { get; set; } = false;
        public string WeaponBaseDamageSource { get; set; } = "unresolved";
        public int WeaponRange { get; set; } = 0;
        public float WeaponCooldown { get; set; } = 0f;
        public float WeaponSpeed { get; set; } = 0f;
        public bool WeaponUsesProjectile { get; set; } = false;
        public float WeaponProjectileSpeed { get; set; } = 0f;
        public float WeaponProjectileSize { get; set; } = 0f;
        public int WeaponBurstCount { get; set; } = 1;
        public int MeleeAttackRatingModPercent { get; set; } = 0;
        public float MeleeAttackSpeedModPercent { get; set; } = 0f;
        public float RangeAttackSpeedModPercent { get; set; } = 0f;
        public float MagicDamageModPercent { get; set; } = 0f;
        public int MovementSpeedModPercent { get; private set; } = 0;
        public int MinMovementSpeedModValue { get; private set; } = 0;
        public int DamageTakenMod { get; set; } = 100;
        public int ArmorDefenseRating { get; set; } = 0;

        public int Strength { get; set; } = 10;
        public int Agility { get; set; } = 10;
        public int Intelligence { get; set; } = 10;
        public int Toughness { get; set; } = 10;
        public int Power { get; set; } = 10;
        private const int BASE_STAT_VALUE = 10;
        private int _allocatedStrength = 0;
        private int _allocatedAgility = 0;
        private int _allocatedEndurance = 0;
        private int _allocatedIntellect = 0;

        private int _level = 1;
        private string _className = "Fighter";
        public int Level => _level;
        public string ClassName => _className;
        public uint Experience { get; set; } = 0;
        public uint Gold { get; set; } = 0;

        private static readonly (int level, float value)[] XPCurve = new[]
        {
            (2, 10f),
            (3, 25f),
            (4, 45f),
            (5, 65f),
            (100, 5000f)
        };

        public uint GetXPThreshold()
        {
            int targetLevel = _level + 1;
            if (targetLevel > GCDatabase.Instance.GetKnobInt("MaxLevel", 100)) return uint.MaxValue;

            float kills = 0;
            for (int curveIndex = 0; curveIndex < XPCurve.Length; curveIndex++)
            {
                if (targetLevel <= XPCurve[curveIndex].level)
                {
                    if (curveIndex == 0)
                    {
                        kills = XPCurve[curveIndex].value;
                    }
                    else
                    {
                        float t = (float)(targetLevel - XPCurve[curveIndex - 1].level) / (XPCurve[curveIndex].level - XPCurve[curveIndex - 1].level);
                        kills = XPCurve[curveIndex - 1].value + t * (XPCurve[curveIndex].value - XPCurve[curveIndex - 1].value);
                    }
                    break;
                }
            }
            if (kills == 0) kills = XPCurve[XPCurve.Length - 1].value;

            return (uint)(kills * 100.0f);
        }
        public static uint GetXPPerKill(int monsterLevel, int playerLevel)
        {
            if (monsterLevel <= playerLevel - 5)
                return 0;

            int effectiveLevel = Math.Min(monsterLevel, playerLevel);

            long num = (long)(effectiveLevel << 8) << 8;
            int den = playerLevel << 8;
            int ratioF32 = (int)(num / den);

            uint xp = (uint)((ratioF32 * 500) >> 8);
            if (xp < 1) xp = 1;
            return xp;
        }

        public static uint GetBaseXPForLevel(int level)
        {
            float kills = 0;
            for (int curveIndex = 0; curveIndex < XPCurve.Length; curveIndex++)
            {
                if (level <= XPCurve[curveIndex].level)
                {
                    if (curveIndex == 0)
                        kills = XPCurve[curveIndex].value;
                    else
                    {
                        float t = (float)(level - XPCurve[curveIndex - 1].level) / (XPCurve[curveIndex].level - XPCurve[curveIndex - 1].level);
                        kills = XPCurve[curveIndex - 1].value + t * (XPCurve[curveIndex].value - XPCurve[curveIndex - 1].value);
                    }
                    break;
                }
            }
            if (kills == 0) kills = XPCurve[XPCurve.Length - 1].value;
            return (uint)(kills * 50);
        }

        public static uint GetClientThreshold(int nextLevel)
        {
            const int MULTIPLIER = 100;

            int targetLevelFixed = nextLevel << 8;

            for (int curveIndex = 0; curveIndex < XPCurve.Length; curveIndex++)
            {
                int levelFixed = (int)XPCurve[curveIndex].level << 8;
                int valueFixed = (int)XPCurve[curveIndex].value << 8;

                if (targetLevelFixed <= levelFixed)
                {
                    if (curveIndex == 0)
                        return (uint)((int)XPCurve[curveIndex].value * MULTIPLIER);

                    int previousLevelFixed = (int)XPCurve[curveIndex - 1].level << 8;
                    int previousValueFixed = (int)XPCurve[curveIndex - 1].value << 8;
                    int delta = valueFixed - previousValueFixed;

                    int interpolationQ16 = (int)((long)(targetLevelFixed - previousLevelFixed) << 16) / (levelFixed - previousLevelFixed);
                    int interpolatedValue = previousValueFixed + (int)((long)delta * interpolationQ16 >> 16);

                    return (uint)((interpolatedValue >> 8) * MULTIPLIER);
                }
            }
            return (uint)((int)XPCurve[XPCurve.Length - 1].value * MULTIPLIER);
        }


        public bool AddExperience(uint xp)
        {
            Experience += xp;
            bool didLevel = false;
            uint needed = GetClientThreshold(_level + 1);
            while (Experience >= needed && _level < GCDatabase.Instance.GetKnobInt("MaxLevel", 100))
            {
                int oldLevel = _level;
                uint oldHPWire = _currentHPWire;
                uint oldManaWire = _currentManaWire;
                uint oldMaxHPWire = MaxHPWire;
                uint oldMaxManaWire = MaxManaWire;

                _level++;
                Experience -= needed;
                _baseHPWire = CalculateBaseHP();
                _baseManaWire = CalculateMaxMana();
                _currentHPWire = MaxHPWire;
                _currentManaWire = MaxManaWire;
                HasClientHP = true;
                HasClientMana = true;
                SetEntitySynchInfoHP(_currentHPWire);
                Debug.LogError($"[LEVEL-UP-CLIENT] level={oldLevel}->{_level} hp={oldHPWire}->{_currentHPWire}/{oldMaxHPWire}->{MaxHPWire} mana={oldManaWire}->{_currentManaWire}/{oldMaxManaWire}->{MaxManaWire} sourceFunction=Hero::onAddExperience full-hp-mana next={GetClientThreshold(_level + 1)}");
                didLevel = true;
                needed = GetClientThreshold(_level + 1);
            }
            return didLevel;
        }

        private static uint HP_PER_LEVEL_WIRE => (uint)(GCDatabase.Instance.GetKnobInt("HeroHealthPerLevel", 16) * 256);
        private static int HP_PER_ENDURANCE => GCDatabase.Instance.GetKnobInt("HealthPerEndurance", 25);

        private uint _baseHPWire = 0;
        private uint _allocatedHPBonusWire = 0;
        private uint _equipmentHPBonusWire = 0;
        private uint _modifierHPBonusWire = 0;
        private int _passiveHPBonusWire = 0;
        private int _passiveStrengthMod = 0;
        private int _passiveAgilityMod = 0;
        private int _passiveEnduranceMod = 0;
        private int _passiveIntellectMod = 0;
        private int _passiveHealthPerEnduranceModPercent = 0;
        private int _passiveManaPerIntellectModPercent = 0;
        private uint _currentHPWire = 0;
        private uint _entitySynchInfoHPWire = 0;
        private float _entitySynchInfoHPTime = -1f;
        private double _entitySynchInfoHPCarry = 0d;
        private bool _passiveMaxTransition = false;
        private uint _passiveTransitionMaxWire = 0;
        private float _clientRegenSuppressUntil = -1f;
        private bool _hasObservedClientHP = false;
        private uint _lastObservedClientHPWire = 0;
        private float _lastObservedClientHPTime = 0f;
        private string _lastObservedClientHPSource = null;
        private const float DamageRegenSuppressSeconds = 10f;
        public const ushort DamageRegenSuppressTicks = 300;
        public const ushort ManaRegenSuppressTicks = 0x96;
        private uint _baseManaWire = 0;
        private uint _equipmentManaBonusWire = 0;
        private int _passiveManaBonusWire = 0;
        private uint _currentManaWire = 0;

        public Dictionary<string, int> EquipmentStats { get; private set; } = new Dictionary<string, int>();

        private int _regenFactor = 0;
        private int _hitPointRegenBonusBase = -1;
        private int _manaPointRegenBonusBase = -1;
        private ushort _regenCooldown = 0;
        private int _manaRegenFactor = 0;
        private ushort _manaRegenCooldown = 0;
        private readonly List<AttributeModifierRuntime> _attributeModifiers = new List<AttributeModifierRuntime>();

        private class AttributeModifierRuntime
        {
            public string ModifierType;
            public string ModifierKey;
            public string SkillPath;
            public string EffectPath;
            public string Source;
            public uint SourceEntityId;
            public int HitPointRegenBonus;
            public uint PowerLevel;
            public string StackRule;
            public ushort RemainingTicks;
            public bool RemoveOnDeath;
        }

        public class AttributeModifierRemoval
        {
            public string ModifierType;
            public string ModifierKey;
            public string SkillPath;
            public string EffectPath;
            public string Source;
            public uint SourceEntityId;
            public string SourceFunction;
        }

        public event Action<PlayerState, AttributeModifierRemoval> OnAttributeModifierRemoved;

        private static bool ShouldAcceptModifierStack(string stackRule, uint incomingPowerLevel, uint incomingDurationTicks, uint existingPowerLevel, uint existingDurationTicks)
        {
            if (string.IsNullOrWhiteSpace(stackRule))
                return true;
            if (string.Equals(stackRule, "UNIQUEBYSOURCE", StringComparison.OrdinalIgnoreCase))
                return incomingPowerLevel >= existingPowerLevel;
            if (string.Equals(stackRule, "UNIQUEBYTYPE", StringComparison.OrdinalIgnoreCase))
            {
                if (incomingPowerLevel <= existingPowerLevel)
                {
                    if (incomingDurationTicks == 0)
                        return existingDurationTicks != 0;
                    if (existingDurationTicks == 0 || incomingDurationTicks <= existingDurationTicks)
                        return false;
                }
                return true;
            }
            return true;
        }

        public bool ShouldAcceptAttributeModifier(string modifierKey, string modifierType, string stackRule, uint powerLevel, ushort durationTicks)
        {
            string runtimeKey = !string.IsNullOrWhiteSpace(modifierKey) ? modifierKey : modifierType;
            int existingIndex = _attributeModifiers.FindIndex(m =>
                string.Equals(!string.IsNullOrWhiteSpace(m.ModifierKey) ? m.ModifierKey : m.ModifierType, runtimeKey, StringComparison.OrdinalIgnoreCase));
            if (existingIndex < 0)
                return true;
            var existing = _attributeModifiers[existingIndex];
            return ShouldAcceptModifierStack(stackRule, powerLevel, durationTicks, existing.PowerLevel, existing.RemainingTicks);
        }

        private uint CalculateBaseHP()
        {
            return ClassPassiveData.CalculateHPWire(_level, BASE_STAT_VALUE + Math.Max(0, _allocatedEndurance), 0);
        }

        private static uint ClampWire(long wire)
        {
            if (wire <= 0) return 0;
            if (wire >= uint.MaxValue) return uint.MaxValue;
            return (uint)wire;
        }

        public void SetCurrentMana(uint wireMana, string source = null, bool applyCooldown = true)
        {
            uint oldMana = _currentManaWire;
            _currentManaWire = Math.Min(wireMana, MaxManaWire);
            HasClientMana = true;
            if (applyCooldown && _currentManaWire < oldMana)
                _manaRegenCooldown = ManaRegenSuppressTicks;
            if (_currentManaWire != oldMana)
                Debug.LogError($"[MANA] source={source ?? "SetCurrentMana"} mana={oldMana}->{_currentManaWire}/{MaxManaWire} cooldown={_manaRegenCooldown}");
        }
        private uint CalculateMaxMana()
        {
            return ClassPassiveData.CalculateManaWire(_level, BASE_STAT_VALUE + Math.Max(0, _allocatedIntellect), 0);
        }

        public uint Op12HP => MaxHPWire;
        public uint Op12MaxHP => MaxHPWire;
        public uint EntitySynchInfoHP
        {
            get
            {
                uint maxHP = _passiveMaxTransition ? _passiveTransitionMaxWire : MaxHPWire;
                if (!HasEntitySynchInfoHP) return _currentHPWire;
                uint current = maxHP > 0 ? Math.Min(_currentHPWire, maxHP) : _currentHPWire;
                uint entitySynchInfoHP = maxHP > 0 ? Math.Min(_entitySynchInfoHPWire, maxHP) : _entitySynchInfoHPWire;
                return HasClientHP && current < entitySynchInfoHP ? current : entitySynchInfoHP;
            }
        }

        public uint MaxHPWire => ClampWire((long)_baseHPWire + _allocatedHPBonusWire + _equipmentHPBonusWire + _modifierHPBonusWire + _passiveHPBonusWire);
        public uint CurrentHPWire => _currentHPWire;
        public uint MaxManaWire => ClampWire((long)_baseManaWire + _equipmentManaBonusWire + _passiveManaBonusWire);
        public uint CurrentManaWire => _currentManaWire;
        public uint MaxHPWireWithoutPassives => ClampWire((long)_baseHPWire + _allocatedHPBonusWire + _equipmentHPBonusWire + _modifierHPBonusWire);
        public uint MaxManaWireWithoutPassives => ClampWire((long)_baseManaWire + _equipmentManaBonusWire);
        public uint AllocatedHPBonusWire => _allocatedHPBonusWire;
        public uint EquipmentHPBonusWire => _equipmentHPBonusWire;
        public uint EquipmentManaBonusWire => _equipmentManaBonusWire;
        public uint ModifierHPBonusWire => _modifierHPBonusWire;
        public int PassiveHPBonusWire => _passiveHPBonusWire;
        public int PassiveManaBonusWire => _passiveManaBonusWire;
        public int AllocatedStrength => _allocatedStrength;
        public int AllocatedAgility => _allocatedAgility;
        public int AllocatedEndurance => _allocatedEndurance;
        public int AllocatedIntellect => _allocatedIntellect;
        public int ClientSpellStrength => Math.Max(1, BASE_STAT_VALUE + _allocatedStrength);
        public int ClientSpellAgility => Math.Max(1, BASE_STAT_VALUE + _allocatedAgility);
        public int ClientSpellIntellect => Math.Max(1, BASE_STAT_VALUE + _allocatedIntellect);
        public byte UpdateNumber = 0;
        public bool IsDamageImmune { get; set; } = false;
        public bool IsZoneSpawnDamageImmune { get; set; } = false;
        public bool HasAnyDamageImmunity => IsDamageImmune || IsZoneSpawnDamageImmune;
        public bool IsInvisible { get; set; } = false;
        public bool HasClientHP { get; private set; } = false;
        public bool HasEntitySynchInfoHP { get; private set; } = false;
        public bool HasClientMana { get; private set; } = false;
        public bool HasObservedClientHP => _hasObservedClientHP;
        public uint LastObservedClientHPWire => _lastObservedClientHPWire;
        public float LastObservedClientHPTime => _lastObservedClientHPTime;
        public string LastObservedClientHPSource => _lastObservedClientHPSource;
        public bool HasPreservableHP => HasClientHP || HasObservedClientHP;
        public uint AvatarHP
        {
            get => _currentHPWire;
            set
            {
                _currentHPWire = MaxHPWire > 0 ? Math.Min(value, MaxHPWire) : value;
                HasClientHP = true;
                SetEntitySynchInfoHP(_currentHPWire);
            }
        }

        public PlayerState()
        {
            ActiveItem = null;
        }

        public void InitializeStats(string className, int level)
        {
            _className = className ?? "Fighter";
            _level = Math.Max(1, level);
            _allocatedStrength = 0;
            _allocatedAgility = 0;
            _allocatedEndurance = 0;
            _allocatedIntellect = 0;
            _baseHPWire = CalculateBaseHP();
            _allocatedHPBonusWire = 0;
            _equipmentHPBonusWire = 0;
            _modifierHPBonusWire = 0;
            _passiveHPBonusWire = 0;
            _passiveStrengthMod = 0;
            _passiveAgilityMod = 0;
            _passiveEnduranceMod = 0;
            _passiveIntellectMod = 0;
            MeleeAttackRatingModPercent = 0;
            MeleeAttackSpeedModPercent = 0f;
            RangeAttackSpeedModPercent = 0f;
            MagicDamageModPercent = 0f;
            _passiveHealthPerEnduranceModPercent = 0;
            _passiveManaPerIntellectModPercent = 0;
            MovementSpeedModPercent = 0;
            MinMovementSpeedModValue = 0;
            _currentHPWire = _baseHPWire;
            _entitySynchInfoHPWire = _currentHPWire;
            _entitySynchInfoHPTime = -1f;
            _entitySynchInfoHPCarry = 0d;
            _clientRegenSuppressUntil = -1f;
            RefreshRegenFactors("InitializeStats");
            _regenCooldown = 0;
            _manaRegenCooldown = 0;
            _hasObservedClientHP = false;
            _lastObservedClientHPWire = 0;
            _lastObservedClientHPTime = 0f;
            _lastObservedClientHPSource = null;
            _baseManaWire = CalculateMaxMana();
            _equipmentManaBonusWire = 0;
            _passiveManaBonusWire = 0;
            _currentManaWire = _baseManaWire;
            HasClientHP = false;
            HasEntitySynchInfoHP = false;
            HasClientMana = false;
            EquipmentStats.Clear();
            Debug.LogError($"[PLAYERSTATE] INITIALIZED: {_className} Level {_level} | BaseHP={_baseHPWire} BaseMana={_baseManaWire}");
        }

        public void ApplyAllocatedStats(int strength, int agility, int endurance, int intellect)
        {
            bool preserveRegenClock = HasEntitySynchInfoHP && _entitySynchInfoHPTime >= 0f;
            if (preserveRegenClock)
                AdvanceEntitySynchInfoHP(Time.time, "ApplyAllocatedStats-pre");

            _allocatedStrength = Math.Max(0, strength);
            _allocatedAgility = Math.Max(0, agility);
            _allocatedEndurance = Math.Max(0, endurance);
            _allocatedIntellect = Math.Max(0, intellect);
            int finalStrength = BASE_STAT_VALUE + _allocatedStrength + _passiveStrengthMod;
            int finalAgility = BASE_STAT_VALUE + _allocatedAgility + _passiveAgilityMod;
            int finalEndurance = BASE_STAT_VALUE + _allocatedEndurance + _passiveEnduranceMod;
            int finalIntellect = BASE_STAT_VALUE + _allocatedIntellect + _passiveIntellectMod;
            Strength = Math.Max(1, finalStrength);
            Agility = Math.Max(1, finalAgility);
            Toughness = Math.Max(1, finalEndurance);
            Intelligence = Math.Max(1, finalIntellect);
            Power = Intelligence;
            _baseHPWire = CalculateBaseHP();
            _allocatedHPBonusWire = 0;
            _baseManaWire = CalculateMaxMana();

            if (HasClientHP)
            {
                if (_currentHPWire > MaxHPWire)
                    _currentHPWire = MaxHPWire;
            }
            else
            {
                _currentHPWire = MaxHPWire;
            }

            if (HasClientMana)
            {
                if (_currentManaWire > MaxManaWire) _currentManaWire = MaxManaWire;
            }
            else
            {
                _currentManaWire = MaxManaWire;
            }

            SetEntitySynchInfoHP(_currentHPWire, resetTickClock: !preserveRegenClock);
            Debug.LogError($"[ALLOC-STATS] STR={strength}->{Strength} AGI={agility}->{Agility} END={endurance}->{Toughness} INT={intellect}->{Intelligence} AllocHP={_allocatedHPBonusWire} MaxHP={MaxHPWire} CurrentHP={_currentHPWire} preserveRegenClock={preserveRegenClock}");
        }

        public void AddTotalHealthBonus(int hpBonus)
        {
            uint wireBonus = (uint)(hpBonus * 256);
            _equipmentHPBonusWire += wireBonus;
        }

        public void AddEnduranceBonus(int enduranceBonus)
        {
            int healthPerEnduranceMod = _passiveHealthPerEnduranceModPercent;
            int percent = Math.Max(0, 100 + healthPerEnduranceMod);
            int percentFixed = (int)(((long)percent * 0x10000L) / 0x6400L);
            long hpPerEnduranceFixed = ((long)HP_PER_ENDURANCE * 256L * percentFixed) >> 8;
            uint wireBonus = (uint)(((((long)enduranceBonus << 8) * hpPerEnduranceFixed) >> 16) * 256L);
            _equipmentHPBonusWire += wireBonus;
            Debug.LogError($"[HP-END] END+{enduranceBonus} hpeMod={healthPerEnduranceMod}% -> wire +{wireBonus}");
        }

        public void AddModifierHPBonus(uint wireBonus)
        {
            _modifierHPBonusWire += wireBonus;
        }

        public void SetPassiveBonuses(int hpWireBonus, int manaWireBonus, int meleeAttackRatingModPercent = 0, float meleeAttackSpeedModPercent = 0f, float rangeAttackSpeedModPercent = 0f, int strengthMod = 0, int agilityMod = 0, int enduranceMod = 0, int intellectMod = 0, int healthPerEnduranceModPercent = 0, int manaPerIntellectModPercent = 0, float magicDamageModPercent = 0f)
        {
            _passiveHPBonusWire = hpWireBonus;
            _passiveManaBonusWire = manaWireBonus;
            MeleeAttackRatingModPercent = meleeAttackRatingModPercent;
            MeleeAttackSpeedModPercent = meleeAttackSpeedModPercent;
            RangeAttackSpeedModPercent = rangeAttackSpeedModPercent;
            MagicDamageModPercent = magicDamageModPercent;
            _passiveStrengthMod = strengthMod;
            _passiveAgilityMod = agilityMod;
            _passiveEnduranceMod = enduranceMod;
            _passiveIntellectMod = intellectMod;
            _passiveHealthPerEnduranceModPercent = healthPerEnduranceModPercent;
            _passiveManaPerIntellectModPercent = manaPerIntellectModPercent;
            Strength = Math.Max(1, BASE_STAT_VALUE + _allocatedStrength + strengthMod);
            Agility = Math.Max(1, BASE_STAT_VALUE + _allocatedAgility + agilityMod);
            Toughness = Math.Max(1, BASE_STAT_VALUE + _allocatedEndurance + enduranceMod);
            Intelligence = Math.Max(1, BASE_STAT_VALUE + _allocatedIntellect + intellectMod);
            Power = Intelligence;
            if (HasClientHP)
            {
                if (_currentHPWire > MaxHPWire) _currentHPWire = MaxHPWire;
                HasClientHP = true;
            }
            else
            {
                _currentHPWire = MaxHPWire;
            }
            SetEntitySynchInfoHP(_currentHPWire);
            if (HasClientMana)
            {
                if (_currentManaWire > MaxManaWire) _currentManaWire = MaxManaWire;
            }
            else
            {
                _currentManaWire = MaxManaWire;
            }
        }

        public void ApplyMaxHPModifier(uint wireBonusToAdd)
        {
            uint oldMax = MaxHPWire;
            _modifierHPBonusWire += wireBonusToAdd;
            Debug.LogError($"[HP-MOD] MaxHP: {oldMax} -> {MaxHPWire} (CurrentHP stays {_currentHPWire})");
        }

        public void RemoveMaxHPModifier(uint wireBonusToRemove)
        {
            _modifierHPBonusWire = wireBonusToRemove > _modifierHPBonusWire
                ? 0 : _modifierHPBonusWire - wireBonusToRemove;
            if (_currentHPWire > MaxHPWire) _currentHPWire = MaxHPWire;
            SetEntitySynchInfoHP(_currentHPWire);
            Debug.LogError($"[HP-MOD] MaxHP now {MaxHPWire} (CurrentHP={_currentHPWire})");
        }

        public void ClearEquipmentBonuses()
        {
            _equipmentHPBonusWire = 0;
            _modifierHPBonusWire = 0;
            _equipmentManaBonusWire = 0;
            WeaponDamage = 0f;
            WeaponDamageVolatility = 0f;
            WeaponLevel = 0;
            WeaponClass = "";
            WeaponDamageType = "";
            WeaponCategory = "";
            WeaponStatsResolved = false;
            WeaponClassId = 0;
            DamageTypeId = -1;
            WeaponDamageLevel = 0;
            WeaponBaseDamage = 0;
            WeaponBaseDamageTracksPlayerLevel = false;
            WeaponBaseDamageSource = "unresolved";
            WeaponRange = 0;
            WeaponCooldown = 0f;
            WeaponSpeed = 0f;
            WeaponUsesProjectile = false;
            WeaponProjectileSpeed = 0f;
            WeaponProjectileSize = 0f;
            WeaponBurstCount = 1;
            ArmorDefenseRating = 0;
            EquipmentStats.Clear();
        }

        public void SetMovementSpeedModifiers(int speedModPercent, int minSpeedModValue)
        {
            MovementSpeedModPercent = speedModPercent;
            MinMovementSpeedModValue = Math.Max(0, minSpeedModValue);
        }

        public void AddArmorDefenseRating(int defenseRating)
        {
            if (defenseRating > 0)
                ArmorDefenseRating += defenseRating;
        }

        public void AddManaBonus(int manaBonus)
        {
            uint wireBonus = (uint)(manaBonus * 256);
            _equipmentManaBonusWire += wireBonus;
        }

        public void AddIntellectManaBonus(int intellectBonus)
        {
            int manaPerIntellectMod = _passiveManaPerIntellectModPercent;
            int percent = Math.Max(0, 100 + manaPerIntellectMod);
            int percentFixed = (int)(((long)percent * 0x10000L) / 0x6400L);
            long manaPerIntellectFixed = ((long)ClassPassiveData.POWER_PER_INTELLECT * 256L * percentFixed) >> 8;
            uint wireBonus = (uint)(((((long)intellectBonus << 8) * manaPerIntellectFixed) >> 16) * 256L);
            _equipmentManaBonusWire += wireBonus;
            Debug.LogError($"[MANA-INT] INT+{intellectBonus} mpiMod={manaPerIntellectMod}% -> wire +{wireBonus}");
        }

        public void RecalculateCurrentHP()
        {
            uint newMaxHP = MaxHPWire;
            if (HasClientHP)
            {
                if (_currentHPWire > newMaxHP)
                    _currentHPWire = newMaxHP;
                HasClientHP = true;
                Debug.LogError($"[HP-FINAL] Kept client HP: {_currentHPWire} (max={newMaxHP}, EntitySynchInfoHP={EntitySynchInfoHP})");
            }
            else
            {
                _currentHPWire = newMaxHP;
                Debug.LogError($"[HP-FINAL] base={_baseHPWire} + allocated={_allocatedHPBonusWire} + equip={_equipmentHPBonusWire} + mod={_modifierHPBonusWire} + passive={_passiveHPBonusWire} = {_currentHPWire} (EntitySynchInfoHP={EntitySynchInfoHP})");
            }
            SetEntitySynchInfoHP(_currentHPWire);
        }


        public bool ApplyOnDamageCallback(uint appliedDamageWire, float clientNow, string source = null)
        {
            if (appliedDamageWire == 0 || EquipmentStats == null) return false;
            int hpSteal = GetEquipmentStat("HIT_POINT_STEAL", "HITPOINTSTEAL");
            int manaSteal = GetEquipmentStat("MANA_POINT_STEAL", "MANAPOINTSTEAL", "MANA_STEAL", "MANASTEAL");
            if (hpSteal <= 0 && manaSteal <= 0) return false;

            if (clientNow >= 0f)
                AdvanceEntitySynchInfoHP(clientNow, $"{source ?? "Damage::apply"}-onDamageCallback-pre");

            uint oldHP = _currentHPWire;
            uint oldEntitySynchInfoHP = _entitySynchInfoHPWire;
            uint oldMana = _currentManaWire;
            uint maxHP = MaxHPWire;
            uint maxMana = MaxManaWire;

            if (hpSteal > 0 && maxHP > 0 && _currentHPWire > 0)
            {
                ulong heal = ((ulong)appliedDamageWire * (uint)hpSteal) / 100UL;
                if (heal > 0)
                {
                    ulong next = (ulong)_currentHPWire + heal;
                    _currentHPWire = next >= maxHP ? maxHP : (uint)next;
                    ClearObservedClientHP();
                    SetEntitySynchInfoHP(_currentHPWire, resetTickClock: clientNow >= 0f, clientTime: clientNow >= 0f ? clientNow : (float?)null);
                    HasClientHP = true;
                }
            }

            if (manaSteal > 0 && maxMana > 0)
            {
                ulong restore = ((ulong)appliedDamageWire * (uint)manaSteal) / 100UL;
                if (restore > 0)
                {
                    ulong next = (ulong)_currentManaWire + restore;
                    _currentManaWire = next >= maxMana ? maxMana : (uint)next;
                    HasClientMana = true;
                }
            }

            bool changed = oldHP != _currentHPWire || oldEntitySynchInfoHP != _entitySynchInfoHPWire || oldMana != _currentManaWire;
            if (changed)
                Debug.LogError($"[DAMAGE-CALLBACK] source=player hpSteal={hpSteal} manaSteal={manaSteal} appliedWire={appliedDamageWire} hp={oldHP}->{_currentHPWire}/{maxHP} entitySynchInfoHP={oldEntitySynchInfoHP}->{_entitySynchInfoHPWire} mana={oldMana}->{_currentManaWire}/{maxMana} sourceFunction=Unit::onDamageCallback@0x0050C470");
            return changed;
        }

        private int GetEquipmentStat(params string[] keys)
        {
            if (EquipmentStats == null || keys == null) return 0;
            foreach (string key in keys)
                if (!string.IsNullOrEmpty(key) && EquipmentStats.TryGetValue(key, out int value))
                    return value;
            return 0;
        }

        public void TakeDamage(uint wireAmount)
        {
            ApplyDamage(wireAmount, true, null, false);
        }

        public void TakeDamage(uint wireAmount, float clientDamageTime, bool advanceBeforeDamage = false)
        {
            ApplyDamage(wireAmount, true, clientDamageTime, advanceBeforeDamage);
        }

        public void TakeQueriedDamage(uint wireAmount, float clientDamageTime, bool advanceBeforeDamage = false)
        {
            ApplyDamage(wireAmount, true, clientDamageTime, advanceBeforeDamage, false);
        }

        public void TakeRuntimeDamage(uint wireAmount)
        {
            ApplyDamage(wireAmount, false, null, false);
        }

        public void TakeRuntimeDamage(uint wireAmount, float clientDamageTime, bool advanceBeforeDamage = false)
        {
            ApplyDamage(wireAmount, false, clientDamageTime, advanceBeforeDamage);
        }

        public void ApplyQueriedDamage(uint wireAmount, float clientDamageTime, bool advanceBeforeDamage = false)
        {
            float? damageTime = clientDamageTime >= 0f ? clientDamageTime : (float?)null;
            ApplyDamage(wireAmount, true, damageTime, advanceBeforeDamage, false);
        }

        private void ApplyDamage(uint wireAmount, bool updateEntitySynchInfo, float? clientDamageTime, bool advanceBeforeDamage, bool applyDamageTakenMod = true)
        {
            if (advanceBeforeDamage && (clientDamageTime.HasValue || _entitySynchInfoHPTime >= 0f))
                AdvanceEntitySynchInfoHP(clientDamageTime ?? _entitySynchInfoHPTime, updateEntitySynchInfo ? "TakeDamage-pre" : "TakeRuntimeDamage-pre");
            if (HasAnyDamageImmunity)
            {
                Debug.LogError($"[TAKEDAMAGE] Immune: {wireAmount} ignored at hp={_currentHPWire}");
                return;
            }
            uint adjustedWireAmount = applyDamageTakenMod ? ApplyDamageTakenMod(wireAmount, DamageTakenMod) : wireAmount;
            Debug.LogError($"[TAKEDAMAGE] Before: {_currentHPWire}, Subtracting: {adjustedWireAmount}");
            _currentHPWire = adjustedWireAmount > _currentHPWire ? 0 : _currentHPWire - adjustedWireAmount;
            HasClientHP = true;
            if (updateEntitySynchInfo)
                SetEntitySynchInfoHP(_currentHPWire, resetTickClock: clientDamageTime.HasValue, clientTime: clientDamageTime);
            ApplyDamageRegenCooldown(clientDamageTime);
            if (_currentHPWire == 0)
                ClearAttributeModifiers("death-damage");
            Debug.LogError($"[TAKEDAMAGE] After: {_currentHPWire}");
        }

        private static uint ApplyDamageTakenMod(uint wireAmount, int damageTakenMod)
        {
            if (wireAmount == 0) return 0;
            if (damageTakenMod < 1) return 0;
            ulong scaled = (ulong)wireAmount * (uint)damageTakenMod / 100UL;
            if (scaled >= uint.MaxValue) return uint.MaxValue;
            return (uint)scaled;
        }

        public void Heal(uint wireAmount)
        {
            _currentHPWire = Math.Min(_currentHPWire + wireAmount, MaxHPWire);
            HasClientHP = true;
            ClearObservedClientHP();
            SetEntitySynchInfoHP(_currentHPWire);
        }

        public void SetCurrentHP(uint wireHP, bool applyDamageCooldown = false)
        {
            _currentHPWire = Math.Min(wireHP, MaxHPWire);
            HasClientHP = true;
            ClearObservedClientHP();
            SetEntitySynchInfoHP(_currentHPWire);
            if (applyDamageCooldown && _currentHPWire < MaxHPWire)
                ApplyDamageRegenCooldown();
        }

        public void SetCurrentHPDeferClamp(uint wireHP)
        {
            _currentHPWire = wireHP;
            HasClientHP = true;
            ClearObservedClientHP();
            _entitySynchInfoHPWire = wireHP;
            HasEntitySynchInfoHP = true;
            _entitySynchInfoHPTime = Time.time;
            _entitySynchInfoHPCarry = 0d;
        }

        public void BeginPassiveMaxTransition(uint oldMaxWire)
        {
            if (oldMaxWire == 0) return;
            _passiveMaxTransition = true;
            _passiveTransitionMaxWire = oldMaxWire;
            Debug.LogError($"[MAX-TRANSITION] begin oldMax={oldMaxWire} newMax={MaxHPWire} curHp={_currentHPWire}");
        }

        public void SetClientReportedHP(uint wireHP, bool updateRuntimeHP = true)
        {
            ObserveClientHP(wireHP, "SetClientReportedHP");
        }

        public void ObserveClientHP(uint wireHP, string source)
        {
            uint clampedHP = Math.Min(wireHP, MaxHPWire);
            _hasObservedClientHP = true;
            _lastObservedClientHPWire = clampedHP;
            _lastObservedClientHPTime = Time.time;
            _lastObservedClientHPSource = source ?? "unknown";
        }

        public void RestoreToFull()
        {
            _currentHPWire = MaxHPWire;
            _currentManaWire = MaxManaWire;
            HasClientHP = true;
            HasClientMana = true;
            _regenCooldown = 0;
            _manaRegenCooldown = 0;
            ClearAttributeModifiers("RestoreToFull");
            ClearObservedClientHP();
            SetEntitySynchInfoHP(_currentHPWire);
        }

        private void ClearObservedClientHP()
        {
            _hasObservedClientHP = false;
            _lastObservedClientHPWire = 0;
            _lastObservedClientHPTime = 0f;
            _lastObservedClientHPSource = null;
        }
        private void SetEntitySynchInfoHP(uint wireHP, bool resetTickClock = true, float? clientTime = null)
        {
            uint maxHP = MaxHPWire;
            _entitySynchInfoHPWire = maxHP > 0 ? Math.Min(wireHP, maxHP) : wireHP;
            HasEntitySynchInfoHP = true;
            if (resetTickClock)
            {
                _entitySynchInfoHPTime = clientTime ?? Time.time;
                _entitySynchInfoHPCarry = 0d;
            }
        }

        private int ResolveHitPointRegenBase()
        {
            return GCDatabase.Instance?.GetKnobInt("HeroHealthRegen", 2) ?? 2;
        }

        public void AdvanceEntitySynchInfoHP(float now, string source = null, bool clientDriven = false)
        {
            if (!HasEntitySynchInfoHP)
            {
                SetEntitySynchInfoHP(_currentHPWire, clientTime: now);
                return;
            }

            uint maxHP = MaxHPWire;
            if (_passiveMaxTransition)
            {
                if (clientDriven)
                {
                    _passiveMaxTransition = false;
                    Debug.LogError($"[MAX-TRANSITION] end source={source ?? "client-mover"} newMax={maxHP} curHp={_currentHPWire}");
                }
                else
                    maxHP = _passiveTransitionMaxWire;
            }
            uint maxMana = MaxManaWire;
            if (maxHP > 0 && _entitySynchInfoHPWire > maxHP)
                _entitySynchInfoHPWire = maxHP;
            if (_currentHPWire > maxHP)
                _currentHPWire = maxHP;
            if (_currentManaWire > maxMana)
                _currentManaWire = maxMana;

            uint oldCurrentHP = _currentHPWire;
            uint oldEntitySynchInfoHP = _entitySynchInfoHPWire;
            uint oldMana = _currentManaWire;
            ushort oldHPCooldown = _regenCooldown;
            ushort oldManaCooldown = _manaRegenCooldown;
            int totalModifierDelta = 0;
            int totalModifierTicks = 0;
            int lastModifierBonus = ResolveHitPointRegenBonus();

            if (maxHP > 0)
            {
                uint runtimeHP = Math.Min(maxHP, _currentHPWire);
                _currentHPWire = runtimeHP;
                _entitySynchInfoHPWire = runtimeHP;
            }

            if (maxHP == 0 || (_entitySynchInfoHPWire == 0 && _currentHPWire == 0))
            {
                _entitySynchInfoHPTime = now;
                _entitySynchInfoHPCarry = 0d;
                return;
            }

            if (_entitySynchInfoHPTime < 0f)
            {
                _entitySynchInfoHPTime = now;
                _entitySynchInfoHPCarry = 0d;
                if (oldCurrentHP != _currentHPWire || oldEntitySynchInfoHP != _entitySynchInfoHPWire)
                    Debug.LogError($"[PLAYER-REGEN] source={source ?? "init"} hp={oldCurrentHP}->{_currentHPWire} entitySynchInfoHP={oldEntitySynchInfoHP}->{_entitySynchInfoHPWire} mana={oldMana}->{_currentManaWire}/{maxMana} hpCooldown={oldHPCooldown}->{_regenCooldown} manaCooldown={oldManaCooldown}->{_manaRegenCooldown} ticks=0 hpFactor={_regenFactor} manaFactor={_manaRegenFactor}");
                return;
            }

            const double clientTickSeconds = 1d / 30d;
            double elapsed = now - _entitySynchInfoHPTime + _entitySynchInfoHPCarry;
            int ticks = (int)(elapsed / clientTickSeconds);
            if (ticks <= 0)
            {
                _entitySynchInfoHPCarry = elapsed;
                _entitySynchInfoHPTime = now;
                if (oldCurrentHP != _currentHPWire || oldEntitySynchInfoHP != _entitySynchInfoHPWire)
                    Debug.LogError($"[PLAYER-REGEN] source={source ?? "entity-synch-info"} hp={oldCurrentHP}->{_currentHPWire} entitySynchInfoHP={oldEntitySynchInfoHP}->{_entitySynchInfoHPWire} mana={oldMana}->{_currentManaWire}/{maxMana} hpCooldown={oldHPCooldown}->{_regenCooldown} manaCooldown={oldManaCooldown}->{_manaRegenCooldown} ticks=0 hpFactor={_regenFactor} manaFactor={_manaRegenFactor}");
                return;
            }

            if (_passiveMaxTransition)
            {
                _passiveMaxTransition = false;
                maxHP = MaxHPWire;
                if (maxHP > 0 && _currentHPWire > maxHP)
                    _currentHPWire = maxHP;
                if (maxHP > 0 && _entitySynchInfoHPWire > maxHP)
                    _entitySynchInfoHPWire = maxHP;
                Debug.LogError($"[MAX-TRANSITION] end source=tick-clamp newMax={maxHP} curHp={_currentHPWire} sourceFunction=Unit::update@0x005093E0");
            }

            if (_regenFactor == 0)
                _regenFactor = ResolveHitPointRegenBase();
            if (_manaRegenFactor == 0)
                _manaRegenFactor = _regenFactor;

            uint runtime = _currentHPWire;
            uint mana = _currentManaWire;
            for (int tickIndex = 0; tickIndex < ticks; tickIndex++)
            {
                AdvanceAttributeModifierTick(source ?? "AdvanceEntitySynchInfoHP");
                int hitPointRegenBonus = ResolveHitPointRegenBonus();
                lastModifierBonus = hitPointRegenBonus;
                if (_regenCooldown > 0)
                    _regenCooldown--;
                if (_manaRegenCooldown > 0)
                    _manaRegenCooldown--;

                if (runtime > 0 && (runtime < maxHP || hitPointRegenBonus < 0))
                {
                    int regenDelta = CombatRuntime.ComputeUnitRegenDeltaWire(maxHP, _regenFactor, 0, hitPointRegenBonus, _regenCooldown > 0);
                    if (regenDelta != 0)
                    {
                        uint beforeRuntime = runtime;
                        runtime = CombatRuntime.ApplyUnitHPShiftWire(runtime, maxHP, regenDelta);
                        if (hitPointRegenBonus != 0 && beforeRuntime != runtime)
                        {
                            totalModifierTicks++;
                            totalModifierDelta += (int)runtime - (int)beforeRuntime;
                        }
                    }
                }

                if (maxMana > 0 && mana < maxMana)
                {
                    int manaRegenDelta = CombatRuntime.ComputeUnitRegenDeltaWire(maxMana, _manaRegenFactor, 0, ManaPointRegenBonus, _manaRegenCooldown > 0);
                    if (manaRegenDelta != 0)
                        mana = CombatRuntime.ApplyUnitHPShiftWire(mana, maxMana, manaRegenDelta);
                }
            }

            _currentHPWire = runtime;
            _entitySynchInfoHPWire = runtime;
            if (_currentHPWire == 0)
                ClearAttributeModifiers("death-regen");
            _currentManaWire = mana;
            if (_currentHPWire > 0)
            {
                HasClientHP = true;
                HasEntitySynchInfoHP = true;
            }
            if (oldMana != _currentManaWire)
                HasClientMana = true;
            _entitySynchInfoHPCarry = elapsed - ticks * clientTickSeconds;
            _entitySynchInfoHPTime = now;
            if (oldCurrentHP != _currentHPWire
                || oldEntitySynchInfoHP != _entitySynchInfoHPWire
                || oldMana != _currentManaWire
                || (oldHPCooldown > 0 && _regenCooldown == 0)
                || (oldManaCooldown > 0 && _manaRegenCooldown == 0))
            {
                Debug.LogError($"[PLAYER-REGEN] source={source ?? "tick"} hp={oldCurrentHP}->{_currentHPWire} entitySynchInfoHP={oldEntitySynchInfoHP}->{_entitySynchInfoHPWire} mana={oldMana}->{_currentManaWire}/{maxMana} hpCooldown={oldHPCooldown}->{_regenCooldown} manaCooldown={oldManaCooldown}->{_manaRegenCooldown} ticks={ticks} hpFactor={_regenFactor} manaFactor={_manaRegenFactor}");
            }
            if (totalModifierTicks > 0)
            {
                Debug.LogError($"[PLAYER-REGEN-MOD] source={source ?? "tick"} hp={oldCurrentHP}->{_currentHPWire}/{maxHP} ticks={ticks} modTicks={totalModifierTicks} bonus={lastModifierBonus} modDelta={totalModifierDelta} active={_attributeModifiers.Count} sourceFunction=Unit::update@0x005093E0 attr=HIT_POINT_REGEN_BONUS");
            }
        }

        public bool ApplyHitPointRegenBonusModifier(string modifierType, int hitPointRegenBonus, ushort durationTicks, bool removeOnDeath, float clientNow, string source = null, string modifierKey = null, uint sourceEntityId = 0, string skillPath = null, string effectPath = null, uint powerLevel = 0, string stackRule = null)
        {
            if (string.IsNullOrWhiteSpace(modifierType) || hitPointRegenBonus == 0)
                return false;

            string runtimeKey = !string.IsNullOrWhiteSpace(modifierKey) ? modifierKey : modifierType;
            int existingIndex = _attributeModifiers.FindIndex(m =>
                string.Equals(!string.IsNullOrWhiteSpace(m.ModifierKey) ? m.ModifierKey : m.ModifierType, runtimeKey, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0 && !ShouldAcceptModifierStack(stackRule, powerLevel, durationTicks, _attributeModifiers[existingIndex].PowerLevel, _attributeModifiers[existingIndex].RemainingTicks))
            {
                Debug.LogError($"[PLAYER-MODIFIER] reject type={modifierType} key={runtimeKey} hpRegenBonus={hitPointRegenBonus} durationTicks={durationTicks} power={powerLevel} existingPower={_attributeModifiers[existingIndex].PowerLevel} existingRemaining={_attributeModifiers[existingIndex].RemainingTicks} stack={stackRule ?? ""} source={source ?? "unknown"} sourceFunction=Modifiers::addModifierLocal@0x00501770");
                return false;
            }

            if (clientNow >= 0f)
                AdvanceEntitySynchInfoHP(clientNow, $"{source ?? "modifier"}-pre-attach");

            var runtime = new AttributeModifierRuntime
            {
                ModifierType = modifierType,
                ModifierKey = runtimeKey,
                SkillPath = skillPath,
                EffectPath = effectPath,
                Source = source ?? "unknown",
                SourceEntityId = sourceEntityId,
                HitPointRegenBonus = hitPointRegenBonus,
                PowerLevel = powerLevel,
                StackRule = stackRule,
                RemainingTicks = durationTicks,
                RemoveOnDeath = removeOnDeath
            };

            if (existingIndex >= 0)
                _attributeModifiers[existingIndex] = runtime;
            else
                _attributeModifiers.Add(runtime);

            Debug.LogError($"[PLAYER-MODIFIER] apply type={modifierType} key={runtimeKey} hpRegenBonus={hitPointRegenBonus} durationTicks={durationTicks} power={powerLevel} stack={stackRule ?? ""} replace={existingIndex >= 0} removeOnDeath={removeOnDeath} source={source ?? "unknown"} sourceFunction=SpellModEffect::doEffect@0x00554460 Modifiers::addModifierLocal@0x00501770");
            return true;
        }

        private int ResolveHitPointRegenBonus()
        {
            if (_hitPointRegenBonusBase < 0)
            {
                var attr = GCDatabase.Instance?.Resolve("AttributesPAL.HitPointRegenB");
                _hitPointRegenBonusBase = attr?.GetInt("Value", 1) ?? 1;
            }
            int bonus = _hitPointRegenBonusBase;
            foreach (var mod in _attributeModifiers)
                bonus += mod.HitPointRegenBonus;
            return bonus;
        }

        private int ManaPointRegenBonus
        {
            get
            {
                if (_manaPointRegenBonusBase < 0)
                {
                    var attr = GCDatabase.Instance?.Resolve("AttributesPAL.ManaRegenB");
                    _manaPointRegenBonusBase = attr?.GetInt("Value", 1) ?? 1;
                }
                return _manaPointRegenBonusBase;
            }
        }

        private void EmitAttributeModifierRemoved(AttributeModifierRuntime mod, string source, string sourceFunction)
        {
            if (mod == null)
                return;
            OnAttributeModifierRemoved?.Invoke(this, new AttributeModifierRemoval
            {
                ModifierType = mod.ModifierType,
                ModifierKey = !string.IsNullOrWhiteSpace(mod.ModifierKey) ? mod.ModifierKey : mod.ModifierType,
                SkillPath = mod.SkillPath,
                EffectPath = mod.EffectPath,
                Source = source ?? mod.Source ?? "unknown",
                SourceEntityId = mod.SourceEntityId,
                SourceFunction = sourceFunction
            });
        }

        private void AdvanceAttributeModifierTick(string source)
        {
            for (int modifierIndex = _attributeModifiers.Count - 1; modifierIndex >= 0; modifierIndex--)
            {
                var mod = _attributeModifiers[modifierIndex];
                if (mod.RemainingTicks == 0)
                    continue;
                mod.RemainingTicks--;
                if (mod.RemainingTicks == 0)
                {
                    Debug.LogError($"[PLAYER-MODIFIER] expire type={mod.ModifierType} key={mod.ModifierKey ?? mod.ModifierType} hpRegenBonus={mod.HitPointRegenBonus} source={source ?? "unknown"} sourceFunction=Modifier::update@0x004FF1B0");
                    EmitAttributeModifierRemoved(mod, source, "Modifiers::update@0x00501E50 Modifiers::removeModifierLocal@0x00501B50");
                    _attributeModifiers.RemoveAt(modifierIndex);
                }
            }
        }

        private void ClearAttributeModifiers(string source)
        {
            for (int modifierIndex = _attributeModifiers.Count - 1; modifierIndex >= 0; modifierIndex--)
            {
                if (!_attributeModifiers[modifierIndex].RemoveOnDeath && source != "RestoreToFull")
                    continue;
                Debug.LogError($"[PLAYER-MODIFIER] remove type={_attributeModifiers[modifierIndex].ModifierType} key={_attributeModifiers[modifierIndex].ModifierKey ?? _attributeModifiers[modifierIndex].ModifierType} source={source ?? "unknown"} sourceFunction=ModifierDesc.RemoveOnDeath");
                EmitAttributeModifierRemoved(_attributeModifiers[modifierIndex], source, "ModifierDesc.RemoveOnDeath Modifiers::processRemoveModifier@0x00502390");
                _attributeModifiers.RemoveAt(modifierIndex);
            }
        }

        public int RemoveAttributeModifiersFromSource(uint sourceEntityId, string source = null)
        {
            if (sourceEntityId == 0 || _attributeModifiers.Count == 0)
                return 0;
            int removed = 0;
            for (int modifierIndex = _attributeModifiers.Count - 1; modifierIndex >= 0; modifierIndex--)
            {
                var mod = _attributeModifiers[modifierIndex];
                if (mod.SourceEntityId != sourceEntityId)
                    continue;
                Debug.LogError($"[PLAYER-MODIFIER] remove-source type={mod.ModifierType} key={mod.ModifierKey ?? mod.ModifierType} sourceEntity={sourceEntityId} source={source ?? "unknown"} sourceFunction=Modifier::update@0x004FF1B0 Modifiers::processRemoveModifier@0x00502390");
                EmitAttributeModifierRemoved(mod, source ?? "source-unit-removed", "Modifier::update@0x004FF1B0 Modifiers::processRemoveModifier@0x00502390");
                _attributeModifiers.RemoveAt(modifierIndex);
                removed++;
            }
            return removed;
        }
        public void RefreshRegenFactors(string source = null)
        {
            _regenFactor = ResolveHitPointRegenBase();
            _manaRegenFactor = _regenFactor;
            Debug.LogError($"[REGEN] source={source ?? "RefreshRegenFactors"} hpRegenFactor={_regenFactor} manaRegenFactor={_manaRegenFactor}");
        }

        public void SetRegenFactor(int hitPointRegen, int hitPointRegenMod = 0, int hitPointRegenBonus = 0)
        {
            _regenFactor = (hitPointRegenMod + 100) * hitPointRegen / 100 + hitPointRegenBonus;
            _manaRegenFactor = ResolveHitPointRegenBase();
            Debug.LogError($"[REGEN] hpRegenFactor={_regenFactor} manaRegenFactor={_manaRegenFactor}");
        }

        public void SetRegenCooldown(ushort ticks)
        {
            _regenCooldown = ticks;
        }

        public void ApplyDamageRegenCooldown(float? clientDamageTime = null)
        {
            float cooldownStart = clientDamageTime ?? (_entitySynchInfoHPTime >= 0f ? _entitySynchInfoHPTime : -DamageRegenSuppressSeconds);
            _clientRegenSuppressUntil = cooldownStart + DamageRegenSuppressSeconds;
            _regenCooldown = DamageRegenSuppressTicks;
            Debug.LogError($"[PLAYER-REGEN-COOLDOWN] hpCooldown={_regenCooldown} suppressUntil={_clientRegenSuppressUntil:F3} hp={_currentHPWire}/{MaxHPWire}");
        }

        public void RunRegenTick()
        {
            AdvanceEntitySynchInfoHP(Time.time, "RunRegenTick");
        }

        public bool IsRegenComplete => _currentHPWire >= MaxHPWire
            && (_currentManaWire >= MaxManaWire || _manaRegenFactor == 0);
        public void LogFullState(string context)
        {
            Debug.LogError($"[PLAYERSTATE-{context}] {_className} L{_level} | Base:{_baseHPWire} + Alloc:{_allocatedHPBonusWire} + Equip:{_equipmentHPBonusWire} + Mod:{_modifierHPBonusWire} + Passive:{_passiveHPBonusWire} = {EntitySynchInfoHP}");
        }
    }
}
