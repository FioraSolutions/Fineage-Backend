using Fineage.Net.Contract;
using Fineage.Zone.World;
using Xunit;
using Fineage.Protocol;

namespace Fineage.Zone.Tests;

/// <summary>
/// The quest journal is part of the Net enter-world burst, not only the reply to <c>RequestQuestList</c>.
/// The client opens the window on the menu button, so a journal that only arrives on request left the tabs
/// empty whenever the reply lost the race with the click.
/// </summary>
public sealed class NetQuestJournalTests
{
    [Fact]
    public async Task Enter_world_carries_the_quest_journal()
    {
        var kit = new NetTestKit();
        var (session, _) = await kit.EnterAsync();

        QuestListMsg list = kit.World.NetQuestList(session);

        Assert.NotNull(list.Active);
        Assert.NotNull(list.CompletedMask);
        Assert.NotNull(list.CompletedQuestIds);
        Assert.True(list.MaxActive > 0);
    }

    [Fact]
    public void Unbound_session_gets_an_empty_journal_instead_of_throwing()
    {
        var kit = new NetTestKit();
        var stranger = new ZoneSession();

        QuestListMsg list = kit.World.NetQuestList(stranger);

        Assert.Empty(list.Active);
        Assert.Empty(list.CompletedMask);
        Assert.Empty(list.CompletedQuestIds);
    }
}
