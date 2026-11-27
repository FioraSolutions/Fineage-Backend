namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        public static byte[] EncodeShowBoardRequest(ShowBoardRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Unused);
        });

        public static ShowBoardRequestNetMsg DecodeShowBoardRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new ShowBoardRequestNetMsg { Unused = r.ReadInt32() };
        }

        public static byte[] EncodeBBSwrite(BBSwriteNetMsg m) => Write(w =>
        {
            w.WriteString(m.Url ?? "");
            w.WriteString(m.Arg1 ?? "");
            w.WriteString(m.Arg2 ?? "");
            w.WriteString(m.Arg3 ?? "");
            w.WriteString(m.Arg4 ?? "");
            w.WriteString(m.Arg5 ?? "");
        });

        public static BBSwriteNetMsg DecodeBBSwrite(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new BBSwriteNetMsg
            {
                Url = r.ReadString(),
                Arg1 = r.ReadString(),
                Arg2 = r.ReadString(),
                Arg3 = r.ReadString(),
                Arg4 = r.ReadString(),
                Arg5 = r.ReadString()
            };
        }

        // Mobius ShowBoard.java fixed tab bypasses (must match CommunityBoardProtocol).
        const string TopBypass = "bypass _bbshome";
        const string FavoriteBypass = "bypass _bbsgetfav";
        const string RegionBypass = "bypass _bbsloc";
        const string ClanBypass = "bypass _bbsclan";
        const string MemoBypass = "bypass _bbsmemo";
        const string MailBypass = "bypass _bbsmail";
        const string FriendsBypass = "bypass _bbsfriends";
        const string AddFavoriteBypass = "bypass bbs_add_fav";

        public static byte[] EncodeShowBoard(ShowBoardNetMsg m) => Write(w =>
        {
            w.WriteByte(m.Show);
            w.WriteString(TopBypass);
            w.WriteString(FavoriteBypass);
            w.WriteString(RegionBypass);
            w.WriteString(ClanBypass);
            w.WriteString(MemoBypass);
            w.WriteString(MailBypass);
            w.WriteString(FriendsBypass);
            w.WriteString(AddFavoriteBypass);
            w.WriteString(m.Content ?? "");
        });

        public static ShowBoardNetMsg DecodeShowBoard(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            byte show = r.ReadByte();
            // Fixed tab bypasses — discarded on decode; body is the content string.
            _ = r.ReadString();
            _ = r.ReadString();
            _ = r.ReadString();
            _ = r.ReadString();
            _ = r.ReadString();
            _ = r.ReadString();
            _ = r.ReadString();
            _ = r.ReadString();
            return new ShowBoardNetMsg { Show = show, Content = r.ReadString() };
        }
    }
}
