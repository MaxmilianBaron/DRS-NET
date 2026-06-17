using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DungeonRunners.Engine;
using Mono.Data.Sqlite;
using DungeonRunners.Data;

namespace DungeonRunners.Database
{
    public static class CharacterRepository
    {

        public static SavedCharacter CreateCharacter(string name, string className, uint accountId, string accountName, string avatarClass = "")
        {
            if (CharacterNameExists(name))
            {
                Debug.LogError($"[DB-CHAR] Name '{name}' already taken");
                return null;
            }

            var classDef = ClassConfig.GetClassDefinition(className);
            if (classDef == null)
            {
                Debug.LogError($"[DB-CHAR] Invalid class: {className}");
                return null;
            }

            try
            {
                using (var connection = GameDatabase.GetConnection())
                using (var transaction = connection.BeginTransaction())
                {
                    GameDatabase.ExecuteNonQuery(connection,
                        @"INSERT INTO characters (account_id, name, class_name, avatar_class, level, experience, gold, current_zone)
                          VALUES (@aid, @name, @class, @avatar, 0, 0, 100, 'tutorial')",
                        ("@aid", (int)accountId), ("@name", name), ("@class", className), ("@avatar", avatarClass));

                    int characterId = Convert.ToInt32(GameDatabase.ExecuteScalar(connection, "SELECT last_insert_rowid()"));

                    var startingEquipment = classDef.startingEquipment;
                    InsertStartingEquipment(connection, characterId, startingEquipment, "weapon", startingEquipment.weapon);
                    InsertStartingEquipment(connection, characterId, startingEquipment, "armor", startingEquipment.armor);
                    InsertStartingEquipment(connection, characterId, startingEquipment, "helmet", startingEquipment.helmet);
                    InsertStartingEquipment(connection, characterId, startingEquipment, "gloves", startingEquipment.gloves);
                    InsertStartingEquipment(connection, characterId, startingEquipment, "boots", startingEquipment.boots);
                    InsertStartingEquipment(connection, characterId, startingEquipment, "shoulders", startingEquipment.shoulders ?? "");
                    InsertStartingEquipment(connection, characterId, startingEquipment, "shield", startingEquipment.shield ?? "");
                    InsertStartingEquipment(connection, characterId, startingEquipment, "ring1", startingEquipment.ring1 ?? "");
                    InsertStartingEquipment(connection, characterId, startingEquipment, "ring2", startingEquipment.ring2 ?? "");
                    InsertStartingEquipment(connection, characterId, startingEquipment, "amulet", startingEquipment.amulet ?? "");

                    if (classDef.startingSkills != null)
                    {
                        foreach (var skill in classDef.startingSkills)
                        {
                            int hotbarSlot = GetStartingSkillHotbarSlot(className, skill);
                            GameDatabase.ExecuteNonQuery(connection,
                                "INSERT OR IGNORE INTO character_skills (character_id, skill_gc_class, level, hotbar_slot) VALUES (@cid, @s, 1, @h)",
                                ("@cid", characterId), ("@s", skill), ("@h", hotbarSlot));
                        }
                    }

                    if (classDef.startingInventory != null)
                    {
                        foreach (var item in classDef.startingInventory)
                        {
                            int count = item.count > 0 ? item.count : 1;
                            GameDatabase.ExecuteNonQuery(connection,
                                "INSERT INTO character_inventory (character_id, gc_class, slot_x, slot_y, count) VALUES (@cid, @gc, @x, @y, @count)",
                                ("@cid", characterId), ("@gc", item.gcClass), ("@x", (int)item.x), ("@y", (int)item.y), ("@count", count));
                        }
                    }

                    GameDatabase.ExecuteNonQuery(connection,
                        "INSERT OR IGNORE INTO character_skills (character_id, skill_gc_class, level) VALUES (@cid, @s, 1)",
                        ("@cid", characterId), ("@s", "skills.generic.SummonBlingGnome"));

                    transaction.Commit();
                    Debug.LogError($"[DB-CHAR] Created '{name}' (ID: {characterId}) class={className} account={accountId}");

                    return GetCharacter((uint)characterId);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DB-CHAR] operation=Create state=failed message='{ex.Message}'");
                return null;
            }
        }

        private static int GetStartingSkillHotbarSlot(string className, string skill)
        {
            string classKey = (className ?? "").ToLowerInvariant();
            string skillGcType = skill ?? "";

            if (classKey.Contains("fight") || classKey.Contains("warrior"))
            {
                if (string.Equals(skillGcType, "skills.generic.Stomp", StringComparison.OrdinalIgnoreCase)) return 100;
                if (string.Equals(skillGcType, "skills.generic.Butcher", StringComparison.OrdinalIgnoreCase)) return 105;
                if (string.Equals(skillGcType, "skills.generic.FighterClassPassive", StringComparison.OrdinalIgnoreCase)) return 108;
                if (string.Equals(skillGcType, "skills.generic.MeleeAttackSpeedModPassive", StringComparison.OrdinalIgnoreCase)) return 109;
            }
            else if (classKey.Contains("ranger"))
            {
                if (string.Equals(skillGcType, "skills.generic.PoisonBlastRadius", StringComparison.OrdinalIgnoreCase)) return 100;
                if (string.Equals(skillGcType, "skills.generic.PoisonShot", StringComparison.OrdinalIgnoreCase)) return 105;
                if (string.Equals(skillGcType, "skills.generic.RangerClassPassive", StringComparison.OrdinalIgnoreCase)) return 108;
                if (string.Equals(skillGcType, "skills.generic.RangeAttackSpeedModPassive", StringComparison.OrdinalIgnoreCase)) return 109;
            }
            else if (classKey.Contains("mage") || classKey.Contains("warlock"))
            {
                if (string.Equals(skillGcType, "skills.generic.ShadowLightning", StringComparison.OrdinalIgnoreCase)) return 100;
                if (string.Equals(skillGcType, "skills.generic.FireBolt", StringComparison.OrdinalIgnoreCase)) return 105;
                if (string.Equals(skillGcType, "skills.generic.MageClassPassive", StringComparison.OrdinalIgnoreCase)) return 108;
                if (string.Equals(skillGcType, "skills.generic.MagicDamageModPassive", StringComparison.OrdinalIgnoreCase)) return 109;
            }

            return -1;
        }


        public static SavedCharacter GetCharacter(uint characterId)
        {
            try
            {
                using (var connection = GameDatabase.GetConnection())
                {
                    SavedCharacter character = null;

                    using (var reader = GameDatabase.ExecuteReader(connection,
                        "SELECT * FROM characters WHERE id = @id", ("@id", (int)characterId)))
                    {
                        if (!reader.Read()) return null;
                        character = ReadCharacterRow(reader);
                    }

                    character.equipment = LoadEquipment(connection, (int)characterId);
                    ApplyStartingEquipmentLevels(character);
                    character.skills = LoadSkillList(connection, (int)characterId);
                    character.skillLevels = LoadSkillLevels(connection, (int)characterId);
                    character.hotbarSlots = LoadHotbarSlots(connection, (int)characterId);
                    character.inventory = LoadInventory(connection, (int)characterId);
                    character.activeQuests = LoadActiveQuests(connection, (int)characterId);
                    character.completedQuests = LoadCompletedQuests(connection, (int)characterId);
                    character.unlockedCheckpoints = LoadCheckpoints(connection, (int)characterId);

                    if (character.posseId != 0)
                    {
                        object posseNameValue = GameDatabase.ExecuteScalar(connection,
                            "SELECT name FROM posses WHERE id = @id", ("@id", (int)character.posseId));
                        character.posseName = posseNameValue == null || posseNameValue == DBNull.Value ? "" : Convert.ToString(posseNameValue);
                    }

                    return character;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DB-CHAR] operation=GetCharacter state=failed message='{ex.Message}'");
                return null;
            }
        }

        public static SavedCharacter GetCharacterByName(string accountName, string characterName)
        {
            try
            {
                using (var connection = GameDatabase.GetConnection())
                {
                    object characterIdValue = GameDatabase.ExecuteScalar(connection,
                        "SELECT id FROM characters WHERE name = @n", ("@n", characterName));
                    if (characterIdValue == null) return null;
                    return GetCharacter(Convert.ToUInt32(characterIdValue));
                }
            }
            catch { return null; }
        }

        public static List<SavedCharacter> GetCharactersForAccount(string accountName)
        {
            var result = new List<SavedCharacter>();
            try
            {
                uint accountId = AccountRepository.GetAccountId(accountName);
                if (accountId == 0) return result;

                using (var connection = GameDatabase.GetConnection())
                {
                    var characterIds = new List<int>();
                    using (var reader = GameDatabase.ExecuteReader(connection,
                        "SELECT id FROM characters WHERE account_id = @aid",
                        ("@aid", (int)accountId)))
                    {
                        while (reader.Read())
                            characterIds.Add(reader.GetInt32(0));
                    }

                    foreach (int characterId in characterIds)
                        result.Add(GetCharacter((uint)characterId));
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DB-CHAR] operation=GetForAccount state=failed message='{ex.Message}'");
            }
            return result;
        }

        public static bool CharacterNameExists(string name)
        {
            try
            {
                using (var connection = GameDatabase.GetConnection())
                {
                    object characterCount = GameDatabase.ExecuteScalar(connection,
                        "SELECT COUNT(*) FROM characters WHERE name = @n", ("@n", name));
                    return Convert.ToInt32(characterCount) > 0;
                }
            }
            catch { return false; }
        }


        public static void SaveCharacter(SavedCharacter character, [CallerMemberName] string caller = null)
        {
            try
            {
                using (var connection = GameDatabase.GetConnection())
                using (var transaction = connection.BeginTransaction())
                {
                    object oldRow = GameDatabase.ExecuteScalar(connection,
                        "SELECT printf('level=%d xp=%d current_hp=%d current_mana=%d max_hp=%d max_mana=%d zone=%s', level, experience, current_hp, current_mana, max_hp, max_mana, current_zone) FROM characters WHERE id = @id",
                        ("@id", (int)character.id));
                    if (character.maxHP <= 0 || character.maxMana <= 0)
                    {
                        object oldMaxHP = GameDatabase.ExecuteScalar(connection, "SELECT max_hp FROM characters WHERE id = @id", ("@id", (int)character.id));
                        object oldMaxMana = GameDatabase.ExecuteScalar(connection, "SELECT max_mana FROM characters WHERE id = @id", ("@id", (int)character.id));
                        bool preservedMaxHP = false;
                        bool preservedMaxMana = false;
                        if (character.maxHP <= 0 && oldMaxHP != null && oldMaxHP != DBNull.Value)
                        {
                            int preserved = Convert.ToInt32(oldMaxHP);
                            if (preserved > 0)
                            {
                                character.maxHP = preserved;
                                preservedMaxHP = true;
                            }
                        }
                        if (character.maxMana <= 0 && oldMaxMana != null && oldMaxMana != DBNull.Value)
                        {
                            int preserved = Convert.ToInt32(oldMaxMana);
                            if (preserved > 0)
                            {
                                character.maxMana = preserved;
                                preservedMaxMana = true;
                            }
                        }
                        Debug.LogError($"[SAVE-CONTEXT] caller={caller ?? "unknown"} id={character.id} preserveMaxResources maxHP={character.maxHP} preservedHP={preservedMaxHP} maxMana={character.maxMana} preservedMana={preservedMaxMana}");
                    }
                    Debug.LogError($"[SAVE-CONTEXT] caller={caller ?? "unknown"} id={character.id} name='{character.name}' old='{oldRow ?? "<missing>"}' outgoing=level={character.level} xp={character.experience} hp={character.currentHP} mana={character.currentMana} maxHP={character.maxHP} maxMana={character.maxMana} zone={character.currentZoneName ?? ""}");

                    GameDatabase.ExecuteNonQuery(connection, @"
                        UPDATE characters SET
                            level = @lv, experience = @xp, gold = @g,
                            avatar_class = @ac,
                            skin = @sk, face = @fc, face_feature = @ff,
                            hair = @hr, hair_color = @hc,
                            current_zone = @zone, zone_id = @zid,
                            position_x = @px, position_y = @py, position_z = @pz,
                            current_hp = @hp, current_mana = @mp,
                            max_hp = @mxhp, max_mana = @mxmp,
                            stat_strength = @str, stat_agility = @agi,
                            stat_intellect = @int, stat_endurance = @end,
                            last_respec_time = @lrt,
                            respec_count = @rsc, pvp_wins = @pvpw, pvp_rating = @pvpr,
                            tp_zone = @tpz, tp_zone_id = @tpzid, tp_target_zone = @tptz,
                            tp_pos_x = @tppx, tp_pos_y = @tppy, tp_pos_z = @tppz,
                            posse_id = @pid, posse_join_cooldown = @pcd
                        WHERE id = @id",
                        ("@id", (int)character.id), ("@lv", (int)character.level), ("@xp", (int)character.experience),
                        ("@g", (int)character.gold), ("@ac", character.avatarClass ?? ""),
                        ("@sk", (int)character.skin), ("@fc", (int)character.face),
                        ("@ff", (int)character.faceFeature), ("@hr", (int)character.hair), ("@hc", (int)character.hairColor),
                        ("@zone", character.currentZoneName ?? "tutorial"), ("@zid", character.zoneId),
                        ("@px", character.position.x), ("@py", character.position.y), ("@pz", character.position.z),
                        ("@hp", (int)character.currentHP), ("@mp", (int)character.currentMana),
                        ("@mxhp", character.maxHP), ("@mxmp", character.maxMana),
                        ("@str", character.statStrength), ("@agi", character.statAgility),
                        ("@int", character.statIntellect), ("@end", character.statEndurance),
                        ("@lrt", character.lastRespecTime),
                        ("@rsc", character.respecCount), ("@pvpw", character.pvpWins), ("@pvpr", character.pvpRating),
                        ("@tpz", character.tpZone ?? ""), ("@tpzid", character.tpZoneId),
                        ("@tptz", character.tpTargetZone ?? ""),
                        ("@tppx", character.tpPosX), ("@tppy", character.tpPosY), ("@tppz", character.tpPosZ),
                        ("@pid", (int)character.posseId), ("@pcd", character.posseJoinCooldown));

                    GameDatabase.ExecuteNonQuery(connection, "DELETE FROM character_equipment WHERE character_id = @cid", ("@cid", (int)character.id));
                    if (character.equipment != null)
                    {
                        var rarityBySlot = character.equipment.slotRarity ?? new Dictionary<string, int>();
                        var levelBySlot = character.equipment.slotLevel ?? new Dictionary<string, int>();
                        int slotRarity; int slotLevel;
                        InsertEquipment(connection, (int)character.id, "weapon", character.equipment.weapon, rarityBySlot.TryGetValue("weapon", out slotRarity) ? slotRarity : -1, levelBySlot.TryGetValue("weapon", out slotLevel) ? slotLevel : -1);
                        InsertEquipment(connection, (int)character.id, "armor", character.equipment.armor, rarityBySlot.TryGetValue("armor", out slotRarity) ? slotRarity : -1, levelBySlot.TryGetValue("armor", out slotLevel) ? slotLevel : -1);
                        InsertEquipment(connection, (int)character.id, "helmet", character.equipment.helmet, rarityBySlot.TryGetValue("helmet", out slotRarity) ? slotRarity : -1, levelBySlot.TryGetValue("helmet", out slotLevel) ? slotLevel : -1);
                        InsertEquipment(connection, (int)character.id, "gloves", character.equipment.gloves, rarityBySlot.TryGetValue("gloves", out slotRarity) ? slotRarity : -1, levelBySlot.TryGetValue("gloves", out slotLevel) ? slotLevel : -1);
                        InsertEquipment(connection, (int)character.id, "boots", character.equipment.boots, rarityBySlot.TryGetValue("boots", out slotRarity) ? slotRarity : -1, levelBySlot.TryGetValue("boots", out slotLevel) ? slotLevel : -1);
                        InsertEquipment(connection, (int)character.id, "shoulders", character.equipment.shoulders ?? "", rarityBySlot.TryGetValue("shoulders", out slotRarity) ? slotRarity : -1, levelBySlot.TryGetValue("shoulders", out slotLevel) ? slotLevel : -1);
                        InsertEquipment(connection, (int)character.id, "shield", character.equipment.shield ?? "", rarityBySlot.TryGetValue("shield", out slotRarity) ? slotRarity : -1, levelBySlot.TryGetValue("shield", out slotLevel) ? slotLevel : -1);
                        InsertEquipment(connection, (int)character.id, "ring1", character.equipment.ring1 ?? "", rarityBySlot.TryGetValue("ring1", out slotRarity) ? slotRarity : -1, levelBySlot.TryGetValue("ring1", out slotLevel) ? slotLevel : -1);
                        InsertEquipment(connection, (int)character.id, "ring2", character.equipment.ring2 ?? "", rarityBySlot.TryGetValue("ring2", out slotRarity) ? slotRarity : -1, levelBySlot.TryGetValue("ring2", out slotLevel) ? slotLevel : -1);
                        InsertEquipment(connection, (int)character.id, "amulet", character.equipment.amulet ?? "", rarityBySlot.TryGetValue("amulet", out slotRarity) ? slotRarity : -1, levelBySlot.TryGetValue("amulet", out slotLevel) ? slotLevel : -1);
                    }

                    GameDatabase.ExecuteNonQuery(connection, "DELETE FROM character_inventory WHERE character_id = @cid", ("@cid", (int)character.id));
                    if (character.inventory != null)
                    {
                        foreach (var item in character.inventory)
                        {
                            GameDatabase.ExecuteNonQuery(connection,
                                "INSERT INTO character_inventory (character_id, gc_class, slot_x, slot_y, count, buy_price, rarity, stored_level, container_id) VALUES (@cid, @gc, @x, @y, @c, @bp, @r, @sl, @cont)",
                                ("@cid", (int)character.id), ("@gc", item.gcClass), ("@x", (int)item.x), ("@y", (int)item.y), ("@c", item.count), ("@bp", (int)item.buyPrice), ("@r", item.rarity), ("@sl", item.storedLevel), ("@cont", (int)item.containerId));
                        }
                    }

                    GameDatabase.ExecuteNonQuery(connection, "DELETE FROM character_skills WHERE character_id = @cid", ("@cid", (int)character.id));
                    if (character.skills != null)
                    {
                        foreach (var skill in character.skills)
                        {
                            int level = character.GetSkillLevel(skill);
                            int hotbar = -1;
                            if (character.hotbarSlots != null)
                            {
                                foreach (var hotbarSlotEntry in character.hotbarSlots)
                                    if (hotbarSlotEntry.skill == skill) { hotbar = (int)hotbarSlotEntry.slot; break; }
                            }
                            GameDatabase.ExecuteNonQuery(connection,
                                "INSERT INTO character_skills (character_id, skill_gc_class, level, hotbar_slot) VALUES (@cid, @s, @l, @h)",
                                ("@cid", (int)character.id), ("@s", skill), ("@l", level), ("@h", hotbar));
                        }
                    }

                    GameDatabase.ExecuteNonQuery(connection, "DELETE FROM character_quests WHERE character_id = @cid AND status = 'active'", ("@cid", (int)character.id));
                    GameDatabase.ExecuteNonQuery(connection, "DELETE FROM quest_objectives WHERE character_id = @cid", ("@cid", (int)character.id));
                    if (character.activeQuests != null)
                    {
                        foreach (var quest in character.activeQuests)
                        {
                            GameDatabase.ExecuteNonQuery(connection,
                                "INSERT OR REPLACE INTO character_quests (character_id, quest_id, quest_giver_id, accepted_at, status) VALUES (@cid, @qid, @gid, @at, 'active')",
                                ("@cid", (int)character.id), ("@qid", quest.questId), ("@gid", quest.questGiverId ?? ""), ("@at", quest.acceptedAt ?? ""));

                            if (quest.objectives != null)
                            {
                                foreach (var objective in quest.objectives)
                                {
                                    GameDatabase.ExecuteNonQuery(connection,
                                        "INSERT INTO quest_objectives (character_id, quest_id, objective_name, type, target, label, required, current) VALUES (@cid, @qid, @on, @t, @tgt, @lb, @req, @cur)",
                                        ("@cid", (int)character.id), ("@qid", quest.questId), ("@on", objective.objectiveName ?? ""),
                                        ("@t", objective.type ?? ""), ("@tgt", objective.target ?? ""), ("@lb", objective.label ?? ""),
                                        ("@req", objective.required), ("@cur", objective.current));
                                }
                            }
                        }
                    }

                    GameDatabase.ExecuteNonQuery(connection, "DELETE FROM completed_quests WHERE character_id = @cid", ("@cid", (int)character.id));
                    if (character.completedQuests != null)
                    {
                        foreach (var questId in character.completedQuests)
                        {
                            GameDatabase.ExecuteNonQuery(connection,
                                "INSERT OR IGNORE INTO completed_quests (character_id, quest_id) VALUES (@cid, @qid)",
                                ("@cid", (int)character.id), ("@qid", questId));
                        }
                    }

                    GameDatabase.ExecuteNonQuery(connection, "DELETE FROM character_checkpoints WHERE character_id = @cid", ("@cid", (int)character.id));
                    if (character.unlockedCheckpoints != null)
                    {
                        foreach (var checkpointId in character.unlockedCheckpoints)
                        {
                            GameDatabase.ExecuteNonQuery(connection,
                                "INSERT OR IGNORE INTO character_checkpoints (character_id, checkpoint_id) VALUES (@cid, @cp)",
                                ("@cid", (int)character.id), ("@cp", checkpointId));
                        }
                    }

                    transaction.Commit();
                    Debug.LogError($"[DB-CHAR] Saved '{character.name}' lv={character.level} xp={character.experience} caller={caller ?? "unknown"} hp={character.currentHP}/{character.maxHP} mana={character.currentMana}/{character.maxMana}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DB-CHAR] operation=SaveCharacter state=failed message='{ex.Message}' stack='{ex.StackTrace}'");
            }
        }


        public static bool DeleteCharacter(uint characterId)
        {
            try
            {
                using (var connection = GameDatabase.GetConnection())
                using (var transaction = connection.BeginTransaction())
                {
                    string characterName = "";
                    uint posseId = 0;
                    bool isFounder = false;
                    using (var reader = GameDatabase.ExecuteReader(connection,
                        @"SELECT c.name, COALESCE(c.posse_id, 0), CASE WHEN p.founder_character_id = c.id THEN 1 ELSE 0 END
                          FROM characters c
                          LEFT JOIN posses p ON p.id = c.posse_id
                          WHERE c.id = @id",
                        ("@id", (int)characterId)))
                    {
                        if (!reader.Read())
                        {
                            Debug.LogError($"[DB-CHAR] action=delete state=missing id={characterId}");
                            return false;
                        }
                        characterName = reader.GetString(0);
                        posseId = (uint)reader.GetInt32(1);
                        isFounder = reader.GetInt32(2) != 0;
                    }

                    if (posseId != 0)
                    {
                        if (isFounder)
                        {
                            GameDatabase.ExecuteNonQuery(connection,
                                "UPDATE characters SET posse_id = 0 WHERE posse_id = @pid",
                                ("@pid", (int)posseId));
                            GameDatabase.ExecuteNonQuery(connection,
                                "DELETE FROM posses WHERE id = @pid",
                                ("@pid", (int)posseId));
                            Debug.LogError($"[DB-CHAR] action=disbandFoundedPosse posseId={posseId} characterId={characterId}");
                        }
                        else
                        {
                            GameDatabase.ExecuteNonQuery(connection,
                                "UPDATE characters SET posse_id = 0 WHERE id = @id",
                                ("@id", (int)characterId));
                        }
                    }

                    if (Convert.ToInt32(GameDatabase.ExecuteScalar(connection,
                        "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='social_friends_v2'")) != 0)
                    {
                        GameDatabase.ExecuteNonQuery(connection,
                            "DELETE FROM social_friends_v2 WHERE character_name = @name OR friend_name = @name",
                            ("@name", characterName));
                    }
                    if (Convert.ToInt32(GameDatabase.ExecuteScalar(connection,
                        "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='social_ignores_v2'")) != 0)
                    {
                        GameDatabase.ExecuteNonQuery(connection,
                            "DELETE FROM social_ignores_v2 WHERE character_name = @name OR ignore_name = @name",
                            ("@name", characterName));
                    }

                    GameDatabase.ExecuteNonQuery(connection,
                        "UPDATE accounts SET current_character_id = 0 WHERE current_character_id = @id",
                        ("@id", (int)characterId));
                    GameDatabase.ExecuteNonQuery(connection,
                        "DELETE FROM characters WHERE id = @id",
                        ("@id", (int)characterId));
                    int deleted = Convert.ToInt32(GameDatabase.ExecuteScalar(connection, "SELECT changes()") ?? 0);
                    transaction.Commit();
                    Debug.LogError($"[DB-CHAR] action=delete id={characterId} deleted={deleted}");
                    return deleted != 0;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DB-CHAR] operation=Delete state=failed message='{ex.Message}'");
                return false;
            }
        }


        private static SavedCharacter ReadCharacterRow(SqliteDataReader r)
        {
            return new SavedCharacter
            {
                id = (uint)GameDatabase.GetInt(r, "id"),
                accountId = (uint)GameDatabase.GetInt(r, "account_id"),
                name = GameDatabase.GetString(r, "name"),
                className = GameDatabase.GetString(r, "class_name", "Fighter"),
                avatarClass = GameDatabase.GetString(r, "avatar_class"),
                level = (byte)GameDatabase.GetInt(r, "level", 1),
                experience = (uint)GameDatabase.GetInt(r, "experience"),
                gold = (uint)GameDatabase.GetInt(r, "gold", 100),
                skin = (byte)GameDatabase.GetInt(r, "skin"),
                face = (byte)GameDatabase.GetInt(r, "face"),
                faceFeature = (byte)GameDatabase.GetInt(r, "face_feature"),
                hair = (byte)GameDatabase.GetInt(r, "hair"),
                hairColor = (byte)GameDatabase.GetInt(r, "hair_color"),
                zoneId = GameDatabase.GetInt(r, "zone_id"),
                currentZoneName = GameDatabase.GetString(r, "current_zone", "tutorial"),
                position = new Vector3(
                    GameDatabase.GetFloat(r, "position_x"),
                    GameDatabase.GetFloat(r, "position_y"),
                    GameDatabase.GetFloat(r, "position_z")),
                currentHP = (uint)GameDatabase.GetInt(r, "current_hp"),
                currentMana = (uint)GameDatabase.GetInt(r, "current_mana"),
                maxHP = GameDatabase.GetInt(r, "max_hp"),
                maxMana = GameDatabase.GetInt(r, "max_mana"),
                tpZone = GameDatabase.GetString(r, "tp_zone", ""),
                tpZoneId = GameDatabase.GetInt(r, "tp_zone_id"),
                tpTargetZone = GameDatabase.GetString(r, "tp_target_zone", ""),
                tpPosX = GameDatabase.GetFloat(r, "tp_pos_x"),
                tpPosY = GameDatabase.GetFloat(r, "tp_pos_y"),
                tpPosZ = GameDatabase.GetFloat(r, "tp_pos_z"),
                statStrength = GameDatabase.GetInt(r, "stat_strength"),
                statAgility = GameDatabase.GetInt(r, "stat_agility"),
                statIntellect = GameDatabase.GetInt(r, "stat_intellect"),
                statEndurance = GameDatabase.GetInt(r, "stat_endurance"),
                lastRespecTime = GameDatabase.GetInt(r, "last_respec_time"),
                respecCount = GameDatabase.GetInt(r, "respec_count"),
                pvpWins = GameDatabase.GetInt(r, "pvp_wins"),
                pvpRating = GameDatabase.GetInt(r, "pvp_rating"),
                posseId = GameDatabase.GetUInt(r, "posse_id"),
                posseJoinCooldown = GameDatabase.GetInt(r, "posse_join_cooldown"),
                posseRankId = GameDatabase.GetInt(r, "posse_rank_id"),
            };
        }

        private static void InsertEquipment(SqliteConnection connection, int charId, string slot, string gcClass, int rarity = -1, int storedLevel = -1)
        {
            GameDatabase.ExecuteNonQuery(connection,
                "INSERT INTO character_equipment (character_id, slot, gc_class, rarity, stored_level) VALUES (@cid, @s, @gc, @r, @sl)",
                ("@cid", charId), ("@s", slot), ("@gc", gcClass ?? ""), ("@r", rarity), ("@sl", storedLevel));
        }

        private static void InsertStartingEquipment(SqliteConnection connection, int charId, StartingEquipment equipment, string slot, string gcClass)
        {
            InsertEquipment(connection, charId, slot, gcClass ?? "", -1, StartingSlotLevel(equipment, slot));
        }

        private static StartingEquipment LoadEquipment(SqliteConnection connection, int charId)
        {
            var equipment = new StartingEquipment();
            using (var reader = GameDatabase.ExecuteReader(connection,
                "SELECT slot, gc_class, COALESCE(rarity, -1), COALESCE(stored_level, -1) FROM character_equipment WHERE character_id = @cid",
                ("@cid", charId)))
            {
                while (reader.Read())
                {
                    string slot = reader.GetString(0);
                    string gcClass = reader.GetString(1);
                    int rarity = reader.GetInt32(2);
                    int storedLevel = reader.GetInt32(3);

                    bool invalidRow = false;
                    string invalidReason = "";
                    switch (slot)
                    {
                        case "weapon":
                        case "armor":
                        case "helmet":
                        case "gloves":
                        case "boots":
                        case "shoulders":
                        case "shield":
                        case "ring1":
                        case "ring2":
                        case "amulet":
                            break;
                        default:
                            invalidRow = true; invalidReason += $"unknown-slot({slot}) "; break;
                    }
                    if (rarity != -1 && (rarity < 0 || rarity > 5))
                    { invalidRow = true; invalidReason += $"rarity-out-of-range({rarity}) "; }
                    if (storedLevel != -1 && (storedLevel < 1 || storedLevel > 120))
                    { invalidRow = true; invalidReason += $"stored_level-out-of-range({storedLevel}) "; }

                    if (invalidRow)
                    {
                        Debug.LogError(
                            $"[EQUIP-VALIDATOR] CORRUPT ROW in character_equipment " +
                            $"char_id={charId} slot='{slot}' gc_class='{gcClass ?? "NULL"}' " +
                            $"rarity={rarity} stored_level={storedLevel} " +
                            $"reasons={invalidReason.Trim()}");
                        Debug.LogError(
                            $"[EQUIP-VALIDATOR] clamping row to safe defaults before passing to serializer");
                        if (rarity != -1 && (rarity < 0 || rarity > 5)) rarity = -1;
                        if (storedLevel != -1 && (storedLevel < 1 || storedLevel > 120)) storedLevel = -1;
                        switch (slot)
                        {
                            case "weapon":
                            case "armor":
                            case "helmet":
                            case "gloves":
                            case "boots":
                            case "shoulders":
                            case "shield":
                            case "ring1":
                            case "ring2":
                            case "amulet": break;
                            default:
                                Debug.LogError($"[EQUIP-VALIDATOR] SKIPPING row (unknown slot '{slot}')");
                                continue;
                        }
                    }

                    equipment.slotRarity[slot] = rarity;
                    equipment.slotLevel[slot] = storedLevel;
                    switch (slot)
                    {
                        case "weapon": equipment.weapon = gcClass; break;
                        case "armor": equipment.armor = gcClass; break;
                        case "helmet": equipment.helmet = gcClass; break;
                        case "gloves": equipment.gloves = gcClass; break;
                        case "boots": equipment.boots = gcClass; break;
                        case "shoulders": equipment.shoulders = gcClass; break;
                        case "shield": equipment.shield = gcClass; break;
                        case "ring1": equipment.ring1 = gcClass; break;
                        case "ring2": equipment.ring2 = gcClass; break;
                        case "amulet": equipment.amulet = gcClass; break;
                    }
                }
            }
            return equipment;
        }

        private static void ApplyStartingEquipmentLevels(SavedCharacter character)
        {
            if (character == null || character.equipment == null) return;

            string classKey = ResolveClassConfigKey(character);
            if (string.IsNullOrEmpty(classKey)) return;

            var classDef = ClassConfig.GetClassDefinition(classKey);
            var starting = classDef?.startingEquipment;
            if (starting == null) return;

            if (character.equipment.slotLevel == null)
                character.equipment.slotLevel = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            ApplyStartingSlotLevel(character, starting, "weapon");
            ApplyStartingSlotLevel(character, starting, "armor");
            ApplyStartingSlotLevel(character, starting, "helmet");
            ApplyStartingSlotLevel(character, starting, "gloves");
            ApplyStartingSlotLevel(character, starting, "boots");
            ApplyStartingSlotLevel(character, starting, "shoulders");
            ApplyStartingSlotLevel(character, starting, "shield");
            ApplyStartingSlotLevel(character, starting, "ring1");
            ApplyStartingSlotLevel(character, starting, "ring2");
            ApplyStartingSlotLevel(character, starting, "amulet");
        }

        private static void ApplyStartingSlotLevel(SavedCharacter character, StartingEquipment starting, string slot)
        {
            int level = StartingSlotLevel(starting, slot);
            if (level <= 0) return;

            string currentGc = GetEquipmentSlot(character.equipment, slot);
            string startingGc = GetEquipmentSlot(starting, slot);
            if (string.IsNullOrWhiteSpace(currentGc) || !SameGcClass(currentGc, startingGc))
                return;

            if (character.equipment.slotLevel.TryGetValue(slot, out int currentLevel) && currentLevel > 0)
                return;

            character.equipment.slotLevel[slot] = level;
            Debug.LogError($"[EQUIP-CLIENT-LEVEL] char={character.id} class={character.className} slot={slot} gc={currentGc} stored_level={level} source=PKG-starting-equipment");
        }

        private static int StartingSlotLevel(StartingEquipment equipment, string slot)
        {
            if (equipment?.slotLevel != null && equipment.slotLevel.TryGetValue(slot, out int level) && level > 0)
                return level;
            return -1;
        }

        private static string ResolveClassConfigKey(SavedCharacter character)
        {
            string combined = $"{character.className} {character.avatarClass}".ToLowerInvariant();
            if (combined.Contains("ranger")) return "Ranger";
            if (combined.Contains("mage") || combined.Contains("warlock")) return "Mage";
            if (combined.Contains("fighter") || combined.Contains("warrior")) return "Fighter";
            return character.className;
        }

        private static string GetEquipmentSlot(StartingEquipment equipment, string slot)
        {
            if (equipment == null) return "";
            switch (slot)
            {
                case "weapon": return equipment.weapon;
                case "armor": return equipment.armor;
                case "helmet": return equipment.helmet;
                case "gloves": return equipment.gloves;
                case "boots": return equipment.boots;
                case "shoulders": return equipment.shoulders;
                case "shield": return equipment.shield;
                case "ring1": return equipment.ring1;
                case "ring2": return equipment.ring2;
                case "amulet": return equipment.amulet;
                default: return "";
            }
        }

        private static bool SameGcClass(string left, string right)
        {
            return string.Equals(NormalizeGcClass(left), NormalizeGcClass(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeGcClass(string gcClass)
        {
            return (gcClass ?? "").Trim().Replace('\\', '.').Replace('/', '.');
        }

        private static List<string> LoadSkillList(SqliteConnection connection, int charId)
        {
            var skills = new List<string>();
            using (var reader = GameDatabase.ExecuteReader(connection,
                "SELECT skill_gc_class FROM character_skills WHERE character_id = @cid",
                ("@cid", charId)))
            {
                while (reader.Read())
                    skills.Add(reader.GetString(0));
            }
            return skills;
        }

        private static List<SkillLevelEntry> LoadSkillLevels(SqliteConnection connection, int charId)
        {
            var levels = new List<SkillLevelEntry>();
            using (var reader = GameDatabase.ExecuteReader(connection,
                "SELECT skill_gc_class, level FROM character_skills WHERE character_id = @cid",
                ("@cid", charId)))
            {
                while (reader.Read())
                    levels.Add(new SkillLevelEntry { skill = reader.GetString(0), level = reader.GetInt32(1) });
            }
            return levels;
        }

        private static List<HotbarSlotEntry> LoadHotbarSlots(SqliteConnection connection, int charId)
        {
            var slots = new List<HotbarSlotEntry>();
            using (var reader = GameDatabase.ExecuteReader(connection,
                "SELECT skill_gc_class, hotbar_slot FROM character_skills WHERE character_id = @cid AND hotbar_slot >= 0",
                ("@cid", charId)))
            {
                while (reader.Read())
                    slots.Add(new HotbarSlotEntry { skill = reader.GetString(0), slot = (uint)reader.GetInt32(1) });
            }
            return slots;
        }

        private static List<SavedInventoryItem> LoadInventory(SqliteConnection connection, int charId)
        {
            var items = new List<SavedInventoryItem>();
            using (var reader = GameDatabase.ExecuteReader(connection,
                "SELECT gc_class, slot_x, slot_y, count, COALESCE(buy_price, 0), COALESCE(rarity, -1), COALESCE(stored_level, -1), COALESCE(container_id, 11) FROM character_inventory WHERE character_id = @cid",
                ("@cid", charId)))
            {
                while (reader.Read())
                {
                    string gcClass = reader.GetString(0);
                    int slotX = reader.GetInt32(1);
                    int slotY = reader.GetInt32(2);
                    int count = reader.GetInt32(3);
                    int buyPrice = reader.GetInt32(4);
                    int rarity = reader.GetInt32(5);
                    int storedLevel = reader.GetInt32(6);
                    int containerId = reader.GetInt32(7);

                    bool isBank = (containerId == 0x0C) || (containerId >= 0x0E && containerId <= 0x13);
                    int maxY = isBank ? 13 : 7;

                    bool invalidRow = false;
                    string invalidReason = "";
                    if (string.IsNullOrEmpty(gcClass))
                    { invalidRow = true; invalidReason += "empty-gc_class "; }
                    if (slotX < 0 || slotX > 9)
                    { invalidRow = true; invalidReason += $"slot_x-out-of-range({slotX}) "; }
                    if (slotY < 0 || slotY > maxY)
                    { invalidRow = true; invalidReason += $"slot_y-out-of-range({slotY}) "; }
                    if (count < 1)
                    { invalidRow = true; invalidReason += $"count-invalid({count}) "; }
                    if (rarity != -1 && (rarity < 0 || rarity > 5))
                    { invalidRow = true; invalidReason += $"rarity-out-of-range({rarity}) "; }
                    if (storedLevel != -1 && (storedLevel < 1 || storedLevel > 120))
                    { invalidRow = true; invalidReason += $"stored_level-out-of-range({storedLevel}) "; }

                    if (invalidRow)
                    {
                        Debug.LogError(
                            $"[INV-VALIDATOR] CORRUPT ROW in character_inventory " +
                            $"char_id={charId} container=0x{containerId:X2} slot=({slotX},{slotY}) gc_class='{gcClass ?? "NULL"}' " +
                            $"count={count} rarity={rarity} stored_level={storedLevel} " +
                            $"buy_price={buyPrice} reasons={invalidReason.Trim()}");
                        if (string.IsNullOrEmpty(gcClass) || slotX < 0 || slotX > 9 || slotY < 0 || slotY > maxY)
                        {
                            Debug.LogError($"[INV-VALIDATOR] SKIPPING row (unrepairable)");
                            continue;
                        }
                        if (count < 1) count = 1;
                        if (rarity != -1 && (rarity < 0 || rarity > 5)) rarity = -1;
                        if (storedLevel != -1 && (storedLevel < 1 || storedLevel > 120)) storedLevel = -1;
                        Debug.LogError($"[INV-VALIDATOR] clamped: count={count} rarity={rarity} stored_level={storedLevel}");
                    }

                    items.Add(new SavedInventoryItem
                    {
                        gcClass = gcClass,
                        x = (byte)slotX,
                        y = (byte)slotY,
                        count = count,
                        buyPrice = (uint)buyPrice,
                        rarity = rarity,
                        storedLevel = storedLevel,
                        containerId = (byte)containerId
                    });
                }
            }
            return items;
        }

        private static List<SavedQuest> LoadActiveQuests(SqliteConnection connection, int charId)
        {
            var quests = new List<SavedQuest>();
            using (var reader = GameDatabase.ExecuteReader(connection,
                "SELECT quest_id, quest_giver_id, accepted_at FROM character_quests WHERE character_id = @cid AND status = 'active'",
                ("@cid", charId)))
            {
                while (reader.Read())
                {
                    var quest = new SavedQuest
                    {
                        questId = reader.GetString(0),
                        questGiverId = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        acceptedAt = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        objectives = new List<SavedQuestObjective>()
                    };
                    quests.Add(quest);
                }
            }

            foreach (var quest in quests)
            {
                using (var reader = GameDatabase.ExecuteReader(connection,
                    "SELECT objective_name, type, target, label, required, current FROM quest_objectives WHERE character_id = @cid AND quest_id = @qid",
                    ("@cid", charId), ("@qid", quest.questId)))
                {
                    while (reader.Read())
                    {
                        quest.objectives.Add(new SavedQuestObjective
                        {
                            objectiveName = reader.IsDBNull(0) ? "" : reader.GetString(0),
                            type = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            target = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            label = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            required = reader.GetInt32(4),
                            current = reader.GetInt32(5)
                        });
                    }
                }
            }

            return quests;
        }

        private static List<string> LoadCompletedQuests(SqliteConnection connection, int charId)
        {
            var quests = new List<string>();
            using (var reader = GameDatabase.ExecuteReader(connection,
                "SELECT quest_id FROM completed_quests WHERE character_id = @cid",
                ("@cid", charId)))
            {
                while (reader.Read())
                    quests.Add(reader.GetString(0));
            }
            return quests;
        }

        private static List<string> LoadCheckpoints(SqliteConnection connection, int charId)
        {
            var checkpoints = new List<string>();
            using (var reader = GameDatabase.ExecuteReader(connection,
                "SELECT checkpoint_id FROM character_checkpoints WHERE character_id = @cid",
                ("@cid", charId)))
            {
                while (reader.Read())
                {
                    string checkpointId = reader.GetString(0);
                    if (!checkpointId.StartsWith("world.checkpoints.", System.StringComparison.OrdinalIgnoreCase))
                        checkpointId = "world.checkpoints." + checkpointId;
                    checkpoints.Add(checkpointId);
                }
            }
            return checkpoints;
        }

        public static void SetPvpStats(long characterId, int wins, int rating)
        {
            try
            {
                using (var connection = GameDatabase.GetConnection())
                {
                    GameDatabase.ExecuteNonQuery(connection,
                        "UPDATE characters SET pvp_wins = @w, pvp_rating = @r WHERE id = @id",
                        ("@w", wins), ("@r", rating), ("@id", characterId));
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[CharacterRepository.SetPvpStats] char {characterId}: {ex.Message}");
            }
        }

        public static void UpdatePvpRecord(string accountName, bool isWin, int ratingDelta)
        {
            try
            {
                using (var connection = GameDatabase.GetConnection())
                {
                    GameDatabase.ExecuteNonQuery(connection, @"
                        UPDATE characters SET
                            pvp_wins   = pvp_wins + @winInc,
                            pvp_rating = MIN(3000, MAX(0, pvp_rating + @rdelta))
                        WHERE id IN (
                            SELECT c.id FROM characters c
                            INNER JOIN accounts a ON c.account_id = a.id
                            WHERE LOWER(a.name) = LOWER(@acct)
                        )",
                        ("@winInc", isWin ? 1 : 0),
                        ("@rdelta", ratingDelta),
                        ("@acct", accountName ?? ""));
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[CharacterRepository.UpdatePvpRecord] {accountName}: {ex.Message}");
            }
        }
    }
}
