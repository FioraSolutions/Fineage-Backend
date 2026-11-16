namespace Fineage.Protocol;

/// <summary>
/// Player death and village respawn. Request <see cref="ReviveRequest"/> (52). Replies <see cref="Die"/>–<see cref="TeleportToLocation"/> (1074–1076; Olympiad uses 1070–1073).
/// Layout: <c>docs/death-v2-protocol.md</c>.
/// </summary>
/// <remarks>Mobius Interlude: <c>Die</c>, <c>Revive</c>, <c>TeleportToLocation</c>, client restart to town.</remarks>
public static class DeathProtocol
{
    public const ushort ReviveRequest = 52;
    public const ushort AdminKillPlayer = 53;

    public const ushort Die = 1074;
    public const ushort Revive = 1075;
    public const ushort TeleportToLocation = 1076;

    /// <summary>Respawn at the Talking Island village restart (Mobius default for TI).</summary>
    public const int ReviveToVillage = 0;

    /// <summary>Accept a pending skill resurrection offer (Mobius <c>reviveAnswer(1)</c>).</summary>
    public const int AcceptSkillResurrection = 1;

    /// <summary>Decline a pending skill resurrection offer (Mobius <c>reviveAnswer(0)</c>).</summary>
    public const int DeclineSkillResurrection = 2;

    /// <summary>Mobius <c>RequestRestartPoint</c> case 2 (castle): owner clan, or defenders while a control tower stands.</summary>
    public const int ReviveToCastle = 3;

    /// <summary>Mobius <c>RequestRestartPoint</c> case 3 (siege HQ): attacker clan with a standing headquarters.</summary>
    public const int ReviveToSiegeHq = 4;

    /// <summary>Die restart bits for the dead player (Mobius <c>Die</c> canTeleport flags).</summary>
    public const int RestartCastle = 1;
    public const int RestartSiegeHq = 2;

    public const string NoCastle = "revive_no_castle";
    public const string NoHeadquarters = "revive_no_hq";

    public const string NotDead = "revive_not_dead";
    public const string AlreadyDead = "death_already";
    public const string Unsupported = "revive_unsupported";
}
