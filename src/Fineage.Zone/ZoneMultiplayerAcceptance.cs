using System.Net;
using System.Net.Sockets;
using Fineage.Game;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Interest;
using Fineage.Zone.World;
using MessagePack;

namespace Fineage.Zone;

public static class ZoneMultiplayerAcceptance
{
    public static async Task RunAsync()
    {
        await RunSharedWorldAsync().ConfigureAwait(false);
        await RunKnownlistDropAsync().ConfigureAwait(false);
    }

    public static async Task RunSharedWorldAsync()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var login = new LoginService(JsonAccountStore.InMemory(), tokens);
        var fighter = Play(login, "selftest", "HumanOne", HumanClasses.Fighter);
        var mystic = Play(login, "selftest2", "HumanTwo", HumanClasses.Mystic);
        if (fighter.CharacterId == mystic.CharacterId)
            throw new InvalidOperationException("the two humans were issued the same character id");

        var world = new ZoneWorld();
        int[] npcIds = [.. world.NpcObjectIds];
        int[] again = [.. world.NpcObjectIds];
        if (!npcIds.SequenceEqual(again) || !npcIds.SequenceEqual(TalkingIsland.Gremlins.Select(g => g.ObjectId)))
            throw new InvalidOperationException("gremlin object ids are not one shared set");

        await using var zone = ZoneHost.Start(tokens, world);
        await using var left = await ZonePeer.ConnectAsync(zone.Port).ConfigureAwait(false);
        await using var right = await ZonePeer.ConnectAsync(zone.Port).ConfigureAwait(false);

        await left.AuthAsync(fighter).ConfigureAwait(false);
        await right.AuthAsync(mystic).ConfigureAwait(false);
        var early = await left.SayAsync("too soon").ConfigureAwait(false);
        if (early.Code != "world")
            throw new InvalidOperationException($"say before enter returned {early.Code}");

        var leftSpawn = await left.EnterAsync(TalkingIsland.Gremlins.Length, expectOthers: 0).ConfigureAwait(false);
        if (leftSpawn.Spawned.ObjectId != fighter.CharacterId || leftSpawn.Spawned.Name != fighter.CharacterName)
            throw new InvalidOperationException("fighter spawn mismatch");
        ExpectGear(leftSpawn, fighter, weaponId: 2369);

        var rightSpawn = await right.EnterAsync(fighter, mystic, TalkingIsland.Gremlins.Length).ConfigureAwait(false);
        ExpectGear(rightSpawn, mystic, weaponId: 6);
        var seen = await left.ExpectAsync<CharInfoMsg>(MsgType.CharInfo).ConfigureAwait(false);
        ExpectChar(seen, mystic, rightSpawn.Spawned.X, rightSpawn.Spawned.Y, rightSpawn.Spawned.Z);
        ExpectChar(rightSpawn.Others[0], fighter, leftSpawn.Spawned.X, leftSpawn.Spawned.Y, leftSpawn.Spawned.Z);
        if (seen.Paperdoll.Length != InventoryMessages.PaperdollSlotCount || seen.Paperdoll[7] != 6)
            throw new InvalidOperationException($"mystic char paperdoll rhand={PaperdollAt(seen, 7)}");
        if (rightSpawn.Others[0].Paperdoll.Length != InventoryMessages.PaperdollSlotCount || rightSpawn.Others[0].Paperdoll[7] != 2369)
            throw new InvalidOperationException($"fighter char paperdoll rhand={PaperdollAt(rightSpawn.Others[0], 7)}");
        ExpectDistinctItems(leftSpawn, rightSpawn, fighter.CharacterId, mystic.CharacterId);
        SameGremlins(leftSpawn.Npcs, rightSpawn.Npcs);

        var blank = await left.SayAsync("   ").ConfigureAwait(false);
        if (blank.Code != "bad_request")
            throw new InvalidOperationException($"blank say returned {blank.Code}");

        await left.SendAsync(MsgType.Say, new SayMsg { Text = "hello zone", ChatType = 0 }).ConfigureAwait(false);
        var saidLeft = await left.ExpectAsync<CreatureSayMsg>(MsgType.CreatureSay).ConfigureAwait(false);
        var saidRight = await right.ExpectAsync<CreatureSayMsg>(MsgType.CreatureSay).ConfigureAwait(false);
        ExpectSay(saidLeft, fighter, "hello zone");
        ExpectSay(saidRight, fighter, "hello zone");

        await right.SendAsync(MsgType.Say, new SayMsg { Text = "back", ChatType = 0 }).ConfigureAwait(false);
        ExpectSay(await right.ExpectAsync<CreatureSayMsg>(MsgType.CreatureSay).ConfigureAwait(false), mystic, "back");
        ExpectSay(await left.ExpectAsync<CreatureSayMsg>(MsgType.CreatureSay).ConfigureAwait(false), mystic, "back");

        var gremlin = TalkingIsland.Gremlins[0];
        await left.SendAsync(MsgType.MoveTo, new MoveToMsg
        {
            DestX = gremlin.X,
            DestY = gremlin.Y,
            DestZ = gremlin.Z
        }).ConfigureAwait(false);
        var moveLeft = await left.ExpectAsync<MoveToMsg>(MsgType.MoveToAck).ConfigureAwait(false);
        var moveRight = await right.ExpectAsync<MoveToMsg>(MsgType.MoveToAck).ConfigureAwait(false);
        ExpectMove(moveLeft, fighter.CharacterId, leftSpawn.Spawned.X, leftSpawn.Spawned.Y, gremlin.X, gremlin.Y);
        ExpectMove(moveRight, fighter.CharacterId, leftSpawn.Spawned.X, leftSpawn.Spawned.Y, gremlin.X, gremlin.Y);

        var leftBehind = right.ExpectAsync<DeleteObjectMsg>(MsgType.DeleteObject);
        await left.DisposeAsync().ConfigureAwait(false);
        var deleted = await leftBehind.ConfigureAwait(false);
        if (deleted.ObjectId != fighter.CharacterId)
            throw new InvalidOperationException($"leave deleted {deleted.ObjectId}, expected {fighter.CharacterId}");
        right.ExpectQuiet();
    }

    public static async Task RunKnownlistDropAsync()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var login = new LoginService(JsonAccountStore.InMemory(), tokens);
        var fighter = Play(login, "selftest", "HumanOne", HumanClasses.Fighter);
        var other = Play(login, "selftest2", "HumanTwo", HumanClasses.Fighter);
        var world = new ZoneWorld(new RangeAoi(1000));

        await using var zone = ZoneHost.Start(tokens, world);
        await using var left = await ZonePeer.ConnectAsync(zone.Port).ConfigureAwait(false);
        await using var right = await ZonePeer.ConnectAsync(zone.Port).ConfigureAwait(false);
        await left.AuthAsync(fighter).ConfigureAwait(false);
        await right.AuthAsync(other).ConfigureAwait(false);
        var leftSpawn = await left.EnterAsync(expectNpcs: 0, expectOthers: 0).ConfigureAwait(false);
        await right.EnterAsync(fighter, other, expectNpcs: 0).ConfigureAwait(false);
        _ = await left.ExpectAsync<CharInfoMsg>(MsgType.CharInfo).ConfigureAwait(false);

        await left.SendAsync(MsgType.MoveTo, new MoveToMsg
        {
            DestX = leftSpawn.Spawned.X + TalkingIsland.MaxStep,
            DestY = leftSpawn.Spawned.Y,
            DestZ = leftSpawn.Spawned.Z
        }).ConfigureAwait(false);

        var ack = await left.ExpectAsync<MoveToMsg>(MsgType.MoveToAck).ConfigureAwait(false);
        if (ack.ObjectId != fighter.CharacterId || ack.DestX != leftSpawn.Spawned.X + TalkingIsland.MaxStep)
            throw new InvalidOperationException("aoi move ack mismatch");

        var dropped = new List<int>();
        dropped.Add((await left.ExpectAsync<DeleteObjectMsg>(MsgType.DeleteObject).ConfigureAwait(false)).ObjectId);
        if (dropped.Count != 1 || dropped[0] != other.CharacterId)
            throw new InvalidOperationException($"knownlist drop was {string.Join(",", dropped)}");

        var gone = await right.ExpectAsync<DeleteObjectMsg>(MsgType.DeleteObject).ConfigureAwait(false);
        if (gone.ObjectId != fighter.CharacterId)
            throw new InvalidOperationException($"watcher delete was {gone.ObjectId}");
        left.ExpectQuiet();
        right.ExpectQuiet();
    }

    static HandoffTicket Play(LoginService login, string account, string name, int classId)
    {
        var result = login.Play(account, "pass1234", name, classId);
        if (!result.Ok || result.Value == null)
            throw new InvalidOperationException($"play failed: {result.Code} {result.Message}");
        return result.Value;
    }

    static void ExpectChar(CharInfoMsg info, HandoffTicket who, int x, int y, int z)
    {
        if (info.ObjectId != who.CharacterId || info.Name != who.CharacterName || info.ClassId != who.ClassId ||
            info.X != x || info.Y != y || info.Z != z)
            throw new InvalidOperationException(
                $"char mismatch id={info.ObjectId} name={info.Name} class={info.ClassId} @ {info.X},{info.Y},{info.Z}");
    }

    static void ExpectSay(CreatureSayMsg say, HandoffTicket who, string text)
    {
        if (say.ObjectId != who.CharacterId || say.Name != who.CharacterName || say.Text != text || say.ChatType != 0)
            throw new InvalidOperationException($"say mismatch id={say.ObjectId} name={say.Name} text={say.Text}");
    }

    static void ExpectMove(MoveToMsg move, int objectId, int originX, int originY, int destX, int destY)
    {
        if (move.ObjectId != objectId || move.OriginX != originX || move.OriginY != originY ||
            move.DestX != destX || move.DestY != destY)
            throw new InvalidOperationException($"move mismatch id={move.ObjectId} dest={move.DestX},{move.DestY} origin={move.OriginX},{move.OriginY}");
    }

    static void ExpectGear(Arrival arrival, HandoffTicket who, int weaponId)
    {
        if (arrival.User.ObjectId != who.CharacterId || arrival.User.ClassId != who.ClassId ||
            arrival.User.X != arrival.Spawned.X || arrival.User.Y != arrival.Spawned.Y || arrival.User.Z != arrival.Spawned.Z)
            throw new InvalidOperationException($"user info mismatch {arrival.User.Name} @ {arrival.User.X},{arrival.User.Y},{arrival.User.Z}");
        if (!IsClassPoint(who.ClassId, arrival.Spawned.X, arrival.Spawned.Y, arrival.Spawned.Z))
            throw new InvalidOperationException($"spawn {arrival.Spawned.X},{arrival.Spawned.Y},{arrival.Spawned.Z} is not class {who.ClassId}");
        if (arrival.User.Paperdoll.Length != InventoryMessages.PaperdollSlotCount || arrival.User.Paperdoll[7].ItemId != weaponId)
            throw new InvalidOperationException($"user rhand={arrival.User.Paperdoll.ElementAtOrDefault(7)?.ItemId}");
        ItemListEntryMsg? weapon = null;
        foreach (var item in arrival.Items.Items)
        {
            if (item.ItemId == weaponId)
                weapon = item;
        }

        if (weapon == null || weapon.EquippedSlot != 7 || weapon.ObjectId != arrival.User.Paperdoll[7].ObjectId)
            throw new InvalidOperationException($"item list missing equipped {weaponId}");
    }

    static void ExpectDistinctItems(Arrival left, Arrival right, int leftCharacterId, int rightCharacterId)
    {
        var ids = new HashSet<int> { leftCharacterId, rightCharacterId };
        foreach (var npc in TalkingIsland.Gremlins)
            ids.Add(npc.ObjectId);
        foreach (var item in left.Items.Items.Concat(right.Items.Items))
        {
            if (item.ObjectId < PlayerInventory.FirstObjectId || !ids.Add(item.ObjectId))
                throw new InvalidOperationException($"item object id {item.ObjectId} collides");
        }
    }

    static int PaperdollAt(CharInfoMsg info, int slot) =>
        info.Paperdoll != null && slot < info.Paperdoll.Length ? info.Paperdoll[slot] : -1;

    static bool IsClassPoint(int classId, int x, int y, int z)
    {
        foreach (var point in ClassSpawn.Points(classId))
        {
            if (point.X == x && point.Y == y && point.Z == z)
                return true;
        }

        return false;
    }

    static void SameGremlins(IReadOnlyList<NpcInfoMsg> left, IReadOnlyList<NpcInfoMsg> right)
    {
        if (left.Count != TalkingIsland.Gremlins.Length || right.Count != left.Count)
            throw new InvalidOperationException($"gremlin count {left.Count}/{right.Count}");
        for (int i = 0; i < left.Count; i++)
        {
            var expected = TalkingIsland.Gremlins[i];
            if (left[i].ObjectId != right[i].ObjectId || left[i].ObjectId != expected.ObjectId ||
                left[i].NpcId != expected.NpcId || right[i].NpcId != expected.NpcId ||
                left[i].Name != "Gremlin" || right[i].Name != "Gremlin" ||
                left[i].X != expected.X || right[i].X != expected.X ||
                left[i].Y != expected.Y || right[i].Y != expected.Y ||
                left[i].Z != expected.Z || right[i].Z != expected.Z)
                throw new InvalidOperationException($"shared gremlin {i} mismatch id={left[i].ObjectId}/{right[i].ObjectId}");
        }
    }

    sealed class ZonePeer : IAsyncDisposable
    {
        readonly TcpClient _client;
        readonly NetworkStream _stream;
        bool _closed;

        ZonePeer(TcpClient client, NetworkStream stream)
        {
            _client = client;
            _stream = stream;
        }

        public static async Task<ZonePeer> ConnectAsync(int port)
        {
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port).ConfigureAwait(false);
            return new ZonePeer(client, client.GetStream());
        }

        public async Task AuthAsync(HandoffTicket ticket)
        {
            await SendAsync(MsgType.Hello, new HelloMsg { ProtocolVersion = 1, ClientBuild = "shared" }).ConfigureAwait(false);
            await ExpectAsync<HelloAckMsg>(MsgType.HelloAck).ConfigureAwait(false);
            await SendAsync(MsgType.Auth, ticket.ToZoneAuth()).ConfigureAwait(false);
            var auth = await ExpectAsync<AuthOkMsg>(MsgType.AuthOk).ConfigureAwait(false);
            if (auth.CharacterName != ticket.CharacterName || auth.SessionId == 0)
                throw new InvalidOperationException($"auth mismatch name={auth.CharacterName}");
        }

        public async Task<ErrorMsg> SayAsync(string text)
        {
            await SendAsync(MsgType.Say, new SayMsg { Text = text }).ConfigureAwait(false);
            return await ExpectAsync<ErrorMsg>(MsgType.Error).ConfigureAwait(false);
        }

        public async Task<Arrival> EnterAsync(int expectNpcs, int expectOthers)
        {
            await SendAsync(MsgType.EnterWorld, new EnterWorldMsg()).ConfigureAwait(false);
            var user = await ExpectAsync<UserInfoMsg>(MsgType.UserInfo).ConfigureAwait(false);
            var items = await ExpectAsync<ItemListMsg>(MsgType.ItemList).ConfigureAwait(false);
            var spawned = await ExpectAsync<PlayerSpawnedMsg>(MsgType.PlayerSpawned).ConfigureAwait(false);
            var npcs = new List<NpcInfoMsg>(expectNpcs);
            for (int i = 0; i < expectNpcs; i++)
                npcs.Add(await ExpectAsync<NpcInfoMsg>(MsgType.NpcInfo).ConfigureAwait(false));
            var others = new List<CharInfoMsg>(expectOthers);
            for (int i = 0; i < expectOthers; i++)
                others.Add(await ExpectAsync<CharInfoMsg>(MsgType.CharInfo).ConfigureAwait(false));
            return new Arrival(user, items, spawned, npcs, others);
        }

        public async Task<Arrival> EnterAsync(HandoffTicket alreadyIn, HandoffTicket self, int expectNpcs)
        {
            var arrival = await EnterAsync(expectNpcs, expectOthers: 1).ConfigureAwait(false);
            if (arrival.Spawned.ObjectId != self.CharacterId || arrival.Spawned.Name != self.CharacterName)
                throw new InvalidOperationException("second spawn mismatch");
            if (arrival.Others[0].ObjectId != alreadyIn.CharacterId)
                throw new InvalidOperationException("enter did not include the player already in the zone");
            return arrival;
        }

        public async Task SendAsync<T>(MsgType type, T msg)
        {
            byte[] body = MessagePackSerializer.Serialize(msg);
            await FrameIO.SendAsync(_stream, (ushort)type, body).ConfigureAwait(false);
        }

        public async Task<T> ExpectAsync<T>(MsgType expected)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            (ushort Type, byte[] Body)? frame;
            try
            {
                frame = await FrameIO.ReadAsync(_stream, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException($"timed out waiting for {expected}");
            }

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

            return MessagePackSerializer.Deserialize<T>(frame.Value.Body);
        }

        public void ExpectQuiet()
        {
            if (_stream.DataAvailable)
                throw new InvalidOperationException("unexpected trailing zone bytes");
        }

        public async ValueTask DisposeAsync()
        {
            if (_closed)
                return;
            _closed = true;
            _client.Dispose();
            await Task.CompletedTask;
        }
    }

    readonly record struct Arrival(UserInfoMsg User, ItemListMsg Items, PlayerSpawnedMsg Spawned, List<NpcInfoMsg> Npcs, List<CharInfoMsg> Others);
}
