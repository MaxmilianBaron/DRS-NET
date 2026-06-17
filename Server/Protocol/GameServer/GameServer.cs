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
    public enum EntitySynchInfoContext
    {
        Unknown,
        WorldInterval,
        BaselineReplay,
        RecoveryReplay,
        RepeatReplay,
        InventoryReplay,
        EquipmentReplay,
        LateArmorReplay,
        ControlGrant,
        ControlAck,
        MoverAck,
        PlayerActionResponse,
        PlayerBasicAttackResponse,
        MonsterAction,
        MonsterMove,
        MonsterDamage,
        EntityInitPrimer
    }

    public partial class GameServer : MonoBehaviour
    {
        private const uint MSG_DEST = 0x000F01;
        private const uint MSG_SOURCE = 0x000F01;
        private static bool VerbosePacketLogging => ServerSettings.GetBool("verbosePacketLogging", false);
        private bool _allowFlush = false;
        private UdpClient _udpListener;
        private bool _isRunning;
        private int _udpPort = 2603;
        private IPEndPoint _clientUDPEndpoint;
        private string GetEndpointKey(IPEndPoint ep) => $"{ep.Address}:{ep.Port}";
        private int _nextConnId = 1;
        private Dictionary<int, string> _users = new Dictionary<int, string>();
        private Dictionary<int, uint> _peerId24 = new Dictionary<int, uint>();
        private Dictionary<string, List<GCObject>> _persistentCharacters = new Dictionary<string, List<GCObject>>();
        private Dictionary<int, bool> _charListSent = new Dictionary<int, bool>();
        private Dictionary<string, GCObject> _selectedCharacter = new Dictionary<string, GCObject>();
        private readonly Dictionary<string, SavedCharacter> _activeCharacter = new Dictionary<string, SavedCharacter>(StringComparer.OrdinalIgnoreCase);
        private const ushort LOOT_ID_MIN = 0xC000;
        private const ushort LOOT_ID_MAX = 0xFDFF;
        private HashSet<string> _freePlayerModifierSent = new HashSet<string>();
        private ActiveModifiers _activeModifiers = new ActiveModifiers();

        private Dictionary<string, bool> _playerIsFree = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, bool> _playerIsAdmin = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, Dictionary<string, ushort>> _remoteBehaviorIds = new Dictionary<string, Dictionary<string, ushort>>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, Dictionary<string, ushort>> _remoteAvatarIds = new Dictionary<string, Dictionary<string, ushort>>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, Dictionary<string, ushort>> _remotePlayerIds = new Dictionary<string, Dictionary<string, ushort>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (ushort AvatarId, uint HPWire, uint ManaWire)> _remoteAvatarResourceState = new Dictionary<string, (ushort, uint, uint)>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, uint> _monsterEntityInitHPState = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<uint, byte> _groupMemberHealthManaState = new Dictionary<uint, byte>();
        private Dictionary<ushort, ChestSpawnData> _chestEntities = new Dictionary<ushort, ChestSpawnData>();

        private AdminCommands _adminCommands;
        private const float CLIENT_CONTACT_RANGE_EPSILON = 1f / 16f;
        private const float CLIENT_ACTION_TIMING_EPSILON = 0.15f;
        private const int StatPointsPerLevel = 5;
        private const float MONSTER_MOVE_SEND_INTERVAL = 0.15f;
        private const float MONSTER_MOVE_TARGET_THRESHOLD = 2.0f;
        private Dictionary<uint, uint> _monsterBehaviorIds = new Dictionary<uint, uint>();
        private Dictionary<uint, float> _lastMonsterMoveSentAt = new Dictionary<uint, float>();
        private Dictionary<uint, (float X, float Y)> _lastMonsterMoveTarget = new Dictionary<uint, (float, float)>();
        private Dictionary<uint, PendingMonsterBehaviorUpdate> _pendingMonsterBehaviorUpdates = new Dictionary<uint, PendingMonsterBehaviorUpdate>();
        private UnitContainer _unitContainer;
        private Equipment _equipment;
        private Dictionary<string, PlayerState> _playerStates = new Dictionary<string, PlayerState>();
        private Dictionary<string, Dictionary<uint, GCObject>> _playerEquippedItems = new Dictionary<string, Dictionary<uint, GCObject>>();
        private Dictionary<string, ushort> _playerManipulatorsIds = new Dictionary<string, ushort>();
        private Dictionary<string, float> _useTargetResponseTimes = new Dictionary<string, float>();
        private const float DROPPED_ITEM_CLEANUP_INTERVAL = 60f;
        private const int DROPPED_ITEM_EXPIRE_MINUTES = 30;
        private Dictionary<string, Dictionary<uint, (GCObject item, byte x, byte y)>> _playerInventoryItems = new Dictionary<string, Dictionary<uint, (GCObject, byte, byte)>>();
        private Dictionary<string, HashSet<int>> _occupiedInventorySlots = new Dictionary<string, HashSet<int>>();
        private Dictionary<string, uint> _inventorySlotCounters = new Dictionary<string, uint>();
        private const float TICK_INTERVAL = 0.033f;
        private const float AUTO_SAVE_INTERVAL = 30f;

        private float _merchantRefreshTimer = 0f;
        private const float MERCHANT_REFRESH_INTERVAL = 0.10f;
        private const float MERCHANT_ACTIVATION_DISTANCE_SQ = 900f;
        private float _groupHealthBroadcastTimer = 0f;
        private const float GROUP_HEALTH_BROADCAST_INTERVAL = 1.0f;

        private static bool IsBankContainer(byte containerId)
            => containerId == 0x0C || (containerId >= 0x0E && containerId <= 0x13);

        private static int ContainerHeight(byte containerId) => IsBankContainer(containerId) ? 14 : 8;
        private const int CONTAINER_WIDTH = 10;

        public void TrackInventoryItem(string connId, uint index, GCObject item, byte x, byte y, byte containerId = 0x0B)
        {
            string key = InvKey(connId, containerId);
            if (!_playerInventoryItems.ContainsKey(key))
                _playerInventoryItems[key] = new Dictionary<uint, (GCObject, byte, byte)>();
            _playerInventoryItems[key][index] = (item, x, y);
            Debug.LogError($"[INV-TRACK] Player {connId} container=0x{containerId:X2}: Index {index} = {item.GCClass} at ({x}, {y})");
        }

        public (int x, int y) FindNextFreeInventorySlot(string connId, int width, int height, byte containerId = 0x0B)
        {
            int invHeight = ContainerHeight(containerId);

            for (byte y = 0; y <= invHeight - height; y++)
            {
                for (byte x = 0; x <= CONTAINER_WIDTH - width; x++)
                {
                    if (!IsInventorySlotOccupied(connId, x, y, width, height, containerId))
                    {
                        return (x, y);
                    }
                }
            }
            return (-1, -1);
        }


        private Dictionary<string, Dictionary<uint, int>> _inventoryStackCounts = new Dictionary<string, Dictionary<uint, int>>();

        public int GetStackCount(string connId, uint slot, byte containerId = 0x0B)
        {
            string key = InvKey(connId, containerId);
            if (_inventoryStackCounts.ContainsKey(key) && _inventoryStackCounts[key].ContainsKey(slot))
                return _inventoryStackCounts[key][slot];
            return 1;
        }

        public void SetStackCount(string connId, uint slot, int count, byte containerId = 0x0B)
        {
            string key = InvKey(connId, containerId);
            if (!_inventoryStackCounts.ContainsKey(key))
                _inventoryStackCounts[key] = new Dictionary<uint, int>();
            _inventoryStackCounts[key][slot] = count;
        }

        public void RemoveEquippedItem(string connId, uint slot)
        {
            if (_playerEquippedItems.ContainsKey(connId) && _playerEquippedItems[connId].ContainsKey(slot))
            {
                _playerEquippedItems[connId].Remove(slot);
                Debug.LogError($"[EQUIP-TRACK] Removed item from slot {slot}");
            }
        }

        public uint GetNextInventorySlot(string playerId)
        {
            if (!_inventorySlotCounters.ContainsKey(playerId))
            {
                _inventorySlotCounters[playerId] = 100;
            }

            uint slot = _inventorySlotCounters[playerId];
            _inventorySlotCounters[playerId]++;

            Debug.LogError($"[INV-SLOT] Generated new inventory slot {slot} for player {playerId}");
            return slot;
        }







        public (GCObject item, byte x, byte y)? GetAndRemoveInventoryItem(string connId, uint index, byte containerId = 0x0B)
        {
            string key = InvKey(connId, containerId);
            if (_playerInventoryItems.ContainsKey(key) && _playerInventoryItems[key].ContainsKey(index))
            {
                var data = _playerInventoryItems[key][index];
                _playerInventoryItems[key].Remove(index);

                ItemData itemData = DatabaseLoader.FindItem(data.item.GCClass);
                int width = itemData?.inventoryWidth ?? 1;
                int height = itemData?.inventoryHeight ?? 1;

                FreeInventorySlots(connId, data.x, data.y, width, height, containerId);
                return data;
            }
            Debug.LogError($"[INV-TRACK]  No item at index {index} in container 0x{containerId:X2}!");
            return null;
        }





        public bool IsInventorySlotOccupied(string connId, byte x, byte y, int width, int height, byte containerId = 0x0B)
        {
            int invHeight = ContainerHeight(containerId);
            if (x + width > CONTAINER_WIDTH || y + height > invHeight)
                return true;

            string key = InvKey(connId, containerId);
            if (!_occupiedInventorySlots.ContainsKey(key))
                return false;

            for (int dx = 0; dx < width; dx++)
            {
                for (int dy = 0; dy < height; dy++)
                {
                    int slotIndex = (y + dy) * CONTAINER_WIDTH + (x + dx);
                    if (_occupiedInventorySlots[key].Contains(slotIndex))
                    {
                        Debug.LogError($"[INV-TRACK]  Cell ({x + dx}, {y + dy}) in container 0x{containerId:X2} is already occupied!");
                        return true;
                    }
                }
            }
            return false;
        }

        public void OccupyInventorySlots(string connId, byte x, byte y, int width, int height, byte containerId = 0x0B)
        {
            string key = InvKey(connId, containerId);
            if (!_occupiedInventorySlots.ContainsKey(key))
                _occupiedInventorySlots[key] = new HashSet<int>();

            for (int dx = 0; dx < width; dx++)
            {
                for (int dy = 0; dy < height; dy++)
                {
                    int slotIndex = (y + dy) * CONTAINER_WIDTH + (x + dx);
                    _occupiedInventorySlots[key].Add(slotIndex);
                }
            }
            Debug.LogError($"[INV-TRACK]  Occupied {width}x{height} slots starting at ({x}, {y}) in container 0x{containerId:X2}");
        }

        public void FreeInventorySlots(string connId, byte x, byte y, int width, int height, byte containerId = 0x0B)
        {
            string key = InvKey(connId, containerId);
            if (!_occupiedInventorySlots.ContainsKey(key))
                return;

            for (int dx = 0; dx < width; dx++)
            {
                for (int dy = 0; dy < height; dy++)
                {
                    int slotIndex = (y + dy) * CONTAINER_WIDTH + (x + dx);
                    _occupiedInventorySlots[key].Remove(slotIndex);
                }
            }
            Debug.LogError($"[INV-TRACK]  Freed {width}x{height} slots starting at ({x}, {y}) in container 0x{containerId:X2}");
        }

        public (uint slot, GCObject item, byte x, byte y)? FindInventoryItemByGCClass(string connId, string gcClass, byte containerId = 0x0B)
        {
            string key = InvKey(connId, containerId);
            if (!_playerInventoryItems.ContainsKey(key)) return null;
            string gcLower = gcClass.ToLower();
            foreach (var inventoryEntry in _playerInventoryItems[key])
            {
                if (inventoryEntry.Value.Item1.GCClass.ToLower() == gcLower)
                    return (inventoryEntry.Key, inventoryEntry.Value.Item1, inventoryEntry.Value.Item2, inventoryEntry.Value.Item3);
            }
            return null;
        }
        private void SavePlayerInventory(RRConnection conn)
        {
            if (conn == null || string.IsNullOrEmpty(conn.LoginName)) return;
            string connId = conn.ConnId.ToString();
            if (!_selectedCharacter.ContainsKey(conn.LoginName)) return;
            var savedChar = CharacterRepository.GetCharacter(_selectedCharacter[conn.LoginName].Id);
            if (savedChar == null) return;
            savedChar.inventory.Clear();

            byte[] saveContainers = { 0x0B, 0x0C, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13 };
            foreach (byte containerId in saveContainers)
            {
                string dictKey = InvKey(connId, containerId);
                if (!_playerInventoryItems.ContainsKey(dictKey)) continue;
                foreach (var inventoryEntry in _playerInventoryItems[dictKey])
                {
                    int count = GetStackCount(connId, inventoryEntry.Key, containerId);
                    var posConflict = savedChar.inventory.Find(i =>
                        i.containerId == containerId &&
                        i.x == inventoryEntry.Value.Item2 &&
                        i.y == inventoryEntry.Value.Item3);
                    if (posConflict != null)
                    {
                        if (count > posConflict.count)
                        {
                            posConflict.gcClass = inventoryEntry.Value.Item1.GCClass;
                            posConflict.count = count;
                        }
                        uint buyPriceConflict = DungeonRunners.Gameplay.MerchantRuntime.GetBuyPrice(connId, inventoryEntry.Value.Item1.GCClass);
                        if (buyPriceConflict > 0) posConflict.buyPrice = buyPriceConflict;
                    }
                    else
                    {
                        string buyPriceGcClass = inventoryEntry.Value.Item1.GCClass;
                        uint itemBuyPrice = DungeonRunners.Gameplay.MerchantRuntime.GetBuyPrice(connId, buyPriceGcClass);
                        savedChar.inventory.Add(new SavedInventoryItem
                        {
                            gcClass = buyPriceGcClass,
                            x = inventoryEntry.Value.Item2,
                            y = inventoryEntry.Value.Item3,
                            count = count,
                            buyPrice = itemBuyPrice,
                            rarity = inventoryEntry.Value.Item1.GetEffectiveRarity(),
                            storedLevel = inventoryEntry.Value.Item1.StoredLevel,
                            containerId = containerId
                        });
                    }
                }
            }

            PlayerState playerState = GetPlayerState(connId);
            if (playerState?.ActiveItem != null)
            {
                byte activeSlotX = 0, activeSlotY = 0;
                bool foundActive = false;
                int activeWidth = 1, activeHeight = 1;
                for (byte rowY = 0; rowY < 8 && !foundActive; rowY++)
                    for (byte columnX = 0; columnX < 10 && !foundActive; columnX++)
                        if (!IsInventorySlotOccupied(connId, columnX, rowY, activeWidth, activeHeight))
                        { activeSlotX = columnX; activeSlotY = rowY; foundActive = true; }
                if (foundActive)
                {
                    savedChar.inventory.Add(new SavedInventoryItem
                    {
                        gcClass = playerState.ActiveItem.GCClass,
                        x = activeSlotX,
                        y = activeSlotY,
                        count = 1,
                        rarity = playerState.ActiveItem.GetEffectiveRarity(),
                        storedLevel = playerState.ActiveItem.StoredLevel
                    });
                    Debug.LogError($"[SAVE] Saved cursor item {playerState.ActiveItem.GCClass} to inventory at ({activeSlotX},{activeSlotY})");
                }
                playerState.ActiveItem = null;
            }

            if (_playerEquippedItems.ContainsKey(connId))
            {
                var eq = _playerEquippedItems[connId];
                savedChar.equipment.weapon = eq.ContainsKey(10) ? eq[10].GCClass : "";
                savedChar.equipment.armor = eq.ContainsKey(6) ? eq[6].GCClass : "";
                savedChar.equipment.helmet = eq.ContainsKey(5) ? eq[5].GCClass : "";
                savedChar.equipment.gloves = eq.ContainsKey(2) ? eq[2].GCClass : "";
                savedChar.equipment.boots = eq.ContainsKey(7) ? eq[7].GCClass : "";
                savedChar.equipment.shoulders = eq.ContainsKey(8) ? eq[8].GCClass : "";
                savedChar.equipment.shield = eq.ContainsKey(11) ? eq[11].GCClass : "";
                savedChar.equipment.ring1 = eq.ContainsKey(3) ? eq[3].GCClass : "";
                savedChar.equipment.ring2 = eq.ContainsKey(4) ? eq[4].GCClass : "";
                savedChar.equipment.amulet = eq.ContainsKey(1) ? eq[1].GCClass : "";
                if (eq.ContainsKey(10)) savedChar.equipment.slotRarity["weapon"] = eq[10].GetEffectiveRarity();
                if (eq.ContainsKey(6)) savedChar.equipment.slotRarity["armor"] = eq[6].GetEffectiveRarity();
                if (eq.ContainsKey(5)) savedChar.equipment.slotRarity["helmet"] = eq[5].GetEffectiveRarity();
                if (eq.ContainsKey(2)) savedChar.equipment.slotRarity["gloves"] = eq[2].GetEffectiveRarity();
                if (eq.ContainsKey(7)) savedChar.equipment.slotRarity["boots"] = eq[7].GetEffectiveRarity();
                if (eq.ContainsKey(8)) savedChar.equipment.slotRarity["shoulders"] = eq[8].GetEffectiveRarity();
                if (eq.ContainsKey(11)) savedChar.equipment.slotRarity["shield"] = eq[11].GetEffectiveRarity();
                if (eq.ContainsKey(3)) savedChar.equipment.slotRarity["ring1"] = eq[3].GetEffectiveRarity();
                if (eq.ContainsKey(4)) savedChar.equipment.slotRarity["ring2"] = eq[4].GetEffectiveRarity();
                if (eq.ContainsKey(1)) savedChar.equipment.slotRarity["amulet"] = eq[1].GetEffectiveRarity();
                if (eq.ContainsKey(10)) savedChar.equipment.slotLevel["weapon"] = eq[10].StoredLevel;
                if (eq.ContainsKey(6)) savedChar.equipment.slotLevel["armor"] = eq[6].StoredLevel;
                if (eq.ContainsKey(5)) savedChar.equipment.slotLevel["helmet"] = eq[5].StoredLevel;
                if (eq.ContainsKey(2)) savedChar.equipment.slotLevel["gloves"] = eq[2].StoredLevel;
                if (eq.ContainsKey(7)) savedChar.equipment.slotLevel["boots"] = eq[7].StoredLevel;
                if (eq.ContainsKey(8)) savedChar.equipment.slotLevel["shoulders"] = eq[8].StoredLevel;
                if (eq.ContainsKey(11)) savedChar.equipment.slotLevel["shield"] = eq[11].StoredLevel;
                if (eq.ContainsKey(3)) savedChar.equipment.slotLevel["ring1"] = eq[3].StoredLevel;
                if (eq.ContainsKey(4)) savedChar.equipment.slotLevel["ring2"] = eq[4].StoredLevel;
                if (eq.ContainsKey(1)) savedChar.equipment.slotLevel["amulet"] = eq[1].StoredLevel;
                Debug.LogError($"[SAVE] Equipment saved: weapon={savedChar.equipment.weapon}, armor={savedChar.equipment.armor}");
            }

            savedChar.currentZoneName = conn.CurrentZoneName ?? "tutorial";
            savedChar.zoneId = (int)conn.CurrentZoneId;
            CharacterRepository.SaveCharacter(savedChar);
            Debug.LogError($"[SAVE] Saved {savedChar.inventory.Count} inventory items for {conn.LoginName}");
        }



        public void SavePlayerInventoryPublic(RRConnection conn)
        {
            SavePlayerInventory(conn);
        }

        public void SavePlayerLevelPublic(RRConnection conn)
        {
            SavePlayerLevel(conn);
        }

        private void RemoveQuestItemsFromInventory(RRConnection conn, ActiveQuest completingQuest)
        {
            if (completingQuest == null) return;
            string connId = conn.ConnId.ToString();

            foreach (var obj in completingQuest.Objectives)
            {
                if (obj.Type == null || !obj.Type.Equals("item", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.IsNullOrEmpty(obj.Target))
                    continue;

                int remainingToRemove = Math.Max(1, obj.Required);
                Debug.LogError($"[QUEST-ITEM-REMOVE] Looking for: {obj.Target} ({obj.Label}) x{remainingToRemove}");

                if (!_playerInventoryItems.ContainsKey(connId))
                {
                    Debug.LogError($"[QUEST-ITEM-REMOVE] No inventory tracked for {connId}");
                    continue;
                }

                var matchingSlots = new List<uint>();
                foreach (var inventoryEntry in _playerInventoryItems[connId])
                {
                    if (inventoryEntry.Value.item != null &&
                        inventoryEntry.Value.item.GCClass != null &&
                        inventoryEntry.Value.item.GCClass.Equals(obj.Target, StringComparison.OrdinalIgnoreCase))
                    {
                        matchingSlots.Add(inventoryEntry.Key);
                    }
                }

                if (matchingSlots.Count == 0)
                {
                    Debug.LogError($"[QUEST-ITEM-REMOVE] NOT FOUND in inventory! Dumping all slots:");
                    foreach (var inventoryEntry in _playerInventoryItems[connId])
                        Debug.LogError($"  slot {inventoryEntry.Key}: {inventoryEntry.Value.item?.GCClass ?? "NULL"}");
                    continue;
                }

                foreach (uint slotId in matchingSlots)
                {
                    if (remainingToRemove <= 0) break;

                    int stack = GetStackCount(connId, slotId);
                    if (stack <= 0) stack = 1;

                    if (stack > remainingToRemove)
                    {
                        int newCount = stack - remainingToRemove;
                        SetStackCount(connId, slotId, newCount);
                        remainingToRemove = 0;

                        if (conn.UnitContainerId != 0)
                        {
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
                        }
                        Debug.LogError($"[QUEST-ITEM-REMOVE] decremented slot {slotId}: {stack} -> {newCount}");
                    }
                    else
                    {
                        remainingToRemove -= stack;
                        GetAndRemoveInventoryItem(connId, slotId);

                        if (conn.UnitContainerId != 0)
                        {
                            var writer = new LEWriter();
                            writer.WriteByte(0x07);
                            writer.WriteByte(0x35);
                            writer.WriteUInt16(conn.UnitContainerId);
                            writer.WriteByte(0x1F);
                            writer.WriteUInt32(slotId);
                            if (!TryWriteEntitySynchForComponent(conn, writer, conn.UnitContainerId, 0x1F, EntitySynchInfoContext.PlayerActionResponse, "QUEST-ITEM-REMOVE", true))
                                continue;
                            writer.WriteByte(0x06);
                            SendCompressedA(conn, 0x01, 0x0F, writer.ToArray());
                        }
                        Debug.LogError($"[QUEST-ITEM-REMOVE] consumed full stack at slot {slotId} (was {stack})");
                    }
                }

                if (remainingToRemove > 0)
                {
                    Debug.LogError($"[QUEST-ITEM-REMOVE]  underflowed: {remainingToRemove}x {obj.Target} still needed but inventory exhausted");
                }
                SavePlayerInventory(conn);
                Debug.LogError($"[QUEST-ITEM-REMOVE]  done for {obj.Target}");
            }
        }


        private Dictionary<int, float> _gotoNextCheckTime = new Dictionary<int, float>();
        private int _udpPacketCount = 0;
        private static readonly byte[] NULL_PRIVKEY = System.Text.Encoding.ASCII.GetBytes("PRIVKEY!");
        private byte[] _udpBlowfishKey;

        private void SendUDPSynAck(IPEndPoint clientEndpoint, byte[] clientSyn)
        {
            try
            {
                byte[] generator = new byte[64];
                generator[0] = 0x02;

                byte[] prime = new byte[128];
                for (int byteIndex = 0; byteIndex < prime.Length; byteIndex++) prime[byteIndex] = 0xFF;

                var synAckPayload = new System.IO.MemoryStream();
                var synAckWriter = new System.IO.BinaryWriter(synAckPayload);

                synAckWriter.Write((byte)0x03);
                synAckWriter.Write(clientSyn.Length > 1 ? clientSyn[1] : (byte)0x01);
                synAckWriter.Write((ushort)0);
                synAckWriter.Write((uint)1);

                synAckWriter.Write((ushort)generator.Length);
                synAckWriter.Write(generator);

                synAckWriter.Write((ushort)prime.Length);
                synAckWriter.Write(prime);

                synAckWriter.Write((ushort)NULL_PUBKEY.Length);
                synAckWriter.Write(NULL_PUBKEY);

                byte[] packet = synAckPayload.ToArray();
                _udpListener.Send(packet, packet.Length, clientEndpoint);

                string epKey = GetEndpointKey(clientEndpoint);
                var session = new UDPSession { Endpoint = clientEndpoint, IsEstablished = false };

                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    byte[] blowfishKey = md5.ComputeHash(NULL_PRIVKEY);
                    session.InitializeCipher(blowfishKey);
                    _udpBlowfishKey = blowfishKey;
                    Debug.LogError($"[UDP] Blowfish Key: {BitConverter.ToString(blowfishKey)}");
                }

                _udpSessions[epKey] = session;
                Debug.LogError($"[UDP] Sent SYN-ACK with PUBKEY12, session created for {epKey}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UDP] sendUDPSynAck state=failed message='{ex.Message}'");
            }
        }

        private string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    string ipStr = ip.ToString();
                    if (ipStr.StartsWith("10."))
                    {
                        return ipStr;
                    }
                }
            }
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            return "127.0.0.1";
        }

        private void StopServer()
        {
            _isRunning = false;
            _listener?.Stop();
            _udpListener?.Close();
            foreach (var conn in _connections.Values)

            {
                conn.Disconnect();
            }
            _connections.Clear();
            Debug.Log("Game Server stopped");
        }

        private IEnumerator PollPendingItemGrants()
        {
            yield return new WaitForSeconds(5f);
            while (_isRunning)
            {
                try { ProcessPendingGrants(); }
                catch (Exception ex) { Debug.LogError($"[GRANTS] state=failed message='{ex.Message}'"); }
                try { ProcessPendingAdminActions(); }
                catch (Exception ex) { Debug.LogError($"[ADMIN-ACT] state=failed message='{ex.Message}'"); }
                yield return new WaitForSeconds(5f);
            }
        }

        private void ProcessPendingAdminActions()
        {
            using (var connection = GameDatabase.GetConnection())
            {
                object tbl = GameDatabase.ExecuteScalar(connection, "SELECT name FROM sqlite_master WHERE type='table' AND name='pending_admin_actions'");
                if (tbl == null) return;

                var actions = new List<(int id, int charId, string actionType, int value)>();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT id, character_id, action_type, value FROM pending_admin_actions ORDER BY id";
                    using (var reader = command.ExecuteReader())
                        while (reader.Read())
                            actions.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3)));
                }
                if (actions.Count == 0) return;

                foreach (var action in actions)
                {
                    try
                    {
                        RRConnection onlineConn = null;
                        string loginName = null;
                        foreach (var selectedCharacterEntry in _selectedCharacter)
                            if (selectedCharacterEntry.Value.Id == action.charId) { loginName = selectedCharacterEntry.Key; break; }
                        if (loginName != null)
                            foreach (var connectionEntry in _connections)
                                if (connectionEntry.Value.LoginName == loginName) { onlineConn = connectionEntry.Value; break; }

                        if (action.actionType == "gold" && onlineConn != null && onlineConn.UnitContainerId != 0)
                        {
                            using (var goldConnection = GameDatabase.GetConnection())
                            {
                                if (action.value > 0)
                                {
                                    GameDatabase.ExecuteNonQuery(goldConnection,
                                        "UPDATE characters SET gold = gold + @amt WHERE id = @id",
                                        ("@amt", action.value), ("@id", action.charId));
                                    var goldActionMessage = new LEWriter();
                                    goldActionMessage.WriteByte(0x07);
                                    goldActionMessage.WriteByte(0x35);
                                    goldActionMessage.WriteUInt16(onlineConn.UnitContainerId);
                                    goldActionMessage.WriteByte(0x20);
                                    goldActionMessage.WriteUInt32((uint)action.value);
                                    goldActionMessage.WriteByte(0x00);
                                    goldActionMessage.WriteUInt32(0x00000000);
                                    goldActionMessage.WriteByte(0x01);
                                    goldActionMessage.WriteByte(0x02);
                                    goldActionMessage.WriteUInt32(0x00000000);
                                    goldActionMessage.WriteByte(0x06);
                                    SendToClient(onlineConn, goldActionMessage.ToArray());
                                }
                                else if (action.value < 0)
                                {
                                    GameDatabase.ExecuteNonQuery(goldConnection,
                                        "UPDATE characters SET gold = MAX(0, gold + @amt) WHERE id = @id",
                                        ("@amt", action.value), ("@id", action.charId));
                                    var goldDebitMessage = new LEWriter();
                                    goldDebitMessage.WriteByte(0x07);
                                    goldDebitMessage.WriteByte(0x35);
                                    goldDebitMessage.WriteUInt16(onlineConn.UnitContainerId);
                                    goldDebitMessage.WriteByte(0x20);
                                    goldDebitMessage.WriteInt32(action.value);
                                    goldDebitMessage.WriteByte(0x00);
                                    goldDebitMessage.WriteUInt32(0x00000000);
                                    goldDebitMessage.WriteByte(0x01);
                                    WritePlayerEntitySynch(onlineConn, goldDebitMessage);
                                    goldDebitMessage.WriteByte(0x06);
                                    SendCompressedA(onlineConn, 0x01, 0x0F, goldDebitMessage.ToArray());
                                }
                                var savedCharacter = GetSavedCharacterForConn(onlineConn);
                                if (savedCharacter != null) savedCharacter.gold = (uint)Math.Max(0, (int)savedCharacter.gold + action.value);
                            }
                            Debug.LogError($"[ADMIN-ACT]  Gold {(action.value > 0 ? "+" : "")}{action.value} for char {action.charId}");
                        }
                        else if (action.actionType == "level" && onlineConn != null)
                        {
                            string connId = onlineConn.ConnId.ToString();
                            PlayerState levelPlayerState = GetPlayerState(connId);
                            var savedChar = GetSavedCharacterForConn(onlineConn);
                            if (savedChar != null && levelPlayerState != null)
                            {
                                int oldLevel = levelPlayerState.Level;
                                int targetLevel = action.value;
                                byte persistedTargetLevel = SavedCharacterLevel.ResolvePersistedLevel(targetLevel);
                                using (var levelConnection = GameDatabase.GetConnection())
                                    GameDatabase.ExecuteNonQuery(levelConnection,
                                        "UPDATE characters SET level=@lv, experience=0 WHERE id=@id",
                                        ("@lv", persistedTargetLevel), ("@id", action.charId));
                                savedChar.level = persistedTargetLevel;
                                savedChar.experience = 0;
                                levelPlayerState.InitializeStats(savedChar.className ?? "Fighter", targetLevel);
                                levelPlayerState.Experience = 0;
                                if (onlineConn.Avatar != null)
                                    CalculateEquipmentBonuses(connId, onlineConn.Avatar);
                                levelPlayerState.RestoreToFull();

                                if (targetLevel > oldLevel)
                                {
                                    for (int lv = oldLevel; lv < targetLevel; lv++)
                                    {
                                        uint threshold = PlayerState.GetClientThreshold(lv + 1);
                                        uint packetXP = threshold * 256 / 5 + 100;
                                        SendAdminXPUpdate(onlineConn, packetXP, (uint)lv);
                                    }
                                }
                                SendAdminEntitySynchInfoHP(onlineConn, levelPlayerState);
                                Debug.LogError($"[ADMIN-ACT]  Level {oldLevel} -> {targetLevel} for char {action.charId}");
                            }
                        }
                        if (action.actionType == "xp" && onlineConn != null)
                        {
                            var savedChar = GetSavedCharacterForConn(onlineConn);
                            if (savedChar != null)
                            {
                                savedChar.experience = (uint)action.value;
                                string connId = onlineConn.ConnId.ToString();
                                PlayerState xpPlayerState = GetPlayerState(connId);
                                if (xpPlayerState != null) xpPlayerState.Experience = (uint)action.value;
                                Debug.LogError($"[ADMIN-ACT]  XP memory updated to {action.value} for char {action.charId}");
                            }
                        }
                        else if (action.actionType == "xp" && onlineConn == null)
                        {
                        }

                        GameDatabase.ExecuteNonQuery(connection, "DELETE FROM pending_admin_actions WHERE id=@id", ("@id", action.id));
                    }
                    catch (Exception ex) { Debug.LogError($"[ADMIN-ACT] Failed action {action.id}: {ex.Message}"); }
                }
            }
        }

        private void ProcessPendingGrants()
        {
            using (var connection = GameDatabase.GetConnection())
            {
                var grants = new List<(int id, int charId, string gcClass, int count, int width, int height, int rarity)>();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT id, character_id, gc_class, count, width, height, rarity FROM pending_item_grants ORDER BY id";
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                            grants.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2),
                                reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6)));
                    }
                }
                if (grants.Count == 0) return;

                foreach (var grant in grants)
                {
                    try
                    {
                        RRConnection onlineConn = null;
                        string loginName = null;
                        foreach (var selectedCharacterEntry in _selectedCharacter)
                        {
                            if (selectedCharacterEntry.Value.Id == grant.charId) { loginName = selectedCharacterEntry.Key; break; }
                        }
                        if (loginName != null)
                        {
                            foreach (var connectionEntry in _connections)
                            {
                                if (connectionEntry.Value.LoginName == loginName) { onlineConn = connectionEntry.Value; break; }
                            }
                        }

                        if (onlineConn != null && onlineConn.UnitContainerId != 0)
                        {
                            string connId = onlineConn.ConnId.ToString();
                            string gcTypeFull = grant.gcClass;
                            if (!gcTypeFull.StartsWith("items.", StringComparison.OrdinalIgnoreCase))
                                gcTypeFull = "items.pal." + gcTypeFull;
                            int itemWidth = grant.width, itemHeight = grant.height;
                            try
                            {
                                using (var dimensionConnection = GameDatabase.GetConnection())
                                using (var dimReader = GameDatabase.ExecuteReader(dimensionConnection,
                                    "SELECT gc_type, width, height FROM item_dimensions WHERE LOWER(gc_type) = LOWER(@g)", ("@g", gcTypeFull)))
                                { if (dimReader.Read()) { gcTypeFull = dimReader.GetString(0); itemWidth = dimReader.GetInt32(1); itemHeight = dimReader.GetInt32(2); } }
                            }
                            catch { }

                            string packetGcType = gcTypeFull.ToLowerInvariant();
                            if (packetGcType.StartsWith("items.pal.")) packetGcType = packetGcType.Substring(10);

                            byte slotX = 0, slotY = 0; bool foundSlot = false;
                            for (byte rowY = 0; rowY < 8 && !foundSlot; rowY++)
                                for (byte columnX = 0; columnX < 10 && !foundSlot; columnX++)
                                    if (!IsInventorySlotOccupied(connId, columnX, rowY, itemWidth, itemHeight))
                                    { slotX = columnX; slotY = rowY; foundSlot = true; }
                            if (!foundSlot) { Debug.LogError($"[GRANTS] Inventory full char {grant.charId}"); continue; }

                            int modSlots = 1;
                            if (DungeonRunners.Gameplay.MerchantRuntime.TryGetAuthoredMerchantModSlots(packetGcType, out int authoredModSlots))
                                modSlots = authoredModSlots;

                            uint slot = GetNextInventorySlot(connId);
                            PlayerState playerState = GetPlayerState(connId);

                            byte itemLevel = playerState != null ? (byte)Math.Max(1, Math.Min(playerState.Level, 100)) : (byte)1;

                            var grantItemMessage = new LEWriter();
                            grantItemMessage.WriteByte(0x07);
                            grantItemMessage.WriteByte(0x35);
                            grantItemMessage.WriteUInt16(onlineConn.UnitContainerId);
                            grantItemMessage.WriteByte(0x1E);
                            grantItemMessage.WriteByte(0x0B);
                            grantItemMessage.WriteByte(0xFF);
                            grantItemMessage.WriteCString(packetGcType);
                            grantItemMessage.WriteUInt32(slot);
                            grantItemMessage.WriteByte(slotX);
                            grantItemMessage.WriteByte(slotY);
                            grantItemMessage.WriteByte((byte)grant.count);
                            grantItemMessage.WriteByte(itemLevel);
                            for (int modIndex = 0; modIndex < modSlots; modIndex++)
                                grantItemMessage.WriteByte(0x00);
                            grantItemMessage.WriteByte(0x01);
                            grantItemMessage.WriteByte(0xFF);
                            grantItemMessage.WriteCString("ScaleModPAL.Rare.Mod1");
                            grantItemMessage.WriteByte(0x03);
                            grantItemMessage.WriteByte(0x15);
                            grantItemMessage.WriteUInt32(0x11111111);
                            WritePlayerEntitySynch(onlineConn, grantItemMessage);
                            grantItemMessage.WriteByte(0x06);

                            Debug.LogError($"[GRANTS] Sending: gc={packetGcType} modSlots={modSlots} slot={slot} pos=({slotX},{slotY})");
                            SendToClient(onlineConn, grantItemMessage.ToArray());

                            string packetGcTypeLower = packetGcType.ToLowerInvariant();
                            string dfcClass = "Armor";
                            if (packetGcTypeLower.Contains("consumable") || packetGcTypeLower.Contains("questitem") || packetGcTypeLower.Contains("ring") || packetGcTypeLower.Contains("amulet") || packetGcTypeLower.Contains("scroll") || packetGcTypeLower.Contains("potion") || packetGcTypeLower.Contains("skillbook") || packetGcTypeLower.Contains("voucher"))
                                dfcClass = "Item";
                            else if (packetGcTypeLower.Contains("sword") || packetGcTypeLower.Contains("axe") || packetGcTypeLower.Contains("mace") || packetGcTypeLower.Contains("dagger") || packetGcTypeLower.Contains("hammer") || packetGcTypeLower.Contains("staff") || packetGcTypeLower.Contains("spear") || packetGcTypeLower.Contains("pick") || packetGcTypeLower.Contains("club") || packetGcTypeLower.Contains("scepter") || packetGcTypeLower.Contains("wand"))
                                dfcClass = "MeleeWeapon";
                            else if (packetGcTypeLower.Contains("bow") || packetGcTypeLower.Contains("cannon") || packetGcTypeLower.Contains("crossbow") || packetGcTypeLower.Contains("xbow") || packetGcTypeLower.Contains("gun"))
                                dfcClass = "RangedWeapon";

                            var newItem = new DungeonRunners.Data.GCObject { GCClass = packetGcType, DFCClass = dfcClass };
                            newItem.StoredRarity = grant.rarity;
                            newItem.StoredLevel = itemLevel;
                            TrackInventoryItem(connId, slot, newItem, slotX, slotY);
                            OccupyInventorySlots(connId, slotX, slotY, itemWidth, itemHeight);
                            SetStackCount(connId, slot, grant.count);
                            SavePlayerInventoryPublic(onlineConn);

                            Debug.LogError($"[GRANTS]  LIVE delivered {packetGcType} x{grant.count} modSlots={modSlots}");
                            GameDatabase.ExecuteNonQuery(connection, "DELETE FROM pending_item_grants WHERE id=@id", ("@id", grant.id));
                            continue;
                        }

                        {
                            var occupied = new HashSet<string>();
                            using (var inventoryCommand = connection.CreateCommand())
                            {
                                inventoryCommand.CommandText = "SELECT slot_x, slot_y FROM character_inventory WHERE character_id=@cid";
                                inventoryCommand.Parameters.AddWithValue("@cid", grant.charId);
                                using (var reader = inventoryCommand.ExecuteReader())
                                    while (reader.Read()) occupied.Add($"{reader.GetInt32(0)},{reader.GetInt32(1)}");
                            }

                            int freeSlotX = -1, freeSlotY = -1;
                            for (int slotY = 0; slotY <= 8 - grant.height && freeSlotY < 0; slotY++)
                                for (int slotX = 0; slotX <= 10 - grant.width && freeSlotX < 0; slotX++)
                                {
                                    bool fits = true;
                                    for (int widthOffset = 0; widthOffset < grant.width && fits; widthOffset++)
                                        for (int heightOffset = 0; heightOffset < grant.height && fits; heightOffset++)
                                            if (occupied.Contains($"{slotX + widthOffset},{slotY + heightOffset}")) fits = false;
                                    if (fits) { freeSlotX = slotX; freeSlotY = slotY; }
                                }

                            if (freeSlotX < 0) { Debug.LogError($"[GRANTS] Inventory full (offline) char {grant.charId}"); continue; }

                            GameDatabase.ExecuteNonQuery(connection,
                                "INSERT INTO character_inventory (character_id,gc_class,slot_x,slot_y,count,rarity,stored_level) VALUES(@cid,@gc,@x,@y,@n,@r,-1)",
                                ("@cid", grant.charId), ("@gc", grant.gcClass), ("@x", freeSlotX), ("@y", freeSlotY), ("@n", grant.count), ("@r", grant.rarity));
                            Debug.LogError($"[GRANTS]  OFFLINE inserted {grant.gcClass} x{grant.count} to char {grant.charId} at ({freeSlotX},{freeSlotY})");
                        }

                        GameDatabase.ExecuteNonQuery(connection, "DELETE FROM pending_item_grants WHERE id=@id", ("@id", grant.id));
                    }
                    catch (Exception ex) { Debug.LogError($"[GRANTS] Failed grant {grant.id}: {ex.Message}"); }
                }
            }
        }

        private IEnumerator AcceptClientsCoroutine()
        {
            while (_isRunning)
            {
                if (_listener.Pending())
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    var endpoint = (System.Net.IPEndPoint)client.Client.RemoteEndPoint;
                    string remoteIP = endpoint.Address.IsIPv4MappedToIPv6
                        ? endpoint.Address.MapToIPv4().ToString()
                        : endpoint.Address.ToString();
                    Debug.LogError($"[GAME-PORT] Connection from {remoteIP}:{endpoint.Port}");

                    string queueUser = QueueConnection.CheckAndConsumeQueueIP(remoteIP);
                    if (queueUser != null)
                    {
                        Debug.LogError($"[QUEUE]  Queue connection from {remoteIP} for {queueUser} on GAME port");
                    }

                    int connId = _nextConnId++;
                    NetworkStream stream = client.GetStream();
                    var conn = new RRConnection(connId, client, stream);
                    if (queueUser != null)
                        conn.LoginName = queueUser;
                    _connections[connId] = conn;
                    Debug.Log($"Client {connId} connected from {client.Client.RemoteEndPoint}");
                    InitiateUDPToClient(conn);
                    StartCoroutine(HandleClientCoroutine(conn));
                }
                yield return null;
            }
        }

        private IEnumerator HandleClientCoroutine(RRConnection conn)
        {
            if (conn.LoginName != null)
            {
                LoadAccountFlags(conn.LoginName);
            }

            byte[] buffer = new byte[8192];
            while (_isRunning && conn.Client.Connected)
            {
                try
                {
                    if (conn.Client.Client.Poll(0, System.Net.Sockets.SelectMode.SelectRead) && !conn.Stream.DataAvailable)
                    {
                        Debug.LogError($"[CLIENT] Connection {conn.ConnId} dead (poll detected)");
                        break;
                    }

                    if (conn.Stream.DataAvailable)
                    {
                        int bytesRead = conn.Stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            byte[] data = new byte[bytesRead];
                            Array.Copy(buffer, data, bytesRead);
                            ProcessMessage(conn, data);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[GAME-SERVER] handleClient conn={conn.ConnId} state=failed message='{ex.Message}' stack='{ex.StackTrace}'");
                }
                yield return null;
            }
            Debug.Log($"Client {conn.ConnId} disconnected");
            QueueConnection.PlayerDisconnected();
            SavePlayerInventory(conn);
            SavePlayerQuests(conn);
            SavePlayerLevel(conn);
            TouchSoloDungeonInstance(conn, "disconnect");

            BlingGnomeRuntime.Instance.OnPlayerDisconnect(conn.ConnId);

            if (conn.LoginName != null)
                _activeModifiers.Clear(conn.LoginName);

            if (conn.LoginName != null)
                _debuffCooldowns.Remove(conn.LoginName);

            if (conn.LoginName != null)
            {
                _freePlayerModifierSent.Remove(conn.LoginName);
                _freePlayerModifierSent.Remove(conn.LoginName + "_sent");
            }

            if (conn.LoginName != null)
            {
                try
                {
                    using (var dbConn = GameDatabase.GetConnection())
                        GameDatabase.ExecuteNonQuery(dbConn,
                            "UPDATE accounts SET current_character_id=0 WHERE username=@u COLLATE NOCASE",
                            ("@u", conn.LoginName));
                }
                catch (System.Exception ex) { Debug.LogError($"[SAVE] clear current_character_id failed for {conn.LoginName}: {ex.Message}"); }
            }

            if (conn.LoginName != null && _selectedCharacter.ContainsKey(conn.LoginName))
            {
                var logoffChar = GetActiveCharacter(conn);
                if (logoffChar != null)
                {
                    logoffChar.tpZone = "";
                    logoffChar.tpZoneId = 0;
                    logoffChar.tpTargetZone = "";
                    logoffChar.tpPosX = 0;
                    logoffChar.tpPosY = 0;
                    logoffChar.tpPosZ = 0;
                    CharacterRepository.SaveCharacter(logoffChar);
                    Debug.LogError($"[DISCONNECT] Cleared town portal from DB");
                }
                InvalidateActiveCharacter(conn);
            }

            if (conn.LoginName != null)
            {
                string dropperName = conn.LoginName;
                List<ushort> toRemove = new List<ushort>();

                foreach (var droppedItemEntry in _droppedItems)
                {
                    if (droppedItemEntry.Value.DroppedBy == dropperName)
                        toRemove.Add(droppedItemEntry.Key);
                }

                foreach (ushort entityId in toRemove)
                {
                    var info = _droppedItems[entityId];

                    foreach (var other in _connections.Values)
                    {
                        if (other == conn) continue;
                        if (!other.IsSpawned) continue;
                        if (other.CurrentZoneName != info.Zone) continue;
                        if (other.InstanceId != info.InstanceId) continue;
                        SendDespawnEntity(other, entityId);
                    }

                    if (info.DbId > 0)
                    {
                        try
                        {
                            using (var dropDatabase = DungeonRunners.Database.GameDatabase.GetConnection())
                            {
                                DungeonRunners.Database.GameDatabase.ExecuteNonQuery(dropDatabase,
                                    "DELETE FROM dropped_items WHERE id = @id",
                                    ("@id", info.DbId));
                            }
                            _dbIdToEntityId.Remove(info.DbId);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[DISCONNECT]  Failed to delete dropped item dbId={info.DbId}: {ex.Message}");
                        }
                    }

                    _droppedItems.Remove(entityId);
                }

                if (toRemove.Count > 0)
                    Debug.LogError($"[DISCONNECT] Removed {toRemove.Count} dropped items for {dropperName}");
            }

            var group = GroupDirectory.Instance.GetGroupForConn(conn.ConnId);
            if (group != null)
            {
                var groupMember = group.Members.Find(member => member.ConnId == conn.ConnId);
                if (groupMember != null)
                {
                    groupMember.CharSqlId = GetCharSqlId(conn);
                    groupMember.AvatarEntityId = GetPlayerAvatarId(conn.LoginName);
                    if (_selectedCharacter.TryGetValue(conn.LoginName, out var selectedCharacter))
                        groupMember.Name = selectedCharacter.Name ?? groupMember.Name;
                }

                uint disconnectedCharId = GetCharSqlId(conn);

                if (GroupDirectory.Instance.DisconnectMember(conn.ConnId))
                {
                    if (group.Members.Any(member => member.IsOnline))
                    {
                        byte[] disconnectPacket = GroupPackets.BuildMemberDisconnected(group.GroupId, disconnectedCharId);
                        foreach (var remainingMember in group.Members)
                        {
                            if (!remainingMember.IsOnline) continue;
                            var remainingMemberConnection = FindConnectionById(remainingMember.ConnId);
                            if (remainingMemberConnection != null)
                                SendToClient(remainingMemberConnection, disconnectPacket);
                        }

                        SendGroupConnectedToAll(group);

                        Debug.LogError($"[GROUP] Sent 0x49+0x35 for disconnect: {conn.LoginName} charId=0x{disconnectedCharId:X8}");
                    }
                    else
                    {
                        foreach (var member in group.Members.ToList())
                            GroupDirectory.Instance.LeaveGroup(member.ConnId);
                    }
                }
                conn.GroupConnectedSent = false;
            }

            if (conn.IsSpawned)
            {
                BroadcastEntityRemove(conn, conn.CurrentZoneGcType);
                conn.IsSpawned = false;
            }
            _monsterSpawnSentByConn.Remove(conn.ConnId);
            _encounterObjectSentByConn.Remove(conn.ConnId);
            ReassignMonsterOwnership(conn);

            string disconnKey = conn.ConnId.ToString();
            ClearUseTarget(conn);
            Combat.WeaponUseRuntime.Instance.ClearConnection(disconnKey);
            if (conn.Avatar != null)
                CombatRuntime.Instance.UnregisterPlayer((uint)conn.Avatar.Id);
            else if (_playerAvatarEntityId.TryGetValue(disconnKey, out uint oldAvatarId))
                CombatRuntime.Instance.UnregisterPlayer(oldAvatarId);
            Debug.LogError($"[COMBAT-LIFECYCLE] disconnect cleared combat state conn={conn.ConnId} avatar={(conn.Avatar != null ? conn.Avatar.Id : 0)}");
            _playerAvatarEntityId.Remove(disconnKey);
            _playerNextSkillEntityId.Remove(disconnKey);
            _playerSkillsComponentId.Remove(disconnKey);
            _playerSkillSlots.Remove(disconnKey);
            _encounterObjectSentByConn.Remove(conn.ConnId);
            _monsterSpawnSentByConn.Remove(conn.ConnId);
            CancelTradeOnDisconnect(conn);
            _connections.Remove(conn.ConnId);

            try
            {
                var forfeited = Gameplay.PVPMatchmaking.Instance.HandleDisconnect(conn.LoginName);
                if (forfeited != null) FinalizeMatchResults(forfeited);
            }
            catch (Exception ex) { Debug.LogError($"[PVP] disconnect cleanup: {ex.Message}"); }

            if (conn.LoginName != null && _selectedCharacter.TryGetValue(conn.LoginName, out var disconnChar))
            {
                SocialRuntime.Instance.PlayerOffline(conn.LoginName, disconnChar.Name, SendSocialViaAuth);
                try { PosseRuntime.Instance.NotifyMemberStateChange(disconnChar.Id, this); }
                catch (Exception ex) { Debug.LogError($"[POSSE] disconnect notify failed: {ex.Message}"); }
            }
            else if (conn.LoginName != null)
            {
                SocialRuntime.Instance.ForceRemoveOnline(conn.LoginName);
            }
            InvalidateActiveCharacter(conn);
            QueueConnection.RemoveQueueStream(conn.LoginName);

            conn.Disconnect();
        }

        private void ProcessMessage(RRConnection conn, byte[] data)
        {
            int offset = 0;

            while (offset < data.Length)
            {
                if (data.Length - offset < 1) break;

                byte messageType = data[offset];
                int messageLen = CalculateMessageLength(data, offset, messageType);

                if (messageLen <= 0 || offset + messageLen > data.Length)
                {
                    Debug.LogError($"[MSG-LOOP] Invalid message length {messageLen} at offset {offset}, remaining {data.Length - offset}");
                    break;
                }

                byte[] singleMessage = new byte[messageLen];
                Array.Copy(data, offset, singleMessage, 0, messageLen);

                ProcessSingleMessage(conn, singleMessage);

                offset += messageLen;
            }
        }

        private int CalculateMessageLength(byte[] data, int offset, byte messageType)
        {
            switch (messageType)
            {
                case 0x02:
                    return data.Length - offset;
                case 0x03:
                    return 4;
                case 0x0A:
                case 0x0E:
                    if (data.Length - offset < 8) return -1;
                    uint bodyLen = BitConverter.ToUInt32(data, offset + 4);
                    return 8 + (int)bodyLen;
                case 0x10:
                    if (data.Length - offset < 8) return -1;
                    uint directBodyLen = (uint)(data[offset + 4] | (data[offset + 5] << 8) | (data[offset + 6] << 16));
                    return 8 + (int)directBodyLen;
                default:
                    return data.Length - offset;
            }
        }

        private void ProcessSingleMessage(RRConnection conn, byte[] data)
        {
            WirePacketTally.OnRawTCP(data);
            if (data.Length < 1) return;

            byte messageType = data[0];

            switch (messageType)
            {
                case 0x02:
                    HandlePing(conn, data);
                    break;
                case 0x03:
                    HandleConnect(conn, data);
                    break;
                case 0x0A:
                    HandleCompressedA(conn, data);
                    break;
                case 0x0E:
                    HandleCompressedE(conn, data);
                    break;
                case 0x10:
                    HandleDirectMessage(conn, data);
                    break;
                default:
                    Debug.LogError($"[RAW-IN] UNKNOWN message type: 0x{messageType:X2}");
                    break;
            }
        }

        private void HandlePing(RRConnection conn, byte[] data)
        {
            var writer = new LEWriter();
            writer.WriteByte(0x02);
            if (data.Length > 1)
            {
                byte[] pingData = new byte[data.Length - 1];
                Array.Copy(data, 1, pingData, 0, data.Length - 1);
                writer.WriteBytes(pingData);
            }
            byte[] response = writer.ToArray();
            lock (conn.SendLock)
            {
                conn.Stream.Write(response, 0, response.Length);
            }
        }

        private void HandleConnect(RRConnection conn, byte[] data)
        {
            Debug.Log($"Client {conn.ConnId} sent connect message");

            var reader = new LEReader(data);
            reader.ReadByte();
            uint clientId = reader.ReadUInt24();
            _peerId24[conn.ConnId] = clientId;

            Debug.Log($"[CLIENT] peerId=0x{clientId:X6}");

            var writer = new LEWriter();
            writer.WriteByte(0x04);
            writer.WriteUInt24((int)clientId);
            writer.WriteUInt32(0);

            byte[] response = writer.ToArray();
            lock (conn.SendLock)
            {
                conn.Stream.Write(response, 0, response.Length);
            }
            Debug.Log($"Sent connect response to client {conn.ConnId}");
        }

        private void HandleCompressedA(RRConnection conn, byte[] data)
        {
            try
            {
                var reader = new LEReader(data);
                reader.ReadByte();
                uint peerId = reader.ReadUInt24();
                uint bodyLen = reader.ReadUInt32();
                byte channel = reader.ReadByte();
                byte messageType = reader.ReadByte();
                reader.ReadByte();
                uint uncompressedLen = reader.ReadUInt32();

                byte[] compressed = reader.ReadBytes((int)(bodyLen - 7));
                byte[] payload = ZlibUtil.Inflate(compressed);

                HandleChannelMessage(conn, channel, messageType, payload);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GAME-SERVER] handleCompressedA state=failed message='{ex.Message}'");
            }
        }

        private void HandleCompressedE(RRConnection conn, byte[] data)
        {
            try
            {
                var reader = new LEReader(data);
                reader.ReadByte();
                uint dest = reader.ReadUInt24();
                uint bodyLen = reader.ReadUInt24();
                reader.ReadByte();
                uint source = reader.ReadUInt24();
                reader.ReadBytes(5);
                uint uncompressedLen = reader.ReadUInt32();

                byte[] compressed = reader.ReadBytes((int)(bodyLen - 12));

                byte[] payload = ZlibUtil.Inflate(compressed);

                if (payload.Length >= 2)
                {
                    byte channel = payload[0];
                    byte messageType = payload[1];
                    byte[] innerData = new byte[payload.Length - 2];
                    Array.Copy(payload, 2, innerData, 0, innerData.Length);

                    HandleChannelMessage(conn, channel, messageType, innerData);
                }
                else
                {
                    Debug.LogError($"[E-LANE] Payload too short: {payload.Length} bytes");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[E-LANE] state=failed message='{ex.Message}'");
            }
        }

        private void HandleDirectMessage(RRConnection conn, byte[] data)
        {
            try
            {
                var reader = new LEReader(data);
                reader.ReadByte();
                uint peerId = reader.ReadUInt24();
                uint bodyLen = reader.ReadUInt24();
                byte channel = reader.ReadByte();
                byte[] payload = reader.ReadBytes((int)bodyLen);

                Debug.Log($"Direct message: peer=0x{peerId:X6}, channel={channel}");
                HandleChannelMessage(conn, channel, 0, payload);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GAME-SERVER] handleDirectMessage state=failed message='{ex.Message}'");
            }
        }

        private void HandleChannelMessage(RRConnection conn, byte channel, byte messageType, byte[] data)
        {
            WirePacketTally.OnChannel(channel, messageType, data);

            switch (channel)
            {
                case 0:
                    HandleInitialConnection(conn, messageType, data);
                    break;
                case 3:
                    SocialRuntime.Instance.HandleMessage(conn, messageType, data, SendSocialViaAuth);
                    break;
                case 4:
                    HandleCharacterChannel(conn, messageType, data);
                    break;
                case 6:
                    Debug.LogError($"[CHAT-CH6] type=0x{messageType:X2} len={data?.Length ?? 0} hex={(data != null && data.Length > 0 ? BitConverter.ToString(data, 0, Math.Min(data.Length, 48)) : "")}");
                    if (_adminCommands == null)
                    {
                        _adminCommands = new AdminCommands();
                        _adminCommands.SetServerCallbacks(
                            (connection, zone) => ChatChangeZone(connection, zone),
                            (connection) => {
                                if (_selectedCharacter.TryGetValue(connection.LoginName, out var selectedCharacter))
                                    return CharacterRepository.GetCharacter(selectedCharacter.Id);
                                return null;
                            },
                            HandleAdminLevelUp,
                            (login, gcType, modId) => _activeModifiers.AddOrReplace(login, new ActiveModifier
                            {
                                GCType = gcType,
                                Id = modId,
                                Level = 0,
                                PowerLevel = 0,
                                Duration = 0,
                                SourceIsSelf = 0,
                                AddedAt = DateTime.UtcNow
                            }),
                            (login, modId) => _activeModifiers.RemoveById(login, modId),
                            AdminCompleteQuest,
                            (c, w) => { WritePlayerEntitySynch(c, w); }
                        );
                    }
                    {
                        PlayerState adminPs = GetPlayerState(conn.ConnId.ToString());
                        if (IsPlayerAdmin(conn.LoginName) && _adminCommands.TryExecute(conn, messageType, data, adminPs, SendSystemMessage, SendToClient))
                            break;
                    }
                    {
                        bool handledByGnome = false;
                        try
                        {
                            var peekReader = new LEReader(data);
                            string peekMessage = peekReader.ReadCString();
                            if (peekMessage.StartsWith("@"))
                            {
                                string command = peekMessage.Substring(1).Trim().ToLower();
                                if (command == "gnome" || command == "bling" || command == "blinggnome" || command == "gs" || command == "gnomestatus")
                                {
                                    BlingGnomeRuntime.Instance.SetServer(this);
                                    BlingGnomeRuntime.Instance.TryExecute(conn, data, SendCompressedA, SendSystemMessage);
                                    handledByGnome = true;
                                }
                            }
                        }
                        catch { }
                        if (handledByGnome) break;
                    }
                    if (_chatCommands == null) _chatCommands = new ChatCommands(this);
                    if (!_chatCommands.TryExecute(conn, data, SendSystemMessage))
                    {
                    }
                    {
                        try
                        {
                            var chatReader = new LEReader(data);
                            string chatText = chatReader.ReadCString();
                            if (!string.IsNullOrEmpty(chatText) && !chatText.StartsWith("/") && !chatText.StartsWith("@"))
                            {
                                HandleChatMessage(conn, messageType, chatText);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[CHAT-RELAY] state=failed message='{ex.Message}'");
                        }
                    }
                    break;
                case 7:
                    HandleClientEntityChannel(conn, messageType, data);
                    break;
                case 9:
                    HandleGroupChannel(conn, messageType, data);
                    break;
                case 10:
                    HandleTradeChannel(conn, messageType, data);
                    break;
                case 12:
                    SocialRuntime.Instance.HandleMessage(conn, messageType, data, SendSocialViaAuth);
                    break;
                case 13:
                    Debug.LogError($"[CH13]  type=0x{messageType:X2} dataLen={data?.Length ?? 0} hex={(data != null && data.Length > 0 ? BitConverter.ToString(data, 0, Math.Min(30, data.Length)) : "EMPTY")} ");
                    if (messageType == 0x07)
                    {
                        Debug.LogError($"[CH13] -> Routing to goToCheckpoint (0x07)");
                        HandleCheckpointTeleportRequest(conn, data);
                    }
                    else if (messageType == 0x0C)
                    {
                        Debug.LogError($"[CH13] -> Routing to Obelisk (0x0C)");
                        HandleObeliskTeleport(conn);
                    }
                    else if (messageType == 0x0B)
                    {
                        Debug.LogError($"[CH13] -> Routing to SavedPlace (0x0B)");
                        HandleSavedPlaceTeleport(conn);
                    }
                    else
                    {
                        Debug.LogError($"[CH13] -> UNHANDLED type 0x{messageType:X2} - routing to HandleZoneChannel");
                        HandleZoneChannel(conn, data);
                    }
                    break;
                case 11:
                    HandleGroupClientChannel(conn, messageType, data);
                    break;
                case 15:
                    PosseRuntime.Instance.HandleMessage(conn, messageType, data, SendSocialViaAuth, this);
                    break;
                default:
                    {
                        string hex = (data != null && data.Length > 0)
                            ? BitConverter.ToString(data, 0, Math.Min(48, data.Length))
                            : "EMPTY";
                        Debug.LogError($"[POSSE] unhandled channel={channel} type=0x{messageType:X2} dataLen={data?.Length ?? 0} hex={hex}");
                    }
                    break;
            }
        }

        private void HandleCheckpointTeleportRequest(RRConnection conn, byte[] data)
        {
            Debug.LogError($"[CP-TELEPORT] ");
            Debug.LogError($"[CP-TELEPORT] Data hex: {BitConverter.ToString(data ?? new byte[0])}");

            try
            {
                if (data == null || data.Length < 1)
                {
                    Debug.LogError($"[CP-TELEPORT]  Empty data");
                    return;
                }

                byte tag = data[0];
                string checkpointName = null;

                if (tag == 0xFF && data.Length > 2)
                {
                    int end = System.Array.IndexOf(data, (byte)0x00, 1);
                    if (end > 1)
                        checkpointName = System.Text.Encoding.ASCII.GetString(data, 1, end - 1);
                    Debug.LogError($"[CP-TELEPORT] Tag 0xFF -> checkpoint name: '{checkpointName}'");
                }
                else if (tag == 0x00)
                {
                    Debug.LogError($"[CP-TELEPORT] Tag 0x00 = null ref - using obelisk rotator");
                    HandleObeliskTeleport(conn);
                    return;
                }
                else
                {
                    Debug.LogError($"[CP-TELEPORT]  Unknown tag: 0x{tag:X2}");
                    return;
                }

                if (string.IsNullOrEmpty(checkpointName))
                {
                    Debug.LogError($"[CP-TELEPORT]  Empty checkpoint name");
                    return;
                }

                var checkpoint = DatabaseLoader.Checkpoints.FirstOrDefault(c =>
                    c.id.Equals(checkpointName, StringComparison.OrdinalIgnoreCase));

                if (checkpoint == null)
                {
                    Debug.LogError($"[CP-TELEPORT]  Checkpoint not in database: '{checkpointName}'");
                    return;
                }

                string destinationZone = checkpoint.zone;
                Debug.LogError($"[CP-TELEPORT]  Teleporting to '{destinationZone}' via checkpoint '{checkpointName}'");

                var destZone = _zones.Values.FirstOrDefault(z =>
                    z.name.Equals(destinationZone, StringComparison.OrdinalIgnoreCase));

                if (destZone != null)
                {
                    conn.PendingSpawnX = destZone.spawnX;
                    conn.PendingSpawnY = destZone.spawnY;
                    conn.PendingSpawnZ = destZone.spawnZ;
                }

                ChangeZone(conn, destinationZone, "");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CP-TELEPORT] state=failed message='{ex.Message}' stack='{ex.StackTrace}'");
            }
        }

        private HashSet<uint> _finalizedMonsterKills = new HashSet<uint>();
        private int _mpDiagCounter = 0;


        public int GetDifficultyForConn(RRConnection conn)
        {
            var group = GroupDirectory.Instance.GetGroupForConn(conn.ConnId);
            if (group != null)
                return Math.Max(0, Math.Min(3, (int)group.MonsterDifficulty));
            return 0;
        }

        public float GetDifficultyHPMult(int difficulty)
        {
            return 1.0f;
        }

        public float GetDifficultyXPMult(int difficulty)
        {
            return 1.0f;
        }

        private void ApplyDifficultyToMonsters(RRConnection conn, string instanceKey)
        {
            int diff = GetDifficultyForConn(conn);
            if (diff == 0) return;
            Debug.LogError($"[DIFFICULTY]  group difficulty scaling is not applied without authored evidence diff={diff} instance='{instanceKey}'");
            return;
        }

        public void SendMonsterToGroupInZone(string zoneName, uint instanceId, Monster monster)
        {
            foreach (var conn in _connections.Values)
            {
                if (!conn.IsConnected || !conn.IsSpawned) continue;
                if (!string.Equals(conn.CurrentZoneName, zoneName, StringComparison.OrdinalIgnoreCase)) continue;
                if (conn.InstanceId != instanceId) continue;

                SendMonsterToClient(conn, monster);
                Debug.LogError($"[GROUP-SPAWN] Sent {monster.Name} to {conn.LoginName} in {zoneName} inst={instanceId}");
            }
        }

        public void BroadcastMonsterDespawnToZone(string zoneName, uint instanceId, uint entityId)
        {
            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x05);
            writer.WriteUInt16((ushort)entityId);
            writer.WriteByte(0x06);
            byte[] despawnPacket = writer.ToArray();

            foreach (var conn in _connections.Values)
            {
                if (!conn.IsConnected || !conn.IsSpawned) continue;
                if (!string.Equals(conn.CurrentZoneName, zoneName, StringComparison.OrdinalIgnoreCase)) continue;
                if (conn.InstanceId != instanceId) continue;

                SendToClient(conn, despawnPacket);
                Debug.LogError($"[GROUP-KILL] Sent despawn for entity {entityId} to {conn.LoginName}");
            }
        }

        public void BroadcastRNGSeedToZone(string zoneName, uint instanceId, uint seed)
        {
            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x0C);
            writer.WriteUInt32(seed);
            writer.WriteByte(0x06);
            byte[] seedPacket = writer.ToArray();

            foreach (var conn in _connections.Values)
            {
                if (!conn.IsConnected || !conn.IsSpawned) continue;
                if (!string.Equals(conn.CurrentZoneName, zoneName, StringComparison.OrdinalIgnoreCase)) continue;
                if (conn.InstanceId != instanceId) continue;

                SendToClient(conn, seedPacket);
            }
            Debug.LogError($"[RNG-BROADCAST] Seed 0x{seed:X8} sent to inst={instanceId} in {zoneName}");
        }

        private void BroadcastRNGSeedToOthersInZone(RRConnection sender, uint seed)
        {
            if (string.IsNullOrEmpty(sender.CurrentZoneName)) return;

            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x0C);
            writer.WriteUInt32(seed);
            writer.WriteByte(0x06);
            byte[] seedPacket = writer.ToArray();

            foreach (var conn in _connections.Values)
            {
                if (conn == sender) continue;
                if (!conn.IsConnected || !conn.IsSpawned) continue;
                if (!string.Equals(conn.CurrentZoneName, sender.CurrentZoneName, StringComparison.OrdinalIgnoreCase)) continue;

                SendToClient(conn, seedPacket);
            }
        }


        public static bool IsPublicZone(string zoneName)
        {
            if (string.IsNullOrEmpty(zoneName)) return true;
            string lower = zoneName.ToLower();
            if (lower.Contains("town")) return true;
            if (lower.Contains("tutorial")) return true;
            if (lower.Contains("dew") || lower.Contains("valley")) return true;
            if (lower == "pvp_start" || lower == "pvp_hub") return true;
            if (lower == "test_pvp1" || lower == "test_pvp2") return true;
            return false;
        }

        private void AssignInstanceId(RRConnection conn)
        {
            if (IsPublicZone(conn.CurrentZoneName))
            {
                conn.InstanceId = 0;
                StampRuntimeInstanceKey(conn, "public-zone");
                Debug.LogError($"[INSTANCE] {conn.LoginName} -> PUBLIC zone '{conn.CurrentZoneName}' (instance 0)");
                return;
            }


            var group = GroupDirectory.Instance.GetGroupForConn(conn.ConnId);

            if (group != null)
            {
                foreach (var member in group.Members)
                {
                    if (member.ConnId == conn.ConnId) continue;
                    var memberConn = FindConnectionById(member.ConnId);
                    if (memberConn == null) continue;
                    if (memberConn.CurrentZoneName != conn.CurrentZoneName) continue;
                    if (memberConn.InstanceId == 0) continue;

                    conn.InstanceId = memberConn.InstanceId;
                    StampRuntimeInstanceKey(conn, "group-member");
                    Debug.LogError($"[INSTANCE] {conn.LoginName} -> DUNGEON '{conn.CurrentZoneName}' (joined group member {memberConn.LoginName}'s instance {conn.InstanceId:X8})");
                    return;
                }

                conn.InstanceId = group.GroupId;
                StampRuntimeInstanceKey(conn, "group");
                Debug.LogError($"[INSTANCE] {conn.LoginName} -> DUNGEON '{conn.CurrentZoneName}' (group instance {group.GroupId})");
                return;
            }

            if (!string.IsNullOrEmpty(conn.LoginName))
            {
                foreach (var other in _connections.Values)
                {
                    if (other == conn) continue;
                    if (other.CurrentZoneName != conn.CurrentZoneName) continue;
                    if (other.InstanceId == 0) continue;

                    var otherGroup = GroupDirectory.Instance.GetGroupForConn(other.ConnId);
                    if (otherGroup == null) continue;

                    bool weAreMember = otherGroup.Members.Any(m =>
                        string.Equals(m.LoginName, conn.LoginName, System.StringComparison.OrdinalIgnoreCase));
                    if (!weAreMember) continue;

                    conn.InstanceId = other.InstanceId;
                    StampRuntimeInstanceKey(conn, "stale-group-recovery");
                    Debug.LogError($"[INSTANCE] {conn.LoginName} -> DUNGEON '{conn.CurrentZoneName}' (joined via stale-group-recovery, latched onto {other.LoginName}'s instance {conn.InstanceId:X8})");
                    return;
                }
            }

            conn.InstanceId = AllocateSoloDungeonInstanceId(conn, conn.CurrentZoneName);
            StampRuntimeInstanceKey(conn, "solo");
            Debug.LogError($"[INSTANCE] {conn.LoginName} -> DUNGEON '{conn.CurrentZoneName}' (SOLO instance {conn.InstanceId:X8}, owner {GetSoloDungeonInstanceOwnerKey(conn, conn.CurrentZoneName)})");
        }

        public bool CanSeePlayer(RRConnection a, RRConnection b)
        {
            if (a == b) return false;
            if (!a.IsConnected || !b.IsConnected) return false;
            if (!a.IsSpawned || !b.IsSpawned) return false;
            if (a.CurrentZoneGcType != b.CurrentZoneGcType) return false;
            if (a.InstanceId != b.InstanceId) return false;
            return true;
        }

        public string GetInstanceZoneKey(RRConnection conn)
        {
            if (conn == null)
                return "";
            string computed = BuildRuntimeInstanceKey(conn);
            if (string.Equals(conn.RuntimeInstanceKey, computed, StringComparison.OrdinalIgnoreCase))
                return conn.RuntimeInstanceKey;
            conn.RuntimeInstanceKey = computed;
            return computed;
        }

        private string BuildRuntimeInstanceKey(RRConnection conn)
        {
            if (conn == null)
                return "";
            if (IsPublicZone(conn.CurrentZoneName))
                return conn.CurrentZoneName ?? "";
            return $"{conn.CurrentZoneName}_inst{conn.InstanceId}";
        }

        private void StampRuntimeInstanceKey(RRConnection conn, string source)
        {
            if (conn == null)
                return;
            conn.RuntimeInstanceKey = BuildRuntimeInstanceKey(conn);
            Debug.LogError($"[INSTANCE] runtimeKey='{conn.RuntimeInstanceKey}' source={source ?? "unknown"} zone='{conn.CurrentZoneName}' instance={conn.InstanceId:X8}");
        }


        private GroupMemberInfo BuildGroupMemberInfo(RRConnection conn)
        {
            uint avatarId = GetPlayerAvatarId(conn.LoginName);
            string charName = conn.LoginName ?? "Unknown";
            uint charSqlId = GetCharSqlId(conn);

            if (_selectedCharacter.TryGetValue(conn.LoginName, out var selectedCharacter))
                charName = selectedCharacter.Name ?? charName;

            var group = GroupDirectory.Instance.GetGroupForConn(conn.ConnId);
            if (group != null)
            {
                var groupMember = group.Members.Find(member => member.ConnId == conn.ConnId);
                if (groupMember != null)
                {
                    groupMember.CharSqlId = charSqlId;
                    groupMember.AvatarEntityId = avatarId;
                    groupMember.Name = charName;
                }
            }

            return new GroupMemberInfo
            {
                CharSQLID = charSqlId,
                Name = charName,
                AvatarEntityId = avatarId,
                IsOnline = true
            };
        }

        private GroupMemberInfo BuildGroupMemberInfoFromCache(Gameplay.GroupMember groupMember)
        {
            return new GroupMemberInfo
            {
                CharSQLID = groupMember.CharSqlId,
                Name = groupMember.Name ?? "Unknown",
                AvatarEntityId = groupMember.AvatarEntityId,
                IsOnline = groupMember.IsOnline
            };
        }

        private uint GetCharSqlId(RRConnection conn)
        {
            if (conn == null) return 0;
            if (!string.IsNullOrEmpty(conn.LoginName)
                && _selectedCharacter.TryGetValue(conn.LoginName, out var selectedCharacter)
                && selectedCharacter.Id != 0)
                return (uint)selectedCharacter.Id;
            if (conn.CharSqlId != 0) return conn.CharSqlId;
            return (uint)(conn.ConnId + 1);
        }

        private RRConnection FindGroupMemberByCharSqlId(Gameplay.Group group, uint charSqlId)
        {
            foreach (var member in group.Members)
            {
                var memberConnection = FindConnectionById(member.ConnId);
                if (memberConnection != null && GetCharSqlId(memberConnection) == charSqlId) return memberConnection;
            }
            return null;
        }
        public void SpawnAdminShop(RRConnection conn, Action<RRConnection, string> sendMessage)
        {
            DespawnAdminShop(conn, sendMessage);
            string[] adminMerchants = {
                "world.town.npc.AdminWeaponVendor",
                "world.town.npc.AdminArmorVendor",
                "world.town.npc.AdminMiscVendor"
            };
            string[] displayTypes = {
                "world.town.npc.VendorWeapon1",
                "world.town.npc.VendorWeapon2",
                "world.town.npc.VendorWeapon3"
            };
            float baseX = conn.PlayerPosX + 3;
            float baseY = conn.PlayerPosY;
            float baseZ = conn.PlayerPosZ;
            var shopNPCs = new List<ZoneNPC>();
            var writer = new LEWriter();
            writer.WriteByte(0x07);
            for (int n = 0; n < adminMerchants.Length; n++)
            {
                string gcClass = adminMerchants[n];
                string name = gcClass.Split('.')[gcClass.Split('.').Length - 1];
                float npcX = baseX + (n * 40);
                float npcY = baseY;
                ushort npcId = (ushort)_nextEntityId++;
                ushort behaviorId = (ushort)_nextEntityId++;
                ushort skillsId = (ushort)_nextEntityId++;
                ushort manipulatorsId = (ushort)_nextEntityId++;
                ushort modifiersId = (ushort)_nextEntityId++;
                ushort merchantId = (ushort)_nextEntityId++;
                uint npcHPWire = ResolveAuthoredUnitMaxHealthWire(displayTypes[n]);
                var npc = new ZoneNPC
                {
                    Id = npcId,
                    UnitBehaviorId = behaviorId,
                    GCClass = displayTypes[n],

                    Name = name,
                    PosX = npcX,
                    PosY = npcY,
                    PosZ = baseZ,
                    Heading = 0,
                    IsMerchant = true,
                    MerchantId = merchantId,
                    IsAdminMerchant = true,
                    IsTrainer = false,
                    TrainerId = 0
                };
                if (!_zoneNPCs.ContainsKey(conn.CurrentZoneId))
                    _zoneNPCs[conn.CurrentZoneId] = new List<ZoneNPC>();
                _zoneNPCs[conn.CurrentZoneId].Add(npc);
                shopNPCs.Add(npc);
                _npcPositions[npcId] = (npcX, npcY, baseZ);
                _allEntityPositions[npcId] = (npcX, npcY, baseZ);
                writer.WriteByte(0x01);
                writer.WriteUInt16(npcId);
                WriteGCType(writer, displayTypes[n], preserveCase: true);
                writer.WriteByte(0x32);
                writer.WriteUInt16(npcId);
                writer.WriteUInt16(behaviorId);
                WriteGCType(writer, "npc.base.behavior", preserveCase: false);
                writer.WriteByte(0x01);
                writer.WriteByte(0xFF); writer.WriteByte(0x00); writer.WriteByte(0x00); writer.WriteByte(0x01);
                writer.WriteByte(0x85); writer.WriteByte(0x00);
                for (int zeroIndex = 0; zeroIndex < 5; zeroIndex++) writer.WriteUInt32(0);
                writer.WriteByte(0x00);
                writer.WriteByte(0xFF); writer.WriteByte(0x00); writer.WriteByte(0x00);
                writer.WriteByte(0x00); writer.WriteByte(0x00);
                writer.WriteUInt32(0); writer.WriteUInt32(0);
                writer.WriteByte(0x32);
                writer.WriteUInt16(npcId);
                writer.WriteUInt16(skillsId);
                WriteGCType(writer, "skills", preserveCase: false);
                writer.WriteByte(0x01);
                writer.WriteByte(0xFF); writer.WriteByte(0xFF); writer.WriteByte(0xFF); writer.WriteByte(0xFF);
                writer.WriteByte(0x00); writer.WriteByte(0x01);
                WriteGCType(writer, "skills.professions.Warrior", preserveCase: true);
                writer.WriteByte(0x32);
                writer.WriteUInt16(npcId);
                writer.WriteUInt16(manipulatorsId);
                WriteGCType(writer, "manipulators", preserveCase: false);
                writer.WriteByte(0x01); writer.WriteByte(0x00);
                writer.WriteByte(0x32);
                writer.WriteUInt16(npcId);
                writer.WriteUInt16(modifiersId);
                WriteGCType(writer, "modifiers", preserveCase: false);
                writer.WriteByte(0x01);
                writer.WriteUInt32(0); writer.WriteByte(0x00); writer.WriteUInt32(0);
                MerchantRuntime.EnsureInventoryForLevel(displayTypes[n], GetPlayerState(conn.ConnId.ToString()).Level);
                MerchantRuntime.WriteMerchantComponent(writer, displayTypes[n], npcId, merchantId);
                writer.WriteByte(0x02);
                writer.WriteUInt16(npcId);
                writer.WriteUInt32(0x06);
                writer.WriteInt32((int)(npcX * 256));
                writer.WriteInt32((int)(npcY * 256));
                writer.WriteInt32((int)(baseZ * 256));
                writer.WriteInt32(0);
                writer.WriteByte(0x00); writer.WriteByte(0x01);
                for (int zeroIndex = 0; zeroIndex < 8; zeroIndex++) writer.WriteUInt32(0);
                writer.WriteByte(0x35);
                writer.WriteUInt16(behaviorId);
                writer.WriteByte(0x04); writer.WriteByte(0x11); writer.WriteByte(0x00);
                writer.WriteInt32((int)(npcX * 256));
                writer.WriteInt32((int)(npcY * 256));
                writer.WriteInt32((int)(baseZ * 256));
                writer.WriteByte(0x02);
                writer.WriteUInt32(npcHPWire);
            }
            writer.WriteByte(0x06);
            byte[] adminPacket = writer.ToArray();
            Debug.LogError($"[ADMIN-SHOP] packetBytes={adminPacket.Length}");
            SendToClient(conn, adminPacket);
            _adminShopNPCs[conn.ConnId] = shopNPCs;
            sendMessage(conn, "[SHOP] vendors=3 state=spawned action=@shop close");
        }

        public void DespawnAdminShop(RRConnection conn, Action<RRConnection, string> sendMessage)
        {
            if (!_adminShopNPCs.TryGetValue(conn.ConnId, out var npcs) || npcs.Count == 0)
                return;
            var writer = new LEWriter();
            writer.WriteByte(0x07);
            foreach (var npc in npcs)
            {
                writer.WriteByte(0x05);
                writer.WriteUInt16((ushort)npc.Id);
                if (_zoneNPCs.TryGetValue(conn.CurrentZoneId, out var zoneList))
                    zoneList.Remove(npc);
            }
            writer.WriteByte(0x06);
            SendToClient(conn, writer.ToArray());
            _adminShopNPCs.Remove(conn.ConnId);
            sendMessage(conn, "[SHOP] state=despawned");
        }
        private void SendGroupRemoveUser(RRConnection conn)
        {
            var group = GroupDirectory.Instance.GetGroupForConn(conn.ConnId);
            if (group == null) return;

            uint charSqlId = GetCharSqlId(conn);
            byte[] removePacket = GroupPackets.BuildProcessRemoveUser(group.GroupId, charSqlId);

            foreach (var member in group.Members)
            {
                if (member.ConnId == conn.ConnId) continue;
                var memberConn = FindConnectionById(member.ConnId);
                if (memberConn != null)
                    SendToClient(memberConn, removePacket);
            }
            Debug.LogError($"[GROUP] Sent processRemoveUser for {conn.LoginName}");
        }

    }

    public class ActiveModifier
    {
        public string GCType;
        public uint Id;
        public byte Level;
        public uint PowerLevel;
        public uint Duration;
        public byte SourceIsSelf;
        public DateTime AddedAt;
    }

    public class ActiveModifiers
    {
        private readonly Dictionary<string, List<ActiveModifier>> _playerModifiers
            = new Dictionary<string, List<ActiveModifier>>(StringComparer.OrdinalIgnoreCase);

        private uint _nextModifierId = 1;

        public uint NextId() => _nextModifierId++;

        public void AddOrReplace(string loginName, ActiveModifier mod)
        {
            if (!_playerModifiers.TryGetValue(loginName, out var list))
            {
                list = new List<ActiveModifier>();
                _playerModifiers[loginName] = list;
            }
            list.RemoveAll(m => string.Equals(m.GCType, mod.GCType, StringComparison.OrdinalIgnoreCase));
            list.Add(mod);
            Debug.LogError($"[ACTIVE-MODIFIERS] Tracked '{mod.GCType}' id={mod.Id} for {loginName} (total={list.Count})");
        }

        public bool Remove(string loginName, string gcType)
        {
            if (!_playerModifiers.TryGetValue(loginName, out var list)) return false;
            int removed = list.RemoveAll(m => string.Equals(m.GCType, gcType, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
                Debug.LogError($"[ACTIVE-MODIFIERS] Removed '{gcType}' from {loginName} (remaining={list.Count})");
            return removed > 0;
        }

        public bool RemoveById(string loginName, uint modId)
        {
            if (!_playerModifiers.TryGetValue(loginName, out var list)) return false;
            int removed = list.RemoveAll(m => m.Id == modId);
            if (removed > 0)
                Debug.LogError($"[ACTIVE-MODIFIERS] Removed modId={modId} from {loginName} (remaining={list.Count})");
            return removed > 0;
        }

        public List<ActiveModifier> ListFor(string loginName)
        {
            if (!_playerModifiers.TryGetValue(loginName, out var list))
                return new List<ActiveModifier>();

            const double TICKS_PER_SEC = 1000.0 / 24.0;
            const double ZONE_BUFFER_TICKS = 3.0 * TICKS_PER_SEC;
            var result = new List<ActiveModifier>();
            var now = DateTime.UtcNow;
            foreach (var mod in list)
            {
                if (mod.Duration == 0)
                {
                    result.Add(mod);
                }
                else
                {
                    double elapsedTicks = (now - mod.AddedAt).TotalSeconds * TICKS_PER_SEC + ZONE_BUFFER_TICKS;
                    if (elapsedTicks < mod.Duration)
                    {
                        result.Add(new ActiveModifier
                        {
                            GCType = mod.GCType,
                            Id = mod.Id,
                            Level = mod.Level,
                            PowerLevel = mod.PowerLevel,
                            Duration = (uint)(mod.Duration - elapsedTicks),
                            SourceIsSelf = mod.SourceIsSelf,
                            AddedAt = mod.AddedAt
                        });
                    }
                }
            }
            list.RemoveAll(m => m.Duration > 0 && (DateTime.UtcNow - m.AddedAt).TotalSeconds * TICKS_PER_SEC >= m.Duration);
            return result;
        }

        public void Clear(string loginName)
        {
            _playerModifiers.Remove(loginName);
        }

        public void SaveToDb(string loginName, int characterId)
        {
            try
            {
                using (var connection = GameDatabase.GetConnection())
                using (var transaction = connection.BeginTransaction())
                {
                    GameDatabase.ExecuteNonQuery(connection,
                        "DELETE FROM character_modifiers WHERE character_id=@cid",
                        ("@cid", characterId));

                    var mods = ListFor(loginName);
                    foreach (var mod in mods)
                    {
                        GameDatabase.ExecuteNonQuery(connection,
                            @"INSERT INTO character_modifiers
                              (character_id, gc_type, modifier_id, level, power_level, duration_remaining, source_is_self)
                              VALUES (@cid, @gc, @mid, @lv, @pl, @dur, @sis)",
                            ("@cid", characterId),
                            ("@gc", mod.GCType),
                            ("@mid", (int)mod.Id),
                            ("@lv", (int)mod.Level),
                            ("@pl", (int)mod.PowerLevel),
                            ("@dur", (int)mod.Duration),
                            ("@sis", (int)mod.SourceIsSelf));
                    }
                    transaction.Commit();
                    Debug.LogError($"[ACTIVE-MODIFIERS] Saved {mods.Count} modifiers for charId={characterId}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ACTIVE-MODIFIERS] SaveToDb failed: {ex.Message}");
            }
        }

        public void LoadFromDb(string loginName, int characterId)
        {
            try
            {
                using (var connection = GameDatabase.GetConnection())
                {
                    using (var reader = GameDatabase.ExecuteReader(connection,
                        "SELECT gc_type, modifier_id, level, power_level, duration_remaining, source_is_self FROM character_modifiers WHERE character_id=@cid",
                        ("@cid", characterId)))
                    {
                        var list = new List<ActiveModifier>();
                        while (reader.Read())
                        {
                            var mod = new ActiveModifier
                            {
                                GCType = reader.GetString(0),
                                Id = (uint)reader.GetInt32(1),
                                Level = (byte)reader.GetInt32(2),
                                PowerLevel = (uint)reader.GetInt32(3),
                                Duration = (uint)reader.GetInt32(4),
                                SourceIsSelf = (byte)reader.GetInt32(5),
                                AddedAt = DateTime.UtcNow
                            };
                            list.Add(mod);
                            if (mod.Id >= _nextModifierId)
                                _nextModifierId = mod.Id + 1;
                        }

                        if (list.Count > 0)
                        {
                            _playerModifiers[loginName] = list;
                            Debug.LogError($"[ACTIVE-MODIFIERS] Loaded {list.Count} modifiers for {loginName} (charId={characterId})");
                        }
                    }

                    GameDatabase.ExecuteNonQuery(connection,
                        "DELETE FROM character_modifiers WHERE character_id=@cid",
                        ("@cid", characterId));
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ACTIVE-MODIFIERS] LoadFromDb failed: {ex.Message}");
            }
        }
    }


}
