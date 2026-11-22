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
        var core = FromEntityCore(entity, self, nowUnixMs, playerPAtkSpd);
        if (entity.Inventory == null)
            throw new InvalidOperationException($"Player {entity.ObjectId} has no inventory.");
        return core with { Items = BuildItems(entity.Inventory) };
    }

    /// <summary>Spawn, stats, and skills only — item list is built off the simulation thread during enter encode.</summary>
    public static EnterPackets FromEntityCore(ZoneEntity entity, EntityId self, long nowUnixMs, int playerPAtkSpd = 0)
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
            new ItemListNetMsg { Items = [] },
            new SkillListMsg { Skills = [] });
    }

    /// <summary>Remote peer appearance (Mobius CharInfo) for Net knownlist.</summary>
    public static EntitySpawnMsg BuildRemoteSpawn(ZoneEntity entity, EntityId id, int playerPAtkSpd = 0) =>
        BuildSpawn(entity, id, isSelf: false, playerPAtkSpd);

    /// <summary>
    /// State frames that must follow an <c>EntitySpawn</c> so a late joiner sees the pawn as it really is
    /// (Mobius <c>Creature.sendInfo</c>): a running pawn gets <c>MoveState</c> toward its live destination,
    /// a sitting player gets <c>ChangeWaitType</c> sitting. The spawn itself carries neither.
    /// </summary>
    public static List<PresenceWireSlot> PlanPresenceFollowUps(ZoneEntity entity, EntityId id, ZoneWorld world)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var slots = new List<PresenceWireSlot>(2);
        if (entity.SiegeNpc is { Role: SiegeNpcRole.Door } && world.DoorStatusFrame(entity) is { } door)
            slots.Add(new(NetOpcode.DoorStatus, null, door));
        if (entity.Dead)
            return slots;
        if (entity.MoveActive)
        {
            slots.Add(new(NetOpcode.MoveState, new MoveStateMsg
            {
                EntityValue = id.Value,
                EntityGeneration = id.Generation,
                OriginX = entity.X,
                OriginY = entity.Y,
                OriginZ = entity.Z,
                DestX = entity.MoveDestX,
                DestY = entity.MoveDestY,
                DestZ = entity.MoveDestZ,
                Running = entity.Running
            }));
        }
        if (entity.Kind == ZoneEntityKind.Player && entity.Sitting)
        {
            slots.Add(new(NetOpcode.ChangeWaitType, new ChangeWaitTypeNetMsg
            {
                EntityValue = id.Value,
                EntityGeneration = id.Generation,
                WaitType = ActionProtocol.WaitSitting,
                X = entity.X,
                Y = entity.Y,
                Z = entity.Z
            }));
        }
        if (world.TryBuildPresenceAbnormal(entity, out var abnormal))
            slots.Add(new(NetOpcode.AbnormalStatusUpdate, abnormal));
        return slots;
    }

    public static List<(ushort Opcode, byte[] Payload)> BuildPresenceFollowUps(ZoneEntity entity, EntityId id, ZoneWorld? world = null)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (world == null)
            return [];
        return PlanPresenceFollowUps(entity, id, world)
            .ConvertAll(slot => (slot.Opcode, EnterWire.EncodePresence(slot)));
    }

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

    public static NpcPresenceCapture CaptureNpcPresence(ZoneEntity entity, EntityId id)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.Kind != ZoneEntityKind.Npc)
            throw new ArgumentException("Npc spawn requires an NPC entity.", nameof(entity));

        return new NpcPresenceCapture(
            id.Value,
            id.Generation,
            entity.Name,
            entity.NpcId,
            entity.X,
            entity.Y,
            entity.Z,
            entity.Heading,
            entity.RunSpeed,
            entity.WalkSpeed,
            entity.Running,
            entity.Attackable,
            entity.Level,
            entity.MaxHp,
            entity.CurHp,
            entity.PAtkSpd > 0 ? entity.PAtkSpd : ZoneCombat.DefaultNpcPAtkSpd,
            entity.Summon != null ? entity.Title : "",
            entity.Summon?.Kind ?? SummonKindNet.None,
            entity.Summon?.OwnerObjectId ?? 0,
            entity.Rhand,
            entity.Lhand);
    }

    public static EntitySpawnMsg BuildNpcSpawn(NpcPresenceCapture npc) => new()
    {
        EntityValue = npc.EntityValue,
        EntityGeneration = npc.EntityGeneration,
        Kind = EntityKindNet.Npc,
        IsSelf = false,
        Name = npc.Name,
        TemplateId = npc.TemplateId,
        ClassId = 0,
        Race = 0,
        Sex = 0,
        X = npc.X,
        Y = npc.Y,
        Z = npc.Z,
        Heading = npc.Heading,
        RunSpeed = npc.RunSpeed,
        WalkSpeed = npc.WalkSpeed,
        Running = npc.Running,
        Attackable = npc.Attackable,
        Dead = npc.CurHp <= 0,
        Level = npc.Level,
        MaxHp = npc.MaxHp,
        PAtkSpd = npc.PAtkSpd,
        CurHp = npc.CurHp,
        Title = npc.Title,
        SummonKind = npc.SummonKind,
        OwnerObjectId = npc.OwnerObjectId,
        Rhand = npc.Rhand,
        Lhand = npc.Lhand
    };

    public static EntitySpawnMsg BuildNpcSpawn(ZoneEntity entity, EntityId id) =>
        BuildNpcSpawn(CaptureNpcPresence(entity, id));

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
            PAtkSpd = pAtkSpd,
            Karma = entity.Karma,
            PvpFlag = ZoneWorld.PvpFlagWire(entity),
            CurHp = entity.CurHp,
            Title = entity.Title ?? "",
            ClanId = entity.ClanId,
            ClanCrestId = entity.ClanCrestId,
            AllyId = entity.AllyId,
            AllyCrestId = entity.AllyCrestId,
            LargeCrestId = entity.LargeCrestId,
            PledgeClass = entity.PledgeClass,
            WeaponEnchant = Math.Min(127, entity.Inventory?.GetEquipped(Fineage.Game.PaperdollSlot.RHand)?.EnchantLevel ?? 0),
            Noble = entity.Noble,
            Hero = entity.Hero,
            Cubics = entity.CubicIds(),
            Team = entity.DuelTeam
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
            Cp = entity.MaxCp > 0 ? entity.CurCp : maxCp,
            MaxCp = entity.MaxCp > 0 ? entity.MaxCp : maxCp
        };

    public static ItemListNetMsg BuildItems(PlayerInventory inventory)
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
                ArmorType = src.ArmorType ?? "",
                Option1 = src.Option1,
                Option2 = src.Option2
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
        var rows = new List<SkillListEntryMsg>();
        foreach (var pair in PlayerPassiveSources.EnumerateOrdered(entity.KnownSkills, entity.ClanSkills, entity.GearSkills, entity.CursedSkills))
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
