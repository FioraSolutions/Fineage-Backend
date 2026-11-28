using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Npcs;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius <c>RestorationRandom</c> (Summon Treasure Key 419), the <c>EffectPoint</c> signets (Battle Force 454–460) and
/// the generic <c>itemConsumeId</c> precondition.
/// <para>
/// 419 used to crash in the heal branch (<c>power=0</c>) and 460 was refused with <c>skill_target</c> in the attack
/// branch, so the tests pin both routings plus the bag/weight preconditions Mobius applies.
/// </para>
/// </summary>
public sealed class RestorationAndSignetTests
{
    const int SummonTreasureKey = 419;
    const int SymbolOfTheAssassin = 460;
    const int ThiefKey = 1661;
    const int BattleSymbol = 8875;
    const int ChestKeyGrade5 = 6669;
    const int SignetNpc = 13024;
    const int DebuffSignetNpc = 13025; // CT 0: Day of Doom signet NPC (HighFive numbered it 13028; CT 0 13028 is Cyclone)
    const int DebuffSignetSkill = 5145;
    const int BattleForce = 5104;

    static (NetTestKit Kit, ZoneSession S, ZoneEntity P) Enter(NetTestKit? kit = null)
    {
        kit ??= new NetTestKit();
        var (bridge, _) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        var (s, p) = kit.EnterAsync(classId: 93).GetAwaiter().GetResult();
        p.KnownSkills[SummonTreasureKey] = 4;
        p.KnownSkills[SymbolOfTheAssassin] = 1;
        p.CurMp = p.MaxMp = 2000;
        return (kit, s, p);
    }

    static void Grant(ZoneEntity player, int itemId, int count)
    {
        player.Inventory!.Add(itemId, count);
    }

    /// <summary>Resolves the cast's deferred effect without waiting for a server tick.</summary>
    static void Resolve(NetTestKit kit, ZoneEntity player)
    {
        kit.Now = kit.Now.AddSeconds(10);
        kit.World.ResolvePendingHitForTest(player);
    }

    [Fact]
    public void Skill_419_spends_two_thief_keys_and_hands_out_a_chest_key()
    {
        var (kit, s, p) = Enter();
        Grant(p, ThiefKey, 4);

        Assert.Null(kit.World.TrySkillUseAsync(s, SummonTreasureKey, 0, CancellationToken.None)
            .GetAwaiter().GetResult());
        Resolve(kit, p);

        Assert.Equal(2, p.Inventory!.CountOf(ThiefKey));
        int keys = 0;
        foreach (int id in new[] { 6669, 6670, 6671, 6672 })
            keys += p.Inventory.CountOf(id);
        // Level 4 hands out one of four grade segments; each segment carries 1–4 keys.
        Assert.InRange(keys, 1, 4);
        Assert.Equal(0, p.PendingHit == null ? 0 : 1);
    }

    [Fact]
    public void Skill_419_without_thief_keys_is_refused_and_costs_no_mp()
    {
        var (kit, s, p) = Enter();
        int mp = p.CurMp;

        Assert.Null(kit.World.TrySkillUseAsync(s, SummonTreasureKey, 0, CancellationToken.None)
            .GetAwaiter().GetResult());

        Assert.Equal(mp, p.CurMp);
        Assert.Null(p.PendingHit);
    }

    [Fact]
    public void Skill_419_refuses_when_the_bag_has_fewer_than_ten_free_slots()
    {
        var (kit, s, p) = Enter();
        Grant(p, ThiefKey, 2);
        int limit = WarehouseRules.InventorySlots(p.Race);
        // One distinct template per free slot gives a deterministic slot count.
        var fillers = kit.Game.Items.Templates
            .Where(t => t.Kind == Fineage.Game.ItemKind.Etc && !t.Stackable)
            .Select(t => t.Id)
            .Where(id => id != ThiefKey && p.Inventory!.CountOf(id) == 0)
            .ToList();
        int filled = p.Inventory!.Items.Count;
        foreach (int id in fillers)
        {
            if (filled >= limit - 5)
                break;
            p.Inventory.Add(id, 1);
            filled++;
        }
        Assert.True(filled >= limit - 5, $"only {filled}/{limit} slots filled");
        int mp = p.CurMp;

        Assert.Null(kit.World.TrySkillUseAsync(s, SummonTreasureKey, 0, CancellationToken.None)
            .GetAwaiter().GetResult());

        Assert.Equal(mp, p.CurMp);
        Assert.Equal(2, p.Inventory.CountOf(ThiefKey));
    }

    [Fact]
    public void Skill_460_spends_a_battle_symbol_and_plants_the_signet()
    {
        var (kit, s, p) = Enter();
        Grant(p, BattleSymbol, 1);
        // Mobius: Symbol of the Assassin requires Battle Force lv2+.
        long now = kit.Now.ToUnixTimeMilliseconds();
        p.Abnormals.Add(new ZoneAbnormalEntry
        {
            Id = BattleForce,
            Value = 2,
            SkillLevel = 2,
            Slot = "PHYSICAL_STANCE",
            ExpiresAtUnixMs = now + 15_000
        });

        Assert.Null(kit.World.TrySkillUseAsync(s, SymbolOfTheAssassin, 0, CancellationToken.None)
            .GetAwaiter().GetResult());
        Resolve(kit, p);

        Assert.Equal(0, p.Inventory!.CountOf(BattleSymbol));
        var signet = SignetOf(kit, p);
        Assert.NotNull(signet);
        Assert.Equal(SignetNpc, signet!.NpcId);
        Assert.False(signet.Attackable);
        Assert.Equal(p.X, signet.X);
        Assert.Equal(p.Y, signet.Y);
    }

    [Fact]
    public void Signet_carries_the_template_despawn_time_and_aura_range()
    {
        var (kit, _, p) = Enter();
        var signet = kit.World.SpawnSignetForTest(p, SignetNpc);

        Assert.NotNull(signet);
        Assert.True(signet!.QuestSpawn!.DespawnAtMs > kit.Now.ToUnixTimeMilliseconds());
        // Mobius <param name="despawn_time" value="120"/> in 13024's template.
        Assert.Equal(120_000,
            signet.QuestSpawn.DespawnAtMs - kit.Now.ToUnixTimeMilliseconds());
        // The signet AI buffs 5129 (Smokescreen) inside its 200 unit affect range.
        Assert.Equal(200, kit.World.SignetAffectRangeForTests(SignetNpc));
        // Mobius EffectPoint overrides the template's "attackable": nothing can target or hit the symbol.
        Assert.False(signet.Attackable);
    }

    [Fact]
    public void Signet_leaves_when_its_despawn_timer_expires()
    {
        var (kit, _, p) = Enter();
        var signet = kit.World.SpawnSignetForTest(p, SignetNpc);
        Assert.NotNull(signet);

        kit.World.TickQuestSpawnsForTest(kit.Now.AddSeconds(121).ToUnixTimeMilliseconds());

        Assert.False(kit.World.TryGetEntityForTest(signet!.ObjectId, out _));
    }

    [Fact]
    public void Signet_pulse_buffs_players_inside_the_affect_range_only()
    {
        var (kit, s, p) = Enter();
        var (_, near) = kit.EnterAsync(classId: 93, x: p.X + 50, y: p.Y, z: p.Z).GetAwaiter().GetResult();
        var (_, far) = kit.EnterAsync(classId: 93, x: p.X + 900, y: p.Y, z: p.Z).GetAwaiter().GetResult();
        Assert.NotNull(kit.World.SpawnSignetForTest(p, SignetNpc));

        kit.World.TickSignetsForTest(kit.Now.AddSeconds(6).ToUnixTimeMilliseconds());

        Assert.Contains(near.Abnormals, a => a.Id == 5129);
        Assert.Contains(p.Abnormals, a => a.Id == 5129);
        Assert.DoesNotContain(far.Abnormals, a => a.Id == 5129);
        _ = s;
    }

    [Fact]
    public void Debuff_signet_pulses_abnormal_on_attackable_npcs_in_range()
    {
        var (kit, _, p) = Enter();
        var mob = kit.World.SpawnMonsterForTests(20001, p.X + 50, p.Y, p.Z);
        Assert.NotNull(kit.World.SpawnSignetForTest(p, DebuffSignetNpc));

        kit.World.TickSignetsForTest(kit.Now.AddSeconds(6).ToUnixTimeMilliseconds());

        Assert.Contains(mob.Abnormals, a => a.Id == DebuffSignetSkill);
    }

    static ZoneEntity? SignetOf(NetTestKit kit, ZoneEntity owner) =>
        kit.World.SpawnedNpcsForTest().FirstOrDefault(e => e.NpcId == SignetNpc &&
            e.QuestSpawn != null && e.QuestSpawn.OwnerObjectId == owner.ObjectId);
}
