using Fineage.Zone.Observation;

namespace Fineage.Agent.Observation.Tests;

static class SnapshotFactory
{
    public static ZoneObservationSnapshot Create(int visible, int objectId = 7, long tick = 42, string namePrefix = "Npc")
    {
        var entities = new ZoneObservedEntity[visible];
        for (int i = 0; i < visible; i++)
        {
            int id = 1000 + i;
            entities[i] = new ZoneObservedEntity(id, "npc", $"{namePrefix}{i}", 10 * i, 0, 0, 0, 1, 0, 50, 100, 20, 40, 0, 0);
        }

        return new ZoneObservationSnapshot(
            objectId,
            tick,
            capturedAtUnixMs: 1_000,
            new ZoneObservedSelf(objectId, "Hero", 0, 0, 0, 0, 1, 0, 100, 100, 50, 50, 25, 25),
            entities,
            inventory: [new ZoneObservedInventoryItem(9001, 57, "Adena", 100, 0, null)],
            inventoryTotal: 1,
            quests: [new ZoneObservedQuest(201, "Destroy the Enemy", 1, 2)],
            questsCompletedCount: 0,
            party: null,
            activeOperation: null);
    }
}
