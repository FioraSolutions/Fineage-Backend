using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Fineage.Net.Contract
{
    /// <summary>
    /// BinaryPrimitives codec for Net payloads (bootstrap). Cold MemoryPack path can replace bodies later
    /// without changing PacketEnvelope framing.
    /// </summary>
    public static partial class NetCodec
    {
        static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        public static byte[] EncodeHello(HelloMsg m) => Write(w =>
        {
            w.WriteByte(m.Major);
            w.WriteByte(m.Minor);
            w.WriteString(m.ClientName);
            w.WriteUInt32(m.FeatureFlags);
        });

        public static HelloMsg DecodeHello(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new HelloMsg
            {
                Major = r.ReadByte(),
                Minor = r.ReadByte(),
                ClientName = r.ReadString(),
                FeatureFlags = r.ReadUInt32()
            };
        }

        public static byte[] EncodeHelloAck(HelloAckMsg m) => Write(w =>
        {
            w.WriteByte(m.Major);
            w.WriteByte(m.Minor);
            w.WriteUInt32(m.FeatureFlags);
            w.WriteString(m.ServerName);
        });

        public static HelloAckMsg DecodeHelloAck(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new HelloAckMsg
            {
                Major = r.ReadByte(),
                Minor = r.ReadByte(),
                FeatureFlags = r.ReadUInt32(),
                ServerName = r.ReadString()
            };
        }

        public static byte[] EncodeReject(RejectMsg m) => Write(w =>
        {
            w.WriteString(m.Code);
            w.WriteString(m.Message);
        });

        public static RejectMsg DecodeReject(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new RejectMsg { Code = r.ReadString(), Message = r.ReadString() };
        }

        public static byte[] EncodeAuth(AuthMsg m) => Write(w =>
        {
            w.WriteString(m.Account);
            w.WriteString(m.Password);
            w.WriteByte(m.RegisterIfMissing ? (byte)1 : (byte)0);
        });

        public static AuthMsg DecodeAuth(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new AuthMsg
            {
                Account = r.ReadString(),
                Password = r.ReadString(),
                RegisterIfMissing = r.ReadByte() != 0
            };
        }

        public static byte[] EncodeAuthResult(AuthResultMsg m) => Write(w =>
        {
            w.WriteByte(m.Ok ? (byte)1 : (byte)0);
            w.WriteString(m.Code);
            w.WriteString(m.Message);
            w.WriteString(m.Account);
        });

        public static AuthResultMsg DecodeAuthResult(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new AuthResultMsg
            {
                Ok = r.ReadByte() != 0,
                Code = r.ReadString(),
                Message = r.ReadString(),
                Account = r.ReadString()
            };
        }

        public static byte[] EncodeListCharacters(ListCharactersMsg _) => Array.Empty<byte>();

        public static ListCharactersMsg DecodeListCharacters(ReadOnlySpan<byte> _) => new();

        public static byte[] EncodeCharacterRoster(CharacterRosterMsg m) => Write(w =>
        {
            w.WriteUInt16((ushort)m.Characters.Length);
            foreach (var c in m.Characters)
                WriteSummary(w, c);
        });

        public static CharacterRosterMsg DecodeCharacterRoster(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int n = r.ReadUInt16();
            var rows = new CharacterSummaryNet[n];
            for (int i = 0; i < n; i++)
                rows[i] = ReadSummary(ref r);
            return new CharacterRosterMsg { Characters = rows };
        }

        public static byte[] EncodeCreateCharacter(CreateCharacterMsg m) => Write(w =>
        {
            w.WriteString(m.Name);
            w.WriteInt32(m.ClassId);
            w.WriteInt32(m.Race);
            w.WriteInt32(m.Sex);
            w.WriteInt32(m.HairStyle);
            w.WriteInt32(m.HairColor);
            w.WriteInt32(m.Face);
        });

        public static CreateCharacterMsg DecodeCreateCharacter(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new CreateCharacterMsg
            {
                Name = r.ReadString(),
                ClassId = r.ReadInt32(),
                Race = r.ReadInt32(),
                Sex = r.ReadInt32(),
                HairStyle = r.ReadInt32(),
                HairColor = r.ReadInt32(),
                Face = r.ReadInt32()
            };
        }

        public static byte[] EncodeCreateResult(CreateResultMsg m) => Write(w =>
        {
            w.WriteByte(m.Ok ? (byte)1 : (byte)0);
            w.WriteString(m.Code);
            w.WriteString(m.Message);
            w.WriteByte(m.Character != null ? (byte)1 : (byte)0);
            if (m.Character != null)
                WriteSummary(w, m.Character);
        });

        public static CreateResultMsg DecodeCreateResult(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            var msg = new CreateResultMsg
            {
                Ok = r.ReadByte() != 0,
                Code = r.ReadString(),
                Message = r.ReadString()
            };
            if (r.ReadByte() != 0)
                msg.Character = ReadSummary(ref r);
            return msg;
        }

        public static byte[] EncodeSelectCharacter(SelectCharacterMsg m) => Write(w =>
        {
            w.WriteInt32(m.CharacterId);
            w.WriteInt32(m.Slot);
            w.WriteString(m.Name);
        });

        public static SelectCharacterMsg DecodeSelectCharacter(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new SelectCharacterMsg
            {
                CharacterId = r.ReadInt32(),
                Slot = r.ReadInt32(),
                Name = r.ReadString()
            };
        }

        public static byte[] EncodeDeleteCharacter(DeleteCharacterMsg m) => Write(w =>
        {
            w.WriteInt32(m.Slot);
            w.WriteInt32(m.CharacterId);
        });

        public static DeleteCharacterMsg DecodeDeleteCharacter(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new DeleteCharacterMsg { Slot = r.ReadInt32(), CharacterId = r.ReadInt32() };
        }

        public static byte[] EncodeRestoreCharacter(RestoreCharacterMsg m) => Write(w =>
        {
            w.WriteInt32(m.Slot);
            w.WriteInt32(m.CharacterId);
        });

        public static RestoreCharacterMsg DecodeRestoreCharacter(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new RestoreCharacterMsg { Slot = r.ReadInt32(), CharacterId = r.ReadInt32() };
        }

        public static byte[] EncodeDeleteResult(DeleteResultMsg m) => Write(w =>
        {
            w.WriteByte(m.Ok ? (byte)1 : (byte)0);
            w.WriteString(m.Code);
            w.WriteString(m.Message);
            w.WriteInt32(m.Slot);
            w.WriteInt32(m.CharacterId);
        });

        public static DeleteResultMsg DecodeDeleteResult(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new DeleteResultMsg
            {
                Ok = r.ReadByte() != 0,
                Code = r.ReadString(),
                Message = r.ReadString(),
                Slot = r.ReadInt32(),
                CharacterId = r.ReadInt32()
            };
        }

        public static byte[] EncodeHandoff(HandoffMsg m) => Write(w =>
        {
            w.WriteString(m.Account);
            w.WriteInt32(m.CharacterId);
            w.WriteString(m.CharacterName);
            w.WriteInt32(m.ClassId);
            w.WriteInt32(m.Race);
            w.WriteInt32(m.Sex);
            w.WriteString(m.Token);
            w.WriteInt64(m.ExpiresUnixSeconds);
            w.WriteString(m.ZoneHost);
            w.WriteInt32(m.ZonePort);
        });

        public static HandoffMsg DecodeHandoff(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new HandoffMsg
            {
                Account = r.ReadString(),
                CharacterId = r.ReadInt32(),
                CharacterName = r.ReadString(),
                ClassId = r.ReadInt32(),
                Race = r.ReadInt32(),
                Sex = r.ReadInt32(),
                Token = r.ReadString(),
                ExpiresUnixSeconds = r.ReadInt64(),
                ZoneHost = r.ReadString(),
                ZonePort = r.ReadInt32()
            };
        }

        public static byte[] EncodeZoneAuth(ZoneAuthMsg m) => Write(w =>
        {
            w.WriteString(m.Account);
            w.WriteString(m.Token);
        });

        public static ZoneAuthMsg DecodeZoneAuth(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new ZoneAuthMsg { Account = r.ReadString(), Token = r.ReadString() };
        }

        public static byte[] EncodeZoneAuthResult(ZoneAuthResultMsg m) => Write(w =>
        {
            w.WriteByte(m.Ok ? (byte)1 : (byte)0);
            w.WriteString(m.Code);
            w.WriteString(m.Message);
            w.WriteUInt64(m.EntityValue);
            w.WriteUInt32(m.EntityGeneration);
        });

        public static ZoneAuthResultMsg DecodeZoneAuthResult(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new ZoneAuthResultMsg
            {
                Ok = r.ReadByte() != 0,
                Code = r.ReadString(),
                Message = r.ReadString(),
                EntityValue = r.ReadUInt64(),
                EntityGeneration = r.ReadUInt32()
            };
        }

        public static byte[] EncodeEnterWorld(EnterWorldMsg _) => Array.Empty<byte>();
        public static EnterWorldMsg DecodeEnterWorld(ReadOnlySpan<byte> _) => new();

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
                ClanId = r.ReadInt32()
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



        sealed class Writer
        {
            byte[] _buf = new byte[256];
            int _len;

            public void WriteByte(byte v)
            {
                Ensure(1);
                _buf[_len++] = v;
            }

            public void WriteUInt16(ushort v)
            {
                Ensure(2);
                BinaryPrimitives.WriteUInt16LittleEndian(_buf.AsSpan(_len), v);
                _len += 2;
            }

            public void WriteInt32(int v)
            {
                Ensure(4);
                BinaryPrimitives.WriteInt32LittleEndian(_buf.AsSpan(_len), v);
                _len += 4;
            }

            public void WriteUInt32(uint v)
            {
                Ensure(4);
                BinaryPrimitives.WriteUInt32LittleEndian(_buf.AsSpan(_len), v);
                _len += 4;
            }

            public void WriteInt64(long v)
            {
                Ensure(8);
                BinaryPrimitives.WriteInt64LittleEndian(_buf.AsSpan(_len), v);
                _len += 8;
            }

            public void WriteUInt64(ulong v)
            {
                Ensure(8);
                BinaryPrimitives.WriteUInt64LittleEndian(_buf.AsSpan(_len), v);
                _len += 8;
            }

            public void WriteFloat(float v)
            {
                Ensure(4);
                BinaryPrimitives.WriteInt32LittleEndian(_buf.AsSpan(_len), BitConverter.SingleToInt32Bits(v));
                _len += 4;
            }

            public void WriteString(string? s)
            {
                s ??= "";
                byte[] bytes = Utf8.GetBytes(s);
                // Length is UInt16 on the wire — allow full range (NpcHtml / admin pages exceed 2 KiB).
                if (bytes.Length > ushort.MaxValue)
                    throw new InvalidOperationException("string too long");
                WriteUInt16((ushort)bytes.Length);
                Ensure(bytes.Length);
                bytes.CopyTo(_buf.AsSpan(_len));
                _len += bytes.Length;
            }

            public void WriteInt32Array(int[]? values)
            {
                values ??= Array.Empty<int>();
                if (values.Length > 256)
                    throw new InvalidOperationException("int array too long");
                WriteUInt16((ushort)values.Length);
                for (int i = 0; i < values.Length; i++)
                    WriteInt32(values[i]);
            }

            public byte[] ToArray()
            {
                var result = new byte[_len];
                Buffer.BlockCopy(_buf, 0, result, 0, _len);
                return result;
            }

            void Ensure(int n)
            {
                if (_len + n <= _buf.Length)
                    return;
                int size = _buf.Length;
                while (size < _len + n)
                    size *= 2;
                Array.Resize(ref _buf, size);
            }
        }

        ref struct Reader
        {
            ReadOnlySpan<byte> _span;
            int _i;

            public Reader(ReadOnlySpan<byte> span)
            {
                _span = span;
                _i = 0;
            }

            public int Remaining => _span.Length - _i;

            public byte ReadByte()
            {
                if (_i >= _span.Length)
                    throw new InvalidOperationException("truncated");
                return _span[_i++];
            }

            public ushort ReadUInt16()
            {
                if (_i + 2 > _span.Length)
                    throw new InvalidOperationException("truncated");
                ushort v = BinaryPrimitives.ReadUInt16LittleEndian(_span.Slice(_i));
                _i += 2;
                return v;
            }

            public int ReadInt32()
            {
                if (_i + 4 > _span.Length)
                    throw new InvalidOperationException("truncated");
                int v = BinaryPrimitives.ReadInt32LittleEndian(_span.Slice(_i));
                _i += 4;
                return v;
            }

            public uint ReadUInt32()
            {
                if (_i + 4 > _span.Length)
                    throw new InvalidOperationException("truncated");
                uint v = BinaryPrimitives.ReadUInt32LittleEndian(_span.Slice(_i));
                _i += 4;
                return v;
            }

            public long ReadInt64()
            {
                if (_i + 8 > _span.Length)
                    throw new InvalidOperationException("truncated");
                long v = BinaryPrimitives.ReadInt64LittleEndian(_span.Slice(_i));
                _i += 8;
                return v;
            }

            public ulong ReadUInt64()
            {
                if (_i + 8 > _span.Length)
                    throw new InvalidOperationException("truncated");
                ulong v = BinaryPrimitives.ReadUInt64LittleEndian(_span.Slice(_i));
                _i += 8;
                return v;
            }

            public float ReadFloat()
            {
                int bits = ReadInt32();
                return BitConverter.Int32BitsToSingle(bits);
            }

            public string ReadString()
            {
                ushort len = ReadUInt16();
                if (_i + len > _span.Length)
                    throw new InvalidOperationException("truncated");
                string s = Utf8.GetString(_span.Slice(_i, len));
                _i += len;
                return s;
            }

            public int[] ReadInt32Array()
            {
                ushort len = ReadUInt16();
                if (len > 256)
                    throw new InvalidOperationException("int array too long");
                var values = new int[len];
                for (int i = 0; i < len; i++)
                    values[i] = ReadInt32();
                return values;
            }
        }
    }
}
