using Glazor.Misc;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace Glazor.Tests;

/// <summary>The helpers of <see cref="Utils"/> and <see cref="Extensions"/> that projects depending on Glazor call.</summary>
public class UtilsTests
{
    private enum Fruit
    {
        [Description("Green apple")] Apple,
        Pear,
    }

    [Test]
    public void IsNumberTellsTheNumericTypesTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Utils.IsNumber(3), Is.True);
            Assert.That(Utils.IsNumber(3.5m), Is.True);
            Assert.That(Utils.IsNumber((Half)1), Is.True);
            Assert.That(Utils.IsNumber("3"), Is.False);
            Assert.That(Utils.IsNumber((object?)null), Is.False);
            Assert.That(Utils.IsNumber(Fruit.Pear), Is.False, "an enum is not a number");
            Assert.That(Utils.IsNumber(typeof(long)), Is.True);
            Assert.That(Utils.IsNumber(typeof(int?)), Is.True, "as IsNumber is for a value of it");
            Assert.That(Utils.IsNumber(typeof(DateTime?)), Is.False);
            Assert.That(Utils.IsNumber(typeof(string)), Is.False);
            Assert.That(Utils.IsNumber(typeof(DateTime)), Is.False);
        });
    }

    [Test]
    public void IdentityIndexOfFindsTheSameInstanceTest()
    {
        string a = new('x', 2), b = new('x', 2);   // equal, not the same instance
        List<string> list = [b, a];
        Assert.Multiple(() =>
        {
            Assert.That(Utils.IdentityIndexOf(list, a), Is.EqualTo(1));
            Assert.That(Utils.IdentityIndexOf(list, "zz"), Is.EqualTo(-1));
        });
    }

    [Test]
    public void ByteArraysEqualComparesTheContentTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Utils.ByteArraysEqual([1, 2, 3], new byte[] { 1, 2, 3 }), Is.True);
            Assert.That(Utils.ByteArraysEqual([1, 2], [1, 2, 3]), Is.False);
            Assert.That(Utils.ByteArraysEqual([], []), Is.True);
        });
    }

    [Test]
    public void GetDescriptionReadsTheDescriptionAttributeTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Fruit.Apple.GetDescription(), Is.EqualTo("Green apple"));
            Assert.That(Fruit.Pear.GetDescription(), Is.EqualTo("Pear"), "the name, without one");
            Assert.That(((Fruit)7).GetDescription(), Is.EqualTo("7"), "a value with no member");
        });
    }

    /// <summary>
    /// A VersionedList bumps its Version on every insert and remove; an indexer write counts only when updates are tracked and
    /// the value really changes, by Equals or by the comparer it was given.
    /// </summary>
    [Test]
    public void VersionedListCountsTheChangesItTracksTest()
    {
        VersionedList<string> plain = new(["a", "b"], trackUpdates: false);
        plain.Add("c");
        plain.Insert(0, "z");
        plain.RemoveAt(0);
        plain.Remove("c");
        int afterInsertsAndRemoves = plain.Version;
        plain[0] = "x";
        VersionedList<string> tracked = new(["a", "b"], trackUpdates: true);
        tracked[0] = "a";
        int afterSameValue = tracked.Version;
        tracked[0] = "x";
        VersionedList<string> caseless = new(["a"], StringComparer.OrdinalIgnoreCase);
        caseless[0] = "A";
        int afterEqualByComparer = caseless.Version;
        caseless[0] = "b";
        Assert.Multiple(() =>
        {
            Assert.That(afterInsertsAndRemoves, Is.EqualTo(4));
            Assert.That(plain.Version, Is.EqualTo(4), "an indexer write, not tracked");
            Assert.That(plain, Is.EqualTo(new[] { "x", "b" }));
            Assert.That(afterSameValue, Is.EqualTo(0), "the same value");
            Assert.That(tracked.Version, Is.EqualTo(1));
            Assert.That(afterEqualByComparer, Is.EqualTo(0), "equal by the comparer");
            Assert.That(caseless.Version, Is.EqualTo(1));
        });
    }

    /// <summary>A VersionedList is read-only when the list it wraps is: over an array, Add and Insert throw.</summary>
    [Test]
    public void VersionedListIsReadOnlyAsItsListTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(((ICollection<string>)new VersionedList<string>(new[] { "a" }, trackUpdates: false)).IsReadOnly, Is.True, "an array");
            Assert.That(((ICollection<string>)new VersionedList<string>(new List<string> { "a" }, trackUpdates: false)).IsReadOnly, Is.False, "a List");
        });
    }

    /// <summary>A combined comparer asks the next comparer only on a tie, in order.</summary>
    [Test]
    public void ComboAsksTheNextComparerOnlyOnATieTest()
    {
        (string Name, int Age, int Id)[] people = [("b", 30, 1), ("a", 40, 2), ("a", 30, 3), ("a", 30, 0)];
        IComparer<(string Name, int Age, int Id)> byName = Comparer<(string Name, int Age, int Id)>.Create((x, y) => string.CompareOrdinal(x.Name, y.Name));
        IComparer<(string Name, int Age, int Id)> byAge = Comparer<(string Name, int Age, int Id)>.Create((x, y) => x.Age.CompareTo(y.Age));
        IComparer<(string Name, int Age, int Id)> byId = Comparer<(string Name, int Age, int Id)>.Create((x, y) => x.Id.CompareTo(y.Id));
        Assert.Multiple(() =>
        {
            Assert.That(people.Order(Comparers.Combo(byName, byAge)).Select(p => p.Id), Is.EqualTo(new[] { 3, 0, 2, 1 }), "stable on the full tie");
            Assert.That(people.Order(Comparers.Combo(byName, byAge, byId)).Select(p => p.Id), Is.EqualTo(new[] { 0, 3, 2, 1 }));
        });
    }

    /// <summary>A combined comparer reads its further comparers once: a sequence that gives them only once still counts.</summary>
    [Test]
    public void ComboReadsItsFurtherComparersOnceTest()
    {
        static IEnumerable<T> Draining<T>(Queue<T> queue)
        {
            while (queue.Count > 0)
                yield return queue.Dequeue();
        }
        var byA = Comparer<(int A, int B, int C)>.Create((x, y) => x.A.CompareTo(y.A));
        var byB = Comparer<(int A, int B, int C)>.Create((x, y) => x.B.CompareTo(y.B));
        var byC = Comparer<(int A, int B, int C)>.Create((x, y) => x.C.CompareTo(y.C));
        var eqA = EqualityComparer<(int A, int B, int C)>.Create((x, y) => x.A == y.A, x => x.A);
        var eqB = EqualityComparer<(int A, int B, int C)>.Create((x, y) => x.B == y.B, x => x.B);
        var eqC = EqualityComparer<(int A, int B, int C)>.Create((x, y) => x.C == y.C, x => x.C);
        Assert.Multiple(() =>
        {
            Assert.That(Comparers.Combo(byA, byB, Draining(new Queue<IComparer<(int A, int B, int C)>>([byC]))).Compare((1, 1, 1), (1, 1, 2)), Is.LessThan(0));
            Assert.That(EqComparers.Combo(eqA, eqB, Draining(new Queue<IEqualityComparer<(int A, int B, int C)>>([eqC]))).Equals((1, 1, 1), (1, 1, 2)), Is.False);
        });
    }
}
