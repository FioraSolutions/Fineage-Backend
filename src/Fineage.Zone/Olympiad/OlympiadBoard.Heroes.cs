using Fineage.Protocol;

namespace Fineage.Zone.Olympiad;

/// <summary>
/// Block G2 period end, after Mobius <c>OlympiadEndTask</c> / <c>sortHerosToBe</c> / <c>Hero.computeNewHeroes</c> /
/// <c>loadNoblesRank</c>: one hero per Interlude third class (9+ matches, a win, best points / matches / wins),
/// the nobles table frozen for the ranking pages, and the Noblesse Gate Pass settlement. Heroes claim the status at
/// a Monument of Heroes; the previous heroes lose theirs when the next ones are chosen.
/// </summary>
public sealed partial class OlympiadBoard
{
    readonly Dictionary<int, OlympiadHeroRow> _heroes = [];
    readonly List<OlympiadNobleRow> _lastPeriod = [];
    readonly Dictionary<int, int> _passes = [];

    /// <summary>Claimed hero (Mobius <c>Hero.isHero</c>).</summary>
    public bool IsHero(int characterId)
    {
        lock (_gate)
            return _heroes.TryGetValue(characterId, out var hero) && hero.Claimed;
    }

    /// <summary>Chosen this period but not yet claimed at a monument (Mobius <c>isUnclaimedHero</c>).</summary>
    public bool IsUnclaimedHero(int characterId)
    {
        lock (_gate)
            return _heroes.TryGetValue(characterId, out var hero) && !hero.Claimed;
    }

    public bool ClaimHero(int characterId)
    {
        lock (_gate)
        {
            if (!_heroes.TryGetValue(characterId, out var hero) || hero.Claimed)
                return false;
            hero.Claimed = true;
            Persist();
            return true;
        }
    }

    /// <summary>Current heroes (Mobius <c>ExHeroList</c> / "who is the hero of this era").</summary>
    public IReadOnlyList<OlympiadHeroRow> Heroes()
    {
        lock (_gate)
            return _heroes.Values.OrderBy(h => h.ClassId).Select(Copy).ToList();
    }

    /// <summary>Mobius <c>getClassLeaderBoard</c>: the last period's top ten names of a class (9+ matches).</summary>
    public IReadOnlyList<string> ClassLeaders(int classId)
    {
        lock (_gate)
            return Ranked(_lastPeriod.Where(row => row.ClassId == classId)).Take(10).Select(row => row.Name).ToList();
    }

    /// <summary>Mobius <c>getLastNobleOlympiadPoints</c>: points the character ended the last period with.</summary>
    public int LastPeriodPoints(int characterId)
    {
        lock (_gate)
            return _lastPeriod.FirstOrDefault(row => row.CharacterId == characterId)?.Points ?? 0;
    }

    public int UnclaimedPasses(int characterId)
    {
        lock (_gate)
            return _passes.TryGetValue(characterId, out int passes) ? passes : 0;
    }

    /// <summary>Mobius <c>OlympiadNoble 10</c>: hands the settled passes over once.</summary>
    public int TakePasses(int characterId)
    {
        lock (_gate)
        {
            if (!_passes.Remove(characterId, out int passes))
                return 0;
            Persist();
            return passes;
        }
    }

    public int RegistrationCount(bool classed)
    {
        lock (_gate)
            return classed ? _classed.Values.Sum(list => list.Count) : _nonClass.Count;
    }

    /// <summary>Running match of the character: its id, the other side, and the phase.</summary>
    public (int MatchId, int OpponentId, OlympiadMatchPhase Phase, int StadiumId)? MatchState(int characterId)
    {
        lock (_gate)
        {
            var match = MatchOf(characterId);
            if (match == null)
                return null;
            return (match.Id, match.OneId == characterId ? match.TwoId : match.OneId, match.Phase, match.Stadium);
        }
    }

    /// <summary>Match ids by stadium for the observer list (Mobius <c>getMatchList</c>).</summary>
    public IReadOnlyDictionary<int, (int MatchId, string Title)> MatchesByStadium()
    {
        lock (_gate)
            return _matches.Values.Where(m => m.Ported && m.Phase != OlympiadMatchPhase.Done)
                .ToDictionary(m => m.Stadium, m => (m.Id, $"{NameOf(m.OneId)} : {NameOf(m.TwoId)}"));
    }

    /// <summary>Mobius <c>OlympiadGame.addDamage</c>: damage a side dealt during the fight decides a timeout.</summary>
    public void NoteDamage(int attackerId, int damage)
    {
        if (damage <= 0)
            return;
        lock (_gate)
        {
            var match = MatchOf(attackerId);
            if (match == null || match.Phase != OlympiadMatchPhase.Fighting || match.Resolved)
                return;
            if (match.OneId == attackerId)
                match.DamageOne += damage;
            else
                match.DamageTwo += damage;
        }
    }

    /// <summary>
    /// Period end bookkeeping, before the validation period starts. Returns the heroes that lost the status so the
    /// world can strip them.
    /// </summary>
    void CloseOlympiadPeriod(OlympiadStep step)
    {
        foreach (var old in _heroes.Values)
            step.RevokedHeroes.Add(old.CharacterId);

        var previous = _heroes.ToDictionary(pair => pair.Key, pair => pair.Value.Count);
        _heroes.Clear();
        for (int classId = OlympiadProtocol.FirstHeroClassId; classId <= OlympiadProtocol.LastHeroClassId; classId++)
        {
            var best = Ranked(_nobles.Values
                    .Where(n => n.ClassId == classId && n.Won > 0)
                    .Select(Row))
                .FirstOrDefault();
            if (best == null)
                continue;
            _heroes[best.CharacterId] = new OlympiadHeroRow
            {
                CharacterId = best.CharacterId,
                ClassId = classId,
                Name = best.Name,
                Claimed = false,
                Count = (previous.TryGetValue(best.CharacterId, out int count) ? count : 0) + 1
            };
        }

        _lastPeriod.Clear();
        _lastPeriod.AddRange(_nobles.Values.Select(Row));

        // Gate pass settlement: ranked nobles (9+ matches) with points left; heroes get the hero bonus on top.
        _passes.Clear();
        foreach (var noble in _nobles.Values)
        {
            if (noble.Done < OlympiadProtocol.HeroMinMatches || noble.Points <= 0)
                continue;
            int points = noble.Points + (_heroes.ContainsKey(noble.CharacterId) ? Settings.HeroPoints : 0);
            long passes = (long)points * Settings.GatePassPerPoint;
            if (passes > 0)
                _passes[noble.CharacterId] = (int)Math.Min(int.MaxValue, passes);
        }
    }

    static IEnumerable<OlympiadNobleRow> Ranked(IEnumerable<OlympiadNobleRow> rows) =>
        rows.Where(row => row.Done >= OlympiadProtocol.HeroMinMatches)
            .OrderByDescending(row => row.Points)
            .ThenByDescending(row => row.Done)
            .ThenByDescending(row => row.Won)
            .ThenBy(row => row.CharacterId);

    static OlympiadNobleRow Row(Noble noble) => new()
    {
        CharacterId = noble.CharacterId,
        ClassId = noble.ClassId,
        Name = noble.Name,
        Points = noble.Points,
        Done = noble.Done,
        Won = noble.Won,
        Lost = noble.Lost,
        Drawn = noble.Drawn
    };

    static OlympiadHeroRow Copy(OlympiadHeroRow row) => new()
    {
        CharacterId = row.CharacterId,
        ClassId = row.ClassId,
        Name = row.Name,
        Claimed = row.Claimed,
        Count = row.Count
    };

    void LoadHeroes(OlympiadSnapshot snap)
    {
        foreach (var row in snap.Heroes ?? [])
        {
            if (row.CharacterId > 0)
                _heroes[row.CharacterId] = Copy(row);
        }
        foreach (var row in snap.LastPeriod ?? [])
        {
            if (row.CharacterId > 0)
                _lastPeriod.Add(row);
        }
        foreach (var row in snap.Passes ?? [])
        {
            if (row.CharacterId > 0 && row.Passes > 0)
                _passes[row.CharacterId] = row.Passes;
        }
    }

    void ExportHeroes(OlympiadSnapshot snap)
    {
        snap.Heroes = _heroes.Values.OrderBy(h => h.ClassId).Select(Copy).ToList();
        snap.LastPeriod = _lastPeriod.Select(row => new OlympiadNobleRow
        {
            CharacterId = row.CharacterId,
            ClassId = row.ClassId,
            Name = row.Name,
            Points = row.Points,
            Done = row.Done,
            Won = row.Won,
            Lost = row.Lost,
            Drawn = row.Drawn
        }).ToList();
        snap.Passes = _passes.OrderBy(p => p.Key).Select(p => new OlympiadPassRow { CharacterId = p.Key, Passes = p.Value }).ToList();
    }
}
