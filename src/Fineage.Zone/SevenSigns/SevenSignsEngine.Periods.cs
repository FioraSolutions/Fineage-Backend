using Fineage.Protocol;

namespace Fineage.Zone.SevenSigns;

public sealed partial class SevenSignsEngine
{
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
}
