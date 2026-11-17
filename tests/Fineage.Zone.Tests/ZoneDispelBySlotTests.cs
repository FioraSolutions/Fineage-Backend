using Fineage.Game;
using Fineage.Game.Skills;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Geo;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius <c>DispelBySlot</c> for the Human dagger line:
/// <list type="bullet">
/// <item>Escape Shackle (453): self, <c>ROOT_PHYSICALLY,3</c> — breaks a root and frees movement.</item>
/// <item>Break Duress (461): per-level table (<c>ROOT_PHYSICALLY,3;ROOT_MAGICALLY,3</c> / <c>+SPEED_DOWN,3</c>).</item>
/// <item>Cure Poison (1012): a level table (<c>#dispelAbnormals</c>) expands to one list per level.</item>
/// </list>
/// A cleanse used to land in the heal path and throw on the missing <c>power</c>, so nothing was ever removed.
/// </summary>
public sealed class ZoneDispelBySlotTests
{
    const int EscapeShackle = 453;
    const int BreakDuress = 461;
    const int CurePoison = 1012;
    const int MobId = 9_400_521;

    static (NetTestKit Kit, NetBridgeRecorder Rec) Kit()
    {
        var kit = new NetTestKit(SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["dispel"] = new("dispel", true,
            [
                new SpawnNpcDefinition(MobId, 90000 + 521, "Gremlin",
                    NetTestKit.ElfX + 30, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                    Level: 40, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        }), "dispel", geo: GeoEngine.Empty);
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, rec);
    }

    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Player, ZoneEntity Mob, NetBridgeRecorder Rec)>
        EnterAsync()
    {
        var (kit, rec) = Kit();
        var (session, player) = await kit.EnterAsync(classId: 7, race: 0, level: 60); // Rogue
        Assert.True(kit.World.TryGetEntityById(MobId, out var mob));
        mob.MaxHp = mob.CurHp = 50_000;
        return (kit, session, player, mob, rec);
    }

    [Fact]
    public void Escape_shackle_row_carries_the_root_dispel()
    {
        var effects = SkillEffectCatalog.LoadDefault();
        Assert.True(effects.TryDispelBySlot(EscapeShackle, out var rows));
        var row = Assert.Single(rows);
        Assert.Equal("ROOT_PHYSICALLY,3", row.DispelAt(1));
    }

    [Fact]
    public void Break_duress_level_table_expands_per_level()
    {
        var effects = SkillEffectCatalog.LoadDefault();
        Assert.True(effects.TryDispelBySlot(BreakDuress, out var rows));
        var row = Assert.Single(rows);
        Assert.Equal(2, row.Dispel.Count);
        Assert.Equal("ROOT_PHYSICALLY,3;ROOT_MAGICALLY,3", row.DispelAt(1));
        Assert.Contains("SPEED_DOWN,3", row.DispelAt(2));
    }

    [Fact]
    public void Cure_poison_table_gives_one_slot_list_per_level()
    {
        var effects = SkillEffectCatalog.LoadDefault();
        Assert.True(effects.TryDispelBySlot(CurePoison, out var rows));
        var row = Assert.Single(rows);
        Assert.Equal(3, row.Dispel.Count);
        Assert.Equal("POISON,3", row.DispelAt(1));
        Assert.Equal("POISON,7", row.DispelAt(2));
        Assert.Equal("POISON,9", row.DispelAt(3));
        // Above the table the last entry is used, as Mobius reads the level index defensively.
        Assert.Equal("POISON,9", row.DispelAt(5));
    }

    [Fact]
    public void Dispel_by_slot_removes_only_the_named_slot()
    {
        var entry = new ZoneAbnormalEntry { Id = 1, Slot = "ROOT_PHYSICALLY", ExpiresAtUnixMs = long.MaxValue };
        var keep = new ZoneAbnormalEntry { Id = 2, Slot = "POISON", ExpiresAtUnixMs = long.MaxValue };
        var list = new List<ZoneAbnormalEntry> { entry, keep };
        Assert.Equal(1, ZoneAbnormals.DispelBySlot(list, "root_physically", count: 3));
        Assert.Equal(keep.Id, Assert.Single(list).Id);
    }

    [Fact]
    public async Task Escape_shackle_clears_a_root_and_keeps_mp_flow()
    {
        var (kit, session, player, _, rec) = await EnterAsync();
        player.KnownSkills[EscapeShackle] = 1;
        player.CurMp = player.MaxMp = 500;
        ZoneAbnormals.Apply(player.Abnormals, effectId: 1206, value: 1, durationSeconds: 30,
            nowUnixMs: kit.Now.ToUnixTimeMilliseconds(), skillLevel: 1, slot: "ROOT_PHYSICALLY");
        Assert.Contains(player.Abnormals, a => a.Slot == "ROOT_PHYSICALLY");

        int mp = player.CurMp;
        var cast = await kit.World.TrySkillUseAsync(session, EscapeShackle, 0, CancellationToken.None);
        Assert.Null(cast);
        Assert.True(player.CurMp < mp, "the cleanse must cost MP");

        // Hit time is 4 s in skills.json; the scheduled effect fires when the pending cast resolves.
        kit.Now = kit.Now.AddSeconds(5);
        kit.World.ResolvePendingHitForTest(player);

        Assert.DoesNotContain(player.Abnormals, a => a.Slot == "ROOT_PHYSICALLY");
        Assert.NotEmpty(rec.Of(nameof(IWorldEventPublisher.FanoutAbnormalStatusUpdate)));
    }

    [Fact]
    public async Task Escape_shackle_without_a_root_is_a_harmless_cast()
    {
        var (kit, session, player, _, _) = await EnterAsync();
        player.KnownSkills[EscapeShackle] = 1;
        player.CurMp = player.MaxMp = 500;
        Assert.Null(await kit.World.TrySkillUseAsync(session, EscapeShackle, 0, CancellationToken.None));
        kit.Now = kit.Now.AddSeconds(5);
        kit.World.ResolvePendingHitForTest(player);
        Assert.Empty(player.Abnormals);
    }
}
