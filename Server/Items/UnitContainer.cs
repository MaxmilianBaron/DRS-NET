using DungeonRunners.Utilities;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DungeonRunners.Engine;
using DungeonRunners.Data;
using System;

namespace DungeonRunners.Networking
{
    public class UnitContainer
    {
        private readonly GameServer _server;

        public UnitContainer(GameServer server)
        {
            _server = server;
        }

        private Dictionary<string, uint> _activeHealthModId = new Dictionary<string, uint>();
        private Dictionary<string, uint> _activeManaModId = new Dictionary<string, uint>();
        private Dictionary<string, uint> _activeBuffModId = new Dictionary<string, uint>();
        private Dictionary<string, float> _healthPotionCooldown = new Dictionary<string, float>();
        private Dictionary<string, float> _manaPotionCooldown = new Dictionary<string, float>();
        private uint _nextPotionModId = 50000;

        private bool IsPotionOnCooldown(string connId, bool isHealth)
        {
            var cooldownByConnection = isHealth ? _healthPotionCooldown : _manaPotionCooldown;
            if (cooldownByConnection.ContainsKey(connId))
            {
                float elapsed = Time.time - cooldownByConnection[connId];
                if (elapsed < 1.0f)
                    return true;
            }
            return false;
        }

        private void StartPotionCooldown(string connId, bool isHealth)
        {
            var cooldownByConnection = isHealth ? _healthPotionCooldown : _manaPotionCooldown;
            cooldownByConnection[connId] = Time.time;
        }

        public void ProcessRequest(RRConnection conn, LEReader reader, ushort componentId, byte subMessage)
        {
            Debug.LogError($"[UNIT-CONTAINER] update component=0x{componentId:X4} sub=0x{subMessage:X2}");
            switch (subMessage)
            {
                case 0x21:
                    ProcessSetLocal(conn, reader, componentId);
                    break;
                case 0x22:
                    ProcessClearLocal(conn, reader, componentId);
                    break;
                case 0x23:
                    ProcessDropItem(conn, reader, componentId);
                    break;
                case 0x25:
                    ProcessUseItem(conn, reader, componentId);
                    break;
                case 0x26:
                    ProcessUseItemPosition(conn, reader, componentId);
                    break;
                case 0x27:
                    ProcessUseItemTarget(conn, reader, componentId);
                    break;
                case 0x28:
                    ProcessGetItemLocal(conn, reader, componentId);
                    break;
                case 0x29:
                    ProcessPutItemXYLocal(conn, reader, componentId);
                    break;
                default:
                    Debug.LogError($"[UNIT-CONTAINER] sub=0x{subMessage:X2} reason=unhandled");
                    break;
            }
        }

        private void ProcessUseItem(RRConnection conn, LEReader reader, ushort componentId)
        {
            uint clientItemId = reader.ReadUInt32();
            Debug.LogError($"[USE-ITEM] clientItemId={clientItemId}");

            string connId = conn.ConnId.ToString();
            PlayerState playerState = _server.GetPlayerState(connId);

            uint actualSlot = clientItemId;
            var itemData = _server.GetInventoryItemBySlot(connId, clientItemId);

            if (itemData == null)
            {
                var allItems = _server.GetAllInventoryItems(connId);
                if (allItems != null && allItems.Count > 0)
                {
                    uint minServerSlot = uint.MaxValue;
                    foreach (var key in allItems.Keys)
                        if (key < minServerSlot) minServerSlot = key;

                    if (clientItemId > minServerSlot)
                    {
                        uint offset = clientItemId - minServerSlot;
                        actualSlot = clientItemId - offset;
                        itemData = _server.GetInventoryItemBySlot(connId, actualSlot);
                        Debug.LogError($"[USE-ITEM] offset={offset} slot={actualSlot}");
                    }

                    if (itemData == null)
                    {
                        var sortedSlots = new List<uint>(allItems.Keys);
                        sortedSlots.Sort();

                        if (sortedSlots.Count > 0)
                        {
                            uint guessBase = clientItemId - (uint)(sortedSlots.Count - 1);
                            int itemIndex = (int)(clientItemId - guessBase);
                            if (itemIndex >= 0 && itemIndex < sortedSlots.Count)
                            {
                                actualSlot = sortedSlots[itemIndex];
                                itemData = _server.GetInventoryItemBySlot(connId, actualSlot);
                                Debug.LogError($"[USE-ITEM] index={itemIndex} slot={actualSlot}");
                            }
                        }
                    }

                    if (itemData == null)
                    {
                        foreach (var inventoryEntry in allItems)
                            Debug.LogError($"[USE-ITEM] available slot={inventoryEntry.Key} item={inventoryEntry.Value.item.GCClass}");
                    }
                }
            }

            if (itemData == null)
            {
                Debug.LogError($"[USE-ITEM] clientItemId={clientItemId} reason=missing");
                return;
            }

            GCObject item = itemData.Value.item;
            byte itemX = itemData.Value.x;
            byte itemY = itemData.Value.y;
            string gcLower = item.GCClass.ToLower();

            Debug.LogError($"[USE-ITEM] item={item.GCClass} pos=({itemX},{itemY}) slot={actualSlot}");

            if (gcLower.Contains("healthpotion") && IsPotionOnCooldown(connId, true))
            {
                Debug.LogError("[HEALTH-POTION] reason=cooldown");
                return;
            }
            if (gcLower.Contains("manapotion") && IsPotionOnCooldown(connId, false))
            {
                Debug.LogError("[MANA-POTION] reason=cooldown");
                return;
            }

            if (gcLower.Contains("townportal"))
            {
                var zoneDef = GCDatabase.Instance?.Resolve(conn.CurrentZoneName ?? "");
                if (zoneDef != null && zoneDef.GetBool("IsTown"))
                {
                    Debug.LogError($"[USE-ITEM] item={item.GCClass} zone={conn.CurrentZoneName} reason=UsableInTown=false sourceFunction=ActiveItem::validateUse@0x00579190");
                    return;
                }
                string respawnZone = zoneDef?.GetString("RespawnZone", "town") ?? "town";
                int tpCount = _server.GetStackCount(connId, actualSlot);
                int remaining = tpCount - 1;
                if (remaining > 0)
                    _server.SetStackCount(connId, actualSlot, remaining);
                else
                {
                    _server.FreeInventorySlots(connId, itemX, itemY, 1, 1);
                    _server.RemoveInventoryItemBySlot(connId, actualSlot);
                }
                _server.SpawnTownPortalWithRemoval(conn, respawnZone.ToLowerInvariant(), componentId, clientItemId,
                    playerState, gcLower, itemX, itemY, remaining);
                return;
            }

            if (gcLower.Contains("skillbook"))
            {
                string skillToLearn = null;
                if (gcLower.Contains("summonblinggnome"))
                    skillToLearn = "skills.generic.SummonBlingGnome";

                Debug.LogError($"[SKILLBOOK] consumed={item.GCClass} teaches={skillToLearn}");

                int bookStack = _server.GetStackCount(connId, actualSlot);
                if (bookStack > 1)
                {
                    _server.SetStackCount(connId, actualSlot, bookStack - 1);
                }
                else
                {
                    _server.FreeInventorySlots(connId, itemX, itemY, 1, 1);
                    _server.RemoveInventoryItemBySlot(connId, actualSlot);
                }

                var skillBookWriter = new LEWriter();
                skillBookWriter.WriteByte(0x07);
                skillBookWriter.WriteByte(0x35);
                skillBookWriter.WriteUInt16(componentId);
                skillBookWriter.WriteByte(0x1F);
                skillBookWriter.WriteUInt32(clientItemId);
                _server.WritePlayerEntitySynch(conn, skillBookWriter);

                if (bookStack > 1)
                {
                    int newCount = bookStack - 1;
                    skillBookWriter.WriteByte(0x35);
                    skillBookWriter.WriteUInt16(componentId);
                    skillBookWriter.WriteByte(0x1E);
                    skillBookWriter.WriteByte(0x0B);
                    skillBookWriter.WriteByte(0xFF);
                    skillBookWriter.WriteCString(DungeonRunners.Data.GCObject.GetPacketGCClassFor(gcLower));
                    skillBookWriter.WriteUInt32(clientItemId);
                    skillBookWriter.WriteByte(itemX);
                    skillBookWriter.WriteByte(itemY);
                    skillBookWriter.WriteByte((byte)newCount);
                    skillBookWriter.WriteByte(0x01);
                    skillBookWriter.WriteByte(0x00);
                    skillBookWriter.WriteByte(0x00);
                    _server.WritePlayerEntitySynch(conn, skillBookWriter);
                }

                skillBookWriter.WriteByte(0x06);
                _server.SendToClient(conn, skillBookWriter.ToArray());

                if (skillToLearn != null)
                {
                    bool granted = _server.GrantSkillRuntime(conn, skillToLearn);
                    string shortName = skillToLearn.Substring(skillToLearn.LastIndexOf('.') + 1);
                    _server.SendSystemMessage(conn, granted
                        ? $"[Skill Book] You learned {shortName}!"
                        : $"[Skill Book] You already know {shortName}, but the book vanishes anyway.");
                }
                return;
            }

            int stackCount = _server.GetStackCount(connId, actualSlot);

            if (stackCount > 1)
            {
                int newCount = stackCount - 1;
                _server.SetStackCount(connId, actualSlot, newCount);

                var useWriter = new LEWriter();
                useWriter.WriteByte(0x07);

                useWriter.WriteByte(0x35);
                useWriter.WriteUInt16(componentId);
                useWriter.WriteByte(0x1F);
                useWriter.WriteUInt32(clientItemId);
                _server.WritePlayerEntitySynch(conn, useWriter);

                useWriter.WriteByte(0x35);
                useWriter.WriteUInt16(componentId);
                useWriter.WriteByte(0x1E);
                useWriter.WriteByte(0x0B);
                useWriter.WriteByte(0xFF);
                useWriter.WriteCString(DungeonRunners.Data.GCObject.GetPacketGCClassFor(gcLower));
                useWriter.WriteUInt32(clientItemId);
                useWriter.WriteByte(itemX);
                useWriter.WriteByte(itemY);
                useWriter.WriteByte((byte)newCount);
                useWriter.WriteByte(0x01);
                useWriter.WriteByte(0x00);
                if (gcLower.Contains("dragonjuice") || gcLower.Contains("intbuff"))
                    useWriter.WriteByte(0x00);
                useWriter.WriteByte(0x00);
                _server.WritePlayerEntitySynch(conn, useWriter);

                useWriter.WriteByte(0x06);
                _server.SendToClient(conn, useWriter.ToArray());
            }
            else
            {
                _server.FreeInventorySlots(connId, itemX, itemY, 1, 1);
                _server.RemoveInventoryItemBySlot(connId, actualSlot);

                var useWriter = new LEWriter();
                useWriter.WriteByte(0x07);
                useWriter.WriteByte(0x35);
                useWriter.WriteUInt16(componentId);
                useWriter.WriteByte(0x1F);
                useWriter.WriteUInt32(clientItemId);
                _server.WritePlayerEntitySynch(conn, useWriter);
                useWriter.WriteByte(0x06);
                _server.SendToClient(conn, useWriter.ToArray());
            }

            if (gcLower.Contains("healthpotion"))
            {
                uint healAmount = 50 * 256;
                uint maxHP = playerState.MaxHPWire;
                playerState.AvatarHP = Math.Min(playerState.AvatarHP + healAmount, maxHP);
                Debug.LogError($"[HEALTH-POTION] hp={playerState.AvatarHP / 256}/{maxHP / 256}");
                SendHPUpdate(conn, componentId, playerState);
                SendPotionModifier(conn, playerState, "PotionPAL.HealthPotion_Noob.Modifier");
                StartPotionCooldown(connId, true);
            }
            else if (gcLower.Contains("manapotion"))
            {
                uint manaAmount = 50 * 256;
                uint maxMana = playerState.MaxManaWire;
                playerState.SetCurrentMana(Math.Min(playerState.CurrentManaWire + manaAmount, maxMana));
                Debug.LogError($"[MANA-POTION] mana={playerState.CurrentManaWire / 256}/{maxMana / 256}");
                SendManaUpdate(conn, componentId, playerState);
                SendPotionModifier(conn, playerState, "PotionPAL.ManaPotion_Noob.Modifier");
                StartPotionCooldown(connId, false);
            }
            else if (gcLower.Contains("dragonjuice"))
            {
                string modPath = gcLower + ".modifier";
                float dur = gcLower.Contains("_lg") ? 120.0f : 60.0f;
                Debug.LogError($"[DRAGONJUICE] modifier={modPath} duration={dur:F1}s");
                SendBuffModifier(conn, playerState, modPath, dur);
            }
            else if (gcLower.Contains("intbuff"))
            {
                string modPath = gcLower + ".modifier";
                float dur = gcLower.Contains("_lg") ? 180.0f : 60.0f;
                Debug.LogError($"[INTBUFF] modifier={modPath} duration={dur:F1}s");
                SendBuffModifier(conn, playerState, modPath, dur);
            }

            Debug.LogError("[USE-ITEM] consumed=True");
        }

        private void ProcessUseItemPosition(RRConnection conn, LEReader reader, ushort componentId)
        {
            Debug.LogError($"[USE-ITEM-POS] sub=0x26 remaining={reader.Remaining}");
            if (reader.Remaining >= 4)
            {
                ProcessUseItem(conn, reader, componentId);
            }
            else
            {
                Debug.LogError("[USE-ITEM-POS] reason=short-read");
            }
        }

        private void ProcessUseItemTarget(RRConnection conn, LEReader reader, ushort componentId)
        {
            Debug.LogError($"[USE-ITEM-TARGET] sub=0x27 remaining={reader.Remaining}");
            if (reader.Remaining > 0)
            {
                byte[] rawData = reader.PeekRemaining();
                Debug.LogError($"[USE-ITEM-TARGET] rawBytes={rawData.Length} hex={BitConverter.ToString(rawData)}");
            }
        }

        private void ProcessSetLocal(RRConnection conn, LEReader reader, ushort componentId)
        {
            Debug.LogError($"[UNIT-CONTAINER] query sub=0x21 remaining={reader.Remaining}");
            var bytes = new List<byte>();
            while (reader.Remaining > 0)
                bytes.Add(reader.ReadByte());
            Debug.LogError($"[UNIT-CONTAINER] queryData sub=0x21 hex={BitConverter.ToString(bytes.ToArray())}");
        }

        private void ProcessClearLocal(RRConnection conn, LEReader reader, ushort componentId)
        {
            Debug.LogError($"[UNIT-CONTAINER] position sub=0x22 remaining={reader.Remaining}");
            var bytes = new List<byte>();
            while (reader.Remaining > 0)
                bytes.Add(reader.ReadByte());
            Debug.LogError($"[UNIT-CONTAINER] positionData sub=0x22 hex={BitConverter.ToString(bytes.ToArray())}");
        }

        private void ProcessPutItemXYLocal(RRConnection conn, LEReader reader, ushort componentId)
        {
            byte inventoryID = reader.ReadByte();
            byte x = reader.ReadByte();
            byte y = reader.ReadByte();
            Debug.LogError($"[UNIT-CONTAINER] place container=0x{inventoryID:X2} pos=({x},{y})");

            PlayerState playerState = _server.GetPlayerState(conn.ConnId.ToString());
            GCObject item = playerState.ActiveItem;

            if (item == null)
            {
                Debug.LogError("[UNIT-CONTAINER] place reason=no-active-item");
                return;
            }

            ItemData itemData = DatabaseLoader.FindItem(item.GCClass);
            int itemWidth = itemData?.inventoryWidth ?? 1;
            int itemHeight = itemData?.inventoryHeight ?? 1;

            if (_server.IsInventorySlotOccupied(conn.ConnId.ToString(), x, y, itemWidth, itemHeight, inventoryID))
            {
                Debug.LogError($"[UNIT-CONTAINER] place size={itemWidth}x{itemHeight} pos=({x},{y}) container=0x{inventoryID:X2} reason=occupied");
                return;
            }

            uint trackingSlot = _server.GetNextInventorySlot(conn.ConnId.ToString());

            var writer = new LEWriter();
            writer.WriteByte(0x07);

            writer.WriteByte(0x35);
            writer.WriteUInt16(componentId);
            writer.WriteByte(0x29);
            _server.WritePlayerEntitySynch(conn, writer);

            writer.WriteByte(0x35);
            writer.WriteUInt16(componentId);
            writer.WriteByte(0x1E);
            writer.WriteByte(inventoryID);

            string gcCheck = item.GCClass.ToLower();
            if (gcCheck.Contains("questitem") || gcCheck.Contains("consumable") || gcCheck.Contains("potion") || gcCheck.Contains("townportal") || gcCheck.Contains("scroll") || gcCheck.Contains("skillbook") || gcCheck.Contains("voucher"))
            {
                string connId = conn.ConnId.ToString();
                int stackCount = _server.GetStackCount(connId, 0xFFFFFFFF);
                if (stackCount <= 0) stackCount = 1;

                writer.WriteByte(0xFF);
                writer.WriteCString(DungeonRunners.Data.GCObject.GetPacketGCClassFor(gcCheck));
                writer.WriteUInt32(trackingSlot);
                writer.WriteByte(x);
                writer.WriteByte(y);
                writer.WriteByte((byte)stackCount);
                writer.WriteByte(0x01);
                writer.WriteByte(0x00);
                if (gcCheck.Contains("dragonjuice") || gcCheck.Contains("intbuff"))
                    writer.WriteByte(0x00);
                writer.WriteByte(0x00);

                _server.SetStackCount(connId, trackingSlot, stackCount, inventoryID);
                _server.SetStackCount(connId, 0xFFFFFFFF, 0);
            }
            else
            {
                int itemLevel = item.GetItemRequiredLevel();
                item.WriteInitForInventory(writer, x, y, trackingSlot, itemLevel);
            }
            _server.WritePlayerEntitySynch(conn, writer);

            writer.WriteByte(0x06);

            _server.SendToClient(conn, writer.ToArray());
            _server.OccupyInventorySlots(conn.ConnId.ToString(), x, y, itemWidth, itemHeight, inventoryID);
            _server.TrackInventoryItem(conn.ConnId.ToString(), trackingSlot, item, x, y, inventoryID);

            playerState.ActiveItem = null;
            _server.SavePlayerInventoryPublic(conn);
            Debug.LogError($"[UNIT-CONTAINER] placed item={item.GCClass} pos=({x},{y}) container=0x{inventoryID:X2}");
        }

        private void ProcessGetItemLocal(RRConnection conn, LEReader reader, ushort componentId)
        {
            uint index = reader.ReadUInt32();
            Debug.LogError($"[UNIT-CONTAINER] pickup slot={index}");
            string connId = conn.ConnId.ToString();
            PlayerState playerState = _server.GetPlayerState(connId);

            if (playerState.ActiveItem != null)
            {
                Debug.LogError($"[UNIT-CONTAINER] pickup activeItem={playerState.ActiveItem.GCClass} reason=cursor-occupied");
                return;
            }

            byte sourceContainer = _server.FindContainerForSlot(connId, index) ?? (byte)0x0B;
            Debug.LogError($"[UNIT-CONTAINER] pickup sourceContainer=0x{sourceContainer:X2}");

            var itemData = _server.GetAndRemoveInventoryItem(connId, index, sourceContainer);
            if (itemData == null)
            {
                Debug.LogError($"[UNIT-CONTAINER] pickup slot={index} container=0x{sourceContainer:X2} reason=missing");
                return;
            }

            GCObject item = itemData.Value.item;
            byte storedX = itemData.Value.x;
            byte storedY = itemData.Value.y;

            ItemData dbItem = DatabaseLoader.FindItem(item.GCClass);
            int itemWidth = dbItem?.inventoryWidth ?? 1;
            int itemHeight = dbItem?.inventoryHeight ?? 1;

            string gcLower = item.GCClass.ToLower();

            if (gcLower.Contains("questitem"))
            {
                int qiStackCount = _server.GetStackCount(connId, index, sourceContainer);
                if (qiStackCount <= 0) qiStackCount = 1;
                _server.FreeInventorySlots(connId, storedX, storedY, itemWidth, itemHeight, sourceContainer);

                var questWriter = new LEWriter();
                questWriter.WriteByte(0x07);

                questWriter.WriteByte(0x35);
                questWriter.WriteUInt16(componentId);
                questWriter.WriteByte(0x1F);
                questWriter.WriteUInt32(index);
                _server.WritePlayerEntitySynch(conn, questWriter);

                questWriter.WriteByte(0x35);
                questWriter.WriteUInt16(componentId);
                questWriter.WriteByte(0x28);
                questWriter.WriteByte(0xFF);
                questWriter.WriteCString(DungeonRunners.Data.GCObject.GetPacketGCClassFor(gcLower));
                questWriter.WriteUInt32(0x00);
                questWriter.WriteByte(0x00);
                questWriter.WriteByte(0x00);
                questWriter.WriteByte((byte)(qiStackCount > 255 ? 255 : qiStackCount));
                questWriter.WriteByte(0x01);
                questWriter.WriteByte(0x00);
                questWriter.WriteByte(0x00);
                _server.WritePlayerEntitySynch(conn, questWriter);

                questWriter.WriteByte(0x06);
                _server.SendToClient(conn, questWriter.ToArray());

                playerState.ActiveItem = item;
                _server.SetStackCount(connId, 0xFFFFFFFF, qiStackCount);
                Debug.LogError($"[UNIT-CONTAINER] cursor item={item.GCClass} count={qiStackCount} kind=quest");
                return;
            }

            if (gcLower.Contains("consumable") || gcLower.Contains("potion")
                || gcLower.Contains("skillbook") || gcLower.Contains("voucher"))
            {
                int stackCount = _server.GetStackCount(connId, index, sourceContainer);
                _server.FreeInventorySlots(connId, storedX, storedY, itemWidth, itemHeight, sourceContainer);

                var consWriter = new LEWriter();
                consWriter.WriteByte(0x07);

                consWriter.WriteByte(0x35);
                consWriter.WriteUInt16(componentId);
                consWriter.WriteByte(0x1F);
                consWriter.WriteUInt32(index);
                _server.WritePlayerEntitySynch(conn, consWriter);

                consWriter.WriteByte(0x35);
                consWriter.WriteUInt16(componentId);
                consWriter.WriteByte(0x28);
                consWriter.WriteByte(0xFF);
                consWriter.WriteCString(DungeonRunners.Data.GCObject.GetPacketGCClassFor(gcLower));
                consWriter.WriteUInt32(0x00);
                consWriter.WriteByte(0x00);
                consWriter.WriteByte(0x00);
                consWriter.WriteByte((byte)(stackCount > 0 ? stackCount : 1));
                consWriter.WriteByte(0x01);
                consWriter.WriteByte(0x00);
                if (gcLower.Contains("dragonjuice") || gcLower.Contains("intbuff"))
                    consWriter.WriteByte(0x00);
                consWriter.WriteByte(0x00);
                _server.WritePlayerEntitySynch(conn, consWriter);

                consWriter.WriteByte(0x06);
                _server.SendToClient(conn, consWriter.ToArray());

                playerState.ActiveItem = item;
                _server.SetStackCount(connId, 0xFFFFFFFF, stackCount);
                Debug.LogError($"[UNIT-CONTAINER] cursor item={item.GCClass} count={stackCount} kind=consumable");
                return;
            }

            _server.FreeInventorySlots(connId, storedX, storedY, itemWidth, itemHeight, sourceContainer);

            var writer = new LEWriter();
            writer.WriteByte(0x07);

            writer.WriteByte(0x35);
            writer.WriteUInt16(componentId);
            writer.WriteByte(0x1F);
            writer.WriteUInt32(index);
            _server.WritePlayerEntitySynch(conn, writer);

            writer.WriteByte(0x35);
            writer.WriteUInt16(componentId);
            writer.WriteByte(0x28);
            item.WriteInitWithoutWeaponBytes(writer, playerState.Level);
            _server.WritePlayerEntitySynch(conn, writer);

            writer.WriteByte(0x06);

            _server.SendToClient(conn, writer.ToArray());

            playerState.ActiveItem = item;
            Debug.LogError($"[UNIT-CONTAINER] cursor item={item.GCClass}");
        }

        private void SendHPUpdate(RRConnection conn, ushort componentId, PlayerState playerState)
        {
            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x35);
            writer.WriteUInt16(componentId);
            writer.WriteByte(0x00);
            _server.WritePlayerEntitySynch(conn, writer);
            writer.WriteByte(0x06);
            _server.SendToClient(conn, writer.ToArray());
        }

        private void SendManaUpdate(RRConnection conn, ushort componentId, PlayerState playerState)
        {
            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x35);
            writer.WriteUInt16(componentId);
            writer.WriteByte(0x00);
            _server.WritePlayerEntitySynch(conn, writer);
            writer.WriteByte(0x06);
            _server.SendToClient(conn, writer.ToArray());
        }

        private void SendPotionModifier(RRConnection conn, PlayerState playerState, string gcType)
        {
            string connId = conn.ConnId.ToString();
            uint instanceId = _nextPotionModId++;

            bool isHealth = gcType.Contains("health");
            var activePotionModifierIds = isHealth ? _activeHealthModId : _activeManaModId;

            if (activePotionModifierIds.ContainsKey(connId) && activePotionModifierIds[connId] != 0)
            {
                uint oldId = activePotionModifierIds[connId];
                var removeWriter = new LEWriter();
                removeWriter.WriteByte(0x07);
                removeWriter.WriteByte(0x35);
                removeWriter.WriteUInt16(conn.ModifiersId);
                removeWriter.WriteByte(0x01);
                removeWriter.WriteUInt32(oldId);
                _server.WritePlayerEntitySynch(conn, removeWriter);
                removeWriter.WriteByte(0x06);
                _server.SendToClient(conn, removeWriter.ToArray());
            }

            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x35);
            writer.WriteUInt16(conn.ModifiersId);
            writer.WriteByte(0x00);
            writer.WriteByte(0xFF);
            writer.WriteCString(gcType);
            writer.WriteUInt32(instanceId);
            writer.WriteByte(0);
            writer.WriteUInt32(0);
            writer.WriteUInt32(0);
            writer.WriteByte(0);
            _server.WritePlayerEntitySynch(conn, writer);
            writer.WriteByte(0x06);
            _server.SendToClient(conn, writer.ToArray());

            activePotionModifierIds[connId] = instanceId;

            float duration = 5.0f;
            _server.StartCoroutine(RemovePotionModifierAfterDelay(conn, playerState, instanceId, duration, isHealth));
        }

        private IEnumerator RemovePotionModifierAfterDelay(
            RRConnection conn, PlayerState playerState, uint modInstanceId, float delay, bool isHealth)
        {
            yield return new WaitForSeconds(delay);

            string connId = conn.ConnId.ToString();
            var activePotionModifierIds = isHealth ? _activeHealthModId : _activeManaModId;

            if (activePotionModifierIds.ContainsKey(connId) && activePotionModifierIds[connId] == modInstanceId)
            {
                var removeWriter = new LEWriter();
                removeWriter.WriteByte(0x07);
                removeWriter.WriteByte(0x35);
                removeWriter.WriteUInt16(conn.ModifiersId);
                removeWriter.WriteByte(0x01);
                removeWriter.WriteUInt32(modInstanceId);
                _server.WritePlayerEntitySynch(conn, removeWriter);
                removeWriter.WriteByte(0x06);
                _server.SendToClient(conn, removeWriter.ToArray());

                activePotionModifierIds[connId] = 0;
                Debug.LogError($"[POTION] removed type={(isHealth ? "health" : "mana")} modifier={modInstanceId} delay={delay:F1}s");
            }
        }

        private void SendBuffModifier(RRConnection conn, PlayerState playerState, string gcType, float durationSec)
        {
            string connId = conn.ConnId.ToString();
            uint instanceId = _nextPotionModId++;
            uint durationTicks = (uint)(durationSec * 30);

            string buffType = gcType.Contains("dragonjuice") ? "dragonjuice" : "intbuff";
            string buffKey = connId + ":" + buffType;

            if (_activeBuffModId.ContainsKey(buffKey) && _activeBuffModId[buffKey] != 0)
            {
                uint oldId = _activeBuffModId[buffKey];
                var removeWriter = new LEWriter();
                removeWriter.WriteByte(0x07);
                removeWriter.WriteByte(0x35);
                removeWriter.WriteUInt16(conn.ModifiersId);
                removeWriter.WriteByte(0x01);
                removeWriter.WriteUInt32(oldId);
                _server.WritePlayerEntitySynch(conn, removeWriter);
                removeWriter.WriteByte(0x06);
                _server.SendToClient(conn, removeWriter.ToArray());
            }

            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x35);
            writer.WriteUInt16(conn.ModifiersId);
            writer.WriteByte(0x00);
            writer.WriteByte(0xFF);
            writer.WriteCString(gcType);
            writer.WriteUInt32(instanceId);
            writer.WriteByte(0x01);
            writer.WriteUInt32(0);
            writer.WriteUInt32(durationTicks);
            writer.WriteByte(0x01);
            _server.WritePlayerEntitySynch(conn, writer);
            writer.WriteByte(0x06);
            _server.SendToClient(conn, writer.ToArray());

            _activeBuffModId[buffKey] = instanceId;

            _server.RecordModifierSent(conn.LoginName, gcType, instanceId,
                level: 1, powerLevel: 0, duration: durationTicks, sourceIsSelf: 1);

            _server.StartCoroutine(RemoveBuffModifierAfterDelay(conn, playerState, instanceId, durationSec, buffKey, gcType));
            Debug.LogError($"[BUFF] applied={gcType} id={instanceId} duration={durationSec:F1}s ticks={durationTicks}");
        }

        private IEnumerator RemoveBuffModifierAfterDelay(
            RRConnection conn, PlayerState playerState, uint modInstanceId, float delay, string buffKey, string gcType)
        {
            yield return new WaitForSeconds(delay);

            if (_activeBuffModId.ContainsKey(buffKey) && _activeBuffModId[buffKey] == modInstanceId)
            {
                var removeWriter = new LEWriter();
                removeWriter.WriteByte(0x07);
                removeWriter.WriteByte(0x35);
                removeWriter.WriteUInt16(conn.ModifiersId);
                removeWriter.WriteByte(0x01);
                removeWriter.WriteUInt32(modInstanceId);
                _server.WritePlayerEntitySynch(conn, removeWriter);
                removeWriter.WriteByte(0x06);
                _server.SendToClient(conn, removeWriter.ToArray());

                _activeBuffModId[buffKey] = 0;
                _server.UntrackModifier(conn.LoginName, gcType);
                Debug.LogError($"[BUFF] removed modifier={modInstanceId} key={buffKey} delay={delay:F1}s");
            }
        }

        private void ProcessDropItem(RRConnection conn, LEReader reader, ushort componentId)
        {
            string connId = conn.ConnId.ToString();
            PlayerState playerState = _server.GetPlayerState(connId);
            GCObject item = playerState.ActiveItem;

            if (item == null)
            {
                Debug.LogError("[DROP] reason=no-active-item");
                return;
            }

            float playerX = conn.PlayerPosX;
            float playerY = conn.PlayerPosY;
            ushort entityId = _server.GetNextLootEntityId();
            int fx = (int)(playerX * 256);
            int fy = (int)(playerY * 256);

            float dropZ = conn.PlayerPosZ + 4.0f;

            var body = new LEWriter();

            body.WriteByte(0x35);
            body.WriteUInt16(componentId);
            body.WriteByte(0x29);
            _server.WritePlayerEntitySynch(conn, body);

            body.WriteByte(0x01);
            body.WriteUInt16(entityId);
            body.WriteByte(0xFF);
            body.WriteCString("itemobject");

            body.WriteByte(0x02);
            body.WriteUInt16(entityId);
            body.WriteUInt32(0x00000006);
            body.WriteInt32(fx);
            body.WriteInt32(fy);
            int fz = (int)(dropZ * 256);
            body.WriteInt32(fz);
            body.WriteInt32(0);
            body.WriteByte(0xF7);
            body.WriteUInt16(0x0000);
            body.WriteByte(0x00);
            body.WriteUInt32(0x00000000);

            body.WriteByte(0x00);
            body.WriteUInt16(0x2233);
            body.WriteUInt32(0x00000000);
            body.WriteInt32(fx);
            body.WriteInt32(fy);
            body.WriteByte(0xBA);

            item.WriteInitForDroppedItem(body, playerState.Level);

            var chanWriter = new LEWriter();
            chanWriter.WriteByte(0x07);
            chanWriter.WriteBytes(body.ToArray());
            chanWriter.WriteByte(0x06);

            _server.SendToClient(conn, chanWriter.ToArray());
            int cursorQty = _server.GetStackCount(connId, 0xFFFFFFFF);
            if (cursorQty <= 0) cursorQty = 1;
            _server.TrackDroppedItem(entityId, item, conn, cursorQty);
            _server.SetStackCount(connId, 0xFFFFFFFF, 0);
            playerState.ActiveItem = null;
            Debug.LogError($"[DROP] item={item.GCClass} entity={entityId}");

            var otherBody = new LEWriter();
            otherBody.WriteByte(0x01);
            otherBody.WriteUInt16(entityId);
            otherBody.WriteByte(0xFF);
            otherBody.WriteCString("itemobject");
            otherBody.WriteByte(0x02);
            otherBody.WriteUInt16(entityId);
            otherBody.WriteUInt32(0x00000006);
            otherBody.WriteInt32(fx);
            otherBody.WriteInt32(fy);
            otherBody.WriteInt32(fz);
            otherBody.WriteInt32(0);
            otherBody.WriteByte(0xF7);
            otherBody.WriteUInt16(0x0000);
            otherBody.WriteByte(0x00);
            otherBody.WriteUInt32(0x00000000);
            otherBody.WriteByte(0x00);
            otherBody.WriteUInt16(0x2233);
            otherBody.WriteUInt32(0x00000000);
            otherBody.WriteInt32(fx);
            otherBody.WriteInt32(fy);
            otherBody.WriteByte(0xBA);
            item.WriteInitForDroppedItem(otherBody, playerState.Level);

            var otherChan = new LEWriter();
            otherChan.WriteByte(0x07);
            otherChan.WriteBytes(otherBody.ToArray());
            otherChan.WriteByte(0x06);
            byte[] otherPacket = otherChan.ToArray();

            foreach (var other in _server.GetConnections().Values)
            {
                if (other == conn) continue;
                if (!other.IsSpawned) continue;
                if (other.CurrentZoneGcType != conn.CurrentZoneGcType) continue;
                if (other.InstanceId != conn.InstanceId) continue;
                _server.SendToClient(other, otherPacket);
            }
            Debug.LogError("[DROP] broadcast=other-players");
        }
    }
}
