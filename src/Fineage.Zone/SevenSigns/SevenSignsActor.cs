using Fineage.Protocol;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.SevenSigns;

/// <summary>The period moved on: the system lines every live player gets with the new sky.</summary>
public sealed record SevenSignsPeriodChange(IReadOnlyList<SevenSignsNotice> Notices);

/// <summary>
/// Seven Signs as a global actor: the weekly period machine advances at the step's wall sample (the boundary is a
/// weekday and hour of the server time zone), never at a clock of its own. Joins, contributions and rewards are
/// requests on <see cref="Engine"/> from commands.
/// </summary>
public sealed class SevenSignsActor : GlobalActor<SevenSignsPeriodChange>
{
    public const string StageName = "seven-signs";

    readonly SevenSignsEngine _engine;

    public SevenSignsActor(SevenSignsEngine engine)
        : base(StageName, ActorTime.Calendar)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public SevenSignsEngine Engine
    {
        get
        {
            RequireActorThread();
            return _engine;
        }
    }

    protected override void OnStep(long nowUnixMs, List<SevenSignsPeriodChange> effects)
    {
        var notices = new List<SevenSignsNotice>();
        if (_engine.TryAdvance(DateTimeOffset.FromUnixTimeMilliseconds(nowUnixMs), notices))
            effects.Add(new SevenSignsPeriodChange(notices));
    }
}

/// <summary>Where a Mammon NPC stands now; <paramref name="At"/> null takes it out of the world.</summary>
public readonly record struct MammonPlacement(int NpcId, (int X, int Y, int Z)? At);

/// <summary>
/// The Mammon NPCs after Mobius <c>SevenSigns.spawnSevenSignsNPC</c> and <c>AutoSpawnHandler</c>, as a global actor on
/// the calendar: during competition results and seal validation the Merchant of Mammon walks the Necropolises while the
/// winning cabal holds Avarice, and the Blacksmith of Mammon walks the Catacombs while it holds Gnosis. Each appears
/// 30 s after its seal goes live and moves to a spot of its list, drawn on the actor's stream, every hour; outside that
/// window both are gone. It reads the seals from <see cref="SevenSignsActor"/> and returns placements for the world.
/// </summary>
public sealed class MammonActor : GlobalActor<MammonPlacement>
{
    public const string StageName = "mammon";

    /// <summary>Mobius <c>AutoSpawnHandler</c> defaults: first spawn 30 s after activation, then every hour.</summary>
    public const long InitialDelayMs = 30_000;
    public const long MoveMs = 3_600_000;

    /// <summary>Mobius <c>random_spawn_loc</c> group 11: the eight Necropolises.</summary>
    public static readonly (int X, int Y, int Z)[] MerchantSpots =
    [
        (-52172, 78884, -4741),   // Necropolis of Devotion
        (-41350, 209876, -5087),  // Necropolis of Sacrifice
        (-21657, 77164, -5173),   // Patriots' Necropolis
        (45029, 123802, -5413),   // Pilgrims' Necropolis
        (83175, 208998, -5439),   // Saints' Necropolis
        (111337, 173804, -5439),  // Necropolis of Worship
        (118343, 132578, -4831),  // Necropolis of Martyrdom
        (172373, -17833, -4901)   // Disciples' Necropolis
    ];

    /// <summary>Mobius <c>random_spawn_loc</c> group 12: the six Catacombs.</summary>
    public static readonly (int X, int Y, int Z)[] BlacksmithSpots =
    [
        (-19360, 13278, -4901),   // Catacomb of Dark Omens
        (-53131, -250502, -7909), // Catacomb of the Heretic
        (46303, 170091, -4981),   // Catacomb of the Branded
        (-20485, -251008, -8165), // Catacomb of the Apostate
        (12669, -248698, -9581),  // Catacomb of the Forbidden Path
        (140519, 79464, -5429)    // Catacomb of the Witch
    ];

    readonly SevenSignsActor _signs;
    readonly Slot _merchant = new(SevenSignsProtocol.MammonMerchantId, MerchantSpots);
    readonly Slot _blacksmith = new(SevenSignsProtocol.MammonBlacksmithId, BlacksmithSpots);
    Random _random;

    public MammonActor(SevenSignsActor signs, Random random)
        : base(StageName, ActorTime.Calendar)
    {
        _signs = signs ?? throw new ArgumentNullException(nameof(signs));
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    /// <summary>The actor's stream; tests script the spots by replacing it.</summary>
    public Random Random
    {
        get => _random;
        set => _random = value ?? throw new ArgumentNullException(nameof(value));
    }

    protected override void OnStep(long nowUnixMs, List<MammonPlacement> effects)
    {
        var live = _signs.Engine.Effects();
        Step(_merchant, live.MammonMerchant, nowUnixMs, effects);
        Step(_blacksmith, live.MammonBlacksmith, nowUnixMs, effects);
    }

    void Step(Slot slot, bool active, long now, List<MammonPlacement> effects)
    {
        if (!active)
        {
            if (slot.Placed)
                effects.Add(new MammonPlacement(slot.NpcId, null));
            slot.Placed = false;
            slot.DueMs = 0;
            return;
        }

        if (slot.DueMs == 0)
        {
            slot.DueMs = now + InitialDelayMs;
            return;
        }

        if (now < slot.DueMs)
            return;

        slot.Placed = true;
        effects.Add(new MammonPlacement(slot.NpcId, slot.Spots[_random.Next(slot.Spots.Length)]));
        slot.DueMs = now + MoveMs;
    }

    sealed class Slot(int npcId, (int X, int Y, int Z)[] spots)
    {
        public int NpcId { get; } = npcId;
        public (int X, int Y, int Z)[] Spots { get; } = spots;
        public long DueMs;
        public bool Placed;
    }
}
