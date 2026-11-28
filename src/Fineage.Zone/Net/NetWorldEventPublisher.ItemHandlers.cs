using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class NetWorldEventPublisher
{
    void IWorldEventPublisher.FanoutDice(IReadOnlyList<int> watcherObjectIds, DiceMsg dice)
    {
        var payload = NetCodec.EncodeDice(new DiceNetMsg
        {
            ObjectId = dice.ObjectId,
            ItemId = dice.ItemId,
            Number = dice.Number,
            X = dice.X,
            Y = dice.Y,
            Z = dice.Z
        });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.Dice, payload);
    }

    void IWorldEventPublisher.FanoutShowMiniMap(IReadOnlyList<int> watcherObjectIds, ShowMiniMapMsg map)
    {
        var payload = NetCodec.EncodeShowMiniMap(new ShowMiniMapNetMsg
        {
            MapId = map.MapId,
            SevenSignsPeriod = map.SevenSignsPeriod
        });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.ShowMiniMap, payload);
    }

    void IWorldEventPublisher.FanoutShowCalculator(IReadOnlyList<int> watcherObjectIds, ShowCalculatorMsg calc)
    {
        var payload = NetCodec.EncodeShowCalculator(new ShowCalculatorNetMsg { ItemId = calc.ItemId });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.ShowCalculator, payload);
    }

    void IWorldEventPublisher.FanoutShowXMasSeal(IReadOnlyList<int> watcherObjectIds, ShowXMasSealMsg seal)
    {
        var payload = NetCodec.EncodeShowXMasSeal(new ShowXMasSealNetMsg { ItemId = seal.ItemId });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.ShowXMasSeal, payload);
    }
}
