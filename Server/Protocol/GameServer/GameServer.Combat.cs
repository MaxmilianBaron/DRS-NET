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
        private static readonly Dictionary<string, (string modGcType, float durBase, float durInc)> _buffModifierMap
            = new Dictionary<string, (string, float, float)>(StringComparer.OrdinalIgnoreCase)
        {
            { "sprint",                    ("skills.generic.Sprint.Modifier",                    30f,  10f) },
            { "manashield",                ("skills.generic.ManaShield.Modifier",                180f,  0f) },
            { "healsself",                 ("skills.generic.HealSelf.Modifier",                    0f,  0f) },
            { "manaself",                  ("skills.generic.ManaSelf.Modifier",                    0f,  0f) },
            { "blight",                    ("skills.generic.Blight.Modifier",                     30f,  1f) },
            { "charge",                    ("skills.generic.Charge.CastModifier",                150f,  0.5f) },
            { "divineresistbuff",          ("skills.generic.DivineResistBuff.Modifier",           40f,  0f) },
            { "fireresistbuff",            ("skills.generic.FireResistBuff.Modifier",             40f,  0f) },
            { "iceresistbuff",             ("skills.generic.IceResistBuff.Modifier",              40f,  0f) },
            { "poisonresistbuff",          ("skills.generic.PoisonResistBuff.Modifier",           40f,  0f) },
            { "shadowresistbuff",          ("skills.generic.ShadowResistBuff.Modifier",           40f,  0f) },
            { "divinedamagebuff",          ("skills.generic.DivineDamageBuff.Modifier",           30f,  0f) },
            { "firedamagebuff",            ("skills.generic.FireDamageBuff.Modifier",             30f,  0f) },
            { "icedamagebuff",             ("skills.generic.IceDamageBuff.Modifier",              30f,  0f) },
            { "poisondamagebuff",          ("skills.generic.PoisonDamageBuff.Modifier",           30f,  0f) },
            { "shadowdamagebuff",          ("skills.generic.ShadowDamageBuff.Modifier",           30f,  0f) },
            { "1hmeleespeedbuff",          ("skills.generic.1HMeleeSpeedBuff.Modifier",           30f,  0f) },
            { "2hmeleespeedbuff",          ("skills.generic.2HMeleeSpeedBuff.Modifier",           30f,  0f) },
            { "rangedspeedbuff",           ("skills.generic.RangedSpeedBuff.Modifier",            30f,  0f) },
            { "stunresistbuff",            ("skills.generic.StunResistBuff.Modifier",             30f,  0f) },
            { "minmovespeedbuff",          ("skills.generic.MinMoveSpeedBuff.Modifier",           15f,  0f) },
            { "aggroincreasemodbuff",      ("skills.generic.AggroIncreaseModBuff.Modifier",       25f,  5f) },
            { "meleedamagereflectionbuff", ("skills.generic.MeleeDamageReflectionBuff.Modifier",  30f,  0f) },
            { "poisonblastradius",         ("skills.generic.PoisonBlastRadius.Modifier",           4f,  0.25f) },
            { "shadowrage",                ("skills.generic.ShadowRage.CastModifier",             25f,  1f) },
            { "firecone",                  ("skills.generic.FireCone.CastModifier",                0f,  0f) },
            { "diviineintervention",       ("skills.generic.DivineIntervention.Modifier",         15f,  1f) },
            { "divineintervention",        ("skills.generic.DivineIntervention.Modifier",         15f,  1f) },
            { "strengthbuff",              ("skills.generic.StrengthBuff.Modifier",               30f, 15f) },
            { "shadowtendrils",            ("skills.generic.ShadowTendrils.Modifier",             30f,  0f) },
            { "firetrail",                 ("skills.generic.FireTrail.Modifier",                  25f,  0f) },
            { "poisontrail",               ("skills.generic.PoisonTrail.Modifier",                60f,  0f) },
        };
        private static readonly Dictionary<string, (string modGcType, float durationSec)> _creatureDebuffMap
            = new Dictionary<string, (string, float)>(StringComparer.OrdinalIgnoreCase)
        {
            { "basicslow",                    ("skills.creature.BasicSlow.Modifier",                    15f) },
            { "basicstun",                    ("skills.creature.BasicStun.Modifier",                     3f) },
            { "creaturerend",                 ("skills.creature.CreatureRend.Modifier",                   5f) },
            { "creaturehamstring",            ("skills.creature.CreatureHamstring.Modifier",              5f) },
            { "creatureenfeeble",             ("skills.creature.CreatureEnfeeble.Modifier",              60f) },
            { "creaturegoldstun",             ("skills.creature.CreatureGoldStun.Modifier",              15f) },
            { "creaturedebuffdivine",         ("skills.creature.CreatureDebuffDivine.Modifier",          15f) },
            { "creaturedebufffire",           ("skills.creature.CreatureDebuffFire.Modifier",            15f) },
            { "creaturedebuffice",            ("skills.creature.CreatureDebuffIce.Modifier",             15f) },
            { "creaturedebuffpoison",         ("skills.creature.CreatureDebuffPoison.Modifier",          15f) },
            { "creaturedebuffshadow",         ("skills.creature.CreatureDebuffShadow.Modifier",          15f) },
            { "widowerweb",                   ("skills.creature.WidowerWeb.Modifier",                    10f) },
            { "widowerblackcloud",            ("skills.creature.WidowerBlackCloud.Modifier",             10f) },
            { "agrockintimidate",             ("skills.creature.AgrockIntimidate.Modifier",              15f) },
            { "abaddonflameprison",           ("skills.creature.AbaddonFlamePrison.Modifier",            10f) },
            { "orokruntshotpoison",           ("skills.creature.OrokRuntShotPoison.Modifier",             3f) },
            { "shadowqueenmortalstrike_fear", ("skills.creature.ShadowQueenMortalStrike_Fear.Modifier",  10f) },
            { "bossmortalstrike_fear",        ("skills.creature.BossMortalStrike_Fear.Modifier",          6f) },
            { "heckledebuff",                 ("skills.creature.HeckleDebuff.Modifier",                   5f) },
            { "griefermultiboltsilence",      ("skills.creature.GrieferMultiBoltSilence.Modifier",        5f) },
            { "relicstun",                    ("skills.creature.RelicStun.Modifier",                      1f) },
            { "combatfearself",               ("skills.creature.CombatFearSelf.Modifier",                10f) },
            { "combatfearfriendsaoe",         ("skills.creature.CombatFearFriendsAoE.Modifier",           8f) },
        };

        private static readonly Dictionary<string, string> _weaponDebuffMap
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "basic",                          "creaturerend" },
            { "wargbase_grunt",                 "creaturerend" },
            { "wargbase_hero",                  "creaturerend" },
            { "wargbase_liger",                 "creaturerend" },
            { "wargbase_pinata_hero",           "creaturerend" },
            { "abba_labba_caster_grunt_base",   "creaturedebufffire" },
            { "abba_labba_caster_hero_base",    "creaturedebufffire" },
            { "boss_caster",                    "creaturedebufffire" },
            { "basicslow",                      "basicslow" },
            { "basicstun",                      "basicstun" },
            { "creaturerend",                   "creaturerend" },
            { "creaturehamstring",              "creaturehamstring" },
            { "creatureenfeeble",               "creatureenfeeble" },
            { "creaturegoldstun",               "creaturegoldstun" },
            { "creaturedebuffdivine",           "creaturedebuffdivine" },
            { "creaturedebufffire",             "creaturedebufffire" },
            { "creaturedebuffice",              "creaturedebuffice" },
            { "creaturedebuffpoison",           "creaturedebuffpoison" },
            { "creaturedebuffshadow",           "creaturedebuffshadow" },
            { "widowerweb",                     "widowerweb" },
            { "widowerblackcloud",              "widowerblackcloud" },
            { "agrockintimidate",               "agrockintimidate" },
            { "abaddonflameprison",             "abaddonflameprison" },
            { "orokruntshotpoison",             "orokruntshotpoison" },
            { "shadowqueenmortalstrike_fear",   "shadowqueenmortalstrike_fear" },
            { "bossmortalstrike_fear",          "bossmortalstrike_fear" },
            { "heckledebuff",                   "heckledebuff" },
            { "griefermultiboltsilence",        "griefermultiboltsilence" },
            { "relicstun",                      "relicstun" },
            { "combatfearself",                 "combatfearself" },
            { "combatfearfriendsaoe",           "combatfearfriendsaoe" },
        };

        private float _combatTimer = 0f;
        private uint _combatTick = 0;
        private float _combatTime = -1f;
        private const float COMBAT_TICK = 1f / 30f;
        private const int CLIENT_UPDATE_PHASE_TARGET = 3;
        private int _clientUpdatePhase = 0;
        private Dictionary<string, HashSet<byte>> _playerSpellSlots = new Dictionary<string, HashSet<byte>>();
        private Dictionary<string, float> _activeSkillBusyUntil = new Dictionary<string, float>();
        private Dictionary<string, Dictionary<uint, string>> _playerManipMap = new Dictionary<string, Dictionary<uint, string>>();
        private Dictionary<string, Dictionary<string, int>> _playerSkillLevels = new Dictionary<string, Dictionary<string, int>>();
        private Dictionary<string, Dictionary<string, uint>> _playerSkillSlots = new Dictionary<string, Dictionary<string, uint>>();

        private float SKILL_VALUE_PER_LEVEL => GCDatabase.Instance.GetKnob("SkillValuePerLevel", 1113.621f);
        private const int MAX_COMBAT_CATCH_UP_TICKS = 8;
        private const int AvatarAggroSampleCatchUp = 6;

        private float GetCombatNow()
        {
            return _combatTime >= 0f ? _combatTime : Time.time;
        }

        private void GetValidationCutoff(out uint cutoffTick, out float cutoffTime)
        {
            CombatRuntime.Instance.GetValidationCutoff(out cutoffTick, out cutoffTime);
        }

        private bool AdvanceCombatClock(out float tickNow, out uint tickIndex)
        {
            if (_combatTime < 0f)
                _combatTime = Time.time;

            tickNow = _combatTime;
            tickIndex = _combatTick;
            if (_combatTimer + 0.0001f < COMBAT_TICK)
                return false;

            _combatTimer -= COMBAT_TICK;
            _combatTick++;
            _combatTime += COMBAT_TICK;
            tickNow = _combatTime;
            tickIndex = _combatTick;
            CombatRuntime.Instance.SetCombatClock(tickIndex, tickNow, "GameServer.Update");
            if (VerboseCombatClock) Debug.LogError($"[CLIENT-COMBAT-CLOCK] tick={tickIndex} time={tickNow:F3} delta={COMBAT_TICK:F3} source=Update");
            return true;
        }

        private bool AdvanceClientUpdatePhase()
        {
            bool updateClients = _clientUpdatePhase == CLIENT_UPDATE_PHASE_TARGET;
            _clientUpdatePhase = updateClients ? 0 : _clientUpdatePhase + 1;
            return updateClients;
        }

        private float _autoSaveTimer = 0f;
        private static readonly bool VerboseCombatClock = System.Environment.GetEnvironmentVariable("DR_SERVER_VERBOSE_COMBAT_CLOCK") == "1";
        private static readonly bool VerboseMonsterDiag = System.Environment.GetEnvironmentVariable("DR_SERVER_VERBOSE_MONSTER_DIAG") == "1";
        private const double EntityUpdateMaxFrameSeconds = 0.333;
        private const double EntityUpdateTimestepMs = 1000.0 / 30.0;
        private double _entityUpdateAccumulatorMs = 0.0;
        private int _entityUpdateBudgetThisFrame = 0;
        private int _entityCatchUpTicksThisFrame = 0;


        void Update()
        {
            _tickTimer += Time.deltaTime;
            _combatTimer += Time.deltaTime;
            _entityUpdateAccumulatorMs += Math.Min(Time.deltaTime, EntityUpdateMaxFrameSeconds) * 1000.0;

            if (_tickTimer >= TICK_INTERVAL)
            {
                _tickTimer -= TICK_INTERVAL;
                AdvanceAllAvatarHP();
                int combatTicks = 0;
                _entityCatchUpTicksThisFrame = 0;
                _entityUpdateBudgetThisFrame = (int)(_entityUpdateAccumulatorMs / EntityUpdateTimestepMs);
                _entityUpdateAccumulatorMs -= _entityUpdateBudgetThisFrame * EntityUpdateTimestepMs;
                uint lastTickIndex = 0;
                float lastTickNow = 0f;
                while (combatTicks < MAX_COMBAT_CATCH_UP_TICKS && AdvanceCombatClock(out float tickNow, out uint tickIndex))
                {
                    bool updateClients = AdvanceClientUpdatePhase();
                    bool allowNewMonsterAttacks = true;
                    DrainAvatarAggroSamples();
                    TickUseTargetMoving();
                    TickCombatDeterministicSystems(tickNow, allowNewMonsterAttacks);
                    CombatRuntime.Instance.MarkEntityUpdateCompleted(tickIndex, tickNow, "GameServer.Update");
                    FlushPendingMonsterBehaviorUpdates(tickNow, tickIndex);
                    FlushAllQueues(updateClients);
                    lastTickIndex = tickIndex;
                    lastTickNow = tickNow;
                    combatTicks++;
                }
                if (combatTicks > 0)
                {
                    WirePacketTally.Report();
                    if (VerboseCombatClock) Debug.LogError($"[CLIENT-COMBAT-CLOCK] completed ticks={combatTicks} lastTick={lastTickIndex} lastTime={lastTickNow:F3} remaining={_combatTimer:F4}");
                }
                if (VerboseCombatClock && _combatTimer >= COMBAT_TICK)
                    Debug.LogError($"[CLIENT-COMBAT-CLOCK] catch-up pending remaining={_combatTimer:F4} maxTicks={MAX_COMBAT_CATCH_UP_TICKS}");
                FlushPendingKills();
                ReleaseCompletedUseTargets();
                FlushPendingClientControlResets();

                try { ProcessMatchmakingTick(); }
                catch (Exception ex) { Debug.LogError($"[PVP-TICK] {ex.Message}"); }
            }

            _merchantRefreshTimer += Time.deltaTime;
            if (_merchantRefreshTimer >= MERCHANT_REFRESH_INTERVAL)
            {
                _merchantRefreshTimer = 0f;
                MerchantRuntime.ProcessRefreshes(_zoneNPCs, _connections, SendCompressedA, ref _nextEntityId);
            }

            _droppedItemCleanupTimer += Time.deltaTime;
            if (_droppedItemCleanupTimer >= DROPPED_ITEM_CLEANUP_INTERVAL)
            {
                _droppedItemCleanupTimer = 0f;
                CleanupExpiredDroppedItems();
            }

            _groupHealthBroadcastTimer += Time.deltaTime;
            if (_groupHealthBroadcastTimer >= GROUP_HEALTH_BROADCAST_INTERVAL)
            {
                _groupHealthBroadcastTimer = 0f;
                foreach (var broadcastGroup in GroupDirectory.Instance.AllGroups())
                {
                    if (broadcastGroup.Members.Count > 1)
                        SendGroupHealthToAll(broadcastGroup);
                }
            }

            _autoSaveTimer += Time.deltaTime;
            if (_autoSaveTimer >= AUTO_SAVE_INTERVAL)
            {
                _autoSaveTimer = 0f;
                foreach (var conn in _connections.Values)
                {
                    if (conn != null && conn.LoginName != null)
                        SavePlayerLevel(conn);
                }
            }
        }






        private void AdvanceAllAvatarHP()
        {
        }

        private static void ResolveMonsterClientVisiblePosition(Combat.Monster monster, out float posX, out float posY)
        {
            posX = monster != null ? monster.PosX : 0f;
            posY = monster != null ? monster.PosY : 0f;
            if (monster != null)
                CombatRuntime.Instance.TryGetMonsterClientVisiblePosition(monster, CombatRuntime.Instance.GetCombatTime(), out posX, out posY);
        }

        private static string ResolveConnectionInstanceKey(RRConnection conn)
        {
            if (conn == null)
                return null;
            if (!string.IsNullOrWhiteSpace(conn.RuntimeInstanceKey))
                return conn.RuntimeInstanceKey;
            if (IsPublicZone(conn.CurrentZoneName))
                return conn.CurrentZoneName;
            return $"{conn.CurrentZoneName}_inst{conn.InstanceId}";
        }

        private struct ItemDropPlacement
        {
            public float X;
            public float Y;
            public float Z;
            public int Draws;
            public string Result;
        }

        private static uint ConsumeItemAddToWorldHeading(string owner)
        {
            uint headingRaw = RandomStreams.GenerateGlobalStatic(
                "ItemObject.addToWorld.heading",
                owner ?? "ItemObject::addToWorld");
            uint headingFixed8 = (headingRaw % 360u) << 8;
            Debug.LogError($"[ITEM-ADDWORLD-CLIENT] source={owner ?? "unknown"} headingRaw=0x{headingRaw:X8} headingFixed8=0x{headingFixed8:X8} sourceFunction=ItemObject::addToWorld@0x0058A0A0 RandomStreams.GenerateGlobalStatic");
            return headingRaw;
        }

        private static ItemDropPlacement ResolveItemDropPlacement(
            RRConnection conn,
            string zoneName,
            uint instanceId,
            float sourceX,
            float sourceY,
            float sourceZ,
            float sourceHeading,
            string owner)
        {
            string pathMapKey = conn != null ? ResolveConnectionInstanceKey(conn) : zoneName;
            PathMap pathMap = !string.IsNullOrWhiteSpace(pathMapKey) ? PathMapCatalog.Instance.GetPathMap(pathMapKey) : null;
            if (pathMap == null)
            {
                Debug.LogError($"[ITEM-PLACEMENT] source={owner ?? "unknown"} zone='{zoneName ?? ""}' instance={instanceId:X8} pathMap=missing result=source-fallback draws=0 pos=({sourceX:F1},{sourceY:F1},{sourceZ:F1}) sourceFunction=ItemObject::SetPositionRandomly@0x0058B400 no-PathMap-no-RNG");
                return new ItemDropPlacement { X = sourceX, Y = sourceY, Z = sourceZ, Draws = 0, Result = "source-fallback" };
            }

            uint headingRaw = RandomStreams.GenerateGlobalStatic(
                "ItemObject::SetPositionRandomly.heading",
                owner ?? "ItemObject::SetPositionRandomly");
            uint randomHeading = (headingRaw % 360u) << 8;
            uint sourceHeadingFixed8 = ToHeadingFixed8(sourceHeading);
            uint reverseHeadingFixed8 = sourceHeadingFixed8 - 0xB400u;
            uint[] headings = { randomHeading, sourceHeadingFixed8, reverseHeadingFixed8 };
            string[] labels = { "random", "source", "reverse" };

            for (int headingIndex = 0; headingIndex < headings.Length; headingIndex++)
            {
                if (!TryItemDropPlacement(pathMap, sourceX, sourceY, headings[headingIndex], out float placementX, out float placementY, out uint minRadius))
                    continue;

                uint radius = RandomStreams.GenerateGlobalStaticRangeInclusive(
                    minRadius,
                    25u,
                    "ItemObject::SetPositionRandomly.radius",
                    owner ?? "ItemObject::SetPositionRandomly");
                HeadingVector(headings[headingIndex], out float dirX, out float dirY);
                float x = sourceX + dirX * radius;
                float y = sourceY + dirY * radius;
                float z = pathMap.GetHeightAt(x, y, sourceZ);
                Debug.LogError($"[ITEM-PLACEMENT-CLIENT] source={owner ?? "unknown"} zone='{zoneName ?? ""}' instance={instanceId:X8} pathMap='{pathMapKey}' result=random branch={labels[headingIndex]} draws=2 headingRaw=0x{headingRaw:X8} headingFixed8=0x{headings[headingIndex]:X8} placement=({placementX:F1},{placementY:F1}) radius={radius} pos=({x:F1},{y:F1},{z:F1}) sourceFunction=ItemObject::SetPositionRandomly@0x0058B400 PathManager::FindFirstValidPointInDir@0x00589880 ItemObject::SetPositionRandomly.spinGroundRay");
                return new ItemDropPlacement { X = x, Y = y, Z = z, Draws = 2, Result = labels[headingIndex] };
            }

            Debug.LogError($"[ITEM-PLACEMENT-CLIENT] source={owner ?? "unknown"} zone='{zoneName ?? ""}' instance={instanceId:X8} pathMap='{pathMapKey}' result=source-fallback draws=1 headingRaw=0x{headingRaw:X8} pos=({sourceX:F1},{sourceY:F1},{sourceZ:F1}) sourceFunction=ItemObject::SetPositionRandomly@0x0058B400 PathManager::FindFirstValidPointInDir@0x00589880 no-radius-draw");
            return new ItemDropPlacement { X = sourceX, Y = sourceY, Z = sourceZ, Draws = 1, Result = "source-fallback" };
        }

        private static bool TryItemDropPlacement(PathMap pathMap, float sourceX, float sourceY, uint headingFixed8, out float placementX, out float placementY, out uint minRadius)
        {
            HeadingVector(headingFixed8, out float dx, out float dy);
            const int startRadius = 5;
            const int maxPlacementRadius = 20;
            placementX = sourceX + dx * startRadius;
            placementY = sourceY + dy * startRadius;
            minRadius = 0;
            if (pathMap == null)
                return false;
            for (int radius = startRadius; radius <= maxPlacementRadius; radius++)
            {
                placementX = sourceX + dx * radius;
                placementY = sourceY + dy * radius;
                if (!pathMap.IsWalkable(placementX, placementY))
                    continue;
                minRadius = (uint)Mathf.Clamp(radius, 0, 25);
                return true;
            }
            return false;
        }

        private static uint ToHeadingFixed8(float headingDegrees)
        {
            int rounded = Mathf.RoundToInt(headingDegrees * 256f);
            return unchecked((uint)rounded);
        }

        private static void HeadingVector(uint headingFixed8, out float x, out float y)
        {
            int degrees = (int)((headingFixed8 >> 8) % 360u);
            int tableIndex = (360 - degrees) % 360;
            float radians = tableIndex * Mathf.Deg2Rad;
            x = Mathf.Sin(radians);
            y = Mathf.Cos(radians);
        }

        private static bool IsBasicMeleeUseTargetFlag(byte useFlags)
        {
            return useFlags == 0x0A || useFlags == 0x0B;
        }

        private const int AvatarSpeedModPercent = 100;
        private const float USE_TARGET_UPDATE_MOVING_STOP_DISTANCE = 33f;
        private const float USE_TARGET_UPDATE_MOVING_STOP_DISTANCE_SQ = USE_TARGET_UPDATE_MOVING_STOP_DISTANCE * USE_TARGET_UPDATE_MOVING_STOP_DISTANCE;

        private void StartUseTargetMoving(RRConnection conn, float targetX, float targetY, int followConnId = 0, ushort entityId = 0)
        {
            if (conn == null || !conn.IsSpawned) return;
            if (followConnId == 0)
            {
                float dx = targetX - conn.PlayerPosX;
                float dy = targetY - conn.PlayerPosY;
                if (dx * dx + dy * dy <= USE_TARGET_UPDATE_MOVING_STOP_DISTANCE_SQ) return;
            }
            conn.UseTargetMovingX = targetX;
            conn.UseTargetMovingY = targetY;
            conn.UseTargetMovingFollowConnId = followConnId;
            conn.UseTargetMovingEntityId = entityId;
            conn.UseTargetMovingInstanceKey = conn.RuntimeInstanceKey ?? "";
            conn.UseTargetMovingSampleCount = 0;
            conn.UseTargetMovingDelayTicks = 1;
            conn.UseTargetMovingStartTime = Time.time;
            conn.UseTargetMovingActive = true;
            Debug.LogError($"[USE-TARGET-MOVING] state=start conn={conn.ConnId} from=({conn.PlayerPosX:F1},{conn.PlayerPosY:F1}) target=({targetX:F1},{targetY:F1}) follow={followConnId} entity={entityId}");
        }

        private void CancelUseTargetMoving(RRConnection conn, string reason)
        {
            if (conn == null || !conn.UseTargetMovingActive) return;
            FlushUseTargetMovingSamples(conn);
            conn.UseTargetMovingActive = false;
            conn.UseTargetMovingFollowConnId = 0;
            conn.UseTargetMovingEntityId = 0;
            if (reason != "client-move")
            {
                var stopRecord = new byte[UnitMoverUpdateRecordSize];
                stopRecord[0] = 0x01;
                BitConverter.GetBytes((int)(conn.PlayerHeading * 256f)).CopyTo(stopRecord, 1);
                BitConverter.GetBytes((int)(conn.PlayerPosX * 256f)).CopyTo(stopRecord, 5);
                BitConverter.GetBytes((int)(conn.PlayerPosY * 256f)).CopyTo(stopRecord, 9);
                BroadcastPlayerMovement(conn, 0xFF, 1, stopRecord);
            }
            Debug.LogError($"[USE-TARGET-MOVING] state=end conn={conn.ConnId} reason={reason} pos=({conn.PlayerPosX:F1},{conn.PlayerPosY:F1})");
        }

        private void FlushUseTargetMovingSamples(RRConnection conn)
        {
            byte sampleCount = conn.UseTargetMovingSampleCount;
            if (sampleCount == 0) return;
            conn.UseTargetMovingSampleCount = 0;
            var batch = new byte[sampleCount * UnitMoverUpdateRecordSize];
            Buffer.BlockCopy(conn.UseTargetMovingSampleData, 0, batch, 0, batch.Length);
            BroadcastPlayerMovement(conn, 0xFF, sampleCount, batch);
        }

        private bool TryResolveUseTargetMovingTarget(RRConnection conn, out float targetX, out float targetY, out string lostReason)
        {
            targetX = conn.UseTargetMovingX;
            targetY = conn.UseTargetMovingY;
            lostReason = null;
            if (conn.UseTargetMovingFollowConnId != 0)
            {
                if (!_connections.TryGetValue(conn.UseTargetMovingFollowConnId, out var followConn) || followConn == null || !followConn.IsSpawned
                    || !string.Equals(followConn.RuntimeInstanceKey ?? "", conn.RuntimeInstanceKey ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    lostReason = "follow-target-lost";
                    return false;
                }
                targetX = followConn.PlayerPosX;
                targetY = followConn.PlayerPosY;
                return true;
            }
            if (conn.UseTargetMovingEntityId != 0)
            {
                var monster = CombatRuntime.Instance.GetMonster(conn.UseTargetMovingEntityId)
                           ?? CombatRuntime.Instance.GetMonsterByComponent(conn.UseTargetMovingEntityId);
                if (monster != null)
                {
                    if (!monster.IsAlive)
                    {
                        lostReason = "entity-dead";
                        return false;
                    }
                    ResolveMonsterClientVisiblePosition(monster, out targetX, out targetY);
                }
            }
            return true;
        }

        private void TickUseTargetMoving()
        {
            foreach (var conn in _connections.Values)
            {
                if (conn == null || !conn.IsSpawned || !conn.UseTargetMovingActive) continue;
                if (!string.Equals(conn.UseTargetMovingInstanceKey ?? "", conn.RuntimeInstanceKey ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    CancelUseTargetMoving(conn, "instance-change");
                    continue;
                }
                var state = GetPlayerState(conn.ConnId.ToString());
                if (state == null || state.CurrentHPWire == 0)
                {
                    CancelUseTargetMoving(conn, "dead");
                    continue;
                }
                if (conn.UseTargetMovingDelayTicks > 0)
                {
                    conn.UseTargetMovingDelayTicks--;
                    continue;
                }
                if (!TryResolveUseTargetMovingTarget(conn, out float targetX, out float targetY, out string lostReason))
                {
                    CancelUseTargetMoving(conn, lostReason ?? "target-lost");
                    continue;
                }
                float deltaX = targetX - conn.PlayerPosX;
                float deltaY = targetY - conn.PlayerPosY;
                float distSq = deltaX * deltaX + deltaY * deltaY;
                if (distSq <= USE_TARGET_UPDATE_MOVING_STOP_DISTANCE_SQ)
                {
                    if (conn.UseTargetMovingFollowConnId == 0)
                        CancelUseTargetMoving(conn, "arrived");
                    continue;
                }
                float dist = (float)Math.Sqrt(distSq);
                float step = ResolveAvatarMoveSpeed(conn, state) * COMBAT_TICK;
                float maxStep = dist - USE_TARGET_UPDATE_MOVING_STOP_DISTANCE;
                bool finalStep = step >= maxStep;
                if (finalStep) step = maxStep;
                float nextX = conn.PlayerPosX + deltaX / dist * step;
                float nextY = conn.PlayerPosY + deltaY / dist * step;
                float headingDeg = Mathf.Atan2(-deltaX, deltaY) * Mathf.Rad2Deg;
                while (headingDeg < 0f) headingDeg += 360f;
                int headingWire = (int)(headingDeg * 256f);
                conn.PlayerPosX = nextX;
                conn.PlayerPosY = nextY;
                conn.PlayerHeading = headingWire / 256f;
                conn.HasLivePlayerPosition = true;
                conn.LivePlayerPosX = nextX;
                conn.LivePlayerPosY = nextY;
                conn.LivePlayerPosZ = conn.PlayerPosZ;
                conn.LivePlayerHeading = conn.PlayerHeading;
                conn.LivePlayerPositionTime = Time.time;
                conn.AggroSamplePosX = nextX;
                conn.AggroSamplePosY = nextY;
                if (conn.Avatar != null)
                    CombatRuntime.Instance.UpdatePlayerPosition((uint)conn.Avatar.Id, nextX, nextY);
                CheckGotoProximity(conn);
                CheckPendingMerchantActivation(conn);
                int sampleOffset = conn.UseTargetMovingSampleCount * UnitMoverUpdateRecordSize;
                var sampleData = conn.UseTargetMovingSampleData;
                sampleData[sampleOffset] = 0x00;
                BitConverter.GetBytes(headingWire).CopyTo(sampleData, sampleOffset + 1);
                BitConverter.GetBytes((int)(nextX * 256f)).CopyTo(sampleData, sampleOffset + 5);
                BitConverter.GetBytes((int)(nextY * 256f)).CopyTo(sampleData, sampleOffset + 9);
                conn.UseTargetMovingSampleCount++;
                FlushUseTargetMovingSamples(conn);
                if (finalStep && conn.UseTargetMovingFollowConnId == 0)
                    CancelUseTargetMoving(conn, "arrived");
            }
        }

        private float ResolveAvatarMoveSpeed(RRConnection conn, PlayerState state)
        {
            string avatarGcType = conn?.AvatarGcType;
            if (string.IsNullOrWhiteSpace(avatarGcType))
                avatarGcType = conn?.Avatar?.GCClass;
            if (string.IsNullOrWhiteSpace(avatarGcType))
                avatarGcType = "avatar.base.avatar";

            var avatar = GCDatabase.Instance?.ResolveWithInheritance(avatarGcType);
            var desc = avatar?.GetChild("Description") ?? avatar;
            float baseSpeed = desc != null ? desc.GetFloat("Speed", 30f) : 30f;
            if (baseSpeed <= 0f) baseSpeed = 30f;
            RefreshMovementSpeedModifiers(conn, state);
            int speedModPercent = AvatarSpeedModPercent + (state != null ? state.MovementSpeedModPercent : 0);
            float runSpeed = baseSpeed * speedModPercent / 100f;
            return runSpeed > 0f ? runSpeed : 57f;
        }

        private void RefreshMovementSpeedModifiers(RRConnection conn, PlayerState state)
        {
            if (state == null) return;
            int speedModPercent = 0;
            int minSpeedModValue = 0;
            if (conn != null && !string.IsNullOrWhiteSpace(conn.LoginName))
            {
                foreach (var mod in _activeModifiers.ListFor(conn.LoginName))
                {
                    speedModPercent += ResolveMovementSpeedModPercent(mod);
                    minSpeedModValue = Math.Max(minSpeedModValue, ResolveMinMovementSpeedModValue(mod));
                }
            }
            state.SetMovementSpeedModifiers(speedModPercent, minSpeedModValue);
        }

        private static int ResolveMovementSpeedModPercent(ActiveModifier mod)
        {
            if (mod == null || string.IsNullOrWhiteSpace(mod.GCType)) return 0;
            int levelIndex = Math.Max(0, mod.Level - 1);
            if (string.Equals(mod.GCType, "skills.generic.Sprint.Modifier", StringComparison.OrdinalIgnoreCase))
                return 30 + levelIndex * 10;
            if (string.Equals(mod.GCType, "skills.generic.SlowDebuff.Modifier", StringComparison.OrdinalIgnoreCase))
                return -15 + levelIndex * -15;
            return 0;
        }

        private static int ResolveMinMovementSpeedModValue(ActiveModifier mod)
        {
            if (mod == null || string.IsNullOrWhiteSpace(mod.GCType)) return 0;
            if (string.Equals(mod.GCType, "skills.generic.MinMoveSpeedBuff.Modifier", StringComparison.OrdinalIgnoreCase))
                return 125;
            return 0;
        }

        private void FlushAllQueues(bool heartbeatIfEmpty = false)
        {
            foreach (var conn in _connections.Values)
                FlushConnQueue(conn, heartbeatIfEmpty);
        }

        private void FlushConnQueue(RRConnection conn, bool heartbeatIfEmpty = false)
        {
            if (conn == null || !conn.IsConnected || !conn.AllowFlush)
                return;
            if (conn.MessageQueue.Count == 0 && !(heartbeatIfEmpty && conn.IsSpawned))
                return;

            var writer = new LEWriter();
            writer.WriteByte(0x07);

            var messages = conn.MessageQueue.DequeueAll();
            foreach (var queuedMessage in messages)
            {
                writer.WriteBytes(queuedMessage);
            }

            writer.WriteByte(0x06);

            SendCompressedA(conn, 0x01, 0x0F, writer.ToArray());
        }

        private static bool QueueClientEntityStream(RRConnection conn, byte[] data)
        {
            if (conn == null || data == null || data.Length == 0)
                return false;
            if (data.Length >= 2 && data[0] == 0x07 && data[data.Length - 1] == 0x06)
            {
                var inner = new byte[data.Length - 2];
                Array.Copy(data, 1, inner, 0, inner.Length);
                conn.MessageQueue.Enqueue(inner);
                return true;
            }
            conn.MessageQueue.Enqueue(data);
            return true;
        }




        private void SendRandomSeed(RRConnection conn, uint seed, bool initializeRng = false)
        {
            if (conn == null)
            {
                Debug.LogError("[RNG-SEED] Cannot send seed - no connection!");
                return;
            }

            _entityUpdateAccumulatorMs = 0.0;

            string instanceKey = GetInstanceZoneKey(conn);
            if (initializeRng)
                CombatRuntime.Instance.InitializeRoomRng(instanceKey, seed, "SendRandomSeed-initialize");
            else
            {
                CombatRuntime.Instance.EnsureRoomRng(instanceKey, seed, "SendRandomSeed-preserve-existing");
                uint effectiveSeed = CombatRuntime.Instance.GetRoomSeedForInstance(instanceKey);
                if (effectiveSeed != 0 && effectiveSeed != seed)
                {
                    Debug.LogError($"[RNG-SEED] Using preserved room seed instance='{instanceKey}' requested=0x{seed:X8} effective=0x{effectiveSeed:X8}");
                    seed = effectiveSeed;
                }
            }
            conn.EntitySchedulerMirror.Reset(instanceKey, "SendRandomSeed");

            var seedMessage = new LEWriter();
            seedMessage.WriteByte(0x07);
            seedMessage.WriteByte(0x0C);
            seedMessage.WriteUInt32(seed);
            seedMessage.WriteByte(0x06);

            SendCompressedA(conn, 0x01, 0x0F, seedMessage.ToArray());
            Debug.LogError($"[RNG-SEED] Sent opcode 0x0C seed: 0x{seed:X8} instance='{instanceKey}' initialize={initializeRng}");
        }

        private bool TryPrepareZoneJoinRoomRuntime(
            RRConnection conn,
            string source,
            out Zone spawnZone,
            out string zoneName,
            out string instanceKey,
            out uint roomSeed,
            out uint layoutSeed)
        {
            spawnZone = null;
            zoneName = null;
            instanceKey = null;
            roomSeed = 0;
            layoutSeed = 0;

            if (conn == null || !_zones.TryGetValue(conn.CurrentZoneId, out spawnZone))
                return false;

            zoneName = spawnZone.name;
            instanceKey = GetInstanceZoneKey(conn);
            roomSeed = ResolveRuntimeZoneSeed(conn, zoneName);
            layoutSeed = ResolveZoneLayoutSeed(conn, zoneName);

            SendRandomSeed(conn, roomSeed, false);
            uint effectiveRoomSeed = CombatRuntime.Instance.GetRoomSeedForInstance(instanceKey);
            if (effectiveRoomSeed != 0)
                roomSeed = effectiveRoomSeed;

            Debug.LogError($"[ZONE-JOIN] Prepared room runtime before player spawn zone='{zoneName}' instance='{instanceKey}' entityManagerOpcode0CSeed=0x{roomSeed:X8} {FormatDungeonLayoutSeedForLog(zoneName, layoutSeed)} source={source ?? "unknown"}");
            return true;
        }

        private static string FormatDungeonLayoutSeedForLog(string zoneName, uint layoutSeed)
        {
            return !string.IsNullOrEmpty(zoneName) && DungeonMazeSpawner.IsProceduralZone(zoneName)
                ? $"dungeonLayoutSeed=0x{layoutSeed:X8}"
                : $"dungeonLayoutSeed=n/a(seedSlot=0x{layoutSeed:X8})";
        }

        private uint ResolveZoneConnectSeed(RRConnection conn, string zoneName)
        {
            uint seed = ResolveDungeonLayoutSeed(conn, zoneName);
            if (conn != null)
                _pendingZoneConnectSeeds[conn.ConnId] = seed;
            return seed;
        }

        private uint ResolveZoneLayoutSeed(RRConnection conn, string zoneName)
        {
            if (!string.IsNullOrEmpty(zoneName)
                && DungeonMazeSpawner.IsProceduralZone(zoneName)
                && conn != null
                && _pendingZoneConnectSeeds.TryGetValue(conn.ConnId, out uint seed))
                return seed;

            return ResolveDungeonLayoutSeed(conn, zoneName);
        }

        private uint ResolveDungeonLayoutSeed(RRConnection conn, string zoneName)
        {
            if (conn == null || string.IsNullOrEmpty(zoneName) || !DungeonMazeSpawner.IsProceduralZone(zoneName))
                return 0xBEEFBEEF;

            string key = GetDungeonLayoutSeedKey(conn, zoneName);
            var group = GroupDirectory.Instance.GetGroupForConn(conn.ConnId);
            if (group != null && conn.InstanceId == group.GroupId)
            {
                _zoneInstanceLayoutSeeds[key] = group.InstanceSeed;
                return group.InstanceSeed;
            }

            if (_zoneInstanceLayoutSeeds.TryGetValue(key, out uint seed))
                return seed;

            seed = GenerateDungeonLayoutSeed();
            _zoneInstanceLayoutSeeds[key] = seed;
            Debug.LogError($"[LAYOUT-SEED] zone={zoneName} instance={conn.InstanceId:X8} seed=0x{seed:X8} owner={GetSoloDungeonInstanceOwnerKey(conn, zoneName)}");
            return seed;
        }

        private string GetDungeonLayoutSeedKey(RRConnection conn, string zoneName)
        {
            return $"{zoneName ?? string.Empty}_inst{conn?.InstanceId ?? 0}";
        }

        private string GetSoloDungeonInstanceOwnerKey(uint charId, string zoneName)
        {
            string owner = charId != 0 ? $"char{charId}" : "char0";
            return $"{owner}:{zoneName ?? string.Empty}";
        }

        private string GetSoloDungeonInstanceOwnerKey(RRConnection conn, string zoneName)
        {
            uint charId = conn != null ? GetCharSqlId(conn) : 0;
            if (charId != 0)
                return GetSoloDungeonInstanceOwnerKey(charId, zoneName);

            string owner = $"conn{(conn?.ConnId ?? 0)}";
            return $"{owner}:{zoneName ?? string.Empty}";
        }

        private bool IsSoloDungeonMemoryExpired(string key)
        {
            if (string.IsNullOrEmpty(key))
                return true;
            if (!_soloDungeonLastActiveUtc.TryGetValue(key, out DateTime lastActiveUtc))
                return false;
            return (DateTime.UtcNow - lastActiveUtc).TotalSeconds > SoloDungeonMemorySeconds;
        }

        private void ForgetSoloDungeonInstance(string key, string zoneName, uint instanceId, string reason)
        {
            if (string.IsNullOrEmpty(key))
                return;

            _soloDungeonInstanceIds.Remove(key);
            _soloDungeonLastActiveUtc.Remove(key);
            string instanceKey = $"{zoneName ?? string.Empty}_inst{instanceId}";
            string seedKey = $"{zoneName ?? string.Empty}_inst{instanceId}";
            _zoneInstanceLayoutSeeds.Remove(seedKey);
            _zoneInstanceRoomSeeds.Remove(seedKey);
            ZoneSpawner.Instance.ResetZone(instanceKey);
            CombatRuntime.Instance.ClearInstanceMobs(instanceKey);
            Debug.LogError($"[INSTANCE-STATE] zone={zoneName ?? ""} owner={key} instance={instanceId:X8} state=forgot reason={reason ?? "unknown"}");
        }

        private bool HasRememberedSoloDungeonInstance(uint charId, string zoneName, out uint instanceId, out bool expired)
        {
            instanceId = 0;
            expired = false;
            if (charId == 0 || string.IsNullOrEmpty(zoneName))
                return false;

            string key = GetSoloDungeonInstanceOwnerKey(charId, zoneName);
            if (!_soloDungeonInstanceIds.TryGetValue(key, out instanceId))
                return false;

            if (IsSoloDungeonMemoryExpired(key))
            {
                expired = true;
                ForgetSoloDungeonInstance(key, zoneName, instanceId, "memory-expired");
                instanceId = 0;
                return false;
            }

            _soloDungeonLastActiveUtc[key] = DateTime.UtcNow;
            return true;
        }

        private void TouchSoloDungeonInstance(RRConnection conn, string reason)
        {
            if (conn == null || string.IsNullOrEmpty(conn.CurrentZoneName) || IsPublicZone(conn.CurrentZoneName))
                return;
            if (GroupDirectory.Instance.GetGroupForConn(conn.ConnId) != null)
                return;

            string key = GetSoloDungeonInstanceOwnerKey(conn, conn.CurrentZoneName);
            if (!_soloDungeonInstanceIds.ContainsKey(key))
                return;

            _soloDungeonLastActiveUtc[key] = DateTime.UtcNow;
            Debug.LogError($"[INSTANCE-STATE] zone={conn.CurrentZoneName} owner={key} instance={conn.InstanceId:X8} state=touch reason={reason ?? "unknown"}");
        }

        private uint AllocateSoloDungeonInstanceId(RRConnection conn, string zoneName)
        {
            string key = GetSoloDungeonInstanceOwnerKey(conn, zoneName);
            if (_soloDungeonInstanceIds.TryGetValue(key, out uint instanceId))
            {
                if (IsSoloDungeonMemoryExpired(key))
                {
                    ForgetSoloDungeonInstance(key, zoneName, instanceId, "allocate-expired");
                }
                else
                {
                    _soloDungeonLastActiveUtc[key] = DateTime.UtcNow;
                    Debug.LogError($"[INSTANCE-STATE] zone={zoneName ?? ""} owner={key} instance={instanceId:X8} state=late-join activeSolo={_soloDungeonInstanceIds.Count}");
                    return instanceId;
                }
            }

            _nextSoloDungeonInstanceId++;
            if (_nextSoloDungeonInstanceId < 0x80000000u)
                _nextSoloDungeonInstanceId = 0x80000001u;

            _soloDungeonInstanceIds[key] = _nextSoloDungeonInstanceId;
            _soloDungeonLastActiveUtc[key] = DateTime.UtcNow;
            Debug.LogError($"[INSTANCE-STATE] zone={zoneName ?? ""} owner={key} instance={_nextSoloDungeonInstanceId:X8} state=fresh activeSolo={_soloDungeonInstanceIds.Count}");
            return _nextSoloDungeonInstanceId;
        }

        public int ResetSoloDungeonInstances(RRConnection conn)
        {
            if (conn == null)
                return 0;

            uint charId = GetCharSqlId(conn);
            string ownerPrefix = (charId != 0 ? $"char{charId}" : "char0") + ":";
            var ownerKeys = _soloDungeonInstanceIds.Keys
                .Where(k => k.StartsWith(ownerPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            int resetCount = 0;
            foreach (string key in ownerKeys)
            {
                if (!_soloDungeonInstanceIds.TryGetValue(key, out uint instanceId))
                    continue;
                string zoneName = key.Substring(ownerPrefix.Length);
                string instanceKey = $"{zoneName}_inst{instanceId}";
                CombatRuntime.Instance.ClearInstanceMobs(instanceKey);
                ForgetSoloDungeonInstance(key, zoneName, instanceId, "reset-instances");
                resetCount++;
            }

            Debug.LogError($"[GROUP] ResetSoloDungeonInstances conn={conn.ConnId} char={charId:X8} reset {resetCount} solo instance(s)");
            return resetCount;
        }

        private uint GenerateDungeonLayoutSeed()
        {
            byte[] bytes = new byte[4];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);

            uint seed = BitConverter.ToUInt32(bytes, 0) ^ (uint)(DateTime.Now.Ticks & 0xFFFFFFFF);
            return seed != 0 ? seed : 1u;
        }

        private uint ResolveRuntimeZoneSeed(RRConnection conn, string zoneName)
        {
            if (conn == null)
            {
                uint fallbackSeed = GenerateDungeonLayoutSeed();
                string fallbackKey = $"no-conn:{zoneName ?? string.Empty}:{fallbackSeed:X8}";
                if (_loggedRuntimeZoneSeeds.Add(fallbackKey))
                    Debug.LogError($"[RUNTIME-SEED] conn=<null> zone={zoneName ?? "<null>"} instance=<none> seed=0x{fallbackSeed:X8} source=fallback");
                return fallbackSeed;
            }

            string instanceKey = GetDungeonLayoutSeedKey(conn, zoneName);
            var group = GroupDirectory.Instance.GetGroupForConn(conn.ConnId);
            if (group != null)
            {
                if (group.EntityManagerSeed == 0)
                {
                    group.EntityManagerSeed = GenerateDungeonLayoutSeed();
                    Debug.LogError($"[RUNTIME-SEED] generated missing group entity-manager seed groupId={group.GroupId} seed=0x{group.EntityManagerSeed:X8}");
                }

                uint groupSeed = group.EntityManagerSeed != 0 ? group.EntityManagerSeed : 1u;
                if (_zoneInstanceRoomSeeds.TryGetValue(instanceKey, out uint cachedGroupSeed) && cachedGroupSeed != groupSeed)
                    Debug.LogError($"[RUNTIME-SEED] replace instance='{instanceKey}' cached=0x{cachedGroupSeed:X8} group=0x{groupSeed:X8} groupId={group.GroupId}");
                _zoneInstanceRoomSeeds[instanceKey] = groupSeed;
                LogRuntimeZoneSeed(conn, zoneName, instanceKey, groupSeed, $"group-room={group.GroupId}");
                return groupSeed;
            }

            if (!_zoneInstanceRoomSeeds.TryGetValue(instanceKey, out uint seed))
            {
                seed = GenerateDungeonLayoutSeed();
                _zoneInstanceRoomSeeds[instanceKey] = seed;
                LogRuntimeZoneSeed(conn, zoneName, instanceKey, seed, "solo-new");
                return seed;
            }

            LogRuntimeZoneSeed(conn, zoneName, instanceKey, seed, "solo-cache");
            return seed;
        }

        private void LogRuntimeZoneSeed(RRConnection conn, string zoneName, string instanceKey, uint seed, string source)
        {
            string key = $"{conn.ConnId}:{zoneName ?? string.Empty}:{instanceKey}:{seed:X8}";
            if (_loggedRuntimeZoneSeeds.Add(key))
                Debug.LogError($"[RUNTIME-SEED] conn={conn.ConnId} zone={zoneName ?? "<null>"} instance='{instanceKey}' seed=0x{seed:X8} source={source}");
        }

        public PlayerState GetPlayerState(string connId)
        {
            if (!_playerStates.ContainsKey(connId))
            {
                _playerStates[connId] = new PlayerState();
            }
            return _playerStates[connId];
        }

        private static bool IsPassiveSkill(string skillGcClass)
        {
            if (string.IsNullOrEmpty(skillGcClass)) return false;
            string lower = skillGcClass.ToLowerInvariant();
            return lower.Contains("passive") || lower.Contains("trait");
        }

        internal struct PassiveManipulator
        {
            public uint Slot;
            public string Skill;
            public byte Level;
            public uint ModifierId;
        }

        private const uint PassiveModifierIdBase = 0xF000;

        private static uint ResolvePassiveModifierId(uint slot)
        {
            return slot > uint.MaxValue - PassiveModifierIdBase ? uint.MaxValue : PassiveModifierIdBase + slot;
        }

        internal static List<PassiveManipulator> CollectPassiveManipulators(SavedCharacter savedChar)
        {
            var result = new List<PassiveManipulator>();
            if (savedChar?.hotbarSlots == null) return result;
            var seenSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var hotbarSlot in savedChar.hotbarSlots.OrderBy(h => h.slot))
            {
                if (!IsPassiveSkill(hotbarSlot.skill)) continue;
                if (!seenSkills.Add(hotbarSlot.skill)) continue;
                int skillLevel = Math.Max(1, savedChar.GetSkillLevel(hotbarSlot.skill));
                byte level = (byte)Math.Min(byte.MaxValue, skillLevel);
                result.Add(new PassiveManipulator { Slot = hotbarSlot.slot, Skill = hotbarSlot.skill, Level = level, ModifierId = ResolvePassiveModifierId(hotbarSlot.slot) });
            }
            return result;
        }

        internal static void WritePassiveManipulatorChild(LEWriter writer, PassiveManipulator passive)
        {
            writer.WriteByte(0xFF);
            writer.WriteCString(passive.Skill.ToLowerInvariant());
            writer.WriteUInt32(passive.Slot);
            writer.WriteByte(passive.Level);
            writer.WriteUInt32(passive.ModifierId);
        }

        internal static void WritePassiveModifiersComponent(LEWriter writer, List<PassiveManipulator> passives, ushort sourceAvatarEntityId)
        {
            var resolvedPassives = (passives ?? new List<PassiveManipulator>())
                .Select(passive => (Passive: passive, ModifierPath: PassiveAttributeModifiers.ResolveModifierPath(passive.Skill)))
                .Where(passive => !string.IsNullOrWhiteSpace(passive.ModifierPath))
                .ToList();
            uint nextModifierId = resolvedPassives.Count > 0 ? resolvedPassives.Max(p => p.Passive.ModifierId) + 1u : PassiveModifierIdBase;
            int posBefore = writer.Length;
            writer.WriteUInt32(nextModifierId);
            writer.WriteUInt32(0x00000000);
            writer.WriteByte((byte)resolvedPassives.Count);
            foreach (var resolvedPassive in resolvedPassives)
            {
                writer.WriteByte(0xFF);
                writer.WriteCString(resolvedPassive.ModifierPath.ToLowerInvariant());
                writer.WriteUInt32(resolvedPassive.Passive.ModifierId);
                writer.WriteByte(resolvedPassive.Passive.Level);
                writer.WriteUInt32(0x00000000);
                writer.WriteUInt32(0x00000000);
                writer.WriteByte(0x01);
            }
            foreach (var resolvedPassive in resolvedPassives)
            {
                writer.WriteByte(0x01);
            }
            if (resolvedPassives.Count > 0)
            {
                byte[] buffer = writer.GetBuffer();
                int length = buffer.Length - posBefore;
                string hex = BitConverter.ToString(buffer, posBefore, length).Replace("-", "");
                string paths = string.Join(",", resolvedPassives.Select(p => p.ModifierPath.ToLowerInvariant() + "#" + p.Passive.ModifierId));
                Debug.LogError($"[MODIFIERS-COMPONENT] avatar={sourceAvatarEntityId} count={resolvedPassives.Count} bytes={length} paths={paths} hex={hex}");
            }
        }

        private static string NormalizeClassPassiveKey(string className)
        {
            if (string.IsNullOrWhiteSpace(className)) return "Fighter";
            string key = className.Trim();
            if (key.EndsWith("Base", StringComparison.OrdinalIgnoreCase))
                key = key.Substring(0, key.Length - 4);
            int lastDot = key.LastIndexOf('.');
            if (lastDot >= 0 && lastDot + 1 < key.Length)
                key = key.Substring(lastDot + 1);
            if (key.Equals("Warlock", StringComparison.OrdinalIgnoreCase))
                return "Mage";
            if (key.Equals("Warrior", StringComparison.OrdinalIgnoreCase))
                return "Fighter";
            if (key.Equals("Ranger", StringComparison.OrdinalIgnoreCase))
                return "Ranger";
            if (key.Equals("Mage", StringComparison.OrdinalIgnoreCase))
                return "Mage";
            if (key.Equals("Fighter", StringComparison.OrdinalIgnoreCase))
                return "Fighter";
            return key;
        }

        private static string ResolveSavedCharacterClassPassiveKey(SavedCharacter savedChar)
        {
            if (!string.IsNullOrWhiteSpace(savedChar?.className))
                return NormalizeClassPassiveKey(savedChar.className);
            if (!string.IsNullOrWhiteSpace(savedChar?.avatarClass))
                return NormalizeClassPassiveKey(savedChar.avatarClass);
            return null;
        }

        private struct PlayerHPPreserve
        {
            public bool HasHP;
            public uint HPWire;
            public uint MaxAtCapture;
            public string Source;
            public bool FromLiveState;
            public bool FromObserved;
            public bool FromSaved;
        }

        private static bool IsPreservedHPWithinTolerance(uint hpWire, uint maxHPWire)
        {
            if (hpWire == 0) return false;
            if (maxHPWire == 0) return true;
            return (ulong)hpWire <= (ulong)maxHPWire + (5u * 256u);
        }

        private static PlayerHPPreserve MakePlayerHPPreserve(uint hpWire, uint maxAtCapture, string source, bool live, bool observed, bool saved)
        {
            return new PlayerHPPreserve
            {
                HasHP = hpWire > 0,
                HPWire = hpWire,
                MaxAtCapture = maxAtCapture,
                Source = source ?? "unknown",
                FromLiveState = live,
                FromObserved = observed,
                FromSaved = saved
            };
        }

        private PlayerHPPreserve CapturePlayerHPPreserve(RRConnection conn, PlayerState playerState, SavedCharacter savedChar, string phase, bool includeSavedCharacter)
        {
            PlayerHPPreserve preserve = default;
            uint maxAtCapture = playerState != null ? playerState.MaxHPWire : 0;

            if (playerState != null)
            {
                if (playerState.HasClientHP && playerState.CurrentHPWire > 0)
                    preserve = MakePlayerHPPreserve(playerState.CurrentHPWire, maxAtCapture, "live-playerstate", true, false, false);
                else if (playerState.HasObservedClientHP && playerState.LastObservedClientHPWire > 0)
                    preserve = MakePlayerHPPreserve(playerState.LastObservedClientHPWire, maxAtCapture, $"observed-client:{playerState.LastObservedClientHPSource ?? "unknown"}", false, true, false);
                else if (playerState.HasEntitySynchInfoHP && playerState.EntitySynchInfoHP > 0)
                    Debug.LogError($"[HP-PRESERVE] phase={phase}-ignore-synthetic-entity-synch-info hp={playerState.EntitySynchInfoHP} maxAtCapture={maxAtCapture} source=server-entity-synch-info sourceFunction=client-mirror-only");

            }

            if (!preserve.HasHP && includeSavedCharacter && savedChar != null && savedChar.currentHP > 0)
                preserve = MakePlayerHPPreserve(savedChar.currentHP, maxAtCapture, "saved-character", false, false, true);

            Debug.LogError($"[HP-PRESERVE] phase={phase}-capture source={(preserve.HasHP ? preserve.Source : "none")} hp={preserve.HPWire} maxAtCapture={maxAtCapture} live={preserve.FromLiveState} observed={preserve.FromObserved} saved={preserve.FromSaved}");
            return preserve;
        }

        private bool ApplyPlayerHPPreserve(RRConnection conn, PlayerState playerState, PlayerHPPreserve preserve, string phase, bool applyDamageCooldown)
        {
            if (playerState == null) return false;
            uint beforeHP = playerState.CurrentHPWire;
            uint maxAfter = playerState.MaxHPWire;
            if (!preserve.HasHP)
            {
                Debug.LogError($"[HP-PRESERVE] phase={phase} source=none before={beforeHP} maxAfter={maxAfter} applied={playerState.CurrentHPWire} entitySynchInfoHP={playerState.EntitySynchInfoHP}");
                return false;
            }

            if (maxAfter > 0 && preserve.HPWire > maxAfter)
            {
                playerState.SetCurrentHPDeferClamp(preserve.HPWire);
            }
            else
            {
                uint appliedHP = maxAfter > 0 ? Math.Min(preserve.HPWire, maxAfter) : preserve.HPWire;
                playerState.SetCurrentHP(appliedHP, applyDamageCooldown && appliedHP < beforeHP);
            }
            if (conn?.Avatar != null && conn.Avatar.Id != 0)
                EntitySynchInfoAuthority.Instance.RegisterPlayer(conn, playerState, (uint)conn.Avatar.Id);
            Debug.LogError($"[HP-PRESERVE] phase={phase} source={preserve.Source} captured={preserve.HPWire} before={beforeHP} maxBefore={preserve.MaxAtCapture} maxAfter={maxAfter} applied={playerState.CurrentHPWire} entitySynchInfoHP={playerState.EntitySynchInfoHP} live={preserve.FromLiveState} observed={preserve.FromObserved} saved={preserve.FromSaved}");
            return true;
        }

        private void ApplyFullHPBaseline(RRConnection conn, PlayerState playerState, PlayerHPPreserve ignoredPreserve, string phase)
        {
            if (playerState == null) return;
            uint beforeHP = playerState.CurrentHPWire;
            uint beforeEntitySynchInfoHP = playerState.EntitySynchInfoHP;
            uint maxBefore = playerState.MaxHPWire;
            playerState.RestoreToFull();
            if (phase != null && phase.StartsWith("zone"))
            {
                uint noPassiveMax = playerState.MaxHPWireWithoutPassives;
                if (noPassiveMax > playerState.MaxHPWire)
                {
                    playerState.SetCurrentHPDeferClamp(noPassiveMax);
                    playerState.BeginPassiveMaxTransition(noPassiveMax);
                }
            }
            if (conn?.Avatar != null && conn.Avatar.Id != 0)
                EntitySynchInfoAuthority.Instance.RegisterPlayer(conn, playerState, (uint)conn.Avatar.Id);
            Debug.LogError($"[ZONE-HP-BASELINE] phase={phase} ignoredSource={(ignoredPreserve.HasHP ? ignoredPreserve.Source : "none")} ignoredHP={ignoredPreserve.HPWire} before={beforeHP}/{maxBefore} beforeEntitySynchInfoHP={beforeEntitySynchInfoHP} applied={playerState.CurrentHPWire}/{playerState.MaxHPWire} entitySynchInfoHP={playerState.EntitySynchInfoHP}");
        }

        private SavedCharacter GetActiveCharacter(RRConnection conn)
        {
            if (conn == null || conn.LoginName == null) return null;
            if (!_selectedCharacter.TryGetValue(conn.LoginName, out var gc) || gc == null) return null;
            if (_activeCharacter.TryGetValue(conn.LoginName, out var cached) && cached != null && cached.id == gc.Id)
                return cached;
            var loaded = CharacterRepository.GetCharacter(gc.Id);
            if (loaded != null) _activeCharacter[conn.LoginName] = loaded;
            return loaded;
        }

        private void InvalidateActiveCharacter(RRConnection conn)
        {
            if (conn?.LoginName != null) _activeCharacter.Remove(conn.LoginName);
        }

        private void RecalculateHotbarPassiveBonuses(string connId)
        {
            RRConnection conn = _connections.Values.FirstOrDefault(c => c.ConnId.ToString() == connId);
            if (conn == null || conn.LoginName == null || !_selectedCharacter.ContainsKey(conn.LoginName))
            {
                PlayerState defaultState = GetPlayerState(connId);
                PlayerHPPreserve defaultHP = CapturePlayerHPPreserve(conn, defaultState, null, "passive", false);
                defaultState.SetPassiveBonuses(0, 0);
                ApplyPlayerHPPreserve(conn, defaultState, defaultHP, "passive", true);
                return;
            }

            SavedCharacter savedChar = GetActiveCharacter(conn);
            RecalculateHotbarPassiveBonuses(conn, savedChar, sendModifiers: false);
        }

        private static uint ScaleWirePercent(uint wire, decimal percent)
        {
            if (wire == 0 || percent <= 0m) return 0;
            decimal scaled = decimal.Truncate(wire * percent / 100m);
            if (scaled >= uint.MaxValue) return uint.MaxValue;
            return (uint)scaled;
        }

        private static int ClampInt64(long value)
        {
            if (value > int.MaxValue) return int.MaxValue;
            if (value < int.MinValue) return int.MinValue;
            return (int)value;
        }

        private void RecalculateHotbarPassiveBonuses(RRConnection conn, SavedCharacter savedChar, bool sendModifiers = true, bool keepPvpPassive = false)
        {
            if (conn == null) return;

            if (!keepPvpPassive && IsPvpZone(conn.CurrentZoneName))
            {
                PlayerState pvpState = GetPlayerState(conn.ConnId.ToString());
                if (pvpState != null)
                {
                    PlayerHPPreserve pvpHpPreserve = CapturePlayerHPPreserve(conn, pvpState, savedChar, "passive", false);
                    pvpState.SetPassiveBonuses(0, 0);
                    ApplyPlayerHPPreserve(conn, pvpState, pvpHpPreserve, "passive", true);
                }
                return;
            }

            PlayerState playerState = GetPlayerState(conn.ConnId.ToString());
            if (playerState != null)
                playerState.AdvanceEntitySynchInfoHP(Time.time, "passive-recompute-pre");
            PlayerHPPreserve hpPreserve = CapturePlayerHPPreserve(conn, playerState, savedChar, "passive", false);
            int hpWireBonus = 0;
            int manaWireBonus = 0;
            int strengthMod = 0;
            int agilityMod = 0;
            int enduranceMod = 0;
            int intellectMod = 0;
            int meleeAttackRatingModPercent = 0;
            float meleeAttackSpeedModPercent = 0f;
            float rangeAttackSpeedModPercent = 0f;
            float magicDamageModPercent = 0f;
            int healthPerEnduranceModPercent = 0;
            int manaPerIntellectModPercent = 0;
            List<string> passiveSkills = CollectPassiveManipulators(savedChar).Select(p => p.Skill).ToList();

            if (passiveSkills.Count > 0)
            {
                var passiveLevels = passiveSkills
                    .Select(skill => (Skill: skill, Level: Math.Max(1, savedChar.GetSkillLevel(skill))))
                    .ToList();
                PassiveAttributeTotals totals = PassiveAttributeModifiers.Resolve(passiveLevels);
                strengthMod = totals.Strength;
                agilityMod = totals.Agility;
                enduranceMod = totals.Endurance;
                intellectMod = totals.Intellect;
                healthPerEnduranceModPercent = totals.HealthPerEnduranceMod;
                manaPerIntellectModPercent = totals.ManaPerIntellectMod;
                meleeAttackRatingModPercent = totals.MeleeAttackRatingMod;
                meleeAttackSpeedModPercent = (float)totals.MeleeAttackSpeedMod;
                rangeAttackSpeedModPercent = (float)totals.RangeAttackSpeedMod;
                magicDamageModPercent = (float)totals.MagicDamageMod;

                int baseEndurance = 10 + Math.Max(0, playerState.AllocatedEndurance);
                int passiveEndurance = Math.Max(1, baseEndurance + enduranceMod);
                uint noPassiveHP = ClassPassiveData.CalculateHPWire(playerState.Level, baseEndurance, 0);
                uint passiveHP = ClassPassiveData.CalculateHPWire(playerState.Level, passiveEndurance, healthPerEnduranceModPercent);
                passiveHP = ScaleWirePercent(passiveHP, 100m + totals.HealthMod);
                hpWireBonus = ClampInt64((long)passiveHP - noPassiveHP);

                int baseIntellect = 10 + Math.Max(0, playerState.AllocatedIntellect);
                int passiveIntellect = Math.Max(1, baseIntellect + intellectMod);
                uint noPassiveMana = ClassPassiveData.CalculateManaWire(playerState.Level, baseIntellect, 0);
                uint passiveMana = ClassPassiveData.CalculateManaWire(playerState.Level, passiveIntellect, manaPerIntellectModPercent);
                manaWireBonus = ClampInt64((long)passiveMana - noPassiveMana);
            }

            uint oldMaxWire = playerState.MaxHPWire;
            playerState.SetPassiveBonuses(hpWireBonus, manaWireBonus, meleeAttackRatingModPercent, meleeAttackSpeedModPercent, rangeAttackSpeedModPercent, strengthMod, agilityMod, enduranceMod, intellectMod, healthPerEnduranceModPercent, manaPerIntellectModPercent, magicDamageModPercent);
            ApplyPlayerHPPreserve(conn, playerState, hpPreserve, "passive", true);
            if (playerState.MaxHPWire < oldMaxWire && playerState.CurrentHPWire > playerState.MaxHPWire)
                playerState.BeginPassiveMaxTransition(oldMaxWire);
            else if (keepPvpPassive && playerState.MaxHPWire > oldMaxWire)
                playerState.RestoreToFull();

            if (sendModifiers)
                SendPassiveModifiers(conn, savedChar);
        }

        private void SendPassiveModifiers(RRConnection conn, SavedCharacter savedChar)
        {
            if (conn.ModifiersId == 0 || conn.Avatar == null) return;
            var passives = CollectPassiveManipulators(savedChar)
                .Select(passive => (Passive: passive, ModifierPath: PassiveAttributeModifiers.ResolveModifierPath(passive.Skill)))
                .Where(passive => !string.IsNullOrWhiteSpace(passive.ModifierPath))
                .ToList();
            foreach (var resolved in passives)
            {
                if (!conn.SentPassiveModifierIds.Add(resolved.Passive.ModifierId))
                    continue;
                var writer = new LEWriter();
                writer.WriteByte(0x07);
                writer.WriteByte(0x35);
                writer.WriteUInt16((ushort)conn.ModifiersId);
                writer.WriteByte(0x00);
                WriteGCType(writer, resolved.ModifierPath, preserveCase: false);
                writer.WriteUInt32(resolved.Passive.ModifierId);
                writer.WriteByte(resolved.Passive.Level);
                writer.WriteUInt32(0x00000000);
                writer.WriteUInt32(0x00000000);
                writer.WriteByte(0x01);
                if (!TryWriteEntitySynchForComponent(conn, writer, (ushort)conn.ModifiersId, 0x00, EntitySynchInfoContext.PlayerActionResponse, "SendPassiveModifiers", true))
                    continue;
                writer.WriteByte(0x06);
                SendCompressedA(conn, 0x01, 0x0F, writer.ToArray());
            }
        }

        private static int ResolveItemModSlotDivisor(GCObject item, string gcClass)
        {
            if (item == null)
                return 8;

            string dfcClass = item.DFCClass ?? string.Empty;
            if (dfcClass == "MeleeWeapon" || dfcClass == "RangedWeapon")
                return 8;

            uint slot = item.TargetSlot ?? 0;
            if (slot == 0)
                slot = item.GetEquipmentSlotFromGCClass();

            return slot switch
            {
                2 or 8 => 20,
                6 => 5,
                5 or 7 or 11 => 10,
                _ => 8
            };
        }

        private static int ResolveEquipmentItemModLevel(GCObject item, string gcClass, int rarity, int playerLevel)
        {
            if (item != null && item.StoredLevel >= 0)
                return Math.Max(1, item.StoredLevel);
            if (rarity == (int)ItemRarity.Mythic)
                return Math.Max(1, playerLevel + 3);
            return Math.Max(1, RPGSettings.GetItemLevel(gcClass));
        }

        private bool TryResolveWeaponDescIds(
            string source,
            string weaponPath,
            string weaponClass,
            string damageType,
            out int weaponClassId,
            out int damageTypeId)
        {
            bool classOk = DamageResolver.TryResolveWeaponClassId(weaponClass, out weaponClassId);
            bool typeOk = DamageResolver.TryResolveDamageTypeId(damageType, out damageTypeId);
            if (classOk && typeOk)
                return true;

            RuntimeEvidence.LogFallbackHit(
                "damage-weapon-desc",
                "unresolved-client-id",
                $"source={source ?? "<null>"} weapon={weaponPath ?? "<null>"} weaponClass={weaponClass ?? "<null>"} damageType={damageType ?? "<null>"} classOk={classOk} typeOk={typeOk} sourceFunction=Weapon::ComputeAttributes-blocked",
                64);
            return false;
        }

        public void CalculateEquipmentBonuses(string connId, GCObject avatar)
        {
            PlayerState playerState = GetPlayerState(connId);
            RRConnection hpConn = _connections.Values.FirstOrDefault(c => c.ConnId.ToString() == connId);
            SavedCharacter hpSavedChar = null;
            if (hpConn != null && hpConn.LoginName != null && _selectedCharacter.ContainsKey(hpConn.LoginName))
                hpSavedChar = GetActiveCharacter(hpConn);
            PlayerHPPreserve hpPreserve = CapturePlayerHPPreserve(hpConn, playerState, hpSavedChar, "equip", false);
            playerState.ClearEquipmentBonuses();

            float bestWeaponDamage = 0.79f;
            float bestWeaponVolatility = 0.25f;
            int bestWeaponLevel = 1;
            int bestWeaponStoredLevel = -1;
            string bestWeaponClass = "";
            string bestWeaponDamageType = "";
            string bestWeaponCategory = "";
            int bestWeaponClassId = 0;
            int bestDamageTypeId = -1;
            int bestWeaponRange = 0;
            float bestWeaponCooldown = 0f;
            float bestWeaponSpeed = 105f;
            bool bestWeaponUsesProjectile = false;
            float bestWeaponProjectileSpeed = 0f;
            float bestWeaponProjectileSize = 0f;
            int bestWeaponBurstCount = 1;
            bool foundWeapon = false;

            if (avatar == null) { Debug.LogError("[EQUIP-STATS] avatar is NULL"); return; }
            var equipment = avatar.Children?.FirstOrDefault(c => c.GCClass == "avatar.base.Equipment");

            var allItems = new Dictionary<string, GCObject>(StringComparer.OrdinalIgnoreCase);
            bool usedTrackedItems = false;
            if (_playerEquippedItems.TryGetValue(connId, out var tracked) && tracked != null && tracked.Count > 0)
            {
                foreach (var trackedItemEntry in tracked)
                    if (trackedItemEntry.Value?.GCClass != null)
                        allItems[trackedItemEntry.Value.GCClass] = trackedItemEntry.Value;
                usedTrackedItems = allItems.Count > 0;
                Debug.LogError($"[EQUIP-STATS] Using {allItems.Count}/{tracked.Count} TRACKED items (authoritative)");
            }
            if (!usedTrackedItems && equipment?.Children != null)
            {
                foreach (var child in equipment.Children)
                    allItems[child.GCClass ?? ""] = child;
                Debug.LogError($"[EQUIP-STATS] Using {allItems.Count} AVATAR children (runtime equipment fallback)");
            }

            Debug.LogError($"[EQUIP-STATS] Processing {allItems.Count} equipped items, DB loaded={DungeonRunners.Data.ItemStatDatabase.Instance.IsLoaded}");

            var itemStatDb = DungeonRunners.Data.ItemStatDatabase.Instance;

            foreach (var item in allItems.Values)
            {
                string gc = item.GCClass ?? "";
                int rarity = item.GetEffectiveRarity();
                string pattern = DungeonRunners.Data.ItemStatDatabase.ExtractPattern(gc);
                bool isWeapon = item.DFCClass == "MeleeWeapon" || item.DFCClass == "RangedWeapon";
                bool isArmor = item.DFCClass == "Armor";
                if (isArmor)
                {
                    float armorDefense = GCDatabase.Instance.GetArmorDefenseRating(gc);
                    if (armorDefense > 0f)
                    {
                        int armorLevel = Math.Max(1, item.StoredLevel >= 0 ? item.StoredLevel : DungeonRunners.Gameplay.RPGSettings.GetItemLevel(gc));
                        float itemDefensePerLevel = GCDatabase.Instance.GetKnob("ItemDefenseRatingPerLevel", 8.26f);
                        int defenseRating = Mathf.Max(0, Mathf.FloorToInt(itemDefensePerLevel * armorLevel * armorDefense) + 1);
                        playerState.AddArmorDefenseRating(defenseRating);
                        if (playerState.EquipmentStats.ContainsKey("DEFENSE_RATING"))
                            playerState.EquipmentStats["DEFENSE_RATING"] += defenseRating;
                        else
                            playerState.EquipmentStats["DEFENSE_RATING"] = defenseRating;
                        Debug.LogError($"[EQUIP-ARMOR] {gc}: level={armorLevel} armorDefense={armorDefense:F4} defenseRating={defenseRating}");
                    }
                }

                if ((isWeapon || isArmor) && itemStatDb.IsLoaded && itemStatDb.HasItem(gc, rarity))
                {
                    int slotDivisor = ResolveItemModSlotDivisor(item, gc);
                    int itemModLevel = ResolveEquipmentItemModLevel(item, gc, rarity, playerState.Level);
                    var stats = itemStatDb.GetItemStatsAtItemLevel(gc, itemModLevel, slotDivisor, rarity);
                    var attrs = itemStatDb.GetItemAttributes(gc, rarity);
                    Debug.LogError($"[EQUIP-ITEM] {gc} -> DB hit rarity={rarity} itemLevel={itemModLevel} slotDivisor={slotDivisor} {stats.Count} stats: {string.Join(", ", attrs)}");

                    stats.TryGetValue("MAX_HIT_POINTS", out int hpBonus);
                    stats.TryGetValue("ENDURANCE", out int endBonus);
                    stats.TryGetValue("MAX_MANA_POINTS", out int manaBonus);
                    stats.TryGetValue("INTELLECT", out int intBonus);

                    if (hpBonus > 0) playerState.AddTotalHealthBonus(hpBonus);
                    if (endBonus > 0) playerState.AddEnduranceBonus(endBonus);
                    if (manaBonus > 0) playerState.AddManaBonus(manaBonus);
                    if (intBonus > 0) playerState.AddIntellectManaBonus(intBonus);

                    foreach (var statEntry in stats)
                    {
                        if (playerState.EquipmentStats.ContainsKey(statEntry.Key))
                            playerState.EquipmentStats[statEntry.Key] += statEntry.Value;
                        else
                            playerState.EquipmentStats[statEntry.Key] = statEntry.Value;
                    }

                    if (hpBonus > 0 || endBonus > 0 || manaBonus > 0 || intBonus > 0)
                        Debug.LogError($"[EQUIP-STATS] {gc}: HP+{hpBonus} END+{endBonus} MANA+{manaBonus} INT+{intBonus}(+{intBonus * GCDatabase.Instance.GetKnobInt("PowerPerIntellect", 17)}mp)");
                }
                else if (isArmor || isWeapon)
                {
                    Debug.LogError($"[EQUIP-ITEM] {gc} -> no authored ItemModifier rows for DFCClass={item.DFCClass} pattern={pattern} rarity={rarity}; stats unchanged sourceFunction=ItemModifier::AddModifiers@0x00588890");
                }

                if (item.DFCClass == "MeleeWeapon" || item.DFCClass == "RangedWeapon")
                {
                    var weaponData = DatabaseLoader.FindItem(gc);
                    var weaponNode = GCDatabase.Instance.ResolveWithInheritance(gc);
                    (float damage, float volatility, float range, float cooldown, string weaponClass, float weaponSpeed,
                     string damageType, string weaponCategory, bool useProjectile, float projectileSpeed, float projectileSize, int burstCount) weaponStats = default;
                    if (weaponNode == null)
                    {
                        RuntimeEvidence.LogFallbackHit("damage-weapon-desc", "missing-gc-node", $"source=equip weapon={gc} sourceFunction=Weapon::ComputeAttributes-return", 64);
                        continue;
                    }

                    weaponStats = GCDatabase.Instance.GetWeaponStats(gc);
                    float authoredWeaponDamage = weaponStats.damage > 0f ? weaponStats.damage : 0f;
                    float authoredWeaponVolatility = weaponStats.volatility > 0f ? weaponStats.volatility : 0.25f;
                    if (authoredWeaponDamage > 0f)
                    {
                        string resolvedWeaponClass = !string.IsNullOrEmpty(weaponStats.weaponClass) ? weaponStats.weaponClass : weaponData != null && !string.IsNullOrEmpty(weaponData.weaponClass) ? weaponData.weaponClass : "";
                        string resolvedWeaponDamageType = !string.IsNullOrEmpty(weaponStats.damageType) ? weaponStats.damageType : "";
                        if (!TryResolveWeaponDescIds("equip", gc, resolvedWeaponClass, resolvedWeaponDamageType, out int clientWeaponClassId, out int clientDamageTypeId))
                            continue;

                        bestWeaponDamage = authoredWeaponDamage;
                        bestWeaponVolatility = Mathf.Clamp(authoredWeaponVolatility, 0f, 0.95f);
                        bestWeaponLevel = Math.Max(1, item.StoredLevel >= 0 ? item.StoredLevel : DungeonRunners.Gameplay.RPGSettings.GetItemLevel(gc));
                        bestWeaponStoredLevel = item.StoredLevel;
                        bestWeaponClass = resolvedWeaponClass;
                        bestWeaponDamageType = resolvedWeaponDamageType;
                        bestWeaponCategory = !string.IsNullOrEmpty(weaponStats.weaponCategory) ? weaponStats.weaponCategory : "";
                        bestWeaponClassId = clientWeaponClassId;
                        bestDamageTypeId = clientDamageTypeId;
                        bestWeaponRange = weaponStats.range > 0 ? Mathf.RoundToInt(weaponStats.range) : weaponData != null && weaponData.range > 0 ? weaponData.range : 0;
                        bestWeaponCooldown = weaponStats.cooldown > 0 ? weaponStats.cooldown : weaponData != null && weaponData.cooldown > 0 ? weaponData.cooldown : 0f;
                        bestWeaponSpeed = weaponStats.weaponSpeed > 0 ? weaponStats.weaponSpeed : weaponData != null && weaponData.weaponSpeed > 0 ? weaponData.weaponSpeed : 105f;
                        bestWeaponUsesProjectile = weaponStats.useProjectile;
                        bestWeaponProjectileSpeed = weaponStats.projectileSpeed;
                        bestWeaponProjectileSize = weaponStats.projectileSize;
                        bestWeaponBurstCount = Math.Max(1, weaponStats.burstCount);
                        foundWeapon = true;
                        Debug.LogError($"[EQUIP-WEAPON] {gc}: dmg={bestWeaponDamage:F2} vol={bestWeaponVolatility:F2} level={bestWeaponLevel} class={bestWeaponClass} damageType={bestWeaponDamageType} category={bestWeaponCategory} range={bestWeaponRange} cd={bestWeaponCooldown:F2} speed={bestWeaponSpeed:F2} useProjectile={bestWeaponUsesProjectile} projectileSpeed={bestWeaponProjectileSpeed:F2} projectileSize={bestWeaponProjectileSize:F2} burst={bestWeaponBurstCount}");
                    }
                }
            }
            RecalculateHotbarPassiveBonuses(connId);
            playerState.RecalculateCurrentHP();
            ApplyPlayerHPPreserve(hpConn, playerState, hpPreserve, "equip", true);

            playerState.SetCurrentMana(playerState.MaxManaWire);

            Debug.LogError($"[EQUIP-TOTAL] MaxHP={playerState.MaxHPWire / 256} MaxMana={playerState.MaxManaWire / 256} EquipStats={playerState.EquipmentStats.Count}");
            foreach (var statEntry in playerState.EquipmentStats)
                Debug.LogError($"[EQUIP-TOTAL]   {statEntry.Key} = {statEntry.Value}");

            if (foundWeapon)
            {
                playerState.WeaponDamage = bestWeaponDamage;
                playerState.WeaponDamageVolatility = bestWeaponVolatility;
                playerState.WeaponLevel = bestWeaponLevel;
                playerState.WeaponClass = bestWeaponClass;
                playerState.WeaponDamageType = bestWeaponDamageType;
                playerState.WeaponCategory = bestWeaponCategory;
                playerState.WeaponStatsResolved = true;
                playerState.WeaponClassId = bestWeaponClassId;
                playerState.DamageTypeId = bestDamageTypeId;
                DamageResolver.ApplyWeaponRuntimeBaseDamage(playerState, playerState.Level, bestWeaponStoredLevel, bestWeaponLevel, "equip");
                playerState.WeaponRange = bestWeaponRange;
                playerState.WeaponCooldown = bestWeaponCooldown;
                playerState.WeaponSpeed = bestWeaponSpeed;
                playerState.WeaponUsesProjectile = bestWeaponUsesProjectile;
                playerState.WeaponProjectileSpeed = bestWeaponProjectileSpeed;
                playerState.WeaponProjectileSize = bestWeaponProjectileSize;
                playerState.WeaponBurstCount = bestWeaponBurstCount;
                Debug.LogError($"[EQUIP-WEAPON] PlayerState updated: dmg={bestWeaponDamage:F2} vol={bestWeaponVolatility:F2} level={bestWeaponLevel} clientDamageLevel={playerState.WeaponDamageLevel} clientBaseDamage={playerState.WeaponBaseDamage} clientBaseSource={playerState.WeaponBaseDamageSource} class={bestWeaponClass}/{playerState.WeaponClassId} damageType={bestWeaponDamageType}/{playerState.DamageTypeId} category={bestWeaponCategory} cooldown={bestWeaponCooldown:F2} speed={bestWeaponSpeed:F2} useProjectile={bestWeaponUsesProjectile} projectileSpeed={bestWeaponProjectileSpeed:F2} projectileSize={bestWeaponProjectileSize:F2} burst={bestWeaponBurstCount}");
            }
        }
        public Dictionary<uint, GCObject> GetAllEquippedItems(string connId)
        {
            var items = new Dictionary<uint, GCObject>();

            uint[] slots = { 1, 2, 3, 4, 5, 6, 7, 8, 10, 11 };

            foreach (uint slot in slots)
            {
                GCObject item = GetEquippedItem(connId, slot);
                if (item != null)
                {
                    items[slot] = item;
                }
            }

            return items;
        }



        private int EstimateItemLevel(string gcClass, int playerLevel)
        {
            for (int charIndex = gcClass.Length - 1; charIndex >= 0; charIndex--)
            {
                if (char.IsDigit(gcClass[charIndex]))
                {
                    int digitStartIndex = charIndex;
                    while (digitStartIndex > 0 && char.IsDigit(gcClass[digitStartIndex - 1]))
                        digitStartIndex--;

                    string tierStr = gcClass.Substring(digitStartIndex, charIndex - digitStartIndex + 1);
                    if (int.TryParse(tierStr, out int tier))
                        return Math.Min(tier * 10, playerLevel);
                }
            }
            return playerLevel / 2;
        }






        private string GetComponentType(string connId, ushort componentId)
        {
            if (_playerComponentTypes.ContainsKey(connId) && _playerComponentTypes[connId].ContainsKey(componentId))
            {
                return _playerComponentTypes[connId][componentId];
            }
            return "Unknown";
        }

        private void TrackComponent(string connId, ushort componentId, string componentType)
        {
            if (!_playerComponentTypes.ContainsKey(connId))
            {
                _playerComponentTypes[connId] = new Dictionary<ushort, string>();
            }
            _playerComponentTypes[connId][componentId] = componentType;
            Debug.LogError($"[COMPONENT-TRACK] Player {connId}: ComponentID 0x{componentId:X4} = {componentType}");
        }

        public void TrackEquippedItem(string connId, uint slot, GCObject item)
        {
            if (!_playerEquippedItems.ContainsKey(connId))
            {
                _playerEquippedItems[connId] = new Dictionary<uint, GCObject>();
            }
            _playerEquippedItems[connId][slot] = item;
            Debug.LogError($"[EQUIP-TRACK] Player {connId}: Slot {slot} = {item.GCClass}");
        }



        private uint GetEntitySynchInfoHPValue(RRConnection conn)
        {
            return GetEntitySynchInfoHPValue(conn, false);
        }

        public bool WritePlayerEntitySynch(RRConnection conn, LEWriter writer)
        {
            return TryWritePlayerEntitySynch(conn, writer, EntitySynchInfoContext.PlayerActionResponse, "WritePlayerEntitySynch", true, true);
        }

        public bool WritePlayerEntitySynch(RRConnection conn, LEWriter writer, EntitySynchInfoContext context)
        {
            return TryWritePlayerEntitySynch(conn, writer, context, context.ToString(), true, true);
        }

        private bool WritePlayerEntitySynchNoFlush(RRConnection conn, LEWriter writer)
        {
            return TryWritePlayerEntitySynch(conn, writer, EntitySynchInfoContext.PlayerActionResponse, "WritePlayerEntitySynchNoFlush", true, false);
        }

        private bool WritePlayerEntitySynchNoCombatFlush(RRConnection conn, LEWriter writer)
        {
            return TryWritePlayerEntitySynch(conn, writer, EntitySynchInfoContext.PlayerActionResponse, "WritePlayerEntitySynchNoCombatFlush", true, false);
        }

        private bool TryWritePlayerEntitySynch(RRConnection conn, LEWriter writer, EntitySynchInfoContext context, string packetName, bool advanceEntitySynchInfo, bool flushCombat)
        {
            if (writer == null) return false;
            if (flushCombat)
            {
                GetValidationCutoff(out _, out float validationCutoffTime);
                FlushCombatBeforeSynch(conn, validationCutoffTime);
            }

            if (TryResolveWriterComponentUpdate(conn, writer, out ushort componentId, out byte subtype))
                return TryWriteEntitySynchForComponent(conn, writer, componentId, subtype, context, packetName, advanceEntitySynchInfo);

            if (!TryResolvePlayerEntitySynchInfoHP(conn, context, packetName, advanceEntitySynchInfo, out uint hpWire))
            {
                Debug.LogError($"[ENTITY-SYNCH-INFO-UNRESOLVED] packet={packetName} context={context} owner=Avatar reason=player-hp-unresolved");
                return false;
            }

            GetValidationCutoff(out uint fallbackCutoffTick, out float fallbackCutoffTime);
            string fallbackRuntimeKey = conn != null ? GetInstanceZoneKey(conn) : null;
            int fallbackRngPos = !string.IsNullOrWhiteSpace(fallbackRuntimeKey) ? CombatRuntime.Instance.GetRoomRngPosForInstance(fallbackRuntimeKey) : -1;
            return TryWriteResolvedEntitySynchInfo(writer, 0, 0, context, packetName, EntitySynchInfoDecision.HP(EntitySynchInfoOwner.Avatar, hpWire, packetName, conn?.Avatar != null ? (uint)conn.Avatar.Id : 0u, 0, 0, GetCombatNow(), $"player-fallback; validationCutoffTick={fallbackCutoffTick} validationCutoffTime={fallbackCutoffTime:F3}", fallbackCutoffTick, fallbackCutoffTime, fallbackRuntimeKey, conn?.EntitySchedulerMirror?.SchedulerTick ?? 0, conn?.EntitySchedulerMirror?.SubEntityPhase ?? false, fallbackRngPos));
        }

        private bool TryWriteEntitySynchForComponent(RRConnection conn, LEWriter writer, ushort componentId, byte subtype, string tag, bool advanceEntitySynchInfo)
        {
            return TryWriteEntitySynchForComponent(conn, writer, componentId, subtype, EntitySynchInfoContextFromTag(tag), tag, advanceEntitySynchInfo);
        }

        private static uint ResolveAuthoredUnitMaxHealthWire(string gcType, uint fallbackHPWire = NonCombatInteractiveHPWire)
        {
            if (string.IsNullOrEmpty(gcType) || GCDatabase.Instance == null || !GCDatabase.Instance.IsLoaded)
                return fallbackHPWire;

            var node = GCDatabase.Instance.ResolveWithInheritance(gcType);
            var desc = node?.GetChild("Description") ?? node;
            if (desc == null || !desc.HasProperty("MaxHealth"))
                return fallbackHPWire;

            float maxHealth = desc.GetFloat("MaxHealth", fallbackHPWire / 256f);
            if (maxHealth <= 0f || float.IsNaN(maxHealth) || float.IsInfinity(maxHealth))
                return fallbackHPWire;

            return (uint)Mathf.Max(1, Mathf.RoundToInt(maxHealth * 256f));
        }

        private static bool AuthoredExtendsClass(string gcType, string dfcClass)
        {
            if (string.IsNullOrEmpty(gcType) || string.IsNullOrEmpty(dfcClass) ||
                GCDatabase.Instance == null || !GCDatabase.Instance.IsLoaded)
                return false;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string current = gcType;
            for (int depth = 0; depth < 32 && !string.IsNullOrEmpty(current); depth++)
            {
                if (!seen.Add(current)) return false;
                var node = GCDatabase.Instance.Resolve(current);
                if (node == null) return false;
                if (AuthoredNameMatches(node.Name, dfcClass) || AuthoredNameMatches(node.Extends, dfcClass))
                    return true;
                current = node.Extends;
            }

            return false;
        }

        private static bool AuthoredNameMatches(string authoredName, string dfcClass)
        {
            if (string.IsNullOrEmpty(authoredName)) return false;
            if (string.Equals(authoredName, dfcClass, StringComparison.OrdinalIgnoreCase)) return true;
            int dot = authoredName.LastIndexOf('.');
            return dot >= 0 &&
                string.Equals(authoredName.Substring(dot + 1), dfcClass, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveAuthoredItemClass(string gcType)
        {
            if (AuthoredExtendsClass(gcType, "RangedWeapon")) return "RangedWeapon";
            if (AuthoredExtendsClass(gcType, "MeleeWeapon")) return "MeleeWeapon";
            if (AuthoredExtendsClass(gcType, "ActiveItem")) return "ActiveItem";
            if (AuthoredExtendsClass(gcType, "Armor")) return "Armor";
            if (AuthoredExtendsClass(gcType, "Item")) return "Item";

            string gcLower = (gcType ?? string.Empty).ToLowerInvariant();
            if (gcLower.Contains("ring") || gcLower.Contains("amulet"))
                return "Item";
            if (gcLower.Contains("crossbow") || gcLower.Contains("bow") ||
                gcLower.Contains("gun") || gcLower.Contains("cannon") ||
                gcLower.Contains("ranged"))
                return "RangedWeapon";
            if (gcLower.Contains("sword") || gcLower.Contains("axe") ||
                gcLower.Contains("mace") || gcLower.Contains("dagger") ||
                gcLower.Contains("hammer") || gcLower.Contains("staff") ||
                gcLower.Contains("spear") || gcLower.Contains("pick") ||
                gcLower.Contains("club") || gcLower.Contains("scepter") ||
                gcLower.Contains("wand") || gcLower.Contains("katana") ||
                gcLower.Contains("polearm") || gcLower.Contains("melee"))
                return "MeleeWeapon";
            return "Armor";
        }

        private static void WriteNonCombatInteractiveEntitySynchInfo(LEWriter writer, string gcType = null)
        {
            uint hpWire = ResolveAuthoredUnitMaxHealthWire(gcType);
            writer.WriteByte(0x02);
            writer.WriteUInt32(hpWire);
            Debug.LogError($"[ENTITY-SYNCH-INFO] packet=NCI owner=NonCombatInteractive gc={gcType ?? "<default>"} flags=0x02 hp={hpWire}");
        }

        private bool TryWriteEntitySynchForComponent(RRConnection conn, LEWriter writer, ushort componentId, byte subtype, EntitySynchInfoContext context, string packetName, bool advanceEntitySynchInfo)
        {
            if (advanceEntitySynchInfo && IsAvatarEntitySynchInfoComponentId(conn, componentId))
            {
                GetValidationCutoff(out _, out float validationCutoffTime);
                FlushCombatBeforeSynch(conn, validationCutoffTime);
            }
            if (!ResolveEntitySynchInfoForComponent(conn, componentId, subtype, context, 0, packetName, advanceEntitySynchInfo, out EntitySynchInfoDecision decision))
            {
                Debug.LogError($"[ENTITY-SYNCH-INFO-UNRESOLVED] packet={packetName} context={context} component={componentId} sub=0x{subtype:X2} owner={decision.Owner} reason={decision.Reason}");
                return false;
            }

            return TryWriteResolvedEntitySynchInfo(writer, componentId, subtype, context, packetName, decision, conn);
        }

        private bool TryWriteResolvedEntitySynchInfo(LEWriter writer, ushort componentId, byte subtype, EntitySynchInfoContext context, string packetName, EntitySynchInfoDecision decision, RRConnection conn = null)
        {
            if (!decision.Allow)
            {
                Debug.LogError($"[ENTITY-SYNCH-INFO-BLOCK] packet={packetName} context={context} component={componentId} sub=0x{subtype:X2} owner={decision.Owner} reason={decision.Reason}");
                return false;
            }

            if (decision.Owner == EntitySynchInfoOwner.Avatar && (decision.Flags & 0x02) == 0 && !ShouldKeepPlayerComponentEntitySynchInfoEmpty(context))
            {
                if (conn != null && TryResolvePlayerEntitySynchInfoHP(conn, context, packetName, false, out uint avatarHPWire))
                {
                    decision.Flags |= 0x02;
                    decision.HPWire = avatarHPWire;
                    decision.OwnerEntityId = conn.Avatar != null ? (uint)conn.Avatar.Id : decision.OwnerEntityId;
                    decision.Reason = $"{decision.Reason}; avatar-hp-required";
                    decision.Provenance = string.IsNullOrEmpty(decision.Provenance) ? "avatar-hp-recovered" : decision.Provenance + "; avatar-hp-recovered";
                    decision.HpMutationSource = string.IsNullOrEmpty(decision.HpMutationSource) ? "avatar-hp-required" : decision.HpMutationSource;
                }
                else
                {
                    Debug.LogError($"[ENTITY-SYNCH-INFO-BLOCK] Avatar suffix without HP packet={packetName} context={context} component={componentId} sub=0x{subtype:X2} flags=0x{decision.Flags:X2} reason={decision.Reason}");
                    return false;
                }
            }

            if (decision.Owner == EntitySynchInfoOwner.Monster && (decision.Flags & 0x02) == 0)
            {
                if (!ShouldKeepMonsterComponentEntitySynchInfoEmpty(context, packetName))
                {
                    Debug.LogError($"[ENTITY-SYNCH-INFO-BLOCK] Monster suffix without HP packet={packetName} context={context} component={componentId} sub=0x{subtype:X2} flags=0x{decision.Flags:X2} reason={decision.Reason}");
                    return false;
                }
            }

            new EntitySynchInfoPayload(decision.Flags, decision.HPWire).Write(writer);

            if (VerboseSynchLogging)
            {
                string hpText = (decision.Flags & 0x02) != 0 ? decision.HPWire.ToString() : "none";
                string useTargetState = conn != null && conn.HasActiveUseTarget
                    ? $" useTarget={conn.ActiveUseTargetId} initUsePassed={conn.ActiveUseTargetInitUsePassed} visibleHit={conn.ActiveUseTargetVisibleHit} lastProjectileSeq={conn.ActiveUseTargetLastProjectileSeq} lastImpactTick={conn.ActiveUseTargetLastImpactTick}"
                    : " useTarget=0 initUsePassed=False visibleHit=False lastProjectileSeq=0 lastImpactTick=-1";
                Debug.LogError($"[ENTITY-SYNCH-INFO] packet={packetName} context={context} component={componentId} sub=0x{subtype:X2} owner={decision.Owner} ownerEntity={decision.OwnerEntityId} flags=0x{decision.Flags:X2} hp={hpText} clientNow={decision.Now:F3} cutoffTick={decision.ValidationCutoffTick} cutoffTime={decision.ValidationCutoffTime:F3} runtime='{decision.RuntimeInstanceKey ?? ""}' schedulerTick={decision.SchedulerTick} subentity={decision.SubEntityPhase} rngPos={decision.RngPos} hpMutation='{decision.HpMutationSource ?? ""}' reason={decision.Reason} provenance={decision.Provenance}{useTargetState}");
            }
            return true;
        }

        private bool TryWriteResolvedEntitySynchInfo(LEWriter writer, ushort componentId, byte subtype, EntitySynchInfoContext context, string packetName, uint ownerEntityId, EntitySynchInfoDecision decision)
        {
            return TryWriteResolvedEntitySynchInfo(writer, componentId, subtype, context, packetName, decision);
        }

        private bool TryWriteRemoteAvatarEntitySynchInfo(RRConnection sourceConn, LEWriter writer, ushort componentId, byte subtype, string packetName)
        {
            if (!TryResolvePlayerEntitySynchInfoHP(sourceConn, EntitySynchInfoContext.PlayerActionResponse, packetName, false, out uint hpWire))
            {
                Debug.LogError($"[ENTITY-SYNCH-INFO-UNRESOLVED] packet={packetName} owner=RemoteAvatar reason=player-hp-unresolved");
                return false;
            }

            string source = sourceConn?.LoginName ?? "unknown";
            GetValidationCutoff(out uint validationCutoffTick, out float validationCutoffTime);
            string runtimeKey = sourceConn != null ? GetInstanceZoneKey(sourceConn) : null;
            int rngPos = !string.IsNullOrWhiteSpace(runtimeKey) ? CombatRuntime.Instance.GetRoomRngPosForInstance(runtimeKey) : -1;
            return TryWriteResolvedEntitySynchInfo(writer, componentId, subtype, EntitySynchInfoContext.PlayerActionResponse, packetName, EntitySynchInfoDecision.HP(EntitySynchInfoOwner.Avatar, hpWire, $"{packetName} source={source}", sourceConn?.Avatar != null ? (uint)sourceConn.Avatar.Id : 0u, componentId, subtype, GetCombatNow(), $"remote-avatar; validationCutoffTick={validationCutoffTick} validationCutoffTime={validationCutoffTime:F3}", validationCutoffTick, validationCutoffTime, runtimeKey, sourceConn?.EntitySchedulerMirror?.SchedulerTick ?? 0, sourceConn?.EntitySchedulerMirror?.SubEntityPhase ?? false, rngPos));
        }

        private bool ResolveEntitySynchInfoForComponent(RRConnection conn, ushort componentId, byte subtype, EntitySynchInfoContext context, uint ownerEntityId, string packetName, bool advanceEntitySynchInfo, out EntitySynchInfoDecision decision)
        {
            decision = EntitySynchInfoDecision.Empty(EntitySynchInfoOwner.Unknown, packetName);

            if (conn == null || (componentId == 0 && ownerEntityId == 0))
                return true;

            if (IsNonUnitPlayerComponentId(conn, componentId))
            {
                decision = EntitySynchInfoDecision.Empty(EntitySynchInfoOwner.NonUnit, $"{packetName} non-unit-player-component");
                return true;
            }

            RegisterEntitySynchInfoPlayer(conn);
            Monster monster = ResolveMonsterForComponent(componentId, ownerEntityId);
            if (monster != null)
            {
                EntitySynchInfoAuthority.Instance.RegisterMonster(monster);
                string monsterPacketName = $"{packetName} context={context} cid={componentId} sub=0x{subtype:X2} owner={monster.EntityId}";
                float suffixNow = GetCombatNow();
                CombatRuntime.EntitySynchInfoVisibilityCutoff hpCutoff = CombatRuntime.Instance.GetEntitySynchInfoValidationCutoff(context, monsterPacketName);
                uint validationCutoffTick = hpCutoff.Tick;
                float validationCutoffTime = hpCutoff.Time;
                string runtimeInstanceKey = !string.IsNullOrWhiteSpace(monster.InstanceKey) ? monster.InstanceKey : monster.ZoneName;
                int rngPos = CombatRuntime.Instance.GetRoomRngPosForInstance(runtimeInstanceKey);
                conn.EntitySchedulerMirror.ObserveSuffixCutoff(runtimeInstanceKey, validationCutoffTick, validationCutoffTime, hpCutoff.IncludeSubEntityEffects, hpCutoff.Phase, monsterPacketName);
                FlushMonsterRuntimeBeforeSynch(conn, monster, context, monsterPacketName, validationCutoffTime, suffixNow, validationCutoffTick);
                PrimeMonsterHPBeforeSynch(monster, validationCutoffTime);
                if (ShouldKeepMonsterComponentEntitySynchInfoEmpty(context, packetName))
                {
                    decision = EntitySynchInfoDecision.Empty(EntitySynchInfoOwner.Monster, monsterPacketName);
                    return true;
                }
                CombatRuntime.Instance.TryResolveMonsterEntitySynchInfoHP(monster, context, monsterPacketName, hpCutoff, out uint monsterHPWire, out string monsterHPReason);
                EntitySynchInfoAuthority.Instance.RecordMonsterOutboundHP(monster, monsterHPWire, monsterPacketName);
                string provenance = $"{monsterHPReason}; visibleCutoffTick={validationCutoffTick}; visibleCutoffTime={validationCutoffTime:F3}; cutoffPhase={hpCutoff.Phase}; includeSubEntity={hpCutoff.IncludeSubEntityEffects}; cutoffReason={hpCutoff.Reason}; lastEntity={hpCutoff.LastEntityTick}@{hpCutoff.LastEntityTime:F3}; lastSubEntity={hpCutoff.LastSubEntityTick}@{hpCutoff.LastSubEntityTime:F3}";
                decision = EntitySynchInfoDecision.HP(EntitySynchInfoOwner.Monster, monsterHPWire, $"{monsterPacketName} {monsterHPReason}", monster.EntityId, componentId != 0 ? componentId : monster.BehaviorId, subtype, suffixNow, provenance, validationCutoffTick, validationCutoffTime, runtimeInstanceKey, conn.EntitySchedulerMirror.SchedulerTick, conn.EntitySchedulerMirror.SubEntityPhase, rngPos, monsterHPReason);
                return true;
            }

            if (conn.Avatar != null && !IsZoneSpawnInvulnerabilityBlockingCombat(conn))
                FlushWeaponUseStateBeforeSynch(conn, $"ResolveEntitySynchInfo:{packetName}", false);

            if (!IsAvatarEntitySynchInfoComponentId(conn, componentId))
            {
                decision = EntitySynchInfoDecision.Empty(EntitySynchInfoOwner.Unknown, packetName);
                return true;
            }

            if (!TryResolvePlayerEntitySynchInfoHP(conn, context, packetName, advanceEntitySynchInfo, out uint avatarHPWire))
            {
                var state = GetPlayerState(conn.ConnId.ToString());
                avatarHPWire = state != null ? state.EntitySynchInfoHP : 0;
                Debug.LogError($"[ENTITY-SYNCH-INFO-RECOVER] packet={packetName} owner=Avatar component={componentId} hp={avatarHPWire} reason=avatar-hp-unresolved");
            }

            GetValidationCutoff(out uint avatarCutoffTick, out float avatarCutoffTime);
            string avatarRuntimeKey = GetInstanceZoneKey(conn);
            int avatarRngPos = !string.IsNullOrWhiteSpace(avatarRuntimeKey) ? CombatRuntime.Instance.GetRoomRngPosForInstance(avatarRuntimeKey) : -1;
            decision = EntitySynchInfoDecision.HP(EntitySynchInfoOwner.Avatar, avatarHPWire, packetName, conn.Avatar != null ? (uint)conn.Avatar.Id : 0u, componentId, subtype, GetCombatNow(), $"avatar-hp; validationCutoffTick={avatarCutoffTick} validationCutoffTime={avatarCutoffTime:F3}", avatarCutoffTick, avatarCutoffTime, avatarRuntimeKey, conn.EntitySchedulerMirror.SchedulerTick, conn.EntitySchedulerMirror.SubEntityPhase, avatarRngPos);
            return true;
        }

        private bool TryResolveWriterComponentUpdate(RRConnection conn, LEWriter writer, out ushort componentId, out byte subtype)
        {
            componentId = 0;
            subtype = 0;
            byte[] data = writer?.GetBuffer();
            if (data == null || data.Length < 3) return false;

            bool found = false;
            for (int byteOffset = 0; byteOffset + 2 < data.Length; byteOffset++)
            {
                byte opcode = data[byteOffset];
                if (opcode != 0x35 && opcode != 0x36) continue;
                if (opcode == 0x35 && byteOffset + 3 >= data.Length) continue;
                ushort cid = (ushort)(data[byteOffset + 1] | (data[byteOffset + 2] << 8));
                bool knownPlayerComponent = IsAvatarEntitySynchInfoComponentId(conn, cid)
                    || IsNonUnitPlayerComponentId(conn, cid);
                if (!knownPlayerComponent && ResolveMonsterForComponent(cid, 0) == null)
                    continue;
                componentId = cid;
                subtype = opcode == 0x35 ? data[byteOffset + 3] : (byte)0;
                found = true;
            }

            return found;
        }

        private Monster ResolveMonsterForComponent(uint componentId, uint ownerEntityId)
        {
            if (CombatRuntime.Instance == null) return null;
            Monster monster = null;
            if (componentId != 0)
            {
                monster = CombatRuntime.Instance.GetMonsterByComponent(componentId)
                    ?? CombatRuntime.Instance.GetMonsterByBehaviorId(componentId)
                    ?? CombatRuntime.Instance.GetMonsterBySkillsId(componentId)
                    ?? CombatRuntime.Instance.GetMonsterByManipulatorsId(componentId);
            }
            if (monster == null && ownerEntityId != 0)
                monster = CombatRuntime.Instance.GetMonster(ownerEntityId);
            return monster;
        }

        private static EntitySynchInfoContext EntitySynchInfoContextFromTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return EntitySynchInfoContext.Unknown;
            switch (tag)
            {
                case "WorldInterval": return EntitySynchInfoContext.WorldInterval;
                case "WorldRuntimeBaselineReplay": return EntitySynchInfoContext.BaselineReplay;
                case "WorldRuntimeRecoveryReplay": return EntitySynchInfoContext.RecoveryReplay;
                case "WorldRuntimeRepeatReplay": return EntitySynchInfoContext.RepeatReplay;
                case "WorldRuntimeInventoryReplay": return EntitySynchInfoContext.InventoryReplay;
                case "WorldRuntimeEquipmentReplay": return EntitySynchInfoContext.EquipmentReplay;
                case "WorldLateArmorReplay": return EntitySynchInfoContext.LateArmorReplay;
                case "WorldUnitBehaviorControlGrant": return EntitySynchInfoContext.ControlGrant;
                case "WorldUnitBehaviorControlAck": return EntitySynchInfoContext.ControlAck;
                case "WorldMoverAck": return EntitySynchInfoContext.MoverAck;
                case "PlayerBasicAttackResponse": return EntitySynchInfoContext.PlayerBasicAttackResponse;
            }
            if (tag.StartsWith("MON-ATTACK", StringComparison.Ordinal)) return EntitySynchInfoContext.MonsterAction;
            if (tag.StartsWith("MON-MOVE", StringComparison.Ordinal)) return EntitySynchInfoContext.MonsterMove;
            if (tag.StartsWith("DAMAGE-HP", StringComparison.Ordinal)) return EntitySynchInfoContext.MonsterDamage;
            if (tag.Contains("Inventory")) return EntitySynchInfoContext.InventoryReplay;
            if (tag.Contains("Equip")) return EntitySynchInfoContext.EquipmentReplay;
            return EntitySynchInfoContext.PlayerActionResponse;
        }

        private static bool ShouldKeepPlayerComponentEntitySynchInfoEmpty(EntitySynchInfoContext context)
        {
            return false;
        }

        private static bool ShouldKeepMonsterComponentEntitySynchInfoEmpty(EntitySynchInfoContext context, string packetName = null)
        {
            return false;
        }

        private static bool ShouldKeepPlayerActionLaneAlive(EntitySynchInfoContext context)
        {
            return context == EntitySynchInfoContext.PlayerBasicAttackResponse || context == EntitySynchInfoContext.PlayerActionResponse;
        }

        private static bool CanApplyPlayerHPBeforeSuffix(EntitySynchInfoContext context, string packetName = null)
        {
            return true;
        }

        private static bool CanAdvancePlayerEntitySynchInfoHP(uint playerEntityId)
        {
            if (playerEntityId == 0) return true;
            return !CombatRuntime.Instance.HasPendingClientVisibleMonsterAttack(playerEntityId);
        }

        private void RegisterEntitySynchInfoPlayer(RRConnection conn)
        {
            if (conn?.Avatar == null) return;
            PlayerState state = GetPlayerState(conn.ConnId.ToString());
            if (state == null) return;
            EntitySynchInfoAuthority.Instance.RegisterPlayer(conn, state, (uint)conn.Avatar.Id);
        }

        private void FlushCombatBeforeSynch(RRConnection conn, float clientNowOverride = -1f)
        {
            if (conn?.Avatar == null) return;
            CombatRuntime.Instance.UpdatePlayerPosition((uint)conn.Avatar.Id, conn.PlayerPosX, conn.PlayerPosY);
            float flushNow = clientNowOverride >= 0f ? clientNowOverride : GetCombatNow();
            if (IsZoneSpawnInvulnerabilityBlockingCombat(conn))
            {
                FlushPendingKills();
                return;
            }
            FlushPendingKills();
            FlushWeaponUseStateBeforeSynch(conn, "FlushCombatBeforeSynch", true, flushNow);
            CombatRuntime.Instance.FlushPlayerCombatBeforeSynch((uint)conn.Avatar.Id, 0f, "FlushCombatBeforeSynch", flushNow);
        }

        private void FlushWeaponUseStateBeforeSynch(RRConnection conn, string source, bool flushKillsAfter, float clientNowOverride = -1f)
        {
            if (conn?.Avatar == null) return;
            uint playerEntityId = (uint)conn.Avatar.Id;
            CombatRuntime.Instance.UpdatePlayerPosition(playerEntityId, conn.PlayerPosX, conn.PlayerPosY);
            float flushNow = clientNowOverride >= 0f ? clientNowOverride : GetCombatNow();
            if (ShouldAdvancePlayerActionSliceBeforeAvatarSuffix(conn, playerEntityId))
            {
                conn.LastAvatarPreSuffixActionSliceFrame = Time.frameCount;
                var player = CombatRuntime.Instance.GetPlayer(playerEntityId);
                Debug.LogError($"[PLAYER-ACTION-PRE-SUFFIX] source={source ?? "unknown"} player={conn.LoginName ?? conn.ConnId.ToString()} activeUseTarget={conn.HasActiveUseTarget} activeAttack={player?.HasActiveClientAttack ?? false} target={conn.ActiveUseTargetId} flushNow={flushNow:F3} slice=due-drain");
            }
            string instanceKey = conn != null ? GetInstanceZoneKey(conn) : null;
            Combat.WeaponUseRuntime.Instance.FlushPlayerEntityBeforeSynch(playerEntityId, CombatRuntime.Instance.GetRoomRngForInstance(instanceKey), flushNow, source);
            if (flushKillsAfter)
            {
                DrainWeaponUseStateKills(source ?? "FlushWeaponUseStateBeforeSynch");
                FlushPendingKills();
            }
        }

        private bool ShouldAdvancePlayerActionSliceBeforeAvatarSuffix(RRConnection conn, uint playerEntityId)
        {
            if (conn == null || playerEntityId == 0) return false;
            if (conn.LastAvatarPreSuffixActionSliceFrame == Time.frameCount) return false;
            var player = CombatRuntime.Instance.GetPlayer(playerEntityId);
            return conn.HasActiveUseTarget || (player != null && player.HasActiveClientAttack);
        }

        private bool TryResolvePlayerEntitySynchInfoHP(RRConnection conn, string packetName, bool advanceEntitySynchInfo, out uint hpWire)
        {
            return TryResolvePlayerEntitySynchInfoHP(conn, EntitySynchInfoContextFromTag(packetName), packetName, advanceEntitySynchInfo, out hpWire);
        }

        private bool TryResolvePlayerEntitySynchInfoHP(RRConnection conn, EntitySynchInfoContext context, string packetName, bool advanceEntitySynchInfo, out uint hpWire)
        {
            hpWire = 0;
            if (conn == null) return false;
            RefreshZoneSpawnInvulnerability(conn);
            PlayerState state = GetPlayerState(conn.ConnId.ToString());
            if (state == null) return false;
            uint playerEntityId = conn.Avatar != null ? (uint)conn.Avatar.Id : 0;
            if (playerEntityId != 0)
                EntitySynchInfoAuthority.Instance.RegisterPlayer(conn, state, playerEntityId);
            GetValidationCutoff(out uint validationCutoffTick, out float validationCutoffTime);
            bool canApplyPlayerHP = CanApplyPlayerHPBeforeSuffix(context, packetName);
            bool pendingClientVisibleAttack = false;
            uint runtimeHPWire = state.EntitySynchInfoHP;
            if (canApplyPlayerHP && playerEntityId != 0 && !IsZoneSpawnInvulnerabilityBlockingCombat(conn))
            {
                CombatRuntime.Instance.UpdatePlayerPosition(playerEntityId, conn.PlayerPosX, conn.PlayerPosY);
                FlushPendingKills();
                FlushWeaponUseStateBeforeSynch(conn, $"TryResolvePlayerEntitySynchInfoHP:{packetName}", true, validationCutoffTime);
                CombatRuntime.Instance.FlushPlayerCombatBeforeSynch(playerEntityId, 0f, $"TryResolvePlayerEntitySynchInfoHP:{packetName}", validationCutoffTime);
                if (!CombatRuntime.Instance.FlushPlayerHPRuntimeBeforeSynch(playerEntityId, packetName, out runtimeHPWire, out bool unsafeAttack, validationCutoffTime))
                {
                    Debug.LogError($"[{packetName}] player HP runtime flush incomplete for {conn.LoginName ?? conn.ConnId.ToString()}: serverHP={state.CurrentHPWire / 256f:F2}/{state.MaxHPWire / 256f:F2} entitySynchInfoHP={state.EntitySynchInfoHP / 256f:F2} runtimeHP={runtimeHPWire / 256f:F2} unsafeAttack={unsafeAttack}");
                }
            }
            pendingClientVisibleAttack = playerEntityId != 0 && CombatRuntime.Instance.HasPendingClientVisibleMonsterAttack(playerEntityId);
            bool canAdvanceEntitySynchInfo = advanceEntitySynchInfo && !pendingClientVisibleAttack;
            if (playerEntityId != 0)
            {
                float clientNow = validationCutoffTime;
                var resolve = EntitySynchInfoAuthority.Instance.ResolveOutboundPlayer(conn, state, playerEntityId, context, packetName, canAdvanceEntitySynchInfo, clientNow, out hpWire);
                if (!resolve.AllowPacket)
                {
                    Debug.LogError($"[{packetName}] player EntitySynchInfo HP unresolved by EntitySynchInfoAuthority for {conn.LoginName ?? conn.ConnId.ToString()}: serverHP={state.CurrentHPWire / 256f:F2}/{state.MaxHPWire / 256f:F2} entitySynchInfoHP={state.EntitySynchInfoHP / 256f:F2} lastOutbound={conn.LastOutboundHPWire / 256f:F2} reason={resolve.Reason}");
                    return false;
                }
                if (VerboseSynchLogging && resolve.HasHP && (context == EntitySynchInfoContext.PlayerActionResponse || context == EntitySynchInfoContext.PlayerBasicAttackResponse || hpWire != state.CurrentHPWire))
                    Debug.LogError($"[PLAYER-HP-SUFFIX] packet={packetName} context={context} player={conn.LoginName ?? conn.ConnId.ToString()} currentHP={state.CurrentHPWire} entitySynchInfoHP={state.EntitySynchInfoHP} outboundHP={hpWire} advanceRequested={advanceEntitySynchInfo} canAdvance={canAdvanceEntitySynchInfo} pendingClientVisibleAttack={pendingClientVisibleAttack} runtimeHP={runtimeHPWire} cutoffTick={validationCutoffTick} cutoffTime={validationCutoffTime:F3}");
                return resolve.HasHP;
            }
            if (canAdvanceEntitySynchInfo)
                state.AdvanceEntitySynchInfoHP(validationCutoffTime, packetName);
            hpWire = state.EntitySynchInfoHP;
            return true;
        }

        private uint GetEntitySynchInfoHPValue(RRConnection conn, bool advanceEntitySynchInfo)
        {
            string caller = null;
            string connIdStr = conn.ConnId.ToString();
            bool existed = _playerStates.ContainsKey(connIdStr);
            PlayerState state = GetPlayerState(connIdStr);

            if (VerboseSynchLogging)
            {
                var trace = new System.Diagnostics.StackTrace(1, false);
                caller = trace.GetFrame(0)?.GetMethod()?.Name ?? "unknown";
                Debug.LogWarning($"[ENTITY-SYNCH-INFO-VALUE] Called by {caller}, returning {state.EntitySynchInfoHP}");
            }

            if (VerboseSynchLogging)
            {
                Debug.LogError($"[ENTITY-SYNCH-INFO-DETAIL] conn={conn.ConnId} key='{connIdStr}' existed={existed} entitySynchInfoHP={state.EntitySynchInfoHP} level={state.Level}");
                Debug.LogError($"[ENTITY-SYNCH-INFO-VALUE] Returning EntitySynchInfoHP: {state.EntitySynchInfoHP} CurrentHPWire: {state.CurrentHPWire}");
                if (state.EntitySynchInfoHP == 0)
                {
                    Debug.LogError("[ENTITY-SYNCH-INFO-DETAIL] entitySynchInfoHP=0 listing player states");
                    foreach (var playerStateEntry in _playerStates)
                    {
                        Debug.LogError($"[ENTITY-SYNCH-INFO-DETAIL] key='{playerStateEntry.Key}' entitySynchInfoHP={playerStateEntry.Value.EntitySynchInfoHP} level={playerStateEntry.Value.Level}");
                    }
                }
            }
            return state.EntitySynchInfoHP;
        }



        private static string InvKey(string connId, byte containerId)
            => containerId == 0x0B ? connId : $"{connId}:0x{containerId:X2}";
        private System.Collections.Concurrent.ConcurrentQueue<PendingSpell> _pendingSpells = new System.Collections.Concurrent.ConcurrentQueue<PendingSpell>();
        private long _nextPendingSpellProjectileSequence;

        private struct PendingAoECast
        {
            public long Sequence;
            public RRConnection Conn;
            public PlayerState State;
            public Combat.SpellData Spell;
            public int SkillLevel;
            public string InstanceKey;
            public float CastX;
            public float CastY;
            public float CastZ;
            public float CastHeading;
            public float CastTime;
            public bool CastSourceOffsetResolved;
            public Vector3 CastSourceOffset;
            public float DueTime;
        }
        private readonly List<PendingAoECast> _pendingAoECasts = new List<PendingAoECast>();
        private readonly object _pendingAoECastLock = new object();
        private long _nextPendingAoECastSequence;

        private void ProcessPendingAoECasts(float now)
        {
            List<PendingAoECast> due = null;
            lock (_pendingAoECastLock)
            {
                if (_pendingAoECasts.Count == 0) return;
                for (int i = 0; i < _pendingAoECasts.Count;)
                {
                    var pending = _pendingAoECasts[i];
                    if (now < pending.DueTime) { i++; continue; }
                    _pendingAoECasts.RemoveAt(i);
                    due ??= new List<PendingAoECast>();
                    due.Add(pending);
                }
            }
            if (due == null) return;
            foreach (var pending in due)
            {
                if (pending.Conn == null || !pending.Conn.IsConnected) continue;
                ApplyAoECast(pending);
            }
        }

        private static int ResolveActiveSkillTriggerFrames(Combat.SpellData spell, PlayerState state)
        {
            if (spell != null && Combat.SpellDatabase.TryResolvePlayerAnimationTiming(spell.AnimationId, state, out _, out int trigger) && trigger > 0)
                return trigger;
            if (spell != null && spell.AnimationTriggerFrames > 0)
                return spell.AnimationTriggerFrames;
            return 15;
        }

        private void ApplyAoECast(PendingAoECast pending)
        {
            RRConnection conn = pending.Conn;
            PlayerState state = pending.State;
            Combat.SpellData spell = pending.Spell;
            int skillLevel = pending.SkillLevel;
            float clientEffectTime = pending.DueTime;
            if (conn == null || spell == null) return;
            ResolveSpellSourcePoint(conn, spell, state, out float playerX, out float playerY, out float playerZ, out float heading, out bool sourceOffsetResolved, out Vector3 sourceOffset);
            if (!spell.HasAoEEffect)
            {
                Debug.LogError($"[SPELL-0x52] action=aoeScan spell={spell.DisplayName} state=skipped reason=no-SpellAOEEffect");
                return;
            }
            float range = spell.ResolveAoERadius(skillLevel);
            if (range <= 0f)
            {
                Debug.LogError($"[SPELL-0x52] action=aoeScan spell={spell.DisplayName} state=skipped reason=no-authored-radius");
                return;
            }
            var monstersInRange = Combat.CombatRuntime.Instance.GetMonstersInSpellEffectRange(playerX, playerY, playerZ, range, pending.InstanceKey, clientEffectTime);
            int maxTargets = spell.ResolveNumTargets(skillLevel);
            string targets = monstersInRange != null
                ? string.Join(",", monstersInRange.Take(maxTargets).Select(m => m.EntityId.ToString()))
                : "";
            string detail = BuildSpellEffectTargetDetail(monstersInRange, playerX, playerY, playerZ, clientEffectTime, maxTargets);
            Debug.LogError($"[SPELL-0x52] action=aoeScan count={monstersInRange?.Count ?? 0} maxTargets={maxTargets} targets={targets} range={range} pos=({playerX:F1},{playerY:F1},{playerZ:F1}) cast=({pending.CastX:F1},{pending.CastY:F1},{pending.CastZ:F1}) castTime={pending.CastTime:F3} heading={heading:F1} castHeading={pending.CastHeading:F1} sourceOffset=({sourceOffset.x:F1},{sourceOffset.y:F1},{sourceOffset.z:F1}) castSourceOffset=({pending.CastSourceOffset.x:F1},{pending.CastSourceOffset.y:F1},{pending.CastSourceOffset.z:F1}) sourceOffsetResolved={sourceOffsetResolved} castSourceOffsetResolved={pending.CastSourceOffsetResolved} detail={detail}");

            if (monstersInRange == null) return;
            int hits = 0;
            foreach (var monster in monstersInRange)
            {
                if (!monster.IsAlive) continue;
                if (hits >= maxTargets) break;

                ApplySpellDamageToMonster(conn, state, spell, monster, CombatRuntime.Instance.GetRoomRngForMonster(monster), skillLevel, true, clientEffectTime);
                hits++;
            }
            if (hits > 0)
                Debug.LogError($"[SPELL-0x52] spell={spell.DisplayName} hits={hits} maxTargets={maxTargets} range={range}");
        }

        private static void ResolveSpellSourcePoint(RRConnection conn, Combat.SpellData spell, PlayerState state, out float sourceX, out float sourceY, out float sourceZ, out float heading, out bool sourceOffsetResolved, out Vector3 sourceOffset)
        {
            sourceX = conn != null && conn.HasLivePlayerPosition ? conn.LivePlayerPosX : conn?.PlayerPosX ?? 0f;
            sourceY = conn != null && conn.HasLivePlayerPosition ? conn.LivePlayerPosY : conn?.PlayerPosY ?? 0f;
            sourceZ = ResolvePlayerSourceZ(conn);
            heading = conn != null && conn.HasLivePlayerPosition ? conn.LivePlayerHeading : conn?.PlayerHeading ?? 0f;
            sourceOffset = Vector3.zero;
            sourceOffsetResolved = spell != null && Combat.SpellDatabase.TryResolvePlayerAnimationSourceOffset(spell.AnimationId, state, out sourceOffset);
            if (!sourceOffsetResolved)
            {
                sourceOffset = Vector3.zero;
                return;
            }

            float headingRad = heading * Mathf.Deg2Rad;
            float sin = Mathf.Sin(headingRad);
            float cos = Mathf.Cos(headingRad);
            float rightX = cos;
            float rightY = -sin;
            float forwardX = sin;
            float forwardY = cos;
            sourceX += rightX * sourceOffset.x + forwardX * sourceOffset.y;
            sourceY += rightY * sourceOffset.x + forwardY * sourceOffset.y;
            sourceZ += sourceOffset.z;
        }

        private static float ResolvePlayerSourceZ(RRConnection conn)
        {
            if (conn == null)
                return 0f;
            if (conn.HasLivePlayerPosition && Mathf.Abs(conn.LivePlayerPosZ) > 0.001f)
                return conn.LivePlayerPosZ;
            if (Mathf.Abs(conn.PlayerPosZ) > 0.001f)
                return conn.PlayerPosZ;
            return 0f;
        }

        private static string BuildSpellEffectTargetDetail(List<Combat.Monster> monsters, float sourceX, float sourceY, float sourceZ, float clientEffectTime, int maxTargets)
        {
            if (monsters == null || monsters.Count == 0 || maxTargets <= 0)
                return "";

            var parts = new List<string>();
            int count = 0;
            foreach (Combat.Monster monster in monsters)
            {
                if (monster == null)
                    continue;
                if (count >= maxTargets)
                    break;
                if (!Combat.CombatRuntime.Instance.TryGetMonsterClientUnitPosition(monster, clientEffectTime, out float targetX, out float targetY, out float targetZ))
                    continue;
                float dx = targetX - sourceX;
                float dy = targetY - sourceY;
                float dz = targetZ - sourceZ;
                float dist = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                parts.Add($"{monster.EntityId}:{dist:F1}@({targetX:F1},{targetY:F1},{targetZ:F1})");
                count++;
            }
            return string.Join(",", parts);
        }

        private bool TryFinalizeMonsterKill(RRConnection conn, Combat.Monster monster, string source)
        {
            if (monster == null) return false;

            string killSource = source ?? "unknown";
            Debug.LogError($"[KILL-FINALIZE] {killSource}: {monster.Name} eid={monster.EntityId} conn={(conn != null ? conn.ConnId.ToString() : "null")}");
            _pendingKills.Remove(monster.EntityId);
            if (!_finalizedMonsterKills.Add(monster.EntityId))
            {
                Debug.LogError($"[KILL-DEDUP] {killSource}: {monster.Name} already finalized");
                return false;
            }
            try
            {
                ProcessMonsterKill(conn, monster, killSource);
                return true;
            }
            catch (Exception ex)
            {
                _finalizedMonsterKills.Remove(monster.EntityId);
                Debug.LogError($"[KILL-ERROR] {killSource}: failed to finalize {monster.Name}#{monster.EntityId}: {ex}");
                throw;
            }
        }

        private void FlushPendingKills()
        {
            if (_pendingKills.Count == 0) return;

            var pending = new System.Collections.Generic.List<uint>(_pendingKills.Keys);
            foreach (uint eid in pending)
            {
                if (!_pendingKills.ContainsKey(eid))
                    continue;
                var pendingKill = _pendingKills[eid];
                _pendingKills.Remove(eid);
                if (pendingKill.conn == null || pendingKill.monster == null)
                    continue;
                TryFinalizeMonsterKill(pendingKill.conn, pendingKill.monster, $"{pendingKill.source}-pending-flush");
            }
        }
        private void ClearFinalizedKill(uint entityId)
        {
            _finalizedMonsterKills.Remove(entityId);
        }

        private static bool IsUseTargetingMonster(RRConnection conn, Combat.Monster monster)
        {
            if (conn == null || monster == null || !conn.HasActiveUseTarget)
                return false;

            ushort targetId = conn.ActiveUseTargetId;
            return targetId == (ushort)monster.EntityId ||
                   targetId == (ushort)monster.BehaviorId ||
                   targetId == (ushort)monster.UnitId ||
                   CombatRuntime.Instance.GetMonster(targetId) == monster ||
                   CombatRuntime.Instance.GetMonsterByComponent(targetId) == monster;
        }

        private void ProcessMonsterKill(RRConnection conn, Combat.Monster monster, string source)
        {
            CombatRuntime.Instance.SetMonsterHPWire(monster, 0, true);
            CombatRuntime.Instance.MarkMonsterDead(monster, source);
            _serverKillCount++;
            Debug.LogError($"[KILL] ");
            Debug.LogError($"[KILL]  KILL #{_serverKillCount}: {monster.Name} via [{source}]");
            Debug.LogError($"[KILL] EntityId={monster.EntityId} GCType={monster.GCType} Level={monster.Level}");
            CombatRuntime.Instance.BeginMonsterDeathLifecycle(monster, source);
            if (conn != null)
                ClearUseTargetAndReleaseControl(conn, "ProcessMonsterKill", sendClientControlReset: true, requireActiveUseTargetForReset: false);

            PlayerState playerState = conn != null ? GetPlayerState(conn.ConnId.ToString()) : null;
            if (playerState != null)
            {
                uint packetXP = ResolveMonsterExperienceReward(monster);
                uint sourceLevel = (uint)Math.Max(1, (int)monster.Level);
                uint effectiveXP = ResolveClientVisibleExperienceReward(conn, monster, playerState, packetXP, sourceLevel);
                if (packetXP > 0 && effectiveXP > 0)
                {
                    int oldLevel = playerState.Level;
                    uint oldHPWire = playerState.CurrentHPWire;
                    uint oldMaxHPWire = playerState.MaxHPWire;
                    bool leveled = playerState.AddExperience(effectiveXP);
                    CommitPlayerHPTruth(conn, playerState, leveled ? "LEVEL-UP-XP" : "KILL-XP", playerState.CurrentHPWire, false, false);
                    SendHeroAddExperienceUpdate(conn, packetXP, sourceLevel);
                    SavePlayerLevel(conn);
                    Debug.LogError($"[KILL-XP] {monster.Name}: packetXP={packetXP} effectiveXP={effectiveXP} sourceLevel={sourceLevel} level={oldLevel}->{playerState.Level}{(leveled ? " LEVELUP" : "")} HP={oldHPWire}->{playerState.CurrentHPWire}/{playerState.MaxHPWire} maxHP={oldMaxHPWire}->{playerState.MaxHPWire}");
                }
            }

            if (monster.GCType != null && (
                monster.GCType.Equals("creatures.whiskers.broodling.basic.champion", StringComparison.OrdinalIgnoreCase) ||
                monster.GCType.Equals("world.dungeon00.mob.boss", StringComparison.OrdinalIgnoreCase)))
            {
                Debug.LogError($"[BOSS]  RATTLE TOOTH KILLED! Opening boss gate ");

                if (WorldEntitySpawner.Instance.FindEntityByName("BossGate", monster.ZoneName, out ushort gateId, out var gateData))
                {
                    var gateWriter = new LEWriter();
                    gateWriter.WriteByte(0x03);
                    gateWriter.WriteUInt16(gateId);
                    gateWriter.WriteByte(0x0A);
                    gateWriter.WriteByte(0x00);
                    byte[] gatePacket = gateWriter.ToArray();

                    Debug.LogError($"[BOSS] Gate entity {gateId} (0x{gateId:X4}), monster.ZoneName={monster.ZoneName}, conn.CurrentZoneName={conn.CurrentZoneName}, conn.InstanceId={conn.InstanceId}");

                    int sentCount = 0;
                    foreach (var zoneConn in _connections.Values)
                    {
                        if (!zoneConn.IsConnected || !zoneConn.IsSpawned) continue;
                        if (!string.Equals(zoneConn.CurrentZoneName, monster.ZoneName, StringComparison.OrdinalIgnoreCase)) continue;
                        if (zoneConn.InstanceId != conn.InstanceId) continue;
                        zoneConn.MessageQueue.Enqueue(gatePacket);
                        sentCount++;
                    }

                    if (sentCount == 0)
                    {
                        Debug.LogError($"[BOSS] broadcast matched=0 direct={conn.LoginName}");
                        conn.MessageQueue.Enqueue(gatePacket);
                        sentCount = 1;
                    }

                    Debug.LogError($"[BOSS] Sent gate open (0x03/0x0A flags=0) for entity {gateId} to {sentCount} players");

                    const string bossGateMessage = "The boss gate is open!";
                    var _bossMsgSeen = new System.Collections.Generic.HashSet<int>();
                    int bossMessageSent = 0;

                    if (conn != null && conn.IsConnected)
                    {
                        SendSystemMessage(conn, bossGateMessage);
                        bossMessageSent = 1;
                    }

                    Debug.LogError($"[BOSS] Boss-gate message '{bossGateMessage}' sent to {bossMessageSent} player(s) (zone+instance+group dedup'd)");

                    try
                    {
                        int _popupSent = 0;
                        var _popupSeen = new System.Collections.Generic.HashSet<int>();
                        foreach (var popupConn in _connections.Values)
                        {
                            if (popupConn == null) continue;
                            if (!popupConn.IsConnected || !popupConn.IsSpawned) continue;
                            if (popupConn.QuestManagerId == 0) continue;
                            if (!string.Equals(popupConn.CurrentZoneName, monster.ZoneName, StringComparison.OrdinalIgnoreCase)) continue;
                            if (popupConn.InstanceId != conn.InstanceId) continue;
                            if (!_popupSeen.Add(popupConn.ConnId)) continue;
                            SendBossGatePopup(popupConn);
                            _popupSent++;
                        }
                        if (conn != null && conn.IsConnected && conn.QuestManagerId != 0 && _popupSeen.Add(conn.ConnId))
                        {
                            SendBossGatePopup(conn);
                            _popupSent++;
                        }
                        Debug.LogError($"[BOSS-POPUP] Triggered popup sequence for {_popupSent} player(s)");
                    }
                    catch (Exception _popupEx)
                    {
                        Debug.LogError($"[BOSS-POPUP] FAILED (non-fatal): {_popupEx.Message}");
                    }
                }
                else
                {
                    Debug.LogError($"[BOSS] bossGate missing zone={monster.ZoneName}");
                }
            }

            var candidateGcTypes = new System.Collections.Generic.List<string>();
            if (monster.AuthoredArchetypeAncestry != null)
            {
                foreach (string ancestryPath in monster.AuthoredArchetypeAncestry)
                {
                    if (!string.IsNullOrWhiteSpace(ancestryPath) &&
                        !candidateGcTypes.Contains(ancestryPath, StringComparer.OrdinalIgnoreCase))
                        candidateGcTypes.Add(ancestryPath);
                }
            }
            if (!string.IsNullOrEmpty(monster.SpawnGCType))
                if (!candidateGcTypes.Contains(monster.SpawnGCType, StringComparer.OrdinalIgnoreCase))
                    candidateGcTypes.Add(monster.SpawnGCType);
            if (!string.IsNullOrEmpty(monster.GCType))
                if (!candidateGcTypes.Contains(monster.GCType, StringComparer.OrdinalIgnoreCase))
                    candidateGcTypes.Add(monster.GCType);
            var questUpdates = QuestManager.Instance.OnCreatureKilled(conn, candidateGcTypes);
            if (questUpdates != null && questUpdates.Count > 0)
            {
                Debug.LogError($"[KILL] Quest objectives updated: {questUpdates.Count}");
                SavePlayerQuests(conn);
            }
            if (playerState != null)
            {
                SavePlayerLevel(conn);
                Debug.LogError($"[KILL] Saved XP={playerState.Experience} Level={playerState.Level}");
            }

            try
            {
                int pLevel = playerState?.Level ?? 1;
                List<LootDrop> drops;

                if (WorldObjectSpawner.IsDestroyableObject(monster.GCType))
                    drops = GCObjectGeneratorTable.Instance.GenerateDestroyableLoot(monster, pLevel, conn != null && !IsPlayerFree(conn.LoginName));
                else
                    drops = GCObjectGeneratorTable.Instance.GenerateMobLoot(monster, pLevel, conn != null && !IsPlayerFree(conn.LoginName));

                UpgradePotionsForMembers(drops, conn);

                foreach (var drop in drops)
                {
                    if (drop.IsGold)
                    {
                        uint lootGold = (uint)drop.GoldAmount;
                        Debug.LogError($"[LOOT] +{lootGold} gold pile from {monster.Name}");
                        try
                        {
                            var goldPlacement = ResolveItemDropPlacement(
                                conn,
                                conn.CurrentZoneName,
                                conn.InstanceId,
                                monster.PosX,
                                monster.PosY,
                                monster.PosZ,
                                monster.Heading,
                                $"mob-gold:{monster.Name}#{monster.EntityId}");
                            float gpx = goldPlacement.X;
                            float gpy = goldPlacement.Y;
                            float gpz = goldPlacement.Z;
                            ushort goldEntityId = GetNextLootEntityId();

                            var goldInfo = new DroppedItemInfo
                            {
                                Item = null,
                                DbId = 0,
                                Zone = conn.CurrentZoneName ?? "",
                                ZoneId = conn.CurrentZoneId,
                                InstanceId = conn.InstanceId,
                                PosX = gpx,
                                PosY = gpy,
                                PosZ = gpz,
                                PlayerLevel = playerState?.Level ?? 1,
                                DroppedBy = conn.LoginName ?? "",
                                IsGoldDrop = true,
                                GoldAmount = lootGold
                            };
                            _droppedItems[goldEntityId] = goldInfo;
                            BroadcastGoldPileSpawnPacket(conn, goldEntityId, gpx, gpy, gpz, lootGold);
                        }
                        catch (Exception goldLootEx)
                        {
                            Debug.LogError($"[LOOT]  gold pile spawn failed: {goldLootEx.Message}");
                        }
                    }
                    else if (drop.IsKingsCoin)
                    {
                        try
                        {
                            int kcCount = drop.KingsCoinCount > 0 ? drop.KingsCoinCount : 1;
                            for (int kcIdx = 0; kcIdx < kcCount; kcIdx++)
                            {
                                var kcItem = new GCObject
                                {
                                    GCClass = "QuestItemPAL.Token",
                                    DFCClass = "Item",
                                    PresetScaleMod = "ScaleModPAL.Binder.Mod1",
                                    StoredRarity = (int)ItemRarity.Normal,
                                    StoredLevel = pLevel
                                };
                                var kcPlacement = ResolveItemDropPlacement(
                                    conn,
                                    conn.CurrentZoneName,
                                    conn.InstanceId,
                                    monster.PosX,
                                    monster.PosY,
                                    monster.PosZ,
                                    monster.Heading,
                                    $"mob-kingscoin:{monster.Name}#{monster.EntityId}:{kcIdx}");
                                float kcPx = kcPlacement.X;
                                float kcPy = kcPlacement.Y;
                                float kcPz = kcPlacement.Z;
                                ushort kcEntityId = GetNextLootEntityId();
                                TrackDroppedItem(kcEntityId, kcItem, conn, 1, kcPx, kcPy, kcPz, pLevel);
                                BroadcastDroppedItemSpawnPacket(conn, kcEntityId, _droppedItems[kcEntityId]);
                            }
                            Debug.LogError($"[LOOT-KC] dropped {kcCount} Kings Coin(s) on ground from {monster.Name}");
                        }
                        catch (Exception kcEx)
                        {
                            Debug.LogError($"[LOOT-KC] ground drop failed (non-fatal): {kcEx.Message}");
                        }
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(drop.GCType)) { Debug.LogError($"[LOOT]  Skipping null GCType from {monster.Name}"); continue; }
                        string _detectedClass = ResolveAuthoredItemClass(drop.GCType);

                        var item = new GCObject
                        {
                            GCClass = drop.GCType,
                            DFCClass = _detectedClass,
                            PresetScaleMod = drop.ScaleMod,
                            StoredRarity = (int)drop.Rarity,
                            StoredLevel = drop.ItemLevel
                        };
                        var itemPlacement = ResolveItemDropPlacement(
                            conn,
                            conn.CurrentZoneName,
                            conn.InstanceId,
                            monster.PosX,
                            monster.PosY,
                            monster.PosZ,
                            monster.Heading,
                            $"mob-item:{monster.Name}#{monster.EntityId}:{drop.GCType}");
                        float dropX = itemPlacement.X;
                        float dropY = itemPlacement.Y;
                        float dropZ = itemPlacement.Z;

                        ushort lootEntityId = GetNextLootEntityId();
                        TrackDroppedItem(lootEntityId, item, conn, 1, dropX, dropY, dropZ, pLevel);

                        BroadcastDroppedItemSpawnPacket(conn, lootEntityId, _droppedItems[lootEntityId]);
                        Debug.LogError($"[LOOT]  {drop.Label} ({drop.Rarity}) at ({dropX:F0},{dropY:F0},{dropZ:F0})");
                    }
                }

                if (drops.Count > 0)
                    Debug.LogError($"[LOOT] {monster.Name}: {drops.Count} drops");
            }
            catch (Exception lootEx)
            {
                Debug.LogError($"[LOOT] state=failed message='{lootEx.Message}' stack='{lootEx.StackTrace}'");
            }

            try
            {
                var questState = QuestManager.Instance.GetPlayerState(conn.ConnId.ToString());
                if (questState != null && questState.ActiveQuests != null && questState.ActiveQuests.Count > 0)
                {
                    var questDropCandidates = new System.Collections.Generic.List<string>();
                    if (monster.AuthoredArchetypeAncestry != null)
                    {
                        foreach (string ancestryPath in monster.AuthoredArchetypeAncestry)
                        {
                            if (!string.IsNullOrWhiteSpace(ancestryPath) &&
                                !questDropCandidates.Contains(ancestryPath, StringComparer.OrdinalIgnoreCase))
                                questDropCandidates.Add(ancestryPath);
                        }
                    }
                    if (!string.IsNullOrEmpty(monster.SpawnGCType))
                        if (!questDropCandidates.Contains(monster.SpawnGCType, StringComparer.OrdinalIgnoreCase))
                            questDropCandidates.Add(monster.SpawnGCType);
                    if (!string.IsNullOrEmpty(monster.GCType))
                        if (!questDropCandidates.Contains(monster.GCType, StringComparer.OrdinalIgnoreCase))
                            questDropCandidates.Add(monster.GCType);

                    var activeIds = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var activeQuest in questState.ActiveQuests)
                        if (!string.IsNullOrEmpty(activeQuest.QuestId)) activeIds.Add(activeQuest.QuestId);

                    var creditedItemTypes = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    int questDropRolls = 0, questDropHits = 0;
                    foreach (var candidateGcType in questDropCandidates)
                    {
                        if (string.IsNullOrEmpty(candidateGcType)) continue;
                        if (!DatabaseLoader.QuestKillDropsByMonster.TryGetValue(candidateGcType, out var rules)) continue;

                        foreach (var rule in rules)
                        {
                            if (!activeIds.Contains(rule.QuestId)) continue;
                            if (creditedItemTypes.Contains(rule.ItemGcType)) continue;
                            if (rule.Chance < 1) continue;

                            questDropRolls++;
                            uint killDropRaw = RandomStreams.GenerateGlobalStatic(
                                "KillDropTrigger::doEvent.server-chance",
                                $"quest={rule.QuestId}:item={rule.ItemGcType}:monster={monster.Name}#{monster.EntityId}");
                            int killDropRoll = (int)(killDropRaw % (uint)rule.Chance);
                            Debug.LogError($"[KILLDROP-CLIENT] quest={rule.QuestId} item={rule.ItemGcType} monster={monster.Name}#{monster.EntityId} event=0x20 chanceDenom={rule.Chance} raw=0x{killDropRaw:X8} roll={killDropRoll} stream=globalStatic sourceFunction=KillDropTrigger::doEvent@0x005CACB0 KillDropTrigger::doEvent.server-chance RandomStreams.GenerateGlobalStatic");
                            if (killDropRoll != 0) continue;

                            creditedItemTypes.Add(rule.ItemGcType);
                            questDropHits++;
                            Debug.LogError($"[QUEST-DROP] item={rule.ItemGcType} player={conn.LoginName} quest={rule.QuestId} chance=1/{rule.Chance}");
                            var questDropItem = new GCObject
                            {
                                GCClass = rule.ItemGcType,
                                DFCClass = "Item",
                                StoredLevel = 1
                            };
                            var questDropPlacement = ResolveItemDropPlacement(
                                conn,
                                conn.CurrentZoneName,
                                conn.InstanceId,
                                monster.PosX,
                                monster.PosY,
                                monster.PosZ,
                                monster.Heading,
                                $"killdrop-item:{monster.Name}#{monster.EntityId}:{rule.ItemGcType}");
                            float questDropX = questDropPlacement.X;
                            float questDropY = questDropPlacement.Y;
                            float questDropZ = questDropPlacement.Z;
                            ushort questDropEntityId = GetNextLootEntityId();
                            TrackDroppedItem(questDropEntityId, questDropItem, conn, 1, questDropX, questDropY, questDropZ, 1);
                            if (_droppedItems.TryGetValue(questDropEntityId, out var questDropInfo))
                                questDropInfo.IsQuestItem = true;
                            BroadcastDroppedItemSpawnPacket(conn, questDropEntityId, _droppedItems[questDropEntityId]);
                        }
                    }
                    if (questDropRolls > 0)
                        Debug.LogError($"[QUEST-DROP] monster='{monster.Name}' rolled={questDropRolls} credited={questDropHits}");
                }
            }
            catch (Exception questDropEx)
            {
                Debug.LogError($"[QUEST-DROP] state=failed message='{questDropEx.Message}' stack='{questDropEx.StackTrace}'");
            }

            Debug.LogError($"[KILL] ");
        }

        private void SendHeroAddExperienceUpdate(RRConnection conn, uint baseXP, uint sourceLevel)
        {
            uint avatarId = GetPlayerAvatarId(conn.LoginName);
            if (avatarId == 0) return;

            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x03);
            writer.WriteUInt16((ushort)avatarId);
            writer.WriteByte(0x0F);
            writer.WriteUInt32(baseXP);
            writer.WriteByte((byte)sourceLevel);
            WritePlayerEntitySynch(conn, writer);
            writer.WriteByte(0x06);

            SendCompressedA(conn, 0x01, 0x0F, writer.ToArray());
            Debug.LogError($"[XP-PACKET] Sent 0x03 via CompressedA: baseXP={baseXP} sourceLevel={sourceLevel}");
        }

        public void SendAdminXPUpdate(RRConnection conn, uint baseXP, uint sourceLevel)
        {
            SendHeroAddExperienceUpdate(conn, baseXP, sourceLevel);
        }

        public void SendFreePlayerModifier(RRConnection conn)
        {
            if (conn.ModifiersId == 0)
            {
                Debug.LogError("[XP-MOD] Cannot send FreePlayerModifier - ModifiersId not set");
                return;
            }

            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x35);
            writer.WriteUInt16((ushort)conn.ModifiersId);
            writer.WriteByte(0x00);
            WriteGCType(writer, "avatar.base.FreePlayerExperienceModifier", preserveCase: true);
            writer.WriteUInt32(1);
            writer.WriteByte(0);
            writer.WriteUInt32(0);
            writer.WriteUInt32(0x00000000);
            writer.WriteByte(0x01);
            WritePlayerEntitySynch(conn, writer);
            writer.WriteByte(0x06);

            SendCompressedA(conn, 0x01, 0x0F, writer.ToArray());
            Debug.LogError($"[XP-MOD] Sent FreePlayerModifier (single) modifiersId={conn.ModifiersId}");
        }

        private System.Collections.IEnumerator SendDelayedFreePlayerModifier(RRConnection conn, float delay)
        {
            yield return new DungeonRunners.Engine.WaitForSeconds(delay);
            if (!_freePlayerModifierSent.Contains(conn.LoginName + "_sent"))
            {
                _freePlayerModifierSent.Add(conn.LoginName + "_sent");
                SendFreePlayerModifier(conn);
            }
        }

        private void SendTrackedModifier(RRConnection conn, ActiveModifier mod)
        {
            if (conn.ModifiersId == 0)
            {
                Debug.LogError($"[MOD-RESEND] Cannot send '{mod.GCType}' - ModifiersId not set");
                return;
            }

            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x35);
            writer.WriteUInt16((ushort)conn.ModifiersId);
            writer.WriteByte(0x00);
            WriteGCType(writer, mod.GCType, preserveCase: true);
            writer.WriteUInt32(mod.Id);
            writer.WriteByte(mod.Level);
            writer.WriteUInt32(mod.PowerLevel);
            writer.WriteUInt32(mod.Duration);
            writer.WriteByte(mod.SourceIsSelf);
            WritePlayerEntitySynch(conn, writer);
            writer.WriteByte(0x06);

            SendCompressedA(conn, 0x01, 0x0F, writer.ToArray());
            Debug.LogError($"[MOD-RESEND] Sent '{mod.GCType}' id={mod.Id} dur={mod.Duration} ticks ({mod.Duration * 24.0 / 1000.0:F1}s) to {conn.LoginName}");
        }

        public void RecordModifierSent(string loginName, string gcType, uint modId,
            byte level = 0, uint powerLevel = 0, uint duration = 0, byte sourceIsSelf = 0)
        {
            _activeModifiers.AddOrReplace(loginName, new ActiveModifier
            {
                GCType = gcType,
                Id = modId,
                Level = level,
                PowerLevel = powerLevel,
                Duration = duration,
                SourceIsSelf = sourceIsSelf,
                AddedAt = DateTime.UtcNow
            });
        }

        public void UntrackModifier(string loginName, string gcType)
        {
            _activeModifiers.Remove(loginName, gcType);
        }

        private void ResendAllModifiers(RRConnection conn)
        {
            if (conn.LoginName == null || conn.ModifiersId == 0) return;

            var mods = _activeModifiers.ListFor(conn.LoginName);
            if (mods.Count == 0) return;

            Debug.LogError($"[MOD-RESEND] Re-sending {mods.Count} modifiers for {conn.LoginName} after zone transition");
            foreach (var mod in mods)
            {
                SendTrackedModifier(conn, mod);
            }
        }

        private List<(string modGcType, uint durationTicks)> ResolveMonsterDebuffs(Combat.Monster monster)
        {
            var result = new List<(string, uint)>();
            if (monster?.Manipulators == null) return result;

            foreach (var manipulatorEntry in monster.Manipulators)
            {
                string gcType = !string.IsNullOrEmpty(manipulatorEntry.Value?.gcType) ? manipulatorEntry.Value.gcType : manipulatorEntry.Key;
                if (string.IsNullOrEmpty(gcType)) continue;

                string shortName = gcType.ToLowerInvariant();
                int dot = shortName.LastIndexOf('.');
                if (dot >= 0) shortName = shortName.Substring(dot + 1);

                string debuffKey = null;
                if (_weaponDebuffMap.TryGetValue(shortName, out var wk)) debuffKey = wk;
                else if (_creatureDebuffMap.ContainsKey(shortName)) debuffKey = shortName;

                if (debuffKey == null) continue;
                if (!_creatureDebuffMap.TryGetValue(debuffKey, out var debuffInfo)) continue;

                uint ticks = debuffInfo.durationSec <= 0 ? 0
                    : (uint)(debuffInfo.durationSec * (1000.0 / 24.0));
                result.Add((debuffInfo.modGcType, ticks));
            }
            return result;
        }

        private void ApplyMonsterDebuffs(RRConnection conn, Combat.Monster monster)
        {
            if (conn.ModifiersId == 0 || conn.LoginName == null) return;

            var debuffs = ResolveMonsterDebuffs(monster);
            if (debuffs.Count == 0) return;
            Debug.LogError($"[MON-DEBUFF] skipped state-message debuff lane player={conn.LoginName} monster={monster?.Name ?? "unknown"}#{monster?.EntityId ?? 0} count={debuffs.Count} sourceFunction=ActiveSkill::doSkillEffect@0x00539630/SpellModEffect::doEffect@0x00554460 reason=not-owned-by-StateMachine-message");
        }


        private void SendHeroRemoveExperienceUpdate(RRConnection conn, uint xpAmount)
        {
            uint avatarId = GetPlayerAvatarId(conn.LoginName);
            if (avatarId == 0) return;

            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x03);
            writer.WriteUInt16((ushort)avatarId);
            writer.WriteByte(0x10);
            writer.WriteUInt32(xpAmount);
            WritePlayerEntitySynch(conn, writer);
            writer.WriteByte(0x06);

            SendCompressedA(conn, 0x01, 0x0F, writer.ToArray());
            Debug.LogError($"[XP-PACKET] Sent RemoveExperience: {xpAmount}");
        }

        public void SendAdminEntitySynchInfoHP(RRConnection conn, PlayerState playerState)
        {
            if (conn.UnitBehaviorId == 0) return;

            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x35);
            writer.WriteUInt16((ushort)conn.UnitBehaviorId);
            writer.WriteByte(0x65);
            writer.WriteByte(conn.SessionID++);
            writer.WriteByte(0x01);
            writer.WriteByte(0x03);
            writer.WriteInt32((int)(conn.PlayerHeading * 256));
            writer.WriteInt32((int)(conn.PlayerPosX * 256));
            writer.WriteInt32((int)(conn.PlayerPosY * 256));
            WritePlayerEntitySynch(conn, writer);
            writer.WriteByte(0x06);
            SendToClient(conn, writer.ToArray());
            Debug.LogError($"[ADMIN-HP] Sent EntitySynchInfo HP via UnitBehavior: {playerState.CurrentHPWire / 256}/{playerState.MaxHPWire / 256}");
        }

        private void HandleAdminLevelUp(RRConnection conn, PlayerState playerState, int oldLevel, int newLevel)
        {
            if (!IsPlayerAdmin(conn.LoginName))
            {
                SendSystemMessage(conn, "You do not have permission.");
                return;
            }

            if (conn.Avatar != null)
                CalculateEquipmentBonuses(conn.ConnId.ToString(), conn.Avatar);
            playerState.RestoreToFull();

            for (int lv = oldLevel; lv < newLevel; lv++)
            {
                uint threshold = PlayerState.GetClientThreshold(lv + 1);
                uint packetXP = threshold * 256 / 5 + 100;
                SendAdminXPUpdate(conn, packetXP, (uint)lv);
                Debug.LogError($"[ADMIN-LEVELUP] Level {lv}->{lv + 1}: threshold={threshold} packetXP={packetXP}");
            }

            SendAdminEntitySynchInfoHP(conn, playerState);
            Debug.LogError($"[ADMIN-LEVELUP] Sent {newLevel - oldLevel} XP packet(s) + EntitySynchInfo HP for level {oldLevel}->{newLevel}");
            try { if (conn.CharSqlId != 0) PosseRuntime.Instance.NotifyMemberStateChange(conn.CharSqlId, this); }
            catch (Exception posseException) { Debug.LogError($"[POSSE] level-up notify failed: {posseException.Message}"); }
        }


        private void DrainAvatarAggroSamples()
        {
            foreach (var conn in _connections.Values)
            {
                if (conn == null) continue;
                if (conn.AvatarAggroSampleQueue.Count > 0)
                {
                    int advance = conn.AvatarAggroSampleQueue.Count > AvatarAggroSampleCatchUp ? conn.AvatarAggroSampleQueue.Count - AvatarAggroSampleCatchUp + 1 : 1;
                    for (int i = 0; i < advance; i++)
                    {
                        var (x, y) = conn.AvatarAggroSampleQueue.Dequeue();
                        conn.AggroSamplePosX = x;
                        conn.AggroSamplePosY = y;
                    }
                }
                uint avatarId = GetPlayerAvatarId(conn.LoginName);
                if (avatarId != 0)
                    CombatRuntime.Instance.UpdatePlayerPosition(avatarId, conn.AggroSamplePosX, conn.AggroSamplePosY);
            }
        }

        private void HandleClientMove(RRConnection conn, LEReader reader, ushort componentId)
        {
            try
            {
                byte sessionId = reader.ReadByte();
                byte moveCount = reader.ReadByte();

                float lastX = 0, lastY = 0, lastHeading = 0;
                float previousX = conn.PlayerPosX;
                float previousY = conn.PlayerPosY;
                int rawStartPos = reader.Position;
                for (int moveIndex = 0; moveIndex < moveCount; moveIndex++)
                {
                    byte moveType = reader.ReadByte();
                    int heading = reader.ReadInt32();
                    int posX = reader.ReadInt32();
                    int posY = reader.ReadInt32();

                    lastX = posX / 256.0f;
                    lastY = posY / 256.0f;
                    lastHeading = heading / 256.0f;
                    conn.AvatarAggroSampleQueue.Enqueue((lastX, lastY));
                }
                int rawEndPos = reader.Position;
                TryConsumeClientEntitySynchInfoSuffix(conn, reader, "MOVE-ENTITY-SYNCH-INFO");

                if (moveCount > 0)
                {
                    if (conn.UseTargetMovingActive)
                    {
                        if (TryResolveUseTargetMovingTarget(conn, out float moveTargetX, out float moveTargetY, out string lostReason))
                        {
                            float beforeDx = moveTargetX - previousX;
                            float beforeDy = moveTargetY - previousY;
                            float afterDx = moveTargetX - lastX;
                            float afterDy = moveTargetY - lastY;
                            float beforeSq = beforeDx * beforeDx + beforeDy * beforeDy;
                            float afterSq = afterDx * afterDx + afterDy * afterDy;
                            if (afterSq > beforeSq + 4f)
                                CancelUseTargetMoving(conn, "client-move");
                        }
                        else
                        {
                            CancelUseTargetMoving(conn, lostReason ?? "target-lost");
                        }
                    }
                    bool positionChanged = !conn.HasLivePlayerPosition || Math.Abs(lastX - previousX) > 0.001f || Math.Abs(lastY - previousY) > 0.001f;
                    if (positionChanged && IsZoneSpawnInvulnerabilityBlockingCombat(conn))
                        ClearZoneSpawnInvulnerability(conn, "MOVE");
                    conn.PlayerPosX = lastX;
                    conn.PlayerPosY = lastY;
                    conn.PlayerHeading = lastHeading;
                    conn.HasLivePlayerPosition = true;
                    conn.LivePlayerPosX = lastX;
                    conn.LivePlayerPosY = lastY;
                    conn.LivePlayerPosZ = conn.PlayerPosZ;
                    conn.LivePlayerHeading = lastHeading;
                    conn.LivePlayerPositionTime = Time.time;

                    CheckGotoProximity(conn);
                    CheckPendingMerchantActivation(conn);
                }
                conn.SessionID = sessionId;

                byte[] rawMoveData = reader.GetRawBytes(rawStartPos, rawEndPos - rawStartPos);
                SendLocalPlayerMovementAck(conn, sessionId, moveCount, rawMoveData);
                RelayMoveUpdate(conn, sessionId, moveCount, rawMoveData);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MOVE] state=failed message='{ex.Message}'");
            }
        }

        private const int UnitMoverUpdateRecordSize = 13;

        private void RelayMoveUpdate(RRConnection conn, byte sessionId, byte moveCount, byte[] rawMoveData)
        {
            if (conn == null || moveCount == 0 || rawMoveData == null) return;
            if (!TryNormalizeUnitMoverUpdateData(moveCount, rawMoveData, out byte safeMoveCount, out byte[] safeMoveData)) return;
            BroadcastPlayerMovement(conn, sessionId, safeMoveCount, safeMoveData);
        }


        private static float Distance2D(float ax, float ay, float bx, float by)
        {
            float dx = bx - ax;
            float dy = by - ay;
            return (float)Math.Sqrt((dx * dx) + (dy * dy));
        }

        private uint ResolveMonsterExperienceReward(Combat.Monster monster)
        {
            if (monster == null) return 0;
            double difficulty = monster.ExperienceDifficulty;
            if (difficulty <= 0d) return 0;
            int sourceLevel = Math.Max(1, (int)monster.Level);
            double reward = PlayerState.GetBaseXPForLevel(sourceLevel) * difficulty;
            if (reward < 1d) reward = 1d;
            if (reward >= uint.MaxValue) return uint.MaxValue;
            return (uint)Math.Round(reward, MidpointRounding.AwayFromZero);
        }

        private uint ResolveClientVisibleExperienceReward(RRConnection conn, Combat.Monster monster, PlayerState playerState, uint packetXP, uint sourceLevel)
        {
            if (monster == null || playerState == null || packetXP == 0) return 0;
            int playerLevel = Math.Max(1, playerState.Level);
            int clientSourceLevel = Math.Max(1, (int)Math.Min(255u, sourceLevel));
            if (clientSourceLevel <= playerLevel - 5)
                return 0;

            int effectiveLevel = Math.Min(clientSourceLevel, playerLevel);
            long numerator = (long)(effectiveLevel << 8) << 8;
            int denominator = playerLevel << 8;
            int ratioF32 = denominator != 0 ? (int)(numerator / denominator) : 0;
            long xpF32 = ((long)packetXP * ratioF32) >> 8;

            bool isFree = conn != null && !string.IsNullOrEmpty(conn.LoginName) && IsPlayerFree(conn.LoginName);
            if (isFree)
            {
                float freePlayerXPMult = GCDatabase.Instance.GetKnob("FreePlayerExperienceMult", 0.87f);
                int freeMultF32 = (int)(freePlayerXPMult * 256.0f);
                xpF32 = (xpF32 * freeMultF32) >> 8;
            }

            long scaledXPBase = (xpF32 * 100L) >> 8;
            float experienceMod = GCDatabase.Instance.GetKnob("ExperienceMod", 5.0f);
            int experienceModPercent = (int)(experienceMod * 100.0f);
            long finalXP = scaledXPBase * experienceModPercent / 100L;

            if (finalXP <= 0) return 0;
            if (finalXP >= uint.MaxValue) return uint.MaxValue;
            uint result = (uint)finalXP;
            Debug.LogError($"[XP-CLIENT] monster={monster.Name}#{monster.EntityId} packetXP={packetXP} difficulty={monster.ExperienceDifficulty:F2} sourceLevel={clientSourceLevel} playerLevel={playerLevel} effectiveLevel={effectiveLevel} ratioF32={ratioF32} free={isFree} xpF32={xpF32} effectiveXP={result}");
            return result;
        }

        private void SendLocalPlayerMovementAck(RRConnection conn, byte sessionId, byte moveCount, byte[] rawMoveData)
        {
            if (conn == null || !conn.IsConnected || conn.UnitBehaviorId == 0) return;
            if (!TryNormalizeUnitMoverUpdateData(moveCount, rawMoveData, out byte safeMoveCount, out byte[] safeMoveData)) return;
            var writer = new LEWriter();
            writer.WriteByte(0x35);
            writer.WriteUInt16((ushort)conn.UnitBehaviorId);
            writer.WriteByte(0x65);
            writer.WriteByte(sessionId);
            writer.WriteByte(safeMoveCount);
            writer.WriteBytes(safeMoveData);
            if (!TryWriteEntitySynchForComponent(conn, writer, (ushort)conn.UnitBehaviorId, 0x65, EntitySynchInfoContext.MoverAck, "WorldMoverAck", true))
                return;
            conn.MessageQueue.Enqueue(writer.ToArray());
        }

        private static bool TryNormalizeUnitMoverUpdateData(byte moveCount, byte[] rawMoveData, out byte safeMoveCount, out byte[] safeMoveData)
        {
            const int recordSize = 13;
            safeMoveCount = 0;
            safeMoveData = Array.Empty<byte>();
            if (moveCount == 0 || rawMoveData == null || rawMoveData.Length == 0) return false;
            int availableCount = Math.Min(moveCount, rawMoveData.Length / recordSize);
            if (availableCount <= 0) return false;
            int safeBytes = availableCount * recordSize;
            safeMoveCount = (byte)availableCount;
            if (safeBytes == rawMoveData.Length)
            {
                safeMoveData = rawMoveData;
                return true;
            }
            safeMoveData = new byte[safeBytes];
            Buffer.BlockCopy(rawMoveData, 0, safeMoveData, 0, safeBytes);
            return true;
        }

        private void HandleComponentUpdate(RRConnection conn, LEReader reader)
        {
            try
            {
                if (reader.Remaining < 3)
                {
                    Debug.LogError($"[COMPONENT] state=short remaining={reader.Remaining}");
                    return;
                }
                ushort componentId = reader.ReadUInt16();
                byte subMessage = reader.ReadByte();

                if (VerbosePacketLogging && subMessage >= 0x30 && subMessage <= 0x3F)
                {
                    byte[] detailRaw = reader.PeekRemaining();
                    string detailHex = detailRaw.Length > 0 ? BitConverter.ToString(detailRaw) : "(empty)";
                    Debug.LogError($"[DETAIL-0x3x] cid=0x{componentId:X4} sub=0x{subMessage:X2} remaining={detailRaw.Length} hex={detailHex}");
                }
                if (subMessage == 0x64)
                {
                    if (componentId >= 50000 && componentId < 60000)
                    {
                        HandleMonsterStateMachineUpdate(conn, reader, componentId);
                        return;
                    }

                    bool componentIsBlingGnome = EntitySynchInfoAuthority.Instance.IsBlingGnomeComponent(componentId);

                    if (reader.Remaining >= 1)
                    {
                        byte flags = reader.ReadByte();
                        ushort messageType = 0xFFFF;
                        ushort scope = 0xFFFF;
                        ushort target = 0;
                        uint value = 0;

                        if ((flags & 0x02) != 0 && reader.Remaining >= 2)
                            messageType = reader.ReadUInt16();
                        if ((flags & 0x04) != 0 && reader.Remaining >= 2)
                            scope = reader.ReadUInt16();
                        if ((flags & 0x08) != 0 && reader.Remaining >= 2)
                            target = reader.ReadUInt16();
                        if ((flags & 0x20) != 0 && reader.Remaining >= 4)
                            value = reader.ReadUInt32();
                        if ((flags & 0x10) != 0 && reader.Remaining >= 2)
                        {
                            ushort extraWordCount = reader.ReadUInt16();
                            for (int wordIndex = 0; wordIndex < extraWordCount && reader.Remaining >= 2; wordIndex++)
                                reader.ReadUInt16();
                        }

                        if (reader.Remaining >= 1)
                        {
                            byte entitySynchInfoFlags = reader.ReadByte();
                            if ((entitySynchInfoFlags & 0x02) != 0 && reader.Remaining >= 4)
                            {
                                uint entitySynchInfoHP = reader.ReadUInt32();
                                if (componentIsBlingGnome)
                                    EntitySynchInfoAuthority.Instance.ObserveClientBlingGnomeHP(conn, componentId, entitySynchInfoHP, "GNOME-SM-HP-0x64");
                                else
                                    ObserveClientPlayerHP(conn, entitySynchInfoHP, "PLAYER-STATE-ENTITY-SYNCH-INFO");
                            }
                        }

                        if (VerbosePacketLogging) Debug.LogError($"[PLAYER-STATE] cid={componentId} flags=0x{flags:X2} type={messageType} value={value}");

                        if (messageType == 0x1C)
                        {
                            var playerState = GetPlayerState(conn.ConnId.ToString());
                            if (playerState != null)
                            {
                                Debug.LogError($"[LEVEL-UP-0x1C] state=confirmed level={playerState.Level} xp={playerState.Experience}");
                            }
                        }
                    }
                    return;
                }

                if (subMessage == 0x65)
                {
                    if (componentId >= 50000 && componentId < 60000)
                    {
                        try
                        {
                            if (reader.Remaining > 0)
                            {
                                byte[] rawPeek = reader.PeekRemaining();
                                if (VerbosePacketLogging) Debug.LogError($"[MONSTER-0x65] cid={componentId} remaining={reader.Remaining} raw={BitConverter.ToString(rawPeek)}");
                            }

                            var monster = CombatRuntime.Instance.GetMonsterByComponent(componentId);
                            int componentOffset = CombatRuntime.Instance.GetComponentOffset(componentId);
                            if (monster != null && componentOffset == 1 && reader.Remaining >= 2)
                            {
                                byte sessionId = reader.ReadByte();
                                byte moveCount = reader.ReadByte();
                                int applied = 0;
                                for (int moveIndex = 0; moveIndex < moveCount && reader.Remaining >= 13; moveIndex++)
                                {
                                    byte moveFlags = reader.ReadByte();
                                    int headingRaw = reader.ReadInt32();
                                    int posXRaw = reader.ReadInt32();
                                    int posYRaw = reader.ReadInt32();
                                    monster.SessionId = sessionId;
                                    monster.Heading = headingRaw / 256f;
                                    monster.PosFixedX = posXRaw;
                                    monster.PosFixedY = posYRaw;
                                    if (monster.EntityId <= ushort.MaxValue)
                                        _allEntityPositions[(ushort)monster.EntityId] = (monster.PosX, monster.PosY, monster.PosZ);
                                    applied++;
                                    if (VerbosePacketLogging) Debug.LogError($"[MONSTER-MOVE-0x65] name='{monster.Name}' cid={componentId} flags=0x{moveFlags:X2} session={sessionId} pos=({monster.PosX:F1},{monster.PosY:F1}) heading={monster.Heading:F1}");
                                }
                                if (applied != moveCount)
                                    Debug.LogError($"[MONSTER-MOVE-0x65] name='{monster.Name}' cid={componentId} expected={moveCount} applied={applied} remaining={reader.Remaining}");
                            }

                            if (reader.Remaining >= 1)
                            {
                                byte entitySynchInfoFlags = reader.ReadByte();
                                if (VerbosePacketLogging) Debug.LogError($"[MONSTER-0x65] cid={componentId} offset={componentOffset} entitySynchInfoFlags=0x{entitySynchInfoFlags:X2}");

                                if ((entitySynchInfoFlags & 0x02) != 0 && reader.Remaining >= 4)
                                {
                                    uint clientHP = reader.ReadUInt32();
                                    int clientActual = (int)(clientHP / 256);
                                    if (monster != null)
                                    {
                                        int serverActual = (int)(CombatRuntime.Instance.PeekMonsterCurrentHPWire(monster) / 256);
                                        if (VerbosePacketLogging) Debug.LogError($"[MONSTER-0x65] name='{monster.Name}' eid={monster.EntityId} clientHp={clientActual} serverHp={serverActual} delta={serverActual - clientActual}");

                                        CombatRuntime.Instance.ObserveClientMonsterHP(monster, clientHP, "MONSTER-MOVE-HP-0x65");
                                    }
                                    else
                                    {
                                        if (VerbosePacketLogging) Debug.LogError($"[MONSTER-0x65] cid={componentId} state=notFound target=combatRuntime");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[MONSTER-0x65] parse state=failed message='{ex.Message}'");
                        }
                        return;
                    }

                    if (EntitySynchInfoAuthority.Instance.IsBlingGnomeComponent(componentId))
                    {
                        try
                        {
                            byte sessionId = reader.Remaining >= 1 ? reader.ReadByte() : (byte)0;
                            byte moveCount = reader.Remaining >= 1 ? reader.ReadByte() : (byte)0;
                            for (int moveIndex = 0; moveIndex < moveCount && reader.Remaining >= 13; moveIndex++)
                            {
                                reader.ReadByte();
                                reader.ReadInt32();
                                reader.ReadInt32();
                                reader.ReadInt32();
                            }
                            if (reader.Remaining >= 1)
                            {
                                byte entitySynchInfoFlags = reader.ReadByte();
                                if ((entitySynchInfoFlags & 0x02) != 0 && reader.Remaining >= 4)
                                {
                                    uint clientHP = reader.ReadUInt32();
                                    EntitySynchInfoAuthority.Instance.ObserveClientBlingGnomeHP(conn, componentId, clientHP, "GNOME-MOVE-HP-0x65");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[GNOME-0x65] parse state=failed message='{ex.Message}'");
                        }
                        return;
                    }

                    HandleClientMove(conn, reader, componentId);
                    return;
                }
                else if (subMessage == 0x02)
                {
                    if (componentId == conn.QuestManagerId)
                    {
                        Debug.LogError("[QUEST-0x02] action=closeCancel state=clearPending");
                        conn.PendingQuestHash = 0;
                        conn.PendingQuestNpcEntityId = 0;
                        conn.ViewingQuestInstanceId = 0;
                    }
                }
                else if (subMessage == 0x03)
                {
                    if (componentId == conn.QuestManagerId)
                    {
                        try
                        {
                            uint instanceId = reader.ReadUInt32();
                            Debug.LogError($"[QUEST-ABANDON] cid=0x{componentId:X4} instanceId=0x{instanceId:X8}");

                            while (reader.Remaining > 0)
                            {
                                byte trail = reader.ReadByte();
                                Debug.LogError($"[QUEST-ABANDON] trailingByte=0x{trail:X2}");
                            }

                            var playerState = QuestManager.Instance.GetPlayerState(conn.ConnId.ToString());
                            if (playerState != null)
                            {
                                var abandoned = playerState.ActiveQuests.FirstOrDefault(q => q.InstanceId == instanceId);
                                if (abandoned != null)
                                {
                                    playerState.ActiveQuests.Remove(abandoned);
                                    Debug.LogError($"[QUEST-ABANDON] quest={abandoned.QuestId} state=removed");
                                }
                                else
                                {
                                    Debug.LogError($"[QUEST-ABANDON] instanceId=0x{instanceId:X8} state=notFound source=activeList");
                                }
                            }

                            QuestManager.Instance.SendRemovePacket(conn, instanceId);

                            SavePlayerQuests(conn);

                            Debug.LogError("[QUEST-ABANDON] state=complete");
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[QUEST-ABANDON] error={ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                        }
                    }
                    else
                    {
                            Debug.LogError($"[COMPONENT] sub=0x03 componentId={componentId} state=notQuest");
                        HandleCancelAction(conn, reader, componentId);
                    }
                }
                else if (subMessage == 0x05)
                {
                    if (componentId == conn.QuestManagerId)
                    {
                        if (conn.PendingTurnInInstanceId != 0)
                        {
                            Debug.LogError($"[QUEST-TURNIN] sub=0x05 action=complete instanceId={conn.PendingTurnInInstanceId}");
                            uint instanceId = conn.PendingTurnInInstanceId;
                            conn.PendingTurnInInstanceId = 0;

                            var completingQuest = QuestManager.Instance.GetQuestByInstanceId(conn.ConnId.ToString(), instanceId);
                            QuestData completedQuestData = null;
                            if (completingQuest != null)
                                completedQuestData = DatabaseLoader.Quests
                                    .FirstOrDefault(q => q.id.Equals(completingQuest.QuestId, StringComparison.OrdinalIgnoreCase));

                            RemoveQuestItemsFromInventory(conn, completingQuest);
                            QuestManager.Instance.HandleTurnInConfirmed(conn, instanceId);
                            SavePlayerQuests(conn);

                            ApplyQuestRewards(conn, completedQuestData);
                        }
                        else
                        {
                            Debug.LogError("[QUEST] sub=0x05 action=acceptButton");
                            uint npcEntityId = reader.ReadUInt32();
                            byte gcTypeIndicator = reader.ReadByte();
                            uint questHash = reader.ReadUInt32();
                            Debug.LogError($"[QUEST-ACCEPT] npc={npcEntityId} hash=0x{questHash:X8}");
                            QuestManager.Instance.HandleAcceptConfirmed(conn, npcEntityId, questHash);
                            if (DatabaseLoader.QuestsByHash.TryGetValue(questHash, out var acceptedQuest1) && !string.IsNullOrEmpty(acceptedQuest1.onAcceptItem))
                                GiveOnAcceptItem(conn, acceptedQuest1.onAcceptItem);
                        }
                    }
                }
                else if (subMessage == 0x06)
                {
                    if (componentId == conn.QuestManagerId)
                    {
                        uint npcEntityId = reader.ReadUInt32();
                        byte gcTypeIndicator = reader.ReadByte();
                        uint questHash = reader.ReadUInt32();
                        Debug.LogError($"[QUEST] npc={npcEntityId} hash=0x{questHash:X8} pending=0x{conn.PendingQuestHash:X8}");

                        if (conn.PendingQuestHash == questHash)
                        {
                            Debug.LogError("[QUEST] action=accept");
                            conn.PendingQuestHash = 0;
                            QuestManager.Instance.HandleAcceptConfirmed(conn, npcEntityId, questHash);
                            if (DatabaseLoader.QuestsByHash.TryGetValue(questHash, out var acceptedQuest2) && !string.IsNullOrEmpty(acceptedQuest2.onAcceptItem))
                                GiveOnAcceptItem(conn, acceptedQuest2.onAcceptItem);
                        }
                        else
                        {
                            Debug.LogError("[QUEST] action=dialog");
                            conn.PendingQuestHash = questHash;
                            conn.PendingQuestNpcEntityId = npcEntityId;
                            QuestManager.Instance.SendQueryResponse(conn, questHash, npcEntityId);
                        }
                    }
                }

                else if (subMessage == 0x04)
                {
                    if (componentId == conn.QuestManagerId)
                    {
                        uint instanceId = reader.ReadUInt32();
                        Debug.LogError($"[QUEST-0x04] instanceId={instanceId} action=checkTurnIn");

                        var playerState = QuestManager.Instance.GetPlayerState(conn.ConnId.ToString());
                        var activeQuest = playerState?.ActiveQuests.FirstOrDefault(q => q.InstanceId == instanceId);

                        if (activeQuest != null)
                        {
                            var objectives = activeQuest.Objectives ?? new System.Collections.Generic.List<DungeonRunners.Gameplay.QuestProgress>();
                            bool allComplete = QuestManager.Instance.CanQueryComplete(activeQuest);
                            Debug.LogError($"[QUEST-0x04] quest={activeQuest.QuestId} allComplete={allComplete}");

                            if (allComplete)
                            {
                                Debug.LogError($"[QUEST-0x04] action=sendTurnInDialog instanceId={instanceId}");
                                QuestManager.Instance.SendTurnInDialog(conn, instanceId);
                            }
                            else
                            {
                                Debug.LogError("[QUEST-0x04] state=incomplete action=view");
                            }
                        }
                        else
                        {
                            Debug.LogError($"[QUEST-0x04] instanceId={instanceId} state=notFound");
                        }
                    }
                }
                else if (subMessage == 0x0A)
                {
                    if (componentId == conn.QuestManagerId)
                    {
                        Debug.LogError("[QUEST-0x0A] action=townPortal source=obelisk");
                        if (conn.HasSavedTownPortal)
                        {
                            Debug.LogError($"[QUEST-0x0A] action=teleport zone={conn.TownPortalZoneName} pos=({conn.TownPortalPosX:F1},{conn.TownPortalPosY:F1})");
                            ChangeZoneToPosition(conn, conn.TownPortalZoneName,
                                conn.TownPortalPosX, conn.TownPortalPosY, conn.TownPortalPosZ);
                        }
                        else
                        {
                            Debug.LogError("[QUEST-0x0A] state=noSavedTownPortal");
                        }
                    }
                }
                else if (subMessage == 0x01)
                {
                    if (componentId == conn.QuestManagerId && reader.Remaining == 0)
                    {
                        if (conn.PendingQuestHash != 0)
                        {
                            Debug.LogError($"[QUEST-ACCEPT] sub=0x01 action=accept hash=0x{conn.PendingQuestHash:X8}");
                            uint questHash = conn.PendingQuestHash;
                            uint npcEntityId = conn.PendingQuestNpcEntityId;
                            conn.PendingQuestHash = 0;
                            conn.PendingQuestNpcEntityId = 0;
                            QuestManager.Instance.HandleAcceptConfirmed(conn, npcEntityId, questHash);
                            if (DatabaseLoader.QuestsByHash.TryGetValue(questHash, out var acceptedQuest3) && !string.IsNullOrEmpty(acceptedQuest3.onAcceptItem))
                                GiveOnAcceptItem(conn, acceptedQuest3.onAcceptItem);
                            SavePlayerQuests(conn);
                        }
                        else if (conn.PendingTurnInInstanceId != 0)
                        {
                            Debug.LogError($"[QUEST-TURNIN] empty-0x01 complete instanceId={conn.PendingTurnInInstanceId}");
                            uint instanceId = conn.PendingTurnInInstanceId;
                            conn.PendingTurnInInstanceId = 0;

                            var completingQuest = QuestManager.Instance.GetQuestByInstanceId(conn.ConnId.ToString(), instanceId);
                            QuestData completedQuestData = null;
                            if (completingQuest != null)
                                completedQuestData = DatabaseLoader.Quests
                                    .FirstOrDefault(q => q.id.Equals(completingQuest.QuestId, StringComparison.OrdinalIgnoreCase));

                            RemoveQuestItemsFromInventory(conn, completingQuest);
                            QuestManager.Instance.HandleTurnInConfirmed(conn, instanceId);
                            SavePlayerQuests(conn);

                            ApplyQuestRewards(conn, completedQuestData);
                        }
                        else
                        {
                            Debug.LogError("[QUEST-0x01] state=noPendingQuest action=view");
                            conn.ViewingQuestInstanceId = 1;
                        }
                        return;
                    }
                    Debug.LogError($"[SUBMSG-0x01] state=questCheckPassed remaining={reader.Remaining}");

                    byte responseId = reader.ReadByte();
                    Debug.LogError($"[ACTION-READ] responseId={responseId}, remaining={reader.Remaining}");

                    byte actionType = reader.ReadByte();
                    Debug.LogError($"[ACTION-READ] actionType=0x{actionType:X2}, remaining={reader.Remaining}");

                    if (actionType == 0x52 && reader.Remaining <= 2)
                    {
                        byte spellSessionID = reader.Remaining >= 1 ? reader.ReadByte() : (byte)0;
                        byte slotID = reader.Remaining >= 1 ? reader.ReadByte() : (byte)0;
                        Debug.LogError($"[SPELL-0x52] action=selfCast sessionId={spellSessionID} slotId={slotID} componentId={componentId} remaining={reader.Remaining}");
                        if (conn.HasActiveUseTarget)
                            ClearUseTargetAndReleaseControl(conn, "ACTION-0x52", componentId);

                        var msg52 = new LEWriter();
                        msg52.WriteByte(0x35);
                        msg52.WriteUInt16(componentId);
                        msg52.WriteByte(0x01);
                        msg52.WriteByte(responseId);
                        msg52.WriteByte(0x52);
                        msg52.WriteByte(spellSessionID);
                        msg52.WriteByte(slotID);
                        if (!TryWriteEntitySynchForComponent(conn, msg52, componentId, 0x01, EntitySynchInfoContext.PlayerActionResponse, "PlayerActionResponse", false))
                        {
                            Debug.LogError($"[SPELL-0x52] state=failed target=actionResponseEntitySynchInfo component={componentId} slot={slotID}");
                            return;
                        }
                        ClearZoneSpawnInvulnerability(conn, $"ACTION-0x{actionType:X2}");
                        conn.MessageQueue.Enqueue(msg52.ToArray());
                        Debug.LogError("[SPELL-0x52] action=enqueue target=actionResponse queue=MessageQueue");


                        if (componentId < 50000)
                        {
                            PlayerState state52 = GetPlayerState(conn.ConnId.ToString());
                            if (state52 != null)
                                HandleSelfCastSpell(conn, state52, slotID, componentId, spellSessionID);
                        }
                        return;
                    }

                    byte sessionID = reader.ReadByte();
                    Debug.LogError($"[ACTION-READ] sessionID={sessionID}, remaining={reader.Remaining}");

                    ushort targetEntityID = reader.ReadUInt16();
                    Debug.LogError($"[ACTION-READ] targetEntityID={targetEntityID}, remaining={reader.Remaining}");

                    Debug.LogError($"[ACTION] ActionType=0x{actionType:X2}, ResponseId={responseId}, SessionID={sessionID}, Target={targetEntityID}");
                    if (actionType != 0x50 && conn.HasActiveUseTarget)
                        ClearUseTargetAndReleaseControl(conn, $"ACTION-0x{actionType:X2}", componentId);

                    if (actionType == 0x06)
                    {
                        if (IsDroppedItem(targetEntityID))
                        {
                            Debug.LogError($"[PICKUP] Player clicked dropped item, auto-bagging. Target={targetEntityID}");
                            HandleItemRightClickPickup(conn, componentId, targetEntityID, responseId, sessionID);
                        }
                        else if (IsPortal(targetEntityID, out var portal))
                        {
                            Debug.LogError($"[PORTAL] Player clicked portal! Target={targetEntityID} -> {portal.TargetZone}");
                            HandlePortalActivation(conn, componentId, targetEntityID, responseId, sessionID, portal);
                        }
                        else if (IsChest(targetEntityID, out var chestData))
                        {
                            float chestDx = conn.PlayerPosX - chestData.PosX;
                            float chestDy = conn.PlayerPosY - chestData.PosY;
                            float chestActivationRange = 40f;
                            if (chestDx * chestDx + chestDy * chestDy > chestActivationRange * chestActivationRange)
                            {
                                StartUseTargetMoving(conn, chestData.PosX, chestData.PosY);
                                Debug.LogError($"[CHEST] approach Target={targetEntityID} ({chestData.Label}) dist={Mathf.Sqrt(chestDx * chestDx + chestDy * chestDy):F1} range={chestActivationRange:F1} action=client-approach");
                            }
                            else
                            {
                                Debug.LogError($"[CHEST] Player clicked chest! Target={targetEntityID} ({chestData.Label})");
                                HandleChestActivation(conn, componentId, targetEntityID, responseId, sessionID, chestData);
                            }
                        }
                        else if (IsCheckpoint(targetEntityID, out var checkpoint))
                        {
                            Debug.LogError($"[CHECKPOINT] Player clicked checkpoint! Target={targetEntityID}");
                            HandleCheckpointActivation(conn, componentId, targetEntityID, responseId, sessionID, checkpoint);
                        }
                        else if (WorldEntitySpawner.Instance.TryGetEntity(targetEntityID, out var weData))
                        {
                            Debug.LogError($"[WORLD-ENTITY] Player clicked {weData.EntityType}: {weData.Label} (id=0x{targetEntityID:X4})");
                            if (weData.IsTeleporter)
                            {
                                HandleTeleporterActivation(conn, componentId, targetEntityID, responseId, sessionID, weData);
                            }
                            else if (weData.IsGate)
                            {
                                var ackMessage = new LEWriter();
                                ackMessage.WriteByte(0x35);
                                ackMessage.WriteUInt16(componentId);
                                ackMessage.WriteByte(0x01);
                                ackMessage.WriteByte(responseId);
                                ackMessage.WriteByte(0x06);
                                ackMessage.WriteByte(sessionID);
                                ackMessage.WriteUInt16(targetEntityID);
                                WritePlayerEntitySynch(conn, ackMessage);
                                conn.MessageQueue.Enqueue(ackMessage.ToArray());

                                bool isPvpGate = weData.GCType != null &&
                                    weData.GCType.IndexOf("pvp", StringComparison.OrdinalIgnoreCase) >= 0;
                                SendSystemMessage(conn, isPvpGate
                                    ? "This gate will open once the PVP match begins."
                                    : "The gate is sealed. Defeat the boss to open it.");
                                Debug.LogError($"[WORLD-ENTITY] gate state=closed reason={(isPvpGate ? "pvp-setup" : "boss-alive")} label={weData.Label}");
                            }
                            else
                            {
                                var ackMessage = new LEWriter();
                                ackMessage.WriteByte(0x35);
                                ackMessage.WriteUInt16(componentId);
                                ackMessage.WriteByte(0x01);
                                ackMessage.WriteByte(responseId);
                                ackMessage.WriteByte(0x06);
                                ackMessage.WriteByte(sessionID);
                                ackMessage.WriteUInt16(targetEntityID);
                                WritePlayerEntitySynch(conn, ackMessage);
                                conn.MessageQueue.Enqueue(ackMessage.ToArray());

                                var nonCombatInteractiveMessage = new LEWriter();
                                nonCombatInteractiveMessage.WriteByte(0x03);
                                nonCombatInteractiveMessage.WriteUInt16(targetEntityID);
                                nonCombatInteractiveMessage.WriteByte(0x0A);
                                nonCombatInteractiveMessage.WriteUInt32(0x00000000);
                                WriteNonCombatInteractiveEntitySynchInfo(nonCombatInteractiveMessage, weData.GCType);
                                conn.MessageQueue.Enqueue(nonCombatInteractiveMessage.ToArray());
                                Debug.LogError($"[WORLD-ENTITY] Sent NCI activate (0x03/0x0A) for {weData.EntityType}: {weData.Label}");

                                try
                                {
                                    if (!string.IsNullOrEmpty(weData.GCType) &&
                                        weData.GCType.IndexOf("NCIPortrait", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        Debug.LogError($"[QUEST] Portrait clicked ({weData.GCType}) - ticking Q02_a2 'Suspicious portrait' objective");
                                        var portraitUpdates = QuestManager.Instance.OnItemPickedUp(conn, "QuestItemPAL2.D00_Q02_a2_01");
                                        if (portraitUpdates != null && portraitUpdates.Count > 0)
                                        {
                                            foreach (var portraitUpdate in portraitUpdates)
                                                Debug.LogError($"[QUEST] Portrait tick: quest={portraitUpdate.QuestId} objective={portraitUpdate.ObjectiveName} now {portraitUpdate.Current}/{portraitUpdate.Required}");
                                        }
                                        else
                                        {
                                            Debug.LogError($"[QUEST] Portrait click produced no objective updates - either Q02_a2 not active or objective already complete");
                                        }
                                    }
                                }
                                catch (Exception portraitException)
                                {
                                    Debug.LogError($"[QUEST] Portrait quest hook failed (non-fatal): {portraitException.Message}");
                                }

                                try
                                {
                                    if (!string.IsNullOrEmpty(weData.Name) &&
                                        weData.Name.Equals("HermitLockbox", StringComparison.OrdinalIgnoreCase))
                                    {
                                        Debug.LogError($"[QUEST] Hermit Lockbox clicked ({weData.GCType}) - dropping HermitRing on ground");
                                        uint hermitChanceRaw = RandomStreams.GenerateGlobalStatic(
                                            "ActivateDropTrigger::doEvent.chance",
                                            "world/dungeon00/quest/Q03_a1:Entity_HermitLockbox");
                                        uint hermitChanceRoll = (hermitChanceRaw % 100u) + 1u;
                                        Debug.LogError($"[ACTIVATEDROP-CLIENT] entity={weData.Name} gc={weData.GCType} item=QuestItemPAL2.D00_Q03_a1_01 chance=100 roll={hermitChanceRoll} pass={(hermitChanceRoll <= 100u)} sourceFunction=ActivateDropTrigger::doEvent@0x005CB460");
                                        if (hermitChanceRoll > 100u)
                                            return;
                                        var hermitItem = new GCObject
                                        {
                                            GCClass = "QuestItemPAL2.D00_Q03_a1_01",
                                            DFCClass = "Item",
                                            StoredLevel = 1
                                        };
                                        ConsumeItemAddToWorldHeading("activatedrop:HermitLockbox:QuestItemPAL2.D00_Q03_a1_01");
                                        var hermitPlacement = ResolveItemDropPlacement(
                                            conn,
                                            !string.IsNullOrWhiteSpace(weData.Zone) ? weData.Zone : conn.CurrentZoneName,
                                            conn.InstanceId,
                                            weData.PosX,
                                            weData.PosY,
                                            weData.PosZ,
                                            weData.Heading,
                                            "activatedrop:HermitLockbox:QuestItemPAL2.D00_Q03_a1_01");
                                        float hpx = hermitPlacement.X;
                                        float hpy = hermitPlacement.Y;
                                        ushort hermitEid = GetNextLootEntityId();
                                        TrackDroppedItem(hermitEid, hermitItem, conn, 1, hpx, hpy, hermitPlacement.Z, 1);
                                        if (_droppedItems.TryGetValue(hermitEid, out var hermitInfo))
                                            hermitInfo.IsQuestItem = true;
                                        BroadcastDroppedItemSpawnPacket(conn, hermitEid, _droppedItems[hermitEid]);
                                    }
                                }
                                catch (Exception _hermitEx)
                                {
                                    Debug.LogError($"[QUEST] Hermit Lockbox hook failed (non-fatal): {_hermitEx.Message}");
                                }

                                if (weData.EntityType == "chest" || weData.EntityType == "boss_chest")
                                {
                                    PlayerState chestPS = GetPlayerState(conn.ConnId.ToString());
                                    int chestLevel = chestPS?.Level ?? 1;
                                    var chestDrops = new List<LootDrop>();
                                    foreach (var (generator, count, slot) in weData.GetChestGenerators(null, 0))
                                    {
                                        var slotDrops = GCObjectGeneratorTable.Instance.GenerateChestLoot(generator, count, chestLevel);
                                        chestDrops.AddRange(slotDrops);
                                        Debug.LogError($"[CHEST-WE] {weData.Label}: slot={slot} generator={generator} count={count} drops={slotDrops.Count} sourceFunction=NonCombatInteractiveDesc-ItemGenerator1-5");
                                    }
                                    Debug.LogError($"[CHEST-WE] {weData.Label}: totalDrops={chestDrops.Count}");
                                    foreach (var drop in chestDrops)
                                    {
                                        if (drop.IsGold)
                                        {
                                            Debug.LogError($"[CHEST-WE] +{drop.GoldAmount} gold from {weData.Label}");
                                        }
                                        else
                                        {
                                            if (string.IsNullOrEmpty(drop.GCType)) { Debug.LogError("[CHEST-WE]  Skipping null GCType"); continue; }
                                            string _we = ResolveAuthoredItemClass(drop.GCType);
                                            var item = new GCObject { GCClass = drop.GCType, DFCClass = _we, PresetScaleMod = drop.ScaleMod, StoredRarity = (int)drop.Rarity, StoredLevel = drop.ItemLevel };
                                            var wePlacement = ResolveItemDropPlacement(
                                                conn,
                                                !string.IsNullOrWhiteSpace(weData.Zone) ? weData.Zone : conn.CurrentZoneName,
                                                conn.InstanceId,
                                                weData.PosX,
                                                weData.PosY,
                                                weData.PosZ,
                                                weData.Heading,
                                                $"worldentity-chest:{weData.Label}:{drop.GCType}");
                                            float lpx = wePlacement.X;
                                            float lpy = wePlacement.Y;
                                            ushort lootId = GetNextLootEntityId();
                                            TrackDroppedItem(lootId, item, conn, 1, lpx, lpy, wePlacement.Z, chestLevel);
                                            BroadcastDroppedItemSpawnPacket(conn, lootId, _droppedItems[lootId]);
                                            Debug.LogError($"[CHEST-WE]  {drop.Label} ({drop.Rarity}) from {weData.Label}");
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            Debug.LogError($"[NPC] Player clicked NPC! Target={targetEntityID}");
                            HandleNPCClick(conn, componentId, targetEntityID, responseId, sessionID);
                        }
                    }
                    else if (actionType == 0x52)
                    {
                        Debug.LogError($"[ACTION]  CHECKPOINT USE (0x52)! Target={targetEntityID}");
                        HandleCheckpointUse(conn, componentId, responseId, sessionID, reader);
                    }
                    else if (actionType == 0xA0)
                    {
                        Debug.LogError($"[ACTION] 0xA0 received (unexpected) target={targetEntityID}");
                    }
                    else if (actionType == 0x50)
                    {
                        byte manipulatorId = sessionID;
                        byte useFlags = (byte)(targetEntityID & 0xFF);
                        byte targetIdLow = (byte)((targetEntityID >> 8) & 0xFF);
                        byte targetIdHigh = reader.ReadByte();
                        ushort actualTargetId = (ushort)(targetIdLow | (targetIdHigh << 8));
                        var gnomeRuntime = BlingGnomeRuntime.Instance;
                        bool isBlingGnomeTarget = gnomeRuntime.TryResolveGnomeTarget(conn, actualTargetId,
                            out uint gnomeEntityId, out ushort gnomeBehaviorId, out bool gnomeBehaviorCreated, out string gnomeTargetReason);
                        Combat.Monster actionTargetMonster = isBlingGnomeTarget ? null : Combat.CombatRuntime.Instance.FindMonsterForTarget(actualTargetId, conn.PlayerPosX, conn.PlayerPosY, GetInstanceZoneKey(conn));
                        TryConsumeClientEntitySynchInfoSuffix(conn, reader, "ACTION-0x50-ENTITY-SYNCH-INFO", actionTargetMonster, out bool acceptedActionMonsterHP);

                        Debug.LogError($"[ATTACK] 0x50: componentId={componentId}, manipulatorId={manipulatorId}, flags={useFlags}, targetId={actualTargetId}, gnome={gnomeEntityId}, gnomeBehavior={gnomeBehaviorId}, gnomeBehaviorCreated={gnomeBehaviorCreated}, gnomeMatch={gnomeTargetReason}");
                        if (isBlingGnomeTarget)
                        {
                            if (componentId != 0)
                                conn.UnitBehaviorId = componentId;

                            var actionResponseMessage = new LEWriter();
                            actionResponseMessage.WriteByte(0x35);
                            actionResponseMessage.WriteUInt16(componentId);
                            actionResponseMessage.WriteByte(0x01);
                            actionResponseMessage.WriteByte(responseId);
                            actionResponseMessage.WriteByte(0x50);
                            actionResponseMessage.WriteByte(manipulatorId);
                            actionResponseMessage.WriteByte(useFlags);
                            actionResponseMessage.WriteUInt16(actualTargetId);
                            const EntitySynchInfoContext actionResponseContext = EntitySynchInfoContext.PlayerActionResponse;
                            const string actionResponseEntitySynchInfoTag = "PlayerActionResponse";
                            if (!TryWriteEntitySynchForComponent(conn, actionResponseMessage, componentId, 0x01, actionResponseContext, actionResponseEntitySynchInfoTag, true))
                            {
                                Debug.LogError($"[GNOME-ACTIVATE] state=failed target=actionResponseEntitySynchInfo component={componentId} targetId={actualTargetId} flags={useFlags} action=releaseFallback");
                                ClearUseTargetAndReleaseControl(conn, "GNOME-ACTIVATE-entity-synch-info-failed", componentId);
                                return;
                            }
                            bool actionResponseSent = QueueClientEntityStream(conn, actionResponseMessage.ToArray());

                            bool activated = false;
                            try
                            {
                                activated = gnomeRuntime.ActivateGnome(conn, actualTargetId);
                                Debug.LogError($"[GNOME-ACTIVATE] action=useTarget target={actualTargetId} manip={manipulatorId} flags={useFlags} component={componentId} sent={actionResponseSent} activated={activated}");
                            }
                            catch (Exception ex)
                            {
                                Debug.LogError($"[GNOME-ACTIVATE] state=failed target={actualTargetId} component={componentId} message='{ex}'");
                            }

                            if (!actionResponseSent)
                            {
                                ClearUseTargetAndReleaseControl(conn, "GNOME-ACTIVATE-send-failed", componentId);
                                return;
                            }

                            if (!activated)
                                Debug.LogError($"[GNOME-ACTIVATE] action=useTarget target={actualTargetId} state=conversionNotStarted");
                        }
                        else if (componentId >= 50000 && componentId < 60000)
                        {
                            Debug.LogError($"[ATTACK] source=monster target=player component={componentId} targetId={actualTargetId}");
                            var attackingMonster = Combat.CombatRuntime.Instance.GetMonsterByComponent(componentId);
                            if (attackingMonster != null)
                                Debug.LogError($"[ATTACK] monster='{attackingMonster.Name}' targetPlayer={actualTargetId}");
                            else
                                Debug.LogError($"[ATTACK] no monster for component={componentId}");
                        }
                        else
                        {
                            if (componentId != 0)
                                conn.UnitBehaviorId = componentId;
                            Debug.LogError($"[ATTACK] source=player action=useTarget component={componentId} target={actualTargetId} manip={manipulatorId} flags={useFlags}");
                            bool handled = false;
                            try
                            {
                                var monster = actionTargetMonster ?? Combat.CombatRuntime.Instance.FindMonsterForTarget(actualTargetId, conn.PlayerPosX, conn.PlayerPosY, GetInstanceZoneKey(conn));
                                if (monster != null)
                                {
                                    HandlePlayerAttackMonster(conn, componentId, responseId, manipulatorId, useFlags, actualTargetId, monster, acceptedActionMonsterHP);
                                    handled = true;
                                }
                                else if (HandlePlayerAttackPlayer(conn, componentId, responseId, manipulatorId, useFlags, actualTargetId))
                                {
                                    handled = true;
                                }
                                else
                                {
                                    Debug.LogError($"[ATTACK] target={actualTargetId} state=noMonster");
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogError($"[ATTACK] error={ex.Message}\n{ex.StackTrace}");
                            }

                            if (!handled)
                            {
                                var unresolvedMonster = Combat.CombatRuntime.Instance.GetMonster(actualTargetId)
                                                       ?? Combat.CombatRuntime.Instance.GetMonsterByComponent(actualTargetId);
                                if (unresolvedMonster != null)
                                {
                                    uint unresolvedHP = Combat.CombatRuntime.Instance.PeekMonsterCurrentHPWire(unresolvedMonster);
                                    bool deadUseTarget = !unresolvedMonster.IsAlive || unresolvedMonster.CurrentHPWire == 0 || unresolvedHP == 0 || _finalizedMonsterKills.Contains((uint)actualTargetId);
                                    Debug.LogError($"[ATTACK] action=useTargetResolve target={actualTargetId} alive={unresolvedMonster.IsAlive} hp={unresolvedMonster.CurrentHPWire} resolvedHp={unresolvedHP} dead={deadUseTarget}");
                                    if (deadUseTarget)
                                    {
                                        bool sent = SendUseTargetActionResponse(conn, componentId, responseId, manipulatorId, useFlags, actualTargetId, EntitySynchInfoContext.PlayerActionResponse, "PlayerActionResponse", "dead-target");
                                        if (!sent)
                                            Debug.LogError($"[ATTACK] failed dead-target ActionResponse target={actualTargetId} component={componentId} flags={useFlags}");
                                        ClearUseTargetAndReleaseControl(conn, "ATTACK-dead-target", componentId, true, false);
                                    }
                                    else
                                    {
                                        var unresolvedLiveTargetMessage = new LEWriter();
                                        unresolvedLiveTargetMessage.WriteByte(0x35);
                                        unresolvedLiveTargetMessage.WriteUInt16(componentId);
                                        unresolvedLiveTargetMessage.WriteByte(0x01);
                                        unresolvedLiveTargetMessage.WriteByte(responseId);
                                        unresolvedLiveTargetMessage.WriteByte(0x50);
                                        unresolvedLiveTargetMessage.WriteByte(manipulatorId);
                                        unresolvedLiveTargetMessage.WriteByte(useFlags);
                                        unresolvedLiveTargetMessage.WriteUInt16(actualTargetId);
                                        WritePlayerEntitySynch(conn, unresolvedLiveTargetMessage);
                                        QueueClientEntityStream(conn, unresolvedLiveTargetMessage.ToArray());
                                        Debug.LogError($"[ATTACK] action=sendActionResponse reason=unresolvedLiveTarget target={actualTargetId} alive={unresolvedMonster.IsAlive} hp={unresolvedMonster.CurrentHPWire}");
                                        ClearUseTargetAndReleaseControl(conn, "ATTACK-unresolved-live-target", componentId);
                                    }
                                }
                                else
                                {
                                    if (_finalizedMonsterKills.Contains((uint)actualTargetId))
                                    {
                                        bool sent = SendUseTargetActionResponse(conn, componentId, responseId, manipulatorId, useFlags, actualTargetId, EntitySynchInfoContext.PlayerActionResponse, "PlayerActionResponse", "finalized-dead-target");
                                        if (!sent)
                                            Debug.LogError($"[ATTACK] failed finalized dead-target ActionResponse target={actualTargetId} component={componentId} flags={useFlags}");
                                        ClearUseTargetAndReleaseControl(conn, "ATTACK-finalized-target-missing", componentId, true, false);
                                    }
                                    else
                                    {
                                        var missingTargetMessage = new LEWriter();
                                        missingTargetMessage.WriteByte(0x35);
                                        missingTargetMessage.WriteUInt16(componentId);
                                        missingTargetMessage.WriteByte(0x01);
                                        missingTargetMessage.WriteByte(responseId);
                                        missingTargetMessage.WriteByte(0x50);
                                        missingTargetMessage.WriteByte(manipulatorId);
                                        missingTargetMessage.WriteByte(useFlags);
                                        missingTargetMessage.WriteUInt16(actualTargetId);
                                        WritePlayerEntitySynch(conn, missingTargetMessage);
                                        QueueClientEntityStream(conn, missingTargetMessage.ToArray());
                                        Debug.LogError($"[ATTACK] action=sendActionResponse reason=fallbackTargetMissing target={actualTargetId}");
                                        ClearUseTargetAndReleaseControl(conn, "ATTACK-fallback-target-missing", componentId);

                                    }
                                }
                            }
                        }
                    }
                    else if (actionType == 0x51)
                    {
                        byte manipulatorId = sessionID;
                        byte actionID = (byte)(targetEntityID & 0xFF);
                        byte posXByte0 = (byte)((targetEntityID >> 8) & 0xFF);
                        byte[] posRemain = reader.Remaining >= 11 ? reader.ReadBytes(11) : reader.ReadBytes(reader.Remaining);

                        int posX = 0, posY = 0, posZ = 0;
                        if (posRemain.Length >= 11)
                        {
                            posX = posXByte0 | (posRemain[0] << 8) | (posRemain[1] << 16) | (posRemain[2] << 24);
                            posY = posRemain[3] | (posRemain[4] << 8) | (posRemain[5] << 16) | (posRemain[6] << 24);
                            posZ = posRemain[7] | (posRemain[8] << 8) | (posRemain[9] << 16) | (posRemain[10] << 24);
                        }
                        float fPosX = posX / 256f;
                        float fPosY = posY / 256f;
                        float fPosZ = posZ / 256f;
                        Debug.LogError($"[SPELL-0x51] action=usePosition manip={manipulatorId} actionId={actionID} pos=({fPosX:F1},{fPosY:F1},{fPosZ:F1})");

                        PlayerState state = componentId < 50000 ? GetPlayerState(conn.ConnId.ToString()) : null;
                        var resolvedSpell = state != null ? ResolveActionSpell(conn, state, actionID) : null;
                        if (resolvedSpell != null && IsActiveSkillBusy(conn, componentId, actionID, resolvedSpell, out float skillBusyRemaining))
                        {
                            Debug.LogError($"[SPELL-BUSY] UsePosition client-busy actionID={actionID} spell={resolvedSpell.DisplayName ?? resolvedSpell.SkillId ?? "UNKNOWN"} remaining={skillBusyRemaining:F3}s not-using sourceFunction=ActiveSkill::isBusy@0x005394F0 timer=ActiveSkill+0x7e");
                            if (reader.Remaining >= 1)
                                TryConsumeClientEntitySynchInfoSuffix(conn, reader, "ACTION-0x51-BUSY-ENTITY-SYNCH-INFO");
                            return;
                        }

                        float positionDistance = 0f;
                        float positionRange = 0f;
                        if (state != null && resolvedSpell != null &&
                            !IsSpellPositionWithinServerRange(conn, resolvedSpell, fPosX, fPosY, out positionDistance, out positionRange))
                        {
                            Debug.LogError($"[SPELL-0x51] action=checkInitUse state=outsideRange spell={resolvedSpell.DisplayName ?? resolvedSpell.SkillId ?? "UNKNOWN"} dist={positionDistance:F1} range={positionRange:F1} pos=({fPosX:F1},{fPosY:F1}) sourceFunction=UsePosition::CheckInitUse@0x00547850 result=no-use");
                            if (reader.Remaining >= 1)
                                TryConsumeClientEntitySynchInfoSuffix(conn, reader, "ACTION-0x51-RANGE-ENTITY-SYNCH-INFO");
                            return;
                        }

                        var positionActionResponseMessage = new LEWriter();
                        positionActionResponseMessage.WriteByte(0x35);
                        positionActionResponseMessage.WriteUInt16(componentId);
                        positionActionResponseMessage.WriteByte(0x01);
                        positionActionResponseMessage.WriteByte(responseId);
                        positionActionResponseMessage.WriteByte(0x51);
                        positionActionResponseMessage.WriteByte(manipulatorId);
                        positionActionResponseMessage.WriteByte(actionID);
                        positionActionResponseMessage.WriteUInt32((uint)posX);
                        positionActionResponseMessage.WriteUInt32((uint)posY);
                        positionActionResponseMessage.WriteUInt32((uint)posZ);
                        if (!TryWriteEntitySynchForComponent(conn, positionActionResponseMessage, componentId, 0x01, EntitySynchInfoContext.PlayerActionResponse, "PlayerActionResponse", false))
                        {
                            Debug.LogError($"[SPELL-0x51] state=failed target=actionResponseEntitySynchInfo component={componentId} actionId={actionID}");
                            return;
                        }
                        ClearZoneSpawnInvulnerability(conn, $"ACTION-0x{actionType:X2}");
                        conn.MessageQueue.Enqueue(positionActionResponseMessage.ToArray());
                        StartActiveSkillBusy(conn, componentId, actionID, resolvedSpell, state);
                        Debug.LogError("[SPELL-0x51] action=enqueue target=actionResponse queue=MessageQueue");


                        if (componentId < 50000)
                        {
                            if (state != null)
                            {
                                Debug.LogError($"[SPELL-0x51] actionId={actionID} spell={resolvedSpell?.DisplayName ?? "NOT RESOLVED"} sessionCtr={manipulatorId} pos=({fPosX:F1},{fPosY:F1})");

                                var nearest = ResolvePositionSpellTarget(conn, resolvedSpell, fPosX, fPosY, out float projectileHitDistance);
                                if (resolvedSpell != null && resolvedSpell.ProjectileSize > 0f && resolvedSpell.ProjectileSpeed > 0f)
                                {
                                    if (nearest != null && nearest.IsAlive)
                                        nearest.UseTargetCount++;
                                    float startX = conn.PlayerPosX;
                                    float startY = conn.PlayerPosY;
                                    float hitHint = nearest != null && nearest.IsAlive
                                        ? projectileHitDistance
                                        : 0f;
                                    var pending = CreatePendingSpellProjectile(conn, state, nearest, resolvedSpell, actionID, actionID, componentId, startX, startY, fPosX, fPosY, nearest == null || !nearest.IsAlive, hitHint);
                                    _pendingSpells.Enqueue(pending);
                                    Debug.LogError($"[SPELL-0x51] action=queueProjectile spell={resolvedSpell?.DisplayName ?? resolvedSpell?.SkillId ?? "UNKNOWN"} slotId={actionID} initialTarget={(nearest != null ? nearest.Name + "#" + nearest.EntityId.ToString() : "none")} delay={pending.ProjectileDelay:F3}s hitHint={pending.ProjectileHitDistance:F2} speed={pending.ProjectileSpeed:F1} step={pending.StepDistance:F3} initPreStep={pending.InitialDistance:F3} maxDist={pending.MaxDistance:F2} seq={pending.Sequence}");
                                }
                                else
                                {
                                    if (nearest != null && nearest.IsAlive)
                                    {
                                        nearest.UseTargetCount++;
                                        if (projectileHitDistance <= 0f)
                                        {
                                            ResolveMonsterClientVisiblePosition(nearest, out float weaponAimX, out float weaponAimY);
                                            projectileHitDistance = Distance2D(conn.PlayerPosX, conn.PlayerPosY, weaponAimX, weaponAimY);
                                        }
                                        float projectileDelay = ResolveProjectileImpactDelay(resolvedSpell, state, projectileHitDistance);
                                        float dueTime = GetCombatNow() + ResolveActiveSkillTriggerFrames(resolvedSpell, state) * COMBAT_TICK + projectileDelay;
                                        _pendingSpells.Enqueue(new PendingSpell { Conn = conn, State = state, Monster = nearest, Spell = resolvedSpell, ManipId = actionID, UseFlags = actionID, ComponentId = componentId, StartX = conn.PlayerPosX, StartY = conn.PlayerPosY, AimX = fPosX, AimY = fPosY, InstanceKey = GetInstanceZoneKey(conn), DueTime = dueTime, ProjectileHitDistance = projectileHitDistance, ProjectileDelay = projectileDelay });
                                        Debug.LogError($"[SPELL-0x51] action=queueDamage target='{nearest.Name}' slotId={actionID} delay={projectileDelay:F3}s hitDist={projectileHitDistance:F2} speed={resolvedSpell?.ProjectileSpeed ?? 0f:F1}");
                                    }
                                    else
                                    {
                                        Debug.LogError($"[SPELL-0x51] state=noTarget projectile=false spell={resolvedSpell?.DisplayName ?? resolvedSpell?.SkillId ?? "UNKNOWN"} slotId={actionID} aim=({fPosX:F1},{fPosY:F1})");
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        Debug.LogError($"[ACTION] Unhandled actionType=0x{actionType:X2} target={targetEntityID}");
                    }

                    if (reader.Remaining >= 1)
                    {
                        TryConsumeClientEntitySynchInfoSuffix(conn, reader, "ACTION-ENTITY-SYNCH-INFO");
                    }

                }
                else if (subMessage == 0x64)
                {
                    Debug.LogError($"[COMPONENT] Client sent 0x64 response - consuming data");
                    HandleClientControlResponse(conn, reader, componentId);
                }
                else if ((subMessage == 0x35 || subMessage == 0x36))
                {
                    string connKey = conn.ConnId.ToString();
                    SavedCharacter savedChar = GetActiveCharacter(conn);
                    if (savedChar == null) { while (reader.Remaining > 0) reader.ReadByte(); return; }
                    if (savedChar.hotbarSlots == null) savedChar.hotbarSlots = new List<HotbarSlotEntry>();
                    if (!_playerManipMap.ContainsKey(connKey)) _playerManipMap[connKey] = new Dictionary<uint, string>();

                    if (subMessage == 0x36 && reader.Remaining >= 4)
                    {
                        uint slot = reader.ReadUInt32();
                        Debug.LogError($"[HOTBAR] REMOVE slot {slot}");
                        string removedSkill = null;
                        if (_playerManipMap[connKey].ContainsKey(slot))
                        {
                            removedSkill = _playerManipMap[connKey][slot];
                            _playerManipMap[connKey].Remove(slot);
                        }
                        var removedSlotEntry = savedChar.hotbarSlots.FirstOrDefault(h => h.slot == slot);
                        if (removedSkill == null && removedSlotEntry != null)
                            removedSkill = removedSlotEntry.skill;
                        savedChar.hotbarSlots.RemoveAll(h => h.slot == slot);
                        if (removedSkill != null)
                        {
                            savedChar.hotbarSlots.RemoveAll(h => string.Equals(h.skill, removedSkill, StringComparison.OrdinalIgnoreCase));
                            foreach (var dupSlot in _playerManipMap[connKey].Where(kv => string.Equals(kv.Value, removedSkill, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList())
                                _playerManipMap[connKey].Remove(dupSlot);
                        }
                        CharacterRepository.SaveCharacter(savedChar);
                        Debug.LogError($"[HOTBAR] Saved remove: slot {slot}, was '{removedSkill}'");

                        if (removedSkill != null &&
                            (removedSkill.IndexOf("BlingGnome", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             removedSkill.IndexOf("SummonBling", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            bool stillOnBar = savedChar.hotbarSlots.Any(h =>
                                h.skill.IndexOf("BlingGnome", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                h.skill.IndexOf("SummonBling", StringComparison.OrdinalIgnoreCase) >= 0);
                            if (!stillOnBar && BlingGnomeRuntime.Instance.HasGnome(conn.ConnId))
                            {
                                Debug.LogError($"[HOTBAR] BlingGnome removed from tray - despawning for {conn.LoginName}");
                                BlingGnomeRuntime.Instance.DespawnGnome(conn,
                                    (c, d, t, b) => SendCompressedA(c, d, t, b));
                            }
                        }

                        ushort manipulatorsComponentId = 0;
                        if (_playerManipulatorsIds.TryGetValue(connKey, out ushort resolvedManipulatorsComponentId))
                            manipulatorsComponentId = resolvedManipulatorsComponentId;
                        if (manipulatorsComponentId != 0 && removedSkill != null)
                        {
                            var hotbarRemoveMessage = new LEWriter();
                            hotbarRemoveMessage.WriteByte(0x07);
                            hotbarRemoveMessage.WriteByte(0x35);
                            hotbarRemoveMessage.WriteUInt16(manipulatorsComponentId);
                            hotbarRemoveMessage.WriteByte(0x01);
                            hotbarRemoveMessage.WriteUInt32(slot);
                            lock (conn.SendLock)
                            {
                                if (IsPassiveSkill(removedSkill))
                                    RecalculateHotbarPassiveBonuses(conn, savedChar, keepPvpPassive: true);
                                WritePlayerEntitySynch(conn, hotbarRemoveMessage);
                                hotbarRemoveMessage.WriteByte(0x06);
                                SendCompressedE(conn, hotbarRemoveMessage.ToArray());
                            }
                            Debug.LogError($"[HOTBAR-MANIP] Sent Remove slot={slot} '{removedSkill}'");
                        }
                        else if (IsPassiveSkill(removedSkill))
                        {
                            RecalculateHotbarPassiveBonuses(conn, savedChar, keepPvpPassive: true);
                        }
                    }
                    else if (subMessage == 0x35 && reader.Remaining >= 9)
                    {
                        uint slot = reader.ReadUInt32();
                        byte typeFlag = reader.ReadByte();
                        uint gcHash = reader.ReadUInt32();
                        Debug.LogError($"[HOTBAR] PLACE slot {slot} hash=0x{gcHash:X8}");

                        string skillGcClass = null;
                        if (_skillHashToGcClass.TryGetValue(gcHash, out string resolvedSkillGcClass))
                            skillGcClass = resolvedSkillGcClass;
                        if (skillGcClass == null)
                            foreach (var manipulatorMapEntry in _playerManipMap[connKey])
                            {
                                uint h = 5381;
                                foreach (char c in manipulatorMapEntry.Value.ToLower()) h = ((h << 5) + h) + (uint)c;
                                if (h == gcHash) { skillGcClass = manipulatorMapEntry.Value; break; }
                            }

                        if (skillGcClass != null)
                        {
                            string displacedSkill = null;
                            if (_playerManipMap[connKey].ContainsKey(slot))
                            {
                                string existing = _playerManipMap[connKey][slot];
                                if (!string.Equals(existing, skillGcClass, StringComparison.OrdinalIgnoreCase))
                                    displacedSkill = existing;
                            }
                            uint? oldSlot = null;
                            foreach (var manipulatorMapEntry in _playerManipMap[connKey])
                                if (string.Equals(manipulatorMapEntry.Value, skillGcClass, StringComparison.OrdinalIgnoreCase) && manipulatorMapEntry.Key != slot)
                                { oldSlot = manipulatorMapEntry.Key; break; }

                            if (oldSlot.HasValue) _playerManipMap[connKey].Remove(oldSlot.Value);
                            if (displacedSkill != null) _playerManipMap[connKey].Remove(slot);
                            _playerManipMap[connKey][slot] = skillGcClass;

                            savedChar.hotbarSlots.RemoveAll(h => h.slot == slot || string.Equals(h.skill, skillGcClass, StringComparison.OrdinalIgnoreCase));
                            if (displacedSkill != null)
                                savedChar.hotbarSlots.RemoveAll(h => string.Equals(h.skill, displacedSkill, StringComparison.OrdinalIgnoreCase));
                            savedChar.hotbarSlots.Add(new HotbarSlotEntry { slot = slot, skill = skillGcClass });
                            CharacterRepository.SaveCharacter(savedChar);
                            Debug.LogError($"[HOTBAR] Saved: slot {slot} = '{skillGcClass}' displaced='{displacedSkill}' oldSlot={oldSlot}");

                            if (skillGcClass.IndexOf("BlingGnome", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                skillGcClass.IndexOf("SummonBling", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                BlingGnomeRuntime.Instance.SetServer(this);
                                if (!BlingGnomeRuntime.Instance.HasGnome(conn.ConnId))
                                {
                                    Debug.LogError($"[HOTBAR] BlingGnome placed on tray - spawning for {conn.LoginName}");
                                    BlingGnomeRuntime.Instance.SpawnGnome(conn,
                                        (c, d, t, b) => SendCompressedA(c, d, t, b),
                                        (c, m) => SendSystemMessage(c, m));
                                }
                            }
                            else if (displacedSkill != null &&
                                (displacedSkill.IndexOf("BlingGnome", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 displacedSkill.IndexOf("SummonBling", StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                bool stillOnBar = savedChar.hotbarSlots.Any(h =>
                                    h.skill.IndexOf("BlingGnome", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    h.skill.IndexOf("SummonBling", StringComparison.OrdinalIgnoreCase) >= 0);
                                if (!stillOnBar && BlingGnomeRuntime.Instance.HasGnome(conn.ConnId))
                                {
                                    Debug.LogError($"[HOTBAR] BlingGnome displaced from tray - despawning for {conn.LoginName}");
                                    BlingGnomeRuntime.Instance.DespawnGnome(conn,
                                        (c, d, t, b) => SendCompressedA(c, d, t, b));
                                }
                            }

                            ushort manipulatorsComponentId = 0;
                            if (_playerManipulatorsIds.TryGetValue(connKey, out ushort resolvedManipulatorsComponentId))
                                manipulatorsComponentId = resolvedManipulatorsComponentId;

                            if (manipulatorsComponentId != 0)
                            {
                                byte skillLevel = (byte)Math.Min(byte.MaxValue, Math.Max(1, savedChar.GetSkillLevel(skillGcClass)));
                                bool skillIsPassive = IsPassiveSkill(skillGcClass);

                                var hotbarAddMessage = new LEWriter();
                                hotbarAddMessage.WriteByte(0x07);
                                hotbarAddMessage.WriteByte(0x35);
                                hotbarAddMessage.WriteUInt16(manipulatorsComponentId);
                                hotbarAddMessage.WriteByte(0x00);
                                hotbarAddMessage.WriteByte(0xFF);
                                hotbarAddMessage.WriteCString(skillGcClass.ToLowerInvariant());
                                hotbarAddMessage.WriteUInt32(slot);
                                hotbarAddMessage.WriteByte(skillLevel);
                                lock (conn.SendLock)
                                {
                                    if (skillIsPassive || IsPassiveSkill(displacedSkill))
                                        RecalculateHotbarPassiveBonuses(conn, savedChar, keepPvpPassive: true);
                                    WritePlayerEntitySynch(conn, hotbarAddMessage);
                                    hotbarAddMessage.WriteByte(0x06);
                                    SendCompressedE(conn, hotbarAddMessage.ToArray());
                                }
                                Debug.LogError($"[HOTBAR-MANIP] Sent Add '{skillGcClass}' slot={slot} level={skillLevel} manipulatorsComponent=0x{manipulatorsComponentId:X4}");
                            }
                            else if (IsPassiveSkill(skillGcClass) || IsPassiveSkill(displacedSkill))
                            {
                                RecalculateHotbarPassiveBonuses(conn, savedChar, keepPvpPassive: true);
                            }
                        }
                        else
                        {
                            Debug.LogError($"[HOTBAR] Could not resolve hash 0x{gcHash:X8} to any known skill");
                        }
                    }
                    else { while (reader.Remaining > 0) reader.ReadByte(); }
                }
                else if (subMessage == 0x21 || subMessage == 0x22 || subMessage == 0x23 || subMessage == 0x25 || subMessage == 0x26 || subMessage == 0x27 || subMessage == 0x28 || subMessage == 0x29)
                {
                    Debug.LogError($"[COMPONENT] Detected inventory/equipment operation 0x{subMessage:X2}");

                    string componentType = GetComponentType(conn.ConnId.ToString(), componentId);
                    Debug.LogError($"[COMPONENT-ROUTE] ComponentID 0x{componentId:X4} routed to: {componentType}");
                    if (componentType == "Equipment")
                    {
                        _equipment.ProcessRequest(conn, reader, componentId, subMessage);
                    }
                    else if (componentType == "UnitContainer")
                    {
                        int unitContainerPayloadRemaining = reader.Remaining;
                        _unitContainer.ProcessRequest(conn, reader, componentId, subMessage);
                        if (subMessage == 0x22 && unitContainerPayloadRemaining == 0)
                        {
                            MerchantRuntime.FlushClientMerchantRefreshOnClientBoundary(conn, SendCompressedA);
                        }
                    }
                    else
                    {
                        Debug.LogError($"[COMPONENT-ROUTE]  Unknown component type for ID 0x{componentId:X4}!");
                    }
                }
                else if (subMessage == 0x1E)
                {
                    bool isMerchant = false;
                    foreach (var zoneNpcEntry in _zoneNPCs)
                    {
                        foreach (var npc in zoneNpcEntry.Value)
                        {
                            if (npc.IsMerchant && npc.MerchantId == componentId)
                            {
                                isMerchant = true;
                                break;
                            }
                        }
                        if (isMerchant) break;
                    }

                    if (isMerchant)
                    {
                        byte[] peek = reader.PeekRemaining();
                        Debug.LogError($"[MERCHANT] RAW BYTES (remaining {peek.Length}): {BitConverter.ToString(peek)}");

                        byte buyByte1 = reader.ReadByte();
                        byte buyByte2 = reader.ReadByte();
                        uint itemId = reader.ReadUInt32();
                        Debug.LogError($"[MERCHANT]  BUY REQUEST! componentId=0x{componentId:X4}, byte1=0x{buyByte1:X2}({buyByte1}), byte2=0x{buyByte2:X2}({buyByte2}), itemId={itemId}");
                        MerchantRuntime.HandleBuyItem(conn, componentId, (ushort)itemId, _zoneNPCs, _selectedCharacter, SendCompressedA, this, buyByte1, buyByte2, IsPlayerFree(conn.LoginName));
                    }
                }
                else if (subMessage == 0x1F)
                {
                    bool isMerchant = false;
                    foreach (var zoneNpcEntry in _zoneNPCs)
                    {
                        foreach (var npc in zoneNpcEntry.Value)
                        {
                            if (npc.IsMerchant && npc.MerchantId == componentId)
                            {
                                isMerchant = true;
                                break;
                            }
                        }
                        if (isMerchant) break;
                    }

                    if (isMerchant)
                    {
                        byte[] sellPeek = reader.PeekRemaining();
                        Debug.LogError($"[MERCHANT] SELL RAW BYTES (remaining {sellPeek.Length}): {BitConverter.ToString(sellPeek)}");
                        ushort entityRef = reader.ReadUInt16();
                        uint itemId = reader.ReadUInt32();
                        Debug.LogError($"[MERCHANT]  SELL REQUEST! componentId=0x{componentId:X4}, entityRef=0x{entityRef:X4}, itemId={itemId}, remaining after read={reader.Remaining}");
                        MerchantRuntime.HandleSellItem(conn, componentId, (ushort)itemId, entityRef, _zoneNPCs, _selectedCharacter, SendCompressedA, GetPlayerState(conn.ConnId.ToString()), this, 0);
                        if (reader.Remaining > 0)
                        {
                            int drained = 0;
                            while (reader.Remaining > 0) { reader.ReadByte(); drained++; }
                            Debug.LogError($"[MERCHANT] Drained {drained} leftover sell bytes");
                        }
                    }
                }
                else
                {
                    ZoneNPC trainerNpc = null;
                    foreach (var zoneNpcEntry in _zoneNPCs)
                    {
                        foreach (var npc in zoneNpcEntry.Value)
                        {
                            if (npc.IsTrainer && npc.TrainerId == componentId)
                            {
                                trainerNpc = npc;
                                break;
                            }
                        }
                        if (trainerNpc != null) break;
                    }

                    if (trainerNpc != null)
                    {
                        byte[] trainerRaw = reader.PeekRemaining();
                        Debug.LogError($"[TRAINER] action=skillTrainerMessage npc='{trainerNpc.Name}' componentId=0x{componentId:X4} subMessage=0x{subMessage:X2} remaining={trainerRaw.Length} hex={BitConverter.ToString(trainerRaw)}");
                        HandleSkillTrainRequest(conn, reader, componentId, subMessage, trainerNpc);
                    }
                    else
                    {
                        string skillConnKey = conn.ConnId.ToString();
                        ushort playerSkillsCid = 0;
                        _playerSkillsComponentId.TryGetValue(skillConnKey, out playerSkillsCid);

                        if (playerSkillsCid != 0 && componentId == playerSkillsCid && subMessage == 0x39)
                        {
                            byte[] rawData = reader.PeekRemaining();
                            Debug.LogError($"[SKILL-EQUIP] 0x39 on Skills cid=0x{componentId:X4} remaining={rawData.Length} hex={BitConverter.ToString(rawData)}");

                            string skillGcClass = "";
                            byte slotByte = 0;
                            try
                            {
                                if (reader.Remaining > 0)
                                {
                                    byte refType = reader.ReadByte();
                                    if (refType == 0xFF && reader.Remaining > 0)
                                        skillGcClass = reader.ReadCString();
                                    else if (reader.Remaining >= 2)
                                    {
                                        ushort refId = reader.ReadUInt16();
                                        Debug.LogError($"[SKILL-EQUIP] entityIdRef={refId}");
                                    }
                                }
                                if (reader.Remaining > 0)
                                    slotByte = reader.ReadByte();

                                if (reader.Remaining > 0)
                                {
                                    byte entitySynchInfoFlags = reader.ReadByte();
                                    if ((entitySynchInfoFlags & 0x02) != 0 && reader.Remaining >= 4)
                                        reader.ReadUInt32();
                                }
                            }
                            catch (Exception parseEx)
                            {
                                Debug.LogError($"[SKILL-EQUIP] parse state=failed message='{parseEx.Message}'");
                                while (reader.Remaining > 0) reader.ReadByte();
                            }

                            Debug.LogError($"[SKILL-EQUIP] skill='{skillGcClass}' slot={slotByte}");

                            Debug.LogError($"[SKILL-EQUIP] state=accepted skill='{skillGcClass}' slot={slotByte} response=none source=clientAssigned");
                        }
                        else if (subMessage == 0x07 && componentId == conn.QuestManagerId)
                        {
                            uint cpHash = 0;
                            if (reader.Remaining >= 1)
                            {
                                byte tag = reader.ReadByte();
                                if (tag == 0x04 && reader.Remaining >= 4) cpHash = reader.ReadUInt32();
                                else if (tag == 0x02 && reader.Remaining >= 2) cpHash = reader.ReadUInt16();
                                else if (tag == 0x01 && reader.Remaining >= 1) cpHash = reader.ReadByte();
                            }
                            while (reader.Remaining > 0) reader.ReadByte();

                            string destZone = null;
                            string connId = conn.ConnId.ToString();
                            var playerState = QuestManager.Instance.GetPlayerState(connId);
                            if (playerState != null)
                            {
                                foreach (var cpId in playerState.UnlockedCheckpoints)
                                {
                                    if (DatabaseLoader.ComputeDJB2Hash(cpId) == cpHash)
                                    {
                                        var cp = DatabaseLoader.Checkpoints.FirstOrDefault(c =>
                                            c.id.Equals(cpId, StringComparison.OrdinalIgnoreCase));
                                        if (cp != null)
                                            destZone = cp.zone;
                                        else if (_checkpointZoneMap.TryGetValue(cpId, out string mz))
                                            destZone = mz;
                                        Debug.LogError($"[CP-TELEPORT] Hash 0x{cpHash:X8} -> '{cpId}' -> zone '{destZone}'");
                                        break;
                                    }
                                }
                            }

                            if (!string.IsNullOrEmpty(destZone))
                            {
                                var zone = _zones.Values.FirstOrDefault(z =>
                                    z.name.Equals(destZone, StringComparison.OrdinalIgnoreCase));
                                if (zone != null)
                                {
                                    conn.PendingSpawnX = zone.spawnX;
                                    conn.PendingSpawnY = zone.spawnY;
                                    conn.PendingSpawnZ = zone.spawnZ;
                                }
                                ChangeZone(conn, destZone, "");
                            }
                            else
                            {
                                Debug.LogError($"[CP-TELEPORT] hash=0x{cpHash:X8} state=notMatched action=rotatorFallback");
                                HandleObeliskTeleport(conn);
                            }
                        }
                        else if (subMessage == 0x0C && componentId == conn.QuestManagerId)
                        {
                            while (reader.Remaining > 0) reader.ReadByte();
                            if (!string.IsNullOrEmpty(conn.ZonePortalSource))
                            {
                                Debug.LogError($"[ZONE-PORTAL] action=teleport zone='{conn.ZonePortalSource}'");
                                var zone = _zones.Values.FirstOrDefault(z =>
                                    z.name.Equals(conn.ZonePortalSource, StringComparison.OrdinalIgnoreCase));
                                if (zone != null)
                                {
                                    conn.PendingSpawnX = zone.spawnX;
                                    conn.PendingSpawnY = zone.spawnY;
                                    conn.PendingSpawnZ = zone.spawnZ;
                                }
                                ChangeZone(conn, conn.ZonePortalSource, "");
                            }
                            else
                                Debug.LogError("[ZONE-PORTAL] state=missing source=zonePortal");
                        }
                        else
                        {
                            Debug.LogError($"[COMPONENT] sub=0x{subMessage:X2} cid=0x{componentId:X4} state=unknown");
                            int drained = 0;
                            while (reader.Remaining > 0) { reader.ReadByte(); drained++; }
                            if (drained > 0)
                                Debug.LogError($"[COMPONENT] drained={drained} reason=drainRemaining");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[COMPONENT] state=failed message='{ex.Message}'");
            }
        }

        private void HandleMonsterStateMachineUpdate(RRConnection conn, LEReader reader, ushort componentId)
        {
            if (reader.Remaining < 1) return;
            if (VerbosePacketLogging) Debug.LogError($"[MONSTER-SM-RAW] cid={componentId} offset={(componentId - 50000) % 6} remaining={reader.Remaining} hex={BitConverter.ToString(reader.PeekRemaining())}");

            int componentOffset = (componentId - 50000) % 6;

            ushort messageType = 0xFFFF;
            ushort scope = 0xFFFF;
            ushort target = 0;
            uint value = 0;
            byte flags = 0;

            if (componentOffset == 2)
            {
                flags = reader.ReadByte();
                if ((flags & 0x02) != 0 && reader.Remaining >= 2)
                    messageType = reader.ReadUInt16();
                if ((flags & 0x04) != 0 && reader.Remaining >= 2)
                    scope = reader.ReadUInt16();
                if ((flags & 0x08) != 0 && reader.Remaining >= 2)
                    target = reader.ReadUInt16();
                if ((flags & 0x20) != 0 && reader.Remaining >= 4)
                    value = reader.ReadUInt32();
                if ((flags & 0x10) != 0 && reader.Remaining >= 2)
                {
                    ushort extraWordCount = reader.ReadUInt16();
                    for (int wordIndex = 0; wordIndex < extraWordCount && reader.Remaining >= 2; wordIndex++)
                        reader.ReadUInt16();
                }
            }
            else if (componentOffset == 1)
            {
                flags = reader.ReadByte();
            }
            else
            {
                flags = reader.ReadByte();
            }

            uint entitySynchInfoHP = 0;
            bool hasEntitySynchInfoHP = false;
            if (reader.Remaining >= 1)
            {
                byte entitySynchInfoFlags = reader.ReadByte();
                if (entitySynchInfoFlags != 0 && (entitySynchInfoFlags & 0x02) != 0 && reader.Remaining >= 4)
                {
                    entitySynchInfoHP = reader.ReadUInt32();
                    hasEntitySynchInfoHP = true;
                }
            }

            string messageName = messageType switch
            {
                0 => "Halt",
                1 => "GoToPrevious",
                2 => "Go",
                3 => "Arrive",
                4 => "CheckDest",
                5 => "Wait/LeaveCombat",
                6 => "Timer",
                7 => "ReturnHome",
                8 => "CombatTick",
                9 => "AGGRO",
                10 => "SecondaryTarget",
                11 => "Forget",
                12 => "Fidget/GOAGGRO",
                13 => "CombatAck/ServerAggro",
                0x925 => "DEAD (0x925)",
                0x9D5 => "DEAD (0x9D5)",
                _ => $"Unknown(0x{messageType:X4})"
            };

            string hpStr = hasEntitySynchInfoHP ? $" HP={entitySynchInfoHP}({entitySynchInfoHP / 256})" : "";
            if (VerbosePacketLogging) Debug.LogError($"[MONSTER-SM] cid={componentId} offset={componentOffset} flags=0x{flags:X2} message={messageType}({messageName}) scope={scope} target={target} value={value}{hpStr}");

            var monster = CombatRuntime.Instance.GetMonsterByComponent(componentId)
                       ?? CombatRuntime.Instance.GetMonsterByBehaviorId(componentId)
                       ?? CombatRuntime.Instance.GetMonsterBySkillsId(componentId)
                       ?? CombatRuntime.Instance.GetMonsterByManipulatorsId(componentId);

            if (monster == null)
            {
                if (VerbosePacketLogging) Debug.LogError($"[MONSTER-SM] No monster found for cid={componentId}");
                return;
            }

            if (hasEntitySynchInfoHP)
            {
                uint clientHPWire = entitySynchInfoHP;
                CombatRuntime.Instance.ObserveClientMonsterHP(monster, clientHPWire, "MONSTER-SM-HP-0x64");
            }

            if (messageType == 0x9D5 || messageType == 0x925)
            {
                Debug.LogError($"[MONSTER-DEATH] {monster.Name} DEATH SIGNAL message=0x{messageType:X4} from client!");
                if (!_finalizedMonsterKills.Contains(monster.EntityId))
                {
                    TryFinalizeMonsterKill(conn, monster, $"DeathMessage-0x{messageType:X4}");
                }
                return;
            }

            if (messageType == 9)
            {
                if (VerbosePacketLogging) Debug.LogError($"[MONSTER-SM] {monster.Name} AGGRO! value={value} target={target}");
                uint targetId = target != 0 ? target : (uint)(conn.Avatar?.Id ?? 0);
                if (targetId != 0)
                {
                    CombatRuntime.Instance.EngageMonsterFromClientAction(monster, targetId);
                }
            }
            else if (messageType == 13)
            {
                if (VerbosePacketLogging) Debug.LogError($"[MONSTER-SM] CombatAck value={value} target={target}");
                monster.State = MonsterState.Combat;
                if (target != 0) monster.TargetId = target;
                string instanceKey = !string.IsNullOrWhiteSpace(monster.InstanceKey) ? monster.InstanceKey : monster.ZoneName;
                uint roomSeed = CombatRuntime.Instance.GetRoomSeedForInstance(instanceKey);
                bool ready = CombatRuntime.Instance.TryGetRoomRuntime(instanceKey, out var runtime) && runtime.Initialized;
                Debug.LogError($"[RNG-SEED] Skipped CombatAck reseed for {monster.Name}#{monster.EntityId} target={target} instance='{instanceKey}' current=0x{roomSeed:X8} ready={ready}");
                try { ApplyMonsterDebuffs(conn, monster); } catch (Exception ex) { Debug.LogError($"[DEBUFF] state=failed message='{ex.Message}'"); }
            }
            else if (messageType == 10)
            {
                monster.State = MonsterState.Combat;
                if (target != 0) monster.TargetId = target;
                try { ApplyMonsterDebuffs(conn, monster); } catch (Exception ex) { Debug.LogError($"[DEBUFF] state=failed message='{ex.Message}'"); }
            }
            else if (messageType == 8)
            {
                try { ApplyMonsterDebuffs(conn, monster); } catch (Exception ex) { Debug.LogError($"[DEBUFF] state=failed message='{ex.Message}'"); }
            }
            else if (messageType == 12)
            {
                if (monster.TargetId == 0 && conn.Avatar != null)
                    monster.TargetId = (uint)conn.Avatar.Id;
                monster.State = MonsterState.Combat;
            }

        }


        public void SendSkillAttackUDP(RRConnection conn, ushort skillsComponentId, uint targetEntityId)
        {
            var session = GetUDPSessionForConnection(conn);
            if (session == null || !session.IsEstablished) return;
            if (!ResolveEntitySynchInfoForComponent(conn, skillsComponentId, 0x64, EntitySynchInfoContext.MonsterAction, 0, "UDP-SKILL", false, out EntitySynchInfoDecision decision))
                return;

            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x35);
            writer.WriteUInt16(skillsComponentId);
            writer.WriteByte(0x64);
            writer.WriteByte(0x03);
            writer.WriteUInt16(0x08);
            writer.WriteUInt16(0xFFFF);
            writer.WriteUInt32(0);
            writer.WriteUInt16((ushort)targetEntityId);
            if (!TryWriteResolvedEntitySynchInfo(writer, skillsComponentId, 0x64, EntitySynchInfoContext.MonsterAction, "UDP-SKILL", decision))
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
            Debug.LogError($"[UDP-SKILL]  Sent Type 8 CombatTick to skillsId={skillsComponentId} target={targetEntityId}");
        }

        public void SendDespawnEntity(RRConnection conn, ushort entityId)
        {
            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x05);
            writer.WriteUInt16(entityId);
            writer.WriteByte(0x06);
            SendToClient(conn, writer.ToArray());
            Debug.LogError($"[DESPAWN] Sent despawn for entity {entityId}");
        }



        private void SendClientControlReset(RRConnection conn, ushort componentId)
        {
            Debug.LogError($"[CONTROL] SendClientControlReset componentId=0x{componentId:X4}");

            var controlResetMessage = new LEWriter();
            if (!WriteClientControlUpdate(conn, controlResetMessage, componentId, false) ||
                !WriteClientControlUpdate(conn, controlResetMessage, componentId, true))
            {
                Debug.LogError($"[CONTROL] Dropped client control reset componentId=0x{componentId:X4}");
                ScheduleClientControlResetRetry(conn, componentId, "write-blocked");
                return;
            }

            conn.MessageQueue.Enqueue(controlResetMessage.ToArray());
            ClearPendingClientControlReset(conn, componentId);
            Debug.LogError($"[CONTROL] Sent client control reset");
        }

        private void ScheduleClientControlResetRetry(RRConnection conn, ushort componentId, string reason)
        {
            if (conn == null || componentId == 0 || !conn.IsConnected) return;
            conn.PendingClientControlReset = true;
            conn.PendingClientControlResetComponentId = componentId;
            conn.PendingClientControlResetAttempts = (byte)Math.Min(255, conn.PendingClientControlResetAttempts + 1);
            float delay = conn.PendingClientControlResetAttempts <= 3 ? 0.10f : conn.PendingClientControlResetAttempts <= 10 ? 0.25f : 1.00f;
            conn.PendingClientControlResetNextAttemptTime = Time.time + delay;
            Debug.LogError($"[CONTROL] Scheduled client control reset retry componentId=0x{componentId:X4} attempt={conn.PendingClientControlResetAttempts} reason={reason} delay={delay:F2}");
        }

        private void ClearPendingClientControlReset(RRConnection conn, ushort componentId)
        {
            if (conn == null || !conn.PendingClientControlReset) return;
            if (componentId != 0 && conn.PendingClientControlResetComponentId != componentId) return;
            conn.PendingClientControlReset = false;
            conn.PendingClientControlResetComponentId = 0;
            conn.PendingClientControlResetNextAttemptTime = 0f;
            conn.PendingClientControlResetAttempts = 0;
        }

        private void FlushPendingClientControlResets()
        {
            foreach (var conn in _connections.Values)
            {
                if (conn == null || !conn.PendingClientControlReset) continue;
                if (!conn.IsConnected)
                {
                    ClearPendingClientControlReset(conn, 0);
                    continue;
                }
                if (Time.time < conn.PendingClientControlResetNextAttemptTime) continue;
                ushort componentId = conn.PendingClientControlResetComponentId;
                if (componentId == 0)
                {
                    ClearPendingClientControlReset(conn, 0);
                    continue;
                }
                Debug.LogError($"[CONTROL] Retrying pending client control reset componentId=0x{componentId:X4} attempt={conn.PendingClientControlResetAttempts}");
                SendClientControlReset(conn, componentId);
            }
        }

        private bool WriteClientControlUpdate(RRConnection conn, LEWriter writer, ushort componentId, bool followClient)
        {
            return WriteClientControlUpdate(conn, writer, componentId, followClient, "CLIENT-CONTROL", null);
        }

        private bool WriteClientControlUpdate(RRConnection conn, LEWriter writer, ushort componentId, bool followClient, string packetName, uint? forcedHPWire)
        {
            writer.WriteByte(0x35);
            writer.WriteUInt16(componentId);
            writer.WriteByte(0x64);
            writer.WriteByte(followClient ? (byte)0x01 : (byte)0x00);
            if (forcedHPWire.HasValue)
            {
                GetValidationCutoff(out uint validationCutoffTick, out float validationCutoffTime);
                uint avatarEntityId = conn?.Avatar != null ? (uint)conn.Avatar.Id : 0u;
                return TryWriteResolvedEntitySynchInfo(writer, componentId, 0x64, EntitySynchInfoContext.ControlAck, packetName, EntitySynchInfoDecision.HP(EntitySynchInfoOwner.Avatar, forcedHPWire.Value, packetName, avatarEntityId, componentId, 0x64, GetCombatNow(), $"forced-player-hp; validationCutoffTick={validationCutoffTick} validationCutoffTime={validationCutoffTime:F3}", validationCutoffTick, validationCutoffTime));
            }
            return TryWriteEntitySynchForComponent(conn, writer, componentId, 0x64, EntitySynchInfoContext.ControlAck, packetName, true);
        }



        private void HandleNPCClick(RRConnection conn, ushort componentId, ushort targetEntityID, byte responseId, byte sessionID)
        {
            Debug.LogError($"[NPC] ");
            Debug.LogError($"[NPC] Activate target=0x{targetEntityID:X4} responseId={responseId} sessionID={sessionID}");
            conn.SessionID = sessionID;

            bool approachStarted = false;
            if (_zoneNPCs.TryGetValue(conn.CurrentZoneId, out var npcs))
            {
                var npc = npcs.FirstOrDefault(n => n.Id == targetEntityID);
                if (npc != null)
                {
                    conn.CurrentDialogNpcId = npc.GCClass;
                    StartUseTargetMoving(conn, npc.PosX, npc.PosY);
                    approachStarted = true;
                    if (npc.IsMerchant)
                    {
                        TrackPendingMerchantActivation(conn, npc);
                    }
                    if (npc.IsPosseMagnate)
                    {
                        SendSystemMessage(conn, "Open the Posse tab in your menu, or type /posse create <name> to start a posse (level 15+, 1,000,000 gold). Type /posse help for the full list of commands.");
                        Debug.LogError($"[POSSE] Player clicked PosseMagnate {npc.GCClass} - chat hint sent");
                    }
                    Debug.LogError($"[NPC]  Set CurrentDialogNpcId = {conn.CurrentDialogNpcId}");
                }
            }

            if (!approachStarted && !string.IsNullOrEmpty(conn.LoginName) && _remoteBehaviorIds.TryGetValue(conn.LoginName, out var remoteMap))
            {
                foreach (var remoteEntry in remoteMap)
                {
                    if (remoteEntry.Value != targetEntityID) continue;
                    var targetConn = _connections.Values.FirstOrDefault(c => c != null && string.Equals(c.LoginName, remoteEntry.Key, StringComparison.OrdinalIgnoreCase));
                    if (targetConn != null && targetConn.IsSpawned)
                        StartUseTargetMoving(conn, targetConn.PlayerPosX, targetConn.PlayerPosY, targetConn.ConnId);
                    break;
                }
            }

            var npcActivateResponseMessage = new LEWriter();
            npcActivateResponseMessage.WriteByte(0x35);
            npcActivateResponseMessage.WriteUInt16(componentId);
            npcActivateResponseMessage.WriteByte(0x01);
            npcActivateResponseMessage.WriteByte(responseId);
            npcActivateResponseMessage.WriteByte(0x06);
            npcActivateResponseMessage.WriteByte(sessionID);
            npcActivateResponseMessage.WriteUInt16(targetEntityID);
            WritePlayerEntitySynch(conn, npcActivateResponseMessage);
            conn.MessageQueue.Enqueue(npcActivateResponseMessage.ToArray());
            Debug.LogError($"[NPC]  Queued activate response");

            if (BlingGnomeRuntime.Instance.HasGnome(conn.ConnId))
            {
                uint gnomeEntityId = BlingGnomeRuntime.Instance.GetGnomeEntityId(conn.ConnId);
                if (targetEntityID == gnomeEntityId)
                {
                    Debug.LogError("[NPC] blingGnome clicked");
                    Debug.LogError("[NPC] blingGnome greetingSound=120 asset=Bling_Gnomes_Summon_01-04");
                }
            }
        }

        private void TrackPendingMerchantActivation(RRConnection conn, ZoneNPC npc)
        {
            conn.PendingMerchantNpcGcClass = npc.GCClass;
            conn.PendingMerchantComponentId = (ushort)npc.MerchantId;
            conn.PendingMerchantTargetX = npc.PosX;
            conn.PendingMerchantTargetY = npc.PosY;

            if (IsPendingMerchantReached(conn))
                ActivatePendingMerchantRefresh(conn);
            else
            {
                Debug.LogError($"[NPC] Pending merchant refresh activation for {npc.GCClass} at ({npc.PosX:F1},{npc.PosY:F1})");
            }
        }

        private void CheckPendingMerchantActivation(RRConnection conn)
        {
            if (string.IsNullOrWhiteSpace(conn.PendingMerchantNpcGcClass))
                return;

            if (!IsPendingMerchantReached(conn))
                return;

            ActivatePendingMerchantRefresh(conn);
        }

        private bool IsPendingMerchantReached(RRConnection conn)
        {
            float dx = conn.PlayerPosX - conn.PendingMerchantTargetX;
            float dy = conn.PlayerPosY - conn.PendingMerchantTargetY;
            return dx * dx + dy * dy <= MERCHANT_ACTIVATION_DISTANCE_SQ;
        }

        private void ActivatePendingMerchantRefresh(RRConnection conn)
        {
            string npcGcType = conn.PendingMerchantNpcGcClass;
            ushort componentId = conn.PendingMerchantComponentId;

            conn.PendingMerchantNpcGcClass = null;
            conn.PendingMerchantComponentId = 0;

            MerchantRuntime.ScheduleClientMerchantRefresh(conn, npcGcType, componentId, DateTime.UtcNow);
            Debug.LogError($"[NPC] Activated client merchant refresh schedule for {npcGcType}");
        }

        private bool SendUseTargetActionResponse(RRConnection conn, ushort componentId, byte responseId, byte manipulatorId, byte useFlags, ushort targetId, EntitySynchInfoContext actionResponseContext, string actionResponseEntitySynchInfoTag, string reason)
        {
            if (conn == null || componentId == 0)
                return false;

            var actionResponseMessage = new LEWriter();
            actionResponseMessage.WriteByte(0x35);
            actionResponseMessage.WriteUInt16(componentId);
            actionResponseMessage.WriteByte(0x01);
            actionResponseMessage.WriteByte(responseId);
            actionResponseMessage.WriteByte(0x50);
            actionResponseMessage.WriteByte(manipulatorId);
            actionResponseMessage.WriteByte(useFlags);
            actionResponseMessage.WriteUInt16(targetId);

            if (!TryWriteEntitySynchForComponent(conn, actionResponseMessage, componentId, 0x01, actionResponseContext, actionResponseEntitySynchInfoTag, true))
                return false;

            conn.MessageQueue.Enqueue(actionResponseMessage.ToArray());
            bool sent = true;
            if (sent)
            {
                PlayerState state = GetPlayerState(conn.ConnId.ToString());
                Debug.LogError($"[ATTACK] action=sendActionResponse reason={reason} target={targetId} component={componentId} responseId={responseId} manip={manipulatorId} flags={useFlags} playerHp={state?.EntitySynchInfoHP ?? 0}");
            }
            return sent;
        }

        private bool HandlePlayerAttackPlayer(RRConnection conn, ushort componentId, byte responseId, byte manipulatorId, byte useFlags, ushort targetId)
        {
            if (conn == null || string.IsNullOrEmpty(conn.LoginName) || targetId == 0)
                return false;
            if (!IsPvpZone(conn.CurrentZoneName))
                return false;
            if (!_remoteAvatarIds.TryGetValue(conn.LoginName, out var avatarMap))
                return false;

            string targetLogin = null;
            foreach (var entry in avatarMap)
            {
                if (entry.Value == targetId) { targetLogin = entry.Key; break; }
            }
            if (targetLogin == null)
                return false;

            var targetConn = _connections.Values.FirstOrDefault(c => c != null && string.Equals(c.LoginName, targetLogin, StringComparison.OrdinalIgnoreCase));
            if (targetConn == null || !targetConn.IsSpawned || targetConn.Avatar == null)
                return false;
            if (targetConn.InstanceId != conn.InstanceId)
                return false;

            PlayerState attackerState = GetPlayerState(conn.ConnId.ToString());
            if (attackerState == null)
                return false;
            if (!Combat.CombatRuntime.Instance.TryGetCombatPlayer((uint)targetConn.Avatar.Id, out var targetPlayer))
                return false;

            string instanceKey = GetInstanceZoneKey(conn);
            bool resolved = Combat.CombatRuntime.Instance.ResolvePlayerVsPlayerDamageWire(attackerState, attackerState.Level, conn.LoginName, targetPlayer, instanceKey, $"PvpWeapon flags={useFlags}", out uint oldHPWire, out uint newHPWire, out bool killed);
            if (!resolved)
                return false;

            SendUseTargetActionResponse(conn, componentId, responseId, manipulatorId, useFlags, targetId, EntitySynchInfoContext.PlayerActionResponse, "PvpPlayerActionResponse", "pvp-attack-player");

            if (newHPWire != oldHPWire)
                SendRemoteAvatarHPUpdates(targetConn);

            Debug.LogError($"[PVP-ATTACK] attacker={conn.LoginName} target={targetLogin}#{targetId} flags={useFlags} hp={oldHPWire}->{newHPWire} killed={killed}");
            return true;
        }

        private void HandlePlayerAttackMonster(RRConnection conn, ushort componentId, byte responseId,
     byte manipulatorId, byte useFlags, ushort targetId, Combat.Monster monster, bool acceptedActionMonsterHP)
        {
            Debug.LogError($"[ATTACK] action=useTarget monster='{monster.Name}' targetId={targetId} sessionCtr={manipulatorId} slotId={useFlags} responseId={responseId}");
            bool isSkillAction = useFlags >= 100;
            bool inAttackRange = true;
            float attackDistance = 0f;
            float attackRange = 0f;
            float clientAttackRange = 0f;
            float clientInitUseRange = 0f;
            float clientInitUseDistance = 0f;
            float initUseTolerance = 0f;
            string initUseSource = "unknown";
            bool clientInitUsePassed = false;
            bool clientMeleeContact = false;
            bool clientRangedBasic = false;
            bool clientRangedProjectileBasic = false;
            bool canStartWeaponUseState = false;
            float weaponUseRange = 0f;
            bool activatedUseTargetBeforeSuffix = false;
            PlayerState state = null;
            Combat.SpellData actionSpell = null;
            if (monster.IsAlive)
                state = GetPlayerState(conn.ConnId.ToString());
            string responseKey = conn != null ? $"{conn.ConnId}:{targetId}:{useFlags}" : "";
            bool redundantBasicAttack = monster.IsAlive && !isSkillAction && IsRedundantUseTarget(conn, targetId, useFlags);
            bool zoneInvulnerabilityBlocking = IsZoneSpawnInvulnerabilityBlockingCombat(conn);
            bool hasLastUseTargetResponse = false;
            float repeatedUseTargetElapsed = float.MaxValue;
            bool coalesceRedundantBasicUseTarget = false;
            if (zoneInvulnerabilityBlocking)
            {
                ClearZoneSpawnInvulnerability(conn, $"ACTION-0x50 flags={useFlags} target={targetId}");
                zoneInvulnerabilityBlocking = false;
            }
            if (redundantBasicAttack && conn != null)
            {
                hasLastUseTargetResponse = _useTargetResponseTimes.TryGetValue(responseKey, out float lastResponse);
                repeatedUseTargetElapsed = hasLastUseTargetResponse ? Time.time - lastResponse : float.MaxValue;
                string ackAge = hasLastUseTargetResponse ? repeatedUseTargetElapsed.ToString("F2") : "none";
                Debug.LogError($"[ATTACK] action=repeatedUseTargetAck monster='{monster.Name}' target={targetId} flags={useFlags} lastResponseAge={ackAge} started={conn.ActiveUseTargetStartedWeaponUse} init={conn.ActiveUseTargetInitUsePassed} sourceFunction=UseTarget::IsRedundant");
                coalesceRedundantBasicUseTarget = true;
            }
            if (monster.IsAlive && !zoneInvulnerabilityBlocking)
            {
                if (Combat.CombatRuntime.Instance.IsMonsterDeathPendingClientConfirmation(monster))
                {
                    Debug.LogError($"[ATTACK] target={targetId} state=pendingClientDeathConfirmation action=releaseUseTarget hp={CombatRuntime.Instance.PeekMonsterCurrentHPWire(monster)}");
                    ClearUseTargetAndReleaseControl(conn, "ATTACK-death-pending", componentId);
                    return;
                }
                clientRangedBasic = !isSkillAction && state != null && Combat.DamageResolver.IsRangedWeapon(state);
                clientRangedProjectileBasic = clientRangedBasic && Combat.DamageResolver.IsProjectileWeapon(state);
                if (!isSkillAction)
                {
                    inAttackRange = clientRangedBasic
                        ? IsRangedBasicTargetWithinServerRange(conn, state, monster, out attackDistance, out attackRange)
                        : IsMeleeTargetWithinServerRange(conn, state, monster, out attackDistance, out attackRange);
                }
                else
                {
                    actionSpell = ResolveActionSpell(conn, state, useFlags);
                    inAttackRange = IsSkillTargetWithinServerRange(conn, actionSpell, monster, out attackDistance, out attackRange);
                }
                if (!isSkillAction)
                {
                    clientInitUseRange = Combat.CombatRuntime.Instance.ResolveUseTargetInitUseRange(state, monster, out initUseTolerance, out initUseSource);
                    ResolveMonsterClientVisiblePosition(monster, out float initTargetX, out float initTargetY);
                    clientInitUsePassed = Combat.CombatRuntime.Instance.EvaluateUseTargetInitUse(
                        conn.PlayerPosX, conn.PlayerPosY, initTargetX, initTargetY,
                        clientInitUseRange, initUseTolerance, out clientInitUseDistance,
                        out _, out _);
                    if (clientRangedProjectileBasic)
                    {
                        clientAttackRange = clientInitUseRange;
                        clientMeleeContact = clientInitUsePassed;
                        attackDistance = clientInitUseDistance;
                    }
                    else
                    {
                        clientAttackRange = clientRangedBasic
                            ? clientInitUseRange
                            : Combat.CombatRuntime.Instance.ResolvePlayerMeleeContactRange(state, monster);
                        clientMeleeContact = clientAttackRange > 0f && attackDistance <= clientAttackRange + CLIENT_CONTACT_RANGE_EPSILON;
                    }
                }
                if (!isSkillAction && state != null)
                {
                    float playerX = conn != null ? conn.PlayerPosX : 0f;
                    float playerY = conn != null ? conn.PlayerPosY : 0f;
                    string lane = clientRangedBasic ? "ranged" : "melee";
                    float projectileReach = clientRangedProjectileBasic ? WeaponUseRuntime.ProjectileRadiusFromAuthoredSize(state.WeaponProjectileSize) + Mathf.Max(0f, state.WeaponProjectileSpeed) * COMBAT_TICK : 0f;
                    ResolveMonsterClientVisiblePosition(monster, out float targetX, out float targetY);
                    Debug.LogError($"[ATTACK-RANGE] lane={lane} weaponClass={state.WeaponClass} weaponRange={state.WeaponRange} weaponSpeed={state.WeaponSpeed:F1} useProjectile={state.WeaponUsesProjectile} projectileSpeed={state.WeaponProjectileSpeed:F1} projectileSize={state.WeaponProjectileSize:F1} projectileReach={projectileReach:F1} player=({playerX:F1},{playerY:F1}) monster=({targetX:F1},{targetY:F1}) dist={attackDistance:F1} range={attackRange:F1} clientRange={clientAttackRange:F1} initUseRange={clientInitUseRange:F1} initUse={clientInitUsePassed} inRange={inAttackRange} clientContact={clientMeleeContact} flags={useFlags} target={targetId}");
                }
                if (!isSkillAction)
                {
                    canStartWeaponUseState = clientRangedProjectileBasic
                        ? (inAttackRange && clientInitUsePassed)
                        : (inAttackRange || clientMeleeContact);
                    weaponUseRange = clientRangedProjectileBasic ? clientInitUseRange : clientAttackRange;
                    if (coalesceRedundantBasicUseTarget)
                    {
                        if (conn != null)
                        {
                            conn.ActiveUseTargetInitUsePassed = clientInitUsePassed;
                            conn.ActiveUseTargetInitUseRange = clientInitUseRange;
                            conn.ActiveUseTargetInitUseDistance = clientInitUseDistance;
                            conn.ActiveUseTargetClientTolerance = initUseTolerance;
                        }
                        if (state != null)
                        {
                            string redundantAtkKey = conn.ConnId.ToString();
                            Combat.WeaponUseRuntime.Instance.RegisterAttack(redundantAtkKey, targetId, monster, state, conn, canStartWeaponUseState, attackDistance, weaponUseRange);
                        }
                        Debug.LogError($"[ATTACK]  redundant UseTarget coalesced target={targetId} flags={useFlags} sessionCtr={manipulatorId} activeSession={conn.ActiveUseTargetSessionId} lastResponseAge={repeatedUseTargetElapsed:F2} started={conn.ActiveUseTargetStartedWeaponUse} init={conn.ActiveUseTargetInitUsePassed} canStart={canStartWeaponUseState} dist={attackDistance:F1} range={weaponUseRange:F1} sourceFunction=UseTarget::IsRedundant+Behavior::doActionLocal action=keep-current-UseTarget mirror=weapon-use noTimingFloor=True");
                        if (SendUseTargetActionResponse(conn, componentId, responseId, manipulatorId, useFlags, targetId, EntitySynchInfoContext.PlayerBasicAttackResponse, "PlayerBasicAttackResponse", "redundant-useTarget") && !string.IsNullOrEmpty(responseKey))
                            _useTargetResponseTimes[responseKey] = Time.time;
                        return;
                    }
                    ActivateUseTarget(conn, targetId, useFlags, componentId, manipulatorId);
                    activatedUseTargetBeforeSuffix = true;
                    if (conn != null)
                    {
                        conn.ActiveUseTargetInitUsePassed = clientInitUsePassed;
                        conn.ActiveUseTargetInitUseRange = clientInitUseRange;
                        conn.ActiveUseTargetInitUseDistance = clientInitUseDistance;
                        conn.ActiveUseTargetClientTolerance = initUseTolerance;
                    }
                }
                if (conn != null && monster.IsAlive)
                {
                    bool moving = isSkillAction ? !inAttackRange : !canStartWeaponUseState;
                    ResolveMonsterClientVisiblePosition(monster, out float moveTargetX, out float moveTargetY);
                    if (moving)
                        StartUseTargetMoving(conn, moveTargetX, moveTargetY, 0, (ushort)monster.EntityId);
                    else
                        CancelUseTargetMoving(conn, "use-ready");
                }
                if (state != null && conn?.Avatar != null)
                {
                    if (!isSkillAction)
                        Combat.CombatRuntime.Instance.SetPlayerActiveClientAttack((uint)conn.Avatar.Id, true, monster.EntityId);
                    bool clientWeaponUseStarted = !isSkillAction && clientRangedProjectileBasic && clientInitUsePassed;
                    Combat.CombatRuntime.Instance.EngageMonsterFromClientAction(monster, (uint)conn.Avatar.Id, clientWeaponUseStarted);
                }
                if (state != null)
                {
                    string atkKey = conn.ConnId.ToString();
                    var oldTarget = Combat.WeaponUseRuntime.Instance.GetActiveTarget(atkKey);
                    bool targetSwitch = oldTarget != null && oldTarget.EntityId != monster.EntityId;
                    if (targetSwitch)
                    {
                        Debug.LogError($"[ATTACK] Target switch from {oldTarget.Name} to {monster.Name}");
                    }
                    monster.UseTargetCount++;
                    if (isSkillAction)
                    {
                        if (inAttackRange)
                        {
                            float projectileHitDistance = Mathf.Max(0f, attackDistance);
                            if (actionSpell != null && actionSpell.ProjectileSize > 0f && actionSpell.ProjectileSpeed > 0f)
                            {
                                ResolveMonsterClientVisiblePosition(monster, out float targetX, out float targetY);
                                var pending = CreatePendingSpellProjectile(conn, state, monster, actionSpell, useFlags, useFlags, componentId, conn.PlayerPosX, conn.PlayerPosY, targetX, targetY, false, projectileHitDistance);
                                _pendingSpells.Enqueue(pending);
                                Debug.LogError($"[SPELL] UseTarget projectile runtime: slotId={useFlags} on {monster.Name} delay={pending.ProjectileDelay:F3}s hitHint={pending.ProjectileHitDistance:F2} speed={pending.ProjectileSpeed:F1} step={pending.StepDistance:F3} initPreStep={pending.InitialDistance:F3} maxDist={pending.MaxDistance:F2} seq={pending.Sequence}");
                            }
                            else
                            {
                                float projectileDelay = ResolveProjectileImpactDelay(actionSpell, state, projectileHitDistance);
                                float dueTime = projectileDelay > 0f ? GetCombatNow() + ResolveActiveSkillTriggerFrames(actionSpell, state) * COMBAT_TICK + projectileDelay : 0f;
                                Debug.LogError($"[SPELL] UseTarget spell: slotId={useFlags} on {monster.Name} queued delay={projectileDelay:F3}s hitDist={projectileHitDistance:F2} speed={actionSpell?.ProjectileSpeed ?? 0f:F1}");
                                ResolveMonsterClientVisiblePosition(monster, out float targetX, out float targetY);
                                _pendingSpells.Enqueue(new PendingSpell { Conn = conn, State = state, Monster = monster, Spell = actionSpell, ManipId = useFlags, UseFlags = useFlags, ComponentId = componentId, StartX = conn.PlayerPosX, StartY = conn.PlayerPosY, AimX = targetX, AimY = targetY, InstanceKey = GetInstanceZoneKey(conn), DueTime = dueTime, ProjectileHitDistance = projectileHitDistance, ProjectileDelay = projectileDelay });
                            }
                        }
                        else
                        {
                            Debug.LogError($"[SPELL] Target outside range: {actionSpell?.DisplayName ?? "UNKNOWN"} on {monster.Name} dist={attackDistance:F1} range={attackRange:F1}");
                        }
                    }
                    else
                    {
                        string weaponLane = clientRangedBasic ? "RangedWeapon" : "Melee";
                        if (clientRangedProjectileBasic)
                        {
                            Combat.WeaponUseRuntime.Instance.RegisterAttack(atkKey, targetId, monster, state, conn, canStartWeaponUseState, attackDistance, weaponUseRange);
                            Debug.LogError($"[ATTACK] {weaponLane} UseTarget armed: slotId={useFlags} on {monster.Name} sessionCtr={manipulatorId} dist={attackDistance:F1} initUseRange={clientInitUseRange:F1} weaponRange={state.WeaponRange:F1} contactRange={weaponUseRange:F1} actionRange={attackRange:F1} currentInitUseWouldPass={clientInitUsePassed} inInitRange={inAttackRange} processedByUseTargetTick={canStartWeaponUseState} rngAdvanced=False hpMutated=False");
                        }
                        else
                        {
                            Debug.LogError($"[ATTACK] {weaponLane}: slotId={useFlags} on {monster.Name} sessionCtr={manipulatorId} dist={attackDistance:F1} range={attackRange:F1} clientRange={clientAttackRange:F1} canStart={canStartWeaponUseState}");
                            Combat.WeaponUseRuntime.Instance.RegisterAttack(atkKey, targetId, monster, state, conn, canStartWeaponUseState, attackDistance, weaponUseRange);
                        }
                    }
                }
                else
                {
                    Debug.LogError($"[COMBAT] no PlayerState for conn={conn.ConnId}; skipping damage");
                }
            }

            var attackResponseMessage = new LEWriter();
            attackResponseMessage.WriteByte(0x35);
            attackResponseMessage.WriteUInt16(componentId);
            attackResponseMessage.WriteByte(0x01);
            attackResponseMessage.WriteByte(responseId);
            attackResponseMessage.WriteByte(0x50);
            attackResponseMessage.WriteByte(manipulatorId);
            attackResponseMessage.WriteByte(useFlags);
            attackResponseMessage.WriteUInt16(targetId);
            EntitySynchInfoContext actionResponseContext = isSkillAction ? EntitySynchInfoContext.PlayerActionResponse : EntitySynchInfoContext.PlayerBasicAttackResponse;
            string actionResponseEntitySynchInfoTag = isSkillAction ? "PlayerActionResponse" : "PlayerBasicAttackResponse";
            if (!activatedUseTargetBeforeSuffix && !zoneInvulnerabilityBlocking && !isSkillAction && monster.IsAlive)
            {
                ActivateUseTarget(conn, targetId, useFlags, componentId, manipulatorId);
                activatedUseTargetBeforeSuffix = true;
            }
            if (!TryWriteEntitySynchForComponent(conn, attackResponseMessage, componentId, 0x01, actionResponseContext, actionResponseEntitySynchInfoTag, true))
            {
                Debug.LogError($"[ATTACK] state=failed target=actionResponseEntitySynchInfo component={componentId} targetId={targetId} flags={useFlags} action=releaseFallback");
                ClearUseTargetAndReleaseControl(conn, "ATTACK-entity-synch-info-failed", componentId);
                return;
            }
            conn.MessageQueue.Enqueue(attackResponseMessage.ToArray());
            if (!zoneInvulnerabilityBlocking && conn?.Avatar != null && monster.IsAlive)
                Combat.CombatRuntime.Instance.SetPlayerActiveClientAttack((uint)conn.Avatar.Id, true, monster.EntityId);
            if (!string.IsNullOrEmpty(responseKey))
                _useTargetResponseTimes[responseKey] = Time.time;
            Debug.LogError($"[ATTACK] action=sendActionResponse alive={monster.IsAlive} playerHp={state?.EntitySynchInfoHP ?? 0} targetHp={CombatRuntime.Instance.PeekMonsterCurrentHPWire(monster)}/{monster.MaxHPWire}");
            if (!activatedUseTargetBeforeSuffix && !zoneInvulnerabilityBlocking && !isSkillAction && monster.IsAlive)
                ActivateUseTarget(conn, targetId, useFlags, componentId, manipulatorId);
            else if (!monster.IsAlive && IsUseTargetingMonster(conn, monster))
                ClearUseTargetAndReleaseControl(conn, "ATTACK-target-dead", componentId);
        }

        private static float ResolveBasicAttackResponseInterval(PlayerState state)
        {
            int ticks = Combat.DamageResolver.ResolveBasicAttackCooldownTicks(state);
            return ticks / 30f;
        }

        private static float ResolveBasicAttackHitDelay(PlayerState state)
        {
            float speed = state != null ? state.WeaponSpeed : 105f;
            float speedPct = Combat.DamageResolver.ResolveWeaponAttackSpeedPct(state);
            float speedScale = 1f + (speedPct / 100f);
            if (speedScale < 0.05f) speedScale = 0.05f;
            speed *= speedScale;
            if (speed <= 1f) speed = 105f;
            int speedField = Math.Max(1, Mathf.RoundToInt(speed));
            int hitTicks = Math.Max(1, (15 * 100) / speedField);
            return hitTicks / 30f;
        }

        private static string GetActiveSkillBusyKey(RRConnection conn, ushort componentId, byte actionId)
        {
            int connId = conn != null ? conn.ConnId : 0;
            return $"{connId}:{componentId}:{actionId}";
        }

        private static float ResolveActiveSkillBusySeconds(Combat.SpellData spell, PlayerState state)
        {
            if (spell == null) return 0f;
            int repeatCount = Mathf.Max(0, spell.RepeatCount);
            if (repeatCount <= 0) return 0f;
            int animationFrames = Combat.SpellDatabase.TryResolvePlayerAnimationTiming(spell.AnimationId, state, out int frames, out _) && frames > 0
                ? frames
                : Mathf.Max(1, spell.AnimationLengthFrames);
            return repeatCount * animationFrames * COMBAT_TICK;
        }

        private static float ResolveSpellDamageClientVisibleTime(Combat.SpellData spell, bool isAoETarget, float clientEffectTime)
        {
            if (spell == null || !isAoETarget || clientEffectTime < 0f)
                return -1f;
            return clientEffectTime;
        }

        private bool IsActiveSkillBusy(RRConnection conn, ushort componentId, byte actionId, Combat.SpellData spell, out float remaining)
        {
            remaining = 0f;
            if (spell == null) return false;
            string key = GetActiveSkillBusyKey(conn, componentId, actionId);
            if (!_activeSkillBusyUntil.TryGetValue(key, out float busyUntil))
                return false;

            remaining = busyUntil - Time.time;
            if (remaining > 0f)
                return true;

            _activeSkillBusyUntil.Remove(key);
            remaining = 0f;
            return false;
        }

        private void StartActiveSkillBusy(RRConnection conn, ushort componentId, byte actionId, Combat.SpellData spell, PlayerState state)
        {
            float busySeconds = ResolveActiveSkillBusySeconds(spell, state);
            if (busySeconds <= 0f) return;
            _activeSkillBusyUntil[GetActiveSkillBusyKey(conn, componentId, actionId)] = Time.time + busySeconds;
        }

        private static bool IsRedundantUseTarget(RRConnection conn, ushort targetId, byte useFlags)
        {
            if (conn == null || !conn.HasActiveUseTarget || conn.ActiveUseTargetId != targetId)
                return false;
            byte activeFlags = conn.ActiveUseTargetFlags;
            bool activeBasicAttack = activeFlags == 0x0A || activeFlags == 0x0B;
            bool incomingBasicAttack = useFlags == 0x0A || useFlags == 0x0B;
            return (activeBasicAttack && incomingBasicAttack) || activeFlags == useFlags;
        }

        private static void ActivateUseTarget(RRConnection conn, ushort targetId, byte useFlags, ushort componentId = 0, byte sessionId = 0)
        {
            if (conn == null) return;
            bool sameBasicTarget = conn.HasActiveUseTarget
                && conn.ActiveUseTargetId == targetId
                && IsBasicMeleeUseTargetFlag(conn.ActiveUseTargetFlags)
                && IsBasicMeleeUseTargetFlag(useFlags);
            bool startedWeaponUse = sameBasicTarget && conn.ActiveUseTargetStartedWeaponUse;
            bool initUsePassed = sameBasicTarget && conn.ActiveUseTargetInitUsePassed;
            bool visibleHit = sameBasicTarget && conn.ActiveUseTargetVisibleHit;
            float initUseRange = sameBasicTarget ? conn.ActiveUseTargetInitUseRange : 0f;
            float initUseDistance = sameBasicTarget ? conn.ActiveUseTargetInitUseDistance : 0f;
            float tolerance = sameBasicTarget ? conn.ActiveUseTargetClientTolerance : 0f;
            long lastProjectileSeq = sameBasicTarget ? conn.ActiveUseTargetLastProjectileSeq : 0;
            int lastImpactTick = sameBasicTarget ? conn.ActiveUseTargetLastImpactTick : -1;

            conn.HasActiveUseTarget = true;
            conn.ActiveUseTargetId = targetId;
            conn.ActiveUseTargetFlags = useFlags;
            conn.ActiveUseTargetComponentId = componentId;
            conn.ActiveUseTargetSessionId = sessionId;
            conn.ActiveUseTargetStartedWeaponUse = startedWeaponUse;
            conn.ActiveUseTargetInitUsePassed = initUsePassed;
            conn.ActiveUseTargetVisibleHit = visibleHit;
            conn.ActiveUseTargetInitUseRange = initUseRange;
            conn.ActiveUseTargetInitUseDistance = initUseDistance;
            conn.ActiveUseTargetClientTolerance = tolerance;
            conn.ActiveUseTargetLastProjectileSeq = lastProjectileSeq;
            conn.ActiveUseTargetLastImpactTick = lastImpactTick;
        }

        private static void ClearUseTarget(RRConnection conn)
        {
            if (conn == null) return;
            conn.HasActiveUseTarget = false;
            conn.ActiveUseTargetId = 0;
            conn.ActiveUseTargetFlags = 0;
            conn.ActiveUseTargetComponentId = 0;
            conn.ActiveUseTargetSessionId = 0;
            conn.ActiveUseTargetInitUsePassed = false;
            conn.ActiveUseTargetStartedWeaponUse = false;
            conn.ActiveUseTargetVisibleHit = false;
            conn.ActiveUseTargetInitUseRange = 0f;
            conn.ActiveUseTargetInitUseDistance = 0f;
            conn.ActiveUseTargetClientTolerance = 0f;
            conn.ActiveUseTargetLastProjectileSeq = 0;
            conn.ActiveUseTargetLastImpactTick = -1;
        }

        private static ushort ResolveClientControlComponentId(RRConnection conn, ushort componentId)
        {
            if (componentId != 0) return componentId;
            if (conn == null) return 0;
            if (conn.UnitBehaviorId != 0 && conn.UnitBehaviorId <= ushort.MaxValue)
                return (ushort)conn.UnitBehaviorId;
            return conn.BehaviorComponentId;
        }

        private void ClearUseTargetAndReleaseControl(RRConnection conn, string source = "unknown", ushort componentId = 0, bool sendClientControlReset = true, bool requireActiveUseTargetForReset = false)
        {
            if (conn == null) return;
            bool hadUseTarget = conn.HasActiveUseTarget;
            ushort targetId = conn.ActiveUseTargetId;
            byte sessionId = conn.ActiveUseTargetSessionId;
            ushort controlComponentId = ResolveClientControlComponentId(conn, componentId);
            CancelUseTargetMoving(conn, source);
            ClearUseTarget(conn);
            Combat.WeaponUseRuntime.Instance.ClearConnection(conn.ConnId.ToString());
            if (conn.Avatar != null)
                Combat.CombatRuntime.Instance.SetPlayerActiveClientAttack((uint)conn.Avatar.Id, false);

            if (sendClientControlReset && (!requireActiveUseTargetForReset || hadUseTarget) && controlComponentId != 0 && conn.IsConnected)
            {
                Debug.LogError($"[CONTROL] Release UseTarget source={source} target={targetId} hadUseTarget={hadUseTarget} componentId=0x{controlComponentId:X4}");
                SendClientControlReset(conn, controlComponentId);
            }
            else
            {
                Debug.LogError($"[CONTROL] Release UseTarget source={source} target={targetId} hadUseTarget={hadUseTarget} no-reset componentId=0x{controlComponentId:X4} connected={conn.IsConnected}");
            }
        }

        private void TryClearUseTargetAndReleaseControl(RRConnection conn, string source)
        {
            if (conn == null) return;
            try
            {
                ClearUseTargetAndReleaseControl(conn, source);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CONTROL-ERROR] {source}: {ex.Message}\n{ex.StackTrace}");
                ClearUseTarget(conn);
            }
        }

        private void ReleaseCompletedUseTargets()
        {
            foreach (var conn in _connections.Values)
                ReleaseCompletedUseTarget(conn);
        }

        private void ReleaseCompletedUseTarget(RRConnection conn)
        {
            if (conn == null || !conn.HasActiveUseTarget) return;
            var monster = CombatRuntime.Instance.GetMonster(conn.ActiveUseTargetId)
                       ?? CombatRuntime.Instance.GetMonsterByComponent(conn.ActiveUseTargetId);
            if (monster == null || !monster.IsAlive || CombatRuntime.Instance.PeekMonsterCurrentHPWire(monster) == 0 || CombatRuntime.Instance.IsMonsterDeathPendingClientConfirmation(monster))
            {
                string state = monster == null ? "missing" : $"alive={monster.IsAlive} hp={CombatRuntime.Instance.PeekMonsterCurrentHPWire(monster)}";
                Debug.LogError($"[CONTROL] Releasing completed UseTarget target={conn.ActiveUseTargetId} {state}");
                ClearUseTargetAndReleaseControl(conn, "ReleaseCompletedUseTarget");
            }
        }

        private bool IsMeleeTargetWithinServerRange(RRConnection conn, PlayerState state, Combat.Monster monster, out float distance, out float allowedRange)
        {
            distance = 0f;
            allowedRange = 0f;
            if (conn == null || monster == null) return false;
            allowedRange = CombatRuntime.Instance.ResolvePlayerMeleeRange(state, monster);
            ResolveMonsterClientVisiblePosition(monster, out float targetX, out float targetY);
            float dx = targetX - conn.PlayerPosX;
            float dy = targetY - conn.PlayerPosY;
            distance = Mathf.Sqrt(dx * dx + dy * dy);
            return distance <= allowedRange + CLIENT_CONTACT_RANGE_EPSILON;
        }

        private bool IsRangedProjectileTargetWithinServerRange(RRConnection conn, PlayerState state, Combat.Monster monster, out float distance, out float allowedRange)
        {
            distance = 0f;
            allowedRange = 0f;
            if (conn == null || monster == null) return false;
            allowedRange = CombatRuntime.Instance.ResolveUseTargetInitUseRange(state, monster, out _, out _);
            ResolveMonsterClientVisiblePosition(monster, out float targetX, out float targetY);
            float dx = targetX - conn.PlayerPosX;
            float dy = targetY - conn.PlayerPosY;
            distance = Mathf.Sqrt(dx * dx + dy * dy);
            return distance <= allowedRange + CLIENT_CONTACT_RANGE_EPSILON;
        }

        private bool IsRangedBasicTargetWithinServerRange(RRConnection conn, PlayerState state, Combat.Monster monster, out float distance, out float allowedRange)
        {
            return IsRangedProjectileTargetWithinServerRange(conn, state, monster, out distance, out allowedRange);
        }

        private Combat.SpellData ResolveActionSpell(RRConnection conn, PlayerState state, byte manipulatorId)
        {
            Combat.SpellDatabase.Initialize();
            var spell = ResolveSpellFromManip(conn, manipulatorId);
            if (spell == null)
                Debug.LogError($"[SPELL] action=resolveActionSpell manipId={manipulatorId} state=missing");
            return spell;
        }

        private bool IsSkillTargetWithinServerRange(RRConnection conn, Combat.SpellData spell, Combat.Monster monster, out float distance, out float allowedRange)
        {
            distance = 0f;
            allowedRange = 0f;
            if (conn == null || monster == null || spell == null || spell.Range <= 0) return false;
            allowedRange = Mathf.Max(1f, spell.Range) + 14f;
            ResolveMonsterClientVisiblePosition(monster, out float targetX, out float targetY);
            float dx = targetX - conn.PlayerPosX;
            float dy = targetY - conn.PlayerPosY;
            distance = Mathf.Sqrt(dx * dx + dy * dy);
            return distance <= allowedRange;
        }

        private bool IsSpellPositionWithinServerRange(RRConnection conn, Combat.SpellData spell, float posX, float posY, out float distance, out float allowedRange)
        {
            distance = 0f;
            allowedRange = 0f;
            if (conn == null || spell == null || spell.Range <= 0) return false;
            allowedRange = Mathf.Max(1f, spell.Range) + 14f;
            float dx = posX - conn.PlayerPosX;
            float dy = posY - conn.PlayerPosY;
            distance = Mathf.Sqrt(dx * dx + dy * dy);
            return distance <= allowedRange;
        }

        private Combat.Monster ResolvePositionSpellTarget(RRConnection conn, Combat.SpellData spell, float posX, float posY, out float projectileHitDistance)
        {
            projectileHitDistance = 0f;
            if (spell != null && spell.ProjectileSize > 0f)
                return FindFirstProjectileMonsterHit(conn, spell, posX, posY, out projectileHitDistance);

            return Combat.CombatRuntime.Instance.GetNearestMonster(posX, posY, 50f, GetInstanceZoneKey(conn));
        }

        private Combat.Monster ResolvePositionSpellTargetFromStart(RRConnection conn, Combat.SpellData spell, float startX, float startY, float posX, float posY, out float projectileHitDistance)
        {
            projectileHitDistance = 0f;
            if (spell != null && spell.ProjectileSize > 0f)
                return FindFirstProjectileMonsterHitFromStart(conn, spell, startX, startY, posX, posY, out projectileHitDistance);

            return Combat.CombatRuntime.Instance.GetNearestMonster(posX, posY, 50f, GetInstanceZoneKey(conn));
        }

        private float ResolveProjectileImpactDelay(Combat.SpellData spell, PlayerState state, float projectileHitDistance)
        {
            if (spell != null && spell.ProjectileSize > 0f && spell.ProjectileSpeed > 0f)
                return Combat.WeaponUseRuntime.ProjectileImpactDelaySeconds(projectileHitDistance, spell.ProjectileSpeed);
            if (state != null && state.WeaponUsesProjectile && state.WeaponProjectileSpeed > 0f)
                return Combat.WeaponUseRuntime.ProjectileImpactDelaySeconds(projectileHitDistance, state.WeaponProjectileSpeed);
            return 0f;
        }

        private float ResolveProjectileMaxDistance(Combat.SpellData spell, float aimDistance)
        {
            if (spell == null || spell.ProjectileSpeed <= 0f)
                return Mathf.Max(0f, aimDistance);
            if (spell.ProjectileLifespan > 0f)
                return Mathf.Max(0f, spell.ProjectileSpeed * spell.ProjectileLifespan * COMBAT_TICK);
            float rangeDistance = spell.Range > 0 ? Mathf.Max(0f, spell.Range + 14f) : 0f;
            return Mathf.Max(Mathf.Max(0f, aimDistance), rangeDistance);
        }

        private PathMap ResolveProjectilePathMap(RRConnection conn, Combat.Monster monster)
        {
            string zoneName = monster?.ZoneName;
            if (string.IsNullOrWhiteSpace(zoneName))
                zoneName = conn?.CurrentZoneName;
            string pathMapKey = !string.IsNullOrWhiteSpace(monster?.InstanceKey)
                ? monster.InstanceKey
                : (conn != null ? GetInstanceZoneKey(conn) : null);
            if (string.IsNullOrWhiteSpace(pathMapKey))
                pathMapKey = zoneName;
            return !string.IsNullOrWhiteSpace(pathMapKey) ? PathMapCatalog.Instance.GetPathMap(pathMapKey) : null;
        }

        private void LogProjectilePathMapForUnitFirst(PathMap pathMap, Combat.SpellData spell, float startX, float startY, float pathX, float pathY, float pathLen, float impactDistance, Combat.Monster monster, bool predictedMove)
        {
            if (pathMap == null)
                return;
            if (pathLen <= 0.001f)
                return;

            float clampedDistance = Mathf.Clamp(impactDistance, 0f, pathLen);
            float t = clampedDistance / pathLen;
            float impactX = startX + pathX * t;
            float impactY = startY + pathY * t;
            if (pathMap.CanReachPoint(startX, startY, impactX, impactY))
                return;

            string spellName = spell?.DisplayName ?? spell?.SkillId ?? "spell";
            string targetName = monster != null ? $"{monster.Name}#{monster.EntityId}" : "<none>";
            Debug.LogError($"[PROJECTILE-PATHMAP] {spellName} path=({startX:F1},{startY:F1})->impact=({impactX:F1},{impactY:F1}) wouldBlock=True target={targetName} predictedMove={predictedMove} clientUnitFirst=True result=log-only sourceFunction=ProjectileChecker::testFirstTime->WorldCollision");
        }

        private Combat.Monster FindFirstProjectileMonsterHit(RRConnection conn, Combat.SpellData spell, float aimX, float aimY, out float projectileHitDistance)
        {
            projectileHitDistance = 0f;
            if (conn == null)
                return null;
            return FindFirstProjectileMonsterHitFromStart(conn, spell, conn.PlayerPosX, conn.PlayerPosY, aimX, aimY, out projectileHitDistance);
        }

        private Combat.Monster FindFirstProjectileMonsterHitFromStart(RRConnection conn, Combat.SpellData spell, float startX, float startY, float aimX, float aimY, out float projectileHitDistance)
        {
            projectileHitDistance = 0f;
            if (conn == null || spell == null || spell.ProjectileSize <= 0f)
                return null;

            float pathX = aimX - startX;
            float pathY = aimY - startY;
            float pathLenSq = pathX * pathX + pathY * pathY;
            if (pathLenSq <= 0.0001f)
                return null;
            float pathLen = Mathf.Sqrt(pathLenSq);
            float projectileMaxDistance = ResolveProjectileMaxDistance(spell, pathLen);
            if (projectileMaxDistance <= 0.001f)
                return null;
            float projectileDirX = pathX / pathLen;
            float projectileDirY = pathY / pathLen;
            float projectilePathX = projectileDirX * projectileMaxDistance;
            float projectilePathY = projectileDirY * projectileMaxDistance;

            Combat.Monster best = null;
            float bestImpactDistance = float.MaxValue;
            float bestDistSq = float.MaxValue;
            bool bestPredictedMove = false;
            float bestHitTime = 0f;
            string instanceKey = GetInstanceZoneKey(conn);

            foreach (var monster in Combat.CombatRuntime.Instance.GetActiveMonsters())
            {
                if (monster == null || !monster.IsAlive)
                    continue;
                if (!Combat.CombatRuntime.Instance.MatchesInstance(monster, instanceKey))
                    continue;
                if (Combat.CombatRuntime.Instance.PeekMonsterCurrentHPWire(monster) == 0)
                    continue;

                PathMap projectilePathMap = ResolveProjectilePathMap(conn, monster);
                Combat.CombatRuntime.Instance.TryGetMonsterClientVisiblePosition(monster, GetCombatNow(), out float visibleMonsterX, out float visibleMonsterY);
                float monsterX = visibleMonsterX - startX;
                float monsterY = visibleMonsterY - startY;
                float projectedDistance = monsterX * projectileDirX + monsterY * projectileDirY;
                float hitRadius = WeaponUseRuntime.ProjectileCollisionRadius(monster.CollisionRadius, spell.ProjectileSize);
                float hitRadiusSq = hitRadius * hitRadius;

                if (projectedDistance + hitRadius >= 0f && projectedDistance - hitRadius <= projectileMaxDistance)
                {
                    float closestDistance = Mathf.Clamp(projectedDistance, 0f, projectileMaxDistance);
                    float closestX = startX + (projectileDirX * closestDistance);
                    float closestY = startY + (projectileDirY * closestDistance);
                    float dx = visibleMonsterX - closestX;
                    float dy = visibleMonsterY - closestY;
                    float distSq = dx * dx + dy * dy;

                    if (distSq <= hitRadiusSq)
                    {
                        float entryOffset = Mathf.Sqrt(Mathf.Max(0f, hitRadiusSq - distSq));
                        float impactDistance = Mathf.Clamp(projectedDistance - entryOffset, 0f, projectileMaxDistance);
                        LogProjectilePathMapForUnitFirst(projectilePathMap, spell, startX, startY, projectilePathX, projectilePathY, projectileMaxDistance, impactDistance, monster, false);
                        if (impactDistance < bestImpactDistance || (Mathf.Abs(impactDistance - bestImpactDistance) <= 0.0001f && distSq < bestDistSq))
                        {
                            best = monster;
                            bestImpactDistance = impactDistance;
                            bestDistSq = distSq;
                            bestPredictedMove = false;
                            bestHitTime = 0f;
                        }
                    }
                }

                if (TryResolveMovingProjectileMonsterHit(conn, monster, spell, startX, startY, projectilePathX, projectilePathY, projectileMaxDistance, hitRadius, out float movingImpactDistance, out float movingDistSq, out float movingHitTime))
                {
                    LogProjectilePathMapForUnitFirst(projectilePathMap, spell, startX, startY, projectilePathX, projectilePathY, projectileMaxDistance, movingImpactDistance, monster, true);
                    if (movingImpactDistance < bestImpactDistance || (Mathf.Abs(movingImpactDistance - bestImpactDistance) <= 0.0001f && movingDistSq < bestDistSq))
                    {
                        best = monster;
                        bestImpactDistance = movingImpactDistance;
                        bestDistSq = movingDistSq;
                        bestPredictedMove = true;
                        bestHitTime = movingHitTime;
                    }
                }
            }

            if (best == null)
            {
                foreach (var monster in Combat.CombatRuntime.Instance.GetActiveMonsters())
                {
                    if (monster == null || !monster.IsAlive)
                        continue;
                    if (!Combat.CombatRuntime.Instance.MatchesInstance(monster, instanceKey))
                        continue;
                    if (Combat.CombatRuntime.Instance.PeekMonsterCurrentHPWire(monster) == 0)
                        continue;

                    Combat.CombatRuntime.Instance.TryGetMonsterClientVisiblePosition(monster, GetCombatNow(), out float visibleMonsterX, out float visibleMonsterY);
                    float hitRadius = WeaponUseRuntime.ProjectileCollisionRadius(monster.CollisionRadius, spell.ProjectileSize);
                    float dx = visibleMonsterX - aimX;
                    float dy = visibleMonsterY - aimY;
                    float endpointDistSq = dx * dx + dy * dy;
                    if (endpointDistSq > hitRadius * hitRadius)
                        continue;

                    float monsterX = visibleMonsterX - startX;
                    float monsterY = visibleMonsterY - startY;
                    float projectedDistance = monsterX * projectileDirX + monsterY * projectileDirY;
                    if (projectedDistance < 0f || projectedDistance > projectileMaxDistance + hitRadius)
                        continue;

                    best = monster;
                    bestImpactDistance = Mathf.Clamp(projectedDistance, 0f, projectileMaxDistance);
                    bestDistSq = endpointDistSq;
                    bestPredictedMove = false;
                    bestHitTime = 0f;
                    Debug.LogError($"[PROJECTILE-HIT] {spell.DisplayName ?? spell.SkillId ?? "spell"} endpoint unit-first fallback aim=({aimX:F1},{aimY:F1}) hit={monster.Name}#{monster.EntityId} dist={Mathf.Sqrt(endpointDistSq):F2} radius={hitRadius:F2} projected={projectedDistance:F2}");
                    break;
                }
            }

            if (best != null)
            {
                Combat.CombatRuntime.Instance.ApplyMonsterWanderClientVisiblePosition(best, "ProjectileChecker-hit");
                projectileHitDistance = bestImpactDistance;
                string predictedMove = bestPredictedMove ? $" predictedMove=True hitTime={bestHitTime:F3}s" : string.Empty;
                string pastAim = bestImpactDistance > pathLen + 0.01f ? " pastAim=True" : string.Empty;
                Debug.LogError($"[PROJECTILE-HIT] {spell.DisplayName ?? spell.SkillId ?? "spell"} path=({startX:F1},{startY:F1})->aim=({aimX:F1},{aimY:F1}) hit={best.Name}#{best.EntityId} hitDist={bestImpactDistance:F2} aimDist={pathLen:F2} maxDist={projectileMaxDistance:F2} delay={ResolveProjectileImpactDelay(spell, null, bestImpactDistance):F3}s dist={Mathf.Sqrt(bestDistSq):F2} radius={WeaponUseRuntime.ProjectileCollisionRadius(best.CollisionRadius, spell.ProjectileSize):F2} projectileRadius={WeaponUseRuntime.ProjectileRadiusFromAuthoredSize(spell.ProjectileSize):F2}{pastAim}{predictedMove}");
                Combat.CombatRuntime.Instance.TryGetMonsterClientVisiblePosition(best, GetCombatNow(), out float bestCvX, out float bestCvY);
                Debug.LogError($"[PROJ-HIT-CV] ent={best.EntityId} cv=({bestCvX:F2},{bestCvY:F2}) aggro={best.AggroTriggered} wanderActive={best.ClientVisibleMoveActive} hpWire={Combat.CombatRuntime.Instance.PeekMonsterCurrentHPWire(best)} hitDist={bestImpactDistance:F2} clientNow={GetCombatNow():F3}");
            }
            else
            {
                Debug.LogError($"[PROJECTILE-HIT] {spell.DisplayName ?? spell.SkillId ?? "spell"} path=({startX:F1},{startY:F1})->aim=({aimX:F1},{aimY:F1}) no unit hit size={spell.ProjectileSize:F1} aimDist={pathLen:F2} maxDist={projectileMaxDistance:F2}");
            }

            return best;
        }

        private bool TryResolveMovingProjectileMonsterHit(
            RRConnection conn,
            Combat.Monster monster,
            Combat.SpellData spell,
            float startX,
            float startY,
            float pathX,
            float pathY,
            float pathLen,
            float hitRadius,
            out float impactDistance,
            out float distSq,
            out float hitTime)
        {
            impactDistance = 0f;
            distSq = float.MaxValue;
            hitTime = 0f;

            if (conn == null || conn.Avatar == null || monster == null || spell == null)
                return false;
            if (spell.ProjectileSpeed <= 0f || pathLen <= 0.001f || hitRadius <= 0f)
                return false;

            uint playerEntityId = (uint)conn.Avatar.Id;
            if (!monster.AggroTriggered || monster.TargetId != playerEntityId || monster.AttackPending)
                return false;

            float targetX = conn.PlayerPosX;
            float targetY = conn.PlayerPosY;
            ResolveMonsterClientVisiblePosition(monster, out float monsterStartX, out float monsterStartY);
            float moveX = targetX - monsterStartX;
            float moveY = targetY - monsterStartY;
            float moveLen = Mathf.Sqrt(moveX * moveX + moveY * moveY);
            if (moveLen <= 0.001f)
                return false;

            float stopRange = Combat.CombatRuntime.Instance.GetMonsterEffectiveAttackRange(monster);
            float moveLimit = Mathf.Max(0f, moveLen - stopRange);
            if (moveLimit <= 0.001f)
                return false;

            float monsterSpeed = Combat.CombatRuntime.Instance.GetMonsterMovementSpeed(monster);
            if (monsterSpeed <= 0f)
                return false;

            float maxTime = pathLen / spell.ProjectileSpeed;
            if (maxTime <= 0f)
                return false;

            float projectileDirX = pathX / pathLen;
            float projectileDirY = pathY / pathLen;
            float moveDirX = moveX / moveLen;
            float moveDirY = moveY / moveLen;
            float hitRadiusSq = hitRadius * hitRadius;
            int samples = Mathf.Max(1, Mathf.CeilToInt(maxTime / COMBAT_TICK));

            for (int sample = 1; sample <= samples; sample++)
            {
                float t = Mathf.Min(maxTime, sample * COMBAT_TICK);
                float projectileDistance = Mathf.Min(pathLen, spell.ProjectileSpeed * t);
                float projectileX = startX + projectileDirX * projectileDistance;
                float projectileY = startY + projectileDirY * projectileDistance;
                float monsterDistance = Mathf.Min(moveLimit, monsterSpeed * t);
                float monsterX = monsterStartX + moveDirX * monsterDistance;
                float monsterY = monsterStartY + moveDirY * monsterDistance;
                float dx = monsterX - projectileX;
                float dy = monsterY - projectileY;
                float sampleDistSq = dx * dx + dy * dy;
                if (sampleDistSq <= hitRadiusSq)
                {
                    impactDistance = projectileDistance;
                    distSq = sampleDistSq;
                    hitTime = t;
                    return true;
                }
            }

            return false;
        }

        private Combat.SpellData ResolveSpellFromManip(RRConnection conn, byte manipulatorId)
        {
            Combat.SpellDatabase.Initialize();
            string connKey = conn.ConnId.ToString();
            if (_playerManipMap.TryGetValue(connKey, out var map))
            {
                if (map.TryGetValue(manipulatorId, out string gcClass))
                {
                    var spell = Combat.SpellDatabase.GetSpell(gcClass);
                    if (spell != null)
                    {
                        Debug.LogError($"[MANIP-RESOLVE] manipId={manipulatorId} -> {gcClass} -> {spell.DisplayName}");
                        return spell;
                    }
                    string shortName = gcClass;
                    int lastDot = gcClass.LastIndexOf('.');
                    if (lastDot >= 0) shortName = gcClass.Substring(lastDot + 1);
                    spell = Combat.SpellDatabase.GetSpell(shortName);
                    if (spell != null)
                    {
                        Debug.LogError($"[MANIP-RESOLVE] manipId={manipulatorId} -> {gcClass} -> short={shortName} -> {spell.DisplayName}");
                        return spell;
                    }
                    Debug.LogError($"[MANIP-RESOLVE] manipId={manipulatorId} -> {gcClass} but NOT in SpellDatabase!");
                }
                else
                {
                    Debug.LogError($"[MANIP-RESOLVE] manipId={manipulatorId} NOT in manipMap (keys: {string.Join(",", map.Keys)})");
                }
            }
            else
            {
                Debug.LogError($"[MANIP-RESOLVE] No manipMap for connection {connKey}");
            }
            return null;
        }

        private void HandleSkillTrainRequest(RRConnection conn, LEReader reader, ushort componentId, byte subMessage, ZoneNPC trainerNpc)
        {
            Debug.LogError($"[TRAINER] ");
            Debug.LogError($"[TRAINER]  SKILL TRAIN REQUEST from {conn.LoginName}");
            Debug.LogError($"[TRAINER]   NPC: {trainerNpc.Name} ({trainerNpc.GCClass}) cid=0x{componentId:X4}");

            if (reader.Remaining < 9)
            {
                Debug.LogError($"[TRAINER]  Not enough data: {reader.Remaining} bytes (need 9)");
                if (reader.Remaining > 0) reader.ReadBytes(reader.Remaining);
                return;
            }

            uint playerEntityId = reader.ReadUInt32();
            byte entityRefType = reader.ReadByte();
            uint skillHash = reader.ReadUInt32();

            Debug.LogError($"[TRAINER] playerEntityId=0x{playerEntityId:X} refType=0x{entityRefType:X2} skillHash=0x{skillHash:X8}");

            if (reader.Remaining > 0)
            {
                byte[] trailing = reader.ReadBytes(reader.Remaining);
                Debug.LogError($"[TRAINER]   trailing entitySynchInfo: {BitConverter.ToString(trailing)}");
            }

            if (!_skillHashToGcClass.TryGetValue(skillHash, out string skillGcClass))
            {
                Debug.LogError($"[TRAINER]  Unknown skill hash 0x{skillHash:X8} - not in DJB2 table");
                return;
            }
            Debug.LogError($"[TRAINER]   Resolved: 0x{skillHash:X8} -> {skillGcClass}");

            if (!_selectedCharacter.ContainsKey(conn.LoginName))
            {
                Debug.LogError($"[TRAINER]  No selected character for {conn.LoginName}");
                return;
            }
            var savedChar = GetActiveCharacter(conn);
            if (savedChar == null)
            {
                Debug.LogError($"[TRAINER]  SavedCharacter not found for {conn.LoginName}");
                return;
            }

            int currentLevel = savedChar.GetSkillLevel(skillGcClass);
            string connKey = conn.ConnId.ToString();
            bool playerHasSkill = false;
            if (_playerSkillLevels.TryGetValue(connKey, out var existingLevels))
                playerHasSkill = existingLevels.ContainsKey(skillGcClass);
            if (!playerHasSkill && savedChar.skills != null)
                playerHasSkill = savedChar.skills.Contains(skillGcClass);

            int nextLevel = playerHasSkill ? currentLevel + 1 : 1;

            Combat.SpellDatabase.Initialize();
            Combat.SpellData trainSpell = Combat.SpellDatabase.GetSpell(skillGcClass);
            if (trainSpell == null)
            {
                Debug.LogError($"[TRAINER] authored skill missing gcClass={skillGcClass}");
                return;
            }

            float goldValueMod = trainSpell.GoldValueMod;
            int requiredLevel = trainSpell.RequiredLevel;
            int maxSkillLevel = trainSpell.MaxSkillLevel;
            if (goldValueMod <= 0f || requiredLevel <= 0 || maxSkillLevel <= 0)
            {
                Debug.LogError($"[TRAINER] authored train data missing gcClass={skillGcClass} goldValueMod={goldValueMod:F2} requiredLevel={requiredLevel} maxSkillLevel={maxSkillLevel}");
                return;
            }

            if (nextLevel > maxSkillLevel)
            {
                Debug.LogError($"[TRAINER]  Already at max level {currentLevel}/{maxSkillLevel} for {skillGcClass}");
                return;
            }

            int goldCost = (int)((requiredLevel + (nextLevel - 1) * goldValueMod) * SKILL_VALUE_PER_LEVEL * goldValueMod);
            if (goldCost < 1) goldCost = 1;

            Debug.LogError($"[TRAINER]   Level: {currentLevel} -> {nextLevel} (max {maxSkillLevel})");
            Debug.LogError($"[TRAINER]   Gold cost: {goldCost} | Player gold: {savedChar.gold}");

            if (savedChar.gold < (uint)goldCost)
            {
                Debug.LogError($"[TRAINER]  Not enough gold: have {savedChar.gold}, need {goldCost}");
                return;
            }

            savedChar.gold -= (uint)goldCost;
            Debug.LogError($"[TRAINER]    Gold: {savedChar.gold + goldCost} -> {savedChar.gold}");

            savedChar.SetSkillLevel(skillGcClass, nextLevel);
            if (!playerHasSkill)
            {
                if (savedChar.skills == null)
                    savedChar.skills = new List<string>();
                if (!savedChar.skills.Contains(skillGcClass))
                    savedChar.skills.Add(skillGcClass);
                Debug.LogError($"[TRAINER]    Learned NEW skill: {skillGcClass}");
            }

            if (!_playerSkillLevels.ContainsKey(connKey))
                _playerSkillLevels[connKey] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _playerSkillLevels[connKey][skillGcClass] = nextLevel;
            int lastDot = skillGcClass.LastIndexOf('.');
            if (lastDot >= 0)
                _playerSkillLevels[connKey][skillGcClass.Substring(lastDot + 1)] = nextLevel;

            CharacterRepository.SaveCharacter(savedChar);
            Debug.LogError($"[TRAINER]    Character saved");


            ushort skillsCid = 0;
            _playerSkillsComponentId.TryGetValue(connKey, out skillsCid);
            Debug.LogError($"[TRAINER-RESPONSE] connKey={connKey} skillsCid=0x{skillsCid:X4}");

            if (skillsCid != 0)
            {

                uint skillEntityId = 0;
                bool hasSlot = false;
                if (_playerSkillSlots.TryGetValue(connKey, out var slots))
                {
                    hasSlot = slots.TryGetValue(skillGcClass, out skillEntityId);
                    if (!hasSlot)
                    {
                        string sn = skillGcClass;
                        int dot = sn.LastIndexOf('.');
                        if (dot >= 0) sn = sn.Substring(dot + 1);
                        hasSlot = slots.TryGetValue(sn, out skillEntityId);
                    }
                    Debug.LogError($"[TRAINER-RESPONSE] Slot lookup: '{skillGcClass}' -> entityId={skillEntityId} found={hasSlot}");
                    Debug.LogError($"[TRAINER-RESPONSE] All slots: {string.Join(", ", slots.Select(kv => $"{kv.Key}={kv.Value}"))}");
                }
                else
                {
                    Debug.LogError($"[TRAINER-RESPONSE]  No slot map for connKey={connKey}");
                }

                {
                    var combined = new LEWriter();
                    combined.WriteByte(0x07);

                    combined.WriteByte(0x35);
                    combined.WriteUInt16(skillsCid);
                    combined.WriteByte(0x33);
                    combined.WriteUInt32(savedChar.gold);
                    WritePlayerEntitySynch(conn, combined);

                    combined.WriteByte(0x35);
                    combined.WriteUInt16(skillsCid);
                    combined.WriteByte(0x32);
                    combined.WriteByte(0xFF);
                    combined.WriteCString(skillGcClass);
                    combined.WriteByte((byte)nextLevel);
                    WritePlayerEntitySynch(conn, combined);

                    combined.WriteByte(0x06);

                    byte[] combinedPacket = combined.ToArray();
                    Debug.LogError($"[TRAINER-COMBINED] hex ({combinedPacket.Length}b): {BitConverter.ToString(combinedPacket)}");
                    SendCompressedE(conn, combinedPacket);
                    Debug.LogError($"[TRAINER-COMBINED]  Sent gold={savedChar.gold} + level={nextLevel} for '{skillGcClass}'");
                }

                if (conn.UnitContainerId != 0)
                {
                    var goldWriter = new LEWriter();
                    goldWriter.WriteByte(0x07);
                    goldWriter.WriteByte(0x35);
                    goldWriter.WriteUInt16(conn.UnitContainerId);
                    goldWriter.WriteByte(0x20);
                    goldWriter.WriteInt32(-goldCost);
                    goldWriter.WriteByte(0x00);
                    goldWriter.WriteUInt32(0x00000000);
                    goldWriter.WriteByte(0x01);
                    WritePlayerEntitySynch(conn, goldWriter);
                    goldWriter.WriteByte(0x06);
                    byte[] goldPacket = goldWriter.ToArray();
                    Debug.LogError($"[TRAINER-GOLD]  RemoveCurrency {goldCost} via UnitContainer 0x{conn.UnitContainerId:X4}");
                    SendCompressedA(conn, 0x01, 0x0F, goldPacket);
                }
                else
                {
                    Debug.LogError($"[TRAINER-GOLD]  UnitContainerId=0, can't send gold update");
                }
            }
            else
            {
                Debug.LogError($"[TRAINER-RESPONSE]  SkillsComponentId=0 for {connKey} - rezone needed");
            }

            Debug.LogError($"[TRAINER]  TRAINED {skillGcClass} to Lv{nextLevel} for {conn.LoginName}");
            Debug.LogError($"[TRAINER] ");

            if (!playerHasSkill)
            {
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
                        Debug.LogError($"[TRAINER] Added '{skillGcClass}' to Manipulators, eid={newSkill.Id}");
                    }
                }
            }

            string shortName = skillGcClass;
            int dotIdx = skillGcClass.LastIndexOf('.');
            if (dotIdx >= 0) shortName = skillGcClass.Substring(dotIdx + 1);
            SendSystemMessage(conn, playerHasSkill
                ? $"{shortName} -> Rank {nextLevel}! ({savedChar.gold} gold)"
                : $"Learned {shortName}! ({savedChar.gold} gold)");
        }

        private int GetPlayerSkillLevel(RRConnection conn, Combat.SpellData spell)
        {
            string connKey = conn.ConnId.ToString();
            if (_playerSkillLevels.TryGetValue(connKey, out var levels))
            {
                if (levels.TryGetValue(spell.ShortName, out int lvl)) return lvl;
                if (!string.IsNullOrEmpty(spell.SkillId) && levels.TryGetValue(spell.SkillId, out lvl)) return lvl;
            }
            return 1;
        }

        private void InitializePlayerSkillLevels(RRConnection conn, SavedCharacter savedChar)
        {
            string connKey = conn.ConnId.ToString();
            var levels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (savedChar.skills != null)
            {
                foreach (var skillGc in savedChar.skills)
                {
                    int savedLevel = savedChar.GetSkillLevel(skillGc);

                    levels[skillGc] = savedLevel;
                    int lastDot = skillGc.LastIndexOf('.');
                    if (lastDot >= 0)
                        levels[skillGc.Substring(lastDot + 1)] = savedLevel;
                }
            }
            _playerSkillLevels[connKey] = levels;
            Debug.LogError($"[SKILL-LEVELS] Initialized {levels.Count} skill entries for {conn.LoginName}");
            foreach (var levelEntry in levels)
                Debug.LogError($"[SKILL-LEVELS]   {levelEntry.Key} = Lv{levelEntry.Value}");
        }

        private void HandleSpellAttack(RRConnection conn, PlayerState state, Combat.Monster monster, byte manipulatorId, byte useFlags, float aimX = 0, float aimY = 0, float clientEffectTime = -1f)
        {
            Combat.SpellDatabase.Initialize();
            var spell = ResolveSpellFromManip(conn, manipulatorId);
            if (spell == null)
            {
                Debug.LogError($"[SPELL] No spell found for manipId={manipulatorId} class={state.ClassName}");
                return;
            }
            int skillLevel = GetPlayerSkillLevel(conn, spell);
            float effectNow = clientEffectTime >= 0f ? clientEffectTime : GetCombatNow();
            int triggerFrames = ResolveActiveSkillTriggerFrames(spell, state);
            int animationFrames = Combat.SpellDatabase.TryResolvePlayerAnimationTiming(spell.AnimationId, state, out int targetFrames, out _) && targetFrames > 0
                ? targetFrames
                : spell.AnimationLengthFrames;
            Debug.LogError($"[SKILL-USE] action=target manipId={manipulatorId} gc={spell.SkillId} spell={spell.DisplayName} level={skillLevel} anim={spell.AnimationId} frames={animationFrames} trigger={triggerFrames} target={monster?.EntityId ?? 0} flags=0x{useFlags:X2} now={effectNow:F3}");
            Debug.LogError($"[SPELL] {spell.DisplayName} (Lv{skillLevel}) cast by {conn.LoginName} on {monster.Name} (flags={useFlags} manip={manipulatorId}) AoE={spell.IsAoE}");

            state.AdvanceEntitySynchInfoHP(effectNow, $"MANA-{spell.DisplayName}-pre-cost");
            uint manaCostWire = (uint)(spell.ManaCostMod * state.Level * 256);
            uint oldMana = state.CurrentManaWire;
            if (state.CurrentManaWire > manaCostWire)
                state.SetCurrentMana(state.CurrentManaWire - manaCostWire, $"spell:{spell.DisplayName}");
            else
                state.SetCurrentMana(0, $"spell:{spell.DisplayName}");
            Debug.LogError($"[MANA] spell={spell.DisplayName} cost={manaCostWire / 256} old={oldMana / 256} current={state.CurrentManaWire / 256} max={state.MaxManaWire / 256}");

            try
            {
                if (_selectedCharacter.ContainsKey(conn.LoginName))
                    using (var manaDb = GameDatabase.GetConnection())
                        GameDatabase.ExecuteNonQuery(manaDb,
                            "UPDATE characters SET current_mana=@mp WHERE id=@id",
                            ("@mp", (int)state.CurrentManaWire), ("@id", (int)_selectedCharacter[conn.LoginName].Id));
            }
            catch (System.Exception ex) { Debug.LogError($"[SAVE] field=current_mana player={conn.LoginName} state=failed message='{ex.Message}'"); }

            var rng = CombatRuntime.Instance.GetRoomRngForMonster(monster);
            ApplySpellDamageToMonster(conn, state, spell, monster, rng, skillLevel, false, effectNow);
            if (spell.IsChainSpell && monster != null)
                HandleChainSpell(conn, state, monster, spell, rng, skillLevel, effectNow);
        }

        private void ApplySpellDamageToMonster(RRConnection conn, PlayerState state,
            Combat.SpellData spell, Combat.Monster target, Combat.MersenneTwister rng,
            int skillLevel, bool isAoETarget, float clientEffectTime = -1f)
        {
            if (spell == null || target == null) return;
            string tag = isAoETarget ? "SPELL-AOE" : "SPELL";
            if (rng == null)
            {
                Debug.LogError($"[{tag}] {spell.DisplayName} -> {target.Name}: room RNG unavailable, damage not applied");
                LogMonsterOnAttackedBlocked(conn, target, tag, "missing-rng-no-Damage::apply");
                return;
            }

            SpellWeaponDamageEffectResult weaponEffect = default;
            if (spell.HasImmediateWeaponDamageEffect)
            {
                weaponEffect = ApplySpellWeaponDamageEffect(conn, state, spell, target, rng, skillLevel, tag, clientEffectTime);
                if (!weaponEffect.Landed)
                {
                    LogMonsterOnAttackedBlocked(conn, target, tag, "weapon-effect-not-landed");
                    return;
                }
                if (!weaponEffect.Applied)
                {
                    LogMonsterOnAttackedBlocked(conn, target, tag, "weapon-effect-not-applied");
                    return;
                }

                ApplySpellWeaponDamageChildEffects(conn, state, spell, target, rng, skillLevel, tag, clientEffectTime);

                bool lethalWeaponDamage = weaponEffect.Died || weaponEffect.NewHPWire == 0 || CombatRuntime.Instance.PeekMonsterCurrentHPWire(target) == 0;
                if (lethalWeaponDamage)
                {
                    try
                    {
                        Debug.LogError($"[{tag}-KILL] Finalizing target={target.EntityId} hp={weaponEffect.OldHPWire}->{weaponEffect.NewHPWire} source=SpellWeaponDamageEffect");
                        bool finalized = TryFinalizeMonsterKill(conn, target, $"{tag}-weapon-kill");
                        if (state != null)
                            CommitPlayerHPTruth(conn, state, $"{tag}-WEAPON-KILL-AFTER-FINALIZE", state.CurrentHPWire, false, false);
                        Debug.LogError($"[{tag}-KILL] Finalize result target={target.EntityId} finalized={finalized} playerLevel={(state != null ? state.Level : 0)} playerHP={(state != null ? state.EntitySynchInfoHP : 0)}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[{tag}-KILL] SpellWeaponDamageEffect finalize failed target={target.EntityId}: {ex}");
                    }
                    CombatRuntime.Instance.CancelMonsterPendingAttack(target, $"{tag}-weapon-kill");
                    if (IsUseTargetingMonster(conn, target))
                        ClearUseTargetAndReleaseControl(conn, $"{tag}-weapon-kill", sendClientControlReset: true, requireActiveUseTargetForReset: true);
                    return;
                }
            }

            if (spell.HasProjectileModifierDamage)
            {
                uint sourceEntityId = conn?.Avatar != null ? (uint)conn.Avatar.Id : 0;
                var modifierResult = CombatRuntime.Instance.ApplyProjectileModifierFromSpell(
                    target,
                    sourceEntityId,
                    state,
                    spell,
                    rng,
                    skillLevel,
                    clientEffectTime >= 0f ? clientEffectTime : GetCombatNow(),
                    tag);

                if (!modifierResult.AppliedModifier)
                {
                    Debug.LogError($"[{tag}] {spell.DisplayName} -> {target.Name}: projectile modifier not applied reason={modifierResult.Reason} hp={CombatRuntime.Instance.PeekMonsterCurrentHPWire(target)} rngAfter={rng.CallsSinceReseed}");
                    LogMonsterOnAttackedBlocked(conn, target, tag, $"modifier-not-applied:{modifierResult.Reason}");
                    return;
                }

                Debug.LogError($"[POISON-SHOT-CHAIN] spell={spell.SkillId} projectileEffect={spell.ProjectileEffectId} modifier={spell.ProjectileModifierId} modifierEffect={spell.ProjectileModifierEffectId} ticks={modifierResult.TicksApplied} hp={modifierResult.OldHPWire}->{modifierResult.NewHPWire} rngAfterImpact={rng.CallsSinceReseed} status=modifier-attached-first-tick-deferred");
                LogMonsterOnAttackedBlocked(conn, target, tag, "modifier-attached-no-Damage::apply");
                return;
            }

            if (spell.HasImmediateWeaponDamageEffect && !spell.HasDirectDamageEffect)
            {
                return;
            }

            int attackerLevel = Math.Max(1, state != null ? state.Level : 1);
            int spellDamageTypeId = Combat.DamageResolver.ResolveDamageTypeId(spell.DamageType);

            var result = Combat.DamageResolver.ProcessSpellAttack(
                rng,
                state.Level,
                state.ClientSpellIntellect,
                state.ClientSpellAgility,
                state.ClientSpellStrength,
                state.WeaponDamage,
                state.WeaponDamageVolatility,
                spell,
                target,
                skillLevel,
                isAoETarget,
                Combat.DamageResolver.ResolveCriticalDamagePercent(state),
                state,
                0);

            string resultName = result.Type.ToString().ToUpperInvariant();
            if (result.Type == Combat.AttackResultType.Miss || result.DamageF32 <= 0)
            {
                Debug.LogError($"[COMBAT-EVENT] actor=player-spell actorId={(conn?.Avatar != null ? conn.Avatar.Id : 0)} target=monster targetId={target.EntityId} result={resultName} damageWire=0 hp={CombatRuntime.Instance.PeekMonsterCurrentHPWire(target)}->{CombatRuntime.Instance.PeekMonsterCurrentHPWire(target)} spell={spell.DisplayName} rngAfter={rng.CallsSinceReseed} marker={tag}");
                return;
            }

            float damageTime = clientEffectTime >= 0f ? clientEffectTime : GetCombatNow();
            float clientVisibleTime = ResolveSpellDamageClientVisibleTime(spell, isAoETarget, damageTime);
            bool applied = CombatRuntime.Instance.ApplyPlayerDamageToMonsterWire(
                target,
                (uint)result.DamageF32,
                tag,
                out uint oldHPWire,
                out uint newHPWire,
                out bool died,
                clientDamageTime: damageTime,
                damageTypeId: result.DamageTypeId,
                rawDamageWire: (uint)result.DamageF32,
                attackerLevel: attackerLevel,
                damageKind: 3,
                clientVisibleTime: clientVisibleTime);

            if (!applied)
            {
                Debug.LogError($"[{tag}] {spell.DisplayName} -> {target.Name}: damage not applied alive={target.IsAlive} hp={CombatRuntime.Instance.PeekMonsterCurrentHPWire(target)}");
                return;
            }
            uint effectRaw = CombatRuntime.Instance.ConsumeOnApplyDamageEffectRng(
                rng,
                "player-spell",
                target.EntityId,
                target.Name,
                oldHPWire,
                newHPWire,
                target.MaxHPWire,
                (uint)result.DamageF32,
                tag,
                physicalWeaponHit: false);
            if (state != null && oldHPWire > newHPWire)
                state.ApplyOnDamageCallback(oldHPWire - newHPWire, clientEffectTime >= 0f ? clientEffectTime : GetCombatNow(), tag);

            Debug.LogError($"[COMBAT-EVENT] actor=player-spell actorId={(conn?.Avatar != null ? conn.Avatar.Id : 0)} target=monster targetId={target.EntityId} result={resultName} damageWire={result.DamageF32} hp={oldHPWire}->{newHPWire} range=[{result.MinDamageF32},{result.MaxDamageF32}] damageRaw=0x{result.DamageRaw:X8} effectRaw=0x{effectRaw:X8} spell={spell.DisplayName} rngAfter={rng.CallsSinceReseed} marker={tag}");
            NotifyMonsterDamagedByConnection(conn, target, tag);

            if (spell.HasSpellKnockDownEffect)
            {
                int strength = spell.ResolveSpellKnockDownStrength(skillLevel);
                CombatRuntime.Instance.ApplySpellKnockDownEffectToMonster(
                    rng,
                    target,
                    attackerLevel,
                    strength,
                    spell.SpellKnockDownChanceF32,
                    tag,
                    clientEffectTime,
                    conn?.Avatar != null ? (uint)conn.Avatar.Id : 0);
            }

            bool lethalSpellDamage = died || newHPWire == 0 || CombatRuntime.Instance.PeekMonsterCurrentHPWire(target) == 0;
            if (lethalSpellDamage)
            {
                if (!died)
                    Debug.LogError($"[{tag}-KILL] Lethal HP reached without died flag target={target.EntityId} hp={oldHPWire}->{newHPWire}");
                try
                {
                    Debug.LogError($"[{tag}-KILL] Finalizing target={target.EntityId} hp={oldHPWire}->{newHPWire}");
                    bool finalized = TryFinalizeMonsterKill(conn, target, $"{tag}-kill");
                    if (state != null)
                        CommitPlayerHPTruth(conn, state, $"{tag}-KILL-AFTER-FINALIZE", state.CurrentHPWire, false, false);
                    Debug.LogError($"[{tag}-KILL] Finalize result target={target.EntityId} finalized={finalized} playerLevel={(state != null ? state.Level : 0)} playerHP={(state != null ? state.EntitySynchInfoHP : 0)}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[{tag}-KILL] Finalize failed target={target.EntityId}: {ex}");
                }
                CombatRuntime.Instance.CancelMonsterPendingAttack(target, $"{tag}-kill");
                if (IsUseTargetingMonster(conn, target))
                    ClearUseTargetAndReleaseControl(conn, $"{tag}-kill", sendClientControlReset: true, requireActiveUseTargetForReset: true);
                return;
            }
        }

        private void ApplySpellWeaponDamageChildEffects(
            RRConnection conn,
            PlayerState state,
            Combat.SpellData spell,
            Combat.Monster target,
            Combat.MersenneTwister rng,
            int skillLevel,
            string tag,
            float clientEffectTime)
        {
            if (spell == null || target == null || !target.IsAlive)
                return;
            if (!spell.HasSpellKnockDownEffect)
                return;

            int strength = spell.ResolveSpellKnockDownStrength(skillLevel);
            CombatRuntime.Instance.ApplySpellKnockDownEffectToMonster(
                rng,
                target,
                Math.Max(1, state != null ? state.Level : 1),
                strength,
                spell.SpellKnockDownChanceF32,
                $"{tag}-SpellWeaponDamageEffect",
                clientEffectTime,
                conn?.Avatar != null ? (uint)conn.Avatar.Id : 0);
        }

        private SpellWeaponDamageEffectResult ApplySpellWeaponDamageEffect(
            RRConnection conn,
            PlayerState state,
            Combat.SpellData spell,
            Combat.Monster target,
            Combat.MersenneTwister rng,
            int skillLevel,
            string tag,
            float clientEffectTime)
        {
            var result = new SpellWeaponDamageEffectResult
            {
                Attempted = true,
                OldHPWire = target != null ? CombatRuntime.Instance.PeekMonsterCurrentHPWire(target) : 0,
                NewHPWire = target != null ? CombatRuntime.Instance.PeekMonsterCurrentHPWire(target) : 0,
                ResultName = "MISS"
            };

            if (state == null || spell == null || target == null || rng == null)
            {
                Debug.LogError($"[SPELL-WEAPON-DAMAGE] skipped spell={spell?.DisplayName ?? "unknown"} target={target?.EntityId ?? 0} state={(state != null)} rng={(rng != null)} tag={tag ?? "SPELL"}");
                return result;
            }

            int arMod = ResolveSpellEffectPercent(spell.ARModMin, spell.ARModMax, spell.ARModInc, skillLevel, 100);
            int damageModRaw = ResolveSpellEffectRawMod(spell.WeaponEffectDamageModMin, spell.WeaponEffectDamageModMax, spell.WeaponEffectDamageModInc, skillLevel);
            int baseAttackRating = DamageResolver.ResolveAvatarAttackRating(state);
            int attackRating = Mathf.Clamp((baseAttackRating * Math.Max(0, arMod)) / 100, 0, 0xFFFF);
            int baseDamageMod = DamageResolver.ResolveDamageMod(state);
            int damagePct = Math.Max(0, 100 + damageModRaw);
            int damageMod = Mathf.Clamp((baseDamageMod * damagePct) / 100, 0, 0xFFFF);
            int attackerLevel = Math.Max(0, state.Level);
            int defenderLevel = Math.Max(0, (int)target.Level);

            var damageInput = new WeaponDamageInput
            {
                Rng = rng,
                Source = $"{tag ?? "SPELL"}-SpellWeaponDamageEffect",
                AttackerLevel = attackerLevel,
                DefenderLevel = defenderLevel,
                AttackRating = attackRating,
                DefenseRating = DamageResolver.ResolveMonsterDefenseRating(target),
                BlockChance = 0,
                DamageLevel = DamageResolver.ResolveWeaponDamageLevel(state),
                DamageBonus = DamageResolver.ResolveWeaponDamageBonus(state),
                DamageMod = damageMod,
                WeaponClassId = DamageResolver.ResolveWeaponClassId(state),
                DamageTypeId = DamageResolver.ResolveDamageTypeId(state),
                WeaponDamageF32 = DamageResolver.GetWeaponBaseDamageF32(state),
                WeaponVolatilityF32 = DamageResolver.GetWeaponVolatilityF32(state),
                CritThreshold = DamageResolver.ResolveCriticalThreshold(state, target),
                CritDamagePercent = DamageResolver.ResolveCriticalDamagePercent(state),
                AttackerState = state,
                IncludeWeaponDamageAdds = true
            };

            DamageResolver.LogDamageSlots(state, damageInput, target, damageInput.Source);
            WeaponDamageResult damageResult = DamageResolver.ResolveWeaponDamage(damageInput);
            result.HitRaw = damageResult.HitRaw;
            result.BlockRaw = damageResult.BlockRaw;
            result.DamageRaw = damageResult.DamageRaw;
            result.HitRoll = damageResult.HitRoll;
            result.BlockRoll = damageResult.BlockRoll;
            result.HitThreshold = damageResult.HitThreshold;
            result.AttackRating = damageResult.AttackRating;
            result.DefenseRating = damageResult.DefenseRating;
            result.DamageMod = damageMod;
            result.SkillDamageModRaw = damageModRaw;
            result.ARMod = arMod;
            result.MinDamageWire = damageResult.MinDamageF32;
            result.MaxDamageWire = damageResult.MaxDamageF32;
            result.DamageWire = damageResult.DamageWire;
            result.IsCritical = damageResult.IsCritical;
            result.ResultName = damageResult.ResultName;

            bool landed = damageResult.IsHit && !damageResult.IsBlocked && damageResult.DamageWire > 0;
            if (!landed)
            {
                Debug.LogError($"[SPELL-WEAPON-DAMAGE] spell={spell.DisplayName} result={damageResult.ResultName} target={target.Name}#{target.EntityId} hp={result.OldHPWire}->{result.NewHPWire} arMod={arMod} ar={baseAttackRating}->{attackRating} dr={damageResult.DefenseRating} hitRaw=0x{damageResult.HitRaw:X8} hitRoll={damageResult.HitRoll} threshold={damageResult.HitThreshold} blockRaw=0x{damageResult.BlockRaw:X8} blockRoll={damageResult.BlockRoll} rngAfter={rng.CallsSinceReseed} tag={tag ?? "SPELL"}");
                Debug.LogError($"[COMBAT-EVENT] actor=player-spell-weapon actorId={(conn?.Avatar != null ? conn.Avatar.Id : 0)} target=monster targetId={target.EntityId} result={damageResult.ResultName} damageWire=0 hp={result.OldHPWire}->{result.NewHPWire} spell={spell.DisplayName} arMod={arMod} damageModRaw={damageModRaw} rngAfter={rng.CallsSinceReseed} marker={tag ?? "SPELL"}");
                return result;
            }

            bool applied = CombatRuntime.Instance.ApplyPlayerWeaponDamageToMonsterWire(
                target,
                damageResult,
                $"{tag}-SpellWeaponDamageEffect",
                out uint oldHPWire,
                out uint newHPWire,
                out bool died,
                out uint effectRaw,
                rng,
                "player-spell-weapon",
                clientEffectTime >= 0f ? clientEffectTime : GetCombatNow(),
                0);
            if (applied)
                NotifyMonsterDamagedByConnection(conn, target, $"{tag}-weapon");

            result.Landed = true;
            result.Applied = applied;
            result.Died = died;
            result.OldHPWire = oldHPWire;
            result.NewHPWire = newHPWire;

            string resultName = damageResult.IsCritical ? "CRIT" : "HIT";
            Debug.LogError($"[SPELL-WEAPON-DAMAGE] spell={spell.DisplayName} result={resultName} target={target.Name}#{target.EntityId} damageWire={damageResult.DamageWire} totalWire={damageResult.TotalDamageWire} addCount={(damageResult.DamageAdds != null ? damageResult.DamageAdds.Count : 0)} hp={oldHPWire}->{newHPWire} applied={applied} died={died} arMod={arMod} ar={baseAttackRating}->{attackRating} dr={damageResult.DefenseRating} damageMod={baseDamageMod}->{damageMod} damageModRaw={damageModRaw} range=[{damageResult.MinDamageF32},{damageResult.MaxDamageF32}] hitRaw=0x{damageResult.HitRaw:X8} blockRaw=0x{damageResult.BlockRaw:X8} dmgRaw=0x{damageResult.DamageRaw:X8} crit={damageResult.IsCritical} effectRaw=0x{effectRaw:X8} rngAfter={rng.CallsSinceReseed} tag={tag ?? "SPELL"}");
            Debug.LogError($"[COMBAT-EVENT] actor=player-spell-weapon actorId={(conn?.Avatar != null ? conn.Avatar.Id : 0)} target=monster targetId={target.EntityId} result={resultName} damageWire={damageResult.DamageWire} totalWire={damageResult.TotalDamageWire} addCount={(damageResult.DamageAdds != null ? damageResult.DamageAdds.Count : 0)} hp={oldHPWire}->{newHPWire} range=[{damageResult.MinDamageF32},{damageResult.MaxDamageF32}] hitRaw=0x{damageResult.HitRaw:X8} blockRaw=0x{damageResult.BlockRaw:X8} damageRaw=0x{damageResult.DamageRaw:X8} effectRaw=0x{effectRaw:X8} arMod={arMod} damageModRaw={damageModRaw} spell={spell.DisplayName} rngAfter={rng.CallsSinceReseed} marker={tag ?? "SPELL"}");
            return result;
        }

        private static int ResolveSpellEffectRawMod(int min, int max, int inc, int skillLevel)
        {
            int raw = min + (Math.Max(1, skillLevel) * inc);
            if (max > 0 && raw > max)
                raw = max;
            return raw;
        }

        private static int ResolveSpellEffectPercent(int min, int max, int inc, int skillLevel, int fallback)
        {
            int raw = ResolveSpellEffectRawMod(min, max, inc, skillLevel);
            return raw > 0 ? raw : fallback;
        }

        private void NotifyMonsterDamagedByConnection(RRConnection conn, Combat.Monster monster, string reason)
        {
            if (conn?.Avatar == null || monster == null) return;
            Combat.CombatRuntime.Instance.NotifyMonsterOnAttackedAdmission(monster, (uint)conn.Avatar.Id, reason);
        }

        private void LogMonsterOnAttackedBlocked(RRConnection conn, Combat.Monster monster, string source, string reason)
        {
            if (monster == null) return;
            uint playerId = conn?.Avatar != null ? (uint)conn.Avatar.Id : 0u;
            Debug.LogError($"[MON-ONATTACKED-NO-ADMISSION] monster={monster.Name}#{monster.EntityId} player={playerId} source={source ?? "unknown"} reason={reason ?? "unknown"} sourceFunction=Damage::apply@0x004F6580->MonsterBehavior2::onAttacked@0x0051B550");
        }

        private void HandleChainSpell(RRConnection conn, PlayerState state, Combat.Monster source,
    Combat.SpellData spell, Combat.MersenneTwister rng, int skillLevel = 1, float clientEffectTime = -1f)
        {
            string instanceKey = !string.IsNullOrWhiteSpace(source?.InstanceKey)
                ? source.InstanceKey
                : GetInstanceZoneKey(conn);
            var nearby = Combat.CombatRuntime.Instance.GetMonstersInRange(
                source.PosX, source.PosY, spell.ChainRange, instanceKey);
            int chainsLeft = spell.NumChains;
            foreach (var target in nearby)
            {
                if (chainsLeft <= 0) break;
                if (target.EntityId == source.EntityId) continue;
                if (!target.IsAlive) continue;
                ApplySpellDamageToMonster(conn, state, spell, target, rng, skillLevel, true, clientEffectTime);
                chainsLeft--;
            }
        }


        private void HandleSelfCastSpell(RRConnection conn, PlayerState state, byte slotID, ushort componentId, byte spellSessionId = 0)
        {
            Combat.SpellDatabase.Initialize();

            var spell = ResolveSpellFromManip(conn, slotID);

            string connKey2 = conn.ConnId.ToString();
            if (_playerManipMap.TryGetValue(connKey2, out var manipMap2) &&
                manipMap2.TryGetValue(slotID, out string gcClass2))
            {
                if (gcClass2.IndexOf("BlingGnome", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    gcClass2.IndexOf("SummonBling", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Debug.LogError($"[SPELL-BLING] Bling Gnome skill detected: {gcClass2}");
                    BlingGnomeRuntime.Instance.SetServer(this);
                    BlingGnomeRuntime.Instance.ToggleGnome(conn,
                        (c, d, t, b) => SendCompressedA(c, d, t, b),
                        (c, m) => SendSystemMessage(c, m));
                    return;
                }
            }

            if (spell == null)
                Debug.LogError($"[SPELL-0x52] action=resolveSelfCast manipId={slotID} state=missing");

            int skillLevel = spell != null ? GetPlayerSkillLevel(conn, spell) : 1;
            Debug.LogError($"[SPELL-0x52] action=handleSelfCast class={state.ClassName} manipId={slotID} spell={spell?.DisplayName ?? "UNKNOWN"} skillLevel={skillLevel}");

            if (spell != null)
            {
                int selfTriggerFrames = ResolveActiveSkillTriggerFrames(spell, state);
                int selfAnimationFrames = Combat.SpellDatabase.TryResolvePlayerAnimationTiming(spell.AnimationId, state, out int selfFrames, out _) && selfFrames > 0
                    ? selfFrames
                    : spell.AnimationLengthFrames;
                Debug.LogError($"[SKILL-USE] action=self manipId={slotID} gc={spell.SkillId} spell={spell.DisplayName} level={skillLevel} anim={spell.AnimationId} frames={selfAnimationFrames} trigger={selfTriggerFrames} component=0x{componentId:X4} session={spellSessionId} now={GetCombatNow():F3}");
                state.AdvanceEntitySynchInfoHP(GetCombatNow(), $"MANA-0x52-{spell.DisplayName}-pre-cost");
                uint manaCostWire = (uint)(spell.ManaCostMod * state.Level * 256);
                uint oldMana = state.CurrentManaWire;
                if (state.CurrentManaWire > manaCostWire)
                    state.SetCurrentMana(state.CurrentManaWire - manaCostWire, $"selfspell:{spell.DisplayName}");
                else
                    state.SetCurrentMana(0, $"selfspell:{spell.DisplayName}");
                Debug.LogError($"[MANA-0x52] spell={spell.DisplayName} cost={manaCostWire / 256} old={oldMana / 256} current={state.CurrentManaWire / 256} max={state.MaxManaWire / 256}");

                try
                {
                    if (_selectedCharacter.ContainsKey(conn.LoginName))
                        using (var manaDb2 = GameDatabase.GetConnection())
                            GameDatabase.ExecuteNonQuery(manaDb2,
                                "UPDATE characters SET current_mana=@mp WHERE id=@id",
                                ("@mp", (int)state.CurrentManaWire), ("@id", (int)_selectedCharacter[conn.LoginName].Id));
                }
                catch (System.Exception ex) { Debug.LogError($"[SAVE] field=current_mana player={conn.LoginName} state=failed message='{ex.Message}'"); }
            }

            if (spell != null && spell.IsAoE)
            {
                int triggerFrames = ResolveActiveSkillTriggerFrames(spell, state);
                float now = GetCombatNow();
                float dueTime = now + triggerFrames * COMBAT_TICK;
                ResolveSpellSourcePoint(conn, spell, state, out float castX, out float castY, out float castZ, out float castHeading, out bool castSourceOffsetResolved, out Vector3 castSourceOffset);
                var pending = new PendingAoECast
                {
                    Sequence = ++_nextPendingAoECastSequence,
                    Conn = conn,
                    State = state,
                    Spell = spell,
                    SkillLevel = skillLevel,
                    InstanceKey = GetInstanceZoneKey(conn),
                    CastX = castX,
                    CastY = castY,
                    CastZ = castZ,
                    CastHeading = castHeading,
                    CastTime = now,
                    CastSourceOffsetResolved = castSourceOffsetResolved,
                    CastSourceOffset = castSourceOffset,
                    DueTime = dueTime
                };
                lock (_pendingAoECastLock)
                {
                    _pendingAoECasts.Add(pending);
                }
                Debug.LogError($"[SPELL-0x52] action=aoeDefer spell={spell.DisplayName} triggerFrames={triggerFrames} now={now:F3} due={dueTime:F3}");
            }
            else
            {
                Debug.LogError($"[SPELL-0x52] aoe=false action=selfCastDamage slotId={slotID} state=skipped");
            }

            {
                string buffConnKey = conn.ConnId.ToString();
                string buffGcClass = null;
                if (_playerManipMap.TryGetValue(buffConnKey, out var buffMap))
                    buffMap.TryGetValue(slotID, out buffGcClass);
                if (buffGcClass != null)
                {
                    string shortKey = buffGcClass.ToLowerInvariant();
                    int lastDot = shortKey.LastIndexOf('.');
                    if (lastDot >= 0) shortKey = shortKey.Substring(lastDot + 1);
                    if (_buffModifierMap.TryGetValue(shortKey, out var buffInfo))
                    {
                        uint durTicks = 0;
                        if (buffInfo.durBase != 0)
                        {
                            long durFixed8 = (long)Math.Round((buffInfo.durBase + skillLevel * buffInfo.durInc) * 256.0);
                            long durTickCount = ((durFixed8 * 30L) + 0x100L) >> 8;
                            durTicks = durTickCount <= 0 ? 0u : durTickCount > ushort.MaxValue ? ushort.MaxValue : (uint)durTickCount;
                        }
                        uint modId = _activeModifiers.NextId();
                        RecordModifierSent(conn.LoginName, buffInfo.modGcType, modId,
                            level: (byte)skillLevel, duration: durTicks, sourceIsSelf: 0x01);
                        Debug.LogError($"[BUFF-TRACK] {shortKey} -> '{buffInfo.modGcType}' dur={durTicks} ticks ({buffInfo.durBase + skillLevel * buffInfo.durInc}s) for {conn.LoginName}");
                    }
                }
            }
        }



        private void HandleItemPickup(RRConnection conn, ushort componentId, ushort targetEntityID, byte responseId, byte sessionID)
        {
            if (_droppedItems.TryGetValue(targetEntityID, out var goldCheck) && goldCheck.GoldAmount > 0)
            {
                HandleItemRightClickPickup(conn, componentId, targetEntityID, responseId, sessionID);
                return;
            }

            Debug.LogError("");
            Debug.LogError("                              ITEM PICKUP START                                ");
            Debug.LogError("");

            bool isQuestPickup = false;
            bool isGoldPickup = false;
            uint goldPickupAmount = 0;
            if (_droppedItems.TryGetValue(targetEntityID, out var preInfo))
            {
                isQuestPickup = preInfo.IsQuestItem;
                isGoldPickup = preInfo.IsGoldDrop;
                goldPickupAmount = preInfo.GoldAmount;
            }
            int droppedQty; GCObject item = GetAndRemoveDroppedItem(targetEntityID, out droppedQty);
            if (item == null)
            {
                Debug.LogError($"[PICKUP]  Item not found for entity {targetEntityID}");
                return;
            }

            Debug.LogError($"[PICKUP] Found item: {item.GCClass} isQuestItem={isQuestPickup} isGold={isGoldPickup}");

            if (isGoldPickup && goldPickupAmount > 0)
            {
                Debug.LogError($"[PICKUP] Gold pile: +{goldPickupAmount} gold");
                try
                {
                    if (_selectedCharacter.TryGetValue(conn.LoginName, out var goldGcObj))
                    {
                        var goldChar = DungeonRunners.Database.CharacterRepository.GetCharacter(goldGcObj.Id);
                        if (goldChar != null)
                        {
                            goldChar.gold += goldPickupAmount;
                            DungeonRunners.Database.CharacterRepository.SaveCharacter(goldChar);
                        }
                    }
                    var goldWriter = new LEWriter();
                    goldWriter.WriteByte(0x07);
                    goldWriter.WriteByte(0x05);
                    goldWriter.WriteUInt16(targetEntityID);
                    if (conn.UnitContainerId != 0)
                    {
                        goldWriter.WriteByte(0x35);
                        goldWriter.WriteUInt16(conn.UnitContainerId);
                        goldWriter.WriteByte(0x20);
                        goldWriter.WriteUInt32(goldPickupAmount);
                        goldWriter.WriteByte(0x00);
                        goldWriter.WriteUInt32(0x00000000);
                        goldWriter.WriteByte(0x01);
                        WritePlayerEntitySynch(conn, goldWriter);
                    }
                    goldWriter.WriteByte(0x06);
                    SendToClient(conn, goldWriter.ToArray());
                }
                catch (Exception gpEx)
                {
                    Debug.LogError($"[PICKUP]  Gold credit failed: {gpEx.Message}");
                }
                return;
            }

            string connId = conn.ConnId.ToString();
            PlayerState playerState = GetPlayerState(connId);
            ushort unitContainerId = GetUnitContainerComponentId(connId);

            Debug.LogError($"[PICKUP] unitBehaviorId=0x{componentId:X4}");
            Debug.LogError($"[PICKUP] unitContainerId=0x{unitContainerId:X4}");

            var writer = new LEWriter();
            writer.WriteByte(0x07);

            writer.WriteByte(0x35);
            writer.WriteUInt16(componentId);
            writer.WriteByte(0x01);
            writer.WriteByte(responseId);
            writer.WriteByte(0x06);
            writer.WriteByte(sessionID);
            writer.WriteUInt16(targetEntityID);
            WritePlayerEntitySynch(conn, writer);
            Debug.LogError($"[PICKUP]  Wrote Activate response");

            writer.WriteByte(0x05);
            writer.WriteUInt16(targetEntityID);
            Debug.LogError($"[PICKUP]  Wrote Remove entity {targetEntityID}");

            writer.WriteByte(0x35);
            writer.WriteUInt16(unitContainerId);
            writer.WriteByte(0x28);
            string pickupGcCheck = item.GCClass.ToLower();
            bool isPickupConsumable = pickupGcCheck.Contains("potion") || pickupGcCheck.Contains("consumable")
                || pickupGcCheck.Contains("townportal") || pickupGcCheck.Contains("scroll")
                || pickupGcCheck.Contains("questitem") || pickupGcCheck.Contains("itempal")
                || pickupGcCheck.Contains("skillbook") || pickupGcCheck.Contains("voucher");
            if (isPickupConsumable)
            {
                writer.WriteByte(0xFF);
                writer.WriteCString(GCObject.GetPacketGCClassFor(item.GCClass));
                writer.WriteUInt32(0);
                writer.WriteByte(0x00);
                writer.WriteByte(0x00);
                writer.WriteByte(0x01);
                int cLevel = item.StoredLevel >= 0 ? item.StoredLevel : 1;
                writer.WriteByte((byte)cLevel);
                writer.WriteByte(0x00);
                if (pickupGcCheck.Contains("dragonjuice") || pickupGcCheck.Contains("intbuff"))
                    writer.WriteByte(0x00);
                writer.WriteByte(0x00);
            }
            else
            {
                item.WriteInitWithoutWeaponBytes(writer, playerState.Level);
            }
                WritePlayerEntitySynch(conn, writer);

            writer.WriteByte(0x06);

            byte[] packet = writer.ToArray();
            Debug.LogError($"[PICKUP] packetBytes={packet.Length}");
            Debug.LogError($"[PICKUP] hex={BitConverter.ToString(packet)}");

            SendToClient(conn, packet);

            if (isQuestPickup)
            {
                Debug.LogError($"[PICKUP] questItem={item.GCClass}");
                NotifyQuestItemAcquired(conn, item.GCClass);
            }

            foreach (var other in _connections.Values)
            {
                if (other == conn) continue;
                if (!other.IsSpawned) continue;
                if (other.CurrentZoneGcType != conn.CurrentZoneGcType) continue;
                if (other.InstanceId != conn.InstanceId) continue;
                SendDespawnEntity(other, targetEntityID);
            }

            playerState.ActiveItem = item;

            var pickedUpWeapon = DatabaseLoader.FindItem(item.GCClass);
            var pickedUpWeaponNode = GCDatabase.Instance.ResolveWithInheritance(item.GCClass);
            (float damage, float volatility, float range, float cooldown, string weaponClass, float weaponSpeed,
             string damageType, string weaponCategory, bool useProjectile, float projectileSpeed, float projectileSize, int burstCount) pickedUpWeaponStats = default;
            if (pickedUpWeaponNode == null)
                RuntimeEvidence.LogFallbackHit("damage-weapon-desc", "missing-gc-node", $"source=pickup weapon={item.GCClass} sourceFunction=Weapon::ComputeAttributes-return", 64);
            else
                pickedUpWeaponStats = GCDatabase.Instance.GetWeaponStats(item.GCClass);
            float pickedUpAuthoredDamage = pickedUpWeaponStats.damage > 0f ? pickedUpWeaponStats.damage : 0f;
            float pickedUpAuthoredVolatility = pickedUpWeaponStats.volatility > 0f ? pickedUpWeaponStats.volatility : 0.25f;
            string pickedUpWeaponClass = !string.IsNullOrEmpty(pickedUpWeaponStats.weaponClass) ? pickedUpWeaponStats.weaponClass : pickedUpWeapon != null && !string.IsNullOrEmpty(pickedUpWeapon.weaponClass) ? pickedUpWeapon.weaponClass : "";
            string pickedUpDamageType = !string.IsNullOrEmpty(pickedUpWeaponStats.damageType) ? pickedUpWeaponStats.damageType : "";
            if (pickedUpAuthoredDamage > 0f &&
                TryResolveWeaponDescIds("pickup", item.GCClass, pickedUpWeaponClass, pickedUpDamageType, out int pickedUpWeaponClassId, out int pickedUpDamageTypeId))
            {
                playerState.WeaponDamage = pickedUpAuthoredDamage;
                playerState.WeaponDamageVolatility = Mathf.Clamp(pickedUpAuthoredVolatility, 0f, 0.95f);
                playerState.WeaponLevel = Math.Max(1, item.StoredLevel >= 0 ? item.StoredLevel : DungeonRunners.Gameplay.RPGSettings.GetItemLevel(item.GCClass));
                playerState.WeaponClass = pickedUpWeaponClass;
                playerState.WeaponDamageType = pickedUpDamageType;
                playerState.WeaponCategory = !string.IsNullOrEmpty(pickedUpWeaponStats.weaponCategory) ? pickedUpWeaponStats.weaponCategory : "";
                playerState.WeaponStatsResolved = true;
                playerState.WeaponClassId = pickedUpWeaponClassId;
                playerState.DamageTypeId = pickedUpDamageTypeId;
                DamageResolver.ApplyWeaponRuntimeBaseDamage(playerState, playerState.Level, item.StoredLevel, playerState.WeaponLevel, "pickup");
                playerState.WeaponRange = pickedUpWeaponStats.range > 0 ? Mathf.RoundToInt(pickedUpWeaponStats.range) : pickedUpWeapon != null && pickedUpWeapon.range > 0 ? pickedUpWeapon.range : 0;
                playerState.WeaponCooldown = pickedUpWeaponStats.cooldown > 0 ? pickedUpWeaponStats.cooldown : pickedUpWeapon != null && pickedUpWeapon.cooldown > 0 ? pickedUpWeapon.cooldown : 0f;
                playerState.WeaponSpeed = pickedUpWeaponStats.weaponSpeed > 0 ? pickedUpWeaponStats.weaponSpeed : pickedUpWeapon != null && pickedUpWeapon.weaponSpeed > 0 ? pickedUpWeapon.weaponSpeed : 105f;
                playerState.WeaponUsesProjectile = pickedUpWeaponStats.useProjectile;
                playerState.WeaponProjectileSpeed = pickedUpWeaponStats.projectileSpeed;
                playerState.WeaponProjectileSize = pickedUpWeaponStats.projectileSize;
                playerState.WeaponBurstCount = Math.Max(1, pickedUpWeaponStats.burstCount);
                Debug.LogError($"[PICKUP] Weapon damage updated to {playerState.WeaponDamage} vol={playerState.WeaponDamageVolatility:F2} level={playerState.WeaponLevel} clientDamageLevel={playerState.WeaponDamageLevel} clientBaseDamage={playerState.WeaponBaseDamage} clientBaseSource={playerState.WeaponBaseDamageSource} class={playerState.WeaponClass}/{playerState.WeaponClassId} damageType={playerState.WeaponDamageType}/{playerState.DamageTypeId} category={playerState.WeaponCategory} range={playerState.WeaponRange} cooldown={playerState.WeaponCooldown:F2} speed={playerState.WeaponSpeed:F2} useProjectile={playerState.WeaponUsesProjectile} projectileSpeed={playerState.WeaponProjectileSpeed:F2} projectileSize={playerState.WeaponProjectileSize:F2} burst={playerState.WeaponBurstCount} from '{item.GCClass}'");
            }

            Debug.LogError($"[PICKUP]  Item picked up and now in hand!");

            if (item.GCClass != null)
                NotifyQuestItemAcquired(conn, item.GCClass);

            Debug.LogError("");
            Debug.LogError("                              ITEM PICKUP COMPLETE                             ");
            Debug.LogError("");
        }

        private void HandleItemRightClickPickup(RRConnection conn, ushort componentId, ushort targetEntityID, byte responseId, byte sessionID)
        {
            Debug.LogError($"[PICKUP-RC] Right-click target=0x{targetEntityID:X4}");

            if (_droppedItems.TryGetValue(targetEntityID, out DroppedItemInfo goldCheck) && goldCheck.GoldAmount > 0)
            {
                uint goldAmount = goldCheck.GoldAmount;
                _droppedItems.Remove(targetEntityID);
                Debug.LogError($"[GOLD-PICKUP] +{goldAmount} gold (entity 0x{targetEntityID:X4})");

                try
                {
                    if (_selectedCharacter.TryGetValue(conn.LoginName, out var lootGcObj))
                    {
                        var lootChar = DungeonRunners.Database.CharacterRepository.GetCharacter(lootGcObj.Id);
                        if (lootChar != null)
                        {
                            lootChar.gold += goldAmount;
                            DungeonRunners.Database.CharacterRepository.SaveCharacter(lootChar);
                        }
                    }
                }
                catch (Exception ex) { Debug.LogError($"[GOLD-PICKUP] db state=failed message='{ex.Message}'"); }

                string connId2 = conn.ConnId.ToString();
                ushort unitContainerId2 = GetUnitContainerComponentId(connId2);
                var gw = new LEWriter();
                gw.WriteByte(0x07);
                gw.WriteByte(0x35);
                gw.WriteUInt16(componentId);
                gw.WriteByte(0x01);
                gw.WriteByte(responseId);
                gw.WriteByte(0x06);
                gw.WriteByte(sessionID);
                gw.WriteUInt16(targetEntityID);
                WritePlayerEntitySynch(conn, gw);
                gw.WriteByte(0x05);
                gw.WriteUInt16(targetEntityID);
                if (unitContainerId2 != 0)
                {
                    gw.WriteByte(0x35);
                    gw.WriteUInt16(unitContainerId2);
                    gw.WriteByte(0x20);
                    gw.WriteUInt32(goldAmount);
                    gw.WriteByte(0x00);
                    gw.WriteUInt32(0x00000000);
                    gw.WriteByte(0x01);
                    WritePlayerEntitySynch(conn, gw);
                }
                gw.WriteByte(0x06);
                SendToClient(conn, gw.ToArray());
                return;
            }

            int droppedQty; GCObject item = GetAndRemoveDroppedItem(targetEntityID, out droppedQty);
            if (item == null)
            {
                Debug.LogError($"[PICKUP-RC] Item not in tracker - probably already picked up");
                return;
            }

            string connId = conn.ConnId.ToString();
            PlayerState playerState = GetPlayerState(connId);
            ushort unitContainerId = GetUnitContainerComponentId(connId);

            string gcCheckMerge = item.GCClass.ToLower();
            bool isStackableSimple = !gcCheckMerge.Contains("questitem")
                                     && (gcCheckMerge.Contains("potion")
                                         || gcCheckMerge.Contains("scroll")
                                         || gcCheckMerge.Contains("townportal")
                                         || gcCheckMerge.Contains("consumable")
                                         || gcCheckMerge.Contains("skillbook")
                                         || gcCheckMerge.Contains("voucher"));
            if (isStackableSimple)
            {
                int maxStack = IsPlayerFree(conn.LoginName) ? 5 : 10;
                if (_playerInventoryItems.ContainsKey(connId))
                {
                    foreach (var inventoryEntry in _playerInventoryItems[connId])
                    {
                        uint existingSlot = inventoryEntry.Key;
                        var entry = inventoryEntry.Value;
                        if (entry.item == null) continue;
                        if (!string.Equals(entry.item.GCClass, item.GCClass, StringComparison.OrdinalIgnoreCase)) continue;

                        int currentCount = GetStackCount(connId, existingSlot);
                        if (currentCount >= maxStack) continue;

                        int newCount = currentCount + droppedQty;
                        Debug.LogError($"[PICKUP-RC] STACK MERGE: {item.GCClass} -> slot {existingSlot} {currentCount}->{newCount} (max {maxStack}, free={IsPlayerFree(conn.LoginName)})");

                        var mWriter = new LEWriter();
                        mWriter.WriteByte(0x07);

                        mWriter.WriteByte(0x35);
                        mWriter.WriteUInt16(componentId);
                        mWriter.WriteByte(0x01);
                        mWriter.WriteByte(responseId);
                        mWriter.WriteByte(0x06);
                        mWriter.WriteByte(sessionID);
                        mWriter.WriteUInt16(targetEntityID);
                        WritePlayerEntitySynch(conn, mWriter);

                        mWriter.WriteByte(0x05);
                        mWriter.WriteUInt16(targetEntityID);

                        mWriter.WriteByte(0x35);
                        mWriter.WriteUInt16(unitContainerId);
                        mWriter.WriteByte(0x22);
                        mWriter.WriteUInt32(existingSlot);
                        mWriter.WriteByte((byte)(newCount > 255 ? 255 : newCount));
                        WritePlayerEntitySynch(conn, mWriter);

                        mWriter.WriteByte(0x06);

                        SendToClient(conn, mWriter.ToArray());
                        SetStackCount(connId, existingSlot, newCount);

                        foreach (var other in _connections.Values)
                        {
                            if (other == conn) continue;
                            if (!other.IsSpawned) continue;
                            if (other.CurrentZoneGcType != conn.CurrentZoneGcType) continue;
                            if (other.InstanceId != conn.InstanceId) continue;
                            SendDespawnEntity(other, targetEntityID);
                        }

                        SavePlayerInventoryPublic(conn);
                        if (item.GCClass != null) NotifyQuestItemAcquired(conn, item.GCClass);
                        return;
                    }
                }
                Debug.LogError($"[PICKUP-RC] No mergeable stack for {item.GCClass} (max {maxStack}) - creating new stack");
            }

            ItemData itemData = DatabaseLoader.FindItem(item.GCClass);
            int itemWidth = itemData?.inventoryWidth ?? 1;
            int itemHeight = itemData?.inventoryHeight ?? 1;

            var (slotX, slotY) = FindNextFreeInventorySlot(connId, itemWidth, itemHeight);
            if (slotX < 0 || slotY < 0)
            {
                Debug.LogError($"[PICKUP-RC] Inventory full - leaving item on ground");
                TrackDroppedItem(targetEntityID, item, conn);
                SendSystemMessage(conn, "Your inventory is full!");
                var fullWriter = new LEWriter();
                fullWriter.WriteByte(0x07);
                fullWriter.WriteByte(0x35);
                fullWriter.WriteUInt16(componentId);
                fullWriter.WriteByte(0x01);
                fullWriter.WriteByte(responseId);
                fullWriter.WriteByte(0x06);
                fullWriter.WriteByte(sessionID);
                fullWriter.WriteUInt16(targetEntityID);
                WritePlayerEntitySynch(conn, fullWriter);
                fullWriter.WriteByte(0x06);
                SendToClient(conn, fullWriter.ToArray());
                return;
            }

            uint trackingSlot = GetNextInventorySlot(connId);
            Debug.LogError($"[PICKUP-RC] {item.GCClass} -> slot ({slotX},{slotY}) trackingSlot={trackingSlot}");

            var writer = new LEWriter();
            writer.WriteByte(0x07);

            writer.WriteByte(0x35);
            writer.WriteUInt16(componentId);
            writer.WriteByte(0x01);
            writer.WriteByte(responseId);
            writer.WriteByte(0x06);
            writer.WriteByte(sessionID);
            writer.WriteUInt16(targetEntityID);
            WritePlayerEntitySynch(conn, writer);

            writer.WriteByte(0x05);
            writer.WriteUInt16(targetEntityID);

            writer.WriteByte(0x35);
            writer.WriteUInt16(unitContainerId);
            writer.WriteByte(0x29);
            WritePlayerEntitySynch(conn, writer);

            writer.WriteByte(0x35);
            writer.WriteUInt16(unitContainerId);
            writer.WriteByte(0x1E);
            writer.WriteByte(0x0B);

            string gcCheck = item.GCClass.ToLower();
            if (gcCheck.Contains("questitem") || gcCheck.Contains("consumable") || gcCheck.Contains("potion") || gcCheck.Contains("townportal") || gcCheck.Contains("scroll") || gcCheck.Contains("skillbook") || gcCheck.Contains("voucher"))
            {
                writer.WriteByte(0xFF);
                writer.WriteCString(GCObject.GetPacketGCClassFor(gcCheck));
                writer.WriteUInt32(trackingSlot);
                writer.WriteByte((byte)slotX);
                writer.WriteByte((byte)slotY);
                writer.WriteByte((byte)droppedQty);
                writer.WriteByte(0x01);
                writer.WriteByte(0x00);
                if (gcCheck.Contains("dragonjuice") || gcCheck.Contains("intbuff"))
                    writer.WriteByte(0x00);
                writer.WriteByte(0x00);
                SetStackCount(connId, trackingSlot, droppedQty);
            }
            else
            {
                int itemLevel = item.GetItemRequiredLevel();
                item.WriteInitForInventory(writer, (byte)slotX, (byte)slotY, trackingSlot, itemLevel);
            }
            WritePlayerEntitySynch(conn, writer);

            writer.WriteByte(0x06);

            byte[] packet = writer.ToArray();
            Debug.LogError($"[PICKUP-RC] packetBytes={packet.Length}");
            if (gcCheck.Contains("skillbook") || gcCheck.Contains("voucher"))
            {
                Debug.LogError($"[PICKUP-RC] item={item.GCClass} gcPacket={GCObject.GetPacketGCClassFor(gcCheck)} trackingSlot={trackingSlot} slot=({slotX},{slotY}) qty={droppedQty}");
                Debug.LogError($"[PICKUP-RC] hex={BitConverter.ToString(packet)}");
            }
            SendToClient(conn, packet);

            OccupyInventorySlots(connId, (byte)slotX, (byte)slotY, itemWidth, itemHeight);
            TrackInventoryItem(connId, trackingSlot, item, (byte)slotX, (byte)slotY);

            foreach (var other in _connections.Values)
            {
                if (other == conn) continue;
                if (!other.IsSpawned) continue;
                if (other.CurrentZoneGcType != conn.CurrentZoneGcType) continue;
                if (other.InstanceId != conn.InstanceId) continue;
                SendDespawnEntity(other, targetEntityID);
            }

            SavePlayerInventoryPublic(conn);

            if (item.GCClass != null)
                NotifyQuestItemAcquired(conn, item.GCClass);

            Debug.LogError($"[PICKUP-RC]  {item.GCClass} placed in inventory at ({slotX},{slotY})");
        }







        private void HandleClientControlResponse(RRConnection conn, LEReader reader, ushort componentId)
        {
            try
            {
                Debug.Log($"[CLIENT-CONTROL]  CLIENT RESPONDED TO OUR 0x64 MESSAGE!");
                Debug.Log($"[CLIENT-CONTROL] ComponentId: {componentId:X4}, Remaining bytes: {reader.Remaining}");

                while (reader.Remaining > 0)
                {
                    byte b = reader.ReadByte();
                    Debug.Log($"[CLIENT-CONTROL] Read byte: 0x{b:X2}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CLIENT-CONTROL] state=failed message='{ex.Message}'");
            }
        }

        private void HandleCancelAction(RRConnection conn, LEReader reader, ushort componentId)
        {
            try
            {
                byte sessionId = reader.ReadByte();
                Debug.LogError($"[CANCEL-ACTION] Client wants to cancel, sessionId=0x{sessionId:X2}");
                string atkKey = conn.ConnId.ToString();
                var oldTarget = Combat.WeaponUseRuntime.Instance.GetActiveTarget(atkKey);
                if (oldTarget != null)
                {
                    Debug.LogError($"[CANCEL-ACTION] Clearing target {oldTarget.Name} (UseTargets={oldTarget.UseTargetCount})");
                }
                var cancelActionMessage = new LEWriter();
                cancelActionMessage.WriteByte(0x35);
                cancelActionMessage.WriteUInt16(componentId);
                cancelActionMessage.WriteByte(0x03);
                cancelActionMessage.WriteByte(sessionId);
                if (!TryWriteEntitySynchForComponent(conn, cancelActionMessage, componentId, 0x03, EntitySynchInfoContext.ControlAck, "CANCEL-ACTION", true))
                {
                    Debug.LogError($"[CANCEL-ACTION] Dropped cancel response because Avatar HP suffix was unresolved component=0x{componentId:X4}");
                    return;
                }
                QueueClientEntityStream(conn, cancelActionMessage.ToArray());
                Debug.LogError($"[CANCEL-ACTION]  Sent cancel response");
                if (conn.HasActiveUseTarget || oldTarget != null)
                    ClearUseTargetAndReleaseControl(conn, "CANCEL-ACTION", componentId);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CANCEL-ACTION] state=failed message='{ex.Message}'");
            }
        }


        private void HandleActionType06(RRConnection conn, LEReader reader, ushort componentId)
        {
            try
            {
                Debug.Log($"[ACTION-06] Client sent 0x06 submessage, remaining bytes: {reader.Remaining}");
                while (reader.Remaining > 0)
                {
                    byte b = reader.ReadByte();
                    Debug.Log($"[ACTION-06] Read byte: 0x{b:X2}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ACTION-06] state=failed message='{ex.Message}'");
            }
        }


        public class ZonePortal
        {
            public uint Id;
            public string GCType;
            public string Name;
            public float PosX;
            public float PosY;
            public float PosZ;
            public float Heading;
            public int Width;
            public int Height;
            public string TargetZone;
            public string SpawnPoint;
            public uint Color;
        }

        public class ZoneCheckpoint
        {
            public uint Id;
            public string GCType;
            public string Name;
            public float PosX;
            public float PosY;
            public float PosZ;
            public float Heading;
        }





        private void InitializeZonePortals()
        {
            Debug.LogError("[INIT-PORTALS] phase=start source=database");

            foreach (var zone in _zones.Values)
            {
                string zoneName = zone.name.ToLower();
                var portalData = DatabaseLoader.GetPortalsForZone(zoneName);

                if (portalData == null || portalData.Count == 0)
                    continue;

                _zonePortals[zone.id] = new List<ZonePortal>();

                foreach (var data in portalData)
                {
                    var portal = new ZonePortal
                    {
                        Id = 0,
                        GCType = data.gcType,
                        Name = data.name,
                        PosX = data.posX,
                        PosY = data.posY,
                        PosZ = data.posZ,
                        Heading = data.heading,
                        Width = data.width,
                        Height = data.height,
                        TargetZone = data.targetZone,
                        SpawnPoint = data.spawnPoint,
                        Color = data.color
                    };

                    _zonePortals[zone.id].Add(portal);
                    Debug.LogError($"[INIT-PORTALS] zone={zone.name} name='{portal.Name}' id={portal.Id} target='{portal.TargetZone}'");
                }
            }

            int totalPortals = _zonePortals.Values.Sum(list => list.Count);
            Debug.LogError($"[INIT-PORTALS] total={totalPortals}");
        }

        private void InitializeZoneCheckpoints()
        {
            Debug.LogError("[INIT-CHECKPOINTS] phase=start source=database");

            foreach (var zone in _zones.Values)
            {
                string zoneName = zone.name.ToLower();
                var checkpointData = DatabaseLoader.GetCheckpointsForZone(zoneName);

                if (checkpointData == null || checkpointData.Count == 0)
                    continue;

                _zoneCheckpoints[zone.id] = new List<ZoneCheckpoint>();

                foreach (var data in checkpointData)
                {
                    var checkpoint = new ZoneCheckpoint
                    {
                        Id = 0,
                        GCType = data.gcType,
                        Name = data.name,
                        PosX = data.posX,
                        PosY = data.posY,
                        PosZ = data.posZ,
                        Heading = data.heading
                    };

                    _zoneCheckpoints[zone.id].Add(checkpoint);
                    Debug.LogError($"[INIT-CHECKPOINTS] zone={zone.name} gcType='{checkpoint.GCType}'");
                }
            }

            Debug.LogError($"[INIT-CHECKPOINTS] total={_zoneCheckpoints.Values.Sum(list => list.Count)}");

            foreach (var checkpointEntry in _zoneCheckpoints)
            {
                var zone = _zones.ContainsKey(checkpointEntry.Key) ? _zones[checkpointEntry.Key] : null;
                if (zone == null) continue;
                foreach (var checkpoint in checkpointEntry.Value)
                {
                    string cpClass = checkpoint.GCType;
                    if (cpClass.EndsWith("Entity", StringComparison.OrdinalIgnoreCase))
                        cpClass = cpClass.Substring(0, cpClass.Length - 6);
                    _checkpointZoneMap[cpClass] = zone.name;
                }
            }
            Debug.LogError($"[INIT-CHECKPOINTS] checkpointZoneMap={_checkpointZoneMap.Count}");
        }

        private enum DungeonPortalRole
        {
            Entry,
            Exit
        }

        private static bool ContainsIgnoreCase(string value, string needle)
        {
            return !string.IsNullOrEmpty(value)
                && value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int GetDungeon00LevelOrdinal(string zoneName)
        {
            if (string.IsNullOrWhiteSpace(zoneName))
                return -1;
            if (zoneName.IndexOf("boss", StringComparison.OrdinalIgnoreCase) >= 0)
                return 4;

            int nameIndex = zoneName.IndexOf("dungeon00_level", StringComparison.OrdinalIgnoreCase);
            if (nameIndex < 0)
                return -1;

            nameIndex += "dungeon00_level".Length;
            int value = 0;
            int digits = 0;
            while (nameIndex < zoneName.Length && char.IsDigit(zoneName[nameIndex]))
            {
                value = value * 10 + (zoneName[nameIndex] - '0');
                nameIndex++;
                digits++;
            }

            return digits > 0 ? value : -1;
        }

        private static DungeonPortalRole ResolveDungeonPortalRole(string currentZoneName, string gcType, string name, string targetZone, string spawnPoint)
        {
            int currentLevel = GetDungeon00LevelOrdinal(currentZoneName);
            int targetLevel = GetDungeon00LevelOrdinal(targetZone);
            if (targetLevel > 0 && currentLevel > 0 && targetLevel > currentLevel)
                return DungeonPortalRole.Exit;
            if (ContainsIgnoreCase(targetZone, "boss") && currentLevel > 0)
                return DungeonPortalRole.Exit;
            if (ContainsIgnoreCase(gcType, "oneway")
                || ContainsIgnoreCase(name, "to_level")
                || ContainsIgnoreCase(name, "to_boss"))
                return DungeonPortalRole.Exit;

            if (ContainsIgnoreCase(gcType, "hub")
                || ContainsIgnoreCase(name, "to_tutorial")
                || ContainsIgnoreCase(targetZone, "tutorial")
                || ContainsIgnoreCase(targetZone, "town"))
                return DungeonPortalRole.Entry;

            if (targetLevel > 0 && currentLevel > 0 && targetLevel <= currentLevel)
                return DungeonPortalRole.Entry;

            return DungeonPortalRole.Exit;
        }

        private static void ResolveDungeonPortalAnchor(
            DungeonMazeSpawner.ProceduralDungeonSnapshot snapshot,
            DungeonPortalRole role,
            out Vector3 position,
            out float heading,
            out int sourceIndex,
            out string tileType,
            out int gridX,
            out int gridY,
            out Vector3 local,
            out string source)
        {
            if (role == DungeonPortalRole.Entry)
            {
                position = snapshot.EntryPortalSpawn;
                heading = snapshot.EntryPortalHeading;
                sourceIndex = snapshot.EntrySourceIndex;
                tileType = snapshot.EntryTileType;
                gridX = snapshot.EntryGridX;
                gridY = snapshot.EntryGridY;
                local = snapshot.EntryPortalAnchorLocal;
                source = snapshot.EntryPortalAnchorSource;
                return;
            }

            position = snapshot.ExitPortalSpawn;
            heading = snapshot.ExitPortalHeading;
            sourceIndex = snapshot.ExitSourceIndex;
            tileType = snapshot.ExitTileType;
            gridX = snapshot.ExitGridX;
            gridY = snapshot.ExitGridY;
            local = snapshot.ExitPortalAnchorLocal;
            source = snapshot.ExitPortalAnchorSource;
        }

        private const uint DungeonPortalHubColor = 0x8200ADFFu;

        private void SendCombatStart(Gameplay.DuelRuntime.DuelInfo duel)
        {
            var challenger = FindConnectionByLogin(duel.ChallengerLogin);
            var target     = FindConnectionByLogin(duel.TargetLogin);
            if (challenger != null)
            {
                SendToClient(challenger, PVPPackets.BuildDuelStatus(
                    PVPPackets.DuelStatusType.InProgress, duel.TargetCharSqlId, 0, 0));
                SendToClient(challenger, PVPPackets.BuildPVPStatusChanged(pvpState: 1, matchId: 0));
            }
            if (target != null)
            {
                SendToClient(target, PVPPackets.BuildDuelStatus(
                    PVPPackets.DuelStatusType.InProgress, duel.ChallengerCharSqlId, 0, 0));
                SendToClient(target, PVPPackets.BuildPVPStatusChanged(pvpState: 1, matchId: 0));
            }
            Debug.LogError($"[PVP-DUEL] Combat start sent: {duel.ChallengerLogin} vs {duel.TargetLogin}");
        }

        private void SendDuelEndPackets(string winnerLogin, string loserLogin, Gameplay.DuelRuntime.DuelInfo duel)
        {
            uint winnerCharSqlId = string.Equals(duel.ChallengerLogin, winnerLogin, StringComparison.OrdinalIgnoreCase)
                ? duel.ChallengerCharSqlId : duel.TargetCharSqlId;
            uint loserCharSqlId  = string.Equals(duel.ChallengerLogin, loserLogin,  StringComparison.OrdinalIgnoreCase)
                ? duel.ChallengerCharSqlId : duel.TargetCharSqlId;

            var winnerConn = FindConnectionByLogin(winnerLogin);
            var loserConn  = FindConnectionByLogin(loserLogin);

            if (winnerConn != null)
            {
                SendToClient(winnerConn, PVPPackets.BuildDuelStatus(
                    PVPPackets.DuelStatusType.Won, loserCharSqlId, 0, 0));
                SendToClient(winnerConn, PVPPackets.BuildPVPStatusChanged(pvpState: 0, matchId: 0));
            }
            if (loserConn != null)
            {
                SendToClient(loserConn, PVPPackets.BuildDuelStatus(
                    PVPPackets.DuelStatusType.Lost, winnerCharSqlId, 0, 0));
                SendToClient(loserConn, PVPPackets.BuildPVPStatusChanged(pvpState: 0, matchId: 0));
            }
            Debug.LogError($"[PVP-DUEL] End packets sent: winner={winnerLogin} loser={loserLogin}");
        }

        private RRConnection FindConnectionByLogin(string loginName)
        {
            return FindConnectionByName(loginName);
        }

        private DateTime _lastMatchmakingTick = DateTime.MinValue;
    }
}
