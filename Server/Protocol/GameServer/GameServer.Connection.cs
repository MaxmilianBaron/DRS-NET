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
        private Dictionary<int, RRConnection> _connections = new Dictionary<int, RRConnection>();

        private Dictionary<string, Dictionary<string, DateTime>> _debuffCooldowns
            = new Dictionary<string, Dictionary<string, DateTime>>(StringComparer.OrdinalIgnoreCase);

        public RRConnection GetConnectionByConnId(int connId)
            => _connections.TryGetValue(connId, out var conn) ? conn : null;

        public List<RRConnection> GetInstancePeerConnections(RRConnection conn)
        {
            var peers = new List<RRConnection>();
            if (conn == null) return peers;
            foreach (var other in _connections.Values)
            {
                if (other == null || other == conn) continue;
                if (!other.IsConnected || !other.IsSpawned || !other.AllowFlush) continue;
                if (!string.Equals(other.RuntimeInstanceKey ?? "", conn.RuntimeInstanceKey ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                peers.Add(other);
            }
            return peers;
        }

        private void HandleDecryptedUDPPacket(UDPSession session, byte[] data)
        {
            if (data == null || data.Length < 1) return;

            byte msgType = data[0];
            _udpPacketCount++;

            if (VerbosePacketLogging)
            {
                string fingerprint = BitConverter.ToString(data, 0, Math.Min(data.Length, 12));
                if (_seenPacketPatterns.Add(fingerprint))
                {
                    string fullHex = BitConverter.ToString(data, 0, Math.Min(data.Length, 40));
                    Debug.LogError($"[NEW-PKT] #{_udpPacketCount} type=0x{msgType:X2} len={data.Length} hex={fullHex}");
                }

                if (_udpPacketCount % 500 == 0)
                    Debug.LogError($"[UDP-COUNT] {_udpPacketCount} packets, {_seenPacketPatterns.Count} unique patterns");
            }

            WirePacketTally.OnUDP(data);

            if (session.Connection == null)
            {
                Debug.LogError("[UDP-RX] no connection linked to UDP session");
                return;
            }

            byte[] payload = data.Length > 1 ? data.Skip(1).ToArray() : new byte[0];
            HandleClientEntityChannel(session.Connection, msgType, payload);
        }

        private void SendEncryptedUDP(UDPSession session, byte[] data)
        {
            if (session == null || !session.IsEstablished) return;
            byte[] encrypted = EncryptUDP(session, data);
            if (encrypted != null)
                _udpListener.Send(encrypted, encrypted.Length, session.Endpoint);
        }

        public void SendRNGSeedUDP(RRConnection conn, uint seed)
        {
            try
            {
                Debug.LogError($"[UDP-RNG] Starting send, conn={(conn != null ? conn.LoginName : "NULL")}");

                var session = GetUDPSessionForConnection(conn);
                if (session != null && session.IsEstablished)
                {
                    var writer = new LEWriter();
                    writer.WriteByte(0x07);
                    writer.WriteByte(0x0C);
                    writer.WriteUInt32(seed);
                    writer.WriteByte(0x06);

                    byte[] packet = writer.ToArray();
                    Debug.LogError($"[UDP-RNG] Packet: {BitConverter.ToString(packet)}");

                    int padLen = (8 - (packet.Length % 8)) % 8;
                    if (padLen > 0)
                    {
                        byte[] padded = new byte[packet.Length + padLen];
                        Array.Copy(packet, padded, packet.Length);
                        packet = padded;
                    }

                    SendEncryptedUDP(session, packet);
                    Debug.LogError($"[UDP-RNG]  Sent seed 0x{seed:X8} to {conn.LoginName}");
                }
                else
                {
                    Debug.LogError($"[UDP-RNG]  NO UDP SESSION for {conn?.LoginName ?? "NULL"}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UDP-RNG] error={ex.Message}\n{ex.StackTrace}");
            }
        }

        private void SendUDPEntitySynchInfoHP(UDPSession session, uint currentHPWire)
        {
            byte[] packet = new byte[8];
            packet[0] = 0x36;
            BitConverter.GetBytes(currentHPWire).CopyTo(packet, 1);
            SendEncryptedUDP(session, packet);
        }

        private UDPSession GetUDPSessionForConnection(RRConnection conn)
        {
            foreach (var session in _udpSessions.Values)
                if (session.Connection == conn) return session;

            var tcpEndpoint = conn?.Client?.Client?.RemoteEndPoint as IPEndPoint;
            if (tcpEndpoint != null)
                foreach (var session in _udpSessions.Values)
                    if (session.Endpoint.Address.Equals(tcpEndpoint.Address)) return session;

            return null;
        }





        private void OnUDPReceive(IAsyncResult ar)
        {
            try
            {
                IPEndPoint remoteEP = null;
                byte[] data = _udpListener.EndReceive(ar, ref remoteEP);
                if (data.Length == 0)
                {
                    _udpListener.BeginReceive(OnUDPReceive, null);
                    return;
                }
                string epKey = GetEndpointKey(remoteEP);
                byte flags = data[0];

                if (flags == 0x01)
                {
                    Debug.LogError("[UDP] Got SYN from client! Sending SYN-ACK...");
                    SendUDPSynAck(remoteEP, data);
                }
                else if (flags == 0x03)
                {
                    Debug.LogError("[UDP] Got client ACK response - handshake completing...");
                    SendUDPAck(remoteEP);
                }
                else if (flags == 0x06)
                {
                    Debug.LogError("[UDP] Got ACKKey (0x06) from client");
                    if (_udpSessions.TryGetValue(epKey, out var session))
                    {
                        session.IsEstablished = true;
                        Debug.LogError("[UDP] Session established via ACKKey");
                    }
                }
                else if (flags == 0x02)
                {
                    Debug.LogError("[UDP] Final ACK received! UDP ENCRYPTION ESTABLISHED!");
                    if (_udpSessions.TryGetValue(epKey, out var session))
                    {
                        session.IsEstablished = true;
                        _clientUDPEndpoint = remoteEP;
                        foreach (var connectionEntry in _connections)
                        {
                            var tcpEndpoint = connectionEntry.Value.Client.Client.RemoteEndPoint as IPEndPoint;
                            if (tcpEndpoint != null && tcpEndpoint.Address.Equals(remoteEP.Address))
                            {
                                session.Connection = connectionEntry.Value;
                                Debug.LogError($"[UDP] Linked to TCP connection {connectionEntry.Key} ({connectionEntry.Value.LoginName})");
                                break;
                            }
                        }

                        byte[] activate = new byte[8] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                        byte[] encrypted = EncryptUDP(session, activate);
                        _udpListener.Send(encrypted, encrypted.Length, remoteEP);
                        Debug.LogError($"[UDP] Sent activation ping");

                        byte[] entityOpen = new byte[8];
                        entityOpen[0] = 0x07;
                        entityOpen[1] = 0x06;
                        byte[] encEntity = EncryptUDP(session, entityOpen);
                        _udpListener.Send(encEntity, encEntity.Length, remoteEP);
                        Debug.LogError("[UDP] Sent entity channel opener 0x07/0x06");

                        Debug.LogError("[UDP] skipped raw player EntitySynchInfo HP baseline");
                    }
                }
                else
                {
                    if (VerbosePacketLogging) Debug.LogError($"[UDP] ENCRYPTED PACKET: {data.Length} bytes, first=0x{flags:X2}, data={BitConverter.ToString(data, 0, Math.Min(data.Length, 20))}");
                    if (VerbosePacketLogging) Debug.LogError($"[UDP] Looking for session key: {epKey}, Sessions count: {_udpSessions.Count}");

                    if (_udpSessions.TryGetValue(epKey, out var session))
                    {
                        if (VerbosePacketLogging) Debug.LogError($"[UDP] Found session, IsEstablished={session.IsEstablished}");
                        if (session.IsEstablished)
                        {
                            if (VerbosePacketLogging) Debug.LogError($"[UDP] Calling DecryptUDP...");
                            byte[] decrypted = DecryptUDP(session, data);
                            if (VerbosePacketLogging) Debug.LogError($"[UDP] Decrypt returned: {(decrypted != null ? decrypted.Length + " bytes" : "NULL")}");

                            if (decrypted != null && decrypted.Length > 0)
                            {
                                if (VerbosePacketLogging) Debug.LogError($"[UDP] DECRYPTED: {BitConverter.ToString(decrypted, 0, Math.Min(decrypted.Length, 30))}");
                                HandleDecryptedUDPPacket(session, decrypted);
                            }
                            else
                            {
                                if (VerbosePacketLogging) Debug.LogError($"[UDP] DECRYPTION FAILED or empty result");
                            }
                        }
                        else
                        {
                            if (VerbosePacketLogging) Debug.LogError($"[UDP] Session NOT established yet");
                        }
                    }
                    else
                    {
                        if (VerbosePacketLogging) Debug.LogError($"[UDP] NO SESSION FOUND for {epKey}");
                        if (VerbosePacketLogging) Debug.LogError($"[UDP] Available sessions: {string.Join(", ", _udpSessions.Keys)}");
                    }
                }
                _udpListener.BeginReceive(OnUDPReceive, null);
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                Debug.LogError($"[UDP] receive state=failed message='{ex.Message}'");
                try { _udpListener.BeginReceive(OnUDPReceive, null); } catch { }
            }
        }
        private static readonly byte[] NULL_PUBKEY = System.Text.Encoding.ASCII.GetBytes("PUBKEY12");

        private void HandleDuelRequest(RRConnection conn, uint targetCharSqlId)
        {
            RRConnection target = null;
            foreach (var selectedCharacterEntry in _selectedCharacter)
            {
                if (selectedCharacterEntry.Value.Id == (int)targetCharSqlId)
                {
                    target = FindConnectionByLogin(selectedCharacterEntry.Key);
                    break;
                }
            }

            if (target == null)
            {
                Debug.LogError($"[PVP-DUEL] Target CharSQLID {targetCharSqlId} not online");
                return;
            }

            IssueDuelChallenge(conn, target);
        }

        public string IssueDuelChallenge(RRConnection challengerConn, RRConnection targetConn)
        {
            if (challengerConn == null || targetConn == null) return "Connection not found.";

            var challengerChar = _selectedCharacter.TryGetValue(challengerConn.LoginName, out var cs)
                ? CharacterRepository.GetCharacter(cs.Id) : null;
            var targetChar = _selectedCharacter.TryGetValue(targetConn.LoginName, out var ts)
                ? CharacterRepository.GetCharacter(ts.Id) : null;
            if (challengerChar == null || targetChar == null)
            {
                Debug.LogError("[PVP-DUEL] Character lookup failed");
                return "Character lookup failed.";
            }

            uint challengerCharSqlId = (uint)challengerChar.id;
            uint targetCharSqlId     = (uint)targetChar.id;
            string err = _duelRuntime.TryChallenge(challengerConn.LoginName, challengerCharSqlId,
                targetConn.LoginName, targetCharSqlId,
                challengerChar.level, targetChar.level);
            if (err != null)
            {
                Debug.LogError($"[PVP-DUEL] Challenge rejected: {err}");
                return err;
            }

            byte[] targetPacket = PVPPackets.BuildDuelStatus(
                PVPPackets.DuelStatusType.Challenged, challengerCharSqlId, 0, 0);
            SendToClient(targetConn, targetPacket);
            Debug.LogError($"[PVP-DUEL] Sent Challenged to {targetConn.LoginName}");
            return null;
        }

        public bool AcceptDuel(RRConnection conn)
        {
            HandleDuelAccept(conn);
            return _duelRuntime.IsInDuel(conn.LoginName);
        }

        public bool DeclineDuel(RRConnection conn)
        {
            var hadDuel = _duelRuntime.IsInDuel(conn.LoginName);
            HandleDuelDecline(conn);
            return hadDuel;
        }

        public string GetDuelStatusFor(string loginName)
        {
            var d = _duelRuntime.GetDuel(loginName);
            if (d == null) return "No active duel.";
            string other = string.Equals(d.ChallengerLogin, loginName, StringComparison.OrdinalIgnoreCase)
                ? d.TargetLogin : d.ChallengerLogin;
            return $"{d.State} vs {other}";
        }

        private void HandleDuelAccept(RRConnection conn)
        {
            var duel = _duelRuntime.TryAccept(conn.LoginName);
            if (duel == null) { Debug.LogError($"[PVP-DUEL] No pending duel for {conn.LoginName}"); return; }

            var challenger = FindConnectionByLogin(duel.ChallengerLogin);
            var target = FindConnectionByLogin(duel.TargetLogin);

            if (challenger != null)
                SendToClient(challenger, PVPPackets.BuildDuelStatus(
                    PVPPackets.DuelStatusType.Accepted, duel.TargetCharSqlId, 0, 0));
            if (target != null)
                SendToClient(target, PVPPackets.BuildDuelStatus(
                    PVPPackets.DuelStatusType.Accepted, duel.ChallengerCharSqlId, 0, 0));

            _pendingDuelActivations.Add((
                DateTime.UtcNow.AddSeconds(Gameplay.DuelRuntime.DuelInfo.CountdownSec),
                duel));
            Debug.LogError($"[PVP-DUEL] Activation scheduled for {duel.ChallengerLogin} vs {duel.TargetLogin} in {Gameplay.DuelRuntime.DuelInfo.CountdownSec}s");
        }

        private void HandleDuelDecline(RRConnection conn)
        {
            var duel = _duelRuntime.TryDecline(conn.LoginName);
            if (duel == null) { Debug.LogError($"[PVP-DUEL] No pending duel for {conn.LoginName}"); return; }

            var challenger = FindConnectionByLogin(duel.ChallengerLogin);
            if (challenger != null)
                SendToClient(challenger, PVPPackets.BuildDuelStatus(
                    PVPPackets.DuelStatusType.Declined, duel.TargetCharSqlId, 0, 0));
        }

        private readonly List<(DateTime dueAt, Gameplay.DuelRuntime.DuelInfo duel)> _pendingDuelActivations
            = new List<(DateTime, Gameplay.DuelRuntime.DuelInfo)>();
        private void BroadcastPlayerMovement(RRConnection conn, byte sessionId, byte moveCount, byte[] rawMoveData)
        {
            uint avatarId = GetPlayerAvatarId(conn.LoginName);
            if (avatarId == 0) return;

            if (moveCount == 0 || rawMoveData == null || rawMoveData.Length == 0) return;

            conn.LastPositionUpdateTime = Time.time;
            conn.LastRelayPosX = conn.PlayerPosX;
            conn.LastRelayPosY = conn.PlayerPosY;
            conn.HasLastRelayPos = true;
            conn.LastRawMoveData = rawMoveData;
            conn.LastRawMoveCount = moveCount;

            byte relayMoveCount = moveCount;
            byte[] relayData = rawMoveData;
            if (!TryNormalizeUnitMoverUpdateData(relayMoveCount, relayData, out relayMoveCount, out relayData)) return;

            foreach (var other in _connections.Values)
            {
                if (other == conn) continue;
                if (!other.IsSpawned) continue;
                if (other.CurrentZoneGcType != conn.CurrentZoneGcType) continue;
                if (other.InstanceId != conn.InstanceId) continue;

                if (!_remoteBehaviorIds.TryGetValue(other.LoginName, out var playerMap)) continue;
                if (!playerMap.TryGetValue(conn.LoginName, out ushort remoteBehaviorId)) continue;

                var remoteMoveMessage = new LEWriter();
                remoteMoveMessage.WriteByte(0x35);
                remoteMoveMessage.WriteUInt16(remoteBehaviorId);
                remoteMoveMessage.WriteByte(0x65);
                remoteMoveMessage.WriteByte(0xFF);
                remoteMoveMessage.WriteByte(relayMoveCount);
                remoteMoveMessage.WriteBytes(relayData);
                if (!TryWriteRemoteAvatarEntitySynchInfo(conn, remoteMoveMessage, remoteBehaviorId, 0x65, "MP-MOVE"))
                    continue;
                other.MessageQueue.Enqueue(remoteMoveMessage.ToArray());
                DungeonRunners.Core.RuntimeEvidence.LogForPlayerPair(conn.LoginName, other.LoginName, "[MP-MOVE]",
                    $"remoteBehavior={remoteBehaviorId} moveCount={relayMoveCount} hp={GetPlayerState(conn.ConnId.ToString())?.EntitySynchInfoHP ?? 0} srcPos=({conn.PlayerPosX:F1},{conn.PlayerPosY:F1}) relayPos=({conn.LastRelayPosX:F1},{conn.LastRelayPosY:F1})");
            }
        }
        public void SendCompressedPublic(RRConnection conn, byte dest, byte messageType, byte[] data)
            => SendCompressedA(conn, dest, messageType, data);
    }
}
