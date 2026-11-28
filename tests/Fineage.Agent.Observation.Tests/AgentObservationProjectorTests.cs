using System.Text.Json;
using Fineage.Agent.Contracts;
using Fineage.Agent.Observation;
using Fineage.Zone.Observation;
using Xunit;

namespace Fineage.Agent.Observation.Tests;

public sealed class AgentObservationProjectorTests
{
    static readonly CharacterId Character = new("4242");
    static readonly CorrelationId Correlation = new("corr-observe-1");

    [Fact]
    public void Visible_set_larger_than_max_entities_is_paged_not_silently_truncated()
    {
        var snapshot = SnapshotFactory.Create(200);
        var first = AgentObservationProjector.Project(snapshot, Character, Correlation, new AgentObservationQuery(PageOffset: 0));
        Assert.True(first.Ok, first.Detail);
        AgentObservation page = first.Observation!;
        Assert.Equal(128, page.Entities.Count);
        Assert.Equal(200, page.TotalVisible);
        Assert.Equal(0, page.PageOffset);
        Assert.True(page.HasMore);
        Assert.Equal("128", page.Cursor.Token);

        var second = AgentObservationProjector.Project(snapshot, Character, Correlation, new AgentObservationQuery(PageOffset: 128));
        Assert.True(second.Ok, second.Detail);
        AgentObservation rest = second.Observation!;
        Assert.Equal(72, rest.Entities.Count);
        Assert.Equal(200, rest.TotalVisible);
        Assert.Equal(128, rest.PageOffset);
        Assert.False(rest.HasMore);
        Assert.Null(rest.Cursor.Token);

        var ids = page.Entities.Select(e => e.ObjectId).Concat(rest.Entities.Select(e => e.ObjectId)).ToHashSet();
        Assert.Equal(200, ids.Count);
        Assert.Equal(snapshot.Visible.Select(v => v.ObjectId).ToHashSet(), ids);
    }

    [Fact]
    public void Untrusted_world_text_is_marked_as_data()
    {
        var snapshot = SnapshotFactory.Create(1);
        var result = AgentObservationProjector.Project(snapshot, Character, Correlation);
        Assert.True(result.Ok, result.Detail);
        AgentObservation observation = result.Observation!;
        JsonElement details = observation.Details!.Value;
        AssertUntrusted(details.GetProperty("name"), "Hero");
        AssertUntrusted(details.GetProperty("quests").GetProperty("active")[0].GetProperty("name"), "Destroy the Enemy");
        AssertUntrusted(details.GetProperty("inventory").GetProperty("items")[0].GetProperty("name"), "Adena");
        AssertUntrusted(observation.Entities[0].Attributes!.Value.GetProperty("name"), "Npc0");
    }

    [Fact]
    public void Kind_filter_is_applied_before_paging()
    {
        var mixed = new ZoneObservationSnapshot(
            7,
            1,
            1,
            new ZoneObservedSelf(7, "Hero", 0, 0, 0, 0, 1, 0, 1, 1, 1, 1, 1, 1),
            [
                new ZoneObservedEntity(10, "npc", "A", 0, 0, 0, 0, 1, 0, 1, 1, 1, 1, 1, 1),
                new ZoneObservedEntity(11, "player", "B", 0, 0, 0, 0, 1, 0, 1, 1, 1, 1, 1, 1),
                new ZoneObservedEntity(12, "npc", "C", 0, 0, 0, 0, 1, 0, 1, 1, 1, 1, 1, 1)
            ],
            [],
            0,
            [],
            0,
            null,
            null);
        var result = AgentObservationProjector.Project(mixed, Character, Correlation, new AgentObservationQuery(Kind: "npc"));
        Assert.True(result.Ok, result.Detail);
        Assert.Equal(2, result.Observation!.TotalVisible);
        Assert.Equal(new[] { 10, 12 }, result.Observation.Entities.Select(e => e.ObjectId).ToArray());
        Assert.False(result.Observation.HasMore);
    }

    [Fact]
    public void Official_json_stays_within_payload_limit()
    {
        var snapshot = SnapshotFactory.Create(200);
        var result = AgentObservationProjector.Project(snapshot, Character, Correlation);
        Assert.True(result.Ok, result.Detail);
        int bytes = AgentContractJson.MeasureUtf8(result.Observation!);
        Assert.True(bytes <= AgentObservation.MaxPayloadBytes, $"payload {bytes} exceeded the cap");
        Assert.True(result.Observation!.Entities.Count <= AgentObservation.MaxEntities);
    }

    [Fact]
    public void Oversized_single_page_is_shrunk_with_has_more_not_silently_dropped()
    {
        // Fat names in details-adjacent attributes plus a wide visible set force the byte cap
        // to fire before the entity cap; the projector must shrink the page and keep HasMore.
        var fat = SnapshotFactory.Create(128);
        var fatQuests = Enumerable.Range(0, 32)
            .Select(i => new ZoneObservedQuest(1000 + i, new string('Q', 800), 1, 1))
            .ToArray();
        fat = new ZoneObservationSnapshot(
            fat.ObjectId, fat.Tick, fat.CapturedAtUnixMs, fat.Self, fat.Visible,
            fat.Inventory, fat.InventoryTotal, fatQuests, 0, null, null);
        var result = AgentObservationProjector.Project(fat, Character, Correlation);
        Assert.True(result.Ok, result.Detail);
        AgentObservation observation = result.Observation!;
        int bytes = AgentContractJson.MeasureUtf8(observation);
        Assert.True(bytes <= AgentObservation.MaxPayloadBytes, $"payload {bytes}");
        if (observation.Entities.Count < 128)
            Assert.True(observation.HasMore);
        Assert.Equal(128, observation.TotalVisible);
    }

    static void AssertUntrusted(JsonElement mark, string text)
    {
        Assert.Equal(text, mark.GetProperty("text").GetString());
        Assert.True(mark.GetProperty("untrusted").GetBoolean());
        Assert.Equal("data", mark.GetProperty("role").GetString());
    }
}
