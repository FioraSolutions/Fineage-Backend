using Fineage.Protocol;

namespace Fineage.Zone.SevenSigns;

public sealed partial class SevenSignsEngine
{
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
    /// Mobius <c>getAncientAdenaReward</c> via priest <c>SevenSigns 9</c>: only during seal validation and only for a
    /// member of the winning cabal. Collecting clears the reward and the stone tallies; the contribution score stays.
    /// Rewards under 3 are refused (Mobius shows the "nothing to collect" page).
    /// </summary>
    public string? TryClaimReward(int characterId, out int ancientAdena)
    {
        ancientAdena = 0;
        if (characterId <= 0)
            return "bad_request";
        lock (_gate)
        {
            if (_period != SevenSignsPeriod.SealValidation)
                return SevenSignsProtocol.PeriodClosed;
            var row = Find(characterId);
            if (row.Cabal == SevenSignsCabal.None || row.Cabal != Winner())
                return SevenSignsProtocol.NoReward;
            if (row.AncientAdena < 3)
                return SevenSignsProtocol.NoReward;
            ancientAdena = row.AncientAdena;
            row.AncientAdena = 0;
            row.Blue = 0;
            row.Green = 0;
            row.Red = 0;
            Persist(force: false);
        }

        return null;
    }

    /// <summary>Mobius <c>calcAncientAdenaReward</c>: what seal stones sell for at a priest (−1 on overflow).</summary>
    public static int StoneAncientAdena(int blue, int green, int red) => SevenSignsRules.ContributionPoints(blue, green, red);

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
}
