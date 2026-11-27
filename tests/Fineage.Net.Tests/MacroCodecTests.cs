using Fineage.Net.Contract;
using Xunit;

namespace Fineage.Net.Tests;

/// <summary>Mobius RequestMakeMacro / RequestDeleteMacro / SendMacroList wire (#927).</summary>
public sealed class MacroCodecTests
{
    [Fact]
    public void Opcodes_match_assigned_net_ids()
    {
        Assert.Equal((ushort)0x03EF, NetOpcode.RequestMakeMacro);
        Assert.Equal((ushort)0x03F0, NetOpcode.RequestDeleteMacro);
        Assert.Equal((ushort)0x03F1, NetOpcode.MacroList);
    }

    [Fact]
    public void Make_macro_round_trips()
    {
        var original = new MakeMacroNetMsg
        {
            Id = 0,
            Name = "Buff",
            Descr = "party",
            Acronym = "BF",
            Icon = 3,
            Commands =
            [
                new MacroCmdNet { Entry = 0, Type = 3, D1 = 0, D2 = 0, Cmd = "/sit" },
                new MacroCmdNet { Entry = 1, Type = 1, D1 = 57, D2 = 1, Cmd = "" }
            ]
        };
        var decoded = NetCodec.DecodeMakeMacro(NetCodec.EncodeMakeMacro(original));
        Assert.Equal(original.Id, decoded.Id);
        Assert.Equal(original.Name, decoded.Name);
        Assert.Equal(original.Descr, decoded.Descr);
        Assert.Equal(original.Acronym, decoded.Acronym);
        Assert.Equal(original.Icon, decoded.Icon);
        Assert.Equal(2, decoded.Commands.Length);
        Assert.Equal("/sit", decoded.Commands[0].Cmd);
        Assert.Equal(57, decoded.Commands[1].D1);
    }

    [Fact]
    public void Macro_list_empty_and_entry_round_trip()
    {
        var empty = NetCodec.DecodeMacroList(NetCodec.EncodeMacroList(new MacroListNetMsg
        {
            Revision = 1,
            Count = 0,
            HasMacro = false
        }));
        Assert.Equal(1, empty.Revision);
        Assert.Equal(0, empty.Count);
        Assert.False(empty.HasMacro);

        var entry = NetCodec.DecodeMacroList(NetCodec.EncodeMacroList(new MacroListNetMsg
        {
            Revision = 2,
            Count = 1,
            HasMacro = true,
            MacroId = 1000,
            Name = "A",
            Descr = "B",
            Acronym = "C",
            Icon = 4,
            Commands = [new MacroCmdNet { Entry = 1, Type = 3, D1 = 0, D2 = 0, Cmd = "/stand" }]
        }));
        Assert.Equal(1000, entry.MacroId);
        Assert.Equal("A", entry.Name);
        Assert.Single(entry.Commands!);
        Assert.Equal("/stand", entry.Commands[0].Cmd);
    }

    [Fact]
    public void Delete_macro_round_trips()
    {
        var decoded = NetCodec.DecodeDeleteMacro(NetCodec.EncodeDeleteMacro(new DeleteMacroNetMsg { Id = 1001 }));
        Assert.Equal(1001, decoded.Id);
    }
}
