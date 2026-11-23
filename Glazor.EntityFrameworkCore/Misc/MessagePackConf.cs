using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;

namespace Glazor.Misc;

/// <summary>The MessagePack settings <see cref="OnDiskChunkedList{T}"/> writes its spilled chunks with.</summary>
public static class MessagePackConf
{
    /// <summary>
    /// How deep a graph of reference types may nest before <see cref="Options"/> refuses it as a cycle. A row is a flat
    /// DTO, so a real one is a few levels deep.
    /// </summary>
    public const int MaxGraphDepth = 64;

    /// <summary>
    /// Contractless, private members included, with <see cref="XDateTimeResolver"/> keeping a <see cref="DateTime"/>'s Kind and a
    /// guard that refuses a graph deeper than <see cref="MaxGraphDepth"/>.
    /// </summary>
    public static readonly MessagePackSerializerOptions Options =
            ContractlessStandardResolverAllowPrivate.Options.WithResolver(CompositeResolver.Create(
                XDateTimeResolver.Instance,
                // Use default resolver last, order matters here
                new DepthGuardResolver(ContractlessStandardResolverAllowPrivate.Options.Resolver, MaxGraphDepth)
            ));
}

/// <summary>
/// Counts the reference types being written inside one another and refuses a graph deeper than its limit: the contractless
/// resolver has no cycle detection, so a row with a back-reference would otherwise overflow the stack and end the process.
/// Reading is left to the reader's own depth limit.
/// </summary>
internal sealed class DepthGuardResolver(IFormatterResolver inner, int maxDepth) : IFormatterResolver
{
    [ThreadStatic] private static int _depth;

    public IMessagePackFormatter<T>? GetFormatter<T>()
    {
        IMessagePackFormatter<T>? formatter = inner.GetFormatter<T>();
        // a value type or a string cannot be part of a cycle
        if (formatter == null || typeof(T).IsValueType || typeof(T) == typeof(string))
            return formatter;
        return new Guarded<T>(formatter, maxDepth);
    }

    private sealed class Guarded<T>(IMessagePackFormatter<T> inner, int maxDepth) : IMessagePackFormatter<T>
    {
        public void Serialize(ref MessagePackWriter writer, T value, MessagePackSerializerOptions options)
        {
            _depth++;
            try
            {
                if (_depth > maxDepth)
                    throw new MessagePackSerializationException(
                        $"Object graph deeper than {maxDepth} while serializing {typeof(T).Name}: a cycle? Rows must be flat, see data-models.md");
                inner.Serialize(ref writer, value, options);
            }
            finally
            {
                _depth--;
            }
        }

        public T Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) => inner.Deserialize(ref reader, options);
    }
}

/// <summary>
/// Keeps <see cref="DateTime.Kind"/>, which the standard msgpack timestamp drops. Written as one two-element
/// array: a formatter must produce exactly one msgpack value, or anything that delimits values structurally
/// (<see cref="MessagePackStreamReader"/>, <c>ConvertToJson</c>, skipping) loses its place.
/// </summary>
public sealed class XDateTimeFormatter : IMessagePackFormatter<DateTime>
{
    /// <summary>The only instance.</summary>
    public static readonly XDateTimeFormatter Instance = new();

    private XDateTimeFormatter() { }

    /// <inheritdoc/>
    public void Serialize(ref MessagePackWriter writer, DateTime value, MessagePackSerializerOptions options)
    {
        writer.WriteArrayHeader(2);
        writer.Write((Int16)value.Kind);
        writer.Write(value.Ticks);
    }

    /// <inheritdoc/>
    public DateTime Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        int count = reader.ReadArrayHeader();
        if (count != 2)
            throw new MessagePackSerializationException($"Expected a 2-element array for a DateTime, found {count}");
        DateTimeKind kind = (DateTimeKind)reader.ReadInt16();
        long ticks = reader.ReadInt64();
        return new DateTime(ticks, kind);
    }
}

/// <summary>An array of <see cref="DateTime"/> written element by element with <see cref="XDateTimeFormatter"/>, so each keeps its <see cref="DateTime.Kind"/>.</summary>
public sealed class XDateTimeArrayFormatter : IMessagePackFormatter<DateTime[]?>
{
    /// <summary>The only instance.</summary>
    public static readonly XDateTimeArrayFormatter Instance = new();

    private XDateTimeArrayFormatter() { }

    /// <inheritdoc/>
    public void Serialize(ref MessagePackWriter writer, DateTime[]? value, MessagePackSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNil();
        }
        else
        {
            writer.WriteArrayHeader(value.Length);
            for (var i = 0; i < value.Length; i++)
                XDateTimeFormatter.Instance.Serialize(ref writer, value[i], options);
        }
    }

    /// <inheritdoc/>
    public DateTime[]? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
            return null;
        var len = reader.ReadArrayHeader();
        var array = new DateTime[len];
        for (var i = 0; i < array.Length; i++)
            array[i] = XDateTimeFormatter.Instance.Deserialize(ref reader, options);
        return array;
    }
}

/// <summary>Resolves <see cref="DateTime"/>, its nullable and its array to the formatters that keep <see cref="DateTime.Kind"/>.</summary>
public sealed class XDateTimeResolver : IFormatterResolver
{
    /// <summary>The only instance.</summary>
    public static readonly XDateTimeResolver Instance = new();

    private XDateTimeResolver() { }

    /// <returns>Null for a type other than <see cref="DateTime"/>, its nullable and its array: the next resolver takes it.</returns>
    public IMessagePackFormatter<T>? GetFormatter<T>()
    {
        return FormatterCache<T>.Formatter;
    }

    private static class FormatterCache<T>
    {
        public static readonly IMessagePackFormatter<T>? Formatter;

        static FormatterCache()
        {
            Formatter = (IMessagePackFormatter<T>?)XDateTimeResolverFormatterHelper.GetFormatter(typeof(T));
        }
    }

    private static class XDateTimeResolverFormatterHelper
    {
        internal static object? GetFormatter(Type t)
        {
            if (t == typeof(DateTime))
                return XDateTimeFormatter.Instance;
            if (t == typeof(DateTime?))
                return new StaticNullableFormatter<DateTime>(XDateTimeFormatter.Instance);
            if (t == typeof(DateTime[]))
                return XDateTimeArrayFormatter.Instance;
            return null;
        }
    }
}
