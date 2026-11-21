using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class MoveCommandPositionTests
{
    [Fact]
    public async Task Move_command_records_authoritative_session_position_after_apply()
    {
        var kit = new NetTestKit();
        var (session, _) = await kit.EnterAsync();

        using var cts = new CancellationTokenSource();
        var pump = kit.World.PumpCommandsAsync(cts.Token);
        try
        {
            int beforeX = session.X;
            int beforeY = session.Y;
            int beforeZ = session.Z;
            var command = new MoveCommand(beforeX + 200, beforeY, beforeZ);
            string? error = await kit.World.ExecuteAsync(session, command, CancellationToken.None);

            Assert.Null(error);
            Assert.Equal(session.X, command.ResultX);
            Assert.Equal(session.Y, command.ResultY);
            Assert.Equal(session.Z, command.ResultZ);
        }
        finally
        {
            await cts.CancelAsync();
            await pump;
        }
    }
}