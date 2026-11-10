using System.Net.Sockets;
using Fineage.Game;
using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Net.Host;

namespace Fineage.Zone;

public static class ZoneAcceptance
{
    public static async Task RunAsync()
    {
        // Mint handoffs in-process via Fineage.Accounts — Zone must not reference Fineage.Login.
        // Auth is HMAC-only; loopback ZoneHost runs with accounts=null and grants starter kits on enter.
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var now = DateTimeOffset.UtcNow;
        var fighter = SeedHandoff(tokens, now, characterId: 1, "selftest", "HumanOne", HumanClasses.Fighter, HumanClasses.RaceHuman);
        var mystic = SeedHandoff(tokens, now, characterId: 2, "selftest2", "HumanTwo", HumanClasses.Mystic, HumanClasses.RaceHuman);

        await using (var zone = ZoneHost.StartNet(tokens))
        {
            await PlayAsync(zone.Port, fighter.Account, fighter.Token, fighter.CharacterId, fighter.CharacterName)
                .ConfigureAwait(false);
            await PlayAsync(zone.Port, mystic.Account, mystic.Token, mystic.CharacterId, mystic.CharacterName)
                .ConfigureAwait(false);
        }
    }

    static HandoffTicket SeedHandoff(
        HandoffTokens tokens,
        DateTimeOffset now,
        int characterId,
        string account,
        string name,
        int classId,
        int race) =>
        tokens.Issue(
            new CharacterRecord(
                characterId,
                name,
                account,
                classId,
                race,
                HumanClasses.SexMale,
                Level: 1,
                CreatedAt: now,
                LastAccess: now),
            now);

    public static void Write(TextWriter output) => output.WriteLine("selftest OK");

    static async Task PlayAsync(int port, string account, string token, int characterId, string name)
    {
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port).ConfigureAwait(false);
        await using var conn = new NetConnection(client, 256);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task read = conn.RunReadLoopAsync(cts.Token);
        try
        {
            var link = new NetLink(conn);

            await link.SendAsync(NetOpcode.Hello, NetCodec.EncodeHello(new HelloMsg { ClientName = "selftest" }));
            await link.ExpectAsync(NetOpcode.HelloAck);

            // EnterWorld before authentication must be refused, and a wrong token is not a credential.
            await link.SendAsync(NetOpcode.EnterWorld, NetCodec.EncodeEnterWorld(new EnterWorldMsg()));
            Require(await link.ExpectCodeAsync(NetOpcode.Reject) == "auth_required", "early enter must be auth_required");

            await link.SendAsync(NetOpcode.ZoneAuth, NetCodec.EncodeZoneAuth(new ZoneAuthMsg { Account = account, Token = "not-a-token" }));
            var rejected = NetCodec.DecodeZoneAuthResult(await link.ExpectPayloadAsync(NetOpcode.ZoneAuthResult));
            Require(!rejected.Ok, "a bad token must not authenticate");

            await link.SendAsync(NetOpcode.ZoneAuth, NetCodec.EncodeZoneAuth(new ZoneAuthMsg { Account = account, Token = token }));
            var auth = NetCodec.DecodeZoneAuthResult(await link.ExpectPayloadAsync(NetOpcode.ZoneAuthResult));
            Require(auth.Ok, $"handoff token must be accepted: {auth.Code} {auth.Message}");

            await link.SendAsync(NetOpcode.MoveInput, NetCodec.EncodeMoveInput(new MoveInputMsg
            {
                DestX = TalkingIsland.SpawnX + 10,
                DestY = TalkingIsland.SpawnY,
                DestZ = TalkingIsland.SpawnZ
            }));
            Require(await link.ExpectCodeAsync(NetOpcode.Reject) == "not_in_world", "move before enter must be rejected");

            await link.SendAsync(NetOpcode.EnterWorld, NetCodec.EncodeEnterWorld(new EnterWorldMsg()));
            var order = new List<ushort>();
            UserInfoNetMsg? user = null;
            ItemListNetMsg? items = null;
            var npcs = new List<NpcSpawnNetMsg>();
            SystemMessageNetMsg? welcome = null;
            EntitySpawnMsg? spawned = null;

            // Net enter burst: self sheets, ShortCutInit, then the welcome line, then the knownlist spawns.
            // The knownlist has no end marker, so the burst closes once a quiet window passes after the welcome.
            using var burst = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            while (true)
            {
                PacketEnvelope? envelope;
                try
                {
                    if (welcome != null)
                        burst.CancelAfter(TimeSpan.FromMilliseconds(350));
                    envelope = await link.NextAsync(burst.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (welcome != null && ex is OperationCanceledException)
                {
                    break;
                }
                order.Add(envelope.Opcode);
                switch (envelope.Opcode)
                {
                    case NetOpcode.UserInfo:
                        user = NetCodec.DecodeUserInfo(envelope.Payload);
                        break;
                    case NetOpcode.ItemList:
                        items = NetCodec.DecodeItemList(envelope.Payload);
                        break;
                    case NetOpcode.SkillList:
                        break;
                    case NetOpcode.EntitySpawn:
                        var spawn = NetCodec.DecodeEntitySpawn(envelope.Payload);
                        if (spawn.IsSelf)
                            spawned = spawn;
                        else if (spawn.Kind == EntityKindNet.Npc)
                            npcs.AddRange(Unpack(spawn));
                        break;
                    case NetOpcode.SystemMessage:
                        welcome ??= NetCodec.DecodeSystemMessage(envelope.Payload);
                        break;
                }
            }

            Require(user != null && items != null && spawned != null, "enter burst must carry UserInfo, ItemList and the self spawn");
            Require(order.IndexOf(NetOpcode.UserInfo) < order.IndexOf(NetOpcode.ItemList), "UserInfo precedes ItemList");
            Require(order.IndexOf(NetOpcode.ItemList) < order.IndexOf(NetOpcode.SkillList), "ItemList precedes SkillList");
            var self = spawned!;
            var selfUser = user!;
            Require(self.Name == name && selfUser.Name == name, $"self mismatch spawn={self.Name} user={selfUser.Name}");
            Require(selfUser.X == self.X && selfUser.Y == self.Y && selfUser.Z == self.Z, "UserInfo position must match the spawn");
            Require(npcs.Count > 0, "enter must stream the visible NPCs");
            Require(npcs.Any(n => string.Equals(n.Name, "Gremlin", StringComparison.OrdinalIgnoreCase)),
                "enter knownlist must include a Gremlin (#138 smoke)");
            Require(welcome!.Id == 1 && welcome.Params.Length == 1 && welcome.Params[0] == name,
                $"welcome id={welcome.Id} text={welcome.Text}");

            bool fighter = items!.Items.Any(item => item.ItemId == 219);
            int weaponId = fighter ? 219 : 177;
            var weapon = items.Items.FirstOrDefault(item => item.ItemId == weaponId);
            Require(weapon != null && weapon.EquippedSlot == 7, $"starter weapon {weaponId} must be equipped in the right hand");
            Require(selfUser.Paperdoll.Length == InventoryMessages.PaperdollSlotCount && selfUser.Paperdoll[7].ItemId == weaponId,
                "UserInfo paperdoll must show the equipped weapon");

            var nearest = TalkingIsland.Gremlins[0];
            int destX = nearest.X, destY = nearest.Y, destZ = nearest.Z;
            if (!MoveLimits.WithinStep(self.X, self.Y, destX, destY))
            {
                destX = self.X + 10;
                destY = self.Y;
                destZ = self.Z;
            }

            await link.SendAsync(NetOpcode.MoveInput, NetCodec.EncodeMoveInput(new MoveInputMsg
            {
                DestX = destX,
                DestY = destY,
                DestZ = destZ
            }));
            var ack = NetCodec.DecodeMoveState((await link.ExpectNextAsync(NetOpcode.MoveState).ConfigureAwait(false)).Payload);
            Require(ack.DestX == destX && ack.DestY == destY, $"move ack mismatch dest={ack.DestX},{ack.DestY}");
        }
        finally
        {
            try { await cts.CancelAsync(); } catch { /* ignore */ }
            try { await read.ConfigureAwait(false); } catch { /* shutdown */ }
        }
    }

    /// <summary>A single spawn envelope carries one entity; NPC batches are only a test-side convenience.</summary>
    static IEnumerable<NpcSpawnNetMsg> Unpack(EntitySpawnMsg spawn)
    {
        yield return new NpcSpawnNetMsg
        {
            EntityValue = spawn.EntityValue,
            Name = spawn.Name,
            X = spawn.X,
            Y = spawn.Y,
            Z = spawn.Z
        };
    }

    static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    sealed class NpcSpawnNetMsg
    {
        public ulong EntityValue { get; init; }
        public string Name { get; init; } = "";
        public int X { get; init; }
        public int Y { get; init; }
        public int Z { get; init; }
    }

    /// <summary>Request/response client over <see cref="NetConnection"/> with a monotonic sequence.</summary>
    sealed class NetLink(NetConnection conn)
    {
        uint _sequence;

        public Task SendAsync(ushort opcode, byte[] payload) =>
            conn.SendAsync(opcode, payload, unchecked(++_sequence), CancellationToken.None);

        public async Task<PacketEnvelope> NextAsync(CancellationToken ct = default) =>
            await conn.Inbox.ReadAsync(ct).ConfigureAwait(false);

        public async Task<PacketEnvelope> ExpectAsync(ushort expected)
        {
            PacketEnvelope envelope = await NextAsync().ConfigureAwait(false);
            Require(envelope.Opcode == expected,
                $"expected opcode 0x{expected:X4}, got 0x{envelope.Opcode:X4}");
            return envelope;
        }

        /// <summary>
        /// Waits for <paramref name="expected"/>, skipping unrelated fanout. The server pushes presence and
        /// status frames that have nothing to do with the request being asserted, and their timing is load
        /// dependent, so a strict next-frame match turns those into flakes.
        /// </summary>
        public async Task<PacketEnvelope> ExpectNextAsync(ushort expected)
        {
            while (true)
            {
                PacketEnvelope envelope = await NextAsync().ConfigureAwait(false);
                if (envelope.Opcode == expected)
                    return envelope;

                Require(envelope.Opcode != NetOpcode.Reject,
                    $"unexpected reject while waiting for 0x{expected:X4}: {NetCodec.DecodeReject(envelope.Payload).Code}");
            }
        }

        public async Task<byte[]> ExpectPayloadAsync(ushort expected) =>
            (await ExpectAsync(expected).ConfigureAwait(false)).Payload;

        public async Task<string> ExpectCodeAsync(ushort expected) =>
            NetCodec.DecodeReject((await ExpectPayloadAsync(expected).ConfigureAwait(false))).Code;
    }
}
