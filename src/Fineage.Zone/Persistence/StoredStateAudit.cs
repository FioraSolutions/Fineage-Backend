using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Protocol.Telemetry;

namespace Fineage.Zone.Persistence;

/// <summary>How serious one stored-state finding is.</summary>
public enum StoredStateSeverity
{
    /// <summary>Expected after an upgrade and repaired by the normal path (a pre-ledger bag aligned on its first move).</summary>
    Notice,

    /// <summary>Saved state the world cannot load as written, or a ledger projection that disagrees with the store.</summary>
    Error
}

/// <summary>
/// One finding of the stored-state audit (#984): who owns the state, which container, which id, what is wrong.
/// <see cref="Code"/> is stable for log search and metrics.
/// </summary>
public sealed record StoredStateIssue(StoredStateSeverity Severity, string Code, string Owner, string Container, int Id, string Detail)
{
    public const string MissingReference = "missing-reference";
    public const string LedgerDivergence = "ledger-divergence";
    public const string LedgerOrphan = "ledger-orphan";
    public const string LedgerInvariant = "ledger-invariant";
    public const string Unjournaled = "unjournaled";

    /// <summary>#996: a saved world subsystem disagrees with the ledger moves that changed it.</summary>
    public const string WorldStateLedger = "world-state-ledger";

    /// <summary>#996: a saved world subsystem breaks its own rules (duplicate serial, negative points, unreadable).</summary>
    public const string WorldStateInvalid = "world-state-invalid";

    /// <summary>#996: a saved world subsystem names a character the store does not have.</summary>
    public const string WorldStateOrphan = "world-state-orphan";

    public override string ToString() => $"{Code} {Owner} {Container} id={Id}: {Detail}";
}

/// <summary>Thrown when saved state cannot be loaded as written. The rows stay in the store untouched.</summary>
public sealed class StoredStateException : InvalidOperationException
{
    public StoredStateException(string subject, IReadOnlyList<StoredStateIssue> issues)
        : base(Format(subject, issues))
    {
        Issues = issues;
    }

    public IReadOnlyList<StoredStateIssue> Issues { get; }

    static string Format(string subject, IReadOnlyList<StoredStateIssue> issues) =>
        $"{subject}: {issues.Count} stored-state issue(s), rows kept in the store unchanged: "
        + string.Join("; ", issues.Take(20).Select(issue => issue.ToString()))
        + (issues.Count > 20 ? $"; … {issues.Count - 20} more" : "");
}

/// <summary>Everything the startup audit found, split by severity.</summary>
public sealed record StoredStateAuditReport(int Characters, int Pets, int Clans, IReadOnlyList<StoredStateIssue> Issues)
{
    public IReadOnlyList<StoredStateIssue> Errors => Issues.Where(issue => issue.Severity == StoredStateSeverity.Error).ToList();

    public bool Clean => Issues.Count == 0;
}

/// <summary>
/// The one check between saved state and the running world (#984):
/// <list type="bullet">
/// <item>every id a saved character, pet or clan warehouse references must exist in the loaded data catalogs, or the
/// character is refused at enter with the names (the rows are never dropped: a later save would delete them);</item>
/// <item>at startup, after the journal replay, every character / pet / clan container the ledger projects must
/// match the rows in the store, and every ledger account must belong to a stored owner.</item>
/// </list>
/// Policy is <c>FINEAGE_STATE_AUDIT</c>: <c>log</c> (default) writes every finding and the metric;
/// <c>block</c> refuses to start while any <see cref="StoredStateSeverity.Error"/> finding exists.
/// </summary>
public static class StoredStateAudit
{
    public const string PolicyVariable = "FINEAGE_STATE_AUDIT";

    /// <summary>Missing data references in one saved character (inventory, warehouse, freight, skills, effects, recipes, henna).</summary>
    public static IReadOnlyList<StoredStateIssue> References(CharacterRecord character, GameData game, HennaCatalog henna)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(henna);
        string owner = CharacterOwner(character);
        var issues = new List<StoredStateIssue>();
        CheckItems(issues, owner, "inventory", character.Items, game.Items);
        CheckItems(issues, owner, "warehouse", character.Warehouse, game.Items);
        CheckItems(issues, owner, "freight", character.Freight, game.Items);
        foreach (var skill in character.LearnedSkills ?? [])
        {
            if (skill != null && skill.IsValid && game.Skills.Find(skill.SkillId) == null)
                issues.Add(Missing(owner, "learned-skill", skill.SkillId, $"level {skill.Level}: no skill in data/skills.json"));
        }

        foreach (var effect in character.Effects ?? [])
        {
            if (effect != null && effect.IsValid && game.Skills.Find(effect.SkillId) == null)
                issues.Add(Missing(owner, "effect", effect.SkillId, $"level {effect.Level}: no skill in data/skills.json"));
        }

        foreach (int recipe in character.KnownRecipes ?? [])
        {
            if (recipe > 0 && !game.Recipes.TryGet(recipe, out _))
                issues.Add(Missing(owner, "recipe", recipe, "no recipe in data/recipes.json"));
        }

        foreach (int dye in new[] { character.HennaDye1, character.HennaDye2, character.HennaDye3 })
        {
            if (dye > 0 && !henna.TryGet(dye, out _))
                issues.Add(Missing(owner, "henna", dye, "no dye in data/henna.json"));
        }

        return issues;
    }

    /// <summary>Saved quest rows whose quest is not in the catalog. The journal keeps them inactive and writes them back.</summary>
    public static IReadOnlyList<StoredStateIssue> QuestReferences(CharacterRecord character, GameData game)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(game);
        var issues = new List<StoredStateIssue>();
        foreach (var quest in character.Quests ?? [])
        {
            if (quest != null && quest.QuestId > 0 && game.Quests.Find(quest.QuestId) == null)
            {
                issues.Add(new StoredStateIssue(StoredStateSeverity.Notice, StoredStateIssue.MissingReference,
                    CharacterOwner(character), "quest", quest.QuestId,
                    $"state {quest.State}: no quest in data/quests; the row is kept inactive and saved back unchanged"));
            }
        }

        return issues;
    }

    /// <summary>Missing item templates in one saved pet bag.</summary>
    public static IReadOnlyList<StoredStateIssue> References(StoredPet pet, ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(pet);
        ArgumentNullException.ThrowIfNull(items);
        var issues = new List<StoredStateIssue>();
        CheckItems(issues, PetOwner(pet), "pet-inventory", pet.Items, items);
        return issues;
    }

    /// <summary>Throws <see cref="StoredStateException"/> naming every missing reference of <paramref name="character"/>.</summary>
    public static void RequireLoadable(CharacterRecord? character, GameData game, HennaCatalog henna)
    {
        if (character == null)
            return;
        var issues = References(character, game, henna);
        if (issues.Count > 0)
        {
            Record(issues);
            throw new StoredStateException($"{CharacterOwner(character)} cannot be loaded", issues);
        }
    }

    /// <summary>Throws <see cref="StoredStateException"/> naming every missing item of <paramref name="pet"/>'s bag.</summary>
    public static void RequireLoadable(StoredPet? pet, ItemCatalog items)
    {
        if (pet == null)
            return;
        var issues = References(pet, items);
        if (issues.Count > 0)
        {
            Record(issues);
            throw new StoredStateException($"{PetOwner(pet)} cannot be loaded", issues);
        }
    }

    /// <summary>
    /// Compares the replayed ledger with the store, container by container. Bags are compared by template rule
    /// (stackables by count, the rest by instance id); warehouses, freight and clan warehouses by count.
    /// </summary>
    public static IReadOnlyList<StoredStateIssue> Ledger(
        EconomyLedger ledger,
        IReadOnlyList<CharacterRecord> characters,
        IReadOnlyList<StoredPet> pets,
        IReadOnlyList<ClanRecord> clans,
        ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentNullException.ThrowIfNull(pets);
        ArgumentNullException.ThrowIfNull(clans);
        ArgumentNullException.ThrowIfNull(items);
        var issues = new List<StoredStateIssue>();
        var expected = new Dictionary<LedgerAccount, string>();
        foreach (var character in characters)
        {
            string owner = CharacterOwner(character);
            Compare(issues, expected, ledger, LedgerAccount.Inventory(character.Id), owner, "inventory",
                BagHoldings(character.Items ?? [], items));
            Compare(issues, expected, ledger, LedgerAccount.Warehouse(character.Id), owner, "warehouse",
                CountHoldings(character.Warehouse ?? []));
            Compare(issues, expected, ledger, LedgerAccount.Freight(character.Id), owner, "freight",
                CountHoldings(character.Freight ?? []));
        }

        foreach (var pet in pets)
        {
            Compare(issues, expected, ledger, LedgerAccount.Pet(pet.ControlObjectId), PetOwner(pet), "pet-inventory",
                BagHoldings(pet.Items, items));
        }

        foreach (var clan in clans)
        {
            var holdings = new LedgerHoldings();
            foreach (var row in clan.Details?.Warehouse ?? [])
            {
                if (row.Count > 0)
                    holdings.Add(row.ItemId, row.Count);
            }

            Compare(issues, expected, ledger, LedgerAccount.ClanWarehouse(clan.Id), $"clan {clan.Id} '{clan.Name}'",
                "clan-warehouse", holdings);
        }

        foreach (var (account, holdings) in ledger.NonEmptyAccounts())
        {
            if (account.IsSystem || expected.ContainsKey(account) || !IsOwnedContainer(account))
                continue;
            issues.Add(new StoredStateIssue(StoredStateSeverity.Error, StoredStateIssue.LedgerOrphan, account.ToString(),
                account.Container.ToString().ToLowerInvariant(), account.OwnerId,
                $"the ledger projects {holdings} but the store has no such owner"));
        }

        foreach (var violation in EconomyInvariantChecker.Check(ledger))
        {
            issues.Add(new StoredStateIssue(StoredStateSeverity.Error, StoredStateIssue.LedgerInvariant, "ledger",
                violation.Code, 0, violation.Message));
        }

        return issues;
    }

    /// <summary>The whole startup audit over one store and the replayed ledger.</summary>
    public static StoredStateAuditReport Run(IAccountStore store, EconomyLedger ledger, GameData game, HennaCatalog henna)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(henna);
        var characters = store.ListAllCharacters();
        var pets = store.ListAllPets();
        var clans = store.ListClans();
        var issues = new List<StoredStateIssue>();
        foreach (var character in characters)
        {
            issues.AddRange(References(character, game, henna));
            issues.AddRange(QuestReferences(character, game));
        }

        issues.AddRange(WorldStateAudit.Check(store.LoadWorldState(), ledger, characters.Select(c => c.Id).ToHashSet()));
        foreach (var pet in pets)
            issues.AddRange(References(pet, game.Items));
        foreach (var clan in clans)
        {
            foreach (var row in clan.Details?.Warehouse ?? [])
            {
                if (row.ItemId > 0 && !game.Items.TryGet(row.ItemId, out _))
                {
                    issues.Add(Missing($"clan {clan.Id} '{clan.Name}'", "clan-warehouse", row.ItemId,
                        $"objectId {row.ObjectId} count {row.Count}: no item in data/items.json"));
                }
            }
        }

        issues.AddRange(Ledger(ledger, characters, pets, clans, game.Items));
        return new StoredStateAuditReport(characters.Count, pets.Count, clans.Count, issues);
    }

    /// <summary>Reads <see cref="PolicyVariable"/>: true when the audit must refuse to start on an error.</summary>
    public static bool BlockOnError(Func<string, string?>? environment = null)
    {
        string? raw = (environment ?? Environment.GetEnvironmentVariable)(PolicyVariable)?.Trim();
        if (string.IsNullOrEmpty(raw) || raw.Equals("log", StringComparison.OrdinalIgnoreCase))
            return false;
        if (raw.Equals("block", StringComparison.OrdinalIgnoreCase))
            return true;
        throw new InvalidOperationException($"{PolicyVariable}={raw} is not a policy: use 'log' (default) or 'block'");
    }

    /// <summary>
    /// Logs and counts every finding; with <paramref name="block"/> throws <see cref="StoredStateException"/> when
    /// any error remains so the host exits before it accepts players.
    /// </summary>
    public static void Apply(StoredStateAuditReport report, bool block)
    {
        ArgumentNullException.ThrowIfNull(report);
        Record(report.Issues);
        foreach (var issue in report.Issues)
        {
            string line = $"state audit {issue}";
            if (issue.Severity == StoredStateSeverity.Error)
                ZoneLog.Error(line);
            else
                ZoneLog.Warn(line);
        }

        var errors = report.Errors;
        ZoneLog.Info(
            $"state audit characters={report.Characters} pets={report.Pets} clans={report.Clans} errors={errors.Count} notices={report.Issues.Count - errors.Count} policy={(block ? "block" : "log")}");
        if (block && errors.Count > 0)
            throw new StoredStateException($"startup state audit ({PolicyVariable}=block)", errors);
    }

    static void Record(IReadOnlyList<StoredStateIssue> issues)
    {
        foreach (var issue in issues)
        {
            FineageTelemetry.StoredStateIssues.Add(1,
                new KeyValuePair<string, object?>("code", issue.Code),
                new KeyValuePair<string, object?>("container", issue.Container),
                new KeyValuePair<string, object?>("severity", issue.Severity == StoredStateSeverity.Error ? "error" : "notice"));
        }
    }

    static void Compare(
        List<StoredStateIssue> issues,
        Dictionary<LedgerAccount, string> expected,
        EconomyLedger ledger,
        LedgerAccount account,
        string owner,
        string container,
        LedgerHoldings stored)
    {
        expected[account] = owner;
        var projected = ledger.HoldingsOf(account);
        if (projected.SameAs(stored))
            return;
        if (projected.IsEmpty)
        {
            issues.Add(new StoredStateIssue(StoredStateSeverity.Notice, StoredStateIssue.Unjournaled, owner, container, 0,
                $"store holds {stored} with no journal history; the first ledger move aligns it with an Untracked adjustment"));
            return;
        }

        issues.Add(new StoredStateIssue(StoredStateSeverity.Error, StoredStateIssue.LedgerDivergence, owner, container, 0,
            $"ledger projects {projected}, store holds {stored}"));
    }

    /// <summary>A character / pet bag as the ledger sees it: stackables by count, everything else by instance id.</summary>
    public static LedgerHoldings BagHoldings(IReadOnlyList<StoredItem> rows, ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(items);
        var holdings = new LedgerHoldings();
        foreach (var row in rows)
        {
            if (row.Count <= 0)
                continue;
            bool stackable = items.TryGet(row.ItemId, out var template)
                ? template.Stackable
                : row.Count > 1 || row.ObjectId <= 0;
            if (stackable)
                holdings.Add(row.ItemId, row.Count);
            else if (row.Count == 1)
                holdings.AddPiece(row.ItemId, row.ObjectId);
            else
                throw new InvalidOperationException($"stored non-stackable item {row.ItemId} objectId={row.ObjectId} has count {row.Count}");
        }

        return holdings;
    }

    static LedgerHoldings CountHoldings(IReadOnlyList<StoredItem> rows)
    {
        var holdings = new LedgerHoldings();
        foreach (var row in rows)
        {
            if (row.Count > 0)
                holdings.Add(row.ItemId, row.Count);
        }

        return holdings;
    }

    static bool IsOwnedContainer(LedgerAccount account) => account switch
    {
        { Owner: LedgerOwnerKind.Character, Container: LedgerContainer.Inventory or LedgerContainer.Warehouse or LedgerContainer.Freight } => true,
        { Owner: LedgerOwnerKind.Pet } => true,
        { Owner: LedgerOwnerKind.Clan, Container: LedgerContainer.Warehouse } => true,
        _ => false
    };

    static void CheckItems(List<StoredStateIssue> issues, string owner, string container, IReadOnlyList<StoredItem>? rows, ItemCatalog items)
    {
        foreach (var row in rows ?? [])
        {
            if (row != null && row.ItemId > 0 && !items.TryGet(row.ItemId, out _))
            {
                issues.Add(Missing(owner, container, row.ItemId,
                    $"objectId {row.ObjectId} count {row.Count}: no item in data/items.json"));
            }
        }
    }

    static StoredStateIssue Missing(string owner, string container, int id, string detail) =>
        new(StoredStateSeverity.Error, StoredStateIssue.MissingReference, owner, container, id, detail);

    static string CharacterOwner(CharacterRecord character) =>
        $"character {character.Id} '{character.Name}' (account {character.Account})";

    static string PetOwner(StoredPet pet) =>
        $"pet {pet.ControlObjectId} '{pet.Name}' (owner character {pet.OwnerCharacterId})";
}
