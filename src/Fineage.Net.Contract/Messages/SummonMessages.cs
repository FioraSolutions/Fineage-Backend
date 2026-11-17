namespace Fineage.Net.Contract
{
    /// <summary>What a summon-control action asks for (Interlude <c>RequestActionUse</c> pet / servitor actions).</summary>
    public enum SummonCommandKind : byte
    {
        /// <summary>Follow the owner (Mobius action 15/21 when the summon stays).</summary>
        Follow = 1,
        /// <summary>Stay where it is (Mobius action 15/21 when it follows).</summary>
        Stay = 2,
        /// <summary>Attack <see cref="SummonCommandNetMsg.TargetObjectId"/> (Mobius 16/22; <see cref="SummonCommandNetMsg.Forced"/> = Ctrl).</summary>
        Attack = 3,
        /// <summary>Drop the current target and stand (Mobius 17/23).</summary>
        Stop = 4,
        /// <summary>Walk to X/Y/Z (Mobius RequestPetMove / action 53/54).</summary>
        Move = 5,
        /// <summary>Cast <see cref="SummonCommandNetMsg.SkillId"/> on the target (Mobius pet/servitor skill actions).</summary>
        UseSkill = 6,
        /// <summary>Dismiss the servitor / return the pet to its collar (Mobius 19 / 52).</summary>
        Unsummon = 7,
        /// <summary>Pet picks up the ground item <see cref="SummonCommandNetMsg.TargetObjectId"/> (Mobius RequestPetGetItem).</summary>
        PickUp = 8,
        /// <summary>Owner gives <see cref="SummonCommandNetMsg.Count"/> of bag item <see cref="SummonCommandNetMsg.ItemObjectId"/> to the pet (RequestGiveItemToPet).</summary>
        GiveItem = 9,
        /// <summary>Owner takes a pet item back (RequestGetItemFromPet).</summary>
        TakeItem = 10,
        /// <summary>Pet uses / equips one of its items (RequestPetUseItem): food, collar armor, weapon.</summary>
        UseItem = 11,
    }

    /// <summary>Client → zone: one summon command (<see cref="NetOpcode.SummonCommand"/>).</summary>
    public sealed class SummonCommandNetMsg
    {
        public SummonCommandKind Kind { get; set; }
        public int TargetObjectId { get; set; }
        public int SkillId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public bool Forced { get; set; }
        public int ItemObjectId { get; set; }
        public int Count { get; set; }
    }

    /// <summary>Summon kind on the wire (EntitySpawn tail / SummonInfo).</summary>
    public enum SummonKindNet : byte
    {
        None = 0,
        Servitor = 1,
        Pet = 2,
    }

    /// <summary>
    /// Zone → owner: the summon window (Mobius <c>PetInfo</c> / <c>PetStatusUpdate</c> / <c>SetSummonRemainTime</c>).
    /// Sent when the summon appears and whenever bars, exp, hunger, lifetime or mode change (coalesced).
    /// </summary>
    public sealed class SummonInfoNetMsg
    {
        public int ObjectId { get; set; }
        public SummonKindNet Kind { get; set; }
        public int NpcId { get; set; }
        public string Name { get; set; } = "";
        public int Level { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int Mp { get; set; }
        public int MaxMp { get; set; }
        /// <summary>Pets: current exp and the exp bounds of the current level. Servitors: 0.</summary>
        public long Exp { get; set; }
        public long ExpLevelStart { get; set; }
        public long ExpLevelEnd { get; set; }
        /// <summary>Pets: hunger meter (Mobius current_feed / max_meal). Servitors: lifetime left / total in seconds.</summary>
        public int Feed { get; set; }
        public int MaxFeed { get; set; }
        public int PAtk { get; set; }
        public int PDef { get; set; }
        public int MAtk { get; set; }
        public int MDef { get; set; }
        public int Accuracy { get; set; }
        public int Evasion { get; set; }
        public int Critical { get; set; }
        public int PAtkSpd { get; set; }
        public int CastSpd { get; set; }
        public int RunSpeed { get; set; }
        /// <summary>1 follows the owner, 0 stays.</summary>
        public bool Following { get; set; }
        public int TargetObjectId { get; set; }
        /// <summary>Pets: weight carried / limit of the pet inventory.</summary>
        public int Load { get; set; }
        public int MaxLoad { get; set; }
        /// <summary>Skills the summon can be told to use (Mobius PetSkillData / NPC skill list).</summary>
        public int[] SkillIds { get; set; } = System.Array.Empty<int>();
        public bool Dead { get; set; }
    }

    /// <summary>Zone → owner: the summon left (Mobius <c>PetDelete</c>). <see cref="Reason"/>: 0 dismissed, 1 lifetime, 2 upkeep, 3 died, 4 owner left.</summary>
    public sealed class SummonGoneNetMsg
    {
        public int ObjectId { get; set; }
        public SummonKindNet Kind { get; set; }
        public int Reason { get; set; }
    }

    public sealed class PetItemNet
    {
        public int ObjectId { get; set; }
        public int ItemId { get; set; }
        public int Count { get; set; }
        public int EnchantLevel { get; set; }
        public bool Equipped { get; set; }
    }

    /// <summary>Zone → owner: full pet inventory (Mobius <c>PetItemList</c>), after every change.</summary>
    public sealed class PetInventoryNetMsg
    {
        public int PetObjectId { get; set; }
        public PetItemNet[] Items { get; set; } = System.Array.Empty<PetItemNet>();
    }
}
