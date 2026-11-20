using Fineage.Game;
using Fineage.Zone.Geo;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Unlock (27) — <c>OpenChest</c>. Interlude: skill 27 always opens a real box (ids 18265–18298);
/// a mimic (other Chest npcs) turns on the caster; a failed roll despawns the box with no loot;
/// success kills it with its own drop table and no exp. Melee or a damaging skill on a still-closed
/// box remaps the drop id (18265 → 21801).
/// </summary>
public sealed class ZoneUnlockTests
{
    const int Unlock = 27;
    const int ThiefKey = 1661;
    const int BoxId = 9_400_727;
    const int MimicId = 9_400_728;
    const int MobId = 9_400_729;
    const int BoxNpc = 18265;
    const int MimicNpc = 21801;

    sealed class AlwaysRoll : Random
    {
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => 0d;
    }

    sealed class NeverRoll : Random
    {
        public override int Next(int maxValue) => maxValue > 0 ? maxValue - 1 : 0;
        public override int Next(int minValue, int maxValue) => maxValue > minValue ? maxValue - 1 : minValue;
        public override double NextDouble() => 0.99d;
    }

    static SpawnNpcDefinition Box(int objectId, int npcId, int dx, int level, string name, long exp = 0) =>
        new(objectId, npcId, name,
            NetTestKit.ElfX + dx, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 1_000, 1f, 80f, 0, 0, "", "Chest",
            Level: level, Exp: exp, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0);

    static SpawnNpcDefinition Monster(int objectId, int dx) =>
        new(objectId, 90000 + 729, "Gremlin",
            NetTestKit.ElfX + dx, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
            Level: 21, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0);

    static (NetTestKit Kit, NetBridgeRecorder Rec) Kit(Random? combatRandom = null, Random? dropRandom = null)
    {
        var kit = new NetTestKit(SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["unlock"] = new("unlock", true,
            [
                Box(BoxId, BoxNpc, 20, 21, "Treasure Chest"),
                Box(MimicId, MimicNpc, 40, 21, "Mimic Chest", exp: 630),
                Monster(MobId, 60)
            ])
        }), "unlock", geo: GeoEngine.Empty, combatRandom: combatRandom ?? new AlwaysRoll(),
            dropRandom: dropRandom ?? new AlwaysRoll());
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, rec);
    }

    static async Task<(NetTestKit Kit, NetBridgeRecorder Rec, ZoneSession Session, ZoneEntity Player, ZoneEntity Box, ZoneEntity Mimic, ZoneEntity Mob)>
        EnterAsync(Random? combatRandom = null, int unlockLevel = 1, int keys = 10)
    {
        var (kit, rec) = Kit(combatRandom);
        var (session, player) = await kit.EnterAsync(classId: 93, race: 0, level: 40);
        player.KnownSkills[Unlock] = unlockLevel;
        player.CurMp = player.MaxMp = 2_000;
        if (keys > 0)
            player.Inventory!.Add(ThiefKey, keys);
        Assert.True(kit.World.TryGetEntityById(BoxId, out var box));
        Assert.True(kit.World.TryGetEntityById(MimicId, out var mimic));
        Assert.True(kit.World.TryGetEntityById(MobId, out var mob));
        box.MaxHp = box.CurHp = 1_000;
        mimic.MaxHp = mimic.CurHp = 5_000;
        return (kit, rec, session, player, box, mimic, mob);
    }

    static void Resolve(NetTestKit kit, ZoneEntity player)
    {
        kit.Now = kit.Now.AddSeconds(10);
        kit.World.ResolvePendingHitForTest(player);
    }

    [Fact]
    public void Unlock_data_matches_mobius_ct0()
    {
        var skill = GameData.LoadDefault().Skills.Find(Unlock);
        Assert.NotNull(skill);
        Assert.Equal("UNLOCKABLE", skill!.TargetType);
        Assert.Equal(40, skill.CastRange);
        Assert.Equal(2500, skill.HitTimeMs);
        Assert.True(skill.TryLevelData(1, out var lv1));
        Assert.Equal(ThiefKey, lv1.ItemConsumeId);
        Assert.Equal(2, lv1.ItemConsumeCount);
        Assert.True(skill.TryLevelData(14, out var lv14));
        Assert.Equal(17, lv14.ItemConsumeCount);
    }

    [Fact]
    public void Open_chance_matches_interlude_table()
    {
        Assert.Equal(100, ZoneWorld.OpenChestSuccessChance(27, 1, 21));
        Assert.Equal(100, ZoneWorld.OpenChestSuccessChance(27, 14, 78));
        Assert.Equal(100, ZoneWorld.OpenChestSuccessChance(2322, 1, 78));
        Assert.Equal(90, ZoneWorld.OpenChestSuccessChance(3155, 1, 78));
        // 2065 baseline 60, 2229 baseline 100; each grade step off chestLevel/10 costs 40.
        Assert.Equal(60, ZoneWorld.OpenChestSuccessChance(2065, 2, 21));
        Assert.Equal(20, ZoneWorld.OpenChestSuccessChance(2065, 1, 21));
        Assert.Equal(100, ZoneWorld.OpenChestSuccessChance(2229, 2, 21));
        Assert.Equal(60, ZoneWorld.OpenChestSuccessChance(2229, 1, 21));
        Assert.Equal(-180, ZoneWorld.OpenChestSuccessChance(2065, 1, 78));
    }

    [Fact]
    public void Smash_drop_id_remaps_boxes_onto_the_mimic_table()
    {
        Assert.Equal(18265, ZoneWorld.ChestDropNpcId(18265, specialDrop: true));
        Assert.Equal(21801, ZoneWorld.ChestDropNpcId(18265, specialDrop: false));
        Assert.Equal(21822, ZoneWorld.ChestDropNpcId(18286, specialDrop: false));
        Assert.Equal(21671, ZoneWorld.ChestDropNpcId(18287, specialDrop: false));
        Assert.Equal(21786, ZoneWorld.ChestDropNpcId(18298, specialDrop: false));
        Assert.Equal(21801, ZoneWorld.ChestDropNpcId(21801, specialDrop: false));
    }

    [Fact]
    public async Task Unlock_opens_a_box_spends_keys_and_drops_its_own_table()
    {
        var (kit, rec, session, player, box, _, _) = await EnterAsync();
        int keys = player.Inventory!.CountOf(ThiefKey);

        Assert.Null(await kit.World.TrySkillUseAsync(session, Unlock, BoxId, CancellationToken.None));
        Resolve(kit, player);

        Assert.Equal(keys - 2, player.Inventory.CountOf(ThiefKey));
        Assert.True(box.CurHp <= 0);
        Assert.True(kit.World.TryGetEntityById(BoxId, out _));
        Assert.Equal(BoxNpc, kit.World.LastDeathDropNpcIdForTest);
        Assert.True(kit.World.GroundItemCountForTest() >= 1, "successful Unlock should drop the chest's own table");
        Assert.Contains(rec.Of("FanoutSocialAction"), args =>
            args.Length >= 3 && Equals(args[1], BoxId) && Equals(args[2], ZoneWorld.SocialChestOpenOk));
    }

    [Fact]
    public async Task Unlock_without_thief_keys_is_refused_and_leaves_the_chest()
    {
        var (kit, rec, session, player, box, _, _) = await EnterAsync(keys: 0);
        int mp = player.CurMp;

        Assert.Null(await kit.World.TrySkillUseAsync(session, Unlock, BoxId, CancellationToken.None));

        Assert.Equal(mp, player.CurMp);
        Assert.Null(player.PendingHit);
        Assert.Equal(1_000, box.CurHp);
        Assert.Equal(0, kit.World.GroundItemCountForTest());
    }

    [Fact]
    public async Task Unlock_on_a_monster_is_an_incorrect_target()
    {
        var (kit, _, session, player, box, _, mob) = await EnterAsync();
        int keys = player.Inventory!.CountOf(ThiefKey);
        int mp = player.CurMp;

        Assert.Null(await kit.World.TrySkillUseAsync(session, Unlock, MobId, CancellationToken.None));

        Assert.Equal(mp, player.CurMp);
        Assert.Equal(keys, player.Inventory.CountOf(ThiefKey));
        Assert.Null(player.PendingHit);
        Assert.Equal(1_000, box.CurHp);
        Assert.True(mob.CurHp > 0);
        Assert.Equal(0, mob.AggroTargetId);
    }

    [Fact]
    public async Task Failed_open_despawns_the_box_with_no_loot()
    {
        var (kit, rec, session, player, box, _, _) = await EnterAsync();
        // Skill 27 is 100% in Interlude, so the fail path is forced here.
        kit.World.ApplyOpenChestForTest(player, box, Unlock, 1, forceSuccess: false);

        Assert.False(kit.World.TryGetEntityById(BoxId, out _));
        Assert.Equal(0, kit.World.GroundItemCountForTest());
        Assert.Contains(rec.Of("FanoutSocialAction"), args =>
            args.Length >= 3 && Equals(args[1], BoxId) && Equals(args[2], ZoneWorld.SocialChestOpenFail));
    }

    [Fact]
    public async Task Unlock_on_a_mimic_provokes_it_and_does_not_kill_it()
    {
        var (kit, rec, session, player, _, mimic, _) = await EnterAsync();
        int keys = player.Inventory!.CountOf(ThiefKey);

        Assert.Null(await kit.World.TrySkillUseAsync(session, Unlock, MimicId, CancellationToken.None));
        Resolve(kit, player);

        Assert.Equal(keys - 2, player.Inventory.CountOf(ThiefKey));
        Assert.True(mimic.CurHp > 0);
        Assert.Equal(player.ObjectId, mimic.AggroTargetId);
        Assert.Equal(0, kit.World.GroundItemCountForTest());
        Assert.Contains(rec.Of("FanoutSocialAction"), args =>
            args.Length >= 3 && Equals(args[1], MimicId) && Equals(args[2], ZoneWorld.SocialChestOpenFail));
    }

    [Fact]
    public async Task Second_unlock_on_an_already_opened_mimic_is_refused()
    {
        var (kit, _, session, player, _, mimic, _) = await EnterAsync();
        Assert.Null(await kit.World.TrySkillUseAsync(session, Unlock, MimicId, CancellationToken.None));
        Resolve(kit, player);
        int keys = player.Inventory!.CountOf(ThiefKey);
        int mp = player.CurMp;
        // Past Mobius Unlock reuse (120 s); the refusal we want is "already opened", not skill_reuse.
        kit.Now = kit.Now.AddSeconds(130);

        Assert.Null(await kit.World.TrySkillUseAsync(session, Unlock, MimicId, CancellationToken.None));

        Assert.Equal(keys, player.Inventory.CountOf(ThiefKey));
        Assert.Equal(mp, player.CurMp);
        Assert.Null(player.PendingHit);
        Assert.True(mimic.ChestInteracted);
    }

    [Fact]
    public async Task Melee_on_a_closed_box_explodes_it_with_the_smashed_drop_id()
    {
        var (kit, _, session, player, box, _, _) = await EnterAsync();

        kit.World.DamageNpcByForTest(player, box, 1);

        Assert.True(box.CurHp <= 0);
        Assert.Equal(21801, kit.World.LastDeathDropNpcIdForTest);
        // Smash tables 21801–21822 are not in data/drops yet, so no ground loot is expected.
        Assert.Equal(0, kit.World.GroundItemCountForTest());
    }

    [Fact]
    public async Task Melee_on_a_mimic_does_not_explode_it()
    {
        var (kit, _, session, player, _, mimic, _) = await EnterAsync();
        int hp = mimic.CurHp;

        kit.World.DamageNpcByForTest(player, mimic, 10);

        Assert.Equal(hp - 10, mimic.CurHp);
        Assert.False(mimic.ChestInteracted);
        Assert.Equal(player.ObjectId, mimic.AggroTargetId);
    }
}
