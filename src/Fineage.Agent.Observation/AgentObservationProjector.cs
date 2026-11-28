using System.Text.Json;
using Fineage.Agent.Contracts;
using Fineage.Zone.Observation;

namespace Fineage.Agent.Observation;

/// <summary>
/// Maps an immutable Zone snapshot to a bounded <see cref="AgentObservation"/> off the
/// simulation thread. Enforces entity and payload limits by paging, never by silent truncation.
/// </summary>
public static class AgentObservationProjector
{
    public static AgentObservationResult Project(
        ZoneObservationSnapshot snapshot,
        CharacterId characterId,
        CorrelationId correlationId,
        AgentObservationQuery? query = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        query ??= AgentObservationQuery.Default;
        if (query.PageOffset < 0)
            return AgentObservationResult.Refuse(AgentRefusalCodes.InvalidContract, "PageOffset must be >= 0.");

        var filtered = new List<ZoneObservedEntity>(snapshot.Visible.Length);
        string? kind = string.IsNullOrWhiteSpace(query.Kind) ? null : query.Kind.Trim();
        foreach (var entity in snapshot.Visible)
        {
            if (kind is not null && !string.Equals(entity.Kind, kind, StringComparison.OrdinalIgnoreCase))
                continue;
            filtered.Add(entity);
        }

        int totalVisible = filtered.Count;
        if (query.PageOffset > totalVisible)
        {
            return AgentObservationResult.Refuse(
                AgentRefusalCodes.InvalidContract,
                $"PageOffset {query.PageOffset} is past the {totalVisible} visible entities.");
        }

        var version = new SnapshotVersion(snapshot.Tick);
        var self = new ObservedSelf(snapshot.Self.ObjectId, snapshot.Self.X, snapshot.Self.Y, snapshot.Self.Z, snapshot.Self.Heading);
        JsonElement fullDetails = BuildDetails(snapshot, includeInventory: true, includeQuests: true);
        int take = Math.Min(AgentObservation.MaxEntities, totalVisible - query.PageOffset);

        AgentContractException? lastLimit = null;
        for (int attempt = take; attempt >= 0; attempt--)
        {
            bool slimDetails = attempt == 0;
            JsonElement details = slimDetails
                ? BuildDetails(snapshot, includeInventory: false, includeQuests: false)
                : fullDetails;
            var page = new ObservedEntity[attempt];
            for (int i = 0; i < attempt; i++)
                page[i] = ToObserved(filtered[query.PageOffset + i]);

            bool hasMore = query.PageOffset + attempt < totalVisible;
            try
            {
                var observation = AgentObservation.Create(
                    version,
                    correlationId,
                    characterId,
                    self,
                    page,
                    details,
                    cursor: AgentCursor.Create(version, hasMore ? (query.PageOffset + attempt).ToString() : null),
                    totalVisible: totalVisible,
                    pageOffset: query.PageOffset,
                    hasMore: hasMore);
                return AgentObservationResult.Success(observation);
            }
            catch (AgentContractException ex) when (ex.Code == AgentRefusalCodes.ObservationLimit)
            {
                lastLimit = ex;
                if (attempt == 0)
                    break;
            }
        }

        return AgentObservationResult.Refuse(
            AgentRefusalCodes.ObservationLimit,
            lastLimit?.Message ?? "Observation exceeded payload limits even with an empty page.");
    }

    static ObservedEntity ToObserved(ZoneObservedEntity entity)
    {
        var attributes = new Dictionary<string, object?>
        {
            ["name"] = new { text = entity.Name, untrusted = true, role = "data" },
            ["heading"] = entity.Heading,
            ["level"] = entity.Level,
            ["classId"] = entity.ClassId,
            ["hp"] = new { cur = entity.CurHp, max = entity.MaxHp },
            ["mp"] = new { cur = entity.CurMp, max = entity.MaxMp },
            ["cp"] = new { cur = entity.CurCp, max = entity.MaxCp }
        };
        JsonElement attrs = JsonSerializer.SerializeToElement(attributes);
        // ObservedEntity caps names at 64 characters (ContractValidation.MaxNameLength).
        string? name = entity.Name;
        if (name is { Length: > 64 })
            name = name[..64];
        return new ObservedEntity(entity.ObjectId, entity.Kind, entity.X, entity.Y, entity.Z, name, attrs);
    }

    static JsonElement BuildDetails(ZoneObservationSnapshot snapshot, bool includeInventory, bool includeQuests)
    {
        object? inventory = null;
        if (includeInventory)
        {
            inventory = new
            {
                total = snapshot.InventoryTotal,
                hasMore = snapshot.Inventory.Length < snapshot.InventoryTotal,
                items = snapshot.Inventory.Select(item => new
                {
                    objectId = item.ObjectId,
                    itemId = item.ItemId,
                    count = item.Count,
                    enchantLevel = item.EnchantLevel,
                    equippedSlot = item.EquippedSlot,
                    name = new { text = item.Name, untrusted = true, role = "data" }
                }).ToArray()
            };
        }

        object? quests = null;
        if (includeQuests)
        {
            quests = new
            {
                completedCount = snapshot.QuestsCompletedCount,
                hasMore = false,
                active = snapshot.Quests.Select(q => new
                {
                    questId = q.QuestId,
                    state = q.State,
                    cond = q.Cond,
                    name = new { text = q.Name, untrusted = true, role = "data" }
                }).ToArray()
            };
        }

        object? party = snapshot.Party is { } p
            ? new
            {
                inParty = true,
                partyId = p.PartyId,
                leaderObjectId = p.LeaderObjectId,
                members = p.Members.Select(m => new
                {
                    objectId = m.ObjectId,
                    level = m.Level,
                    classId = m.ClassId,
                    name = new { text = m.Name, untrusted = true, role = "data" }
                }).ToArray()
            }
            : new { inParty = false };

        var payload = new
        {
            capturedAtUnixMs = snapshot.CapturedAtUnixMs,
            hp = new { cur = snapshot.Self.CurHp, max = snapshot.Self.MaxHp },
            mp = new { cur = snapshot.Self.CurMp, max = snapshot.Self.MaxMp },
            cp = new { cur = snapshot.Self.CurCp, max = snapshot.Self.MaxCp },
            level = snapshot.Self.Level,
            classId = snapshot.Self.ClassId,
            name = new { text = snapshot.Self.Name, untrusted = true, role = "data" },
            inventory,
            quests,
            party,
            activeOperation = snapshot.ActiveOperation
        };
        return JsonSerializer.SerializeToElement(payload);
    }
}
