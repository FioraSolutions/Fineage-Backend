using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class RaidCurseTests
{
    [Fact]
    public void Melee_on_overlevel_player_applies_turn_stone_curse()
    {
        var raid = RaidBoss(level: 30);
        var player = Player(level: 39);

        var bench = new WorldBench();
        Assert.True(bench.World.TryApplyRaidCurseForTest(raid, player, magicSkill: false));

        Assert.Contains(player.Abnormals, a => a.Id == 4515);
        Assert.True(ZoneWorld.IsActionBlocked(player));
    }

    [Fact]
    public void Magic_skill_path_applies_silence_curse()
    {
        var raid = RaidBoss(level: 30);
        var player = Player(level: 45);

        var bench = new WorldBench();
        Assert.True(bench.World.TryApplyRaidCurseForTest(raid, player, magicSkill: true));

        Assert.Contains(player.Abnormals, a => a.Id == 4215);
        Assert.True(ZoneWorld.IsSilenced(player));
        Assert.True(ZoneWorld.IsPhysicallyMuted(player));
    }

    [Fact]
    public void Level_gap_exactly_eight_does_not_curse()
    {
        var raid = RaidBoss(level: 30);
        var player = Player(level: 38);

        var bench = new WorldBench();
        Assert.False(bench.World.TryApplyRaidCurseForTest(raid, player, magicSkill: false));
        Assert.Empty(player.Abnormals);
    }

    [Fact]
    public void Normal_monster_never_curses()
    {
        var mob = new ZoneEntity
        {
            ObjectId = 2,
            Kind = ZoneEntityKind.Npc,
            NpcType = "Monster",
            Level = 20
        };
        var player = Player(level: 40);
        var bench = new WorldBench();
        Assert.False(bench.World.TryApplyRaidCurseForTest(mob, player, magicSkill: false));
    }

    static ZoneEntity RaidBoss(int level) => new()
    {
        ObjectId = 1,
        Kind = ZoneEntityKind.Npc,
        NpcType = "RaidBoss",
        Attackable = true,
        Level = level,
        MaxHp = 10_000,
        CurHp = 10_000
    };

    static ZoneEntity Player(int level) => new()
    {
        ObjectId = 100,
        Kind = ZoneEntityKind.Player,
        Session = new ZoneSession(),
        Level = level,
        MaxHp = 500,
        CurHp = 500
    };
}