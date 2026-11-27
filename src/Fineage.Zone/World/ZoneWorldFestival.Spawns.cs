using Fineage.Zone.Npcs;
using Fineage.Zone.SevenSigns;
using Fineage.Zone.Spawns;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    static bool IsFestivalArcher(int npcId) => npcId is
        18011 or 18016 or 18021 or 18026 or 18031 or 18036 or 18041 or 18046 or 18051 or 18056 or 18061;

    void ApplyFestivalEffect(SevenSignsFestivalEffect effect)
    {
        if (effect.Kind == SevenSignsFestivalEffectKind.LaunchRegistration)
        {
            LaunchFestivalRegistration(effect.PartyId);
            return;
        }

        if (FestivalBoard.OfParty(effect.PartyId) is not { } instance)
            return;
        switch (effect.Kind)
        {
            case SevenSignsFestivalEffectKind.SpawnWitch:
                SpawnFestivalWitch(instance);
                break;
            case SevenSignsFestivalEffectKind.SpawnPrimary:
                SpawnFestivalWave(instance, FestivalMobWave.Primary, skipArchers: false, offeringBonus: 1);
                instance.PrimarySpawned = true;
                break;
            case SevenSignsFestivalEffectKind.SpawnSecondary:
                SpawnFestivalWave(instance, FestivalMobWave.Secondary, skipArchers: false, offeringBonus: 1);
                instance.SecondarySpawned = true;
                break;
            case SevenSignsFestivalEffectKind.SpawnChests:
                SpawnFestivalWave(instance, FestivalMobWave.Chest, skipArchers: false, offeringBonus: 5);
                instance.ChestSpawned = true;
                break;
            case SevenSignsFestivalEffectKind.SpawnChallenge:
                SpawnFestivalWave(instance, FestivalMobWave.Primary, skipArchers: true, offeringBonus: 2);
                break;
            case SevenSignsFestivalEffectKind.EndInstance:
                EndFestivalInstance(effect.PartyId, recordScore: true);
                break;
        }
    }

    void SpawnFestivalWitch(SevenSignsFestivalInstance instance)
    {
        if (instance.WitchSpawned)
            return;
        var tier = _festivalCatalog.Tier(instance.TierId);
        var side = _festivalCatalog.Side(tier, instance.Cabal);
        var template = NpcCatalog.LoadDefault().Get(side.WitchNpcId);
        int objectId = AllocateQuestNpcObjectId();
        var def = SpawnCatalog.FromTemplate(objectId, template, side.WitchX, side.WitchY, side.WitchZ, 0, attackable: false);
        MaterializeSpawn(MergeSpawnCombatStats(def), mapId: _activeMapId);
        if (_byId.TryGetValue(objectId, out var npc))
        {
            npc.InstanceId = instance.InstanceId;
            npc.FestivalPartyId = instance.PartyId;
            npc.HomeSet = false;
            instance.WitchSpawned = true;
            instance.WitchObjectId = objectId;
            FanoutNewNpc(npc);
        }
    }

    void SpawnFestivalWave(
        SevenSignsFestivalInstance instance,
        FestivalMobWave wave,
        bool skipArchers,
        int offeringBonus)
    {
        var templates = NpcCatalog.LoadDefault();
        foreach (var row in _festivalSpawns.Spawns(instance.Cabal, instance.TierId, wave))
        {
            if (skipArchers && IsFestivalArcher(row.NpcId))
                continue;
            if (!templates.ById.TryGetValue(row.NpcId, out var template))
                continue;
            int objectId = AllocateQuestNpcObjectId();
            var def = SpawnCatalog.FromTemplate(
                objectId,
                template,
                row.X,
                row.Y,
                row.Z,
                -1,
                respawnDelaySeconds: _festivalCatalog.DefaultRespawnSeconds);
            MaterializeNpc(MergeSpawnCombatStats(def), mapId: null, template.Hp, template.Mp);
            if (!_byId.TryGetValue(objectId, out var npc))
                continue;
            npc.InstanceId = instance.InstanceId;
            npc.FestivalPartyId = instance.PartyId;
            npc.FestivalOfferingBonus = Math.Max(1, offeringBonus);
            instance.SpawnedNpcObjectIds.Add(objectId);
            FanoutNewNpc(npc);
        }
    }

    void UnspawnFestivalInstance(SevenSignsFestivalInstance instance)
    {
        foreach (int objectId in instance.SpawnedNpcObjectIds.ToArray())
        {
            if (_byId.TryGetValue(objectId, out var npc))
                DespawnNpc(npc);
        }
        instance.SpawnedNpcObjectIds.Clear();
        if (instance.WitchObjectId is int witchId && _byId.TryGetValue(witchId, out var witch))
            DespawnNpc(witch);
        instance.WitchObjectId = null;
    }

    internal bool TryIncreaseFestivalChallenge(ZoneEntity player, int tierId, SevenSignsCabal cabal)
    {
        if (_party.Of(player.ObjectId) is not { } party || party.LeaderObjectId != player.ObjectId)
            return false;
        if (FestivalBoard.OfParty(party.PartyId) is not { } instance)
            return false;
        if (instance.TierId != tierId || instance.Cabal != cabal || instance.ChallengeIncreased)
            return false;
        instance.ChallengeIncreased = true;
        ApplyFestivalEffect(new SevenSignsFestivalEffect(party.PartyId, SevenSignsFestivalEffectKind.SpawnChallenge));
        return true;
    }
}