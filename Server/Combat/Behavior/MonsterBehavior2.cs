using System;

namespace DungeonRunners.Combat.Behavior
{
    public interface IMonsterBehaviorContext
    {
        uint RoomDraw(string site);
        bool HasTarget { get; }
        float DistanceToTarget { get; }
        bool TargetInMeleeReach { get; }
        bool TargetIsPlayer { get; }
        bool HasAttackDistanceRange { get; }
        int RepositionReachFixed(int headingFixed, int distFixed);
    }

    public sealed class MonsterBehavior2
    {
        private const int MsgUpdateSkill = 3;
        private const int MsgAttackAlt = 2;
        private const int MsgSkillRearm = 4;
        private const int MsgSkillCond = 5;
        private const int MsgThreatNear = 9;
        private const int MsgThreatAlt = 10;
        private const int MsgScanTick = 6;
        private const int MsgScanArm = 2;

        private const int StateInit = 0;
        private const int StateIdle = 5;
        private const int StateAttack = 6;
        private const int StateRecover = 0xb;
        private const int StateAcquire = 0xc;
        private const int StateWanderMove = 0x14;
        private const int StateFollow = 0x1d;

        private const int FollowSubChase = 2;
        private const int FollowSubNear = 5;
        private const int FollowSubReposition = 8;

        private const int ScanSubScan = 0x10;
        private const int ScanSubRetreat = 0x11;
        private const int ScanSubCommit = 0x12;

        private const int NearDistSquaredFixed = 0x38400;

        public readonly StateMachine Sm = new StateMachine();
        public int TopState;
        public int CombatTimer;
        public bool SwingDrawsEnabled;

        private int _followSub;
        private bool _followActive;
        private int _scanSub;
        private int _scanCountdown;
        private bool _maneuverActive;
        private int _scanMoveGapTicks;
        private int _attackUseIndex;
        private int _prevUseIndex;

        private const int ScanMoveGapScanTicks = 3;

        public void EnterCombat(IMonsterBehaviorContext context)
        {
            Sm.Reset();
            TopState = StateInit;
            CombatTimer = 0;
            _followActive = false;
            _maneuverActive = false;
            _scanSub = 0;
            _followSub = 0;
            _prevUseIndex = 0;
            _scanMoveGapTicks = 0;
            States(context, StateInit, EventEnter, 0);
        }

        public const int EventUpdate = 0;
        public const int EventEnter = 1;
        public const int EventExit = 2;
        public const int EventMessage = 3;

        public void Tick(IMonsterBehaviorContext context)
        {
            if (CombatTimer > 0) CombatTimer--;
            Sm.DeliverMessages((id, param) => OnMessage(context, id));
            States(context, TopState, EventUpdate, 0);
            UpdateActiveSubAction(context);
        }

        private void OnMessage(IMonsterBehaviorContext context, int id)
        {
            if (id == MsgScanTick)
            {
                OnScanTick(context);
                return;
            }
            if (id == 0xa)
                return;
            States(context, TopState, EventMessage, id);
        }

        private void SetState(IMonsterBehaviorContext context, int next)
        {
            if (TopState == next) return;
            States(context, TopState, EventExit, 0);
            TopState = next;
            States(context, next, EventEnter, 0);
        }

        private void States(IMonsterBehaviorContext context, int state, int ev, int msgId)
        {
            switch (state)
            {
                case StateInit:
                    if (ev == EventEnter)
                    {
                        Sm.SendMessageA(context.HasTarget ? 0xf : 0x2d, 0, 0);
                        TopState = StateIdle;
                        States(context, StateIdle, EventEnter, 0);
                    }
                    return;

                case StateIdle:
                    if (ev == EventMessage)
                    {
                        if (msgId == MsgThreatNear && context.HasTarget)
                            SetState(context, StateAttack);
                        else if (msgId == MsgThreatAlt && context.HasTarget)
                            SetState(context, StateAcquire);
                        else if (msgId == 5 || msgId == 8)
                            DoManeuverAction(context);
                    }
                    else if (ev == EventUpdate)
                    {
                        if (context.HasTarget)
                        {
                            if (!_followActive)
                                StartFollow(context);
                            if (!_maneuverActive)
                                DoManeuverAction(context);
                        }
                    }
                    return;

                case StateAttack:
                    if (ev == EventEnter)
                    {
                        CombatTimer = 300;
                        MeleeWeaponUse(context);
                    }
                    else if (ev == EventMessage)
                    {
                        if (msgId == MsgThreatNear)
                            SetState(context, context.TargetInMeleeReach ? StateRecover : StateIdle);
                        else if (msgId == 8)
                            SetState(context, StateRecover);
                    }
                    return;

                case StateRecover:
                    if (ev == EventMessage)
                    {
                        if (msgId == 8)
                            SetState(context, StateAttack);
                        else if (msgId == MsgThreatNear)
                            SetState(context, context.TargetInMeleeReach ? StateAttack : StateIdle);
                    }
                    return;

                case StateAcquire:
                    if (ev == EventEnter)
                        SetState(context, context.TargetInMeleeReach ? StateAttack : StateIdle);
                    return;

                case StateFollow:
                case StateWanderMove:
                    return;
            }
        }

        private void StartFollow(IMonsterBehaviorContext context)
        {
            _followActive = true;
            float dist = context.DistanceToTarget;
            int distFixed = (int)(dist * 256f);
            _followSub = (long)distFixed * distFixed < NearDistSquaredFixed ? FollowSubNear : FollowSubChase;
            uint raw = context.RoomDraw("Follow::start");
            Sm.SendMessageA(0xa, (int)(raw % 10u), 10);
        }

        private void DoManeuverAction(IMonsterBehaviorContext context)
        {
            _maneuverActive = true;
            _scanSub = ScanSubScan;
            _scanCountdown = 3;
            context.RoomDraw("SearchForAttack::ResetScanHeading");
            Sm.SendMessageA(MsgScanArm, 0, 15);
            Sm.SendMessageA(MsgScanTick, 0, 5);
        }

        private void UpdateActiveSubAction(IMonsterBehaviorContext context)
        {
            if (_followActive && _followSub == FollowSubReposition)
                PickRandomNearbyPoint(context);
        }

        private void OnScanTick(IMonsterBehaviorContext context)
        {
            if (!_maneuverActive || _scanSub != ScanSubScan) return;

            if (context.TargetInMeleeReach)
            {
                _scanSub = ScanSubCommit;
                Sm.SendMessageA(MsgThreatNear, 0, 0);
                return;
            }

            if (_scanMoveGapTicks > 0)
            {
                _scanMoveGapTicks--;
                if (_scanMoveGapTicks == 0)
                {
                    _scanCountdown = 3;
                    context.RoomDraw("SearchForAttack::ResetScanHeading");
                }
                Sm.SendMessageA(MsgScanTick, 0, 5);
                return;
            }

            if (context.HasAttackDistanceRange)
                context.RoomDraw("SearchForAttack::GetRandomAttackDistance");

            _scanCountdown--;

            if (_scanCountdown <= 0)
                _scanMoveGapTicks = ScanMoveGapScanTicks;

            Sm.SendMessageA(MsgScanTick, 0, 5);
        }

        private void PickRandomNearbyPoint(IMonsterBehaviorContext context)
        {
            uint h = context.RoomDraw("PickRandomNearbyPoint");
            int headingDeg = (int)(h % 0x168u);
            int iter = 0;
            do
            {
                uint d = context.RoomDraw("PickRandomNearbyPoint");
                int distFixed = (int)((d % 0x14u + 10u) * 0x100u);
                int reach = context.RepositionReachFixed(headingDeg << 8, distFixed);
                if (reach > 0xa00) break;
                if (headingDeg < 0xb4) headingDeg += 0xb4;
                else if (headingDeg > 0xb4) headingDeg -= 0xb4;
                iter++;
            } while (iter < 2);
            _followSub = FollowSubChase;
        }

        private void MeleeWeaponUse(IMonsterBehaviorContext context)
        {
            if (!SwingDrawsEnabled) return;
            uint useRaw = context.RoomDraw("MeleeWeapon::use");
            _attackUseIndex = (int)(((useRaw & 1u) + (uint)_prevUseIndex + 1u) % 3u);
            _prevUseIndex = _attackUseIndex;

            context.RoomDraw("Weapon::hitRoll");
            context.RoomDraw("Weapon::blockGate");
            context.RoomDraw("Weapon::damageRoll");
        }
    }
}
