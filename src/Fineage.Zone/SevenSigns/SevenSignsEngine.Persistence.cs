using Fineage.Protocol;

namespace Fineage.Zone.SevenSigns;

public sealed partial class SevenSignsEngine
{
    public void Save()
    {
        lock (_gate)
            Persist(force: true);
    }

    void AssignSealOwners(IList<SevenSignsNotice> notices)
    {
        var winner = Winner();
        int dawnMembers = Count(SevenSignsCabal.Dawn);
        int duskMembers = Count(SevenSignsCabal.Dusk);
        _avarice = Assign(SevenSignsSeal.Avarice, winner, dawnMembers, duskMembers, notices);
        _gnosis = Assign(SevenSignsSeal.Gnosis, winner, dawnMembers, duskMembers, notices);
        _strife = Assign(SevenSignsSeal.Strife, winner, dawnMembers, duskMembers, notices);
    }

    SevenSignsCabal Assign(
        SevenSignsSeal seal,
        SevenSignsCabal winner,
        int dawnMembers,
        int duskMembers,
        IList<SevenSignsNotice> notices)
    {
        int dawnPercent = SevenSignsRules.MemberPercent(Choosing(SevenSignsCabal.Dawn, seal), dawnMembers);
        int duskPercent = SevenSignsRules.MemberPercent(Choosing(SevenSignsCabal.Dusk, seal), duskMembers);
        var previous = Owner(seal);
        var next = SevenSignsRules.ResolveOwner(
            previous,
            winner,
            dawnPercent,
            duskPercent,
            _options.ClaimPercent,
            _options.RetainPercent);
        if (next != SevenSignsCabal.None)
            notices.Add(SealNotice(seal, next));
        return next;
    }

    void ResetPlayers()
    {
        foreach (var row in _players.Values)
        {
            row.Cabal = SevenSignsCabal.None;
            row.Seal = SevenSignsSeal.None;
            row.Contribution = 0;
        }
    }

    int ScoreLocked(SevenSignsCabal cabal)
    {
        long total = _dawnStones + _duskStones;
        return cabal switch
        {
            SevenSignsCabal.Dawn => SevenSignsRules.CabalScore(_dawnStones, total, _dawnFestival, _options.StoneScoreScale),
            SevenSignsCabal.Dusk => SevenSignsRules.CabalScore(_duskStones, total, _duskFestival, _options.StoneScoreScale),
            _ => 0
        };
    }

    SevenSignsCabal Winner() => SevenSignsRules.Highest(ScoreLocked(SevenSignsCabal.Dusk), ScoreLocked(SevenSignsCabal.Dawn));

    int Count(SevenSignsCabal cabal)
    {
        int n = 0;
        foreach (var row in _players.Values)
        {
            if (row.Cabal == cabal)
                n++;
        }

        return n;
    }

    int Choosing(SevenSignsCabal cabal, SevenSignsSeal seal)
    {
        int n = 0;
        foreach (var row in _players.Values)
        {
            if (row.Cabal == cabal && row.Seal == seal)
                n++;
        }

        return n;
    }

    SevenSignsCabal Owner(SevenSignsSeal seal) => seal switch
    {
        SevenSignsSeal.Avarice => _avarice,
        SevenSignsSeal.Gnosis => _gnosis,
        SevenSignsSeal.Strife => _strife,
        _ => SevenSignsCabal.None
    };

    PlayerRecord Find(int characterId)
    {
        if (!_players.TryGetValue(characterId, out var row))
        {
            row = new PlayerRecord { CharacterId = characterId };
            _players[characterId] = row;
        }

        return row;
    }

    void Load(SevenSignsSnapshot snapshot)
    {
        _cycle = snapshot.Cycle < 1 ? 1 : snapshot.Cycle;
        _period = Enum.IsDefined(typeof(SevenSignsPeriod), snapshot.Period)
            ? (SevenSignsPeriod)snapshot.Period
            : SevenSignsPeriod.Recruiting;
        _previousWinner = CabalOrNone(snapshot.PreviousWinner);
        _dawnStones = Math.Max(0, snapshot.DawnStonePoints);
        _duskStones = Math.Max(0, snapshot.DuskStonePoints);
        _dawnFestival = Math.Max(0, snapshot.DawnFestivalScore);
        _duskFestival = Math.Max(0, snapshot.DuskFestivalScore);
        _avarice = CabalOrNone(snapshot.AvariceOwner);
        _gnosis = CabalOrNone(snapshot.GnosisOwner);
        _strife = CabalOrNone(snapshot.StrifeOwner);
        _players.Clear();
        foreach (var row in snapshot.Players)
        {
            if (row.CharacterId <= 0 || _players.ContainsKey(row.CharacterId))
                continue;
            _players[row.CharacterId] = new PlayerRecord
            {
                CharacterId = row.CharacterId,
                Cabal = CabalOrNone(row.Cabal),
                Seal = Enum.IsDefined(typeof(SevenSignsSeal), row.Seal) ? (SevenSignsSeal)row.Seal : SevenSignsSeal.None,
                Blue = Math.Max(0, row.BlueStones),
                Green = Math.Max(0, row.GreenStones),
                Red = Math.Max(0, row.RedStones),
                Contribution = Math.Max(0, row.Contribution),
                AncientAdena = Math.Max(0, row.AncientAdena)
            };
        }

        _nextChange = snapshot.NextChangeUnixMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(snapshot.NextChangeUnixMs)
            : SevenSignsSchedule.Next(_period, _clock.UtcNow, _options);
    }

    void Persist(bool force)
    {
        if (!force && _options.LazyUpdate)
            return;
        _store.Save(new SevenSignsSnapshot
        {
            Version = 1,
            Cycle = _cycle,
            Period = (int)_period,
            PreviousWinner = (int)_previousWinner,
            DawnStonePoints = _dawnStones,
            DuskStonePoints = _duskStones,
            DawnFestivalScore = _dawnFestival,
            DuskFestivalScore = _duskFestival,
            NextChangeUnixMs = _nextChange.ToUnixTimeMilliseconds(),
            AvariceOwner = (int)_avarice,
            GnosisOwner = (int)_gnosis,
            StrifeOwner = (int)_strife,
            Players = _players.Values
                .OrderBy(row => row.CharacterId)
                .Select(row => new SevenSignsPlayerRow
                {
                    CharacterId = row.CharacterId,
                    Cabal = (int)row.Cabal,
                    Seal = (int)row.Seal,
                    BlueStones = row.Blue,
                    GreenStones = row.Green,
                    RedStones = row.Red,
                    Contribution = row.Contribution,
                    AncientAdena = row.AncientAdena
                })
                .ToList()
        });
    }

    static int AddCapped(int current, int amount)
    {
        long next = (long)current + amount;
        if (next >= int.MaxValue)
            return int.MaxValue;
        return (int)next;
    }

    static SevenSignsCabal CabalOrNone(int value) =>
        value is (int)SevenSignsCabal.Dusk or (int)SevenSignsCabal.Dawn
            ? (SevenSignsCabal)value
            : SevenSignsCabal.None;

    static SevenSignsNotice Notice(int id, string text) => new(id, text);

    static SevenSignsNotice SealNotice(SevenSignsSeal seal, SevenSignsCabal owner)
    {
        bool dawn = owner == SevenSignsCabal.Dawn;
        string side = dawn ? "The Lords of Dawn" : "The Revolutionaries of Dusk";
        return seal switch
        {
            SevenSignsSeal.Avarice => Notice(
                dawn ? SevenSignsProtocol.AvariceDawnMessage : SevenSignsProtocol.AvariceDuskMessage,
                $"{side} obtained the Seal of Avarice."),
            SevenSignsSeal.Gnosis => Notice(
                dawn ? SevenSignsProtocol.GnosisDawnMessage : SevenSignsProtocol.GnosisDuskMessage,
                $"{side} obtained the Seal of Gnosis."),
            _ => Notice(
                dawn ? SevenSignsProtocol.StrifeDawnMessage : SevenSignsProtocol.StrifeDuskMessage,
                $"{side} obtained the Seal of Strife.")
        };
    }

    sealed class PlayerRecord
    {
        public int CharacterId { get; init; }

        public SevenSignsCabal Cabal { get; set; }

        public SevenSignsSeal Seal { get; set; }

        public int Blue { get; set; }

        public int Green { get; set; }

        public int Red { get; set; }

        public int Contribution { get; set; }

        public int AncientAdena { get; set; }
    }
}
