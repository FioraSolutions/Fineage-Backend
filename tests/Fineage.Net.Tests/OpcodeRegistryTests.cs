using System.Reflection;
using Fineage.Net.Contract;
using Xunit;

namespace Fineage.Net.Tests;

public sealed class OpcodeRegistryTests
{
    [Fact]
    public void Every_opcode_registry_rejects_a_duplicate_value()
    {
        AssertUnique(typeof(NetOpcode));
        foreach (var nested in typeof(NetOpcode).GetNestedTypes())
            AssertUnique(nested);
    }

    static void AssertUnique(Type registry)
    {
        var fields = registry
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.IsLiteral && f.FieldType == typeof(ushort))
            .Select(f => (f.Name, Value: (ushort)f.GetRawConstantValue()!))
            .ToList();
        var duplicate = fields.GroupBy(f => f.Value).FirstOrDefault(g => g.Count() > 1);
        Assert.True(
            duplicate == null,
            duplicate == null
                ? ""
                : $"{registry.Name} opcode 0x{duplicate.Key:X4} is assigned to {string.Join(", ", duplicate.Select(f => f.Name))}.");
    }
}
