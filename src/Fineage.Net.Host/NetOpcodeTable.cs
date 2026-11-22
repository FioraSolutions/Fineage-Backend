using System.Collections.Frozen;
using System.Reflection;
using Fineage.Net.Contract;

namespace Fineage.Net.Host;

/// <summary>
/// Opcode constants from <see cref="NetOpcode"/>, built once and read-only afterwards.
/// A value that is not in this table is not a protocol opcode. A value that is in the table but has no handler
/// is a known opcode the listener did not register.
/// </summary>
public static class NetOpcodeTable
{
    static readonly FrozenDictionary<ushort, string> Names = Build();

    public static int Count => Names.Count;

    public static bool TryGetName(ushort opcode, out string name) => Names.TryGetValue(opcode, out name!);

    /// <summary>Name and hex when the opcode is published, otherwise the hex alone.</summary>
    public static string Format(ushort opcode) =>
        Names.TryGetValue(opcode, out var name) ? $"{name} (0x{opcode:X4})" : $"0x{opcode:X4}";

    /// <summary>Reject text for a frame the listener did not dispatch.</summary>
    public static string DescribeUnknown(ushort opcode) =>
        Names.TryGetValue(opcode, out var name)
            ? $"opcode {name} (0x{opcode:X4}) has no handler"
            : $"opcode 0x{opcode:X4} is not a known NetOpcode";

    static FrozenDictionary<ushort, string> Build()
    {
        var map = new Dictionary<ushort, string>();
        foreach (var field in typeof(NetOpcode).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (!field.IsLiteral || field.FieldType != typeof(ushort))
                continue;
            ushort value = (ushort)field.GetRawConstantValue()!;
            if (!map.TryAdd(value, field.Name))
                throw new InvalidOperationException(
                    $"NetOpcode {field.Name} duplicates 0x{value:X4}, already named {map[value]}.");
        }

        if (map.Count == 0)
            throw new InvalidOperationException("NetOpcode published no opcode constants.");
        return map.ToFrozenDictionary();
    }
}
