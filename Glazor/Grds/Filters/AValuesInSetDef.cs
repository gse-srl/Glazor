using Glazor.Misc;
using System.Linq.Expressions;
using System.Reflection;

namespace Glazor.Grds.Filters;

/// <summary>
/// The filter of a column whose value is a collection, an <see cref="IQueryable{VT}"/>: a row passes when the key of one of
/// its items is selected.
/// </summary>
/// <typeparam name="SELF">The concrete type deriving from this class.</typeparam>
/// <typeparam name="EVT">The type of the column's value, the collection.</typeparam>
/// <typeparam name="VT">The type of the collection's items.</typeparam>
/// <typeparam name="KT">The type of an item's key.</typeparam>
public abstract class AValuesInSetDef<SELF, EVT, VT, KT> : AFilterDef<SELF, EVT> where SELF : AValuesInSetDef<SELF, EVT, VT, KT> where EVT : IQueryable<VT>
{
    /// <summary>True keeps the filter in memory: it is never pushed down to the database, even on an EF model.</summary>
    public bool AlwaysInMemory { get; }
    /// <summary>Gives an item's key, which the selection holds; part of the predicate pushed down to the database.</summary>
    public Expression<Func<VT, KT>> ValueToKeyExpr { get; }
    /// <summary><see cref="ValueToKeyExpr"/> compiled, for the filter applied in memory.</summary>
    public Func<VT, KT> ValueToKeyFn { get; }

    /// <param name="alwaysInMemory">True keeps the filter in memory: see <see cref="AlwaysInMemory"/>.</param>
    /// <param name="valueToKeyExpr">Gives an item's key, which the selection holds.</param>
    public AValuesInSetDef(bool alwaysInMemory, Expression<Func<VT, KT>> valueToKeyExpr)
    {
        AlwaysInMemory = alwaysInMemory;
        ValueToKeyExpr = valueToKeyExpr;
        ValueToKeyFn = valueToKeyExpr.Compile();
    }

    /// <summary>Whether a null or empty collection passes the filter.</summary>
    public abstract bool MustIncludeNullOrEmpty();

    /// <summary>The keys of the values selected: a collection passes when the key of one of its items is one of them.</summary>
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
            return MustIncludeNullOrEmpty();
        IEnumerable<VT> values = (EVT)value;   // Enumerable.Any: Queryable.Any on an EnumerableQuery compiles a tree per call
        if (!values.Any())  // ie. Count() == 0
            return MustIncludeNullOrEmpty();
        IReadOnlySet<KT> selected = GetSelected();
        // a row passes when any of its values is selected; SelectsAll compares the sets: only for a row with none selected
        return values.Any(v => selected.Contains(ValueToKeyFn.Invoke(v))) || SelectsAll();
    }

    /// <inheritdoc/>
    public override bool CanCreatePredicate() => !AlwaysInMemory;

    private static readonly MethodInfo containsMethod =
        typeof(IReadOnlySet<KT?>).GetMethod(nameof(IReadOnlySet<KT?>.Contains), new Type[] { typeof(KT) }).RequireNotNull();

    // This is really an extension and so it's a static method
    private static readonly MethodInfo anyFun =
        ((Func<IQueryable<VT>, bool>)Queryable.Any<VT>).Method;
    // This is really an extension and so it's a static method
    private static readonly MethodInfo anyMethod =
        ((Func<IQueryable<VT>, Expression<Func<VT, bool>>, bool>)Queryable.Any<VT>).Method;

    /// <inheritdoc/>
    protected override Expression CreatePredicateImpl<TGridItem>(InvocationExpression _pexpr)
    {
        IReadOnlySet<KT> selected = GetSelected();
        // EF translates no cast on a collection navigation: the column's expression is invoked as it is, without its box to object
        Expression evt_pexpr = Expression.Convert(_pexpr, typeof(EVT));
        if (_pexpr.Expression is LambdaExpression prop)
        {
            Expression body = prop.Body is UnaryExpression { NodeType: ExpressionType.Convert } box && box.Type == typeof(object) ? box.Operand : prop.Body;
            if (typeof(EVT).IsAssignableFrom(body.Type))
                evt_pexpr = Expression.Invoke(Expression.Lambda(body, prop.Parameters), _pexpr.Arguments);
        }
        var vParam = Expression.Parameter(typeof(VT), "v");
        var invokeAndCastExpr = Expression.Convert(Expression.Invoke(ValueToKeyExpr, vParam), typeof(KT));
        var containsCall = Expression.Call(ValueExpr(selected), containsMethod, invokeAndCastExpr);
        var predicate = Expression.Lambda(containsCall, vParam);
        // vexpr = _pexpr().Any(v => selected.Contains((KT)ValueToKeyExpr.Invoke(v))), or _pexpr().Any() when every value is selected
        Expression vexpr = SelectsAll() ? Expression.Call(anyFun, evt_pexpr) : Expression.Call(anyMethod, evt_pexpr, predicate);
        if (MustIncludeNullOrEmpty())
        {
            // vexpr = !_pexpr().Any() || vexpr()
            vexpr = Expression.OrElse(Expression.Not(Expression.Call(anyFun, evt_pexpr)), vexpr);
        }
        return vexpr;
    }
}
