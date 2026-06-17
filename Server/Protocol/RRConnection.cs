using System;
using System.Collections.Generic;
using System.Net.Sockets;
using DungeonRunners.Engine;
using DungeonRunners.Data;
using DungeonRunners.Core;

namespace DungeonRunners.Networking
{
    public class RRConnection
    {
        public int ConnId { get; private set; }
        public TcpClient Client { get; private set; }
        public NetworkStream Stream { get; private set; }
        public object SendLock { get; } = new object();
        public bool IsConnected { get; set; }
        public string LoginName { get; set; }
        public uint PeerId24 { get; set; }

        public uint CurrentZoneId { get; set; }
        public string CurrentZoneGcType { get; set; } = "world.town";

        public float PlayerPosX { get; set; } = 480.0f;
        public float PlayerPosY { get; set; } = -191.0f;
        public float PlayerHeading { get; set; } = 0.0f;
        public readonly System.Collections.Generic.Queue<(float X, float Y)> AvatarAggroSampleQueue = new System.Collections.Generic.Queue<(float, float)>();
        public float AggroSamplePosX { get; set; } = 480.0f;
        public float AggroSamplePosY { get; set; } = -191.0f;
        public byte SessionID { get; set; } = 0;
        public uint UnitBehaviorId { get; set; }
        public float LastPositionUpdateTime { get; set; } = 0f;
        public HashSet<uint> SentPassiveModifierIds { get; } = new HashSet<uint>();
        public bool HasLastRelayPos { get; set; } = false;
        public float LastRelayPosX { get; set; } = 0f;
        public float LastRelayPosY { get; set; } = 0f;
        public byte LastRawMoveCount { get; set; } = 0;
        public byte[] LastRawMoveData { get; set; } = null;
        public float IgnoreClientHPUntilTime { get; set; } = 0f;
        public uint LastOutboundHPWire { get; set; } = 0;
        public float LastOutboundHPTime { get; set; } = 0f;
        public string LastOutboundHPSource { get; set; } = null;
        public uint LastObservedClientHPWire { get; set; } = 0;
        public float LastObservedClientHPTime { get; set; } = 0f;
        public string LastObservedClientHPSource { get; set; } = null;
        public bool HasActiveUseTarget { get; set; } = false;
        public ushort ActiveUseTargetId { get; set; } = 0;
        public byte ActiveUseTargetFlags { get; set; } = 0;
        public ushort ActiveUseTargetComponentId { get; set; } = 0;
        public byte ActiveUseTargetSessionId { get; set; } = 0;
        public bool ActiveUseTargetInitUsePassed { get; set; } = false;
        public bool ActiveUseTargetStartedWeaponUse { get; set; } = false;
        public bool ActiveUseTargetVisibleHit { get; set; } = false;
        public float ActiveUseTargetInitUseRange { get; set; } = 0f;
        public float ActiveUseTargetInitUseDistance { get; set; } = 0f;
        public float ActiveUseTargetClientTolerance { get; set; } = 0f;
        public long ActiveUseTargetLastProjectileSeq { get; set; } = 0;
        public int ActiveUseTargetLastImpactTick { get; set; } = -1;
        public int LastAvatarPreSuffixActionSliceFrame { get; set; } = -1;
        public bool PendingClientControlReset { get; set; } = false;
        public ushort PendingClientControlResetComponentId { get; set; } = 0;
        public float PendingClientControlResetNextAttemptTime { get; set; } = 0f;
        public byte PendingClientControlResetAttempts { get; set; } = 0;
        public ushort UnitContainerId { get; set; } = 0;
        public ushort ModifiersId { get; set; } = 0;
        public bool ZoneSpawnInvulnerabilityActive { get; set; } = false;
        public float ZoneSpawnInvulnerabilityExpiresAt { get; set; } = 0f;
        public float ZoneSpawnInvulnerabilitySentAt { get; set; } = 0f;
        public int ZoneSpawnInvulnerabilityClearFrame { get; set; } = -1;

        public bool IsAdmin { get; set; } = true;

        public ushort DialogManagerId { get; set; } = 0;
        public string CurrentDialogNpcId { get; set; } = null;
        public string PendingMerchantNpcGcClass { get; set; } = null;
        public ushort PendingMerchantComponentId { get; set; } = 0;
        public float PendingMerchantTargetX { get; set; } = 0f;
        public float PendingMerchantTargetY { get; set; } = 0f;
        public string ActiveMerchantNpcGcClass { get; set; } = null;
        public ushort ActiveMerchantComponentId { get; set; } = 0;
        public DateTime ActiveMerchantRefreshDueUtc { get; set; } = DateTime.MinValue;
        public bool ActiveMerchantRefreshReady { get; set; } = false;
        public bool HasActiveMerchantRefresh { get; set; } = false;
        public uint NextQuestInstanceId = 1;
        public uint PendingQuestHash { get; set; } = 0;
        public ushort QuestManagerId { get; set; } = 0;
        public uint PendingQuestNpcEntityId { get; set; } = 0;
        public uint ViewingQuestInstanceId { get; set; } = 0;
        public uint PendingTurnInInstanceId { get; set; } = 0;
        public bool IsAbandonConfirmed { get; set; } = false;
        public int AbandonClickCount { get; set; } = 0;

        public MessageQueue MessageQueue { get; private set; }
        public byte MovementGeneration = 0;
        public Coroutine TickCoroutine { get; set; }
        public GCObject Avatar { get; set; }
        public GCObject Player { get; set; }
        public uint UpdateNumber { get; set; } = 0;
        public ClientEntitySchedulerMirror EntitySchedulerMirror { get; } = new ClientEntitySchedulerMirror();

        public float PendingSpawnX;
        public float PendingSpawnY;
        public float PendingSpawnZ;
        public string PendingSpawnPoint { get; set; } = "";

        public string CurrentZoneName { get; set; } = "";
        public uint InstanceId { get; set; } = 0;
        public string RuntimeInstanceKey { get; set; } = "";
        public string AvatarGcType { get; set; } = "";
        public string ClassName { get; set; } = "Fighter";
        public ushort SkillsComponentId { get; set; } = 0;
        public ushort ManipulatorsComponentId { get; set; } = 0;
        public ushort ModifiersComponentId { get; set; } = 0;
        public ushort BehaviorComponentId { get; set; } = 0;
        public int PlayerLevel { get; set; } = 1;
        public uint CharSqlId { get; set; } = 0;
        public bool GroupConnectedSent { get; set; } = false;
        public float PlayerPosZ { get; set; } = 0f;
        public bool HasLivePlayerPosition { get; set; } = false;
        public float LivePlayerPosX { get; set; } = 0f;
        public float LivePlayerPosY { get; set; } = 0f;
        public float LivePlayerPosZ { get; set; } = 0f;
        public float LivePlayerHeading { get; set; } = 0f;
        public float LivePlayerPositionTime { get; set; } = 0f;
        public bool IsSpawned { get; set; } = false;
        public bool UseTargetMovingActive { get; set; } = false;
        public float UseTargetMovingX { get; set; } = 0f;
        public float UseTargetMovingY { get; set; } = 0f;
        public int UseTargetMovingFollowConnId { get; set; } = 0;
        public ushort UseTargetMovingEntityId { get; set; } = 0;
        public string UseTargetMovingInstanceKey { get; set; } = "";
        public byte UseTargetMovingSampleCount { get; set; } = 0;
        public byte[] UseTargetMovingSampleData { get; } = new byte[39];
        public byte UseTargetMovingDelayTicks { get; set; } = 0;
        public float UseTargetMovingStartTime { get; set; } = 0f;
        public bool FullHPBaselineOnNextSpawn { get; set; } = false;
        public bool RespawnFullHPPending { get; set; } = false;
        public bool AllowFlush { get; set; } = false;

        public bool HasSavedTownPortal { get; set; } = false;
        public string TownPortalZoneName { get; set; } = "";
        public string TownPortalTargetZone { get; set; } = "";
        public uint TownPortalZoneId { get; set; } = 0;
        public float TownPortalPosX { get; set; }
        public float TownPortalPosY { get; set; }
        public float TownPortalPosZ { get; set; }

        public string ZonePortalSource { get; set; } = "";
        public int ObeliskClickIndex { get; set; } = 0;

        public RRConnection(int connId, TcpClient client, NetworkStream stream)
        {
            ConnId = connId;
            Client = client;
            Stream = stream;
            IsConnected = true;
            LoginName = null;
            PeerId24 = 0;
            CurrentZoneId = 0;
            MessageQueue = new MessageQueue();
        }

        public void Disconnect()
        {
            IsConnected = false;
            try
            {
                Stream?.Close();
                Client?.Close();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RR-CONNECTION] disconnect state=error message='{ex.Message}'");
            }
        }
    }
}
