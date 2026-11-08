using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius Creature.doDie → broadcastPacket(Die): every player who knows the mob sees it die; late observers see a corpse.</summary>
public sealed class NetDeathBroadcastTests
{
    const int MobId = 9_300_001;

    static SpawnCatalog Map() =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["death"] = new("death", true,
            [
                new SpawnNpcDefinition(MobId, 90000 + 2, "Gremlin", NetTestKit.ElfX + 30, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 1, 1f, 1f, 60, 0, "", "Monster",
                    Level: 1, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        });

    [Fact]
    public async Task Mob_death_is_broadcast_to_killer_and_bystander_and_corpse_spawns_dead()
    {
        var kit = new NetTestKit(Map(), "death");
        var (killerSession, killer) = await kit.EnterAsync(classId: 0, race: 0);
        var (_, bystander) = await kit.EnterAsync(classId: 0, race: 0, x: NetTestKit.ElfX + 200);
        Assert.True(kit.World.TryGetEntityById(MobId, out var mob));
        Assert.Contains(MobId, killer.Knownlist);
        Assert.Contains(MobId, bystander.Knownlist);
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;

        Assert.Null(await kit.World.TrySelectTargetAsync(killerSession, MobId, CancellationToken.None));
        Assert.Null(await kit.World.TryAttackAsync(killerSession, MobId, CancellationToken.None));
        // HP 1 mob: keep swinging (misses allowed) until it dies.
        for (int i = 0; i < 60 && mob.CurHp > 0; i++)
        {
            kit.Now = kit.Now.AddMilliseconds(500);
            await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        }
        Assert.Equal(0, mob.CurHp);

        var dies = rec.Of(nameof(IWorldEventPublisher.FanoutDie));
        Assert.Single(dies);
        var watchers = (IReadOnlyList<int>)dies[0][0]!;
        Assert.Contains(killer.ObjectId, watchers);
        Assert.Contains(bystander.ObjectId, watchers);
        Assert.Equal(MobId, (int)dies[0][1]!);
        Assert.Equal(killer.ObjectId, (int)dies[0][2]!);
        // Both saw the killing blow's HP drop to 0.
        Assert.Contains(rec.Of(nameof(IWorldEventPublisher.FanoutStatusUpdate)),
            s => ((IReadOnlyList<int>)s[0]!).Contains(bystander.ObjectId) && ((StatusUpdateNetMsg)s[1]!).EntityValue == (ulong)(uint)MobId);

        // A player arriving while the corpse lies gets it as dead (no standing gremlin).
        var (lateSession, late) = await kit.EnterAsync(classId: 0, race: 0, x: NetTestKit.ElfX - 200);
        Assert.Contains(MobId, late.Knownlist);
        var corpse = NetEnterWorldProjector.BuildPresenceSpawn(mob, EntityId.FromObjectId(MobId, 1));
        Assert.True(corpse.Dead);
        Assert.True(NetCodec.DecodeEntitySpawn(NetCodec.EncodeEntitySpawn(corpse)).Dead);

        // Corpse decay: DeleteObject (EntityDespawn) to everyone who still knows it.
        rec.Calls.Clear();
        kit.Now = kit.Now.AddMinutes(5);
        await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        var despawns = rec.Of(nameof(IWorldEventPublisher.FanoutEntityDespawn));
        Assert.Contains(despawns, d => ((IReadOnlyList<int>)d[0]!).Contains(bystander.ObjectId) &&
                                       ((IReadOnlyList<int>)d[0]!).Contains(late.ObjectId) &&
                                       (int)d[1]! == MobId);
        _ = lateSession;
    }

    [Fact]
    public async Task Live_mob_spawns_alive()
    {
        var kit = new NetTestKit(Map(), "death");
        await kit.EnterAsync(classId: 0, race: 0);
        Assert.True(kit.World.TryGetEntityById(MobId, out var mob));
        Assert.False(NetEnterWorldProjector.BuildPresenceSpawn(mob, EntityId.FromObjectId(MobId, 1)).Dead);
    }

    [Fact]
    public async Task Admin_kill_fans_die_and_a_zero_hp_bar_to_the_victim()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        var (session, elf) = await kit.EnterAsync();

        Assert.Null(await kit.World.TryAdminKillPlayerAsync(session, CancellationToken.None));
        Assert.True(elf.Dead);
        Assert.Equal(0, elf.CurHp);

        // The victim knows itself, so it is a watcher of its own die + status fanout.
        var die = Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutDie)));
        Assert.Contains(elf.ObjectId, (IReadOnlyList<int>)die[0]!);
        Assert.Equal(elf.ObjectId, (int)die[1]!);

        Assert.Contains(rec.Of(nameof(IWorldEventPublisher.FanoutStatusUpdate)), s =>
            ((IReadOnlyList<int>)s[0]!).Contains(elf.ObjectId) &&
            ((StatusUpdateNetMsg)s[1]!).EntityValue == (ulong)(uint)elf.ObjectId &&
            ((StatusUpdateNetMsg)s[1]!).Values[0] == 0);
    }
}
