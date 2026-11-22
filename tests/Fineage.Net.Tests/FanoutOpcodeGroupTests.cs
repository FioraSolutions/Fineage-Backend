using Fineage.Net.Contract;
using Fineage.Net.Host;
using Xunit;

namespace Fineage.Net.Tests;

public sealed class FanoutOpcodeGroupTests
{
    [Theory]
    [InlineData(NetOpcode.AttackState, "combat_attack")]
    [InlineData(NetOpcode.MagicSkillUse, "combat_skill")]
    [InlineData(NetOpcode.MoveState, "presence_move")]
    [InlineData(NetOpcode.EntitySpawn, "presence_spawn")]
    [InlineData(NetOpcode.SystemMessage, "chat")]
    [InlineData(0xFFFF, "other")]
    public void Maps_opcode_to_group(ushort opcode, string expected) =>
        Assert.Equal(expected, FanoutOpcodeGroup.For(opcode));
}