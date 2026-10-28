using Fineage.Protocol;
using Fineage.Zone.SevenSigns;

namespace Fineage.Zone.Dungeons;

public enum DungeonAccessCode
{
    Allowed = 0,
    UnknownDungeon = 1,
    NotRegistered = 2,
    DawnOnly = 3,
    DuskOnly = 4,
    FeeRequired = 5,
    Dead = 6
}

/// <summary>Seven Signs facts the gate reads. Cabal membership stays on the visitor.</summary>
public readonly record struct DungeonAccessState(
    SevenSignsPeriod Period,
    SevenSignsCabal WinningCabal,
    SevenSignsCabal AvariceOwner,
    SevenSignsCabal GnosisOwner);

/// <summary>
/// Visitor at a gate. <see cref="CountItem"/> is how many of an item id they hold.
/// A null counter means they hold nothing.
/// </summary>
public readonly record struct DungeonCharacter(SevenSignsCabal Cabal, bool Dead, Func<int, int>? CountItem = null)
{
    public int Count(int itemId) => itemId <= 0 ? 0 : CountItem?.Invoke(itemId) ?? 0;
}

/// <summary>
/// <c>AltOpenCatacumbs</c> / <c>AltOpenNecropolis</c>. Both default to closed, matching Mobius <c>Feature.ini</c>.
/// </summary>
public sealed class DungeonAccessOptions
{
    public bool OpenCatacombs { get; init; }

    public bool OpenNecropolis { get; init; }

    public static DungeonAccessOptions FromEnvironment() => new()
    {
        OpenCatacombs = ReadBool("FINEAGE_SSQ_OPEN_CATACOMBS"),
        OpenNecropolis = ReadBool("FINEAGE_SSQ_OPEN_NECROPOLIS")
    };

    static bool ReadBool(string name)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text))
            return false;
        return text.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) ||
            text.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) ||
            text.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Ziggurat entrance rules from Mobius CT 0 Interlude <c>DungeonGatekeeper</c>.
/// Necropolises consult the Seal of Avarice. Catacombs consult the Seal of Gnosis.
/// </summary>
public sealed class DungeonAccess
{
    readonly DungeonCatalog _catalog;
    readonly DungeonAccessOptions _options;

    public DungeonAccess(DungeonCatalog catalog, DungeonAccessOptions? options = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _options = options ?? new DungeonAccessOptions();
        State = new DungeonAccessState(SevenSignsPeriod.Recruiting, SevenSignsCabal.None, SevenSignsCabal.None, SevenSignsCabal.None);
    }

    public DungeonCatalog Catalog => _catalog;

    public DungeonAccessOptions Options => _options;

    public DungeonAccessState State { get; private set; }

    public void Use(DungeonAccessState state) => State = state;

    public void Observe(SevenSignsEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var effects = engine.Effects();
        Use(new DungeonAccessState(engine.Period, engine.WinningCabal, effects.AvariceOwner, effects.GnosisOwner));
    }

    /// <summary>
    /// Whether <paramref name="character"/> may pass the entrance of <paramref name="dungeonId"/>.
    /// Dead and fee are checked only after the cabal gate would let them through.
    /// </summary>
    public DungeonAccessCode CanEnter(DungeonCharacter character, int dungeonId)
    {
        if (!_catalog.TryGet(dungeonId, out var site))
            return DungeonAccessCode.UnknownDungeon;

        if (!GateOpen(site))
        {
            var denied = CabalGate(character.Cabal, site);
            if (denied != DungeonAccessCode.Allowed)
                return denied;
        }

        if (character.Dead)
            return DungeonAccessCode.Dead;
        if (site.FeeCount > 0 && character.Count(site.FeeItemId) < site.FeeCount)
            return DungeonAccessCode.FeeRequired;
        return DungeonAccessCode.Allowed;
    }

    /// <summary>
    /// Still inside after a period change. Fee and death do not evict someone who already passed the gate.
    /// </summary>
    public bool MayRemain(SevenSignsCabal cabal, int dungeonId) =>
        CanEnter(new DungeonCharacter(cabal, false, _ => int.MaxValue), dungeonId) == DungeonAccessCode.Allowed;

    public IReadOnlyList<DungeonNotice> RefusalLines(DungeonAccessCode code, int dungeonId)
    {
        switch (code)
        {
            case DungeonAccessCode.DawnOnly:
                return
                [
                    new DungeonNotice(DungeonProtocol.DawnOnlyMessage, DungeonProtocol.DawnOnlyText),
                    new DungeonNotice(DungeonProtocol.GateRefusedMessage, DungeonProtocol.GateRefusedText)
                ];
            case DungeonAccessCode.DuskOnly:
                return
                [
                    new DungeonNotice(DungeonProtocol.DuskOnlyMessage, DungeonProtocol.DuskOnlyText),
                    new DungeonNotice(DungeonProtocol.GateRefusedMessage, DungeonProtocol.GateRefusedText)
                ];
            case DungeonAccessCode.NotRegistered:
                return [new DungeonNotice(DungeonProtocol.GateRefusedMessage, DungeonProtocol.GateRefusedText)];
            case DungeonAccessCode.FeeRequired:
                return FeeLine(dungeonId);
            default:
                return [];
        }
    }

    /// <summary>
    /// Kick copy for the period that just began.
    /// Results and seal validation share the validation sentence. Recruitment and competition use the unsigned sentence.
    /// </summary>
    public static DungeonNotice KickNotice(SevenSignsPeriod period) =>
        period is SevenSignsPeriod.Results or SevenSignsPeriod.SealValidation
            ? new DungeonNotice(DungeonProtocol.KickedValidationMessage, DungeonProtocol.KickedValidationText)
            : new DungeonNotice(DungeonProtocol.KickedUnsignedMessage, DungeonProtocol.KickedUnsignedText);

    public static string ToWire(DungeonAccessCode code) => code switch
    {
        DungeonAccessCode.UnknownDungeon => DungeonProtocol.Unknown,
        DungeonAccessCode.NotRegistered => DungeonProtocol.Cabal,
        DungeonAccessCode.DawnOnly => DungeonProtocol.Dawn,
        DungeonAccessCode.DuskOnly => DungeonProtocol.Dusk,
        DungeonAccessCode.FeeRequired => DungeonProtocol.Fee,
        DungeonAccessCode.Dead => InventoryProtocol.Dead,
        _ => "bad_request"
    };

    bool GateOpen(DungeonSite site) =>
        site.Kind == DungeonKind.Catacomb ? _options.OpenCatacombs : _options.OpenNecropolis;

    DungeonAccessCode CabalGate(SevenSignsCabal cabal, DungeonSite site)
    {
        if (State.Period != SevenSignsPeriod.SealValidation)
            return cabal == SevenSignsCabal.None ? DungeonAccessCode.NotRegistered : DungeonAccessCode.Allowed;

        var sealOwner = site.Kind == DungeonKind.Necropolis ? State.AvariceOwner : State.GnosisOwner;
        var winner = State.WinningCabal;
        if (winner == SevenSignsCabal.Dawn && (cabal != SevenSignsCabal.Dawn || sealOwner != SevenSignsCabal.Dawn))
            return DungeonAccessCode.DawnOnly;
        if (winner == SevenSignsCabal.Dusk && (cabal != SevenSignsCabal.Dusk || sealOwner != SevenSignsCabal.Dusk))
            return DungeonAccessCode.DuskOnly;
        if (winner == SevenSignsCabal.None && cabal != SevenSignsCabal.None)
            return DungeonAccessCode.Allowed;
        if (cabal == SevenSignsCabal.None)
            return DungeonAccessCode.NotRegistered;
        return DungeonAccessCode.Allowed;
    }

    IReadOnlyList<DungeonNotice> FeeLine(int dungeonId)
    {
        if (!_catalog.TryGet(dungeonId, out var site))
            return [];
        if (site.FeeItemId == DungeonProtocol.AdenaItemId)
            return [new DungeonNotice(DungeonProtocol.NotEnoughAdenaMessage, DungeonProtocol.NotEnoughAdenaText)];
        if (site.FeeItemId == DungeonProtocol.AncientAdenaItemId)
            return [new DungeonNotice(DungeonProtocol.NotEnoughItemsMessage, DungeonProtocol.NotEnoughAncientAdenaText)];
        return [new DungeonNotice(DungeonProtocol.NotEnoughItemsMessage, DungeonProtocol.NotEnoughItemsText)];
    }
}
