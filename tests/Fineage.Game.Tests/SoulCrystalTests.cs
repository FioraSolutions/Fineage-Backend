using Fineage.Game.Quests;
using Fineage.Game.Services;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Quest 350 Enhance Your Weapon: the magisters' pack and the soul crystal table.</summary>
public sealed class SoulCrystalTests
{
    static readonly GameData Data = GameData.LoadDefault();
    static readonly SoulCrystalCatalog Crystals = SoulCrystalCatalog.LoadDefault();
    static readonly QuestActor Me = new(0, 45, 1);

    [Fact]
    public void Table_matches_mobius_samples()
    {
        Assert.True(Crystals.IsCrystal(4629));
        Assert.True(Crystals.IsCrystal(5580));
        Assert.False(Crystals.IsCrystal(5908)); // grade 13 is the top
        Assert.Equal(new SoulAbsorbRule(9, true, SoulAbsorbMode.LastHit), Crystals.Rule(20583, 1));
        Assert.Null(Crystals.Rule(20583, 2));
        Assert.Equal(SoulAbsorbMode.FullParty, Crystals.Rule(29020, 10)!.Mode);
        Assert.Null(Crystals.Rule(29020, 9));
        Assert.True(Crystals.IsPartyLeveling(29020));
        Assert.False(Crystals.IsPartyLeveling(20583));
        foreach (int id in new[] { 4629, 4639, 5577, 5580, 5908, 5911, 5914 })
            Assert.True(Data.Items.TryGet(id, out _), $"item {id}");
    }

    [Fact]
    public void Only_a_single_crystal_counts()
    {
        var bag = new PlayerInventory(Data.Items);
        Assert.Null(Crystals.Single(bag));
        bag.Add(4630);
        Assert.Equal(1, Crystals.Single(bag)!.Level);
        bag.Add(4641);
        Assert.Null(Crystals.Single(bag));
    }

    [Fact]
    public void Magister_starts_the_quest_and_hands_one_crystal()
    {
        var m = new QuestMachine(Data.Quests, Data.Items);
        var j = new QuestJournal();
        var bag = new PlayerInventory(Data.Items);
        m.Talk(j, bag, 350, 30115, Me);
        m.AdvEvent(j, bag, 350, 30115, "30115-04", Me);
        Assert.Equal(QuestPhase.Started, j.Find(350)!.Phase);
        m.AdvEvent(j, bag, 350, 30115, "30115-10", Me);
        Assert.Equal(1, bag.CountOf(4640));
        m.AdvEvent(j, bag, 350, 30115, "30115-09", Me);
        Assert.Equal(0, bag.CountOf(4629));
        m.AdvEvent(j, bag, 350, 30856, "exit", Me);
        Assert.Null(j.Find(350));
    }

    [Fact]
    public void Low_level_cannot_start() =>
        Assert.False(new QuestMachine(Data.Quests, Data.Items)
            .AdvEvent(new QuestJournal(), new PlayerInventory(Data.Items), 350, 30194, "30194-04", new QuestActor(0, 39, 1)).Ok);
}
