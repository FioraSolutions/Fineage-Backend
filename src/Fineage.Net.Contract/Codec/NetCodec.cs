using System;
using System.Buffers.Binary;
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
        });

        public static EntitySpawnMsg DecodeEntitySpawn(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new EntitySpawnMsg
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
