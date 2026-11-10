using Fineage.Game;
using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// Projects the live <see cref="ZoneEntity"/> produced by <see cref="ZoneWorld.TryEnterAsync"/>
/// onto Net enter packets (Mobius enter order: spawn → stats → item list → skill list).
/// </summary>
static class NetEnterWorldProjector
{
    public readonly record struct EnterPackets(
        EntitySpawnMsg Spawn,
        Fineage.Net.Contract.PlayerStatsMsg Stats,
        ItemListNetMsg Items,
        SkillListMsg Skills);

    public static EnterPackets FromEntity(ZoneEntity entity, EntityId self, long nowUnixMs, int playerPAtkSpd = 0)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.Kind != ZoneEntityKind.Player)
            throw new ArgumentException("Net enter requires a player entity.", nameof(entity));
        if (entity.Inventory == null)
            throw new InvalidOperationException($"Player {entity.ObjectId} has no inventory.");

        int maxCp = ResolveMaxCp(entity);
        return new EnterPackets(
            BuildSpawn(entity, self, isSelf: true, playerPAtkSpd),
            BuildStats(entity, self, maxCp),
            BuildItems(entity.Inventory),
            BuildSkills(entity, nowUnixMs));
    }

    /// <summary>Remote peer appearance (Mobius CharInfo) for Net knownlist.</summary>
    public static EntitySpawnMsg BuildRemoteSpawn(ZoneEntity entity, EntityId id, int playerPAtkSpd = 0) =>
        BuildSpawn(entity, id, isSelf: false, playerPAtkSpd);

    /// <summary>Player or NPC presence packet for Net knownlist (Mobius CharInfo / NpcInfo).</summary>
    public static EntitySpawnMsg BuildPresenceSpawn(ZoneEntity entity, EntityId id, int playerPAtkSpd = 0)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return entity.Kind switch
        {
            ZoneEntityKind.Player => BuildSpawn(entity, id, isSelf: false, playerPAtkSpd),
            ZoneEntityKind.Npc => BuildNpcSpawn(entity, id),
            _ => throw new ArgumentException($"Net presence spawn unsupported for {entity.Kind}.", nameof(entity))
        };
    }

    public static EntitySpawnMsg BuildNpcSpawn(ZoneEntity entity, EntityId id)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.Kind != ZoneEntityKind.Npc)
            throw new ArgumentException("Npc spawn requires an NPC entity.", nameof(entity));

        return new EntitySpawnMsg
        {
            EntityValue = id.Value,
            EntityGeneration = id.Generation,
            Kind = EntityKindNet.Npc,
            IsSelf = false,
            Name = entity.Name,
            TemplateId = entity.NpcId,
            ClassId = 0,
            Race = 0,
            Sex = 0,
            X = entity.X,
            Y = entity.Y,
            Z = entity.Z,
            Heading = entity.Heading,
            RunSpeed = entity.RunSpeed,
            WalkSpeed = entity.WalkSpeed,
            Running = entity.Running,
            Attackable = entity.Attackable,
            Dead = entity.CurHp <= 0,
            Level = entity.Level,
            MaxHp = entity.MaxHp,
            PAtkSpd = entity.PAtkSpd > 0 ? entity.PAtkSpd : ZoneCombat.DefaultNpcPAtkSpd
        };
    }

    static EntitySpawnMsg BuildSpawn(ZoneEntity entity, EntityId id, bool isSelf, int pAtkSpd = 0) =>
        new()
        {
            EntityValue = id.Value,
            EntityGeneration = id.Generation,
            Kind = EntityKindNet.Player,
            IsSelf = isSelf,
            Name = entity.Name,
            TemplateId = entity.ClassId,
            ClassId = entity.ClassId,
            Race = entity.Race,
            Sex = entity.Sex,
            X = entity.X,
            Y = entity.Y,
            Z = entity.Z,
            Heading = entity.Heading,
            RunSpeed = entity.RunSpeed,
            WalkSpeed = entity.WalkSpeed,
            Running = entity.Running,
            Paperdoll = entity.Inventory == null ? [] : InventoryMessages.ItemIds(entity.Inventory),
            Dead = entity.Dead,
            Level = entity.Level,
            MaxHp = entity.MaxHp,
            PAtkSpd = pAtkSpd
        };

    static Fineage.Net.Contract.PlayerStatsMsg BuildStats(ZoneEntity entity, EntityId self, int maxCp) =>
        new()
        {
            EntityValue = self.Value,
            EntityGeneration = self.Generation,
            Level = entity.Level <= 0 ? 1 : entity.Level,
            Exp = entity.Exp,
            Sp = entity.Sp,
            Hp = entity.CurHp,
            MaxHp = entity.MaxHp,
            Mp = entity.CurMp,
            MaxMp = entity.MaxMp,
            Cp = maxCp,
            MaxCp = maxCp
        };

    static ItemListNetMsg BuildItems(PlayerInventory inventory)
    {
        var v2 = InventoryMessages.ItemList(inventory);
        var rows = new ItemListEntryNet[v2.Items.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            var src = v2.Items[i];
            rows[i] = new ItemListEntryNet
            {
                ObjectId = src.ObjectId,
                ItemId = src.ItemId,
                Count = src.Count,
                EnchantLevel = src.EnchantLevel,
                EquippedSlot = src.EquippedSlot,
                BodySlot = src.BodySlot ?? "",
                Kind = src.Kind ?? "",
                Icon = src.Icon ?? "",
                WeaponType = src.WeaponType ?? "",
                ArmorType = src.ArmorType ?? ""
            };
        }

        return new ItemListNetMsg { Items = rows };
    }

    /// <summary>
    /// Mobius <c>sendSkillList</c>: learned book + clan skills + ephemeral gear skills
    /// (item SA / complete armor sets). Higher level wins on id collision.
    /// </summary>
    internal static SkillListMsg BuildSkills(ZoneEntity entity, long nowUnixMs)
    {
        var skills = PlayerPassiveSources.Merge(entity.KnownSkills, entity.ClanSkills, entity.GearSkills);
        var rows = new List<SkillListEntryMsg>(skills.Count);
        foreach (var pair in skills.OrderBy(p => p.Key))
        {
            int reuse = 0;
            if (entity.SkillReuseUntilMs.TryGetValue(pair.Key, out long until) && until > nowUnixMs)
                reuse = (int)Math.Min(int.MaxValue, until - nowUnixMs);
            rows.Add(new SkillListEntryMsg
            {
                SkillId = pair.Key,
                Level = pair.Value,
                ReuseDelayMs = reuse
            });
        }

        return new SkillListMsg { Skills = rows.ToArray() };
    }

    internal static SkillListNetMsg ToNetSkillList(SkillListMsg list)
    {
        var src = list.Skills ?? [];
        var rows = new SkillListEntryNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            var row = src[i];
            rows[i] = new SkillListEntryNet
            {
                SkillId = row.SkillId,
                Level = row.Level,
                ReuseRemainingMs = row.ReuseDelayMs
            };
        }

        return new SkillListNetMsg { Skills = rows };
    }

    static int ResolveMaxCp(ZoneEntity entity)
    {
        int level = Math.Max(1, entity.Level);
        if (!ClassTemplates.Catalog.TryGet(entity.ClassId, out var template) || template == null)
            return 0;
        try
        {
            return template.ModifiedCp(level);
        }
        catch (ArgumentOutOfRangeException)
        {
            return template.MaxCp;
        }
    }
}
