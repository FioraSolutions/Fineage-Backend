using Fineage.Protocol;

namespace Fineage.Zone.Olympiad;

public sealed partial class OlympiadBoard
{
    void Matchmake(long now, OlympiadStep step, Func<int, OlympiadPresence?>? presence, Random random)
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
            List<int>? picked = readyClasses.Count == 0 ? null : PickClass(readyClasses, random);
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
                    (one, two) = TakePair(_nonClass, random);
                    classedMatch = false;
                }
                else
                {
                    (one, two) = TakePair(picked!, random);
                    classedMatch = true;
                }
            }
            else if (picked != null)
            {
                (one, two) = TakePair(picked, random);
                classedMatch = true;
            }
            else
            {
                (one, two) = TakePair(_nonClass, random);
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

    List<int>? PickClass(List<int> ready, Random random)
    {
        int classId = ready[random.Next(ready.Count)];
        if (!_classed.TryGetValue(classId, out var list) || list.Count < 2)
            return null;
        return list;
    }

    (int One, int Two) TakePair(List<int> list, Random random)
    {
        int first = random.Next(list.Count);
        int one = list[first];
        list.RemoveAt(first);
        int second = random.Next(list.Count);
        int two = list[second];
        list.RemoveAt(second);
        return (one, two);
    }
}
