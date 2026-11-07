# Zone Net friends + block

> Mobius `RequestFriend*` / `RequestBlock` / `FriendList` / `BlockList`. Server slice [#285](https://github.com/FioraSolutions/Fineage-Backend/issues/285) landed in `#287`; friend chat (type 12) remains open.

## Scope

| In | Out |
|----|-----|
| Invite by name (online), answer, delete, list + online flag | Friend chat channel (type 12) |
| Block / unblock / list / block-all | FriendListExtended UI |
| Persist friends + blocked on character JSON mirror | Normalized `character_friends` table |
| Online/offline notify via `FriendUpdate` | Cross-zone multi-instance |

## Opcodes

| Dir | Opcode | Name |
|-----|--------|------|
| C→S | `0x039F` | `FriendInvite` |
| C→S | `0x03A0` | `FriendAnswer` (`Response` 1 = accept) |
| C→S | `0x03A1` | `FriendListRequest` |
| C→S | `0x03A2` | `FriendDelete` |
| C→S | `0x03A3` | `BlockRequest` |
| S→C | `0x03A4` | `FriendInviteAsk` |
| S→C | `0x03A5` | `FriendList` |
| S→C | `0x03A6` | `FriendUpdate` |
| S→C | `0x03A7` | `BlockList` |

`BlockRequest.Type`: `0` add, `1` remove, `2` list, `3` block-all on, `4` block-all off.

Limits: **128** friends, **128** blocked (`FriendProtocol`).
