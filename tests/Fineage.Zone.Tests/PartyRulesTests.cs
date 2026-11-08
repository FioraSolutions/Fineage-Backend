using Fineage.Zone.Party;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class PartyRulesTests
{
    [Fact]
    public void Max_members_is_interlude_nine()
    {
        Assert.Equal(9, PartyRules.MaxMembers);
        Assert.False(PartyRules.IsFull(8));
        Assert.True(PartyRules.IsFull(9));
    }
}
