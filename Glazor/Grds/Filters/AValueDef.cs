using Glazor.Grds.Props;
using System.Linq.Expressions;

namespace Glazor.Grds.Filters;

/// <summary>
/// The base of a definition that filters on one value, as <see cref="BoolDef"/> and <see cref="DefaultDef"/> do: a row passes
/// when its value equals it, or is null and nulls are included. In memory <typeparamref name="VT"/> must implement
/// <see cref="IComparable"/>: a value equals it when <see cref="IComparable.CompareTo"/> returns 0.
/// </summary>
public abstract class AValueDef<SELF, VT> : AFilterDef<SELF, VT> where SELF : AValueDef<SELF, VT>
{
    /// <summary>Whether a row whose value is null passes.</summary>
    public abstract bool MustIncludeNull();

    /// <summary>The value to match; null passes every row whose value is not null.</summary>
    public abstract VT? GetValue();

    /// <inheritdoc/>
    public override bool IsVoid()
    {
        return MustIncludeNull() && GetValue() == null;
    }

    /// <inheritdoc/>
    public override bool Includes(object? value)
    {
        if (value == null)
            return MustIncludeNull();
        VT? vv = GetValue();  // null means accept all except nulls
        if (vv == null)
            return true;
        return Object.ReferenceEquals(vv, value) ||
            ((IComparable)vv).CompareTo(value) == 0;
    }

    /// <inheritdoc/>
    public override bool CanCreatePredicate() => true;

    /// <inheritdoc/>
    protected override Expression CreatePredicateImpl<TGridItem>(InvocationExpression _pexpr)
    {
        VT? vv = GetValue();
        UnaryExpression pexpr = Expression.Convert(_pexpr, typeof(VT));
        Expression vexpr = vv == null ? Expression.Constant(true)  // null means accept all except nulls
            : ExprUtils.NullableExpression(pexpr, ValueExpr(vv), Expression.Equal);
        if (!ExprUtils.IsNullable(GetValueType()))
            return vexpr;
        Expression res = MustIncludeNull() ?
            Expression.OrElse(Expression.Equal(pexpr, Expression.Constant(null, GetValueType())), vexpr)
            : Expression.AndAlso(Expression.NotEqual(pexpr, Expression.Constant(null, GetValueType())), vexpr);
        return res;
    }
}
