using Glazor.Misc;
using System.Linq.Expressions;

namespace Glazor.Tests;

[TestFixture]
public class ExpressionComparerTests
{
    private sealed record Item(int Id, string Name, Item? Parent);

    private static Expression<Func<Item, T>> L<T>(Expression<Func<Item, T>> e) => e;

    [Test]
    public void SameSourceInDifferentInstancesIsEqualTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ExpressionComparer.Equals(L(i => i.Id), L(i => i.Id)), Is.True);
            Assert.That(ExpressionComparer.Equals(L(i => i.Parent!.Name.Length + 1), L(x => x.Parent!.Name.Length + 1)), Is.True, "parameter names do not matter");
            Assert.That(ExpressionComparer.Equals(L(i => i.Name.StartsWith("a")), L(i => i.Name.StartsWith("a"))), Is.True);
            Assert.That(ExpressionComparer.Equals(L(i => new { i.Id, i.Name }), L(i => new { i.Id, i.Name })), Is.True);
            Assert.That(ExpressionComparer.Equals(L(i => i.Parent == null ? "" : i.Parent.Name), L(i => i.Parent == null ? "" : i.Parent.Name)), Is.True);
            Assert.That(ExpressionComparer.Equals(null, null), Is.True);
        });
    }

    [Test]
    public void DifferentTreesAreNotEqualTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ExpressionComparer.Equals(L(i => i.Id), L(i => i.Name.Length)), Is.False, "member");
            Assert.That(ExpressionComparer.Equals(L(i => i.Id + 1), L(i => i.Id + 2)), Is.False, "constant");
            Assert.That(ExpressionComparer.Equals(L(i => i.Name.StartsWith("a")), L(i => i.Name.EndsWith("a"))), Is.False, "method");
            Assert.That(ExpressionComparer.Equals(L(i => i.Id + 1), L(i => i.Id - 1)), Is.False, "operator");
            Assert.That(ExpressionComparer.Equals(L(i => i.Id), null), Is.False);
        });
    }
}
