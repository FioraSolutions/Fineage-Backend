using Fineage.Net.Contract;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Net enter-world UserInfo carries the live speeds, i.e. the class template base already multiplied by the
/// <c>FuncMoveSpeed</c> DEX bonus (Mobius <c>CreatureStat.getRunSpeed()</c> → <c>calcStat(Stat.MOVE_SPEED, …)</c>,
/// <c>FuncMoveSpeed.java:34-45</c>). Human Fighter DEX 30 → ×1.1, Human Mystic DEX 21 → ×1.01
/// (<c>data/stat-bonuses.json</c> = <c>statBonus.xml</c>) — the wire value is not the raw template number.
/// </summary>
public sealed class NetClassSpeedTests
{
    [Theory]
    [InlineData(0, 126.5f, 88f)]      // 115 / 80 × 1.10
    [InlineData(10, 121.2f, 78.78f)]  // 120 / 78 × 1.01
    public async Task Enter_world_user_info_uses_the_class_move_speed_with_dex_bonus(
        int classId, float run, float walk)
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync(classId: classId, race: 0);

        Assert.True(kit.World.TryBuildNetUserInfo(session, out var user));
        Assert.Equal(classId, user.ClassId);
        Assert.Equal(run, user.RunSpeed);
        Assert.Equal(walk, user.WalkSpeed);

        // The remote spawn peers see must agree with the owner's sheet, or the avatar snaps on arrival.
        var spawn = NetEnterWorldProjector.BuildRemoteSpawn(player, EntityId.FromObjectId(player.ObjectId, 1));
        Assert.Equal(run, spawn.RunSpeed);
        Assert.Equal(walk, spawn.WalkSpeed);
    }
}
