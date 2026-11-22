using Fineage.Net.Contract;

namespace Fineage.Net.Host;

/// <summary>Low-cardinality fanout drop groups for Prometheus (opcode_group label).</summary>
public static class FanoutOpcodeGroup
{
    public static string For(ushort opcode) => opcode switch
    {
        NetOpcode.AttackState => "combat_attack",
        NetOpcode.MagicSkillUse or NetOpcode.MagicSkillLaunched or NetOpcode.MagicSkillCanceled => "combat_skill",
        NetOpcode.MoveState or NetOpcode.ChangeMoveType or NetOpcode.PositionCorrection => "presence_move",
        NetOpcode.EntitySpawn or NetOpcode.EntityDespawn or NetOpcode.EntityDie => "presence_spawn",
        NetOpcode.InventoryUpdate or NetOpcode.ItemList or NetOpcode.DropItem => "inventory",
        NetOpcode.StatusUpdate or NetOpcode.AbnormalStatusUpdate => "presence_status",
        NetOpcode.SystemMessage or NetOpcode.CreatureSay or NetOpcode.Say => "chat",
        _ => "other"
    };
}