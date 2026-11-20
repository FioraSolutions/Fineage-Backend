using Fineage.Zone.Combat;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius EffectList.add: same abnormalType is exclusive (equal/higher abnormalLevel replaces,
/// lower is rejected). Different types, or an empty type, stack (empty type replaces only the
/// same skill id).
/// </summary>
public sealed class ZoneAbnormalStackTests
{
    const string FocusDagger = "FOCUS_DAGGER";

    static List<ZoneAbnormalEntry> List() => new();

    [Fact]
    public void Same_slot_equal_level_replaces()
    {
        var list = List();
        Assert.True(ZoneAbnormals.Apply(list, 356, 1, 120, 0, 1, FocusDagger)); // Focus Chance
        Assert.True(ZoneAbnormals.Apply(list, 357, 1, 120, 0, 1, FocusDagger)); // Focus Power
        Assert.Single(list);
        Assert.Equal(357, list[0].Id);
        Assert.Equal(1, list[0].Value);
        Assert.Equal(FocusDagger, list[0].Slot);
    }

    [Fact]
    public void Same_slot_higher_level_replaces()
    {
        var list = List();
        Assert.True(ZoneAbnormals.Apply(list, 400, 1, 30, 0, 1, "HIT_DOWN"));
        Assert.True(ZoneAbnormals.Apply(list, 401, 2, 30, 0, 7, "HIT_DOWN"));
        Assert.Single(list);
        Assert.Equal(401, list[0].Id);
        Assert.Equal(2, list[0].Value);
    }

    [Fact]
    public void Same_slot_lower_level_is_rejected()
    {
        var list = List();
        Assert.True(ZoneAbnormals.Apply(list, 401, 3, 30, 0, 10, "HIT_DOWN"));
        Assert.False(ZoneAbnormals.Apply(list, 400, 2, 30, 0, 7, "HIT_DOWN"));
        Assert.Single(list);
        Assert.Equal(401, list[0].Id);
        Assert.Equal(3, list[0].Value);
    }

    [Fact]
    public void Different_slots_stack()
    {
        var list = List();
        Assert.True(ZoneAbnormals.Apply(list, 1068, 3, 1200, 0, 3, "PA_UP"));
        Assert.True(ZoneAbnormals.Apply(list, 1040, 3, 1200, 0, 3, "PD_UP"));
        Assert.Equal(2, list.Count);
        Assert.Contains(list, e => e.Id == 1068 && e.Slot == "PA_UP");
        Assert.Contains(list, e => e.Id == 1040 && e.Slot == "PD_UP");
    }

    [Fact]
    public void Empty_slot_replaces_same_skill_and_stacks_with_others()
    {
        var list = List();
        Assert.True(ZoneAbnormals.Apply(list, 286, 1, 15, 0, 1, ""));
        Assert.True(ZoneAbnormals.Apply(list, 1381, 1, 20, 0, 1, ""));
        Assert.True(ZoneAbnormals.Apply(list, 286, 1, 15, 1000, 2, ""));
        Assert.Equal(2, list.Count);
        var provoke = Assert.Single(list, e => e.Id == 286);
        Assert.Equal(2, provoke.SkillLevel);
        Assert.Contains(list, e => e.Id == 1381);
    }
}
