using System.Linq.Expressions;

namespace Glazor.Misc;

/// <summary>
/// Structural equality of expression trees: two lambdas written the same compare equal even when
/// they are different instances, as they are when a host builds one on every render.
/// Lambda parameters are matched by position; constants by <see cref="object.Equals(object?, object?)"/>.
/// </summary>
internal static class ExpressionComparer
{
    public static bool Equals(Expression? x, Expression? y) => new Walker().Eq(x, y);

    private sealed class Walker
    {
        private readonly List<(ParameterExpression X, ParameterExpression Y)> _scope = [];

        public bool Eq(Expression? x, Expression? y)
        {
            if (ReferenceEquals(x, y))
                return true;
            if (x == null || y == null || x.NodeType != y.NodeType || x.Type != y.Type)
                return false;
            switch (x)
            {
                case LambdaExpression lx:
                {
                    var ly = (LambdaExpression)y;
                    if (lx.Parameters.Count != ly.Parameters.Count)
                        return false;
                    for (int i = 0; i < lx.Parameters.Count; i++)
                    {
                        if (lx.Parameters[i].Type != ly.Parameters[i].Type)
                            return false;
                        _scope.Add((lx.Parameters[i], ly.Parameters[i]));
                    }
                    bool res = Eq(lx.Body, ly.Body);
                    _scope.RemoveRange(_scope.Count - lx.Parameters.Count, lx.Parameters.Count);
                    return res;
                }
                case ParameterExpression px:
                {
                    var py = (ParameterExpression)y;
                    for (int i = _scope.Count - 1; i >= 0; i--)
                        if (ReferenceEquals(_scope[i].X, px))
                            return ReferenceEquals(_scope[i].Y, py);
                    return false;   // a parameter bound outside the trees compared
                }
                case MemberExpression mx:
                {
                    var my = (MemberExpression)y;
                    return mx.Member == my.Member && Eq(mx.Expression, my.Expression);
                }
                case MethodCallExpression cx:
                {
                    var cy = (MethodCallExpression)y;
                    return cx.Method == cy.Method && Eq(cx.Object, cy.Object) && EqAll(cx.Arguments, cy.Arguments);
                }
                case ConstantExpression kx:
                    return object.Equals(kx.Value, ((ConstantExpression)y).Value);
                case UnaryExpression ux:
                {
                    var uy = (UnaryExpression)y;
                    return ux.Method == uy.Method && Eq(ux.Operand, uy.Operand);
                }
                case BinaryExpression bx:
                {
                    var by = (BinaryExpression)y;
                    return bx.Method == by.Method && bx.IsLiftedToNull == by.IsLiftedToNull
                        && Eq(bx.Left, by.Left) && Eq(bx.Right, by.Right) && Eq(bx.Conversion, by.Conversion);
                }
                case ConditionalExpression cx:
                {
                    var cy = (ConditionalExpression)y;
                    return Eq(cx.Test, cy.Test) && Eq(cx.IfTrue, cy.IfTrue) && Eq(cx.IfFalse, cy.IfFalse);
                }
                case NewExpression nx:
                {
                    var ny = (NewExpression)y;
                    return nx.Constructor == ny.Constructor && EqAll(nx.Arguments, ny.Arguments)
                        && (nx.Members ?? []).SequenceEqual(ny.Members ?? []);
                }
                case NewArrayExpression ax:
                    return EqAll(ax.Expressions, ((NewArrayExpression)y).Expressions);
                case MemberInitExpression ix:
                {
                    var iy = (MemberInitExpression)y;
                    return Eq(ix.NewExpression, iy.NewExpression) && EqBindings(ix.Bindings, iy.Bindings);
                }
                case ListInitExpression lx:
                {
                    var ly = (ListInitExpression)y;
                    if (!Eq(lx.NewExpression, ly.NewExpression) || lx.Initializers.Count != ly.Initializers.Count)
                        return false;
                    for (int i = 0; i < lx.Initializers.Count; i++)
                        if (lx.Initializers[i].AddMethod != ly.Initializers[i].AddMethod
                            || !EqAll(lx.Initializers[i].Arguments, ly.Initializers[i].Arguments))
                            return false;
                    return true;
                }
                case InvocationExpression vx:
                {
                    var vy = (InvocationExpression)y;
                    return Eq(vx.Expression, vy.Expression) && EqAll(vx.Arguments, vy.Arguments);
                }
                case IndexExpression dx:
                {
                    var dy = (IndexExpression)y;
                    return dx.Indexer == dy.Indexer && Eq(dx.Object, dy.Object) && EqAll(dx.Arguments, dy.Arguments);
                }
                case TypeBinaryExpression tx:
                {
                    var ty = (TypeBinaryExpression)y;
                    return tx.TypeOperand == ty.TypeOperand && Eq(tx.Expression, ty.Expression);
                }
                case DefaultExpression:
                    return true;
                default:
                    return false;
            }
        }

        private bool EqAll(IReadOnlyList<Expression> xs, IReadOnlyList<Expression> ys)
        {
            if (xs.Count != ys.Count)
                return false;
            for (int i = 0; i < xs.Count; i++)
                if (!Eq(xs[i], ys[i]))
                    return false;
            return true;
        }

        private bool EqBindings(IReadOnlyList<MemberBinding> xs, IReadOnlyList<MemberBinding> ys)
        {
            if (xs.Count != ys.Count)
                return false;
            for (int i = 0; i < xs.Count; i++)
            {
                if (xs[i].BindingType != ys[i].BindingType || xs[i].Member != ys[i].Member)
                    return false;
                if (xs[i] is MemberAssignment ax)
                {
                    if (!Eq(ax.Expression, ((MemberAssignment)ys[i]).Expression))
                        return false;
                }
                else if (xs[i] is MemberMemberBinding mx)
                {
                    if (!EqBindings(mx.Bindings, ((MemberMemberBinding)ys[i]).Bindings))
                        return false;
                }
                else
                    return false;   // list bindings are not compared
            }
            return true;
        }
    }
}
