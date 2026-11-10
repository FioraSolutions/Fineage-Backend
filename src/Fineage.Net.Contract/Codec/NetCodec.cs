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
