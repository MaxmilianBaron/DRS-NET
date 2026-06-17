using System;
using System.Collections.Generic;
using DungeonRunners.Combat;
using DungeonRunners.Networking;
using DungeonRunners.Engine;

namespace DungeonRunners.Networking.EntitySynchInfo
{
    public sealed class EntitySynchInfoAuthority
    {
        public static EntitySynchInfoAuthority Instance { get; } = new EntitySynchInfoAuthority();

        public const uint WirePerHP = 256u;
        public const uint ClientHpMaxToleranceWire = 5u * WirePerHP;
        private const double TickSeconds = 1d / 30d;
        private const ushort DamageRegenCooldownTicks = 300;

        private readonly Dictionary<uint, EntitySynchInfoOwnerState> _byEntity = new Dictionary<uint, EntitySynchInfoOwnerState>();
        private readonly Dictionary<uint, EntitySynchInfoOwnerState> _playerByConn = new Dictionary<uint, EntitySynchInfoOwnerState>();
        private readonly Dictionary<ushort, uint> _componentOwnerEntity = new Dictionary<ushort, uint>();
        private readonly Dictionary<ushort, EntitySynchInfoOwnerKind> _componentKind = new Dictionary<ushort, EntitySynchInfoOwnerKind>();
        private readonly Dictionary<uint, RRConnection> _playerConnections = new Dictionary<uint, RRConnection>();
        private readonly Dictionary<uint, Monster> _monsters = new Dictionary<uint, Monster>();

        private EntitySynchInfoAuthority()
        {
        }

        public EntitySynchInfoOwnerState RegisterPlayer(RRConnection conn, PlayerState playerState, uint avatarEntityId)
        {
            return RegisterPlayer(conn, playerState, avatarEntityId, null, true);
        }

        private EntitySynchInfoOwnerState RegisterPlayer(RRConnection conn, PlayerState playerState, uint avatarEntityId, float? clientTime, bool allowWallClockRegenInit)
        {
            if (conn == null || playerState == null || avatarEntityId == 0) return null;
            EntitySynchInfoOwnerState s = GetOrCreate(avatarEntityId, EntitySynchInfoOwnerKind.PlayerAvatar);
            s.OwnerKind = EntitySynchInfoOwnerKind.PlayerAvatar;
            s.ConnectionId = (uint)Math.Max(0, conn.ConnId);
            s.OwnerName = conn.LoginName ?? conn.ConnId.ToString();
            s.MaxHPWire = playerState.MaxHPWire;
            uint rawHP = playerState.CurrentHPWire;
            s.RuntimeHPWire = s.MaxHPWire > 0 ? Math.Min(rawHP, s.MaxHPWire) : rawHP;
            s.IsAlive = s.RuntimeHPWire > 0;
            if (s.LastRegenTime < 0f)
            {
                if (clientTime.HasValue)
                    s.LastRegenTime = clientTime.Value;
                else if (allowWallClockRegenInit)
                    s.LastRegenTime = Time.time;
            }
            if (conn.IgnoreClientHPUntilTime > s.IgnoreClientReportsUntil)
                s.IgnoreClientReportsUntil = conn.IgnoreClientHPUntilTime;
            _playerByConn[(uint)Math.Max(0, conn.ConnId)] = s;
            _playerConnections[avatarEntityId] = conn;
            RegisterPlayerComponents(conn, avatarEntityId);
            MirrorPlayer(conn, playerState, s);
            return s;
        }

        public EntitySynchInfoOwnerState RegisterMonster(Monster monster)
        {
            if (monster == null || monster.EntityId == 0) return null;
            bool existed = _byEntity.TryGetValue(monster.EntityId, out EntitySynchInfoOwnerState s);
            if (!existed)
                s = GetOrCreate(monster.EntityId, EntitySynchInfoOwnerKind.Monster);
            s.OwnerKind = EntitySynchInfoOwnerKind.Monster;
            s.OwnerName = monster.Name ?? monster.EntityId.ToString();
            s.MaxHPWire = monster.MaxHPWire;
            uint rawHP = monster.IsAlive ? monster.CurrentHPWire : 0;
            uint clampedRaw = s.MaxHPWire > 0 ? Math.Min(rawHP, s.MaxHPWire) : rawHP;
            if (!existed)
            {
                s.RuntimeHPWire = clampedRaw;
            }
            else if (!monster.IsAlive)
            {
                s.RuntimeHPWire = 0;
            }
            else if (s.RuntimeHPWire > 0 && clampedRaw > 0)
            {
                s.RuntimeHPWire = Math.Min(s.ClampHP(s.RuntimeHPWire), clampedRaw);
            }
            else
            {
                s.RuntimeHPWire = s.ClampHP(s.RuntimeHPWire);
            }
            s.IsAlive = monster.IsAlive && s.RuntimeHPWire > 0;
            if (s.LastRegenTime < 0f) s.LastRegenTime = Time.time;
            _monsters[monster.EntityId] = monster;
            RegisterMonsterComponents(monster);
            MirrorToMonster(monster, s);
            return s;
        }

        public EntitySynchInfoOwnerState RegisterBlingGnome(uint gnomeEntityId, ushort behaviorComponentId, uint maxHPWire, string name)
        {
            if (gnomeEntityId == 0) return null;
            bool existed = _byEntity.TryGetValue(gnomeEntityId, out EntitySynchInfoOwnerState s);
            if (!existed)
                s = GetOrCreate(gnomeEntityId, EntitySynchInfoOwnerKind.BlingGnome);
            s.OwnerKind = EntitySynchInfoOwnerKind.BlingGnome;
            s.OwnerName = name ?? gnomeEntityId.ToString();
            s.MaxHPWire = maxHPWire;
            if (!existed)
                s.RuntimeHPWire = s.ClampHP(maxHPWire);
            else
                s.RuntimeHPWire = s.ClampHP(s.RuntimeHPWire);
            s.IsAlive = s.RuntimeHPWire > 0;
            if (s.LastRegenTime < 0f) s.LastRegenTime = Time.time;
            RegisterBlingGnomeComponents(gnomeEntityId, behaviorComponentId);
            return s;
        }

        public void UnregisterBlingGnome(uint gnomeEntityId)
        {
            if (gnomeEntityId == 0) return;
            _byEntity.Remove(gnomeEntityId);
            RemoveComponentOwner(gnomeEntityId);
        }

        public bool TryResolveBlingGnomeOwner(uint gnomeEntityId, out EntitySynchInfoOwnerRef owner)
        {
            owner = default;
            if (gnomeEntityId == 0 || !_byEntity.TryGetValue(gnomeEntityId, out EntitySynchInfoOwnerState s)) return false;
            owner = EntitySynchInfoOwnerRef.BlingGnomeOwner(gnomeEntityId, s.OwnerName);
            return true;
        }

        public bool TryGetBlingGnomeRuntimeHP(uint gnomeEntityId, out uint hpWire)
        {
            hpWire = 0;
            if (gnomeEntityId == 0 || !_byEntity.TryGetValue(gnomeEntityId, out EntitySynchInfoOwnerState s)) return false;
            hpWire = s.ClampHP(s.RuntimeHPWire);
            return true;
        }

        public bool IsBlingGnomeComponent(ushort componentId)
        {
            return componentId != 0
                && _componentKind.TryGetValue(componentId, out EntitySynchInfoOwnerKind kind)
                && kind == EntitySynchInfoOwnerKind.BlingGnome;
        }

        public bool ObserveClientBlingGnomeHP(RRConnection conn, ushort componentId, uint clientHPWire, string source)
        {
            if (componentId == 0) return false;
            if (!_componentKind.TryGetValue(componentId, out EntitySynchInfoOwnerKind kind) || kind != EntitySynchInfoOwnerKind.BlingGnome)
                return false;
            if (!TryResolveComponentOwner(conn, componentId, 0, out EntitySynchInfoOwnerRef owner) || owner.Kind != EntitySynchInfoOwnerKind.BlingGnome)
                return false;
            if (!_byEntity.TryGetValue(owner.EntityId, out EntitySynchInfoOwnerState s)) return false;
            uint observed = clientHPWire > s.MaxHPWire ? s.MaxHPWire : clientHPWire;
            return ObserveClientHpReport(owner, observed, ClassifyReportSource(source), source ?? "client-gnome-hp", true, out _);
        }

        public void UnregisterPlayer(RRConnection conn)
        {
            if (conn == null) return;
            uint avatarEntityId = conn.Avatar != null ? (uint)conn.Avatar.Id : 0;
            _playerByConn.Remove((uint)Math.Max(0, conn.ConnId));
            if (avatarEntityId != 0)
                _playerConnections.Remove(avatarEntityId);
            UnregisterPlayerComponents(conn, avatarEntityId);
        }

        public void UnregisterMonster(uint monsterEntityId)
        {
            if (monsterEntityId == 0) return;
            _byEntity.Remove(monsterEntityId);
            _monsters.Remove(monsterEntityId);
            RemoveComponentOwner(monsterEntityId);
        }

        public EntitySynchInfoOwnerState GetPlayerState(RRConnection conn, PlayerState playerState, uint avatarEntityId)
        {
            return RegisterPlayer(conn, playerState, avatarEntityId);
        }

        public EntitySynchInfoOwnerState GetMonsterState(Monster monster)
        {
            return RegisterMonster(monster);
        }

        public bool TryResolvePlayerOwner(RRConnection conn, PlayerState playerState, uint avatarEntityId, out EntitySynchInfoOwnerRef owner)
        {
            owner = default;
            if (RegisterPlayer(conn, playerState, avatarEntityId) == null) return false;
            owner = EntitySynchInfoOwnerRef.Player(avatarEntityId, conn);
            return true;
        }

        public bool TryResolveMonsterOwner(Monster monster, out EntitySynchInfoOwnerRef owner)
        {
            owner = default;
            if (RegisterMonster(monster) == null) return false;
            owner = EntitySynchInfoOwnerRef.MonsterOwner(monster);
            return true;
        }

        public bool TryResolveComponentOwner(RRConnection conn, ushort componentId, uint entityId, out EntitySynchInfoOwnerRef owner)
        {
            owner = default;
            if (componentId != 0 && _componentKind.TryGetValue(componentId, out EntitySynchInfoOwnerKind kind) && kind == EntitySynchInfoOwnerKind.NonUnit)
            {
                owner = EntitySynchInfoOwnerRef.NonUnit(componentId, "player-empty-component");
                return true;
            }
            if (componentId != 0 && _componentOwnerEntity.TryGetValue(componentId, out uint ownerEntity))
            {
                if (_byEntity.TryGetValue(ownerEntity, out EntitySynchInfoOwnerState state))
                {
                    if (state.OwnerKind == EntitySynchInfoOwnerKind.PlayerAvatar)
                    {
                        _playerConnections.TryGetValue(ownerEntity, out RRConnection playerConn);
                        owner = EntitySynchInfoOwnerRef.Player(ownerEntity, playerConn ?? conn);
                        return true;
                    }
                    if (state.OwnerKind == EntitySynchInfoOwnerKind.Monster)
                    {
                        _monsters.TryGetValue(ownerEntity, out Monster monster);
                        owner = new EntitySynchInfoOwnerRef(EntitySynchInfoOwnerKind.Monster, ownerEntity, null, monster, state.OwnerName);
                        return true;
                    }
                    if (state.OwnerKind == EntitySynchInfoOwnerKind.BlingGnome)
                    {
                        owner = EntitySynchInfoOwnerRef.BlingGnomeOwner(ownerEntity, state.OwnerName);
                        return true;
                    }
                }
            }
            if (entityId != 0 && _byEntity.TryGetValue(entityId, out EntitySynchInfoOwnerState entityState))
            {
                if (entityState.OwnerKind == EntitySynchInfoOwnerKind.PlayerAvatar)
                {
                    _playerConnections.TryGetValue(entityId, out RRConnection playerConn);
                    owner = EntitySynchInfoOwnerRef.Player(entityId, playerConn ?? conn);
                    return true;
                }
                if (entityState.OwnerKind == EntitySynchInfoOwnerKind.Monster)
                {
                    _monsters.TryGetValue(entityId, out Monster monster);
                    owner = new EntitySynchInfoOwnerRef(EntitySynchInfoOwnerKind.Monster, entityId, null, monster, entityState.OwnerName);
                    return true;
                }
                if (entityState.OwnerKind == EntitySynchInfoOwnerKind.BlingGnome)
                {
                    owner = EntitySynchInfoOwnerRef.BlingGnomeOwner(entityId, entityState.OwnerName);
                    return true;
                }
            }
            return false;
        }

        public EntitySynchInfoResolveResult ResolveOutboundPlayer(RRConnection conn, PlayerState playerState, uint avatarEntityId, EntitySynchInfoContext context, string packetName, bool advanceEntitySynchInfo, out uint hpWire)
        {
            return ResolveOutboundPlayerCore(conn, playerState, avatarEntityId, context, packetName, advanceEntitySynchInfo, null, out hpWire);
        }

        public EntitySynchInfoResolveResult ResolveOutboundPlayer(RRConnection conn, PlayerState playerState, uint avatarEntityId, EntitySynchInfoContext context, string packetName, bool advanceEntitySynchInfo, float clientTime, out uint hpWire)
        {
            return ResolveOutboundPlayerCore(conn, playerState, avatarEntityId, context, packetName, advanceEntitySynchInfo, clientTime, out hpWire);
        }

        private EntitySynchInfoResolveResult ResolveOutboundPlayerCore(RRConnection conn, PlayerState playerState, uint avatarEntityId, EntitySynchInfoContext context, string packetName, bool advanceEntitySynchInfo, float? clientTime, out uint hpWire)
        {
            hpWire = 0;
            EntitySynchInfoOwnerState s = RegisterPlayer(conn, playerState, avatarEntityId, clientTime, false);
            if (s == null)
                return EntitySynchInfoResolveResult.Block(EntitySynchInfoOwnerKind.PlayerAvatar, avatarEntityId, "missing-player-state");
            if (advanceEntitySynchInfo && clientTime.HasValue)
            {
                playerState.AdvanceEntitySynchInfoHP(clientTime.Value, packetName ?? "EntitySynchInfoAuthority");
                ApplyPlayerKnownFromDomain(playerState, s);
            }
            hpWire = playerState.EntitySynchInfoHP;
            MarkOutbound(s, hpWire, packetName, clientTime);
            MirrorPlayer(conn, playerState, s);
            return EntitySynchInfoResolveResult.AllowHP(EntitySynchInfoOwnerKind.PlayerAvatar, avatarEntityId, hpWire, "client-visible-hp");
        }

        public EntitySynchInfoResolveResult ResolveOutboundMonster(Monster monster, EntitySynchInfoContext context, string packetName, out uint hpWire)
        {
            hpWire = 0;
            EntitySynchInfoOwnerState s = RegisterMonster(monster);
            if (s == null)
                return EntitySynchInfoResolveResult.Block(EntitySynchInfoOwnerKind.Monster, monster != null ? monster.EntityId : 0, "missing-monster-state");
            hpWire = s.ClampHP(s.RuntimeHPWire);
            MarkOutbound(s, hpWire, packetName);
            MirrorToMonster(monster, s);
            return EntitySynchInfoResolveResult.AllowHP(EntitySynchInfoOwnerKind.Monster, monster.EntityId, hpWire, "client-visible-hp");
        }

        public bool ObserveClientHpReport(EntitySynchInfoOwnerRef owner, uint hpWire, EntitySynchInfoReportSource source, string packetName, bool reportCameFromEntitySynchInfo, out EntitySynchInfoReportDecision decision)
        {
            decision = default;
            if (!_byEntity.TryGetValue(owner.EntityId, out EntitySynchInfoOwnerState s))
            {
                decision = EntitySynchInfoReportDecision.Reject("missing-state");
                return false;
            }
            if (Time.time < s.IgnoreClientReportsUntil)
            {
                RememberReject(s, hpWire, "ignore-window");
                decision = EntitySynchInfoReportDecision.Reject("ignore-window");
                return false;
            }
            if (s.MaxHPWire > 0 && hpWire > s.MaxHPWire + ClientHpMaxToleranceWire)
            {
                RememberReject(s, hpWire, "above-max+tolerance");
                decision = EntitySynchInfoReportDecision.Reject("above-max+tolerance");
                return false;
            }
            uint clamped = s.ClampHP(hpWire);
            RecordObservedClientHP(s, clamped, source.ToString());
            decision = EntitySynchInfoReportDecision.AcceptObserved("observed-only");
            MirrorOwner(owner, s);
            return true;
        }

        public void RecordPlayerOutboundHP(RRConnection conn, PlayerState playerState, uint avatarEntityId, uint hpWire, string source)
        {
            EntitySynchInfoOwnerState s = RegisterPlayer(conn, playerState, avatarEntityId);
            if (s == null) return;
            MarkOutbound(s, s.ClampHP(hpWire), source ?? "outbound");
            MirrorPlayer(conn, playerState, s);
        }

        public void RecordMonsterOutboundHP(Monster monster, uint hpWire, string source)
        {
            EntitySynchInfoOwnerState s = RegisterMonster(monster);
            if (s == null) return;
            uint clamped = s.ClampHP(hpWire);
            MarkOutbound(s, clamped, source ?? "outbound");
            MirrorToMonster(monster, s);
        }

        public void SetRuntimeHP(EntitySynchInfoOwnerState s, uint hpWire, string reason)
        {
            SetRuntimeHPInternal(s, hpWire, reason);
        }

        public void ApplyDamage(EntitySynchInfoOwnerState s, uint damageWire, string reason)
        {
            ApplyDamage(s, damageWire, reason, null);
        }

        public void ApplyDamage(EntitySynchInfoOwnerState s, uint damageWire, string reason, float clientDamageTime)
        {
            ApplyDamage(s, damageWire, reason, (float?)clientDamageTime);
        }

        private void ApplyDamage(EntitySynchInfoOwnerState s, uint damageWire, string reason, float? clientDamageTime)
        {
            if (s == null || damageWire == 0) return;
            uint old = s.RuntimeHPWire;
            uint next = damageWire >= old ? 0 : old - damageWire;
            SetRuntimeHPInternal(s, next, reason ?? "damage");
            s.RegenCooldownTicks = DamageRegenCooldownTicks;
            s.RegenCarrySeconds = 0d;
            if (clientDamageTime.HasValue)
                s.LastRegenTime = clientDamageTime.Value;
        }

        public void ApplyHeal(EntitySynchInfoOwnerState s, uint amountWire, string reason, bool visibleImmediately)
        {
            if (s == null || amountWire == 0) return;
            uint old = s.RuntimeHPWire;
            uint next = s.ClampHP(old + amountWire);
            SetRuntimeHPInternal(s, next, reason ?? "heal");
        }

        public void SetMaxHP(EntitySynchInfoOwnerState s, uint newMaxHPWire, EntitySynchInfoMaxHPMode mode, string reason)
        {
            if (s == null) return;
            uint oldMax = s.MaxHPWire;
            uint oldRuntime = s.RuntimeHPWire;
            s.MaxHPWire = newMaxHPWire;
            if (mode == EntitySynchInfoMaxHPMode.FillToMax)
                s.RuntimeHPWire = newMaxHPWire;
            else if (mode == EntitySynchInfoMaxHPMode.PreservePercent && oldMax > 0)
            {
                ulong scaled = (ulong)oldRuntime * newMaxHPWire / oldMax;
                s.RuntimeHPWire = (uint)Math.Min((ulong)newMaxHPWire, scaled);
            }
            else if (mode == EntitySynchInfoMaxHPMode.AddDeltaToCurrent && newMaxHPWire > oldMax)
                s.RuntimeHPWire = Math.Min(newMaxHPWire, oldRuntime + (newMaxHPWire - oldMax));
            else
                s.RuntimeHPWire = Math.Min(oldRuntime, newMaxHPWire);
        }

        public void AdvanceRegen(EntitySynchInfoOwnerState s, float now, bool allowObservedAdvance)
        {
            if (s == null) return;
            if (!s.RegenEnabled || s.RegenFactor <= 0 || s.MaxHPWire == 0 || !s.IsAlive)
            {
                s.LastRegenTime = now;
                s.RegenCarrySeconds = 0d;
                return;
            }
            if (s.LastRegenTime < 0f)
            {
                s.LastRegenTime = now;
                return;
            }
            if (s.RuntimeHPWire >= s.MaxHPWire)
            {
                s.LastRegenTime = now;
                s.RegenCarrySeconds = 0d;
                return;
            }
            double elapsed = (now - s.LastRegenTime) + s.RegenCarrySeconds;
            int ticks = (int)(elapsed / TickSeconds);
            if (ticks <= 0)
            {
                s.RegenCarrySeconds = elapsed;
                s.LastRegenTime = now;
                return;
            }
            uint before = s.RuntimeHPWire;
            for (int tickIndex = 0; tickIndex < ticks && s.RuntimeHPWire < s.MaxHPWire; tickIndex++)
            {
                if (s.RegenCooldownTicks > 0)
                {
                    s.RegenCooldownTicks--;
                    if (s.RegenCooldownTicks > 0)
                        continue;
                }
                uint delta = (uint)(((long)s.RegenFactor * s.MaxHPWire) / 3000L + 1L);
                s.RuntimeHPWire = s.ClampHP(s.RuntimeHPWire + delta);
            }
            s.RegenCarrySeconds = elapsed - ticks * TickSeconds;
            s.LastRegenTime = now;
        }

        public static EntitySynchInfoReportSource ClassifyReportSource(string source)
        {
            if (string.IsNullOrEmpty(source)) return EntitySynchInfoReportSource.Unknown;
            if (source.StartsWith("ENTITY-SYNCH-INFO", StringComparison.Ordinal)) return EntitySynchInfoReportSource.ClientEntitySynchSuffix;
            if (source.StartsWith("SEND-UPDATE", StringComparison.Ordinal)) return EntitySynchInfoReportSource.ClientSendUpdate;
            if (source.StartsWith("PLAYER-STATE-ENTITY-SYNCH-INFO", StringComparison.Ordinal)) return EntitySynchInfoReportSource.ClientPlayerStateEntitySynchInfo;
            if (source.StartsWith("ACTION-0x50-ENTITY-SYNCH-INFO", StringComparison.Ordinal)) return EntitySynchInfoReportSource.ClientEntitySynchSuffix;
            if (source.StartsWith("MONSTER-MOVE-HP", StringComparison.Ordinal)) return EntitySynchInfoReportSource.ClientEntitySynchSuffix;
            if (source.StartsWith("MONSTER-SM-HP", StringComparison.Ordinal)) return EntitySynchInfoReportSource.ClientEntitySynchSuffix;
            if (source.StartsWith("ENTITY-SYNCH-INFO", StringComparison.Ordinal)) return EntitySynchInfoReportSource.ClientEntitySynchSuffix;
            return EntitySynchInfoReportSource.Unknown;
        }

        private EntitySynchInfoOwnerState GetOrCreate(uint entityId, EntitySynchInfoOwnerKind kind)
        {
            if (!_byEntity.TryGetValue(entityId, out EntitySynchInfoOwnerState s))
            {
                s = new EntitySynchInfoOwnerState { OwnerEntityId = entityId, OwnerKind = kind };
                _byEntity[entityId] = s;
            }
            return s;
        }

        private void RegisterPlayerComponents(RRConnection conn, uint avatarEntityId)
        {
            RegisterComponent((ushort)avatarEntityId, avatarEntityId, EntitySynchInfoOwnerKind.PlayerAvatar);
            RegisterComponent((ushort)conn.UnitBehaviorId, avatarEntityId, EntitySynchInfoOwnerKind.PlayerAvatar);
            RegisterComponent(conn.BehaviorComponentId, avatarEntityId, EntitySynchInfoOwnerKind.PlayerAvatar);
            RegisterComponent(conn.SkillsComponentId, avatarEntityId, EntitySynchInfoOwnerKind.PlayerAvatar);
            RegisterComponent(conn.ManipulatorsComponentId, avatarEntityId, EntitySynchInfoOwnerKind.PlayerAvatar);
            RegisterComponent((ushort)conn.ModifiersId, avatarEntityId, EntitySynchInfoOwnerKind.PlayerAvatar);
            RegisterComponent(conn.ModifiersComponentId, avatarEntityId, EntitySynchInfoOwnerKind.PlayerAvatar);
            RegisterComponent((ushort)conn.UnitContainerId, avatarEntityId, EntitySynchInfoOwnerKind.PlayerAvatar);
            RegisterComponent((ushort)conn.DialogManagerId, avatarEntityId, EntitySynchInfoOwnerKind.NonUnit);
            RegisterComponent((ushort)conn.QuestManagerId, avatarEntityId, EntitySynchInfoOwnerKind.NonUnit);
        }

        private void UnregisterPlayerComponents(RRConnection conn, uint avatarEntityId)
        {
            if (conn == null) return;
            RemoveComponent((ushort)avatarEntityId);
            RemoveComponent((ushort)conn.UnitBehaviorId);
            RemoveComponent(conn.BehaviorComponentId);
            RemoveComponent(conn.SkillsComponentId);
            RemoveComponent(conn.ManipulatorsComponentId);
            RemoveComponent((ushort)conn.ModifiersId);
            RemoveComponent(conn.ModifiersComponentId);
            RemoveComponent((ushort)conn.UnitContainerId);
            RemoveComponent((ushort)conn.DialogManagerId);
            RemoveComponent((ushort)conn.QuestManagerId);
        }

        private void RegisterMonsterComponents(Monster monster)
        {
            if (monster == null) return;
            RegisterComponent((ushort)monster.EntityId, monster.EntityId, EntitySynchInfoOwnerKind.Monster);
            RegisterComponent((ushort)monster.BehaviorId, monster.EntityId, EntitySynchInfoOwnerKind.Monster);
            RegisterComponent((ushort)monster.SkillsId, monster.EntityId, EntitySynchInfoOwnerKind.Monster);
            RegisterComponent((ushort)monster.ManipulatorsId, monster.EntityId, EntitySynchInfoOwnerKind.Monster);
            RegisterComponent((ushort)monster.ModifiersId, monster.EntityId, EntitySynchInfoOwnerKind.Monster);
            RegisterComponent((ushort)monster.UnitId, monster.EntityId, EntitySynchInfoOwnerKind.Monster);
        }

        private void RegisterBlingGnomeComponents(uint gnomeEntityId, ushort behaviorComponentId)
        {
            RegisterComponent((ushort)gnomeEntityId, gnomeEntityId, EntitySynchInfoOwnerKind.BlingGnome);
            RegisterComponent(behaviorComponentId, gnomeEntityId, EntitySynchInfoOwnerKind.BlingGnome);
        }

        private void RegisterComponent(ushort componentId, uint ownerEntityId, EntitySynchInfoOwnerKind kind)
        {
            if (componentId == 0 || ownerEntityId == 0) return;
            _componentOwnerEntity[componentId] = ownerEntityId;
            _componentKind[componentId] = kind;
        }

        private void RemoveComponent(ushort componentId)
        {
            if (componentId == 0) return;
            _componentOwnerEntity.Remove(componentId);
            _componentKind.Remove(componentId);
        }

        private void RemoveComponentOwner(uint ownerEntityId)
        {
            if (ownerEntityId == 0) return;
            List<ushort> remove = new List<ushort>();
            foreach (var componentOwnerEntry in _componentOwnerEntity)
            {
                if (componentOwnerEntry.Value == ownerEntityId)
                    remove.Add(componentOwnerEntry.Key);
            }
            foreach (ushort componentId in remove)
                RemoveComponent(componentId);
        }

        private void SetRuntimeHPInternal(EntitySynchInfoOwnerState s, uint hpWire, string reason)
        {
            if (s == null) return;
            uint old = s.RuntimeHPWire;
            uint next = s.ClampHP(hpWire);
            s.RuntimeHPWire = next;
            s.IsAlive = next > 0;
        }

        private void RecordObservedClientHP(EntitySynchInfoOwnerState s, uint hpWire, string source)
        {
            uint clamped = s.ClampHP(hpWire);
            s.LastObservedClientHPWire = clamped;
            s.LastObservedClientHPTime = Time.time;
            s.LastObservedClientHPSource = source ?? "unknown";
        }

        private void RememberReject(EntitySynchInfoOwnerState s, uint hpWire, string reason)
        {
            s.LastRejectedClientHPWire = hpWire;
            s.LastRejectedClientHPTime = Time.time;
            s.LastRejectedClientHPReason = reason ?? "rejected";
        }

        private void ApplyPlayerKnownFromDomain(PlayerState playerState, EntitySynchInfoOwnerState s)
        {
            if (playerState == null || s == null) return;
            s.MaxHPWire = playerState.MaxHPWire;
            uint rawHP = playerState.CurrentHPWire;
            s.RuntimeHPWire = s.MaxHPWire > 0 ? Math.Min(rawHP, s.MaxHPWire) : rawHP;
            s.IsAlive = s.RuntimeHPWire > 0;
        }

        private void MirrorPlayer(RRConnection conn, PlayerState playerState, EntitySynchInfoOwnerState s)
        {
            if (conn == null || s == null) return;
            conn.LastOutboundHPWire = s.LastOutboundHPWire;
            conn.LastOutboundHPTime = s.LastOutboundHPTime;
            conn.LastOutboundHPSource = s.LastOutboundPacket;
        }

        private void MirrorToMonster(Monster monster, EntitySynchInfoOwnerState s)
        {
            if (monster == null || s == null) return;
            monster.CurrentHPWire = s.RuntimeHPWire;
            monster.LastClientHPReportTime = s.LastObservedClientHPTime;
            monster.LastClientHPReportWire = s.LastObservedClientHPWire;
        }

        private void MirrorOwner(EntitySynchInfoOwnerRef owner, EntitySynchInfoOwnerState s)
        {
            if (owner.Kind == EntitySynchInfoOwnerKind.PlayerAvatar && owner.Connection != null)
                MirrorPlayer(owner.Connection, null, s);
            if (owner.Kind == EntitySynchInfoOwnerKind.Monster && owner.Monster != null)
                MirrorToMonster(owner.Monster, s);
        }

        private void MarkOutbound(EntitySynchInfoOwnerState s, uint hpWire, string packetName, float? clientTime = null)
        {
            s.LastOutboundHPWire = hpWire;
            s.LastOutboundHPTime = clientTime ?? Time.time;
            s.LastOutboundPacket = packetName ?? "unknown";
            s.LastOutboundFlags = 0x02;
        }

        private bool IsHpIncreaseSource(EntitySynchInfoReportSource source)
        {
            return source == EntitySynchInfoReportSource.ServerPotionHeal
                || source == EntitySynchInfoReportSource.ServerSkillHeal
                || source == EntitySynchInfoReportSource.ServerRegen
                || source == EntitySynchInfoReportSource.ServerEquipmentChange
                || source == EntitySynchInfoReportSource.ServerStatAllocation
                || source == EntitySynchInfoReportSource.ServerLevelUp
                || source == EntitySynchInfoReportSource.ServerRespawn
                || source == EntitySynchInfoReportSource.ServerZoneLoad
                || source == EntitySynchInfoReportSource.PersistenceLoad;
        }

        private bool IsWithinTolerance(uint a, uint b, uint tolerance)
        {
            return a >= b ? a - b <= tolerance : b - a <= tolerance;
        }
    }
}
