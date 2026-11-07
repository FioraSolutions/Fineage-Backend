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
    public static class NetCodec
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

        public static byte[] EncodeClanCreateRequest(ClanCreateRequestNetMsg m) => Write(w => w.WriteString(m.Name ?? ""));

        public static ClanCreateRequestNetMsg DecodeClanCreateRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new ClanCreateRequestNetMsg { Name = r.ReadString() };
        }

        public static byte[] EncodeClanInviteRequest(ClanInviteRequestNetMsg m) => Write(w => w.WriteInt32(m.TargetObjectId));

        public static ClanInviteRequestNetMsg DecodeClanInviteRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new ClanInviteRequestNetMsg { TargetObjectId = r.ReadInt32() };
        }

        public static byte[] EncodeClanAnswer(ClanAnswerNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.RequestorObjectId);
            w.WriteInt32(m.Response);
        });

        public static ClanAnswerNetMsg DecodeClanAnswer(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new ClanAnswerNetMsg { RequestorObjectId = r.ReadInt32(), Response = r.ReadInt32() };
        }

        public static byte[] EncodeClanKickRequest(ClanKickRequestNetMsg m) => Write(w => w.WriteInt32(m.TargetObjectId));

        public static ClanKickRequestNetMsg DecodeClanKickRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new ClanKickRequestNetMsg { TargetObjectId = r.ReadInt32() };
        }

        public static byte[] EncodePledgeStatus(PledgeStatusNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ClanId);
            w.WriteString(m.Name ?? "");
            w.WriteInt32(m.Level);
            w.WriteInt32(m.LeaderObjectId);
            w.WriteInt32(m.CrestId);
            w.WriteInt32(m.MemberCount);
            w.WriteInt64(m.DissolvingExpiryUnixMs);
            w.WriteInt64(m.CharPenaltyExpiryUnixMs);
        });

        public static PledgeStatusNetMsg DecodePledgeStatus(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PledgeStatusNetMsg
            {
                ClanId = r.ReadInt32(),
                Name = r.ReadString(),
                Level = r.ReadInt32(),
                LeaderObjectId = r.ReadInt32(),
                CrestId = r.ReadInt32(),
                MemberCount = r.ReadInt32(),
                DissolvingExpiryUnixMs = r.ReadInt64(),
                CharPenaltyExpiryUnixMs = r.ReadInt64()
            };
        }

        public static byte[] EncodePledgeMemberList(PledgeMemberListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ClanId);
            w.WriteInt32(m.LeaderObjectId);
            PledgeMemberNet[] rows = m.Members ?? System.Array.Empty<PledgeMemberNet>();
            w.WriteInt32(rows.Length);
            foreach (PledgeMemberNet row in rows)
            {
                w.WriteInt32(row.ObjectId);
                w.WriteString(row.Name ?? "");
                w.WriteInt32(row.ClassId);
                w.WriteInt32(row.Level);
                w.WriteByte(row.Online ? (byte)1 : (byte)0);
                w.WriteInt32(row.PledgeType);
            }
        });

        public static PledgeMemberListNetMsg DecodePledgeMemberList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int clanId = r.ReadInt32();
            int leader = r.ReadInt32();
            int n = r.ReadInt32();
            var rows = new PledgeMemberNet[n];
            for (int i = 0; i < n; i++)
            {
                rows[i] = new PledgeMemberNet
                {
                    ObjectId = r.ReadInt32(),
                    Name = r.ReadString(),
                    ClassId = r.ReadInt32(),
                    Level = r.ReadInt32(),
                    Online = r.ReadByte() != 0,
                    PledgeType = r.ReadInt32()
                };
            }

            return new PledgeMemberListNetMsg { ClanId = clanId, LeaderObjectId = leader, Members = rows };
        }

        public static byte[] EncodeClanAskJoin(ClanAskJoinNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.RequestorObjectId);
            w.WriteString(m.RequestorName ?? "");
            w.WriteInt32(m.ClanId);
            w.WriteString(m.ClanName ?? "");
            w.WriteInt32(m.PledgeType);
        });

        public static ClanAskJoinNetMsg DecodeClanAskJoin(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new ClanAskJoinNetMsg
            {
                RequestorObjectId = r.ReadInt32(),
                RequestorName = r.ReadString(),
                ClanId = r.ReadInt32(),
                ClanName = r.ReadString(),
                PledgeType = r.ReadInt32()
            };
        }

        public static byte[] EncodeClanJoinResult(ClanJoinResultNetMsg m) => Write(w => w.WriteInt32(m.Response));

        public static ClanJoinResultNetMsg DecodeClanJoinResult(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new ClanJoinResultNetMsg { Response = r.ReadInt32() };
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
                    EquippedSlot = r.ReadInt32()
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

        public static byte[] EncodeBuyList(BuyListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.NpcObjectId);
            w.WriteInt32(m.ListId);
            w.WriteInt32(m.BaseTaxPercent);
            w.WriteInt32(m.CastleTaxPercent);
            var items = m.Items ?? Array.Empty<BuyListItemNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new BuyListItemNet();
                w.WriteInt32(row.ItemId);
                w.WriteInt32(row.Price);
                w.WriteInt32(row.Count);
            }
        });

        public static BuyListNetMsg DecodeBuyList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int npc = r.ReadInt32();
            int listId = r.ReadInt32();
            int baseTax = r.ReadInt32();
            int castleTax = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var items = new BuyListItemNet[n];
            for (int i = 0; i < n; i++)
                items[i] = new BuyListItemNet { ItemId = r.ReadInt32(), Price = r.ReadInt32(), Count = r.ReadInt32() };
            return new BuyListNetMsg { NpcObjectId = npc, ListId = listId, BaseTaxPercent = baseTax, CastleTaxPercent = castleTax, Items = items };
        }

        public static byte[] EncodeSellList(SellListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.NpcObjectId);
            var items = m.Items ?? Array.Empty<SellListItemNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new SellListItemNet();
                w.WriteInt32(row.ObjectId);
                w.WriteInt32(row.ItemId);
                w.WriteInt32(row.Count);
                w.WriteInt32(row.Price);
                w.WriteInt32(row.EnchantLevel);
            }
        });

        public static SellListNetMsg DecodeSellList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int npc = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var items = new SellListItemNet[n];
            for (int i = 0; i < n; i++)
                items[i] = new SellListItemNet
                {
                    ObjectId = r.ReadInt32(),
                    ItemId = r.ReadInt32(),
                    Count = r.ReadInt32(),
                    Price = r.ReadInt32(),
                    EnchantLevel = r.ReadInt32()
                };
            return new SellListNetMsg { NpcObjectId = npc, Items = items };
        }

        public static byte[] EncodeMultiSellList(MultiSellListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.NpcObjectId);
            w.WriteInt32(m.ListId);
            w.WriteByte(m.MaintainEnchantment ? (byte)1 : (byte)0);
            w.WriteByte(m.InventoryOnly ? (byte)1 : (byte)0);
            w.WriteInt32(m.CastleTaxPercent);
            var entries = m.Entries ?? Array.Empty<MultiSellEntryNet>();
            w.WriteInt32(entries.Length);
            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i] ?? new MultiSellEntryNet();
                w.WriteInt32(e.EntryId);
                WriteParts(w, e.Ingredients);
                WriteParts(w, e.Products);
            }
        });

        static void WriteParts(Writer w, MultiSellPartNet[]? parts)
        {
            parts ??= Array.Empty<MultiSellPartNet>();
            w.WriteInt32(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i] ?? new MultiSellPartNet();
                w.WriteInt32(p.ItemId);
                w.WriteInt32(p.Count);
                w.WriteInt32(p.EnchantLevel);
            }
        }

        static MultiSellPartNet[] ReadParts(ref Reader r)
        {
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var parts = new MultiSellPartNet[n];
            for (int i = 0; i < n; i++)
                parts[i] = new MultiSellPartNet { ItemId = r.ReadInt32(), Count = r.ReadInt32(), EnchantLevel = r.ReadInt32() };
            return parts;
        }

        public static MultiSellListNetMsg DecodeMultiSellList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int npc = r.ReadInt32();
            int listId = r.ReadInt32();
            bool maintain = r.ReadByte() != 0;
            bool invOnly = r.ReadByte() != 0;
            int tax = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var entries = new MultiSellEntryNet[n];
            for (int i = 0; i < n; i++)
            {
                entries[i] = new MultiSellEntryNet
                {
                    EntryId = r.ReadInt32(),
                    Ingredients = ReadParts(ref r),
                    Products = ReadParts(ref r)
                };
            }
            return new MultiSellListNetMsg
            {
                NpcObjectId = npc,
                ListId = listId,
                MaintainEnchantment = maintain,
                InventoryOnly = invOnly,
                CastleTaxPercent = tax,
                Entries = entries
            };
        }

        public static byte[] EncodeShopBalance(ShopBalanceNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Adena);
            w.WriteInt32(m.Weight);
            w.WriteInt32(m.MaxWeight);
            w.WriteInt32(m.Slots);
            w.WriteInt32(m.MaxSlots);
            w.WriteInt32(m.BaseTaxPercent);
            w.WriteInt32(m.CastleTaxPercent);
        });

        public static ShopBalanceNetMsg DecodeShopBalance(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new ShopBalanceNetMsg
            {
                Adena = r.ReadInt32(),
                Weight = r.ReadInt32(),
                MaxWeight = r.ReadInt32(),
                Slots = r.ReadInt32(),
                MaxSlots = r.ReadInt32(),
                BaseTaxPercent = r.ReadInt32(),
                CastleTaxPercent = r.ReadInt32()
            };
        }

        public static byte[] EncodeRequestBuyItem(RequestBuyItemNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.NpcObjectId);
            w.WriteInt32(m.ListId);
            var items = m.Items ?? Array.Empty<ShopCountNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new ShopCountNet();
                w.WriteInt32(row.ItemId);
                w.WriteInt32(row.Count);
            }
        });

        public static RequestBuyItemNetMsg DecodeRequestBuyItem(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int npc = r.ReadInt32();
            int listId = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var items = new ShopCountNet[n];
            for (int i = 0; i < n; i++)
                items[i] = new ShopCountNet { ItemId = r.ReadInt32(), Count = r.ReadInt32() };
            return new RequestBuyItemNetMsg { NpcObjectId = npc, ListId = listId, Items = items };
        }

        public static byte[] EncodeRequestSellItem(RequestSellItemNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.NpcObjectId);
            var items = m.Items ?? Array.Empty<ShopObjectCountNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new ShopObjectCountNet();
                w.WriteInt32(row.ObjectId);
                w.WriteInt32(row.Count);
            }
        });

        public static RequestSellItemNetMsg DecodeRequestSellItem(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int npc = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var items = new ShopObjectCountNet[n];
            for (int i = 0; i < n; i++)
                items[i] = new ShopObjectCountNet { ObjectId = r.ReadInt32(), Count = r.ReadInt32() };
            return new RequestSellItemNetMsg { NpcObjectId = npc, Items = items };
        }

        public static byte[] EncodeRequestMultiSellChoose(RequestMultiSellChooseNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.NpcObjectId);
            w.WriteInt32(m.ListId);
            w.WriteInt32(m.EntryId);
            w.WriteInt32(m.Amount);
        });

        public static RequestMultiSellChooseNetMsg DecodeRequestMultiSellChoose(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new RequestMultiSellChooseNetMsg
            {
                NpcObjectId = r.ReadInt32(),
                ListId = r.ReadInt32(),
                EntryId = r.ReadInt32(),
                Amount = r.ReadInt32()
            };
        }

        static void WriteItemListEntries(Writer w, ItemListEntryNet[]? items)
        {
            items ??= Array.Empty<ItemListEntryNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new ItemListEntryNet();
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
        }

        static ItemListEntryNet[] ReadItemListEntries(ref Reader r)
        {
            int n = r.ReadInt32();
            if (n < 0) n = 0;
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
            return rows;
        }

        public static byte[] EncodeWarehouseDepositList(WarehouseDepositListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.NpcObjectId);
            w.WriteInt32(m.FeePerLine);
            w.WriteInt32(m.SlotsUsed);
            w.WriteInt32(m.SlotsMax);
            w.WriteInt32(m.Adena);
            WriteItemListEntries(w, m.Items);
        });

        public static WarehouseDepositListNetMsg DecodeWarehouseDepositList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new WarehouseDepositListNetMsg
            {
                NpcObjectId = r.ReadInt32(),
                FeePerLine = r.ReadInt32(),
                SlotsUsed = r.ReadInt32(),
                SlotsMax = r.ReadInt32(),
                Adena = r.ReadInt32(),
                Items = ReadItemListEntries(ref r)
            };
        }

        public static byte[] EncodeWarehouseWithdrawList(WarehouseWithdrawListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.NpcObjectId);
            w.WriteInt32(m.SlotsUsed);
            w.WriteInt32(m.SlotsMax);
            w.WriteInt32(m.InventorySlotsUsed);
            w.WriteInt32(m.InventorySlotsMax);
            w.WriteInt32(m.CurrentWeight);
            w.WriteInt32(m.MaxWeight);
            WriteItemListEntries(w, m.Items);
        });

        public static WarehouseWithdrawListNetMsg DecodeWarehouseWithdrawList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new WarehouseWithdrawListNetMsg
            {
                NpcObjectId = r.ReadInt32(),
                SlotsUsed = r.ReadInt32(),
                SlotsMax = r.ReadInt32(),
                InventorySlotsUsed = r.ReadInt32(),
                InventorySlotsMax = r.ReadInt32(),
                CurrentWeight = r.ReadInt32(),
                MaxWeight = r.ReadInt32(),
                Items = ReadItemListEntries(ref r)
            };
        }

        public static byte[] EncodeWarehouseDepositRequest(WarehouseDepositRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.NpcObjectId);
            var lines = m.Lines ?? Array.Empty<ShopObjectCountNet>();
            w.WriteInt32(lines.Length);
            for (int i = 0; i < lines.Length; i++)
            {
                var row = lines[i] ?? new ShopObjectCountNet();
                w.WriteInt32(row.ObjectId);
                w.WriteInt32(row.Count);
            }
        });

        public static WarehouseDepositRequestNetMsg DecodeWarehouseDepositRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int npc = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var lines = new ShopObjectCountNet[n];
            for (int i = 0; i < n; i++)
                lines[i] = new ShopObjectCountNet { ObjectId = r.ReadInt32(), Count = r.ReadInt32() };
            return new WarehouseDepositRequestNetMsg { NpcObjectId = npc, Lines = lines };
        }

        public static byte[] EncodeWarehouseWithdrawRequest(WarehouseWithdrawRequestNetMsg m) =>
            EncodeWarehouseDepositRequest(new WarehouseDepositRequestNetMsg { NpcObjectId = m.NpcObjectId, Lines = m.Lines });

        public static WarehouseWithdrawRequestNetMsg DecodeWarehouseWithdrawRequest(ReadOnlySpan<byte> p)
        {
            var dep = DecodeWarehouseDepositRequest(p);
            return new WarehouseWithdrawRequestNetMsg { NpcObjectId = dep.NpcObjectId, Lines = dep.Lines };
        }

        public static byte[] EncodeRequestQuestList() => Array.Empty<byte>();
        public static void DecodeRequestQuestList(ReadOnlySpan<byte> _) { }

        public static byte[] EncodeRequestQuestAbort(RequestQuestAbortNetMsg m) => Write(w => w.WriteInt32(m.QuestId));
        public static RequestQuestAbortNetMsg DecodeRequestQuestAbort(ReadOnlySpan<byte> p) =>
            new() { QuestId = new Reader(p).ReadInt32() };

        public static byte[] EncodeQuestList(QuestListNetMsg m) => Write(w =>
        {
            var active = m.Active ?? Array.Empty<QuestJournalEntryNet>();
            w.WriteInt32(active.Length);
            for (int i = 0; i < active.Length; i++)
            {
                var row = active[i] ?? new QuestJournalEntryNet();
                w.WriteInt32(row.QuestId);
                w.WriteString(row.Name ?? "");
                w.WriteInt32(row.State);
                w.WriteInt32(row.Cond);
                w.WriteInt32(row.ProgressFlags);
                w.WriteInt32(row.JournalWord);
                w.WriteByte(row.Repeatable ? (byte)1 : (byte)0);
            }
            var mask = m.CompletedMask ?? Array.Empty<byte>();
            w.WriteInt32(mask.Length);
            for (int i = 0; i < mask.Length; i++)
                w.WriteByte(mask[i]);
            var done = m.CompletedQuestIds ?? Array.Empty<int>();
            w.WriteInt32(done.Length);
            for (int i = 0; i < done.Length; i++)
                w.WriteInt32(done[i]);
            w.WriteInt32(m.MaxActive);
        });

        public static QuestListNetMsg DecodeQuestList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var active = new QuestJournalEntryNet[n];
            for (int i = 0; i < n; i++)
            {
                active[i] = new QuestJournalEntryNet
                {
                    QuestId = r.ReadInt32(),
                    Name = r.ReadString(),
                    State = r.ReadInt32(),
                    Cond = r.ReadInt32(),
                    ProgressFlags = r.ReadInt32(),
                    JournalWord = r.ReadInt32(),
                    Repeatable = r.ReadByte() != 0
                };
            }
            int maskLen = r.ReadInt32();
            if (maskLen < 0) maskLen = 0;
            var mask = new byte[maskLen];
            for (int i = 0; i < maskLen; i++)
                mask[i] = r.ReadByte();
            int doneLen = r.ReadInt32();
            if (doneLen < 0) doneLen = 0;
            var done = new int[doneLen];
            for (int i = 0; i < doneLen; i++)
                done[i] = r.ReadInt32();
            return new QuestListNetMsg
            {
                Active = active,
                CompletedMask = mask,
                CompletedQuestIds = done,
                MaxActive = r.ReadInt32()
            };
        }

        public static byte[] EncodePartyInviteRequest(PartyInviteRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.TargetObjectId);
            w.WriteInt32(m.ItemDistribution);
        });
        public static PartyInviteRequestNetMsg DecodePartyInviteRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PartyInviteRequestNetMsg { TargetObjectId = r.ReadInt32(), ItemDistribution = r.ReadInt32() };
        }

        public static byte[] EncodePartyAnswerJoinRequest(PartyAnswerJoinRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.RequestorObjectId);
            w.WriteInt32(m.Response);
        });
        public static PartyAnswerJoinRequestNetMsg DecodePartyAnswerJoinRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PartyAnswerJoinRequestNetMsg { RequestorObjectId = r.ReadInt32(), Response = r.ReadInt32() };
        }

        public static byte[] EncodePartyLeaveRequest() => Array.Empty<byte>();
        public static void DecodePartyLeaveRequest(ReadOnlySpan<byte> _) { }

        public static byte[] EncodePartyKickRequest(PartyKickRequestNetMsg m) => Write(w => w.WriteInt32(m.TargetObjectId));
        public static PartyKickRequestNetMsg DecodePartyKickRequest(ReadOnlySpan<byte> p) =>
            new() { TargetObjectId = new Reader(p).ReadInt32() };

        public static byte[] EncodePartyAskJoin(PartyAskJoinNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.RequestorObjectId);
            w.WriteString(m.RequestorName ?? "");
            w.WriteInt32(m.ItemDistribution);
        });
        public static PartyAskJoinNetMsg DecodePartyAskJoin(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PartyAskJoinNetMsg
            {
                RequestorObjectId = r.ReadInt32(),
                RequestorName = r.ReadString(),
                ItemDistribution = r.ReadInt32()
            };
        }

        public static byte[] EncodePartyJoinResult(PartyJoinResultNetMsg m) => Write(w => w.WriteInt32(m.Response));
        public static PartyJoinResultNetMsg DecodePartyJoinResult(ReadOnlySpan<byte> p) =>
            new() { Response = new Reader(p).ReadInt32() };

        public static byte[] EncodePartyMemberList(PartyMemberListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.LeaderObjectId);
            w.WriteInt32(m.PartyId);
            var members = m.Members ?? Array.Empty<PartyMemberEntryNet>();
            w.WriteInt32(members.Length);
            for (int i = 0; i < members.Length; i++)
            {
                var row = members[i] ?? new PartyMemberEntryNet();
                w.WriteInt32(row.ObjectId);
                w.WriteString(row.Name ?? "");
                w.WriteInt32(row.ClassId);
                w.WriteInt32(row.Level);
                w.WriteInt32(row.CurHp);
                w.WriteInt32(row.MaxHp);
                w.WriteInt32(row.CurMp);
                w.WriteInt32(row.MaxMp);
            }
        });
        public static PartyMemberListNetMsg DecodePartyMemberList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int leader = r.ReadInt32();
            int partyId = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var members = new PartyMemberEntryNet[n];
            for (int i = 0; i < n; i++)
            {
                members[i] = new PartyMemberEntryNet
                {
                    ObjectId = r.ReadInt32(),
                    Name = r.ReadString(),
                    ClassId = r.ReadInt32(),
                    Level = r.ReadInt32(),
                    CurHp = r.ReadInt32(),
                    MaxHp = r.ReadInt32(),
                    CurMp = r.ReadInt32(),
                    MaxMp = r.ReadInt32()
                };
            }
            return new PartyMemberListNetMsg { LeaderObjectId = leader, PartyId = partyId, Members = members };
        }

        public static byte[] EncodeTradeRequest(TradeRequestNetMsg m) => Write(w => w.WriteInt32(m.TargetObjectId));
        public static TradeRequestNetMsg DecodeTradeRequest(ReadOnlySpan<byte> p) =>
            new() { TargetObjectId = new Reader(p).ReadInt32() };

        public static byte[] EncodeTradeAnswerRequest(TradeAnswerRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.RequestorObjectId);
            w.WriteInt32(m.Response);
        });
        public static TradeAnswerRequestNetMsg DecodeTradeAnswerRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new TradeAnswerRequestNetMsg { RequestorObjectId = r.ReadInt32(), Response = r.ReadInt32() };
        }

        public static byte[] EncodeTradeAddItemRequest(TradeAddItemRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.InventoryObjectId);
            w.WriteInt64(m.Count);
        });
        public static TradeAddItemRequestNetMsg DecodeTradeAddItemRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new TradeAddItemRequestNetMsg { InventoryObjectId = r.ReadInt32(), Count = r.ReadInt64() };
        }

        public static byte[] EncodeTradeDoneRequest(TradeDoneRequestNetMsg m) => Write(w => w.WriteInt32(m.Response));
        public static TradeDoneRequestNetMsg DecodeTradeDoneRequest(ReadOnlySpan<byte> p) =>
            new() { Response = new Reader(p).ReadInt32() };

        public static byte[] EncodeTradeAsk(TradeAskNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.RequestorObjectId);
            w.WriteString(m.RequestorName ?? "");
        });
        public static TradeAskNetMsg DecodeTradeAsk(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new TradeAskNetMsg { RequestorObjectId = r.ReadInt32(), RequestorName = r.ReadString() };
        }

        public static byte[] EncodeTradeRequestResult(TradeRequestResultNetMsg m) => Write(w => w.WriteInt32(m.Response));
        public static TradeRequestResultNetMsg DecodeTradeRequestResult(ReadOnlySpan<byte> p) =>
            new() { Response = new Reader(p).ReadInt32() };

        public static byte[] EncodeTradeStart(TradeStartNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.PartnerObjectId);
            w.WriteString(m.PartnerName ?? "");
        });
        public static TradeStartNetMsg DecodeTradeStart(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new TradeStartNetMsg { PartnerObjectId = r.ReadInt32(), PartnerName = r.ReadString() };
        }

        public static byte[] EncodeTradeOfferUpdate(TradeOfferUpdateNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.IsOwn);
            w.WriteInt32(m.InventoryObjectId);
            w.WriteInt32(m.ItemId);
            w.WriteInt64(m.Count);
            w.WriteInt32(m.EnchantLevel);
        });
        public static TradeOfferUpdateNetMsg DecodeTradeOfferUpdate(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new TradeOfferUpdateNetMsg
            {
                IsOwn = r.ReadInt32(),
                InventoryObjectId = r.ReadInt32(),
                ItemId = r.ReadInt32(),
                Count = r.ReadInt64(),
                EnchantLevel = r.ReadInt32()
            };
        }

        public static byte[] EncodeTradeConfirmState(TradeConfirmStateNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.SelfConfirmed);
            w.WriteInt32(m.PartnerConfirmed);
        });
        public static TradeConfirmStateNetMsg DecodeTradeConfirmState(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new TradeConfirmStateNetMsg { SelfConfirmed = r.ReadInt32(), PartnerConfirmed = r.ReadInt32() };
        }

        public static byte[] EncodeTradeFinished(TradeFinishedNetMsg m) => Write(w => w.WriteInt32(m.Response));
        public static TradeFinishedNetMsg DecodeTradeFinished(ReadOnlySpan<byte> p) =>
            new() { Response = new Reader(p).ReadInt32() };

        public static byte[] EncodeSetPrivateStoreSell(SetPrivateStoreSellNetMsg m) => Write(w =>
        {
            w.WriteString(m.Message ?? "");
            w.WriteInt32(m.Packaged);
            var items = m.Items ?? System.Array.Empty<PrivateStoreSellRowNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new PrivateStoreSellRowNet();
                w.WriteInt32(row.InventoryObjectId);
                w.WriteInt32(row.ItemId);
                w.WriteInt64(row.Count);
                w.WriteInt64(row.Price);
                w.WriteInt32(row.EnchantLevel);
            }
        });
        public static SetPrivateStoreSellNetMsg DecodeSetPrivateStoreSell(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            string message = r.ReadString();
            int packaged = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var items = new PrivateStoreSellRowNet[n];
            for (int i = 0; i < n; i++)
            {
                items[i] = new PrivateStoreSellRowNet
                {
                    InventoryObjectId = r.ReadInt32(),
                    ItemId = r.ReadInt32(),
                    Count = r.ReadInt64(),
                    Price = r.ReadInt64(),
                    EnchantLevel = r.ReadInt32()
                };
            }
            return new SetPrivateStoreSellNetMsg { Message = message, Packaged = packaged, Items = items };
        }

        public static byte[] EncodePrivateStoreListRequest(PrivateStoreListRequestNetMsg m) =>
            Write(w => w.WriteInt32(m.StoreObjectId));
        public static PrivateStoreListRequestNetMsg DecodePrivateStoreListRequest(ReadOnlySpan<byte> p) =>
            new() { StoreObjectId = new Reader(p).ReadInt32() };

        public static byte[] EncodePrivateStoreBuy(PrivateStoreBuyNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.StoreObjectId);
            var items = m.Items ?? System.Array.Empty<PrivateStoreSellRowNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new PrivateStoreSellRowNet();
                w.WriteInt32(row.InventoryObjectId);
                w.WriteInt64(row.Count);
                w.WriteInt64(row.Price);
            }
        });
        public static PrivateStoreBuyNetMsg DecodePrivateStoreBuy(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int storeId = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var items = new PrivateStoreSellRowNet[n];
            for (int i = 0; i < n; i++)
            {
                items[i] = new PrivateStoreSellRowNet
                {
                    InventoryObjectId = r.ReadInt32(),
                    Count = r.ReadInt64(),
                    Price = r.ReadInt64()
                };
            }
            return new PrivateStoreBuyNetMsg { StoreObjectId = storeId, Items = items };
        }

        public static byte[] EncodePrivateStoreInfo(PrivateStoreInfoNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ObjectId);
            w.WriteInt32(m.StoreType);
            w.WriteString(m.Message ?? "");
        });
        public static PrivateStoreInfoNetMsg DecodePrivateStoreInfo(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PrivateStoreInfoNetMsg
            {
                ObjectId = r.ReadInt32(),
                StoreType = r.ReadInt32(),
                Message = r.ReadString()
            };
        }

        public static byte[] EncodePrivateStoreListSell(PrivateStoreListSellNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.StoreObjectId);
            w.WriteString(m.StoreName ?? "");
            w.WriteString(m.Message ?? "");
            w.WriteInt32(m.Packaged);
            w.WriteInt64(m.BuyerAdena);
            var items = m.Items ?? System.Array.Empty<PrivateStoreSellRowNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new PrivateStoreSellRowNet();
                w.WriteInt32(row.InventoryObjectId);
                w.WriteInt32(row.ItemId);
                w.WriteInt64(row.Count);
                w.WriteInt64(row.Price);
                w.WriteInt32(row.EnchantLevel);
            }
        });
        public static PrivateStoreListSellNetMsg DecodePrivateStoreListSell(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int storeId = r.ReadInt32();
            string name = r.ReadString();
            string message = r.ReadString();
            int packaged = r.ReadInt32();
            long adena = r.ReadInt64();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var items = new PrivateStoreSellRowNet[n];
            for (int i = 0; i < n; i++)
            {
                items[i] = new PrivateStoreSellRowNet
                {
                    InventoryObjectId = r.ReadInt32(),
                    ItemId = r.ReadInt32(),
                    Count = r.ReadInt64(),
                    Price = r.ReadInt64(),
                    EnchantLevel = r.ReadInt32()
                };
            }
            return new PrivateStoreListSellNetMsg
            {
                StoreObjectId = storeId,
                StoreName = name,
                Message = message,
                Packaged = packaged,
                BuyerAdena = adena,
                Items = items
            };
        }

        public static byte[] EncodeSetPrivateStoreBuy(SetPrivateStoreBuyNetMsg m) => Write(w =>
        {
            w.WriteString(m.Message ?? "");
            var items = m.Items ?? System.Array.Empty<PrivateStoreBuyRowNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new PrivateStoreBuyRowNet();
                w.WriteInt32(row.ItemId);
                w.WriteInt64(row.Count);
                w.WriteInt64(row.Price);
            }
        });
        public static SetPrivateStoreBuyNetMsg DecodeSetPrivateStoreBuy(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            string message = r.ReadString();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var items = new PrivateStoreBuyRowNet[n];
            for (int i = 0; i < n; i++)
            {
                items[i] = new PrivateStoreBuyRowNet
                {
                    ItemId = r.ReadInt32(),
                    Count = r.ReadInt64(),
                    Price = r.ReadInt64()
                };
            }
            return new SetPrivateStoreBuyNetMsg { Message = message, Items = items };
        }

        public static byte[] EncodePrivateStoreSellTo(PrivateStoreSellToNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.StoreObjectId);
            var items = m.Items ?? System.Array.Empty<PrivateStoreSellRowNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new PrivateStoreSellRowNet();
                w.WriteInt32(row.InventoryObjectId);
                w.WriteInt32(row.ItemId);
                w.WriteInt64(row.Count);
                w.WriteInt64(row.Price);
            }
        });
        public static PrivateStoreSellToNetMsg DecodePrivateStoreSellTo(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int storeId = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var items = new PrivateStoreSellRowNet[n];
            for (int i = 0; i < n; i++)
            {
                items[i] = new PrivateStoreSellRowNet
                {
                    InventoryObjectId = r.ReadInt32(),
                    ItemId = r.ReadInt32(),
                    Count = r.ReadInt64(),
                    Price = r.ReadInt64()
                };
            }
            return new PrivateStoreSellToNetMsg { StoreObjectId = storeId, Items = items };
        }

        public static byte[] EncodePrivateStoreListBuy(PrivateStoreListBuyNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.StoreObjectId);
            w.WriteString(m.StoreName ?? "");
            w.WriteString(m.Message ?? "");
            w.WriteInt64(m.StoreAdena);
            var items = m.Items ?? System.Array.Empty<PrivateStoreBuyRowNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new PrivateStoreBuyRowNet();
                w.WriteInt32(row.ItemId);
                w.WriteInt64(row.Count);
                w.WriteInt64(row.Price);
            }
        });
        public static PrivateStoreListBuyNetMsg DecodePrivateStoreListBuy(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int storeId = r.ReadInt32();
            string name = r.ReadString();
            string message = r.ReadString();
            long adena = r.ReadInt64();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var items = new PrivateStoreBuyRowNet[n];
            for (int i = 0; i < n; i++)
            {
                items[i] = new PrivateStoreBuyRowNet
                {
                    ItemId = r.ReadInt32(),
                    Count = r.ReadInt64(),
                    Price = r.ReadInt64()
                };
            }
            return new PrivateStoreListBuyNetMsg
            {
                StoreObjectId = storeId,
                StoreName = name,
                Message = message,
                StoreAdena = adena,
                Items = items
            };
        }

        public static byte[] EncodeRecipeBookOpen(RecipeBookOpenNetMsg m) => Write(w => w.WriteInt32(m.Dwarven));
        public static RecipeBookOpenNetMsg DecodeRecipeBookOpen(ReadOnlySpan<byte> p) =>
            new() { Dwarven = new Reader(p).ReadInt32() };

        public static byte[] EncodeRecipeMakeSelf(RecipeMakeSelfNetMsg m) => Write(w => w.WriteInt32(m.RecipeId));
        public static RecipeMakeSelfNetMsg DecodeRecipeMakeSelf(ReadOnlySpan<byte> p) =>
            new() { RecipeId = new Reader(p).ReadInt32() };

        public static byte[] EncodeRecipeBookList(RecipeBookListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Dwarven);
            var ids = m.RecipeIds ?? System.Array.Empty<int>();
            w.WriteInt32(ids.Length);
            for (int i = 0; i < ids.Length; i++)
                w.WriteInt32(ids[i]);
        });
        public static RecipeBookListNetMsg DecodeRecipeBookList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int dwarven = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var ids = new int[n];
            for (int i = 0; i < n; i++)
                ids[i] = r.ReadInt32();
            return new RecipeBookListNetMsg { Dwarven = dwarven, RecipeIds = ids };
        }

        public static byte[] EncodeRecipeItemMakeInfo(RecipeItemMakeInfoNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.RecipeId);
            w.WriteInt32(m.Result);
            w.WriteInt32(m.CurrentMp);
            w.WriteInt32(m.MaxMp);
        });
        public static RecipeItemMakeInfoNetMsg DecodeRecipeItemMakeInfo(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new RecipeItemMakeInfoNetMsg
            {
                RecipeId = r.ReadInt32(),
                Result = r.ReadInt32(),
                CurrentMp = r.ReadInt32(),
                MaxMp = r.ReadInt32()
            };
        }

        public static byte[] EncodeSetRecipeShop(SetRecipeShopNetMsg m) => Write(w =>
        {
            w.WriteString(m.Message ?? "");
            var items = m.Items ?? System.Array.Empty<RecipeShopRowNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new RecipeShopRowNet();
                w.WriteInt32(row.RecipeId);
                w.WriteInt64(row.Price);
            }
        });
        public static SetRecipeShopNetMsg DecodeSetRecipeShop(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            string message = r.ReadString();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var items = new RecipeShopRowNet[n];
            for (int i = 0; i < n; i++)
            {
                items[i] = new RecipeShopRowNet
                {
                    RecipeId = r.ReadInt32(),
                    Price = r.ReadInt64()
                };
            }
            return new SetRecipeShopNetMsg { Message = message, Items = items };
        }

        public static byte[] EncodeRecipeShopMakeInfo(RecipeShopMakeInfoNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ManufacturerObjectId);
            w.WriteInt32(m.RecipeId);
        });
        public static RecipeShopMakeInfoNetMsg DecodeRecipeShopMakeInfo(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new RecipeShopMakeInfoNetMsg
            {
                ManufacturerObjectId = r.ReadInt32(),
                RecipeId = r.ReadInt32()
            };
        }

        public static byte[] EncodeRecipeShopMakeItem(RecipeShopMakeItemNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ManufacturerObjectId);
            w.WriteInt32(m.RecipeId);
        });
        public static RecipeShopMakeItemNetMsg DecodeRecipeShopMakeItem(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new RecipeShopMakeItemNetMsg
            {
                ManufacturerObjectId = r.ReadInt32(),
                RecipeId = r.ReadInt32()
            };
        }

        public static byte[] EncodeRecipeShopSellList(RecipeShopSellListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ManufacturerObjectId);
            w.WriteInt32(m.CurrentMp);
            w.WriteInt32(m.MaxMp);
            w.WriteInt64(m.BuyerAdena);
            var items = m.Items ?? System.Array.Empty<RecipeShopRowNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new RecipeShopRowNet();
                w.WriteInt32(row.RecipeId);
                w.WriteInt64(row.Price);
            }
        });
        public static RecipeShopSellListNetMsg DecodeRecipeShopSellList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int manufacturer = r.ReadInt32();
            int curMp = r.ReadInt32();
            int maxMp = r.ReadInt32();
            long adena = r.ReadInt64();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var items = new RecipeShopRowNet[n];
            for (int i = 0; i < n; i++)
            {
                items[i] = new RecipeShopRowNet
                {
                    RecipeId = r.ReadInt32(),
                    Price = r.ReadInt64()
                };
            }
            return new RecipeShopSellListNetMsg
            {
                ManufacturerObjectId = manufacturer,
                CurrentMp = curMp,
                MaxMp = maxMp,
                BuyerAdena = adena,
                Items = items
            };
        }

        public static byte[] EncodeRecipeShopItemInfo(RecipeShopItemInfoNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ManufacturerObjectId);
            w.WriteInt32(m.RecipeId);
            w.WriteInt32(m.CurrentMp);
            w.WriteInt32(m.MaxMp);
            w.WriteInt32(m.Result);
        });
        public static RecipeShopItemInfoNetMsg DecodeRecipeShopItemInfo(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new RecipeShopItemInfoNetMsg
            {
                ManufacturerObjectId = r.ReadInt32(),
                RecipeId = r.ReadInt32(),
                CurrentMp = r.ReadInt32(),
                MaxMp = r.ReadInt32(),
                Result = r.ReadInt32()
            };
        }

        public static byte[] EncodeFriendInvite(FriendInviteNetMsg m) =>
            Write(w => w.WriteString(m.Name ?? ""));
        public static FriendInviteNetMsg DecodeFriendInvite(ReadOnlySpan<byte> p) =>
            new() { Name = new Reader(p).ReadString() };

        public static byte[] EncodeFriendAnswer(FriendAnswerNetMsg m) =>
            Write(w => w.WriteInt32(m.Response));
        public static FriendAnswerNetMsg DecodeFriendAnswer(ReadOnlySpan<byte> p) =>
            new() { Response = new Reader(p).ReadInt32() };

        public static byte[] EncodeFriendDelete(FriendDeleteNetMsg m) =>
            Write(w => w.WriteString(m.Name ?? ""));
        public static FriendDeleteNetMsg DecodeFriendDelete(ReadOnlySpan<byte> p) =>
            new() { Name = new Reader(p).ReadString() };

        public static byte[] EncodeBlockRequest(BlockRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Type);
            w.WriteString(m.Name ?? "");
        });
        public static BlockRequestNetMsg DecodeBlockRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new BlockRequestNetMsg { Type = r.ReadInt32(), Name = r.ReadString() };
        }

        public static byte[] EncodeFriendInviteAsk(FriendInviteAskNetMsg m) =>
            Write(w => w.WriteString(m.RequestorName ?? ""));
        public static FriendInviteAskNetMsg DecodeFriendInviteAsk(ReadOnlySpan<byte> p) =>
            new() { RequestorName = new Reader(p).ReadString() };

        public static byte[] EncodeFriendList(FriendListNetMsg m) => Write(w =>
        {
            var rows = m.Friends ?? System.Array.Empty<FriendRowNet>();
            w.WriteInt32(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i] ?? new FriendRowNet();
                w.WriteInt32(row.ObjectId);
                w.WriteString(row.Name ?? "");
                w.WriteInt32(row.Online);
            }
        });
        public static FriendListNetMsg DecodeFriendList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var rows = new FriendRowNet[n];
            for (int i = 0; i < n; i++)
            {
                rows[i] = new FriendRowNet
                {
                    ObjectId = r.ReadInt32(),
                    Name = r.ReadString(),
                    Online = r.ReadInt32()
                };
            }
            return new FriendListNetMsg { Friends = rows };
        }

        public static byte[] EncodeFriendUpdate(FriendUpdateNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Added);
            w.WriteInt32(m.ObjectId);
            w.WriteString(m.Name ?? "");
            w.WriteInt32(m.Online);
        });
        public static FriendUpdateNetMsg DecodeFriendUpdate(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new FriendUpdateNetMsg
            {
                Added = r.ReadInt32(),
                ObjectId = r.ReadInt32(),
                Name = r.ReadString(),
                Online = r.ReadInt32()
            };
        }

        public static byte[] EncodeBlockList(BlockListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.BlockAll);
            var rows = m.Blocked ?? System.Array.Empty<FriendRowNet>();
            w.WriteInt32(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i] ?? new FriendRowNet();
                w.WriteInt32(row.ObjectId);
                w.WriteString(row.Name ?? "");
                w.WriteInt32(row.Online);
            }
        });
        public static BlockListNetMsg DecodeBlockList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int blockAll = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var rows = new FriendRowNet[n];
            for (int i = 0; i < n; i++)
            {
                rows[i] = new FriendRowNet
                {
                    ObjectId = r.ReadInt32(),
                    Name = r.ReadString(),
                    Online = r.ReadInt32()
                };
            }
            return new BlockListNetMsg { BlockAll = blockAll, Blocked = rows };
        }

        public static byte[] EncodeBossStatusRequest(BossStatusRequestNetMsg m) => Write(w => w.WriteInt32(m.NpcId));

        public static BossStatusRequestNetMsg DecodeBossStatusRequest(ReadOnlySpan<byte> p) =>
            new() { NpcId = new Reader(p).ReadInt32() };

        public static byte[] EncodeAdminBossSchedule(AdminBossScheduleNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.NpcId);
            w.WriteInt32(m.Action);
        });

        public static AdminBossScheduleNetMsg DecodeAdminBossSchedule(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new AdminBossScheduleNetMsg { NpcId = r.ReadInt32(), Action = r.ReadInt32() };
        }

        public static byte[] EncodeBossStatus(BossStatusNetMsg m) => Write(w =>
        {
            var rows = m.Bosses ?? System.Array.Empty<BossStatusEntryNet>();
            w.WriteInt32(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i] ?? new BossStatusEntryNet();
                w.WriteInt32(row.NpcId);
                w.WriteInt32(row.ObjectId);
                w.WriteInt32(row.Kind);
                w.WriteInt32(row.Status);
                w.WriteInt64(row.RespawnUnixMs);
                w.WriteInt32(row.CurrentHp);
                w.WriteInt32(row.MaxHp);
                w.WriteInt32(row.CurrentMp);
                w.WriteInt32(row.MaxMp);
                w.WriteByte(row.Enabled ? (byte)1 : (byte)0);
                w.WriteByte(row.Spawned ? (byte)1 : (byte)0);
            }
        });

        public static BossStatusNetMsg DecodeBossStatus(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var rows = new BossStatusEntryNet[n];
            for (int i = 0; i < n; i++)
            {
                rows[i] = new BossStatusEntryNet
                {
                    NpcId = r.ReadInt32(),
                    ObjectId = r.ReadInt32(),
                    Kind = r.ReadInt32(),
                    Status = r.ReadInt32(),
                    RespawnUnixMs = r.ReadInt64(),
                    CurrentHp = r.ReadInt32(),
                    MaxHp = r.ReadInt32(),
                    CurrentMp = r.ReadInt32(),
                    MaxMp = r.ReadInt32(),
                    Enabled = r.ReadByte() != 0,
                    Spawned = r.ReadByte() != 0
                };
            }
            return new BossStatusNetMsg { Bosses = rows };
        }

        public static byte[] EncodeSiegeListRequest(SiegeListRequestNetMsg m) => Write(w => w.WriteInt32(m.CastleId));

        public static SiegeListRequestNetMsg DecodeSiegeListRequest(ReadOnlySpan<byte> p) =>
            new() { CastleId = new Reader(p).ReadInt32() };

        public static byte[] EncodeSiegeRegisterRequest(SiegeRegisterRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.CastleId);
            w.WriteInt32(m.Attacker);
            w.WriteInt32(m.Joining);
        });

        public static SiegeRegisterRequestNetMsg DecodeSiegeRegisterRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new SiegeRegisterRequestNetMsg
            {
                CastleId = r.ReadInt32(),
                Attacker = r.ReadInt32(),
                Joining = r.ReadInt32()
            };
        }

        public static byte[] EncodeSiegeDefenderAnswerRequest(SiegeDefenderAnswerRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.CastleId);
            w.WriteInt32(m.ClanId);
            w.WriteInt32(m.Approved);
        });

        public static SiegeDefenderAnswerRequestNetMsg DecodeSiegeDefenderAnswerRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new SiegeDefenderAnswerRequestNetMsg
            {
                CastleId = r.ReadInt32(),
                ClanId = r.ReadInt32(),
                Approved = r.ReadInt32()
            };
        }

        public static byte[] EncodeAdminCaptureCastle(AdminCaptureCastleNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.CastleId);
            w.WriteInt32(m.ClanId);
        });

        public static AdminCaptureCastleNetMsg DecodeAdminCaptureCastle(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new AdminCaptureCastleNetMsg { CastleId = r.ReadInt32(), ClanId = r.ReadInt32() };
        }

        public static byte[] EncodeSiegeInfo(SiegeInfoNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.CastleId);
            w.WriteString(m.Name ?? "");
            w.WriteInt32(m.OwnerClanId);
            w.WriteInt32(m.TaxPercent);
            w.WriteInt64(m.SiegeUnixMs);
            w.WriteInt64(m.RegistrationDeadlineUnixMs);
            w.WriteByte(m.RegistrationClosed ? (byte)1 : (byte)0);
            w.WriteByte(m.InProgress ? (byte)1 : (byte)0);
            w.WriteInt64(m.EndsUnixMs);
            var clans = m.Clans ?? System.Array.Empty<SiegeClanNet>();
            w.WriteInt32(clans.Length);
            for (int i = 0; i < clans.Length; i++)
            {
                var row = clans[i] ?? new SiegeClanNet();
                w.WriteInt32(row.ClanId);
                w.WriteInt32(row.Side);
            }
        });

        public static SiegeInfoNetMsg DecodeSiegeInfo(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            var info = new SiegeInfoNetMsg
            {
                CastleId = r.ReadInt32(),
                Name = r.ReadString(),
                OwnerClanId = r.ReadInt32(),
                TaxPercent = r.ReadInt32(),
                SiegeUnixMs = r.ReadInt64(),
                RegistrationDeadlineUnixMs = r.ReadInt64(),
                RegistrationClosed = r.ReadByte() != 0,
                InProgress = r.ReadByte() != 0,
                EndsUnixMs = r.ReadInt64()
            };
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var clans = new SiegeClanNet[n];
            for (int i = 0; i < n; i++)
                clans[i] = new SiegeClanNet { ClanId = r.ReadInt32(), Side = r.ReadInt32() };
            info.Clans = clans;
            return info;
        }

        public static byte[] EncodeOlympiadRegisterRequest(OlympiadRegisterRequestNetMsg m) =>
            Write(w => w.WriteInt32(m.Classed));

        public static OlympiadRegisterRequestNetMsg DecodeOlympiadRegisterRequest(ReadOnlySpan<byte> p) =>
            new() { Classed = new Reader(p).ReadInt32() };

        public static byte[] EncodeAdminReportOlympiadResult(AdminReportOlympiadResultNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.MatchId);
            w.WriteInt32(m.WinnerObjectId);
        });

        public static AdminReportOlympiadResultNetMsg DecodeAdminReportOlympiadResult(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new AdminReportOlympiadResultNetMsg { MatchId = r.ReadInt32(), WinnerObjectId = r.ReadInt32() };
        }

        public static byte[] EncodeOlympiadStatus(OlympiadStatusNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Period);
            w.WriteInt32(m.Cycle);
            w.WriteByte(m.InCompetition ? (byte)1 : (byte)0);
            w.WriteInt64(m.CompetitionStartUnixMs);
            w.WriteInt64(m.CompetitionEndUnixMs);
            w.WriteInt64(m.OlympiadEndUnixMs);
            w.WriteInt64(m.ValidationEndUnixMs);
            w.WriteInt64(m.NextWeeklyUnixMs);
            w.WriteByte(m.RegistrationOpen ? (byte)1 : (byte)0);
            w.WriteByte(m.Noble ? (byte)1 : (byte)0);
            w.WriteInt32(m.Points);
            w.WriteInt32(m.CompetitionsDone);
            w.WriteInt32(m.Won);
            w.WriteInt32(m.Lost);
            w.WriteInt32(m.Drawn);
            w.WriteInt32(m.Queue);
            w.WriteInt32(m.MatchId);
            w.WriteInt32(m.StadiumId);
            w.WriteInt32(m.ClassId);
        });

        public static OlympiadStatusNetMsg DecodeOlympiadStatus(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new OlympiadStatusNetMsg
            {
                Period = r.ReadInt32(),
                Cycle = r.ReadInt32(),
                InCompetition = r.ReadByte() != 0,
                CompetitionStartUnixMs = r.ReadInt64(),
                CompetitionEndUnixMs = r.ReadInt64(),
                OlympiadEndUnixMs = r.ReadInt64(),
                ValidationEndUnixMs = r.ReadInt64(),
                NextWeeklyUnixMs = r.ReadInt64(),
                RegistrationOpen = r.ReadByte() != 0,
                Noble = r.ReadByte() != 0,
                Points = r.ReadInt32(),
                CompetitionsDone = r.ReadInt32(),
                Won = r.ReadInt32(),
                Lost = r.ReadInt32(),
                Drawn = r.ReadInt32(),
                Queue = r.ReadInt32(),
                MatchId = r.ReadInt32(),
                StadiumId = r.ReadInt32(),
                ClassId = r.ReadInt32()
            };
        }

        public static byte[] EncodeOlympiadMatch(OlympiadMatchNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.MatchId);
            w.WriteInt32(m.StadiumId);
            w.WriteInt32(m.ZoneId);
            w.WriteByte(m.Classed ? (byte)1 : (byte)0);
            w.WriteInt32(m.Phase);
            w.WriteInt32(m.PlayerOneId);
            w.WriteInt32(m.PlayerTwoId);
            w.WriteString(m.PlayerOneName ?? "");
            w.WriteString(m.PlayerTwoName ?? "");
            w.WriteInt32(m.InstanceId);
        });

        public static OlympiadMatchNetMsg DecodeOlympiadMatch(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new OlympiadMatchNetMsg
            {
                MatchId = r.ReadInt32(),
                StadiumId = r.ReadInt32(),
                ZoneId = r.ReadInt32(),
                Classed = r.ReadByte() != 0,
                Phase = r.ReadInt32(),
                PlayerOneId = r.ReadInt32(),
                PlayerTwoId = r.ReadInt32(),
                PlayerOneName = r.ReadString(),
                PlayerTwoName = r.ReadString(),
                InstanceId = r.ReadInt32()
            };
        }

        public static byte[] EncodeOlympiadCountdown(OlympiadCountdownNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.MatchId);
            w.WriteInt32(m.Kind);
            w.WriteInt32(m.Seconds);
        });

        public static OlympiadCountdownNetMsg DecodeOlympiadCountdown(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new OlympiadCountdownNetMsg
            {
                MatchId = r.ReadInt32(),
                Kind = r.ReadInt32(),
                Seconds = r.ReadInt32()
            };
        }

        public static byte[] EncodeOlympiadResult(OlympiadResultNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.MatchId);
            w.WriteInt32(m.Outcome);
            w.WriteInt32(m.WinnerObjectId);
            w.WriteInt32(m.PointsOne);
            w.WriteInt32(m.PointsTwo);
            w.WriteInt32(m.DeltaOne);
            w.WriteInt32(m.DeltaTwo);
            w.WriteInt32(m.RewardItemId);
            w.WriteInt32(m.RewardCount);
            w.WriteInt32(m.CompetitionsDoneOne);
            w.WriteInt32(m.CompetitionsDoneTwo);
        });

        public static OlympiadResultNetMsg DecodeOlympiadResult(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new OlympiadResultNetMsg
            {
                MatchId = r.ReadInt32(),
                Outcome = r.ReadInt32(),
                WinnerObjectId = r.ReadInt32(),
                PointsOne = r.ReadInt32(),
                PointsTwo = r.ReadInt32(),
                DeltaOne = r.ReadInt32(),
                DeltaTwo = r.ReadInt32(),
                RewardItemId = r.ReadInt32(),
                RewardCount = r.ReadInt32(),
                CompetitionsDoneOne = r.ReadInt32(),
                CompetitionsDoneTwo = r.ReadInt32()
            };
        }

        public static byte[] EncodeDungeonEnterRequest(DungeonEnterRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.DungeonId);
            w.WriteInt32(m.Action);
        });

        public static DungeonEnterRequestNetMsg DecodeDungeonEnterRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new DungeonEnterRequestNetMsg { DungeonId = r.ReadInt32(), Action = r.ReadInt32() };
        }

        public static byte[] EncodeDungeonEntered(DungeonEnteredNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.DungeonId);
            w.WriteInt32(m.X);
            w.WriteInt32(m.Y);
            w.WriteInt32(m.Z);
            w.WriteByte(m.Inside ? (byte)1 : (byte)0);
            w.WriteInt32(m.Action);
        });

        public static DungeonEnteredNetMsg DecodeDungeonEntered(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new DungeonEnteredNetMsg
            {
                DungeonId = r.ReadInt32(),
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Z = r.ReadInt32(),
                Inside = r.ReadByte() != 0,
                Action = r.ReadInt32()
            };
        }

        public static byte[] EncodeSevenSignsActionRequest(SevenSignsActionRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Action);
            w.WriteInt32(m.Cabal);
            w.WriteInt32(m.Seal);
            w.WriteInt32(m.BlueStones);
            w.WriteInt32(m.GreenStones);
            w.WriteInt32(m.RedStones);
        });

        public static SevenSignsActionRequestNetMsg DecodeSevenSignsActionRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new SevenSignsActionRequestNetMsg
            {
                Action = r.ReadInt32(),
                Cabal = r.ReadInt32(),
                Seal = r.ReadInt32(),
                BlueStones = r.ReadInt32(),
                GreenStones = r.ReadInt32(),
                RedStones = r.ReadInt32()
            };
        }

        public static byte[] EncodeSevenSignsStatus(SevenSignsStatusNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Period);
            w.WriteInt32(m.Cycle);
            w.WriteInt32(m.PlayerCabal);
            w.WriteInt32(m.PlayerSeal);
            w.WriteInt32(m.PlayerStoneCount);
            w.WriteInt32(m.PlayerAncientAdena);
            w.WriteInt32(m.PlayerContribution);
            w.WriteInt32(m.DawnScore);
            w.WriteInt32(m.DuskScore);
            w.WriteInt32(m.DawnFestivalScore);
            w.WriteInt32(m.DuskFestivalScore);
            w.WriteInt64(m.DawnStonePoints);
            w.WriteInt64(m.DuskStonePoints);
            w.WriteInt32(m.AvariceOwner);
            w.WriteInt32(m.GnosisOwner);
            w.WriteInt32(m.StrifeOwner);
            w.WriteInt32(m.DawnMembers);
            w.WriteInt32(m.DuskMembers);
            w.WriteInt32(m.WinningCabal);
            w.WriteInt64(m.NextChangeUnixMs);
            w.WriteInt32(m.SkyState);
            w.WriteInt32(m.DawnAvarice);
            w.WriteInt32(m.DawnGnosis);
            w.WriteInt32(m.DawnStrife);
            w.WriteInt32(m.DuskAvarice);
            w.WriteInt32(m.DuskGnosis);
            w.WriteInt32(m.DuskStrife);
            w.WriteInt32(m.PreviousWinner);
        });

        public static SevenSignsStatusNetMsg DecodeSevenSignsStatus(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new SevenSignsStatusNetMsg
            {
                Period = r.ReadInt32(),
                Cycle = r.ReadInt32(),
                PlayerCabal = r.ReadInt32(),
                PlayerSeal = r.ReadInt32(),
                PlayerStoneCount = r.ReadInt32(),
                PlayerAncientAdena = r.ReadInt32(),
                PlayerContribution = r.ReadInt32(),
                DawnScore = r.ReadInt32(),
                DuskScore = r.ReadInt32(),
                DawnFestivalScore = r.ReadInt32(),
                DuskFestivalScore = r.ReadInt32(),
                DawnStonePoints = r.ReadInt64(),
                DuskStonePoints = r.ReadInt64(),
                AvariceOwner = r.ReadInt32(),
                GnosisOwner = r.ReadInt32(),
                StrifeOwner = r.ReadInt32(),
                DawnMembers = r.ReadInt32(),
                DuskMembers = r.ReadInt32(),
                WinningCabal = r.ReadInt32(),
                NextChangeUnixMs = r.ReadInt64(),
                SkyState = r.ReadInt32(),
                DawnAvarice = r.ReadInt32(),
                DawnGnosis = r.ReadInt32(),
                DawnStrife = r.ReadInt32(),
                DuskAvarice = r.ReadInt32(),
                DuskGnosis = r.ReadInt32(),
                DuskStrife = r.ReadInt32(),
                PreviousWinner = r.ReadInt32()
            };
        }

        public static byte[] EncodeSevenSignsSky(SevenSignsSkyNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.State);
            w.WriteInt32(m.Period);
            w.WriteInt32(m.WinningCabal);
        });

        public static SevenSignsSkyNetMsg DecodeSevenSignsSky(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new SevenSignsSkyNetMsg
            {
                State = r.ReadInt32(),
                Period = r.ReadInt32(),
                WinningCabal = r.ReadInt32()
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

        static void WriteSummary(Writer w, CharacterSummaryNet c)
        {
            w.WriteInt32(c.CharacterId);
            w.WriteInt32(c.Slot);
            w.WriteString(c.Name);
            w.WriteInt32(c.ClassId);
            w.WriteInt32(c.Race);
            w.WriteInt32(c.Sex);
            w.WriteInt32(c.Level);
            w.WriteInt32(c.Hp);
            w.WriteInt32(c.MaxHp);
            w.WriteInt32(c.Mp);
            w.WriteInt32(c.MaxMp);
            w.WriteInt32(c.HairStyle);
            w.WriteInt32(c.HairColor);
            w.WriteInt32(c.Face);
            w.WriteByte(c.PendingDelete ? (byte)1 : (byte)0);
            w.WriteInt32(c.X);
            w.WriteInt32(c.Y);
            w.WriteInt32(c.Z);
            w.WriteInt32(c.DeleteSecondsLeft);
            w.WriteByte(c.IsLastPlayed ? (byte)1 : (byte)0);
            w.WriteInt32(c.CurCp);
            w.WriteInt32(c.MaxCp);
            w.WriteInt64(c.Exp);
            w.WriteInt32(c.Sp);
            w.WriteInt32(c.Karma);
            w.WriteInt32(c.WeaponItemId);
            w.WriteInt32Array(c.Paperdoll);
        }

        static CharacterSummaryNet ReadSummary(ref Reader r)
        {
            var row = new CharacterSummaryNet
            {
                CharacterId = r.ReadInt32(),
                Slot = r.ReadInt32(),
                Name = r.ReadString(),
                ClassId = r.ReadInt32(),
                Race = r.ReadInt32(),
                Sex = r.ReadInt32(),
                Level = r.ReadInt32(),
                Hp = r.ReadInt32(),
                MaxHp = r.ReadInt32(),
                Mp = r.ReadInt32(),
                MaxMp = r.ReadInt32(),
                HairStyle = r.ReadInt32(),
                HairColor = r.ReadInt32(),
                Face = r.ReadInt32(),
                PendingDelete = r.ReadByte() != 0,
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Z = r.ReadInt32(),
                DeleteSecondsLeft = r.ReadInt32(),
                IsLastPlayed = r.ReadByte() != 0,
                CurCp = r.ReadInt32(),
                MaxCp = r.ReadInt32(),
                Exp = r.ReadInt64(),
                Sp = r.ReadInt32(),
                Karma = r.ReadInt32(),
                WeaponItemId = r.ReadInt32(),
                Paperdoll = r.ReadInt32Array()
            };
            if (row.DeleteSecondsLeft > 0)
                row.PendingDelete = true;
            return row;
        }

        static byte[] Write(Action<Writer> write)
        {
            var w = new Writer();
            write(w);
            return w.ToArray();
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
