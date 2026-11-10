using Fineage.Protocol;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Skill 254 Spoil → death → skill 42 Sweeper through the normal SkillUse path (Mobius CT0 rules).</summary>
public sealed class SpoilSweepSkillTests
{
    static async Task<(NetTestKit Kit, ZoneSession S, World.ZoneEntity P, World.ZoneEntity Mob)> SetupAsync(NetTestKit? kit = null)
    {
        kit ??= new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var mob = kit.World.AttackableForTest()!;
        Assert.NotNull(mob);
        var (session, player) = await kit.EnterAsync(classId: 54, race: 4, x: mob.X + 10, y: mob.Y, z: mob.Z);
        player.KnownSkills[254] = 11;
        player.KnownSkills[42] = 1;
        player.CurMp = 500;
        mob.Level = 1; // magic level 72 vs level 1: roll always lands
        return (kit, session, player, mob);
    }

    static async Task<string?> Cast(NetTestKit kit, ZoneSession s, int skill, int target)
    {
        var error = await kit.World.TrySkillUseAsync(s, skill, target, CancellationToken.None);
        await kit.World.CompleteCastForTestAsync(s); // Spoil lands at hitTime
        return error;
    }

    [Fact]
    public async Task Spoil_then_sweep_gives_corpse_loot_and_consumes_the_corpse()
    {
        var (kit, s, p, mob) = await SetupAsync();
        Assert.Null(await Cast(kit, s, 254, mob.ObjectId));
        Assert.Equal(p.ObjectId, mob.SpoilerObjectId);

        kit.Now = kit.Now.AddSeconds(20); // past reuse / cast
        Assert.Null(await Cast(kit, s, 254, mob.ObjectId)); // already spoiled (357), no cost
        Assert.Equal(p.ObjectId, mob.SpoilerObjectId);

        kit.World.KillForTest(mob, p);
        mob.SweepItems.Clear();
        mob.SweepItems.Add(new Fineage.Zone.Drops.DropStack(1864, 3)); // Stem
        long before = p.Inventory!.CountOf(1864);
        Assert.Null(await Cast(kit, s, 42, mob.ObjectId));
        Assert.Equal(before + 3, p.Inventory.CountOf(1864));
        Assert.Equal(0, mob.SpoilerObjectId); // corpse consumed (despawned)
    }

    [Fact]
    public async Task Only_spoiler_or_party_may_sweep_and_spoil_decays_with_the_corpse()
    {
        var (kit, s, p, mob) = await SetupAsync();
        var (other, stranger) = await kit.EnterAsync(classId: 54, race: 4, x: mob.X + 10, y: mob.Y, z: mob.Z);
        stranger.KnownSkills[42] = 1;
        stranger.CurMp = 100;
        Assert.Null(await Cast(kit, s, 254, mob.ObjectId));
        kit.World.KillForTest(mob, p);
        mob.SweepItems.Clear();
        mob.SweepItems.Add(new Fineage.Zone.Drops.DropStack(1864, 2));

        Assert.Null(await Cast(kit, other, 42, mob.ObjectId)); // 683 no priority rights
        Assert.Equal(0, stranger.Inventory!.CountOf(1864));

        kit.World.JoinPartyForTest(p, stranger);
        Assert.Null(await Cast(kit, other, 42, mob.ObjectId));
        Assert.Equal(2, stranger.Inventory.CountOf(1864));

        // Second spoiled corpse: too old in the last 2 s, then decays at 7 + 10 s.
        var mob2 = kit.World.AttackableForTest()!;
        mob2.Level = 1;
        p.X = mob2.X + 10; p.Y = mob2.Y;
        kit.Now = kit.Now.AddSeconds(20);
        Assert.Null(await Cast(kit, s, 254, mob2.ObjectId));
        Assert.Equal(p.ObjectId, mob2.SpoilerObjectId);
        kit.World.KillForTest(mob2, p);
        mob2.SweepItems.Add(new Fineage.Zone.Drops.DropStack(1864, 1));
        kit.Now = kit.Now.AddMilliseconds(15500);
        Assert.Null(await Cast(kit, s, 42, mob2.ObjectId)); // 1247 corpse too old
        Assert.Equal(0, p.Inventory!.CountOf(1864));
        kit.Now = kit.Now.AddMilliseconds(1600);
        kit.World.DecayForTest();
        Assert.Equal(0, mob2.SpoilerObjectId);
        Assert.Empty(mob2.SweepItems);
    }

    [Fact]
    public async Task Sweep_by_turn_including_spoil_gives_loot_to_next_party_member()
    {
        var (kit, s, p, mob) = await SetupAsync();
        var (other, mate) = await kit.EnterAsync(classId: 54, race: 4, x: mob.X + 10, y: mob.Y, z: mob.Z);
        mate.KnownSkills[42] = 1;
        mate.CurMp = 100;
        // Leader first in MemberIds → ByTurnIncludingSpoil cursor 0→1 → mate
        kit.World.JoinPartyForTest(Fineage.Game.Services.PartyDistributionType.ByTurnIncludingSpoil, p, mate);

        Assert.Null(await Cast(kit, s, 254, mob.ObjectId));
        kit.World.KillForTest(mob, p);
        mob.SweepItems.Clear();
        mob.SweepItems.Add(new Fineage.Zone.Drops.DropStack(1864, 5));

        long beforeLeader = p.Inventory!.CountOf(1864);
        long beforeMate = mate.Inventory!.CountOf(1864);
        // AdminSweep hits the same ApplySweep / distributeItem path as skill 42.
        Assert.Null(await kit.World.TryAdminSweepAsync(s, new AdminSweepCorpseMsg { TargetObjectId = mob.ObjectId }, default));
        Assert.Equal(beforeLeader, p.Inventory.CountOf(1864));
        Assert.Equal(beforeMate + 5, mate.Inventory.CountOf(1864));
    }

    [Fact]
    public async Task Sweep_adena_splits_among_in_range_party()
    {
        var (kit, s, p, mob) = await SetupAsync();
        var (_, mate) = await kit.EnterAsync(classId: 54, race: 4, x: mob.X + 10, y: mob.Y, z: mob.Z);
        kit.World.JoinPartyForTest(Fineage.Game.Services.PartyDistributionType.FindersKeepers, p, mate);

        Assert.Null(await Cast(kit, s, 254, mob.ObjectId));
        kit.World.KillForTest(mob, p);
        mob.SweepItems.Clear();
        mob.SweepItems.Add(new Fineage.Zone.Drops.DropStack(57, 100)); // Adena

        long beforeL = p.Inventory!.CountOf(57);
        long beforeM = mate.Inventory!.CountOf(57);
        Assert.Null(await kit.World.TryAdminSweepAsync(s, new AdminSweepCorpseMsg { TargetObjectId = mob.ObjectId }, default));
        Assert.Equal(beforeL + 50, p.Inventory.CountOf(57));
        Assert.Equal(beforeM + 50, mate.Inventory.CountOf(57));
    }
}
