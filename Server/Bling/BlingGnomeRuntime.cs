using System;
using System.Collections;
using System.Collections.Generic;
using DungeonRunners.Engine;
using DungeonRunners.Utilities;
using DungeonRunners.Core;
using DungeonRunners.Data;

namespace DungeonRunners.Networking
{
    public class BlingGnomeRuntime : MonoBehaviour
    {
        private static BlingGnomeRuntime _instance;
        public static BlingGnomeRuntime Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("BlingGnomeRuntime");
                    _instance = go.AddComponent<BlingGnomeRuntime>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private class GnomeState
        {
            public ushort EntityId;
            public ushort BehaviorId;
            public ushort ModifiersId;
            public ushort ManipulatorsId;
            public uint OwnerEntityId;

            public float PosX, PosY, PosZ;
            public float Heading;

            public float SpawnTime;
            public float ActivateUntil;

            public int ItemsConverted;
            public uint GoldGenerated;
            public uint HitPointsWire;

            public bool BehaviorCreated;
            public bool IsActive;
            public int FidgetSerial;
            public ushort LastFidgetAnim;
            public float NextFidgetTime;
            public float NextSearchTime;
            public float SpawnPosX, SpawnPosY;
            public float SpawnHeading;
            public bool HasMoveTarget;
            public float MoveTargetX, MoveTargetY;
            public float LastFollowRuntimeAt;
            public bool LastMoverUpdateValid;
            public float LastMoverUpdateAt;
            public float LastMoverUpdatePosX, LastMoverUpdatePosY;
            public float LastMoverUpdateHeading;
            public bool LastMoverUpdateTerminal;

            public Coroutine BehaviorCoroutine;
            public Coroutine ConversionCoroutine;
            public SendPacketDelegate SendPacket;
            public SendMessageDelegate SendMessage;
            public byte SnapshotLevel = 1;
            public byte[] ComponentPacket;
        }

        private Dictionary<int, GnomeState> _gnomes = new Dictionary<int, GnomeState>();
        private uint _nextEntityId = 0x6000;


        private const string ENTITY_GC_TYPE = "creatures.summon.blinggnome.base.BlingGnome_Summon";
        private const string BEHAVIOR_GC_TYPE = "creatures.summon.blinggnome.base.BlingGnome_Summon.Behavior";
        private const string SKILL_TYPE = "skills.generic.SummonBlingGnome";

        private const float CONVERSION_RATIO = 0.5f;
        private const float GOLD_VALUE_MOD = 2.3f;
        private const int CONVERT_SEARCH_RADIUS = 200;
        private const int BEHAVIOR_SEARCH_RANGE = 250;
        private const int GNOME_SPEED = 50;
        private const float COOLDOWN_SECONDS = 45f;
        private const float EFFECT_DURATION_SECONDS = 10f;
        private const float CONVERT_BOUNCE_SECONDS = 2.05f;
        private const int IDLE_BASE_SECONDS = 2;
        private const int IDLE_VARIABLE_SECONDS = 7;

        private const float FOLLOW_START_RADIUS = 30f;
        private const float FOLLOW_SETTLE_RADIUS = 30f;
        private const float FOLLOW_TOO_CLOSE = 10f;
        private const float FOLLOW_RETARGET_DEADBAND = 20f;
        private const float FOLLOW_MAX_STEP_CADENCE = 0.132f;
        private const float FOLLOW_DEFAULT_STEP_CADENCE = 0.099f;
        private const float FOLLOW_STEP_MIN_INTERVAL = 0.033f;
        private const float MOVER_UPDATE_ACTIVE_MIN_INTERVAL = 0.150f;
        private const float MOVER_UPDATE_SETTLED_MIN_INTERVAL = 0.500f;
        private const float MOVER_UPDATE_ACTIVE_MAX_DRIFT = 14f;
        private const float MOVER_UPDATE_SETTLED_MAX_DRIFT = 18f;
        private const float LEASH_SNAP_RADIUS = 480f;

        private const float BEHAVIOR_TICK_INTERVAL = 0.15f;
        private const float SEARCH_PULSE_SECONDS = 1.0f;
        private const float BEHAVIOR_CREATE_DELAY = 0.066f;
        private const float DESPAWN_SECONDS = 120f;

        private const ushort ANIM_IDLE = 100;
        private const ushort ANIM_FIDGET_FIRST = 101;
        private const ushort ANIM_FIDGET_LAST = 104;
        private const ushort ANIM_PICKUP = 110;
        private const ushort ANIM_ACTIVE_SKILL = 140;
        private const ushort ANIM_DEATH = 150;
        private const ushort ANIM_SPAWN = 180;

        private const byte ANIM_STATE_DIRECT = 0x00;
        private const byte ANIM_STATE_ACTIVE = 0x06;
        private const byte ANIM_STATE_FIDGET = 0x08;

        private const ushort SOUND_FIDGET = 111;
        private const int FIDGET_SOUND_CHANCE_DIVISOR = 3;

        private const float BLING_HEALTH_SCALE = 0.3f;
        private static readonly int[] HENCHMAN_HEALTH_LEVELS = { 1, 5, 10, 50, 75, 100, 110 };
        private static readonly float[] HENCHMAN_HEALTH_VALUES = { 245f, 1550f, 2810f, 10989f, 19646f, 26009f, 28020f };

        private const int ENTITY_INIT_MODE = 2;

        private static ushort ComputeNPCComponentId(ushort entityId, ushort slot)
        {
            if (entityId == 0) return slot;
            uint id = ((uint)entityId & 0xFF00u) + 0x0100u + ((uint)entityId & 0x00FFu) * 0x10u + slot;
            if (id <= 0xFFFF) return (ushort)id;
            return (ushort)(entityId + 0x0100 + slot);
        }

        private const int COMPONENT_CREATE_MODE = 3;

        private GameServer _server;
        private System.Random _rng = new System.Random();
        private static readonly bool SendServerGnomeMoverUpdates = true;
        private static readonly bool VerboseGnomeLog = System.Environment.GetEnvironmentVariable("DR_SERVER_VERBOSE_GNOME") == "1";

        public delegate void SendPacketDelegate(RRConnection conn, byte dest, byte type, byte[] data);
        public delegate void SendMessageDelegate(RRConnection conn, string message);

        public void SetServer(GameServer server) => _server = server;
        public void SetNextEntityId(uint id) => _nextEntityId = id;
        public uint GetNextEntityId() => _nextEntityId++;
        public bool HasGnome(int connId) => _gnomes.ContainsKey(connId);

        private void BroadcastGnomePacket(RRConnection ownerConn, GnomeState g, byte[] packet)
        {
            EnqueueGnomeBlock(ownerConn, packet);
            if (_server == null) return;
            foreach (var peer in _server.GetInstancePeerConnections(ownerConn))
                EnqueueGnomeBlock(peer, packet);
        }

        private static void EnqueueGnomeBlock(RRConnection conn, byte[] packet)
        {
            if (conn == null || packet == null || packet.Length < 2) return;
            if (packet[0] == 0x07 && packet[packet.Length - 1] == 0x06)
            {
                var inner = new byte[packet.Length - 2];
                Array.Copy(packet, 1, inner, 0, inner.Length);
                conn.MessageQueue.Enqueue(inner);
            }
            else
            {
                conn.MessageQueue.Enqueue(packet);
            }
        }

        public void SendGnomesToConnection(RRConnection viewer)
        {
            if (viewer == null || _server == null) return;
            foreach (var entry in _gnomes)
            {
                if (entry.Key == viewer.ConnId) continue;
                var g = entry.Value;
                var ownerConn = _server.GetConnectionByConnId(entry.Key);
                if (ownerConn == null || !ownerConn.IsConnected || !ownerConn.IsSpawned) continue;
                if (!string.Equals(ownerConn.RuntimeInstanceKey ?? "", viewer.RuntimeInstanceKey ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                uint hitPointsWire = g.HitPointsWire;
                if (EntitySynchInfo.EntitySynchInfoAuthority.Instance.TryGetBlingGnomeRuntimeHP(g.EntityId, out uint runtimeHPWire))
                    hitPointsWire = runtimeHPWire;
                ushort viewerOwnerEntityId = _server.ResolveRemotePlayerEntityId(viewer, ownerConn);
                g.SendPacket(viewer, 0x01, 0x0F, BuildEntitySnapshotPacket(g, viewerOwnerEntityId, g.SnapshotLevel, hitPointsWire));
                if (g.ComponentPacket != null) g.SendPacket(viewer, 0x01, 0x0F, g.ComponentPacket);
                Debug.LogError($"[GNOME-SPAWN] late-join replication entity=0x{g.EntityId:X4} owner={ownerConn.LoginName} viewer={viewer.LoginName} ownerRef=0x{viewerOwnerEntityId:X4}");
            }
        }

        private static float ResolveCurveValue(int level, int[] levels, float[] values, float fallback)
        {
            if (levels == null || values == null || levels.Length == 0 || levels.Length != values.Length)
                return fallback;

            level = Mathf.Max(1, level);
            if (level <= levels[0])
                return values[0];

            for (int curveIndex = 1; curveIndex < levels.Length; curveIndex++)
            {
                if (level <= levels[curveIndex])
                {
                    int prevLevel = levels[curveIndex - 1];
                    int nextLevel = levels[curveIndex];
                    int prevValueI = (int)values[curveIndex - 1];
                    int nextValueI = (int)values[curveIndex];
                    if (nextLevel <= prevLevel)
                        return nextValueI;
                    return prevValueI + ((nextValueI - prevValueI) * (level - prevLevel)) / (nextLevel - prevLevel);
                }
            }

            return values[values.Length - 1];
        }

        private static uint ResolveBlingGnomeHitPointsWire(int level)
        {
            int curveRaw = ToFixedRounded(ResolveCurveValue(level, HENCHMAN_HEALTH_LEVELS, HENCHMAN_HEALTH_VALUES, 245f));
            int scaleRaw = ToFixedRounded(BLING_HEALTH_SCALE);
            long health = ((long)curveRaw * scaleRaw) >> 16;
            if (health < 1)
                health = 1;
            return (uint)health * 256u;
        }

        public uint GetGnomeEntityId(int connId)
        {
            if (_gnomes.TryGetValue(connId, out var g)) return g.EntityId;
            return 0;
        }

        public bool TryResolveGnomeTarget(RRConnection conn, ushort targetEntityId,
            out uint entityId, out ushort behaviorId, out bool behaviorCreated, out string reason)
        {
            entityId = 0;
            behaviorId = 0;
            behaviorCreated = false;

            if (!TryResolveGnomeState(conn, targetEntityId, out var g, out _, out reason))
                return false;

            entityId = g.EntityId;
            behaviorId = g.BehaviorId;
            behaviorCreated = g.BehaviorCreated;
            return targetEntityId == 0 || g.EntityId == targetEntityId;
        }

        private bool TryResolveGnomeState(RRConnection conn, ushort targetEntityId,
            out GnomeState g, out int stateConnId, out string reason)
        {
            g = null;
            stateConnId = 0;
            reason = "none";

            if (conn == null)
            {
                reason = "nil-conn";
                return false;
            }

            if (_gnomes.TryGetValue(conn.ConnId, out g))
            {
                stateConnId = conn.ConnId;
                if (targetEntityId == 0 || g.EntityId == targetEntityId)
                {
                    reason = "owned-conn";
                    return true;
                }

                reason = $"owned-target-mismatch expected={g.EntityId} got={targetEntityId}";
                return false;
            }

            if (targetEntityId != 0)
            {
                int foundConnId = 0;
                GnomeState found = null;
                foreach (var gnomeEntry in _gnomes)
                {
                    if (gnomeEntry.Value.EntityId != targetEntityId)
                        continue;
                    foundConnId = gnomeEntry.Key;
                    found = gnomeEntry.Value;
                    break;
                }

                if (found != null)
                {
                    uint avatarEntityId = conn.Avatar != null ? (uint)conn.Avatar.Id : 0;
                    if (found.OwnerEntityId != 0 && avatarEntityId != 0 && found.OwnerEntityId != avatarEntityId)
                    {
                        reason = $"target-owned-by-other owner={found.OwnerEntityId} avatar={avatarEntityId}";
                        return false;
                    }

                    if (foundConnId != conn.ConnId)
                    {
                        _gnomes.Remove(foundConnId);
                        _gnomes[conn.ConnId] = found;
                        reason = $"target-rekey oldConn={foundConnId} newConn={conn.ConnId}";
                        Debug.LogError($"[GNOME-TARGET] Rebound Bling Gnome entity={found.EntityId} from conn={foundConnId} to conn={conn.ConnId}");
                    }
                    else
                    {
                        reason = "target-entity";
                    }

                    g = found;
                    stateConnId = conn.ConnId;
                    return true;
                }
            }

            reason = "no-gnome";
            return false;
        }

        public void CleanupForZoneTransition(int connId)
        {
            if (_gnomes.TryGetValue(connId, out var g))
            {
                Debug.LogError($"[GNOME] Cleaning up gnome (ID=0x{g.EntityId:X4}) for zone transition");
                var ownerConn = _server?.GetConnectionByConnId(connId);
                if (ownerConn != null && _server != null)
                {
                    var despawn = new LEWriter();
                    despawn.WriteByte(0x07);
                    despawn.WriteByte(0x05);
                    despawn.WriteUInt16(g.EntityId);
                    despawn.WriteByte(0x06);
                    byte[] despawnPacket = despawn.ToArray();
                    foreach (var peer in _server.GetInstancePeerConnections(ownerConn))
                        g.SendPacket(peer, 0x01, 0x0F, despawnPacket);
                }
                if (g.BehaviorCoroutine != null) StopCoroutine(g.BehaviorCoroutine);
                if (g.ConversionCoroutine != null) StopCoroutine(g.ConversionCoroutine);
                g.BehaviorCreated = false;
                EntitySynchInfo.EntitySynchInfoAuthority.Instance.UnregisterBlingGnome(g.EntityId);
                _gnomes.Remove(connId);
            }
        }


        public void TryExecute(RRConnection conn, byte[] data, SendPacketDelegate sendPacket, SendMessageDelegate sendMessage)
        {
            try
            {
                if (data == null || data.Length < 1) return;
                var reader = new LEReader(data);
                string message = reader.ReadCString();
                if (message.StartsWith("@"))
                {
                    string command = message.Substring(1).ToLower().Trim();
                    if (command == "gnome" || command == "bling" || command == "blinggnome")
                        ToggleGnomeFromChat(conn, sendPacket, sendMessage);
                    else if (command == "gnomestatus" || command == "gs")
                        ShowGnomeStatus(conn, sendMessage);
                }
            }
            catch (Exception ex) { Debug.LogError($"[GNOME-CHAT] state=failed message='{ex.Message}'"); }
        }

        private void ShowGnomeStatus(RRConnection conn, SendMessageDelegate sendMessage)
        {
            if (!_gnomes.TryGetValue(conn.ConnId, out var gnome))
            {
                sendMessage(conn, "[Bling Gnome] No gnome active. Use skill or @gnome to summon!");
                return;
            }
            string activeStatus = gnome.IsActive ? $" | ACTIVE ({Mathf.Max(0f, gnome.ActivateUntil - Time.time):F0}s)" : "";
            sendMessage(conn, $"[Bling Gnome] Items: {gnome.ItemsConverted} | Gold: {gnome.GoldGenerated} | Persistent{activeStatus}");
        }

        private void ResolveOwnerAnchor(RRConnection conn, out float posX, out float posY, out float posZ, out float heading)
        {
            if (conn != null && conn.HasLivePlayerPosition)
            {
                posX = conn.LivePlayerPosX;
                posY = conn.LivePlayerPosY;
                posZ = conn.LivePlayerPosZ;
                heading = conn.LivePlayerHeading;
                return;
            }

            posX = conn != null ? conn.PlayerPosX : 0f;
            posY = conn != null ? conn.PlayerPosY : 0f;
            posZ = conn != null ? conn.PlayerPosZ : 0f;
            heading = conn != null ? conn.PlayerHeading : 0f;
        }


        public void ToggleGnome(RRConnection conn, SendPacketDelegate sendPacket, SendMessageDelegate sendMessage)
        {
            if (_gnomes.ContainsKey(conn.ConnId))
            {
                ActivateGnome(conn);
            }
            else
            {
                SpawnGnome(conn, sendPacket, sendMessage);
            }
        }

        public void ToggleGnomeFromChat(RRConnection conn, SendPacketDelegate sendPacket, SendMessageDelegate sendMessage)
        {
            if (_gnomes.ContainsKey(conn.ConnId))
            {
                var g = _gnomes[conn.ConnId];
                if (g.GoldGenerated > 0)
                    sendMessage(conn, $"[Bling Gnome] Converted {g.ItemsConverted} items into {g.GoldGenerated} gold!");
                DespawnGnome(conn, sendPacket);
            }
            else
            {
                SpawnGnome(conn, sendPacket, sendMessage);
            }
        }

        public bool ActivateGnome(RRConnection conn)
        {
            return ActivateGnome(conn, 0);
        }

        public bool ActivateGnome(RRConnection conn, ushort targetEntityId)
        {
            if (!TryResolveGnomeState(conn, targetEntityId, out var g, out _, out string reason))
            {
                Debug.LogError($"[GNOME-ACTIVATE] skipped target={targetEntityId} reason={reason}");
                return false;
            }

            if (!g.BehaviorCreated)
            {
                Debug.LogError($"[GNOME-ACTIVATE] skipped target={targetEntityId} entity={g.EntityId} reason=behavior-not-created match={reason}");
                return false;
            }

            g.IsActive = true;
            g.ActivateUntil = Time.time + EFFECT_DURATION_SECONDS;
            g.ItemsConverted = 0;
            g.GoldGenerated = 0;
            g.NextSearchTime = Time.time;

            SendPlayAnimation(conn, g, ANIM_ACTIVE_SKILL, ANIM_STATE_ACTIVE, 40);
            SendConvertItemsToGoldAction(conn, g);
            if (g.ConversionCoroutine != null) StopCoroutine(g.ConversionCoroutine);
            g.ConversionCoroutine = StartCoroutine(ApplyActiveConversion(conn, g));

            Debug.LogError($"[GNOME-ACTIVATE] Conversion window open for {EFFECT_DURATION_SECONDS}s, radius={CONVERT_SEARCH_RADIUS}, target={targetEntityId}, match={reason}");
            return true;
        }


        public void SpawnGnome(RRConnection conn, SendPacketDelegate sendPacket, SendMessageDelegate sendMessage)
        {
            Debug.LogError($"[GNOME-SPAWN] Spawning BlingGnome_Summon for {conn.LoginName}");

            ushort gnomeId = (ushort)_nextEntityId++;

            ushort modifiersId = ComputeNPCComponentId(gnomeId, 0x05);
            ushort manipulatorsId = ComputeNPCComponentId(gnomeId, 0x06);
            ushort behaviorId = ComputeNPCComponentId(gnomeId, 0x07);
            Debug.LogError($"[GNOME-SPAWN] entity=0x{gnomeId:X4} mod=0x{modifiersId:X4} manip=0x{manipulatorsId:X4} beh=0x{behaviorId:X4}");

            string zoneName = conn.CurrentZoneName ?? "Town";
            ResolveOwnerAnchor(conn, out float ownerX, out float ownerY, out float ownerZ, out float ownerHeading);
            float posXf = ownerX + 5f;
            float posYf = ownerY + 5f;
            float posZf = PathMapCatalog.Instance.GetHeight(zoneName, posXf, posYf, ownerZ);
            float headingf = ownerHeading;

            var g = new GnomeState
            {
                EntityId = gnomeId,
                BehaviorId = behaviorId,
                ModifiersId = modifiersId,
                ManipulatorsId = manipulatorsId,
                OwnerEntityId = conn.Avatar != null ? (uint)conn.Avatar.Id : 0,
                PosX = posXf,
                PosY = posYf,
                PosZ = posZf,
                Heading = headingf,
                SpawnPosX = posXf,
                SpawnPosY = posYf,
                SpawnHeading = headingf,
                SpawnTime = Time.time,
                BehaviorCreated = false,
                IsActive = false,
                NextFidgetTime = Time.time + GetFidgetDelay(),
                NextSearchTime = Time.time + SEARCH_PULSE_SECONDS,
                SendPacket = sendPacket,
                SendMessage = sendMessage
            };
            _gnomes[conn.ConnId] = g;

            SendEntitySnapshot(conn, g);

            if (COMPONENT_CREATE_MODE > 0)
            {
                StartCoroutine(DelayedBehaviorCreate(conn, g));
            }
            else
            {
                Debug.LogError("[GNOME] componentCreateMode=0 skipping component create");
            }

            if (COMPONENT_CREATE_MODE >= 3)
            {
                g.BehaviorCoroutine = StartCoroutine(GnomeBehaviorLoop(conn));
            }
        }

        private void SendEntitySnapshot(RRConnection conn, GnomeState g)
        {
            ushort ownerEntityId = 0;
            if (conn.Player != null && conn.Player.Id > 0)
                ownerEntityId = (ushort)conn.Player.Id;
            if (ownerEntityId == 0 && _server != null)
                ownerEntityId = (ushort)_server.GetPlayerAvatarId(conn.LoginName);

            int level = conn.PlayerLevel;
            if (_server != null)
            {
                PlayerState playerState = _server.GetPlayerState(conn.ConnId.ToString());
                if (playerState != null && playerState.Level > 0)
                    level = playerState.Level;
            }
            level = Mathf.Clamp(level, 1, 255);
            g.SnapshotLevel = (byte)level;
            uint hitPointsWire = ResolveBlingGnomeHitPointsWire(level);
            g.HitPointsWire = hitPointsWire;
            EntitySynchInfo.EntitySynchInfoAuthority.Instance.RegisterBlingGnome(g.EntityId, g.BehaviorId, hitPointsWire, $"BlingGnome#{g.EntityId}");

            byte[] packet = BuildEntitySnapshotPacket(g, ownerEntityId, g.SnapshotLevel, hitPointsWire);
            Debug.LogError($"[GNOME-SPAWN] Entity snapshot: {packet.Length} bytes, mode={ENTITY_INIT_MODE}, hex={BitConverter.ToString(packet)}");
            g.SendPacket(conn, 0x01, 0x0F, packet);
            if (_server == null) return;
            foreach (var peer in _server.GetInstancePeerConnections(conn))
            {
                ushort peerOwnerEntityId = _server.ResolveRemotePlayerEntityId(peer, conn);
                g.SendPacket(peer, 0x01, 0x0F, BuildEntitySnapshotPacket(g, peerOwnerEntityId, g.SnapshotLevel, hitPointsWire));
                Debug.LogError($"[GNOME-SPAWN] peer replication entity=0x{g.EntityId:X4} viewer={peer.LoginName} owner=0x{peerOwnerEntityId:X4}");
            }
        }

        private byte[] BuildEntitySnapshotPacket(GnomeState g, ushort ownerEntityId, byte level, uint hitPointsWire)
        {
            int posX_fx = ToFixed(g.PosX);
            int posY_fx = ToFixed(g.PosY);
            int posZ_fx = ToFixed(g.PosZ);
            int heading_fx = ToFixed(g.Heading);
            byte unitFlags = 0x16;
            if (ownerEntityId != 0)
                unitFlags = (byte)(unitFlags | 0x01);
            const uint manaPointsWire = 0;

            var writer = new LEWriter();
            writer.WriteByte(0x07);

            writer.WriteByte(0x01);
            writer.WriteUInt16(g.EntityId);
            writer.WriteByte(0xFF);
            writer.WriteCString(ENTITY_GC_TYPE);

            if (ENTITY_INIT_MODE >= 1)
            {
                writer.WriteByte(0x02);
                writer.WriteUInt16(g.EntityId);

                writer.WriteUInt32(0x06);
                writer.WriteInt32(posX_fx);
                writer.WriteInt32(posY_fx);
                writer.WriteInt32(posZ_fx);
                writer.WriteInt32(heading_fx);
                writer.WriteByte(0x00);

                writer.WriteByte(unitFlags);
                writer.WriteByte(level);
                writer.WriteUInt16(0);
                writer.WriteUInt16(0);
                if ((unitFlags & 0x01) != 0)
                    writer.WriteUInt16(ownerEntityId);
                writer.WriteUInt32(hitPointsWire);
                writer.WriteUInt32(manaPointsWire);
                writer.WriteByte(0x00);

                writer.WriteByte(0x00);
                writer.WriteUInt16(0); writer.WriteUInt16(0);
                writer.WriteByte(0x00);
                writer.WriteUInt16(0); writer.WriteUInt32(0);
                writer.WriteByte(0x00);
                writer.WriteUInt32(0); writer.WriteUInt32(0); writer.WriteUInt32(0);
                Debug.LogError($"[GNOME-SPAWN]  StockUnit init: unitFlags=0x{unitFlags:X2}, level={level}, owner=0x{ownerEntityId:X4}, hp=0x{hitPointsWire:X8}, mana=0x{manaPointsWire:X8}");
            }

            writer.WriteByte(0x06);
            return writer.ToArray();
        }

        private IEnumerator DelayedBehaviorCreate(RRConnection conn, GnomeState g)
        {
            yield return new WaitForSeconds(BEHAVIOR_CREATE_DELAY);

            if (!_gnomes.TryGetValue(conn.ConnId, out var current) || !object.ReferenceEquals(current, g) || !conn.IsConnected)
                yield break;

            int posX_fx = ToFixed(g.PosX);
            int posY_fx = ToFixed(g.PosY);



            if (COMPONENT_CREATE_MODE >= 1)
            {
                var writer = new LEWriter();
                writer.WriteByte(0x07);

                writer.WriteByte(0x32);
                writer.WriteUInt16(g.EntityId);
                writer.WriteUInt16(g.ModifiersId);
                writer.WriteByte(0xFF);
                writer.WriteCString("Modifiers");
                writer.WriteByte(0x01);
                writer.WriteUInt32(0);
                writer.WriteUInt32(0);
                writer.WriteByte(0x00);

                if (COMPONENT_CREATE_MODE >= 2)
                {
                    writer.WriteByte(0x32);
                    writer.WriteUInt16(g.EntityId);
                    writer.WriteUInt16(g.ManipulatorsId);
                    writer.WriteByte(0xFF);
                    writer.WriteCString("Manipulators");
                    writer.WriteByte(0x01);
                    writer.WriteByte(0x00);
                }

                if (COMPONENT_CREATE_MODE >= 3)
                {
                    int anchorX_fx = ToFixed(g.PosX);
                    int anchorY_fx = ToFixed(g.PosY);

                    writer.WriteByte(0x32);
                    writer.WriteUInt16(g.EntityId);
                    writer.WriteUInt16(g.BehaviorId);
                    writer.WriteByte(0xFF);
                    writer.WriteCString(BEHAVIOR_GC_TYPE);
                    writer.WriteByte(0x01);

                    writer.WriteByte(0x00);
                    writer.WriteByte(0x00);
                    writer.WriteByte(0x00);
                    writer.WriteByte(0x01);
                    writer.WriteByte(0x42);
                    writer.WriteInt32(anchorX_fx);
                    writer.WriteInt32(anchorY_fx);
                    writer.WriteByte(0x01);
                    writer.WriteByte(0x00);
                    writer.WriteByte(0x00);
                    writer.WriteByte(0x00);

                    writer.WriteByte(0x09);
                    writer.WriteUInt16(0x0000);

                    writer.WriteInt32(anchorX_fx);
                    writer.WriteInt32(anchorY_fx);
                    WritePlayAnimationSubmsg(writer, g, ANIM_SPAWN, ANIM_STATE_DIRECT, 24);
                }

                writer.WriteByte(0x06);

                byte[] packet = writer.ToArray();
                Debug.LogError($"[GNOME-COMPONENT] all-in-one packet bytes={packet.Length} mode={COMPONENT_CREATE_MODE}");
                g.ComponentPacket = packet;
                BroadcastGnomePacket(conn, g, packet);
            }

            if (COMPONENT_CREATE_MODE >= 3)
            {
                g.BehaviorCreated = true;
                Debug.LogError($"[GNOME-COMPONENT] components=3 mod=0x{g.ModifiersId:X4} manip=0x{g.ManipulatorsId:X4} beh=0x{g.BehaviorId:X4}");

                Debug.LogError("[GNOME-COMPONENT] spawnAnim=180 inline");
            }
            else
            {
                Debug.LogError($"[GNOME-COMPONENT] componentCreateMode={COMPONENT_CREATE_MODE} skipping behavior/create anim");
            }
        }


        public void DespawnGnome(RRConnection conn, SendPacketDelegate sendPacket, bool playDeathAnim = true)
        {
            if (!_gnomes.TryGetValue(conn.ConnId, out var g)) return;
            if (g.BehaviorCoroutine != null) StopCoroutine(g.BehaviorCoroutine);
            if (g.ConversionCoroutine != null) StopCoroutine(g.ConversionCoroutine);

            if (playDeathAnim && g.BehaviorCreated && conn.IsConnected)
            {
                SendPlayAnimation(conn, g, ANIM_DEATH, ANIM_STATE_DIRECT, 24);
                _gnomes.Remove(conn.ConnId);
                StartCoroutine(DelayedEntityRemove(conn, g, sendPacket, 0.8f));
            }
            else
            {
                _gnomes.Remove(conn.ConnId);
                if (conn.IsConnected)
                    SendEntityRemove(conn, g, sendPacket);
                EntitySynchInfo.EntitySynchInfoAuthority.Instance.UnregisterBlingGnome(g.EntityId);
            }
        }

        private IEnumerator DelayedEntityRemove(RRConnection conn, GnomeState g, SendPacketDelegate sendPacket, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (conn != null && conn.IsConnected)
                SendEntityRemove(conn, g, sendPacket);
            if (g.ConversionCoroutine != null) StopCoroutine(g.ConversionCoroutine);
            EntitySynchInfo.EntitySynchInfoAuthority.Instance.UnregisterBlingGnome(g.EntityId);
        }

        private void SendEntityRemove(RRConnection conn, GnomeState g, SendPacketDelegate sendPacket)
        {
            var writer = new LEWriter();
            writer.WriteByte(0x07);
            writer.WriteByte(0x05);
            writer.WriteUInt16(g.EntityId);
            writer.WriteByte(0x06);
            byte[] packet = writer.ToArray();
            sendPacket(conn, 0x01, 0x0F, packet);
            if (_server != null)
            {
                foreach (var peer in _server.GetInstancePeerConnections(conn))
                    sendPacket(peer, 0x01, 0x0F, packet);
            }
            Debug.LogError($"[GNOME] Despawned 0x{g.EntityId:X4} - {g.ItemsConverted} items -> {g.GoldGenerated}g");
        }


        private IEnumerator GnomeBehaviorLoop(RRConnection conn)
        {
            yield return new WaitForSeconds(BEHAVIOR_CREATE_DELAY + 0.2f);

            while (conn != null && conn.IsConnected && _gnomes.TryGetValue(conn.ConnId, out var g))
            {
                float now = Time.time;

                if (g.IsActive && now >= g.ActivateUntil)
                {
                    g.IsActive = false;
                    Debug.LogError("[GNOME-BEHAVIOR] Activation window expired");
                }

                UpdateFollowMirror(conn, g, now);

                if (now >= g.NextSearchTime)
                {
                    g.NextSearchTime = now + SEARCH_PULSE_SECONDS;
                    if (TryFindNearestGoldDrop(conn, g, out var closestId, out var closestInfo))
                    {
                        yield return StartCoroutine(PickupGroundItem(conn, g, closestId, closestInfo));
                        yield return new WaitForSeconds(BEHAVIOR_TICK_INTERVAL);
                        continue;
                    }
                }

                if (g.BehaviorCreated && now >= g.NextFidgetTime && !g.IsActive && !g.HasMoveTarget)
                {
                    ushort fidgetAnim = PickFidgetAnimation(g);
                    if (fidgetAnim != 0)
                    {
                        SendPlayAnimation(conn, g, fidgetAnim, ANIM_STATE_FIDGET, fidgetAnim == 103 ? 50u : 40u);
                        g.NextFidgetTime = now + GetFidgetDelay();
                    }
                }

                yield return new WaitForSeconds(BEHAVIOR_TICK_INTERVAL);
            }
        }


        private IEnumerator PickupGroundItem(RRConnection conn, GnomeState g, ushort entityId, GameServer.DroppedItemInfo item)
        {
            bool goldDrop = IsGoldLike(item);
            if (g.BehaviorCreated && !goldDrop)
                SendPlayAnimation(conn, g, ANIM_PICKUP, ANIM_STATE_DIRECT, 16);

            yield return new WaitForSeconds(goldDrop ? 0.1f : 0.3f);

            if (!_gnomes.TryGetValue(conn.ConnId, out var current) || current.EntityId != g.EntityId || !conn.IsConnected)
                yield break;

            uint goldAmount;
            if (goldDrop)
            {
                goldAmount = item.GoldAmount;
            }
            else if (current.IsActive && CanConvertItem(conn, item))
            {
                goldAmount = CalculateConversionGold(conn, item);
            }
            else
            {
                yield break;
            }

            if (goldAmount == 0) goldAmount = 1;

            if (!goldDrop)
                yield return new WaitForSeconds(CONVERT_BOUNCE_SECONDS);

            if (!_gnomes.TryGetValue(conn.ConnId, out current) || current.EntityId != g.EntityId || !conn.IsConnected)
                yield break;

            if (!_server.BlingGnomePickupItem(conn, entityId))
                yield break;

            _server.BlingGnomeCreditGold(conn, goldAmount);

            current.ItemsConverted++;
            current.GoldGenerated += goldAmount;

            Debug.LogError($"[GNOME-PICKUP] Item 0x{entityId:X4} -> {goldAmount}g (total: {current.GoldGenerated}g)");

            yield return new WaitForSeconds(0.2f);
        }

        private IEnumerator ApplyActiveConversion(RRConnection conn, GnomeState g)
        {
            yield return new WaitForSeconds(0.15f);

            if (!_gnomes.TryGetValue(conn.ConnId, out var current) || current.EntityId != g.EntityId || !conn.IsConnected)
                yield break;

            string zone = conn.CurrentZoneName ?? "";
            uint instId = conn.InstanceId;
            ResolveOwnerAnchor(conn, out float ownerX, out float ownerY, out _, out _);
            var candidates = new List<(ushort entityId, GameServer.DroppedItemInfo info)>();
            var seen = new HashSet<ushort>();

            AddConversionCandidates(_server.GetDroppedItemsNear(zone, instId, current.PosX, current.PosY, CONVERT_SEARCH_RADIUS), conn, candidates, seen);
            AddConversionCandidates(_server.GetDroppedItemsNear(zone, instId, ownerX, ownerY, CONVERT_SEARCH_RADIUS), conn, candidates, seen);
            candidates.Sort((a, b) => a.entityId.CompareTo(b.entityId));

            var pendingConversions = new List<(ushort entityId, uint goldAmount)>();
            Debug.LogError($"[GNOME-CONVERT] Search pulse radius={CONVERT_SEARCH_RADIUS} candidates={candidates.Count}");
            foreach (var candidate in candidates)
            {
                if (!_gnomes.TryGetValue(conn.ConnId, out current) || current.EntityId != g.EntityId || !current.IsActive || !conn.IsConnected)
                    yield break;
                if (!CanConvertItem(conn, candidate.info))
                    continue;

                uint goldAmount = CalculateConversionGold(conn, candidate.info);
                if (goldAmount == 0) goldAmount = 1;

                pendingConversions.Add((candidate.entityId, goldAmount));
            }

            if (pendingConversions.Count == 0)
                yield break;

            Debug.LogError($"[GNOME-CONVERT] BounceAndConvert pending={pendingConversions.Count} bounce={CONVERT_BOUNCE_SECONDS:F2}s");
            yield return new WaitForSeconds(CONVERT_BOUNCE_SECONDS);

            foreach (var pending in pendingConversions)
            {
                if (!_gnomes.TryGetValue(conn.ConnId, out current) || current.EntityId != g.EntityId || !conn.IsConnected)
                    yield break;
                if (current.BehaviorCreated)
                    SendPlayAnimation(conn, current, ANIM_PICKUP, ANIM_STATE_DIRECT, 16);
                yield return new WaitForSeconds(0.3f);
                if (!_gnomes.TryGetValue(conn.ConnId, out current) || current.EntityId != g.EntityId || !conn.IsConnected)
                    yield break;
                if (!_server.BlingGnomePickupItem(conn, pending.entityId))
                    continue;

                _server.BlingGnomeCreditGold(conn, pending.goldAmount);

                current.ItemsConverted++;
                current.GoldGenerated += pending.goldAmount;

                Debug.LogError($"[GNOME-GOLDGENERATED] Item 0x{pending.entityId:X4} -> {pending.goldAmount}g (total: {current.GoldGenerated}g)");
                yield return new WaitForSeconds(0.05f);
            }
        }

        private void AddConversionCandidates(List<(ushort entityId, GameServer.DroppedItemInfo info)> items, RRConnection conn, List<(ushort entityId, GameServer.DroppedItemInfo info)> candidates, HashSet<ushort> seen)
        {
            if (items == null) return;
            foreach (var item in items)
            {
                if (seen.Contains(item.entityId)) continue;
                if (!CanConvertItem(conn, item.info)) continue;
                seen.Add(item.entityId);
                candidates.Add(item);
            }
        }

        private bool TryFindNearestGoldDrop(RRConnection conn, GnomeState g, out ushort entityId, out GameServer.DroppedItemInfo item)
        {
            entityId = 0;
            item = null;
            float closestDistSq = float.MaxValue;
            string zone = conn.CurrentZoneName ?? "";
            uint instId = conn.InstanceId;
            ResolveOwnerAnchor(conn, out float ownerX, out float ownerY, out _, out _);

            ConsiderGoldCandidates(_server.GetDroppedItemsNear(zone, instId, g.PosX, g.PosY, BEHAVIOR_SEARCH_RANGE), conn, g, ownerX, ownerY, ref entityId, ref item, ref closestDistSq);
            ConsiderGoldCandidates(_server.GetDroppedItemsNear(zone, instId, ownerX, ownerY, BEHAVIOR_SEARCH_RANGE), conn, g, ownerX, ownerY, ref entityId, ref item, ref closestDistSq);
            return item != null;
        }

        private void ConsiderGoldCandidates(List<(ushort entityId, GameServer.DroppedItemInfo info)> items, RRConnection conn, GnomeState g, float ownerX, float ownerY, ref ushort entityId, ref GameServer.DroppedItemInfo closestItem, ref float closestDistSq)
        {
            if (items == null) return;
            foreach (var candidate in items)
            {
                var info = candidate.info;
                if (!IsGoldLike(info) || !IsAllowedForOwner(conn, info)) continue;
                float distSq = Mathf.Min(
                    DistSq(info.PosX, info.PosY, g.PosX, g.PosY),
                    DistSq(info.PosX, info.PosY, ownerX, ownerY));
                if (distSq >= closestDistSq) continue;
                closestDistSq = distSq;
                entityId = candidate.entityId;
                closestItem = info;
            }
        }

        private bool IsAllowedForOwner(RRConnection conn, GameServer.DroppedItemInfo item)
        {
            if (item == null) return false;
            return string.IsNullOrWhiteSpace(item.DroppedBy) || string.Equals(item.DroppedBy, conn.LoginName ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsGoldLike(GameServer.DroppedItemInfo item)
        {
            return item != null && (item.IsGoldDrop || item.GoldAmount > 0);
        }

        private bool CanConvertItem(RRConnection conn, GameServer.DroppedItemInfo item)
        {
            if (item == null || item.Item == null) return false;
            if (item.IsQuestItem || IsGoldLike(item) || !IsAllowedForOwner(conn, item)) return false;
            if (item.Item.GetEffectiveRarity() > 3) return false;
            string gc = item.Item.GCClass ?? "";
            return gc.IndexOf("quest", StringComparison.OrdinalIgnoreCase) < 0
                && gc.IndexOf("unique", StringComparison.OrdinalIgnoreCase) < 0
                && gc.IndexOf("collection", StringComparison.OrdinalIgnoreCase) < 0
                && gc.IndexOf("mythic", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private uint CalculateConversionGold(RRConnection conn, GameServer.DroppedItemInfo item)
        {
            uint baseGold = _server.GetItemGoldValue(item);
            if (baseGold == 0) baseGold = (uint)Math.Max(1, item.PlayerLevel * 2);

            float converted = baseGold * CONVERSION_RATIO * GOLD_VALUE_MOD;
            return (uint)Math.Max(1, (int)converted);
        }


        private void SendMoverUpdate(RRConnection conn, GnomeState g, bool terminal, float now)
        {
            if (!g.BehaviorCreated) return;
            if (!SendServerGnomeMoverUpdates) return;
            if (!_gnomes.TryGetValue(conn.ConnId, out var liveG) || !ReferenceEquals(liveG, g)) return;

            float prevHeading = g.LastMoverUpdateValid ? g.LastMoverUpdateHeading : g.SpawnHeading;
            float prevX = g.LastMoverUpdateValid ? g.LastMoverUpdatePosX : g.SpawnPosX;
            float prevY = g.LastMoverUpdateValid ? g.LastMoverUpdatePosY : g.SpawnPosY;
            byte flags = BuildBlingMoverFlags(prevHeading, prevX, prevY, g.Heading, g.PosX, g.PosY, terminal);

            var writer = new LEWriter();
            writer.WriteByte(0x07);

            writer.WriteByte(0x35);
            writer.WriteUInt16(g.BehaviorId);
            writer.WriteByte(0x65);
            writer.WriteByte(0x00);

            if (terminal)
            {
                writer.WriteByte(0x02);
                WriteMoverRecord(writer, (byte)((BuildBlingMoverFlags(g.Heading, g.PosX, g.PosY, g.Heading, g.PosX + 1f, g.PosY, false) | 0x06) & 0x06), g.Heading, g.PosX + 1f, g.PosY);
                WriteMoverRecord(writer, (byte)((flags | 0x03) & 0x07), g.Heading, g.PosX, g.PosY);
            }
            else
            {
                writer.WriteByte(0x01);
                WriteMoverRecord(writer, flags, g.Heading, g.PosX, g.PosY);
            }

            WriteGnomeEntitySynchInfo(writer, g);
            writer.WriteByte(0x06);

            BroadcastGnomePacket(conn, g, writer.ToArray());
            g.LastMoverUpdateValid = true;
            g.LastMoverUpdateAt = now;
            g.LastMoverUpdatePosX = g.PosX;
            g.LastMoverUpdatePosY = g.PosY;
            g.LastMoverUpdateHeading = g.Heading;
            g.LastMoverUpdateTerminal = terminal;
        }

        private void UpdateFollowMirror(RRConnection conn, GnomeState g, float now)
        {
            if (!g.BehaviorCreated) return;

            ResolveOwnerAnchor(conn, out float ownerX, out float ownerY, out float ownerZ, out float ownerHeading);
            float followX = ownerX;
            float followY = ownerY;
            int offsetIndex = g.EntityId % 4;
            float offsetX = (offsetIndex == 1 || offsetIndex == 3) ? -18f : 18f;
            float offsetY = offsetIndex >= 2 ? -10f : 10f;
            if (DistSq(g.PosX, g.PosY, ownerX, ownerY) < FOLLOW_TOO_CLOSE * FOLLOW_TOO_CLOSE)
            {
                followX += offsetX;
                followY += offsetY;
            }

            float followZ = ownerZ;
            float followHeading = ownerHeading;
            float ownerDistSq = DistSq(g.PosX, g.PosY, ownerX, ownerY);
            float followDistSq = DistSq(g.PosX, g.PosY, followX, followY);
            bool followMoveActive = g.HasMoveTarget;
            bool followShouldPath = ShouldBlingFollowPath(followMoveActive, ownerDistSq, followDistSq);
            bool changed = false;
            bool stateChanged = false;

            if (followShouldPath)
            {
                if (g.HasMoveTarget && DistSq(g.MoveTargetX, g.MoveTargetY, followX, followY) < FOLLOW_RETARGET_DEADBAND * FOLLOW_RETARGET_DEADBAND)
                {
                    followX = g.MoveTargetX;
                    followY = g.MoveTargetY;
                }

                if (!g.HasMoveTarget || !NearlyEqual(g.MoveTargetX, followX) || !NearlyEqual(g.MoveTargetY, followY))
                {
                    g.MoveTargetX = followX;
                    g.MoveTargetY = followY;
                    g.HasMoveTarget = true;
                    stateChanged = true;
                }

                bool moveDue = g.LastFollowRuntimeAt <= 0f || now - g.LastFollowRuntimeAt >= FOLLOW_STEP_MIN_INTERVAL;
                if (moveDue)
                {
                    float dx = followX - g.PosX;
                    float dy = followY - g.PosY;
                    if (!NearlyEqual(dx, 0f) || !NearlyEqual(dy, 0f))
                    {
                        float step = ComputeFollowStep(g, now);
                        ProjectStep(g.PosX, g.PosY, followX, followY, step, out float newX, out float newY);
                        float heading = HeadingFromDelta(dx, dy, g.Heading);
                        if (!NearlyEqual(g.PosX, newX) || !NearlyEqual(g.PosY, newY))
                        {
                            g.PosX = newX;
                            g.PosY = newY;
                            changed = true;
                        }
                        if (!NearlyEqual(g.Heading, heading))
                        {
                            g.Heading = heading;
                            changed = true;
                        }
                    }
                    g.LastFollowRuntimeAt = now;
                }

                if (!NearlyEqual(g.PosZ, followZ))
                {
                    g.PosZ = followZ;
                    changed = true;
                }

                if (NearlyEqual(g.PosX, followX) && NearlyEqual(g.PosY, followY))
                {
                    if (g.HasMoveTarget || !NearlyEqual(g.MoveTargetX, 0f) || !NearlyEqual(g.MoveTargetY, 0f))
                    {
                        g.HasMoveTarget = false;
                        g.MoveTargetX = 0f;
                        g.MoveTargetY = 0f;
                        stateChanged = true;
                    }
                    if (!NearlyEqual(g.Heading, followHeading))
                    {
                        g.Heading = followHeading;
                        changed = true;
                    }
                }
            }
            else if (followMoveActive)
            {
                if (g.HasMoveTarget || !NearlyEqual(g.MoveTargetX, 0f) || !NearlyEqual(g.MoveTargetY, 0f))
                {
                    g.HasMoveTarget = false;
                    g.MoveTargetX = 0f;
                    g.MoveTargetY = 0f;
                    stateChanged = true;
                }
                if (!NearlyEqual(g.PosZ, followZ))
                {
                    g.PosZ = followZ;
                    changed = true;
                }
                if (!NearlyEqual(g.Heading, followHeading))
                {
                    g.Heading = followHeading;
                    changed = true;
                }
            }

            bool terminalUpdateNeeded = !followShouldPath && !g.HasMoveTarget && g.LastMoverUpdateValid && !g.LastMoverUpdateTerminal;
            if ((changed || stateChanged || terminalUpdateNeeded) && MoverUpdateDue(g, now))
            {
                SendMoverUpdate(conn, g, !g.HasMoveTarget, now);
            }
        }

        private bool ShouldBlingFollowPath(bool followMoveActive, float ownerDistSq, float followDistSq)
        {
            if (ownerDistSq < FOLLOW_TOO_CLOSE * FOLLOW_TOO_CLOSE && followDistSq > FOLLOW_RETARGET_DEADBAND * FOLLOW_RETARGET_DEADBAND)
                return true;
            if (followDistSq <= FOLLOW_SETTLE_RADIUS * FOLLOW_SETTLE_RADIUS)
                return false;
            if (followMoveActive)
                return true;
            return ownerDistSq > FOLLOW_START_RADIUS * FOLLOW_START_RADIUS;
        }

        private bool MoverUpdateDue(GnomeState g, float now)
        {
            float prevX = g.LastMoverUpdateValid ? g.LastMoverUpdatePosX : g.SpawnPosX;
            float prevY = g.LastMoverUpdateValid ? g.LastMoverUpdatePosY : g.SpawnPosY;
            float prevHeading = g.LastMoverUpdateValid ? g.LastMoverUpdateHeading : g.SpawnHeading;
            bool terminal = !g.HasMoveTarget;
            float movedSq = DistSq(prevX, prevY, g.PosX, g.PosY);
            bool headingChanged = !NearlyEqual(prevHeading, g.Heading);
            bool terminalChanged = g.LastMoverUpdateValid && g.LastMoverUpdateTerminal != terminal;

            if (movedSq <= 0f && !headingChanged && !terminalChanged)
                return false;
            if (!g.LastMoverUpdateValid || g.LastMoverUpdateAt <= 0f)
                return true;
            if (terminalChanged)
                return true;
            if (g.HasMoveTarget)
            {
                if (movedSq >= MOVER_UPDATE_ACTIVE_MAX_DRIFT * MOVER_UPDATE_ACTIVE_MAX_DRIFT)
                    return true;
                return now - g.LastMoverUpdateAt >= MOVER_UPDATE_ACTIVE_MIN_INTERVAL;
            }
            if (movedSq >= MOVER_UPDATE_SETTLED_MAX_DRIFT * MOVER_UPDATE_SETTLED_MAX_DRIFT)
                return true;
            return now - g.LastMoverUpdateAt >= MOVER_UPDATE_SETTLED_MIN_INTERVAL;
        }

        private byte BuildBlingMoverFlags(float prevHeading, float prevX, float prevY, float heading, float posX, float posY, bool terminal)
        {
            byte flags = 0x00;
            if (terminal) flags |= 0x01;
            if (!NearlyEqual(prevHeading, heading)) flags |= 0x02;
            if (!terminal && (!NearlyEqual(prevX, posX) || !NearlyEqual(prevY, posY)))
            {
                flags |= 0x02;
                flags |= 0x04;
            }
            return (byte)(flags & 0x07);
        }

        private void WriteMoverRecord(LEWriter writer, byte flags, float heading, float posX, float posY)
        {
            writer.WriteByte((byte)(flags & 0x07));
            writer.WriteInt32(ToFixed(heading));
            writer.WriteInt32(ToFixed(posX));
            writer.WriteInt32(ToFixed(posY));
        }

        private void WritePlayAnimationSubmsg(LEWriter writer, GnomeState g, ushort logicalId, byte animState, uint durationTicks)
        {
            byte wireState = animState;
            uint wireArg = logicalId;

            if (logicalId == ANIM_ACTIVE_SKILL)
            {
                wireState = ANIM_STATE_ACTIVE;
                wireArg = 40;
            }
            else if (logicalId >= ANIM_FIDGET_FIRST && logicalId <= ANIM_FIDGET_LAST)
            {
                wireState = ANIM_STATE_DIRECT;
                wireArg = logicalId;
            }

            writer.WriteByte(0x35);
            writer.WriteUInt16(g.BehaviorId);
            writer.WriteByte(0x04);
            writer.WriteByte(0x20);
            writer.WriteByte(0x00);
            writer.WriteUInt32(wireState);
            writer.WriteUInt32(wireArg);
            writer.WriteUInt32(durationTicks);
            writer.WriteUInt32(0x3F800000);
            WriteGnomeEntitySynchInfo(writer, g);
        }

        private void SendPlayAnimation(RRConnection conn, GnomeState g, ushort logicalId, byte animState, uint durationTicks)
        {
            if (!g.BehaviorCreated) return;
            if (!_gnomes.TryGetValue(conn.ConnId, out var liveG) || !ReferenceEquals(liveG, g)) return;

            var writer = new LEWriter();
            writer.WriteByte(0x07);

            WritePlayAnimationSubmsg(writer, g, logicalId, animState, durationTicks);

            writer.WriteByte(0x06);

            BroadcastGnomePacket(conn, g, writer.ToArray());
        }

        private void SendConvertItemsToGoldAction(RRConnection conn, GnomeState g)
        {
            if (!g.BehaviorCreated) return;
            if (!_gnomes.TryGetValue(conn.ConnId, out var liveG) || !ReferenceEquals(liveG, g)) return;

            ResolveOwnerAnchor(conn, out float ownerX, out float ownerY, out _, out _);
            int sourceX_fx = ToFixed(ownerX);
            int sourceY_fx = ToFixed(ownerY);
            int goldValueMod_fx = (int)(GOLD_VALUE_MOD * 256);

            var writer = new LEWriter();
            writer.WriteByte(0x07);

            writer.WriteByte(0x35);
            writer.WriteUInt16(g.BehaviorId);
            writer.WriteByte(0x04);
            writer.WriteByte(0xA1);

            writer.WriteByte(0x00);
            writer.WriteUInt32(5);
            writer.WriteUInt16((ushort)CONVERT_SEARCH_RADIUS);
            writer.WriteInt32(sourceX_fx);
            writer.WriteInt32(sourceY_fx);
            writer.WriteInt32(goldValueMod_fx);

            WriteGnomeEntitySynchInfo(writer, g);
            writer.WriteByte(0x06);

            BroadcastGnomePacket(conn, g, writer.ToArray());
            Debug.LogError($"[GNOME-CONVERT] ConvertItemsToGold action sent, radius={CONVERT_SEARCH_RADIUS}, mod={GOLD_VALUE_MOD}");
        }


        private void WriteGnomeEntitySynchInfo(LEWriter writer, GnomeState g)
        {
            EntitySynchInfo.EntitySynchInfoAuthority.Instance.TryGetBlingGnomeRuntimeHP(g.EntityId, out uint hitPointsWire);

            writer.WriteByte(0x02);
            writer.WriteUInt32(hitPointsWire);
            if (VerboseGnomeLog)
                Debug.LogError($"[ENTITY-SYNCH-INFO] packet=GNOME owner=BlingGnome entity={g.EntityId} component={g.BehaviorId} flags=0x02 hp={hitPointsWire}");
        }

        private ushort PickFidgetAnimation(GnomeState gnome)
        {
            ushort[] candidates = { ANIM_FIDGET_FIRST, 102, 103, ANIM_FIDGET_LAST };
            gnome.FidgetSerial++;

            int candidateIndex = _rng.Next(candidates.Length);
            ushort animationId = candidates[candidateIndex];

            if (animationId == gnome.LastFidgetAnim && candidates.Length > 1)
                animationId = candidates[(candidateIndex + 1) % candidates.Length];

            gnome.LastFidgetAnim = animationId;
            return animationId;
        }

        private float GetFidgetDelay()
        {
            return IDLE_BASE_SECONDS + (float)(_rng.NextDouble() * IDLE_VARIABLE_SECONDS);
        }

        private float ComputeFollowStep(GnomeState g, float now)
        {
            float cadence = FOLLOW_DEFAULT_STEP_CADENCE;
            if (g.LastFollowRuntimeAt > 0f)
                cadence = Mathf.Max(0f, now - g.LastFollowRuntimeAt);
            if (cadence > FOLLOW_MAX_STEP_CADENCE)
                cadence = FOLLOW_MAX_STEP_CADENCE;
            float step = Mathf.Round(GNOME_SPEED * cadence);
            return Mathf.Max(1f, step);
        }

        private static void ProjectStep(float posX, float posY, float targetX, float targetY, float step, out float newX, out float newY)
        {
            float dx = targetX - posX;
            float dy = targetY - posY;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            if (dist <= 0f || dist <= step)
            {
                newX = targetX;
                newY = targetY;
                return;
            }
            float scale = step / dist;
            newX = posX + dx * scale;
            newY = posY + dy * scale;
        }

        private static float HeadingFromDelta(float dx, float dy, float fallback)
        {
            if (NearlyEqual(dx, 0f) && NearlyEqual(dy, 0f))
                return fallback;
            float heading = Mathf.Round(Mathf.Atan2(-dx, dy) * Mathf.Rad2Deg);
            while (heading < 0f) heading += 360f;
            while (heading >= 360f) heading -= 360f;
            return heading;
        }

        private static bool NearlyEqual(float a, float b)
        {
            return Mathf.Abs(a - b) < 0.001f;
        }

        private static int ToFixed(float v) => (int)(v * 256f);
        private static int ToFixedRounded(float v) => v >= 0f ? (int)(v * 256f + 0.5f) : (int)(v * 256f - 0.5f);
        private static float DistSq(float ax, float ay, float bx, float by)
        {
            float dx = ax - bx, dy = ay - by;
            return dx * dx + dy * dy;
        }


        public void OnPlayerDisconnect(int connId)
        {
            if (_gnomes.TryGetValue(connId, out var g))
            {
                if (g.BehaviorCoroutine != null) StopCoroutine(g.BehaviorCoroutine);
                if (g.ConversionCoroutine != null) StopCoroutine(g.ConversionCoroutine);
                EntitySynchInfo.EntitySynchInfoAuthority.Instance.UnregisterBlingGnome(g.EntityId);
                _gnomes.Remove(connId);
            }
        }
    }
}
