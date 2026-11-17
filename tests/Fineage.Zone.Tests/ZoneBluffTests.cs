using Fineage.Game;
using Fineage.Zone.Combat;
using Fineage.Zone.Geo;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Bluff (358) — the Human dagger instant payload next to the STUN abnormal. Mobius <c>Bluff.onStart</c> turns the
/// victim to the caster's heading (so a break-behind crit becomes possible) and <c>TargetCancel</c> +
/// <c>DeleteHateOfMe</c> make it forget who hit it. Raid bosses and the headquarters NPC never turn.
/// </summary>
public sealed class ZoneBluffTests
{
    const int BluffSkill = 358;
    const int MobId = 9_400_631;
    const int RaidId = 9_400_632;

    /// <summary>Deterministic roll: every Mobius <c>chance</c> check in Bluff's payload succeeds.</summary>
    sealed class AlwaysRoll : Random
    {
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => 0d;
    }

    static (NetTestKit Kit, NetBridgeRecorder Rec) Kit()
    {
        var kit = new NetTestKit(SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["bluff"] = new("bluff", true,
            [
                new SpawnNpcDefinition(MobId, 90000 + 631, "Gremlin",
                    NetTestKit.ElfX + 30, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                    Level: 40, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: []),
                new SpawnNpcDefinition(RaidId, 90000 + 632, "Raid Gremlin",
                    NetTestKit.ElfX + 40, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 50_000, 1f, 1f, 60, 0, "", "RaidBoss",
                    Level: 40, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        }), "bluff", geo: GeoEngine.Empty, combatRandom: new AlwaysRoll());
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, rec);
    }

    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Player, ZoneEntity Mob)> EnterAsync()
    {
        var (kit, _) = Kit();
        var (session, player) = await kit.EnterAsync(classId: 7, race: 0, level: 60); // Rogue, knows Bluff at 55
        player.KnownSkills[BluffSkill] = 1;
        player.CurMp = player.MaxMp = 500;
        Assert.True(kit.World.TryGetEntityById(MobId, out var mob));
        mob.MaxHp = mob.CurHp = 50_000;
        mob.Heading = 0;
        return (kit, session, player, mob);
    }

    [Fact]
    public void Bluff_row_carries_the_instant_payload_effects()
    {
        var effects = SkillEffectCatalog.LoadDefault();
        Assert.True(effects.TryGet(BluffSkill, out var rows));
        var names = rows.Select(r => r.Name).ToArray();
        Assert.Contains("Bluff", names);
        Assert.Contains("TargetCancel", names);
        Assert.Contains("DeleteHateOfMe", names);
        Assert.Contains("Stun", names);
    }

    [Fact]
    public async Task Bluff_turns_the_victim_to_the_caster_heading()
    {
        var (kit, session, player, mob) = await EnterAsync();
        player.Heading = 16_384;          // the caster's facing
        mob.Heading = 0;                  // looking straight back at the caster
        Assert.NotEqual(player.Heading, mob.Heading);

        Assert.Null(await kit.World.TrySkillUseAsync(session, BluffSkill, MobId, CancellationToken.None));
        kit.Now = kit.Now.AddSeconds(2);
        kit.World.ResolvePendingHitForTest(player);

        Assert.Equal(player.Heading, mob.Heading);
    }

    [Fact]
    public async Task Bluff_drops_the_victim_hate_and_target()
    {
        var (kit, session, player, mob) = await EnterAsync();
        mob.HateList[player.ObjectId] = 5_000;
        mob.AggroTargetId = player.ObjectId;
        Assert.Equal(player.ObjectId, mob.AggroTargetId);

        Assert.Null(await kit.World.TrySkillUseAsync(session, BluffSkill, MobId, CancellationToken.None));
        kit.Now = kit.Now.AddSeconds(2);
        kit.World.ResolvePendingHitForTest(player);

        Assert.DoesNotContain(player.ObjectId, mob.HateList.Keys);
        Assert.NotEqual(player.ObjectId, mob.AggroTargetId);
    }

    [Fact]
    public async Task Bluff_leaves_a_raid_boss_facing_unchanged()
    {
        var (kit, session, player, _) = await EnterAsync();
        Assert.True(kit.World.TryGetEntityById(RaidId, out var raid));
        raid.MaxHp = raid.CurHp = 90_000;
        raid.Heading = 0;
        player.Heading = 32_000;

        Assert.Null(await kit.World.TrySkillUseAsync(session, BluffSkill, RaidId, CancellationToken.None));
        kit.Now = kit.Now.AddSeconds(2);
        kit.World.ResolvePendingHitForTest(player);

        // Mobius refuses to rotate raid bosses; the stun can still land, the heading stays.
        Assert.Equal(0, raid.Heading);
    }
}
