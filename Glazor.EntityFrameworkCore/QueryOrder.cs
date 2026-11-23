using System.Linq.Expressions;
using System.Reflection;

namespace Glazor.EntityFrameworkCore;

/// <summary>
/// The members of the row that a query's own OrderBy and ThenBy calls read. A change to none of them cannot move the row in
/// that order, since a selector is a function of what it reads.
/// </summary>
internal static class QueryOrder
{
    /// <returns>null when an order reads more than the row's own scalar members: a selector over another type, after a
    ///     projection or a join, a navigation, whose values may not be loaded, a member of a derived type, or the row as a
    ///     whole.</returns>
    internal static IReadOnlyList<MemberInfo>? RowMembers(Expression query, Type rowType)
    {
        List<MemberInfo> members = new();
        bool projected = false;   // a Select after the order may drop or remap the members it reads
        while (query is MethodCallExpression call && call.Arguments.Count > 0 && typeof(IQueryable).IsAssignableFrom(call.Arguments[0].Type)
            && !IsSetOperation(call))   // EF drops an order a set operation follows: the model pages by the key alone
        {
            if (call.Method.Name is nameof(Queryable.Select))
                projected = true;
            else if (call.Method.Name is nameof(Queryable.OrderBy) or nameof(Queryable.OrderByDescending)
                or nameof(Queryable.ThenBy) or nameof(Queryable.ThenByDescending))
            {
                if (projected || call.Arguments.Count < 2 || StripQuotes(call.Arguments[1]) is not LambdaExpression selector
                        || selector.Parameters.Count != 1 || selector.Parameters[0].Type != rowType)
                    return null;
                MembersVisitor visitor = new(selector.Parameters[0]);
                visitor.Visit(selector.Body);
                if (visitor.Unsupported)
                    return null;
                members.AddRange(visitor.Members);
            }
            query = call.Arguments[0];
        }
        return members.Distinct().ToList();
    }

    internal static bool IsSetOperation(MethodCallExpression call) => call.Method.DeclaringType == typeof(Queryable)
        && call.Method.Name is nameof(Queryable.Distinct) or nameof(Queryable.DistinctBy) or nameof(Queryable.Union)
            or nameof(Queryable.UnionBy) or nameof(Queryable.Concat) or nameof(Queryable.Intersect) or nameof(Queryable.IntersectBy)
            or nameof(Queryable.Except) or nameof(Queryable.ExceptBy);

    internal static Func<TRow, object?> Getter<TRow>(MemberInfo member)
    {
        ParameterExpression row = Expression.Parameter(typeof(TRow), "row");
        return Expression.Lambda<Func<TRow, object?>>(Expression.Convert(Expression.MakeMemberAccess(row, member), typeof(object)), row)
            .Compile();
    }

    private static Expression StripQuotes(Expression expr)
    {
        while (expr is UnaryExpression { NodeType: ExpressionType.Quote } quote)
            expr = quote.Operand;
        return expr;
    }

    private static Expression? StripConverts(Expression? expr)
    {
        while (expr is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs } convert)
            expr = convert.Operand;
        return expr;
    }

    private static bool IsScalar(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime)
            || type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(TimeSpan)
            || type == typeof(Guid);
    }

    private sealed class MembersVisitor(ParameterExpression row) : ExpressionVisitor
    {
        internal List<MemberInfo> Members { get; } = new();

        internal bool Unsupported { get; private set; }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (StripConverts(node.Expression) != row)
                return base.VisitMember(node);
            Type type = node.Member is PropertyInfo property ? property.PropertyType : ((FieldInfo)node.Member).FieldType;
            // A cast may reach a member of a derived type, which rows of other types do not have
            if (IsScalar(type) && node.Member.DeclaringType!.IsAssignableFrom(row.Type))
                Members.Add(node.Member);
            else
                Unsupported = true;
            return node;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == row)
                Unsupported = true;   // reached only when the row is used other than through one of its members
            return node;
        }
    }
}
