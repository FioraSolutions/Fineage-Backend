using Fineage.Accounts;
using Fineage.Game.Economy;
using Fineage.Game.Punishment;
using Fineage.Protocol;
using Fineage.Zone.Lottery;
using Fineage.Zone.MonsterRace;
using Fineage.Zone.PcCafe;

namespace Fineage.Zone.Persistence;

/// <summary>
/// The stored-state audit for the world subsystems (#996), run with <see cref="StoredStateAudit"/>: every saved
/// document must parse, keep its own rules, name only stored characters, and agree with the ledger moves that change
/// it. The ledger side uses <see cref="EconomyLedger.TallyOf"/> over <see cref="TalliedKinds"/>:
/// <list type="bullet">
/// <item>lottery — the tickets saved for the running lottery N are exactly the <see cref="LedgerTxKind.LotteryBuy"/>
/// moves naming N (a ticket is only forgotten after its lottery is drawn);</item>
/// <item>monster race — the bets saved for the running race N add up to the adena the
/// <see cref="LedgerTxKind.MonsterRaceBet"/> moves naming N paid to the race track (bets clear when the race ends,
/// before N moves on).</item>
/// </list>
/// Before #996 these documents were files written beside the ledger transaction, so either side could land alone;
/// these checks are what that would look like.
/// </summary>
public static class WorldStateAudit
{
    public static IReadOnlyList<LedgerTxKind> TalliedKinds { get; } =
    [
        LedgerTxKind.LotteryBuy,
        LedgerTxKind.LotteryPrize,
        LedgerTxKind.MonsterRaceBet,
        LedgerTxKind.MonsterRacePayout,
        LedgerTxKind.FishingChampionshipPrize,
        LedgerTxKind.PcCafeCoupon,
    ];

    public static IReadOnlyList<StoredStateIssue> Check(
        IReadOnlyList<WorldStateDocument> documents,
        EconomyLedger ledger,
        IReadOnlySet<int> characterIds)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(characterIds);
        var issues = new List<StoredStateIssue>();
        var bySystem = documents.ToDictionary(d => d.System, StringComparer.Ordinal);
        foreach (var document in documents)
        {
            if (!WorldStateSystems.All.Contains(document.System, StringComparer.Ordinal))
                issues.Add(Invalid(document.System, 0, $"revision {document.Revision}: no subsystem of this build reads it"));
        }

        Lottery(issues, bySystem, ledger);
        MonsterRace(issues, bySystem, ledger);
        RaidBossPoints(issues, bySystem, characterIds);
        Characters(issues, bySystem, WorldStateCatalog.SevenSigns, characterIds, s => s.Players.Select(p => p.CharacterId));
        Characters(issues, bySystem, WorldStateCatalog.Olympiad, characterIds, s => s.Nobles.Select(n => n.CharacterId).Concat(s.Granted));
        PcCafe(issues, bySystem, characterIds);
        Punishments(issues, bySystem);
        Parsed(issues, bySystem, WorldStateCatalog.BossSchedule);
        Parsed(issues, bySystem, WorldStateCatalog.FishingChampionship);
        return issues;
    }

    static void Lottery(List<StoredStateIssue> issues, Dictionary<string, WorldStateDocument> bySystem, EconomyLedger ledger)
    {
        if (Parsed(issues, bySystem, WorldStateCatalog.Lottery) is not { } lottery)
            return;
        foreach (var serial in lottery.Tickets.GroupBy(t => t.Serial).Where(g => g.Count() > 1))
            issues.Add(Invalid(WorldStateSystems.Lottery, serial.Key, $"serial {serial.Key} is saved {serial.Count()} times"));
        foreach (var ahead in lottery.Tickets.Where(t => t.Number > lottery.Number))
            issues.Add(Invalid(WorldStateSystems.Lottery, ahead.Serial, $"ticket serial {ahead.Serial} is for lottery {ahead.Number}, after the running lottery {lottery.Number}"));
        int saved = lottery.Tickets.Count(t => t.Number == lottery.Number);
        var bought = ledger.TallyOf(LedgerTxKind.LotteryBuy, lottery.Number);
        if (saved != bought.Moves)
        {
            issues.Add(new StoredStateIssue(StoredStateSeverity.Error, StoredStateIssue.WorldStateLedger,
                $"world-state {WorldStateSystems.Lottery}", "tickets", lottery.Number,
                $"lottery {lottery.Number} has {saved} saved ticket(s) but the ledger has {bought.Moves} LotteryBuy move(s) for it"));
        }
    }

    static void MonsterRace(List<StoredStateIssue> issues, Dictionary<string, WorldStateDocument> bySystem, EconomyLedger ledger)
    {
        if (Parsed(issues, bySystem, WorldStateCatalog.MonsterRace) is not { } race)
            return;
        foreach (var (lane, amount) in race.BetsPerLane.Where(pair => pair.Value < 0 || pair.Key is < 1 or > MonsterRaceProtocol.RunnerCount))
            issues.Add(Invalid(WorldStateSystems.MonsterRace, lane, $"race {race.RaceNumber} lane {lane} holds a bet of {amount}"));
        long saved = race.BetsPerLane.Values.Sum();
        long paid = ledger.TallyOf(LedgerTxKind.MonsterRaceBet, race.RaceNumber).SystemAmountOf(MonsterRaceProtocol.AdenaItemId);
        if (saved != paid)
        {
            issues.Add(new StoredStateIssue(StoredStateSeverity.Error, StoredStateIssue.WorldStateLedger,
                $"world-state {WorldStateSystems.MonsterRace}", "bets", race.RaceNumber,
                $"race {race.RaceNumber} has {saved} adena of saved bets but the ledger's MonsterRaceBet moves paid {paid} to the track"));
        }
    }

    static void RaidBossPoints(List<StoredStateIssue> issues, Dictionary<string, WorldStateDocument> bySystem, IReadOnlySet<int> characterIds)
    {
        if (Parsed(issues, bySystem, WorldStateCatalog.RaidBossPoints) is not { } points)
            return;
        foreach (var row in points.Characters)
        {
            foreach (var (boss, value) in row.ByBoss.Where(pair => pair.Value < 0))
                issues.Add(Invalid(WorldStateSystems.RaidBossPoints, row.CharacterId, $"character {row.CharacterId} has {value} points for boss {boss}"));
        }

        Orphans(issues, WorldStateSystems.RaidBossPoints, points.Characters.Select(r => r.CharacterId), characterIds);
    }

    static void PcCafe(List<StoredStateIssue> issues, Dictionary<string, WorldStateDocument> bySystem, IReadOnlySet<int> characterIds)
    {
        if (Parsed(issues, bySystem, WorldStateCatalog.PcCafe) is not { } cafe)
            return;
        foreach (var row in cafe.Characters.Where(r => r.Points is < 0 or > PcCafeProtocol.MaxPoints))
            issues.Add(Invalid(WorldStateSystems.PcCafe, row.CharacterId, $"character {row.CharacterId} holds {row.Points} points"));
        Orphans(issues, WorldStateSystems.PcCafe, cafe.Characters.Select(r => r.CharacterId), characterIds);
    }

    static void Punishments(List<StoredStateIssue> issues, Dictionary<string, WorldStateDocument> bySystem)
    {
        if (Parsed(issues, bySystem, WorldStateCatalog.Punishment) is not { } punishments)
            return;
        foreach (var row in punishments.Rows.Where(r => string.IsNullOrWhiteSpace(r.Key)
                                                        || (r.Affect == PunishmentAffect.Character && !int.TryParse(r.Key, out _))))
            issues.Add(Invalid(WorldStateSystems.Punishment, 0, $"{row.Affect} {row.Type} row has key '{row.Key}'"));
    }

    static void Characters<T>(
        List<StoredStateIssue> issues,
        Dictionary<string, WorldStateDocument> bySystem,
        WorldStateKind<T> kind,
        IReadOnlySet<int> characterIds,
        Func<T, IEnumerable<int>> ids)
        where T : class, Fineage.Game.Persistence.IVersionedStateSnapshot
    {
        if (Parsed(issues, bySystem, kind) is { } data)
            Orphans(issues, kind.System, ids(data), characterIds);
    }

    static void Orphans(List<StoredStateIssue> issues, string system, IEnumerable<int> ids, IReadOnlySet<int> characterIds)
    {
        foreach (int id in ids.Distinct().Where(id => !characterIds.Contains(id)).Order())
        {
            issues.Add(new StoredStateIssue(StoredStateSeverity.Notice, StoredStateIssue.WorldStateOrphan,
                $"world-state {system}", "character", id, "names a character the store does not have (deleted?)"));
        }
    }

    static T? Parsed<T>(List<StoredStateIssue> issues, Dictionary<string, WorldStateDocument> bySystem, WorldStateKind<T> kind)
        where T : class, Fineage.Game.Persistence.IVersionedStateSnapshot
    {
        if (!bySystem.TryGetValue(kind.System, out var document))
            return null;
        try
        {
            return kind.Parse(document);
        }
        catch (WorldStateException ex)
        {
            issues.Add(Invalid(kind.System, 0, ex.Message));
            return null;
        }
    }

    static StoredStateIssue Invalid(string system, int id, string detail) =>
        new(StoredStateSeverity.Error, StoredStateIssue.WorldStateInvalid, $"world-state {system}", "document", id, detail);
}
