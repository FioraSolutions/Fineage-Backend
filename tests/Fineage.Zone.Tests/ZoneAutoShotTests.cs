using Fineage.Game;
using Fineage.Accounts;
using Fineage.Login;
using Fineage.Zone.Combat;
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
        var world = new ZoneWorld(accounts: store);
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
}
