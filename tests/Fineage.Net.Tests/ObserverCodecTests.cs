using Fineage.Net.Contract;
using Xunit;

namespace Fineage.Net.Tests;

/// <summary>
/// Wire shapes added for "everyone sees the same": Attack hit-list tail, MagicSkillLaunched, MagicSkillCanceled,
/// PlaySound. Old frames (no tail) must still decode.
/// </summary>
public sealed class ObserverCodecTests
{
    static AttackStateMsg Swing() => new()
    {
        AttackerValue = 7, AttackerGeneration = 1, TargetValue = 9, TargetGeneration = 1,
        Damage = 120, Flags = 0x20, AttackerX = 1, AttackerY = 2, AttackerZ = 3, TargetX = 4, TargetY = 5, TargetZ = 6
    };

    [Fact]
    public void Single_hit_attack_has_no_tail_and_decodes_with_no_extra_hits()
    {
        var bytes = NetCodec.EncodeAttackState(Swing());
        Assert.Equal(8 + 4 + 8 + 4 + 4 + 1 + 6 * 4, bytes.Length); // legacy size: old clients see the same bytes
        var back = NetCodec.DecodeAttackState(bytes);
        Assert.Equal(120, back.Damage);
        Assert.Equal(0x20, back.Flags);
        Assert.Empty(back.ExtraHits);
    }

    [Fact]
    public void Dual_attack_carries_the_second_hit_in_the_tail()
    {
        var swing = Swing();
        swing.ExtraHits = [new AttackHitNet { Damage = 0, Flags = 0x80 }];
        var back = NetCodec.DecodeAttackState(NetCodec.EncodeAttackState(swing));
        var second = Assert.Single(back.ExtraHits);
        Assert.Equal(0, second.Damage);
        Assert.Equal(0x80, second.Flags);
        Assert.Equal(120, back.Damage);
    }

    [Fact]
    public void Launched_canceled_and_play_sound_roundtrip()
    {
        var launched = NetCodec.DecodeMagicSkillLaunched(NetCodec.EncodeMagicSkillLaunched(new MagicSkillLaunchedNetMsg
        {
            CasterObjectId = 11, SkillId = 1177, SkillLevel = 3, TargetObjectIds = [21, 22]
        }));
        Assert.Equal(11, launched.CasterObjectId);
        Assert.Equal(1177, launched.SkillId);
        Assert.Equal(3, launched.SkillLevel);
        Assert.Equal(new[] { 21, 22 }, launched.TargetObjectIds);

        Assert.Equal(11, NetCodec.DecodeMagicSkillCanceled(NetCodec.EncodeMagicSkillCanceled(new MagicSkillCanceledNetMsg { ObjectId = 11 })).ObjectId);

        var sound = NetCodec.DecodePlaySound(NetCodec.EncodePlaySound(new PlaySoundNetMsg
        {
            Type = 0, File = "ItemSound.quest_accept", ObjectId = 0, X = 1, Y = 2, Z = 3, DelayMs = 0
        }));
        Assert.Equal(0, sound.Type);
        Assert.Equal("ItemSound.quest_accept", sound.File);
        Assert.Equal((1, 2, 3), (sound.X, sound.Y, sound.Z));
    }

    [Fact]
    public void New_opcodes_do_not_collide()
    {
        var all = typeof(NetOpcode).GetFields()
            .Where(f => f.IsLiteral && f.FieldType == typeof(ushort))
            .Select(f => (ushort)f.GetRawConstantValue()!)
            .ToList();
        foreach (ushort op in new[] { NetOpcode.MagicSkillLaunched, NetOpcode.MagicSkillCanceled, NetOpcode.PlaySound })
            Assert.Equal(1, all.Count(v => v == op));
    }
}
