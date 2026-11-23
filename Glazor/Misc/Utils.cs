namespace Glazor.Misc;

/// <summary>Small helpers the library uses and a host may too: <c>IsNumber</c>, <c>IdentityIndexOf</c>, <c>ByteArraysEqual</c>.</summary>
public static class Utils
{
    /// <summary>
    /// see: https://stackoverflow.com/questions/43289/comparing-two-byte-arrays-in-net<br/>
    /// byte[] is implicitly convertible to ReadOnlySpan&lt;byte>.
    /// </summary>
    public static bool ByteArraysEqual(ReadOnlySpan<byte> a1, ReadOnlySpan<byte> a2)
    {
        return a1.SequenceEqual(a2);
    }

    /// <summary>The index of the first element that is <paramref name="o"/> itself, by reference; -1 when none is.</summary>
    public static int IdentityIndexOf<T>(IEnumerable<T> coll, T o)
    {
        int i = 0;
        foreach (T t in coll)
        {
            if (ReferenceEquals(t, o))
                return i;
            i++;
        }
        return -1;
    }

    /// <summary>Whether <paramref name="value"/> is a boxed value of a built-in numeric type.</summary>
    public static bool IsNumber(object? value)
    {
        return value is decimal
            || value is long
            || value is int
            || value is double
            || value is float
            || value is sbyte
            || value is byte
            || value is short
            || value is ushort
            || value is uint
            || value is ulong
            || value is Int128
            || value is UInt128
            || value is nint
            || value is nuint
            || value is Half
            ;
    }

    /// <summary>Whether <paramref name="type"/>, or the type its nullable wraps, is a built-in numeric type.</summary>
    public static bool IsNumber(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;   // as IsNumber(object) for a boxed int?
        return typeof(decimal).IsAssignableFrom(type)
            || typeof(long).IsAssignableFrom(type)
            || typeof(int).IsAssignableFrom(type)
            || typeof(double).IsAssignableFrom(type)
            || typeof(float).IsAssignableFrom(type)
            || typeof(sbyte).IsAssignableFrom(type)
            || typeof(byte).IsAssignableFrom(type)
            || typeof(short).IsAssignableFrom(type)
            || typeof(ushort).IsAssignableFrom(type)
            || typeof(uint).IsAssignableFrom(type)
            || typeof(ulong).IsAssignableFrom(type)
            || typeof(Int128).IsAssignableFrom(type)
            || typeof(UInt128).IsAssignableFrom(type)
            || typeof(nint).IsAssignableFrom(type)
            || typeof(nuint).IsAssignableFrom(type)
            || typeof(Half).IsAssignableFrom(type)
            ;
    }
}
