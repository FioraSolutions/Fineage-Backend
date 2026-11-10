using Fineage.Net.Contract;
using Xunit;

namespace Fineage.Net.Tests;

/// <summary>
/// Encode/decode round-trips for party / clan / chat / shortcut messages (#353 NetCodec track).
/// </summary>
public sealed class SocialCodecRoundTripTests
{
    [Fact]
    public void CreatureSay_and_SystemMessage_roundtrip()
    {
        var say = NetCodec.DecodeCreatureSay(NetCodec.EncodeCreatureSay(new CreatureSayNetMsg
        {
            ObjectId = 42,
            ChatType = 0,
            Name = "Alden",
            Text = "hello"
        }));
        Assert.Equal(42, say.ObjectId);
        Assert.Equal("Alden", say.Name);
        Assert.Equal("hello", say.Text);

        var sys = NetCodec.DecodeSystemMessage(NetCodec.EncodeSystemMessage(new SystemMessageNetMsg
        {
            Id = 144,
            Text = "That item cannot be discarded.",
            Params = ["sword"]
        }));
        Assert.Equal(144, sys.Id);
        Assert.Equal("That item cannot be discarded.", sys.Text);
        Assert.Equal(new[] { "sword" }, sys.Params);
    }

    [Fact]
    public void Party_invite_and_member_list_roundtrip()
    {
        var ask = NetCodec.DecodePartyAskJoin(NetCodec.EncodePartyAskJoin(new PartyAskJoinNetMsg
        {
            RequestorObjectId = 7,
            RequestorName = "Leader",
            ItemDistribution = 1
        }));
        Assert.Equal(7, ask.RequestorObjectId);
        Assert.Equal("Leader", ask.RequestorName);
        Assert.Equal(1, ask.ItemDistribution);

        var list = NetCodec.DecodePartyMemberList(NetCodec.EncodePartyMemberList(new PartyMemberListNetMsg
        {
            LeaderObjectId = 7,
            PartyId = 3,
            Members =
            [
                new PartyMemberEntryNet
                {
                    ObjectId = 7,
                    Name = "Leader",
                    ClassId = 0,
                    Level = 10,
                    CurHp = 100,
                    MaxHp = 100,
                    CurMp = 50,
                    MaxMp = 50
                }
            ]
        }));
        Assert.Equal(7, list.LeaderObjectId);
        Assert.Equal(3, list.PartyId);
        Assert.Equal("Leader", Assert.Single(list.Members!).Name);
        Assert.Equal(10, list.Members![0].Level);
    }

    [Fact]
    public void Clan_pledge_status_and_shortcut_roundtrip()
    {
        var pledge = NetCodec.DecodePledgeStatus(NetCodec.EncodePledgeStatus(new PledgeStatusNetMsg
        {
            ClanId = 9,
            Name = "Fineage",
            Level = 1,
            LeaderObjectId = 42,
            CrestId = 0,
            MemberCount = 2,
            DissolvingExpiryUnixMs = 0,
            CharPenaltyExpiryUnixMs = 0
        }));
        Assert.Equal(9, pledge.ClanId);
        Assert.Equal("Fineage", pledge.Name);
        Assert.Equal(42, pledge.LeaderObjectId);
        Assert.Equal(2, pledge.MemberCount);

        var cut = NetCodec.DecodeShortCut(NetCodec.EncodeShortCut(new ShortCutNetMsg
        {
            Slot = 3,
            Type = 1,
            Id = 254,
            Level = 1
        }));
        Assert.Equal(3, cut.Slot);
        Assert.Equal(254, cut.Id);
        Assert.Equal(1, cut.Level);
    }
}
