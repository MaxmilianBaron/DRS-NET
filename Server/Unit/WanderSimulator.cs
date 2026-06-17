using System;
using System.Collections.Generic;
using DungeonRunners.Core;
using DungeonRunners.Engine;

namespace DungeonRunners.Combat
{
    public class WanderSimulator
    {
        private static WanderSimulator _instance;
        public static WanderSimulator Instance => _instance ??= new WanderSimulator();
        private static readonly bool VerboseWanderLogs = IsTruthy(Environment.GetEnvironmentVariable("DR_SERVER_VERBOSE_WANDER_LOGS"));

        private List<WanderState> _entities = new List<WanderState>();
        private List<uint> _tickOrder = new List<uint>();

        public int EntityCount => _entities.Count;

        private static bool IsTruthy(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            switch (value.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    return true;
                default:
                    return false;
            }
        }

        private static void LogVerboseWander(string message)
        {
            if (VerboseWanderLogs)
                Debug.LogError(message);
        }

        private static void LogWanderClientPos(WanderState ws)
        {
            if (ws.Monster == null)
                return;
            if (float.IsNaN(ws.ClientX) || float.IsNaN(ws.ClientY))
            {
                Debug.LogError($"[MON-WANDER-POS] entity={ws.EntityId} client=(NaN,NaN) state={ws.State} target=({ws.TargetX:F1},{ws.TargetY:F1}) sourceFunction=UnitMover::Update(Fixed8)");
                return;
            }
            int fx = (int)Math.Round(ws.ClientX * UnitMover.Fixed);
            int fy = (int)Math.Round(ws.ClientY * UnitMover.Fixed);
            if (ws.HasLoggedClientPos && fx == ws.LastLoggedFixedX && fy == ws.LastLoggedFixedY)
                return;
            ws.HasLoggedClientPos = true;
            ws.LastLoggedFixedX = fx;
            ws.LastLoggedFixedY = fy;
            Debug.LogError($"[MON-WANDER-POS] entity={ws.EntityId} client=({ws.ClientX:F2},{ws.ClientY:F2}) fixed8=(0x{(uint)fx:X8},0x{(uint)fy:X8}) state={ws.State} target=({ws.TargetX:F1},{ws.TargetY:F1}) sourceFunction=UnitMover::Update(Fixed8)");
        }

        public void RegisterEntity(uint entityId, bool canWander = true)
        {
            UnregisterEntity(entityId);
            var state = new WanderState
            {
                EntityId = entityId,
                State = 0,
                Timer = 0,
                CanWander = canWander
            };
            _entities.Add(state);
            _tickOrder.Add(entityId);
            Debug.LogError($"[WANDER-SIM] Registered entity {entityId} (total: {_entities.Count})");
        }

        public void RegisterMonster(Monster monster, bool canWander = true)
        {
            if (monster == null) return;
            UnregisterEntity(monster.EntityId);
            var state = new WanderState
            {
                EntityId = monster.EntityId,
                Monster = monster,
                State = 3,
                Timer = 0,
                CanWander = canWander,
                DefaultX = monster.SpawnPosX,
                DefaultY = monster.SpawnPosY,
                ClientX = monster.PosX,
                ClientY = monster.PosY,
                TargetX = monster.PosX,
                TargetY = monster.PosY
            };
            _entities.Add(state);
            _tickOrder.Add(monster.EntityId);
            Debug.LogError($"[WANDER-SIM] Registered monster {monster.EntityId} walk={monster.WalkSpeed:F1} range={monster.WanderRange:F1} canWander={canWander} (total: {_entities.Count})");
            LogWanderClientPos(state);
        }

        public void UnregisterEntity(uint entityId)
        {
            _entities.RemoveAll(e => e.EntityId == entityId);
            _tickOrder.Remove(entityId);
        }

        public void TickAll(MersenneTwister roomRng)
        {
            for (int entityIndex = 0; entityIndex < _entities.Count; entityIndex++)
            {
                TickEntity(_entities[entityIndex], roomRng, ResolveUnitOwnedRng(_entities[entityIndex], roomRng));
            }
        }

        public void TickEntity(uint entityId, MersenneTwister roomRng, MersenneTwister unitOwnedRng = null)
        {
            for (int entityIndex = 0; entityIndex < _entities.Count; entityIndex++)
            {
                if (_entities[entityIndex].EntityId != entityId)
                    continue;
                TickEntity(_entities[entityIndex], roomRng, unitOwnedRng ?? ResolveUnitOwnedRng(_entities[entityIndex], roomRng));
                return;
            }
        }

        public bool TryGetClientVisiblePosition(uint entityId, out float posX, out float posY)
        {
            for (int entityIndex = 0; entityIndex < _entities.Count; entityIndex++)
            {
                var state = _entities[entityIndex];
                if (state.EntityId != entityId)
                    continue;

                posX = state.ClientX;
                posY = state.ClientY;
                return true;
            }

            posX = 0f;
            posY = 0f;
            return false;
        }

        private void TickEntity(WanderState ws, MersenneTwister roomRng, MersenneTwister unitOwnedRng)
        {
            if (ws.Monster != null && (!ws.Monster.IsAlive || ws.Monster.State != MonsterState.Idle))
                return;

            switch (ws.State)
            {
                case 0:
                    ws.State = 3;
                    break;

                case 1:
                    int rngBeforeTarget = roomRng?.CallsSinceReseed ?? -1;
                    uint rawX = RngLedger.Generate(roomRng, "room", "Wander::target-x", $"{ws.Monster?.Name ?? "monster"}#{ws.EntityId}");
                    uint rawY = RngLedger.Generate(roomRng, "room", "Wander::target-y", $"{ws.Monster?.Name ?? "monster"}#{ws.EntityId}");
                    LogVerboseWander($"[WANDER-RNG] entity={ws.EntityId} state=1 target rawX=0x{rawX:X8} rawY=0x{rawY:X8} rng={rngBeforeTarget}->{roomRng?.CallsSinceReseed ?? -1} stream=room");
                    if (ws.Monster != null && ws.Monster.WanderRange > 0f)
                    {
                        int range = Mathf.Max(1, Mathf.RoundToInt(ws.Monster.WanderRange));
                        uint span = (uint)Mathf.Max(1, range * 2);
                        float baseX = ws.CanWander ? ws.DefaultX : ws.ClientX;
                        float baseY = ws.CanWander ? ws.DefaultY : ws.ClientY;
                        ws.TargetX = baseX + (int)(rawX % span) - range;
                        ws.TargetY = baseY + (int)(rawY % span) - range;
                        ws.TargetAttempt++;
                        Debug.LogError($"[WANDER-TGT] entity={ws.EntityId} st={ws.State} cw={(ws.CanWander ? 1 : 0)} anchor=({baseX:F2},{baseY:F2}) cur=({ws.ClientX:F2},{ws.ClientY:F2}) rawX=0x{rawX:X8} rawY=0x{rawY:X8} target=({ws.TargetX:F2},{ws.TargetY:F2})");
                        bool pathValid = true;
                        if (ws.CanWander && !string.IsNullOrWhiteSpace(ws.Monster.ZoneName))
                        {
                            string pathMapKey = !string.IsNullOrWhiteSpace(ws.Monster.InstanceKey)
                                ? ws.Monster.InstanceKey
                                : ws.Monster.ZoneName;
                            var pathMap = PathMapCatalog.Instance.GetPathMap(pathMapKey);
                            if (pathMap != null)
                                pathValid = pathMap.CanReachPoint(ws.ClientX, ws.ClientY, ws.TargetX, ws.TargetY);
                            if (!pathValid)
                            {
                                LogVerboseWander($"[WANDER-AUDIT] entity={ws.EntityId} state=1 attempt={ws.TargetAttempt} canWander={ws.CanWander} anchor=({baseX:F1},{baseY:F1}) current=({ws.ClientX:F1},{ws.ClientY:F1}) rawX=0x{rawX:X8} rawY=0x{rawY:X8} target=({ws.TargetX:F1},{ws.TargetY:F1}) pathValid=False accepted=False rng={rngBeforeTarget}->{unitOwnedRng?.CallsSinceReseed ?? -1} stream=unitOwnedCombat");
                                return;
                            }
                        }
                        float dx = ws.TargetX - ws.ClientX;
                        float dy = ws.TargetY - ws.ClientY;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        float speed = ws.Monster.WalkSpeed > 0f ? ws.Monster.WalkSpeed : ws.Monster.MoveSpeed;
                        ws.MoveTicksRemaining = speed > 0f ? Mathf.Max(1, Mathf.CeilToInt(dist / speed * 30f)) : 1;
                        LogVerboseWander($"[WANDER-AUDIT] entity={ws.EntityId} state=1 attempt={ws.TargetAttempt} canWander={ws.CanWander} anchor=({baseX:F1},{baseY:F1}) current=({ws.ClientX:F1},{ws.ClientY:F1}) rawX=0x{rawX:X8} rawY=0x{rawY:X8} target=({ws.TargetX:F1},{ws.TargetY:F1}) pathValid={pathValid} accepted=True travelTicks={ws.MoveTicksRemaining} rng={rngBeforeTarget}->{unitOwnedRng?.CallsSinceReseed ?? -1} stream=unitOwnedCombat");
                        ws.HasTarget = true;
                    }
                    else
                    {
                        ws.HasTarget = false;
                        ws.MoveTicksRemaining = 1;
                    }
                    ws.State = 2;
                    ws.TargetAttempt = 0;
                    break;

                case 2:
                    if (ws.Monster != null && ws.HasTarget)
                    {
                        if (!ws.FixedInit)
                        {
                            ws.FixedX = (int)Math.Round(ws.ClientX * UnitMover.Fixed);
                            ws.FixedY = (int)Math.Round(ws.ClientY * UnitMover.Fixed);
                            ws.FixedInit = true;
                        }
                        int tgtX = (int)Math.Round(ws.TargetX * UnitMover.Fixed);
                        int tgtY = (int)Math.Round(ws.TargetY * UnitMover.Fixed);
                        float speed = ws.Monster.WalkSpeed > 0f ? ws.Monster.WalkSpeed : ws.Monster.MoveSpeed;
                        int stepFixed = (int)Math.Round(speed * UnitMover.Fixed / 30f);
                        if (stepFixed < 1) stepFixed = 1;
                        int turnRate = UnitMover.TurnRatePerTickFixed(ws.Monster.TurnRateDegrees);
                        if (!ws.HeadingInit)
                        {
                            ws.HeadingFixed = UnitMover.VectorToHeadingFixed(tgtX - ws.FixedX, tgtY - ws.FixedY);
                            ws.HeadingInit = true;
                        }
                        int nextX, nextY, nextHeading;
                        bool arrived;
                        UnitMover.StepTowardFixedHeading(ws.FixedX, ws.FixedY, ws.HeadingFixed, tgtX, tgtY, stepFixed, turnRate, out nextX, out nextY, out nextHeading, out arrived);
                        var wpm = PathMapCatalog.Instance.GetPathMap(!string.IsNullOrWhiteSpace(ws.Monster.InstanceKey) ? ws.Monster.InstanceKey : ws.Monster.ZoneName);
                        UnitMover.ResolveMovement(wpm, ws.FixedX, ws.FixedY, nextX, nextY, out nextX, out nextY);
                        ws.FixedX = nextX;
                        ws.FixedY = nextY;
                        ws.HeadingFixed = nextHeading;
                        ws.ClientX = nextX / (float)UnitMover.Fixed;
                        ws.ClientY = nextY / (float)UnitMover.Fixed;
                        if (arrived)
                        {
                            ws.ClientX = ws.TargetX;
                            ws.ClientY = ws.TargetY;
                            ws.FixedInit = false;
                            ws.HasTarget = false;
                            ws.State = 3;
                            LogVerboseWander($"[WANDER-MOVE] entity={ws.EntityId} arrived visual=({ws.ClientX:F1},{ws.ClientY:F1}) target=({ws.TargetX:F1},{ws.TargetY:F1}) sourceFunction=UnitMover::Update(Fixed8)");
                        }
                        LogWanderClientPos(ws);
                    }
                    else
                    {
                        ws.ArriveTicks++;
                        if (ws.ArriveTicks >= 1)
                        {
                            ws.State = 3;
                            ws.ArriveTicks = 0;
                        }
                    }
                    break;

                case 3:
                    {
                        int rngBeforeTimer = roomRng?.CallsSinceReseed ?? -1;
                        uint raw = RngLedger.Generate(roomRng, "room", "Wander::timer", $"{ws.Monster?.Name ?? "monster"}#{ws.EntityId}");
                        uint decision = raw % 150;
                        ushort timer = (ushort)(decision + 90);

                        if (ws.CanWander)
                        {
                            timer = (ushort)(timer * 3);
                        }

                        ws.Timer = timer;
                        ws.State = 4;
                        LogVerboseWander($"[WANDER-RNG] entity={ws.EntityId} state=3 timer raw=0x{raw:X8} roll={decision} timer={timer} canWander={ws.CanWander} rng={rngBeforeTimer}->{roomRng?.CallsSinceReseed ?? -1} stream=room");
                    }
                    break;

                case 4:
                    if (ws.Timer > 0)
                        ws.Timer--;
                    if (ws.Timer > 0)
                        return;

                    if (!ws.CanWander)
                    {
                        ws.State = 1;
                        return;
                    }

                    {
                        int rngBeforeMoveCheck = roomRng?.CallsSinceReseed ?? -1;
                        uint raw = RngLedger.Generate(roomRng, "room", "Wander::move-check", $"{ws.Monster?.Name ?? "monster"}#{ws.EntityId}");
                        uint roll = raw % 100;
                        LogVerboseWander($"[WANDER-RNG] entity={ws.EntityId} state=4 move-check raw=0x{raw:X8} roll={roll} rng={rngBeforeMoveCheck}->{roomRng?.CallsSinceReseed ?? -1} stream=room");

                        if (roll < 30)
                        {
                            ws.State = 1;
                        }
                        else
                        {
                            ws.Timer = 450;
                        }
                    }
                    break;

                default:
                    ws.State = 3;
                    break;
            }
        }

        private static MersenneTwister ResolveUnitOwnedRng(WanderState ws, MersenneTwister roomRng)
        {
            if (ws?.Monster?.Rng != null)
                return ws.Monster.Rng;
            if (ws?.Monster != null)
                Debug.LogError($"[RNG-LEDGER] stream=unitOwnedCombat phase=Wander::resolve-unit-rng owner='{ws.Monster.Name}#{ws.Monster.EntityId}' alias=room clientObject=EntityManager+0x44 reason=client-unit-manager-alias sourceFunction=Wander::update");
            return roomRng;
        }

        public void Clear()
        {
            _entities.Clear();
            _tickOrder.Clear();
        }

        public string DumpState()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[WANDER-SIM] {_entities.Count} entities:");
            int[] stateCounts = new int[5];
            foreach (var e in _entities)
            {
                if (e.State >= 0 && e.State <= 4) stateCounts[e.State]++;
            }
            sb.AppendLine($"  State0={stateCounts[0]} State1={stateCounts[1]} State2={stateCounts[2]} State3={stateCounts[3]} State4={stateCounts[4]}");
            return sb.ToString();
        }

        public string DescribeSchedule(int soonTicks = 30)
        {
            int state1Pending = 0;
            int movingState2 = 0;
            int state3Waiting = 0;
            int state4Due = 0;
            int state4Soon = 0;
            foreach (var e in _entities)
            {
                switch (e.State)
                {
                    case 1:
                        state1Pending++;
                        break;
                    case 2:
                        movingState2++;
                        break;
                    case 3:
                        state3Waiting++;
                        break;
                    case 4:
                        if (e.Timer == 0) state4Due++;
                        else if (e.Timer <= soonTicks) state4Soon++;
                        break;
                }
            }
            return $"state3Waiting={state3Waiting} state4Due={state4Due} state4Soon={state4Soon} state1Pending={state1Pending} movingState2={movingState2}";
        }

        private PathMap ResolveWanderPathMap(WanderState ws)
        {
            if (ws.PathMapResolved) return ws.CachedPathMap;
            ws.PathMapResolved = true;
            if (ws.Monster == null) return null;
            string key = !string.IsNullOrWhiteSpace(ws.Monster.InstanceKey) ? ws.Monster.InstanceKey : ws.Monster.ZoneName;
            if (string.IsNullOrWhiteSpace(key)) return null;
            ws.CachedPathMap = PathMapCatalog.Instance.GetPathMap(key);
            return ws.CachedPathMap;
        }
    }

    public class WanderState
    {
        public uint EntityId;
        public byte State;
        public ushort Timer;
        public bool CanWander;
        public int ArriveTicks;
        public Monster Monster;
        public float DefaultX;
        public float DefaultY;
        public float ClientX;
        public float ClientY;
        public float TargetX;
        public float TargetY;
        public bool HasTarget;
        public int TargetAttempt;
        public int MoveTicksRemaining;
        public int IdleSubTick;
        public int FixedX;
        public int FixedY;
        public bool FixedInit;
        public int HeadingFixed;
        public bool HeadingInit;
        public PathMap CachedPathMap;
        public bool PathMapResolved;
        public int LastLoggedFixedX = int.MinValue;
        public int LastLoggedFixedY = int.MinValue;
        public bool HasLoggedClientPos;
    }
}
