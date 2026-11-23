using Glazor.Grds.Props;
using System.Linq.Expressions;

namespace Glazor.Tests;

/// <summary>
/// <see cref="ExprUtils.PropagateNull{TDelegate}"/> rewrites member-access chains so a null in the middle
/// yields null instead of throwing. Every in-memory column getter goes through it, so the whole grid depends
/// on this rewrite being both applied and semantics-preserving.
/// </summary>
public class ExprUtilsTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void PropagateNullTest()
    {
        Expression<Func<Root, string?>> expr = r => r.Child!.Name;

        // Without the rewrite a null link throws — this is the reason the visitor exists.
        Assert.That(() => expr.Compile().Invoke(new Root(null)), Throws.TypeOf<NullReferenceException>());

        var safe = ExprUtils.PropagateNull(expr).Compile();
        Assert.That(safe.Invoke(new Root(new Child("Vallers", 17))), Is.EqualTo("Vallers"));
        Assert.That(safe.Invoke(new Root(null)), Is.Null);
    }

    [Test]
    public void PropagateNullOnDeepChainTest()
    {
        Expression<Func<Root, string?>> expr = r => r.Child!.Inner!.Label;
        var safe = ExprUtils.PropagateNull(expr).Compile();

        Assert.That(safe.Invoke(new Root(new Child("Vallers", 17, new Inner("deep")))), Is.EqualTo("deep"));
        // A null at either link, not just the first, must yield null rather than throw.
        Assert.That(safe.Invoke(new Root(new Child("Vallers", 17))), Is.Null);
        Assert.That(safe.Invoke(new Root(null)), Is.Null);
    }

    /// <summary>The arguments of a method called on a member are guarded too, not only the member the call is made on.</summary>
    [Test]
    public void PropagateNullInMethodArgumentsTest()
    {
        Expression<Func<Root, string?>> expr = r => r.Child!.Name.Replace("a", r.Child.Inner!.Label);
        var safe = ExprUtils.PropagateNull(expr).Compile();

        Assert.That(safe.Invoke(new Root(new Child("Vallers", 17, new Inner("e")))), Is.EqualTo("Vellers"));
        Assert.That(safe.Invoke(new Root(new Child("Vallers", 17))), Is.EqualTo("Vllers"), "a null argument reaches the call as null");
        Assert.That(safe.Invoke(new Root(null)), Is.Null);
    }

    /// <summary>A call whose arguments are guarded keeps those guards when a member or a call is read on its result.</summary>
    [Test]
    public void PropagateNullUnderAGuardedCallTest()
    {
        Expression<Func<Root, string?>> expr = r => r.Child!.Name.Replace("a", r.Child.Inner!.Label).ToUpper();
        var safe = ExprUtils.PropagateNull(expr).Compile();

        Assert.That(safe.Invoke(new Root(new Child("Vallers", 17))), Is.EqualTo("VLLERS"), "the guarded argument, under ToUpper");
        Assert.That(safe.Invoke(new Root(null)), Is.Null);
    }

    /// <summary>
    /// A member whose own type is a non-nullable value type cannot be guarded — the rewrite would have to
    /// change the node's type, which it cannot do. Pinning the real boundary here, because the two forms look
    /// interchangeable at the call site and only one of them survives a null link.
    /// </summary>
    [Test]
    public void PropagateNullCannotGuardNonNullableValueTypeMemberTest()
    {
        // Casting the *result* does not help: the cast sits outside the member access, so the access itself is
        // still unguarded. NB this is the form the XML doc on PropagateNull currently suggests.
        Expression<Func<Root, int?>> outerCast = r => (int?)r.Child!.Age;
        var viaOuterCast = ExprUtils.PropagateNull(outerCast).Compile();

        Assert.That(viaOuterCast.Invoke(new Root(new Child("Vallers", 17))), Is.EqualTo(17));
        Assert.That(() => viaOuterCast.Invoke(new Root(null)), Throws.TypeOf<NullReferenceException>(),
            "an int member cannot be guarded, so a null link still throws");

        // What does work: a member that is already nullable, so the rewrite has a null to hand back.
        Expression<Func<Root, int?>> nullableMember = r => r.Child!.NullableAge;
        var viaNullableMember = ExprUtils.PropagateNull(nullableMember).Compile();

        Assert.That(viaNullableMember.Invoke(new Root(new Child("Vallers", 17))), Is.EqualTo(17));
        Assert.That(viaNullableMember.Invoke(new Root(null)), Is.Null);

        // ...and so does writing the guard by hand, which is the fallback when the member cannot be changed.
        Expression<Func<Root, int?>> handGuarded = r => r.Child == null ? null : (int?)r.Child.Age;
        var viaHandGuard = ExprUtils.PropagateNull(handGuarded).Compile();

        Assert.That(viaHandGuard.Invoke(new Root(new Child("Vallers", 17))), Is.EqualTo(17));
        Assert.That(viaHandGuard.Invoke(new Root(null)), Is.Null);
    }

    [Test]
    public void IsNullableTest()
    {
        // Reference and interface types are nullable...
        Assert.That(ExprUtils.IsNullable(typeof(string)), Is.True);
        Assert.That(ExprUtils.IsNullable(typeof(Root)), Is.True);
        Assert.That(ExprUtils.IsNullable(typeof(IComparable)), Is.True);
        // ...so are Nullable<T> structs...
        Assert.That(ExprUtils.IsNullable(typeof(int?)), Is.True);
        Assert.That(ExprUtils.IsNullable(typeof(DateOnly?)), Is.True);
        // ...but bare value types are not.
        Assert.That(ExprUtils.IsNullable(typeof(int)), Is.False);
        Assert.That(ExprUtils.IsNullable(typeof(DateOnly)), Is.False);

        // IsNullableStructType is the narrower question: only Nullable<T> qualifies.
        Assert.That(ExprUtils.IsNullableStructType(typeof(int?)), Is.True);
        Assert.That(ExprUtils.IsNullableStructType(typeof(int)), Is.False);
        Assert.That(ExprUtils.IsNullableStructType(typeof(string)), Is.False);
    }

    /// <summary>
    /// <see cref="ExprUtils.NullableExpression"/> lifts whichever side is not already nullable, so the two
    /// operands are comparable. Getting this wrong is what breaks EF Core's ability to bind the comparison
    /// to a column, so the lifted type matters as much as the result.
    /// </summary>
    [Test]
    public void NullableExpressionLiftsOneSideTest()
    {
        var nullableSide = Expression.Constant(17, typeof(int?));
        var plainSide = Expression.Constant(17, typeof(int));

        var lifted = ExprUtils.NullableExpression(nullableSide, plainSide, Expression.Equal);
        Assert.That(lifted.Right.Type, Is.EqualTo(typeof(int?)), "the plain side should have been lifted");
        Assert.That(Expression.Lambda<Func<bool>>(lifted).Compile().Invoke(), Is.True);

        // ...and symmetrically.
        var lifted2 = ExprUtils.NullableExpression(plainSide, nullableSide, Expression.Equal);
        Assert.That(lifted2.Left.Type, Is.EqualTo(typeof(int?)));
        Assert.That(Expression.Lambda<Func<bool>>(lifted2).Compile().Invoke(), Is.True);

        // Both already nullable: nothing to lift, and a null never equals a value.
        var nullConst = Expression.Constant(null, typeof(int?));
        var bothNullable = ExprUtils.NullableExpression(nullableSide, nullConst, Expression.Equal);
        Assert.That(Expression.Lambda<Func<bool>>(bothNullable).Compile().Invoke(), Is.False);
    }

    private record Inner(string Label);
    private record Child(string Name, int Age, Inner? Inner = null)
    {
        public int? NullableAge => Age;
    }
    private record Root(Child? Child);
}
