using System;
using System.IO;

namespace Fineage.Net.Contract;

public static partial class NetCodec
{
    public static byte[] EncodeAugmentConfirmTarget(AugmentConfirmTargetNetMsg m) =>
        Write(w => w.WriteInt32(m.TargetObjectId));

    public static AugmentConfirmTargetNetMsg DecodeAugmentConfirmTarget(ReadOnlySpan<byte> p) =>
        new() { TargetObjectId = new Reader(p).ReadInt32() };

    public static byte[] EncodeAugmentConfirmRefiner(AugmentConfirmRefinerNetMsg m) => Write(w =>
    {
        w.WriteInt32(m.TargetObjectId);
        w.WriteInt32(m.RefinerObjectId);
    });

    public static AugmentConfirmRefinerNetMsg DecodeAugmentConfirmRefiner(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        return new AugmentConfirmRefinerNetMsg
        {
            TargetObjectId = r.ReadInt32(),
            RefinerObjectId = r.ReadInt32()
        };
    }

    public static byte[] EncodeAugmentConfirmGem(AugmentConfirmGemNetMsg m) => Write(w =>
    {
        w.WriteInt32(m.TargetObjectId);
        w.WriteInt32(m.RefinerObjectId);
        w.WriteInt32(m.GemstoneObjectId);
        w.WriteInt64(m.GemstoneCount);
    });

    public static AugmentConfirmGemNetMsg DecodeAugmentConfirmGem(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        return new AugmentConfirmGemNetMsg
        {
            TargetObjectId = r.ReadInt32(),
            RefinerObjectId = r.ReadInt32(),
            GemstoneObjectId = r.ReadInt32(),
            GemstoneCount = r.ReadInt64()
        };
    }

    public static byte[] EncodeAugmentRefine(AugmentRefineNetMsg m) => Write(w =>
    {
        w.WriteInt32(m.TargetObjectId);
        w.WriteInt32(m.RefinerObjectId);
        w.WriteInt32(m.GemstoneObjectId);
        w.WriteInt64(m.GemstoneCount);
    });

    public static AugmentRefineNetMsg DecodeAugmentRefine(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        return new AugmentRefineNetMsg
        {
            TargetObjectId = r.ReadInt32(),
            RefinerObjectId = r.ReadInt32(),
            GemstoneObjectId = r.ReadInt32(),
            GemstoneCount = r.ReadInt64()
        };
    }

    public static byte[] EncodeAugmentRefineCancel(AugmentRefineCancelNetMsg m) =>
        Write(w => w.WriteInt32(m.TargetObjectId));

    public static AugmentRefineCancelNetMsg DecodeAugmentRefineCancel(ReadOnlySpan<byte> p) =>
        new() { TargetObjectId = new Reader(p).ReadInt32() };

    public static byte[] EncodeVariationResult(VariationResultNetMsg m) => Write(w =>
    {
        w.WriteInt32(m.Option1);
        w.WriteInt32(m.Option2);
        w.WriteInt32(m.Success);
    });

    public static VariationResultNetMsg DecodeVariationResult(ReadOnlySpan<byte> p)
    {
        var r = new Reader(p);
        return new VariationResultNetMsg
        {
            Option1 = r.ReadInt32(),
            Option2 = r.ReadInt32(),
            Success = r.ReadInt32()
        };
    }

    public static byte[] EncodeVariationPutItem(VariationPutItemNetMsg m) => Write(w =>
    {
        w.WriteInt32(m.TargetObjectId);
        w.WriteInt32(m.ItemId);
        w.WriteInt32(m.Result);
    });

    public static byte[] EncodeVariationPutRefiner(VariationPutRefinerNetMsg m) => Write(w =>
    {
        w.WriteInt32(m.RefinerObjectId);
        w.WriteInt32(m.ItemId);
        w.WriteInt32(m.RefinerId);
        w.WriteInt32(m.Result);
    });

    public static byte[] EncodeVariationPutGem(VariationPutGemNetMsg m) => Write(w =>
    {
        w.WriteInt32(m.GemstoneObjectId);
        w.WriteInt32(m.RefinerItemId);
        w.WriteInt64(m.GemstoneCount);
        w.WriteInt32(m.Result);
    });

    public static byte[] EncodeVariationCancelItem(VariationCancelItemNetMsg m) => Write(w =>
    {
        w.WriteInt32(m.TargetObjectId);
        w.WriteInt32(m.ItemId);
        w.WriteInt32(m.Result);
    });

    public static byte[] EncodeVariationCancelResult(VariationCancelResultNetMsg m) =>
        Write(w => w.WriteInt32(m.RefundAdena));
}