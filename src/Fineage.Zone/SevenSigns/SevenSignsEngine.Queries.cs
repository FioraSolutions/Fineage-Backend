using Fineage.Protocol;

namespace Fineage.Zone.SevenSigns;

public sealed partial class SevenSignsEngine
{
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
