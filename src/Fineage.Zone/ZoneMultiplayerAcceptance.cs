using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Combat;
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

        var leftSpawn = await left.EnterAsync(expectOthers: 0).ConfigureAwait(false);
        if (leftSpawn.Spawned.ObjectId != fighter.CharacterId || leftSpawn.Spawned.Name != fighter.CharacterName)
            throw new InvalidOperationException("fighter spawn mismatch");

        var rightSpawn = await right.EnterAsync(fighter, mystic).ConfigureAwait(false);
        var seen = await left.ExpectAsync<CharInfoMsg>(MsgType.CharInfo).ConfigureAwait(false);
        ExpectChar(seen, mystic, TalkingIsland.SpawnX, TalkingIsland.SpawnY, TalkingIsland.SpawnZ);
        ExpectChar(rightSpawn.Others[0], fighter, TalkingIsland.SpawnX, TalkingIsland.SpawnY, TalkingIsland.SpawnZ);
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
        ExpectMove(moveLeft, fighter.CharacterId, gremlin.X, gremlin.Y);
        ExpectMove(moveRight, fighter.CharacterId, gremlin.X, gremlin.Y);

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
        var mystic = Play(login, "selftest2", "HumanTwo", HumanClasses.Mystic);
        var world = new ZoneWorld(new RangeAoi(1000));

        await using var zone = ZoneHost.Start(tokens, world);
        await using var left = await ZonePeer.ConnectAsync(zone.Port).ConfigureAwait(false);
        await using var right = await ZonePeer.ConnectAsync(zone.Port).ConfigureAwait(false);
        await left.AuthAsync(fighter).ConfigureAwait(false);
        await right.AuthAsync(mystic).ConfigureAwait(false);
        await left.EnterAsync(expectOthers: 0).ConfigureAwait(false);
        await right.EnterAsync(fighter, mystic).ConfigureAwait(false);
        _ = await left.ExpectAsync<CharInfoMsg>(MsgType.CharInfo).ConfigureAwait(false);

        await left.SendAsync(MsgType.MoveTo, new MoveToMsg
        {
            DestX = TalkingIsland.SpawnX + TalkingIsland.MaxStep,
            DestY = TalkingIsland.SpawnY,
            DestZ = TalkingIsland.SpawnZ
        }).ConfigureAwait(false);

        var ack = await left.ExpectAsync<MoveToMsg>(MsgType.MoveToAck).ConfigureAwait(false);
        if (ack.ObjectId != fighter.CharacterId || ack.DestX != TalkingIsland.SpawnX + TalkingIsland.MaxStep)
            throw new InvalidOperationException("aoi move ack mismatch");

        var dropped = new List<int>();
        for (int i = 0; i < 4; i++)
            dropped.Add((await left.ExpectAsync<DeleteObjectMsg>(MsgType.DeleteObject).ConfigureAwait(false)).ObjectId);
        int[] expected =
        [
            mystic.CharacterId,
            TalkingIsland.Gremlins[0].ObjectId,
            TalkingIsland.Gremlins[1].ObjectId,
            TalkingIsland.Gremlins[2].ObjectId
        ];
        Array.Sort(expected);
        if (!dropped.SequenceEqual(expected))
            throw new InvalidOperationException($"knownlist drop was {string.Join(",", dropped)}");

        var gone = await right.ExpectAsync<DeleteObjectMsg>(MsgType.DeleteObject).ConfigureAwait(false);
        if (gone.ObjectId != fighter.CharacterId)
            throw new InvalidOperationException($"watcher delete was {gone.ObjectId}");
        left.ExpectQuiet();
        right.ExpectQuiet();
    }

    public static async Task RunCombatSharedAsync()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var login = new LoginService(JsonAccountStore.InMemory(), tokens);
        var fighter = Play(login, "combat1", "Striker", HumanClasses.Fighter);
        var mystic = Play(login, "combat2", "Watcher", HumanClasses.Mystic);

        var world = new ZoneWorld();
        await using var zone = ZoneHost.Start(tokens, world);
        await using var attacker = await ZonePeer.ConnectAsync(zone.Port).ConfigureAwait(false);
        await using var observer = await ZonePeer.ConnectAsync(zone.Port).ConfigureAwait(false);

        await attacker.AuthAsync(fighter).ConfigureAwait(false);
        await observer.AuthAsync(mystic).ConfigureAwait(false);
        await attacker.EnterAsync(expectOthers: 0).ConfigureAwait(false);
        await observer.EnterAsync(fighter, mystic).ConfigureAwait(false);
        _ = await attacker.ExpectAsync<CharInfoMsg>(MsgType.CharInfo).ConfigureAwait(false);

        var gremlin = TalkingIsland.Gremlins[0];
        await attacker.MoveNearAsync(gremlin).ConfigureAwait(false);
        _ = await observer.ExpectAsync<MoveToMsg>(MsgType.MoveToAck).ConfigureAwait(false);

        await attacker.SendAsync(MsgType.SelectTarget, new SelectTargetMsg { TargetObjectId = gremlin.ObjectId })
            .ConfigureAwait(false);
        var target = await attacker.ExpectAsync<MyTargetSelectedMsg>(MsgType.MyTargetSelected).ConfigureAwait(false);
        if (target.ObjectId != gremlin.ObjectId)
            throw new InvalidOperationException($"target select was {target.ObjectId}");

        int hitsToKill = TalkingIsland.GremlinMaxHp / ZoneCombat.SampleMeleeDamage;
        for (int hit = 1; hit <= hitsToKill; hit++)
        {
            await attacker.SendAsync(MsgType.AttackRequest, new AttackRequestMsg()).ConfigureAwait(false);
            var atkLeft = await attacker.ExpectAsync<AttackMsg>(MsgType.Attack).ConfigureAwait(false);
            var hpLeft = await attacker.ExpectAsync<StatusUpdateMsg>(MsgType.StatusUpdate).ConfigureAwait(false);
            var atkRight = await observer.ExpectAsync<AttackMsg>(MsgType.Attack).ConfigureAwait(false);
            var hpRight = await observer.ExpectAsync<StatusUpdateMsg>(MsgType.StatusUpdate).ConfigureAwait(false);

            int expectedHp = TalkingIsland.GremlinMaxHp - hit * ZoneCombat.SampleMeleeDamage;
            ExpectCombat(gremlin.ObjectId, fighter.CharacterId, expectedHp, atkLeft, hpLeft);
            ExpectCombat(gremlin.ObjectId, fighter.CharacterId, expectedHp, atkRight, hpRight);
        }

        var delLeft = await attacker.ExpectAsync<DeleteObjectMsg>(MsgType.DeleteObject).ConfigureAwait(false);
        var delRight = await observer.ExpectAsync<DeleteObjectMsg>(MsgType.DeleteObject).ConfigureAwait(false);
        if (delLeft.ObjectId != gremlin.ObjectId || delRight.ObjectId != gremlin.ObjectId)
            throw new InvalidOperationException($"delete expected {gremlin.ObjectId}");

        await attacker.SendAsync(MsgType.Say, new SayMsg { Text = "gg", ChatType = 0 }).ConfigureAwait(false);
        ExpectSay(await attacker.ExpectAsync<CreatureSayMsg>(MsgType.CreatureSay).ConfigureAwait(false), fighter, "gg");
        ExpectSay(await observer.ExpectAsync<CreatureSayMsg>(MsgType.CreatureSay).ConfigureAwait(false), fighter, "gg");

        attacker.ExpectQuiet();
        observer.ExpectQuiet();
    }

    static void ExpectCombat(int targetId, int attackerId, int expectedCurHp, AttackMsg attack, StatusUpdateMsg status)
    {
        if (attack.AttackerObjectId != attackerId || attack.TargetObjectId != targetId ||
            attack.Damage != ZoneCombat.SampleMeleeDamage)
            throw new InvalidOperationException($"attack mismatch a={attack.AttackerObjectId} t={attack.TargetObjectId} d={attack.Damage}");
        if (status.ObjectId != targetId || status.AttributeIds.Length != 2 ||
            status.AttributeIds[0] != InterludeStatus.CurHp || status.AttributeIds[1] != InterludeStatus.MaxHp ||
            status.Values.Length != 2 || status.Values[0] != expectedCurHp ||
            status.Values[1] != TalkingIsland.GremlinMaxHp)
            throw new InvalidOperationException(
                $"status mismatch id={status.ObjectId} hp={status.Values[0]}/{status.Values[1]} expected={expectedCurHp}");
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

    static void ExpectMove(MoveToMsg move, int objectId, int destX, int destY)
    {
        if (move.ObjectId != objectId || move.OriginX != TalkingIsland.SpawnX || move.OriginY != TalkingIsland.SpawnY ||
            move.DestX != destX || move.DestY != destY)
            throw new InvalidOperationException($"move mismatch id={move.ObjectId} dest={move.DestX},{move.DestY}");
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

        public async Task<Arrival> EnterAsync(int expectOthers)
        {
            await SendAsync(MsgType.EnterWorld, new EnterWorldMsg()).ConfigureAwait(false);
            var spawned = await ExpectAsync<PlayerSpawnedMsg>(MsgType.PlayerSpawned).ConfigureAwait(false);
            var npcs = new List<NpcInfoMsg>(TalkingIsland.Gremlins.Length);
            for (int i = 0; i < TalkingIsland.Gremlins.Length; i++)
                npcs.Add(await ExpectAsync<NpcInfoMsg>(MsgType.NpcInfo).ConfigureAwait(false));
            var others = new List<CharInfoMsg>(expectOthers);
            for (int i = 0; i < expectOthers; i++)
                others.Add(await ExpectAsync<CharInfoMsg>(MsgType.CharInfo).ConfigureAwait(false));
            return new Arrival(spawned, npcs, others);
        }

        public async Task<Arrival> EnterAsync(HandoffTicket alreadyIn, HandoffTicket self)
        {
            var arrival = await EnterAsync(expectOthers: 1).ConfigureAwait(false);
            if (arrival.Spawned.ObjectId != self.CharacterId || arrival.Spawned.Name != self.CharacterName)
                throw new InvalidOperationException("second spawn mismatch");
            if (arrival.Others[0].ObjectId != alreadyIn.CharacterId)
                throw new InvalidOperationException("enter did not include the player already in the zone");
            return arrival;
        }

        public async Task MoveNearAsync(NpcInfoMsg npc)
        {
            await SendAsync(MsgType.MoveTo, new MoveToMsg
            {
                DestX = npc.X,
                DestY = npc.Y,
                DestZ = npc.Z
            }).ConfigureAwait(false);
            await ExpectAsync<MoveToMsg>(MsgType.MoveToAck).ConfigureAwait(false);
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

    readonly record struct Arrival(PlayerSpawnedMsg Spawned, List<NpcInfoMsg> Npcs, List<CharInfoMsg> Others);
}
