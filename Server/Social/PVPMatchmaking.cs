using System;
using System.Collections.Generic;
using System.Linq;
using DungeonRunners.Engine;

namespace DungeonRunners.Gameplay
{
    public class PVPMatchmaking
    {
        public static readonly PVPMatchmaking Instance = new PVPMatchmaking();

        public enum Archetype
        {
            GroupDuelMatch,
            GroupPracticeMatch,
            GroupDeathMatch,
            GroupDeathMatchUnrated,
        }

        private static readonly Dictionary<string, Archetype> _archetypeByGcType =
            new Dictionary<string, Archetype>(StringComparer.OrdinalIgnoreCase)
            {
                { "pvp.GroupDuelMatch",         Archetype.GroupDuelMatch },
                { "pvp.GroupPracticeMatch",     Archetype.GroupPracticeMatch },
                { "pvp.GroupDeathMatch",        Archetype.GroupDeathMatch },
                { "pvp.GroupDeathMatchUnrated", Archetype.GroupDeathMatchUnrated },
            };

        private static readonly Dictionary<uint, Archetype> _archetypeByTypeId = BuildTypeIdMap();

        private static Dictionary<uint, Archetype> BuildTypeIdMap()
        {
            var map = new Dictionary<uint, Archetype>();
            foreach (var kv in _archetypeByGcType)
                map[HashGcType(kv.Key)] = kv.Value;
            return map;
        }

        public static uint HashGcType(string gcPath)
        {
            uint h = 5381;
            if (!string.IsNullOrEmpty(gcPath))
                foreach (char c in gcPath.ToLowerInvariant()) h = ((h << 5) + h) + (uint)c;
            return h;
        }

        public static bool TryParseArchetype(string gcType, out Archetype a)
            => _archetypeByGcType.TryGetValue(gcType ?? "", out a);

        public static bool TryParseArchetypeByTypeId(uint typeId, out Archetype a)
            => _archetypeByTypeId.TryGetValue(typeId, out a);

        public static uint GetTypeId(Archetype a)
        {
            foreach (var kv in _archetypeByGcType)
                if (kv.Value == a) return HashGcType(kv.Key);
            return 0;
        }

        public static string GetGcPath(Archetype a)
        {
            foreach (var kv in _archetypeByGcType)
                if (kv.Value == a) return kv.Key;
            return null;
        }

        public static string TeamGcPath(Archetype a, bool isRed)
        {
            if (a == Archetype.GroupPracticeMatch) return "pvp.FFATeam";
            string list = a == Archetype.GroupDuelMatch ? "pvp.DuelTeamList" : "pvp.DefaultTeamList";
            return list + (isRed ? ".RedTeam" : ".BlueTeam");
        }

        public struct MatchRules
        {
            public int MinGroupCount;
            public int MaxGroupCount;
            public int SetupTimeSec;
            public int CombatTimeSec;
            public int RewardTimeSec;
            public int StartingRatingTolerance;
            public double RatingToleranceIncrement;
            public string[] Zones;
            public bool FreeForAll;
        }

        public static MatchRules Rules(Archetype a)
        {
            switch (a)
            {
                case Archetype.GroupDeathMatch:
                    return new MatchRules {
                        MinGroupCount = 2, MaxGroupCount = 2,
                        SetupTimeSec = 15, CombatTimeSec = 300, RewardTimeSec = 120,
                        StartingRatingTolerance = 60, RatingToleranceIncrement = 1.0235,
                        Zones = new[] { "DeathMatch01", "DeathMatch02", "DeathMatch03", "DeathMatch04" },
                        FreeForAll = false };
                case Archetype.GroupDeathMatchUnrated:
                    return new MatchRules {
                        MinGroupCount = 2, MaxGroupCount = 2,
                        SetupTimeSec = 30, CombatTimeSec = 420, RewardTimeSec = 0,
                        StartingRatingTolerance = 60, RatingToleranceIncrement = 1.0235,
                        Zones = new[] { "DeathMatchUnrated01", "DeathMatchUnrated02", "DeathMatchUnrated03", "DeathMatchUnrated04" },
                        FreeForAll = false };
                case Archetype.GroupDuelMatch:
                    return new MatchRules {
                        MinGroupCount = 2, MaxGroupCount = 2,
                        SetupTimeSec = 30, CombatTimeSec = 420, RewardTimeSec = 0,
                        StartingRatingTolerance = 50, RatingToleranceIncrement = 1.0235,
                        Zones = new[] { "PVPGroupDuelMatch" },
                        FreeForAll = false };
                case Archetype.GroupPracticeMatch:
                    return new MatchRules {
                        MinGroupCount = 1, MaxGroupCount = 1,
                        SetupTimeSec = 0, CombatTimeSec = 0, RewardTimeSec = 0,
                        StartingRatingTolerance = 0, RatingToleranceIncrement = 1.0,
                        Zones = new[] { "PVPGroupPracticeZone" },
                        FreeForAll = true };
                default:
                    return new MatchRules {
                        MinGroupCount = 2, MaxGroupCount = 2,
                        SetupTimeSec = 30, CombatTimeSec = 420,
                        Zones = new[] { "PVPGroupDuelMatch" } };
            }
        }

        public static int MinGroupCount(Archetype a) => Rules(a).MinGroupCount;

        public static bool IsRanked(Archetype a)
        {
            switch (a)
            {
                case Archetype.GroupDuelMatch:
                case Archetype.GroupDeathMatch:
                    return true;
                default: return false;
            }
        }

        public static int EloDelta(int rating, List<int> opponentRatings, double score, int k = 32)
        {
            if (opponentRatings == null || opponentRatings.Count == 0) return 0;
            double expected = 0.0;
            foreach (int opp in opponentRatings)
                expected += 1.0 / (1.0 + Math.Pow(10.0, (opp - rating) / 400.0));
            expected /= opponentRatings.Count;
            return (int)Math.Round(k * (score - expected));
        }

        public string PickZoneForArchetype(Archetype a)
        {
            var zones = Rules(a).Zones;
            if (zones == null || zones.Length == 0) return null;
            if (zones.Length == 1) return zones[0];
            int n = System.Threading.Interlocked.Increment(ref _dmRoundRobin);
            return zones[((n % zones.Length) + zones.Length) % zones.Length];
        }
        private int _dmRoundRobin;

        public enum QueueState
        {
            None,
            Queued,
            Matched,
            InMatch,
        }

        public class QueueEntry
        {
            public string LoginName;
            public uint CharSqlId;
            public Archetype Archetype;
            public DateTime QueuedAt;
            public int PvpRating;
            public string AssignedMatchId;
            public string TeamKey;
        }

        public class Match
        {
            public string MatchId;
            public Archetype Archetype;
            public string ZoneName;
            public int InstanceId;
            public List<string> ParticipantLogins = new List<string>();
            public DateTime CreatedAt;
            public DateTime? StartedAt;
            public DateTime? EndedAt;
            public string WinnerLogin;
            public Dictionary<string, int> KillCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, int> DeathCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, int> ParticipantRatings = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            public List<string> RedTeamLogins = new List<string>();
            public List<string> BlueTeamLogins = new List<string>();
            public bool IsRed(string login) => RedTeamLogins.Contains(login, StringComparer.OrdinalIgnoreCase);

            public int CombatTimeSec;
            public int SetupTimeSec;

            public enum MatchPhase { Setup, Combat, Ended }
            public MatchPhase Phase = MatchPhase.Setup;
            public DateTime? CombatStartedAt;

            public bool EndedByForfeit;

            public bool SetupElapsed => Phase == MatchPhase.Setup && StartedAt.HasValue
                && (DateTime.UtcNow - StartedAt.Value).TotalSeconds >= Math.Max(0, SetupTimeSec);

            public bool IsExpired => Phase == MatchPhase.Combat && CombatStartedAt.HasValue && CombatTimeSec > 0
                && (DateTime.UtcNow - CombatStartedAt.Value).TotalSeconds > CombatTimeSec;

            public bool SpawnTimedOut => !StartedAt.HasValue
                && (DateTime.UtcNow - CreatedAt).TotalSeconds > 90;
        }

        private readonly Dictionary<string, QueueEntry> _queue =
            new Dictionary<string, QueueEntry>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Match> _activeMatches =
            new Dictionary<string, Match>();

        private readonly Dictionary<string, string> _playerToMatch =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly object _lock = new object();
        private int _nextInstanceId = 100000;


        public bool EnqueuePlayer(string loginName, uint charSqlId, Archetype archetype, int pvpRating, string teamKey)
        {
            lock (_lock)
            {
                if (_queue.ContainsKey(loginName))
                {
                    Debug.LogError($"[PVP-MATCH] {loginName} is already queued");
                    return false;
                }
                if (_playerToMatch.ContainsKey(loginName))
                {
                    Debug.LogError($"[PVP-MATCH] {loginName} is already in a match");
                    return false;
                }

                _queue[loginName] = new QueueEntry
                {
                    LoginName = loginName,
                    CharSqlId = charSqlId,
                    Archetype = archetype,
                    QueuedAt = DateTime.UtcNow,
                    PvpRating = pvpRating,
                    TeamKey = teamKey ?? ("solo:" + loginName),
                };
                Debug.LogError($"[PVP-MATCH] {loginName} queued for {archetype} (rating {pvpRating}). " +
                               $"Queue size for archetype: {_queue.Values.Count(e => e.Archetype == archetype)}");
                return true;
            }
        }

        public bool DequeuePlayer(string loginName)
        {
            lock (_lock)
            {
                if (!_queue.Remove(loginName))
                    return false;
                Debug.LogError($"[PVP-MATCH] {loginName} removed from queue");
                return true;
            }
        }

        public QueueState GetState(string loginName)
        {
            lock (_lock)
            {
                if (_playerToMatch.TryGetValue(loginName, out var matchId)
                    && _activeMatches.TryGetValue(matchId, out var match))
                {
                    return match.StartedAt.HasValue ? QueueState.InMatch : QueueState.Matched;
                }
                return _queue.ContainsKey(loginName) ? QueueState.Queued : QueueState.None;
            }
        }

        public Match GetMatchForPlayer(string loginName)
        {
            lock (_lock)
            {
                if (_playerToMatch.TryGetValue(loginName, out var matchId)
                    && _activeMatches.TryGetValue(matchId, out var match))
                    return match;
                return null;
            }
        }

        public List<Match> RunMatchmaking()
        {
            var newMatches = new List<Match>();
            lock (_lock)
            {
                foreach (Archetype a in Enum.GetValues(typeof(Archetype)))
                {
                    int minTeams = MinGroupCount(a);

                    while (true)
                    {
                        var teams = _queue.Values
                            .Where(e => e.Archetype == a && e.AssignedMatchId == null)
                            .GroupBy(e => e.TeamKey ?? ("solo:" + e.LoginName))
                            .Select(g => new Team(g.Key, g.ToList()))
                            .OrderBy(t => t.QueuedAt)
                            .ToList();

                        if (teams.Count < minTeams) break;

                        var anchor = teams[0];
                        var chosen = new List<Team> { anchor };
                        var rest = teams.Skip(1);
                        chosen.AddRange(IsRanked(a)
                            ? rest.OrderBy(t => Math.Abs(t.AvgRating - anchor.AvgRating)).Take(minTeams - 1)
                            : rest.Take(minTeams - 1));

                        if (chosen.Count < minTeams) break;

                        var participants = chosen.SelectMany(t => t.Members).ToList();
                        var match = CreateMatch(a, participants);
                        if (chosen.Count >= 1) match.RedTeamLogins.AddRange(chosen[0].Members.Select(m => m.LoginName));
                        if (chosen.Count >= 2) match.BlueTeamLogins.AddRange(chosen[1].Members.Select(m => m.LoginName));
                        newMatches.Add(match);

                        Debug.LogError($"[PVP-MATCH] formed {a}: " +
                            string.Join(" vs ", chosen.Select(t => string.Join("+", t.Members.Select(m => m.LoginName)))));

                        foreach (var queueEntry in participants)
                            _queue.Remove(queueEntry.LoginName);
                    }
                }
            }

            foreach (var m in newMatches)
                Debug.LogError($"[PVP-MATCH] Created match {m.MatchId} ({m.Archetype}) in zone {m.ZoneName}#{m.InstanceId}: " +
                               string.Join(", ", m.ParticipantLogins));

            return newMatches;
        }

        private sealed class Team
        {
            public readonly string Key;
            public readonly List<QueueEntry> Members;
            public readonly DateTime QueuedAt;
            public readonly int AvgRating;
            public Team(string key, List<QueueEntry> members)
            {
                Key = key;
                Members = members;
                QueuedAt = members.Min(m => m.QueuedAt);
                AvgRating = (int)members.Average(m => m.PvpRating);
            }
        }

        public void MarkMatchStarted(string matchId)
        {
            lock (_lock)
            {
                if (_activeMatches.TryGetValue(matchId, out var m) && !m.StartedAt.HasValue)
                {
                    m.Phase = Match.MatchPhase.Setup;
                    Debug.LogError($"[PVP-MATCH] Match {matchId} SPAWNED ({m.ParticipantLogins.Count} players) - awaiting both clients ready");
                }
            }
        }

        public void BeginSetupPhase(string matchId)
        {
            lock (_lock)
            {
                if (_activeMatches.TryGetValue(matchId, out var m) && !m.StartedAt.HasValue)
                {
                    m.StartedAt = DateTime.UtcNow;
                    Debug.LogError($"[PVP-MATCH] Match {matchId} SETUP countdown started ({m.SetupTimeSec}s) - both clients ready");
                }
            }
        }

        public bool RecordKill(string killerLogin, string victimLogin)
        {
            lock (_lock)
            {
                if (!_playerToMatch.TryGetValue(killerLogin, out var matchId))
                    return false;
                if (!_activeMatches.TryGetValue(matchId, out var match) || !match.StartedAt.HasValue)
                    return false;
                if (!match.ParticipantLogins.Contains(victimLogin, StringComparer.OrdinalIgnoreCase))
                    return false;

                if (!match.KillCounts.ContainsKey(killerLogin)) match.KillCounts[killerLogin] = 0;
                if (!match.DeathCounts.ContainsKey(victimLogin)) match.DeathCounts[victimLogin] = 0;
                match.KillCounts[killerLogin]++;
                match.DeathCounts[victimLogin]++;

                Debug.LogError($"[PVP-MATCH] {killerLogin} killed {victimLogin} in match {matchId} " +
                               $"(K/D: {killerLogin}={match.KillCounts[killerLogin]}, victim deaths={match.DeathCounts[victimLogin]})");

                bool isOneVsOne = match.ParticipantLogins.Count == 2;
                if (isOneVsOne)
                {
                    match.WinnerLogin = killerLogin;
                    return true;
                }
                return false;
            }
        }

        public Match EndMatch(string matchId, string reason)
        {
            lock (_lock)
            {
                if (!_activeMatches.TryGetValue(matchId, out var match))
                    return null;

                match.EndedAt = DateTime.UtcNow;

                if (match.WinnerLogin == null && match.KillCounts.Count > 0)
                {
                    match.WinnerLogin = match.KillCounts
                        .OrderByDescending(kv => kv.Value)
                        .ThenBy(kv => match.DeathCounts.TryGetValue(kv.Key, out var d) ? d : int.MaxValue)
                        .First().Key;
                }

                _activeMatches.Remove(matchId);
                foreach (var login in match.ParticipantLogins)
                    _playerToMatch.Remove(login);

                Debug.LogError($"[PVP-MATCH] Match {matchId} ENDED ({reason}). Winner: {match.WinnerLogin ?? "<none>"}");
                return match;
            }
        }

        public (List<Match> newMatches, List<Match> endedMatches, List<Match> enteringCombat) Tick()
        {
            var ended = new List<Match>();
            var enteringCombat = new List<Match>();
            lock (_lock)
            {
                foreach (var kv in _activeMatches.ToList())
                {
                    var m = kv.Value;
                    if (m.SpawnTimedOut)
                    {
                        m.Phase = Match.MatchPhase.Ended;
                        ended.Add(m);
                        _activeMatches.Remove(kv.Key);
                        foreach (var login in m.ParticipantLogins) _playerToMatch.Remove(login);
                        Debug.LogError($"[PVP-MATCH] Match {kv.Key} EXPIRED (spawn timeout)");
                    }
                    else if (m.SetupElapsed)
                    {
                        m.Phase = Match.MatchPhase.Combat;
                        m.CombatStartedAt = DateTime.UtcNow;
                        enteringCombat.Add(m);
                        Debug.LogError($"[PVP-MATCH] Match {kv.Key} SETUP->COMBAT (battle begins, combat {m.CombatTimeSec}s)");
                    }
                    else if (m.IsExpired)
                    {
                        m.Phase = Match.MatchPhase.Ended;
                        m.EndedAt = DateTime.UtcNow;
                        if (m.WinnerLogin == null && m.KillCounts.Count > 0)
                            m.WinnerLogin = m.KillCounts.OrderByDescending(x => x.Value).First().Key;
                        ended.Add(m);
                        _activeMatches.Remove(kv.Key);
                        foreach (var login in m.ParticipantLogins) _playerToMatch.Remove(login);
                        Debug.LogError($"[PVP-MATCH] Match {kv.Key} EXPIRED (combat time limit)");
                    }
                }
            }

            var newMatches = RunMatchmaking();
            return (newMatches, ended, enteringCombat);
        }

        public Match HandleDisconnect(string loginName)
        {
            lock (_lock)
            {
                _queue.Remove(loginName);

                if (_playerToMatch.TryGetValue(loginName, out var matchId)
                    && _activeMatches.TryGetValue(matchId, out var match))
                {
                    if (match.ParticipantLogins.Count == 2)
                    {
                        match.WinnerLogin = match.ParticipantLogins
                            .FirstOrDefault(p => !p.Equals(loginName, StringComparison.OrdinalIgnoreCase));
                        match.EndedByForfeit = true;
                        return EndMatch(matchId, $"{loginName} forfeited (disconnect/leave)");
                    }
                    else
                    {
                        match.ParticipantLogins.Remove(loginName);
                        _playerToMatch.Remove(loginName);
                        if (match.ParticipantLogins.Count <= 1)
                        {
                            match.WinnerLogin = match.ParticipantLogins.FirstOrDefault();
                            match.EndedByForfeit = true;
                            return EndMatch(matchId, "all but one player left");
                        }
                    }
                }
                return null;
            }
        }

        private Match CreateMatch(Archetype a, List<QueueEntry> participants)
        {
            var matchId = Guid.NewGuid().ToString("N").Substring(0, 12);
            var instanceId = System.Threading.Interlocked.Increment(ref _nextInstanceId);
            var rules = Rules(a);
            var match = new Match
            {
                MatchId = matchId,
                Archetype = a,
                ZoneName = PickZoneForArchetype(a),
                InstanceId = instanceId,
                CreatedAt = DateTime.UtcNow,
                ParticipantLogins = participants.Select(queueEntry => queueEntry.LoginName).ToList(),
                CombatTimeSec = rules.CombatTimeSec,
                SetupTimeSec = rules.SetupTimeSec,
            };
            _activeMatches[matchId] = match;
            foreach (var queueEntry in participants)
            {
                _playerToMatch[queueEntry.LoginName] = matchId;
                queueEntry.AssignedMatchId = matchId;
                match.ParticipantRatings[queueEntry.LoginName] = queueEntry.PvpRating;
            }
            return match;
        }

        public int TotalQueued => _queue.Count;
        public int TotalActiveMatches => _activeMatches.Count;
        public IEnumerable<Match> GetActiveMatches()
        {
            lock (_lock) return _activeMatches.Values.ToList();
        }
    }
}
