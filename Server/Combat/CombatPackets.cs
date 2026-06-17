using System;
using DungeonRunners.Engine;
using DungeonRunners.Utilities;
using DungeonRunners.Networking.EntitySynchInfo;
using Org.BouncyCastle.Bcpg.Sig;

namespace DungeonRunners.Combat
{
    public static class CombatPackets
    {
        private static void WriteEntitySynchInfo(LEWriter writer, string packetName, string owner, uint ownerEntityId, uint componentId, byte subtype, byte entitySynchInfoFlags, uint entitySynchInfoHPWire, bool requireHP)
        {
            if (requireHP)
                entitySynchInfoFlags = 0x02;

            writer.WriteByte(entitySynchInfoFlags);
            if ((entitySynchInfoFlags & 0x02) != 0)
                writer.WriteUInt32(entitySynchInfoHPWire);

            string hpText = (entitySynchInfoFlags & 0x02) != 0 ? entitySynchInfoHPWire.ToString() : "none";
            Debug.LogError($"[ENTITY-SYNCH-INFO] packet={packetName} owner={owner} entity={ownerEntityId} component={componentId} sub=0x{subtype:X2} flags=0x{entitySynchInfoFlags:X2} hp={hpText}");
        }

        private static void RejectRawAliveHPSuffix(string packetName, byte entitySynchInfoFlags)
        {
            if ((entitySynchInfoFlags & 0x02) != 0)
                throw new InvalidOperationException($"{packetName} raw HP suffix is quarantined; use a resolved EntitySynchInfo payload");
        }

        private static void WriteResolvedEntitySynchInfo(LEWriter writer, string packetName, string owner, ResolvedEntitySynchInfo entitySynchInfo, bool requireHP)
        {
            if (requireHP && !entitySynchInfo.HasHP)
                throw new InvalidOperationException($"{packetName} requires a resolved HP EntitySynchInfo payload");

            entitySynchInfo.Payload.Write(writer);
            string hpText = entitySynchInfo.HasHP ? entitySynchInfo.HPWire.ToString() : "none";
            Debug.LogError($"[ENTITY-SYNCH-INFO] packet={packetName} owner={owner} entity={entitySynchInfo.OwnerEntityId} component={entitySynchInfo.ComponentId} sub=0x{entitySynchInfo.Subtype:X2} flags=0x{entitySynchInfo.Flags:X2} hp={hpText} clientNow={entitySynchInfo.Now:F3} cutoffTick={entitySynchInfo.ValidationCutoffTick} cutoffTime={entitySynchInfo.ValidationCutoffTime:F3} reason={entitySynchInfo.Reason} provenance={entitySynchInfo.Provenance}");
        }

        private static void WriteUnitReadInit(LEWriter writer, byte level, uint currentHPWire, uint currentManaWire, uint encounterObjectId)
        {
            byte unitFlags = currentManaWire > 0 ? (byte)0x06 : (byte)0x02;
            if (encounterObjectId != 0)
                unitFlags |= 0x20;
            writer.WriteByte(unitFlags);
            writer.WriteByte(level);
            writer.WriteUInt16(0);
            writer.WriteUInt16(0);
            writer.WriteUInt32(currentHPWire);
            if ((unitFlags & 0x04) != 0)
                writer.WriteUInt32(currentManaWire);
            if ((unitFlags & 0x20) != 0)
                writer.WriteUInt16((ushort)encounterObjectId);
        }

        private static void WriteBehaviorReadInitNoActions(LEWriter writer, byte endByte)
        {
            writer.WriteByte(0xFF);
            writer.WriteByte(0x00);
            writer.WriteByte(0x00);
            writer.WriteByte(endByte);
        }

        private static void WriteMonsterUnitMoverReadInit(LEWriter writer)
        {
            writer.WriteByte(0x85);
            writer.WriteByte(0x00);
            writer.WriteUInt32(0x00000000);
            writer.WriteUInt32(0x00000000);
            writer.WriteUInt32(0x00000000);
            writer.WriteUInt32(0x00000000);
            writer.WriteUInt32(0x00000000);
            writer.WriteByte(0x00);
        }

        private static void WriteUnitBehaviorReadInitNoClientControl(LEWriter writer)
        {
            writer.WriteByte(0xFF);
            writer.WriteByte(0x00);
            writer.WriteByte(0x00);
        }

        private static void WriteStateMachineReadMessageHeader(
            LEWriter writer,
            byte flags,
            ushort field10 = 0,
            ushort field12 = 0,
            ushort field14 = 0)
        {
            writer.WriteByte(flags);
            if ((flags & 0x02) != 0)
                writer.WriteUInt16(field10);
            if ((flags & 0x04) != 0)
                writer.WriteUInt16(field12);
            if ((flags & 0x08) != 0)
                writer.WriteUInt16(field14);
        }

        private static void WriteMonsterBehavior2ReadInit(
            LEWriter writer,
            byte flags,
            ushort primaryTargetId = 0,
            ushort secondaryTargetId = 0,
            ushort targetFilter = 0)
        {
            writer.WriteByte(flags);
            writer.WriteUInt32(0x00000000);
            writer.WriteUInt32(0x00000000);
            if ((flags & 0x04) != 0)
                writer.WriteUInt16(primaryTargetId);
            if ((flags & 0x08) != 0)
                writer.WriteUInt16(secondaryTargetId);
            if ((flags & 0x10) != 0)
                writer.WriteUInt16(targetFilter);
        }

        public static byte[] BuildMonsterSpawnPacket(
     Monster monster,
     uint behaviorId,
     uint skillsId,
     uint manipulatorsId,
     uint modifiersId,
     ushort targetEntityId,
     ushort playerEntityId,
     ResolvedEntitySynchInfo entitySynchInfo)
        {
            var writer = new LEWriter();
            int posX = (int)(monster.PosX * 256);
            int posY = (int)(monster.PosY * 256);
            int posZ = (int)(monster.PosZ * 256);
            int heading = (int)(monster.Heading * 256);
            byte lvl = monster.Level;
            if (lvl == 0) lvl = 1;
            if (monster.IsAlive && !entitySynchInfo.HasHP)
                throw new InvalidOperationException($"MON-SPAWN requires resolved HP for alive monster {monster.Name}#{monster.EntityId}");
            uint resolvedHPWire = entitySynchInfo.HPWire;
            if (resolvedHPWire > monster.MaxHPWire) resolvedHPWire = monster.MaxHPWire;
            if (entitySynchInfo.HasHP && resolvedHPWire != entitySynchInfo.HPWire)
            {
                entitySynchInfo = new ResolvedEntitySynchInfo(
                    EntitySynchInfoPayload.FromHP(resolvedHPWire),
                    entitySynchInfo.OwnerEntityId,
                    entitySynchInfo.ComponentId,
                    entitySynchInfo.Subtype,
                    entitySynchInfo.Now,
                    entitySynchInfo.Reason,
                    entitySynchInfo.Provenance,
                    entitySynchInfo.ValidationCutoffTick,
                    entitySynchInfo.ValidationCutoffTime,
                    entitySynchInfo.RuntimeInstanceKey,
                    entitySynchInfo.SchedulerTick,
                    entitySynchInfo.SubEntityPhase,
                    entitySynchInfo.RngPos,
                    entitySynchInfo.HpMutationSource);
            }
            uint resolvedManaWire = monster.MaxManaWire > 0 ? Math.Min(monster.CurrentManaWire, monster.MaxManaWire) : monster.CurrentManaWire;

            writer.WriteByte(0x07);

            writer.WriteByte(0x01);
            writer.WriteUInt16((ushort)monster.EntityId);
            string entityGCType = MapToBaseGCType(monster.SpawnGCType ?? monster.GCType);
            Debug.LogError($"[SPAWN-PKT] Monster {monster.Name} entityGCType='{entityGCType}' spawnGCType='{monster.SpawnGCType}' baseGCType='{monster.GCType}' pos=({monster.PosX:F2},{monster.PosY:F2},{monster.PosZ:F2}) wire=({posX},{posY},{posZ}) heading={monster.Heading:F2}/{heading} level={lvl} unitInitHPWire={resolvedHPWire}/{monster.MaxHPWire} suffixHPWire={entitySynchInfo.HPWire}/{monster.MaxHPWire} manaWire={resolvedManaWire}/{monster.MaxManaWire} aggroRange={monster.AggroRange:F2} attackRange={monster.AttackRange:F2}");
            WriteGCType(writer, entityGCType, true);

            writer.WriteByte(0x02);
            writer.WriteUInt16((ushort)monster.EntityId);

            writer.WriteUInt32(0x06);
            writer.WriteInt32(posX);
            writer.WriteInt32(posY);
            writer.WriteInt32(posZ);
            writer.WriteInt32(heading);
            writer.WriteByte(0x00);

            WriteUnitReadInit(writer, lvl, resolvedHPWire, resolvedManaWire, monster.EncounterObjectEntityId);

            writer.WriteByte(0x00);
            writer.WriteUInt16(0);
            writer.WriteUInt16(0);
            writer.WriteByte(0x00);
            writer.WriteUInt16(0);
            writer.WriteUInt32(0);
            writer.WriteByte(0x00);
            writer.WriteUInt32(0);
            writer.WriteUInt32(0);
            writer.WriteUInt32(0);

            writer.WriteByte(0x32);
            writer.WriteUInt16((ushort)monster.EntityId);
            writer.WriteUInt16((ushort)behaviorId);
            string behaviorType = monster.SpawnBehaviourType ?? monster.BehaviourType;
            Debug.LogError($"[SPAWN-PKT] Monster {monster.Name} behaviorType='{behaviorType}' (SpawnOverride='{monster.SpawnBehaviourType}' Default='{monster.BehaviourType}')");
            WriteGCType(writer, behaviorType, false);
            writer.WriteByte(0x01);

            WriteBehaviorReadInitNoActions(writer, 0x00);
            WriteMonsterUnitMoverReadInit(writer);
            WriteUnitBehaviorReadInitNoClientControl(writer);
            WriteStateMachineReadMessageHeader(writer, 0x0F, 0xFFFF, 0xFFFF, 0xFFFF);
            WriteMonsterBehavior2ReadInit(writer, 0x00);

            writer.WriteByte(0x32);
            writer.WriteUInt16((ushort)monster.EntityId);
            writer.WriteUInt16((ushort)skillsId);
            WriteGCType(writer, "skills", false);
            writer.WriteByte(0x01);
            writer.WriteByte(0xFF);
            writer.WriteByte(0xFF);
            writer.WriteByte(0xFF);
            writer.WriteByte(0xFF);
            writer.WriteByte(0x00);
            writer.WriteByte(0x00);
            writer.WriteByte(0x32);
            writer.WriteUInt16((ushort)monster.EntityId);
            writer.WriteUInt16((ushort)manipulatorsId);
            WriteGCType(writer, "manipulators", false);
            writer.WriteByte(0x01);

            var manipulatorsToSend = new System.Collections.Generic.List<ManipulatorEntry>();

            if (monster.Manipulators != null)
            {
                if (monster.Manipulators.TryGetValue("skill1", out var skill1))
                {
                    float cooldown = GetManipulatorFloat(skill1, "CoolDown");
                    float range = GetManipulatorFloat(skill1, "Range");
                    uint id = GetManipulatorUInt(skill1, "ID");
                    manipulatorsToSend.Add(new ManipulatorEntry(skill1.gcType, id, cooldown, range, ManipulatorType.ActiveSkill));
                }

                if (monster.Manipulators.TryGetValue("skill2", out var skill2))
                {
                    float cooldown = GetManipulatorFloat(skill2, "CoolDown");
                    float range = GetManipulatorFloat(skill2, "Range");
                    uint id = GetManipulatorUInt(skill2, "ID");
                    manipulatorsToSend.Add(new ManipulatorEntry(skill2.gcType, id, cooldown, range, ManipulatorType.ActiveSkill));
                }

                if (monster.Manipulators.TryGetValue("primaryweapon", out var weapon))
                {
                    float cooldown = GetManipulatorFloat(weapon, "CoolDown");
                    float range = GetManipulatorFloat(weapon, "Range");
                    uint id = GetManipulatorUInt(weapon, "ID");
                    var weaponType = IsRangedManipulator(weapon) ? ManipulatorType.RangedWeapon : ManipulatorType.MeleeWeapon;
                    manipulatorsToSend.Add(new ManipulatorEntry(weapon.gcType, id, cooldown, range, weaponType));
                }
            }

            writer.WriteByte((byte)manipulatorsToSend.Count);
            Debug.LogError($"[SPAWN-PACKET] Writing {manipulatorsToSend.Count} manipulators");

            foreach (var manip in manipulatorsToSend)
            {
                WriteGCType(writer, manip.GCType, true);
                WriteMonsterManipulatorDataInit(writer, manip);
                Debug.LogError($"[SPAWN-PACKET]   Manip: {manip.GCType} type={manip.Type} authoredId={manip.Id} range={manip.Range:F2} cooldown={manip.Cooldown:F2} clientDataInit=0x{GetMonsterManipulatorDataInitShape(manip)}");
            }

            writer.WriteByte(0x32);
            writer.WriteUInt16((ushort)monster.EntityId);
            writer.WriteUInt16((ushort)modifiersId);
            WriteGCType(writer, "modifiers", false);
            writer.WriteByte(0x01);
            writer.WriteUInt32(0);
            writer.WriteByte(0x00);
            writer.WriteUInt32(0);

            writer.WriteByte(0x35);
            writer.WriteUInt16((ushort)behaviorId);
            writer.WriteByte(0x04);
            writer.WriteByte(0x04);
            writer.WriteByte(0xFF);
            writer.WriteInt32(posX);
            writer.WriteInt32(posY);
            writer.WriteInt32(posZ);
            writer.WriteUInt16((ushort)monster.EntityId);
            WriteResolvedEntitySynchInfo(writer, "MON-SPAWN-ACTION", "Monster", entitySynchInfo, true);

            writer.WriteByte(0x35);
            writer.WriteUInt16((ushort)behaviorId);
            writer.WriteByte(0x65);
            writer.WriteByte(0x00);
            writer.WriteByte(0x01);
            writer.WriteByte(0x03);
            writer.WriteInt32(heading);
            writer.WriteInt32(posX);
            writer.WriteInt32(posY);
            WriteResolvedEntitySynchInfo(writer, "MON-SPAWN-MOVER", "Monster", new ResolvedEntitySynchInfo(entitySynchInfo.Payload, entitySynchInfo.OwnerEntityId, behaviorId, 0x65, entitySynchInfo.Now, entitySynchInfo.Reason, entitySynchInfo.Provenance, entitySynchInfo.ValidationCutoffTick, entitySynchInfo.ValidationCutoffTime, entitySynchInfo.RuntimeInstanceKey, entitySynchInfo.SchedulerTick, entitySynchInfo.SubEntityPhase, entitySynchInfo.RngPos, entitySynchInfo.HpMutationSource), true);

            writer.WriteByte(0x06);


            byte[] packet = writer.ToArray();
            Debug.LogError($"[MONSTER-SPAWN-HEX] Size: {packet.Length} hex: {BitConverter.ToString(packet)}");
            return packet;
        }

        public static byte[] BuildMonsterEntityInitHPRefreshPacket(Monster monster, uint currentHPWire)
        {
            var writer = new LEWriter();
            int posX = (int)(monster.PosX * 256);
            int posY = (int)(monster.PosY * 256);
            int posZ = (int)(monster.PosZ * 256);
            int heading = (int)(monster.Heading * 256);
            byte lvl = monster.Level;
            if (lvl == 0) lvl = 1;
            if (currentHPWire > monster.MaxHPWire) currentHPWire = monster.MaxHPWire;
            uint currentManaWire = monster.MaxManaWire > 0 ? Math.Min(monster.CurrentManaWire, monster.MaxManaWire) : monster.CurrentManaWire;

            writer.WriteByte(0x02);
            writer.WriteUInt16((ushort)monster.EntityId);
            writer.WriteUInt32(0x06);
            writer.WriteInt32(posX);
            writer.WriteInt32(posY);
            writer.WriteInt32(posZ);
            writer.WriteInt32(heading);
            writer.WriteByte(0x00);
            WriteUnitReadInit(writer, lvl, currentHPWire, currentManaWire, monster.EncounterObjectEntityId);
            writer.WriteByte(0x00);
            writer.WriteUInt16(0);
            writer.WriteUInt16(0);
            writer.WriteByte(0x00);
            writer.WriteUInt16(0);
            writer.WriteUInt32(0);
            writer.WriteByte(0x00);
            writer.WriteUInt32(0);
            writer.WriteUInt32(0);
            writer.WriteUInt32(0);
            return writer.ToArray();
        }

        public static byte[] BuildEncounterObjectSpawnPacket(uint encounterObjectId, float posX, float posY, float posZ, float heading)
        {
            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x01);
            writer.WriteUInt16((ushort)encounterObjectId);
            WriteGCType(writer, "EncounterObject", true);
            writer.WriteByte(0x02);
            writer.WriteUInt16((ushort)encounterObjectId);
            writer.WriteUInt32(0x06);
            writer.WriteInt32((int)(posX * 256));
            writer.WriteInt32((int)(posY * 256));
            writer.WriteInt32((int)(posZ * 256));
            writer.WriteInt32((int)(heading * 256));
            writer.WriteByte(0x00);
            writer.WriteByte(0x00);
            writer.WriteByte(0x00);
            writer.WriteByte(0x00);
            writer.WriteByte(0x00);
            writer.WriteByte(0x00);
            writer.WriteUInt16(0);
            writer.WriteUInt16(0);
            writer.WriteByte(0x00);
            writer.WriteByte(0x00);
            writer.WriteByte(0x00);
            writer.WriteByte(0x00);
            writer.WriteByte(0x06);
            return writer.ToArray();
        }

        public static byte[] BuildIntervalPacket(uint updateNumber, uint entityUpdateNum,
            ushort nodeCountA, ushort nodeCountB)
        {
            var writer = new LEWriter();
            writer.WriteByte(0x0D);
            writer.WriteUInt32(updateNumber);
            writer.WriteUInt32(updateNumber);
            writer.WriteUInt32(0);
            writer.WriteUInt32(entityUpdateNum);
            writer.WriteUInt16(nodeCountA);
            writer.WriteUInt16(nodeCountB);
            return writer.ToArray();
        }
        public static byte[] BuildMonsterDespawnPacket(uint entityId)
        {
            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x05);
            writer.WriteUInt16((ushort)entityId);
            writer.WriteByte(0x06);
            return writer.ToArray();
        }



        public static byte[] BuildMonsterMovePacket(uint entityId, uint behaviorId, int fxWire, int fyWire, int headingWire, ResolvedEntitySynchInfo entitySynchInfo, bool beginEndStream = true)
        {
            var writer = new LEWriter();

            if (beginEndStream)
                writer.WriteByte(0x07);

            writer.WriteByte(0x35);
            writer.WriteUInt16((ushort)behaviorId);
            writer.WriteByte(0x65);
            writer.WriteByte(0x00);
            writer.WriteByte(0x01);
            writer.WriteByte(0x03);
            writer.WriteInt32(headingWire);
            writer.WriteInt32(fxWire);
            writer.WriteInt32(fyWire);

            WriteResolvedEntitySynchInfo(writer, "MON-MOVE", "Monster", entitySynchInfo, true);

            if (beginEndStream)
                writer.WriteByte(0x06);

            return writer.ToArray();
        }

        private static void WriteGCType(LEWriter writer, string gcType, bool preserveCase)
        {
            string safeTypeName = preserveCase ? gcType : gcType.ToLower();
            writer.WriteByte(0xFF);
            writer.WriteCString(safeTypeName);
        }

        private static float GetManipulatorFloat(ManipulatorData manipulator, string property)
        {
            if (manipulator?.properties == null) return 0f;
            if (!manipulator.properties.TryGetValue(property, out string value)) return 0f;
            if (float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float result))
                return result;
            return 0f;
        }

        private static uint GetManipulatorUInt(ManipulatorData manipulator, string property)
        {
            if (manipulator?.properties == null) return 0;
            if (!manipulator.properties.TryGetValue(property, out string value)) return 0;
            if (uint.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out uint result))
                return result;
            return 0;
        }

        private static bool IsRangedManipulator(ManipulatorData manipulator)
        {
            if (manipulator?.properties == null) return false;
            return manipulator.properties.ContainsKey("ShotType")
                || manipulator.properties.ContainsKey("UseProjectile")
                || manipulator.properties.ContainsKey("ProjectileSpeed")
                || manipulator.properties.ContainsKey("ProjectileSize");
        }

        private static void WriteMonsterManipulatorDataInit(LEWriter writer, ManipulatorEntry manip)
        {
            if (manip.Type == ManipulatorType.ActiveSkill)
            {
                writer.WriteUInt32(manip.Id);
                writer.WriteByte(0x00);
                writer.WriteByte(0x00);
                return;
            }

            WriteMonsterItemReadDataNoModifiers(writer, manip.Id);
            writer.WriteUInt16(0x0000);

            if (manip.Type == ManipulatorType.MeleeWeapon)
                writer.WriteByte(0x00);

            writer.WriteUInt16(0x0000);
        }

        private static void WriteMonsterItemReadDataNoModifiers(LEWriter writer, uint itemId)
        {
            writer.WriteUInt32(itemId);
            writer.WriteByte(0x00);
            writer.WriteByte(0x00);
            writer.WriteByte(0x00);
            writer.WriteByte(0x00);
            writer.WriteByte(0x00);
            writer.WriteByte(0x00);
        }

        private static string GetMonsterManipulatorDataInitShape(ManipulatorEntry manip)
        {
            switch (manip.Type)
            {
                case ManipulatorType.ActiveSkill:
                    return UInt32LEHex(manip.Id) + "0000";
                case ManipulatorType.MeleeWeapon:
                    return UInt32LEHex(manip.Id) + "0000000000000000000000";
                case ManipulatorType.RangedWeapon:
                    return UInt32LEHex(manip.Id) + "00000000000000000000";
                default:
                    return "";
            }
        }

        private static string UInt32LEHex(uint value)
        {
            return $"{value & 0xFF:X2}{(value >> 8) & 0xFF:X2}{(value >> 16) & 0xFF:X2}{(value >> 24) & 0xFF:X2}";
        }

        private static string MapToBaseGCType(string gcType)
        {
            switch (gcType.ToLower())
            {
                case "creatures.forestcreatures.warg.basic.pup":
                    return "world.dungeon00.mob.melee01.rank1";
                case "creatures.forestcreatures.warg.basic.grunt":
                    return "world.dungeon00.mob.melee02.rank1";
                case "creatures.whiskers.broodling.basic.grunt":
                    return "world.dungeon00.mob.melee03.rank1";
                case "creatures.whiskers.blademaster.basic.grunt":
                    return "world.dungeon00.mob.melee04.rank1";
                case "creatures.whiskers.broodling.basic.champion":
                    return "world.dungeon00.mob.boss";
                case "world.objects.barrel.breakable":
                case "world.objects.barrel.breakable.02":
                case "world.objects.barrel.breakable.03":
                    return "world.dungeon00.mob.CreatureBarrel";
                default:
                    return gcType;
            }
        }

        public static byte[] BuildMonsterFollowPacket(uint monsterBehaviorId, ushort targetPlayerId, ResolvedEntitySynchInfo entitySynchInfo)
        {
            var writer = new LEWriter();
            writer.WriteByte(0x35);
            writer.WriteUInt16((ushort)monsterBehaviorId);
            writer.WriteByte(0x04);
            writer.WriteByte(0x16);
            writer.WriteByte(0x00);
            writer.WriteUInt16(targetPlayerId);
            WriteResolvedEntitySynchInfo(writer, "MON-FOLLOW", "Monster", entitySynchInfo, true);
            return writer.ToArray();
        }

        public static byte[] BuildMonsterAttackPacket(uint monsterEntityId, uint monsterBehaviorId, ushort targetPlayerId, byte useFlags, ResolvedEntitySynchInfo entitySynchInfo, bool useTargetAction, bool beginEndStream = true)
        {
            var writer = new LEWriter();
            if (beginEndStream)
                writer.WriteByte(0x07);

            writer.WriteByte(0x35);
            writer.WriteUInt16((ushort)monsterBehaviorId);
            writer.WriteByte(0x04);
            writer.WriteByte(useTargetAction ? (byte)0x50 : (byte)0xF0);
            writer.WriteByte(0x00);
            if (useTargetAction)
                writer.WriteByte(useFlags);
            writer.WriteUInt16(targetPlayerId);
            WriteResolvedEntitySynchInfo(writer, "MON-ATTACK", "Monster", entitySynchInfo, true);

            if (beginEndStream)
                writer.WriteByte(0x06);

            return writer.ToArray();
        }

        public static byte[] BuildPlayerStunActionPacket(ushort behaviorId, byte actionClassId, ushort headingWire, ushort strengthWire, ResolvedEntitySynchInfo entitySynchInfo)
        {
            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x35);
            writer.WriteUInt16(behaviorId);
            writer.WriteByte(0x04);
            writer.WriteByte(actionClassId);
            writer.WriteByte(actionClassId);
            writer.WriteUInt16(headingWire);
            writer.WriteUInt16(strengthWire);
            WriteResolvedEntitySynchInfo(writer, "PLAYER-STUN-ACTION", "Avatar", entitySynchInfo, true);
            writer.WriteByte(0x06);
            return writer.ToArray();
        }

        public static byte[] BuildPlayerModifierAddPacket(ushort modifiersId, string gcType, uint modifierId, byte level, uint powerLevel, uint durationTicks, byte sourceIsSelf, ResolvedEntitySynchInfo entitySynchInfo)
        {
            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x35);
            writer.WriteUInt16(modifiersId);
            writer.WriteByte(0x00);
            WriteGCType(writer, gcType, true);
            writer.WriteUInt32(modifierId);
            writer.WriteByte(level);
            writer.WriteUInt32(powerLevel);
            writer.WriteUInt32(durationTicks);
            writer.WriteByte(sourceIsSelf);
            WriteResolvedEntitySynchInfo(writer, "PLAYER-MODIFIER-ADD", "Avatar", entitySynchInfo, true);
            writer.WriteByte(0x06);
            return writer.ToArray();
        }

        public static byte[] BuildPlayerModifierRemovePacket(ushort modifiersId, uint modifierId, ResolvedEntitySynchInfo entitySynchInfo)
        {
            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x35);
            writer.WriteUInt16(modifiersId);
            writer.WriteByte(0x01);
            writer.WriteUInt32(modifierId);
            WriteResolvedEntitySynchInfo(writer, "PLAYER-MODIFIER-REMOVE", "Avatar", entitySynchInfo, true);
            writer.WriteByte(0x06);
            return writer.ToArray();
        }

        private struct ManipulatorEntry
        {
            public string GCType;
            public uint Id;
            public float Cooldown;
            public float Range;
            public ManipulatorType Type;

            public ManipulatorEntry(string gcType, uint id, float cooldown, float range, ManipulatorType type)
            {
                GCType = gcType;
                Id = id;
                Cooldown = cooldown;
                Range = range;
                Type = type;
            }
        }

        private enum ManipulatorType
        {
            MeleeWeapon,
            RangedWeapon,
            ActiveSkill
        }
    }
}
