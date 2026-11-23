using System.Linq.Expressions;
using System.Reflection;

namespace Glazor.Grds.Mdls;

/// <summary>
/// Whether a column's value can be null, for the filter dialog's "Include null" box: a value type only when it is
/// <see cref="Nullable{T}"/>; a reference type when the member chain its expression reads has a member the compiler's
/// nullable annotations do not mark non-null, or when there is no expression to read.
/// </summary>
internal static class ColumnNullability
{
    public static bool ValueCanBeNull<TGridItem>(this IColumnInfo<TGridItem> info)
    {
        Type type = info.Column.GetValueType();
        if (type.IsValueType)
            return Nullable.GetUnderlyingType(type) != null;
        if (info.PropertyExpression?.Body is not UnaryExpression { Operand: InvocationExpression { Expression: LambdaExpression getter } })
            return true;
        NullabilityInfoContext ctx = new();
        Expression? e = getter.Body;
        while (true)
        {
            switch (e)
            {
                case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } u:
                    e = u.Operand;
                    continue;
                case MemberExpression m:
                    NullabilityState state = m.Member switch
                    {
                        PropertyInfo p => ctx.Create(p).ReadState,
                        FieldInfo f => ctx.Create(f).ReadState,
                        _ => NullabilityState.Unknown,
                    };
                    if (state != NullabilityState.NotNull)
                        return true;
                    if (m.Expression == null)
                        return false;   // a static member, checked above
                    e = m.Expression;
                    continue;
                case ParameterExpression:
                    return false;
                default:
                    return true;   // a call, a conditional, anything the annotations do not describe
            }
        }
    }
}
