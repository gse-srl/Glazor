using Glazor.Grds.Props;
using Glazor.Misc;
using System.Linq.Expressions;
using System.Reflection;

namespace Glazor.Grds.Filters;

/// <summary>
/// Abstract base class for set-based filter definitions, selection is based on the key extracted from each value object.
/// </summary>
/// <typeparam name="SELF">The concrete type implementing this abstract class.</typeparam>
/// <typeparam name="VT">The type of the values being filtered.</typeparam>
/// <typeparam name="KT">The type of the key extracted from each value.</typeparam>
public abstract class ASetDef<SELF, VT, KT> : AFilterDef<SELF, VT> where SELF : ASetDef<SELF, VT, KT>
{
    /// <summary>Gives a value's key, which the selection holds; part of the predicate pushed down to the database.</summary>
    public Expression<Func<VT, KT>> ValueToKeyExpr { get; }
    /// <summary><see cref="ValueToKeyExpr"/> compiled, for the filter applied in memory.</summary>
    public Func<VT, KT> ValueToKeyFn { get; }

    /// <param name="valueToKeyExpr">Gives a value's key, which the selection holds.</param>
    public ASetDef(Expression<Func<VT, KT>> valueToKeyExpr)
    {
        ValueToKeyExpr = valueToKeyExpr;
        ValueToKeyFn = valueToKeyExpr.Compile();
    }

    /// <summary>Whether a null value passes the filter.</summary>
    public abstract bool MustIncludeNull();

    /// <summary>The keys of the values selected: a value passes when its key is one of them.</summary>
    public abstract IReadOnlySet<KT> GetSelected();

    /// <summary>
    /// Every value is selected, those read and any added since: none was left out, or the set starts full and has not read
    /// its values yet.
    /// </summary>
    private protected virtual bool SelectsAll() => false;

    /// <inheritdoc/>
    public override bool Includes(object? value)
    {
        if (value == null)
            return MustIncludeNull();
        IReadOnlySet<KT> selected = GetSelected();
        return selected.Contains(ValueToKeyFn.Invoke((VT)value)) || SelectsAll();   // SelectsAll compares the sets: only for a value not selected
    }

    /// <inheritdoc/>
    public override bool CanCreatePredicate() => true;

    private static readonly MethodInfo containsMethod =
        typeof(IReadOnlySet<KT?>).GetMethod(nameof(IReadOnlySet<KT?>.Contains), new Type[] { typeof(KT) }).RequireNotNull();

    /// <inheritdoc/>
    protected override Expression CreatePredicateImpl<TGridItem>(InvocationExpression _pexpr)
    {
        if (SelectsAll())   // void when it includes null, so here every value but null
            return Expression.NotEqual(_pexpr, Expression.Constant(null, _pexpr.Type));
        IReadOnlySet<KT> selected = GetSelected();
        // value = (VT)_pexpr
        UnaryExpression value = Expression.Convert(_pexpr, typeof(VT));
        // pexpr = (KT)valueToKeyExpr.Invoke(value)
        UnaryExpression pexpr = Expression.Convert(Expression.Invoke(ValueToKeyExpr, value), typeof(KT));
        // vexpr = selected.Contains(pexpr())
        Expression vexpr = Expression.Call(ValueExpr(selected), containsMethod, pexpr);
        if (!ExprUtils.IsNullable(GetValueType()))   // the column may still be nullable: tested on the boxed value, as DefaultDef does
            return MustIncludeNull() ? Expression.OrElse(Expression.ReferenceEqual(_pexpr, Expression.Constant(null)), vexpr) : vexpr;
        // The null test is on the value, whose type can differ from the key's. Its constant is GetValueType(), not typeof(VT):
        // EnumDef maps a nullable enum column to Enum, and NullableExpression lifts the value to the column's type so EF can bind it.
        //
        // res = MustIncludeNull() ? value() == null || vexpr() : value() != null && vexpr();
        Expression res = MustIncludeNull() ?
            Expression.OrElse(ExprUtils.NullableExpression(value, Expression.Constant(null, GetValueType()), Expression.Equal), vexpr)
            : Expression.AndAlso(ExprUtils.NullableExpression(value, Expression.Constant(null, GetValueType()), Expression.NotEqual), vexpr);
        return res;
    }
}

/// <summary>
/// Abstract base class for set filter definitions where the value itself is the key the selection holds.
/// </summary>
/// <typeparam name="SELF">The concrete type inheriting from ASetDef.</typeparam>
/// <typeparam name="VT">The value type managed by the set definition.</typeparam>
public abstract class ASetDef<SELF, VT> : ASetDef<SELF, VT, VT> where SELF : ASetDef<SELF, VT> 
{
    /// <summary>A set whose keys are the values themselves.</summary>
    public ASetDef() : base(vt => vt)
    {
    }
}
