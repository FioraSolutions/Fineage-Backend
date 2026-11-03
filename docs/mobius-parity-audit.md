# Сверка с L2J Mobius CT0 Interlude

Сверка логики нашего сервера (ветка `dev`, коммит `03c5376`) против эталонного L2J Mobius CT0 Interlude.
Документ фиксирует только расхождения. Это не план рефакторинга.

## Как читать

**Пути.** Ссылки на Mobius даны от корня `ref/mobius-ct0-interlude/java/org/l2jmobius/` (далее просто `gameserver/...`, `Config.java`, `config/*.ini`). Ссылки на наш код — от корня репозитория (`src/...`, `data/...`). Номера строк — на момент коммита `03c5376`.

**Критичность.**

| Метка | Значение |
|---|---|
| `high` | игрок чувствует сразу и/или это ломает баланс |
| `medium` | заметно, но есть обход |
| `low` | косметика или редкий кейс |
| `stub` | каркас есть, наполнения нет |

**Область.** Проверено 13 механик (`D1`–`D63`) и 20 систем, которых у нас нет целиком (`M1`–`M20`).
Формулировка «не найдено» означает, что поиск по `src/` не дал результата; в скобках перечислено, что именно искали.

**Что НЕ проверялось.** Сетевой протокол (MessagePack V2), логин/лобби, чат, клан-меню, осадный календарь, Seven Signs как состояние — они сравнивались только там, где влияют на боевую механику.

## Сводка

| Механика | Mobius | У нас | Расхождение | Критичность |
|---|---|---|---|---|
| 1. Смерть и респавн | `Player.doDie` → lost-exp, дебафф 5076, CP, дроп вещей | `ZoneWorldDeath.cs` — только админ-килл, 100% HP/MP, без потерь | есть, глубокое | `high` |
| 2. Физ. урон | `Formulas.calcPhysDam` + miss/crit/shield/ss/position | `ZoneWorldCombat.cs` — константа 32 | есть, полное | `high` |
| 3. Маг. урон и блоу | `calcMagicDam`, `calcBlowDamage`, `calcMagicSuccess` | `Power + 32/3` | есть, полное | `high` |
| 4. Скиллы и каст | таймеры `onMagicLaunchedTimer`/`onMagicHitTimer`, `calcAtkBreak` | мгновенный урон на запрос | есть, полное | `high` |
| 5. Стат-пайплайн | `CreatureStat.calcStat` + `Calculator` + порядок Func | статы класса читаются, но в бой не идут | есть, глубокое | `high` |
| 6. Реген | `calcHpRegen`/`calcMpRegen`, зоны, осада, кланхолл, CP | `maxHp/64 + level/12`, только игроки | есть | `medium` |
| 7. Агро и ИИ | `AttackableAI`: hate, aggro range, LOS, return home, clan help | ИИ нет, моб не инициирует бой | отсутствует | `high` |
| 8. Награды за убийство | `calculateRewards` → exp/SP по урону и партии | нет ни exp, ни SP | отсутствует | `high` |
| 9. Квесты | `Quest.notifyKill`/`onTalk` + party-кредит | движок есть, `onKill` не подключён | частично | `high` |
| 10. Инвентарь и вес | `MaxLoad` = CON×69000, penalty 4270, слоты | веса нет, ёмкость бесконечна | отсутствует | `high` |
| 11. Торговля и склад | `TradeList`, `PlayerWarehouse`/`ClanWarehouse`/`PlayerFreight`, private store | обмен есть, склад и магазины — заглушки | частично | `stub` |
| 12. Дроп и спойл | `calculateDrops`, spoil/sweep скиллами, `DropProtection` | дроп есть, спойл только админский | частично | `stub` |
| 13. Спавны | `SpawnTable` + территории + `WalkingManager` | плоский JSON, территории игнорируются | частично | `stub` |

**Итого расхождений: 83** — `high` 34, `medium` 30, `low` 14, `stub` 5.
Из них 63 — внутри перечисленных механик, 20 — системы, отсутствующие целиком.

---

## 1. Смерть и респавн

### Mobius

`gameserver/model/actor/Player.java:4765` — `Player.doDie`. Поверх `Creature.doDie` (`gameserver/model/actor/Creature.java:2319`) делает:

- дроп вещей при смерти: `Player.java:4868` (`onDieDropItem`, тело `Player.java:4961`–`5055`) — шансы `KARMA_RATE_DROP*`, `PLAYER_RATE_DROP*`, ограничение `dropLimit`, исключения (adena, quest, shadow, time-limited);
- потеря опыта: `Player.java:4894`–`4899` вызывает `calculateDeathExpPenalty` (`Player.java:5318`–`5364`): `PlayerXpPercentLostData.getXpPercent(level)` (`data/xml/PlayerXpPercentLostData.java:68`) × `Stat.REDUCE_EXP_LOST_BY_RAID/_MOB/_PVP` × `RATE_KARMA_EXP_LOST` (`Config.java:2282`), затем `removeExp(lostExp)`; `getExpBeforeDeath()` сохраняется, чтобы рес мог вернуть опыт (`Player.java:5297` `restoreExp`).
- дебафф Death Penalty: `Player.java:4931` → `calculateDeathPenaltyBuffLevel` (`Player.java:11690`–`11720`) c шансом `DEATH_PENALTY_CHANCE` (`config/Character.ini:32`, значение 10) и множителями по типу убийцы; `increaseDeathPenaltyBuffLevel` (`Player.java:11722`–`11745`) навешивает скилл `5076` уровня 1–15 и присылает `EtcStatusUpdate`.
- прочее: снятие кубиков (`Player.java:4903`–`4912`), сброс кармы (`Player.java:4949`–`4951`), `AntiFeedManager`, отключение петов/самонов.

Респавн: `Creature.doRevive` (`Creature.java:2525`–`2557`) восстанавливает HP/MP/CP **не до 100%**, а до долей из конфига — `RESPAWN_RESTORE_HP/MP/CP` (`config/Character.ini:37`–`39`: CP 0, HP 65, MP 0), затем `new Revive(this)`. Точка возрождения выбирается в `gameserver/network/clientpackets/RequestRestartPoint.java:120` (`portPlayer`): кланхолл (`:138`), замок (`:152`), siege HQ (`:186`), фиксированная точка (`:217`), тюрьма (`:240`), с восстановлением опыта от кланхолла/замка (`:148`, `:182`).

### У нас

`src/Fineage.Zone/World/ZoneWorldDeath.cs`:

- `TryAdminKillPlayer` (`:42`) — единственный вход в смерть игрока; вызывается только из админ-пакета (`src/Fineage.Zone/ZoneServer.cs:957`–`975`).
- `ApplyPlayerDeath` (`:68`–`86`): `Dead = true`, `CurHp = 0`, сброс `Sitting`/`Running`/`ActiveEnchantObjectId`, `Abnormals.Clear()`, `CombatUntilUnixMs = 0`. Ни опыта, ни дебаффа, ни дропа вещей, ни кармы/`PkKills`.
- `ReviveAtVillage` (`:89`–`111`): `CurHp = MaxHp`, `CurMp = MaxMp` (`:92`–`93`) — всегда 100%. CP не существует как поле вовсе (см. `src/Fineage.Zone/World/ZoneWorld.cs:27`–`97`; в проводе `src/Fineage.Protocol/Messages.cs:482`–`489` есть только CurHp/MaxHp/CurMp/MaxMp).
- Точка рестарта — константа `TalkingIslandRestart` (`:101`), других вариантов нет; `DeathProtocol.ReviveToVillage = 0` — единственный поддерживаемый action (`:60`, `src/Fineage.Protocol/DeathProtocol.cs:19`).

### Расхождение

- `D1` **`high`** — потери опыта при смерти нет: `calculateDeathExpPenalty` не портирован, `PlayerXpPercentLostData` в `data/` отсутствует (искал `XpPercentLost`, `DeathExpPenalty`, `removeExp` в `src/` и `data/`).
- `D2` **`high`** — дебаффа Death Penalty (скилл 5076) нет; `EtcStatusUpdate` в протоколе отсутствует (искал `EtcStatus`, `5076`). Игрок после смерти не слабее, чем до.
- `D3` **`high`** — CP нет ни в модели `ZoneEntity`, ни в `InterludeStatus` (`Messages.cs:482`–`489`), ни в регене. В Interlude CP — первый пул урона в PvP; без него PvP-баланс невозможен.
- `D4` **`high`** — игрок не может умереть от боя: `ApplyPlayerDeath` вызывается только из `TryAdminKillPlayer`, а `ZoneWorldCombat.TryAttack` (`:62`–`110`) требует `target.Kind == ZoneEntityKind.Npc` (`:77`) и уменьшает HP только цели-мобу (`:85`). PvE-смерть и PvP-урон отсутствуют полностью.
- `D5` **`medium`** — респавн 100% HP/MP вместо `RespawnRestoreHP = 65` / `RespawnRestoreMP = 0` / `RespawnRestoreCP = 0` (`config/Character.ini:37`–`39`). Обхода нет, но лечится ручным вводом значения.
- `D6` **`medium`** — один restart point вместо шести видов (`RequestRestartPoint.java:138`–`247`); нет кланхолла, замка, siege HQ, тюрьмы, festival, agathion. Восстановление опыта от кланхолла/замка (`:148`, `:182`) отсутствует вместе с ними.
- `D7` **`low`** — у игрока после смерти нет трупа и таймера распада (`DecayTaskManager`, `gameserver/taskmanager/DecayTaskManager.java:34`); никто не видит лежащее тело, кроме пакета `Die`. Дроп вещей при смерти и потеря кармы (`Player.java:4949`, `:4961`) не реализованы и не могут быть, пока нет кармы/`KARMA_*` (искал `Karma`, `KARMA_`).

---

## 2. Формулы физического урона

### Mobius

`gameserver/model/stats/Formulas.java:646` — `calcPhysDam`:

- базис `attacker.getPAtk(target) / target.getPDef(attacker)` (`:651`–`652`);
- позиционный бонус `1.2` сзади, `1.1` с фланга (`:650`);
- PvP-защита `Stat.PVP_PHYSICAL_DEF` (`:658`);
- щит прибавляет `getShldDef()` или perfect block возвращает 1 (`:660`–`672`);
- soulshot удваивает базу, к ней прибавляется `skill.getPower(...)` (`:676`–`677`);
- крит: `2 × CRITICAL_DAMAGE × CRITICAL_DAMAGE_POS × DEFENCE_CRITICAL_DAMAGE × (76×damage×prox/defence)` плюс аддитивные бонусы (`:679`–`688`); без крита — `76×damage×prox/defence` (`:688`);
- trait-бонус `calcAttackTraitBonus` (`Formulas.java:1961`), разброс оружия `getRandomDamageMultiplier()` (`Creature.java:5964`), минимум 1 (`:705`–`713`).

Обвязка одного удара — `gameserver/model/actor/Creature.java:1202`–`1256` (`doAttackHitByBow`): `Formulas.calcHitMiss` (`:1209`, формула `Formulas.java:1111`: `(80 + 2×(accuracy−evasion))×10`, кламп 200…980 из 1000), `calcShldUse` (`:1220`, `Formulas.java:1135`), `calcCrit` (`:1223`, `Formulas.java:993`), затем `calcPhysDam` (`:1226`), расход стрелы (`:1212`), отложенный `HitTask` по скорости атаки (`:1240`).

### У нас

`src/Fineage.Zone/Combat/ZoneCombat.cs:7`–`8`: `MeleeRange = 900`, `SampleMeleeDamage = 32` (комментарий `:3` прямо говорит «not full Interlude formulas»). `src/Fineage.Zone/World/ZoneWorldCombat.cs:84`–`85`: `int damage = ZoneCombat.SampleMeleeDamage; target.CurHp -= damage`. Проверка дистанции — `ZoneCombat.InMeleeRange` (`:9`–`14`), проверки видимости — `VisibleTo` (`ZoneWorldCombat.cs:205`).

### Расхождение

- `D8` **`high`** — урон константный 32 (`ZoneWorldCombat.cs:84`); нет ни `PAtk`, ни `PDef`, ни грейда оружия, ни уровня. Моб с HP 96 (`src/Fineage.Zone/TalkingIsland.cs:17`) умирает ровно за 3 удара при любом эквипе.
- `D9` **`high`** — нет промахов: `calcHitMiss` не портирован (искал `Accuracy`, `Evasion` в `src/`; `Accuracy`/`CritRate` есть только в `src/Fineage.Game/ClassTemplate.cs:113`–`115` как справочные поля класса и в бою не читаются).
- `D10` **`high`** — нет критов: `calcCrit`/`CRITICAL_DAMAGE*` отсутствуют; `CritRate` из класса нигде не используется.
- `D11` **`high`** — нет щитов: `calcShldUse` и `getShldDef()` не портированы (искал `Shield`, `Shld` — совпадений нет).
- `D12` **`high`** — нет soulshot/стрел: расход и множитель `×2` (`Formulas.java:676`) отсутствуют, сам предмет-расходник не расходуется (искал `SoulShot`, `Arrow` в `src/`; `EtcItemType.Arrow` есть только как условие экипировки в `src/Fineage.Game/PlayerInventory.cs:448`).
- `D13` **`medium`** — нет позиционных бонусов (behind/flank, `Formulas.java:650`), нет разброса оружия, нет trait/attribute модификаторов, нет PvP/PvE-множителей. Позиция игрока вообще не влияет на урон: `TryAttack` знает только дистанцию.
- `D14` **`high`** — нет ограничения скорости атаки: каждый `AttackRequest` наносит урон немедленно (`ZoneWorldCombat.cs:62`–`110`), поля «до следующего удара» в `ZoneEntity` нет (искал `NextAttack`, `AttackDelay`, `PAtkSpd` — в `src/` только справочные поля класса `ClassTemplate.cs:117`). DPS = частота пакетов клиента.

---

## 3. Магический урон и блоу

### Mobius

`Formulas.calcMagicDam` (`Formulas.java:792`–`870`): `mDef` цели с учётом щита (`:795`–`806`), `mAtk` атакующего, PvP-модификаторы (`:815`–`823`), spirit/blessed spirit shot `×2`/`×4` (`:826`), формула `91×sqrt(mAtk)/mDef × skill.getPower(...)` (`:828`), затем проверка `calcMagicSuccess` (`:1396`) с `ALT_GAME_MAGICFAILURES` и половинным уроном при частичном успехе (`:831`–`850`).

`Formulas.calcBlowDamage` (`Formulas.java:497`): отдельная формула для блоу-скиллов (позиция сзади, крит-множитель, `CRITICAL_DAMAGE`), плюс `calcCounterDamage` для контратаки.

### У нас

`src/Fineage.Zone/World/ZoneWorldSkills.cs:105`: `int damage = Math.Max(1, levelData.Power + ZoneCombat.SampleMeleeDamage / 3);` — `Power` из `data/skills.json` плюс 10. `Power` берётся из `SkillLevelData.Power` (`src/Fineage.Game/Skills/SkillLevelData.cs:19`).

### Расхождение

- `D15` **`high`** — формула `Power + 10` вместо `calcMagicDam`/`calcPhysDam(skill)`; `mAtk`, `mDef`, уровень кастера и цели не участвуют. Скилл 1-го уровня и 37-го бьют одинаково сильно при равном `Power`.
- `D16` **`high`** — нет блоу-формулы, нет магических провалов (`calcMagicSuccess`), нет mcrit, нет counter/reflect. Все скиллы — один линейный путь (`ZoneWorldSkills.cs:105`).
- `D17` **`medium`** — нет HP-drain, over-hit и элементальных/трейт-модификаторов: `SkillLevelData` (`SkillLevelData.cs:5`–`23`) содержит только `MpConsume`, `Power`, `AbnormalLevel` — поля эффекта отсутствуют.

---

## 4. Скиллы и каст

### Mobius

`Creature.doCast` (`Creature.java:1486`) → `checkDoCastConditions` → `beginCast` (`:1530`). Внутри:

- время каста `skillTime = hitTime + coolTime`, затем `Formulas.calcAtkSpd` (`:1709`–`1716`, `Formulas.java:1096`), магические скиллы −40% при заряженных SPS/BSS (`:1718`–`1721`);
- `_castInterruptTime` (`:1744`) — окно, в котором каст можно прервать; `isCastingNow`/`isCastingSimultaneouslyNow`;
- перезарядка `reuseDelay × MAGIC_REUSE_RATE` (магия) / `P_REUSE` (физика) / `DANCE_REUSE` (`:1752`–`1768`);
- `onMagicLaunchedTimer` (`:5359`–`5483`): проверка выхода цели за `effectRange`, **LOS** (`canSeeTarget`) и **peace zone** (`isInsidePeaceZone`), затем `onMagicHitTimer` (`:5492`) наносит урон;
- прерывание от урона: `Formulas.calcAtkBreak` (`Formulas.java:1029`) — шанс сбить каст, если `ALT_GAME_CANCEL_CAST` (`config/Character.ini:129`).

### У нас

`src/Fineage.Zone/World/ZoneWorldSkills.cs`: `TrySkillUse` (`:45`–`75`) проверяет `KnownSkills` (`:55`), пассивность (`:58`), перезарядку по словарю `SkillReuseUntilMs` (`:62`), MP (`:65`), затем сразу ветвится в `TrySkillUseAttack` (`:77`), `TrySkillUseBuff` (`:121`), `TrySkillUseDebuff` (`:154`). Во всех трёх урон/эффект применяется синхронно, MP списывается сразу (`:100`, `:143`, `:176`), `HitTimeMs` только уезжает в пакет `BuildCast` (`:196`–`212`, поле `:209`).

### Расхождение

- `D18` **`high`** — урона по таймеру нет: `HitTimeMs` не используется для отложенного применения (искал `HitTimeMs` — единственные вхождения: `ZoneWorldSkills.cs:209` (запись в пакет), `Messages.cs:984` (поле), `SkillDefinition.cs:27,46`, `SkillCatalog.cs:85,108`). Скилл с кастом 1.7 с бьёт мгновенно.
- `D19` **`high`** — прерывания каста нет: `calcAtkBreak` не портирован (искал `Interrupt`, `CalcAtkBreak`), поэтому нет ни файла вроде `ZoneWorldCastInterrupt.cs`, ни флага «кастую». Удар по кастеру ничего не сбивает.
- `D20` **`medium`** — нет `mpInitialConsume` (списание на старте каста) и повторной проверки MP на момент попадания; в `SkillDefinition` (`src/Fineage.Game/Skills/SkillDefinition.cs:7`–`80`) есть только `MpConsume` и `ReuseDelayMs`, полей `coolTime`, `mpInitialConsume`, `isMagic`, `isStatic` нет.
- `D21` **`medium`** — перезарядка не масштабируется статами: `player.SkillReuseUntilMs[skillId] = now + template.ReuseDelayMs` (`ZoneWorldSkills.cs:102`, `:145`, `:178`) — чистый XML-делей без `MAGIC_REUSE_RATE`/`P_REUSE`.
- `D22` **`medium`** — проверки мира/LOS/peace zone на момент попадания отсутствуют: цель проверяется только при запросе (`ZoneWorldSkills.cs:94`–`97`), а мир-зоны у нас не существует вовсе (см. `M3`).
- `D23` **`low`** — нет SPS/BSS и их влияния на каст/урон (`Formulas.java:826`, `Creature.java:1718`); расходников зарядки в проекте нет.

---

## 5. Стат-пайплайн

### Mobius

`gameserver/model/actor/stat/CreatureStat.java:79` — `calcStat(stat, init, target, skill)`: берёт `Calculator` из массива по `stat.ordinal()` и прогоняет все `AbstractFunction` по порядку `_order` (док в `gameserver/model/stats/functions/AbstractFunction.java:40`–`47`; вставка с сортировкой по `getOrder()` — `gameserver/model/stats/Calculator.java:118`–`135`, прогон — `:199`–`204`). Порядок: add/sub → mul/div → нелинейные.

Геттеры на этом построены: `getPAtk` (`CreatureStat.java:509`, с клампом `Config.MAX_PATK`), `getPAtkSpd` (`:526`), `getPDef` (`:540`), `getMAtk` (`:269`), `getMDef` (`:341`), `getAccuracy` (`:132`). Отсюда же работает grade penalty: `Player.refreshExpertisePenalty` (`Player.java:2017`–`2070`) считает штраф по кристаллу надетых вещей и вешает скилл `GRADE_PENALTY` (`:2061`), который сам является набором `Func` — то есть штраф **применяется к статам автоматически**.

### У нас

Статы класса — статичные поля `src/Fineage.Game/ClassTemplate.cs:60`–`70` (`PAtk`, `MAtk`, `PDef`, `MDef`, `Accuracy`, `CritRate`, `PAtkSpd`, `MAtkSpd`, `RunSpeed`, `WalkSpeed`) с геттерами `:104`–`123`. Загрузчик валидирует их наличие (`src/Fineage.Game/ClassCatalog.cs:327`–`333`).

Штраф грейда считается корректно — `src/Fineage.Game/GradePenalty.cs:25`–`52` (та же логика, что `Player.java:2017`–`2070`) — и уходит клиенту полями `WeaponGradePenalty`/`ArmorGradePenalty`/`GradePenalty` (`src/Fineage.Protocol/Messages.cs:280`–`288`, сборка `src/Fineage.Zone/InventoryMessages.cs:26`–`47`), пересчёт при смене вещей — `src/Fineage.Zone/World/ZoneWorldGear.cs:234`–`245`.

### Расхождение

- `D24` **`high`** — `calcStat`/`Calculator` отсутствуют: динамического пересчёта статов нет (искал `calcStat`, `Calculator`, `StatFunction`, `ZoneWorldStats`, `SkillStatEffects` — ни одного файла). Баффы `ZoneAbnormals` хранят только id/значение/срок (`src/Fineage.Zone/Combat/ZoneAbnormalEntry.cs`), но ни на один стат не влияют.
- `D25` **`high`** — статы класса в бою не читаются вообще: `PAtk`, `MAtk`, `PDef`, `MDef`, `Accuracy`, `CritRate` не встречаются ни в `src/Fineage.Zone/World/ZoneWorldCombat.cs`, ни в `ZoneWorldSkills.cs` (искал `.PAtk`, `.PDef`, `.MAtk`, `.MDef` по `src/Fineage.Zone` — совпадений нет). Персонаж 1-го и 40-го уровня бьют одинаково.
- `D26` **`high`** — grade penalty посчитан, но не применён: это прямо записано в TODO `src/Fineage.Game/GradePenalty.cs:9`–`15` («skill 4267 is not applied to stats… combat, movement, and regen are unchanged»). В Mobius штраф идёт через `calcStat` и режет скорость атаки, уклонение, реген, exp и точность на 20.
- `D27` **`medium`** — бонусы экипировки не суммируются: `ItemTemplate.PDef` (`src/Fineage.Game/ItemTemplate.cs:18`) заполняется из XML (`src/Fineage.Game/Mobius/MobiusCatalogImporter.cs:216`–`217`), но зона его нигде не складывает в статы (искал `.PDef` по `src/Fineage.Zone` — нет). Надевание брони не меняет получаемый урон.
- `D28` **`medium`** — `PAtkSpd`/`MAtkSpd` не влияют ни на что; `RunSpeed`/`WalkSpeed` только передаются клиенту в `PlayerSpawnedMsg`/`UserInfoMsg`/`NpcInfoMsg` (`src/Fineage.Zone/World/ZoneWorld.cs:614`–`625`, `:627`–`640`) — сервер движение не валидирует по скорости, только по шагу 8000 (`ZoneWorld.cs:361`, `src/Fineage.Zone/TalkingIsland.cs:18`). Скорости, поправленные в соседней ветке, в `dev` не отражены — здесь их нет как механики.
- `D29` **`low`** — `MaxHp`/`MaxMp` считаются только из таблицы класса (×бонус CON/MEN на 1-м уровне, `ClassTemplate.cs:147`–`163`) и пересчитываются лишь при левелапе (`src/Fineage.Zone/World/ZoneWorldQuests.cs:213`–`221`). Бафф на MaxHP невозможен.

---

## 6. Реген HP/MP

### Mobius

`Formulas.getRegeneratePeriod` (`Formulas.java:96`) — 3 с (двери — 5 мин). `calcHpRegen` (`:245`) — базовый реген от шаблона и уровня, множители `HP_REGEN_MULTIPLIER`, чемпион, Seven Signs festival (`:261`), осада (`:267`–`271`), кланхолл-функция `FUNC_RESTORE_HP` (`:275`–`284`), затем `Stat.REGENERATE_HP_RATE` из баффов/эквипа. `calcMpRegen` (`:342`) — аналогично. Отдельно считается CP-реген и остановка регена в бою (`Creature.java:2357` `_status.stopHpMpRegeneration()`).

### У нас

`src/Fineage.Zone/Combat/ZoneRegen.cs:9`–`21`: `DefaultTickMs = 3000`; `HpPerTick = max(1, maxHp/64 + level/12)`, сидя ×2; `MpPerTick = max(1, maxMp/32 + level/10)`, сидя ×2. Применяется в `src/Fineage.Zone/World/ZoneWorldRegen.cs:49`–`74` — по списку **игроков** (`:51`), тик из `ZoneSimulationSettings` (`src/Fineage.Zone/ZoneSimulationSettings.cs:7`).

### Расхождение

- `D30` **`high`** — мобы и боссы не регенерируются вообще: цикл идёт только по `Players()` (`ZoneWorldRegen.cs:51`). Раненый моб остаётся раненым навсегда, пока не умрёт.
- `D31` **`medium`** — формула `maxHp/64 + level/12` не имеет отношения к `calcHpRegen`: нет CON/MEN, нет баффов, нет зон, осады, кланхолла, нет остановки регена в бою (в `TickRegen` нет проверки `CombatUntilUnixMs`). Раненый в бою игрок лечится тем же тиком.
- `D32` **`low`** — CP-регена нет (CP как механики нет, см. `D3`).
- `D33` **`medium`** — реген не зависит от веса/перегруза и от состояния (лёжа/сидя) кроме удвоения; `refreshOverloaded`-аналога нет, поэтому штрафной вариант регена недостижим.

---

## 7. Агро и ИИ мобов

### Mobius

`gameserver/ai/AttackableAI.java`:

- `thinkActive` (`:436`–`632`): перебор видимых целей → `autoAttackCondition` (`:552`) → `addDamageHate` при нулевом hate (`:558`–`577`) → выбор самого ненавистного (`:583`, `getMostHated` в `Attackable.java:874`) → `AI_INTENTION_ATTACK` при `aggro + _globalAggro > 0` (`:589`–`600`); забывание агро при полном HP/MP (`:604`–`610`); возврат домой при уходе за `MAX_DRIFT_RANGE` (`:626`–`630`, `config/NPC.ini:106` = 300);
- `autoAttackCondition` (`:168`–`311`): инвул, двери (`:191`), мёртвые, радиус агро (`:198`), silent move (`:203`), GM-флаг (`:214`–`219`), guard-правила (`:242`–`257`), friendly mob (`:257`–`270`), chaos/clan (`:277`–`288`), **peace zone** (`:297`–`302`, `ALT_MOB_AGRO_IN_PEACEZONE` = true, `config/NPC.ini:21`), `isAggressive() && GeoEngine.canSeeTarget` (`:310`);
- `thinkAttack` (`:732`–`796`): проверка ухода от спавна по `AggroDistanceCheck*` (`:740`–`795`, `config/NPC.ini:111` = False), при срабатывании — `clearAggroList` и возврат с восстановлением HP/MP (`:752`–`755`);
- `onEvtAttacked` (`:2323`–`2371`): таймаут атаки, `addDamageHate(attacker, 0, 1)`, переключение интента, помощь миньонам (`:2357`–`2368`).

Hate-модель: `Attackable.addDamage` (`Attackable.java:700`), `addDamageHate` (`:740`), `reduceHate` (`:796`), `getMostHated` (`:874`).

### У нас

ИИ нет. Моб — пассивная сущность `ZoneEntity` (`src/Fineage.Zone/World/ZoneWorld.cs:27`–`97`) без hate-полей. Агрессию моб не проявляет никогда: единственный путь урона по нему — входящий `AttackRequest` (`src/Fineage.Zone/World/ZoneWorldCombat.cs:62`–`110`). Ни `ZoneWorldAggro.cs`, ни `ZoneWorldMobAi.cs` не существует (искал оба имени и `Aggro|Hate` по `src/` — совпадений нет).

### Расхождение

- `D34` **`high`** — hate-листа нет: моб не запоминает, кто его бил, и не выбирает цель. Нет `addDamageHate`/`getMostHated`, нет переключения цели.
- `D35` **`high`** — нет `autoAttackCondition`: моб не агрится по радиусу, не проверяет LOS, не различает guard/friendly/chaos, не учитывает peace zone. Агрессивные мобы из `data/spawns/*.json` (`Attackable = true`) ведут себя как манекены.
- `D36` **`high`** — нет `thinkAttack` → нет возврата домой, нет «дрейфа» за игроком, нет восстановления HP при уходе от спавна. Моб не двигается вообще (в симуляции нет шага движения мобов, только реген и спавны — `ZoneWorldRegen.cs:32`–`39`).
- `D37` **`medium`** — нет помощи клана при убийстве: `Creature.doDie` (`Creature.java:2375`–`2390`) обзванивает соседей по `template.getClanHelpRange()`; у нас ничего.
- `D38` **`medium`** — нет миньонов: ни `onAssist` (`AttackableAI.java:2357`), ни `onMinionDie`/`onMasterDie` (`Attackable.java:309`–`325`), ни `MINIONS_RESPAWN_TIME` (`Config.java:738`). Модели петов/самонов нет (см. `M7`).
- `D39` **`low`** — нет `_globalAggro`-механики (запрет мгновенного агро сразу после спавна, `AttackableAI.java:2331`–`2335`) и нет «забывания» агро при полном HP/MP.

---

## 8. Награды за убийство (exp/SP)

### Mobius

`Attackable.calculateRewards` (`gameserver/model/actor/Attackable.java:350`–`470`): суммирует урон по `_aggroList`, отбрасывает цели дальше `ALT_PARTY_RANGE` (`:388`, `Config.java:1890` = 1500), считает урон по партиям (`:408`–`455`), определяет `maxDealer` (`:400`–`404`), затем `calculateExpAndSp` (`:1347`) — exp пропорционален доле урона (`getExpReward() × damage / totalDamage`, `:1355`) с отсечкой по `MONSTER_EXP_MAX_LEVEL_DIFFERENCE` (`:1353`) и штрафом за разницу уровней (`:1358`–`1365`); SP аналогично. Over-hit даёт бонус (`:526`–`530`), `EXPSP_RATE` применяется через `calcStat` (`:535`–`536`), саммон-пенальти (`:500`).

Партия: `calculateRewards` распределяет на участников в known-области (`:620`–`640`), `Party.ItemDistribution` определяет лут.

### У нас

`src/Fineage.Zone/World/ZoneWorldCombat.cs:147`–`180` (`RemoveDeadNpc`): при смерти моба только роллятся дропы (`:168`–`171`, `SpawnDeathDrops` `:172`/`:179`). Опыт и SP не начисляются: в `ZoneWorldCombat.cs` нет обращений к `Exp`/`Sp` (искал `Exp`, `Sp` по `src/Fineage.Zone/World` — совпадения только в `ZoneWorldQuests.cs:195`–`224`, то есть только за квесты). Модель `_aggroList`/урона по участникам отсутствует — `ZoneEntity` не хранит вклад в урон.

### Расхождение

- `D40` **`high`** — убийство моба не даёт ни опыта, ни SP. Прокачка возможна только квестами (`ZoneWorldQuests.cs:195`–`224`) — то есть гейм-луп «фарм мобов» отсутствует.
- `D41` **`high`** — нет распределения награды по урону: нет `totalDamage`, нет `maxDealer`, нет отсечки по дистанции.
- `D42` **`medium`** — нет штрафа/бонуса за разницу уровней (`calculateExpAndSp`, `:1347`–`1365`): даже когда exp появится, гриф на серых мобах не будет ограничен.
- `D43` **`medium`** — нет партийного распределения exp/SP и лута: партия существует только как приглашения/чат (`src/Fineage.Zone/World/ZoneWorldParty.cs:16`–`21`), поле `ItemDistribution` хранится (`:19`, `:33`, `:129`–`146`) и ни на что не влияет (в файле нет `Exp`, `Loot`, `Damage` — проверено).

---

## 9. Квесты

### Mobius

`gameserver/model/quest/Quest.java`:

- `notifyKill` (`:563`–`577`) → `onKill` (`:1033`); `notifyTalk` (`:582`–`602`) → проверка `getStartConditionHtml` (`:586`–`591`) → `onTalk` (`:1044`); `onFirstTalk` (`:1066`); `notifyEvent` (`:525`) и `notifyEventReceived` (`:801`) — событийная шина;
- партийный кредит: `getRandomPartyMemberState` (`:2288`–`2331`) — случайный участник партии, прошедший условия и проверку дистанции до цели (`checkDistanceToTarget`), с весом `playerChance` для инициатора;
- квестовые дропы подмешиваются в дроп моба через `NpcTemplate.calculateDrops` (`gameserver/model/actor/templates/NpcTemplate.java:649`), квест-предметы отдельным инвентарём.

### У нас

`src/Fineage.Zone/World/ZoneWorldQuests.cs`: `Talk` (`:109`–`137`) — `FirstTalk`/`Talk`/`AdvEvent`, `Publish` (`:150`–`193`), `ApplyProgress` (`:195`–`224`). Движок — `src/Fineage.Game/Quests/QuestMachine.cs`: `FirstTalk` (`:24`), `Talk` (`:41`), `AdvEvent` (`:51`), `Abort` (`:61`), `OnKill` (`:86`–`129`).

`OnKill` реализован, но не вызывается из боя — это зафиксировано в коде: `src/Fineage.Zone/World/ZoneWorldCombat.cs:149` («Kill quest steps are not applied here. IQuestKillHook is the later combat entry.»), `src/Fineage.Game/Quests/IQuestKillHook.cs:7`–`10` и `src/Fineage.Protocol/QuestProtocol.cs:19`.

### Расхождение

- `D44` **`high`** — `onKill` не подключён: интерфейс `IQuestKillHook` объявлен, `QuestMachine` его реализует, но ни один вызов из `RemoveDeadNpc` не идёт. Квесты «убей N мобов» невозможны.
- `D45` **`high`** — партийного кредита нет: `OnKill` работает с одним `QuestJournal` (`QuestMachine.cs:86`), распределения на партию нет (искал `Party` в `QuestMachine.cs` — ни одного вхождения). В партии квест засчитается только тому, кто добил.
- `D46` **`medium`** — квестовых дропов нет: `DropRoller.RollDeath` (`src/Fineage.Zone/Drops/DropRoller.cs:5`–`26`) роллит только группы из `data/drops/<npc>.json`; квестовых предметов в дропе моба не бывает (файл один — `data/drops/npc-20001-gremlin.json`).
- `D47` **`low`** — нет событийной шины `notifyEvent`/`notifyEventReceived` (`Quest.java:525`, `:801`): только `AdvEvent` по bypass (`ZoneWorldQuests.cs:122`–`124`).
- `D48` **`low`** — нет `getStartConditionHtml` на разговоре (`Quest.java:586`–`591`): у нас первый разговор идёт напрямую в `FirstTalk` (`ZoneWorldQuests.cs:123`), стартовое условие показывается внутри него, а не заменяет диалог.

---

## 10. Инвентарь и вес

### Mobius

- `Creature.getMaxLoad` (`Creature.java:6278`–`6286`): `CON_bonus × 69000 × ALT_WEIGHT_LIMIT` (`config/Character.ini:24` — в этом датапаке `AltWeightLimit = 10`), затем `calcStat(Stat.WEIGHT_LIMIT)`.
- `Player.refreshOverloaded` (`Player.java:1970`–`2013`): весовые пороги 50/66.6/80/100% → уровни 1–4, скилл `4270` (`:2003`), `EtcStatusUpdate`.
- `PlayerInventory.validateWeight` (`gameserver/model/itemcontainer/PlayerInventory.java:872`–`880`) — отказ при перегрузе, если не GM в diet-режиме; `validateCapacity` (`:863`–`866`) — лимит слотов (обычные и квестовые отдельно).
- Каждый предмет в `ItemTemplate` имеет `weight`; `refreshWeight` (`PlayerInventory.java:747`–`751`) вызывает `refreshOverloaded`.

### У нас

`src/Fineage.Game/ItemTemplate.cs:7`–`28` — запись из 15 полей, **поля веса нет**. `src/Fineage.Game/PlayerInventory.cs` — работа с сумкой: `AddPieces` (`:68`–`80`), `Add` (`:142`–`170`), `TryTake` (`:88`–`115`), `Equip` (`:249`–`326`), `TryConsume` (`:333`–`358`). Ни одного ограничения по весу или количеству слотов: искал `Weight`, `MaxLoad`, `Overload` по `src/` — совпадения только `ZoneOverload.cs`/`LoginOverload.cs` (это **ограничители входящих подключений**, не вес).

### Расхождение

- `D49` **`high`** — веса нет: ни в шаблоне предмета, ни в инвентаре, ни в каталоге (`data/items.json`). Персонаж может нести бесконечно много.
- `D50` **`high`** — `refreshOverloaded` и штраф 4270 (1–4) отсутствуют; в Mobius они режут скорость, реген и шанс уклонения.
- `D51` **`medium`** — лимита слотов инвентаря нет (`validateCapacity` не портирован): можно держать неограниченное число разных предметов.
- `D52` **`low`** — отдельного лимита/инвентаря квестовых предметов нет (`getQuestInventoryLimit`); квест-предметы лежат в общей сумке и учитываются в `CountOf`.

---

## 11. Торговля и склад

### Mobius

- Обмен: `gameserver/model/TradeList.java` — `confirm` (`:366`–`418`) с двухфазным подтверждением и **детерминированным порядком блокировок** (`:383`–`398`, по `objectId`), `validate` (`:440`) с проверкой веса и слотов получателя (`:720`, `:726`), `doExchange`.
- Склад: `gameserver/model/itemcontainer/PlayerWarehouse.java`, `ClanWarehouse.java`, `PlayerFreight.java` поверх `Warehouse.java`; обработчики `SendWareHouseDepositList.java`, `SendWareHouseWithDrawList.java`.
- Приватные магазины: `TradeList.privateStoreSell` (`:830`–`748+`), пакеты `RequestPrivateStoreBuy/Sell/Manage*` в `gameserver/network/clientpackets/`, крафт — `Recipes.xml` + `ManufactureItem`.

### У нас

Обмен реализован: `src/Fineage.Zone/World/ZoneWorldTrade.cs` — `TryTradeRequest` (`:118`–`145`), `TryTradeAnswer` (`:147`–`194`), `TryTradeAddItem` (`:196`–`253`), `TryTradeDone` (`:250`–`290`), `TryExecuteTrade` (`:292`–`335`), `ValidateOffers` (`:337`–`349`). Дистанция 150 (`src/Fineage.Protocol/TradeProtocol.cs:7`), максимум 8 строк предложения (`:9`).

Склад, магазин, телепорт, баффер — заглушки: `src/Fineage.Zone/NpcHtml/BypassRegistry.cs:63`–`74` регистрирует `StubBypassHandler` для `Shop` (`Buy`, `Sell`, `Multisell`, `exc_multisell`, `Wear`, `Exchange`), `Teleport` (5 команд), `Warehouse` (9 команд: `DepositP`, `WithdrawP`, `DepositC`, `WithdrawC`, `DepositF`, `WithdrawF`, `package_deposit`, `package_withdraw`), `Buff`. Ответ заглушки — `src/Fineage.Zone/NpcHtml/BypassHandlers.cs:114`–`117`: «{Service} is not implemented yet.»

### Расхождение

- `D53` **`stub`** — склад (личный, клановый, freight) — заглушка: `BypassRegistry.cs:66`–`74`, обработчика нет, классов-контейнеров склада в `src/` нет (искал `Warehouse` — только имена команд).
- `D54` **`stub`** — приватные магазины и крафт — заглушка: `BypassRegistry.cs:63`; пакетов `PrivateStore*` в `src/Fineage.Protocol/` нет.
- `D55` **`medium`** — при обмене не проверяются вес и слоты получателя (`TradeList.validate:720`–`730`): `ValidateOffers` (`ZoneWorldTrade.cs:337`) проверяет только наличие, не-надетость и количество.
- `D56` **`medium`** — нет запретов Interlude на обмен (quest-item, no-trade/shadow, «в олимпиаде/приватном магазине нельзя») и нет двухфазной блокировки как в `TradeList.confirm:383`–`398`: у нас весь обмен идёт под общим `lock (_gate)` зоны (`ZoneWorldTrade.cs:40`, `:58`, `:76`, `:94`), что корректно по гонкам, но правила «что нельзя» не перенесены.

---

## 12. Дроп и спойл

### Mobius

- `NpcTemplate.calculateDrops` (`gameserver/model/actor/templates/NpcTemplate.java:649`) — групповые и негрупповые дропы, `RATE_DROP_CHANCE_BY_ID`/`RATE_DROP_AMOUNT_BY_ID` (`:717`, `:1003`), premium-множители.
- `Attackable.doItemDrop` (`Attackable.java:1034`–`1180`): раздача в инвентарь при `AUTO_LOOT`, иначе создание предмета на земле (`dropItem`), спец-ивенты.
- Спойл/свип как игровые скиллы: `Attackable.isSpoiled` (`:1503`), `takeSweep` (`:1180`), `checkSpoilOwner` (`:1219`), `isOldCorpse` (`:1200`), `takeHarvest` (`:1189`).
- Защита дропа: `gameserver/model/DropProtection.java:35` — 15 с эксклюзивного права поднять (владелец, его партия, пет).
- Время жизни предмета на земле: `taskmanager/ItemsAutoDestroyTaskManager.java`.

### У нас

`src/Fineage.Zone/Drops/DropRoller.cs` — `RollDeath` (`:5`–`26`) по группам с `PassPercent` и `PickWeighted`, `RollCorpse` (`:29`–`49`). Спавн на земле — `src/Fineage.Zone/World/ZoneWorldGround.cs:47`–`92`; подбор — `TryPickup` (`:25`–`45`). Спойл/свип — только админские команды: `src/Fineage.Zone/World/ZoneWorldSpoil.cs:39`–`57` (`TryAdminSpoil`) и `:59`–`83` (`TryAdminSweep`), включаются `MsgType.AdminSpoilTarget`/`AdminSweepCorpse` (`src/Fineage.Zone/ZoneServer.cs:370`, `:397`).

### Расхождение

- `D57` **`stub`** — спойл/свип есть как каркас (ролл корпса `ZoneWorldCombat.cs:166`–`176`, труп `CorpseAwaitingSweep`), но игровых скиллов `Spoil`/`Sweep` нет: единственный вход — админ-пакет. Обычный игрок спойлить не может.
- `D58` **`medium`** — защиты дропа нет: `TryPickup` (`ZoneWorldGround.cs:25`–`45`) не проверяет владельца/партию/время (аналог `DropProtection.java:35`). Любой, кто в радиусе 900 (`ZoneWorldGround.cs:38` через `ZoneCombat.MeleeRange`), забирает чужой дроп.
- `D59` **`medium`** — датапак дропов почти пуст: один файл `data/drops/npc-20001-gremlin.json` на весь сервер; групповые дропы Mobius, per-item rate, premium, event-drop не перенесены. `DropRates` (`src/Fineage.Zone/Drops/DropRates.cs`) — глобальные множители, без per-id.
- `D60` **`low`** — у предмета на земле нет времени жизни: `ZoneWorldGround.cs` не содержит таймера удаления (искал `AutoDestroy`, `LifeTime`) — мусор копится до рестарта.

---

## 13. Спавны и респавн

### Mobius

`gameserver/data/SpawnTable.java:60` + `Spawn.java`: спавн привязан к территории (`<territory>` с узлами и `minZ/maxZ`), `count` NPC на спавн, случайная точка внутри территории, `respawnDelay`/`respawnRandom`, домашняя точка для возврата, `WalkingManager` (`gameserver/instancemanager/WalkingManager.java:57`, данные `data/Routes.xml` — 15 маршрутов) для патрулей.

### У нас

`src/Fineage.Zone/Spawns/SpawnCatalog.cs` — загрузчик читает **плоский** список `npcs` (`ReadPending` `:162`–`205`, DTO `:230`–`246`: `objectId`, `npcId`, `x/y/z`, `heading`, `respawnDelaySeconds`, `respawnRandomSeconds`). Импортёр из Mobius XML, наоборот, пишет **`territories`**: `src/Fineage.Game/Mobius/MobiusCatalogImporter.cs:235`–`252` (`zone`, `minZ`, `maxZ`, `nodes`, `npcs` с `count`). Ключа `territories` в `data/spawns/*.json` нет ни в одном файле (проверено grep по каталогу), а `NpcRowDto` его не знает — `System.Text.Json` молча игнорирует.

Материализация — `src/Fineage.Zone/World/ZoneWorldSpawns.cs`: `BootstrapSpawns` (`:20`–`47`), `MaterializeSpawn` (`:49`–`74`), `ScheduleRespawn` (`:76`–`87`), `TickSpawns` (`:89`–`105`).

### Расхождение

- `D61` **`stub`** — импорт территорий написан, но не читается: `territories` игнорируется загрузчиком, поэтому спавны Mobius грузятся только как фиксированные точки (и это отражено в тексте самого импортёра: `MobiusCatalogImporter.cs:237` — «Territory spawns; fixed coordinates are a later slice»).
- `D62` **`medium`** — нет случайной точки в территории, нет `count > 1` на строку (поле `count` есть в XML-ридере `src/Fineage.Game/Mobius/MobiusSpawnXmlReader.cs:8`, но в JSON-схему спавна не попадает), нет walking routes/патрулей.
- `D63` **`low`** — респавн считается от `objectId`-слота (`ZoneWorldSpawns.cs:76`–`87`), а не от смерти: у Mobius задержка привязана к спавну и может отличаться по инстансу/зоне. Домашней точки для возврата нет.

---

## Что есть в Mobius, но у нас нет

| Механика | Где в Mobius | Почему важно | Критичность |
|---|---|---|---|
| `M1` Двери | `gameserver/model/actor/instance/Door.java:55`, данные `data/Doors.xml` (593 двери) | без дверей нет замков, кланхоллов, катакомб: любая осада и любой инстанс не запирается | `high` |
| `M2` Инстансы | `gameserver/instancemanager/InstanceManager.java:41` (`createInstanceFromTemplate` `:355`) | нет отдельных копий мира: ни рифта, ни камушка, ни рейд-инстансов | `high` |
| `M3` Мир-зоны (29 типов) | `gameserver/instancemanager/ZoneManager.java`, `gameserver/model/zone/type/*.java` (Peace, Water, Swamp, Boss, Siege, Jail, MotherTree, NoRestart, NoStore, Damage, Effect, Fishing, Hq, Derby, Arena, OlympiadStadium, Script, Condition, …) | нет peace zone → нет запрета PvP у города, нет безопасных зон, нет воды/болота, нет зон урона; на этом висят агро (`AttackableAI.java:297`), каст (`Creature.java:5431`), реген (`Formulas.java:275`), restarts (`RequestRestartPoint.java:105`) | `high` |
| `M4` Территории и патрули | `gameserver/data/SpawnTable.java:60`, `gameserver/instancemanager/WalkingManager.java:57`, `data/Routes.xml` | спавн «россыпью» по территории и ходячие NPC — база ощущения живого мира | `high` |
| `M5` Олимпиада | `gameserver/model/olympiad/Olympiad.java:58` | нет арены 1×1/3×3, нет Noblesse-цикла, нет Hero | `high` |
| `M6` Осады (боевая часть) | `gameserver/instancemanager/SiegeManager.java`, `CastleManager.java`; у нас `src/Fineage.Zone/World/ZoneWorldSiege.cs` — только регистрация/расписание (`:44`–`70`) | осада не имеет боя: нет флагов, нет урона по воротам/кристаллам, нет захвата через смерть | `stub` |
| `M7` Петы и самоны | `gameserver/model/actor/instance/Pet.java:75`, `gameserver/model/actor/Summon.java` | нет пета-компаньона, нет саммонов-классов (Warlock/Elemental Summoner), нет `PetSkillData.xml` | `high` |
| `M8` Кубики | `gameserver/model/actor/instance/Cubic.java:47` | часть классов (Summoner/Necromancer) без кубиков недееспособна | `medium` |
| `M9` Дуэли | `gameserver/model/Duel.java:48` | нет согласованного PvP без кармы; нужен и для ивентов | `medium` |
| `M10` Рыбалка | `gameserver/model/fishing/Fishing.java:34` | отдельный гейм-луп и `FishingZone` | `low` |
| `M11` Манор / семена | `data/Seeds.xml`, `gameserver/model/Seed.java`, `CropProcure.java`, `SeedProduction.java` | экономика замков и крафт-цепочка | `medium` |
| `M12` Проклятое оружие | `gameserver/instancemanager/CursedWeaponsManager.java:58`, `data/CursedWeapons.xml` | PvP-контент с потерей оружия при смерти (`Player.java:4862`) | `medium` |
| `M13` AutoPlay | `gameserver/taskmanager/AutoPlayTaskManager.java:42` | автофарм/авто-нюк, в этой сборке — часть клиент-серверного контракта | `low` |
| `M14` Оффлайн-трейд | `gameserver/util/OfflineTradeUtil.java:36` | рынок живёт без онлайна продавца | `medium` |
| `M15` Трупы и распад | `gameserver/taskmanager/DecayTaskManager.java:34` | труп — цель для `Sweep`/`Harvest`/ресурректа; без него спойл-цикл неполон | `medium` |
| `M16` Dimensional Rift | `data/DimensionalRift.xml`, `gameserver/model/DimensionalRift.java` | партийный инстанс-данж | `medium` |
| `M17` Рецепты и крафт | `data/Recipes.xml`, `gameserver/model/RecipeList.java`, `ManufactureItem.java` | крафт — половина экономики Interlude | `medium` |
| `M18` Смена класса / сабклассы / Noblesse | `data/SkillLearn.xml`, `gameserver/model/actor/instance/ClassMaster.java`; у нас `src/Fineage.Game/Skills/PlayerSkillBook.cs:29`–`33` прямо отмечает, что NPC-скиллы выдаются «до появления RequestAcquireSkill» | нет 1-й/2-й профессии, сабклассов, Noblesse — верхняя половина контента недостижима | `high` |
| `M19` Community board | `gameserver/communitybbs/` | нет встроенной доски объявлений/телепортов/рынка | `low` |
| `M20` Аугментация | `data/AugmentationData.java`, `data/EnchantItemOptions.xml` | нет Lifestone-аугментации — часть эндгейм-прогрессии | `low` |

**Не найдено в этой сборке** (проверял по именам классов в `java/org/l2jmobius/gameserver/`): `Manor`, `AttendanceReward`, `FishingChampionManager`. Раздел «Attendance» из вашего списка в этом дереве отсутствует — утверждать его наличие у Mobius не могу.

---

## Топ-10 приоритетов

1. `D4` + `D34`–`D36` — бой и ИИ: игрок не умирает от мобов, мобы не агрятся и не двигаются; без этого нет ни PvE, ни гринда.
2. `D8`–`D14` — физ. формула, промахи, криты, щит, соулшоты и скорость атаки вместо константы 32.
3. `D24`–`D26` — стат-пайплайн `calcStat` и применение grade penalty; сейчас статы класса и штраф грейда не влияют ни на что.
4. `D18`–`D19` — каст по таймеру и прерывание каста: сейчас скиллы мгновенные и несбиваемые.
5. `D40`–`D43` — exp/SP за убийство с распределением по урону и партии: без этого нет прокачки.
6. `D1`–`D3` — потери опыта, Death Penalty 5076 и CP: без них смерть и PvP бессмысленны.
7. `D44`–`D45` — подключить `IQuestKillHook` и партийный кредит квестов: квесты на убийство сейчас не засчитываются.
8. `D49`–`D51` — вес (`MaxLoad`, penalty 4270) и лимиты слотов: инвентарь сейчас безграничен.
9. `M3` + `M1` — мир-зоны и двери: на них висят peace zone, агро, каст, реген и restarts.
10. `D61` — починить связку «импортёр пишет `territories` → загрузчик их не читает», иначе все спавны Mobius останутся ручными точками.
