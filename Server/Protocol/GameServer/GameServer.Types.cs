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

        private enum EntitySynchInfoOwner
        {
            Unknown,
            NonUnit,
            Avatar,
            Monster
        }

        private struct EntitySynchInfoDecision
        {
            public bool Allow;
            public byte Flags;
            public uint HPWire;
            public EntitySynchInfoOwner Owner;
            public string Reason;
            public uint OwnerEntityId;
            public uint ComponentId;
            public byte Subtype;
            public float Now;
            public uint ValidationCutoffTick;
            public float ValidationCutoffTime;
            public string Provenance;
            public string RuntimeInstanceKey;
            public uint SchedulerTick;
            public bool SubEntityPhase;
            public int RngPos;
            public string HpMutationSource;

            public static EntitySynchInfoDecision Empty(EntitySynchInfoOwner owner, string reason)
            {
                return new EntitySynchInfoDecision { Allow = true, Flags = 0x00, HPWire = 0, Owner = owner, Reason = reason };
            }

            public static EntitySynchInfoDecision HP(EntitySynchInfoOwner owner, uint hpWire, string reason, uint ownerEntityId = 0, uint componentId = 0, byte subtype = 0, float clientNow = -1f, string provenance = null, uint validationCutoffTick = 0, float validationCutoffTime = -1f, string runtimeInstanceKey = null, uint schedulerTick = 0, bool subEntityPhase = false, int rngPos = -1, string hpMutationSource = null)
            {
                return new EntitySynchInfoDecision { Allow = true, Flags = 0x02, HPWire = hpWire, Owner = owner, Reason = reason, OwnerEntityId = ownerEntityId, ComponentId = componentId, Subtype = subtype, Now = clientNow, Provenance = provenance, ValidationCutoffTick = validationCutoffTick, ValidationCutoffTime = validationCutoffTime, RuntimeInstanceKey = runtimeInstanceKey, SchedulerTick = schedulerTick, SubEntityPhase = subEntityPhase, RngPos = rngPos, HpMutationSource = hpMutationSource };
            }

            public static EntitySynchInfoDecision Block(EntitySynchInfoOwner owner, string reason)
            {
                return new EntitySynchInfoDecision { Allow = false, Flags = 0x00, HPWire = 0, Owner = owner, Reason = reason };
            }

            public ResolvedEntitySynchInfo ToResolved(uint fallbackOwnerEntityId, uint fallbackComponentId, byte fallbackSubtype, float fallbackNow, string fallbackProvenance)
            {
                uint ownerEntityId = OwnerEntityId != 0 ? OwnerEntityId : fallbackOwnerEntityId;
                uint componentId = ComponentId != 0 ? ComponentId : fallbackComponentId;
                byte resolvedSubtype = Subtype != 0 ? Subtype : fallbackSubtype;
                float clientNow = Now >= 0f ? Now : fallbackNow;
                string provenance = !string.IsNullOrWhiteSpace(Provenance) ? Provenance : fallbackProvenance;
                return new ResolvedEntitySynchInfo(new EntitySynchInfoPayload(Flags, HPWire), ownerEntityId, componentId, resolvedSubtype, clientNow, Reason, provenance, ValidationCutoffTick, ValidationCutoffTime, RuntimeInstanceKey, SchedulerTick, SubEntityPhase, RngPos, HpMutationSource);
            }
        }

        private enum PendingMonsterBehaviorKind
        {
            Move,
            Attack,
            Follow
        }

        private struct PendingMonsterBehaviorUpdate
        {
            public PendingMonsterBehaviorKind Kind;
            public uint EntityId;
            public uint BehaviorId;
            public int ConnId;
            public uint TargetEntityId;
            public float TargetX;
            public float TargetY;
            public byte UseFlags;
            public byte SessionId;
            public bool UseTargetAction;
            public float QueuedAt;
        }

        private TcpListener _listener;
        private class UDPSession
        {
            public IPEndPoint Endpoint;
            public byte[] BlowfishKey;
            public BlowfishEngine EncryptEngine;
            public BlowfishEngine DecryptEngine;
            public bool IsEstablished;
            public RRConnection Connection;

            public void InitializeCipher(byte[] key)
            {
                BlowfishKey = key;
                EncryptEngine = new BlowfishEngine();
                DecryptEngine = new BlowfishEngine();
                EncryptEngine.Init(true, new KeyParameter(key));
                DecryptEngine.Init(false, new KeyParameter(key));
            }
        }

        private Dictionary<string, UDPSession> _udpSessions = new Dictionary<string, UDPSession>();

        private static readonly Dictionary<uint, string> _skillHashToGcClass = new Dictionary<uint, string>
        {
            { 0xA6CCC405u, "skills.generic.1HMeleeSpeedBuff" },
            { 0xB301E9E6u, "skills.generic.2HMeleeSpeedBuff" },
            { 0xE70E75EDu, "skills.generic.AggroIncreaseModBuff" },
            { 0x5E5B060Au, "skills.generic.Blight" },
            { 0xCCA86938u, "skills.generic.BlockKnockdownProcPassive" },
            { 0x3F7F0F7Du, "skills.generic.Butcher" },
            { 0x6063983Au, "skills.generic.Charge" },
            { 0x60ADE560u, "skills.generic.Cleave" },
            { 0x393590D7u, "skills.generic.CleaveUpgradeProcPassive" },
            { 0x449319F1u, "skills.generic.DivineDamageBuff" },
            { 0xA86DE2F4u, "skills.generic.DivineIntervention" },
            { 0x31E9D9CFu, "skills.generic.DivineMeleeAttack" },
            { 0x99C3D77Bu, "skills.generic.DivineRay" },
            { 0x5D83082Cu, "skills.generic.DivineResistBuff" },
            { 0x1F1A7C64u, "skills.generic.DivineResistPassive" },
            { 0xAC80BBEEu, "skills.generic.FearMeleeAttack" },
            { 0xC4A217A3u, "skills.generic.FearResistModPassive" },
            { 0xE588072Cu, "skills.generic.FearShot" },
            { 0x997C6A0Au, "skills.generic.FighterClassPassive" },
            { 0x40243947u, "skills.generic.FireBolt" },
            { 0x4024C5DBu, "skills.generic.FireCone" },
            { 0x63425376u, "skills.generic.FireCurseShot" },
            { 0xB2A6D958u, "skills.generic.FireDamageBuff" },
            { 0x8C80D7DDu, "skills.generic.FireMeleeSummon" },
            { 0xCB96C793u, "skills.generic.FireResistBuff" },
            { 0xA142566Bu, "skills.generic.FireResistPassive" },
            { 0x402CE606u, "skills.generic.FireRing" },
            { 0x402D6E54u, "skills.generic.FireShot" },
            { 0xBD9F10B4u, "skills.generic.HealSelf" },
            { 0x2F4A0032u, "skills.generic.IceBolt" },
            { 0xDE3EE483u, "skills.generic.IceDamageBuff" },
            { 0x54B3785Du, "skills.generic.IceMultiBolt" },
            { 0xF72ED2BEu, "skills.generic.IceResistBuff" },
            { 0x4BFA15B6u, "skills.generic.IceResistPassive" },
            { 0x2F53353Fu, "skills.generic.IceShot" },
            { 0xF1B72961u, "skills.generic.IceTargetedBurst" },
            { 0x56823B18u, "skills.generic.IceTargetedBurstUpgradeProcPassive" },
            { 0x327BF3B8u, "skills.generic.InfectiousPoisonUpgradeProcPassive" },
            { 0x90E6BBFBu, "skills.generic.MageClassPassive" },
            { 0xF3EBCFABu, "skills.generic.MagicDamageModPassive" },
            { 0x448B48B7u, "skills.generic.ManaSelf" },
            { 0x94B500E6u, "skills.generic.ManaShield" },
            { 0x7502AD10u, "skills.generic.MeleeAttackRatingModPassive" },
            { 0x8E2E23BCu, "skills.generic.MeleeAttackSpeedModPassive" },
            { 0x9B9699A5u, "skills.generic.MeleeDamageReflectionBuff" },
            { 0x4AC3B99Fu, "skills.generic.MinMoveSpeedBuff" },
            { 0x82EEC8C9u, "skills.generic.MonsterBaitHealthModPassive" },
            { 0xC3578423u, "skills.generic.NoxiousShot" },
            { 0x45E3A604u, "skills.generic.PenetrateKnockdownShot" },
            { 0x5FAE1AA6u, "skills.generic.PoisonBlastRadius" },
            { 0xA366A18Au, "skills.generic.PoisonDamageBuff" },
            { 0xBC568FC5u, "skills.generic.PoisonResistBuff" },
            { 0xBB68895Du, "skills.generic.PoisonResistPassive" },
            { 0xC30F7906u, "skills.generic.PoisonShot" },
            { 0x2515F184u, "skills.generic.PoisonTrail" },
            { 0xF7B882A1u, "skills.generic.RangeAttackSpeedModPassive" },
            { 0x83AC0575u, "skills.generic.RangedSpeedBuff" },
            { 0x582F9DC0u, "skills.generic.RangerClassPassive" },
            { 0xBBA98687u, "skills.generic.ShadowBolt" },
            { 0x80C11698u, "skills.generic.ShadowDamageBuff" },
            { 0xAA5741BAu, "skills.generic.ShadowLightning" },
            { 0x5E4290C8u, "skills.generic.ShadowLightningKnockdown" },
            { 0xECA5DFB1u, "skills.generic.ShadowLightningUpgradeProcPassive" },
            { 0xBBB21055u, "skills.generic.ShadowRage" },
            { 0x99B104D3u, "skills.generic.ShadowResistBuff" },
            { 0x12B28BABu, "skills.generic.ShadowResistPassive" },
            { 0x892B23BBu, "skills.generic.ShadowTendrils" },
            { 0x14D911E1u, "skills.generic.SlowDeBuff" },
            { 0x3AA3DD1Du, "skills.generic.SnowmanFreezeAura" },
            { 0x756AB8F5u, "skills.generic.SnowmanHealthModAuraBuff" },
            { 0xF7D5A663u, "skills.generic.SnowManIceDamageProcAuraModBuff" },
            { 0x86501370u, "skills.generic.Sprint" },
            { 0x2ADDC8C3u, "skills.generic.Stomp" },
            { 0x7A0B94B7u, "skills.generic.StunResistBuff" },
            { 0xDFF88E97u, "skills.generic.SummonerClassPassive" },
            { 0x1F638F77u, "skills.generic.SummonMonsterBait" },
            { 0xDD957B31u, "skills.generic.SummonBlingGnome" },
            { 0x7E1353D2u, "skills.Generic.SummonSnowman" },
            { 0xE9DDA4DFu, "skills.generic.Teleport" },
        };
        public class DroppedItemInfo
        {
            public GCObject Item;
            public long DbId;
            public string Zone;
            public uint ZoneId;
            public uint InstanceId;
            public float PosX, PosY, PosZ;
            public int PlayerLevel;
            public string DroppedBy;
            public int Quantity = 1;
            public bool IsQuestItem;
            public bool IsGoldDrop;
            public uint GoldAmount;
        }
        private Dictionary<ushort, DroppedItemInfo> _droppedItems = new Dictionary<ushort, DroppedItemInfo>();

        private struct GotoGCData
        {
            public readonly string TargetZone;
            public readonly string TargetEntity;
            public readonly int Range;
            public GotoGCData(string zone, string entity, int range)
            { TargetZone = zone; TargetEntity = entity; Range = range; }
        }

        private void GiveOnAcceptItem(RRConnection conn, string onAcceptItem)
        {
            if (string.IsNullOrEmpty(onAcceptItem)) return;

            string connId = conn.ConnId.ToString();
            var playerState = GetPlayerState(connId);
            if (playerState == null || conn.UnitContainerId == 0) return;

            string gcClass = onAcceptItem;
            var generated = GCObjectGeneratorTable.Instance.GenerateAuthoredGeneratorLoot(onAcceptItem, 1, playerState.Level, !IsPlayerFree(conn.LoginName), "quest-onaccept");
            var generatedItem = generated.FirstOrDefault(d => d != null && d.IsItem);
            if (generatedItem != null && !string.IsNullOrWhiteSpace(generatedItem.GCType))
                gcClass = generatedItem.GCType;
            else if (!IsDirectAuthoredRewardItem(onAcceptItem))
            {
                RuntimeEvidence.LogFallbackHit("gc-object-generator-table", "missing-generator", $"source=quest-onaccept generator={onAcceptItem}", 32);
                Debug.LogError($"[AUTHORED-COVERAGE] area=quest-onaccept reason=missing-generator generator={onAcceptItem}");
                return;
            }

            Debug.LogError($"[QUEST-ACCEPT-ITEM] Giving item: {onAcceptItem} -> GCClass={gcClass} source={(generatedItem != null ? "authored-generator" : "direct-item")}");

            var itemData = DatabaseLoader.FindItem(gcClass);
            int itemWidth = itemData?.inventoryWidth ?? 1;
            int itemHeight = itemData?.inventoryHeight ?? 1;

            byte slotX = 0, slotY = 0;
            bool foundSlot = false;
            for (byte row = 0; row < 8 && !foundSlot; row++)
                for (byte col = 0; col < 10 && !foundSlot; col++)
                    if (!IsInventorySlotOccupied(connId, col, row, itemWidth, itemHeight))
                    { slotX = col; slotY = row; foundSlot = true; }

            if (!foundSlot)
            {
                Debug.LogError($"[QUEST-ACCEPT-ITEM] Inventory full! Cannot give {gcClass}");
                return;
            }

            uint slot = GetNextInventorySlot(connId);

            var writer = new LEWriter();
            writer.WriteByte(0x07);

            writer.WriteByte(0x35);
            writer.WriteUInt16(conn.UnitContainerId);
            writer.WriteByte(0x1E);
            writer.WriteByte(0x0B);
            writer.WriteByte(0xFF);
            writer.WriteCString(GCObject.GetPacketGCClassFor(gcClass));
            writer.WriteUInt32(slot);
            writer.WriteByte(slotX);
            writer.WriteByte(slotY);
            writer.WriteByte(0x01);
            writer.WriteByte(0x01);
            writer.WriteByte(0x00);
            writer.WriteByte(0x00);
            WritePlayerEntitySynch(conn, writer);

            writer.WriteByte(0x06);

            byte[] packet = writer.ToArray();
            Debug.LogError($"[QUEST-ACCEPT-ITEM] Sending 0x1E packet ({packet.Length} bytes): {BitConverter.ToString(packet)}");
            SendCompressedA(conn, 0x01, 0x0F, packet);

            var newItem = new GCObject { GCClass = gcClass, DFCClass = "Item" };
            TrackInventoryItem(connId, slot, newItem, slotX, slotY);
            OccupyInventorySlots(connId, slotX, slotY, itemWidth, itemHeight);
            SetStackCount(connId, slot, 1);

            SavePlayerInventory(conn);
            NotifyQuestItemAcquired(conn, gcClass);

            Debug.LogError($"[QUEST-ACCEPT-ITEM]  {gcClass} placed in inventory at ({slotX},{slotY}) slot {slot}");
        }

        private void GiveStackedItem(RRConnection conn, string gcType, int totalCount, int maxStackSize = 100)
        {
            if (totalCount <= 0 || string.IsNullOrEmpty(gcType)) return;
            if (conn.UnitContainerId == 0)
            {
                Debug.LogError($"[GIVE-STACKED] No UnitContainerId - cannot give {gcType}");
                return;
            }

            string connId = conn.ConnId.ToString();
            int remaining = totalCount;

            Debug.LogError($"[GIVE-STACKED] {gcType} x{totalCount} (max stack {maxStackSize})");

            if (_playerInventoryItems.ContainsKey(connId))
            {
                var slots = new List<uint>(_playerInventoryItems[connId].Keys);
                foreach (var slotId in slots)
                {
                    if (remaining <= 0) break;
                    var entry = _playerInventoryItems[connId][slotId];
                    if (entry.item == null) continue;
                    if (!string.Equals(entry.item.GCClass, gcType, StringComparison.OrdinalIgnoreCase))
                        continue;

                    int currentCount = GetStackCount(connId, slotId);
                    if (currentCount >= maxStackSize) continue;

                    int canAdd = System.Math.Min(maxStackSize - currentCount, remaining);
                    int newCount = currentCount + canAdd;
                    remaining -= canAdd;

                    var qWriter = new LEWriter();
                    qWriter.WriteByte(0x07);
                    qWriter.WriteByte(0x35);
                    qWriter.WriteUInt16(conn.UnitContainerId);
                    qWriter.WriteByte(0x22);
                    qWriter.WriteUInt32(slotId);
                    qWriter.WriteByte((byte)(newCount > 255 ? 255 : newCount));
                    WritePlayerEntitySynch(conn, qWriter);
                    qWriter.WriteByte(0x06);
                    SendCompressedA(conn, 0x01, 0x0F, qWriter.ToArray());

                    SetStackCount(connId, slotId, newCount);
                    Debug.LogError($"[GIVE-STACKED] Topped up slot {slotId} {currentCount}->{newCount} (+{canAdd})");
                }
            }

            var itemData = DatabaseLoader.FindItem(gcType);
            int itemWidth = itemData?.inventoryWidth ?? 1;
            int itemHeight = itemData?.inventoryHeight ?? 1;

            while (remaining > 0)
            {
                var (sx, sy) = FindNextFreeInventorySlot(connId, itemWidth, itemHeight);
                if (sx < 0)
                {
                    Debug.LogError($"[GIVE-STACKED]  Inventory full! {remaining}x {gcType} not given");
                    break;
                }
                byte slotX = (byte)sx, slotY = (byte)sy;

                int stackSize = System.Math.Min(remaining, maxStackSize);
                remaining -= stackSize;
                uint slot = GetNextInventorySlot(connId);

                var writer = new LEWriter();
                writer.WriteByte(0x07);
                writer.WriteByte(0x35);
                writer.WriteUInt16(conn.UnitContainerId);
                writer.WriteByte(0x1E);
                writer.WriteByte(0x0B);
                writer.WriteByte(0xFF);
                writer.WriteCString(GCObject.GetPacketGCClassFor(gcType));
                writer.WriteUInt32(slot);
                writer.WriteByte(slotX);
                writer.WriteByte(slotY);
                writer.WriteByte((byte)(stackSize > 255 ? 255 : stackSize));
                writer.WriteByte(0x01);
                writer.WriteByte(0x00);
                writer.WriteByte(0x00);
                WritePlayerEntitySynch(conn, writer);
                writer.WriteByte(0x06);
                SendCompressedA(conn, 0x01, 0x0F, writer.ToArray());

                var newItem = new GCObject { GCClass = gcType, DFCClass = "Item" };
                TrackInventoryItem(connId, slot, newItem, slotX, slotY);
                OccupyInventorySlots(connId, slotX, slotY, itemWidth, itemHeight);
                SetStackCount(connId, slot, stackSize);

                Debug.LogError($"[GIVE-STACKED] New stack: slot {slot} at ({slotX},{slotY}) x{stackSize}");
            }

            SavePlayerInventory(conn);
            Debug.LogError($"[GIVE-STACKED]  Done. {totalCount - remaining}/{totalCount} {gcType} placed");
        }

        private void UpgradePotionsForMembers(List<LootDrop> drops, RRConnection conn)
        {
            string who = conn?.LoginName ?? "<null>";
            if (drops == null) { Debug.LogError($"[LOOT-MEMBER] {who}: drops==null, skipping"); return; }
            if (conn == null) { Debug.LogError($"[LOOT-MEMBER] <null conn>: drops.Count={drops.Count}, skipping"); return; }

            bool isFree = IsPlayerFree(conn.LoginName);
            bool isAdmin = IsPlayerAdmin(conn.LoginName);
            Debug.LogError($"[LOOT-MEMBER] called for {who} isFree={isFree} isAdmin={isAdmin} drops.Count={drops.Count}");

            foreach (var drop in drops)
            {
                if (drop == null) { Debug.LogError("[LOOT-MEMBER]   drop=null"); continue; }
                if (drop.IsGold) { Debug.LogError($"[LOOT-MEMBER]   gold +{drop.GoldAmount}"); continue; }
                Debug.LogError($"[LOOT-MEMBER]   item gcType='{drop.GCType ?? "<null>"}' label='{drop.Label ?? "<null>"}'");
            }

            if (isFree) { Debug.LogError($"[LOOT-MEMBER] {who} is free, no upgrades"); return; }

            int swapped = 0;
            foreach (var drop in drops)
            {
                if (drop == null || drop.IsGold || string.IsNullOrEmpty(drop.GCType)) continue;
                string gcType = drop.GCType;
                string gcTypeLower = gcType.ToLowerInvariant();
                string originalGcType = gcType;

                if (gcTypeLower.Contains("minorhealthpotion"))
                {
                    drop.GCType = System.Text.RegularExpressions.Regex.Replace(
                        gcType, "minorhealthpotion", "MajorHealthPotion",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    drop.Label = "Major Health Potion";
                    Debug.LogError($"[LOOT-MEMBER]   SWAP A-H: '{originalGcType}' -> '{drop.GCType}'");
                    swapped++;
                    continue;
                }
                if (gcTypeLower.Contains("minormanapotion"))
                {
                    drop.GCType = System.Text.RegularExpressions.Regex.Replace(
                        gcType, "minormanapotion", "MajorManaPotion",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    drop.Label = "Major Mana Potion";
                    Debug.LogError($"[LOOT-MEMBER]   SWAP A-M: '{originalGcType}' -> '{drop.GCType}'");
                    swapped++;
                    continue;
                }

                if (gcTypeLower.Contains("healthpotion_sm"))
                {
                    drop.GCType = System.Text.RegularExpressions.Regex.Replace(
                        gcType, "healthpotion_sm", "HealthPotion_Lg",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    drop.Label = "Major Health Potion";
                    Debug.LogError($"[LOOT-MEMBER]   SWAP B-H: '{originalGcType}' -> '{drop.GCType}'");
                    swapped++;
                    continue;
                }
                if (gcTypeLower.Contains("manapotion_sm"))
                {
                    drop.GCType = System.Text.RegularExpressions.Regex.Replace(
                        gcType, "manapotion_sm", "ManaPotion_Lg",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    drop.Label = "Major Mana Potion";
                    Debug.LogError($"[LOOT-MEMBER]   SWAP B-M: '{originalGcType}' -> '{drop.GCType}'");
                    swapped++;
                    continue;
                }

                if (gcTypeLower.Contains("potion"))
                {
                    Debug.LogError($"[LOOT-MEMBER]   POTION NOT MATCHED: '{gcType}' (need a swap rule for this)");
                }
            }
            Debug.LogError($"[LOOT-MEMBER] {who}: {swapped} swap(s) total");
        }

        private void CheckGotoProximity(RRConnection conn)
        {
            int connKey = conn.ConnId;
            float now = Time.time;
            if (_gotoNextCheckTime.TryGetValue(connKey, out float next) && now < next)
                return;
            _gotoNextCheckTime[connKey] = now + 1.0f;

            string connId = conn.ConnId.ToString();
            var questState = QuestManager.Instance.GetPlayerState(connId);
            if (questState == null) return;

            bool hasGoto = false;
            foreach (var quest in questState.ActiveQuests)
            {
                foreach (var objective in quest.Objectives)
                {
                    if (!objective.IsComplete && objective.Type != null &&
                        objective.Type.Equals("goto", StringComparison.OrdinalIgnoreCase))
                    { hasGoto = true; break; }
                }
                if (hasGoto) break;
            }
            if (!hasGoto) return;

            string curZone = conn.CurrentZoneName;
            if (string.IsNullOrEmpty(curZone)) return;

            float playerX = conn.PlayerPosX;
            float playerY = conn.PlayerPosY;

            foreach (var quest in questState.ActiveQuests)
            {
                foreach (var objective in quest.Objectives)
                {
                    if (objective.IsComplete) continue;
                    if (objective.Type == null || !objective.Type.Equals("goto", StringComparison.OrdinalIgnoreCase)) continue;

                    if (!string.IsNullOrEmpty(objective.Label) &&
                        _gotoGCByLabel.TryGetValue(objective.Label, out var gotoTarget))
                    {
                        if (!string.IsNullOrEmpty(gotoTarget.TargetZone) &&
                            !curZone.StartsWith(gotoTarget.TargetZone, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (!string.IsNullOrEmpty(gotoTarget.TargetEntity))
                        {
                            int range = gotoTarget.Range > 0 ? gotoTarget.Range : 100;

                            var waypoints = DatabaseLoader.GetWaypointsForZone(curZone);
                            if (waypoints != null)
                            {
                                foreach (var waypoint in waypoints)
                                {
                                    if (!waypoint.name.Equals(gotoTarget.TargetEntity, StringComparison.OrdinalIgnoreCase))
                                        continue;
                                    float deltaX = playerX - waypoint.posX;
                                    float deltaY = playerY - waypoint.posY;
                                    float dist = (float)System.Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
                                    if (dist <= range)
                                    {
                                        Debug.LogError($"[GOTO-PROXIMITY]  Waypoint match: player ({playerX:F0},{playerY:F0}) within {dist:F0} of '{waypoint.name}' in {curZone} (range={range}) - completing '{objective.Label}'");
                                        CompleteGotoObjective(conn, quest, objective);
                                        return;
                                    }
                                }
                            }

                            var portalsStepB = DatabaseLoader.GetPortalsForZone(curZone);
                            if (portalsStepB != null)
                            {
                                foreach (var portal in portalsStepB)
                                {
                                    if (string.IsNullOrEmpty(portal.targetZone)) continue;
                                    var targetWps = DatabaseLoader.GetWaypointsForZone(portal.targetZone);
                                    if (targetWps == null) continue;

                                    bool targetHasEntity = false;
                                    foreach (var waypoint in targetWps)
                                        if (waypoint.name.Equals(gotoTarget.TargetEntity, StringComparison.OrdinalIgnoreCase))
                                        { targetHasEntity = true; break; }
                                    if (!targetHasEntity) continue;

                                    float deltaX = playerX - portal.posX;
                                    float deltaY = playerY - portal.posY;
                                    float dist = (float)System.Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
                                    if (dist <= range)
                                    {
                                        Debug.LogError($"[GOTO-PROXIMITY]  Portal match: player ({playerX:F0},{playerY:F0}) within {dist:F0} of portal '{portal.name}'->{portal.targetZone} (range={range}) - completing '{objective.Label}'");
                                        CompleteGotoObjective(conn, quest, objective);
                                        return;
                                    }
                                }
                            }
                        }
                        continue;
                    }

                    if (string.IsNullOrEmpty(objective.Target)) continue;
                    var portals = DatabaseLoader.GetPortalsForZone(curZone);
                    if (portals == null || portals.Count == 0) continue;

                    foreach (var portal in portals)
                    {
                        if (string.IsNullOrEmpty(portal.targetZone)) continue;

                        var targetWaypoints = DatabaseLoader.GetWaypointsForZone(portal.targetZone);
                        if (targetWaypoints == null) continue;

                        bool waypointMatch = false;
                        foreach (var waypoint in targetWaypoints)
                            if (waypoint.name.Equals(objective.Target, StringComparison.OrdinalIgnoreCase))
                            { waypointMatch = true; break; }
                        if (!waypointMatch) continue;

                        float deltaX = playerX - portal.posX;
                        float deltaY = playerY - portal.posY;
                        float dist = (float)System.Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
                        float triggerRange = System.Math.Max(portal.width, portal.height);
                        if (triggerRange < 100) triggerRange = 100;

                        if (dist <= triggerRange)
                        {
                            Debug.LogError($"[GOTO-PROXIMITY]  Portal path: player at ({playerX:F0},{playerY:F0}) within {dist:F0} of portal '{portal.name}' (range={triggerRange}) - completing '{objective.Label}'");
                            CompleteGotoObjective(conn, quest, objective);
                            return;
                        }
                    }
                }
            }
        }

        private void CompleteGotoObjectivesOnZoneEntry(RRConnection conn)
        {
            string connId = conn.ConnId.ToString();
            var questState = QuestManager.Instance.GetPlayerState(connId);
            if (questState == null) return;

            string curZone = conn.CurrentZoneName;
            if (string.IsNullOrEmpty(curZone)) return;

            bool anyCompleted = false;
            foreach (var quest in questState.ActiveQuests)
            {
                foreach (var objective in quest.Objectives)
                {
                    if (objective.IsComplete) continue;
                    if (objective.Type == null || !objective.Type.Equals("goto", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.IsNullOrEmpty(objective.Label)) continue;

                    if (!_gotoGCByLabel.TryGetValue(objective.Label, out var gotoTarget)) continue;
                    if (string.IsNullOrEmpty(gotoTarget.TargetZone)) continue;
                    if (!curZone.StartsWith(gotoTarget.TargetZone, StringComparison.OrdinalIgnoreCase)) continue;

                    if (string.IsNullOrEmpty(gotoTarget.TargetEntity))
                    {
                        Debug.LogError($"[GOTO-ZONE-ENTRY]  Player entered '{curZone}' - completing goto '{objective.Label}' for quest {quest.QuestId}");
                        CompleteGotoObjective(conn, quest, objective);
                        anyCompleted = true;
                    }
                }
            }

            if (anyCompleted)
                SavePlayerQuests(conn);
        }

        private void CompleteGotoObjective(RRConnection conn, ActiveQuest quest, QuestProgress objective)
        {
            objective.Current = objective.Required;
            QuestManager.Instance.SendProgressPacket(conn, quest.InstanceId, quest);
            SavePlayerQuests(conn);
        }


        private void ApplyQuestRewards(RRConnection conn, QuestData questData)
        {
            if (questData == null) return;

            float questGoldPerLevel = GCDatabase.Instance.GetKnob("QuestGoldPerLevel", 250f);
            float questXPPerLevel = GCDatabase.Instance.GetKnob("QuestExperiencePerLevel", 100f);
            float memberGoldMod = GCDatabase.Instance.GetKnob("MemberGoldMod", 1.15f);
            float freePlayerXPMult = GCDatabase.Instance.GetKnob("FreePlayerExperienceMult", 0.87f);
            float gcExperienceMod = GCDatabase.Instance.GetKnob("ExperienceMod", 5.0f);

            bool isFree = IsPlayerFree(conn.LoginName);
            int difficulty = GetDifficultyForConn(conn);
            float diffXPMult = GetDifficultyXPMult(difficulty);

            int qLevel = questData.level > 0 ? questData.level : 1;
            float cashMult = questData.cashReward > 0 ? questData.cashReward : 0.5f;

            uint goldReward = (uint)System.Math.Max(1, System.Math.Round(
                qLevel * questGoldPerLevel * cashMult));

            uint xpReward = 0;

            int tokenReward = questData.tokenReward;

            Debug.LogError($"[QUEST-REWARDS] {questData.id} (L{qLevel}) -> " +
                           $"{goldReward} gold, {xpReward} XP, {tokenReward} King's Coin(s)" +
                           $" [free={isFree}, cashx{cashMult}, diffx{diffXPMult}, xpBuff={questData.grantXPBuff}]");

            if (goldReward > 0 && _selectedCharacter.TryGetValue(conn.LoginName, out var rewardGcObj))
            {
                var rewardChar = DungeonRunners.Database.CharacterRepository.GetCharacter(rewardGcObj.Id);
                if (rewardChar != null)
                {
                    rewardChar.gold += goldReward;
                    DungeonRunners.Database.CharacterRepository.SaveCharacter(rewardChar);
                }
                if (conn.UnitContainerId != 0)
                {
                    var goldPacket = new LEWriter();
                    goldPacket.WriteByte(0x07);
                    goldPacket.WriteByte(0x35);
                    goldPacket.WriteUInt16(conn.UnitContainerId);
                    goldPacket.WriteByte(0x20);
                    goldPacket.WriteUInt32(goldReward);
                    goldPacket.WriteByte(0x00);
                    goldPacket.WriteUInt32(0x00000000);
                    goldPacket.WriteByte(0x01);
                    WritePlayerEntitySynch(conn, goldPacket);
                    goldPacket.WriteByte(0x06);
                    SendToClient(conn, goldPacket.ToArray());
                    Debug.LogError($"[QUEST-REWARDS]  Sent +{goldReward} gold to client");
                }
            }

            if (tokenReward > 0)
            {
                GiveStackedItem(conn, "QuestItemPAL.Token", tokenReward, 100);
            }


            if (questData.grantXPBuff)
            {
                SendQuestXPBonusModifier(conn);
            }

            if (!string.IsNullOrEmpty(questData.rewardItemGenerator) && questData.numRewardItems > 0)
            {
                Debug.LogError($"[QUEST-REWARDS]  Rolling {questData.numRewardItems}x from {questData.rewardItemGenerator}");
                string gen = questData.rewardItemGenerator;
                var authoredRewardDrops = GCObjectGeneratorTable.Instance.GenerateAuthoredGeneratorLoot(
                    gen,
                    questData.numRewardItems,
                    qLevel,
                    !isFree,
                    "quest-reward");
                if (authoredRewardDrops.Count > 0)
                {
                    foreach (var drop in authoredRewardDrops)
                    {
                        if (drop == null) continue;
                        if (drop.IsGold && drop.GoldAmount > 0)
                        {
                            GiveGold(conn, (uint)drop.GoldAmount, "quest-reward-generator");
                            continue;
                        }
                        if (drop.IsKingsCoin && drop.KingsCoinCount > 0)
                        {
                            GiveStackedItem(conn, "QuestItemPAL.Token", drop.KingsCoinCount, 100);
                            continue;
                        }
                        if (drop.IsItem && !string.IsNullOrWhiteSpace(drop.GCType))
                        {
                            GiveStackedItem(conn, drop.GCType, 1, 1);
                        }
                    }
                    Debug.LogError($"[QUEST-REWARDS] Authored generator {gen} produced {authoredRewardDrops.Count} reward(s)");
                    return;
                }
                if (TryGiveDirectAuthoredQuestReward(conn, questData, gen))
                    return;

                if (GCObjectGeneratorTable.Instance.CanResolveAuthoredGenerator(gen))
                {
                    Debug.LogError($"[AUTHORED-COVERAGE] area=quest-reward reason=authored-generator-empty quest={questData.id} generator={gen} count={questData.numRewardItems}");
                    return;
                }

                RuntimeEvidence.LogFallbackHit(
                    "gc-object-generator-table",
                    "missing-generator",
                    $"source=quest-reward quest={questData.id} generator={gen} count={questData.numRewardItems}",
                    32);
                Debug.LogError($"[AUTHORED-COVERAGE] area=quest-reward reason=missing-generator quest={questData.id} generator={gen} count={questData.numRewardItems}");
            }
        }

        private bool TryGiveDirectAuthoredQuestReward(RRConnection conn, QuestData questData, string gcType)
        {
            if (conn == null || string.IsNullOrWhiteSpace(gcType))
                return false;

            if (!IsDirectAuthoredRewardItem(gcType))
                return false;

            int count = Math.Max(1, questData?.numRewardItems ?? 1);
            GiveStackedItem(conn, gcType, count);
            Debug.LogError($"[QUEST-REWARDS] Direct authored item reward: quest={questData?.id ?? "<null>"} gcType={gcType} count={count}");
            return true;
        }

        private static bool IsDirectAuthoredRewardItem(string gcType)
        {
            if (string.IsNullOrWhiteSpace(gcType))
                return false;
            if (DatabaseLoader.FindItem(gcType) != null)
                return true;
            if (GCDatabase.Instance == null || !GCDatabase.Instance.IsLoaded)
                return false;

            var node = GCDatabase.Instance.ResolveWithInheritance(gcType);
            if (node == null)
                return false;
            string extends = node.Extends ?? string.Empty;
            if (extends.IndexOf("Generator", StringComparison.OrdinalIgnoreCase) >= 0 ||
                gcType.IndexOf("Generator", StringComparison.OrdinalIgnoreCase) >= 0 ||
                gcType.EndsWith("IG", StringComparison.OrdinalIgnoreCase))
                return false;

            return AuthoredExtendsClass(gcType, "Item") ||
                   AuthoredExtendsClass(gcType, "ActiveItem") ||
                   AuthoredExtendsClass(gcType, "MeleeWeapon") ||
                   AuthoredExtendsClass(gcType, "RangedWeapon") ||
                   AuthoredExtendsClass(gcType, "Armor");
        }

        public void SendQuestXPBonusModifier(RRConnection conn)
        {
            if (conn.ModifiersId == 0)
            {
                Debug.LogError("[QUEST-XP-BUFF] Cannot send QuestXPBonus - ModifiersId not set");
                return;
            }

            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x35);
            writer.WriteUInt16((ushort)conn.ModifiersId);
            writer.WriteByte(0x00);
            WriteGCType(writer, "quests.base.QuestXPBonus", preserveCase: true);
            writer.WriteUInt32(2);
            writer.WriteByte(0);
            writer.WriteUInt32(0);
            writer.WriteUInt32(0x00000000);
            writer.WriteByte(0x01);
            WritePlayerEntitySynch(conn, writer);
            writer.WriteByte(0x06);

            SendCompressedA(conn, 0x01, 0x0F, writer.ToArray());
            Debug.LogError($"[QUEST-XP-BUFF]  Sent QuestXPBonus modifier (+15% EXPMOD)");

            RecordModifierSent(conn.LoginName, "quests.base.QuestXPBonus", 2,
                level: 0, powerLevel: 0, duration: 0, sourceIsSelf: 1);
            Debug.LogError($"[QUEST-XP-BUFF]  Tracked for zone-change persistence");
        }

        public void SendZoneSpawnInvulnerability(RRConnection conn)
        {
            if (!ShouldSendZoneSpawnInvulnerability(conn))
            {
                ClearZoneSpawnInvulnerability(conn, "ZONE-SKIP");
                Debug.LogError($"[ZONE-INVULN] Skipped ZoneSpawnInvulnerability for {conn?.LoginName ?? "<null>"} (zone={conn?.CurrentZoneName ?? ""})");
                return;
            }

            if (conn.ModifiersId == 0)
            {
                Debug.LogError("[ZONE-INVULN] Cannot send ZoneSpawnInvulnerability - ModifiersId not set");
                return;
            }

            SetZoneSpawnInvulnerability(conn);
            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x35);
            writer.WriteUInt16((ushort)conn.ModifiersId);
            writer.WriteByte(0x00);
            WriteGCType(writer, "avatar.base.ZoneSpawnInvulnerabilityModifier", preserveCase: true);
            writer.WriteUInt32(3);
            writer.WriteByte(0);
            writer.WriteUInt32(0);
            writer.WriteUInt32(ZoneSpawnInvulnerabilityDuration);
            writer.WriteByte(0x01);
            WritePlayerEntitySynch(conn, writer);
            writer.WriteByte(0x06);

            SendCompressedA(conn, 0x01, 0x0F, writer.ToArray());
            Debug.LogError($"[ZONE-INVULN] Sent ZoneSpawnInvulnerability for {conn.LoginName} (zone={conn.CurrentZoneName})");
        }

        private const uint ZoneSpawnInvulnerabilityDuration = 1800;

        private struct PendingSpell
        {
            public long Sequence;
            public RRConnection Conn;
            public PlayerState State;
            public Combat.Monster Monster;
            public Combat.SpellData Spell;
            public byte ManipId;
            public byte UseFlags;
            public ushort ComponentId;
            public float StartX;
            public float StartY;
            public float AimX;
            public float AimY;
            public string InstanceKey;
            public float DueTime;
            public float ProjectileHitDistance;
            public float ProjectileDelay;
            public bool QueuedWithoutInitialTarget;
            public bool ProjectileRuntimeInitialized;
            public float FireTime;
            public int FireTick;
            public int LastUpdateTick;
            public int UpdatesCompleted;
            public int MaxLifetimeTicks;
            public float ProjectileSpeed;
            public float ProjectileSize;
            public float StepDistance;
            public float InitialDistance;
            public float CurrentDistance;
            public float MaxDistance;
        }

        private struct SpellPreSuffixFlushResult
        {
            public uint TargetEntityId;
            public int PendingBefore;
            public int PendingAfter;
            public int DueForTarget;
            public int DueOther;
            public int Applied;
            public int SkippedDead;
            public int RequeuedFuture;
            public int RequeuedOther;
            public uint BeforeHPWire;
            public uint AfterHPWire;
        }

        private struct SpellWeaponDamageEffectResult
        {
            public bool Attempted;
            public bool Landed;
            public bool Applied;
            public bool Died;
            public uint OldHPWire;
            public uint NewHPWire;
            public uint DamageWire;
            public uint HitRaw;
            public uint BlockRaw;
            public uint DamageRaw;
            public int HitRoll;
            public int BlockRoll;
            public int HitThreshold;
            public int AttackRating;
            public int DefenseRating;
            public int DamageMod;
            public int SkillDamageModRaw;
            public int ARMod;
            public int MinDamageWire;
            public int MaxDamageWire;
            public bool IsCritical;
            public string ResultName;
        }

        private RRConnection FindConnectionByAvatarEntityId(uint entityId)
        {
            if (entityId == 0) return null;
            foreach (var conn in _connections.Values)
            {
                if (conn?.Avatar == null) continue;
                if ((uint)conn.Avatar.Id == entityId)
                    return conn;
            }
            return null;
        }

        private void DrainPendingModifierKills()
        {
            while (CombatRuntime.Instance.HasPendingModifierKills)
            {
                var kill = CombatRuntime.Instance.DequeuePendingModifierKill();
                if (kill == null)
                    break;

                var monster = CombatRuntime.Instance.GetMonster(kill.TargetEntityId);
                var conn = FindConnectionByAvatarEntityId(kill.SourceEntityId);
                Debug.LogError($"[POISON-SHOT-MOD-KILL] drain target={kill.TargetEntityId} sourceEntity={kill.SourceEntityId} clientDamageTime={kill.DamageTime:F3} conn={(conn != null ? conn.ConnId.ToString() : "null")} source={kill.Source ?? "modifier-tick"}");
                if (monster == null)
                {
                    Debug.LogError($"[POISON-SHOT-MOD-KILL] missing monster target={kill.TargetEntityId}");
                    continue;
                }

                CombatRuntime.Instance.CancelMonsterPendingAttack(monster, "SPELL-MOD-tick-kill");
                try
                {
                    bool finalized = TryFinalizeMonsterKill(conn, monster, "SPELL-MOD-tick");
                    Debug.LogError($"[POISON-SHOT-MOD-KILL] finalize result={finalized} monster={monster.Name} eid={monster.EntityId}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[KILL-ERROR] SPELL-MOD-tick finalize failed for {monster.Name}: {ex.Message}\n{ex.StackTrace}");
                    if (IsUseTargetingMonster(conn, monster))
                        ClearUseTargetAndReleaseControl(conn, "SPELL-MOD-tick-error");
                }

                if (conn != null && IsUseTargetingMonster(conn, monster))
                    ClearUseTargetAndReleaseControl(conn, "SPELL-MOD-tick-kill", sendClientControlReset: true, requireActiveUseTargetForReset: true);
            }
        }

        private void DrainWeaponUseStateKills(string source)
        {
            while (Combat.WeaponUseRuntime.Instance.HasPendingKills)
            {
                var kill = Combat.WeaponUseRuntime.Instance.DequeueKill();
                if (kill == null || !kill.Killed)
                    continue;

                Debug.LogError($"[WEAPON-USE]  KILL: {kill.Monster?.Name ?? "monster"} killed by {kill.ConnKey} ({kill.DamageDealt} final dmg) source={source ?? "unknown"}");
                if (kill.Monster != null)
                    CombatRuntime.Instance.CancelMonsterPendingAttack(kill.Monster, "WeaponUseState-kill");
                try
                {
                    bool finalized = TryFinalizeMonsterKill(kill.Connection, kill.Monster, source ?? "WeaponUseState-tick");
                    Debug.LogError($"[WEAPON-USE] finalize result={finalized} monster={kill.Monster?.Name} eid={kill.Monster?.EntityId} source={source ?? "unknown"}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[KILL-ERROR] WeaponUseState finalize failed for {kill.Monster?.Name}: {ex.Message}\n{ex.StackTrace}");
                    if (IsUseTargetingMonster(kill.Connection, kill.Monster))
                        ClearUseTargetAndReleaseControl(kill.Connection, "WeaponUseState-error");
                }
                if (kill.Connection != null)
                    ClearUseTargetAndReleaseControl(kill.Connection, "WeaponUseState-kill", sendClientControlReset: true, requireActiveUseTargetForReset: true);
            }
        }

        private HashSet<string> GetActivePlayerInstanceKeys()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var conn in _connections.Values)
            {
                if (conn == null || !conn.IsConnected || conn.Avatar == null)
                    continue;
                string key = conn.RuntimeInstanceKey;
                if (!string.IsNullOrEmpty(key))
                    keys.Add(key);
            }
            return keys;
        }

        private void TickCombatDeterministicSystems(float tickNow, bool allowNewMonsterAttacks)
        {
            ProcessPendingSpellsForPlayerEntity(0, tickNow);
            ProcessPendingAoECasts(tickNow);

            bool tickEntityWanderThisPass = _entityCatchUpTicksThisFrame < _entityUpdateBudgetThisFrame;
            if (tickEntityWanderThisPass)
                _entityCatchUpTicksThisFrame++;

            HashSet<string> activeInstanceKeys = GetActivePlayerInstanceKeys();

            Combat.WeaponUseRuntime.Instance.TickProjectileEntityPhase(null, tickNow, "ClientEntityManager.updateEntities-subentities");
            CombatRuntime.Instance.MarkSubEntityUpdateCompleted(CombatRuntime.Instance.CombatTick, tickNow, "GameServer.Update-subentities");

            foreach (uint entityId in CombatRuntime.Instance.GetEntityOrderSnapshot())
            {
                if (CombatRuntime.Instance.IsMonsterEntity(entityId))
                {
                    var monster = CombatRuntime.Instance.GetMonster(entityId);
                    if (monster != null && !string.IsNullOrEmpty(monster.InstanceKey) && !activeInstanceKeys.Contains(monster.InstanceKey))
                        continue;
                    var monsterRng = CombatRuntime.Instance.GetRoomRngForMonster(monster);
                    if (tickEntityWanderThisPass && monsterRng != null)
                        Combat.WanderSimulator.Instance.TickEntity(entityId, monsterRng);
                    if (monster != null && !monster.AggroTriggered)
                        CombatRuntime.Instance.ApplyMonsterWanderClientVisiblePosition(monster, "wander-tick");
                    CombatRuntime.Instance.TickMonsterUpdateSkillsForEntity(entityId);
                    CombatRuntime.Instance.UpdateMonsterEntity(entityId, COMBAT_TICK, allowNewMonsterAttacks, tickNow, tickEntityWanderThisPass);
                }
                else if (CombatRuntime.Instance.IsPlayerEntity(entityId))
                {
                    var playerRng = CombatRuntime.Instance.GetRoomRngForPlayerEntity(entityId);
                    Combat.WeaponUseRuntime.Instance.TickPlayerEntity(entityId, playerRng, tickNow);
                }
            }

            CombatRuntime.Instance.UpdateMaintenance(COMBAT_TICK);
            UpdateEncounterObjects();
            DrainPendingModifierKills();
            DrainWeaponUseStateKills("WeaponUseState-tick");
            while (Combat.WeaponUseRuntime.Instance.HasPendingControlReleases)
            {
                var releaseConn = Combat.WeaponUseRuntime.Instance.DequeueControlRelease();
                if (releaseConn != null)
                    ClearUseTargetAndReleaseControl(releaseConn, "WeaponUseState-resolve-release", sendClientControlReset: true, requireActiveUseTargetForReset: true);
            }
        }

        private void ProcessPendingSpellsForPlayerEntity(uint playerEntityId, float now)
        {
            int pendingCount = _pendingSpells.Count;
            for (int pendingIndex = 0; pendingIndex < pendingCount; pendingIndex++)
            {
                if (!_pendingSpells.TryDequeue(out var pending))
                    break;

                uint pendingPlayerId = pending.Conn?.Avatar != null ? (uint)pending.Conn.Avatar.Id : 0;
                if (playerEntityId != 0 && pendingPlayerId != playerEntityId)
                {
                    _pendingSpells.Enqueue(pending);
                    continue;
                }

                if (pending.ProjectileRuntimeInitialized)
                {
                    if (!UpdatePendingSpellProjectile(ref pending, now, "ProcessPendingSpells"))
                        _pendingSpells.Enqueue(pending);
                    continue;
                }

                if (!IsPendingSpellDue(pending, now))
                {
                    _pendingSpells.Enqueue(pending);
                    continue;
                }

                Combat.Monster target = ResolvePendingSpellTarget(ref pending, now, "ProcessPendingSpells");
                if (target != null && target.IsAlive)
                    HandleSpellAttack(pending.Conn, pending.State, target, pending.ManipId, pending.UseFlags, pending.AimX, pending.AimY, ResolvePendingSpellImpactTime(pending, now));
                else if (pending.QueuedWithoutInitialTarget)
                    Debug.LogError($"[SPELL-PROJECTILE] no-hit spell={pending.Spell?.DisplayName ?? pending.Spell?.SkillId ?? "UNKNOWN"} manip={pending.ManipId} aim=({pending.AimX:F1},{pending.AimY:F1}) due={pending.DueTime:F3} now={now:F3} source=ProcessPendingSpells");
            }
        }

        private PendingSpell CreatePendingSpellProjectile(
            RRConnection conn,
            PlayerState state,
            Combat.Monster monster,
            Combat.SpellData spell,
            byte manipId,
            byte useFlags,
            ushort componentId,
            float startX,
            float startY,
            float aimX,
            float aimY,
            bool queuedWithoutInitialTarget,
            float hitDistanceHint)
        {
            float dx = aimX - startX;
            float dy = aimY - startY;
            float aimDistance = Mathf.Sqrt((dx * dx) + (dy * dy));
            if (aimDistance <= 0.001f)
                aimDistance = Mathf.Max(0.001f, hitDistanceHint);

            float speed = spell != null ? Mathf.Max(1f, spell.ProjectileSpeed) : 0f;
            float size = spell != null ? Mathf.Max(0f, spell.ProjectileSize) : 0f;
            float maxDistance = spell != null && size > 0f && speed > 0f
                ? ResolveProjectileMaxDistance(spell, aimDistance)
                : Mathf.Max(0f, hitDistanceHint);
            if (maxDistance <= 0.001f)
                maxDistance = Mathf.Max(0.001f, aimDistance);

            float fireTime = GetCombatNow() + ResolveActiveSkillTriggerFrames(spell, state) * COMBAT_TICK;
            int fireTick = Combat.WeaponUseRuntime.TickIndexFromTime(fireTime);
            int maxLifetimeTicks = spell != null && size > 0f && speed > 0f
                ? Math.Max(1, Combat.WeaponUseRuntime.ProjectileFlightTicks(maxDistance, speed))
                : 1;
            float stepDistance = spell != null && size > 0f && speed > 0f
                ? Combat.WeaponUseRuntime.ProjectileStepDistance(speed)
                : 0f;
            float initialDistance = spell != null && size > 0f && speed > 0f
                ? Combat.WeaponUseRuntime.ProjectileInitialDistance(speed, maxDistance)
                : 0f;
            float resolvedHitDistance = hitDistanceHint > 0f
                ? Mathf.Min(hitDistanceHint, maxDistance)
                : maxDistance;
            float projectileDelay = ResolveProjectileImpactDelay(spell, state, resolvedHitDistance);

            return new PendingSpell
            {
                Sequence = ++_nextPendingSpellProjectileSequence,
                Conn = conn,
                State = state,
                Monster = monster,
                Spell = spell,
                ManipId = manipId,
                UseFlags = useFlags,
                ComponentId = componentId,
                StartX = startX,
                StartY = startY,
                AimX = aimX,
                AimY = aimY,
                InstanceKey = conn != null ? GetInstanceZoneKey(conn) : null,
                DueTime = fireTime + projectileDelay,
                ProjectileHitDistance = resolvedHitDistance,
                ProjectileDelay = projectileDelay,
                QueuedWithoutInitialTarget = queuedWithoutInitialTarget,
                ProjectileRuntimeInitialized = spell != null && size > 0f && speed > 0f,
                FireTime = fireTime,
                FireTick = fireTick,
                LastUpdateTick = fireTick,
                UpdatesCompleted = initialDistance > 0f ? 1 : 0,
                MaxLifetimeTicks = maxLifetimeTicks,
                ProjectileSpeed = speed,
                ProjectileSize = size,
                StepDistance = stepDistance,
                InitialDistance = initialDistance,
                CurrentDistance = initialDistance,
                MaxDistance = maxDistance
            };
        }

        private bool UpdatePendingSpellProjectile(ref PendingSpell pending, float now, string source)
        {
            Combat.SpellData spell = pending.Spell;
            if (spell == null)
            {
                Combat.SpellDatabase.Initialize();
                spell = ResolveSpellFromManip(pending.Conn, pending.ManipId);
                pending.Spell = spell;
            }

            if (spell == null || pending.ProjectileSpeed <= 0f || pending.ProjectileSize <= 0f)
                return IsPendingSpellDue(pending, now);

            int nowTick = Combat.WeaponUseRuntime.TickIndexFromTime(now);
            if (nowTick <= pending.LastUpdateTick)
                return false;

            for (int updateTick = pending.LastUpdateTick + 1; updateTick <= nowTick; updateTick++)
            {
                float updateTime = updateTick * Combat.WeaponUseRuntime.UpdateTickSeconds;
                if (pending.UpdatesCompleted >= pending.MaxLifetimeTicks)
                {
                    Debug.LogError($"[SPELL-PROJECTILE] expired no-hit spell={spell.DisplayName ?? spell.SkillId ?? "spell"} seq={pending.Sequence} manip={pending.ManipId} aim=({pending.AimX:F1},{pending.AimY:F1}) current={pending.CurrentDistance:F2}/{pending.MaxDistance:F2} updates={pending.UpdatesCompleted}/{pending.MaxLifetimeTicks} tick={updateTick} source={source ?? "unknown"} sourceFunction=Projectile::update lifetime-zero-before-unit-check");
                    return true;
                }

                float beforeDistance = pending.CurrentDistance;
                float afterDistance = Mathf.Min(pending.MaxDistance, beforeDistance + Mathf.Max(0.001f, pending.StepDistance));
                pending.UpdatesCompleted++;
                pending.LastUpdateTick = updateTick;

                if (TryResolvePendingSpellTargetAlongSegment(ref pending, beforeDistance, afterDistance, out Combat.Monster target, out float hitDistance, out bool worldBlocked))
                {
                    pending.Monster = target;
                    pending.ProjectileHitDistance = hitDistance;
                    pending.ProjectileDelay = Mathf.Max(0f, updateTime - pending.FireTime);
                    pending.DueTime = updateTime;
                    pending.CurrentDistance = hitDistance;
                    float hitRadius = WeaponUseRuntime.ProjectileCollisionRadius(target.CollisionRadius, pending.ProjectileSize);
                    Debug.LogError($"[SPELL-PROJECTILE] subentity impact spell={spell.DisplayName ?? spell.SkillId ?? "spell"} seq={pending.Sequence} target={target.Name}#{target.EntityId} segment={beforeDistance:F2}->{afterDistance:F2} hitDist={hitDistance:F2} tick={updateTick} updates={pending.UpdatesCompleted}/{pending.MaxLifetimeTicks} radius={hitRadius:F2} projectileRadius={WeaponUseRuntime.ProjectileRadiusFromAuthoredSize(pending.ProjectileSize):F2} worldBlocked={worldBlocked} source={source ?? "unknown"}");
                    if (target.IsAlive && CombatRuntime.Instance.PeekMonsterCurrentHPWire(target) != 0)
                        HandleSpellAttack(pending.Conn, pending.State, target, pending.ManipId, pending.UseFlags, pending.AimX, pending.AimY, updateTime);
                    return true;
                }

                pending.CurrentDistance = afterDistance;
                if (pending.CurrentDistance + 0.0001f >= pending.MaxDistance || pending.UpdatesCompleted >= pending.MaxLifetimeTicks)
                {
                    Debug.LogError($"[SPELL-PROJECTILE] expired no-hit spell={spell.DisplayName ?? spell.SkillId ?? "spell"} seq={pending.Sequence} manip={pending.ManipId} aim=({pending.AimX:F1},{pending.AimY:F1}) current={pending.CurrentDistance:F2}/{pending.MaxDistance:F2} updates={pending.UpdatesCompleted}/{pending.MaxLifetimeTicks} tick={updateTick} source={source ?? "unknown"} sourceFunction=Projectile::update range-end");
                    return true;
                }
            }

            return false;
        }

        private bool TryResolvePendingSpellTargetAlongSegment(ref PendingSpell pending, float segmentStart, float segmentEnd, out Combat.Monster hitMonster, out float hitDistance, out bool worldBlocked)
        {
            hitMonster = null;
            hitDistance = segmentEnd;
            worldBlocked = false;
            Combat.SpellData spell = pending.Spell;
            if (spell == null || pending.Conn == null)
                return false;

            float dx = pending.AimX - pending.StartX;
            float dy = pending.AimY - pending.StartY;
            float lenSq = (dx * dx) + (dy * dy);
            if (lenSq <= 0.0001f)
                return false;

            float pathDistance = Mathf.Sqrt(lenSq);
            float dirX = dx / pathDistance;
            float dirY = dy / pathDistance;
            string zoneName = !string.IsNullOrWhiteSpace(pending.Monster?.ZoneName)
                ? pending.Monster.ZoneName
                : pending.Conn.CurrentZoneName;
            string instanceKey = !string.IsNullOrWhiteSpace(pending.InstanceKey)
                ? pending.InstanceKey
                : (pending.Conn != null ? GetInstanceZoneKey(pending.Conn) : null);
            float projectileSize = Mathf.Max(0f, pending.ProjectileSize);
            float projectileRadius = WeaponUseRuntime.ProjectileRadiusFromAuthoredSize(projectileSize);
            float scanRange = Mathf.Max(segmentEnd + projectileRadius + 20f, pending.MaxDistance + projectileRadius + 20f);

            Combat.Monster best = null;
            float bestAlong = float.MaxValue;
            float bestDistSq = float.MaxValue;
            bool bestBlocked = false;
            foreach (var candidate in CombatRuntime.Instance.GetActiveMonsters())
            {
                if (candidate == null || !candidate.IsAlive)
                    continue;
                if (CombatRuntime.Instance.PeekMonsterCurrentHPWire(candidate) == 0)
                    continue;
                if (!CombatRuntime.Instance.MatchesInstance(candidate, instanceKey))
                    continue;
                if (!string.IsNullOrWhiteSpace(zoneName)
                    && !string.IsNullOrWhiteSpace(candidate.ZoneName)
                    && !string.Equals(candidate.ZoneName, zoneName, StringComparison.OrdinalIgnoreCase))
                    continue;

                float clientVisibleNow = pending.LastUpdateTick * WeaponUseRuntime.UpdateTickSeconds;
                CombatRuntime.Instance.TryGetMonsterClientVisiblePosition(candidate, clientVisibleNow, out float candidateX, out float candidateY);
                if (((candidateX - pending.StartX) * (candidateX - pending.StartX)) + ((candidateY - pending.StartY) * (candidateY - pending.StartY)) > scanRange * scanRange)
                    continue;

                float cx = candidateX - pending.StartX;
                float cy = candidateY - pending.StartY;
                float projected = (cx * dirX) + (cy * dirY);
                float radius = WeaponUseRuntime.ProjectileCollisionRadius(candidate.CollisionRadius, projectileSize);
                if (projected + radius < segmentStart || projected - radius > segmentEnd)
                    continue;

                float closestAlong = Mathf.Clamp(projected, segmentStart, segmentEnd);
                float closestX = pending.StartX + (dirX * closestAlong);
                float closestY = pending.StartY + (dirY * closestAlong);
                float missX = candidateX - closestX;
                float missY = candidateY - closestY;
                float distSq = (missX * missX) + (missY * missY);
                float radiusSq = radius * radius;
                if (distSq > radiusSq)
                    continue;

                float entryOffset = Mathf.Sqrt(Mathf.Max(0f, radiusSq - distSq));
                float impactAlong = Mathf.Clamp(projected - entryOffset, segmentStart, segmentEnd);
                if (impactAlong < bestAlong || (Mathf.Abs(impactAlong - bestAlong) <= 0.0001f && distSq < bestDistSq))
                {
                    best = candidate;
                    bestAlong = impactAlong;
                    bestDistSq = distSq;
                    bestBlocked = false;
                }
            }

            if (best == null)
                return false;

            CombatRuntime.Instance.ApplyMonsterWanderClientVisiblePosition(best, "SpellProjectileChecker-subentity-hit");
            hitMonster = best;
            hitDistance = Mathf.Max(0f, bestAlong);
            worldBlocked = bestBlocked;
            return true;
        }

        private Combat.Monster ResolvePendingSpellTarget(ref PendingSpell pending, float now, string source)
        {
            string instanceKey = !string.IsNullOrWhiteSpace(pending.InstanceKey)
                ? pending.InstanceKey
                : (pending.Conn != null ? GetInstanceZoneKey(pending.Conn) : null);
            if (pending.Monster != null &&
                pending.Monster.IsAlive &&
                CombatRuntime.Instance.PeekMonsterCurrentHPWire(pending.Monster) != 0 &&
                CombatRuntime.Instance.MatchesInstance(pending.Monster, instanceKey))
                return pending.Monster;

            Combat.SpellData spell = pending.Spell;
            if (spell == null)
            {
                Combat.SpellDatabase.Initialize();
                spell = ResolveSpellFromManip(pending.Conn, pending.ManipId);
                pending.Spell = spell;
            }

            if (spell == null || spell.ProjectileSize <= 0f)
                return null;

            float startX = pending.StartX;
            float startY = pending.StartY;
            if (pending.Conn != null && Math.Abs(startX) <= 0.0001f && Math.Abs(startY) <= 0.0001f)
            {
                startX = pending.Conn.PlayerPosX;
                startY = pending.Conn.PlayerPosY;
            }

            Combat.Monster target = ResolvePositionSpellTargetFromStart(pending.Conn, spell, startX, startY, pending.AimX, pending.AimY, out float hitDistance);
            if (target == null)
                return null;

            pending.Monster = target;
            pending.ProjectileHitDistance = hitDistance;
            pending.ProjectileDelay = ResolveProjectileImpactDelay(spell, pending.State, hitDistance);
            Debug.LogError($"[SPELL-PROJECTILE] resolved spell={spell.DisplayName ?? spell.SkillId ?? "spell"} target={target.Name}#{target.EntityId} hitDist={hitDistance:F2} due={pending.DueTime:F3} now={now:F3} source={source ?? "unknown"} queuedWithoutInitialTarget={pending.QueuedWithoutInitialTarget}");
            return target;
        }

        private bool IsPendingSpellDue(PendingSpell pending, float now)
        {
            return pending.DueTime <= 0f || now + 0.0001f >= pending.DueTime;
        }

        private float ResolvePendingSpellImpactTime(PendingSpell pending, float now)
        {
            return pending.DueTime > 0f ? pending.DueTime : now;
        }

        private SpellPreSuffixFlushResult FlushPendingSpellsForMonsterBeforeSynch(Combat.Monster monster, float now, string source)
        {
            var result = new SpellPreSuffixFlushResult
            {
                TargetEntityId = monster != null ? monster.EntityId : 0,
                PendingBefore = _pendingSpells.Count,
                BeforeHPWire = monster != null ? CombatRuntime.Instance.PeekMonsterCurrentHPWire(monster) : 0u
            };

            if (monster == null || result.PendingBefore == 0)
            {
                result.PendingAfter = _pendingSpells.Count;
                result.AfterHPWire = result.BeforeHPWire;
                return result;
            }

            int pendingCount = _pendingSpells.Count;
            for (int pendingIndex = 0; pendingIndex < pendingCount; pendingIndex++)
            {
                if (!_pendingSpells.TryDequeue(out var pending))
                    break;

                if (pending.ProjectileRuntimeInitialized)
                {
                    uint beforeProjectileHP = CombatRuntime.Instance.PeekMonsterCurrentHPWire(monster);
                    bool consumed = UpdatePendingSpellProjectile(ref pending, now, source);
                    if (!consumed)
                    {
                        result.RequeuedFuture++;
                        _pendingSpells.Enqueue(pending);
                        continue;
                    }

                    bool sameProjectileTarget = pending.Monster != null && pending.Monster.EntityId == monster.EntityId;
                    if (sameProjectileTarget)
                        result.DueForTarget++;
                    else
                        result.DueOther++;

                    uint afterProjectileHP = CombatRuntime.Instance.PeekMonsterCurrentHPWire(monster);
                    if (afterProjectileHP != beforeProjectileHP)
                        result.Applied++;
                    continue;
                }

                if (!IsPendingSpellDue(pending, now))
                {
                    result.RequeuedFuture++;
                    _pendingSpells.Enqueue(pending);
                    continue;
                }

                Combat.Monster resolvedTarget = ResolvePendingSpellTarget(ref pending, now, source);
                bool sameTarget = resolvedTarget != null && resolvedTarget.EntityId == monster.EntityId;
                if (sameTarget)
                    result.DueForTarget++;
                else
                    result.DueOther++;

                if (resolvedTarget == null || !resolvedTarget.IsAlive || CombatRuntime.Instance.PeekMonsterCurrentHPWire(resolvedTarget) == 0)
                {
                    result.SkippedDead++;
                    if (pending.QueuedWithoutInitialTarget)
                        Debug.LogError($"[SPELL-PRE-SUFFIX] source={source ?? "unknown"} spell={pending.Spell?.DisplayName ?? pending.Spell?.SkillId ?? "UNKNOWN"} no-hit after projectile runtime aim=({pending.AimX:F1},{pending.AimY:F1}) due={pending.DueTime:F3} now={now:F3}");
                    continue;
                }

                uint beforeHP = CombatRuntime.Instance.PeekMonsterCurrentHPWire(resolvedTarget);
                HandleSpellAttack(
                    pending.Conn,
                    pending.State,
                    resolvedTarget,
                    pending.ManipId,
                    pending.UseFlags,
                    pending.AimX,
                    pending.AimY,
                    ResolvePendingSpellImpactTime(pending, now));
                uint afterHP = CombatRuntime.Instance.PeekMonsterCurrentHPWire(resolvedTarget);
                result.Applied++;
                Debug.LogError($"[SPELL-PRE-SUFFIX] source={source ?? "unknown"} target={resolvedTarget.Name}#{resolvedTarget.EntityId} behavior={resolvedTarget.BehaviorId} due={pending.DueTime:F3} now={now:F3} delay={pending.ProjectileDelay:F3} hitDist={pending.ProjectileHitDistance:F2} hp={beforeHP}->{afterHP} sameTarget={sameTarget} applied=True");
            }

            result.PendingAfter = _pendingSpells.Count;
            result.AfterHPWire = CombatRuntime.Instance.PeekMonsterCurrentHPWire(monster);
            if (result.PendingBefore > 0 || result.DueForTarget > 0 || result.DueOther > 0 || result.Applied > 0)
                Debug.LogError($"[SPELL-PRE-SUFFIX] source={source ?? "unknown"} target={monster.Name}#{monster.EntityId} pending={result.PendingBefore}->{result.PendingAfter} due={result.DueForTarget} dueOther={result.DueOther} applied={result.Applied} skippedDead={result.SkippedDead} future={result.RequeuedFuture} other={result.RequeuedOther} hp={result.BeforeHPWire}->{result.AfterHPWire} now={now:F3}");
            return result;
        }

        private Dictionary<uint, (RRConnection conn, Combat.Monster monster, string source, float time)> _pendingKills
            = new Dictionary<uint, (RRConnection, Combat.Monster, string, float)>();
    }
}
