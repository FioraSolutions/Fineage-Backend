using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius MacroList: make / delete persist and restore on enter (#927).</summary>
public sealed class ZoneMacroTests
{
    static async Task<(ZoneWorld World, ZoneSession Session, ZoneEntity Player)> Enter(
        MemoryAccountStore store, int characterId, string account, string name)
    {
        var world = new ZoneWorld(accounts: store, ledgerJournal: new MemoryLedgerJournal());
        var session = new ZoneSession();
        session.Bind(characterId, account, name, HumanClasses.Fighter);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        Assert.True(world.TryGetLivePlayer(session, out var player));
        return (world, session, player);
    }

    static (MemoryAccountStore Store, int CharacterId) NewCharacter()
    {
        var store = MemoryAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(login.Register("macro", "pass1234").Ok);
        var created = login.CreateHuman("macro", "MacroHero", HumanClasses.Fighter);
        return (store, created.Value!.Id);
    }

    static MakeMacroMsg NewMacro(string name, int id = 0, params MacroCmdMsg[] cmds) => new()
    {
        Id = id,
        Name = name,
        Descr = "d",
        Acronym = "M",
        Icon = 1,
        Commands = cmds.Length == 0
            ? [new MacroCmdMsg { Entry = 0, Type = 3, D1 = 0, D2 = 0, Cmd = "/sit" }]
            : cmds
    };

    [Fact]
    public async Task Make_delete_persist_across_relog()
    {
        var (store, id) = NewCharacter();
        var (world, session, player) = await Enter(store, id, "macro", "MacroHero");
        Assert.Empty(player.Macros);

        Assert.Null(await world.TryMakeMacroAsync(session, NewMacro("Attack"), CancellationToken.None));
        Assert.Single(player.Macros);
        int macroId = player.Macros.Keys.First();
        Assert.Equal(StoredMacro.FirstId, macroId);

        await world.LeaveAsync(session, CancellationToken.None);

        var (world2, session2, player2) = await Enter(store, id, "macro", "MacroHero");
        Assert.Single(player2.Macros);
        Assert.Equal("Attack", player2.Macros[macroId].Name);
        var saved = Assert.Single(store.FindById("macro", id)!.Macros!);
        Assert.Equal("Attack", saved.Name);

        Assert.Null(await world2.TryDeleteMacroAsync(session2, macroId, CancellationToken.None));
        Assert.Empty(player2.Macros);
        await world2.LeaveAsync(session2, CancellationToken.None);

        var (world3, session3, player3) = await Enter(store, id, "macro", "MacroHero");
        Assert.Empty(player3.Macros);
        Assert.Empty(store.FindById("macro", id)!.Macros ?? []);
        await world3.LeaveAsync(session3, CancellationToken.None);
    }

    [Fact]
    public async Task Empty_name_and_long_descr_refuse_without_persist()
    {
        var (store, id) = NewCharacter();
        var (world, session, _) = await Enter(store, id, "macro", "MacroHero");

        Assert.Null(await world.TryMakeMacroAsync(session, NewMacro(""), CancellationToken.None));
        var longDescr = NewMacro("x");
        longDescr.Descr = new string('a', MacroProtocol.MaxDescrLength + 1);
        Assert.Null(await world.TryMakeMacroAsync(session, longDescr, CancellationToken.None));
        Assert.Empty(store.FindById("macro", id)!.Macros ?? []);
        await world.LeaveAsync(session, CancellationToken.None);
    }

    [Fact]
    public async Task Delete_clears_macro_shortcuts()
    {
        var (store, id) = NewCharacter();
        var (world, session, player) = await Enter(store, id, "macro", "MacroHero");
        Assert.Null(await world.TryMakeMacroAsync(session, NewMacro("Buff"), CancellationToken.None));
        int macroId = player.Macros.Keys.First();
        Assert.Null(world.RegisterShortcut(session, StoredShortcut.TypeMacro, 0, macroId, 0).Error);
        Assert.Single(player.Shortcuts);

        Assert.Null(await world.TryDeleteMacroAsync(session, macroId, CancellationToken.None));
        Assert.Empty(player.Macros);
        Assert.Empty(player.Shortcuts);
        await world.LeaveAsync(session, CancellationToken.None);
    }

    [Fact]
    public void Encode_decode_commands_round_trip()
    {
        var cmds = new StoredMacroCmd[]
        {
            new(0, 3, 1, 0, "/sit"),
            new(1, 1, 57, 1, "")
        };
        string raw = ZoneWorld.EncodeMacroCommands(cmds);
        Assert.True(raw.Length <= StoredMacro.MaxCommandsLength);
        var back = ZoneWorld.DecodeMacroCommands(raw);
        Assert.Equal(2, back.Count);
        Assert.Equal(3, back[0].Type);
        Assert.Equal("/sit", back[0].Cmd);
        Assert.Equal(57, back[1].D1);
    }
}
