using Glazor.Grds.Mdls;
using Glazor.Misc;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Glazor.Grds.Filters;

/// <summary>
/// Factory class for creating filter definitions based on data type.
/// </summary>
public class FilterDefFactory : JsonConverter<AFilterDef>
{
    /// <summary>
    /// The factory a grid uses when none is given as its <c>FilterDefFactory</c> parameter, and the one the JSON converter of
    /// <see cref="AFilterDef"/> forwards to: one subclass set here, at startup, covers the filter dialogs and the persisted
    /// mementos alike, so an override of <see cref="GetDefType"/> that allow-lists the type names a store may hold applies to
    /// every restore.
    /// </summary>
    public static FilterDefFactory Default { get; set => field = value ?? throw new ArgumentNullException(nameof(value)); } = new();

    /// <summary>
    /// Creates a filter definition for the specified data type.
    /// </summary>
    /// <param name="model">The data grid model.</param>
    /// <param name="columnName">The column's name.</param>
    /// <param name="column">The column</param>
    /// <returns>An instance of <see cref="AFilterDef{T}"/>.</returns>
    public virtual AFilterDef CreateDefinition<TGridItem>(IDataGridModel<TGridItem> model, string columnName, IColumn<TGridItem> column)
    {
        var orgDataType = column.GetValueType();
        var dataType = Nullable.GetUnderlyingType(orgDataType) ?? orgDataType;
        if (dataType.IsEnum)
        {
            return new EnumDef(orgDataType, true);
        }
        // The ones supported by <InputDate>, see: https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.components.forms.inputdate-1?view=aspnetcore-10.0
        else if (typeof(DateTime).IsAssignableFrom(dataType))
        {
            return new DateDef<DateTime>();
        }
        else if (typeof(DateTimeOffset).IsAssignableFrom(dataType))
        {
            return new DateDef<DateTimeOffset>();
        }
        else if (typeof(DateOnly).IsAssignableFrom(dataType))
        {
            return new DateDef<DateOnly>();
        }
        else if (typeof(TimeOnly).IsAssignableFrom(dataType))
        {
            return new DateDef<TimeOnly>();
        }
        else if (dataType.IsNumber())
        {
            // Same as Microsoft.AspNetCore.Components.Forms.InputNumber
            if (typeof(int).IsAssignableFrom(dataType))
                return new NumberDef<int>();
            else if (typeof(long).IsAssignableFrom(dataType))
                return new NumberDef<long>();
            else if (typeof(short).IsAssignableFrom(dataType))
                return new NumberDef<short>();
            else if (typeof(float).IsAssignableFrom(dataType))
                return new NumberDef<float>();
            else if (typeof(double).IsAssignableFrom(dataType))
                return new NumberDef<double>();
            else if (typeof(decimal).IsAssignableFrom(dataType))
                return new NumberDef<decimal>();
            else
                throw new InvalidOperationException($"Unsupported numeric type {dataType} for column: {columnName}");
        }
        else if (typeof(bool).IsAssignableFrom(dataType))
        {
            return new BoolDef();
        }
        else
        {
            return new DefaultDef();
        }
    }

    /// <inheritdoc/>
    public sealed override bool CanConvert(Type typeToConvert)
    {
        return typeof(AFilterDef).IsAssignableFrom(typeToConvert);
    }

    // Version, Culture and PublicKeyToken, in every part of an assembly-qualified name including the
    // generic arguments.
    private static readonly Regex assemblyIdentity =
        new(@",\s*(Version|Culture|PublicKeyToken)=[^,\]]*", RegexOptions.Compiled);

    // The regex is not cheap and the answer never changes for a given Type. It is asked for on every
    // serialization and, since a definition derives its identity from its serialized form, on every
    // equality check and every GetHashCode too.
    private static readonly ConcurrentDictionary<Type, string> typeNames = new();

    /// <summary>
    /// An assembly-qualified name stripped of version, culture and public key token, so that a persisted
    /// filter is not invalidated by a rebuild of the assembly declaring the type.<br/>
    /// <see cref="Type.GetType(string, bool)"/> resolves what this produces: the assembly is matched by
    /// simple name.
    /// </summary>
    /// <returns>e.g. "Glazor.Grds.Filters.BoolDef, Glazor"</returns>
    public static string TypeName(Type type) =>
        typeNames.GetOrAdd(type, static t => assemblyIdentity.Replace(t.AssemblyQualifiedName.RequireNotNull(), ""));

    /// <summary>
    /// The definition type a persisted filter names, as <see cref="TypeName"/> wrote it. The name comes from the store: an
    /// allow-list matches it as a string, since resolving it first loads the assemblies it names.
    /// </summary>
    protected virtual Type GetDefType(string defType) => Type.GetType(defType, true)!;

    /// <summary>
    /// The enum type a persisted <see cref="EnumDef"/> names, resolved as <see cref="GetDefType"/> resolves a definition's type:
    /// the name comes from the store, so an allow-list belongs here too.
    /// </summary>
    protected virtual Type GetEnumType(string enumType) => Type.GetType(enumType, true)!;

    internal Type ResolveEnumType(string enumType) => GetEnumType(CheckTypeName(enumType));

    // A converter on a property is not in the options, so a definition reads which factory is reading it here
    [ThreadStatic] private static FilterDefFactory? _reading;

    internal static FilterDefFactory Reading => _reading ?? Default;

    // Type.GetType recurses once per nesting level of a name: a deep one overflows the stack and ends the process
    private static string CheckTypeName(string name) =>
        System.Reflection.Metadata.TypeName.TryParse(name, out _) ? name : throw new JsonException("Not a type name, or one nested too deep");

    /// <inheritdoc/>
    public sealed override AFilterDef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            return ReadDefinition(ref reader, typeToConvert, options);
        }
        // A type renamed or removed since the store was written fails in many ways; the caller catches one. A path is a nested
        // serializer's, relative to the definition: wrapped, the outer serializer sets its own
        catch (Exception e) when (e is not JsonException { Path: null })
        {
            throw new JsonException($"Cannot read a filter definition: {e.Message}", e);
        }
    }

    private AFilterDef ReadDefinition(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException();
        Type? defType = null;
        AFilterDef? def = null;
        Utf8JsonReader defReader = default;
        bool defBeforeType = false;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException();
            string? propertyName = reader.GetString();
            switch (propertyName)
            {
                case null: throw new JsonException();
                case "defType":
                    string dt = JsonSerializer.Deserialize<string>(ref reader).RequireNotNull();
                    defType = GetDefType(CheckTypeName(dt)).RequireNotNull();
                    if (!typeof(AFilterDef).IsAssignableFrom(defType))
                        throw new InvalidCastException($"Not an AFilterDef: {defType}");
                    break;
                case "def":
                    if (defType == null)
                    {
                        defReader = reader;  // a store that orders the keys, jsonb for one, puts "def" first: read it once the type is known
                        defBeforeType = true;
                        reader.TrySkip();  // Skip refuses a stream's partial buffer, though a converter's value is all read ahead
                        break;
                    }
                    def = ReadDef(ref reader);
                    break;
                default:
                    reader.Skip();  // skip the value
                    break;
            }
        }
        if (def == null && defBeforeType && defType != null)   // without a defType the deferred def has no type to read into
            def = ReadDef(ref defReader);
        if (def != null)
            return def;
        throw new JsonException($"Unexpected: {typeToConvert} defType: {defType}");

        AFilterDef ReadDef(ref Utf8JsonReader json)
        {
            // Here we use the mandatory no arg c-tor, which can be even not public, just to create a "deserialization prototype"
            // which will deserialize the real instance.
            AFilterDef proto = (AFilterDef)Activator.CreateInstance(defType.RequireNotNull(), true).RequireNotNull();
            FilterDefFactory? outer = _reading;
            _reading = this;
            try
            {
                return proto.Read(ref json, typeToConvert, options);
            }
            finally
            {
                _reading = outer;
            }
        }
    }

    /// <inheritdoc/>
    public sealed override void Write(Utf8JsonWriter writer, AFilterDef value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("defType");
        writer.WriteStringValue(TypeName(value.GetType()));
        writer.WritePropertyName("def");
        value.Write(writer, options);
        writer.WriteEndObject();
    }
}

/// <summary>
/// The converter <see cref="AFilterDef"/> carries. It forwards to <see cref="FilterDefFactory.Default"/>, read at each call, so
/// a host replaces the factory and not the attribute; one given to <c>JsonSerializerOptions.Converters</c> takes precedence
/// over it, as any converter in the options does.
/// </summary>
public sealed class DefaultFilterDefConverter : JsonConverter<AFilterDef>
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert) => FilterDefFactory.Default.CanConvert(typeToConvert);

    /// <inheritdoc/>
    public override AFilterDef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => FilterDefFactory.Default.Read(ref reader, typeToConvert, options);

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, AFilterDef value, JsonSerializerOptions options)
        => FilterDefFactory.Default.Write(writer, value, options);
}
