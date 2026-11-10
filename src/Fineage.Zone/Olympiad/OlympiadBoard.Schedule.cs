using Fineage.Protocol;

namespace Fineage.Zone.Olympiad;

public sealed partial class OlympiadBoard
{
    void CatchUp(long now, OlympiadStep step, bool announce)
    {
        for (int i = 0; i < 8; i++)
        {
            FlushPeriodNotes(step, announce);
            bool moved = false;
            if (_period == OlympiadProtocol.PeriodValidation)
            {
                if (_inComp)
                    EndCompetition(step, announce);
                if (now >= _validationEnd)
                {
                    FinishValidation(now);
                    moved = true;
                }
            }
            else if (now >= _olympiadEnd)
            {
                if (_inComp)
                    EndCompetition(step, announce);
                BeginValidation(now);
                moved = true;
            }
            else
            {
                if (_nextWeekly > 0 && now >= _nextWeekly)
                {
                    ApplyWeekly();
                    _nextWeekly = now + OlympiadSchedule.WeeklyDelay(Settings);
                    Persist();
                    moved = true;
                }

                RefreshCompetition(now, step, announce);
            }

            FlushPeriodNotes(step, announce);
            if (!moved)
                return;
        }
    }

    void FlushPeriodNotes(OlympiadStep step, bool announce)
    {
        if (!announce)
            return;
        if (_announcePeriodStart)
        {
            step.Notices.Add(OlympiadLines.PeriodStarted(_cycle));
            step.RefreshAll = true;
            _announcePeriodStart = false;
        }

        if (_announcePeriodEnd)
        {
            step.Notices.Add(OlympiadLines.PeriodEnded(_cycle));
            step.RefreshAll = true;
            _announcePeriodEnd = false;
        }
    }

    void RefreshCompetition(long now, OlympiadStep step, bool announce)
    {
        if (_period != OlympiadProtocol.PeriodCompetition)
            return;
        var window = OlympiadSchedule.At(now, Settings);
        _compStart = window.StartUnixMs;
        _compEnd = window.EndUnixMs;
        if (window.Open)
        {
            if (!_inComp)
            {
                _inComp = true;
                _compAnnounced = false;
                _registrationAnnounced = false;
                _nextMatchmake = now;
            }

            if (announce && !_compAnnounced)
            {
                _compAnnounced = true;
                step.Notices.Add(OlympiadLines.CompetitionStarted());
                step.RefreshAll = true;
            }

            if (announce && !_registrationAnnounced && _compEnd - now < Settings.RegistrationCloseMillis)
            {
                _registrationAnnounced = true;
                step.Notices.Add(OlympiadLines.RegistrationEnded());
                step.RefreshAll = true;
            }
        }
        else if (_inComp)
        {
            EndCompetition(step, announce);
        }
    }

    void EndCompetition(OlympiadStep step, bool announce)
    {
        _inComp = false;
        _compAnnounced = false;
        _classed.Clear();
        _nonClass.Clear();
        if (announce)
        {
            step.Notices.Add(OlympiadLines.CompetitionEnded());
            step.RefreshAll = true;
        }
    }

    void BeginValidation(long now)
    {
        _period = OlympiadProtocol.PeriodValidation;
        _inComp = false;
        _compAnnounced = false;
        _classed.Clear();
        _nonClass.Clear();
        _validationEnd = now + Settings.ValidationMillis;
        _announcePeriodEnd = true;
        Persist();
    }

    void FinishValidation(long now)
    {
        _cycle++;
        _period = OlympiadProtocol.PeriodCompetition;
        _nobles.Clear();
        _validationEnd = 0;
        _olympiadEnd = OlympiadSchedule.MonthEnd(now, Settings);
        _nextWeekly = now + OlympiadSchedule.WeeklyDelay(Settings);
        _announcePeriodStart = true;
        Persist();
    }

    void ApplyWeekly()
    {
        if (_period == OlympiadProtocol.PeriodValidation)
            return;
        foreach (var noble in _nobles.Values)
            noble.Points = OlympiadRules.AddWeekly(noble.Points, noble.Done, Settings.MaxPoints, Settings.WeeklyPoints);
    }
}
