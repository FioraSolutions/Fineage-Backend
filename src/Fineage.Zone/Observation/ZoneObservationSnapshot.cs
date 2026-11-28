using System.Collections.Immutable;

namespace Fineage.Zone.Observation;

/// <summary>
/// Immutable copy of what one bound character legitimately sees at one tick.
/// Captured on the simulation thread from the live knownlist; contains no live
/// <c>ZoneEntity</c> or <c>ZoneWorld</c> references, so observers can read it
/// off the tick without locking the world.
/// </summary>
public sealed class ZoneObservationSnapshot
{
    public ZoneObservationSnapshot(
        int objectId,
        long tick,
        long capturedAtUnixMs,
        ZoneObservedSelf self,
        IReadOnlyList<ZoneObservedEntity> visible,
        IReadOnlyList<ZoneObservedInventoryItem> inventory,
        int inventoryTotal,
        IReadOnlyList<ZoneObservedQuest> quests,
        int questsCompletedCount,
        ZoneObservedParty? party,
        string? activeOperation)
    {
        ObjectId = objectId;
        Tick = tick;
        CapturedAtUnixMs = capturedAtUnixMs;
        Self = self ?? throw new ArgumentNullException(nameof(self));
        Visible = visible is ImmutableArray<ZoneObservedEntity> arr
            ? arr
            : (visible ?? throw new ArgumentNullException(nameof(visible))).ToImmutableArray();
        Inventory = inventory is ImmutableArray<ZoneObservedInventoryItem> inv
            ? inv
            : (inventory ?? throw new ArgumentNullException(nameof(inventory))).ToImmutableArray();
        InventoryTotal = inventoryTotal;
        Quests = quests is ImmutableArray<ZoneObservedQuest> q
            ? q
            : (quests ?? throw new ArgumentNullException(nameof(quests))).ToImmutableArray();
        QuestsCompletedCount = questsCompletedCount;
        Party = party;
        ActiveOperation = activeOperation;
    }

    public int ObjectId { get; }
    public long Tick { get; }
    public long CapturedAtUnixMs { get; }
    public ZoneObservedSelf Self { get; }
    public ImmutableArray<ZoneObservedEntity> Visible { get; }
    public ImmutableArray<ZoneObservedInventoryItem> Inventory { get; }
    public int InventoryTotal { get; }
    public ImmutableArray<ZoneObservedQuest> Quests { get; }
    public int QuestsCompletedCount { get; }
    public ZoneObservedParty? Party { get; }
    public string? ActiveOperation { get; }
}

public sealed record ZoneObservedSelf(
    int ObjectId,
    string Name,
    int X,
    int Y,
    int Z,
    int Heading,
    int Level,
    int ClassId,
    int CurHp,
    int MaxHp,
    int CurMp,
    int MaxMp,
    int CurCp,
    int MaxCp);

public sealed record ZoneObservedEntity(
    int ObjectId,
    string Kind,
    string Name,
    int X,
    int Y,
    int Z,
    int Heading,
    int Level,
    int ClassId,
    int CurHp,
    int MaxHp,
    int CurMp,
    int MaxMp,
    int CurCp,
    int MaxCp);

public sealed record ZoneObservedInventoryItem(
    int ObjectId,
    int ItemId,
    string Name,
    int Count,
    int EnchantLevel,
    string? EquippedSlot);

public sealed record ZoneObservedQuest(
    int QuestId,
    string Name,
    int State,
    int Cond);

public sealed record ZoneObservedPartyMember(int ObjectId, string Name, int Level, int ClassId);

public sealed record ZoneObservedParty(
    int PartyId,
    int LeaderObjectId,
    IReadOnlyList<ZoneObservedPartyMember> Members);
