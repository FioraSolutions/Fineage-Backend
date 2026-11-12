namespace Fineage.Net.Contract
{
    public sealed class ZoneAuthMsg
    {
        public string Account { get; set; } = "";
        public string Token { get; set; } = "";
    }

    public sealed class ZoneAuthResultMsg
    {
        public bool Ok { get; set; }
        public string Code { get; set; } = "";
        public string Message { get; set; } = "";
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
    }

    public sealed class EnterWorldMsg
    {
    }

    public enum EntityKindNet : byte
    {
        Player = 1,
        Npc = 2,
        Item = 3,
    }

    public sealed class EntitySpawnMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        public EntityKindNet Kind { get; set; }
        public bool IsSelf { get; set; }
        public string Name { get; set; } = "";
        public int TemplateId { get; set; }
        public int ClassId { get; set; }
        public int Race { get; set; }
        public int Sex { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int Heading { get; set; }
        public float RunSpeed { get; set; }
        public float WalkSpeed { get; set; }
        public bool Running { get; set; } = true;
        /// <summary>NPC only: true when the entity is combat-attackable (mobs).</summary>
        public bool Attackable { get; set; }
        /// <summary>Player only (Mobius CharInfo): equipped item ids per paperdoll slot; empty for NPCs / old peers.</summary>
        public int[] Paperdoll { get; set; } = System.Array.Empty<int>();
        /// <summary>Mobius isAlikeDead: the entity is a corpse when it enters view (late observer sees it lying, not standing).</summary>
        public bool Dead { get; set; }
        /// <summary>Mobius level: drives NPC target colour (InterludeStatus.TargetColor) and level in the nameplate.</summary>
        public int Level { get; set; }
        /// <summary>Max HP so a client can size the HP bar without waiting for the first StatusUpdate.</summary>
        public int MaxHp { get; set; }
        /// <summary>Physical attack speed (Mobius P.AtkSpd); drives client attack anim timing. Optional wire tail.</summary>
        public int PAtkSpd { get; set; }
        /// <summary>Mobius karma (PK red name). Optional wire tail; 0 for NPCs / old peers.</summary>
        public int Karma { get; set; }
        /// <summary>Mobius PvP flag (purple name). 0/1. Optional wire tail.</summary>
        public byte PvpFlag { get; set; }
        /// <summary>
        /// Current HP at enter-view (Mobius NpcInfo/CharInfo). Optional wire tail after <see cref="PvpFlag"/>.
        /// Absent on old peers — decode fills <see cref="MaxHp"/> (full) so select-target StatusUpdate is not a false hit.
        /// </summary>
        public int CurHp { get; set; }
    }

    public sealed class PlayerStatsMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        public int Level { get; set; }
        public long Exp { get; set; }
        public int Sp { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int Mp { get; set; }
        public int MaxMp { get; set; }
        public int Cp { get; set; }
        public int MaxCp { get; set; }
    }

    public sealed class EtcStatusUpdateNetMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        /// <summary>1–7 increase-force charges. Always 0 until the force system exists.</summary>
        public int Charges { get; set; }
        /// <summary>0 none, 1 ≥50%, 2 ≥66.6%, 3 ≥80%, 4 ≥100% of the load limit (Mobius skill 4270 level).</summary>
        public int WeightPenalty { get; set; }
        /// <summary>Mobius writes 1 when chat is refused, banned, or in silence mode.</summary>
        public bool ChatBlocked { get; set; }
        /// <summary>Inside a Mobius DANGER_AREA zone.</summary>
        public bool DangerArea { get; set; }
        /// <summary>
        /// Mobius writes 1 when either expertise penalty is above 0 (the packet field is a flag, not the 1–4 level).
        /// </summary>
        public bool GradePenalty { get; set; }
        /// <summary>Charm of Courage: allows resurrection on the same spot during a siege.</summary>
        public bool CharmOfCourage { get; set; }
        /// <summary>1–15 death-penalty buff level.</summary>
        public int DeathPenalty { get; set; }
    }

    public sealed class EntityDespawnMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
    }

    public sealed class PaperdollSlotNet
    {
        public int ObjectId { get; set; }
        public int ItemId { get; set; }
        public int EnchantLevel { get; set; }
    }

    /// <summary>
    /// Mobius UserInfo for the owner: full character sheet (vitals, base stats, combat stats from the live
    /// server stat pipeline, speeds and paperdoll). Sent on enter and whenever gear/buffs/level change the sheet.
    /// </summary>

    public sealed class UserInfoNetMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        public string Name { get; set; } = "";
        public int ClassId { get; set; }
        public int Race { get; set; }
        public int Sex { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public float RunSpeed { get; set; }
        public float WalkSpeed { get; set; }
        public bool Running { get; set; } = true;
        public int Level { get; set; } = 1;
        public long Exp { get; set; }
        public int Sp { get; set; }
        public int CurHp { get; set; }
        public int MaxHp { get; set; }
        public int CurMp { get; set; }
        public int MaxMp { get; set; }
        public int CurCp { get; set; }
        public int MaxCp { get; set; }
        public int Str { get; set; }
        public int Dex { get; set; }
        public int Con { get; set; }
        public int Int { get; set; }
        public int Wit { get; set; }
        public int Men { get; set; }
        public int PAtk { get; set; }
        public int MAtk { get; set; }
        public int PDef { get; set; }
        public int MDef { get; set; }
        public int PAtkSpd { get; set; }
        public int MAtkSpd { get; set; }
        public int Accuracy { get; set; }
        public int Evasion { get; set; }
        public int CritRate { get; set; }
        public int WeaponGradePenalty { get; set; }
        public int ArmorGradePenalty { get; set; }
        public int ClanId { get; set; }
        /// <summary>Mobius currentLoad — inventory weight for the status/inventory gauges.</summary>
        public int CurrentWeight { get; set; }
        /// <summary>Mobius getMaxLoad — CON-derived carry limit.</summary>
        public int MaxWeight { get; set; }
        public PaperdollSlotNet[] Paperdoll { get; set; } = System.Array.Empty<PaperdollSlotNet>();
        /// <summary>Mobius karma — PK name colour. Optional wire tail.</summary>
        public int Karma { get; set; }
        /// <summary>Mobius PvP flag — purple name while flagged. Optional wire tail.</summary>
        public byte PvpFlag { get; set; }
    }
}
