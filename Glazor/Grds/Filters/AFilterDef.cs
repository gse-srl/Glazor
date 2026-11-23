using Glazor.Misc;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Glazor.Grds.Filters;

/// <summary>
/// How a filter definition is serialized.
/// It is also used to implement IEquatable contract for filter definitions.<br/>
/// </summary>
public abstract record AFilterDefSer
{
}

/// <summary>
/// Represents an abstract filter definition.<br/>
/// It is an open hierarchy in the sense that new filter definitions can be added by the glazor user, so it must support it.<br/>
/// This is the reason why, for example, an instance is in charge of acting as a sort of prototype upon deserialization and so any derived class must have the 
/// default c-tor.<br/>
/// </summary>
[JsonConverter(typeof(DefaultFilterDefConverter))]
public abstract class AFilterDef : IEquatable<AFilterDef>
{
    private static readonly ILogger<AFilterDef> logger = LoggerSupport.Logger<AFilterDef>();

    /// <summary>
    /// Gets the original .NET type of the filter definition.
    /// </summary>
    /// <returns>The type of the filter definition.</returns>
    public abstract Type GetValueType();

    /// <summary>
    /// Asked when a model filters its rows anew: an in-memory model at each sync, an EF model at a fresh query, not between the
    /// pages of one.
    /// </summary>
    /// <returns>False if for some reasons the filter should be discarded</returns>
    public virtual bool IsStillApplicable() => true;

    /// <summary>
    /// Indicates whether the definition is not influential for the filter definition.
    /// </summary>
    /// <returns>True if the definition is void; otherwise, false.</returns>
    public abstract bool IsVoid();

    /// <summary>
    /// Determines whether the specified value is included by this filter definition.
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns>True if the value is included; otherwise, false.</returns>
    public abstract bool Includes(object? value);

    /// <summary>
    /// Creates a clone of the current filter definition. The grid clones a definition before it edits or applies it,
    /// so a subclass, of a built-in definition too, overrides it to return its own type, or it comes back as its base.
    /// </summary>
    /// <returns>A clone of the current filter definition.</returns>
    public abstract AFilterDef Clone();

    /// <summary>
    /// Basically the "special" blazor component to be used to ask the user for the filter values.<br/>
    /// </summary>
    public abstract FilterDefRendererType Renderer();

    /// <summary>
    /// Used for serialization and IEquatable contract.
    /// </summary>
    protected abstract AFilterDefSer CreateSerRecord();

    /// <summary>
    /// Writes the definition as the record <see cref="CreateSerRecord"/> makes, under its own type, for <see cref="FilterDefFactory"/>.
    /// </summary>
    public void Write(Utf8JsonWriter writer, JsonSerializerOptions options)
    {
        AFilterDefSer rec = CreateSerRecord();
        JsonSerializer.Serialize(writer, rec, rec.GetType(), options);  // serialize the real type
    }

    /// <summary>
    /// Upon deserialization a "prototype" instance is created via the default c-tor, it has to deserialize the information and return 
    /// the real instance.
    /// </summary>
    /// <returns>The definition read: the prototype filled in, or a new instance; <see cref="FilterDefFactory"/> keeps what is returned and
    ///     discards the prototype.</returns>
    public abstract AFilterDef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options);

    /// <summary>
    /// True if this filter can CreatePredicate() to be used by EF-Core.
    /// When false filters are always executed in memory.
    /// </summary>
    public abstract bool CanCreatePredicate();

    /// <summary>
    /// The filter as a predicate on the value <paramref name="prop"/> reads from a row, for an EF query to apply in the database.
    /// A void filter admits every row. Throws when <see cref="CanCreatePredicate"/> is false.
    /// </summary>
    public Expression<Func<TGridItem, bool>> CreatePredicate<TGridItem>(Expression<Func<TGridItem, object?>> prop)
    {
        if (!CanCreatePredicate())
            throw new InvalidOperationException($"Query push down not enabled for: {this}");
        if (IsVoid())
            return x => true;
        // A string goes unboxed: EF turns a cast from object to string into CAST(... AS nvarchar(max)), which no index serves
        if (prop.Body is UnaryExpression { NodeType: ExpressionType.Convert } box && box.Type == typeof(object) && box.Operand.Type == typeof(string))
            prop = Expression.Lambda<Func<TGridItem, object?>>(box.Operand, prop.Parameters);
        ParameterExpression paramO = Expression.Parameter(typeof(TGridItem), "o");
        Expression<Func<TGridItem, bool>> res = Expression.Lambda<Func<TGridItem, bool>>(
            CreatePredicateImpl<TGridItem>(Expression.Invoke(prop, paramO)),
            paramO);
        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Expression<Func<{TGridItem}, bool>> = {expr}", typeof(TGridItem), res);
        return res;
    }

    /// <summary>
    /// Creates the predicate implementation for the specified grid item type to be used by EF-Core.
    /// </summary>
    protected abstract Expression CreatePredicateImpl<TGridItem>(InvocationExpression pexpr);

    /// <summary>
    /// A value for <see cref="CreatePredicateImpl"/> to compare with. Read from an object, as a lambda's captured variable is, so
    /// EF sends it as a parameter: a constant would be inlined into the SQL, and each value typed would compile a plan of its own.
    /// </summary>
    protected static Expression ValueExpr<T>(T value) => Expression.Field(Expression.Constant(new ValueBox<T>(value)), nameof(ValueBox<T>.Value));

    private sealed class ValueBox<T>(T value)
    {
        public readonly T Value = value;
    }

    /// <summary>
    /// A definition is identified by its runtime type together with the record it serializes to. The type has
    /// to be part of it: this is an open hierarchy, so a definition written by a library user may derive from
    /// a built-in one and add state of its own, and unless it also overrides <see cref="CreateSerRecord"/> it
    /// inherits the record of its base. Without the type check the two would compare equal and the extra
    /// state would be invisible.
    /// </summary>
    public bool Equals(AFilterDef? other)
    {
        if (other == null)
            return false;
        if (Object.ReferenceEquals(this, other))
            return true;
        if (other.GetType() != this.GetType())
            return false;
        var rec = CreateSerRecord();
        var orec = other.CreateSerRecord();
        return rec.Equals(orec);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is AFilterDef other && Equals(other);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var rec = CreateSerRecord();
        return HashCode.Combine(GetType(), rec);   // the type is part of the identity, see Equals()
    }
}

/// <summary>
/// Represents an abstract typed filter definition.
/// </summary>
/// <typeparam name="VT">The value type of the filter definition.</typeparam>
public abstract class AFilterDef<VT> : AFilterDef
{
    /// <summary>
    /// Gets the .NET type of the filter definition.
    /// </summary>
    /// <returns>The type of the filter definition.</returns>
    public override Type GetValueType() => typeof(VT);

    /// <summary>
    /// Indicates whether the definition is not influential for the filter definition.
    /// </summary>
    /// <returns>True if the definition is void; otherwise, false.</returns>
    public override abstract bool IsVoid();
}

/// <summary>
/// Represents an abstract typed filter definition.
/// </summary>
/// <typeparam name="SELF">The concrete definition type, so <c>Clone()</c> can return it rather than the base.</typeparam>
/// <typeparam name="VT">The value type of the filter definition.</typeparam>
public abstract class AFilterDef<SELF, VT> : AFilterDef<VT> where SELF : AFilterDef<SELF, VT>
{
    /// <inheritdoc/>
    public override abstract SELF Clone();
}
