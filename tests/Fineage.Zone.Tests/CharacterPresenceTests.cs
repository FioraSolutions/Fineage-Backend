using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class CharacterPresenceTests
{
    [Fact]
    public async Task Enter_saves_last_access_and_leave_keeps_the_moved_position()
    {
        var store = JsonAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        var ticket = login.Play("alice", "secret1", "Aria", HumanClasses.Fighter).Value!;
        var world = new ZoneWorld(accounts: store, spawnRandom: new Random(1));

        await using var stream = new MemoryStream();
        var session = new ZoneSession(stream);
        session.Bind(ticket.CharacterId, ticket.Account, ticket.CharacterName, ticket.ClassId);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        Assert.Contains(new ClassSpawn.Point(session.X, session.Y, session.Z), ClassSpawn.Points(HumanClasses.Fighter));

        int movedX = session.X + 100;
        int movedY = session.Y;
        int movedZ = session.Z;
        Assert.Null(await world.TryMoveAsync(session, new MoveToMsg
        {
            DestX = movedX,
            DestY = movedY,
            DestZ = movedZ
        }, CancellationToken.None));

        await world.LeaveAsync(session, CancellationToken.None);
        var saved = store.FindById(ticket.Account, ticket.CharacterId);
        Assert.NotNull(saved);
        Assert.True(saved.HasPosition);
        Assert.Equal(movedX, saved.X);
        Assert.Equal(movedY, saved.Y);
        Assert.Equal(movedZ, saved.Z);
        Assert.NotEqual(default, saved.LastAccess);

        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        Assert.Equal(movedX, session.X);
        Assert.Equal(movedY, session.Y);
        Assert.Equal(movedZ, session.Z);
    }

    [Fact]
    public async Task Enter_while_already_online_stores_the_previous_session_first()
    {
        var store = JsonAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        var ticket = login.Play("alice", "secret1", "Aria", HumanClasses.Fighter).Value!;
        var world = new ZoneWorld(accounts: store, spawnRandom: new Random(2));

        await using var firstStream = new MemoryStream();
        await using var secondStream = new MemoryStream();
        var first = new ZoneSession(firstStream);
        var second = new ZoneSession(secondStream);
        first.Bind(ticket.CharacterId, ticket.Account, ticket.CharacterName, ticket.ClassId);
        second.Bind(ticket.CharacterId, ticket.Account, ticket.CharacterName, ticket.ClassId);

        Assert.True(await world.TryEnterAsync(first, CancellationToken.None));
        int movedX = first.X + 50;
        Assert.Null(await world.TryMoveAsync(first, new MoveToMsg
        {
            DestX = movedX,
            DestY = first.Y,
            DestZ = first.Z
        }, CancellationToken.None));

        Assert.True(await world.TryEnterAsync(second, CancellationToken.None));
        Assert.False(first.InWorld);
        Assert.True(second.InWorld);
        Assert.Equal(movedX, second.X);
        Assert.Equal(movedX, store.FindById(ticket.Account, ticket.CharacterId)!.X);
    }
}
