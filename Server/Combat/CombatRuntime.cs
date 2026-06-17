using System;
using System.Collections.Generic;
using DungeonRunners.Engine;
using DungeonRunners.Networking;
using DungeonRunners.Networking.EntitySynchInfo;
using DungeonRunners.Core;
using DungeonRunners.Data;
using System.Linq;
using System.IO;
namespace DungeonRunners.Combat
{
    public class CombatRuntime
    {
        private static CombatRuntime _instance;
        public static CombatRuntime Instance => _instance ??= new CombatRuntime();
        private static readonly bool VerboseMonsterDiag = System.Environment.GetEnvironmentVariable("DR_SERVER_VERBOSE_MONSTER_DIAG") == "1";

        private Dictionary<uint, Monster> _activeMonsters = new Dictionary<uint, Monster>();
        private Dictionary<uint, CombatPlayer> _players = new Dictionary<uint, CombatPlayer>();
        private readonly Dictionary<string, RoomRuntime> _roomRuntimes = new Dictionary<string, RoomRuntime>(StringComparer.OrdinalIgnoreCase);
        private string _currentRoomRuntimeKey = RoomRuntime.DefaultInstanceKey;
        private readonly List<uint> _entityOrder = new List<uint>();
        private readonly HashSet<uint> _entityOrderSet = new HashSet<uint>();
        private Dictionary<uint, float> _playerCombatAdvanceTime = new Dictionary<uint, float>();
        private List<RespawnEntry> _respawnQueue = new List<RespawnEntry>();
        private Dictionary<uint, uint> _monsterRuntimeHPWire = new Dictionary<uint, uint>();
        private Dictionary<uint, MonsterHPState> _monsterHPStates = new Dictionary<uint, MonsterHPState>();
        private HashSet<uint> _monsterRuntimeDamageCommitted = new HashSet<uint>();
        private Dictionary<uint, float> _monsterHPRegenLastTime = new Dictionary<uint, float>();
        private Dictionary<uint, float> _monsterHPRegenCarryWire = new Dictionary<uint, float>();
        private Dictionary<uint, ushort> _monsterHPRegenCooldownTicks = new Dictionary<uint, ushort>();
        private Dictionary<uint, float> _monsterManaRegenLastTime = new Dictionary<uint, float>();
        private Dictionary<uint, ushort> _monsterManaRegenCooldownTicks = new Dictionary<uint, ushort>();
        private Dictionary<uint, float> _monsterDeathUpdateAccum = new Dictionary<uint, float>();
        private Dictionary<uint, string> _monsterStateTraceSignatures = new Dictionary<uint, string>();
        private readonly Dictionary<string, EncounterRuntime> _encounterRuntimes = new Dictionary<string, EncounterRuntime>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, ActiveMonsterModifier> _activeMonsterModifiers = new Dictionary<string, ActiveMonsterModifier>(StringComparer.Ordinal);
        private Dictionary<string, ActivePlayerDamageModifier> _activePlayerDamageModifiers = new Dictionary<string, ActivePlayerDamageModifier>(StringComparer.Ordinal);
        private readonly Dictionary<string, uint> _playerModifierNetworkIds = new Dictionary<string, uint>(StringComparer.Ordinal);
        private uint _nextPlayerModifierNetworkId = 0x40000000u;
        private uint _nextPlayerModifierStackSerial = 1u;
        private Queue<PendingModifierKill> _pendingModifierKills = new Queue<PendingModifierKill>();
        private Dictionary<uint, float> _monsterFarTargetActionLogTime = new Dictionary<uint, float>();
        private readonly List<PendingMonsterProjectileImpact> _pendingMonsterProjectiles = new List<PendingMonsterProjectileImpact>();
        private long _nextMonsterProjectileSequence;
        private const int CLIENT_PROJECTILE_SPEED_FIXED_DENOMINATOR = 0x1e00;
        private bool _advancingMonsterModifiers;
        private float _lastCombatTraceSummaryTime;
        private uint _combatTick;
        private float _combatTime = -1f;
        private bool _hasCompletedEntityUpdate;
        private uint _lastCompletedEntityUpdateTick;
        private float _lastCompletedEntityUpdateTime = -1f;
        private bool _hasCompletedSubEntityUpdate;
        private uint _lastCompletedSubEntityUpdateTick;
        private float _lastCompletedSubEntityUpdateTime = -1f;
        private const float CLIENT_UNIT_TICK_INTERVAL = 1f / 30f;
        private const float KNOCKDOWN_STATE_MESSAGE_TICKS = 11f;
        private const float KNOCKDOWN_VISUAL_MOVE_TICKS = 10f;
        private const float KNOCKDOWN_STRENGTH_DISTANCE_SCALE = 1f / 30f;
        private const int MONSTER_HP_HISTORY_LIMIT = 32;
        private const ushort CLIENT_DAMAGE_REGEN_COOLDOWN_TICKS = 300;
        private const int CLIENT_UNIT_REGEN_DIVISOR = 3000;
        private const int CLIENT_PERCENT_SCALE = 100;
        private const ushort CLIENT_STOCKUNIT_FADE_TICKS = 35;
        private const float CLIENT_CONTACT_RANGE_EPSILON = 1f / 16f;
        private const float CLIENT_DEFAULT_UNIT_PERCEPTION = 100f;
        private const float CLIENT_DEFAULT_UNIT_SCAN_FREQUENCY = 1f;
        private const float CLIENT_DEFAULT_UNIT_FLEE_RANGE = 0f;
        private const int CLIENT_DEFAULT_UNIT_COLLISION_BAND = 1;
        private const int CLIENT_DEFAULT_UNIT_COLLISION_PRIORITY = 0;
        private const bool CLIENT_DEFAULT_UNIT_AUTO_SCAN = false;
        private const bool CLIENT_DEFAULT_UNIT_AVOID_UNITS = true;
        private const bool CLIENT_DEFAULT_UNIT_TURN_BEFORE_MOVING = true;
        private const bool CLIENT_DEFAULT_UNIT_PLAYER_CONTROLLED = true;
        private const float CLIENT_DEFAULT_MONSTER_AGGRO_RANGE = 40f;
        private const float CLIENT_DEFAULT_MONSTER_SHOUT_RANGE = 50f;
        private const float CLIENT_DEFAULT_MONSTER_WANDER_RANGE = 100f;
        private const float CLIENT_DEFAULT_MONSTER_LEASH_RANGE = 0f;
        private const float CLIENT_DEFAULT_MONSTER_TELEPORT_FREQUENCY = 150f;
        private const float CLIENT_DEFAULT_MONSTER_TELEPORT_LIMBO_TIME = 60f;
        private const float CLIENT_DEFAULT_MONSTER_RETREAT_RANGE_SQUARED = 640000f;
        private const float CLIENT_DEFAULT_MONSTER_BASE_TIME = 30f;
        private const float CLIENT_DEFAULT_MONSTER_VARIABLE_TIME = 0f;
        private const bool CLIENT_DEFAULT_MONSTER_RETREATABLE = true;
        private const bool CLIENT_DEFAULT_MONSTER_LEASHED = false;
        private const bool CLIENT_DEFAULT_MONSTER_USE_IDLE_TIME = false;
        private const byte CLIENT_PLAYER_STUN_ACTION_KNOCKBACK_ID = 0x0A;
        private const byte CLIENT_RUNTIME_MODIFIER_SOURCE_IS_SELF = 1;

        private class MonsterHPState
        {
            public bool RuntimeInitialized;
            public uint RuntimeHPWire;
            public float LastClientHPReportTime;
            public uint LastClientHPReportWire;
            public uint LastPacketHPWire;
            public readonly List<MonsterHpHistoryEntry> History = new List<MonsterHpHistoryEntry>();
        }

        private class MonsterHpHistoryEntry
        {
            public uint BeforeHPWire;
            public uint AfterHPWire;
            public uint Tick;
            public float Time;
            public string Source;
            public bool CommittedDamage;
            public bool SubEntityMutation;
            public string MutationPhase;
        }

        public struct EntitySynchInfoVisibilityCutoff
        {
            public uint Tick;
            public float Time;
            public bool IncludeSubEntityEffects;
            public string Reason;
            public string Phase;
            public EntitySynchInfoContext Context;
            public string SourceContext;
            public bool HasEntityCutoff;
            public uint LastEntityTick;
            public float LastEntityTime;
            public bool HasSubEntityCutoff;
            public uint LastSubEntityTick;
            public float LastSubEntityTime;
        }

        private class ActiveMonsterModifier
        {
            public uint TargetEntityId;
            public uint SourceEntityId;
            public PlayerState SourceState;
            public SpellData Spell;
            public int SkillLevel;
            public int LastTick;
            public ushort DurationTicksInitial;
            public int DurationTicksRemaining;
            public ushort FrequencyTicks;
            public int FrequencyCountdownTicks;
            public int TicksApplied;
            public string ModifierKey;
        }

        private class ActivePlayerDamageModifier
        {
            public uint TargetEntityId;
            public uint SourceEntityId;
            public string SkillPath;
            public string EffectPath;
            public string ModifierPath;
            public string ModifierEffectPath;
            public string AttackType;
            public string DamageType;
            public int DamageTypeId;
            public byte DamageKind;
            public float DamageMod;
            public float DamageVolatility;
            public int SourceLevel;
            public int SourceIntellect;
            public int SourceAgility;
            public uint PowerLevel;
            public ushort DurationTicks;
            public ushort FrequencyTicks;
            public float ApplyTime;
            public float NextTickTime;
            public float Frequency;
            public float ExpireTime;
            public int MaxTicks;
            public int TicksApplied;
            public bool RemoveOnDeath;
            public string StackRule;
            public string ModifierKey;
        }

        public class MonsterModifierApplyResult
        {
            public bool AppliedModifier;
            public bool DamageApplied;
            public bool Died;
            public uint OldHPWire;
            public uint NewHPWire;
            public int TicksApplied;
            public string Reason;
        }

        private class MonsterHitPointRegenSkillEffect
        {
            public string SkillPath;
            public string EffectPath;
            public string ModifierPath;
            public int HitPointRegenBonus;
            public ushort DurationTicks;
            public float DurationSeconds;
            public bool RemoveOnDeath;
            public string StackRule;
        }

        private class MonsterWeaponDamageSkillEffect
        {
            public string SkillPath;
            public string EffectPath;
            public string WeaponEffectPath;
            public int AttackRatingMod;
            public int DamageMod;
            public bool HasKnockBack;
            public string KnockBackEffectPath;
            public int KnockBackStrength;
            public int KnockBackChanceWire;
            public bool HasKnockDown;
            public string KnockDownEffectPath;
            public int KnockDownStrength;
            public int KnockDownChanceWire;
            public MonsterDamageModifierSkillEffect DamageModifier;
        }

        private class MonsterStunActionSkillEffect
        {
            public string SkillPath;
            public string EffectPath;
            public string ActionEffectPath;
            public string ActionFamily;
            public int Strength;
            public int ChanceWire;
            public bool IsKnockDown;
        }

        public class PlayerStunActionResolved
        {
            public string SkillPath;
            public string EffectPath;
            public string EffectFamily;
            public string ActionClassName;
            public byte ActionClassId;
            public ushort HeadingWire;
            public ushort StrengthWire;
            public int AuthoredStrength;
            public int ChanceWire;
            public uint ChanceRaw;
            public uint ChanceRoll;
            public int StunResistWire;
            public uint StunRaw;
            public uint StunRoll;
            public string Source;
            public bool KnockDownPlayerBranch;
        }

        public class PlayerModifierLifecycle
        {
            public string Visual;
            public string InitSound;
            public string InitEffect;
            public string RemoveEffect;
            public string OverlayIcon;
            public int OverlayDuration;
            public bool HasClientLocalLifecycle => !string.IsNullOrWhiteSpace(Visual)
                || !string.IsNullOrWhiteSpace(InitSound)
                || !string.IsNullOrWhiteSpace(InitEffect)
                || !string.IsNullOrWhiteSpace(RemoveEffect)
                || !string.IsNullOrWhiteSpace(OverlayIcon)
                || OverlayDuration > 0;
        }

        public class PlayerModifierNetworkEvent
        {
            public bool Add;
            public string ModifierKey;
            public string GCType;
            public uint ModifierId;
            public byte Level;
            public uint PowerLevel;
            public uint DurationTicks;
            public byte SourceIsSelf;
            public bool Replace;
            public string SkillPath;
            public string EffectPath;
            public string Source;
            public string SourceFunction;
            public PlayerModifierLifecycle Lifecycle;
        }

        private class MonsterDamageModifierSkillEffect
        {
            public string SkillPath;
            public string EffectPath;
            public string ModifierPath;
            public string ModifierEffectPath;
            public string AttackType;
            public string DamageType;
            public int DamageTypeId;
            public byte DamageKind;
            public float DamageMod;
            public float DamageVolatility;
            public float DurationSeconds;
            public ushort DurationTicks;
            public float FrequencySeconds;
            public ushort FrequencyTicks;
            public bool RemoveOnDeath;
            public string StackRule;
        }

        private class MonsterSkillEffectSupport
        {
            public string SkillPath;
            public string EffectPath;
            public readonly List<string> Families = new List<string>();
            public readonly List<string> UnsupportedFamilies = new List<string>();
            public string Status = "UNKNOWN";
            public string ModifierPath;
            public string Attribute;
            public string Reason;
        }

        public class PendingModifierKill
        {
            public uint SourceEntityId;
            public uint TargetEntityId;
            public string Source;
            public float DamageTime;
        }

        public bool HasPendingModifierKills => _pendingModifierKills.Count > 0;
        public uint CombatTick => _combatTick;
        public float CombatTime => GetCombatTime();
        public uint LastCompletedEntityUpdateTick => _hasCompletedEntityUpdate ? _lastCompletedEntityUpdateTick : 0u;
        public float LastCompletedEntityUpdateTime => _hasCompletedEntityUpdate ? _lastCompletedEntityUpdateTime : Mathf.Max(0f, GetCombatTime() - CLIENT_UNIT_TICK_INTERVAL);
        public uint LastCompletedSubEntityUpdateTick => _hasCompletedSubEntityUpdate ? _lastCompletedSubEntityUpdateTick : 0u;
        public float LastCompletedSubEntityUpdateTime => _hasCompletedSubEntityUpdate ? _lastCompletedSubEntityUpdateTime : Mathf.Max(0f, GetCombatTime() - CLIENT_UNIT_TICK_INTERVAL);

        public void SetCombatClock(uint tick, float time, string source = null)
        {
            if (time < 0f) time = Time.time;
            bool backwards = _combatTime >= 0f && time + 0.0001f < _combatTime;
            if (backwards)
            {
                Debug.LogError($"[CLIENT-COMBAT-CLOCK] ignored backwards clock tick={tick} time={time:F3} currentTick={_combatTick} currentTime={_combatTime:F3} source={source ?? "unknown"}");
                return;
            }
            _combatTick = tick;
            _combatTime = time;
        }

        public void MarkEntityUpdateCompleted(uint tick, float time, string source = null)
        {
            if (time < 0f) time = GetCombatTime();
            if (_hasCompletedEntityUpdate && time + 0.0001f < _lastCompletedEntityUpdateTime)
            {
                return;
            }

            _hasCompletedEntityUpdate = true;
            _lastCompletedEntityUpdateTick = tick;
            _lastCompletedEntityUpdateTime = time;
        }

        public void MarkSubEntityUpdateCompleted(uint tick, float time, string source = null)
        {
            if (time < 0f) time = GetCombatTime();
            if (_hasCompletedSubEntityUpdate && time + 0.0001f < _lastCompletedSubEntityUpdateTime)
            {
                return;
            }

            _hasCompletedSubEntityUpdate = true;
            _lastCompletedSubEntityUpdateTick = tick;
            _lastCompletedSubEntityUpdateTime = time;
        }

        public void GetValidationCutoff(out uint tick, out float time)
        {
            if (_hasCompletedSubEntityUpdate &&
                (!_hasCompletedEntityUpdate || _lastCompletedSubEntityUpdateTime > _lastCompletedEntityUpdateTime + 0.0001f))
            {
                tick = _lastCompletedSubEntityUpdateTick;
                time = _lastCompletedSubEntityUpdateTime;
                return;
            }

            if (_hasCompletedEntityUpdate)
            {
                tick = _lastCompletedEntityUpdateTick;
                time = _lastCompletedEntityUpdateTime;
                return;
            }

            tick = _combatTick > 0 ? _combatTick - 1u : 0u;
            time = Mathf.Max(0f, GetCombatTime() - CLIENT_UNIT_TICK_INTERVAL);
        }

        public EntitySynchInfoVisibilityCutoff GetEntitySynchInfoValidationCutoff(EntitySynchInfoContext context, string source = null)
        {
            EntitySynchInfoVisibilityCutoff cutoff = new EntitySynchInfoVisibilityCutoff
            {
                Tick = _combatTick > 0 ? _combatTick - 1u : 0u,
                Time = Mathf.Max(0f, GetCombatTime() - CLIENT_UNIT_TICK_INTERVAL),
                IncludeSubEntityEffects = false,
                Reason = "fallback-previous-entity-update",
                Phase = "fallback-previous-entity",
                Context = context,
                SourceContext = source ?? "unknown",
                HasEntityCutoff = _hasCompletedEntityUpdate,
                LastEntityTick = _hasCompletedEntityUpdate ? _lastCompletedEntityUpdateTick : 0u,
                LastEntityTime = _hasCompletedEntityUpdate ? _lastCompletedEntityUpdateTime : -1f,
                HasSubEntityCutoff = _hasCompletedSubEntityUpdate,
                LastSubEntityTick = _hasCompletedSubEntityUpdate ? _lastCompletedSubEntityUpdateTick : 0u,
                LastSubEntityTime = _hasCompletedSubEntityUpdate ? _lastCompletedSubEntityUpdateTime : -1f
            };

            bool subEntityAfterEntity = _hasCompletedSubEntityUpdate
                && (!_hasCompletedEntityUpdate
                    || _lastCompletedSubEntityUpdateTick > _lastCompletedEntityUpdateTick
                    || _lastCompletedSubEntityUpdateTime > _lastCompletedEntityUpdateTime + 0.0001f);
            bool subEntityStrictlyBeforeEntity = _hasCompletedSubEntityUpdate
                && _hasCompletedEntityUpdate
                && (_lastCompletedSubEntityUpdateTick < _lastCompletedEntityUpdateTick
                    || (_lastCompletedSubEntityUpdateTick == _lastCompletedEntityUpdateTick
                        && _lastCompletedSubEntityUpdateTime + 0.0001f < _lastCompletedEntityUpdateTime));
            bool subEntitySameEntityTick = _hasCompletedSubEntityUpdate
                && _hasCompletedEntityUpdate
                && _lastCompletedSubEntityUpdateTick == _lastCompletedEntityUpdateTick
                && Mathf.Abs(_lastCompletedSubEntityUpdateTime - _lastCompletedEntityUpdateTime) <= 0.0001f;

            if (IsRuntimeMonsterHPSuffixContext(context) && subEntityAfterEntity)
            {
                if (_hasCompletedEntityUpdate)
                {
                    cutoff.Tick = _lastCompletedEntityUpdateTick;
                    cutoff.Time = _lastCompletedEntityUpdateTime;
                    cutoff.Phase = "entity-before-subentity";
                }
                else
                {
                    cutoff.Phase = "fallback-before-subentity";
                }
                cutoff.Reason = "completed-subentity-pending-client-update";
                cutoff.IncludeSubEntityEffects = false;
            }
            else if (_hasCompletedEntityUpdate)
            {
                cutoff.Tick = _lastCompletedEntityUpdateTick;
                cutoff.Time = _lastCompletedEntityUpdateTime;
                cutoff.Reason = "completed-entity-update";
                cutoff.Phase = "entity";
                cutoff.IncludeSubEntityEffects = subEntityStrictlyBeforeEntity;
                if (cutoff.IncludeSubEntityEffects)
                    cutoff.Phase = "entity-after-subentity";
            }

            Debug.LogError($"[CLIENT-HP-VISIBILITY] context={context} phase={cutoff.Phase} tick={cutoff.Tick} time={cutoff.Time:F3} includeSubEntity={cutoff.IncludeSubEntityEffects} reason={cutoff.Reason} source={source ?? "unknown"} lastEntity={(_hasCompletedEntityUpdate ? _lastCompletedEntityUpdateTick.ToString() : "none")}@{(_hasCompletedEntityUpdate ? _lastCompletedEntityUpdateTime.ToString("F3") : "none")} lastSubEntity={(_hasCompletedSubEntityUpdate ? _lastCompletedSubEntityUpdateTick.ToString() : "none")}@{(_hasCompletedSubEntityUpdate ? _lastCompletedSubEntityUpdateTime.ToString("F3") : "none")} subEntityAfterEntity={subEntityAfterEntity} subEntityStrictlyBeforeEntity={subEntityStrictlyBeforeEntity} subEntitySameEntityTick={subEntitySameEntityTick}");
            if (cutoff.Reason.StartsWith("fallback", StringComparison.OrdinalIgnoreCase) ||
                cutoff.Phase.StartsWith("fallback", StringComparison.OrdinalIgnoreCase) ||
                cutoff.Reason.Equals("completed-subentity-pending-client-update", StringComparison.OrdinalIgnoreCase))
            {
                RuntimeEvidence.LogFallbackHit(
                    "hp-cutoff",
                    cutoff.Reason,
                    $"context={context} phase={cutoff.Phase} source='{source ?? "unknown"}' tick={cutoff.Tick}",
                    64);
            }
            return cutoff;
        }

        public float GetCombatTime()
        {
            return _combatTime >= 0f ? _combatTime : Time.time;
        }

        public PendingModifierKill DequeuePendingModifierKill()
        {
            return _pendingModifierKills.Count > 0 ? _pendingModifierKills.Dequeue() : null;
        }

        private Dictionary<uint, uint> _componentToEntityMap = new Dictionary<uint, uint>();

        private Dictionary<uint, uint> _clientToServerIdMap = new Dictionary<uint, uint>();

        private uint _nextMonsterId = 50000;
        private float? _avatarCombatRadius;

        public event Action<Monster> OnMonsterSpawned;
        public event Action<Monster> OnMonsterDespawned;
        public event Action<Monster> OnMonsterPositionChanged;
        public event Action<Monster, CombatPlayer, byte> OnMonsterAttackStarted;
        public event Action<Monster, CombatPlayer, bool, uint> OnMonsterAttackResolved;
        public event Action<Monster, CombatPlayer, bool, uint, string> OnPlayerDamageResolved;
        public event Action<Monster, CombatPlayer, PlayerStunActionResolved> OnPlayerStunActionResolved;
        public event Action<Monster, CombatPlayer, PlayerModifierNetworkEvent> OnPlayerModifierNetworkEvent;


        public uint AllocateComponentId()
        {
            return _nextMonsterId++;
        }
        public CombatRuntime()
        {
            Debug.LogError("[COMBAT-RUNTIME] state=initialized");
            Debug.LogError("[COMBAT-RUNTIME] roomRngSeed=pending");
        }

        private void RegisterEntityOrder(uint entityId)
        {
            if (entityId == 0 || !_entityOrderSet.Add(entityId))
                return;
            _entityOrder.Add(entityId);
        }

        private void UnregisterEntityOrder(uint entityId)
        {
            if (!_entityOrderSet.Remove(entityId))
                return;
            _entityOrder.Remove(entityId);
        }

        public List<uint> GetEntityOrderSnapshot()
        {
            return new List<uint>(_entityOrder);
        }

        public bool IsMonsterEntity(uint entityId)
        {
            return _activeMonsters.ContainsKey(entityId);
        }

        public bool IsPlayerEntity(uint entityId)
        {
            return _players.ContainsKey(entityId);
        }

        public bool TryGetCombatPlayerForController(uint entityId, out CombatPlayer player)
        {
            return _players.TryGetValue(entityId, out player);
        }

        public IEnumerable<Monster> GetMonstersInZone(string zoneName)
        {
            string instanceKey = string.IsNullOrWhiteSpace(zoneName)
                ? null
                : RoomRuntime.NormalizeInstanceKey(zoneName);
            return _activeMonsters.Values.Where(m =>
                string.Equals(m.ZoneName, zoneName, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(instanceKey) && MatchesInstance(m, instanceKey)));
        }

        public int ClearZoneMobs(string zoneName)
        {
            var toRemove = _activeMonsters.Values
                .Where(m => m.ZoneName != null && (
                    string.Equals(m.ZoneName, zoneName, StringComparison.OrdinalIgnoreCase) ||
                    m.ZoneName.StartsWith(zoneName + "_inst", StringComparison.OrdinalIgnoreCase)))
                .Select(m => m.EntityId)
                .ToList();

            foreach (uint eid in toRemove)
            {
                if (_activeMonsters.TryGetValue(eid, out var monster))
                {
                    _componentToEntityMap.Remove(monster.BehaviorId);
                    _componentToEntityMap.Remove(monster.SkillsId);
                    _componentToEntityMap.Remove(monster.ManipulatorsId);
                    _componentToEntityMap.Remove(monster.ModifiersId);
                    _componentToEntityMap.Remove(monster.UnitId);
                    _componentToEntityMap.Remove(eid);
                    _monsterRuntimeHPWire.Remove(eid);
                    _monsterHPStates.Remove(eid);
                    _monsterRuntimeDamageCommitted.Remove(eid);
                    _monsterHPRegenLastTime.Remove(eid);
                    _monsterHPRegenCarryWire.Remove(eid);
                    _monsterHPRegenCooldownTicks.Remove(eid);
                    _monsterManaRegenLastTime.Remove(eid);
                    _monsterManaRegenCooldownTicks.Remove(eid);
                    _monsterStateTraceSignatures.Remove(eid);
                    _monsterFarTargetActionLogTime.Remove(eid);
                    RemoveMonsterModifiersForTarget(eid, "ClearZoneMobs");
                    RemovePlayerModifiersFromSource(eid, "ClearZoneMobs");
                    WanderSimulator.Instance.UnregisterEntity(eid);
                    UnregisterEntityOrder(eid);
                }
                _activeMonsters.Remove(eid);
            }

            Debug.LogError($"[BEHAVIOR] ClearZoneMobs('{zoneName}'): removed {toRemove.Count} monsters");
            return toRemove.Count;
        }

        public int ClearInstanceMobs(string instanceKey)
        {
            string normalizedInstanceKey = RoomRuntime.NormalizeInstanceKey(instanceKey);
            if (string.IsNullOrWhiteSpace(normalizedInstanceKey))
                return 0;

            var toRemove = _activeMonsters.Values
                .Where(m => MatchesInstance(m, normalizedInstanceKey))
                .Select(m => m.EntityId)
                .ToList();

            foreach (uint eid in toRemove)
            {
                if (_activeMonsters.TryGetValue(eid, out var monster))
                {
                    _componentToEntityMap.Remove(monster.BehaviorId);
                    _componentToEntityMap.Remove(monster.SkillsId);
                    _componentToEntityMap.Remove(monster.ManipulatorsId);
                    _componentToEntityMap.Remove(monster.ModifiersId);
                    _componentToEntityMap.Remove(monster.UnitId);
                    _componentToEntityMap.Remove(eid);
                    _monsterRuntimeHPWire.Remove(eid);
                    _monsterHPStates.Remove(eid);
                    _monsterRuntimeDamageCommitted.Remove(eid);
                    _monsterHPRegenLastTime.Remove(eid);
                    _monsterHPRegenCarryWire.Remove(eid);
                    _monsterHPRegenCooldownTicks.Remove(eid);
                    _monsterManaRegenLastTime.Remove(eid);
                    _monsterManaRegenCooldownTicks.Remove(eid);
                    _monsterStateTraceSignatures.Remove(eid);
                    _monsterFarTargetActionLogTime.Remove(eid);
                    RemoveMonsterModifiersForTarget(eid, "ClearInstanceMobs");
                    RemovePlayerModifiersFromSource(eid, "ClearInstanceMobs");
                    WanderSimulator.Instance.UnregisterEntity(eid);
                    UnregisterEntityOrder(eid);
                }
                _activeMonsters.Remove(eid);
            }

            Debug.LogError($"[ZONE-JOIN] ClearInstanceMobs('{normalizedInstanceKey}'): removed {toRemove.Count} monsters");
            return toRemove.Count;
        }
        public CombatPlayer RegisterPlayer(uint entityId, string name, PlayerState state, float posX, float posY, string instanceKey = null)
        {
            string normalizedInstanceKey = string.IsNullOrWhiteSpace(instanceKey)
                ? null
                : RoomRuntime.NormalizeInstanceKey(instanceKey);
            if (string.IsNullOrWhiteSpace(normalizedInstanceKey))
            {
                RuntimeEvidence.LogFallbackHit("rng-instance", "register-player-missing-instance", $"player={entityId} name='{name ?? ""}'", 64);
                Debug.LogError($"[RNG-INSTANCE] source=RegisterPlayer reason=missing-instance player={entityId} name='{name ?? ""}'");
            }

            var player = new CombatPlayer
            {
                EntityId = entityId,
                Name = name,
                PlayerState = state,
                PosX = posX,
                PosY = posY,
                InstanceKey = normalizedInstanceKey,
                IsAlive = true
            };

            _players[entityId] = player;
            int droppedCrossInstance = 0;
            foreach (var monster in _activeMonsters.Values)
            {
                if (monster == null) continue;
                bool targetsPlayer = monster.TargetId == entityId
                    || monster.CombatContactTargetId == entityId;
                if (!targetsPlayer || MatchesInstance(monster, normalizedInstanceKey)) continue;
                if (ClearMonsterTargetStateForLostPlayer(monster, entityId, "player-changed-instance"))
                    droppedCrossInstance++;
            }
            if (droppedCrossInstance > 0)
                Debug.LogError($"[COMBAT-LIFECYCLE] player {name}#{entityId} entered instance '{normalizedInstanceKey}'; dropped {droppedCrossInstance} cross-instance monster targets sourceFunction=MonsterBehavior2::UpdateTargets@0x0051CB50 out-of-world-target-watcher");
            if (state != null)
            {
                state.OnAttributeModifierRemoved -= HandlePlayerAttributeModifierRemoved;
                state.OnAttributeModifierRemoved += HandlePlayerAttributeModifierRemoved;
            }
            RegisterEntityOrder(entityId);
            _playerCombatAdvanceTime[entityId] = GetCombatTime();
            Debug.LogError($"[COMBAT] registerPlayer name='{name}' id={entityId} pos=({posX:F1},{posY:F1})");
            return player;
        }
        public string DumpPlayerIds()
        {
            var ids = string.Join(", ", _players.Keys);
            return $"[{ids}] (count={_players.Count})";
        }
        public void UpdatePlayerPosition(uint entityId, float posX, float posY)
        {
            if (_players.TryGetValue(entityId, out var player))
            {
                player.PosX = posX;
                player.PosY = posY;
            }
        }

        public bool ApplyMonsterWanderClientVisiblePosition(Monster monster, string source)
        {
            if (monster == null || !monster.IsAlive || monster.AggroTriggered)
                return false;
            if (!TryGetMonsterWanderClientVisiblePosition(monster, out float visualX, out float visualY))
                return false;

            float delta = Distance2D(monster.PosX, monster.PosY, visualX, visualY);
            if (delta <= 0.001f)
                return false;

            float oldX = monster.PosX;
            float oldY = monster.PosY;
            monster.PosX = visualX;
            monster.PosY = visualY;
            monster.PosZ = ResolveTerrainHeight(monster, visualX, visualY, monster.PosZ);
            ResetMonsterClientVisiblePosition(monster, visualX, visualY, GetCombatTime(), source ?? "wander");
            if (VerboseMonsterDiag) Debug.LogError($"[MON-WANDER-POS] {monster.Name}#{monster.EntityId} source={source ?? "unknown"} authoritative=({oldX:F1},{oldY:F1})->clientVisible=({visualX:F1},{visualY:F1}) delta={delta:F1}");
            return true;
        }

        private static float ResolveTerrainHeight(Monster monster, float worldX, float worldY, float fallback)
        {
            if (monster == null) return fallback;
            string key = !string.IsNullOrWhiteSpace(monster.InstanceKey) ? monster.InstanceKey : monster.ZoneName;
            if (string.IsNullOrWhiteSpace(key)) return fallback;
            var pathMap = DungeonRunners.Core.PathMapCatalog.Instance.GetPathMap(key);
            if (pathMap == null && !string.IsNullOrWhiteSpace(monster.ZoneName))
                pathMap = DungeonRunners.Core.PathMapCatalog.Instance.GetPathMap(monster.ZoneName);
            if (pathMap == null || !pathMap.IsWalkable(worldX, worldY)) return fallback;
            return pathMap.GetHeightAt(worldX, worldY, fallback);
        }

        public bool TryGetMonsterWanderClientVisiblePosition(Monster monster, out float visualX, out float visualY)
        {
            visualX = monster != null ? monster.PosX : 0f;
            visualY = monster != null ? monster.PosY : 0f;
            if (monster == null || !monster.IsAlive || monster.AggroTriggered)
                return false;
            return WanderSimulator.Instance.TryGetClientVisiblePosition(monster.EntityId, out visualX, out visualY);
        }

        public void ResetMonsterClientVisiblePosition(Monster monster, float posX, float posY, float clientNow, string source)
        {
            if (monster == null) return;
            if (clientNow < 0f) clientNow = GetCombatTime();
            monster.ClientVisibleMoveInitialized = true;
            monster.ClientVisibleMoveActive = false;
            monster.ClientVisiblePosX = posX;
            monster.ClientVisiblePosY = posY;
            monster.ClientVisibleMoveTargetX = posX;
            monster.ClientVisibleMoveTargetY = posY;
            monster.ClientVisibleMoveLastTime = clientNow;
            monster.ClientVisibleFixedInit = false;
            monster.ClientVisibleHeadingInit = false;
            if (VerboseMonsterDiag) Debug.LogError($"[MON-CLIENT-POS] reset {monster.Name}#{monster.EntityId} source={source ?? "unknown"} visible=({posX:F1},{posY:F1}) server=({monster.PosX:F1},{monster.PosY:F1}) clientNow={clientNow:F3}");
        }

        public void RecordMonsterMoveClientVisible(Monster monster, float targetX, float targetY, float clientNow, string source)
        {
            if (monster == null || !monster.IsAlive) return;
            if (clientNow < 0f) clientNow = GetCombatTime();
            if (!monster.ClientVisibleMoveInitialized)
                ResetMonsterClientVisiblePosition(monster, monster.PosX, monster.PosY, clientNow, $"{source ?? "unknown"}-lazy");

            AdvanceMonsterClientVisiblePosition(monster, clientNow);
            monster.ClientVisibleMoveTargetX = targetX;
            monster.ClientVisibleMoveTargetY = targetY;
            monster.ClientVisibleMoveLastTime = clientNow;
            monster.ClientVisibleMoveActive = Distance2D(monster.ClientVisiblePosX, monster.ClientVisiblePosY, targetX, targetY) > 0.001f;
            if (VerboseMonsterDiag) Debug.LogError($"[MON-CLIENT-POS] move {monster.Name}#{monster.EntityId} source={source ?? "unknown"} visible=({monster.ClientVisiblePosX:F1},{monster.ClientVisiblePosY:F1}) dest=({targetX:F1},{targetY:F1}) server=({monster.PosX:F1},{monster.PosY:F1}) speed={ResolveMonsterMovementSpeed(monster):F1} clientNow={clientNow:F3}");
        }

        public bool TryGetMonsterClientVisiblePosition(Monster monster, float clientNow, out float visualX, out float visualY)
        {
            visualX = monster != null ? monster.PosX : 0f;
            visualY = monster != null ? monster.PosY : 0f;
            if (monster == null || !monster.IsAlive)
                return false;

            if (!monster.AggroTriggered)
                return TryGetMonsterWanderClientVisiblePosition(monster, out visualX, out visualY);

            if (clientNow < 0f) clientNow = GetCombatTime();
            if (monster.ClientVisibleMoveInitialized)
            {
                AdvanceMonsterClientVisiblePosition(monster, clientNow);
                visualX = monster.ClientVisiblePosX;
                visualY = monster.ClientVisiblePosY;
                return true;
            }

            visualX = monster.PosX;
            visualY = monster.PosY;
            return true;
        }

        public bool TryGetMonsterClientUnitPosition(Monster monster, float clientNow, out float visualX, out float visualY)
        {
            visualX = monster != null ? monster.PosX : 0f;
            visualY = monster != null ? monster.PosY : 0f;
            if (monster == null || !monster.IsAlive)
                return false;

            return TryGetMonsterClientVisiblePosition(monster, clientNow, out visualX, out visualY);
        }

        public bool TryGetMonsterClientUnitPosition(Monster monster, float clientNow, out float visualX, out float visualY, out float visualZ)
        {
            visualZ = monster != null ? monster.PosZ : 0f;
            if (!TryGetMonsterClientUnitPosition(monster, clientNow, out visualX, out visualY))
                return false;
            visualZ = ResolveTerrainHeight(monster, visualX, visualY, monster.PosZ);
            return true;
        }

        private void AdvanceMonsterClientVisiblePosition(Monster monster, float clientNow)
        {
            if (monster == null || !monster.ClientVisibleMoveInitialized)
                return;
            if (clientNow < monster.ClientVisibleMoveLastTime)
            {
                monster.ClientVisibleMoveLastTime = clientNow;
                return;
            }

            float elapsed = clientNow - monster.ClientVisibleMoveLastTime;
            if (elapsed <= 0.0001f)
                return;

            if (!monster.ClientVisibleMoveActive)
            {
                monster.ClientVisibleMoveLastTime = clientNow;
                return;
            }

            float speed = ResolveMonsterMovementSpeed(monster);
            if (speed <= 0f)
            {
                monster.ClientVisiblePosX = monster.ClientVisibleMoveTargetX;
                monster.ClientVisiblePosY = monster.ClientVisibleMoveTargetY;
                monster.ClientVisibleFixedInit = false;
                monster.ClientVisibleMoveActive = false;
                monster.ClientVisibleMoveLastTime = clientNow;
                return;
            }

            int ticks = (int)(elapsed / CLIENT_UNIT_TICK_INTERVAL + 0.0001f);
            if (ticks < 1)
                return;

            if (!monster.ClientVisibleFixedInit)
            {
                monster.ClientVisibleFixedX = (int)Math.Round(monster.ClientVisiblePosX * UnitMover.Fixed);
                monster.ClientVisibleFixedY = (int)Math.Round(monster.ClientVisiblePosY * UnitMover.Fixed);
                monster.ClientVisibleFixedInit = true;
                monster.ClientVisibleHeadingInit = false;
            }
            int tgtX = (int)Math.Round(monster.ClientVisibleMoveTargetX * UnitMover.Fixed);
            int tgtY = (int)Math.Round(monster.ClientVisibleMoveTargetY * UnitMover.Fixed);
            int speedFixed = (int)Math.Round(speed * UnitMover.Fixed);
            int stepFixed = (int)(((long)speedFixed << 8) / 0x1e00);
            if (stepFixed < 1) stepFixed = 1;
            int turnRate = UnitMover.TurnRatePerTickFixed(monster.TurnRateDegrees);
            if (!monster.ClientVisibleHeadingInit)
            {
                monster.ClientVisibleHeadingFixed = UnitMover.VectorToHeadingFixed(tgtX - monster.ClientVisibleFixedX, tgtY - monster.ClientVisibleFixedY);
                monster.ClientVisibleHeadingInit = true;
            }

            var pm = PathMapCatalog.Instance.GetPathMap(!string.IsNullOrWhiteSpace(monster.InstanceKey) ? monster.InstanceKey : monster.ZoneName);
            bool arrived = false;
            for (int tickIndex = 0; tickIndex < ticks && !arrived; tickIndex++)
            {
                UnitMover.StepTowardFixedHeading(monster.ClientVisibleFixedX, monster.ClientVisibleFixedY, monster.ClientVisibleHeadingFixed, tgtX, tgtY, stepFixed, turnRate, out int nextX, out int nextY, out int nextHeading, out arrived);
                UnitMover.ResolveMovement(pm, monster.ClientVisibleFixedX, monster.ClientVisibleFixedY, nextX, nextY, out nextX, out nextY);
                monster.ClientVisibleFixedX = nextX;
                monster.ClientVisibleFixedY = nextY;
                monster.ClientVisibleHeadingFixed = nextHeading;
            }
            monster.ClientVisiblePosX = monster.ClientVisibleFixedX / (float)UnitMover.Fixed;
            monster.ClientVisiblePosY = monster.ClientVisibleFixedY / (float)UnitMover.Fixed;
            if (arrived)
                monster.ClientVisibleMoveActive = false;
            monster.ClientVisibleMoveLastTime += ticks * CLIENT_UNIT_TICK_INTERVAL;
            Debug.LogError($"[MON-MOVER-STEP] entity={monster.EntityId} ticks={ticks} fixedX={monster.ClientVisibleFixedX} fixedY={monster.ClientVisibleFixedY} headingFixed={monster.ClientVisibleHeadingFixed} stepFixed={stepFixed} turnRate={turnRate} tgtFixedX={tgtX} tgtFixedY={tgtY} arrived={arrived} clientNow={clientNow:F3}");
        }

        public void EngageMonsterFromClientAction(Monster monster, uint playerEntityId, bool clientWeaponUseStarted = false)
        {
            if (monster == null || !monster.IsAlive) return;
            if (monster.DeathPendingClientConfirmation) return;
            if (!_players.TryGetValue(playerEntityId, out var player) || player == null) return;
            float allowedRange = ResolveMonsterEffectiveAttackRange(monster);
            float monsterX = monster.PosX;
            float monsterY = monster.PosY;
            TryGetMonsterClientVisiblePosition(monster, GetCombatTime(), out monsterX, out monsterY);
            float dist = Distance2D(monsterX, monsterY, player.PosX, player.PosY);
            float contactRange = ResolveClientContactRange(monster, player, allowedRange);
            bool inContact = contactRange > 0f && dist <= contactRange + CLIENT_CONTACT_RANGE_EPSILON;
            float aggroRange = ResolveMonsterAggroAdmissionRange(monster);
            bool inAuthoredAggro = aggroRange > 0f && dist <= aggroRange + CLIENT_CONTACT_RANGE_EPSILON;
            float clientNow = GetCombatTime();
            bool alreadyTargetingPlayer = monster.AggroTriggered && monster.TargetId == player.EntityId;
            bool contactWasActive = monster.CombatContactTargetId == player.EntityId && monster.CombatContactUntil > clientNow;
            if (!alreadyTargetingPlayer && !inAuthoredAggro && !inContact && !clientWeaponUseStarted)
            {
                Debug.LogError($"[AGGRO-OBSERVE] client intent out-of-range no-admit {monster.Name}#{monster.EntityId}->{player.Name} dist={dist:F1} aggro={aggroRange:F1} targetSearch={ResolveMonsterTargetSearchRange(monster, false):F1} clientRange={contactRange:F1} sourceFunction=MonsterBehavior2::onAttacked@0x0051B550");
                return;
            }
            if (!alreadyTargetingPlayer && !inAuthoredAggro && !inContact && clientWeaponUseStarted)
            {
                if (monster.CombatContactTargetId == player.EntityId)
                {
                    monster.CombatContactTargetId = 0;
                    monster.CombatContactUntil = 0f;
                }
                Debug.LogError($"[AGGRO-OBSERVE] client weapon use outside aggro {monster.Name}#{monster.EntityId}->{player.Name} dist={dist:F1} aggro={aggroRange:F1} targetSearch={ResolveMonsterTargetSearchRange(monster, false):F1} clientRange={contactRange:F1} action=client-use-aggro sourceFunction=RangedWeapon::doHit+Projectile::doImpact->MonsterBehavior2::onAttacked");
            }
            ApplyMonsterWanderClientVisiblePosition(monster, "client-action");
            bool attackPathClear = IsMonsterAttackPathClear(monster, player, inContact ? "client-contact" : null);
            if (!attackPathClear)
            {
                inContact = false;
                ClearMonsterCombatContact(monster, player);
            }
            string aggroReason = clientWeaponUseStarted && !inAuthoredAggro && !inContact ? "client-client-use" : "client";
            AggroMonster(monster, player, aggroReason, false);
            if (inContact)
            {
                monster.CombatContactTargetId = player.EntityId;
                monster.CombatContactUntil = clientNow + Mathf.Max(0.25f, ResolveMonsterAttackWindup(monster) + 0.25f);
                if (!contactWasActive)
                    Debug.LogError($"[MON-CONTACT] {monster.Name}#{monster.EntityId}->{player.Name} dist={dist:F1} range={allowedRange:F1} clientRange={contactRange:F1}");
            }
            else if (monster.CombatContactTargetId == player.EntityId)
            {
                monster.CombatContactTargetId = 0;
                monster.CombatContactUntil = 0f;
            }
            TraceMonsterState(monster, "client-action", player, dist, allowedRange, inContact ? "client-contact" : aggroReason);
        }

        private MonsterHPState GetMonsterHPState(Monster monster)
        {
            if (monster == null) return null;
            if (!_monsterHPStates.TryGetValue(monster.EntityId, out var state))
            {
                state = new MonsterHPState();
                _monsterHPStates[monster.EntityId] = state;
            }

            if (!state.RuntimeInitialized)
            {
                uint hp = 0;
                if (!_monsterRuntimeHPWire.TryGetValue(monster.EntityId, out hp))
                    hp = monster.IsAlive ? monster.CurrentHPWire : 0;
                state.RuntimeHPWire = hp > monster.MaxHPWire ? monster.MaxHPWire : hp;
                state.RuntimeInitialized = true;
            }

            if (monster.LastClientHPReportTime > state.LastClientHPReportTime)
            {
                state.LastClientHPReportTime = monster.LastClientHPReportTime;
                state.LastClientHPReportWire = monster.LastClientHPReportWire;
            }
            ApplyMonsterHPState(monster, state);
            return state;
        }

        private void ApplyMonsterHPState(Monster monster, MonsterHPState state)
        {
            if (monster == null || state == null) return;
            if (!state.RuntimeInitialized)
            {
                uint hp = 0;
                if (!_monsterRuntimeHPWire.TryGetValue(monster.EntityId, out hp))
                    hp = monster.IsAlive ? monster.CurrentHPWire : 0;
                state.RuntimeHPWire = hp > monster.MaxHPWire ? monster.MaxHPWire : hp;
                state.RuntimeInitialized = true;
            }
            if (state.RuntimeHPWire > monster.MaxHPWire)
                state.RuntimeHPWire = monster.MaxHPWire;
            _monsterRuntimeHPWire[monster.EntityId] = state.RuntimeHPWire;
            ApplyMonsterHPFields(monster, state);
            EntitySynchInfoAuthority.Instance.RegisterMonster(monster);
            if (_activeMonsters.TryGetValue(monster.EntityId, out var active) && active != monster)
            {
                ApplyMonsterHPFields(active, state);
                EntitySynchInfoAuthority.Instance.RegisterMonster(active);
            }
        }

        private void ApplyMonsterHPFields(Monster monster, MonsterHPState state)
        {
            if (monster == null || state == null) return;
            monster.CurrentHPWire = state.RuntimeHPWire;
            monster.LastClientHPReportTime = state.LastClientHPReportTime;
            monster.LastClientHPReportWire = state.LastClientHPReportWire;
        }

        private uint ResolveTickForTime(float clientTime, uint explicitTick = 0)
        {
            if (explicitTick != 0)
                return explicitTick;
            if (_combatTime >= 0f && clientTime >= 0f && Mathf.Abs(clientTime - _combatTime) <= CLIENT_UNIT_TICK_INTERVAL + 0.0001f)
                return _combatTick;
            if (clientTime >= 0f)
                return (uint)Mathf.Max(0, Mathf.FloorToInt((clientTime / CLIENT_UNIT_TICK_INTERVAL) + 0.0001f));
            return _combatTick;
        }

        private static bool IsSubEntityMonsterHPMutationSource(string source)
        {
            if (string.IsNullOrEmpty(source))
                return false;
            return source.IndexOf("RangedProjectile", StringComparison.OrdinalIgnoreCase) >= 0
                || source.IndexOf("SpellProjectile", StringComparison.OrdinalIgnoreCase) >= 0
                || source.IndexOf("Projectile-HIT", StringComparison.OrdinalIgnoreCase) >= 0
                || source.IndexOf("subentity", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ResolveMonsterHPMutationPhase(string source, bool committedDamage, bool subEntityMutation)
        {
            if (subEntityMutation)
                return "subentity";
            if (!string.IsNullOrEmpty(source) && source.IndexOf("regen", StringComparison.OrdinalIgnoreCase) >= 0)
                return "regen";
            return committedDamage ? "entity-damage" : "runtime";
        }

        private void RecordMonsterHPHistory(Monster monster, MonsterHPState state, uint beforeHPWire, uint afterHPWire, bool committedDamage, float clientTime, string source, uint explicitTick = 0)
        {
            if (monster == null || state == null || beforeHPWire == afterHPWire)
                return;

            float resolvedTime = clientTime >= 0f ? clientTime : GetCombatTime();
            uint clientTick = ResolveTickForTime(resolvedTime, explicitTick);
            bool subEntityMutation = IsSubEntityMonsterHPMutationSource(source);
            var entry = new MonsterHpHistoryEntry
            {
                BeforeHPWire = beforeHPWire,
                AfterHPWire = afterHPWire,
                Tick = clientTick,
                Time = resolvedTime,
                Source = source ?? "unknown",
                CommittedDamage = committedDamage,
                SubEntityMutation = subEntityMutation,
                MutationPhase = ResolveMonsterHPMutationPhase(source, committedDamage, subEntityMutation)
            };

            state.History.Add(entry);
            if (state.History.Count > MONSTER_HP_HISTORY_LIMIT)
                state.History.RemoveRange(0, state.History.Count - MONSTER_HP_HISTORY_LIMIT);
            Debug.LogError($"[MON-HP-HISTORY] entity={monster.EntityId} name={monster.Name} phase={entry.MutationPhase} tick={clientTick} time={resolvedTime:F3} hp={beforeHPWire}->{afterHPWire}/{monster.MaxHPWire} committed={committedDamage} subentity={entry.SubEntityMutation} source={source ?? "unknown"}");
        }

        private static bool IsMonsterHpHistoryVisible(MonsterHpHistoryEntry entry, EntitySynchInfoVisibilityCutoff cutoff)
        {
            if (entry == null)
                return true;
            const float timeEpsilon = 0.0001f;
            if (entry.SubEntityMutation)
            {
                if (entry.Tick > cutoff.Tick)
                    return false;
                if (entry.Tick == cutoff.Tick && !cutoff.IncludeSubEntityEffects)
                    return false;
                if (entry.Tick < cutoff.Tick)
                    return true;
            }
            if (entry.Time + timeEpsilon < cutoff.Time)
                return true;
            if (entry.Time > cutoff.Time + timeEpsilon)
                return false;
            return !entry.SubEntityMutation || cutoff.IncludeSubEntityEffects;
        }

        private bool TryResolveMonsterHPFromHistory(Monster monster, MonsterHPState state, EntitySynchInfoVisibilityCutoff cutoff, out uint hpWire, out string hpView, out string mutationSource, out string excludedSameTickProjectile)
        {
            hpWire = state != null ? state.RuntimeHPWire : 0;
            hpView = "runtime-current";
            mutationSource = "none";
            excludedSameTickProjectile = "none";
            if (monster == null || state == null || state.History.Count == 0)
                return false;

            for (int historyIndex = state.History.Count - 1; historyIndex >= 0; historyIndex--)
            {
                MonsterHpHistoryEntry entry = state.History[historyIndex];
                if (IsMonsterHpHistoryVisible(entry, cutoff))
                    break;

                hpWire = entry.BeforeHPWire;
                hpView = "history-cutoff";
                mutationSource = $"{entry.Source ?? "unknown"} phase={entry.MutationPhase ?? "unknown"} tick={entry.Tick} time={entry.Time:F3}";
                if (entry.SubEntityMutation)
                {
                    string visibility = entry.Tick > cutoff.Tick ? "future-subentity" : "same-tick-subentity";
                    excludedSameTickProjectile = $"{visibility} source={entry.Source ?? "unknown"} phase={entry.MutationPhase ?? "subentity"} tick={entry.Tick} time={entry.Time:F3} cutoffPhase={cutoff.Phase ?? "unknown"} cutoff={cutoff.Tick}@{cutoff.Time:F3} includeSubEntity={cutoff.IncludeSubEntityEffects} hp={entry.BeforeHPWire}->{entry.AfterHPWire}";
                }
            }

            if (hpWire > monster.MaxHPWire)
                hpWire = monster.MaxHPWire;
            return hpView == "history-cutoff";
        }

        private uint PeekRuntimeMonsterHPWire(Monster monster)
        {
            var state = GetMonsterHPState(monster);
            return state != null ? state.RuntimeHPWire : 0;
        }

        private int ResolveMonsterHealthRegenFactor(Monster monster)
        {
            if (monster == null || !monster.HasAuthoredHealthRegen)
                return 0;
            float authoredGlobal = GCDatabase.Instance.GetKnob("MonsterHealthRegen", 2f);
            float authoredUnit = Mathf.Max(0f, monster.HealthRegen);
            return ComputeUnitDescRegenFactor(authoredUnit, authoredGlobal);
        }

        private int ResolveMonsterHealthRegenModPct(Monster monster)
        {
            return 0;
        }

        private int ResolveMonsterAdditiveHealthRegen(Monster monster)
        {
            return 1;
        }

        internal static int ComputeUnitRegenDeltaWire(uint maxHPWire, int baseRegen, int regenModPct, int additiveRegen, bool cooldownActive)
        {
            if (maxHPWire == 0) return 0;
            long regen;
            long bonus = additiveRegen;
            if (cooldownActive)
            {
                if (bonus == 0) return 0;
                regen = bonus;
            }
            else
            {
                regen = (((long)regenModPct + CLIENT_PERCENT_SCALE) * baseRegen) / CLIENT_PERCENT_SCALE + bonus;
            }

            long delta = (regen * maxHPWire) / CLIENT_UNIT_REGEN_DIVISOR;
            if (!cooldownActive)
                delta += 1;
            if (delta > int.MaxValue) return int.MaxValue;
            if (delta < int.MinValue) return int.MinValue;
            return (int)delta;
        }

        internal static uint ApplyUnitHPShiftWire(uint hpWire, uint maxHPWire, int deltaWire)
        {
            long shifted = (long)hpWire + deltaWire;
            if (shifted <= 0) return 0;
            if (shifted >= maxHPWire) return maxHPWire;
            return (uint)shifted;
        }

        private int ComputeMonsterHealthRegenDeltaWire(Monster monster, bool cooldownActive)
        {
            if (monster == null) return 0;
            return ComputeUnitRegenDeltaWire(
                monster.MaxHPWire,
                ResolveMonsterHealthRegenFactor(monster),
                ResolveMonsterHealthRegenModPct(monster),
                ResolveMonsterAdditiveHealthRegen(monster),
                cooldownActive);
        }

        private void ResetMonsterHPRegenClock(Monster monster, float now)
        {
            if (monster == null) return;
            if (now < 0f) now = GetCombatTime();
            _monsterHPRegenLastTime[monster.EntityId] = now;
            _monsterHPRegenCarryWire[monster.EntityId] = 0f;
        }

        private int ResolveMonsterManaRegenFactor(Monster monster)
        {
            if (monster == null || !monster.HasAuthoredManaRegen)
                return 0;
            float authoredGlobal = GCDatabase.Instance.GetKnob("MonsterPowerRegen", 2f);
            float authoredUnit = Mathf.Max(0f, monster.ManaRegen);
            return ComputeUnitDescRegenFactor(authoredUnit, authoredGlobal);
        }

        private static int ComputeUnitDescRegenFactor(float authoredUnit, float authoredGlobal)
        {
            if (authoredUnit <= 0f || authoredGlobal <= 0f)
                return 0;
            long unitF32 = DamageResolver.Fixed32FromAuthoredDecimal(authoredUnit);
            long globalF32 = DamageResolver.Fixed32FromAuthoredDecimal(authoredGlobal);
            long value = (unitF32 * globalF32) >> 16;
            if (value <= 0) return 0;
            return value > ushort.MaxValue ? ushort.MaxValue : (int)value;
        }

        private uint ComputeMonsterManaRegenDeltaWire(Monster monster)
        {
            if (monster == null || monster.MaxManaWire == 0) return 0;
            int regenFactor = ResolveMonsterManaRegenFactor(monster);
            if (regenFactor <= 0) return 0;
            long delta = ((long)regenFactor * monster.MaxManaWire) / 3000L + 1L;
            if (delta <= 0) return 0;
            return delta > uint.MaxValue ? uint.MaxValue : (uint)delta;
        }

        private void ResetMonsterManaRegenClock(Monster monster, float now)
        {
            if (monster == null) return;
            if (now < 0f) now = GetCombatTime();
            _monsterManaRegenLastTime[monster.EntityId] = now;
        }

        private uint ApplyMonsterHealthRegen(Monster monster, string source, float now = -1f)
        {
            if (monster == null) return 0;
            uint hp = PeekRuntimeMonsterHPWire(monster);
            if (now < 0f) now = GetCombatTime();
            if (!monster.IsAlive || hp == 0 || monster.MaxHPWire == 0)
            {
                ResetMonsterHPRegenClock(monster, now);
                _monsterHPRegenCooldownTicks.Remove(monster.EntityId);
                return hp;
            }
            if (hp >= monster.MaxHPWire && ResolveMonsterAdditiveHealthRegen(monster) >= 0)
            {
                ResetMonsterHPRegenClock(monster, now);
                _monsterHPRegenCooldownTicks.Remove(monster.EntityId);
                return hp;
            }

            if (!_monsterHPRegenLastTime.TryGetValue(monster.EntityId, out float lastTime) || lastTime <= 0f)
            {
                _monsterHPRegenLastTime[monster.EntityId] = now;
                return hp;
            }

            float elapsed = now - lastTime;
            if (elapsed <= 0f) return hp;

            int ticks = Mathf.FloorToInt(elapsed / CLIENT_UNIT_TICK_INTERVAL);
            if (ticks <= 0) return hp;

            _monsterHPRegenLastTime[monster.EntityId] = lastTime + ticks * CLIENT_UNIT_TICK_INTERVAL;

            uint oldHP = hp;
            _monsterHPRegenCooldownTicks.TryGetValue(monster.EntityId, out ushort cooldown);
            int regenFactor = ResolveMonsterHealthRegenFactor(monster);
            int regenMod = ResolveMonsterHealthRegenModPct(monster);
            int additiveRegen = ResolveMonsterAdditiveHealthRegen(monster);
            for (int tickIndex = 0; tickIndex < ticks && (hp < monster.MaxHPWire || additiveRegen < 0); tickIndex++)
            {
                if (cooldown > 0)
                    cooldown--;
                bool cooldownActive = cooldown > 0;
                int regenWire = ComputeMonsterHealthRegenDeltaWire(monster, cooldownActive);
                if (regenWire != 0)
                {
                    hp = ApplyUnitHPShiftWire(hp, monster.MaxHPWire, regenWire);
                    if (hp == 0) break;
                }
            }

            if (cooldown > 0)
                _monsterHPRegenCooldownTicks[monster.EntityId] = cooldown;
            else
                _monsterHPRegenCooldownTicks.Remove(monster.EntityId);

            if (oldHP == hp) return hp;

            if (hp >= monster.MaxHPWire)
            {
                hp = monster.MaxHPWire;
                _monsterRuntimeDamageCommitted.Remove(monster.EntityId);
                _monsterHPRegenCarryWire[monster.EntityId] = 0f;
            }

            var state = GetMonsterHPState(monster);
            RecordMonsterHPHistory(monster, state, oldHP, hp, false, now, source ?? "regen");
            state.RuntimeHPWire = hp;
            state.RuntimeInitialized = true;
            ApplyMonsterHPState(monster, state);
            Debug.LogError($"[MON-REGEN] {monster.Name}#{monster.EntityId} source={source ?? "unknown"} hp={oldHP / 256f:F2}->{hp / 256f:F2}/{monster.MaxHPWire / 256f:F2} ticks={ticks} cooldown={cooldown} base={regenFactor} mod={regenMod} additive={additiveRegen}");
            return hp;
        }

        private void ApplyMonsterManaRegen(Monster monster, string source, float now = -1f)
        {
            if (monster == null) return;
            if (now < 0f) now = GetCombatTime();
            if (!monster.IsAlive || monster.MaxManaWire == 0)
            {
                ResetMonsterManaRegenClock(monster, now);
                _monsterManaRegenCooldownTicks.Remove(monster.EntityId);
                return;
            }
            if (monster.CurrentManaWire > monster.MaxManaWire)
                monster.CurrentManaWire = monster.MaxManaWire;
            if (monster.CurrentManaWire >= monster.MaxManaWire)
            {
                ResetMonsterManaRegenClock(monster, now);
                _monsterManaRegenCooldownTicks.Remove(monster.EntityId);
                return;
            }
            if (ResolveMonsterManaRegenFactor(monster) <= 0)
            {
                ResetMonsterManaRegenClock(monster, now);
                return;
            }
            if (!_monsterManaRegenLastTime.TryGetValue(monster.EntityId, out float lastTime) || lastTime <= 0f)
            {
                _monsterManaRegenLastTime[monster.EntityId] = now;
                return;
            }

            float elapsed = now - lastTime;
            if (elapsed <= 0f) return;
            int ticks = Mathf.FloorToInt(elapsed / CLIENT_UNIT_TICK_INTERVAL);
            if (ticks <= 0) return;
            _monsterManaRegenLastTime[monster.EntityId] = lastTime + ticks * CLIENT_UNIT_TICK_INTERVAL;

            uint oldMana = monster.CurrentManaWire;
            uint mana = oldMana;
            _monsterManaRegenCooldownTicks.TryGetValue(monster.EntityId, out ushort cooldown);
            for (int tickIndex = 0; tickIndex < ticks && mana < monster.MaxManaWire; tickIndex++)
            {
                if (cooldown > 0)
                    cooldown--;
                if (cooldown > 0)
                    continue;

                uint regenWire = ComputeMonsterManaRegenDeltaWire(monster);
                if (regenWire == 0) continue;
                mana = regenWire >= monster.MaxManaWire - mana ? monster.MaxManaWire : mana + regenWire;
            }

            if (cooldown > 0)
                _monsterManaRegenCooldownTicks[monster.EntityId] = cooldown;
            else
                _monsterManaRegenCooldownTicks.Remove(monster.EntityId);

            if (oldMana == mana) return;
            monster.CurrentManaWire = mana;
            Debug.LogError($"[MON-MANA-REGEN] {monster.Name}#{monster.EntityId} source={source ?? "unknown"} mana={oldMana / 256f:F2}->{mana / 256f:F2}/{monster.MaxManaWire / 256f:F2} ticks={ticks} cooldown={cooldown} factor={ResolveMonsterManaRegenFactor(monster)}");
        }

        private uint ApplyMonsterVitalsRegen(Monster monster, string source, float now = -1f)
        {
            uint hp = ApplyMonsterHealthRegen(monster, source, now);
            ApplyMonsterManaRegen(monster, source, now);
            return hp;
        }

        public uint AdvanceMonsterVitalsToTime(Monster monster, float now, string source = null)
        {
            if (now < 0f) now = GetCombatTime();
            return monster != null ? ApplyMonsterVitalsRegen(monster, source ?? "AdvanceMonsterVitalsToTime", now) : 0u;
        }

        public uint AdvanceMonsterRuntimeBeforeSynch(Monster monster, float now, string source = null)
        {
            if (now < 0f) now = GetCombatTime();
            if (!_advancingMonsterModifiers)
                AdvanceMonsterModifierRuntime(GetRoomRngForMonster(monster), now, source ?? "AdvanceMonsterRuntimeBeforeSynch", monster?.EntityId ?? 0u);
            uint hp = AdvanceMonsterVitalsToTime(monster, now, source ?? "AdvanceMonsterRuntimeBeforeSynch");
            return monster != null ? PeekRuntimeMonsterHPWire(monster) : hp;
        }

        private uint GetRuntimeMonsterHPWire(Monster monster, string source = null, float now = -1f)
        {
            return PeekRuntimeMonsterHPWire(monster);
        }

        private static string BuildMonsterModifierKey(uint targetEntityId, uint sourceEntityId, SpellData spell)
        {
            string modifierId = spell?.ProjectileModifierId;
            if (string.IsNullOrEmpty(modifierId))
                modifierId = spell?.ProjectileModifierEffectId ?? "modifier";
            return $"{targetEntityId}:{sourceEntityId}:{modifierId}";
        }

        private void RemoveMonsterModifiersForTarget(uint targetEntityId, string source)
        {
            if (targetEntityId == 0 || _activeMonsterModifiers.Count == 0) return;
            var keys = _activeMonsterModifiers
                .Where(modifierEntry => modifierEntry.Value != null && modifierEntry.Value.TargetEntityId == targetEntityId)
                .Select(modifierEntry => modifierEntry.Key)
                .ToList();
            foreach (string key in keys)
                _activeMonsterModifiers.Remove(key);
            if (keys.Count > 0)
                Debug.LogError($"[POISON-SHOT-MOD] remove target={targetEntityId} count={keys.Count} source={source ?? "unknown"}");
        }

        private string BuildPlayerDamageModifierKey(uint targetEntityId, uint sourceEntityId, MonsterDamageModifierSkillEffect effect)
        {
            return BuildPlayerRuntimeModifierKey(targetEntityId, sourceEntityId, effect?.ModifierPath ?? effect?.ModifierEffectPath ?? "modifier", effect?.StackRule ?? string.Empty, CLIENT_RUNTIME_MODIFIER_SOURCE_IS_SELF);
        }

        private string BuildPlayerRuntimeModifierKey(uint targetEntityId, uint sourceEntityId, string modifierId, string stackRule, byte sourceIsSelf)
        {
            string key = BuildPlayerModifierStackKey(targetEntityId, sourceEntityId, modifierId, stackRule, sourceIsSelf);
            if (IsAdditiveModifierStack(stackRule))
                return $"{key}:stack:{_nextPlayerModifierStackSerial++}";
            return key;
        }

        private static bool IsAdditiveModifierStack(string stackRule)
        {
            return string.IsNullOrWhiteSpace(stackRule) || string.Equals(stackRule, "NONE", StringComparison.OrdinalIgnoreCase) || string.Equals(stackRule, "0", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildPlayerModifierStackKey(uint targetEntityId, uint sourceEntityId, string modifierId, string stackRule, byte sourceIsSelf)
        {
            if (string.IsNullOrWhiteSpace(modifierId))
                modifierId = "modifier";
            if (string.Equals(stackRule, "UNIQUEBYSOURCE", StringComparison.OrdinalIgnoreCase) && sourceIsSelf == 0 && sourceEntityId != 0)
                return $"{targetEntityId}:{sourceEntityId}:{modifierId}";
            return $"{targetEntityId}:{modifierId}";
        }

        private static uint ResolveActivePlayerModifierRemainingTicks(ActivePlayerDamageModifier mod, float now)
        {
            if (mod == null || mod.DurationTicks == 0)
                return 0;
            float endTime = float.IsPositiveInfinity(mod.ExpireTime) ? mod.ApplyTime + (mod.DurationTicks / 30f) : mod.ExpireTime;
            float remaining = endTime - now;
            if (remaining <= 0f)
                return 0;
            int remainingTicks = Mathf.Clamp(Mathf.CeilToInt(remaining * 30f - 0.0001f), 0, ushort.MaxValue);
            return (uint)remainingTicks;
        }

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

        private uint AllocatePlayerModifierNetworkId(string key, bool track = true)
        {
            if (string.IsNullOrWhiteSpace(key)) key = "modifier";
            uint id = _nextPlayerModifierNetworkId++;
            if (track)
                _playerModifierNetworkIds[key] = id;
            return id;
        }

        private uint ResolveMonsterSkillPowerLevelWire(Monster monster, string skillPath)
        {
            int level = 1;
            GCNode skill = null;
            if (!string.IsNullOrWhiteSpace(skillPath))
                skill = GCDatabase.Instance?.ResolveWithInheritance(skillPath);
            GCNode desc = skill?.GetChild("Description") ?? skill;
            int basePower = desc != null ? desc.GetInt("PowerLevel", desc.GetInt("PowerLevelMin", 1)) : 1;
            int powerInc = desc != null ? desc.GetInt("PowerLevelInc", 0) : 0;
            long power = ((long)basePower << 8) + ((long)Math.Max(0, level - 1) * powerInc << 8);
            if (power <= 0 && monster != null) power = Math.Max(1, (int)monster.Level) << 8;
            if (power <= 0) power = 0x100;
            if (power > uint.MaxValue) return uint.MaxValue;
            return (uint)power;
        }

        private PlayerModifierLifecycle ResolvePlayerModifierLifecycle(string modifierPath)
        {
            var lifecycle = new PlayerModifierLifecycle();
            if (string.IsNullOrWhiteSpace(modifierPath))
                return lifecycle;

            GCNode modifier = GCDatabase.Instance?.ResolveWithInheritance(modifierPath);
            GCNode desc = modifier?.GetChild("Description") ?? modifier;
            if (desc == null)
                return lifecycle;

            lifecycle.Visual = desc.GetString("Visual", null);
            lifecycle.InitSound = desc.GetString("InitSound", null);
            lifecycle.InitEffect = desc.GetString("InitEffect", null);
            lifecycle.RemoveEffect = desc.GetString("RemoveEffect", null);
            lifecycle.OverlayIcon = desc.GetString("OverlayIcon", null);
            lifecycle.OverlayDuration = desc.GetInt("OverlayDuration", 0);
            return lifecycle;
        }

        private void RaisePlayerModifierAdd(Monster sourceMonster, CombatPlayer target, string modifierKey, string gcType, byte level, uint powerLevel, uint durationTicks, byte sourceIsSelf, bool replace, string skillPath, string effectPath, string source, string sourceFunction, bool trackModifierId = true)
        {
            if (target == null || string.IsNullOrWhiteSpace(gcType))
                return;
            uint id = AllocatePlayerModifierNetworkId(modifierKey, trackModifierId);
            OnPlayerModifierNetworkEvent?.Invoke(sourceMonster, target, new PlayerModifierNetworkEvent
            {
                Add = true,
                ModifierKey = modifierKey,
                GCType = gcType,
                ModifierId = id,
                Level = level,
                PowerLevel = powerLevel,
                DurationTicks = durationTicks,
                SourceIsSelf = sourceIsSelf,
                Replace = replace,
                SkillPath = skillPath,
                EffectPath = effectPath,
                Source = source,
                SourceFunction = sourceFunction,
                Lifecycle = ResolvePlayerModifierLifecycle(gcType)
            });
        }

        private void RaisePlayerModifierRemove(Monster sourceMonster, CombatPlayer target, string modifierKey, string gcType, string skillPath, string effectPath, string source, string sourceFunction)
        {
            if (target == null || string.IsNullOrWhiteSpace(modifierKey))
                return;
            if (!_playerModifierNetworkIds.TryGetValue(modifierKey, out uint id))
                return;
            _playerModifierNetworkIds.Remove(modifierKey);
            OnPlayerModifierNetworkEvent?.Invoke(sourceMonster, target, new PlayerModifierNetworkEvent
            {
                Add = false,
                ModifierKey = modifierKey,
                GCType = gcType,
                ModifierId = id,
                SkillPath = skillPath,
                EffectPath = effectPath,
                Source = source,
                SourceFunction = sourceFunction,
                Lifecycle = ResolvePlayerModifierLifecycle(gcType)
            });
        }

        private void HandlePlayerAttributeModifierRemoved(PlayerState state, PlayerState.AttributeModifierRemoval removal)
        {
            if (state == null || removal == null)
                return;
            CombatPlayer target = _players.Values.FirstOrDefault(player => player != null && ReferenceEquals(player.PlayerState, state));
            if (target == null)
                return;
            _activeMonsters.TryGetValue(removal.SourceEntityId, out Monster sourceMonster);
            RaisePlayerModifierRemove(sourceMonster, target, removal.ModifierKey, removal.ModifierType, removal.SkillPath, removal.EffectPath, removal.Source ?? "unknown", removal.SourceFunction ?? "Modifiers::processRemoveModifier@0x00502390");
            Debug.LogError($"[PLAYER-MODIFIER] network-remove target={target.Name}#{target.EntityId} modifier={removal.ModifierType ?? ""} key={removal.ModifierKey ?? ""} sourceMonster={sourceMonster?.Name ?? "none"}#{sourceMonster?.EntityId ?? 0u} source={removal.Source ?? "unknown"} sourceFunction={removal.SourceFunction ?? "Modifiers::processRemoveModifier@0x00502390"}");
        }

        private void RemovePlayerDamageModifiersForTarget(uint targetEntityId, string source, bool removeOnlyOnDeath = true)
        {
            if (targetEntityId == 0 || _activePlayerDamageModifiers.Count == 0) return;
            var removals = _activePlayerDamageModifiers
                .Where(modifierEntry => modifierEntry.Value != null && modifierEntry.Value.TargetEntityId == targetEntityId && (!removeOnlyOnDeath || modifierEntry.Value.RemoveOnDeath))
                .Select(modifierEntry => new { Key = modifierEntry.Key, Mod = modifierEntry.Value })
                .ToList();
            _players.TryGetValue(targetEntityId, out CombatPlayer target);
            foreach (var removal in removals)
            {
                _activeMonsters.TryGetValue(removal.Mod.SourceEntityId, out Monster sourceMonster);
                RaisePlayerModifierRemove(sourceMonster, target, removal.Key, removal.Mod.ModifierPath, removal.Mod.SkillPath, removal.Mod.EffectPath, source ?? "unknown", "Modifiers::processRemoveModifier@0x00502390");
                _activePlayerDamageModifiers.Remove(removal.Key);
            }
            if (removals.Count > 0)
                Debug.LogError($"[PLAYER-EFFECTMOD] remove target={targetEntityId} count={removals.Count} source={source ?? "unknown"} removeOnlyOnDeath={removeOnlyOnDeath} sourceFunction=ModifierDesc.RemoveOnDeath");
        }

        private void RemovePlayerModifiersFromSource(uint sourceEntityId, string source)
        {
            if (sourceEntityId == 0) return;
            _activeMonsters.TryGetValue(sourceEntityId, out Monster sourceMonster);
            var removals = _activePlayerDamageModifiers
                .Where(modifierEntry => modifierEntry.Value != null && modifierEntry.Value.SourceEntityId == sourceEntityId)
                .Select(modifierEntry => new { Key = modifierEntry.Key, Mod = modifierEntry.Value })
                .ToList();
            foreach (var removal in removals)
            {
                _players.TryGetValue(removal.Mod.TargetEntityId, out CombatPlayer target);
                RaisePlayerModifierRemove(sourceMonster, target, removal.Key, removal.Mod.ModifierPath, removal.Mod.SkillPath, removal.Mod.EffectPath, source ?? "unknown", "Modifier::update@0x004FF1B0 Modifiers::processRemoveModifier@0x00502390");
                _activePlayerDamageModifiers.Remove(removal.Key);
            }

            int attributeRemoved = 0;
            foreach (var player in _players.Values.ToList())
            {
                if (player?.PlayerState == null)
                    continue;
                attributeRemoved += player.PlayerState.RemoveAttributeModifiersFromSource(sourceEntityId, source ?? "source-unit-removed");
            }

            if (removals.Count > 0 || attributeRemoved > 0)
                Debug.LogError($"[PLAYER-MODIFIER] remove-source sourceEntity={sourceEntityId} effectMods={removals.Count} attributeMods={attributeRemoved} source={source ?? "unknown"} sourceFunction=Modifier::update@0x004FF1B0 Modifiers::processRemoveModifier@0x00502390");
        }

        private bool ApplyPlayerDamageModifierFromMonster(Monster sourceMonster, CombatPlayer target, MonsterDamageModifierSkillEffect effect, float now, string source)
        {
            if (sourceMonster == null || target == null || target.PlayerState == null || effect == null)
                return false;
            if (!target.IsAlive || target.PlayerState.CurrentHPWire == 0)
                return false;

            float applyTime = now >= 0f ? now : GetCombatTime();
            float frequency = effect.FrequencySeconds > 0f ? effect.FrequencySeconds : 1f;
            float duration = effect.DurationSeconds > 0f ? effect.DurationSeconds : 0f;
            ushort durationTicks = effect.DurationTicks > 0 ? effect.DurationTicks : ComputeSpellModDurationTicks(duration);
            ushort frequencyTicks = effect.FrequencyTicks > 0 ? effect.FrequencyTicks : ComputeEffectModFrequencyTicks(frequency);
            int maxTicks = ComputeEffectModApplyTickBudget(durationTicks, frequencyTicks);
            float intervalSeconds = ComputeEffectModIntervalSeconds(frequencyTicks);
            float expireTime = ComputeEffectModExpireTime(applyTime, durationTicks);
            string key = BuildPlayerDamageModifierKey(target.EntityId, sourceMonster.EntityId, effect);
            bool replace = _activePlayerDamageModifiers.TryGetValue(key, out ActivePlayerDamageModifier existing);
            uint powerLevel = ResolveMonsterSkillPowerLevelWire(sourceMonster, effect.SkillPath);
            uint existingRemainingTicks = replace ? ResolveActivePlayerModifierRemainingTicks(existing, applyTime) : 0u;
            bool stackAccepted = !replace || ShouldAcceptModifierStack(effect.StackRule, powerLevel, durationTicks, existing.PowerLevel, existingRemainingTicks);
            RaisePlayerModifierAdd(sourceMonster, target, key, effect.ModifierPath, 1, powerLevel, durationTicks, CLIENT_RUNTIME_MODIFIER_SOURCE_IS_SELF, replace, effect.SkillPath, effect.EffectPath, source ?? "unknown", "SpellModEffect::doEffect@0x00554460 Modifiers::processAddModifier@0x00502280", stackAccepted);
            if (!stackAccepted)
            {
                Debug.LogError($"[PLAYER-MODIFIER-STACK] result=reject target={target.Name}#{target.EntityId} source={sourceMonster.Name}#{sourceMonster.EntityId} skill={effect.SkillPath} modifier={effect.ModifierPath} stack={effect.StackRule ?? "UNKNOWN"} key={key} incomingPower={powerLevel} existingPower={existing.PowerLevel} incomingDuration={durationTicks} existingRemaining={existingRemainingTicks} sourceIsSelf={CLIENT_RUNTIME_MODIFIER_SOURCE_IS_SELF} sourceFunction=Modifiers::addModifierLocal@0x00501770");
                return false;
            }
            _activePlayerDamageModifiers[key] = new ActivePlayerDamageModifier
            {
                TargetEntityId = target.EntityId,
                SourceEntityId = sourceMonster.EntityId,
                SkillPath = effect.SkillPath,
                EffectPath = effect.EffectPath,
                ModifierPath = effect.ModifierPath,
                ModifierEffectPath = effect.ModifierEffectPath,
                AttackType = effect.AttackType,
                DamageType = effect.DamageType,
                DamageTypeId = effect.DamageTypeId,
                DamageKind = effect.DamageKind,
                DamageMod = effect.DamageMod,
                DamageVolatility = effect.DamageVolatility,
                SourceLevel = Math.Max(1, (int)sourceMonster.Level),
                SourceIntellect = ResolveMonsterSpellIntellect(sourceMonster),
                SourceAgility = ResolveMonsterSpellAgility(sourceMonster),
                PowerLevel = powerLevel,
                DurationTicks = durationTicks,
                FrequencyTicks = frequencyTicks,
                ApplyTime = applyTime,
                NextTickTime = applyTime + intervalSeconds,
                Frequency = intervalSeconds,
                ExpireTime = expireTime,
                MaxTicks = maxTicks,
                TicksApplied = 0,
                RemoveOnDeath = effect.RemoveOnDeath,
                StackRule = effect.StackRule,
                ModifierKey = key
            };

            Debug.LogError($"[PLAYER-EFFECTMOD] {(replace ? "replace" : "add")} target={target.Name}#{target.EntityId} source={sourceMonster.Name}#{sourceMonster.EntityId} skill={effect.SkillPath} modifier={effect.ModifierPath} effect={effect.ModifierEffectPath} attackType={effect.AttackType} damageTypeId={effect.DamageTypeId} damageKind={effect.DamageKind} duration={duration:F2}s frequency={frequency:F2}s durationTicks={durationTicks} frequencyTicks={frequencyTicks} maxTicks={maxTicks} power={powerLevel} stack={effect.StackRule ?? "UNKNOWN"} firstTickAt={applyTime + intervalSeconds:F3} expireAt={expireTime:F3} source={source ?? "unknown"} sourceFunction=SpellModEffect::doEffect@0x00554460 EffectMod::init@0x0055FE20 EffectMod::update@0x0055FE70 Modifiers::addModifierLocal@0x00501770");
            return true;
        }

        private static int ResolveMonsterSpellIntellect(Monster monster)
        {
            if (monster?.Slots == null) return 10;
            return Math.Max(1, monster.Slots.Get(UnitSlot.Intellect, 10));
        }

        private static int ResolveMonsterSpellAgility(Monster monster)
        {
            if (monster?.Slots == null) return 10;
            return Math.Max(1, monster.Slots.Get(UnitSlot.Agility, 10));
        }

        private MonsterModifierApplyResult AdvancePlayerDamageModifierRuntime(uint onlyTargetEntityId, float now, string source)
        {
            var aggregate = new MonsterModifierApplyResult
            {
                Reason = source ?? "player-effectmod-runtime"
            };
            if (_activePlayerDamageModifiers.Count == 0)
                return aggregate;
            if (now < 0f) now = GetCombatTime();

            var keys = _activePlayerDamageModifiers.Keys.ToList();
            foreach (string key in keys)
            {
                if (!_activePlayerDamageModifiers.TryGetValue(key, out var mod) || mod == null)
                    continue;
                if (onlyTargetEntityId != 0 && mod.TargetEntityId != onlyTargetEntityId)
                    continue;
                if (!_players.TryGetValue(mod.TargetEntityId, out var target) || target == null || target.PlayerState == null)
                {
                    _activePlayerDamageModifiers.Remove(key);
                    _playerModifierNetworkIds.Remove(key);
                    Debug.LogError($"[PLAYER-EFFECTMOD] remove target={mod.TargetEntityId} modifier={mod.ModifierKey} source={source ?? "unknown"} reason=missing-target sourceFunction=EffectMod::update@0x0055FE70");
                    continue;
                }
                if (!target.IsAlive || target.PlayerState.CurrentHPWire == 0)
                {
                    if (mod.RemoveOnDeath)
                    {
                        RaisePlayerModifierRemove(null, target, key, mod.ModifierPath, mod.SkillPath, mod.EffectPath, source ?? "unknown", "ModifierDesc.RemoveOnDeath Modifiers::processRemoveModifier@0x00502390");
                        _activePlayerDamageModifiers.Remove(key);
                        Debug.LogError($"[PLAYER-EFFECTMOD] remove target={target.Name}#{target.EntityId} modifier={mod.ModifierKey} source={source ?? "unknown"} reason=RemoveOnDeath sourceFunction=ModifierDesc.RemoveOnDeath");
                    }
                    continue;
                }
                if (!_activeMonsters.TryGetValue(mod.SourceEntityId, out var sourceMonster) || sourceMonster == null)
                {
                    RaisePlayerModifierRemove(null, target, key, mod.ModifierPath, mod.SkillPath, mod.EffectPath, source ?? "unknown", "EffectMod::update@0x0055FE70 Modifiers::processRemoveModifier@0x00502390");
                    _activePlayerDamageModifiers.Remove(key);
                    Debug.LogError($"[PLAYER-EFFECTMOD] remove target={target.Name}#{target.EntityId} modifier={mod.ModifierKey} source={source ?? "unknown"} reason=missing-source-monster sourceFunction=EffectMod::update@0x0055FE70");
                    continue;
                }

                MersenneTwister modRng = GetRoomRngForMonster(sourceMonster);
                if (modRng == null)
                {
                    RuntimeEvidence.LogFallbackHit(
                        "rng-instance",
                        "player-effectmod-missing-instance-rng",
                        $"target={target.EntityId} sourceMonster={sourceMonster.EntityId} source={source ?? "unknown"}",
                        64);
                    Debug.LogError($"[PLAYER-EFFECTMOD] skip tick target={target.Name}#{target.EntityId} sourceMonster={sourceMonster.Name}#{sourceMonster.EntityId} source={source ?? "unknown"} reason=missing-instance-rng");
                    continue;
                }
                bool expiredBeforeTick = IsEffectModExpired(mod.DurationTicks, mod.ExpireTime, now);
                if (expiredBeforeTick && (mod.MaxTicks == 0 || mod.TicksApplied >= mod.MaxTicks || mod.NextTickTime > mod.ExpireTime + 0.0001f))
                {
                    RaisePlayerModifierRemove(sourceMonster, target, key, mod.ModifierPath, mod.SkillPath, mod.EffectPath, source ?? "unknown", "Modifiers::update@0x00501E50 Modifiers::removeModifierLocal@0x00501B50");
                    _activePlayerDamageModifiers.Remove(key);
                    Debug.LogError($"[PLAYER-EFFECTMOD] complete target={target.Name}#{target.EntityId} source={sourceMonster.Name}#{sourceMonster.EntityId} ticks={mod.TicksApplied}/{mod.MaxTicks} hp={target.PlayerState.CurrentHPWire} reason=duration-expired expireAt={mod.ExpireTime:F3} now={now:F3} source={source ?? "unknown"} sourceFunction=Modifier::update@0x004FF1B0 EffectMod::update@0x0055FE70");
                    continue;
                }
                if (now + 0.0001f < mod.NextTickTime)
                    continue;

                while (now + 0.0001f >= mod.NextTickTime && mod.TicksApplied < mod.MaxTicks && mod.NextTickTime <= mod.ExpireTime + 0.0001f && target.IsAlive && target.PlayerState.CurrentHPWire > 0)
                {
                    int tickIndex = mod.TicksApplied + 1;
                    int minDamage;
                    int maxDamage;
                    if (string.Equals(mod.AttackType, "RANGED", StringComparison.OrdinalIgnoreCase))
                        DamageResolver.ComputeRangedDamageRange(mod.SourceLevel, Math.Max(1, mod.SourceAgility), 1f, mod.DamageMod, mod.DamageVolatility, out minDamage, out maxDamage);
                    else
                        DamageResolver.ComputeSpellDamageRange(mod.SourceLevel, Math.Max(1, mod.SourceIntellect), mod.DamageMod, mod.DamageVolatility, out minDamage, out maxDamage);

                    uint damageRaw = RngLedger.Generate(modRng, "room", "EffectMod::applyEffect:SpellDamageEffect::damage", $"{sourceMonster.Name}#{sourceMonster.EntityId}->{target.Name}#{target.EntityId}");
                    int rawDamageWire = DamageResolver.RollSpellDamageRange(minDamage, maxDamage, damageRaw);
                    uint damageWire = (uint)Math.Max(0, rawDamageWire);
                    uint beforeHP = target.PlayerState.CurrentHPWire;
                    DamageQueryResult query = ApplyPlayerDamageQueryWire(damageWire, target, sourceMonster, mod.DamageTypeId, mod.DamageKind, mod.NextTickTime, source ?? "EffectMod::applyEffect", mod.SourceLevel, modRng, true);
                    uint adjustedDamageWire = query.AdjustedDamageWire;
                    uint afterHP = beforeHP;
                    uint effectRaw = 0;
                    bool applied = adjustedDamageWire > 0;

                    if (applied)
                    {
                        target.PlayerState.TakeQueriedDamage(adjustedDamageWire, mod.NextTickTime);
                        afterHP = target.PlayerState.CurrentHPWire;
                        target.IsAlive = afterHP > 0;
                        effectRaw = ConsumeOnApplyDamageEffectRng(modRng, "monster-effectmod", target.EntityId, target.Name, beforeHP, afterHP, target.PlayerState.MaxHPWire, adjustedDamageWire, source ?? "EffectMod::applyEffect", physicalWeaponHit: false);
                        if (mod.DamageKind != 3 && beforeHP > afterHP)
                            ApplyMonsterOnDamageCallback(sourceMonster, beforeHP - afterHP, mod.NextTickTime, source ?? "EffectMod::applyEffect");
                    }

                    string resultName = applied ? "HIT" : query.ResultName;
                    Debug.LogError($"[PLAYER-EFFECTMOD-TICK] target={target.Name}#{target.EntityId} source={sourceMonster.Name}#{sourceMonster.EntityId} tick={tickIndex}/{mod.MaxTicks} result={resultName} skill={mod.SkillPath} modifier={mod.ModifierPath} effect={mod.ModifierEffectPath} attackType={mod.AttackType} damageType={mod.DamageType} damageTypeId={mod.DamageTypeId} damageKind={mod.DamageKind} preQueryWire={damageWire} damageWire={adjustedDamageWire} hp={beforeHP}->{afterHP}/{target.PlayerState.MaxHPWire} range=[{minDamage},{maxDamage}] damageRaw=0x{damageRaw:X8} effectRaw=0x{effectRaw:X8} resist={DamageResistLogCode(query)} due={mod.NextTickTime:F3} now={now:F3} rngAfter={modRng.CallsSinceReseed} sourceFunction=EffectMod::applyEffect@0x0055FF70 SpellDamageEffect::doEffect@0x0054FD20");
                    Debug.LogError($"[CLIENT-DAMAGE-CONTRACT] source={source ?? "EffectMod::applyEffect"} target={target.Name}#{target.EntityId} damageTypeId={mod.DamageTypeId} damageKind={mod.DamageKind} rawRollWire={damageWire} preQueryWire={damageWire} damageTakenMod={query.DamageTakenMod:F2} typeMod={query.DamageTypeMod:F2} postQueryWire={adjustedDamageWire} hp={beforeHP}->{afterHP}/{target.PlayerState.MaxHPWire} resist={query.ResultName} resistRaw=0x{query.ResistRaw:X8} resistChance={query.ResistChanceWire / 256f:F2} clientDamageTime={mod.NextTickTime:F3} target=Avatar sourceFunction=Damage::apply@0x004F6580 Unit::onQueryApplyDamage@0x0050B9C0");
                    Debug.LogError($"[PLAYER-DAMAGE] source=monster-effectmod attacker={sourceMonster.Name}#{sourceMonster.EntityId} target={target.Name}#{target.EntityId} result={resultName} skill={mod.SkillPath} damageWire={adjustedDamageWire} preQueryWire={damageWire} hp={beforeHP}->{afterHP}/{target.PlayerState.MaxHPWire} marker=SPELL-MOD source={source} damageRaw=0x{damageRaw:X8} effectRaw=0x{effectRaw:X8} rngPos={modRng.CallsSinceReseed}");
                    if (applied)
                        OnPlayerDamageResolved?.Invoke(sourceMonster, target, true, afterHP, source != null ? $"EffectMod::applyEffect:{source}" : "EffectMod::applyEffect");

                    aggregate.DamageApplied |= applied;
                    aggregate.Died |= afterHP == 0;
                    if (aggregate.TicksApplied == 0)
                        aggregate.OldHPWire = beforeHP;
                    aggregate.NewHPWire = afterHP;
                    mod.TicksApplied++;
                    aggregate.TicksApplied++;
                    mod.NextTickTime += mod.Frequency;
                    if (afterHP == 0)
                    {
                        RemovePlayerDamageModifiersForTarget(target.EntityId, source ?? "EffectMod::target-death");
                        break;
                    }
                }

                if (_activePlayerDamageModifiers.TryGetValue(key, out var afterMod))
                {
                    bool targetDead = !target.IsAlive || target.PlayerState.CurrentHPWire == 0;
                    bool expired = IsEffectModExpired(afterMod.DurationTicks, afterMod.ExpireTime, now);
                    bool removeAfterTick = expired || (targetDead && afterMod.RemoveOnDeath);
                    if (removeAfterTick)
                    {
                        string reason = targetDead && afterMod.RemoveOnDeath ? "RemoveOnDeath" : "duration-expired";
                        RaisePlayerModifierRemove(sourceMonster, target, key, afterMod.ModifierPath, afterMod.SkillPath, afterMod.EffectPath, source ?? "unknown", reason == "RemoveOnDeath" ? "ModifierDesc.RemoveOnDeath Modifiers::processRemoveModifier@0x00502390" : "Modifiers::update@0x00501E50 Modifiers::removeModifierLocal@0x00501B50");
                        _activePlayerDamageModifiers.Remove(key);
                        Debug.LogError($"[PLAYER-EFFECTMOD] complete target={target.Name}#{target.EntityId} source={sourceMonster.Name}#{sourceMonster.EntityId} ticks={afterMod.TicksApplied}/{afterMod.MaxTicks} hp={target.PlayerState.CurrentHPWire} reason={reason} expireAt={afterMod.ExpireTime:F3} now={now:F3} source={source ?? "unknown"} sourceFunction=EffectMod::update@0x0055FE70");
                    }
                }
            }

            return aggregate;
        }

        public MonsterModifierApplyResult ApplyProjectileModifierFromSpell(Monster target, uint sourceEntityId, PlayerState sourceState, SpellData spell, MersenneTwister rng, int skillLevel, float now, string source)
        {
            var result = new MonsterModifierApplyResult
            {
                Reason = source ?? "spell-mod",
                OldHPWire = target != null ? PeekRuntimeMonsterHPWire(target) : 0,
                NewHPWire = target != null ? PeekRuntimeMonsterHPWire(target) : 0
            };

            if (target == null || spell == null || !spell.HasProjectileModifierDamage)
            {
                result.Reason = "missing-target-or-modifier";
                return result;
            }
            if (!target.IsAlive || PeekRuntimeMonsterHPWire(target) == 0)
            {
                result.Reason = "target-dead";
                return result;
            }
            if (sourceState == null)
            {
                result.Reason = "missing-source-state";
                return result;
            }
            if (rng == null)
            {
                result.Reason = "missing-rng";
                return result;
            }

            float applyTime = now >= 0f ? now : GetCombatTime();
            float frequency = spell.ProjectileModifierFrequency > 0f ? spell.ProjectileModifierFrequency : 1f;
            float duration = spell.ProjectileModifierDuration > 0f ? spell.ProjectileModifierDuration : 0f;
            ushort durationTicks = ComputeSpellModDurationTicks(duration);
            ushort frequencyTicks = ComputeEffectModFrequencyTicks(frequency);
            int startTick = (int)ResolveTickForTime(applyTime);
            int initialFrequencyCountdown = Math.Max(1, (int)frequencyTicks);
            string key = BuildMonsterModifierKey(target.EntityId, sourceEntityId, spell);
            bool replace = _activeMonsterModifiers.ContainsKey(key);
            _activeMonsterModifiers[key] = new ActiveMonsterModifier
            {
                TargetEntityId = target.EntityId,
                SourceEntityId = sourceEntityId,
                SourceState = sourceState,
                Spell = spell,
                SkillLevel = skillLevel,
                LastTick = startTick,
                DurationTicksInitial = durationTicks,
                DurationTicksRemaining = durationTicks,
                FrequencyTicks = frequencyTicks,
                FrequencyCountdownTicks = initialFrequencyCountdown,
                TicksApplied = 0,
                ModifierKey = key
            };

            result.AppliedModifier = true;
            result.NewHPWire = PeekRuntimeMonsterHPWire(target);
            Debug.LogError($"[POISON-SHOT-MOD] {(replace ? "replace" : "add")} target={target.Name}#{target.EntityId} source={sourceEntityId} modifier={spell.ProjectileModifierId} effect={spell.ProjectileModifierEffectId} duration={duration:F2}s frequency={frequency:F2}s durationTicks={durationTicks} frequencyTicks={frequencyTicks} countdown={initialFrequencyCountdown} stack={spell.ProjectileModifierStackRule ?? "UNKNOWN"} firstTick=deferred impactTime={applyTime:F3} clientTick={startTick} hp={result.OldHPWire}->{result.NewHPWire} rngBefore={rng.CallsSinceReseed} source={source ?? "unknown"} sourceFunction=SpellModEffect::doEffect@0x00554460 EffectMod::init@0x0055FE20 EffectMod::update@0x0055FE70");
            return result;
        }

        public MonsterModifierApplyResult AdvanceMonsterModifierRuntimeForTarget(uint targetEntityId, MersenneTwister rng, float now, string source)
        {
            return AdvanceMonsterModifierRuntime(rng, now, source, targetEntityId);
        }

        private MonsterModifierApplyResult AdvanceMonsterModifierRuntime(MersenneTwister rng, float now, string source, uint onlyTargetEntityId = 0)
        {
            var aggregate = new MonsterModifierApplyResult
            {
                Reason = source ?? "modifier-runtime"
            };
            if (_advancingMonsterModifiers || _activeMonsterModifiers.Count == 0)
                return aggregate;
            if (now < 0f) now = GetCombatTime();
            int nowTick = (int)ResolveTickForTime(now);

            _advancingMonsterModifiers = true;
            try
            {
                var keys = _activeMonsterModifiers.Keys.ToList();
                foreach (string key in keys)
                {
                    if (!_activeMonsterModifiers.TryGetValue(key, out var mod) || mod == null)
                        continue;
                    if (onlyTargetEntityId != 0 && mod.TargetEntityId != onlyTargetEntityId)
                        continue;
                    if (!_activeMonsters.TryGetValue(mod.TargetEntityId, out var monster) || monster == null || !monster.IsAlive || PeekRuntimeMonsterHPWire(monster) == 0)
                    {
                        _activeMonsterModifiers.Remove(key);
                        Debug.LogError($"[POISON-SHOT-MOD] remove target={mod.TargetEntityId} modifier={mod.ModifierKey} source={source ?? "unknown"} reason=RemoveOnDeathOrMissing");
                        continue;
                    }
                    if (mod.SourceState == null || mod.Spell == null)
                    {
                        _activeMonsterModifiers.Remove(key);
                        Debug.LogError($"[POISON-SHOT-MOD] remove target={mod.TargetEntityId} modifier={mod.ModifierKey} source={source ?? "unknown"} reason=missing-source-state");
                        continue;
                    }
                    MersenneTwister modRng = GetRoomRngForMonster(monster);
                    if (modRng == null)
                    {
                        RuntimeEvidence.LogFallbackHit(
                            "rng-instance",
                            "modifier-missing-instance-rng",
                            $"target={monster.EntityId} source={source ?? "unknown"}",
                            64);
                        Debug.LogError($"[POISON-SHOT-MOD] skip tick target={monster.Name}#{monster.EntityId} source={source ?? "unknown"} reason=missing-instance-rng");
                        continue;
                    }
                    if (nowTick <= mod.LastTick)
                        continue;

                    int ticksToProcess = Math.Min(256, nowTick - mod.LastTick);
                    string removeAfterTick = null;
                    for (int modifierTickIndex = 0; modifierTickIndex < ticksToProcess && monster.IsAlive && PeekRuntimeMonsterHPWire(monster) > 0; modifierTickIndex++)
                    {
                        mod.LastTick++;
                        if (mod.DurationTicksInitial > 0)
                        {
                            if (mod.DurationTicksRemaining > 0)
                                mod.DurationTicksRemaining--;
                            if (mod.DurationTicksRemaining == 0)
                            {
                                removeAfterTick = "duration-expired";
                                break;
                            }
                        }

                        if (mod.FrequencyCountdownTicks > 0)
                            mod.FrequencyCountdownTicks--;
                        if (mod.FrequencyCountdownTicks != 0)
                            continue;

                        int appliedTickIndex = mod.TicksApplied + 1;
                        float clientDamageTime = mod.LastTick * CLIENT_UNIT_TICK_INTERVAL;
                        var damage = DamageResolver.ProcessProjectileModifierTick(
                            modRng,
                            mod.SourceState.Level,
                            mod.SourceState.ClientSpellIntellect,
                            mod.SourceState.ClientSpellAgility,
                            mod.SourceState.ClientSpellStrength,
                            mod.SourceState.WeaponDamage,
                            mod.SourceState.WeaponDamageVolatility,
                            mod.Spell,
                            monster,
                            mod.SkillLevel,
                            DamageResolver.ResolveCriticalDamagePercent(mod.SourceState),
                            mod.SourceState,
                            DamageResolver.ResolveSpellCriticalThreshold(mod.SourceState, monster, mod.Spell?.ProjectileModifierCriticalChance ?? 0f));

                        if (damage.Type == AttackResultType.Miss || damage.DamageF32 <= 0)
                        {
                            Debug.LogError($"[POISON-SHOT-TICK] target={monster.Name}#{monster.EntityId} tick={appliedTickIndex} result={damage.Type} damageWire=0 hp={PeekRuntimeMonsterHPWire(monster)} durationRemaining={mod.DurationTicksRemaining} frequencyTicks={mod.FrequencyTicks} clientTick={mod.LastTick} source={source ?? "unknown"} rngAfter={modRng.CallsSinceReseed}");
                        }
                        else
                        {
                            bool applied = ApplyPlayerDamageToMonsterWire(
                                monster,
                                (uint)damage.DamageF32,
                                "SPELL-MOD",
                                out uint oldHPWire,
                                out uint newHPWire,
                                out bool died,
                                clientDamageTime: clientDamageTime,
                                clientDamageTick: (uint)Math.Max(0, mod.LastTick),
                                damageTypeId: damage.DamageTypeId,
                                rawDamageWire: (uint)damage.DamageF32);
                            if (applied)
                                NotifyMonsterDamagedByPlayer(monster, mod.SourceEntityId, "modifier-tick");
                            uint effectRaw = applied
                                ? ConsumeOnApplyDamageEffectRng(modRng, "player-spell-mod", monster.EntityId, monster.Name, oldHPWire, newHPWire, monster.MaxHPWire, (uint)damage.DamageF32, source ?? "modifier-tick", physicalWeaponHit: false)
                                : 0;
                            string resultName = damage.Type.ToString().ToUpperInvariant();
                            Debug.LogError($"[POISON-SHOT-TICK] target={monster.Name}#{monster.EntityId} source={mod.SourceEntityId} tick={appliedTickIndex} result={resultName} damageWire={damage.DamageF32} hp={oldHPWire}->{newHPWire} range=[{damage.MinDamageF32},{damage.MaxDamageF32}] damageRaw=0x{damage.DamageRaw:X8} effectRaw=0x{effectRaw:X8} durationRemaining={mod.DurationTicksRemaining} frequencyTicks={mod.FrequencyTicks} clientTick={mod.LastTick} due={clientDamageTime:F3} now={now:F3} applied={applied} died={died} rngAfter={modRng.CallsSinceReseed}");
                            Debug.LogError($"[COMBAT-EVENT] actor=player-spell-mod actorId={mod.SourceEntityId} target=monster targetId={monster.EntityId} result={resultName} damageWire={damage.DamageF32} hp={oldHPWire}->{newHPWire} range=[{damage.MinDamageF32},{damage.MaxDamageF32}] damageRaw=0x{damage.DamageRaw:X8} effectRaw=0x{effectRaw:X8} spell={mod.Spell.DisplayName} rngAfter={modRng.CallsSinceReseed} marker=SPELL-MOD");
                            aggregate.DamageApplied |= applied;
                            aggregate.Died |= died;
                            if (aggregate.TicksApplied == 0)
                                aggregate.OldHPWire = oldHPWire;
                            aggregate.NewHPWire = newHPWire;
                            if (died)
                            {
                                _pendingModifierKills.Enqueue(new PendingModifierKill
                                {
                                    SourceEntityId = mod.SourceEntityId,
                                    TargetEntityId = monster.EntityId,
                                    Source = source ?? "modifier-tick",
                                    DamageTime = clientDamageTime
                                });
                            }
                        }

                        mod.TicksApplied++;
                        aggregate.TicksApplied++;
                        mod.FrequencyCountdownTicks = Math.Max(1, (int)mod.FrequencyTicks);
                        if (!monster.IsAlive || PeekRuntimeMonsterHPWire(monster) == 0)
                            break;
                    }

                    if (removeAfterTick != null || !monster.IsAlive || PeekRuntimeMonsterHPWire(monster) == 0)
                    {
                        _activeMonsterModifiers.Remove(key);
                        string reason = !monster.IsAlive || PeekRuntimeMonsterHPWire(monster) == 0 ? "target-dead" : removeAfterTick;
                        Debug.LogError($"[POISON-SHOT-MOD] complete target={monster.Name}#{monster.EntityId} source={mod.SourceEntityId} ticks={mod.TicksApplied} durationTicks={mod.DurationTicksInitial} remaining={mod.DurationTicksRemaining} hp={PeekRuntimeMonsterHPWire(monster)} reason={reason ?? "target-dead"} source={source ?? "unknown"} sourceFunction=Modifier::update@0x004FF1B0 EffectMod::update@0x0055FE70");
                    }
                }
            }
            finally
            {
                _advancingMonsterModifiers = false;
            }

            return aggregate;
        }

        private void SetRuntimeMonsterHPWire(Monster monster, uint hp, bool committedDamage, float regenClockTime = -1f, string source = "SET")
        {
            SetRuntimeMonsterHPWire(monster, hp, committedDamage, regenClockTime, source, 0u, -1f);
        }

        private void SetRuntimeMonsterHPWire(Monster monster, uint hp, bool committedDamage, float regenClockTime, string source, uint clientTick)
        {
            SetRuntimeMonsterHPWire(monster, hp, committedDamage, regenClockTime, source, clientTick, -1f);
        }

        private void SetRuntimeMonsterHPWire(Monster monster, uint hp, bool committedDamage, float regenClockTime, string source, uint clientTick, float historyTime)
        {
            if (monster == null) return;
            if (hp > monster.MaxHPWire) hp = monster.MaxHPWire;
            var state = GetMonsterHPState(monster);
            uint oldHP = state.RuntimeHPWire;
            float resolvedHistoryTime = historyTime >= 0f ? historyTime : (regenClockTime >= 0f ? regenClockTime : GetCombatTime());
            RecordMonsterHPHistory(monster, state, oldHP, hp, committedDamage, resolvedHistoryTime, source, clientTick);
            state.RuntimeHPWire = hp;
            state.RuntimeInitialized = true;
            if (hp == 0 || hp >= monster.MaxHPWire)
            {
                monster.DeathPendingClientConfirmation = false;
                monster.DeathPendingSince = 0f;
            }
            if (committedDamage && hp > 0 && hp < monster.MaxHPWire)
            {
                _monsterRuntimeDamageCommitted.Add(monster.EntityId);
                ushort cooldown = ResolveDamageRegenCooldownTicks(monster);
                if (cooldown > 0)
                    _monsterHPRegenCooldownTicks[monster.EntityId] = cooldown;
                else
                    _monsterHPRegenCooldownTicks.Remove(monster.EntityId);
                Debug.LogError($"[MON-REGEN-COOLDOWN] entity={monster.EntityId} name={monster.Name} cooldown={cooldown} stockUnitDelayClear={ShouldClearStockUnitDamageRegenDelay(monster)} source={source ?? "unknown"}");
            }
            else if (hp == 0 || (!committedDamage && hp >= monster.MaxHPWire))
            {
                _monsterRuntimeDamageCommitted.Remove(monster.EntityId);
                _monsterHPRegenCooldownTicks.Remove(monster.EntityId);
            }
            ApplyMonsterHPState(monster, state);
            ResetMonsterHPRegenClock(monster, regenClockTime);
            if (oldHP != hp)
                Debug.LogError($"[MON-HP-CANON] write entity={monster.EntityId} old={oldHP} new={hp} committed={committedDamage} source={source ?? "unknown"}");
        }

        private ushort ResolveDamageRegenCooldownTicks(Monster monster)
        {
            return ShouldClearStockUnitDamageRegenDelay(monster) ? (ushort)0 : CLIENT_DAMAGE_REGEN_COOLDOWN_TICKS;
        }

        private bool ShouldClearStockUnitDamageRegenDelay(Monster monster)
        {
            return monster != null && ResolveStockUnitDamageRegenDelayClearFlag();
        }

        private bool ResolveStockUnitDamageRegenDelayClearFlag()
        {
            int clientWorldSettingsField = GCDatabase.Instance.GetKnobInt("MinLevelForWorldChat", 15);
            return (clientWorldSettingsField & 0x00000800) != 0;
        }

        public uint GetMonsterCurrentHPWire(Monster monster)
        {
            return GetRuntimeMonsterHPWire(monster, "HP-READ");
        }

        public uint GetMonsterCurrentHPWire(Monster monster, string source)
        {
            return GetRuntimeMonsterHPWire(monster, source);
        }

        public uint GetMonsterCurrentHPWire(Monster monster, string source, float now)
        {
            return GetRuntimeMonsterHPWire(monster, source, now);
        }

        public uint PeekMonsterCurrentHPWire(Monster monster)
        {
            return PeekRuntimeMonsterHPWire(monster);
        }

        private static bool IsRuntimeMonsterHPSuffixContext(EntitySynchInfoContext context)
        {
            return context == EntitySynchInfoContext.MonsterAction
                || context == EntitySynchInfoContext.MonsterMove
                || context == EntitySynchInfoContext.MonsterDamage;
        }

        public string DescribeMonsterHPState(Monster monster)
        {
            if (monster == null) return "monster=<null>";
            var state = GetMonsterHPState(monster);
            if (state == null) return $"{monster.Name}#{monster.EntityId} state=<null>";

            float observedAge = state.LastClientHPReportTime > 0f ? Time.time - state.LastClientHPReportTime : -1f;
            bool dirty = _monsterRuntimeDamageCommitted.Contains(monster.EntityId);
            return $"{monster.Name}#{monster.EntityId} runtime={state.RuntimeHPWire}/{monster.MaxHPWire} dirty={dirty} client={state.LastClientHPReportWire} clientAge={observedAge:F3} lastPacket={state.LastPacketHPWire}";
        }

        public bool TryResolveMonsterEntitySynchInfoHP(Monster monster, string packetName, out uint hpWire)
        {
            return TryResolveMonsterEntitySynchInfoHP(monster, EntitySynchInfoContext.Unknown, packetName, out hpWire, out _);
        }

        public bool TryResolveMonsterEntitySynchInfoHP(Monster monster, EntitySynchInfoContext context, string packetName, out uint hpWire, out string reason)
        {
            return TryResolveMonsterEntitySynchInfoHP(monster, context, packetName, -1f, out hpWire, out reason);
        }

        public bool TryResolveMonsterEntitySynchInfoHP(Monster monster, EntitySynchInfoContext context, string packetName, float now, out uint hpWire, out string reason)
        {
            EntitySynchInfoVisibilityCutoff cutoff = new EntitySynchInfoVisibilityCutoff
            {
                Tick = ResolveTickForTime(now >= 0f ? now : GetCombatTime()),
                Time = now >= 0f ? now : GetCombatTime(),
                IncludeSubEntityEffects = true,
                Reason = "legacy-runtime-cutoff",
                Phase = "legacy-runtime",
                Context = context,
                SourceContext = packetName ?? "unknown",
                HasEntityCutoff = _hasCompletedEntityUpdate,
                LastEntityTick = _hasCompletedEntityUpdate ? _lastCompletedEntityUpdateTick : 0u,
                LastEntityTime = _hasCompletedEntityUpdate ? _lastCompletedEntityUpdateTime : -1f,
                HasSubEntityCutoff = _hasCompletedSubEntityUpdate,
                LastSubEntityTick = _hasCompletedSubEntityUpdate ? _lastCompletedSubEntityUpdateTick : 0u,
                LastSubEntityTime = _hasCompletedSubEntityUpdate ? _lastCompletedSubEntityUpdateTime : -1f
            };
            RuntimeEvidence.LogFallbackHit(
                "hp-cutoff",
                "legacy-runtime-cutoff",
                $"context={context} packet='{packetName ?? "unknown"}' tick={cutoff.Tick}",
                64);
            return TryResolveMonsterEntitySynchInfoHP(monster, context, packetName, cutoff, out hpWire, out reason);
        }

        public bool TryResolveMonsterEntitySynchInfoHP(Monster monster, EntitySynchInfoContext context, string packetName, EntitySynchInfoVisibilityCutoff cutoff, out uint hpWire, out string reason)
        {
            hpWire = 0;
            reason = "missing-monster";
            if (monster == null) return false;
            uint serverHPWire = PeekRuntimeMonsterHPWire(monster);
            var state = GetMonsterHPState(monster);
            if (state == null)
            {
                reason = "missing-monster-hp-state";
                return false;
            }

            bool dirtyRuntimeHP = _monsterRuntimeDamageCommitted.Contains(monster.EntityId)
                && serverHPWire > 0
                && serverHPWire < monster.MaxHPWire;
            bool exactClientObservedHP = state.LastClientHPReportTime > 0f
                && state.LastClientHPReportWire == serverHPWire;

            bool usedHistory = TryResolveMonsterHPFromHistory(monster, state, cutoff, out uint visibleHPWire, out string hpView, out string mutationSource, out string excludedSameTickProjectile);
            hpWire = serverHPWire;
            state.LastPacketHPWire = hpWire;
            ApplyMonsterHPState(monster, state);
            reason = exactClientObservedHP && hpWire == serverHPWire
                ? "client-confirmed-hp"
                : dirtyRuntimeHP ? "server-runtime-dirty-hp" : "server-runtime-hp";
            reason = $"{reason}; hpView={hpView}; visibleCutoffTick={cutoff.Tick}; visibleCutoffTime={cutoff.Time:F3}; cutoffPhase={cutoff.Phase}; includeSubEntity={cutoff.IncludeSubEntityEffects}; cutoffReason={cutoff.Reason}; mutationSource={mutationSource}; excludedSameTickProjectile={excludedSameTickProjectile}; runtimeHP={serverHPWire}; usedHistory={usedHistory}; lastEntity={cutoff.LastEntityTick}@{cutoff.LastEntityTime:F3}; lastSubEntity={cutoff.LastSubEntityTick}@{cutoff.LastSubEntityTime:F3}";
            if (usedHistory)
                Debug.LogError($"[MON-HP-VISIBLE] packet={packetName ?? "unknown"} context={context} entity={monster.EntityId} name={monster.Name} runtimeHP={serverHPWire} visibleHP={hpWire} cutoffPhase={cutoff.Phase} cutoffTick={cutoff.Tick} cutoffTime={cutoff.Time:F3} includeSubEntity={cutoff.IncludeSubEntityEffects} mutationSource={mutationSource} excludedSameTickProjectile={excludedSameTickProjectile}");
            return true;
        }

        private void CommitClientReportedMonsterHP(Monster monster, MonsterHPState state, uint hpWire, string source)
        {
            if (monster == null || state == null) return;
            if (hpWire > monster.MaxHPWire) hpWire = monster.MaxHPWire;
            if (state.RuntimeInitialized && state.RuntimeHPWire == hpWire) return;
            SetRuntimeMonsterHPWire(monster, hpWire, hpWire < monster.MaxHPWire, Time.time, $"client-authority:{source ?? "unknown"}");
        }

        public void RecordMonsterHPObservation(Monster monster, uint hpWire, string source)
        {
            if (monster == null) return;
            if (hpWire > monster.MaxHPWire) hpWire = monster.MaxHPWire;
            var state = GetMonsterHPState(monster);
            if (state == null) return;
            state.LastClientHPReportTime = Time.time;
            state.LastClientHPReportWire = hpWire;
            ApplyMonsterHPState(monster, state);
            CommitClientReportedMonsterHP(monster, state, hpWire, source);
            Debug.LogError($"[MON-HP-CLIENT] client-authoritative {monster.Name}#{monster.EntityId} hp={hpWire / 256f:F2}/{monster.MaxHPWire / 256f:F2} source={source ?? "unknown"}");
            if (EntitySynchInfoAuthority.Instance.TryResolveMonsterOwner(monster, out EntitySynchInfoOwnerRef owner))
                EntitySynchInfoAuthority.Instance.ObserveClientHpReport(owner, hpWire, EntitySynchInfoAuthority.ClassifyReportSource(source), source ?? "client-monster-hp", true, out _);
        }

        public void RecordMonsterOutboundHP(Monster monster, uint hpWire, string source)
        {
            if (monster == null) return;
            if (hpWire > monster.MaxHPWire) hpWire = monster.MaxHPWire;
            var state = GetMonsterHPState(monster);
            if (state == null) return;
            state.LastPacketHPWire = hpWire;
            ApplyMonsterHPState(monster, state);
            EntitySynchInfoAuthority.Instance.RecordMonsterOutboundHP(monster, hpWire, source ?? "outbound");
        }

        public void SetMonsterHPWire(Monster monster, uint hp, bool committedDamage = false, string source = "SET")
        {
            SetRuntimeMonsterHPWire(monster, hp, committedDamage, -1f, source);
        }

        public void NotifyMonsterDamagedByPlayer(Monster monster, uint playerEntityId, string reason)
        {
            NotifyMonsterOnAttackedAdmission(monster, playerEntityId, reason);
        }

        public void NotifyMonsterOnAttackedAdmission(Monster monster, uint playerEntityId, string reason)
        {
            if (monster == null || playerEntityId == 0) return;
            if (!_players.TryGetValue(playerEntityId, out var player) || player == null || !player.IsAlive) return;
            if (!monster.IsAlive || monster.DeathPendingClientConfirmation)
            {
                if (monster.TargetId != 0) return;
                monster.TargetId = playerEntityId;
                Debug.LogError($"[MON-ONATTACKED-ADMISSION] monster={monster.Name}#{monster.EntityId} player={player.Name}#{player.EntityId} source={reason ?? "unknown"} rangeGate=False smmsg=0x09 lethal=True sourceFunction=Damage::apply@0x004F6580->MonsterBehavior2::onAttacked@0x0051B550");
                PropagateMonsterShout(monster, player);
                return;
            }
            Debug.LogError($"[MON-ONATTACKED-ADMISSION] monster={monster.Name}#{monster.EntityId} player={player.Name}#{player.EntityId} source={reason ?? "unknown"} rangeGate=False smmsg=0x09 sourceFunction=Damage::apply@0x004F6580->MonsterBehavior2::onAttacked@0x0051B550");
            AggroMonster(monster, player, reason, false);
        }

        public bool IsMonsterDeathPendingClientConfirmation(Monster monster)
        {
            return monster != null && monster.DeathPendingClientConfirmation;
        }

        public void MarkMonsterDead(Monster monster, string source)
        {
            MarkMonsterDead(monster, source, true);
        }

        private void MarkMonsterDead(Monster monster, string source, bool mirrorActive)
        {
            if (monster == null) return;

            bool hadRuntimeState = monster.IsAlive
                || monster.State != MonsterState.Dead
                || monster.TargetId != 0
                || monster.AggroTriggered
                || monster.AggroSent
                || monster.AlertSourceEntityId != 0
                || monster.AttackPending
                || monster.AttackSoundPending
                || monster.AttackClientVisible
                || monster.AttackContactOnly
                || monster.CombatContactTargetId != 0;

            monster.DeathPendingClientConfirmation = false;
            monster.DeathPendingSince = 0f;
            monster.IsAlive = false;
            monster.State = MonsterState.Dead;
            monster.ClearTarget();
            monster.AggroTriggered = false;
            monster.AggroSent = false;
            monster.AttackPending = false;
            monster.AttackSoundPending = false;
            monster.AttackClientVisible = false;
            monster.AttackContactOnly = false;
            monster.AttackHitResolved = false;
            monster.AttackStartTick = 0;
            monster.AttackCommitTick = 0;
            monster.AttackSoundTick = 0;
            monster.AttackEndTick = 0;
            monster.AttackUseRaw = 0;
            monster.UsePrimaryActiveSkillThisAttack = false;
            monster.AttackCommitTargetX = 0f;
            monster.AttackCommitTargetY = 0f;
            monster.CombatContactTargetId = 0;
            monster.CombatContactUntil = 0f;
            monster.SpellKnockDownEndTime = 0f;
            monster.SpellKnockDownStrength = 0;
            monster.SpellKnockDownSourceEntityId = 0;
            monster.KnockBackActive = false;
            monster.Ai?.PostMessage(MonsterMessageId.DeathWarning, source);
            monster.Ai?.SetState(MonsterStateId.DeathWarning, "death");
            EncounterOnUnitDied(monster, source ?? "death");
            _monsterFarTargetActionLogTime.Remove(monster.EntityId);
            RemoveMonsterModifiersForTarget(monster.EntityId, source ?? "death");
            RemovePlayerModifiersFromSource(monster.EntityId, source ?? "death");
            WanderSimulator.Instance.UnregisterEntity(monster.EntityId);

            if (hadRuntimeState)
                Debug.LogError($"[MON-DEATH-STATE] cleared action/move target for {monster.Name}#{monster.EntityId} source={source ?? "unknown"}");

            if (mirrorActive && _activeMonsters.TryGetValue(monster.EntityId, out var active) && active != monster)
                MarkMonsterDead(active, source, false);
        }

        private void MarkMonsterDeathPendingClientConfirmation(Monster monster, string source)
        {
            if (monster == null) return;
            bool keepPendingAttack = monster.AttackPending
                && monster.AttackContactOnly
                && !monster.AttackHitResolved
                && monster.AttackCommitTick > 0
                && monster.TargetId != 0;
            monster.DeathPendingClientConfirmation = true;
            monster.DeathPendingSince = GetCombatTime();
            if (!keepPendingAttack)
            {
                monster.AttackPending = false;
                monster.AttackClientVisible = false;
                monster.AttackContactOnly = false;
                monster.AttackSoundPending = false;
                monster.AttackHitResolved = false;
                monster.AttackStartTick = 0;
                monster.AttackEndTick = 0;
                monster.AttackCommitTick = 0;
                monster.AttackSoundTick = 0;
                monster.CombatContactTargetId = 0;
                monster.CombatContactUntil = 0f;
                monster.TargetId = 0;
                monster.AlertSourceEntityId = 0;
                monster.AggroTriggered = false;
                if (monster.IsAlive)
                    monster.State = MonsterState.Idle;
                monster.Ai?.PostMessage(MonsterMessageId.DeathWarning, source);
                monster.Ai?.SetState(MonsterStateId.DeathWarning, "death-pending");
            }
            Debug.LogError($"[MON-DEATH-GATE] pending client death confirmation {monster.Name}#{monster.EntityId} source={source ?? "unknown"} hp={GetRuntimeMonsterHPWire(monster, "death-pending")} keepAttack={keepPendingAttack}");
        }

        public void BeginMonsterDeathLifecycle(Monster monster, string source)
        {
            if (monster == null) return;
            MarkMonsterDead(monster, source);
            if (monster.DeathLifecycleActive)
                return;

            monster.DeathLifecycleActive = true;
            monster.DeathRemoveSent = false;
            monster.DeathState = 7;
            monster.CorpseTicksRemaining = monster.CorpseLingerTicks;
            monster.FadeTicksRemaining = 0;
            _monsterDeathUpdateAccum[monster.EntityId] = 0f;
            WanderSimulator.Instance.UnregisterEntity(monster.EntityId);
            Debug.LogError($"[MON-DEATH-LIFECYCLE] state=7 corpse {monster.Name}#{monster.EntityId} ticks={monster.CorpseTicksRemaining} source={source ?? "unknown"}");
        }

        private void ProcessDeathLifecycles(float deltaTime)
        {
            if (deltaTime <= 0f || _activeMonsters.Count == 0)
                return;

            var monsterIds = new List<uint>(_activeMonsters.Keys);
            foreach (uint entityId in monsterIds)
            {
                if (!_activeMonsters.TryGetValue(entityId, out var monster))
                    continue;
                if (!monster.DeathLifecycleActive || monster.DeathRemoveSent)
                    continue;

                _monsterDeathUpdateAccum.TryGetValue(entityId, out float accum);
                accum += deltaTime;
                int ticks = Mathf.Min(256, Mathf.FloorToInt(accum / CLIENT_UNIT_TICK_INTERVAL));
                if (ticks <= 0)
                {
                    _monsterDeathUpdateAccum[entityId] = accum;
                    continue;
                }

                accum -= ticks * CLIENT_UNIT_TICK_INTERVAL;
                _monsterDeathUpdateAccum[entityId] = accum;

                for (int tickIndex = 0; tickIndex < ticks; tickIndex++)
                {
                    if (monster.DeathState == 7)
                    {
                        if (monster.CorpseTicksRemaining > 0)
                            monster.CorpseTicksRemaining--;
                        if (monster.CorpseTicksRemaining == 0)
                        {
                            monster.DeathState = 9;
                            monster.FadeTicksRemaining = CLIENT_STOCKUNIT_FADE_TICKS;
                            Debug.LogError($"[MON-DEATH-LIFECYCLE] state=9 fade {monster.Name}#{monster.EntityId} ticks={monster.FadeTicksRemaining}");
                        }
                        continue;
                    }

                    if (monster.DeathState == 9)
                    {
                        if (monster.FadeTicksRemaining > 0)
                            monster.FadeTicksRemaining--;
                        if (monster.FadeTicksRemaining == 0)
                        {
                            monster.DeathRemoveSent = true;
                            float respawnDelaySeconds = monster.RespawnRateTicks * CLIENT_UNIT_TICK_INTERVAL;
                            Debug.LogError($"[MON-DEATH-LIFECYCLE] remove {monster.Name}#{monster.EntityId} after corpse/fade autoRespawn={monster.AutoRespawn} respawnTicks={monster.RespawnRateTicks}");
                            DespawnMonster(entityId, monster.AutoRespawn, respawnDelaySeconds);
                            break;
                        }
                    }
                }
            }
        }

        public void LogMonsterClientVisibleSwingNoDamage(Monster monster, string reason)
        {
            if (monster == null || !monster.IsAlive) return;
            uint hp = GetRuntimeMonsterHPWire(monster, "SWING-NO-DAMAGE");
            Debug.LogError($"[MON-HP-TRUTH] NO-DAMAGE {monster.Name}#{monster.EntityId} hp={hp / 256f:F2}/{monster.MaxHPWire / 256f:F2} reason={reason ?? "unknown"}");
        }

        private void RecordMonsterHPReport(Monster monster, MonsterHPState state, uint hpWire)
        {
            if (monster == null || state == null) return;
            state.LastClientHPReportTime = Time.time;
            state.LastClientHPReportWire = hpWire;
            ApplyMonsterHPState(monster, state);
        }

        public bool ApplyPlayerDamageToMonsterWire(Monster monster, uint damageWire, string source, out uint oldHPWire, out uint newHPWire, out bool died, float clientDamageTime = -1f, uint clientDamageTick = 0, int damageTypeId = 0, int weaponClassId = 0, uint rawDamageWire = 0, int attackerLevel = 1, byte damageKind = 0, float clientVisibleTime = -1f, bool spellEffectResistResolved = false, bool spellEffectVulnerable = false)
        {
            oldHPWire = 0;
            newHPWire = 0;
            died = false;
            if (monster == null || !monster.IsAlive || damageWire == 0) return false;

            float damageTime = clientDamageTime >= 0f ? clientDamageTime : GetCombatTime();
            uint preAdvanceHP = PeekRuntimeMonsterHPWire(monster);
            AdvanceMonsterVitalsToTime(monster, damageTime, $"PRE-DAMAGE:{source ?? "damage"}");
            oldHPWire = PeekRuntimeMonsterHPWire(monster);
            newHPWire = oldHPWire;
            if (preAdvanceHP != oldHPWire)
                Debug.LogError($"[MON-HP-TRUTH] PRE-DAMAGE {monster.Name}#{monster.EntityId} hp={preAdvanceHP}->{oldHPWire}/{monster.MaxHPWire} clientDamageTime={damageTime:F3} source={source ?? "unknown"}");

            DamageQueryResult query = ApplyDamageQueryWire(damageWire, monster, damageTypeId, damageKind, clientDamageTime, source, attackerLevel, spellEffectResistResolved, spellEffectVulnerable);
            uint adjustedDamageWire = query.AdjustedDamageWire;
            if (adjustedDamageWire == 0)
            {
                newHPWire = oldHPWire;
                Debug.LogError($"[MONSTER-DAMAGE] source={source ?? "unknown"} target={monster.Name}#{monster.EntityId} damageWire={damageWire} adjustedWire=0 hp={oldHPWire}->{newHPWire}/{monster.MaxHPWire} died=False clientDamageTime={damageTime:F3} result={query.ResultName}");
                Debug.LogError($"[CLIENT-DAMAGE-CONTRACT] source={source ?? "unknown"} target={monster.Name}#{monster.EntityId} weaponClassId={weaponClassId} damageTypeId={damageTypeId} rawRollWire={(rawDamageWire != 0 ? rawDamageWire : damageWire)} preQueryWire={damageWire} damageTakenMod={query.DamageTakenMod:F2} typeMod={query.DamageTypeMod:F2} postQueryWire=0 hp={oldHPWire}->{newHPWire}/{monster.MaxHPWire} resist={query.ResultName} resistRaw=0x{query.ResistRaw:X8} resistChance={query.ResistChanceWire / 256f:F2} clientDamageTime={damageTime:F3} clientTick={clientDamageTick}");
                return false;
            }

            newHPWire = adjustedDamageWire >= oldHPWire ? 0u : oldHPWire - adjustedDamageWire;
            float visibleTime = clientVisibleTime >= 0f ? clientVisibleTime : damageTime;
            SetRuntimeMonsterHPWire(monster, newHPWire, true, damageTime, source ?? "damage", clientDamageTick, visibleTime);

            if (newHPWire == 0)
            {
                MarkMonsterDead(monster, source);
                died = true;
            }

            Debug.LogError($"[MONSTER-DAMAGE] source={source ?? "unknown"} target={monster.Name}#{monster.EntityId} damageWire={damageWire} adjustedWire={adjustedDamageWire} hp={oldHPWire}->{newHPWire}/{monster.MaxHPWire} died={died} clientDamageTime={damageTime:F3} result={query.ResultName}");
            Debug.LogError($"[CLIENT-DAMAGE-CONTRACT] source={source ?? "unknown"} target={monster.Name}#{monster.EntityId} weaponClassId={weaponClassId} damageTypeId={damageTypeId} rawRollWire={(rawDamageWire != 0 ? rawDamageWire : damageWire)} preQueryWire={damageWire} damageTakenMod={query.DamageTakenMod:F2} typeMod={query.DamageTypeMod:F2} postQueryWire={adjustedDamageWire} hp={oldHPWire}->{newHPWire}/{monster.MaxHPWire} resist={query.ResultName} resistRaw=0x{query.ResistRaw:X8} resistChance={query.ResistChanceWire / 256f:F2} clientDamageTime={damageTime:F3} clientTick={clientDamageTick}");
            Debug.LogError($"[MON-HP-TRUTH] COMPUTED {monster.Name}#{monster.EntityId} source={source ?? "unknown"} hp={oldHPWire / 256f:F2}->{newHPWire / 256f:F2}/{monster.MaxHPWire / 256f:F2} dmg={adjustedDamageWire / 256f:F2}");
            return true;
        }

        public bool ApplyPlayerWeaponDamageToMonsterWire(Monster monster, WeaponDamageResult damageResult, string source, out uint oldHPWire, out uint newHPWire, out bool died, float clientDamageTime = -1f, uint clientDamageTick = 0)
        {
            return ApplyPlayerWeaponDamageToMonsterWire(monster, damageResult, source, out oldHPWire, out newHPWire, out died, out _, null, null, clientDamageTime, clientDamageTick);
        }

        public bool ApplyPlayerWeaponDamageToMonsterWire(Monster monster, WeaponDamageResult damageResult, string source, out uint oldHPWire, out uint newHPWire, out bool died, out uint effectRaw, MersenneTwister effectRng, string effectActor, float clientDamageTime = -1f, uint clientDamageTick = 0)
        {
            oldHPWire = 0;
            newHPWire = monster != null ? PeekRuntimeMonsterHPWire(monster) : 0;
            died = false;
            effectRaw = 0;
            if (monster == null || damageResult == null || !damageResult.IsHit || damageResult.IsBlocked || damageResult.DamageWire == 0)
                return false;

            string baseSource = source ?? "WeaponDamage";
            string rngActor = effectActor ?? "player-weapon";
            bool applied = ApplyPlayerDamageToMonsterWire(
                monster,
                damageResult.DamageWire,
                baseSource,
                out oldHPWire,
                out newHPWire,
                out died,
                clientDamageTime,
                clientDamageTick,
                damageResult.DamageTypeId,
                damageResult.WeaponClassId,
                damageResult.DamageWire,
                damageResult.AttackerLevel,
                0);
            if (!applied)
                return false;

            effectRaw = ConsumeOnApplyDamageEffectRng(effectRng, rngActor, monster.EntityId, monster.Name, oldHPWire, newHPWire, monster.MaxHPWire, damageResult.DamageWire, baseSource);

            uint primaryAppliedWire = oldHPWire > newHPWire ? oldHPWire - newHPWire : 0;
            uint totalAppliedWire = primaryAppliedWire;
            if (damageResult.DamageAdds != null)
            {
                foreach (WeaponDamageEvent add in damageResult.DamageAdds)
                {
                    if (add == null || add.DamageWire == 0 || died || monster == null || !monster.IsAlive || PeekRuntimeMonsterHPWire(monster) == 0)
                        break;

                    string addSource = $"{baseSource}-WeaponDamageAdd-{add.Element}";

                    bool addApplied = ApplyPlayerDamageToMonsterWire(
                        monster,
                        add.DamageWire,
                        addSource,
                        out uint addOldHPWire,
                        out uint addNewHPWire,
                        out bool addDied,
                        clientDamageTime,
                        clientDamageTick,
                        add.DamageTypeId,
                        damageResult.WeaponClassId,
                        add.DamageWire,
                        damageResult.AttackerLevel,
                        3);
                    if (!addApplied)
                        continue;

                    uint addEffectRaw = ConsumeOnApplyDamageEffectRng(effectRng, rngActor, monster.EntityId, monster.Name, addOldHPWire, addNewHPWire, monster.MaxHPWire, add.DamageWire, addSource);

                    uint appliedWire = addOldHPWire > addNewHPWire ? addOldHPWire - addNewHPWire : 0;
                    totalAppliedWire = ClampWireAdd(totalAppliedWire, appliedWire);
                    newHPWire = addNewHPWire;
                    died |= addDied;
                    Debug.LogError($"[CLIENT-DAMAGE-ADD] source={baseSource} target={monster.Name}#{monster.EntityId} element={add.Element} damageTypeId={add.DamageTypeId} weaponAdd={add.WeaponAdd} bonus={add.DamageBonus} mod={add.DamageMod} weaponDamage={DamageResolver.ToFloat(add.WeaponDamageF32):F4} preQueryWire={add.DamageWire} hp={addOldHPWire}->{addNewHPWire}/{monster.MaxHPWire} died={addDied} effectRaw=0x{addEffectRaw:X8} clientDamageTime={clientDamageTime:F3} clientTick={clientDamageTick}");
                }
            }

            if (damageResult.AttackerState != null && primaryAppliedWire > 0)
                damageResult.AttackerState.ApplyOnDamageCallback(primaryAppliedWire, clientDamageTime, baseSource);

            Debug.LogError($"[CLIENT-WEAPON-DAMAGE-REPLAY] source={baseSource} target={monster.Name}#{monster.EntityId} primaryWire={damageResult.DamageWire} addCount={(damageResult.DamageAdds != null ? damageResult.DamageAdds.Count : 0)} totalRawWire={damageResult.TotalDamageWire} totalAppliedWire={totalAppliedWire} hp={oldHPWire}->{newHPWire}/{monster.MaxHPWire} result={damageResult.ResultName} died={died} effectRaw=0x{effectRaw:X8}");
            return true;
        }

        private bool ApplyMonsterOnDamageCallback(Monster monster, uint appliedDamageWire, float clientNow, string source)
        {
            if (monster == null || appliedDamageWire == 0 || monster.Slots == null) return false;
            int hpSteal = monster.Slots.Get(UnitSlot.HitPointSteal);
            int manaSteal = monster.Slots.Get(UnitSlot.ManaPointSteal);
            if (hpSteal <= 0 && manaSteal <= 0) return false;

            uint oldHP = PeekRuntimeMonsterHPWire(monster);
            uint oldMana = monster.CurrentManaWire;
            uint nextHP = oldHP;
            uint nextMana = oldMana;

            if (hpSteal > 0 && oldHP > 0 && monster.MaxHPWire > 0)
            {
                ulong heal = ((ulong)appliedDamageWire * (uint)hpSteal) / 100UL;
                if (heal > 0)
                {
                    ulong hp = (ulong)oldHP + heal;
                    nextHP = hp >= monster.MaxHPWire ? monster.MaxHPWire : (uint)hp;
                    if (nextHP != oldHP)
                        SetRuntimeMonsterHPWire(monster, nextHP, false, clientNow, $"{source ?? "Damage::apply"}:Unit::onDamageCallback");
                }
            }

            if (manaSteal > 0 && monster.MaxManaWire > 0)
            {
                ulong restore = ((ulong)appliedDamageWire * (uint)manaSteal) / 100UL;
                if (restore > 0)
                {
                    ulong mana = (ulong)oldMana + restore;
                    nextMana = mana >= monster.MaxManaWire ? monster.MaxManaWire : (uint)mana;
                    monster.CurrentManaWire = nextMana;
                }
            }

            bool changed = oldHP != nextHP || oldMana != nextMana;
            if (changed)
                Debug.LogError($"[DAMAGE-CALLBACK] source=monster monster={monster.Name}#{monster.EntityId} hpSteal={hpSteal} manaSteal={manaSteal} appliedWire={appliedDamageWire} hp={oldHP}->{nextHP}/{monster.MaxHPWire} mana={oldMana}->{nextMana}/{monster.MaxManaWire} sourceFunction=Unit::onDamageCallback@0x0050C470");
            return changed;
        }

        private static uint ClampWireAdd(uint left, uint right)
        {
            ulong sum = (ulong)left + right;
            return sum >= uint.MaxValue ? uint.MaxValue : (uint)sum;
        }

        public bool ObserveClientMonsterHP(Monster monster, uint clientHPWire, string source)
        {
            if (monster == null) return false;
            const uint toleranceWire = 5u * 256u;
            if (clientHPWire > monster.MaxHPWire + toleranceWire)
            {
                Debug.LogError($"[{source}] Monster HP rejected: {monster.Name}#{monster.EntityId} client={clientHPWire / 256f:F2} max={monster.MaxHPWire / 256f:F2}");
                return false;
            }

            var state = GetMonsterHPState(monster);
            if (state == null) return false;
            uint observedHPWire = clientHPWire > monster.MaxHPWire ? monster.MaxHPWire : clientHPWire;
            if (EntitySynchInfoAuthority.Instance.TryResolveMonsterOwner(monster, out EntitySynchInfoOwnerRef owner))
                EntitySynchInfoAuthority.Instance.ObserveClientHpReport(owner, observedHPWire, EntitySynchInfoAuthority.ClassifyReportSource(source), source ?? "client-monster-hp", true, out _);
            RecordMonsterHPReport(monster, state, observedHPWire);
            CommitClientReportedMonsterHP(monster, state, observedHPWire, source);
            Debug.LogError($"[{source}] Monster HP client-authoritative: {monster.Name}#{monster.EntityId} client={observedHPWire / 256f:F2} runtime={state.RuntimeHPWire / 256f:F2}/{monster.MaxHPWire / 256f:F2} wire={observedHPWire}");
            return true;
        }

        public bool CanSendMonsterEntitySynchInfoHP(Monster monster, string packetName)
        {
            if (monster == null) return false;
            return TryResolveMonsterEntitySynchInfoHP(monster, packetName, out _);
        }

        private bool AggroMonster(Monster monster, CombatPlayer player, string reason, bool alignForCombat)
        {
            if (monster == null || player == null || !monster.IsAlive || monster.DeathPendingClientConfirmation) return false;
            bool firstAggro = !monster.AggroTriggered || monster.TargetId != player.EntityId;
            monster.AggroTriggered = true;
            monster.TargetId = player.EntityId;
            monster.AlertSourceEntityId = 0;
            monster.State = MonsterState.Combat;
            monster.Ai?.PostMessage(MonsterMessageId.TargetChanged, reason);
            monster.Ai?.SetState(MonsterStateId.Attack, "aggro-target");
            EncounterMarkActive(monster, "aggro-target");
            if (firstAggro)
            {
                WanderSimulator.Instance.UnregisterEntity(monster.EntityId);
                monster.AttackPending = false;
                monster.AttackSoundPending = false;
                Debug.LogError($"[SERVER-AGGRO] {monster.Name} -> {player.Name} reason={reason}");
                OnMonsterAggro?.Invoke(monster, player);
                PropagateMonsterShout(monster, player);
            }
            TraceMonsterState(monster, "aggro", player, Distance2D(monster.PosX, monster.PosY, player.PosX, player.PosY), ResolveMonsterEffectiveAttackRange(monster), reason);
            return firstAggro;
        }

        private void PropagateMonsterShout(Monster source, CombatPlayer player)
        {
            if (source == null || player == null || source.ShoutRange <= 0f) return;
            int shoutFixed = Mathf.RoundToInt(source.ShoutRange * 256f);
            long shoutSq = ((long)shoutFixed * shoutFixed) >> 8;
            string pathMapKey = ResolveMonsterPathMapKey(source);
            PathMap pathMap = !string.IsNullOrWhiteSpace(pathMapKey) ? PathMapCatalog.Instance.GetPathMap(pathMapKey) : null;
            foreach (var monster in _activeMonsters.Values)
            {
                if (monster == source || !monster.IsAlive || monster.AggroTriggered || monster.TargetId != 0) continue;
                if (!string.Equals(ResolveMonsterPathMapKey(monster), pathMapKey, StringComparison.OrdinalIgnoreCase)) continue;

                int dx = monster.PosFixedX - source.PosFixedX;
                int dy = monster.PosFixedY - source.PosFixedY;
                long distSq = (((long)dx * dx) >> 8) + (((long)dy * dy) >> 8);
                if (distSq > shoutSq) continue;
                if (pathMap != null
                    && pathMap.TryCanReachPoint(source.PosX, source.PosY, monster.PosX, monster.PosY, out bool canAssistReach)
                    && !canAssistReach) continue;

                if (!HasAlertEncounterRelation(monster, source, out string relation))
                {
                    Debug.LogError($"[SERVER-SHOUT] source={source.EntityId} target={monster.EntityId} shout={source.ShoutRange:F1} dist={Mathf.Sqrt(distSq):F1} action=ignored relation={relation} sourceGroup={source.EncounterGroupKey ?? ""} targetGroup={monster.EncounterGroupKey ?? ""}");
                    continue;
                }

                monster.AlertSourceEntityId = source.EntityId;
                monster.Ai?.PostMessage(MonsterMessageId.AlertChanged, "shout-alert");
                Debug.LogError($"[SERVER-SHOUT] source={source.EntityId} target={monster.EntityId} shout={source.ShoutRange:F1} dist={Mathf.Sqrt(distSq):F1} action=alert-source relation={relation} sourceTarget={source.TargetId} sourceGroup={source.EncounterGroupKey ?? ""} targetGroup={monster.EncounterGroupKey ?? ""}");
                TraceMonsterState(monster, "alert", player, Mathf.Sqrt(distSq), ResolveMonsterEffectiveAttackRange(monster), $"source={source.EntityId} relation={relation}");
            }
        }

        private void ProcessMonsterAssistAlerts(Monster onlyMonster = null)
        {
            foreach (var monster in SelectMonsters(onlyMonster))
                TryAssistFromAlertSource(monster, "assist-update");
        }

        private bool TryAssistFromAlertSource(Monster monster, string source)
        {
            if (monster == null || !monster.IsAlive || monster.DeathPendingClientConfirmation)
                return false;
            if (monster.AlertSourceEntityId == 0 || monster.AggroTriggered || monster.TargetId != 0)
                return false;
            if (!_activeMonsters.TryGetValue(monster.AlertSourceEntityId, out var alertSource) || alertSource == null)
            {
                Debug.LogError($"[SERVER-ASSIST] target={monster.Name}#{monster.EntityId} source={monster.AlertSourceEntityId} action=clear reason=missing-alert-source");
                monster.AlertSourceEntityId = 0;
                return false;
            }
            if (!HasAlertEncounterRelation(monster, alertSource, out string relation))
            {
                Debug.LogError($"[SERVER-ASSIST] source={alertSource.EntityId} target={monster.EntityId} action=clear relation={relation} sourceGroup={alertSource.EncounterGroupKey ?? ""} targetGroup={monster.EncounterGroupKey ?? ""}");
                monster.AlertSourceEntityId = 0;
                return false;
            }
            if (alertSource.TargetId == 0 || !_players.TryGetValue(alertSource.TargetId, out var target) || target == null || !target.IsAlive || target.PlayerState == null)
            {
                Debug.LogError($"[SERVER-ASSIST] source={alertSource.EntityId} target={monster.EntityId} action=clear relation={relation} reason=no-source-target sourceTarget={alertSource.TargetId}");
                monster.AlertSourceEntityId = 0;
                return false;
            }
            if (target.PlayerState.IsZoneSpawnDamageImmune || (target.PlayerState.CurrentHPWire == 0 && target.PlayerState.EntitySynchInfoHP == 0))
                return false;

            monster.AggroTriggered = true;
            monster.TargetId = target.EntityId;
            monster.State = MonsterState.Combat;
            monster.Ai?.SetState(MonsterStateId.Assist, source);
            monster.Ai?.PostMessage(MonsterMessageId.AlertChanged, source);
            EncounterLogAssistNotActive(monster, source ?? "assist");
            WanderSimulator.Instance.UnregisterEntity(monster.EntityId);
            monster.AttackPending = false;
            monster.AttackSoundPending = false;
            OnMonsterAggro?.Invoke(monster, target);
            Debug.LogError($"[SERVER-ASSIST] source={alertSource.Name}#{alertSource.EntityId} target={monster.Name}#{monster.EntityId} copiedTarget={target.Name}#{target.EntityId} relation={relation} source={source ?? "unknown"}");
            TraceMonsterState(monster, "assist", target, Distance2D(monster.PosX, monster.PosY, target.PosX, target.PosY), ResolveMonsterEffectiveAttackRange(monster), $"source={alertSource.EntityId} relation={relation}");
            return true;
        }

        private bool PlayerHasIncomingAttacker(uint playerEntityId, uint exceptEntityId = 0)
        {
            foreach (var monster in _activeMonsters.Values)
            {
                if (monster == null || !monster.IsAlive || !monster.AggroTriggered) continue;
                if (exceptEntityId != 0 && monster.EntityId == exceptEntityId) continue;
                if (monster.TargetId == playerEntityId) return true;
            }
            return false;
        }

        public IEnumerable<Monster> GetAllMonsters()
        {
            return _activeMonsters.Values;
        }
        public void UnregisterPlayer(uint entityId)
        {
            _players.Remove(entityId);
            UnregisterEntityOrder(entityId);
            _playerCombatAdvanceTime.Remove(entityId);
            int clearedMonsters = 0;
            foreach (var monster in _activeMonsters.Values)
            {
                if (monster == null) continue;
                if (ClearMonsterTargetStateForLostPlayer(monster, entityId, "UnregisterPlayer"))
                    clearedMonsters++;
            }
            Debug.LogError($"[COMBAT-LIFECYCLE] unregistered player {entityId} and cleared monster targeting state count={clearedMonsters}");
        }

        private bool ClearMonsterTargetStateForLostPlayer(Monster monster, uint lostPlayerEntityId, string source)
        {
            if (monster == null || !monster.IsAlive || monster.State == MonsterState.Dead || monster.DeathPendingClientConfirmation)
                return false;

            bool touchesLostPlayer = lostPlayerEntityId != 0 &&
                (monster.TargetId == lostPlayerEntityId ||
                 monster.CombatContactTargetId == lostPlayerEntityId);
            bool hasRuntimeTargetState =
                monster.TargetId != 0 ||
                monster.CombatContactTargetId != 0 ||
                monster.AlertSourceEntityId != 0 ||
                monster.AggroTriggered ||
                monster.AggroSent ||
                monster.AttackPending ||
                monster.AttackSoundPending ||
                monster.AttackClientVisible ||
                monster.AttackContactOnly ||
                monster.AttackHitResolved ||
                monster.UsePrimaryActiveSkillThisAttack ||
                monster.State == MonsterState.Chase ||
                monster.State == MonsterState.Combat ||
                monster.State == MonsterState.Attacking;
            bool targetMissing = monster.TargetId == 0 || !_players.ContainsKey(monster.TargetId);
            if (!hasRuntimeTargetState || (!touchesLostPlayer && !targetMissing))
                return false;

            MonsterState oldState = monster.State;
            uint oldTarget = monster.TargetId;
            uint oldContact = monster.CombatContactTargetId;
            bool oldAggro = monster.AggroTriggered;
            bool oldPending = monster.AttackPending;

            monster.ClearTarget();
            monster.AggroTriggered = false;
            monster.AggroSent = false;
            monster.AttackPending = false;
            monster.AttackSoundPending = false;
            monster.AttackClientVisible = false;
            monster.AttackContactOnly = false;
            monster.AttackHitResolved = false;
            monster.UsePrimaryActiveSkillThisAttack = false;
            monster.AttackStartTick = 0;
            monster.AttackCommitTick = 0;
            monster.AttackSoundTick = 0;
            monster.AttackEndTick = 0;
            monster.AttackUseRaw = 0;
            monster.AttackSoundRaw = 0;
            monster.AttackSoundGateRaw = 0;
            monster.AttackSoundRepeatRaw = 0;
            monster.AttackCommitTargetX = 0f;
            monster.AttackCommitTargetY = 0f;
            monster.CombatContactTargetId = 0;
            monster.CombatContactUntil = 0f;
            monster.ChaseHeadingInit = false;
            monster.ClientVisibleHeadingInit = false;
            if (monster.IsAlive)
                monster.State = MonsterState.Idle;
            _monsterFarTargetActionLogTime.Remove(monster.EntityId);

            Debug.LogError($"[MON-TARGET-CLEAR] monster={monster.Name}#{monster.EntityId} lostPlayer={lostPlayerEntityId} oldState={oldState} oldTarget={oldTarget} oldContact={oldContact} oldAggro={oldAggro} oldPending={oldPending} hp={GetRuntimeMonsterHPWire(monster, "target-clear")}/{monster.MaxHPWire} pos=({monster.PosX:F1},{monster.PosY:F1},{monster.PosZ:F1}) source={source ?? "unknown"} sourceFunction=MonsterBehavior2::ClearTargets/UpdateTargets");
            TraceMonsterState(monster, "target-clear", null, -1f, ResolveMonsterEffectiveAttackRange(monster), source);
            return true;
        }
        private RoomRuntime CurrentRoomRuntime => GetRoomRuntime(_currentRoomRuntimeKey);

        public MersenneTwister RoomRng => CurrentRoomRuntime.RoomRng;

        public uint RoomSeed => CurrentRoomRuntime.Seed;

        public bool IsRoomRngReady => CurrentRoomRuntime.Initialized;

        public int RoomRngCallsSinceReseed => CurrentRoomRuntime.RngCallsSinceReseed;

        public MersenneTwister RoomRandom => CurrentRoomRuntime.RoomRng;
        public uint RandomSeed => CurrentRoomRuntime.Seed;

        public string CurrentRoomRuntimeKey => _currentRoomRuntimeKey;

        public RoomRuntime GetRoomRuntime(string instanceKey)
        {
            string key = RoomRuntime.NormalizeInstanceKey(instanceKey);
            if (!_roomRuntimes.TryGetValue(key, out var runtime))
            {
                runtime = new RoomRuntime(key);
                _roomRuntimes[key] = runtime;
            }
            return runtime;
        }

        public bool TryGetRoomRuntime(string instanceKey, out RoomRuntime runtime)
        {
            string key = RoomRuntime.NormalizeInstanceKey(instanceKey);
            return _roomRuntimes.TryGetValue(key, out runtime);
        }

        public bool TryGetInitializedRoomRuntime(string instanceKey, out RoomRuntime runtime)
        {
            runtime = null;
            if (string.IsNullOrWhiteSpace(instanceKey))
            {
                RuntimeEvidence.LogFallbackHit("rng-instance", "missing-instance-key", "source=TryGetInitializedRoomRuntime", 64);
                Debug.LogError("[RNG-INSTANCE] source=TryGetInitializedRoomRuntime reason=missing-instance-key rng=null");
                return false;
            }

            string key = RoomRuntime.NormalizeInstanceKey(instanceKey);
            if (string.Equals(key, RoomRuntime.DefaultInstanceKey, StringComparison.OrdinalIgnoreCase))
            {
                RuntimeEvidence.LogFallbackHit("rng-instance", "default-instance-key", $"source=TryGetInitializedRoomRuntime instance='{key}'", 64);
                Debug.LogError($"[RNG-INSTANCE] source=TryGetInitializedRoomRuntime reason=default-instance-key instance='{key}' rng=null");
                return false;
            }

            if (!_roomRuntimes.TryGetValue(key, out runtime) || runtime == null || !runtime.Initialized)
            {
                RuntimeEvidence.LogFallbackHit("rng-instance", "uninitialized-instance", $"source=TryGetInitializedRoomRuntime instance='{key}'", 64);
                Debug.LogError($"[RNG-INSTANCE] source=TryGetInitializedRoomRuntime reason=uninitialized-instance instance='{key}' rng=null");
                runtime = null;
                return false;
            }

            return true;
        }

        public RoomRuntime RequireInitializedRoomRuntime(string instanceKey, string source)
        {
            if (TryGetInitializedRoomRuntime(instanceKey, out var runtime))
                return runtime;

            Debug.LogError($"[RNG-INSTANCE] source={source ?? "unknown"} reason=required-runtime-unavailable instance='{instanceKey ?? "<null>"}'");
            return null;
        }

        public void SetCurrentRoomRuntime(string instanceKey, string source = null)
        {
            _currentRoomRuntimeKey = RoomRuntime.NormalizeInstanceKey(instanceKey);
            GetRoomRuntime(_currentRoomRuntimeKey);
            Debug.LogError($"[ROOM-RUNTIME] current='{_currentRoomRuntimeKey}' source={source ?? "unknown"}");
        }

        private string ResolveMonsterRuntimeKey(Monster monster)
        {
            if (!string.IsNullOrWhiteSpace(monster?.InstanceKey))
                return monster.InstanceKey;
            RuntimeEvidence.LogFallbackHit(
                "rng-instance",
                "monster-missing-instance",
                $"monster={monster?.Name ?? "<null>"}#{monster?.EntityId ?? 0}",
                64);
            return null;
        }

        public RoomRuntime GetRoomRuntimeForMonster(Monster monster)
        {
            return RequireInitializedRoomRuntime(ResolveMonsterRuntimeKey(monster), "monster-runtime");
        }

        public CombatContext GetCombatContextForMonster(Monster monster, string source = null)
        {
            RoomRuntime runtime = GetRoomRuntimeForMonster(monster);
            return runtime != null ? runtime.CreateContext(source) : new CombatContext(null, source);
        }

        public MersenneTwister GetRoomRngForMonster(Monster monster)
        {
            return GetRoomRuntimeForMonster(monster)?.RoomRng;
        }

        public MersenneTwister GetRoomRngForInstance(string instanceKey)
        {
            return RequireInitializedRoomRuntime(instanceKey, "instance-rng")?.RoomRng;
        }

        public string GetPlayerInstanceKey(uint playerEntityId)
        {
            return _players.TryGetValue(playerEntityId, out var player) && !string.IsNullOrWhiteSpace(player?.InstanceKey)
                ? player.InstanceKey
                : null;
        }

        public MersenneTwister GetRoomRngForPlayerEntity(uint playerEntityId)
        {
            string instanceKey = GetPlayerInstanceKey(playerEntityId);
            if (string.IsNullOrWhiteSpace(instanceKey) || instanceKey == RoomRuntime.DefaultInstanceKey)
            {
                RuntimeEvidence.LogFallbackHit(
                    "rng-instance",
                    "player-missing-instance",
                    $"player={playerEntityId}",
                    64);
                Debug.LogError($"[RNG-INSTANCE] source=GetRoomRngForPlayerEntity reason=player-missing-instance player={playerEntityId} rng=null");
                return null;
            }
            return GetRoomRngForInstance(instanceKey);
        }

        public uint GetRoomSeedForInstance(string instanceKey)
        {
            return TryGetInitializedRoomRuntime(instanceKey, out var runtime) ? runtime.Seed : 0u;
        }

        public int GetRoomRngPosForInstance(string instanceKey)
        {
            return TryGetInitializedRoomRuntime(instanceKey, out var runtime) ? runtime.RngCallsSinceReseed : -1;
        }

        public void InitializeRoomRng(uint seed)
        {
            InitializeRoomRng(_currentRoomRuntimeKey, seed, "legacy-current");
        }

        public void InitializeRoomRng(string instanceKey, uint seed, string source = null)
        {
            string key = RoomRuntime.NormalizeInstanceKey(instanceKey);
            _currentRoomRuntimeKey = key;
            RoomRuntime runtime = GetRoomRuntime(key);
            runtime.Initialize(seed, source ?? "InitializeRoomRng");
            Debug.LogError($"[RNG-SEED] room initialize instance='{key}' seed=0x{seed:X8} rngPos=0 monsters={_activeMonsters.Count(m => string.Equals(ResolveMonsterRuntimeKey(m.Value), key, StringComparison.OrdinalIgnoreCase))} players={_players.Count}");
        }

        public void EnsureRoomRng(string instanceKey, uint seed, string source = null)
        {
            string key = RoomRuntime.NormalizeInstanceKey(instanceKey);
            _currentRoomRuntimeKey = key;
            RoomRuntime runtime = GetRoomRuntime(key);
            bool initialized = runtime.EnsureInitialized(seed, source ?? "EnsureRoomRng");
            Debug.LogError($"[RNG-SEED] ensure instance='{key}' seed=0x{seed:X8} initialized={initialized} current=0x{runtime.Seed:X8} rngPos={runtime.RngCallsSinceReseed} source={source ?? "unknown"}");
        }

        public void AdvanceRoomRng(int count, string source)
        {
            CurrentRoomRuntime.Advance(count, source);
        }

        public void AdvanceRoomRng(string instanceKey, int count, string source)
        {
            GetRoomRuntime(instanceKey).Advance(count, source);
        }

        public void ReseedRoomRng(uint seed)
        {
            ReseedRoomRng(_currentRoomRuntimeKey, seed, "legacy-current");
        }

        public void ReseedRoomRng(string instanceKey, uint seed, string source = null)
        {
            string key = RoomRuntime.NormalizeInstanceKey(instanceKey);
            _currentRoomRuntimeKey = key;
            GetRoomRuntime(key).Reseed(seed, source ?? "ReseedRoomRng");
        }
        public void InitializeRandomSeed(uint seed)
        {
            RoomRuntime runtime = CurrentRoomRuntime;
            if (runtime.Initialized)
            {
                if (runtime.Seed != seed)
                    Debug.LogError($"[ROOM-RNG] Ignored legacy reseed request instance='{runtime.InstanceKey}' seed=0x{seed:X8} current=0x{runtime.Seed:X8} rngPos={runtime.RngCallsSinceReseed}");
                return;
            }
            InitializeRoomRng(seed);
        }
        public event Action<Monster, CombatPlayer> OnMonsterAggro;
        public CombatPlayer GetPlayer(uint entityId)
        {
            return _players.TryGetValue(entityId, out var p) ? p : null;
        }

        public void SetPlayerActiveClientAttack(uint entityId, bool active, uint targetId = 0)
        {
            if (_players.TryGetValue(entityId, out var player) && player != null)
            {
                player.HasActiveClientAttack = active;
                player.ActiveClientAttackTargetId = active ? targetId : 0;
            }
        }

        public void FlushPlayerCombatBeforeSynch(uint playerEntityId, float deltaTime, string source = null, float clientNowOverride = -1f)
        {
            if (playerEntityId == 0) return;
            float clientNow = clientNowOverride >= 0f ? clientNowOverride : GetCombatTime();
            float clientDelta = 0f;
            float elapsed = 0f;
            int dueTicks = 0;
            int consumedTicks = 0;
            float previousAdvanceTime = clientNow;
            float nextAdvanceTime = clientNow;
            if (deltaTime > 0f)
            {
                clientDelta = ResolvePlayerCombatAdvanceDelta(playerEntityId, deltaTime, out elapsed, out dueTicks, out consumedTicks, out previousAdvanceTime, out nextAdvanceTime);
            }
            else if (!_playerCombatAdvanceTime.ContainsKey(playerEntityId))
            {
                _playerCombatAdvanceTime[playerEntityId] = clientNow;
            }
            TracePlayerPreSuffixCombatAdvance(playerEntityId, source ?? "FlushPlayerCombatBeforeSynch", deltaTime, clientDelta, elapsed, dueTicks, consumedTicks, previousAdvanceTime, nextAdvanceTime, clientNow);
            AdvancePlayerDamageModifierRuntime(playerEntityId, clientNow, source ?? "FlushPlayerCombatBeforeSynch");
            AdvanceMonsterModifierRuntime(null, clientNow, source ?? "FlushPlayerCombatBeforeSynch");
            ProcessMonsterAttacks(0f, playerEntityId, false, null, clientNow);
        }

        private float ResolvePlayerCombatAdvanceDelta(uint playerEntityId, float deltaTime, out float elapsed, out int dueTicks, out int consumedTicks, out float previousAdvanceTime, out float nextAdvanceTime)
        {
            float now = GetCombatTime();
            elapsed = 0f;
            dueTicks = 0;
            consumedTicks = 0;
            previousAdvanceTime = now;
            nextAdvanceTime = now;

            if (deltaTime > 0f)
            {
                elapsed = deltaTime;
                dueTicks = Mathf.Max(1, Mathf.CeilToInt(deltaTime / CLIENT_UNIT_TICK_INTERVAL));
                consumedTicks = dueTicks;
                _playerCombatAdvanceTime[playerEntityId] = now;
                return deltaTime;
            }

            if (!_playerCombatAdvanceTime.TryGetValue(playerEntityId, out previousAdvanceTime) || previousAdvanceTime <= 0f || previousAdvanceTime > now)
            {
                previousAdvanceTime = now;
                _playerCombatAdvanceTime[playerEntityId] = previousAdvanceTime;
                nextAdvanceTime = previousAdvanceTime;
                return 0f;
            }

            elapsed = Mathf.Max(0f, now - previousAdvanceTime);
            dueTicks = Mathf.FloorToInt((elapsed + 0.0001f) / CLIENT_UNIT_TICK_INTERVAL);
            if (dueTicks <= 0)
            {
                nextAdvanceTime = previousAdvanceTime;
                return 0f;
            }

            consumedTicks = dueTicks;
            float clientDelta = consumedTicks * CLIENT_UNIT_TICK_INTERVAL;
            nextAdvanceTime = previousAdvanceTime + clientDelta;
            if (nextAdvanceTime > now)
                nextAdvanceTime = now;
            _playerCombatAdvanceTime[playerEntityId] = nextAdvanceTime;
            return clientDelta;
        }

        private void TracePlayerPreSuffixCombatAdvance(uint playerEntityId, string source, float requestedDelta, float clientDelta, float elapsed, int dueTicks, int consumedTicks, float previousAdvanceTime, float nextAdvanceTime, float clientNow)
        {
            if (!_players.TryGetValue(playerEntityId, out var player) || player == null)
                return;

            foreach (var monster in _activeMonsters.Values)
            {
                if (monster == null || !monster.AggroTriggered || !monster.IsAlive)
                    continue;
                bool targetsPlayer = monster.TargetId == playerEntityId;
                bool contactsPlayer = monster.CombatContactTargetId == playerEntityId && clientNow <= monster.CombatContactUntil;
                if (!targetsPlayer && !contactsPlayer)
                    continue;

                float dist = Distance2D(monster.PosX, monster.PosY, player.PosX, player.PosY);
                float allowedRange = ResolveMonsterEffectiveAttackRange(monster);
                string action = clientDelta > 0f ? "advance" : "due-drain";
                Debug.LogError($"[PRE-SUFFIX-DUE-DRAIN] action={action} source={source ?? "unknown"} player={player.Name}#{player.EntityId} monster={monster.Name}#{monster.EntityId} behavior={monster.BehaviorId} unit={monster.UnitId} gc='{monster.GCType}' spawnGc='{monster.SpawnGCType}' state={monster.State} target={monster.TargetId} pending={monster.AttackPending} hitResolved={monster.AttackHitResolved} clientVisible={monster.AttackClientVisible} clientContact={monster.AttackContactOnly} contactTarget={monster.CombatContactTargetId} dist={dist:F1} range={allowedRange:F1} requestedDelta={requestedDelta:F3} clientDelta={clientDelta:F3} elapsed={elapsed:F3} dueTicks={dueTicks} consumedTicks={consumedTicks} clock={previousAdvanceTime:F3}->{nextAdvanceTime:F3} clientNow={clientNow:F3} nextAttackTick={monster.NextAttackTick} commitTick={monster.AttackCommitTick} endTick={monster.AttackEndTick} pathGate=client");
            }
        }

        public IEnumerable<CombatPlayer> GetAllPlayers()
        {
            return _players.Values;
        }
        private static Dictionary<string, string> _zoneBehaviors;

        private void LoadZoneBehaviors()
        {
            _zoneBehaviors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var connection = DungeonRunners.Database.GameDatabase.GetConnection())
                using (var reader = DungeonRunners.Database.GameDatabase.ExecuteReader(connection,
                    "SELECT zone_name, behavior_mode FROM zone_behaviors WHERE enabled = 1"))
                {
                    while (reader.Read())
                    {
                        string zone = reader.GetString(0);
                        string mode = reader.GetString(1);
                        _zoneBehaviors[zone] = mode;
                    }
                }
                Debug.LogError($"[COMBAT-RUNTIME] zoneBehaviors={_zoneBehaviors.Count} source=sqlite");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[COMBAT-RUNTIME] zoneBehaviors=0 source=sqlite state=loadFailed message='{ex.Message}'");
            }
        }

        public string GetBehaviorMode(string zoneName)
        {
            return "dungeon_specific";
        }

        private string GetBehaviorForZone(string zoneName)
        {
            string mode = GetBehaviorMode(zoneName);
            if (mode == "guard")
            {
                Debug.LogError($"[BEHAVIOR] Zone '{zoneName}' -> GUARD -> using world.dungeon09.mob.base.oneoff_behavior");
                return "world.dungeon09.mob.base.oneoff_behavior";
            }
            Debug.LogError($"[BEHAVIOR] Zone '{zoneName}' -> '{mode}' -> using authored creature behavior");
            return null;
        }

        private string ResolveSpawnBehaviourType(string zoneBehaviourType, string spawnGcType, string baseGcType)
        {
            if (!string.IsNullOrEmpty(zoneBehaviourType))
                return zoneBehaviourType;
            return ResolveAuthoredChildPath(spawnGcType, "Behavior") ?? ResolveAuthoredChildPath(baseGcType, "Behavior");
        }

        public Monster SpawnMonster(string gcType, float posX, float posY, float posZ, float heading = 0f, string zoneName = null, string encounterGroupKey = null, float encounterDifficulty = 1f, string spawnGcTypeOverride = null, string instanceKey = null)
        {
            var creatureData = DatabaseLoader.FindCreature(gcType);
            if (creatureData == null)
            {
                Debug.LogError($"[COMBAT] creature='{gcType}' state=missing");
                return null;
            }

            string spawnGcType = !string.IsNullOrEmpty(spawnGcTypeOverride)
                ? spawnGcTypeOverride
                : ResolveDungeonCreaturePath(zoneName, creatureData.gcType);
            string zoneBehaviourType = GetBehaviorForZone(zoneName);
            GCNode authoredCreature = ResolveAuthoredCreatureNode(spawnGcType, creatureData.gcType);
            string spawnBehaviourType = ResolveSpawnBehaviourType(zoneBehaviourType, spawnGcType, creatureData.gcType);
            GCNode authoredDesc = authoredCreature?.GetChild("Description") ?? authoredCreature;
            GCNode authoredWeapon = GetAuthoredWeaponDescription(authoredCreature, spawnGcType, creatureData.gcType);
            GCNode authoredBehavior = ResolveAuthoredBehaviorNode(spawnBehaviourType);
            var spawnManipulators = BuildSpawnManipulators(spawnGcType, creatureData.gcType, authoredCreature, creatureData.manipulators);
            string creatureDifficulty = GetAuthoredString(authoredDesc, "CreatureDifficulty", creatureData.creatureDifficulty);
            float unitDifficulty = GetAuthoredFloat(authoredDesc, "Difficulty", MonsterHealthTable.GetDifficultyModifier(creatureDifficulty));
            float maxHealth = GetAuthoredFloat(authoredDesc, "MaxHealth", creatureData.maxHealth);
            float perceptionRange = GetAuthoredBehaviorFloat(authoredBehavior, authoredCreature, "Perception", CLIENT_DEFAULT_UNIT_PERCEPTION);
            float aggroFallback = CLIENT_DEFAULT_MONSTER_AGGRO_RANGE;
            float attackCooldown = GetAuthoredFloat(authoredWeapon, "CoolDown", 1.75f);
            bool weaponUsesProjectile = GetAuthoredBool(authoredWeapon, "UseProjectile", false);
            float weaponProjectileSpeed = Mathf.Max(0f, GetAuthoredFloat(authoredWeapon, "ProjectileSpeed", 0f));
            float weaponProjectileSize = Mathf.Max(0f, GetAuthoredFloat(authoredWeapon, "ProjectileSize", 0f));
            float weaponDescRange = Mathf.Max(0f, GetAuthoredFloat(authoredWeapon, "Range", 0f));
            AttackTiming attackTiming = ResolveAttackTiming(authoredDesc, weaponUsesProjectile, attackCooldown, spawnGcType ?? creatureData.gcType);
            string attackType = GetAuthoredBehaviorString(authoredBehavior, authoredCreature, "AttackType", "0");
            string idleAction = GetAuthoredBehaviorString(authoredBehavior, authoredCreature, "IdleAction", "3");
            string logicType = GetAuthoredBehaviorString(authoredBehavior, authoredCreature, "LogicType", "0");
            string attackStyle = GetAuthoredBehaviorString(authoredBehavior, authoredCreature, "AttackStyle", "0");
            bool retreatable = GetAuthoredBehaviorBool(authoredBehavior, authoredCreature, "Retreatable", CLIENT_DEFAULT_MONSTER_RETREATABLE);
            bool leashed = GetAuthoredBehaviorBool(authoredBehavior, authoredCreature, "Leashed", CLIENT_DEFAULT_MONSTER_LEASHED);
            bool useIdleTime = GetAuthoredBehaviorBool(authoredBehavior, authoredCreature, "UseIdleTime", CLIENT_DEFAULT_MONSTER_USE_IDLE_TIME);
            bool autoScan = GetAuthoredBehaviorBool(authoredBehavior, authoredCreature, "AutoScan", CLIENT_DEFAULT_UNIT_AUTO_SCAN);
            bool avoidUnits = GetAuthoredBehaviorBool(authoredBehavior, authoredCreature, "AvoidUnits", CLIENT_DEFAULT_UNIT_AVOID_UNITS);
            bool turnBeforeMoving = GetAuthoredBehaviorBool(authoredBehavior, authoredCreature, "TurnBeforeMoving", CLIENT_DEFAULT_UNIT_TURN_BEFORE_MOVING);
            bool playerControlled = GetAuthoredBehaviorBool(authoredBehavior, authoredCreature, "PlayerControlled", CLIENT_DEFAULT_UNIT_PLAYER_CONTROLLED);
            int collisionBand = GetAuthoredBehaviorInt(authoredBehavior, authoredCreature, "CollisionBand", CLIENT_DEFAULT_UNIT_COLLISION_BAND);
            int collisionPriority = GetAuthoredBehaviorInt(authoredBehavior, authoredCreature, "CollisionPriority", CLIENT_DEFAULT_UNIT_COLLISION_PRIORITY);
            float scanFrequency = GetAuthoredBehaviorFloat(authoredBehavior, authoredCreature, "ScanFrequency", CLIENT_DEFAULT_UNIT_SCAN_FREQUENCY);
            float fleeRange = GetAuthoredBehaviorFloat(authoredBehavior, authoredCreature, "FleeRange", CLIENT_DEFAULT_UNIT_FLEE_RANGE);
            float retreatRangeSquared = GetAuthoredBehaviorFloat(authoredBehavior, authoredCreature, "RetreatRangeSquared", CLIENT_DEFAULT_MONSTER_RETREAT_RANGE_SQUARED);
            float teleportFrequency = GetAuthoredBehaviorFloat(authoredBehavior, authoredCreature, "TeleportFrequency", CLIENT_DEFAULT_MONSTER_TELEPORT_FREQUENCY);
            float teleportLimboTime = GetAuthoredBehaviorFloat(authoredBehavior, authoredCreature, "TeleportLimboTime", CLIENT_DEFAULT_MONSTER_TELEPORT_LIMBO_TIME);
            float baseTime = GetAuthoredBehaviorFloat(authoredBehavior, authoredCreature, "BaseTime", CLIENT_DEFAULT_MONSTER_BASE_TIME);
            float variableTime = GetAuthoredBehaviorFloat(authoredBehavior, authoredCreature, "VariableTime", CLIENT_DEFAULT_MONSTER_VARIABLE_TIME);
            float leashRange = GetAuthoredBehaviorFloat(authoredBehavior, authoredCreature, "LeashRange", CLIENT_DEFAULT_MONSTER_LEASH_RANGE);

            string runtimeInstanceKey = !string.IsNullOrWhiteSpace(instanceKey) ? instanceKey : zoneName;
            RoomRuntime spawnRuntime = RequireInitializedRoomRuntime(runtimeInstanceKey, "SpawnMonster");
            if (spawnRuntime == null)
            {
                Debug.LogError($"[ROOM-RNG] Blocked monster spawn before client seed gcType='{gcType}' zone='{zoneName}' instance='{runtimeInstanceKey ?? "<null>"}'");
                return null;
            }
            if (!spawnRuntime.Initialized)
            {
                Debug.LogError($"[ROOM-RNG] Blocked monster spawn before client seed gcType='{gcType}' zone='{zoneName}' instance='{spawnRuntime.InstanceKey}'");
                return null;
            }
            SetCurrentRoomRuntime(spawnRuntime.InstanceKey, "SpawnMonster");

            byte tierLevel = GetLevelForTier(creatureDifficulty);
            byte zoneBase = GetZoneBaseLevel(zoneName);
            byte calculatedLevel = (byte)Math.Min(110, tierLevel + zoneBase);
            Debug.LogError($"[COMBAT] levelCalc tier={creatureDifficulty} tierLevel={tierLevel} zone={zoneName} zoneBase={zoneBase} level={calculatedLevel}");

            uint entityId = _nextMonsterId++;
            uint behaviorId = _nextMonsterId++;
            uint skillsId = _nextMonsterId++;
            uint manipulatorsId = _nextMonsterId++;
            uint modifiersId = _nextMonsterId++;
            uint unitId = _nextMonsterId++;

            bool isAlive = GetAuthoredBool(authoredDesc, "IsAlive", true);
            bool isOneHit = GetAuthoredBool(authoredDesc, "IsOneHit", false);
            uint initHPWire = (!isAlive && isOneHit)
                ? 256u
                : MonsterHealthTable.CalculateHPWire(calculatedLevel, unitDifficulty, maxHealth);

            var monster = new Monster
            {
                EntityId = entityId,
                BehaviorId = behaviorId,
                SkillsId = skillsId,
                ManipulatorsId = manipulatorsId,
                ModifiersId = modifiersId,
                UnitId = unitId,

                GCType = creatureData.gcType,
                SpawnGCType = spawnGcType,
                BehaviourType = creatureData.behaviourType,
                Name = GetAuthoredString(authoredDesc, "Label", creatureData.name),
                Faction = creatureData.faction,

                CreatureType = creatureData.creatureType,
                Element = creatureData.element,
                Tier = creatureDifficulty,
                Level = calculatedLevel,
                Difficulty = unitDifficulty,
                ExperienceDifficulty = encounterDifficulty,

                MaxHPWire = initHPWire,
                CurrentHPWire = initHPWire,


                MaxManaWire = (uint)(creatureData.manaPoints * 256),
                CurrentManaWire = (uint)(creatureData.manaPoints * 256),
                BaseDamage = creatureData.baseDamage,
                AttackRating = GetAuthoredFloat(authoredDesc, "AttackRating", creatureData.AttackRatingF),
                DamageMod = GetAuthoredFloat(authoredDesc, "DamageMod", creatureData.DamageModF),
                DamageTakenMod = GetAuthoredFloat(authoredDesc, "DamageTakenMod", 100f),
                DamageImmunity = GetAuthoredFloat(authoredDesc, "DamageImmunity", 0f),
                DamageResist = GetAuthoredFloat(authoredDesc, "DamageResist", 0f),
                CrushingResist = GetAuthoredFloat(authoredDesc, "CrushingResist", 0f),
                PiercingResist = GetAuthoredFloat(authoredDesc, "PiercingResist", 0f),
                SlashingResist = GetAuthoredFloat(authoredDesc, "SlashingResist", 0f),
                DefenseRating = GetAuthoredFloat(authoredDesc, "DefenseRating", creatureData.DefenseRatingF),
                CritChance = GetAuthoredFloat(authoredDesc, "CriticalChance", creatureData.CritChanceF),
                DivineResist = GetAuthoredFloat(authoredDesc, "DivineResist", creatureData.DivineResistF),
                FireResist = GetAuthoredFloat(authoredDesc, "FireResist", creatureData.FireResistF),
                IceResist = GetAuthoredFloat(authoredDesc, "IceResist", creatureData.IceResistF),
                PoisonResist = GetAuthoredFloat(authoredDesc, "PoisonResist", creatureData.PoisonResistF),
                ShadowResist = GetAuthoredFloat(authoredDesc, "ShadowResist", creatureData.ShadowResistF),
                MagicDamageResist = GetAuthoredFloat(authoredDesc, "MagicResist", GetAuthoredFloat(authoredDesc, "MagicDamageResist", 0f)),
                HealthRegen = GetAuthoredFloat(authoredDesc, "HealthRegen", 0f),
                HasAuthoredHealthRegen = authoredDesc != null && authoredDesc.HasProperty("HealthRegen"),
                ManaRegen = GetAuthoredFloat(authoredDesc, "ManaRegen", GetAuthoredFloat(authoredDesc, "PowerRegen", 0f)),
                HasAuthoredManaRegen = authoredDesc != null && (authoredDesc.HasProperty("ManaRegen") || authoredDesc.HasProperty("PowerRegen")),
                DamageVolatility = GetAuthoredFloat(authoredWeapon, "DamageVolatility", 0.5f),
                WeaponDamage = GetAuthoredFloat(authoredWeapon, "Damage", 1.0f),
                WeaponClass = GetAuthoredString(authoredWeapon, "WeaponClass", "HTH"),
                WeaponDamageType = GetAuthoredString(authoredWeapon, "DamageType", "CRUSHING"),
                WeaponUsesProjectile = weaponUsesProjectile,
                WeaponProjectileSpeed = weaponProjectileSpeed,
                WeaponProjectileSize = weaponProjectileSize,
                WeaponRange = weaponDescRange,
                PosX = posX,
                PosY = posY,
                PosZ = posZ,
                SpawnPosX = posX,
                SpawnPosY = posY,
                SpawnPosZ = posZ,
                Heading = heading,

                PerceptionRange = perceptionRange,
                AggroRange = GetAuthoredBehaviorFloat(authoredBehavior, authoredCreature, "AgroRange", aggroFallback),
                ShoutRange = GetAuthoredBehaviorFloat(authoredBehavior, authoredCreature, "ShoutRange", CLIENT_DEFAULT_MONSTER_SHOUT_RANGE),
                LeashRange = leashRange,
                AttackRange = GetAuthoredAttackRange(authoredDesc, authoredWeapon, creatureData),
                ClientSyncTolerance = GetAuthoredFloat(authoredWeapon, "ClientSyncTolerance", 10f),
                CollisionRadius = GetAuthoredFloat(authoredDesc, "CollisionRadius", 5f),
                AttackType = attackType,
                IdleAction = idleAction,
                LogicType = logicType,
                AttackStyle = attackStyle,
                Retreatable = retreatable,
                Leashed = leashed,
                UseIdleTime = useIdleTime,
                AutoScan = autoScan,
                AvoidUnits = avoidUnits,
                TurnBeforeMoving = turnBeforeMoving,
                PlayerControlled = playerControlled,
                CollisionBand = collisionBand,
                CollisionPriority = collisionPriority,
                ScanFrequency = scanFrequency,
                FleeRange = fleeRange,
                RetreatRangeSquared = retreatRangeSquared,
                TeleportFrequency = teleportFrequency,
                TeleportLimboTime = teleportLimboTime,
                BaseTime = baseTime,
                VariableTime = variableTime,
                CorpseLingerTicks = (ushort)Mathf.Clamp(GetAuthoredInt(authoredDesc, "CorpseLingerTime", 900), 0, ushort.MaxValue),
                AutoRespawn = GetAuthoredBool(authoredCreature, "AutoRespawn", GetAuthoredBool(authoredDesc, "AutoRespawn", false)),
                RespawnRateTicks = (ushort)Mathf.Clamp(GetAuthoredInt(authoredCreature, "RespawnRate", GetAuthoredInt(authoredDesc, "RespawnRate", 3600)), 0, ushort.MaxValue),
                AttackSpeed = GetAuthoredFloat(authoredDesc, "AttackSpeed", 1f),
                AttackCooldown = attackCooldown,
                AttackLeadDelay = attackTiming.AttackLeadDelay,
                AttackSoundLeadDelay = attackTiming.AttackSoundLeadDelay,
                HasAttackSound = HasAuthoredAttackSound(authoredDesc),
                AttackWeaponSoundCount = CountAuthoredSounds(authoredDesc, "WEAPONATTACK"),
                AttackRepeatSoundCount = CountAuthoredSounds(authoredDesc, "ATTACK"),
                AttackTotalFrames = attackTiming.TotalFrames,
                AttackHitFrames = attackTiming.HitFrames,
                AttackSoundFrames = attackTiming.SoundFrames,
                AttackFrameResolved = attackTiming.VariantResolved,
                AttackTimingSource = attackTiming.Source,
                AttackTimingReason = attackTiming.Reason,
                MoveSpeed = GetAuthoredMoveSpeed(authoredDesc, creatureData),
                WalkSpeed = GetAuthoredWalkSpeed(authoredCreature, authoredDesc, creatureData),
                WanderRange = GetAuthoredWanderRange(authoredBehavior, authoredCreature),

                Manipulators = spawnManipulators,

                State = MonsterState.Idle,
                IsAlive = true,
                SpawnTime = Time.time,
                ZoneName = zoneName,
                InstanceKey = spawnRuntime.InstanceKey,
                AuthoredArchetypeAncestry = BuildAuthoredArchetypeAncestry(spawnGcType, creatureData.gcType),
                EncounterGroupKey = encounterGroupKey,
                SpawnBehaviourType = spawnBehaviourType
            };

            monster.WeaponClassId = DamageResolver.ResolveWeaponClassId(monster.WeaponClass);
            monster.DamageTypeId = DamageResolver.ResolveDamageTypeId(monster.WeaponDamageType);
            MaterializeMonsterUnitSlots(monster);
            InitializeMonsterAiRuntime(monster);
            ResetMonsterClientVisiblePosition(monster, monster.PosX, monster.PosY, GetCombatTime(), "SPAWN");
            _activeMonsters[entityId] = monster;
            RegisterEntityOrder(entityId);
            SetRuntimeMonsterHPWire(monster, monster.CurrentHPWire, false, Time.time, "SPAWN");
            monster.RngSeed = spawnRuntime.Seed;
            monster.Rng = spawnRuntime.RoomRng;
            ConfigureMonsterPrimaryActiveSkill(monster);
            ConsumeState0MultiAttackDraw(monster, spawnRuntime.RoomRng);
            Debug.LogError($"[COMBAT] monster='{monster.Name}' roomRuntime='{monster.InstanceKey}' seed=0x{spawnRuntime.Seed:X8}");

            _componentToEntityMap[entityId] = entityId;
            _componentToEntityMap[behaviorId] = entityId;
            _componentToEntityMap[skillsId] = entityId;
            _componentToEntityMap[manipulatorsId] = entityId;
            _componentToEntityMap[modifiersId] = entityId;
            _componentToEntityMap[unitId] = entityId;

            if (ShouldRegisterWander(idleAction, monster.WanderRange, zoneName))
            {
                bool canWander = !string.IsNullOrWhiteSpace(monster.EncounterGroupKey);
                Debug.LogError($"[WANDER-LEASH] {monster.Name}#{monster.EntityId} canWander={canWander} encGroup='{monster.EncounterGroupKey}' source=Unit+0x30c-EncounterObject");
                WanderSimulator.Instance.RegisterMonster(monster, canWander);
            }

            int clientAttackRating = DamageResolver.ResolveMonsterAttackRating(monster);
            int clientDefenseRating = DamageResolver.ResolveMonsterDefenseRating(monster);
            float monsterDamageTable = ResolveMonsterDamageTable(monster.Level);
            float effectiveWeaponDamage = monster.WeaponDamage > 0f ? monster.WeaponDamage : 1f;
            float effectiveDamage = monsterDamageTable * ResolveMonsterDamageModifier(monster) * effectiveWeaponDamage;
            Debug.LogError($"[COMBAT] spawn name='{monster.Name}' id={entityId} level={calculatedLevel} hp={monster.MaxHP} damage={creatureData.baseDamage} unitDifficulty={monster.Difficulty:F2} damageMod={monster.DamageMod:F2} encounterDifficulty={monster.ExperienceDifficulty:F2} effectiveDamageMod={ResolveMonsterDamageModifier(monster):F2}");
            Debug.LogError($"[SPAWN-AUDIT] id={entityId} name='{monster.Name}' baseGc='{monster.GCType}' spawnGc='{monster.SpawnGCType}' zone='{zoneName}' group='{monster.EncounterGroupKey}' level={monster.Level} hpWire={monster.MaxHPWire} manaWire={monster.MaxManaWire} hpRegen={monster.HealthRegen:F3} manaRegen={monster.ManaRegen:F3} hpRegenFactor={ResolveMonsterHealthRegenFactor(monster)} manaRegenFactor={ResolveMonsterManaRegenFactor(monster)} unitDiff={monster.Difficulty:F2} encounterDiff={monster.ExperienceDifficulty:F2} attackRatingAuth={monster.AttackRating:F3} attackRating={clientAttackRating} defenseRatingAuth={monster.DefenseRating:F3} defenseRating={clientDefenseRating} crit={monster.CritChance:F2} damageTable={monsterDamageTable:F3} damageMod={monster.DamageMod:F3} weaponClass={monster.WeaponClass}/{monster.WeaponClassId} damageType={monster.WeaponDamageType}/{monster.DamageTypeId} weaponDamage={monster.WeaponDamage:F3} volatility={monster.DamageVolatility:F3} effectiveDamage={effectiveDamage:F3} aggro={monster.AggroRange:F1} wander={monster.WanderRange:F1} pos=({monster.PosX:F1},{monster.PosY:F1},{monster.PosZ:F1})");
            Debug.LogError($"[COMBAT] componentIds entity={entityId} behavior={behaviorId} skills={skillsId} manipulators={manipulatorsId} modifiers={modifiersId} unit={unitId}");
            Debug.LogError($"[COMBAT] position=({posX:F1},{posY:F1},{posZ:F1}) perceptionRange={monster.PerceptionRange} aggroRange={monster.AggroRange} shoutRange={monster.ShoutRange} leashRange={monster.LeashRange} attackRange={monster.AttackRange} syncTolerance={monster.ClientSyncTolerance} collisionRadius={monster.CollisionRadius} attackSpeed={monster.AttackSpeed} cooldown={monster.AttackCooldown} walkSpeed={monster.WalkSpeed} wanderRange={monster.WanderRange} group={monster.EncounterGroupKey}");
            Debug.LogError($"[COMBAT] ai attackType={monster.AttackType} idleAction={monster.IdleAction} logicType={monster.LogicType} attackStyle={monster.AttackStyle} retreatable={monster.Retreatable} leashed={monster.Leashed} useIdleTime={monster.UseIdleTime} autoScan={monster.AutoScan} avoidUnits={monster.AvoidUnits} turnBeforeMoving={monster.TurnBeforeMoving} playerControlled={monster.PlayerControlled} collisionBand={monster.CollisionBand} collisionPriority={monster.CollisionPriority} scanFrequency={monster.ScanFrequency} fleeRange={monster.FleeRange} retreatRangeSquared={monster.RetreatRangeSquared} teleportFrequency={monster.TeleportFrequency} teleportLimboTime={monster.TeleportLimboTime} baseTime={monster.BaseTime} variableTime={monster.VariableTime}");
            TraceMonsterState(monster, "spawn", null, -1f, monster.AttackRange, "spawn");

            OnMonsterSpawned?.Invoke(monster);
            return monster;
        }

        private static void MaterializeMonsterUnitSlots(Monster monster)
        {
            if (monster == null)
                return;

            monster.Slots ??= new UnitSlotState();
            monster.Slots.ResetDefaults();
            monster.Slots[UnitSlot.AttackRating] = Fixed.ToWire(monster.AttackRating);
            monster.Slots[UnitSlot.DamageMod] = Fixed.Percent(monster.DamageMod * 100f);
            monster.Slots[UnitSlot.DefenseRating] = Fixed.ToWire(monster.DefenseRating);
            monster.Slots[UnitSlot.CriticalChance] = Fixed.Percent(monster.CritChance);
            monster.Slots[UnitSlot.MaxHealth] = (int)Math.Min(int.MaxValue, monster.MaxHPWire);
            monster.Slots[UnitSlot.MaxMana] = (int)Math.Min(int.MaxValue, monster.MaxManaWire);
            monster.Slots[UnitSlot.HealthRegen] = Fixed.ToWire(monster.HealthRegen);
            monster.Slots[UnitSlot.ManaRegen] = Fixed.ToWire(monster.ManaRegen);
            monster.Slots[UnitSlot.GlobalDamageTakenMod] = Fixed.Percent(monster.DamageTakenMod);
            monster.Slots[UnitSlot.DamageImmunity] = Fixed.Percent(monster.DamageImmunity);
            monster.Slots[UnitSlot.DamageResist] = Fixed.Percent(monster.DamageResist);
            monster.Slots[UnitSlot.CrushingResist] = Fixed.Percent(monster.CrushingResist);
            monster.Slots[UnitSlot.PiercingResist] = Fixed.Percent(monster.PiercingResist);
            monster.Slots[UnitSlot.SlashingResist] = Fixed.Percent(monster.SlashingResist);
            monster.Slots[UnitSlot.FireResist] = Fixed.Percent(monster.FireResist);
            monster.Slots[UnitSlot.IceResist] = Fixed.Percent(monster.IceResist);
            monster.Slots[UnitSlot.PoisonResist] = Fixed.Percent(monster.PoisonResist);
            monster.Slots[UnitSlot.ShadowResist] = Fixed.Percent(monster.ShadowResist);
            monster.Slots[UnitSlot.DivineResist] = Fixed.Percent(monster.DivineResist);
            monster.Slots[UnitSlot.MagicResist] = Fixed.Percent(monster.MagicDamageResist);
            monster.Slots[UnitSlot.FireDamageTakenMod] = Fixed.Percent(monster.FireDamageTakenMod);
            monster.Slots[UnitSlot.IceDamageTakenMod] = Fixed.Percent(monster.IceDamageTakenMod);
            monster.Slots[UnitSlot.PoisonDamageTakenMod] = Fixed.Percent(monster.PoisonDamageTakenMod);
            monster.Slots[UnitSlot.ShadowDamageTakenMod] = Fixed.Percent(monster.ShadowDamageTakenMod);
            monster.Slots[UnitSlot.DivineDamageTakenMod] = Fixed.Percent(monster.DivineDamageTakenMod);
            Debug.LogError($"[CLIENT-SLOTS] monster={monster.Name}#{monster.EntityId} source=Unit::computeAttributes slots='{monster.Slots.DescribeDamageCore()}' status=materialized-desc-only activeSkillMods=incomplete");
        }

        private void InitializeMonsterAiRuntime(Monster monster)
        {
            if (monster == null)
                return;

            monster.Ai ??= new MonsterAiRuntime();
            monster.Ai.SetState(MonsterStateId.IdleSearch, "spawn->idle");
            monster.Ai.SkillListBuilt = false;
            monster.Ai.TargetEntityId = 0;
            monster.Ai.AlertSourceEntityId = 0;
            if (HasEncounterObject(monster))
            {
                EncounterRuntime encounter = GetOrCreateEncounterRuntime(monster);
                encounter.AddUnit(monster.EntityId);
                monster.Encounter = encounter;
                ApplyEncounterGroup(encounter);
            }
            Debug.LogError($"[MON-FSM] monster={monster.Name}#{monster.EntityId} clientState={monster.Ai.StateId} encounterState={monster.EncounterObjectState} live={monster.EncounterLiveUnitCount} returning={monster.EncounterReturningUnitCount} sourceFunction=MonsterBehavior2::States+EncounterObject::update status=schema-only");
        }

        private string ResolveEncounterRuntimeKey(Monster monster)
        {
            if (monster == null || string.IsNullOrWhiteSpace(monster.EncounterGroupKey))
                return null;
            string instance = !string.IsNullOrWhiteSpace(monster.InstanceKey)
                ? RoomRuntime.NormalizeInstanceKey(monster.InstanceKey)
                : RoomRuntime.NormalizeInstanceKey(monster.ZoneName);
            return $"{instance}:{monster.EncounterGroupKey}";
        }

        private EncounterRuntime GetOrCreateEncounterRuntime(Monster monster)
        {
            string key = ResolveEncounterRuntimeKey(monster);
            if (string.IsNullOrWhiteSpace(key))
                return null;
            if (!_encounterRuntimes.TryGetValue(key, out EncounterRuntime runtime))
            {
                runtime = new EncounterRuntime { Key = key };
                _encounterRuntimes[key] = runtime;
                Debug.LogError($"[ENCOUNTER-RUNTIME] create key='{key}' sourceFunction=EncounterObject::update shared=True packetRuntime=unresolved");
            }
            return runtime;
        }

        private static void ApplyEncounterMirror(Monster monster)
        {
            EncounterRuntime encounter = monster?.Encounter;
            if (monster == null || encounter == null)
                return;

            monster.EncounterObjectState = encounter.StateByte;
            monster.EncounterLiveUnitCount = encounter.LiveUnitCount;
            monster.EncounterReturningUnitCount = encounter.ReturningUnitCount;
            monster.EncounterActiveTimer = encounter.ActiveTimer;
            monster.EncounterScanTimer = encounter.ScanTimer;
            monster.EncounterScanEnabled = encounter.ScanEnabled;
        }

        private void ApplyEncounterGroup(EncounterRuntime encounter)
        {
            if (encounter == null)
                return;
            foreach (var monster in _activeMonsters.Values)
            {
                if (monster?.Encounter == encounter)
                    ApplyEncounterMirror(monster);
            }
        }

        private void TickEncounterDeactivation()
        {
            if (_encounterRuntimes.Count == 0 || _players.Count == 0)
                return;
            foreach (var encounter in _encounterRuntimes.Values)
            {
                if (encounter == null || encounter.StateByte != 2)
                    continue;
                bool playerInRange = false;
                float keepRadiusSq = 0f;
                float ex = 0f, ey = 0f;
                foreach (var monster in _activeMonsters.Values)
                {
                    if (monster == null || monster.Encounter != encounter || !monster.IsAlive)
                        continue;
                    ex = monster.PosX;
                    ey = monster.PosY;
                    float keep = monster.ShoutRange > 0f ? monster.ShoutRange : CLIENT_DEFAULT_MONSTER_SHOUT_RANGE;
                    keepRadiusSq = keep * keep;
                    foreach (var player in _players.Values)
                    {
                        if (player == null) continue;
                        float dx = player.PosX - ex;
                        float dy = player.PosY - ey;
                        if (dx * dx + dy * dy <= keepRadiusSq)
                        {
                            playerInRange = true;
                            break;
                        }
                    }
                    if (playerInRange)
                        break;
                }
                if (encounter.TickActive(playerInRange))
                    Debug.LogError($"[ENCOUNTER-RUNTIME] key='{encounter.Key}' deactivate state={encounter.StateByte} live={encounter.LiveUnitCount} reason=player-left sourceFunction=EncounterObject::update@0x00563040+0x13c");
            }
        }

        private void EncounterMarkActive(Monster monster, string reason)
        {
            if (!HasEncounterObject(monster))
                return;
            EncounterRuntime encounter = monster.Encounter ?? GetOrCreateEncounterRuntime(monster);
            if (encounter == null)
                return;
            monster.Encounter = encounter;
            encounter.MarkActive();
            ApplyEncounterGroup(encounter);
            Debug.LogError($"[ENCOUNTER-RUNTIME] key='{encounter.Key}' state={encounter.StateByte} live={encounter.LiveUnitCount} returning={encounter.ReturningUnitCount} reason={reason ?? "active"} sourceFunction=EncounterObject+0x142 shared=True");
        }

        private void EncounterLogAssistNotActive(Monster monster, string reason)
        {
            if (!HasEncounterObject(monster))
                return;
            EncounterRuntime encounter = monster.Encounter ?? GetOrCreateEncounterRuntime(monster);
            if (encounter == null)
                return;
            monster.Encounter = encounter;
            ApplyEncounterMirror(monster);
            Debug.LogError($"[ENCOUNTER-RUNTIME] key='{encounter.Key}' state={encounter.StateByte} live={encounter.LiveUnitCount} returning={encounter.ReturningUnitCount} reason={reason ?? "assist"} action=not-active sourceFunction=MonsterBehavior2::States@0x0051BB30 assist=0x0C no-EncounterObject+0x142-write");
        }

        private void EncounterOnUnitDied(Monster monster, string reason)
        {
            if (!HasEncounterObject(monster))
                return;
            EncounterRuntime encounter = monster.Encounter ?? GetOrCreateEncounterRuntime(monster);
            if (encounter == null)
                return;
            monster.Encounter = encounter;
            encounter.MarkUnitDied(monster.EntityId);
            ApplyEncounterGroup(encounter);
            ApplyEncounterMirror(monster);
            Debug.LogError($"[ENCOUNTER-RUNTIME] key='{encounter.Key}' onUnitDied unit={monster.EntityId} state={encounter.StateByte} live={encounter.LiveUnitCount} returning={encounter.ReturningUnitCount} scan={encounter.ScanEnabled} activeTimer={encounter.ActiveTimer} reason={reason ?? "death"} sourceFunction=EncounterObject::OnUnitDied shared=True");
        }

        private void EncounterOnUnitRemoved(Monster monster, string reason)
        {
            if (!HasEncounterObject(monster))
                return;
            EncounterRuntime encounter = monster.Encounter ?? GetOrCreateEncounterRuntime(monster);
            if (encounter == null)
                return;
            monster.Encounter = encounter;
            bool reset = encounter.MarkUnitRemoved(monster.EntityId);
            ApplyEncounterGroup(encounter);
            ApplyEncounterMirror(monster);
            Debug.LogError($"[ENCOUNTER-RUNTIME] key='{encounter.Key}' onUnitRemoved unit={monster.EntityId} reset={reset} state={encounter.StateByte} live={encounter.LiveUnitCount} returning={encounter.ReturningUnitCount} scan={encounter.ScanEnabled} scanTimer={encounter.ScanTimer} activeTimer={encounter.ActiveTimer} reason={reason ?? "remove"} sourceFunction=EncounterObject::OnUnitRemoved shared=True");
        }

        private List<string> BuildAuthoredArchetypeAncestry(params string[] roots)
        {
            var ancestry = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var gc = GCDatabase.Instance;
            if (gc == null || !gc.IsLoaded)
                return ancestry;

            foreach (string root in roots)
            {
                if (string.IsNullOrWhiteSpace(root))
                    continue;
                foreach (string path in gc.GetInheritanceChainPaths(root))
                {
                    if (seen.Add(path))
                        ancestry.Add(path);
                }
            }
            return ancestry;
        }
        private string ResolveDungeonCreaturePath(string zoneName, string baseGcType)
        {
            if (string.IsNullOrEmpty(zoneName) || string.IsNullOrEmpty(baseGcType))
                return null;
            var gc = GCDatabase.Instance;
            if (gc == null || !gc.IsLoaded)
                return null;

            string lookupZone = zoneName;
            int instIdx = zoneName.IndexOf("_inst", StringComparison.OrdinalIgnoreCase);
            if (instIdx > 0)
                lookupZone = zoneName.Substring(0, instIdx);

            if (!lookupZone.StartsWith("dungeon", StringComparison.OrdinalIgnoreCase))
                return null;
            int underscoreIdx = lookupZone.IndexOf('_');
            if (underscoreIdx <= 0) return null;
            string dungeonPrefix = lookupZone.Substring(0, underscoreIdx);

            int lvlIdx = lookupZone.IndexOf("_level", StringComparison.OrdinalIgnoreCase);
            if (lvlIdx < 0 || lvlIdx + 8 > lookupZone.Length) return null;
            string numStr = lookupZone.Substring(lvlIdx + 6, 2);
            if (!int.TryParse(numStr, out int levelNum) || levelNum < 1 || levelNum > 3)
                return null;
            int rank = levelNum;

            if (baseGcType.Equals("creatures.whiskers.broodling.basic.champion", StringComparison.OrdinalIgnoreCase))
                return null;

            string rankSuffix = $".rank{rank}";
            string dungeonMobPrefix = $"world.{dungeonPrefix}.mob.";
            foreach (string candidate in gc.RegisteredPaths)
            {
                if (string.IsNullOrWhiteSpace(candidate) ||
                    !candidate.EndsWith(rankSuffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string resolved = null;
                if (candidate.StartsWith(dungeonMobPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    resolved = candidate;
                }
                else if (candidate.IndexOf('.') == candidate.LastIndexOf('.'))
                {
                    resolved = dungeonMobPrefix + candidate;
                }

                if (string.IsNullOrEmpty(resolved))
                    continue;
                var candidateAncestry = BuildResolvedAuthoredAncestrySet(gc, resolved);
                if (!candidateAncestry.Contains(baseGcType))
                    continue;

                Debug.LogError($"[SPAWN-ARCHETYPE] base='{baseGcType}' zone='{lookupZone}' resolved='{resolved}' source=authored-ancestry");
                return resolved;
            }

            RuntimeEvidence.LogFallbackHit("spawn-archetype", "dungeon-family-unresolved", $"zone={lookupZone} base={baseGcType} rank={rank}", 32);
            return null;
        }

        private static HashSet<string> BuildResolvedAuthoredAncestrySet(GCDatabase gc, string path)
        {
            var ancestry = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (gc == null || string.IsNullOrWhiteSpace(path))
                return ancestry;

            string current = path.Trim();
            for (int depth = 0; depth < 32 && !string.IsNullOrWhiteSpace(current); depth++)
            {
                if (!ancestry.Add(current))
                    break;
                var node = gc.Resolve(current);
                if (node == null)
                    break;
                if (!string.IsNullOrWhiteSpace(node.Name))
                    ancestry.Add(node.Name);
                current = node.Extends;
            }

            return ancestry;
        }

        private GCNode ResolveAuthoredCreatureNode(string spawnGcType, string baseGcType)
        {
            var gc = GCDatabase.Instance;
            if (gc == null || !gc.IsLoaded) return null;

            GCNode node = null;
            if (!string.IsNullOrEmpty(spawnGcType))
                node = gc.ResolveWithInheritance(spawnGcType);
            if (node == null && !string.IsNullOrEmpty(baseGcType))
                node = gc.ResolveWithInheritance(baseGcType);
            return node;
        }

        private GCNode ResolveAuthoredBehaviorNode(string behaviorGcType)
        {
            var gc = GCDatabase.Instance;
            if (gc == null || !gc.IsLoaded || string.IsNullOrEmpty(behaviorGcType)) return null;
            return gc.ResolveWithInheritance(behaviorGcType);
        }

        private string ResolveAuthoredChildPath(string rootPath, string childPath)
        {
            if (string.IsNullOrEmpty(rootPath) || string.IsNullOrEmpty(childPath)) return null;
            var gc = GCDatabase.Instance;
            if (gc == null || !gc.IsLoaded) return null;

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string currentPath = rootPath;
            while (!string.IsNullOrEmpty(currentPath) && visited.Add(currentPath))
            {
                var node = gc.Resolve(currentPath);
                if (node == null) return null;
                if (RawChildPathExists(node, childPath))
                    return currentPath + "." + childPath;
                currentPath = node.Extends;
            }
            return null;
        }

        private bool RawChildPathExists(GCNode node, string childPath)
        {
            if (node == null || string.IsNullOrEmpty(childPath)) return false;
            var current = node;
            foreach (string part in childPath.Split('.'))
            {
                if (current == null || !current.Children.TryGetValue(part, out current))
                    return false;
            }
            return true;
        }

        private GCNode ResolveEffectiveAuthoredChild(string rootPath, string childPath)
        {
            if (string.IsNullOrEmpty(rootPath) || string.IsNullOrEmpty(childPath)) return null;
            var gc = GCDatabase.Instance;
            if (gc == null || !gc.IsLoaded) return null;
            return gc.ResolveWithInheritance(rootPath)?.ResolvePath(childPath);
        }

        private Dictionary<string, ManipulatorData> BuildSpawnManipulators(string spawnGcType, string baseGcType, GCNode authoredCreature, Dictionary<string, ManipulatorData> fallback)
        {
            var result = new Dictionary<string, ManipulatorData>(StringComparer.OrdinalIgnoreCase);
            var seenSkillPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            SidecarMonsterSkills sidecarSkillRow = ResolveSidecarSpawnSkillRow(spawnGcType, baseGcType, out string sidecarSourcePath);
            bool useAuthoritativeSidecarSkills = sidecarSkillRow != null;
            var manipulatorCandidates = new[]
            {
                authoredCreature?.GetChild("Manipulators"),
                ResolveEffectiveAuthoredChild(spawnGcType, "Manipulators"),
                ResolveEffectiveAuthoredChild(baseGcType, "Manipulators")
            };
            bool hadManipulatorNode = false;
            bool hadPrimaryWeaponNode = false;
            int skillIndex = 1;
            foreach (var manipulators in manipulatorCandidates)
            {
                if (manipulators == null)
                    continue;
                hadManipulatorNode = true;
                var primaryWeapon = manipulators.GetChild("PrimaryWeapon");
                if (primaryWeapon != null && !result.ContainsKey("primaryweapon"))
                {
                    hadPrimaryWeaponNode = true;
                    string primaryWeaponPath = ResolveManipulatorRuntimeType(primaryWeapon);
                    if (!string.IsNullOrEmpty(primaryWeaponPath))
                        result["primaryweapon"] = CreateSpawnManipulator(primaryWeaponPath, primaryWeapon);
                    else
                    {
                        RuntimeEvidence.LogFallbackHit(
                            "spawn-manipulator",
                            "primaryweapon-unresolved",
                            $"spawn='{spawnGcType ?? ""}' base='{baseGcType ?? ""}'",
                            16);
                    }
                }

                if (useAuthoritativeSidecarSkills)
                    continue;

                if (manipulators.AnonymousChildren != null)
                {
                    foreach (var child in manipulators.AnonymousChildren)
                    {
                        if (child == null || !IsActiveSkillManipulatorPath(child.Extends)) continue;
                        if (!seenSkillPaths.Add(child.Extends)) continue;
                        result[$"skill{skillIndex++}"] = CreateSpawnManipulator(child.Extends, child);
                    }
                }

                foreach (var manipulatorEntry in manipulators.Children)
                {
                    if (manipulatorEntry.Key.Equals("PrimaryWeapon", StringComparison.OrdinalIgnoreCase)) continue;
                    string manipulatorPath = ResolveManipulatorRuntimeType(manipulatorEntry.Value);
                    if (string.IsNullOrEmpty(manipulatorPath))
                        manipulatorPath = ResolveAuthoredChildPath(spawnGcType, "Manipulators." + manipulatorEntry.Key) ?? ResolveAuthoredChildPath(baseGcType, "Manipulators." + manipulatorEntry.Key);
                    if (string.IsNullOrEmpty(manipulatorPath) && IsActiveSkillManipulatorPath(manipulatorEntry.Value.Extends))
                        manipulatorPath = manipulatorEntry.Value.Extends;
                    if (string.IsNullOrEmpty(manipulatorPath)) continue;
                    if (!IsActiveSkillManipulatorPath(manipulatorPath) && !IsActiveSkillManipulatorPath(manipulatorEntry.Value.Extends)) continue;
                    if (!seenSkillPaths.Add(manipulatorPath)) continue;
                    result[$"skill{skillIndex++}"] = CreateSpawnManipulator(manipulatorPath, manipulatorEntry.Value);
                }
            }

            if (useAuthoritativeSidecarSkills)
            {
                skillIndex = ApplySidecarSpawnSkillsAuthoritative(result, sidecarSkillRow, spawnGcType, baseGcType, sidecarSourcePath);
                Debug.LogError($"[MON-SKILL-INHERIT] spawn='{spawnGcType ?? ""}' base='{baseGcType ?? ""}' skills={skillIndex - 1} authoritativeSidecar=True sourceFunction=GCObject::getChildByType@0x005E9E50 createChildInstances@0x005E9840");
                return result;
            }

            if (result.Count > 0)
            {
                if (hadManipulatorNode && skillIndex > 1)
                    Debug.LogError($"[MON-SKILL-INHERIT] spawn='{spawnGcType ?? ""}' base='{baseGcType ?? ""}' skills={skillIndex - 1} sourceFunction=GCObject::createChildInstances@0x005E9840");
                return result;
            }

            if (authoredCreature != null)
            {
                string reason = hadManipulatorNode
                    ? (hadPrimaryWeaponNode ? "authored-primary-unresolved" : "authored-empty")
                    : "authored-missing-manipulators";
                RuntimeEvidence.LogFallbackHit(
                    "spawn-manipulator",
                    reason,
                    $"spawn='{spawnGcType ?? ""}' base='{baseGcType ?? ""}'",
                    16);
                Debug.LogError($"[AUTHORED-COVERAGE] area=spawn-manipulator reason={reason} spawn='{spawnGcType ?? ""}' base='{baseGcType ?? ""}' dbFallbackSuppressed=True");
                return result;
            }

                RuntimeEvidence.LogFallbackHit(
                    "spawn-manipulator",
                    hadManipulatorNode ? "authored-empty" : "missing-authored",
                    $"spawn='{spawnGcType ?? ""}' base='{baseGcType ?? ""}' dbFallbackSuppressed=True dbCount={fallback?.Count ?? 0}",
                    16);
            Debug.LogError($"[AUTHORED-COVERAGE] area=spawn-manipulator reason={(hadManipulatorNode ? "authored-empty" : "missing-authored")} spawn='{spawnGcType ?? ""}' base='{baseGcType ?? ""}' dbFallbackSuppressed=True dbCount={fallback?.Count ?? 0}");
            return result;
        }

        private SidecarMonsterSkills ResolveSidecarSpawnSkillRow(string spawnGcType, string baseGcType, out string sourcePath)
        {
            sourcePath = null;
            var sidecar = SidecarCatalog.Instance;
            if (sidecar == null || !sidecar.IsLoaded)
                return null;

            SidecarMonsterSkills row = null;
            if (!string.IsNullOrWhiteSpace(spawnGcType) && sidecar.TryGetEffectiveMonsterSkills(spawnGcType, out row))
            {
                sourcePath = spawnGcType;
                return row;
            }
            else if (!string.IsNullOrWhiteSpace(baseGcType) && sidecar.TryGetEffectiveMonsterSkills(baseGcType, out row))
            {
                sourcePath = baseGcType;
                return row;
            }

            return null;
        }

        private int ApplySidecarSpawnSkillsAuthoritative(Dictionary<string, ManipulatorData> result, SidecarMonsterSkills row, string spawnGcType, string baseGcType, string sourcePath)
        {
            int skillIndex = 1;
            if (result != null)
            {
                var staleSkillKeys = new List<string>();
                foreach (string key in result.Keys)
                {
                    if (key.StartsWith("skill", StringComparison.OrdinalIgnoreCase))
                        staleSkillKeys.Add(key);
                }
                foreach (string key in staleSkillKeys)
                    result.Remove(key);
            }

            if (row?.Skills == null || row.Skills.Count == 0)
            {
                Debug.LogError($"[MON-SKILL-SIDECAR] authoritative=True spawn='{spawnGcType ?? ""}' base='{baseGcType ?? ""}' source='{sourcePath ?? row?.MonsterPath ?? ""}' selectedManipulatorsNodeId='{row?.ManipulatorsNodeId ?? ""}' manipulatorSource='{row?.ManipulatorsSource ?? ""}' skillCount={row?.SkillCount ?? 0} loadedSkills=0 sourceFunction=GCObject::getChildByType@0x005E9E50 createChildInstances@0x005E9840");
                return skillIndex;
            }

            foreach (var skill in row.Skills)
            {
                string skillPath = !string.IsNullOrWhiteSpace(skill.SkillPath) ? skill.SkillPath : skill.ExtendsRaw;
                if (string.IsNullOrWhiteSpace(skillPath))
                    continue;
                result[$"skill{skillIndex++}"] = CreateSpawnManipulatorFromSidecar(skill);
            }

            Debug.LogError($"[MON-SKILL-SIDECAR] authoritative=True spawn='{spawnGcType ?? ""}' base='{baseGcType ?? ""}' source='{sourcePath ?? row.MonsterPath ?? ""}' selectedManipulatorsNodeId='{row.ManipulatorsNodeId ?? ""}' manipulatorSource='{row.ManipulatorsSource ?? ""}' skillCount={row.SkillCount} loadedSkills={row.Skills.Count} totalSkills={skillIndex - 1} sourceFunction=GCObject::getChildByType@0x005E9E50 createChildInstances@0x005E9840");
            return skillIndex;
        }

        private ManipulatorData CreateSpawnManipulatorFromSidecar(SidecarSkill skill)
        {
            string skillPath = !string.IsNullOrWhiteSpace(skill.SkillPath) ? skill.SkillPath : skill.ExtendsRaw;
            var data = new ManipulatorData { gcType = skillPath };
            if (skill.EffectiveProperties != null)
            {
                foreach (var propertyEntry in skill.EffectiveProperties)
                    data.properties[propertyEntry.Key] = propertyEntry.Value;
            }
            if (skill.LocalOverrideProperties != null)
            {
                foreach (var propertyEntry in skill.LocalOverrideProperties)
                {
                    data.properties[propertyEntry.Key] = propertyEntry.Value;
                    if (propertyEntry.Key.StartsWith("Description.", StringComparison.OrdinalIgnoreCase))
                        data.properties[propertyEntry.Key.Substring("Description.".Length)] = propertyEntry.Value;
                }
            }
            if (skill.IsPrimaryAttack)
                data.properties["IsPrimaryAttack"] = "true";
            return data;
        }

        private string ResolveManipulatorRuntimeType(GCNode manipulatorNode)
        {
            if (manipulatorNode == null) return null;
            if (!string.IsNullOrEmpty(manipulatorNode.Extends))
                return manipulatorNode.Extends;
            return null;
        }

        private ManipulatorData CreateSpawnManipulator(string gcType, GCNode node)
        {
            var data = new ManipulatorData { gcType = gcType };
            CopyManipulatorProperties(data, ResolveAuthoredWeaponDescription(node));
            CopyManipulatorProperties(data, node);
            CopyManipulatorProperties(data, node?.GetChild("Description"));
            return data;
        }

        private void ConsumeState0MultiAttackDraw(Monster monster, MersenneTwister roomRng)
        {
            if (monster == null || roomRng == null)
                return;
            if (!monster.HasMultiAttackStateDraw || monster.MultiAttackCountRange == 0)
            {
                Debug.LogError($"[MON-STATE0] {monster?.Name}#{monster?.EntityId} multiAttackDraw=skip flag={monster?.HasMultiAttackStateDraw} base={monster?.MultiAttackBaseCount} range={monster?.MultiAttackCountRange} roomRngPos={roomRng?.CallsSinceReseed} sourceFunction=MonsterBehavior2::States@0x0051bd7f-0x0051bdae(bit4-unset)");
                return;
            }
            uint raw = RngLedger.Generate(roomRng, "room", "monster-state0:MonsterBehavior2::States+multiAttack", monster.InstanceKey);
            uint attacks = (uint)monster.MultiAttackBaseCount + (raw % monster.MultiAttackCountRange);
            Debug.LogError($"[MON-STATE0] {monster.Name}#{monster.EntityId} multiAttackDraw=fire raw=0x{raw:X8} base={monster.MultiAttackBaseCount} range={monster.MultiAttackCountRange} attacks={attacks} roomRngPos={roomRng.CallsSinceReseed} sourceFunction=MonsterBehavior2::States@0x0051bdae Random::generate@0x0044b1f0");
        }

        private void ConfigureMonsterPrimaryActiveSkill(Monster monster)
        {
            if (monster == null) return;
            monster.ActiveSkills.Clear();
            monster.SelectedActiveSkill = null;
            monster.PrimaryActiveSkillPath = null;
            monster.PrimaryActiveSkillId = 10;
            monster.PrimaryActiveSkillRange = 0f;
            monster.PrimaryActiveSkillSpellUseRange = 0f;
            monster.PrimaryActiveSkillMinimumRange = -1f;
            monster.PrimaryActiveSkillTargetType = null;
            monster.PrimaryActiveSkillSpellUse = null;
            monster.PrimaryActiveSkillHasSelfHealthPct = false;
            monster.PrimaryActiveSkillSelfHealthPct = 0f;
            monster.PrimaryActiveSkillHasTargetHealthPct = false;
            monster.PrimaryActiveSkillTargetHealthPct = 0f;
            monster.PrimaryActiveSkillCooldownSeconds = 0f;
            monster.PrimaryActiveSkillCooldownTicks = 0;
            monster.PrimaryActiveSkillCooldownRemainingTicks = 0;
            monster.PrimaryActiveSkillCooldownLastTime = GetCombatTime();
            monster.PrimaryActiveSkillAnimationId = 0;
            monster.PrimaryActiveSkillEffect = null;
            monster.PrimaryActiveSkillCastModifier = null;
            monster.UsePrimaryActiveSkillThisAttack = false;
            monster.HasMultiAttackStateDraw = false;
            monster.MultiAttackBaseCount = 0;
            monster.MultiAttackCountRange = 0;

            if (monster.Manipulators == null) return;
            foreach (var manipulator in monster.Manipulators.Values)
            {
                if (!IsActiveSkillManipulatorPath(manipulator?.gcType))
                    continue;

                var skill = CreateMonsterActiveSkillRuntime(manipulator, IsPrimaryActiveSkillManipulator(manipulator));
                skill.CooldownLastTime = GetCombatTime();
                monster.ActiveSkills.Add(skill);
                LogMonsterActiveSkillEffectSupport(monster, skill);

                if (!monster.HasMultiAttackStateDraw &&
                    TryGetManipulatorBool(manipulator, "RepeatAnimation", out bool repeatAnimation) && repeatAnimation)
                {
                    int repeatCount = GetManipulatorInt(manipulator, "RepeatCount", 0);
                    if (repeatCount > 0)
                    {
                        monster.MultiAttackBaseCount = (ushort)Mathf.Clamp(repeatCount, 0, ushort.MaxValue);
                        monster.MultiAttackCountRange = (ushort)Mathf.Clamp(repeatCount, 0, ushort.MaxValue);
                        monster.HasMultiAttackStateDraw = monster.MultiAttackCountRange != 0;
                    }
                }
            }

            MonsterActiveSkillRuntime selected = monster.ActiveSkills.FirstOrDefault(s => s.IsPrimaryAttack)
                ?? monster.ActiveSkills.FirstOrDefault();
            if (selected != null)
                ApplyMonsterActiveSkillSelection(monster, selected);
            Debug.LogError($"[MON-SKILL] build-list {monster.Name}#{monster.EntityId} active={monster.ActiveSkills.Count} primary={monster.PrimaryActiveSkillPath ?? "none"} sourceFunction=MonsterBehavior2::BuildSkillLists");
        }

        private MonsterActiveSkillRuntime CreateMonsterActiveSkillRuntime(ManipulatorData manipulator, bool isPrimaryAttack)
        {
            float cooldown = Mathf.Max(0f, GetManipulatorFloat(manipulator, "CoolDown", 0f));
            bool hasMinimumRange = TryGetManipulatorFloat(manipulator, "SpellUseMinimumRange", out float minimumRange);
            bool hasSelfHealthPct = TryGetManipulatorFloat(manipulator, "SelfHealthPct", out float selfHealthPct);
            bool hasTargetHealthPct = TryGetManipulatorFloat(manipulator, "TargetHealthPct", out float targetHealthPct);
            return new MonsterActiveSkillRuntime
            {
                Path = manipulator.gcType,
                Id = GetManipulatorByte(manipulator, "ID", 10),
                Range = GetManipulatorFloat(manipulator, "Range", 0f),
                SpellUseRange = GetManipulatorFloat(manipulator, "SpellUseRange", 0f),
                SpellUseMinimumRange = hasMinimumRange ? minimumRange : -1f,
                TargetType = GetManipulatorString(manipulator, "TargetType", null),
                SpellUse = GetManipulatorString(manipulator, "SpellUse", null),
                HasSelfHealthPct = hasSelfHealthPct,
                SelfHealthPct = selfHealthPct,
                HasTargetHealthPct = hasTargetHealthPct,
                TargetHealthPct = targetHealthPct,
                CooldownSeconds = cooldown,
                CooldownTicks = (ushort)Mathf.Clamp(Mathf.RoundToInt(cooldown * 30f), 0, ushort.MaxValue),
                CooldownRemainingTicks = 0,
                AnimationId = GetManipulatorInt(manipulator, "AnimationID", 0),
                Effect = GetManipulatorString(manipulator, "Effect", null),
                CastModifier = GetManipulatorString(manipulator, "CastModifier", null),
                IsPrimaryAttack = isPrimaryAttack
            };
        }

        private static void ApplyMonsterActiveSkillSelection(Monster monster, MonsterActiveSkillRuntime skill)
        {
            if (monster == null || skill == null) return;
            monster.SelectedActiveSkill = skill;
            monster.PrimaryActiveSkillPath = skill.Path;
            monster.PrimaryActiveSkillId = skill.Id;
            monster.PrimaryActiveSkillRange = skill.Range;
            monster.PrimaryActiveSkillSpellUseRange = skill.SpellUseRange;
            monster.PrimaryActiveSkillMinimumRange = skill.SpellUseMinimumRange;
            monster.PrimaryActiveSkillTargetType = skill.TargetType;
            monster.PrimaryActiveSkillSpellUse = skill.SpellUse;
            monster.PrimaryActiveSkillHasSelfHealthPct = skill.HasSelfHealthPct;
            monster.PrimaryActiveSkillSelfHealthPct = skill.SelfHealthPct;
            monster.PrimaryActiveSkillHasTargetHealthPct = skill.HasTargetHealthPct;
            monster.PrimaryActiveSkillTargetHealthPct = skill.TargetHealthPct;
            monster.PrimaryActiveSkillCooldownSeconds = skill.CooldownSeconds;
            monster.PrimaryActiveSkillCooldownTicks = skill.CooldownTicks;
            monster.PrimaryActiveSkillCooldownRemainingTicks = skill.CooldownRemainingTicks;
            monster.PrimaryActiveSkillCooldownLastTime = skill.CooldownLastTime;
            monster.PrimaryActiveSkillAnimationId = skill.AnimationId;
            monster.PrimaryActiveSkillEffect = skill.Effect;
            monster.PrimaryActiveSkillCastModifier = skill.CastModifier;
        }

        private bool IsPrimaryActiveSkillManipulator(ManipulatorData manipulator)
        {
            if (manipulator == null || string.IsNullOrWhiteSpace(manipulator.gcType))
                return false;
            if (!IsActiveSkillManipulatorPath(manipulator.gcType))
                return false;
            if (TryGetManipulatorBool(manipulator, "IsPrimaryAttack", out bool primaryFromManipulator))
                return primaryFromManipulator;

            var node = GCDatabase.Instance?.ResolveWithInheritance(manipulator.gcType);
            var desc = node?.GetChild("Description") ?? node;
            return desc != null && desc.GetBool("IsPrimaryAttack", false);
        }

        private bool IsActiveSkillManipulatorPath(string gcType)
        {
            var gc = GCDatabase.Instance;
            string current = gcType;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (!string.IsNullOrWhiteSpace(current) && visited.Add(current))
            {
                if (current.Equals("ActiveSkill", StringComparison.OrdinalIgnoreCase)
                    || current.EndsWith(".ActiveSkill", StringComparison.OrdinalIgnoreCase))
                    return true;

                var node = gc?.Resolve(current);
                current = node?.Extends;
            }
            return !string.IsNullOrEmpty(gcType) && gcType.StartsWith("skills.", StringComparison.OrdinalIgnoreCase);
        }

        private void CopyManipulatorProperties(ManipulatorData data, GCNode node)
        {
            if (data == null || node == null) return;
            foreach (var propertyEntry in node.Properties)
                data.properties[propertyEntry.Key] = propertyEntry.Value;
        }

        private static float GetManipulatorFloat(ManipulatorData manipulator, string property, float fallback)
        {
            if (manipulator?.properties == null || !manipulator.properties.TryGetValue(property, out string raw))
                return fallback;
            return float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value)
                ? value
                : fallback;
        }

        private static bool TryGetManipulatorFloat(ManipulatorData manipulator, string property, out float value)
        {
            value = 0f;
            if (manipulator?.properties == null || !manipulator.properties.TryGetValue(property, out string raw))
                return false;
            return float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        private static int GetManipulatorInt(ManipulatorData manipulator, string property, int fallback)
        {
            if (manipulator?.properties == null || !manipulator.properties.TryGetValue(property, out string raw))
                return fallback;
            return int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int value)
                ? value
                : fallback;
        }

        private static byte GetManipulatorByte(ManipulatorData manipulator, string property, byte fallback)
        {
            int value = GetManipulatorInt(manipulator, property, fallback);
            return (byte)Mathf.Clamp(value, byte.MinValue, byte.MaxValue);
        }

        private static string GetManipulatorString(ManipulatorData manipulator, string property, string fallback)
        {
            if (manipulator?.properties == null || !manipulator.properties.TryGetValue(property, out string raw))
                return fallback;
            return string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim().Trim('"');
        }

        private static bool TryGetManipulatorBool(ManipulatorData manipulator, string property, out bool value)
        {
            value = false;
            if (manipulator?.properties == null || !manipulator.properties.TryGetValue(property, out string raw))
                return false;
            raw = raw?.Trim().Trim('"');
            if (bool.TryParse(raw, out value))
                return true;
            if (int.TryParse(raw, out int intValue))
            {
                value = intValue != 0;
                return true;
            }
            return false;
        }

        private GCNode GetAuthoredWeaponDescription(GCNode creatureNode, string spawnGcType = null, string baseGcType = null)
        {
            var manipulators = creatureNode?.GetChild("Manipulators");
            var weapon = manipulators?.GetChild("PrimaryWeapon") ??
                         ResolveEffectiveAuthoredChild(spawnGcType, "Manipulators.PrimaryWeapon") ??
                         ResolveEffectiveAuthoredChild(baseGcType, "Manipulators.PrimaryWeapon");
            return ResolveAuthoredWeaponDescription(weapon);
        }

        private GCNode GetAuthoredBehaviorDescription(GCNode creatureNode)
        {
            var behavior = creatureNode?.GetChild("Behavior");
            return ResolveAuthoredBehaviorDescription(behavior);
        }

        private GCNode ResolveAuthoredBehaviorDescription(GCNode behaviorNode)
        {
            return ResolveAuthoredBehaviorDescription(behaviorNode, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        private GCNode ResolveAuthoredBehaviorDescription(GCNode behaviorNode, HashSet<string> visited)
        {
            if (behaviorNode == null) return null;
            string key = behaviorNode.Name + "|" + (behaviorNode.Extends ?? "");
            if (!visited.Add(key)) return behaviorNode.GetChild("Description") ?? behaviorNode;

            GCNode baseDescription = null;
            if (!string.IsNullOrEmpty(behaviorNode.Extends))
            {
                var baseBehavior = GCDatabase.Instance?.ResolveWithInheritance(behaviorNode.Extends);
                baseDescription = ResolveAuthoredBehaviorDescription(baseBehavior, visited);
            }

            var description = behaviorNode.GetChild("Description") ?? behaviorNode;
            return baseDescription == null ? description : MergeAuthoredNodes(baseDescription, description);
        }

        private GCNode ResolveAuthoredWeaponDescription(GCNode weaponNode)
        {
            return ResolveAuthoredWeaponDescription(weaponNode, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        private GCNode ResolveAuthoredWeaponDescription(GCNode weaponNode, HashSet<string> visited)
        {
            if (weaponNode == null) return null;
            string key = weaponNode.Name + "|" + (weaponNode.Extends ?? "");
            if (!visited.Add(key)) return weaponNode.GetChild("Description") ?? weaponNode;

            GCNode baseDescription = null;
            if (!string.IsNullOrEmpty(weaponNode.Extends))
            {
                var baseWeapon = GCDatabase.Instance?.ResolveWithInheritance(weaponNode.Extends);
                baseDescription = ResolveAuthoredWeaponDescription(baseWeapon, visited);
            }

            var description = weaponNode.GetChild("Description") ?? weaponNode;
            return baseDescription == null ? description : MergeAuthoredNodes(baseDescription, description);
        }

        private GCNode MergeAuthoredNodes(GCNode parent, GCNode child)
        {
            if (parent == null) return child;
            if (child == null) return parent;

            var merged = new GCNode
            {
                Name = child.Name,
                Extends = child.Extends ?? parent.Extends,
                IsStatic = child.IsStatic || parent.IsStatic,
                IsAnonymous = child.IsAnonymous || parent.IsAnonymous,
                SourceFile = child.SourceFile
            };

            foreach (var propertyEntry in parent.Properties) merged.Properties[propertyEntry.Key] = propertyEntry.Value;
            foreach (var propertyEntry in child.Properties) merged.Properties[propertyEntry.Key] = propertyEntry.Value;

            foreach (var childEntry in parent.Children) merged.Children[childEntry.Key] = childEntry.Value;
            foreach (var childEntry in child.Children)
            {
                merged.Children[childEntry.Key] = merged.Children.TryGetValue(childEntry.Key, out var existing)
                    ? MergeAuthoredNodes(existing, childEntry.Value)
                    : childEntry.Value;
            }

            foreach (var entry in parent.AnonymousChildren) merged.AnonymousChildren.Add(entry);
            foreach (var entry in child.AnonymousChildren) merged.AnonymousChildren.Add(entry);

            return merged;
        }

        private float GetAuthoredFloat(GCNode node, string property, float fallback)
        {
            return node != null && node.HasProperty(property) ? node.GetFloat(property, fallback) : fallback;
        }

        private string GetAuthoredString(GCNode node, string property, string fallback)
        {
            return node != null && node.HasProperty(property) ? node.GetString(property, fallback) : fallback;
        }

        private int GetAuthoredInt(GCNode node, string property, int fallback)
        {
            return node != null && node.HasProperty(property) ? node.GetInt(property, fallback) : fallback;
        }

        private bool GetAuthoredBool(GCNode node, string property, bool fallback)
        {
            return node != null && node.HasProperty(property) ? node.GetBool(property, fallback) : fallback;
        }

        private float GetAuthoredBehaviorFloat(GCNode creatureNode, string property, float fallback)
        {
            var desc = GetAuthoredBehaviorDescription(creatureNode);
            return GetAuthoredFloat(desc, property, fallback);
        }

        private float GetAuthoredBehaviorFloat(GCNode behaviorNode, GCNode creatureNode, string property, float fallback)
        {
            var behaviorDesc = ResolveAuthoredBehaviorDescription(behaviorNode);
            if (behaviorDesc != null && behaviorDesc.HasProperty(property))
                return behaviorDesc.GetFloat(property, fallback);
            return GetAuthoredBehaviorFloat(creatureNode, property, fallback);
        }

        private string GetAuthoredBehaviorString(GCNode behaviorNode, GCNode creatureNode, string property, string fallback)
        {
            var behaviorDesc = ResolveAuthoredBehaviorDescription(behaviorNode);
            if (behaviorDesc != null && behaviorDesc.HasProperty(property))
                return behaviorDesc.GetString(property, fallback);
            var creatureDesc = GetAuthoredBehaviorDescription(creatureNode);
            return GetAuthoredString(creatureDesc, property, fallback);
        }

        private int GetAuthoredBehaviorInt(GCNode behaviorNode, GCNode creatureNode, string property, int fallback)
        {
            var behaviorDesc = ResolveAuthoredBehaviorDescription(behaviorNode);
            if (behaviorDesc != null && behaviorDesc.HasProperty(property))
                return behaviorDesc.GetInt(property, fallback);
            var creatureDesc = GetAuthoredBehaviorDescription(creatureNode);
            return GetAuthoredInt(creatureDesc, property, fallback);
        }

        private bool GetAuthoredBehaviorBool(GCNode behaviorNode, GCNode creatureNode, string property, bool fallback)
        {
            var behaviorDesc = ResolveAuthoredBehaviorDescription(behaviorNode);
            if (behaviorDesc != null && behaviorDesc.HasProperty(property))
                return behaviorDesc.GetBool(property, fallback);
            var creatureDesc = GetAuthoredBehaviorDescription(creatureNode);
            return GetAuthoredBool(creatureDesc, property, fallback);
        }

        private bool ShouldRegisterWander(string idleAction, float wanderRange, string zoneName)
        {
            string action = NormalizeAuthoredEnumToken(idleAction);
            if (!string.IsNullOrEmpty(action))
            {
                if (IsWanderIdleAction(action))
                    return true;
                if (action.Equals("FOLLOW", StringComparison.OrdinalIgnoreCase))
                    return false;
                if (action.Equals("GUARD", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (action.Equals("NOTHING", StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return wanderRange > 0f;
        }

        private static bool IsWanderIdleAction(string idleAction)
        {
            string action = NormalizeAuthoredEnumToken(idleAction);
            return action.Equals("WANDER", StringComparison.OrdinalIgnoreCase) ||
                   action.Equals("3", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeAuthoredEnumToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            return value.Trim().Trim('"', '\'');
        }

        private static bool HasEncounterObject(Monster monster)
        {
            return monster != null && !string.IsNullOrEmpty(monster.EncounterGroupKey);
        }

        private static bool HasWanderEncounterOwner(Monster monster)
        {
            return HasEncounterObject(monster);
        }

        private static bool HasEncounterObjectActiveSearch(Monster monster)
        {
            ApplyEncounterMirror(monster);
            return HasEncounterObject(monster) && monster.EncounterObjectState == 2;
        }

        private static bool HasAlertEncounterRelation(Monster listener, Monster source, out string relation)
        {
            relation = "invalid";
            if (listener == null || source == null)
                return false;
            if (!HasEncounterObject(listener))
            {
                relation = "listener-no-encounter";
                return true;
            }
            if (!HasEncounterObject(source))
            {
                relation = "source-no-encounter";
                return false;
            }
            if (string.Equals(listener.EncounterGroupKey, source.EncounterGroupKey, StringComparison.OrdinalIgnoreCase))
            {
                relation = "same-encounter";
                return true;
            }
            relation = "different-encounter";
            return false;
        }

        private float GetAuthoredLeashRange(GCNode behaviorNode, GCNode creatureNode, string tier)
        {
            if (TryGetAuthoredLeashRange(ResolveAuthoredBehaviorDescription(behaviorNode), out float leashRange))
                return leashRange;
            if (TryGetAuthoredLeashRange(GetAuthoredBehaviorDescription(creatureNode), out leashRange))
                return leashRange;
            return GetLeashRangeForTier(tier);
        }

        private bool TryGetAuthoredLeashRange(GCNode behaviorDesc, out float leashRange)
        {
            leashRange = 0f;
            if (behaviorDesc == null) return false;
            if (behaviorDesc.HasProperty("Leashed") && !behaviorDesc.GetBool("Leashed", true))
                return true;
            if (behaviorDesc.HasProperty("LeashRange"))
            {
                leashRange = behaviorDesc.GetFloat("LeashRange", 0f);
                return true;
            }
            return false;
        }

        private float GetAuthoredMoveSpeed(GCNode authoredDesc, CreatureData creature)
        {
            float speed = GetAuthoredFloat(authoredDesc, "Speed", float.NaN);
            if (!float.IsNaN(speed)) return speed;
            return GetMoveSpeedFromCreature(creature);
        }

        private float GetAuthoredWalkSpeed(GCNode authoredCreature, GCNode authoredDesc, CreatureData creature)
        {
            float speed = GetAuthoredFloat(authoredDesc, "WalkSpeed", float.NaN);
            if (!float.IsNaN(speed)) return speed;
            speed = GetAuthoredFloat(authoredCreature, "WalkSpeed", float.NaN);
            if (!float.IsNaN(speed)) return speed;
            speed = GetAuthoredFloat(GCDatabase.Instance?.ResolveWithInheritance("creatures.base.UnitStock"), "WalkSpeed", float.NaN);
            if (!float.IsNaN(speed)) return speed;
            return Mathf.Min(GetMoveSpeedFromCreature(creature), 25f);
        }

        private float GetAuthoredWanderRange(GCNode behaviorNode, GCNode creatureNode)
        {
            float range = GetAuthoredFloat(creatureNode, "WanderRange", float.NaN);
            if (!float.IsNaN(range)) return range;
            range = GetAuthoredBehaviorFloat(behaviorNode, creatureNode, "WanderRange", float.NaN);
            if (!float.IsNaN(range)) return range;
            return CLIENT_DEFAULT_MONSTER_WANDER_RANGE;
        }

        private float GetAuthoredAttackRange(GCNode authoredDesc, GCNode authoredWeapon, CreatureData creature)
        {
            float weaponRange = GetAuthoredFloat(authoredWeapon, "Range", float.NaN);
            if (!float.IsNaN(weaponRange)) return weaponRange;
            float attackRange = GetAuthoredFloat(authoredDesc, "AttackRange", float.NaN);
            if (!float.IsNaN(attackRange)) return attackRange;
            Debug.LogError($"[AUTHORED-COVERAGE] area=spawn-manipulator status=client-default reason=missing-weapon-range creature='{creature?.gcType ?? ""}' sourceFunction=WeaponDesc::WeaponDesc@0x00599DD0 range=0");
            return 0f;
        }

        private struct AttackTiming
        {
            public float AttackLeadDelay;
            public float AttackSoundLeadDelay;
            public int[] TotalFrames;
            public int[] HitFrames;
            public int[] SoundFrames;
            public bool[] VariantResolved;
            public ResolutionSource Source;
            public string Reason;
        }

        private static void ResolveAttackDefaultFrames(bool useProjectile, out int totalFrames, out int hitFrame, out int soundFrame)
        {
            if (useProjectile)
            {
                totalFrames = 10;
                hitFrame = 5;
                soundFrame = 15;
                return;
            }

            totalFrames = 30;
            hitFrame = 15;
            soundFrame = 10;
        }

        private static void ApplyAttackDefaultFrame(AttackTiming timing, int variant, bool useProjectile)
        {
            ResolveAttackDefaultFrames(useProjectile, out int totalFrames, out int hitFrame, out int soundFrame);
            timing.TotalFrames[variant] = totalFrames;
            timing.HitFrames[variant] = hitFrame;
            timing.SoundFrames[variant] = soundFrame;
            timing.VariantResolved[variant] = true;
        }

        private static void ApplyAttackDefaultFrames(AttackTiming timing, bool useProjectile)
        {
            for (int variantIndex = 0; variantIndex < timing.VariantResolved.Length; variantIndex++)
                ApplyAttackDefaultFrame(timing, variantIndex, useProjectile);
        }

        private AttackTiming ResolveAttackTiming(GCNode authoredDesc, bool weaponUsesProjectile, float attackCooldown, string source)
        {
            ResolveAttackDefaultFrames(weaponUsesProjectile, out int defaultTotalFrames, out int defaultHitFrame, out int defaultSoundFrame);
            var timing = new AttackTiming
            {
                AttackLeadDelay = Mathf.Max(1f / 30f, defaultHitFrame / 30f),
                AttackSoundLeadDelay = Mathf.Max(1f / 30f, defaultSoundFrame / 30f),
                TotalFrames = new[] { 0, 0, 0 },
                HitFrames = new[] { 0, 0, 0 },
                SoundFrames = new[] { 0, 0, 0 },
                VariantResolved = new[] { false, false, false },
                Source = ResolutionSource.Blocked,
                Reason = "unresolved"
            };

            string animationsPath = GetAuthoredString(authoredDesc, "Animations", "");
            if (string.IsNullOrEmpty(animationsPath))
            {
                ApplyAttackDefaultFrames(timing, weaponUsesProjectile);
                timing.Source = ResolutionSource.Client;
                timing.Reason = "client-default-missing-animations-path";
                Debug.LogError($"[ATTACK-TIMING] source='{source ?? "unknown"}' reason=missing-animations-path sourceKind={timing.Source} frames={defaultTotalFrames}/{defaultHitFrame}/{defaultSoundFrame} sourceFunction={(weaponUsesProjectile ? "RangedWeapon::update projectile" : "MeleeWeapon::update")}");
                return timing;
            }

            var animations = GCDatabase.Instance.ResolveWithInheritance(animationsPath);
            if (animations == null || animations.AnonymousChildren == null || animations.AnonymousChildren.Count == 0)
            {
                ApplyAttackDefaultFrames(timing, weaponUsesProjectile);
                timing.Source = ResolutionSource.Client;
                timing.Reason = "client-default-empty-animation-node";
                Debug.LogError($"[ATTACK-TIMING] source='{source ?? "unknown"}' path='{animationsPath}' reason=animations-node-empty sourceKind={timing.Source} frames={defaultTotalFrames}/{defaultHitFrame}/{defaultSoundFrame} sourceFunction={(weaponUsesProjectile ? "RangedWeapon::update projectile" : "MeleeWeapon::update")}");
                return timing;
            }

            var animationById = new Dictionary<int, GCNode>();
            foreach (var animation in animations.AnonymousChildren)
            {
                if (animation == null) continue;
                int id = animation.GetInt("ID", 0);
                if (id > 0 && !animationById.ContainsKey(id))
                    animationById[id] = animation;
            }

            bool foundAny = false;
            bool invalidAny = false;
            GCNode leadRow = null;
            for (int variant = 0; variant < 3; variant++)
            {
                int normalId = 110 + variant;
                int specialId = 510 + variant;
                GCNode row = null;
                int resolvedId = 0;
                if (animationById.TryGetValue(normalId, out row))
                    resolvedId = normalId;
                else if (animationById.TryGetValue(specialId, out row))
                    resolvedId = specialId;

                if (row == null)
                {
                    ApplyAttackDefaultFrame(timing, variant, weaponUsesProjectile);
                    foundAny = true;
                    Debug.LogError($"[ATTACK-TIMING-ROW] source='{source ?? "unknown"}' path='{animationsPath}' variant={variant} animationId=missing total={timing.TotalFrames[variant]} hit={timing.HitFrames[variant]} sound={timing.SoundFrames[variant]} sourceKind={ResolutionSource.Client} reason=client-default-missing-animation");
                    continue;
                }

                int total = row.GetInt("NumFrames", 0);
                int hit = row.GetInt("TriggerTime", 0);
                int sound = row.GetInt("SoundTriggerTime", 0);
                if (total <= 0 || hit <= 0 || sound <= 0)
                {
                    invalidAny = true;
                    RuntimeEvidence.LogFallbackHit(
                        "attack-animation",
                        "invalid-authored-attack-frames",
                        $"source='{source ?? "unknown"}' path='{animationsPath}' variant={variant} animationId={resolvedId} total={total} hit={hit} sound={sound}",
                        32);
                    continue;
                }
                timing.TotalFrames[variant] = total;
                timing.HitFrames[variant] = hit;
                timing.SoundFrames[variant] = sound;
                timing.VariantResolved[variant] = true;
                if (leadRow == null)
                    leadRow = row;
                foundAny = true;
                Debug.LogError($"[ATTACK-TIMING-ROW] source='{source ?? "unknown"}' path='{animationsPath}' variant={variant} animationId={resolvedId} total={timing.TotalFrames[variant]} hit={timing.HitFrames[variant]} sound={timing.SoundFrames[variant]} sourceKind={ResolutionSource.Client}");
            }

            if (foundAny && !invalidAny)
            {
                if (leadRow != null)
                {
                    float trigger = leadRow.GetFloat("TriggerTime", float.NaN);
                    if (!float.IsNaN(trigger) && trigger > 0f)
                        timing.AttackLeadDelay = Mathf.Max(1f / 30f, trigger / 30f);
                    float soundTrigger = leadRow.GetFloat("SoundTriggerTime", float.NaN);
                    if (!float.IsNaN(soundTrigger) && soundTrigger > 0f)
                        timing.AttackSoundLeadDelay = Mathf.Max(1f / 30f, soundTrigger / 30f);
                }
                timing.Source = ResolutionSource.Client;
                timing.Reason = "animation-id-map";
            }
            else if (!foundAny)
            {
                ApplyAttackDefaultFrames(timing, weaponUsesProjectile);
                timing.Source = ResolutionSource.Client;
                timing.Reason = "client-default-missing-attack-trigger";
            }
            else
            {
                timing.Source = ResolutionSource.Blocked;
                timing.Reason = "invalid-authored-attack-frames";
            }

            Debug.LogError($"[ATTACK-TIMING] source='{source ?? "unknown"}' path='{animationsPath}' lead={timing.AttackLeadDelay:F3} soundLead={timing.AttackSoundLeadDelay:F3} total=[{string.Join(",", timing.TotalFrames)}] hit=[{string.Join(",", timing.HitFrames)}] sound=[{string.Join(",", timing.SoundFrames)}] sourceKind={timing.Source} reason={timing.Reason}");
            return timing;
        }

        private float GetAuthoredAttackLeadDelay(GCNode authoredDesc, float fallback, out float soundLeadDelay)
        {
            soundLeadDelay = Mathf.Max(0.1f, fallback * (10f / 15f));
            string animationsPath = GetAuthoredString(authoredDesc, "Animations", "");
            if (string.IsNullOrEmpty(animationsPath)) return fallback;

            var animations = GCDatabase.Instance.ResolveWithInheritance(animationsPath);
            if (animations == null || animations.AnonymousChildren == null || animations.AnonymousChildren.Count == 0) return fallback;

            float firstTrigger = float.NaN;
            foreach (var animation in animations.AnonymousChildren)
            {
                float trigger = animation.GetFloat("TriggerTime", float.NaN);
                if (float.IsNaN(trigger) || trigger <= 0f) continue;
                if (float.IsNaN(firstTrigger)) firstTrigger = trigger;
                int id = animation.GetInt("ID", 0);
                if (id >= 110 && id <= 119)
                {
                    float soundTrigger = animation.GetFloat("SoundTriggerTime", float.NaN);
                    if (!float.IsNaN(soundTrigger) && soundTrigger > 0f)
                        soundLeadDelay = Mathf.Max(0.1f, soundTrigger / 30f);
                    return Mathf.Max(0.1f, trigger / 30f);
                }
            }

            return float.IsNaN(firstTrigger) ? fallback : Mathf.Max(0.1f, firstTrigger / 30f);
        }

        private int[] GetAuthoredAttackFrames(GCNode authoredDesc, string property, int fallback)
        {
            int[] values = { fallback, fallback, fallback };
            string animationsPath = GetAuthoredString(authoredDesc, "Animations", "");
            if (string.IsNullOrEmpty(animationsPath)) return values;
            var animations = GCDatabase.Instance.ResolveWithInheritance(animationsPath);
            if (animations?.AnonymousChildren == null) return values;
            foreach (var animation in animations.AnonymousChildren)
            {
                int id = animation.GetInt("ID", 0);
                int index = id >= 110 && id <= 112 ? id - 110 : (id >= 510 && id <= 512 ? id - 510 : -1);
                if (index < 0 || index >= values.Length) continue;
                int value = animation.GetInt(property, fallback);
                if (value > 0) values[index] = value;
            }
            return values;
        }

        private int CountAuthoredSounds(GCNode authoredDesc, string soundId)
        {
            string soundsPath = GetAuthoredString(authoredDesc, "Sounds", "");
            if (string.IsNullOrEmpty(soundsPath)) return 0;
            var sounds = GCDatabase.Instance.ResolveWithInheritance(soundsPath);
            if (sounds?.AnonymousChildren == null) return 0;
            foreach (var sound in sounds.AnonymousChildren)
            {
                string current = sound.GetString("SoundId", "");
                if (!string.Equals(current, soundId, StringComparison.OrdinalIgnoreCase)) continue;
                string list = sound.GetString("Sounds", "");
                if (string.IsNullOrWhiteSpace(list)) return 0;
                int count = 0;
                foreach (string item in list.Split(','))
                {
                    if (!string.IsNullOrWhiteSpace(item.Trim().Trim('"'))) count++;
                }
                return count;
            }
            return 0;
        }

        private bool HasAuthoredAttackSound(GCNode authoredDesc)
        {
            return CountAuthoredSounds(authoredDesc, "ATTACK") > 0 || CountAuthoredSounds(authoredDesc, "WEAPONATTACK") > 0;
        }

        private float GetManipulatorFloat(CreatureData creature, string propName, float fallback)
        {
            if (creature.manipulators == null) return fallback;
            foreach (var manip in creature.manipulators.Values)
            {
                if (manip.properties != null && manip.properties.TryGetValue(propName, out string val))
                {
                    if (float.TryParse(val, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float result))
                        return result;
                }
            }
            return fallback;
        }
        public List<Monster> SpawnFactionGroup(string faction, string tier, float centerX, float centerY, float centerZ, int count, float radius = 10f, string zoneName = null)
        {
            var spawned = new List<Monster>();
            var creatures = DatabaseLoader.GetCreaturesByFaction(faction);

            if (!string.IsNullOrEmpty(tier))
                creatures = creatures.FindAll(c => c.tier.Equals(tier, StringComparison.OrdinalIgnoreCase));

            if (creatures.Count == 0)
            {
                Debug.LogError($"[COMBAT] faction='{faction}' tier='{tier}' state=noCreatures");
                return spawned;
            }

            for (int spawnIndex = 0; spawnIndex < count; spawnIndex++)
            {
                var randomCreature = creatures[DungeonRunners.Engine.Random.Range(0, creatures.Count)];
                float angle = DungeonRunners.Engine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float dist = DungeonRunners.Engine.Random.Range(0f, radius);
                float spawnX = centerX + Mathf.Cos(angle) * dist;
                float spawnZ = centerZ + Mathf.Sin(angle) * dist;

                var monster = SpawnMonster(randomCreature.gcType, spawnX, centerY, spawnZ, angle * Mathf.Rad2Deg, zoneName);
                if (monster != null)
                    spawned.Add(monster);
            }

            return spawned;
        }
        public Monster GetMonsterByComponent(uint componentId)
        {
            if (_componentToEntityMap.TryGetValue(componentId, out uint entityId))
                return GetMonster(entityId);
            return null;
        }
        public void ResetAllMonsters()
        {
            foreach (var monsterEntry in _activeMonsters)
            {
                monsterEntry.Value.IsAlive = true;
                SetRuntimeMonsterHPWire(monsterEntry.Value, monsterEntry.Value.MaxHPWire, false, Time.time, "RESET");
                var state = GetMonsterHPState(monsterEntry.Value);
                state.LastClientHPReportTime = 0f;
                state.LastClientHPReportWire = monsterEntry.Value.MaxHPWire;
                ApplyMonsterHPState(monsterEntry.Value, state);
                monsterEntry.Value.UseTargetCount = 0;
                monsterEntry.Value.AlertSourceEntityId = 0;
            }
            Debug.LogError($"[COMBAT] resetForZoneTransition monsters={_activeMonsters.Count}");
        }
        public int GetComponentOffset(uint componentId)
        {
            if (_componentToEntityMap.TryGetValue(componentId, out uint entityId))
                return (int)(componentId - entityId);
            return -1;
        }
        public void DespawnMonster(uint entityId, bool allowRespawn = true, float respawnDelaySeconds = 30f)
        {
            if (!_activeMonsters.TryGetValue(entityId, out var monster))
                return;

            EncounterOnUnitRemoved(monster, allowRespawn ? "despawn-respawn" : "despawn");
            RemovePlayerModifiersFromSource(monster.EntityId, allowRespawn ? "despawn-respawn" : "despawn");

            _componentToEntityMap.Remove(monster.EntityId);
            _componentToEntityMap.Remove(monster.BehaviorId);
            _componentToEntityMap.Remove(monster.SkillsId);
            _componentToEntityMap.Remove(monster.ManipulatorsId);
            _componentToEntityMap.Remove(monster.ModifiersId);
            _componentToEntityMap.Remove(monster.UnitId);
            _monsterRuntimeHPWire.Remove(entityId);
            _monsterHPStates.Remove(entityId);
            _monsterRuntimeDamageCommitted.Remove(entityId);
            _monsterHPRegenLastTime.Remove(entityId);
            _monsterHPRegenCarryWire.Remove(entityId);
            _monsterHPRegenCooldownTicks.Remove(entityId);
            _monsterManaRegenLastTime.Remove(entityId);
            _monsterManaRegenCooldownTicks.Remove(entityId);
            _monsterDeathUpdateAccum.Remove(entityId);
            _monsterStateTraceSignatures.Remove(entityId);
            ClearMonsterProjectilesForMonster(entityId);

            WanderSimulator.Instance.UnregisterEntity(entityId);
            OnMonsterDespawned?.Invoke(monster);
            UnregisterEntityOrder(entityId);
            _activeMonsters.Remove(entityId);

            if (allowRespawn)
            {
                _respawnQueue.Add(new RespawnEntry
                {
                    GCType = monster.GCType,
                    ZoneName = monster.ZoneName,
                    InstanceKey = monster.InstanceKey,
                    PosX = monster.SpawnPosX,
                    PosY = monster.SpawnPosY,
                    PosZ = monster.SpawnPosZ,
                    Heading = monster.Heading,
                    EncounterGroupKey = monster.EncounterGroupKey,
                    EncounterDifficulty = monster.ExperienceDifficulty,
                    RespawnTime = Time.time + Mathf.Max(0f, respawnDelaySeconds)
                });
            }
        }
        public void RemoveComponentMapping(uint componentId)
        {
            _componentToEntityMap.Remove(componentId);
        }

        public void AddComponentMapping(uint componentId, uint entityId)
        {
            _componentToEntityMap[componentId] = entityId;
        }
        public Monster GetMonster(uint entityId)
        {
            return _activeMonsters.TryGetValue(entityId, out var m) ? m : null;
        }

        public bool IsMonster(uint entityId)
        {
            return _activeMonsters.ContainsKey(entityId);
        }

        public IEnumerable<Monster> GetActiveMonsters() => _activeMonsters.Values;

        public Monster GetNearestMonster(float posX, float posY, float maxRange = 50f)
        {
            return GetNearestMonster(posX, posY, maxRange, null);
        }

        public Monster GetNearestMonster(float posX, float posY, float maxRange, string instanceKey)
        {
            Monster nearest = null;
            float nearestDist = maxRange * maxRange;
            string normalizedInstanceKey = string.IsNullOrWhiteSpace(instanceKey)
                ? null
                : RoomRuntime.NormalizeInstanceKey(instanceKey);

            Debug.LogError($"[GET-NEAREST] pos=({posX:F1},{posY:F1}) range={maxRange} instance={normalizedInstanceKey ?? "any"} monsters={_activeMonsters.Count}");

            foreach (var monster in _activeMonsters.Values)
            {
                if (!IsMonsterCombatSelectable(monster)) continue;
                if (!MatchesInstance(monster, normalizedInstanceKey)) continue;

                float dx = monster.PosX - posX;
                float dy = monster.PosY - posY;
                float distSq = dx * dx + dy * dy;
                float dist = Mathf.Sqrt(distSq);

                Debug.LogError($"[GET-NEAREST] candidate='{monster.Name}' pos=({monster.PosX:F1},{monster.PosY:F1}) dist={dist:F1}");

                if (distSq < nearestDist)
                {
                    nearestDist = distSq;
                    nearest = monster;
                }
            }

            if (nearest != null)
                Debug.LogError($"[GET-NEAREST] result='{nearest.Name}' pos=({nearest.PosX:F1},{nearest.PosY:F1})");
            else
                Debug.LogError($"[GET-NEAREST] result=none range={maxRange}");

            return nearest;
        }

        private bool IsMonsterCombatSelectable(Monster monster)
        {
            return monster != null && monster.IsAlive && GetRuntimeMonsterHPWire(monster, "SELECT") > 0;
        }

        public bool MatchesInstance(Monster monster, string instanceKey)
        {
            if (monster == null)
                return false;
            if (string.IsNullOrWhiteSpace(instanceKey))
                return true;
            string normalizedInstanceKey = string.IsNullOrWhiteSpace(instanceKey)
                ? null
                : RoomRuntime.NormalizeInstanceKey(instanceKey);
            string monsterKey = RoomRuntime.NormalizeInstanceKey(monster.InstanceKey);
            return string.Equals(monsterKey, normalizedInstanceKey, StringComparison.OrdinalIgnoreCase);
        }

        public Monster FindMonsterForTarget(ushort clientTargetId, float playerPosX, float playerPosY, string instanceKey = null)
        {
            Debug.LogError($"[COMBAT] findMonsterForTarget target={clientTargetId} player=({playerPosX:F1},{playerPosY:F1}) active={_activeMonsters.Count}");

            var monster = GetMonster(clientTargetId);
            if (monster != null)
            {
                if (!IsMonsterCombatSelectable(monster))
                {
                    Debug.LogError($"[COMBAT] target={clientTargetId} resolved=entityId monster='{monster.Name}' alive={monster.IsAlive} hp={monster.CurrentHPWire / 256}/{monster.MaxHPWire / 256}");
                    return null;
                }
                Debug.LogError($"[COMBAT] target={clientTargetId} resolved=entityId");
                return monster;
            }

            if (_componentToEntityMap.TryGetValue(clientTargetId, out uint entityId))
            {
                monster = GetMonster(entityId);
                if (monster != null)
                {
                    if (!IsMonsterCombatSelectable(monster))
                    {
                        Debug.LogError($"[COMBAT] targetComponent={clientTargetId} monster='{monster.Name}' alive={monster.IsAlive} hp={monster.CurrentHPWire / 256}/{monster.MaxHPWire / 256}");
                        return null;
                    }
                    Debug.LogError($"[COMBAT] target={clientTargetId} resolved=componentId entityId={entityId}");
                    return monster;
                }
            }

            if (_clientToServerIdMap.TryGetValue(clientTargetId, out uint serverId))
            {
                monster = GetMonster(serverId);
                if (monster != null)
                {
                    if (!IsMonsterCombatSelectable(monster))
                    {
                        Debug.LogError($"[COMBAT] learnedTarget={clientTargetId} monster='{monster.Name}' alive={monster.IsAlive} hp={monster.CurrentHPWire / 256}/{monster.MaxHPWire / 256}");
                        return null;
                    }
                    Debug.LogError($"[COMBAT] target={clientTargetId} resolved=learnedClientId entityId={serverId}");
                    return monster;
                }
            }

            Debug.LogError($"[COMBAT] target={clientTargetId} state=notFound action=nearbyOnly");
            monster = GetNearestMonster(playerPosX, playerPosY, 30f, instanceKey);

            if (monster != null)
            {
                _clientToServerIdMap[clientTargetId] = monster.EntityId;
                Debug.LogError($"[COMBAT] learnedMapping clientId={clientTargetId} entityId={monster.EntityId} monster='{monster.Name}'");
            }

            return monster;
        }

        public DamageResult ApplyDamage(uint attackerId, uint defenderId, int damageAmount)
        {
            var monster = GetMonster(defenderId);
            uint damageWire = (uint)Math.Max(1, damageAmount) * 256u;
            bool applied = ApplyPlayerDamageToMonsterWire(monster, damageWire, "ApplyDamage", out uint oldHPWire, out uint newHPWire, out bool died);
            if (!applied)
                return new DamageResult { Success = false };
            int appliedDamage = (int)(((oldHPWire > newHPWire ? oldHPWire - newHPWire : 0u) + 255u) / 256u);

            return new DamageResult
            {
                Success = true,
                DamageDealt = appliedDamage,
                IsCritical = false,
                DefenderDied = died,
                NewHPWire = newHPWire
            };
        }

        private float Distance2D(float x1, float y1, float x2, float y2)
        {
            float dx = x2 - x1;
            float dy = y2 - y1;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private float ResolveMonsterEffectiveAttackRange(Monster monster)
        {
            if (monster == null || monster.AttackRange <= 0f) return 0f;
            return monster.AttackRange + Mathf.Max(0f, monster.CollisionRadius) + ResolveAvatarCombatRadius();
        }

        private float ResolveMonsterManipulatorUseRange(Monster monster)
        {
            if (monster == null) return 0f;
            float range = monster.WeaponRange > 0f ? monster.WeaponRange : monster.AttackRange;
            if (range <= 0f) return 0f;
            return range + Mathf.Max(0f, monster.ClientSyncTolerance);
        }

        public float GetMonsterEffectiveAttackRange(Monster monster)
        {
            return ResolveMonsterEffectiveAttackRange(monster);
        }

        public float ResolvePlayerMeleeRange(PlayerState state, Monster monster)
        {
            float weaponRange = state != null && state.WeaponRange > 0 ? state.WeaponRange : monster != null ? monster.AttackRange : 0f;
            float monsterRadius = monster != null ? Mathf.Max(0f, monster.CollisionRadius) : 0f;
            return Mathf.Max(1f, weaponRange) + ResolveAvatarCombatRadius() + monsterRadius;
        }

        public float ResolvePlayerMeleeContactRange(PlayerState state, Monster monster)
        {
            return ResolvePlayerMeleeRange(state, monster);
        }

        public float ResolvePlayerRangedProjectileRange(PlayerState state, Monster monster)
        {
            float range = ResolvePlayerMeleeRange(state, monster);
            if (state == null || !DamageResolver.IsProjectileWeapon(state))
                return range;

            float projectileSize = Mathf.Max(0f, state.WeaponProjectileSize);
            float firstTickTravel = state.WeaponProjectileSpeed > 0f ? Mathf.Max(0f, state.WeaponProjectileSpeed) * CLIENT_UNIT_TICK_INTERVAL : 0f;
            return range + projectileSize + firstTickTravel;
        }

        public float ResolvePlayerRangedWeaponUseRange(PlayerState state)
        {
            if (state != null && state.WeaponRange > 0f)
                return state.WeaponRange;
            return 1f;
        }

        public const float DefaultManipulatorInitUseRange = 64000f;

        public float ResolveUseTargetInitUseRange(PlayerState state, Monster monster, out float clientTolerance, out string source)
        {
            clientTolerance = 0f;
            source = "ManipulatorDesc+0x6c init-use reach reader=UseTarget::CheckInitUse@0x00548980";
            if (state != null && DamageResolver.IsProjectileWeapon(state))
            {
                float rangedInitUse = ResolvePlayerRangedWeaponUseRange(state);
                return rangedInitUse > 0f ? rangedInitUse : DefaultManipulatorInitUseRange;
            }

            return DefaultManipulatorInitUseRange;
        }

        public bool EvaluateUseTargetInitUse(float actorX, float actorY, float targetX, float targetY,
            float initUseRange, float clientTolerance, out float distance, out long distanceSqFixed8, out long thresholdSqFixed8)
        {
            float dx = targetX - actorX;
            float dy = targetY - actorY;
            distance = Mathf.Sqrt((dx * dx) + (dy * dy));

            int dxFixed = Mathf.RoundToInt(dx * 256f);
            int dyFixed = Mathf.RoundToInt(dy * 256f);
            distanceSqFixed8 = (((long)dxFixed * dxFixed) >> 8) + (((long)dyFixed * dyFixed) >> 8);

            float effectiveRange = Mathf.Max(0f, initUseRange + clientTolerance);
            int rangeFixed = Mathf.RoundToInt(effectiveRange * 256f);
            thresholdSqFixed8 = ((long)rangeFixed * rangeFixed) >> 8;
            return thresholdSqFixed8 > 0 && distanceSqFixed8 <= thresholdSqFixed8;
        }

        private float ResolveClientContactRange(Monster monster, CombatPlayer player, float allowedRange)
        {
            if (monster == null || player == null) return 0f;
            return allowedRange;
        }

        private static string ResolveMonsterPathMapKey(Monster monster)
        {
            if (!string.IsNullOrWhiteSpace(monster?.InstanceKey))
                return monster.InstanceKey;
            return monster?.ZoneName;
        }

        private bool IsMonsterAttackPathClear(Monster monster, CombatPlayer target, string source)
        {
            if (monster == null || target == null) return false;
            string pathMapKey = ResolveMonsterPathMapKey(monster);
            if (string.IsNullOrWhiteSpace(pathMapKey)) return true;
            PathMap pathMap = PathMapCatalog.Instance.GetPathMap(pathMapKey);
            if (pathMap == null) return true;
            if (!pathMap.TryCanReachPoint(monster.PosX, monster.PosY, target.PosX, target.PosY, out bool clear))
            {
                if (!string.IsNullOrEmpty(source))
                    Debug.LogError($"[MON-LOS] {monster.Name}#{monster.EntityId}->{target.Name} pathCoverage=False source={source} pathKey='{pathMapKey}' path=({monster.PosX:F1},{monster.PosY:F1})->({target.PosX:F1},{target.PosY:F1}) action=client-unblocked");
                return true;
            }
            if (!clear && !string.IsNullOrEmpty(source))
                Debug.LogError($"[MON-LOS] {monster.Name}#{monster.EntityId}->{target.Name} worldBlocked=True source={source} pathKey='{pathMapKey}' path=({monster.PosX:F1},{monster.PosY:F1})->({target.PosX:F1},{target.PosY:F1})");
            return clear;
        }

        private void ClearMonsterCombatContact(Monster monster, CombatPlayer target)
        {
            if (monster == null || target == null) return;
            if (monster.CombatContactTargetId != target.EntityId) return;
            monster.CombatContactTargetId = 0;
            monster.CombatContactUntil = 0f;
        }

        private bool IsClientCombatContact(Monster monster, CombatPlayer target, float dist, float allowedRange)
        {
            if (!IsMonsterAttackPathClear(monster, target, null))
            {
                ClearMonsterCombatContact(monster, target);
                return false;
            }
            if (HasCombatContact(monster, target)) return true;
            if (monster == null || target == null) return false;
            float contactRange = ResolveClientContactRange(monster, target, allowedRange);
            return contactRange > 0f && dist <= contactRange + CLIENT_CONTACT_RANGE_EPSILON;
        }

        private bool HasMonsterTargetAction(Monster monster, CombatPlayer target, float dist)
        {
            if (monster == null || target == null) return false;
            if (!monster.AggroTriggered || monster.TargetId != target.EntityId) return false;
            float targetRange = ResolveMonsterTargetSearchRange(monster, true);
            return targetRange <= 0f || dist <= targetRange + CLIENT_CONTACT_RANGE_EPSILON;
        }

        private bool HasMonsterWeaponRuntimeReach(Monster monster, CombatPlayer target, float dist, float allowedRange, bool clientContact)
        {
            if (monster == null || target == null) return false;
            float runtimeRange = ResolveMonsterManipulatorUseRange(monster);
            if (runtimeRange <= 0f)
                runtimeRange = allowedRange;
            return runtimeRange > 0f && dist <= runtimeRange + CLIENT_CONTACT_RANGE_EPSILON;
        }

        private bool HasMonsterInitUseGateway(Monster monster, CombatPlayer target)
        {
            if (monster == null || target == null) return false;
            float mx = monster.PosX;
            float my = monster.PosY;
            TryGetMonsterClientVisiblePosition(monster, GetCombatTime(), out mx, out my);
            float initUseRange = ResolveMonsterManipulatorUseRange(monster);
            if (initUseRange <= 0f)
                return false;
            return EvaluateUseTargetInitUse(mx, my, target.PosX, target.PosY,
                initUseRange, 0f, out _, out _, out _);
        }

        private float ResolveAvatarCombatRadius()
        {
            if (_avatarCombatRadius.HasValue) return _avatarCombatRadius.Value;
            float radius = 3f;
            var avatar = GCDatabase.Instance?.ResolveWithInheritance("avatar.base.avatar");
            var desc = avatar?.GetChild("Description") ?? avatar;
            radius = GetAuthoredFloat(desc, "CollisionRadius", radius);
            var bounds = avatar?.GetChild("Object")?.GetChild("Description") ?? avatar?.GetChild("Object");
            if (bounds != null)
            {
                float boundsRadius = Mathf.Max(
                    Mathf.Abs(GetAuthoredFloat(bounds, "MinX", 0f)),
                    Mathf.Abs(GetAuthoredFloat(bounds, "MaxX", 0f)),
                    Mathf.Abs(GetAuthoredFloat(bounds, "MinY", 0f)),
                    Mathf.Abs(GetAuthoredFloat(bounds, "MaxY", 0f)));
                if (boundsRadius > 0f) radius = boundsRadius;
            }
            _avatarCombatRadius = Mathf.Max(0f, radius);
            return _avatarCombatRadius.Value;
        }

        private int ResolveMonsterAttackWindupTicks(Monster monster)
        {
            ResolveMonsterAttackAnimationFrames(monster, out int totalFrames, out int hitFrame, out int soundFrame);
            return Mathf.Max(1, ResolveMonsterAttackFrameTicks(monster, hitFrame));
        }

        private float ResolveMonsterAttackWindup(Monster monster)
        {
            return ResolveMonsterAttackWindupTicks(monster) / 30f;
        }

        private int ResolveMonsterAttackSoundDelayTicks(Monster monster, int windupTicks)
        {
            ResolveMonsterAttackAnimationFrames(monster, out int totalFrames, out int hitFrame, out int soundFrame);
            int soundTicks = ResolveMonsterAttackFrameTicks(monster, soundFrame);
            return Mathf.Clamp(soundTicks, 1, Mathf.Max(1, windupTicks));
        }

        private float ResolveMonsterAttackSoundDelay(Monster monster, float windup)
        {
            ResolveMonsterAttackAnimationFrames(monster, out int totalFrames, out int hitFrame, out int soundFrame);
            float soundDelay = ResolveMonsterAttackFrameSeconds(monster, soundFrame);
            return Mathf.Clamp(soundDelay, 1f / 30f, Mathf.Max(1f / 30f, windup));
        }

        private void ResolveMonsterAttackAnimationFrames(Monster monster, out int totalFrames, out int hitFrame, out int soundFrame)
        {
            int attackIndex = monster != null ? Mathf.Clamp(monster.AttackAnimationIndex, 0, 2) : 0;
            bool useProjectile = monster != null && monster.WeaponUsesProjectile;
            ResolveAttackDefaultFrames(useProjectile, out int defaultTotalFrames, out int defaultHitFrame, out int defaultSoundFrame);
            totalFrames = monster?.AttackTotalFrames != null && monster.AttackTotalFrames.Length > attackIndex ? monster.AttackTotalFrames[attackIndex] : defaultTotalFrames;
            hitFrame = monster?.AttackHitFrames != null && monster.AttackHitFrames.Length > attackIndex ? monster.AttackHitFrames[attackIndex] : defaultHitFrame;
            soundFrame = monster?.AttackSoundFrames != null && monster.AttackSoundFrames.Length > attackIndex ? monster.AttackSoundFrames[attackIndex] : defaultSoundFrame;
            bool resolved = monster?.AttackFrameResolved != null &&
                monster.AttackFrameResolved.Length > attackIndex &&
                monster.AttackFrameResolved[attackIndex] &&
                totalFrames > 0 && hitFrame > 0 && soundFrame > 0;
            if (!resolved)
            {
                totalFrames = defaultTotalFrames;
                hitFrame = defaultHitFrame;
                soundFrame = defaultSoundFrame;
                Debug.LogError($"[ATTACK-TIMING] monster={monster?.Name ?? "<null>"} gc={monster?.SpawnGCType ?? monster?.GCType ?? "<null>"} variant={attackIndex} sourceKind={ResolutionSource.Client} reason={monster?.AttackTimingReason ?? "unresolved"} frames={totalFrames}/{hitFrame}/{soundFrame} sourceFunction={(useProjectile ? "RangedWeapon::update projectile" : "MeleeWeapon::update")}");
                return;
            }
        }

        private int ResolveMonsterAttackFrameTicks(Monster monster, int frame)
        {
            float authoredSpeed = GCDatabase.Instance.GetKnob("MonsterAttackSpeed", 100f);
            float attackSpeed = authoredSpeed;
            if (monster != null)
                attackSpeed *= Mathf.Max(0.01f, monster.AttackSpeed);
            int speedField = Mathf.Max(1, Mathf.RoundToInt(attackSpeed));
            return Mathf.Max(1, (frame * 100) / speedField);
        }

        private float ResolveMonsterAttackFrameSeconds(Monster monster, int frame)
        {
            return ResolveMonsterAttackFrameTicks(monster, frame) / 30f;
        }

        private int ResolveMonsterAttackTotalTicks(Monster monster)
        {
            ResolveMonsterAttackAnimationFrames(monster, out int totalFrames, out int hitFrame, out int soundFrame);
            return Mathf.Max(1, ResolveMonsterAttackFrameTicks(monster, totalFrames));
        }

        private int ResolveMonsterAttackCooldownTicks(Monster monster)
        {
            float cooldown = monster != null ? monster.AttackCooldown : 1.75f;
            int cooldownFixed256 = Mathf.Max(0, Mathf.RoundToInt(Mathf.Max(0.01f, cooldown) * 256f));
            return Mathf.Max(1, (int)(((long)cooldownFixed256 * 0x1e00L) >> 16));
        }

        private float ResolveMonsterAttackCooldownSeconds(Monster monster)
        {
            return ResolveMonsterAttackCooldownTicks(monster) / 30f;
        }

        private void AdvanceMonsterAttackAnimation(Monster monster)
        {
            MersenneTwister rng = GetRoomRngForMonster(monster);
            if (monster == null || rng == null) return;
            uint useRaw = RngLedger.Generate(rng, "room", "monster-attack:use-animation", monster.InstanceKey ?? monster.ZoneName);
            uint previous = monster.AttackAnimationIndex;
            monster.AttackUseRaw = useRaw;
            monster.AttackAnimationIndex = (byte)(((useRaw & 1u) + previous + 1u) % 3u);
        }

        private void ConsumeMonsterAttackSoundRng(Monster monster)
        {
            if (monster == null || !monster.AttackSoundPending) return;
            monster.AttackSoundPending = false;
            uint soundRaw = RandomStreams.GenerateGlobalSound("monster-attack:sound", $"{monster.Name}#{monster.EntityId}");
            monster.AttackSoundRaw = soundRaw;
            monster.AttackSoundGateRaw = soundRaw;
            monster.AttackSoundRepeatRaw = (soundRaw & 3u) == 0 ? soundRaw : 0;
            var rng = GetRoomRngForMonster(monster);
            string roomRngPos = rng != null ? rng.CallsSinceReseed.ToString() : "n/a";
            Debug.LogError($"[MON-ATTACK] {monster.Name} sound clientGlobalSoundRng=True raw=0x{soundRaw:X8} repeat={(monster.AttackSoundRepeatRaw != 0)} weaponSounds={monster.AttackWeaponSoundCount} attackSounds={monster.AttackRepeatSoundCount} globalSoundRngPos={RandomStreams.GlobalSoundCalls} roomRngPos={roomRngPos}");
        }

        public uint ConsumeOnApplyDamageEffectRng(MersenneTwister rng, string actor, uint targetId, string targetName, uint oldHPWire, uint newHPWire, uint targetMaxHPWire, uint damageWire, string source, bool physicalWeaponHit = true)
        {
            return 0;
        }

        private const int IdleGatePeriodTicks = 40;

        private sealed class MeleeBehaviorContext : Behavior.IMonsterBehaviorContext
        {
            public MersenneTwister Rng;
            public string Owner;
            public bool TargetPresent;
            public float Dist;
            public float Range;

            public uint RoomDraw(string site) => RngLedger.Generate(Rng, "room", site, Owner);
            public bool HasTarget => TargetPresent;
            public float DistanceToTarget => Dist;
            public bool TargetInMeleeReach => Dist <= Range;
            public bool TargetIsPlayer => true;
            public bool HasAttackDistanceRange => false;
            public int RepositionReachFixed(int headingFixed, int distFixed) => 0xa01;
        }

        private MeleeBehaviorContext _meleeBehaviorContext;

        private MeleeBehaviorContext BuildMeleeBehaviorContext(MersenneTwister rng, Monster monster, CombatPlayer target, float dist, float allowedRange)
        {
            if (_meleeBehaviorContext == null) _meleeBehaviorContext = new MeleeBehaviorContext();
            _meleeBehaviorContext.Rng = rng;
            _meleeBehaviorContext.Owner = monster.InstanceKey ?? monster.ZoneName;
            _meleeBehaviorContext.TargetPresent = target != null;
            _meleeBehaviorContext.Dist = dist;
            _meleeBehaviorContext.Range = allowedRange;
            return _meleeBehaviorContext;
        }

        private void TickMonsterBehavior(Monster monster, CombatPlayer target, float dist, float allowedRange)
        {
            var rng = GetRoomRngForMonster(monster);
            if (rng == null) return;

            bool freshTarget = monster.Behavior == null || monster.MeleeScanTargetId != target.EntityId;
            if (monster.Behavior == null)
                monster.Behavior = new Behavior.MonsterBehavior2();

            var behaviorContext = BuildMeleeBehaviorContext(rng, monster, target, dist, allowedRange);
            if (freshTarget)
            {
                monster.MeleeScanTargetId = target.EntityId;
                monster.Behavior.EnterCombat(behaviorContext);
                return;
            }
            monster.Behavior.Tick(behaviorContext);
        }

        public void FlushPlayerAttackCommitsBeforeSynch(uint playerEntityId)
        {
            if (playerEntityId == 0) return;
            ProcessMonsterAttacks(0f, playerEntityId, false, null, GetCombatTime());
        }

        private bool IsMonsterKnockDownActive(Monster monster, float now, string source)
        {
            if (monster == null || monster.SpellKnockDownEndTime <= 0f)
                return false;
            if (now + 0.0001f < monster.SpellKnockDownEndTime)
            {
                if (monster.AttackPending)
                    CancelMonsterPendingAttack(monster, $"{source ?? "update"}-SpellKnockDownEffect");
                if (monster.State == MonsterState.Attacking)
                    monster.State = MonsterState.Combat;
                if (monster.KnockBackActive)
                    ApplyKnockBackDisplacement(monster, now);
                return true;
            }
            if (monster.KnockBackActive)
            {
                ApplyKnockBackDisplacement(monster, monster.SpellKnockDownEndTime);
                monster.KnockBackActive = false;
            }
            Debug.LogError($"[SPELL-KNOCKDOWN] target={monster.Name}#{monster.EntityId} result=END strength={monster.SpellKnockDownStrength} now={now:F3} source={source ?? "update"} sourceFunction=KnockDown::UpdateKnockDown@0x0052A6B0");
            monster.SpellKnockDownEndTime = 0f;
            monster.SpellKnockDownStrength = 0;
            monster.SpellKnockDownSourceEntityId = 0;
            return false;
        }

        private void ApplyKnockBackDisplacement(Monster monster, float now)
        {
            float duration = KNOCKDOWN_VISUAL_MOVE_TICKS * CLIENT_UNIT_TICK_INTERVAL;
            float progress = duration > 0f ? (now - monster.KnockBackStartTime) / duration : 1f;
            if (progress < 0f) progress = 0f;
            if (progress > 1f) progress = 1f;
            float px = monster.KnockBackStartX + (monster.KnockBackDestX - monster.KnockBackStartX) * progress;
            float py = monster.KnockBackStartY + (monster.KnockBackDestY - monster.KnockBackStartY) * progress;
            monster.PosX = px;
            monster.PosY = py;
            monster.PosZ = ResolveTerrainHeight(monster, px, py, monster.PosZ);
            monster.ClientVisiblePosX = px;
            monster.ClientVisiblePosY = py;
        }

        public void CancelMonsterPendingAttack(Monster monster, string reason)
        {
            if (monster == null) return;
            bool hadPending = monster.AttackPending || monster.AttackSoundPending || monster.AttackClientVisible;
            monster.AttackPending = false;
            monster.AttackSoundPending = false;
            monster.AttackClientVisible = false;
            monster.AttackContactOnly = false;
            monster.AttackHitResolved = false;
            monster.AttackStartTick = 0;
            monster.AttackCommitTick = 0;
            monster.AttackSoundTick = 0;
            monster.AttackEndTick = 0;
            monster.AttackUseRaw = 0;
            monster.AttackCommitTargetX = 0f;
            monster.AttackCommitTargetY = 0f;
            monster.CombatContactTargetId = 0;
            monster.CombatContactUntil = 0f;
            if (monster.State == MonsterState.Attacking)
                monster.State = MonsterState.Combat;
            if (hadPending)
                Debug.LogError($"[MON-ATTACK] canceled pending attack {monster.Name}#{monster.EntityId} reason={reason}");
            TraceMonsterState(monster, "attack-cancel", null, -1f, -1f, reason);
        }

        public void DelayMonsterAttackRetry(Monster monster, string reason)
        {
            if (monster == null) return;
            int cooldownTicks = ResolveMonsterAttackCooldownTicks(monster);
            monster.NextAttackTick = (int)_combatTick + cooldownTicks;
            Debug.LogError($"[MON-ATTACK] retry delayed {monster.Name}#{monster.EntityId} reason={reason} cooldownTicks={cooldownTicks}");
        }

        private bool ShouldLogFarTargetAction(Monster monster, float now)
        {
            if (monster == null) return false;
            if (!_monsterFarTargetActionLogTime.TryGetValue(monster.EntityId, out float last) || now - last >= 1f)
            {
                _monsterFarTargetActionLogTime[monster.EntityId] = now;
                return true;
            }
            return false;
        }

        private void TraceFarTargetAction(Monster monster, CombatPlayer target, float dist, float allowedRange, string source)
        {
            if (!ShouldLogFarTargetAction(monster, GetCombatTime())) return;
            float targetRange = ResolveMonsterTargetSearchRange(monster, true);
            Debug.LogError($"[MON-ATTACK-REACH] target-action-only {monster.Name}#{monster.EntityId}->{target?.Name ?? "player"} source={source ?? "unknown"} dist={dist:F1} meleeRange={allowedRange:F1} targetRange={targetRange:F1} action=target-only-no-damage");
            TraceMonsterState(monster, "target-action-only", target, dist, allowedRange, source ?? "reach");
        }

        public bool HasPendingClientVisibleMonsterAttack(uint playerEntityId)
        {
            if (playerEntityId == 0) return false;
            float clientNow = GetCombatTime();
            foreach (var monster in _activeMonsters.Values)
            {
                if (monster == null) continue;
                if (!monster.IsAlive) continue;
                bool targetsPlayer = monster.TargetId == playerEntityId;
                bool contactsPlayer = monster.CombatContactTargetId == playerEntityId && clientNow <= monster.CombatContactUntil;
                if (!_players.TryGetValue(playerEntityId, out var player) || player == null || !player.IsAlive || player.PlayerState == null) continue;
                float dist = Distance2D(monster.PosX, monster.PosY, player.PosX, player.PosY);
                bool attackPathClear = IsMonsterAttackPathClear(monster, player, null);
                if (!attackPathClear) continue;
                float allowedRange = ResolveMonsterEffectiveAttackRange(monster);
                if (allowedRange <= 0f) continue;
                bool clientContact = IsClientCombatContact(monster, player, dist, allowedRange);
                bool weaponRuntimeReach = HasMonsterWeaponRuntimeReach(monster, player, dist, allowedRange, clientContact);
                bool visibleCommitTolerance = monster.AttackClientVisible && AttackCommitTargetStillValid(monster, player);
                if (monster.AttackPending && !monster.AttackHitResolved && (targetsPlayer || contactsPlayer) && (weaponRuntimeReach || monster.UsePrimaryActiveSkillThisAttack || visibleCommitTolerance))
                    return true;
                if (contactsPlayer)
                    return true;
                if (!targetsPlayer || !monster.AggroTriggered) continue;
                if (weaponRuntimeReach)
                    return true;
            }
            return false;
        }

        public bool FlushPlayerHPRuntimeBeforeSynch(uint playerEntityId, string source, out uint hpWire, out bool unsafeAttack, float clientNowOverride = -1f)
        {
            hpWire = 0;
            unsafeAttack = false;
            if (playerEntityId == 0) return true;
            if (!_players.TryGetValue(playerEntityId, out var target) || target == null || target.PlayerState == null) return true;
            float clientNow = clientNowOverride >= 0f ? clientNowOverride : GetCombatTime();
            target.PlayerState.AdvanceEntitySynchInfoHP(clientNow, source ?? "FlushPlayerHPRuntimeBeforeSynch");
            hpWire = target.PlayerState.EntitySynchInfoHP;

            foreach (var monster in _activeMonsters.Values)
            {
                if (monster == null) continue;
                bool targetsPlayer = monster.TargetId == playerEntityId;
                bool contactsPlayer = monster.CombatContactTargetId == playerEntityId && clientNow <= monster.CombatContactUntil;
                if (!monster.AttackPending || monster.AttackHitResolved || (!targetsPlayer && !contactsPlayer)) continue;
                if (target.PlayerState.IsZoneSpawnDamageImmune)
                {
                    CancelMonsterPendingAttack(monster, $"{source}-target_zone_spawn_invulnerability");
                    continue;
                }
                if (target.PlayerState.CurrentHPWire == 0 && target.PlayerState.EntitySynchInfoHP == 0)
                {
                    target.IsAlive = false;
                    CancelMonsterPendingAttack(monster, $"{source}-target_dead");
                    continue;
                }
                if (monster.DeathPendingClientConfirmation
                    && !(monster.AttackContactOnly && monster.AttackPending && !monster.AttackHitResolved))
                {
                    CancelMonsterPendingAttack(monster, $"{source}-monster_death_pending_client_confirmation");
                    continue;
                }

                float allowedRange = ResolveMonsterEffectiveAttackRange(monster);
                if (allowedRange <= 0f)
                {
                    unsafeAttack = true;
                    continue;
                }
                float dist = Distance2D(monster.PosX, monster.PosY, target.PosX, target.PosY);
                bool attackPathClear = IsMonsterAttackPathClear(monster, target, $"{source}-target-clear");
                if (monster.AttackCommitTick <= 0)
                {
                    if (!attackPathClear)
                    {
                        CancelMonsterPendingAttack(monster, $"{source}-world_blocked");
                        if (monster.IsAlive)
                            monster.State = MonsterState.Chase;
                        Debug.LogError($"[MON-DAMAGE-ENTITY-SYNCH-INFO] {monster.Name}#{monster.EntityId}->{target.Name} cancel worldBlocked source={source} dist={dist:F1} range={allowedRange:F1}");
                        OnMonsterAttackResolved?.Invoke(monster, target, false, target.PlayerState.CurrentHPWire);
                        hpWire = target.PlayerState.EntitySynchInfoHP;
                        continue;
                    }
                    bool initUseGateway = HasMonsterInitUseGateway(monster, target);
                    if (!initUseGateway && !monster.UsePrimaryActiveSkillThisAttack)
                    {
                        CancelMonsterPendingAttack(monster, $"{source}-init_use_gateway_pending dist={dist:F1} range={allowedRange:F1} targetRange={ResolveMonsterTargetSearchRange(monster, true):F1}");
                        if (monster.IsAlive)
                            monster.State = MonsterState.Chase;
                        continue;
                    }
                    if (GetRoomRngForMonster(monster) == null)
                    {
                        unsafeAttack = true;
                        continue;
                    }
                    ArmMonsterRuntimeAttack(monster, target, "ENTITY-SYNCH-INFO-ARM", clientNow);
                }
                if (GetRoomRngForMonster(monster) == null)
                {
                    unsafeAttack = true;
                    continue;
                }
                if (monster.AttackSoundPending && (int)_combatTick >= monster.AttackSoundTick)
                    ConsumeMonsterAttackSoundRng(monster);
                if ((int)_combatTick < monster.AttackCommitTick)
                {
                    hpWire = target.PlayerState.EntitySynchInfoHP;
                    unsafeAttack = true;
                    Debug.LogError($"[PLAYER-HP-CLIENT-PENDING] {source} keeping current HP before client hit frame {monster.Name}#{monster.EntityId}->{target.Name} nowTick={_combatTick} commitTick={monster.AttackCommitTick} hp={hpWire / 256f:F2}");
                    continue;
                }
                if (monster.AttackClientVisible && LastCompletedEntityUpdateTime < monster.AttackClientVisibleTime)
                {
                    hpWire = target.PlayerState.EntitySynchInfoHP;
                    unsafeAttack = true;
                    Debug.LogError($"[PLAYER-HP-CLIENT-PENDING] {source} holding damage commit until client processes attack {monster.Name}#{monster.EntityId}->{target.Name} now={clientNow:F3} visibleAt={monster.AttackClientVisibleTime:F3} lastEntityUpdate={LastCompletedEntityUpdateTime:F3} hp={hpWire / 256f:F2}");
                    continue;
                }
                if (monster.AttackSoundPending)
                    ConsumeMonsterAttackSoundRng(monster);
                monster.AttackHitResolved = true;
                if (monster.AttackEndTick < (int)_combatTick)
                    monster.AttackEndTick = (int)_combatTick;

                if (!TryDeferMonsterProjectileImpact(monster, target, dist, "MON-DAMAGE-ENTITY-SYNCH-INFO", source, clientNow))
                    ResolveMonsterAttackDamage(monster, target, dist, "MON-DAMAGE-ENTITY-SYNCH-INFO", source);
                monster.AttackPending = false;
                monster.AttackClientVisible = false;
                monster.AttackContactOnly = false;
                monster.AttackSoundPending = false;
                monster.AttackHitResolved = false;
                monster.AttackStartTick = 0;
                monster.AttackEndTick = 0;
                monster.AttackCommitTick = 0;
                monster.AttackSoundTick = 0;
                if (monster.IsAlive)
                    monster.State = MonsterState.Combat;
                hpWire = target.PlayerState.EntitySynchInfoHP;
            }

            return !unsafeAttack;
        }

        private bool AttackCommitTargetStillValid(Monster monster, CombatPlayer target)
        {
            if (monster == null || target == null) return false;
            float tolerance = monster.ClientSyncTolerance > 0f ? monster.ClientSyncTolerance : 10f;
            int tcx = Mathf.RoundToInt(target.PosX * 256f) - Mathf.RoundToInt(monster.AttackCommitTargetX * 256f);
            int tcy = Mathf.RoundToInt(target.PosY * 256f) - Mathf.RoundToInt(monster.AttackCommitTargetY * 256f);
            long distSqFixed8 = (((long)tcx * tcx) >> 8) + (((long)tcy * tcy) >> 8);
            int tolFixed = Mathf.RoundToInt(tolerance * 256f);
            return distSqFixed8 <= (((long)tolFixed * tolFixed) >> 8);
        }

        private bool HasCombatContact(Monster monster, CombatPlayer target)
        {
            return monster != null && target != null && monster.CombatContactTargetId == target.EntityId && GetCombatTime() <= monster.CombatContactUntil;
        }

        private float ResolveMonsterDamageTable(byte level)
        {
            return GCDatabase.Instance.RequireCurveValue("MonsterDamage", Mathf.Clamp(level, 1, 110));
        }

        private int ResolveMonsterAttackRating(Monster monster)
        {
            return DamageResolver.ResolveMonsterAttackRating(monster);
        }

        private static uint ApplyDamageTakenModWire(uint damageWire, float damageTakenMod)
        {
            if (damageWire == 0) return 0;
            if (damageTakenMod < 1f) return 0;
            double scaled = damageWire * (double)damageTakenMod / 100.0;
            if (scaled >= uint.MaxValue) return uint.MaxValue;
            return (uint)scaled;
        }

        private static float ResolveMonsterDamageTypeTakenMod(Monster monster, int damageTypeId)
        {
            if (monster == null) return 100f;
            return damageTypeId switch
            {
                3 => monster.FireDamageTakenMod,
                4 => monster.IceDamageTakenMod,
                5 => monster.PoisonDamageTakenMod,
                6 => monster.ShadowDamageTakenMod,
                7 => monster.DivineDamageTakenMod,
                _ => 100f
            };
        }

        private sealed class DamageQueryResult
        {
            public uint AdjustedDamageWire;
            public float DamageTakenMod = 100f;
            public float DamageTypeMod = 100f;
            public string ResultName = "NONE";
            public uint ResistRaw;
            public int ResistChanceWire;
        }

        private enum DamageResistResult
        {
            None = 0,
            Immune = 1,
            Resisted = 2,
            Vulnerable = 3
        }

        private static DamageQueryResult ApplyDamageQueryWire(uint damageWire, Monster monster, int damageTypeId, byte damageKind, float clientDamageTime, string source, int attackerLevel, bool spellEffectResistResolved = false, bool spellEffectVulnerable = false)
        {
            var result = new DamageQueryResult
            {
                AdjustedDamageWire = damageWire,
                DamageTakenMod = monster != null ? monster.DamageTakenMod : 100f,
                DamageTypeMod = ResolveMonsterDamageTypeTakenMod(monster, damageTypeId),
                ResultName = "NONE"
            };

            result.AdjustedDamageWire = ApplyDamageTakenModWire(result.AdjustedDamageWire, result.DamageTakenMod);
            if (damageTypeId >= 3 && damageTypeId <= 7 && result.DamageTypeMod != 100f)
            {
                if (result.DamageTypeMod < 1f)
                {
                    result.AdjustedDamageWire = 0;
                    result.ResultName = "TYPE_ABSORB";
                    return result;
                }
                result.AdjustedDamageWire = ApplyDamageTakenModWire(result.AdjustedDamageWire, result.DamageTypeMod);
            }

            if (result.AdjustedDamageWire == 0 || monster == null)
                return result;

            if (spellEffectResistResolved)
            {
                result.ResultName = spellEffectVulnerable ? "VULNERABLE" : "NONE";
                if (spellEffectVulnerable)
                    result.AdjustedDamageWire = ClampWireAdd(result.AdjustedDamageWire, result.AdjustedDamageWire);
                return result;
            }

            DamageResistResult resist = CheckMonsterDamageResist(monster, damageKind, damageTypeId, 0x100, attackerLevel, out int resistChanceWire, out uint resistRaw, source);
            result.ResistChanceWire = resistChanceWire;
            result.ResistRaw = resistRaw;
            result.ResultName = resist.ToString().ToUpperInvariant();
            if (resist == DamageResistResult.Immune || resist == DamageResistResult.Resisted)
            {
                result.AdjustedDamageWire = 0;
                return result;
            }
            if (resist == DamageResistResult.Vulnerable)
                result.AdjustedDamageWire = ClampWireAdd(result.AdjustedDamageWire, result.AdjustedDamageWire);
            return result;
        }

        public bool ApplySpellKnockDownEffectToMonster(MersenneTwister rng, Monster target, int attackerLevel, int strength, int chanceWire, string marker, float clientEffectTime = -1f, uint sourceEntityId = 0)
        {
            if (target == null || !target.IsAlive)
                return false;
            string source = marker ?? "SPELL";
            if (rng == null)
            {
                Debug.LogError($"[SPELL-KNOCKDOWN] target={target.Name}#{target.EntityId} result=NO_RNG reason=missing-rng source={source} sourceFunction=SpellKnockDownEffect::doEffect@0x00553360 Unit::CheckStunResist@0x0050C630");
                return false;
            }

            int normalizedChance = Mathf.Clamp(chanceWire, 0, 0x6400);
            uint chanceRaw = 0;
            uint chanceRoll = 0;
            if (normalizedChance < 0x6400)
            {
                chanceRaw = RngLedger.Generate(rng, "unitOwnedCombat", $"{source}:SpellKnockDownEffect::CheckChance", "SpellEffect::CheckChance");
                chanceRoll = chanceRaw % 0x6464u;
                if (chanceRoll >= (uint)normalizedChance)
                {
                    Debug.LogError($"[SPELL-KNOCKDOWN] target={target.Name}#{target.EntityId} result=CHANCE_FAIL strength={strength} chanceWire={normalizedChance} chanceRaw=0x{chanceRaw:X8} chanceRoll={chanceRoll} rngPos={rng.CallsSinceReseed} source={source} sourceFunction=SpellEffect::CheckChance@0x00545FF0 SpellKnockDownEffect::doEffect@0x00553360");
                    return false;
                }
            }

            int stunResistWire = ResolveMonsterStunResistChanceWire(target, attackerLevel);
            uint stunRaw = RngLedger.Generate(rng, "unitOwnedCombat", $"{source}:SpellKnockDownEffect::CheckStunResist", "Unit::CheckStunResist");
            uint stunRoll = stunRaw % 0x6400u;
            if (stunRoll < (uint)Mathf.Max(0, stunResistWire))
            {
                Debug.LogError($"[SPELL-KNOCKDOWN] target={target.Name}#{target.EntityId} result=RESIST strength={strength} chanceWire={normalizedChance} chanceRaw=0x{chanceRaw:X8} chanceRoll={chanceRoll} stunResistWire={stunResistWire} stunRaw=0x{stunRaw:X8} stunRoll={stunRoll} rngPos={rng.CallsSinceReseed} source={source} sourceFunction=SpellKnockDownEffect::doEffect@0x00553360 SpellEffect::CheckChance@0x00545FF0 Unit::CheckStunResist@0x0050C630");
                return false;
            }

            float now = clientEffectTime >= 0f ? clientEffectTime : GetCombatTime();
            float endTime = now + KNOCKDOWN_STATE_MESSAGE_TICKS * CLIENT_UNIT_TICK_INTERVAL;
            if (target.SpellKnockDownEndTime < endTime)
                target.SpellKnockDownEndTime = endTime;
            target.SpellKnockDownStrength = strength;
            target.SpellKnockDownSourceEntityId = sourceEntityId;

            float dirX = 0f, dirY = 0f;
            if (sourceEntityId != 0 && _players.TryGetValue(sourceEntityId, out var knockSource))
            {
                dirX = target.PosX - knockSource.PosX;
                dirY = target.PosY - knockSource.PosY;
            }
            float dirLen = Mathf.Sqrt(dirX * dirX + dirY * dirY);
            if (dirLen > 0.001f) { dirX /= dirLen; dirY /= dirLen; }
            else { dirX = 1f; dirY = 0f; }
            float knockDistance = Mathf.Max(0f, strength * KNOCKDOWN_STRENGTH_DISTANCE_SCALE);
            target.KnockBackStartX = target.PosX;
            target.KnockBackStartY = target.PosY;
            float rawDestX = target.PosX + dirX * knockDistance;
            float rawDestY = target.PosY + dirY * knockDistance;
            var knockPm = PathMapCatalog.Instance.GetPathMap(!string.IsNullOrWhiteSpace(target.InstanceKey) ? target.InstanceKey : target.ZoneName);
            if (knockPm != null && knockPm.CastGroundRaySlide(target.PosX, target.PosY, rawDestX, rawDestY, out float clampDestX, out float clampDestY))
            {
                target.KnockBackDestX = clampDestX;
                target.KnockBackDestY = clampDestY;
            }
            else
            {
                target.KnockBackDestX = rawDestX;
                target.KnockBackDestY = rawDestY;
            }
            target.KnockBackStartTime = now;
            target.KnockBackActive = knockDistance > 0.001f;

            CancelMonsterPendingAttack(target, $"{source}-SpellKnockDownEffect");
            if (target.State == MonsterState.Attacking)
                target.State = MonsterState.Combat;
            Debug.LogError($"[SPELL-KNOCKDOWN] target={target.Name}#{target.EntityId} result=ACTION strength={strength} distance={knockDistance:F2} chanceWire={normalizedChance} chanceRaw=0x{chanceRaw:X8} chanceRoll={chanceRoll} stunResistWire={stunResistWire} stunRaw=0x{stunRaw:X8} stunRoll={stunRoll} start={now:F3} end={target.SpellKnockDownEndTime:F3} dest=({target.KnockBackDestX:F1},{target.KnockBackDestY:F1}) rngPos={rng.CallsSinceReseed} source={source} sourceFunction=SpellKnockDownEffect::doEffect@0x00553360 Unit::CheckStunResist@0x0050C630 KnockDown::EnterKnockDown@0x0052A5E0");
            return true;
        }

        private static int ResolveMonsterStunResistChanceWire(Monster target, int attackerLevel)
        {
            int resist = (target?.Slots?.Get(UnitSlot.StunResist) ?? 0) * 0x100;
            int targetLevel = target?.Level ?? 0;
            int diff = (targetLevel * 0x100) - (Math.Max(1, attackerLevel) * 0x100);
            if (diff > 0x500)
                resist += (int)(((long)(diff - 0x500) * 0x500) >> 8);
            if (resist > 0x5A00) resist = 0x5A00;
            if (resist < 0x500) resist = 0x500;
            return resist;
        }

        private static DamageResistResult CheckMonsterDamageResist(Monster monster, byte damageKind, int damageTypeId, int fixedScale, int attackerLevel, out int resistChanceWire, out uint resistRaw, string source)
        {
            resistChanceWire = 0;
            resistRaw = 0;
            if (monster?.Slots == null)
                return DamageResistResult.None;

            if (monster.Slots.Get(UnitSlot.DamageImmunity) > 0)
            {
                resistChanceWire = 0x6400;
                Debug.LogError($"[DAMAGE-RESIST] target={monster.Name}#{monster.EntityId} result=IMMUNE chance=100.00 raw=0x00000000 damageKind={damageKind} damageTypeId={damageTypeId} source={source ?? "unknown"} sourceFunction=Unit::CheckDamageResist@0x0050B660");
                return DamageResistResult.Immune;
            }

            int resistWire = monster.Slots.Get(UnitSlot.DamageResist) * 0x100;
            if (damageKind == 3)
                resistWire += monster.Slots.Get(UnitSlot.MagicResist) * 0x100;
            resistWire += ResolveMonsterDamageTypeResist(monster, damageTypeId) * 0x100;

            long scaledResist = ((long)resistWire * fixedScale) >> 8;
            attackerLevel = Math.Max(1, attackerLevel);
            int denom = Math.Max(1, attackerLevel * 0x0D00);
            long q = (scaledResist << 8) / denom;
            resistChanceWire = (int)((q * 0x4B00L) >> 8);
            if (resistChanceWire > 0x6400) resistChanceWire = 0x6400;
            if (resistChanceWire < -0x6400) resistChanceWire = -0x6400;
            if (resistChanceWire >= 0x6400)
                return DamageResistResult.Immune;

            MersenneTwister rng = monster.Rng;
            if (rng == null)
            {
                Debug.LogError($"[DAMAGE-RESIST] target={monster.Name}#{monster.EntityId} result=NO_RNG chance={resistChanceWire / 256f:F2} raw=0x00000000 damageKind={damageKind} damageTypeId={damageTypeId} source={source ?? "unknown"} reason=missing-unit-owned-rng sourceFunction=Unit::CheckDamageResist@0x0050B660");
                return DamageResistResult.None;
            }

            resistRaw = RngLedger.Generate(rng, "unitOwnedCombat", "Unit::CheckDamageResist", $"{monster.Name}#{monster.EntityId}");
            int roll = (int)(resistRaw % 0x6400);
            DamageResistResult result;
            if (resistChanceWire < 0)
                result = roll < -resistChanceWire ? DamageResistResult.Vulnerable : DamageResistResult.None;
            else
                result = roll < resistChanceWire ? DamageResistResult.Resisted : DamageResistResult.None;
            Debug.LogError($"[DAMAGE-RESIST] target={monster.Name}#{monster.EntityId} result={result.ToString().ToUpperInvariant()} chance={resistChanceWire / 256f:F2} roll={roll / 256f:F2} raw=0x{resistRaw:X8} damageKind={damageKind} damageTypeId={damageTypeId} source={source ?? "unknown"} rngPos={rng.CallsSinceReseed} sourceFunction=Unit::CheckDamageResist@0x0050B660");
            return result;
        }

        private static int ResolveMonsterDamageTypeResist(Monster monster, int damageTypeId)
        {
            if (monster?.Slots == null) return 0;
            return damageTypeId switch
            {
                0 => monster.Slots.Get(UnitSlot.CrushingResist),
                1 => monster.Slots.Get(UnitSlot.PiercingResist),
                2 => monster.Slots.Get(UnitSlot.SlashingResist),
                3 => monster.Slots.Get(UnitSlot.FireResist),
                4 => monster.Slots.Get(UnitSlot.IceResist),
                5 => monster.Slots.Get(UnitSlot.PoisonResist),
                6 => monster.Slots.Get(UnitSlot.ShadowResist),
                7 => monster.Slots.Get(UnitSlot.DivineResist),
                _ => 0
            };
        }


        private static DamageQueryResult ApplyPlayerDamageQueryWire(uint damageWire, CombatPlayer target, Monster attacker, int damageTypeId, byte damageKind, float clientDamageTime, string source, int attackerLevel, MersenneTwister rng, bool resistable)
        {
            PlayerState state = target?.PlayerState;
            var result = new DamageQueryResult
            {
                AdjustedDamageWire = damageWire,
                DamageTakenMod = state != null ? state.DamageTakenMod : 100f,
                DamageTypeMod = ResolvePlayerDamageTypeTakenMod(state, damageTypeId),
                ResultName = "NONE"
            };

            if (state != null && state.HasAnyDamageImmunity)
            {
                result.AdjustedDamageWire = 0;
                result.ResultName = "IMMUNE";
                result.ResistChanceWire = 0x6400;
                Debug.LogError($"[DAMAGE-RESIST] target={target.Name}#{target.EntityId} result=IMMUNE chance=100.00 raw=0x00000000 damageKind={damageKind} damageTypeId={damageTypeId} source={source ?? "unknown"} sourceFunction=Unit::CheckDamageResist@0x0050B660 target=Avatar");
                return result;
            }

            result.AdjustedDamageWire = ApplyDamageTakenModWire(result.AdjustedDamageWire, result.DamageTakenMod);
            if (damageTypeId >= 3 && damageTypeId <= 7 && result.DamageTypeMod != 100f)
            {
                if (result.DamageTypeMod < 1f)
                {
                    result.AdjustedDamageWire = 0;
                    result.ResultName = "TYPE_ABSORB";
                    return result;
                }
                result.AdjustedDamageWire = ApplyDamageTakenModWire(result.AdjustedDamageWire, result.DamageTypeMod);
            }

            if (result.AdjustedDamageWire == 0 || target == null)
                return result;

            if (!resistable)
                return result;

            DamageResistResult resist = CheckPlayerDamageResist(target, attacker, damageKind, damageTypeId, 0x100, attackerLevel, rng, out int resistChanceWire, out uint resistRaw, source);
            result.ResistChanceWire = resistChanceWire;
            result.ResistRaw = resistRaw;
            result.ResultName = resist.ToString().ToUpperInvariant();
            if (resist == DamageResistResult.Immune || resist == DamageResistResult.Resisted)
            {
                result.AdjustedDamageWire = 0;
                return result;
            }
            if (resist == DamageResistResult.Vulnerable)
                result.AdjustedDamageWire = ClampWireAdd(result.AdjustedDamageWire, result.AdjustedDamageWire);
            return result;
        }

        private static DamageResistResult CheckPlayerDamageResist(CombatPlayer target, Monster attacker, byte damageKind, int damageTypeId, int fixedScale, int attackerLevel, MersenneTwister rng, out int resistChanceWire, out uint resistRaw, string source)
        {
            resistChanceWire = 0;
            resistRaw = 0;
            PlayerState state = target?.PlayerState;
            if (state == null)
                return DamageResistResult.None;

            int resistWire = GetEquipmentStatSum(state, "DAMAGE_RESIST", "DAMAGERESIST") * 0x100;
            if (damageKind == 3)
                resistWire += GetEquipmentStatSum(state, "MAGIC_DAMAGE_RESIST", "MAGIC_RESIST", "MAGICDAMAGERESIST", "MAGICRESIST") * 0x100;
            resistWire += ResolvePlayerDamageTypeResist(state, damageTypeId) * 0x100;

            long scaledResist = ((long)resistWire * fixedScale) >> 8;
            attackerLevel = Math.Max(1, attackerLevel);
            int denom = Math.Max(1, attackerLevel * 0x0D00);
            long q = (scaledResist << 8) / denom;
            resistChanceWire = (int)((q * 0x4B00L) >> 8);
            if (resistChanceWire > 0x5A00) resistChanceWire = 0x5A00;
            if (resistChanceWire < -0x6400) resistChanceWire = -0x6400;

            if (rng == null)
            {
                Debug.LogError($"[DAMAGE-RESIST] target={target.Name}#{target.EntityId} result=NO_RNG chance={resistChanceWire / 256f:F2} raw=0x00000000 damageKind={damageKind} damageTypeId={damageTypeId} source={source ?? "unknown"} reason=missing-world-rng sourceFunction=Unit::CheckDamageResist@0x0050B660 target=Avatar attacker={attacker?.Name ?? "unknown"}#{attacker?.EntityId ?? 0}");
                return DamageResistResult.None;
            }

            resistRaw = RngLedger.Generate(rng, "unitOwnedCombat", "Unit::CheckDamageResist", $"{target.Name}#{target.EntityId}");
            int roll = (int)(resistRaw % 0x6400);
            DamageResistResult result;
            if (resistChanceWire < 0)
                result = roll < -resistChanceWire ? DamageResistResult.Vulnerable : DamageResistResult.None;
            else
                result = roll < resistChanceWire ? DamageResistResult.Resisted : DamageResistResult.None;
            Debug.LogError($"[DAMAGE-RESIST] target={target.Name}#{target.EntityId} result={result.ToString().ToUpperInvariant()} chance={resistChanceWire / 256f:F2} roll={roll / 256f:F2} raw=0x{resistRaw:X8} damageKind={damageKind} damageTypeId={damageTypeId} source={source ?? "unknown"} rngPos={rng.CallsSinceReseed} sourceFunction=Unit::CheckDamageResist@0x0050B660 target=Avatar attacker={attacker?.Name ?? "unknown"}#{attacker?.EntityId ?? 0}");
            return result;
        }

        private static int ResolvePlayerDamageTypeResist(PlayerState state, int damageTypeId)
        {
            return damageTypeId switch
            {
                0 => GetEquipmentStatSum(state, "CRUSHING_DAMAGE_RESIST", "CRUSHINGDAMAGERESIST"),
                1 => GetEquipmentStatSum(state, "PIERCING_DAMAGE_RESIST", "PIERCINGDAMAGERESIST"),
                2 => GetEquipmentStatSum(state, "SLASHING_DAMAGE_RESIST", "SLASHINGDAMAGERESIST"),
                3 => GetEquipmentStatSum(state, "FIRE_DAMAGE_RESIST", "FIREDAMAGERESIST"),
                4 => GetEquipmentStatSum(state, "ICE_DAMAGE_RESIST", "ICEDAMAGERESIST", "COLD_DAMAGE_RESIST", "COLDDAMAGERESIST"),
                5 => GetEquipmentStatSum(state, "POISON_DAMAGE_RESIST", "POISONDAMAGERESIST"),
                6 => GetEquipmentStatSum(state, "SHADOW_DAMAGE_RESIST", "SHADOWDAMAGERESIST"),
                7 => GetEquipmentStatSum(state, "DIVINE_DAMAGE_RESIST", "DIVINEDAMAGERESIST"),
                _ => 0
            };
        }

        private static float ResolvePlayerDamageTypeTakenMod(PlayerState state, int damageTypeId)
        {
            int mod = damageTypeId switch
            {
                3 => GetEquipmentStatSum(state, "FIRE_DAMAGE_TAKEN_MOD", "FIREDAMAGETAKENMOD"),
                4 => GetEquipmentStatSum(state, "ICE_DAMAGE_TAKEN_MOD", "ICEDAMAGETAKENMOD", "COLD_DAMAGE_TAKEN_MOD", "COLDDAMAGETAKENMOD"),
                5 => GetEquipmentStatSum(state, "POISON_DAMAGE_TAKEN_MOD", "POISONDAMAGETAKENMOD"),
                6 => GetEquipmentStatSum(state, "SHADOW_DAMAGE_TAKEN_MOD", "SHADOWDAMAGETAKENMOD"),
                7 => GetEquipmentStatSum(state, "DIVINE_DAMAGE_TAKEN_MOD", "DIVINEDAMAGETAKENMOD"),
                _ => 0
            };
            return 100f + mod;
        }

        private static int DamageResistLogCode(DamageQueryResult query)
        {
            if (query == null || query.ResultName == "NONE") return 0;
            return query.ResultName == "VULNERABLE" ? 3 : 1;
        }

        private static int GetEquipmentStatSum(PlayerState state, params string[] keys)
        {
            if (state?.EquipmentStats == null || keys == null || keys.Length == 0) return 0;
            int total = 0;
            foreach (var statEntry in state.EquipmentStats)
            {
                string key = CanonicalStatName(statEntry.Key);
                for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
                {
                    if (key == CanonicalStatName(keys[keyIndex]))
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

        private int ResolveAvatarDefenseRating(PlayerState state, Monster attacker)
        {
            if (state == null) return 0;
            float defensePerStrength = GCDatabase.Instance.GetKnob("DefenseRatingPerStrength", 14f);
            int rating = Mathf.RoundToInt(state.Strength * defensePerStrength) + state.ArmorDefenseRating;
            int modifier = GetEquipmentStat(state, "DEFENSE_RATING_MOD");
            string weaponClass = ResolveMonsterWeaponClass(attacker);
            if (IsRangedWeaponClass(weaponClass))
            {
                rating += GetEquipmentStat(state, "RANGE_DEFENSE_RATING");
                modifier += GetEquipmentStat(state, "RANGE_DEFENSE_RATING_MOD");
            }
            else
            {
                rating += GetEquipmentStat(state, "MELEE_DEFENSE_RATING");
                modifier += GetEquipmentStat(state, "MELEE_DEFENSE_RATING_MOD");
            }
            return Mathf.Max(0, (int)(((long)Mathf.Max(0, rating) * (modifier + 100)) / 100));
        }

        private static int GetEquipmentStat(PlayerState state, string key)
        {
            if (state?.EquipmentStats == null || string.IsNullOrEmpty(key)) return 0;
            return state.EquipmentStats.TryGetValue(key, out int value) ? value : 0;
        }

        private static string ResolveMonsterWeaponClass(Monster monster)
        {
            if (monster?.Manipulators != null &&
                monster.Manipulators.TryGetValue("primaryweapon", out var weapon) &&
                weapon?.properties != null &&
                weapon.properties.TryGetValue("WeaponClass", out var weaponClass) &&
                !string.IsNullOrWhiteSpace(weaponClass))
                return weaponClass;

            if (!string.IsNullOrWhiteSpace(monster?.WeaponClass))
                return monster.WeaponClass;

            return monster?.BehaviourType?.IndexOf("ranged", StringComparison.OrdinalIgnoreCase) >= 0 ? "1HRANGED" : "HTH";
        }

        private static bool IsRangedWeaponClass(string weaponClass)
        {
            if (string.IsNullOrEmpty(weaponClass)) return false;
            return weaponClass.Equals("1HRANGED", StringComparison.OrdinalIgnoreCase)
                || weaponClass.Equals("2HRANGED", StringComparison.OrdinalIgnoreCase)
                || weaponClass.Equals("2HCANNON", StringComparison.OrdinalIgnoreCase);
        }

        private int ResolveAvatarBlockChance(PlayerState state)
        {
            if (state == null || state.EquipmentStats == null) return 0;
            return state.EquipmentStats.TryGetValue("BLOCK", out int block) ? Mathf.Clamp(block, 0, 100) : 0;
        }

        private int ResolveHitThreshold(int attackRating, int defenseRating, int attackerLevel, int defenderLevel)
        {
            return DamageResolver.ResolveHitThreshold(attackRating, defenseRating, attackerLevel, defenderLevel);
        }

        private WeaponDamageInput CreateMonsterWeaponDamageInput(Monster monster, CombatPlayer target, MersenneTwister rng, string source)
        {
            int attackerLevel = monster != null ? Mathf.Clamp(monster.Level, 0, 110) : 1;
            int defenderLevel = target?.PlayerState != null ? Mathf.Clamp(target.PlayerState.Level, 0, 110) : attackerLevel;
            float baseDamage = monster != null ? ResolveMonsterDamageTable(monster.Level) : 1f;
            float damageMod = monster != null ? ResolveMonsterDamageModifier(monster) : 1f;
            float weaponScale = monster != null && monster.WeaponDamage > 0f ? monster.WeaponDamage : 1f;
            float volatility = monster != null ? Mathf.Clamp(monster.DamageVolatility, 0f, 0.95f) : 0.5f;

            return new WeaponDamageInput
            {
                Rng = rng,
                Source = source,
                AttackerLevel = attackerLevel,
                DefenderLevel = defenderLevel,
                AttackRating = ResolveMonsterAttackRating(monster),
                DefenseRating = ResolveAvatarDefenseRating(target?.PlayerState, monster),
                BlockChance = ResolveAvatarBlockChance(target?.PlayerState),
                DamageLevel = Mathf.Max(1, Mathf.RoundToInt(baseDamage)),
                DamageBonus = 0,
                DamageMod = Mathf.Max(0, Mathf.RoundToInt(damageMod * 100f)),
                WeaponClassId = monster != null ? monster.WeaponClassId : 1,
                DamageTypeId = monster != null ? monster.DamageTypeId : 0,
                WeaponDamageF32 = DamageResolver.Fixed32FromAuthoredDecimal(weaponScale),
                WeaponVolatilityF32 = DamageResolver.Fixed32FromAuthoredDecimal(volatility),
                CritThreshold = DamageResolver.ResolveMonsterCriticalThreshold(monster, target?.PlayerState),
                CritDamagePercent = 200
            };
        }

        private bool MonsterUsesProjectileWeapon(Monster monster)
        {
            return monster != null && monster.WeaponUsesProjectile && monster.WeaponProjectileSpeed > 0f;
        }

        private static int ProjectileFlightTicksFixed(float rangeUnits, float projectileSpeed)
        {
            int rangeFixed16 = Mathf.Max(0, Mathf.RoundToInt(Mathf.Max(0f, rangeUnits) * 65536f));
            int speedByte = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(0f, projectileSpeed)), 1, 255);
            long stepDenominator = ((long)speedByte << 16) / CLIENT_PROJECTILE_SPEED_FIXED_DENOMINATOR;
            if (stepDenominator <= 0) stepDenominator = 1;
            long flight = ((long)rangeFixed16) / stepDenominator;
            int flightTicks = (int)(flight >> 8);
            return Math.Max(1, flightTicks);
        }

        private bool TryDeferMonsterProjectileImpact(Monster monster, CombatPlayer target, float dist, string marker, string source, float clientNow)
        {
            if (!MonsterUsesProjectileWeapon(monster) || target?.PlayerState == null)
                return false;

            TryGetMonsterClientVisiblePosition(monster, clientNow, out float startX, out float startY);
            float targetX = target.PosX;
            float targetY = target.PosY;
            float pathDistance = Distance2D(startX, startY, targetX, targetY);
            float weaponRange = monster.WeaponRange > 0f ? monster.WeaponRange : Mathf.Max(0f, ResolveMonsterEffectiveAttackRange(monster));
            float flightDistance = Mathf.Max(pathDistance, 0f);
            if (weaponRange > 0f)
                flightDistance = Mathf.Min(flightDistance, weaponRange);

            int fireTick = (int)ResolveTickForTime(clientNow);
            int flightTicks = ProjectileFlightTicksFixed(flightDistance, monster.WeaponProjectileSpeed);
            int firstCollisionTick = fireTick + 1;
            int dueTick = Math.Max(firstCollisionTick, fireTick + flightTicks);
            float dueTime = dueTick * CLIENT_UNIT_TICK_INTERVAL;

            var pending = new PendingMonsterProjectileImpact
            {
                Sequence = ++_nextMonsterProjectileSequence,
                MonsterEntityId = monster.EntityId,
                TargetEntityId = target.EntityId,
                Marker = marker,
                Source = source,
                Dist = dist,
                FireTick = fireTick,
                FlightTicks = flightTicks,
                DueTick = dueTick,
                DueTime = dueTime
            };
            _pendingMonsterProjectiles.Add(pending);

            Debug.LogError($"[MON-PROJECTILE] {monster.Name}#{monster.EntityId}->{target.Name} schedule seq={pending.Sequence} marker={marker} source={source} fireTick={fireTick} firstCollisionTick={firstCollisionTick} flightTicks={flightTicks} dueTick={dueTick} dueTime={dueTime:F3} dist={dist:F1} pathDist={pathDistance:F1} flightDist={flightDistance:F1} speed={monster.WeaponProjectileSpeed:F1} size={monster.WeaponProjectileSize:F1} range={weaponRange:F1} sourceFunction=RangedWeapon::doHit@0x00595DD0->Projectile::init@0x005934D0 commitAtImpact=Projectile::doImpact@0x00594430->Weapon::applyDamage@0x00597E50");
            return true;
        }

        private void DrainDueMonsterProjectileImpacts(float clientNow)
        {
            if (_pendingMonsterProjectiles.Count == 0)
                return;

            int nowTick = (int)ResolveTickForTime(clientNow);
            _pendingMonsterProjectiles.Sort((left, right) =>
            {
                int tick = left.DueTick.CompareTo(right.DueTick);
                if (tick != 0) return tick;
                return left.Sequence.CompareTo(right.Sequence);
            });

            for (int projectileIndex = 0; projectileIndex < _pendingMonsterProjectiles.Count;)
            {
                PendingMonsterProjectileImpact pending = _pendingMonsterProjectiles[projectileIndex];
                if (pending == null)
                {
                    _pendingMonsterProjectiles.RemoveAt(projectileIndex);
                    continue;
                }
                if (pending.DueTick > nowTick)
                {
                    projectileIndex++;
                    continue;
                }

                _pendingMonsterProjectiles.RemoveAt(projectileIndex);

                Monster monster = GetMonster(pending.MonsterEntityId);
                CombatPlayer target = GetPlayer(pending.TargetEntityId);
                if (monster == null || !monster.IsAlive || target == null || !target.IsAlive || target.PlayerState == null)
                {
                    Debug.LogError($"[MON-PROJECTILE] impact-discard seq={pending.Sequence} monster={pending.MonsterEntityId} target={pending.TargetEntityId} marker={pending.Marker} source={pending.Source} dueTick={pending.DueTick} nowTick={nowTick} reason=actor-invalid");
                    continue;
                }

                float impactTime = Mathf.Max(clientNow, pending.DueTime);
                Debug.LogError($"[MON-PROJECTILE] {monster.Name}#{monster.EntityId}->{target.Name} impact seq={pending.Sequence} marker={pending.Marker} source={pending.Source} fireTick={pending.FireTick} flightTicks={pending.FlightTicks} dueTick={pending.DueTick} nowTick={nowTick} impactTime={impactTime:F3} sourceFunction=Projectile::doImpact@0x00594430");
                ResolveMonsterAttackDamage(monster, target, pending.Dist, pending.Marker, $"{pending.Source}-projectile-impact");
            }
        }

        public void ClearMonsterProjectilesForMonster(uint monsterEntityId)
        {
            if (monsterEntityId == 0 || _pendingMonsterProjectiles.Count == 0)
                return;
            _pendingMonsterProjectiles.RemoveAll(pending => pending != null && pending.MonsterEntityId == monsterEntityId);
        }

        private void ResolveMonsterAttackDamage(Monster monster, CombatPlayer target, float dist, string marker, string source)
        {
            float clientNow = GetCombatTime();
            target.PlayerState.AdvanceEntitySynchInfoHP(clientNow, $"{marker}-pre-damage");
            float clientVisibleMonsterX = monster.PosX, clientVisibleMonsterY = monster.PosY;
            TryGetMonsterClientVisiblePosition(monster, clientNow, out clientVisibleMonsterX, out clientVisibleMonsterY);
            float monsterAttackRange = ResolveMonsterEffectiveAttackRange(monster);
            bool projectileImpact = source != null && source.IndexOf("projectile", StringComparison.OrdinalIgnoreCase) >= 0;
            long clientVisibleDistSqFixed8 = 0, clientVisibleRangeSqFixed8 = 0;
            float clientVisibleAttackDist = 0f;
            bool clientVisibleInContact = true;
            if (monsterAttackRange > 0f)
                clientVisibleInContact = EvaluateUseTargetInitUse(clientVisibleMonsterX, clientVisibleMonsterY, target.PosX, target.PosY, monsterAttackRange, CLIENT_CONTACT_RANGE_EPSILON, out clientVisibleAttackDist, out clientVisibleDistSqFixed8, out clientVisibleRangeSqFixed8);
            DungeonRunners.Core.RuntimeEvidence.LogForPlayer(target.Name, "[MON-HIT]", $"monster={monster.Name}#{monster.EntityId} dist={dist:F1} cvDistSq={clientVisibleDistSqFixed8} rangeSq={clientVisibleRangeSqFixed8} cvMon=({clientVisibleMonsterX:F1},{clientVisibleMonsterY:F1}) avatar=({target.PosX:F1},{target.PosY:F1}) attackRange={monsterAttackRange:F1} inContact={clientVisibleInContact} projectile={projectileImpact} targetHp={target.PlayerState.CurrentHPWire} source={source ?? marker}");
            if (!projectileImpact && !clientVisibleInContact)
            {
                DungeonRunners.Core.RuntimeEvidence.LogForPlayer(target.Name, "[MON-HIT-SKIP]", $"monster={monster.Name}#{monster.EntityId} cvDistSq={clientVisibleDistSqFixed8} rangeSq={clientVisibleRangeSqFixed8} attackRange={monsterAttackRange:F1} reason=client-visible-out-of-contact source={source ?? marker}");
                ClearMonsterCombatContact(monster, target);
                OnMonsterAttackResolved?.Invoke(monster, target, false, target.PlayerState.CurrentHPWire);
                return;
            }
            if (TryApplyMonsterActiveSkillEffect(monster, target, clientNow, dist, marker, source, out bool activeSkillHandled, out bool activeSkillHPShifted))
            {
                target.IsAlive = target.PlayerState.CurrentHPWire > 0;
                OnMonsterAttackResolved?.Invoke(monster, target, activeSkillHPShifted, target.PlayerState.CurrentHPWire);
                return;
            }

            RoomRuntime runtime = GetRoomRuntimeForMonster(monster);
            if (runtime == null)
            {
                Debug.LogError($"[MON-DAMAGE-ENTITY-SYNCH-INFO] {monster?.Name ?? "monster"}#{monster?.EntityId ?? 0} missing room runtime source={source ?? "unknown"}");
                return;
            }
            MersenneTwister rng = runtime.RoomRng;
            if (rng == null)
            {
                Debug.LogError($"[MON-DAMAGE-ENTITY-SYNCH-INFO] {monster?.Name ?? "monster"}#{monster?.EntityId ?? 0} missing room RNG instance='{runtime.InstanceKey}' source={source ?? "unknown"}");
                return;
            }

            WeaponDamageInput damageInput = CreateMonsterWeaponDamageInput(monster, target, rng, source);
            WeaponDamageResult damageResult = DamageResolver.ResolveWeaponDamage(damageInput);
            int attackRating = damageResult.AttackRating;
            int defenseRating = damageResult.DefenseRating;
            int defenderLevel = damageResult.DefenderLevel;
            int attackerLevel = damageResult.AttackerLevel;
            int hitThreshold = damageResult.HitThreshold;
            float hitChance = hitThreshold / 256f;
            uint hitRaw = damageResult.HitRaw;
            uint blockRaw = damageResult.BlockRaw;
            int hitRoll = damageResult.HitRoll;
            int blockRoll = damageResult.BlockRoll;
            int blockChance = damageResult.BlockChance;
            bool hit = damageResult.IsHit;
            bool blocked = damageResult.IsBlocked;
            if (hit && !blocked)
            {
                uint damageRaw = damageResult.DamageRaw;
                uint damageWire = damageResult.DamageWire;
                int minDamage = damageResult.MinDamageF32;
                int maxDamage = damageResult.MaxDamageF32;
                int averageDamage = (minDamage + maxDamage) / 2;
                uint currentHPWire = target.PlayerState.CurrentHPWire;
                DamageQueryResult query = ApplyPlayerDamageQueryWire(damageWire, target, monster, damageResult.DamageTypeId, 0, clientNow, source ?? marker, attackerLevel, rng, false);
                uint adjustedDamageWire = query.AdjustedDamageWire;
                if (adjustedDamageWire == 0)
                {
                    uint sameHPWire = target.PlayerState.CurrentHPWire;
                    target.IsAlive = sameHPWire > 0;
                    Debug.LogError($"[{marker}] {monster.Name}#{monster.EntityId}->{target.Name} no-damage source={source} dmg={damageWire / 256f:F2} adjusted=0 hp={sameHPWire / 256f:F2}/{target.PlayerState.MaxHPWire / 256f:F2} range=[{minDamage / 256f:F2},{maxDamage / 256f:F2}] avg={averageDamage / 256f:F2} ar={attackRating} dr={defenseRating} levels={attackerLevel}->{defenderLevel} chance={hitChance:F1} rngPos={rng.CallsSinceReseed} anim={monster.AttackAnimationIndex} use=0x{monster.AttackUseRaw:X8} sound=0x{monster.AttackSoundRaw:X8} soundGate=0x{monster.AttackSoundGateRaw:X8} soundRepeat=0x{monster.AttackSoundRepeatRaw:X8} hit=0x{hitRaw:X8}/{hitRoll} block=0x{blockRaw:X8}/{blockRoll}/{blockChance} dmgRaw=0x{damageRaw:X8} dist={dist:F1} result={query.ResultName}");
                    Debug.LogError($"[CLIENT-DAMAGE-CONTRACT] source={source ?? marker ?? "monster"} target={target.Name}#{target.EntityId} weaponClassId={damageResult.WeaponClassId} damageTypeId={damageResult.DamageTypeId} rawRollWire={damageWire} preQueryWire={damageWire} damageTakenMod={query.DamageTakenMod:F2} typeMod={query.DamageTypeMod:F2} postQueryWire=0 hp={currentHPWire}->{sameHPWire}/{target.PlayerState.MaxHPWire} resist={query.ResultName} resistRaw=0x{query.ResistRaw:X8} resistChance={query.ResistChanceWire / 256f:F2} clientDamageTime={clientNow:F3} clientTick=0 target=Avatar");
                    Debug.LogError($"[PLAYER-DAMAGE] source=monster attacker={monster.Name}#{monster.EntityId} target={target.Name}#{target.EntityId} result={query.ResultName} damageWire=0 preQueryWire={damageWire} hp={sameHPWire}->{sameHPWire}/{target.PlayerState.MaxHPWire} marker={marker} source={source} hitRaw=0x{hitRaw:X8} blockRaw=0x{blockRaw:X8} damageRaw=0x{damageRaw:X8} rngSeed=0x{runtime.Seed:X8} rngPos={rng.CallsSinceReseed}");
                    Debug.LogError($"[COMBAT-EVENT] actor=monster actorId={monster.EntityId} target=player targetId={target.EntityId} result={query.ResultName} damageWire=0 hp={sameHPWire}->{sameHPWire} hitRaw=0x{hitRaw:X8} hitRoll={hitRoll} threshold={hitThreshold} blockRaw=0x{blockRaw:X8} blockRoll={blockRoll} blockChance={blockChance} damageRaw=0x{damageRaw:X8} resist={DamageResistLogCode(query)} rngAfter={rng.CallsSinceReseed} marker={marker} source={source}");
                    OnMonsterAttackResolved?.Invoke(monster, target, false, sameHPWire);
                }
                else
                {
                    target.PlayerState.TakeQueriedDamage(adjustedDamageWire, clientNow);
                    uint newHPWire = target.PlayerState.CurrentHPWire;
                    target.IsAlive = newHPWire > 0;
                    uint effectRaw = ConsumeOnApplyDamageEffectRng(rng, "monster", target.EntityId, target.Name, currentHPWire, newHPWire, target.PlayerState.MaxHPWire, adjustedDamageWire, source ?? marker ?? "Weapon::applyDamage");
                    if (currentHPWire > newHPWire)
                        ApplyMonsterOnDamageCallback(monster, currentHPWire - newHPWire, clientNow, source ?? marker);
                    if (newHPWire == 0)
                        RemovePlayerDamageModifiersForTarget(target.EntityId, source ?? marker ?? "monster-damage-death");
                    Debug.LogError($"[{marker}] {monster.Name}#{monster.EntityId}->{target.Name} HIT source={source} dmg={adjustedDamageWire / 256f:F2} preQuery={damageWire / 256f:F2} hp={currentHPWire / 256f:F2}->{newHPWire / 256f:F2}/{target.PlayerState.MaxHPWire / 256f:F2} range=[{minDamage / 256f:F2},{maxDamage / 256f:F2}] avg={averageDamage / 256f:F2} ar={attackRating} dr={defenseRating} levels={attackerLevel}->{defenderLevel} chance={hitChance:F1} rngPos={rng.CallsSinceReseed} anim={monster.AttackAnimationIndex} use=0x{monster.AttackUseRaw:X8} sound=0x{monster.AttackSoundRaw:X8} soundGate=0x{monster.AttackSoundGateRaw:X8} soundRepeat=0x{monster.AttackSoundRepeatRaw:X8} hit=0x{hitRaw:X8}/{hitRoll} block=0x{blockRaw:X8}/{blockRoll}/{blockChance} dmgRaw=0x{damageRaw:X8} dist={dist:F1} result={query.ResultName}");
                    Debug.LogError($"[CLIENT-DAMAGE-CONTRACT] source={source ?? marker ?? "monster"} target={target.Name}#{target.EntityId} weaponClassId={damageResult.WeaponClassId} damageTypeId={damageResult.DamageTypeId} rawRollWire={damageWire} preQueryWire={damageWire} damageTakenMod={query.DamageTakenMod:F2} typeMod={query.DamageTypeMod:F2} postQueryWire={adjustedDamageWire} hp={currentHPWire}->{newHPWire}/{target.PlayerState.MaxHPWire} resist={query.ResultName} resistRaw=0x{query.ResistRaw:X8} resistChance={query.ResistChanceWire / 256f:F2} clientDamageTime={clientNow:F3} clientTick=0 target=Avatar");
                    Debug.LogError($"[PLAYER-DAMAGE] source=monster attacker={monster.Name}#{monster.EntityId} target={target.Name}#{target.EntityId} result=HIT damageWire={adjustedDamageWire} preQueryWire={damageWire} hp={currentHPWire}->{newHPWire}/{target.PlayerState.MaxHPWire} marker={marker} source={source} hitRaw=0x{hitRaw:X8} blockRaw=0x{blockRaw:X8} damageRaw=0x{damageRaw:X8} effectRaw=0x{effectRaw:X8} rngSeed=0x{runtime.Seed:X8} rngPos={rng.CallsSinceReseed}");
                    Debug.LogError($"[COMBAT-EVENT] actor=monster actorId={monster.EntityId} target=player targetId={target.EntityId} result=HIT damageWire={adjustedDamageWire} hp={currentHPWire}->{newHPWire} hitRaw=0x{hitRaw:X8} hitRoll={hitRoll} threshold={hitThreshold} blockRaw=0x{blockRaw:X8} blockRoll={blockRoll} blockChance={blockChance} damageRaw=0x{damageRaw:X8} effectRaw=0x{effectRaw:X8} resist={DamageResistLogCode(query)} rngAfter={rng.CallsSinceReseed} marker={marker} source={source}");
                    OnMonsterAttackResolved?.Invoke(monster, target, true, newHPWire);
                }
            }
            else if (blocked)
            {
                Debug.LogError($"[{marker}] {monster.Name}#{monster.EntityId}->{target.Name} block source={source} ar={attackRating} dr={defenseRating} levels={attackerLevel}->{defenderLevel} chance={hitChance:F1} rngPos={rng.CallsSinceReseed} anim={monster.AttackAnimationIndex} use=0x{monster.AttackUseRaw:X8} sound=0x{monster.AttackSoundRaw:X8} soundGate=0x{monster.AttackSoundGateRaw:X8} soundRepeat=0x{monster.AttackSoundRepeatRaw:X8} hit=0x{hitRaw:X8}/{hitRoll} block=0x{blockRaw:X8}/{blockRoll}/{blockChance} dist={dist:F1}");
                Debug.LogError($"[PLAYER-DAMAGE] source=monster attacker={monster.Name}#{monster.EntityId} target={target.Name}#{target.EntityId} result=BLOCK damageWire=0 hp={target.PlayerState.CurrentHPWire}->{target.PlayerState.CurrentHPWire}/{target.PlayerState.MaxHPWire} marker={marker} source={source} hitRaw=0x{hitRaw:X8} blockRaw=0x{blockRaw:X8} rngSeed=0x{runtime.Seed:X8} rngPos={rng.CallsSinceReseed}");
                Debug.LogError($"[COMBAT-EVENT] actor=monster actorId={monster.EntityId} target=player targetId={target.EntityId} result=BLOCK damageWire=0 hp={target.PlayerState.CurrentHPWire}->{target.PlayerState.CurrentHPWire} hitRaw=0x{hitRaw:X8} hitRoll={hitRoll} threshold={hitThreshold} blockRaw=0x{blockRaw:X8} blockRoll={blockRoll} blockChance={blockChance} resist=0 rngAfter={rng.CallsSinceReseed} marker={marker} source={source}");
                OnMonsterAttackResolved?.Invoke(monster, target, false, target.PlayerState.CurrentHPWire);
            }
            else
            {
                Debug.LogError($"[{marker}] {monster.Name}#{monster.EntityId}->{target.Name} miss source={source} ar={attackRating} dr={defenseRating} levels={attackerLevel}->{defenderLevel} chance={hitChance:F1} rngPos={rng.CallsSinceReseed} anim={monster.AttackAnimationIndex} use=0x{monster.AttackUseRaw:X8} sound=0x{monster.AttackSoundRaw:X8} soundGate=0x{monster.AttackSoundGateRaw:X8} soundRepeat=0x{monster.AttackSoundRepeatRaw:X8} hit=0x{hitRaw:X8}/{hitRoll} threshold={hitThreshold} block=0x{blockRaw:X8}/{blockRoll}/{blockChance} dist={dist:F1}");
                Debug.LogError($"[PLAYER-DAMAGE] source=monster attacker={monster.Name}#{monster.EntityId} target={target.Name}#{target.EntityId} result=MISS damageWire=0 hp={target.PlayerState.CurrentHPWire}->{target.PlayerState.CurrentHPWire}/{target.PlayerState.MaxHPWire} marker={marker} source={source} hitRaw=0x{hitRaw:X8} blockRaw=0x{blockRaw:X8} rngSeed=0x{runtime.Seed:X8} rngPos={rng.CallsSinceReseed}");
                Debug.LogError($"[COMBAT-EVENT] actor=monster actorId={monster.EntityId} target=player targetId={target.EntityId} result=MISS damageWire=0 hp={target.PlayerState.CurrentHPWire}->{target.PlayerState.CurrentHPWire} hitRaw=0x{hitRaw:X8} hitRoll={hitRoll} threshold={hitThreshold} blockRaw=0x{blockRaw:X8} blockRoll={blockRoll} blockChance={blockChance} resist=0 rngAfter={rng.CallsSinceReseed} marker={marker} source={source}");
                OnMonsterAttackResolved?.Invoke(monster, target, false, target.PlayerState.CurrentHPWire);
            }
        }

        public bool TryGetCombatPlayer(uint entityId, out CombatPlayer player)
        {
            return _players.TryGetValue(entityId, out player) && player != null;
        }

        public bool ResolvePlayerVsPlayerDamageWire(PlayerState attackerState, int attackerLevel, string attackerName, CombatPlayer target, string instanceKey, string source, out uint oldHPWire, out uint newHPWire, out bool killed)
        {
            oldHPWire = target?.PlayerState?.CurrentHPWire ?? 0;
            newHPWire = oldHPWire;
            killed = false;
            if (attackerState == null || target?.PlayerState == null)
                return false;

            MersenneTwister rng = !string.IsNullOrWhiteSpace(instanceKey) ? GetRoomRngForInstance(instanceKey) : null;
            if (rng == null)
            {
                Debug.LogError($"[PVP-DAMAGE] attacker={attackerName} target={target.Name}#{target.EntityId} missing-room-rng instance='{instanceKey}' source={source ?? "unknown"}");
                return false;
            }

            float clientNow = GetCombatTime();
            target.PlayerState.AdvanceEntitySynchInfoHP(clientNow, $"{source ?? "PVP-DAMAGE"}-pre-damage");
            oldHPWire = target.PlayerState.CurrentHPWire;

            int defenderLevel = Math.Max(1, target.PlayerState.Level);
            var damageInput = new WeaponDamageInput
            {
                Rng = rng,
                Source = source ?? "PvpWeapon",
                AttackerLevel = Math.Max(0, attackerLevel),
                DefenderLevel = defenderLevel,
                AttackRating = DamageResolver.ResolveAvatarAttackRating(attackerState),
                DefenseRating = Math.Max(0, target.PlayerState.ArmorDefenseRating),
                BlockChance = 0,
                DamageLevel = DamageResolver.ResolveWeaponDamageLevel(attackerState),
                DamageBonus = DamageResolver.ResolveWeaponDamageBonus(attackerState),
                DamageMod = DamageResolver.ResolveDamageMod(attackerState),
                WeaponClassId = DamageResolver.ResolveWeaponClassId(attackerState),
                DamageTypeId = DamageResolver.ResolveDamageTypeId(attackerState),
                WeaponDamageF32 = DamageResolver.GetWeaponBaseDamageF32(attackerState),
                WeaponVolatilityF32 = DamageResolver.GetWeaponVolatilityF32(attackerState),
                CritThreshold = DamageResolver.ResolveCriticalThreshold(attackerState, null),
                CritDamagePercent = DamageResolver.ResolveCriticalDamagePercent(attackerState),
                AttackerState = attackerState,
                IncludeWeaponDamageAdds = true
            };

            WeaponDamageResult damageResult = DamageResolver.ResolveWeaponDamage(damageInput);
            uint hitRaw = damageResult.HitRaw;
            uint blockRaw = damageResult.BlockRaw;
            int hitRoll = damageResult.HitRoll;
            int blockRoll = damageResult.BlockRoll;
            int hitThreshold = damageResult.HitThreshold;
            int attackerLevelOut = damageResult.AttackerLevel;

            if (!damageResult.IsHit || damageResult.IsBlocked)
            {
                Debug.LogError($"[PVP-DAMAGE] attacker={attackerName} target={target.Name}#{target.EntityId} result={(damageResult.IsBlocked ? "BLOCK" : "MISS")} hp={oldHPWire}->{oldHPWire}/{target.PlayerState.MaxHPWire} hitRaw=0x{hitRaw:X8} hitRoll={hitRoll} threshold={hitThreshold} blockRaw=0x{blockRaw:X8} blockRoll={blockRoll} rngPos={rng.CallsSinceReseed} source={source}");
                newHPWire = oldHPWire;
                return true;
            }

            uint damageWire = damageResult.DamageWire;
            DamageQueryResult query = ApplyPlayerDamageQueryWire(damageWire, target, null, damageResult.DamageTypeId, 0, clientNow, source ?? "PvpWeapon", attackerLevelOut, rng, true);
            uint adjustedDamageWire = query.AdjustedDamageWire;
            if (adjustedDamageWire == 0)
            {
                newHPWire = target.PlayerState.CurrentHPWire;
                target.IsAlive = newHPWire > 0;
                Debug.LogError($"[PVP-DAMAGE] attacker={attackerName} target={target.Name}#{target.EntityId} result={query.ResultName} no-damage preQuery={damageWire} hp={newHPWire}/{target.PlayerState.MaxHPWire} rngPos={rng.CallsSinceReseed} source={source}");
                return true;
            }

            target.PlayerState.TakeQueriedDamage(adjustedDamageWire, clientNow);
            newHPWire = target.PlayerState.CurrentHPWire;
            target.IsAlive = newHPWire > 0;
            killed = newHPWire == 0;
            if (killed)
                RemovePlayerDamageModifiersForTarget(target.EntityId, source ?? "pvp-damage-death");
            Debug.LogError($"[PVP-DAMAGE] attacker={attackerName} target={target.Name}#{target.EntityId} result=HIT damageWire={adjustedDamageWire} preQuery={damageWire} hp={oldHPWire}->{newHPWire}/{target.PlayerState.MaxHPWire} hitRaw=0x{hitRaw:X8} hitRoll={hitRoll} threshold={hitThreshold} rngPos={rng.CallsSinceReseed} source={source}");
            return true;
        }

        private bool TryApplyMonsterActiveSkillEffect(Monster monster, CombatPlayer target, float clientNow, float dist, string marker, string source, out bool handled, out bool hpShifted)
        {
            handled = false;
            hpShifted = false;
            if (monster == null || target?.PlayerState == null || !monster.UsePrimaryActiveSkillThisAttack)
                return false;

            if (!TryResolveMonsterHitPointRegenSkillEffect(monster, out MonsterHitPointRegenSkillEffect effect))
            {
                if (TryResolveMonsterWeaponDamageSkillEffect(monster, out MonsterWeaponDamageSkillEffect weaponEffect))
                {
                    handled = true;
                    CommitMonsterPrimarySkillUse(monster, $"{marker ?? "MON-SKILL"}:ActiveSkill::use@0x00538DD0");
                    hpShifted = ApplyMonsterWeaponDamageSkillEffect(monster, target, weaponEffect, clientNow, dist, marker, source);
                    return true;
                }

                if (TryResolveMonsterStunActionSkillEffect(monster, out MonsterStunActionSkillEffect stunEffect))
                {
                    handled = true;
                    CommitMonsterPrimarySkillUse(monster, $"{marker ?? "MON-SKILL"}:ActiveSkill::use@0x00538DD0");
                    ApplyMonsterStunActionSkillEffect(monster, target, stunEffect, marker, source);
                    return true;
                }

                handled = true;
                CommitMonsterPrimarySkillUse(monster, $"{marker ?? "MON-SKILL"}:ActiveSkill::use@0x00538DD0");
                MonsterSkillEffectSupport support = ResolveMonsterSkillEffectSupport(monster.PrimaryActiveSkillPath, monster.PrimaryActiveSkillEffect);
                string families = support != null && support.Families.Count > 0 ? string.Join(",", support.Families) : "none";
                string unsupported = support != null && support.UnsupportedFamilies.Count > 0 ? string.Join(",", support.UnsupportedFamilies) : "none";
                Debug.LogError($"[MON-SKILL-EFFECT] state=unhandled monster={monster.Name}#{monster.EntityId} target={target.Name} skill={monster.PrimaryActiveSkillPath ?? "none"} effect={monster.PrimaryActiveSkillEffect ?? "none"} families={families} status={support?.Status ?? "UNKNOWN"} unsupported={unsupported} source={source ?? marker ?? "unknown"} reason=unsupported-effect sourceFunction=ActiveSkill::doSkillEffect@0x00539630");
                return true;
            }

            handled = true;
            CommitMonsterPrimarySkillUse(monster, $"{marker ?? "MON-SKILL"}:ActiveSkill::use@0x00538DD0");
            uint beforeHP = target.PlayerState.CurrentHPWire;
            uint powerLevel = ResolveMonsterSkillPowerLevelWire(monster, effect.SkillPath);
            string modifierKey = BuildPlayerRuntimeModifierKey(target.EntityId, monster.EntityId, effect.ModifierPath, effect.StackRule, CLIENT_RUNTIME_MODIFIER_SOURCE_IS_SELF);
            bool replaceModifier = _playerModifierNetworkIds.ContainsKey(modifierKey);
            bool stackAccepted = target.PlayerState.ShouldAcceptAttributeModifier(modifierKey, effect.ModifierPath, effect.StackRule, powerLevel, effect.DurationTicks);
            if (!stackAccepted)
            {
                RaisePlayerModifierAdd(monster, target, modifierKey, effect.ModifierPath, 1, powerLevel, effect.DurationTicks, CLIENT_RUNTIME_MODIFIER_SOURCE_IS_SELF, replaceModifier, effect.SkillPath, effect.EffectPath, source ?? marker ?? "unknown", "SpellModEffect::doEffect@0x00554460 Modifiers::processAddModifier@0x00502280", false);
                uint rejectedHP = target.PlayerState.CurrentHPWire;
                Debug.LogError($"[PLAYER-MODIFIER-STACK] result=reject target={target.Name}#{target.EntityId} source={monster.Name}#{monster.EntityId} skill={effect.SkillPath} modifier={effect.ModifierPath} stack={effect.StackRule ?? "UNKNOWN"} key={modifierKey} incomingPower={powerLevel} incomingDuration={effect.DurationTicks} sourceIsSelf={CLIENT_RUNTIME_MODIFIER_SOURCE_IS_SELF} sourceFunction=Modifiers::addModifierLocal@0x00501770");
                Debug.LogError($"[SPELL-MOD] skill={effect.SkillPath} effect={effect.EffectPath} modifier={effect.ModifierPath} attr=HIT_POINT_REGEN_BONUS value={effect.HitPointRegenBonus} durationSeconds={effect.DurationSeconds:F2} durationTicks={effect.DurationTicks} applied=False hp={beforeHP}->{rejectedHP}/{target.PlayerState.MaxHPWire} source={source ?? marker ?? "unknown"} sourceFunction=SpellModEffect::doEffect@0x00554460 chanceDraw=false stack={effect.StackRule ?? ""} stackAccepted=False");
                return true;
            }
            bool applied = target.PlayerState.ApplyHitPointRegenBonusModifier(
                effect.ModifierPath,
                effect.HitPointRegenBonus,
                effect.DurationTicks,
                effect.RemoveOnDeath,
                clientNow,
                $"{marker ?? "MON-SKILL"}:{effect.SkillPath}",
                modifierKey,
                monster.EntityId,
                effect.SkillPath,
                effect.EffectPath,
                powerLevel,
                effect.StackRule);
            uint afterHP = target.PlayerState.CurrentHPWire;
            hpShifted = afterHP != beforeHP;
            if (applied)
                RaisePlayerModifierAdd(monster, target, modifierKey, effect.ModifierPath, 1, powerLevel, effect.DurationTicks, CLIENT_RUNTIME_MODIFIER_SOURCE_IS_SELF, replaceModifier, effect.SkillPath, effect.EffectPath, source ?? marker ?? "unknown", "SpellModEffect::doEffect@0x00554460 Modifiers::processAddModifier@0x00502280");
            Debug.LogError($"[SPELL-MOD] skill={effect.SkillPath} effect={effect.EffectPath} modifier={effect.ModifierPath} attr=HIT_POINT_REGEN_BONUS value={effect.HitPointRegenBonus} durationSeconds={effect.DurationSeconds:F2} durationTicks={effect.DurationTicks} power={powerLevel} applied={applied} hp={beforeHP}->{afterHP}/{target.PlayerState.MaxHPWire} source={source ?? marker ?? "unknown"} sourceFunction=SpellModEffect::doEffect@0x00554460 chanceDraw=false stack={effect.StackRule ?? ""} stackAccepted=True");
            return true;
        }

        private void LogMonsterActiveSkillEffectSupport(Monster monster, MonsterActiveSkillRuntime skill)
        {
            if (monster == null || skill == null || string.IsNullOrWhiteSpace(skill.Path))
                return;
            MonsterSkillEffectSupport support = ResolveMonsterSkillEffectSupport(skill.Path, skill.Effect);
            string families = support.Families.Count > 0 ? string.Join(",", support.Families) : "none";
            string unsupported = support.UnsupportedFamilies.Count > 0 ? string.Join(",", support.UnsupportedFamilies) : "none";
            Debug.LogError($"[MON-SKILL-EFFECT-SUPPORT] monster={monster.Name}#{monster.EntityId} skill={skill.Path} effect={support.EffectPath ?? skill.Effect ?? "none"} families={families} status={support.Status} unsupported={unsupported} modifier={support.ModifierPath ?? ""} attr={support.Attribute ?? ""} reason={support.Reason ?? ""} sourceFunction=ActiveSkill::doSkillEffect@0x00539630 SpellModEffect::doEffect@0x00554460 SpellWeaponDamageEffect::doEffect@0x0055E460 SpellKnockBackEffect::doEffect@0x00552C80 SpellKnockDownEffect::doEffect@0x005534F0");
        }

        private MonsterSkillEffectSupport ResolveMonsterSkillEffectSupport(string skillPath, string fallbackEffectPath)
        {
            var support = new MonsterSkillEffectSupport
            {
                SkillPath = skillPath,
                EffectPath = fallbackEffectPath
            };

            var gc = GCDatabase.Instance;
            if (gc == null || !gc.IsLoaded)
            {
                support.Status = "UNRESOLVED";
                support.Reason = "missing-gc-database";
                return support;
            }

            GCNode skill = gc.ResolveWithInheritance(skillPath);
            GCNode skillDesc = skill?.GetChild("Description") ?? skill;
            string effectPath = skillDesc?.GetString("Effect", fallbackEffectPath) ?? fallbackEffectPath;
            support.EffectPath = effectPath;
            if (string.IsNullOrWhiteSpace(effectPath))
            {
                support.Status = "NO_EFFECT";
                support.Reason = "skill-has-no-effect-property";
                return support;
            }

            GCNode effectNode = ResolveAuthoredNodeReference(effectPath, skill);
            if (effectNode == null)
            {
                support.Status = "UNRESOLVED";
                support.Reason = "effect-path-unresolved";
                return support;
            }

            foreach (GCNode effectChild in EnumerateEffectChildren(effectNode))
            {
                string family = NormalizeEffectFamily(effectChild.Extends);
                if (string.IsNullOrWhiteSpace(family) || support.Families.Contains(family))
                    continue;
                support.Families.Add(family);
            }

            if (support.Families.Count == 0)
            {
                support.Status = "NO_EFFECT_CHILDREN";
                support.Reason = "effect-node-has-no-client-effect-children";
                return support;
            }

            bool hasSupportedHpRegenMod = TryClassifyHitPointRegenBonusModEffect(effectNode, skill, out string modifierPath, out string attribute);
            support.ModifierPath = modifierPath;
            support.Attribute = attribute;

            foreach (string family in support.Families)
            {
                if (family == "SpellSoundEffect")
                    continue;
                if (family == "SpellModEffect" && hasSupportedHpRegenMod)
                    continue;
                if (family == "SpellWeaponDamageEffect" || family == "SpellKnockBackEffect" || family == "SpellKnockDownEffect" || family == "SpellDamageEffect")
                    continue;
                support.UnsupportedFamilies.Add(family);
            }

            if (support.UnsupportedFamilies.Count > 0)
            {
                support.Status = "UNRESOLVED";
                support.Reason = "unhandled-client-effect-family";
            }
            else if (support.Families.Contains("SpellModEffect") && hasSupportedHpRegenMod && support.Families.All(f => f == "SpellModEffect" || f == "SpellSoundEffect"))
            {
                support.Status = "SUPPORTED_HP_REGEN_BONUS";
                support.Reason = "server-mirrors-SpellModEffect-HIT_POINT_REGEN_BONUS-runtime";
            }
            else if (support.Families.Contains("SpellWeaponDamageEffect"))
            {
                bool hasStunAction = support.Families.Contains("SpellKnockBackEffect") || support.Families.Contains("SpellKnockDownEffect");
                support.Status = hasStunAction ? "INCOMPLETE_WEAPON_DAMAGE_STUN_ACTION" : "INCOMPLETE_WEAPON_DAMAGE";
                support.Reason = hasStunAction ? "weapon-damage-hp-mirror-and-stun-action-rng-without-full-modifier-replication" : "weapon-damage-hp-mirror-exists";
            }
            else if (support.Families.Contains("SpellDamageEffect"))
            {
                support.Status = "INCOMPLETE_SPELL_DAMAGE";
                support.Reason = "SpellDamageEffect/DOT/client-modifier-update-timing-not-closed";
            }
            else if (support.Families.Contains("SpellKnockBackEffect") || support.Families.Contains("SpellKnockDownEffect"))
            {
                support.Status = "INCOMPLETE_STUN_ACTION";
                support.Reason = "server-mirrors-SpellEffect-CheckChance-and-Unit-CheckStunResist-rng-without-full-modifier-replication";
            }
            else
            {
                support.Status = "INCOMPLETE";
                support.Reason = "client-effect-family-present-without-runtime-mirror";
            }

            return support;
        }

        private static IEnumerable<GCNode> EnumerateEffectChildren(GCNode effectNode)
        {
            if (effectNode == null)
                yield break;
            foreach (var child in EnumerateEffectTree(effectNode))
                yield return child;
        }

        private static IEnumerable<GCNode> EnumerateEffectTree(GCNode effectNode)
        {
            if (effectNode == null)
                yield break;
            string family = NormalizeEffectFamily(effectNode.Extends);
            if (!string.IsNullOrWhiteSpace(family)
                && !string.Equals(family, "SpellEffect", StringComparison.OrdinalIgnoreCase))
                yield return effectNode;
            foreach (var child in effectNode.AnonymousChildren)
            {
                foreach (var nested in EnumerateEffectTree(child))
                    yield return nested;
            }
            foreach (var child in effectNode.Children.Values)
            {
                foreach (var nested in EnumerateEffectTree(child))
                    yield return nested;
            }
        }

        private static string NormalizeEffectFamily(string extendsRaw)
        {
            if (string.IsNullOrWhiteSpace(extendsRaw))
                return "";
            string value = extendsRaw.Replace('\\', '.').Replace('/', '.');
            int dot = value.LastIndexOf('.');
            if (dot >= 0 && dot + 1 < value.Length)
                value = value.Substring(dot + 1);
            return value.EndsWith("Effect", StringComparison.OrdinalIgnoreCase) ? value : "";
        }

        private bool TryClassifyHitPointRegenBonusModEffect(GCNode effectNode, GCNode skillContext, out string modifierPath, out string attribute)
        {
            modifierPath = null;
            attribute = null;
            GCNode modEffect = FindEffectChild(effectNode, "SpellModEffect");
            if (modEffect == null)
                return false;

            modifierPath = modEffect.GetString("Modifier", null);
            if (string.IsNullOrWhiteSpace(modifierPath))
                return false;

            GCNode modifier = ResolveAuthoredNodeReference(modifierPath, skillContext);
            GCNode modifierDesc = modifier?.GetChild("Description") ?? modifier;
            GCNode attributeNode = FindEffectChild(modifierDesc, "Attribute");
            attribute = attributeNode?.GetString("Attribute", null);
            return string.Equals(attribute, "HIT_POINT_REGEN_BONUS", StringComparison.OrdinalIgnoreCase);
        }

        private bool ApplyMonsterWeaponDamageSkillEffect(Monster monster, CombatPlayer target, MonsterWeaponDamageSkillEffect effect, float clientNow, float dist, string marker, string source)
        {
            RoomRuntime runtime = GetRoomRuntimeForMonster(monster);
            MersenneTwister rng = runtime?.RoomRng;
            if (rng == null)
            {
                Debug.LogError($"[SPELL-WEAPON-DAMAGE] result=NO_RNG attacker={monster?.Name ?? "monster"}#{monster?.EntityId ?? 0} target={target?.Name ?? "unknown"} skill={effect?.SkillPath ?? "none"} source={source ?? marker ?? "unknown"} reason=missing-room-rng sourceFunction=SpellWeaponDamageEffect::doEffect@0x0055E460");
                return false;
            }

            WeaponDamageInput damageInput = CreateMonsterWeaponDamageInput(monster, target, rng, $"{marker ?? "MON-SKILL"}:SpellWeaponDamageEffect");
            int baseAttackRating = damageInput.AttackRating;
            int baseDamageMod = damageInput.DamageMod;
            if (effect.AttackRatingMod != 0)
                damageInput.AttackRating = Math.Max(0, (damageInput.AttackRating * (100 + effect.AttackRatingMod)) / 100);
            if (effect.DamageMod != 0)
                damageInput.DamageMod = Math.Max(0, damageInput.DamageMod + effect.DamageMod);

            WeaponDamageResult damageResult = DamageResolver.ResolveWeaponDamage(damageInput);
            bool hpShifted = false;
            uint beforeHP = target.PlayerState.CurrentHPWire;
            if (damageResult.IsHit && !damageResult.IsBlocked)
            {
                uint damageWire = damageResult.DamageWire;
                DamageQueryResult query = ApplyPlayerDamageQueryWire(damageWire, target, monster, damageResult.DamageTypeId, 0, clientNow, source ?? marker ?? "SpellWeaponDamageEffect", damageResult.AttackerLevel, rng, true);
                uint adjustedDamageWire = query.AdjustedDamageWire;
                if (adjustedDamageWire == 0)
                {
                    uint sameHP = target.PlayerState.CurrentHPWire;
                    target.IsAlive = sameHP > 0;
                    Debug.LogError($"[SPELL-WEAPON-DAMAGE] {monster.Name}#{monster.EntityId}->{target.Name} no-damage skill={effect.SkillPath} effect={effect.WeaponEffectPath} dmg={damageWire / 256f:F2} adjusted=0 hp={sameHP}->{sameHP}/{target.PlayerState.MaxHPWire} arMod={effect.AttackRatingMod} ar={baseAttackRating}->{damageInput.AttackRating} dmgMod={baseDamageMod}->{damageInput.DamageMod} hit=0x{damageResult.HitRaw:X8}/{damageResult.HitRoll} block=0x{damageResult.BlockRaw:X8}/{damageResult.BlockRoll}/{damageResult.BlockChance} dmgRaw=0x{damageResult.DamageRaw:X8} rngPos={rng.CallsSinceReseed} result={query.ResultName} sourceFunction=SpellWeaponDamageEffect::doEffect@0x0055E460");
                    Debug.LogError($"[CLIENT-DAMAGE-CONTRACT] source={source ?? marker ?? "SpellWeaponDamageEffect"} target={target.Name}#{target.EntityId} weaponClassId={damageResult.WeaponClassId} damageTypeId={damageResult.DamageTypeId} rawRollWire={damageWire} preQueryWire={damageWire} damageTakenMod={query.DamageTakenMod:F2} typeMod={query.DamageTypeMod:F2} postQueryWire=0 hp={beforeHP}->{sameHP}/{target.PlayerState.MaxHPWire} resist={query.ResultName} resistRaw=0x{query.ResistRaw:X8} resistChance={query.ResistChanceWire / 256f:F2} clientDamageTime={clientNow:F3} clientTick=0 target=Avatar");
                    Debug.LogError($"[PLAYER-DAMAGE] source=monster-skill attacker={monster.Name}#{monster.EntityId} target={target.Name}#{target.EntityId} result={query.ResultName} skill={effect.SkillPath} damageWire=0 preQueryWire={damageWire} hp={sameHP}->{sameHP}/{target.PlayerState.MaxHPWire} marker={marker} source={source} hitRaw=0x{damageResult.HitRaw:X8} blockRaw=0x{damageResult.BlockRaw:X8} damageRaw=0x{damageResult.DamageRaw:X8} rngSeed=0x{(runtime?.Seed ?? 0u):X8} rngPos={rng.CallsSinceReseed}");
                    Debug.LogError($"[COMBAT-EVENT] actor=monster actorId={monster.EntityId} target=player targetId={target.EntityId} result={query.ResultName} skill={effect.SkillPath} damageWire=0 hp={sameHP}->{sameHP} hitRaw=0x{damageResult.HitRaw:X8} hitRoll={damageResult.HitRoll} threshold={damageResult.HitThreshold} blockRaw=0x{damageResult.BlockRaw:X8} blockRoll={damageResult.BlockRoll} blockChance={damageResult.BlockChance} damageRaw=0x{damageResult.DamageRaw:X8} resist={DamageResistLogCode(query)} rngAfter={rng.CallsSinceReseed} marker={marker} source={source}");
                }
                else
                {
                    target.PlayerState.TakeQueriedDamage(adjustedDamageWire, clientNow);
                    uint afterHP = target.PlayerState.CurrentHPWire;
                    target.IsAlive = afterHP > 0;
                    hpShifted = afterHP != beforeHP;
                    uint effectRaw = ConsumeOnApplyDamageEffectRng(rng, "monster-skill", target.EntityId, target.Name, beforeHP, afterHP, target.PlayerState.MaxHPWire, adjustedDamageWire, source ?? marker ?? "SpellWeaponDamageEffect");
                    if (beforeHP > afterHP)
                        ApplyMonsterOnDamageCallback(monster, beforeHP - afterHP, clientNow, source ?? marker ?? "SpellWeaponDamageEffect");
                    if (afterHP == 0)
                        RemovePlayerDamageModifiersForTarget(target.EntityId, source ?? marker ?? "SpellWeaponDamageEffect");
                    bool modifierApplied = effect.DamageModifier != null && afterHP > 0 && ApplyPlayerDamageModifierFromMonster(monster, target, effect.DamageModifier, clientNow, source ?? marker ?? "SpellWeaponDamageEffect");
                    Debug.LogError($"[SPELL-WEAPON-DAMAGE] {monster.Name}#{monster.EntityId}->{target.Name} HIT skill={effect.SkillPath} effect={effect.WeaponEffectPath} dmg={adjustedDamageWire / 256f:F2} preQuery={damageWire / 256f:F2} hp={beforeHP / 256f:F2}->{afterHP / 256f:F2}/{target.PlayerState.MaxHPWire / 256f:F2} arMod={effect.AttackRatingMod} ar={baseAttackRating}->{damageInput.AttackRating} dmgMod={baseDamageMod}->{damageInput.DamageMod} hit=0x{damageResult.HitRaw:X8}/{damageResult.HitRoll} block=0x{damageResult.BlockRaw:X8}/{damageResult.BlockRoll}/{damageResult.BlockChance} dmgRaw=0x{damageResult.DamageRaw:X8} effectRaw=0x{effectRaw:X8} modifierApplied={modifierApplied} dist={dist:F1} rngPos={rng.CallsSinceReseed} result={query.ResultName} sourceFunction=Weapon::applyDamage@0x00597E50");
                    Debug.LogError($"[CLIENT-DAMAGE-CONTRACT] source={source ?? marker ?? "SpellWeaponDamageEffect"} target={target.Name}#{target.EntityId} weaponClassId={damageResult.WeaponClassId} damageTypeId={damageResult.DamageTypeId} rawRollWire={damageWire} preQueryWire={damageWire} damageTakenMod={query.DamageTakenMod:F2} typeMod={query.DamageTypeMod:F2} postQueryWire={adjustedDamageWire} hp={beforeHP}->{afterHP}/{target.PlayerState.MaxHPWire} resist={query.ResultName} resistRaw=0x{query.ResistRaw:X8} resistChance={query.ResistChanceWire / 256f:F2} clientDamageTime={clientNow:F3} clientTick=0 target=Avatar");
                    Debug.LogError($"[PLAYER-DAMAGE] source=monster-skill attacker={monster.Name}#{monster.EntityId} target={target.Name}#{target.EntityId} result=HIT skill={effect.SkillPath} damageWire={adjustedDamageWire} preQueryWire={damageWire} hp={beforeHP}->{afterHP}/{target.PlayerState.MaxHPWire} marker={marker} source={source} hitRaw=0x{damageResult.HitRaw:X8} blockRaw=0x{damageResult.BlockRaw:X8} damageRaw=0x{damageResult.DamageRaw:X8} effectRaw=0x{effectRaw:X8} rngSeed=0x{(runtime?.Seed ?? 0u):X8} rngPos={rng.CallsSinceReseed}");
                    Debug.LogError($"[COMBAT-EVENT] actor=monster actorId={monster.EntityId} target=player targetId={target.EntityId} result=HIT skill={effect.SkillPath} damageWire={adjustedDamageWire} hp={beforeHP}->{afterHP} hitRaw=0x{damageResult.HitRaw:X8} hitRoll={damageResult.HitRoll} threshold={damageResult.HitThreshold} blockRaw=0x{damageResult.BlockRaw:X8} blockRoll={damageResult.BlockRoll} blockChance={damageResult.BlockChance} damageRaw=0x{damageResult.DamageRaw:X8} effectRaw=0x{effectRaw:X8} resist={DamageResistLogCode(query)} rngAfter={rng.CallsSinceReseed} marker={marker} source={source}");
                }
            }
            else if (damageResult.IsBlocked)
            {
                Debug.LogError($"[SPELL-WEAPON-DAMAGE] {monster.Name}#{monster.EntityId}->{target.Name} BLOCK skill={effect.SkillPath} effect={effect.WeaponEffectPath} hp={beforeHP}->{beforeHP}/{target.PlayerState.MaxHPWire} arMod={effect.AttackRatingMod} ar={baseAttackRating}->{damageInput.AttackRating} dmgMod={baseDamageMod}->{damageInput.DamageMod} hit=0x{damageResult.HitRaw:X8}/{damageResult.HitRoll} block=0x{damageResult.BlockRaw:X8}/{damageResult.BlockRoll}/{damageResult.BlockChance} dist={dist:F1} rngPos={rng.CallsSinceReseed} sourceFunction=Weapon::applyDamage@0x00597E50");
                Debug.LogError($"[PLAYER-DAMAGE] source=monster-skill attacker={monster.Name}#{monster.EntityId} target={target.Name}#{target.EntityId} result=BLOCK skill={effect.SkillPath} damageWire=0 hp={beforeHP}->{beforeHP}/{target.PlayerState.MaxHPWire} marker={marker} source={source} hitRaw=0x{damageResult.HitRaw:X8} blockRaw=0x{damageResult.BlockRaw:X8} rngSeed=0x{(runtime?.Seed ?? 0u):X8} rngPos={rng.CallsSinceReseed}");
            }
            else
            {
                Debug.LogError($"[SPELL-WEAPON-DAMAGE] {monster.Name}#{monster.EntityId}->{target.Name} MISS skill={effect.SkillPath} effect={effect.WeaponEffectPath} hp={beforeHP}->{beforeHP}/{target.PlayerState.MaxHPWire} arMod={effect.AttackRatingMod} ar={baseAttackRating}->{damageInput.AttackRating} dmgMod={baseDamageMod}->{damageInput.DamageMod} hit=0x{damageResult.HitRaw:X8}/{damageResult.HitRoll} threshold={damageResult.HitThreshold} block=0x{damageResult.BlockRaw:X8}/{damageResult.BlockRoll}/{damageResult.BlockChance} dist={dist:F1} rngPos={rng.CallsSinceReseed} sourceFunction=Weapon::applyDamage@0x00597E50");
                Debug.LogError($"[PLAYER-DAMAGE] source=monster-skill attacker={monster.Name}#{monster.EntityId} target={target.Name}#{target.EntityId} result=MISS skill={effect.SkillPath} damageWire=0 hp={beforeHP}->{beforeHP}/{target.PlayerState.MaxHPWire} marker={marker} source={source} hitRaw=0x{damageResult.HitRaw:X8} blockRaw=0x{damageResult.BlockRaw:X8} rngSeed=0x{(runtime?.Seed ?? 0u):X8} rngPos={rng.CallsSinceReseed}");
            }

            ConsumeWeaponStunActionRng(rng, monster, target, effect, marker, source);
            return hpShifted;
        }

        private bool TryResolveMonsterHitPointRegenSkillEffect(Monster monster, out MonsterHitPointRegenSkillEffect effect)
        {
            effect = null;
            if (monster == null || string.IsNullOrWhiteSpace(monster.PrimaryActiveSkillPath))
                return false;

            var gc = GCDatabase.Instance;
            if (gc == null || !gc.IsLoaded)
                return false;

            GCNode skill = gc.ResolveWithInheritance(monster.PrimaryActiveSkillPath);
            GCNode skillDesc = skill?.GetChild("Description") ?? skill;
            string effectPath = skillDesc?.GetString("Effect", monster.PrimaryActiveSkillEffect) ?? monster.PrimaryActiveSkillEffect;
            if (string.IsNullOrWhiteSpace(effectPath))
                return false;

            GCNode effectNode = ResolveAuthoredNodeReference(effectPath, skill);
            if (effectNode == null)
                return false;

            GCNode modEffect = FindEffectChild(effectNode, "SpellModEffect");
            if (modEffect == null)
                return false;

            string modifierPath = modEffect.GetString("Modifier", null);
            if (string.IsNullOrWhiteSpace(modifierPath))
                return false;

            GCNode modifier = ResolveAuthoredNodeReference(modifierPath, skill);
            GCNode modifierDesc = modifier?.GetChild("Description") ?? modifier;
            if (modifierDesc == null)
                return false;

            GCNode attributeNode = FindEffectChild(modifierDesc, "Attribute");
            if (attributeNode == null)
                return false;

            string attribute = attributeNode.GetString("Attribute", null);
            if (!string.Equals(attribute, "HIT_POINT_REGEN_BONUS", StringComparison.OrdinalIgnoreCase))
                return false;

            int value = attributeNode.GetInt("Value", 0);
            if (value == 0)
                return false;

            float durationSeconds = modEffect.GetFloat("Duration", 0f);
            effect = new MonsterHitPointRegenSkillEffect
            {
                SkillPath = monster.PrimaryActiveSkillPath,
                EffectPath = effectPath,
                ModifierPath = modifierPath,
                HitPointRegenBonus = value,
                DurationSeconds = durationSeconds,
                DurationTicks = ComputeSpellModDurationTicks(durationSeconds),
                RemoveOnDeath = modifierDesc.GetBool("RemoveOnDeath", false),
                StackRule = modifierDesc.GetString("StackRule", "")
            };
            return true;
        }

        private bool TryResolveMonsterWeaponDamageSkillEffect(Monster monster, out MonsterWeaponDamageSkillEffect effect)
        {
            effect = null;
            if (monster == null || string.IsNullOrWhiteSpace(monster.PrimaryActiveSkillPath))
                return false;

            var gc = GCDatabase.Instance;
            if (gc == null || !gc.IsLoaded)
                return false;

            GCNode skill = gc.ResolveWithInheritance(monster.PrimaryActiveSkillPath);
            GCNode skillDesc = skill?.GetChild("Description") ?? skill;
            string effectPath = skillDesc?.GetString("Effect", monster.PrimaryActiveSkillEffect) ?? monster.PrimaryActiveSkillEffect;
            if (string.IsNullOrWhiteSpace(effectPath))
                return false;

            GCNode effectNode = ResolveAuthoredNodeReference(effectPath, skill);
            if (effectNode == null)
                return false;

            GCNode weaponDamage = FindEffectChild(effectNode, "SpellWeaponDamageEffect");
            if (weaponDamage == null)
                return false;

            GCNode knockBack = FindEffectChildByExtends(effectNode, "SpellKnockBackEffect");
            GCNode knockDown = FindEffectChildByExtends(effectNode, "SpellKnockDownEffect");
            TryResolveMonsterDamageModifierSkillEffect(effectNode, skill, monster.PrimaryActiveSkillPath, effectPath, out MonsterDamageModifierSkillEffect damageModifier);
            int skillLevel = 1;
            effect = new MonsterWeaponDamageSkillEffect
            {
                SkillPath = monster.PrimaryActiveSkillPath,
                EffectPath = effectPath,
                WeaponEffectPath = BuildAuthoredEffectPath(effectPath, weaponDamage),
                AttackRatingMod = ResolveSkillLinearMod(weaponDamage, "ARMod", skillLevel),
                DamageMod = ResolveSkillLinearMod(weaponDamage, "DamageMod", skillLevel),
                HasKnockBack = knockBack != null,
                KnockBackEffectPath = knockBack != null ? BuildAuthoredEffectPath(effectPath, knockBack) : null,
                KnockBackStrength = knockBack != null ? ResolveSkillLinearMod(knockBack, "Strength", skillLevel) : 0,
                KnockBackChanceWire = ResolveSpellEffectChanceWire(knockBack),
                HasKnockDown = knockDown != null,
                KnockDownEffectPath = knockDown != null ? BuildAuthoredEffectPath(effectPath, knockDown) : null,
                KnockDownStrength = knockDown != null ? ResolveSkillLinearMod(knockDown, "Strength", skillLevel) : 0,
                KnockDownChanceWire = ResolveSpellEffectChanceWire(knockDown),
                DamageModifier = damageModifier
            };
            return true;
        }

        private bool TryResolveMonsterStunActionSkillEffect(Monster monster, out MonsterStunActionSkillEffect effect)
        {
            effect = null;
            if (monster == null || string.IsNullOrWhiteSpace(monster.PrimaryActiveSkillPath))
                return false;

            var gc = GCDatabase.Instance;
            if (gc == null || !gc.IsLoaded)
                return false;

            GCNode skill = gc.ResolveWithInheritance(monster.PrimaryActiveSkillPath);
            GCNode skillDesc = skill?.GetChild("Description") ?? skill;
            string effectPath = skillDesc?.GetString("Effect", monster.PrimaryActiveSkillEffect) ?? monster.PrimaryActiveSkillEffect;
            if (string.IsNullOrWhiteSpace(effectPath))
                return false;

            GCNode effectNode = ResolveAuthoredNodeReference(effectPath, skill);
            if (effectNode == null)
                return false;
            if (FindEffectChildByExtends(effectNode, "SpellWeaponDamageEffect") != null || FindEffectChildByExtends(effectNode, "SpellDamageEffect") != null)
                return false;

            GCNode knockDown = FindEffectChildByExtends(effectNode, "SpellKnockDownEffect");
            GCNode knockBack = FindEffectChildByExtends(effectNode, "SpellKnockBackEffect");
            GCNode action = knockDown ?? knockBack;
            if (action == null)
                return false;

            int skillLevel = 1;
            bool isKnockDown = knockDown != null;
            string family = isKnockDown ? "SpellKnockDownEffect" : "SpellKnockBackEffect";
            effect = new MonsterStunActionSkillEffect
            {
                SkillPath = monster.PrimaryActiveSkillPath,
                EffectPath = effectPath,
                ActionEffectPath = BuildAuthoredEffectPath(effectPath, action),
                ActionFamily = family,
                Strength = ResolveSkillLinearMod(action, "Strength", skillLevel),
                ChanceWire = ResolveSpellEffectChanceWire(action),
                IsKnockDown = isKnockDown
            };
            return true;
        }

        private bool TryResolveMonsterDamageModifierSkillEffect(GCNode effectNode, GCNode skill, string skillPath, string effectPath, out MonsterDamageModifierSkillEffect effect)
        {
            effect = null;
            if (effectNode == null)
                return false;

            GCNode modifierEffectRoot = effectNode;
            GCNode weaponDamage = FindEffectChild(effectNode, "SpellWeaponDamageEffect");
            string nestedEffectPath = weaponDamage?.GetString("Effect", null);
            if (!string.IsNullOrWhiteSpace(nestedEffectPath))
            {
                GCNode nestedEffectNode = ResolveAuthoredNodeReference(nestedEffectPath, skill);
                if (nestedEffectNode != null)
                    modifierEffectRoot = nestedEffectNode;
            }

            GCNode modEffect = FindEffectChild(modifierEffectRoot, "SpellModEffect");
            if (modEffect == null && modifierEffectRoot != effectNode)
                modEffect = FindEffectChild(effectNode, "SpellModEffect");
            if (modEffect == null)
                return false;

            string modifierPath = modEffect.GetString("Modifier", null);
            if (string.IsNullOrWhiteSpace(modifierPath))
                return false;

            GCNode modifier = ResolveAuthoredNodeReference(modifierPath, skill);
            GCNode modifierDesc = modifier?.GetChild("Description") ?? modifier;
            if (modifierDesc == null)
                return false;

            string modifierEffectPath = modifierDesc.GetString("Effect", null);
            if (string.IsNullOrWhiteSpace(modifierEffectPath))
                return false;

            GCNode modifierEffect = ResolveAuthoredNodeReference(modifierEffectPath, modifier ?? skill);
            if (modifierEffect == null)
                return false;

            GCNode damageEffect = FindEffectChild(modifierEffect, "SpellDamageEffect");
            if (damageEffect == null)
                return false;

            string attackType = damageEffect.GetString("AttackType", "MAGIC");
            string damageType = damageEffect.GetString("DamageType", "POISON");
            if (!DamageResolver.TryResolveDamageTypeId(damageType, out int damageTypeId))
                return false;

            float damageMod = damageEffect.GetFloat("DamageMod", 0f);
            if (damageMod <= 0f)
                return false;

            float durationSeconds = modEffect.GetFloat("Duration", 0f);
            if (durationSeconds <= 0f)
                durationSeconds = modifierDesc.GetFloat("Duration", 0f);
            float frequencySeconds = modifierDesc.GetFloat("Frequency", 0f);
            if (frequencySeconds <= 0f)
                frequencySeconds = CLIENT_UNIT_TICK_INTERVAL;

            effect = new MonsterDamageModifierSkillEffect
            {
                SkillPath = skillPath,
                EffectPath = nestedEffectPath ?? effectPath,
                ModifierPath = modifierPath,
                ModifierEffectPath = modifierEffectPath,
                AttackType = attackType,
                DamageType = damageType,
                DamageTypeId = damageTypeId,
                DamageKind = string.Equals(attackType, "MAGIC", StringComparison.OrdinalIgnoreCase) ? (byte)3 : (byte)0,
                DamageMod = damageMod,
                DamageVolatility = damageEffect.GetFloat("DamageVolatility", 0f),
                DurationSeconds = durationSeconds,
                DurationTicks = ComputeSpellModDurationTicks(durationSeconds),
                FrequencySeconds = frequencySeconds,
                FrequencyTicks = ComputeEffectModFrequencyTicks(frequencySeconds),
                RemoveOnDeath = modifierDesc.GetBool("RemoveOnDeath", false),
                StackRule = modifierDesc.GetString("StackRule", "")
            };
            return true;
        }

        private static int ResolveSkillLinearMod(GCNode node, string prefix, int skillLevel)
        {
            if (node == null || string.IsNullOrWhiteSpace(prefix))
                return 0;
            int level = Mathf.Max(0, skillLevel);
            int min = node.GetInt(prefix + "Min", 0);
            int inc = node.GetInt(prefix + "Inc", 0);
            int value = min + inc * level;
            if (node.HasProperty(prefix + "Max"))
            {
                int max = node.GetInt(prefix + "Max", value);
                if (value > max) value = max;
            }
            return value;
        }

        private static string BuildAuthoredEffectPath(string effectPath, GCNode child)
        {
            if (string.IsNullOrWhiteSpace(effectPath))
                return child?.Extends ?? "unknown";
            if (child == null)
                return effectPath;
            if (!string.IsNullOrWhiteSpace(child.Name) && !child.IsAnonymous)
                return $"{effectPath}.{child.Name}";
            return $"{effectPath}.{child.Extends ?? "anonymous"}";
        }

        private void ApplyMonsterStunActionSkillEffect(Monster monster, CombatPlayer target, MonsterStunActionSkillEffect effect, string marker, string source)
        {
            if (monster == null || target == null || effect == null)
                return;
            RoomRuntime runtime = GetRoomRuntimeForMonster(monster);
            MersenneTwister rng = runtime?.RoomRng;
            if (rng == null)
            {
                Debug.LogError($"[SPELL-STUN-ACTION] attacker={monster.Name}#{monster.EntityId} target={target.Name}#{target.EntityId} skill={effect.SkillPath} effect={effect.ActionEffectPath} family={effect.ActionFamily} source={source ?? marker ?? "unknown"} result=NO_RNG reason=missing-room-rng sourceFunction=SpellEffect::CheckChance@0x00545FF0 Unit::CheckStunResist@0x0050C630");
                return;
            }
            ConsumeStunActionEffectRng(rng, monster, target, effect.SkillPath, effect.ActionEffectPath, effect.ActionFamily, effect.Strength, effect.ChanceWire, marker, source);
        }

        private void ConsumeWeaponStunActionRng(MersenneTwister rng, Monster monster, CombatPlayer target, MonsterWeaponDamageSkillEffect effect, string marker, string source)
        {
            if (effect == null)
                return;
            if (effect.HasKnockBack)
                ConsumeStunActionEffectRng(rng, monster, target, effect.SkillPath, effect.KnockBackEffectPath, "SpellKnockBackEffect", effect.KnockBackStrength, effect.KnockBackChanceWire, marker, source);
            if (effect.HasKnockDown)
                ConsumeStunActionEffectRng(rng, monster, target, effect.SkillPath, effect.KnockDownEffectPath, "SpellKnockDownEffect", effect.KnockDownStrength, effect.KnockDownChanceWire, marker, source);
        }

        private bool ConsumeStunActionEffectRng(MersenneTwister rng, Monster monster, CombatPlayer target, string skillPath, string effectPath, string effectFamily, int strength, int chanceWire, string marker, string source)
        {
            if (rng == null)
                return false;
            string family = string.IsNullOrWhiteSpace(effectFamily) ? "SpellKnockBackEffect" : effectFamily;
            string client = family == "SpellKnockDownEffect" ? "SpellKnockDownEffect::doEffect@0x00553360" : "SpellKnockBackEffect::doEffect@0x00552C80";
            if (!ConsumeSpellEffectChanceRng(rng, monster, target, skillPath, effectPath, family, chanceWire, marker, source, out uint chanceRaw, out uint chanceRoll))
            {
                Debug.LogError($"[SPELL-STUN-ACTION] attacker={monster?.Name ?? "monster"}#{monster?.EntityId ?? 0} target={target?.Name ?? "unknown"}#{target?.EntityId ?? 0} skill={skillPath ?? "none"} effect={effectPath ?? "none"} family={family} result=CHANCE_FAIL strength={strength} chanceWire={chanceWire} chance={chanceWire / 256f:F2} chanceRaw=0x{chanceRaw:X8} chanceRoll={chanceRoll} rngPos={rng.CallsSinceReseed} packet=NOT_SENT source={source ?? marker ?? "unknown"} sourceFunction=SpellEffect::CheckChance@0x00545FF0 {client}");
                return false;
            }

            int resistWire = ResolvePlayerStunResistChanceWire(target, monster?.Level ?? 0);
            uint stunRaw = RngLedger.Generate(rng, "unitOwnedCombat", $"{marker ?? "MON-SKILL"}:{family}::CheckStunResist", "Unit::CheckStunResist");
            uint stunRoll = stunRaw % 0x6400u;
            bool resisted = stunRoll < (uint)Mathf.Max(0, resistWire);
            if (resisted)
            {
                Debug.LogError($"[SPELL-STUN-ACTION] attacker={monster?.Name ?? "monster"}#{monster?.EntityId ?? 0} target={target?.Name ?? "unknown"}#{target?.EntityId ?? 0} skill={skillPath ?? "none"} effect={effectPath ?? "none"} family={family} result=RESIST strength={strength} chanceWire={chanceWire} chanceRaw=0x{chanceRaw:X8} chanceRoll={chanceRoll} stunResistWire={resistWire} stunRaw=0x{stunRaw:X8} stunRoll={stunRoll} rngPos={rng.CallsSinceReseed} packet=NOT_SENT source={source ?? marker ?? "unknown"} sourceFunction={client} SpellEffect::CheckChance@0x00545FF0 Unit::CheckStunResist@0x0050C630");
                return false;
            }

            PlayerStunActionResolved action = BuildPlayerStunAction(monster, target, skillPath, effectPath, family, strength, chanceWire, chanceRaw, chanceRoll, resistWire, stunRaw, stunRoll, source ?? marker ?? "unknown");
            OnPlayerStunActionResolved?.Invoke(monster, target, action);
            Debug.LogError($"[SPELL-STUN-ACTION] attacker={monster?.Name ?? "monster"}#{monster?.EntityId ?? 0} target={target?.Name ?? "unknown"}#{target?.EntityId ?? 0} skill={skillPath ?? "none"} effect={effectPath ?? "none"} family={family} result=ACTION_QUEUED action={action.ActionClassName} actionId=0x{action.ActionClassId:X2} heading={action.HeadingWire} strengthWire={action.StrengthWire} authoredStrength={action.AuthoredStrength} knockDownPlayerBranch={action.KnockDownPlayerBranch} chanceWire={chanceWire} chanceRaw=0x{chanceRaw:X8} chanceRoll={chanceRoll} stunResistWire={resistWire} stunRaw=0x{stunRaw:X8} stunRoll={stunRoll} rngPos={rng.CallsSinceReseed} source={source ?? marker ?? "unknown"} sourceFunction={client} SpellEffect::CheckChance@0x00545FF0 Unit::CheckStunResist@0x0050C630 KnockBack::writeData@0x0052A320");
            return true;
        }

        private PlayerStunActionResolved BuildPlayerStunAction(Monster monster, CombatPlayer target, string skillPath, string effectPath, string family, int strength, int chanceWire, uint chanceRaw, uint chanceRoll, int resistWire, uint stunRaw, uint stunRoll, string source)
        {
            float sourceX = monster != null ? monster.PosX : 0f;
            float sourceY = monster != null ? monster.PosY : 0f;
            if (monster != null && TryGetMonsterClientVisiblePosition(monster, GetCombatTime(), out float visibleX, out float visibleY))
            {
                sourceX = visibleX;
                sourceY = visibleY;
            }
            float targetX = target != null ? target.PosX : sourceX;
            float targetY = target != null ? target.PosY : sourceY;
            bool knockDownPlayerBranch;
            ushort strengthWire = ResolvePlayerStunActionStrengthWire(family, strength, out knockDownPlayerBranch);
            return new PlayerStunActionResolved
            {
                SkillPath = skillPath,
                EffectPath = effectPath,
                EffectFamily = family,
                ActionClassName = "KnockBack",
                ActionClassId = CLIENT_PLAYER_STUN_ACTION_KNOCKBACK_ID,
                HeadingWire = ResolveDestHeadingWire(sourceX, sourceY, targetX, targetY),
                StrengthWire = strengthWire,
                AuthoredStrength = strength,
                ChanceWire = chanceWire,
                ChanceRaw = chanceRaw,
                ChanceRoll = chanceRoll,
                StunResistWire = resistWire,
                StunRaw = stunRaw,
                StunRoll = stunRoll,
                Source = source,
                KnockDownPlayerBranch = knockDownPlayerBranch
            };
        }

        private static ushort ResolvePlayerStunActionStrengthWire(string family, int strength, out bool knockDownPlayerBranch)
        {
            knockDownPlayerBranch = string.Equals(family, "SpellKnockDownEffect", StringComparison.OrdinalIgnoreCase);
            long value = knockDownPlayerBranch ? (long)strength * 2L : strength;
            if (value < 0) value = 0;
            if (value > ushort.MaxValue) value = ushort.MaxValue;
            return (ushort)value;
        }

        private static ushort ResolveDestHeadingWire(float sourceX, float sourceY, float targetX, float targetY)
        {
            float dx = targetX - sourceX;
            float dy = targetY - sourceY;
            float distance = Mathf.Sqrt(dx * dx + dy * dy);
            if (distance <= 0.0001f)
                return 0;
            float absX = Mathf.Abs(dx);
            float absY = Mathf.Abs(dy);
            float heading;
            if (absX <= absY)
            {
                float angle = Mathf.Acos(Mathf.Clamp(absX / distance, -1f, 1f)) * Mathf.Rad2Deg;
                heading = dy < 0f ? 360f - (angle + 90f) : 360f - (90f - angle);
            }
            else
            {
                float angle = Mathf.Asin(Mathf.Clamp(absY / distance, -1f, 1f)) * Mathf.Rad2Deg;
                heading = dx < 0f ? 360f - (angle + 270f) : 360f - (90f - angle);
            }
            while (heading < 0f) heading += 360f;
            while (heading > 360f) heading -= 360f;
            return (ushort)Mathf.Clamp(Mathf.RoundToInt(heading), 0, 360);
        }

        private bool ConsumeSpellEffectChanceRng(MersenneTwister rng, Monster monster, CombatPlayer target, string skillPath, string effectPath, string effectFamily, int chanceWire, string marker, string source, out uint raw, out uint roll)
        {
            raw = 0;
            roll = 0;
            int normalizedChance = Mathf.Clamp(chanceWire, 0, 0x6400);
            if (normalizedChance >= 0x6400)
                return true;
            raw = RngLedger.Generate(rng, "unitOwnedCombat", $"{marker ?? "MON-SKILL"}:{effectFamily}::CheckChance", "SpellEffect::CheckChance");
            roll = raw % 0x6464u;
            return roll < (uint)normalizedChance;
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

        private static int ResolvePlayerStunResistChanceWire(CombatPlayer target, int attackerLevel)
        {
            int resist = GetEquipmentStatSum(target?.PlayerState, "STUN_RESIST", "STUNRESIST") * 0x100;
            int targetLevel = target?.PlayerState?.Level ?? 0;
            int diff = (targetLevel * 0x100) - (attackerLevel * 0x100);
            if (diff > 0x500)
                resist += (int)(((long)(diff - 0x500) * 0x500) >> 8);
            if (resist > 0x5A00) resist = 0x5A00;
            if (resist < 0x500) resist = 0x500;
            return resist;
        }

        private static GCNode ResolveAuthoredNodeReference(string path, GCNode contextRoot)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            var gc = GCDatabase.Instance;
            GCNode node = gc?.ResolveWithInheritance(path);
            if (node != null)
                return node;

            if (contextRoot == null)
                return null;

            string rootName = contextRoot.Name;
            if (!string.IsNullOrWhiteSpace(rootName) &&
                path.StartsWith(rootName + ".", StringComparison.OrdinalIgnoreCase))
            {
                string subPath = path.Substring(rootName.Length + 1);
                return ResolveChildPath(contextRoot, subPath);
            }

            int dot = path.IndexOf('.');
            while (dot >= 0 && dot + 1 < path.Length)
            {
                string suffix = path.Substring(dot + 1);
                GCNode child = ResolveChildPath(contextRoot, suffix);
                if (child != null)
                    return child;
                dot = path.IndexOf('.', dot + 1);
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

        private static GCNode FindEffectChild(GCNode node, string clientExtends)
        {
            if (node == null)
                return null;
            if (AuthoredExtends(node, clientExtends) || node.HasProperty("Attribute"))
                return node;
            foreach (var child in node.AnonymousChildren)
            {
                if (AuthoredExtends(child, clientExtends) || child.HasProperty("Attribute"))
                    return child;
            }
            foreach (var child in node.Children.Values)
            {
                if (AuthoredExtends(child, clientExtends) || child.HasProperty("Attribute"))
                    return child;
            }
            return null;
        }

        private static GCNode FindEffectChildByExtends(GCNode node, string clientExtends)
        {
            if (node == null)
                return null;
            if (AuthoredExtends(node, clientExtends))
                return node;
            foreach (var child in node.AnonymousChildren)
            {
                if (AuthoredExtends(child, clientExtends))
                    return child;
            }
            foreach (var child in node.Children.Values)
            {
                if (AuthoredExtends(child, clientExtends))
                    return child;
            }
            return null;
        }

        private static bool AuthoredExtends(GCNode node, string clientExtends)
        {
            if (node == null || string.IsNullOrWhiteSpace(clientExtends))
                return false;
            string ext = node.Extends ?? string.Empty;
            if (string.Equals(ext, clientExtends, StringComparison.OrdinalIgnoreCase))
                return true;
            return ext.EndsWith("." + clientExtends, StringComparison.OrdinalIgnoreCase);
        }

        private static ushort ComputeSpellModDurationTicks(float durationSeconds)
        {
            if (durationSeconds <= 0f || float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds))
                return 0;
            long fixed8Seconds = Mathf.RoundToInt(durationSeconds * 256f);
            long ticks = ((fixed8Seconds * 30L) + 0x100L) >> 8;
            if (ticks <= 0) return 0;
            if (ticks > ushort.MaxValue) return ushort.MaxValue;
            return (ushort)ticks;
        }

        private static ushort ComputeEffectModFrequencyTicks(float frequencySeconds)
        {
            if (frequencySeconds <= 0f || float.IsNaN(frequencySeconds) || float.IsInfinity(frequencySeconds))
                return 0;
            long fixed8Seconds = Mathf.RoundToInt(frequencySeconds * 256f);
            long ticks = (fixed8Seconds * 30L) >> 8;
            if (ticks <= 0) return 0;
            if (ticks > ushort.MaxValue) return ushort.MaxValue;
            return (ushort)ticks;
        }

        private static int ComputeEffectModApplyTickBudget(ushort durationTicks, ushort frequencyTicks)
        {
            int period = Math.Max(1, (int)frequencyTicks);
            if (durationTicks == 0)
                return int.MaxValue;
            if (durationTicks <= period)
                return 0;
            return (durationTicks - 1) / period;
        }

        private static float ComputeEffectModIntervalSeconds(ushort frequencyTicks)
        {
            return Math.Max(1, (int)frequencyTicks) / 30f;
        }

        private static float ComputeEffectModExpireTime(float applyTime, ushort durationTicks)
        {
            if (durationTicks == 0)
                return float.PositiveInfinity;
            return applyTime + durationTicks / 30f;
        }

        private static bool IsEffectModExpired(ushort durationTicks, float expireTime, float now)
        {
            return durationTicks > 0 && now + 0.0001f >= expireTime;
        }

        private float ResolveMonsterDamageModifier(Monster monster)
        {
            if (monster == null) return 1f;
            float damageMod = monster.DamageMod > 0f ? monster.DamageMod : 1f;
            return damageMod;
        }

        private IEnumerable<Monster> SelectMonsters(Monster onlyMonster)
        {
            if (onlyMonster != null)
            {
                yield return onlyMonster;
                yield break;
            }

            foreach (var monster in _activeMonsters.Values)
                yield return monster;
        }

        private uint PeekMonsterHPWireForTrace(Monster monster)
        {
            if (monster == null) return 0;
            if (_monsterHPStates.TryGetValue(monster.EntityId, out var authority) && authority.RuntimeInitialized)
                return authority.RuntimeHPWire;
            if (_monsterRuntimeHPWire.TryGetValue(monster.EntityId, out uint runtimeHP))
                return runtimeHP;
            return monster.CurrentHPWire;
        }

        private void TraceMonsterState(Monster monster, string phase, CombatPlayer target = null, float dist = -1f, float range = -1f, string reason = null)
        {
            if (monster == null) return;

            ApplyEncounterMirror(monster);
            uint hp = PeekMonsterHPWireForTrace(monster);
            string targetText = target != null ? $"{target.Name}#{target.EntityId}" : (monster.TargetId != 0 ? monster.TargetId.ToString() : "none");
            string signature = $"{monster.State}|{monster.IsAlive}|{monster.AggroTriggered}|{monster.TargetId}|{monster.AlertSourceEntityId}|{monster.AttackPending}|{monster.AttackClientVisible}|{monster.AttackContactOnly}|{monster.AttackHitResolved}|{monster.UsePrimaryActiveSkillThisAttack}|{monster.DeathPendingClientConfirmation}|{monster.DeathState}|{hp}|{monster.CurrentManaWire}|{Mathf.RoundToInt(monster.PosX * 10f)}|{Mathf.RoundToInt(monster.PosY * 10f)}|{monster.CombatContactTargetId}|{monster.AttackCommitTick}|{monster.AttackEndTick}|{monster.PrimaryActiveSkillCooldownRemainingTicks}";
            if (_monsterStateTraceSignatures.TryGetValue(monster.EntityId, out var previous) && previous == signature)
                return;

            _monsterStateTraceSignatures[monster.EntityId] = signature;
            string distText = dist >= 0f ? $"{dist:F1}" : "n/a";
            string rangeText = range >= 0f ? $"{range:F1}" : "n/a";
            RoomRuntime runtime = GetRoomRuntimeForMonster(monster);
            string instance = runtime?.InstanceKey ?? monster.InstanceKey ?? "<missing>";
            uint seed = runtime?.Seed ?? 0u;
            int rngPos = runtime?.RngCallsSinceReseed ?? -1;
            if (VerboseMonsterDiag) Debug.LogError($"[MON-STATE] phase={phase ?? "unknown"} monster={monster.Name}#{monster.EntityId} behavior={monster.BehaviorId} unit={monster.UnitId} gc='{monster.GCType}' spawnGc='{monster.SpawnGCType}' zone='{monster.ZoneName}' instance='{instance}' state={monster.State} clientState={monster.Ai?.StateId ?? 0} clientMessage={monster.Ai?.LastMessageId ?? 0} encounterState={monster.EncounterObjectState} encounterLive={monster.EncounterLiveUnitCount} encounterReturning={monster.EncounterReturningUnitCount} alive={monster.IsAlive} aggro={monster.AggroTriggered} target={targetText} alertSource={monster.AlertSourceEntityId} deathPending={monster.DeathPendingClientConfirmation} clientDeath={monster.DeathState} hp={hp}/{monster.MaxHPWire} mana={monster.CurrentManaWire}/{monster.MaxManaWire} pos=({monster.PosX:F1},{monster.PosY:F1},{monster.PosZ:F1}) dist={distText} range={rangeText} pending={monster.AttackPending} clientVisible={monster.AttackClientVisible} clientContactOnly={monster.AttackContactOnly} hitResolved={monster.AttackHitResolved} session={monster.AttackSessionId} commitTick={monster.AttackCommitTick} endTick={monster.AttackEndTick} contactTarget={monster.CombatContactTargetId} contactUntil={monster.CombatContactUntil:F3} skill={monster.PrimaryActiveSkillPath ?? "none"} useSkill={monster.UsePrimaryActiveSkillThisAttack} skillCd={monster.PrimaryActiveSkillCooldownRemainingTicks}/{monster.PrimaryActiveSkillCooldownTicks} rngSeed=0x{seed:X8} rngPos={rngPos} reason={reason ?? "state-change"}");
        }

        private void TraceCombatTick(string phase, float deltaTime, bool allowNewAttacks, Monster onlyMonster = null)
        {
            float now = GetCombatTime();
            if (now - _lastCombatTraceSummaryTime < 1f)
                return;

            _lastCombatTraceSummaryTime = now;
            int alive = 0;
            int aggro = 0;
            int pending = 0;
            int attacking = 0;
            int deathPending = 0;
            foreach (var monster in SelectMonsters(onlyMonster))
            {
                if (monster == null) continue;
                if (monster.IsAlive) alive++;
                if (monster.AggroTriggered) aggro++;
                if (monster.AttackPending) pending++;
                if (monster.State == MonsterState.Attacking) attacking++;
                if (monster.DeathPendingClientConfirmation || monster.DeathLifecycleActive) deathPending++;
            }

            string scope = onlyMonster != null ? onlyMonster.EntityId.ToString() : "all";
            RoomRuntime tickRuntime = onlyMonster != null ? GetRoomRuntimeForMonster(onlyMonster) : CurrentRoomRuntime;
            Debug.LogError($"[COMBAT-TICK] phase={phase ?? "update"} dt={deltaTime:F3} scope={scope} players={_players.Count} monsters={_activeMonsters.Count} alive={alive} aggro={aggro} pending={pending} attacking={attacking} deathPending={deathPending} allowNew={allowNewAttacks} instance='{tickRuntime?.InstanceKey ?? "<missing>"}' rngReady={tickRuntime != null && tickRuntime.Initialized} roomSeed=0x{(tickRuntime?.Seed ?? 0u):X8} rngPos={tickRuntime?.RngCallsSinceReseed ?? -1}");
        }

        private bool ShouldRunProximityScanThisTick(Monster monster)
        {
            if (monster == null) return false;
            if (!monster.IsAlive || monster.AggroTriggered || monster.TargetId != 0)
                return false;
            if (monster.ProximityScanCountdownTicks > 0)
            {
                monster.ProximityScanCountdownTicks--;
            }
            if (monster.ProximityScanCountdownTicks > 0)
                return false;
            int scanFixed256 = Mathf.Max(0, Mathf.RoundToInt(Mathf.Max(0f, monster.ScanFrequency) * 256f));
            int periodTicks = Mathf.Clamp((int)(((long)scanFixed256 * 0x1e00L) >> 16), 1, short.MaxValue);
            monster.ProximityScanCountdownTicks = (short)periodTicks;
            return true;
        }

        private void ProcessProximityAggro(uint playerEntityId = 0, Monster onlyMonster = null)
        {
            if (_players.Count == 0) return;
            var pathMaps = new Dictionary<string, PathMap>(StringComparer.OrdinalIgnoreCase);
            foreach (var monster in SelectMonsters(onlyMonster))
            {
                if (!monster.IsAlive || monster.AggroTriggered || monster.TargetId != 0) continue;
                float monsterX = monster.PosX;
                float monsterY = monster.PosY;
                PathMap pathMap = null;
                string pathMapKey = ResolveMonsterPathMapKey(monster);
                if (!string.IsNullOrWhiteSpace(pathMapKey))
                {
                    if (!pathMaps.TryGetValue(pathMapKey, out pathMap))
                    {
                        pathMap = PathMapCatalog.Instance.GetPathMap(pathMapKey);
                        pathMaps[pathMapKey] = pathMap;
                    }
                }
                float range = ResolveMonsterAggroAdmissionRange(monster);
                if (range <= 0f) continue;

                CombatPlayer nearest = null;
                float nearestSq = float.MaxValue;
                float closestAnySq = float.MaxValue;
                bool closestAnyReach = true;
                float rangeSq = range * range;
                foreach (var player in _players.Values)
                {
                    if (playerEntityId != 0 && player.EntityId != playerEntityId) continue;
                    if (player == null || !player.IsAlive || player.PlayerState == null) continue;
                    if (!MatchesInstance(monster, player.InstanceKey)) continue;
                    if (player.PlayerState.CurrentHPWire == 0 && player.PlayerState.EntitySynchInfoHP == 0) continue;
                    float dx = player.PosX - monsterX;
                    float dy = player.PosY - monsterY;
                    float distSq = dx * dx + dy * dy;
                    bool reach = true;
                    if (pathMap != null
                        && pathMap.TryCanReachPoint(monsterX, monsterY, player.PosX, player.PosY, out bool canReach))
                        reach = canReach;
                    if (distSq < closestAnySq)
                    {
                        closestAnySq = distSq;
                        closestAnyReach = reach;
                    }
                    if (distSq > rangeSq || distSq >= nearestSq) continue;
                    if (!reach) continue;
                    nearest = player;
                    nearestSq = distSq;
                }

                if (nearest != null)
                {
                    ApplyMonsterWanderClientVisiblePosition(monster, "proximity-acquire");
                    Debug.LogError($"[AGGRO-OBSERVE] proximity admit {monster.Name}#{monster.EntityId}->{nearest.Name} dist={Mathf.Sqrt(nearestSq):F1} aggro={range:F1} targetSearch={ResolveMonsterTargetSearchRange(monster, false):F1} sourceFunction=AgroRange-admission");
                    AggroMonster(monster, nearest, "proximity", false);
                }
                else if (closestAnySq < float.MaxValue)
                {
                    float closestDist = Mathf.Sqrt(closestAnySq);
                    bool inRange = closestAnySq <= rangeSq;
                    string verdict = !inRange
                        ? "OUT-OF-RANGE:position-divergence"
                        : (!closestAnyReach ? "IN-RANGE-NO-PATH:pathmap" : "IN-RANGE-NO-ADMIT:gate");
                    if (VerboseMonsterDiag) Debug.LogError($"[AGGRO-DETAIL] no-admit {monster.Name}#{monster.EntityId} monsterPos=({monsterX:F1},{monsterY:F1},{monster.PosZ:F1}) closestPlayerDist={closestDist:F1} aggroRange={range:F1} inRange={inRange} reach={closestAnyReach} pathMap={(pathMap != null)} verdict={verdict} sourceFunction=AgroRange-scan");
                }
            }
        }

        private float ResolveMonsterAggroAdmissionRange(Monster monster)
        {
            if (monster == null) return 0f;
            return monster.AggroRange > 0f ? monster.AggroRange : 0f;
        }

        private float ResolveMonsterTargetSearchRange(Monster monster, bool hasPathMap)
        {
            if (monster == null) return 0f;
            return monster.AggroRange;
        }

        private void ProcessMonsterMovement(float deltaTime)
        {
            ProcessMonsterMovement(deltaTime, 0);
        }

        private void ProcessMonsterMovement(float deltaTime, uint playerEntityId)
        {
            ProcessMonsterMovement(deltaTime, playerEntityId, null, true);
        }

        private void ProcessMonsterMovement(float deltaTime, uint playerEntityId, Monster onlyMonster)
        {
            ProcessMonsterMovement(deltaTime, playerEntityId, onlyMonster, true);
        }

        private void ProcessMonsterMovement(float deltaTime, uint playerEntityId, Monster onlyMonster, bool emitPositionChanged)
        {
            if (deltaTime <= 0f) return;
            float clientNow = GetCombatTime();
            var pathMaps = new Dictionary<string, PathMap>(StringComparer.OrdinalIgnoreCase);
            foreach (var monster in SelectMonsters(onlyMonster))
            {
                if (!monster.IsAlive || !monster.AggroTriggered || monster.TargetId == 0) continue;
                if (playerEntityId != 0 && monster.TargetId != playerEntityId) continue;
                if (!_players.TryGetValue(monster.TargetId, out var target) || target == null || !target.IsAlive || target.PlayerState == null) continue;
                if (target.PlayerState.CurrentHPWire == 0 && target.PlayerState.EntitySynchInfoHP == 0) continue;
                if (IsMonsterKnockDownActive(monster, clientNow, "movement")) continue;
                if (monster.AttackPending)
                {
                    if (emitPositionChanged)
                        OnMonsterPositionChanged?.Invoke(monster);
                    continue;
                }

                if (monster.LeashRange > 0f)
                {
                    TryGetMonsterClientVisiblePosition(monster, clientNow, out float mlx, out float mly);
                    int lhx = Mathf.RoundToInt(mlx * 256f) - Mathf.RoundToInt(monster.SpawnPosX * 256f);
                    int lhy = Mathf.RoundToInt(mly * 256f) - Mathf.RoundToInt(monster.SpawnPosY * 256f);
                    long leashSq = ((long)Mathf.RoundToInt(monster.LeashRange * 256f) * Mathf.RoundToInt(monster.LeashRange * 256f)) >> 8;
                    if ((((long)lhx * lhx) >> 8) + (((long)lhy * lhy) >> 8) > leashSq)
                    {
                        ClearMonsterCombatContact(monster, target);
                        monster.MeleeScanTargetId = 0;
                        monster.AggroTriggered = false;
                        monster.TargetId = 0;
                        monster.State = MonsterState.Idle;
                        WanderSimulator.Instance.RegisterMonster(monster, false);
                        continue;
                    }
                }

                float allowedRange = ResolveMonsterEffectiveAttackRange(monster);
                if (allowedRange <= 0f) continue;

                float behaviorMonsterX = monster.PosX;
                float behaviorMonsterY = monster.PosY;
                TryGetMonsterClientVisiblePosition(monster, GetCombatTime(), out behaviorMonsterX, out behaviorMonsterY);
                float dx = target.PosX - behaviorMonsterX;
                float dy = target.PosY - behaviorMonsterY;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                TickMonsterBehavior(monster, target, dist, allowedRange);
                PathMap pathMap = null;
                string pathMapKey = ResolveMonsterPathMapKey(monster);
                if (!string.IsNullOrWhiteSpace(pathMapKey))
                {
                    if (!pathMaps.TryGetValue(pathMapKey, out pathMap))
                    {
                        pathMap = PathMapCatalog.Instance.GetPathMap(pathMapKey);
                        pathMaps[pathMapKey] = pathMap;
                    }
                }
                if (pathMap != null
                    && pathMap.TryCanReachPoint(monster.PosX, monster.PosY, target.PosX, target.PosY, out bool canMoveReach)
                    && !canMoveReach)
                {
                    ClearMonsterCombatContact(monster, target);
                    if (monster.State == MonsterState.Combat)
                        monster.State = MonsterState.Chase;
                    TraceMonsterState(monster, "movement", target, dist, allowedRange, "path-blocked");
                    continue;
                }
                if (dist <= allowedRange + CLIENT_CONTACT_RANGE_EPSILON || dist <= 0.001f)
                {
                    if (monster.State == MonsterState.Chase)
                        monster.State = MonsterState.Combat;
                    monster.CombatContactTargetId = target.EntityId;
                    monster.CombatContactUntil = clientNow + Mathf.Max(0.25f, ResolveMonsterAttackWindup(monster) + 0.25f);
                    TraceMonsterState(monster, "movement", target, dist, allowedRange, "contact");
                    continue;
                }

                float speed = ResolveMonsterMovementSpeed(monster);
                if (speed <= 0f)
                {
                    monster.State = MonsterState.Chase;
                    TraceMonsterState(monster, "movement", target, dist, allowedRange, "no-speed");
                    continue;
                }

                int curFixedX = monster.PosFixedX;
                int curFixedY = monster.PosFixedY;
                int targetFixedX = (int)Math.Round(target.PosX * UnitMover.Fixed);
                int targetFixedY = (int)Math.Round(target.PosY * UnitMover.Fixed);
                int rangeFixed = (int)Math.Round(allowedRange * UnitMover.Fixed);
                int speedFixed = (int)Math.Round(speed * UnitMover.Fixed);
                int stepFixed = (int)(((long)speedFixed << 8) / 0x1e00);
                if (stepFixed < 1) stepFixed = 1;
                int chaseTurnRate = UnitMover.TurnRatePerTickFixed(monster.TurnRateDegrees);
                if (!monster.ChaseHeadingInit)
                {
                    monster.ChaseHeadingFixed = UnitMover.VectorToHeadingFixed(targetFixedX - curFixedX, targetFixedY - curFixedY);
                    monster.ChaseHeadingInit = true;
                }
                int chaseTicks = 1;
                var chasePm = PathMapCatalog.Instance.GetPathMap(!string.IsNullOrWhiteSpace(monster.InstanceKey) ? monster.InstanceKey : monster.ZoneName);
                bool chaseArrived = false;
                for (int t = 0; t < chaseTicks && !chaseArrived; t++)
                {
                    long rdx = (long)targetFixedX - curFixedX;
                    long rdy = (long)targetFixedY - curFixedY;
                    if (UnitMover.IntSqrt(rdx * rdx + rdy * rdy) <= rangeFixed)
                    {
                        chaseArrived = true;
                        break;
                    }
                    UnitMover.StepTowardFixedHeading(curFixedX, curFixedY, monster.ChaseHeadingFixed, targetFixedX, targetFixedY, stepFixed, chaseTurnRate, out int nextFixedX, out int nextFixedY, out int nextHeading, out chaseArrived);
                    UnitMover.ResolveMovement(chasePm, curFixedX, curFixedY, nextFixedX, nextFixedY, out nextFixedX, out nextFixedY);
                    curFixedX = nextFixedX;
                    curFixedY = nextFixedY;
                    monster.ChaseHeadingFixed = nextHeading;
                }
                monster.PosFixedX = curFixedX;
                monster.PosFixedY = curFixedY;
                monster.PosZ = ResolveTerrainHeight(monster, monster.PosX, monster.PosY, monster.PosZ);
                monster.Heading = UnitMover.WrapDegrees((UnitMover.FullCircleFixed - monster.ChaseHeadingFixed) >> 8);
                monster.State = MonsterState.Chase;

                long chaseRdx = (long)targetFixedX - curFixedX;
                long chaseRdy = (long)targetFixedY - curFixedY;
                int remainingFixed = UnitMover.IntSqrt(chaseRdx * chaseRdx + chaseRdy * chaseRdy);
                int contactRangeFixed = rangeFixed + (int)Math.Round(CLIENT_CONTACT_RANGE_EPSILON * UnitMover.Fixed);
                float remaining = remainingFixed / (float)UnitMover.Fixed;
                if (remainingFixed <= contactRangeFixed)
                {
                    monster.State = MonsterState.Combat;
                    monster.CombatContactTargetId = target.EntityId;
                    monster.CombatContactUntil = clientNow + Mathf.Max(0.25f, ResolveMonsterAttackWindup(monster) + 0.25f);
                }
                if (emitPositionChanged)
                    OnMonsterPositionChanged?.Invoke(monster);
                TraceMonsterState(monster, "movement", target, remaining, allowedRange, "move");
            }
        }

        private static float ResolveMonsterMovementSpeed(Monster monster)
        {
            if (monster == null) return 0f;
            if (monster.MoveSpeed > 0f) return monster.MoveSpeed;
            if (monster.WalkSpeed > 0f) return monster.WalkSpeed;
            return 0f;
        }

        public float GetMonsterMovementSpeed(Monster monster)
        {
            return ResolveMonsterMovementSpeed(monster);
        }

        private void AdvanceMonsterPrimarySkillCooldown(Monster monster, float now)
        {
            if (monster == null)
                return;

            if (monster.ActiveSkills != null && monster.ActiveSkills.Count > 0)
            {
                foreach (var skill in monster.ActiveSkills)
                    AdvanceMonsterSkillCooldown(monster, skill, now);
                if (monster.SelectedActiveSkill != null)
                    ApplyMonsterActiveSkillSelection(monster, monster.SelectedActiveSkill);
                return;
            }

            if (monster.PrimaryActiveSkillCooldownRemainingTicks == 0)
            {
                if (monster.PrimaryActiveSkillCooldownLastTime <= 0f)
                    monster.PrimaryActiveSkillCooldownLastTime = now;
                return;
            }
            if (monster.PrimaryActiveSkillCooldownLastTime <= 0f)
            {
                monster.PrimaryActiveSkillCooldownLastTime = now;
                return;
            }

            int ticks = Mathf.FloorToInt((now - monster.PrimaryActiveSkillCooldownLastTime) / CLIENT_UNIT_TICK_INTERVAL);
            if (ticks <= 0) return;
            ushort oldTicks = monster.PrimaryActiveSkillCooldownRemainingTicks;
            monster.PrimaryActiveSkillCooldownRemainingTicks = ticks >= oldTicks ? (ushort)0 : (ushort)(oldTicks - ticks);
            monster.PrimaryActiveSkillCooldownLastTime += ticks * CLIENT_UNIT_TICK_INTERVAL;
            if (oldTicks != monster.PrimaryActiveSkillCooldownRemainingTicks)
                Debug.LogError($"[MON-SKILL-CD] advance {monster.Name}#{monster.EntityId} skill={monster.PrimaryActiveSkillPath ?? "none"} ticks={oldTicks}->{monster.PrimaryActiveSkillCooldownRemainingTicks} elapsedTicks={ticks}");
        }

        private void AdvanceMonsterSkillCooldown(Monster monster, MonsterActiveSkillRuntime skill, float now)
        {
            if (monster == null || skill == null)
                return;
            if (skill.CooldownRemainingTicks == 0)
            {
                if (skill.CooldownLastTime <= 0f)
                    skill.CooldownLastTime = now;
                return;
            }
            if (skill.CooldownLastTime <= 0f)
            {
                skill.CooldownLastTime = now;
                return;
            }

            int ticks = Mathf.FloorToInt((now - skill.CooldownLastTime) / CLIENT_UNIT_TICK_INTERVAL);
            if (ticks <= 0) return;
            ushort oldTicks = skill.CooldownRemainingTicks;
            skill.CooldownRemainingTicks = ticks >= oldTicks ? (ushort)0 : (ushort)(oldTicks - ticks);
            skill.CooldownLastTime += ticks * CLIENT_UNIT_TICK_INTERVAL;
            if (oldTicks != skill.CooldownRemainingTicks)
                Debug.LogError($"[MON-SKILL-CD] advance {monster.Name}#{monster.EntityId} skill={skill.Path ?? "none"} ticks={oldTicks}->{skill.CooldownRemainingTicks} elapsedTicks={ticks}");
        }

        private void SelectMonsterPrimarySkillForAttack(Monster monster, CombatPlayer target, float dist, float fallbackRange, string source)
        {
            if (monster == null) return;
            monster.UsePrimaryActiveSkillThisAttack = false;
            IReadOnlyList<MonsterActiveSkillRuntime> skillList = monster.ActiveSkills != null && monster.ActiveSkills.Count > 0
                ? monster.ActiveSkills
                : null;
            if ((skillList == null || skillList.Count == 0) && string.IsNullOrWhiteSpace(monster.PrimaryActiveSkillPath))
                return;

            float now = GetCombatTime();
            TryGetMonsterClientVisiblePosition(monster, now, out float skillMonX, out float skillMonY);
            if (skillList != null && skillList.Count > 0)
            {
                foreach (var skill in skillList)
                    AdvanceMonsterSkillCooldown(monster, skill, now);
            }
            else
                AdvanceMonsterPrimarySkillCooldown(monster, now);

            if (skillList != null && skillList.Count > 0)
            {
                int cooldownBlocked = 0;
                int rangeBlocked = 0;
                int minimumRangeBlocked = 0;
                int targetTypeBlocked = 0;
                int spellUseBlocked = 0;
                int selfHealthBlocked = 0;
                int targetHealthBlocked = 0;
                MonsterActiveSkillRuntime selected = null;
                foreach (var skill in skillList)
                {
                    if (!IsMonsterActiveSkillCombatUse(skill))
                    {
                        spellUseBlocked++;
                        continue;
                    }
                    if (!IsMonsterActiveSkillEnemyTarget(skill))
                    {
                        targetTypeBlocked++;
                        continue;
                    }
                    if (!PassesMonsterActiveSkillHealthPct(monster.CurrentHPWire, monster.MaxHPWire, skill.HasSelfHealthPct, skill.SelfHealthPct))
                    {
                        selfHealthBlocked++;
                        continue;
                    }
                    if (target?.PlayerState != null && !PassesMonsterActiveSkillHealthPct(target.PlayerState.CurrentHPWire, target.PlayerState.MaxHPWire, skill.HasTargetHealthPct, skill.TargetHealthPct))
                    {
                        targetHealthBlocked++;
                        continue;
                    }
                    float candidateRange = ResolveMonsterActiveSkillUseRange(skill, fallbackRange) + monster.CollisionRadius + ResolveAvatarCombatRadius();
                    float minimumRange = ResolveMonsterActiveSkillMinimumRange(skill);
                    if (skill.CooldownRemainingTicks > 0)
                    {
                        cooldownBlocked++;
                        continue;
                    }
                    if (minimumRange >= 0f && dist + CLIENT_CONTACT_RANGE_EPSILON < minimumRange)
                    {
                        minimumRangeBlocked++;
                        continue;
                    }
                    if (target == null || !EvaluateUseTargetInitUse(skillMonX, skillMonY, target.PosX, target.PosY, candidateRange, CLIENT_CONTACT_RANGE_EPSILON, out _, out _, out _))
                    {
                        rangeBlocked++;
                        continue;
                    }
                    selected = skill;
                    break;
                }

                if (selected == null)
                {
                    Debug.LogError($"[MON-SKILL] validate skip=no-candidate {monster.Name}#{monster.EntityId}->{target?.Name ?? "unknown"} active={skillList.Count} cooldownBlocked={cooldownBlocked} rangeBlocked={rangeBlocked} minimumRangeBlocked={minimumRangeBlocked} targetTypeBlocked={targetTypeBlocked} spellUseBlocked={spellUseBlocked} selfHealthBlocked={selfHealthBlocked} targetHealthBlocked={targetHealthBlocked} dist={dist:F1} source={source} sourceFunction=MonsterBehavior2::UpdateSkills");
                    return;
                }

                ApplyMonsterActiveSkillSelection(monster, selected);
                monster.UsePrimaryActiveSkillThisAttack = true;
                float selectedRange = ResolveMonsterActiveSkillUseRange(selected, fallbackRange);
                float selectedMinimumRange = ResolveMonsterActiveSkillMinimumRange(selected);
                Debug.LogError($"[MON-SKILL] validate use {monster.Name}#{monster.EntityId}->{target?.Name ?? "unknown"} skill={selected.Path} id={selected.Id} skillOrder=client-first targetCandidate=0/1 dist={dist:F1} useRange={selectedRange:F1} minRange={selectedMinimumRange:F1} authoredRange={selected.Range:F1} targetType={selected.TargetType ?? ""} spellUse={selected.SpellUse ?? ""} cooldownTicks={selected.CooldownTicks} source={source} sourceFunction=MonsterBehavior2::UpdateSkills");
                return;
            }

            float skillRange = (monster.PrimaryActiveSkillSpellUseRange > 0f
                ? monster.PrimaryActiveSkillSpellUseRange
                : (monster.PrimaryActiveSkillRange > 0f ? monster.PrimaryActiveSkillRange : fallbackRange))
                + monster.CollisionRadius + ResolveAvatarCombatRadius();
            float skillMinimumRange = monster.PrimaryActiveSkillMinimumRange;
            if (!IsCombatSpellUse(monster.PrimaryActiveSkillSpellUse))
            {
                Debug.LogError($"[MON-SKILL] validate skip=spell-use {monster.Name}#{monster.EntityId}->{target?.Name ?? "unknown"} skill={monster.PrimaryActiveSkillPath} spellUse={monster.PrimaryActiveSkillSpellUse ?? ""} dist={dist:F1} range={skillRange:F1} source={source} sourceFunction=MonsterBehavior2::UpdateSkills");
                return;
            }
            if (!IsEnemyTargetType(monster.PrimaryActiveSkillTargetType))
            {
                Debug.LogError($"[MON-SKILL] validate skip=target-type {monster.Name}#{monster.EntityId}->{target?.Name ?? "unknown"} skill={monster.PrimaryActiveSkillPath} targetType={monster.PrimaryActiveSkillTargetType ?? ""} dist={dist:F1} range={skillRange:F1} source={source} sourceFunction=MonsterBehavior2::UpdateSkills");
                return;
            }
            if (!PassesMonsterActiveSkillHealthPct(monster.CurrentHPWire, monster.MaxHPWire, monster.PrimaryActiveSkillHasSelfHealthPct, monster.PrimaryActiveSkillSelfHealthPct))
            {
                Debug.LogError($"[MON-SKILL] validate skip=self-hp {monster.Name}#{monster.EntityId}->{target?.Name ?? "unknown"} skill={monster.PrimaryActiveSkillPath} hp={monster.CurrentHPWire}/{monster.MaxHPWire} pctLimit={monster.PrimaryActiveSkillSelfHealthPct:F1} dist={dist:F1} range={skillRange:F1} source={source} sourceFunction=MonsterBehavior2::UpdateSkills");
                return;
            }
            if (target?.PlayerState != null && !PassesMonsterActiveSkillHealthPct(target.PlayerState.CurrentHPWire, target.PlayerState.MaxHPWire, monster.PrimaryActiveSkillHasTargetHealthPct, monster.PrimaryActiveSkillTargetHealthPct))
            {
                Debug.LogError($"[MON-SKILL] validate skip=target-hp {monster.Name}#{monster.EntityId}->{target?.Name ?? "unknown"} skill={monster.PrimaryActiveSkillPath} hp={target.PlayerState.CurrentHPWire}/{target.PlayerState.MaxHPWire} pctLimit={monster.PrimaryActiveSkillTargetHealthPct:F1} dist={dist:F1} range={skillRange:F1} source={source} sourceFunction=MonsterBehavior2::UpdateSkills");
                return;
            }
            if (monster.PrimaryActiveSkillCooldownRemainingTicks > 0)
            {
                Debug.LogError($"[MON-SKILL] validate skip=cooldown {monster.Name}#{monster.EntityId}->{target?.Name ?? "unknown"} skill={monster.PrimaryActiveSkillPath} remaining={monster.PrimaryActiveSkillCooldownRemainingTicks}/{monster.PrimaryActiveSkillCooldownTicks} dist={dist:F1} range={skillRange:F1} source={source}");
                return;
            }
            if (skillMinimumRange >= 0f && dist + CLIENT_CONTACT_RANGE_EPSILON < skillMinimumRange)
            {
                Debug.LogError($"[MON-SKILL] validate skip=min-range {monster.Name}#{monster.EntityId}->{target?.Name ?? "unknown"} skill={monster.PrimaryActiveSkillPath} dist={dist:F1} minRange={skillMinimumRange:F1} range={skillRange:F1} source={source}");
                return;
            }
            if (target == null || !EvaluateUseTargetInitUse(skillMonX, skillMonY, target.PosX, target.PosY, skillRange, CLIENT_CONTACT_RANGE_EPSILON, out _, out _, out _))
            {
                Debug.LogError($"[MON-SKILL] validate skip=range {monster.Name}#{monster.EntityId}->{target?.Name ?? "unknown"} skill={monster.PrimaryActiveSkillPath} dist={dist:F1} range={skillRange:F1} source={source}");
                return;
            }

            monster.UsePrimaryActiveSkillThisAttack = true;
            Debug.LogError($"[MON-SKILL] validate use {monster.Name}#{monster.EntityId}->{target?.Name ?? "unknown"} skill={monster.PrimaryActiveSkillPath} id={monster.PrimaryActiveSkillId} targetCandidate=0/1 dist={dist:F1} useRange={skillRange:F1} minRange={skillMinimumRange:F1} authoredRange={monster.PrimaryActiveSkillRange:F1} targetType={monster.PrimaryActiveSkillTargetType ?? ""} spellUse={monster.PrimaryActiveSkillSpellUse ?? ""} cooldownTicks={monster.PrimaryActiveSkillCooldownTicks} source={source} sourceFunction=MonsterBehavior2::UpdateSkills");
        }

        private const float ClientActiveRange = 700f;

        private bool IsMonsterWithinClientActiveRange(Monster monster)
        {
            if (monster == null || _players.Count == 0)
                return false;
            TryGetMonsterWanderClientVisiblePosition(monster, out float mx, out float my);
            float rangeSq = ClientActiveRange * ClientActiveRange;
            foreach (var player in _players.Values)
            {
                if (player == null || !player.IsAlive || player.PlayerState == null)
                    continue;
                float dx = player.PosX - mx;
                float dy = player.PosY - my;
                if (dx * dx + dy * dy <= rangeSq)
                    return true;
            }
            return false;
        }

        public bool IsMonsterWithinClientActiveRangeForEntity(uint entityId)
        {
            return _activeMonsters.TryGetValue(entityId, out var monster) && IsMonsterWithinClientActiveRange(monster);
        }

        private void TickMonsterUpdateSkillsTimer(Monster monster)
        {
            if (monster == null || !monster.IsAlive)
                return;
            if (monster.State != MonsterState.Idle || monster.AggroTriggered)
                return;

            IReadOnlyList<MonsterActiveSkillRuntime> skillList = monster.ActiveSkills != null && monster.ActiveSkills.Count > 0
                ? monster.ActiveSkills
                : null;
            if (skillList == null)
                return;

            float now = GetCombatTime();
            foreach (var skill in skillList)
                AdvanceMonsterSkillCooldown(monster, skill, now);

            if (monster.UpdateSkillsTimerTicks > 0)
            {
                monster.UpdateSkillsTimerTicks--;
                return;
            }
            monster.UpdateSkillsTimerTicks = (short)IdleGatePeriodTicks;

            var skillRng = GetRoomRngForMonster(monster);
            if (skillRng == null)
                return;
            int rngBefore = skillRng.CallsSinceReseed;
            uint gateRaw = RngLedger.Generate(skillRng, "room", "MonsterBehavior2::UpdateSkills:idle-gate", $"{monster.Name}#{monster.EntityId}");
            uint gateRoll = gateRaw % 100u;
            Debug.LogError($"[MON-SKILL] idle-gate {monster.Name}#{monster.EntityId} gateRaw=0x{gateRaw:X8} roll={gateRoll} pos={rngBefore}->{skillRng.CallsSinceReseed} skill={monster.PrimaryActiveSkillPath ?? "none"} sourceFunction=MonsterBehavior2::UpdateSkills");
        }

        private static float ResolveMonsterActiveSkillUseRange(MonsterActiveSkillRuntime skill, float fallbackRange)
        {
            if (skill == null) return fallbackRange;
            if (skill.SpellUseRange > 0f) return skill.SpellUseRange;
            if (skill.Range > 0f) return skill.Range;
            return fallbackRange;
        }

        private static float ResolveMonsterActiveSkillMinimumRange(MonsterActiveSkillRuntime skill)
        {
            return skill != null ? skill.SpellUseMinimumRange : -1f;
        }

        private static bool IsMonsterActiveSkillCombatUse(MonsterActiveSkillRuntime skill)
        {
            return IsCombatSpellUse(skill?.SpellUse);
        }

        private static bool IsMonsterActiveSkillEnemyTarget(MonsterActiveSkillRuntime skill)
        {
            return IsEnemyTargetType(skill?.TargetType);
        }

        private static bool IsCombatSpellUse(string spellUse)
        {
            return string.IsNullOrWhiteSpace(spellUse)
                || spellUse.Equals("COMBAT", StringComparison.OrdinalIgnoreCase)
                || spellUse.Equals("1", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEnemyTargetType(string targetType)
        {
            return string.IsNullOrWhiteSpace(targetType)
                || targetType.Equals("ENEMY", StringComparison.OrdinalIgnoreCase)
                || targetType.Equals("2", StringComparison.OrdinalIgnoreCase);
        }

        private static bool PassesMonsterActiveSkillHealthPct(uint currentHPWire, uint maxHPWire, bool hasLimit, float limit)
        {
            if (!hasLimit)
                return true;
            if (maxHPWire == 0)
                return false;
            float pct = currentHPWire * 100f / maxHPWire;
            return pct <= limit + 0.0001f;
        }

        public void CommitMonsterPrimarySkillUse(Monster monster, string source)
        {
            if (monster == null || !monster.UsePrimaryActiveSkillThisAttack || string.IsNullOrWhiteSpace(monster.PrimaryActiveSkillPath))
                return;
            var selected = monster.SelectedActiveSkill;
            if (selected != null && selected.CooldownTicks > 0)
            {
                selected.CooldownRemainingTicks = selected.CooldownTicks;
                selected.CooldownLastTime = GetCombatTime();
                ApplyMonsterActiveSkillSelection(monster, selected);
            }
            else if (monster.PrimaryActiveSkillCooldownTicks > 0)
            {
                monster.PrimaryActiveSkillCooldownRemainingTicks = monster.PrimaryActiveSkillCooldownTicks;
                monster.PrimaryActiveSkillCooldownLastTime = GetCombatTime();
            }
            Debug.LogError($"[MON-SKILL-CD] set {monster.Name}#{monster.EntityId} skill={monster.PrimaryActiveSkillPath} ticks={monster.PrimaryActiveSkillCooldownRemainingTicks} source={source ?? "unknown"}");
        }

        private void ProcessMonsterAttacks(float deltaTime)
        {
            ProcessMonsterAttacks(deltaTime, 0);
        }

        private void ProcessMonsterAttacks(float deltaTime, uint playerEntityId, bool allowNewAttacks = true)
        {
            ProcessMonsterAttacks(deltaTime, playerEntityId, allowNewAttacks, null);
        }

        private void ProcessMonsterAttacks(float deltaTime, uint playerEntityId, bool allowNewAttacks, Monster onlyMonster, float clientNow = -1f)
        {
            float now = clientNow >= 0f ? clientNow : GetCombatTime();

            DrainDueMonsterProjectileImpacts(now);

            foreach (var monster in SelectMonsters(onlyMonster))
            {
                if (GetRoomRngForMonster(monster) == null)
                    continue;
                AdvanceMonsterPrimarySkillCooldown(monster, now);
                TryAssistFromAlertSource(monster, "attack-loop");
                bool pendingClientVisibleAttack = monster.AttackPending && monster.AttackClientVisible;
                bool pendingRuntimeAttack = monster.AttackPending && monster.AttackCommitTick > 0;
                if (!monster.AggroTriggered) continue;
                if (monster.DeathPendingClientConfirmation
                    && !(pendingRuntimeAttack && monster.AttackContactOnly && !monster.AttackHitResolved))
                {
                    CancelMonsterPendingAttack(monster, "monster_death_pending_client_confirmation");
                    continue;
                }
                if (!monster.IsAlive && !pendingClientVisibleAttack && !pendingRuntimeAttack)
                {
                    monster.AttackPending = false;
                    monster.AttackSoundPending = false;
                    monster.AttackHitResolved = false;
                    monster.AttackStartTick = 0;
                    monster.AttackEndTick = 0;
                    monster.AttackCommitTick = 0;
                    monster.AttackSoundTick = 0;
                    continue;
                }
                if (playerEntityId != 0 && monster.TargetId != playerEntityId) continue;
                if (!_players.TryGetValue(monster.TargetId, out var target) || !target.IsAlive || target.PlayerState == null) continue;
                if (!string.IsNullOrWhiteSpace(target.InstanceKey) && !MatchesInstance(monster, target.InstanceKey))
                {
                    Debug.LogError($"[COMBAT-LIFECYCLE] {monster.Name}#{monster.EntityId} dropping target {target.Name}#{monster.TargetId}: left monster instance '{monster.InstanceKey}' for '{target.InstanceKey}' sourceFunction=MonsterBehavior2::UpdateTargets@0x0051CB50 target-watcher-invalidated-out-of-world");
                    ClearMonsterTargetStateForLostPlayer(monster, monster.TargetId, "target-left-instance");
                    continue;
                }
                if (target.PlayerState.IsZoneSpawnDamageImmune)
                {
                    CancelMonsterPendingAttack(monster, "target_zone_spawn_invulnerability");
                    continue;
                }
                if (target.PlayerState.CurrentHPWire == 0 && target.PlayerState.EntitySynchInfoHP == 0)
                {
                    target.IsAlive = false;
                    CancelMonsterPendingAttack(monster, "target_dead");
                    continue;
                }
                if (IsMonsterKnockDownActive(monster, now, "attack-loop")) continue;
                float allowedRange = ResolveMonsterEffectiveAttackRange(monster);
                if (allowedRange <= 0f) continue;
                float attackMonsterX = monster.PosX;
                float attackMonsterY = monster.PosY;
                TryGetMonsterClientVisiblePosition(monster, now, out attackMonsterX, out attackMonsterY);
                float dist = Distance2D(attackMonsterX, attackMonsterY, target.PosX, target.PosY);
                bool attackPathClear = IsMonsterAttackPathClear(monster, target, null);
                bool clientTargetAction = HasMonsterTargetAction(monster, target, dist);
                bool initUseGateway = HasMonsterInitUseGateway(monster, target);
                TraceMonsterState(monster, "attack-loop", target, dist, allowedRange, monster.AttackPending ? "pending" : "ready");

                if (monster.AttackPending)
                {
                    if (monster.AttackCommitTick <= 0)
                    {
                        if (!attackPathClear)
                        {
                            CancelMonsterPendingAttack(monster, $"world_blocked dist={dist:F1} range={allowedRange:F1}");
                            if (monster.IsAlive)
                                monster.State = MonsterState.Chase;
                            Debug.LogError($"[MON-DAMAGE] {monster.Name}#{monster.EntityId}->{target.Name} cancel worldBlocked dist={dist:F1} monsterRange={allowedRange:F1}");
                            OnMonsterAttackResolved?.Invoke(monster, target, false, target.PlayerState.CurrentHPWire);
                            continue;
                        }
                        if (!monster.IsAlive)
                        {
                            CancelMonsterPendingAttack(monster, "dead_unarmed");
                            continue;
                        }
                        SelectMonsterPrimarySkillForAttack(monster, target, dist, allowedRange, "pending-start");
                        if (!initUseGateway && !monster.UsePrimaryActiveSkillThisAttack)
                        {
                            if (clientTargetAction)
                                TraceFarTargetAction(monster, target, dist, allowedRange, "pending-start");
                            DelayMonsterAttackRetry(monster, "init_use_gateway_pending_start");
                            CancelMonsterPendingAttack(monster, $"init_use_gateway_pending_start dist={dist:F1} range={allowedRange:F1} targetRange={ResolveMonsterTargetSearchRange(monster, true):F1}");
                            if (monster.IsAlive)
                                monster.State = MonsterState.Chase;
                            continue;
                        }
                        if (!monster.AttackClientVisible)
                        {
                            int handlerCount = OnMonsterAttackStarted?.GetInvocationList().Length ?? 0;
                            Debug.LogError($"[MON-ATTACK] dispatch start {monster.Name}->{target.Name} session={monster.AttackSessionId} handlers={handlerCount} dist={dist:F1} range={allowedRange:F1}");
                            OnMonsterAttackStarted?.Invoke(monster, target, monster.AttackSessionId);
                            if (!monster.AttackPending)
                            {
                                Debug.LogError($"[MON-ATTACK] {monster.Name}->{target.Name} START canceled session={monster.AttackSessionId}");
                                continue;
                            }
                        }
                        ArmMonsterRuntimeAttack(monster, target, "START", now);
                        continue;
                    }
                    if (monster.AttackHitResolved)
                    {
                        if ((int)_combatTick < monster.AttackEndTick) continue;
                        monster.AttackPending = false;
                        monster.AttackClientVisible = false;
                        monster.AttackContactOnly = false;
                        monster.AttackSoundPending = false;
                        monster.AttackHitResolved = false;
                        monster.AttackStartTick = 0;
                        monster.AttackEndTick = 0;
                        monster.AttackCommitTick = 0;
                        monster.AttackSoundTick = 0;
                        if (monster.IsAlive)
                            monster.State = MonsterState.Combat;
                        TraceMonsterState(monster, "attack-complete", target, dist, allowedRange, "end");
                        continue;
                    }
                    if (monster.AttackSoundPending && (int)_combatTick >= monster.AttackSoundTick)
                        ConsumeMonsterAttackSoundRng(monster);
                    if ((int)_combatTick < monster.AttackCommitTick) continue;
                    if (monster.AttackSoundPending)
                        ConsumeMonsterAttackSoundRng(monster);
                    monster.AttackHitResolved = true;
                    if (monster.AttackEndTick < (int)_combatTick)
                        monster.AttackEndTick = (int)_combatTick;
                    WeaponUseRuntime.Instance.DrainDueProjectileImpactsForPlayer(target.EntityId, GetRoomRngForMonster(monster), now, "MON-DAMAGE-subentity-first");
                    if (!monster.IsAlive)
                    {
                        CancelMonsterPendingAttack(monster, "killed_by_subentity_before_attack");
                        continue;
                    }
                    if (monster.SpellKnockDownEndTime > 0f && now + 0.0001f < monster.SpellKnockDownEndTime)
                    {
                        CancelMonsterPendingAttack(monster, "knockdown_at_hit_resolve");
                        Debug.LogError($"[MON-DAMAGE] {monster.Name}#{monster.EntityId}->{target.Name} cancel knockdownActive endTime={monster.SpellKnockDownEndTime:F3} now={now:F3} sourceFunction=KnockDown::UpdateKnockDown@0x0052A6B0");
                        OnMonsterAttackResolved?.Invoke(monster, target, false, target.PlayerState.CurrentHPWire);
                        continue;
                    }
                    if (!TryDeferMonsterProjectileImpact(monster, target, dist, "MON-DAMAGE", "ProcessMonsterAttacks", now))
                        ResolveMonsterAttackDamage(monster, target, dist, "MON-DAMAGE", "ProcessMonsterAttacks");
                    continue;
                }
                else
                {
                    if (!monster.IsAlive) continue;
                    if (!allowNewAttacks) continue;
                    if (!attackPathClear)
                    {
                        ClearMonsterCombatContact(monster, target);
                        continue;
                    }
                    SelectMonsterPrimarySkillForAttack(monster, target, dist, allowedRange, "new-start");
                    if (!initUseGateway && !monster.UsePrimaryActiveSkillThisAttack)
                    {
                        if (clientTargetAction)
                            TraceFarTargetAction(monster, target, dist, allowedRange, "new-start");
                        if (monster.State == MonsterState.Combat)
                            monster.State = MonsterState.Chase;
                        continue;
                    }
                    if ((int)_combatTick < monster.NextAttackTick) continue;
                    monster.AttackPending = true;
                    monster.AttackClientVisible = false;
                    monster.AttackContactOnly = false;
                    monster.AttackHitResolved = false;
                    monster.AttackSoundRaw = 0;
                    monster.AttackSoundGateRaw = 0;
                    monster.AttackSoundRepeatRaw = 0;
                    monster.AttackUseRaw = 0;
                    monster.AttackStartTick = (int)_combatTick;
                    monster.AttackCommitTick = 0;
                    monster.AttackSoundTick = 0;
                    monster.AttackEndTick = 0;
                    monster.AttackSoundPending = false;
                    monster.AttackCommitTargetX = target.PosX;
                    monster.AttackCommitTargetY = target.PosY;
                    monster.State = MonsterState.Attacking;
                    monster.Ai?.SetState(MonsterStateId.Attack, "DoAttackAction");
                    if (monster.Ai != null)
                        monster.Ai.SkillDelayTimer = 300;
                    EncounterMarkActive(monster, "DoAttackAction");
                    monster.AttackSessionId++;
                    if (monster.AttackSessionId == 0) monster.AttackSessionId = 1;
                    int handlerCount = OnMonsterAttackStarted?.GetInvocationList().Length ?? 0;
                    Debug.LogError($"[MON-ATTACK] dispatch start {monster.Name}->{target.Name} session={monster.AttackSessionId} handlers={handlerCount} dist={dist:F1} range={allowedRange:F1}");
                    TraceMonsterState(monster, "attack-start", target, dist, allowedRange, "dispatch");
                    OnMonsterAttackStarted?.Invoke(monster, target, monster.AttackSessionId);
                    if (monster.AttackPending)
                        ArmMonsterRuntimeAttack(monster, target, "START", now);
                    else
                        Debug.LogError($"[MON-ATTACK] {monster.Name}->{target.Name} START canceled session={monster.AttackSessionId}");
                    continue;
                }
            }
        }

        private void ArmMonsterClientVisibleAttack(Monster monster, CombatPlayer target, float clientNow = -1f)
        {
            ArmMonsterRuntimeAttack(monster, target, "START", clientNow);
        }

        private void ArmMonsterRuntimeAttack(Monster monster, CombatPlayer target, string marker, float clientNow = -1f)
        {
            if (monster == null || target == null) return;
            float now = clientNow >= 0f ? clientNow : GetCombatTime();
            if (IsMonsterKnockDownActive(monster, now, "attack-arm"))
                return;
            int nowTick = (int)_combatTick;
            if (monster.AttackStartTick <= 0)
                monster.AttackStartTick = nowTick;
            if (monster.AttackCommitTick > 0)
                return;
            AdvanceMonsterAttackAnimation(monster);
            int hitTicks = ResolveMonsterAttackWindupTicks(monster);
            ResolveMonsterAttackAnimationFrames(monster, out int totalFrames, out int hitFrame, out int soundFrame);
            int startTick = monster.AttackStartTick > 0 ? monster.AttackStartTick : nowTick;
            monster.AttackCommitTick = startTick + hitTicks;
            monster.AttackSoundTick = startTick + ResolveMonsterAttackSoundDelayTicks(monster, hitTicks);
            monster.AttackEndTick = startTick + Mathf.Max(hitTicks, ResolveMonsterAttackTotalTicks(monster));
            monster.NextAttackTick = startTick + ResolveMonsterAttackCooldownTicks(monster);
            monster.AttackHitResolved = false;
            monster.AttackSoundPending = monster.HasAttackSound || monster.AttackWeaponSoundCount > 0 || monster.AttackRepeatSoundCount > 0;
            Debug.LogError($"[MON-ATTACK] {monster.Name}->{target.Name} {marker} anim={monster.AttackAnimationIndex} session={monster.AttackSessionId} use=0x{monster.AttackUseRaw:X8} frames={totalFrames}/{hitFrame}/{soundFrame} soundAtTick={monster.AttackSoundTick} hitAtTick={monster.AttackCommitTick} endAtTick={monster.AttackEndTick} weaponSounds={monster.AttackWeaponSoundCount} attackSounds={monster.AttackRepeatSoundCount}");
            TraceMonsterState(monster, "attack-arm", target, -1f, ResolveMonsterEffectiveAttackRange(monster), marker);
        }

        public void Update(float deltaTime)
        {
            Update(deltaTime, true);
        }

        public void Update(float deltaTime, bool allowNewMonsterAttacks)
        {
            TraceCombatTick("Update", deltaTime, allowNewMonsterAttacks);
            ProcessProximityAggro();
            ProcessMonsterAssistAlerts();
            float clientNow = GetCombatTime();
            ProcessMonsterAttacks(deltaTime, 0, allowNewMonsterAttacks, null, clientNow);
            AdvancePlayerDamageModifierRuntime(0, clientNow, "Update");
            AdvanceMonsterModifierRuntime(null, clientNow, "Update");
            ProcessMonsterMovement(deltaTime);
            TickEncounterDeactivation();
            UpdateMaintenance(deltaTime);
        }

        public void UpdateMonsterEntity(uint entityId, float deltaTime, bool allowNewMonsterAttacks)
        {
            UpdateMonsterEntity(entityId, deltaTime, allowNewMonsterAttacks, GetCombatTime());
        }

        public void TickMonsterUpdateSkillsForEntity(uint entityId)
        {
            if (_activeMonsters.TryGetValue(entityId, out var monster))
                TickMonsterUpdateSkillsTimer(monster);
        }

        public void UpdateMonsterEntity(uint entityId, float deltaTime, bool allowNewMonsterAttacks, float clientNow)
        {
            UpdateMonsterEntity(entityId, deltaTime, allowNewMonsterAttacks, clientNow, true);
        }

        public void UpdateMonsterEntity(uint entityId, float deltaTime, bool allowNewMonsterAttacks, float clientNow, bool entityBudgetTick)
        {
            if (!_activeMonsters.TryGetValue(entityId, out var monster))
                return;
            TraceCombatTick("UpdateMonsterEntity", deltaTime, allowNewMonsterAttacks, monster);
            if (ShouldRunProximityScanThisTick(monster))
                ProcessProximityAggro(0, monster);
            ProcessMonsterAssistAlerts(monster);
            ProcessMonsterAttacks(deltaTime, 0, allowNewMonsterAttacks, monster, clientNow);
            AdvanceMonsterModifierRuntimeForTarget(entityId, GetRoomRngForMonster(monster), clientNow, "UpdateMonsterEntity");
            if (entityBudgetTick)
                ProcessMonsterMovement(deltaTime, 0, monster);
        }

        public void UpdateMaintenance(float deltaTime)
        {
            ProcessDeathLifecycles(deltaTime);
            for (int respawnIndex = _respawnQueue.Count - 1; respawnIndex >= 0; respawnIndex--)
            {
                if (Time.time >= _respawnQueue[respawnIndex].RespawnTime)
                {
                    SpawnMonster(_respawnQueue[respawnIndex].GCType, _respawnQueue[respawnIndex].PosX, _respawnQueue[respawnIndex].PosY, _respawnQueue[respawnIndex].PosZ, _respawnQueue[respawnIndex].Heading, _respawnQueue[respawnIndex].ZoneName, _respawnQueue[respawnIndex].EncounterGroupKey, _respawnQueue[respawnIndex].EncounterDifficulty, null, _respawnQueue[respawnIndex].InstanceKey);
                    _respawnQueue.RemoveAt(respawnIndex);
                }
            }
        }

        public void ClearAll()
        {
            foreach (var id in new List<uint>(_activeMonsters.Keys))
                DespawnMonster(id, false);
            _respawnQueue.Clear();
            _players.Clear();
            _playerCombatAdvanceTime.Clear();
            _monsterRuntimeHPWire.Clear();
            _monsterHPStates.Clear();
            _monsterRuntimeDamageCommitted.Clear();
            _monsterHPRegenLastTime.Clear();
            _monsterHPRegenCarryWire.Clear();
            _monsterHPRegenCooldownTicks.Clear();
            _monsterManaRegenLastTime.Clear();
            _monsterManaRegenCooldownTicks.Clear();
            _monsterStateTraceSignatures.Clear();
            _encounterRuntimes.Clear();
            _activeMonsterModifiers.Clear();
            _activePlayerDamageModifiers.Clear();
            _playerModifierNetworkIds.Clear();
            _pendingModifierKills.Clear();
            _monsterFarTargetActionLogTime.Clear();
            _pendingMonsterProjectiles.Clear();
            _nextMonsterProjectileSequence = 0;
            _entityOrder.Clear();
            _entityOrderSet.Clear();
            _hasCompletedEntityUpdate = false;
            _lastCompletedEntityUpdateTick = 0;
            _lastCompletedEntityUpdateTime = -1f;
            _hasCompletedSubEntityUpdate = false;
            _lastCompletedSubEntityUpdateTick = 0;
            _lastCompletedSubEntityUpdateTime = -1f;
        }

        private float GetAggroRangeForTier(string tier)
        {
            return 30f;
        }

        private byte GetLevelForTier(string tier)
        {
            if (string.IsNullOrEmpty(tier))
                return 1;

            return tier.ToUpper() switch
            {
                "FODDER" => 0,
                "RECRUIT" => 1,
                "VETERAN" => 2,
                "CHAMPION" => 4,
                "HERO" => 6,
                "WARMONGER" => 8,
                _ => 1
            };
        }

        private byte GetZoneBaseLevel(string zoneName)
        {
            if (string.IsNullOrEmpty(zoneName)) return 1;

            string lower = zoneName.ToLower();

            if (lower.Contains("tutorial")) return 1;

            if (lower.StartsWith("dungeon") && lower.Length >= 9)
            {
                if (int.TryParse(lower.Substring(7, 2), out int dungeonNum))
                {
                    return (byte)(dungeonNum * 4);
                }
            }

            return 1;
        }

        private float GetLeashRangeForTier(string tier)
        {
            return tier?.ToLower() switch
            {
                "grunt" => 25f,
                "champion" => 30f,
                "hero" => 35f,
                "boss" => 50f,
                _ => 30f
            };
        }

        public Monster GetMonsterByBehaviorId(uint behaviorId)
        {
            foreach (var monster in _activeMonsters.Values)
            {
                if (monster.BehaviorId == behaviorId)
                    return monster;
            }
            return null;
        }

        public Monster GetMonsterByManipulatorsId(uint manipulatorsId)
        {
            foreach (var monster in _activeMonsters.Values)
            {
                if (monster.ManipulatorsId == manipulatorsId)
                    return monster;
            }
            return null;
        }

        public Monster GetMonsterBySkillsId(uint skillsId)
        {
            foreach (var monster in _activeMonsters.Values)
            {
                if (monster.SkillsId == skillsId)
                    return monster;
            }
            return null;
        }

        private float GetMoveSpeedFromCreature(CreatureData creature)
        {
            if (!string.IsNullOrEmpty(creature.speed) &&
                float.TryParse(creature.speed, out float speed))
            {
                float serverSpeed = speed;
                Debug.LogError($"[COMBAT-RUNTIME] gcType='{creature.gcType}' dbMoveSpeed={speed} serverSpeed={serverSpeed}");
                return serverSpeed;
            }

            return 30f;
        }
        public List<Monster> GetMonstersInRange(float x, float y, float range)
        {
            return GetMonstersInRange(x, y, range, null);
        }

        public List<Monster> GetMonstersInRange(float x, float y, float range, string instanceKey)
        {
            var result = new List<Monster>();
            float rangeSq = range * range;
            string normalizedInstanceKey = string.IsNullOrWhiteSpace(instanceKey)
                ? null
                : RoomRuntime.NormalizeInstanceKey(instanceKey);
            foreach (var monster in GetAllMonsters())
            {
                if (!monster.IsAlive) continue;
                if (!MatchesInstance(monster, normalizedInstanceKey)) continue;
                float dx = monster.PosX - x;
                float dy = monster.PosY - y;
                if (dx * dx + dy * dy <= rangeSq)
                    result.Add(monster);
            }
            return result;
        }

        public List<Monster> GetMonstersInClientVisibleRange(float x, float y, float range, string instanceKey, float clientNow)
        {
            var result = new List<Monster>();
            float rangeSq = range * range;
            string normalizedInstanceKey = string.IsNullOrWhiteSpace(instanceKey)
                ? null
                : RoomRuntime.NormalizeInstanceKey(instanceKey);
            foreach (var monster in GetAllMonsters())
            {
                if (!monster.IsAlive) continue;
                if (!MatchesInstance(monster, normalizedInstanceKey)) continue;

                float rawDx = monster.PosX - x;
                float rawDy = monster.PosY - y;
                if (rawDx * rawDx + rawDy * rawDy <= rangeSq)
                {
                    result.Add(monster);
                    continue;
                }

                if (!TryGetMonsterClientVisiblePosition(monster, clientNow, out float visibleX, out float visibleY))
                    continue;
                float visibleDx = visibleX - x;
                float visibleDy = visibleY - y;
                if (visibleDx * visibleDx + visibleDy * visibleDy <= rangeSq)
                    result.Add(monster);
            }
            return result;
        }

        public List<Monster> GetMonstersInSpellEffectRange(float x, float y, float range, string instanceKey, float clientNow)
        {
            return GetMonstersInSpellEffectRange(x, y, 0f, range, instanceKey, clientNow);
        }

        public List<Monster> GetMonstersInSpellEffectRange(float x, float y, float z, float range, string instanceKey, float clientNow)
        {
            var ranked = new List<(Monster Monster, float DistanceSq)>();
            float rangeSq = range * range;
            string normalizedInstanceKey = string.IsNullOrWhiteSpace(instanceKey)
                ? null
                : RoomRuntime.NormalizeInstanceKey(instanceKey);
            foreach (var monster in GetAllMonsters())
            {
                if (!monster.IsAlive) continue;
                if (!MatchesInstance(monster, normalizedInstanceKey)) continue;
                if (!TryGetMonsterClientUnitPosition(monster, clientNow, out float visibleX, out float visibleY, out float visibleZ))
                    continue;

                float dx = visibleX - x;
                float dy = visibleY - y;
                float dz = visibleZ - z;
                float distanceSq = dx * dx + dy * dy + dz * dz;
                if (distanceSq <= rangeSq)
                    ranked.Add((monster, distanceSq));
            }

            ranked.Sort((left, right) =>
            {
                int distanceCompare = left.DistanceSq.CompareTo(right.DistanceSq);
                return distanceCompare != 0
                    ? distanceCompare
                    : left.Monster.EntityId.CompareTo(right.Monster.EntityId);
            });

            var result = new List<Monster>(ranked.Count);
            foreach (var item in ranked)
                result.Add(item.Monster);
            return result;
        }
        private class RespawnEntry
        {
            public string GCType;
            public string ZoneName;
            public string InstanceKey;
            public float PosX, PosY, PosZ;
            public float Heading;
            public string EncounterGroupKey;
            public float EncounterDifficulty;
            public float RespawnTime;
        }
    }

    public class PendingMonsterProjectileImpact
    {
        public long Sequence;
        public uint MonsterEntityId;
        public uint TargetEntityId;
        public string Marker;
        public string Source;
        public float Dist;
        public int FireTick;
        public int FlightTicks;
        public int DueTick;
        public float DueTime;
    }

    public class CombatPlayer
    {
        public uint EntityId;
        public string Name;
        public PlayerState PlayerState;
        public float PosX, PosY;
        public bool IsAlive = true;
        public bool HasActiveClientAttack;
        public uint ActiveClientAttackTargetId;
        public string InstanceKey;
    }

    public class DamageResult
    {
        public bool Success;
        public int DamageDealt;
        public bool IsCritical;
        public bool DefenderDied;
        public uint NewHPWire;
    }
}
