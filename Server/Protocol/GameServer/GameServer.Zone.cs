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
        private Dictionary<int, uint> _pendingZoneConnectSeeds = new Dictionary<int, uint>();
        private Dictionary<string, uint> _zoneInstanceLayoutSeeds = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, uint> _zoneInstanceRoomSeeds = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, uint> _soloDungeonInstanceIds = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, DateTime> _soloDungeonLastActiveUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private const double SoloDungeonMemorySeconds = 10 * 60;
        private uint _nextSoloDungeonInstanceId = 0x80000000u;
        private HashSet<string> _loggedRuntimeZoneSeeds = new HashSet<string>();
        private Dictionary<uint, Zone> _zones = new Dictionary<uint, Zone>();
        private Dictionary<uint, List<ZoneNPC>> _zoneNPCs = new Dictionary<uint, List<ZoneNPC>>();
        private Dictionary<uint, List<ZonePortal>> _zonePortals = new Dictionary<uint, List<ZonePortal>>();
        private Dictionary<uint, List<ZoneCheckpoint>> _zoneCheckpoints = new Dictionary<uint, List<ZoneCheckpoint>>();
        private Dictionary<ushort, ZonePortal> _portalEntities = new Dictionary<ushort, ZonePortal>();
        private Dictionary<ushort, ZoneCheckpoint> _checkpointEntities = new Dictionary<ushort, ZoneCheckpoint>();
        private HashSet<ushort> _activatedWorldEntities = new HashSet<ushort>();
        private readonly Dictionary<int, List<ushort>> _portalEntitiesByConn = new Dictionary<int, List<ushort>>();
        private readonly Dictionary<int, List<ushort>> _checkpointEntitiesByConn = new Dictionary<int, List<ushort>>();
        private Dictionary<int, List<ZoneNPC>> _adminShopNPCs = new Dictionary<int, List<ZoneNPC>>();

        private static readonly Dictionary<string, GotoGCData> _gotoGCByLabel =
            new Dictionary<string, GotoGCData>(StringComparer.OrdinalIgnoreCase)
        {
            { "Go to Dew Valley", new GotoGCData("Tutorial", "", 0) },
            { "Algor's Terror-Dome Base Camp", new GotoGCData("dungeon02_level00", "", 0) },
            { "Algor's Terror-Dome", new GotoGCData("dungeon02_level01", "", 0) },
            { "Explore Subterrorania", new GotoGCData("dungeon02_level01_off1a", "", 0) },
            { "Explore Arborgeddon", new GotoGCData("dungeon02_level03_off1a", "", 0) },
            { "Explore Haterium", new GotoGCData("dungeon02_level04_off1a", "", 0) },
            { "Explore Grave Forge", new GotoGCData("dungeon02_level05_off1a", "", 0) },
            { "Explore Plague Plant", new GotoGCData("dungeon02_level07_off1a", "", 0) },
            { "Explore Abyss Mill", new GotoGCData("dungeon02_level07_off2a", "", 0) },
            { "Locate Entrance to Rattle Tooth's Lair", new GotoGCData("dungeon00_level03", "boss_spawn", 200) },
            { "Find the Townston Commander", new GotoGCData("town", "Guard_Commander", 150) },
            { "Locate the Farm on Level 1", new GotoGCData("dungeon15_level01", "farm", 50) },
            { "Locate the Farm on Level 1 Branch", new GotoGCData("dungeon15_level01_off1a", "farm", 50) },
            { "Locate the Farm on Level 2", new GotoGCData("dungeon15_level02", "farm", 50) },
            { "Locate the Farm on Level 3", new GotoGCData("dungeon15_level03", "farm", 50) },
            { "Locate the Farm on Level 3 Branch", new GotoGCData("dungeon15_level03_off1a", "farm", 50) },
            { "Locate the Farm on Level 4", new GotoGCData("dungeon15_level04", "farm", 50) },
            { "Locate the Farm on Level 4 Branch", new GotoGCData("dungeon15_level04_off1a", "farm", 50) },
            { "Farm on Level 1", new GotoGCData("dungeon15_level01", "farm", 50) },
            { "Farm on Level 1 Branch", new GotoGCData("dungeon15_level01_off1a", "farm", 50) },
            { "Farm on Level 2", new GotoGCData("dungeon15_level02", "farm", 50) },
            { "Located the ''Mutant Operations Center''", new GotoGCData("dungeon01_level05", "off1", 150) },
            { "Explored the ''Mutant Operations Center''", new GotoGCData("dungeon01_level05_off1a", "level05_off1a_teleporter", 150) },
            { "Located the ''Rumored Refinery''", new GotoGCData("dungeon01_level04", "off1", 150) },
            { "Explore the ''Rumored Refinery''", new GotoGCData("dungeon01_level04_off1a", "level04_off1a_teleporter", 150) },
            { "Locate Sissirat's Revenge", new GotoGCData("dungeon15_level04_off1a_lair", "", 0) },
            { "Locate Vergrim's Revenge", new GotoGCData("dungeon15_level07_off1a_lair", "", 0) },
            { "Survey the Orok Outpost", new GotoGCData("dungeon01_level01_off1a", "", 0) },
            { "Survey Capwn's Family Ratstaurant", new GotoGCData("dungeon01_level03_off1a", "", 0) },
            { "Survey Not the Rumored Refinery", new GotoGCData("dungeon01_level04_off1a", "", 0) },
            { "Survey Not the Mutant Operation Center", new GotoGCData("dungeon01_level05_off1a", "", 0) },
            { "Survey Little Dew Valley Forest", new GotoGCData("dungeon01_level07_off1a", "", 0) },
            { "Survey Porthole Laboratories", new GotoGCData("dungeon01_level07_off2a", "", 0) },
            { "Go to Townston", new GotoGCData("Town", "", 0) },
        };
        private const float ZoneSpawnInvulnerabilityTicksPerSecond = 30f;
        private const float ZoneSpawnInvulnerabilityAddDelay = 0f;

        private IEnumerator SendZoneSpawnInvulnerabilityAfterDelay(RRConnection conn, float delay)
        {
            if (delay > 0f)
                yield return new WaitForSeconds(delay);
            if (conn == null || !conn.IsConnected || !conn.IsSpawned) yield break;
            SendZoneSpawnInvulnerability(conn);
        }

        private void SetZoneSpawnInvulnerability(RRConnection conn)
        {
            if (conn == null) return;
            conn.ZoneSpawnInvulnerabilityActive = true;
            conn.ZoneSpawnInvulnerabilityExpiresAt = Time.time + ZoneSpawnInvulnerabilityDuration / ZoneSpawnInvulnerabilityTicksPerSecond;
            conn.ZoneSpawnInvulnerabilitySentAt = Time.time;
            conn.ZoneSpawnInvulnerabilityClearFrame = -1;
            PlayerState state = GetPlayerState(conn.ConnId.ToString());
            if (state != null)
                state.IsZoneSpawnDamageImmune = true;
            Debug.LogError($"[ZONE-INVULN] Server immunity active for {conn.LoginName} until={conn.ZoneSpawnInvulnerabilityExpiresAt:F3}");
        }

        private void RefreshZoneSpawnInvulnerability(RRConnection conn)
        {
            if (conn == null || !conn.ZoneSpawnInvulnerabilityActive) return;
            if (conn.ZoneSpawnInvulnerabilityExpiresAt > 0f && Time.time >= conn.ZoneSpawnInvulnerabilityExpiresAt)
                ClearZoneSpawnInvulnerability(conn, "EXPIRE");
        }

        private bool IsZoneSpawnInvulnerabilityBlockingCombat(RRConnection conn)
        {
            if (conn == null) return false;
            RefreshZoneSpawnInvulnerability(conn);
            return conn.ZoneSpawnInvulnerabilityActive;
        }

        private void ClearZoneSpawnInvulnerability(RRConnection conn, string reason)
        {
            if (conn == null) return;
            PlayerState state = GetPlayerState(conn.ConnId.ToString());
            bool had = conn.ZoneSpawnInvulnerabilityActive || (state != null && state.IsZoneSpawnDamageImmune);
            if (!had) return;
            conn.ZoneSpawnInvulnerabilityActive = false;
            conn.ZoneSpawnInvulnerabilityExpiresAt = 0f;
            conn.ZoneSpawnInvulnerabilitySentAt = 0f;
            conn.ZoneSpawnInvulnerabilityClearFrame = Time.frameCount;
            if (state != null)
                state.IsZoneSpawnDamageImmune = false;
            Debug.LogError($"[ZONE-INVULN] Cleared server immunity for {conn.LoginName} reason={reason}");
        }

        private bool ShouldSendZoneSpawnInvulnerability(RRConnection conn)
        {
            if (conn == null) return false;

            string zoneName = conn.CurrentZoneName;
            if (string.IsNullOrWhiteSpace(zoneName) && conn.CurrentZoneId != 0 && _zones.TryGetValue(conn.CurrentZoneId, out Zone zone))
                zoneName = zone.name;

            return ZoneAllowsSpawnInvulnerability(zoneName);
        }

        private static bool ZoneAllowsSpawnInvulnerability(string zoneName)
        {
            zoneName = (zoneName ?? "").Trim().ToLowerInvariant();
            if (zoneName.Length == 0) return false;

            if (zoneName == "tutorial") return false;
            if (zoneName == "world.tutorial") return false;
            if (zoneName == "town") return false;
            if (zoneName == "world.town") return false;
            if (zoneName == "thehub") return false;
            if (zoneName == "world.thehub") return false;
            if (zoneName == "pvp_start") return false;
            if (zoneName == "pvp_hub") return false;
            if (zoneName.StartsWith("town")) return false;
            if (zoneName.StartsWith("world.town")) return false;
            if (zoneName.Contains("hub")) return false;

            if (zoneName == "amazon_dungeon") return true;
            if (zoneName.StartsWith("dungeon")) return true;
            if (zoneName.StartsWith("world.dungeon")) return true;
            if (zoneName.StartsWith("d0")) return true;
            if (zoneName.StartsWith("d1")) return true;
            if (zoneName.StartsWith("elite")) return true;
            if (zoneName.StartsWith("epic")) return true;
            if (zoneName.StartsWith("squeakeasy")) return true;

            return false;
        }

        private void AdminCompleteQuest(RRConnection conn, uint instanceId)
        {
            try
            {
                var completingQuest = QuestManager.Instance.GetQuestByInstanceId(conn.ConnId.ToString(), instanceId);
                if (completingQuest == null)
                {
                    Debug.LogError($"[ADMIN-COMPLETEQUEST] No active quest with instanceId={instanceId}");
                    return;
                }

                Debug.LogError($"[ADMIN-COMPLETEQUEST] Completing {completingQuest.QuestId} (instanceId={instanceId})");

                QuestData completedQuestData = DatabaseLoader.Quests
                    .FirstOrDefault(q => q.id.Equals(completingQuest.QuestId, StringComparison.OrdinalIgnoreCase));

                if (completingQuest.Objectives != null)
                {
                    foreach (var objective in completingQuest.Objectives)
                    {
                        if (objective.Required > 0 && objective.Current < objective.Required)
                            objective.Current = objective.Required;
                    }
                }

                RemoveQuestItemsFromInventory(conn, completingQuest);
                QuestManager.Instance.HandleTurnInConfirmed(conn, instanceId);
                SavePlayerQuests(conn);
                ApplyQuestRewards(conn, completedQuestData);

                Debug.LogError($"[ADMIN-COMPLETEQUEST] Done");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ADMIN-COMPLETEQUEST] error={ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void SavePlayerQuests(RRConnection conn)
        {
            string connId = conn.ConnId.ToString();
            if (!_selectedCharacter.ContainsKey(conn.LoginName)) return;
            var savedChar = CharacterRepository.GetCharacter(_selectedCharacter[conn.LoginName].Id);
            if (savedChar == null) return;

            var questState = QuestManager.Instance.GetPlayerState(connId);
            if (questState == null) return;

            savedChar.activeQuests = questState.ActiveQuests.Select(q => new SavedQuest
            {
                questId = q.QuestId,
                questGiverId = q.QuestGiverId,
                acceptedAt = q.AcceptedAt.ToString("o"),
                objectives = q.Objectives.Select(o => new SavedQuestObjective
                {
                    objectiveName = o.ObjectiveName,
                    type = o.Type,
                    target = o.Target,
                    label = o.Label,
                    required = o.Required,
                    current = o.Current
                }).ToList()
            }).ToList();

            savedChar.completedQuests = new List<string>(questState.CompletedQuests);

            savedChar.unlockedCheckpoints = new List<string>(questState.UnlockedCheckpoints);

            savedChar.currentZoneName = conn.CurrentZoneName ?? "tutorial";
            savedChar.zoneId = (int)conn.CurrentZoneId;
            CharacterRepository.SaveCharacter(savedChar);
            Debug.LogError($"[SAVE] Saved {savedChar.activeQuests.Count} active, {savedChar.completedQuests.Count} completed quests for {conn.LoginName}");
        }

        public void NotifyQuestItemAcquired(RRConnection conn, string itemGcType)
        {
            if (conn == null || string.IsNullOrEmpty(itemGcType)) return;
            var updates = QuestManager.Instance.OnItemPickedUp(conn, itemGcType);
            if (updates != null && updates.Count > 0)
            {
                Debug.LogError($"[QUEST-ITEM] Item '{itemGcType}' updated {updates.Count} quest objective(s) for {conn.LoginName}");
                SavePlayerQuests(conn);
            }
        }

        private void SavePlayerLevel(RRConnection conn, [CallerMemberName] string caller = null)
        {
            string connId = conn.ConnId.ToString();
            if (!_selectedCharacter.ContainsKey(conn.LoginName))
            {
                Debug.LogError($"[SAVE] SKIP: No selected character for {conn.LoginName}");
                return;
            }
            var savedChar = GetActiveCharacter(conn);
            if (savedChar == null)
            {
                Debug.LogError($"[SAVE] SKIP: Character not found for ID={_selectedCharacter[conn.LoginName].Id}");
                return;
            }

            PlayerState playerState = GetPlayerState(connId);
            if (playerState == null)
            {
                Debug.LogError($"[SAVE] SKIP: No PlayerState for {connId}");
                return;
            }

            if (playerState.Level <= 1 && playerState.Experience == 0
                && (savedChar.level > 1 || savedChar.experience > 0))
            {
                Debug.LogError($"[SAVE] SKIPPED - playerState looks uninitialized; would wipe DB lvl={savedChar.level} xp={savedChar.experience} for {conn.LoginName}");
                return;
            }
            if (playerState.MaxHPWire == 0 || playerState.MaxManaWire == 0)
            {
                Debug.LogError($"[SAVE] SKIPPED - playerState max resources uninitialized; would wipe DB maxHP={savedChar.maxHP} maxMana={savedChar.maxMana} for {conn.LoginName} caller={caller ?? "unknown"}");
                return;
            }

            byte persistedRuntimeLevel = SavedCharacterLevel.ResolvePersistedLevel(playerState.Level);
            savedChar.level = persistedRuntimeLevel;
            savedChar.experience = playerState.Experience;
            savedChar.currentHP = playerState.CurrentHPWire;
            savedChar.currentMana = playerState.CurrentManaWire;
            savedChar.currentZoneName = conn.CurrentZoneName ?? "tutorial";
            savedChar.zoneId = (int)conn.CurrentZoneId;

            savedChar.maxHP = (int)(playerState.MaxHPWire / 256);
            savedChar.maxMana = (int)(playerState.MaxManaWire / 256);

            if (_playerSkillLevels.TryGetValue(connId, out var skillLevels))
            {
                foreach (var skillLevelEntry in skillLevels)
                {
                    if (skillLevelEntry.Key.Contains("."))
                        savedChar.SetSkillLevel(skillLevelEntry.Key, skillLevelEntry.Value);
                }
            }

            Debug.LogError($"[SAVE-CONTEXT] caller={caller ?? "unknown"} login={conn.LoginName} char={savedChar.id} outgoingLevel={savedChar.level} outgoingLevel={playerState.Level} outgoingXP={savedChar.experience} currentHP={savedChar.currentHP} maxHP={savedChar.maxHP} currentMana={savedChar.currentMana} maxMana={savedChar.maxMana} playerStateLevel={playerState.Level} playerStateXP={playerState.Experience} stateHP={playerState.CurrentHPWire}/{playerState.MaxHPWire} stateMana={playerState.CurrentManaWire}/{playerState.MaxManaWire}");
            CharacterRepository.SaveCharacter(savedChar, $"SavePlayerLevel:{caller ?? "unknown"}");
            Debug.LogError($"[SAVE] Level={savedChar.level} XP={savedChar.experience} HP={savedChar.currentHP / 256}/{savedChar.maxHP} Mana={savedChar.currentMana / 256}/{savedChar.maxMana} for {conn.LoginName} caller={caller ?? "unknown"}");
        }

        public (GCObject item, byte x, byte y)? GetInventoryItemBySlot(string connId, uint slotIndex, byte containerId = 0x0B)
        {
            string key = InvKey(connId, containerId);
            if (_playerInventoryItems.ContainsKey(key) && _playerInventoryItems[key].ContainsKey(slotIndex))
            {
                return _playerInventoryItems[key][slotIndex];
            }
            Debug.LogError($"[INV-TRACK] No item at slot {slotIndex} in container 0x{containerId:X2}");
            return null;
        }

        public void RemoveInventoryItemBySlot(string connId, uint slotIndex, byte containerId = 0x0B)
        {
            string key = InvKey(connId, containerId);
            if (_playerInventoryItems.ContainsKey(key) && _playerInventoryItems[key].ContainsKey(slotIndex))
            {
                _playerInventoryItems[key].Remove(slotIndex);
                if (_inventoryStackCounts.ContainsKey(key)) _inventoryStackCounts[key].Remove(slotIndex);
                Debug.LogError($"[INV-TRACK] Removed item at slot {slotIndex} in container 0x{containerId:X2}");
            }
        }
        public Dictionary<uint, (GCObject item, byte x, byte y)> GetAllInventoryItems(string connId, byte containerId = 0x0B)
        {
            string key = InvKey(connId, containerId);
            if (_playerInventoryItems.ContainsKey(key))
                return _playerInventoryItems[key];
            return null;
        }

        public byte? FindContainerForSlot(string connId, uint slotIndex)
        {
            if (_playerInventoryItems.ContainsKey(connId) && _playerInventoryItems[connId].ContainsKey(slotIndex))
                return 0x0B;
            byte[] bankIds = { 0x0C, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13 };
            foreach (byte containerId in bankIds)
            {
                string key = InvKey(connId, containerId);
                if (_playerInventoryItems.ContainsKey(key) && _playerInventoryItems[key].ContainsKey(slotIndex))
                    return containerId;
            }
            return null;
        }

        public GCObject GetEquippedItem(string connId, uint slot)
        {
            Debug.LogError($"[GET-EQUIP] Looking for Player {connId} slot {slot}");

            if (!_playerEquippedItems.ContainsKey(connId))
            {
                Debug.LogError($"[GET-EQUIP]  Player {connId} NOT in _playerEquippedItems!");
                Debug.LogError($"[GET-EQUIP] Available players: {string.Join(", ", _playerEquippedItems.Keys)}");
                return null;
            }

            if (!_playerEquippedItems[connId].ContainsKey(slot))
            {
                Debug.LogError($"[GET-EQUIP]  Slot {slot} not found for player {connId}!");
                Debug.LogError($"[GET-EQUIP] Available slots: {string.Join(", ", _playerEquippedItems[connId].Keys)}");
                return null;
            }

            GCObject item = _playerEquippedItems[connId][slot];
            Debug.LogError($"[GET-EQUIP]  Found: {item.GCClass}");
            return item;
        }
        public void TrackManipulatorsId(string connId, ushort manipulatorsId)
        {
            _playerManipulatorsIds[connId] = manipulatorsId;
            Debug.LogError($"[MANIPULATORS-TRACK] Player {connId}: Manipulators ID = 0x{manipulatorsId:X4}");
        }

        public ushort GetManipulatorsComponentId(string connId)
        {
            if (_playerManipulatorsIds.ContainsKey(connId))
                return _playerManipulatorsIds[connId];
            Debug.LogError($"[MANIPULATORS]  No ID tracked for player {connId}!");
            return 0;
        }
        public ushort GetUnitContainerComponentId(string connId)
        {
            if (_playerComponentTypes.ContainsKey(connId))
            {
                foreach (var componentEntry in _playerComponentTypes[connId])
                {
                    if (componentEntry.Value == "UnitContainer")
                        return componentEntry.Key;
                }
            }
            Debug.LogError($"[UNITCONTAINER]  No ID tracked for player {connId}!");
            return 0;
        }


        public void TrackDroppedItem(ushort entityId, GCObject item, RRConnection conn, int quantity = 1, float? posX = null, float? posY = null, float? posZ = null, int? playerLevelOverride = null)
        {
            var info = new DroppedItemInfo
            {
                Item = item,
                DbId = 0,
                Zone = conn.CurrentZoneName ?? "",
                ZoneId = conn.CurrentZoneId,
                InstanceId = conn.InstanceId,
                PosX = posX ?? conn.PlayerPosX,
                PosY = posY ?? conn.PlayerPosY,
                PosZ = posZ ?? conn.PlayerPosZ,
                PlayerLevel = playerLevelOverride ?? (GetPlayerState(conn.ConnId.ToString())?.Level ?? 1),
                DroppedBy = conn.LoginName ?? "",
                Quantity = quantity
            };

            try
            {
                using (var db = DungeonRunners.Database.GameDatabase.GetConnection())
                {
                    DungeonRunners.Database.GameDatabase.ExecuteNonQuery(db,
                        @"INSERT INTO dropped_items (zone, zone_id, instance_id, gc_class, dfc_class, pos_x, pos_y, pos_z, player_level, target_slot, preset_scale_mod, dropped_by, rarity, stored_level)
                          VALUES (@zone, @zid, @iid, @gc, @nc, @px, @py, @pz, @pl, @ts, @psm, @db, @rar, @slv)",
                        ("@zone", info.Zone),
                        ("@zid", (int)info.ZoneId),
                        ("@iid", (int)info.InstanceId),
                        ("@gc", item.GCClass),
                        ("@nc", item.DFCClass ?? "Armor"),
                        ("@px", (double)info.PosX),
                        ("@py", (double)info.PosY),
                        ("@pz", (double)info.PosZ),
                        ("@pl", info.PlayerLevel),
                        ("@ts", (int)(item.TargetSlot ?? 0xFFFFFFFF)),
                        ("@psm", item.PresetScaleMod ?? ""),
                        ("@db", info.DroppedBy),
                        ("@rar", item.GetEffectiveRarity()),
                        ("@slv", item.StoredLevel));

                    long dbId = Convert.ToInt64(DungeonRunners.Database.GameDatabase.ExecuteScalar(db, "SELECT last_insert_rowid()"));
                    info.DbId = dbId;
                    _dbIdToEntityId[dbId] = entityId;
                    Debug.LogError($"[DROP-TRACK] Saved to DB: dbId={dbId}, entityId={entityId}, GCClass={item.GCClass}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DROP-TRACK]  DB save failed: {ex.Message}");
            }

            _droppedItems[entityId] = info;
            Debug.LogError($"[DROP-TRACK] Stored dropped item: entityId={entityId}, GCClass={item.GCClass}, zone={info.Zone}, instance={info.InstanceId}");
        }

        public bool IsDroppedItem(ushort entityId)
        {
            return _droppedItems.ContainsKey(entityId);
        }

        public GCObject GetAndRemoveDroppedItem(ushort entityId, out int quantity)
        {
            quantity = 1;
            if (_droppedItems.TryGetValue(entityId, out DroppedItemInfo info))
            {
                _droppedItems.Remove(entityId);
                quantity = info.Quantity;

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
                        Debug.LogError($"[DROP-TRACK] Deleted from DB: dbId={info.DbId}, entityId={entityId}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[DROP-TRACK]  DB delete failed: {ex.Message}");
                    }
                }

                return info.Item;
            }
            return null;
        }


        private void SendDroppedItemsForZone(RRConnection conn)
        {
            string zone = conn.CurrentZoneName ?? "";
            uint instanceId = conn.InstanceId;

            Debug.LogError($"[DROP-LOAD] Loading dropped items for zone={zone}, instance={instanceId}");

            int inMemoryCount = 0;
            foreach (var droppedItemEntry in _droppedItems)
            {
                var info = droppedItemEntry.Value;
                if (info.Zone != zone || info.InstanceId != instanceId) continue;

                if (info.GoldAmount > 0)
                    SendGoldPileSpawnPacket(conn, droppedItemEntry.Key, info.PosX, info.PosY, info.PosZ, info.GoldAmount);
                else
                    SendDroppedItemSpawnPacket(conn, droppedItemEntry.Key, info);
                inMemoryCount++;
            }
            Debug.LogError($"[DROP-LOAD] Re-sent {inMemoryCount} in-memory items");

            int dbCount = 0;
            try
            {
                using (var db = DungeonRunners.Database.GameDatabase.GetConnection())
                using (var reader = DungeonRunners.Database.GameDatabase.ExecuteReader(db,
                    "SELECT id, gc_class, dfc_class, pos_x, pos_y, pos_z, player_level, target_slot, preset_scale_mod, dropped_by, COALESCE(rarity, -1), COALESCE(stored_level, -1) FROM dropped_items WHERE zone = @zone AND instance_id = @iid",
                    ("@zone", zone), ("@iid", (int)instanceId)))
                {
                    while (reader.Read())
                    {
                        long dbId = reader.GetInt64(0);

                        if (_dbIdToEntityId.ContainsKey(dbId)) continue;

                        string gcClass = reader.GetString(1);
                        string dfcClass = reader.GetString(2);
                        float posX = (float)reader.GetDouble(3);
                        float posY = (float)reader.GetDouble(4);
                        float posZ = (float)reader.GetDouble(5);
                        int playerLevel = reader.GetInt32(6);
                        int targetSlotRaw = reader.GetInt32(7);
                        string presetScaleMod = reader.IsDBNull(8) ? "" : reader.GetString(8);
                        string droppedBy = reader.IsDBNull(9) ? "" : reader.GetString(9);
                        int itemRarity = reader.GetInt32(10);
                        int itemStoredLevel = reader.GetInt32(11);

                        var item = new GCObject
                        {
                            GCClass = gcClass,
                            DFCClass = dfcClass,
                            StoredRarity = itemRarity,
                            StoredLevel = itemStoredLevel
                        };
                        if (targetSlotRaw >= 0 && targetSlotRaw != -1 && (uint)targetSlotRaw != 0xFFFFFFFF)
                            item.TargetSlot = (uint)targetSlotRaw;
                        if (!string.IsNullOrEmpty(presetScaleMod))
                            item.PresetScaleMod = presetScaleMod;

                        ushort entityId = GetNextLootEntityId();
                        var info = new DroppedItemInfo
                        {
                            Item = item,
                            DbId = dbId,
                            Zone = zone,
                            ZoneId = conn.CurrentZoneId,
                            InstanceId = instanceId,
                            PosX = posX,
                            PosY = posY,
                            PosZ = posZ,
                            PlayerLevel = playerLevel,
                            DroppedBy = droppedBy
                        };

                        _droppedItems[entityId] = info;
                        _dbIdToEntityId[dbId] = entityId;

                        SendDroppedItemSpawnPacket(conn, entityId, info);
                        dbCount++;

                        Debug.LogError($"[DROP-LOAD] Loaded from DB: dbId={dbId}, entityId={entityId}, gc={gcClass}, pos=({posX},{posY},{posZ})");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DROP-LOAD]  DB load failed: {ex.Message}");
            }

            Debug.LogError($"[DROP-LOAD]  Zone load complete: {inMemoryCount} in-memory + {dbCount} from DB");
        }

        private void SendDroppedItemSpawnPacket(RRConnection conn, ushort entityId, DroppedItemInfo info)
        {
            int fx = (int)(info.PosX * 256);
            int fy = (int)(info.PosY * 256);
            int fz = (int)(info.PosZ * 256);

            var body = new LEWriter();
            body.WriteByte(0x01);
            body.WriteUInt16(entityId);
            body.WriteByte(0xFF);
            body.WriteCString("itemobject");
            body.WriteByte(0x02);
            body.WriteUInt16(entityId);
            body.WriteUInt32(0x00000006);
            body.WriteInt32(fx);
            body.WriteInt32(fy);
            body.WriteInt32(fz);
            body.WriteInt32(0);
            body.WriteByte(0xF7);
            body.WriteUInt16(0x0000);
            body.WriteByte(0x00);
            body.WriteUInt32(0x00000000);
            body.WriteByte(0x06);
            body.WriteUInt16(0x0000);
            body.WriteUInt32(0x00000000);
            body.WriteUInt32(0x00000000);
            body.WriteUInt32(27000);
            body.WriteByte(0x01);
            int beforeInit = body.Position;
            info.Item.WriteInitForDroppedItem(body, info.PlayerLevel);
            int afterInit = body.Position;

            var chan = new LEWriter();
            chan.WriteByte(0x07);
            chan.WriteBytes(body.ToArray());
            chan.WriteByte(0x06);
            byte[] finalPacket = chan.ToArray();

            if (info.Item != null)
            {
                byte[] bodyBytes = body.ToArray();
                int writeInitLen = afterInit - beforeInit;
                string writeInitHex = BitConverter.ToString(bodyBytes, beforeInit, writeInitLen).Replace("-", " ");
                string fullHex = BitConverter.ToString(finalPacket).Replace("-", " ");
                Debug.LogError($"[DROP-WRITEINIT] gc={info.Item.GCClass} writeInitBytes={writeInitLen} hex={writeInitHex}");
                Debug.LogError($"[DROP-WRITEINIT] full-packet ({finalPacket.Length}B) hex={fullHex}");
            }

            SendToClient(conn, finalPacket);
        }

        private void BroadcastDroppedItemSpawnPacket(RRConnection conn, ushort entityId, DroppedItemInfo info)
        {
            SendDroppedItemSpawnPacket(conn, entityId, info);
            foreach (var other in _connections.Values)
            {
                if (other == conn || !other.IsSpawned) continue;
                if (other.CurrentZoneGcType != conn.CurrentZoneGcType || other.InstanceId != conn.InstanceId) continue;
                SendDroppedItemSpawnPacket(other, entityId, info);
            }
        }

        private void BroadcastGoldPileSpawnPacket(RRConnection conn, ushort entityId, float posX, float posY, float posZ, uint goldAmount)
        {
            SendGoldPileSpawnPacket(conn, entityId, posX, posY, posZ, goldAmount);
            foreach (var other in _connections.Values)
            {
                if (other == conn || !other.IsSpawned) continue;
                if (other.CurrentZoneGcType != conn.CurrentZoneGcType || other.InstanceId != conn.InstanceId) continue;
                SendGoldPileSpawnPacket(other, entityId, posX, posY, posZ, goldAmount);
            }
        }

        private void SendGoldPileSpawnPacket(RRConnection conn, ushort entityId, float posX, float posY, float posZ, uint goldAmount)
        {
            int fx = (int)(posX * 256);
            int fy = (int)(posY * 256);
            int fz = (int)(posZ * 256);

            var body = new LEWriter();
            body.WriteByte(0x01);
            body.WriteUInt16(entityId);
            body.WriteByte(0xFF);
            body.WriteCString("itemobject");
            body.WriteByte(0x02);
            body.WriteUInt16(entityId);
            body.WriteUInt32(0x00000006);
            body.WriteInt32(fx);
            body.WriteInt32(fy);
            body.WriteInt32(fz);
            body.WriteInt32(0);
            body.WriteByte(0xF7);
            body.WriteUInt16(0x0000);
            body.WriteByte(0x00);
            body.WriteUInt32(0x00000000);
            body.WriteByte(0x06);
            body.WriteUInt16(0x0000);
            body.WriteUInt32(0x00000000);
            body.WriteUInt32(0x00000000);
            body.WriteUInt32(27000);
            body.WriteByte(0x01);
            body.WriteByte(0xFF);
            body.WriteCString("Currency");
            body.WriteUInt32(0);
            body.WriteByte(0x00);
            body.WriteByte(0x00);
            body.WriteByte(0x01);
            body.WriteByte(0x01);
            body.WriteByte(0x00);
            body.WriteByte(0x00);
            body.WriteUInt32(goldAmount);

            var chan = new LEWriter();
            chan.WriteByte(0x07);
            chan.WriteBytes(body.ToArray());
            chan.WriteByte(0x06);
            SendToClient(conn, chan.ToArray());
        }


        private void CleanupExpiredDroppedItems()
        {
            List<long> expiredDbIds = new List<long>();
            try
            {
                using (var db = DungeonRunners.Database.GameDatabase.GetConnection())
                {
                    using (var reader = DungeonRunners.Database.GameDatabase.ExecuteReader(db,
                        $"SELECT id FROM dropped_items WHERE dropped_at < datetime('now', '-{DROPPED_ITEM_EXPIRE_MINUTES} minutes')"))
                    {
                        while (reader.Read())
                            expiredDbIds.Add(reader.GetInt64(0));
                    }

                    if (expiredDbIds.Count > 0)
                    {
                        DungeonRunners.Database.GameDatabase.ExecuteNonQuery(db,
                            $"DELETE FROM dropped_items WHERE dropped_at < datetime('now', '-{DROPPED_ITEM_EXPIRE_MINUTES} minutes')");
                        Debug.LogError($"[DROP-CLEANUP] Deleted {expiredDbIds.Count} expired items from DB");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DROP-CLEANUP]  DB cleanup failed: {ex.Message}");
            }

            foreach (long dbId in expiredDbIds)
            {
                if (_dbIdToEntityId.TryGetValue(dbId, out ushort entityId))
                {
                    if (_droppedItems.TryGetValue(entityId, out DroppedItemInfo info))
                    {
                        foreach (var other in _connections.Values)
                        {
                            if (!other.IsSpawned) continue;
                            if (other.CurrentZoneName != info.Zone) continue;
                            if (other.InstanceId != info.InstanceId) continue;
                            SendDespawnEntity(other, entityId);
                        }
                        _droppedItems.Remove(entityId);
                    }
                    _dbIdToEntityId.Remove(dbId);
                }
            }
        }













        private void LoadZones()
        {
            try
            {
                using (var connection = DungeonRunners.Database.GameDatabase.GetConnection())
                using (var reader = DungeonRunners.Database.GameDatabase.ExecuteReader(connection, "SELECT * FROM zones"))
                {
                    while (reader.Read())
                    {
                        var zone = new Zone
                        {
                            id = DungeonRunners.Database.GameDatabase.GetUInt(reader, "id"),
                            name = DungeonRunners.Database.GameDatabase.GetString(reader, "name"),
                            gcType = DungeonRunners.Database.GameDatabase.GetString(reader, "gc_type"),
                            spawnX = DungeonRunners.Database.GameDatabase.GetFloat(reader, "spawn_x"),
                            spawnY = DungeonRunners.Database.GameDatabase.GetFloat(reader, "spawn_y"),
                            spawnZ = DungeonRunners.Database.GameDatabase.GetFloat(reader, "spawn_z"),
                            spawnHeading = DungeonRunners.Database.GameDatabase.GetFloat(reader, "spawn_heading"),
                            respawnZone = DungeonRunners.Database.GameDatabase.GetString(reader, "respawn_zone"),
                            exploredBitCount = GetIntSafe(reader, "explored_bit_count", 0)
                        };
                        if (zone.id != 0) _zones[zone.id] = zone;
                    }
                }
                Debug.Log($"Loaded {_zones.Count} zones from SQLite");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ZONE-LOAD] state=failed message='{ex.Message}'");
            }
        }

        private ushort ResolveExploredBitCount(RRConnection conn, string source)
        {
            if (conn != null && _zones.TryGetValue(conn.CurrentZoneId, out Zone zone) && zone.exploredBitCount > 0)
                return (ushort)zone.exploredBitCount;

            if (conn != null && DungeonMazeSpawner.TryResolveExploredBitCount(conn.CurrentZoneName, out ushort proceduralBitCount))
            {
                Debug.LogError($"[MINIMAP] source={source ?? "unknown"} zoneId=0x{conn.CurrentZoneId:X8} zone='{conn.CurrentZoneName ?? ""}' exploredBitCount={proceduralBitCount} source=procedural-dungeon-maze sourceFunction=MiniMapExplored::ReadExploredBits");
                return proceduralBitCount;
            }

            Debug.LogError($"[MINIMAP] source={source ?? "unknown"} zoneId=0x{(conn != null ? conn.CurrentZoneId : 0):X8} zone='{conn?.CurrentZoneName ?? ""}' exploredBitCount=0 reason=no-authored-zone-count sourceFunction=ZoneMessageReady");
            return 0;
        }

        private void StartServer()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, ServerSettings.Get("gamePort", 2603));
                _listener.Start();
                _isRunning = true;

                string localIP = GetLocalIPAddress();
                Debug.Log($"Game Server started on 0.0.0.0:2603 (accessible via {localIP}:2603)");
                StartCoroutine(AcceptClientsCoroutine());
                StartCoroutine(PollPendingItemGrants());
                StartUDPListener();
                QueueConnection.OnQueueStreamRegistered += OnQueueStreamReady;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to start game server: {ex.Message}");
            }
        }
        private void StartUDPListener()
        {
            try
            {
                _udpListener = new UdpClient(_udpPort);
                Debug.LogError($"[UDP] UDP Listener started on port {_udpPort}!");
                _udpListener.BeginReceive(OnUDPReceive, null);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UDP] Failed to start on {_udpPort}: {ex.Message}");
            }
        }
        private void InitiateUDPToClient(RRConnection conn)
        {
            try
            {
                var tcpEndpoint = conn.Client.Client.RemoteEndPoint as IPEndPoint;
                if (tcpEndpoint == null) return;

                string clientIP = tcpEndpoint.Address.ToString();
                var clientEndpoint = new IPEndPoint(IPAddress.Parse(clientIP), 2603);

                byte[] synPacket = BuildUDPSynPacket(conn.ConnId);
                _udpListener.Send(synPacket, synPacket.Length, clientEndpoint);
                Debug.LogError($"[UDP] Sent SYN to {clientEndpoint}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UDP] initiateUDPToClient state=failed message='{ex.Message}'");
            }
        }

        private byte[] BuildUDPSynPacket(int connId)
        {
            var writer = new LEWriter();
            writer.WriteByte(0x01);
            writer.WriteUInt32((uint)connId);
            writer.WriteUInt32(0);
            writer.WriteUInt32(0);
            return writer.ToArray();
        }

        private void SendUDPAck(IPEndPoint clientEndpoint)
        {
            try
            {
                var writer = new LEWriter();
                writer.WriteByte(0x02);
                writer.WriteUInt32(0);
                writer.WriteUInt32(1);
                writer.WriteUInt32(1);

                byte[] ackPacket = writer.ToArray();
                _udpListener.Send(ackPacket, ackPacket.Length, clientEndpoint);
                Debug.LogError($"[UDP] Sent ACK to {clientEndpoint}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UDP] sendUDPAck state=failed message='{ex.Message}'");
            }
        }


        private byte[] EncryptUDP(UDPSession session, byte[] plaintext)
        {
            int paddedLen = ((plaintext.Length + 7) / 8) * 8;
            byte[] padded = new byte[paddedLen];
            Array.Copy(plaintext, padded, plaintext.Length);
            byte[] encrypted = new byte[paddedLen];
            for (int blockOffset = 0; blockOffset < paddedLen; blockOffset += 8)
                session.EncryptEngine.ProcessBlock(padded, blockOffset, encrypted, blockOffset);
            return encrypted;
        }

        private byte[] DecryptUDP(UDPSession session, byte[] ciphertext)
        {
            if (ciphertext.Length % 8 != 0) return null;
            byte[] decrypted = new byte[ciphertext.Length];
            for (int blockOffset = 0; blockOffset < ciphertext.Length; blockOffset += 8)
                session.DecryptEngine.ProcessBlock(ciphertext, blockOffset, decrypted, blockOffset);
            return decrypted;
        }

        private HashSet<string> _seenPacketPatterns = new HashSet<string>();
        private Dictionary<string, string> _checkpointZoneMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private const uint DungeonPortalAggColor = 0xFFFF0000u;

        private static uint ResolveAuthoredDungeonPortalColor(string gcType, uint fallback)
        {
            if (ContainsIgnoreCase(gcType, "hub"))
                return DungeonPortalHubColor;
            if (ContainsIgnoreCase(gcType, "agg"))
                return DungeonPortalAggColor;
            return fallback;
        }

        private static void ApplyDungeonPortalAuthority(DungeonMazeSpawner.ProceduralDungeonSnapshot snapshot, DungeonPortalRole role, ZonePortal portal)
        {
            if (snapshot == null || portal == null)
                return;

            string gcType = role == DungeonPortalRole.Entry ? snapshot.EntryPortalGcType : snapshot.ExitPortalGcType;
            string targetZone = role == DungeonPortalRole.Entry ? snapshot.EntryLinkToZone : snapshot.ExitLinkToZone;
            string spawnPoint = role == DungeonPortalRole.Entry ? snapshot.EntryLinkToSpawn : snapshot.ExitLinkToSpawn;

            if (!string.IsNullOrEmpty(gcType))
                portal.GCType = gcType;
            if (!string.IsNullOrEmpty(targetZone))
                portal.TargetZone = targetZone;
            if (!string.IsNullOrEmpty(spawnPoint))
                portal.SpawnPoint = spawnPoint;

            portal.Color = ResolveAuthoredDungeonPortalColor(portal.GCType, portal.Color);
        }

        private static ZonePortal CloneZonePortal(ZonePortal source)
        {
            if (source == null) return null;
            return new ZonePortal
            {
                Id = source.Id,
                GCType = source.GCType,
                Name = source.Name,
                PosX = source.PosX,
                PosY = source.PosY,
                PosZ = source.PosZ,
                Heading = source.Heading,
                Width = source.Width,
                Height = source.Height,
                TargetZone = source.TargetZone,
                SpawnPoint = source.SpawnPoint,
                Color = source.Color
            };
        }

        private List<ZonePortal> ResolveZonePortalsForConnection(RRConnection conn, List<ZonePortal> portals)
        {
            if (portals == null || portals.Count == 0)
                return portals;
            if (!TryGetProceduralDungeonSnapshot(conn, out var snapshot))
                return portals;

            var resolved = new List<ZonePortal>(portals.Count);
            foreach (var portal in portals)
            {
                var clone = CloneZonePortal(portal);
                if (clone == null) continue;
                DungeonPortalRole role = ResolveDungeonPortalRole(conn.CurrentZoneName, clone.GCType, clone.Name, clone.TargetZone, clone.SpawnPoint);
                ApplyDungeonPortalAuthority(snapshot, role, clone);
                ResolveDungeonPortalAnchor(snapshot, role, out var position, out float heading, out int sourceIndex, out string tileType, out int gridX, out int gridY, out var local, out string source);
                clone.PosX = position.x;
                clone.PosY = position.y;
                clone.PosZ = position.z;
                clone.Heading = heading;
                resolved.Add(clone);
                Debug.LogError($"[DUNGEON-PORTAL] zone={snapshot.ZoneName} layoutSeed=0x{snapshot.LayoutSeed:X8} roomSeed=0x{snapshot.RoomSeed:X8} name={clone.Name} gc={clone.GCType} target={clone.TargetZone} spawnPoint={clone.SpawnPoint} role={role.ToString().ToLowerInvariant()} src={sourceIndex} tile='{tileType}' grid=({gridX},{gridY}) local=({local.x:F1},{local.y:F1},{local.z:F1}) rawAuthoredWorld=({clone.PosX:F1},{clone.PosY:F1},{clone.PosZ:F1}) sentWorld=({clone.PosX:F1},{clone.PosY:F1},{clone.PosZ:F1}) snapApplied=False heading={clone.Heading:F1} source='{source}'");
            }
            return resolved;
        }


        private void SendZonePortals(RRConnection conn, uint zoneId)
        {
            if (!_zonePortals.TryGetValue(zoneId, out var portals) || portals.Count == 0)
            {
                Debug.LogError($"[SEND-ZONE-PORTALS] zone={zoneId} state=empty");
                return;
            }
            portals = ResolveZonePortalsForConnection(conn, portals);

            if (VerbosePacketLogging) Debug.LogError($"[SEND-ZONE-PORTALS] count={portals.Count}");

            var writer = new LEWriter();
            writer.WriteByte(0x07);

            foreach (var portal in portals)
            {
                ushort portalId = (ushort)_nextEntityId++;
                _portalEntities[portalId] = portal;
                if (!_portalEntitiesByConn.TryGetValue(conn.ConnId, out var connPortalIds))
                {
                    connPortalIds = new List<ushort>();
                    _portalEntitiesByConn[conn.ConnId] = connPortalIds;
                }
                connPortalIds.Add(portalId);
                _allEntityPositions[portalId] = (portal.PosX, portal.PosY, portal.PosZ);
                if (VerbosePacketLogging) Debug.LogError($"[PORTAL] name='{portal.Name}' id=0x{portalId:X4} pos=({portal.PosX},{portal.PosY},{portal.PosZ}) target='{portal.TargetZone}'");
                if (VerbosePacketLogging) Debug.LogError($"[PORTAL] registered id=0x{portalId:X4} target='{portal.TargetZone}'");
                writer.WriteByte(0x01);
                writer.WriteUInt16(portalId);
                WriteGCType(writer, portal.GCType, preserveCase: true);

                writer.WriteByte(0x02);
                writer.WriteUInt16(portalId);

                writer.WriteUInt32(0x06);


                int posX = (int)(portal.PosX * 256);
                int posY = (int)(portal.PosY * 256);
                int posZ = (int)(portal.PosZ * 256);
                int heading = (int)(portal.Heading * 256);

                writer.WriteInt32(posX);
                writer.WriteInt32(posY);
                writer.WriteInt32(posZ);
                writer.WriteInt32(heading);
                writer.WriteByte(0x00);

                if (VerbosePacketLogging) Debug.LogError($"[PORTAL-WRITE] entityId=0x{portalId:X4} label='{portal.TargetZone}' spawnPoint='{portal.SpawnPoint}' width={portal.Width} height={portal.Height} color=0x{portal.Color:X8}");
                writer.WriteCString(portal.SpawnPoint ?? "");
                writer.WriteCString(portal.TargetZone ?? "");

                writer.WriteUInt16((ushort)portal.Width);
                writer.WriteUInt16((ushort)portal.Height);
                writer.WriteUInt32(portal.Color);
                if (VerbosePacketLogging) Debug.LogError($"[PORTAL-HEX] {BitConverter.ToString(writer.ToArray())}");
                if (VerbosePacketLogging) Debug.LogError($"[PORTAL-WRITE] entityId=0x{portalId:X4} label='{portal.TargetZone}' spawnPoint='{portal.SpawnPoint}' width={portal.Width} height={portal.Height} color=0x{portal.Color:X8}");
            }

            writer.WriteByte(0x06);

            byte[] packetData = writer.ToArray();
            if (VerbosePacketLogging) Debug.LogError($"[SEND-ZONE-PORTALS] fullHexBytes={packetData.Length} hex={BitConverter.ToString(packetData).Replace("-", " ")}");
            if (VerbosePacketLogging) Debug.LogError("[SEND-ZONE-PORTALS] hexScope=portal-packet");
            if (VerbosePacketLogging) Debug.LogError($"[SEND-ZONE-PORTALS] length={packetData.Length}");
            if (VerbosePacketLogging) Debug.LogError($"[SEND-ZONE-PORTALS] hex={BitConverter.ToString(packetData)}");
            SendCompressedA(conn, 0x01, 0x0f, packetData);

            if (VerbosePacketLogging) Debug.LogError($"[SEND-ZONE-PORTALS] sent={portals.Count} bytes={packetData.Length}");
        }








        private void ClearConnZoneEntities(RRConnection conn)
        {
            if (conn == null) return;
            if (_portalEntitiesByConn.TryGetValue(conn.ConnId, out var portalIds))
            {
                foreach (var id in portalIds)
                    _portalEntities.Remove(id);
                portalIds.Clear();
            }
            if (_checkpointEntitiesByConn.TryGetValue(conn.ConnId, out var checkpointIds))
            {
                foreach (var id in checkpointIds)
                    _checkpointEntities.Remove(id);
                checkpointIds.Clear();
            }
        }

        private void SendZoneCheckpoints(RRConnection conn, uint zoneId)
        {
            if (!_zoneCheckpoints.TryGetValue(zoneId, out var checkpoints) || checkpoints.Count == 0)
            {
                Debug.LogError($"[SEND-ZONE-CHECKPOINTS] zone={zoneId} state=empty");
                return;
            }

            if (VerbosePacketLogging) Debug.LogError($"[SEND-ZONE-CHECKPOINTS] count={checkpoints.Count}");

            var writer = new LEWriter();
            writer.WriteByte(0x07);

            foreach (var checkpoint in checkpoints)
            {
                ushort checkpointId = (ushort)_nextEntityId++;
                _checkpointEntities[checkpointId] = checkpoint;
                if (!_checkpointEntitiesByConn.TryGetValue(conn.ConnId, out var connCheckpointIds))
                {
                    connCheckpointIds = new List<ushort>();
                    _checkpointEntitiesByConn[conn.ConnId] = connCheckpointIds;
                }
                connCheckpointIds.Add(checkpointId);
                _allEntityPositions[checkpointId] = (checkpoint.PosX, checkpoint.PosY, checkpoint.PosZ);
                if (VerbosePacketLogging) Debug.LogError($"[CHECKPOINT] gcType='{checkpoint.GCType}' id=0x{checkpointId:X4} pos=({checkpoint.PosX},{checkpoint.PosY},{checkpoint.PosZ})");

                writer.WriteByte(0x01);
                writer.WriteUInt16(checkpointId);
                WriteGCType(writer, checkpoint.GCType, preserveCase: true);

                writer.WriteByte(0x02);
                writer.WriteUInt16(checkpointId);

                writer.WriteUInt32(0x06);

                int posX = (int)(checkpoint.PosX * 256);
                int posY = (int)(checkpoint.PosY * 256);
                int posZ = (int)(checkpoint.PosZ * 256);
                int heading = (int)(checkpoint.Heading * 256);

                writer.WriteInt32(posX);
                writer.WriteInt32(posY);
                writer.WriteInt32(posZ);
                writer.WriteInt32(heading);
                writer.WriteByte(0x00);
            }

            writer.WriteByte(0x06);

            byte[] packetData = writer.ToArray();
            SendCompressedA(conn, 0x01, 0x0f, packetData);

            if (VerbosePacketLogging) Debug.LogError($"[SEND-ZONE-CHECKPOINTS] sent={checkpoints.Count} bytes={packetData.Length}");
        }

        private void SendZoneChests(RRConnection conn, string zoneName)
        {
            if (string.IsNullOrEmpty(zoneName)) return;
            var chests = new List<ChestSpawnData>();
            if (chests.Count == 0) return;

            Debug.LogError($"[CHESTS] Spawning {chests.Count} treasure chests in {zoneName}");

            var writer = new LEWriter();
            writer.WriteByte(0x07);

            foreach (var chest in chests)
            {
                ushort chestId = (ushort)_nextEntityId++;
                ushort behaviorId = (ushort)_nextEntityId++;

                var chestPathMap = DungeonRunners.Core.PathMapCatalog.Instance.GetPathMap(zoneName);
                if (chestPathMap != null && chestPathMap.IsWalkable(chest.PosX, chest.PosY))
                {
                    float terrainZ = chestPathMap.GetHeightAt(chest.PosX, chest.PosY, chest.PosZ);
                    chest.PosZ = terrainZ;
                }

                _chestEntities[chestId] = chest;
                _allEntityPositions[chestId] = (chest.PosX, chest.PosY, chest.PosZ);

                writer.WriteByte(0x01);
                writer.WriteUInt16(chestId);
                WriteGCType(writer, chest.GCType, preserveCase: true);

                writer.WriteByte(0x02);
                writer.WriteUInt16(chestId);

                writer.WriteUInt32(0x06);

                writer.WriteInt32((int)(chest.PosX * 256));
                writer.WriteInt32((int)(chest.PosY * 256));
                writer.WriteInt32((int)(chest.PosZ * 256));
                writer.WriteInt32((int)(chest.Heading * 256));
                writer.WriteByte(0x00);


                writer.WriteByte(0x00);
                writer.WriteByte(0x00);
                writer.WriteUInt16(0);
                writer.WriteUInt16(0);

                writer.WriteByte(0x00);
                writer.WriteByte(0x00);
                writer.WriteUInt16(0);

                writer.WriteByte(0x32);
                writer.WriteUInt16(chestId);
                writer.WriteUInt16(behaviorId);
                WriteGCType(writer, "base.noncombatinteractive.behavior", preserveCase: true);
                writer.WriteByte(0x01);

                writer.WriteByte(0xFF);
                writer.WriteByte(0x00);
                writer.WriteByte(0x00);
                writer.WriteByte(0x01);

                writer.WriteByte(0x08);
                writer.WriteInt32((int)(chest.Heading * 256));
                writer.WriteInt32((int)(chest.Heading * 256));
                writer.WriteByte(0x00);

                writer.WriteByte(0xFF);
                writer.WriteByte(0x00);
                writer.WriteByte(0x00);

                Debug.LogError($"[CHEST] {chest.Label} ({chest.GCType}) id=0x{chestId:X4} beh=0x{behaviorId:X4} at ({chest.PosX:F0},{chest.PosY:F0},{chest.PosZ:F0})");
            }

            writer.WriteByte(0x06);

            byte[] chestPacket = writer.ToArray();
            SendCompressedA(conn, 0x01, 0x0f, chestPacket);
            Debug.LogError($"[CHESTS]  Sent {chests.Count} chests ({chestPacket.Length} bytes)");
        }


        private static WorldEntityData CloneWorldEntityData(WorldEntityData source)
        {
            if (source == null) return null;
            return new WorldEntityData
            {
                Id = source.Id,
                Zone = source.Zone,
                Name = source.Name,
                GCType = source.GCType,
                EntityType = source.EntityType,
                PosX = source.PosX,
                PosY = source.PosY,
                PosZ = source.PosZ,
                Heading = source.Heading,
                Flags = source.Flags,
                ItemGenerator = source.ItemGenerator,
                ItemCount = source.ItemCount,
                ItemGenerator2 = source.ItemGenerator2,
                ItemCount2 = source.ItemCount2,
                ItemGenerator3 = source.ItemGenerator3,
                ItemCount3 = source.ItemCount3,
                ItemGenerator4 = source.ItemGenerator4,
                ItemCount4 = source.ItemCount4,
                ItemGenerator5 = source.ItemGenerator5,
                ItemCount5 = source.ItemCount5,
                TargetZone = source.TargetZone,
                TargetSpawn = source.TargetSpawn,
                Label = source.Label,
                AllowMultiple = source.AllowMultiple
            };
        }

        private WorldEntityData ResolveWorldEntityDataForConnection(RRConnection conn, WorldEntityData data)
        {
            if (data == null || !data.IsTeleporter)
                return data;
            if (!TryGetProceduralDungeonSnapshot(conn, out var snapshot))
                return data;

            var clone = CloneWorldEntityData(data);
            DungeonPortalRole role = ResolveDungeonPortalRole(conn.CurrentZoneName, clone.GCType, clone.Name, clone.TargetZone, clone.TargetSpawn);
            ResolveDungeonPortalAnchor(snapshot, role, out var position, out float heading, out int sourceIndex, out string tileType, out int gridX, out int gridY, out var local, out string source);
            clone.PosX = position.x;
            clone.PosY = position.y;
            clone.PosZ = position.z;
            clone.Heading = heading;
            Debug.LogError($"[DUNGEON-PORTAL] world-teleporter zone={snapshot.ZoneName} layoutSeed=0x{snapshot.LayoutSeed:X8} roomSeed=0x{snapshot.RoomSeed:X8} label={clone.Label} gc={clone.GCType} target={clone.TargetZone} spawnPoint={clone.TargetSpawn} role={role.ToString().ToLowerInvariant()} src={sourceIndex} tile='{tileType}' grid=({gridX},{gridY}) local=({local.x:F1},{local.y:F1},{local.z:F1}) rawAuthoredWorld=({clone.PosX:F1},{clone.PosY:F1},{clone.PosZ:F1}) sentWorld=({clone.PosX:F1},{clone.PosY:F1},{clone.PosZ:F1}) snapApplied=False heading={clone.Heading:F1} source='{source}'");
            return clone;
        }

        private readonly Dictionary<string, List<ushort>> _pvpGateIdsByLogin =
            new Dictionary<string, List<ushort>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, int> _pendingKingsCoins =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _pvpControllerReady =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, DateTime> _pvpGateDropDue =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, ushort> _pvpControllerIdByLogin =
            new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, List<(DateTime due, string msg)>> _pvpCombatReminders =
            new Dictionary<string, List<(DateTime, string)>>(StringComparer.OrdinalIgnoreCase);

        private void SendZoneWorldEntities(RRConnection conn)
        {
            if (WorldEntitySpawner.Instance == null) return;
            string zoneName = conn.CurrentZoneName;
            if (string.IsNullOrEmpty(zoneName)) return;

            if (conn.LoginName != null) _pvpGateIdsByLogin.Remove(conn.LoginName);

            var entities = WorldEntitySpawner.Instance.GetEntitiesForZone(zoneName);
            if (entities.Count == 0) return;

            Debug.LogError($"[WORLD-ENTITIES] Spawning {entities.Count} world entities in {zoneName}");

            var writer = new LEWriter();
            writer.WriteByte(0x07);

            foreach (var ent in entities)
            {
                ushort entId = (ushort)_nextEntityId++;
                ushort behaviorId = (ushort)_nextEntityId++;
                ent.EntityId = entId;
                var data = ResolveWorldEntityDataForConnection(conn, ent.Data);

                WorldEntitySpawner.WriteEntitySpawn(writer, entId, behaviorId, data);
                WorldEntitySpawner.Instance.TrackSpawnedEntity(entId, data);

                if (data.IsGate && conn.LoginName != null)
                {
                    if (!_pvpGateIdsByLogin.TryGetValue(conn.LoginName, out var gids))
                        _pvpGateIdsByLogin[conn.LoginName] = gids = new List<ushort>();
                    gids.Add(entId);
                }

                _allEntityPositions[entId] = (data.PosX, data.PosY, data.PosZ);


                Debug.LogError($"[WORLD-ENTITY] {data.EntityType}: {data.Label} ({data.GCType}) id=0x{entId:X4} at ({data.PosX:F0},{data.PosY:F0},{data.PosZ:F0})");
            }

            writer.WriteByte(0x06);

            byte[] packet = writer.ToArray();
            SendCompressedA(conn, 0x01, 0x0f, packet);
            Debug.LogError($"[WORLD-ENTITIES]  Sent {entities.Count} entities ({packet.Length} bytes)");
        }


        private void HandleTeleporterActivation(RRConnection conn, ushort componentId,
            ushort targetEntityId, byte responseId, byte sessionId, WorldEntityData teleporter)
        {
            Debug.LogError($"[TELEPORTER] ");
            Debug.LogError($"[TELEPORTER] {teleporter.Label} -> {teleporter.TargetZone} ({teleporter.TargetSpawn})");
            conn.SessionID = sessionId;

            var teleporterActivationMessage = new LEWriter();
            teleporterActivationMessage.WriteByte(0x35);
            teleporterActivationMessage.WriteUInt16(componentId);
            teleporterActivationMessage.WriteByte(0x01);
            teleporterActivationMessage.WriteByte(responseId);
            teleporterActivationMessage.WriteByte(0x06);
            teleporterActivationMessage.WriteByte(sessionId);
            teleporterActivationMessage.WriteUInt16(targetEntityId);
            WritePlayerEntitySynch(conn, teleporterActivationMessage);
            conn.MessageQueue.Enqueue(teleporterActivationMessage.ToArray());

            if (!string.IsNullOrEmpty(teleporter.TargetZone))
            {
                float spawnX = 0, spawnY = 0, spawnZ = 0;
                if (!string.IsNullOrEmpty(teleporter.TargetSpawn))
                {
                    var waypoints = DatabaseLoader.GetWaypointsForZone(teleporter.TargetZone);
                    if (waypoints != null)
                    {
                        var waypoint = waypoints.FirstOrDefault(waypointData =>
                            waypointData.name.Equals(teleporter.TargetSpawn, StringComparison.OrdinalIgnoreCase));
                        if (waypoint != null)
                        {
                            spawnX = waypoint.posX;
                            spawnY = waypoint.posY;
                            spawnZ = waypoint.posZ;
                            Debug.LogError($"[TELEPORTER] Found spawn '{teleporter.TargetSpawn}' at ({spawnX},{spawnY},{spawnZ})");
                        }
                    }
                }

                conn.PendingSpawnX = spawnX;
                conn.PendingSpawnY = spawnY;
                conn.PendingSpawnZ = spawnZ;

                ChangeZone(conn, teleporter.TargetZone, teleporter.TargetSpawn ?? "");
                Debug.LogError($"[TELEPORTER] Player {conn.LoginName} teleported to {teleporter.TargetZone}");
            }
        }

        private float _lastWorldEntityCheckTime;

        private void HandleEnterPvpZone(RRConnection conn)
        {
            Debug.LogError($"[PVP] {conn.LoginName} marked as in PVP hub area");
        }

        private void HandleRequestPvpMatch(RRConnection conn, byte[] data)
        {
            if (data == null || data.Length < 2)
            {
                Debug.LogError($"[PVP] requestPVPMatch payload too short ({data?.Length ?? 0} bytes)");
                return;
            }

            Gameplay.PVPMatchmaking.Archetype archetype;
            byte tag = data[0];
            if (tag == 0x04 || tag == 0x02 || tag == 0x01)
            {
                uint typeId;
                if (tag == 0x04 && data.Length >= 5) typeId = BitConverter.ToUInt32(data, 1);
                else if (tag == 0x02 && data.Length >= 3) typeId = BitConverter.ToUInt16(data, 1);
                else if (tag == 0x01 && data.Length >= 2) typeId = data[1];
                else { Debug.LogError($"[PVP] truncated TypeID payload (hex: {BitConverter.ToString(data)})"); return; }

                if (!Gameplay.PVPMatchmaking.TryParseArchetypeByTypeId(typeId, out archetype))
                {
                    Debug.LogError($"[PVP] Unknown match TypeID 0x{typeId:X8} (hex: {BitConverter.ToString(data)})");
                    return;
                }
                Debug.LogError($"[PVP] {conn.LoginName} requested archetype: {archetype} (TypeID 0x{typeId:X8})");
            }
            else
            {
                string archetypeGcType = TryReadGcTypeString(data);
                if (string.IsNullOrEmpty(archetypeGcType))
                {
                    Debug.LogError($"[PVP] Could not parse match archetype from payload (hex: {BitConverter.ToString(data)})");
                    return;
                }
                Debug.LogError($"[PVP] {conn.LoginName} requested archetype: '{archetypeGcType}'");
                if (!Gameplay.PVPMatchmaking.TryParseArchetype(archetypeGcType, out archetype))
                {
                    Debug.LogError($"[PVP] Unknown archetype '{archetypeGcType}'");
                    return;
                }
            }

            int rating = 1500;
            try
            {
                var dbChar = Database.CharacterRepository.GetCharacterByName(conn.LoginName, conn.LoginName);
                if (dbChar != null && dbChar.pvpRating > 0)
                    rating = dbChar.pvpRating;
            }
            catch (Exception ex) { Debug.LogError($"[PVP] Could not load PvP rating: {ex.Message}"); }

            uint charSqlId = conn.CharSqlId;
            var pvpGroup = Gameplay.GroupDirectory.Instance.GetGroupForConn(conn.ConnId);
            string teamKey = (pvpGroup != null && pvpGroup.Members.Count(m => m.IsOnline) > 1)
                ? "grp:" + pvpGroup.GroupId
                : "solo:" + conn.LoginName;
            bool ok = Gameplay.PVPMatchmaking.Instance.EnqueuePlayer(
                conn.LoginName, charSqlId, archetype, rating, teamKey);

            if (!ok)
            {
                Debug.LogError($"[PVP] {conn.LoginName} could not be enqueued");
                return;
            }

            uint pvpGroupId = Gameplay.GroupDirectory.Instance.GetGroupForConn(conn.ConnId)?.GroupId ?? 1u;
            SendToClient(conn, PVPPackets.BuildPVPMatchStatus(pvpGroupId, 0, 0, Gameplay.PVPMatchmaking.GetGcPath(archetype)));
            Debug.LogError($"[PVP] {conn.LoginName} now-in-queue status sent ({archetype}, group {pvpGroupId})");

            ProcessMatchmakingTick(forceRun: true);
        }

        private void HandleCancelPvpMatch(RRConnection conn)
        {
            bool removed = Gameplay.PVPMatchmaking.Instance.DequeuePlayer(conn.LoginName);
            Debug.LogError($"[PVP] cancel queue for {conn.LoginName}: {(removed ? "OK" : "was not queued")}");
            if (removed)
                SendToClient(conn, PVPPackets.BuildPVPMatchStatus(Gameplay.GroupDirectory.Instance.GetGroupForConn(conn.ConnId)?.GroupId ?? 1u, 0, 0, null));
        }

        private void HandleLeavePvp(RRConnection conn)
        {
            Gameplay.PVPMatchmaking.Instance.DequeuePlayer(conn.LoginName);
            var match = Gameplay.PVPMatchmaking.Instance.HandleDisconnect(conn.LoginName);
            if (match != null)
            {
                Debug.LogError($"[PVP] {conn.LoginName} forfeited match {match.MatchId}");
                FinalizeMatchResults(match);
            }
            SendToClient(conn, PVPPackets.BuildPVPMatchStatus(Gameplay.GroupDirectory.Instance.GetGroupForConn(conn.ConnId)?.GroupId ?? 1u, 0, 0, null));
            if (_zones.TryGetValue(conn.CurrentZoneId, out var z) && IsPvpZone(z.name))
                ChangeZone(conn, "pvp_start", "respawn");
        }

        public void ProcessMatchmakingTick(bool forceRun = false)
        {
            if (!forceRun && (DateTime.UtcNow - _lastMatchmakingTick).TotalMilliseconds < 1000)
                return;
            _lastMatchmakingTick = DateTime.UtcNow;

            if (_pendingDuelActivations.Count > 0)
            {
                var now = DateTime.UtcNow;
                for (int activationIndex = _pendingDuelActivations.Count - 1; activationIndex >= 0; activationIndex--)
                {
                    var (dueAt, duel) = _pendingDuelActivations[activationIndex];
                    if (dueAt <= now)
                    {
                        _pendingDuelActivations.RemoveAt(activationIndex);
                        if (_duelRuntime.ActivateCombat(duel.ChallengerLogin))
                            SendCombatStart(duel);
                    }
                }
            }

            var (newMatches, endedMatches, enteringCombat) = Gameplay.PVPMatchmaking.Instance.Tick();
            foreach (var match in newMatches)
                SpawnMatchParticipants(match);
            foreach (var match in enteringCombat)
                BeginPvpCombat(match);
            foreach (var match in endedMatches)
                FinalizeMatchResults(match);

            if (_pvpGateDropDue.Count > 0)
            {
                var nowUtc = DateTime.UtcNow;
                foreach (var kv in _pvpGateDropDue.ToList())
                {
                    if (nowUtc < kv.Value) continue;
                    var gConn = _connections.Values.FirstOrDefault(c => c.IsConnected && c.IsSpawned
                        && c.LoginName?.Equals(kv.Key, StringComparison.OrdinalIgnoreCase) == true);
                    if (gConn != null)
                    {
                        DropPvpGatesFor(gConn);
                        SendPVPControllerTransition(gConn, 0x17);
                        ScheduleCombatTimeReminders(gConn);
                    }
                    _pvpGateDropDue.Remove(kv.Key);
                }
            }

            if (_pvpCombatReminders.Count > 0)
            {
                var nowUtc = DateTime.UtcNow;
                foreach (var kv in _pvpCombatReminders.ToList())
                {
                    var list = kv.Value;
                    while (list.Count > 0 && nowUtc >= list[0].due)
                    {
                        var rConn = _connections.Values.FirstOrDefault(c => c.IsConnected && c.IsSpawned
                            && c.LoginName?.Equals(kv.Key, StringComparison.OrdinalIgnoreCase) == true
                            && _zones.TryGetValue(c.CurrentZoneId, out var rz) && IsPvpZone(rz.name));
                        if (rConn != null) SendSystemMessage(rConn, list[0].msg);
                        list.RemoveAt(0);
                    }
                    if (list.Count == 0) _pvpCombatReminders.Remove(kv.Key);
                }
            }

            if (_pendingKingsCoins.Count > 0)
            {
                foreach (var kv in _pendingKingsCoins.ToList())
                {
                    var rConn = _connections.Values.FirstOrDefault(c => c.IsConnected && c.IsSpawned
                        && c.LoginName?.Equals(kv.Key, StringComparison.OrdinalIgnoreCase) == true
                        && string.Equals(c.CurrentZoneName, "pvp_start", StringComparison.OrdinalIgnoreCase));
                    if (rConn == null) continue;
                    int coins = kv.Value;
                    GiveStackedItem(rConn, "QuestItemPAL.Token", coins, 100);
                    SendSystemMessage(rConn, coins >= 10
                        ? "You received ten King's Coins for this match."
                        : "You received five King's Coins for this match.");
                    _pendingKingsCoins.Remove(kv.Key);
                    Debug.LogError($"[PVP-MATCH] {kv.Key} granted {coins} King's Coins in pvp_start (deferred)");
                }
            }
        }

        private void SpawnMatchParticipants(Gameplay.PVPMatchmaking.Match match)
        {
            if (!_zones.Values.Any(z => z.name.Equals(match.ZoneName, StringComparison.OrdinalIgnoreCase)))
            {
                Debug.LogError($"[PVP-MATCH] Zone '{match.ZoneName}' not loaded - match {match.MatchId} cannot start");
                Gameplay.PVPMatchmaking.Instance.EndMatch(match.MatchId, "zone not loaded");
                return;
            }

            foreach (var login in match.ParticipantLogins)
            {
                var pConn = _connections.Values.FirstOrDefault(c =>
                    c.LoginName?.Equals(login, StringComparison.OrdinalIgnoreCase) == true);
                if (pConn == null) { Debug.LogError($"[PVP-MATCH] {login} not connected"); continue; }


                string entryPoint = match.IsRed(login) ? "red_team_start" : "blue_team_start";
                Debug.LogError($"[PVP-MATCH] Spawning {login} into {match.ZoneName}#{match.InstanceId} @ {entryPoint}");
                ChangeZone(pConn, match.ZoneName, entryPoint, (uint)match.InstanceId);
            }
            Gameplay.PVPMatchmaking.Instance.MarkMatchStarted(match.MatchId);
        }

        private void BeginPvpCombat(Gameplay.PVPMatchmaking.Match match)
        {
            Debug.LogError($"[PVP-MATCH] BeginPvpCombat {match.ZoneName}#{match.InstanceId} (combat phase begun)");
        }

        private void DropPvpGatesFor(RRConnection conn)
        {
            if (conn?.LoginName == null) return;
            if (!_pvpGateIdsByLogin.TryGetValue(conn.LoginName, out var gateIds) || gateIds.Count == 0)
            {
                Debug.LogError($"[PVP-GATES] no tracked gates for {conn.LoginName} (zone={conn.CurrentZoneName})");
                return;
            }
            foreach (var gid in gateIds)
            {
                var w = new LEWriter();
                w.WriteByte(0x03);
                w.WriteUInt16(gid);
                w.WriteByte(0x0A);
                w.WriteByte(0x00);
                conn.MessageQueue.Enqueue(w.ToArray());
            }
            Debug.LogError($"[PVP-GATES] dropped {gateIds.Count} gates for {conn.LoginName} (per-client, synced to countdown)");
        }

        private void SendPVPMatchControllerSpawn(RRConnection conn)
        {
            if (conn == null) return;
            var match = Gameplay.PVPMatchmaking.Instance.GetMatchForPlayer(conn.LoginName);
            ushort controllerId = (ushort)_nextEntityId++;
            var w = new LEWriter();
            w.WriteByte(0x07);
            w.WriteByte(0x01);
            w.WriteUInt16(controllerId);
            WriteGCType(w, "PVPMatchController", preserveCase: true);

            w.WriteByte(0x02);
            w.WriteUInt16(controllerId);
            w.WriteUInt32(0);
            w.WriteByte(0x00);
            w.WriteByte(0x00);
            if (match != null && match.Archetype == Gameplay.PVPMatchmaking.Archetype.GroupPracticeMatch)
            {
                w.WriteByte(0x01);
                WritePVPTeamScore(w, "pvp.FFATeam");
            }
            else
            {
                string teamList = (match != null && match.Archetype == Gameplay.PVPMatchmaking.Archetype.GroupDuelMatch)
                    ? "pvp.DuelTeamList" : "pvp.DefaultTeamList";
                w.WriteByte(0x02);
                WritePVPTeamScore(w, teamList + ".RedTeam");
                WritePVPTeamScore(w, teamList + ".BlueTeam");
            }

            w.WriteByte(0x06);
            SendToClient(conn, w.ToArray());
            if (conn.LoginName != null)
            {
                _pvpControllerReady.Add(conn.LoginName);
                _pvpControllerIdByLogin[conn.LoginName] = controllerId;
                int setupSec = match?.SetupTimeSec ?? 15;
                _pvpGateDropDue[conn.LoginName] = DateTime.UtcNow.AddSeconds(setupSec);
            }
            Debug.LogError($"[PVP-MATCH] Sent PVPMatchController entity {controllerId} (+team scores) to {conn.LoginName} (zone={conn.CurrentZoneName})");

            if (match != null && match.ParticipantLogins.All(l => _pvpControllerReady.Contains(l)))
                Gameplay.PVPMatchmaking.Instance.BeginSetupPhase(match.MatchId);
        }

        private void WritePVPTeamScore(LEWriter w, string teamGcPath)
        {
            for (int i = 0; i < 6; i++) w.WriteUInt32(0);
            w.WriteByte(0x00); w.WriteByte(0x00); w.WriteByte(0x00);
            w.WriteByte(0xFF); w.WriteCString(teamGcPath);
        }

        private void SendPVPControllerTransition(RRConnection conn, ushort nextState)
        {
            if (conn?.LoginName == null) return;
            if (!_pvpControllerIdByLogin.TryGetValue(conn.LoginName, out var controllerId)) return;
            var w = new LEWriter();
            w.WriteByte(0x07);
            w.WriteByte(0x02);
            w.WriteUInt16(controllerId);
            w.WriteUInt32(0);
            w.WriteByte(0x00);
            w.WriteByte(0x09);
            w.WriteUInt16(nextState);
            w.WriteByte(0x00);
            w.WriteByte(0x06);
            SendToClient(conn, w.ToArray());
            Debug.LogError($"[PVP-MATCH] Drove controller {controllerId} -> FSM next-state 0x{nextState:X2} for {conn.LoginName} (server-timed)");
        }

        private void ScheduleCombatTimeReminders(RRConnection conn)
        {
            if (conn?.LoginName == null) return;
            var match = Gameplay.PVPMatchmaking.Instance.GetMatchForPlayer(conn.LoginName);
            int combatSec = match?.CombatTimeSec ?? 300;
            var now = DateTime.UtcNow;
            var list = new List<(DateTime, string)>();
            for (int elapsed = 60; elapsed < combatSec; elapsed += 60)
            {
                int remMin = (combatSec - elapsed) / 60;
                if (remMin < 1) continue;
                string msg = remMin == 1
                    ? "The battle will end in 1 minute!"
                    : $"The battle will end in {remMin} minutes!";
                list.Add((now.AddSeconds(elapsed), msg));
            }
            if (list.Count > 0) _pvpCombatReminders[conn.LoginName] = list;
        }

        private void FinalizeMatchResults(Gameplay.PVPMatchmaking.Match match)
        {
            foreach (var login in match.ParticipantLogins) _pvpCombatReminders.Remove(login);

            if (string.IsNullOrEmpty(match.WinnerLogin))
            {
                Debug.LogError($"[PVP-MATCH] Match {match.MatchId} ended with no winner (stalemate) - driving END card");
                foreach (var login in match.ParticipantLogins)
                {
                    var sConn = _connections.Values.FirstOrDefault(c =>
                        c.LoginName?.Equals(login, StringComparison.OrdinalIgnoreCase) == true);
                    if (sConn != null && sConn.IsConnected && sConn.IsSpawned
                        && _zones.TryGetValue(sConn.CurrentZoneId, out var sz) && IsPvpZone(sz.name))
                        SendPVPControllerTransition(sConn, 0x16);
                }
                return;
            }

            bool isRanked = Gameplay.PVPMatchmaking.IsRanked(match.Archetype);

            foreach (var login in match.ParticipantLogins.Concat(new[] { match.WinnerLogin }).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                bool isWinner = login.Equals(match.WinnerLogin, StringComparison.OrdinalIgnoreCase);
                int preMatchRating = match.ParticipantRatings.TryGetValue(login, out var mr) ? mr : 1500;
                int ratingDelta = 0;
                int newRating = preMatchRating;
                if (isRanked)
                {
                    var opponentRatings = match.ParticipantLogins
                        .Where(p => !p.Equals(login, StringComparison.OrdinalIgnoreCase))
                        .Select(p => match.ParticipantRatings.TryGetValue(p, out var or) ? or : 1500)
                        .ToList();
                    ratingDelta = Gameplay.PVPMatchmaking.EloDelta(preMatchRating, opponentRatings, isWinner ? 1.0 : 0.0);
                    newRating = System.Math.Max(0, System.Math.Min(3000, preMatchRating + ratingDelta));
                }
                try
                {
                    Debug.LogError($"[PVP-MATCH] {login}: {(isWinner ? "WIN" : "LOSS")}, rating {preMatchRating}->{newRating} ({ratingDelta:+#;-#;0})");
                    var pConn = _connections.Values.FirstOrDefault(c =>
                        c.LoginName?.Equals(login, StringComparison.OrdinalIgnoreCase) == true);

                    var sc = pConn != null ? GetActiveCharacter(pConn) : null;
                    if (sc != null)
                    {
                        if (isWinner) sc.pvpWins += 1;
                        if (isRanked) sc.pvpRating = newRating;
                        Database.CharacterRepository.SetPvpStats((long)sc.id, sc.pvpWins, sc.pvpRating);
                    }
                    if (isRanked)
                    {
                        _pendingKingsCoins[login] = isWinner ? 10 : 5;
                        Debug.LogError($"[PVP-MATCH] {login} pending {(isWinner ? 10 : 5)} King's Coins (granted on return to pvp_start)");
                    }
                    if (pConn != null && !match.EndedByForfeit && _zones.TryGetValue(pConn.CurrentZoneId, out var z) && IsPvpZone(z.name))
                        ChangeZone(pConn, "pvp_start", "respawn");
                }
                catch (Exception ex) { Debug.LogError($"[PVP-MATCH] Failed record for {login}: {ex.Message}"); }
            }
        }

        private static bool IsPvpZone(string zoneName)
        {
            if (string.IsNullOrEmpty(zoneName)) return false;
            return zoneName.StartsWith("PVPGroup", StringComparison.OrdinalIgnoreCase)
                || zoneName.StartsWith("DeathMatch", StringComparison.OrdinalIgnoreCase);
        }

        private static string TryReadGcTypeString(byte[] data)
        {
            if (data == null || data.Length < 2) return null;
            if (data[0] != 0xFF) return null;
            int end = 1;
            while (end < data.Length && data[end] != 0) end++;
            if (end <= 1) return null;
            return System.Text.Encoding.ASCII.GetString(data, 1, end - 1);
        }

        public void SendGroupConnectedToAll(Gameplay.Group group)
        {
            var leaderConn = FindConnectionById(group.LeaderConnId);

            uint leaderGroupId = 0;
            if (leaderConn != null)
                leaderGroupId = leaderConn.CurrentZoneId;
            else
            {
                foreach (var member in group.Members)
                {
                    if (!member.IsOnline) continue;
                    var memberConnection = FindConnectionById(member.ConnId);
                    if (memberConnection != null) { leaderGroupId = memberConnection.CurrentZoneId; break; }
                }
            }

            var members = new System.Collections.Generic.List<GroupMemberInfo>();
            var leaderMember = group.Members.Find(member => member.ConnId == group.LeaderConnId);
            if (leaderMember != null)
            {
                var leaderConnection = FindConnectionById(leaderMember.ConnId);
                if (leaderConnection != null && leaderMember.IsOnline)
                    members.Add(BuildGroupMemberInfo(leaderConnection));
                else if (leaderMember.CharSqlId != 0)
                    members.Add(BuildGroupMemberInfoFromCache(leaderMember));
            }
            foreach (var member in group.Members)
            {
                if (member.ConnId == group.LeaderConnId) continue;
                var memberConnection = FindConnectionById(member.ConnId);
                if (memberConnection != null && member.IsOnline)
                    members.Add(BuildGroupMemberInfo(memberConnection));
                else if (member.CharSqlId != 0)
                    members.Add(BuildGroupMemberInfoFromCache(member));
            }

            foreach (var member in group.Members)
            {
                if (!member.IsOnline) continue;
                var memberConnection = FindConnectionById(member.ConnId);
                if (memberConnection != null)
                {
                    if (!memberConnection.GroupConnectedSent)
                    {
                        uint selfLeaderKey = GetCharSqlId(memberConnection);
                        byte[] connectedPacket = GroupPackets.BuildProcessConnected(
                            selfLeaderKey, group.MonsterDifficulty, group.InviteMode);
                        SendToClient(memberConnection, connectedPacket);
                        memberConnection.GroupConnectedSent = true;
                        Debug.LogError($"[GROUP] Sent 0x30 to {memberConnection.LoginName}: GC+0xB0=0x{selfLeaderKey:X8}");
                    }

                    byte isOpenGroup = (byte)(group.IsOpen ? 1 : 0);
                    uint leaderCharSqlId = 0;
                    var leaderKeyConnection = FindConnectionById(group.LeaderConnId);
                    if (leaderKeyConnection != null)
                        leaderCharSqlId = GetCharSqlId(leaderKeyConnection);
                    else
                        leaderCharSqlId = group.Members.FirstOrDefault(m => m.ConnId == group.LeaderConnId)?.CharSqlId ?? 0;
                    byte[] groupPacket = GroupPackets.BuildProcessUserChangedGroup(
                        group.GroupId, leaderCharSqlId, 0xFF, isOpenGroup,
                        0, 0,
                        (byte)members.Count, members.ToArray());
                    SendToClient(memberConnection, groupPacket);
                    Debug.LogError($"[GROUP] Sent 0x35 to {memberConnection.LoginName}: leaderCharSqlId=0x{leaderCharSqlId:X8}, {members.Count} members");
                    SendJoinTalkbackGroup(memberConnection, group);
                }
            }

        }

        private void SendJoinTalkbackGroup(RRConnection conn, Gameplay.Group group)
        {
            SendJoinTalkbackGroup(conn, group?.GroupId ?? 0);
        }

        private void SendJoinTalkbackGroup(RRConnection conn, uint talkbackGroupId)
        {
            if (conn == null || talkbackGroupId == 0) return;
            if (conn.Client?.Client?.LocalEndPoint is not System.Net.IPEndPoint localEndPoint) return;
            byte[] address = localEndPoint.Address.MapToIPv4().GetAddressBytes();
            if (address.Length != 4) return;
            var writer = new LEWriter();
            writer.WriteByte(0x09);
            writer.WriteByte(0x50);
            writer.WriteUInt32(GetCharSqlId(conn));
            writer.WriteUInt32(GetCharSqlId(conn));
            writer.WriteByte(IsPlayerFree(conn.LoginName) ? (byte)0x00 : (byte)0x01);
            writer.WriteUInt32(talkbackGroupId);
            writer.WriteByte(address[0]);
            writer.WriteByte(address[1]);
            writer.WriteByte(address[2]);
            writer.WriteByte(address[3]);
            writer.WriteUInt32(DungeonRunners.Talkback.TalkbackServer.Port);
            SendToClient(conn, writer.ToArray());
            Debug.LogError($"[TALKBACK] JoinTalkbackGroup conn={conn.ConnId} ip={localEndPoint.Address} port={DungeonRunners.Talkback.TalkbackServer.Port} userId=0x{GetCharSqlId(conn):X8} groupId={talkbackGroupId}");
        }

        private void SendGroupHealthToAll(Gameplay.Group group)
        {
            foreach (var member in group.Members)
            {
                if (!member.IsOnline) continue;
                var memberConnection = FindConnectionById(member.ConnId);
                if (memberConnection == null) continue;
                uint charSqlId = GetCharSqlId(memberConnection);
                ResolveGroupMemberHealthMana(memberConnection, out byte hp15, out byte mp15);
                byte[] healthPacket = GroupPackets.BuildMemberHealthMana(charSqlId, hp15, mp15);
                _groupMemberHealthManaState[charSqlId] = PackGroupHealthMana(hp15, mp15);
                foreach (var target in group.Members)
                {
                    if (!target.IsOnline) continue;
                    var targetConnection = FindConnectionById(target.ConnId);
                    if (targetConnection != null) SendCompressedA(targetConnection, 0x01, 0x0F, healthPacket);
                }
            }
        }

        private void ResolveGroupMemberHealthMana(RRConnection conn, out byte hp15, out byte mp15)
        {
            hp15 = 15;
            mp15 = 15;
            var state = GetPlayerState(conn.ConnId.ToString());
            if (state == null) return;
            if (state.MaxHPWire > 0)
                hp15 = (byte)Math.Clamp((int)((long)state.CurrentHPWire * 15 / state.MaxHPWire), 0, 15);
            if (state.MaxManaWire > 0)
                mp15 = (byte)Math.Clamp((int)((long)state.CurrentManaWire * 15 / state.MaxManaWire), 0, 15);
        }

        private static byte PackGroupHealthMana(byte hp15, byte mp15)
        {
            return (byte)(((hp15 & 0xF) << 4) | (mp15 & 0xF));
        }

        private void SendGroupMemberHealthManaIfChanged(RRConnection conn)
        {
            if (conn == null || string.IsNullOrEmpty(conn.LoginName)) return;
            var group = GroupDirectory.Instance.GetGroupForConn(conn.ConnId);
            if (group == null || group.Members.Count <= 1) return;
            uint charSqlId = GetCharSqlId(conn);
            if (charSqlId == 0) return;
            ResolveGroupMemberHealthMana(conn, out byte hp15, out byte mp15);
            byte packed = PackGroupHealthMana(hp15, mp15);
            if (_groupMemberHealthManaState.TryGetValue(charSqlId, out byte current) && current == packed)
                return;
            _groupMemberHealthManaState[charSqlId] = packed;
            byte[] healthPacket = GroupPackets.BuildMemberHealthMana(charSqlId, hp15, mp15);
            foreach (var member in group.Members)
            {
                if (!member.IsOnline) continue;
                var targetConnection = FindConnectionById(member.ConnId);
                if (targetConnection != null) SendCompressedA(targetConnection, 0x01, 0x0F, healthPacket);
            }
        }

        private void HandleGroupChannel(RRConnection conn, byte messageType, byte[] data)
        {
            Debug.LogError($"[CH9] type=0x{messageType:X2} len={data?.Length ?? 0} data={BitConverter.ToString(data ?? new byte[0])}");

            if (_adminCommands == null)
                _adminCommands = new AdminCommands();
            _adminCommands.SetServerCallbacks(
                (c, zone) => ChatChangeZone(c, zone),
                (c) => {
                    if (_selectedCharacter.TryGetValue(c.LoginName, out var ch))
                        return CharacterRepository.GetCharacter(ch.Id);
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

            PlayerState playerState = GetPlayerState(conn.ConnId.ToString());

            if (IsPlayerAdmin(conn.LoginName) && _adminCommands.TryExecute(conn, messageType, data, playerState, SendSystemMessage, SendToClient))
            {
                var ack = new LEWriter();
                ack.WriteByte(9);
                ack.WriteByte(messageType);
                SendCompressedA(conn, 0x01, 0x0F, ack.ToArray());
                return;
            }

            switch (messageType)
            {
                case 0x00:
                    Debug.Log($"[GROUP] Connect ack from {conn.LoginName}");
                    return;
                case 0x16:
                case 0x12:
                case 0x20:
                case 0x21:
                case 0x22:
                case 0x14:
                case 0x15:
                case 0x17:
                case 0x24:
                case 0x26:
                case 0x27:
                case 0x28:
                case 0x29:
                case 0x2A:
                case 0x2B:
                case 0x2C:
                case 0x2D:
                case 0x2E:
                case 0x2F:
                    HandleGroupClientChannel(conn, messageType, data);
                    return;
            }

            Debug.Log($"Group channel message type: 0x{messageType:X2}");
            var groupAck = new LEWriter();
            groupAck.WriteByte(9);
            groupAck.WriteByte(messageType);
            SendCompressedA(conn, 0x01, 0x0F, groupAck.ToArray());
        }


        private void HandleZoneChannel(RRConnection conn, byte[] body)
        {
            Debug.LogError($" HandleZoneChannel: bodyLen={body?.Length ?? 0}, body={BitConverter.ToString(body ?? new byte[0])}");

            if (body == null || body.Length == 0)
            {
                if (conn.TickCoroutine != null)
                {
                    Debug.LogError("[ZONE-JOIN] Player already spawned - ignoring duplicate zone join request");
                    return;
                }

                Debug.Log("CLIENT SENT EMPTY 13/6 PROGRESSION REQUEST - Starting zone progression!");

                Debug.LogError($"[ZONE-JOIN] zoneId={conn.CurrentZoneId} hex=0x{conn.CurrentZoneId:X8} source=HandleCharacterPlay");

                Debug.Log("STEP 1: Sending ZoneChannel + ZoneMessageReady (ID 1) - should trigger State 110");
                LEWriter zoneReadyWriter = new LEWriter();
                zoneReadyWriter.WriteByte(13);
                zoneReadyWriter.WriteByte(1);

                uint zoneReadyPlayerId = GetCharSqlId(conn);
                zoneReadyWriter.WriteUInt32(zoneReadyPlayerId);
                Debug.LogError($"[ZONE-READY] playerUserId=0x{zoneReadyPlayerId:X8} sourceFunction=ZoneClient::processReady@0x5FC250 field=ZoneClient+0xf4");

                ushort exploredBitCount = ResolveExploredBitCount(conn, "ZoneProgressionEmpty");

                zoneReadyWriter.WriteUInt16(exploredBitCount);
                for (int exploredBitIndex = 0; exploredBitIndex < exploredBitCount; exploredBitIndex++)
                {
                    zoneReadyWriter.WriteUInt32(0x00000000);
                }

                byte[] step1Data = zoneReadyWriter.ToArray();
                Debug.LogError($"[STEP1] ZoneMessageReady: {step1Data.Length} bytes");
                Debug.LogError($"[STEP1] Hex: {BitConverter.ToString(step1Data.Take(20).ToArray())}...");

                SendCompressedA(conn, 0x01, 0x0F, step1Data);
                Debug.Log("STEP 1 COMPLETE: Sent ZoneChannel + ZoneMessageReady (ID 1)");

                Debug.Log("STEP 2: Sending ZoneChannel + ZoneMessageInstanceCount (ID 5) - should trigger States 114, 115");
                LEWriter zoneInstanceWriter = new LEWriter();
                zoneInstanceWriter.WriteByte(13);
                zoneInstanceWriter.WriteByte(5);
                zoneInstanceWriter.WriteUInt32(0x00);
                zoneInstanceWriter.WriteUInt32(0x00);
                SendCompressedA(conn, 0x01, 0x0F, zoneInstanceWriter.ToArray());
                Debug.Log("STEP 2 COMPLETE: Sent ZoneChannel + ZoneMessageInstanceCount (ID 5)");

                Debug.Log("STEP 3: Sending ClientEntityChannel + Interval (ID 0x0D)");
                int tick = (int)(Time.time / 0.033f);
                LEWriter intervalWriter = new LEWriter();
                intervalWriter.WriteByte(7);
                intervalWriter.WriteByte(0x0D);
                intervalWriter.WriteInt32(tick);
                intervalWriter.WriteInt32(0x21);
                Debug.LogError($"[INTERVAL] tick={tick} tickInterval=0x21 messageRatio=1 sourceFunction=ClientEntityManager::processInterval@0x5DA7D0");
                intervalWriter.WriteInt32(1);
                intervalWriter.WriteInt32(1);
                intervalWriter.WriteUInt16(100);
                intervalWriter.WriteUInt16(20);

                intervalWriter.WriteByte(0x06);

                Debug.Log($"INTERVAL MESSAGE SIZE: {intervalWriter.ToArray().Length} bytes (before compression)");
                Debug.Log($"INTERVAL DATA: {BitConverter.ToString(intervalWriter.ToArray())}");
                SendCompressedA(conn, 0x01, 0x0F, intervalWriter.ToArray());
                Debug.Log("STEP 3 COMPLETE: Sent ClientEntityChannel + Interval (ID 0x0D)");

                bool zoneRuntimePrepared = TryPrepareZoneJoinRoomRuntime(
                    conn,
                    "ZONE-JOIN pre-player",
                    out Zone preparedSpawnZone,
                    out string preparedZoneName,
                    out string preparedInstanceKey,
                    out uint preparedRoomSeed,
                    out uint preparedLayoutSeed);

                Debug.Log("[ZONE-JOIN] step=4 action=send-spawn-data before=connected-message");
                SendPlayerEntitySpawn(conn);
                StartCoroutine(SendZoneSpawnInvulnerabilityAfterDelay(conn, ZoneSpawnInvulnerabilityAddDelay));
                Debug.Log("[ZONE-JOIN] step=4 action=send-spawn-data state=complete");

                if ((zoneRuntimePrepared && preparedSpawnZone != null) || _zones.TryGetValue(conn.CurrentZoneId, out preparedSpawnZone))
                {
                    string zoneName = zoneRuntimePrepared ? preparedZoneName : preparedSpawnZone.name;
                    string instanceKey = zoneRuntimePrepared ? preparedInstanceKey : GetInstanceZoneKey(conn);
                    Debug.LogError($"[ZONE-JOIN] Zone: '{zoneName}' instance: '{instanceKey}' (inst={conn.InstanceId})");

                    bool mobsAlreadyExist = CombatRuntime.Instance.GetMonstersInZone(instanceKey).Any();
                    bool continuousPresence = _connections.Values.Any(o =>
                        o != null && o != conn && o.IsSpawned &&
                        string.Equals(GetInstanceZoneKey(o), instanceKey, StringComparison.OrdinalIgnoreCase));
                    if (mobsAlreadyExist && !continuousPresence)
                    {
                        int reentryCleared = CombatRuntime.Instance.ClearInstanceMobs(instanceKey);
                        ZoneSpawner.Instance.ResetZone(instanceKey);
                        Debug.LogError($"[ZONE-JOIN] RE-ENTRY solo (no continuous presence) in '{instanceKey}', cleared {reentryCleared} stale monsters; respawning fresh (returning client still holds old entity ids, reusing them collides error 5)");
                        mobsAlreadyExist = false;
                    }

                    if (mobsAlreadyExist)
                    {
                        Debug.LogError($"[ZONE-JOIN]  LATE JOINER - mobs already exist in '{instanceKey}', sending existing monsters");
                        if (!zoneRuntimePrepared)
                            SendRandomSeed(conn, ResolveRuntimeZoneSeed(conn, zoneName), false);
                        foreach (var monster in CombatRuntime.Instance.GetMonstersInZone(instanceKey))
                        {
                            if (!monster.IsAlive) continue;
                            SendMonsterToClient(conn, monster);
                            Debug.LogError($"[ZONE-JOIN] Sent existing monster {monster.Name} (ID:{monster.EntityId}) to late joiner {conn.LoginName}");
                        }
                    }
                    else
                    {
                        _finalizedMonsterKills.Clear();
                        _pendingKills.Clear();

                        uint rngSeed = zoneRuntimePrepared ? preparedRoomSeed : ResolveRuntimeZoneSeed(conn, zoneName);
                        uint layoutSeed = zoneRuntimePrepared ? preparedLayoutSeed : ResolveZoneLayoutSeed(conn, zoneName);
                        if (!zoneRuntimePrepared)
                            SendRandomSeed(conn, rngSeed, true);
                        Debug.LogError($"[ZONE-JOIN] EntityManager opcode 0x0C seed: 0x{rngSeed:X8} {FormatDungeonLayoutSeedForLog(zoneName, layoutSeed)} for instance '{instanceKey}'");

                        var spawned = ZoneSpawner.Instance.SpawnZoneMobsForInstance(zoneName, instanceKey, layoutSeed, rngSeed);

                        ApplyDifficultyToMonsters(conn, instanceKey);

                        var freshSpawnRecipients = GetInstanceRecipients(conn);
                        foreach (var monster in CombatRuntime.Instance.GetMonstersInZone(instanceKey))
                        {
                            monster.AggroTriggered = false;
                            monster.State = MonsterState.Idle;
                            monster.TargetId = 0;
                            monster.RngSeed = rngSeed;

                            foreach (var recipient in freshSpawnRecipients)
                                SendMonsterToClient(recipient, monster);
                            Debug.LogError($"[ZONE-JOIN] Sent zone monster {monster.Name} to {freshSpawnRecipients.Count} instance conn(s)");
                        }
                    }
                }



                {
                    var zoneGroup = GroupDirectory.Instance.GetGroupForConn(conn.ConnId);
                    if (zoneGroup != null)
                    {
                        GroupDirectory.Instance.UpdateMemberZone(conn.ConnId, conn.CurrentZoneName ?? "");
                        SendGroupConnectedToAll(zoneGroup);
                        SendGroupHealthToAll(zoneGroup);
                        Debug.LogError($"[GROUP] Sent full group state for zone join: group={zoneGroup.GroupId}");
                    }
                    else
                    {
                        uint soloSelfKey = GetCharSqlId(conn);
                        byte[] soloConnected = GroupPackets.BuildProcessConnected(soloSelfKey, 0, 0);
                        SendCompressedA(conn, 0x01, 0x0F, soloConnected);
                        byte[] groupState = GroupPackets.BuildProcessUserChangedGroup(
                            1, soloSelfKey, 0xFF, 0,
                            0, 0,
                            0, new GroupMemberInfo[0]);
                        SendCompressedA(conn, 0x01, 0x0F, groupState);
                        Debug.LogError($"[GROUP] Sent 0x30+0x35 solo leader: GC+0xB0=GC+0xD4=0x{soloSelfKey:X8}");
                        SendJoinTalkbackGroup(conn, soloSelfKey);
                    }
                }

                ResendAllModifiers(conn);

                return;
            }

            if (body.Length >= 1)
            {
                byte type = body[0];
                Debug.Log($"Zone channel message type: 0x{type:X2}");

                if (type == 0x06)
                {
                    if (conn.TickCoroutine != null)
                    {
                        Debug.LogError($"[ZONE-JOIN]  Got 0x06 but player already spawned (TickCoroutine active) - IGNORING to prevent screen jump. body={BitConverter.ToString(body)}");
                        return;
                    }

                    conn.AllowFlush = false;
                    Debug.LogError($"[ZONE-JOIN]  Client sent ZoneJoin (0x06) - FULL SPAWN SEQUENCE");

                    bool isZoneTransition = (conn.Avatar != null && conn.Player != null);
                    Debug.LogError($"[ZONE-JOIN] isZoneTransition={isZoneTransition} (Avatar={(conn.Avatar != null ? conn.Avatar.Id.ToString() : "null")}, Player={(conn.Player != null ? conn.Player.Id.ToString() : "null")})");


                    Debug.LogError("[ZONE-JOIN] FIRST LOGIN - Sending full spawn sequence");

                    ushort exploredBitCount = ResolveExploredBitCount(conn, "ZoneJoin");

                    var readyWriter = new LEWriter();
                    readyWriter.WriteByte(13);
                    readyWriter.WriteByte(1);
                    readyWriter.WriteUInt32(GetCharSqlId(conn));
                    readyWriter.WriteUInt16(exploredBitCount);
                    for (int exploredBitIndex = 0; exploredBitIndex < exploredBitCount; exploredBitIndex++)
                    {
                        readyWriter.WriteUInt32(0x00000000);
                    }
                    SendCompressedA(conn, 0x01, 0x0F, readyWriter.ToArray());

                    var instanceWriter = new LEWriter();
                    instanceWriter.WriteByte(13);
                    instanceWriter.WriteByte(5);
                    instanceWriter.WriteUInt32(0x00);
                    instanceWriter.WriteUInt32(0x00);
                    SendCompressedA(conn, 0x01, 0x0F, instanceWriter.ToArray());

                    int tick = (int)(Time.time / 0.033f);
                    var intervalWriter = new LEWriter();
                    intervalWriter.WriteByte(7);
                    intervalWriter.WriteByte(0x0D);
                    intervalWriter.WriteInt32(tick);
                    intervalWriter.WriteInt32(33);
                    intervalWriter.WriteInt32(1);
                    intervalWriter.WriteInt32(1);
                    intervalWriter.WriteUInt16(100);
                    intervalWriter.WriteUInt16(20);
                    intervalWriter.WriteByte(0x06);
                    SendCompressedA(conn, 0x01, 0x0F, intervalWriter.ToArray());

                    bool zoneRuntimePrepared = TryPrepareZoneJoinRoomRuntime(
                        conn,
                        "ZONE-JOIN pre-player",
                        out Zone preparedSpawnZone,
                        out string preparedZoneName,
                        out string preparedInstanceKey,
                        out uint preparedRoomSeed,
                        out uint preparedLayoutSeed);

                    SendPlayerEntitySpawn(conn);
                    StartCoroutine(SendZoneSpawnInvulnerabilityAfterDelay(conn, ZoneSpawnInvulnerabilityAddDelay));

                    if ((zoneRuntimePrepared && preparedSpawnZone != null) || _zones.TryGetValue(conn.CurrentZoneId, out preparedSpawnZone))
                    {
                        string zoneName = zoneRuntimePrepared ? preparedZoneName : preparedSpawnZone.name;
                        string instanceKey = zoneRuntimePrepared ? preparedInstanceKey : GetInstanceZoneKey(conn);
                        Debug.LogError($"[ZONE-JOIN] Zone: '{zoneName}' instance: '{instanceKey}' (inst={conn.InstanceId})");

                        bool mobsAlreadyExist = CombatRuntime.Instance.GetMonstersInZone(instanceKey).Any();
                        bool continuousPresence = _connections.Values.Any(o =>
                            o != null && o != conn && o.IsSpawned &&
                            string.Equals(GetInstanceZoneKey(o), instanceKey, StringComparison.OrdinalIgnoreCase));
                        if (mobsAlreadyExist && !continuousPresence)
                        {
                            int reentryCleared = CombatRuntime.Instance.ClearInstanceMobs(instanceKey);
                            ZoneSpawner.Instance.ResetZone(instanceKey);
                            Debug.LogError($"[ZONE-JOIN] RE-ENTRY solo (no continuous presence) in '{instanceKey}', cleared {reentryCleared} stale monsters; respawning fresh (returning client still holds old entity ids, reusing them collides error 5)");
                            mobsAlreadyExist = false;
                        }

                        if (mobsAlreadyExist)
                        {
                            Debug.LogError($"[ZONE-JOIN]  LATE JOINER - mobs already exist in '{instanceKey}', sending existing monsters");
                            if (!zoneRuntimePrepared)
                                SendRandomSeed(conn, ResolveRuntimeZoneSeed(conn, zoneName), false);
                            foreach (var monster in CombatRuntime.Instance.GetMonstersInZone(instanceKey))
                            {
                                if (!monster.IsAlive) continue;
                                SendMonsterToClient(conn, monster);
                                Debug.LogError($"[ZONE-JOIN] Sent existing monster {monster.Name} (ID:{monster.EntityId}) to late joiner {conn.LoginName}");
                            }
                        }
                        else
                        {
                            _finalizedMonsterKills.Clear();
                            _pendingKills.Clear();

                            uint rngSeed = zoneRuntimePrepared ? preparedRoomSeed : ResolveRuntimeZoneSeed(conn, zoneName);
                            uint layoutSeed = zoneRuntimePrepared ? preparedLayoutSeed : ResolveZoneLayoutSeed(conn, zoneName);
                            if (!zoneRuntimePrepared)
                                SendRandomSeed(conn, rngSeed, true);
                            Debug.LogError($"[ZONE-JOIN] EntityManager opcode 0x0C seed: 0x{rngSeed:X8} {FormatDungeonLayoutSeedForLog(zoneName, layoutSeed)} for instance '{instanceKey}'");

                            var spawned = ZoneSpawner.Instance.SpawnZoneMobsForInstance(zoneName, instanceKey, layoutSeed, rngSeed);

                            ApplyDifficultyToMonsters(conn, instanceKey);

                            foreach (var monster in CombatRuntime.Instance.GetMonstersInZone(instanceKey))
                            {
                                monster.AggroTriggered = false;
                                monster.State = MonsterState.Idle;
                                monster.TargetId = 0;
                                monster.RngSeed = rngSeed;

                                SendMonsterToClient(conn, monster);
                                Debug.LogError($"[ZONE-JOIN] Sent zone monster {monster.Name} to client");
                            }
                        }
                    }


                    {
                        var zoneGroup = GroupDirectory.Instance.GetGroupForConn(conn.ConnId);
                        if (zoneGroup != null)
                        {
                            GroupDirectory.Instance.UpdateMemberZone(conn.ConnId, conn.CurrentZoneName ?? "");
                            SendGroupConnectedToAll(zoneGroup);
                            Debug.LogError($"[GROUP] Sent full group state for zone-transition: group={zoneGroup.GroupId}");
                            var zoneStates = new List<(int ConnId, byte[] Packet)>();
                            foreach (var groupMember in zoneGroup.Members)
                            {
                                var groupMemberConnection = FindConnectionById(groupMember.ConnId);
                                if (groupMemberConnection == null) continue;
                                uint memberCharSqlId = GetCharSqlId(groupMemberConnection);
                                if (memberCharSqlId == 0) continue;
                                zoneStates.Add((groupMember.ConnId, GroupPackets.BuildUserChangedZone(memberCharSqlId, groupMemberConnection.CurrentZoneName ?? "")));
                            }
                            foreach (var groupMember in zoneGroup.Members)
                            {
                                var groupMemberConnection = FindConnectionById(groupMember.ConnId);
                                if (groupMemberConnection == null) continue;
                                foreach (var zoneState in zoneStates)
                                {
                                    if (zoneState.ConnId == groupMember.ConnId) continue;
                                    SendToClient(groupMemberConnection, zoneState.Packet);
                                }
                            }
                        }
                        else
                        {
                            uint soloSelfKey2 = GetCharSqlId(conn);
                            byte[] soloConnected = GroupPackets.BuildProcessConnected(soloSelfKey2, 0, 0);
                            SendCompressedA(conn, 0x01, 0x0F, soloConnected);
                            byte[] groupState = GroupPackets.BuildProcessUserChangedGroup(
                                1, soloSelfKey2, 0xFF, 0,
                                0, 0, 0, new GroupMemberInfo[0]);
                            SendCompressedA(conn, 0x01, 0x0F, groupState);
                            Debug.LogError($"[GROUP] Sent 0x30+0x35 zone-transition solo leader: GC+0xB0=GC+0xD4=0x{soloSelfKey2:X8}");
                            SendJoinTalkbackGroup(conn, soloSelfKey2);
                        }
                    }

                    ResendAllModifiers(conn);

                    return;
                }
                else if (type == 0x00)
                {
                    Debug.Log("ZoneJoin request (0x00) - acknowledging zone join");
                    LEWriter joinAck = new LEWriter();
                    joinAck.WriteByte(13);
                    joinAck.WriteByte(0x00);
                    SendCompressedA(conn, 0x01, 0x0F, joinAck.ToArray());
                }
                else if (type == 0x01)
                {
                    Debug.Log("ZoneEnter confirmation (0x01) - client successfully entered zone");
                }
                else if (type == 0x08)
                {
                    Debug.Log("IGNORING 13/8 message - do not acknowledge to prevent crashes");
                }
                else
                {
                    Debug.LogWarning($"Unknown zone channel message type: 0x{type:X2}");
                }
            }
        }

        private void SendCharacterList(RRConnection conn)
        {
            Debug.LogError($"[CHARLIST] *** ENTRY *** Sending character list for {conn.LoginName}");

            try
            {
                if (!_persistentCharacters.TryGetValue(conn.LoginName, out var characters))
                {
                    characters = new List<GCObject>();
                    _persistentCharacters[conn.LoginName] = characters;
                }

                if (characters.Count == 0)
                {
                    ;
                    var savedChars = CharacterRepository.GetCharactersForAccount(conn.LoginName);
                    Debug.LogError($"[CHARLIST] Loading {savedChars?.Count ?? 0} characters for account '{conn.LoginName}'");

                    foreach (var savedChar in savedChars ?? new List<SavedCharacter>())
                    {
                        var charObj = new GCObject
                        {
                            Id = savedChar.id,
                            DFCClass = "Player",
                            GCClass = "Player",
                            Name = savedChar.name
                        };
                        characters.Add(charObj);
                        Debug.LogError($"[CHARLIST] Loaded: {savedChar.name} (ID={savedChar.id})");
                    }
                }

                Debug.LogError($"[CHARLIST] Found {characters.Count} characters");

                if (characters.Count == 0)
                {
                    var emptyWriter = new LEWriter();
                    emptyWriter.WriteByte(4);
                    emptyWriter.WriteByte(3);
                    emptyWriter.WriteByte(0);
                    SendCompressedA(conn, 0x01, 0x0f, emptyWriter.ToArray());
                    _charListSent[conn.ConnId] = true;
                    return;
                }



                uint savedNextEntityId = _nextEntityId;
                _nextEntityId = 10000;
                var sortedCharacters = characters.OrderBy(c => c.Id).ToList();

                int slotIndex = 0;
                foreach (var character in sortedCharacters)
                {
                    var writer = new LEWriter();
                    writer.WriteByte(4);
                    writer.WriteByte(2);
                    writer.WriteUInt32(character.Id);
                    writer.WriteCString(character.Name);

                    GCObject playerObject = GCObjectFactory.NewPlayer(character.Name, (uint)character.Id, GroupDirectory.Instance.GetGroupForConn(conn.ConnId)?.GroupId ?? 0);
                    playerObject.Id = character.Id;

                    SavedCharacter savedChar = CharacterRepository.GetCharacter(character.Id);
                    if (savedChar == null)
                    {
                        savedChar = CharacterRepository.CreateCharacter(character.Name, "Fighter", AccountRepository.GetAccountId(conn.LoginName), conn.LoginName);
                    }

                    var avatar = GCObjectFactory.LoadAvatar(savedChar);
                    avatar.Id = _nextEntityId++;

                    foreach (var child in avatar.Children)
                    {
                        child.Id = _nextEntityId++;
                        if (child.Children != null)
                        {
                            foreach (var grandchild in child.Children)
                            {
                                grandchild.Id = _nextEntityId++;
                            }
                        }
                    }

                    Debug.LogError($"[CHARLIST-IDS] Character: {character.Name} (slot {slotIndex})");
                    var equipmentComponent = avatar.Children.FirstOrDefault(c => c.DFCClass == "Equipment");
                    var manipulatorsComponent = avatar.Children.FirstOrDefault(c => c.DFCClass == "Manipulators");
                    if (equipmentComponent != null)
                    {
                        foreach (var equipmentChild in equipmentComponent.Children)
                        {
                            Debug.LogError($"[CHARLIST-IDS]   Equipment child: {equipmentChild.GCClass} ID={equipmentChild.Id}");
                        }
                    }
                    if (manipulatorsComponent != null)
                    {
                        foreach (var manipulatorChild in manipulatorsComponent.Children)
                        {
                            Debug.LogError($"[CHARLIST-IDS]   Manipulators child: {manipulatorChild.GCClass} ID={manipulatorChild.Id}");
                        }
                    }

                    playerObject.AddChild(avatar);

                    Debug.LogError($"[CHARLIST-DETAIL] slot={slotIndex} name={character.Name}");
                    Debug.LogError($"[CHARLIST-DETAIL] avatarChildren={avatar.Children.Count}");
                    foreach (var child in avatar.Children)
                    {
                        int grandchildCount = child.Children?.Count ?? 0;
                        Debug.LogError($"[CHARLIST-DETAIL] child={child.DFCClass} id={child.Id} children={grandchildCount}");
                    }

                    var procModifierObject = GCObjectFactory.NewProcModifier();
                    procModifierObject.Id = _nextEntityId++;
                    playerObject.AddChild(procModifierObject);

                    playerObject.WriteFullGCObject(writer);

                    byte[] currentData = writer.ToArray();
                    Debug.LogError($"[CHARLIST-BYTES] slot={slotIndex} name={character.Name} id={character.Id}");
                    Debug.LogError($"[CHARLIST-BYTES] len={currentData.Length}");
                    Debug.LogError("[CHARLIST-BYTES] first=50");
                    int dumpLen = Math.Min(50, currentData.Length);
                    Debug.LogError($"[CHARLIST-BYTES] {BitConverter.ToString(currentData, 0, dumpLen)}");

                    Debug.LogError($"[CHARLIST] Sending slot {slotIndex} as TYPE 2: {currentData.Length} bytes");
                    SendCompressedA(conn, 0x01, 0x0f, currentData);

                    slotIndex++;
                }

                _charListSent[conn.ConnId] = true;
                _nextEntityId = Math.Max(savedNextEntityId, _nextEntityId);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CHARLIST] state=failed message='{ex.Message}' stack='{ex.StackTrace}'");
            }
        }


        private bool TryFindZoneByName(string zoneName, out Zone zone)
        {
            zone = null;
            if (string.IsNullOrWhiteSpace(zoneName))
                return false;
            zone = _zones.Values.FirstOrDefault(z =>
                z != null && !string.IsNullOrEmpty(z.name) && z.name.Equals(zoneName.Trim(), StringComparison.OrdinalIgnoreCase));
            return zone != null;
        }

        private Zone ResolveCharacterPlayZone(RRConnection conn, uint charId, SavedCharacter savedChar, out string reason)
        {
            reason = "default";
            TryFindZoneByName("tutorial", out Zone tutorialZone);

            string savedZoneName = savedChar?.currentZoneName;
            if (!TryFindZoneByName(savedZoneName, out Zone savedZone))
            {
                reason = $"missing-saved-zone:{savedZoneName ?? ""}";
                return tutorialZone ?? _zones.Values.FirstOrDefault();
            }

            if (IsPvpZone(savedZone.name) && TryFindZoneByName("pvp_start", out Zone pvpHub))
            {
                reason = "saved-pvp-arena-redirect:pvp_start";
                conn.PendingSpawnPoint = "respawn";
                return pvpHub;
            }

            if (IsPublicZone(savedZone.name))
            {
                reason = "saved-public-zone";
                return savedZone;
            }

            if (GroupDirectory.Instance.GetGroupForConn(conn.ConnId) != null)
            {
                reason = "saved-group-dungeon";
                return savedZone;
            }

            if (HasRememberedSoloDungeonInstance(charId, savedZone.name, out uint rememberedInstance, out bool expired))
            {
                reason = $"saved-solo-dungeon-memory:{rememberedInstance:X8}";
                return savedZone;
            }

            if (expired)
                reason = "saved-solo-dungeon-expired";
            else
                reason = "saved-solo-dungeon-missing";

            if (!string.IsNullOrEmpty(savedZone.respawnZone) && TryFindZoneByName(savedZone.respawnZone, out Zone respawnZone))
            {
                reason += $":redirect:{respawnZone.name}";
                return respawnZone;
            }

            reason += ":no-respawn-zone";
            return savedZone ?? tutorialZone;
        }

        private void HandleCharacterPlay(RRConnection conn, byte[] data)
        {
            Debug.Log($"HandleCharacterPlay for {conn.LoginName}");
            try
            {
                if (!_persistentCharacters.TryGetValue(conn.LoginName, out var characters) || characters.Count == 0)
                {
                    Debug.LogError($"No characters found for {conn.LoginName}");
                    return;
                }

                GCObject character = null;
                if (data != null && data.Length >= 4)
                {
                    var reader = new LEReader(data);
                    uint selectedCharId = reader.ReadUInt32();
                    Debug.LogError($"[CHAR-PLAY] selectedId={selectedCharId}");
                    character = characters.Find(c => c.Id == selectedCharId);
                }
                if (character == null)
                    character = characters[0];

                _selectedCharacter[conn.LoginName] = character;
                _activeCharacter.Remove(conn.LoginName);
                conn.CharSqlId = character.Id;
                Debug.Log($"Selected character: {character.Name} (ID={character.Id})");

                try
                {
                    using (var dbConn = GameDatabase.GetConnection())
                        GameDatabase.ExecuteNonQuery(dbConn,
                            "UPDATE accounts SET current_character_id=@cid WHERE username=@u COLLATE NOCASE",
                            ("@cid", (int)character.Id), ("@u", conn.LoginName));
                }
                catch (System.Exception ex) { Debug.LogError($"[SAVE] set current_character_id failed for {conn.LoginName}: {ex.Message}"); }
                Debug.Log("Character selected for spawning - will create fresh entities during spawn");
                var ackWriter = new LEWriter();
                ackWriter.WriteByte(4);
                ackWriter.WriteByte(5);
                SendCompressedA(conn, 0x01, 0x0F, ackWriter.ToArray());
                Debug.Log("Sent 4/5 acknowledgment");
                var groupWriter = new LEWriter();
                groupWriter.WriteByte(9);
                groupWriter.WriteByte(0);
                SendCompressedA(conn, 0x01, 0x0F, groupWriter.ToArray());
                Debug.Log("Sent 9/0 group connected");

                {
                    var reconnectedGroup = GroupDirectory.Instance.ReconnectMember(conn.LoginName, conn.ConnId);
                    if (reconnectedGroup != null)
                    {
                        uint reconnCharId = GetCharSqlId(conn);
                        byte[] reconnectPacket = GroupPackets.BuildMemberReconnected(reconnectedGroup.GroupId, reconnCharId);
                        foreach (var reconnectedMember in reconnectedGroup.Members)
                        {
                            if (!reconnectedMember.IsOnline) continue;
                            if (reconnectedMember.ConnId == conn.ConnId) continue;
                            var reconnectedMemberConnection = FindConnectionById(reconnectedMember.ConnId);
                            if (reconnectedMemberConnection != null)
                                SendToClient(reconnectedMemberConnection, reconnectPacket);
                        }
                        foreach (var reconnectedMember in reconnectedGroup.Members)
                        {
                            var reconnectedMemberConnection = FindConnectionById(reconnectedMember.ConnId);
                            if (reconnectedMemberConnection != null) reconnectedMemberConnection.GroupConnectedSent = false;
                        }
                        conn.GroupConnectedSent = false;
                        SendGroupConnectedToAll(reconnectedGroup);
                        SendGroupHealthToAll(reconnectedGroup);
                        Debug.LogError($"[GROUP] Reconnected {conn.LoginName} to group {reconnectedGroup.GroupId}, sent 0x4A + full group state, reset all GroupConnectedSent.");
                    }
                }

                Debug.LogError("[ZONE-MSG] Building complete zone message (13/0)...");
                var zoneWriter = new LEWriter();
                zoneWriter.WriteByte(13);
                zoneWriter.WriteByte(0);


                var savedChar = CharacterRepository.GetCharacter(character.Id);
                var startZone = ResolveCharacterPlayZone(conn, character.Id, savedChar, out string zoneResolveReason);
                string zoneName = startZone?.name ?? "tutorial";
                zoneWriter.WriteCString(zoneName);
                uint zoneId = startZone?.id ?? 2781714545u;
                conn.CurrentZoneId = zoneId;
                conn.CurrentZoneName = zoneName;
                _monsterSpawnSentByConn.Remove(conn.ConnId);
                _encounterObjectSentByConn.Remove(conn.ConnId);
                GroupDirectory.Instance.UpdateMemberZone(conn.ConnId, zoneName);
                try { if (conn.CharSqlId != 0) PosseRuntime.Instance.NotifyMemberStateChange(conn.CharSqlId, this); }
                catch (Exception posseException) { Debug.LogError($"[POSSE] zone-notify failed: {posseException.Message}"); }
                Debug.LogError($"[ZONE-MSG] zoneId={zoneId} hex=0x{zoneId:X8}");
                conn.CurrentZoneGcType = ResolveZoneGcType(startZone);

                Debug.LogError($"[ZONE] CurrentZoneGcType set to: {conn.CurrentZoneGcType} reason={zoneResolveReason}");
                AssignInstanceId(conn);

                uint zoneSeed = ResolveZoneConnectSeed(conn, zoneName);
                zoneWriter.WriteUInt32(zoneSeed);
                Debug.LogError($"[ZONE-MSG] Sending seed: 0x{zoneSeed:X8} zone={zoneName}");
                zoneWriter.WriteByte(0x01);
                zoneWriter.WriteByte(0xFF);
                zoneWriter.WriteCString("");
                zoneWriter.WriteUInt32(0x00);

                byte[] zoneData = zoneWriter.ToArray();
                Debug.LogError($"[ZONE-MSG] Complete zone message: {zoneData.Length} bytes");
                Debug.LogError($"[ZONE-MSG] Hex: {BitConverter.ToString(zoneData)}");
                SendCompressedA(conn, 0x01, 0x0F, zoneData);
                Debug.Log("Sent 13/0 zone info (COMPLETE FORMAT)");
                Debug.Log("Waiting for client to send zone progression request...");

                SocialRuntime.Instance.PlayerOnline(conn.LoginName, character.Name, conn, SendSocialViaAuth);
                SocialRuntime.Instance.SendLoginSocialInit(conn, character.Name, SendSocialViaAuth);

                PosseRuntime.Instance.SendConnectionNotification(conn, true, SendSocialViaAuth);
                SendPosseStateForCharacter(conn, character.Id, SendSocialViaAuth);
                try { PosseRuntime.Instance.NotifyMemberStateChange(character.Id, this); }
                catch (Exception ex2) { Debug.LogError($"[POSSE] login-notify (HandleCharacterPlay) failed: {ex2.Message}"); }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CHARACTER-PLAY] state=failed message='{ex.Message}' stack='{ex.StackTrace}'");
            }
        }

        private void HandleEntityRequest(RRConnection conn, LEReader reader, byte[] data)
        {
            if (reader.Remaining < 3)
            {
                HandleClientRequestRespawn(conn);
                return;
            }

            ushort entityId = reader.ReadUInt16();
            byte requestType = reader.ReadByte();
            Debug.LogError($"[ENTITY-REQ] entity={entityId} type=0x{requestType:X2} remaining={reader.Remaining} from {conn.LoginName}");

            switch (requestType)
            {
                case 0x11:
                    HandleStatSpendRequest(conn, reader);
                    break;
                case 0x12:
                    HandleStatReturnRequest(conn, reader);
                    break;
                case 0x13:
                    HandleRespecRequest(conn);
                    break;
                default:
                    HandleClientRequestRespawn(conn);
                    break;
            }
        }

        private void HandleStatSpendRequest(RRConnection conn, LEReader reader)
        {
            if (reader.Remaining < 2) { Debug.LogError("[STAT-SPEND] Not enough data"); return; }

            byte statType = reader.ReadByte();
            byte numPoints = reader.ReadByte();
            Debug.LogError($"[STAT-SPEND] statType={statType} numPoints={numPoints} from {conn.LoginName}");

            if (statType > 3 || numPoints == 0) { Debug.LogError($"[STAT-SPEND] Invalid"); return; }

            if (!_selectedCharacter.TryGetValue(conn.LoginName, out var selChar)) return;
            var savedChar = CharacterRepository.GetCharacter(selChar.Id);
            if (savedChar == null) return;

            int totalAllocated = savedChar.statStrength + savedChar.statAgility
                               + savedChar.statEndurance + savedChar.statIntellect;
            int pointsPerLevel = StatPointsPerLevel;
            int totalAvailable = (SavedCharacterLevel.ResolveRuntimeLevel(savedChar) - 1) * pointsPerLevel;
            int remaining = totalAvailable - totalAllocated;

            if (numPoints > remaining) { Debug.LogError($"[STAT-SPEND] Not enough: want={numPoints} have={remaining}"); return; }

            switch (statType)
            {
                case 0: savedChar.statStrength += numPoints; break;
                case 1: savedChar.statAgility += numPoints; break;
                case 2: savedChar.statEndurance += numPoints; break;
                case 3: savedChar.statIntellect += numPoints; break;
            }

            CharacterRepository.SaveCharacter(savedChar);
            Debug.LogError($"[STAT-SPEND] STR={savedChar.statStrength} AGI={savedChar.statAgility} END={savedChar.statEndurance} INT={savedChar.statIntellect} pts={remaining - numPoints}");
            GetPlayerState(conn.ConnId.ToString()).ApplyAllocatedStats(savedChar.statStrength, savedChar.statAgility, savedChar.statEndurance, savedChar.statIntellect);
            SendHeroStatUpdate(conn, 0x11, numPoints, statType);
        }

        private void HandleStatReturnRequest(RRConnection conn, LEReader reader)
        {
            if (reader.Remaining < 2) { Debug.LogError("[STAT-RETURN] Not enough data"); return; }

            byte statType = reader.ReadByte();
            byte numPoints = reader.ReadByte();
            Debug.LogError($"[STAT-RETURN] statType={statType} numPoints={numPoints} from {conn.LoginName}");

            if (statType > 3 || numPoints == 0) return;
            if (!_selectedCharacter.TryGetValue(conn.LoginName, out var selChar)) return;
            var savedChar = CharacterRepository.GetCharacter(selChar.Id);
            if (savedChar == null) return;

            int currentVal = 0;
            switch (statType) { case 0: currentVal = savedChar.statStrength; break; case 1: currentVal = savedChar.statAgility; break; case 2: currentVal = savedChar.statEndurance; break; case 3: currentVal = savedChar.statIntellect; break; }
            if (numPoints > currentVal) return;

            switch (statType)
            {
                case 0: savedChar.statStrength -= numPoints; break;
                case 1: savedChar.statAgility -= numPoints; break;
                case 2: savedChar.statEndurance -= numPoints; break;
                case 3: savedChar.statIntellect -= numPoints; break;
            }

            CharacterRepository.SaveCharacter(savedChar);
            Debug.LogError($"[STAT-RETURN] STR={savedChar.statStrength} AGI={savedChar.statAgility} END={savedChar.statEndurance} INT={savedChar.statIntellect}");
            GetPlayerState(conn.ConnId.ToString()).ApplyAllocatedStats(savedChar.statStrength, savedChar.statAgility, savedChar.statEndurance, savedChar.statIntellect);
            SendHeroStatUpdate(conn, 0x12, numPoints, statType);
        }

        private List<(int level, int fx32Value)> _respecCostCurveFx;

        private static int GetRespecCooldownSeconds()
        {
            var knobs = DungeonRunners.Data.GCDatabase.Instance.GlobalKnobs;
            if (knobs == null || !knobs.HasProperty("ReSpecTime"))
                throw new InvalidDataException("GlobalKnobs.ReSpecTime not loaded");
            return Math.Max(0, knobs.GetInt("ReSpecTime", 0));
        }

        private int EvaluateReSpecCostCurveFx32(int targetLevel)
        {
            if (_respecCostCurveFx == null)
            {
                _respecCostCurveFx = new List<(int, int)>();
                var tables = DungeonRunners.Data.GCDatabase.Instance.GetNode("Tables");
                var respecTable = tables?.GetChild("ReSpecCost");
                if (respecTable != null)
                {
                    foreach (var entry in respecTable.AnonymousChildren)
                    {
                        int level = entry.GetInt("Level", 0);
                        float value = entry.GetFloat("Value", 0f);
                        if (level > 0)
                        {
                            int fx32 = (int)Math.Ceiling(value * 256.0);
                            _respecCostCurveFx.Add((level, fx32));
                        }
                    }
                    _respecCostCurveFx.Sort((a, b) => a.level.CompareTo(b.level));
                    Debug.LogError($"[RESPEC-CURVE] Loaded {_respecCostCurveFx.Count} entries from Tables.ReSpecCost");
                    foreach (var e in _respecCostCurveFx)
                        Debug.LogError($"[RESPEC-CURVE]   L{e.level} = fx32({e.fx32Value})");
                }
                else
                {
                    RuntimeEvidence.LogFallbackHit("respec-cost", "missing-ReSpecCost", "sourceFunction=blocked compatibility=none", 1);
                    throw new InvalidDataException("Tables.ReSpecCost not loaded");
                }
            }

            if (_respecCostCurveFx.Count == 0)
            {
                RuntimeEvidence.LogFallbackHit("respec-cost", "empty-ReSpecCost", "sourceFunction=blocked compatibility=none", 1);
                throw new InvalidDataException("Tables.ReSpecCost has no entries");
            }

            if (targetLevel <= _respecCostCurveFx[0].level)
                return _respecCostCurveFx[0].fx32Value;

            if (targetLevel >= _respecCostCurveFx[_respecCostCurveFx.Count - 1].level)
                return _respecCostCurveFx[_respecCostCurveFx.Count - 1].fx32Value;

            for (int curveIndex = 1; curveIndex < _respecCostCurveFx.Count; curveIndex++)
            {
                if (targetLevel <= _respecCostCurveFx[curveIndex].level)
                {
                    int lowerLevel = _respecCostCurveFx[curveIndex - 1].level;
                    int upperLevel = _respecCostCurveFx[curveIndex].level;
                    int lowerValue = _respecCostCurveFx[curveIndex - 1].fx32Value;
                    int upperValue = _respecCostCurveFx[curveIndex].fx32Value;

                    int interpolationFixed = ((targetLevel - lowerLevel) * 65536) / (upperLevel - lowerLevel);
                    int delta = upperValue - lowerValue;
                    int interpolatedDelta = (int)(((long)delta * interpolationFixed) >> 16);
                    return lowerValue + interpolatedDelta;
                }
            }

            return _respecCostCurveFx[_respecCostCurveFx.Count - 1].fx32Value;
        }

        private void HandleRespecRequest(RRConnection conn)
        {
            Debug.LogError($"[RESPEC] from {conn.LoginName}");

            if (!_selectedCharacter.TryGetValue(conn.LoginName, out var selChar)) return;
            var savedChar = CharacterRepository.GetCharacter(selChar.Id);
            if (savedChar == null) return;

            int nowUnix = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            int cooldownSeconds = GetRespecCooldownSeconds();
            int elapsed = nowUnix - savedChar.lastRespecTime;
            if (savedChar.lastRespecTime > 0 && elapsed < cooldownSeconds)
            {
                int remaining = cooldownSeconds - elapsed;
                int mins = remaining / 60;
                int secs = remaining % 60;
                SendSystemMessage(conn, $"Respec on cooldown. {mins}m {secs}s remaining.");
                Debug.LogError($"[RESPEC] REJECTED: cooldown {remaining}s remaining");
                return;
            }

                int curveFixed32 = EvaluateReSpecCostCurveFx32(SavedCharacterLevel.ResolveRuntimeLevel(savedChar));
            uint goldCost = (uint)(((long)curveFixed32 * 1000) >> 8);
            Debug.LogError($"[RESPEC] Level={savedChar.level} fx32={curveFixed32} -> cost={goldCost} gold");

            if (savedChar.gold < goldCost)
            {
                SendSystemMessage(conn, $"Not enough gold to respec. Need {goldCost}, have {savedChar.gold}.");
                Debug.LogError($"[RESPEC] REJECTED: gold {savedChar.gold} < cost {goldCost}");
                return;
            }

            savedChar.gold -= goldCost;
            savedChar.statStrength = 0;
            savedChar.statAgility = 0;
            savedChar.statEndurance = 0;
            savedChar.statIntellect = 0;
            savedChar.lastRespecTime = nowUnix;
            savedChar.respecCount++;

            CharacterRepository.SaveCharacter(savedChar);
            Debug.LogError($"[RESPEC] Stats reset, gold {savedChar.gold + goldCost} -> {savedChar.gold} (cost {goldCost}), respec #{savedChar.respecCount}");
            GetPlayerState(conn.ConnId.ToString()).ApplyAllocatedStats(savedChar.statStrength, savedChar.statAgility, savedChar.statEndurance, savedChar.statIntellect);

            uint avatarId = GetPlayerAvatarId(conn.LoginName);
            if (avatarId == 0) return;

            if (conn.UnitContainerId != 0)
            {
                var goldWriter = new LEWriter();
                goldWriter.WriteByte(0x07);
                goldWriter.WriteByte(0x35);
                goldWriter.WriteUInt16(conn.UnitContainerId);
                goldWriter.WriteByte(0x20);
                goldWriter.WriteInt32(-(int)goldCost);
                goldWriter.WriteByte(0x00);
                goldWriter.WriteUInt32(0x00000000);
                goldWriter.WriteByte(0x01);
                WritePlayerEntitySynch(conn, goldWriter);
                goldWriter.WriteByte(0x06);
                byte[] goldPacket = goldWriter.ToArray();
                SendCompressedA(conn, 0x01, 0x0F, goldPacket);
                Debug.LogError($"[RESPEC-GOLD] Sent RemoveCurrency {goldCost} (merchant format)");
            }

            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x03);
            writer.WriteUInt16((ushort)avatarId);
            writer.WriteByte(0x13);
            writer.WriteByte(0x02);
            writer.WriteUInt32(0xFFFF00);
            writer.WriteByte(0x06);
            SendCompressedA(conn, 0x01, 0x0F, writer.ToArray());

            Debug.LogError($"[RESPEC] Sent respec packet, avatar={avatarId}");
        }

        private void SendHeroStatUpdate(RRConnection conn, byte subType, byte numPoints, byte statType)
        {
            uint avatarId = GetPlayerAvatarId(conn.LoginName);
            if (avatarId == 0) return;

            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x03);
            writer.WriteUInt16((ushort)avatarId);
            writer.WriteByte(subType);
            writer.WriteByte(statType);
            writer.WriteByte(numPoints);
            WritePlayerEntitySynch(conn, writer);
            writer.WriteByte(0x06);

            SendCompressedA(conn, 0x01, 0x0F, writer.ToArray());
            Debug.LogError($"[STAT-UPDATE] Sent 0x03 sub=0x{subType:X2} pts={numPoints} stat={statType} avatar={avatarId}");
        }

        private void HandleClientRequestRespawn(RRConnection conn)
        {
            Debug.LogError($"[RESPAWN] HandleClientRequestRespawn for {conn.LoginName}");

            try
            {
                if (_playerStates.TryGetValue(conn.LoginName, out var playerState))
                {
                    playerState.RestoreToFull();
                    Debug.LogError($"[RESPAWN] Restored HP={playerState.CurrentHPWire} Mana={playerState.CurrentManaWire}");
                }

                conn.RespawnFullHPPending = true;

                string respawnZone = GetRespawnZone(conn.CurrentZoneGcType);
                var respawnPos = GetRespawnPosition(respawnZone);
                Debug.LogError($"[RESPAWN] {conn.CurrentZoneGcType} -> {respawnZone} at ({respawnPos.x}, {respawnPos.y}, {respawnPos.z})");

                if (BlingGnomeRuntime.Instance.HasGnome(conn.ConnId))
                    BlingGnomeRuntime.Instance.DespawnGnome(conn, (c, d, t, b) => SendCompressedA(c, d, t, b), playDeathAnim: false);
                ChangeZoneToPosition(conn, respawnZone, respawnPos.x, respawnPos.y, respawnPos.z);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RESPAWN] state=failed message='{ex.Message}' stack='{ex.StackTrace}'");
            }
        }
        private (float x, float y, float z) GetRespawnPosition(string zoneName)
        {
            var waypoints = DatabaseLoader.GetWaypointsForZone(zoneName);
            foreach (var waypoint in waypoints)
            {
                if (waypoint.name.Equals("Respawn", StringComparison.OrdinalIgnoreCase))
                    return (waypoint.posX, waypoint.posY, waypoint.posZ);
            }

            var zone = _zones.Values.FirstOrDefault(z =>
                z.name.Equals(zoneName, StringComparison.OrdinalIgnoreCase));
            if (zone != null)
                return (zone.spawnX, zone.spawnY, zone.spawnZ);

            var tutorial = _zones.Values.FirstOrDefault(z =>
                z.name.Equals("tutorial", StringComparison.OrdinalIgnoreCase));
            if (tutorial != null)
            {
                Debug.LogError($"[RESPAWN] zone='{zoneName}' missing authored spawn; using authored tutorial spawn ({tutorial.spawnX}, {tutorial.spawnY}, {tutorial.spawnZ})");
                return (tutorial.spawnX, tutorial.spawnY, tutorial.spawnZ);
            }

            Debug.LogError($"[RESPAWN] zone='{zoneName}' missing authored spawn and tutorial fallback zone");
            return (0f, 0f, 0f);
        }
        private string GetRespawnZone(string currentZoneGcType)
        {
            string requested = currentZoneGcType?.Trim();
            var zone = _zones.Values.FirstOrDefault(z =>
                !string.IsNullOrEmpty(requested) &&
                !string.IsNullOrEmpty(z.gcType) &&
                z.gcType.Equals(requested, StringComparison.OrdinalIgnoreCase));

            if (zone == null && !string.IsNullOrEmpty(requested))
            {
                zone = _zones.Values.FirstOrDefault(z =>
                    !string.IsNullOrEmpty(z.name) &&
                    (z.name.Equals(requested, StringComparison.OrdinalIgnoreCase) ||
                     ("world." + z.name).Equals(requested, StringComparison.OrdinalIgnoreCase)));
            }

            if (zone != null && !string.IsNullOrEmpty(zone.respawnZone))
                return zone.respawnZone;

            return "tutorial";
        }





        private static void WriteGCType(LEWriter writer, string typeName, bool preserveCase = false)
        {
            bool verbose = VerbosePacketLogging;
            if (verbose)
                Debug.LogError($"[WRITE-GC-TYPE] phase=start type='{typeName}' preserveCase={preserveCase}");
            int positionBefore = writer.Position;
            if (verbose)
                Debug.LogError($"[WRITE-GC-TYPE] positionBefore={positionBefore}");

            string safeTypeName = preserveCase ? typeName : typeName.ToLower();

            if (verbose)
                Debug.LogError($"[WRITE-GC-TYPE] safeType='{safeTypeName}' original='{typeName}'");
            byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(safeTypeName);
            if (verbose)
                Debug.LogError($"[WRITE-GC-TYPE] stringBytes={nameBytes.Length}");
            writer.WriteByte(0xFF);
            if (verbose)
                Debug.LogError($"[WRITE-GC-TYPE] write=0xFF position={positionBefore}");
            writer.WriteCString(safeTypeName);
            if (verbose)
                Debug.LogError($"[WRITE-GC-TYPE] cstring=complete");
            int positionAfter = writer.Position;
            int bytesWritten = positionAfter - positionBefore;
            int expectedBytes = 1 + nameBytes.Length + 1;
            if (verbose)
            {
                Debug.LogError($"[WRITE-GC-TYPE] positionAfter={positionAfter}");
                Debug.LogError($"[WRITE-GC-TYPE] bytesWritten={bytesWritten} expectedBytes={expectedBytes}");
                if (bytesWritten != expectedBytes)
                {
                    Debug.LogError($"[WRITE-GC-TYPE] state=mismatch expectedBytes={expectedBytes} bytesWritten={bytesWritten}");
                }
                else
                {
                    Debug.LogError($"[WRITE-GC-TYPE] state=matched");
                }
                string hex = BitConverter.ToString(nameBytes).Replace("-", " ");
                Debug.LogError($"[WRITE-GC-TYPE] hex=FF {hex} 00");
            }
        }

        private static void WriteGCTypeTag22(LEWriter writer, string typeName, bool preserveCase = false)
        {
            string safeTypeName = preserveCase ? typeName : typeName.ToLower();

            byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(safeTypeName);
            writer.WriteByte(0x16);
            writer.WriteUInt16((ushort)nameBytes.Length);
            writer.WriteBytes(nameBytes);
            if (VerbosePacketLogging)
                Debug.LogError($"[WRITE-GC-TYPE-TAG22] type='{safeTypeName}' bytes={3 + nameBytes.Length} opcode=0x16");
        }






        public ushort ResolveRemotePlayerEntityId(RRConnection viewerConn, RRConnection ownerConn)
        {
            if (string.IsNullOrEmpty(viewerConn?.LoginName) || string.IsNullOrEmpty(ownerConn?.LoginName)) return 0;
            if (_remotePlayerIds.TryGetValue(viewerConn.LoginName, out var playerMap) && playerMap.TryGetValue(ownerConn.LoginName, out ushort remotePlayerId) && remotePlayerId != 0)
                return remotePlayerId;
            if (_remoteAvatarIds.TryGetValue(viewerConn.LoginName, out var avatarMap) && avatarMap.TryGetValue(ownerConn.LoginName, out ushort remoteAvatarId))
                return remoteAvatarId;
            return 0;
        }

        private static string RemoteAvatarResourceKey(string viewerLogin, string sourceLogin)
            => $"{viewerLogin}\u001f{sourceLogin}";

        private static void ResolveRemoteAvatarWorldPosition(RRConnection playerConn, out int posX, out int posY, out int posZ, out int heading)
        {
            posX = (int)(playerConn.PlayerPosX * 256);
            posY = (int)(playerConn.PlayerPosY * 256);
            posZ = (int)(playerConn.PlayerPosZ * 256);
            heading = (int)(playerConn.PlayerHeading * 256);
            posX += 1280;
        }

        private static void WriteRemoteAvatarInitPayload(LEWriter writer, RRConnection playerConn, ushort remotePlayerId, uint hpWire, uint manaWire, SavedCharacter remoteChar, int posX, int posY, int posZ, int heading)
        {
            byte level = (byte)Math.Max(1, playerConn.PlayerLevel);

            writer.WriteUInt32(0x06);
            writer.WriteInt32(posX);
            writer.WriteInt32(posY);
            writer.WriteInt32(posZ);
            writer.WriteInt32(heading);
            writer.WriteByte(0x01);
            writer.WriteUInt16(0);

            writer.WriteByte(0x07);
            writer.WriteByte(level);
            writer.WriteUInt16(0);
            writer.WriteUInt16(0);
            writer.WriteUInt16(remotePlayerId);
            writer.WriteUInt32(hpWire);
            writer.WriteUInt32(manaWire);

            writer.WriteUInt32(0);
            writer.WriteUInt16((ushort)(remoteChar?.statStrength ?? 0));
            writer.WriteUInt16((ushort)(remoteChar?.statAgility ?? 0));
            writer.WriteUInt16((ushort)(remoteChar?.statEndurance ?? 0));
            writer.WriteUInt16((ushort)(remoteChar?.statIntellect ?? 0));
            writer.WriteUInt16(0);
            writer.WriteUInt16(0);
            writer.WriteUInt32(remoteChar != null ? (uint)remoteChar.pvpWins : 0);
            writer.WriteUInt32(remoteChar != null ? (uint)remoteChar.pvpRating : 0);
            writer.WriteByte(remoteChar?.face ?? 0);
            writer.WriteByte(remoteChar?.hair ?? 0);
            writer.WriteByte(remoteChar?.hairColor ?? 0);
        }

        private void ClearRemoteAvatarResourceState(string loginName)
        {
            if (string.IsNullOrEmpty(loginName)) return;
            string prefix = loginName + "\u001f";
            string suffix = "\u001f" + loginName;
            var keys = _remoteAvatarResourceState.Keys
                .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (string key in keys)
                _remoteAvatarResourceState.Remove(key);
        }

        private readonly Dictionary<string, uint> _remoteAvatarHPSent = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        private void SendRemoteAvatarHPUpdates(RRConnection sourceConn)
        {
            if (sourceConn == null || !sourceConn.IsSpawned || string.IsNullOrEmpty(sourceConn.LoginName)) return;
            PlayerState state = GetPlayerState(sourceConn.ConnId.ToString());
            if (state == null) return;
            uint hpWire = state.EntitySynchInfoHP;
            foreach (var viewerConn in _connections.Values)
            {
                if (viewerConn == sourceConn || !viewerConn.IsSpawned || !viewerConn.AllowFlush) continue;
                if (viewerConn.InstanceId != sourceConn.InstanceId) continue;
                if (!string.Equals(viewerConn.CurrentZoneGcType, sourceConn.CurrentZoneGcType, StringComparison.OrdinalIgnoreCase)) continue;
                if (!_remoteBehaviorIds.TryGetValue(viewerConn.LoginName, out var behaviorMap)) continue;
                if (!behaviorMap.TryGetValue(sourceConn.LoginName, out ushort remoteBehaviorId) || remoteBehaviorId == 0) continue;
                string key = RemoteAvatarResourceKey(viewerConn.LoginName, sourceConn.LoginName);
                if (_remoteAvatarHPSent.TryGetValue(key, out uint lastHp) && lastHp == hpWire) continue;
                var writer = new LEWriter();
                writer.WriteByte(0x35);
                writer.WriteUInt16(remoteBehaviorId);
                writer.WriteByte(0x65);
                writer.WriteByte(0xFF);
                writer.WriteByte(0x00);
                if (!TryWriteRemoteAvatarEntitySynchInfo(sourceConn, writer, remoteBehaviorId, 0x65, "MP-HP"))
                    continue;
                viewerConn.MessageQueue.Enqueue(writer.ToArray());
                _remoteAvatarHPSent[key] = hpWire;
            }
        }

        private byte[] BuildOtherPlayerSpawnPacket(RRConnection playerConn, uint avatarEntityId, RRConnection viewerConn)
        {
            if (!TryResolvePlayerEntitySynchInfoHP(playerConn, "MP-SPAWN", false, out uint remoteHPWire))
                return null;

            PlayerState remoteState = GetPlayerState(playerConn.ConnId.ToString());
            uint remoteManaWire = remoteState != null ? Math.Min(remoteState.CurrentManaWire, remoteState.MaxManaWire) : 0;
            var writer = new LEWriter();
            ResolveRemoteAvatarWorldPosition(playerConn, out int posX, out int posY, out int posZ, out int heading);

            ushort remoteBehaviorId = (ushort)_nextEntityId++;
            ushort remoteSkillsId = (ushort)_nextEntityId++;
            ushort remoteManipId = (ushort)_nextEntityId++;
            ushort remoteModId = (ushort)_nextEntityId++;
            ushort remotePlayerId = (ushort)_nextEntityId++;

            if (!_remoteBehaviorIds.ContainsKey(viewerConn.LoginName))
                _remoteBehaviorIds[viewerConn.LoginName] = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
            _remoteBehaviorIds[viewerConn.LoginName][playerConn.LoginName] = remoteBehaviorId;

            if (!_remoteAvatarIds.ContainsKey(viewerConn.LoginName))
                _remoteAvatarIds[viewerConn.LoginName] = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
            _remoteAvatarIds[viewerConn.LoginName][playerConn.LoginName] = (ushort)avatarEntityId;

            if (!_remotePlayerIds.ContainsKey(viewerConn.LoginName))
                _remotePlayerIds[viewerConn.LoginName] = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
            _remotePlayerIds[viewerConn.LoginName][playerConn.LoginName] = remotePlayerId;

            SavedCharacter remoteChar = GetActiveCharacter(playerConn);

            writer.WriteByte(0x07);

            writer.WriteByte(0x01);
            writer.WriteUInt16((ushort)avatarEntityId);
            WriteGCType(writer, playerConn.AvatarGcType, preserveCase: true);

            writer.WriteByte(0x01);
            writer.WriteUInt16(remotePlayerId);
            WriteGCType(writer, "player");

            writer.WriteByte(0x02);
            writer.WriteUInt16(remotePlayerId);
            writer.WriteCString(remoteChar?.name ?? playerConn.LoginName);
            writer.WriteUInt32(0x00);
            writer.WriteUInt32(GroupDirectory.Instance.GetGroupForConn(playerConn.ConnId)?.GroupId ?? 0);
            byte remoteMembership;
            if (IsPlayerAdmin(playerConn.LoginName))
                remoteMembership = 0x00;
            else if (IsPlayerFree(playerConn.LoginName))
                remoteMembership = 0x02;
            else
                remoteMembership = 0x01;
            writer.WriteByte(remoteMembership);
            writer.WriteUInt32(GetCharSqlId(playerConn));
            writer.WriteUInt32(remoteChar != null ? (uint)remoteChar.pvpWins : 0);
            writer.WriteUInt32(remoteChar != null ? (uint)remoteChar.pvpRating : 0);
            var remotePvpMatch = Gameplay.PVPMatchmaking.Instance.GetMatchForPlayer(playerConn.LoginName);
            if (remotePvpMatch != null)
            {
                string remoteTeamPath = Gameplay.PVPMatchmaking.TeamGcPath(remotePvpMatch.Archetype, remotePvpMatch.IsRed(playerConn.LoginName));
                writer.WriteByte(0xFF); writer.WriteCString(remoteTeamPath);
            }
            else
            {
                writer.WriteByte(0x00);
            }
            writer.WriteCString(remoteChar?.posseName ?? "");
            writer.WriteUInt32(0x00);

            writer.WriteByte(0x32);
            writer.WriteUInt16((ushort)avatarEntityId);
            writer.WriteUInt16(remoteSkillsId);
            WriteGCType(writer, "skills", preserveCase: false);
            writer.WriteByte(0x01);
            writer.WriteByte(0xFF); writer.WriteByte(0xFF);
            writer.WriteByte(0xFF); writer.WriteByte(0xFF);
            writer.WriteByte(0x00);
            writer.WriteByte(0x01);
            string profession = (playerConn.ClassName?.ToLower()) switch
            {
                "mage" or "warlock" => "skills.professions.Warlock",
                "ranger" => "skills.professions.Ranger",
                _ => "skills.professions.Warrior"
            };
            WriteGCType(writer, profession, preserveCase: true);

            writer.WriteByte(0x32);
            writer.WriteUInt16((ushort)avatarEntityId);
            writer.WriteUInt16(remoteManipId);
            WriteGCType(writer, "manipulators", preserveCase: false);
            writer.WriteByte(0x01);

            var skillItems = new List<GCObject>();
            var equipmentItems = new List<GCObject>();
            string playerConnectionKey = playerConn.ConnId.ToString();
            Dictionary<uint, string> playerManipulatorMap = null;
            if (_playerManipMap.ContainsKey(playerConnectionKey))
                playerManipulatorMap = _playerManipMap[playerConnectionKey];

            if (playerConn.Avatar != null)
            {
                var manipulatorsComponent = playerConn.Avatar.Children?.FirstOrDefault(c =>
                    c.DFCClass == "Manipulators" || c.GCClass == "Manipulators");
                if (manipulatorsComponent?.Children != null)
                {
                    foreach (var child in manipulatorsComponent.Children)
                    {
                        bool isEquipment = (child.DFCClass == "Armor" || child.DFCClass == "Item" ||
                                       child.DFCClass == "MeleeWeapon" || child.DFCClass == "RangedWeapon");
                        if (isEquipment && !string.IsNullOrEmpty(child.GCClass))
                        {
                            var itemData = DatabaseLoader.FindItem(child.GCClass);
                            var generalItem = itemData == null ? DatabaseLoader.FindGeneralItem(child.GCClass) : null;
                            if (itemData != null || generalItem != null)
                            {
                                equipmentItems.Add(child);
                            }
                            else
                            {
                                string merchantLookupKey = child.GCClass.ToLowerInvariant();
                                if (merchantLookupKey.StartsWith("items.pal.")) merchantLookupKey = merchantLookupKey.Substring(10);
                                if (DungeonRunners.Gameplay.MerchantRuntime.HasAuthoredMerchantModSlots(merchantLookupKey))
                                    equipmentItems.Add(child);
                            }
                        }
                        else if (!isEquipment && !string.IsNullOrEmpty(child.GCClass) &&
                                 !child.GCClass.StartsWith("skills.professions.", StringComparison.OrdinalIgnoreCase))
                        {
                            skillItems.Add(child);
                        }
                    }
                }
            }

            writer.WriteByte((byte)(skillItems.Count + equipmentItems.Count));

            uint remoteSlotCounter = 200;
            foreach (var skill in skillItems)
            {
                uint slotId = remoteSlotCounter++;
                if (playerManipulatorMap != null)
                {
                    foreach (var manipulatorMapEntry in playerManipulatorMap)
                    {
                        if (string.Equals(manipulatorMapEntry.Value, skill.GCClass, StringComparison.OrdinalIgnoreCase))
                        {
                            slotId = manipulatorMapEntry.Key;
                            break;
                        }
                    }
                }
                writer.WriteByte(0xFF);
                writer.WriteCString(skill.GCClass.ToLower());
                writer.WriteUInt32(slotId);
                byte remoteSkillLevel = (byte)(remoteChar?.GetSkillLevel(skill.GCClass) ?? 1);
                writer.WriteByte(remoteSkillLevel);
                writer.WriteByte(0x00);
            }

            foreach (var item in equipmentItems)
            {
                item.WriteInit(writer, Math.Max(1, playerConn.PlayerLevel));
            }
            Debug.LogError($"[MULTIPLAYER-MANIP] {skillItems.Count} skills + {equipmentItems.Count} equipment for {playerConn.LoginName}");

            writer.WriteByte(0x32);
            writer.WriteUInt16((ushort)avatarEntityId);
            writer.WriteUInt16(remoteModId);
            WriteGCType(writer, "modifiers", preserveCase: false);
            writer.WriteByte(0x01);
            var remotePassives = remoteChar != null && !IsPvpZone(playerConn.CurrentZoneName)
                ? CollectPassiveManipulators(remoteChar).ToList()
                : null;
            WritePassiveModifiersComponent(writer, remotePassives, (ushort)avatarEntityId);

            writer.WriteByte(0x32);
            writer.WriteUInt16((ushort)avatarEntityId);
            writer.WriteUInt16(remoteBehaviorId);
            WriteGCType(writer, "avatar.base.UnitBehavior");
            writer.WriteByte(0x01);

            writer.WriteByte(0xFF);
            writer.WriteByte(0x00);
            writer.WriteByte(0x00);

            writer.WriteByte(0xFF);
            writer.WriteByte(0x00);
            writer.WriteUInt32(0x00000000);
            writer.WriteUInt32(0x00000000);

            writer.WriteByte(0x00);

            writer.WriteByte(0xFF);
            writer.WriteByte(0x00);
            writer.WriteByte(0x00);

            writer.WriteByte(0x02);
            writer.WriteUInt16((ushort)avatarEntityId);
            WriteRemoteAvatarInitPayload(writer, playerConn, remotePlayerId, remoteHPWire, remoteManaWire, remoteChar, posX, posY, posZ, heading);
            _remoteAvatarResourceState[RemoteAvatarResourceKey(viewerConn.LoginName, playerConn.LoginName)] = ((ushort)avatarEntityId, remoteHPWire, remoteManaWire);

            writer.WriteByte(0x35);
            writer.WriteUInt16(remoteBehaviorId);
            writer.WriteByte(0x04);
            writer.WriteByte(0x11);
            writer.WriteByte(0x00);
            writer.WriteInt32(posX);
            writer.WriteInt32(posY);
            writer.WriteInt32(posZ);
            GetValidationCutoff(out uint spawnCutoffTick, out float spawnCutoffTime);
            if (!TryWriteResolvedEntitySynchInfo(writer, remoteBehaviorId, 0x04, EntitySynchInfoContext.PlayerActionResponse, "MP-SPAWN-WARP", EntitySynchInfoDecision.HP(EntitySynchInfoOwner.Avatar, remoteHPWire, $"MP-SPAWN-WARP source={playerConn.LoginName}", remoteBehaviorId, remoteBehaviorId, 0x04, GetCombatNow(), $"remote-spawn-warp; validationCutoffTick={spawnCutoffTick} validationCutoffTime={spawnCutoffTime:F3}", spawnCutoffTick, spawnCutoffTime)))
                return null;

            writer.WriteByte(0x06);

            Debug.LogError($"[MULTIPLAYER] Built avatar spawn for {playerConn.LoginName}: {writer.Position} bytes (avatar.base.UnitBehavior)");
            return writer.ToArray();
        }

        private void BroadcastEntityRemove(RRConnection leavingConn, string zoneGcType)
        {
            Debug.LogError($"[MULTIPLAYER] Player {leavingConn.LoginName} leaving zone {zoneGcType}");

            foreach (var connectionEntry in _connections)
            {
                var other = connectionEntry.Value;
                if (other == leavingConn) continue;
                if (!other.IsSpawned) continue;
                if (_remoteAvatarIds.TryGetValue(other.LoginName, out var avatarMap) &&
                    avatarMap.TryGetValue(leavingConn.LoginName, out ushort avatarEntityId))
                {
                    var writer = new LEWriter();
                    writer.WriteByte(0x07);
                    writer.WriteByte(0x05);
                    writer.WriteUInt16(avatarEntityId);
                    if (_remotePlayerIds.TryGetValue(other.LoginName, out var playerIdMap) &&
                        playerIdMap.TryGetValue(leavingConn.LoginName, out ushort playerEntityId))
                    {
                        writer.WriteByte(0x05);
                        writer.WriteUInt16(playerEntityId);
                        playerIdMap.Remove(leavingConn.LoginName);
                    }
                    writer.WriteByte(0x06);
                    SendToClient(other, writer.ToArray());
                    Debug.LogError($"[MULTIPLAYER] Sent entity despawn (0x05) for avatar {avatarEntityId} to {other.LoginName}");
                    _remoteAvatarResourceState.Remove(RemoteAvatarResourceKey(other.LoginName, leavingConn.LoginName));
                    avatarMap.Remove(leavingConn.LoginName);
                }

                if (_remoteBehaviorIds.TryGetValue(other.LoginName, out var behaviorMap))
                    behaviorMap.Remove(leavingConn.LoginName);
            }

            if (leavingConn.IsConnected && _remoteAvatarIds.TryGetValue(leavingConn.LoginName, out var leaverViewMap) && leaverViewMap.Count > 0)
            {
                _remotePlayerIds.TryGetValue(leavingConn.LoginName, out var leaverPlayerMap);
                foreach (var viewedEntry in leaverViewMap)
                {
                    var writer = new LEWriter();
                    writer.WriteByte(0x07);
                    writer.WriteByte(0x05);
                    writer.WriteUInt16(viewedEntry.Value);
                    if (leaverPlayerMap != null && leaverPlayerMap.TryGetValue(viewedEntry.Key, out ushort viewedPlayerId))
                    {
                        writer.WriteByte(0x05);
                        writer.WriteUInt16(viewedPlayerId);
                    }
                    writer.WriteByte(0x06);
                    SendToClient(leavingConn, writer.ToArray());
                    Debug.LogError($"[MULTIPLAYER] Sent entity despawn (0x05) for avatar {viewedEntry.Value} to leaver {leavingConn.LoginName} (owner {viewedEntry.Key})");
                    _remoteAvatarResourceState.Remove(RemoteAvatarResourceKey(leavingConn.LoginName, viewedEntry.Key));
                }
            }

            _remoteBehaviorIds.Remove(leavingConn.LoginName);
            _remoteAvatarIds.Remove(leavingConn.LoginName);
            _remotePlayerIds.Remove(leavingConn.LoginName);
            ClearRemoteAvatarResourceState(leavingConn.LoginName);
            _pvpControllerReady.Remove(leavingConn.LoginName);
            _pvpControllerIdByLogin.Remove(leavingConn.LoginName);
            _pvpGateDropDue.Remove(leavingConn.LoginName);
            _pvpCombatReminders.Remove(leavingConn.LoginName);

            var keysToRemove = new List<string>();
            foreach (var key in _remoteSessionIds.Keys)
                if (key.Contains(leavingConn.LoginName))
                    keysToRemove.Add(key);
            foreach (var key in keysToRemove)
                _remoteSessionIds.Remove(key);
            leavingConn.HasLastRelayPos = false;
            leavingConn.LastRelayPosX = 0f;
            leavingConn.LastRelayPosY = 0f;
            leavingConn.LastRawMoveData = null;
            leavingConn.LastRawMoveCount = 0;
        }

        private Dictionary<string, byte> _remoteSessionIds = new Dictionary<string, byte>();

        private string ResolveCharacterName(RRConnection conn)
        {
            string name = conn?.LoginName ?? "Unknown";
            if (conn?.LoginName != null && _selectedCharacter.TryGetValue(conn.LoginName, out var ch))
                name = ch?.Name ?? name;
            return name;
        }

        private static byte[] BuildChatMessagePacket(byte dispChannel, string senderName, string message)
        {
            var writer = new LEWriter();
            writer.WriteByte(0x06);
            writer.WriteByte(0x00);
            writer.WriteByte(dispChannel);
            if (dispChannel == 0x02 || dispChannel == 0x03 || dispChannel == 0x04 || dispChannel == 0x05
                || dispChannel == 0x0B || dispChannel == 0x0C || dispChannel == 0x10)
                writer.WriteByte(0x00);
            if (dispChannel != 0x0D)
                writer.WriteCString(senderName ?? "");
            writer.WriteCString(message ?? "");
            return writer.ToArray();
        }

        private void SendChatMessage(RRConnection conn, byte dispChannel, string senderName, string message)
        {
            SendCompressedA(conn, 0x01, 0x0F, BuildChatMessagePacket(dispChannel, senderName, message));
        }

        private void BroadcastChatMessage(byte dispChannel, string senderName, string message, string zoneName)
        {
            byte[] chatPacket = BuildChatMessagePacket(dispChannel, senderName, message);
            foreach (var other in _connections.Values)
            {
                if (!other.IsSpawned) continue;
                if (zoneName != null && !string.Equals(other.CurrentZoneName, zoneName, StringComparison.OrdinalIgnoreCase)) continue;
                SendCompressedA(other, 0x01, 0x0F, chatPacket);
            }
        }

        private RRConnection FindConnectionByCharacterName(string characterName)
        {
            if (string.IsNullOrWhiteSpace(characterName)) return null;
            foreach (var other in _connections.Values)
            {
                if (string.IsNullOrEmpty(other?.LoginName)) continue;
                if (_selectedCharacter.TryGetValue(other.LoginName, out var ch) && ch?.Name != null
                    && string.Equals(ch.Name, characterName, StringComparison.OrdinalIgnoreCase))
                    return other;
            }
            return null;
        }

        private void HandleChatMessage(RRConnection conn, byte chatChannel, string text)
        {
            string senderName = ResolveCharacterName(conn);
            switch (chatChannel)
            {
                case 1:
                    BroadcastChatMessage(0x02, senderName, text, null);
                    break;
                case 2:
                    BroadcastChatMessage(0x03, senderName, text, conn.CurrentZoneName);
                    break;
                case 3:
                {
                    var group = GroupDirectory.Instance.GetGroupForConn(conn.ConnId);
                    if (group == null)
                    {
                        SendChatMessage(conn, 0x0A, senderName, text);
                        break;
                    }
                    foreach (var member in group.Members)
                    {
                        if (!member.IsOnline) continue;
                        if (_connections.TryGetValue(member.ConnId, out var memberConn) && memberConn.IsSpawned)
                            SendChatMessage(memberConn, 0x04, senderName, text);
                    }
                    break;
                }
                case 4:
                {
                    int split = text.IndexOf(' ');
                    string targetName = split > 0 ? text.Substring(0, split) : text;
                    string message = split > 0 ? text.Substring(split + 1).Trim() : "";
                    RRConnection target = FindConnectionByCharacterName(targetName);
                    if (target == null || !target.IsSpawned || message.Length == 0)
                    {
                        SendChatMessage(conn, 0x0A, targetName, message.Length == 0 ? text : message);
                        break;
                    }
                    SendChatMessage(target, 0x05, senderName, message);
                    SendChatMessage(conn, 0x06, ResolveCharacterName(target), message);
                    break;
                }
                case 5:
                    BroadcastChatMessage(0x0B, senderName, text, null);
                    break;
                case 6:
                    BroadcastChatMessage(0x0C, senderName, text, null);
                    break;
                case 7:
                    BroadcastChatMessage(0x10, senderName, text, null);
                    break;
                default:
                    Debug.LogError($"[CHAT-CH6] unhandled chatChannel={chatChannel} sender={senderName} sourceFunction=ChatClient::sendChatMessage@0x5FFCA0");
                    break;
            }
        }

        private void BroadcastPlayerDeath(RRConnection deadConn)
        {
            Debug.LogError($"[MP-DEATH] from {deadConn.LoginName} zone='{deadConn.CurrentZoneGcType}'");
            foreach (var other in _connections.Values)
            {
                if (other == deadConn) continue;
                if (!other.IsSpawned) continue;
                if (other.CurrentZoneGcType != deadConn.CurrentZoneGcType) continue;
                if (other.InstanceId != deadConn.InstanceId) continue;

                if (!_remoteBehaviorIds.TryGetValue(other.LoginName, out var behaviorMap)) continue;
                if (!behaviorMap.TryGetValue(deadConn.LoginName, out ushort remoteBehaviorId)) continue;

                var remoteDeathMessage = new LEWriter();
                remoteDeathMessage.WriteByte(0x07);
                remoteDeathMessage.WriteByte(0x36);
                remoteDeathMessage.WriteUInt16(remoteBehaviorId);
                GetValidationCutoff(out uint deathCutoffTick, out float deathCutoffTime);
                if (!TryWriteResolvedEntitySynchInfo(remoteDeathMessage, remoteBehaviorId, 0x00, EntitySynchInfoContext.PlayerActionResponse, "MP-DEATH", EntitySynchInfoDecision.HP(EntitySynchInfoOwner.Avatar, 0, $"MP-DEATH source={deadConn.LoginName}", remoteBehaviorId, remoteBehaviorId, 0x00, GetCombatNow(), $"remote-death; validationCutoffTick={deathCutoffTick} validationCutoffTime={deathCutoffTime:F3}", deathCutoffTick, deathCutoffTime)))
                    continue;
                remoteDeathMessage.WriteByte(0x06);
                SendToClient(other, remoteDeathMessage.ToArray());
                Debug.LogError($"[MULTIPLAYER-DEATH] Sent EntitySynchInfo HP=0 to {other.LoginName} for {deadConn.LoginName}");
            }
        }

        private bool TryPrepareProceduralDungeonSnapshot(RRConnection conn, out DungeonMazeSpawner.ProceduralDungeonSnapshot snapshot)
        {
            snapshot = null;
            if (conn == null || string.IsNullOrEmpty(conn.CurrentZoneName))
                return false;
            if (!DungeonMazeSpawner.IsProceduralZone(conn.CurrentZoneName))
                return false;

            string zoneName = conn.CurrentZoneName;
            string instanceKey = GetInstanceZoneKey(conn);
            uint layoutSeed = ResolveZoneLayoutSeed(conn, zoneName);
            uint roomSeed = ResolveRuntimeZoneSeed(conn, zoneName);
            snapshot = ZoneSpawner.Instance.GetOrCreateProceduralSnapshot(zoneName, instanceKey, layoutSeed, roomSeed);
            return snapshot != null;
        }

        private bool TryGetProceduralDungeonSnapshot(RRConnection conn, out DungeonMazeSpawner.ProceduralDungeonSnapshot snapshot)
        {
            snapshot = null;
            if (conn == null || string.IsNullOrEmpty(conn.CurrentZoneName))
                return false;
            if (!DungeonMazeSpawner.IsProceduralZone(conn.CurrentZoneName))
                return false;

            string instanceKey = GetInstanceZoneKey(conn);
            if (ZoneSpawner.Instance.TryGetProceduralSnapshot(instanceKey, out snapshot))
                return true;

            return TryPrepareProceduralDungeonSnapshot(conn, out snapshot);
        }

        private (float x, float y, float z) ResolvePlayerSpawnPosition(RRConnection conn)
        {
            float spawnX, spawnY, spawnZ;
            bool proceduralSnapshotSpawn = false;

            if (TryPrepareProceduralDungeonSnapshot(conn, out var dungeonSnapshot))
            {
                proceduralSnapshotSpawn = true;
                string pendingSpawnPoint = conn.PendingSpawnPoint ?? "";
                if (DungeonMazeSpawner.TryResolveSpawnPoint(
                    dungeonSnapshot,
                    pendingSpawnPoint,
                    out var resolvedSpawn,
                    out float resolvedHeading,
                    out int resolvedSourceIndex,
                    out string resolvedTileType,
                    out int resolvedGridX,
                    out int resolvedGridY,
                    out var resolvedLocal,
                    out string resolvedSource))
                {
                    spawnX = resolvedSpawn.x;
                    spawnY = resolvedSpawn.y;
                    spawnZ = resolvedSpawn.z;
                    conn.PlayerHeading = resolvedHeading;
                    Debug.LogError($"[SPAWN] Procedural snapshot named spawnPoint='{pendingSpawnPoint}' zone={dungeonSnapshot.ZoneName} layoutSeed=0x{dungeonSnapshot.LayoutSeed:X8} roomSeed=0x{dungeonSnapshot.RoomSeed:X8} player=({spawnX:F1}, {spawnY:F1}, {spawnZ:F1}) heading={conn.PlayerHeading:F1} src={resolvedSourceIndex} tile='{resolvedTileType}' grid=({resolvedGridX},{resolvedGridY}) local=({resolvedLocal.x:F1},{resolvedLocal.y:F1},{resolvedLocal.z:F1}) source='{resolvedSource}'");
                }
                else
                {
                    spawnX = dungeonSnapshot.PlayerSpawn.x;
                    spawnY = dungeonSnapshot.PlayerSpawn.y;
                    spawnZ = dungeonSnapshot.PlayerSpawn.z;
                    conn.PlayerHeading = dungeonSnapshot.PlayerHeading;
                    Debug.LogError($"[SPAWN] Procedural snapshot default entry spawnPoint='{pendingSpawnPoint}' zone={dungeonSnapshot.ZoneName} layoutSeed=0x{dungeonSnapshot.LayoutSeed:X8} roomSeed=0x{dungeonSnapshot.RoomSeed:X8} player=({spawnX:F1}, {spawnY:F1}, {spawnZ:F1}) heading={conn.PlayerHeading:F1} entry=({dungeonSnapshot.EntryGridX},{dungeonSnapshot.EntryGridY}) src={dungeonSnapshot.EntrySourceIndex} tile='{dungeonSnapshot.EntryTileType}' playerLocal=({dungeonSnapshot.PlayerAnchorLocal.x:F1},{dungeonSnapshot.PlayerAnchorLocal.y:F1},{dungeonSnapshot.PlayerAnchorLocal.z:F1}) entryPortal=({dungeonSnapshot.EntryPortalSpawn.x:F1},{dungeonSnapshot.EntryPortalSpawn.y:F1},{dungeonSnapshot.EntryPortalSpawn.z:F1}) entryPortalLocal=({dungeonSnapshot.EntryPortalAnchorLocal.x:F1},{dungeonSnapshot.EntryPortalAnchorLocal.y:F1},{dungeonSnapshot.EntryPortalAnchorLocal.z:F1}) walkable={dungeonSnapshot.PlayerAnchorWalkable}/{dungeonSnapshot.EntryPortalAnchorWalkable} yTransform=worldGridY=gridY/BuildWorld source='{dungeonSnapshot.PlayerAnchorSource}'");
                }
                conn.PendingSpawnX = 0;
                conn.PendingSpawnY = 0;
                conn.PendingSpawnZ = 0;
                conn.PendingSpawnPoint = "";
            }
            else if (conn.PendingSpawnX != 0 || conn.PendingSpawnY != 0 || conn.PendingSpawnZ != 0)
            {
                spawnX = conn.PendingSpawnX;
                spawnY = conn.PendingSpawnY;
                spawnZ = conn.PendingSpawnZ;
                conn.PendingSpawnX = 0;
                conn.PendingSpawnY = 0;
                conn.PendingSpawnZ = 0;
                Debug.LogError($"[SPAWN] Using CHECKPOINT position: ({spawnX}, {spawnY}, {spawnZ})");
            }
            else if (!string.IsNullOrEmpty(conn.PendingSpawnPoint)
                     && DatabaseLoader.GetWaypointsForZone(conn.CurrentZoneName ?? "")
                            .FirstOrDefault(w => w.name != null && w.name.Equals(conn.PendingSpawnPoint, StringComparison.OrdinalIgnoreCase)) is { } namedWp)
            {
                spawnX = (float)namedWp.posX;
                spawnY = (float)namedWp.posY;
                spawnZ = (float)namedWp.posZ;
                conn.PlayerHeading = (float)namedWp.heading;
                Debug.LogError($"[SPAWN] Named waypoint '{namedWp.name}' in {conn.CurrentZoneName}: ({spawnX}, {spawnY}, {spawnZ}) h={conn.PlayerHeading}");
                conn.PendingSpawnPoint = "";
            }
            else if (_zones.TryGetValue(conn.CurrentZoneId, out Zone currentZone) && (currentZone.spawnX != 0 || currentZone.spawnY != 0))
            {
                spawnX = currentZone.spawnX;
                spawnY = currentZone.spawnY;
                spawnZ = currentZone.spawnZ;
                Debug.LogError($"[SPAWN] Zone '{currentZone.name}' spawn: ({spawnX}, {spawnY}, {spawnZ})");
            }
            else
            {
                string fallbackZoneName = conn.CurrentZoneName ?? "";
                var waypoints = DatabaseLoader.GetWaypointsForZone(fallbackZoneName);
                var waypoint = waypoints.FirstOrDefault(waypointData => waypointData.name.Equals("Start", StringComparison.OrdinalIgnoreCase));
                if (waypoint != null)
                {
                    spawnX = (float)waypoint.posX;
                    spawnY = (float)waypoint.posY;
                    spawnZ = (float)waypoint.posZ;
                    Debug.LogError($"[SPAWN] Using waypoint '{waypoint.name}' fallback: ({spawnX}, {spawnY}, {spawnZ})");
                }
                else
                {
                    spawnX = 415f;
                    spawnY = -180f;
                    spawnZ = 50f;
                    Debug.LogError($"[SPAWN]  Zone {conn.CurrentZoneId} has no spawn data, using town fallback");
                }
            }

            string pmZone = conn.CurrentZoneName ?? "";
            var pathMap = DungeonRunners.Core.PathMapCatalog.Instance.GetPathMap(pmZone);
            if (pathMap != null && !proceduralSnapshotSpawn)
            {
                if (pathMap.IsWalkable(spawnX, spawnY))
                {
                    float terrainZ = pathMap.GetHeightAt(spawnX, spawnY, spawnZ);
                    Debug.LogError($"[SPAWN] PathMap height correction: Z {spawnZ:F1} -> {terrainZ:F1}");
                    spawnZ = terrainZ;
                }
                else
                {
                    Debug.LogError($"[SPAWN]  PathMap says ({spawnX:F1}, {spawnY:F1}) is NOT walkable - using spawn Z as-is");
                }
            }
            else if (proceduralSnapshotSpawn)
            {
                Debug.LogError($"[SPAWN] Procedural snapshot raw anchor kept: no PathMap height correction pos=({spawnX:F1},{spawnY:F1},{spawnZ:F1})");
            }

            int playersInZone = 0;
            foreach (var other in _connections.Values)
            {
                if (other == conn) continue;
                if (!other.IsSpawned) continue;
                if (other.CurrentZoneGcType == conn.CurrentZoneGcType && other.InstanceId == conn.InstanceId)
                    playersInZone++;
            }
            if (playersInZone > 0 && !proceduralSnapshotSpawn)
            {
                float angle = playersInZone * 1.2566f;
                spawnX += (float)Math.Cos(angle) * 5f;
                spawnY += (float)Math.Sin(angle) * 5f;
                Debug.LogError($"[SPAWN] Offset for player #{playersInZone + 1}: ({spawnX:F1}, {spawnY:F1})");
            }
            else if (playersInZone > 0)
            {
                Debug.LogError($"[SPAWN] Procedural snapshot keeps authored spawn with {playersInZone} existing players in instance");
            }

            return (spawnX, spawnY, spawnZ);
        }

        private void SendRemoteClientControl(RRConnection viewer, RRConnection source)
        {
            if (viewer == null || source == null || string.IsNullOrEmpty(viewer.LoginName)) return;
            if (!_remoteBehaviorIds.TryGetValue(viewer.LoginName, out var behaviorMap)
                || !behaviorMap.TryGetValue(source.LoginName, out ushort remoteBehaviorId)
                || remoteBehaviorId == 0) return;
            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x35);
            writer.WriteUInt16(remoteBehaviorId);
            writer.WriteByte(0x64);
            writer.WriteByte(0x01);
            if (!TryWriteRemoteAvatarEntitySynchInfo(source, writer, remoteBehaviorId, 0x64, "MP-CONTROL")) return;
            writer.WriteByte(0x06);
            SendToClient(viewer, writer.ToArray());
            Debug.LogError($"[MP-CONTROL] FollowClient grant viewer={viewer.LoginName} source={source.LoginName} behavior={remoteBehaviorId} sourceFunction=UnitBehavior::FollowClient@0x5202F0");
        }

        private IEnumerator SendDeferredClientControl(RRConnection conn, ushort behaviorId)
        {
            yield return new WaitForSeconds(0.55f);
            if (conn == null || !conn.IsConnected) yield break;
            ushort componentId = conn.UnitBehaviorId != 0 ? (ushort)conn.UnitBehaviorId : behaviorId;
            if (componentId == 0) yield break;

            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x35);
            writer.WriteUInt16(componentId);
            writer.WriteByte(0x64);
            writer.WriteByte(0x01);
            WritePlayerEntitySynch(conn, writer);
            writer.WriteByte(0x06);
            SendCompressedA(conn, 0x01, 0x0F, writer.ToArray());
            Debug.LogError($"[FOLLOW-DEFER] Sent client control for UnitBehavior={componentId}");
        }

        private void SendPlayerEntitySpawn(RRConnection conn)
        {
            Debug.LogError("[SEND-PLAYER-ENTITY-SPAWN] state=start");
            Debug.LogError($" SendPlayerEntitySpawn CALLED! Stack trace:");
            Debug.LogError(Environment.StackTrace);

            try
            {
                if (conn.LoginName != null && _playerIsFree.ContainsKey(conn.LoginName))
                {
                    try
                    {
                        using (var banDb = Database.GameDatabase.GetConnection())
                        {
                            object banResult = Database.GameDatabase.ExecuteScalar(banDb,
                                "SELECT is_banned FROM accounts WHERE username = @name",
                                ("@name", conn.LoginName));
                            if (banResult != null && System.Convert.ToInt32(banResult) != 0)
                            {
                                SendSystemMessage(conn, "Your account has been banned. Contact an administrator.");
                                Debug.LogError($"[ACCOUNT] Banned user {conn.LoginName} tried to enter game");
                                StartCoroutine(DelayedBanDisconnect(conn));
                                return;
                            }
                        }
                    }
                    catch { }
                }

                if (!_selectedCharacter.TryGetValue(conn.LoginName, out var character))
                {
                    Debug.LogError($"No selected character found for {conn.LoginName}");
                    return;
                }

                SavedCharacter savedChar = CharacterRepository.GetCharacter(character.Id);
                if (savedChar == null)
                {
                    Debug.LogError($"[OP4]  No saved data for character {character.Id}, creating default Fighter");
                    savedChar = CharacterRepository.CreateCharacter(character.Name, "Fighter", AccountRepository.GetAccountId(conn.LoginName), conn.LoginName);
                }
                else
                {
                    Debug.LogError($"[OP4]  Loaded character {savedChar.name} ({savedChar.className}) from JSON");
                }
                string spawnConnIdKey = conn.ConnId.ToString();

                {
                    var pvpMatchForSpawn = Gameplay.PVPMatchmaking.Instance.GetMatchForPlayer(conn.LoginName);
                    if (pvpMatchForSpawn != null && _zones.TryGetValue(conn.CurrentZoneId, out var pvpSpawnZone) && IsPvpZone(pvpSpawnZone.name))
                        SendPVPMatchControllerSpawn(conn);
                }
                Debug.LogError($"[SPAWN] Initializing PlayerState with key='{spawnConnIdKey}' for conn.ConnId={conn.ConnId}");
                PlayerState playerState = GetPlayerState(spawnConnIdKey);

                if (playerState.Experience > 0 || playerState.Level > 1)
                {
                    var currentChar = CharacterRepository.GetCharacter(_selectedCharacter[conn.LoginName].Id);
                    byte currentPersistedLevel = SavedCharacterLevel.ResolvePersistedLevel(playerState.Level);
                    if (currentChar != null && (currentPersistedLevel > currentChar.level || playerState.Experience > currentChar.experience))
                    {
                        currentChar.level = currentPersistedLevel;
                        currentChar.experience = playerState.Experience;
                        CharacterRepository.SaveCharacter(currentChar);
                        savedChar = currentChar;
                        Debug.LogError($"[ZONE] Saved progress before zone transition: persistedLevel={currentChar.level} clientLevel={playerState.Level} xp={currentChar.experience}");
                    }
                }

                bool isFreshPlayerState = (playerState.Experience == 0 && playerState.Level <= 1);
                bool isZoneTransition = (conn.Avatar != null && conn.Player != null);
                bool useFullHPBaseline = isZoneTransition && (conn.FullHPBaselineOnNextSpawn || conn.RespawnFullHPPending || IsPvpZone(conn.CurrentZoneName));
                bool includeSavedCharacterHP = isZoneTransition || (savedChar != null && savedChar.currentHP > 0);
                PlayerHPPreserve spawnHPPreserve = CapturePlayerHPPreserve(conn, playerState, savedChar, isZoneTransition ? "zone-preinit" : "spawn-preinit", includeSavedCharacterHP);
                bool preservedHPFromLiveState = spawnHPPreserve.FromLiveState;
                uint preservedLiveHPWire = preservedHPFromLiveState ? spawnHPPreserve.HPWire : 0;
                uint preservedSavedHPWire = savedChar.currentHP;
                uint hpToKeepWire = spawnHPPreserve.HasHP ? spawnHPPreserve.HPWire : 0;
                bool preservedHPKnown = spawnHPPreserve.HasHP;
                string hpToKeepSource = spawnHPPreserve.HasHP ? spawnHPPreserve.Source : "none";
                uint manaToKeepWire = isZoneTransition && playerState.HasClientMana ? playerState.CurrentManaWire : savedChar.currentMana;
                Debug.LogError($"[SPAWN-HP-PRESERVE] zoneTransition={isZoneTransition} baseline={useFullHPBaseline} includeSaved={includeSavedCharacterHP} source={hpToKeepSource} preserved={hpToKeepWire} live={preservedLiveHPWire} saved={preservedSavedHPWire}");

                int runtimeLevel = SavedCharacterLevel.ResolveRuntimeLevel(savedChar);
                Debug.LogError($"[SPAWN-LEVEL] persistedLevel={savedChar.level} runtimeLevel={runtimeLevel} xp={savedChar.experience} source=SavedCharacterLevel");
                playerState.InitializeStats(savedChar.className ?? "Fighter", runtimeLevel);
                playerState.ApplyAllocatedStats(savedChar.statStrength, savedChar.statAgility, savedChar.statEndurance, savedChar.statIntellect);
                if (isFreshPlayerState)
                {
                    playerState.Experience = savedChar.experience;
                    Debug.LogError($"[SPAWN-XP] Fresh login: loaded XP={savedChar.experience} persistedLv={savedChar.level} clientLv={playerState.Level} from DB for {conn.LoginName}");
                }
                else
                {
                    Debug.LogError($"[SPAWN-XP] Zone transition: keeping live XP={playerState.Experience} Lv={playerState.Level} for {conn.LoginName}");
                }
                playerState.RefreshRegenFactors("spawn-authored-hero-desc");


                InitializePlayerSkillLevels(conn, savedChar);

                if (!string.IsNullOrEmpty(savedChar.tpZone))
                {
                    conn.HasSavedTownPortal = true;
                    conn.TownPortalZoneName = savedChar.tpZone;
                    conn.TownPortalZoneId = (uint)savedChar.tpZoneId;
                    conn.TownPortalTargetZone = savedChar.tpTargetZone;
                    conn.TownPortalPosX = savedChar.tpPosX;
                    conn.TownPortalPosY = savedChar.tpPosY;
                    conn.TownPortalPosZ = savedChar.tpPosZ;
                    Debug.LogError($"[SPAWN] Loaded saved town portal: {savedChar.tpZone}");
                }

                if (savedChar.equipment != null && !string.IsNullOrEmpty(savedChar.equipment.weapon))
                {
                    var weaponData = DatabaseLoader.FindItem(savedChar.equipment.weapon);
                    int weaponLevel = 1;
                    int weaponStoredLevel = -1;
                    if (savedChar.equipment.slotLevel != null && savedChar.equipment.slotLevel.TryGetValue("weapon", out int savedWeaponLevel) && savedWeaponLevel >= 0)
                    {
                        weaponLevel = savedWeaponLevel;
                        weaponStoredLevel = savedWeaponLevel;
                    }
                    else
                        weaponLevel = DungeonRunners.Gameplay.RPGSettings.GetItemLevel(savedChar.equipment.weapon);
                    var weaponNode = GCDatabase.Instance.ResolveWithInheritance(savedChar.equipment.weapon);
                    (float damage, float volatility, float range, float cooldown, string weaponClass, float weaponSpeed,
                     string damageType, string weaponCategory, bool useProjectile, float projectileSpeed, float projectileSize, int burstCount) weaponStats = default;
                    if (weaponNode == null)
                        RuntimeEvidence.LogFallbackHit("damage-weapon-desc", "missing-gc-node", $"source=spawn weapon={savedChar.equipment.weapon} sourceFunction=Weapon::ComputeAttributes-return", 64);
                    else
                        weaponStats = GCDatabase.Instance.GetWeaponStats(savedChar.equipment.weapon);
                    float authoredWeaponDamage = weaponStats.damage > 0f ? weaponStats.damage : 0f;
                    float authoredWeaponVolatility = weaponStats.volatility > 0f ? weaponStats.volatility : 0.25f;
                    string resolvedWeaponClass = !string.IsNullOrEmpty(weaponStats.weaponClass) ? weaponStats.weaponClass : weaponData != null && !string.IsNullOrEmpty(weaponData.weaponClass) ? weaponData.weaponClass : "";
                    string resolvedDamageType = !string.IsNullOrEmpty(weaponStats.damageType) ? weaponStats.damageType : "";
                    if (authoredWeaponDamage > 0f &&
                        TryResolveWeaponDescIds("spawn", savedChar.equipment.weapon, resolvedWeaponClass, resolvedDamageType, out int clientWeaponClassId, out int clientDamageTypeId))
                    {
                        playerState.WeaponDamage = authoredWeaponDamage;
                        playerState.WeaponDamageVolatility = Mathf.Clamp(authoredWeaponVolatility, 0f, 0.95f);
                        playerState.WeaponLevel = Math.Max(1, weaponLevel);
                        playerState.WeaponClass = resolvedWeaponClass;
                        playerState.WeaponDamageType = resolvedDamageType;
                        playerState.WeaponCategory = !string.IsNullOrEmpty(weaponStats.weaponCategory) ? weaponStats.weaponCategory : "";
                        playerState.WeaponStatsResolved = true;
                        playerState.WeaponClassId = clientWeaponClassId;
                        playerState.DamageTypeId = clientDamageTypeId;
                        DamageResolver.ApplyWeaponRuntimeBaseDamage(playerState, playerState.Level, weaponStoredLevel, playerState.WeaponLevel, "spawn");
                        playerState.WeaponRange = weaponStats.range > 0 ? Mathf.RoundToInt(weaponStats.range) : weaponData != null && weaponData.range > 0 ? weaponData.range : 0;
                        playerState.WeaponCooldown = weaponStats.cooldown > 0 ? weaponStats.cooldown : weaponData != null && weaponData.cooldown > 0 ? weaponData.cooldown : 0f;
                        playerState.WeaponSpeed = weaponStats.weaponSpeed > 0 ? weaponStats.weaponSpeed : weaponData != null && weaponData.weaponSpeed > 0 ? weaponData.weaponSpeed : 105f;
                        playerState.WeaponUsesProjectile = weaponStats.useProjectile;
                        playerState.WeaponProjectileSpeed = weaponStats.projectileSpeed;
                        playerState.WeaponProjectileSize = weaponStats.projectileSize;
                        playerState.WeaponBurstCount = Math.Max(1, weaponStats.burstCount);
                        Debug.LogError($"[SPAWN] Weapon '{savedChar.equipment.weapon}' damage={playerState.WeaponDamage} vol={playerState.WeaponDamageVolatility:F2} level={playerState.WeaponLevel} clientDamageLevel={playerState.WeaponDamageLevel} clientBaseDamage={playerState.WeaponBaseDamage} clientBaseSource={playerState.WeaponBaseDamageSource} class={playerState.WeaponClass}/{playerState.WeaponClassId} damageType={playerState.WeaponDamageType}/{playerState.DamageTypeId} category={playerState.WeaponCategory} range={playerState.WeaponRange} cooldown={playerState.WeaponCooldown:F2} speed={playerState.WeaponSpeed:F2} useProjectile={playerState.WeaponUsesProjectile} projectileSpeed={playerState.WeaponProjectileSpeed:F2} projectileSize={playerState.WeaponProjectileSize:F2} burst={playerState.WeaponBurstCount} from authored data");
                    }
                    else
                    {
                        playerState.WeaponDamage = 0f;
                        playerState.WeaponDamageVolatility = 0f;
                        playerState.WeaponLevel = 0;
                        playerState.WeaponClass = "";
                        playerState.WeaponDamageType = "";
                        playerState.WeaponCategory = "";
                        playerState.WeaponStatsResolved = false;
                        playerState.WeaponClassId = 0;
                        playerState.DamageTypeId = -1;
                        playerState.WeaponDamageLevel = 0;
                        playerState.WeaponBaseDamage = 0;
                        playerState.WeaponBaseDamageTracksPlayerLevel = false;
                        playerState.WeaponBaseDamageSource = "spawn:unresolved";
                        playerState.WeaponRange = 0;
                        playerState.WeaponCooldown = 0f;
                        playerState.WeaponSpeed = 0f;
                        playerState.WeaponUsesProjectile = false;
                        playerState.WeaponProjectileSpeed = 0f;
                        playerState.WeaponProjectileSize = 0f;
                        playerState.WeaponBurstCount = 1;
                        RuntimeEvidence.LogFallbackHit("damage-level", "spawn-weapon-unresolved", $"weapon={savedChar.equipment.weapon}", 32);
                        Debug.LogError($"[SPAWN] Weapon '{savedChar.equipment.weapon}' unresolved; leaving weapon damage fields blocked until equipment data resolves");
                    }
                }

                Debug.LogError($"[SPAWN] playerState key='{spawnConnIdKey}' level={playerState.Level} xp={playerState.Experience} weaponDamage={playerState.WeaponDamage} weaponVolatility={playerState.WeaponDamageVolatility} weaponLevel={playerState.WeaponLevel} baseDamage={playerState.WeaponBaseDamage} baseSource={playerState.WeaponBaseDamageSource} op12HP={playerState.Op12HP} entitySynchInfoHP={playerState.EntitySynchInfoHP}");

                var activeQuests = ConvertToActiveQuests(savedChar.activeQuests);

                foreach (var loadedQ in activeQuests)
                    loadedQ.InstanceId = conn.NextQuestInstanceId++;

                var completedQuests = savedChar.completedQuests ?? new List<string>();
                var unlockedCheckpoints = savedChar.unlockedCheckpoints ?? new List<string>();
                if (unlockedCheckpoints.Count == 0)
                {
                    unlockedCheckpoints.Add("world.checkpoints.TownCheckpoint");
                    unlockedCheckpoints.Add("world.checkpoints.TutorialCheckpoint");
                }
                QuestManager.Instance.InitializePlayer(conn.ConnId.ToString(), activeQuests, completedQuests, unlockedCheckpoints, playerState.Level);
                Debug.LogError($"[SPAWN] component=QuestManager persistedLevel={savedChar.level} clientLevel={playerState.Level} active={activeQuests.Count} completed={completedQuests.Count}");

                GCObject avatar;
                GCObject player;







                Debug.LogError("[SPAWN] state=first-login action=create-entities");

                if (isZoneTransition)
                {
                    Debug.LogError("[ZONE-TRANSITION] action=reassign-ids source=reuse-objects");
                    avatar = conn.Avatar;
                    player = conn.Player;

                    if (_playerComponentTypes.ContainsKey(spawnConnIdKey))
                        _playerComponentTypes[spawnConnIdKey].Clear();
                    if (_playerManipulatorsIds.ContainsKey(spawnConnIdKey))
                        _playerManipulatorsIds.Remove(spawnConnIdKey);
                    if (_playerEquippedItems.ContainsKey(spawnConnIdKey))
                        _playerEquippedItems[spawnConnIdKey].Clear();

                    _nextEntityId = (uint)(conn.ConnId * 500 + 10);

                    avatar.Id = _nextEntityId++;
                    player.Id = _nextEntityId++;

                    var freshChar = CharacterRepository.GetCharacter(_selectedCharacter[conn.LoginName].Id);

                    var freshEquipItems = new List<GCObject>();
                    if (freshChar?.equipment != null)
                    {
                        string[] eqSlots = { freshChar.equipment.weapon, freshChar.equipment.armor,
                            freshChar.equipment.helmet, freshChar.equipment.gloves, freshChar.equipment.boots,
                            freshChar.equipment.shoulders, freshChar.equipment.shield,
                            freshChar.equipment.ring1, freshChar.equipment.ring2, freshChar.equipment.amulet };
                        string[] eqSlotNames = { "weapon", "armor", "helmet", "gloves", "boots",
                            "shoulders", "shield", "ring1", "ring2", "amulet" };
                        for (int eqIdx = 0; eqIdx < eqSlots.Length; eqIdx++)
                        {
                            string equipmentGcClass = eqSlots[eqIdx];
                            if (string.IsNullOrEmpty(equipmentGcClass)) continue;
                            string fixedGc = GCObject.GetPacketGCClassFor(equipmentGcClass);
                            string gcLower = fixedGc.ToLower();
                            string dfcClass = "Armor";
                            if (gcLower.Contains("ring") || gcLower.Contains("amulet"))
                                dfcClass = "Item";
                            else if (gcLower.Contains("sword") || gcLower.Contains("axe") || gcLower.Contains("mace") || gcLower.Contains("dagger") || gcLower.Contains("hammer") || gcLower.Contains("staff") || gcLower.Contains("spear") || gcLower.Contains("pick") || gcLower.Contains("club") || gcLower.Contains("katana") || gcLower.Contains("polearm"))
                                dfcClass = "MeleeWeapon";
                            else if (gcLower.Contains("bow") || gcLower.Contains("gun") || gcLower.Contains("crossbow") || gcLower.Contains("cannon"))
                                dfcClass = "RangedWeapon";
                            freshEquipItems.Add(new GCObject
                            {
                                GCClass = fixedGc,
                                DFCClass = dfcClass,
                                StoredRarity = (freshChar.equipment.slotRarity != null &&
                                    freshChar.equipment.slotRarity.TryGetValue(eqSlotNames[eqIdx], out int eqRar)) ? eqRar : -1,
                                StoredLevel = (freshChar.equipment.slotLevel != null &&
                                    freshChar.equipment.slotLevel.TryGetValue(eqSlotNames[eqIdx], out int eqLvl)) ? eqLvl : -1,
                                TargetSlot = (eqSlotNames[eqIdx] == "shield" && (dfcClass == "MeleeWeapon" || dfcClass == "RangedWeapon")) ? (uint?)11 : null
                            });
                        }
                        Debug.LogError($"[ZONE-EQUIP] items={freshEquipItems.Count} source=db");
                    }

                    GCObject ztManipulators = avatar.Children.FirstOrDefault(c => c.GCClass == "Manipulators");
                    if (ztManipulators != null)
                    {
                        int removedCount = ztManipulators.Children.RemoveAll(c =>
                            c.DFCClass == "Armor" || c.DFCClass == "MeleeWeapon" ||
                            c.DFCClass == "RangedWeapon" || c.DFCClass == "Item");
                        Debug.LogError($"[ZONE-MANIP] removed={removedCount} remainingSkills={ztManipulators.Children.Count}");

                        foreach (var eqItem in freshEquipItems)
                            ztManipulators.Children.Add(eqItem);

                        ztManipulators.Id = _nextEntityId++;
                        foreach (var child in ztManipulators.Children)
                            child.Id = _nextEntityId++;
                        conn.ManipulatorsComponentId = (ushort)ztManipulators.Id;
                        TrackManipulatorsId(spawnConnIdKey, (ushort)ztManipulators.Id);
                    }
                    GCObject ztEquipment = avatar.Children.FirstOrDefault(c => c.GCClass == "avatar.base.Equipment");
                    if (ztEquipment != null)
                    {
                        ztEquipment.Id = _nextEntityId++;
                        ztEquipment.Children.Clear();
                        foreach (var eqItem in freshEquipItems)
                            ztEquipment.Children.Add(eqItem);
                        TrackComponent(spawnConnIdKey, (ushort)ztEquipment.Id, "Equipment");
                    }
                    GCObject zoneTransitionQuestManager = player.Children.FirstOrDefault(c => c.GCClass == "QuestManager");
                    if (zoneTransitionQuestManager != null)
                    {
                        zoneTransitionQuestManager.Id = _nextEntityId++;
                        conn.QuestManagerId = (ushort)zoneTransitionQuestManager.Id;
                    }
                    GCObject zoneTransitionDialogManager = player.Children.FirstOrDefault(c => c.GCClass == "DialogManager");
                    if (zoneTransitionDialogManager != null)
                    {
                        zoneTransitionDialogManager.Id = _nextEntityId++;
                        conn.DialogManagerId = (ushort)zoneTransitionDialogManager.Id;
                    }
                    GCObject zoneTransitionUnitContainer = avatar.Children.FirstOrDefault(c => c.GCClass == "UnitContainer");
                    if (zoneTransitionUnitContainer != null)
                    {
                        zoneTransitionUnitContainer.Id = _nextEntityId++;
                        foreach (var child in zoneTransitionUnitContainer.Children)
                            child.Id = _nextEntityId++;
                        conn.UnitContainerId = (ushort)zoneTransitionUnitContainer.Id;
                        TrackComponent(spawnConnIdKey, (ushort)zoneTransitionUnitContainer.Id, "UnitContainer");
                    }
                    GCObject ztMod = avatar.Children.FirstOrDefault(c => c.GCClass == "Modifiers");
                    if (ztMod != null)
                    {
                        ztMod.Id = _nextEntityId++;
                        conn.ModifiersId = (ushort)ztMod.Id;
                        conn.SentPassiveModifierIds.Clear();
                    }
                    GCObject ztSkills = avatar.Children.FirstOrDefault(c => c.GCClass == "avatar.base.skills");
                    if (ztSkills != null) ztSkills.Id = _nextEntityId++;
                    GCObject ztUB = avatar.Children.FirstOrDefault(c => c.GCClass == "avatar.base.UnitBehavior");
                    if (ztUB != null) ztUB.Id = _nextEntityId++;

                    Debug.LogError($"[ZONE-TRANSITION] avatarId={avatar.Id} playerId={player.Id} nextId={_nextEntityId}");

                    if (_playerAvatarEntityId.TryGetValue(spawnConnIdKey, out uint previousAvatarIdZone) && previousAvatarIdZone != avatar.Id)
                    {
                        Combat.WeaponUseRuntime.Instance.ClearConnection(spawnConnIdKey);
                        CombatRuntime.Instance.UnregisterPlayer(previousAvatarIdZone);
                        Debug.LogError($"[COMBAT-LIFECYCLE] avatar id changed conn={spawnConnIdKey} old={previousAvatarIdZone} new={avatar.Id}");
                    }
                    _playerAvatarEntityId[spawnConnIdKey] = avatar.Id;

                    CalculateEquipmentBonuses(conn.ConnId.ToString(), avatar);
                    if (useFullHPBaseline)
                    {
                        ApplyFullHPBaseline(conn, playerState, spawnHPPreserve, "zone");
                    }
                    else if (ApplyPlayerHPPreserve(conn, playerState, spawnHPPreserve, "zone", true))
                    {
                        Debug.LogError($"[ZONE-HP-PRESERVE] source={hpToKeepSource} preserved={hpToKeepWire} applied={playerState.CurrentHPWire}/{playerState.MaxHPWire} baseline={useFullHPBaseline} live={preservedHPFromLiveState}");
                        Debug.LogError($"[ZONE-HP-REGEN]  damage regen cooldown restored ticks={PlayerState.DamageRegenSuppressTicks} hp={playerState.CurrentHPWire}/{playerState.MaxHPWire}");
                    }
                    else
                    {
                        Debug.LogError($"[ZONE-HP-FULL] action=restore source={hpToKeepSource} preserved={hpToKeepWire} baseline={useFullHPBaseline}");
                        playerState.RestoreToFull();
                    }
                    if (!useFullHPBaseline && manaToKeepWire > 0)
                        playerState.SetCurrentMana(manaToKeepWire, "ZONE-HP-PRESERVE", false);
                    conn.IgnoreClientHPUntilTime = 0f;
                    RecordPlayerHPKnown(conn, "ZONE-HP-INIT", playerState.CurrentHPWire);

                    foreach (var prop in avatar.Properties)
                    {
                        if (prop is UInt32Property up && up.Name == "HitPoints")
                        {
                            up.Value = 1337;
                            break;
                        }
                    }
                    Debug.LogError($"[ZONE] HP={playerState.CurrentHPWire / 256}/{playerState.MaxHPWire / 256} Mana={playerState.CurrentManaWire / 256}/{playerState.MaxManaWire / 256}");

                    try { SavePlayerLevel(conn); }
                    catch (Exception ex) { Debug.LogError($"[ZONE] save state=failed message={ex.Message}"); }

                    playerState.LogFullState("ZONE-TRANSITION");
                }
                else
                {
                    Debug.LogError("[SPAWN] state=first-login action=create-entities");

                    _nextEntityId = (uint)(conn.ConnId * 500 + 10);

                    avatar = GCObjectFactory.LoadAvatar(savedChar);
                    player = GCObjectFactory.NewPlayer(character.Name, (uint)character.Id, GroupDirectory.Instance.GetGroupForConn(conn.ConnId)?.GroupId ?? 0);

                    CalculateEquipmentBonuses(conn.ConnId.ToString(), avatar);
                    playerState.LogFullState("AFTER-AVATAR-LOAD");

                    conn.Avatar = avatar;
                    conn.Player = player;

                    Debug.LogError("[SPAWN-ID] scope=entity state=start");

                    avatar.Id = _nextEntityId++;
                    Debug.LogError($"[OP1] avatarId={avatar.Id}");

                    player.Id = _nextEntityId++;
                    Debug.LogError($"[OP2] playerId={player.Id}");

                    Debug.LogError("[SPAWN-ID] scope=avatar-components state=start");

                    GCObject manipulatorsObject = avatar.Children.FirstOrDefault(c => c.GCClass == "Manipulators");
                    if (manipulatorsObject != null)
                    {
                        manipulatorsObject.Id = _nextEntityId++;
                        Debug.LogError($"[OP4] manipulatorsId={manipulatorsObject.Id}");
                        foreach (var child in manipulatorsObject.Children)
                        {
                            child.Id = _nextEntityId++;
                            Debug.LogError($"[OP4] childGcClass='{child.GCClass}' id={child.Id}");
                        }
                    }

                    GCObject equipmentObject = avatar.Children.FirstOrDefault(c => c.GCClass == "avatar.base.Equipment");
                    if (equipmentObject != null)
                    {
                        equipmentObject.Id = _nextEntityId++;
                        Debug.LogError($"[OP5] equipmentId={equipmentObject.Id}");
                        foreach (var child in equipmentObject.Children)
                        {
                            child.Id = _nextEntityId++;
                            Debug.LogError($"[OP5] childGcClass='{child.GCClass}' id={child.Id}");
                        }
                    }

                    Debug.LogError("[SPAWN-ID] scope=player-components state=start");

                    GCObject questManagerObject = player.Children.FirstOrDefault(c => c.GCClass == "QuestManager");
                    if (questManagerObject == null)
                    {
                        Debug.LogError("[SPAWN-ID] component=QuestManager state=missing action=create");
                        questManagerObject = new GCObject
                        {
                            GCClass = "QuestManager",
                            DFCClass = "QuestManager",
                            Name = "QuestManager"
                        };
                        player.AddChild(questManagerObject);
                    }
                    questManagerObject.Id = _nextEntityId++;
                    Debug.LogError($"[OP6] questManagerId={questManagerObject.Id}");

                    GCObject dialogManagerObject = player.Children.FirstOrDefault(c => c.GCClass == "DialogManager");
                    if (dialogManagerObject == null)
                    {
                        Debug.LogError("[SPAWN-ID] component=DialogManager state=missing action=create");
                        dialogManagerObject = new GCObject
                        {
                            GCClass = "DialogManager",
                            DFCClass = "DialogManager",
                            Name = "DialogManager"
                        };
                        player.AddChild(dialogManagerObject);
                    }
                    dialogManagerObject.Id = _nextEntityId++;
                    Debug.LogError($"[OP7] dialogManagerId={dialogManagerObject.Id}");

                    GCObject unitContainerObject = avatar.Children.FirstOrDefault(c => c.GCClass == "UnitContainer");
                    if (unitContainerObject != null)
                    {
                        unitContainerObject.Id = _nextEntityId++;
                        Debug.LogError($"[OP8] unitContainerId={unitContainerObject.Id}");
                        foreach (var child in unitContainerObject.Children)
                        {
                            child.Id = _nextEntityId++;
                            Debug.LogError($"[OP8] inventoryGcClass='{child.GCClass}' id={child.Id}");
                        }
                    }

                    GCObject modifiersObject = avatar.Children.FirstOrDefault(c => c.GCClass == "Modifiers");
                    if (modifiersObject != null)
                    {
                        modifiersObject.Id = _nextEntityId++;
                        conn.ModifiersId = (ushort)modifiersObject.Id;
                        conn.SentPassiveModifierIds.Clear();
                        Debug.LogError($"[OP9] modifiersId={modifiersObject.Id}");

                    }

                    GCObject skillsObject = avatar.Children.FirstOrDefault(c => c.GCClass == "avatar.base.skills");
                    if (skillsObject == null)
                    {
                        Debug.LogError("[SPAWN-ID] component=Skills state=missing action=create");
                        skillsObject = new GCObject
                        {
                            GCClass = "avatar.base.skills",
                            DFCClass = "Skills",
                            Name = "Skills"
                        };
                        avatar.AddChild(skillsObject);
                    }
                    skillsObject.Id = _nextEntityId++;
                    Debug.LogError($"[OP10] skillsId={skillsObject.Id}");

                    GCObject unitBehaviorObject = avatar.Children.FirstOrDefault(c => c.GCClass == "avatar.base.UnitBehavior");
                    if (unitBehaviorObject != null)
                    {
                        unitBehaviorObject.Id = _nextEntityId++;
                        Debug.LogError($"[OP11] unitBehaviorId={unitBehaviorObject.Id}");
                    }

                    Debug.LogError($"[SPAWN-ID] state=complete nextId={_nextEntityId}");
                }

                Debug.LogError($"[SPAWN-ID] avatarId=0x{avatar.Id:X4} playerId=0x{player.Id:X4}");

                if (_playerAvatarEntityId.TryGetValue(spawnConnIdKey, out uint previousAvatarIdNormal) && previousAvatarIdNormal != avatar.Id)
                {
                    Combat.WeaponUseRuntime.Instance.ClearConnection(spawnConnIdKey);
                    CombatRuntime.Instance.UnregisterPlayer(previousAvatarIdNormal);
                    Debug.LogError($"[COMBAT-LIFECYCLE] avatar id changed conn={spawnConnIdKey} old={previousAvatarIdNormal} new={avatar.Id}");
                }
                _playerAvatarEntityId[spawnConnIdKey] = avatar.Id;

                if (useFullHPBaseline)
                {
                    ApplyFullHPBaseline(conn, playerState, spawnHPPreserve, isZoneTransition ? "zone-final" : "spawn");
                }
                else if (ApplyPlayerHPPreserve(conn, playerState, spawnHPPreserve, isZoneTransition ? "zone-final" : "spawn", true))
                {
                    Debug.LogError($"[SPAWN-HP-PRESERVE] source={hpToKeepSource} preserved={hpToKeepWire} applied={playerState.CurrentHPWire}/{playerState.MaxHPWire} baseline={useFullHPBaseline} live={preservedHPFromLiveState}");
                    Debug.LogError($"[SPAWN-HP-REGEN]  damage regen cooldown restored ticks={PlayerState.DamageRegenSuppressTicks} hp={playerState.CurrentHPWire}/{playerState.MaxHPWire}");
                }
                else
                {
                    Debug.LogError($"[SPAWN-HP-FULL] Restoring full HP source={hpToKeepSource} preserved={hpToKeepWire} baseline={useFullHPBaseline}");
                    playerState.RestoreToFull();
                }

                if (!useFullHPBaseline && manaToKeepWire > 0)
                    playerState.SetCurrentMana(manaToKeepWire, "SPAWN-HP-PRESERVE", false);

                conn.IgnoreClientHPUntilTime = 0f;
                RecordPlayerHPKnown(conn, "SPAWN-HP-INIT", playerState.CurrentHPWire);

                if (!isZoneTransition)
                {
                    foreach (var prop in avatar.Properties)
                    {
                        if (prop is UInt32Property up && up.Name == "HitPoints")
                        {
                            up.Value = 1337;
                            break;
                        }
                    }
                    Debug.LogError($"[SPAWN] Full HP: {playerState.MaxHPWire / 256} Mana: {playerState.MaxManaWire / 256} (savedHP={savedChar.currentHP / 256} savedMana={savedChar.currentMana / 256})");
                }

                playerState.AvatarHP = playerState.CurrentHPWire;
                if (useFullHPBaseline)
                {
                    Debug.LogError($"[ZONE-HP-BASELINE] completed flag source={hpToKeepSource} preservedKnown={preservedHPKnown} hp={playerState.CurrentHPWire}/{playerState.MaxHPWire}");
                    conn.FullHPBaselineOnNextSpawn = false;
                    conn.RespawnFullHPPending = false;
                }
                Debug.LogError($"[SPAWN] Server HP set: {playerState.CurrentHPWire / 256} / {playerState.MaxHPWire / 256} Mana: {playerState.CurrentManaWire / 256} / {playerState.MaxManaWire / 256}");

                try { SavePlayerLevel(conn); }
                catch (Exception ex) { Debug.LogError($"[SPAWN] Login save failed: {ex.Message}"); }








                var writer = new LEWriter();

                writer.WriteByte(0x07);
                Debug.LogError(" BeginStream (ClientEntityChannel 0x07)");
                Debug.LogError($"[OP1-DETAIL] avatar.GCClass='{avatar.GCClass}'");
                Debug.LogError($"[OP1-DETAIL] savedChar.avatarClass='{savedChar.avatarClass}'");
                Debug.LogError($"[OP1-DETAIL] savedChar.className='{savedChar.className}'");
                Debug.LogError("[OP1] action=create-avatar state=start");
                int beforeOp = writer.Position;

                writer.WriteByte(0x01);
                writer.WriteUInt16((ushort)avatar.Id);
                WriteGCType(writer, avatar.GCClass, preserveCase: true);
                int opBytes = (int)(writer.Position - beforeOp);
                Debug.LogError($" Create Avatar ID={avatar.Id:X4} Class={avatar.GCClass.ToLower()} ({opBytes} bytes)");
                Debug.LogError($" Create Avatar ID={avatar.Id:X4} Class={avatar.GCClass.ToLower()} ({opBytes} bytes)");
                Debug.LogError($"[CUMULATIVE-OP1] Total after Op1: {writer.Position} bytes");
                Debug.LogError("[OP2] action=create-player state=start");
                beforeOp = writer.Position;

                writer.WriteByte(0x01);
                writer.WriteUInt16((ushort)player.Id);
                WriteGCType(writer, player.GCClass.ToLower());

                opBytes = (int)(writer.Position - beforeOp);
                Debug.LogError($" Create Player ID={player.Id:X4} Class={player.GCClass.ToLower()} ({opBytes} bytes)");
                Debug.LogError($" Create Player ID={player.Id:X4} Class={player.GCClass.ToLower()} ({opBytes} bytes)");
                Debug.LogError($"[CUMULATIVE-OP2] Total after Op2: {writer.Position} bytes");
                Debug.LogError("[OP3] action=init-player state=start");
                beforeOp = writer.Position;

                writer.WriteByte(0x02);
                writer.WriteUInt16((ushort)player.Id);

                writer.WriteCString(player.Name);

                writer.WriteUInt32(0x00);
                writer.WriteUInt32(GroupDirectory.Instance.GetGroupForConn(conn.ConnId)?.GroupId ?? 0);

                byte membershipByte;
                if (IsPlayerAdmin(conn.LoginName))
                    membershipByte = 0x00;
                else if (IsPlayerFree(conn.LoginName))
                    membershipByte = 0x02;
                else
                    membershipByte = 0x01;
                writer.WriteByte(membershipByte);

                uint op3UserId = GetCharSqlId(conn);
                writer.WriteUInt32(op3UserId);
                Debug.LogError($"[OP3-USERID] charSqlId=0x{op3UserId:X8}");

                uint _pvpWins = 0, _pvpRating = 0;
                if (_selectedCharacter.TryGetValue(conn.LoginName, out var _pvpSel))
                {
                    var _pvpChar = CharacterRepository.GetCharacter(_pvpSel.Id);
                    if (_pvpChar != null) { _pvpWins = (uint)_pvpChar.pvpWins; _pvpRating = (uint)_pvpChar.pvpRating; }
                }
                writer.WriteUInt32(_pvpWins);
                writer.WriteUInt32(_pvpRating);

                var selfPvpMatch = Gameplay.PVPMatchmaking.Instance.GetMatchForPlayer(conn.LoginName);
                if (selfPvpMatch != null)
                {
                    string selfTeamPath = Gameplay.PVPMatchmaking.TeamGcPath(selfPvpMatch.Archetype, selfPvpMatch.IsRed(conn.LoginName));
                    writer.WriteByte(0xFF); writer.WriteCString(selfTeamPath);
                }
                else
                {
                    writer.WriteByte(0x00);
                }

                string op3PosseName = savedChar.posseName ?? "";
                writer.WriteCString(op3PosseName);

                writer.WriteUInt32(0x00);

                opBytes = (int)(writer.Position - beforeOp);
                Debug.LogError($"[OP3] state=complete bytes={opBytes} pos={writer.Position}");
                Debug.LogError("[OP4] component=Manipulators state=start");


                beforeOp = writer.Position;

                var manipulators = avatar.Children.FirstOrDefault(c => c.GCClass == "Manipulators");
                if (manipulators == null)
                {
                    Debug.LogError(" Manipulators not found in avatar children!");
                    return;
                }

                Debug.LogError($"[OP4] manipulatorsId={manipulators.Id}");
                Debug.LogError($"[OP4-START] Total children in Manipulators: {manipulators.Children?.Count ?? 0}");
                TrackManipulatorsId(conn.ConnId.ToString(), (ushort)manipulators.Id);
                conn.ManipulatorsComponentId = (ushort)manipulators.Id;
                writer.WriteByte(0x32);
                writer.WriteUInt16((ushort)avatar.Id);
                writer.WriteUInt16((ushort)manipulators.Id);
                WriteGCType(writer, "Manipulators");
                writer.WriteByte(0x01);
                Debug.LogError($"[OP4-HEADER] Component header written");

                int validChildCount = 0;
                var validChildren = new List<GCObject>();

                if (manipulators.Children != null)
                {
                    foreach (var child in manipulators.Children)
                    {
                        bool isEquipment = (child.DFCClass == "Armor" || child.DFCClass == "Item" ||
                   child.DFCClass == "MeleeWeapon" ||
                   child.DFCClass == "RangedWeapon");

                        if (isEquipment)
                        {
                            ItemData itemData = DatabaseLoader.FindItem(child.GCClass);
                            if (itemData != null)
                            {
                                validChildren.Add(child);
                                validChildCount++;
                                Debug.LogError($"[OP4-PRECHECK]  Equipment '{child.GCClass}' is VALID (in ItemDatabase)");
                            }
                            else
                            {
                                string lk4 = child.GCClass.ToLowerInvariant();
                                if (lk4.StartsWith("items.pal.")) lk4 = lk4.Substring(10);
                                if (DungeonRunners.Gameplay.MerchantRuntime.HasAuthoredMerchantModSlots(lk4))
                                {
                                    validChildren.Add(child);
                                    validChildCount++;
                                    Debug.LogError($"[OP4-PRECHECK]  Equipment '{child.GCClass}' is VALID (in authored slot catalog)");
                                }
                                else
                                {
                                    var generalItem = DatabaseLoader.FindGeneralItem(child.GCClass);
                                    if (generalItem != null)
                                    {
                                        validChildren.Add(child);
                                        validChildCount++;
                                        Debug.LogError($"[OP4-PRECHECK]  Equipment '{child.GCClass}' is VALID (in GeneralItemDatabase)");
                                    }
                                    else
                                    {
                                        Debug.LogError($"[OP4-PRECHECK]  Equipment '{child.GCClass}' NOT in any database - WILL SKIP");
                                    }
                                }
                            }
                        }
                        else if (child.GCClass != null && child.GCClass.StartsWith("skills.professions.", StringComparison.OrdinalIgnoreCase))
                        {
                            Debug.LogError($"[OP4-PRECHECK]  Profession '{child.GCClass}' is NOT an ActiveSkill - WILL SKIP");
                        }
                        else
                        {
                            validChildren.Add(child);
                            validChildCount++;
                            Debug.LogError($"[OP4-PRECHECK]  Skill '{child.GCClass}' is VALID");
                        }
                    }
                }

                int passiveCount = 0;
                if (savedChar.hotbarSlots != null)
                    foreach (var hotbarSlot in savedChar.hotbarSlots)
                        if (IsPassiveSkill(hotbarSlot.skill))
                            passiveCount++;
                byte childCount = (byte)(validChildCount + passiveCount);
                writer.WriteByte(childCount);
                Debug.LogError($"[OP4-COUNT] Writing {childCount} VALID children (skills + equipment + {passiveCount} passives)");

                if (validChildren.Count > 0)
                {
                    var startingSlotMap = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
                    string playerClass = (savedChar.className ?? "Fighter").ToLower();
                    if (playerClass.Contains("fight") || playerClass.Contains("warrior"))
                    {
                        startingSlotMap["skills.generic.Stomp"] = 100;
                        startingSlotMap["skills.generic.Butcher"] = 105;
                    }
                    else if (playerClass.Contains("ranger"))
                    {
                        startingSlotMap["skills.generic.PoisonBlastRadius"] = 100;
                        startingSlotMap["skills.generic.PoisonShot"] = 105;
                    }
                    else
                    {
                        startingSlotMap["skills.generic.ShadowLightning"] = 100;
                        startingSlotMap["skills.generic.FireBolt"] = 105;
                    }
                    Debug.LogError($"[OP4-SLOTS] Class={playerClass}, starting slots: {string.Join(", ", startingSlotMap.Select(slotEntry => $"{slotEntry.Key}->{slotEntry.Value}"))}");

                    if (savedChar.hotbarSlots != null)
                        foreach (var hotbarSlot in savedChar.hotbarSlots)
                        {
                            startingSlotMap[hotbarSlot.skill] = hotbarSlot.slot;
                            Debug.LogError($"[OP4-HOTBAR] Loaded saved slot: {hotbarSlot.skill} -> {hotbarSlot.slot}");
                        }

                    uint nonSlotIdCounter = 200;
                    byte writeIndex = 0;
                    string connKey = conn.ConnId.ToString();
                    _playerSpellSlots[connKey] = new HashSet<byte>();
                    _playerManipMap[connKey] = new Dictionary<uint, string>();

                    Debug.LogError($"");
                    Debug.LogError($"[OP4] ");
                    Debug.LogError($"[OP4]  FIRST PASS: WRITING ALL SKILLS");
                    Debug.LogError($"[OP4] ");

                    foreach (var child in validChildren)
                    {
                        bool isEquipment = (child.DFCClass == "Armor" || child.DFCClass == "Item" ||
                    child.DFCClass == "MeleeWeapon" ||
                    child.DFCClass == "RangedWeapon");

                        if (!isEquipment)
                        {
                            uint skillId;
                            if (startingSlotMap.TryGetValue(child.GCClass, out uint slotId))
                            {
                                skillId = slotId;
                                Debug.LogError($"[OP4-SKILL] {child.GCClass} -> STARTING SLOT {skillId}");
                            }
                            else
                            {
                                skillId = nonSlotIdCounter++;
                                Debug.LogError($"[OP4-SKILL] {child.GCClass} -> UNASSIGNED (id={skillId}, no hotbar slot)");
                            }

                            _playerSpellSlots[connKey].Add(writeIndex);
                            _playerManipMap[connKey][skillId] = child.GCClass;
                            Debug.LogError($"[OP4-SKILL] Slot {writeIndex} = SPELL: {child.GCClass} -> manipId={skillId}");

                            Debug.LogError($"");
                            Debug.LogError($"[OP4-SKILL] ");
                            Debug.LogError($"[OP4-SKILL] Writing skill: {child.GCClass}");

                            int childStart = writer.Position;

                            writer.WriteByte(0xFF);
                            writer.WriteCString(child.GCClass.ToLower());
                            writer.WriteUInt32(skillId);
                            byte skillLevel = (byte)savedChar.GetSkillLevel(child.GCClass);
                            writer.WriteByte(skillLevel);
                            writer.WriteByte(0x00);

                            int childBytes = writer.Position - childStart;
                            Debug.LogError($"[OP4-SKILL] gcClass={child.GCClass} level={skillLevel} bytes={childBytes} id=0x{skillId:X8}");
                            writeIndex++;
                        }
                    }

                    foreach (var passive in CollectPassiveManipulators(savedChar))
                    {
                        _playerManipMap[connKey][passive.Slot] = passive.Skill;
                        WritePassiveManipulatorChild(writer, passive);
                        writeIndex++;
                        Debug.LogError($"[OP4-PASSIVE] {passive.Skill} slot={passive.Slot} level={passive.Level} modifierId=0x{passive.ModifierId:X8} sourceFunction=PassiveSkill::readInit@0x53D0E0");
                    }

                    Debug.LogError($"");
                    Debug.LogError($"[OP4] ");
                    Debug.LogError($"[OP4]  SECOND PASS: WRITING EQUIPMENT FULL WRITEINIT DATA!");
                    Debug.LogError($"[OP4] ");

                    foreach (var child in validChildren)
                    {
                        bool isEquipment = (child.DFCClass == "Armor" || child.DFCClass == "Item" ||
                    child.DFCClass == "MeleeWeapon" ||
                    child.DFCClass == "RangedWeapon");

                        if (isEquipment)
                        {
                            Debug.LogError($"[OP4-EQUIP] Slot {writeIndex} = EQUIP: {child.GCClass}");
                            Debug.LogError($"");
                            Debug.LogError($"[OP4-EQUIP] ");
                            Debug.LogError($"[OP4-EQUIP] Writing FULL WriteInit for: {child.GCClass}");

                            int childStart = writer.Position;

                            child.WriteInit(writer, playerState.Level);

                            int childBytes = writer.Position - childStart;
                            Debug.LogError($"[OP4-EQUIP]  WriteInit complete: {childBytes} bytes");
                            if (VerbosePacketLogging) Debug.LogError($"[OP4-EQUIP] Hex: {BitConverter.ToString(writer.ToArray(), childStart, childBytes)}");
                            writeIndex++;
                        }
                    }
                }
                else
                {
                    Debug.LogError($"[OP4-WARNING] No valid children to write!");
                }

                opBytes = (int)(writer.Position - beforeOp);
                Debug.LogError($"[OP4-COMPLETE] Manipulators component: {opBytes} bytes");
                if (_playerSpellSlots.ContainsKey(conn.ConnId.ToString()))
                    Debug.LogError($"[OP4-SPELLMAP] Spell slots for {conn.LoginName}: [{string.Join(", ", _playerSpellSlots[conn.ConnId.ToString()])}]");
                if (_playerManipMap.ContainsKey(conn.ConnId.ToString()))
                {
                    foreach (var manipulatorEntry in _playerManipMap[conn.ConnId.ToString()])
                        Debug.LogError($"[OP4-MANIPMAP] manipId={manipulatorEntry.Key} -> {manipulatorEntry.Value}");
                }

                if (VerbosePacketLogging)
                {
                    Debug.LogError($"");
                    Debug.LogError("[OP4-HEX]");
                    Debug.LogError("[OP4-HEX] operation=4 complete");
                    StringBuilder op4Hex = new StringBuilder();
                    for (int rowOffset = beforeOp; rowOffset < writer.Position; rowOffset += 16)
                    {
                        op4Hex.Append($"{rowOffset:X8}  ");
                        for (int columnOffset = 0; columnOffset < 16; columnOffset++)
                        {
                            if (rowOffset + columnOffset < writer.Position)
                                op4Hex.Append($"{writer.ToArray()[rowOffset + columnOffset]:X2} ");
                            else
                                op4Hex.Append("   ");
                        }
                        op4Hex.Append(" |");
                        for (int columnOffset = 0; columnOffset < 16 && rowOffset + columnOffset < writer.Position; columnOffset++)
                        {
                            byte byteValue = writer.ToArray()[rowOffset + columnOffset];
                            op4Hex.Append((byteValue >= 32 && byteValue < 127) ? (char)byteValue : '.');
                        }
                        op4Hex.AppendLine("|");
                    }
                    Debug.LogError(op4Hex.ToString());
                    Debug.LogError("[OP4-HEX]");
                }
                Debug.LogError("[OP4] state=complete");
                Debug.LogError($"[OP4-COMPLETE] Manipulators component: {opBytes} bytes");
                Debug.LogError($"[CUMULATIVE-OP4] Total after Op4: {writer.Position} bytes");


                Debug.LogError("[OP5] component=Equipment state=start");
                beforeOp = writer.Position;

                var equipment = avatar.Children.FirstOrDefault(c => c.GCClass == "avatar.base.Equipment");

                if (equipment == null)
                {
                    Debug.LogError(" Equipment not found in avatar children!");
                    return;
                }

                Debug.LogError($"[OP5] equipmentId={equipment.Id} children={equipment.Children?.Count ?? 0}");
                TrackComponent(conn.ConnId.ToString(), (ushort)equipment.Id, "Equipment");

                int op5HeaderStart = writer.Position;
                writer.WriteByte(0x32);
                writer.WriteUInt16((ushort)avatar.Id);
                writer.WriteUInt16((ushort)equipment.Id);
                WriteGCType(writer, "avatar.base.Equipment");
                writer.WriteByte(0x01);
                int op5HeaderBytes = writer.Position - op5HeaderStart;
                Debug.LogError($"[OP5-HEADER] Component header written ({op5HeaderBytes} bytes)");

                var equippedItems = equipment.Children?.Where(c =>
      c.DFCClass == "Armor" ||
      c.DFCClass == "MeleeWeapon" ||
      c.DFCClass == "RangedWeapon" ||
      c.DFCClass == "Item"
  ).ToList() ?? new List<GCObject>();

                var validItems = new List<GCObject>();
                foreach (var item in equippedItems)
                {
                    ItemData itemData = DatabaseLoader.FindItem(item.GCClass);
                    if (itemData != null)
                    {
                        validItems.Add(item);
                        Debug.LogError($"[OP5-VALID] Item '{item.GCClass}' found in ItemDatabase - will write");
                    }
                    else
                    {
                        string lk = item.GCClass.ToLowerInvariant();
                        if (lk.StartsWith("items.pal.")) lk = lk.Substring(10);
                        if (DungeonRunners.Gameplay.MerchantRuntime.HasAuthoredMerchantModSlots(lk))
                        {
                            validItems.Add(item);
                            Debug.LogError($"[OP5-VALID] Item '{item.GCClass}' found in authored slot catalog - will write");
                        }
                        else
                        {
                            var generalItem = DatabaseLoader.FindGeneralItem(item.GCClass);
                            if (generalItem != null)
                            {
                                validItems.Add(item);
                                Debug.LogError($"[OP5-VALID] Item '{item.GCClass}' found in GeneralItemDatabase (ring/amulet) - will write");
                            }
                            else
                            {
                                Debug.LogError($"[OP5-SKIP] Item '{item.GCClass}' NOT in any database - SKIPPING!");
                            }
                        }
                    }
                }

                byte itemCount = (byte)validItems.Count;

                int op5CountBytePos = writer.Position;
                writer.WriteByte(itemCount);
                Debug.LogError($"[OP5-COUNT] Writing {itemCount} VALID equipped items at position {op5CountBytePos}");

                foreach (var item in validItems)
                {
                    int itemStart = writer.Position;

                    Debug.LogError($"");
                    Debug.LogError($"[OP5-ITEM] ");
                    Debug.LogError($"[OP5-ITEM] Writing FULL DATA for: {item.GCClass}");
                    Debug.LogError($"[OP5-ITEM]  class: {item.DFCClass}");
                    Debug.LogError($"[OP5-ITEM] ");

                    writer.WriteByte(0xFF);
                    writer.WriteCString(item.GetPacketGCClass());

                    uint equipSlot = 0;
                    string gcLower = item.GCClass.ToLower();

                    bool isMythic = item.GCClass.IndexOf("MythicPAL", StringComparison.OrdinalIgnoreCase) >= 0
                    || DungeonRunners.Gameplay.MerchantRuntime.HasAuthoredMerchantModSlots(
                        gcLower.StartsWith("items.pal.") ? gcLower.Substring(10) : gcLower);
                    bool isPartialbuilt = gcLower.Contains("partialbuilt");
                    bool isPrebuilt = gcLower.Contains("prebuilt");
                    bool isGeneratedboss = gcLower.Contains("generatedboss");
                    bool hasMythicSeasonal = gcLower.Contains("mythicseasonal");
                    bool hasMythicInName = gcLower.Contains("mythic") && !hasMythicSeasonal;
                    bool hasSeasonal = gcLower.Contains("seasonal");
                    bool hasWishingwell = gcLower.Contains("wishingwell");
                    bool hasUnique = gcLower.Contains("unique");
                    bool hasJustSeasonal = hasSeasonal && !gcLower.Contains("mythic") && !hasUnique;
                    bool hasUniqueSeasonal = hasUnique && hasSeasonal;

                    if (gcLower.Contains("helm"))
                        equipSlot = 5;
                    else if (gcLower.Contains("shoulder") || gcLower.Contains("pauldron"))
                        equipSlot = 8;
                    else if (gcLower.Contains("armor") || gcLower.Contains("chest") || gcLower.Contains("body"))
                        equipSlot = 6;
                    else if (gcLower.Contains("gloves"))
                        equipSlot = 2;
                    else if (gcLower.Contains("boots"))
                        equipSlot = 7;
                    else if (gcLower.Contains("shield"))
                        equipSlot = 11;
                    else if (gcLower.Contains("ring"))
                    {
                        var existingRing = GetEquippedItem(conn.ConnId.ToString(), 3);
                        if (existingRing == null)
                            equipSlot = 3;
                        else
                            equipSlot = 4;
                    }
                    else if (gcLower.Contains("amulet"))
                        equipSlot = 1;
                    else if (gcLower.Contains("weapon") || gcLower.Contains("sword") || gcLower.Contains("axe") || gcLower.Contains("mace") || gcLower.Contains("staff") || gcLower.Contains("pick") || gcLower.Contains("bow") || gcLower.Contains("gun") || gcLower.Contains("crossbow") || gcLower.Contains("cannon") || gcLower.Contains("club") || gcLower.Contains("katana") || gcLower.Contains("polearm"))
                    {
                        if (item.TargetSlot.HasValue && item.TargetSlot.Value == 11)
                            equipSlot = 11;
                        else
                            equipSlot = 10;
                    }

                    Debug.LogError($"[OP5-ITEM] Equipment slot: {equipSlot}, isMythic: {isMythic}, isPartialbuilt: {isPartialbuilt}, isPrebuilt: {isPrebuilt}, isGeneratedboss: {isGeneratedboss}, hasMythicSeasonal: {hasMythicSeasonal}, hasMythicInName: {hasMythicInName}, hasSeasonal: {hasSeasonal}, hasWishingwell: {hasWishingwell}, hasUnique: {hasUnique}, hasJustSeasonal: {hasJustSeasonal}, hasUniqueSeasonal: {hasUniqueSeasonal}");
                    TrackEquippedItem(conn.ConnId.ToString(), equipSlot, item);

                    ItemData itemData = DatabaseLoader.FindItem(item.GCClass);

                    int modCount;
                    if (item.DFCClass == "Item" || gcLower.Contains("ring") || gcLower.Contains("amulet"))
                    {
                        bool isAmulet = gcLower.Contains("amulet");

                        if (isAmulet)
                        {
                            bool isMythicAmulet = gcLower.Contains("mythic");
                            int amuletLevel = isMythicAmulet ? (playerState.Level + 3) : item.GetItemRequiredLevel();


                            int amuletModCount;
                            List<string> amuletMods;

                            if (isMythicAmulet)
                            {
                                amuletMods = DatabaseLoader.GetAmuletModifiers(item.GCClass);
                                amuletModCount = amuletMods.Count;
                            }
                            else
                            {
                                if (gcLower.Contains("amuletpal.amulet"))
                                {
                                    amuletModCount = 1;
                                    amuletMods = new List<string>();
                                }
                                else if (gcLower.Contains("questamuletpal") || gcLower.Contains("uniqueamuletpal"))
                                {
                                    amuletModCount = 2;
                                    amuletMods = new List<string>();
                                }
                                else
                                {
                                    amuletModCount = 1;
                                    amuletMods = new List<string>();
                                }
                            }

                            Debug.LogError($"[OP5-AMULET] GCClass: {item.GCClass}, Slot: {equipSlot}, Level: {amuletLevel}, ModCount: {amuletModCount}, Mythic: {isMythicAmulet}");

                            writer.WriteUInt32(equipSlot);

                            writer.WriteByte(0x00);

                            writer.WriteByte(0x00);

                            writer.WriteByte(0x01);

                            writer.WriteByte((byte)amuletLevel);

                            if (amuletModCount <= 0)
                            {
                                continue;
                            }

                            if (isMythicAmulet)
                            {
                                writer.WriteByte(0x00);
                            }

                            for (int modIndex = 0; modIndex < amuletModCount; modIndex++)
                            {
                                writer.WriteByte(0x00);
                            }

                            writer.WriteByte((byte)amuletMods.Count);

                            foreach (var mod in amuletMods)
                            {
                                writer.WriteByte(0xFF);
                                writer.WriteCString(mod);
                                writer.WriteByte(0x03);
                                writer.WriteByte(0x15);
                                writer.WriteUInt32(0x11111111);
                            }

                            byte[] amuletBytes = writer.ToArray();
                            int amuletDataLen = writer.Position - itemStart;
                            Debug.LogError($"[OP5-AMULET-HEX] Wrote {amuletDataLen} bytes for {item.GCClass}:");
                            Debug.LogError($"[OP5-AMULET-HEX] {BitConverter.ToString(amuletBytes, itemStart, amuletDataLen)}");

                            continue;
                        }
                        else
                        {
                            bool isMythicRing = gcLower.Contains("mythic");
                            int ringLevel = isMythicRing ? (playerState.Level + 3) : item.GetItemRequiredLevel();
                            int ringModCount;
                            List<string> ringMods;

                            if (isMythicRing)
                            {
                                ringMods = DatabaseLoader.GetRingModifiers(item.GCClass);
                                ringModCount = ringMods.Count;
                            }
                            else if (gcLower.Contains("ringpal.ring"))
                            {
                                ringModCount = 1;
                                ringMods = new List<string>();
                            }
                            else if (gcLower.Contains("questringpal"))
                            {
                                ringModCount = DatabaseLoader.GetRingModSlotCount(item.GCClass);
                                ringMods = new List<string>();
                            }
                            else if (gcLower.Contains("uniqueringpal"))
                            {
                                ringModCount = 1;
                                ringMods = new List<string>();
                            }
                            else
                            {
                                ringModCount = 1;
                                ringMods = new List<string>();
                            }

                            Debug.LogError($"[OP5-RING] GCClass: {item.GCClass}, Slot: {equipSlot}, Level: {ringLevel}, ModCount: {ringModCount}, Mythic: {isMythicRing}");

                            writer.WriteUInt32(equipSlot);
                            writer.WriteByte(0x00);
                            writer.WriteByte(0x00);
                            writer.WriteByte(0x01);
                            writer.WriteByte((byte)ringLevel);

                            if (ringModCount <= 0)
                            {
                                continue;
                            }

                            if (isMythicRing)
                            {
                                writer.WriteByte(0x00);
                            }

                            for (int modIndex = 0; modIndex < ringModCount; modIndex++)
                            {
                                writer.WriteByte(0x00);
                            }

                            writer.WriteByte((byte)ringMods.Count);

                            foreach (var mod in ringMods)
                            {
                                writer.WriteByte(0xFF);
                                writer.WriteCString(mod);
                                writer.WriteByte(0x03);
                                writer.WriteByte(0x15);
                                writer.WriteUInt32(0x11111111);
                            }

                            byte[] ringBytes = writer.ToArray();
                            int ringDataLen = writer.Position - itemStart;
                            Debug.LogError($"[OP5-RING-HEX] Wrote {ringDataLen} bytes for {item.GCClass}:");
                            Debug.LogError($"[OP5-RING-HEX] {BitConverter.ToString(ringBytes, itemStart, ringDataLen)}");

                            continue;
                        }
                    }

                    int gcLookupModCount = DungeonRunners.Gameplay.MerchantRuntime.GetOP5ModCount(item.GCClass);
                    if (gcLookupModCount >= 0)
                    {
                        modCount = gcLookupModCount;
                        Debug.LogError($"[OP5] GC LOOKUP: {item.GCClass} -> modCount={modCount}");
                    }
                    else
                    {
                        modCount = itemData?.modCount ?? 3;
                        if (gcLower.Contains("chain") && !gcLower.Contains("shield"))
                            modCount = 1;
                        Debug.LogError($"[OP5] source=db modCount={modCount}");
                    }

                    Debug.LogError($"[OP5-ITEM] ModCount: {modCount} (isMythic={isMythic}, isGeneratedboss={isGeneratedboss}, dbModCount={itemData?.modCount})");

                    int equipDataStart = writer.Position;

                    writer.WriteUInt32(equipSlot);
                    writer.WriteByte(0);
                    writer.WriteByte(0);
                    writer.WriteByte(1);

                    int itemLevel = isMythic ? (playerState.Level + 3) : ItemData.GetRequiredLevelFromGCClass(item.GCClass);
                    writer.WriteByte((byte)itemLevel);
                    Debug.LogError($"[OP5-ITEM] Item level: {itemLevel} (isMythic={isMythic})");

                    bool writeFlag = false;

                    if (isMythic && !isPartialbuilt)
                    {
                        writeFlag = true;
                    }
                    else if (isPartialbuilt && hasMythicSeasonal)
                    {
                        writeFlag = true;
                    }
                    else if (isPartialbuilt && hasUniqueSeasonal && !gcLower.Contains("fighter"))
                    {
                        writeFlag = true;
                    }
                    else if (isPrebuilt && hasMythicInName)
                    {
                        writeFlag = true;
                    }
                    else if (isPrebuilt && hasMythicSeasonal)
                    {
                        writeFlag = true;
                    }
                    else if (isPrebuilt && hasWishingwell && !gcLower.Contains("fighter") && !gcLower.Contains("helm"))
                    {
                        writeFlag = true;
                    }

                    if (!writeFlag && isMythic && !gcLower.Contains("mythicpal"))
                    {
                        writeFlag = true;
                    }

                    if (writeFlag)
                    {
                        if ((gcLower.Contains("scale") && !gcLower.Contains("helm") && !gcLower.Contains("armor") && !gcLower.Contains("100") && !gcLower.Contains("101")) ||
                            (gcLower.Contains("splint") && !gcLower.Contains("shoulder") && !gcLower.Contains("armor") && !gcLower.Contains("100") && !gcLower.Contains("101")))
                        {
                            Debug.LogError($"[OP5-ITEM] SKIPPED flag for scale/splint");
                            writeFlag = false;
                            if (gcLookupModCount >= 0) modCount++;
                        }
                    }

                    bool isWeapon = (item.DFCClass == "MeleeWeapon" || item.DFCClass == "RangedWeapon");
                    if (writeFlag && (!isWeapon || isMythic))
                    {
                        writer.WriteByte(0x00);
                        Debug.LogError($"[EQUIP-WRITEINIT] Wrote FLAG byte (armor only)");
                    }

                    Debug.LogError($"[OP5-ITEM] Basic data written (slot, position, count, level, flags)");

                    int modSlotsStart = writer.Position;
                    for (int modSlotIndex = 0; modSlotIndex < modCount; modSlotIndex++)
                    {
                        writer.WriteByte(0x00);
                    }
                    Debug.LogError($"[OP5-MODS] Wrote {modCount} mod slot bytes (0x00 each) at position {modSlotsStart}");

                    if (gcLookupModCount >= 0)
                    {
                        writer.WriteByte(0x00);
                        Debug.LogError($"[OP5-MODS] GC lookup item - no ScaleMod (mods baked in GC class)");
                    }
                    else
                    {
                        int op5Tier = RPGSettings.GetTierFromGcType(item.GCClass);
                        var op5Rarity = RPGSettings.GetRarityFromTier(op5Tier);
                        if (op5Rarity == ItemRarity.Normal)
                        {
                            int effective = item.GetEffectiveRarity();
                            if (effective > 0 && effective < 5)
                                op5Rarity = (ItemRarity)effective;
                        }
                        var op5WireMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetItemWireMods(item.GCClass);
                        if (op5WireMods.Count == 0)
                            op5WireMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetWrapperIGWireMods(item.GCClass, op5Rarity.ToString());

                        if (DungeonRunners.Data.ItemStatDatabase.PathBEnabled && op5WireMods.Count > 0)
                        {
                            var op5Hashes = new List<uint>();
                            var op5Emitted = new List<string>();
                            var op5Skipped = new List<string>();
                            foreach (var (slot, modRef) in op5WireMods.OrderBy(w => w.Slot))
                            {
                                uint h = DungeonRunners.Data.ItemStatDatabase.Instance.GetGCClassHash(modRef);
                                if (h != 0) { op5Hashes.Add(h); op5Emitted.Add($"{modRef}#0x{h:X8}"); }
                                else op5Skipped.Add(modRef);
                            }
                            writer.WriteByte((byte)op5Hashes.Count);
                            foreach (uint h in op5Hashes)
                            {
                                writer.WriteByte(0x04);
                                writer.WriteUInt32(h);
                                writer.WriteByte(0x00);
                            }
                            Debug.LogError($"[ITEM-WIRE-MODS] context=op5 kind=armor item={item.GCClass} rarity={op5Rarity} storedRarity={item.StoredRarity} effective={item.GetEffectiveRarity()} mods={op5Hashes.Count}/{op5WireMods.Count} phase1ModCount={modCount} emitted=[{string.Join(" | ", op5Emitted)}] skipped=[{string.Join(",", op5Skipped)}]");
                        }
                        else if (op5Rarity == ItemRarity.Normal)
                        {
                            writer.WriteByte(0x00);
                            Debug.LogError($"[OP5-MODS] Normal item - no ScaleMod");
                        }
                        else
                        {
                            writer.WriteByte(0x01);
                            int itemModStart = writer.Position;
                            writer.WriteByte(0xFF);
                            string modifierClass = RPGSettings.GetDeterministicScaleMod(item.GCClass, op5Rarity);
                            Debug.LogError($"[OP5-MODS] Item '{item.GCClass}' -> rarity={op5Rarity} Modifier '{modifierClass}' (deterministic)");
                            writer.WriteCString(modifierClass);
                            writer.WriteByte(0x03);
                            writer.WriteByte(0x15);
                            writer.WriteUInt32(0x11111111);
                            int itemModBytes = writer.Position - itemModStart;
                            Debug.LogError($"[OP5-MODS] ItemModifier written ({itemModBytes} bytes)");
                        }
                    }

                    int itemBytes = writer.Position - itemStart;
                    Debug.LogError($"[OP5-ITEM] gcClass='{item.GCClass}' state=complete");
                    Debug.LogError($"[OP5-ITEM]    Slot: {equipSlot}");
                    Debug.LogError($"[OP5-ITEM]    ModCount: {modCount}");
                    Debug.LogError($"[OP5-ITEM]    Total Bytes: {itemBytes}");
                    if (VerbosePacketLogging) Debug.LogError($"[OP5-ITEM]    Full hex: {BitConverter.ToString(writer.ToArray(), itemStart, itemBytes)}");
                }

                opBytes = (int)(writer.Position - beforeOp);
                Debug.LogError($"[OP5-COMPLETE] component=Equipment bytes={opBytes} equippedItems={itemCount} state=complete");

                if (VerbosePacketLogging)
                {
                    Debug.LogError($"");
                    Debug.LogError("[OP5-HEX]");
                    Debug.LogError("[OP5-HEX] operation=5 complete");
                    StringBuilder op5Hex = new StringBuilder();
                    for (int rowOffset = beforeOp; rowOffset < writer.Position; rowOffset += 16)
                    {
                        op5Hex.Append($"{rowOffset:X8}  ");
                        for (int columnOffset = 0; columnOffset < 16; columnOffset++)
                        {
                            if (rowOffset + columnOffset < writer.Position)
                                op5Hex.Append($"{writer.ToArray()[rowOffset + columnOffset]:X2} ");
                            else
                                op5Hex.Append("   ");
                        }
                        op5Hex.Append(" |");
                        for (int columnOffset = 0; columnOffset < 16 && rowOffset + columnOffset < writer.Position; columnOffset++)
                        {
                            byte byteValue = writer.ToArray()[rowOffset + columnOffset];
                            op5Hex.Append((byteValue >= 32 && byteValue < 127) ? (char)byteValue : '.');
                        }
                        op5Hex.AppendLine("|");
                    }
                    Debug.LogError(op5Hex.ToString());
                    Debug.LogError("[OP5-HEX]");
                }
                Debug.LogError("[OP5] state=complete");
                Debug.LogError($"[OP5-COMPLETE] component=Equipment bytes={opBytes} equippedItems={itemCount} state=complete");
                Debug.LogError($"[CUMULATIVE-OP5] Total after Op5: {writer.Position} bytes");
                Debug.LogError("[OP6] component=QuestManager state=start");
                beforeOp = writer.Position;

                var questManager = player.Children.FirstOrDefault(c => c.GCClass == "QuestManager");
                if (questManager == null)
                {
                    Debug.LogError("[OP6] component=QuestManager state=missing action=create");
                    questManager = new GCObject
                    {
                        GCClass = "QuestManager",
                        DFCClass = "QuestManager",
                        Name = "QuestManager",
                        Id = _nextEntityId++
                    };
                    player.AddChild(questManager);
                    Debug.LogError("[OP6] component=QuestManager state=created");
                }

                conn.QuestManagerId = (ushort)questManager.Id;
                Debug.LogError($"[OP6] questManagerId={questManager.Id}");

                QuestManager.Instance.WriteQuestManagerComponent(
                    writer,
                    conn.ConnId.ToString(),
                    (ushort)player.Id,
                    (ushort)questManager.Id,
                    (w, gcType, preserveCase) => WriteGCType(w, gcType, preserveCase),
                    conn
                );

                opBytes = (int)(writer.Position - beforeOp);
                Debug.LogError($"[OP6] component=QuestManager bytes={opBytes} state=complete");

                if (VerbosePacketLogging)
                {
                    Debug.LogError("[OP6-HEX]");
                    StringBuilder opHex = new StringBuilder();
                    for (int rowOffset = beforeOp; rowOffset < writer.Position; rowOffset += 16)
                    {
                        opHex.Append($"{rowOffset:X8}  ");
                        for (int columnOffset = 0; columnOffset < 16; columnOffset++)
                        {
                            if (rowOffset + columnOffset < writer.Position)
                                opHex.Append($"{writer.ToArray()[rowOffset + columnOffset]:X2} ");
                            else
                                opHex.Append("   ");
                        }
                        opHex.Append(" |");
                        for (int columnOffset = 0; columnOffset < 16 && rowOffset + columnOffset < writer.Position; columnOffset++)
                        {
                            byte byteValue = writer.ToArray()[rowOffset + columnOffset];
                            opHex.Append((byteValue >= 32 && byteValue < 127) ? (char)byteValue : '.');
                        }
                        opHex.AppendLine("|");
                    }
                    Debug.LogError(opHex.ToString());
                    Debug.LogError("[OP6-HEX]");
                }
                Debug.LogError($"[CUMULATIVE-OP6] Total after Op6: {writer.Position} bytes");


                Debug.LogError("[OP7] component=DialogManager state=start");
                beforeOp = writer.Position;

                var dialogManager = player.Children.FirstOrDefault(c => c.GCClass == "DialogManager");
                if (dialogManager == null)
                {
                    Debug.LogError("[OP7] component=DialogManager state=missing action=create");
                    dialogManager = new GCObject();
                    dialogManager.GCClass = "DialogManager";
                    dialogManager.DFCClass = "DialogManager";
                    dialogManager.Name = "DialogManager";
                    dialogManager.Id = _nextEntityId++;
                    player.AddChild(dialogManager);
                    Debug.LogError("[OP7] component=DialogManager state=created");
                }

                Debug.LogError($"[OP7] dialogManagerId={dialogManager.Id}");

                writer.WriteByte(0x32);
                writer.WriteUInt16((ushort)player.Id);
                writer.WriteUInt16((ushort)dialogManager.Id);
                WriteGCType(writer, "DialogManager");
                writer.WriteByte(0x01);

                opBytes = (int)(writer.Position - beforeOp);
                Debug.LogError($"[OP7] component=DialogManager bytes={opBytes} data=none state=complete");



                conn.DialogManagerId = (ushort)dialogManager.Id;
                Debug.LogError($"[OP7] component=DialogManager trackedId={dialogManager.Id}");

                opBytes = (int)(writer.Position - beforeOp);
                Debug.LogError($"[OP7] component=DialogManager bytes={opBytes} data=none state=complete");


                if (VerbosePacketLogging)
                {
                    Debug.LogError("[OP7-HEX]");
                    Debug.LogError("[OP7-HEX] state=complete");
                    StringBuilder opHex7 = new StringBuilder();
                    for (int rowOffset = beforeOp; rowOffset < writer.Position; rowOffset += 16)
                    {
                        opHex7.Append($"{rowOffset:X8}  ");
                        for (int columnOffset = 0; columnOffset < 16; columnOffset++)
                        {
                            if (rowOffset + columnOffset < writer.Position)
                                opHex7.Append($"{writer.ToArray()[rowOffset + columnOffset]:X2} ");
                            else
                                opHex7.Append("   ");
                        }
                        opHex7.Append(" |");
                        for (int columnOffset = 0; columnOffset < 16 && rowOffset + columnOffset < writer.Position; columnOffset++)
                        {
                            byte byteValue = writer.ToArray()[rowOffset + columnOffset];
                            opHex7.Append((byteValue >= 32 && byteValue < 127) ? (char)byteValue : '.');
                        }
                        opHex7.AppendLine("|");
                    }
                    Debug.LogError(opHex7.ToString());
                    Debug.LogError("[OP7-HEX]");
                }
                Debug.LogError("[OP7] component=DialogManager data=none state=complete");
                Debug.LogError($"[CUMULATIVE-OP7] Total after Op7: {writer.Position} bytes");


                Debug.LogError("[OP8] component=UnitContainer state=start");
                beforeOp = writer.Position;

                var unitContainer = avatar.Children.FirstOrDefault(c => c.GCClass == "UnitContainer");
                if (unitContainer == null)
                {
                    Debug.LogError("[OP8] component=UnitContainer state=missing");
                    return;
                }
                Debug.LogError($"[OP8] unitContainerId={unitContainer.Id}");
                TrackComponent(conn.ConnId.ToString(), (ushort)unitContainer.Id, "UnitContainer");
                conn.UnitContainerId = (ushort)unitContainer.Id;
                writer.WriteByte(0x32);
                writer.WriteUInt16((ushort)avatar.Id);
                writer.WriteUInt16((ushort)unitContainer.Id);
                WriteGCType(writer, "UnitContainer");
                writer.WriteByte(0x01);

                writer.WriteUInt32(0);
                writer.WriteUInt32(savedChar.gold);

                Debug.LogError($"[UNITCONTAINER] gold={savedChar.gold}");

                var mainInventory = unitContainer.Children.FirstOrDefault(c => c.GCClass == "avatar.base.Inventory");
                var bankPage1 = unitContainer.Children.FirstOrDefault(c => c.GCClass == "avatar.base.Bank");
                var bankPage2 = unitContainer.Children.FirstOrDefault(c => c.GCClass == "avatar.base.Bank2");
                var bankPage3 = unitContainer.Children.FirstOrDefault(c => c.GCClass == "avatar.base.Bank3");
                var bankPage4 = unitContainer.Children.FirstOrDefault(c => c.GCClass == "avatar.base.Bank4");
                var bankPage5 = unitContainer.Children.FirstOrDefault(c => c.GCClass == "avatar.base.Bank5");
                var bankPage6 = unitContainer.Children.FirstOrDefault(c => c.GCClass == "avatar.base.Bank6");
                var bankPage7 = unitContainer.Children.FirstOrDefault(c => c.GCClass == "avatar.base.Bank7");
                var tradeInventory = unitContainer.Children.FirstOrDefault(c => c.GCClass == "avatar.base.TradeInventory");

                if (mainInventory == null || bankPage1 == null || tradeInventory == null
                    || bankPage2 == null || bankPage3 == null || bankPage4 == null
                    || bankPage5 == null || bankPage6 == null || bankPage7 == null)
                {
                    Debug.LogError(" Missing required inventories!");
                    return;
                }

                var inventoriesToWrite = new[] {
            (inventory: mainInventory, id: (byte)0x0B, name: "Inventory"),
            (inventory: bankPage1, id: (byte)0x0C, name: "Bank"),
            (inventory: tradeInventory, id: (byte)0x0D, name: "TradeInventory"),
            (inventory: bankPage2, id: (byte)0x0E, name: "Bank2"),
            (inventory: bankPage3, id: (byte)0x0F, name: "Bank3"),
            (inventory: bankPage4, id: (byte)0x10, name: "Bank4"),
            (inventory: bankPage5, id: (byte)0x11, name: "Bank5"),
            (inventory: bankPage6, id: (byte)0x12, name: "Bank6"),
            (inventory: bankPage7, id: (byte)0x13, name: "Bank7")
        };

                writer.WriteByte((byte)inventoriesToWrite.Length);

                {
                    string clearConnId = conn.ConnId.ToString();
                    byte[] allContainers = { 0x0B, 0x0C, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13 };
                    foreach (byte containerId in allContainers)
                    {
                        string key = InvKey(clearConnId, containerId);
                        if (_playerInventoryItems.ContainsKey(key))
                            _playerInventoryItems[key].Clear();
                        if (_inventoryStackCounts.ContainsKey(key))
                            _inventoryStackCounts[key].Clear();
                        if (_occupiedInventorySlots.ContainsKey(key))
                            _occupiedInventorySlots[key].Clear();
                    }
                    if (_inventorySlotCounters.ContainsKey(clearConnId))
                        _inventorySlotCounters.Remove(clearConnId);

                    if (savedChar.inventory != null)
                    {
                        foreach (var bpItem in savedChar.inventory)
                        {
                            if (bpItem.buyPrice > 0)
                                DungeonRunners.Gameplay.MerchantRuntime.SetBuyPrice(clearConnId, bpItem.gcClass, bpItem.buyPrice);
                        }
                    }
                }

                uint globalItemIndex = 1;

                foreach (var inv in inventoriesToWrite)
                {
                    Debug.LogError($"     Writing {inv.name}");
                    WriteGCType(writer, inv.inventory.GCClass);
                    writer.WriteByte(inv.id);
                    writer.WriteByte(0x01);

                    bool isPersistedContainer = (inv.id == 0x0B || inv.id == 0x0C || (inv.id >= 0x0E && inv.id <= 0x13));
                    var containerItems = (isPersistedContainer && savedChar.inventory != null)
                        ? savedChar.inventory.FindAll(i => i.containerId == inv.id)
                        : new List<SavedInventoryItem>();

                    if (containerItems.Count > 0)
                    {
                        string clearConnId = conn.ConnId.ToString();
                        writer.WriteByte((byte)containerItems.Count);
                        Debug.LogError($"[UNITCONTAINER] gcType={inv.inventory.GCClass} id=0x{inv.id:X2} items={containerItems.Count}");

                        foreach (var item in containerItems)
                        {
                            uint itemIndex = globalItemIndex++;
                            string gcTypeToSend = item.gcClass.ToLowerInvariant();
                            string packetGcType = GCObject.GetPacketGCClassFor(item.gcClass);
                            var itemData = DatabaseLoader.FindItem(gcTypeToSend);
                            bool isInvChain = gcTypeToSend.Contains("chain") && !gcTypeToSend.Contains("shield");
                            bool isInvAmulet = gcTypeToSend.Contains("amulet");
                            bool isInvRing = gcTypeToSend.Contains("ring");
                            int modSlots;
                            if (isInvChain)
                                modSlots = 1;
                            else if (isInvAmulet || isInvRing)
                                modSlots = 1;
                            else if (itemData != null)
                                modSlots = itemData.modCount;
                            else
                                modSlots = 2;
                            int itemLevel;
                            if (item.storedLevel >= 0)
                            {
                                itemLevel = item.storedLevel;
                            }
                            else
                            {
                                bool isInvMythic = gcTypeToSend.IndexOf("mythicpal", StringComparison.OrdinalIgnoreCase) >= 0
                                    || DungeonRunners.Gameplay.MerchantRuntime.HasAuthoredMerchantModSlots(
                                        gcTypeToSend.StartsWith("items.pal.") ? gcTypeToSend.Substring(10) : gcTypeToSend);
                                itemLevel = isInvMythic ? (playerState.Level + 3) : ItemData.GetRequiredLevelFromGCClass(gcTypeToSend);
                            }

                            bool isSimpleItem = gcTypeToSend.Contains("questitem")
                                             || gcTypeToSend.Contains("potion")
                                             || gcTypeToSend.Contains("consumable")
                                             || gcTypeToSend.Contains("townportal")
                                             || gcTypeToSend.Contains("scroll")
                                             || gcTypeToSend.Contains("skillbook")
                                             || gcTypeToSend.Contains("voucher");

                            int invItemStart = writer.Position;
                            writer.WriteByte(0xFF);
                            writer.WriteCString(packetGcType);
                            writer.WriteUInt32(itemIndex);
                            writer.WriteByte(item.x);
                            writer.WriteByte(item.y);
                            writer.WriteByte((byte)(item.count > 0 ? item.count : 1));
                            writer.WriteByte((byte)itemLevel);

                            if (isSimpleItem)
                            {
                                writer.WriteByte(0x00);
                                if (gcTypeToSend.Contains("dragonjuice") || gcTypeToSend.Contains("intbuff"))
                                    writer.WriteByte(0x00);
                                writer.WriteByte(0x00);
                                Debug.LogError($"        -> Simple item {itemIndex}: {gcTypeToSend} (no ScaleMod)");
                            }
                            else if ((gcTypeToSend.Contains("ring") || gcTypeToSend.Contains("amulet")) && gcTypeToSend.Contains("mythic"))
                            {
                                bool isAmuletJ = gcTypeToSend.Contains("amulet");
                                List<string> jMods = isAmuletJ
                                    ? DatabaseLoader.GetAmuletModifiers(item.gcClass)
                                    : DatabaseLoader.GetRingModifiers(item.gcClass);
                                int jModCount = jMods.Count;
                                writer.WriteByte(0x00);
                                for (int modIndex = 0; modIndex < jModCount; modIndex++) writer.WriteByte(0x00);
                                writer.WriteByte(0x00);
                                Debug.LogError($"        -> Mythic jewelry {itemIndex}: {gcTypeToSend} placeholders={jModCount + 1} phase2=0");
                            }
                            else
                            {
                                string lookupKey = gcTypeToSend;
                                if (lookupKey.StartsWith("items.pal.")) lookupKey = lookupKey.Substring(10);
                                int invMerchantSlots;
                                if (DungeonRunners.Gameplay.MerchantRuntime.TryGetAuthoredMerchantModSlots(lookupKey, out invMerchantSlots))
                                {
                                    for (int modIndex = 0; modIndex < invMerchantSlots; modIndex++)
                                    {
                                        writer.WriteByte(0x00);
                                    }
                                    writer.WriteByte(0x00);
                                    Debug.LogError($"        -> GC LOOKUP {itemIndex}: {gcTypeToSend} modSlots={invMerchantSlots} (no ScaleMod)");
                                }
                                else
                                {
                                    for (int modIndex = 0; modIndex < modSlots; modIndex++)
                                    {
                                        writer.WriteByte(0x00);
                                    }
                                    int tier = RPGSettings.GetTierFromGcType(item.gcClass);
                                    var itemRarity = RPGSettings.GetRarityFromTier(tier);
                                    if (itemRarity == ItemRarity.Normal)
                                    {
                                        if (item.rarity > 0 && item.rarity < 5)
                                            itemRarity = (ItemRarity)item.rarity;
                                        else
                                        {
                                            int detected = DungeonRunners.Data.GCObject.DetectRarityFromGCClass(item.gcClass);
                                            if (detected > 0 && detected < 5)
                                                itemRarity = (ItemRarity)detected;
                                        }
                                    }

                                    var restoreWireMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetItemWireMods(item.gcClass);
                                    if (restoreWireMods.Count == 0)
                                        restoreWireMods = DungeonRunners.Data.ItemStatDatabase.Instance.GetWrapperIGWireMods(item.gcClass, itemRarity.ToString());

                                    if (DungeonRunners.Data.ItemStatDatabase.PathBEnabled && restoreWireMods.Count > 0)
                                    {
                                        var hashes = new List<uint>();
                                        foreach (var (slot, modRef) in restoreWireMods.OrderBy(w => w.Slot))
                                        {
                                            uint h = DungeonRunners.Data.ItemStatDatabase.Instance.GetGCClassHash(modRef);
                                            if (h != 0) hashes.Add(h);
                                        }
                                        writer.WriteByte((byte)hashes.Count);
                                        foreach (uint h in hashes)
                                        {
                                            writer.WriteByte(0x04);
                                            writer.WriteUInt32(h);
                                            writer.WriteByte(0x00);
                                        }
                                        Debug.LogError($"[ITEM-WIRE-MODS] context=inventory index={itemIndex} item={gcTypeToSend} rarity={itemRarity} mods={hashes.Count} phase1ModSlots={modSlots}");
                                    }
                                    else if (itemRarity == ItemRarity.Normal)
                                    {
                                        writer.WriteByte(0x00);
                                        Debug.LogError($"        -> Equipment {itemIndex}: {gcTypeToSend} rarity=Normal (no ScaleMod)");
                                    }
                                    else
                                    {
                                        string scaleMod = RPGSettings.GetDeterministicScaleMod(item.gcClass, itemRarity);
                                        writer.WriteByte(0x01);
                                        writer.WriteByte(0xFF);
                                        writer.WriteCString(scaleMod);
                                        writer.WriteByte(0x03);
                                        writer.WriteByte(0x15);
                                        writer.WriteUInt32(0x11111111);
                                        Debug.LogError($"        -> Equipment {itemIndex}: {gcTypeToSend} rarity={itemRarity} scaleMod={scaleMod} (deterministic)");
                                    }
                                }
                            }

                            {
                                int invItemLen = writer.Position - invItemStart;
                                var invBuf = writer.ToArray();
                                var invHexSb = new System.Text.StringBuilder(invItemLen * 3);
                                for (int hi = 0; hi < invItemLen; hi++)
                                {
                                    if (hi > 0) invHexSb.Append(' ');
                                    invHexSb.Append(invBuf[invItemStart + hi].ToString("X2"));
                                }
                                Debug.LogError($"[INV-RESTORE] item#{itemIndex} gc={item.gcClass} rar={item.rarity} lv={item.storedLevel} bytes={invItemLen} hex={invHexSb}");
                            }

                            string gcClassLower = item.gcClass.ToLower();
                            string dfcClass = "Armor";
                            if (gcClassLower.Contains("questitem") || gcClassLower.Contains("consumable") || gcClassLower.Contains("townportal") || gcClassLower.Contains("ring") || gcClassLower.Contains("amulet") || gcClassLower.Contains("scroll") || gcClassLower.Contains("potion") || gcClassLower.Contains("skillbook") || gcClassLower.Contains("voucher"))
                                dfcClass = "Item";
                            else if (gcClassLower.Contains("sword") || gcClassLower.Contains("axe") || gcClassLower.Contains("mace") || gcClassLower.Contains("dagger") || gcClassLower.Contains("hammer") || gcClassLower.Contains("staff") || gcClassLower.Contains("spear"))
                                dfcClass = "MeleeWeapon";
                            else if (gcClassLower.Contains("bow") || gcClassLower.Contains("gun") || gcClassLower.Contains("crossbow"))
                                dfcClass = "RangedWeapon";
                            var gcObj = new GCObject { GCClass = item.gcClass, DFCClass = dfcClass };
                            gcObj.StoredRarity = item.rarity;
                            gcObj.StoredLevel = item.storedLevel;
                            TrackInventoryItem(conn.ConnId.ToString(), itemIndex, gcObj, item.x, item.y, inv.id);
                            var itemDims = DungeonRunners.Gameplay.MerchantRuntime.GetItemDimensions(item.gcClass);
                            int iw = itemDims.width, ih = itemDims.height;
                            OccupyInventorySlots(conn.ConnId.ToString(), item.x, item.y, iw, ih, inv.id);
                            SetStackCount(conn.ConnId.ToString(), itemIndex, item.count > 0 ? item.count : 1, inv.id);

                            Debug.LogError($"        -> Item {itemIndex} (container 0x{inv.id:X2}): {gcTypeToSend} at ({item.x},{item.y})");
                        }
                    }
                    else
                    {
                        writer.WriteByte(0x00);
                        Debug.LogError($"[UNITCONTAINER] gcType={inv.inventory.GCClass} id=0x{inv.id:X2} items=0");
                    }
                }

                {
                    string slotCounterKey = conn.ConnId.ToString();
                    uint floor = globalItemIndex > 100 ? globalItemIndex : 100;
                    _inventorySlotCounters[slotCounterKey] = floor;
                }

                writer.WriteByte(0x00);
                Debug.LogError($"     UnitContainer final byte: 0x00");

                opBytes = (int)(writer.Position - beforeOp);
                Debug.LogError($"[OP8] component=UnitContainer bytes={opBytes} state=complete");

                if (VerbosePacketLogging)
                {
                    Debug.LogError("[OP7-HEX]");
                    Debug.LogError("[OP7-HEX] state=complete");
                    StringBuilder opHex8 = new StringBuilder();
                    for (int rowOffset = beforeOp; rowOffset < writer.Position; rowOffset += 16)
                    {
                        opHex8.Append($"{rowOffset:X8}  ");
                        for (int columnOffset = 0; columnOffset < 16; columnOffset++)
                        {
                            if (rowOffset + columnOffset < writer.Position)
                                opHex8.Append($"{writer.ToArray()[rowOffset + columnOffset]:X2} ");
                            else
                                opHex8.Append("   ");
                        }
                        opHex8.Append(" |");
                        for (int columnOffset = 0; columnOffset < 16 && rowOffset + columnOffset < writer.Position; columnOffset++)
                        {
                            byte byteValue = writer.ToArray()[rowOffset + columnOffset];
                            opHex8.Append((byteValue >= 32 && byteValue < 127) ? (char)byteValue : '.');
                        }
                        opHex8.AppendLine("|");
                    }
                    Debug.LogError(opHex8.ToString());
                    Debug.LogError("[OP7-HEX]");
                }
                Debug.LogError("[OP8] component=UnitContainer state=complete");
                Debug.LogError($"[CUMULATIVE-OP8] Total after Op8: {writer.Position} bytes");

                Debug.LogError("[OP9] component=Modifiers state=start");
                beforeOp = writer.Position;

                var modifiers = avatar.Children.FirstOrDefault(c => c.GCClass == "Modifiers");
                if (modifiers == null)
                {
                    Debug.LogError(" Modifiers not found in avatar children!");
                    return;
                }

                Debug.LogError($"[OP9] modifiersId={modifiers.Id}");

                writer.WriteByte(0x32);
                writer.WriteUInt16((ushort)avatar.Id);
                writer.WriteUInt16((ushort)modifiers.Id);
                WriteGCType(writer, "Modifiers");
                writer.WriteByte(0x01);

                WritePassiveModifiersComponent(writer, null, (ushort)avatar.Id);

                opBytes = (int)(writer.Position - beforeOp);
                Debug.LogError($"[OP9] component=Modifiers passives=0 bytes={opBytes} state=complete");

                if (VerbosePacketLogging)
                {
                    Debug.LogError("[OP7-HEX]");
                    Debug.LogError("[OP7-HEX] state=complete");
                    StringBuilder opHex9 = new StringBuilder();
                    for (int rowOffset = beforeOp; rowOffset < writer.Position; rowOffset += 16)
                    {
                        opHex9.Append($"{rowOffset:X8}  ");
                        for (int columnOffset = 0; columnOffset < 16; columnOffset++)
                        {
                            if (rowOffset + columnOffset < writer.Position)
                                opHex9.Append($"{writer.ToArray()[rowOffset + columnOffset]:X2} ");
                            else
                                opHex9.Append("   ");
                        }
                        opHex9.Append(" |");
                        for (int columnOffset = 0; columnOffset < 16 && rowOffset + columnOffset < writer.Position; columnOffset++)
                        {
                            byte byteValue = writer.ToArray()[rowOffset + columnOffset];
                            opHex9.Append((byteValue >= 32 && byteValue < 127) ? (char)byteValue : '.');
                        }
                        opHex9.AppendLine("|");
                    }
                    Debug.LogError(opHex9.ToString());
                    Debug.LogError("[OP7-HEX]");
                }
                Debug.LogError("[OP9] component=Modifiers state=complete");
                Debug.LogError($"[CUMULATIVE-OP9] Total after Op9: {writer.Position} bytes");


                Debug.LogError("[OP10] component=Skills state=start");
                beforeOp = writer.Position;

                var skills = avatar.Children.FirstOrDefault(c => c.GCClass == "avatar.base.skills");
                if (skills == null)
                {
                    Debug.LogError("[OP10] component=Skills state=missing action=create");
                    skills = new GCObject();
                    skills.GCClass = "avatar.base.skills";
                    skills.DFCClass = "Skills";
                    skills.Name = "Skills";
                    skills.Id = _nextEntityId++;
                    avatar.AddChild(skills);
                    Debug.LogError("[OP10] component=Skills state=created");
                }

                Debug.LogError($"[OP10] skillsId={skills.Id}");

                _playerSkillsComponentId[spawnConnIdKey] = (ushort)skills.Id;
                _playerSkillSlots[spawnConnIdKey] = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

                int headerStart = writer.Position;
                writer.WriteByte(0x32);
                writer.WriteUInt16((ushort)avatar.Id);
                writer.WriteUInt16((ushort)skills.Id);
                WriteGCType(writer, "avatar.base.skills");
                writer.WriteByte(0x01);
                int headerEnd = writer.Position;
                Debug.LogError($"[OP10-HEADER] Component header: {headerEnd - headerStart} bytes (expected 26)");
                Debug.LogError($"[OP10-HEADER] Component header written");

                writer.WriteUInt32(0xFFFFFFFF);
                Debug.LogError($"[OP10-INIT] Wrote 0xFFFFFFFF");

                var manipulatorSkills = manipulators?.Children?.Where(c =>
                    c.DFCClass == "ActiveSkill"
                    && !(c.GCClass ?? "").StartsWith("skills.professions.", StringComparison.OrdinalIgnoreCase)).ToList()
                    ?? new List<GCObject>();

                var passiveSkills = new List<GCObject>();
                if (savedChar.skills != null)
                {
                    foreach (var skillId in savedChar.skills)
                    {
                        if (skillId.ToLower().Contains("passive") || skillId.ToLower().Contains("trait"))
                        {
                            passiveSkills.Add(new GCObject
                            {
                                DFCClass = "PassiveSkill",
                                GCClass = skillId,
                                Name = null
                            });
                            Debug.LogError($"[OP10-PASSIVE] Including passive: {skillId}");
                        }
                    }
                }

                int totalSkillCount = manipulatorSkills.Count + passiveSkills.Count;
                Debug.LogError($"[OP10-COUNT] Active skills: {manipulatorSkills.Count}, Passive skills: {passiveSkills.Count}, Total: {totalSkillCount}");
                Debug.LogError($"[OP10-COUNT] DatabaseLoader.Skills.Count = {DatabaseLoader.Skills.Count} (NOT USING!)");
                writer.WriteByte((byte)totalSkillCount);
                Debug.LogError($"[OP10-COUNT] Writing {totalSkillCount} skills (active + passive)");


                foreach (var skillObj in manipulatorSkills)
                {
                    int skillStart = writer.Position;
                    uint skillEntityId = 0;

                    WriteGCType(writer, skillObj.GCClass);
                    writer.WriteUInt32(skillEntityId);

                    int savedSkillLevel = savedChar.GetSkillLevel(skillObj.GCClass);
                    if (savedSkillLevel < 1) savedSkillLevel = 1;
                    writer.WriteByte((byte)savedSkillLevel);

                    _playerSkillSlots[spawnConnIdKey][skillObj.GCClass] = skillEntityId;
                    int dotIdx = skillObj.GCClass.LastIndexOf('.');
                    if (dotIdx >= 0)
                        _playerSkillSlots[spawnConnIdKey][skillObj.GCClass.Substring(dotIdx + 1)] = skillEntityId;

                    int skillBytes = writer.Position - skillStart;
                    Debug.LogError($"[OP10-SKILL] '{skillObj.GCClass}' entityId={skillEntityId} level={savedSkillLevel} ({skillBytes} bytes)");
                }
                foreach (var skillObj in passiveSkills)
                {
                    int skillStart = writer.Position;
                    uint skillEntityId = 0;

                    WriteGCType(writer, skillObj.GCClass);
                    writer.WriteUInt32(skillEntityId);

                    int savedSkillLevel = savedChar.GetSkillLevel(skillObj.GCClass);
                    if (savedSkillLevel < 1) savedSkillLevel = 1;
                    writer.WriteByte((byte)savedSkillLevel);

                    _playerSkillSlots[spawnConnIdKey][skillObj.GCClass] = skillEntityId;
                    int dotIdx = skillObj.GCClass.LastIndexOf('.');
                    if (dotIdx >= 0)
                        _playerSkillSlots[spawnConnIdKey][skillObj.GCClass.Substring(dotIdx + 1)] = skillEntityId;

                    int skillBytes = writer.Position - skillStart;
                    Debug.LogError($"[OP10-PASSIVE] '{skillObj.GCClass}' entityId={skillEntityId} level={savedSkillLevel} ({skillBytes} bytes)");
                }
                _playerNextSkillEntityId[spawnConnIdKey] = _nextEntityId;
                Debug.LogError($"[OP10-TRACK] Next skill entity ID for trainer: {_nextEntityId}");
                Debug.LogError("[OP10-DETAIL]");
                Debug.LogError($"[OP10-DETAIL] positionAfterLoop={writer.Position}");
                Debug.LogError("[OP10-DETAIL] lastBytes=30");
                if (VerbosePacketLogging && writer.Position >= 30)
                {
                    StringBuilder hexAfterLoop = new StringBuilder();
                    for (int byteOffset = writer.Position - 30; byteOffset < writer.Position; byteOffset++)
                    {
                        hexAfterLoop.Append($"{writer.ToArray()[byteOffset]:X2} ");
                    }
                    Debug.LogError(hexAfterLoop.ToString());
                }
                Debug.LogError("[OP10-DETAIL]");

                Debug.LogError($"[OP10-PROFESSION] Writing SkillProfession...");
                int professionStart = writer.Position;

                writer.WriteByte(0x01);
                Debug.LogError($"[OP10-DETAIL] afterWriteByte=0x01 position={writer.Position}");
                if (VerbosePacketLogging) Debug.LogError($"[OP10-DETAIL] bytes={BitConverter.ToString(writer.ToArray(), professionStart, writer.Position - professionStart)}");

                int beforeGCType = writer.Position;
                string profession = savedChar.className?.ToLower() switch
                {
                    "mage" or "warlock" => "skills.professions.Warlock",
                    "ranger" => "skills.professions.Ranger",
                    _ => "skills.professions.Warrior"
                };
                Debug.LogError($"[OP10-PROFESSION] savedChar.className = '{savedChar.className}', sending profession = '{profession}'");
                WriteGCType(writer, profession);
                int afterGCType = writer.Position;
                Debug.LogError($"[OP10-PROFESSION] {profession} written");

                opBytes = (int)(writer.Position - beforeOp);
                Debug.LogError($"[OP10-COMPLETE] Skills: {opBytes} bytes");


                Debug.LogError($"[OP10-DETAIL] afterWriteGCType position={writer.Position}");
                if (VerbosePacketLogging) Debug.LogError($"[OP10-DETAIL] gcTypeBytes={BitConverter.ToString(writer.ToArray(), beforeGCType, afterGCType - beforeGCType)}");

                if (VerbosePacketLogging) Debug.LogError($"[OP10-DETAIL] professionBytes={BitConverter.ToString(writer.ToArray(), professionStart, writer.Position - professionStart)}");

                Debug.LogError($"[OP10-PROFESSION] {profession} written");
                Debug.LogError($"[OP10-COMPLETE] Skills: {opBytes} bytes");
                Debug.LogError($"[CUMULATIVE-OP10] Total after Op10: {writer.Position} bytes");




                var resolvedSpawn = ResolvePlayerSpawnPosition(conn);
                float spawnX = resolvedSpawn.x;
                float spawnY = resolvedSpawn.y;
                float spawnZ = resolvedSpawn.z;
                conn.PlayerPosX = spawnX;
                conn.PlayerPosY = spawnY;
                conn.PlayerPosZ = spawnZ;
                conn.LivePlayerPosX = spawnX;
                conn.LivePlayerPosY = spawnY;
                conn.LivePlayerPosZ = spawnZ;
                conn.LivePlayerHeading = conn.PlayerHeading;
                conn.AvatarAggroSampleQueue.Clear();
                conn.AggroSamplePosX = spawnX;
                conn.AggroSamplePosY = spawnY;
                Debug.LogError($"[SPAWN-TRACK] Primed authoritative player position before OP12/combat registration pos=({spawnX:F1},{spawnY:F1},{spawnZ:F1}) heading={conn.PlayerHeading:F1} zone={conn.CurrentZoneName} instance={conn.InstanceId}");
                if (!DungeonMazeSpawner.IsProceduralZone(conn.CurrentZoneName) && _zones.TryGetValue(conn.CurrentZoneId, out Zone preOp12Zone) && preOp12Zone.spawnHeading != 0)
                    conn.PlayerHeading = preOp12Zone.spawnHeading;
                conn.LivePlayerHeading = conn.PlayerHeading;
                Debug.LogError("[OP12] action=init-avatar state=start");
                beforeOp = writer.Position;

                writer.WriteByte(0x02);
                writer.WriteUInt16((ushort)avatar.Id);
                Debug.LogError($"[OP12] opcode=0x02 avatarId={avatar.Id}");

                Debug.LogError($"[OP12] Before WorldEntity.WriteInit: position {writer.Position}");
                writer.WriteUInt32(0x04);
                Debug.LogError($"[OP12] WorldEntityFlags: 0x04");

                writer.WriteInt32((int)(spawnX * 256));
                Debug.LogError($"[OP12] After posX: position {writer.Position}");
                writer.WriteInt32((int)(spawnY * 256));
                Debug.LogError($"[OP12] After posY: position {writer.Position}");
                writer.WriteInt32((int)(spawnZ * 256));
                Debug.LogError($"[OP12] After posZ: position {writer.Position}");
                writer.WriteInt32((int)(conn.PlayerHeading * 256));
                Debug.LogError($"[OP12] After heading ({conn.PlayerHeading}): position {writer.Position}");
                writer.WriteByte(0x01);
                Debug.LogError($"[OP12] After initFlags: position {writer.Position}");

                writer.WriteUInt16(0);
                Debug.LogError($"[OP12] After Unk1Case (0, was avatar.Id): position {writer.Position}");

                Debug.LogError($"[OP12] Before Unit.WriteInit: position {writer.Position}");
                writer.WriteByte(0x07);
                Debug.LogError($"[OP12] After unitFlags: position {writer.Position}");

                byte unitLevel = (byte)Math.Max(1, Math.Min(255, playerState.Level));
                writer.WriteByte(unitLevel);
                Debug.LogError($"[OP12] After level={unitLevel} persistedLevel={savedChar.level}: position {writer.Position}");

                writer.WriteUInt16(0);
                Debug.LogError($"[OP12] After unk1: position {writer.Position}");
                writer.WriteUInt16(0);
                Debug.LogError($"[OP12] After unk2: position {writer.Position}");

                writer.WriteUInt16((ushort)player.Id);
                Debug.LogError($"[OP12] ownerId={player.Id} previousConnId={conn.ConnId}");


                uint avatarEntitySynchInfoHPValue = GetEntitySynchInfoHPValue(conn);
                uint avatarManaValue = playerState.CurrentManaWire;
                writer.WriteUInt32(avatarEntitySynchInfoHPValue);
                writer.WriteUInt32(avatarManaValue);
                Debug.LogError($"[OP12] HP/Mana init: hp={avatarEntitySynchInfoHPValue} mana={avatarManaValue}");

                Debug.LogError($"[OP12] Before Hero.WriteInit: position {writer.Position}");
                writer.WriteUInt32(playerState.Experience);
                Debug.LogError($"[OP12] After Hero XP({playerState.Experience}): position {writer.Position}");
                Debug.LogError($"[OP12] After Hero uint32(0): position {writer.Position}");

                int _pointsPerLevel = StatPointsPerLevel;
                int _totalAllocated = savedChar.statStrength + savedChar.statAgility
                                    + savedChar.statEndurance + savedChar.statIntellect;
                int _totalAvailable = (playerState.Level - 1) * _pointsPerLevel;
                int _statPtsRemaining = System.Math.Max(0, _totalAvailable - _totalAllocated);
                writer.WriteUInt16((ushort)savedChar.statStrength);
                writer.WriteUInt16((ushort)savedChar.statAgility);
                writer.WriteUInt16((ushort)savedChar.statEndurance);
                writer.WriteUInt16((ushort)savedChar.statIntellect);
                writer.WriteUInt16((ushort)_statPtsRemaining);

                int respecCooldownRemaining = 0;
                if (savedChar.lastRespecTime > 0)
                {
                    int nowSec = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    int elapsedSec = nowSec - savedChar.lastRespecTime;
                    respecCooldownRemaining = System.Math.Max(0, GetRespecCooldownSeconds() - elapsedSec);
                }
                writer.WriteUInt16((ushort)respecCooldownRemaining);
                Debug.LogError($"[OP12] Stats: STR={savedChar.statStrength} AGI={savedChar.statAgility} END={savedChar.statEndurance} INT={savedChar.statIntellect} pts={_statPtsRemaining} respecTimer={respecCooldownRemaining}s");

                writer.WriteUInt32((uint)savedChar.pvpWins);
                writer.WriteUInt32((uint)savedChar.pvpRating);

                Debug.LogError($"[OP12] Before Avatar.WriteInit: position {writer.Position}");
                writer.WriteByte(savedChar.face);
                writer.WriteByte(savedChar.hair);
                writer.WriteByte(savedChar.hairColor);
                Debug.LogError($"[OP12] Avatar appearance: Face={savedChar.face}, Hair={savedChar.hair}, HairColor={savedChar.hairColor}");
                Debug.LogError($"[OP12] After Avatar.WriteInit (3 bytes): position {writer.Position}");



                opBytes = (int)(writer.Position - beforeOp);
                Debug.LogError($"[OP12-COMPLETE] Init Avatar: {opBytes} bytes");
                Debug.LogError($"[CUMULATIVE-OP12] Total after Op12: {writer.Position} bytes");
                Debug.LogError(" Operation 12 complete - SPAWN PACKET ENDS HERE!");
                Debug.LogError($"[CUMULATIVE-OP12] Total after Op12: {writer.Position} bytes");
                Debug.LogError(" Operation 12 complete - SPAWN PACKET ENDS HERE!");

                Debug.LogError(" Operation 12 complete - SPAWN PACKET ENDS HERE!");

                Debug.LogError("[OP11] component=UnitBehavior state=start");
                beforeOp = writer.Position;

                var unitBehavior = avatar.Children.FirstOrDefault(c => c.GCClass == "avatar.base.UnitBehavior");

                if (unitBehavior == null)
                {
                    Debug.LogError("[OP11] UnitBehavior missing in avatar children");
                    Debug.LogError(" LoadAvatar() should have created it!");
                    return;
                }

                Debug.LogError($"[OP11] unitBehaviorId={unitBehavior.Id} state=assigned");

                writer.WriteByte(0x32);
                writer.WriteUInt16((ushort)avatar.Id);
                writer.WriteUInt16((ushort)unitBehavior.Id);
                WriteGCType(writer, "avatar.base.UnitBehavior");
                writer.WriteByte(0x01);

                Debug.LogError($"[OP11-HEADER] Component header complete - Entity {avatar.Id}, Component {unitBehavior.Id}");

                int writeInitStart = writer.Position;

                writer.WriteByte(0xFF);
                Debug.LogError($"[OP11-BYTE] Offset {writer.Position - 1}: 0xFF (Behavior marker)");

                writer.WriteByte(0x00);
                Debug.LogError($"[OP11-BYTE] Offset {writer.Position - 1}: 0x00 (Action1 null)");

                writer.WriteByte(0x00);
                Debug.LogError($"[OP11-BYTE] Offset {writer.Position - 1}: 0x00 (Action2 null)");

                writer.WriteByte(0xFF);
                Debug.LogError($"[OP11-BYTE] Offset {writer.Position - 1}: 0xFF (Generation counter 0x7d)");
                byte unitMoverFlags = 0x08;
                writer.WriteByte(unitMoverFlags);
                Debug.LogError($"[OP11-BYTE] Offset {writer.Position - 1}: 0x{unitMoverFlags:X2} (UnitMoverFlags)");

                writer.WriteInt32((int)(conn.PlayerHeading * 256));
                Debug.LogError($"[OP11-UINT32] Offset {writer.Position - 4}: UnitMover heading = {conn.PlayerHeading}");

                writer.WriteInt32((int)(conn.PlayerHeading * 256));
                Debug.LogError($"[OP11-UINT32] Offset {writer.Position - 4}: UnitMover heading2 = {conn.PlayerHeading}");

                byte waypointFlags = 0x00;
                writer.WriteByte(waypointFlags);
                Debug.LogError($"[OP11-BYTE] Offset {writer.Position - 1}: 0x{waypointFlags:X2} (WaypointFlags)");

                writer.WriteByte(0xFF);
                Debug.LogError($"[OP11-BYTE] Offset {writer.Position - 1}: 0xFF (SessionID)");

                writer.WriteByte(0x00);
                Debug.LogError($"[OP11-BYTE] Offset {writer.Position - 1}: 0x00 (UnitBehaviorUnk1)");

                writer.WriteByte(0x00);
                Debug.LogError($"[OP11-BYTE] Offset {writer.Position - 1}: 0x00 (UnitBehaviorUnk2)");
                opBytes = (int)(writer.Position - beforeOp);
                int writeInitBytes = writer.Position - writeInitStart;

                Debug.LogError($"[OP11-COMPLETE] WriteInit: {writeInitBytes} bytes");
                Debug.LogError($"[OP11-COMPLETE] Total operation: {opBytes} bytes");
                Debug.LogError("[OP11] state=complete");
                Debug.LogError($"[OP12] packetBytesBefore={writer.Position}");

                Debug.LogError($"");
                Debug.LogError($"[SIZE-CHECK] ");
                Debug.LogError($"[SIZE-CHECK] Position before Operation 12: {writer.Position} bytes");
                Debug.LogError($"[SIZE-CHECK] Expected position: 1920 bytes");
                int diff = writer.Position - 1920;

                if (writer.Position > 1937)
                {
                    Debug.LogError($"[SIZE-CHECK] result=over extra={writer.Position - 1937}");
                }
                else if (writer.Position < 1937)
                {
                    Debug.LogError($"[SIZE-CHECK] result=under missing={1937 - writer.Position}");
                }
                else
                {
                    Debug.LogError("[SIZE-CHECK] result=match");
                }

                Debug.LogError($"[SIZE-CHECK] Last 50 bytes before Op12:");
                if (VerbosePacketLogging && writer.Position >= 50)
                {
                    StringBuilder lastBytes = new StringBuilder();
                    for (int byteOffset = writer.Position - 50; byteOffset < writer.Position; byteOffset++)
                    {
                        lastBytes.Append($"{writer.ToArray()[byteOffset]:X2} ");
                        if ((byteOffset - (writer.Position - 50) + 1) % 16 == 0)
                        {
                            lastBytes.Append("\n              ");
                        }
                    }
                    Debug.LogError($"              {lastBytes.ToString()}");
                }
                Debug.LogError($"[SIZE-CHECK] ");
                Debug.LogError($"");
                Debug.LogError("[OP11] state=complete");
                Debug.LogError($"[CUMULATIVE-OP11] Total after Op11: {writer.Position} bytes");
                Debug.LogError($"[OP12] packetBytesBefore={writer.Position}");

                Debug.LogError($"[CUMULATIVE-OP11] Total after Op11: {writer.Position} bytes");
                Debug.LogError($"[OP12] packetBytesBefore={writer.Position}");

                Debug.LogError($"[SPAWN-TRACK] ");
                Debug.LogError($"[SPAWN-TRACK] ABOUT TO WRITE 0x46 (EndStreamConnected)");
                Debug.LogError($"[SPAWN-TRACK] conn.UpdateNumber BEFORE 0x46: {conn.UpdateNumber}");
                Debug.LogError($"[SPAWN-TRACK] isZoneTransition: {isZoneTransition}");

                writer.WriteByte(0x46);
                byte[] spawnData = writer.ToArray();
                if (VerbosePacketLogging)
                {
                    Debug.LogError($"[PLAYER-SPAWN-HEX] Size: {spawnData.Length} hex: {BitConverter.ToString(spawnData)}");
                    Debug.LogError("[FULL-SPAWN-PACKET]");
                    StringBuilder fullHex = new StringBuilder();
                    for (int rowOffset = 0; rowOffset < spawnData.Length; rowOffset += 16)
                    {
                        fullHex.Append($"{rowOffset:X8}  ");
                        for (int columnOffset = 0; columnOffset < 16; columnOffset++)
                        {
                            if (rowOffset + columnOffset < spawnData.Length)
                                fullHex.Append($"{spawnData[rowOffset + columnOffset]:X2} ");
                            else
                                fullHex.Append("   ");
                        }
                        fullHex.Append(" |");
                        for (int columnOffset = 0; columnOffset < 16 && rowOffset + columnOffset < spawnData.Length; columnOffset++)
                        {
                            byte byteValue = spawnData[rowOffset + columnOffset];
                            fullHex.Append((byteValue >= 32 && byteValue < 127) ? (char)byteValue : '.');
                        }
                        fullHex.AppendLine("|");
                    }
                    Debug.LogError(fullHex.ToString());
                }
                Debug.LogError($"[PACKET-1] bytes={spawnData.Length}");
                Debug.LogError($"[PACKET-1] lastByte=0x{spawnData[spawnData.Length - 1]:X2} expected=0x46");
                Debug.LogError("[PACKET-1] action=send-spawn-packet");
                SendCompressedA(conn, 0x01, 0x0F, spawnData);
                Debug.LogError("[PACKET-1] spawn packet sent");





                if (savedChar.hotbarSlots != null)
                {
                    string passiveConnectionKey = conn.ConnId.ToString();
                    if (!_playerManipMap.ContainsKey(passiveConnectionKey)) _playerManipMap[passiveConnectionKey] = new Dictionary<uint, string>();
                    RecalculateHotbarPassiveBonuses(conn, savedChar);
                }

                SendZoneNPCs(conn, conn.CurrentZoneId);
                SendZonePortals(conn, conn.CurrentZoneId);
                SpawnReturnTownPortal(conn);
                if (conn.CurrentZoneName == null || !conn.CurrentZoneName.Contains("boss"))
                    SendZoneCheckpoints(conn, conn.CurrentZoneId);
                SendZoneChests(conn, conn.CurrentZoneName);
                SendZoneWorldEntities(conn);
                SendDroppedItemsForZone(conn);

                bool hasBlingSkill = false;
                if (savedChar.hotbarSlots != null)
                    hasBlingSkill = savedChar.hotbarSlots.Any(hotbarSlot => hotbarSlot.skill.IndexOf("BlingGnome", StringComparison.OrdinalIgnoreCase) >= 0
                                                                         || hotbarSlot.skill.IndexOf("SummonBling", StringComparison.OrdinalIgnoreCase) >= 0);
                if (hasBlingSkill)
                {
                    BlingGnomeRuntime.Instance.SetServer(this);
                    BlingGnomeRuntime.Instance.CleanupForZoneTransition(conn.ConnId);
                    if (!BlingGnomeRuntime.Instance.HasGnome(conn.ConnId))
                    {
                        Debug.LogError($"[ZONE-IN] BlingGnome skill found - scheduling delayed spawn for {conn.LoginName}");
                        StartCoroutine(DelayedGnomeSpawn(conn, conn.CurrentZoneName, conn.CurrentZoneId, conn.InstanceId));
                    }
                }

                Debug.LogError($"[ZONE-IN] Sending quest update for zone: {conn.CurrentZoneGcType}");
                if (QuestManager.Instance != null)
                    QuestManager.Instance.SendAvailableQuestUpdateForZone(conn);

                _spawnedAvatarIds[conn.LoginName] = avatar.Id;

                conn.AvatarGcType = avatar.GCClass;
                conn.ClassName = savedChar.className ?? "Fighter";
                conn.PlayerLevel = playerState.Level;
                conn.BehaviorComponentId = (ushort)(unitBehavior?.Id ?? 0);
                conn.SkillsComponentId = (ushort)(skills?.Id ?? 0);
                conn.ManipulatorsComponentId = (ushort)(manipulators?.Id ?? 0);
                var modifiersComponent = avatar.Children?.FirstOrDefault(c => c.GCClass == "Modifiers");
                conn.ModifiersComponentId = (ushort)(modifiersComponent?.Id ?? 0);
                conn.IsSpawned = true;
                conn.LastOutboundHPWire = 0;
                conn.LastOutboundHPTime = 0f;
                conn.LastOutboundHPSource = null;
                conn.LastObservedClientHPWire = 0;
                conn.LastObservedClientHPTime = 0f;
                conn.LastObservedClientHPSource = null;
                Debug.LogError($"[MULTIPLAYER] Saved spawn data for {conn.LoginName}: avatar={avatar.Id} behavior={conn.BehaviorComponentId}");
                var combatPlayerState = GetPlayerState(conn.ConnId.ToString());
                if (combatPlayerState != null)
                {
                    string combatInstanceKey = GetInstanceZoneKey(conn);
                    CombatRuntime.Instance.RegisterPlayer(avatar.Id, conn.LoginName, combatPlayerState, conn.PlayerPosX, conn.PlayerPosY, combatInstanceKey);
                    Debug.LogError($"[COMBAT] registerPlayer name='{conn.LoginName}' instance={combatInstanceKey}");

                }



                if (unitBehavior != null)
                {
                    Debug.LogError("");
                    Debug.LogError("");
                    Debug.LogError("[PACKET-2] build spawn action + FollowClient");
                    Debug.LogError("");

                    var followWriter = new LEWriter();
                    if (!DungeonMazeSpawner.IsProceduralZone(conn.CurrentZoneName) && _zones.TryGetValue(conn.CurrentZoneId, out Zone spawnHeadingZone) && spawnHeadingZone.spawnHeading != 0)
                    {
                        conn.PlayerHeading = spawnHeadingZone.spawnHeading;
                        Debug.LogError($"[HEADING]  Set heading to {spawnHeadingZone.spawnHeading} from zone '{spawnHeadingZone.name}' (id={conn.CurrentZoneId})");
                    }

                    followWriter.WriteByte(0x07);
                    Debug.LogError($"[PACKET-2] BeginStream: 0x07");

                    int spawnStart = followWriter.Position;
                    followWriter.WriteByte(0x35);
                    followWriter.WriteUInt16((ushort)unitBehavior.Id);
                    followWriter.WriteByte(0x04);
                    followWriter.WriteByte(0x04);
                    followWriter.WriteByte(0xFF);
                    followWriter.WriteInt32((int)(spawnX * 256));
                    followWriter.WriteInt32((int)(spawnY * 256));
                    followWriter.WriteInt32((int)(spawnZ * 256));
                    followWriter.WriteUInt16((ushort)avatar.Id);

                    int spawnSynchPos = followWriter.Position;
                    Debug.LogError($"[SPAWN-ENTITY-SYNCH] Position before 0x02: {spawnSynchPos}");


                    WritePlayerEntitySynchNoCombatFlush(conn, followWriter);

                    Debug.LogError($"[SPAWN-ENTITY-SYNCH] SPAWN ACTION total bytes: {followWriter.Position - spawnStart}");

                    Debug.LogError($"[PACKET-2] SPAWN ACTION: UnitBehavior={unitBehavior.Id:X4}, Pos=({spawnX},{spawnY},{spawnZ})");

                    followWriter.WriteByte(0x06);
                    Debug.LogError($"[PACKET-2] EndStream: 0x06 at position {followWriter.Position - 1}");

                    byte[] followData = followWriter.ToArray();

                    if (VerbosePacketLogging)
                    {
                        Debug.LogError($"[PACKET-2-COMPLETE] ");
                        Debug.LogError($"[PACKET-2-COMPLETE] Total packet size: {followData.Length} bytes");
                        Debug.LogError($"[PACKET-2-COMPLETE] Full hex dump:");
                        StringBuilder hexDump = new StringBuilder();
                        for (int byteOffset = 0; byteOffset < followData.Length; byteOffset++)
                        {
                            hexDump.Append($"{followData[byteOffset]:X2} ");
                            if ((byteOffset + 1) % 16 == 0)
                            {
                                hexDump.Append("\n[PACKET-2-COMPLETE]                      ");
                            }
                        }
                        Debug.LogError($"[PACKET-2-COMPLETE]                      {hexDump}");
                        Debug.LogError($"[PACKET-2-COMPLETE] ");
                    }

                    Debug.LogError("[PACKET-2] send begin");
                    SendCompressedA(conn, 0x01, 0x0F, followData);
                    Debug.LogError("[PACKET-2] send complete");
                    Debug.LogError($"[SPAWN-TRACK] After Packet 2, conn.UpdateNumber: {conn.UpdateNumber}");

                    Debug.LogError("[PACKET-3] type=UnitMoverUpdate opcode=0x65");

                    var moveWriter = new LEWriter();
                    moveWriter.WriteByte(0x07);
                    Debug.LogError($"[PACKET-3] BeginStream (0x07)");

                    moveWriter.WriteByte(0x35);
                    moveWriter.WriteUInt16((ushort)unitBehavior.Id);
                    moveWriter.WriteByte(0x65);
                    moveWriter.WriteByte(0x00);
                    moveWriter.WriteByte(0x01);
                    moveWriter.WriteByte(0x03);
                    moveWriter.WriteInt32((int)(conn.PlayerHeading * 256));
                    moveWriter.WriteInt32((int)(spawnX * 256));
                    moveWriter.WriteInt32((int)(spawnY * 256));

                    int pkt3SynchPos = moveWriter.Position;
                    WritePlayerEntitySynchNoCombatFlush(conn, moveWriter);


                    moveWriter.WriteByte(0x06);

                    byte[] moveData = moveWriter.ToArray();
                    Debug.LogError($"[PACKET-3] Total size: {moveData.Length} bytes");
                    if (VerbosePacketLogging) Debug.LogError($"[PACKET-3] Full hex: {BitConverter.ToString(moveData)}");
                    Debug.LogError("[PACKET-3] send begin");
                    SendCompressedA(conn, 0x01, 0x0F, moveData);
                    Debug.LogError("[PACKET-3] send complete");
                    Debug.LogError($"[SPAWN-TRACK] After Packet 3, conn.UpdateNumber: {conn.UpdateNumber}");
                    StartCoroutine(SendDeferredClientControl(conn, (ushort)unitBehavior.Id));




                    Debug.LogError("[WELCOME] state=start");
                    var welcomeWriter = new LEWriter();
                    welcomeWriter.WriteByte(0x06);
                    welcomeWriter.WriteByte(0x00);
                    welcomeWriter.WriteByte(0x0d);
                    string rawWelcome = ServerSettings.GetString("welcomeMessage", "Welcome to Dungeon Runners!");
                    string welcomeColor = ServerSettings.GetString("welcomeColor", "");
                    string welcomeMessage = WrapChatColor(rawWelcome, welcomeColor, "") + "\n";
                    foreach (char c in welcomeMessage)
                    {
                        welcomeWriter.WriteByte((byte)c);
                    }
                    welcomeWriter.WriteByte(0x00);
                    SendCompressedA(conn, 0x01, 0x0F, welcomeWriter.ToArray());
                    Debug.LogError("[WELCOME] Sent welcome message after packets 1-3");

                    string motd = ServerSettings.GetString("motd", "");
                    if (!string.IsNullOrEmpty(motd))
                    {
                        string motdColor = ServerSettings.GetString("motdColor", "");
                        var motdWriter = new LEWriter();
                        motdWriter.WriteByte(0x06);
                        motdWriter.WriteByte(0x00);
                        motdWriter.WriteByte(0x0d);
                        string motdMessage = WrapChatColor(motd, motdColor, "") + "\n";
                        foreach (char c in motdMessage)
                            motdWriter.WriteByte((byte)c);
                        motdWriter.WriteByte(0x00);
                        SendCompressedA(conn, 0x01, 0x0F, motdWriter.ToArray());
                        Debug.LogError($"[MOTD] Sent: {motd}");
                    }

                    string spawnSweepInstanceKey = GetInstanceZoneKey(conn);
                    if (!string.IsNullOrWhiteSpace(spawnSweepInstanceKey))
                    {
                        foreach (var sweepMonster in CombatRuntime.Instance.GetMonstersInZone(spawnSweepInstanceKey))
                        {
                            if (!sweepMonster.IsAlive) continue;
                            SendMonsterToClient(conn, sweepMonster);
                        }
                    }
                    conn.AllowFlush = true;
                    Debug.LogError("[SPAWN]  Spawn complete - enabling message queue flushing");

                    if (IsPlayerFree(conn.LoginName) && conn.ModifiersId != 0
                        && !_freePlayerModifierSent.Contains(conn.LoginName))
                    {
                        _freePlayerModifierSent.Add(conn.LoginName);
                        _freePlayerModifierSent.Add(conn.LoginName + "_sent");
                        StartCoroutine(SendDelayedFreePlayerModifier(conn, 2.0f));
                    }

                    if (conn.TickCoroutine != null)
                    {
                        StopCoroutine(conn.TickCoroutine);
                        Debug.LogError("[TICK] Stopped old tick coroutine");
                    }

                    conn.PlayerPosX = spawnX;
                    conn.PlayerPosY = spawnY;
                    conn.PlayerPosZ = spawnZ;
                    conn.HasLivePlayerPosition = true;
                    conn.LivePlayerPosX = spawnX;
                    conn.LivePlayerPosY = spawnY;
                    conn.LivePlayerPosZ = spawnZ;
                    conn.LivePlayerHeading = conn.PlayerHeading;
                    conn.LivePlayerPositionTime = Time.time;
                    conn.SessionID = 0xFF;

                    conn.TickCoroutine = StartCoroutine(SendTickUpdates(conn, unitBehavior.Id));
                }
                else
                {
                    Debug.LogError("[SPAWN] UnitBehavior missing; cannot send spawn/followclient");
                }

                Debug.LogError($"");
                Debug.LogError("");
                Debug.LogError("[SPAWN] SendPlayerEntitySpawn complete");

                try
                {
                    foreach (var other in _connections.Values)
                    {
                        if (other == conn) continue;
                        if (!other.IsSpawned) continue;
                        if (other.CurrentZoneGcType != conn.CurrentZoneGcType) continue;
                        if (other.InstanceId != conn.InstanceId) continue;

                        bool pvpMatch = Gameplay.PVPMatchmaking.Instance.GetMatchForPlayer(conn.LoginName) != null;
                        if (pvpMatch && (!_pvpControllerReady.Contains(conn.LoginName)
                                         || !_pvpControllerReady.Contains(other.LoginName)))
                        {
                            Debug.LogError($"[MULTIPLAYER] deferring {conn.LoginName}<->{other.LoginName} avatar exchange (controllers not both ready yet)");
                            continue;
                        }

                        uint otherAvatarId = GetPlayerAvatarId(other.LoginName);
                        bool connAlreadyHasOther = _remoteBehaviorIds.TryGetValue(conn.LoginName, out var connRemoteMap)
                            && connRemoteMap.ContainsKey(other.LoginName);
                        if (otherAvatarId != 0 && !connAlreadyHasOther)
                        {
                            byte[] otherSpawn = BuildOtherPlayerSpawnPacket(other, otherAvatarId, conn);
                            if (otherSpawn != null)
                            {
                                SendToClient(conn, otherSpawn);
                                SendRemoteClientControl(conn, other);
                                Debug.LogError($"[MULTIPLAYER] Sent {other.LoginName}'s avatar to {conn.LoginName}");
                            }
                        }

                        uint myAvatarId = GetPlayerAvatarId(conn.LoginName);
                        bool otherAlreadyHasConn = _remoteBehaviorIds.TryGetValue(other.LoginName, out var otherRemoteMap)
                            && otherRemoteMap.ContainsKey(conn.LoginName);
                        if (myAvatarId != 0 && !otherAlreadyHasConn)
                        {
                            byte[] mySpawn = BuildOtherPlayerSpawnPacket(conn, myAvatarId, other);
                            if (mySpawn != null)
                            {
                                SendToClient(other, mySpawn);
                                SendRemoteClientControl(other, conn);
                                Debug.LogError($"[MULTIPLAYER] Sent {conn.LoginName}'s avatar to {other.LoginName}");
                            }
                        }
                    }
                }
                catch (Exception mpEx)
                {
                    Debug.LogError($"[MULTIPLAYER] spawnExchange state=failed message='{mpEx.Message}'");
                }
                BlingGnomeRuntime.Instance.SendGnomesToConnection(conn);
                Debug.LogError($"   PACKET 1: Spawn ({spawnData.Length} bytes) ending with 0x46");
                Debug.LogError($"   PACKET 2: Spawn Action + FollowClient ending with 0x06");
                Debug.LogError($"   PACKET 3: UnitMoverUpdate (0x65)");
                Debug.LogError($"   WELCOME: Sent after all packets");
                Debug.LogError("");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SEND-PLAYER-ENTITY-SPAWN] state=failed message='{ex.Message}'");
                Debug.LogError($"[SEND-PLAYER-ENTITY-SPAWN] stack='{ex.StackTrace}'");
            }
        }





        private string HexDump(byte[] bytes, int offset, int length)
        {
            var sb = new System.Text.StringBuilder();
            int bytesPerLine = 16;

            for (int rowOffset = 0; rowOffset < length; rowOffset += bytesPerLine)
            {
                sb.Append($"{(offset + rowOffset):X8}  ");

                for (int columnOffset = 0; columnOffset < bytesPerLine; columnOffset++)
                {
                    if (rowOffset + columnOffset < length)
                    {
                        sb.Append($"{bytes[offset + rowOffset + columnOffset]:X2} ");
                    }
                    else
                    {
                        sb.Append("   ");
                    }

                    if (columnOffset == 7) sb.Append(" ");
                }

                sb.Append(" |");

                for (int columnOffset = 0; columnOffset < bytesPerLine && rowOffset + columnOffset < length; columnOffset++)
                {
                    byte byteValue = bytes[offset + rowOffset + columnOffset];
                    sb.Append(byteValue >= 32 && byteValue < 127 ? (char)byteValue : '.');
                }

                sb.Append("|");

                if (rowOffset + bytesPerLine < length)
                {
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }



        private IEnumerator DelayedGnomeSpawn(RRConnection conn, string zoneName, uint zoneId, uint instanceId)
        {
            yield return new WaitForSeconds(0.5f);

            if (conn == null || !conn.IsConnected) yield break;
            if (!string.Equals(conn.CurrentZoneName, zoneName, StringComparison.OrdinalIgnoreCase)) yield break;
            if (conn.CurrentZoneId != zoneId || conn.InstanceId != instanceId) yield break;
            if (BlingGnomeRuntime.Instance.HasGnome(conn.ConnId)) yield break;
            var spawnGateState = GetPlayerState(conn.ConnId.ToString());
            if (spawnGateState != null && spawnGateState.CurrentHPWire == 0) yield break;

            Debug.LogError($"[ZONE-IN] Delayed BlingGnome spawn firing for {conn.LoginName}");
            BlingGnomeRuntime.Instance.SetServer(this);
            BlingGnomeRuntime.Instance.SpawnGnome(conn,
                (targetConnection, dest, messageType, data) => SendCompressedA(targetConnection, dest, messageType, data),
                (targetConnection, message) => SendSystemMessage(targetConnection, message));
        }

        private IEnumerator SendTickUpdates(RRConnection conn, uint unitBehaviorId)
        {
            Debug.LogError($"[TICK] Starting for UnitBehavior 0x{unitBehaviorId:X4}");
            conn.UnitBehaviorId = unitBehaviorId;
            Debug.LogError($"[TICK] Using position ({conn.PlayerPosX}, {conn.PlayerPosY})");

            int tickCount = 0;

            while (conn != null && conn.IsConnected)
            {
                tickCount++;


                PlayerState tickState = GetPlayerState(conn.ConnId.ToString());
                uint tickPlayerEntityId = conn.Avatar != null ? (uint)conn.Avatar.Id : 0;
                bool canAdvancePlayerHP = tickState != null
                    && CanAdvancePlayerEntitySynchInfoHP(tickPlayerEntityId);
                if (tickState != null)
                    tickState.AdvanceEntitySynchInfoHP(GetCombatNow(), "ServerTick");

                if (_adminCommands != null && canAdvancePlayerHP && _adminCommands.IsRegenActive(conn.ConnId))
                {
                    tickState.RunRegenTick();
                    if (tickState.IsRegenComplete)
                    {
                        _adminCommands.ClearRegenFlag(conn.ConnId);
                    }
                }
                SendRemoteAvatarHPUpdates(conn);
                SendGroupMemberHealthManaIfChanged(conn);

                yield return new WaitForSeconds(0.033f);
            }

            Debug.LogError($"[TICK] STOPPED after {tickCount} ticks");
        }









        private void ReassignEntityIDs(GCObject player)
        {
            player.Id = _nextEntityId++;

            foreach (var child in player.Children)
            {
                child.Id = _nextEntityId++;

                if (child.Children != null)
                {
                    foreach (var grandchild in child.Children)
                    {
                        grandchild.Id = _nextEntityId++;
                    }
                }
            }
        }

        private uint GetClientId24(int connId)
        {
            return _peerId24.TryGetValue(connId, out var id) ? id : 0u;
        }

        private void SendSocialViaAuth(RRConnection conn, byte dest, byte messageType, byte[] innerData)
        {
            SendCompressedA(conn, dest, messageType, innerData);
        }

        private void SendSocialViaQueue(RRConnection conn, byte dest, byte messageType, byte[] innerData)
        {
            SendCompressedA(conn, dest, messageType, innerData);
        }

        private void OnQueueStreamReady(string username)
        {
            foreach (var conn in _connections.Values)
            {
                if (conn.IsConnected && conn.LoginName != null &&
                    conn.LoginName.Equals(username, StringComparison.OrdinalIgnoreCase))
                {
                    if (_selectedCharacter.TryGetValue(conn.LoginName, out var selectedCharacter) && selectedCharacter.Name != null)
                    {
                        Debug.LogError($"[QUEUE-CONNECTION] Queue ready for {username} - resending social init");
                        SocialRuntime.Instance.SendLoginSocialInit(conn, selectedCharacter.Name, SendSocialViaAuth);
                        PosseRuntime.Instance.SendConnectionNotification(conn, true, SendSocialViaAuth);
                        SendPosseStateForCharacter(conn, selectedCharacter.Id, SendSocialViaAuth);
                        try { PosseRuntime.Instance.NotifyMemberStateChange(selectedCharacter.Id, this); }
                        catch (Exception ex2) { Debug.LogError($"[POSSE] login-notify (OnQueueStreamReady) failed: {ex2.Message}"); }
                    }
                    break;
                }
            }
        }

        private void SendCompressedE(RRConnection conn, byte[] innerData)
        {
            try
            {
                if (conn == null || innerData == null || innerData.Length == 0)
                {
                    Debug.LogError("[SEND-COMPRESSEDE] dropped empty packet");
                    return;
                }
                byte[] compressed = ZlibUtil.Deflate(innerData);
                int compressedLen = compressed.Length + 12;

                var writer = new LEWriter();
                writer.WriteByte(0x0E);
                writer.WriteUInt24((int)MSG_DEST);
                writer.WriteUInt24(compressedLen);
                writer.WriteByte(0x00);
                writer.WriteUInt24((int)MSG_SOURCE);
                writer.WriteByte(0x01);
                writer.WriteByte(0x00);
                writer.WriteByte(0x01);
                writer.WriteByte(0x00);
                writer.WriteByte(0x00);
                writer.WriteUInt32((uint)innerData.Length);
                writer.WriteBytes(compressed);

                byte[] data = writer.ToArray();
                lock (conn.SendLock)
                {
                    conn.Stream.Write(data, 0, data.Length);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SEND-COMPRESSED-E] state=failed message='{ex.Message}'");
            }
        }



        private void SendCompressedA(RRConnection conn, byte dest, byte messageType, byte[] innerData)
        {
            SendCompressedA(conn, dest, messageType, innerData, InferSendCompressedAEntitySynchInfoContext(conn, innerData), "SEND-COMPRESSEDA");
        }

        private EntitySynchInfoContext InferSendCompressedAEntitySynchInfoContext(RRConnection conn, byte[] innerData)
        {
            if (innerData == null || innerData.Length < 2) return EntitySynchInfoContext.PlayerActionResponse;
            if (innerData[0] == 0x07 && innerData[1] == 0x0D) return EntitySynchInfoContext.WorldInterval;
            if (innerData[0] == 0x07 && (innerData[1] == 0x01 || innerData[1] == 0x02 || innerData[1] == 0x32)) return EntitySynchInfoContext.EntityInitPrimer;
            for (int byteOffset = 0; byteOffset + 3 < innerData.Length; byteOffset++)
            {
                if (innerData[byteOffset] != 0x35) continue;
                ushort componentId = (ushort)(innerData[byteOffset + 1] | (innerData[byteOffset + 2] << 8));
                byte subtype = innerData[byteOffset + 3];
                if (subtype == 0x64)
                    return IsAvatarOrAvatarComponentId(conn, componentId) ? EntitySynchInfoContext.ControlAck : EntitySynchInfoContext.MonsterAction;
                if (subtype == 0x65)
                    return IsAvatarOrAvatarComponentId(conn, componentId) ? EntitySynchInfoContext.MoverAck : EntitySynchInfoContext.MonsterMove;
                if (subtype == 0x04) return EntitySynchInfoContext.MonsterAction;
                if (subtype == 0x01) return EntitySynchInfoContext.PlayerActionResponse;
            }
            return EntitySynchInfoContext.PlayerActionResponse;
        }

        private bool SendCompressedA(RRConnection conn, byte dest, byte messageType, byte[] innerData, EntitySynchInfoContext entitySynchInfoContext, string packetName)
        {
            try
            {
                if (conn == null || innerData == null || innerData.Length == 0)
                {
                    Debug.LogError($"[SEND-COMPRESSEDA] dropped empty packet dest=0x{dest:X2} type=0x{messageType:X2}");
                    return false;
                }
                byte[] compressed = ZlibUtil.Deflate(innerData);
                uint peer = GetClientId24(conn.ConnId);
                var writer = new LEWriter();
                writer.WriteByte(0x0A);
                writer.WriteUInt24((int)(peer & 0xFFFFFFu));
                writer.WriteUInt32((uint)(compressed.Length + 7));
                writer.WriteByte(dest);
                writer.WriteByte(messageType);
                writer.WriteByte(0x00);
                writer.WriteUInt32((uint)innerData.Length);
                writer.WriteBytes(compressed);
                byte[] data = writer.ToArray();
                lock (conn.SendLock)
                {
                    conn.Stream.Write(data, 0, data.Length);
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SEND-COMPRESSED-A] state=failed message='{ex.Message}'");
                return false;
            }
        }

        public void SendToClient(RRConnection conn, byte[] data)
        {
            FlushConnQueue(conn);
            SendCompressedA(conn, 0x01, 0x0F, data);
        }






        private static int GetIntSafe(System.Data.IDataReader reader, string column, int defaultValue)
        {
            try { for (int columnIndex = 0; columnIndex < reader.FieldCount; columnIndex++) if (reader.GetName(columnIndex) == column) return reader.GetInt32(columnIndex); } catch { }
            return defaultValue;
        }

        [System.Serializable]
        public class Zone
        {
            public uint id;
            public string name;
            public string gcType;
            public float spawnX;
            public float spawnY;
            public float spawnZ;
            public float spawnHeading;
            public string respawnZone;
            public int exploredBitCount;
            public uint Id => id;
            public string Name => name;
        }

        [System.Serializable]
        public class ZoneList
        {
            public List<Zone> zones;

        }
        private List<ActiveQuest> ConvertToActiveQuests(List<SavedQuest> saved)
        {
            if (saved == null) return new List<ActiveQuest>();
            return saved.Select(sq => new ActiveQuest
            {
                QuestId = sq.questId,
                QuestGiverId = sq.questGiverId,
                AcceptedAt = DateTime.TryParse(sq.acceptedAt, out var dt) ? dt : DateTime.UtcNow,
                Objectives = sq.objectives?.Select(o => new QuestProgress
                {
                    ObjectiveName = o.objectiveName,
                    Type = o.type,
                    Target = o.target,
                    Label = o.label,
                    Required = o.required,
                    Current = o.current
                }).ToList() ?? new List<QuestProgress>()
            }).ToList();
        }



        public Dictionary<int, RRConnection> GetConnections() => _connections;

        public List<string> GetZoneNames()
        {
            return _zones.Values.Select(z => z.name).OrderBy(n => n).ToList();
        }

        public SavedCharacter GetSavedCharacterForConn(RRConnection conn)
        {
            if (conn == null || string.IsNullOrEmpty(conn.LoginName)) return null;
            if (_selectedCharacter.TryGetValue(conn.LoginName, out var gcObj))
                return CharacterRepository.GetCharacter((uint)gcObj.Id);
            return null;
        }

        public bool GrantSkillRuntime(RRConnection conn, string skillGcClass)
        {
            if (conn == null || string.IsNullOrEmpty(skillGcClass)) return false;

            var savedChar = GetSavedCharacterForConn(conn);
            if (savedChar == null)
            {
                Debug.LogError($"[GRANT-SKILL] No saved character for {conn.LoginName}");
                return false;
            }

            string connKey = conn.ConnId.ToString();

            bool alreadyHas = false;
            if (_playerSkillLevels.TryGetValue(connKey, out var existingLevels))
                alreadyHas = existingLevels.ContainsKey(skillGcClass);
            if (!alreadyHas && savedChar.skills != null)
                alreadyHas = savedChar.skills.Any(s => string.Equals(s, skillGcClass, StringComparison.OrdinalIgnoreCase));

            if (alreadyHas)
            {
                Debug.LogError($"[GRANT-SKILL] {conn.LoginName} already has {skillGcClass}");
                return false;
            }

            if (savedChar.skills == null)
                savedChar.skills = new List<string>();
            savedChar.skills.Add(skillGcClass);
            savedChar.SetSkillLevel(skillGcClass, 1);
            CharacterRepository.SaveCharacter(savedChar);

            if (!_playerSkillLevels.ContainsKey(connKey))
                _playerSkillLevels[connKey] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _playerSkillLevels[connKey][skillGcClass] = 1;
            int lastDot = skillGcClass.LastIndexOf('.');
            if (lastDot >= 0)
                _playerSkillLevels[connKey][skillGcClass.Substring(lastDot + 1)] = 1;

            ushort skillsCid = 0;
            _playerSkillsComponentId.TryGetValue(connKey, out skillsCid);

            Debug.LogError("[GRANT-SKILL-DETAIL]");
            Debug.LogError($"[GRANT-SKILL-DETAIL] connKey='{connKey}' login='{conn.LoginName}'");
            Debug.LogError($"[GRANT-SKILL-DETAIL] skillsCid=0x{skillsCid:X4} ({skillsCid})");
            Debug.LogError($"[GRANT-SKILL-DETAIL] unitContainerId=0x{conn.UnitContainerId:X4}");
            Debug.LogError($"[GRANT-SKILL-DETAIL] avatarId={conn.Avatar?.Id ?? 0}");
            Debug.LogError($"[GRANT-SKILL-DETAIL] skillGcClass='{skillGcClass}' len={skillGcClass.Length}");
            Debug.LogError($"[GRANT-SKILL-DETAIL] skillGcClassBytes={BitConverter.ToString(System.Text.Encoding.ASCII.GetBytes(skillGcClass))}");
            if (conn.Avatar?.Children != null)
            {
                var skillsNode = conn.Avatar.Children.FirstOrDefault(c => c.GCClass == "avatar.base.skills");
                Debug.LogError($"[GRANT-SKILL-DETAIL] avatar.base.skills id={skillsNode?.Id ?? 0} skillsCid={skillsCid}");
            }

            if (skillsCid != 0)
            {
                uint currentGold = 0;
                try
                {
                    var savedCharacter = GetSavedCharacterForConn(conn);
                    if (savedCharacter != null) currentGold = savedCharacter.gold;
                }
                catch { }

                var grantSkillMessage = new LEWriter();
                grantSkillMessage.WriteByte(0x07);

                grantSkillMessage.WriteByte(0x35);
                grantSkillMessage.WriteUInt16(skillsCid);
                grantSkillMessage.WriteByte(0x33);
                grantSkillMessage.WriteUInt32(currentGold);
                WritePlayerEntitySynch(conn, grantSkillMessage);

                grantSkillMessage.WriteByte(0x35);
                grantSkillMessage.WriteUInt16(skillsCid);
                grantSkillMessage.WriteByte(0x32);
                grantSkillMessage.WriteByte(0xFF);
                grantSkillMessage.WriteCString(skillGcClass);
                grantSkillMessage.WriteByte(0x01);
                WritePlayerEntitySynch(conn, grantSkillMessage);

                grantSkillMessage.WriteByte(0x06);

                byte[] packet = grantSkillMessage.ToArray();
                Debug.LogError($"[GRANT-SKILL-HEX] ({packet.Length}b) {BitConverter.ToString(packet)}");
                Debug.LogError($"[GRANT-SKILL-ANNOT] 07 | 35 cid=0x{skillsCid:X4} 33 gold={currentGold} 00 | 35 cid=0x{skillsCid:X4} 32 FF \"{skillGcClass}\"+00 level=1 00 | 06");

                SendCompressedE(conn, packet);
                Debug.LogError($"[GRANT-SKILL] Sent combined 0x33+0x32 (trainer-shape) for '{skillGcClass}'");
            }
            else
            {
                Debug.LogError($"[GRANT-SKILL]  skillsCid=0 - Skills component ID not captured. Skill saved to DB but won't appear until zone.");
            }

            bool isPassive = skillGcClass.ToLower().Contains("passive") || skillGcClass.ToLower().Contains("trait");
            if (!isPassive)
            {
                var manip = conn.Avatar?.Children?.FirstOrDefault(c => c.GCClass == "Manipulators");
                if (manip != null)
                {
                    var newSkill = new GCObject
                    {
                        GCClass = skillGcClass,
                        DFCClass = "ActiveSkill",
                        Name = skillGcClass,
                        Id = _nextEntityId++
                    };
                    manip.AddChild(newSkill);
                    if (_playerSkillSlots.TryGetValue(connKey, out var slotMap))
                        slotMap[skillGcClass] = (uint)newSkill.Id;
                    if (_playerManipMap.TryGetValue(connKey, out var manipMap))
                    {
                        uint nextManipId = 100;
                        foreach (var k in manipMap.Keys)
                            if (k >= nextManipId) nextManipId = k + 1;
                        manipMap[nextManipId] = skillGcClass;
                    }
                    Debug.LogError($"[GRANT-SKILL] Added '{skillGcClass}' to Manipulators, eid={newSkill.Id}");
                }
            }

            Debug.LogError($"[GRANT-SKILL]  Granted '{skillGcClass}' to {conn.LoginName}");
            return true;
        }

        public RRConnection FindConnectionByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var selectedCharacterEntry in _selectedCharacter)
            {
                if (selectedCharacterEntry.Value.Name != null && selectedCharacterEntry.Value.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    var conn = _connections.Values.FirstOrDefault(c =>
                        c.IsConnected && c.LoginName == selectedCharacterEntry.Key);
                    if (conn != null) return conn;
                }
            }
            return _connections.Values.FirstOrDefault(c =>
                c.IsConnected && !string.IsNullOrEmpty(c.LoginName) &&
                c.LoginName.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public RRConnection FindConnectionById(int connId)
        {
            _connections.TryGetValue(connId, out var conn);
            return conn;
        }

        public void SetPlayerMembershipPublic(string loginName, bool isFree) { SetPlayerMembership(loginName, isFree); }
        public bool IsPlayerFreePublic(string loginName) { return IsPlayerFree(loginName); }
        public Dictionary<string, bool> GetAllMembershipsPublic()
        {
            var result = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var db = Database.GameDatabase.GetConnection())
                using (var reader = Database.GameDatabase.ExecuteReader(db, "SELECT username, is_member FROM accounts"))
                {
                    while (reader.Read())
                        result[reader.GetString(0)] = reader.GetInt32(1) == 0;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MEMBER] getAll state=failed message='{ex.Message}'");
            }
            return result;
        }

        public uint GetCharSqlIdPublic(RRConnection conn) => GetCharSqlId(conn);
    }
}
