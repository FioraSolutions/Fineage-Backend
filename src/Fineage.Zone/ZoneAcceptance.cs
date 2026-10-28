using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using MessagePack;

namespace Fineage.Zone;

public static class ZoneAcceptance
{
    public static async Task RunAsync()
    {
        var login = await LoginAcceptance.RunAsync().ConfigureAwait(false);
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);

        await using (var zone = ZoneHost.Start(tokens))
        {
            await PlayAsync(zone.Port, login.Fighter.Account, login.Fighter.Token, login.Fighter.CharacterId, login.Fighter.CharacterName)
                .ConfigureAwait(false);
            await PlayAsync(zone.Port, login.Mystic.Account, login.Mystic.Token, login.Mystic.CharacterId, login.Mystic.CharacterName)
                .ConfigureAwait(false);
        }
    }

    public static void Write(TextWriter output) => output.WriteLine("selftest OK");

    static async Task PlayAsync(int port, string account, string token, int characterId, string name)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port).ConfigureAwait(false);
        await using var stream = client.GetStream();

        await SendAsync(stream, MsgType.Hello, new HelloMsg { ProtocolVersion = 1, ClientBuild = "selftest" })
            .ConfigureAwait(false);
        await ExpectAsync(stream, MsgType.HelloAck).ConfigureAwait(false);

        await SendAsync(stream, MsgType.EnterWorld, new EnterWorldMsg()).ConfigureAwait(false);
        var early = await ExpectErrorAsync(stream).ConfigureAwait(false);
        if (early.Code != "auth_required")
            throw new InvalidOperationException($"expected auth_required, got {early.Code}");

        await SendAsync(stream, MsgType.Auth, new AuthMsg { Account = account, Password = "not-a-token" })
            .ConfigureAwait(false);
        var bad = await ExpectErrorAsync(stream).ConfigureAwait(false);
        if (bad.Code != "invalid_credentials")
            throw new InvalidOperationException($"expected invalid_credentials, got {bad.Code}");

        await SendAsync(stream, MsgType.Auth, new AuthMsg { Account = account, Password = token })
            .ConfigureAwait(false);
        var authBody = await ExpectAsync(stream, MsgType.AuthOk).ConfigureAwait(false);
        var auth = MessagePackSerializer.Deserialize<AuthOkMsg>(authBody);
        if (auth.CharacterName != name || auth.SessionId == 0)
            throw new InvalidOperationException($"auth ok mismatch name={auth.CharacterName} session={auth.SessionId}");

        await SendAsync(stream, MsgType.MoveTo, new MoveToMsg
        {
            DestX = TalkingIsland.SpawnX + 10,
            DestY = TalkingIsland.SpawnY,
            DestZ = TalkingIsland.SpawnZ
        }).ConfigureAwait(false);
        var tooSoon = await ExpectErrorAsync(stream).ConfigureAwait(false);
        if (tooSoon.Code != "world")
            throw new InvalidOperationException($"expected world, got {tooSoon.Code}");

        await SendAsync(stream, MsgType.EnterWorld, new EnterWorldMsg { CharacterSlot = 0 }).ConfigureAwait(false);
        var user = MessagePackSerializer.Deserialize<UserInfoMsg>(await ExpectAsync(stream, MsgType.UserInfo).ConfigureAwait(false));
        var items = MessagePackSerializer.Deserialize<ItemListMsg>(await ExpectAsync(stream, MsgType.ItemList).ConfigureAwait(false));
        var spawned = MessagePackSerializer.Deserialize<PlayerSpawnedMsg>(await ExpectAsync(stream, MsgType.PlayerSpawned).ConfigureAwait(false));
        if (spawned.ObjectId != characterId || spawned.Name != name ||
            user.ObjectId != characterId || user.X != spawned.X || user.Y != spawned.Y || user.Z != spawned.Z)
            throw new InvalidOperationException($"spawn mismatch id={spawned.ObjectId} name={spawned.Name} @ {spawned.X},{spawned.Y},{spawned.Z}");
        bool fighter = HasItem(items, 2369);
        int classId = fighter ? ClassSpawn.HumanFighter : ClassSpawn.HumanMystic;
        if (!IsClassPoint(classId, spawned.X, spawned.Y, spawned.Z))
            throw new InvalidOperationException($"spawn {spawned.X},{spawned.Y},{spawned.Z} is not a class {classId} point");
        int weaponId = fighter ? 2369 : 6;
        if (user.Paperdoll.Length != InventoryMessages.PaperdollSlotCount || user.Paperdoll[7].ItemId != weaponId)
            throw new InvalidOperationException($"user paperdoll rhand={user.Paperdoll.ElementAtOrDefault(7)?.ItemId}");
        var weapon = FindItem(items, weaponId);
        if (weapon.EquippedSlot != 7 || weapon.ObjectId != user.Paperdoll[7].ObjectId || weapon.ObjectId < 100_000)
            throw new InvalidOperationException($"item list weapon id={weapon.ItemId} slot={weapon.EquippedSlot} object={weapon.ObjectId}");

        var seen = new List<NpcInfoMsg>();
        for (int i = 0; i < TalkingIsland.Gremlins.Length; i++)
        {
            var body = await ExpectAsync(stream, MsgType.NpcInfo).ConfigureAwait(false);
            seen.Add(MessagePackSerializer.Deserialize<NpcInfoMsg>(body));
        }

        if (seen.Count != TalkingIsland.Gremlins.Length)
            throw new InvalidOperationException($"expected {TalkingIsland.Gremlins.Length} gremlins, got {seen.Count}");
        for (int i = 0; i < seen.Count; i++)
        {
            var npc = seen[i];
            var expected = TalkingIsland.Gremlins[i];
            if (npc.NpcId != TalkingIsland.GremlinNpcId || npc.Name != "Gremlin" ||
                npc.ObjectId != expected.ObjectId || npc.X != expected.X || npc.Y != expected.Y || npc.Z != expected.Z)
                throw new InvalidOperationException($"gremlin {i} mismatch id={npc.NpcId} @ {npc.X},{npc.Y},{npc.Z}");
        }

        var nearest = TalkingIsland.Gremlins[0];
        int destX = nearest.X;
        int destY = nearest.Y;
        int destZ = nearest.Z;
        if (!TalkingIsland.WithinStep(spawned.X, spawned.Y, destX, destY))
        {
            destX = spawned.X + 10;
            destY = spawned.Y;
            destZ = spawned.Z;
        }

        await SendAsync(stream, MsgType.MoveTo, new MoveToMsg
        {
            ObjectId = spawned.ObjectId,
            DestX = destX,
            DestY = destY,
            DestZ = destZ,
            OriginX = spawned.X,
            OriginY = spawned.Y,
            OriginZ = spawned.Z
        }).ConfigureAwait(false);
        var ackBody = await ExpectAsync(stream, MsgType.MoveToAck).ConfigureAwait(false);
        var ack = MessagePackSerializer.Deserialize<MoveToMsg>(ackBody);
        if (ack.ObjectId != characterId || ack.OriginX != spawned.X || ack.OriginY != spawned.Y ||
            ack.DestX != destX || ack.DestY != destY)
            throw new InvalidOperationException($"move ack mismatch dest={ack.DestX},{ack.DestY} origin={ack.OriginX},{ack.OriginY}");
    }

    static bool HasItem(ItemListMsg items, int itemId)
    {
        foreach (var item in items.Items)
        {
            if (item.ItemId == itemId)
                return true;
        }

        return false;
    }

    static ItemListEntryMsg FindItem(ItemListMsg items, int itemId)
    {
        foreach (var item in items.Items)
        {
            if (item.ItemId == itemId)
                return item;
        }

        throw new InvalidOperationException($"item {itemId} missing from ItemList");
    }

    static bool IsClassPoint(int classId, int x, int y, int z)
    {
        foreach (var point in ClassSpawn.Points(classId))
        {
            if (point.X == x && point.Y == y && point.Z == z)
                return true;
        }

        return false;
    }

    static async Task SendAsync<T>(NetworkStream stream, MsgType type, T msg)
    {
        byte[] body = MessagePackSerializer.Serialize(msg);
        await FrameIO.SendAsync(stream, (ushort)type, body).ConfigureAwait(false);
    }

    static async Task<byte[]> ExpectAsync(NetworkStream stream, MsgType expected)
    {
        var frame = await FrameIO.ReadAsync(stream).ConfigureAwait(false);
        if (frame == null)
            throw new InvalidOperationException($"expected {expected}, got EOF");
        if (frame.Value.Type != (ushort)expected)
        {
            string detail = "";
            if (frame.Value.Type == (ushort)MsgType.Error)
            {
                var err = MessagePackSerializer.Deserialize<ErrorMsg>(frame.Value.Body);
                detail = $" {err.Code} {err.Message}";
            }

            throw new InvalidOperationException($"expected {expected}, got {(MsgType)frame.Value.Type}{detail}");
        }

        return frame.Value.Body;
    }

    static async Task<ErrorMsg> ExpectErrorAsync(NetworkStream stream)
    {
        var body = await ExpectAsync(stream, MsgType.Error).ConfigureAwait(false);
        return MessagePackSerializer.Deserialize<ErrorMsg>(body);
    }
}
