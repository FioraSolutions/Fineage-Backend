namespace Fineage.Net.Contract
{
    public sealed class AttackStanceNetMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
    }

    /// <summary>Payload of <c>RequestAutoSoulShot</c> and <c>AutoSoulShot</c>.</summary>

    public sealed class AutoSoulShotNetMsg
    {
        public int ItemId { get; set; }
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// One Mobius shortcut. <see cref="Slot"/> is the client world slot (slot + page * 12).
    /// <see cref="Type"/>: 1 item (id = item object id), 2 skill (id = skill id), 3 action, 4 macro, 5 recipe.
    /// Payload of <c>RequestShortCutReg</c> and <c>ShortCutRegister</c>; rows of <c>ShortCutInit</c>.
    /// </summary>

    public sealed class SelectTargetNetMsg
    {
        public int TargetObjectId { get; set; }
    }

    public sealed class AttackRequestNetMsg
    {
        public int TargetObjectId { get; set; }
    }

    public sealed class TargetSelectedNetMsg
    {
        public int TargetObjectId { get; set; }
        public int ColorHint { get; set; }
    }

    /// <summary>
    /// Zone → client: the player's selection was dropped (Mobius <c>TargetUnselected</c>), so the target
    /// window and nameplate close. Carries the object id that was unselected, 0 when there was none.
    /// </summary>

    public sealed class TargetUnselectedNetMsg
    {
        public int TargetObjectId { get; set; }
    }

    public sealed class AttackStateMsg
    {
        public ulong AttackerValue { get; set; }
        public uint AttackerGeneration { get; set; }
        public ulong TargetValue { get; set; }
        public uint TargetGeneration { get; set; }
        public int Damage { get; set; }
        public byte Flags { get; set; }
        public int AttackerX { get; set; }
        public int AttackerY { get; set; }
        public int AttackerZ { get; set; }
        public int TargetX { get; set; }
        public int TargetY { get; set; }
        public int TargetZ { get; set; }
    }

    public sealed class StatusUpdateNetMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        public int[] AttributeIds { get; set; } = System.Array.Empty<int>();
        public int[] Values { get; set; } = System.Array.Empty<int>();
    }

    public sealed class EntityDieMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        public int KillerObjectId { get; set; }
    }

    public sealed class UseSkillNetMsg
    {
        public int SkillId { get; set; }
        public int TargetObjectId { get; set; }
        public bool CtrlPressed { get; set; }
    }

    public sealed class MagicSkillUseNetMsg
    {
        public int CasterObjectId { get; set; }
        public int TargetObjectId { get; set; }
        public int SkillId { get; set; }
        public int SkillLevel { get; set; }
        public int HitTimeMs { get; set; }
        public int ReuseDelayMs { get; set; }
    }

    public sealed class ReviveRequestNetMsg
    {
        /// <summary>Restart to village. Matches Backend <c>Fineage.Protocol.DeathProtocol.ReviveToVillage</c>.</summary>
        public const int ReviveToVillage = 0;
        /// <summary>Accept skill resurrection. Matches <c>DeathProtocol.AcceptSkillResurrection</c>.</summary>
        public const int AcceptSkillResurrection = 1;
        /// <summary>Decline skill resurrection. Matches <c>DeathProtocol.DeclineSkillResurrection</c>.</summary>
        public const int DeclineSkillResurrection = 2;

        public int Action { get; set; }
    }

    public sealed class ReviveNetMsg
    {
        public int ObjectId { get; set; }
    }

    public sealed class AbnormalEffectNet
    {
        public int Id { get; set; }
        public int Value { get; set; }
        public int DurationSeconds { get; set; }
        /// <summary>Mobius <c>abnormalType</c> slot (SLEEP, STUN, PARALYZE, …).</summary>
        public string Slot { get; set; } = "";
    }

    public sealed class AbnormalStatusUpdateNetMsg
    {
        public int ObjectId { get; set; }
        public AbnormalEffectNet[] Effects { get; set; } = System.Array.Empty<AbnormalEffectNet>();
    }
}
