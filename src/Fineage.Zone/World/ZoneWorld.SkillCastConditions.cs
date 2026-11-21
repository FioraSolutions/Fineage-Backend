using Fineage.Game;
using Fineage.Game.Services;
using Fineage.Game.Skills;
using Fineage.Zone.Combat;
using Fineage.Zone.Npcs;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    ZoneEntity? ResolveSkillConditionTarget(
        ZoneSession session,
        ZoneEntity player,
        SkillUseIntent request,
        Fineage.Game.Skills.SkillDefinition template)
    {
        int targetId = request.TargetObjectId > 0 ? request.TargetObjectId : session.SelectedTargetId;
        if (targetId <= 0 && template.TargetType.Equals("SELF", StringComparison.OrdinalIgnoreCase))
            targetId = player.ObjectId;
        if (targetId <= 0)
            return null;
        return _byId.TryGetValue(targetId, out var target) ? target : null;
    }

    internal bool SkillCastConditionsMet(
        ZoneSession session,
        ZoneEntity player,
        ZoneEntity? target,
        Fineage.Game.Skills.SkillDefinition template,
        int skillId,
        out int? refusalMessageId)
    {
        refusalMessageId = null;
        if (template.CastCondition != null)
        {
            if (IsFishingSkill(skillId))
                return true;
            var ctx = new SkillCastCtx(this, player, target);
            if (!SkillCastConditionEvaluator.Met(template.CastCondition, ctx))
            {
                refusalMessageId = ctx.RefusalMessageId;
                return false;
            }
            return true;
        }

        if (template.UsingKinds.Length > 0 && !IsFishingSkill(skillId) && !Wearing(player, template.UsingKinds))
            return false;
        return PlayerConditionsMet(session, player, template);
    }

    internal string NpcRaceOf(ZoneEntity? entity)
    {
        if (entity is not { Kind: ZoneEntityKind.Npc })
            return "";
        var catalog = NpcCatalog.LoadDefault();
        return catalog.ById.TryGetValue(entity.NpcId, out var t) ? (t.Race ?? "") : "";
    }

    internal bool CanSweepForSkillCheck(ZoneEntity player, ZoneEntity? target)
    {
        if (target is not { Kind: ZoneEntityKind.Npc, Attackable: true } || target.CurHp > 0)
            return false;
        long remaining = target.DecayAtMs - NowUnixMs();
        var refusal = _spoilRules.Sweep(true, target.SpoilerObjectId, player.ObjectId,
            SameParty(player.ObjectId, target.SpoilerObjectId), remaining);
        return refusal == Fineage.Game.Services.SweepRefusal.None;
    }

    internal bool CanSummonForSkillCheck(ZoneEntity player) => SummonOf(player) == null && !player.Dead;

    internal bool CanSummonSiegeGolemForSkillCheck(ZoneEntity player) =>
        player.ClanId > 0 && !HasCursedWeapon(player.Inventory) && CastleAtEntity(player) > 0;

    internal bool CanCreateSiegeBaseForSkillCheck(ZoneEntity player) => player.ClanId > 0;

    internal bool CanTakeCastleForSkillCheck(ZoneEntity player) => player.ClanId > 0;

    internal bool CanResurrectForSkillCheck(ZoneEntity player, ZoneEntity? target) =>
        target is { Dead: true, Kind: ZoneEntityKind.Player };

    internal bool InsideZoneIdForTests(ZoneEntity player, int zoneId) => false;

    internal bool InSiegeZoneForTests(ZoneEntity player) =>
        CastleAtEntity(player) > 0 || SiegeAreas.CastleAt(player.X, player.Y, player.Z) > 0;

    sealed class SkillCastCtx(ZoneWorld world, ZoneEntity player, ZoneEntity? target) : SkillCastConditionContext
    {
        readonly long _now = world.NowUnixMs();

        public override void LogUnsupported(string message) => ZoneLog.Warn(message);
        public override bool UsingKinds(string[] kinds) => ConditionUsingItemType.Matches(player.Inventory, kinds);
        public override bool UsingSlot(string[] slots) => ConditionUsingItemType.MatchesSlot(player.Inventory, slots);
        public override int InventoryUsedSlots => player.Inventory?.Items.Count ?? 0;
        public override int InventoryLimit => WarehouseRules.InventorySlots(player.Race);
        public override int LoadPercent
        {
            get
            {
                int max = world.MaxLoadOf(player);
                if (max <= 0) return 0;
                long load = CurrentLoadOf(player) - world.BonusWeightPenaltyOf(player);
                return (int)(load * 100 / max);
            }
        }
        public override int HpPercent => player.MaxHp <= 0 ? 0 : (int)(player.CurHp * 100 / player.MaxHp);
        public override int MpPercent => player.MaxMp <= 0 ? 0 : (int)(player.CurMp * 100 / player.MaxMp);
        public override int SoulCharges => player.ForceCharges;
        public override int Level => player.Level;
        public override int Sex => player.Sex;
        public override string RaceName => player.Race switch
        {
            0 => "HUMAN",
            1 => "ELF",
            2 => "DARKELF",
            3 => "ORC",
            4 => "DWARF",
            _ => "HUMAN"
        };
        public override int ElementSeedTotal
        {
            get
            {
                int n = 0;
                foreach (var key in player.ElementSeedUntilMs.Keys)
                    n += world.ElementSeedCount(player, key, _now);
                return n;
            }
        }
        public override int ElementSeedCount(string element) =>
            string.IsNullOrWhiteSpace(element) ? 0 : world.ElementSeedCount(player, element.ToUpperInvariant(), _now);
        public override bool HasActiveEffect(int skillId, int level)
        {
            foreach (var ab in player.Abnormals)
            {
                if (ab.Id == skillId && Math.Max(ab.SkillLevel, ab.Value) >= level && ab.ExpiresAtUnixMs > _now)
                    return true;
            }
            return false;
        }
        public override bool HasActiveSkill(int skillId, int level) =>
            player.KnownSkills.TryGetValue(skillId, out int lv) && lv >= level;
        public override bool InsideZone(int zoneId) => world.InsideZoneIdForTests(player, zoneId);
        public override bool CanSweep() => world.CanSweepForSkillCheck(player, target);
        public override bool CanSummon() => world.CanSummonForSkillCheck(player);
        public override bool HasSummon => world.SummonOf(player) != null;
        public override bool CanSummonSiegeGolem() => world.CanSummonSiegeGolemForSkillCheck(player);
        public override bool CanCreateSiegeBase() => world.CanCreateSiegeBaseForSkillCheck(player);
        public override bool CanTakeCastle() => world.CanTakeCastleForSkillCheck(player);
        public override bool CanEscape() => false;
        public override bool CanResurrect() => world.CanResurrectForSkillCheck(player, target);
        public override bool CanCallPc() => false;
        public override bool InSiegeZone() => world.InSiegeZoneForTests(player);
        public override bool RidingStrider => player.Mount is { } m && m.NpcId is 12526 or 12527 or 12528;
        public override bool Flying => player.Mount is { WyvernRent: true };
        public override bool Resting => player.Sitting;
        public override bool Moving => player.MoveActive;
        public override bool Running => player.Running;
        public override bool Standing => !player.Sitting && !player.MoveActive;
        public override bool Chaotic => player.Karma > 0;
        public override int ClanHallId => 0;
        public override bool BehindTarget => target != null &&
            ZoneCombat.IsBehind(player.X, player.Y, target.X, target.Y, target.Heading);
        public override bool FrontOfTarget => target != null &&
            ZoneCombat.IsInFrontOf(player.X, player.Y, target.X, target.Y, target.Heading);
        public override string TargetRace => target == null ? "" :
            target.Kind == ZoneEntityKind.Player
                ? target.Race switch { 0 => "HUMAN", 1 => "ELF", 2 => "DARKELF", 3 => "ORC", 4 => "DWARF", _ => "" }
                : string.IsNullOrEmpty(target.CreatureRace) ? world.NpcRaceOf(target) : target.CreatureRace;
        public override int TargetNpcId => target?.NpcId ?? 0;
        public override string TargetNpcType => target?.NpcType ?? "";
        public override bool TargetIsPartyMateExceptSelf => false;
        public override bool TargetHasActiveSkill(int skillId)
        {
            if (target == null) return false;
            foreach (var ab in target.Abnormals)
            {
                if (ab.Id == skillId && ab.ExpiresAtUnixMs > _now)
                    return true;
            }
            return false;
        }
        public override bool IsNight => world.IsNight;
    }
}