using System.Reflection;
using Fineage.Agent.Contracts;
using Xunit;

namespace Fineage.Agent.Contracts.Tests;

/// <summary>
/// Money and usage amounts in the Agent contracts are integer credits or integer
/// quantities. Floating-point properties are banned so they cannot slip in later.
/// </summary>
public sealed class MonetaryTypeGuardTests
{
    [Fact]
    public void Contract_types_have_no_floating_point_fields_or_properties()
    {
        var violations = new List<string>();
        foreach (Type type in typeof(AgentContractSchema).Assembly.GetExportedTypes())
        {
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (IsBanned(property.PropertyType))
                    violations.Add($"{type.Name}.{property.Name} is {property.PropertyType}");
            }

            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (IsBanned(field.FieldType))
                    violations.Add($"{type.Name}.{field.Name} is {field.FieldType}");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Usage_and_quote_amounts_are_integer_credit_types()
    {
        Assert.Equal(typeof(CreditAmount), typeof(UsageEvent).GetProperty(nameof(UsageEvent.Credits))!.PropertyType);
        Assert.Equal(typeof(long), typeof(UsageEvent).GetProperty(nameof(UsageEvent.Quantity))!.PropertyType);
        Assert.Equal(typeof(CreditAmount), typeof(BillingQuote).GetProperty(nameof(BillingQuote.Credits))!.PropertyType);
        Assert.Equal(typeof(long), typeof(BillingQuote).GetProperty(nameof(BillingQuote.Quantity))!.PropertyType);
    }

    static bool IsBanned(Type type)
    {
        Type actual = Nullable.GetUnderlyingType(type) ?? type;
        return actual == typeof(float) || actual == typeof(double) || actual == typeof(decimal);
    }
}
