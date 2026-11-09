using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class NetWorldEventPublisher
{
    void IWorldEventPublisher.FanoutBossStatus(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.BossStatusMsg status)
    {
        var src = status.Bosses ?? [];
        var bosses = new BossStatusEntryNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            var row = src[i];
            bosses[i] = new BossStatusEntryNet
            {
                NpcId = row.NpcId,
                ObjectId = row.ObjectId,
                Kind = row.Kind,
                Status = row.Status,
                RespawnUnixMs = row.RespawnUnixMs,
                CurrentHp = row.CurrentHp,
                MaxHp = row.MaxHp,
                CurrentMp = row.CurrentMp,
                MaxMp = row.MaxMp,
                Enabled = row.Enabled,
                Spawned = row.Spawned
            };
        }
        var payload = NetCodec.EncodeBossStatus(new BossStatusNetMsg { Bosses = bosses });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.BossStatus, payload);
    }

    void IWorldEventPublisher.FanoutSiegeInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SiegeInfoMsg info)
    {
        var clans = info.Clans ?? [];
        var netClans = new SiegeClanNet[clans.Length];
        for (int i = 0; i < clans.Length; i++)
        {
            var row = clans[i];
            netClans[i] = new SiegeClanNet { ClanId = row.ClanId, Side = row.Side };
        }
        var payload = NetCodec.EncodeSiegeInfo(new SiegeInfoNetMsg
        {
            CastleId = info.CastleId,
            Name = info.Name ?? "",
            OwnerClanId = info.OwnerClanId,
            TaxPercent = info.TaxPercent,
            SiegeUnixMs = info.SiegeUnixMs,
            RegistrationDeadlineUnixMs = info.RegistrationDeadlineUnixMs,
            RegistrationClosed = info.RegistrationClosed,
            InProgress = info.InProgress,
            EndsUnixMs = info.EndsUnixMs,
            Clans = netClans
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.SiegeInfo, payload);
    }

    void IWorldEventPublisher.FanoutOlympiadStatus(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.OlympiadStatusMsg status)
    {
        var payload = NetCodec.EncodeOlympiadStatus(new OlympiadStatusNetMsg
        {
            Period = status.Period,
            Cycle = status.Cycle,
            InCompetition = status.InCompetition,
            CompetitionStartUnixMs = status.CompetitionStartUnixMs,
            CompetitionEndUnixMs = status.CompetitionEndUnixMs,
            OlympiadEndUnixMs = status.OlympiadEndUnixMs,
            ValidationEndUnixMs = status.ValidationEndUnixMs,
            NextWeeklyUnixMs = status.NextWeeklyUnixMs,
            RegistrationOpen = status.RegistrationOpen,
            Noble = status.Noble,
            Points = status.Points,
            CompetitionsDone = status.CompetitionsDone,
            Won = status.Won,
            Lost = status.Lost,
            Drawn = status.Drawn,
            Queue = status.Queue,
            MatchId = status.MatchId,
            StadiumId = status.StadiumId,
            ClassId = status.ClassId
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.OlympiadStatus, payload);
    }

    void IWorldEventPublisher.FanoutOlympiadMatch(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.OlympiadMatchMsg match)
    {
        var payload = NetCodec.EncodeOlympiadMatch(new OlympiadMatchNetMsg
        {
            MatchId = match.MatchId,
            StadiumId = match.StadiumId,
            ZoneId = match.ZoneId,
            Classed = match.Classed,
            Phase = match.Phase,
            PlayerOneId = match.PlayerOneId,
            PlayerTwoId = match.PlayerTwoId,
            PlayerOneName = match.PlayerOneName ?? "",
            PlayerTwoName = match.PlayerTwoName ?? "",
            InstanceId = match.InstanceId
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.OlympiadMatch, payload);
    }

    void IWorldEventPublisher.FanoutOlympiadCountdown(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.OlympiadCountdownMsg countdown)
    {
        var payload = NetCodec.EncodeOlympiadCountdown(new OlympiadCountdownNetMsg
        {
            MatchId = countdown.MatchId,
            Kind = countdown.Kind,
            Seconds = countdown.Seconds
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.OlympiadCountdown, payload);
    }

    void IWorldEventPublisher.FanoutOlympiadResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.OlympiadResultMsg result)
    {
        var payload = NetCodec.EncodeOlympiadResult(new OlympiadResultNetMsg
        {
            MatchId = result.MatchId,
            Outcome = result.Outcome,
            WinnerObjectId = result.WinnerObjectId,
            PointsOne = result.PointsOne,
            PointsTwo = result.PointsTwo,
            DeltaOne = result.DeltaOne,
            DeltaTwo = result.DeltaTwo,
            RewardItemId = result.RewardItemId,
            RewardCount = result.RewardCount,
            CompetitionsDoneOne = result.CompetitionsDoneOne,
            CompetitionsDoneTwo = result.CompetitionsDoneTwo
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.OlympiadResult, payload);
    }

    void IWorldEventPublisher.FanoutSevenSignsStatus(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SevenSignsStatusMsg status)
    {
        var payload = NetCodec.EncodeSevenSignsStatus(new SevenSignsStatusNetMsg
        {
            Period = status.Period,
            Cycle = status.Cycle,
            PlayerCabal = status.PlayerCabal,
            PlayerSeal = status.PlayerSeal,
            PlayerStoneCount = status.PlayerStoneCount,
            PlayerAncientAdena = status.PlayerAncientAdena,
            PlayerContribution = status.PlayerContribution,
            DawnScore = status.DawnScore,
            DuskScore = status.DuskScore,
            DawnFestivalScore = status.DawnFestivalScore,
            DuskFestivalScore = status.DuskFestivalScore,
            DawnStonePoints = status.DawnStonePoints,
            DuskStonePoints = status.DuskStonePoints,
            AvariceOwner = status.AvariceOwner,
            GnosisOwner = status.GnosisOwner,
            StrifeOwner = status.StrifeOwner,
            DawnMembers = status.DawnMembers,
            DuskMembers = status.DuskMembers,
            WinningCabal = status.WinningCabal,
            NextChangeUnixMs = status.NextChangeUnixMs,
            SkyState = status.SkyState,
            DawnAvarice = status.DawnAvarice,
            DawnGnosis = status.DawnGnosis,
            DawnStrife = status.DawnStrife,
            DuskAvarice = status.DuskAvarice,
            DuskGnosis = status.DuskGnosis,
            DuskStrife = status.DuskStrife,
            PreviousWinner = status.PreviousWinner
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.SevenSignsStatus, payload);
    }

    void IWorldEventPublisher.FanoutSevenSignsSky(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SevenSignsSkyMsg sky)
    {
        var payload = NetCodec.EncodeSevenSignsSky(new SevenSignsSkyNetMsg
        {
            State = sky.State,
            Period = sky.Period,
            WinningCabal = sky.WinningCabal
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.SevenSignsSky, payload);
    }

    void IWorldEventPublisher.FanoutDungeonEntered(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.DungeonEnteredMsg entered)
    {
        var payload = NetCodec.EncodeDungeonEntered(new DungeonEnteredNetMsg
        {
            DungeonId = entered.DungeonId,
            X = entered.X,
            Y = entered.Y,
            Z = entered.Z,
            Inside = entered.Inside,
            Action = entered.Action
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.DungeonEntered, payload);
    }

}
