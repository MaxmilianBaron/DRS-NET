using System;
using System.Collections.Generic;
using DungeonRunners.Engine;

namespace DungeonRunners.Gameplay
{
    public class DuelRuntime
    {
        public enum DuelState
        {
            None,
            PendingAccept,
            Countdown,
            Active,
        }

        public class DuelInfo
        {
            public string ChallengerLogin;
            public string TargetLogin;
            public uint ChallengerCharSqlId;
            public uint TargetCharSqlId;
            public DuelState State;
            public DateTime CreatedAt;
            public DateTime? CombatStartAt;

            public const int AcceptTimeoutSec = 30;
            public const int CountdownSec = 5;

            public bool IsExpired => State == DuelState.PendingAccept &&
                (DateTime.UtcNow - CreatedAt).TotalSeconds > AcceptTimeoutSec;
        }

        private readonly Dictionary<string, DuelInfo> _activeDuels = new Dictionary<string, DuelInfo>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, DateTime> _cooldowns = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        public int CooldownSeconds { get; set; } = 60;

        public int MinLevel { get; set; } = 5;

        public bool IsInDuel(string loginName) => _activeDuels.ContainsKey(loginName);

        public DuelInfo GetDuel(string loginName)
        {
            _activeDuels.TryGetValue(loginName, out var info);
            return info;
        }

        public string TryChallenge(string challengerLogin, uint challengerCharSqlId,
            string targetLogin, uint targetCharSqlId, int challengerLevel, int targetLevel)
        {
            if (string.Equals(challengerLogin, targetLogin, StringComparison.OrdinalIgnoreCase))
                return "You cannot duel yourself.";

            if (challengerLevel < MinLevel)
                return $"You must be at least level {MinLevel} to duel.";

            if (targetLevel < MinLevel)
                return $"Your target must be at least level {MinLevel} to duel.";

            if (IsInDuel(challengerLogin))
                return "You are already in a duel.";

            if (IsInDuel(targetLogin))
                return "That player is already in a duel.";

            if (_cooldowns.TryGetValue(challengerLogin, out var lastEnd) &&
                (DateTime.UtcNow - lastEnd).TotalSeconds < CooldownSeconds)
            {
                int remaining = CooldownSeconds - (int)(DateTime.UtcNow - lastEnd).TotalSeconds;
                return $"Duel cooldown: {remaining}s remaining.";
            }

            var duel = new DuelInfo
            {
                ChallengerLogin = challengerLogin,
                TargetLogin = targetLogin,
                ChallengerCharSqlId = challengerCharSqlId,
                TargetCharSqlId = targetCharSqlId,
                State = DuelState.PendingAccept,
                CreatedAt = DateTime.UtcNow,
            };

            _activeDuels[challengerLogin] = duel;
            _activeDuels[targetLogin] = duel;

            Debug.LogError($"[PVP-DUEL] {challengerLogin} challenged {targetLogin} to a duel");
            return null;
        }

        public DuelInfo TryAccept(string targetLogin)
        {
            if (!_activeDuels.TryGetValue(targetLogin, out var duel))
                return null;

            if (duel.State != DuelState.PendingAccept)
                return null;

            if (!string.Equals(duel.TargetLogin, targetLogin, StringComparison.OrdinalIgnoreCase))
                return null;

            if (duel.IsExpired)
            {
                EndDuel(targetLogin, "Duel request expired.");
                return null;
            }

            duel.State = DuelState.Countdown;
            duel.CombatStartAt = DateTime.UtcNow.AddSeconds(DuelInfo.CountdownSec);
            Debug.LogError($"[PVP-DUEL] {targetLogin} accepted duel from {duel.ChallengerLogin} - countdown started");
            return duel;
        }

        public DuelInfo TryDecline(string targetLogin)
        {
            if (!_activeDuels.TryGetValue(targetLogin, out var duel))
                return null;

            if (duel.State != DuelState.PendingAccept)
                return null;

            Debug.LogError($"[PVP-DUEL] {targetLogin} declined duel from {duel.ChallengerLogin}");
            var info = duel;
            EndDuel(targetLogin, null);
            return info;
        }

        public bool ActivateCombat(string loginName)
        {
            if (!_activeDuels.TryGetValue(loginName, out var duel))
                return false;

            if (duel.State != DuelState.Countdown)
                return false;

            duel.State = DuelState.Active;
            Debug.LogError($"[PVP-DUEL] Combat active: {duel.ChallengerLogin} vs {duel.TargetLogin}");
            return true;
        }

        public bool AreInActiveDuel(string loginA, string loginB)
        {
            if (!_activeDuels.TryGetValue(loginA, out var duel))
                return false;

            if (duel.State != DuelState.Active)
                return false;

            return (string.Equals(duel.ChallengerLogin, loginB, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(duel.TargetLogin, loginB, StringComparison.OrdinalIgnoreCase));
        }

        public (string winner, string loser, DuelInfo duel) ReportKill(string killerLogin, string victimLogin)
        {
            if (!AreInActiveDuel(killerLogin, victimLogin))
                return (null, null, null);

            var duel = _activeDuels[killerLogin];
            Debug.LogError($"[PVP-DUEL] {killerLogin} defeated {victimLogin}!");
            EndDuel(killerLogin, null);
            return (killerLogin, victimLogin, duel);
        }

        public DuelInfo HandleDisconnect(string loginName)
        {
            if (!_activeDuels.TryGetValue(loginName, out var duel))
                return null;

            Debug.LogError($"[PVP-DUEL] {loginName} disconnected during duel - forfeited");
            var info = duel;
            EndDuel(loginName, null);
            return info;
        }

        public List<DuelInfo> CleanupExpired()
        {
            var expired = new List<DuelInfo>();
            var toRemove = new List<string>();

            foreach (var duelEntry in _activeDuels)
            {
                if (duelEntry.Value.IsExpired && !expired.Contains(duelEntry.Value))
                {
                    expired.Add(duelEntry.Value);
                    toRemove.Add(duelEntry.Value.ChallengerLogin);
                    toRemove.Add(duelEntry.Value.TargetLogin);
                }
            }

            foreach (var name in toRemove)
                _activeDuels.Remove(name);

            return expired;
        }

        private void EndDuel(string loginName, string reason)
        {
            if (!_activeDuels.TryGetValue(loginName, out var duel))
                return;

            _cooldowns[duel.ChallengerLogin] = DateTime.UtcNow;
            _cooldowns[duel.TargetLogin] = DateTime.UtcNow;

            _activeDuels.Remove(duel.ChallengerLogin);
            _activeDuels.Remove(duel.TargetLogin);

            if (reason != null)
                Debug.LogError($"[PVP-DUEL] Duel ended: {reason}");
        }
    }
}
