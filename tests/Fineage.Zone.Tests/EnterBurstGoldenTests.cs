using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.Net;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class EnterBurstGoldenTests
{
    [Fact]
    public async Task Entrant_peer_receives_the_same_presence_followups_as_the_later_move_diff()
    {
        var kit = new NetTestKit();
        var (peerSession, peer) = await kit.EnterAsync();
        var (session, entrant) = await kit.EnterAsync();
        entrant.Sitting = true;
        var handler = new SessionLifecyclePacketHandler(
            new MemoryHandoffStore(new HandoffTokens(HandoffTokens.DefaultSecret)),
            kit.World,
            new EntityIdMap(),
            new NetPresenceHub(),
            () => kit.Now,
            _ => 0,
            (_, _, _, _) => Task.CompletedTask);

        var frames = EnterEncoder.EncodeAndDispatch(handler.CommitEnterForTest(
            session, EntityId.FromObjectId(entrant.ObjectId, 1), entrant));
        var peerFrames = frames.Where(f => f.PeerObjectId == peer.ObjectId).ToList();

        Assert.Contains(peerFrames, f => f.Opcode == NetOpcode.EntitySpawn);
        Assert.Contains(peerFrames, f => f.Opcode == NetOpcode.ChangeWaitType);
        Assert.Equal(
            NetEnterWorldProjector.BuildPresenceFollowUps(entrant,
                EntityId.FromObjectId(entrant.ObjectId, 1), kit.World).Select(f => f.Opcode),
            peerFrames.Where(f => f.Opcode != NetOpcode.EntitySpawn && f.Opcode != NetOpcode.RelationChanged)
                .Select(f => f.Opcode));
    }

    [Fact]
    public async Task Enter_burst_signatures_are_stable_for_seeded_world()
    {
        var kit = new NetTestKit();
        var mob1 = kit.World.SpawnMonsterForTests(20001, NetTestKit.ElfX + 50, NetTestKit.ElfY, NetTestKit.ElfZ);
        var mob2 = kit.World.SpawnMonsterForTests(20001, NetTestKit.ElfX + 55, NetTestKit.ElfY, NetTestKit.ElfZ);
        _ = mob1;
        _ = mob2;

        var (session, entity) = await kit.EnterAsync();
        var self = EntityId.FromObjectId(entity.ObjectId, 1);
        var handler = new SessionLifecyclePacketHandler(
            new MemoryHandoffStore(new HandoffTokens(HandoffTokens.DefaultSecret)),
            kit.World,
            new EntityIdMap(),
            new NetPresenceHub(),
            () => kit.Now,
            _ => 0,
            (_, _, _, _) => Task.CompletedTask);

        var snapshot = handler.CommitEnterForTest(session, self, entity);
        var frames = EnterEncoder.EncodeAndDispatch(snapshot);
        IReadOnlyList<string> wire = NetEnterSpawnRecord.Describe(frames);

        var snapshotAgain = handler.CommitEnterForTest(session, self, entity);
        var framesAgain = EnterEncoder.EncodeAndDispatch(snapshotAgain);
        Assert.Equal(wire, NetEnterSpawnRecord.Describe(framesAgain));
        Assert.True(frames.Count > 4);
    }
}