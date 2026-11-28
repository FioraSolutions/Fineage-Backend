using System.Collections;
using System.Reflection;
using Fineage.Net.Contract;
using Xunit;

namespace Fineage.Net.Tests;

/// <summary>
/// Every NetCodec Encode/Decode pair must round-trip Fineage fields. This is the
/// semantic check in docs/adr/0001-custom-net-wire.md: Mobius packets name the
/// field list, not a retail byte layout.
/// </summary>
public sealed class CodecRoundTripCatalogTests
{
    [Fact]
    public void Every_encode_decode_pair_roundtrips_a_populated_message()
    {
        var failures = new List<string>();
        int pairs = 0;
        foreach (var pair in Pairs())
        {
            pairs++;
            try
            {
                object original = Populate(pair.MessageType, seed: 7);
                var encoded = (byte[])pair.Encode.Invoke(null, new[] { original })!;
                object? decoded = InvokeDecode(pair.Decode, encoded);
                // Some message properties are derived or wider than the field on the
                // wire (a u8 stored on an int). The wire contract is: decode then
                // encode again yields the same payload.
                var again = (byte[])pair.Encode.Invoke(null, new[] { decoded })!;
                if (!encoded.SequenceEqual(again))
                    throw new InvalidOperationException($"re-encode payload mismatch ({encoded.Length} vs {again.Length} bytes)");
            }
            catch (Exception ex)
            {
                failures.Add($"{pair.Name}: {Unwrap(ex).GetType().Name}: {Unwrap(ex).Message}");
            }
        }

        Assert.True(pairs > 200, $"expected a large codec catalog, found {pairs} pairs");
        Assert.True(failures.Count == 0, $"{failures.Count} codec pair(s) failed:\n" + string.Join("\n", failures));
    }

    static IEnumerable<(string Name, MethodInfo Encode, MethodInfo Decode, Type MessageType)> Pairs()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
        var encodes = typeof(NetCodec).GetMethods(flags)
            .Where(m => m.Name.StartsWith("Encode", StringComparison.Ordinal)
                        && m.ReturnType == typeof(byte[])
                        && m.GetParameters().Length == 1)
            .ToList();

        foreach (var encode in encodes)
        {
            string name = encode.Name["Encode".Length..];
            if (name.Length == 0)
                continue;
            Type messageType = encode.GetParameters()[0].ParameterType;
            if (messageType == typeof(byte[]) || messageType.IsPrimitive)
                continue;
            var decode = typeof(NetCodec).GetMethod("Decode" + name, flags);
            if (decode is null)
                continue;
            var decodeParams = decode.GetParameters();
            if (decodeParams.Length != 1)
                continue;
            yield return (name, encode, decode, messageType);
        }
    }

    static object? InvokeDecode(MethodInfo decode, byte[] payload)
    {
        ParameterInfo p = decode.GetParameters()[0];
        if (p.ParameterType == typeof(byte[]))
            return decode.Invoke(null, new object[] { payload });
        if (p.ParameterType == typeof(ReadOnlySpan<byte>))
        {
            // ReadOnlySpan cannot be boxed through MethodInfo.Invoke. Call via a typed helper.
            var helper = typeof(CodecRoundTripCatalogTests)
                .GetMethod(nameof(DecodeSpan), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(decode.ReturnType);
            return helper.Invoke(null, new object[] { decode, payload });
        }

        throw new InvalidOperationException($"unsupported decode signature for {decode.Name}: {p.ParameterType}");
    }

    static T DecodeSpan<T>(MethodInfo decode, byte[] payload)
    {
        var fn = decode.CreateDelegate<DecodeSpanFunc<T>>();
        return fn(payload);
    }

    delegate T DecodeSpanFunc<T>(ReadOnlySpan<byte> payload);

    static object Populate(Type type, int seed)
    {
        if (type == typeof(string))
            return "s" + seed;
        if (type == typeof(bool))
            return seed % 2 == 0;
        if (type == typeof(byte))
            return (byte)(seed & 0x7F);
        if (type == typeof(sbyte))
            return (sbyte)(seed % 50);
        if (type == typeof(short))
            return (short)(1000 + seed);
        if (type == typeof(ushort))
            return (ushort)(2000 + seed);
        if (type == typeof(int))
            return 10_000 + seed;
        if (type == typeof(uint))
            return (uint)(20_000 + seed);
        if (type == typeof(long))
            return 1_000_000L + seed;
        if (type == typeof(ulong))
            return (ulong)(2_000_000 + seed);
        if (type == typeof(float))
            return 1.5f + seed;
        if (type == typeof(double))
            return 2.5 + seed;
        if (type == typeof(char))
            return (char)('A' + (seed % 26));
        if (type == typeof(byte[]))
            return new byte[] { (byte)seed, 2, 3 };
        if (type.IsEnum)
        {
            Array values = Enum.GetValues(type);
            return values.Length == 0 ? Activator.CreateInstance(type)! : values.GetValue(seed % values.Length)!;
        }

        if (type.IsArray)
        {
            Type elem = type.GetElementType()!;
            Array arr = Array.CreateInstance(elem, 1);
            arr.SetValue(Populate(elem, seed + 1), 0);
            return arr;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            Type elem = type.GetGenericArguments()[0];
            var list = (IList)Activator.CreateInstance(type)!;
            list.Add(Populate(elem, seed + 1));
            return list;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            return Populate(Nullable.GetUnderlyingType(type)!, seed);

        object instance = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("cannot construct " + type.FullName);
        foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanWrite || prop.GetIndexParameters().Length > 0)
                continue;
            try
            {
                object value;
                if (prop.Name == "DecoBytes" && prop.PropertyType == typeof(byte[]))
                    value = Enumerable.Range(0, NetCodec.AgitDecoByteCount).Select(i => (byte)(seed + i)).ToArray();
                else
                    value = Populate(prop.PropertyType, seed + prop.Name.Length);
                prop.SetValue(instance, value);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"populate {type.Name}.{prop.Name}: {Unwrap(ex).Message}", ex);
            }
        }

        // NetCodec.ReadCharacter forces PendingDelete when DeleteSecondsLeft > 0.
        PropertyInfo? pending = type.GetProperty("PendingDelete");
        PropertyInfo? seconds = type.GetProperty("DeleteSecondsLeft");
        if (pending is { CanWrite: true } && seconds is not null && seconds.GetValue(instance) is int left && left > 0)
            pending.SetValue(instance, true);

        return instance;
    }

    static Exception Unwrap(Exception ex)
    {
        while (ex is TargetInvocationException { InnerException: { } inner })
            ex = inner;
        return ex;
    }
}
