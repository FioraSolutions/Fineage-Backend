using Fineage.Game.Economy;
using Fineage.Game.Quests;
using Fineage.Protocol;
using Fineage.Zone.DimensionalRift;
using Fineage.Zone.NpcHtml;
using Fineage.Zone.Npcs;
using Fineage.Zone.Party;
using Fineage.Zone.Punishment;
using Fineage.Zone.Spawns;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    const int QuestDimensionalRift = 635;

    DimensionalRiftBoard RiftBoard => _dimensionalRiftActor.Board;

    public bool IsInDimensionalRiftPeace(int x, int y, int z) =>
        _dimensionalRift.Contains(x, y, z, DimensionalRiftSettings.WaitingRoomType, DimensionalRiftSettings.WaitingRoomId);

    public bool IsInDimensionalRiftZone(int x, int y, int z) =>
        _dimensionalRift.Contains(x, y, z, DimensionalRiftSettings.WaitingRoomType, DimensionalRiftSettings.RiftOuterRoomId);

    /// <summary>Mobius <c>checkIfInRiftZone(_,_,_,false)</c>: outer rift band excluding the peace waiting room.</summary>
    public bool IsInDimensionalRift(int x, int y, int z) =>
        IsInDimensionalRiftZone(x, y, z) && !IsInDimensionalRiftPeace(x, y, z);

    public bool PlayerInDimensionalRift(int objectId) => PlayerZoneChecks.IsInDimensionalRift(this, objectId);

    void TickDimensionalRift(Fineage.Zone.Simulation.TickContext tick)
    {
        foreach (var effect in _dimensionalRiftActor.Step(tick))
            ApplyDimensionalRiftEffect(effect);
    }

    void ApplyDimensionalRiftEffect(DimensionalRiftEffect effect)
    {
        if (RiftBoard.OfParty(effect.PartyId) is not { } instance)
            return;
        switch (effect.Kind)
        {
            case DimensionalRiftEffectKind.SpawnRoom:
                SpawnRiftRoom(instance);
                break;
            case DimensionalRiftEffectKind.EarthquakeWarning:
                FanRiftEarthquake(instance);
                break;
            case DimensionalRiftEffectKind.Jump:
                AutoJumpRift(instance);
                break;
            case DimensionalRiftEffectKind.EndInstance:
                EndRiftInstance(instance, questExit: true);
                break;
        }
    }

    string? ApplyDimensionalRift(ZoneSession session, ZoneEntity player, ZoneEntity npc, BypassReply reply)
    {
        return reply.ServiceArg switch
        {
            "enter" => StartDimensionalRift(session, player, npc, reply.ServiceId),
            "change" => ManualJumpRift(session, player, npc),
            "exit" => ManualExitRift(session, player, npc),
            _ => "bad_request"
        };
    }

    string? StartDimensionalRift(ZoneSession session, ZoneEntity player, ZoneEntity npc, int type)
    {
        if (_party.Of(player.ObjectId) is not { } party)
        {
            SendRiftHtml(session, player, npc, "seven_signs/rift/NoParty.htm");
            return null;
        }

        if (party.LeaderObjectId != player.ObjectId)
        {
            SendRiftHtml(session, player, npc, "seven_signs/rift/NotPartyLeader.htm");
            return null;
        }

        if (RiftBoard.PartyInRift(party.PartyId))
        {
            SendRiftHtml(session, player, npc, "seven_signs/rift/Cheater.htm");
            return null;
        }

        if (party.MemberIds.Count < _dimensionalRiftSettings.MinPartySize)
        {
            string html = LoadRiftHtml(session, "seven_signs/rift/SmallParty.htm")
                .Replace("%count%", _dimensionalRiftSettings.MinPartySize.ToString(), StringComparison.Ordinal);
            SendHtml(session, player, npc, html);
            return null;
        }

        if (!RiftBoard.IsAllowedEnter(type))
        {
            NetBridge?.FanoutSystemMessage([session.ObjectId], new SystemMessageMsg { Text = "Rift is full. Try later." });
            return null;
        }

        var members = PartyMembersOf(party);
        foreach (var member in members)
        {
            if (!IsInDimensionalRiftPeace(member.X, member.Y, member.Z))
            {
                SendRiftHtml(session, player, npc, "seven_signs/rift/NotInWaitingRoom.htm");
                return null;
            }
        }

        int cost = _dimensionalRiftSettings.EntryCost(type);
        foreach (var member in members)
        {
            if (member.Inventory == null || member.Inventory.CountOf(DimensionalRiftSettings.FragmentItemId) < cost)
            {
                string html = LoadRiftHtml(session, "seven_signs/rift/NoFragments.htm")
                    .Replace("%count%", cost.ToString(), StringComparison.Ordinal);
                SendHtml(session, player, npc, html);
                return null;
            }
        }

        return RecordBagChange(
            player,
            LedgerTxKind.DungeonEntryFee,
            LedgerAccount.Of(SystemAccount.SevenSigns),
            () =>
            {
                var changes = new List<InventoryUpdateEntryMsg>();
                foreach (var member in members)
                {
                    if (member.Session == null || member.Inventory == null)
                        return (string?)"world";
                    Take(member.Inventory, DimensionalRiftSettings.FragmentItemId, cost, changes);
                    if (changes.Count > 0 && member.Session.ObjectId == session.ObjectId)
                    {
                        NetBridge?.FanoutInventoryUpdate([session.ObjectId], new InventoryUpdateMsg { Changes = changes.ToArray() });
                        SaveInventory(session, member.Inventory);
                    }
                    else if (member.Session.ObjectId != session.ObjectId)
                        SaveInventory(member.Session, member.Inventory);
                }

                int room = PickRandomRoom(type, []);
                long now = NowUnixMs();
                var instance = _dimensionalRiftActor.StartInstance(
                    party.PartyId,
                    party.LeaderObjectId,
                    type,
                    room,
                    party.MemberIds,
                    now);
                foreach (var member in members)
                {
                    TeleportToRiftRoom(member, type, room);
                    if (member.Session != null)
                    {
                        var outcome = RecordQuest(member, () => _quests.AutoStart(member.Quests, member.Inventory!, QuestDimensionalRift));
                        if (outcome.Lines.Count > 0 || outcome.Sounds.Count > 0)
                            Publish(member.Session, member, outcome, npc.ObjectId, npc.NpcId);
                    }
                }

                instance.SpawnAtUnixMs = now + _dimensionalRiftSettings.SpawnDelayMs;
                return (string?)null;
            },
            _ => type);
    }

    string? ManualJumpRift(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        if (_party.Of(player.ObjectId) is not { } party || !RiftBoard.PartyInRift(party.PartyId))
        {
            SendRiftHtml(session, player, npc, "seven_signs/rift/Cheater.htm");
            return null;
        }

        if (party.LeaderObjectId != player.ObjectId)
        {
            SendRiftHtml(session, player, npc, "seven_signs/rift/NotPartyLeader.htm");
            return null;
        }

        if (RiftBoard.OfParty(party.PartyId) is not { } instance)
            return null;
        if (instance.CurrentJump >= instance.MaxJumps)
        {
            SendRiftHtml(session, player, npc, "seven_signs/rift/AlreadyTeleported.htm");
            return null;
        }

        instance.CurrentJump++;
        ManualJumpToNextRoom(instance);
        return null;
    }

    string? ManualExitRift(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        if (_party.Of(player.ObjectId) is not { } party || !RiftBoard.PartyInRift(party.PartyId))
        {
            SendRiftHtml(session, player, npc, "seven_signs/rift/Cheater.htm");
            return null;
        }

        if (party.LeaderObjectId != player.ObjectId)
        {
            SendRiftHtml(session, player, npc, "seven_signs/rift/NotPartyLeader.htm");
            return null;
        }

        if (RiftBoard.OfParty(party.PartyId) is { } instance)
            EndRiftInstance(instance, questExit: true);
        return null;
    }

    void AutoJumpRift(DimensionalRiftInstance instance)
    {
        int alive = instance.MemberSnapshot.Count(id =>
            _byId.TryGetValue(id, out var e) && e.Session != null && !instance.RevivedInWaitingRoom.Contains(id));
        if (instance.CurrentJump >= instance.MaxJumps || alive <= instance.DeadMemberIds.Count)
        {
            EndRiftInstance(instance, questExit: true);
            return;
        }

        instance.CurrentJump++;
        JumpToNextRoom(instance);
    }

    void ManualJumpToNextRoom(DimensionalRiftInstance instance)
    {
        UnspawnRiftRoom(instance);
        JumpToNextRoom(instance);
        _dimensionalRiftActor.ScheduleJump(instance, NowUnixMs(), manual: true);
        instance.Spawned = false;
        instance.SpawnAtUnixMs = NowUnixMs() + _dimensionalRiftSettings.SpawnDelayMs;
    }

    void JumpToNextRoom(DimensionalRiftInstance instance)
    {
        UnspawnRiftRoom(instance);
        int room = PickRandomRoom(instance.Type, instance.CompletedRooms);
        instance.CompletedRooms.Add(instance.CurrentRoom);
        RiftBoard.Release(instance.Type, instance.CurrentRoom);
        instance.CurrentRoom = room;
        instance.BossRoom = _dimensionalRift.Room(instance.Type, room).Boss;
        RiftBoard.Occupy(instance.Type, room, instance.PartyId);
        foreach (int id in instance.MemberSnapshot)
        {
            if (_byId.TryGetValue(id, out var member) && member.Session != null && !instance.RevivedInWaitingRoom.Contains(id))
                TeleportToRiftRoom(member, instance.Type, room);
        }

        instance.Spawned = false;
        instance.SpawnAtUnixMs = NowUnixMs() + _dimensionalRiftSettings.SpawnDelayMs;
        _dimensionalRiftActor.ScheduleJump(instance, NowUnixMs(), manual: false);
    }

    int PickRandomRoom(int type, HashSet<int> exclude)
    {
        var empty = RiftBoard.FreeRooms(type).Where(r => !exclude.Contains(r)).ToList();
        if (empty.Count == 0)
            empty = RiftBoard.FreeRooms(type);
        return empty[_riftRandom.Next(empty.Count)];
    }

    void TeleportToRiftRoom(ZoneEntity player, int type, int room)
    {
        var site = _dimensionalRift.Room(type, room);
        Relocate(player, site.Teleport.X, site.Teleport.Y, site.Teleport.Z);
        PlayerZoneChecks.RegisterRiftParticipation(this, player.ObjectId, inside: true);
    }

    void TeleportToWaitingRoom(ZoneEntity player)
    {
        var peace = _dimensionalRift.Room(
            DimensionalRiftSettings.WaitingRoomType,
            DimensionalRiftSettings.WaitingRoomId);
        Relocate(player, peace.Teleport.X, peace.Teleport.Y, peace.Teleport.Z);
        PlayerZoneChecks.RegisterRiftParticipation(this, player.ObjectId, inside: false);
        MaybeExitRiftQuest(player);
    }

    void MaybeExitRiftQuest(ZoneEntity player)
    {
        if (player.Quests.Find(QuestDimensionalRift) is not { } row || row.Phase != QuestPhase.Started)
            return;
        if (row.Cond != 1)
            return;
        if (player.Session == null || player.Inventory == null)
            return;
        var outcome = RecordQuest(player, () => _quests.Abort(player.Quests, player.Inventory, QuestDimensionalRift));
        if (outcome.Ok)
            Publish(player.Session, player, outcome, 0, 0);
    }

    void EndRiftInstance(DimensionalRiftInstance instance, bool questExit)
    {
        UnspawnRiftRoom(instance);
        foreach (int id in instance.MemberSnapshot)
        {
            if (_byId.TryGetValue(id, out var member) && member.Session != null && !instance.RevivedInWaitingRoom.Contains(id))
            {
                TeleportToWaitingRoom(member);
                if (questExit)
                    MaybeExitRiftQuest(member);
            }
        }

        _dimensionalRiftActor.KillInstance(instance.PartyId);
    }

    void SpawnRiftRoom(DimensionalRiftInstance instance)
    {
        if (instance.Spawned)
            return;
        instance.Spawned = true;
        var templates = NpcCatalog.LoadDefault();
        var room = _dimensionalRift.Room(instance.Type, instance.CurrentRoom);
        foreach (var row in _dimensionalRift.SpawnsInRoom(instance.Type, instance.CurrentRoom))
        {
            if (!templates.ById.TryGetValue(row.MobId, out var template))
                continue;
            for (int i = 0; i < row.Count; i++)
            {
                (int x, int y) = _dimensionalRift.RandomXY(room, _riftRandom);
                int z = room.Teleport.Z;
                if (_geo.HasGeo(x, y))
                    z = _geo.GetHeight(x, y, z);
                var def = SpawnCatalog.FromTemplate(
                    AllocateQuestNpcObjectId(),
                    template,
                    x,
                    y,
                    z,
                    -1,
                    respawnDelaySeconds: row.RespawnDelaySeconds);
                MaterializeNpc(MergeSpawnCombatStats(def), mapId: null, template.Hp, template.Mp);
                if (_byId.TryGetValue(def.ObjectId, out var npc))
                {
                    npc.RiftPartyId = instance.PartyId;
                    instance.SpawnedNpcObjectIds.Add(npc.ObjectId);
                    FanoutNewNpc(npc);
                }
            }
        }
    }

    void UnspawnRiftRoom(DimensionalRiftInstance instance)
    {
        foreach (int objectId in instance.SpawnedNpcObjectIds.ToArray())
        {
            if (_byId.TryGetValue(objectId, out var npc))
                DespawnNpc(npc);
        }

        instance.SpawnedNpcObjectIds.Clear();
        instance.Spawned = false;
    }

    void FanRiftEarthquake(DimensionalRiftInstance instance)
    {
        // Mobius Earthquake packet — fan-out when the client protocol exposes it.
        _ = instance;
    }

    void SendRiftHtml(ZoneSession session, ZoneEntity player, ZoneEntity npc, string path)
    {
        string html = LoadRiftHtml(session, path).Replace("%npc_name%", npc.Name, StringComparison.Ordinal);
        SendHtml(session, player, npc, html);
    }

    string LoadRiftHtml(ZoneSession session, string path) =>
        TryPage(session.Language, path, out var html) ? html : "<html><body>%npcname%</body></html>";

    List<ZoneEntity> PartyMembersOf(ZoneParty party)
    {
        var list = new List<ZoneEntity>(party.MemberIds.Count);
        foreach (int id in party.MemberIds)
        {
            if (_byId.TryGetValue(id, out var entity) && entity.Session != null)
                list.Add(entity);
        }

        return list;
    }

    void RiftOnPartyLeave(int objectId)
    {
        if (_party.Of(objectId) is not { } party || !RiftBoard.PartyInRift(party.PartyId))
            return;
        if (RiftBoard.OfParty(party.PartyId) is not { } instance)
            return;
        instance.DeadMemberIds.Remove(objectId);
        instance.RevivedInWaitingRoom.Remove(objectId);
        int active = party.MemberIds.Count;
        if (active < _dimensionalRiftSettings.MinPartySize || active <= 1)
            EndRiftInstance(instance, questExit: true);
    }

    void RiftOnPlayerDeath(ZoneEntity player)
    {
        if (_party.Of(player.ObjectId) is not { } party || !RiftBoard.PartyInRift(party.PartyId))
            return;
        if (RiftBoard.OfParty(party.PartyId) is not { } instance)
            return;
        instance.DeadMemberIds.Add(player.ObjectId);
    }

    void RiftOnPlayerRevive(ZoneEntity player, bool usedTeleport)
    {
        if (_party.Of(player.ObjectId) is not { } party || !RiftBoard.PartyInRift(party.PartyId))
            return;
        if (RiftBoard.OfParty(party.PartyId) is not { } instance)
            return;
        instance.DeadMemberIds.Remove(player.ObjectId);
        if (usedTeleport)
        {
            instance.RevivedInWaitingRoom.Add(player.ObjectId);
            instance.DeadMemberIds.Add(player.ObjectId);
            TeleportToWaitingRoom(player);
            int stillInside = instance.MemberSnapshot.Count(id =>
                _byId.TryGetValue(id, out var e) && e.Session != null && !instance.RevivedInWaitingRoom.Contains(id));
            if (stillInside < _dimensionalRiftSettings.MinPartySize)
                EndRiftInstance(instance, questExit: true);
        }
    }

    void RiftSanitizeLogin(ZoneEntity player)
    {
        if (!IsInDimensionalRift(player.X, player.Y, player.Z))
            return;
        TeleportToWaitingRoom(player);
    }
}