using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
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
    }
}
