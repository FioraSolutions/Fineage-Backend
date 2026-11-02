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
            return msg;
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
                    EquippedSlot = r.ReadInt32()
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
                    EquippedSlot = r.ReadInt32()
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
                if (bytes.Length > 2048)
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
