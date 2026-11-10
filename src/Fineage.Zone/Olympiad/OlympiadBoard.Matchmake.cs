using Fineage.Protocol;

namespace Fineage.Zone.Olympiad;

public sealed partial class OlympiadBoard
{
    void Matchmake(long now, OlympiadStep step, Func<int, OlympiadPresence?>? presence)
    {
        if (_nobles.Count == 0)
        {
            _nextMatchmake = now + OlympiadProtocol.IdleMatchmakeMillis;
            return;
        }

        _nextMatchmake = now + OlympiadProtocol.MatchmakeMillis;
        var readyClasses = new List<int>();
        foreach (var (classId, list) in _classed)
        {
            if (list.Count >= Settings.ClassedMin)
                readyClasses.Add(classId);
        }

        bool gateNon = _nonClass.Count >= Settings.NonClassedMin;
        if (readyClasses.Count == 0 && !gateNon)
            return;

        int started = 0;
        for (int stadium = 0; stadium < Stadiums.Count; stadium++)
        {
            if (_busy[stadium])
                continue;
            List<int>? picked = readyClasses.Count == 0 ? null : PickClass(readyClasses);
            bool nonPair = gateNon && _nonClass.Count >= 2;
            if (picked == null && !nonPair)
                break;

            bool firstHalf = stadium < Stadiums.Count / 2;
            bool classedMatch;
            int one;
            int two;
            if (firstHalf)
            {
                if (nonPair)
                {
                    (one, two) = TakePair(_nonClass);
                    classedMatch = false;
                }
                else
                {
                    (one, two) = TakePair(picked!);
                    classedMatch = true;
                }
            }
            else if (picked != null)
            {
                (one, two) = TakePair(picked);
                classedMatch = true;
            }
            else
            {
                (one, two) = TakePair(_nonClass);
                classedMatch = false;
            }

            var match = new Match
            {
                Id = _nextMatchId++,
                Stadium = stadium,
                Classed = classedMatch,
                OneId = one,
                TwoId = two,
                Phase = OlympiadMatchPhase.Countdown,
                PhaseStart = now + started * 1000L,
                CountdownKind = OlympiadProtocol.CountdownStadium
            };
            match.PhaseEnds = match.PhaseStart + Settings.WaitSeconds * 1000L;
            match.Announcements = OlympiadSchedule.StadiumCountdown(match.PhaseStart, Settings.WaitSeconds);
            _busy[stadium] = true;
            _matches[match.Id] = match;
            started++;
            NotePhase(match, step);
            AdvanceMatch(match, now, step, presence);
        }
    }

    List<int>? PickClass(List<int> ready)
    {
        int classId = ready[_random.Next(ready.Count)];
        if (!_classed.TryGetValue(classId, out var list) || list.Count < 2)
            return null;
        return list;
    }

    (int One, int Two) TakePair(List<int> list)
    {
        int first = _random.Next(list.Count);
        int one = list[first];
        list.RemoveAt(first);
        int second = _random.Next(list.Count);
        int two = list[second];
        list.RemoveAt(second);
        return (one, two);
    }
}
