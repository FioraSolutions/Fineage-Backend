using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Net;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius <c>YOU_HIT_S1_FOR_S2_DAMAGE</c> (2264) after a melee or offensive skill hit: the attacker sees its own
/// damage line in system chat, and watchers do not. Carried over from the V2-era dev test when the feature moved
/// to Net fanout.
/// </summary>
public sealed class NetCombatDamageChatTests
{
    const int MobId = 9_300_001;

    /// <summary>Gremlin in melee reach of the Elf spawn, so the swing lands on the first request.</summary>
    static SpawnCatalog Adjacent() =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["damage-chat"] = new("damage-chat", true,
            [
                new SpawnNpcDefinition(MobId, 90_001, "Gremlin", NetTestKit.ElfX + 30, NetTestKit.ElfY, NetTestKit.ElfZ,
                    0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                    Level: 1, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        });

    [Fact]
    public async Task Melee_hit_sends_the_attacker_only_damage_line()
    {
        var kit = new NetTestKit(Adjacent(), "damage-chat");
        var (session, player) = await kit.EnterAsync(classId: 0, race: 0); // Human Fighter: melee
        Assert.True(kit.World.TryGetEntityById(MobId, out var mob));

        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;

        Assert.Null(await kit.World.TrySelectTargetAsync(session, MobId, CancellationToken.None));
        Assert.Null(await kit.World.TryAttackAsync(session, MobId, CancellationToken.None));

        var attacks = rec.Of(nameof(IWorldEventPublisher.FanoutAttackState))
            .Select(args => Assert.IsType<Fineage.Protocol.AttackMsg>(args[1]))
            .ToList();
        var swing = Assert.Single(attacks);

        var lines = rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage));
        var damageLines = lines
            .Select(args => (Watchers: Assert.IsAssignableFrom<IReadOnlyList<int>>(args[0]),
                             Line: Assert.IsType<SystemMessageMsg>(args[1])))
            .Where(entry => entry.Line.Id == CombatProtocol.HitTargetForDamage)
            .ToList();

        // A miss carries flag 0x80 and no damage, so Mobius emits no hit line at all.
        if (swing.Flags == ZoneCombat.HitFlagMiss)
        {
            Assert.Empty(damageLines);
            return;
        }

        var hit = Assert.Single(damageLines);
        Assert.Equal(player.ObjectId, Assert.Single(hit.Watchers)); // attacker only, never the knownlist
        Assert.Equal(mob.Name, hit.Line.Params[0]);
        Assert.Equal(swing.Damage.ToString(), hit.Line.Params[1]);
        Assert.Contains("damage", hit.Line.Text);
    }
}
