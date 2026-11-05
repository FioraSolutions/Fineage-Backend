using Fineage.Net.Contract;
using Fineage.Protocol;

namespace Fineage.Zone.Combat;

/// <summary>System chat lines for combat (attacker-only fan-out).</summary>
public static class CombatLines
{
    public static SystemMessageMsg HitTarget(string? targetName, int damage)
    {
        string name = string.IsNullOrWhiteSpace(targetName) ? "target" : targetName.Trim();
        string amount = damage.ToString();
        return new SystemMessageMsg
        {
            Id = CombatProtocol.HitTargetForDamage,
            Params = [name, amount],
            Text = $"You hit {name} for {amount} damage."
        };
    }

    /// <summary>Net form of <see cref="SystemMessageMsg"/> for the attacker's own damage line.</summary>
    public static SystemMessageNetMsg ToNet(SystemMessageMsg message) => new()
    {
        Id = message.Id,
        Text = message.Text,
        Params = message.Params ?? []
    };
}
