using Glazor.Grds.Props;
using Glazor.Misc;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Glazor.EntityFrameworkCore;

/// <summary>
/// The key of an <see cref="EFDataGridModel{TDbContext, TGridItem}"/>'s rows: a column, or several as an anonymous type, a
/// record, a class whose constructor takes its properties, or a tuple (<c>a =&gt; new { a.X, a.Y }</c>, <c>a =&gt; ValueTuple.Create(a.X, a.Y)</c>). Neither EF nor a comparer can order or compare a composite object as a whole, so
/// ordering, comparison and the key predicate go member by member; a plain key is its own only member. A composite key of a
/// class that does not compare by value is refused.
/// </summary>
internal sealed class ItemKeyHelper<TGridItem>
{
    private readonly Expression<Func<TGridItem, object>> _keyExpr;
    private readonly Expression<Func<TGridItem, object>>[] _parts;
    private Func<TGridItem, object>? _keyFn;
    private Func<TGridItem, object>[]? _partFns;

    internal ItemKeyHelper(Expression<Func<TGridItem, object>> keyExpr)
    {
        Expression body = Body(keyExpr);
        // the current row is found again by comparing whole keys
        if (body is NewExpression { Members: null } or MemberInitExpression && !ComparesByValue(body.Type))
            throw new InvalidOperationException(
                $"ItemKeyExpr: {body.Type.Name} must compare by value, as a record or a type overriding Equals and GetHashCode does");
        _keyExpr = keyExpr;
        _parts = Members(keyExpr)
            .Select(m => Expression.Lambda<Func<TGridItem, object>>(m.Part.Type.IsValueType ? Expression.Convert(m.Part, typeof(object)) : m.Part,
                keyExpr.Parameters))
            .ToArray();
    }

    internal Func<TGridItem, object> KeyFn => _keyFn ??= _keyExpr.Compile();

    /// <summary>The type of the key, the member's own for a plain key.</summary>
    internal Type KeyType => Body(_keyExpr).Type;

    internal IOrderedQueryable<TGridItem> OrderBy(IQueryable<TGridItem> query) => ThenBy(query.OrderBy(_parts[0]), true, 1);

    internal IOrderedQueryable<TGridItem> ThenBy(IOrderedQueryable<TGridItem> query, bool ascending) => ThenBy(query, ascending, 0);

    private IOrderedQueryable<TGridItem> ThenBy(IOrderedQueryable<TGridItem> query, bool ascending, int from)
    {
        for (int i = from; i < _parts.Length; i++)
            query = ascending ? query.ThenBy(_parts[i]) : query.ThenByDescending(_parts[i]);
        return query;
    }

    // EF drops an order that a set operation follows, so a walk down the query stops there
    internal static bool HasOrdering(Expression query)
    {
        while (query is MethodCallExpression call && call.Arguments.Count > 0 && typeof(IQueryable).IsAssignableFrom(call.Arguments[0].Type)
            && !QueryOrder.IsSetOperation(call))
        {
            if (call.Method.Name is nameof(Queryable.OrderBy) or nameof(Queryable.OrderByDescending) or nameof(Queryable.ThenBy) or nameof(Queryable.ThenByDescending))
                return true;
            query = call.Arguments[0];
        }
        return false;
    }

    // The key right after the last OrderBy or ThenBy, when every call that follows keeps the row type (AsNoTracking, Include,
    // Where, a Select to the same type); null when one does not, or there is no ordering
    internal IQueryable<TGridItem>? WithTheKeyAfterTheLastOrdering(IQueryable<TGridItem> query)
    {
        List<MethodCallExpression> following = [];
        Expression node = query.Expression;
        while (node is MethodCallExpression call && call.Arguments.Count > 0 && typeof(IQueryable).IsAssignableFrom(call.Arguments[0].Type)
            && typeof(IQueryable<TGridItem>).IsAssignableFrom(call.Type) && !QueryOrder.IsSetOperation(call))
        {
            if (call.Method.Name is nameof(Queryable.OrderBy) or nameof(Queryable.OrderByDescending) or nameof(Queryable.ThenBy) or nameof(Queryable.ThenByDescending))
            {
                if (query.Provider.CreateQuery<TGridItem>(call) is not IOrderedQueryable<TGridItem> ordered)
                    return null;
                Expression completed = ThenBy(ordered, true).Expression;
                for (int i = following.Count - 1; i >= 0; i--)
                {
                    // IQueryable<> is covariant: a call over a type derived from the row's cannot take a ThenBy over the row type
                    if (!following[i].Method.GetParameters()[0].ParameterType.IsAssignableFrom(completed.Type))
                        return null;
                    completed = following[i].Update(following[i].Object, following[i].Arguments.Skip(1).Prepend(completed));
                }
                return query.Provider.CreateQuery<TGridItem>(completed);
            }
            following.Add(call);
            node = call.Arguments[0];
        }
        return null;
    }

    internal int Compare(TGridItem gi1, TGridItem gi2)
    {
        foreach (Func<TGridItem, object> part in _partFns ??= _parts.Select(p => p.Compile()).ToArray())
        {
            object v1 = part(gi1), v2 = part(gi2);
            int res = Object.ReferenceEquals(v1, v2) ? 0     // both null too
                : v1 is IComparable c1 ? c1.CompareTo(v2)
                : v2 is IComparable c2 ? -c2.CompareTo(v1)
                : throw new InvalidOperationException("ItemKeyExpr must be IComparable, member by member for a composite key");
            if (res != 0)
                return res;
        }
        return 0;
    }

    /// <summary>
    /// <c>key(o) == value</c>, as EF can translate it: the key boxes its value (<c>Convert(a.Id, object)</c>) and
    /// <c>Equal(object, int)</c> has no operator, so each unboxed member is compared with a value of its own type, read from a box
    /// so that EF sends it as a parameter, on the key's own parameter, which gives a plain <c>WHERE Id = @p</c>, or one equality
    /// per member of a composite key, and one query for every key.
    /// </summary>
    internal static Expression<Func<TGridItem, bool>> Predicate(Expression<Func<TGridItem, object>> key, object value)
    {
        Expression? vexpr = null;
        foreach ((Expression part, MemberInfo? member) in Members(key))
        {
            object? memberValue = member switch
            {
                null => value,
                PropertyInfo p => p.GetValue(value),
                FieldInfo f => f.GetValue(value),
                MethodInfo m => m.Invoke(value, null),
                _ => throw new NotSupportedException($"Key member {member}"),
            };
            Expression boxed = Expression.Field(Expression.Constant(new StrongBox<object?>(memberValue)), nameof(StrongBox<object?>.Value));
            Expression eq = ExprUtils.NullableExpression(part, Expression.Convert(boxed, part.Type), Expression.Equal);
            vexpr = vexpr == null ? eq : Expression.AndAlso(vexpr, eq);
        }
        return Expression.Lambda<Func<TGridItem, bool>>(vexpr.RequireNotNull(), key.Parameters);
    }

    private static Expression Body(Expression<Func<TGridItem, object>> key)
        => key.Body is UnaryExpression { NodeType: ExpressionType.Convert } boxed ? boxed.Operand : key.Body;

    private static bool ComparesByValue(Type type)
        => type.IsValueType || type.GetMethod(nameof(Equals), new[] { typeof(object) })!.DeclaringType != typeof(object);

    private static List<(Expression Part, MemberInfo? Member)> Members(Expression<Func<TGridItem, object>> key)
    {
        Expression body = Body(key);
        List<(Expression Part, MemberInfo? Member)>? members = body switch
        {
            NewExpression n => TupleItems(n.Type, n.Arguments) ?? ConstructorMembers(n),
            MethodCallExpression { Method.Name: nameof(Tuple.Create) } c => TupleItems(c.Type, c.Arguments),
            MemberInitExpression mi => ConstructorMembers(mi.NewExpression)?
                .Concat(mi.Bindings.OfType<MemberAssignment>().Select(b => (b.Expression, (MemberInfo?)b.Member))).ToList(),
            _ => null,
        };
        return members is { Count: > 0 } ? members : [(body, null)];
    }

    // The constructor's arguments with the members they set, or null when they cannot be named
    private static List<(Expression Part, MemberInfo? Member)>? ConstructorMembers(NewExpression n)
    {
        if (n.Arguments.Count == 0)
            return new();
        IReadOnlyList<MemberInfo>? members = n.Members;   // an anonymous type's
        members ??= ParameterProperties(n);
        return members?.Select((m, i) => (n.Arguments[i], (MemberInfo?)m)).ToList();
    }

    private static readonly HashSet<Type> tupleTypes =
    [
        typeof(ValueTuple<>), typeof(ValueTuple<,>), typeof(ValueTuple<,,>), typeof(ValueTuple<,,,>), typeof(ValueTuple<,,,,>),
        typeof(ValueTuple<,,,,,>), typeof(ValueTuple<,,,,,,>),
        typeof(Tuple<>), typeof(Tuple<,>), typeof(Tuple<,,>), typeof(Tuple<,,,>), typeof(Tuple<,,,,>), typeof(Tuple<,,,,,>), typeof(Tuple<,,,,,,>),
    ];

    // A Tuple or ValueTuple of up to seven items, built with new or Create: each argument is the item at its place, a field of a
    // ValueTuple, a property of a Tuple. An expression takes no tuple literal, so these are the forms a key can have.
    private static List<(Expression Part, MemberInfo? Member)>? TupleItems(Type type, IReadOnlyList<Expression> arguments)
    {
        if (!type.IsGenericType || !tupleTypes.Contains(type.GetGenericTypeDefinition()))
            return null;
        return arguments.Select((a, i) => (a, (MemberInfo?)type.GetMember("Item" + (i + 1)).Single())).ToList();
    }

    // A positional record, or a class: each constructor parameter is the property of the same name and type, the name in
    // camelCase for a class
    private static PropertyInfo[]? ParameterProperties(NewExpression n)
    {
        ParameterInfo[] parameters = n.Constructor?.GetParameters() ?? [];
        PropertyInfo[] props = parameters.Select(p => n.Type.GetProperty(p.Name!, p.ParameterType)
                ?? n.Type.GetProperty(p.Name!, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase, null, p.ParameterType, [], null))
            .OfType<PropertyInfo>().Where(p => p.GetMethod != null).ToArray();
        return parameters.Length > 0 && props.Length == parameters.Length ? props : null;
    }
}
