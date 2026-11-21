# Localization

Player-facing text is served in the player's language. English (`en`) is the only locale shipped with the server. Another language is a new folder under `data/l10n`. No server code change is required to add one.

There is one lookup. System strings, NPC and quest HTML, and names the server itself sends all go through `LocalizationCatalog` (`Fineage.Game.L10n`). A feature does not pick a language with its own `if`.

## Player language

The language is an account preference. Every character on the account shares it. At enter, the zone copies it onto that character's session (`ZoneSession.Language`), and HTML and system lines for that player use the session.

The client sends the tag on two packets. Both tails are optional, so an older client still connects.

| Packet | Opcode | Tail |
|--------|--------|------|
| `Auth` | `0x0201` | After the register-if-missing byte, an optional UTF-8 string (uint16 length + bytes). Omitted: the account keeps its stored language. |
| `EnterWorld` | `0x0303` | The payload is that string, or empty. Empty: the zone uses the language stored at login. |

A missing tag, or a tag that is not a locale, is `en`. A locale tag is a lowercase language (`en`, `ru`) or language plus a short region (`pt-br`): two letters, then an optional `-` and 2–8 letters or digits.

The tag is stored on `accounts.language` (migration `020_account_language.sql`, default `en`). Login and Zone are separate processes, so the column is what the zone reads. The in-memory snapshot carries the same field for tests and backup.

## Lookup

`LocalizationCatalog.Get(key, locale)`:

1. The row for that locale.
2. If it is missing and the locale is not `en`: a **warning** that names the key and the locale (`event=l10n_fallback key=… locale=… fallback=en`), then the English row.
3. If English has no row either: an **error** that names the key (`event=l10n_missing key=… locale=en`). The player sees the key, not a stand-in sentence.

`Resolve(key, locale, english)` is the same rule when the English text is already in hand (a datapack name, or HTML read from the shipped file) and is not yet a row in `data/l10n/en`. Supplying that English text is the English hit. It does not skip the warning for any other locale.

There is no silent fallback.

## Files

```
data/l10n/<locale>/strings.json          key → text, {0} {1} … are string.Format args
data/l10n/<locale>/html/...              same relative paths the English HTML uses
data/l10n/<locale>/names/items.json      "57": "Adena"
data/l10n/<locale>/names/skills.json
data/l10n/<locale>/names/npcs.json
```

Keys:

| Kind | Key |
|------|-----|
| System / chat line | the key in `strings.json`, for example `chat.welcome` |
| NPC HTML | `html/<folder>/<file>`, for example `html/merchant/30001.htm` |
| Quest HTML | `html/quests/<quest-folder>/<file>` |
| Vendored quest dialogs | `html/mobius-quest-dialogs/<relative>` |
| Item / skill / NPC name | `name.item.<id>`, `name.skill.<id>`, `name.npc.<id>` |

English NPC HTML already shipped under `data/html` is the `en` corpus. Quest HTML shipped next to each quest is the English body of that dialog. A file at `data/l10n/en/html/...` overrides the shipped page. Other locales only read `data/l10n/<locale>/`. Dropping a folder named `en` is optional.

The HTML loader asks the catalog for the player's locale, then the usual type folder (`merchant`, `guard`, …), then `default`, then `npcdefault.htm`. That path order is how an NPC page is chosen. It is not a second language fallback. A locale miss is still the one warning above.

## Add a language

1. Pick a tag, for example `ru`.
2. Create `data/l10n/ru/`.
3. Copy `data/l10n/en/strings.json` and translate the values. Leave the keys alone. Keep `{0}` placeholders.
4. For each NPC or quest page you translate, add the file at the same relative path under `data/l10n/ru/html/`. Pages you skip stay English and log `l10n_fallback` with the key and `ru`.
5. For names the server sends, add `names/items.json`, `names/skills.json`, and `names/npcs.json`. Ids you skip fall back to English with the same warning.
6. Restart the server. It picks up every directory under `data/l10n` whose name is a locale tag. A folder that is not a tag is ignored.

Do not add a switch in server code for the new language.

## Unity client

The server localizes text it composes. The client localizes its own UI and the names it reads from `.dat` files. A client agent should do the following and nothing else on the wire.

1. **Send the language.** On `Auth` (`0x0201`), after `account` (string), `password` (string), and `registerIfMissing` (byte), write one more string: the tag (`en`, `ru`, `pt-br`). On `EnterWorld` (`0x0303`), the payload is that same string. Omit the tail only for an older build that does not know the field; the server then keeps the stored tag, or `en`. Strings are uint16 little-endian length plus UTF-8, the same encoding as every other Net string.
2. **Do not send a language per window.** One tag per connection. Changing it means sending it again on the next `Auth` or `EnterWorld`.
3. **Client string table for UI.** Buttons, tooltips, the options screen, and any line the client draws without a server packet live in a client table keyed the same way (`data/l10n/<locale>/…` or the client's equivalent). The server does not send those.
4. **`.dat` names.** Item, skill, and NPC names that the client looks up by id from Interlude `.dat` files stay client-side. Ship a table per language, or keep the English `.dat` until a translation exists. When a packet already contains a name string (NPC `EntitySpawn.name`, a system-message line, an HTML window), show that string. The server has already localized it. Do not look it up again.
5. **System messages and HTML.** `SystemMessage.text` and `NpcHtml.html` arrive in the player's language. Display them as sent. `SystemMessage.id` stays the Interlude id so the client can still pick an icon; the text is the line.
6. **Adding a language on the client** is a new string table plus the matching `data/l10n/<locale>/` folder on the server. The packets do not change.

## What the server localizes

- NPC, bypass, and quest-file HTML, through `LocalizedHtml`, using the character session's language.
- Chat and combat system lines whose `SystemMessageMsg.Key` is set (`data/l10n/en/strings.json`). The zone fills `Text` for that player before the packet is encoded. The key is not on the wire.
- Item and NPC names the server inserts into a per-player packet (HTML `%npcname%`, pet item lines), via `name.item.<id>` and `name.npc.<id>`.

## What is still English

System messages that still assign `Text` and leave `Key` empty are sent as written. Move one by adding the key to `strings.json` and setting `Key` (keep the English `Text` so a catalog miss still has a line until the English row exists). Broadcast `EntitySpawn` is one payload for every watcher, so a shared spawn name cannot differ per watcher. Nameplates the client draws from `.dat` ids stay on the client. Skill names are ids on the wire; the client resolves them from its own table.
