# Сверка с L2J Mobius CT0 Interlude

Сверка логики сервера против эталонного L2J Mobius CT0 Interlude.
Документ фиксирует только расхождения. Это не план рефакторинга.

## База сверки

| Параметр | Значение |
|---|---|
| Наша ветка | `feat/net-foundation` (worktree `backend-parity-nf`, ветка `docs/parity-nf`) |
| Коммит | `a52304d` (`feat(zone): inventory weight and Weight Penalty 4270 — overload slows the pawn and halves regen`) |
| Дата сверки | 2026-11-03 |
| Эталон | `ref/mobius-ct0-interlude/java/org/l2jmobius/` |

### Почему предыдущие две версии отозваны

**Обе прежние версии этого документа отозваны как сделанные по неверной базе.**

- Версия по `dev` (коммит `03c5376`) сверялась с параллельной **старой** линией, которая разошлась
  с целевой архитектурой и отстала.
- Версия по `origin/main` (коммит `7c8e3bf`) сверялась с `main` — это **тоже старая архитектура**.

Целевая база — **`feat/net-foundation`**, и она не является ни `main`, ни `dev`. Проверено на этом
дереве:

```
$ git merge-base --is-ancestor main HEAD && echo ok      # main — чистый предок
$ git rev-list --count main..HEAD                        # 73
$ git ls-tree -r --name-only main -- src/Fineage.Net.Contract | wc -l   # 0
```

То есть `main` целиком содержится в `feat/net-foundation`, поверх него лежат **73 коммита** новой
архитектуры, а каталога `src/Fineage.Net.Contract/` на `main` **нет вообще** (сейчас там 12
отслеживаемых путей: 10 `.cs`, `.csproj`, `.gitignore`). `dev` → `feat/net-foundation` даёт 106
коммитов в одну сторону и 5 в другую — линии несовместимы.

**Следствие для метода.** Новая архитектура разложена по трём слоям, и «не найдено в старом месте»
больше не означает «не найдено»:

| Слой | Что там | Файлов/строк |
|---|---|---|
| `src/Fineage.Net.Contract/` | `NetOpcode` (92 opcode-константы), `NetCodec` (2062 строки), `NetFraming`, `PacketEnvelope`, `WorldMessages`, `IDomainModule` | 10 `.cs` |
| `src/Fineage.Zone/Net/` | `NetZoneServer` (1614), `NetEnterWorldProjector`, `NetPresenceHub` | 5 файлов |
| `src/Fineage.Zone/World/` | вся боевая логика: `ZoneWorldCombat`, `ZoneWorldAggro`, `ZoneWorldStats`, `ZoneWorldWeight`, `ZoneWorldSpoil`, `ZoneWorldCastInterrupt`, `ZoneWorldSkillDamage` … | 40+ файлов |
| `src/Fineage.Zone/Combat/` | `ZoneCombat` (446) — формулы, `SkillCombatCatalog`, `SkillStatEffects`, `ZoneRegen` | 9 файлов |
| `src/Fineage.Protocol/V2/` | старый MessagePack V2, всё ещё живой как параллельный транспорт | `Messages.cs` (1009) |

Поиск вёлся по **всем** слоям. Восемь пунктов прошлой версии снято именно потому, что механика
лежала в новом слое, а не там, где её искали.

### Ограничение этой сверки

`ref/` в `.gitignore` (строки 36–37, 45) и в этом worktree **отсутствует**. Поэтому:

- утверждения о стороне Mobius (`Player.java:4765`, `Formulas.calcPhysDam` и т. д.) **перенесены из
  аудита по `main`** как есть и заново не перепроверялись;
- заново проверена **только наша сторона** — каждое утверждение ниже подтверждается файлом и
  строкой в `feat/net-foundation`.

## Как читать

**Пути.** Ссылки на Mobius — от корня `ref/mobius-ct0-interlude/java/org/l2jmobius/` (далее
`gameserver/...`). Ссылки на наш код — от корня репозитория (`src/...`, `data/...`). Номера строк —
на момент коммита `a52304d`.

**Критичность.**

| Метка | Значение |
|---|---|
| `high` | игрок чувствует сразу и/или это ломает баланс |
| `medium` | заметно, но есть обход |
| `low` | косметика или редкий кейс |
| `stub` | каркас есть, наполнения нет |

**Область.** Проверено 13 механик (`D1`–`D63`) и 20 систем (`M1`–`M20`), плюс `N1`, `N2`.
Формулировка «не найдено» означает, что поиск по `src/` и `data/` не дал результата; в скобках —
что именно искали.

**Что НЕ проверялось.** Сетевой протокол как таковой (кодек, framing, лобби, чат), клан-меню,
осадный календарь, Seven Signs как состояние — они сравнивались только там, где влияют на боевую
механику.

## Сводка

| Механика | Mobius | У нас на `feat/net-foundation` | Расхождение | Критичность |
|---|---|---|---|---|
| 1. Смерть и респавн | `Player.doDie` → lost-exp, дебафф 5076, CP, дроп вещей | смерть от боя есть, потерь/дебаффа/CP нет | частично | `high` |
| 2. Физ. урон | `Formulas.calcPhysDam` + miss/crit/shield/ss/position | `70·PAtk/PDef`, miss, crit, shield, соулшоты, spread; стрел нет | есть, без стрел | `medium` |
| 3. Маг. урон и блоу | `calcMagicDam`, `calcBlowDamage`, `calcMagicSuccess` | `91·√MAtk/MDef·power`, блоу, lethal, mcrit/fail; легаси-фолбэк | есть, частично | `low` |
| 4. Скиллы и каст | `onMagicLaunchedTimer`/`onMagicHitTimer`, `calcAtkBreak` | hitTime + coolTime + перезарядка по статам + прерывание; нет peace zone | есть, частично | `low` |
| 5. Стат-пайплайн | `CreatureStat.calcStat` + `Calculator` | SET→MUL→ADD, баффы, пассивы `using`, вес 4270, грейд 4267 | есть, кроме 5076 | `high` |
| 6. Реген | `calcHpRegen`/`calcMpRegen`, зоны, осада, кланхолл, CP | `maxHp/64 + level/12`, стат-фолд, только игроки | частично | `medium` |
| 7. Агро и ИИ | `AttackableAI`: hate, aggro range, LOS, return home, clan help | всё перечисленное, кроме миньонов и `_globalAggro` | есть, частично | `medium` |
| 8. Награды за убийство | `calculateRewards` → exp/SP по урону и партии | exp/SP убийце, квест-кредит по партии | частично | `medium` |
| 9. Квесты | `Quest.notifyKill`/`onTalk` + party-кредит | `onKill` подключён, партийный кредит, `drop_item` | **есть** | — |
| 10. Инвентарь и вес | `MaxLoad` = CON×69000, penalty 4270, слоты | вес и штраф 4270 полностью; слоты частично | частично | `medium` |
| 11. Торговля и склад | `TradeList`, `ClanWarehouse`/`PlayerFreight`, private store | обмен, личный склад, магазины; клан/freight/лавки — нет | частично | `medium` |
| 12. Дроп и спойл | `calculateDrops`, spoil/sweep, `DropProtection` | всё перечисленное работает | **есть** | — |
| 13. Спавны | `SpawnTable` + территории + `WalkingManager` | плоский JSON, территории игнорируются | частично | `stub` |

**Итого актуальных расхождений: 54** — из них `high` **4**, `medium` **27**, `low` **20**,
`stub` **3**.

Разбивка: внутри механик (`D`-пункты + `N1`/`N2`) открыто **34** (`high` 2, `medium` 16,
`low` 15, `stub` 1), и ещё **20** систем целиком (`M1`–`M20`: `high` 2, `medium` 11, `low` 5,
`stub` 2).

Полностью закрыто **31** пункт `D`: `D1`, `D2`, `D5`, `D8`, `D9`, `D10`, `D11`, `D14`, `D18`,
`D19`, `D20`, `D21`, `D23`, `D24`, `D25`, `D28`, `D33`, `D34`, `D35`, `D37`, `D40`, `D44`, `D45`,
`D46`, `D49`, `D50`, `D52`, `D57`, `D58`, `D60`, `D63`.

---

## 1. Смерть и респавн

### Mobius

`gameserver/model/actor/Player.java:4765` — `Player.doDie`. Поверх `Creature.doDie`
(`gameserver/model/actor/Creature.java:2319`) делает:

- дроп вещей при смерти: `Player.java:4961` (`onDieDropItem`) — шансы `KARMA_RATE_DROP*`,
  `PLAYER_RATE_DROP*`, ограничение `dropLimit`, исключения (adena, quest, shadow, time-limited);
- потеря опыта: `Player.java:4898` → `calculateDeathExpPenalty` (`Player.java:5318`):
  `PlayerXpPercentLostData.getXpPercent(level)` × `Stat.REDUCE_EXP_LOST_BY_RAID/_MOB/_PVP` ×
  `RATE_KARMA_EXP_LOST`, затем `removeExp(lostExp)`; `setExpBeforeDeath` (`Player.java:4858`) и
  `restoreExp` (`Player.java:5297`) позволяют ресу вернуть часть;
- дебафф Death Penalty: `Player.java:11718` → `increaseDeathPenaltyBuffLevel` навешивает скилл
  `5076` уровня 1–15 и присылает `EtcStatusUpdate`;
- прочее: снятие кубиков, сброс кармы, `AntiFeedManager`, отключение петов/самонов.

Респавн: `Creature.doRevive` (`Creature.java:2525`) восстанавливает HP/MP/CP **не до 100%**, а до
долей из `config/Character.ini` (`RespawnRestoreHP/MP/CP`). Точка возрождения —
`gameserver/network/clientpackets/RequestRestartPoint.java:120` (`portPlayer`): кланхолл (`:145`),
замок (`:160`, `:178`), siege flag (`:214`), фиксированная точка, тюрьма.

### У нас

`src/Fineage.Zone/World/ZoneWorldDeath.cs`:

- `TryAdminKillPlayer` (`:42`) — админ-килл под флагом `_adminCommands`.
- **Смерть от боя работает**: `ResolveMeleeHit` (`src/Fineage.Zone/World/ZoneWorldCombat.cs:375`)
  проводит удар в `:396` (`target.CurHp = Math.Max(0, target.CurHp - hit.Damage)`), ветка игрока —
  `:411`–`:421`, при `CurHp <= 0` вызывается `ApplyPlayerDeath(target, attacker.ObjectId, outbound)`
  (`:421`). Мобы бьют игрока через `_meleeHits` в `TickMobAttacks`
  (`src/Fineage.Zone/World/ZoneWorldMobAi.cs:146`–`:160`), поэтому PvE-смерть достижима.
- `ApplyPlayerDeath` (`:68`): `Dead = true` (`:73`), `CurHp = 0`, сброс `Sitting`/`Running`/
  `ActiveEnchantObjectId`, `Abnormals.Clear()`, `RefreshRunSpeed` (потому что баффы `runSpd`
  снимаются вместе с абнормалами), `CombatUntilUnixMs = 0`, `FanoutPlayerDeath`, `SaveVitals`.
  Ни опыта, ни дебаффа, ни дропа вещей, ни кармы.
- `ReviveAtVillage` (`:90`): `CurHp = MaxHp`, `CurMp = MaxMp` (`:93`–`:94`) — всегда 100%,
  телепорт в `TalkingIslandRestart`, `Revive` + `TeleportToLocation` + `UserInfo`.
- CP. В `ZoneEntity` (`src/Fineage.Zone/World/ZoneWorld.cs:33`–`:300`) **по-прежнему нет** ни `CurCp`,
  ни `MaxCp`. `BuildUserInfoVitals` берёт `maxCp` из шаблона класса (`:1171`) и подставляет его
  **в оба поля**: `InventoryMessages.UserInfoVitals(…, maxCp, maxCp, …)`
  (`src/Fineage.Zone/World/ZoneWorld.cs:1196`–`:1197`). Net-проекция делает то же самое:
  `NetEnterWorldProjector.cs:114`–`:116` (`Cp = maxCp, MaxCp = maxCp`).

### Расхождение

- ~~`D1`~~ **снято** — потеря опыта при смерти портирована: `DeathRuleCatalog`
  (`src/Fineage.Game/DeathRuleCatalog.cs`) читает `data/xp-percent-lost.json`, транспонированный из
  `playerXpPercentLost.xml` скриптом `scripts/gen_xp_percent_lost.py`. `ApplyDeathExpLoss`
  (`src/Fineage.Zone/World/ZoneWorldDeathRules.cs`) повторяет `Player.calculateDeathExpPenalty`
  (`Player.java:5318-5363`): процент берётся от полосы
  `expForLevel(level + 1) - expForLevel(level)`, а не от абсолютного опыта; `removeExp`
  (`PlayableStat.java:94-124`) не пускает опыт ниже нуля и снижает уровень, когда полоса
  проваливается. Delevel пересчитывает `MaxHp`/`MaxMp`, книгу скиллов
  (`checkPlayerSkills`, `Player.java:12602-12640`), grade/weight penalty и шлёт StatusUpdate + UserInfo.
- ~~`D2`~~ **снято** — Death Penalty применяется. `RollDeathPenalty` (`ZoneWorldDeathRules.cs`)
  повторяет `Player.calculateDeathPenaltyBuffLevel` (`:11690-11720`): бросок
  `Rnd.get(1, 100) <= DeathPenaltyChance` только от смерти по мобу, затем
  `increaseDeathPenaltyBuffLevel` (`:11722-11743`) поднимает уровень до 15, шлёт `EtcStatusUpdate`
  с новым значением и SystemMessage 1916. Уровень фолдится как пассивный источник 5076
  (`FoldDeathPenalty`, `ZoneWorldWeight.cs`; `PlayerStatMods` → `ZoneWorldStats.cs:111-113`), поэтому
  `pAtk`/`pAtkSpd`/`mAtk`/`mAtkSpd`/`pDef`/`mDef`/`runSpd`/`accCombat`/`rEvas` реально падают, а
  `EtcStatusUpdateNetMsg.DeathPenalty` теперь несёт уровень, а не 0. Уровень переживает релог:
  колонка `characters.death_penalty_level` (миграция `005_character_death_penalty.sql`,
  `Player.java:337`/`:6578`), `IAccountStore.TrySaveDeathPenalty`.
- `D3` **`medium`** — CP существует только как поле отображения: `CurCp` всегда равен `MaxCp`
  (`ZoneWorld.cs:1196`–`:1197`, `NetEnterWorldProjector.cs:115`–`:116`), в `ZoneEntity` нет счётчика
  CP, урон по CP не идёт, CP-регена нет. Колонка в окне есть, механики нет.
- `D4` **`high`** — **PvP-урона нет**. `TryAttack` отбрасывает не-NPC цель
  (`src/Fineage.Zone/World/ZoneWorldCombat.cs:121`: `target.Kind != ZoneEntityKind.Npc ||
  !target.Attackable`); то же в атакующих скиллах (`ZoneWorldSkills.cs:221` и `:390`) и в дебаффах
  (`:390`). `PvpFlagged` есть у сущности (`ZoneWorld.cs:68`) и копируется из состояния экипировки
  (`ZoneWorldGear.cs:254`), но нигде не выставляется в `true` по бою; кармы/PK нет. Ветка игрока в
  `ResolveMeleeHit` (`:411`–`:421`) достижима только со стороны NPC (моб бьёт игрока). Ни дуэлей,
  ни осадного боя, ни PvP-флага.
- ~~`D5`~~ **снято** — респавн восстанавливает доли из `Character.ini`: `RestoreShare`
  (`ZoneWorldDeath.cs`) повторяет `Creature.doRevive` (`Creature.java:2524-2553`) — бар поднимается до
  `max * share`, только если он ещё ниже цели, поэтому «воскрешение», уже вылечившее больше, не
  обрезается. Значения (`RespawnRestoreHP = 65`, `RespawnRestoreMP = 0`) читаются из того же
  `data/xp-percent-lost.json`, а не зашиты в код.
- `D6` **`medium`** — один restart point вместо шести видов: `TryRevive` принимает только
  `DeathProtocol.ReviveToVillage` и отвергает остальное (`ZoneWorldDeath.cs:60`), сама константа —
  единственный action (`src/Fineage.Protocol/DeathProtocol.cs:18`). Нет кланхолла, замка, siege
  flag, тюрьмы.
- `D7` **`low`** — у игрока после смерти нет трупа (у NPC труп есть, см. `M15`); дроп вещей при
  смерти и потеря кармы не реализованы и не могут быть, пока нет кармы (`SpawnDeathDrops`
  вызывается только из `RemoveDeadNpc`, `ZoneWorldCombat.cs:494`).

---

## 2. Формулы физического урона

### Mobius

`gameserver/model/stats/Formulas.java:646` — `calcPhysDam`:

- базис `attacker.getPAtk(target) / target.getPDef(attacker)`;
- позиционный бонус `1.2` сзади, `1.1` с фланга;
- PvP-защита `Stat.PVP_PHYSICAL_DEF`;
- щит прибавляет `getShldDef()` или perfect block;
- soulshot удваивает базу, к ней прибавляется `skill.getPower(...)`;
- крит: `2 × CRITICAL_DAMAGE × …`, без крита — `76 × damage × prox / defence`;
- trait-бонус `calcAttackTraitBonus`, разброс оружия `getRandomDamageMultiplier()`, минимум 1.

Обвязка одного удара — `Creature.java` (`doAttackHitByBow`): `Formulas.calcHitMiss`
(`Formulas.java:1111`), `calcShldUse` (`:1135`), `calcCrit` (`:993`), затем `calcPhysDam`, расход
стрелы, отложенный `HitTask` по скорости атаки.

### У нас

`src/Fineage.Zone/Combat/ZoneCombat.cs` (446 строк) — расчёт целиком:

- `AttackTimeMs(pAtkSpd) = max(100, 500000 / Pspd)` (`:96`) — Mobius `calculateTimeBetweenAttacks`;
- `HitChancePermille(accuracy, evasion, conditionBonus)` (`:139`) — `(80 + 2·(acc − eva))·10 ‰`,
  clamp `200..980`;
- `HitConditionBonus` (`:179`) — `HitConditionBonusData`: back ±10, front 0, side 5, high 3, low −3,
  dark −10;
- `RollShield` (`:120`) — `rShld · DEX` (×1.3 против лука), perfect block 5% → 1 урона;
- `RollMelee` (`:199`, `:203`, `:211`) — miss → shield → crit → `70 · PAtk / PDef` (`:226`),
  крит ×2, разброс `1 + Rnd[−r, r]/100`, флаги `0x20` crit / `0x80` miss / `0x10|grade` soulshot /
  `0x40` shield;
- `AccuracyOrEvasion` (`:192`) — `base + √DEX·6 + level`;
- `MaxRunSpeed = 250` (`:38`), `MobMeleeDamage(level)` (`:391`) остался только как fallback, когда у
  NPC нет `pAtk`.

Статы: `PlayerStats` (`src/Fineage.Zone/World/ZoneWorldStats.cs:22`) и `PlayerStatMods` (`:102`).
Удар игрока — `TryAttack` (`ZoneWorldCombat.cs:103`): проверка дистанции через `PlayerReach`,
серверный `ApproachTarget` при недолёте (`:128`), `CanSeeTarget` (`:139`, SysMsg 181 «Cannot see
target»), поворот атакующего к цели (`:152`), затем `RollMelee` (`:153`–`:157`) с реальными
`PAtk`/`PDef`/`Accuracy`/`Evasion`/`CritRate`/`RandomDamage`/`ShieldRate`/`HitConditionBonus`.
Попадание отложено до `hitTime` (`:176`), разрешается в `ResolveMeleeHit` (`:375`) с повторной
проверкой досягаемости (`:383`).

### Расхождение

- ~~`D8`~~ **снято** — «урон игрока константный 32» более не верно: `ZoneWorldCombat.cs:153`
  считает через `RollMelee` от `PlayerStats(player).PAtk` и `NpcPDef(target)`, формула
  `70·PAtk/PDef` (`ZoneCombat.cs:226`). `SampleMeleeDamage` (`ZoneCombat.cs:90`) остался как
  справочная константа для тестов, в бою не используется.
- ~~`D9`~~ **снято** — промахи есть: `HitChancePermille` (`ZoneCombat.cs:139`) + флаг `HitFlagMiss`,
  ненулевой урон не применяется (`ZoneWorldCombat.cs:386`–`:393`: хейт и стойка ставятся, HP нет).
- ~~`D10`~~ **снято** — криты есть: `critPermille` в `RollMelee` (`:223`), cap `MaxCritPermille = 500`
  (`:131`), ×2 урона, флаг `0x20`; база — `crit · DEX · 10` (`ZoneWorldStats.cs:86`).
- ~~`D11`~~ **снято** — щиты есть: `RollShield` (`ZoneCombat.cs:120`), блок добавляет `sDef` к
  `PDef`, perfect block даёт 1 урона, флаг `0x40`. И у игрока (`ZoneWorldCombat.cs:156`), и у
  NPC (`:207`–`:208`).
- ~~`D12`~~ **снято** — соулшоты и стрелы на паритете. Соулшоты: `ZoneWorldShots.cs`
  (`TryChargeShot` `:35`) — зарядка по грейду оружия, расход `weapon.Soulshots`, `×2 P.Atk` на
  следующий удар, флаг `0x10|grade`, авто-перезарядка (`RequestAutoSoulShot`, opcode `0x0355`),
  спириты `×2` / blessed `×4` M.Atk в `RollSkillDamage` (`ZoneWorldSkillDamage.cs:64`–`:68`).
  Стрелы: `ZoneWorldArrows.cs` — `checkAndEquipArrows` (поиск стрелы того же грейда, что и лук,
  авто-надевание в левую руку + `InventoryUpdate`), отсутствие стрелы → `SysMsg 112`
  «You have run out of arrows.» и сброс автоатаки, `reduceArrowCount` — 1 стрела за выстрел,
  последняя стрела уничтожает стак, снимает левую руку и пересылает `ItemList`
  (`ZoneWorldCombat.cs:225`–`:238`, `:329`).
- `D13` **`low`** *(было `medium`)* — позиция частично работает: `HitConditionBonus` влияет на шанс
  попадания (back/side/front, перепад Z, ночь) и на блоу (`RollBlowDamage` proximity 1.2/1.1/1).
  Нет: позиционного множителя урона в обычном `calcPhysDam` (1.2/1.1), trait/attribute
  модификаторов, PvP/PvE-множителей (PvP не существует, см. `D4`). Разброс оружия есть.
- ~~`D14`~~ **снято** — «интервал — глобальная константа» более не верно: `AttackTimeMs(Pspd)`
  (`ZoneCombat.cs:96`), `PlayerAttackTimeMs` берёт `PlayerStats.PAtkSpd`, удар отложен на
  `timeAtk / 2` (`ZoneWorldCombat.cs:176`).

---

## 3. Магический урон и блоу

### Mobius

`Formulas.calcMagicDam` (`Formulas.java:792`): `mDef` цели с учётом щита, `mAtk` атакующего,
PvP-модификаторы, spirit/blessed spirit shot `×2`/`×4`, формула `91×sqrt(mAtk)/mDef ×
skill.getPower(...)`, затем `calcMagicSuccess` (`:1396`) с `ALT_GAME_MAGICFAILURES` и половинным
уроном при частичном успехе.

`Formulas.calcBlowDamage` (`Formulas.java:497`): отдельная формула для блоу-скиллов (позиция сзади,
крит-множитель), плюс `calcCounterDamage`.

### У нас

- `MagicDamage` (`ZoneCombat.cs:354`) — `91·√MAtk/MDef·power`;
- `RollMagicSuccess` (`:259`) — `100 − round(1.3^(targetLvl − magicLvl))`;
- `RollMagicDamage` (`:268`) — mcrit → провал → половинный урон → резист (1 урон), mcrit ×3 против
  NPC / ×2.5 против игрока;
- `SpiritshotMAtkMultiplier` (`:352`) — `×2` / `×4`;
- `RollBlowDamage` (`:311`) — `calcBlowDamage` + `calcBackstabDamage`, proximity, `ss 1.458`,
  крит ×2; `RollBlowSuccess` (`:303`), `BlowSideMod` (`:300`);
- `RollLethal` (`:336`) — full/half lethal с гейтом `magicLevel < targetLevel − 6`.

Путь: `RollSkillDamage` (`src/Fineage.Zone/World/ZoneWorldSkillDamage.cs:52`) — физические скиллы
`70·(PAtk·ss + power)/PDef` (`ZoneCombat.cs:244`), магические через `RollMagicDamage`, блоу через
`RollBlow` (`:92`). Данные — `data/skill-combat.json` (375 скиллов; `magic` у 131, `blowEffect` у 6,
`magicLevel` у 6, `usingKinds` у 41). Сообщения: 44 crit, 139 resisted, 158 attack failed,
1280 magic critical, 1668 lethal.

### Расхождение

- ~~`D15`~~ **снято, остаток `low`** — формула `Power + 10` осталась только как **legacy fallback**
  для скиллов, которых нет в `skill-combat.json` (`ZoneWorldSkills.cs:256`–`:270`,
  `LegacySkillDamageBonus = 10`). Все 375 каталогизированных атакующих скиллов идут через
  `CastMobiusAttack` (`ZoneWorldSkillDamage.cs:13`). Остаток: закрыть оставшиеся атакующие скиллы
  каталогом, чтобы fallback не срабатывал.
- `D16` **`low`** *(было `high`)* — есть блоу, mcrit, магические провалы, lethal. Нет:
  counter/reflect (поиск `reflect`, `counter` по бою — только телеметрия и `DungeonAccess`),
  mcrit для скиллов вне каталога.
- `D17` **`medium`** — нет HP-drain и обработки over-hit: `SkillLevelData` (`src/Fineage.Game/Skills/`
  ) не имеет полей эффекта, а флаг `overHit` присутствует в данных у 63 скиллов
  (`data/skill-combat.json`), но в коде не читается (`rg overHit src/*.cs` — пусто).
  Трейт/элементальных модификаторов нет.

---

## 4. Скиллы и каст

### Mobius

`Creature.doCast` (`Creature.java:1486`) → `checkDoCastConditions` → `beginCast`:

- время каста `skillTime = hitTime + coolTime`, поправка на `Formulas.calcAtkSpd`, −40% для магии
  при заряженных SPS/BSS;
- `_castInterruptTime` — окно, в котором каст можно прервать; `isCastingNow`;
- перезарядка `reuseDelay × MAGIC_REUSE_RATE` / `P_REUSE` / `DANCE_REUSE`;
- `onMagicLaunchedTimer`: проверка выхода цели за `effectRange`, LOS (`canSeeTarget`) и peace zone,
  затем `onMagicHitTimer` наносит урон;
- прерывание от урона: `Formulas.calcAtkBreak` (`Formulas.java:1029`), при `ALT_GAME_CANCEL_CAST`.

### У нас

`src/Fineage.Zone/World/ZoneWorldSkills.cs` — `TrySkillUse` (`:153`): `KnownSkills`, пассивность,
перезарядка (`SkillReuseUntilMs`), MP, затем маршрутизация в spoil/sweep (`:184`) → buff → debuff →
support → attack.

- `CastMobiusAttack` (`ZoneWorldSkillDamage.cs:13`): `mpInitialConsume` на старте каста (`:25`),
  `hitTime`/`coolTime`/`reuse` через `calcAtkSpd` (`:18`–`:20`, `ZoneCombat.cs:236`/`:240`),
  `mpConsume` на запуске (`:54`), `CastingUntilMs = hitTime + coolTime` (`:42`), урон считается в
  `onMagicHitTimer` (`:44`).
- Прерывание: `CastRules.AttackBreakRate` (`src/Fineage.Game/Combat/CastRules.cs:14`) — точный
  `calcAtkBreak` (init 15, `+√(13·dmg)`, `−(MEN·100 − 100)`, clamp 1..99), `MaybeBreakCast`
  (`src/Fineage.Zone/World/ZoneWorldCastInterrupt.cs:39`) с SysMsg 27, только magic/static
  (`CastRules.Interruptible`, `:26`).
- Проверки на запуске: цель жива, в пределах `effectRange` и в LOS — иначе `AbortCast`
  (`ZoneWorldMobAi.cs:59`–`:73`).
- Каст блокирует движение и автоатаку, цель каста «встаёт» (`StopForCast`), недолёт → серверный
  `ApproachTarget` и отложенный каст.
- Buff/debuff/heal/spoil применяются **на hitTime** через `ScheduleEffect`
  (`ZoneWorldCastInterrupt.cs:67`), а не мгновенно.

### Расхождение

- ~~`D18`~~ **снято** — `PendingSkillHit` + `TickPendingHits` реализуют `onMagicHitTimer`, и это
  распространено и на баффы/дебаффы/лечение/спойл.
- ~~`D19`~~ **снято** — прерывание каста реализовано (`CastRules.AttackBreakRate`,
  `ZoneWorldCastInterrupt.cs:39`–`:58`), включая MEN-бонус и ограничение magic/static.
- ~~`D20`~~ **снято** — `coolTime` есть: `data/skill-combat.json` содержит `coolTimeMs`,
  `CastingUntilMs = hitTime + coolTime` (`ZoneWorldSkillDamage.cs:42`); MP списывается дважды —
  `mpInitialConsume` на старте, `mpConsume` на запуске (`:25`, `:54`).
- ~~`D21`~~ **снято** — перезарядка масштабируется статами: `SkillReuseMs = reuse·333/speed`
  (`ZoneCombat.cs:240`), `speed` = Cspd для магии, Pspd для физики (`ZoneWorldSkillDamage.cs:20`).
- `D22` **`low`** *(было `medium`)* — LOS и `effectRange` на момент попадания проверяются
  (`ZoneWorldMobAi.cs:58`–`:73`, SysMsg 181 в `ZoneWorldCombat.cs:139`–`:143`). Осталось только
  **peace zone** — её физически нет, потому что нет мир-зон (см. `M3`).
- ~~`D23`~~ **снято** — SPS/BSS есть: зарядка спиритом (`ZoneWorldShots.cs:35`), множитель
  `×2`/`×4` M.Atk на магический каст, расход на запуске (`ZoneWorldSkillDamage.cs:64`–`:68`).

---

## 5. Стат-пайплайн

### Mobius

`gameserver/model/actor/stat/CreatureStat.java:79` — `calcStat(stat, init, target, skill)`: берёт
`Calculator` по `stat.ordinal()` и прогоняет `AbstractFunction` по порядку `_order`
(`gameserver/model/stats/Calculator.java:118`–`:135`, прогон `:199`–`:204`). Порядок:
add/sub → mul/div → нелинейные.

Геттеры построены на этом: `getPAtk`, `getPAtkSpd`, `getPDef`, `getMAtk`, `getMDef`, `getAccuracy`.
Отсюда же grade penalty: `Player.refreshExpertisePenalty` (`Player.java:2017`) вешает скилл
`GRADE_PENALTY`, который сам является набором `Func` — штраф применяется к статам автоматически.

### У нас

`src/Fineage.Zone/World/ZoneWorldStats.cs`:

- `PlayerStats` (`:22`) — `P.Atk = (weapon pAtk | fist)·STR·levelMod`; `M.Atk = (weapon | base)·INT²·
  levelMod²`; `P.Def = (80 − доля нагого слота + armor pDef)·levelMod` с раздельными нагими долями
  для fighter/mystic и покрытием ног full-body; `M.Def = (41 − доли украшений + jewel mDef)·MEN·
  levelMod`; `Pspd = (weapon | 300)·DEX`; `Cspd = base·WIT`; `accuracy = √DEX·6 + level`;
  `crit = base·DEX·10`; щит даёт `sDef`, `rShld·DEX` и штраф к evasion (`:74`–`:92`).
- `PlayerStatMods` (`:102`) — баффы из `Abnormals` + пассивные мастери **с условием `using`**
  (тип оружия / тип брони / SHIELD) + виртуальный источник Weight Penalty 4270.
- `CombatStatMods.Apply` (`src/Fineage.Zone/Combat/SkillStatEffects.cs:62`) — Mobius-порядок
  `SET` 0 → `MUL` 20 → `ADD`/`SUB` 30.
- Единый путь скорости: `RunSpeedStat`/`WalkSpeedStat`/`SwimRunSpeedStat` (`:116`–`:145`) →
  `FoldSpeed` (`:163`): `FuncMoveSpeed` (×DEX) → XML-функции → `+RUN_SPD_BOOST` → cap
  `MaxRunSpeed = 250` только игрокам; нижнего порога нет — 0 легален (Weight Penalty 4).
- `RefreshRunSpeed` (`:201`) пересчитывает кэш и сообщает, изменилось ли что-то, чтобы выслать
  `ChangeMoveType`.
- Данные: `skill-stat-effects.json` (446 скиллов), `skill-passives.json` (188), `stat-bonuses.json`.

### Расхождение

- ~~`D24`~~ **снято** — `calcStat`-аналог есть, со стат-модификаторами баффов и пассивов.
- ~~`D25`~~ **снято** — статы читаются в бою: `PlayerStats` используется в `TryAttack`
  (`ZoneWorldCombat.cs:148`), в `RollSkillDamage` (`ZoneWorldSkillDamage.cs:55`), в
  `NpcMeleeRoll` (`ZoneWorldCombat.cs:207`–`:209`) и в `BuildUserInfoVitals`
  (`ZoneWorld.cs:1179`). Персонаж 1-го и 40-го уровня бьют по-разному.
- ~~`D26`~~ **снято** — Grade Penalty 4267 теперь **применяется к статам**. `RefreshGradePenalty`
  (`ZoneWorldGear.cs:262`) пишет `WeaponGradePenalty`/`ArmorGradePenalty` и вычисленный из них
  `GradePenaltyLevel` (`max(weapon, armor)`, как `addSkill(4267, …)` в `Player.java:2059`), а
  `FoldGradePenalty` (`ZoneWorldWeight.cs`) фолдит функции 4267 в общий `CombatStatMods`-пайплайн —
  ровно так же, как это уже было сделано для 4270 (`FoldWeightPenalty`). Уровни 1–4 множат
  `pAtkSpd`/`mAtkSpd`/`rEvas`/`runSpd`/`regHp`/`regMp` на 0.22/0.19/0.16/0.13 и вычитают 20 accuracy,
  поэтому персонаж в не по чину высоком грейде реально замедляется и слабеет. Пересчёт идёт на
  надевании/снятии (`ZoneWorldGear.cs:191`), на заточке (`ZoneWorldEnchant.cs:251`), на входе в мир
  (`ZoneWorld.cs:753`) и на левелапе (`ZoneWorldQuests.cs:368`). Тесты —
  `tests/Fineage.Zone.Tests/GradePenaltySkillTests.cs`.
- `D27` **`low`** *(было `medium`)* — бонусы экипировки **суммируются** (`ZoneWorldStats.cs:44`–`:72`),
  `pAtk` заполнен у всех 152 оружий, `pDef` — у 55 из 87 броней. Остаток: наполнить `pDef` у 32
  броней и `mDef` у украшений, иначе нагие доли отрабатывают вместо реальных значений.
- ~~`D28`~~ **снято** — `PAtkSpd` задаёт темп ударов (`ZoneCombat.cs:96`), `MAtkSpd` — hit/cool/reuse
  каста (`:236`, `:240`), `RunSpeed`/`WalkSpeed` идут через полный `runSpd`-пайплайн с DEX,
  баффами, пассивами, штрафом 4270 и cap 250. Валидация клиентского `MoveTo` — через
  `ValidatePosition` (opcode `0x0350`) → `PositionCorrection` (`NetZoneServer.cs:533`–`:540`).
- `D29` **`low`** — `MaxHp`/`MaxMp` берутся только из таблицы класса (`ZoneWorldRegen.cs:175`,
  пересчёт при левелапе `ZoneWorldQuests.cs:320`–`:321`, при смене класса
  `ZoneWorldVillageMasters.cs:137`–`:138`). `CombatStat` не содержит `MaxHp`, поэтому бафф на
  максимум HP невозможен.
- `N2` **`low`** — при левелапе HP/MP выставляются в новый максимум
  (`player.CurHp = player.MaxHp; player.CurMp = player.MaxMp`, `ZoneWorldQuests.cs:322`–`:323`).
  У Mobius левелап добавляет дельту максимума к текущему значению, а не лечит до полного.

---

## 6. Реген HP/MP

### Mobius

`Formulas.getRegeneratePeriod` (`Formulas.java:96`) — 3 с. `calcHpRegen` (`:245`) — базовый реген
от шаблона и уровня, множители `HP_REGEN_MULTIPLIER`, чемпион, Seven Signs festival, осада,
кланхолл-функция `FUNC_RESTORE_HP`, затем `Stat.REGENERATE_HP_RATE` из баффов/эквипа.
`calcMpRegen` (`:342`) — аналогично. Отдельно считается CP-реген и остановка регена в бою
(`Creature._status.stopHpMpRegeneration()`).

### У нас

`src/Fineage.Zone/Combat/ZoneRegen.cs:11`–`:21`: `DefaultTickMs = 3000`;
`HpPerTick = max(1, maxHp/64 + level/12)`, сидя ×2; `MpPerTick = max(1, maxMp/32 + level/10)`,
сидя ×2.

`TickRegen` (`src/Fineage.Zone/World/ZoneWorldRegen.cs:60`) — цикл **только по игрокам** (`:62`),
но базовые ставки прогоняются через `PlayerStatMods` (`:73`, `:76`): `Apply(CombatStat.RegHp, …)` и
`Apply(CombatStat.RegMp, …)`. Дробный остаток переносится между тиками (`RegenCarryHp/Mp`,
`:87`–`:99`), потому что витали целочисленные, а Mobius держит их `double`.

### Расхождение

- ~~`D30`~~ **снято** — реген мобов есть: `TickNpcRegen` (`ZoneWorldRegen.cs:107`) в том же 3-секундном
  тике, ставки `hpRegen`/`mpRegen` берутся из `<vitals>` Mobius-датапака (`data/npcs.json`, поля
  `hpRegen`/`mpRegen`, импортёр `tools/import_npc_templates.py`), `max(1, rate)` на тик, потолок —
  максимумы, дробный остаток переносится (`RegenCarryHp/Mp`). Раненый моб больше не остаётся
  раненым навсегда. Полное лечение при возврате домой — отдельно: Mobius лечит только на
  leash-ресете (`AttackableAI.java:749`), а `Attackable.returnHome()` (`:1685`) не лечит; теперь так же
  (`TickLeash`/`TryLeashReset`, `ZoneNpcSettings`). Выключить можно `FINEAGE_NPC_REGEN_ENABLED=false`.
- `D31` **`medium`** *(переписано)* — базовая формула по-прежнему `maxHp/64 + level/12`
  (`ZoneRegen.cs:11`), без CON/MEN, зон, осады и кланхолла. Но баффы и мастери теперь на неё
  влияют через стат-фолд, и Weight Penalty 4270 честно режет реген вдвое. Осталось: остановка
  регена в бою — `TickRegen` не смотрит `CombatUntilUnixMs` (поиск по `ZoneWorldRegen.cs` пуст),
  поэтому раненый в бою игрок лечится тем же тиком.
- `D32` **`low`** — CP-регена нет (CP как счётчика нет, см. `D3`).
- ~~`D33`~~ **снято** — штраф перегруза на реген работает: `FoldWeightPenalty`
  (`src/Fineage.Zone/World/ZoneWorldWeight.cs:146`) добавляет функции 4270 (`regHp`/`regMp` ×0.5) в
  общий `CombatStatMods`, а `TickRegen` их применяет (`ZoneWorldRegen.cs:73`–`:78`).

---

## 7. Агро и ИИ мобов

### Mobius

`gameserver/ai/AttackableAI.java`:

- `thinkActive` (`:436`): перебор видимых целей → `autoAttackCondition` → `addDamageHate` при
  нулевом hate → выбор самого ненавистного (`getMostHated`) → `AI_INTENTION_ATTACK`; забывание
  агро при полном HP/MP; возврат домой за `MAX_DRIFT_RANGE`;
- `autoAttackCondition` (`:168`): инвул, двери, мёртвые, радиус агро, silent move, GM-флаг,
  guard-правила, friendly mob, chaos/clan, peace zone, `isAggressive() && GeoEngine.canSeeTarget`;
- `thinkAttack` (`:732`): проверка ухода от спавна, `clearAggroList` и возврат с восстановлением
  HP/MP;
- `onEvtAttacked` (`:2323`): `addDamageHate(attacker, 0, 1)`, переключение интента, помощь миньонам;
- `_globalAggro` (`:112`, `:128`) — запрет мгновенного агро после спавна.

Hate-модель: `Attackable.addDamageHate` (`Attackable.java:740`), `getMostHated`, `reduceHate`.

### У нас

`src/Fineage.Zone/World/ZoneWorldAggro.cs`:

- `DamageHate(damage, npcLevel) = max(1, damage·100/(level+7))` (`:20`) — точная Mobius-формула;
- `AddHate` (`:22`) — словарь `HateList` на NPC, `MostHated` (`:40`) выбирает валидную цель с
  максимальным hate и чистит протухшие записи;
- `ValidHateTarget` (`:34`) — живой игрок в том же инстансе и в пределах `AggroForgetRange`;
- `CanSeeTarget` (`:65`) — сначала закрытые двери, затем `GeoEngine.canSeeTarget`;
- `TickAggression` (`:81`) — сканирование раз в 500 мс: `Aggressive && AggroRange > 0`, 3D-радиус,
  LOS, затем `AddHate`; флаг и радиус берутся из `data/npcs.json` (4928 шаблонов с `aggroRange`,
  2841 с `aggressive`), парсинг — `src/Fineage.Zone/Npcs/NpcCatalog.cs:147`, `:167`;
- `CallClanHelp` (`:109`) — простаивающие мобы того же клана в `clanHelpRange` получают 1 hate;
- `TickAttackStance` (`:128`) — Net `AutoAttackStart/Stop`.

`TickMobAttacks` (`src/Fineage.Zone/World/ZoneWorldMobAi.cs:94`): протухшая цель выкидывается, берётся
следующая по hate (`:111`–`:118`), при отсутствии целей — `ReturnHome` (`:119`). Погоня —
`ChaseStep` (`:167`) через общий таймер движения, с `MoveToAck`, run-режимом и запоминанием
домашней точки (`:184`–`:188`).

### Расхождение

- ~~`D34`~~ **снято** — hate-модель есть: словарь с числами, `damage·100/(lvl+7)`, выбор
  `MostHated`, падение на следующего по hate при потере цели. Нет только `reduceHate`
  (естественного затухания).
- ~~`D35`~~ **снято** — проактивное агро есть: `TickAggression` (`ZoneWorldAggro.cs:81`) с 3D-радиусом
  и LOS, флаг `Aggressive` действительно читается. Остаток не про агро, а про дискриминацию
  целей: guard/friendly/chaos/peace zone не различаются (`M3`).
- `D36` **`low`** *(было `medium`)* — погоня, домашняя точка и возврат домой есть
  (`ChaseStep` `:167`, `HomeSet` теперь ставится на спавне (`ZoneWorldSpawns.cs:116`),
  `ReturnHome` `ZoneWorldNpcMovement.cs:50`). Поводок по расстоянию сделан
  (`TickLeash`/`TryLeashReset`, `ZoneNpcSettings`): `AggroDistanceCheckRange` 2000, рейды 4000,
  GrandBoss пропускается, на ресете HP/MP по `AggroDistanceCheckRestoreLife`. Но в шипнутом
  `NPC.ini` `AggroDistanceCheckEnabled = False`, поэтому по умолчанию он выключен — как в Mobius;
  остаётся `AggroForgetRange = 3000` (`ZoneCombat.cs:365`) как радиус «забывания» цели.
- ~~`D37`~~ **снято** — помощь клана есть (`CallClanHelp`, `ZoneWorldAggro.cs:109`) с
  `clanHelpRange`, списком кланов и `IgnoreNpcIds`.
- `D38` **`medium`** — миньонов нет: поиск `Minion` по `src/` пуст, нет `onAssist`/`onMinionDie`/
  `onMasterDie`, нет `MINIONS_RESPAWN_TIME`. Модели петов/самонов тоже нет (см. `M7`).
- `D39` **`low`** — нет `_globalAggro` (запрет мгновенного агро после спавна) и нет «забывания»
  агро при полном HP/MP. Поиск `GlobalAggro` по `src/` пуст.

---

## 8. Награды за убийство (exp/SP)

### Mobius

`Attackable.calculateRewards` (`Attackable.java:350`): суммирует урон по `_aggroList`, отбрасывает
цели дальше `ALT_PARTY_RANGE`, считает урон по партиям, определяет `maxDealer`, затем
`calculateExpAndSp` (`:1347`) — exp пропорционален доле урона с отсечкой по
`MONSTER_EXP_MAX_LEVEL_DIFFERENCE` и штрафом за разницу уровней; SP аналогично. Over-hit даёт
бонус, `EXPSP_RATE` применяется через `calcStat`, есть саммон-пенальти.

### У нас

`GrantKillReward` (`src/Fineage.Zone/World/ZoneWorldQuests.cs:589`) → `DistributeKillReward` (`:601`):
суммирует `ZoneEntity.DamageBy` (заполняется в `RecordDamage`, `ZoneWorldAggro.cs:26`, из
`ResolveMeleeHit` и `ResolvePendingHit`), отбрасывает урон ≤ 1, мёртвых и цели дальше
`QuestPartyRange` = 1600 (`ALT_PARTY_RANGE`). `KillRewardFormula.CalculateExpAndSp`
(`src/Fineage.Zone/Rewards/KillRewardFormula.cs:47`) повторяет Mobius: доля урона в целочисленной
арифметике, отсечка `±MonsterExpMaxLevelDifference` (`ZoneRewardSettings`, `Rates.ini` = 40, env
`FINEAGE_MONSTER_EXP_MAX_LEVEL_DIFFERENCE`), множители 0.97…0.03 для 85+ на серых мобах. Партия
считается в `SharePartyKill` (`:646`) — бонус `Party.BONUS_EXP_SP`, деление `level² / Σ level²`,
отсечка `PartyXpCutoffLevel` = 20. Каждому получателю `SendKillReward` (`:705`) шлёт
`SystemMessage 95`, `StatusUpdate`, `UserInfo`, `PlayerStats`, сохраняет прогресс и запускает
`ApplyLevelUp`. Вызывается из `ResolveMeleeHit` (`ZoneWorldCombat.cs:484`) и `ResolvePendingHit`
(`ZoneWorldMobAi.cs:90`).

### Расхождение

- ~~`D40`~~ **снято** — награда есть, гейм-луп «фарм мобов» работает.
- ~~`D41`~~ **снято** — exp/SP делятся по доле урона (`DamageBy`, отсечка 1600 по дистанции),
  последний удар больше ничего не решает.
- ~~`D42`~~ **снято** — отсечка `MonsterExpMaxLevelDifference` и множители для 85+ на серых мобах
  есть в `KillRewardFormula`, юнит-тесты в `tests/Fineage.Zone.Tests/KillRewardTests.cs`.
- ~~`D43`~~ **снято** для exp/SP — партийный пул с бонусом `Party.BONUS_EXP_SP`, делением по
  `level²` и `PartyXpCutoffLevel` есть (`SharePartyKill`). Остаток: партийного распределения лута
  (`ItemDistribution` по-прежнему только хранится), over-hit бонуса, `EXPSP_RATE`/премиум-множителей
  и саммон-пенальти нет.

---

## 9. Квесты

### Mobius

`gameserver/model/quest/Quest.java`:

- `notifyKill` (`:563`) → `onKill` (`:1033`); `notifyTalk` с `getStartConditionHtml` (`:587`) →
  `onTalk` (`:1044`); `onFirstTalk` (`:1066`); `notifyEventReceived` (`:801`);
- партийный кредит: `getRandomPartyMemberState` (`:2288`) — случайный участник партии, прошедший
  условия и проверку дистанции, с весом `playerChance` для инициатора;
- квестовые дропы подмешиваются в дроп моба через `NpcTemplate.calculateDrops`.

### У нас

`src/Fineage.Zone/World/ZoneWorldQuests.cs`: `Talk` (`:110`) — `FirstTalk`/`Talk`/`AdvEvent`,
`Publish`, `ApplyProgress`. Движок — `src/Fineage.Game/Quests/QuestMachine.cs`: `FirstTalk`,
`Talk`, `AdvEvent`, `Abort`, `OnKill` (`:90`), `PartyKillQuests` (`:137`), `PartyKillApplies`
(`:159`), `OnPartyKill` (`:175`).

**`onKill` подключён**: `ApplyQuestKill` (`ZoneWorldQuests.cs:157`) вызывается из
`GrantKillReward` (`:437`), а тот — из боя (`ZoneWorldCombat.cs:406`, `ZoneWorldMobAi.cs:90`).
Партийный кредит: шаги с `party: true` раздаются одному случайному подходящему участнику партии
в радиусе `QuestPartyRange = 1600` (`:182`), включая убийцу (`:164`–`:179`); тесты —
`tests/Fineage.Zone.Tests/NetQuestKillTests.cs`.

Данные: `data/quests.json` — 11 квестов, 24 `drop_item`-операции в `ops` (`give_item` 50,
`take_item` 44, `set_cond` 26, `complete` 18, `drop_item` 16, `give_exp_sp` 15, `give_adena` 15,
`create`/`start` по 10, `set` 8, `sell_items` 2, `exchange_all` 1). Kill-шаги есть у Q267
(npc 20325), Q313 (20509), Q316 (20040, `party: true`).

### Расхождение

- ~~`D44`~~ **снято** — `onKill` вызывается из боевого пути. **Важно:** комментарии в коде устарели
  и противоречат факту — `ZoneWorldCombat.cs:447` («Kill quest steps are not applied here»),
  `src/Fineage.Protocol/QuestProtocol.cs:19`, `src/Fineage.Game/Quests/IQuestKillHook.cs`. Их надо
  поправить, иначе следующий аудит снова сделает ложный вывод.
- ~~`D45`~~ **снято** — партийный кредит есть (`ZoneWorldQuests.cs:164`–`:179`, `QuestPartyRange`
  `:182`, `PartyKillApplies` в `QuestMachine.cs:159`).
- ~~`D46`~~ **снято** — квестовые дропы есть: операция `drop_item` с `chance`/`limit`/`condAtLimit`
  (`QuestCatalog.cs:231`, `:247`) и выдача напрямую в сумку.
- `D47` **`low`** — нет событийной шины `notifyEvent`/`notifyEventReceived`: поиск `NotifyEvent` по
  `src/` пуст, есть только `AdvEvent` по bypass (`ZoneWorldQuests.cs:131`).
- `D48` **`low`** — нет `getStartConditionHtml` на разговоре: поиск `StartCondition` по `src/` пуст;
  стартовое условие показывается внутри `FirstTalk`, а не заменяет диалог.

---

## 10. Инвентарь и вес

### Mobius

- `Creature.getMaxLoad` (`Creature.java:6278`): `CON_bonus × 69000 × ALT_WEIGHT_LIMIT`, затем
  `calcStat(Stat.WEIGHT_LIMIT)`.
- `Player.refreshOverloaded` (`Player.java:1970`): весовые пороги 50/66.6/80/100% → уровни 1–4,
  скилл `4270`, `EtcStatusUpdate`.
- `PlayerInventory.validateWeight` / `validateCapacity` — отказ при перегрузе и лимит слотов;
  `refreshWeight` (`:749`) вызывает `refreshOverloaded`.

### У нас

Вес и штраф **реализованы полностью**:

- `MaxLoadOf` (`src/Fineage.Zone/World/ZoneWorldWeight.cs:42`) — `floor(CON bonus·69000·AltWeightLimit)`
  и поверх `calcStat(WEIGHT_LIMIT)`; `BonusWeightPenaltyOf` (`:33`) — `WEIGHT_PENALTY` (сеты брони);
- `WeightPenalty.Level` (`src/Fineage.Game/WeightPenalty.cs`) — пороги 500/666/800/1000 ‰,
  Java-деление;
- `RefreshWeightPenalty` (`ZoneWorldWeight.cs:61`) — пересчёт, `RefreshRunSpeedAndFanout`, затем
  `EtcStatusUpdate` (`:127`, поле `WeightPenalty`);
- `FoldWeightPenalty` (`:146`) — функции 4270 (`runSpd` ×1/×0.5/×0.5/×0, `regHp`/`regMp` ×0.5)
  фолдятся в общий пайплайн как виртуальный пассивный источник, а не записью в книгу скиллов;
  обоснование — в комментарии `:17`–`:21`;
- пересчёт на каждое изменение сумки — через `SaveInventory` → `RefreshWeightPenaltyAfterInventoryChange`
  (`:110`);
- проверки веса: магазин (`ZoneWorldShops.cs:148`, `:219`), склад/депозит
  (`ZoneWorldNpcServices.cs:465`), свип (`ZoneWorldSpoil.cs:143`).

Слоты: `WarehouseRules.InventorySlots` (80 / 100 для дварфа,
`src/Fineage.Game/Services/WarehouseRules.cs:18`–`:19`), `ShopTrade.SlotLimit`
(`src/Fineage.Game/Shops/ShopTrade.cs:28`). Проверяются при покупке/мультиселле
(`ShopTrade.cs:128`, `:285`), при депозите (`ZoneWorldNpcServices.cs:458`), при свипе
(`ZoneWorldSpoil.cs:135`).

Квестовые предметы: `ItemTemplate.QuestItem` (`src/Fineage.Game/ItemTemplate.cs:43`) →
`CanDropTradeStore` (`:49`) / `CanSell` (`:54`); 54 предмета помечены `quest` в `data/items.json`;
блокировки стоят на выбрасывании (`ZoneWorldGround.cs:223`), обмене (`ZoneWorldTrade.cs:219`),
депозите и продаже (`ZoneWorldNpcServices.cs:364`, `:590`).

### Расхождение

- ~~`D49`~~ **снято** — вес в шаблоне, инвентаре, каталоге и проверках есть.
- `N1` **`medium`** — вес проверяется **только** в магазине, складе и свипе. Не проверяется:
  подбор с земли (`TryPickup`, `src/Fineage.Zone/World/ZoneWorldGround.cs:99`, тело
  `:148`–`:172` — ни `MaxLoadOf`, ни слотов), обмен (`ValidateOffers`,
  `ZoneWorldTrade.cs:342`–`:354`), квестовая выдача в сумку (`Publish`, `ZoneWorldQuests.cs:235`).
  Перегруз вне магазина недостижим.
- ~~`D50`~~ **снято** — `refreshOverloaded` и штраф 4270 есть, включая `EtcStatusUpdate`, пороги и
  влияние на скорость/реген. Тест — `tests/Fineage.Zone.Tests/WeightPenaltyTests.cs`.
- `D51` **`low`** *(было `medium`)* — лимит слотов есть, но не везде: магазин, депозит и свип
  проверяют, подбор с земли и обмен — нет. Держать 200 разных предметов, подбирая их с пола,
  по-прежнему можно.
- ~~`D52`~~ **снято** — квестовые предметы помечены и заблокированы для дропа/трейда/продажи/склада.

---

## 11. Торговля и склад

### Mobius

- Обмен: `gameserver/model/TradeList.java` — `confirm` (`:366`) с двухфазным подтверждением и
  детерминированным порядком блокировок по `objectId`, `validate` с проверкой веса и слотов
  получателя, `doExchange`.
- Склад: `PlayerWarehouse.java`, `ClanWarehouse.java`, `PlayerFreight.java` поверх `Warehouse.java`.
- Приватные магазины: `TradeList.privateStoreSell`, пакеты `RequestPrivateStoreBuy/Sell/Manage*`,
  крафт — `data/Recipes.xml` + `ManufactureItem`.

### У нас

- Обмен: `ZoneWorldTrade.cs` — запрос/ответ/добавление/готовность, **двухфазное подтверждение**
  (`RequestorConfirmed`/`PartnerConfirmed`, `:278`–`:284`), `TryExecuteTrade` (`:299`),
  `ValidateOffers` (`:342`).
- Магазины NPC работают: `ShopBypassHandler` (Buy, Sell, Multisell, exc_multisell, Exchange),
  `data/buylists.json`, `data/multisell.json`, налог города.
- Личный склад работает: `WarehouseBypassHandler` (DepositP/WithdrawP), `PrivateWarehouse`,
  пошлина и вес (`ZoneWorldNpcServices.cs:458`–`:465`).
- Заглушки: `src/Fineage.Zone/NpcHtml/BypassRegistry.cs:65` — `Wear` (нет окна примерки),
  `:68` — `Teleport showNoblesSelect`, `:71`–`:78` — `DepositC`, `WithdrawC`, `DepositF`,
  `WithdrawF`, `package_*`.

### Расхождение

- `D53` **`medium`** — клановый и freight-склад остались заглушкой: `BypassRegistry.cs:71`–`:78`,
  обработчика нет, классов-контейнеров `ClanWarehouse`/`Freight` в `src/` нет (поиск пуст, кроме
  константы `NpcServiceProtocol.FreightDeferred`). Личный склад работает.
- `D54` **`medium`** — нет приватных магазинов и крафта: поиск `PrivateStore` по `src/` пуст,
  `RecipeList`/`ManufactureItem` не портированы (`Recipe` встречается только как тип записи в
  `AccountStore.cs:47`). Магазины NPC работают.
- `D55` **`medium`** — при обмене не проверяются вес и слоты получателя: `ValidateOffers`
  (`ZoneWorldTrade.cs:342`–`:354`) проверяет только наличие, не-надетость и количество.
- `D56` **`low`** *(было `medium`)* — двухфазное подтверждение и запрет квестовых предметов уже
  есть (`:278`–`:284`, `:219`). Осталось: проверка веса/слотов получателя и запреты на
  shadow/no-trade (поиск `shadow` по `ItemTemplate.cs`/`ZoneWorldTrade.cs` пуст).

---

## 12. Дроп и спойл

### Mobius

- `NpcTemplate.calculateDrops` — групповые и негрупповые дропы, `RATE_DROP_CHANCE_BY_ID`/
  `RATE_DROP_AMOUNT_BY_ID`, premium-множители.
- `Attackable.doItemDrop` (`:1034`): раздача в инвентарь при `AUTO_LOOT`, иначе предмет на земле.
- Спойл/свип как игровые скиллы: `isSpoiled` (`:1503`), `takeSweep`, `checkSpoilOwner`,
  `takeHarvest`.
- Защита дропа: `gameserver/model/DropProtection.java`.
- Время жизни предмета на земле: `taskmanager/ItemsAutoDestroyTaskManager.java`.

### У нас

- Ролл: `src/Fineage.Zone/Drops/DropRoller.cs` — `RollDeath` по группам с `PassPercent` и
  `PickWeighted`, `RollCorpse`; множители `DropRates` (`DeathDropChanceMultiplier` и т. д.).
- Спавн на землю: `SpawnDeathDrops` → `SpawnGroundStack`, разброс ±70 (`DeathDropScatter`,
  `ZoneWorldGround.cs:57`), подбор — `TryPickup` (`:99`) с догоном до предмета
  (`PickupReach = 36`).
- **Защита дропа есть**: `DropProtectMs = 15_000` (`ZoneWorldGround.cs:51`),
  `CanPickupProtected` (`:63`) — владелец, его партия; отказ → SysMsg 145.
- **Время жизни есть**: `GroundItemAutoDestroyMs = 600_000` (`:54`), `HerbAutoDestroyMs = 60_000`
  (`:56`), `TickGroundExpiry` (`:80`).
- **Спойл/свип полноценный**: `TrySkillUseSpoilOrSweep` (`ZoneWorldSpoil.cs:164`) маршрутизируется
  из обычного `TrySkillUse` (`ZoneWorldSkills.cs:184`); `ApplySpoil` (`:75`) — условия 109/357,
  `calcMagicSuccess` с magicLevel из `data/spoil.json`, SysMsg 612/139, hate на спойлера;
  `ApplySweep` (`:110`) — 343/683/1247, права владельца/партии, вес и слоты (`:135`–`:145`),
  `ConsumeBody` через `DespawnNpc`. Время трупа — 7 с, +10 с для заспойленного
  (`data/spoil.json`: `corpseTimeMs` 7000, `spoiledCorpseExtendMs` 10000,
  `sweepAllowedBeforeDecayMs` 2000).
- Гербы: `data/herbs.json`, автоиспользование при подборе (`ZoneWorldHerbs.cs`).

### Расхождение

- ~~`D57`~~ **снято** — спойл/свип доступны обычному игроку, с правами владельца и партии.
- ~~`D58`~~ **снято** — защита дропа есть (15 с, владелец/партия).
- `D59` **`medium`** *(переписано)* — датапак дропов вырос, но всё ещё осколочный: **22 файла**
  `data/drops/npc-*.json`, 182 группы дропа, против 6519 шаблонов NPC. Нет per-item rate,
  premium/event-дропов; `DropRates` — только четыре глобальных множителя
  (`src/Fineage.Zone/Drops/DropRates.cs`).
- ~~`D60`~~ **снято** — у предмета на земле есть время жизни, травы живут 60 с, обычный дроп 600 с.

---

## 13. Спавны и респавн

### Mobius

`gameserver/data/SpawnTable.java` + `Spawn.java`: спавн привязан к территории (`<territory>` с
узлами и `minZ/maxZ`), `count` NPC на спавн, случайная точка внутри территории, `respawnDelay`/
`respawnRandom`, домашняя точка для возврата, `WalkingManager` для патрулей (`data/Routes.xml`).

### У нас

`src/Fineage.Zone/Spawns/SpawnCatalog.cs` читает **плоский** список `npcs`: `objectId`, `npcId`,
`x/y/z`, `heading`, `respawnDelaySeconds`, `respawnRandomSeconds` (`:220`, `:241`, `:264`).
В `data/spawns/` **188 файлов, 34 112 записей**, из них 187 карт включены.

Проверено по данным: `rg -l "territor" data/spawns/` → 0, `rg -l '"count"' data/spawns/` → 0.

Импортёр из Mobius XML, наоборот, читает территории и `count`:
`src/Fineage.Game/Mobius/MobiusSpawnXmlReader.cs:13`, `:32`–`:41` (`territory`, `minZ`, `maxZ`,
`nodes`, `count`), и передаёт их в `MobiusCatalogImporter.cs:235`–`:252`. В JSON-схему спавна они
не попадают.

Материализация — `src/Fineage.Zone/World/ZoneWorldSpawns.cs`: `BootstrapSpawns` (`:25`),
`MaterializeSpawn` (`:67`), `ScheduleRespawn` (`:119`), `TickSpawns`.

### Расхождение

- `D61` **`stub`** — импорт территорий написан, но не читается: ключей `territories`/`count` нет ни
  в одном файле `data/spawns/`, поэтому спавны Mobius грузятся только фиксированными точками.
- `D62` **`medium`** — нет случайной точки в территории, нет `count > 1` на строку, нет walking
  routes/патрулей: поиск `patrol`, `route`, `waypoint` находит только комментарии про geo-path в
  `GeoPathFinding.cs:6`–`:7`, а не патрульные маршруты NPC.
- ~~`D63`~~ **снято** — респавн считается корректно от смерти: `DespawnNpc` → `ScheduleRespawn`
  (`ZoneWorldCombat.cs:517`, `ZoneWorldSpawns.cs:119`) с задержкой и джиттером из определения
  спавна; домашняя точка для возврата домой теперь тоже есть (`HomeSet`, `ZoneWorldMobAi.cs:184`).

---

## Что есть в Mobius, но у нас нет

| Механика | Где в Mobius | Почему важно | Критичность |
|---|---|---|---|
| `M1` Двери | `gameserver/model/actor/instance/Door.java`, `data/Doors.xml` | **Уточнение:** коллизия дверей есть — `data/doors.json` (593 двери, конвертация `tools/import_doors_water.py`), `DoorTable` и `FirstBlocking` в `src/Fineage.Zone/Geo/MoveZones.cs:27`–`:79`, проверка в `ClipDoors` (`ZoneWorldMovement.cs:326`) и в `CanSeeTarget` (`ZoneWorldAggro.cs:65`). Нет: сущности двери на Net, открытия/закрытия (door state — только datapack default, комментарий `MoveZones.cs:26`) | `medium` |
| `M2` Инстансы | `gameserver/instancemanager/InstanceManager.java` | **Уточнение:** изоляция по `InstanceId` есть (`ZoneWorld.cs:283`, `:547`, `:569`, `:845`), арены Олимпиады, входы в катакомбы. Нет шаблонов инстансов и копий мира | `stub` |
| `M3` Мир-зоны (29 типов) | `gameserver/instancemanager/ZoneManager.java`, `model/zone/type/*.java` | нет peace zone → нет запрета PvP у города, нет безопасных зон, зон урона, осадных зон; на этом висят агро (`D35`), каст (`D22`), реген (`D31`), restarts (`D6`). Водные зоны **есть** (`data/water-zones.json`, 431) и дают swim-скорость | `high` |
| `M4` Территории и патрули | `SpawnTable.java`, `WalkingManager.java`, `data/Routes.xml` | спавн «россыпью» по территории и ходячие NPC. Дублирует `D61`/`D62` | `medium` |
| `M5` Олимпиада | `gameserver/model/olympiad/Olympiad.java` | **Уточнение:** регистрация, матчмейкинг, стадионы, очки, цикл и `Report` есть (`OlympiadBoard.cs:151`, `:167`, `:341`). Нет боёв (нет PvP-урона, `D4`), нет Hero; `GrantNoblesse` (`:69`) не вызывается никем (поиск по `src/` даёт только объявление и обёртку `ZoneWorldOlympiad.cs:28`) | `medium` |
| `M6` Осады (боевая часть) | `SiegeManager.java`, `CastleManager.java` | регистрация, расписание, владелец, `CaptureCastle` есть (`ZoneWorldSiege.cs:13`); нет флагов, урона по воротам/кристаллам, захвата через смерть | `stub` |
| `M7` Петы и самоны | `instance/Pet.java`, `Summon.java` | поиск `Pet`/`Summon`/`TamedBeast` по бою и `src/Fineage.Zone` пуст — нет пета-компаньона, нет саммонов-классов, нет `PetSkillData.xml` | `high` |
| `M8` Кубики | `instance/Cubic.java` | поиск `Cubic` по `src/` пуст; часть классов (Summoner/Necromancer) без кубиков недееспособна | `medium` |
| `M9` Дуэли | `model/Duel.java` | поиск `Duel` по `src/` пуст; нужен и для ивентов, и как вход в PvP без кармы | `medium` |
| `M10` Рыбалка | `model/fishing/Fishing.java` | есть только тип оружия `FishingRod` и проверка `Lure` в offhand (`PlayerInventory.cs:592`); гейм-лупа нет | `low` |
| `M11` Манор / семена | `data/Seeds.xml`, `Seed.java`, `CropProcure.java` | поиск пуст; экономика замков и крафт-цепочка | `medium` |
| `M12` Проклятое оружие | `CursedWeaponsManager.java` | есть только гейт Олимпиады по id предмета (`ZoneWorldOlympiad.cs:346`, Зарич/Акаманах); самих проклятых оружий, механики потери при смерти нет | `medium` |
| `M13` AutoPlay | `taskmanager/AutoPlayTaskManager.java` | поиск `AutoPlay` пуст | `low` |
| `M14` Оффлайн-трейд | `util/OfflineTradeUtil.java` | поиск `OfflineTrade`/`offline` пуст; рынок требует онлайн-продавца | `medium` |
| `M15` Трупы и распад | `taskmanager/DecayTaskManager.java` | **Уточнение:** труп NPC, `CorpseDecayMs = 7000` (`ZoneCombat.cs:373`), продление до 17 с для заспойленного и `TickCorpseDecay` есть. Нет трупа игрока, ресурректа и `Harvest` | `low` |
| `M16` Dimensional Rift | `data/DimensionalRift.xml` | поиск `DimensionalRift` пуст; есть только входы в катакомбы/некрополи (`data/dungeons.json`, гейт по Seven Signs) | `medium` |
| `M17` Рецепты и крафт | `data/Recipes.xml`, `RecipeList.java`, `ManufactureItem.java` | поиск пуст; дублирует часть `D54` | `medium` |
| `M18` Смена класса / сабклассы / Noblesse | `data/SkillLearn.xml`, `instance/ClassMaster.java` | **Уточнение:** **первая профессия работает** — `data/village-masters.json` (`transferTalk` 4, `mageRoots` 4, `changes` 2), `VillageMasterCatalog`, `ZoneWorldVillageMasters.cs`, квесты Q406/Q407/Q408/Q409, тесты `ClassChangeTests.cs`; выдаётся 15 купонов D (8869), SysMsg 1308. Нет: 2-й/3-й профессии, сабклассов, Noblesse | `medium` |
| `M19` Community board | `gameserver/communitybbs/` | поиск `CommunityBoard` пуст | `low` |
| `M20` Аугментация | `AugmentationData.java`, `data/EnchantItemOptions.xml` | поиск `Augment`/`Lifestone` пуст | `low` |

**Не найдено в эталонном дереве** (по именам классов в `ref/mobius-ct0-interlude/...`, перенесено
из прошлой версии): `AttendanceReward`, `FishingChampionManager`. В этом worktree `ref/` нет, поэтому
перепроверить нельзя.

---

## Что отличалось от аудита по `main`

Это и есть содержательная разница между старой архитектурой и `feat/net-foundation`. Каждый пункт
проверен по коду этого дерева.

| Пункт | Что было в аудите по `main` | Что на самом деле в `feat/net-foundation` |
|---|---|---|
| `D8` | «урон игрока константный 32» | `RollMelee` + `70·PAtk/PDef` (`ZoneCombat.cs:211`–`:233`, `ZoneWorldCombat.cs:153`) — **снято** |
| `D9` | «промахов нет» | `HitChancePermille` + флаг `0x80` (`ZoneCombat.cs:139`, `ZoneWorldCombat.cs:386`) — **снято** |
| `D10` | «критов нет» | crit в `RollMelee`, cap 500 ‰, флаг `0x20` — **снято** |
| `D11` | «щитов нет» | `RollShield`, perfect block 5%, `sDef`, флаг `0x40` — **снято** |
| `D12` | «soulshot нет, множителя ×2 нет» | соулшоты/спириты работают (`ZoneWorldShots.cs:35`, `ZoneWorldSkillDamage.cs:64`), стрелы расходуются (`ZoneWorldArrows.cs`) — **снято** |
| `D14` | «интервал — глобальная константа» | `500000/Pspd` (`ZoneCombat.cs:96`) — **снято** |
| `D19` | «прерывания каста нет» | `calcAtkBreak` + SysMsg 27 (`CastRules.cs:14`, `ZoneWorldCastInterrupt.cs:39`) — **снято** |
| `D20` | «нет `coolTime`» | `coolTimeMs` в данных, `hitTime + coolTime` (`ZoneWorldSkillDamage.cs:42`) — **снято** |
| `D21` | «перезарядка не масштабируется статами» | `reuse·333/speed` (`ZoneCombat.cs:240`) — **снято** |
| `D23` | «SPS/BSS нет» | зарядка спиритом, `×2`/`×4` M.Atk — **снято** |
| `D24` | «`calcStat` отсутствует» | `PlayerStatMods` SET→MUL→ADD, баффы, пассивы `using` (`ZoneWorldStats.cs:102`) — **снято** |
| `D25` | «статы в бою не читаются» | `PlayerStats` используется и в бою, и в `UserInfo` — **снято** |
| `D26` | «скилла 4267 нет в `data/skills.json`» | скилл **есть** в `data/skill-passives.json:5004` с полным набором функций, но не фолдится — стало задачей на проводку, а не на импорт |
| `D28` | «`PAtkSpd`/`MAtkSpd` ни на что не влияют» | задают темп ударов, hit/cool/reuse; `runSpd` — полный пайплайн с DEX, cap 250, штрафом 4270 — **снято** |
| `D33` | «реген не зависит от веса» | 4270 фолдится в `RegHp`/`RegMp`, `TickRegen` применяет — **снято** |
| `D34` | «hate-листа нет, помнит последнего ударившего» | `HateList` с числами, `damage·100/(lvl+7)`, `MostHated` (`ZoneWorldAggro.cs:20`–`:60`) — **снято** |
| `D35` | «проактивного агро нет» | `TickAggression` с 3D-радиусом и LOS (`ZoneWorldAggro.cs:81`) — **снято** |
| `D36` | «нет домашней точки и возврата домой» | `HomeSet` + `ReturnHome` (`ZoneWorldMobAi.cs:184`, `ZoneWorldNpcMovement.cs:50`); остался только размер «поводка» — `low` |
| `D37` | «помощи клана нет» | `CallClanHelp` (`ZoneWorldAggro.cs:109`) — **снято** |
| `D44` | «`onKill` не подключён» | подключён через `GrantKillReward` (`ZoneWorldQuests.cs:437`); **комментарии в коде устарели** |
| `D45` | «партийного кредита нет» | один случайный подходящий участник в 1600 (`ZoneWorldQuests.cs:164`–`:179`) — **снято** |
| `D46` | «квестовых дропов нет» | операция `drop_item` (16 в датапаке) — **снято** |
| `D50` | «штрафа 4270 нет» | реализован целиком, включая `EtcStatusUpdate` и реген — **снято** |
| `D52` | «отдельного инвентаря квестовых предметов нет» | флаг `QuestItem` + блокировки дропа/трейда/продажи/склада — **снято** |
| `D57` | «спойл только админский» | спойл/свип играбельны, с правами и временем трупа (`ZoneWorldSpoil.cs:75`, `:110`) — **снято** |
| `D58` | «защиты дропа нет» | `DropProtection` 15 с, владелец + партия (`ZoneWorldGround.cs:51`, `:63`) — **снято** |
| `D59` | «1 файл дропа» | 22 файла, 182 группы — всё ещё `medium`, но на порядок больше |
| `D60` | «времени жизни нет» | 600 с / 60 с для трав, `TickGroundExpiry` — **снято** |
| `D61` | «территории игнорируются» | **подтверждается**: в `data/spawns/` нет ни `territories`, ни `count` |
| `D63` | «нет домашней точки» | есть (`HomeSet`) — **снято** |
| `M1` | «дверей нет» | коллизия дверей + 593 двери в данных; нет открытия → `medium` |
| `M18` | «нет 1-й/2-й профессии» | первая профессия работает end-to-end → `medium` |

**Где старая архитектура и новая действительно расходятся сильнее всего:** боевые формулы
(`D8`–`D23`), стат-пайплайн (`D24`–`D28`), ИИ мобов (`D34`–`D37`), квестовый `onKill` (`D44`–`D46`),
склад/вес и спойл (`D50`–`D60`). Общий рисунок: на `main` отсутствовал **слой применения** — данные
и каркас часто были, но статы не доходили до боя. На `feat/net-foundation` этот слой построен
(`ZoneWorldStats`, `SkillStatEffects`, `ZoneWorldAggro`, `ZoneCombat`), и почти все оставшиеся
расхождения — это **неперенесённые ветки внутри уже существующего пайплайна** (4267 и 5076 не
фолдятся, хотя 4270 фолдится; награда не делится по урону, хотя `GrantKillReward` есть;
вес не проверяется на подборе, хотя `MaxLoadOf` есть).

**Проверка свежих фич ветки** (из последних коммитов) — что реально есть:

| Фича | Статус | Где |
|---|---|---|
| Weight Penalty 4270 | **есть** | `WeightPenalty.cs`, `ZoneWorldWeight.cs:146` |
| runSpd-пайплайн | **есть** | `ZoneWorldStats.cs:116`–`:174`, cap 250 |
| Long/Short-каст | такой терминологии в коде нет; фактический эквивалент — `hitTime` + `coolTime` + `isMagic`/`static`-прерываемость: **есть** | `ZoneWorldSkillDamage.cs:18`–`:42`, `CastRules.cs:26` |
| hits по `hitTime` | **есть** | `PendingMeleeHit`, `PendingSkillHit`, `TickPendingHits` |
| soulshot | **есть** (стрел нет) | `ZoneWorldShots.cs:35` |
| spoil/sweep | **есть** | `ZoneWorldSpoil.cs:75`, `:110` |
| партийный квест-кредит | **есть** | `ZoneWorldQuests.cs:164`–`:179` |
| ShortCuts | **есть** | opcode `0x0358`–`0x035C`, `NetZoneServer.cs:585`–`:599`, персист |
| UserInfo на Net | **есть** | `NetEnterWorldProjector.cs`, `ZoneWorldNetUserInfo.cs` |

---

## Топ-10 приоритетов

1. **`D4` — PvP-урон.** Один снятый фильтр `Kind != Npc` в `TryAttack` и в скиллах открывает дуэли,
   осадный бой (`M6`) и матчи Олимпиады (`M5`), где уже готовы матчмейкинг, стадионы и очки. Без
   него три системы стоят на месте.
2. ~~**`D26` — grade penalty 4267.**~~ **Сделано.** `FoldGradePenalty` фолдит 4267 так же, как 4270;
   штраф грейда теперь режет скорость, темп атаки, уклонение и реген.
3. **`D1`, `D2` — потеря опыта и Death Penalty 5076.** Данные 5076 тоже уже есть
   (`skill-passives.json:5542`) и не фолдятся. Сейчас смерть почти бесплатна.
4. **`D3`, `D32` — CP как реальный пул.** Счётчик в `ZoneEntity`, урон по CP, реген, восстановление
   при респе. Сейчас `CurCp` всегда равен `MaxCp`.
5. **`D41`, `D42`, `D43` — награда за убийство.** Распределение по доле урона, `maxDealer`, штраф за
   разницу уровней, партийные exp/SP и лут. `GrantKillReward` уже есть — не хватает агрегатора урона.
6. **`N1`, `D55`, `D51` — вес и слоты вне магазина.** Подбор с земли, обмен, квестовая выдача.
   `MaxLoadOf` и `InventorySlots` готовы, проверок просто нет на этих путях.
7. **`D38`, `M7` — петы, самоны и миньоны.** Полностью отсутствуют; на них висят три класса и
   `MINIONS_RESPAWN_TIME`.
8. **`M3` — мир-зоны.** Peace zone разблокирует `D22` (проверка на попадании), уточняет `D35`
   (guard/friendly/chaos), `D31` (реген в бою) и `D6` (restart points). Водные зоны — готовый
   образец слоя.
9. **`D61`, `D62` (`M4`) — территории спавна.** Читатель XML уже отдаёт `territory`/`count`
   (`MobiusSpawnXmlReader.cs:32`–`:41`), теряется только на JSON-схеме. Плюс патрули.
10. **`D53`, `D54`, `M17` — клановый/freight-склад, приватные лавки, крафт.** Заглушки перечислены
    в одном месте (`BypassRegistry.cs:71`–`:78`); это половина экономики Interlude.

Отдельно, дешёвые правки без новой механики: `D5` (доли респавна вместо 100%), `N2` (левелап
добавляет дельту, а не лечит), `D31` (остановка регена в бою по `CombatUntilUnixMs`), `D12` (расход
стрел).
