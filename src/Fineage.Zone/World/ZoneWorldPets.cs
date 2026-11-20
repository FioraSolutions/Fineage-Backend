using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Services;
using Fineage.Game.Skills;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Npcs;

namespace Fineage.Zone.World;

/// <summary>
/// Pets after Mobius CT 0 (<c>SummonItems</c>, <c>SummonPet</c>, <c>Pet</c>, <c>PetStat</c>, <c>PetInventory</c>): a
/// collar (control item) summons its pet after a 5 s cast; the pet keeps level, exp, HP / MP, meal meter and its own
/// bag under the collar's object id (<c>pets</c> table). It eats every 10 s (more in battle) and eats food from its
/// bag when hungry; at 0 it stops obeying and may run away. Kill exp is split with the owner by the level row's
/// <c>get_exp_type</c>; the collar's enchant shows the pet level. A dead pet loses exp and must be resurrected within
/// 20 minutes, or it is gone with its collar (its items go to the owner).
/// </summary>
public sealed partial class ZoneWorld
{
    public const int SysMsgCannotMoveSitting = 31;
    public const int SysMsgCannotSummonInCombat = 578;
    public const int SysMsgCannotSummonTrade = 577;
    public const int SysMsgPetCannotCarry = 544;
    public const int SysMsgPetBagFull = 545;
    public const int SysMsgPetTooHeavy = 546;
    public const int SysMsgDeadPetCannotReturn = 589;
    public const int SysMsgPetInBattle = 579;
    public const int SysMsgHungryPetNoReturn = 594;
    public const int SysMsgPetLeftStarving = 593;
    public const int SysMsgPetVeryHungry = 597;
    public const int SysMsgPetAteStillHungry = 596;
    public const int SysMsgPetCannotUseItem = 972;
    public const int SysMsgPetGainedExp = 1014;
    public const int SysMsgPetPickedUp = 1020;
    public const int SysMsgPetPickedUpCount = 1021;
    public const int SysMsgPetPickedUpAdena = 1023;
    public const int SysMsgPetKilled = 1519;
    public const int SysMsgPetAteFood = 1527;
    public const int SysMsgPetUnresponsive = 1864;
    public const int SysMsgItemDisappeared = 301;

    /// <summary>Mobius <c>Pet.startFeed</c>: <c>scheduleAtFixedRate(FeedTask, 10000, 10000)</c>.</summary>
    public const int PetFeedTickMs = 10_000;
    /// <summary>Mobius <c>DecayTaskManager</c> for a pet: 20 minutes to resurrect it (SysMsg 1519).</summary>
    public const int PetCorpseMs = 20 * 60 * 1000;
    /// <summary>Mobius <c>INVENTORY_MAXIMUM_PET</c>.</summary>
    public const int PetInventorySlots = 12;
    /// <summary>Mobius <c>RequestActionUse</c> 19: a pet under 40 % meal cannot be sent back.</summary>
    public const int PetReturnMinFedPercent = 40;
    /// <summary>Mobius <c>FeedTask</c>: a starving pet leaves with this % chance per tick.</summary>
    public const int PetLeaveChance = 30;
    /// <summary>Mobius <c>SummonPet</c>: <c>spawnMe(owner + 50, owner + 100)</c>.</summary>
    const int PetSpawnDx = 50, PetSpawnDy = 100;
    /// <summary>Collar summon skill (Mobius 2046 "Summon Pet", static, hitTime 5000).</summary>
    public const int PetSummonSkillId = 2046;
    /// <summary>Blessed Scroll of Resurrection for Pets and its skill (Mobius 2179, hitTime 15000, power 100).</summary>
    public const int PetResurrectScrollId = 6387;
    public const int PetResurrectSkillId = 2179;
    public const int PetResurrectHitMs = 15_000;

    readonly PetCatalog _pets = PetCatalog.LoadDefault();
    /// <summary>Pets kept here when the world runs without an account store (tests, dev).</summary>
    readonly Dictionary<int, StoredPet> _petMemory = [];
    readonly Random _petRandom = new();

    internal PetCatalog Pets => _pets;

    static PetState? PetOf(ZoneEntity summon) => summon.Summon?.Pet;

    ZoneEntity? PetEntityOf(ZoneEntity player) => SummonOf(player) is { Summon.Pet: not null } s ? s : null;

    /// <summary>Object id of the collar of the pet the player has out, 0 when none (quests 421).</summary>
    internal int PetControlObjectIdOf(ZoneEntity player) => PetEntityOf(player)?.Summon?.Pet?.ControlObjectId ?? 0;

    StoredPet? LoadPet(int controlObjectId) =>
        _persist.Enabled ? _persist.FindPet(controlObjectId) : _petMemory.GetValueOrDefault(controlObjectId);

    void StorePet(ZoneEntity owner, ZoneEntity summon)
    {
        if (PetOf(summon) is not { } pet)
            return;
        var row = new StoredPet(
            pet.ControlObjectId,
            owner.ObjectId,
            summon.NpcId,
            pet.Name,
            summon.Level,
            pet.Exp,
            Math.Max(0, summon.CurHp),
            Math.Max(0, summon.CurMp),
            pet.Fed,
            pet.Inventory.Items.Select(static i => new StoredItem(i.ItemId, i.Count, -1, i.EnchantLevel, i.ObjectId)).ToArray());
        if (_persist.Enabled)
            _persist.SavePet(row);
        else
            _petMemory[row.ControlObjectId] = row;
    }

    void ForgetPet(int controlObjectId)
    {
        if (_persist.Enabled)
            _persist.DeletePet(controlObjectId);
        else
            _petMemory.Remove(controlObjectId);
    }

    // ---------------------------------------------------------------- collar use

    /// <summary>Mobius <c>SummonItems.onItemUse</c> refusals, then the 5 s static cast; the pet appears at hit time.</summary>
    string? TryUseCollar(ZoneSession session, ZoneEntity player, ItemInstance collar, PetTemplate template)
    {
        long now = NowUnixMs();
        if (player.Dead || now < player.CastingUntilMs || player.PendingHit != null)
            return null;
        if (player.Sitting)
        {
            SelfLine(session, SysMsgCannotMoveSitting, "You cannot move while sitting.");
            return null;
        }
        if (SummonOf(player) != null)
        {
            SelfLine(session, SysMsgAlreadyHavePet, "You already have a pet.");
            return null;
        }
        if (player.CombatUntilUnixMs > now)
        {
            SelfLine(session, SysMsgCannotSummonInCombat, "You cannot summon during combat.");
            return null;
        }
        if (_tradeByPlayer.ContainsKey(player.ObjectId) || player.PrivateStoreType != 0)
        {
            SelfLine(session, SysMsgCannotSummonTrade, "You cannot summon during a trade or while using the private shops.");
            return null;
        }
        if (_summonTemplates?.ById.ContainsKey(template.NpcId) == false)
            return null;

        int hitMs = template.SummonCastMs;
        var cast = new MagicSkillUseMsg
        {
            CasterObjectId = player.ObjectId,
            TargetObjectId = player.ObjectId,
            SkillId = PetSummonSkillId,
            SkillLevel = 1,
            HitTimeMs = hitMs,
            ReuseDelayMs = 0
        };
        StopForCast(player, player, now);
        FanoutSkill(player, player, cast, targetStatus: null, CasterMpStatus(player));
        player.CastingUntilMs = now + hitMs;
        var skill = new SkillDefinition(PetSummonSkillId, "Summon Pet", passive: false, operateType: "A1", targetType: "SELF",
            castRange: 0, hitTimeMs: hitMs, reuseDelayMs: 0, abnormalTimeSeconds: 0, isDebuff: false,
            levels: [new SkillLevelData(1, mpConsume: 0, power: 0)])
        {
            MagicType = 2
        };
        int controlObjectId = collar.ObjectId;
        ScheduleEffect(player, player, skill, 1, now, hitMs, (_, at) =>
        {
            if (player.Dead || SummonOf(player) != null)
            {
                if (SummonOf(player) != null)
                    SelfLine(session, SysMsgAlreadyHavePet, "You already have a pet.");
                return;
            }
            // Mobius SummonPet: the collar must still be the player's.
            if (player.Inventory?.Find(controlObjectId) is not { } item || item.ItemId != template.ItemId)
                return;
            SpawnPet(player, item, template, at);
        });
        return null;
    }

    // ---------------------------------------------------------------- spawn

    ZoneEntity? SpawnPet(ZoneEntity owner, ItemInstance collar, PetTemplate template, long now)
    {
        _summonTemplates ??= NpcCatalog.LoadDefault();
        if (!_summonTemplates.ById.TryGetValue(template.NpcId, out var npc))
        {
            ZoneLog.Info($"pet skip unknown npcId={template.NpcId}");
            return null;
        }

        var stored = LoadPet(collar.ObjectId);
        if (stored != null && stored.NpcId != template.NpcId)
            stored = null; // a collar of another kind (should not happen): start over
        var bag = new PlayerInventory(_game.Items, _itemIds);
        foreach (var row in stored?.Items ?? [])
        {
            if (!_game.Items.TryGet(row.ItemId, out _) || row.Count < 1 || row.ObjectId <= 0 || bag.Find(row.ObjectId) != null)
                continue;
            bag.AddRestored(row.ObjectId, row.ItemId, row.Count, row.EnchantLevel);
        }

        // Mobius Pet.spawnPet: a new pet starts at the template level (the collar's level when it already has one,
        // e.g. a strider bugle handed out by quest 421); Sin Eater follows the owner.
        int level = stored?.Level ?? Math.Max(template.MinLevel, Math.Max(npc.Level, collar.EnchantLevel));
        if (template.SyncLevel)
            level = owner.Level;
        level = Math.Clamp(level, template.MinLevel, template.MaxLevel);
        var pet = new PetState
        {
            Template = template,
            ControlObjectId = collar.ObjectId,
            ControlItemId = collar.ItemId,
            Exp = stored?.Exp ?? template.ExpForLevel(level),
            Fed = stored?.Fed ?? template.At(level).MaxMeal,
            Inventory = bag,
            Name = stored?.Name ?? "",
            NextFeedMs = now + PetFeedTickMs
        };

        int objectId = AllocateQuestNpcObjectId();
        int x = owner.X + PetSpawnDx, y = owner.Y + PetSpawnDy, z = owner.Z;
        if (_geo.HasGeo(x, y))
            z = _geo.GetHeight(x, y, z);
        var entity = new ZoneEntity
        {
            ObjectId = objectId,
            Kind = ZoneEntityKind.Npc,
            InstanceId = owner.InstanceId,
            Name = pet.Name.Length > 0 ? pet.Name : npc.Name,
            Title = owner.Name,
            NpcType = npc.Type,
            X = x,
            Y = y,
            Z = z,
            Heading = owner.Heading,
            NpcId = npc.NpcId,
            Attackable = false,
            PAtkSpd = npc.AttackSpeed,
            BaseAccuracy = npc.Accuracy,
            BaseEvasion = npc.Evasion,
            BaseCritical = npc.Critical,
            RandomDamage = npc.RandomDamage,
            Dex = npc.Dex,
            ShieldDef = npc.ShieldDef,
            ShieldRate = npc.ShieldRate,
            AttackRange = npc.AttackRange,
            Aggressive = false,
            DefenceTraits = npc.DefenceTraits,
            RunSpeed = npc.RunSpeed,
            WalkSpeed = npc.WalkSpeed,
            Running = true,
            HomeSet = false,
            Summon = new SummonState
            {
                OwnerObjectId = owner.ObjectId,
                Kind = SummonKindNet.Pet,
                NextLifeTickMs = long.MaxValue,
                Pet = pet
            }
        };
        ApplyPetLevel(entity, pet, npc, level);
        if (stored != null)
        {
            entity.CurHp = Math.Min(entity.MaxHp, stored.CurHp);
            entity.CurMp = Math.Min(entity.MaxMp, stored.CurMp);
        }
        _byId.Add(objectId, entity);
        _aoi.Enter(objectId, x, y);
        _summonIds.Add(objectId);
        owner.SummonObjectId = objectId;
        FanoutNewNpc(entity);

        // Mobius: a pet stored dead comes back dead with a fresh decay timer.
        if (stored != null && entity.CurHp <= 0)
        {
            entity.CurHp = 0;
            entity.Dead = true;
            entity.Summon.DecayAtMs = now + PetCorpseMs;
            if (owner.Session is { } deadSession)
                SelfLine(deadSession, SysMsgPetKilled, PetKilledText);
        }
        else if (entity.CurHp < entity.MaxHp || entity.CurMp < entity.MaxMp)
        {
            SyncNeedRegen(entity);
        }

        SetCollarLevel(owner, pet, entity.Level);
        if (stored == null)
            StorePet(owner, entity);
        SendSummonInfo(entity);
        SendPetInventory(owner, entity);
        ZoneLog.Info($"pet spawn owner={owner.ObjectId} collar={collar.ObjectId} npc={npc.NpcId} lv{entity.Level} object={objectId} new={stored == null}");
        return entity;
    }

    const string PetKilledText =
        "Your pet has been killed! Make sure you resurrect your pet within 20 minutes or your pet and all of it's items will disappear forever!";

    static double LevelMod(int level) => (level + 89) / 100d;

    /// <summary>
    /// Pet stats at <paramref name="level"/>: the npc template is the pet at its template level; other levels scale it
    /// by the Mobius <c>org_*</c> level row (× level modifier for P.Atk / P.Def / M.Def, × its square for M.Atk, the
    /// same curve Mobius applies on top of the row).
    /// </summary>
    void ApplyPetLevel(ZoneEntity s, PetState pet, NpcTemplate npc, int level)
    {
        var at = pet.Template.At(level);
        var baseRow = pet.Template.At(npc.Level);
        double m = LevelMod(level), m0 = LevelMod(npc.Level);
        static double Scale(double template, double now, double then, double mod) =>
            then > 0 && now > 0 ? template * now / then * mod : template;
        s.Level = level;
        s.MaxHp = Math.Max(1, (int)Math.Round(Scale(npc.Hp, at.Hp, baseRow.Hp, 1)));
        s.MaxMp = Math.Max(1, (int)Math.Round(Scale(npc.Mp, at.Mp, baseRow.Mp, 1)));
        s.PAtk = Scale(npc.PAtk, at.PAtk, baseRow.PAtk, m / m0);
        s.PDef = Scale(npc.PDef, at.PDef, baseRow.PDef, m / m0);
        s.MDef = Scale(npc.MDef, at.MDef, baseRow.MDef, m / m0);
        s.MAtk = Scale(npc.MAtk, at.MAtk, baseRow.MAtk, m * m / (m0 * m0));
        s.HpRegen = Scale(npc.HpRegen, at.HpRegen, baseRow.HpRegen, 1);
        s.MpRegen = Scale(npc.MpRegen, at.MpRegen, baseRow.MpRegen, 1);
        s.CurHp = s.MaxHp;
        s.CurMp = s.MaxMp;
        s.Summon!.Skills = PetSkillsAt(pet.Template, level);
    }

    /// <summary>Mobius <c>Pet.getSkillLevel</c>: level / 10, past 70 seven plus one per 5 levels; attack skills only.</summary>
    IReadOnlyList<(int SkillId, int Level)> PetSkillsAt(PetTemplate template, int level)
    {
        var list = new List<(int, int)>();
        foreach (var row in template.Skills)
        {
            if (row.MinLevel > level || !_skillCombat.TryGet(row.SkillId, out var combat) || combat == null)
                continue;
            int skillLevel = row.SkillLevel > 0 ? row.SkillLevel : level > 70 ? 7 + (level - 70) / 5 : level / 10;
            list.Add((row.SkillId, Math.Clamp(skillLevel, 1, Math.Max(1, combat.Levels))));
        }
        return list;
    }

    /// <summary>Mobius <c>SummonPet</c> / level-up: <c>controlItem.setEnchantLevel(pet.getLevel())</c>.</summary>
    void SetCollarLevel(ZoneEntity owner, PetState pet, int level)
    {
        if (owner.Inventory?.Find(pet.ControlObjectId) is not { } collar || collar.EnchantLevel == level)
            return;
        try
        {
            owner.Inventory.SetEnchantLevel(collar, Math.Clamp(level, 0, 127));
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }
        if (owner.Session is { } session)
        {
            NetBridge?.FanoutInventoryUpdate([session.ObjectId], InventoryMessages.Update(InventoryChange.Modify, collar));
            SaveInventory(session, owner.Inventory);
        }
    }

    // ---------------------------------------------------------------- packets

    void FillPetInfo(SummonInfoNetMsg info, ZoneEntity s, PetState pet)
    {
        info.Exp = pet.Exp;
        info.ExpLevelStart = pet.Template.ExpForLevel(s.Level);
        info.ExpLevelEnd = pet.Template.ExpForLevel(s.Level + 1);
        info.Feed = pet.Fed;
        info.MaxFeed = pet.Template.At(s.Level).MaxMeal;
        info.Load = (int)Math.Min(int.MaxValue, pet.Inventory.TotalWeight);
        info.MaxLoad = pet.Template.Load;
        if (pet.Fed <= 0)
            info.RunSpeed /= 2;
    }

    void SendPetInventory(ZoneEntity owner, ZoneEntity summon)
    {
        if (NetBridge == null || owner.Session == null || PetOf(summon) is not { } pet)
            return;
        NetBridge.FanoutPetInventory([owner.ObjectId], new PetInventoryNetMsg
        {
            PetObjectId = summon.ObjectId,
            Items = pet.Inventory.Items.Select(static i => new PetItemNet
            {
                ObjectId = i.ObjectId,
                ItemId = i.ItemId,
                Count = i.Count,
                EnchantLevel = i.EnchantLevel,
                Equipped = i.IsEquipped
            }).ToArray()
        });
    }

    // ---------------------------------------------------------------- feeding

    bool PetHungry(ZoneEntity s, PetState pet) =>
        pet.Fed < pet.Template.HungryLimit / 100d * pet.Template.At(s.Level).MaxMeal;

    /// <summary>Mobius <c>Pet.FeedTask.run</c>: eat, starve (30 % to leave), auto-eat from the bag when hungry.</summary>
    bool TickPetFeed(ZoneEntity owner, ZoneEntity s, PetState pet, long now)
    {
        var session = owner.Session!;
        var row = pet.Template.At(s.Level);
        int use = s.CombatUntilUnixMs > now ? row.MealBattle : row.MealNormal;
        pet.Fed = pet.Fed > use ? pet.Fed - use : 0;
        if (pet.Fed <= 0)
        {
            if (_petRandom.Next(100) < PetLeaveChance)
            {
                SelfLine(session, SysMsgPetLeftStarving, "Starving, grumpy and fed up, your pet has left.");
                UnsummonPet(owner, s, 0);
                return false;
            }
            SelfLine(session, SysMsgPetUnresponsive, "Your pet/servitor is unresponsive and will not obey any orders.");
            SendSummonInfo(s);
            return true;
        }

        if (PetHungry(s, pet))
        {
            ItemInstance? food = null;
            foreach (var f in pet.Template.Food)
            {
                food = pet.Inventory.FindByItemId(f.ItemId);
                if (food != null)
                    break;
            }
            if (food != null)
            {
                string name = ItemName(food.ItemId);
                SelfLine(session, SysMsgPetAteFood, $"Your pet was hungry so it ate {name}.", name);
                PetEat(owner, s, pet, food);
            }
            else if (pet.Template.Food.Count == 0)
            {
                SelfLine(session, SysMsgPetVeryHungry, "Your pet is very hungry. Please be careful.");
            }
        }
        SendSummonInfo(s);
        return true;
    }

    string ItemName(int itemId) => _game.Items.TryGet(itemId, out var def) && def != null ? def.Name : itemId.ToString();

    /// <summary>Mobius <c>PetFood.useFood</c> for a pet: one piece from its bag, + the food skill's feed, cap at max.</summary>
    bool PetEat(ZoneEntity owner, ZoneEntity s, PetState pet, ItemInstance food)
    {
        if (pet.Template.FoodFor(food.ItemId) is not { } kind)
        {
            if (owner.Session is { } refusal)
                SelfLine(refusal, SysMsgPetCannotUseItem, "This pet cannot use this item.");
            return false;
        }
        pet.Inventory.TakeOne(food);
        pet.Fed = Math.Min(pet.Template.At(s.Level).MaxMeal, pet.Fed + kind.Feed);
        var cast = new MagicSkillUseMsg { CasterObjectId = s.ObjectId, TargetObjectId = s.ObjectId, SkillId = 2048, SkillLevel = 1 };
        if (NetBridge != null && KnownWatchers(s.ObjectId) is { Count: > 0 } watchers)
            NetBridge.FanoutMagicSkillUse(watchers, cast);
        if (PetHungry(s, pet) && owner.Session is { } session)
            SelfLine(session, SysMsgPetAteStillHungry, "Your pet ate a little, but is still hungry.");
        SendPetInventory(owner, s);
        SendSummonInfo(s);
        return true;
    }

    // ---------------------------------------------------------------- exp

    /// <summary>
    /// Mobius <c>Player.addExpAndSp</c>: with a living pet within party range, the owner keeps the level row's
    /// <c>get_exp_type</c> % of the exp / SP and the pet earns the rest.
    /// </summary>
    (double Exp, double Sp) SharePetExp(ZoneEntity player, double exp, double sp)
    {
        if (exp <= 0 || PetEntityOf(player) is not { } s || PetOf(s) is not { } pet ||
            !ZoneCombat.Within(player.X, player.Y, s.X, s.Y, QuestPartyRange) || s.InstanceId != player.InstanceId)
            return (exp, sp);
        double kept = Math.Clamp(pet.Template.At(s.Level).OwnerExpTaken / 100d, 0, 1);
        if (!s.Dead && s.CurHp > 0)
            AddPetExp(player, s, pet, (long)Math.Round(exp * (1 - kept)));
        return (exp * kept, sp * kept);
    }

    void AddPetExp(ZoneEntity owner, ZoneEntity s, PetState pet, long gained)
    {
        if (gained == 0)
            return;
        int max = pet.Template.MaxLevel;
        long cap = pet.Template.ExpForLevel(max + 1) - 1;
        pet.Exp = Math.Clamp(pet.Exp + gained, 0, Math.Max(0, cap));
        if (gained > 0 && owner.Session is { } session)
            SelfLine(session, SysMsgPetGainedExp, $"Your pet gained {gained} experience points.", gained.ToString(System.Globalization.CultureInfo.InvariantCulture));

        int level = s.Level;
        while (level < max && pet.Exp >= pet.Template.ExpForLevel(level + 1))
            level++;
        while (level > pet.Template.MinLevel && pet.Exp < pet.Template.ExpForLevel(level))
            level--;
        if (level != s.Level)
        {
            bool up = level > s.Level;
            double hp = s.CurHp, mp = s.CurMp;
            _summonTemplates ??= NpcCatalog.LoadDefault();
            if (_summonTemplates.ById.TryGetValue(s.NpcId, out var npc))
                ApplyPetLevel(s, pet, npc, level);
            if (!up)
            {
                s.CurHp = (int)Math.Min(s.MaxHp, hp);
                s.CurMp = (int)Math.Min(s.MaxMp, mp);
            }
            if (up && NetBridge != null && KnownWatchers(s.ObjectId) is { Count: > 0 } watchers)
                NetBridge.FanoutSocialAction(watchers, s.ObjectId, LevelUpSocialAction);
            FanoutVitals(s, StatusFor(s));
            SetCollarLevel(owner, pet, level);
            StorePet(owner, s);
        }
        SendSummonInfo(s);
    }

    /// <summary>Mobius <c>SocialAction.LEVEL_UP</c>.</summary>
    const int LevelUpSocialAction = 2122;

    // ---------------------------------------------------------------- death / resurrection / decay

    /// <summary>Mobius <c>Pet.deathPenalty</c>: (−0.07 × level + 6.5) % of the level's exp span, outside PvP zones.</summary>
    void PetDeathPenalty(ZoneEntity owner, ZoneEntity s, PetState pet)
    {
        pet.ExpBeforeDeath = pet.Exp;
        if (InArenaOnly(s))
            return;
        int level = s.Level;
        double percent = -0.07 * level + 6.5;
        long span = pet.Template.ExpForLevel(level + 1) - pet.Template.ExpForLevel(level);
        long lost = (long)Math.Round(span * percent / 100d);
        if (lost > 0)
        {
            pet.Exp = Math.Max(pet.Template.ExpForLevel(pet.Template.MinLevel), pet.Exp - lost);
            int newLevel = level;
            while (newLevel > pet.Template.MinLevel && pet.Exp < pet.Template.ExpForLevel(newLevel))
                newLevel--;
            if (newLevel != level)
            {
                _summonTemplates ??= NpcCatalog.LoadDefault();
                if (_summonTemplates.ById.TryGetValue(s.NpcId, out var npc))
                    ApplyPetLevel(s, pet, npc, newLevel);
                s.CurHp = 0;
                s.CurMp = 0;
                SetCollarLevel(owner, pet, newLevel);
            }
        }
    }

    void PetDied(ZoneEntity owner, ZoneEntity s, PetState pet, long now)
    {
        PetDeathPenalty(owner, s, pet);
        s.Summon!.DecayAtMs = now + PetCorpseMs;
        pet.PickupTargetId = 0;
        if (owner.Session is { } session)
            SelfLine(session, SysMsgPetKilled, PetKilledText);
        StorePet(owner, s);
    }

    /// <summary>Mobius <c>Pet.doRevive(power)</c>: back to life, <paramref name="power"/> % of the lost exp restored.</summary>
    void RevivePet(ZoneEntity s, double power)
    {
        if (PetOf(s) is not { } pet || !s.Dead || OwnerOf(s) is not { } owner)
            return;
        if (pet.ExpBeforeDeath > pet.Exp && power > 0)
            AddPetExp(owner, s, pet, (long)Math.Round((pet.ExpBeforeDeath - pet.Exp) * Math.Min(100, power) / 100d));
        pet.ExpBeforeDeath = 0;
        s.Dead = false;
        s.Summon!.DecayAtMs = 0;
        // Mobius Creature.doRevive: 70 % HP / MP unless the restore was 100 %.
        s.CurHp = Math.Max(1, (int)(s.MaxHp * 0.7));
        s.CurMp = (int)(s.MaxMp * 0.7);
        pet.NextFeedMs = NowUnixMs() + PetFeedTickMs;
        FanoutRevive(s);
        FanoutVitals(s, StatusFor(s));
        SyncNeedRegen(s);
        StorePet(owner, s);
        SendSummonInfo(s);
    }

    /// <summary>Mobius <c>Pet.deleteMe</c> after the corpse decayed: bag to the owner, collar destroyed, row deleted.</summary>
    void PetDecayed(ZoneEntity owner, ZoneEntity s, PetState pet)
    {
        var session = owner.Session;
        var inventory = owner.Inventory;
        if (inventory != null && session != null)
        {
            var changes = new List<InventoryUpdateEntryMsg>();
            foreach (var item in pet.Inventory.Items.ToArray())
                MovePetItem(pet.Inventory, inventory, item, item.Count, null, changes);
            if (inventory.Find(pet.ControlObjectId) is { } collar)
            {
                inventory.Remove(collar);
                changes.Add(InventoryMessages.Update(InventoryChange.Remove, collar).Changes[0]);
                string name = ItemName(collar.ItemId);
                SelfLine(session, SysMsgItemDisappeared, $"{name} has disappeared.", name);
            }
            if (changes.Count > 0)
            {
                FanInventory(session, Collapse(changes));
                SaveInventory(session, inventory);
            }
        }
        ForgetPet(pet.ControlObjectId);
        RemoveSummon(s, 3);
    }

    // ---------------------------------------------------------------- unsummon

    /// <summary>Mobius <c>Pet.unSummon</c>: stop feeding, store the pet, leave the world.</summary>
    void UnsummonPet(ZoneEntity owner, ZoneEntity s, int reason)
    {
        StorePet(owner, s);
        RemoveSummon(s, reason);
    }

    // ---------------------------------------------------------------- bag

    /// <summary>
    /// Moves <paramref name="count"/> of <paramref name="item"/> between bags keeping the object id when the whole
    /// stack / piece moves (Mobius <c>transferItem</c>); a stack merges into the target's stack.
    /// </summary>
    static ItemInstance MovePetItem(PlayerInventory from, PlayerInventory to, ItemInstance item, int count,
        List<InventoryUpdateEntryMsg>? fromChanges, List<InventoryUpdateEntryMsg>? toChanges = null)
    {
        bool whole = count >= item.Count;
        if (item.IsEquipped)
            from.Unequip(item);
        if (whole)
        {
            from.Remove(item);
            fromChanges?.Add(InventoryMessages.Update(InventoryChange.Remove, item).Changes[0]);
        }
        else
        {
            from.TryReduce(item, count, out _);
            fromChanges?.Add(InventoryMessages.Update(InventoryChange.Modify, item).Changes[0]);
        }

        ItemInstance landed;
        bool merged = item.Template.Stackable && to.FindByItemId(item.ItemId) != null;
        if (merged || !whole)
            landed = to.Add(item.ItemId, count, item.Template.Stackable ? 0 : item.EnchantLevel);
        else
            landed = to.AddRestored(item.ObjectId, item.ItemId, count, item.EnchantLevel);
        (toChanges ?? fromChanges)?.Add(InventoryMessages.Update(merged ? InventoryChange.Modify : InventoryChange.Add, landed).Changes[0]);
        return landed;
    }

    long WeightOf(int itemId, int count) =>
        _game.Items.TryGet(itemId, out var def) && def != null ? (long)def.Weight * count : 0;

    bool PetBagFits(PetState pet, ItemInstance item, int count, ZoneSession session)
    {
        bool merges = item.Template.Stackable && pet.Inventory.FindByItemId(item.ItemId) != null;
        if (!merges && pet.Inventory.Items.Count >= PetInventorySlots)
        {
            SelfLine(session, SysMsgPetBagFull, "Your pet cannot carry any more items. Remove some, then try again.");
            return false;
        }
        if (pet.Inventory.TotalWeight + WeightOf(item.ItemId, count) > pet.Template.Load)
        {
            SelfLine(session, SysMsgPetTooHeavy, "Unable to place item, your pet is too encumbered.");
            return false;
        }
        return true;
    }

    /// <summary>Mobius <c>RequestGiveItemToPet</c>.</summary>
    void GiveItemToPet(ZoneSession session, ZoneEntity owner, ZoneEntity s, PetState pet, int objectId, int count)
    {
        if (count <= 0 || owner.Inventory?.Find(objectId) is not { } item || count > item.Count)
            return;
        if (owner.PrivateStoreType != 0 || _tradeByPlayer.ContainsKey(owner.ObjectId))
            return;
        if (item.IsEquipped || _pets.IsCollar(item.ItemId) || !item.Template.CanDropTradeStore)
        {
            SelfLine(session, SysMsgPetCannotCarry, "Your pet cannot carry this item.");
            return;
        }
        if (s.Dead || s.CurHp <= 0)
        {
            SelfLine(session, SysMsgPetCannotCarry, "Your pet cannot carry this item.");
            return;
        }
        if (!PetBagFits(pet, item, count, session))
            return;
        var changes = new List<InventoryUpdateEntryMsg>();
        MovePetItem(owner.Inventory, pet.Inventory, item, count, changes, new List<InventoryUpdateEntryMsg>());
        FanInventory(session, Collapse(changes));
        SaveInventory(session, owner.Inventory);
        StorePet(owner, s);
        SendPetInventory(owner, s);
        SendSummonInfo(s);
    }

    /// <summary>Mobius <c>RequestGetItemFromPet</c>.</summary>
    void TakeItemFromPet(ZoneSession session, ZoneEntity owner, ZoneEntity s, PetState pet, int objectId, int count)
    {
        if (count <= 0 || owner.Inventory == null || pet.Inventory.Find(objectId) is not { } item || count > item.Count)
            return;
        var changes = new List<InventoryUpdateEntryMsg>();
        MovePetItem(pet.Inventory, owner.Inventory, item, count, null, changes);
        FanInventory(session, Collapse(changes));
        SaveInventory(session, owner.Inventory);
        StorePet(owner, s);
        SendPetInventory(owner, s);
        SendSummonInfo(s);
    }

    /// <summary>Mobius <c>RequestPetUseItem</c>: food from the pet's bag (pet gear is not in the item data yet).</summary>
    void PetUseItem(ZoneSession session, ZoneEntity owner, ZoneEntity s, PetState pet, int objectId)
    {
        if (pet.Inventory.Find(objectId) is not { } item)
            return;
        if (owner.Dead || s.Dead || s.CurHp <= 0)
        {
            string name = ItemName(item.ItemId);
            SelfLine(session, SysMsgUnsuitableTerms, $"{name} cannot be used due to unsuitable terms.", name);
            return;
        }
        if (PetEat(owner, s, pet, item))
            StorePet(owner, s);
    }

    // ---------------------------------------------------------------- pick up

    /// <summary>Mobius <c>Pet.doPickupItem</c> once the pet stands on the item: protection, slots, weight, into its bag.</summary>
    void PetPickUp(ZoneEntity owner, ZoneEntity s, PetState pet, ZoneEntity ground, long now)
    {
        pet.PickupTargetId = 0;
        var session = owner.Session!;
        string name = ItemName(ground.GroundItemId);
        if (!CanPickupProtected(owner, ground, now) || IsHerb(ground.GroundItemId) || !_game.Items.TryGet(ground.GroundItemId, out var def) || def == null)
        {
            SelfLine(session, SysMsgFailedToPickup, $"You have failed to pick up {name}.", name);
            return;
        }
        bool merges = def.Stackable && pet.Inventory.FindByItemId(def.Id) != null;
        int pieces = def.Stackable ? 1 : ground.GroundCount;
        if (!merges && pet.Inventory.Items.Count + pieces > PetInventorySlots)
        {
            SelfLine(session, SysMsgPetBagFull, "Your pet cannot carry any more items. Remove some, then try again.");
            return;
        }
        if (pet.Inventory.TotalWeight + WeightOf(def.Id, ground.GroundCount) > pet.Template.Load)
        {
            SelfLine(session, SysMsgPetTooHeavy, "Unable to place item, your pet is too encumbered.");
            return;
        }
        int count = ground.GroundCount;
        if (def.Stackable || count <= 1)
            pet.Inventory.Add(def.Id, count);
        else
            pet.Inventory.AddPieces(def.Id, count);
        RemoveGroundItem(ground);
        if (def.Id == WarehouseRules.AdenaItemId)
            SelfLine(session, SysMsgPetPickedUpAdena, $"Your pet picked up {count} adena.", count.ToString());
        else if (count > 1)
            SelfLine(session, SysMsgPetPickedUpCount, $"Your pet picked up {count} {name}(s).", name, count.ToString());
        else
            SelfLine(session, SysMsgPetPickedUp, $"Your pet picked up {name}.", name);
        StorePet(owner, s);
        SendPetInventory(owner, s);
        SendSummonInfo(s);
    }

    /// <summary>Pick-up order: run to the item, take it when within pick-up range.</summary>
    bool ThinkPetPickup(ZoneEntity owner, ZoneEntity s, PetState pet, long now)
    {
        if (pet.PickupTargetId == 0)
            return false;
        if (!_byId.TryGetValue(pet.PickupTargetId, out var ground) || ground.Kind != ZoneEntityKind.GroundItem ||
            ground.InstanceId != s.InstanceId || !ZoneCombat.Within(s.X, s.Y, ground.X, ground.Y, SummonMaxChase))
        {
            pet.PickupTargetId = 0;
            return false;
        }
        if (!ZoneCombat.InPickupRange(s.X, s.Y, ground.X, ground.Y))
        {
            SummonApproach(s, ground, 20, now);
            return true;
        }
        if (s.MoveActive)
        {
            StopTimedMove(s);
            BroadcastNpcMove(s, s.X, s.Y, s.Z);
        }
        PetPickUp(owner, s, pet, ground, now);
        return true;
    }

    // ---------------------------------------------------------------- pet resurrection

    /// <summary>
    /// A resurrection on a dead pet (skill or the pet scroll): revived at hit time with the skill's exp restore.
    /// Mobius asks the owner first when someone else casts it; here it lands directly.
    /// </summary>
    string? TrySkillUseResurrectPet(ZoneSession session, ZoneEntity player, ZoneEntity target, int skillId,
        SkillDefinition template, SkillLevelData levelData, int level, long now, double power)
    {
        if (template.CastRange > 0 && !ZoneCombat.InCastRange(player.X, player.Y, target.X, target.Y, template.CastRange))
            return SkillProtocol.Range;
        ConsumeSkillMp(player, template, levelData.MpConsume);
        var (hitMs, reuseMs) = ScaledSkillTimes(player, template, level);
        if (reuseMs > 0)
            player.SkillReuseUntilMs[skillId] = now + reuseMs;
        var cast = BuildCast(player, target, skillId, level, hitMs, reuseMs);
        StopForCast(player, target, now);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player));
        player.CastingUntilMs = now + Math.Max(0, hitMs);
        ScheduleEffect(player, player, template, level, now, hitMs, (_, _) =>
        {
            if (_byId.ContainsKey(target.ObjectId))
                RevivePet(target, power);
        });
        return null;
    }

    /// <summary>Blessed Scroll of Resurrection for Pets (Mobius 2179): 15 s static cast on the dead pet, 100 % exp back.</summary>
    string? TryUsePetResurrectScroll(ZoneSession session, ZoneEntity player, ItemInstance scroll)
    {
        long now = NowUnixMs();
        if (player.Dead || now < player.CastingUntilMs)
            return null;
        int targetId = session.SelectedTargetId;
        if (!_byId.TryGetValue(targetId, out var target) || PetOf(target) == null || !target.Dead)
        {
            string name = ItemName(scroll.ItemId);
            SelfLine(session, SysMsgUnsuitableTerms, $"{name} cannot be used due to unsuitable terms.", name);
            return null;
        }
        var template = new SkillDefinition(PetResurrectSkillId, "Blessed Scroll of Resurrection: Pet", passive: false,
            operateType: "A1", targetType: "PC_BODY", castRange: 400, hitTimeMs: PetResurrectHitMs, reuseDelayMs: 0,
            abnormalTimeSeconds: 0, isDebuff: false, levels: [new SkillLevelData(1, mpConsume: 0, power: 100)])
        {
            MagicType = 2
        };
        if (!ZoneCombat.InCastRange(player.X, player.Y, target.X, target.Y, template.CastRange))
            return SkillProtocol.Range;
        var cast = BuildCast(player, target, PetResurrectSkillId, 1, PetResurrectHitMs, 0);
        StopForCast(player, target, now);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player));
        player.CastingUntilMs = now + PetResurrectHitMs;
        int scrollObjectId = scroll.ObjectId;
        ScheduleEffect(player, player, template, 1, now, PetResurrectHitMs, (_, _) =>
        {
            if (!_byId.ContainsKey(target.ObjectId) || !target.Dead || player.Inventory?.Find(scrollObjectId) is not { } item)
                return;
            player.Inventory.TryReduce(item, 1, out bool removed);
            NetBridge?.FanoutInventoryUpdate([session.ObjectId], InventoryMessages.Update(removed ? InventoryChange.Remove : InventoryChange.Modify, item));
            SaveInventory(session, player.Inventory);
            RevivePet(target, 100);
        });
        return null;
    }

    // ---------------------------------------------------------------- test hooks

    internal void TickPetFeedForTest(ZoneEntity player)
    {
        lock (_gate)
        {
            if (PetEntityOf(player) is { } s && OwnerOf(s) is { } owner)
                TickPetFeed(owner, s, s.Summon!.Pet!, NowUnixMs());
        }
    }

    internal void AddPetExpForTest(ZoneEntity player, long exp)
    {
        lock (_gate)
        {
            if (PetEntityOf(player) is { } s && OwnerOf(s) is { } owner)
                AddPetExp(owner, s, s.Summon!.Pet!, exp);
        }
    }

    internal void KillSummonForTest(ZoneEntity summon)
    {
        lock (_gate)
            SummonDied(summon, null, NowUnixMs());
    }

    internal StoredPet? LoadPetForTest(int controlObjectId)
    {
        lock (_gate)
            return LoadPet(controlObjectId);
    }
}
