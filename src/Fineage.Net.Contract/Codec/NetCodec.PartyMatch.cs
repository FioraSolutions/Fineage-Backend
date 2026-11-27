namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        public static byte[] EncodePartyMatchConfig(PartyMatchConfigNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Auto);
            w.WriteInt32(m.Location);
            w.WriteInt32(m.Level);
        });

        public static PartyMatchConfigNetMsg DecodePartyMatchConfig(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PartyMatchConfigNetMsg
            {
                Auto = r.ReadInt32(),
                Location = r.ReadInt32(),
                Level = r.ReadInt32()
            };
        }

        public static byte[] EncodePartyMatchList(PartyMatchListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.RoomId);
            w.WriteInt32(m.MaxMembers);
            w.WriteInt32(m.MinLevel);
            w.WriteInt32(m.MaxLevel);
            w.WriteInt32(m.Loot);
            w.WriteString(m.Title ?? "");
        });

        public static PartyMatchListNetMsg DecodePartyMatchList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PartyMatchListNetMsg
            {
                RoomId = r.ReadInt32(),
                MaxMembers = r.ReadInt32(),
                MinLevel = r.ReadInt32(),
                MaxLevel = r.ReadInt32(),
                Loot = r.ReadInt32(),
                Title = r.ReadString()
            };
        }

        public static byte[] EncodePartyMatchDetailRequest(PartyMatchDetailRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.RoomId);
            w.WriteInt32(m.Unk1);
            w.WriteInt32(m.Unk2);
            w.WriteInt32(m.Unk3);
        });

        public static PartyMatchDetailRequestNetMsg DecodePartyMatchDetailRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PartyMatchDetailRequestNetMsg
            {
                RoomId = r.ReadInt32(),
                Unk1 = r.ReadInt32(),
                Unk2 = r.ReadInt32(),
                Unk3 = r.ReadInt32()
            };
        }

        public static byte[] EncodePartyMatchDismiss(PartyMatchDismissNetMsg m) =>
            Write(w => { w.WriteInt32(m.RoomId); w.WriteInt32(m.Unk); });

        public static PartyMatchDismissNetMsg DecodePartyMatchDismiss(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PartyMatchDismissNetMsg { RoomId = r.ReadInt32(), Unk = r.ReadInt32() };
        }

        public static byte[] EncodePartyMatchWithdraw(PartyMatchWithdrawNetMsg m) =>
            Write(w => { w.WriteInt32(m.RoomId); w.WriteInt32(m.Unk); });

        public static PartyMatchWithdrawNetMsg DecodePartyMatchWithdraw(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PartyMatchWithdrawNetMsg { RoomId = r.ReadInt32(), Unk = r.ReadInt32() };
        }

        public static byte[] EncodePartyMatchOust(PartyMatchOustNetMsg m) => Write(w => w.WriteInt32(m.ObjectId));

        public static PartyMatchOustNetMsg DecodePartyMatchOust(ReadOnlySpan<byte> p) =>
            new() { ObjectId = new Reader(p).ReadInt32() };

        public static byte[] EncodePartyMatchAskJoin(PartyMatchAskJoinNetMsg m) =>
            Write(w => w.WriteString(m.Name ?? ""));

        public static PartyMatchAskJoinNetMsg DecodePartyMatchAskJoin(ReadOnlySpan<byte> p) =>
            new() { Name = new Reader(p).ReadString() };

        public static byte[] EncodePartyMatchAnswerJoin(PartyMatchAnswerJoinNetMsg m) =>
            Write(w => w.WriteInt32(m.Answer));

        public static PartyMatchAnswerJoinNetMsg DecodePartyMatchAnswerJoin(ReadOnlySpan<byte> p) =>
            new() { Answer = new Reader(p).ReadInt32() };

        public static byte[] EncodePartyMatchWaitingListRequest(PartyMatchWaitingListRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Page);
            w.WriteInt32(m.MinLevel);
            w.WriteInt32(m.MaxLevel);
            w.WriteInt32(m.Mode);
        });

        public static PartyMatchWaitingListRequestNetMsg DecodePartyMatchWaitingListRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PartyMatchWaitingListRequestNetMsg
            {
                Page = r.ReadInt32(),
                MinLevel = r.ReadInt32(),
                MaxLevel = r.ReadInt32(),
                Mode = r.ReadInt32()
            };
        }

        public static byte[] EncodePartyMatchDetail(PartyMatchDetailNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.RoomId);
            w.WriteInt32(m.MaxMembers);
            w.WriteInt32(m.MinLevel);
            w.WriteInt32(m.MaxLevel);
            w.WriteInt32(m.Loot);
            w.WriteInt32(m.Location);
            w.WriteString(m.Title ?? "");
        });

        public static PartyMatchDetailNetMsg DecodePartyMatchDetail(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PartyMatchDetailNetMsg
            {
                RoomId = r.ReadInt32(),
                MaxMembers = r.ReadInt32(),
                MinLevel = r.ReadInt32(),
                MaxLevel = r.ReadInt32(),
                Loot = r.ReadInt32(),
                Location = r.ReadInt32(),
                Title = r.ReadString()
            };
        }

        public static byte[] EncodePartyMatchRoomList(PartyMatchRoomListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.HasRooms ? 1 : 0);
            var rooms = m.Rooms ?? System.Array.Empty<PartyMatchRoomListEntryNet>();
            w.WriteInt32(rooms.Length);
            for (int i = 0; i < rooms.Length; i++)
            {
                var room = rooms[i] ?? new PartyMatchRoomListEntryNet();
                w.WriteInt32(room.RoomId);
                w.WriteString(room.Title ?? "");
                w.WriteInt32(room.Location);
                w.WriteInt32(room.MinLevel);
                w.WriteInt32(room.MaxLevel);
                w.WriteInt32(room.MaxMembers);
                w.WriteInt32(room.Members);
                w.WriteString(room.OwnerName ?? "");
            }
        });

        public static PartyMatchRoomListNetMsg DecodePartyMatchRoomList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            bool has = r.ReadInt32() != 0;
            int n = r.ReadInt32();
            var rooms = new PartyMatchRoomListEntryNet[n];
            for (int i = 0; i < n; i++)
            {
                rooms[i] = new PartyMatchRoomListEntryNet
                {
                    RoomId = r.ReadInt32(),
                    Title = r.ReadString(),
                    Location = r.ReadInt32(),
                    MinLevel = r.ReadInt32(),
                    MaxLevel = r.ReadInt32(),
                    MaxMembers = r.ReadInt32(),
                    Members = r.ReadInt32(),
                    OwnerName = r.ReadString()
                };
            }

            return new PartyMatchRoomListNetMsg { HasRooms = has, Rooms = rooms };
        }

        public static byte[] EncodePartyRoomMemberList(PartyRoomMemberListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Mode);
            var members = m.Members ?? System.Array.Empty<PartyRoomMemberEntryNet>();
            w.WriteInt32(members.Length);
            for (int i = 0; i < members.Length; i++)
                WriteMember(w, members[i] ?? new PartyRoomMemberEntryNet());
        });

        public static PartyRoomMemberListNetMsg DecodePartyRoomMemberList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int mode = r.ReadInt32();
            int n = r.ReadInt32();
            var members = new PartyRoomMemberEntryNet[n];
            for (int i = 0; i < n; i++)
                members[i] = ReadMember(r);
            return new PartyRoomMemberListNetMsg { Mode = mode, Members = members };
        }

        public static byte[] EncodePartyRoomMemberUpdate(PartyRoomMemberUpdateNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Mode);
            WriteMember(w, m.Member ?? new PartyRoomMemberEntryNet());
        });

        public static PartyRoomMemberUpdateNetMsg DecodePartyRoomMemberUpdate(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PartyRoomMemberUpdateNetMsg { Mode = r.ReadInt32(), Member = ReadMember(r) };
        }

        public static byte[] EncodePartyRoomClose() => Write(_ => { });

        public static byte[] EncodePartyRoomAskJoin(PartyRoomAskJoinNetMsg m) =>
            Write(w => w.WriteString(m.CharName ?? ""));

        public static PartyRoomAskJoinNetMsg DecodePartyRoomAskJoin(ReadOnlySpan<byte> p) =>
            new() { CharName = new Reader(p).ReadString() };

        public static byte[] EncodePartyMatchWaitingRoom(PartyMatchWaitingRoomNetMsg m) => Write(w =>
        {
            var players = m.Players ?? System.Array.Empty<PartyMatchWaitingEntryNet>();
            w.WriteInt32(m.Page);
            w.WriteInt32(players.Length);
            for (int i = 0; i < players.Length; i++)
            {
                var pl = players[i] ?? new PartyMatchWaitingEntryNet();
                w.WriteString(pl.Name ?? "");
                w.WriteInt32(pl.ClassId);
                w.WriteInt32(pl.Level);
            }
        });

        public static PartyMatchWaitingRoomNetMsg DecodePartyMatchWaitingRoom(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int page = r.ReadInt32();
            int n = r.ReadInt32();
            var players = new PartyMatchWaitingEntryNet[n];
            for (int i = 0; i < n; i++)
            {
                players[i] = new PartyMatchWaitingEntryNet
                {
                    Name = r.ReadString(),
                    ClassId = r.ReadInt32(),
                    Level = r.ReadInt32()
                };
            }

            return new PartyMatchWaitingRoomNetMsg { Page = page, Players = players };
        }

        static void WriteMember(Writer w, PartyRoomMemberEntryNet m)
        {
            w.WriteInt32(m.ObjectId);
            w.WriteString(m.Name ?? "");
            w.WriteInt32(m.ClassId);
            w.WriteInt32(m.Level);
            w.WriteInt32(m.Location);
            w.WriteInt32(m.MemberType);
            w.WriteInt32(0);
        }

        static PartyRoomMemberEntryNet ReadMember(Reader r)
        {
            var member = new PartyRoomMemberEntryNet
            {
                ObjectId = r.ReadInt32(),
                Name = r.ReadString(),
                ClassId = r.ReadInt32(),
                Level = r.ReadInt32(),
                Location = r.ReadInt32(),
                MemberType = r.ReadInt32()
            };
            _ = r.ReadInt32(); // Mobius instance trail
            return member;
        }
    }
}
