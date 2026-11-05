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

    public const string NotDead = "revive_not_dead";
    public const string AlreadyDead = "death_already";
    public const string Unsupported = "revive_unsupported";
}
