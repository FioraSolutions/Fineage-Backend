using Fineage.Net.Contract;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class NetWorldEventPublisher
{
    void SendAll(IReadOnlyList<int> watchers, ushort opcode, byte[] payload)
    {
        foreach (int watcher in watchers)
            _presence.TrySend(watcher, opcode, payload);
    }

    void IWorldEventPublisher.FanoutCrestData(IReadOnlyList<int> watcherObjectIds, CrestDataNetMsg crest) =>
        SendAll(watcherObjectIds, NetOpcode.CrestData, NetCodec.EncodeCrestData(crest));

    void IWorldEventPublisher.FanoutPledgeWarList(IReadOnlyList<int> watcherObjectIds, PledgeWarListNetMsg wars) =>
        SendAll(watcherObjectIds, NetOpcode.PledgeWarList, NetCodec.EncodePledgeWarList(wars));

    void IWorldEventPublisher.FanoutRelationChanged(IReadOnlyList<int> watcherObjectIds, RelationChangedNetMsg relation) =>
        SendAll(watcherObjectIds, NetOpcode.RelationChanged, NetCodec.EncodeRelationChanged(relation));

    void IWorldEventPublisher.FanoutPledgeInfo(IReadOnlyList<int> watcherObjectIds, PledgeInfoNetMsg info) =>
        SendAll(watcherObjectIds, NetOpcode.PledgeInfo, NetCodec.EncodePledgeInfo(info));

    void IWorldEventPublisher.FanoutAllyInfo(IReadOnlyList<int> watcherObjectIds, AllyInfoNetMsg info) =>
        SendAll(watcherObjectIds, NetOpcode.AllyInfo, NetCodec.EncodeAllyInfo(info));

    void IWorldEventPublisher.FanoutAllyAskJoin(IReadOnlyList<int> watcherObjectIds, AllyAskJoinNetMsg ask) =>
        SendAll(watcherObjectIds, NetOpcode.AllyAskJoin, NetCodec.EncodeAllyAskJoin(ask));

    void IWorldEventPublisher.FanoutClanHallList(IReadOnlyList<int> watcherObjectIds, ClanHallListNetMsg halls) =>
        SendAll(watcherObjectIds, NetOpcode.ClanHallList, NetCodec.EncodeClanHallList(halls));

    void IWorldEventPublisher.FanoutAgitDecoInfo(IReadOnlyList<int> watcherObjectIds, AgitDecoInfoNetMsg deco) =>
        SendAll(watcherObjectIds, NetOpcode.AgitDecoInfo, NetCodec.EncodeAgitDecoInfo(deco));

    void IWorldEventPublisher.FanoutDoorStatus(IReadOnlyList<int> watcherObjectIds, DoorStatusNetMsg door) =>
        SendAll(watcherObjectIds, NetOpcode.DoorStatus, NetCodec.EncodeDoorStatus(door));

    void IWorldEventPublisher.FanoutCastleInfo(IReadOnlyList<int> watcherObjectIds, CastleInfoNetMsg castle) =>
        SendAll(watcherObjectIds, NetOpcode.CastleInfo, NetCodec.EncodeCastleInfo(castle));

    void IWorldEventPublisher.FanoutManorInfo(IReadOnlyList<int> watcherObjectIds, ManorInfoNetMsg manor) =>
        SendAll(watcherObjectIds, NetOpcode.ManorInfo, NetCodec.EncodeManorInfo(manor));

    void IWorldEventPublisher.FanoutFishing(IReadOnlyList<int> watcherObjectIds, FishingEventNetMsg fishing) =>
        SendAll(watcherObjectIds, NetOpcode.FishingEvent, NetCodec.EncodeFishingEvent(fishing));

    void IWorldEventPublisher.FanoutSummonInfo(IReadOnlyList<int> watcherObjectIds, SummonInfoNetMsg info) =>
        SendAll(watcherObjectIds, NetOpcode.SummonInfo, NetCodec.EncodeSummonInfo(info));

    void IWorldEventPublisher.FanoutSummonGone(IReadOnlyList<int> watcherObjectIds, SummonGoneNetMsg gone) =>
        SendAll(watcherObjectIds, NetOpcode.SummonGone, NetCodec.EncodeSummonGone(gone));

    void IWorldEventPublisher.FanoutPetInventory(IReadOnlyList<int> watcherObjectIds, PetInventoryNetMsg inventory) =>
        SendAll(watcherObjectIds, NetOpcode.PetInventory, NetCodec.EncodePetInventory(inventory));

    void IWorldEventPublisher.FanoutRide(IReadOnlyList<int> watcherObjectIds, RideNetMsg ride) =>
        SendAll(watcherObjectIds, NetOpcode.Ride, NetCodec.EncodeRide(ride));

    void IWorldEventPublisher.FanoutPlayerActionState(IReadOnlyList<int> watcherObjectIds, PlayerActionStateNetMsg state) =>
        SendAll(watcherObjectIds, NetOpcode.PlayerActionState, NetCodec.EncodePlayerActionState(state));

    void IWorldEventPublisher.FanoutDuelEvent(IReadOnlyList<int> watcherObjectIds, DuelEventNetMsg duel) =>
        SendAll(watcherObjectIds, NetOpcode.DuelEvent, NetCodec.EncodeDuelEvent(duel));

    void IWorldEventPublisher.FanoutDuelUserInfo(IReadOnlyList<int> watcherObjectIds, DuelUserInfoNetMsg info) =>
        SendAll(watcherObjectIds, NetOpcode.DuelUserInfo, NetCodec.EncodeDuelUserInfo(info));
}
