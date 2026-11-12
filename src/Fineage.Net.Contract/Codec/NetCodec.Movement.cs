using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        public static byte[] EncodeMoveInput(MoveInputMsg m) => Write(w =>
        {
            w.WriteInt32(m.DestX);
            w.WriteInt32(m.DestY);
            w.WriteInt32(m.DestZ);
        });

        public static MoveInputMsg DecodeMoveInput(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new MoveInputMsg { DestX = r.ReadInt32(), DestY = r.ReadInt32(), DestZ = r.ReadInt32() };
        }

        public static byte[] EncodeValidatePosition(ValidatePositionMsg m) => Write(w =>
        {
            w.WriteInt32(m.X);
            w.WriteInt32(m.Y);
            w.WriteInt32(m.Z);
            w.WriteInt32(m.Heading);
        });

        public static ValidatePositionMsg DecodeValidatePosition(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new ValidatePositionMsg { X = r.ReadInt32(), Y = r.ReadInt32(), Z = r.ReadInt32(), Heading = r.ReadInt32() };
        }

        public static byte[] EncodeActionUse(ActionUseNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ActionId);
            w.WriteByte(m.CtrlPressed ? (byte)1 : (byte)0);
            w.WriteByte(m.ShiftPressed ? (byte)1 : (byte)0);
        });

        public static ActionUseNetMsg DecodeActionUse(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new ActionUseNetMsg { ActionId = r.ReadInt32(), CtrlPressed = r.ReadByte() != 0, ShiftPressed = r.ReadByte() != 0 };
        }

        public static byte[] EncodeChangeMoveType(ChangeMoveTypeNetMsg m) => Write(w =>
        {
            w.WriteUInt64(m.EntityValue);
            w.WriteUInt32(m.EntityGeneration);
            w.WriteByte(m.Running ? (byte)1 : (byte)0);
            w.WriteFloat(m.RunSpeed);
            w.WriteFloat(m.WalkSpeed);
            w.WriteByte(m.InWater ? (byte)1 : (byte)0);
        });

        public static ChangeMoveTypeNetMsg DecodeChangeMoveType(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new ChangeMoveTypeNetMsg
            {
                EntityValue = r.ReadUInt64(),
                EntityGeneration = r.ReadUInt32(),
                Running = r.ReadByte() != 0,
                RunSpeed = r.ReadFloat(),
                WalkSpeed = r.ReadFloat(),
                InWater = r.ReadByte() != 0
            };
        }

        /// <summary>Mobius <c>ChangeWaitType</c>: wait type + the actor position the client settles the pawn to.</summary>
        public static byte[] EncodeChangeWaitType(ChangeWaitTypeNetMsg m) => Write(w =>
        {
            w.WriteUInt64(m.EntityValue);
            w.WriteUInt32(m.EntityGeneration);
            w.WriteInt32(m.WaitType);
            w.WriteInt32(m.X);
            w.WriteInt32(m.Y);
            w.WriteInt32(m.Z);
        });

        public static ChangeWaitTypeNetMsg DecodeChangeWaitType(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new ChangeWaitTypeNetMsg
            {
                EntityValue = r.ReadUInt64(),
                EntityGeneration = r.ReadUInt32(),
                WaitType = r.ReadInt32(),
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Z = r.ReadInt32()
            };
        }

        /// <summary>Mobius <c>SocialAction</c>: actor + gesture id.</summary>
        public static byte[] EncodeSocialAction(SocialActionNetMsg m) => Write(w =>
        {
            w.WriteUInt64(m.EntityValue);
            w.WriteUInt32(m.EntityGeneration);
            w.WriteInt32(m.ActionId);
        });

        public static SocialActionNetMsg DecodeSocialAction(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new SocialActionNetMsg
            {
                EntityValue = r.ReadUInt64(),
                EntityGeneration = r.ReadUInt32(),
                ActionId = r.ReadInt32()
            };
        }

        /// <summary>Mobius <c>EtcStatusUpdate</c> field order (<c>EtcStatusUpdate.java:39-45</c>).</summary>
        public static byte[] EncodeEtcStatusUpdate(EtcStatusUpdateNetMsg m) => Write(w =>
        {
            w.WriteUInt64(m.EntityValue);
            w.WriteUInt32(m.EntityGeneration);
            w.WriteInt32(m.Charges);
            w.WriteInt32(m.WeightPenalty);
            w.WriteByte(m.ChatBlocked ? (byte)1 : (byte)0);
            w.WriteByte(m.DangerArea ? (byte)1 : (byte)0);
            w.WriteByte(m.GradePenalty ? (byte)1 : (byte)0);
            w.WriteByte(m.CharmOfCourage ? (byte)1 : (byte)0);
            w.WriteInt32(m.DeathPenalty);
        });

        public static EtcStatusUpdateNetMsg DecodeEtcStatusUpdate(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new EtcStatusUpdateNetMsg
            {
                EntityValue = r.ReadUInt64(),
                EntityGeneration = r.ReadUInt32(),
                Charges = r.ReadInt32(),
                WeightPenalty = r.ReadInt32(),
                ChatBlocked = r.ReadByte() != 0,
                DangerArea = r.ReadByte() != 0,
                GradePenalty = r.ReadByte() != 0,
                CharmOfCourage = r.ReadByte() != 0,
                DeathPenalty = r.ReadInt32()
            };
        }

        public static byte[] EncodeAttackStance(AttackStanceNetMsg m) => Write(w =>
        {
            w.WriteUInt64(m.EntityValue);
            w.WriteUInt32(m.EntityGeneration);
        });
        public static AttackStanceNetMsg DecodeAttackStance(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new AttackStanceNetMsg { EntityValue = r.ReadUInt64(), EntityGeneration = r.ReadUInt32() };
        }

        public static byte[] EncodeAutoSoulShot(AutoSoulShotNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ItemId);
            w.WriteByte(m.Enabled ? (byte)1 : (byte)0);
        });

        public static AutoSoulShotNetMsg DecodeAutoSoulShot(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new AutoSoulShotNetMsg { ItemId = r.ReadInt32(), Enabled = r.ReadByte() != 0 };
        }

        public static byte[] EncodeShortCut(ShortCutNetMsg m) => Write(w => WriteShortCut(w, m));

        public static ShortCutNetMsg DecodeShortCut(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return ReadShortCut(ref r);
        }

        public static byte[] EncodeShortCutInit(ShortCutInitNetMsg m) => Write(w =>
        {
            var rows = m.Shortcuts ?? Array.Empty<ShortCutNetMsg>();
            w.WriteInt32(rows.Length);
            for (int i = 0; i < rows.Length; i++)
                WriteShortCut(w, rows[i]);
        });

        public static ShortCutInitNetMsg DecodeShortCutInit(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int n = r.ReadInt32();
            if (n < 0 || n > 1024)
                throw new InvalidDataException("shortcut list length");
            var rows = new ShortCutNetMsg[n];
            for (int i = 0; i < n; i++)
                rows[i] = ReadShortCut(ref r);
            return new ShortCutInitNetMsg { Shortcuts = rows };
        }

        public static byte[] EncodeShortCutDel(ShortCutDelNetMsg m) => Write(w => w.WriteInt32(m.Slot));

        public static ShortCutDelNetMsg DecodeShortCutDel(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new ShortCutDelNetMsg { Slot = r.ReadInt32() };
        }

        static void WriteShortCut(Writer w, ShortCutNetMsg m)
        {
            w.WriteInt32(m.Type);
            w.WriteInt32(m.Slot);
            w.WriteInt32(m.Id);
            w.WriteInt32(m.Level);
        }

        static ShortCutNetMsg ReadShortCut(ref Reader r) => new ShortCutNetMsg
        {
            Type = r.ReadInt32(),
            Slot = r.ReadInt32(),
            Id = r.ReadInt32(),
            Level = r.ReadInt32()
        };

        public static byte[] EncodeStopMove(StopMoveMsg _) => Array.Empty<byte>();
        public static StopMoveMsg DecodeStopMove(ReadOnlySpan<byte> _) => new();

        public static byte[] EncodeMoveState(MoveStateMsg m) => Write(w =>
        {
            w.WriteUInt64(m.EntityValue);
            w.WriteUInt32(m.EntityGeneration);
            w.WriteInt32(m.OriginX);
            w.WriteInt32(m.OriginY);
            w.WriteInt32(m.OriginZ);
            w.WriteInt32(m.DestX);
            w.WriteInt32(m.DestY);
            w.WriteInt32(m.DestZ);
            w.WriteByte(m.Running ? (byte)1 : (byte)0);
        });

        public static MoveStateMsg DecodeMoveState(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new MoveStateMsg
            {
                EntityValue = r.ReadUInt64(),
                EntityGeneration = r.ReadUInt32(),
                OriginX = r.ReadInt32(),
                OriginY = r.ReadInt32(),
                OriginZ = r.ReadInt32(),
                DestX = r.ReadInt32(),
                DestY = r.ReadInt32(),
                DestZ = r.ReadInt32(),
                Running = r.ReadByte() != 0
            };
        }

        public static byte[] EncodePositionCorrection(PositionCorrectionMsg m) => Write(w =>
        {
            w.WriteUInt64(m.EntityValue);
            w.WriteUInt32(m.EntityGeneration);
            w.WriteInt32(m.X);
            w.WriteInt32(m.Y);
            w.WriteInt32(m.Z);
            w.WriteInt32(m.Heading);
        });

        public static byte[] EncodeEntityDespawn(EntityDespawnMsg m) => Write(w =>
        {
            w.WriteUInt64(m.EntityValue);
            w.WriteUInt32(m.EntityGeneration);
        });

        public static EntityDespawnMsg DecodeEntityDespawn(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new EntityDespawnMsg
            {
                EntityValue = r.ReadUInt64(),
                EntityGeneration = r.ReadUInt32()
            };
        }
    }
}
