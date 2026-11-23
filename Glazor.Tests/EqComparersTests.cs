using Glazor.Misc;

namespace Glazor.Tests;

/// <summary>
/// <see cref="EqComparers"/> exists because a collection member does not compare by content in C#, so these
/// comparers are what every content-based Equals in the library is built on. Two properties matter and are
/// easy to lose: a comparer must agree with its own GetHashCode, and one written for an unordered member must
/// not let the enumeration order leak into the answer — the order a HashSet or a Dictionary hands its
/// elements over follows its insertion and removal history, not its content.
/// </summary>
public class EqComparersTests
{
    /// <summary>
    /// Same content, different enumeration order: a Dictionary reuses the slot freed by a removal, so
    /// add/remove/add does not enumerate in the same order as adding the survivors outright.
    /// </summary>
    private static Dictionary<string, int> Shuffled()
    {
        Dictionary<string, int> d = new() { ["a"] = 1, ["zz"] = 99, ["b"] = 2 };
        d.Remove("zz");
        d["c"] = 3;
        return d;
    }

    private static Dictionary<string, int> Straight() => new() { ["a"] = 1, ["b"] = 2, ["c"] = 3 };

    // ───────────────────────────────── ForSet ─────────────────────────────────

    [Test]
    public void ForSetIgnoresTheOrderTest()
    {
        var cmp = EqComparers.ForSet<string>();

        Assert.Multiple(() =>
        {
            Assert.That(cmp.Equals(["a", "b"], ["b", "a"]), Is.True);
            Assert.That(cmp.GetHashCode(["a", "b"]), Is.EqualTo(cmp.GetHashCode(["b", "a"])),
                "a comparer has to agree with its own hash");
        });
    }

    /// <summary>
    /// A set is defined by its membership, so a repeated element adds nothing. Both directions matter: a
    /// count-then-difference comparison gets the first case wrong and the second one right, and a hash folded
    /// over the raw enumerable rather than the distinct elements disagrees with either.
    /// </summary>
    [Test]
    public void ForSetIgnoresRepetitionsTest()
    {
        var cmp = EqComparers.ForSet<string>();

        Assert.Multiple(() =>
        {
            Assert.That(cmp.Equals(["a", "a"], ["a"]), Is.True, "the same set, written twice over");
            Assert.That(cmp.GetHashCode(["a", "a"]), Is.EqualTo(cmp.GetHashCode(["a"])),
                "and it must hash as that set");
            Assert.That(cmp.Equals(["a", "a"], ["a", "b"]), Is.False,
                "two different sets that happen to hold the same number of elements");
        });
    }

    [Test]
    public void ForSetStillSeesARealDifferenceTest()
    {
        var cmp = EqComparers.ForSet<string>();

        Assert.Multiple(() =>
        {
            Assert.That(cmp.Equals(["a", "b"], ["a"]), Is.False, "a missing element");
            Assert.That(cmp.Equals(["a"], ["a", "b"]), Is.False, "an extra element");
            Assert.That(cmp.Equals(null, null), Is.True, "both absent");
            Assert.That(cmp.Equals(null, []), Is.False, "absent is not empty");
            Assert.That(cmp.GetHashCode(null!), Is.EqualTo(0), "and absent hashes to zero");
        });
    }

    /// <summary>
    /// A null is an ordinary member of a set, and the hash fold has to stay consistent with that.
    /// </summary>
    [Test]
    public void ForSetTreatsNullAsAMemberTest()
    {
        var cmp = EqComparers.ForSet<string?>();
        string?[] one = { null }, other = { null };

        Assert.Multiple(() =>
        {
            Assert.That(cmp.Equals([null], [null]), Is.True);
            Assert.That(cmp.GetHashCode(one), Is.EqualTo(cmp.GetHashCode(other)));
            Assert.That(cmp.Equals(["a", null], ["a"]), Is.False);
        });
    }

    [Test]
    public void ForSetHonoursAnItemComparerTest()
    {
        var cmp = EqComparers.ForSet<string>(StringComparer.OrdinalIgnoreCase);

        Assert.Multiple(() =>
        {
            Assert.That(cmp.Equals(["A"], ["a"]), Is.True);
            Assert.That(cmp.GetHashCode(["A"]), Is.EqualTo(cmp.GetHashCode(["a"])));
        });
    }

    /// <summary>
    /// The members handed to this comparer are not always materialised — EnumDef projects its selection with
    /// a lazy Select — so the comparer must not depend on being able to enumerate only once.
    /// </summary>
    [Test]
    public void ForSetHandlesALazyEnumerableTest()
    {
        var cmp = EqComparers.ForSet<string>();
        IEnumerable<string> lazy = new[] { "a", "b" }.Select(s => s);

        Assert.Multiple(() =>
        {
            Assert.That(cmp.Equals(lazy, ["b", "a"]), Is.True);
            Assert.That(cmp.GetHashCode(lazy), Is.EqualTo(cmp.GetHashCode(["b", "a"])));
        });
    }

    // ─────────────────────────────── ForDictionary ───────────────────────────────

    /// <summary>
    /// The position of the entries is not part of a dictionary's value, so it must not reach the hash either.
    /// </summary>
    [Test]
    public void ForDictionaryIgnoresTheEntryOrderTest()
    {
        var cmp = EqComparers.ForDictionary<string, int>();
        Dictionary<string, int> shuffled = Shuffled();
        Dictionary<string, int> straight = Straight();

        Assert.That(shuffled.Keys, Is.Not.EqualTo(straight.Keys).AsCollection,
            "the premise: the two enumerate in a different order");
        Assert.Multiple(() =>
        {
            Assert.That(cmp.Equals(shuffled, straight), Is.True, "same entries, so the same dictionary");
            Assert.That(cmp.GetHashCode(shuffled), Is.EqualTo(cmp.GetHashCode(straight)),
                "and it has to hash alike whatever order it enumerates in");
        });
    }

    [Test]
    public void ForDictionaryStillSeesARealDifferenceTest()
    {
        var cmp = EqComparers.ForDictionary<string, int>();
        Dictionary<string, int> baseline = Straight();

        Assert.Multiple(() =>
        {
            Assert.That(cmp.Equals(baseline, new Dictionary<string, int> { ["a"] = 1, ["b"] = 2, ["c"] = 4 }), Is.False, "a changed value");
            Assert.That(cmp.Equals(baseline, new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }), Is.False, "a missing entry");
            Assert.That(cmp.Equals(baseline, new Dictionary<string, int> { ["a"] = 1, ["b"] = 2, ["c"] = 3, ["d"] = 4 }), Is.False, "an extra entry");
            Assert.That(cmp.Equals(baseline, new Dictionary<string, int> { ["a"] = 1, ["b"] = 2, ["z"] = 3 }), Is.False, "a renamed key");
            Assert.That(cmp.Equals(null, null), Is.True);
            Assert.That(cmp.Equals(null, baseline), Is.False);
        });
    }

    /// <summary>
    /// An equality is symmetric, also with an entry comparer that takes two entries of one dictionary as the same, here keys that
    /// differ only in case.
    /// </summary>
    [Test]
    public void ForDictionaryIsSymmetricTest()
    {
        var cmp = EqComparers.ForDictionary(EqComparers.KeyValuePair<string, int>(StringComparer.OrdinalIgnoreCase, EqualityComparer<int>.Default));
        Dictionary<string, int> twoCases = new() { ["a"] = 1, ["A"] = 1 };
        Dictionary<string, int> twoKeys = new() { ["a"] = 1, ["b"] = 1 };
        Assert.Multiple(() =>
        {
            Assert.That(cmp.Equals(twoKeys, twoCases), Is.False, "b has no match");
            Assert.That(cmp.Equals(twoCases, twoKeys), Is.EqualTo(cmp.Equals(twoKeys, twoCases)), "the other way round");
        });
    }

    /// <summary>
    /// Two dictionaries that compare equal hash alike, also when each holds two entries the entry comparer takes as the same.
    /// </summary>
    [Test]
    public void ForDictionaryHashesEqualDictionariesAlikeTest()
    {
        var cmp = EqComparers.ForDictionary(EqComparers.KeyValuePair<string, int>(StringComparer.OrdinalIgnoreCase, EqualityComparer<int>.Default));
        Dictionary<string, int> x = new() { ["a"] = 1, ["A"] = 1, ["b"] = 2 };
        Dictionary<string, int> y = new() { ["a"] = 1, ["b"] = 2, ["B"] = 2 };
        Assert.That(cmp.Equals(x, y), Is.True, "the premise: equal under the entry comparer");
        Assert.That(cmp.GetHashCode(x), Is.EqualTo(cmp.GetHashCode(y)));
    }

    // ──────────────────── ForEnumerable, ForList, ForHashSet, ForSortedSet ────────────────────

    [Test]
    public void ForEnumerableAndForListFollowTheOrderTest()
    {
        var seq = EqComparers.ForEnumerable<string>();
        var list = EqComparers.ForList<string>();

        Assert.Multiple(() =>
        {
            Assert.That(seq.Equals(["a", "b"], new List<string> { "a", "b" }), Is.True, "any enumerable with the same items");
            Assert.That(seq.GetHashCode(["a", "b"]), Is.EqualTo(seq.GetHashCode(new List<string> { "a", "b" })));
            Assert.That(seq.Equals(["a", "b"], ["b", "a"]), Is.False, "the order counts");
            Assert.That(seq.Equals(null, null), Is.True);
            Assert.That(seq.Equals(["a"], null), Is.False);
            Assert.That(list.Equals(["a", "b"], ["a", "b"]), Is.True);
            Assert.That(list.Equals(["a", "b"], ["a", "b", "c"]), Is.False);
        });
    }

    [Test]
    public void ForEnumerableHonoursAnItemComparerTest()
    {
        var cmp = EqComparers.ForEnumerable(StringComparer.OrdinalIgnoreCase);

        Assert.Multiple(() =>
        {
            Assert.That(cmp.Equals(["a", "B"], ["A", "b"]), Is.True);
            Assert.That(cmp.GetHashCode(["a", "B"]), Is.EqualTo(cmp.GetHashCode(["A", "b"])),
                "a comparer has to agree with its own hash");
        });
    }

    [Test]
    public void ForHashSetAndForSortedSetCompareTheContentTest()
    {
        var hashSets = EqComparers.ForHashSet<string>();
        var sortedSets = EqComparers.ForSortedSet<string>();

        Assert.Multiple(() =>
        {
            Assert.That(hashSets.Equals(["a", "b"], ["b", "a"]), Is.True);
            Assert.That(hashSets.GetHashCode(["a", "b"]), Is.EqualTo(hashSets.GetHashCode(["b", "a"])));
            Assert.That(hashSets.Equals(["a", "b"], ["a", "c"]), Is.False);
            Assert.That(sortedSets.Equals(["b", "a"], ["a", "b"]), Is.True);
            Assert.That(sortedSets.GetHashCode(["b", "a"]), Is.EqualTo(sortedSets.GetHashCode(["a", "b"])));
            Assert.That(sortedSets.Equals(["a", "b"], ["a"]), Is.False);
        });
    }
}
