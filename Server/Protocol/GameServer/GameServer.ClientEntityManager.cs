using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using DungeonRunners.Engine;
using DungeonRunners.Utilities;
using DungeonRunners.Data;
using DungeonRunners.Core;
using System.Text;
using System.Reflection;
using System.Runtime.CompilerServices;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;
using DungeonRunners.Gameplay;
using DungeonRunners.Database;
using DungeonRunners.Engine.Playables;
using System.Security.Cryptography;
using DungeonRunners.Combat;
using DungeonRunners.Networking.EntitySynchInfo;

namespace DungeonRunners.Networking
{
    public partial class GameServer
    {
        private uint _nextEntityId = 170;

        private long _nextLootEntityId = 0xBFFF;
        public ushort GetNextLootEntityId()
        {
            const long range = (long)(LOOT_ID_MAX - LOOT_ID_MIN + 1);
            long raw = System.Threading.Interlocked.Increment(ref _nextLootEntityId);
            return (ushort)(LOOT_ID_MIN + ((raw - LOOT_ID_MIN) % range));
        }


        public List<(ushort entityId, DroppedItemInfo info)> GetDroppedItemsNear(string zone, uint instanceId, float x, float y, float radius)
        {
            var result = new List<(ushort, DroppedItemInfo)>();
            lock (_droppedItems)
            {
                foreach (var droppedItemEntry in _droppedItems)
                {
                    var info = droppedItemEntry.Value;
                    if (info.Zone != zone || info.InstanceId != instanceId) continue;
                    float dx = info.PosX - x;
                    float dy = info.PosY - y;
                    if (dx * dx + dy * dy <= radius * radius)
                        result.Add((droppedItemEntry.Key, info));
                }
            }
            return result;
        }

        public bool BlingGnomePickupItem(RRConnection conn, ushort entityId)
        {
            if (!_droppedItems.TryGetValue(entityId, out var info)) return false;

            if (info.DbId > 0)
            {
                try
                {
                    using (var db = DungeonRunners.Database.GameDatabase.GetConnection())
                    {
                        DungeonRunners.Database.GameDatabase.ExecuteNonQuery(db,
                            "DELETE FROM dropped_items WHERE id = @id",
                            ("@id", info.DbId));
                    }
                    _dbIdToEntityId.Remove(info.DbId);
                    Debug.LogError($"[BLING-PICKUP] Deleted converted drop from DB: dbId={info.DbId}, entityId=0x{entityId:X4}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[BLING-PICKUP] DB delete failed: {ex.Message}");
                }
            }

            bool sentDespawn = false;
            foreach (var other in _connections.Values)
            {
                if (other == null || !other.IsSpawned) continue;
                if (!string.Equals(other.CurrentZoneName ?? "", info.Zone ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                if (other.InstanceId != info.InstanceId) continue;
                SendDespawnEntity(other, entityId);
                sentDespawn = true;
            }

            if (!sentDespawn && conn != null)
                SendDespawnEntity(conn, entityId);

            _droppedItems.Remove(entityId);
            Debug.LogError($"[BLING-PICKUP] Removed entity 0x{entityId:X4} from world dbId={info.DbId}");
            return true;
        }

        public void BlingGnomeCreditGold(RRConnection conn, uint goldAmount)
        {
            if (goldAmount == 0) return;
            if (!_playerStates.TryGetValue(conn.LoginName, out var state)) return;
            state.Gold += goldAmount;
            if (conn.UnitContainerId != 0)
            {
                var goldPacket = new LEWriter();
                goldPacket.WriteByte(0x07);
                goldPacket.WriteByte(0x35);
                goldPacket.WriteUInt16(conn.UnitContainerId);
                goldPacket.WriteByte(0x20);
                goldPacket.WriteUInt32(goldAmount);
                goldPacket.WriteByte(0x00);
                goldPacket.WriteUInt32(0x00000000);
                goldPacket.WriteByte(0x01);
                if (TryWriteEntitySynchForComponent(conn, goldPacket, conn.UnitContainerId, 0x20, EntitySynchInfoContext.PlayerActionResponse, "BLING-GOLD", true))
                {
                    goldPacket.WriteByte(0x06);
                    SendToClient(conn, goldPacket.ToArray());
                }
            }
            Debug.LogError($"[BLING-GOLD] Credited {goldAmount} gold to {conn.LoginName} (total: {state.Gold})");
            try
            {
                if (_selectedCharacter.ContainsKey(conn.LoginName))
                    using (var db = GameDatabase.GetConnection())
                        GameDatabase.ExecuteNonQuery(db,
                            "UPDATE characters SET gold=@g WHERE id=@id",
                            ("@g", (int)state.Gold), ("@id", (int)_selectedCharacter[conn.LoginName].Id));
            }
            catch (Exception ex) { Debug.LogError($"[BLING-GOLD] db state=failed message='{ex.Message}'"); }
        }

        public void BlingGnomeSpawnGoldPile(RRConnection conn, float posX, float posY, float posZ, uint goldAmount)
        {
            ushort entityId = GetNextLootEntityId();
            var goldInfo = new DroppedItemInfo
            {
                Item = null,
                DbId = 0,
                Zone = conn.CurrentZoneName ?? "",
                ZoneId = conn.CurrentZoneId,
                InstanceId = conn.InstanceId,
                PosX = posX,
                PosY = posY,
                PosZ = posZ,
                PlayerLevel = 1,
                DroppedBy = conn.LoginName ?? "",
                IsGoldDrop = true,
                GoldAmount = goldAmount
            };
            _droppedItems[entityId] = goldInfo;
            SendGoldPileSpawnPacket(conn, entityId, posX, posY, posZ, goldAmount);
            Debug.LogError($"[BLING-GOLD] Created gold pile 0x{entityId:X4} worth {goldAmount}g at ({posX:F1},{posY:F1},{posZ:F1})");
        }

        public uint GetItemGoldValue(DroppedItemInfo item)
        {
            if (item.Item != null)
            {
                var itemData = DatabaseLoader.FindItem(item.Item.GCClass);
                if (itemData != null && itemData.goldValue > 0)
                    return (uint)itemData.goldValue;
            }
            return (uint)Math.Max(1, item.PlayerLevel * 2);
        }

        private Dictionary<string, uint> _spawnedAvatarIds = new Dictionary<string, uint>();
        private uint _nextCombatComponentId = 60000;
        private Dictionary<string, Dictionary<ushort, string>> _playerComponentTypes = new Dictionary<string, Dictionary<ushort, string>>();

        private Dictionary<string, ushort> _playerSkillsComponentId = new Dictionary<string, ushort>();
        private Dictionary<string, uint> _playerAvatarEntityId = new Dictionary<string, uint>();
        private Dictionary<string, uint> _playerNextSkillEntityId = new Dictionary<string, uint>();
        private ushort _entityIdCounter = 0x0100;
        private Dictionary<long, ushort> _dbIdToEntityId = new Dictionary<long, ushort>();
        private float _droppedItemCleanupTimer = 0f;

        public ushort GetNextEntityId()
        {
            return _entityIdCounter++;
        }
        public uint GetPlayerAvatarId(string loginName)
        {
            if (_spawnedAvatarIds.TryGetValue(loginName, out uint avatarId))
            {
                return avatarId;
            }
            return 0;
        }

        public GCObject GetSelectedCharacter(string loginName)
        {
            if (!string.IsNullOrEmpty(loginName) && _selectedCharacter.TryGetValue(loginName, out var ch))
                return ch;
            return null;
        }

        public IEnumerable<RRConnection> AllConnectedConnections()
        {
            foreach (var conn in _connections.Values)
            {
                if (!conn.IsConnected || string.IsNullOrEmpty(conn.LoginName)) continue;
                yield return conn;
            }
        }

        public IEnumerable<RRConnection> GetConnectedMemberConnsForPosse(uint posseId)
        {
            if (posseId == 0) yield break;
            foreach (var conn in _connections.Values)
            {
                if (!conn.IsConnected || string.IsNullOrEmpty(conn.LoginName)) continue;
                if (!_selectedCharacter.TryGetValue(conn.LoginName, out var gcObj) || gcObj == null) continue;
                var savedCharacter = CharacterRepository.GetCharacter(gcObj.Id);
                if (savedCharacter != null && savedCharacter.posseId == posseId) yield return conn;
            }
        }

        private void SendPosseStateForCharacter(RRConnection conn, uint characterId,
            Action<RRConnection, byte, byte, byte[]> sendCompressed)
        {
            try
            {
                var savedChar = CharacterRepository.GetCharacter(characterId);
                if (savedChar != null && savedChar.posseId != 0)
                {
                    var posse = PosseRepository.GetPosse(savedChar.posseId);
                    if (posse != null)
                    {
                        var memberNames = PosseRepository.MemberNames(posse.Id);
                        var members = new List<(uint, string, bool)>(memberNames.Count);
                        using (var dbConn = GameDatabase.GetConnection())
                        using (var reader = GameDatabase.ExecuteReader(dbConn,
                            "SELECT id, name FROM characters WHERE posse_id = @pid ORDER BY id",
                            ("@pid", (int)posse.Id)))
                        {
                            while (reader.Read())
                            {
                                uint memberCharacterId = (uint)reader.GetInt32(0);
                                string characterName = reader.GetString(1);
                                members.Add((memberCharacterId, characterName, memberCharacterId == posse.FounderCharacterId));
                            }
                        }
                        Debug.LogError($"[POSSE-LOGIN] Restoring posse '{posse.Name}' id={posse.Id} ({members.Count} members) for {savedChar.name}");
                        PosseRuntime.Instance.SendCachedPosseFull(conn, characterId, posse, members, sendCompressed, this);
                        return;
                    }
                    Debug.LogError($"[POSSE-LOGIN] character id={characterId} has posse_id={savedChar.posseId} but row missing  falling back to no-posse state");
                }
                Debug.LogError($"[POSSE-LOGIN] character id={characterId} has no posse  skipping cache push so Tad button reads 'eligible'");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[POSSE-LOGIN] sendPosseStateForCharacter state=failed message='{ex.Message}'");
            }
        }


        private void OnMonsterSpawned(Monster monster)
        {
            _monsterBehaviorIds[monster.EntityId] = monster.BehaviorId;
            Debug.LogError($"[COMBAT] monster='{monster.Name}' spawnedId={monster.EntityId} behaviorId={monster.BehaviorId}");
            WirePacketTally.RegisterMonster(
                (ushort)monster.EntityId, (ushort)monster.BehaviorId,
                (ushort)monster.SkillsId, (ushort)monster.ManipulatorsId,
                (ushort)monster.ModifiersId);
        }

        private Dictionary<uint, int> _monsterOwnerConnId = new Dictionary<uint, int>();

        private void OnMonsterDespawned(Monster monster)
        {
            if (monster == null) return;
            uint entityId = monster.EntityId;
            _monsterBehaviorIds.Remove(entityId);
            _lastMonsterMoveSentAt.Remove(entityId);
            _pendingMonsterBehaviorUpdates.Remove(entityId);
            var packet = CombatPackets.BuildMonsterDespawnPacket(entityId);
            foreach (var zoneConn in _connections.Values)
            {
                if (zoneConn == null || !zoneConn.IsConnected || !zoneConn.IsSpawned) continue;
                if (!string.Equals(zoneConn.RuntimeInstanceKey, monster.InstanceKey, StringComparison.OrdinalIgnoreCase))
                    continue;
                SendCompressedA(zoneConn, 0x01, 0x0f, packet);
            }
            _monsterOwnerConnId.Remove(entityId);
        }

        private List<RRConnection> GetMonsterInstanceRecipients(uint entityId)
        {
            var recipients = new List<RRConnection>();
            if (!_monsterOwnerConnId.TryGetValue(entityId, out int ownerConnId)) return recipients;
            if (!_connections.TryGetValue(ownerConnId, out var ownerConn) || ownerConn == null) return recipients;
            foreach (var conn in _connections.Values)
            {
                if (conn == null || !conn.IsConnected) continue;
                if (conn.InstanceId != ownerConn.InstanceId) continue;
                if (!string.Equals(conn.CurrentZoneGcType, ownerConn.CurrentZoneGcType, StringComparison.OrdinalIgnoreCase)) continue;
                recipients.Add(conn);
            }
            return recipients;
        }

        internal List<RRConnection> GetInstanceRecipients(RRConnection origin)
        {
            var recipients = new List<RRConnection>();
            if (origin == null) return recipients;
            foreach (var conn in _connections.Values)
            {
                if (conn == null || !conn.IsConnected || !conn.IsSpawned || !conn.AllowFlush) continue;
                if (conn.InstanceId != origin.InstanceId) continue;
                if (!string.Equals(conn.CurrentZoneGcType, origin.CurrentZoneGcType, StringComparison.OrdinalIgnoreCase)) continue;
                recipients.Add(conn);
            }
            return recipients;
        }

        private void PrimeMonsterHPBeforeSynch(Monster monster)
        {
            CombatRuntime.Instance.PeekMonsterCurrentHPWire(monster);
        }

        private void PrimeMonsterHPBeforeSynch(Monster monster, float now)
        {
            CombatRuntime.Instance.PeekMonsterCurrentHPWire(monster);
        }

        private WeaponUseFlushResult FlushMonsterRuntimeBeforeSynch(RRConnection conn, Monster monster, EntitySynchInfoContext context, string packetName, float validationCutoffTime, float suffixNow, uint validationCutoffTick)
        {
            var empty = new WeaponUseFlushResult();
            if (monster == null)
                return empty;

            string source = $"{packetName ?? "unknown"} context={context}";
            uint beforeHP = CombatRuntime.Instance.PeekMonsterCurrentHPWire(monster);
            if (conn?.Avatar != null)
                CombatRuntime.Instance.UpdatePlayerPosition((uint)conn.Avatar.Id, conn.PlayerPosX, conn.PlayerPosY);

            var weaponFlush = Combat.WeaponUseRuntime.Instance.FlushMonsterEntityBeforeSynch(
                monster.EntityId,
                CombatRuntime.Instance.GetRoomRngForMonster(monster),
                validationCutoffTime,
                $"MonsterPreSuffix:{source}");

            if (Combat.WeaponUseRuntime.Instance.HasPendingKills)
                DrainWeaponUseStateKills($"MonsterPreSuffix:{source}");

            uint afterWeaponHP = CombatRuntime.Instance.PeekMonsterCurrentHPWire(monster);
            var spellFlush = FlushPendingSpellsForMonsterBeforeSynch(monster, validationCutoffTime, $"MonsterPreSuffix:{source}");
            uint afterSpellHP = CombatRuntime.Instance.PeekMonsterCurrentHPWire(monster);
            uint afterHP = CombatRuntime.Instance.AdvanceMonsterRuntimeBeforeSynch(monster, validationCutoffTime, $"MON-PRE-SUFFIX:{source}");
            if (CombatRuntime.Instance.HasPendingModifierKills)
                DrainPendingModifierKills();
            if (weaponFlush.BeforeHPWire == 0 && beforeHP != 0)
                weaponFlush.BeforeHPWire = beforeHP;
            weaponFlush.AfterHPWire = afterHP;

            Debug.LogError($"[MON-PRE-SUFFIX-COMBAT] source={packetName ?? "unknown"} context={context} target={monster.TargetId} monster={monster.Name}#{monster.EntityId} behavior={monster.BehaviorId} hp={beforeHP}->{afterHP}/{monster.MaxHPWire} spellHP={spellFlush.BeforeHPWire}->{afterSpellHP} spellPending={spellFlush.PendingBefore}->{spellFlush.PendingAfter} spellDue={spellFlush.DueForTarget} spellDueOther={spellFlush.DueOther} spellApplied={spellFlush.Applied} weaponHP={weaponFlush.BeforeHPWire}->{afterWeaponHP} pendingProjectiles={weaponFlush.PendingBefore}->{weaponFlush.PendingAfter} projectilesResolved={weaponFlush.ProjectilesResolved} cycleTicks={weaponFlush.CycleTicks} hadTargetCycle={weaponFlush.HadTargetCycle} clientNow={suffixNow:F3} cutoffTick={validationCutoffTick} cutoffTime={validationCutoffTime:F3}");
            return weaponFlush;
        }

        private bool TryPrimeMonsterHPBeforeSynch(RRConnection conn, Monster monster, uint hpWire, string source)
        {
            if (conn == null || monster == null) return false;
            string hpState = CombatRuntime.Instance != null ? CombatRuntime.Instance.DescribeMonsterHPState(monster) : "state=<missing>";
            if (VerboseMonsterDiag) Debug.LogError($"[MON-HP-PRIMER] suffix current {monster.Name}#{monster.EntityId} hp={hpWire / 256f:F2}/{monster.MaxHPWire / 256f:F2} source={source} {hpState}");
            string key = $"{conn.ConnId}\u001f{monster.EntityId}";
            if (_monsterEntityInitHPState.TryGetValue(key, out uint current) && current == hpWire)
                return true;
            byte[] packet = CombatPackets.BuildMonsterEntityInitHPRefreshPacket(monster, hpWire);
            conn.MessageQueue.Enqueue(packet);
            _monsterEntityInitHPState[key] = hpWire;
            return true;
        }

        private EntitySynchInfoDecision ResolveMonsterRuntimeHPDecision(Monster monster, string packetName, string reason)
        {
            GetValidationCutoff(out uint validationCutoffTick, out float validationCutoffTime);
            CombatRuntime.Instance.TryResolveMonsterEntitySynchInfoHP(monster, EntitySynchInfoContext.Unknown, packetName, validationCutoffTime, out uint hpWire, out string hpReason);
            EntitySynchInfoAuthority.Instance.RecordMonsterOutboundHP(monster, hpWire, $"{packetName} {hpReason} {reason}");
            Debug.LogError($"[ENTITY-SYNCH-INFO-RECOVER] packet={packetName} owner=Monster entity={monster.EntityId} hp={hpWire} reason={reason} hpReason='{hpReason}'");
            return EntitySynchInfoDecision.HP(EntitySynchInfoOwner.Monster, hpWire, $"{packetName} {hpReason} {reason}", monster.EntityId, monster.BehaviorId, 0x04, GetCombatNow(), $"{hpReason}; validationCutoffTick={validationCutoffTick} validationCutoffTime={validationCutoffTime:F3}", validationCutoffTick, validationCutoffTime, hpMutationSource: hpReason);
        }

        private void OnMonsterAttackStarted(Monster monster, CombatPlayer target, byte sessionId)
        {
            if (monster == null || target == null) return;


            RRConnection targetConn = null;
            foreach (var conn in _connections.Values)
            {
                if (conn == null || !conn.IsConnected || conn.Avatar == null) continue;
                if (conn.Avatar.Id != target.EntityId) continue;
                targetConn = conn;
                break;
            }

            if (targetConn == null)
            {
                CombatRuntime.Instance.CancelMonsterPendingAttack(monster, "MON-ATTACK-no-target-connection");
                return;
            }

            if (IsZoneSpawnInvulnerabilityBlockingCombat(targetConn))
            {
                CombatRuntime.Instance.CancelMonsterPendingAttack(monster, "ZoneSpawn-blocked-MON-ATTACK");
                Debug.LogError($"[MON-ATTACK] Deferred packet while ZoneSpawn blocks combat {monster.Name}->{target.Name} behavior={monster.BehaviorId} session={sessionId}");
                return;
            }

            if (!monster.IsAlive || CombatRuntime.Instance.PeekMonsterCurrentHPWire(monster) == 0)
            {
                CombatRuntime.Instance.CancelMonsterPendingAttack(monster, "MON-ATTACK-flush-dead");
                return;
            }
            byte useFlags = ResolveMonsterPrimaryManipulatorId(monster);
            bool useTargetAction = ShouldUseMonsterUseTargetAction(monster);
            monster.AttackClientVisible = true;
            monster.AttackClientVisibleTime = GetCombatNow();
            QueuePendingMonsterBehaviorUpdate(new PendingMonsterBehaviorUpdate
            {
                Kind = PendingMonsterBehaviorKind.Attack,
                EntityId = monster.EntityId,
                BehaviorId = monster.BehaviorId,
                ConnId = targetConn.ConnId,
                TargetEntityId = target.EntityId,
                UseFlags = useFlags,
                SessionId = sessionId,
                UseTargetAction = useTargetAction,
                QueuedAt = GetCombatNow()
            }, "MON-ATTACK");
            Debug.LogError($"[MON-ATTACK-QUEUE] client {(useTargetAction ? "UseTarget" : "AttackTarget2")} {monster.Name}->{target.Name} behavior={monster.BehaviorId} session={sessionId} flags={useFlags} target={target.EntityId}");
        }

        private void OnMonsterAttackResolved(Monster monster, CombatPlayer target, bool damaged, uint hpWire)
        {
            HandlePlayerDamageResolved(monster, target, damaged, hpWire, damaged ? "MON-ATTACK-RESOLVE-HIT" : "MON-ATTACK-RESOLVE-NO-DAMAGE");
        }

        private void OnPlayerDamageResolved(Monster monster, CombatPlayer target, bool damaged, uint hpWire, string source)
        {
            HandlePlayerDamageResolved(monster, target, damaged, hpWire, string.IsNullOrWhiteSpace(source) ? (damaged ? "PLAYER-DAMAGE-RESOLVE-HIT" : "PLAYER-DAMAGE-RESOLVE-NO-DAMAGE") : source);
        }

        private void OnPlayerStunActionResolved(Monster monster, CombatPlayer target, CombatRuntime.PlayerStunActionResolved action)
        {
            if (target == null || action == null)
                return;
            RRConnection targetConn = null;
            foreach (var conn in _connections.Values)
            {
                if (conn == null || !conn.IsConnected || conn.Avatar == null) continue;
                if (conn.Avatar.Id != target.EntityId) continue;
                targetConn = conn;
                break;
            }
            if (targetConn == null)
            {
                Debug.LogError($"[PLAYER-STUN-ACTION] not-sent reason=missing-target-connection player={target.Name}#{target.EntityId} action={action.ActionClassName} actionId=0x{action.ActionClassId:X2}");
                return;
            }
            ushort componentId = targetConn.UnitBehaviorId != 0 ? (ushort)targetConn.UnitBehaviorId : targetConn.BehaviorComponentId;
            if (componentId == 0)
            {
                Debug.LogError($"[PLAYER-STUN-ACTION] not-sent reason=missing-behavior-component player={target.Name}#{target.EntityId} action={action.ActionClassName} actionId=0x{action.ActionClassId:X2}");
                return;
            }
            if (!ResolveEntitySynchInfoForComponent(targetConn, componentId, 0x04, EntitySynchInfoContext.PlayerActionResponse, target.EntityId, "PLAYER-STUN-ACTION", false, out EntitySynchInfoDecision decision))
            {
                Debug.LogError($"[PLAYER-STUN-ACTION] not-sent reason=entity-synch-info-decision player={target.Name}#{target.EntityId} action={action.ActionClassName} actionId=0x{action.ActionClassId:X2} decision={decision.Reason ?? "none"}");
                return;
            }
            ResolvedEntitySynchInfo entitySynchInfo = decision.ToResolved(target.EntityId, componentId, 0x04, GetCombatNow(), "PLAYER-STUN-ACTION sourceFunction=KnockBack::writeData@0x0052A320");
            byte[] packet = CombatPackets.BuildPlayerStunActionPacket(componentId, action.ActionClassId, action.HeadingWire, action.StrengthWire, entitySynchInfo);
            bool sent = QueueClientEntityStream(targetConn, packet);
            Debug.LogError($"[PLAYER-STUN-ACTION] sent={sent} monster={monster?.Name ?? "monster"}#{monster?.EntityId ?? 0} player={target.Name}#{target.EntityId} component=0x{componentId:X4} action={action.ActionClassName} actionId=0x{action.ActionClassId:X2} heading={action.HeadingWire} strengthWire={action.StrengthWire} authoredStrength={action.AuthoredStrength} knockDownPlayerBranch={action.KnockDownPlayerBranch} hp={entitySynchInfo.HPWire} chanceRaw=0x{action.ChanceRaw:X8} chanceRoll={action.ChanceRoll} stunResistWire={action.StunResistWire} stunRaw=0x{action.StunRaw:X8} stunRoll={action.StunRoll} source={action.Source ?? "unknown"} sourceFunction=Behavior::processUpdate@0x00515620 KnockBack::writeData@0x0052A320");
        }

        private void OnPlayerModifierNetworkEvent(Monster monster, CombatPlayer target, CombatRuntime.PlayerModifierNetworkEvent mod)
        {
            if (target == null || mod == null)
                return;
            RRConnection targetConn = null;
            foreach (var conn in _connections.Values)
            {
                if (conn == null || !conn.IsConnected || conn.Avatar == null) continue;
                if (conn.Avatar.Id != target.EntityId) continue;
                targetConn = conn;
                break;
            }
            if (targetConn == null)
            {
                Debug.LogError($"[PLAYER-MODIFIER-PACKET] not-sent reason=missing-target-connection player={target.Name}#{target.EntityId} add={mod.Add} gc={mod.GCType ?? ""} id={mod.ModifierId}");
                return;
            }
            ushort componentId = targetConn.ModifiersId != 0 ? targetConn.ModifiersId : targetConn.ModifiersComponentId;
            if (componentId == 0)
            {
                Debug.LogError($"[PLAYER-MODIFIER-PACKET] not-sent reason=missing-modifiers-component player={target.Name}#{target.EntityId} add={mod.Add} gc={mod.GCType ?? ""} id={mod.ModifierId}");
                return;
            }
            byte subtype = mod.Add ? (byte)0x00 : (byte)0x01;
            string packetName = mod.Add ? "PLAYER-MODIFIER-ADD" : "PLAYER-MODIFIER-REMOVE";
            if (!ResolveEntitySynchInfoForComponent(targetConn, componentId, subtype, EntitySynchInfoContext.PlayerActionResponse, target.EntityId, packetName, false, out EntitySynchInfoDecision decision))
            {
                Debug.LogError($"[PLAYER-MODIFIER-PACKET] not-sent reason=entity-synch-info-decision player={target.Name}#{target.EntityId} add={mod.Add} gc={mod.GCType ?? ""} id={mod.ModifierId} decision={decision.Reason ?? "none"}");
                return;
            }
            ResolvedEntitySynchInfo entitySynchInfo = decision.ToResolved(target.EntityId, componentId, subtype, GetCombatNow(), $"{packetName} sourceFunction={mod.SourceFunction ?? "Modifiers::processUpdate"}");
            byte[] packet = mod.Add
                ? CombatPackets.BuildPlayerModifierAddPacket(componentId, mod.GCType, mod.ModifierId, mod.Level, mod.PowerLevel, mod.DurationTicks, mod.SourceIsSelf, entitySynchInfo)
                : CombatPackets.BuildPlayerModifierRemovePacket(componentId, mod.ModifierId, entitySynchInfo);
            bool sent = QueueClientEntityStream(targetConn, packet);
            string lifecycle = mod.Lifecycle != null && mod.Lifecycle.HasClientLocalLifecycle
                ? $"visual={mod.Lifecycle.Visual ?? ""} initSound={mod.Lifecycle.InitSound ?? ""} initEffect={mod.Lifecycle.InitEffect ?? ""} removeEffect={mod.Lifecycle.RemoveEffect ?? ""} overlay={mod.Lifecycle.OverlayIcon ?? ""} overlayDuration={mod.Lifecycle.OverlayDuration}"
                : "none";
            Debug.LogError($"[PLAYER-MODIFIER-PACKET] sent={sent} packet={packetName} monster={monster?.Name ?? "monster"}#{monster?.EntityId ?? 0} player={target.Name}#{target.EntityId} component=0x{componentId:X4} gc={mod.GCType ?? ""} id={mod.ModifierId} level={mod.Level} power={mod.PowerLevel} duration={mod.DurationTicks} sourceIsSelf={mod.SourceIsSelf} replace={mod.Replace} hp={entitySynchInfo.HPWire} skill={mod.SkillPath ?? ""} effect={mod.EffectPath ?? ""} lifecycle={lifecycle} source={mod.Source ?? "unknown"} sourceFunction={mod.SourceFunction ?? "Modifiers::processUpdate"}");
        }

        private void HandlePlayerDamageResolved(Monster monster, CombatPlayer target, bool damaged, uint hpWire, string source)
        {
            if (target == null) return;
            RRConnection targetConn = null;
            foreach (var conn in _connections.Values)
            {
                if (conn == null || !conn.IsConnected || conn.Avatar == null) continue;
                if (conn.Avatar.Id != target.EntityId) continue;
                targetConn = conn;
                break;
            }

            if (targetConn == null) return;
            uint entitySynchInfoHP = hpWire;
            bool attackClientVisible = monster != null && monster.AttackClientVisible;
            bool clientContact = monster != null && monster.AttackContactOnly;
            bool attackPending = monster != null && monster.AttackPending;
            bool hitResolved = monster != null && monster.AttackHitResolved;
            Debug.LogError($"[PLAYER-HP-TRUTH] RESOLVE monster={monster?.Name ?? "unknown"}#{monster?.EntityId ?? 0} player={target.Name} damaged={damaged} serverHP={entitySynchInfoHP} clientVisible={attackClientVisible} clientContact={clientContact} pending={attackPending} hitResolved={hitResolved} source={source ?? "unknown"}");
            PlayerState state = GetPlayerState(targetConn.ConnId.ToString());
            if (damaged)
            {
                if (state != null)
                    CommitPlayerHPTruth(targetConn, state, source, entitySynchInfoHP, false, false);
                else
                    RecordPlayerHPKnown(targetConn, source, entitySynchInfoHP);
                if (entitySynchInfoHP == 0 || hpWire == 0)
                    HandleLocalPlayerDeathFromMonster(targetConn, monster, target, entitySynchInfoHP);
                else
                    Debug.LogError($"[PLAYER-HP-TRUTH] DAMAGE source={source} player={target.Name} hp={entitySynchInfoHP / 256f:F2}");
            }
            else
            {
                Debug.LogError($"[PLAYER-HP-TRUTH] NO-DAMAGE source={source} player={target.Name} keepEntitySynchInfoHP={(state != null ? state.EntitySynchInfoHP / 256f : entitySynchInfoHP / 256f):F2}");
            }
        }

        private void HandleLocalPlayerDeathFromMonster(RRConnection conn, Monster monster, CombatPlayer target, uint hpWire)
        {
            if (conn == null) return;
            if (BlingGnomeRuntime.Instance.HasGnome(conn.ConnId))
                BlingGnomeRuntime.Instance.DespawnGnome(conn, (c, d, t, b) => SendCompressedA(c, d, t, b), playDeathAnim: false);
            ClearUseTarget(conn);
            Combat.WeaponUseRuntime.Instance.ClearConnection(conn.ConnId.ToString());
            if (conn.Avatar != null)
                CombatRuntime.Instance.SetPlayerActiveClientAttack((uint)conn.Avatar.Id, false);

            ushort componentId = conn.UnitBehaviorId != 0 ? (ushort)conn.UnitBehaviorId : conn.BehaviorComponentId;
            if (componentId == 0)
            {
                Debug.LogError($"[PLAYER-DEATH] Missing player behavior component player={conn.LoginName ?? conn.ConnId.ToString()} hp={hpWire}");
                return;
            }

            var playerDeathControlMessage = new LEWriter();
            playerDeathControlMessage.WriteByte(0x07);
            if (!WriteClientControlUpdate(conn, playerDeathControlMessage, componentId, false, "PLAYER-DEATH-CONTROL", hpWire))
            {
                Debug.LogError($"[PLAYER-DEATH] Dropped local control release player={conn.LoginName ?? conn.ConnId.ToString()} component=0x{componentId:X4} hp={hpWire}");
                return;
            }
            playerDeathControlMessage.WriteByte(0x06);

            QueueClientEntityStream(conn, playerDeathControlMessage.ToArray());
            BroadcastPlayerDeath(conn);
            Debug.LogError($"[PLAYER-DEATH] Sent local control release player={target?.Name ?? conn.LoginName ?? conn.ConnId.ToString()} monster={monster?.Name ?? "unknown"}#{monster?.EntityId ?? 0} component=0x{componentId:X4} hp={hpWire}");
        }

        private static byte ResolveMonsterPrimaryManipulatorId(Monster monster)
        {
            if (monster == null || !monster.UsePrimaryActiveSkillThisAttack)
                return 10;
            if (monster.PrimaryActiveSkillId != 0)
                return monster.PrimaryActiveSkillId;
            if (monster.Manipulators == null)
                return 10;

            foreach (var manipulator in monster.Manipulators.Values)
            {
                if (!IsPrimaryActiveSkillManipulator(manipulator))
                    continue;
                if (TryGetManipulatorByte(manipulator, "ID", out byte id))
                    return id;
            }

            return 10;
        }

        private static bool ShouldUseMonsterUseTargetAction(Monster monster)
        {
            if (monster == null)
                return false;
            if (!monster.UsePrimaryActiveSkillThisAttack)
                return false;
            if (!string.IsNullOrWhiteSpace(monster.PrimaryActiveSkillPath))
                return true;
            if (monster.Manipulators == null)
                return false;

            foreach (var manipulator in monster.Manipulators.Values)
            {
                if (IsPrimaryActiveSkillManipulator(manipulator))
                    return true;
            }

            return false;
        }

        private static bool IsPrimaryActiveSkillManipulator(ManipulatorData manipulator)
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

        private static bool IsActiveSkillManipulatorPath(string gcType)
        {
            var gc = GCDatabase.Instance;
            string current = gcType;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (!string.IsNullOrWhiteSpace(current) && visited.Add(current))
            {
                if (current.Equals("ActiveSkill", StringComparison.OrdinalIgnoreCase) ||
                    current.EndsWith(".ActiveSkill", StringComparison.OrdinalIgnoreCase))
                    return true;

                var node = gc?.Resolve(current);
                current = node?.Extends;
            }

            return false;
        }

        private static bool TryGetManipulatorByte(ManipulatorData manipulator, string property, out byte value)
        {
            value = 0;
            if (manipulator?.properties == null || !manipulator.properties.TryGetValue(property, out string text))
                return false;
            return byte.TryParse(text, out value);
        }

        private static bool TryGetManipulatorBool(ManipulatorData manipulator, string property, out bool value)
        {
            value = false;
            if (manipulator?.properties == null || !manipulator.properties.TryGetValue(property, out string text))
                return false;
            if (bool.TryParse(text, out value))
                return true;
            if (text == "1")
            {
                value = true;
                return true;
            }
            if (text == "0")
            {
                value = false;
                return true;
            }
            return false;
        }

        private void OnMonsterMoved(Monster monster)
        {
            if (Time.time - monster.SpawnTime < 0.5f)
                return;
            if (!_monsterBehaviorIds.TryGetValue(monster.EntityId, out var behaviorId))
                return;
            RRConnection ownerConn = null;
            if (_monsterOwnerConnId.TryGetValue(monster.EntityId, out int ownerConnId) && _connections.TryGetValue(ownerConnId, out var foundConn))
                ownerConn = foundConn;
            if (ownerConn == null) return;
            if (!monster.IsAlive || CombatRuntime.Instance.PeekMonsterCurrentHPWire(monster) == 0)
                return;

            QueuePendingMonsterBehaviorUpdate(new PendingMonsterBehaviorUpdate
            {
                Kind = PendingMonsterBehaviorKind.Move,
                EntityId = monster.EntityId,
                BehaviorId = behaviorId,
                ConnId = ownerConn.ConnId,
                TargetEntityId = monster.TargetId,
                TargetX = monster.PosX,
                TargetY = monster.PosY,
                QueuedAt = GetCombatNow()
            }, "MON-MOVE");
        }

        private void QueuePendingMonsterBehaviorUpdate(PendingMonsterBehaviorUpdate update, string packetName)
        {
            bool followBroadcast = update.Kind == PendingMonsterBehaviorKind.Follow;
            if (update.EntityId == 0 || update.BehaviorId == 0 || (update.ConnId == 0 && !followBroadcast))
                return;
            uint queueKey = update.Kind switch
            {
                PendingMonsterBehaviorKind.Follow => update.EntityId | 0x40000000u,
                PendingMonsterBehaviorKind.Attack => update.EntityId | 0x20000000u,
                _ => update.EntityId
            };
            _pendingMonsterBehaviorUpdates[queueKey] = update;
            if (VerboseMonsterDiag) Debug.LogError($"[MON-BEHAVIOR-QUEUE] packet={packetName} entity={update.EntityId} behavior={update.BehaviorId} kind={update.Kind} conn={update.ConnId} target={update.TargetEntityId} queuedAt={update.QueuedAt:F3}");
        }

        private void FlushPendingMonsterBehaviorUpdates(float writerNow, uint writerTick)
        {
            if (_pendingMonsterBehaviorUpdates.Count == 0)
                return;

            var pending = _pendingMonsterBehaviorUpdates.Values.ToList();
            _pendingMonsterBehaviorUpdates.Clear();
            foreach (var update in pending)
            {
                switch (update.Kind)
                {
                    case PendingMonsterBehaviorKind.Move:
                        FlushPendingMonsterMoveUpdate(update, writerNow, writerTick);
                        break;
                    case PendingMonsterBehaviorKind.Attack:
                        FlushPendingMonsterAttackUpdate(update, writerNow, writerTick);
                        break;
                    case PendingMonsterBehaviorKind.Follow:
                        FlushPendingMonsterFollowUpdate(update, writerNow, writerTick);
                        break;
                }
            }
        }

        private ushort ResolveRemoteAvatarEntityId(RRConnection recipient, RRConnection targetOwnerConn, uint canonicalTargetId)
        {
            if (recipient == null || targetOwnerConn == null) return 0;
            if (recipient.ConnId == targetOwnerConn.ConnId) return (ushort)canonicalTargetId;
            if (_remoteAvatarIds.TryGetValue(recipient.LoginName, out var avatarMap)
                && avatarMap.TryGetValue(targetOwnerConn.LoginName, out ushort remoteAvatarTargetId))
                return remoteAvatarTargetId;
            return 0;
        }

        private void FlushPendingMonsterFollowUpdate(PendingMonsterBehaviorUpdate update, float writerNow, uint writerTick)
        {
            Monster monster = CombatRuntime.Instance.GetMonster(update.EntityId);
            if (monster == null || !monster.IsAlive || CombatRuntime.Instance.PeekMonsterCurrentHPWire(monster) == 0)
                return;
            RRConnection targetOwnerConn = FindConnectionByAvatarEntityId(update.TargetEntityId);
            foreach (var recipient in GetMonsterInstanceRecipients(update.EntityId))
            {
                if (recipient == null || !recipient.IsConnected) continue;
                ushort remoteAvatarTargetId = ResolveRemoteAvatarEntityId(recipient, targetOwnerConn, update.TargetEntityId);
                if (remoteAvatarTargetId == 0) continue;
                EntitySynchInfoDecision decision = ResolveMonsterBehaviorWriterHPDecision(recipient, monster, (ushort)update.BehaviorId, 0x04, EntitySynchInfoContext.MonsterAction, "MON-FOLLOW", writerNow, writerTick);
                if (decision.HPWire == 0) continue;
                if ((decision.Flags & 0x02) != 0 && !TryPrimeMonsterHPBeforeSynch(recipient, monster, decision.HPWire, "MON-FOLLOW"))
                    continue;
                var packet = CombatPackets.BuildMonsterFollowPacket(update.BehaviorId, remoteAvatarTargetId, decision.ToResolved(monster.EntityId, update.BehaviorId, 0x04, writerNow, "MON-FOLLOW client-writer"));
                recipient.MessageQueue.Enqueue(packet);
                if (VerboseMonsterDiag) Debug.LogError($"[MON-FOLLOW-FLUSH] {monster.Name}#{monster.EntityId} behavior={update.BehaviorId} target={update.TargetEntityId} remoteTarget={remoteAvatarTargetId} hp={decision.HPWire} conn={recipient.ConnId} sourceFunction=Follow::readData@0x5227A0");
            }
        }

        private void FlushPendingMonsterMoveUpdate(PendingMonsterBehaviorUpdate update, float writerNow, uint writerTick)
        {
            Monster monster = CombatRuntime.Instance.GetMonster(update.EntityId);
            if (monster == null)
                return;
            if (!monster.IsAlive || CombatRuntime.Instance.PeekMonsterCurrentHPWire(monster) == 0)
            {
                Debug.LogError($"[MON-MOVE-FLUSH-SKIP] dead {monster.Name}#{monster.EntityId} behavior={update.BehaviorId} queuedAt={update.QueuedAt:F3} writer={writerTick}@{writerNow:F3}");
                return;
            }

            bool recorded = false;
            foreach (var recipient in GetMonsterInstanceRecipients(update.EntityId))
            {
                EntitySynchInfoDecision decision = ResolveMonsterBehaviorWriterHPDecision(recipient, monster, (ushort)update.BehaviorId, 0x65, EntitySynchInfoContext.MonsterMove, "MON-MOVE", writerNow, writerTick);
                if (!monster.IsAlive || decision.HPWire == 0 || CombatRuntime.Instance.PeekMonsterCurrentHPWire(monster) == 0)
                {
                    Debug.LogError($"[MON-MOVE-FLUSH-SKIP] client-writer-dead {monster.Name}#{monster.EntityId} behavior={update.BehaviorId} hp={decision.HPWire} queuedAt={update.QueuedAt:F3} writer={writerTick}@{writerNow:F3}");
                    return;
                }
                if ((decision.Flags & 0x02) != 0 && !TryPrimeMonsterHPBeforeSynch(recipient, monster, decision.HPWire, "MON-MOVE"))
                    continue;
                var chasePlayer = CombatRuntime.Instance.GetPlayer(monster.TargetId);
                float headingDX = (chasePlayer != null ? chasePlayer.PosX : monster.PosX) - monster.PosX;
                float headingDY = (chasePlayer != null ? chasePlayer.PosY : monster.PosY) - monster.PosY;
                int headingWire = (headingDX == 0f && headingDY == 0f) ? 0 : (int)(Mathf.Atan2(headingDY, headingDX) * Mathf.Rad2Deg * 256f);
                var packet = CombatPackets.BuildMonsterMovePacket(monster.EntityId, update.BehaviorId, monster.PosFixedX, monster.PosFixedY, headingWire, decision.ToResolved(monster.EntityId, update.BehaviorId, 0x65, writerNow, "MON-MOVE client-writer"), false);
                recipient.MessageQueue.Enqueue(packet);
                if (!recorded)
                {
                    CombatRuntime.Instance.RecordMonsterMoveClientVisible(monster, monster.PosX, monster.PosY, writerNow, "MON-MOVE");
                    recorded = true;
                }
                if (VerboseMonsterDiag) Debug.LogError($"[MON-MOVE-FLUSH] {monster.Name}#{monster.EntityId} behavior={update.BehaviorId} target={update.TargetEntityId} dest=({update.TargetX:F1},{update.TargetY:F1}) hp={decision.HPWire} conn={recipient.ConnId} queuedAt={update.QueuedAt:F3} writer={writerTick}@{writerNow:F3} queue={recipient.MessageQueue.Count}");
            }
        }

        private void FlushPendingMonsterAttackUpdate(PendingMonsterBehaviorUpdate update, float writerNow, uint writerTick)
        {
            if (!_connections.TryGetValue(update.ConnId, out var targetConn) || targetConn == null || !targetConn.IsConnected)
                return;

            Monster monster = CombatRuntime.Instance.GetMonster(update.EntityId);
            CombatPlayer target = CombatRuntime.Instance.GetPlayer(update.TargetEntityId);
            if (monster == null || target == null)
                return;
            if (IsZoneSpawnInvulnerabilityBlockingCombat(targetConn))
            {
                monster.AttackClientVisible = false;
                CombatRuntime.Instance.CancelMonsterPendingAttack(monster, "ZoneSpawn-blocked-MON-ATTACK-flush");
                Debug.LogError($"[MON-ATTACK-FLUSH-SKIP] zoneSpawn {monster.Name}->{target.Name} behavior={update.BehaviorId} session={update.SessionId}");
                return;
            }
            if (!monster.IsAlive || CombatRuntime.Instance.PeekMonsterCurrentHPWire(monster) == 0)
            {
                monster.AttackClientVisible = false;
                CombatRuntime.Instance.CancelMonsterPendingAttack(monster, "MON-ATTACK-flush-dead");
                Debug.LogError($"[MON-ATTACK-FLUSH-SKIP] dead {monster.Name}#{monster.EntityId} behavior={update.BehaviorId} session={update.SessionId} queuedAt={update.QueuedAt:F3} writer={writerTick}@{writerNow:F3}");
                return;
            }

            EntitySynchInfoDecision decision = ResolveMonsterBehaviorWriterHPDecision(targetConn, monster, (ushort)update.BehaviorId, 0x04, EntitySynchInfoContext.MonsterAction, "MON-ATTACK", writerNow, writerTick);
            if (!monster.IsAlive || decision.HPWire == 0 || CombatRuntime.Instance.PeekMonsterCurrentHPWire(monster) == 0)
            {
                monster.AttackClientVisible = false;
                CombatRuntime.Instance.CancelMonsterPendingAttack(monster, "MON-ATTACK-client-writer-dead");
                Debug.LogError($"[MON-ATTACK-FLUSH-SKIP] client-writer-dead {monster.Name}#{monster.EntityId} behavior={update.BehaviorId} session={update.SessionId} hp={decision.HPWire} queuedAt={update.QueuedAt:F3} writer={writerTick}@{writerNow:F3}");
                return;
            }
            if ((decision.Flags & 0x02) != 0 && !TryPrimeMonsterHPBeforeSynch(targetConn, monster, decision.HPWire, "MON-ATTACK"))
            {
                string hpState = CombatRuntime.Instance.DescribeMonsterHPState(monster);
                Debug.LogError($"[MON-ATTACK] primer unresolved; continuing with client-writer HP {monster.Name}->{target.Name} behavior={update.BehaviorId} session={update.SessionId} hp={decision.HPWire} hpState={hpState}");
            }

            byte[] packet = CombatPackets.BuildMonsterAttackPacket(
                monster.EntityId,
                update.BehaviorId,
                (ushort)update.TargetEntityId,
                update.UseFlags,
                decision.ToResolved(monster.EntityId, update.BehaviorId, 0x04, writerNow, "MON-ATTACK client-writer"),
                update.UseTargetAction,
                false);
            targetConn.MessageQueue.Enqueue(packet);
            foreach (var recipient in GetMonsterInstanceRecipients(update.EntityId))
            {
                if (recipient == null || recipient.ConnId == targetConn.ConnId || !recipient.IsConnected) continue;
                ushort remoteAvatarTargetId = ResolveRemoteAvatarEntityId(recipient, targetConn, update.TargetEntityId);
                if (remoteAvatarTargetId == 0) continue;
                if ((decision.Flags & 0x02) != 0)
                    TryPrimeMonsterHPBeforeSynch(recipient, monster, decision.HPWire, "MON-ATTACK");
                byte[] recipientPacket = CombatPackets.BuildMonsterAttackPacket(
                    monster.EntityId,
                    update.BehaviorId,
                    remoteAvatarTargetId,
                    update.UseFlags,
                    decision.ToResolved(monster.EntityId, update.BehaviorId, 0x04, writerNow, "MON-ATTACK client-writer"),
                    update.UseTargetAction,
                    false);
                recipient.MessageQueue.Enqueue(recipientPacket);
            }
            if (update.UseTargetAction)
                CombatRuntime.Instance.CommitMonsterPrimarySkillUse(monster, "MON-ATTACK");
            monster.AttackClientVisible = true;
            monster.AttackClientVisibleTime = writerNow;
            LogPlayerHPVisibleEvent(targetConn, $"MonsterAttackStarted {monster.Name}#{monster.EntityId}");
            Debug.LogError($"[MON-ATTACK-FLUSH] client {(update.UseTargetAction ? "UseTarget" : "AttackTarget2")} {monster.Name}->{target.Name} behavior={update.BehaviorId} session={update.SessionId} flags={update.UseFlags} target={update.TargetEntityId} hp={decision.HPWire} queuedAt={update.QueuedAt:F3} writer={writerTick}@{writerNow:F3} queue={targetConn.MessageQueue.Count}");
        }

        private EntitySynchInfoDecision ResolveMonsterBehaviorWriterHPDecision(RRConnection conn, Monster monster, ushort componentId, byte subtype, EntitySynchInfoContext context, string packetName, float writerNow, uint writerTick)
        {
            EntitySynchInfoAuthority.Instance.RegisterMonster(monster);
            uint beforeHP = CombatRuntime.Instance.PeekMonsterCurrentHPWire(monster);

            string runtimeInstanceKey = !string.IsNullOrWhiteSpace(monster.InstanceKey) ? monster.InstanceKey : monster.ZoneName;
            CombatRuntime.EntitySynchInfoVisibilityCutoff hpCutoff = CombatRuntime.Instance.GetEntitySynchInfoValidationCutoff(context, $"{packetName} client-writer");
            FlushMonsterRuntimeBeforeSynch(conn, monster, context, packetName, hpCutoff.Time, writerNow, hpCutoff.Tick);
            int rngPos = CombatRuntime.Instance.GetRoomRngPosForInstance(runtimeInstanceKey);
            CombatRuntime.Instance.TryResolveMonsterEntitySynchInfoHP(monster, context, packetName, hpCutoff, out uint hpWire, out string hpReason);
            EntitySynchInfoAuthority.Instance.RecordMonsterOutboundHP(monster, hpWire, $"{packetName} {hpReason}");
            if (conn != null)
                conn.EntitySchedulerMirror.ObserveSuffixCutoff(runtimeInstanceKey, hpCutoff.Tick, hpCutoff.Time, hpCutoff.IncludeSubEntityEffects, hpCutoff.Phase, packetName);
            string provenance = $"{hpReason}; beforeHP={beforeHP}; writerTick={writerTick}; writerTime={writerNow:F3}; visibleCutoffTick={hpCutoff.Tick}; visibleCutoffTime={hpCutoff.Time:F3}; cutoffPhase={hpCutoff.Phase}; includeSubEntity={hpCutoff.IncludeSubEntityEffects}; cutoffReason={hpCutoff.Reason}; lastEntity={hpCutoff.LastEntityTick}@{hpCutoff.LastEntityTime:F3}; lastSubEntity={hpCutoff.LastSubEntityTick}@{hpCutoff.LastSubEntityTime:F3}";
            return EntitySynchInfoDecision.HP(EntitySynchInfoOwner.Monster, hpWire, $"{packetName} {hpReason}", monster.EntityId, componentId, subtype, writerNow, provenance, hpCutoff.Tick, hpCutoff.Time, runtimeInstanceKey, conn?.EntitySchedulerMirror.SchedulerTick ?? writerTick, hpCutoff.IncludeSubEntityEffects, rngPos, hpReason);
        }

        private void UpdateEncounterObjects()
        {
            if (_connections == null || _connections.Count == 0)
                return;

            Dictionary<string, List<RRConnection>> connsByInstance = null;
            foreach (var conn in _connections.Values)
            {
                if (conn == null || !conn.IsConnected || conn.Avatar == null)
                    continue;
                string instanceKey = conn.RuntimeInstanceKey;
                if (string.IsNullOrEmpty(instanceKey))
                    continue;
                if (!ZoneSpawner.Instance.HasEncounterObjects(instanceKey))
                    continue;
                connsByInstance ??= new Dictionary<string, List<RRConnection>>(StringComparer.OrdinalIgnoreCase);
                if (!connsByInstance.TryGetValue(instanceKey, out var conns))
                {
                    conns = new List<RRConnection>();
                    connsByInstance[instanceKey] = conns;
                }
                conns.Add(conn);
            }

            if (connsByInstance == null)
                return;

            foreach (var instanceEntry in connsByInstance)
            {
                var positions = new List<(float x, float y)>(instanceEntry.Value.Count);
                foreach (var instanceConnection in instanceEntry.Value)
                    positions.Add((instanceConnection.PlayerPosX, instanceConnection.PlayerPosY));

                var newly = ZoneSpawner.Instance.UpdateEncounterObjects(instanceEntry.Key, positions);
                if (newly == null || newly.Count == 0)
                    continue;

                foreach (var monster in newly)
                {
                    monster.AggroTriggered = false;
                    monster.State = MonsterState.Idle;
                    monster.TargetId = 0;
                    foreach (var instanceConnection in instanceEntry.Value)
                    {
                        if (!instanceConnection.IsSpawned || !instanceConnection.AllowFlush) continue;
                        SendMonsterToClient(instanceConnection, monster);
                    }
                }
                Debug.LogError($"[ENCOUNTER-OBJECT] replicated {newly.Count} lazily-generated monsters to {instanceEntry.Value.Count} conn(s) instance='{instanceEntry.Key}' sourceFunction=EncounterObject::update+SendMonsterToClient");
            }
        }

        private readonly Dictionary<string, uint> _encounterObjectIds = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, HashSet<uint>> _encounterObjectSentByConn = new Dictionary<int, HashSet<uint>>();
        private readonly Dictionary<int, HashSet<uint>> _monsterSpawnSentByConn = new Dictionary<int, HashSet<uint>>();

        private void ReassignMonsterOwnership(RRConnection conn)
        {
            var owned = _monsterOwnerConnId.Where(e => e.Value == conn.ConnId).Select(e => e.Key).ToList();
            foreach (uint entityId in owned)
            {
                Monster monster = CombatRuntime.Instance.GetMonster(entityId);
                RRConnection next = null;
                if (monster != null)
                {
                    foreach (var other in _connections.Values)
                    {
                        if (other == null || other == conn || !other.IsSpawned || !other.IsConnected) continue;
                        if (!string.Equals(other.RuntimeInstanceKey, monster.InstanceKey, StringComparison.OrdinalIgnoreCase)) continue;
                        next = other;
                        break;
                    }
                }
                if (next != null)
                    _monsterOwnerConnId[entityId] = next.ConnId;
                else
                    _monsterOwnerConnId.Remove(entityId);
            }
        }

        private void EnsureEncounterObject(RRConnection conn, Monster monster)
        {
            if (string.IsNullOrWhiteSpace(monster.EncounterGroupKey))
                return;
            string key = (monster.InstanceKey ?? string.Empty) + "|" + monster.EncounterGroupKey;
            if (!_encounterObjectIds.TryGetValue(key, out uint encId))
            {
                encId = CombatRuntime.Instance.AllocateComponentId();
                _encounterObjectIds[key] = encId;
            }
            monster.EncounterObjectEntityId = encId;
            if (!_encounterObjectSentByConn.TryGetValue(conn.ConnId, out var sent))
            {
                sent = new HashSet<uint>();
                _encounterObjectSentByConn[conn.ConnId] = sent;
            }
            if (sent.Add(encId))
            {
                byte[] encounterObjectPacket = CombatPackets.BuildEncounterObjectSpawnPacket(encId, monster.PosX, monster.PosY, monster.PosZ, monster.Heading);
                SendToClient(conn, encounterObjectPacket);
                Debug.LogError($"[ENCOUNTER-OBJECT-SPAWN] encId={encId} group='{monster.EncounterGroupKey}' instance='{monster.InstanceKey}' pos=({monster.PosX:F1},{monster.PosY:F1}) conn={conn.ConnId} sourceFunction=EncounterObject::writeInit@0x562B40");
            }
        }

        public void SendMonsterToClient(RRConnection conn, Monster monster)
        {
            if (!_monsterSpawnSentByConn.TryGetValue(conn.ConnId, out var sentMonsters))
            {
                sentMonsters = new HashSet<uint>();
                _monsterSpawnSentByConn[conn.ConnId] = sentMonsters;
            }
            if (!sentMonsters.Add(monster.EntityId))
            {
                Debug.LogError($"[SPAWN-DEDUP] entity={monster.EntityId} name='{monster.Name}' conn={conn.ConnId} state=already-sent");
                return;
            }
            ushort targetId = 0;
            EnsureEncounterObject(conn, monster);
            CombatRuntime.Instance.ResetMonsterClientVisiblePosition(monster, monster.PosX, monster.PosY, GetCombatNow(), "SPAWN-PKT");
            if (!ResolveEntitySynchInfoForComponent(conn, (ushort)monster.BehaviorId, 0x04, EntitySynchInfoContext.EntityInitPrimer, monster.EntityId, "SPAWN-PKT", false, out EntitySynchInfoDecision spawnDecision)
                || (spawnDecision.Flags & 0x02) == 0)
            {
                spawnDecision = ResolveMonsterRuntimeHPDecision(monster, "SPAWN-PKT", spawnDecision.Reason);
            }
            byte[] packet = CombatPackets.BuildMonsterSpawnPacket(
                monster,
                monster.BehaviorId,
                monster.SkillsId,
                monster.ManipulatorsId,
                monster.ModifiersId,
                targetId,
                targetId,
                spawnDecision.ToResolved(monster.EntityId, monster.BehaviorId, 0x04, GetCombatNow(), "SPAWN-PKT")
            );
            SendToClient(conn, packet);
            _monsterOwnerConnId[monster.EntityId] = conn.ConnId;
            CombatRuntime.Instance.RecordMonsterOutboundHP(monster, spawnDecision.HPWire, "SPAWN-PKT");
            _monsterEntityInitHPState[$"{conn.ConnId}\u001f{monster.EntityId}"] = spawnDecision.HPWire;

            Debug.LogError($"[SPAWN] Monster {monster.Name} spawned with RNG seed 0x{monster.RngSeed:X8}");

        }





        public void SendMonsterFollowClientUDP(RRConnection conn, ushort behaviorId, uint currentHPWire)
        {
            uint targetEntityId = conn.Avatar?.Id ?? 0;
        }

        public void SendMonsterFollowClientUDP(RRConnection conn, ushort behaviorId, uint currentHPWire, uint targetEntityId)
        {

            var session = GetUDPSessionForConnection(conn);
            if (session == null || !session.IsEstablished)
            {
                Debug.LogError($"[UDP-FOLLOW] No UDP session for monster behaviorId={behaviorId}");
                return;
            }

            if (!ResolveEntitySynchInfoForComponent(conn, behaviorId, 0x64, EntitySynchInfoContext.MonsterMove, 0, "UDP-FOLLOW", false, out EntitySynchInfoDecision decision))
                return;

            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x35);
            writer.WriteUInt16(behaviorId);
            writer.WriteByte(0x64);
            writer.WriteByte(0x01);
            if (!TryWriteResolvedEntitySynchInfo(writer, behaviorId, 0x64, EntitySynchInfoContext.MonsterMove, "UDP-FOLLOW", decision))
                return;
            writer.WriteByte(0x06);

            byte[] packet = writer.ToArray();

            int padLen = (8 - (packet.Length % 8)) % 8;
            if (padLen > 0)
            {
                byte[] padded = new byte[packet.Length + padLen];
                Array.Copy(packet, padded, packet.Length);
                packet = padded;
            }

            SendEncryptedUDP(session, packet);
            Debug.LogError($"[UDP-FOLLOW] Sent FollowClient behaviorId={behaviorId} target={targetEntityId} HP={currentHPWire}");
        }

        public void SendMonsterIdleActivateUDP(RRConnection conn, ushort behaviorId, uint currentHPWire)
        {
            var session = GetUDPSessionForConnection(conn);
            if (session == null || !session.IsEstablished)
            {
                Debug.LogError($"[UDP-IDLE] No UDP session for behaviorId={behaviorId}");
                return;
            }

            if (!ResolveEntitySynchInfoForComponent(conn, behaviorId, 0x64, EntitySynchInfoContext.MonsterAction, 0, "UDP-IDLE", false, out EntitySynchInfoDecision decision))
                return;

            uint hpWire = (decision.Flags & 0x02) != 0 ? decision.HPWire : currentHPWire;

            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x35);
            writer.WriteUInt16(behaviorId);
            writer.WriteByte(0x64);
            writer.WriteByte(0x01);
            writer.WriteUInt16(0x05);
            writer.WriteUInt16(0xFFFF);
            writer.WriteUInt32(hpWire);
            if (!TryWriteResolvedEntitySynchInfo(writer, behaviorId, 0x64, EntitySynchInfoContext.MonsterAction, "UDP-IDLE", decision))
                return;
            writer.WriteByte(0x06);

            byte[] packet = writer.ToArray();
            int padLen = (8 - (packet.Length % 8)) % 8;
            if (padLen > 0)
            {
                byte[] padded = new byte[packet.Length + padLen];
                Array.Copy(packet, padded, packet.Length);
                packet = padded;
            }

            SendEncryptedUDP(session, packet);
            Debug.LogError($"[UDP-IDLE] Sent IdleActivate behaviorId={behaviorId} HP={currentHPWire}");
        }

        public void SendMonsterCombatTickUDP(RRConnection conn, ushort behaviorComponentId, uint playerEntityId)
        {
            var session = GetUDPSessionForConnection(conn);
            if (session == null || !session.IsEstablished)
            {
                Debug.LogError($"[COMBAT-TICK] No UDP session for behaviorId={behaviorComponentId}");
                return;
            }

            if (!ResolveEntitySynchInfoForComponent(conn, behaviorComponentId, 0x64, EntitySynchInfoContext.MonsterAction, 0, "UDP-COMBAT-TICK", false, out EntitySynchInfoDecision decision))
                return;

            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x35);
            writer.WriteUInt16(behaviorComponentId);
            writer.WriteByte(0x64);
            writer.WriteByte(0x01);
            writer.WriteUInt16(0x000C);
            writer.WriteUInt16(0xFFFF);
            writer.WriteUInt32(0);
            writer.WriteUInt16((ushort)playerEntityId);
            if (!TryWriteResolvedEntitySynchInfo(writer, behaviorComponentId, 0x64, EntitySynchInfoContext.MonsterAction, "UDP-COMBAT-TICK", decision))
                return;
            writer.WriteByte(0x06);

            byte[] packet = writer.ToArray();
            int padLen = (8 - (packet.Length % 8)) % 8;
            if (padLen > 0)
            {
                byte[] padded = new byte[packet.Length + padLen];
                Array.Copy(packet, padded, packet.Length);
                packet = padded;
            }

            SendEncryptedUDP(session, packet);
            Debug.LogError($"[COMBAT-TICK] Sent Type 0x0C combatTick to behaviorId={behaviorComponentId} target={playerEntityId}");
        }
        private void HandleMonsterAggro(Monster monster, CombatPlayer player)
        {
            if (monster == null || player == null) return;
            QueuePendingMonsterBehaviorUpdate(new PendingMonsterBehaviorUpdate
            {
                Kind = PendingMonsterBehaviorKind.Follow,
                EntityId = monster.EntityId,
                BehaviorId = monster.BehaviorId,
                TargetEntityId = player.EntityId,
                QueuedAt = GetCombatNow()
            }, "MON-FOLLOW");
            Debug.LogError($"[SERVER-AGGRO] follow-broadcast {monster.Name}#{monster.EntityId} -> {player.Name}#{player.EntityId} behavior={monster.BehaviorId}");
        }
        void Start()
        {

            Debug.LogError("");
            Debug.LogError(" SERVER STARTING - LOADING DATABASE...");
            Debug.LogError("");

            RandomStreams.EnsureGlobalStaticSeededFromTime64("GameServer.Start");
            Database.GameDatabase.Initialize();
            ServerSettings.Load();

            Debug.LogError("[MEMBER] Using accounts.is_member column (1=member, 0=free)");
            DungeonRunners.Networking.DRLog.InitFromConfig();
            _udpPort = ServerSettings.Get("gamePort", 2603);

            string gcPath = DungeonRunners.Core.DataPaths.GcDir;
            bool sidecarLoaded = DungeonRunners.Data.SidecarCatalog.Instance.LoadFromAssets();
            DungeonRunners.Data.PackageCatalog.Instance.LoadFromAssets();
            DungeonRunners.Data.GCDatabase.Instance.Load(gcPath);
            if (!sidecarLoaded ||
                !DungeonRunners.Data.DescriptorCatalog.Instance.BuildFromSidecar(DungeonRunners.Data.SidecarCatalog.Instance))
                DungeonRunners.Data.DescriptorCatalog.Instance.BuildFromGCDatabase(DungeonRunners.Data.GCDatabase.Instance);
            DungeonRunners.Data.DescriptorCatalog.Instance.RunStartupCheck();
            WorldCollision.Instance.RunStartupCheck();

            DungeonRunners.Data.ItemStatDatabase.Instance.Load();
            DatabaseLoader.LoadAll();
            DungeonMazeSpawner.RunStartupManifestCheck();
            GCObjectGeneratorTable.Instance.Initialize();
            WorldEntitySpawner.Instance.Initialize();
            WorldEntitySpawner.Instance.SeedPvpArenaGates();

            Debug.LogError("[SERVER] Loading class configuration...");
            Debug.LogError("[SERVER] Loading class configuration...");
            ClassConfig.Load();
            Debug.LogError("[SERVER] Class configuration loaded");
            FallbackAudit.RunStartupCoverage();


            LoadZones();
            _unitContainer = new UnitContainer(this);
            _equipment = new Equipment(this);
            Debug.LogError(" Inventory and Equipment handlers initialized");
            CombatRuntime.Instance.OnMonsterSpawned += OnMonsterSpawned;
            CombatRuntime.Instance.OnMonsterDespawned += OnMonsterDespawned;
            CombatRuntime.Instance.OnMonsterAttackStarted += OnMonsterAttackStarted;
            CombatRuntime.Instance.OnMonsterAttackResolved += OnMonsterAttackResolved;
            CombatRuntime.Instance.OnPlayerDamageResolved += OnPlayerDamageResolved;
            CombatRuntime.Instance.OnPlayerStunActionResolved += OnPlayerStunActionResolved;
            CombatRuntime.Instance.OnPlayerModifierNetworkEvent += OnPlayerModifierNetworkEvent;
            CombatRuntime.Instance.OnMonsterPositionChanged += OnMonsterMoved;
            CombatRuntime.Instance.OnMonsterAggro += HandleMonsterAggro;
            DungeonRunners.Talkback.TalkbackServer.Instance.ResolveMemberFlag = userId =>
            {
                foreach (var memberConn in _connections.Values)
                {
                    if (memberConn == null || string.IsNullOrEmpty(memberConn.LoginName)) continue;
                    if (GetCharSqlId(memberConn) != (uint)userId) continue;
                    return !IsPlayerFree(memberConn.LoginName);
                }
                return true;
            };
            Debug.LogError($" Combat System initialized - {DatabaseLoader.Creatures.Count} creatures loaded");




            Debug.LogError("");
            Debug.LogError(" DATABASE LOADED - SERVER READY!");
            Debug.LogError("");
            InitializeTownNPCs();
            InitializeZonePortals();
            InitializeZoneCheckpoints();
            QuestManager.Instance.SetSendCallback(SendCompressedA);
            QuestManager.Instance.SetEntitySynchCallback(WritePlayerEntitySynch);
            StartServer();
        }

        void OnDestroy()
        {
            CombatRuntime.Instance.OnMonsterAttackStarted -= OnMonsterAttackStarted;
            CombatRuntime.Instance.OnMonsterAttackResolved -= OnMonsterAttackResolved;
            CombatRuntime.Instance.OnPlayerDamageResolved -= OnPlayerDamageResolved;
            CombatRuntime.Instance.OnPlayerStunActionResolved -= OnPlayerStunActionResolved;
            CombatRuntime.Instance.OnPlayerModifierNetworkEvent -= OnPlayerModifierNetworkEvent;
            CombatRuntime.Instance.OnMonsterPositionChanged -= OnMonsterMoved;
            CombatRuntime.Instance.OnMonsterAggro -= HandleMonsterAggro;
            StopServer();
        }
        private float _tickTimer = 0f;

        private HashSet<uint> _positionReportedMonsters = new HashSet<uint>();

        private void CheckWorldEntityProximity(RRConnection conn)
        {
            float now = DungeonRunners.Engine.Time.time;
            if (now - _lastWorldEntityCheckTime < 0.5f) return;
            _lastWorldEntityCheckTime = now;

            if (WorldEntitySpawner.Instance == null) return;
            float playerX = conn.PlayerPosX;
            float playerY = conn.PlayerPosY;
            const float RANGE = 40f;

            foreach (var spawnedEntityEntry in WorldEntitySpawner.Instance.GetSpawnedEntities())
            {
                ushort entityId = spawnedEntityEntry.Key;
                var data = spawnedEntityEntry.Value;
                if (_activatedWorldEntities.Contains(entityId)) continue;

                float dx = playerX - data.PosX;
                float dy = playerY - data.PosY;
                if (dx * dx + dy * dy > RANGE * RANGE) continue;

                if (data.IsChest)
                {
                    continue;
                }
                else if (data.IsTeleporter)
                {
                    _activatedWorldEntities.Add(entityId);
                    Debug.LogError($"[PROXIMITY] Teleporter {data.Label} (0x{entityId:X4})");
                    HandleTeleporterActivation(conn, 0, entityId, 0, conn.SessionID, data);
                }
                else if (data.IsShrine)
                {
                    _activatedWorldEntities.Add(entityId);
                    Debug.LogError($"[PROXIMITY] Shrine {data.Label} (0x{entityId:X4})");
                }
            }
        }


        private bool IsPortal(ushort entityId, out ZonePortal portal)
        {
            bool found = _portalEntities.TryGetValue(entityId, out portal);
            Debug.LogError($"[IS-PORTAL] entityId=0x{entityId:X4} found={found} count={_portalEntities.Count}");
            if (!found)
                Debug.LogError($"[IS-PORTAL] knownIds={string.Join(",", _portalEntities.Keys.Select(k => $"0x{k:X4}"))}");
            return found;
        }

        private bool IsCheckpoint(ushort entityId, out ZoneCheckpoint checkpoint)
        {
            return _checkpointEntities.TryGetValue(entityId, out checkpoint);
        }

        private void HandlePortalActivation(RRConnection conn, ushort componentId, ushort targetEntityId, byte responseId, byte sessionId, ZonePortal portal)
        {
            Debug.LogError($"[PORTAL] Activating portal to {portal.TargetZone} @ {portal.SpawnPoint}");
            conn.SessionID = sessionId;

            var portalActivationMessage = new LEWriter();
            portalActivationMessage.WriteByte(0x35);
            portalActivationMessage.WriteUInt16(componentId);
            portalActivationMessage.WriteByte(0x01);
            portalActivationMessage.WriteByte(responseId);
            portalActivationMessage.WriteByte(0x06);
            portalActivationMessage.WriteByte(sessionId);
            portalActivationMessage.WriteUInt16(targetEntityId);

            WritePlayerEntitySynch(conn, portalActivationMessage);
            conn.MessageQueue.Enqueue(portalActivationMessage.ToArray());

            Debug.LogError($"[PORTAL]  Queued portal activation response");

            if (portal.Name == "TownPortal")
            {
                SendDespawnEntity(conn, targetEntityId);
                _portalEntities.Remove(targetEntityId);
                if (portal.TargetZone.Equals(conn.TownPortalZoneName, StringComparison.OrdinalIgnoreCase))
                {
                    conn.HasSavedTownPortal = false;
                    ClearTownPortalFromDB(conn);
                    Debug.LogError($"[PORTAL] Return portal used, cleared saved state");
                }
                Debug.LogError($"[PORTAL] Despawned town portal 0x{targetEntityId:X4}");
            }

            if (!string.IsNullOrEmpty(portal.SpawnPoint))
            {
                var waypoints = DatabaseLoader.GetWaypointsForZone(portal.TargetZone);
                var waypoint = waypoints.FirstOrDefault(waypointData => waypointData.name.Equals(portal.SpawnPoint, StringComparison.OrdinalIgnoreCase));
                if (waypoint != null)
                {
                    conn.PendingSpawnX = (float)waypoint.posX;
                    conn.PendingSpawnY = (float)waypoint.posY;
                    conn.PendingSpawnZ = (float)waypoint.posZ;
                    Debug.LogError($"[PORTAL] Using waypoint '{portal.SpawnPoint}' coords: ({waypoint.posX}, {waypoint.posY}, {waypoint.posZ})");
                }
                else
                {
                    Debug.LogError($"[PORTAL]  Waypoint '{portal.SpawnPoint}' not found for zone '{portal.TargetZone}'");
                }
            }

            conn.ZonePortalSource = conn.CurrentZoneName;

            ChangeZone(conn, portal.TargetZone, portal.SpawnPoint);
        }

        private void HandleCheckpointActivation(RRConnection conn, ushort componentId, ushort targetEntityId, byte responseId, byte sessionId, ZoneCheckpoint checkpoint)
        {
            Debug.LogError($"[CHECKPOINT] Player activated checkpoint: {checkpoint.GCType}");
            conn.SessionID = sessionId;

            var checkpointActivationMessage = new LEWriter();
            checkpointActivationMessage.WriteByte(0x35);
            checkpointActivationMessage.WriteUInt16(componentId);
            checkpointActivationMessage.WriteByte(0x01);
            checkpointActivationMessage.WriteByte(responseId);
            checkpointActivationMessage.WriteByte(0x06);
            checkpointActivationMessage.WriteByte(sessionId);
            checkpointActivationMessage.WriteUInt16(targetEntityId);
            WritePlayerEntitySynch(conn, checkpointActivationMessage);
            conn.MessageQueue.Enqueue(checkpointActivationMessage.ToArray());

            string cpClass = checkpoint.GCType;
            if (cpClass.EndsWith("Entity", StringComparison.OrdinalIgnoreCase))
                cpClass = cpClass.Substring(0, cpClass.Length - 6);

            var knownCp = DatabaseLoader.Checkpoints.FirstOrDefault(c =>
                c.id.Equals(cpClass, StringComparison.OrdinalIgnoreCase));
            if (knownCp != null)
            {
                string connId = conn.ConnId.ToString();
                var playerState = QuestManager.Instance.GetPlayerState(connId);
                if (playerState != null && !playerState.UnlockedCheckpoints.Contains(cpClass))
                {
                    playerState.UnlockedCheckpoints.Add(cpClass);
                    Debug.LogError($"[CHECKPOINT]  Unlocked obelisk: {cpClass}  zone '{knownCp.zone}'");
                    SavePlayerQuests(conn);
                }
            }

            Debug.LogError($"[CHECKPOINT]  Queued activation response");
        }

        private bool IsChest(ushort entityId, out ChestSpawnData chest)
        {
            return _chestEntities.TryGetValue(entityId, out chest);
        }

        private void HandleChestActivation(RRConnection conn, ushort componentId,
            ushort targetEntityId, byte responseId, byte sessionId, ChestSpawnData chest)
        {
            Debug.LogError($"[CHEST] ");
            Debug.LogError($"[CHEST] Opening: {chest.Label} gc={chest.GCType}");
            conn.SessionID = sessionId;

            var chestActivationMessage = new LEWriter();
            chestActivationMessage.WriteByte(0x35);
            chestActivationMessage.WriteUInt16(componentId);
            chestActivationMessage.WriteByte(0x01);
            chestActivationMessage.WriteByte(responseId);
            chestActivationMessage.WriteByte(0x06);
            chestActivationMessage.WriteByte(sessionId);
            chestActivationMessage.WriteUInt16(targetEntityId);
            WritePlayerEntitySynch(conn, chestActivationMessage);
            conn.MessageQueue.Enqueue(chestActivationMessage.ToArray());

            var nonCombatInteractiveMessage = new LEWriter();
            nonCombatInteractiveMessage.WriteByte(0x03);
            nonCombatInteractiveMessage.WriteUInt16(targetEntityId);
            nonCombatInteractiveMessage.WriteByte(0x0A);
            nonCombatInteractiveMessage.WriteUInt32(0x00000001);
            WriteNonCombatInteractiveEntitySynchInfo(nonCombatInteractiveMessage, chest.GCType);
            conn.MessageQueue.Enqueue(nonCombatInteractiveMessage.ToArray());

            PlayerState playerState = GetPlayerState(conn.ConnId.ToString());
            int pLevel = playerState?.Level ?? 1;

            var drops = new List<LootDrop>();
            foreach (var (generator, count, slot) in chest.GetChestGenerators(null, 0))
            {
                var slotDrops = GCObjectGeneratorTable.Instance.GenerateChestLoot(generator, count, pLevel);
                drops.AddRange(slotDrops);
                Debug.LogError($"[CHEST] slot={slot} generator={generator} count={count} drops={slotDrops.Count} sourceFunction=NonCombatInteractiveDesc-ItemGenerator1-5");
            }

            UpgradePotionsForMembers(drops, conn);

            foreach (var drop in drops)
            {
                if (drop.IsGold)
                {
                    Debug.LogError($"[CHEST] +{drop.GoldAmount} gold");
                }
                else
                {
                    if (string.IsNullOrEmpty(drop.GCType)) { Debug.LogError("[CHEST]  Skipping null GCType"); continue; }
                    string _detectedChest = ResolveAuthoredItemClass(drop.GCType);

                    var item = new GCObject
                    {
                        GCClass = drop.GCType,
                        DFCClass = _detectedChest,
                        PresetScaleMod = drop.ScaleMod,
                        StoredRarity = (int)drop.Rarity,
                        StoredLevel = drop.ItemLevel
                    };

                    var chestPlacement = ResolveItemDropPlacement(
                        conn,
                        conn.CurrentZoneName,
                        conn.InstanceId,
                        chest.PosX,
                        chest.PosY,
                        chest.PosZ,
                        chest.Heading,
                        $"nci-chest:{chest.Label}:{drop.GCType}");
                    float dropX = chestPlacement.X;
                    float dropY = chestPlacement.Y;
                    float dropZ = chestPlacement.Z;

                    ushort lootId = GetNextLootEntityId();
                    TrackDroppedItem(lootId, item, conn, 1, dropX, dropY, dropZ, pLevel);

                    SendDroppedItemSpawnPacket(conn, lootId, _droppedItems[lootId]);
                    Debug.LogError($"[CHEST]  {drop.Label} ({drop.Rarity}) at ({dropX:F0},{dropY:F0},{dropZ:F0})");
                }
            }

            _chestEntities.Remove(targetEntityId);

            Debug.LogError($"[CHEST] {chest.Label}: {drops.Count} drops, chest opened");
            Debug.LogError($"[CHEST] ");
        }

        private void HandleCheckpointUse(RRConnection conn, ushort componentId, byte responseId, byte sessionID, LEReader reader)
        {
            Debug.LogError($"[CHECKPOINT-USE] ");
            Debug.LogError($"[CHECKPOINT-USE] Player selected checkpoint destination!");

            try
            {
                string checkpointGcType = reader.ReadCString();
                Debug.LogError($"[CHECKPOINT-USE] Target checkpoint gcType: '{checkpointGcType}'");

                var checkpointData = DatabaseLoader.GetCheckpointByGcType(checkpointGcType);

                if (checkpointData == null)
                {
                    Debug.LogError($"[CHECKPOINT-USE]  Checkpoint '{checkpointGcType}' not found in database!");
                    return;
                }

                Debug.LogError($"[CHECKPOINT-USE] Found checkpoint: zone='{checkpointData.zone}', pos=({checkpointData.posX}, {checkpointData.posY}, {checkpointData.posZ})");

                var checkpointUseMessage = new LEWriter();
                checkpointUseMessage.WriteByte(0x35);
                checkpointUseMessage.WriteUInt16(componentId);
                checkpointUseMessage.WriteByte(0x01);
                checkpointUseMessage.WriteByte(responseId);
                checkpointUseMessage.WriteByte(0x52);
                checkpointUseMessage.WriteByte(sessionID);
                checkpointUseMessage.WriteByte(0x00);
                WritePlayerEntitySynch(conn, checkpointUseMessage);
                conn.MessageQueue.Enqueue(checkpointUseMessage.ToArray());

                Debug.LogError($"[CHECKPOINT-USE]  Queued use response, teleporting to {checkpointData.zone}");

                ChangeZoneToPosition(conn, checkpointData.zone, checkpointData.posX, checkpointData.posY, checkpointData.posZ);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CHECKPOINT-USE] state=failed message='{ex.Message}' stack='{ex.StackTrace}'");
            }
        }

        private static bool ShouldUseFullHPZoneBaseline(string fromZone, string toZone)
        {
            if (!DungeonMazeSpawner.TryResolveExploredBitCount(toZone, out _))
                return false;
            if (DungeonMazeSpawner.TryResolveExploredBitCount(fromZone, out _))
                return false;
            return true;
        }

        private static string ResolveZoneGcType(Zone zone)
        {
            if (zone != null && !string.IsNullOrEmpty(zone.gcType))
                return zone.gcType;
            string zoneName = zone?.name ?? "";
            if (zoneName.IndexOf("tutorial", StringComparison.OrdinalIgnoreCase) >= 0)
                return "world.tutorial";
            if (zoneName.IndexOf("town", StringComparison.OrdinalIgnoreCase) >= 0)
                return "world.town";
            if (!string.IsNullOrEmpty(zoneName))
                return "world." + zoneName;
            return "world.tutorial";
        }

        private void ChangeZoneToPosition(RRConnection conn, string targetZone, float spawnX, float spawnY, float spawnZ)
        {
            if (conn.IsSpawned)
                BroadcastEntityRemove(conn, conn.CurrentZoneGcType);

            Debug.LogError($"[ZONE] ");
            Debug.LogError($"[ZONE] CHECKPOINT TELEPORT: {targetZone} @ ({spawnX}, {spawnY}, {spawnZ})");
            Debug.LogError($"[ZONE] ");
            BlingGnomeRuntime.Instance.SetServer(this);
            BlingGnomeRuntime.Instance.CleanupForZoneTransition(conn.ConnId);

            if (conn.TickCoroutine != null)
            {
                StopCoroutine(conn.TickCoroutine);
                conn.TickCoroutine = null;
                Debug.LogError("[ZONE]  Stopped tick coroutine");
            }

            conn.AllowFlush = false;
            conn.MessageQueue.Clear();

            var zone = _zones.Values.FirstOrDefault(z => z.name.Equals(targetZone, StringComparison.OrdinalIgnoreCase));
            if (zone == null)
            {
                Debug.LogError($"[ZONE] target='{targetZone}' state=notFound");
                conn.AllowFlush = true;
                return;
            }

            conn.FullHPBaselineOnNextSpawn = ShouldUseFullHPZoneBaseline(conn.CurrentZoneName, zone.name);
            if (conn.FullHPBaselineOnNextSpawn)
                Debug.LogError($"[ZONE-HP-BASELINE] full HP baseline queued: {conn.CurrentZoneName} -> {zone.name}");
            conn.CurrentZoneId = zone.id;
            conn.CurrentZoneName = zone.name;
            _monsterSpawnSentByConn.Remove(conn.ConnId);
            _encounterObjectSentByConn.Remove(conn.ConnId);
            CompleteGotoObjectivesOnZoneEntry(conn);
            GroupDirectory.Instance.UpdateMemberZone(conn.ConnId, zone.name);
            conn.CurrentZoneGcType = ResolveZoneGcType(zone);

            Debug.LogError($"[ZONE] CurrentZoneGcType set to: {conn.CurrentZoneGcType}");
            Debug.LogError($"[ZONE] CurrentZoneName set to: {conn.CurrentZoneName}");
            AssignInstanceId(conn);

            if (_selectedCharacter.TryGetValue(conn.LoginName, out var zoneChangeChar1))
            {
                SocialRuntime.Instance.NotifyFriendsZoneChange(conn.LoginName, zoneChangeChar1.Name, zone.name, SendSocialViaAuth);
                SocialRuntime.Instance.PushWhoListToAll(SendSocialViaAuth);
            }

            conn.PendingSpawnX = spawnX;
            conn.PendingSpawnY = spawnY;
            conn.PendingSpawnZ = spawnZ;

            ClearConnZoneEntities(conn);
            _activatedWorldEntities.Clear();
            _encounterObjectSentByConn.Remove(conn.ConnId);
            _monsterSpawnSentByConn.Remove(conn.ConnId);
            WorldEntitySpawner.Instance?.ClearZoneEntities();

            SavePlayerInventory(conn);
            SavePlayerQuests(conn);
            SavePlayerLevel(conn);
            var disconnectWriter = new LEWriter();
            disconnectWriter.WriteByte(0x0D);
            disconnectWriter.WriteByte(0x02);
            disconnectWriter.WriteCString("zoneleave");
            SendCompressedA(conn, 0x01, 0x0F, disconnectWriter.ToArray());
            Debug.LogError("[ZONE]  Sent DISCONNECT");

            var writer = new LEWriter();
            writer.WriteByte(0x0D);
            writer.WriteByte(0x00);
            writer.WriteCString(zone.name);
            uint zoneSeed = ResolveZoneConnectSeed(conn, zone.name);
            writer.WriteUInt32(zoneSeed);
            writer.WriteByte(0x01);
            writer.WriteByte(0xFF);
            writer.WriteCString("");
            writer.WriteUInt32(0x00);

            SendCompressedA(conn, 0x01, 0x0F, writer.ToArray());
            Debug.LogError($"[ZONE]  Sent CONNECT seed=0x{zoneSeed:X8} - waiting for client 0x06 response");
        }




        private void SendActivationResponse(RRConnection conn, ushort componentId, ushort targetEntityId, byte responseId, byte sessionId)
        {
            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x35);
            writer.WriteUInt16(componentId);
            writer.WriteByte(0x2E);

            writer.WriteByte(0x02);
            writer.WriteByte(0x06);
            writer.WriteByte(responseId);
            writer.WriteByte(sessionId);

            writer.WriteUInt16(targetEntityId);

            writer.WriteByte(0x06);

            SendCompressedA(conn, 0x01, 0x0f, writer.ToArray());
            Debug.LogError($"[ACTIVATE] Sent activation response");
        }

        public void ChatChangeZone(RRConnection conn, string zoneName)
        {
            Debug.LogError($"[CHAT-ZONE] @z command: {zoneName}");
            ChangeZone(conn, zoneName, "");
        }

        public void SpawnTownPortalWithRemoval(RRConnection conn, string targetZone,
      ushort componentId, uint itemSlotId, PlayerState playerState,
      string gcLower, byte itemX, byte itemY, int remainingCount)
        {
            ushort portalEntityId = (ushort)(0xFE00 + (_nextEntityId++ & 0xFF));

            float headingRad = conn.PlayerHeading * (float)System.Math.PI / 180.0f;
            float spawnX = conn.PlayerPosX + (float)System.Math.Sin(headingRad) * 10.0f;
            float spawnY = conn.PlayerPosY + (float)System.Math.Cos(headingRad) * 10.0f;
            Debug.LogError($"[TOWN-PORTAL] Player pos=({conn.PlayerPosX:F1}, {conn.PlayerPosY:F1}) heading={conn.PlayerHeading:F1}deg portal=({spawnX:F1}, {spawnY:F1})");
            int fx = (int)(spawnX * 256);
            int fy = (int)(spawnY * 256);
            int fz = (int)(conn.PlayerPosZ * 256);

            _portalEntities[portalEntityId] = new ZonePortal
            {
                Id = portalEntityId,
                GCType = "items.townportal.TownPortalBlue",
                Name = "TownPortal",
                PosX = spawnX,
                PosY = spawnY,
                PosZ = conn.PlayerPosZ,
                TargetZone = targetZone,
                SpawnPoint = "",
                Width = 3,
                Height = 3,
                Color = 0x0000FFFF
            };

            conn.HasSavedTownPortal = true;
            conn.TownPortalZoneName = conn.CurrentZoneName;
            conn.TownPortalZoneId = conn.CurrentZoneId;
            conn.TownPortalTargetZone = targetZone;
            conn.TownPortalPosX = spawnX;
            conn.TownPortalPosY = spawnY;
            conn.TownPortalPosZ = conn.PlayerPosZ;

            if (_selectedCharacter.ContainsKey(conn.LoginName))
            {
                var saveChar = CharacterRepository.GetCharacter(_selectedCharacter[conn.LoginName].Id);
                if (saveChar != null)
                {
                    saveChar.tpZone = conn.TownPortalZoneName;
                    saveChar.tpZoneId = (int)conn.TownPortalZoneId;
                    saveChar.tpTargetZone = conn.TownPortalTargetZone;
                    saveChar.tpPosX = conn.TownPortalPosX;
                    saveChar.tpPosY = conn.TownPortalPosY;
                    saveChar.tpPosZ = conn.TownPortalPosZ;
                    CharacterRepository.SaveCharacter(saveChar);
                    Debug.LogError($"[TOWN-PORTAL] Saved to DB");
                }
            }

            var removeWriter = new LEWriter();
            removeWriter.WriteByte(0x07);
            removeWriter.WriteByte(0x35);
            removeWriter.WriteUInt16(componentId);
            removeWriter.WriteByte(0x1F);
            removeWriter.WriteUInt32(itemSlotId);
            WritePlayerEntitySynch(conn, removeWriter);

            if (remainingCount > 0)
            {
                removeWriter.WriteByte(0x35);
                removeWriter.WriteUInt16(componentId);
                removeWriter.WriteByte(0x1E);
                removeWriter.WriteByte(0x0B);
                removeWriter.WriteByte(0xFF);
                removeWriter.WriteCString(GCObject.GetPacketGCClassFor(gcLower));
                removeWriter.WriteUInt32(itemSlotId);
                removeWriter.WriteByte(itemX);
                removeWriter.WriteByte(itemY);
                removeWriter.WriteByte((byte)remainingCount);
                removeWriter.WriteByte(0x01);
                removeWriter.WriteByte(0x00);
                removeWriter.WriteByte(0x00);
                WritePlayerEntitySynch(conn, removeWriter);
            }

            removeWriter.WriteByte(0x06);
            SendToClient(conn, removeWriter.ToArray());

            var questManagerWriter = new LEWriter();
            questManagerWriter.WriteByte(0x07);
            questManagerWriter.WriteByte(0x35);
            questManagerWriter.WriteUInt16(conn.QuestManagerId);
            questManagerWriter.WriteByte(0x0A);
            questManagerWriter.WriteByte(0x01);
            questManagerWriter.WriteUInt32(conn.CurrentZoneId);
            questManagerWriter.WriteCString(conn.CurrentZoneName);
            questManagerWriter.WriteCString("");
            if (!WritePlayerEntitySynch(conn, questManagerWriter)) return;
            questManagerWriter.WriteByte(0x06);
            SendToClient(conn, questManagerWriter.ToArray());

            var spawnWriter = new LEWriter();
            spawnWriter.WriteByte(0x07);
            spawnWriter.WriteByte(0x01);
            spawnWriter.WriteUInt16(portalEntityId);
            spawnWriter.WriteByte(0xFF);
            spawnWriter.WriteCString("items.townportal.TownPortalBlue");

            spawnWriter.WriteByte(0x02);
            spawnWriter.WriteUInt16(portalEntityId);
            spawnWriter.WriteUInt32(0x06);
            spawnWriter.WriteInt32(fx);
            spawnWriter.WriteInt32(fy);
            spawnWriter.WriteInt32(fz);
            spawnWriter.WriteInt32(0);
            spawnWriter.WriteByte(0x01);
            spawnWriter.WriteUInt16((ushort)(conn.Avatar?.Id ?? 0));

            spawnWriter.WriteCString(targetZone);
            spawnWriter.WriteCString("");
            spawnWriter.WriteByte(0x02);
            spawnWriter.WriteUInt32(0x00);
            spawnWriter.WriteUInt32(conn.CurrentZoneId);

            spawnWriter.WriteByte(0x06);

            Debug.LogError($"[TOWN-PORTAL] Spawning TownPortalBlue 0x{portalEntityId:X4} at ({spawnX:F1}, {spawnY:F1})  {targetZone} zoneGUID={conn.CurrentZoneId}");
            Debug.LogError($"[TOWN-PORTAL] Saved portal: zone={conn.TownPortalZoneName} pos=({spawnX:F1},{spawnY:F1})");
            SendCompressedA(conn, 0x01, 0x0f, spawnWriter.ToArray());

            var otherWriter = new LEWriter();
            otherWriter.WriteByte(0x07);
            otherWriter.WriteByte(0x01);
            otherWriter.WriteUInt16(portalEntityId);
            otherWriter.WriteByte(0xFF);
            otherWriter.WriteCString("items.townportal.TownPortalBlue");
            otherWriter.WriteByte(0x02);
            otherWriter.WriteUInt16(portalEntityId);
            otherWriter.WriteUInt32(0x04);
            otherWriter.WriteInt32(fx);
            otherWriter.WriteInt32(fy);
            otherWriter.WriteInt32(fz);
            otherWriter.WriteInt32(0);
            otherWriter.WriteByte(0x01);
            otherWriter.WriteUInt16((ushort)(conn.Avatar?.Id ?? 0));
            otherWriter.WriteCString(targetZone);
            otherWriter.WriteCString("");
            otherWriter.WriteByte(0x02);
            otherWriter.WriteUInt32(0x00);
            otherWriter.WriteUInt32(conn.CurrentZoneId);
            otherWriter.WriteByte(0x06);

            byte[] otherPortalPacket = otherWriter.ToArray();
            foreach (var other in _connections.Values)
            {
                if (other == conn) continue;
                if (!other.IsSpawned) continue;
                if (other.CurrentZoneGcType != conn.CurrentZoneGcType) continue;
                if (other.InstanceId != conn.InstanceId) continue;
                SendCompressedA(other, 0x01, 0x0f, otherPortalPacket);
            }
            Debug.LogError($"[TOWN-PORTAL] Broadcast portal to other players in zone");
        }

        private void SpawnReturnTownPortal(RRConnection conn)
        {
            if (!conn.HasSavedTownPortal) return;
            if (!conn.CurrentZoneName.Equals(conn.TownPortalZoneName, StringComparison.OrdinalIgnoreCase)) return;

            ushort portalEntityId = (ushort)(0xFE00 + (_nextEntityId++ & 0xFF));
            int fx = (int)(conn.TownPortalPosX * 256);
            int fy = (int)(conn.TownPortalPosY * 256);
            int fz = (int)(conn.TownPortalPosZ * 256);


            var spawnWriter = new LEWriter();
            spawnWriter.WriteByte(0x07);
            spawnWriter.WriteByte(0x01);
            spawnWriter.WriteUInt16(portalEntityId);
            spawnWriter.WriteByte(0xFF);
            spawnWriter.WriteCString("items.townportal.TownPortalBlue");
            spawnWriter.WriteByte(0x02);
            spawnWriter.WriteUInt16(portalEntityId);
            spawnWriter.WriteUInt32(0x04);
            spawnWriter.WriteInt32(fx);
            spawnWriter.WriteInt32(fy);
            spawnWriter.WriteInt32(fz);
            spawnWriter.WriteInt32(0);
            spawnWriter.WriteByte(0x01);
            spawnWriter.WriteUInt16((ushort)(conn.Avatar?.Id ?? 0));
            spawnWriter.WriteCString(conn.TownPortalTargetZone);
            spawnWriter.WriteCString("");
            spawnWriter.WriteByte(0x01);
            spawnWriter.WriteUInt32(0x00);
            spawnWriter.WriteUInt32(conn.TownPortalZoneId);
            spawnWriter.WriteByte(0x06);

            SendCompressedA(conn, 0x01, 0x0f, spawnWriter.ToArray());
            conn.HasSavedTownPortal = false;
            ClearTownPortalFromDB(conn);
            Debug.LogError($"[TOWN-PORTAL] Re-spawned return portal at ({conn.TownPortalPosX:F1}, {conn.TownPortalPosY:F1})");

            byte[] returnPortalPacket = spawnWriter.ToArray();
            foreach (var other in _connections.Values)
            {
                if (other == conn) continue;
                if (!other.IsSpawned) continue;
                if (other.CurrentZoneGcType != conn.CurrentZoneGcType) continue;
                if (other.InstanceId != conn.InstanceId) continue;
                SendCompressedA(other, 0x01, 0x0f, returnPortalPacket);
            }

            StartCoroutine(DespawnPortalAfterDelay(conn, portalEntityId, 5.0f));
        }

        private System.Collections.IEnumerator DespawnPortalAfterDelay(RRConnection conn, ushort portalEntityId, float delay)
        {
            yield return new DungeonRunners.Engine.WaitForSeconds(delay);
            if (conn == null || !conn.IsConnected) yield break;
            SendDespawnEntity(conn, portalEntityId);

            foreach (var other in _connections.Values)
            {
                if (other == conn) continue;
                if (!other.IsSpawned) continue;
                if (other.CurrentZoneGcType != conn.CurrentZoneGcType) continue;
                if (other.InstanceId != conn.InstanceId) continue;
                SendDespawnEntity(other, portalEntityId);
            }

            Debug.LogError($"[TOWN-PORTAL] Auto-despawned return portal 0x{portalEntityId:X4}");
        }

        private void ClearTownPortalFromDB(RRConnection conn)
        {
            if (conn.LoginName == null || !_selectedCharacter.ContainsKey(conn.LoginName)) return;
            var ch = CharacterRepository.GetCharacter(_selectedCharacter[conn.LoginName].Id);
            if (ch == null) return;
            ch.tpZone = "";
            ch.tpZoneId = 0;
            ch.tpTargetZone = "";
            ch.tpPosX = 0;
            ch.tpPosY = 0;
            ch.tpPosZ = 0;
            CharacterRepository.SaveCharacter(ch);
            Debug.LogError($"[TOWN-PORTAL] Cleared from DB");
        }


        private void ChangeZone(RRConnection conn, string targetZone, string spawnPoint, uint? forcedInstanceId = null)
        {
            BlingGnomeRuntime.Instance.SetServer(this);
            BlingGnomeRuntime.Instance.CleanupForZoneTransition(conn.ConnId);

            if (_adminShopNPCs.ContainsKey(conn.ConnId))
            {
                if (_adminShopNPCs.TryGetValue(conn.ConnId, out var shopNpcs))
                {
                    foreach (var npc in shopNpcs)
                    {
                        if (_zoneNPCs.TryGetValue(conn.CurrentZoneId, out var zoneList))
                            zoneList.Remove(npc);
                    }
                    _adminShopNPCs.Remove(conn.ConnId);
                }
            }

            if (conn.IsSpawned)
                BroadcastEntityRemove(conn, conn.CurrentZoneGcType);

            Debug.LogError($"[ZONE-TRACK] ");
            Debug.LogError($"[ZONE-TRACK] CHANGEZONE START");
            Debug.LogError($"[ZONE-TRACK] conn.UpdateNumber BEFORE: {conn.UpdateNumber}");
            Debug.LogError($"[ZONE-TRACK] Target zone: {targetZone}");
            Debug.LogError($"[ZONE-TRACK] ");
            Debug.LogError($"[ZONE] ");
            Debug.LogError($"[ZONE] ZONE TRANSITION: {targetZone} @ {spawnPoint}");
            Debug.LogError($"[ZONE] ");

            if (conn.TickCoroutine != null)
            {
                StopCoroutine(conn.TickCoroutine);
                conn.TickCoroutine = null;
                Debug.LogError("[ZONE]  Stopped tick coroutine");
            }

            conn.AllowFlush = false;

            conn.MessageQueue.Clear();
            Debug.LogError("[ZONE]  Cleared message queue");

            var zone = _zones.Values.FirstOrDefault(z => z.name.Equals(targetZone, StringComparison.OrdinalIgnoreCase));
            if (zone == null)
            {
                Debug.LogError($"[ZONE] target='{targetZone}' state=notFound");
                conn.AllowFlush = true;
                return;
            }

            conn.PendingSpawnPoint = spawnPoint ?? "";
            Debug.LogError($"[ZONE] PendingSpawnPoint set to '{conn.PendingSpawnPoint}' for target zone {zone.name}");
            conn.FullHPBaselineOnNextSpawn = ShouldUseFullHPZoneBaseline(conn.CurrentZoneName, zone.name);
            if (conn.FullHPBaselineOnNextSpawn)
                Debug.LogError($"[ZONE-HP-BASELINE] full HP baseline queued: {conn.CurrentZoneName} -> {zone.name}");
            conn.CurrentZoneId = zone.id;
            conn.CurrentZoneName = zone.name;
            _monsterSpawnSentByConn.Remove(conn.ConnId);
            _encounterObjectSentByConn.Remove(conn.ConnId);
            CompleteGotoObjectivesOnZoneEntry(conn);
            GroupDirectory.Instance.UpdateMemberZone(conn.ConnId, zone.name);
            conn.CurrentZoneGcType = ResolveZoneGcType(zone);

            Debug.LogError($"[ZONE] CurrentZoneGcType set to: {conn.CurrentZoneGcType}");
            Debug.LogError($"[ZONE] CurrentZoneName set to: {conn.CurrentZoneName}");
            if (forcedInstanceId.HasValue)
            {
                conn.InstanceId = forcedInstanceId.Value;
                StampRuntimeInstanceKey(conn, "pvp-match");
                Debug.LogError($"[INSTANCE] {conn.LoginName} -> PVP MATCH '{conn.CurrentZoneName}' (forced instance {conn.InstanceId:X8})");
            }
            else
            {
                AssignInstanceId(conn);
            }

            if (_selectedCharacter.TryGetValue(conn.LoginName, out var zoneChangeChar2))
            {
                SocialRuntime.Instance.NotifyFriendsZoneChange(conn.LoginName, zoneChangeChar2.Name, zone.name, SendSocialViaAuth);
                SocialRuntime.Instance.PushWhoListToAll(SendSocialViaAuth);
            }

            ClearConnZoneEntities(conn);
            _activatedWorldEntities.Clear();
            WorldEntitySpawner.Instance?.ClearZoneEntities();

            Debug.LogError("[ZONE]  Keeping conn.Avatar and conn.Player for entity reuse");

            SavePlayerInventory(conn);
            SavePlayerQuests(conn);
            SavePlayerLevel(conn);
            var disconnectWriter = new LEWriter();
            disconnectWriter.WriteByte(0x0D);
            disconnectWriter.WriteByte(0x02);
            disconnectWriter.WriteCString("zoneleave");
            SendCompressedA(conn, 0x01, 0x0F, disconnectWriter.ToArray());
            Debug.LogError("[ZONE]  Sent DISCONNECT");

            var writer = new LEWriter();
            writer.WriteByte(0x0D);
            writer.WriteByte(0x00);
            writer.WriteCString(zone.name);

            uint zoneSeed = ResolveZoneConnectSeed(conn, zone.name);
            writer.WriteUInt32(zoneSeed);
            Debug.LogError($"[ZONE] Sending seed: 0x{zoneSeed:X8} for zone {zone.name}");

            writer.WriteByte(0x01);
            writer.WriteByte(0xFF);
            writer.WriteCString("");
            writer.WriteUInt32(0x00);

            SendCompressedA(conn, 0x01, 0x0F, writer.ToArray());
            Debug.LogError($"[ZONE]  Sent CONNECT seed=0x{zoneSeed:X8} - waiting for client 0x06 response");

        }





        [System.Serializable]
        public class ZoneNPC
        {
            public uint Id;
            public uint UnitBehaviorId;
            public string GCClass;
            public string Name;
            public float PosX;
            public float PosY;
            public float PosZ;
            public float Heading;
            public bool IsMerchant;
            public uint MerchantId;
            public bool IsAdminMerchant;
            public bool IsTrainer;
            public uint TrainerId;
            public bool IsBank;
            public uint BankComponentId;
            public bool IsPosseMagnate;
            public uint PosseOptionComponentId;
            public List<string> TrainerSkills;
        }

        private static List<string> GetTrainerSkillList(string npcGcClass)
        {
            if (npcGcClass.IndexOf("TrainerFighter", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new List<string>
                {
                    "skills.generic.1HMeleeSpeedBuff",
                    "skills.generic.2HMeleeSpeedBuff",
                    "skills.generic.CleaveUpgradeProcPassive",
                    "skills.generic.FearResistModPassive",
                    "skills.generic.Butcher",
                    "skills.generic.Cleave",
                    "skills.generic.ShadowRage",
                    "skills.generic.DivineDamageBuff",
                    "skills.generic.DivineResistBuff",
                    "skills.generic.DivineResistPassive",
                    "skills.generic.FearMeleeAttack",
                    "skills.generic.FighterClassPassive",
                    "skills.generic.HealSelf",
                    "skills.generic.Charge",
                    "skills.generic.BlockKnockdownProcPassive",
                    "skills.generic.MinMoveSpeedBuff",
                    "skills.generic.MeleeAttackRatingModPassive",
                    "skills.generic.FireMeleeSummon",
                    "skills.generic.AggroIncreaseModBuff",
                    "skills.generic.MeleeDamageReflectionBuff",
                    "skills.generic.Stomp",
                    "skills.generic.DivineMeleeAttack",
                    "skills.generic.StunResistBuff",
                    "skills.generic.SlowDeBuff",
                    "skills.generic.MeleeAttackSpeedModPassive",
                };
            }
            else if (npcGcClass.IndexOf("TrainerMage", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new List<string>
                {
                    "skills.generic.ManaShield",
                    "skills.generic.MageClassPassive",
                    "skills.Generic.SummonSnowman",
                    "skills.generic.IceDamageBuff",
                    "skills.generic.IceTargetedBurst",
                    "skills.generic.SummonerClassPassive",
                    "skills.generic.FireCone",
                    "skills.generic.ShadowDamageBuff",
                    "skills.generic.FireBolt",
                    "skills.generic.FireResistBuff",
                    "skills.generic.FireResistPassive",
                    "skills.generic.DivineIntervention",
                    "skills.generic.IceBolt",
                    "skills.generic.IceResistBuff",
                    "skills.generic.IceMultiBolt",
                    "skills.generic.IceResistPassive",
                    "skills.generic.ShadowLightningUpgradeProcPassive",
                    "skills.generic.MagicDamageModPassive",
                    "skills.generic.SnowManIceDamageProcAuraModBuff",
                    "skills.generic.IceTargetedBurstUpgradeProcPassive",
                    "skills.generic.SnowmanFreezeAura",
                    "skills.generic.FireDamageBuff",
                    "skills.generic.DivineRay",
                    "skills.generic.FireRing",
                    "skills.generic.ShadowBolt",
                    "skills.generic.ShadowLightning",
                    "skills.generic.ShadowResistBuff",
                    "skills.generic.ShadowResistPassive",
                    "skills.generic.ShadowLightningKnockdown",
                    "skills.generic.ShadowTendrils",
                    "skills.generic.SnowmanHealthModAuraBuff",
                    "skills.generic.ManaSelf",
                    "skills.generic.Teleport",
                };
            }
            else if (npcGcClass.IndexOf("TrainerRanger", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new List<string>
                {
                    "skills.generic.Blight",
                    "skills.generic.InfectiousPoisonUpgradeProcPassive",
                    "skills.generic.RangerClassPassive",
                    "skills.generic.FireCurseShot",
                    "skills.generic.MonsterBaitHealthModPassive",
                    "skills.generic.FireShot",
                    "skills.generic.FearShot",
                    "skills.generic.PoisonBlastRadius",
                    "skills.generic.PoisonDamageBuff",
                    "skills.generic.SummonMonsterBait",
                    "skills.generic.PoisonTrail",
                    "skills.generic.NoxiousShot",
                    "skills.generic.PoisonResistBuff",
                    "skills.generic.PoisonResistPassive",
                    "skills.generic.PoisonShot",
                    "skills.generic.PenetrateKnockdownShot",
                    "skills.generic.RangedSpeedBuff",
                    "skills.generic.IceShot",
                    "skills.generic.Sprint",
                    "skills.generic.RangeAttackSpeedModPassive",
                };
            }
            return new List<string>();
        }


        private void InitializeTownNPCs()
        {
            Debug.LogError("");
            Debug.LogError("[INIT-TOWN-NPCS] phase=start zone=town");
            Debug.LogError("");

            var townZone = _zones.Values.FirstOrDefault(z => z.name.ToLower() == "town");
            if (townZone == null)
            {
                Debug.LogError("[INIT-TOWN-NPCS] zone=town state=missing");
                return;
            }

            uint zoneId = townZone.id;
            _zoneNPCs[zoneId] = new List<ZoneNPC>();

            if (DatabaseLoader.TownNPCs == null || DatabaseLoader.TownNPCs.Count == 0)
            {
                Debug.LogError("[INIT-TOWN-NPCS] zone=town state=empty");
                return;
            }

            foreach (var npcData in DatabaseLoader.TownNPCs)
            {
                bool isMerchant = MerchantRuntime.IsMerchant(npcData.gcType);
                bool isTrainer = npcData.gcType.IndexOf("Trainer", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isBank = npcData.gcType.EndsWith(".Bank", StringComparison.OrdinalIgnoreCase);
                bool isPosseMagnate = npcData.gcType.EndsWith(".PosseMagnate", StringComparison.OrdinalIgnoreCase);
                var npc = new ZoneNPC
                {
                    GCClass = npcData.gcType,
                    Name = npcData.name,
                    PosX = npcData.posX,
                    PosY = npcData.posY,
                    PosZ = npcData.posZ,
                    Heading = npcData.heading,
                    Id = _nextEntityId++,
                    UnitBehaviorId = _nextEntityId++,
                    IsMerchant = isMerchant,
                    MerchantId = isMerchant ? _nextEntityId++ : 0,
                    IsTrainer = isTrainer,
                    TrainerId = isTrainer ? _nextEntityId++ : 0,
                    TrainerSkills = isTrainer ? GetTrainerSkillList(npcData.gcType) : null,
                    IsBank = isBank,
                    BankComponentId = isBank ? _nextEntityId++ : 0,
                    IsPosseMagnate = isPosseMagnate,
                    PosseOptionComponentId = isPosseMagnate ? _nextEntityId++ : 0
                };

                _zoneNPCs[zoneId].Add(npc);
                string tags = (isMerchant ? " merchant=true" : "") + (isTrainer ? $" trainerId={npc.TrainerId}" : "") + (isBank ? $" bankId={npc.BankComponentId}" : "") + (isPosseMagnate ? $" posseId={npc.PosseOptionComponentId}" : "");
                Debug.LogError($"[INIT-TOWN-NPCS] create name='{npc.Name}' id={npc.Id}{tags}");
            }
            var tutorialZone = _zones.Values.FirstOrDefault(z => z.name.ToLower() == "tutorial");
            if (tutorialZone != null)
            {
                uint tutorialZoneId = tutorialZone.id;
                _zoneNPCs[tutorialZoneId] = new List<ZoneNPC>();

                if (DatabaseLoader.TutorialNPCs != null && DatabaseLoader.TutorialNPCs.Count > 0)
                {
                    foreach (var npcData in DatabaseLoader.TutorialNPCs)
                    {
                        bool isMerchant = MerchantRuntime.IsMerchant(npcData.gcType);
                        bool isTrainer = npcData.gcType.IndexOf("Trainer", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool isBank = npcData.gcType.EndsWith(".Bank", StringComparison.OrdinalIgnoreCase);
                        bool isPosseMagnate = npcData.gcType.EndsWith(".PosseMagnate", StringComparison.OrdinalIgnoreCase);
                        var npc = new ZoneNPC
                        {
                            GCClass = npcData.gcType,
                            Name = npcData.name,
                            PosX = npcData.posX,
                            PosY = npcData.posY,
                            PosZ = npcData.posZ,
                            Heading = npcData.heading,
                            Id = _nextEntityId++,
                            UnitBehaviorId = _nextEntityId++,
                            IsMerchant = isMerchant,
                            MerchantId = isMerchant ? _nextEntityId++ : 0,
                            IsTrainer = isTrainer,
                            TrainerId = isTrainer ? _nextEntityId++ : 0,
                            TrainerSkills = isTrainer ? GetTrainerSkillList(npcData.gcType) : null,
                            IsBank = isBank,
                            BankComponentId = isBank ? _nextEntityId++ : 0,
                            IsPosseMagnate = isPosseMagnate,
                            PosseOptionComponentId = isPosseMagnate ? _nextEntityId++ : 0
                        };

                        _zoneNPCs[tutorialZoneId].Add(npc);
                        string tags = (isMerchant ? " merchant=true" : "") + (isTrainer ? $" trainerId={npc.TrainerId}" : "") + (isBank ? $" bankId={npc.BankComponentId}" : "") + (isPosseMagnate ? $" posseId={npc.PosseOptionComponentId}" : "");
                        Debug.LogError($"[INIT-TUTORIAL-NPCS] create name='{npc.Name}' id={npc.Id}{tags}");
                    }
                }


                Debug.LogError($"[INIT-TOWN-NPCS] zone=town count={_zoneNPCs[zoneId].Count}");
                Debug.LogError("");
            }

            var pvpZone = _zones.Values.FirstOrDefault(z => z.name.Equals("pvp_start", StringComparison.OrdinalIgnoreCase));
            if (pvpZone != null && DatabaseLoader.PvpNPCs != null && DatabaseLoader.PvpNPCs.Count > 0)
            {
                uint pvpZoneId = pvpZone.id;
                _zoneNPCs[pvpZoneId] = new List<ZoneNPC>();

                foreach (var npcData in DatabaseLoader.PvpNPCs)
                {
                    bool isMerchant = MerchantRuntime.IsMerchant(npcData.gcType);
                    bool isTrainer = npcData.gcType.IndexOf("Trainer", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isBank = npcData.gcType.EndsWith(".Bank", StringComparison.OrdinalIgnoreCase);
                    bool isPosseMagnate = npcData.gcType.EndsWith(".PosseMagnate", StringComparison.OrdinalIgnoreCase);
                    bool isPvpNpc = npcData.gcType.IndexOf("L33tenant", StringComparison.OrdinalIgnoreCase) >= 0
                                 || npcData.name.IndexOf("L33tenant", StringComparison.OrdinalIgnoreCase) >= 0;
                    var npc = new ZoneNPC
                    {
                        GCClass = npcData.gcType,
                        Name = npcData.name,
                        PosX = npcData.posX,
                        PosY = npcData.posY,
                        PosZ = npcData.posZ,
                        Heading = npcData.heading,
                        Id = _nextEntityId++,
                        UnitBehaviorId = _nextEntityId++,
                        IsMerchant = isMerchant,
                        MerchantId = isMerchant ? _nextEntityId++ : 0,
                        IsTrainer = isTrainer,
                        TrainerId = isTrainer ? _nextEntityId++ : 0,
                        TrainerSkills = isTrainer ? GetTrainerSkillList(npcData.gcType) : null,
                        IsBank = isBank,
                        BankComponentId = isBank ? _nextEntityId++ : 0,
                        IsPosseMagnate = isPosseMagnate,
                        PosseOptionComponentId = isPosseMagnate ? _nextEntityId++ : 0
                    };

                    _zoneNPCs[pvpZoneId].Add(npc);
                    Debug.LogError($"[INIT-PVP-NPCS] create name='{npc.Name}' id={npc.Id} pvpQueue={isPvpNpc}");
                }
                Debug.LogError($"[INIT-PVP-NPCS] zone=pvp_start count={_zoneNPCs[pvpZoneId].Count}");
            }
        }

        private void SendZoneNPCs(RRConnection conn, uint zoneId)
        {
            if (VerbosePacketLogging) Debug.LogError("");
            if (VerbosePacketLogging) Debug.LogError($"[SEND-ZONE-NPCS] phase=start zone={zoneId}");
            if (VerbosePacketLogging) Debug.LogError("");

            if (!_zoneNPCs.TryGetValue(zoneId, out var npcs) || npcs.Count == 0)
            {
                Debug.LogError($"[SEND-ZONE-NPCS] zone={zoneId} state=empty");
                return;
            }

            if (VerbosePacketLogging) Debug.LogError($"[SEND-ZONE-NPCS] zone={zoneId} count={npcs.Count}");
            if (VerbosePacketLogging) Debug.LogError($"[SEND-ZONE-NPCS] batch=single");

            var writer = new LEWriter();
            writer.WriteByte(0x07);
            if (VerbosePacketLogging) Debug.LogError($"[SEND-ZONE-NPCS] stream=begin opcode=0x07");

            int npcCounter = 0;
            foreach (var npc in npcs)
            {
                int npcStartPos = writer.Position;
                npcCounter++;
                if (VerbosePacketLogging) Debug.LogError("");
                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}] ");
                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}]  PROCESSING NPC #{npcCounter} OF {npcs.Count}");
                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}] ");
                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}]  Name:     {npc.Name}");
                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}]  GCClass:  {npc.GCClass}");
                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}]  Position: ({npc.PosX:F2}, {npc.PosY:F2}, {npc.PosZ:F2})");
                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}]  Heading:  {npc.Heading:F2}");
                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}] ");

                ushort npcId = (ushort)npc.Id;
                ushort behaviorId = (ushort)npc.UnitBehaviorId;
                _npcPositions[npcId] = (npc.PosX, npc.PosY, npc.PosZ);
                _allEntityPositions[npcId] = (npc.PosX, npc.PosY, npc.PosZ);
                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}]  Tracking position for entityId=0x{npcId:X4}");
                ushort skillsId = (ushort)_nextEntityId++;
                ushort manipulatorsId = (ushort)_nextEntityId++;
                ushort modifiersId = (ushort)_nextEntityId++;

                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}] ids state=start");
                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}] npcId=0x{npcId:X4} value={npcId}");
                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}] behaviorId=0x{behaviorId:X4} value={behaviorId}");
                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}] skillsId=0x{skillsId:X4} value={skillsId}");
                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}] manipulatorsId=0x{manipulatorsId:X4} value={manipulatorsId}");
                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}] modifiersId=0x{modifiersId:X4} value={modifiersId}");
                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}] ");

                string behaviorGCType = "npc.base.behavior";

                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}] gcTypes state=start");
                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}] entityGcType='{npc.GCClass}' preserveCase=true");
                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}] behaviorGcType='{behaviorGCType}' preserveCase=false");
                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}] ");

                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}] op=1 action=create-npc-entity opcode=0x01");
                writer.WriteByte(0x01);
                writer.WriteUInt16(npcId);
                string entityGcType = npc.GCClass;
                if (entityGcType.Contains("AdminWeaponVendor")) entityGcType = "world.town.npc.VendorWeapon1";
                else if (entityGcType.Contains("AdminArmorVendor")) entityGcType = "world.town.npc.VendorWeapon2";
                else if (entityGcType.Contains("AdminMiscVendor")) entityGcType = "world.town.npc.VendorWeapon3";
                uint npcHPWire = ResolveAuthoredUnitMaxHealthWire(entityGcType);
                WriteGCType(writer, entityGcType, preserveCase: true);

                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}]  OPERATION 2: CREATE BEHAVIOR COMPONENT (0x32)");
                writer.WriteByte(0x32);
                writer.WriteUInt16(npcId);
                writer.WriteUInt16(behaviorId);
                WriteGCType(writer, behaviorGCType, preserveCase: false);
                writer.WriteByte(0x01);

                writer.WriteByte(0xFF);
                writer.WriteByte(0x00);
                writer.WriteByte(0x00);
                writer.WriteByte(0x01);

                writer.WriteByte(0x85);
                writer.WriteByte(0x00);
                writer.WriteUInt32(0x00000000);
                writer.WriteUInt32(0x00000000);
                writer.WriteUInt32(0x00000000);
                writer.WriteUInt32(0x00000000);
                writer.WriteUInt32(0x00000000);

                writer.WriteByte(0x00);

                writer.WriteByte(0xFF);
                writer.WriteByte(0x00);
                writer.WriteByte(0x00);

                writer.WriteByte(0x00);
                writer.WriteByte(0x00);
                writer.WriteUInt32(0x00000000);
                writer.WriteUInt32(0x00000000);

                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}]  OPERATION 3: CREATE SKILLS COMPONENT (0x32)");
                writer.WriteByte(0x32);
                writer.WriteUInt16(npcId);
                writer.WriteUInt16(skillsId);
                WriteGCType(writer, "skills", preserveCase: false);
                writer.WriteByte(0x01);
                writer.WriteByte(0xFF);
                writer.WriteByte(0xFF);
                writer.WriteByte(0xFF);
                writer.WriteByte(0xFF);
                writer.WriteByte(0x00);
                writer.WriteByte(0x01);
                WriteGCType(writer, "skills.professions.Warrior", preserveCase: true);

                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}]  OPERATION 4: CREATE MANIPULATORS COMPONENT (0x32)");
                writer.WriteByte(0x32);
                writer.WriteUInt16(npcId);
                writer.WriteUInt16(manipulatorsId);
                WriteGCType(writer, "manipulators", preserveCase: false);
                writer.WriteByte(0x01);
                writer.WriteByte(0x00);

                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}]  OPERATION 5: CREATE MODIFIERS COMPONENT (0x32)");
                writer.WriteByte(0x32);
                writer.WriteUInt16(npcId);
                writer.WriteUInt16(modifiersId);
                WriteGCType(writer, "modifiers", preserveCase: false);
                writer.WriteByte(0x01);
                writer.WriteUInt32(0x00000000);
                writer.WriteByte(0x00);
                writer.WriteUInt32(0x00000000);

                if (npc.IsMerchant)
                {
                    int playerLevel = GetPlayerState(conn.ConnId.ToString()).Level;
                    MerchantRuntime.EnsureInventoryForLevel(npc.GCClass, playerLevel);

                    int merchantStart = writer.Position;
                    if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}]  CREATING MERCHANT COMPONENT (startPos={merchantStart})");
                    MerchantRuntime.WriteMerchantComponent(writer, npc.GCClass, npcId, (ushort)npc.MerchantId);
                    if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}]  MERCHANT DONE (endPos={writer.Position}, bytes={writer.Position - merchantStart})");
                }

                if (npc.IsTrainer)
                {
                    if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}]  CREATING SKILLTRAINER COMPONENT (trainerId={npc.TrainerId})");
                    writer.WriteByte(0x32);
                    writer.WriteUInt16(npcId);
                    writer.WriteUInt16((ushort)npc.TrainerId);
                    int lastDot = npc.GCClass.LastIndexOf('.');
                    string gcPrefix = npc.GCClass.Substring(0, lastDot);
                    string npcName = npc.GCClass.Substring(lastDot + 1);
                    string skillTrainerGcType = gcPrefix + ".base." + npcName + "Base.SkillTrainer";
                    if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}]  SkillTrainer GCType: {skillTrainerGcType}");
                    WriteGCType(writer, skillTrainerGcType, preserveCase: true);
                    writer.WriteByte(0x00);
                    if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}]  SKILLTRAINER DONE (trainerId=0x{npc.TrainerId:X4})");
                }

                if (npc.IsBank)
                {
                    Debug.LogError($"[NPC-{npcCounter}]  CREATING BANK COMPONENT (bankId={npc.BankComponentId})");
                    writer.WriteByte(0x32);
                    writer.WriteUInt16(npcId);
                    writer.WriteUInt16((ushort)npc.BankComponentId);
                    WriteGCType(writer, "banker", preserveCase: false);
                    writer.WriteByte(0x00);
                    Debug.LogError($"[NPC-{npcCounter}]  BANK DONE (bankId=0x{npc.BankComponentId:X4})");
                }

                if (npc.IsPosseMagnate)
                {
                    Debug.LogError($"[POSSE] CREATING POSSE COMPONENT (cid={npc.PosseOptionComponentId}) for {npc.GCClass}");
                    writer.WriteByte(0x32);
                    writer.WriteUInt16(npcId);
                    writer.WriteUInt16((ushort)npc.PosseOptionComponentId);
                    WriteGCType(writer, "PosseRegistry", preserveCase: false);
                    writer.WriteByte(0x00);
                    Debug.LogError($"[POSSE] POSSE COMPONENT DONE (cid=0x{npc.PosseOptionComponentId:X4})");
                }

                var pvpAccess = GetPvpAccessPoint(npc.GCClass);
                if (pvpAccess != null)
                {
                    ushort pvpComponentId = (ushort)_nextEntityId++;
                    Debug.LogError($"[PVP-ACCESS] CREATING PVPACCESSPOINT COMPONENT (cid=0x{pvpComponentId:X4}) gc='{pvpAccess.Value.subPath}' match='{pvpAccess.Value.matchType}' for {npc.Name}");
                    writer.WriteByte(0x32);
                    writer.WriteUInt16(npcId);
                    writer.WriteUInt16(pvpComponentId);
                    WriteGCType(writer, pvpAccess.Value.subPath, preserveCase: true);
                    writer.WriteByte(0x00);
                }





                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}]  OPERATION 6: INIT NPC ENTITY (0x02)");
                writer.WriteByte(0x02);
                writer.WriteUInt16(npcId);
                writer.WriteUInt32(0x06);

                int posX = (int)(npc.PosX * 256);
                int posY = (int)(npc.PosY * 256);
                int posZ = (int)(npc.PosZ * 256);
                int heading = (int)(npc.Heading * 256);

                writer.WriteInt32(posX);
                writer.WriteInt32(posY);
                writer.WriteInt32(posZ);
                writer.WriteInt32(heading);
                writer.WriteByte(0x00);
                writer.WriteByte(0x01);

                for (int zeroIndex = 0; zeroIndex < 8; zeroIndex++)
                    writer.WriteUInt32(0x00000000);

                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}]  OPERATION 7: WARP TO POSITION (0x35)");
                writer.WriteByte(0x35);
                writer.WriteUInt16(behaviorId);
                writer.WriteByte(0x04);
                writer.WriteByte(0x11);
                writer.WriteByte(0x00);

                writer.WriteInt32(posX);
                writer.WriteInt32(posY);
                writer.WriteInt32(posZ);

                writer.WriteByte(0x02);
                writer.WriteUInt32(npcHPWire);
                if (VerbosePacketLogging) Debug.LogError($"[NPC-{npcCounter}]  NPC OPERATIONS WRITTEN TO BATCH (startPos={npcStartPos}, endPos={writer.Position}, bytes={writer.Position - npcStartPos}, isMerchant={npc.IsMerchant}, isTrainer={npc.IsTrainer})");
            }

            writer.WriteByte(0x06);
            if (VerbosePacketLogging) Debug.LogError($"[SEND-ZONE-NPCS] stream=end opcode=0x06");

            byte[] packetData = writer.ToArray();
            int totalPacketSize = packetData.Length;
            if (VerbosePacketLogging) Debug.LogError($"[SEND-ZONE-NPCS] batchBytes={totalPacketSize} npcs={npcs.Count}");
            if (VerbosePacketLogging) Debug.LogError($"[SEND-ZONE-NPCS] hexFirst200={BitConverter.ToString(packetData, 0, Math.Min(200, packetData.Length))}");

            if (VerbosePacketLogging) Debug.LogError("[SEND-ZONE-NPCS] sendPhase=begin");
            SendCompressedA(conn, 0x01, 0x0f, packetData);
            if (VerbosePacketLogging) Debug.LogError("[SEND-ZONE-NPCS] sendPhase=complete");

            if (VerbosePacketLogging) Debug.LogError("");
            if (VerbosePacketLogging) Debug.LogError("");
            if (VerbosePacketLogging) Debug.LogError($"[SEND-ZONE-NPCS] sent={npcs.Count} batch=single");
            if (VerbosePacketLogging) Debug.LogError("");
        }

        private readonly Dictionary<string, (string subPath, string matchType)?> _pvpAccessPointCache =
            new Dictionary<string, (string subPath, string matchType)?>(StringComparer.OrdinalIgnoreCase);

        private (string subPath, string matchType)? GetPvpAccessPoint(string gcType)
        {
            if (string.IsNullOrEmpty(gcType)) return null;
            if (_pvpAccessPointCache.TryGetValue(gcType, out var cached)) return cached;

            (string subPath, string matchType)? result = null;
            try
            {
                var node = GCDatabase.Instance?.ResolveWithInheritance(gcType);
                if (node != null)
                {
                    foreach (var kv in node.Children)
                    {
                        var child = kv.Value;
                        if (child != null && !string.IsNullOrEmpty(child.Extends)
                            && child.Extends.IndexOf("PVPAccessPoint", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            result = (gcType + "." + kv.Key, child.GetString("MatchType"));
                            break;
                        }
                    }
                }
            }
            catch (Exception ex) { Debug.LogError($"[PVP-ACCESS] resolve failed for '{gcType}': {ex.Message}"); }

            _pvpAccessPointCache[gcType] = result;
            return result;
        }










        private void HandleGroupClientChannel(RRConnection conn, byte messageType, byte[] data)
        {
            string hex = data != null ? BitConverter.ToString(data, 0, Math.Min(data.Length, 60)) : "null";
            Debug.LogError($"[GROUP-CH0B]  RECEIVED from {conn.LoginName}: type=0x{messageType:X2} len={data?.Length ?? 0} hex={hex}");

            var reader = (data != null && data.Length > 0) ? new LEReader(data) : null;

            try
            {
                switch (messageType)
                {

                    case 0x16:
                        {
                            if (reader == null) { Debug.LogError("[GROUP-CH0B] 0x16 no data"); break; }
                            string targetName = reader.ReadCString();
                            Debug.LogError($"[GROUP-CH0B] INVITE BY NAME '{targetName}' from {conn.LoginName}");
                            var target = FindConnectionByName(targetName);
                            if (target == null)
                            {
                                Debug.LogError($"[GROUP-CH0B] Target '{targetName}' not found online");
                                break;
                            }

                            if (!GroupDirectory.Instance.IsInGroup(conn.ConnId))
                            {
                                GroupDirectory.Instance.CreateGroup(conn.ConnId, conn.LoginName, conn.LoginName);
                                var newGroup = GroupDirectory.Instance.GetGroupForConn(conn.ConnId);
                                if (newGroup != null)
                                    SendGroupConnectedToAll(newGroup);
                            }

                            if (GroupDirectory.Instance.InvitePlayer(conn.ConnId, target.ConnId))
                            {
                                var group = GroupDirectory.Instance.GetGroupForConn(conn.ConnId);
                                uint inviterCharId = 0;
                                string inviterName = conn.LoginName;
                                if (_selectedCharacter.TryGetValue(conn.LoginName, out var inviterChar))
                                {
                                    inviterCharId = (uint)inviterChar.Id;
                                    inviterName = inviterChar.Name ?? inviterName;
                                }
                                byte[] invitePacket = GroupPackets.BuildProcessInvitation(
                                    inviterCharId, group.GroupId, inviterName, 0x00);
                                SendToClient(target, invitePacket);
                                Debug.LogError($"[GROUP] Sent processInvitation(0x32) to {target.LoginName}: inviteId=0x{inviterCharId:X8} groupId={group.GroupId} name='{inviterName}'");

                                SendSystemMessage(conn, $"Invite sent to {targetName}.");
                            }
                            break;
                        }

                    case 0x12:
                        {
                            if (reader == null) { Debug.LogError("[GROUP-CH0B] 0x12 no data"); break; }
                            uint targetId = reader.ReadUInt32();
                            Debug.LogError($"[GROUP-CH0B] INVITE BY ID 0x{targetId:X8} from {conn.LoginName}");
                            RRConnection targetById = null;
                            foreach (var candidate in _connections.Values)
                            {
                                if (candidate == null || candidate == conn || !candidate.IsConnected) continue;
                                if (GetCharSqlId(candidate) == targetId) { targetById = candidate; break; }
                            }
                            if (targetById == null)
                            {
                                Debug.LogError($"[GROUP-CH0B] Target charId 0x{targetId:X8} not found online");
                                break;
                            }

                            if (!GroupDirectory.Instance.IsInGroup(conn.ConnId))
                            {
                                GroupDirectory.Instance.CreateGroup(conn.ConnId, conn.LoginName, conn.LoginName);
                                var newGroup = GroupDirectory.Instance.GetGroupForConn(conn.ConnId);
                                if (newGroup != null)
                                    SendGroupConnectedToAll(newGroup);
                            }

                            if (GroupDirectory.Instance.InvitePlayer(conn.ConnId, targetById.ConnId))
                            {
                                var group = GroupDirectory.Instance.GetGroupForConn(conn.ConnId);
                                uint inviterCharId = 0;
                                string inviterName = conn.LoginName;
                                if (_selectedCharacter.TryGetValue(conn.LoginName, out var inviterChar))
                                {
                                    inviterCharId = (uint)inviterChar.Id;
                                    inviterName = inviterChar.Name ?? inviterName;
                                }
                                byte[] invitePacket = GroupPackets.BuildProcessInvitation(
                                    inviterCharId, group.GroupId, inviterName, 0x00);
                                SendToClient(targetById, invitePacket);
                                Debug.LogError($"[GROUP] Sent processInvitation(0x32) to {targetById.LoginName}: inviteId=0x{inviterCharId:X8} groupId={group.GroupId} name='{inviterName}'");
                                SendSystemMessage(conn, $"Invite sent to {targetById.LoginName}.");
                            }
                            break;
                        }

                    case 0x20:
                        {
                            uint inviteId = reader?.ReadUInt32() ?? 0;
                            Debug.LogError($"[GROUP-CH0B] ACCEPT invite (inviteId=0x{inviteId:X8}) from {conn.LoginName}");
                            var group = GroupDirectory.Instance.AcceptInvite(conn.ConnId, conn.LoginName, conn.LoginName);
                            if (group != null)
                            {
                                SendGroupConnectedToAll(group);
                                SendGroupHealthToAll(group);
                                if (group.IsOpen)
                                    SocialRuntime.Instance.PushWhoListToAll(SendSocialViaAuth);
                                Debug.LogError($"[GROUP] Group {group.GroupId} formed with {group.Members.Count} members");
                            }
                            break;
                        }

                    case 0x21:
                        {
                            uint inviteId = reader?.ReadUInt32() ?? 0;
                            Debug.LogError($"[GROUP-CH0B] DECLINE invite (inviteId=0x{inviteId:X8}) from {conn.LoginName}");
                            GroupDirectory.Instance.DeclineInvite(conn.ConnId);
                            break;
                        }

                    case 0x22:
                        {
                            Debug.LogError($"[GROUP-CH0B] LEAVE group from {conn.LoginName}");
                            var leaveGroup = GroupDirectory.Instance.GetGroupForConn(conn.ConnId);
                            bool wasLeader = (leaveGroup != null && leaveGroup.LeaderConnId == conn.ConnId);
                            SendGroupRemoveUser(conn);
                            GroupDirectory.Instance.LeaveGroup(conn.ConnId);
                            conn.GroupConnectedSent = false;

                            byte[] leaverSolo = GroupPackets.BuildProcessUserChangedGroup(
                                1, conn.CurrentZoneId, 0xFF, 0, 0, 0, 0, new GroupMemberInfo[0]);
                            SendCompressedA(conn, 0x01, 0x0F, leaverSolo);
                            SendJoinTalkbackGroup(conn, GetCharSqlId(conn));

                            if (leaveGroup != null && leaveGroup.Members.Count > 0)
                            {
                                if (leaveGroup.Members.Count == 1)
                                {
                                    var lastConn = FindConnectionById(leaveGroup.Members[0].ConnId);
                                    if (lastConn != null)
                                    {
                                        byte[] soloState = GroupPackets.BuildProcessUserChangedGroup(
                                            1, lastConn.CurrentZoneId, 0xFF, 0, 0, 0, 0, new GroupMemberInfo[0]);
                                        SendCompressedA(lastConn, 0x01, 0x0F, soloState);
                                        lastConn.GroupConnectedSent = false;
                                        SendJoinTalkbackGroup(lastConn, GetCharSqlId(lastConn));
                                    }
                                    GroupDirectory.Instance.LeaveGroup(leaveGroup.Members[0].ConnId);
                                }
                                else
                                {
                                    if (wasLeader)
                                    {
                                        foreach (var remainingMember in leaveGroup.Members)
                                        {
                                            var remainingMemberConnection = FindConnectionById(remainingMember.ConnId);
                                            if (remainingMemberConnection != null) remainingMemberConnection.GroupConnectedSent = false;
                                        }
                                    }
                                    SendGroupConnectedToAll(leaveGroup);
                                }
                            }
                            SocialRuntime.Instance.PushWhoListToAll(SendSocialViaAuth);
                            break;
                        }

                    case 0x14:
                        {
                            if (reader == null) break;
                            uint kickId = reader.ReadUInt32();
                            Debug.LogError($"[GROUP-CH0B] KICK member 0x{kickId:X8} from {conn.LoginName}");
                            var group = GroupDirectory.Instance.GetGroupForConn(conn.ConnId);
                            if (group == null || group.LeaderConnId != conn.ConnId) break;
                            RRConnection kickTarget = FindGroupMemberByCharSqlId(group, kickId);
                            if (kickTarget == null || kickTarget.ConnId == conn.ConnId) break;
                            byte[] kickPacket = GroupPackets.BuildProcessRemoveUser(group.GroupId, kickId);
                            foreach (var member in group.Members)
                            {
                                var memberConnection = FindConnectionById(member.ConnId);
                                if (memberConnection != null) SendToClient(memberConnection, kickPacket);
                            }
                            GroupDirectory.Instance.LeaveGroup(kickTarget.ConnId);
                            kickTarget.GroupConnectedSent = false;
                            byte[] kickSolo = GroupPackets.BuildProcessUserChangedGroup(
                                1, kickTarget.CurrentZoneId, 0xFF, 0, 0, 0, 0, new GroupMemberInfo[0]);
                            SendCompressedA(kickTarget, 0x01, 0x0F, kickSolo);
                            if (group.Members.Count == 1)
                            {
                                var lastConn = FindConnectionById(group.Members[0].ConnId);
                                if (lastConn != null)
                                {
                                    byte[] soloState = GroupPackets.BuildProcessUserChangedGroup(
                                        1, lastConn.CurrentZoneId, 0xFF, 0, 0, 0, 0, new GroupMemberInfo[0]);
                                    SendCompressedA(lastConn, 0x01, 0x0F, soloState);
                                    lastConn.GroupConnectedSent = false;
                                }
                                GroupDirectory.Instance.LeaveGroup(group.Members[0].ConnId);
                            }
                            else
                            {
                                SendGroupConnectedToAll(group);
                            }
                            SocialRuntime.Instance.PushWhoListToAll(SendSocialViaAuth);
                            Debug.LogError($"[GROUP] Kicked charSqlId=0x{kickId:X8} from group {group.GroupId}");
                            break;
                        }

                    case 0x15:
                        {
                            if (reader == null) break;
                            uint newLeaderId = reader.ReadUInt32();
                            Debug.LogError($"[GROUP-CH0B] SET LEADER 0x{newLeaderId:X8} from {conn.LoginName}");
                            var group = GroupDirectory.Instance.GetGroupForConn(conn.ConnId);
                            if (group == null || group.LeaderConnId != conn.ConnId) break;
                            RRConnection newLeaderConn = FindGroupMemberByCharSqlId(group, newLeaderId);
                            if (newLeaderConn == null) break;
                            int newLeaderConnId = newLeaderConn.ConnId;
                            GroupDirectory.Instance.SetLeader(conn.ConnId, newLeaderConnId);
                            byte[] setLeaderPacket = GroupPackets.BuildProcessSetLeader(group.GroupId, newLeaderId);
                            foreach (var member in group.Members)
                            {
                                var memberConnection = FindConnectionById(member.ConnId);
                                if (memberConnection != null)
                                    SendToClient(memberConnection, setLeaderPacket);
                            }
                            SocialRuntime.Instance.PushWhoListToAll(SendSocialViaAuth);
                            Debug.LogError($"[GROUP] processSetLeader group={group.GroupId} newLeader=0x{newLeaderId:X8}");
                            break;
                        }

                    case 0x17:
                        {
                            if (reader == null) break;
                            byte diff = reader.ReadByte();
                            Debug.LogError($"[GROUP-CH0B] SET DIFFICULTY {diff} from {conn.LoginName}");
                            bool personalOnly;
                            if (!GroupDirectory.Instance.SetMonsterDifficulty(conn.ConnId, diff, out personalOnly))
                                break;
                            var group = GroupDirectory.Instance.GetGroupForConn(conn.ConnId);
                            if (group != null)
                            {
                                byte[] diffPacket = GroupPackets.BuildMonsterDifficulty(diff, personalOnly);
                                if (personalOnly)
                                {
                                    SendToClient(conn, diffPacket);
                                }
                                else
                                {
                                    foreach (var member in group.Members)
                                    {
                                        var memberConnection = FindConnectionById(member.ConnId);
                                        if (memberConnection != null) SendToClient(memberConnection, diffPacket);
                                    }
                                }
                            }
                            break;
                        }

                    case 0x24:
                        {
                            if (reader == null) break;
                            byte flag = reader.ReadByte();
                            Debug.LogError($"[GROUP-CH0B] SET OPEN GROUP flag={flag} from {conn.LoginName}");
                            var group = GroupDirectory.Instance.GetGroupForConn(conn.ConnId);
                            if (group != null)
                            {
                                group.IsOpen = (flag != 0);
                                SendGroupConnectedToAll(group);
                                SocialRuntime.Instance.PushWhoListToAll(SendSocialViaAuth);
                            }
                            break;
                        }

                    case 0x26:
                        {
                            Debug.LogError($"[GROUP-CH0B] RESET INSTANCES from {conn.LoginName}");
                            var resetGroup = GroupDirectory.Instance.GetGroupForConn(conn.ConnId);
                            if (resetGroup != null && resetGroup.LeaderConnId != conn.ConnId)
                            {
                                SendSystemMessage(conn, "Only the party leader can reset dungeon instances.");
                                break;
                            }
                            GroupDirectory.Instance.ResetInstances(conn.ConnId);
                            int soloReset = ResetSoloDungeonInstances(conn);
                            SendSystemMessage(conn, soloReset > 0
                                ? "Dungeon instances reset. Re-enter to get a new layout."
                                : "Dungeon instances reset.");
                            break;
                        }

                    case 0x28:
                        {
                            if (reader == null) break;
                            byte mode = reader.ReadByte();
                            Debug.LogError($"[GROUP-CH0B] SET INVITE MODE {mode} from {conn.LoginName}");
                            var group = GroupDirectory.Instance.GetGroupForConn(conn.ConnId);
                            if (group != null)
                                group.InviteMode = mode;
                            SendToClient(conn, GroupPackets.BuildChangedInviteMode(mode));
                            break;
                        }

                    case 0x27:
                        {
                            if (reader == null) break;
                            uint gotoId = reader.ReadUInt32();
                            Debug.LogError($"[GROUP-CH0B] GOTO member 0x{gotoId:X8} from {conn.LoginName}");
                            var group = GroupDirectory.Instance.GetGroupForConn(conn.ConnId);
                            if (group == null) break;
                            RRConnection gotoTarget = FindGroupMemberByCharSqlId(group, gotoId);
                            if (gotoTarget == null || gotoTarget.ConnId == conn.ConnId) break;
                            string targetZone = gotoTarget.CurrentZoneName ?? "tutorial";
                            float gotoX = gotoTarget.PlayerPosX;
                            float gotoY = gotoTarget.PlayerPosY;
                            float gotoZ = gotoTarget.PlayerPosZ;
                            Debug.LogError($"[GROUP] GoTo: {conn.LoginName}  {gotoTarget.LoginName} at {targetZone} ({gotoX},{gotoY},{gotoZ})");
                            ChangeZoneToPosition(conn, targetZone, gotoX, gotoY, gotoZ);
                            break;
                        }

                    case 0x29:
                        Debug.LogError($"[PVP] {conn.LoginName} entering PVP hub zone");
                        HandleEnterPvpZone(conn);
                        break;

                    case 0x2A:
                        Debug.LogError($"[PVP] {conn.LoginName} requesting PVP match");
                        HandleRequestPvpMatch(conn, data);
                        break;

                    case 0x2B:
                        Debug.LogError($"[PVP] {conn.LoginName} cancelling PVP queue");
                        HandleCancelPvpMatch(conn);
                        break;

                    case 0x2C:
                        Debug.LogError($"[PVP] {conn.LoginName} leaving PVP system");
                        HandleLeavePvp(conn);
                        break;

                    case 0x2D:
                        {
                            if (reader == null || data.Length < 4) { Debug.LogError("[PVP] 0x2D no data"); break; }
                            uint targetCharSqlId = reader.ReadUInt32();
                            Debug.LogError($"[PVP-DUEL] {conn.LoginName} requests duel with CharSQLID {targetCharSqlId}");
                            HandleDuelRequest(conn, targetCharSqlId);
                            break;
                        }

                    case 0x2E:
                        {
                            Debug.LogError($"[PVP-DUEL] {conn.LoginName} accepts duel");
                            HandleDuelAccept(conn);
                            break;
                        }

                    case 0x2F:
                        {
                            Debug.LogError($"[PVP-DUEL] {conn.LoginName} declines duel");
                            HandleDuelDecline(conn);
                            break;
                        }

                    default:
                        Debug.LogError($"[GROUP-CH0B] unhandled type=0x{messageType:X2}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GROUP-CH0B] type=0x{messageType:X2} state=failed message='{ex.Message}' stack='{ex.StackTrace}'");
            }
        }

        private readonly Gameplay.DuelRuntime _duelRuntime = new Gameplay.DuelRuntime();
    }
}
