using Fineage.Game;
using Fineage.Game.Services;
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
    public IReadOnlyList<(int SkillId, int Level)> Skills { get; set; } = [];

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

    /// <summary>Pet only (Kind = Pet): collar, level table, exp, meal meter and the pet's own bag.</summary>
    public PetState? Pet { get; init; }
}

/// <summary>Mobius <c>Pet</c>: control item, exp / level, current meal (<c>_curFed</c>) and <c>PetInventory</c>.</summary>
sealed class PetState
{
    public required PetTemplate Template { get; init; }
    public int ControlObjectId { get; init; }
    public int ControlItemId { get; init; }
    public long Exp { get; set; }
    public int Fed { get; set; }
    public required PlayerInventory Inventory { get; init; }
    /// <summary>Exp before the last death (Mobius <c>_expBeforeDeath</c>), for a resurrection's restore.</summary>
    public long ExpBeforeDeath { get; set; }
    public long NextFeedMs { get; set; }
    /// <summary>Pick-up order: ground item the pet runs to (Mobius AI_INTENTION_PICK_UP); 0 = none.</summary>
    public int PickupTargetId { get; set; }
    /// <summary>Custom pet name; empty shows the npc name.</summary>
    public string Name { get; set; } = "";
}

sealed partial class ZoneEntity
{
    /// <summary>Set on the summon entity itself (Kind is Npc).</summary>
    public SummonState? Summon { get; set; }

    /// <summary>Set on a player: object id of its servitor / pet in the world, 0 when none.</summary>
    public int SummonObjectId { get; set; }
}

/// <summary>
/// A cubic floating around its owner (Mobius <c>Cubic</c>): template, power, action interval and cap, lifetime and
/// the running action session (Mobius <c>CubicAction</c> / <c>CubicHeal</c> task with its own activation count).
/// </summary>
sealed class CubicState
{
    public required Fineage.Game.Services.CubicTemplate Template { get; init; }
    public int Id => Template.Id;
    public double Power { get; init; }
    public long DelayMs { get; init; }
    public int Chance { get; init; }
    public int MaxCount { get; init; }
    public long ExpiresAtMs { get; init; }
    public bool GivenByOther { get; init; }
    /// <summary>Action task running (Mobius <c>_actionTask != null</c>).</summary>
    public bool Running { get; set; }
    public long NextActionMs { get; set; }
    /// <summary>Activations in the current action session (Mobius <c>CubicAction._currentCount</c>).</summary>
    public int Count { get; set; }
}

sealed partial class ZoneEntity
{
    /// <summary>Players only: active cubics, oldest first (Mobius <c>Player._cubics</c>).</summary>
    public List<CubicState> Cubics => _cubics ??= [];

    List<CubicState>? _cubics;

    public bool HasCubics => _cubics is { Count: > 0 };

    public int[] CubicIds()
    {
        if (!HasCubics)
            return [];
        var ids = new int[Cubics.Count];
        for (int i = 0; i < ids.Length; i++)
            ids[i] = Cubics[i].Id;
        return ids;
    }
}
