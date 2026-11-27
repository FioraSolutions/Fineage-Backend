using System.Buffers.Binary;
using Fineage.Accounts;
using Fineage.Game;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// #991 client trust boundary. The client sends intent only: a destination, a target, a count, a skill
/// id. The server owns absolute position, damage rolls, stack counts and reuse timers. These tests drive
/// the same world/decoder paths the Net handlers use and assert that client-supplied fields never become
/// authoritative state.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ClientTrustBoundaryTests
{
    const int DashSkillId = 4;
    const int Adena = 57;

    [Fact]
    public async Task Client_position_is_compared_never_adopted_and_a_far_move_does_not_teleport()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync();
        using var cts = new CancellationTokenSource();
        Task pump = kit.World.PumpCommandsAsync(cts.Token);
        try
        {
            int x = session.X, y = session.Y, z = session.Z;
            string? drift = await kit.World.ExecuteAsync(
                session,
                new ValidatePositionCommand(x + 100_000, y + 100_000, z + 50_000),
                CancellationToken.None);
            Assert.StartsWith("position drift", drift);
            Assert.Equal((x, y), (player.X, player.Y));
            Assert.NotEqual(z + 50_000, player.Z);
            int serverZ = player.Z;

            // A far MoveInput may start a timed leg or be refused; either way it is not a teleport.
            await kit.World.ExecuteAsync(
                session, new MoveCommand(x + 100_000, y, serverZ), CancellationToken.None);
            Assert.True(Math.Abs(player.X - x) < 1_000, $"client dest teleported the pawn to {player.X}");
        }
        finally
        {
            await cts.CancelAsync();
            await pump;
        }
    }

    [Fact]
    public void Attack_and_skill_request_contracts_carry_no_damage_or_cooldown_override()
    {
        Assert.Null(typeof(AttackRequestNetMsg).GetProperty("Damage"));
        Assert.Null(typeof(AttackRequestNetMsg).GetProperty("Critical"));
        Assert.Null(typeof(UseSkillNetMsg).GetProperty("Damage"));
        Assert.Null(typeof(UseSkillNetMsg).GetProperty("ReuseDelayMs"));
        Assert.Null(typeof(UseSkillNetMsg).GetProperty("Cooldown"));

        byte[] payload = new byte[9];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 12_345);
        payload[4] = 1; // Ctrl / forced
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(5, 4), 999_999); // fake client damage
        AttackRequestNetMsg decoded = NetCodec.DecodeAttackRequest(payload);
        Assert.Equal(12_345, decoded.TargetObjectId);
        Assert.True(decoded.Forced);
    }

    [Fact]
    public async Task Client_count_cannot_destroy_more_than_the_server_side_stack()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync();
        var row = player.Inventory!.Add(Adena, 3);
        int before = player.Inventory.CountOf(Adena);

        Assert.Equal("bad_request",
            await kit.World.TryDestroyItemAsync(session, row.ObjectId, before + 1_000, CancellationToken.None));
        Assert.Equal(before, player.Inventory.CountOf(Adena));
    }

    [Fact]
    public async Task Client_cannot_clear_a_server_side_skill_reuse_timer()
    {
        var clock = new MutableClock
        {
            Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000)
        };
        var bench = new WorldBench(
            clock: () => clock.Now,
            spawns: Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(),
            mapId: null);
        BenchPlayer actor = await bench.EnterAsync(
            "cooldown", "Cooldown", classId: HumanClasses.Fighter, level: 20);
        ZoneEntity player = actor.Player;
        player.KnownSkills[DashSkillId] = 1;
        player.CurMp = player.MaxMp = 500;

        Assert.Null(await bench.World.TrySkillUseAsync(
            actor.Session, DashSkillId, player.ObjectId, CancellationToken.None));
        Assert.True(player.SkillReuseUntilMs.TryGetValue(DashSkillId, out long until));
        Assert.True(until > bench.Now().ToUnixTimeMilliseconds());

        // Past the 1 s cast, still inside the 80 s reuse. A second client press carries only
        // skill/target; it cannot replace the server stamp.
        clock.Now = clock.Now.AddSeconds(2);
        Assert.Equal(SkillProtocol.Reuse, await bench.World.TrySkillUseAsync(
            actor.Session, DashSkillId, player.ObjectId, CancellationToken.None));
        Assert.Equal(until, player.SkillReuseUntilMs[DashSkillId]);
    }
}
