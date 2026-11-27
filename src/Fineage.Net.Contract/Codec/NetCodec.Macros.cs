namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        public static byte[] EncodeMakeMacro(MakeMacroNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Id);
            w.WriteString(m.Name ?? "");
            w.WriteString(m.Descr ?? "");
            w.WriteString(m.Acronym ?? "");
            w.WriteByte((byte)m.Icon);
            var cmds = m.Commands ?? System.Array.Empty<MacroCmdNet>();
            w.WriteByte((byte)cmds.Length);
            for (int i = 0; i < cmds.Length; i++)
            {
                var c = cmds[i] ?? new MacroCmdNet();
                w.WriteByte((byte)c.Entry);
                w.WriteByte((byte)c.Type);
                w.WriteInt32(c.D1);
                w.WriteByte((byte)c.D2);
                w.WriteString(c.Cmd ?? "");
            }
        });

        public static MakeMacroNetMsg DecodeMakeMacro(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int id = r.ReadInt32();
            string name = r.ReadString();
            string descr = r.ReadString();
            string acronym = r.ReadString();
            int icon = r.ReadByte();
            int count = r.ReadByte();
            var cmds = new MacroCmdNet[count];
            for (int i = 0; i < count; i++)
            {
                cmds[i] = new MacroCmdNet
                {
                    Entry = r.ReadByte(),
                    Type = r.ReadByte(),
                    D1 = r.ReadInt32(),
                    D2 = r.ReadByte(),
                    Cmd = r.ReadString()
                };
            }

            return new MakeMacroNetMsg
            {
                Id = id,
                Name = name,
                Descr = descr,
                Acronym = acronym,
                Icon = icon,
                Commands = cmds
            };
        }

        public static byte[] EncodeDeleteMacro(DeleteMacroNetMsg m) => Write(w => w.WriteInt32(m.Id));

        public static DeleteMacroNetMsg DecodeDeleteMacro(ReadOnlySpan<byte> p) =>
            new() { Id = new Reader(p).ReadInt32() };

        public static byte[] EncodeMacroList(MacroListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Revision);
            w.WriteByte(0);
            w.WriteByte((byte)m.Count);
            w.WriteByte(m.HasMacro ? (byte)1 : (byte)0);
            if (!m.HasMacro)
                return;
            w.WriteInt32(m.MacroId);
            w.WriteString(m.Name ?? "");
            w.WriteString(m.Descr ?? "");
            w.WriteString(m.Acronym ?? "");
            w.WriteByte((byte)m.Icon);
            var cmds = m.Commands ?? System.Array.Empty<MacroCmdNet>();
            w.WriteByte((byte)cmds.Length);
            for (int i = 0; i < cmds.Length; i++)
            {
                var c = cmds[i] ?? new MacroCmdNet();
                w.WriteByte((byte)(i + 1));
                w.WriteByte((byte)c.Type);
                w.WriteInt32(c.D1);
                w.WriteByte((byte)c.D2);
                w.WriteString(c.Cmd ?? "");
            }
        });

        public static MacroListNetMsg DecodeMacroList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int rev = r.ReadInt32();
            _ = r.ReadByte();
            int count = r.ReadByte();
            bool has = r.ReadByte() != 0;
            var msg = new MacroListNetMsg { Revision = rev, Count = count, HasMacro = has };
            if (!has)
                return msg;
            msg.MacroId = r.ReadInt32();
            msg.Name = r.ReadString();
            msg.Descr = r.ReadString();
            msg.Acronym = r.ReadString();
            msg.Icon = r.ReadByte();
            int n = r.ReadByte();
            var cmds = new MacroCmdNet[n];
            for (int i = 0; i < n; i++)
            {
                cmds[i] = new MacroCmdNet
                {
                    Entry = r.ReadByte(),
                    Type = r.ReadByte(),
                    D1 = r.ReadInt32(),
                    D2 = r.ReadByte(),
                    Cmd = r.ReadString()
                };
            }

            msg.Commands = cmds;
            return msg;
        }
    }
}
