using System.Net;
using System.Net.Sockets;
using Fineage.Game;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Drops;
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

        await left.SendAsync(MsgType.Say, new SayMsg { Text = "hello zone", ChatType = ChatProtocol.All }).ConfigureAwait(false);
        var saidLeft = await left.ExpectAsync<CreatureSayMsg>(MsgType.CreatureSay).ConfigureAwait(false);
        ExpectSay(saidLeft, fighter, "hello zone");

        await right.SendAsync(MsgType.Say, new SayMsg { Text = "back", ChatType = ChatProtocol.Shout }).ConfigureAwait(false);
        ExpectSay(await right.ExpectAsync<CreatureSayMsg>(MsgType.CreatureSay).ConfigureAwait(false), mystic, "back", ChatProtocol.Shout);
        ExpectSay(await left.ExpectAsync<CreatureSayMsg>(MsgType.CreatureSay).ConfigureAwait(false), mystic, "back", ChatProtocol.Shout);

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

    public static async Task RunCombatSharedAsync()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var login = new LoginService(JsonAccountStore.InMemory(), tokens);
        var fighter = Play(login, "combat1", "Striker", HumanClasses.Fighter);
        var mystic = Play(login, "combat2", "Watcher", HumanClasses.Mystic);

        var world = new ZoneWorld(
            spawnRandom: new Random(MobiusDropTestSeed.DefaultSeed),
            dropRandom: new Random(MobiusDropTestSeed.DefaultSeed));
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

        var dropLeft = await attacker.ExpectAsync<SpawnItemMsg>(MsgType.SpawnItem).ConfigureAwait(false);
        var dropRight = await observer.ExpectAsync<SpawnItemMsg>(MsgType.SpawnItem).ConfigureAwait(false);
        ExpectGroundDrop(dropLeft, dropRight, gremlin);

        await attacker.SendAsync(MsgType.Say, new SayMsg { Text = "gg", ChatType = ChatProtocol.Shout }).ConfigureAwait(false);
        ExpectSay(await attacker.ExpectAsync<CreatureSayMsg>(MsgType.CreatureSay).ConfigureAwait(false), fighter, "gg", ChatProtocol.Shout);
        ExpectSay(await observer.ExpectAsync<CreatureSayMsg>(MsgType.CreatureSay).ConfigureAwait(false), fighter, "gg", ChatProtocol.Shout);

        attacker.ExpectQuiet();
        observer.ExpectQuiet();
    }

    public static async Task RunGroundPickupSharedAsync()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var login = new LoginService(JsonAccountStore.InMemory(), tokens);
        var picker = Play(login, "pick1", "Picker", HumanClasses.Fighter);
        var observer = Play(login, "pick2", "Watcher", HumanClasses.Mystic);

        var world = new ZoneWorld(
            spawnRandom: new Random(MobiusDropTestSeed.DefaultSeed),
            dropRandom: new Random(MobiusDropTestSeed.DefaultSeed));
        await using var zone = ZoneHost.Start(tokens, world);
        await using var left = await ZonePeer.ConnectAsync(zone.Port).ConfigureAwait(false);
        await using var right = await ZonePeer.ConnectAsync(zone.Port).ConfigureAwait(false);

        await left.AuthAsync(picker).ConfigureAwait(false);
        await right.AuthAsync(observer).ConfigureAwait(false);
        await left.EnterAsync(expectOthers: 0).ConfigureAwait(false);
        await right.EnterAsync(picker, observer).ConfigureAwait(false);
        _ = await left.ExpectAsync<CharInfoMsg>(MsgType.CharInfo).ConfigureAwait(false);

        var gremlin = TalkingIsland.Gremlins[0];
        await left.MoveNearAsync(gremlin).ConfigureAwait(false);
        _ = await right.ExpectAsync<MoveToMsg>(MsgType.MoveToAck).ConfigureAwait(false);

        await left.SendAsync(MsgType.SelectTarget, new SelectTargetMsg { TargetObjectId = gremlin.ObjectId })
            .ConfigureAwait(false);
        _ = await left.ExpectAsync<MyTargetSelectedMsg>(MsgType.MyTargetSelected).ConfigureAwait(false);

        int hitsToKill = TalkingIsland.GremlinMaxHp / ZoneCombat.SampleMeleeDamage;
        for (int hit = 1; hit <= hitsToKill; hit++)
        {
            await left.SendAsync(MsgType.AttackRequest, new AttackRequestMsg()).ConfigureAwait(false);
            _ = await left.ExpectAsync<AttackMsg>(MsgType.Attack).ConfigureAwait(false);
            _ = await left.ExpectAsync<StatusUpdateMsg>(MsgType.StatusUpdate).ConfigureAwait(false);
            _ = await right.ExpectAsync<AttackMsg>(MsgType.Attack).ConfigureAwait(false);
            _ = await right.ExpectAsync<StatusUpdateMsg>(MsgType.StatusUpdate).ConfigureAwait(false);
        }

        _ = await left.ExpectAsync<DeleteObjectMsg>(MsgType.DeleteObject).ConfigureAwait(false);
        _ = await right.ExpectAsync<DeleteObjectMsg>(MsgType.DeleteObject).ConfigureAwait(false);
        var dropLeft = await left.ExpectAsync<SpawnItemMsg>(MsgType.SpawnItem).ConfigureAwait(false);
        var dropRight = await right.ExpectAsync<SpawnItemMsg>(MsgType.SpawnItem).ConfigureAwait(false);
        ExpectGroundDrop(dropLeft, dropRight, gremlin);

        await left.SendAsync(MsgType.PickupRequest, new PickupRequestMsg { ObjectId = dropLeft.ObjectId })
            .ConfigureAwait(false);
        var update = await left.ExpectAsync<InventoryUpdateMsg>(MsgType.InventoryUpdate).ConfigureAwait(false);
        var goneLeft = await left.ExpectAsync<DeleteObjectMsg>(MsgType.DeleteObject).ConfigureAwait(false);
        var goneRight = await right.ExpectAsync<DeleteObjectMsg>(MsgType.DeleteObject).ConfigureAwait(false);
        if (goneLeft.ObjectId != dropLeft.ObjectId || goneRight.ObjectId != dropLeft.ObjectId)
            throw new InvalidOperationException($"pickup delete expected {dropLeft.ObjectId}");
        if (update.Changes.Length != 1)
            throw new InvalidOperationException($"expected one inventory change, got {update.Changes.Length}");
        var row = update.Changes[0];
        var expected = MobiusDropTestSeed.ExpectedGremlinDeathDrop();
        if (row.Change != InventoryChange.Add || row.ItemId != expected.ItemId || row.Count != expected.Count)
            throw new InvalidOperationException($"inventory add item={row.ItemId} count={row.Count}");

        left.ExpectQuiet();
        right.ExpectQuiet();
    }

    public static async Task RunSpoilAdminAsync()
    {
        if (!ZoneAdmin.Enabled)
            throw new InvalidOperationException($"{ZoneAdmin.EnvVar} must be enabled for spoil admin selftest.");

        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var login = new LoginService(JsonAccountStore.InMemory(), tokens);
        var fighter = Play(login, "spoil1", "Spoiler", HumanClasses.Fighter);

        var world = new ZoneWorld(spawnRandom: new Random(901), dropRandom: new Random(901));
        await using var zone = ZoneHost.Start(tokens, world);
        await using var peer = await ZonePeer.ConnectAsync(zone.Port).ConfigureAwait(false);
        await peer.AuthAsync(fighter).ConfigureAwait(false);
        await peer.EnterAsync(TalkingIsland.Gremlins.Length, expectOthers: 0).ConfigureAwait(false);

        var gremlin = TalkingIsland.Gremlins[0];
        await peer.MoveNearAsync(gremlin).ConfigureAwait(false);
        await peer.SendAsync(MsgType.AdminSpoilTarget, new AdminSpoilTargetMsg { TargetObjectId = gremlin.ObjectId })
            .ConfigureAwait(false);

        await peer.SendAsync(MsgType.SelectTarget, new SelectTargetMsg { TargetObjectId = gremlin.ObjectId })
            .ConfigureAwait(false);
        _ = await peer.ExpectAsync<MyTargetSelectedMsg>(MsgType.MyTargetSelected).ConfigureAwait(false);

        int hits = TalkingIsland.GremlinMaxHp / ZoneCombat.SampleMeleeDamage;
        for (int hit = 0; hit < hits; hit++)
        {
            await peer.SendAsync(MsgType.AttackRequest, new AttackRequestMsg()).ConfigureAwait(false);
            _ = await peer.ExpectAsync<AttackMsg>(MsgType.Attack).ConfigureAwait(false);
            _ = await peer.ExpectAsync<StatusUpdateMsg>(MsgType.StatusUpdate).ConfigureAwait(false);
        }

        _ = await peer.ExpectAsync<SpawnItemMsg>(MsgType.SpawnItem).ConfigureAwait(false);

        await peer.SendAsync(MsgType.AdminSweepCorpse, new AdminSweepCorpseMsg { TargetObjectId = gremlin.ObjectId })
            .ConfigureAwait(false);
        var update = await peer.ExpectAsync<InventoryUpdateMsg>(MsgType.InventoryUpdate).ConfigureAwait(false);
        _ = await peer.ExpectAsync<DeleteObjectMsg>(MsgType.DeleteObject).ConfigureAwait(false);

        if (update.Changes.Length != 1 || update.Changes[0].Change != InventoryChange.Add ||
            update.Changes[0].ItemId != 1864 || update.Changes[0].Count < 1)
            throw new InvalidOperationException($"sweep inventory item={update.Changes[0].ItemId} count={update.Changes[0].Count}");

        peer.ExpectQuiet();
    }

    static void ExpectGroundDrop(SpawnItemMsg left, SpawnItemMsg right, NpcInfoMsg gremlin)
    {
        var expected = MobiusDropTestSeed.ExpectedGremlinDeathDrop();
        if (left.ObjectId != right.ObjectId || left.ObjectId < PlayerInventory.FirstObjectId)
            throw new InvalidOperationException($"drop object id {left.ObjectId}/{right.ObjectId}");
        if (left.ItemId != expected.ItemId || left.Count != expected.Count || !left.Stackable)
            throw new InvalidOperationException($"drop item={left.ItemId} count={left.Count} stackable={left.Stackable}");
        if (left.X != gremlin.X || left.Y != gremlin.Y || left.Z != gremlin.Z)
            throw new InvalidOperationException($"drop @ {left.X},{left.Y},{left.Z} expected gremlin tile");
    }

    public static async Task RunBuffSharedAsync()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var login = new LoginService(JsonAccountStore.InMemory(), tokens);
        var caster = Play(login, "buff1", "Windy", HumanClasses.Fighter);
        var observer = Play(login, "buff2", "Looker", HumanClasses.Mystic);

        var world = new ZoneWorld();
        await using var zone = ZoneHost.Start(tokens, world);
        await using var left = await ZonePeer.ConnectAsync(zone.Port).ConfigureAwait(false);
        await using var right = await ZonePeer.ConnectAsync(zone.Port).ConfigureAwait(false);

        await left.AuthAsync(caster).ConfigureAwait(false);
        await right.AuthAsync(observer).ConfigureAwait(false);
        await left.EnterAsync(expectOthers: 0).ConfigureAwait(false);
        await right.EnterAsync(caster, observer).ConfigureAwait(false);
        _ = await left.ExpectAsync<CharInfoMsg>(MsgType.CharInfo).ConfigureAwait(false);

        await left.SendAsync(MsgType.BuffRequest, new BuffRequestMsg
        {
            EffectId = ZoneBuffs.SampleWindWalk
        }).ConfigureAwait(false);

        var selfUpdate = await left.ExpectAsync<AbnormalStatusUpdateMsg>(MsgType.AbnormalStatusUpdate).ConfigureAwait(false);
        var peerUpdate = await right.ExpectAsync<AbnormalStatusUpdateMsg>(MsgType.AbnormalStatusUpdate).ConfigureAwait(false);
        ExpectAbnormal(selfUpdate, caster.CharacterId, ZoneBuffs.SampleWindWalk);
        ExpectAbnormal(peerUpdate, caster.CharacterId, ZoneBuffs.SampleWindWalk);

        await left.SendAsync(MsgType.BuffRequest, new BuffRequestMsg { Remove = true }).ConfigureAwait(false);
        var clearedSelf = await left.ExpectAsync<AbnormalStatusUpdateMsg>(MsgType.AbnormalStatusUpdate).ConfigureAwait(false);
        var clearedPeer = await right.ExpectAsync<AbnormalStatusUpdateMsg>(MsgType.AbnormalStatusUpdate).ConfigureAwait(false);
        if (clearedSelf.ObjectId != caster.CharacterId || clearedSelf.Effects.Length != 0)
            throw new InvalidOperationException("self clear mismatch");
        if (clearedPeer.ObjectId != caster.CharacterId || clearedPeer.Effects.Length != 0)
            throw new InvalidOperationException("peer clear mismatch");

        left.ExpectQuiet();
        right.ExpectQuiet();
    }

    public static async Task RunRegenSharedAsync()
    {
        string? priorTick = Environment.GetEnvironmentVariable("FINEAGE_ZONE_TICK_MS");
        Environment.SetEnvironmentVariable("FINEAGE_ZONE_TICK_MS", "25");
        try
        {
            var store = JsonAccountStore.InMemory();
            var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
            var login = new LoginService(store, tokens);
            var fighter = Play(login, "regen1", "Fighter", HumanClasses.Fighter);
            var mystic = Play(login, "regen2", "Mystic", HumanClasses.Mystic);
            if (!store.TrySaveVitals(fighter.Account, fighter.CharacterId, hp: 100, mp: 30) ||
                !store.TrySaveVitals(mystic.Account, mystic.CharacterId, hp: 80, mp: 40))
                throw new InvalidOperationException("failed to seed wounded vitals");

            var world = new ZoneWorld(accounts: store);
            await using var zone = ZoneHost.Start(tokens, world);
            await using var left = await ZonePeer.ConnectAsync(zone.Port).ConfigureAwait(false);
            await using var right = await ZonePeer.ConnectAsync(zone.Port).ConfigureAwait(false);

            await left.AuthAsync(fighter).ConfigureAwait(false);
            await right.AuthAsync(mystic).ConfigureAwait(false);
            await left.EnterAsync(expectOthers: 0).ConfigureAwait(false);
            await right.EnterAsync(fighter, mystic, TalkingIsland.Gremlins.Length).ConfigureAwait(false);

            var selfStatus = await AwaitRegenUpdateAsync(left, fighter.CharacterId).ConfigureAwait(false);
            var peerStatus = await AwaitRegenUpdateAsync(right, fighter.CharacterId).ConfigureAwait(false);
            ExpectRegen(selfStatus, fighter.CharacterId, ClassTemplates.HumanFighterHp, ClassTemplates.HumanFighterMp,
                minHp: 101, minMp: 31);
            ExpectRegen(peerStatus, fighter.CharacterId, ClassTemplates.HumanFighterHp, ClassTemplates.HumanFighterMp,
                minHp: 101, minMp: 31);
            if (peerStatus.Values[0] < selfStatus.Values[0] || peerStatus.Values[2] < selfStatus.Values[2])
                throw new InvalidOperationException("observer regen lagged behind self");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FINEAGE_ZONE_TICK_MS", priorTick);
        }
    }

    public static async Task RunPartySharedAsync()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var login = new LoginService(JsonAccountStore.InMemory(), tokens);
        var leader = Play(login, "party1", "Leader", HumanClasses.Fighter);
        var member = Play(login, "party2", "Member", HumanClasses.Mystic);
        if (leader.CharacterId == member.CharacterId)
            throw new InvalidOperationException("party selftest ids collided");

        var world = new ZoneWorld();
        await using var zone = ZoneHost.Start(tokens, world);
        await using var left = await ZonePeer.ConnectAsync(zone.Port).ConfigureAwait(false);
        await using var right = await ZonePeer.ConnectAsync(zone.Port).ConfigureAwait(false);

        await left.AuthAsync(leader).ConfigureAwait(false);
        await right.AuthAsync(member).ConfigureAwait(false);
        await left.EnterAsync(expectOthers: 0).ConfigureAwait(false);
        await right.EnterAsync(leader, member, TalkingIsland.Gremlins.Length).ConfigureAwait(false);
        var peerSeen = await left.ExpectAsync<CharInfoMsg>(MsgType.CharInfo).ConfigureAwait(false);
        if (peerSeen.ObjectId != member.CharacterId)
            throw new InvalidOperationException("leader did not receive member CharInfo before party invite");

        await left.SendAsync(MsgType.PartyInviteRequest, new PartyInviteRequestMsg
        {
            TargetObjectId = member.CharacterId,
            ItemDistribution = 0
        }).ConfigureAwait(false);

        var ask = await right.ExpectAsync<PartyAskJoinMsg>(MsgType.PartyAskJoin).ConfigureAwait(false);
        if (ask.RequestorObjectId != leader.CharacterId || ask.RequestorName != leader.CharacterName || ask.ItemDistribution != 0)
            throw new InvalidOperationException("AskJoin mismatch");

        await right.SendAsync(MsgType.PartyAnswerJoinRequest, new PartyAnswerJoinRequestMsg
        {
            RequestorObjectId = leader.CharacterId,
            Response = 1
        }).ConfigureAwait(false);

        var joined = await left.ExpectAsync<PartyJoinResultMsg>(MsgType.PartyJoinResult).ConfigureAwait(false);
        if (joined.Response != 1)
            throw new InvalidOperationException("join result expected accept");

        var listLeft = await left.ExpectAsync<PartyMemberListMsg>(MsgType.PartyMemberList).ConfigureAwait(false);
        var listRight = await right.ExpectAsync<PartyMemberListMsg>(MsgType.PartyMemberList).ConfigureAwait(false);
        ExpectPartyList(listLeft, leader.CharacterId, leader.CharacterId, member.CharacterId);
        ExpectPartyList(listRight, leader.CharacterId, leader.CharacterId, member.CharacterId);

        await right.SendAsync(MsgType.PartyLeaveRequest, new PartyLeaveRequestMsg()).ConfigureAwait(false);
        var cleared = await left.ExpectAsync<PartyMemberListMsg>(MsgType.PartyMemberList).ConfigureAwait(false);
        if (cleared.Members.Length != 0 || cleared.PartyId != 0)
            throw new InvalidOperationException("leader should receive empty party list after member leaves");

        left.ExpectQuiet();
        right.ExpectQuiet();
    }

    static void ExpectPartyList(PartyMemberListMsg list, int leaderId, params int[] memberIds)
    {
        if (list.LeaderObjectId != leaderId || list.Members.Length != memberIds.Length || list.PartyId == 0)
            throw new InvalidOperationException($"party list leader={list.LeaderObjectId} count={list.Members.Length}");
        for (int i = 0; i < memberIds.Length; i++)
        {
            if (list.Members[i].ObjectId != memberIds[i])
                throw new InvalidOperationException($"party member {i} id={list.Members[i].ObjectId}");
        }
    }

    static async Task<StatusUpdateMsg> AwaitRegenUpdateAsync(ZonePeer peer, int objectId)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            var (type, body) = await peer.ReadFrameAsync().ConfigureAwait(false);
            if (type == MsgType.CharInfo)
                continue;
            if (type != MsgType.StatusUpdate)
                throw new InvalidOperationException($"expected regen StatusUpdate, got {type}");
            var status = MessagePackSerializer.Deserialize<StatusUpdateMsg>(body);
            if (status.ObjectId == objectId)
                return status;
        }

        throw new InvalidOperationException($"timed out waiting for regen StatusUpdate object={objectId}");
    }

    static void ExpectRegen(
        StatusUpdateMsg status,
        int objectId,
        int maxHp,
        int maxMp,
        int minHp,
        int minMp)
    {
        if (status.ObjectId != objectId || status.AttributeIds.Length != 4 ||
            status.AttributeIds[0] != InterludeStatus.CurHp ||
            status.AttributeIds[1] != InterludeStatus.MaxHp ||
            status.AttributeIds[2] != InterludeStatus.CurMp ||
            status.AttributeIds[3] != InterludeStatus.MaxMp ||
            status.Values.Length != 4)
            throw new InvalidOperationException($"regen status shape mismatch id={status.ObjectId}");
        if (status.Values[1] != maxHp || status.Values[3] != maxMp)
            throw new InvalidOperationException($"regen max mismatch hp={status.Values[1]} mp={status.Values[3]}");
        if (status.Values[0] < minHp || status.Values[2] < minMp)
            throw new InvalidOperationException(
                $"regen too low hp={status.Values[0]}/{maxHp} mp={status.Values[2]}/{maxMp} expected>={minHp}/{minMp}");
    }

    static void ExpectAbnormal(AbnormalStatusUpdateMsg update, int objectId, int effectId)
    {
        if (update.ObjectId != objectId)
            throw new InvalidOperationException($"abnormal object={update.ObjectId} expected={objectId}");
        if (update.Effects.Length != 1)
            throw new InvalidOperationException($"expected one effect, got {update.Effects.Length}");
        var effect = update.Effects[0];
        if (effect.Id != effectId || effect.Value != ZoneBuffs.SampleEffectValue ||
            effect.DurationSeconds != ZoneBuffs.SampleDurationSeconds)
            throw new InvalidOperationException($"effect id={effect.Id} value={effect.Value} dur={effect.DurationSeconds}");
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

    static void ExpectSay(CreatureSayMsg say, HandoffTicket who, string text, int chatType = ChatProtocol.All)
    {
        if (say.ObjectId != who.CharacterId || say.Name != who.CharacterName || say.Text != text || say.ChatType != chatType)
            throw new InvalidOperationException($"say mismatch id={say.ObjectId} name={say.Name} text={say.Text} type={say.ChatType}");
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
        foreach (var point in ClassTemplates.Points(classId))
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

        public Task<Arrival> EnterAsync(int expectOthers) =>
            EnterAsync(TalkingIsland.Gremlins.Length, expectOthers);

        public async Task<Arrival> EnterAsync(int expectNpcs, int expectOthers)
        {
            await SendAsync(MsgType.EnterWorld, new EnterWorldMsg()).ConfigureAwait(false);
            var user = await ExpectIgnoringRegenAsync<UserInfoMsg>(MsgType.UserInfo).ConfigureAwait(false);
            var items = await ExpectIgnoringRegenAsync<ItemListMsg>(MsgType.ItemList).ConfigureAwait(false);
            var spawned = await ExpectIgnoringRegenAsync<PlayerSpawnedMsg>(MsgType.PlayerSpawned).ConfigureAwait(false);
            var npcs = new List<NpcInfoMsg>(expectNpcs);
            for (int i = 0; i < expectNpcs; i++)
                npcs.Add(await ExpectIgnoringRegenAsync<NpcInfoMsg>(MsgType.NpcInfo).ConfigureAwait(false));
            var others = new List<CharInfoMsg>(expectOthers);
            for (int i = 0; i < expectOthers; i++)
                others.Add(await ExpectIgnoringRegenAsync<CharInfoMsg>(MsgType.CharInfo).ConfigureAwait(false));
            var welcome = await ExpectIgnoringRegenAsync<SystemMessageMsg>(MsgType.SystemMessage).ConfigureAwait(false);
            if (welcome.Id != ChatProtocol.Welcome || welcome.Text.Length == 0 || welcome.Params.Length != 1)
                throw new InvalidOperationException($"welcome system message id={welcome.Id} text={welcome.Text}");
            return new Arrival(user, items, spawned, npcs, others);
        }

        public Task<Arrival> EnterAsync(HandoffTicket alreadyIn, HandoffTicket self) =>
            EnterAsync(alreadyIn, self, TalkingIsland.Gremlins.Length);

        public async Task<Arrival> EnterAsync(HandoffTicket alreadyIn, HandoffTicket self, int expectNpcs)
        {
            var arrival = await EnterAsync(expectNpcs, expectOthers: 1).ConfigureAwait(false);
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

        public async Task<(MsgType Type, byte[] Body)> ReadFrameAsync()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            (ushort Type, byte[] Body)? frame;
            try
            {
                frame = await FrameIO.ReadAsync(_stream, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException("timed out waiting for zone frame");
            }

            if (frame == null)
                throw new InvalidOperationException("expected zone frame, got EOF");
            return ((MsgType)frame.Value.Type, frame.Value.Body);
        }

        public async Task<T> ExpectAsync<T>(MsgType expected)
        {
            var (type, body) = await ReadFrameAsync().ConfigureAwait(false);
            if (type != expected)
            {
                string detail = "";
                if (type == MsgType.Error)
                {
                    var err = MessagePackSerializer.Deserialize<ErrorMsg>(body);
                    detail = $" {err.Code} {err.Message}";
                }

                throw new InvalidOperationException($"expected {expected}, got {type}{detail}");
            }

            return MessagePackSerializer.Deserialize<T>(body);
        }

        async Task<T> ExpectIgnoringRegenAsync<T>(MsgType expected)
        {
            while (true)
            {
                var (type, body) = await ReadFrameAsync().ConfigureAwait(false);
                if (type == MsgType.StatusUpdate)
                    continue;
                if (type != expected)
                {
                    string detail = "";
                    if (type == MsgType.Error)
                    {
                        var err = MessagePackSerializer.Deserialize<ErrorMsg>(body);
                        detail = $" {err.Code} {err.Message}";
                    }

                    throw new InvalidOperationException($"expected {expected}, got {type}{detail}");
                }

                return MessagePackSerializer.Deserialize<T>(body);
            }
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
