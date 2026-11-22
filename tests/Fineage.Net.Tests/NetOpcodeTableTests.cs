using Fineage.Net.Contract;
using Fineage.Net.Host;
using Xunit;

namespace Fineage.Net.Tests;

public sealed class NetOpcodeTableTests
{
    [Fact]
    public void Every_published_opcode_is_named_and_unknown_values_are_not()
    {
        Assert.True(NetOpcodeTable.TryGetName(NetOpcode.Hello, out var hello));
        Assert.Equal("Hello", hello);
        Assert.Equal("Hello (0x0101)", NetOpcodeTable.Format(NetOpcode.Hello));
        Assert.Equal("opcode Hello (0x0101) has no handler", NetOpcodeTable.DescribeUnknown(NetOpcode.Hello));

        Assert.False(NetOpcodeTable.TryGetName(0x0FFE, out _));
        Assert.Equal("0x0FFE", NetOpcodeTable.Format(0x0FFE));
        Assert.Equal("opcode 0x0FFE is not a known NetOpcode", NetOpcodeTable.DescribeUnknown(0x0FFE));

        int constants = 0;
        foreach (var field in typeof(NetOpcode).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (!field.IsLiteral || field.FieldType != typeof(ushort))
                continue;
            constants++;
            ushort value = (ushort)field.GetRawConstantValue()!;
            Assert.True(NetOpcodeTable.TryGetName(value, out var name), $"missing opcode {field.Name}");
            Assert.Equal(field.Name, name);
        }

        Assert.Equal(constants, NetOpcodeTable.Count);
        Assert.True(constants > 0);
    }

    [Fact]
    public void Published_opcodes_have_unique_values()
    {
        var seen = new Dictionary<ushort, string>();
        foreach (var field in typeof(NetOpcode).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (!field.IsLiteral || field.FieldType != typeof(ushort))
                continue;
            ushort value = (ushort)field.GetRawConstantValue()!;
            if (seen.TryGetValue(value, out var first))
                Assert.Fail($"NetOpcode.{field.Name} and NetOpcode.{first} both use 0x{value:X4}");
            seen[value] = field.Name;
        }
    }

    [Fact]
    public void Mpc_and_enchant_ex_opcodes_match_mobius_wire_folding()
    {
        Assert.Equal(0xD00D, NetOpcode.CommandChannelAskRequest);
        Assert.Equal(0xD00E, NetOpcode.CommandChannelAcceptRequest);
        Assert.Equal(0xD00F, NetOpcode.CommandChannelOustRequest);
        Assert.Equal(0xFE25, NetOpcode.CommandChannelOpen);
        Assert.Equal(0xFE26, NetOpcode.CommandChannelClose);
        Assert.Equal(0xFE27, NetOpcode.CommandChannelAskJoin);
        Assert.Equal(0xFE30, NetOpcode.CommandChannelInfo);
    }
}
