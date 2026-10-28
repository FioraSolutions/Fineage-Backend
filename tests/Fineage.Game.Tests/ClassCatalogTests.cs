using Fineage.Game;
using Xunit;

namespace Fineage.Game.Tests;

public class ClassCatalogTests
{
    static readonly int[] InterludeIds =
    [
        .. Enumerable.Range(0, 58),
        .. Enumerable.Range(88, 31)
    ];

    [Fact]
    public void Every_interlude_class_loads()
    {
        var catalog = GameData.LoadDefault().Classes!;
        Assert.Equal(InterludeIds, catalog.All.Select(row => row.ClassId).ToArray());
        Assert.Equal(89, catalog.All.Count);
        foreach (var row in catalog.All)
        {
            Assert.Equal(ClassCatalog.LevelCount, row.LevelCount);
            Assert.NotEmpty(row.Spawns);
            Assert.InRange(row.Race, 0, 4);
            Assert.True(row.Str > 0 && row.Dex > 0 && row.Con > 0 && row.Int > 0 && row.Wit > 0 && row.Men > 0);
            Assert.True(row.PAtk > 0 && row.MAtk > 0 && row.PDef > 0 && row.MDef > 0);
            Assert.Equal((int)(Math.Sqrt(row.Dex) * 6d + 1d), row.Accuracy);
            Assert.Equal(300, row.PAtkSpd);
            Assert.Equal(333, row.MAtkSpd);
            Assert.Equal(4, row.CritRate);
            var chain = catalog.ParentChain(row.ClassId);
            Assert.Equal(row.ClassId, chain[^1]);
            Assert.True(catalog.Get(chain[0]).IsBase);
            Assert.Equal(chain.Count - 1, row.ClassLevel);
        }
    }

    [Fact]
    public void Human_fighter_and_mystic_match_mobius_level_one()
    {
        var catalog = GameData.LoadDefault().Classes!;
        var fighter = catalog.Get(0);
        Assert.Equal("Human Fighter", fighter.Name);
        Assert.Equal(-1, fighter.ParentClassId);
        Assert.True(fighter.IsBase);
        Assert.Equal(0, fighter.Race);
        Assert.Equal(40, fighter.Str);
        Assert.Equal(30, fighter.Dex);
        Assert.Equal(43, fighter.Con);
        Assert.Equal(21, fighter.Int);
        Assert.Equal(11, fighter.Wit);
        Assert.Equal(25, fighter.Men);
        Assert.Equal(4, fighter.PAtk);
        Assert.Equal(6, fighter.MAtk);
        Assert.Equal(80, fighter.PDef);
        Assert.Equal(41, fighter.MDef);
        Assert.Equal(33, fighter.Accuracy);
        Assert.Equal(115, fighter.RunSpeed);
        Assert.Equal(80, fighter.WalkSpeed);
        Assert.Equal(9d, fighter.CollisionMale.Radius);
        Assert.Equal(23d, fighter.CollisionMale.Height);
        Assert.Equal(8d, fighter.CollisionFemale.Radius);
        Assert.Equal(23.5d, fighter.CollisionFemale.Height);
        Assert.Equal(new MapPoint(-71338, 258271, -3104), fighter.Spawns[0]);
        Assert.Equal(80d, fighter.HpGain(1));
        Assert.Equal(30d, fighter.MpGain(1));
        Assert.Equal(32d, fighter.CpGain(1));
        Assert.Equal(91.83d, fighter.HpGain(2), precision: 5);
        Assert.Equal(1.58d, catalog.StatBonuses.Bonus(StatSlot.Con, 43), precision: 5);
        Assert.Equal(1.28d, catalog.StatBonuses.Bonus(StatSlot.Men, 25), precision: 5);
        Assert.Equal(126, catalog.ModifiedHp(0, 1));
        Assert.Equal(38, catalog.ModifiedMp(0, 1));
        Assert.Equal(50, catalog.ModifiedCp(0, 1));

        var mystic = catalog.Get(10);
        Assert.Equal(22, mystic.Str);
        Assert.Equal(21, mystic.Dex);
        Assert.Equal(27, mystic.Con);
        Assert.Equal(41, mystic.Int);
        Assert.Equal(20, mystic.Wit);
        Assert.Equal(39, mystic.Men);
        Assert.Equal(3, mystic.PAtk);
        Assert.Equal(54, mystic.PDef);
        Assert.Equal(120, mystic.RunSpeed);
        Assert.Equal(78, mystic.WalkSpeed);
        Assert.Equal(7.5d, mystic.CollisionMale.Radius);
        Assert.Equal(22.8d, mystic.CollisionMale.Height, precision: 5);
        Assert.Equal(6.5d, mystic.CollisionFemale.Radius);
        Assert.Equal(22.5d, mystic.CollisionFemale.Height);
        Assert.Equal(new MapPoint(-90875, 248162, -3570), mystic.Spawns[0]);
        Assert.Equal(101d, mystic.HpGain(1));
        Assert.Equal(40d, mystic.MpGain(1));
        Assert.Equal(0.98d, catalog.StatBonuses.Bonus(StatSlot.Con, 27), precision: 5);
        Assert.Equal(1.48d, catalog.StatBonuses.Bonus(StatSlot.Men, 39), precision: 5);
        Assert.Equal(98, catalog.ModifiedHp(10, 1));
        Assert.Equal(59, catalog.ModifiedMp(10, 1));
    }

    [Fact]
    public void Other_races_and_professions_keep_parent_race_and_chain()
    {
        var catalog = GameData.LoadDefault().Classes!;
        Assert.Equal([0, 10, 18, 25, 31, 38, 44, 49, 53], catalog.BaseClassIds);
        Assert.Equal([0, 1, 2, 3, 4], catalog.BaseRaces);
        Assert.True(catalog.CreationListMatchesBaseClasses(catalog.BaseClassIdSet));

        var elf = catalog.Get(18);
        Assert.Equal(1, elf.Race);
        Assert.Equal(36, elf.Str);
        Assert.Equal(35, elf.Dex);
        Assert.Equal(125, elf.RunSpeed);
        Assert.Equal(90, elf.WalkSpeed);
        Assert.Equal(new MapPoint(46045, 41251, -3440), elf.Spawns[0]);
        Assert.Equal(elf.Spawns[0], catalog.Get(25).Spawns[0]);

        var dark = catalog.Get(31);
        Assert.Equal(2, dark.Race);
        Assert.Equal(41, dark.Str);
        Assert.Equal(new MapPoint(28295, 11063, -4224), dark.Spawns[0]);

        var orc = catalog.Get(44);
        Assert.Equal(3, orc.Race);
        Assert.Equal(47, orc.Con);
        Assert.Equal(11d, orc.CollisionMale.Radius);
        Assert.Equal(28d, orc.CollisionMale.Height);
        Assert.Equal(new MapPoint(-56733, -113459, -690), orc.Spawns[0]);
        Assert.Equal(orc.Collision(0), orc.CollisionMale);
        Assert.Equal(orc.Collision(1), orc.CollisionFemale);

        var dwarf = catalog.Get(53);
        Assert.Equal(4, dwarf.Race);
        Assert.Equal(39, dwarf.Str);
        Assert.Equal(10, dwarf.Wit);
        Assert.Equal(new MapPoint(108644, -173947, -400), dwarf.Spawns[0]);

        var warrior = catalog.Get(1);
        Assert.False(warrior.IsBase);
        Assert.Equal(1, warrior.ClassLevel);
        Assert.Equal(0, warrior.ParentClassId);
        Assert.Equal(64d, warrior.CpGain(1));
        Assert.Equal([0, 1, 2], catalog.ParentChain(2));
        Assert.Equal([0, 1, 2, 88], catalog.ParentChain(88));
        Assert.Equal(32, catalog.Get(34).ParentClassId);
        Assert.Equal([31, 32, 34], catalog.ParentChain(34));
        Assert.Equal(28, catalog.Get(104).ParentClassId);
        Assert.Equal([25, 26, 28, 104], catalog.ParentChain(104));
        Assert.Throws<InvalidDataException>(() => catalog.EnsureCreatable([1]));
    }

    [Fact]
    public void Experience_table_matches_mobius_checkpoints()
    {
        var experience = GameData.LoadDefault().Classes!.Experience;
        Assert.Equal(80, experience.MaxLevel);
        Assert.Equal(80, experience.MaxPetLevel);
        Assert.Equal(0, experience.ExpToBeLevel(1));
        Assert.Equal(68, experience.ExpToBeLevel(2));
        Assert.Equal(48229, experience.ExpToBeLevel(10));
        Assert.Equal(4_200_000_000L, experience.ExpToBeLevel(80));
        Assert.Equal(6_300_000_000L, experience.ExpToBeLevel(81));
    }
}
