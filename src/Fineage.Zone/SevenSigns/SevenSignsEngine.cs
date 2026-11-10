using Fineage.Protocol;

namespace Fineage.Zone.SevenSigns;

/// <summary>
/// Period machine, cabal membership, seal-stone scores, and seal ownership.
/// Festival rooms are not run here; <see cref="AddFestivalScore"/> is the score hook.
/// </summary>
public sealed partial class SevenSignsEngine
{
    readonly IGameClock _clock;
    readonly SevenSignsOptions _options;
    readonly ISevenSignsStore _store;
    readonly ISevenSignsFestivalHook? _festival;
    readonly object _gate = new();
    readonly Dictionary<int, PlayerRecord> _players = [];

    int _cycle = 1;
    SevenSignsPeriod _period = SevenSignsPeriod.Recruiting;
    SevenSignsCabal _previousWinner = SevenSignsCabal.None;
    long _dawnStones;
    long _duskStones;
    int _dawnFestival;
    int _duskFestival;
    DateTimeOffset _nextChange;
    SevenSignsCabal _avarice = SevenSignsCabal.None;
    SevenSignsCabal _gnosis = SevenSignsCabal.None;
    SevenSignsCabal _strife = SevenSignsCabal.None;

    public SevenSignsEngine(
        IGameClock clock,
        SevenSignsOptions? options = null,
        ISevenSignsStore? store = null,
        ISevenSignsFestivalHook? festival = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? new SevenSignsOptions();
        _store = store ?? new MemorySevenSignsStore();
        _festival = festival;
        Load(_store.Load());
    }

    public SevenSignsOptions Options => _options;

    public SevenSignsPeriod Period
    {
        get { lock (_gate) return _period; }
    }

    public int Cycle
    {
        get { lock (_gate) return _cycle; }
    }

    public DateTimeOffset NextChange
    {
        get { lock (_gate) return _nextChange; }
    }

    /// <summary>Opens <c>seven-signs.json</c> inside <paramref name="stateDirectory"/> (default <c>data</c>).</summary>
    public static SevenSignsEngine OpenInStateDirectory(string stateDirectory, IGameClock? clock = null)
    {
        string path = Path.Combine(string.IsNullOrWhiteSpace(stateDirectory) ? "data" : stateDirectory, "seven-signs.json");
        return new SevenSignsEngine(
            clock ?? new SystemGameClock(),
            SevenSignsOptions.FromEnvironment(),
            new JsonSevenSignsStore(path));
    }

    public bool AdvanceDue(IList<SevenSignsNotice> notices) => TryAdvance(_clock.UtcNow, notices);

    public bool TryAdvance(DateTimeOffset utcNow, IList<SevenSignsNotice> notices)
    {
        ArgumentNullException.ThrowIfNull(notices);
        SevenSignsPeriod ended;
        SevenSignsCabal endedWinner = SevenSignsCabal.None;
        lock (_gate)
        {
            if (utcNow < _nextChange)
                return false;

            ended = _period;
            _period = ended == SevenSignsPeriod.SealValidation
                ? SevenSignsPeriod.Recruiting
                : ended + 1;
            if (ended == SevenSignsPeriod.Competition)
                endedWinner = Winner();
            // Move the boundary first so a festival hook cannot advance the same period again.
            _nextChange = SevenSignsSchedule.Next(_period, utcNow, _options);
        }

        if (ended == SevenSignsPeriod.Recruiting)
            _festival?.OnCompetitionStarted();
        else if (ended == SevenSignsPeriod.Competition)
            _festival?.OnCompetitionEnded(endedWinner);

        lock (_gate)
        {
            switch (ended)
            {
                case SevenSignsPeriod.Recruiting:
                    notices.Add(Notice(SevenSignsProtocol.PeriodCompetitionMessage,
                        "Seven Signs competition has begun."));
                    break;
                case SevenSignsPeriod.Competition:
                    notices.Add(Notice(SevenSignsProtocol.PeriodResultsMessage,
                        "Seven Signs competition has ended. Results are being tallied."));
                    // Festival reward has already run, so seal ownership sees those points.
                    // The announced winner stays the score from before that reward.
                    AssignSealOwners(notices);
                    if (endedWinner == SevenSignsCabal.Dawn)
                        notices.Add(Notice(SevenSignsProtocol.DawnWonMessage, "The Lords of Dawn won the competition."));
                    else if (endedWinner == SevenSignsCabal.Dusk)
                        notices.Add(Notice(SevenSignsProtocol.DuskWonMessage, "The Revolutionaries of Dusk won the competition."));
                    _previousWinner = endedWinner;
                    break;
                case SevenSignsPeriod.Results:
                    notices.Add(Notice(SevenSignsProtocol.PeriodValidationMessage,
                        "Seven Signs seal validation has begun."));
                    break;
                case SevenSignsPeriod.SealValidation:
                    notices.Add(Notice(SevenSignsProtocol.PeriodRecruitingMessage,
                        "Seven Signs seal validation has ended. Recruitment is open."));
                    ResetPlayers();
                    break;
            }

            if (ended != SevenSignsPeriod.SealValidation)
                Persist(force: true);
        }

        if (ended == SevenSignsPeriod.SealValidation)
        {
            _festival?.OnCycleReset();
            lock (_gate)
            {
                _dawnStones = 0;
                _duskStones = 0;
                _dawnFestival = 0;
                _duskFestival = 0;
                _cycle++;
                Persist(force: true);
            }
        }

        return true;
    }

    public string? TryJoin(
        int characterId,
        int classLevel,
        bool clanOwnsCastle,
        SevenSignsCabal cabal,
        SevenSignsSeal seal,
        SevenSignsPayer? pay,
        IList<SevenSignsNotice> notices)
    {
        ArgumentNullException.ThrowIfNull(notices);
        if (characterId <= 0)
            return "bad_request";
        if (cabal is not (SevenSignsCabal.Dawn or SevenSignsCabal.Dusk))
            return SevenSignsProtocol.BadCabal;
        if (seal is not (SevenSignsSeal.Avarice or SevenSignsSeal.Gnosis or SevenSignsSeal.Strife))
            return SevenSignsProtocol.BadSeal;

        SevenSignsJoinCost cost = default;
        bool charge = false;
        lock (_gate)
        {
            if (_period != SevenSignsPeriod.Competition)
                return SevenSignsProtocol.PeriodClosed;
            if (classLevel <= 0)
                return SevenSignsProtocol.ClassTooLow;
            if (Find(characterId).Cabal != SevenSignsCabal.None)
                return SevenSignsProtocol.BadCabal;

            if (cabal == SevenSignsCabal.Dusk && _options.CastleRestrictsDusk && clanOwnsCastle)
                return SevenSignsProtocol.CastleBlocked;

            if (cabal == SevenSignsCabal.Dawn &&
                _options.CastleRestrictsDawn &&
                classLevel >= 2 &&
                !clanOwnsCastle)
            {
                charge = true;
                cost = new SevenSignsJoinCost(0, 1);
            }
        }

        if (charge)
        {
            if (pay == null || !pay(cost))
            {
                var adena = new SevenSignsJoinCost(_options.DawnJoinFeeAdena, 0);
                if (pay == null || !pay(adena))
                    return SevenSignsProtocol.FeeRequired;
            }
        }

        lock (_gate)
        {
            if (_period != SevenSignsPeriod.Competition)
                return SevenSignsProtocol.PeriodClosed;
            var row = Find(characterId);
            if (row.Cabal != SevenSignsCabal.None)
                return SevenSignsProtocol.BadCabal;
            row.Cabal = cabal;
            row.Seal = seal;
            if (cabal == SevenSignsCabal.Dawn)
                notices.Add(Notice(SevenSignsProtocol.JoinedDawnMessage, "You joined the Lords of Dawn."));
            else
                notices.Add(Notice(SevenSignsProtocol.JoinedDuskMessage, "You joined the Revolutionaries of Dusk."));
            notices.Add(seal switch
            {
                SevenSignsSeal.Avarice => Notice(SevenSignsProtocol.ChoseAvariceMessage, "You chose the Seal of Avarice."),
                SevenSignsSeal.Gnosis => Notice(SevenSignsProtocol.ChoseGnosisMessage, "You chose the Seal of Gnosis."),
                _ => Notice(SevenSignsProtocol.ChoseStrifeMessage, "You chose the Seal of Strife.")
            });
            Persist(force: false);
        }

        return null;
    }

    public string? TryContribute(int characterId, int blue, int green, int red)
    {
        if (characterId <= 0 || blue < 0 || green < 0 || red < 0)
            return "bad_request";
        if (blue == 0 && green == 0 && red == 0)
            return "bad_request";

        int points = SevenSignsRules.ContributionPoints(blue, green, red);
        if (points < 0)
            return SevenSignsProtocol.ContributionRejected;

        lock (_gate)
        {
            if (_period != SevenSignsPeriod.Competition)
                return SevenSignsProtocol.PeriodClosed;
            var row = Find(characterId);
            if (row.Cabal == SevenSignsCabal.None)
                return SevenSignsProtocol.BadCabal;
            long next = (long)row.Contribution + points;
            if (next > _options.MaxPlayerContribution)
                return SevenSignsProtocol.ContributionRejected;

            row.Blue += blue;
            row.Green += green;
            row.Red += red;
            row.Contribution = (int)next;
            long ancient = (long)row.AncientAdena + points;
            row.AncientAdena = ancient > int.MaxValue ? int.MaxValue : (int)ancient;
            if (row.Cabal == SevenSignsCabal.Dawn)
                _dawnStones += points;
            else
                _duskStones += points;
            Persist(force: false);
        }

        return null;
    }

    /// <summary>
    /// Festival of Darkness score hook. The same amount is removed from the rival when the rival has at least that many festival points.
    /// </summary>
    public void AddFestivalScore(SevenSignsCabal cabal, int amount)
    {
        if (amount <= 0 || cabal is not (SevenSignsCabal.Dawn or SevenSignsCabal.Dusk))
            return;

        lock (_gate)
        {
            if (cabal == SevenSignsCabal.Dusk)
            {
                _duskFestival = AddCapped(_duskFestival, amount);
                if (_dawnFestival >= amount)
                    _dawnFestival -= amount;
            }
            else
            {
                _dawnFestival = AddCapped(_dawnFestival, amount);
                if (_duskFestival >= amount)
                    _duskFestival -= amount;
            }

            Persist(force: false);
        }
    }

    public int Score(SevenSignsCabal cabal)
    {
        lock (_gate)
            return ScoreLocked(cabal);
    }

    public SevenSignsCabal WinningCabal
    {
        get { lock (_gate) return Winner(); }
    }

    public int PlayerCabal(int characterId)
    {
        lock (_gate)
            return (int)Find(characterId).Cabal;
    }

    public int PlayerSeal(int characterId)
    {
        lock (_gate)
            return (int)Find(characterId).Seal;
    }

    public int PlayerContribution(int characterId)
    {
        lock (_gate)
            return Find(characterId).Contribution;
    }

    public SevenSignsEffects Effects()
    {
        lock (_gate)
        {
            var winner = Winner();
            bool window = _period is SevenSignsPeriod.Results or SevenSignsPeriod.SealValidation;
            bool avariceLive = window && _avarice != SevenSignsCabal.None && _avarice == winner;
            bool gnosisLive = window && _gnosis != SevenSignsCabal.None && _gnosis == winner;
            double physical = 1;
            double magic = 1;
            if (_strife == SevenSignsCabal.Dawn)
            {
                physical = _options.DawnGatePhysical;
                magic = _options.DawnGateMagic;
            }
            else if (_strife == SevenSignsCabal.Dusk)
            {
                physical = _options.DuskGatePhysical;
                magic = _options.DuskGateMagic;
            }

            return new SevenSignsEffects
            {
                ResultsOrValidation = window,
                SealValidation = _period == SevenSignsPeriod.SealValidation,
                AvariceOwner = _avarice,
                GnosisOwner = _gnosis,
                StrifeOwner = _strife,
                WinningCabal = winner,
                MammonMerchant = avariceLive,
                Lilith = avariceLive && winner == SevenSignsCabal.Dawn,
                Anakim = avariceLive && winner == SevenSignsCabal.Dusk,
                MammonBlacksmith = gnosisLive,
                Preachers = gnosisLive,
                StrifeGatePhysical = physical,
                StrifeGateMagic = magic,
                DuskSummonBlocked = _period == SevenSignsPeriod.SealValidation && _strife == SevenSignsCabal.Dawn
            };
        }
    }

    public int SkyState()
    {
        lock (_gate)
            return SevenSignsRules.SkyState(_period, Winner());
    }

    public SevenSignsStatusMsg StatusFor(int characterId)
    {
        lock (_gate)
        {
            var row = Find(characterId);
            int dawnMembers = Count(SevenSignsCabal.Dawn);
            int duskMembers = Count(SevenSignsCabal.Dusk);
            return new SevenSignsStatusMsg
            {
                Period = (int)_period,
                Cycle = _cycle,
                PlayerCabal = (int)row.Cabal,
                PlayerSeal = (int)row.Seal,
                PlayerStoneCount = row.Blue + row.Green + row.Red,
                PlayerAncientAdena = row.AncientAdena,
                PlayerContribution = row.Contribution,
                DawnScore = ScoreLocked(SevenSignsCabal.Dawn),
                DuskScore = ScoreLocked(SevenSignsCabal.Dusk),
                DawnFestivalScore = _dawnFestival,
                DuskFestivalScore = _duskFestival,
                DawnStonePoints = _dawnStones,
                DuskStonePoints = _duskStones,
                AvariceOwner = (int)_avarice,
                GnosisOwner = (int)_gnosis,
                StrifeOwner = (int)_strife,
                DawnMembers = dawnMembers,
                DuskMembers = duskMembers,
                WinningCabal = (int)Winner(),
                NextChangeUnixMs = _nextChange.ToUnixTimeMilliseconds(),
                SkyState = SevenSignsRules.SkyState(_period, Winner()),
                DawnAvarice = Choosing(SevenSignsCabal.Dawn, SevenSignsSeal.Avarice),
                DawnGnosis = Choosing(SevenSignsCabal.Dawn, SevenSignsSeal.Gnosis),
                DawnStrife = Choosing(SevenSignsCabal.Dawn, SevenSignsSeal.Strife),
                DuskAvarice = Choosing(SevenSignsCabal.Dusk, SevenSignsSeal.Avarice),
                DuskGnosis = Choosing(SevenSignsCabal.Dusk, SevenSignsSeal.Gnosis),
                DuskStrife = Choosing(SevenSignsCabal.Dusk, SevenSignsSeal.Strife),
                PreviousWinner = (int)_previousWinner
            };
        }
    }

    public SevenSignsSkyMsg Sky()
    {
        lock (_gate)
        {
            var winner = Winner();
            return new SevenSignsSkyMsg
            {
                State = SevenSignsRules.SkyState(_period, winner),
                Period = (int)_period,
                WinningCabal = (int)winner
            };
        }
    }
}
