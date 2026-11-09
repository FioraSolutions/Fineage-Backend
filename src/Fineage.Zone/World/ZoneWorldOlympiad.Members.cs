using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Olympiad;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    void QueueMatch(OlympiadMatchNote note)
    {
        var message = new OlympiadMatchMsg
        {
            MatchId = note.MatchId,
            StadiumId = note.StadiumId,
            ZoneId = note.ZoneId,
            Classed = note.Classed,
            Phase = note.Phase,
            PlayerOneId = note.PlayerOneId,
            PlayerTwoId = note.PlayerTwoId,
            PlayerOneName = note.PlayerOneName,
            PlayerTwoName = note.PlayerTwoName,
            InstanceId = note.InstanceId
        };
        FanOlympiadBoth(note.PlayerOneId, note.PlayerTwoId, match: message);
    }

    void QueueCountdown(OlympiadCountdownNote note)
    {
        var packet = new OlympiadCountdownMsg
        {
            MatchId = note.MatchId,
            Kind = note.Kind,
            Seconds = note.Seconds
        };
        var line = new SystemMessageMsg
        {
            Id = OlympiadProtocol.CountdownLine,
            Params = [note.Kind.ToString(), note.Seconds.ToString()],
            Text = OlympiadLines.CountdownText(note.Kind, note.Seconds)
        };
        FanOlympiadBoth(note.PlayerOneId, note.PlayerTwoId, countdown: packet);
        FanOlympiadBoth(note.PlayerOneId, note.PlayerTwoId, line: line);
    }

    void QueueResult(OlympiadResultNote note)
    {
        var message = new OlympiadResultMsg
        {
            MatchId = note.MatchId,
            Outcome = note.Outcome,
            WinnerObjectId = note.WinnerObjectId,
            PointsOne = note.PointsOne,
            PointsTwo = note.PointsTwo,
            DeltaOne = note.DeltaOne,
            DeltaTwo = note.DeltaTwo,
            RewardItemId = note.RewardItemId,
            RewardCount = note.RewardCount,
            CompetitionsDoneOne = note.CompetitionsDoneOne,
            CompetitionsDoneTwo = note.CompetitionsDoneTwo
        };
        FanOlympiadBoth(note.PlayerOneId, note.PlayerTwoId, result: message);
    }

    void QueueOlympiadStatus(ZoneSession session, long nowUnixMs)
    {
        var view = _olympiad.View(session.ObjectId, nowUnixMs);
        var message = new OlympiadStatusMsg
        {
            Period = view.Period,
            Cycle = view.Cycle,
            InCompetition = view.InCompetition,
            CompetitionStartUnixMs = view.CompetitionStartUnixMs,
            CompetitionEndUnixMs = view.CompetitionEndUnixMs,
            OlympiadEndUnixMs = view.OlympiadEndUnixMs,
            ValidationEndUnixMs = view.ValidationEndUnixMs,
            NextWeeklyUnixMs = view.NextWeeklyUnixMs,
            RegistrationOpen = view.RegistrationOpen,
            Noble = view.Noble,
            Points = view.Points,
            CompetitionsDone = view.CompetitionsDone,
            Won = view.Won,
            Lost = view.Lost,
            Drawn = view.Drawn,
            Queue = view.Queue,
            MatchId = view.MatchId,
            StadiumId = view.StadiumId,
            ClassId = view.ClassId
        };
        NetBridge?.FanoutOlympiadStatus([session.ObjectId], message);
    }

    /// <summary>Both match participants get one Net message per event, exactly as <c>SendBoth</c> does for V2.</summary>
    void FanOlympiadBoth(
        int one,
        int two,
        OlympiadMatchMsg? match = null,
        OlympiadCountdownMsg? countdown = null,
        OlympiadResultMsg? result = null,
        SystemMessageMsg? line = null)
    {
        if (NetBridge == null)
            return;
        var watchers = new List<int>(2);
        if (_byId.TryGetValue(one, out var first) && first.Session is { InWorld: true })
            watchers.Add(one);
        if (two != one && _byId.TryGetValue(two, out var second) && second.Session is { InWorld: true })
            watchers.Add(two);
        if (watchers.Count == 0)
            return;
        if (match != null)
            NetBridge.FanoutOlympiadMatch(watchers, match);
        if (countdown != null)
            NetBridge.FanoutOlympiadCountdown(watchers, countdown);
        if (result != null)
            NetBridge.FanoutOlympiadResult(watchers, result);
        if (line != null)
            NetBridge.FanoutSystemMessage(watchers, line);
    }

    OlympiadPresence? LookupOlympiad(int characterId)
    {
        if (!_byId.TryGetValue(characterId, out var entity) || entity.Kind != ZoneEntityKind.Player || entity.Session is not { InWorld: true })
            return null;
        return new OlympiadPresence(
            true,
            entity.Dead,
            entity.ClassId,
            entity.ClassId,
            entity.X,
            entity.Y,
            entity.Z,
            entity.Inventory?.Items.Count ?? 0,
            InventoryLimit(entity),
            HasCursedWeapon(entity.Inventory));
    }

    static OlympiadActor ActorFor(ZoneEntity player) => new(
        player.ObjectId,
        player.Name,
        player.ClassId,
        player.ClassId,
        player.Inventory?.Items.Count ?? 0,
        InventoryLimit(player),
        HasCursedWeapon(player.Inventory));

    static int InventoryLimit(ZoneEntity player) =>
        player.Race == OlympiadProtocol.DwarfRace ? OlympiadProtocol.DwarfInventoryLimit : OlympiadProtocol.InventoryLimit;

    static bool HasCursedWeapon(PlayerInventory? inventory) =>
        inventory != null &&
        (inventory.CountOf(OlympiadProtocol.Zariche) > 0 || inventory.CountOf(OlympiadProtocol.Akamanah) > 0);

    void CollectVisible(ZoneEntity self, HashSet<int> into)
    {
        into.Clear();
        foreach (int id in _aoi.GetVisible(self.ObjectId))
        {
            if (_byId.TryGetValue(id, out var other) && other.InstanceId == self.InstanceId)
                into.Add(id);
        }
    }

    bool SameInstance(int leftId, int rightId)
    {
        if (!_byId.TryGetValue(leftId, out var left) || !_byId.TryGetValue(rightId, out var right))
            return false;
        return left.InstanceId == right.InstanceId;
    }
}
