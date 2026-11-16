using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        public static byte[] EncodeSelectTarget(SelectTargetNetMsg m) => Write(w => w.WriteInt32(m.TargetObjectId));
        public static SelectTargetNetMsg DecodeSelectTarget(ReadOnlySpan<byte> p) =>
            new() { TargetObjectId = new Reader(p).ReadInt32() };

        public static byte[] EncodeAttackRequest(AttackRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.TargetObjectId);
            if (m.Forced.HasValue)
                w.WriteByte(m.Forced.Value ? (byte)1 : (byte)0);
        });
        public static AttackRequestNetMsg DecodeAttackRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            var m = new AttackRequestNetMsg { TargetObjectId = r.ReadInt32() };
            if (r.Remaining > 0)
                m.Forced = r.ReadByte() != 0;
            return m;
        }

        public static byte[] EncodeTargetSelected(TargetSelectedNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.TargetObjectId);
            w.WriteInt32(m.ColorHint);
        });

        public static TargetSelectedNetMsg DecodeTargetSelected(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new TargetSelectedNetMsg { TargetObjectId = r.ReadInt32(), ColorHint = r.ReadInt32() };
        }

        public static byte[] EncodeTargetUnselected(TargetUnselectedNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.TargetObjectId);
        });

        public static TargetUnselectedNetMsg DecodeTargetUnselected(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new TargetUnselectedNetMsg { TargetObjectId = r.ReadInt32() };
        }

        public static byte[] EncodeAttackState(AttackStateMsg m) => Write(w =>
        {
            w.WriteUInt64(m.AttackerValue);
            w.WriteUInt32(m.AttackerGeneration);
            w.WriteUInt64(m.TargetValue);
            w.WriteUInt32(m.TargetGeneration);
            w.WriteInt32(m.Damage);
            w.WriteByte(m.Flags);
            w.WriteInt32(m.AttackerX);
            w.WriteInt32(m.AttackerY);
            w.WriteInt32(m.AttackerZ);
            w.WriteInt32(m.TargetX);
            w.WriteInt32(m.TargetY);
            w.WriteInt32(m.TargetZ);
            var extra = m.ExtraHits ?? Array.Empty<AttackHitNet>();
            if (extra.Length > 0)
            {
                w.WriteInt32(extra.Length);
                foreach (var hit in extra)
                {
                    w.WriteInt32(hit?.Damage ?? 0);
                    w.WriteByte(hit?.Flags ?? 0);
                }
            }
        });

        public static byte[] EncodeMagicSkillLaunched(MagicSkillLaunchedNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.CasterObjectId);
            w.WriteInt32(m.SkillId);
            w.WriteInt32(m.SkillLevel);
            var targets = m.TargetObjectIds ?? Array.Empty<int>();
            w.WriteInt32(targets.Length);
            foreach (int t in targets)
                w.WriteInt32(t);
        });

        public static MagicSkillLaunchedNetMsg DecodeMagicSkillLaunched(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            var m = new MagicSkillLaunchedNetMsg { CasterObjectId = r.ReadInt32(), SkillId = r.ReadInt32(), SkillLevel = r.ReadInt32() };
            int n = r.ReadInt32();
            if (n < 0 || n > 256)
                throw new InvalidDataException("launched target count");
            var targets = new int[n];
            for (int i = 0; i < n; i++)
                targets[i] = r.ReadInt32();
            m.TargetObjectIds = targets;
            return m;
        }

        public static byte[] EncodeMagicSkillCanceled(MagicSkillCanceledNetMsg m) => Write(w => w.WriteInt32(m.ObjectId));

        public static MagicSkillCanceledNetMsg DecodeMagicSkillCanceled(ReadOnlySpan<byte> p) =>
            new() { ObjectId = new Reader(p).ReadInt32() };

        public static byte[] EncodePlaySound(PlaySoundNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Type);
            w.WriteString(m.File ?? "");
            w.WriteInt32(m.ObjectId);
            w.WriteInt32(m.X);
            w.WriteInt32(m.Y);
            w.WriteInt32(m.Z);
            w.WriteInt32(m.DelayMs);
        });

        public static PlaySoundNetMsg DecodePlaySound(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PlaySoundNetMsg
            {
                Type = r.ReadInt32(),
                File = r.ReadString(),
                ObjectId = r.ReadInt32(),
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Z = r.ReadInt32(),
                DelayMs = r.ReadInt32()
            };
        }

        public static AttackStateMsg DecodeAttackState(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            var m = new AttackStateMsg
            {
                AttackerValue = r.ReadUInt64(),
                AttackerGeneration = r.ReadUInt32(),
                TargetValue = r.ReadUInt64(),
                TargetGeneration = r.ReadUInt32(),
                Damage = r.ReadInt32(),
                Flags = r.ReadByte(),
                AttackerX = r.ReadInt32(),
                AttackerY = r.ReadInt32(),
                AttackerZ = r.ReadInt32(),
                TargetX = r.ReadInt32(),
                TargetY = r.ReadInt32(),
                TargetZ = r.ReadInt32()
            };
            if (r.Remaining >= 4)
            {
                int n = r.ReadInt32();
                if (n < 0 || n > 8)
                    throw new InvalidDataException("attack hit count");
                var extra = new AttackHitNet[n];
                for (int i = 0; i < n; i++)
                    extra[i] = new AttackHitNet { Damage = r.ReadInt32(), Flags = r.ReadByte() };
                m.ExtraHits = extra;
            }
            return m;
        }

        public static byte[] EncodeStatusUpdate(StatusUpdateNetMsg m) => Write(w =>
        {
            w.WriteUInt64(m.EntityValue);
            w.WriteUInt32(m.EntityGeneration);
            var ids = m.AttributeIds ?? Array.Empty<int>();
            var vals = m.Values ?? Array.Empty<int>();
            int n = Math.Min(ids.Length, vals.Length);
            w.WriteInt32(n);
            for (int i = 0; i < n; i++)
            {
                w.WriteInt32(ids[i]);
                w.WriteInt32(vals[i]);
            }
        });

        public static StatusUpdateNetMsg DecodeStatusUpdate(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            ulong entity = r.ReadUInt64();
            uint gen = r.ReadUInt32();
            int n = r.ReadInt32();
            if (n < 0)
                n = 0;
            var ids = new int[n];
            var vals = new int[n];
            for (int i = 0; i < n; i++)
            {
                ids[i] = r.ReadInt32();
                vals[i] = r.ReadInt32();
            }

            return new StatusUpdateNetMsg
            {
                EntityValue = entity,
                EntityGeneration = gen,
                AttributeIds = ids,
                Values = vals
            };
        }

        public static byte[] EncodeEntityDie(EntityDieMsg m) => Write(w =>
        {
            w.WriteUInt64(m.EntityValue);
            w.WriteUInt32(m.EntityGeneration);
            w.WriteInt32(m.KillerObjectId);
            if (m.RestartFlags != 0)
                w.WriteByte((byte)m.RestartFlags);
        });

        public static EntityDieMsg DecodeEntityDie(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            var die = new EntityDieMsg
            {
                EntityValue = r.ReadUInt64(),
                EntityGeneration = r.ReadUInt32(),
                KillerObjectId = r.ReadInt32()
            };
            if (r.Remaining >= 1)
                die.RestartFlags = r.ReadByte();
            return die;
        }
    }
}
