using Fineage.Protocol;
using MessagePack;
using Xunit;

namespace Fineage.Login.Tests;

public class ProtocolCompatTests
{
    [Fact]
    public void AuthMsg_MatchesZoneServerBytes()
    {
        var msg = new AuthMsg { Account = "hero", Password = "tok" };
        Assert.Equal("92A46865726FA3746F6B", Convert.ToHexString(MessagePackSerializer.Serialize(msg)));
    }

    [Fact]
    public void HelloMsg_MatchesZoneServerBytes()
    {
        var msg = new HelloMsg { ProtocolVersion = 1, ClientBuild = "selftest" };
        Assert.Equal("9201A873656C6674657374", Convert.ToHexString(MessagePackSerializer.Serialize(msg)));
    }

    [Fact]
    public void ErrorMsg_MatchesZoneServerBytes()
    {
        var msg = new ErrorMsg { Code = "auth", Message = "Auth required" };
        Assert.Equal("92A461757468AD41757468207265717569726564", Convert.ToHexString(MessagePackSerializer.Serialize(msg)));
    }

    [Fact]
    public void AuthOkMsg_MatchesZoneServerBytes()
    {
        var msg = new AuthOkMsg { SessionId = 7, CharacterName = "Test" };
        Assert.Equal("9207A454657374", Convert.ToHexString(MessagePackSerializer.Serialize(msg)));
    }

    [Fact]
    public void ZoneOpcodes_MatchFineageZoneServer()
    {
        Assert.Equal(1, (ushort)MsgType.Hello);
        Assert.Equal(2, (ushort)MsgType.Auth);
        Assert.Equal(3, (ushort)MsgType.EnterWorld);
        Assert.Equal(4, (ushort)MsgType.MoveTo);
        Assert.Equal(5, (ushort)MsgType.SelectTarget);
        Assert.Equal(6, (ushort)MsgType.AttackRequest);
        Assert.Equal(1001, (ushort)MsgType.HelloAck);
        Assert.Equal(1002, (ushort)MsgType.AuthOk);
        Assert.Equal(1003, (ushort)MsgType.PlayerSpawned);
        Assert.Equal(1004, (ushort)MsgType.MoveToAck);
        Assert.Equal(1005, (ushort)MsgType.CharInfo);
        Assert.Equal(1006, (ushort)MsgType.NpcInfo);
        Assert.Equal(1007, (ushort)MsgType.DeleteObject);
        Assert.Equal(1008, (ushort)MsgType.MyTargetSelected);
        Assert.Equal(1009, (ushort)MsgType.Attack);
        Assert.Equal(1010, (ushort)MsgType.StatusUpdate);
        Assert.Equal(1099, (ushort)MsgType.Error);
    }

    [Fact]
    public void Say_MatchesProtocolV2Layout()
    {
        Assert.Equal((ushort)MsgType.Say, (ushort)Fineage.Protocol.V2.MsgType.Say);
        Assert.Equal((ushort)MsgType.CreatureSay, (ushort)Fineage.Protocol.V2.MsgType.CreatureSay);

        byte[] say = MessagePackSerializer.Serialize(new SayMsg { Text = "hi", ChatType = 0 });
        byte[] sayV2 = MessagePackSerializer.Serialize(new Fineage.Protocol.V2.SayMsg { Text = "hi", ChatType = 0 });
        Assert.Equal(say, sayV2);

        byte[] heard = MessagePackSerializer.Serialize(new CreatureSayMsg
        {
            ObjectId = 4,
            Name = "Aria",
            Text = "hi",
            ChatType = 0
        });
        byte[] heardV2 = MessagePackSerializer.Serialize(new Fineage.Protocol.V2.CreatureSayMsg
        {
            ObjectId = 4,
            Name = "Aria",
            Text = "hi",
            ChatType = 0
        });
        Assert.Equal(heard, heardV2);
    }

    [Fact]
    public void LoginOpcodes_DoNotReuseZoneOpcodes()
    {
        ushort[] zone =
        [
            1, 2, 3, 4, 5, 6, 8,
            1001, 1002, 1003, 1004, 1005, 1006, 1007, 1008, 1009, 1010, 1012,
            1099
        ];
        ushort[] login =
        [
            (ushort)MsgType.Register,
            (ushort)MsgType.CreateCharacter,
            (ushort)MsgType.ListCharacters,
            (ushort)MsgType.SelectCharacter,
            (ushort)MsgType.RegisterOk,
            (ushort)MsgType.CharacterCreated,
            (ushort)MsgType.CharacterList,
            (ushort)MsgType.Handoff,
            (ushort)MsgType.LoginOk
        ];
        Assert.All(login, op => Assert.DoesNotContain(op, zone));
    }

    [Fact]
    public async Task Frame_RoundTripsLengthPrefixedBody()
    {
        using var stream = new MemoryStream();
        byte[] body = [1, 2, 3, 4];
        await FrameIO.SendAsync(stream, (ushort)MsgType.Auth, body);
        stream.Position = 0;
        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)MsgType.Auth, frame!.Value.Type);
        Assert.Equal(body, frame.Value.Body);
        Assert.Equal(8, stream.Length);
    }
}
