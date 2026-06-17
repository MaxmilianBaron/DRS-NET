using DungeonRunners.Utilities;
using DungeonRunners.Engine;
using DungeonRunners.Data;
using DungeonRunners.Gameplay;
using System;
using System.Collections.Generic;

namespace DungeonRunners.Networking
{
    public class Equipment
    {
        private GameServer _server;

        public Equipment(GameServer server)
        {
            _server = server;
        }

        public void ProcessRequest(RRConnection conn, LEReader reader, ushort componentId, byte subMessage)
        {
            Debug.LogError($"[EQUIPMENT] update component=0x{componentId:X4} sub=0x{subMessage:X2}");

            switch (subMessage)
            {
                case 0x21:
                    Debug.LogError($"[EQUIPMENT] query sub=0x21 remaining={reader.Remaining}");
                    List<byte> bytes21 = new List<byte>();
                    while (reader.Remaining > 0)
                    {
                        byte b = reader.ReadByte();
                        bytes21.Add(b);
                    }
                    if (bytes21.Count > 0)
                        Debug.LogError($"[EQUIPMENT] queryData sub=0x21 hex={BitConverter.ToString(bytes21.ToArray())}");
                    break;

                case 0x22:
                    Debug.LogError($"[EQUIPMENT] position sub=0x22 remaining={reader.Remaining}");
                    List<byte> bytes22 = new List<byte>();
                    while (reader.Remaining > 0)
                    {
                        byte b = reader.ReadByte();
                        bytes22.Add(b);
                    }
                    if (bytes22.Count > 0)
                        Debug.LogError($"[EQUIPMENT] positionData sub=0x22 hex={BitConverter.ToString(bytes22.ToArray())}");
                    break;

                case 0x28:
                    ProcessEquipItem(conn, reader, componentId);
                    break;

                case 0x29:
                    ProcessUnEquipItem(conn, reader, componentId);
                    break;

                default:
                    Debug.LogError($"[EQUIPMENT] sub=0x{subMessage:X2} reason=unhandled");
                    break;
            }
        }

        private void ProcessUnEquipItem(RRConnection conn, LEReader reader, ushort componentId)
        {
            uint slot = reader.ReadUInt32();

            Debug.LogError($"[EQUIPMENT] unequip slot=0x{slot:X2}");

            PlayerState playerState = _server.GetPlayerState(conn.ConnId.ToString());

            GCObject equippedItem = GetEquippedItemAtSlot(conn, slot);

            if (equippedItem == null)
            {
                Debug.LogError($"[EQUIPMENT] unequip slot={slot} reason=empty");
                return;
            }

            Debug.LogError($"[EQUIPMENT] unequip item={equippedItem.GCClass}");

            Debug.LogError($"[EQUIPMENT] entitySynchInfoHP={playerState.EntitySynchInfoHP}");

            ushort unitContainerComponentId = GetUnitContainerComponentId(conn);
            ushort manipulatorsComponentId = GetManipulatorsComponentId(conn);

            Debug.LogError($"[EQUIPMENT] equipmentId=0x{componentId:X4}");
            Debug.LogError($"[EQUIPMENT] unitContainerId=0x{unitContainerComponentId:X4}");
            Debug.LogError($"[EQUIPMENT] manipulatorsId=0x{manipulatorsComponentId:X4}");

            var writer = new LEWriter();

            writer.WriteByte(0x07);

            writer.WriteByte(0x35);
            writer.WriteUInt16(componentId);
            writer.WriteByte(0x29);
            writer.WriteUInt32(slot);
            WriteEquipmentEntitySynch(writer, conn);

            writer.WriteByte(0x35);
            writer.WriteUInt16(unitContainerComponentId);
            writer.WriteByte(0x28);
            equippedItem.WriteInitWithoutWeaponBytes(writer, playerState.Level);
            WriteEquipmentEntitySynch(writer, conn);

            writer.WriteByte(0x35);
            writer.WriteUInt16(manipulatorsComponentId);
            writer.WriteByte(0x01);
            writer.WriteUInt32(slot);
            WriteEquipmentEntitySynch(writer, conn);

            writer.WriteByte(0x06);

            byte[] packet = writer.ToArray();
            Debug.LogError($"[EQUIPMENT] packet=unequip bytes={packet.Length}");
            Debug.LogError($"[EQUIPMENT] packetHex={BitConverter.ToString(packet)}");
            _server.SendToClient(conn, packet);

            _server.RemoveEquippedItem(conn.ConnId.ToString(), slot);
            _server.CalculateEquipmentBonuses(conn.ConnId.ToString(), conn.Avatar);
            Debug.LogError($"[EQUIPMENT] entitySynchInfoHPAfter={playerState.EntitySynchInfoHP}");

            playerState.ActiveItem = equippedItem;

            Debug.LogError($"[EQUIPMENT] unequipped slot={slot} activeItem={equippedItem.GCClass}");
        }

        private void ProcessEquipItem(RRConnection conn, LEReader reader, ushort componentId)
        {
            uint slot = reader.ReadUInt32();
            Debug.LogError($"[EQUIPMENT] equip slot=0x{slot:X2}");

            PlayerState playerState = _server.GetPlayerState(conn.ConnId.ToString());
            GCObject item = playerState.ActiveItem;

            if (item == null)
            {
                Debug.LogError("[EQUIPMENT] equip reason=no-active-item");
                return;
            }

            if (item.GCClass.StartsWith("QuestItemPAL", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError($"[EQUIPMENT] equip item={item.GCClass} reason=quest-item");
                return;
            }

            item.TargetSlot = null;
            uint correctSlot = item.GetEquipmentSlotFromGCClass();
            if (slot != correctSlot)
            {
                if (correctSlot == 10 && slot == 11 && !IsTwoHandedWeapon(item))
                {
                    Debug.LogError($"[EQUIPMENT] equip item={item.GCClass} slot=11 reason=dual-wield");
                }
                else if ((correctSlot == 3 || correctSlot == 4) && (slot == 3 || slot == 4))
                {
                    Debug.LogError($"[EQUIPMENT] equip item={item.GCClass} slot={slot} reason=ring-slot");
                }
                else
                {
                    Debug.LogError($"[EQUIPMENT] equip item={item.GCClass} slot={slot} correctSlot={correctSlot} reason=slot-mismatch");
                    return;
                }
            }
            Debug.LogError($"[EQUIPMENT] equip item={item.GCClass} slot={slot} slotValid=True");

            {
                int itemLevel = item.StoredLevel >= 0 ? item.StoredLevel : RPGSettings.GetItemLevel(item.GCClass);
                int requiredLevel = Math.Max(0, Math.Min(100, itemLevel - 5));

                if (playerState.Level < requiredLevel)
                {
                    Debug.LogError($"[EQUIPMENT] equip item={item.GCClass} requiredLevel={requiredLevel} playerLevel={playerState.Level} storedLevel={item.StoredLevel} reason=level");
                    return;
                }
                Debug.LogError($"[EQUIPMENT] levelCheck playerLevel={playerState.Level} requiredLevel={requiredLevel} storedLevel={item.StoredLevel}");

                if (RequiresMembership(item) && _server.IsPlayerFreePublic(conn.LoginName))
                {
                    Debug.LogError($"[EQUIPMENT] equip item={item.GCClass} reason=membership");
                    return;
                }
            }

            {
                bool is2HWeapon = IsTwoHandedWeapon(item);

                if (is2HWeapon && slot == 10)
                {
                    GCObject equippedOffhand = GetEquippedItemAtSlot(conn, 11);
                    if (equippedOffhand != null)
                    {
                        Debug.LogError($"[EQUIPMENT] equip item={item.GCClass} offhand={equippedOffhand.GCClass} reason=two-hand-offhand");
                        return;
                    }
                }
                else if (slot == 11)
                {
                    GCObject equippedWeapon = GetEquippedItemAtSlot(conn, 10);
                    if (equippedWeapon != null && IsTwoHandedWeapon(equippedWeapon))
                    {
                        Debug.LogError($"[EQUIPMENT] equip item={item.GCClass} weapon={equippedWeapon.GCClass} reason=offhand-two-hand");
                        return;
                    }
                }
            }

            uint itemCorrectSlot = item.GetEquipmentSlotFromGCClass();
            if (itemCorrectSlot == 10 && slot == 11)
            {
                item.TargetSlot = 11;
                Debug.LogError($"[EQUIPMENT] targetSlot=11 item={item.GCClass}");
            }
            else if (itemCorrectSlot == 10 && slot == 10)
            {
                item.TargetSlot = null;
                Debug.LogError($"[EQUIPMENT] targetSlot=primary item={item.GCClass}");
            }

            GCObject existingItem = GetEquippedItemAtSlot(conn, slot);
            Debug.LogError($"[EQUIPMENT] equip item={item.GCClass}");

            if (existingItem != null)
            {
                Debug.LogError($"[EQUIPMENT] equip slot={slot} existing={existingItem.GCClass} swap=True");
            }

            _server.TrackEquippedItem(conn.ConnId.ToString(), slot, item);
            _server.CalculateEquipmentBonuses(conn.ConnId.ToString(), conn.Avatar);
            Debug.LogError($"[EQUIPMENT] entitySynchInfoHPAfterRecalc={playerState.EntitySynchInfoHP}");

            ushort unitContainerComponentId = GetUnitContainerComponentId(conn);
            ushort manipulatorsComponentId = GetManipulatorsComponentId(conn);

            var writer = new LEWriter();
            writer.WriteByte(0x07);

            if (existingItem != null)
            {
                writer.WriteByte(0x35);
                writer.WriteUInt16(componentId);
                writer.WriteByte(0x29);
                writer.WriteUInt32(slot);
                WriteEquipmentEntitySynch(writer, conn);

                writer.WriteByte(0x35);
                writer.WriteUInt16(manipulatorsComponentId);
                writer.WriteByte(0x01);
                writer.WriteUInt32(slot);
                WriteEquipmentEntitySynch(writer, conn);
            }

            writer.WriteByte(0x35);
            writer.WriteUInt16(componentId);
            writer.WriteByte(0x28);
            item.WriteInitWithoutWeaponBytes(writer, playerState.Level);
            WriteEquipmentEntitySynch(writer, conn);

            writer.WriteByte(0x35);
            writer.WriteUInt16(unitContainerComponentId);
            if (existingItem != null)
            {
                writer.WriteByte(0x28);
                existingItem.WriteInitWithoutWeaponBytes(writer, playerState.Level);
            }
            else
            {
                writer.WriteByte(0x29);
            }
            WriteEquipmentEntitySynch(writer, conn);

            writer.WriteByte(0x35);
            writer.WriteUInt16(manipulatorsComponentId);
            writer.WriteByte(0x00);
            item.WriteInitWithoutWeaponBytes(writer, playerState.Level);
            WriteEquipmentEntitySynch(writer, conn);

            writer.WriteByte(0x06);

            byte[] packet = writer.ToArray();
            Debug.LogError($"[EQUIPMENT] packet=equip bytes={packet.Length}");
            _server.SendToClient(conn, packet);

            if (existingItem != null)
            {
                playerState.ActiveItem = existingItem;
                Debug.LogError($"[EQUIPMENT] equipped={item.GCClass} activeItem={existingItem.GCClass} swap=True");
            }
            else
            {
                playerState.ActiveItem = null;
                Debug.LogError($"[EQUIPMENT] equipped={item.GCClass} slot={slot}");
            }

            Debug.LogError("[EQUIPMENT] save=post-equip");
        }

        private static bool IsTwoHandedWeapon(GCObject item)
        {
            if (item == null) return false;
            if (GCObject.TryResolveWeaponClass(item.GCClass, out string weaponClass))
                return IsTwoHandedWeaponClass(weaponClass);
            string gcLow = item.GCClass?.ToLowerInvariant() ?? string.Empty;
            return gcLow.Contains("2h");
        }

        private static bool IsTwoHandedWeaponClass(string weaponClass)
        {
            string value = weaponClass?.Trim().ToUpperInvariant() ?? string.Empty;
            return value == "2HRANGED"
                || value == "2HMELEE"
                || value == "2HMACE"
                || value == "2HSWORD"
                || value == "2HAXE"
                || value == "2HCANNON"
                || value == "2HCROSSBOW"
                || value == "2HBOW"
                || value == "2HGUN"
                || value == "POLEARM";
        }

        private static bool RequiresMembership(GCObject item)
        {
            GCNode desc = GCObject.ResolveItemDescription(item?.GCClass);
            if (desc == null) return false;
            if (desc.GetBool("ForceRequiresMembership", false)) return true;
            if (desc.GetBool("ForceNotRequiresMembership", false)) return false;
            return desc.GetBool("RequiresMembership", false);
        }

        private void WriteEquipmentEntitySynch(LEWriter writer, RRConnection conn)
        {
            _server.WritePlayerEntitySynch(conn, writer);
        }

        private GCObject GetEquippedItemAtSlot(RRConnection conn, uint slot)
        {
            return _server.GetEquippedItem(conn.ConnId.ToString(), slot);
        }

        private ushort GetUnitContainerComponentId(RRConnection conn)
        {
            return _server.GetUnitContainerComponentId(conn.ConnId.ToString());
        }

        private ushort GetManipulatorsComponentId(RRConnection conn)
        {
            return _server.GetManipulatorsComponentId(conn.ConnId.ToString());
        }
    }
}
