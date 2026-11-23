using System.Linq.Expressions;

namespace Glazor.Grds.Props;

/// <summary>Null-safe expression helpers, for a host that writes its own filter predicates.</summary>
public static class ExprUtils
{
    /// <summary>The untyped form of <see cref="PropagateNull{TDelegate}(Expression{TDelegate})"/>.</summary>
    public static Expression PropagateNull(Expression expression)
    {
        return new MemberNullPropagationVisitor().Visit(expression);
    }

    /// <summary>
    /// Rewrites member access chains so a null in the middle yields null rather than throwing. The result is for compiling:
    /// it holds blocks, which a LINQ provider such as EF Core does not translate.
    /// <para>
    /// A member whose own type is a non-nullable value type cannot be guarded, because the rewrite would have
    /// to change the type of the node. The <em>member itself</em> has to be nullable; casting the result of the
    /// access does not help, since the cast sits outside the access that would throw:
    /// <code>
    /// x => (int?)x.Child.Age       // still throws when Child is null: the cast is outside the access
    /// x => x.Child.NullableAge     // ok, the member is already int?
    /// </code>
    /// When the member cannot be changed, write the guard by hand instead:
    /// <code>
    /// x => x.Child == null ? null : (int?)x.Child.Age
    /// </code>
    /// </para>
    /// </summary>
    public static Expression<TDelegate> PropagateNull<TDelegate>(Expression<TDelegate> expression)
    {
        return (Expression<TDelegate>)new MemberNullPropagationVisitor().Visit(expression);
    }

    /// <summary>Whether a value of type <paramref name="t"/> can be null: a class, an interface or a <see cref="Nullable{T}"/>.</summary>
    public static bool IsNullable(Type t)
    {
        return t.IsClass || t.IsInterface || IsNullableStructType(t);
    }

    /// <summary>Whether <paramref name="t"/> is a <see cref="Nullable{T}"/>.</summary>
    public static bool IsNullableStructType(Type t)
    {
        return Nullable.GetUnderlyingType(t) != null;
    }

    /// <summary>
    /// Applies <paramref name="binaryExpression"/>, such as <c>Expression.Equal</c>, to the two operands, first lifting the one
    /// that is not a <see cref="Nullable{T}"/> to the other's type when only one of them is.
    /// </summary>
    public static BinaryExpression NullableExpression(Expression e1, Expression e2,
        Func<Expression, Expression, BinaryExpression> binaryExpression)
    {
        if (IsNullableStructType(e1.Type) && !IsNullableStructType(e2.Type))
            e2 = Expression.Convert(e2, e1.Type);
        else if (!IsNullableStructType(e1.Type) && IsNullableStructType(e2.Type))
            e1 = Expression.Convert(e1, e2.Type);
        return binaryExpression(e1, e2);
    }

    /// <summary>
    /// <c>(o) =&gt; exprs[0](o) &amp;&amp; exprs[1](o) ... exprs[n](o)</c>
    /// </summary>
    public static Expression<Func<TGridItem, VT>> CombineExpressions<TGridItem, VT>(IEnumerable<Expression> exprs,
        Func<Expression, Expression, BinaryExpression> combiner)
    {
        ParameterExpression paramO = Expression.Parameter(typeof(TGridItem), "o");
        Expression? prev = null;
        foreach (Expression expr in exprs)
        {
            if (prev == null)
                prev = Expression.Invoke(expr, paramO);
            else
                prev = combiner(prev, Expression.Invoke(expr, paramO));
        }
        if (prev == null)
            throw new ArgumentException("Empty: " + nameof(exprs));
        return Expression.Lambda<Func<TGridItem, VT>>(prev, paramO);
    }
}
