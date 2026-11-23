using System.Linq.Expressions;

namespace Glazor.Grds.Props;

//https://stackoverflow.com/a/30489160/1698682
internal class MemberNullPropagationVisitor : ExpressionVisitor
{
    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Expression == null || !ExprUtils.IsNullable(node.Expression.Type))
            return base.VisitMember(node);
        if (node.Type.IsValueType && Nullable.GetUnderlyingType(node.Type) == null)
            return base.VisitMember(node);
        var expression = Visit(node.Expression);
        var v = Expression.Variable(expression.Type, "v");   // the receiver, guarded below it, read once
        return Expression.Block(node.Type, [v],
            Expression.Assign(v, expression),
            Expression.Condition(IsNull(v), Expression.Constant(null, node.Type), node.Update(v)));
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Object == null || !ExprUtils.IsNullable(node.Object.Type))
            return base.VisitMethodCall(node);
        // the same guard as VisitMember: nothing to return in place of a void or a non-nullable value
        if (node.Type == typeof(void) ||
            (node.Type.IsValueType && Nullable.GetUnderlyingType(node.Type) == null))
            return base.VisitMethodCall(node);
        var expression = Visit(node.Object);
        var v = Expression.Variable(expression.Type, "v");
        return Expression.Block(node.Type, [v],
            Expression.Assign(v, expression),
            Expression.Condition(IsNull(v), Expression.Constant(null, node.Type), node.Update(v, Visit(node.Arguments))));
    }

    // By reference, as ?. tests: a user-defined == is not run on every read
    private static Expression IsNull(Expression e) => e.Type.IsValueType
        ? Expression.Equal(e, Expression.Constant(null, e.Type))
        : Expression.ReferenceEqual(e, Expression.Constant(null, e.Type));
}
