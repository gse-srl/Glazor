using Glazor.Grds.Props;
using System.Linq.Expressions;

namespace Glazor.Grds.Filters;

/// <summary>
/// Represents an abstract range filter definition that supports specifying lower and upper bounds, as well as inclusion
/// of null values, for filtering operations.
/// </summary>
/// <remarks>This class provides a base for implementing range-based filters, allowing derived types to define how
/// range boundaries and null inclusion are handled. It is commonly used in scenarios where filtering by a value range
/// is required, such as querying data grids or collections. Implementations should specify the logic for determining
/// the range limits and whether null values are considered part of the range.</remarks>
/// <typeparam name="SELF">The type of the derived range filter definition. Must inherit from <c>ARangeDef&lt;SELF, VT&gt;</c>.</typeparam>
/// <typeparam name="VT">The type of the values used for the range boundaries.</typeparam>
public abstract class ARangeDef<SELF, VT> : AFilterDef<SELF, VT> where SELF : ARangeDef<SELF, VT>
{
    /// <summary>Whether a null value passes the filter.</summary>
    public abstract bool MustIncludeNull();

    /// <summary>The lower bound, included; null for none.</summary>
    public abstract VT? GetFrom();

    /// <summary>The upper bound, included; null for none.</summary>
    public abstract VT? GetTo();

    /// <summary>
    /// Where the range ends, and whether that end is excluded: <see cref="GetTo"/>, included, unless a subclass widens the
    /// bound, as <see cref="DateDef{VT}"/> does to the end of the minute or day its editor picks.
    /// </summary>
    protected virtual (VT? End, bool Excluded) GetEnd() => (GetTo(), false);

    /// <inheritdoc/>
    public override bool IsVoid()
    {
        return MustIncludeNull() && GetFrom() == null && GetTo() == null;
    }

    /// <inheritdoc/>
    public override bool Includes(object? value)
    {
        if (value == null)
            return MustIncludeNull();
        VT? from = GetFrom();
        (VT? end, bool excluded) = GetEnd();
        if (from == null && end == null)
            return true;
        if (from != null && ((IComparable)value).CompareTo(from) < 0)
            return false;
        if (end != null && ((IComparable)value).CompareTo(end) is int cmp && (excluded ? cmp >= 0 : cmp > 0))
            return false;
        return true;
    }

    /// <inheritdoc/>
    public override bool CanCreatePredicate() => true;

    /// <inheritdoc/>
    protected override Expression CreatePredicateImpl<TGridItem>(InvocationExpression _pexpr)
    {
        VT? from = GetFrom();
        (VT? end, bool excluded) = GetEnd();
        Func<Expression, Expression, BinaryExpression> beforeEnd = excluded ? Expression.LessThan : Expression.LessThanOrEqual;
        UnaryExpression pexpr = Expression.Convert(_pexpr, typeof(VT));
        BinaryExpression rangeExpr = Expression.AndAlso(
            from == null ? Expression.Constant(true) : ExprUtils.NullableExpression(pexpr, ValueExpr(from), Expression.GreaterThanOrEqual),
            end == null ? Expression.Constant(true) : ExprUtils.NullableExpression(pexpr, ValueExpr(end), beforeEnd));
        if (!ExprUtils.IsNullable(GetValueType()))
            return rangeExpr;
        Expression res = MustIncludeNull() ?
            Expression.OrElse(Expression.Equal(pexpr, Expression.Constant(null, GetValueType())), rangeExpr)
            : Expression.AndAlso(Expression.NotEqual(pexpr, Expression.Constant(null, GetValueType())), rangeExpr);
        return res;
    }

    // Each default is the type's own, the value options that skip nulls or defaults leave out of the JSON
    /// <summary>The definition as a memento stores it.</summary>
    /// <param name="IncludeNull">Whether a null value passes.</param>
    /// <param name="From">The lower end of the range, none when null.</param>
    /// <param name="To">The upper end of the range, none when null.</param>
    public record Serialized(bool IncludeNull = false, VT? From = default, VT? To = default) : AFilterDefSer;

    /// <inheritdoc/>
    protected override Serialized CreateSerRecord() => new Serialized(MustIncludeNull(), GetFrom(), GetTo());

}
