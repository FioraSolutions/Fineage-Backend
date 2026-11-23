using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class NetWorldEventPublisher
{
    void IWorldEventPublisher.FanoutAugmentShowMakeWindow(IReadOnlyList<int> watcherObjectIds) =>
        SendEmpty(watcherObjectIds, NetOpcode.AugmentShowMakeWindow);

    void IWorldEventPublisher.FanoutAugmentShowCancelWindow(IReadOnlyList<int> watcherObjectIds) =>
        SendEmpty(watcherObjectIds, NetOpcode.AugmentShowCancelWindow);

    void IWorldEventPublisher.FanoutAugmentPutItemResult(IReadOnlyList<int> watcherObjectIds, VariationPutItemMsg result) =>
        Send(watcherObjectIds, NetOpcode.AugmentPutItemResult, NetCodec.EncodeVariationPutItem(ToPutItem(result)));

    void IWorldEventPublisher.FanoutAugmentPutRefinerResult(IReadOnlyList<int> watcherObjectIds, VariationPutRefinerMsg result) =>
        Send(watcherObjectIds, NetOpcode.AugmentPutRefinerResult, NetCodec.EncodeVariationPutRefiner(ToPutRefiner(result)));

    void IWorldEventPublisher.FanoutAugmentPutGemResult(IReadOnlyList<int> watcherObjectIds, VariationPutGemMsg result) =>
        Send(watcherObjectIds, NetOpcode.AugmentPutGemResult, NetCodec.EncodeVariationPutGem(ToPutGem(result)));

    void IWorldEventPublisher.FanoutAugmentVariationResult(IReadOnlyList<int> watcherObjectIds, VariationResultMsg result) =>
        Send(watcherObjectIds, NetOpcode.AugmentVariationResult, NetCodec.EncodeVariationResult(new VariationResultNetMsg
        {
            Option1 = result.Option1,
            Option2 = result.Option2,
            Success = result.Success
        }));

    void IWorldEventPublisher.FanoutAugmentPutCancelItemResult(IReadOnlyList<int> watcherObjectIds, VariationCancelItemMsg result) =>
        Send(watcherObjectIds, NetOpcode.AugmentPutCancelItemResult, NetCodec.EncodeVariationCancelItem(new VariationCancelItemNetMsg
        {
            TargetObjectId = result.TargetObjectId,
            ItemId = result.ItemId,
            Result = result.Result
        }));

    void IWorldEventPublisher.FanoutAugmentCancelResult(IReadOnlyList<int> watcherObjectIds, VariationCancelResultMsg result) =>
        Send(watcherObjectIds, NetOpcode.AugmentCancelResult, NetCodec.EncodeVariationCancelResult(new VariationCancelResultNetMsg
        {
            RefundAdena = result.RefundAdena
        }));

    static VariationPutItemNetMsg ToPutItem(VariationPutItemMsg m) => new()
    {
        TargetObjectId = m.TargetObjectId,
        ItemId = m.ItemId,
        Result = m.Result
    };

    static VariationPutRefinerNetMsg ToPutRefiner(VariationPutRefinerMsg m) => new()
    {
        RefinerObjectId = m.RefinerObjectId,
        ItemId = m.ItemId,
        RefinerId = m.RefinerId,
        Result = m.Result
    };

    static VariationPutGemNetMsg ToPutGem(VariationPutGemMsg m) => new()
    {
        GemstoneObjectId = m.GemstoneObjectId,
        RefinerItemId = m.RefinerItemId,
        GemstoneCount = m.GemstoneCount,
        Result = m.Result
    };

    void Send(IReadOnlyList<int> watchers, ushort opcode, byte[] payload)
    {
                    _presence.TryBroadcastCritical(watchers, opcode, payload);
    }

    void SendEmpty(IReadOnlyList<int> watchers, ushort opcode)
    {
        _presence.TryBroadcastCritical(watchers, opcode, Array.Empty<byte>());
    }
}