using Fineage.Accounts;
using Fineage.Game.Economy;
using Fineage.Zone.Persistence;

namespace Fineage.Zone.World;

/// <summary>One finding of <see cref="ZoneWorld.ReconcileEconomy"/>: what is wrong, in which container, for which id.</summary>
public sealed record EconomyReconcileViolation(string Code, string Container, int OwnerId, string Detail)
{
    public const string NotQuiescent = "not-quiescent";
    public const string LedgerInvariant = "ledger-invariant";
    public const string StoreDivergence = "store-divergence";
    public const string StoreOrphan = "store-orphan";
    public const string LiveDivergence = "live-divergence";
    public const string GroundWithoutEntity = "ground-without-entity";
    public const string GroundUnjournaled = "ground-unjournaled";
    public const string PieceInTwoPlaces = "piece-in-two-places";
    public const string StoredPieceInTwoPlaces = "stored-piece-in-two-places";

    public override string ToString() => $"{Code} {Container}:{OwnerId}: {Detail}";
}

/// <summary>
/// Ledger ↔ store ↔ live world, at one instant. <see cref="Violations"/> are value that is in two places, missing from
/// one, or different between them; <see cref="Notices"/> are store rows from before the ledger existed (#984
/// unjournaled bags), which the first ledger move aligns with an explicit Untracked adjustment.
/// </summary>
public sealed class EconomyReconcileReport
{
    public required string Phase { get; init; }
    public int LedgerAccounts { get; set; }
    public int StoredOwners { get; set; }
    public int LiveContainers { get; set; }
    public int GroundStacks { get; set; }
    public List<EconomyReconcileViolation> Violations { get; } = [];
    public List<string> Notices { get; } = [];
    public bool Ok => Violations.Count == 0;

    public string Summary =>
        $"economy reconcile phase={Phase} ledger-accounts={LedgerAccounts} stored-owners={StoredOwners} " +
        $"live-containers={LiveContainers} ground-stacks={GroundStacks} violations={Violations.Count} notices={Notices.Count}";

    /// <summary>Fails loudly: every violation with its container and id, never a silent correction.</summary>
    public void ThrowIfViolations()
    {
        if (!Ok)
            throw new EconomyReconcileException(this);
    }
}

public sealed class EconomyReconcileException(EconomyReconcileReport report) : InvalidOperationException(
    report.Summary + Environment.NewLine + string.Join(Environment.NewLine, report.Violations.Take(100).Select(v => "  " + v))
    + (report.Violations.Count > 100 ? $"{Environment.NewLine}  … {report.Violations.Count - 100} more" : ""))
{
    public EconomyReconcileReport Report { get; } = report;
}

/// <summary>#955: reconciliation of the auditable economy ledger against the store and the live world.</summary>
public sealed partial class ZoneWorld
{
    /// <summary>
    /// Reconciles, under the world turn and with the economy quiescent (call <see cref="FlushEconomyForReconcile"/>
    /// first on a running world):
    /// <list type="bullet">
    /// <item>ledger-only invariants (sources − sinks, no negative owned balance);</item>
    /// <item>ledger vs store: every saved character inventory / warehouse / freight, pet inventory and clan warehouse,
    /// and no ledger account whose owner the store does not have;</item>
    /// <item>ledger vs live world: every online bag, loaded warehouse / freight, summoned pet, loaded clan warehouse and
    /// every ground stack, both ways (a ground account without a stack, a stack without a ground account);</item>
    /// <item>one place per piece: an item instance id held by two ledger accounts, or saved in two rows.</item>
    /// </list>
    /// </summary>
    public EconomyReconcileReport ReconcileEconomy(string phase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        var report = new EconomyReconcileReport { Phase = phase };
        using (_affinity.Step())
        {
            _economyPersist?.DrainCompletions(this);
            int pending = _economyPersist?.PendingCount ?? 0;
            bool dirty;
            using (_economyLock.Enter())
                dirty = HasDirtyEconomy();
            if (pending > 0 || dirty)
            {
                report.Violations.Add(new EconomyReconcileViolation(EconomyReconcileViolation.NotQuiescent, "economy", 0,
                    $"{pending} economy batch(es) in flight, dirty containers={dirty}: flush before reconciling"));
            }

            foreach (var violation in EconomyInvariantChecker.Check(_ledger))
                report.Violations.Add(new EconomyReconcileViolation(EconomyReconcileViolation.LedgerInvariant, violation.Code, 0, violation.Message));

            var accounts = _ledger.NonEmptyAccounts().ToList();
            report.LedgerAccounts = accounts.Count;
            ReconcilePieces(report, accounts);
            ReconcileStore(report);
            ReconcileLive(report, accounts);
        }

        return report;
    }

    /// <summary>Off-step: commits every dirty container and waits until no economy batch is in flight.</summary>
    public bool FlushEconomyForReconcile(TimeSpan budget)
    {
        if (!TryCommitDirtyEconomy(discardWithoutPublish: null))
            return false;
        if (_economyPersist == null)
            return true;
        using (_affinity.Step())
        {
            if (!_economyPersist.WaitForAllBatches(budget, this))
                return false;
            _economyPersist.DrainCompletions(this);
        }

        return true;
    }

    /// <summary>Reconcile and log; with <c>FINEAGE_STATE_AUDIT=block</c> a violation throws (startup refuses to run).</summary>
    public EconomyReconcileReport ReconcileEconomyAndLog(string phase, Func<string, string?>? environment = null)
    {
        // Boot posts Expire moves for the ground that did not survive; shutdown leaves the last logouts' journal rows.
        // Both have to be durable before the store can be compared with the ledger.
        if (!FlushEconomyForReconcile(TimeSpan.FromSeconds(30)))
            ZoneLog.Error($"economy reconcile phase={phase}: the economy flush before reconciling failed");
        var report = ReconcileEconomy(phase);
        foreach (var violation in report.Violations)
            ZoneLog.Error($"economy reconcile violation phase={phase} {violation}");
        if (report.Ok)
            ZoneLog.Info(report.Summary);
        else
            ZoneLog.Error(report.Summary);
        if (!report.Ok && StoredStateAudit.BlockOnError(environment))
            report.ThrowIfViolations();
        return report;
    }

    static void ReconcilePieces(EconomyReconcileReport report, List<(LedgerAccount Account, LedgerHoldings Holdings)> accounts)
    {
        var owner = new Dictionary<int, LedgerAccount>();
        foreach (var (account, holdings) in accounts)
        {
            if (account.IsSystem)
                continue;
            foreach (var (instanceId, itemId) in holdings.Instances)
            {
                if (owner.TryGetValue(instanceId, out var first))
                {
                    report.Violations.Add(new EconomyReconcileViolation(EconomyReconcileViolation.PieceInTwoPlaces,
                        account.Container.ToString().ToLowerInvariant(), account.OwnerId,
                        $"item {itemId} instance {instanceId} is held by {first} and by {account}"));
                }
                else
                {
                    owner[instanceId] = account;
                }
            }
        }
    }

    void ReconcileStore(EconomyReconcileReport report)
    {
        if (_accounts == null)
            return;
        var characters = _accounts.ListAllCharacters();
        var pets = _accounts.ListAllPets();
        var clans = _accounts.ListClans();
        report.StoredOwners = characters.Count + pets.Count + clans.Count;
        foreach (var issue in StoredStateAudit.Ledger(_ledger, characters, pets, clans, _game.Items))
        {
            if (issue.Code == StoredStateIssue.LedgerInvariant)
                continue; // already reported from the ledger itself
            if (issue.Severity == StoredStateSeverity.Notice)
            {
                report.Notices.Add(issue.ToString());
                continue;
            }

            string code = issue.Code == StoredStateIssue.LedgerOrphan
                ? EconomyReconcileViolation.StoreOrphan
                : EconomyReconcileViolation.StoreDivergence;
            report.Violations.Add(new EconomyReconcileViolation(code, issue.Container, OwnerIdOf(issue), $"{issue.Owner}: {issue.Detail}"));
        }

        // A saved piece is one row: the same instance id in two saved bags is an item in two places.
        var seen = new Dictionary<int, string>();
        void Rows(IReadOnlyList<StoredItem>? rows, string where)
        {
            foreach (var row in rows ?? [])
            {
                if (row.ObjectId <= 0 || row.Count != 1 || !_game.Items.TryGet(row.ItemId, out var template) || template.Stackable)
                    continue;
                if (seen.TryGetValue(row.ObjectId, out var first))
                {
                    report.Violations.Add(new EconomyReconcileViolation(EconomyReconcileViolation.StoredPieceInTwoPlaces, "store", row.ObjectId,
                        $"item {row.ItemId} instance {row.ObjectId} is saved in {first} and in {where}"));
                }
                else
                {
                    seen[row.ObjectId] = where;
                }
            }
        }

        foreach (var character in characters)
            Rows(character.Items, $"character {character.Id} inventory");
        foreach (var pet in pets)
            Rows(pet.Items, $"pet {pet.ControlObjectId} inventory");
    }

    static int OwnerIdOf(StoredStateIssue issue)
    {
        if (issue.Id != 0)
            return issue.Id;
        // Owners read "character 52 'Name' (account a)" / "clan 7 'Name'" / "pet 123 …".
        var parts = issue.Owner.Split(' ', 3);
        return parts.Length >= 2 && int.TryParse(parts[1], out int id) ? id : 0;
    }

    void ReconcileLive(EconomyReconcileReport report, List<(LedgerAccount Account, LedgerHoldings Holdings)> accounts)
    {
        void Compare(LedgerAccount account, LedgerHoldings live, string container)
        {
            report.LiveContainers++;
            var ledger = _ledger.HoldingsOf(account);
            if (ledger.SameAs(live))
                return;
            if (ledger.IsEmpty && account.Container != LedgerContainer.Ground)
            {
                report.Notices.Add($"{container}:{account.OwnerId} live {live} has no journal history yet");
                return;
            }

            report.Violations.Add(new EconomyReconcileViolation(EconomyReconcileViolation.LiveDivergence, container, account.OwnerId,
                $"ledger {ledger}, live world {live}"));
        }

        foreach (var player in Players())
        {
            if (player.Inventory != null)
                Compare(LedgerAccount.Inventory(player.ObjectId), HoldingsOf(player.Inventory), "inventory");
            if (player.Warehouse != null)
                Compare(LedgerAccount.Warehouse(player.ObjectId), HoldingsOf(player.Warehouse), "warehouse");
            if (player.Freight != null)
                Compare(LedgerAccount.Freight(player.ObjectId), HoldingsOf(player.Freight), "freight");
            if (player.SummonObjectId != 0 && _byId.TryGetValue(player.SummonObjectId, out var summon) && summon.Summon?.Pet is { } pet)
                Compare(LedgerAccount.Pet(pet.ControlObjectId), HoldingsOf(pet.Inventory), "pet-inventory");
        }

        foreach (var clan in _clan.All)
        {
            // Only a warehouse the world has loaded is live state; an unopened one is its store row (checked above).
            if (clan.Warehouse is { } warehouse)
                Compare(LedgerAccount.ClanWarehouse(clan.Id), HoldingsOf(warehouse), "clan-warehouse");
        }

        var stacks = _byId.Values.Where(e => e.Kind == ZoneEntityKind.GroundItem).ToList();
        report.GroundStacks = stacks.Count;
        foreach (var ground in stacks)
        {
            var account = LedgerAccount.Ground(ground.ObjectId);
            if (_ledger.HoldingsOf(account).IsEmpty)
            {
                report.Violations.Add(new EconomyReconcileViolation(EconomyReconcileViolation.GroundUnjournaled, "ground", ground.ObjectId,
                    $"stack of item {ground.GroundItemId} x{ground.GroundCount} is on the ground with no ledger account"));
                continue;
            }

            Compare(account, GroundHoldingsOf(ground), "ground");
        }

        foreach (var (account, holdings) in accounts)
        {
            if (account.Container != LedgerContainer.Ground)
                continue;
            if (!_byId.TryGetValue(account.OwnerId, out var entity) || entity.Kind != ZoneEntityKind.GroundItem)
            {
                report.Violations.Add(new EconomyReconcileViolation(EconomyReconcileViolation.GroundWithoutEntity, "ground", account.OwnerId,
                    $"the ledger holds {holdings} on the ground but no ground stack {account.OwnerId} is in the world"));
            }
        }
    }
}
