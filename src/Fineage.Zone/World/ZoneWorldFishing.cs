using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Fishing;
using Fineage.Zone.Inventory;

namespace Fineage.Zone.World;

/// <summary>One cast line: where the bait floats, the fish on it, and the fight once it bites.</summary>
sealed class FishingSession
{
    public int BaitX { get; init; }
    public int BaitY { get; init; }
    public int BaitZ { get; init; }
    public int LureId { get; init; }
    public required FishRow Fish { get; init; }
    /// <summary>−1 when nothing will bite (day lure at night or night lure by day).</summary>
    public int Group { get; init; }
    public long GiveUpMs { get; init; }
    public long NextCheckMs { get; set; }
    public int CheckDelayMs { get; init; }
    public FishingFight? Fight { get; set; }
}

/// <summary>Mobius <c>Fishing</c> combat state.</summary>
sealed class FishingFight
{
    public int Hp { get; set; }
    public int MaxHp { get; init; }
    public int Regen { get; init; }
    public int Time { get; set; }
    public int Mode { get; set; }
    public int Deceptive { get; set; }
    public int Stop { get; set; }
    public int GoodUse { get; set; }
    public int Anim { get; set; }
    public bool Upper { get; init; }
    public int LureType { get; init; }
    public long NextTickMs { get; set; }
}

/// <summary>
/// Fishing after Mobius CT_0: the <c>Fishing</c> effect (1312) casts a lure onto a fishing zone's water, the
/// <c>LookingForFish</c> task rolls the fish's guts until it bites, the <c>Fishing</c> combat ticks each second
/// (regen while it fights, mode flips, deceptive flips for upper-grade and night fish), and <c>Pumping</c> (1313) /
/// <c>Reeling</c> (1314) pull its HP to zero. A catch gives the fish item, or at the level band's probability a
/// fishing monster. Fishing shots double one pull. Reimplemented, not copied.
/// </summary>
public sealed partial class ZoneWorld
{
    Random _fishingRandom;
    readonly Dictionary<int, FishingSession> _fishers = [];

    /// <summary>Fish, rods, zones and the guild skill tree. Tests swap in a small table.</summary>
    internal FishingData FishingTable { get; set; } = FishingData.Default;

    internal Random FishingRandom
    {
        get => _fishingRandom;
        set => _fishingRandom = value ?? throw new ArgumentNullException(nameof(value));
    }

    internal static bool IsFishingEntity(ZoneEntity entity) => entity.Fishing;

    bool IsFishingSkill(int skillId) =>
        skillId is FishingProtocol.FishingSkillId or FishingProtocol.PumpingSkillId or FishingProtocol.ReelingSkillId;

    string? TrySkillUseFishing(ZoneSession session, ZoneEntity player, Fineage.Game.Skills.SkillDefinition template,
        Fineage.Game.Skills.SkillLevelData levelData, int level, long now)
    {
        ConsumeSkillMp(player, template, levelData.MpConsume);
        int reuseMs = Math.Max(0, template.ReuseDelayMs);
        if (reuseMs > 0)
            player.SkillReuseUntilMs[template.SkillId] = now + reuseMs;
        FanoutSkill(player, player, BuildCast(player, player, template.SkillId, level, 0, reuseMs), targetStatus: null,
            CasterMpStatus(player));

        if (template.SkillId == FishingProtocol.FishingSkillId)
            CastLine(session, player, now);
        else
            Pull(session, player, template.SkillId == FishingProtocol.ReelingSkillId, level);
        return null;
    }

    void CastLine(ZoneSession session, ZoneEntity player, long now)
    {
        if (_fishers.TryGetValue(player.ObjectId, out _))
        {
            EndFishing(player, win: false);
            SelfLine(session, FishingProtocol.Cancelled, "Your attempt at fishing has been cancelled.");
            return;
        }

        var inventory = player.Inventory!;
        var rod = inventory.GetEquipped(PaperdollSlot.RHand);
        if (rod == null || rod.Template.WeaponType != WeaponType.FishingRod)
        {
            SelfLine(session, FishingProtocol.NoRod, "You do not have a fishing pole equipped.");
            return;
        }

        var lure = inventory.GetEquipped(PaperdollSlot.LHand);
        if (lure == null || lure.Template.EtcType != EtcItemType.Lure)
        {
            SelfLine(session, FishingProtocol.NoLure, "You must put bait on your hook before you can fish.");
            return;
        }

        if (player.PrivateStoreType != 0)
        {
            SelfLine(session, FishingProtocol.CannotFishInStore,
                "You cannot fish while using a recipe book, private manufacture or private store.");
            return;
        }

        if (Water.IsInWater(player.X, player.Y, player.Z))
        {
            SelfLine(session, FishingProtocol.UnderWater, "You cannot fish while under water.");
            return;
        }

        if (!FindBait(player, out int baitX, out int baitY, out int baitZ))
        {
            SelfLine(session, FishingProtocol.CantFishHere, "You can't fish here.");
            return;
        }

        int lureId = lure.ItemId;
        int left = inventory.TakeOne(lure);
        NetBridge?.FanoutInventoryUpdate([player.ObjectId],
            InventoryMessages.Update(left > 0 ? InventoryChange.Modify : InventoryChange.Remove, lure));
        SaveInventory(session, inventory);

        StartFishing(session, player, lureId, baitX, baitY, baitZ, now);
    }

    /// <summary>
    /// Mobius bait point: a random 90–250 in front of the fisher by heading, then every distance from 250 down to 90,
    /// until one lands inside both a fishing zone and water, in sight, with no ground above the surface.
    /// </summary>
    bool FindBait(ZoneEntity player, out int x, out int y, out int z)
    {
        double radian = player.Heading / 182.044444444 * Math.PI / 180.0;
        double cos = Math.Cos(radian), sin = Math.Sin(radian);
        int distance = _fishingRandom.Next(FishingProtocol.MinBaitDistance, FishingProtocol.MaxBaitDistance + 1);
        if (BaitAt(player, distance, cos, sin, out x, out y, out z))
            return true;
        for (distance = FishingProtocol.MaxBaitDistance; distance >= FishingProtocol.MinBaitDistance; distance--)
        {
            if (BaitAt(player, distance, cos, sin, out x, out y, out z))
                return true;
        }
        return false;
    }

    bool BaitAt(ZoneEntity player, int distance, double cos, double sin, out int x, out int y, out int z)
    {
        x = (int)(player.X + cos * distance);
        y = (int)(player.Y + sin * distance);
        z = 0;
        if (!FishingTable.InFishingZone(x, y) || Water.SurfaceAt(x, y) is not { } surface)
            return false;
        z = surface;
        if (!_geo.CanSeeTarget(player.X, player.Y, player.Z, x, y, z))
            return false;
        if (_geo.HasGeo(x, y) && (_geo.GetHeight(x, y, z) > z || _geo.GetHeight(x, y, player.Z) > z))
            return false;
        return true;
    }

    void StartFishing(ZoneSession session, ZoneEntity player, int lureId, int x, int y, int z, long now)
    {
        StopForCast(player, player, now);
        int expertise = player.KnownSkills.GetValueOrDefault(FishingProtocol.ExpertiseSkillId);
        int lvl = FishingRules.Level(expertise, _fishingRandom.Next(100));
        int grade = FishingRules.Grade(lureId);
        int group = FishingRules.Group(lureId, grade, _fishingRandom.Next(100));
        var candidates = FishingTable.FishFor(lvl, group, grade);
        if (candidates.Count == 0)
        {
            // Mobius: "Error - Fish are not defined" and the line comes back in.
            SelfLine(session, 0, "There are no fish of that kind here.");
            player.Fishing = true;
            _fishers[player.ObjectId] = new FishingSession { Fish = candidates.FirstOrDefault() ?? EmptyFish, LureId = lureId, Group = -1 };
            EndFishing(player, win: false);
            return;
        }

        var fish = candidates[_fishingRandom.Next(candidates.Count)];
        SelfLine(session, FishingProtocol.CastLine, "You cast your line and start to fish.");
        bool nightLure = FishingRules.IsNightLure(lureId);
        if (IsNight != nightLure)
            group = -1;
        else
            group = fish.Group;

        var line = new FishingSession
        {
            BaitX = x,
            BaitY = y,
            BaitZ = z,
            LureId = lureId,
            Fish = fish,
            Group = group,
            GiveUpMs = now + fish.StartCombatTime * 1000L + FishingProtocol.GiveUpExtraMs,
            NextCheckMs = now + FishingProtocol.FirstCheckMs,
            CheckDelayMs = Math.Max(1000, FishingRules.CheckDelayMs(lureId, fish.GutsCheckTime))
        };
        _fishers[player.ObjectId] = line;
        player.Fishing = true;
        player.FishX = x;
        player.FishY = y;
        player.FishZ = z;
        NetBridge?.FanoutFishing(WatchersOf(player), new FishingEventNetMsg
        {
            Kind = FishingProtocol.EventStart,
            FisherObjectId = player.ObjectId,
            FishGroup = group,
            X = x,
            Y = y,
            Z = z,
            NightLure = nightLure
        });
        NetBridge?.FanoutPlaySound([player.ObjectId], 1, "SF_P_01", 0, 0, 0, 0, 0);
        ZoneLog.Info($"[fishing] cast object={player.ObjectId} lure={lureId} fish={fish.FishId} group={group} at {x},{y},{z}");
    }

    static readonly FishRow EmptyFish = new(0, 0, "", -1, 0, 0, 0, 1, 0, 0, 0, 1);

    void TickFishing(long now)
    {
        if (_fishers.Count == 0)
            return;
        foreach (int id in _fishers.Keys.ToArray())
        {
            if (!_fishers.TryGetValue(id, out var line))
                continue;
            if (!_byId.TryGetValue(id, out var player) || player.Session == null)
            {
                _fishers.Remove(id);
                continue;
            }

            if (player.Dead || !HoldsRod(player))
            {
                EndFishing(player, win: false);
                continue;
            }

            if (line.Fight == null)
                TickLookingForFish(player, line, now);
            else
                TickFight(player, line, now);
        }
    }

    bool HoldsRod(ZoneEntity player) =>
        player.Inventory?.GetEquipped(PaperdollSlot.RHand)?.Template.WeaponType == WeaponType.FishingRod;

    void TickLookingForFish(ZoneEntity player, FishingSession line, long now)
    {
        while (line.Fight == null && _fishers.ContainsKey(player.ObjectId) && now >= line.NextCheckMs)
        {
            long at = line.NextCheckMs;
            line.NextCheckMs += line.CheckDelayMs;
            if (at >= line.GiveUpMs)
            {
                EndFishing(player, win: false);
                return;
            }

            if (line.Group == -1)
                continue;
            if (line.Fish.Guts > _fishingRandom.Next(100))
                StartFight(player, line, at);
        }

        if (line.Fight == null && _fishers.ContainsKey(player.ObjectId) && now >= line.GiveUpMs)
            EndFishing(player, win: false);
    }

    void StartFight(ZoneEntity player, FishingSession line, long now)
    {
        bool noob = line.Fish.Grade == 0;
        bool upper = line.Fish.Grade == 2 || FishingRules.IsNightLure(line.LureId);
        int deceptive = 0, lureType;
        if (upper)
        {
            deceptive = _fishingRandom.Next(100) >= 90 ? 1 : 0;
            lureType = 2;
        }
        else
            lureType = noob ? 0 : 1;

        var fight = new FishingFight
        {
            Hp = line.Fish.Hp,
            MaxHp = line.Fish.Hp,
            Regen = (int)line.Fish.HpRegen,
            Time = line.Fish.CombatDuration,
            Deceptive = deceptive,
            Upper = upper,
            LureType = lureType,
            Mode = _fishingRandom.Next(100) >= 80 ? 1 : 0,
            NextTickMs = now + FishingProtocol.CombatTickMs
        };
        line.Fight = fight;
        NetBridge?.FanoutFishing(WatchersOf(player), new FishingEventNetMsg
        {
            Kind = FishingProtocol.EventCombat,
            FisherObjectId = player.ObjectId,
            Time = fight.Time,
            Hp = fight.MaxHp,
            Mode = fight.Mode,
            LureType = lureType,
            Deceptive = deceptive
        });
        NetBridge?.FanoutPlaySound([player.ObjectId], 1, "SF_S_01", 0, 0, 0, 0, 0);
        if (player.Session != null)
            SelfLine(player.Session, FishingProtocol.CombatBite, "You've got a bite!");
    }

    void TickFight(ZoneEntity player, FishingSession line, long now)
    {
        var fight = line.Fight!;
        while (now >= fight.NextTickMs && ReferenceEquals(line.Fight, fight) && _fishers.ContainsKey(player.ObjectId))
        {
            fight.NextTickMs += FishingProtocol.CombatTickMs;
            if (fight.Hp >= fight.MaxHp * 2)
            {
                SelfLine(player.Session!, FishingProtocol.CombatEscaped, "The fish has resisted your attempt to bring it in.");
                FinishFight(player, win: false);
                return;
            }

            if (fight.Time <= 0)
            {
                SelfLine(player.Session!, FishingProtocol.CombatTimeUp,
                    "Your fishing time is up and the fish got away.");
                FinishFight(player, win: false);
                return;
            }

            fight.Time--;
            if (fight.Mode == 1)
                fight.Hp += fight.Regen;
            if (fight.Stop == 0)
            {
                fight.Stop = 1;
                if (_fishingRandom.Next(100) >= 70)
                    fight.Mode = fight.Mode == 0 ? 1 : 0;
                if (fight.Upper && _fishingRandom.Next(100) >= 90)
                    fight.Deceptive = fight.Deceptive == 0 ? 1 : 0;
            }
            else
                fight.Stop--;

            var tick = HpEvent(player, fight, goodUse: 0, penalty: 0);
            NetBridge?.FanoutFishing(fight.Anim != 0 ? WatchersOf(player) : [player.ObjectId], tick);
        }
    }

    static FishingEventNetMsg HpEvent(ZoneEntity player, FishingFight fight, int goodUse, int penalty) => new()
    {
        Kind = FishingProtocol.EventHp,
        FisherObjectId = player.ObjectId,
        Time = fight.Time,
        Hp = fight.Hp,
        Mode = fight.Mode,
        GoodUse = goodUse,
        Anim = fight.Anim,
        Penalty = penalty,
        Deceptive = fight.Deceptive
    };

    void Pull(ZoneSession session, ZoneEntity player, bool reel, int skillLevel)
    {
        if (!_fishers.TryGetValue(player.ObjectId, out var line) || line.Fight is not { } fight)
        {
            if (reel)
                SelfLine(session, FishingProtocol.ReelOnlyFishing, "You may only use the Reeling skill while you are fishing.");
            else
                SelfLine(session, FishingProtocol.PumpOnlyFishing, "You may only use the Pumping skill while you are fishing.");
            return;
        }

        var rodItem = player.Inventory!.GetEquipped(PaperdollSlot.RHand);
        var rod = rodItem == null ? null : FishingTable.Rod(rodItem.ItemId);
        if (rod == null)
            return;

        int expertiseLevel = player.KnownSkills.GetValueOrDefault(FishingProtocol.ExpertiseSkillId);
        double expertise = expertiseLevel > 0 ? FishingTable.Power(expertiseLevel) : 1;
        bool shot = player.FishShotCharged;
        var (dmg, pen) = FishingRules.Damage(rod.Damage, rod.Level, expertise, FishingTable.Power(skillLevel), shot,
            expertiseLevel, skillLevel);
        if (pen > 0)
            SelfLine(session, FishingProtocol.ExpertisePenalty,
                "Due to your Reeling and/or Pumping skill being three or more levels higher than your Fishing skill, a 50 damage penalty will be applied.");
        if (shot)
            player.FishShotCharged = false;

        fight.Anim = reel ? 2 : 1;
        if (_fishingRandom.Next(100) > 90)
        {
            SelfLine(session, FishingProtocol.FishResisted, "The fish is resisting your efforts to haul it in! Look at that bobber go!");
            fight.GoodUse = 0;
            ChangeFishHp(player, line, fight, 0, 0);
            return;
        }

        // Reeling works on a fighting fish (mode 1), pumping on a resting one (mode 0); deception reverses both.
        bool works = reel ? (fight.Mode == 1) != (fight.Deceptive == 1) : (fight.Mode == 0) != (fight.Deceptive == 1);
        string skill = reel ? "Reeling" : "Pumping";
        if (works)
        {
            if (pen == 0)
                SelfLine(session, reel ? FishingProtocol.ReelSuccess : FishingProtocol.PumpSuccess,
                    $"{skill} is successful, damage: {dmg}", dmg.ToString());
            else
                SelfLine(session, reel ? FishingProtocol.ReelSuccessPenalty : FishingProtocol.PumpSuccessPenalty,
                    $"{skill} is successful. Mastery Penalty: {pen}", dmg.ToString(), pen.ToString());
            fight.GoodUse = 1;
            ChangeFishHp(player, line, fight, dmg, pen);
        }
        else
        {
            SelfLine(session, reel ? FishingProtocol.ReelFailed : FishingProtocol.PumpFailed,
                $"You failed at {skill}, and your target's HP has recovered by {dmg}.", dmg.ToString());
            fight.GoodUse = 2;
            ChangeFishHp(player, line, fight, -dmg, pen);
        }
    }

    void ChangeFishHp(ZoneEntity player, FishingSession line, FishingFight fight, int damage, int penalty)
    {
        fight.Hp = Math.Max(0, fight.Hp - damage);
        NetBridge?.FanoutFishing(WatchersOf(player), HpEvent(player, fight, fight.GoodUse, penalty));
        fight.Anim = 0;
        if (fight.Hp > fight.MaxHp * 2)
        {
            fight.Hp = fight.MaxHp * 2;
            FinishFight(player, win: false);
        }
        else if (fight.Hp == 0)
            FinishFight(player, win: true);
    }

    /// <summary>Mobius <c>Fishing.doDie</c>: a win rolls the level band's monster, otherwise the fish item.</summary>
    void FinishFight(ZoneEntity player, bool win)
    {
        if (win && _fishers.TryGetValue(player.ObjectId, out var line) && player.Session is { } session &&
            FishingTable.MonsterFor(player.Level) is { } monster)
        {
            if (_fishingRandom.Next(100) <= monster.Probability)
            {
                SelfLine(session, FishingProtocol.CaughtMonster, "You caught something smelly and scary, maybe you should throw it back!?");
                ApplyQuestSpawns(player, player, [monster.NpcId]);
            }
            else
            {
                SelfLine(session, FishingProtocol.CaughtSomething, "You caught something!");
                var inventory = player.Inventory!;
                int fishId = line.Fish.ItemId;
                RecordBagChange(
                    player,
                    LedgerTxKind.FishingReward,
                    LedgerAccount.Of(SystemAccount.Gathering),
                    () =>
                    {
                        GiveItem(session, inventory, fishId, 1);
                        string name = _game.Items.TryGet(fishId, out var t) ? t.Name : fishId.ToString();
                        SelfLine(session, ManorProtocol.ObtainedOne, $"You have obtained {name}.", name);
                        return true;
                    },
                    _ => fishId);
            }
        }

        EndFishing(player, win);
    }

    /// <summary>Mobius <c>Player.endFishing</c>.</summary>
    void EndFishing(ZoneEntity player, bool win)
    {
        if (!_fishers.Remove(player.ObjectId, out var line))
            return;
        player.Fishing = false;
        player.FishX = player.FishY = player.FishZ = 0;
        var session = player.Session;
        if (line.Fight == null && session != null)
            SelfLine(session, FishingProtocol.BaitLost, "Baits have been lost because the fish got away.");
        NetBridge?.FanoutFishing(WatchersOf(player), new FishingEventNetMsg
        {
            Kind = FishingProtocol.EventEnd,
            FisherObjectId = player.ObjectId,
            Win = win
        });
        if (session != null)
            SelfLine(session, FishingProtocol.StopFishing, "You reel your line in and stop fishing.");
        ZoneLog.Info($"[fishing] end object={player.ObjectId} win={win}");
    }

    /// <summary>Mobius <c>FishShots</c>: a rod of the shot's grade, charged once, the item skill shown to everyone.</summary>
    string? TryChargeFishShot(ZoneSession session, ZoneEntity player, ItemInstance shot)
    {
        var rod = player.Inventory!.GetEquipped(PaperdollSlot.RHand);
        if (rod == null || rod.Template.WeaponType != WeaponType.FishingRod)
            return null;
        if (player.FishShotCharged)
            return null;
        if (rod.Template.Crystal != shot.Template.Crystal)
        {
            SelfLine(session, FishingProtocol.ShotGradeMismatch, "That is the wrong grade of soulshot for that fishing pole.");
            return null;
        }

        int left = player.Inventory.TakeOne(shot);
        NetBridge?.FanoutInventoryUpdate([player.ObjectId],
            InventoryMessages.Update(left > 0 ? InventoryChange.Modify : InventoryChange.Remove, shot));
        SaveInventory(session, player.Inventory);
        player.FishShotCharged = true;
        NetBridge?.FanoutMagicSkillUse(WatchersOf(player),
            BuildCast(player, player, FishingProtocol.FishShotSkill(shot.ItemId), 1, 0, 0));
        return null;
    }

    /// <summary>Mobius <c>Fisherman.showFishSkillList</c>: the next level of each guild skill the player's level allows.</summary>
    List<FishingSkillRow> FishingOffers(ZoneEntity player) =>
        FishingTable.SkillTree
            .Where(r => r.GetLevel <= player.Level && player.KnownSkills.GetValueOrDefault(r.SkillId) == r.Level - 1)
            .OrderBy(r => r.SkillId).ThenBy(r => r.Level)
            .ToList();

    string? ShowFishingSkills(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        session.TrainerNpcId = npc.ObjectId;
        var rows = FishingOffers(player);
        if (rows.Count == 0)
        {
            int next = FishingTable.SkillTree
                .Where(r => r.GetLevel > player.Level && player.KnownSkills.GetValueOrDefault(r.SkillId) == r.Level - 1)
                .Select(r => r.GetLevel).DefaultIfEmpty(0).Min();
            if (next > 0)
                SelfLine(session, 607, $"You do not have any further skills to learn. Come back when you have reached Level {next}.",
                    next.ToString());
            else
                SelfLine(session, 750, "There are no other skills to learn.");
        }

        NetBridge?.FanoutAcquireSkillList([player.ObjectId], new AcquireSkillListMsg
        {
            SkillType = FishingProtocol.AcquireSkillTypeFishing,
            Skills = rows.Select(r => new AcquireSkillEntryMsg
            {
                SkillId = r.SkillId,
                NextLevel = r.Level,
                MaxLevel = r.Level,
                SpCost = 0,
                Requirements = 1
            }).ToArray()
        });
        return null;
    }

    void PublishFishingSkillInfo(ZoneEntity player, int skillId, int level)
    {
        var row = FishingOffers(player).FirstOrDefault(r => r.SkillId == skillId && r.Level == level);
        if (row == null || NetBridge == null)
            return;
        NetBridge.FanoutAcquireSkillInfo([player.ObjectId], new AcquireSkillInfoMsg
        {
            SkillId = row.SkillId,
            Level = row.Level,
            SpCost = 0,
            SkillType = FishingProtocol.AcquireSkillTypeFishing,
            Requirements = row.Items.Select(i => new AcquireSkillRequirementMsg { Type = 4, ItemId = i.ItemId, Count = i.Count, Unk = 0 }).ToArray()
        });
    }

    string? LearnFishingSkill(ZoneSession session, ZoneEntity player, int skillId, int level)
    {
        var npc = TrainerNpcOf(session);
        var row = FishingOffers(player).FirstOrDefault(r => r.SkillId == skillId && r.Level == level);
        if (row == null)
        {
            ShowFishingSkills(session, player, npc);
            return "bad_request";
        }

        var inventory = player.Inventory!;
        if (row.Items.Any(i => inventory.CountOf(i.ItemId) < i.Count))
        {
            SelfLine(session, 276, "You do not have the necessary materials or prerequisites to learn this skill.");
            ShowFishingSkills(session, player, npc);
            return null;
        }

        var changes = new List<InventoryUpdateEntryMsg>();
        foreach (var (itemId, count) in row.Items)
            Take(inventory, itemId, count, changes);
        if (changes.Count > 0)
        {
            FanInventory(session, Collapse(changes));
            SaveInventory(session, inventory);
        }

        player.LearnedSkills[row.SkillId] = row.Level;
        RefreshKnownSkills(player);
        _persist.ReplaceLearnedSkills(session.Account, session.ObjectId,
            player.LearnedSkills.Select(p => new Fineage.Accounts.StoredSkill(p.Key, p.Value)).ToList());
        NetBridge?.FanoutSkillList([player.ObjectId], Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, NowUnixMs()));
        string name = _game.Skills.Find(row.SkillId)?.Name ?? $"Skill {row.SkillId}";
        SelfLine(session, 277, $"You have earned {name}.", name);
        ShowFishingSkills(session, player, npc);
        return null;
    }

    internal void TickFishingForTests()
    {
        using (_affinity.Step())
            TickFishing(NowUnixMs());
    }

    internal (int Hp, int MaxHp, int Mode, int Deceptive, int Time)? FishFightForTests(int objectId)
    {
        using (_affinity.Step())
            return _fishers.TryGetValue(objectId, out var l) && l.Fight is { } f ? (f.Hp, f.MaxHp, f.Mode, f.Deceptive, f.Time) : null;
    }

    internal (int FishId, int Group, int BaitX, int BaitY, int BaitZ)? FishingLineForTests(int objectId)
    {
        using (_affinity.Step())
            return _fishers.TryGetValue(objectId, out var l) ? (l.Fish.FishId, l.Group, l.BaitX, l.BaitY, l.BaitZ) : null;
    }

    internal void SetFishModeForTests(int objectId, int mode, int deceptive)
    {
        using (_affinity.Step())
        {
            if (_fishers.TryGetValue(objectId, out var l) && l.Fight is { } f)
            {
                f.Mode = mode;
                f.Deceptive = deceptive;
            }
        }
    }
}
