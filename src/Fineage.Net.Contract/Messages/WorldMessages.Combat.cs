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

        /// <summary>
        /// Ctrl / force-attack. Optional wire tail (one byte). Absent (old clients) = legacy behaviour, treated as forced.
        /// New clients send 0 for a plain click: a white (non-flagged, karma 0) player or a party mate is then refused.
        /// </summary>
        public bool? Forced { get; set; }
    }

    /// <summary>
    /// Zone → client: Mobius <c>MyTargetSelected</c> (self, <see cref="SelectorObjectId"/> = 0) or
    /// <c>TargetSelected</c> to AOI peers (<see cref="SelectorObjectId"/> = the aiming player).
    /// </summary>
    public sealed class TargetSelectedNetMsg
    {
        public int TargetObjectId { get; set; }
        public int ColorHint { get; set; }
        /// <summary>0 = self (nameplate colour). Else object id of the player who aimed.</summary>
        public int SelectorObjectId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
    }

    /// <summary>
    /// Zone → client: the player's selection was dropped (Mobius <c>TargetUnselected</c>), so the target
    /// window and nameplate close. Self: <see cref="TargetObjectId"/> is the cleared target.
    /// Peers: <see cref="SelectorObjectId"/> is the player who cleared (Mobius writes that creature id).
    /// </summary>
    public sealed class TargetUnselectedNetMsg
    {
        public int TargetObjectId { get; set; }
        /// <summary>0 = self clear. Else object id of the player who dropped their aim (peer notify).</summary>
        public int SelectorObjectId { get; set; }
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

        /// <summary>
        /// Further hits of the same swing (Interlude Attack packet hit list: dual / fist second hit). Optional wire
        /// tail: <c>int32 count</c> + rows <c>int32 damage, byte flags</c>. Absent ⇒ single hit.
        /// </summary>
        public AttackHitNet[] ExtraHits { get; set; } = System.Array.Empty<AttackHitNet>();
    }

    public sealed class AttackHitNet
    {
        public int Damage { get; set; }
        public byte Flags { get; set; }
    }

    /// <summary>Interlude MagicSkillLaunched: the skill fires at hitTime on <see cref="TargetObjectIds"/>.</summary>
    public sealed class MagicSkillLaunchedNetMsg
    {
        public int CasterObjectId { get; set; }
        public int SkillId { get; set; }
        public int SkillLevel { get; set; }
        public int[] TargetObjectIds { get; set; } = System.Array.Empty<int>();
    }

    /// <summary>Interlude MagicSkillCanceled: stop the cast animation of <see cref="ObjectId"/>.</summary>
    public sealed class MagicSkillCanceledNetMsg
    {
        public int ObjectId { get; set; }
    }

    /// <summary>
    /// Interlude PlaySound: <see cref="Type"/> 0 = sound effect, 1 = music; <see cref="File"/> e.g.
    /// <c>ItemSound.quest_accept</c>; <see cref="ObjectId"/> 0 = not bound to an object (play at the listener).
    /// </summary>
    public sealed class PlaySoundNetMsg
    {
        public int Type { get; set; }
        public string File { get; set; } = "";
        public int ObjectId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int DelayMs { get; set; }
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
        /// <summary>
        /// Optional tail (Mobius <c>Die</c> restart buttons, dead player only): bit0 to castle, bit1 to siege
        /// headquarters, bit2 to clan hall. Absent on older frames and on NPC deaths = 0.
        /// </summary>
        public int RestartFlags { get; set; }

        public const int RestartCastle = 1;
        public const int RestartSiegeHq = 2;
        public const int RestartClanHall = 4;
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
        /// <summary>Restart inside the castle (owner / defender clan). Matches <c>DeathProtocol.ReviveToCastle</c>.</summary>
        public const int ReviveToCastle = 3;
        /// <summary>Restart at the clan's siege headquarters. Matches <c>DeathProtocol.ReviveToSiegeHq</c>.</summary>
        public const int ReviveToSiegeHq = 4;
        /// <summary>Restart inside the clan's owned clan hall. Matches <c>DeathProtocol.ReviveToClanHall</c>.</summary>
        public const int ReviveToClanHall = 5;

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
        /// <summary>Mobius <c>abnormalVisualEffect</c> (STEALTH, DOT_POISON, …). Empty when unset.</summary>
        public string Visual { get; set; } = "";
    }

    public sealed class AbnormalStatusUpdateNetMsg
    {
        public int ObjectId { get; set; }
        public AbnormalEffectNet[] Effects { get; set; } = System.Array.Empty<AbnormalEffectNet>();
    }
}
