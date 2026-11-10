using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
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
                w.WriteString(e.Slot ?? "");
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
                    DurationSeconds = r.ReadInt32(),
                    Slot = r.ReadString()
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
