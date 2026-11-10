using Fineage.Net.Contract;
using Xunit;

namespace Fineage.Net.Tests;

/// <summary>
/// Mandatory encode/decode round-trips for combat/status messages (#353 NetCodec track).
/// </summary>
public sealed class CombatCodecRoundTripTests
{
    [Fact]
    public void AttackState_and_AttackStance_roundtrip()
    {
        var atk = NetCodec.DecodeAttackState(NetCodec.EncodeAttackState(new AttackStateMsg
        {
            AttackerValue = 11,
            AttackerGeneration = 2,
            TargetValue = 22,
            TargetGeneration = 3,
            Damage = 150,
            Flags = 0x04,
            AttackerX = 1,
            AttackerY = 2,
            AttackerZ = 3,
            TargetX = 4,
            TargetY = 5,
            TargetZ = 6
        }));
        Assert.Equal(11uL, atk.AttackerValue);
        Assert.Equal(22uL, atk.TargetValue);
        Assert.Equal(150, atk.Damage);
        Assert.Equal(0x04, atk.Flags);
        Assert.Equal(6, atk.TargetZ);

        var stance = NetCodec.DecodeAttackStance(NetCodec.EncodeAttackStance(new AttackStanceNetMsg
        {
            EntityValue = 9,
            EntityGeneration = 1
        }));
        Assert.Equal(9uL, stance.EntityValue);
        Assert.Equal(1u, stance.EntityGeneration);
    }

    [Fact]
    public void Target_and_StatusUpdate_roundtrip()
    {
        var selected = NetCodec.DecodeTargetSelected(NetCodec.EncodeTargetSelected(new TargetSelectedNetMsg
        {
            TargetObjectId = 77,
            ColorHint = 2
        }));
        Assert.Equal(77, selected.TargetObjectId);
        Assert.Equal(2, selected.ColorHint);

        var unselected = NetCodec.DecodeTargetUnselected(NetCodec.EncodeTargetUnselected(new TargetUnselectedNetMsg
        {
            TargetObjectId = 88
        }));
        Assert.Equal(88, unselected.TargetObjectId);

        var status = NetCodec.DecodeStatusUpdate(NetCodec.EncodeStatusUpdate(new StatusUpdateNetMsg
        {
            EntityValue = 5,
            EntityGeneration = 4,
            AttributeIds = [9, 10],
            Values = [100, 200]
        }));
        Assert.Equal(5uL, status.EntityValue);
        Assert.Equal(new[] { 9, 10 }, status.AttributeIds);
        Assert.Equal(new[] { 100, 200 }, status.Values);
    }

    [Fact]
    public void Die_Revive_and_MagicSkillUse_roundtrip()
    {
        var die = NetCodec.DecodeEntityDie(NetCodec.EncodeEntityDie(new EntityDieMsg
        {
            EntityValue = 3,
            EntityGeneration = 1,
            KillerObjectId = 99
        }));
        Assert.Equal(3uL, die.EntityValue);
        Assert.Equal(99, die.KillerObjectId);

        var revive = NetCodec.DecodeRevive(NetCodec.EncodeRevive(new ReviveNetMsg { ObjectId = 3 }));
        Assert.Equal(3, revive.ObjectId);

        var skill = NetCodec.DecodeMagicSkillUse(NetCodec.EncodeMagicSkillUse(new MagicSkillUseNetMsg
        {
            CasterObjectId = 1,
            TargetObjectId = 2,
            SkillId = 1147,
            SkillLevel = 3,
            HitTimeMs = 1200,
            ReuseDelayMs = 4000
        }));
        Assert.Equal(1147, skill.SkillId);
        Assert.Equal(3, skill.SkillLevel);
        Assert.Equal(1200, skill.HitTimeMs);
        Assert.Equal(4000, skill.ReuseDelayMs);
    }

    [Fact]
    public void WaitType_MoveType_and_EtcStatus_roundtrip()
    {
        var wait = NetCodec.DecodeChangeWaitType(NetCodec.EncodeChangeWaitType(new ChangeWaitTypeNetMsg
        {
            EntityValue = 8,
            EntityGeneration = 2,
            WaitType = 0,
            X = 10,
            Y = 20,
            Z = 30
        }));
        Assert.Equal(0, wait.WaitType);
        Assert.Equal(20, wait.Y);

        var move = NetCodec.DecodeChangeMoveType(NetCodec.EncodeChangeMoveType(new ChangeMoveTypeNetMsg
        {
            EntityValue = 8,
            EntityGeneration = 2,
            Running = false,
            RunSpeed = 120f,
            WalkSpeed = 80f,
            InWater = true
        }));
        Assert.False(move.Running);
        Assert.Equal(120f, move.RunSpeed);
        Assert.True(move.InWater);

        var etc = NetCodec.DecodeEtcStatusUpdate(NetCodec.EncodeEtcStatusUpdate(new EtcStatusUpdateNetMsg
        {
            EntityValue = 8,
            EntityGeneration = 2,
            Charges = 0,
            WeightPenalty = 2,
            ChatBlocked = true,
            DangerArea = false,
            GradePenalty = true,
            CharmOfCourage = false,
            DeathPenalty = 5
        }));
        Assert.Equal(2, etc.WeightPenalty);
        Assert.True(etc.ChatBlocked);
        Assert.True(etc.GradePenalty);
        Assert.Equal(5, etc.DeathPenalty);
    }
}
