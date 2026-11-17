using Fineage.Net.Contract;

namespace Fineage.Zone.World;

/// <summary>
/// A servitor (Mobius <c>Servitor</c>) or pet riding on an NPC-kind entity: who owns it, its lifetime / upkeep clock
/// (Mobius <c>Servitor.run</c>, every 5 s) and the owner's orders (follow / stay / attack target / move).
/// </summary>
sealed class SummonState
{
    public int OwnerObjectId { get; init; }
    public SummonKindNet Kind { get; init; } = SummonKindNet.Servitor;
    public int SkillId { get; init; }
    public int SkillLevel { get; init; }
    public long LifeTotalMs { get; init; }
    public long LifeRemainingMs { get; set; }
    public int UpkeepItemId { get; init; }
    public int UpkeepCount { get; init; }
    public long UpkeepIntervalMs { get; init; }
    public long UpkeepRemainingMs { get; set; }
    public double ExpMultiplier { get; init; } = 1;
    public IReadOnlyList<(int SkillId, int Level)> Skills { get; init; } = [];

    /// <summary>Mobius <c>Summon.getFollowStatus</c>: true runs after the owner, false stays put.</summary>
    public bool Following { get; set; } = true;
    /// <summary>Attack order (Mobius SummonAI intention ATTACK); 0 = none.</summary>
    public int TargetId { get; set; }
    /// <summary>A RequestPetMove walk is in progress (follow is suspended until a new order).</summary>
    public bool MoveOrder { get; set; }
    /// <summary>Ordered skill waiting for range (Mobius <c>useMagic</c> through the summon); 0 = none.</summary>
    public int PendingSkillId { get; set; }
    public long NextLifeTickMs { get; set; }
    public long NextAttackMs { get; set; }
    /// <summary>Dead summon: when the corpse goes away.</summary>
    public long DecayAtMs { get; set; }
}

sealed partial class ZoneEntity
{
    /// <summary>Set on the summon entity itself (Kind is Npc).</summary>
    public SummonState? Summon { get; set; }

    /// <summary>Set on a player: object id of its servitor / pet in the world, 0 when none.</summary>
    public int SummonObjectId { get; set; }
}
