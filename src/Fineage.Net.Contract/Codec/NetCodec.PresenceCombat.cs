using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        public static byte[] EncodeEntitySpawn(EntitySpawnMsg m) => Write(w =>
        {
            w.WriteUInt64(m.EntityValue);
            w.WriteUInt32(m.EntityGeneration);
            w.WriteByte((byte)m.Kind);
            w.WriteByte(m.IsSelf ? (byte)1 : (byte)0);
            w.WriteString(m.Name);
            w.WriteInt32(m.TemplateId);
            w.WriteInt32(m.ClassId);
            w.WriteInt32(m.Race);
            w.WriteInt32(m.Sex);
            w.WriteInt32(m.X);
            w.WriteInt32(m.Y);
            w.WriteInt32(m.Z);
            w.WriteInt32(m.Heading);
            w.WriteFloat(m.RunSpeed);
            w.WriteFloat(m.WalkSpeed);
            w.WriteByte(m.Running ? (byte)1 : (byte)0);
            w.WriteByte(m.Attackable ? (byte)1 : (byte)0);
            w.WriteInt32Array(m.Paperdoll);
            w.WriteByte(m.Dead ? (byte)1 : (byte)0);
            w.WriteInt32(m.Level);
            w.WriteInt32(m.MaxHp);
            w.WriteInt32(m.PAtkSpd);
        });

        public static EntitySpawnMsg DecodeEntitySpawn(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            var msg = new EntitySpawnMsg
            {
                EntityValue = r.ReadUInt64(),
                EntityGeneration = r.ReadUInt32(),
                Kind = (EntityKindNet)r.ReadByte(),
                IsSelf = r.ReadByte() != 0,
                Name = r.ReadString(),
                TemplateId = r.ReadInt32(),
                ClassId = r.ReadInt32(),
                Race = r.ReadInt32(),
                Sex = r.ReadInt32(),
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Z = r.ReadInt32(),
                Heading = r.ReadInt32(),
                RunSpeed = r.ReadFloat(),
                WalkSpeed = r.ReadFloat(),
                Running = r.ReadByte() != 0
            };
            if (r.Remaining > 0)
                msg.Attackable = r.ReadByte() != 0;
            if (r.Remaining > 0)
                msg.Paperdoll = r.ReadInt32Array();
            if (r.Remaining > 0)
                msg.Dead = r.ReadByte() != 0;
            if (r.Remaining > 0)
                msg.Level = r.ReadInt32();
            if (r.Remaining > 0)
                msg.MaxHp = r.ReadInt32();
            if (r.Remaining > 0)
                msg.PAtkSpd = r.ReadInt32();
            return msg;
        }

        public static byte[] EncodeUserInfo(UserInfoNetMsg m) => Write(w =>
        {
            w.WriteUInt64(m.EntityValue);
            w.WriteUInt32(m.EntityGeneration);
            w.WriteString(m.Name);
            w.WriteInt32(m.ClassId);
            w.WriteInt32(m.Race);
            w.WriteInt32(m.Sex);
            w.WriteInt32(m.X);
            w.WriteInt32(m.Y);
            w.WriteInt32(m.Z);
            w.WriteFloat(m.RunSpeed);
            w.WriteFloat(m.WalkSpeed);
            w.WriteByte(m.Running ? (byte)1 : (byte)0);
            w.WriteInt32(m.Level);
            w.WriteInt64(m.Exp);
            w.WriteInt32(m.Sp);
            w.WriteInt32(m.CurHp);
            w.WriteInt32(m.MaxHp);
            w.WriteInt32(m.CurMp);
            w.WriteInt32(m.MaxMp);
            w.WriteInt32(m.CurCp);
            w.WriteInt32(m.MaxCp);
            w.WriteInt32(m.Str);
            w.WriteInt32(m.Dex);
            w.WriteInt32(m.Con);
            w.WriteInt32(m.Int);
            w.WriteInt32(m.Wit);
            w.WriteInt32(m.Men);
            w.WriteInt32(m.PAtk);
            w.WriteInt32(m.MAtk);
            w.WriteInt32(m.PDef);
            w.WriteInt32(m.MDef);
            w.WriteInt32(m.PAtkSpd);
            w.WriteInt32(m.MAtkSpd);
            w.WriteInt32(m.Accuracy);
            w.WriteInt32(m.Evasion);
            w.WriteInt32(m.CritRate);
            w.WriteInt32(m.WeaponGradePenalty);
            w.WriteInt32(m.ArmorGradePenalty);
            w.WriteInt32(m.ClanId);
            w.WriteInt32(m.CurrentWeight);
            w.WriteInt32(m.MaxWeight);
            var slots = m.Paperdoll ?? Array.Empty<PaperdollSlotNet>();
            w.WriteInt32(slots.Length);
            for (int i = 0; i < slots.Length; i++)
            {
                w.WriteInt32(slots[i]?.ObjectId ?? 0);
                w.WriteInt32(slots[i]?.ItemId ?? 0);
                w.WriteInt32(slots[i]?.EnchantLevel ?? 0);
            }
        });

        public static UserInfoNetMsg DecodeUserInfo(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            var m = new UserInfoNetMsg
            {
                EntityValue = r.ReadUInt64(),
                EntityGeneration = r.ReadUInt32(),
                Name = r.ReadString(),
                ClassId = r.ReadInt32(),
                Race = r.ReadInt32(),
                Sex = r.ReadInt32(),
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Z = r.ReadInt32(),
                RunSpeed = r.ReadFloat(),
                WalkSpeed = r.ReadFloat(),
                Running = r.ReadByte() != 0,
                Level = r.ReadInt32(),
                Exp = r.ReadInt64(),
                Sp = r.ReadInt32(),
                CurHp = r.ReadInt32(),
                MaxHp = r.ReadInt32(),
                CurMp = r.ReadInt32(),
                MaxMp = r.ReadInt32(),
                CurCp = r.ReadInt32(),
                MaxCp = r.ReadInt32(),
                Str = r.ReadInt32(),
                Dex = r.ReadInt32(),
                Con = r.ReadInt32(),
                Int = r.ReadInt32(),
                Wit = r.ReadInt32(),
                Men = r.ReadInt32(),
                PAtk = r.ReadInt32(),
                MAtk = r.ReadInt32(),
                PDef = r.ReadInt32(),
                MDef = r.ReadInt32(),
                PAtkSpd = r.ReadInt32(),
                MAtkSpd = r.ReadInt32(),
                Accuracy = r.ReadInt32(),
                Evasion = r.ReadInt32(),
                CritRate = r.ReadInt32(),
                WeaponGradePenalty = r.ReadInt32(),
                ArmorGradePenalty = r.ReadInt32(),
                ClanId = r.ReadInt32(),
                CurrentWeight = r.ReadInt32(),
                MaxWeight = r.ReadInt32()
            };
            int n = r.ReadInt32();
            if (n < 0 || n > 64)
                throw new InvalidDataException("paperdoll length");
            var slots = new PaperdollSlotNet[n];
            for (int i = 0; i < n; i++)
                slots[i] = new PaperdollSlotNet { ObjectId = r.ReadInt32(), ItemId = r.ReadInt32(), EnchantLevel = r.ReadInt32() };
            m.Paperdoll = slots;
            return m;
        }

        public static byte[] EncodePlayerStats(PlayerStatsMsg m) => Write(w =>
        {
            w.WriteUInt64(m.EntityValue);
            w.WriteUInt32(m.EntityGeneration);
            w.WriteInt32(m.Level);
            w.WriteInt64(m.Exp);
            w.WriteInt32(m.Sp);
            w.WriteInt32(m.Hp);
            w.WriteInt32(m.MaxHp);
            w.WriteInt32(m.Mp);
            w.WriteInt32(m.MaxMp);
            w.WriteInt32(m.Cp);
            w.WriteInt32(m.MaxCp);
        });

        public static PlayerStatsMsg DecodePlayerStats(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PlayerStatsMsg
            {
                EntityValue = r.ReadUInt64(),
                EntityGeneration = r.ReadUInt32(),
                Level = r.ReadInt32(),
                Exp = r.ReadInt64(),
                Sp = r.ReadInt32(),
                Hp = r.ReadInt32(),
                MaxHp = r.ReadInt32(),
                Mp = r.ReadInt32(),
                MaxMp = r.ReadInt32(),
                Cp = r.ReadInt32(),
                MaxCp = r.ReadInt32()
            };
        }

        public static byte[] EncodeItemList(ItemListNetMsg m) => Write(w =>
        {
            var rows = m.Items ?? Array.Empty<ItemListEntryNet>();
            w.WriteInt32(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                w.WriteInt32(row.ObjectId);
                w.WriteInt32(row.ItemId);
                w.WriteInt32(row.Count);
                w.WriteInt32(row.EnchantLevel);
                w.WriteInt32(row.EquippedSlot);
                w.WriteString(row.BodySlot ?? "");
                w.WriteString(row.Kind ?? "");
                w.WriteString(row.Icon ?? "");
                w.WriteString(row.WeaponType ?? "");
                w.WriteString(row.ArmorType ?? "");
            }
        });

        public static ItemListNetMsg DecodeItemList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int n = r.ReadInt32();
            if (n < 0 || n > 4096)
                throw new InvalidDataException("item list length");
            var rows = new ItemListEntryNet[n];
            for (int i = 0; i < n; i++)
            {
                rows[i] = new ItemListEntryNet
                {
                    ObjectId = r.ReadInt32(),
                    ItemId = r.ReadInt32(),
                    Count = r.ReadInt32(),
                    EnchantLevel = r.ReadInt32(),
                    EquippedSlot = r.ReadInt32(),
                    BodySlot = r.ReadString(),
                    Kind = r.ReadString(),
                    Icon = r.ReadString(),
                    WeaponType = r.ReadString(),
                    ArmorType = r.ReadString()
                };
            }

            return new ItemListNetMsg { Items = rows };
        }

        public static byte[] EncodeSkillList(SkillListNetMsg m) => Write(w =>
        {
            var rows = m.Skills ?? Array.Empty<SkillListEntryNet>();
            w.WriteInt32(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                w.WriteInt32(row.SkillId);
                w.WriteInt32(row.Level);
                w.WriteInt32(row.ReuseRemainingMs);
            }
        });

        public static SkillListNetMsg DecodeSkillList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int n = r.ReadInt32();
            if (n < 0 || n > 4096)
                throw new InvalidDataException("skill list length");
            var rows = new SkillListEntryNet[n];
            for (int i = 0; i < n; i++)
            {
                rows[i] = new SkillListEntryNet
                {
                    SkillId = r.ReadInt32(),
                    Level = r.ReadInt32(),
                    ReuseRemainingMs = r.ReadInt32()
                };
            }

            return new SkillListNetMsg { Skills = rows };
        }

        public static byte[] EncodeAcquireSkillList(AcquireSkillListNetMsg m) => Write(w =>
        {
            var rows = m.Skills ?? Array.Empty<AcquireSkillEntryNet>();
            w.WriteInt32(m.SkillType);
            w.WriteInt32(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                w.WriteInt32(row.SkillId);
                w.WriteInt32(row.NextLevel);
                w.WriteInt32(row.MaxLevel);
                w.WriteInt32(row.SpCost);
                w.WriteInt32(row.Requirements);
            }
        });

        public static AcquireSkillListNetMsg DecodeAcquireSkillList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int type = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0 || n > 4096)
                throw new InvalidDataException("acquire skill list length");
            var rows = new AcquireSkillEntryNet[n];
            for (int i = 0; i < n; i++)
            {
                rows[i] = new AcquireSkillEntryNet
                {
                    SkillId = r.ReadInt32(),
                    NextLevel = r.ReadInt32(),
                    MaxLevel = r.ReadInt32(),
                    SpCost = r.ReadInt32(),
                    Requirements = r.ReadInt32()
                };
            }

            return new AcquireSkillListNetMsg { SkillType = type, Skills = rows };
        }

        public static byte[] EncodeAcquireSkillInfo(AcquireSkillInfoNetMsg m) => Write(w =>
        {
            var reqs = m.Requirements ?? Array.Empty<AcquireSkillRequirementNet>();
            w.WriteInt32(m.SkillId);
            w.WriteInt32(m.Level);
            w.WriteInt32(m.SpCost);
            w.WriteInt32(m.SkillType);
            w.WriteInt32(reqs.Length);
            for (int i = 0; i < reqs.Length; i++)
            {
                var req = reqs[i];
                w.WriteInt32(req.Type);
                w.WriteInt32(req.ItemId);
                w.WriteInt64(req.Count);
                w.WriteInt32(req.Unk);
            }
        });

        public static AcquireSkillInfoNetMsg DecodeAcquireSkillInfo(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int id = r.ReadInt32();
            int level = r.ReadInt32();
            int sp = r.ReadInt32();
            int type = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0 || n > 1024)
                throw new InvalidDataException("acquire skill info length");
            var reqs = new AcquireSkillRequirementNet[n];
            for (int i = 0; i < n; i++)
            {
                reqs[i] = new AcquireSkillRequirementNet
                {
                    Type = r.ReadInt32(),
                    ItemId = r.ReadInt32(),
                    Count = r.ReadInt64(),
                    Unk = r.ReadInt32()
                };
            }

            return new AcquireSkillInfoNetMsg
            {
                SkillId = id,
                Level = level,
                SpCost = sp,
                SkillType = type,
                Requirements = reqs
            };
        }

        /// <summary>Mobius <c>RequestAcquireSkillInfo</c> / <c>RequestAcquireSkill</c>: skill id, level, skill type.</summary>
        public static byte[] EncodeAcquireSkillRequest(int skillId, int level, int skillType) => Write(w =>
        {
            w.WriteInt32(skillId);
            w.WriteInt32(level);
            w.WriteInt32(skillType);
        });

        public static (int SkillId, int Level, int SkillType) DecodeAcquireSkillRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return (r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
        }

        public static byte[] EncodeEnchantRequest(EnchantRequestNetMsg m) => Write(w => w.WriteInt32(m.ScrollObjectId));

        public static EnchantRequestNetMsg DecodeEnchantRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new EnchantRequestNetMsg { ScrollObjectId = r.ReadInt32() };
        }

        public static byte[] EncodeEnchantConfirm(EnchantConfirmNetMsg m) => Write(w => w.WriteInt32(m.TargetObjectId));

        public static EnchantConfirmNetMsg DecodeEnchantConfirm(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new EnchantConfirmNetMsg { TargetObjectId = r.ReadInt32() };
        }

        public static byte[] EncodeEnchantResult(EnchantResultNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Result);
            w.WriteInt32(m.EnchantLevel);
            w.WriteInt32(m.ObjectId);
            w.WriteInt32(m.CrystalItemId);
            w.WriteInt32(m.CrystalCount);
        });

        public static EnchantResultNetMsg DecodeEnchantResult(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new EnchantResultNetMsg
            {
                Result = r.ReadInt32(),
                EnchantLevel = r.ReadInt32(),
                ObjectId = r.ReadInt32(),
                CrystalItemId = r.ReadInt32(),
                CrystalCount = r.ReadInt32()
            };
        }
        public static PositionCorrectionMsg DecodePositionCorrection(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PositionCorrectionMsg
            {
                EntityValue = r.ReadUInt64(),
                EntityGeneration = r.ReadUInt32(),
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Z = r.ReadInt32(),
                Heading = r.ReadInt32()
            };
        }

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

        public static byte[] EncodeSelectTarget(SelectTargetNetMsg m) => Write(w => w.WriteInt32(m.TargetObjectId));
        public static SelectTargetNetMsg DecodeSelectTarget(ReadOnlySpan<byte> p) =>
            new() { TargetObjectId = new Reader(p).ReadInt32() };

        public static byte[] EncodeAttackRequest(AttackRequestNetMsg m) => Write(w => w.WriteInt32(m.TargetObjectId));
        public static AttackRequestNetMsg DecodeAttackRequest(ReadOnlySpan<byte> p) =>
            new() { TargetObjectId = new Reader(p).ReadInt32() };

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
        });

        public static AttackStateMsg DecodeAttackState(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new AttackStateMsg
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
        });

        public static EntityDieMsg DecodeEntityDie(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new EntityDieMsg
            {
                EntityValue = r.ReadUInt64(),
                EntityGeneration = r.ReadUInt32(),
                KillerObjectId = r.ReadInt32()
            };
        }

        public static byte[] EncodeDropItem(DropItemNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.DropperObjectId);
            w.WriteUInt64(m.EntityValue);
            w.WriteUInt32(m.EntityGeneration);
            w.WriteInt32(m.ItemId);
            w.WriteInt32(m.X);
            w.WriteInt32(m.Y);
            w.WriteInt32(m.Z);
            w.WriteByte(m.Stackable ? (byte)1 : (byte)0);
            w.WriteInt32(m.Count);
        });

        public static DropItemNetMsg DecodeDropItem(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new DropItemNetMsg
            {
                DropperObjectId = r.ReadInt32(),
                EntityValue = r.ReadUInt64(),
                EntityGeneration = r.ReadUInt32(),
                ItemId = r.ReadInt32(),
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Z = r.ReadInt32(),
                Stackable = r.ReadByte() != 0,
                Count = r.ReadInt32()
            };
        }

        public static byte[] EncodePickupRequest(PickupRequestNetMsg m) => Write(w => w.WriteInt32(m.GroundObjectId));
        public static PickupRequestNetMsg DecodePickupRequest(ReadOnlySpan<byte> p) =>
            new() { GroundObjectId = new Reader(p).ReadInt32() };

        public static byte[] EncodeInventoryUpdate(InventoryUpdateNetMsg m) => Write(w =>
        {
            var rows = m.Changes ?? Array.Empty<InventoryUpdateEntryNet>();
            w.WriteInt32(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                w.WriteInt32(row.Change);
                w.WriteInt32(row.ObjectId);
                w.WriteInt32(row.ItemId);
                w.WriteInt32(row.Count);
                w.WriteInt32(row.EnchantLevel);
                w.WriteInt32(row.EquippedSlot);
                w.WriteString(row.BodySlot ?? "");
                w.WriteString(row.Kind ?? "");
                w.WriteString(row.Icon ?? "");
                w.WriteString(row.WeaponType ?? "");
                w.WriteString(row.ArmorType ?? "");
            }
        });

        public static InventoryUpdateNetMsg DecodeInventoryUpdate(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int n = r.ReadInt32();
            if (n < 0)
                n = 0;
            var rows = new InventoryUpdateEntryNet[n];
            for (int i = 0; i < n; i++)
            {
                rows[i] = new InventoryUpdateEntryNet
                {
                    Change = r.ReadInt32(),
                    ObjectId = r.ReadInt32(),
                    ItemId = r.ReadInt32(),
                    Count = r.ReadInt32(),
                    EnchantLevel = r.ReadInt32(),
                    EquippedSlot = r.ReadInt32(),
                    BodySlot = r.ReadString(),
                    Kind = r.ReadString(),
                    Icon = r.ReadString(),
                    WeaponType = r.ReadString(),
                    ArmorType = r.ReadString()
                };
            }

            return new InventoryUpdateNetMsg { Changes = rows };
        }

        public static byte[] EncodeDropItemRequest(DropItemRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ObjectId);
            w.WriteInt32(m.Count);
            w.WriteInt32(m.X);
            w.WriteInt32(m.Y);
            w.WriteInt32(m.Z);
        });

        public static DropItemRequestNetMsg DecodeDropItemRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new DropItemRequestNetMsg
            {
                ObjectId = r.ReadInt32(),
                Count = r.ReadInt32(),
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Z = r.ReadInt32()
            };
        }

        public static byte[] EncodeDestroyItemRequest(DestroyItemRequestMsg m) => Write(w =>
        {
            w.WriteInt32(m.ObjectId);
            w.WriteInt32(m.Count);
        });

        public static DestroyItemRequestMsg DecodeDestroyItemRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new DestroyItemRequestMsg { ObjectId = r.ReadInt32(), Count = r.ReadInt32() };
        }

        public static byte[] EncodeUseItem(UseItemNetMsg m) => Write(w => w.WriteInt32(m.ObjectId));
        public static UseItemNetMsg DecodeUseItem(ReadOnlySpan<byte> p) =>
            new() { ObjectId = new Reader(p).ReadInt32() };

        public static byte[] EncodeUnequipItem(UnequipItemNetMsg m) => Write(w => w.WriteInt32(m.PaperdollSlot));
        public static UnequipItemNetMsg DecodeUnequipItem(ReadOnlySpan<byte> p) =>
            new() { PaperdollSlot = new Reader(p).ReadInt32() };

        public static byte[] EncodeSay(SayNetMsg m) => Write(w =>
        {
            w.WriteString(m.Text ?? "");
            w.WriteInt32(m.ChatType);
        });

        public static SayNetMsg DecodeSay(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new SayNetMsg { Text = r.ReadString(), ChatType = r.ReadInt32() };
        }

        public static byte[] EncodeCreatureSay(CreatureSayNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ObjectId);
            w.WriteString(m.Name ?? "");
            w.WriteString(m.Text ?? "");
            w.WriteInt32(m.ChatType);
        });

        public static CreatureSayNetMsg DecodeCreatureSay(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new CreatureSayNetMsg
            {
                ObjectId = r.ReadInt32(),
                Name = r.ReadString(),
                Text = r.ReadString(),
                ChatType = r.ReadInt32()
            };
        }

        public static byte[] EncodeSystemMessage(SystemMessageNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Id);
            w.WriteString(m.Text ?? "");
            var pars = m.Params ?? Array.Empty<string>();
            w.WriteInt32(pars.Length);
            for (int i = 0; i < pars.Length; i++)
                w.WriteString(pars[i] ?? "");
        });

        public static SystemMessageNetMsg DecodeSystemMessage(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int id = r.ReadInt32();
            string text = r.ReadString();
            int n = r.ReadInt32();
            if (n < 0)
                n = 0;
            var pars = new string[n];
            for (int i = 0; i < n; i++)
                pars[i] = r.ReadString();
            return new SystemMessageNetMsg { Id = id, Text = text, Params = pars };
        }

        public static byte[] EncodeUseSkill(UseSkillNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.SkillId);
            w.WriteInt32(m.TargetObjectId);
            w.WriteByte(m.CtrlPressed ? (byte)1 : (byte)0);
        });

        public static UseSkillNetMsg DecodeUseSkill(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new UseSkillNetMsg
            {
                SkillId = r.ReadInt32(),
                TargetObjectId = r.ReadInt32(),
                CtrlPressed = r.ReadByte() != 0
            };
        }

        public static byte[] EncodeMagicSkillUse(MagicSkillUseNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.CasterObjectId);
            w.WriteInt32(m.TargetObjectId);
            w.WriteInt32(m.SkillId);
            w.WriteInt32(m.SkillLevel);
            w.WriteInt32(m.HitTimeMs);
            w.WriteInt32(m.ReuseDelayMs);
        });

        public static MagicSkillUseNetMsg DecodeMagicSkillUse(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new MagicSkillUseNetMsg
            {
                CasterObjectId = r.ReadInt32(),
                TargetObjectId = r.ReadInt32(),
                SkillId = r.ReadInt32(),
                SkillLevel = r.ReadInt32(),
                HitTimeMs = r.ReadInt32(),
                ReuseDelayMs = r.ReadInt32()
            };
        }

        public static byte[] EncodeReviveRequest(ReviveRequestNetMsg m) => Write(w => w.WriteInt32(m.Action));
        public static ReviveRequestNetMsg DecodeReviveRequest(ReadOnlySpan<byte> p) =>
            new() { Action = new Reader(p).ReadInt32() };

        public static byte[] EncodeRevive(ReviveNetMsg m) => Write(w => w.WriteInt32(m.ObjectId));
        public static ReviveNetMsg DecodeRevive(ReadOnlySpan<byte> p) =>
            new() { ObjectId = new Reader(p).ReadInt32() };

        public static byte[] EncodeTeleportToLocation(TeleportToLocationNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ObjectId);
            w.WriteInt32(m.X);
            w.WriteInt32(m.Y);
            w.WriteInt32(m.Z);
            w.WriteInt32(m.Heading);
        });

        public static TeleportToLocationNetMsg DecodeTeleportToLocation(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new TeleportToLocationNetMsg
            {
                ObjectId = r.ReadInt32(),
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Z = r.ReadInt32(),
                Heading = r.ReadInt32()
            };
        }

        public static byte[] EncodeAbnormalStatusUpdate(AbnormalStatusUpdateNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ObjectId);
            var effects = m.Effects ?? Array.Empty<AbnormalEffectNet>();
            w.WriteInt32(effects.Length);
            for (int i = 0; i < effects.Length; i++)
            {
                var e = effects[i] ?? new AbnormalEffectNet();
                w.WriteInt32(e.Id);
                w.WriteInt32(e.Value);
                w.WriteInt32(e.DurationSeconds);
            }
        });

        public static AbnormalStatusUpdateNetMsg DecodeAbnormalStatusUpdate(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int objectId = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0)
                n = 0;
            var effects = new AbnormalEffectNet[n];
            for (int i = 0; i < n; i++)
            {
                effects[i] = new AbnormalEffectNet
                {
                    Id = r.ReadInt32(),
                    Value = r.ReadInt32(),
                    DurationSeconds = r.ReadInt32()
                };
            }

            return new AbnormalStatusUpdateNetMsg { ObjectId = objectId, Effects = effects };
        }

        public static byte[] EncodeNpcTalk(NpcTalkNetMsg m) => Write(w => w.WriteInt32(m.NpcObjectId));
        public static NpcTalkNetMsg DecodeNpcTalk(ReadOnlySpan<byte> p) =>
            new() { NpcObjectId = new Reader(p).ReadInt32() };

        public static byte[] EncodeBypassRequest(BypassRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.NpcObjectId);
            w.WriteString(m.Command ?? "");
        });

        public static BypassRequestNetMsg DecodeBypassRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new BypassRequestNetMsg { NpcObjectId = r.ReadInt32(), Command = r.ReadString() };
        }

        public static byte[] EncodeNpcHtml(NpcHtmlNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.NpcObjectId);
            w.WriteInt32(m.NpcId);
            w.WriteString(m.Html ?? "");
            w.WriteInt32(m.ItemId);
        });

        public static NpcHtmlNetMsg DecodeNpcHtml(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new NpcHtmlNetMsg
            {
                NpcObjectId = r.ReadInt32(),
                NpcId = r.ReadInt32(),
                Html = r.ReadString(),
                ItemId = r.ReadInt32()
            };
        }

    }
}
