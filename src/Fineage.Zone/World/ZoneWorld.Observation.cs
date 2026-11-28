using Fineage.Game;
using Fineage.Zone.Observation;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    const int ObservationInventoryCap = 32;
    const int ObservationQuestCap = 32;

    readonly ZoneObservationPublisher _observationPublisher = new();

    /// <summary>
    /// Capture the bound character's knownlist and summaries on the simulation thread
    /// and publish an immutable snapshot. The tick does not wait for Agent I/O; this
    /// request completes after capture+publish, then callers read the snapshot off-thread.
    /// </summary>
    public async Task<ZoneObservationSnapshot?> PublishObservationAsync(ZoneSession session, CancellationToken ct)
    {
        try
        {
            return await OnSimulationAsync(session, "agent-observe", () => CaptureAndPublishObservation(session), ct)
                .ConfigureAwait(false);
        }
        catch (ZoneIngressRefusal)
        {
            return null;
        }
    }

    /// <summary>
    /// Lock-free read of the latest published snapshot. Safe off the simulation thread;
    /// does not touch world locks or mutable entities.
    /// </summary>
    public bool TryGetPublishedObservation(int objectId, out ZoneObservationSnapshot snapshot) =>
        _observationPublisher.TryGet(objectId, out snapshot!);

    ZoneObservationSnapshot CaptureAndPublishObservation(ZoneSession session)
    {
        if (!TryGetLivePlayer(session, out var self))
            throw new InvalidOperationException($"Character {session.ObjectId} is not in the world.");

        var visible = new List<ZoneObservedEntity>(self.Knownlist.Count);
        foreach (int id in self.Knownlist)
        {
            if (!_byId.TryGetValue(id, out var other))
                continue;
            visible.Add(CopyEntity(other));
        }

        visible.Sort(static (a, b) => a.ObjectId.CompareTo(b.ObjectId));

        var inventoryRows = new List<ZoneObservedInventoryItem>();
        int inventoryTotal = 0;
        if (self.Inventory?.Items is { } items)
        {
            inventoryTotal = items.Count;
            foreach (var item in items)
            {
                if (inventoryRows.Count >= ObservationInventoryCap)
                    break;
                inventoryRows.Add(new ZoneObservedInventoryItem(
                    item.ObjectId,
                    item.ItemId,
                    item.Template.Name ?? "",
                    item.Count,
                    item.EnchantLevel,
                    item.EquippedSlot?.ToString()));
            }
        }

        var questList = NetQuestList(self);
        var questRows = new List<ZoneObservedQuest>();
        var active = questList.Active ?? [];
        foreach (var entry in active)
        {
            if (questRows.Count >= ObservationQuestCap)
                break;
            questRows.Add(new ZoneObservedQuest(entry.QuestId, entry.Name ?? "", entry.State, entry.Cond));
        }

        ZoneObservedParty? party = null;
        if (_party.Of(self.ObjectId) is { } zoneParty)
        {
            var members = new List<ZoneObservedPartyMember>(zoneParty.MemberIds.Count);
            foreach (int memberId in zoneParty.MemberIds)
            {
                string name = "";
                int level = 0;
                int classId = 0;
                if (_byId.TryGetValue(memberId, out var mate))
                {
                    name = mate.Name ?? "";
                    level = mate.Level;
                    classId = mate.ClassId;
                }

                members.Add(new ZoneObservedPartyMember(memberId, name, level, classId));
            }

            party = new ZoneObservedParty(zoneParty.PartyId, zoneParty.LeaderObjectId, members);
        }

        var snapshot = new ZoneObservationSnapshot(
            self.ObjectId,
            _simulationTicks.Tick,
            NowUnixMs(),
            new ZoneObservedSelf(
                self.ObjectId,
                self.Name ?? "",
                self.X,
                self.Y,
                self.Z,
                self.Heading,
                self.Level,
                self.ClassId,
                self.CurHp,
                self.MaxHp,
                self.CurMp,
                self.MaxMp,
                self.CurCp,
                self.MaxCp),
            visible,
            inventoryRows,
            inventoryTotal,
            questRows,
            (questList.CompletedQuestIds ?? []).Length,
            party,
            activeOperation: null);

        _observationPublisher.Publish(snapshot);
        return snapshot;
    }

    static ZoneObservedEntity CopyEntity(ZoneEntity entity)
    {
        string kind = entity.Kind switch
        {
            ZoneEntityKind.Player => "player",
            ZoneEntityKind.GroundItem => "item",
            _ => "npc"
        };
        return new ZoneObservedEntity(
            entity.ObjectId,
            kind,
            entity.Name ?? "",
            entity.X,
            entity.Y,
            entity.Z,
            entity.Heading,
            entity.Level,
            entity.ClassId,
            entity.CurHp,
            entity.MaxHp,
            entity.CurMp,
            entity.MaxMp,
            entity.CurCp,
            entity.MaxCp);
    }
}
