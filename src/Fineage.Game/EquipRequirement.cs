namespace Fineage.Game;

/// <summary>Why an equip condition failed. The zone maps these onto <c>Error</c> 1099 codes.</summary>
public enum EquipDeny
{
    Class,
    Race,
    Sex,
    Hero,
    ClanLeader,
    Pledge,
    Pvp,
    Level,
    Castle,
    ClanHall,
    Pk,
    /// <summary>Exact Expertise rank required by an item condition. This is not the grade penalty.</summary>
    Grade,
    Condition
}

/// <summary>
/// Who is trying to equip. Expertise is skill 239's auto-get rank for <see cref="Level"/>.
/// </summary>
public readonly record struct EquipActor(
    int ClassId,
    int Race,
    int Sex,
    int Level,
    bool Hero = false,
    bool InClan = false,
    bool ClanLeader = false,
    int PledgeClass = 0,
    int CastleId = 0,
    int HideoutId = 0,
    bool PvpFlagged = false,
    int PkKills = 0)
{
    public int ExpertiseLevel => Expertise.ForLevel(Level);
}

/// <summary>
/// One equip condition from item data. Attribute checks follow Mobius player conditions:
/// class id must be listed, race must be listed, sex is 0 male / 1 female, hero and PvP
/// flag are equality, pledge class requires a clan and passes for the clan leader
/// (pledge class -1 is the leader alone), castle -1 is any castle, and an exact
/// Expertise rank is a hard block when the item asks for one.
/// </summary>
public abstract record EquipRequirement
{
    public abstract EquipDeny? Deny(EquipActor actor);

    public sealed record AllOf(IReadOnlyList<EquipRequirement> Parts) : EquipRequirement
    {
        public override EquipDeny? Deny(EquipActor actor)
        {
            foreach (var part in Parts)
            {
                var deny = part.Deny(actor);
                if (deny != null)
                    return deny;
            }

            return null;
        }
    }

    public sealed record AnyOf(IReadOnlyList<EquipRequirement> Parts) : EquipRequirement
    {
        public override EquipDeny? Deny(EquipActor actor)
        {
            EquipDeny? first = null;
            foreach (var part in Parts)
            {
                var deny = part.Deny(actor);
                if (deny == null)
                    return null;
                first ??= deny;
            }

            return first ?? EquipDeny.Condition;
        }
    }

    public sealed record Negate(EquipRequirement Inner) : EquipRequirement
    {
        public override EquipDeny? Deny(EquipActor actor) =>
            Inner.Deny(actor) == null ? EquipDeny.Condition : null;
    }

    public sealed record Classes(IReadOnlyList<int> Ids) : EquipRequirement
    {
        public override EquipDeny? Deny(EquipActor actor) =>
            Contains(Ids, actor.ClassId) ? null : EquipDeny.Class;
    }

    public sealed record Races(IReadOnlyList<int> Ids) : EquipRequirement
    {
        public override EquipDeny? Deny(EquipActor actor) =>
            Contains(Ids, actor.Race) ? null : EquipDeny.Race;
    }

    public sealed record SexIs(int Sex) : EquipRequirement
    {
        public override EquipDeny? Deny(EquipActor actor) =>
            actor.Sex == Sex ? null : EquipDeny.Sex;
    }

    public sealed record HeroIs(bool Expected) : EquipRequirement
    {
        public override EquipDeny? Deny(EquipActor actor) =>
            actor.Hero == Expected ? null : EquipDeny.Hero;
    }

    public sealed record ClanLeaderIs(bool Expected) : EquipRequirement
    {
        public override EquipDeny? Deny(EquipActor actor) =>
            actor.ClanLeader == Expected ? null : EquipDeny.ClanLeader;
    }

    public sealed record PvpIs(bool Expected) : EquipRequirement
    {
        public override EquipDeny? Deny(EquipActor actor) =>
            actor.PvpFlagged == Expected ? null : EquipDeny.Pvp;
    }

    public sealed record MinLevel(int Level) : EquipRequirement
    {
        public override EquipDeny? Deny(EquipActor actor) =>
            actor.Level >= Level ? null : EquipDeny.Level;
    }

    /// <summary>Pledge-class gate. -1 means clan leader. A clan leader passes any positive rank.</summary>
    public sealed record PledgeAtLeast(int Rank) : EquipRequirement
    {
        public override EquipDeny? Deny(EquipActor actor)
        {
            if (!actor.InClan)
                return Rank == -1 ? EquipDeny.ClanLeader : EquipDeny.Pledge;
            if (Rank == -1 && !actor.ClanLeader)
                return EquipDeny.ClanLeader;
            if (actor.ClanLeader || actor.PledgeClass >= Rank)
                return null;
            return EquipDeny.Pledge;
        }
    }

    /// <summary>0 with no clan passes. -1 means any castle. Otherwise the castle id must match.</summary>
    public sealed record CastleIs(int Castle) : EquipRequirement
    {
        public override EquipDeny? Deny(EquipActor actor)
        {
            if (!actor.InClan)
                return Castle == 0 ? null : EquipDeny.Castle;
            if (Castle == -1)
                return actor.CastleId > 0 ? null : EquipDeny.Castle;
            return actor.CastleId == Castle ? null : EquipDeny.Castle;
        }
    }

    /// <summary>A single 0 with no clan passes. A single -1 means any clan hall.</summary>
    public sealed record ClanHalls(IReadOnlyList<int> Ids) : EquipRequirement
    {
        public override EquipDeny? Deny(EquipActor actor)
        {
            if (!actor.InClan)
                return Ids.Count == 1 && Ids[0] == 0 ? null : EquipDeny.ClanHall;
            if (Ids.Count == 1 && Ids[0] == -1)
                return actor.HideoutId > 0 ? null : EquipDeny.ClanHall;
            return Contains(Ids, actor.HideoutId) ? null : EquipDeny.ClanHall;
        }
    }

    public sealed record PkAtMost(int Kills) : EquipRequirement
    {
        public override EquipDeny? Deny(EquipActor actor) =>
            actor.PkKills <= Kills ? null : EquipDeny.Pk;
    }

    /// <summary>Hard block: Expertise rank must equal this crystal level. Not the grade penalty.</summary>
    public sealed record ExpertiseIs(int Rank) : EquipRequirement
    {
        public override EquipDeny? Deny(EquipActor actor) =>
            actor.ExpertiseLevel == Rank ? null : EquipDeny.Grade;
    }

    static bool Contains(IReadOnlyList<int> ids, int value)
    {
        foreach (int id in ids)
        {
            if (id == value)
                return true;
        }

        return false;
    }
}
