using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Fineage.Protocol;
using MessagePack;
using Xunit;

namespace Fineage.Login.Tests;

public class CharacterCreationTests
{
    [Fact]
    public void Name_length_forbidden_fragment_charset_and_template()
    {
        var service = Service();
        Assert.True(service.Register("alice", "secret1").Ok);

        Assert.Equal(LobbyProtocol.NameTooLong, service.CreateHuman("alice", "", HumanClasses.Fighter).Code);
        Assert.Equal(LobbyProtocol.NameTooLong, service.CreateHuman("alice", "   ", HumanClasses.Fighter).Code);
        Assert.Equal(LobbyProtocol.NameTooLong, service.CreateHuman("alice", new string('a', 17), HumanClasses.Fighter).Code);
        Assert.True(service.CreateHuman("alice", new string('b', 16), HumanClasses.Fighter).Ok);

        Assert.Equal(LobbyProtocol.InvalidName, service.CreateHuman("alice", "Bad Name", HumanClasses.Fighter).Code);
        Assert.Equal(LobbyProtocol.InvalidName, service.CreateHuman("alice", "xxAnnouyy", HumanClasses.Fighter).Code);
        Assert.Equal(LobbyProtocol.InvalidName, service.CreateHuman("alice", "AMMOU", HumanClasses.Mystic).Code);

        var templated = Service(create: new CharacterCreateOptions
        {
            NameTemplate = new Regex("^Z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
        });
        Assert.True(templated.Register("bob", "secret1").Ok);
        Assert.Equal(LobbyProtocol.InvalidName, templated.CreateHuman("bob", "Aria", HumanClasses.Fighter).Code);
        Assert.True(templated.CreateHuman("bob", "Zed", HumanClasses.Fighter).Ok);
    }

    [Fact]
    public void Appearance_ranges_depend_on_sex_and_log_a_warning()
    {
        var warnings = new List<string>();
        var service = Service(warn: warnings.Add);
        Assert.True(service.Register("alice", "secret1").Ok);

        var male = service.CreateHuman("alice", "MaleOk", HumanClasses.Fighter, sex: HumanClasses.SexMale, hairStyle: 4, hairColor: 3, face: 2);
        Assert.True(male.Ok, male.Message);
        Assert.Equal(4, male.Value!.HairStyle);
        Assert.Equal(3, male.Value.HairColor);
        Assert.Equal(2, male.Value.Face);
        Assert.Equal(HumanClasses.SexMale, male.Value.Sex);

        var female = service.CreateHuman("alice", "FemOk", HumanClasses.Mystic, sex: HumanClasses.SexFemale, hairStyle: 6, hairColor: 0, face: 0);
        Assert.True(female.Ok, female.Message);
        Assert.Equal(6, female.Value!.HairStyle);
        Assert.Equal(HumanClasses.SexFemale, female.Value.Sex);

        Assert.Equal(LobbyProtocol.CreationFailed, service.CreateHuman("alice", "MaleHair", HumanClasses.Fighter, hairStyle: 5).Code);
        Assert.Equal(LobbyProtocol.CreationFailed, service.CreateHuman("alice", "FemHair", HumanClasses.Fighter, sex: HumanClasses.SexFemale, hairStyle: 7).Code);
        Assert.Equal(LobbyProtocol.CreationFailed, service.CreateHuman("alice", "Face", HumanClasses.Fighter, face: 3).Code);
        Assert.Equal(LobbyProtocol.CreationFailed, service.CreateHuman("alice", "Color", HumanClasses.Fighter, hairColor: 4).Code);
        Assert.Equal(LobbyProtocol.CreationFailed, service.CreateHuman("alice", "Neg", HumanClasses.Fighter, face: -1).Code);
        Assert.Equal(5, warnings.Count);
        Assert.All(warnings, line => Assert.Contains("appearance rejected", line));
    }

    [Fact]
    public void Cap_includes_pending_deletes_and_zero_is_unlimited()
    {
        var capped = Service(deleteAfterDays: 7);
        Assert.True(capped.Register("alice", "secret1").Ok);
        for (int i = 0; i < 7; i++)
            Assert.True(capped.CreateHuman("alice", "Alt" + i, HumanClasses.Fighter).Ok);
        Assert.True(capped.Delete("alice", slot: 0).Ok);
        Assert.Equal(LobbyProtocol.CharacterLimit, capped.CreateHuman("alice", "Extra", HumanClasses.Mystic).Code);

        var open = Service(create: new CharacterCreateOptions { CharMaxNumber = 0 });
        Assert.True(open.Register("bob", "secret1").Ok);
        for (int i = 0; i < 8; i++)
            Assert.True(open.CreateHuman("bob", "Open" + i, HumanClasses.Fighter).Ok);
    }

    [Fact]
    public void Duplicate_name_is_case_insensitive_and_concurrent_creates_succeed_once()
    {
        var store = JsonAccountStore.InMemory();
        var service = Service(store);
        Assert.True(service.Register("alice", "secret1").Ok);
        Assert.True(service.Register("bob", "secret1").Ok);
        Assert.True(service.CreateHuman("alice", "Aria", HumanClasses.Fighter).Ok);
        Assert.Equal(LobbyProtocol.NameTaken, service.CreateHuman("bob", "aria", HumanClasses.Mystic).Code);

        int wins = 0;
        Parallel.For(0, 8, _ =>
        {
            var result = service.CreateHuman("alice", "SameOne", HumanClasses.Fighter);
            if (result.Ok)
                Interlocked.Increment(ref wins);
            else
                Assert.Equal(LobbyProtocol.NameTaken, result.Code);
        });
        Assert.Equal(1, wins);
        Assert.Equal(1, store.List("alice").Count(c => string.Equals(c.Name, "SameOne", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Non_base_class_and_disabled_race_fail_creation()
    {
        var service = Service();
        Assert.True(service.Register("alice", "secret1").Ok);
        Assert.Equal(LobbyProtocol.CreationFailed, service.CreateHuman("alice", "War", classId: 1).Code);
        Assert.Equal(LobbyProtocol.RaceNotSupported, service.CreateHuman("alice", "ElfF", classId: 18).Code);
        Assert.Equal(LobbyProtocol.ClassNotSupported, service.CreateHuman("alice", "Nope", classId: 200).Code);
        var elf = service.CreateHuman("alice", "ElfF", classId: 18, race: 1);
        Assert.True(elf.Ok, elf.Message);
        Assert.Equal(1, elf.Value!.Race);

        var elvesOnly = Service(create: new CharacterCreateOptions
        {
            AllowedRaces = new HashSet<int> { 1 },
            AllowedClasses = new HashSet<int> { HumanClasses.Fighter, HumanClasses.Mystic }
        });
        Assert.True(elvesOnly.Register("alice", "secret1").Ok);
        Assert.Equal(LobbyProtocol.CreationFailed, elvesOnly.CreateHuman("alice", "Human", HumanClasses.Fighter, race: 0).Code);
    }

    [Fact]
    public void Create_persists_appearance_sex_time_position_vitals_and_starter_kit()
    {
        var now = DateTimeOffset.UnixEpoch.AddHours(2);
        var store = JsonAccountStore.InMemory();
        var service = Service(store, () => now, create: new CharacterCreateOptions { StartingAdena = 40, StartingSp = 0 });
        Assert.True(service.Register("alice", "secret1").Ok);

        var created = service.CreateHuman(
            "alice", "Aria", HumanClasses.Fighter, sex: HumanClasses.SexFemale, hairStyle: 6, hairColor: 1, face: 2);
        Assert.True(created.Ok, created.Message);
        var character = created.Value!;
        Assert.Equal(HumanClasses.SexFemale, character.Sex);
        Assert.Equal(6, character.HairStyle);
        Assert.Equal(1, character.HairColor);
        Assert.Equal(2, character.Face);
        Assert.Equal(now, character.CreatedAt);
        Assert.Equal(1, character.Level);
        Assert.Equal("", character.Title);
        Assert.Equal(0, character.Sp);
        Assert.Equal(40, character.Adena);
        Assert.Equal(ClassTemplates.HumanFighterHp, character.MaxHp);
        Assert.Equal(character.MaxHp, character.Hp);
        Assert.Equal(ClassTemplates.HumanFighterMp, character.MaxMp);
        Assert.Equal(character.MaxMp, character.Mp);
        var point = ClassTemplates.Preview(HumanClasses.Fighter);
        Assert.True(character.HasPosition);
        Assert.Equal(point.X, character.X);
        Assert.Equal(point.Y, character.Y);
        Assert.Equal(point.Z, character.Z);
        Assert.Contains(character.Items!, item => item.ItemId == 2369 && item.EquippedSlot == 7);
        Assert.Contains(character.Items!, item => item.ItemId == 57 && item.Count == 40 && item.EquippedSlot == -1);

        var mystic = service.CreateHuman("alice", "Mage", HumanClasses.Mystic);
        Assert.Equal(ClassTemplates.HumanMysticHp, mystic.Value!.Hp);
        Assert.Equal(ClassTemplates.HumanMysticMp, mystic.Value.Mp);
        Assert.Equal(mystic.Value.MaxHp, mystic.Value.Hp);

        var roster = service.List("alice").Value!;
        var row = LobbyWire.ToSummaries(roster, now)[0];
        Assert.Equal("Aria", row.Name);
        Assert.Equal(6, row.HairStyle);
        Assert.Equal(point.X, row.X);
        Assert.Equal(HumanClasses.SexFemale, row.Sex);
    }

    [Fact]
    public void Every_base_class_matches_the_lobby_and_can_be_created()
    {
        var catalog = ClassTemplates.Catalog;
        var service = Service(create: new CharacterCreateOptions { CharMaxNumber = 0 });
        Assert.True(catalog.CreationListMatchesBaseClasses(service.CreatableClassIds));
        Assert.True(service.Register("alice", "secret1").Ok);

        foreach (int classId in catalog.BaseClassIds)
        {
            var template = catalog.Get(classId);
            var created = service.CreateHuman("alice", "C" + classId, classId, race: template.Race);
            Assert.True(created.Ok, created.Message);
            Assert.Equal(template.Race, created.Value!.Race);
            Assert.Equal(catalog.ModifiedHp(classId, 1), created.Value.MaxHp);
            Assert.Equal(catalog.ModifiedMp(classId, 1), created.Value.MaxMp);
            Assert.Equal(template.Spawns[0].X, created.Value.X);
            Assert.Equal(template.Spawns[0].Y, created.Value.Y);
            Assert.Equal(template.Spawns[0].Z, created.Value.Z);
        }

        Assert.Equal(LobbyProtocol.CreationFailed, service.CreateHuman("alice", "Third", classId: 88).Code);
        Assert.Throws<InvalidDataException>(() => Service(create: new CharacterCreateOptions
        {
            AllowedClasses = new HashSet<int> { 1 }
        }));
    }

    [Fact]
    public void Server_wide_toggle_rejects_create()
    {
        var service = Service(create: new CharacterCreateOptions { CreateAllowed = false });
        Assert.True(service.Register("alice", "secret1").Ok);
        Assert.Equal(LobbyProtocol.CreateNotAllowed, service.CreateHuman("alice", "Aria", HumanClasses.Fighter).Code);
    }

    [Fact]
    public async Task Wire_create_returns_the_character_then_the_roster()
    {
        var service = Service();
        Assert.True(service.Register("hero", "pass1234").Ok);
        var server = new LoginServer(service, new LoginServerOptions { SelectMinInterval = TimeSpan.Zero });
        await using var link = await Link.Open(server);

        await link.Send(MsgType.Auth, new AuthMsg { Account = "hero", Password = "pass1234" });
        await link.Expect(MsgType.LoginOk);
        await link.Send(MsgType.CreateCharacter, new CreateCharacterMsg
        {
            Name = "Aria",
            ClassId = HumanClasses.Fighter,
            Race = HumanClasses.RaceHuman,
            Sex = HumanClasses.SexFemale,
            HairStyle = 6,
            HairColor = 2,
            Face = 1
        });

        var created = await link.Expect<CharacterCreatedMsg>(MsgType.CharacterCreated);
        Assert.Equal("Aria", created.Name);
        Assert.Equal(6, created.HairStyle);
        Assert.Equal(2, created.HairColor);
        Assert.Equal(1, created.Face);
        Assert.Equal(ClassTemplates.Preview(HumanClasses.Fighter).X, created.X);

        var roster = await link.Expect<CharacterListMsg>(MsgType.CharacterList);
        var row = Assert.Single(roster.Characters);
        Assert.Equal(created.CharacterId, row.CharacterId);
        Assert.Equal(6, row.HairStyle);
        Assert.Equal(HumanClasses.SexFemale, row.Sex);
        Assert.Equal(created.X, row.X);
        Assert.Equal(1, row.Level);
    }

    static LoginService Service(
        IAccountStore? store = null,
        Func<DateTimeOffset>? clock = null,
        int? deleteAfterDays = null,
        CharacterCreateOptions? create = null,
        Action<string>? warn = null) =>
        new(store ?? JsonAccountStore.InMemory(), new HandoffTokens(HandoffTokens.DefaultSecret), clock, deleteAfterDays: deleteAfterDays, create: create, warn: warn);

    sealed class Link : IAsyncDisposable
    {
        readonly TcpListener _listener;
        readonly CancellationTokenSource _cts;
        readonly Task _accept;
        readonly TcpClient _client;
        readonly NetworkStream _stream;

        Link(TcpListener listener, CancellationTokenSource cts, Task accept, TcpClient client, NetworkStream stream)
        {
            _listener = listener;
            _cts = cts;
            _accept = accept;
            _client = client;
            _stream = stream;
        }

        public static async Task<Link> Open(LoginServer server)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var cts = new CancellationTokenSource();
            var accept = Task.Run(async () =>
            {
                using var accepted = await listener.AcceptTcpClientAsync(cts.Token);
                await using var stream = accepted.GetStream();
                await server.HandleAsync(stream, cts.Token);
            });
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            return new Link(listener, cts, accept, client, client.GetStream());
        }

        public Task Send<T>(MsgType type, T message) =>
            FrameIO.SendAsync(_stream, (ushort)type, MessagePackSerializer.Serialize(message));

        public async Task Expect(MsgType expected)
        {
            var frame = await Read();
            Assert.Equal((ushort)expected, frame.Type);
        }

        public async Task<T> Expect<T>(MsgType expected)
        {
            var frame = await Read();
            Assert.Equal((ushort)expected, frame.Type);
            return MessagePackSerializer.Deserialize<T>(frame.Body);
        }

        async Task<(ushort Type, byte[] Body)> Read()
        {
            var frame = await FrameIO.ReadAsync(_stream);
            Assert.NotNull(frame);
            return (frame!.Value.Type, frame.Value.Body);
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            _listener.Stop();
            await _cts.CancelAsync();
            try
            {
                await _accept.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
            }

            _cts.Dispose();
        }
    }
}
