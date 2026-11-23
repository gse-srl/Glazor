using Glazor.Grds.Props;
using Glazor.Misc;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;

namespace Glazor.Grds.Filters;

/// <summary>
/// Compares a string to the object ToString() method result. Pushed down, a value that is not a string is converted by the
/// database, whose text can differ from .NET's.
/// </summary>
public class DefaultDef : AValueDef<DefaultDef, string>
{
    /// <summary>Whether a null value passes the filter; true by default.</summary>
    public bool IncludeNull { get; set; } = true;
    /// <summary>The text to match; null or empty lets every value that is not null pass.</summary>
    public string? Value { get; set; }
    /// <summary>True matches <see cref="Value"/> anywhere in the value's text, false only at its start; true by default.</summary>
    public bool Contains { get; set; } = true;

    /// <inheritdoc/>
    public override DefaultDef Clone()
    {
        return new DefaultDef()
        {
            IncludeNull = this.IncludeNull,
            Value = this.Value,
            Contains = this.Contains
        };
    }

    /// <inheritdoc/>
    public override bool MustIncludeNull() => IncludeNull;

    /// <inheritdoc/>
    public override string? GetValue() => String.IsNullOrEmpty(Value) ? null : Value;

    /// <inheritdoc/>
    public override bool Includes(object? value)
    {
        string? svalue = value?.ToString();
        if (svalue == null)
            return MustIncludeNull();
        string? vv = GetValue();
        if (string.IsNullOrEmpty(vv))
            return true;
        if (vv == svalue)
            return true;
        return Contains ? svalue.Contains(vv, StringComparison.CurrentCultureIgnoreCase)
            : svalue.StartsWith(vv, StringComparison.CurrentCultureIgnoreCase);
    }

    private static readonly MethodInfo containsMethod =
        typeof(string).GetMethod(nameof(string.Contains), [typeof(string)]).RequireNotNull();
    private static readonly MethodInfo startsWithMethod =
        typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)]).RequireNotNull();
    // The types whose ToString() EF turns into the database's own cast
    private static readonly HashSet<Type> toStringTypes = new()
    {
        typeof(bool), typeof(char), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
        typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal), typeof(Guid),
        typeof(DateTime), typeof(DateTimeOffset), typeof(DateOnly), typeof(TimeOnly), typeof(TimeSpan),
    };

    /// <inheritdoc/>
    protected override Expression CreatePredicateImpl<TGridItem>(InvocationExpression pexpr)
    {
        string? vv = GetValue();
        Type type = ColumnType(pexpr);
        // Those types go through ToString(), as Includes does; a string, object or a custom type is cast whole to string
        Expression text = toStringTypes.Contains(Nullable.GetUnderlyingType(type) ?? type)
            ? ToText(Expression.Convert(pexpr, type)) : Expression.Convert(pexpr, typeof(string));
        Expression vexpr = string.IsNullOrEmpty(vv) ? Expression.Constant(true)  // null means accept all except nulls
            : Expression.Call(text, Contains ? containsMethod : startsWithMethod, ValueExpr(vv));
        if (type.IsValueType && Nullable.GetUnderlyingType(type) == null)
            return vexpr;
        // On the boxed value: a test on the typed one would pick a user-defined ==, which EF cannot translate
        Expression isNull = Expression.ReferenceEqual(pexpr, Expression.Constant(null));
        Expression isNotNull = Expression.ReferenceNotEqual(pexpr, Expression.Constant(null));
        return MustIncludeNull() ? Expression.OrElse(isNull, vexpr) : Expression.AndAlso(isNotNull, vexpr);
    }

    // The column's own type, which a value type's property expression boxes to object
    private static Type ColumnType(InvocationExpression pexpr)
    {
        Expression body = pexpr.Expression is LambdaExpression lambda ? lambda.Body : pexpr;
        return body is UnaryExpression { NodeType: ExpressionType.Convert } boxed && body.Type == typeof(object) ? boxed.Operand.Type : body.Type;
    }

    private static Expression ToText(Expression value)
    {
        if (Nullable.GetUnderlyingType(value.Type) is { } underlying)
            value = Expression.Convert(value, underlying);   // only reached once the null test passed
        return Expression.Call(value, value.Type.GetMethod(nameof(ToString), Type.EmptyTypes).RequireNotNull());
    }

    private static readonly FilterDefRendererType<DefaultDefRenderer> rendererType = new();

    /// <inheritdoc/>
    public override FilterDefRendererType Renderer() => rendererType;   // a subclass may name its own editor

    // Each default is the type's own, the value options that skip nulls or defaults leave out of the JSON
    /// <summary>The definition as a memento stores it.</summary>
    /// <param name="IncludeNull">Whether a null value passes.</param>
    /// <param name="Value">The text to match, none when null or empty.</param>
    /// <param name="Contains">True to match the text anywhere in the value, false at its start only.</param>
    public record Serialized(bool IncludeNull = false, string? Value = null, bool Contains = false) : AFilterDefSer;

    /// <inheritdoc/>
    protected override Serialized CreateSerRecord() => new Serialized(IncludeNull, GetValue(), Contains);   // "" filters as null does

    /// <inheritdoc/>
    public override DefaultDef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        Serialized ser = JsonSerializer.Deserialize<Serialized>(ref reader, options).RequireNotNull();
        IncludeNull = ser.IncludeNull;
        Value = ser.Value;
        Contains = ser.Contains;
        return this;
    }
}
