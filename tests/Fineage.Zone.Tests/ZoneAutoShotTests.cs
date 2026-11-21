using Fineage.Game;
using Fineage.Accounts;
using Fineage.Login;
using Fineage.Zone.Combat;
using Fineage.Zone.Inventory;
using Fineage.Game.Economy;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius RequestAutoSoulShot: auto recharge after each discharge, spiritshot / blessed spiritshot M.Atk.</summary>
public sealed class ZoneAutoShotTests
{
    static async Task<(ZoneWorld World, ZoneSession Session, ZoneEntity Player)> Enter()
    {
        var store = MemoryAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(login.Register("shots", "pass1234").Ok);
        var created = login.CreateHuman("shots", "Shooter", HumanClasses.Fighter);
        var ticket = login.Play("shots", "pass1234", created.Value!.Name, HumanClasses.Fighter);
        var world = new ZoneWorld(accounts: store,
        ledgerJournal: new MemoryLedgerJournal());
        var session = new ZoneSession();
        session.Bind(ticket.Value!.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        Assert.True(world.TryGetLivePlayer(session, out var player));
        return (world, session, player);
    }

    [Fact]
    public void Spiritshot_multiplies_matk_like_mobius()
    {
        Assert.Equal(1d, ZoneCombat.SpiritshotMAtkMultiplier(0));
        Assert.Equal(2d, ZoneCombat.SpiritshotMAtkMultiplier(1));
        Assert.Equal(4d, ZoneCombat.SpiritshotMAtkMultiplier(2));
        var items = GameData.LoadDefault().Items;
        Assert.True(items.TryGet(2509, out var sps) && sps!.IsSpiritshot);
        Assert.True(items.TryGet(3947, out var bsps) && bsps!.IsBlessedSpiritshot);
        Assert.True(items.TryGet(6, out var wand) && wand!.Spiritshots > 0);
    }

    [Fact]
    public async Task Auto_soulshot_charges_now_recharges_after_discharge_and_turns_off_when_empty()
    {
        var (world, session, player) = await Enter();
        var bag = player.Inventory!;
        if (bag.GetEquipped(PaperdollSlot.RHand)?.Template.Soulshots is not > 0)
            bag.Equip(bag.Add(219)); // Sword Breaker: 2 soulshots per charge
        int perShot = bag.GetEquipped(PaperdollSlot.RHand)!.Template.Soulshots;
        bag.Add(1835, perShot * 3);
        int total = bag.FindByItemId(1835)!.Count;

        Assert.Null(await world.TryAutoSoulShotAsync(session, 1835, true, CancellationToken.None));
        Assert.Contains(1835, player.AutoShots);
        Assert.True(player.SoulshotCharged);
        Assert.Equal(total - perShot, bag.FindByItemId(1835)!.Count);

        // Discharge + auto recharge until the stack runs dry.
        world.DischargeSoulshot(player);
        Assert.True(player.SoulshotCharged);
        Assert.Equal(total - 2 * perShot, bag.FindByItemId(1835)?.Count ?? 0);
        int guard = 0;
        while (player.AutoShots.Contains(1835) && guard++ < 10_000)
            world.DischargeSoulshot(player);
        Assert.DoesNotContain(1835, player.AutoShots);

        Assert.Null(await world.TryAutoSoulShotAsync(session, 1835, false, CancellationToken.None));
        Assert.NotNull(await world.TryAutoSoulShotAsync(session, 57, true, CancellationToken.None)); // adena: not owned / not a shot
    }

    [Fact]
    public void Shot_templates_cover_d_c_grades_and_beast_ids()
    {
        var items = GameData.LoadDefault().Items;
        Assert.True(items.TryGet(1463, out var dss) && dss!.IsSoulshot && dss.ShotGrade == CrystalGrade.D);
        Assert.True(items.TryGet(1464, out var css) && css!.IsSoulshot && css.ShotGrade == CrystalGrade.C);
        Assert.True(items.TryGet(2510, out var dsps) && dsps!.IsSpiritshot && dsps.ShotGrade == CrystalGrade.D);
        Assert.True(items.TryGet(6645, out var beast) && ZoneWorld.IsBeastShot(beast!.Id) && ZoneWorld.IsShot(beast));
        Assert.True(items.TryGet(6646, out var bsps) && ZoneWorld.IsBeastShot(bsps!.Id));
        Assert.True(items.TryGet(6647, out var bbsps) && ZoneWorld.IsBeastShot(bbsps!.Id));
    }

    [Fact]
    public async Task D_grade_soulshot_auto_charges()
    {
        var (world, session, player) = await Enter();
        var bag = player.Inventory!;
        bag.Equip(bag.Add(69)); // Bastard Sword: D-grade, 2 soulshots
        int perShot = bag.GetEquipped(PaperdollSlot.RHand)!.Template.Soulshots;
        bag.Add(1463, perShot * 2);
        Assert.Null(await world.TryAutoSoulShotAsync(session, 1463, true, CancellationToken.None));
        Assert.Contains(1463, player.AutoShots);
        Assert.True(player.SoulshotCharged);
        Assert.Equal(perShot, bag.FindByItemId(1463)!.Count);
    }

    [Fact]
    public async Task Beast_shot_auto_requires_a_summon_and_does_not_charge_the_player()
    {
        var (world, session, player) = await Enter();
        player.Inventory!.Add(6645, 100);
        Assert.Equal(ShotRules.NoSummon, await world.TryAutoSoulShotAsync(session, 6645, true, CancellationToken.None));
        Assert.DoesNotContain(6645, player.AutoShots);

        player.SummonObjectId = 1;
        Assert.Null(await world.TryAutoSoulShotAsync(session, 6645, true, CancellationToken.None));
        Assert.Contains(6645, player.AutoShots);
        Assert.False(player.SoulshotCharged);
        Assert.Equal(0, player.SpiritshotCharge);
        Assert.Equal(100, player.Inventory.FindByItemId(6645)!.Count);
    }

    [Fact]
    public async Task Melee_soulshot_recharges_at_hit_time_not_at_swing_start()
    {
        var kit = new NetTestKit(Adjacent(), "ss-timing");
        var (session, player) = await kit.EnterAsync(classId: 0, race: 0);
        var bag = player.Inventory!;
        if (bag.GetEquipped(PaperdollSlot.RHand)?.Template.Soulshots is not > 0)
            bag.Equip(bag.Add(219));
        bag.Add(1835, 100);
        Assert.Null(await kit.World.TryAutoSoulShotAsync(session, 1835, true, CancellationToken.None));
        Assert.True(player.SoulshotCharged);

        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;

        Assert.Null(await kit.World.TrySelectTargetAsync(session, MobId, CancellationToken.None));
        Assert.Null(await kit.World.TryAttackAsync(session, MobId, CancellationToken.None));
        Assert.False(player.SoulshotCharged); // fired with the swing, not yet recharged
        Assert.Empty(rec.Of(nameof(IWorldEventPublisher.FanoutMagicSkillUse))); // glow waits for onHitTimer

        // Stop the auto loop so the next swing cannot consume the recharge in the same tick.
        player.Intent.AutoAttackTargetId = 0;
        kit.Now = kit.Now.AddSeconds(2);
        await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        Assert.True(player.SoulshotCharged); // onHitTimer recharge
        Assert.NotEmpty(rec.Of(nameof(IWorldEventPublisher.FanoutMagicSkillUse)));
    }

    const int MobId = 9_300_077;

    static Fineage.Zone.Spawns.SpawnCatalog Adjacent() =>
        Fineage.Zone.Spawns.SpawnCatalog.From(new Dictionary<string, Fineage.Zone.Spawns.SpawnMapDefinition>
        {
            ["ss-timing"] = new("ss-timing", true,
            [
                new Fineage.Zone.Spawns.SpawnNpcDefinition(MobId, 90_001, "Gremlin", NetTestKit.ElfX + 30, NetTestKit.ElfY, NetTestKit.ElfZ,
                    0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                    Level: 1, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        });
}
