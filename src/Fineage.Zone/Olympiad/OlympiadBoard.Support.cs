using Fineage.Protocol;

namespace Fineage.Zone.Olympiad;

public sealed partial class OlympiadBoard
{
    void ApplyScore(int characterId, OlympiadRules.Score score)
    {
        if (!_nobles.TryGetValue(characterId, out var noble))
            return;
        noble.Points = score.Points;
        noble.Done = score.Done;
        noble.Won = score.Won;
        noble.Lost = score.Lost;
        noble.Drawn = score.Drawn;
    }

    OlympiadRules.Score ScoreOf(int characterId)
    {
        if (!_nobles.TryGetValue(characterId, out var noble))
            return new OlympiadRules.Score(Settings.StartPoints, 0, 0, 0, 0);
        return new OlympiadRules.Score(noble.Points, noble.Done, noble.Won, noble.Lost, noble.Drawn);
    }

    string NameOf(int characterId) =>
        _nobles.TryGetValue(characterId, out var noble) ? noble.Name : "";

    bool RegistrationOpen(long now) =>
        Settings.Enabled &&
        _inComp &&
        _period == OlympiadProtocol.PeriodCompetition &&
        _compEnd - now >= Settings.RegistrationCloseMillis;

    bool OnClassedList(int characterId)
    {
        foreach (var list in _classed.Values)
        {
            if (list.Contains(characterId))
                return true;
        }

        return false;
    }

    void RemoveFromQueues(int characterId)
    {
        _nonClass.Remove(characterId);
        foreach (var list in _classed.Values)
            list.Remove(characterId);
    }

    Match? MatchOf(int characterId)
    {
        foreach (var match in _matches.Values)
        {
            if (match.OneId == characterId || match.TwoId == characterId)
                return match;
        }

        return null;
    }

    static void Fail(OlympiadStep step, int characterId, string code, OlympiadNotice notice)
    {
        step.Error = code;
        step.Notices.Add(notice);
        _ = characterId;
    }

    static OlympiadNotice NoticeFor(string code, int characterId, bool classed) => code switch
    {
        OlympiadProtocol.Disabled => OlympiadLines.NotInProgress(characterId),
        OlympiadProtocol.Period => OlympiadLines.NotInProgress(characterId),
        OlympiadProtocol.Noble => OlympiadLines.NoblesOnly(characterId),
        OlympiadProtocol.Subclass => OlympiadLines.Subclass(characterId),
        OlympiadProtocol.Cursed => OlympiadLines.Cursed(characterId),
        OlympiadProtocol.Inventory => OlympiadLines.Inventory(characterId),
        OlympiadProtocol.Late => OlympiadLines.TooLate(characterId),
        OlympiadProtocol.Classed => OlympiadLines.AlreadyClassed(characterId),
        OlympiadProtocol.Registered => OlympiadLines.AlreadyRegistered(characterId),
        OlympiadProtocol.Points => OlympiadLines.NeedPoints(characterId, classed ? OlympiadProtocol.ClassedPointFloor : OlympiadProtocol.NonClassedPointFloor),
        _ => OlympiadLines.NotInProgress(characterId)
    };

    void Load(OlympiadSnapshot snap, long now)
    {
        _cycle = snap.Cycle < 1 ? 1 : snap.Cycle;
        _period = snap.Period == OlympiadProtocol.PeriodValidation
            ? OlympiadProtocol.PeriodValidation
            : OlympiadProtocol.PeriodCompetition;
        _olympiadEnd = snap.OlympiadEndUnixMs;
        _validationEnd = snap.ValidationEndUnixMs;
        _nextWeekly = snap.NextWeeklyUnixMs;
        foreach (int id in snap.Granted)
        {
            if (id > 0)
                _granted.Add(id);
        }

        foreach (var row in snap.Nobles)
        {
            if (row.CharacterId <= 0 || _nobles.ContainsKey(row.CharacterId))
                continue;
            _nobles[row.CharacterId] = new Noble
            {
                CharacterId = row.CharacterId,
                ClassId = row.ClassId,
                Name = row.Name ?? "",
                Points = OlympiadRules.ClampStored(row.Points, Math.Max(0, row.Done), Settings.MaxPoints, Settings.WeeklyPoints),
                Done = Math.Max(0, row.Done),
                Won = Math.Max(0, row.Won),
                Lost = Math.Max(0, row.Lost),
                Drawn = Math.Max(0, row.Drawn)
            };
        }

        bool fresh = snap.OlympiadEndUnixMs == 0;
        if (fresh)
        {
            _period = OlympiadProtocol.PeriodCompetition;
            _olympiadEnd = OlympiadSchedule.MonthEnd(now, Settings);
            _validationEnd = 0;
            _nextWeekly = now + OlympiadSchedule.WeeklyDelay(Settings);
            _announcePeriodStart = true;
            Persist();
            return;
        }

        if (_period == OlympiadProtocol.PeriodValidation)
        {
            if (now >= _validationEnd)
                FinishValidation(now);
            return;
        }

        if (now >= _olympiadEnd)
        {
            _olympiadEnd = OlympiadSchedule.MonthEnd(now, Settings);
            _nextWeekly = now + OlympiadSchedule.WeeklyDelay(Settings);
            _announcePeriodStart = true;
            Persist();
            return;
        }

        if (_nextWeekly > 0 && now >= _nextWeekly)
        {
            ApplyWeekly();
            _nextWeekly = now + OlympiadSchedule.WeeklyDelay(Settings);
            Persist();
        }
    }

    bool Persist()
    {
        try
        {
            _store.Save(Export());
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    OlympiadSnapshot Export()
    {
        var nobles = new List<OlympiadNobleRow>(_nobles.Count);
        foreach (var noble in _nobles.Values.OrderBy(row => row.CharacterId))
        {
            nobles.Add(new OlympiadNobleRow
            {
                CharacterId = noble.CharacterId,
                ClassId = noble.ClassId,
                Name = noble.Name,
                Points = noble.Points,
                Done = noble.Done,
                Won = noble.Won,
                Lost = noble.Lost,
                Drawn = noble.Drawn
            });
        }

        return new OlympiadSnapshot
        {
            Version = 1,
            Cycle = _cycle,
            Period = _period,
            OlympiadEndUnixMs = _olympiadEnd,
            ValidationEndUnixMs = _validationEnd,
            NextWeeklyUnixMs = _nextWeekly,
            Granted = _granted.OrderBy(id => id).ToList(),
            Nobles = nobles
        };
    }

    sealed class Noble
    {
        public int CharacterId;
        public int ClassId;
        public string Name = "";
        public int Points;
        public int Done;
        public int Won;
        public int Lost;
        public int Drawn;
    }

    sealed class Match
    {
        public int Id;
        public int Stadium;
        public bool Classed;
        public int OneId;
        public int TwoId;
        public OlympiadMatchPhase Phase;
        public long PhaseStart;
        public long PhaseEnds;
        public bool Ported;
        public bool Resolved;
        public bool OneLeft;
        public bool TwoLeft;
        public bool HomeOne;
        public bool HomeTwo;
        public int ReturnOneX;
        public int ReturnOneY;
        public int ReturnOneZ;
        public int ReturnTwoX;
        public int ReturnTwoY;
        public int ReturnTwoZ;
        public int CountdownKind;
        public int Cursor;
        public List<OlympiadSchedule.Announcement> Announcements = [];
    }
}
