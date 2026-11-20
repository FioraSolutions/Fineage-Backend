using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

/// <summary>
/// Duels (Mobius CT_0 <c>DuelManager</c> / <c>Duel</c> / <c>RequestDuelStart</c> / <c>RequestDuelAnswerStart</c>,
/// reimplemented): 1v1 and party duels, ask / answer, countdown, fight to 1 HP, surrender, timeout, cancel on
/// distance / PvP flag / peace zone / interference, HP–MP–CP restore and party-arena teleport.
/// </summary>
public sealed partial class ZoneWorld
{
    public const int DuelSysNoOpponent = 1926;
    public const int DuelSysChallenged = 1927;
    public const int DuelSysPartyChallenged = 1928;
    public const int DuelSysTheyAccepted = 1929;
    public const int DuelSysYouAccepted = 1930;
    public const int DuelSysDeclined = 1931;
    public const int DuelSysYouAcceptedParty = 1933;
    public const int DuelSysTheyAcceptedParty = 1934;
    public const int DuelSysPartyDeclined = 1936;
    public const int DuelSysTargetNoParty = 1937;
    public const int DuelSysChallengesYou = 1938;
    public const int DuelSysPartyChallengesYou = 1939;
    public const int DuelSysCannotRequest = 1940;
    public const int DuelSysOpposingPartyCannot = 1942;
    public const int DuelSysTransport = 1944;
    public const int DuelSysBeginIn = 1945;
    public const int DuelSysBegin = 1949;
    public const int DuelSysWon = 1950;
    public const int DuelSysPartyWon = 1951;
    public const int DuelSysTie = 1952;
    public const int DuelSysWithdrew = 1955;
    public const int DuelSysPartyWithdrew = 1956;
    public const int DuelSysTooFar = 2028;
    public const int DuelSysBusy = 153;

    public const int DuelRequestRange = 250;
    public const int DuelMaxDistance = 1600;
    public const long DuelAskMs = 15_000;
    public const long DuelSoloMs = 120_000;
    public const long DuelPartyMs = 300_000;
    public const long DuelStartDelayMs = 3_000;
    public const long DuelPartyTeleportWaitMs = 20_000;
    public const long DuelEndDelayMs = 5_000;
    public const int DuelInstanceBase = 2_000_000;
    // Mobius Duel.teleportPlayers: the party duel arena.
    public const int DuelArenaX = -83760;
    public const int DuelArenaY = -238825;
    public const int DuelArenaZ = -3331;

    enum DuelPhase { Countdown, Fighting, Ending }

    enum DuelResult { None, Win, Surrender, Timeout, Canceled }

    sealed class ZoneDuel
    {
        public int Id;
        public bool Party;
        public int LeaderA, LeaderB;
        public readonly List<int> TeamA = [], TeamB = [];
        public DuelPhase Phase = DuelPhase.Countdown;
        public int Countdown = 4;
        public long NextStepMs;
        public long EndAtMs;
        public byte SurrenderTeam;
        public byte WinnerTeam;
        public DuelResult Result;
        public readonly Dictionary<int, (int X, int Y, int Z, int Inst, int Hp, int Mp)> Saved = [];
        public IEnumerable<int> All => TeamA.Concat(TeamB);
        public List<int> TeamOf(byte team) => team == 1 ? TeamA : TeamB;
    }

    readonly Dictionary<int, ZoneDuel> _duels = [];
    int _nextDuelId;

    public Task<string?> TryDuelCommandAsync(ZoneSession session, DuelCommandNetMsg msg, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");
        string? error;
        lock (_gate)
            error = TryDuelCommand(session, msg);
        return Task.FromResult(error);
    }

    string? TryDuelCommand(ZoneSession session, DuelCommandNetMsg msg)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Session == null)
            return "world";
        long now = NowUnixMs();
        switch (msg.Kind)
        {
            case DuelCommandKind.Request:
                DuelRequest(player, msg.TargetName ?? "", msg.Party, now);
                return null;
            case DuelCommandKind.Answer:
                DuelAnswer(player, msg.Response, now);
                return null;
            case DuelCommandKind.Surrender:
                DuelSurrender(player);
                return null;
            default:
                return "duel";
        }
    }

    // ---------------------------------------------------------------- request / answer

    void DuelRequest(ZoneEntity player, string targetName, bool party, long now)
    {
        var session = player.Session!;
        ZoneEntity? target = targetName.Length > 0
            ? FindOnlinePlayerByName(targetName)
            : _byId.TryGetValue(session.SelectedTargetId, out var sel) ? CreditOf(sel) : null;
        if (target == null || target.Kind != ZoneEntityKind.Player || ReferenceEquals(target, player))
        {
            SelfLine(session, DuelSysNoOpponent, "There is no opponent to receive your challenge for a duel.");
            return;
        }
        if (DuelRefusal(player, now) != null)
        {
            SelfLine(session, DuelSysCannotRequest, "You are unable to request a duel at this time.");
            return;
        }
        if (DuelRefusal(target, now) is { } why)
        {
            SelfLine(session, why.Id, why.Text, target.Name);
            return;
        }
        if (target.InstanceId != player.InstanceId ||
            !ZoneCombat.Within(player.X, player.Y, target.X, target.Y, DuelRequestRange))
        {
            SelfLine(session, DuelSysTooFar, $"{target.Name} is too far away to receive a duel challenge.", target.Name);
            return;
        }

        if (party)
        {
            if (!_partyByPlayer.TryGetValue(player.ObjectId, out int pa) || !_parties.TryGetValue(pa, out var myParty) ||
                myParty.LeaderObjectId != player.ObjectId)
            {
                SelfLine(session, DuelSysCannotRequest, "You are unable to request a duel at this time.");
                return;
            }
            if (!_partyByPlayer.TryGetValue(target.ObjectId, out int pb) || !_parties.TryGetValue(pb, out var theirParty))
            {
                SelfLine(session, DuelSysTargetNoParty,
                    "Since the person you challenged is not currently in a party, they cannot duel against your party.");
                return;
            }
            if (pa == pb)
            {
                SelfLine(session, DuelSysCannotRequest, "You are unable to request a duel at this time.");
                return;
            }
            foreach (var m in PartyMembersOf(player))
            {
                if (DuelRefusal(m, now) != null)
                {
                    SelfLine(session, DuelSysCannotRequest, "You are unable to request a duel at this time.");
                    return;
                }
            }
            foreach (var m in PartyMembersOf(target))
            {
                if (DuelRefusal(m, now) != null)
                {
                    SelfLine(session, DuelSysOpposingPartyCannot,
                        "The opposing party is currently unable to accept a challenge to a duel.");
                    return;
                }
            }
            // Mobius: the challenge goes to the other party's leader.
            if (_byId.TryGetValue(theirParty.LeaderObjectId, out var leader))
                target = leader;
        }

        if (target.DuelRefuse || (target.DuelAskFrom != 0 && target.DuelAskUntilMs > now) ||
            target.PrivateStoreType != 0 || _tradeByPlayer.ContainsKey(target.ObjectId) ||
            _pendingTradeAsk.ContainsKey(target.ObjectId))
        {
            SelfLine(session, DuelSysBusy, $"{target.Name} is busy. Please try again later.", target.Name);
            return;
        }

        target.DuelAskFrom = player.ObjectId;
        target.DuelAskParty = party;
        target.DuelAskUntilMs = now + DuelAskMs;
        NetBridge?.FanoutDuelEvent([target.ObjectId], new DuelEventNetMsg { Kind = DuelEventKind.Ask, Party = party, Name = player.Name });
        if (party)
        {
            SelfLine(session, DuelSysPartyChallenged, $"{target.Name}'s party has been challenged to a duel.", target.Name);
            SelfLine(target.Session!, DuelSysPartyChallengesYou, $"{player.Name}'s party has challenged your party to a duel.", player.Name);
        }
        else
        {
            SelfLine(session, DuelSysChallenged, $"{target.Name} has been challenged to a duel.", target.Name);
            SelfLine(target.Session!, DuelSysChallengesYou, $"{player.Name} has challenged you to a duel.", player.Name);
        }
        ZoneLog.Info($"duel ask from={player.ObjectId} to={target.ObjectId} party={party}");
    }

    void DuelAnswer(ZoneEntity player, int response, long now)
    {
        int fromId = player.DuelAskFrom;
        bool party = player.DuelAskParty;
        bool live = fromId != 0 && player.DuelAskUntilMs > now;
        player.DuelAskFrom = 0;
        player.DuelAskUntilMs = 0;
        if (response == -1)
            player.DuelRefuse = true;
        if (!live || !_byId.TryGetValue(fromId, out var requester) || requester.Session == null)
            return;
        var session = player.Session!;
        if (response != 1)
        {
            if (party)
                SelfLine(requester.Session, DuelSysPartyDeclined, "The opposing party has declined your challenge to a duel.");
            else
                SelfLine(requester.Session, DuelSysDeclined, $"{player.Name} has declined your challenge to a duel.", player.Name);
            return;
        }

        // Re-check everybody: the world moved on during the ask (Mobius DuelManager.addDuel).
        var teamA = party ? PartyMembersOf(requester) : [requester];
        var teamB = party ? PartyMembersOf(player) : [player];
        if (party && (!_partyByPlayer.TryGetValue(requester.ObjectId, out int pa) || !_partyByPlayer.TryGetValue(player.ObjectId, out int pb) || pa == pb))
            return;
        foreach (var m in teamA.Concat(teamB))
        {
            if (DuelRefusal(m, now) != null)
            {
                SelfLine(session, DuelSysCannotRequest, "You are unable to request a duel at this time.");
                SelfLine(requester.Session, DuelSysCannotRequest, "You are unable to request a duel at this time.");
                return;
            }
        }

        if (party)
        {
            SelfLine(session, DuelSysYouAcceptedParty,
                $"You have accepted {requester.Name}'s challenge to a party duel. The duel will begin in a few moments.", requester.Name);
            SelfLine(requester.Session, DuelSysTheyAcceptedParty,
                $"{player.Name} has accepted your challenge to duel against their party. The duel will begin in a few moments.", player.Name);
        }
        else
        {
            SelfLine(session, DuelSysYouAccepted,
                $"You have accepted {requester.Name}'s challenge to a duel. The duel will begin in a few moments.", requester.Name);
            SelfLine(requester.Session, DuelSysTheyAccepted,
                $"{player.Name} has accepted your challenge to a duel. The duel will begin in a few moments.", player.Name);
        }

        var duel = new ZoneDuel
        {
            Id = ++_nextDuelId,
            Party = party,
            LeaderA = requester.ObjectId,
            LeaderB = player.ObjectId,
            NextStepMs = now + DuelStartDelayMs
        };
        foreach (var m in teamA)
        {
            duel.TeamA.Add(m.ObjectId);
            m.DuelId = duel.Id;
        }
        foreach (var m in teamB)
        {
            duel.TeamB.Add(m.ObjectId);
            m.DuelId = duel.Id;
        }
        _duels[duel.Id] = duel;
        ZoneLog.Info($"duel start id={duel.Id} a={requester.ObjectId} b={player.ObjectId} party={party}");
    }

    /// <summary>Mobius <c>Player.canDuel</c>: the reason this player cannot duel now, or null.</summary>
    (int Id, string Text)? DuelRefusal(ZoneEntity p, long now)
    {
        string n = p.Name;
        if (p.Dead || p.CurHp <= 0)
            return (DuelSysCannotRequest, "You are unable to request a duel at this time.");
        if (p.CombatUntilUnixMs > now)
            return (2021, $"{n} cannot duel because {n} is currently engaged in battle.");
        if (p.CurHp < p.MaxHp / 2 || p.CurMp < p.MaxMp / 2)
            return (2019, $"{n} cannot duel because {n}'s HP or MP is below 50%.");
        if (p.DuelId != 0)
            return (2022, $"{n} cannot duel because {n} is already engaged in a duel.");
        if (_olympiadArena.Contains(p.ObjectId) || _olympiad.Board.MatchState(p.ObjectId) != null)
            return (2024, $"{n} cannot duel because {n} is participating in the Olympiad.");
        if (p.Karma > 0 || HasCursedWeapon(p.Inventory))
            return (2023, $"{n} cannot duel because {n} is in a chaotic state.");
        if (p.PrivateStoreType != 0)
            return (2017, $"{n} cannot duel because {n} is currently engaged in a private store or manufacture.");
        if (p.Fishing)
            return (2018, $"{n} cannot duel because {n} is currently fishing.");
        if (p.InstanceId != 0 || InPvpArea(p) || Peace.Contains(p.X, p.Y, p.Z))
            return (2020, $"{n} cannot make a challenge to a duel because {n} is currently in a duel-prohibited area (Peaceful Zone / Seven Signs Zone / Near Water / Restart Prohibited Area).");
        return null;
    }

    void DuelSurrender(ZoneEntity player)
    {
        if (player.DuelId == 0 || !_duels.TryGetValue(player.DuelId, out var duel) || duel.Phase != DuelPhase.Fighting)
            return;
        if (duel.SurrenderTeam != 0)
            return;
        // Mobius Duel.doSurrender: in a party duel the whole team gives up (any member may call it).
        duel.SurrenderTeam = duel.TeamA.Contains(player.ObjectId) ? (byte)1 : (byte)2;
        foreach (int id in duel.TeamOf(duel.SurrenderTeam))
            if (_byId.TryGetValue(id, out var m))
                m.DuelState = DuelFighterState.Dead;
        foreach (int id in duel.TeamOf(duel.SurrenderTeam == 1 ? (byte)2 : (byte)1))
            if (_byId.TryGetValue(id, out var m))
                m.DuelState = DuelFighterState.Winner;
    }

    // ---------------------------------------------------------------- tick

    void TickDuels(long now)
    {
        if (_duels.Count == 0)
            return;
        foreach (var duel in _duels.Values.ToList())
        {
            switch (duel.Phase)
            {
                case DuelPhase.Countdown:
                    if (now >= duel.NextStepMs)
                        DuelCountdownStep(duel, now);
                    break;
                case DuelPhase.Fighting:
                    DuelCheck(duel, now);
                    break;
                case DuelPhase.Ending:
                    if (now >= duel.NextStepMs)
                        DuelFinish(duel);
                    break;
            }
        }
    }

    void DuelCountdownStep(ZoneDuel duel, long now)
    {
        if (duel.All.Any(id => !_byId.ContainsKey(id)))
        {
            duel.Result = DuelResult.Canceled;
            DuelFinish(duel);
            return;
        }
        if (duel.Countdown == 4)
        {
            // Mobius Duel.savePlayerConditions + teleportPlayers (party duel only, then a 20 s wait).
            foreach (int id in duel.All)
            {
                var m = _byId[id];
                duel.Saved[id] = (m.X, m.Y, m.Z, m.InstanceId, m.CurHp, m.CurMp);
            }
            duel.Countdown = 3;
            if (duel.Party)
            {
                DuelLine(duel.All, DuelSysTransport, "In a moment, you will be transported to the site where the duel will take place.");
                DuelTeleportIn(duel);
                duel.NextStepMs = now + DuelPartyTeleportWaitMs;
            }
            else
            {
                duel.NextStepMs = now + 1000;
            }
            return;
        }
        if (duel.Countdown > 0)
        {
            DuelLine(duel.All, DuelSysBeginIn, $"The duel will begin in {duel.Countdown} second(s).", duel.Countdown.ToString());
            duel.Countdown--;
            duel.NextStepMs = now + 1000;
            return;
        }
        DuelBegin(duel, now);
    }

    void DuelTeleportIn(ZoneDuel duel)
    {
        int inst = DuelInstanceBase + duel.Id;
        int i = 0;
        foreach (int id in duel.TeamA)
            if (_byId.TryGetValue(id, out var m))
                Relocate(m, DuelArenaX + i++ * 40, DuelArenaY - 150, DuelArenaZ, inst, persist: false);
        i = 0;
        foreach (int id in duel.TeamB)
            if (_byId.TryGetValue(id, out var m))
                Relocate(m, DuelArenaX + i++ * 40, DuelArenaY + 150, DuelArenaZ, inst, persist: false);
    }

    void DuelBegin(ZoneDuel duel, long now)
    {
        duel.Phase = DuelPhase.Fighting;
        duel.EndAtMs = now + (duel.Party ? DuelPartyMs : DuelSoloMs);
        DuelLine(duel.All, DuelSysBegin, "Let the duel begin!");
        foreach (var (team, ids) in new[] { ((byte)1, duel.TeamA), ((byte)2, duel.TeamB) })
        {
            foreach (int id in ids)
            {
                if (!_byId.TryGetValue(id, out var m))
                    continue;
                m.DuelState = DuelFighterState.Duelling;
                m.DuelTeam = team;
                NetBridge?.FanoutDuelEvent([id], new DuelEventNetMsg { Kind = DuelEventKind.Ready, Party = duel.Party, Team = team });
                NetBridge?.FanoutDuelEvent([id], new DuelEventNetMsg { Kind = DuelEventKind.Start, Party = duel.Party, Team = team });
                BroadcastCubics(m); // team circle rides on UserInfo / CharInfo
            }
        }
        foreach (int id in duel.All)
            if (_byId.TryGetValue(id, out var m))
                SendDuelUserInfo(m);
    }

    void DuelCheck(ZoneDuel duel, long now)
    {
        var result = DuelResult.None;
        if (duel.All.Any(id => !_byId.ContainsKey(id)))
            result = DuelResult.Canceled;
        else if (duel.SurrenderTeam != 0)
        {
            result = DuelResult.Surrender;
            duel.WinnerTeam = duel.SurrenderTeam == 1 ? (byte)2 : (byte)1;
        }
        else if (now >= duel.EndAtMs)
            result = DuelResult.Timeout;
        else if (duel.TeamA.All(id => _byId[id].DuelState == DuelFighterState.Dead))
        {
            result = DuelResult.Win;
            duel.WinnerTeam = 2;
        }
        else if (duel.TeamB.All(id => _byId[id].DuelState == DuelFighterState.Dead))
        {
            result = DuelResult.Win;
            duel.WinnerTeam = 1;
        }
        else if (!duel.Party)
        {
            var a = _byId[duel.LeaderA];
            var b = _byId[duel.LeaderB];
            if (a.DuelState == DuelFighterState.Interrupted || b.DuelState == DuelFighterState.Interrupted ||
                a.InstanceId != b.InstanceId ||
                !ZoneCombat.Within(a.X, a.Y, b.X, b.Y, DuelMaxDistance) ||
                a.PvpFlagged || b.PvpFlagged ||
                InPvpArea(a) || InPvpArea(b) ||
                Peace.Contains(a.X, a.Y, a.Z) || Peace.Contains(b.X, b.Y, b.Z))
                result = DuelResult.Canceled;
        }
        if (result == DuelResult.None)
            return;
        duel.Result = result;
        if (result is DuelResult.Win or DuelResult.Surrender)
        {
            // Mobius: stop the fight, the losers kneel, the duel closes 5 s later.
            foreach (int id in duel.All)
                if (_byId.TryGetValue(id, out var m))
                    DuelStopFighting(m);
            foreach (int id in duel.TeamOf(duel.WinnerTeam == 1 ? (byte)2 : (byte)1))
                if (_byId.TryGetValue(id, out var m))
                    NetBridge?.FanoutSocialAction(KnownNetPlayersOf(m), m.ObjectId, 7);
            foreach (int id in duel.TeamOf(duel.WinnerTeam))
                if (_byId.TryGetValue(id, out var m) && m.DuelState != DuelFighterState.Dead)
                    m.DuelState = DuelFighterState.Winner;
            duel.Phase = DuelPhase.Ending;
            duel.NextStepMs = now + DuelEndDelayMs;
            return;
        }
        DuelFinish(duel);
    }

    void DuelStopFighting(ZoneEntity m)
    {
        AbortCast(m, "duel", notify: false);
        if (m.Session != null)
        {
            m.Intent.AutoAttackTargetId = 0;
            m.Intent.ClearApproach();
        }
    }

    void DuelFinish(ZoneDuel duel)
    {
        _duels.Remove(duel.Id);
        bool restore = duel.Result is DuelResult.Win or DuelResult.Surrender or DuelResult.Timeout;
        string winner = "";
        if (duel.WinnerTeam != 0 && _byId.TryGetValue(duel.WinnerTeam == 1 ? duel.LeaderA : duel.LeaderB, out var w))
            winner = w.Name;
        string loser = "";
        if (duel.WinnerTeam != 0 && _byId.TryGetValue(duel.WinnerTeam == 1 ? duel.LeaderB : duel.LeaderA, out var l))
            loser = l.Name;

        foreach (int id in duel.All)
        {
            if (!_byId.TryGetValue(id, out var m))
                continue;
            if (duel.Result is DuelResult.Canceled or DuelResult.Timeout)
                DuelStopFighting(m);
            DuelRelease(m, duel, restore);
        }

        var ids = duel.All.Where(_byId.ContainsKey).ToList();
        switch (duel.Result)
        {
            case DuelResult.Win:
                if (duel.Party)
                    DuelLine(ids, DuelSysPartyWon, $"{winner}'s party has won the duel.", winner);
                else
                    DuelLine(ids, DuelSysWon, $"{winner} has won the duel.", winner);
                break;
            case DuelResult.Surrender:
                if (duel.Party)
                    DuelLine(ids, DuelSysPartyWithdrew, $"Since {loser}'s party withdrew from the duel, {winner}'s party has won.", loser, winner);
                else
                    DuelLine(ids, DuelSysWithdrew, $"Since {loser} withdrew from the duel, {winner} has won.", loser, winner);
                break;
            default:
                DuelLine(ids, DuelSysTie, "The duel has ended in a tie.");
                break;
        }
        foreach (int id in ids)
            NetBridge?.FanoutDuelEvent([id], new DuelEventNetMsg { Kind = DuelEventKind.End, Party = duel.Party, Name = winner });
        ZoneLog.Info($"duel end id={duel.Id} result={duel.Result} winner={winner}");
    }

    /// <summary>Takes one fighter out of the duel: restore bars (normal end), back from the party arena, clear the team circle.</summary>
    void DuelRelease(ZoneEntity m, ZoneDuel duel, bool restore)
    {
        bool hadTeam = m.DuelTeam != 0;
        m.DuelId = 0;
        m.DuelTeam = 0;
        m.DuelState = DuelFighterState.None;
        if (duel.Saved.TryGetValue(m.ObjectId, out var saved))
        {
            if (restore && !m.Dead)
            {
                m.CurHp = Math.Max(m.CurHp, Math.Min(saved.Hp, m.MaxHp));
                m.CurMp = Math.Max(m.CurMp, Math.Min(saved.Mp, m.MaxMp));
                FanoutVitals(m, PlayerVitalsStatus(m));
                if (m.Session != null)
                    SaveVitals(m.Session, m);
            }
            if (duel.Party && m.InstanceId == DuelInstanceBase + duel.Id)
                Relocate(m, saved.X, saved.Y, saved.Z, saved.Inst);
        }
        if (hadTeam)
            BroadcastCubics(m);
    }

    void DuelLine(IEnumerable<int> ids, int id, string text, params string[] args)
    {
        var list = ids.Where(_byId.ContainsKey).ToList();
        if (list.Count > 0)
            ClanLine(list, id, text, args);
    }

    /// <summary>Mobius ExDuelUpdateUserInfo: a fighter's bars to the other team.</summary>
    void SendDuelUserInfo(ZoneEntity m)
    {
        if (NetBridge == null || m.DuelId == 0 || !_duels.TryGetValue(m.DuelId, out var duel) || duel.Phase == DuelPhase.Countdown)
            return;
        var foes = (duel.TeamA.Contains(m.ObjectId) ? duel.TeamB : duel.TeamA).Where(_byId.ContainsKey).ToList();
        if (foes.Count == 0)
            return;
        int maxCp = PartyMaxCp(m);
        NetBridge.FanoutDuelUserInfo(foes, new DuelUserInfoNetMsg
        {
            Name = m.Name,
            ObjectId = m.ObjectId,
            ClassId = m.ClassId,
            Level = m.Level,
            Hp = m.CurHp,
            MaxHp = m.MaxHp,
            Mp = m.CurMp,
            MaxMp = m.MaxMp,
            Cp = maxCp,
            MaxCp = maxCp
        });
    }

    // ---------------------------------------------------------------- rules hooks

    /// <summary>Both are fighters of the same running duel on opposite teams (Mobius isAutoAttackable in a duel).</summary>
    bool DuelFoes(ZoneEntity a, ZoneEntity b) =>
        a.DuelId != 0 && a.DuelId == b.DuelId && a.DuelActive && b.DuelActive && a.DuelTeam != b.DuelTeam;

    /// <summary>A hit between two members of the same duel: no PvP flag, no karma (Mobius updatePvPStatus in a duel).</summary>
    static bool SameDuel(ZoneEntity a, ZoneEntity b) => a.DuelId != 0 && a.DuelId == b.DuelId;

    /// <summary>
    /// Mobius PlayerStatus.reduceHp in a duel: a beaten or winning fighter takes no damage; a hit from outside the
    /// duel interrupts it. Returns true when the hit must not change HP.
    /// </summary>
    bool DuelBlocksHit(ZoneEntity? attacker, ZoneEntity victim)
    {
        if (victim.Kind != ZoneEntityKind.Player || victim.DuelId == 0 || victim.DuelState == DuelFighterState.None)
            return false;
        if (victim.DuelState is DuelFighterState.Dead or DuelFighterState.Winner)
            return true;
        var credit = attacker == null ? null : CreditOf(attacker);
        if (credit == null || credit.Kind != ZoneEntityKind.Player || credit.DuelId != victim.DuelId)
            victim.DuelState = DuelFighterState.Interrupted;
        return false;
    }

    /// <summary>Mobius NpcStatus / SummonStatus: a fighter hitting a monster or an outsider's summon interrupts its duel.</summary>
    void DuelNoteOutsideHit(ZoneEntity attacker, ZoneEntity target)
    {
        var credit = CreditOf(attacker);
        if (!credit.DuelActive)
            return;
        var owner = target.Summon != null ? OwnerOf(target) : null;
        if (owner != null && owner.DuelId == credit.DuelId)
            return;
        credit.DuelState = DuelFighterState.Interrupted;
    }

    /// <summary>
    /// Mobius PlayerStatus lethal hit in a duel: HP stays at 1, the fighter is beaten (no death, no penalty).
    /// Returns false when the player is not fighting a duel.
    /// </summary>
    bool DuelKnockout(ZoneEntity player)
    {
        if (!player.DuelActive)
            return false;
        player.CurHp = 1;
        player.DuelState = DuelFighterState.Dead;
        DuelStopFighting(player);
        foreach (var other in Players())
        {
            if (other.Session != null && other.Intent.AutoAttackTargetId == player.ObjectId)
                other.Intent.AutoAttackTargetId = 0;
        }
        FanoutVitals(player, PlayerVitalsStatus(player));
        ZoneLog.Info($"duel knockout object={player.ObjectId} duel={player.DuelId}");
        return true;
    }

    /// <summary>Mobius: a fighter cannot target players outside its duel, nor a beaten / winning fighter.</summary>
    bool DuelTargetRefused(ZoneEntity attacker, ZoneEntity target)
    {
        target = CreditOf(target);
        if (target.Kind != ZoneEntityKind.Player || ReferenceEquals(attacker, target))
            return false;
        if (attacker.DuelActive && target.DuelId != attacker.DuelId)
            return true;
        if (target.DuelId != 0 && target.DuelId == attacker.DuelId && !target.DuelActive)
            return true;
        return false;
    }

    /// <summary>Mobius Cubic.getCubicTarget in a duel: only the other side (1v1 opponent / enemy party member).</summary>
    ZoneEntity? DuelCubicTarget(ZoneEntity owner, ZoneEntity target)
    {
        var enemy = CreditOf(target);
        if (enemy.Kind != ZoneEntityKind.Player || !DuelFoes(owner, enemy))
            return null;
        return target;
    }

    /// <summary>Mobius Duel.onRemoveFromParty: a leader leaving cancels the party duel, anyone else just drops out.</summary>
    void DuelOnPartyRemove(int objectId)
    {
        if (!_byId.TryGetValue(objectId, out var m) || m.DuelId == 0 || !_duels.TryGetValue(m.DuelId, out var duel) || !duel.Party)
            return;
        if (objectId == duel.LeaderA || objectId == duel.LeaderB)
        {
            duel.Result = DuelResult.Canceled;
            DuelFinish(duel);
            return;
        }
        duel.TeamA.Remove(objectId);
        duel.TeamB.Remove(objectId);
        DuelRelease(m, duel, restore: true);
        NetBridge?.FanoutDuelEvent([objectId], new DuelEventNetMsg { Kind = DuelEventKind.End, Party = true });
    }

    /// <summary>Logout: the tick cancels the duel; a party fighter is saved at its pre-duel spot, not in the arena.</summary>
    void DuelOnLeave(ZoneEntity entity)
    {
        entity.DuelAskFrom = 0;
        if (entity.DuelId == 0 || !_duels.TryGetValue(entity.DuelId, out var duel))
            return;
        if (duel.Party && duel.Saved.TryGetValue(entity.ObjectId, out var saved) && entity.InstanceId == DuelInstanceBase + duel.Id)
        {
            entity.X = saved.X;
            entity.Y = saved.Y;
            entity.Z = saved.Z;
            entity.InstanceId = saved.Inst;
            if (entity.Session != null)
            {
                entity.Session.X = saved.X;
                entity.Session.Y = saved.Y;
                entity.Session.Z = saved.Z;
            }
        }
        entity.DuelId = 0;
        entity.DuelTeam = 0;
        entity.DuelState = DuelFighterState.None;
    }

    // ---------------------------------------------------------------- test hooks

    internal void RunDuelTickForTest()
    {
        lock (_gate)
            TickDuels(NowUnixMs());
    }

    internal int DuelCountForTest
    {
        get { lock (_gate) return _duels.Count; }
    }
}

public sealed partial class ZoneWorld
{
    /// <summary>Test hook: a skill hit of <paramref name="damage"/> from <paramref name="attacker"/> on a player.</summary>
    internal void SkillDamagePlayerForTest(ZoneEntity attacker, ZoneEntity target, int damage)
    {
        lock (_gate)
            ApplySkillDamageToPlayer(attacker, target, damage, NowUnixMs());
    }

    internal ZoneEntity? CubicTargetForTest(ZoneEntity owner)
    {
        lock (_gate)
            return CubicTarget(owner);
    }

    internal bool DuelTargetRefusedForTest(ZoneEntity attacker, ZoneEntity target)
    {
        lock (_gate)
            return DuelTargetRefused(attacker, target);
    }
}
